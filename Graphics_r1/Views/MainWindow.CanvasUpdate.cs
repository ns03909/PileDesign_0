using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using TransformGroup = System.Windows.Media.TransformGroup;

namespace PileDesign.Views
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    /// 

    public partial class MainWindow : RibbonWindow
    {
        // カラーバージオメトリコア
        private static List<ColorBaredGeometry> GetColorBarGeometriesCore(
            ObservableCollection<double> values,
            Func<double, Color> getColor)
        {
            if (values.Count == 0)
            {
                return [];
            }

            double maxValue = values.Max();
            double minValue = values.Min();
            double gap = maxValue - minValue;
            List<ColorBaredGeometry> colorBaredGeometries = [];
            if (gap == 0)
            {
                colorBaredGeometries.Add(new()
                {
                    BottomRange = maxValue,
                    TopRange = maxValue,
                    Color = getColor(0.5)
                });
                return colorBaredGeometries;
            }

            int i = 1;
            double roundedGap = 0;
            while (roundedGap == 0)
            {
                roundedGap = RoundToSignificantDigits(gap * Math.Pow(0.1, i), 1);
                i += 1;
            }

            double rangeMin = GetLargestMultipleLessThan(roundedGap, minValue);
            double bottomRange = rangeMin;

            while (bottomRange <= maxValue)
            {
                double topRange = bottomRange + roundedGap;
                double middleRange = (bottomRange + topRange) * 0.5;
                colorBaredGeometries.Add(new()
                {
                    BottomRange = bottomRange,
                    TopRange = topRange,
                    Color = getColor((middleRange - minValue) / (maxValue - minValue))
                });
                bottomRange += roundedGap;
            }
            return colorBaredGeometries;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            byte R = (byte)Math.Round(a.R + (b.R - a.R) * t);
            byte G = (byte)Math.Round(a.G + (b.G - a.G) * t);
            byte B = (byte)Math.Round(a.B + (b.B - a.B) * t);
            return Color.FromRgb(R, G, B);
        }

        // モノクロバージオメトリ取得メソッド
        public static List<ColorBaredGeometry> GetMonoColorBarGeometries(Color color, ObservableCollection<double> values)
        {
            return GetColorBarGeometriesCore(values, _ => color);
        }

        // 荷重組み合わせインデックス取得メソッド
        public int GetLoadCombinationIndex(string loadCombinationString)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations.Count; i++)
            {
                var loadCombination = LoadCombinations.GetLoadCombination(viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, loadCombinationString);
                if (viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations[i] == loadCombination)
                {
                    return i;
                }
            }
            return -1;
        }

        private (int, int) GetLoadLoadCaseIndexLoadCombinationIndex()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            int loadCaseIndex;
            for (loadCaseIndex = 0; loadCaseIndex < viewModel.DirectionOption.Count; loadCaseIndex++)
            {
                if (viewModel.DirectionOption[loadCaseIndex] == viewModel.SelectedDirection.ToString("N1"))
                {
                    break;
                }
            }

            // 整数に変換
            int loadCombinationIndex = GetLoadCombinationIndex(viewModel.SelectedLoadCombinationName);

            return (loadCaseIndex, loadCombinationIndex);
        }

        // 3D地盤変位更新メソッド

        public void UpdateForcedDisplacement3D()
        {
            if (DataContext is not MainWindowViewModel vm) return;

            // 地震時以外は描画対象外
            if (LoadCases.GetLoadCase(vm.CurrentInputModel.LoadCasesInput.AllLoadCases, vm.SelectedLoadCaseName) is not LoadCase selLc)
                return;
            if (selLc.Level is not (1 or 2)) return;

            if (!TryGetLoadContext(vm, out var lc, out var comb, out double cos, out double sin, out int level))
                return;

            // 1) 根入れ（Embedment）ライン
            if (vm.CurrentInputModel.EmbedmentInput != null &&
                vm.CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count != 0)
            {
                if (vm.IsElementSplit)
                {
                    // 分割済: 各アイテム毎に1本ずつ（上端-下端）
                    var items = vm.CurrentInputModel.ElementDivision.DoatsuGoryokuBane.Items;
                    for (int j = 0; j < items.Count; j++)
                    {
                        var it = items[j];
                        double x = it.X0, y = it.Y0;

                        var zItemI = it.ZDataItemTop;
                        var zItemJ = it.ZDataItemBtm;

                        double zI = zItemI.Z, zJ = zItemJ.Z;
                        double displacementI = GetDispFromZDataItem(zItemI, level, vm.IsLiquefaction);
                        double displacementJ = GetDispFromZDataItem(zItemJ, level, vm.IsLiquefaction);

                        DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, displacementI, zJ, displacementJ, isFirstSegment: true);
                    }
                }
                else
                {
                    // 未分割: 埋設領域の中心 (EmbedmentLayer[0] の中心) で地盤土質点列を使用
                    int groundNo = vm.CurrentInputModel.EmbedmentInput.GroundNo;
                    var masses = vm.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData;
                    if (masses.Count >= 2)
                    {
                        var layer0 = vm.CurrentInputModel.EmbedmentInput.EmbedmentLayers[0];
                        double x = (layer0.X1 + layer0.X2) * 0.5;
                        double y = (layer0.Y1 + layer0.Y2) * 0.5;

                        for (int j = 0; j < masses.Count - 1; j++)
                        {
                            var mI = masses[j];
                            var mJ = masses[j + 1];

                            double zI = mI.AltitudeDepth;
                            double zJ = mJ.AltitudeDepth;
                            double displacementI = GetDispFromGroundMass(mI, level, vm.IsLiquefaction);
                            double displacementJ = GetDispFromGroundMass(mJ, level, vm.IsLiquefaction);

                            DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, displacementI, zJ, displacementJ, isFirstSegment: j == 0);
                        }
                    }
                }
            }

            // 2) 杭配置ごとのライン
            foreach (var pile in vm.CurrentInputModel.PileLayoutItems)
            {
                double x = pile.Point3D.X;
                double y = pile.Point3D.Y;

                if (vm.IsElementSplit)
                {
                    // 分割済: SoilPile の ZDataItems 列
                    var soilPile = vm.CurrentInputModel.ElementDivision.SoilPiles[pile.SoilPileAltNo - 1];
                    var zs = soilPile.ZDataItems;
                    for (int j = 0; j < zs.Count - 1; j++)
                    {
                        var zItemI = zs[j];
                        var zItemJ = zs[j + 1];

                        double zI = zItemI.Z;
                        double zJ = zItemJ.Z;

                        double dispI = GetDispFromZDataItem(zItemI, level, vm.IsLiquefaction);
                        double dispJ = GetDispFromZDataItem(zItemJ, level, vm.IsLiquefaction);

                        DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, dispI, zJ, dispJ, isFirstSegment: j == 0);
                    }
                }
                else
                {
                    // 未分割: GroundMassesData 列
                    int groundNo = pile.GroundNo;
                    var masses = vm.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData;
                    for (int j = 0; j < masses.Count - 1; j++)
                    {
                        var mI = masses[j];
                        var mJ = masses[j + 1];

                        double zI = mI.AltitudeDepth;
                        double zJ = mJ.AltitudeDepth;

                        double displacementI = GetDispFromGroundMass(mI, level, vm.IsLiquefaction);
                        double displacementJ = GetDispFromGroundMass(mJ, level, vm.IsLiquefaction);

                        DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, displacementI, zJ, displacementJ, isFirstSegment: j == 0);
                    }
                }
            }
        }

        // 以降: ヘルパー関数群を同クラス内に追加
        private static double ScaleDispMmToModel(double dispMm, LoadCombination comb, MainWindowViewModel vm)
            => dispMm * 0.001 * comb.Alpha1 * vm.DisplacementDiagramRatio * vm.ModelExtent;

        private static double GetDispFromZDataItem(ZDataItem item, int level, bool isLiquefaction)
        {
            if (level == 1)
                return isLiquefaction ? item.GroundDisp1L : item.GroundDisp1;
            else
                return isLiquefaction ? item.GroundDisp2L : item.GroundDisp2;
        }

        private static double GetDispFromGroundMass(GroundMassDataInput mass, int level, bool isLiquefaction)
        {
            int idx = level == 1 ? 0 : 1;
            return isLiquefaction ? mass.DmaxUStarSigmaGammaCyH[idx] : mass.DmaxUStar[idx];
        }

        private bool TryGetLoadContext(
            MainWindowViewModel vm,
            out LoadCase loadCase,
            out LoadCombination loadCombination,
            out double cos, out double sin, out int level)
        {
            loadCase = LoadCases.GetLoadCase(vm.CurrentInputModel.LoadCasesInput.AllLoadCases, vm.SelectedLoadCaseName);
            cos = sin = 0;
            loadCombination = null;
            level = 0;

            if (loadCase == null) return false;
            if (loadCase.Level is not (1 or 2)) return false;

            // 既存のインデックス取得（組合せだけ使う）
            var (_, loadCombinationIndex) = GetLoadLoadCaseIndexLoadCombinationIndex();
            if (loadCombinationIndex < 0 ||
                loadCombinationIndex >= vm.CurrentInputModel.LoadCasesInput.LoadCombinations.Count)
                return false;

            loadCombination = vm.CurrentInputModel.LoadCasesInput.LoadCombinations[loadCombinationIndex];
            double ang = loadCase.LoadAngle * Math.PI / 180.0;
            cos = Math.Cos(ang);
            sin = Math.Sin(ang);
            level = loadCase.Level;
            return true;
        }

        private void DrawGroundDispSegment(
            MainWindowViewModel vm,
            LoadCombination comb,
            double cos, double sin,
            double x, double y,
            double zI, double displacementIMm,
            double zJ, double displacementJMm,
            bool isFirstSegment)
        {
            // 1) 変位のスケーリング（mm→モデル座標）
            double dI = ScaleDispMmToModel(displacementIMm, comb, vm);
            double dJ = ScaleDispMmToModel(displacementJMm, comb, vm);

            double dxI = dI * cos, dyI = dI * sin;
            double dxJ = dJ * cos, dyJ = dJ * sin;

            // 2) 3D→2D 変換（原点/変位後）
            Point3D pI0 = new(x, y, zI);
            Point3D pJ0 = new(x, y, zJ);
            Point3D pI1 = new(x + dxI, y + dyI, zI);
            Point3D pJ1 = new(x + dxJ, y + dyJ, zJ);

            Point pI0_2D = vm.CanvasThreeDView.Transformation(pI0);
            Point pJ0_2D = vm.CanvasThreeDView.Transformation(pJ0);
            Point pI1_2D = vm.CanvasThreeDView.Transformation(pI1);
            Point pJ1_2D = vm.CanvasThreeDView.Transformation(pJ1);

            // 3) 原位置→変位後→変位後→原位置の四角形ポリライン
            var quad = new[] { pI0_2D, pI1_2D, pJ1_2D, pJ0_2D };
            AddPolyLineGeometry(quad, vm.CanvasGeometry.PathGeoGroundDisp);

            // 4) 値表示（従来ロジックを踏襲）
            if (vm.IsResultValueVisible)
            {
                string format = "{0:N" + vm.DecimalPlaces + "}";
                if (vm.IsPileTopResultValueVisibleOnly)
                {
                    if (isFirstSegment)
                    {
                        AddText3D(Brushes.Brown, string.Format(format, displacementIMm),
                            pI1_2D.X, pI1_2D.Y, "C", "C", 0.0);
                    }
                }
                else
                {
                    DrawResultValueTexts(
                        vm.IsResultValueVisible, Brushes.Brown,
                        displacementIMm, displacementJMm,
                        pI1_2D, pJ1_2D,
                        pJ0_2D, pI0_2D,
                        format, format);
                }
            }
        }

        // 剛床描画
        private void UpdateRigidFloor3D()
        {
            if (Canvas3DLayout == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel == null) return;

            if (!viewModel.IsCapturingForExport)
            {
                if (viewModel.CurrentInputModel.PileLayoutItems.Count > 0)
                {
                    var visiblepileLocations = viewModel.CurrentInputModel.PileLayoutItems
                        .Where(pileLocation => pileLocation.IsVisible)
                        .Select(pileLocation => pileLocation.Point3D)
                        .ToList();

                    if (visiblepileLocations.Count != 0)
                    {
                        viewModel.CanvasThreeDView.SetCt(new ObservableCollection<Point3D>(visiblepileLocations));
                    }
                }
            }

            viewModel.CanvasThreeDView.SetOrg(Canvas3DWidth, Canvas3DHeight);

            // 慣性力作用点
            //if (viewModel.IsActionPointVisible)
            {
                var selectedLoadCase = LoadCases.GetLoadCase(viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                if (selectedLoadCase == null) return;
                if (selectedLoadCase.Level == 1 || selectedLoadCase.Level == 2)
                {
                    ObservableCollection<LoadCase> loadCases;
                    if (selectedLoadCase.Level == 1)
                        loadCases = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1;
                    else /*if (viewModel.SelectedLoad == "レベル2")*/
                        loadCases = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2;

                    Point coordinate0;
                    foreach (LoadCase loadCase in loadCases)
                    {
                        if (viewModel.SelectedDirection == loadCase.LoadAngle)
                        {
                            double x = loadCase.ForceActionPointX;
                            double y = loadCase.ForceActionPointY;
                            double z = loadCase.ForceActionPointAltitude;
                            Point3D loc = new(x, y, z);
                            coordinate0 = viewModel.CanvasThreeDView.Transformation(loc);
                            break;
                        }
                    }

                    foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (!pileLocation.IsVisible) continue;

                        // ConnectionNode（杭頭Z + ΔZc）
                        double connectingZ = pileLocation.Z + pileLocation.FoundationBeamDeltaZc;
                        Point3D loc = new(pileLocation.X, pileLocation.Y, connectingZ);
                        Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                        LineGeometry lineGeometry = new(new Point(coordinate.X, coordinate.Y), new Point(coordinate0.X, coordinate0.Y));
                        viewModel.CanvasGeometry.PathGeoRigidFloor.AddGeometry(lineGeometry);
                    }
                }
            }
        }

        // 円錐台の母線を返すメソッド
        private List<LineGeometry> GetConeGeneratrixes2D(Point point2D, double radius1, double radius2, double distance2D, double flattening)
        {
            // flattening = Y / X
            List<LineGeometry> lineGeometries = [];
            double factor = flattening * (radius1 - radius2) / (-distance2D);
            if (factor < -1 || 1 < factor)
            { return lineGeometries; }
            else
            {
                double theta0 = Math.Asin(factor);
                Point point1;
                Point point2;
                for (int i = -1; i <= 1; i += 2)
                {
                    point1 = point2D + new Vector(radius1 * Math.Cos(theta0) * i, flattening * radius1 * Math.Sin(theta0));
                    point2 = point2D + new Vector(radius2 * Math.Cos(theta0) * i, flattening * radius2 * Math.Sin(theta0) - distance2D);

                    lineGeometries.Add(new LineGeometry(point1, point2));
                }
                return lineGeometries;
            }
        }

        // 長さ1のベクトルを返す
        private Point GetAdjustUnit(Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            return distance < Math.Pow(10, -5) ?
                new Point(0, 0) : new Point { X = dx / distance, Y = dy / distance };
        }

        // MainWindow クラス内の任意のフィールド群に追加
        private readonly Dictionary<Color, Path> _settlementBandPaths = [];
        private Path _settlementContoursPath;

        // 根入部描画更新メソッド
        private void UpdateEmbedment3D()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            if (viewModel == null ||
                viewModel.CurrentInputModel == null ||
                viewModel.CurrentInputModel.EmbedmentInput == null ||
                viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers == null ||
                viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count == 0)
            { return; }

            if (viewModel.IsElementSplit)
            {
                // null チェックを追加
                var doatsuGoryokuBane = viewModel.CurrentInputModel.ElementDivision?.DoatsuGoryokuBane;
                if (doatsuGoryokuBane?.Items != null)
                {
                    for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
                    {
                        var item = doatsuGoryokuBane.Items[i];
                        double x1 = item.X1;
                        double x2 = item.X2;
                        double y1 = item.Y1;
                        double y2 = item.Y2;
                        double z1 = item.ZBtm;
                        double z2 = item.ZTop;
                        CreateBoxGeometry(x1, x2, y1, y2, z1, z2);
                    }
                }
            }
            else
            {
                for (int i = 0; i < viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count; i++)
                {
                    EmbedmentDataItem embedmentDataItem = viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers[i];
                    double x1 = embedmentDataItem.X1;
                    double x2 = embedmentDataItem.X2;
                    double y1 = embedmentDataItem.Y1;
                    double y2 = embedmentDataItem.Y2;
                    double z1 = embedmentDataItem.BottomAltitude;
                    double z2 = embedmentDataItem.TopAltitude;
                    CreateBoxGeometry(x1, x2, y1, y2, z1, z2);
                }
            }
        }

        // 箱ジオメトリ作成メソッド
        private void CreateBoxGeometry(double x1, double x2, double y1, double y2, double z1, double z2)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            Point coordinate111 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y1, z1));
            Point coordinate211 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y1, z1));
            Point coordinate121 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y2, z1));
            Point coordinate112 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y1, z2));
            Point coordinate221 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y2, z1));
            Point coordinate212 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y1, z2));
            Point coordinate122 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y2, z2));
            Point coordinate222 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y2, z2));

            DrawLine3D(coordinate111, coordinate112, false);
            DrawLine3D(coordinate211, coordinate212, false);
            DrawLine3D(coordinate121, coordinate122, false);
            DrawLine3D(coordinate221, coordinate222, false);

            DrawLine3D(coordinate111, coordinate211, false);
            DrawLine3D(coordinate121, coordinate221, false);
            DrawLine3D(coordinate112, coordinate212, false);
            DrawLine3D(coordinate122, coordinate222, false);

            DrawLine3D(coordinate111, coordinate121, false);
            DrawLine3D(coordinate211, coordinate221, false);
            DrawLine3D(coordinate112, coordinate122, false);
            DrawLine3D(coordinate212, coordinate222, false);

            DrawLine3D(coordinate111, coordinate221, true);
            DrawLine3D(coordinate211, coordinate121, true);
            DrawLine3D(coordinate121, coordinate211, true);
            DrawLine3D(coordinate221, coordinate111, true);

            DrawLine3D(coordinate112, coordinate222, true);
            DrawLine3D(coordinate212, coordinate122, true);
            DrawLine3D(coordinate122, coordinate212, true);
            DrawLine3D(coordinate222, coordinate112, true);
        }

        // 線分描画メソッド
        private void DrawLine3D(Point point1, Point point2, bool isDiagonal)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            var lineGeometry = new LineGeometry(point1, point2);
            var path = isDiagonal
                ? (viewModel.IsElementSplit
                    ? viewModel.CanvasGeometry.PathGeoDividedEmbedmentDiagonals
                    : viewModel.CanvasGeometry.PathGeoEmbedmentDiagonals)
                : (viewModel.IsElementSplit
                    ? viewModel.CanvasGeometry.PathGeoDividedEmbedmenSides
                    : viewModel.CanvasGeometry.PathGeoEmbedmenSides);
            path.AddGeometry(lineGeometry);
        }

        // 節点描画の更新
        private void UpdateNodes3D()
        {
            if (Canvas3DLayout == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel == null) return;

            if (!viewModel.IsCapturingForExport)
            {
                if (viewModel.CurrentInputModel.PileLayoutItems.Count > 0)
                {
                    var visiblepileLocations = viewModel.CurrentInputModel.PileLayoutItems
                        .Where(pileLocation => pileLocation.IsVisible)
                        .Select(pileLocation => pileLocation.Point3D)
                        .ToList();

                    if (visiblepileLocations.Count != 0)
                    {
                        viewModel.CanvasThreeDView.SetCt(new ObservableCollection<Point3D>(visiblepileLocations));
                    }
                }
            }

            viewModel.CanvasThreeDView.SetOrg(Canvas3DWidth, Canvas3DHeight);

            foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pileLocation.IsVisible) continue;

                Point3D loc = pileLocation.Point3D;
                Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                // 杭頭節点
                if (viewModel.IsNodeVisible)
                {
                    EllipseGeometry ellipse = new(new Point(coordinate.X, coordinate.Y), actualNodeSize * 0.5, actualNodeSize * 0.5);
                    viewModel.CanvasGeometry.PathGeoPileTopNodes.AddGeometry(ellipse);
                }

                // 節点番号
                if (viewModel.IsNodeNoVisible)
                {
                    AddText3D(Brushes.DarkBlue, GetNodeNoText(pileLocation), coordinate.X, coordinate.Y, "L", "B", 0.0);
                }

                // 杭頭ラベル
                if (viewModel.IsLabelVisible)
                {
                    AddText3D(Brushes.Green, GetLabelText(pileLocation), coordinate.X, coordinate.Y, "L", "T", 0.0);
                }
                // 杭要素, 杭節点
                UpdatePileElement(pileLocation);
            }

            // 慣性力作用点
            if (viewModel.IsActionPointVisible)
            {
                var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);

                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

                if (selectedLoadCase != null && selectedLoadCombination != null)
                {
                    foreach (LoadCase loadCase in viewModel.CurrentInputModel.LoadCasesInput.AllSeismicLoadCases)
                    {
                        if (selectedLoadCase.LoadName == loadCase.LoadName)
                        {
                            double x = loadCase.ForceActionPointX;
                            double y = loadCase.ForceActionPointY;
                            double z = loadCase.ForceActionPointAltitude;
                            Point3D loc = new(x, y, z);
                            Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                            EllipseGeometry ellipse = new(new Point(coordinate.X, coordinate.Y), actualNodeSize * 0.75, actualNodeSize * 0.75);
                            viewModel.CanvasGeometry.PathGeoActPoint.AddGeometry(ellipse);
                        }
                    }
                }
            }

            // 注意: DrawPileTopNodesとDrawElemPathはDrawAllPaths内で既に処理されるため、ここでは呼び出さない

            if (viewModel == null ||
                viewModel.CurrentInputModel == null ||
                viewModel.CurrentInputModel.EmbedmentInput == null ||
                viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers == null)
            { return; }

            // 根入れ部
            if (viewModel.IsElementSplit)
            {
                // null チェックを追加
                var doatsuGoryokuBane = viewModel.CurrentInputModel.ElementDivision?.DoatsuGoryokuBane;
                if (doatsuGoryokuBane?.Items != null)
                {
                    for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
                    {
                        var item = doatsuGoryokuBane.Items[i];
                        double x1 = item.X1;
                        double x2 = item.X2;
                        double y1 = item.Y1;
                        double y2 = item.Y2;
                        double z1 = item.ZBtm;
                        double z2 = item.ZTop;
                        CreateBoxGeometry(x1, x2, y1, y2, z1, z2);
                    }
                }
            }
            else
            {
                for (int i = 0; i < viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count; i++)
                {
                    EmbedmentDataItem embedmentDataItem = viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers[i];
                    double x1 = embedmentDataItem.X1;
                    double x2 = embedmentDataItem.X2;
                    double y1 = embedmentDataItem.Y1;
                    double y2 = embedmentDataItem.Y2;
                    double z1 = embedmentDataItem.BottomAltitude;
                    double z2 = embedmentDataItem.TopAltitude;
                    CreateBoxGeometry(x1, x2, y1, y2, z1, z2);
                }
            }
        }

        // 節点と接続ロッドを作成するメソッド
        private void CreateNeireNodesAndConnectingRod(int i, double x1, double x2, double y1, double y2, double z1, double z2)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            Point coordinate0 = viewModel.CanvasThreeDView.Transformation
                        (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, (z1 + z2) * 0.5));

            Point coordinate001 = viewModel.CanvasThreeDView.Transformation
                (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, z1));

            Point coordinate002 = viewModel.CanvasThreeDView.Transformation
                (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, z2));

            DrawLine3D(coordinate001, coordinate002, true);

            AddText3D(Brushes.Black, $"{i + 1}", coordinate0.X, coordinate0.Y, "L", "C", 0.0);

            // 節点
            if (viewModel.IsNodeVisible)
            {
                EllipseGeometry ellipse1 = new(coordinate001, actualNodeSize * 0.5, actualNodeSize * 0.5);
                EllipseGeometry ellipse2 = new(coordinate002, actualNodeSize * 0.5, actualNodeSize * 0.5);
                if (viewModel.IsElementSplit)
                {
                    viewModel.CanvasGeometry.PathGeoDividedEmbedmentDiagonals.AddGeometry(ellipse1);
                    viewModel.CanvasGeometry.PathGeoDividedEmbedmentDiagonals.AddGeometry(ellipse2);
                }
                else
                {
                    viewModel.CanvasGeometry.PathGeoEmbedmentDiagonals.AddGeometry(ellipse1);
                    viewModel.CanvasGeometry.PathGeoEmbedmentDiagonals.AddGeometry(ellipse2);
                }
            }
        }

        // 線分ジオメトリの追加メソッド
        private static void AddLineGeometry(Point start, Point end, PathGeometry pathGeometry)
        {
            var lineGeometry = new LineGeometry
            {
                StartPoint = start,
                EndPoint = end
            };
            pathGeometry.AddGeometry(lineGeometry);
        }

        // 楕円ジオメトリの追加メソッド
        private void AddEllipseGeometry(Point point2, Point point3, PathGeometry pathGeometry)
        {
            var ellipse = new EllipseGeometry(new Point(point2.X, point3.Y), actualNodeSize * 0.5, actualNodeSize * 0.5);
            pathGeometry.AddGeometry(ellipse);
        }


        // カラーバンド境界の一括前計算と、境界内判定ヘルパー
        private static List<double> BuildColorBandBoundaries(List<ColorBaredGeometry> geos)
        {
            // 下端/上端の重複をまとめて昇順に
            return [.. geos.SelectMany(g => new[] { g.BottomRange, g.TopRange })
                       .Distinct()
                       .OrderBy(x => x)];
        }

        // 追加: 閉区間用（全帯で上端を含む）カラー選択
        private static ColorBaredGeometry? PickColorGeometryInclusiveTop(double midValue, List<ColorBaredGeometry> geos)
        {
            for (int i = 0; i < geos.Count; i++)
            {
                var g = geos[i];
                if (g.BottomRange <= midValue && midValue <= g.TopRange) return g;
            }
            return null;
        }

        // 通常区間用（最後の帯のみ上端を含む）カラー選択
        private static ColorBaredGeometry? PickColorGeometry(double midValue, List<ColorBaredGeometry> geos)
        {
            for (int i = 0; i < geos.Count; i++)
            {
                var g = geos[i];
                bool isLast = i == geos.Count - 1;
                if ((g.BottomRange <= midValue && midValue < g.TopRange) ||
                    (isLast && midValue == g.TopRange))
                {
                    return g;
                }
            }
            return null;
        }

        private void AddColorPolyLineGeometry(
            IEnumerable<Point> points,
            List<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            bool isClosed = false)
        {
            var pointList = points.ToList();
            if (pointList.Count < 2 || values.Count < 2) return;

            // NaN/Infinity座標はWPFレンダースレッドをクラッシュさせるためスキップ
            foreach (var p in pointList)
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return;

            // 1回だけ前計算
            var boundaries = BuildColorBandBoundaries(colorBaredGeometries);

            // 範囲の最小/最大（防御的に取得）
            double rangeMin = colorBaredGeometries.Count > 0 ? colorBaredGeometries.First().BottomRange : 0.0;
            double rangeMax = colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last().TopRange : 0.0;

            void DrawSegment(Point p1, Point p2, double v1, double v2, Func<double, ColorBaredGeometry?> picker)
            {
                // 区間内で色が切り替わる境界値を抽出（strict）
                var splitValues = boundaries.Where(b => (b > Math.Min(v1, v2)) && (b < Math.Max(v1, v2))).ToList();

                // 重要: v1 -> v2 の順で分割点列を並べる
                if (v1 <= v2)
                {
                    splitValues = [.. splitValues.OrderBy(x => x)];
                }
                else
                {
                    splitValues = [.. splitValues.OrderByDescending(x => x)];
                }

                var segmentValues = new List<double> { v1 };
                segmentValues.AddRange(splitValues);
                segmentValues.Add(v2);

                for (int j = 0; j < segmentValues.Count - 1; j++)
                {
                    double sv1 = segmentValues[j];
                    double sv2 = segmentValues[j + 1];

                    double t1 = v2 == v1 ? 0.0 : (sv1 - v1) / (v2 - v1);
                    double t2 = v2 == v1 ? 1.0 : (sv2 - v1) / (v2 - v1);

                    Point sp1 = new(p1.X + (p2.X - p1.X) * t1, p1.Y + (p2.Y - p1.Y) * t1);
                    Point sp2 = new(p1.X + (p2.X - p1.X) * t2, p1.Y + (p2.Y - p1.Y) * t2);

                    // 中点値（値空間）→境界外はクリップ
                    double midValue = (sv1 + sv2) * 0.5;
                    if (!double.IsFinite(midValue))
                        continue;
                    midValue = Math.Min(Math.Max(midValue, rangeMin), rangeMax);
                    // 色取得（通常Picker、取得できなければ inclusiveTop をフォールバック）
                    var colorGeometry = picker(midValue) ?? PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);

                    // 最終フォールバック（極端な場合に一応最後の帯を使う）
                    if (colorGeometry == null && colorBaredGeometries.Count > 0)
                    {
                        colorGeometry = colorBaredGeometries.Last();
                    }

                    // 描画
                    colorGeometry?.PathGeometry.AddGeometry(new LineGeometry(sp1, sp2));
                }
            }

            // 通常区間: 既存（最後の帯だけ上端含む）ルール
            for (int i = 0; i < pointList.Count - 1; i++)
            {
                DrawSegment(pointList[i], pointList[i + 1], values[i], values[i + 1],
                    mid => PickColorGeometry(mid, colorBaredGeometries));
            }

            // 閉ループの最後の区間だけ、 inclusive-top ルールを使う
            if (isClosed)
            {
                DrawSegment(pointList[^1], pointList[0], values[^1], values[0],
                    mid => PickColorGeometryInclusiveTop(mid, colorBaredGeometries));
            }
        }

        private void AddColorPolyLineAreaGeometry(
            IEnumerable<Point> points,
            List<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            bool isClosed = false)
        {
            // このメソッドは「4点ポリライン専用」
            var pointList = points.ToList();
            if (pointList.Count != 4 || values == null || values.Count != 4 || colorBaredGeometries == null) return;

            // NaN/Infinity座標はWPFレンダースレッドをクラッシュさせるためスキップ
            foreach (var p in pointList)
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return;

            var vm = DataContext as MainWindowViewModel;
            bool fillAreasEnabled = vm?.IsAreaPainted ?? false;
            if (!fillAreasEnabled)
            {
                // 塗り領域が無効の場合は色分割線で描画（ダイアグラムの輪郭線）
                AddColorPolyLineGeometry(points, values, colorBaredGeometries, isClosed);
                return;
            }

            // カラーバンド境界（昇順）
            var boundaries = BuildColorBandBoundaries(colorBaredGeometries);

            // ポリライン点（期待順: p0=杭I, p1=Iの力点, p2=Jの力点, p3=杭J）
            Point p0 = pointList[0];
            Point p1 = pointList[1];
            Point p2 = pointList[2];
            Point p3 = pointList[3];

            double v0 = values[0];
            double v1 = values[1];
            double v2 = values[2];
            double v3 = values[3];

            // 中央区間 p1-p2 を色分割する（p1 と p2 を必ず含む）
            double segLen = GetDistanceBetweenTwoNodes(p1, p2);
            if (segLen <= 1e-12)
            {
                // degenerate -> 何もしない
                return;
            }

            // 交差するバンド値を抽出して、区間内の t (0..1) を求める
            var crossBounds = boundaries
                .Where(b => (b > Math.Min(v1, v2)) && (b < Math.Max(v1, v2)))
                .ToList();

            // 進行方向に合わせてソート（v1 -> v2 の方向）
            if (v1 <= v2) crossBounds = [.. crossBounds.OrderBy(x => x)];
            else crossBounds = [.. crossBounds.OrderByDescending(x => x)];

            // tList: 0.0, t_cross..., 1.0
            var tList = new List<double> { 0.0 };
            foreach (var bound in crossBounds)
            {
                double t = (v2 == v1) ? 0.0 : (bound - v1) / (v2 - v1);
                t = Math.Max(0.0, Math.Min(1.0, t));
                tList.Add(t);
            }
            tList.Add(1.0);

            // baseline vector (p0 -> p3)
            Vector baselineVec = p3 - p0;

            double maxAllowedDist = Math.Max(50, Math.Max(Canvas3DWidth, Canvas3DHeight) * 2); // はみ出し抑止

            // 各小区間ごとに処理
            for (int i = 0; i < tList.Count - 1; i++)
            {
                double t1 = tList[i];
                double t2 = tList[i + 1];

                // polyline 側の分割点
                Point sp1 = new(p1.X + (p2.X - p1.X) * t1, p1.Y + (p2.Y - p1.Y) * t1);
                Point sp2 = new(p1.X + (p2.X - p1.X) * t2, p1.Y + (p2.Y - p1.Y) * t2);

                // 代表値: 区間中央の値空間
                double valMid;
                {
                    // 中央の値は値空間上の中点（境界値がそのまま入る）
                    double vv1 = v1 + (v2 - v1) * t1;
                    double vv2 = v1 + (v2 - v1) * t2;
                    valMid = (vv1 + vv2) * 0.5;
                }

                // カラー取得（範囲外はクリップして最後の帯を使う）
                double rMin = colorBaredGeometries.Count > 0 ? colorBaredGeometries.First().BottomRange : valMid;
                double rMax = colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last().TopRange : valMid;
                if (!double.IsFinite(valMid)) continue;
                valMid = Math.Min(Math.Max(valMid, rMin), rMax);
                var colorGeo = PickColorGeometry(valMid, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(valMid, colorBaredGeometries) ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);

                // polyline 側の線分を色分割線として追加（見える化）
                colorGeo?.PathGeometry.AddGeometry(new LineGeometry(sp1, sp2));

                // baseline 上の対応点（p0 -> p3 を t1/t2 比で分割）
                Point bp1 = new(p0.X + baselineVec.X * t1, p0.Y + baselineVec.Y * t1);
                Point bp2 = new(p0.X + baselineVec.X * t2, p0.Y + baselineVec.Y * t2);

                // はみ出しチェック（安全）
                if (GetDistanceBetweenTwoNodes(sp1, bp1) > maxAllowedDist || GetDistanceBetweenTwoNodes(sp2, bp2) > maxAllowedDist)
                {
                    continue;
                }

                // 四角形 (sp1 -> sp2 -> bp2 -> bp1) を作成して半透明で塗る
                if (colorGeo != null && Canvas3DLayout != null)
                {
                    var fig = new PathFigure
                    {
                        StartPoint = sp1,
                        IsClosed = true,
                        IsFilled = true
                    };
                    fig.Segments.Add(new PolyLineSegment([sp2, bp2, bp1], true));
                    var poly = new PathGeometry();
                    poly.Figures.Add(fig);
                    if (poly.CanFreeze) poly.Freeze();

                    Color baseColor = colorGeo.Color;
                    Color areaColor = Color.FromArgb(120, baseColor.R, baseColor.G, baseColor.B);
                    var brush = new SolidColorBrush(areaColor);
                    if (brush.CanFreeze) brush.Freeze();

                    var path = new System.Windows.Shapes.Path
                    {
                        Data = poly,
                        Fill = brush,
                        Stroke = Brushes.Transparent,
                        StrokeThickness = 0,
                        IsHitTestVisible = false,
                        CacheMode = new BitmapCache(1.0)
                    };
                    Canvas3DLayout.Children.Add(path);
                }
            }
        }


        //ポリライン
        private void AddPolyLineGeometry(IEnumerable<Point> points, PathGeometry pathGeometry, bool isClosed = false)
        {
            var pointList = points.ToList();
            //if (pointList.Count < 2) return new PathGeometry();

            var pathFigure = new PathFigure
            {
                StartPoint = pointList[0],
                IsClosed = isClosed
            };

            var polyLineSegment = new PolyLineSegment();
            for (int i = 1; i < pointList.Count; i++)
            {
                polyLineSegment.Points.Add(pointList[i]);
            }
            pathFigure.Segments.Add(polyLineSegment);
            pathGeometry.Figures.Add(pathFigure);
        }

        // 追加: ポリライン＋節点マーカー描画メソッド
        // - markerDiameter: null の場合は actualNodeSize を使用
        // - markerPathGeometry: null の場合は polyline と同じ pathGeometry に追加
        // - markEndPointsOnly: 始点/終点のみマーカーを打つ場合 true
        private void AddPolyLineGeometryWithMarkers(
            IEnumerable<Point> points,
            PathGeometry pathGeometry,
            bool isClosed = false,
            double? markerDiameter = null,
            PathGeometry markerPathGeometry = null,
            bool markEndPointsOnly = false)
        {
            var pts = points?.ToList();
            if (pts == null || pts.Count == 0) return;

            // ポリライン本体
            AddPolyLineGeometry(pts, pathGeometry, isClosed);

            // マーカーの出力先
            var targetPath = markerPathGeometry ?? pathGeometry;

            // マーカー直径（未指定なら既定）
            double dia = markerDiameter ?? actualNodeSize;
            if (dia <= 0) return;

            // 打点の選択
            IEnumerable<Point> markerPoints = pts;
            if (markEndPointsOnly && pts.Count >= 2)
            {
                markerPoints = [pts[0], pts[^1]];
            }

            // マーカーを追加
            foreach (var p in markerPoints)
            {
                var ellipse = new EllipseGeometry(p, dia * 0.5, dia * 0.5);
                targetPath.AddGeometry(ellipse);
            }
        }


        // 選択節点更新メソッド
        private void UpdateSelectedNodesAndElements3D()
        {
            // 選択された節点の描画
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (pileLocation.IsVisible && pileLocation.IsSelected)
                {
                    Point3D loc = pileLocation.Point3D;
                    Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                    EllipseGeometry ellipse = new(new Point(coordinate.X, coordinate.Y), actualNodeSize * 1, actualNodeSize * 1);
                    viewModel.CanvasGeometry.PathGeoSelectedPileNodes.AddGeometry(ellipse);
                }
            }

            // 一般節点の選択描画
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (InputNode node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.IsVisible && node.IsSelected)
                    {
                        Point3D loc = new(node.X, node.Y, node.Z);
                        Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                        // 杭節点と同じ選択時サイズ
                        EllipseGeometry ellipse = new(new Point(coordinate.X, coordinate.Y), actualNodeSize * 1, actualNodeSize * 1);

                        // ノードタイプに応じて適切な PathGeometry に追加
                        if (node.Type == NodeType.Pile)
                        {
                            viewModel.CanvasGeometry.PathGeoInputNodesPile.AddGeometry(ellipse);
                        }
                        else
                        {
                            viewModel.CanvasGeometry.PathGeoInputNodesGeneral.AddGeometry(ellipse);
                        }
                    }
                }
            }

            // 基礎梁節点の選択描画（梁要素表示がONの場合のみ）
            if (viewModel.IsFoundationBeamVisible && viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (FoundationNode node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    if (node.IsVisible && node.IsSelected)
                    {
                        Point3D loc = new(node.X, node.Y, node.Z);
                        Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);

                        // 杭節点と同じ選択時サイズ（PathGeoFoundationNodesは選択時には使わず、専用のパスを使う）
                        EllipseGeometry ellipse = new(new Point(coordinate.X, coordinate.Y), actualNodeSize * 1, actualNodeSize * 1);
                        viewModel.CanvasGeometry.PathGeoSelectedPileNodes.AddGeometry(ellipse);
                    }
                }
            }



            // 一般梁要素の選択描画（梁要素表示がONの場合のみ）
            if (viewModel.IsFoundationBeamVisible && viewModel.CurrentInputModel.FoundationBeamInput != null)
            {
                var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;
                var nodeDict = fbInput.Nodes.ToDictionary(n => n.No, n => n);

                foreach (var beam in fbInput.Beams)
                {
                    if (beam.IsSelected && beam.IsVisible)
                    {
                        // 新方式: Type + Guid から座標を解決
                        Point3D? loc0 = null;
                        Point3D? loc1 = null;

                        // NodeI の座標を解決
                        if (beam.NodeI_Id != Guid.Empty)
                        {
                            var coordsI = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                            if (coordsI.HasValue)
                                loc0 = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                        }
                        else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nodeI))
                        {
                            // 旧方式（後方互換性）
                            loc0 = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                        }

                        // NodeJ の座標を解決
                        if (beam.NodeJ_Id != Guid.Empty)
                        {
                            var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                            if (coordsJ.HasValue)
                                loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                        }
                        else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ))
                        {
                            // 旧方式（後方互換性）
                            loc1 = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                        }

                        // 座標が両方とも解決できた場合のみ描画
                        if (!loc0.HasValue || !loc1.HasValue) continue;

                        Point3D point0 = loc0.Value;
                        Point3D point1 = loc1.Value;

                        // 要素縮小モードの場合は端点を縮小
                        if (viewModel.IsShrinkElementMode)
                        {
                            (point0, point1) = GetShrinkElementPoints(point0, point1);
                        }

                        Point coordinate0 = viewModel.CanvasThreeDView.Transformation(point0);
                        Point coordinate1 = viewModel.CanvasThreeDView.Transformation(point1);

                        LineGeometry lineGeometry = new() { StartPoint = coordinate0, EndPoint = coordinate1 };
                        viewModel.CanvasGeometry.PathGeoSelectedElements.AddGeometry(lineGeometry);

                        // 選択された梁要素の断面形状も描画
                        if (viewModel.IsBeamElementSectionVisible)
                        {
                            var fbSections = fbInput.Sections?.ToDictionary(s => s.No, s => s);
                            double bw = beam.Width;
                            double bh = beam.Height;
                            if (fbSections != null && fbSections.TryGetValue(beam.SectionNo, out var sec))
                            {
                                bw = sec.Width;
                                bh = sec.Height;
                            }
                            AddBeamSectionGeometry2D(viewModel, point0, point1, bw, bh, beam.AngleBeta);
                        }
                    }
                }
            }
        }




        // 節点番号取得メソッド
        private string GetNodeNoText(PileLayoutDataItem pileLocation)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
            {
                if (viewModel.CurrentInputModel.PileLayoutItems[i] == pileLocation)
                {
                    return (i + 1).ToString();
                }
            }
            return "0";
        }

        private string GetLabelText(PileLayoutDataItem pileLocation)
        {
            if (DataContext is not MainWindowViewModel vm) return string.Empty;

            var sb = new System.Text.StringBuilder();

            // 基本ラベル
            if (vm.IsPileRefVisible) sb.Append($"{pileLocation.PileBodyNo}, ");
            if (vm.IsSoilRefVisible) sb.Append($"{pileLocation.GroundNo}, ");
            if (vm.IsPileTopLevelVisible) sb.Append($"Z={pileLocation.Point3D.Z:N3}, ");
            if (vm.IsGroupPileFactorLabelVisible) sb.Append($"ξ={pileLocation.GroupPileFactor:N3}, ");
            if (vm.IsPileDiaSpacingRatioLabelVisible) sb.Append($"R/B={pileLocation.PileSpacingFactor:N3}, ");

            // 前後杭ラベル
            if (vm.IsFrontPileLabelVisible)
            {
                var lci = vm.CurrentInputModel?.LoadCasesInput;
                var selected = (lci?.AllLoadCases != null)
                    ? LoadCases.GetLoadCase(lci.AllLoadCases, vm.SelectedLoadCaseName)
                    : null;

                if (selected != null && pileLocation.IsFrontPiles != null)
                {
                    if (selected.Level == 1)
                    {
                        var list = lci?.LoadCasesLevel1;
                        if (list != null)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i]?.LoadName == selected.LoadName)
                                {
                                    if (i < pileLocation.IsFrontPiles.Count)
                                        sb.Append(pileLocation.IsFrontPiles[i] ? "前, " : "後, ");
                                    break;
                                }
                            }
                        }
                    }
                    else if (selected.Level == 2)
                    {
                        var list = lci?.LoadCasesLevel2;
                        if (list != null)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i]?.LoadName == selected.LoadName)
                                {
                                    if (i < pileLocation.IsFrontPiles.Count)
                                        sb.Append(pileLocation.IsFrontPiles[i] ? "前, " : "後, ");
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            var label = sb.ToString();
            if (label.EndsWith(", "))
                label = label[..^2]; // 最後のカンマとスペースを削除
            return label;
        }

        //テキスト追加メソッド
        private void AddText3D(Brush solidColorBrush, string text, double x, double y,
            string horizontalPos, string verticalPos, double textAngle, double scaleY = 1.0)
        {
            // NaN/Infinity座標はWPFレンダースレッドをクラッシュさせるためスキップ
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;

            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = viewModel.LabelSize,
                Foreground = solidColorBrush
            };

            // テキストの幅と高さを測定するために、TextBlockを一時的にCanvasに追加
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size textSize = textBlock.DesiredSize;

            // TextBlockの回転とスケーリングを設定
            TransformGroup transformGroup = new();
            transformGroup.Children.Add(new RotateTransform(textAngle));
            transformGroup.Children.Add(new ScaleTransform(1.0, scaleY, textSize.Width / 2, textSize.Height / 2));
            textBlock.RenderTransform = transformGroup;

            // RenderTransformOriginを設定
            textBlock.RenderTransformOrigin = new Point(0.5, 0.5);

            // スケーリング後のサイズで位置を調整
            Size scaledSize = new(textSize.Width, textSize.Height * scaleY);
            Point adjustedPoint = AdjustTextPosition(new Point(x, y), scaledSize, horizontalPos, verticalPos, textAngle);

            Canvas.SetLeft(textBlock, adjustedPoint.X);
            Canvas.SetTop(textBlock, adjustedPoint.Y);

            // TextBlockInfoに情報を格納し、リストに追加
            var textBlockInfo = new TextBlockInfo
            {
                TextBlock = textBlock,
                X = adjustedPoint.X,
                Y = adjustedPoint.Y,
                TextAngle = textAngle,
                ScaleY = scaleY // 追加
            };

            TextBlockInfos.Add(textBlockInfo);
        }

        private void ClearCanvasSelection()
        {
            var viewModel = _mainWindowViewModel;
            foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pileLocation.IsSelected = false;
            }

            // 一般節点の選択解除
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (InputNode node in viewModel.CurrentInputModel.InputNodes)
                {
                    node.IsSelected = false;
                }
            }

            // 基礎梁節点・要素の選択解除
            if (viewModel.CurrentInputModel.FoundationBeamInput != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    node.IsSelected = false;
                }

                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                {
                    beam.IsSelected = false;
                }
            }

            //UpdateCanvas3D();
            RequestUpdateCanvas3D();
            MatchDataGridSelectedItems();
        }

        // 直近の節点選択メソッド
        private bool SelectNode3DIfNearby(Point clickPosition, bool isAdd)
        {
            var viewModel = _mainWindowViewModel;

            ObservableCollection<PileLayoutDataItem> pileLayoutCollection = viewModel.CurrentInputModel.PileLayoutItems;
            double nearestDistance = double.MaxValue;
            bool hasSelected = false;
            int nearestNo = 9999;
            PileLayoutDataItem? nearestPileLayoutDataItem = null;
            InputNode? nearestInputNode = null;
            FoundationNode? nearestFoundationNode = null;
            string nearestType = ""; // "Pile", "InputNode", or "FoundationNode"

            if (isAdd == false)
            {
                ClearCanvasSelection();
            }

            // 杭節点の距離をチェック
            int no = 0;
            foreach (PileLayoutDataItem pileLayout in pileLayoutCollection)
            {
                no += 1;
                Point3D point = pileLayout.Point3D;
                Point canvasCoordinate = viewModel.CanvasThreeDView.Transformation(point);
                double distance = GetDistanceBetweenTwoNodes(clickPosition, canvasCoordinate);

                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPileLayoutDataItem = pileLayout;
                    nearestNo = no;
                    nearestType = "Pile";
                }
            }

            // 一般節点の距離をチェック
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (InputNode node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (!node.IsVisible) continue;

                    Point3D loc = new(node.X, node.Y, node.Z);
                    Point canvasCoordinate = viewModel.CanvasThreeDView.Transformation(loc);
                    double distance = GetDistanceBetweenTwoNodes(clickPosition, canvasCoordinate);

                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestInputNode = node;
                        nearestType = "InputNode";
                    }
                }
            }

            // 基礎梁節点の距離をチェック
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (FoundationNode node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    if (!node.IsVisible) continue;
                    Point3D loc = new(node.X, node.Y, node.Z);
                    Point canvasCoordinate = viewModel.CanvasThreeDView.Transformation(loc);
                    double distance = GetDistanceBetweenTwoNodes(clickPosition, canvasCoordinate);

                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestFoundationNode = node;
                        nearestType = "FoundationNode";
                    }
                }
            }

            // 最も近い節点が選択範囲内にある場合の処理
            if (nearestDistance < SelectionTolerance)
            {
                if (nearestType == "Pile" && nearestPileLayoutDataItem != null)
                {
                    nearestPileLayoutDataItem.IsSelected = true;
                    hasSelected = true;
                }
                else if (nearestType == "InputNode" && nearestInputNode != null)
                {
                    nearestInputNode.IsSelected = true;
                    hasSelected = true;
                }
                else if (nearestType == "FoundationNode" && nearestFoundationNode != null)
                {
                    nearestFoundationNode.IsSelected = true;
                    hasSelected = true;
                }
                RequestUpdateCanvas3D(); // UpdateCanvas3D();
                MatchDataGridSelectedItems();
            }
            else // 節点が選択範囲内にない場合の処理 >> 要素と節点の距離
            {
                FoundationBeamElement? nearestBeam = null;

                // 梁要素をチェック
                if (viewModel.CurrentInputModel.FoundationBeamInput != null)
                {
                    var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;
                    var nodeDict = fbInput.Nodes.ToDictionary(n => n.No, n => n);

                    foreach (var beam in fbInput.Beams)
                    {
                        if (!beam.IsVisible) continue;

                        // 新方式: Type + Guid から座標を解決（描画側と同じロジック）
                        Point3D? loc0 = null;
                        Point3D? loc1 = null;

                        if (beam.NodeI_Id != Guid.Empty)
                        {
                            var coordsI = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                            if (coordsI.HasValue)
                                loc0 = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                        }
                        else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nodeI))
                        {
                            loc0 = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                        }

                        if (beam.NodeJ_Id != Guid.Empty)
                        {
                            var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                            if (coordsJ.HasValue)
                                loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                        }
                        else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ))
                        {
                            loc1 = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                        }

                        if (!loc0.HasValue || !loc1.HasValue) continue;

                        Point node0 = viewModel.CanvasThreeDView.Transformation(loc0.Value);
                        Point node1 = viewModel.CanvasThreeDView.Transformation(loc1.Value);
                        double distance = GetDistanceBetweenNodeAndLine(node0, node1, clickPosition);

                        if (distance <= nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestBeam = beam;
                        }
                    }
                }

                // 要素が選択範囲内にある場合の処理
                if (nearestDistance < SelectionTolerance && nearestBeam != null)
                {
                    nearestBeam.IsSelected = true;
                    hasSelected = true;
                    RequestUpdateCanvas3D(); // UpdateCanvas3D();
                    MatchDataGridSelectedItems();
                }
            }
            return hasSelected;
        }

        private void MatchDataGridSelectedItems()
        {
            var viewModel = _mainWindowViewModel;
            // フラグをセットしてイベントの処理を停止
            isSelectionChanging = true;

            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();

            foreach (var pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (pileLocation.IsSelected)
                {
                    DataGridPileLayout.SelectedItems.Add(pileLocation);
                    DataGridPileAxialForce.SelectedItems.Add(pileLocation);
                    DataGridIsFrontPile.SelectedItems.Add(pileLocation);
                }
            }

            // 一般節点DataGridの選択を同期
            if (DataGridInputNodes != null && viewModel.CurrentInputModel.InputNodes != null)
            {
                DataGridInputNodes.SelectedItems.Clear();
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.IsSelected)
                    {
                        DataGridInputNodes.SelectedItems.Add(node);
                    }
                }
            }

            // 一般梁要素DataGridの選択を同期
            if (DataGridFoundationBeams != null && viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                DataGridFoundationBeams.SelectedItems.Clear();
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                {
                    if (beam.IsSelected)
                    {
                        DataGridFoundationBeams.SelectedItems.Add(beam);
                    }
                }
            }

            // フラグをリセットしてイベントの処理を再開
            isSelectionChanging = false;

            // ステータスバーの選択数表示を更新
            viewModel.RaisePropertyChanged(nameof(viewModel.SelectionCountText));

            // プロパティパネルの更新
            viewModel.UpdatePropertyPanel();
        }

        // 2点間の距離を返すメソッド
        private static double GetDistanceBetweenTwoNodes(Point p1, Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
        private static double GetDistanceBetweenNodeAndLine(Point lineStart, Point lineEnd, Point p)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            if (dx == 0 && dy == 0)
            {
                // lineStart と lineEnd が同じ点の場合
                return GetDistanceBetweenTwoNodes(lineStart, p);
            }

            // 線分の長さの二乗
            double lineLengthSquared = dx * dx + dy * dy;

            // 点 p から線分の始点 lineStart へのベクトル
            double t = ((p.X - lineStart.X) * dx + (p.Y - lineStart.Y) * dy) / lineLengthSquared;

            if (t < 0)
            {
                // 点 p が線分の外側で lineStart に最も近い場合
                return GetDistanceBetweenTwoNodes(lineStart, p);
            }
            else if (t > 1)
            {
                // 点 p が線分の外側で lineEnd に最も近い場合
                return GetDistanceBetweenTwoNodes(lineEnd, p);
            }

            // 点 p から線分上の最近接点へのベクトル
            Point projection = new(lineStart.X + t * dx, lineStart.Y + t * dy);
            return GetDistanceBetweenTwoNodes(projection, p);
        }

        private bool isSelectionChanging = false;

        private void ConfirmSelection3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double x1 = Math.Min(startPoint.X, endPoint.X);
            double x2 = Math.Max(startPoint.X, endPoint.X);
            double y1 = Math.Min(startPoint.Y, endPoint.Y);
            double y2 = Math.Max(startPoint.Y, endPoint.Y);

            // すべてのアイテムの選択状態をリセット
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                foreach (var pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    pileLocation.IsSelected = false;
                }

                foreach (var element in viewModel.CurrentInputModel.Elements)
                {
                    element.IsSelected = false;
                }

                // 一般節点の選択状態をリセット
                if (viewModel.CurrentInputModel.InputNodes != null)
                {
                    foreach (var node in viewModel.CurrentInputModel.InputNodes)
                    {
                        node.IsSelected = false;
                    }
                }

                // 基礎梁節点・要素の選択状態をリセット
                if (viewModel.CurrentInputModel.FoundationBeamInput != null)
                {
                    foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                    {
                        node.IsSelected = false;
                    }

                    foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    {
                        beam.IsSelected = false;
                    }
                }
            }

            // 選択範囲内のアイテムを選択状態にする
            var selectedPileLocations = viewModel.CurrentInputModel.PileLayoutItems
                .Where(pileLocation => pileLocation.IsVisible)
                .Where(pileLocation =>
                {
                    Point coordinate = viewModel.CanvasThreeDView.Transformation(pileLocation.Point3D);
                    return x1 <= coordinate.X && coordinate.X < x2 && y1 <= coordinate.Y && coordinate.Y < y2;
                });

            foreach (var pileLocation in selectedPileLocations)
            {
                pileLocation.IsSelected = true;
            }

            // 一般節点の選択
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                var selectedInputNodes = viewModel.CurrentInputModel.InputNodes
                    .Where(node => node.IsVisible)
                    .Where(node =>
                    {
                        Point3D loc = new(node.X, node.Y, node.Z);
                        Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);
                        return x1 <= coordinate.X && coordinate.X < x2 && y1 <= coordinate.Y && coordinate.Y < y2;
                    });

                foreach (var node in selectedInputNodes)
                {
                    node.IsSelected = true;
                }
            }

            // 基礎梁節点の選択
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                var selectedFoundationNodes = viewModel.CurrentInputModel.FoundationBeamInput.Nodes
                    .Where(node => node.IsVisible)
                    .Where(node =>
                    {
                        Point3D loc = new(node.X, node.Y, node.Z);
                        Point coordinate = viewModel.CanvasThreeDView.Transformation(loc);
                        return x1 <= coordinate.X && coordinate.X < x2 && y1 <= coordinate.Y && coordinate.Y < y2;
                    });

                foreach (var node in selectedFoundationNodes)
                {
                    node.IsSelected = true;
                }
            }

            // 選択窓交差選択モード
            if (viewModel.IsCrossSelectionMode)
            {
                foreach (var element in viewModel.CurrentInputModel.Elements)
                {
                    Point3D locS = element.Nodes[0].Point3D;
                    Point3D locE = element.Nodes[1].Point3D;
                    Point coordinateS = viewModel.CanvasThreeDView.Transformation(locS);
                    Point coordinateE = viewModel.CanvasThreeDView.Transformation(locE);

                    if (IsLineIntersectingRectangle(coordinateS, coordinateE, x1, y1, x2, y2) || IsLineInsideRectangle(coordinateS, coordinateE, x1, y1, x2, y2))
                    {
                        element.IsSelected = true;
                    }
                }

                // 一般梁要素の交差選択
                if (viewModel.CurrentInputModel.FoundationBeamInput != null)
                {
                    var nodeDict = viewModel.CurrentInputModel.FoundationBeamInput.Nodes.ToDictionary(n => n.No, n => n);

                    foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    {
                        if (!beam.IsVisible) continue;

                        // 新方式: Type + Guid から座標を解決
                        Point3D? locS = null;
                        Point3D? locE = null;

                        // NodeI の座標を解決
                        if (beam.NodeI_Id != Guid.Empty)
                        {
                            var coordsI = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                            if (coordsI.HasValue)
                                locS = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                        }
                        else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nodeI))
                        {
                            // 旧方式（後方互換性）
                            locS = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                        }

                        // NodeJ の座標を解決
                        if (beam.NodeJ_Id != Guid.Empty)
                        {
                            var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                            if (coordsJ.HasValue)
                                locE = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                        }
                        else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ))
                        {
                            // 旧方式（後方互換性）
                            locE = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                        }

                        // 座標が両方とも解決できた場合のみ選択判定
                        if (!locS.HasValue || !locE.HasValue) continue;

                        Point coordinateS = viewModel.CanvasThreeDView.Transformation(locS.Value);
                        Point coordinateE = viewModel.CanvasThreeDView.Transformation(locE.Value);

                        if (IsLineIntersectingRectangle(coordinateS, coordinateE, x1, y1, x2, y2) || IsLineInsideRectangle(coordinateS, coordinateE, x1, y1, x2, y2))
                        {
                            beam.IsSelected = true;
                        }
                    }
                }
            }
            else // 選択窓包絡選択モード
            {
                foreach (var element in viewModel.CurrentInputModel.Elements)
                {
                    Point3D locS = element.Nodes[0].Point3D;
                    Point3D locE = element.Nodes[1].Point3D;
                    Point coordinateS = viewModel.CanvasThreeDView.Transformation(locS);
                    Point coordinateE = viewModel.CanvasThreeDView.Transformation(locE);

                    if (IsLineInsideRectangle(coordinateS, coordinateE, x1, y1, x2, y2))
                    {
                        element.IsSelected = true;
                    }
                }

                // 一般梁要素の包絡選択
                if (viewModel.CurrentInputModel.FoundationBeamInput != null)
                {
                    var nodeDict = viewModel.CurrentInputModel.FoundationBeamInput.Nodes.ToDictionary(n => n.No, n => n);

                    foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    {
                        if (!beam.IsVisible) continue;

                        // 新方式: Type + Guid から座標を解決
                        Point3D? locS = null;
                        Point3D? locE = null;

                        // NodeI の座標を解決
                        if (beam.NodeI_Id != Guid.Empty)
                        {
                            var coordsI = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                            if (coordsI.HasValue)
                                locS = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                        }
                        else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nodeI))
                        {
                            // 旧方式（後方互換性）
                            locS = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                        }

                        // NodeJ の座標を解決
                        if (beam.NodeJ_Id != Guid.Empty)
                        {
                            var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                            if (coordsJ.HasValue)
                                locE = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                        }
                        else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ))
                        {
                            // 旧方式（後方互換性）
                            locE = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                        }

                        // 座標が両方とも解決できた場合のみ選択判定
                        if (!locS.HasValue || !locE.HasValue) continue;

                        Point coordinateS = viewModel.CanvasThreeDView.Transformation(locS.Value);
                        Point coordinateE = viewModel.CanvasThreeDView.Transformation(locE.Value);

                        if (IsLineInsideRectangle(coordinateS, coordinateE, x1, y1, x2, y2))
                        {
                            beam.IsSelected = true;
                        }
                    }
                }
            }
            MatchDataGridSelectedItems();
            //UpdateCanvas3D();
            RequestUpdateCanvas3D();
        }

        private bool IsLineIntersectingRectangle(Point p1, Point p2, double x1, double y1, double x2, double y2)
        {
            // 四角形の4つの辺を定義
            var rectLines = new List<(Point, Point)>
            {
                (new Point(x1, y1), new Point(x2, y1)), // 上辺
                (new Point(x2, y1), new Point(x2, y2)), // 右辺
                (new Point(x2, y2), new Point(x1, y2)), // 下辺
                (new Point(x1, y2), new Point(x1, y1))  // 左辺
            };

            // 各辺と線分が交差するかどうかを判定
            foreach (var (rectP1, rectP2) in rectLines)
            {
                if (DoLinesIntersect(p1, p2, rectP1, rectP2))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsLineInsideRectangle(Point p1, Point p2, double x1, double y1, double x2, double y2)
        {
            return (x1 <= p1.X && p1.X <= x2 && y1 <= p1.Y && p1.Y <= y2 &&
                   x1 <= p2.X && p2.X <= x2 && y1 <= p2.Y && p2.Y <= y2);
        }

        private bool DoLinesIntersect(Point p1, Point p2, Point p3, Point p4)
        {
            double d1 = Direction(p3, p4, p1);
            double d2 = Direction(p3, p4, p2);
            double d3 = Direction(p1, p2, p3);
            double d4 = Direction(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            if (d1 == 0 && OnSegment(p3, p4, p1)) return true;
            if (d2 == 0 && OnSegment(p3, p4, p2)) return true;
            if (d3 == 0 && OnSegment(p1, p2, p3)) return true;
            if (d4 == 0 && OnSegment(p1, p2, p4)) return true;

            return false;
        }

        private double Direction(Point pi, Point pj, Point pk)
        {
            return (pk.X - pi.X) * (pj.Y - pi.Y) - (pj.X - pi.X) * (pk.Y - pi.Y);
        }
        private bool OnSegment(Point pi, Point pj, Point pk)
        {
            return Math.Min(pi.X, pj.X) <= pk.X && pk.X <= Math.Max(pi.X, pj.X) &&
                   Math.Min(pi.Y, pj.Y) <= pk.Y && pk.Y <= Math.Max(pi.Y, pj.Y);
        }
        // ホバーハイライト: マウス直下の最も近い節点/要素をハイライト表示
        // 戻り値: 最近接の杭節点/一般節点のワールドZ座標（スナップ範囲内の場合）、範囲外はnull
        private double? UpdateHoverHighlight(Point mousePos)
        {
            var viewModel = _mainWindowViewModel;
            if (viewModel?.CurrentInputModel == null) return null;

            var canvasGeo = viewModel.CanvasGeometry;
            canvasGeo.PathGeoHoverNode.Figures.Clear();
            canvasGeo.PathGeoHoverElement.Figures.Clear();

            double nearestNodeDist = double.MaxValue;
            Point nearestNodeCoord = default;
            double? nearestPileOrGeneralZ = null; // 杭/一般節点のスナップZ

            double nearestElemDist = double.MaxValue;
            Point nearestElemStart = default;
            Point nearestElemEnd = default;

            // 杭節点
            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;
                Point coord = viewModel.CanvasThreeDView.Transformation(pile.Point3D);
                double dist = GetDistanceBetweenTwoNodes(mousePos, coord);
                if (dist < nearestNodeDist)
                {
                    nearestNodeDist = dist;
                    nearestNodeCoord = coord;
                    nearestPileOrGeneralZ = pile.Z;
                }
            }

            // 一般節点（General）
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (!node.IsVisible || node.Type != NodeType.General) continue;
                    Point3D loc = new(node.X, node.Y, node.Z);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    double dist = GetDistanceBetweenTwoNodes(mousePos, coord);
                    if (dist < nearestNodeDist)
                    {
                        nearestNodeDist = dist;
                        nearestNodeCoord = coord;
                        nearestPileOrGeneralZ = node.Z;
                    }
                }
            }

            // 基礎梁節点（ハイライト対象だがスナップZ更新対象外）
            if (viewModel.IsFoundationBeamVisible && viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    if (!node.IsVisible) continue;
                    Point3D loc = new(node.X, node.Y, node.Z);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    double dist = GetDistanceBetweenTwoNodes(mousePos, coord);
                    if (dist < nearestNodeDist)
                    {
                        nearestNodeDist = dist;
                        nearestNodeCoord = coord;
                        // 基礎梁節点ではスナップZを更新しない（杭/一般節点のみ）
                        nearestPileOrGeneralZ = null;
                    }
                }
            }

            // 基礎梁要素
            if (viewModel.IsFoundationBeamVisible && viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                var nodeDict = viewModel.CurrentInputModel.FoundationBeamInput.Nodes.ToDictionary(n => n.No, n => n);
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                {
                    if (!beam.IsVisible) continue;
                    if (!nodeDict.TryGetValue(beam.NodeI_No, out var nodeI)) continue;
                    if (!nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ)) continue;

                    Point3D loc0 = new(nodeI.X, nodeI.Y, nodeI.Z);
                    Point3D loc1 = new(nodeJ.X, nodeJ.Y, nodeJ.Z);
                    Point p0 = viewModel.CanvasThreeDView.Transformation(loc0);
                    Point p1 = viewModel.CanvasThreeDView.Transformation(loc1);
                    double dist = GetDistanceBetweenNodeAndLine(p0, p1, mousePos);
                    if (dist < nearestElemDist)
                    {
                        nearestElemDist = dist;
                        nearestElemStart = p0;
                        nearestElemEnd = p1;
                    }
                }
            }

            // 最も近い節点をハイライト（要素より節点を優先）
            if (nearestNodeDist < SelectionTolerance && nearestNodeDist <= nearestElemDist)
            {
                double r = actualNodeSize * 1.5;
                EllipseGeometry ellipse = new(nearestNodeCoord, r, r);
                canvasGeo.PathGeoHoverNode.AddGeometry(ellipse);
                return nearestPileOrGeneralZ;
            }
            // 最も近い要素をハイライト
            else if (nearestElemDist < SelectionTolerance)
            {
                LineGeometry line = new() { StartPoint = nearestElemStart, EndPoint = nearestElemEnd };
                canvasGeo.PathGeoHoverElement.AddGeometry(line);
            }
            return null;
        }

        private void ClearHoverHighlight()
        {
            var viewModel = _mainWindowViewModel;
            if (viewModel?.CanvasGeometry == null) return;
            viewModel.CanvasGeometry.PathGeoHoverNode.Figures.Clear();
            viewModel.CanvasGeometry.PathGeoHoverElement.Figures.Clear();
        }

        private void DataGridPileLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                if (FindResource("NodeContextMenu") is ContextMenu cm)
                {
                    cm.PlacementTarget = sender as UIElement;
                    cm.IsOpen = true;
                }
            }
        }
    }
}