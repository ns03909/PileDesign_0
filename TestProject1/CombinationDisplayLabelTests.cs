using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 利用者の声 (2026-11-07): 応力・変位図の凡例・見出しで、荷重組合せを番号と係数の両方で示す
    /// (以前は画面の凡例が番号だけ・計算書の図の題が係数だけで、4 つの組合せの図を見比べにくかった)。
    /// </summary>
    [TestClass]
    public class CombinationDisplayLabelTests
    {
        [TestMethod]
        public void CombinationDisplayLabelShowsNumberAndFactors()
        {
            var comb = new LoadCombination(2, -1.0, 0.5, -0.5);
            Assert.AreEqual("組合せ2（αL=-1.00, βU=0.50, βL=-0.50）", comb.DisplayLabel);
        }

        [TestMethod]
        public void GraphLegendsShowTheCombinationNumberAndFactors()
        {
            var comb = new LoadCombination(3, 1.0, -0.5, 0.5);
            var loadCase = new LoadCase { LoadName = "U1" };
            var pile = new PileLayoutDataItem { No = 7 };

            var pileLegend = typeof(GraphViewModel).GetMethod("GetPileLegendText", BindingFlags.NonPublic | BindingFlags.Static)!;
            var generalLegend = typeof(GraphViewModel).GetMethod("GetGeneralLegendText", BindingFlags.NonPublic | BindingFlags.Static)!;

            string p = (string)pileLegend.Invoke(null, [loadCase, comb, true, pile])!;
            string g = (string)generalLegend.Invoke(null, [loadCase, comb, false])!;

            StringAssert.Contains(p, comb.DisplayLabel);
            StringAssert.Contains(p, "杭7");
            StringAssert.Contains(p, "液状化考慮");
            StringAssert.Contains(g, comb.DisplayLabel);
            StringAssert.Contains(g, "液状化非考慮");
            Assert.IsFalse(p.Contains("LIQ:") || g.Contains("LIQ:"), "液状化が「LIQ:True」の形のまま");
        }
    }
}
