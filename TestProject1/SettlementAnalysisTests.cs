using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// Steinnbrener.CalcSettlement（多層地盤における矩形載荷面の沈下解析カーネル）のテスト。
    /// </summary>
    [TestClass]
    public class SteinnbrenerTests
    {
        private static ObservableCollection<SettlementSoilLayer> SingleLayer(double thickness = 10.0, double ek = 10_000.0, double nu = 0.3)
            => new() { new SettlementSoilLayer { Thickness = thickness, Ek = ek, PoissonsRatio = nu } };

        [TestMethod]
        public void CalcSettlement_EmptyLoads_ReturnsZero()
        {
            var s = Steinnbrener.CalcSettlement(new Point(0, 0), new ObservableCollection<RectLoad>(), SingleLayer());
            Assert.AreEqual(0.0, s, 1e-12);
        }

        [TestMethod]
        public void CalcSettlement_ZeroWidthRect_ReturnsZero()
        {
            // X1 == X2 の degenerate 矩形は寄与 0
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = 1, X2 = 1, Y1 = -1, Y2 = 1, QA = 100 }
            };
            var s = Steinnbrener.CalcSettlement(new Point(10, 10), loads, SingleLayer());
            Assert.AreEqual(0.0, s, 1e-12);
        }

        [TestMethod]
        public void CalcSettlement_ZeroHeightRect_ReturnsZero()
        {
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = 2, Y2 = 2, QA = 100 }
            };
            var s = Steinnbrener.CalcSettlement(new Point(0, 0), loads, SingleLayer());
            Assert.AreEqual(0.0, s, 1e-12);
        }

        [TestMethod]
        public void CalcSettlement_PointBelowSquareLoadCenter_IsPositive()
        {
            // 荷重矩形 [-1,1] × [-1,1]、面積 4、QA=400 ⇒ q=100、単層 Ek=10000, ν=0.3
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
            };
            var s = Steinnbrener.CalcSettlement(new Point(0, 0), loads, SingleLayer());
            Assert.IsTrue(s > 0, $"中央点の沈下は正である必要がある (actual: {s})");
        }

        [TestMethod]
        public void CalcSettlement_SymmetricPointsGiveEqualSettlement()
        {
            // 対称な荷重矩形に対して、対称な観測点は同じ沈下を持つ
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
            };
            var soil = SingleLayer();

            double sPlus = Steinnbrener.CalcSettlement(new Point(5, 0), loads, soil);
            double sMinus = Steinnbrener.CalcSettlement(new Point(-5, 0), loads, soil);
            Assert.AreEqual(sPlus, sMinus, 1e-9);

            double sUp = Steinnbrener.CalcSettlement(new Point(0, 5), loads, soil);
            double sDown = Steinnbrener.CalcSettlement(new Point(0, -5), loads, soil);
            Assert.AreEqual(sUp, sDown, 1e-9);
        }

        [TestMethod]
        public void CalcSettlement_CenterGreaterThanFarPoint()
        {
            // 中央点の沈下 > 遠方点の沈下 (単調性)
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
            };
            var soil = SingleLayer();
            double sCenter = Steinnbrener.CalcSettlement(new Point(0, 0), loads, soil);
            double sFar = Steinnbrener.CalcSettlement(new Point(100, 100), loads, soil);
            Assert.IsTrue(sCenter > sFar, $"中央 {sCenter} <= 遠方 {sFar}");
            // 遠方では数値ノイズで微小負値が出ることを許容（|s| < 1e-6）
            Assert.IsTrue(System.Math.Abs(sFar) < 1e-6, $"遠方は 0 近傍 (actual: {sFar})");
        }

        [TestMethod]
        public void CalcSettlement_StifferSoilGivesSmallerSettlement()
        {
            // Ek を大きくすると沈下は小さくなる
            var loads = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
            };
            double soft = Steinnbrener.CalcSettlement(new Point(0, 0), loads, SingleLayer(ek: 5_000));
            double stiff = Steinnbrener.CalcSettlement(new Point(0, 0), loads, SingleLayer(ek: 50_000));
            Assert.IsTrue(soft > stiff, $"soft {soft} <= stiff {stiff}");
        }

        [TestMethod]
        public void CalcSettlement_DoubleLoadGivesDoubleSettlement()
        {
            // 線形性: q を 2 倍にすると沈下も 2 倍（単層・中央点）
            var load1 = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
            };
            var load2 = new ObservableCollection<RectLoad>
            {
                new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 800 }
            };
            var soil = SingleLayer();
            double s1 = Steinnbrener.CalcSettlement(new Point(0, 0), load1, soil);
            double s2 = Steinnbrener.CalcSettlement(new Point(0, 0), load2, soil);
            Assert.AreEqual(2 * s1, s2, 1e-9);
        }
    }

    /// <summary>
    /// SettlementAnalysisService.PerformSettlementAnalysis のテスト。
    /// </summary>
    [TestClass]
    public class SettlementAnalysisServiceTests
    {
        [TestMethod]
        public void PerformSettlementAnalysis_NoSoilLayers_ReturnsFailureWithMessage()
        {
            var service = new SettlementAnalysisService();
            var pgs = new PileGroupSettlement(); // SettlementSoilLayers は空で初期化される

            var result = service.PerformSettlementAnalysis(
                pgs,
                pileLayoutItems: [],
                soilPiles: [],
                gridXItems: [],
                gridYItems: [],
                xMin: 0, xMax: 0, yMin: 0, yMax: 0,
                xOffset: 0, yOffset: 0, xSpacing: 1, ySpacing: 1);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ErrorMessage);
            StringAssert.Contains(result.ErrorMessage, "土層");
        }

        [TestMethod]
        public void PerformSettlementAnalysis_NullSoilLayers_ReturnsFailure()
        {
            var service = new SettlementAnalysisService();
            var pgs = new PileGroupSettlement { SettlementSoilLayers = null! };

            var result = service.PerformSettlementAnalysis(
                pgs, [], [], [], [],
                0, 0, 0, 0, 0, 0, 1, 1);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ErrorMessage);
        }

        [TestMethod]
        public void PerformSettlementAnalysis_ArbitraryRectLoad_ProducesGridData()
        {
            var service = new SettlementAnalysisService();
            var pgs = new PileGroupSettlement
            {
                LoadingType = "任意矩形",
                RectLoads = new ObservableCollection<RectLoad>
                {
                    new() { X1 = -1, X2 = 1, Y1 = -1, Y2 = 1, QA = 400 }
                },
                SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>
                {
                    new() { Thickness = 10.0, Ek = 10_000.0, PoissonsRatio = 0.3 }
                }
            };

            var result = service.PerformSettlementAnalysis(
                pgs,
                pileLayoutItems: [],
                soilPiles: [],
                gridXItems: [],
                gridYItems: [],
                xMin: -2, xMax: 2,
                yMin: -2, yMax: 2,
                xOffset: 0, yOffset: 0,
                xSpacing: 1, ySpacing: 1);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.SettlementGridData);
            Assert.IsTrue(result.SettlementGridData.Count > 0, "グリッド点は 1 点以上生成されるはず");

            // 中央 (0,0) 付近の沈下は正値
            var near = result.SettlementGridData
                .Where(d => System.Math.Abs(d.X) < 1e-6 && System.Math.Abs(d.Y) < 1e-6)
                .ToList();
            if (near.Count > 0)
                Assert.IsTrue(near[0].Settlement > 0, $"中央点の沈下は正 (actual: {near[0].Settlement})");
        }
    }
}
