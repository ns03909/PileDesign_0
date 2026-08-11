using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// Q-N 曲線 (低減後) の β2 切替 (σ0=(1/3)ξFc で 0.75→0.65) が
    /// 「同一 N の複製点による垂直な段差」として描かれることの検証。
    /// 従来はサンプリング区間で斜めに繋がっていた (2026-08-11 計算例9 で指摘)。
    /// </summary>
    [TestClass]
    public class QnBetaStepTests
    {
        // 計算例10 相当 (D=1500 / Fc27 / 30-D29)
        private static PileSection CreateInsituRcSection() => new()
        {
            PileBodyType = "場所打ち鉄筋コンクリート杭",
            PileSectionType = "鉄筋コンクリート部",
            ConcreteOutDia = 1500.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
        };

        [TestMethod]
        public void FactoredUltimateNQ_HasVerticalStepAtBeta2Threshold()
        {
            var section = CreateInsituRcSection();
            var (n, q) = section.FactoredUltimateNQ;
            Assert.IsTrue(n.Count > 10, "QN 曲線が生成されていない");

            // 同一 N の複製点 (垂直段差) を探す
            int stepIdx = -1;
            for (int i = 0; i < n.Count - 1; i++)
            {
                if (n[i] == n[i + 1]) { stepIdx = i; break; }
            }
            Assert.IsTrue(stepIdx >= 0, "低減後安全限界 QN に閾値の複製点 (垂直段差) がない");

            // 段差は下向き (β2: 0.75 → 0.65 なので Q が減る)
            Assert.IsTrue(q[stepIdx] > q[stepIdx + 1],
                $"段差が下向きでない: Q={q[stepIdx]:F1} → {q[stepIdx + 1]:F1}");

            // 段差比はほぼ 0.65/0.75
            double ratio = q[stepIdx + 1] / q[stepIdx];
            Assert.AreEqual(0.65 / 0.75, ratio, 1e-6,
                $"段差比が β2 比 (0.65/0.75) と一致しない: {ratio:F6}");
        }

        [TestMethod]
        public void UnfactoredUltimateNQ_HasNoDuplicatePoints()
        {
            var section = CreateInsituRcSection();
            var (n, _) = section.UnfactoredUltimateNQ;
            Assert.IsTrue(n.Count > 10, "QN 曲線が生成されていない");

            for (int i = 0; i < n.Count - 1; i++)
            {
                Assert.AreNotEqual(n[i], n[i + 1], $"低減前曲線に複製点が入っている (i={i})");
            }
        }

        [TestMethod]
        public void FactoredUltimateNQ_IsSortedByN()
        {
            var section = CreateInsituRcSection();
            var (n, _) = section.FactoredUltimateNQ;

            for (int i = 0; i < n.Count - 1; i++)
            {
                Assert.IsTrue(n[i] <= n[i + 1], $"N が昇順でない (i={i}: {n[i]:F1} > {n[i + 1]:F1})");
            }
        }
    }
}
