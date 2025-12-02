using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Element = PileDesign.Models.InputData.Element;
using Node = PileDesign.FEM.Node;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using TransformGroup = System.Windows.Media.TransformGroup;

namespace PileDesign.Views
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    /// 




    public partial class MainWindow : Window
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

        //// カラーバージオメトリ取得メソッド
        //public static List<ColorBaredGeometry> GetColorBarGeometries(ObservableCollection<double> values)
        //{
        //    // Diverging カラーバー: 0 をグレー、負側を青へ、正側を赤へ、絶対値が大きい方を完全な青/赤にする
        //    if (values == null || values.Count == 0)
        //    {
        //        return [];
        //    }

        //    double minValue = values.Min();
        //    double maxValue = values.Max();
        //    double maxAbs = Math.Max(Math.Abs(minValue), Math.Abs(maxValue));

        //    // 基準色定義
        //    Color gray = Color.FromRgb(128, 128, 128);
        //    Color blue = Color.FromRgb(0, 0, 255);
        //    Color red = Color.FromRgb(255, 0, 0);

        //    // getColor delegate は GetColorBarGeometriesCore により
        //    // 中央レンジ (middleRange) を min..max に正規化した t (0..1) を受け取ります。
        //    // ここでは t -> middle を復元し、middle/maxAbs に基づくダイバージング着色を行います。
        //    return GetColorBarGeometriesCore(values, t =>
        //    {
        //        // middleRange を復元
        //        double middle = minValue + t * (maxValue - minValue);

        //        if (maxAbs <= 1e-12)
        //        {
        //            // 全てゼロに近い場合はグレー固定
        //            return gray;
        //        }

        //        // -1..1 に正規化
        //        double v = middle / maxAbs;
        //        v = Math.Max(-1.0, Math.Min(1.0, v));

        //        // ゼロ付近はグレー
        //        if (Math.Abs(v) < 1e-9)
        //            return gray;

        //        if (v > 0)
        //        {
        //            // 0(グレー) -> +max(赤)
        //            return LerpColor(gray, red, v);
        //        }
        //        else
        //        {
        //            // 0(グレー) -> -max(青)
        //            return LerpColor(gray, blue, -v);
        //        }
        //    });
        //}

        // 補助: 色の線形補間 (0..1)
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

        // 杭軸力更新メソッド
        public static void UpdateAxialForceLabel3D()
        {
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
                        double dispI = GetDispFromZDataItem(zItemI, level, vm.IsLiquefaction);
                        double dispJ = GetDispFromZDataItem(zItemJ, level, vm.IsLiquefaction);

                        DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, dispI, zJ, dispJ, isFirstSegment: true);
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
                            double dispI = GetDispFromGroundMass(mI, level, vm.IsLiquefaction);
                            double dispJ = GetDispFromGroundMass(mJ, level, vm.IsLiquefaction);

                            DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, dispI, zJ, dispJ, isFirstSegment: j == 0);
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

                        double dispI = GetDispFromGroundMass(mI, level, vm.IsLiquefaction);
                        double dispJ = GetDispFromGroundMass(mJ, level, vm.IsLiquefaction);

                        DrawGroundDispSegment(vm, comb, cos, sin, x, y, zI, dispI, zJ, dispJ, isFirstSegment: j == 0);
                    }
                }
            }
        }

        // 以降: ヘルパー関数群を同クラス内に追加

        private static double ScaleDispMmToModel(double dispMm, LoadCombination comb, MainWindowViewModel vm)
            => dispMm * 0.001 * comb.Alpha1 * vm.DispDiagramMultiplier;

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
            double zI, double dispIMm,
            double zJ, double dispJMm,
            bool isFirstSegment)
        {
            // 1) 変位のスケーリング（mm→モデル座標）
            double dI = ScaleDispMmToModel(dispIMm, comb, vm);
            double dJ = ScaleDispMmToModel(dispJMm, comb, vm);

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
                        AddText3D(Brushes.Brown, string.Format(format, dispIMm),
                            pI1_2D.X, pI1_2D.Y, "C", "C", 0.0);
                    }
                }
                else
                {
                    DrawResultValueTexts(
                        vm.IsResultValueVisible, Brushes.Brown,
                        dispIMm, dispJMm,
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

            if (viewModel.CurrentInputModel.PileLayoutItems.Count > 0)
            {
                var visiblePileLocations = viewModel.CurrentInputModel.PileLayoutItems
                    .Where(pilelocation => pilelocation.IsVisible)
                    .Select(pilelocation => pilelocation.Point3D)
                    .ToList();

                if (visiblePileLocations.Count != 0)
                {
                    viewModel.CanvasThreeDView.SetCt(new ObservableCollection<Point3D>(visiblePileLocations));
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
                    {
                        loadCases = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1;
                    }
                    else /*if (viewModel.SelectedLoad == "レベル2")*/
                    {
                        loadCases = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2;
                    }

                    Point coord0;
                    foreach (LoadCase loadCase in loadCases)
                    {
                        if (viewModel.SelectedDirection == loadCase.LoadAngle)
                        {
                            double x = loadCase.ForceActionPointX;
                            double y = loadCase.ForceActionPointY;
                            double z = loadCase.ForceActionPointAltitude;
                            Point3D loc = new(x, y, z);
                            coord0 = viewModel.CanvasThreeDView.Transformation(loc);
                            break;
                        }
                    }

                    foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (!pilelocation.IsVisible) continue;

                        Point3D loc = pilelocation.Point3D;
                        Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                        // 杭頭節点
                        LineGeometry lineGeometry = new(new Point(coord.X, coord.Y), new Point(coord0.X, coord0.Y));
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
                var doatsuGoryokuBane = viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane;
                foreach (var item in doatsuGoryokuBane.Items)
                {
                    double x1 = item.X1;
                    double x2 = item.X2;
                    double y1 = item.Y1;
                    double y2 = item.Y2;
                    double z1 = item.ZBtm;
                    double z2 = item.ZTop;
                    CreateBoxGeometry(x1, x2, y1, y2, z1, z2);
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
            Point coord111 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y1, z1));
            Point coord211 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y1, z1));
            Point coord121 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y2, z1));
            Point coord112 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y1, z2));
            Point coord221 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y2, z1));
            Point coord212 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y1, z2));
            Point coord122 = viewModel.CanvasThreeDView.Transformation(new Point3D(x1, y2, z2));
            Point coord222 = viewModel.CanvasThreeDView.Transformation(new Point3D(x2, y2, z2));

            DrawLine3D(coord111, coord112, false);
            DrawLine3D(coord211, coord212, false);
            DrawLine3D(coord121, coord122, false);
            DrawLine3D(coord221, coord222, false);

            DrawLine3D(coord111, coord211, false);
            DrawLine3D(coord121, coord221, false);
            DrawLine3D(coord112, coord212, false);
            DrawLine3D(coord122, coord222, false);

            DrawLine3D(coord111, coord121, false);
            DrawLine3D(coord211, coord221, false);
            DrawLine3D(coord112, coord122, false);
            DrawLine3D(coord212, coord222, false);

            DrawLine3D(coord111, coord221, true);
            DrawLine3D(coord211, coord121, true);
            DrawLine3D(coord121, coord211, true);
            DrawLine3D(coord221, coord111, true);

            DrawLine3D(coord112, coord222, true);
            DrawLine3D(coord212, coord122, true);
            DrawLine3D(coord122, coord212, true);
            DrawLine3D(coord222, coord112, true);
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

        

        

        // N値描画の更新
        //private void UpdateNValue3D()
        //{
        //    if (DataContext is not MainWindowViewModel vm) return;

        //    var valuesPath = vm.CanvasGeometry.PathGeoNValues;
        //    var gridPath = vm.CanvasGeometry.PathGeoNValueGrids;

        //    foreach (var pile in vm.CurrentInputModel.PileLayoutItems)
        //    {
        //        if (!pile.IsVisible) continue;

        //        int pileBodyIndex = pile.PileBodyNo - 1;
        //        int groundIndex = pile.GroundNo - 1;

        //        // インデックス範囲チェック
        //        if (pileBodyIndex < 0 || pileBodyIndex >= vm.CurrentInputModel.PileBodies.Count) continue;
        //        if (groundIndex < 0 || groundIndex >= vm.CurrentInputModel.GroundsInput.Count) continue;

        //        var ground = vm.CurrentInputModel.GroundsInput[groundIndex];
        //        var masses = ground.GroundMassesData;
        //        if (masses == null || masses.Count == 0) continue;

        //        // 1) N値ポリライン（節点マーカー付き）
        //        var points = new List<Point>(masses.Count);
        //        foreach (var m in masses)
        //        {
        //            var p = vm.CanvasThreeDView.Transformation(
        //                new Point3D(pile.Point3D.X, pile.Point3D.Y, m.AltitudeDepth));
        //            p.X += ComputeNShift(m.NValue, vm.CanvasThreeDView); // N=60 で横幅 2.0*Scale
        //            points.Add(p);
        //        }
        //        AddPolyLineGeometryWithMarkers(points, valuesPath, isClosed: false, markerDiameter: 2, markerPathGeometry: valuesPath);

        //        // 2) 縦目盛り（0～60, 10毎）と上端水平線
        //        double topZ = ground.GroundTopAltitude;
        //        double btmZ = ground.GroundLayers[^1].BottomAltitude;

        //        var top2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, topZ));
        //        var btm2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmZ));

        //        double xShiftEnd = DrawNScaleGrid(top2D0, btm2D0, maxN: 60, stepN: 10, gridPath, vm.CanvasThreeDView);

        //        // 上端水平線（0～最大N目盛位置まで）
        //        AddLineGeometry(top2D0, new Point(top2D0.X + xShiftEnd, top2D0.Y), gridPath);

        //        // 3) 層境界の水平線（各BottomAltitudeで0～最大N目盛位置まで）
        //        foreach (var layer in ground.GroundLayers)
        //        {
        //            var p0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, layer.BottomAltitude));
        //            AddLineGeometry(p0, new Point(p0.X + xShiftEnd, p0.Y), gridPath);
        //        }
        //    }
        //}

        //// N値→Xオフセット（N=60 で横幅 2.0*Scale）
        //private static double ComputeNShift(double nValue, CanvasThreeDView view)
        //    => nValue / 60.0 * 2.0 * view.Scale;

        //// N目盛りグリッド（縦線群）を描画し、最大NのXオフセットを返す
        //private double DrawNScaleGrid(Point top2D0, Point btm2D0, double maxN, double stepN, PathGeometry gridPath, CanvasThreeDView view)
        //{
        //    double xShiftEnd = 0.0;
        //    for (double n = 0; n <= maxN; n += stepN)
        //    {
        //        // 既存仕様に合わせて分母は固定60（スケール一貫性のため）
        //        double xShift = n / 60.0 * 2.0 * view.Scale;
        //        var top = new Point(top2D0.X + xShift, top2D0.Y);
        //        var btm = new Point(btm2D0.X + xShift, btm2D0.Y);
        //        AddLineGeometry(top, btm, gridPath);
        //        if (Math.Abs(n - maxN) < double.Epsilon) xShiftEnd = xShift;
        //    }
        //    return xShiftEnd;
        //}


        // 粘着力描画の更新
        //private void UpdateCohesive3D()
        //{
        //    if (DataContext is not MainWindowViewModel vm) return;

        //    var valuesPath = vm.CanvasGeometry.PathGeoNValues;         // 従来同様のパスを使用
        //    var gridPath = vm.CanvasGeometry.PathGeoNValueGrids;

        //    foreach (var pile in vm.CurrentInputModel.PileLayoutItems)
        //    {
        //        if (!pile.IsVisible) continue;

        //        int pileBodyIndex = pile.PileBodyNo - 1;
        //        int groundIndex = pile.GroundNo - 1;

        //        // インデックス範囲チェック
        //        if (pileBodyIndex < 0 || pileBodyIndex >= vm.CurrentInputModel.PileBodies.Count) continue;
        //        if (groundIndex < 0 || groundIndex >= vm.CurrentInputModel.GroundsInput.Count) continue;

        //        var ground = vm.CurrentInputModel.GroundsInput[groundIndex];
        //        var layers = ground.GroundLayers;
        //        if (layers == null || layers.Count == 0) continue;

        //        // 2D基準点（上端・下端）
        //        double topZ = ground.GroundTopAltitude;
        //        double btmZ = layers[^1].BottomAltitude;

        //        var top2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, topZ));
        //        var btm2D0 = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmZ));

        //        // 1) 各層のポリゴンを描画（cohesion に応じて +X 偏位）
        //        for (int i = 0; i < layers.Count; i++)
        //        {
        //            var layer = layers[i];

        //            double zTop = i == 0 ? topZ : layers[i - 1].BottomAltitude;
        //            double zBtm = layer.BottomAltitude;

        //            var top2D = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zTop));
        //            var btm2D = vm.CanvasThreeDView.Transformation(new Point3D(pile.Point3D.X, pile.Point3D.Y, zBtm));

        //            double xShift = ComputeCohesionShift(layer.Cohesive, vm.CanvasThreeDView);

        //            // 四角形ポリゴン（上端→上端+dx→下端+dx→下端）
        //            var rect = new List<Point>
        //            {
        //                top2D,
        //                new(top2D.X + xShift, top2D.Y),
        //                new(btm2D.X + xShift, btm2D.Y),
        //                btm2D
        //            };
        //            AddPolyLineGeometry(rect, valuesPath, isClosed: true);
        //        }

        //        // 2) グリッドの縦線（0～200, 50刻み）と上端・下端の水平線
        //        double xShiftEnd = DrawCohesionScaleGrid(top2D0, btm2D0, maxC: 200, stepC: 50, gridPath, vm.CanvasThreeDView);

        //        // 上端・下端の水平線（0 から最大オフセットまで）
        //        AddLineGeometry(top2D0, new Point(top2D0.X + xShiftEnd, top2D0.Y), gridPath);
        //        AddLineGeometry(btm2D0, new Point(btm2D0.X + xShiftEnd, btm2D0.Y), gridPath);
        //    }
        //}

        //// 粘着力→Xオフセット（C=200 で横幅 2.0*Scale）
        //private static double ComputeCohesionShift(double cohesion, CanvasThreeDView view)
        //    => cohesion / 200.0 * 2.0 * view.Scale;

        //// 粘着力目盛りグリッド（縦線群）を描画し、最大CのXオフセットを返す
        //private double DrawCohesionScaleGrid(Point top2D0, Point btm2D0, double maxC, double stepC, PathGeometry gridPath, CanvasThreeDView view)
        //{
        //    double xShiftEnd = 0.0;
        //    for (double c = 0; c <= maxC; c += stepC)
        //    {
        //        double xShift = c / 200.0 * 2.0 * view.Scale;
        //        var top = new Point(top2D0.X + xShift, top2D0.Y);
        //        var btm = new Point(btm2D0.X + xShift, btm2D0.Y);
        //        AddLineGeometry(top, btm, gridPath);
        //        if (Math.Abs(c - maxC) < double.Epsilon) xShiftEnd = xShift;
        //    }
        //    return xShiftEnd;
        //}
        //private void UpdateCohesive3D()
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;

        //    PathGeometry pathGeometry = viewModel.CanvasGeometry.PathGeoNValues;
        //    PathGeometry pathGeometryGrids = viewModel.CanvasGeometry.PathGeoNValueGrids;

        //    foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
        //    {
        //        if (!pilelocation.IsVisible)
        //        { continue; }

        //        int pileBodyIndex = pilelocation.PileBodyNo - 1;
        //        int groundIndex = pilelocation.GroundNo - 1;

        //        // インデックス範囲チェック
        //        if (pileBodyIndex < 0 || pileBodyIndex >= viewModel.CurrentInputModel.PileBodies.Count)
        //            continue; // またはエラー通知

        //        if (groundIndex < 0 || groundIndex >= viewModel.CurrentInputModel.GroundsInput.Count)
        //            continue; // またはエラー通知

        //        Point topCoord0;
        //        Point btmCoord0;
        //        // 粘着力
        //        double topAltitude = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundTopAltitude;
        //        for (int i = 0; i < viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers.Count; i++)
        //        {
        //            List<Point> points = [];
        //            GroundLayerInput groundLayerInput = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers[i];
        //            double topAlt;
        //            double btmAlt;
        //            if (i == 0)
        //            { 
        //                topAlt = topAltitude;
        //                btmAlt = groundLayerInput.BottomAltitude;
        //            }
        //            else
        //            {
        //                topAlt = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers[i - 1].BottomAltitude;
        //                btmAlt = groundLayerInput.BottomAltitude;
        //            }
        //            double cohesion = groundLayerInput.Cohesive;

        //            Point3D top = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, topAlt);
        //            Point3D btm = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, btmAlt);
        //            Point topCoord = viewModel.CanvasThreeDView.Transformation(top);
        //            Point btmCoord = viewModel.CanvasThreeDView.Transformation(btm);
        //            if (i == 0) topCoord0 = viewModel.CanvasThreeDView.Transformation(top);
        //            btmCoord0 = btmCoord;

        //            double xShift = cohesion / 200.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
        //            points.Add(topCoord);
        //            points.Add(new() { X = topCoord.X + xShift, Y = topCoord.Y });
        //            points.Add(new() { X = btmCoord.X + xShift, Y = btmCoord.Y });
        //            points.Add(btmCoord);

        //            AddPolyLineGeometry(points, pathGeometry, true);
        //        }

        //        // 0-200
        //        for (int i = 0; i <= 200; i += 50)
        //        {
        //            double xShift = i / 200.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
        //            Point topCoord = new() { X = topCoord0.X + xShift, Y = topCoord0.Y };
        //            Point btmCoord = new() { X = btmCoord0.X + xShift, Y = btmCoord0.Y };

        //            AddLineGeometry(topCoord, btmCoord, pathGeometryGrids);

        //            if (i == 200)
        //            {
        //                AddLineGeometry(topCoord0, topCoord, pathGeometryGrids);
        //                AddLineGeometry(btmCoord0, btmCoord, pathGeometryGrids);
        //            }
        //        }
        //    }
        //}


        

        // 節点描画の更新
        private void UpdateNodes3D()
        {
            if (Canvas3DLayout == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel == null) return;

            if (viewModel.CurrentInputModel.PileLayoutItems.Count > 0)
            {
                var visiblePileLocations = viewModel.CurrentInputModel.PileLayoutItems
                    .Where(pilelocation => pilelocation.IsVisible)
                    .Select(pilelocation => pilelocation.Point3D)
                    .ToList();

                if (visiblePileLocations.Count != 0)
                {
                    viewModel.CanvasThreeDView.SetCt(new ObservableCollection<Point3D>(visiblePileLocations));
                }
            }

            viewModel.CanvasThreeDView.SetOrg(Canvas3DWidth, Canvas3DHeight);

            foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pilelocation.IsVisible) continue;

                Point3D loc = pilelocation.Point3D;
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                // 杭頭節点
                if (viewModel.IsNodeVisible)
                {
                    EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), actualNodeSize * 0.5, actualNodeSize * 0.5);
                    viewModel.CanvasGeometry.PathGeoPileTopNodes.AddGeometry(ellipse);
                }

                // 節点番号
                if (viewModel.IsNodeNoVisible)
                {
                    AddText3D(Brushes.DarkBlue, GetNodeNoText(pilelocation), coord.X, coord.Y, "L", "B", 0.0);
                }

                // 杭頭ラベル
                if (viewModel.IsLabelVisible)
                {
                    AddText3D(Brushes.Green, GetLabelText(pilelocation), coord.X, coord.Y, "L", "T", 0.0);
                }

                // 杭要素, 杭節点
                UpdatePileElement(pilelocation);
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
                            Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                            EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), actualNodeSize * 0.75, actualNodeSize * 0.75);
                            viewModel.CanvasGeometry.PathGeoActPoint.AddGeometry(ellipse);
                        }
                    }
                }
            }

            viewModel.CanvasGeometry.DrawPileTopNodes(Canvas3DLayout);
            viewModel.CanvasGeometry.DrawElemPath(Canvas3DLayout);

            if (viewModel == null ||
                viewModel.CurrentInputModel == null ||
                viewModel.CurrentInputModel.EmbedmentInput == null ||
                viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers == null)
            { return; }

            // 根入れ部
            if (viewModel.IsElementSplit)
            {
                for (int i = 0; i < InputModel.ElementDivision.DoatsuGoryokuBane.Items.Count; i++)
                {
                    var item = InputModel.ElementDivision.DoatsuGoryokuBane.Items[i];
                    double x1 = item.X1;
                    double x2 = item.X2;
                    double y1 = item.Y1;
                    double y2 = item.Y2;
                    double z1 = item.ZBtm;
                    double z2 = item.ZTop;
                    CreateNeireNodesAndConnectingRod(i, x1, x2, y1, y2, z1, z2);
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
                    CreateNeireNodesAndConnectingRod(i, x1, x2, y1, y2, z1, z2);
                }
            }
        }

        //
        private void CreateNeireNodesAndConnectingRod(int i, double x1, double x2, double y1, double y2, double z1, double z2)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            Point coord0 = viewModel.CanvasThreeDView.Transformation
                        (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, (z1 + z2) * 0.5));

            Point coord001 = viewModel.CanvasThreeDView.Transformation
                (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, z1));

            Point coord002 = viewModel.CanvasThreeDView.Transformation
                (new Point3D((x1 + x2) * 0.5, (y1 + y2) * 0.5, z2));

            DrawLine3D(coord001, coord002, true);

            AddText3D(Brushes.Black, $"{i + 1}", coord0.X, coord0.Y, "L", "C", 0.0);

            // 節点
            if (viewModel.IsNodeVisible)
            {
                EllipseGeometry ellipse1 = new(coord001, actualNodeSize * 0.5, actualNodeSize * 0.5);
                EllipseGeometry ellipse2 = new(coord002, actualNodeSize * 0.5, actualNodeSize * 0.5);
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

        //private void AddColorPolyLineGeometry(
        //    IEnumerable<Point> points,
        //    List<double> values,
        //    List<ColorBaredGeometry> colorBaredGeometries,
        //    bool isClosed = false)
        //{
        //    var pointList = points.ToList();
        //    if (pointList.Count < 2 || values.Count < 2) return;

        //    // 1回だけ前計算
        //    var boundaries = BuildColorBandBoundaries(colorBaredGeometries);

        //    void DrawSegment(Point p1, Point p2, double v1, double v2, Func<double, ColorBaredGeometry?> picker)
        //    {
        //        // 区間内で色が切り替わる境界値を抽出
        //        var splitValues = boundaries.Where(b => (b > Math.Min(v1, v2)) && (b < Math.Max(v1, v2))).ToList();

        //        var segmentValues = new List<double> { v1 };
        //        segmentValues.AddRange(splitValues);
        //        segmentValues.Add(v2);

        //        for (int j = 0; j < segmentValues.Count - 1; j++)
        //        {
        //            double sv1 = segmentValues[j];
        //            double sv2 = segmentValues[j + 1];

        //            double t1 = v2 == v1 ? 0.0 : (sv1 - v1) / (v2 - v1);
        //            double t2 = v2 == v1 ? 1.0 : (sv2 - v1) / (v2 - v1);

        //            Point sp1 = new(p1.X + (p2.X - p1.X) * t1, p1.Y + (p2.Y - p1.Y) * t1);
        //            Point sp2 = new(p1.X + (p2.X - p1.X) * t2, p1.Y + (p2.Y - p1.Y) * t2);

        //            double midValue = (sv1 + sv2) * 0.5;
        //            var colorGeometry = picker(midValue);
        //            colorGeometry?.PathGeometry.AddGeometry(new LineGeometry(sp1, sp2));
        //        }
        //    }


        //    // 通常区間: 既存（最後の帯だけ上端含む）ルール
        //    for (int i = 0; i < pointList.Count - 1; i++)
        //    {
        //        DrawSegment(pointList[i], pointList[i + 1], values[i], values[i + 1],
        //            mid => PickColorGeometry(mid, colorBaredGeometries));
        //    }

        //    // 閉ループの最後の区間だけ、以前のルール（全帯で上端含む）に戻す
        //    if (isClosed)
        //    {
        //        DrawSegment(pointList[^1], pointList[0], values[^1], values[0],
        //            mid => PickColorGeometryInclusiveTop(mid, colorBaredGeometries));
        //    }
        //}
        private void AddColorPolyLineGeometry(
            IEnumerable<Point> points,
            List<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            bool isClosed = false)
        {
            var pointList = points.ToList();
            if (pointList.Count < 2 || values.Count < 2) return;

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

            var vm = DataContext as MainWindowViewModel;
            bool fillAreasEnabled = vm?.IsAreaPainted ?? false;
            if (!fillAreasEnabled)
            {
                // 色分割線だけ必要なら AddColorPolyLineGeometry を使ってください（ここは塗り領域処理専用）
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
        // - markerDiameter: null の場合は acturalNodeSize を使用
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

            foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (pilelocation.IsVisible && pilelocation.IsSelected)
                {
                    Point3D loc = pilelocation.Point3D;
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                    EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), actualNodeSize * 1, actualNodeSize * 1);
                    viewModel.CanvasGeometry.PathGeoSelectedPileNodes.AddGeometry(ellipse);

                    //EllipseGeometry ellipse1 = new(new Point(coord.X, coord.Y), acturalNodeSize * 2, acturalNodeSize * 2);
                    //viewModel.CanvasGeometry.PathGeoSelectedPileNodes.AddGeometry(ellipse1);
                }
            }

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                if (element.IsVisible && element.IsSelected)
                {
                    Point3D loc0 = element.Nodes[0].Point3D;
                    Point3D loc1 = element.Nodes[1].Point3D;

                    if (viewModel.IsShrinkElementMode)
                    {
                        (loc0, loc1) = GetShrinkedElementPoints(loc0, loc1);
                    }

                    Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);
                    Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                    LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                    viewModel.CanvasGeometry.PathGeoSelectedElements.AddGeometry(lineGeometry);
                }
            }
        }

        // 要素番号取得メソッド
        private string GetElementNoText(Element element)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            for (int i = 0; i < viewModel.CurrentInputModel.Elements.Count; i++)
            {
                if (viewModel.CurrentInputModel.Elements[i] == element)
                {
                    return (i + 1).ToString();
                }
            }
            return "0";
        }

        // 節点番号取得メソッド
        private string GetNodeNoText(PileLayoutDataItem pilelocation)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
            {
                if (viewModel.CurrentInputModel.PileLayoutItems[i] == pilelocation)
                {
                    return (i + 1).ToString();
                }
            }
            return "0";
        }

        //// ラベル取得メソッド
        //private string GetLabelText(PileLayoutDataItem pilelocation)
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    string label = string.Empty;

        //    if (viewModel.IsPileRefVisible) label += pilelocation.PileBodyNo.ToString() + ", ";
        //    if (viewModel.IsSoilRefVisible) label += pilelocation.GroundNo.ToString() + ", ";
        //    if (viewModel.IsPileTopLevelVisible) label += pilelocation.Point3D.Z.ToString("N3") + ", ";
        //    if (viewModel.IsGroupPileFactorLabelVisible) label += pilelocation.GroupPileFactor.ToString("N3") + ", ";
        //    if (viewModel.IsPileDiaSpacingRatioLabelVisible) label += pilelocation.PileSpacingFactor.ToString("N3") + ", ";
        //    if (viewModel.IsFrontPileLabelVisible)
        //    {
        //        var selectedLoadCase = LoadCases.GetLoadCase(viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);

        //        if (selectedLoadCase.Level == 1)
        //        {
        //            var loadCases1 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1;
        //            for (int i = 0; i < loadCases1.Count; i++)
        //            {
        //                if (loadCases1[i].LoadName == selectedLoadCase.LoadName)
        //                {
        //                    label += pilelocation.IsFrontPiles[i] == true ? "前, " : "後, ";
        //                    break;
        //                }
        //            }
        //        }
        //        else if (selectedLoadCase.Level == 2)
        //        {
        //            var loadCases2 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2;
        //            for (int i = 0; i < loadCases2.Count; i++)
        //            {
        //                if (loadCases2[i].LoadName == selectedLoadCase.LoadName)
        //                {
        //                    label += pilelocation.IsFrontPiles[i] == true ? "前, " : "後, ";
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //    if (label.Length > 0)
        //    {
        //        label = label[..^2]; // 最後のカンマとスペースを削除
        //    }
        //    return label;
        //}
        //// ラベル取得メソッド（Nullガード強化）
        private string GetLabelText(PileLayoutDataItem pilelocation)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return string.Empty;

            var sb = new System.Text.StringBuilder();

            // 基本ラベル
            if (vm.IsPileRefVisible) sb.Append($"{pilelocation.PileBodyNo}, ");
            if (vm.IsSoilRefVisible) sb.Append($"{pilelocation.GroundNo}, ");
            if (vm.IsPileTopLevelVisible) sb.Append($"{pilelocation.Point3D.Z:N3}, ");
            if (vm.IsGroupPileFactorLabelVisible) sb.Append($"{pilelocation.GroupPileFactor:N3}, ");
            if (vm.IsPileDiaSpacingRatioLabelVisible) sb.Append($"{pilelocation.PileSpacingFactor:N3}, ");

            // 前後杭ラベル
            if (vm.IsFrontPileLabelVisible)
            {
                var lci = vm.CurrentInputModel?.LoadCasesInput;
                var selected = (lci?.AllLoadCases != null)
                    ? LoadCases.GetLoadCase(lci.AllLoadCases, vm.SelectedLoadCaseName)
                    : null;

                if (selected != null && pilelocation.IsFrontPiles != null)
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
                                    if (i < pilelocation.IsFrontPiles.Count)
                                        sb.Append(pilelocation.IsFrontPiles[i] ? "前, " : "後, ");
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
                                    if (i < pilelocation.IsFrontPiles.Count)
                                        sb.Append(pilelocation.IsFrontPiles[i] ? "前, " : "後, ");
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
            foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pilelocation.IsSelected = false;
            }

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                element.IsSelected = false;
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
            if (pileLayoutCollection.Count == 0) { return false; }
            double nearestDistance = double.MaxValue;
            bool hasSelected = false;
            int nearestNo = 9999;
            PileLayoutDataItem nearestPileLayoutDataItem = pileLayoutCollection[0];

            if (isAdd == false)
            {
                ClearCanvasSelection();
            }

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
                }
            }

            // 節点が選択範囲内にある場合の処理
            if (nearestDistance < SelectionTolerance)
            {
                if (viewModel.IsElementAddMode)
                {
                    TextBoxElementNodeInput.Text += nearestNo.ToString() + ", ";
                }
                else
                {
                    nearestPileLayoutDataItem.IsSelected = true;
                    hasSelected = true;
                }
                RequestUpdateCanvas3D(); // UpdateCanvas3D();
                MatchDataGridSelectedItems();
            }

            else // 節点が選択範囲内にない場合の処理 >> 要素と節点の距離
            {
                Element nearestElement = null;
                foreach (Element element in viewModel.CurrentInputModel.Elements)
                {
                    Point3D node0_3D = element.Nodes[0].Point3D;
                    Point3D node1_3D = element.Nodes[1].Point3D;
                    Point node0 = viewModel.CanvasThreeDView.Transformation(node0_3D);
                    Point node1 = viewModel.CanvasThreeDView.Transformation(node1_3D);
                    double distance = GetDistanceBetweenNodeAndLine(node0, node1, clickPosition);

                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestElement = element;
                    }
                }

                // 要素が選択範囲内にある場合の処理
                if (nearestDistance < SelectionTolerance)
                {
                    nearestElement.IsSelected = true;
                    hasSelected = true;
                    RequestUpdateCanvas3D(); // UpdateCanvas3D();
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

            // フラグをリセットしてイベントの処理を再開
            isSelectionChanging = false;
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
            }

            // 選択範囲内のアイテムを選択状態にする
            var selectedPileLocations = viewModel.CurrentInputModel.PileLayoutItems
                .Where(pileLocation => pileLocation.IsVisible)
                .Where(pileLocation =>
                {
                    Point coord = viewModel.CanvasThreeDView.Transformation(pileLocation.Point3D);
                    return x1 <= coord.X && coord.X < x2 && y1 <= coord.Y && coord.Y < y2;
                });

            foreach (var pileLocation in selectedPileLocations)
            {
                pileLocation.IsSelected = true;
            }

            // 選択窓交差選択モード
            if (viewModel.IsCrossSelectionMode)
            {
                foreach (var element in viewModel.CurrentInputModel.Elements)
                {
                    Point3D locS = element.Nodes[0].Point3D;
                    Point3D locE = element.Nodes[1].Point3D;
                    Point coordS = viewModel.CanvasThreeDView.Transformation(locS);
                    Point coordE = viewModel.CanvasThreeDView.Transformation(locE);

                    if (IsLineIntersectingRectangle(coordS, coordE, x1, y1, x2, y2) || IsLineInsideRectangle(coordS, coordE, x1, y1, x2, y2))
                    {
                        element.IsSelected = true;
                    }
                }
            }
            else // 選択窓包絡選択モード
            {
                foreach (var element in viewModel.CurrentInputModel.Elements)
                {
                    Point3D locS = element.Nodes[0].Point3D;
                    Point3D locE = element.Nodes[1].Point3D;
                    Point coordS = viewModel.CanvasThreeDView.Transformation(locS);
                    Point coordE = viewModel.CanvasThreeDView.Transformation(locE);

                    if (IsLineInsideRectangle(coordS, coordE, x1, y1, x2, y2))
                    {
                        element.IsSelected = true;
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