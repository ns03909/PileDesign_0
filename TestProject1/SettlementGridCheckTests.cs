using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下のコンタ図の格子の入力を、解く前に確かめること (2026-09-27 のレビュー)。
    /// 1. 間隔が 0 以下なら止める (以前は格子を最小値の 1 点だけにして解き、コンタ図の範囲が黙って消えた)
    /// 2. 格子点の数を作る前に見積もり、上限を超えたら目安の間隔と一緒に知らせる
    /// </summary>
    [TestClass]
    public class SettlementGridCheckTests
    {
        private static ObservableCollection<GridDataItem> Grid(params double[] coords)
        {
            var items = new ObservableCollection<GridDataItem>();
            foreach (var c in coords)
            {
                var item = new GridDataItem();
                // 座標のセッターは数値でない値を拒む。解析側の守りを確かめるため、そのときはフィールドへ直接入れる
                if (double.IsFinite(c)) item.Coord = c;
                else typeof(GridDataItem).GetField("_coord", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                         .SetValue(item, c);
                items.Add(item);
            }
            return items;
        }

        [TestMethod]
        public void TheEstimateMatchesTheGridThatIsBuilt()
        {
            foreach (var (min, max, offset, spacing, grid) in new[]
            {
                (0.0, 10.0, 2.0, 1.8, Grid()),
                (0.0, 10.0, 2.0, 1.8, Grid(3.0, 7.5)),
                (-5.0, 5.0, 0.0, 0.7, Grid(-5.0, 0.0, 5.0)),
                (0.0, 0.0, 1.0, 5.0, Grid()),
            })
            {
                int built = PileGroupSettlement.GetCoord(min, max, offset, spacing, grid).Count;
                Assert.AreEqual(built, PileGroupSettlement.EstimateCoordCount(min, max, offset, spacing, grid), 1e-9,
                    $"見積もりが作った格子と合いません (min {min} max {max} 余裕 {offset} 間隔 {spacing})");
            }
        }

        private static string? Check(double xSpacing, double ySpacing)
            => SettlementAnalysisService.DescribeGridProblem(0, 20, 0, 20, 2, 2, xSpacing, ySpacing, Grid(), Grid());

        [TestMethod]
        public void NonFiniteRangeOrGridLineIsRejectedBeforeCounting()
        {
            string? badRange = SettlementAnalysisService.DescribeGridProblem(
                double.NaN, 20, 0, 20, 2, 2, 1, 1, Grid(), Grid());
            StringAssert.Contains(badRange, "X 範囲");

            string? badLine = SettlementAnalysisService.DescribeGridProblem(
                0, 20, 0, 20, 2, 2, 1, 1, Grid(3, double.PositiveInfinity), Grid());
            StringAssert.Contains(badLine, "X 通り芯 2 番目");
            Assert.IsTrue(double.IsNaN(PileGroupSettlement.EstimateCoordCount(
                0, 20, 2, 1, Grid(3, double.PositiveInfinity))));
            Assert.ThrowsException<ArgumentException>(() => PileGroupSettlement.GetCoord(
                0, 20, 2, 1, Grid(3, double.PositiveInfinity)));
        }

        [TestMethod]
        public void ANonPositiveSpacingIsAnInputError()
        {
            Assert.IsNull(Check(1.8, 1.8), "普通の入力を止めています");

            StringAssert.Contains(Check(0, 1.8), "X 間隔が 0 以下か数値ではありません");
            StringAssert.Contains(Check(1.8, -1), "Y 間隔が 0 以下か数値ではありません");
            StringAssert.Contains(Check(double.NaN, 1.8), "X間隔(m)");
            StringAssert.Contains(Check(double.PositiveInfinity, 1.8), "X 間隔");
        }

        [TestMethod]
        public void TooManyPointsAreRefusedWithASuggestedSpacing()
        {
            string? tooMany = Check(0.01, 0.01);   // 約 2400 × 2400 点
            Assert.IsNotNull(tooMany, "格子点の上限を見ていません");
            StringAssert.Contains(tooMany, "格子点が多すぎます");
            StringAssert.Contains(tooMany, "目安");

            // 目安の間隔にすれば上限に収まる
            double suggested = 0.01 * Math.Sqrt(PileGroupSettlement.EstimateCoordCount(0, 20, 2, 0.01, Grid())
                * PileGroupSettlement.EstimateCoordCount(0, 20, 2, 0.01, Grid()) / SettlementAnalysisService.MaxGridPoints);
            Assert.IsNull(Check(suggested * 1.01, suggested * 1.01), "目安の間隔でもまだ上限を超えます");
        }

        /// <summary>格子を作る前に確かめること (群杭沈下・基礎梁考慮・画面の格子の描画)。</summary>
        [TestMethod]
        public void TheGridIsCheckedBeforeItIsBuilt()
        {
            string service = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Services", "SettlementAnalysisService.cs"),
                "public SettlementAnalysisResult PerformSettlementAnalysis(");
            int check = service.IndexOf("DescribeGridProblem(", StringComparison.Ordinal);
            int build = service.IndexOf("SetGridX(", StringComparison.Ordinal);
            Assert.IsTrue(check >= 0 && check < build, "群杭沈下解析が格子を作る前に確かめていません");

            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "GroupSettlementWithBeamCalculationViewModel.cs"),
                "SettlementAnalysisService.DescribeGridProblem(", "基礎梁考慮の群杭沈下解析が格子を確かめていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasSettlement.cs"),
                "DescribeGridProblem(", "画面の格子の描画が、点の多すぎる格子を作ります");
        }
    }
}
