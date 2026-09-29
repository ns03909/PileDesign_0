using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.Generic;

namespace TestProject1
{
    [TestClass]
    public class SettlementGridRenderValidationTests
    {
        private static SettlementGridDataItem P(double x, double y, double settlement) =>
            new() { X = x, Y = y, Settlement = settlement };

        [TestMethod]
        public void SwappingPointValuesChangesTheFingerprint()
        {
            var points = new List<SettlementGridDataItem>
            {
                P(0, 0, 1), P(1, 0, 2), P(2, 0, 3),
            };
            var before = new SettlementGridFingerprint(points, 0, 1);
            points[0].Settlement = 3;
            points[2].Settlement = 1;
            var after = new SettlementGridFingerprint(points, 0, 1);

            Assert.AreNotEqual(before, after);
            Assert.AreEqual(before, new SettlementGridFingerprint(
                [P(0, 0, 1), P(1, 0, 2), P(2, 0, 3)], 0, 1));
        }

        [TestMethod]
        public void MissingOrDuplicatedPointIsRejectedBeforeDrawing()
        {
            double[] xs = [0, 1];
            double[] ys = [0, 1];
            var points = new List<SettlementGridDataItem>
            {
                P(0, 0, 1), P(1, 0, 2), P(0, 1, 3),
            };

            Assert.IsFalse(SettlementGridDataValidator.TryBuildGrid(points, xs, ys, out _, out var missing));
            StringAssert.Contains(missing, "X=1, Y=1");

            points.Add(P(0, 0, 4));
            Assert.IsFalse(SettlementGridDataValidator.TryBuildGrid(points, xs, ys, out _, out var duplicate));
            StringAssert.Contains(duplicate, "重複");

            points[3] = P(1, 1, 4);
            Assert.IsTrue(SettlementGridDataValidator.TryBuildGrid(points, xs, ys, out var grid, out _));
            Assert.AreEqual(4, grid![1, 1].Settlement);
        }

        [TestMethod]
        public void NearbyCoordinatesKeepTheirOwnValues()
        {
            double[] xs = [0, 0.0005];
            double[] ys = [0, 1];
            var points = new List<SettlementGridDataItem>
            {
                P(0, 0, 1), P(0.0005, 0, 9), P(0, 1, 2), P(0.0005, 1, 10),
            };

            Assert.IsTrue(SettlementGridDataValidator.TryBuildGrid(points, xs, ys, out var grid, out _));
            Assert.AreEqual(1, grid![0, 0].Settlement);
            Assert.AreEqual(9, grid[1, 0].Settlement);
        }
    }
}
