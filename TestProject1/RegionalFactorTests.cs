using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 地域係数 Z の入力 (2026-11-21、利用者の判断)。
    ///
    /// <para>それまでは地盤変位の算定 (基礎指針'19 式 (4.5.3) の α = 1 + L·Z·Cα·T0/ΣH) と応答スペクトル法の
    /// Sa(T) = L·Z·Sa0(T) で Z を 1.0 に固定していて、入力欄もヘルプの記述も無かった。
    /// 既定 1.0 なので、以前のファイルの結果は変わらない。</para>
    /// </summary>
    [TestClass]
    public class RegionalFactorTests
    {
        /// <summary>土層 GL−10 m まで、土質点 10 点の一様な砂質土 (a2(b2) の略算)。</summary>
        private static GroundInput Ground(double z)
        {
            var g = new GroundInput
            {
                GroundTopAltitude = 0.0,
                BedrockDensity = 19.0,
                BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土",
                CalculationMethod = "a2(b2)",
                RegionalFactor = z,
            };
            g.GroundLayers = [new GroundLayerInput
            {
                No = 1, BottomGLDepth = -10.0, LayerThickness = 10.0, BottomAltitude = -10.0,
                Name = "砂質土", GranularityClass = "砂質土", Density = 18.0, NValue = 10.0, Vs = 180.0, Es = 7000.0,
            }];
            g.GroundMassesData = [];
            for (int i = 1; i <= 10; i++)
                g.GroundMassesData.Add(new GroundMassDataInput { No = i, GLDepth = -i, H = 1.0, NValue = 10.0, VS0 = 180.0, Fc = 20.0 });
            return g;
        }

        private static double SurfaceDisplacement(double z, int level)
        {
            var ground = Ground(z);
            var vm = new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground };
            vm.Update();
            return ground.GroundMassesData[0].DmaxUStar[level];
        }

        [TestMethod]
        public void TheDefaultIsOne_AndACopyKeepsTheValue()
        {
            Assert.AreEqual(1.0, new GroundInput().RegionalFactor, "既定値が 1.0 でない (以前のファイルの結果が変わる)");
            var g = Ground(0.8);
            Assert.AreEqual(0.8, g.DeepCopy().RegionalFactor, "写しで地域係数が落ちた");
            g.RegionalFactor = 5.0;
            Assert.AreEqual(2.0, g.RegionalFactor, "範囲 (0.1〜2.0) に丸めていない");
        }

        /// <summary>Z が α に入る: 小さくすると地表の地盤変位が小さくなり、1.0 は以前 (固定) と同じ。</summary>
        [TestMethod]
        public void TheFactorReachesTheGroundDisplacement()
        {
            for (int level = 0; level < 2; level++)
            {
                double one = SurfaceDisplacement(1.0, level);
                double point8 = SurfaceDisplacement(0.8, level);
                Assert.IsTrue(one > 0, $"(前提) レベル{level + 1} の地表の地盤変位が 0");
                Assert.IsTrue(point8 < one, $"レベル{level + 1}: Z = 0.8 で地盤変位が小さくならない ({point8} ≧ {one})。Z が算定に届いていない");
            }

            // a2(b2): Dmax = C1(α²−1)·fA·ΣH、α = 1 + L·Z·Cα·T0/ΣH。fA = 1 になる範囲では (α²−1) の比になる
            var g1 = Ground(1.0); var vm1 = new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = g1 }; vm1.Update();
            // 一様な Vs0 = 180 なので T0 = 4ΣH/180 から ΣH を戻す (工学的基盤の土質点を数えるかに依らない)
            double t0 = g1.NaturalPeriod, sumH = t0 * 180.0 / 4.0;
            Assert.IsTrue(sumH > 5 && sumH <= 10 + 1e-9, $"(前提) ΣH = {sumH}");
            double Alpha(double z) => System.Math.Min(1 + 1.0 * z * 40 * t0 / sumH, 4.0);
            double FA(double z) => System.Math.Min(1.6 * Alpha(z) * t0, 1.0);
            double expected = (Alpha(0.8) * Alpha(0.8) - 1) * FA(0.8) / ((Alpha(1.0) * Alpha(1.0) - 1) * FA(1.0));
            Assert.AreEqual(expected, SurfaceDisplacement(0.8, 1) / SurfaceDisplacement(1.0, 1), 1e-9,
                "Z = 0.8 と 1.0 の地盤変位の比が式 (4.5.1)・(4.5.3) と合わない");
        }

        /// <summary>告示の値 (0.7〜1.0) の外は解析前の確認で知らせる。中は知らせない。</summary>
        [TestMethod]
        public void OutsideTheNoticeRangeIsReported()
        {
            foreach (var (z, expectNotice) in new[] { (1.0, false), (0.7, false), (0.85, false), (0.5, true), (1.2, true) })
            {
                var model = new InputModel { GroundsInput = [Ground(z)] };
                bool noticed = CheckInputData.CollectRegionalFactorNotices(model).Any(d => d.Message.Contains("地域係数"));
                Assert.AreEqual(expectNotice, noticed, $"Z = {z} の知らせ");
            }
        }
    }
}
