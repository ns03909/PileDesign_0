using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    /// 土質点ごとの「液状化の判定」(自動 / 対象にする / 対象にしない)。
    ///
    /// <para>基礎指針'19 3.2 (1) は「原則的に地表面から 20 m 程度以浅、細粒分含有率 35% 以下」としたうえで、
    /// 造成地盤が 20 m 以深まで続くなら下端まで、粘土分 10% 以下か塑性指数 15 以下の埋立・盛土は Fc &gt; 35% でも、
    /// 洪積層でも N 値の小さい層・透水性の低い層に囲まれた礫も検討する、と例外を挙げる。
    /// 例外を規則にせず、設計者が土質点ごとに決める (2026-11-23 利用者の判断)。既定の「自動」は従来の規則で、結果は変わらない。</para>
    /// </summary>
    [TestClass]
    public class LiquefactionJudgementTests
    {
        [TestMethod]
        public void IncludeIgnoresTheDepthAndFcButNotTheWaterTable()
        {
            const double gw = -1.0;
            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(gw, -25.0, 10.0), "(前提) 自動では 20 m 以深は対象外");
            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(gw, -5.0, 60.0), "(前提) 自動では Fc > 35% は対象外");

            Assert.IsTrue(Liquefaction.IsLiquefactionLayer(gw, -25.0, 10.0, GroundMassDataInput.LiquefactionInclude), "造成地盤が 20 m 以深まで続く場合");
            Assert.IsTrue(Liquefaction.IsLiquefactionLayer(gw, -5.0, 60.0, GroundMassDataInput.LiquefactionInclude), "埋立の低塑性シルト");
            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(gw, -0.5, 10.0, GroundMassDataInput.LiquefactionInclude), "地下水位より上は飽和していないので対象にしない");

            Assert.IsFalse(Liquefaction.IsLiquefactionLayer(gw, -5.0, 10.0, GroundMassDataInput.LiquefactionExclude));
            Assert.AreEqual(Liquefaction.IsLiquefactionLayer(gw, -5.0, 10.0), Liquefaction.IsLiquefactionLayer(gw, -5.0, 10.0, GroundMassDataInput.LiquefactionAuto));
        }

        [TestMethod]
        public void TheDefaultIsAutoAndUnknownValuesAreIgnored()
        {
            var m = new GroundMassDataInput();
            Assert.AreEqual(GroundMassDataInput.LiquefactionAuto, m.LiquefactionJudgement);
            m.LiquefactionJudgement = GroundMassDataInput.LiquefactionExclude;
            m.LiquefactionJudgement = null!;   // ComboBox が書き戻す null
            m.LiquefactionJudgement = "対象";  // 選択肢に無い値
            Assert.AreEqual(GroundMassDataInput.LiquefactionExclude, m.LiquefactionJudgement);
            Assert.AreEqual(GroundMassDataInput.LiquefactionExclude, m.DeepCopy().LiquefactionJudgement, "DeepCopy (Undo の写し) で残る");
        }

        private static GroundInput Ground(string judgementAt25m, string judgementAt5m)
        {
            var g = new GroundInput
            {
                GroundTopAltitude = 0.0, BedrockDensity = 19.0, BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土", CalculationMethod = "a1(b1)",
                GroundWaterGLDepth = -1.0, GroundAcceleration1 = 2.0, GroundAcceleration2 = 3.5,
            };
            g.GroundLayers = [new GroundLayerInput
            {
                No = 1, BottomGLDepth = -30.0, LayerThickness = 30.0, BottomAltitude = -30.0,
                Name = "砂質土", GranularityClass = "砂質土", Density = 18.0, NValue = 10.0, Vs = 180.0, Es = 7000.0,
            }];
            g.GroundMassesData = [];
            for (int i = 1; i <= 28; i++)
                g.GroundMassesData.Add(new GroundMassDataInput { No = i, GLDepth = -i, H = 1.0, NValue = 10.0, VS0 = 180.0, Fc = 10.0 });
            g.GroundMassesData[24].LiquefactionJudgement = judgementAt25m;
            g.GroundMassesData[4].LiquefactionJudgement = judgementAt5m;
            return g;
        }

        [TestMethod]
        public void TheGroundWindowUsesTheJudgement()
        {
            var g = Ground(GroundMassDataInput.LiquefactionInclude, GroundMassDataInput.LiquefactionExclude);
            new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = g }.Update();

            var deep = g.GroundMassesData[24];
            Assert.IsTrue(deep.IsLiquefactionLayer, "「対象にする」の GL−25 m が対象になっていない");
            Assert.IsTrue(deep.FL[1] is > 0, "対象にした土質点の FL が求まっていない");
            Assert.IsFalse(g.GroundMassesData[23].IsLiquefactionLayer, "「自動」の GL−24 m は対象外のまま");
            Assert.IsFalse(g.GroundMassesData[4].IsLiquefactionLayer, "「対象にしない」の GL−5 m が対象になっている");
            Assert.IsNull(g.GroundMassesData[4].FL[1], "対象にしない土質点に FL が出ている");
            Assert.IsTrue(g.GroundMassesData[3].IsLiquefactionLayer, "「自動」の GL−4 m は対象のまま");
        }

        [TestMethod]
        public void TheInputWarnsAboutClayWithoutFcAndDesignatedPointsBelow20m()
        {
            var g = Ground(GroundMassDataInput.LiquefactionInclude, GroundMassDataInput.LiquefactionAuto);
            g.GroundMassesData[2].GranularityClass = "粘性土";
            g.GroundMassesData[2].Fc = 0.0;
            g.ValidateForAnalysis(out string warning);
            StringAssert.Contains(warning, "土質点番号 3: 粘性土ですが細粒分含有率 Fc が 0");
            StringAssert.Contains(warning, "土質点番号 25: GL−20 m 以深を液状化の判定の対象にしています");

            // 設計者が決めたら知らせない (Fc を入れた・対象にしないにした)
            g.GroundMassesData[2].LiquefactionJudgement = GroundMassDataInput.LiquefactionExclude;
            g.GroundMassesData[24].LiquefactionJudgement = GroundMassDataInput.LiquefactionAuto;
            g.ValidateForAnalysis(out warning);
            Assert.IsFalse(warning.Contains("粘性土ですが", StringComparison.Ordinal), warning);
            Assert.IsFalse(warning.Contains("GL−20 m 以深", StringComparison.Ordinal), warning);
        }

        /// <summary>
        /// 図3.2.2 の線は Fc = 50% (ΔNf = 11) で止まっている。その先は 0 に落とさず端の値で頭打ちにし、
        /// 範囲外を読んでいると知らせる (2026-11-23 利用者の判断)。「対象にする」でだけ通る経路。
        /// </summary>
        [TestMethod]
        public void FinesContentAbove50PercentUsesTheEndOfTheCurve()
        {
            var g = Ground(GroundMassDataInput.LiquefactionAuto, GroundMassDataInput.LiquefactionInclude);
            g.GroundMassesData[4].Fc = 50.0;
            g.GroundMassesData[5].Fc = 60.0;
            g.GroundMassesData[5].LiquefactionJudgement = GroundMassDataInput.LiquefactionInclude;
            new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = g }.Update();

            Assert.AreEqual(11.0, g.GroundMassesData[4].DeltaNf!.Value, 1e-9, "Fc = 50% は図の端 11");
            Assert.AreEqual(11.0, g.GroundMassesData[5].DeltaNf!.Value, 1e-9, "Fc > 50% が 11 で頭打ちになっていない");

            g.ValidateForAnalysis(out string warning);
            StringAssert.Contains(warning, "土質点番号 6: 細粒分含有率 Fc が 50% を超えています");
            Assert.IsFalse(warning.Contains("土質点番号 5: 細粒分含有率 Fc が 50%", StringComparison.Ordinal), "50% ちょうどは図の範囲内");
        }

        [TestMethod]
        public void TheJudgementSurvivesASaveAndLoad()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var masses = input.GroundsInput[0].GroundMassesData;
            masses.Clear();
            for (int i = 1; i <= 3; i++) masses.Add(new GroundMassDataInput { No = i, GLDepth = -i, H = 1.0 });
            masses[0].LiquefactionJudgement = GroundMassDataInput.LiquefactionInclude;
            masses[1].LiquefactionJudgement = GroundMassDataInput.LiquefactionExclude;

            var service = new FileOperationService(new JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });
            string path = Path.Combine(Path.GetTempPath(), $"pd_liq_{Guid.NewGuid():N}.pdjson");
            try
            {
                service.SaveProjectData(path, input, null);
                var loaded = service.LoadProjectData(path).InputModel.GroundsInput[0].GroundMassesData;
                Assert.AreEqual(GroundMassDataInput.LiquefactionInclude, loaded[0].LiquefactionJudgement);
                Assert.AreEqual(GroundMassDataInput.LiquefactionExclude, loaded[1].LiquefactionJudgement);
                Assert.IsTrue(loaded.Skip(2).All(m => m.LiquefactionJudgement == GroundMassDataInput.LiquefactionAuto));
            }
            finally { File.Delete(path); }
        }
    }
}
