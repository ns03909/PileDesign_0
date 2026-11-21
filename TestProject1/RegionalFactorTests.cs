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
    /// 同日にいったん地盤ごとの入力にしたが、建設地で 1 つに決まる値なので利用者の要望で基本設定へ移した。
    /// 地盤変位は地盤ごとに計算して保存するので、基本設定で変えたらすべての地盤を計算し直す。
    /// 既定 1.0 なので、以前のファイルの結果は変わらない。</para>
    /// </summary>
    [TestClass]
    public class RegionalFactorTests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        /// <summary>土層 GL−10 m まで、土質点 10 点の一様な砂質土 (a2(b2) の略算)。</summary>
        private static GroundInput Ground()
        {
            var g = new GroundInput
            {
                GroundTopAltitude = 0.0,
                BedrockDensity = 19.0,
                BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土",
                CalculationMethod = "a2(b2)",
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

        /// <summary>基本設定の Z で 1 つの地盤を計算し、地表の地盤変位 (mm) を返す。</summary>
        private static (double Displacement, double T0) Surface(double z, int level)
        {
            using var main = new MainWindowViewModel();
            main.CurrentInputModel!.FundamentalInput.RegionalFactor = z;
            var ground = Ground();
            var vm = new GroundLayerViewModel(main) { GroundInput = ground };
            vm.Update();
            return (ground.GroundMassesData[0].DmaxUStar[level], ground.NaturalPeriod);
        }

        [TestMethod]
        public void TheDefaultIsOne_AndTheValueIsClamped()
        {
            Assert.AreEqual(1.0, new FundamentalInput().RegionalFactor, "既定値が 1.0 でない (以前のファイルの結果が変わる)");
            var f = new FundamentalInput { RegionalFactor = 0.8 };
            Assert.AreEqual(0.8, f.ShallowCopy().RegionalFactor, "写しで地域係数が落ちた");
            f.RegionalFactor = 5.0;
            Assert.AreEqual(2.0, f.RegionalFactor, "範囲 (0.1〜2.0) に丸めていない");
        }

        /// <summary>基本設定の Z が α に入る: 小さくすると地表の地盤変位が式 (4.5.1)・(4.5.3) どおりに小さくなる。</summary>
        [TestMethod]
        public void TheFactorReachesTheGroundDisplacement()
        {
            for (int level = 0; level < 2; level++)
            {
                double one = Surface(1.0, level).Displacement, point8 = Surface(0.8, level).Displacement;
                Assert.IsTrue(one > 0, $"(前提) レベル{level + 1} の地表の地盤変位が 0");
                Assert.IsTrue(point8 < one, $"レベル{level + 1}: Z = 0.8 で地盤変位が小さくならない ({point8} ≧ {one})。Z が算定に届いていない");
            }

            // a2(b2): Dmax = C1(α²−1)·fA·ΣH、α = 1 + L·Z·Cα·T0/ΣH。一様な Vs0 = 180 なので T0 = 4ΣH/180 から ΣH を戻す
            var (d1, t0) = Surface(1.0, 1);
            double sumH = t0 * 180.0 / 4.0;
            Assert.IsTrue(sumH > 5 && sumH <= 10 + 1e-9, $"(前提) ΣH = {sumH}");
            double Alpha(double z) => System.Math.Min(1 + 1.0 * z * 40 * t0 / sumH, 4.0);
            double FA(double z) => System.Math.Min(1.6 * Alpha(z) * t0, 1.0);
            double expected = (Alpha(0.8) * Alpha(0.8) - 1) * FA(0.8) / ((Alpha(1.0) * Alpha(1.0) - 1) * FA(1.0));
            Assert.AreEqual(expected, Surface(0.8, 1).Displacement / d1, 1e-9, "Z = 0.8 と 1.0 の地盤変位の比が式と合わない");
        }

        /// <summary>
        /// 基本設定で Z を変えて保存すると、すべての地盤の地盤変位を計算し直す (地盤ごとに保存している値が古いまま残らない)。
        /// キャンセルなら計算し直さず、Z も戻る。
        /// </summary>
        [TestMethod]
        public void SavingTheSettingsRecalculatesEveryGround()
        {
            using var main = new MainWindowViewModel();
            var model = main.CurrentInputModel!;
            model.GroundsInput = [Ground(), Ground()];
            GroundLayerViewModel.RecalculateAllGrounds(main);
            double before0 = model.GroundsInput[0].GroundMassesData[0].DmaxUStar[1];
            double before1 = model.GroundsInput[1].GroundMassesData[0].DmaxUStar[1];
            Assert.IsTrue(before0 > 0 && before1 > 0, "(前提) 地盤変位が計算されていない");

            // キャンセル: 戻る
            var cancel = new FundamentalViewModel(main) { RegionalFactor = 0.8 };
            cancel.CancelCommand.Execute(null);
            Assert.AreEqual(1.0, model.FundamentalInput.RegionalFactor, "キャンセルで地域係数が戻らない");
            Assert.AreEqual(before0, model.GroundsInput[0].GroundMassesData[0].DmaxUStar[1], "キャンセルなのに地盤変位が変わった");

            // 保存: すべての地盤を計算し直す
            var ok = new FundamentalViewModel(main) { RegionalFactor = 0.8 };
            ok.OkCommand.Execute(null);
            Assert.AreEqual(0.8, model.FundamentalInput.RegionalFactor);
            Assert.IsTrue(model.GroundsInput[0].GroundMassesData[0].DmaxUStar[1] < before0, "保存しても地盤 1 の地盤変位を計算し直していない");
            Assert.IsTrue(model.GroundsInput[1].GroundMassesData[0].DmaxUStar[1] < before1, "保存しても地盤 2 の地盤変位を計算し直していない");
        }

        /// <summary>告示の値 (0.7〜1.0) の外は解析前の確認で知らせる。中は知らせない。</summary>
        [TestMethod]
        public void OutsideTheNoticeRangeIsReported()
        {
            foreach (var (z, expectNotice) in new[] { (1.0, false), (0.7, false), (0.85, false), (0.5, true), (1.2, true) })
            {
                var model = new InputModel { FundamentalInput = new FundamentalInput { RegionalFactor = z } };
                bool noticed = CheckInputData.CollectRegionalFactorNotices(model).Any(d => d.Message.Contains("地域係数"));
                Assert.AreEqual(expectNotice, noticed, $"Z = {z} の知らせ");
            }
        }
    }
}
