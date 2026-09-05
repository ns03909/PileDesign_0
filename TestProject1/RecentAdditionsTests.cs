using PileDesign.Converters;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// GroundLayerInput の計算プロパティ Gs0 / Es0 のテスト。
    /// 公式: Gs0 = γ × Vs² / 9.80665、Es0 = 2(1 + νs) × Gs0
    /// </summary>
    [TestClass]
    public class GroundLayerInputComputedTests
    {
        [TestMethod]
        public void Gs0_VsZero_IsZero()
        {
            var layer = new GroundLayerInput { Density = 18.0, Vs = 0.0 };
            Assert.AreEqual(0.0, layer.Gs0, 1e-12);
        }

        [TestMethod]
        public void Gs0_DensityZero_IsZero()
        {
            var layer = new GroundLayerInput { Density = 0.0, Vs = 200.0 };
            Assert.AreEqual(0.0, layer.Gs0, 1e-12);
        }

        [TestMethod]
        public void Gs0_FormulaMatches()
        {
            // γ=18 kN/m³, Vs=200 m/s → Gs0 = 18 × 200² / 9.80665 ≈ 73,420 kN/m²
            var layer = new GroundLayerInput { Density = 18.0, Vs = 200.0 };
            double expected = 18.0 * 200.0 * 200.0 / 9.80665;
            Assert.AreEqual(expected, layer.Gs0, 1e-6);
        }

        [TestMethod]
        public void Es0_FormulaMatches()
        {
            // νs=0.3, Gs0 → Es0 = 2 × 1.3 × Gs0
            var layer = new GroundLayerInput { Density = 18.0, Vs = 200.0, PoissonsRatio = 0.3 };
            double gs0 = 18.0 * 200.0 * 200.0 / 9.80665;
            double expected = 2.0 * (1.0 + 0.3) * gs0;
            Assert.AreEqual(expected, layer.Es0, 1e-6);
        }

        [TestMethod]
        public void Es0_ChangesWithPoissonsRatio()
        {
            var layer = new GroundLayerInput { Density = 18.0, Vs = 200.0, PoissonsRatio = 0.3 };
            double es0_03 = layer.Es0;
            layer.PoissonsRatio = 0.45;
            double es0_045 = layer.Es0;
            // νs を上げると Es0 も上がる
            Assert.IsTrue(es0_045 > es0_03);
            // 比率: 2(1+0.45) / 2(1+0.3) = 1.45/1.3
            Assert.AreEqual(es0_03 * 1.45 / 1.3, es0_045, 1e-6);
        }

        [TestMethod]
        public void DefaultPoissonsRatio_IsSandyDefault()
        {
            // 既定値は 0.3 (砂質土相当)
            var layer = new GroundLayerInput();
            Assert.AreEqual(0.3, layer.PoissonsRatio, 1e-12);
        }
    }

    /// <summary>
    /// PileGroupSettlement.GetEffectiveLayersForAnalysis のテスト
    /// (荷重面位置に応じて、土層を切り詰めた解析用レイヤを返す)
    /// </summary>
    [TestClass]
    public class EffectiveSettlementLayersTests
    {
        private static SettlementSoilLayer L(double bottom, double thickness, double ek = 10000, double nu = 0.3)
            => new() { BottomAltitude = bottom, Thickness = thickness, Ek = ek, PoissonsRatio = nu };

        [TestMethod]
        public void EmptyLayers_ReturnsSame()
        {
            var layers = new ObservableCollection<SettlementSoilLayer>();
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -5, layers);
            Assert.AreSame(layers, r);
        }

        [TestMethod]
        public void NullLayers_ReturnsNull()
        {
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -5, null!);
            Assert.IsNull(r);
        }

        [TestMethod]
        public void LoadingPlaneEqualsTop_ReturnsSameInstance()
        {
            // 荷重面 == 土層上端 → そのまま返す
            var layers = new ObservableCollection<SettlementSoilLayer>
            {
                L(-3, 3), L(-10, 7)
            };
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, 0, layers);
            Assert.AreSame(layers, r);
        }

        [TestMethod]
        public void LoadingPlaneInFirstLayer_TrimsFirstThickness()
        {
            // 土層上端 0、第1層下端-3、第2層下端-10
            // 荷重面 -1 → 第1層内 → 厚さ -1-(-3)=2 に切詰、層数 2 のまま
            var layers = new ObservableCollection<SettlementSoilLayer>
            {
                L(-3, 3, ek: 11111, nu: 0.4),
                L(-10, 7, ek: 22222, nu: 0.35)
            };
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -1, layers);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(-3, r[0].BottomAltitude, 1e-12);
            Assert.AreEqual(2.0, r[0].Thickness, 1e-12); // -1 - (-3)
            Assert.AreEqual(11111, r[0].Ek);             // 元層の Ek/νs を継承
            Assert.AreEqual(0.4, r[0].PoissonsRatio);
            // 第 2 層は元参照
            Assert.AreSame(layers[1], r[1]);
        }

        [TestMethod]
        public void LoadingPlaneInSecondLayer_DropsFirstLayerAndTrims()
        {
            // 荷重面 -5 → 第2層内 (-3〜-10)、第1層は捨てる
            var layers = new ObservableCollection<SettlementSoilLayer>
            {
                L(-3, 3),
                L(-10, 7, ek: 33333, nu: 0.25)
            };
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -5, layers);
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(-10, r[0].BottomAltitude, 1e-12);
            Assert.AreEqual(5.0, r[0].Thickness, 1e-12); // -5 - (-10)
            Assert.AreEqual(33333, r[0].Ek);
            Assert.AreEqual(0.25, r[0].PoissonsRatio);
        }

        [TestMethod]
        public void TrimmedFirstLayer_IsNewInstance_NotSharedReference()
        {
            // 切詰めた最上層は新規インスタンスでなければならない
            // (元の SettlementSoilLayer の Thickness を破壊してはいけない)
            var layers = new ObservableCollection<SettlementSoilLayer> { L(-3, 3), L(-10, 7) };
            var origThickness0 = layers[0].Thickness;
            var r = PileGroupSettlement.GetEffectiveLayersForAnalysis(0, -1, layers);
            // 元のレイヤ thickness は変わっていない
            Assert.AreEqual(origThickness0, layers[0].Thickness);
            // r[0] は元 layers[0] とは別インスタンス
            Assert.AreNotSame(layers[0], r[0]);
        }
    }

    /// <summary>
    /// 個別十字荷重面 (5 矩形分割) の幾何テスト
    /// </summary>
    [TestClass]
    public class CrossRectLoadGeometryTests
    {
        [TestMethod]
        public void GetCrossDimensions_RadiusZero_ReturnsAllZero()
        {
            var (a, b, c) = PileGroupSettlement.GetCrossDimensions(0);
            Assert.AreEqual(0, a, 1e-12);
            Assert.AreEqual(0, b, 1e-12);
            Assert.AreEqual(0, c, 1e-12);
        }

        [TestMethod]
        public void GetCrossDimensions_AreaEqualsCircle()
        {
            // 5 矩形の面積合計 a² + 4·b·c が 円面積 π·r² と等価
            double r = 1.5;
            var (a, b, c) = PileGroupSettlement.GetCrossDimensions(r);
            double areaCross = a * a + 4.0 * b * c;
            double areaCircle = Math.PI * r * r;
            Assert.AreEqual(areaCircle, areaCross, 1e-9);
        }

        [TestMethod]
        public void GetCrossDimensions_RatiosFollowFormula()
        {
            // 解析的関係: c = b/4, a = b·(1+√2)
            var (a, b, c) = PileGroupSettlement.GetCrossDimensions(2.0);
            Assert.AreEqual(b / 4.0, c, 1e-12);
            Assert.AreEqual(b * (1.0 + Math.Sqrt(2.0)), a, 1e-12);
        }

        [TestMethod]
        public void GetCrossRectLoads_ProducesFiveRectangles()
        {
            var rects = PileGroupSettlement.GetCrossRectLoads(new Point(10, 20), 1.0, 100);
            Assert.AreEqual(5, rects.Count);
        }

        [TestMethod]
        public void GetCrossRectLoads_TotalQAEqualsInputQA()
        {
            // 5 矩形の QA の合計が入力 qa と一致
            double qa = 480.0;
            var rects = PileGroupSettlement.GetCrossRectLoads(new Point(0, 0), 1.5, qa);
            double sumQA = rects.Sum(r => r.QA);
            Assert.AreEqual(qa, sumQA, 1e-6);
        }

        [TestMethod]
        public void GetCrossRectLoads_AllUnitLoadsEqual()
        {
            // 5 矩形は等分布なので 単位面積荷重 (Q = QA/A) が全矩形で等しい
            var rects = PileGroupSettlement.GetCrossRectLoads(new Point(0, 0), 2.0, 200);
            double q0 = rects[0].Q;
            for (int i = 1; i < 5; i++)
                Assert.AreEqual(q0, rects[i].Q, 1e-9, $"rect[{i}] の単位荷重が rect[0] と異なる");
        }
    }

    /// <summary>
    /// ZElevationConverter / SumConverter の動作テスト
    /// </summary>
    [TestClass]
    public class ConverterTests
    {
        [TestMethod]
        public void SumConverter_TwoDoubles_AddsThem()
        {
            var c = new SumConverter();
            object?[] vals = { 5.0, 3.0 };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(8.0, (double)r!, 1e-12);
        }

        [TestMethod]
        public void SumConverter_NegativeValues_AddsCorrectly()
        {
            var c = new SumConverter();
            object?[] vals = { -2.5, 0.5 };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(-2.0, (double)r!, 1e-12);
        }

        [TestMethod]
        public void SumConverter_NullArray_ReturnsZero()
        {
            var c = new SumConverter();
            var r = c.Convert(null!, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(0.0, (double)r!, 1e-12);
        }

        [TestMethod]
        public void SumConverter_StringNumeric_ParsesAndSums()
        {
            // 文字列に数値が入った場合もパースして加算
            var c = new SumConverter();
            object?[] vals = { "1.5", "2.5" };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(4.0, (double)r!, 1e-12);
        }

        [TestMethod]
        public void ZElevationConverter_ConvertAddsZAndRefAlt()
        {
            // [0]=Z, [1]=ReferenceAltitude → 標高 = Z + RefAlt
            var c = new ZElevationConverter();
            object?[] vals = { -3.5, 50.0 };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(46.5, (double)r!, 1e-12);
        }

        [TestMethod]
        public void ZElevationConverter_RefAltZero_ReturnsZ()
        {
            var c = new ZElevationConverter();
            object?[] vals = { 7.25, 0.0 };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(7.25, (double)r!, 1e-12);
        }

        [TestMethod]
        public void ZElevationConverter_TooFewValues_ReturnsZero()
        {
            var c = new ZElevationConverter();
            object?[] vals = { 5.0 };
            var r = c.Convert(vals, typeof(double), null!, CultureInfo.InvariantCulture);
            Assert.AreEqual(0.0, (double)r!, 1e-12);
        }
    }

    /// <summary>
    /// 液状化指標 PL (岩崎ら 1982) の計算テスト
    /// PL = Σ F·W·H, F=max(0, 1-FL), W=max(0, 10-0.5z), z∈[0, 20m]
    /// </summary>
    [TestClass]
    public class PLCalculationTests
    {
        private static GroundMassDataInput Mass(double glDepth, double h, double? fl1, double? fl2, bool isLiq = true)
        {
            var m = new GroundMassDataInput
            {
                GLDepth = glDepth,
                H = h,
                IsLiquefactionLayer = isLiq,
                FL = new ObservableCollection<double?>(new double?[] { fl1, fl2 })
            };
            return m;
        }

        [TestMethod]
        public void PL_NoLiquefactionLayers_IsZero()
        {
            // 全てのマスが IsLiquefactionLayer=false
            var masses = new[]
            {
                Mass(-2, 1.0, 0.5, 0.5, isLiq: false),
                Mass(-5, 1.0, 0.3, 0.3, isLiq: false),
            };
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-12);
        }

        [TestMethod]
        public void PL_AllFLAboveOne_IsZero()
        {
            // 全 FL > 1 → F = max(0, 1-FL) = 0
            var masses = new[]
            {
                Mass(-2, 1.0, 1.5, 2.0),
                Mass(-5, 1.0, 1.2, 1.1),
            };
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-12);
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 1), 1e-12);
        }

        [TestMethod]
        public void PL_Below20m_IsExcluded()
        {
            // 深さ 20m 以遠は PL 計算対象外
            var masses = new[]
            {
                Mass(-25, 1.0, 0.0, 0.0), // FL=0 だが深さ 25m → 除外
            };
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-12);
        }

        [TestMethod]
        public void PL_NullFL_IsExcluded()
        {
            // FL = null → 除外 (液状化判定対象外)
            var masses = new[] { Mass(-2, 1.0, null, null) };
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-12);
        }

        [TestMethod]
        public void PL_SingleMass_FormulaMatches()
        {
            // 深さ 5m、H=1.0、FL=0.5 → F=0.5、W=10-0.5×5=7.5、PL=0.5×7.5×1.0=3.75
            var masses = new[] { Mass(-5, 1.0, 0.5, 0.5) };
            Assert.AreEqual(3.75, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
        }

        [TestMethod]
        public void PL_AtSurface_WMaximized()
        {
            // 深さ 0m → W=10、F=1-0=1.0、H=2.0 → PL=1×10×2=20
            var masses = new[] { Mass(0, 2.0, 0.0, 0.0) };
            Assert.AreEqual(20.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
        }

        [TestMethod]
        public void PL_At20m_WIsZero()
        {
            // 深さ 20m → W=10-0.5×20=0 → 寄与 0
            var masses = new[] { Mass(-20, 1.0, 0.0, 0.0) };
            Assert.AreEqual(0.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
        }

        [TestMethod]
        public void PL_MultipleMasses_AreSummed()
        {
            // 2 つの mass 寄与を合算
            // (z=2, FL=0.5, H=1) → F=0.5, W=9, contrib=4.5
            // (z=5, FL=0.5, H=2) → F=0.5, W=7.5, contrib=7.5
            // 合計 12.0
            var masses = new[]
            {
                Mass(-2, 1.0, 0.5, 0.5),
                Mass(-5, 2.0, 0.5, 0.5),
            };
            Assert.AreEqual(12.0, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
        }

        [TestMethod]
        public void PL_LevelIndependence()
        {
            // レベル 1 と 2 で別々の FL を使う
            var masses = new[] { Mass(-2, 1.0, 0.5, 0.8) }; // FL[0]=0.5, FL[1]=0.8
            // L1: F=0.5, W=9 → 4.5
            // L2: F=0.2, W=9 → 1.8
            Assert.AreEqual(4.5, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
            Assert.AreEqual(1.8, GroundLayerViewModel.ComputeIwasakiPL(masses, 1), 1e-9);
        }

        [TestMethod]
        public void PL_MixedLayers_ExcludesNonLiq()
        {
            // 液状化層と非液状化層が混在 → 液状化層のみ寄与
            var masses = new[]
            {
                Mass(-2, 1.0, 0.5, 0.5, isLiq: true),     // 寄与
                Mass(-3, 1.0, 0.0, 0.0, isLiq: false),    // 除外
                Mass(-5, 1.0, 0.5, 0.5, isLiq: true),     // 寄与
            };
            // (z=2): F=0.5, W=9, H=1 → 4.5
            // (z=5): F=0.5, W=7.5, H=1 → 3.75
            // 合計 8.25
            Assert.AreEqual(8.25, GroundLayerViewModel.ComputeIwasakiPL(masses, 0), 1e-9);
        }
    }

    /// <summary>
    /// 沈下解析の並列化が逐次結果と等価であることのテスト
    /// (CalculateGridSettlements / CalculatePileSettlements は Parallel.For を使うが、
    /// 出力順と値は逐次と完全一致するはず)
    /// </summary>
    [TestClass]
    public class SettlementParallelEquivalenceTests
    {
        private static (PileGroupSettlement pgs,
                        ObservableCollection<PileLayoutDataItem> piles,
                        ObservableCollection<SoilPile> soilPiles,
                        ObservableCollection<GridDataItem> gridX,
                        ObservableCollection<GridDataItem> gridY) BuildFixture()
        {
            // 任意矩形 1 つ + 単一土層
            var pgs = new PileGroupSettlement
            {
                LoadingType = "任意矩形",
                LoadingPlaneAltitude = 0,
                SoilLayersTopAltitude = 0,
                RectLoads = new ObservableCollection<RectLoad>
                {
                    new() { X1 = -5, X2 = 5, Y1 = -5, Y2 = 5, QA = 25_000 }
                },
                SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>
                {
                    new() { BottomAltitude = -10, Thickness = 10, Ek = 10_000, PoissonsRatio = 0.3 }
                },
            };

            // 杭配置 4 本 (Point3D は X/Y/Z computed なので個別に設定)
            var piles = new ObservableCollection<PileLayoutDataItem>();
            for (int i = 0; i < 4; i++)
            {
                piles.Add(new PileLayoutDataItem
                {
                    PileNo = i + 1,
                    X = (i % 2) * 4 - 2,
                    Y = (i / 2) * 4 - 2,
                    Z = 0,
                });
            }
            var soilPiles = new ObservableCollection<SoilPile>();

            // グリッド (5×5)
            var gridX = new ObservableCollection<GridDataItem>();
            var gridY = new ObservableCollection<GridDataItem>();
            return (pgs, piles, soilPiles, gridX, gridY);
        }

        [TestMethod]
        public void GridSettlements_ParallelMatchesSequential()
        {
            // 並列実装 (現状) と参照逐次 を比較。Steinnbrener.CalcSettlement は
            // static 純粋関数 → 並列でも同じ入力なら同じ出力でなければならない。
            var (pgs, piles, soilPiles, gridX, gridY) = BuildFixture();
            var svc = new SettlementAnalysisService();
            var result = svc.PerformSettlementAnalysis(
                pgs, piles, soilPiles, gridX, gridY,
                xMin: -10, xMax: 10, yMin: -10, yMax: 10,
                xOffset: 0, yOffset: 0,
                xSpacing: 5, ySpacing: 5);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.SettlementGridData);

            // 逐次再計算してビット同等を検証
            var xs = pgs.SettlementGridX;
            var ys = pgs.SettlementGridY;
            var layers = pgs.SettlementSoilLayers;
            int idx = 0;
            foreach (var x in xs)
            {
                foreach (var y in ys)
                {
                    double seqMm = Steinnbrener.CalcSettlement(new Point(x, y), pgs.RectLoads, layers) * 1000.0;
                    var item = result.SettlementGridData[idx];
                    Assert.AreEqual(x, item.X, 1e-12);
                    Assert.AreEqual(y, item.Y, 1e-12);
                    Assert.AreEqual(seqMm, item.Settlement, 1e-9, $"並列結果が逐次と差分 (idx={idx} x={x} y={y})");
                    idx++;
                }
            }
        }

        [TestMethod]
        public void PileSettlements_ParallelMatchesSequential()
        {
            // 各杭の沈下量が並列計算後も逐次計算と同じか。
            // 値は解析が返す PileSettlements_mm が正 (入力側の杭には書かない)。
            var (pgs, piles, soilPiles, gridX, gridY) = BuildFixture();
            var svc = new SettlementAnalysisService();
            var result = svc.PerformSettlementAnalysis(
                pgs, piles, soilPiles, gridX, gridY,
                xMin: -10, xMax: 10, yMin: -10, yMax: 10,
                xOffset: 0, yOffset: 0,
                xSpacing: 5, ySpacing: 5);

            foreach (var p in piles)
            {
                double seqMm = Steinnbrener.CalcSettlement(
                    new Point(p.Point3D.X, p.Point3D.Y), pgs.RectLoads, pgs.SettlementSoilLayers) * 1000.0;
                Assert.IsTrue(result.PileSettlements_mm.TryGetValue(p.PileNo, out double parMm),
                    $"杭 {p.PileNo} の沈下量が結果に無い");
                Assert.AreEqual(seqMm, parMm, 1e-9,
                    $"杭 {p.PileNo} の並列結果が逐次と差分");
            }
        }
    }
}
