using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 最下層より深い土質点があっても、地盤変位が NaN にならないこと。入力側で警告が出ること。
    ///
    /// <para>土質点の密度などは重なる土層から写すので、どの土層とも重ならない土質点
    /// (最下層より深い) は密度 0 のまま残る。すると等価S波速度と等価せん断ばね剛性が 0 になり、
    /// モード形の漸化式 U_i = U_{i−1} − 40/K_{i−1}/(αT0)²·μ が K で割って −∞ → NaN。
    /// U* は最下の土質点の U を使うので、<b>1 か所の NaN が上の全土質点に広がり</b>、
    /// 地盤変位の分布が全部 NaN になっていた。保存もできない (NaN は JSON に書けない)。
    /// 同梱例題の設計例集3.8 (土質点 GL−50 m / 土層 GL−28.3 m) と 3.5-2 で実際に起きていた。
    /// 2026-09-12 に、K = 0 の土質点で地盤の柱を止める (工学的基盤と同じ扱い) ようにした。</para>
    /// </summary>
    [TestClass]
    public class GroundBelowDeepestLayerTests
    {
        /// <summary>土層は GL−10 m まで、土質点は GL−15 m まである地盤。</summary>
        private static GroundInput Ground()
        {
            var g = new GroundInput
            {
                GroundTopAltitude = 0.0,
                BedrockDensity = 19.0,
                BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土",
                CalculationMethod = "a1(b1)",
            };
            g.GroundLayers = [new GroundLayerInput
            {
                No = 1, BottomGLDepth = -10.0, LayerThickness = 10.0, BottomAltitude = -10.0,
                Name = "砂質土", GranularityClass = "砂質土", Density = 18.0, NValue = 10.0, Vs = 180.0, Es = 7000.0,
            }];

            g.GroundMassesData = [];
            for (int i = 1; i <= 15; i++)
            {
                g.GroundMassesData.Add(new GroundMassDataInput
                {
                    No = i,
                    GLDepth = -i,
                    H = 1.0,
                    NValue = 10.0,
                    VS0 = 180.0,
                    Fc = 20.0,
                    Density = 0.0,   // 土層から写される (最下層より深い土質点は 0 のまま)
                });
            }
            return g;
        }

        [TestMethod]
        public void TheDisplacementStaysFiniteAndStopsAtTheDeepestLayer()
        {
            var ground = Ground();
            var vm = new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground };
            vm.Update();

            var masses = ground.GroundMassesData;
            for (int i = 0; i < masses.Count; i++)
            {
                foreach (int level in new[] { 0, 1 })
                {
                    Assert.IsTrue(double.IsFinite(masses[i].UStar[level]),
                        $"土質点 {i + 1} (GL{masses[i].GLDepth:0.0}m) の U* が {masses[i].UStar[level]} です");
                    Assert.IsTrue(double.IsFinite(masses[i].DmaxUStar[level]),
                        $"土質点 {i + 1} の地盤変位が {masses[i].DmaxUStar[level]} です");
                }
            }

            // 地表は 1、止めた位置 (最下層 GL−10 m の 1 つ下 = 11 番目) から下は 0
            Assert.AreEqual(1.0, masses[0].UStar[1], 1e-12, "地表の U* が 1 ではありません");
            Assert.AreEqual(0.0, masses[10].UStar[1], 0.0, "最下層より深い土質点の U* が 0 ではありません");
            Assert.AreEqual(0.0, masses[^1].UStar[1], 0.0, "最下の土質点の U* が 0 ではありません");
            Assert.IsTrue(masses[0].DmaxUStar[1] > 0, "地表の地盤変位が 0 です");
        }

        /// <summary>
        /// 土質点が 1 件だけの地盤。基盤の変位 u_{N+1} が地表の 1 と同じになるので、
        /// U* = (U − u_{N+1})/(1 − u_{N+1}) が 0/0 で NaN になっていた。
        /// 同梱の地盤例題 ExampleCPT2018 (土質点 1 件) は、これで保存できなくなっていた。
        /// </summary>
        [TestMethod]
        public void ASingleMassGroundStaysFinite()
        {
            var ground = Ground();
            ground.GroundMassesData = [ground.GroundMassesData[0]];

            var vm = new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground };
            vm.Update();

            var mass = ground.GroundMassesData[0];
            foreach (int level in new[] { 0, 1 })
            {
                Assert.IsTrue(double.IsFinite(mass.UStar[level]), $"U* が {mass.UStar[level]} です");
                Assert.IsTrue(double.IsFinite(mass.DmaxUStar[level]), $"地盤変位が {mass.DmaxUStar[level]} です");
                Assert.IsTrue(double.IsFinite(mass.DmaxUStarSigmaGammaCyH[level]),
                    $"液状化時の地盤変位が {mass.DmaxUStarSigmaGammaCyH[level]} です");
            }
        }

        [TestMethod]
        public void TheInputWarnsAboutMassesBelowTheDeepestLayer()
        {
            var ground = Ground();
            bool ok = ground.ValidateForAnalysis(out string warning);

            Assert.IsFalse(ok, "最下層より深い土質点があるのに警告が出ません");
            StringAssert.Contains(warning, "最下層", "警告の文面に最下層より深いことが書かれていません");
            StringAssert.Contains(warning, "土質点番号 11", "最下層より深い最初の土質点が指摘されていません");
        }

        [TestMethod]
        public void AGroundWithinItsLayersDoesNotWarn()
        {
            var ground = Ground();
            // 土層を土質点より深くすれば、この警告は出ない
            ground.GroundLayers[0].BottomGLDepth = -20.0;
            ground.GroundLayers[0].LayerThickness = 20.0;
            ground.GroundLayers[0].BottomAltitude = -20.0;

            ground.ValidateForAnalysis(out string warning);
            Assert.IsFalse(warning.Contains("最下層", StringComparison.Ordinal),
                $"土層の範囲内なのに最下層の警告が出ています: {warning}");
        }
    }
}
