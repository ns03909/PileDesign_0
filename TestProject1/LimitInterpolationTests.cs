using System.Collections.Generic;
using System.Linq;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 限界曲線から軸力に対応する限界値を読む処理を固定する。
    ///
    /// N-M 曲線は<b>閉じた包絡線</b>で、1 つの軸力を圧縮側と引張側で 2 度横切る。
    /// 軸力制限を入れた曲線では、境界の垂直線でさらに横切る。
    /// 検定は「その軸力で発揮できる最大の曲げ」を見るので、横切った区間の最大値を採る。
    ///
    /// かつては経路ごとに別々の実装だった。計算書の限界線だけ
    /// 「N で並べ替えて最初に挟まる区間を採る」実装で、
    /// 圧縮側と引張側の点を結んだ値を返しうる形だった（同じ軸力で検定と違う限界値になる）。
    /// さらに範囲外の軸力を端の値へ丸めており、軸力制限の外に「端の耐力がある」線を引いていた。
    /// </summary>
    [TestClass]
    public class LimitInterpolationTests
    {
        /// <summary>閉じた N-M 曲線（引張側 → 圧縮側 → 引張側に戻る）を模したもの。</summary>
        private static (List<double> N, List<double> M) ClosedCurve() =>
        (
            //        下枝 (小さい M) を N 昇順 → 折り返して上枝 (大きい M) を N 降順
            [-500, 0, 1000, 2000, 3000, 2000, 1000, 0, -500],
            [0, 300, 500, 600, 700, 900, 800, 400, 0]
        );

        [TestMethod]
        public void PicksTheLargestBranchAtTheSameAxialForce()
        {
            var (n, m) = ClosedCurve();

            // N=1000 は 2 度横切る（下枝 500 / 上枝 800）。包絡なので大きい方。
            Assert.AreEqual(800.0, PileSection.InterpolateLimitAtAxialForce(n, m, 1000), 1e-9,
                "同じ軸力を複数回横切るとき、最大の枝を採っていない");

            // N=2000 も 2 度（600 / 900）
            Assert.AreEqual(900.0, PileSection.InterpolateLimitAtAxialForce(n, m, 2000), 1e-9);
        }

        [TestMethod]
        public void InterpolatesLinearlyInsideASegment()
        {
            var (n, m) = ClosedCurve();

            // N=500 は下枝 (0→1000 で 300→500) と上枝 (1000→0 で 800→400) の 2 区間に挟まる。
            // 下枝 400 / 上枝 600 なので 600。
            Assert.AreEqual(600.0, PileSection.InterpolateLimitAtAxialForce(n, m, 500), 1e-9);
        }

        [TestMethod]
        public void ReturnsNaNOutsideTheCurve()
        {
            var (n, m) = ClosedCurve();

            Assert.IsTrue(double.IsNaN(PileSection.InterpolateLimitAtAxialForce(n, m, 5000)),
                "軸力制限の外で端の値を返している（そこに耐力があると読めてしまう）");
            Assert.IsTrue(double.IsNaN(PileSection.InterpolateLimitAtAxialForce(n, m, -1000)),
                "引張側の範囲外で端の値を返している");
        }

        [TestMethod]
        public void TakesTheLargerValueOnAVerticalSegment()
        {
            // 軸力制限の境界に入る垂直な区間 (同じ N で M=0 → M=700)
            List<double> n = [1000, 1000, 2000];
            List<double> m = [0, 700, 500];

            Assert.AreEqual(700.0, PileSection.InterpolateLimitAtAxialForce(n, m, 1000), 1e-9,
                "垂直区間で 0 側を拾っている");
        }

        [TestMethod]
        public void HandlesEmptyAndMismatchedInput()
        {
            Assert.IsTrue(double.IsNaN(PileSection.InterpolateLimitAtAxialForce(null, null, 0)));
            Assert.IsTrue(double.IsNaN(PileSection.InterpolateLimitAtAxialForce([1.0], [1.0], 1)));
            Assert.IsTrue(double.IsNaN(PileSection.InterpolateLimitAtAxialForce([1.0, 2.0], [1.0], 1)),
                "点数が食い違う曲線で値を返している");
        }

        /// <summary>
        /// 実際の断面の N-M 曲線でも、圧縮側で複数回横切ることがある。
        /// そのとき最大の枝を採ることを、実データで確かめる。
        /// </summary>
        [TestMethod]
        public void RealSection_MaxBranchIsNotSmallerThanAnyCrossing()
        {
            var s = ShearAxialDependenceTableTests.CreateInsituRcSectionForCurveTests();
            var nm = s.FactoredUltimateNM;
            Assert.IsTrue(nm.N.Count > 2, "N-M 曲線が作れていない");

            double nMin = nm.N.Min(), nMax = nm.N.Max();
            for (int k = 1; k < 20; k++)
            {
                double target = nMin + (nMax - nMin) * k / 20.0;
                double limit = PileSection.InterpolateLimitAtAxialForce(nm.N, nm.M, target);
                Assert.IsFalse(double.IsNaN(limit), $"範囲内 (N={target:F0}) で値が返らない");

                // 横切ったどの区間の値より小さくないこと
                for (int i = 0; i < nm.N.Count - 1; i++)
                {
                    double n0 = nm.N[i], n1 = nm.N[i + 1];
                    if (target < System.Math.Min(n0, n1) || target > System.Math.Max(n0, n1)) continue;
                    double dn = n1 - n0;
                    double v = System.Math.Abs(dn) < 1e-10
                        ? System.Math.Max(nm.M[i], nm.M[i + 1])
                        : nm.M[i] + (nm.M[i + 1] - nm.M[i]) * (target - n0) / dn;
                    Assert.IsTrue(limit >= v - 1e-6,
                        $"N={target:F0} で最大の枝を採っていない (返り値 {limit:F1} < 区間値 {v:F1})");
                }
            }
        }
    }
}
