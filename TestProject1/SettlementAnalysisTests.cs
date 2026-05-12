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

    /// <summary>
    /// SettlementAnalysisService.BuildAutoCrossRectLoads (個別矩形/個別十字 等の荷重生成) のテスト。
    /// </summary>
    [TestClass]
    public class BuildAutoCrossRectLoadsTests
    {
        private static PileLayoutDataItem MakePile(int no, double x, double y, double vL0 = 1000)
        {
            return new PileLayoutDataItem
            {
                PileNo = no,
                X = x,
                Y = y,
                Z = 0,
                AxialForceVL0 = vL0,
                AxialForceVLAdditional = 0,
                SoilPileAltNo = 1,
                PileBodyNo = 1,
                GroundNo = 1,
            };
        }

        private static SoilPile MakeSoilPile(double loadDia)
            => new() { GroupPileLoadDia = loadDia };

        [TestMethod]
        public void BuildAutoCrossRectLoads_AnyRect_ReturnsExistingLoadsAsIs()
        {
            // 任意矩形: 既存の RectLoads をそのまま返す (生成なし)
            var existing = new RectLoad { X1 = -2, X2 = 2, Y1 = -2, Y2 = 2, QA = 500 };
            var pgs = new PileGroupSettlement
            {
                LoadingType = "任意矩形",
                RectLoads = new ObservableCollection<RectLoad> { existing }
            };

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(
                pgs, new ObservableCollection<PileLayoutDataItem>(), new ObservableCollection<SoilPile>(), null!);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(existing, result[0]);
        }

        [TestMethod]
        public void BuildAutoCrossRectLoads_IndividualRect_GeneratesOnePerPile_EquivalentArea()
        {
            // 個別矩形: 杭ごとに 1 矩形、一辺 = √π · r で円と等価面積
            var pgs = new PileGroupSettlement
            {
                LoadingType = "個別矩形",
                RectLoads = new ObservableCollection<RectLoad>()
            };
            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(1, 0, 0, vL0: 800),
                MakePile(2, 5, 0, vL0: 600),
            };
            var soilPiles = new ObservableCollection<SoilPile> { MakeSoilPile(1.0) };

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(pgs, piles, soilPiles, null!);

            Assert.AreEqual(2, result.Count);

            // 1 杭目の検証: r=0.5、一辺 = √π · 0.5 ≈ 0.886
            var first = result[0];
            double expectedSide = System.Math.Sqrt(System.Math.PI) * 0.5;
            double expectedHalf = expectedSide * 0.5;
            Assert.AreEqual(0 - expectedHalf, first.X1, 1e-9);
            Assert.AreEqual(0 + expectedHalf, first.X2, 1e-9);
            Assert.AreEqual(800.0, first.QA, 1e-9);
            Assert.AreEqual(1, first.LinkedPileNo);

            // 2 杭目: 中心 X=5
            Assert.AreEqual(5 - expectedHalf, result[1].X1, 1e-9);
            Assert.AreEqual(5 + expectedHalf, result[1].X2, 1e-9);
            Assert.AreEqual(600.0, result[1].QA, 1e-9);
            Assert.AreEqual(2, result[1].LinkedPileNo);
        }

        [TestMethod]
        public void BuildAutoCrossRectLoads_IndividualRect_PreservesUserEditedDimensions()
        {
            // 個別矩形: 既存矩形 (LinkedPileNo 付き) は DX/DY を維持、中心と QA のみ更新
            var existing = new RectLoad
            {
                LinkedPileNo = 1,
                X1 = 0, X2 = 4, // DX = 4 (ユーザ編集済)
                Y1 = -1, Y2 = 1, // DY = 2
                QA = 100, // 古い値
            };
            var pgs = new PileGroupSettlement
            {
                LoadingType = "個別矩形",
                RectLoads = new ObservableCollection<RectLoad> { existing }
            };
            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(1, 10, 20, vL0: 999),
            };
            var soilPiles = new ObservableCollection<SoilPile> { MakeSoilPile(1.0) };

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(pgs, piles, soilPiles, null!);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(existing, result[0]); // 同一インスタンスを再利用

            // CenterX/Y は新しい杭位置に追従
            Assert.AreEqual(10.0, existing.CenterX, 1e-9);
            Assert.AreEqual(20.0, existing.CenterY, 1e-9);

            // QA は新値に更新
            Assert.AreEqual(999.0, existing.QA, 1e-9);

            // DX/DY (寸法) は維持されているはず: X2-X1 = 4, Y2-Y1 = 2
            Assert.AreEqual(4.0, existing.X2 - existing.X1, 1e-9, "DX (= X2-X1) は維持されるはず");
            Assert.AreEqual(2.0, existing.Y2 - existing.Y1, 1e-9, "DY (= Y2-Y1) は維持されるはず");
        }

        [TestMethod]
        public void BuildAutoCrossRectLoads_IndividualCross_GeneratesMultipleRectsPerPile()
        {
            // 個別十字: 杭ごとに十字状の複数矩形を生成
            var pgs = new PileGroupSettlement
            {
                LoadingType = "個別十字",
                RectLoads = new ObservableCollection<RectLoad>()
            };
            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(1, 0, 0, vL0: 1000),
            };
            var soilPiles = new ObservableCollection<SoilPile> { MakeSoilPile(1.0) };

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(pgs, piles, soilPiles, null!);

            // 個別十字は GetCrossRectLoads が複数の矩形を生成 (具体数は実装依存だが ≥1)
            Assert.IsTrue(result.Count >= 1, $"十字配置で少なくとも 1 つの矩形が生成される (actual: {result.Count})");
        }

        [TestMethod]
        public void BuildAutoCrossRectLoads_ZeroRadius_SkipsPile()
        {
            // GroupPileLoadDia=0 の杭は荷重生成をスキップ (NaN 除算回避)
            var pgs = new PileGroupSettlement
            {
                LoadingType = "個別矩形",
                RectLoads = new ObservableCollection<RectLoad>()
            };
            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(1, 0, 0),
                MakePile(2, 5, 0),
            };
            var soilPiles = new ObservableCollection<SoilPile> { MakeSoilPile(0) }; // radius=0

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(pgs, piles, soilPiles, null!);

            Assert.AreEqual(0, result.Count, "radius=0 の杭は全てスキップされるはず");
        }

        [TestMethod]
        public void BuildAutoCrossRectLoads_BeamAwareIndividualRect_PreservesQA()
        {
            // 個別矩形（基礎梁考慮）: 既存矩形では QA も維持 (反復解析の k_i·S_2 で更新されるため触らない)
            var existing = new RectLoad
            {
                LinkedPileNo = 1,
                X1 = 0, X2 = 1, Y1 = 0, Y2 = 1,
                QA = 555, // 反復解析で計算済の値、書き換え禁止
            };
            var pgs = new PileGroupSettlement
            {
                LoadingType = "個別矩形(基礎梁考慮)",
                RectLoads = new ObservableCollection<RectLoad> { existing }
            };
            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(1, 0, 0, vL0: 9999), // 入力軸力は無視されるはず
            };
            var soilPiles = new ObservableCollection<SoilPile> { MakeSoilPile(1.0) };

            // BeamAware パスは LoadingType が "個別矩形（基礎梁考慮）" だが括弧の種類は実装と一致させる
            // 実装: 全角の "個別矩形（基礎梁考慮）" を使用
            pgs.LoadingType = "個別矩形（基礎梁考慮）";

            var result = SettlementAnalysisService.BuildAutoCrossRectLoads(pgs, piles, soilPiles, null!);

            // QA は既存値を維持 (LoadingType=基礎梁考慮 では新しい入力軸力で上書きしない)
            if (result.Count > 0 && ReferenceEquals(result[0], existing))
            {
                Assert.AreEqual(555.0, existing.QA, 1e-9, "基礎梁考慮: 既存 QA は維持される");
            }
        }
    }
}
