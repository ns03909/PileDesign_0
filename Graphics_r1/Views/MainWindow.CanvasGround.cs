using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow
    {
        // 杭周地盤描画の更新
        private void UpdateGround3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pileLocation.IsVisible)
                { continue; }

                int pileBodyIndex = pileLocation.PileBodyNo - 1;
                int groundIndex = pileLocation.GroundNo - 1;

                // インデックス範囲チェック
                if (pileBodyIndex < 0 || pileBodyIndex >= viewModel.CurrentInputModel.PileBodies.Count)
                    continue; // またはエラー通知

                if (groundIndex < 0 || groundIndex >= viewModel.CurrentInputModel.GroundsInput.Count)
                    continue; // またはエラー通知

                // 地盤dia: PileToeDia × 2 を土層柱径とする
                // (拡底杭・拡大根固め杭・回転貫入杭いずれも、杭先端域の地盤撹乱範囲を 2×PileToeDia で可視化)
                double soilDia;
                if (viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileToeDia == 0)
                {
                    soilDia = 2.0 * viewModel.CanvasThreeDView.Scale;
                }
                else
                {
                    soilDia = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileToeDia / 1000.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
                }

                // 地表
                double groundTopAltitude = viewModel.CurrentInputModel.GroundsInput[pileLocation.GroundNo - 1].GroundTopAltitude;
                Point3D loc1 = new(pileLocation.Point3D.X, pileLocation.Point3D.Y, groundTopAltitude);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                EllipseGeometry ellipse1 = new(coord1, soilDia * 0.5, soilDia * 0.5 * flattening);
                viewModel.CanvasGeometry.PathGeoPileSoils.AddGeometry(ellipse1);

                // 地下水位
                double groundWaterTableAltitude = viewModel.CurrentInputModel.GroundsInput[pileLocation.GroundNo - 1].GroundWaterTableAltitude;
                Point3D loc3 = new(pileLocation.Point3D.X, pileLocation.Point3D.Y, groundWaterTableAltitude);
                Point coord3 = viewModel.CanvasThreeDView.Transformation(loc3);

                EllipseGeometry ellipse3 = new(coord3, soilDia * 0.5, soilDia * 0.5 * flattening);
                viewModel.CanvasGeometry.PathGeoPileGroundWater.AddGeometry(ellipse3);

                foreach (GroundLayerInput groundLayerInput in viewModel.CurrentInputModel.GroundsInput[pileLocation.GroundNo - 1].GroundLayers)
                {
                    double zBtm = groundLayerInput.BottomAltitude;
                    double zTop = groundLayerInput.LayerThickness + zBtm;
                    string granularityClass = groundLayerInput.GranularityClass;

                    Point3D top = new(pileLocation.Point3D.X, pileLocation.Point3D.Y, zTop);
                    Point3D btm = new(pileLocation.Point3D.X, pileLocation.Point3D.Y, zBtm);
                    Point btm2D = viewModel.CanvasThreeDView.Transformation(btm);

                    EllipseGeometry ellipse2 = new(btm2D, soilDia * 0.5, soilDia * 0.5 * flattening);
                    viewModel.CanvasGeometry.PathGeoPileSoils.AddGeometry(ellipse2);

                    // PathGeometryを選択
                    PathGeometry pathGeometry = granularityClass switch
                    {
                        "粘性土" => viewModel.CanvasGeometry.PathGeoClay,
                        "砂質土" => viewModel.CanvasGeometry.PathGeoSand,
                        "礫質土" => viewModel.CanvasGeometry.PathGeoGravel,
                        _ => viewModel.CanvasGeometry.PathGeoPileSoils
                    };

                    var pointTop1 = viewModel.CanvasThreeDView.Transformation(top) + new Vector(soilDia * 0.5, 0);
                    var pointBtm1 = viewModel.CanvasThreeDView.Transformation(btm) + new Vector(soilDia * 0.5, 0);
                    var pointTop2 = viewModel.CanvasThreeDView.Transformation(top) + new Vector(-soilDia * 0.5, 0);
                    var pointBtm2 = viewModel.CanvasThreeDView.Transformation(btm) + new Vector(-soilDia * 0.5, 0);

                    AddLineGeometry(pointTop1, pointBtm1, pathGeometry);
                    AddLineGeometry(pointTop2, pointBtm2, pathGeometry);
                }
            }
        }


        // N値描画の更新
        private void UpdateGroundMassValue3D(string type, double gridStep, double gridMax)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var valuesPath = vm.CanvasGeometry.PathGeoNValues;
            var gridPath = vm.CanvasGeometry.PathGeoNValueGrids;

            foreach (var pile in vm.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;

                int pileBodyIndex = pile.PileBodyNo - 1;
                int groundIndex = pile.GroundNo - 1;

                // インデックス範囲チェック
                if (pileBodyIndex < 0 || pileBodyIndex >= vm.CurrentInputModel.PileBodies.Count) continue;
                if (groundIndex < 0 || groundIndex >= vm.CurrentInputModel.GroundsInput.Count) continue;

                var ground = vm.CurrentInputModel.GroundsInput[groundIndex];
                var masses = ground.GroundMassesData;
                if (masses == null || masses.Count == 0) continue;

                // 1) ポリライン（節点マーカー付き）＋ 数値ラベル用データ
                var points = new List<Point>(masses.Count);
                var labelValues = new List<double>(masses.Count);

                foreach (var m in masses)
                {
                    var p = vm.CanvasThreeDView.Transformation(
                        new Point3D(pile.Point3D.X, pile.Point3D.Y, m.AltitudeDepth));

                    double value = (type == "NValue") ? m.NValue
                                 : (type == "VS0") ? m.VS0
                                 : (type == "Fc") ? m.Fc
                                 : m.Fc;

                    p.X += ComputeGroundMassValueShift(value, gridMax, vm.CanvasThreeDView); // 右へシフト
                    points.Add(p);
                    labelValues.Add(value);
                }

                AddPolyLineGeometryWithMarkers(points, valuesPath, isClosed: false, markerDiameter: 2, markerPathGeometry: valuesPath);

                // 追加: 数値ラベル（各ポリラインの右に表示）
                if (vm.IsSoilValueVisible)
                {
                    string format = "{0:N" + vm.DecimalPlaces + "}";
                    const double labelOffset = 6.0; // ポリライン右側の余白(px)

                    for (int i = 0; i < points.Count; i++)
                    {
                        var pt = points[i];
                        var val = labelValues[i];

                        // 右側に配置: X を少し右へ、Y はそのまま、左右=Left, 上下=Center
                        AddText3D(Brushes.Black, string.Format(format, val),
                                  pt.X + labelOffset, pt.Y, "L", "C", 0.0);
                    }
                }

                // 2) 縦目盛りと上端水平線
                if (ground.GroundLayers == null || ground.GroundLayers.Count == 0) continue;
                double topZ = ground.GroundTopAltitude;
                double btmZ = ground.GroundLayers[^1].BottomAltitude;

                var top2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, topZ));
                var btm2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmZ));

                double xShiftEnd = DrawGroundMassValueScaleGrid(top2D0, btm2D0, gridMax, gridStep, gridPath, vm.CanvasThreeDView);

                // 上端水平線（0～最大目盛位置まで）
                AddLineGeometry(top2D0, new Point(top2D0.X + xShiftEnd, top2D0.Y), gridPath);

                // 3) 層境界の水平線（各BottomAltitudeで0～最大目盛位置まで）
                foreach (var layer in ground.GroundLayers)
                {
                    var p0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, layer.BottomAltitude));
                    AddLineGeometry(p0, new Point(p0.X + xShiftEnd, p0.Y), gridPath);
                }

                // 4) 土層別の半透明塗りつぶし（粘性土=茶, 砂質土=橙, 礫質土=緑）
                AddSoilLayerFillRectangles(vm, pile, ground, topZ, xShiftEnd);
            }
        }

        // 土層パラメータグラフ背景に土層別の半透明塗りつぶし矩形を追加
        private void AddSoilLayerFillRectangles(MainWindowViewModel vm,
            PileLayoutDataItem pile, GroundInput ground,
            double topZ, double xShiftEnd)
        {
            if (ground.GroundLayers == null || ground.GroundLayers.Count == 0) return;
            for (int i = 0; i < ground.GroundLayers.Count; i++)
            {
                var layer = ground.GroundLayers[i];
                double zTop = i == 0 ? topZ : ground.GroundLayers[i - 1].BottomAltitude;
                double zBtm = layer.BottomAltitude;

                var pTopL = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zTop));
                var pBtmL = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zBtm));

                PathGeometry fillPath = layer.GranularityClass switch
                {
                    "粘性土" => vm.CanvasGeometry.PathGeoSoilParamFillClay,
                    "砂質土" => vm.CanvasGeometry.PathGeoSoilParamFillSand,
                    "礫質土" => vm.CanvasGeometry.PathGeoSoilParamFillGravel,
                    _ => null
                };
                if (fillPath == null) continue;

                var rect = new List<Point>
                {
                    pTopL,
                    new(pTopL.X + xShiftEnd, pTopL.Y),
                    new(pBtmL.X + xShiftEnd, pBtmL.Y),
                    pBtmL
                };
                AddPolyLineGeometry(rect, fillPath, isClosed: true);
            }
        }

        // N値→Xオフセット（N=60 で横幅 2.0*Scale）
        private static double ComputeGroundMassValueShift(double value, double gridMax, CanvasThreeDView view)
            => value / gridMax * 2.0 * view.Scale;

        // N目盛りグリッド（縦線群）を描画し、最大NのXオフセットを返す
        private double DrawGroundMassValueScaleGrid(Point top2D0, Point btm2D0, double gridMax, double gridStep, PathGeometry gridPath, CanvasThreeDView view)
        {
            double xShiftEnd = 0.0;
            for (double n = 0; n <= gridMax; n += gridStep)
            {
                double xShift = n / gridMax * 2.0 * view.Scale;
                var top = new Point(top2D0.X + xShift, top2D0.Y);
                var btm = new Point(btm2D0.X + xShift, btm2D0.Y);
                AddLineGeometry(top, btm, gridPath);
                if (Math.Abs(n - gridMax) < double.Epsilon) xShiftEnd = xShift;
            }
            return xShiftEnd;
        }

        //  土層パラメータ表示
        private void UpdateGroundLayerValue3D(string type, double gridStep, double gridMax)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var valuesPath = vm.CanvasGeometry.PathGeoNValues;         // 従来同様のパスを使用
            var gridPath = vm.CanvasGeometry.PathGeoNValueGrids;

            foreach (var pile in vm.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;

                int pileBodyIndex = pile.PileBodyNo - 1;
                int groundIndex = pile.GroundNo - 1;

                // インデックス範囲チェック
                if (pileBodyIndex < 0 || pileBodyIndex >= vm.CurrentInputModel.PileBodies.Count) continue;
                if (groundIndex < 0 || groundIndex >= vm.CurrentInputModel.GroundsInput.Count) continue;

                var ground = vm.CurrentInputModel.GroundsInput[groundIndex];
                var layers = ground.GroundLayers;
                if (layers == null || layers.Count == 0) continue;

                // 2D基準点（上端・下端）
                double topZ = ground.GroundTopAltitude;
                double btmZ = layers[^1].BottomAltitude;

                var top2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, topZ));
                var btm2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmZ));

                // 1) 各層のポリゴンを描画（cohesion に応じて +X 偏位）
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];

                    double zTop = i == 0 ? topZ : layers[i - 1].BottomAltitude;
                    double zBtm = layer.BottomAltitude;

                    var top2D = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zTop));
                    var btm2D = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zBtm));

                    double value = (type == "density") ? layer.Density
                                 : (type == "cohesive") ? layer.Cohesive
                                 : (type == "Vs") ? layer.Vs
                                 : (type == "Es") ? layer.Es
                                 : (type == "NValue") ? layer.NValue
                                 : layer.Es;
                    double xShift = ComputeGridShift(value, gridMax, vm.CanvasThreeDView);

                    // 四角形ポリゴン（上端→上端+dx→下端+dx→下端）
                    var rect = new List<Point>
                    {
                        top2D,
                        new(top2D.X + xShift, top2D.Y),
                        new(btm2D.X + xShift, btm2D.Y),
                        btm2D
                    };
                    AddPolyLineGeometry(rect, valuesPath, isClosed: true);


                    // 追加: 数値ラベル（第2点-第3点の中央に左揃えで表示）
                    if (vm.IsSoilValueVisible)
                    {
                        string format = "{0:N" + vm.DecimalPlaces + "}";
                        double xRight = top2D.X + xShift;                 // 第2点/第3点のX（同じ）
                        double yCenter = (top2D.Y + btm2D.Y) * 0.5;       // 縦中央
                        AddText3D(Brushes.Black, string.Format(format, value),
                                  xRight, yCenter, "L", "C", 0.0);
                    }
                }

                // 2) グリッドの縦線（0～200, 50刻み）と上端・下端の水平線
                double xShiftEnd = DrawScaleGrid(top2D0, btm2D0, gridMax, gridStep, gridPath, vm.CanvasThreeDView);

                // 上端・下端の水平線（0 から最大オフセットまで）
                AddLineGeometry(top2D0, new Point(top2D0.X + xShiftEnd, top2D0.Y), gridPath);
                AddLineGeometry(btm2D0, new Point(btm2D0.X + xShiftEnd, btm2D0.Y), gridPath);

                // 3) 土層別の半透明塗りつぶし（粘性土=茶, 砂質土=橙, 礫質土=緑）
                AddSoilLayerFillRectangles(vm, pile, ground, topZ, xShiftEnd);
            }
        }

        // 粘着力→Xオフセット（C=200 で横幅 2.0*Scale）
        private static double ComputeGridShift(double value, double gridMax, CanvasThreeDView view)
            => value / gridMax * 2.0 * view.Scale;

        // 粘着力目盛りグリッド（縦線群）を描画し、最大CのXオフセットを返す
        private double DrawScaleGrid(Point top2D0, Point btm2D0, double gridMax, double gridStep, PathGeometry gridPath, CanvasThreeDView view)
        {
            double xShiftEnd = 0.0;
            for (double c = 0; c <= gridMax; c += gridStep)
            {
                double xShift = c / gridMax * 2.0 * view.Scale;
                var top = new Point(top2D0.X + xShift, top2D0.Y);
                var btm = new Point(btm2D0.X + xShift, btm2D0.Y);
                AddLineGeometry(top, btm, gridPath);
                if (Math.Abs(c - gridMax) < double.Epsilon) xShiftEnd = xShift;
            }
            return xShiftEnd;
        }
    }
}
