using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 鋼管杭の杭頭部 (コンクリート充填鋼管部) の充填長 l1 (外部レビュー #10 2026-11-20)。
    ///
    /// <para>杭頭部の検討式は、杭頭から L = 4D を充填部 l1・鋼管部 l3 = L − l1 に分けて合成剛性 EIeq を求める。
    /// 以前は l1 = D 固定で入力できなかった。いまは杭断面の「充填長 l1」で入れる。未入力 (0) は杭径 D で、
    /// 以前のファイルの計算は変わらない。杭体の区間長とは別の入力なので、違うときは解析前の注意に出す。</para>
    /// </summary>
    [TestClass]
    public class SteelPipeHeadLengthTests
    {
        private static PileSection Head(double fill = 0)
            => new() { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.CftSection, PipeDia = 800, PipeTs = 12, ConcreteFc = 24, CftFillLength = fill };

        // ── 諸元 ──

        [TestMethod]
        public void TheSpecsShowTheLengthAndWhereItCameFrom()
        {
            var head = Head();
            Assert.IsTrue(head.PileDiameter > 700, $"(前提) 杭径が鋼管外径から決まる: {head.PileDiameter}");
            head.SetSpecs();
            var assumed = head.SelectedPileSectionSpecification.SingleOrDefault(s => s.Item == "充填長 (仮定)");
            Assert.IsNotNull(assumed, "未入力なのに仮定であることを示していない");
            Assert.AreEqual($"{head.PileDiameter:N0}", assumed.Value, "未入力の充填長が杭径 D でない");
            StringAssert.Contains(assumed.Note, "杭径 D");

            head.CftFillLength = 1600;
            head.SetSpecs();
            var input = head.SelectedPileSectionSpecification.SingleOrDefault(s => s.Item == "充填長");
            Assert.IsNotNull(input, "入力した充填長を出していない");
            Assert.AreEqual("1,600", input.Value);
            Assert.IsFalse(head.SelectedPileSectionSpecification.Any(s => s.Item == "充填長 (仮定)"), "入力したのに仮定と出している");

            var middle = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.SteelPipeSection, PipeDia = 800, PipeTs = 12, CftFillLength = 1600 };
            middle.SetSpecs();
            Assert.IsFalse(middle.SelectedPileSectionSpecification.Any(s => s.Item.StartsWith("充填長")), "鋼管部にも出している");
        }

        // ── 検討式 ──

        [TestMethod]
        public void BlankIsTheDiameter_AndLongerFillIsStiffer()
        {
            double Eieq(double fill) => Head(fill).TryCreateSteelPipeSection()!.ComputeEIeqHead();
            var blank = Head();
            double d = blank.PileDiameter;

            Assert.AreEqual(Eieq(0), Eieq(d), Eieq(0) * 1e-12, "充填長 = D の入力が未入力と違う");
            Assert.IsTrue(Eieq(2 * d) > Eieq(d), "充填部を長くしても剛性が上がらない (充填部の方が鋼管部より硬い)");
            Assert.IsTrue(Eieq(0.5 * d) < Eieq(d), "充填部を短くしても剛性が下がらない");

            // 4D 以上は L 全体が充填部: EIeq = 杭断面の EI (合成 EI)
            double full = blank.EI * 1e9;
            Assert.AreEqual(full, Eieq(4 * d), full * 1e-9, "L 全体が充填部なのに合成 EI と違う");
            Assert.AreEqual(Eieq(4 * d), Eieq(10 * d), "4D を超えた分まで数えている");
        }

        [TestMethod]
        public void TheFillLengthReachesTheAnalysisCurve_AndTheCacheKey()
        {
            var a = Head();
            var b = Head(2400);
            Assert.AreNotEqual(a.GetMPhiCacheKey(1000), b.GetMPhiCacheKey(1000), "充填長が違うのに M-φ のキャッシュの鍵が同じ (古い曲線が返る)");

            // 解析に入る M-φ (杭頭部の検討式) の最初の折れ点の曲率 φMd = Md / EIeq が、充填長で変わる
            PileSection.ClearMphiCache();
            var (pa, _) = a.GetMPhiRelationship(1000);
            var (pb, _) = b.GetMPhiRelationship(1000);
            Assert.IsTrue(pa.Count >= 2 && pb.Count >= 2, "(前提) 曲線が作れる");
            Assert.IsTrue(pb[1] < pa[1], $"充填部を長くしたのに解析の M-φ が硬くならない: φ1 {pa[1]} → {pb[1]}");
        }

        // ── 解析前の注意 ──

        [TestMethod]
        public void ANoticeIsRaisedWhenTheSegmentLengthDiffers()
        {
            // segmentLength が null なら、杭体に入れた後の断面の杭径 D (杭体に入れると断面の諸元が初期化されることがある)
            static InputModel Model(double? segmentLength, double fill)
            {
                var section = Head(fill);
                var body = new PileBodyInput { PileBodyType = PileTypeNames.SteelPipe };
                var segment = new PileBodySegment { No = 1, PileSection = section };
                body.PileBodySegments = [segment];
                segment.SegmentLength = segmentLength ?? segment.PileSection.PileDiameter / 1000.0;
                segment.SegmentDepth = segment.SegmentLength;
                return new InputModel { PileBodies = [body] };
            }
            static string[] Notices(InputModel m) => CheckInputData.CollectPileBodyNotices(m).Select(n => n.Message).Where(s => s.Contains("充填長")).ToArray();

            var equal = Notices(Model(null, 0));
            Assert.AreEqual(0, equal.Length, "区間長 = D で未入力なのに注意を出している: " + string.Join(" / ", equal));

            var assumed = Notices(Model(2.0, 0));
            Assert.AreEqual(1, assumed.Length, "区間長と仮定の D が違うのに知らせていない");
            StringAssert.Contains(assumed[0], "未入力のため杭径 D と仮定");
            StringAssert.Contains(assumed[0], "2,000 mm");

            Assert.AreEqual(0, Notices(Model(2.0, 2000)).Length, "入力した充填長が区間長と同じなのに注意を出している");
            var differs = Notices(Model(2.0, 1500));
            Assert.AreEqual(1, differs.Length);
            Assert.IsFalse(differs[0].Contains("仮定"), "入力した値なのに仮定と書いている");
        }

        // ── 計算書 ──

        [TestMethod]
        public void TheReportStatesTheLengthUsed()
        {
            string source = TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs");
            StringAssert.Contains(source, "充填長 l1 を用いて求めた");
            StringAssert.Contains(source, "(杭径 D と仮定)");
            StringAssert.Contains(source, "引張軸力のときの曲げ耐力は、「基礎部材の強度と変形性能」の式と異なる");
        }

        // ── 保存ファイル ──

        /// <summary>入力した充填長は保存して読み戻せる。以前のファイル (項目が無い) は未入力 (杭径 D) として読む。</summary>
        [TestMethod]
        public void TheFillLengthSurvivesSaveAndLoad_AndOldFilesReadAsBlank()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var svc = new FileOperationService(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                });
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
                var body = new PileBodyInput { PileBodyType = PileTypeNames.SteelPipe };
                body.PileBodySegments = [new PileBodySegment { No = 1, SegmentLength = 1.6, SegmentDepth = 1.6, PileSection = Head(1600) }];
                input.PileBodies = [body];

                string file = Path.Combine(dir, "fill.pdj");
                svc.SaveProjectData(file, input, new PileDesign.FEM.AnaModel(), null);
                var loaded = svc.LoadProjectData(file).InputModel.PileBodies[0].PileBodySegments[0].PileSection;
                Assert.AreEqual(1600, loaded.CftFillLength, "入力した充填長が保存の往復で変わった");

                string text = FileOperationService.ReadSaveFileText(file);
                string old = Regex.Replace(text, "\\s*\"CftFillLength\":\\s*[0-9.eE+-]+,?", "");
                Assert.IsFalse(old.Contains("CftFillLength"), "(前提) 充填長の項目を消せていない");
                string oldFile = Path.Combine(dir, "old.pdj");
                File.WriteAllText(oldFile, old);
                var fromOld = svc.LoadProjectData(oldFile).InputModel.PileBodies[0].PileBodySegments[0].PileSection;
                Assert.AreEqual(0, fromOld.CftFillLength, "以前のファイルを未入力として読んでいない");
                Assert.AreEqual(fromOld.PileDiameter, fromOld.EffectiveCftFillLength, "未入力の充填長が杭径 D でない");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 後片付けの失敗は無視 */ }
            }
        }
    }
}
