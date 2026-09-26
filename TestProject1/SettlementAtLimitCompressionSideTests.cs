using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 限界支持力時の杭頭沈下量を、荷重-沈下曲線の<b>圧縮側</b>だけから求めること (2026-09-27 のレビュー)。
    ///
    /// 単杭沈下の解析は圧縮・引抜きの両方向を 1 つの曲線 (LoadDisplacements) に記録する (圧縮が正の荷重)。
    /// 限界時の沈下量は荷重を絶対値にしてから補間していたので、同じ大きさの荷重に引抜き側の点があると
    /// その変位を拾い得た。圧縮と引抜きで同じ絶対荷重を持ち、変位が違う小さな曲線で確かめる。
    /// </summary>
    [TestClass]
    public class SettlementAtLimitCompressionSideTests
    {
        /// <summary>
        /// Ru = 3000 kN (→ R_SLS 1000 / R_DLS 2000 / R_ULS 3000)。
        /// 圧縮側は 1000 / 2000 / 3000 kN で 2 / 5 / 9 mm、引抜き側は同じ大きさで 10 / 20 / 30 mm。
        /// </summary>
        private static SoilPile PileWithTwoSidedCurve()
        {
            var pile = new SoilPile { Rfu = 3000 };
            foreach (var (load, disp) in new[]
            {
                (-3000.0, -30.0), (-2000.0, -20.0), (-1000.0, -10.0),
                (0.0, 0.0),
                (1000.0, 2.0), (2000.0, 5.0), (3000.0, 9.0),
            })
            {
                pile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = load, D0s = disp });
            }
            return pile;
        }

        [TestMethod]
        public void LimitSettlementsUseOnlyTheCompressionSide()
        {
            var pile = PileWithTwoSidedCurve();
            Assert.AreEqual(1000, pile.R_SLS, 1e-9, "(前提) R_SLS");

            Assert.AreEqual(2.0, pile.SettlementAtR_SLS, 1e-9, "使用限界時の沈下量に引抜き側の変位が混ざっています");
            Assert.AreEqual(5.0, pile.SettlementAtR_DLS, 1e-9, "損傷限界時の沈下量に引抜き側の変位が混ざっています");
            Assert.AreEqual(9.0, pile.SettlementAtR_ULS, 1e-9, "終局限界時の沈下量に引抜き側の変位が混ざっています");
        }

        [TestMethod]
        public void BetweenPointsItInterpolatesOnTheCompressionSide()
        {
            var pile = new SoilPile { Rfu = 4500 };   // R_SLS = 1500 → 圧縮側 1000..2000 の中点 = 3.5 mm
            foreach (var (load, disp) in new[] { (-2000.0, -20.0), (-1000.0, -10.0), (0.0, 0.0), (1000.0, 2.0), (2000.0, 5.0), (5000.0, 30.0) })
                pile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = load, D0s = disp });

            Assert.AreEqual(3.5, pile.SettlementAtR_SLS, 1e-9);
        }

        // ── 曲線の範囲外 (2 件目) ─────────────────────────────

        /// <summary>限界支持力が曲線の最大荷重を超えたら、端の値を下限として示す (普通の計算値と区別する)。</summary>
        [TestMethod]
        public void BeyondTheCurveTheLimitSettlementIsMarkedAsALowerBound()
        {
            var pile = PileWithTwoSidedCurve();
            pile.Rfu = 4500;   // R_ULS 4500 > 曲線の最大 3000

            var uls = pile.CompressionSettlementAt(pile.R_ULS);
            Assert.IsTrue(uls.BeyondCurve, "曲線の範囲外なのに印が付いていません");
            Assert.AreEqual(9.0, uls.Value, 1e-9, "端の値 (圧縮側) を返すこと");
            StringAssert.Contains(pile.SettlementAtR_ULSText, "範囲外");
            StringAssert.Contains(pile.SettlementAtR_ULSText, "≧9.0");

            Assert.IsFalse(pile.CompressionSettlementAt(pile.R_SLS).BeyondCurve, "範囲内 (1500 kN) に印が付いています");
            Assert.AreEqual("3.5", pile.SettlementAtR_SLSText);
        }

        [TestMethod]
        public void BeyondTheCurveTheSettlementCheckCannotSayOk()
        {
            PileLayoutDataItem Pile(int no, double settlement_m, bool beyond) => new()
            {
                PileNo = no, SinglePileSettlementVL = settlement_m, SinglePileSettlementVLBeyondCurve = beyond,
            };
            var input = new InputModel
            {
                FundamentalInput = new FundamentalInput { EvaluateSettlement = true, AllowableSettlement_mm = 20 },
                PileLayoutItems = [Pile(1, 0.010, beyond: true), Pile(2, 0.030, beyond: true), Pile(3, 0.010, beyond: false)],
            };

            var items = PileDesign.Services.PileSettlementEvaluator.Evaluate(input);
            var byPile = items.ToDictionary(i => i.PileNo!.Value);

            Assert.IsTrue(byPile[1].IsUnavailable, "下限 10 mm で許容 20 mm 以下を OK としています (実際はもっと沈む)");
            StringAssert.Contains(byPile[1].UnavailableReason, "下限");
            Assert.IsTrue(byPile[2].IsJudged && !byPile[2].IsOk, "下限 30 mm で許容値を超えているのに NG と判定していません");
            Assert.IsTrue(byPile[3].IsJudged && byPile[3].IsOk, "範囲内の杭まで判定をやめています");
        }

        [TestMethod]
        public void TheDeformationAngleIsNotJudgedFromLowerBounds()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Services", "SettlementDeformationAngleEvaluator.cs"),
                "public static List<EvaluationItem> Evaluate(");
            StringAssert.Contains(body, "SinglePileSettlementVLBeyondCurve", "変形角が、範囲外 (下限) の単杭沈下量をそのまま使っています");
            StringAssert.Contains(body, "items.Add(Unavailable(caseName, beyondReason));");
        }
    }
}
