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
    /// <para>l2 (充填部下端〜鋼管の断面変化部) も杭断面で入れる (2026-11-21)。0 は厚部なし。節点 (l1 + l2) と EIeq の l2 に渡す。</para>
    /// </summary>
    [TestClass]
    public class SteelPipeHeadLengthTests
    {
        private static PileSection Head(double fill = 0, double thick = 0)
            => new() { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.CftSection, PipeDia = 800, PipeTs = 12, ConcreteFc = 24, CftFillLength = fill, CftThickPartLength = thick };

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
            Assert.IsFalse(middle.SelectedPileSectionSpecification.Any(s => s.Item == "充填部下端〜鋼管の断面変化部"), "鋼管部にも l2 を出している");

            // l2 (充填部下端〜鋼管の断面変化部) (2026-11-21)。0 は厚部なし
            var thick = head.SelectedPileSectionSpecification.SingleOrDefault(s => s.Item == "充填部下端〜鋼管の断面変化部");
            Assert.IsNotNull(thick, "l2 (充填部下端〜鋼管の断面変化部) を出していない");
            Assert.AreEqual("0", thick.Value);
            StringAssert.Contains(thick.Note, "充填部の下端で断面が変わる");
            head.CftThickPartLength = 800;
            head.SetSpecs();
            Assert.AreEqual("800", head.SelectedPileSectionSpecification.Single(s => s.Item == "充填部下端〜鋼管の断面変化部").Value);
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

        /// <summary>
        /// l2 (充填部下端〜鋼管の断面変化部) と鋼管薄部の剛性 EI3 (2026-11-21)。厚部 EI2 は杭頭部の鋼管、薄部 EI3 は杭体の下の区間の鋼管。
        /// 薄部が杭頭部の鋼管より柔らかければ、l2 を長くする (薄部を厚部に置き換える) と EIeq は硬くなる。
        /// 下の区間が無い (EI3 = 0 → 杭頭部の鋼管と同じ) ときは、l2 は EIeq を変えない。
        /// </summary>
        [TestMethod]
        public void TheThickPartLengthAndTheThinPipeEIReachTheFormula()
        {
            double d = Head().PileDiameter;
            double thinEI = Pipe(9).EI;   // kNm²
            double Eieq(double fill, double thick, double ei3)
            {
                var head = Head(fill, thick);
                head.HeadThinPipeEI = ei3;
                return head.TryCreateSteelPipeSection()!.ComputeEIeqHead();
            }

            Assert.AreEqual(d, Head(0, d).TryCreateSteelPipeSection()!.HeadThickPartLength, "l2 の入力が検討式に渡っていない");
            Assert.AreEqual(0.0, Head(0, 0).TryCreateSteelPipeSection()!.HeadThickPartLength);

            // 下の区間が無い: EI2 = EI3 なので l2 は効かない (以前と同じ値)
            Assert.AreEqual(Eieq(1600, 0, 0), Eieq(1600, d, 0), Eieq(1600, 0, 0) * 1e-12, "EI2 = EI3 なのに l2 で EIeq が変わった");

            // 薄い下の区間: EI3 が効き、EIeq は下がる。l2 を長くすると硬くなる
            Assert.IsTrue(Eieq(1600, 0, thinEI) < Eieq(1600, 0, 0), "薄い鋼管の EI3 を渡しても EIeq が下がらない");
            Assert.IsTrue(Eieq(1600, d, thinEI) > Eieq(1600, 0, thinEI), "厚部を長くしても EIeq が上がらない");
            Assert.AreEqual(Eieq(1600, 10 * d, thinEI), Eieq(1600, 10 * d, 0), Eieq(1600, 0, 0) * 1e-12, "L を超える l2 で薄部が残っている");
            Assert.AreNotEqual(Head().GetMPhiCacheKey(1000), WithEI3(Head(), thinEI).GetMPhiCacheKey(1000), "EI3 が違うのに M-φ のキャッシュの鍵が同じ");

            // 中間部の断面には渡さない
            var middle = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.SteelPipeSection, PipeDia = 800, PipeTs = 12, CftThickPartLength = 800, HeadThinPipeEI = thinEI };
            Assert.AreEqual(0.0, middle.TryCreateSteelPipeSection()!.HeadThickPartLength, "鋼管部の断面に l2 を渡している");
            Assert.IsNull(middle.TryCreateSteelPipeSection()!.HeadThinPipeEI, "鋼管部の断面に EI3 を渡している");
        }

        private static PileSection Pipe(double ts)
            => new() { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.SteelPipeSection, PipeDia = 800, PipeTs = ts };

        private static PileSection WithEI3(PileSection s, double ei3) { s.HeadThinPipeEI = ei3; return s; }

        /// <summary>
        /// EI3 は、杭頭から断面変化部 l1 + l2 のすぐ下にある鋼管部の区間から取る。充填鋼管部の区間の中なら、その下の鋼管部。
        /// 鋼管部の区間が無ければ 0 (杭頭部の鋼管と同じ)。
        /// </summary>
        [TestMethod]
        public void TheThinPipeEIComesFromTheSegmentBelowTheSectionChange()
        {
            static double EIOf(PileSection pipe) { var s = pipe.TryCreateSteelPipeSection()!; return s.E * s.Iisteel / 1e9; }
            var head = Head(1600);
            var thick = Pipe(16);
            var thin = Pipe(9);
            var segments = new System.Collections.Generic.List<PileBodySegment>
            {
                new() { No = 1, SegmentLength = 1.6, PileSection = head },
                new() { No = 2, SegmentLength = 1.0, PileSection = thick },
                new() { No = 3, SegmentLength = 20.0, PileSection = thin },
            };

            InputModel.ApplyHeadThinPipeEI(segments);
            Assert.AreEqual(EIOf(thick), head.HeadThinPipeEI, 1e-6, "l2 = 0 なのに充填部のすぐ下の区間から取っていない");

            head.CftThickPartLength = 1200;   // 断面変化部は 2.8 m → 3 番目の区間
            InputModel.ApplyHeadThinPipeEI(segments);
            Assert.AreEqual(EIOf(thin), head.HeadThinPipeEI, 1e-6, "断面変化部より下の区間から取っていない");

            head.CftThickPartLength = 0;
            head.CftFillLength = 1000;        // 断面変化部 1.0 m はまだ充填鋼管部の区間の中 → その下の鋼管部
            InputModel.ApplyHeadThinPipeEI(segments);
            Assert.AreEqual(EIOf(thick), head.HeadThinPipeEI, 1e-6, "充填鋼管部の区間の中なのに、その下の鋼管部から取っていない");

            InputModel.ApplyHeadThinPipeEI([segments[0]]);
            Assert.AreEqual(0.0, head.HeadThinPipeEI, "下に鋼管部が無いのに値が残った");
        }

        /// <summary>EI3 は杭体から配る値なので、配る場所 (解析の入口・読込・例題の読込・土層-杭セットの作り直し) から外れていないこと。</summary>
        [TestMethod]
        public void TheThinPipeEIIsSyncedAtEveryEntry()
        {
            StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "Services", "CheckInputData.cs"),
                "public static bool ValidateForAnalysis("), "SyncSteelPipeHeadThinPipeEI()", "解析の入口で配っていない");
            StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol("), "SyncSteelPipeHeadThinPipeEI()", "読込で配っていない");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Services", "PileExampleLoader.cs"), "SyncSteelPipeHeadThinPipeEI()", "例題の読込で配っていない");
            StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "Models", "InputData", "InputModel.cs"),
                "public void GenerateSoilPiles("), "SyncSteelPipeHeadThinPipeEI()", "土層-杭セットの作り直しで配っていない");
        }

        [TestMethod]
        public void TheFillLengthReachesTheAnalysisCurve_AndTheCacheKey()
        {
            var a = Head();
            var b = Head(2400);
            Assert.AreNotEqual(a.GetMPhiCacheKey(1000), b.GetMPhiCacheKey(1000), "充填長が違うのに M-φ のキャッシュの鍵が同じ (古い曲線が返る)");
            Assert.AreNotEqual(a.GetMPhiCacheKey(1000), Head(0, 800).GetMPhiCacheKey(1000), "l2 が違うのに M-φ のキャッシュの鍵が同じ");

            // 解析に入る M-φ (杭頭部の検討式) の最初の折れ点の曲率 φMd = Md / EIeq が、充填長で変わる
            PileSection.ClearMphiCache();
            var (pa, _) = a.GetMPhiRelationship(1000);
            var (pb, _) = b.GetMPhiRelationship(1000);
            Assert.IsTrue(pa.Count >= 2 && pb.Count >= 2, "(前提) 曲線が作れる");
            Assert.IsTrue(pb[1] < pa[1], $"充填部を長くしたのに解析の M-φ が硬くならない: φ1 {pa[1]} → {pb[1]}");
        }

        // ── 要素分割の節点 ──

        /// <summary>
        /// 鋼管杭の土層-杭セットは、杭頭 (パイルキャップ下面) から充填部の下端 l1・鋼管の断面変化部 l1 + l2・4D に
        /// 節点を持つ (利用者の要望 2026-11-20)。l1 は充填長の入力 (未入力なら D)、l2 は 0 なので断面変化部は l1 と重なる。
        /// 解析と同じ組み立て (GenerateSoilPiles → SoilPileNodeAltitudes) を通して、設計例集3.5 (鋼管杭) で確かめる。
        /// </summary>
        [TestMethod]
        public void NodesArePlacedAtTheHeadZoneBoundaries()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (input == null) throw new AssertInconclusiveException("例題ファイルなし: " + err);
            var pile = input.PileLayoutItems[0];
            var head = input.PileBodyAt(pile.PileBodyNo)!.PileBodySegments
                .Select(s => s.PileSection).First(s => s.PileSectionType == PileTypeNames.CftSection);
            double d = head.PileDiameter / 1000.0;

            double[] Depths()
            {
                input.GenerateSoilPiles();
                double top = pile.PileHeadZ;
                return pile.SoilPileAt(input)!.ZDataItems.Select(z => top - z.Z).ToArray();
            }
            static void Has(double[] depths, double depth, string what)
                => Assert.IsTrue(depths.Any(x => Math.Abs(x - depth) < 1e-6),
                    $"{what} ({depth:F3} m) に節点がありません: {string.Join(", ", depths.Select(x => x.ToString("F3")))}");

            var blank = Depths();
            Has(blank, d, "充填部の下端 (未入力 = D)");

            // 鋼管薄部の EI3 は、杭体の断面と土層-杭セットの複写の両方に入っている (解析は複写を読む)
            Assert.IsTrue(head.HeadThinPipeEI > 0, "(前提) 設計例集3.5 の杭体には充填鋼管部の下に鋼管部がある");
            var copyHead = pile.SoilPileAt(input)!.PileBodySegments.Select(s => s.PileSection).First(s => s.PileSectionType == PileTypeNames.CftSection);
            Assert.AreNotSame(head, copyHead, "(前提) 土層-杭セットは断面を複写して持つ");
            Assert.AreEqual(head.HeadThinPipeEI, copyHead.HeadThinPipeEI, "土層-杭セットの複写に EI3 が入っていない");
            Has(blank, 4 * d, "検討区間の下端 4D");

            head.CftFillLength = 1600;
            var filled = Depths();
            Has(filled, 1.6, "充填部の下端 (入力 1,600 mm) = 断面変化部 (l2 = 0)");
            Has(filled, 4 * d, "検討区間の下端 4D");

            // l2 (充填部下端〜鋼管の断面変化部) を入れると、断面変化部 l1 + l2 にも節点が入る (2026-11-21)
            head.CftThickPartLength = 400;
            var thick = Depths();
            Has(thick, 1.6, "充填部の下端 l1");
            Has(thick, 2.0, "鋼管の断面変化部 l1 + l2");
            Has(thick, 4 * d, "検討区間の下端 4D");
        }

        [TestMethod]
        public void TheNodeDepthsComeFromTheSameDefinitionAsTheFormula()
        {
            var (l1, change, zone) = SteelPipeSection.HeadZoneDepths(800, 0, 0);
            Assert.AreEqual((800.0, 800.0, 3200.0), (l1, change, zone), "未入力: l1 = D、断面変化部 = l1 (l2 = 0)、L = 4D");
            Assert.AreEqual((1500.0, 1500.0, 3200.0), SteelPipeSection.HeadZoneDepths(800, 1500, 0));
            Assert.AreEqual((1500.0, 2300.0, 3200.0), SteelPipeSection.HeadZoneDepths(800, 1500, 800), "断面変化部 = l1 + l2");
            Assert.AreEqual((800.0, 1200.0, 3200.0), SteelPipeSection.HeadZoneDepths(800, 0, 400), "l1 未入力でも l2 は D から足す");

            // 検討式も同じ定数を使う (4.0 * D の書き写しが残っていない)
            string source = TestSource.Read("Graphics_r1", "Models", "InputData", "SteelPipeSection.cs");
            string eieq = TestSource.MethodBody(source, "internal double ComputeEIeqHead(");
            StringAssert.Contains(eieq, "HeadZoneDiameters * D");
            StringAssert.Contains(eieq, "HeadThickPartLength");
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
                body.PileBodySegments = [new PileBodySegment { No = 1, SegmentLength = 1.6, SegmentDepth = 1.6, PileSection = Head(1600, 700) }];
                input.PileBodies = [body];

                string file = Path.Combine(dir, "fill.pdj");
                svc.SaveProjectData(file, input, new PileDesign.FEM.AnaModel(), null);
                var loaded = svc.LoadProjectData(file).InputModel.PileBodies[0].PileBodySegments[0].PileSection;
                Assert.AreEqual(1600, loaded.CftFillLength, "入力した充填長が保存の往復で変わった");
                Assert.AreEqual(700, loaded.CftThickPartLength, "入力したl2 (充填部下端〜鋼管の断面変化部) が保存の往復で変わった");

                string text = FileOperationService.ReadSaveFileText(file);
                string old = Regex.Replace(text, "\\s*\"Cft(FillLength|ThickPartLength)\":\\s*[0-9.eE+-]+,?", "");
                Assert.IsFalse(old.Contains("CftFillLength") || old.Contains("CftThickPartLength"), "(前提) 充填長・l2 の項目を消せていない");
                string oldFile = Path.Combine(dir, "old.pdj");
                File.WriteAllText(oldFile, old);
                var fromOld = svc.LoadProjectData(oldFile).InputModel.PileBodies[0].PileBodySegments[0].PileSection;
                Assert.AreEqual(0, fromOld.CftFillLength, "以前のファイルを未入力として読んでいない");
                Assert.AreEqual(fromOld.PileDiameter, fromOld.EffectiveCftFillLength, "未入力の充填長が杭径 D でない");
                Assert.AreEqual(0, fromOld.CftThickPartLength, "以前のファイルを厚部なし (l2 = 0) として読んでいない");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 後片付けの失敗は無視 */ }
            }
        }
    }
}
