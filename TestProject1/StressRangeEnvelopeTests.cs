using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 利用者の声 (2026-11-07): N-M 図に地震時の点を囲む包絡線 (応力範囲) を既定で描く。
    /// </summary>
    [TestClass]
    public class StressRangeEnvelopeTests
    {
        [TestMethod]
        public void HullEnclosesAllPointsAndDropsInteriorOnes()
        {
            double[] xs = [0, 10, 10, 0, 5, 3];
            double[] ys = [0, 0, 8, 8, 4, 2];
            var (hx, hy) = StressRangeEnvelope.Hull(xs, ys);

            // 四隅の 4 点 + 閉じるための最初の点
            Assert.AreEqual(5, hx.Length);
            Assert.AreEqual(hx[0], hx[^1]);
            Assert.AreEqual(hy[0], hy[^1]);
            var corners = Enumerable.Range(0, 4).Select(i => (hx[i], hy[i])).ToHashSet();
            CollectionAssert.AreEquivalent(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 8.0), (0.0, 8.0) }, corners.ToArray());
            Assert.IsFalse(corners.Contains((5.0, 4.0)), "内側の点が頂点に入っています");

            // 反時計回り (符号付き面積が正)
            double area2 = 0;
            for (int i = 0; i < hx.Length - 1; i++) area2 += hx[i] * hy[i + 1] - hx[i + 1] * hy[i];
            Assert.AreEqual(160.0, area2, 1e-9);
        }

        [TestMethod]
        public void HullOfCollinearPointsIsTheSegmentBetweenTheEnds()
        {
            var (hx, hy) = StressRangeEnvelope.Hull([300, 100, 200], [0, 0, 0]);
            CollectionAssert.AreEqual(new[] { 100.0, 300.0 }, hx);
            CollectionAssert.AreEqual(new[] { 0.0, 0.0 }, hy);
        }

        [TestMethod]
        public void HullIsEmptyWhenThereIsNothingToEnclose()
        {
            Assert.AreEqual(0, StressRangeEnvelope.Hull([], []).X.Length);
            Assert.AreEqual(0, StressRangeEnvelope.Hull([5, 5], [1, 1]).X.Length, "同じ点が 2 つでも囲むものはありません");
            Assert.AreEqual(0, StressRangeEnvelope.Hull([5, double.NaN], [1, 2]).X.Length, "数でない点を除くと 1 点です");
        }

        [TestMethod]
        public void HullSkipsNonFinitePoints()
        {
            var (hx, hy) = StressRangeEnvelope.Hull([0, 1, 0, double.PositiveInfinity], [0, 0, 1, 5]);
            Assert.AreEqual(4, hx.Length);
            Assert.IsTrue(hx.All(double.IsFinite) && hy.All(double.IsFinite));
        }

        [TestMethod]
        public void AddToDrawsADottedLineInTheGivenColor()
        {
            var plot = new ScottPlot.Plot();
            var color = ScottPlot.Colors.Red;
            var line = StressRangeEnvelope.AddTo(plot, [0, 10, 5], [0, 0, 8], color, "レベル2 応力範囲");

            Assert.IsNotNull(line);
            Assert.AreEqual("レベル2 応力範囲", line!.LegendText);
            Assert.AreEqual(color, line.LineColor);
            Assert.AreEqual(ScottPlot.LinePattern.Dotted, line.LinePattern);
            Assert.AreEqual(0, line.MarkerSize);

            Assert.IsNull(StressRangeEnvelope.AddTo(new ScottPlot.Plot(), [1], [1], color, "x"), "1 点では描きません");
        }

        [TestMethod]
        public void ReportDrawsTheEnvelopeByDefault()
        {
            var vm = new MainWindowViewModel { IsHorizontalAnalysisDone = true };
            Assert.IsTrue(vm.DocxOutput.IncludeHorizontal_NMinTEnvelope, "計算書の N-M 図の包絡線が既定で描かれません");
        }

        [TestMethod]
        public void GraphDrawsTheEnvelopeByDefault()
        {
            var gvm = new GraphViewModel(new MainWindowViewModel());
            Assert.IsTrue(gvm.IsStressRangeEnvelopeVisible, "グラフの N-M 図の包絡線が既定で描かれません");
        }
    }
}
