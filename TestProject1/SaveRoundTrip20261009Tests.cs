using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の保存・履歴の見直しで見つかった、保存ファイルを往復すると入力が既定値に戻る誤り。
    /// Undo の写し (フィールドも含める) では残るので、保存して開き直すまで気づけない形だった。
    /// </summary>
    [TestClass]
    public class SaveRoundTrip20261009Tests
    {
        private static FileOperationService Service() => new(new JsonSerializerOptions
        {
            WriteIndented = false,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        });

        private static InputModel SaveAndLoad(InputModel input)
        {
            string path = Path.Combine(Path.GetTempPath(), $"pd_roundtrip_{Guid.NewGuid():N}.pdjson");
            try
            {
                Service().SaveProjectData(path, input, null);
                return Service().LoadProjectData(path).InputModel;
            }
            finally { File.Delete(path); }
        }

        private static (double Theta, double Moment)[] Points(PileHeadRotationDef def)
            => def.CurveXY?.Points.ToArray() ?? [];

        /// <summary>
        /// 杭頭部の入力が保存ファイルを往復すること。以前は <c>PileBodyInput.PileTop</c> が公開フィールドで書き出されず、
        /// キャプテンパイルの PC リングの選択が消えて杭頭が剛接合で解かれ、鋼管杭の杭体側主筋は D29×0 に戻っていた。
        /// </summary>
        [TestMethod]
        public void PileTop_SurvivesASaveAndLoad_AndGivesTheSameMTheta()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var pb = input.PileBodies[0];
            pb.PileTopType = "キャプテンパイル工法";
            var cp = new CaptainPile(24.0, 22669.0);
            var ring = cp.PCRings.First(r => (r.Name ?? "").EndsWith("N"));
            cp.PCRing = ring;
            cp.D = ring.D;
            cp.SelectedPCRingName = ring.Name!;
            cp.Nu = 0.85;
            cp.Update();
            pb.PileTop.CaptainPile = cp;
            pb.PileTop.PileCapFc = 30.0;
            pb.PileTop.MainBarSize2 = "D32";
            pb.PileTop.MainBarNum2 = 24;
            var before = pb.GetMThetaRelationship(1000.0);
            Assert.AreNotEqual(PileHeadRotationMode.Rigid, before.Mode, "(前提) キャプテンパイルの M-θ が組めていません");

            var restored = SaveAndLoad(input).PileBodies[0];

            Assert.AreEqual(30.0, restored.PileTop.PileCapFc, 1e-9, "パイルキャップの Fc が既定に戻っています");
            Assert.AreEqual("D32", restored.PileTop.MainBarSize2);
            Assert.AreEqual(24, restored.PileTop.MainBarNum2, "杭体側の主筋本数が既定に戻っています");
            Assert.AreEqual(ring.Name, restored.PileTop.CaptainPile?.SelectedPCRingName, "PC リングの選択が消えています");
            Assert.AreEqual(0.85, restored.PileTop.CaptainPile!.Nu, 1e-12, "絞り率が既定に戻っています");
            Assert.IsTrue(restored.PileTop.CaptainPile.PCRingOption.Contains(ring.Name!),
                "PC リングのカタログを読み直していません (保存しないので、読込後に読み直す)");

            var after = restored.GetMThetaRelationship(1000.0);
            Assert.AreEqual(before.Mode, after.Mode, "開き直すと杭頭の扱いが変わります (剛接合に落ちている)");
            var a = Points(before); var b = Points(after);
            Assert.AreEqual(a.Length, b.Length, "M-θ の点数が違います");
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].Theta, b[i].Theta, 1e-12 + 1e-9 * Math.Abs(a[i].Theta), $"θ[{i}]");
                Assert.AreEqual(a[i].Moment, b[i].Moment, 1e-9 + 1e-9 * Math.Abs(a[i].Moment), $"M[{i}]");
            }
        }

        /// <summary>
        /// ファイルで空だった一覧 (基礎梁の無い計算例の材料・断面) に、読込の仕上げが既定の要素を足しても、
        /// その項目を「ファイルに無かったので既定値にした」と数えない (今の版で保存したファイルで「古い形式からの読み込み」が出た)。
        /// </summary>
        [TestMethod]
        public void EmptyListsInTheFile_AreNotReportedAsFilledProperties()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            input.FoundationBeamInput.Materials.Clear();
            input.FoundationBeamInput.Sections.Clear();
            string path = Path.Combine(Path.GetTempPath(), $"pd_emptylists_{Guid.NewGuid():N}.pdjson");
            try
            {
                Service().SaveProjectData(path, input, null);
                var data = Service().LoadProjectData(path);
                data.InputModel.EnsureFoundationBeamDefaults();
                Assert.IsTrue(data.InputModel.FoundationBeamInput.Materials.Count > 0, "(前提) 読込の仕上げが既定の材料を足していません");
                var (_, missing) = Service().CompareInputProperties(data, data.InputModel);
                Assert.IsFalse(missing.Any(m => m.StartsWith("FoundationBeamInput.Materials[]", StringComparison.Ordinal)
                                             || m.StartsWith("FoundationBeamInput.Sections[]", StringComparison.Ordinal)),
                    "空の一覧に足した要素の項目を「既定値で補った」と数えています: " + string.Join(", ", missing));
            }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// FT-Pile の FT キャップの選択と、そこから導く支圧係数が保存ファイルを往復すること。
        /// 以前は <c>FTPile.FTCap</c> もフィールドで、開き直すと寸法 0 のキャップ・支圧係数 0 になり、θ の上限が 0 除算になった。
        /// </summary>
        [TestMethod]
        public void FtPile_KeepsTheCapAndItsBearingFactor_AcrossASaveAndLoad()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var pb = input.PileBodies[0];
            pb.PileTopType = "FT-Pile構法";
            var ft = new FTPile(24.0, 22669.0);
            Assert.IsTrue(ft.FTCaps?.Count > 0, "(前提) FT キャップのカタログが読めていません");
            var cap = ft.FTCaps!.OrderBy(c => c.Phi).Skip(ft.FTCaps.Count / 2).First();
            ft.FTCap = cap;
            ft.SelectedFTCapName = cap.Phi.ToString();
            ft.FTPilePile.SetDimensions(600.0, 400.0);
            ft.Update();
            Assert.IsTrue(ft.PhiC > 0, "(前提) 支圧係数が求まっていません");
            pb.PileTop.FTPile = ft;

            var restored = SaveAndLoad(input).PileBodies[0].PileTop.FTPile;

            Assert.IsNotNull(restored, "FT-Pile のモデルが消えています");
            Assert.AreEqual(cap.Phi, restored.FTCap.Phi, 1e-9, "FT キャップの選択が既定に戻っています");
            Assert.AreEqual(ft.PhiC, restored.PhiC, 1e-12, "支圧係数を読込後に組み直していません (0 のままだと θ の上限が 0 除算)");
            Assert.AreEqual(ft.Ns?.Count ?? 0, restored.Ns?.Count ?? 0, "N-M-θ の表を組み直していません");
            Assert.AreEqual(ft.FTCaps.Count, restored.FTCaps?.Count ?? 0, "FT キャップのカタログを読み直していません");
        }
    }
}
