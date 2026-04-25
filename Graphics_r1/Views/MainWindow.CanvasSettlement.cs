using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : RibbonWindow
    {
        // クラスフィールドのどこか適切な場所（他の _xxx フィールドに倣う）
        private ObservableCollection<Point3D>? _pendingSettlementPoints;
        private ObservableCollection<double>? _pendingSettlementValues;
        private string? _pendingSettlementTitle;
        private string? _pendingSettlementUnit;

        // 荷重面描画の更新
        private void UpdateSettlementLoad3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            List<PathFigure> rectangleGeometries = [];

            if (viewModel.CurrentInputModel.PileGroupSettlement.LoadingType == "個別十字"
                || viewModel.CurrentInputModel.PileGroupSettlement.LoadingType == "個別十字（基礎梁考慮）")
            {
                if (viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    //MessageBox.Show("個別十字荷重を作成するには地盤杭セットが作られている必要があります。キャンセルします。");
                    return;
                }

                foreach (PileLayoutDataItem pileLayout in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    Point3D loc1 = new(pileLayout.Point3D.X, pileLayout.Point3D.Y, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude);
                    double radius = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pileLayout.SoilPileAltNo - 1].GroupPileLoadDia * 0.5;

                    List<(Point3D, Point3D)> rectangles = PileGroupSettlement.GetFiveRectsPoints(loc1, radius);
                    rectangleGeometries.AddRange(viewModel.CanvasThreeDView.RectsTranformation(rectangles));
                }
            }
            else if (viewModel.CurrentInputModel.PileGroupSettlement.LoadingType == "任意矩形")
            {
                foreach (Models.InputData.RectLoad rectLoad in viewModel.CurrentInputModel.PileGroupSettlement.RectLoads)
                {
                    Point3D loc1 = new(rectLoad.X1, rectLoad.Y1, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude);
                    Point3D loc3 = new(rectLoad.X2, rectLoad.Y2, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude);

                    List<(Point3D, Point3D)> rectangles = [(loc1, loc3)];
                    rectangleGeometries.AddRange(viewModel.CanvasThreeDView.RectsTranformation(rectangles));

                    Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                    Point coord3 = viewModel.CanvasThreeDView.Transformation(loc3);
                    double xAve = (coord1.X + coord3.X) * 0.5;
                    double yAve = (coord1.Y + coord3.Y) * 0.5;
                    double qa = rectLoad.QA;
                    double q = rectLoad.Q;
                    AddText3D(Brushes.SaddleBrown, $"{qa:N0}", xAve, yAve, "C", "B", 0);
                    AddText3D(Brushes.SaddleBrown, $"({q:N0})", xAve, yAve, "C", "T", 0);
                }
            }
            MainCanvasGeometry.AddRectanglesToPathGeometry(viewModel.CanvasGeometry.PathGeoRectLoads, rectangleGeometries);
        }

        // 群杭グリッド
        private void UpdateGroupPileGrid3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel == null) return;
            if (viewModel.CurrentInputModel.PileGroupSettlement == null) return;

            double xmin = viewModel.GroupPileSettlementXMin;
            double xmax = viewModel.GroupPileSettlementXMax;
            double ymin = viewModel.GroupPileSettlementYMin;
            double ymax = viewModel.GroupPileSettlementYMax;
            double xOffset = viewModel.GroupPileSettlementXOffset;
            double yOffset = viewModel.GroupPileSettlementYOffset;
            double xSpacing = viewModel.GroupPileSettlementXSpacing;
            double ySpacing = viewModel.GroupPileSettlementYSpacing;

            ObservableCollection<double> xs = PileGroupSettlement.GetCoord(
                xmin, xmax, xOffset, xSpacing, viewModel.CurrentInputModel.GridXItems
                );
            ObservableCollection<double> ys = PileGroupSettlement.GetCoord(
                ymin, ymax, yOffset, ySpacing, viewModel.CurrentInputModel.GridYItems
                );

            double z = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            // draw grid
            foreach (var y0 in ys)
            {
                Point3D start3D = new(xs[0], y0, z);
                Point3D end3D = new(xs[^1], y0, z);

                Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                Point end = viewModel.CanvasThreeDView.Transformation(end3D);
                AddLineGeometry(start, end, viewModel.CanvasGeometry.PathGeoSettlementGrid);
            }

            foreach (var x0 in xs)
            {
                Point3D start3D = new(x0, ys[0], z);
                Point3D end3D = new(x0, ys[^1], z);

                Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                Point end = viewModel.CanvasThreeDView.Transformation(end3D);
                AddLineGeometry(start, end, viewModel.CanvasGeometry.PathGeoSettlementGrid);
            }

            // 土層
            foreach (var soilLayer in viewModel.CurrentInputModel.PileGroupSettlement.SettlementSoilLayers)
            {
                double bottomLevel = soilLayer.BottomAltitude;
                double topLevel = bottomLevel + soilLayer.Thickness;

                Point3D b003D = new(xs[0], ys[0], bottomLevel);
                Point3D bn03D = new(xs[^1], ys[0], bottomLevel);
                Point3D b0n3D = new(xs[0], ys[^1], bottomLevel);
                Point3D bnn3D = new(xs[^1], ys[^1], bottomLevel);

                Point3D t003D = new(xs[0], ys[0], topLevel);
                Point3D tn03D = new(xs[^1], ys[0], topLevel);
                Point3D t0n3D = new(xs[0], ys[^1], topLevel);
                Point3D tnn3D = new(xs[^1], ys[^1], topLevel);

                Point b00 = viewModel.CanvasThreeDView.Transformation(b003D);
                Point bn0 = viewModel.CanvasThreeDView.Transformation(bn03D);
                Point b0n = viewModel.CanvasThreeDView.Transformation(b0n3D);
                Point bnn = viewModel.CanvasThreeDView.Transformation(bnn3D);

                Point t00 = viewModel.CanvasThreeDView.Transformation(t003D);
                Point tn0 = viewModel.CanvasThreeDView.Transformation(tn03D);
                Point t0n = viewModel.CanvasThreeDView.Transformation(t0n3D);
                Point tnn = viewModel.CanvasThreeDView.Transformation(tnn3D);

                AddLineGeometry(b00, bn0, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(b00, b0n, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(bn0, bnn, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(b0n, bnn, viewModel.CanvasGeometry.PathGeoSettlementGrid);

                AddLineGeometry(b00, t00, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(b0n, t0n, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(bn0, tn0, viewModel.CanvasGeometry.PathGeoSettlementGrid);
                AddLineGeometry(bnn, tnn, viewModel.CanvasGeometry.PathGeoSettlementGrid);
            }
        }

        // 荷重ケース名取得メソッド
        private double GetForceZ(MainWindowViewModel viewModel, PileLayoutDataItem pileLocation, LoadCase selectedLoadCase)
        {
            switch (viewModel.SelectedLoadCaseName)
            {
                case "VL0":
                    return -pileLocation.AxialForceVL0;
                case "VLadd":
                    return -pileLocation.AxialForceVLAdditional;
                case "VL":
                    return -pileLocation.AxialForceVL0 - pileLocation.AxialForceVLAdditional;
            }

            if (selectedLoadCase.Level == 1)
            {
                for (int index = 0; index < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; index++)
                {
                    if (selectedLoadCase.LoadName == viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[index].LoadName)
                    {
                        return -pileLocation.AxialForceLevel1s[index];
                    }
                }

            }
            else if (selectedLoadCase.Level == 2)
            {
                for (int index = 0; index < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; index++)
                {
                    if (selectedLoadCase.LoadName == viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[index].LoadName)
                    {
                        return -pileLocation.AxialForceLevel2s[index];
                    }
                }
            }
            return 0;
        }

        // 沈下検討用土層描画メソッド
        private void UpdateSettlementGround3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double loadingPlaneAltitude = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            AddLineGeometryToPath(new Point3D(0, 0, loadingPlaneAltitude), Canvas3DWidth, viewModel.CanvasGeometry.PathGeoPileSoils);

            foreach (var settlementSoilLayer in viewModel.CurrentInputModel.PileGroupSettlement.SettlementSoilLayers)
            {
                AddLineGeometryToPath(new Point3D(0, 0, settlementSoilLayer.BottomAltitude), Canvas3DWidth, viewModel.CanvasGeometry.PathGeoPileSoils);
            }
        }

        private void AddLineGeometryToPath(Point3D loc, double canvasWidth, PathGeometry pathGeometry)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            Point coord = viewModel.CanvasThreeDView.Transformation(loc);

            LineGeometry lineGeometry = new()
            {
                StartPoint = new Point(0, coord.Y),
                EndPoint = new Point(canvasWidth, coord.Y)
            };

            pathGeometry.AddGeometry(lineGeometry);
        }

        // 沈下グリッド変位メソッド
        private void UpdateSettlementGridDeformation()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 必要なデータが存在しない場合は終了
            if (viewModel.CurrentInputModel == null ||
                viewModel.CurrentInputModel.PileGroupSettlement == null) return;

            var pileGroupSettlement = viewModel.CurrentInputModel.PileGroupSettlement;
            if (pileGroupSettlement.SettlementGridX == null ||
                pileGroupSettlement.SettlementGridY == null ||
                pileGroupSettlement.SettlementGridData == null)
                return;

            double z = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
            var xs = pileGroupSettlement.SettlementGridX;
            var ys = pileGroupSettlement.SettlementGridY;
            var items = pileGroupSettlement.SettlementGridData;

            if (xs.Count == 0 || ys.Count == 0 || items.Count == 0)
                return;

            // フィンガープリント作成（データ変更検知）
            double minS = items.Min(it => it.Settlement);
            double maxS = items.Max(it => it.Settlement);
            double sumX = items.Sum(it => it.X);
            double sumY = items.Sum(it => it.Y);
            double sumS = items.Sum(it => it.Settlement);
            var fp = new PileDesign.ViewModels.SettlementGridFingerprint(
                xs.Count, ys.Count, items.Count,
                minS, maxS,
                z, viewModel.DisplacementDiagramRatio * viewModel.ModelExtent,
                sumX, sumY, sumS);

            bool needRebuild = viewModel.SettlementWorldCache.Fingerprint == null ||
                               !viewModel.SettlementWorldCache.Fingerprint.Equals(fp);

            // 沈下値をモデル座標オフセットへ変換するスケール (m offset / mm settlement)。
            // 杭・地盤変位と同じ正規化方式: dispRatio × ModelExtent / maxAbsDisp
            double maxAbsSettlementMm = Math.Max(Math.Abs(minS), Math.Abs(maxS));
            double dispScaleMperMm = maxAbsSettlementMm > 1e-15
                ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxAbsSettlementMm
                : 0.0;

            if (needRebuild)
            {
                // 再構築
                var cache = new PileDesign.ViewModels.SettlementGridRenderCache();

                // カラーバー帯（色と範囲）- レインボーカラースケールを使用
                var allValues = new ObservableCollection<double>(items.Select(x => x.Settlement));
                var colorBarGeometries = ColorBarUtils.GetColorBarGeometries(allValues, steps: 12, mode: ColorBarUtils.ColorBarMode.Rainbow);
                // 要素名を明示：Bottom/Top/Color
                var colorBands = colorBarGeometries
                    .Select(g => (Bottom: g.BottomRange, Top: g.TopRange, g.Color))
                    .ToList();

                cache.ColorBands = colorBands;

                // 2次元配列化（ix,iy必須）
                var grid = new SettlementGridDataItem[xs.Count, ys.Count];
                foreach (var it in items)
                {
                    int ix = xs.IndexOf(it.X);
                    int iy = ys.IndexOf(it.Y);
                    if (ix >= 0 && iy >= 0) grid[ix, iy] = it;
                }

                // 変形グリッド線分（3D）: Y方向
                for (int ix = 0; ix < xs.Count; ix++)
                {
                    for (int iy = 0; iy < ys.Count - 1; iy++)
                    {
                        var p1 = grid[ix, iy];
                        var p2 = grid[ix, iy + 1];
                        var s1 = z - p1.Settlement * dispScaleMperMm;
                        var s2 = z - p2.Settlement * dispScaleMperMm;
                        cache.GridSegments3D.Add((new Point3D(p1.X, p1.Y, s1), new Point3D(p2.X, p2.Y, s2)));
                    }
                }
                // 変形グリッド線分（3D）: X方向
                for (int iy = 0; iy < ys.Count; iy++)
                {
                    for (int ix = 0; ix < xs.Count - 1; ix++)
                    {
                        var p1 = grid[ix, iy];
                        var p2 = grid[ix + 1, iy];
                        var s1 = z - p1.Settlement * dispScaleMperMm;
                        var s2 = z - p2.Settlement * dispScaleMperMm;
                        cache.GridSegments3D.Add((new Point3D(p1.X, p1.Y, s1), new Point3D(p2.X, p2.Y, s2)));
                    }
                }
                cache.ColorBands = colorBands;

                // 等値帯ポリゴン（3D）
                var contourLevels = colorBands.Select(b => b.Bottom).ToList();
                if (colorBands.Count > 0) contourLevels.Add(colorBands[^1].Top); // Item2 -> Top に統一

                // セル毎に等値帯を抽出
                for (int ix = 0; ix < xs.Count - 1; ix++)
                {
                    for (int iy = 0; iy < ys.Count - 1; iy++)
                    {
                        var p00 = grid[ix, iy];
                        var p10 = grid[ix + 1, iy];
                        var p11 = grid[ix + 1, iy + 1];
                        var p01 = grid[ix, iy + 1];

                        double[] vals = [p00.Settlement, p10.Settlement, p11.Settlement, p01.Settlement];
                        SettlementGridDataItem[] cellPts = [p00, p10, p11, p01];

                        for (int k = 0; k < contourLevels.Count - 1; k++)
                        {
                            double minC = contourLevels[k];
                            double maxC = contourLevels[k + 1];
                            var bandColor = colorBands[k].Color;

                            // 頂点内点
                            List<Point3D> regionPoints3D = [];
                            for (int v = 0; v < 4; v++)
                            {
                                if (vals[v] >= minC && vals[v] <= maxC)
                                {
                                    double zz = z - cellPts[v].Settlement * dispScaleMperMm;
                                    regionPoints3D.Add(new Point3D(cellPts[v].X, cellPts[v].Y, zz));
                                }
                            }

                            // 4辺の交点（minC, maxC）
                            static bool Cross(double a, double b, double t) =>
                                (a < t && b > t) || (a > t && b < t);

                            int[,] edges = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 } };
                            foreach (var level in new[] { minC, maxC })
                            {
                                for (int e = 0; e < 4; e++)
                                {
                                    int a = edges[e, 0], b = edges[e, 1];
                                    if (Cross(vals[a], vals[b], level))
                                    {
                                        var p = Interpolate3D(cellPts[a], cellPts[b], level, z, dispScaleMperMm);
                                        regionPoints3D.Add(p);
                                    }
                                }
                            }

                            // 重複除去（座標近似）
                            regionPoints3D = [.. regionPoints3D
                                .GroupBy(pt => (Math.Round(pt.X, 6), Math.Round(pt.Y, 6), Math.Round(pt.Z, 6)))
                                .Select(g => g.First())];

                            if (regionPoints3D.Count >= 3)
                            {
                                cache.IsoBands3D.Add(new PileDesign.ViewModels.SettlementIsoBand
                                {
                                    Points = regionPoints3D,
                                    Color = bandColor
                                });
                            }
                        }
                    }
                }

                // 等高線（3D）: Marching Squares
                foreach (double contour in contourLevels)
                {
                    for (int ix = 0; ix < xs.Count - 1; ix++)
                    {
                        for (int iy = 0; iy < ys.Count - 1; iy++)
                        {
                            var p00 = grid[ix, iy];
                            var p10 = grid[ix + 1, iy];
                            var p11 = grid[ix + 1, iy + 1];
                            var p01 = grid[ix, iy + 1];

                            double v00 = p00.Settlement;
                            double v10 = p10.Settlement;
                            double v11 = p11.Settlement;
                            double v01 = p01.Settlement;

                            List<Point3D> contour3D = [];
                            if ((v00 - contour) * (v10 - contour) < 0)
                                contour3D.Add(Interpolate3D(p00, p10, contour, z, dispScaleMperMm));
                            if ((v10 - contour) * (v11 - contour) < 0)
                                contour3D.Add(Interpolate3D(p10, p11, contour, z, dispScaleMperMm));
                            if ((v11 - contour) * (v01 - contour) < 0)
                                contour3D.Add(Interpolate3D(p11, p01, contour, z, dispScaleMperMm));
                            if ((v01 - contour) * (v00 - contour) < 0)
                                contour3D.Add(Interpolate3D(p01, p00, contour, z, dispScaleMperMm));

                            if (contour3D.Count >= 2)
                            {
                                cache.Contours3D.Add(contour3D);
                                cache.ContourLevels3D.Add(contour);
                            }
                        }
                    }
                }

                cache.Fingerprint = fp;
                viewModel.SettlementWorldCache = cache;
            }

            // ここから「描画」段階（投影のみ）
            // グリッド線は PathGeoSettlementGrid にまとめて出力
            var settlementGridGeometry = new PathGeometry();
            foreach (var (Start, End) in viewModel.SettlementWorldCache.GridSegments3D)
            {
                var s = viewModel.CanvasThreeDView.Transformation(Start);
                var e = viewModel.CanvasThreeDView.Transformation(End);
                settlementGridGeometry.AddGeometry(new LineGeometry(s, e));
            }
            viewModel.CanvasGeometry.PathGeoSettlementGrid = settlementGridGeometry;

            // 等値帯（色ごとに1本のPathGeometryへ集約）
            var bandGeometries = new Dictionary<Color, PathGeometry>();

            foreach (var band in viewModel.SettlementWorldCache.IsoBands3D)
            {
                var color = band.Color;
                if (!bandGeometries.TryGetValue(color, out var pg))
                {
                    pg = new PathGeometry();
                    bandGeometries[color] = pg;
                }

                // 2Dへ投影して時計回りに整列
                var pts2D = band.Points.Select(p => viewModel.CanvasThreeDView.Transformation(p)).ToList();
                var ordered = SortClockwise(pts2D);
                if (ordered.Count == 0) continue;

                var fig = new PathFigure
                {
                    StartPoint = ordered[0],
                    IsClosed = true,
                    IsFilled = true
                };
                if (ordered.Count > 1)
                {
                    fig.Segments.Add(new PolyLineSegment([.. ordered.Skip(1)], true));
                }
                bandGeometries[color].Figures.Add(fig);
            }

            // 既存の色Pathが不要になった場合を除去するために現行色集合を把握
            var currentColors = new HashSet<Color>(bandGeometries.Keys);

            // 2) Pathを作成/更新（色ごと）
            foreach (var kv in bandGeometries)
            {
                var color = kv.Key;
                var geom = kv.Value;
                if (geom.CanFreeze) geom.Freeze();

                // 半透明の色を作成（アルファ値を180に設定、約70%の不透明度）
                var semiTransparentColor = Color.FromArgb(180, color.R, color.G, color.B);

                if (!_settlementBandPaths.TryGetValue(color, out var path))
                {
                    var fillBrush = new SolidColorBrush(semiTransparentColor);
                    fillBrush.Freeze();
                    path = new Path
                    {
                        Fill = fillBrush,
                        Stroke = Brushes.Transparent,
                        StrokeThickness = 0,
                        IsHitTestVisible = false,
                        CacheMode = new BitmapCache(1.0)
                    };
                    Canvas3DLayout.Children.Add(path);
                    _settlementBandPaths[color] = path;
                }
                else if (!Canvas3DLayout.Children.Contains(path))
                {
                    Canvas3DLayout.Children.Add(path);
                }

                path.Data = geom;
                // 半透明のブラシを設定
                path.Fill = new SolidColorBrush(semiTransparentColor);
                if (path.Fill is SolidColorBrush sb && sb.CanFreeze) sb.Freeze();
            }

            // 3) 使われなくなった色Pathを削除
            var obsoleteColors = _settlementBandPaths.Keys.Where(c => !currentColors.Contains(c)).ToList();
            foreach (var c in obsoleteColors)
            {
                var p = _settlementBandPaths[c];
                Canvas3DLayout.Children.Remove(p);
                _settlementBandPaths.Remove(c);
            }

            // 等高線（全てを1本のStreamGeometryに集約）
            var contoursGeometry = new StreamGeometry();
            using (var ctx = contoursGeometry.Open())
            {
                foreach (var line3D in viewModel.SettlementWorldCache.Contours3D)
                {
                    var pts = line3D.Select(p => viewModel.CanvasThreeDView.Transformation(p)).ToList();
                    if (pts.Count == 0) continue;

                    ctx.BeginFigure(pts[0], isFilled: false, isClosed: false);
                    if (pts.Count > 1)
                    {
                        ctx.PolyLineTo([.. pts.Skip(1)], isStroked: true, isSmoothJoin: false);
                    }
                }
            }
            contoursGeometry.Freeze();

            _settlementContoursPath ??= new Path
            {
                Stroke = Brushes.White,
                StrokeThickness = 1.0,
                IsHitTestVisible = false,
                CacheMode = new BitmapCache(1.0)
            };
            // Children.Clear() 後なので毎回追加（未追加なら）
            if (!Canvas3DLayout.Children.Contains(_settlementContoursPath))
            {
                Canvas3DLayout.Children.Add(_settlementContoursPath);
            }
            _settlementContoursPath.Data = contoursGeometry;

            // 等高線ラベル（値表示ONの場合、コンタ線上に沈下量を表示）
            if (viewModel.IsResultValueVisible && viewModel.SettlementWorldCache.ContourLevels3D.Count > 0)
            {
                string fmt = "{0:N" + viewModel.DecimalPlaces + "}";

                // 各contourレベルごとにセグメントを集約し、適度な間隔でラベルを配置
                var levelSegments = new Dictionary<double, List<List<Point3D>>>();
                for (int i = 0; i < viewModel.SettlementWorldCache.Contours3D.Count; i++)
                {
                    double level = viewModel.SettlementWorldCache.ContourLevels3D[i];
                    if (!levelSegments.ContainsKey(level))
                        levelSegments[level] = new List<List<Point3D>>();
                    levelSegments[level].Add(viewModel.SettlementWorldCache.Contours3D[i]);
                }

                foreach (var (level, segments) in levelSegments)
                {
                    // セグメント数に応じて間引き（多すぎるとラベルが重なる）
                    int labelInterval = Math.Max(1, segments.Count / 3);
                    for (int i = 0; i < segments.Count; i += labelInterval)
                    {
                        var seg = segments[i];
                        if (seg.Count < 2) continue;
                        // セグメントの中点にラベルを配置
                        var mid3D = new Point3D(
                            (seg[0].X + seg[^1].X) * 0.5,
                            (seg[0].Y + seg[^1].Y) * 0.5,
                            (seg[0].Z + seg[^1].Z) * 0.5);
                        var mid2D = viewModel.CanvasThreeDView.Transformation(mid3D);
                        if (!double.IsFinite(mid2D.X)) continue;

                        AddText3D(Brushes.White, string.Format(fmt, level),
                            mid2D.X, mid2D.Y, "C", "C", 0);
                    }
                }
            }

            // カラーバー（キャッシュの範囲から再構築）
            if (viewModel.SettlementWorldCache.ColorBands.Count > 0)
            {
                var values = new ObservableCollection<double>(items.Select(x => x.Settlement));
                var cb = viewModel.SettlementWorldCache.ColorBands
                    .Select(b => new ColorBaredGeometry
                    {
                        BottomRange = b.Bottom,
                        TopRange = b.Top,
                        Color = b.Color
                    }).ToList();

                ColorBar.DrawStepColorBar(
                    ColorBarCanvas,
                    cb,
                    "沈下量",
                    "mm",
                    values.Min(),
                    values.Max(),
                    "{0:N" + viewModel.DecimalPlaces + "}",
                        viewModel.LabelSize
                );
            }

            // 線形補間（3D座標生成）
            static Point3D Interpolate3D(SettlementGridDataItem p1, SettlementGridDataItem p2, double contour, double z, double dispScaleMperMm)
            {
                double t = (contour - p1.Settlement) / (p2.Settlement - p1.Settlement);
                double x = p1.X + t * (p2.X - p1.X);
                double y = p1.Y + t * (p2.Y - p1.Y);
                double zz = z - (p1.Settlement + t * (p2.Settlement - p1.Settlement)) * dispScaleMperMm;
                return new Point3D(x, y, zz);
            }
        }

        // 時計回り整列メソッド
        private List<Point> SortClockwise(List<Point> points)
        {
            if (points.Count <= 3) return [.. points];
            var center = new Point(points.Average(p => p.X), points.Average(p => p.Y));
            return [.. points.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X))];
        }

        // 沈下マップツールチップ用フィールド
        private System.Windows.Controls.Primitives.Popup? _settlementTooltipPopup;
        private System.Windows.Controls.TextBlock? _settlementTooltipText;

        /// <summary>
        /// マウス位置から沈下値を取得してツールチップを表示
        /// </summary>
        private void UpdateSettlementTooltip(Point mousePos)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 群杭グリッド変位表示が有効かチェック
            if (!viewModel.IsGroupPileGridDeformationVisible)
            {
                HideSettlementTooltip();
                return;
            }

            // 沈下データが存在するか確認
            var pileGroupSettlement = viewModel.CurrentInputModel?.PileGroupSettlement;
            if (pileGroupSettlement?.SettlementGridData == null ||
                pileGroupSettlement.SettlementGridData.Count == 0 ||
                pileGroupSettlement.SettlementGridX == null ||
                pileGroupSettlement.SettlementGridY == null)
            {
                HideSettlementTooltip();
                return;
            }

            // スクリーン座標からワールド座標へ変換
            Point3D worldPoint = viewModel.CanvasThreeDView.InverseTransformation(mousePos);
            double worldX = worldPoint.X;
            double worldY = worldPoint.Y;

            // グリッド範囲内かチェック
            var xs = pileGroupSettlement.SettlementGridX;
            var ys = pileGroupSettlement.SettlementGridY;

            if (xs.Count < 2 || ys.Count < 2)
            {
                HideSettlementTooltip();
                return;
            }

            double xMin = xs.Min();
            double xMax = xs.Max();
            double yMin = ys.Min();
            double yMax = ys.Max();

            if (worldX < xMin || worldX > xMax || worldY < yMin || worldY > yMax)
            {
                HideSettlementTooltip();
                return;
            }

            // 沈下値を双線形補間で取得
            double? settlement = InterpolateSettlement(worldX, worldY, pileGroupSettlement);
            if (settlement == null)
            {
                HideSettlementTooltip();
                return;
            }

            // ツールチップを表示
            ShowSettlementTooltip(mousePos, worldX, worldY, settlement.Value);
        }

        /// <summary>
        /// 双線形補間で沈下値を取得
        /// </summary>
        private double? InterpolateSettlement(double x, double y, PileGroupSettlement pileGroupSettlement)
        {
            var xs = pileGroupSettlement.SettlementGridX;
            var ys = pileGroupSettlement.SettlementGridY;
            var items = pileGroupSettlement.SettlementGridData;

            // x, yを含むセルを探す
            int ix = -1, iy = -1;
            for (int i = 0; i < xs.Count - 1; i++)
            {
                if (x >= xs[i] && x <= xs[i + 1])
                {
                    ix = i;
                    break;
                }
            }
            for (int i = 0; i < ys.Count - 1; i++)
            {
                if (y >= ys[i] && y <= ys[i + 1])
                {
                    iy = i;
                    break;
                }
            }

            if (ix < 0 || iy < 0) return null;

            // 4隅の点を取得
            var p00 = items.FirstOrDefault(p => Math.Abs(p.X - xs[ix]) < 0.001 && Math.Abs(p.Y - ys[iy]) < 0.001);
            var p10 = items.FirstOrDefault(p => Math.Abs(p.X - xs[ix + 1]) < 0.001 && Math.Abs(p.Y - ys[iy]) < 0.001);
            var p01 = items.FirstOrDefault(p => Math.Abs(p.X - xs[ix]) < 0.001 && Math.Abs(p.Y - ys[iy + 1]) < 0.001);
            var p11 = items.FirstOrDefault(p => Math.Abs(p.X - xs[ix + 1]) < 0.001 && Math.Abs(p.Y - ys[iy + 1]) < 0.001);

            if (p00 == null || p10 == null || p01 == null || p11 == null) return null;

            // 双線形補間
            double tx = (x - xs[ix]) / (xs[ix + 1] - xs[ix]);
            double ty = (y - ys[iy]) / (ys[iy + 1] - ys[iy]);

            double s00 = p00.Settlement;
            double s10 = p10.Settlement;
            double s01 = p01.Settlement;
            double s11 = p11.Settlement;

            double s0 = s00 * (1 - tx) + s10 * tx;
            double s1 = s01 * (1 - tx) + s11 * tx;
            double settlement = s0 * (1 - ty) + s1 * ty;

            return settlement;
        }

        /// <summary>
        /// 沈下値ツールチップを表示
        /// </summary>
        private void ShowSettlementTooltip(Point mousePos, double worldX, double worldY, double settlement)
        {
            // ポップアップが未作成なら作成
            if (_settlementTooltipPopup == null)
            {
                _settlementTooltipText = new System.Windows.Controls.TextBlock
                {
                    Background = BrushDarkBackground,
                    Foreground = Brushes.White,
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 12
                };

                var border = new System.Windows.Controls.Border
                {
                    BorderBrush = Brushes.DarkGray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = _settlementTooltipText
                };

                _settlementTooltipPopup = new System.Windows.Controls.Primitives.Popup
                {
                    Child = border,
                    AllowsTransparency = true,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                    PlacementTarget = Canvas3DLayout,
                    IsHitTestVisible = false
                };
            }

            // ツールチップのテキストを更新
            _settlementTooltipText!.Text = $"沈下量: {settlement:F3} mm\nX: {worldX:F2} m, Y: {worldY:F2} m";

            // 位置を更新（マウスの右下に表示）
            _settlementTooltipPopup.HorizontalOffset = mousePos.X + 15;
            _settlementTooltipPopup.VerticalOffset = mousePos.Y + 15;
            _settlementTooltipPopup.IsOpen = true;
        }

        /// <summary>
        /// 沈下値ツールチップを非表示
        /// </summary>
        private void HideSettlementTooltip()
        {
            if (_settlementTooltipPopup != null)
            {
                _settlementTooltipPopup.IsOpen = false;
            }
        }
    }
}
