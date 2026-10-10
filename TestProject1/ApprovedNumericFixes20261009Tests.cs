using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 に利用者の了解を得て直した、結果が変わる誤り (基礎梁のねじり定数・既製杭の Ec・材料オプションの切替・SC 杭の M-φ・
    /// 慣性力 0 のケース・新規作成の解析条件)。
    /// </summary>
    [TestClass]
    public class ApprovedNumericFixes20261009Tests
    {
        /// <summary>矩形断面のねじり定数は 長辺 a・短辺 b で β·a·b³ (以前は断面の表とウィザードが a³·b で、0.5×1.0 m で 4 倍)。</summary>
        [TestMethod]
        public void BeamTorsionConstant_UsesLongSideTimesShortSideCubed()
        {
            double a = 1.0, b = 0.5;
            double beta = 1.0 / 3.0 - 0.21 * (b / a) * (1 - Math.Pow(b / a, 4) / 12.0);
            double expected = beta * a * b * b * b;
            Assert.AreEqual(expected, BeamSection.RectangularTorsionConstant(0.5, 1.0), 1e-12);
            Assert.AreEqual(expected, BeamSection.RectangularTorsionConstant(1.0, 0.5), 1e-12, "幅とせいを入れ替えても同じ値");

            var section = new BeamSection { Width = 0.5, Height = 1.0 };
            Assert.AreEqual(expected * section.IxxFactor, section.TorsionalMoment, 1e-12, "断面の表の J が式と違います");

            // 式を直す前に保存したファイル (J だけ古い値) も、開けば正しい値になる
            string json = JsonSerializer.Serialize(section).Replace(
                $"\"TorsionalMoment\":{JsonSerializer.Serialize(section.TorsionalMoment)}", "\"TorsionalMoment\":0.1144");
            var restored = JsonSerializer.Deserialize<BeamSection>(json)!;
            Assert.AreEqual(section.TorsionalMoment, restored.TorsionalMoment, 1e-12, "読込で J を求め直していません");

            foreach (var file in new[] { Path.Combine("Graphics_r1", "ViewModels", "BeamSectionWizardViewModel.cs"),
                                         Path.Combine("Graphics_r1", "FEM", "AnalysisModelling.cs"),
                                         Path.Combine("Graphics_r1", "FEM", "VerticalBeamModelling.cs") })
                StringAssert.Contains(TestSource.Read(file.Split(Path.DirectorySeparatorChar)), "RectangularTorsionConstant(",
                    $"{file} が共通の式を使っていません (写しは直すと取り残される)");
        }

        private static PileSection Phc(string product)
        {
            var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete, PileSectionType = PileTypeNames.Phc };
            s.SelectedPrecastPile.Name = product;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        /// <summary>既製杭の Ec はカタログの値のまま (以前は開き直すと式の値に変わった)。</summary>
        [TestMethod]
        public void PrecastConcreteE_StaysAtTheCatalogValue_AfterReload()
        {
            var section = Phc("PHC-600-標準-85-B");
            double catalog = section.SelectedPrecastPile.Ec;
            Assert.IsTrue(catalog > 0, "(前提) カタログに Ec がありません");
            Assert.AreEqual(catalog, section.ConcreteE, 1e-9);

            section.RecalculateConcreteE();
            Assert.AreEqual(catalog, section.ConcreteE, 1e-9, "式で計算し直すとカタログの Ec から変わります");

            var opts = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve };
            var restored = JsonSerializer.Deserialize<PileSection>(JsonSerializer.Serialize(section, opts), opts)!;
            Assert.AreEqual(catalog, restored.ConcreteE, 1e-9, "開き直すとカタログの Ec から変わります");
        }

        /// <summary>「鋼材のヤング係数を指針値に」の切替が、入力済みの断面にも届き、外せばカタログの値に戻る。</summary>
        [TestMethod]
        public void GuideYoungsModulus_ReachesExistingSections_BothWays()
        {
            bool original = ConcreteModelOptions.UseGuideYoungsModulus;
            try
            {
                ConcreteModelOptions.UseGuideYoungsModulus = false;
                var section = Phc("PHC-600-標準-85-B");
                double catalogEp = section.SelectedPrecastPile.Ep;
                Assert.AreEqual(catalogEp, section.TendonEp, 1e-9);

                ConcreteModelOptions.UseGuideYoungsModulus = true;
                section.ReapplyPrecastSteelYoungsModulus();
                Assert.AreEqual(ConcreteModelOptions.GuideModularRatioForPrecast * section.ConcreteE, section.TendonEp, 1e-6,
                    "指針値にしても入力済みの断面の Ep が変わりません");

                ConcreteModelOptions.UseGuideYoungsModulus = false;
                section.ReapplyPrecastSteelYoungsModulus();
                Assert.AreEqual(catalogEp, section.TendonEp, 1e-9, "指針値を外してもカタログの Ep に戻りません");
            }
            finally { ConcreteModelOptions.UseGuideYoungsModulus = original; }
        }

        /// <summary>SC 杭の M-φ の終局点の β は、検定の表 (安全限界 β1 = β2 = 1.0) と同じ。</summary>
        [TestMethod]
        public void ScMPhiBeta_MatchesTheCheckTable()
        {
            Assert.AreEqual(1.0, SCSection.MPhiBeta1);
            Assert.AreEqual(1.0, SCSection.MPhiBeta2);
        }

        /// <summary>慣性力 0 のケースでも、地盤変位があれば解く (以前は慣性力 0 なら飛ばしていた)。</summary>
        [TestMethod]
        public void ZeroInertiaCase_IsSolvedWhenThereIsGroundDisplacement()
        {
            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            // 判定は HasLoadToRun に出してあり、件数の数え方と共有する (WholeProgramAudit20261010Tests)
            StringAssert.Contains(run, "!HasLoadToRun(loadCase, null)");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "if (!HasGroundDisplacementAt(loadCase.Level)) return false;");
            StringAssert.Contains(run, "慣性力・地盤変位ともゼロのためスキップ");
            Assert.IsFalse(run.Contains("荷重がゼロのためスキップ"), "慣性力だけで飛ばす古い判定が残っています");
        }

        /// <summary>
        /// 新しいモデルの入力の署名が、複製 (元に戻すの控え) でも変わらない。以前は既定の梁断面が丸めた諸元 (J = 1.597 など) を持ち、
        /// 複製の読込で求め直した値と食い違って、元に戻すのあとに「再解析が必要」が誤って付いた。
        /// </summary>
        [TestMethod]
        public void NewModelSignature_IsStableAcrossCopies()
        {
            var input = new PileDesign.ViewModels.MainWindowViewModel().CurrentInputModel!;
            string? original = PileDesign.ViewModels.MainWindowViewModel.InputSignature(input);
            var copy = input.CopyForHistory();
            Assert.IsNotNull(original);
            Assert.AreEqual(original, PileDesign.ViewModels.MainWindowViewModel.InputSignature(copy), "複製すると署名が変わります");
            Assert.AreEqual(original, PileDesign.ViewModels.MainWindowViewModel.InputSignature(copy.CopyForHistory()));
        }

        /// <summary>新規作成 (Reset) は解析の条件も既定に戻す (以前は前のプロジェクトの P-S ばね・ねじれ拘束などが残った)。</summary>
        [TestMethod]
        public void Reset_RestoresAnalysisConditions()
        {
            var input = new PileDesign.ViewModels.MainWindowViewModel().CurrentInputModel!;
            input.UseAnalysisAxialForce = true;
            input.UsePsSpringAtPileTip = true;
            input.IsVLAnalysisEnabled = true;
            input.RestrainFoundationTorsion = true;
            input.Reset();
            Assert.IsFalse(input.UseAnalysisAxialForce);
            Assert.IsFalse(input.UsePsSpringAtPileTip);
            Assert.IsFalse(input.IsVLAnalysisEnabled);
            Assert.IsFalse(input.RestrainFoundationTorsion);
        }
    }
}
