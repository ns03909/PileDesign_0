using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 土圧合力ばね (根入部) の土圧 p0・pp は、区間の<b>深さ</b>の土被り圧で決まること (2026-10-09)。
    ///
    /// 以前は p0 = (q + γh)K0、pp = (q + γh)Kp + 2c√Kp の h に<b>区間の厚さ</b> (ZTop − ZBtm) を使っていた。
    /// 同じ厚さの区間は浅くても深くても同じ土圧になり、深い区間の抵抗を過小に見ていた。
    /// 基礎指針'19 (4.3.21)(4.3.22) の合力 ½γB(zb²−za²)K を区間厚と B で割ると γ(za+zb)/2·K で、
    /// z は地表面からの深さ。いまは区間の上端・下端の有効土被り圧 (地表面から数える) の平均を使う。
    /// また上載荷重 q は式にあるが入力欄がなく、常に 0 のまま (呼び出し側が与えれば効く)。
    /// </summary>
    [TestClass]
    public class EarthPressureSpringDepth20261009Tests
    {
        private static double Kp(double phiDeg) => Math.Pow(Math.Tan((45 + phiDeg / 2.0) * Math.PI / 180), 2);

        private static DoatsuGoryokuBaneItem Item(double zTop, double zBtm, double gamma, double phi, double c)
            // 一様な土層・地下水位なし・地表面 0 の区間 (有効土被り圧 = γ × 地表面からの深さ)
            => new()
            {
                Gamma = gamma, Phi = phi, C = c, ZTop = zTop, ZBtm = zBtm, K0 = 0.5,
                StressTop = gamma * -zTop, StressBtm = gamma * -zBtm,
            };

        [TestMethod]
        public void SegmentsOfEqualThickness_DeeperOneHasLargerPressure()
        {
            var shallow = Item(-1.0, -2.0, 18, 30, 10);
            var deep = Item(-5.0, -6.0, 18, 30, 10);
            foreach (var it in new[] { shallow, deep }) it.SetDeltaPAndYsp(0.3, 0.03);

            Assert.AreEqual(shallow.DZ, deep.DZ, 1e-12, "(前提) 区間の厚さが揃っていません");
            Assert.IsTrue(deep.P0 > shallow.P0, $"静止土圧が深さで増えていません (浅 {shallow.P0} / 深 {deep.P0})");
            Assert.IsTrue(deep.Pp > shallow.Pp, $"受働土圧が深さで増えていません (浅 {shallow.Pp} / 深 {deep.Pp})");
            Assert.IsTrue(deep.GetPressure(0.3) > shallow.GetPressure(0.3), "上限の土圧度が深さで増えていません");
            Assert.IsTrue(deep.GetPressure(0.01) > shallow.GetPressure(0.01), "初期の土圧度が深さで増えていません");
        }

        /// <summary>手計算: 深さ 2〜4 m、γ=18、φ=30°、c=10、q=0 → σz' = 18·3 = 54。</summary>
        [TestMethod]
        public void HandCalculation_MatchesShishin19Eq4_3_21And22()
        {
            var item = Item(-2.0, -4.0, 18, 30, 10);
            double kp = 3.0; // tan²(60°)
            Assert.AreEqual(kp, item.Kp, 1e-9);
            Assert.AreEqual(54.0, item.StressMid, 1e-9);
            Assert.AreEqual(54.0 * 0.5, item.P0, 1e-9);                          // 27
            Assert.AreEqual(54.0 * kp + 2 * 10 * Math.Sqrt(kp), item.Pp, 1e-9);  // 162 + 34.641 = 196.641

            // 区間の合力 (×B·h) が基礎指針'19 (4.3.21)(4.3.22) の合力と一致する (B = 1 m あたり)
            double za = 2.0, zb = 4.0, h = zb - za;
            double p0Book = 0.5 * 18 * (zb * zb - za * za) * 0.5;
            double ppBook = 2 * 10 * (zb - za) * Math.Sqrt(kp) + 0.5 * 18 * (zb * zb - za * za) * kp;
            Assert.AreEqual(p0Book, item.P0 * h, 1e-9, "(4.3.21) と合いません");
            Assert.AreEqual(ppBook, item.Pp * h, 1e-9, "(4.3.22) と合いません");
        }

        [TestMethod]
        public void Surcharge_AddsToTheOverburdenOfEverySegment()
        {
            var item = Item(-2.0, -4.0, 18, 30, 0);
            double p0 = item.P0, pp = item.Pp;
            Assert.AreEqual(0.0, item.Q, "上載荷重の既定は 0 (入力欄がない)");

            item.Q = 20;
            Assert.AreEqual(p0 + 20 * 0.5, item.P0, 1e-9, "q が静止土圧に K0 倍で足されていません");
            Assert.AreEqual(pp + 20 * item.Kp, item.Pp, 1e-9, "q が受働土圧に Kp 倍で足されていません");
        }

        // ===== 画面で組み立てた土圧合力ばね (ElementDivisionViewModel.SetDoatsuGoryokuBane) =====

        /// <summary>
        /// 計算例9 の地盤 (地表面 0、地下水位 −1.5、0〜−0.8 γ14.7 / −0.8〜−1.5 γ14.7 / −1.5〜−2.5 γ15.7 / −2.5〜−8.5 γ17.7) に
        /// 根入部の区間を置いて組み立てる。
        /// </summary>
        private static (ElementDivisionViewModel vm, GroundInput ground)? Build(double[] zs, Action<GroundInput>? customize = null)
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (input?.ElementDivision?.SoilEmbedment == null) return null;
            var ground = input.GroundsInput[input.ElementDivision.SoilEmbedment.GroundNo - 1];
            // 地下水位は例題の GL 深さ (−1.5) を標高に直したもの。テストの読み込みは標高を写さないので明示する。
            ground.GroundWaterTableAltitude = -1.5;
            customize?.Invoke(ground);

            var mainVm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(mainVm);
            var vm = new ElementDivisionViewModel(mainVm,
                input.ElementDivision.SoilPiles.Select(sp => sp.DeepCopy()).ToList(),
                input.ElementDivision.SoilEmbedment.DeepCopy());
            vm.EmbedmentZsCollection = new ObservableCollection<EmbedmentZDataItem>(
                zs.Select(z => new EmbedmentZDataItem { Z = z, GroundInput = ground }));
            vm.SetDoatsuGoryokuBane();
            return (vm, ground);
        }

        private static readonly double[] Zs = [0.0, -0.8, -1.5, -2.5, -3.5, -4.5, -5.5, -6.5];

        [TestMethod]
        public void BuiltSprings_UseEffectiveOverburdenFromTheGroundSurface()
        {
            var built = Build(Zs);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, ground) = built.Value;
            Assert.AreEqual(0.0, ground.GroundTopAltitude, 1e-9, "(前提) 地表面");
            Assert.AreEqual(-1.5, ground.GroundWaterTableAltitude, 1e-9, "(前提) 地下水位");

            var items = vm.DoatsuGoryokuBane.Items;
            Assert.AreEqual(Zs.Length - 1, items.Count);
            var a = items.Single(i => Math.Abs(i.ZTop - -2.5) < 1e-9);  // 細砂 2.5〜3.5 m
            var b = items.Single(i => Math.Abs(i.ZTop - -5.5) < 1e-9);  // 細砂 5.5〜6.5 m

            // 手計算: σ'(−2.5) = 14.7·0.8 + 14.7·0.7 + 15.7·1.0 − 10·1.0 = 27.75
            //         σ'(−3.5) = 27.75 + 17.7·1.0 − 10·1.0 = 35.45
            Assert.AreEqual(27.75, a.StressTop, 1e-9);
            Assert.AreEqual(35.45, a.StressBtm, 1e-9);
            Assert.AreEqual(31.6, a.StressMid, 1e-9);
            Assert.AreEqual(31.6 * 0.5, a.P0, 1e-9);
            Assert.AreEqual(31.6 * Kp(a.Phi) + 2 * a.C * Math.Sqrt(Kp(a.Phi)), a.Pp, 1e-9);
            Assert.AreEqual(ground.GetFrictionAngle(19.4, 31.6), a.Phi, 1e-9, "内部摩擦角も同じ土被り圧から求める");

            // 同じ厚さ・同じ土層で、深い区間の方が土圧が大きい (以前は等しかった)
            Assert.AreEqual(a.DZ, b.DZ, 1e-9);
            Assert.IsTrue(b.P0 > a.P0 && b.Pp - b.P0 > a.Pp - a.P0,
                $"深い区間の土圧が大きくなっていません (浅 p0={a.P0:F2}, pp−p0={a.Pp - a.P0:F2} / 深 p0={b.P0:F2}, pp−p0={b.Pp - b.P0:F2})");
            Assert.AreEqual(0.0, a.Q, "上載荷重は入力欄がないので 0");
        }

        /// <summary>
        /// 地中応力計算用標高を根切り底に置いても、外壁の外側の地盤の土圧は地表面から数える。
        /// 杭と同じく応力計算面から数えると、それより上の区間の土圧が 0 になる。
        /// </summary>
        [TestMethod]
        public void BuiltSprings_IgnoreTheStressPlaneForPiles()
        {
            var built = Build(Zs, g => g.StressAltitude = -6.5);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, ground) = built.Value;
            Assert.AreEqual(-6.5, ground.StressAltitude, 1e-9, "(前提) 応力計算面を下げられていません");

            var a = vm.DoatsuGoryokuBane.Items.Single(i => Math.Abs(i.ZTop - -2.5) < 1e-9);
            Assert.AreEqual(31.6, a.StressMid, 1e-9, "応力計算面から数えています");
            Assert.IsTrue(a.Pp - a.P0 > 0);
        }

        /// <summary>
        /// 文献照合: 基礎指針'19 例図9.4 (土圧合力ばね P_Et·W/Np、杭 1 本あたり) は、変位 0.1 m で約 1,090 kN、
        /// 0.3 m で約 1,590 kN (図からの読み取り)。根入部を土層境界で区切った計算例9 の Y 方向 (受圧幅 36.8 m、杭 18 本) は
        /// 本実装で 0.1 m 1,071 kN・0.3 m 1,578 kN になり、読み取りの誤差の範囲で合う。
        /// 以前の γh (h = 区間の厚さ) では 0.1 m 592 kN・0.3 m 872 kN で、図の 55% 程度しかなかった。
        /// </summary>
        [TestMethod]
        public void Example9_ReproducesShishin19Fig9_4()
        {
            var built = Build(Zs);
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;
            var bane = vm.DoatsuGoryokuBane;
            Assert.AreEqual(0.325, bane.DeltaP, 1e-9, "(前提) Δp = 0.05 × 根入れ深さ 6.5 m");

            double PerPile(double y) => bane.Items.Sum(it => it.GetPressure(y) * it.DZ * it.DX) / 18.0;
            Assert.AreEqual(1090, PerPile(0.1), 1090 * 0.05, "例図9.4 の 0.1 m の値と合いません");
            Assert.AreEqual(1590, PerPile(0.3), 1590 * 0.05, "例図9.4 の 0.3 m の値と合いません");
        }
    }
}
