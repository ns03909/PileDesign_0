using System.Collections.Generic;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 杭頭 2 点間の変形角。
    ///
    /// すべての杭頭の組について θ = |Uz_i − Uz_j| / (2 点間の水平距離) を求め、
    /// その<b>最大値</b>を限界値と比べる。基礎の回転・不同沈下による変形角。
    /// 全ペアを表に並べると読めないので、検定は 1 条件につき最大値 1 件だけ出し、
    /// どの組で最大になったかを対象名で示す。
    /// </summary>
    [TestClass]
    public class PileHeadDeformationAngleTests
    {
        /// <summary>(杭No, X[m], Y[m], 鉛直変位[m])。</summary>
        private static (int, double, double, double) P(int no, double x, double y, double uz) => (no, x, y, uz);

        [TestMethod]
        public void PicksTheLargestAngleAmongAllPairs()
        {
            // 3 本並び。1-2 は 1mm/5m = 2e-4、2-3 は 4mm/5m = 8e-4、1-3 は 5mm/10m = 5e-4
            var heads = new List<(int PileNo, double X, double Y, double Uz)>
            {
                P(1, 0, 0, 0.000),
                P(2, 5, 0, -0.001),
                P(3, 10, 0, -0.005),
            };

            var max = EvaluationService.MaxDeformationAngle(heads);

            Assert.IsNotNull(max);
            Assert.AreEqual(8.0e-4, max!.Value.Angle, 1e-12, "最大の組を選んでいない");
            Assert.AreEqual(2, max.Value.PileNoA);
            Assert.AreEqual(3, max.Value.PileNoB);
        }

        /// <summary>斜めに離れた組でも、距離は 2 点間の水平距離で取ること。</summary>
        [TestMethod]
        public void UsesTheHorizontalDistanceBetweenTheTwoPiles()
        {
            var heads = new List<(int PileNo, double X, double Y, double Uz)>
            {
                P(1, 0, 0, 0.000),
                P(2, 3, 4, -0.010),   // 距離 5 m、差 10 mm → 2.0e-3
            };

            var max = EvaluationService.MaxDeformationAngle(heads);
            Assert.AreEqual(2.0e-3, max!.Value.Angle, 1e-12);
        }

        /// <summary>
        /// 同じ位置にある杭 (距離 0) の組は角が定義できないので飛ばすこと。
        /// 0 で割ると無限大が最大値として選ばれ、常に NG になる。
        /// </summary>
        [TestMethod]
        public void SkipsPairsAtTheSamePosition()
        {
            var heads = new List<(int PileNo, double X, double Y, double Uz)>
            {
                P(1, 0, 0, 0.000),
                P(2, 0, 0, -0.010),   // 同じ位置
                P(3, 5, 0, -0.001),
            };

            var max = EvaluationService.MaxDeformationAngle(heads);

            Assert.IsNotNull(max);
            Assert.IsTrue(double.IsFinite(max!.Value.Angle), "距離 0 の組で無限大になっている");
            Assert.AreEqual(1.8e-3, max.Value.Angle, 1e-12, "2-3 の組 (9mm/5m) が最大のはず");
        }

        [TestMethod]
        public void ReturnsNullWhenThereIsNoPair()
        {
            Assert.IsNull(EvaluationService.MaxDeformationAngle([]));
            Assert.IsNull(EvaluationService.MaxDeformationAngle([P(1, 0, 0, 0)]));
            Assert.IsNull(EvaluationService.MaxDeformationAngle([P(1, 0, 0, 0), P(2, 0, 0, -0.01)]),
                "距離 0 の組しか無いときに値を返している");
        }

        /// <summary>
        /// 沈下検討の対象の既定は「単杭＋群杭沈下」。
        /// 群杭沈下解析を実行していなければ群杭分は 0 なので、実質単杭沈下になる。
        /// </summary>
        [TestMethod]
        public void SettlementDesignBasis_DefaultsToSinglePlusGroup()
        {
            var f = new PileDesign.Models.InputData.FundamentalInput();

            Assert.IsTrue(f.SettlementDesignIncludesGroup, "既定が単杭＋群杭沈下になっていない");
            Assert.AreEqual("単杭＋群杭沈下", f.SettlementDesignBasisName);

            f.SettlementDesignIncludesGroup = false;
            Assert.AreEqual("単杭沈下", f.SettlementDesignBasisName,
                "対象を切り替えても名乗りが変わっていない");
        }

        /// <summary>基本設定の既定値。1/1000・1/200・1/143。</summary>
        [TestMethod]
        public void DefaultLimitsAreFixed()
        {
            var f = new PileDesign.Models.InputData.FundamentalInput();
            Assert.AreEqual(1.0e-3, f.ServiceDeformationAngleLimit, 1e-15, "使用限界");
            Assert.AreEqual(5.0e-3, f.DamageDeformationAngleLimit, 1e-15, "損傷限界");
            Assert.AreEqual(7.0e-3, f.UltimateDeformationAngleLimit, 1e-15, "終局限界");
        }

        /// <summary>基本設定で変えた値が検定に効くこと。</summary>
        [TestMethod]
        public void LimitsComeFromTheFundamentalInput()
        {
            var f = new PileDesign.Models.InputData.FundamentalInput
            {
                ServiceDeformationAngleLimit = 2.0e-3,
                DamageDeformationAngleLimit = 6.0e-3,
                UltimateDeformationAngleLimit = 9.0e-3,
            };

            Assert.AreEqual(2.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Service), 1e-15);
            Assert.AreEqual(6.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Damage), 1e-15);
            Assert.AreEqual(9.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Ultimate), 1e-15);
        }

        /// <summary>
        /// 旧いファイル (値を持たない) では既定値に落ちること。
        /// 0 のまま使うと、どんな変形角でも NG になる。
        /// </summary>
        [TestMethod]
        public void FallsBackToDefaultsWhenUnset()
        {
            var f = new PileDesign.Models.InputData.FundamentalInput
            {
                ServiceDeformationAngleLimit = 0.0,
                DamageDeformationAngleLimit = -1.0,
                UltimateDeformationAngleLimit = double.NaN,
            };

            Assert.AreEqual(1.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Service), 1e-15);
            Assert.AreEqual(5.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Damage), 1e-15);
            Assert.AreEqual(7.0e-3,
                EvaluationService.DeformationAngleLimitFor(f, EvaluationService.LimitState.Ultimate), 1e-15);

            Assert.AreEqual(1.0e-3,
                EvaluationService.DeformationAngleLimitFor(null, EvaluationService.LimitState.Service), 1e-15,
                "基本設定が無いときに既定値へ落ちていない");
        }
    }
}
