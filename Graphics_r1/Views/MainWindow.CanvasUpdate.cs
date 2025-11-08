//using DocumentFormat.OpenXml.Wordprocessing;
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

        private bool isLightweightDrawing = false;

        public void UpdateCanvas3D()
        {
            Canvas3DLayout?.Children.Clear();

            if (DataContext is not MainWindowViewModel viewModel) return;


            viewModel.CanvasGeometry.Clear();
            TextBlockInfos.Clear();

            if (isLightweightDrawing)
            {    // 軽量描画: ノード・要素・軸・グリッドのみ
                UpdateNodes3D();
                if (viewModel.IsXYZAxesVisible) UpdateAxes3D();
                UpdateCanvasCube(); // XYZキューブの更新

                // 追加: 組み立てたジオメトリを実際に描画する
                viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);

                return;
            }

            ColorBarCanvas?.Children.Clear();

            UpdateNodes3D(); // 節点描画の更新

            UpdateCanvasCube(); // XYZキューブの更新

            UpdateSelectedNodesAndElements3D(); // 選択節点描画の更新

            if (viewModel.IsEmbedmentBoxVisible) UpdateEmbedment3D(); // 根入部描画の更新

            if (viewModel.IsXYZAxesVisible) UpdateAxes3D(); // XYZ軸の更新

            if (viewModel.IsGroundVisible) UpdateGround3D(); // 杭周地盤描画の更新

            if (viewModel.IsNValueVisible) UpdateGroundMassValue3D("NValue", 10, 60); // N値描画の更新

            if (viewModel.IsVS0Visible) UpdateGroundMassValue3D("VS0", 100, 400); // VS0描画の更新

            if (viewModel.IsFcVisible) UpdateGroundMassValue3D("Fc", 20, 100); // Fc描画の更新

            if (viewModel.IsDensityVisible) UpdateGroundLayerValue3D("density", 5, 25); // 密度描画の更新

            if (viewModel.IsCohesiveVisible) UpdateGroundLayerValue3D("cohesive", 50, 200); // 粘着力描画の更新

            if (viewModel.IsVsVisible) UpdateGroundLayerValue3D("Vs", 100, 500); // Vs描画の更新

            if (viewModel.IsEsVisible) UpdateGroundLayerValue3D("Es", 10000, 50000); // Es描画の更新

            if (viewModel.IsSettlementLoadVisible) UpdateSettlementLoad3D(); // 荷重面描画の更新

            if (viewModel.IsElementVisible) UpdateGeneralElement3D(); // 要素描画の更新

            if ((MainWindowViewModel)DataContext == null) return;

            // 平面図の場合
            if (viewModel.CanvasThreeDView.Phi == 90 && viewModel.CanvasThreeDView.IsPerspective == false)
            {
                if (viewModel.IsTickMarkVisible) UpdateTickMarks3DPlan();  // 目盛りの更新
                if (viewModel.IsGridLineVisible) UpdateGridLines3DPlan(); // 通り心の更新

                if (viewModel.CanvasThreeDView.Tht == -90) // XY（平面）の場合
                {
                    if (viewModel.IsGridLineVisible) UpdateDimensionLines3DPlan();
                }
            }

            // 側面図の場合
            else if (viewModel.CanvasThreeDView.Phi == 0 && viewModel.CanvasThreeDView.IsPerspective == false)
            {

                if (viewModel.IsSettlementGroundVisible) UpdateSettlementGround3D(); // 側面図用沈下描画の更新
                UpdateTickMarks3DElevation(); // 目盛りの更新

                if (viewModel.CanvasThreeDView.Tht == 0) // YZ（右側面）の場合
                {
                    if (viewModel.IsTickMarkVisible) UpdateTickMarks3DYofYZ();
                    if (viewModel.IsGridLineVisible) UpdateGridLinesAndDimensionsYforYZ(); // 通り心の更新
                }

                if (viewModel.CanvasThreeDView.Tht == -90) // XZ（正面）の場合
                {
                    if (viewModel.IsTickMarkVisible) UpdateTickMarks3DXofXZ();
                    if (viewModel.IsGridLineVisible) UpdateGridLinesAndDimensionsXforXZ(); // 通り心の更新
                }
            }

            else
            {
                if (viewModel.IsGridLineVisible) UpdateGridLines3D(); // 通り心の更新
            }

            //if (viewModel.IsLoadingVisible) UpdateLoading3D(); // 軸力・慣性力の描画
            if (viewModel.IsMassLoadingVisible || viewModel.IsAxialLoadingVisible) UpdateLoading3D(); // 軸力・慣性力の描画
            //if (viewModel.IsAxialForceLabelVisible) UpdateAxialForceLabel3D(); // 杭軸力の描画

            if (viewModel.IsForcedDisplacementVisible) UpdateForcedDisplacement3D(); // 3D地盤変位更新メソッド

            if (viewModel.IsAnalysisResultVisible) UpdateAnalysisResult3D(); // 解析結果の描画
            //else
            //{
            //    // ColorBarCanvasの内容をクリア
            //    //ColorBarCanvas?.Children.Clear();
            //}

            // 剛床の描画
            if (viewModel.IsRigidFloorVisible) UpdateRigidFloor3D();

            // 群杭沈下グリッドの描画
            if (viewModel.IsGroupPileGridVisible) UpdateGroupPileGrid3D();

            // 変形後沈下グリッドの描画
            if (viewModel.IsGroupPileGridDeformationVisible) UpdateSettlementGridDeformation(); // 群杭沈下地盤変位の描画

            // 全てのパスを描画
            viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);

            // テキスト一括レンダリング
            RenderTextBlocksWithDrawingVisual();
        }

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

        // カラーバージオメトリ取得メソッド
        public static List<ColorBaredGeometry> GetColorBarGeometries(ObservableCollection<double> values)
        {
            return GetColorBarGeometriesCore(values, t => ColorBar.GetColor(t));
        }

        // モノクロバージオメトリ取得メソッド
        public static List<ColorBaredGeometry> GetMonoColorBarGeometries(Color color, ObservableCollection<double> values)
        {
            return GetColorBarGeometriesCore(values, _ => color);
        }

        //// カラーバーの範囲を返すメソッド
        //public static List<ColorBaredGeometry> GetColorBarGeometries(ObservableCollection<double> values)
        //{
        //    double maxValue = values.Max();
        //    double minValue = values.Min();
        //    double gap = maxValue - minValue;
        //    List<ColorBaredGeometry> colorBaredGeometries = [];
        //    if (gap == 0)
        //    {
        //        colorBaredGeometries.Add(
        //            new()
        //            {
        //                BottomRange = maxValue,
        //                TopRange = maxValue,
        //                Color = ColorBar.GetColor(0.5)
        //            });
        //        return colorBaredGeometries;
        //    }

        //    int i = 1;
        //    double roundedGap = 0;
        //    while (roundedGap == 0)
        //    {
        //        roundedGap = RoundToSignificantDigits(gap * Math.Pow(0.1, i), 1);
        //        i += 1;
        //    }

        //    double rangeMin = GetLargestMultipleLessThan(roundedGap, minValue);

        //    double bottomRange = rangeMin;

        //    while (bottomRange <= maxValue)
        //    {
        //        double topRange = bottomRange + roundedGap;
        //        double middleRange = (bottomRange + topRange) * 0.5;
        //        Color color = ColorBar.GetColor((middleRange - minValue) / (maxValue - minValue));

        //        colorBaredGeometries.Add(
        //        new()
        //        {
        //            BottomRange = bottomRange,
        //            TopRange = topRange,
        //            Color = color
        //        });

        //        bottomRange += roundedGap;
        //    }
        //    return colorBaredGeometries;
        //}

        //static List<ColorBaredGeometry> GetMonoColorBarGeometries(Color color, ObservableCollection<double> values)
        //{
        //    double maxValue = values.Max();
        //    double minValue = values.Min();
        //    double gap = maxValue - minValue;
        //    List<ColorBaredGeometry> colorBaredGeometries = [];
        //    if (gap == 0)
        //    {
        //        colorBaredGeometries.Add(
        //            new()
        //            {
        //                BottomRange = maxValue,
        //                TopRange = maxValue,
        //                Color = color
        //            });
        //        return colorBaredGeometries;
        //    }

        //    int i = 1;
        //    double roundedGap = 0;
        //    while (roundedGap == 0)
        //    {
        //        roundedGap = RoundToSignificantDigits(gap * Math.Pow(0.1, i), 1);
        //        i += 1;
        //    }

        //    double rangeMin = GetLargestMultipleLessThan(roundedGap, minValue);

        //    double bottomRange = rangeMin;

        //    while (bottomRange <= maxValue)
        //    {
        //        double topRange = bottomRange + roundedGap;
        //        colorBaredGeometries.Add(
        //        new()
        //        {
        //            BottomRange = bottomRange,
        //            TopRange = topRange,
        //            Color = Colors.Black
        //        });

        //        bottomRange += roundedGap;
        //    }
        //    return colorBaredGeometries;
        //}

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
            //if (DataContext is not MainWindowViewModel viewModel) return;

            //ObservableCollection<Point3D> points = [];
            //ObservableCollection<Vector3D> valueVectors = [];
            //ObservableCollection<double> values = [];

            //var selectedLoadCase = LoadCases.GetLoadCase(
            //viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            //if (selectedLoadCase == null) return;

            //var selectedLoadCombination = LoadCombinations.GetLoadCombination(
            //viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            //if (selectedLoadCombination == null) return;


            //// 杭頭
            //foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
            //{
            //    double value = 0;


            //    if (selectedLoadCase.Level == 0)
            //    {
            //        if (selectedLoadCase.LoadName == "VL")
            //        {
            //            value = pilelocation.AxialForceVL0;
            //            //break;
            //        }
            //        else if (selectedLoadCase.LoadName == "VLadd")
            //        {
            //            value = pilelocation.AxialForceVLAdditional;
            //            //break;
            //        }
            //        else if (selectedLoadCase.LoadName == "VL+VLadd")
            //        {
            //            value = (pilelocation.AxialForceVL0 + pilelocation.AxialForceVLAdditional);
            //            //break;
            //        }
            //        //}
            //    }
            //    else if (selectedLoadCase.Level == 1)
            //    {
            //        var loadCases1 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1;
            //        for (int i = 0; i < loadCases1.Count; i++)
            //        {
            //            if (loadCases1[i].LoadName == selectedLoadCase.LoadName)
            //            {
            //                value = pilelocation.AxialForceLevel1s[i];
            //                break;
            //            }
            //        }
            //    }
            //    else if (selectedLoadCase.Level == 2)
            //    {
            //        var loadCases2 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2;
            //        for (int i = 0; i < loadCases2.Count; i++)
            //        {
            //            if (loadCases2[i].LoadName == selectedLoadCase.LoadName)
            //            {
            //                value = pilelocation.AxialForceLevel2s[i];
            //                break;
            //            }
            //        }
            //    }

            //    if (!pilelocation.IsVisible) continue;

            //    Point3D locPileTop = pilelocation.Point3D;
            //    //double forceZ = GetForceZ(viewModel, pilelocation, selectedLoadCase);
            //    double forceZ = -value;
            //    points.Add(new Point3D(locPileTop.X, locPileTop.Y, locPileTop.Z));
            //    valueVectors.Add(new Vector3D(0, 0, forceZ));
            //    values.Add(Math.Abs(forceZ));
            //}


            //if (values.Count > 0)
            //{
            //    Color colorBlack = (Color)ColorConverter.ConvertFromString("#000000");

            //    List<ColorBaredGeometry> colorBaredGeometries = GetMonoColorBarGeometries(colorBlack, values);
            //    Update3DValueArrows(points, valueVectors, colorBaredGeometries, 0, false);
            //}
        }

        // 3D荷重更新メソッド
        public void UpdateLoading3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            ObservableCollection<Point3D> points = [];
            ObservableCollection<Vector3D> valueVectors = [];
            ObservableCollection<double> values = [];

            var selectedLoadCase = LoadCases.GetLoadCase(
            viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            if (selectedLoadCase == null) return;

            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
            viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (selectedLoadCombination == null) return;


            // 杭頭
            if (viewModel.IsAxialLoadingVisible)
            {
                foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    if (!pilelocation.IsVisible) continue;

                    double forceZ = GetForceZ(viewModel, pilelocation, selectedLoadCase); // 既存ヘルパー
                    Point3D locPileTop = pilelocation.Point3D;

                    points.Add(new Point3D(locPileTop.X, locPileTop.Y, locPileTop.Z));
                    valueVectors.Add(new Vector3D(0, 0, forceZ)); // GetForceZ は符号付きを返している
                    values.Add(Math.Abs(forceZ));
                }
            }



            // 慣性力作用点
            if (viewModel.IsActionPointVisible && viewModel.IsMassLoadingVisible)
            {
                double x = selectedLoadCase.ForceActionPointX;
                double y = selectedLoadCase.ForceActionPointY;
                double z = selectedLoadCase.ForceActionPointAltitude;
                points.Add(new(x, y, z));

                double force = selectedLoadCase.UpperMassForce * selectedLoadCombination.Beta1
                    + selectedLoadCase.FoundationMassForce * selectedLoadCombination.Beta2;
                double forceX = force * Math.Cos(selectedLoadCase.LoadAngle * Math.PI / 180);
                double forceY = force * Math.Sin(selectedLoadCase.LoadAngle * Math.PI / 180);

                valueVectors.Add(new(forceX, forceY, 0));
                values.Add(new Vector3D(forceX, forceY, 0).Length);
            }

            if (values.Count > 0)
            {
                //Color colorBlack = (Color)ColorConverter.ConvertFromString("#000000");

                //List<ColorBaredGeometry> colorBaredGeometries = GetMonoColorBarGeometries(colorBlack, values);
                //Update3DValueArrows(points, valueVectors, colorBaredGeometries, 0, false);
                // 直接 Colors.Black を使う
                List<ColorBaredGeometry> colorBaredGeometries = GetMonoColorBarGeometries(Colors.Black, values);
                Update3DValueArrows(points, valueVectors, colorBaredGeometries, 0, false);
            }
        }


        // 3D地盤変位更新メソッド
        //public void UpdateForcedDisplacement3D()
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;
        //    //CurrentInputModel inputModel = viewModel.CurrentInputModel;

        //    //if (!viewModel.IsElementSplit) return;
        //    var selectedLoadCase = LoadCases.GetLoadCase(viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
        //    if (selectedLoadCase == null) return;
        //    if (selectedLoadCase.Level != 1 && selectedLoadCase.Level != 2) return;

        //    (int loadCaseIndex, int loadCombinationIndex) = GetLoadLoadCaseIndexLoadCombinationIndex();
        //    LoadCase loadCase = selectedLoadCase.Level == 1 ?
        //        viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[loadCaseIndex] :
        //        viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[loadCaseIndex];

        //    LoadCombination loadCombination = viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations[loadCombinationIndex];
        //    double cos = Math.Cos(loadCase.LoadAngle * Math.PI / 180);
        //    double sin = Math.Sin(loadCase.LoadAngle * Math.PI / 180);

        //    ObservableCollection<ObservableCollection<Point3D>> pointSets = [];
        //    ObservableCollection<ObservableCollection<Vector3D>> valueVectorSets = [];
        //    ObservableCollection<ObservableCollection<double>> valueSets = [];

        //    // 根入
        //    //if (viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane != null)
        //    if (viewModel.CurrentInputModel.EmbedmentInput != null && viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count != 0)
        //    {
        //        if (viewModel.IsElementSplit)
        //        {
        //            for (int j = 0; j < viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane.Items.Count; j++)
        //            {
        //                var item = viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane.Items[j];
        //                double x = item.X0;
        //                double y = item.Y0;

        //                ZDataItem zDataItemI = item.ZDataItemTop;
        //                double zI = zDataItemI.Z;
        //                ZDataItem zDataItemJ = item.ZDataItemBtm;
        //                double zJ = zDataItemJ.Z;

        //                double groundDispI;
        //                double groundDispJ;

        //                if (selectedLoadCase.Level == 1)
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? zDataItemI.GroundDisp1L : zDataItemI.GroundDisp1;
        //                    groundDispJ = viewModel.IsLiquefaction ? zDataItemJ.GroundDisp1L : zDataItemJ.GroundDisp1;
        //                }
        //                else // if (viewModel.SelectedLoad == "レベル2")
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? zDataItemI.GroundDisp2L : zDataItemI.GroundDisp2;
        //                    groundDispJ = viewModel.IsLiquefaction ? zDataItemJ.GroundDisp2L : zDataItemJ.GroundDisp2;
        //                }

        //                double factoredGroundDispI = groundDispI * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispIX = factoredGroundDispI * cos;
        //                double factoredGroundDispIY = factoredGroundDispI * sin;

        //                double factoredGroundDispJ = groundDispJ * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispJX = factoredGroundDispJ * cos;
        //                double factoredGroundDispJY = factoredGroundDispJ * sin;

        //                Point3D nodeI3D = new() { X = x, Y = y, Z = zI };
        //                Point3D nodeJ3D = new() { X = x, Y = y, Z = zJ };
        //                Point3D nodeIDisp3D = new(
        //                    nodeI3D.X + factoredGroundDispIX,
        //                    nodeI3D.Y + factoredGroundDispIY,
        //                    nodeI3D.Z);
        //                Point3D nodeJDisp3D = new(
        //                    nodeJ3D.X + factoredGroundDispJX,
        //                    nodeJ3D.Y + factoredGroundDispJY,
        //                    nodeJ3D.Z);

        //                Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //                Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
        //                Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
        //                Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //                var pointsA = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
        //                AddPolyLineGeometry(pointsA, viewModel.CanvasGeometry.PathGeoGroundDisp);

        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //                    if (viewModel.IsPileTopResultValueVisibleOnly)
        //                    {
        //                        if (j == 0)
        //                        {
        //                            AddText3D(Brushes.Brown, string.Format(format, groundDispI),
        //                            nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        DrawResultValueTexts(
        //                            viewModel.IsResultValueVisible, Brushes.Brown,
        //                            groundDispI, groundDispJ,
        //                            nodeIDisp2D, nodeJDisp2D,
        //                            nodeJ2D, nodeI2D,
        //                            format, format);
        //                    }
        //                }
        //            }
        //        }

        //        else
        //        {
        //            int groundNo = viewModel.CurrentInputModel.EmbedmentInput.GroundNo;
        //            var item = viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers;
        //            double x = (item[0].X1 + item[0].X2) * 0.5;
        //            double y = (item[0].Y1 + item[0].Y2) * 0.5;

        //            for (int j = 0; j < viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData.Count - 1; j++)
        //            {
        //                GroundMassDataInput gmdiI = viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData[j];
        //                GroundMassDataInput gmdiJ = viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData[j + 1];
        //                double zI = gmdiI.AltitudeDepth;
        //                double zJ = gmdiJ.AltitudeDepth;

        //                double groundDispI;
        //                double groundDispJ;

        //                if (selectedLoadCase.Level == 1)
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? gmdiI.DmaxUStarSigmaGammaCyH[0] : gmdiI.DmaxUStar[0];
        //                    groundDispJ = viewModel.IsLiquefaction ? gmdiJ.DmaxUStarSigmaGammaCyH[0] : gmdiJ.DmaxUStar[0];
        //                }
        //                else // if (viewModel.SelectedLoad == "レベル2")
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? gmdiI.DmaxUStarSigmaGammaCyH[1] : gmdiI.DmaxUStar[1];
        //                    groundDispJ = viewModel.IsLiquefaction ? gmdiJ.DmaxUStarSigmaGammaCyH[1] : gmdiJ.DmaxUStar[1];
        //                }

        //                double factoredGroundDispI = groundDispI * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispIX = factoredGroundDispI * cos;
        //                double factoredGroundDispIY = factoredGroundDispI * sin;

        //                double factoredGroundDispJ = groundDispJ * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispJX = factoredGroundDispJ * cos;
        //                double factoredGroundDispJY = factoredGroundDispJ * sin;

        //                Point3D nodeI3D = new() { X = x, Y = y, Z = zI };
        //                Point3D nodeJ3D = new() { X = x, Y = y, Z = zJ };
        //                Point3D nodeIDisp3D = new(
        //                    nodeI3D.X + factoredGroundDispIX,
        //                    nodeI3D.Y + factoredGroundDispIY,
        //                    nodeI3D.Z);
        //                Point3D nodeJDisp3D = new(
        //                    nodeJ3D.X + factoredGroundDispJX,
        //                    nodeJ3D.Y + factoredGroundDispJY,
        //                    nodeJ3D.Z);

        //                Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //                Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
        //                Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
        //                Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //                var pointsA = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
        //                AddPolyLineGeometry(pointsA, viewModel.CanvasGeometry.PathGeoGroundDisp);

        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //                    if (viewModel.IsPileTopResultValueVisibleOnly)
        //                    {
        //                        if (j == 0)
        //                        {
        //                            AddText3D(Brushes.Brown, string.Format(format, groundDispI),
        //                            nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        DrawResultValueTexts(
        //                            viewModel.IsResultValueVisible, Brushes.Brown,
        //                            groundDispI, groundDispJ,
        //                            nodeIDisp2D, nodeJDisp2D,
        //                            nodeJ2D, nodeI2D,
        //                            format, format);
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    // 杭配置
        //    if (viewModel.IsElementSplit)
        //    {
        //        for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
        //        {

        //            PileLayoutDataItem pilelocation = viewModel.CurrentInputModel.PileLayoutItems[i];
        //            var soilPile = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pilelocation.SoilPileAltNo - 1];

        //            double x = pilelocation.Point3D.X;
        //            double y = pilelocation.Point3D.Y;

        //            pointSets.Add([]);
        //            valueVectorSets.Add([]);
        //            valueSets.Add([]);

        //            // 各杭節点
        //            for (int j = 0; j < soilPile.ZDataItems.Count - 1; j++)
        //            {
        //                ZDataItem zDataItemI = soilPile.ZDataItems[j];
        //                double zI = zDataItemI.Z;
        //                ZDataItem zDataItemJ = soilPile.ZDataItems[j + 1];
        //                double zJ = zDataItemJ.Z;

        //                double groundDispI;
        //                double groundDispJ;

        //                if (selectedLoadCase.Level == 1)
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? zDataItemI.GroundDisp1L : zDataItemI.GroundDisp1;
        //                    groundDispJ = viewModel.IsLiquefaction ? zDataItemJ.GroundDisp1L : zDataItemJ.GroundDisp1;
        //                }
        //                else // if (viewModel.SelectedLoad == "レベル2")
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? zDataItemI.GroundDisp2L : zDataItemI.GroundDisp2;
        //                    groundDispJ = viewModel.IsLiquefaction ? zDataItemJ.GroundDisp2L : zDataItemJ.GroundDisp2;
        //                }

        //                double factoredGroundDispI = groundDispI * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispIX = factoredGroundDispI * cos;
        //                double factoredGroundDispIY = factoredGroundDispI * sin;

        //                double factoredGroundDispJ = groundDispJ * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispJX = factoredGroundDispJ * cos;
        //                double factoredGroundDispJY = factoredGroundDispJ * sin;

        //                Point3D nodeI3D = new() { X = x, Y = y, Z = zI };
        //                Point3D nodeJ3D = new() { X = x, Y = y, Z = zJ };
        //                Point3D nodeIDisp3D = new(
        //                    nodeI3D.X + factoredGroundDispIX,
        //                    nodeI3D.Y + factoredGroundDispIY,
        //                    nodeI3D.Z);
        //                Point3D nodeJDisp3D = new(
        //                    nodeJ3D.X + factoredGroundDispJX,
        //                    nodeJ3D.Y + factoredGroundDispJY,
        //                    nodeJ3D.Z);

        //                Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //                Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
        //                Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
        //                Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //                var pointsA = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
        //                AddPolyLineGeometry(pointsA, viewModel.CanvasGeometry.PathGeoGroundDisp);

        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //                    if (viewModel.IsPileTopResultValueVisibleOnly)
        //                    {
        //                        if (j == 0)
        //                        {
        //                            AddText3D(Brushes.Brown, string.Format(format, groundDispI),
        //                            nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        DrawResultValueTexts(
        //                            viewModel.IsResultValueVisible, Brushes.Brown,
        //                            groundDispI, groundDispJ,
        //                            nodeIDisp2D, nodeJDisp2D,
        //                            nodeJ2D, nodeI2D,
        //                            format, format);
        //                    }
        //                }
        //            }
        //        }

        //    }
        //    else
        //    {
        //        for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
        //        {

        //            PileLayoutDataItem pilelocation = viewModel.CurrentInputModel.PileLayoutItems[i];
        //            //var soilPile = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pilelocation.SoilPileAltNo - 1];

        //            pointSets.Add([]);
        //            valueVectorSets.Add([]);
        //            valueSets.Add([]);

        //            // 各杭節点
        //            int groundNo = pilelocation.GroundNo;
        //            var item = viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers;
        //            double x = pilelocation.Point3D.X;
        //            double y = pilelocation.Point3D.Y;

        //            for (int j = 0; j < viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData.Count - 1; j++)
        //            {
        //                GroundMassDataInput gmdiI = viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData[j];
        //                GroundMassDataInput gmdiJ = viewModel.CurrentInputModel.GroundsInput[groundNo - 1].GroundMassesData[j + 1];
        //                double zI = gmdiI.AltitudeDepth;
        //                double zJ = gmdiJ.AltitudeDepth;

        //                double groundDispI;
        //                double groundDispJ;

        //                if (selectedLoadCase.Level == 1)
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? gmdiI.DmaxUStarSigmaGammaCyH[0] : gmdiI.DmaxUStar[0];
        //                    groundDispJ = viewModel.IsLiquefaction ? gmdiJ.DmaxUStarSigmaGammaCyH[0] : gmdiJ.DmaxUStar[0];
        //                }
        //                else // if (viewModel.SelectedLoad == "レベル2")
        //                {
        //                    groundDispI = viewModel.IsLiquefaction ? gmdiI.DmaxUStarSigmaGammaCyH[1] : gmdiI.DmaxUStar[1];
        //                    groundDispJ = viewModel.IsLiquefaction ? gmdiJ.DmaxUStarSigmaGammaCyH[1] : gmdiJ.DmaxUStar[1];
        //                }

        //                double factoredGroundDispI = groundDispI * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispIX = factoredGroundDispI * cos;
        //                double factoredGroundDispIY = factoredGroundDispI * sin;

        //                double factoredGroundDispJ = groundDispJ * 0.001 * loadCombination.Alpha1 * viewModel.DispDiagramMultiplier;
        //                double factoredGroundDispJX = factoredGroundDispJ * cos;
        //                double factoredGroundDispJY = factoredGroundDispJ * sin;

        //                Point3D nodeI3D = new() { X = x, Y = y, Z = zI };
        //                Point3D nodeJ3D = new() { X = x, Y = y, Z = zJ };
        //                Point3D nodeIDisp3D = new(
        //                    nodeI3D.X + factoredGroundDispIX,
        //                    nodeI3D.Y + factoredGroundDispIY,
        //                    nodeI3D.Z);
        //                Point3D nodeJDisp3D = new(
        //                    nodeJ3D.X + factoredGroundDispJX,
        //                    nodeJ3D.Y + factoredGroundDispJY,
        //                    nodeJ3D.Z);

        //                Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //                Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
        //                Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
        //                Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //                var pointsA = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
        //                AddPolyLineGeometry(pointsA, viewModel.CanvasGeometry.PathGeoGroundDisp);

        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //                    if (viewModel.IsPileTopResultValueVisibleOnly)
        //                    {
        //                        if (j == 0)
        //                        {
        //                            AddText3D(Brushes.Brown, string.Format(format, groundDispI),
        //                            nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        DrawResultValueTexts(
        //                            viewModel.IsResultValueVisible, Brushes.Brown,
        //                            groundDispI, groundDispJ,
        //                            nodeIDisp2D, nodeJDisp2D,
        //                            nodeJ2D, nodeI2D,
        //                            format, format);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}
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

        // 群杭グリッド
        private void UpdateGroupPileGrid3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel == null) return;
            if (viewModel.CurrentInputModel.PileGroupSettlement == null) return;

            double xmin = viewModel.GroupPileSettlementXmin;
            double xmax = viewModel.GroupPileSettlementXmax;
            double ymin = viewModel.GroupPileSettlementYmin;
            double ymax = viewModel.GroupPileSettlementYmax;
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

            double z = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

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
        private double GetForceZ(MainWindowViewModel viewModel, PileLayoutDataItem pilelocation, LoadCase selectedLoadCase)
        {
            switch (viewModel.SelectedLoadCaseName)
            {
                case "VL0":
                    return -pilelocation.AxialForceVL0;
                case "VLadd":
                    return -pilelocation.AxialForceVLAdditional;
                case "VL":
                    return -pilelocation.AxialForceVL0 - pilelocation.AxialForceVLAdditional;
            }

            if (selectedLoadCase.Level == 1)
            {
                for (int index = 0; index < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; index++)
                {
                    if (selectedLoadCase.LoadName == viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[index].LoadName)
                    {
                        return -pilelocation.AxialForceLevel1s[index];
                    }
                }

            }
            else if (selectedLoadCase.Level == 2)
            {
                for (int index = 0; index < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; index++)
                {
                    if (selectedLoadCase.LoadName == viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[index].LoadName)
                    {
                        return -pilelocation.AxialForceLevel2s[index];
                    }
                }
            }
            return 0;
        }

        private void UpdateValueTextsCore<T>(
            IList<T> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            Func<T, Point> to2D,
            int decimalPlaces = 3,
            string horizontalPos = "L",
            string verticalPos = "B")
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel == null || values.Count == 0) return;

            for (int i = 0; i < points.Count; i++)
            {
                double value = values[i];
                Point point2D = to2D(points[i]);
                for (int j = 0; j < colorBaredGeometries.Count; j++)
                {
                    if ((j == 0 && colorBaredGeometries[j].BottomRange <= value && value <= colorBaredGeometries[j].TopRange) ||
                        (j != 0 && colorBaredGeometries[j].BottomRange < value && value <= colorBaredGeometries[j].TopRange))
                    {
                        AddText3D(
                            new SolidColorBrush(colorBaredGeometries[j].Color),
                            GetNumberString(value, decimalPlaces),
                            point2D.X,
                            point2D.Y,
                            horizontalPos,
                            verticalPos,
                            0.0
                        );
                        break;
                    }
                }
            }
        }

        public void UpdateValueTexts2D(
            ObservableCollection<Point> points2D,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            int decimalPlaces = 3,
            string horizontalPos = "L",
            string verticalPos = "B")
        {
            UpdateValueTextsCore(points2D, values, colorBaredGeometries, p => p, decimalPlaces, horizontalPos, verticalPos);
        }

        public void UpdateValueTexts(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            UpdateValueTextsCore(points, values, colorBaredGeometries, p => viewModel.CanvasThreeDView.Transformation(p));
        }

        // 番号取得メソッド
        private string GetNumberString(double value, int decimalPlaces)
        {
            return value.ToString($"N{decimalPlaces}");
        }

        // テキスト位置の調整メソッド
        private Point AdjustTextPosition(Point originalPoint, Size textSize, string horizontalPos, string verticalPos, double textAngle)
        {
            double x = originalPoint.X;
            double y = originalPoint.Y;

            double dx = 0;
            double dy = 0;
            switch (horizontalPos)
            {
                case "C":
                    dx = textSize.Width / 2;
                    break;
                case "R":
                    dx = textSize.Width;
                    break;
            }

            switch (verticalPos)
            {
                case "C":
                    dy = textSize.Height / 2;
                    break;
                case "B":
                    dy = textSize.Height;
                    break;
            }

            x -= Math.Cos(textAngle / 180 * Math.PI) * dx - Math.Sin(textAngle / 180 * Math.PI) * dy;
            y -= Math.Sin(textAngle / 180 * Math.PI) * dx + Math.Cos(textAngle / 180 * Math.PI) * dy;
            return new Point(x, y);
        }

        // バブルチャート描画メソッド
        public void UpdateValueBubbles(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBarGeometries
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            if (viewModel == null || values.Count == 0)
            { return; }
            double maxBubbleDia2D/* = viewModel.BubbleDia*/;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

            for (int i = 0; i < points.Count; i++)
            {
                double value = values[i];
                double bubbleDia2D;
                if (absMaxValue == 0)
                {
                    bubbleDia2D = 0;
                }
                else
                {
                    bubbleDia2D = Math.Abs(value) * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;
                }

                Point center2D = viewModel.CanvasThreeDView.Transformation(points[i]);

                EllipseGeometry ellipse = new(center2D, bubbleDia2D * 0.5, bubbleDia2D * 0.5 * flattening);

                foreach (ColorBaredGeometry colorBaredGeometry in colorBarGeometries)
                {
                    if (colorBaredGeometry.BottomRange <= value && value <= colorBaredGeometry.TopRange)
                    {
                        colorBaredGeometry.PathGeometry.AddGeometry(ellipse);
                        break;
                    }
                }
            }
        }

        // 3Dポリライン描画メソッド
        public void Update3DValuePolyLines(
            ObservableCollection<ObservableCollection<Point3D>> pointSets,
            ObservableCollection<ObservableCollection<Vector3D>> valueVectorSets,
            Color color,
            bool hasColorBar
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;

            if (viewModel == null || valueVectorSets.Count == 0)
            { return; }

            // 各ベクトルの長さ（ノルム）を計算し、その中で最大の値を取得
            double absMaxValue = valueVectorSets.SelectMany(v => v).Max(v => v.Length);
            ObservableCollection<Point> TextPos2Ds = [];

            List<PathGeometry> polyLines = [];
            List<LineGeometry> lines = [];

            ColorBaredGeometry colorBaredGeometry = new()
            {
                Color = color
            };

            PathGeometry pathGeometry = new();

            for (int i = 0; i < pointSets.Count; i++)
            {
                PathFigure pathFigure = new();
                PolyLineSegment polyLineSegment = new()
                {
                    Points = []
                };

                for (int j = 0; j < pointSets[i].Count; j++)
                {
                    // 座標
                    Point point2D = viewModel.CanvasThreeDView.Transformation(pointSets[i][j]);

                    double value = valueVectorSets[i][j].Length;

                    // valueVectors[i] を正規化
                    Vector3D normalizedVector = valueVectorSets[i][j];

                    // ベクトル長さ
                    double vectorLength2D = absMaxValue == 0 ? 0 : maxArrowLength2D * value / absMaxValue;

                    // 尾の位置の計算
                    Point3D shiftPoint3DTemp = pointSets[i][j] - normalizedVector;
                    Point shiftPoint2DTemp = viewModel.CanvasThreeDView.Transformation(shiftPoint3DTemp);

                    // ベクトル
                    Vector vectorTemp = point2D - shiftPoint2DTemp;

                    // shift2Dを計算
                    Point shiftPoint2D = vectorTemp.Length > Math.Pow(10, -3) ?
                    point2D + vectorTemp / vectorTemp.Length * vectorLength2D : point2D;

                    lines.Add(new(point2D, shiftPoint2D));

                    if (j == 0)
                    {
                        pathFigure.StartPoint = shiftPoint2D;
                    }
                    else
                    {
                        polyLineSegment.Points.Add(shiftPoint2D);
                        LineGeometry lineGeometry = new(point2D, shiftPoint2D);
                        pathGeometry.AddGeometry(lineGeometry);
                    }
                }

                // PathFigureにPolyLineSegmentを追加
                pathFigure.Segments.Add(polyLineSegment);
                // PathGeometryにPathFigureを追加
                pathGeometry.Figures.Add(pathFigure);
            }

            LineGeometry line = new(new(Canvas3DWidth, Canvas3DHeight), new(0, 0));
            colorBaredGeometry.PathGeometry.AddGeometry(pathGeometry);
            colorBaredGeometry.DrawPathes(Canvas3DLayout);
        }

        // 3D矢印描画メソッド
        public void Update3DValueArrows(
            ObservableCollection<Point3D> points,
            ObservableCollection<Vector3D> valueVectors, // 荷重方向ベクトル
            List<ColorBaredGeometry> colorBaredGeometries,
            int decimalPlaces,
            bool hasColorBar,
            bool isPointAtHead = true,
            bool isDoubleHead = false) // モーメント、回転角のとき二重矢印にする
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;
            double maxArrowLength3D = 5;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;

            if (viewModel == null || valueVectors.Count == 0)
            { return; }

            // 各ベクトルの長さ（ノルム）を計算し、その中で最大の値を取得
            double absMaxValue = valueVectors.Max(v => v.Length);
            ObservableCollection<Point> TextPos2Ds = [];
            Vector arrowVectorTemp;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < colorBaredGeometries.Count; j++)
                {
                    Vector3D viewVector = viewModel.CanvasThreeDView.GetViewVector(); // 注視ベクトル
                    double value = valueVectors[i].Length;

                    if ((j == 0 && colorBaredGeometries[j].BottomRange <= value && value <= colorBaredGeometries[j].TopRange) ||
                        (j != 0 && colorBaredGeometries[j].BottomRange < value && value <= colorBaredGeometries[j].TopRange))
                    {
                        Point arrowHead2D;
                        Point arrowEnd2D;

                        // valueVectors[i] を正規化
                        Vector3D normalizedVector = valueVectors[i];

                        // 尾の長さ
                        double tailLength2D = absMaxValue == 0 ? 0 : maxArrowLength3D * value / absMaxValue * viewModel.CanvasThreeDView.Scale * viewModel.ForceDiagramMultiplier;

                        if (isPointAtHead)
                        {
                            // 矢印頭座標
                            arrowHead2D = viewModel.CanvasThreeDView.Transformation(points[i]);

                            // 矢印尾の位置を計算
                            Point3D arrowEnd3DTemp = points[i] - normalizedVector;
                            Point arrowEnd2DTemp = viewModel.CanvasThreeDView.Transformation(arrowEnd3DTemp);

                            // arrowVector を計算
                            arrowVectorTemp = arrowHead2D - arrowEnd2DTemp;

                            // arrowEnd2D を計算
                            arrowEnd2D = arrowVectorTemp.Length > Math.Pow(10, -3) ?
                                arrowHead2D - arrowVectorTemp / arrowVectorTemp.Length * tailLength2D : arrowHead2D;
                            TextPos2Ds.Add(arrowEnd2D);
                        }
                        else // if (!isPointAtHead)
                        {
                            // 矢印尾座標
                            arrowEnd2D = viewModel.CanvasThreeDView.Transformation(points[i]);

                            // 矢印頭の位置を計算
                            Point3D arrowHead3DTemp = points[i] + normalizedVector;
                            Point arrowHead2DTemp = viewModel.CanvasThreeDView.Transformation(arrowHead3DTemp);

                            // arrowVector を計算
                            arrowVectorTemp = arrowHead2DTemp - arrowEnd2D;

                            // arrowEnd2D を計算
                            arrowHead2D = arrowVectorTemp.Length > Math.Pow(10, -3) ?
                                arrowEnd2D + arrowVectorTemp / arrowVectorTemp.Length * tailLength2D : arrowEnd2D;
                            TextPos2Ds.Add(arrowHead2D);
                        }

                        // 矢印本体
                        // LineGeometry の作成
                        LineGeometry lineTale = new(arrowHead2D, arrowEnd2D);
                        colorBaredGeometries[j].PathGeometry.AddGeometry(lineTale);

                        double cosAngle = Vector3D.DotProduct(viewVector, valueVectors[i]) / (viewVector.Length * valueVectors[i].Length);
                        double sinAngle = Math.Sqrt(1 - Math.Pow(cosAngle, 2));

                        Vector arrow2DTemp = arrowHead2D - arrowEnd2D;

                        // １つ目のコーン
                        Point center2D = arrowHead2D - arrowHeadLength2D * sinAngle * arrow2DTemp / arrow2DTemp.Length;
                        EllipseGeometry ellipse = new(center2D, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * cosAngle);
                        RotateTransform rotateTransform = new()
                        {
                            Angle = GetAngle(arrowVectorTemp) + 90, // 回転角度（度単位）
                            CenterX = center2D.X,
                            CenterY = center2D.Y
                        };

                        GeometryGroup geometryGroup = new();
                        geometryGroup.Children.Add(ellipse);
                        geometryGroup.Transform = rotateTransform;
                        colorBaredGeometries[j].PathGeometry.AddGeometry(geometryGroup);

                        // 母線
                        List<LineGeometry> lineGeometries =
                        GetConeGeneratrixes3D(arrowHead2D, center2D, arrowHeadDia2D * 0.5);

                        foreach (LineGeometry lineGeometry in lineGeometries)
                        {
                            colorBaredGeometries[j].PathGeometry.AddGeometry(lineGeometry);
                        }

                        // 2つ目のコーン（ダブルヘッド）
                        if (isDoubleHead)
                        {
                            // 1つ目のコーンの底面中心(center2D)を2つ目のコーンの頂点とする
                            Point arrowHead2D_2 = center2D;
                            Point center2D_2 = arrowHead2D_2 - arrowHeadLength2D * sinAngle * arrow2DTemp / arrow2DTemp.Length;

                            EllipseGeometry ellipse2 = new(center2D_2, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * cosAngle);
                            RotateTransform rotateTransform2 = new()
                            {
                                Angle = GetAngle(arrowVectorTemp) + 90,
                                CenterX = center2D_2.X,
                                CenterY = center2D_2.Y
                            };
                            GeometryGroup geometryGroup2 = new();
                            geometryGroup2.Children.Add(ellipse2);
                            geometryGroup2.Transform = rotateTransform2;
                            colorBaredGeometries[j].PathGeometry.AddGeometry(geometryGroup2);

                            List<LineGeometry> lineGeometries2 = GetConeGeneratrixes3D(
                                arrowHead2D_2, center2D_2, arrowHeadDia2D * 0.5);
                            foreach (LineGeometry lineGeometry in lineGeometries2)
                            {
                                colorBaredGeometries[j].PathGeometry.AddGeometry(lineGeometry);
                            }
                        }
                    }
                }
            }

            foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
            {
                colorBaredGeometry.DrawPathes(Canvas3DLayout);
            }

            ObservableCollection<double> values = [];
            foreach (Vector3D value in valueVectors)
            {
                values.Add(value.Length);
            }

            UpdateValueTexts2D(TextPos2Ds, values, colorBaredGeometries, decimalPlaces, "R", "T"); // テキスト

            if (hasColorBar) { ColorBar.DrawStepColorBar(ColorBarCanvas, colorBaredGeometries); }
            else { ColorBarCanvas.Children.Clear(); }
        }

        // 角度取得メソッド
        public static double GetAngle(Vector vector)
        {
            if (vector.Length == 0)
            {
                return 0;
            }

            // (1, 0) との内積を計算
            double dotProduct = Vector.Multiply(vector, new Vector(1, 0));

            // ベクトルの長さを計算
            double vectorLength = vector.Length;

            // cosθ を計算
            double cosTheta = dotProduct / vectorLength;

            // 角度をラジアンから度に変換
            double angle = Math.Acos(cosTheta) * 180 / Math.PI;

            // y成分が負の場合、角度を反転
            if (vector.Y < 0)
            {
                angle = -angle;
            }

            return angle;
        }

        // 母線を返すメソッド
        public static List<LineGeometry> GetConeGeneratrixes3D(Point arrowHead2D, Point center2D, double radius)
        {
            Vector directionVector = (arrowHead2D - center2D);
            Vector unitOrthogonalVector = GetUnitOrthogonalVector(directionVector);
            List<LineGeometry> lineGeometries = [];
            for (int i = -1; i <= 1; i += 2)
            {
                lineGeometries.Add(
                    new()
                    {
                        StartPoint = arrowHead2D,
                        EndPoint = center2D + i * unitOrthogonalVector * radius,
                    });
            }
            return lineGeometries;
        }

        // 単位直交ベクトルを得るメソッド
        public static Vector GetUnitOrthogonalVector(Vector vector)
        {
            // 元のベクトルを90度回転させた直交ベクトルを求める
            Vector orthogonalVector = new(-vector.Y, vector.X);

            // 直交ベクトルを単位ベクトルに正規化する
            orthogonalVector.Normalize();

            return orthogonalVector;
        }

        // 矢印描画メソッド
        public void UpdateValueArrows(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            /*double maxArrowLength2D*//* = viewModel.ArrowLength*/
            ;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;
            if (viewModel == null || values.Count == 0)
            { return; }

            double flattening = viewModel.CanvasThreeDView.Flattening;
            double flattening0 = Math.Sqrt(1 - Math.Pow(flattening, 2));
            //double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

            for (int i = 0; i < points.Count; i++)
            {
                foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                {
                    double value = values[i];
                    if (colorBaredGeometry.BottomRange <= value && value <= colorBaredGeometry.TopRange)
                    {
                        // 尾
                        double tailLength2D = value * 0.01 * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;

                        // 頂点
                        Point arrowEnd2D = viewModel.CanvasThreeDView.Transformation(points[i]);
                        // 頂点
                        Point arrowHead2D = viewModel.CanvasThreeDView.Transformation(points[i] - new Vector3D(0, 0, tailLength2D));
                        // LineGeometryの作成
                        LineGeometry lineTale = new(arrowEnd2D, arrowHead2D);

                        colorBaredGeometry.PathGeometry.AddGeometry(lineTale);

                        //楕円中心位置
                        Point center2D = new(arrowHead2D.X, arrowHead2D.Y - arrowHeadLength2D * flattening0);

                        //楕円
                        EllipseGeometry ellipse1 = new(center2D, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * flattening);
                        colorBaredGeometry.PathGeometry.AddGeometry(ellipse1);

                        //母線
                        List<LineGeometry> lineGeometries =
                        GetConeGeneratrixes(center2D, arrowHeadDia2D * 0.5, 0, -arrowHeadLength2D * flattening0, flattening);

                        foreach (LineGeometry lineGeometry in lineGeometries)
                        {
                            colorBaredGeometry.PathGeometry.AddGeometry(lineGeometry);
                        }
                    }
                }
            }

            // 描画する要素に対して、IsHitTestVisibleをfalseに設定
            foreach (UIElement element in Canvas3DLayout.Children)
            {
                element.IsHitTestVisible = false;
            }

            // ZIndexを調整して他のUI要素の背面に配置
            Panel.SetZIndex(Canvas3DLayout, -1);
        }

        // 有効数字1桁の値を返すメソッド
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (value == 0)
            { return 0; }

            double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) + 1 - significantDigits);
            return Math.Round(value / scale) * scale;
        }

        // a の倍数で b を超える最小の値を返すメソッド
        public static double GetSmallestMultipleGreaterThan(double a, double b)
        {
            return Math.Ceiling(b / a) * a;
        }

        // a の倍数で b より小さい最大の値を返すメソッド
        public static double GetLargestMultipleLessThan(double a, double b)
        {
            return Math.Floor(b / a) * a;
        }

        // 円錐台の母線を返すメソッド
        private List<LineGeometry> GetConeGeneratrixes(Point point2D, double radius1, double radius2, double distance2D, double flattening)
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

        //テキスト一括レンダリングメソッド

        private Image _textLayerImage;

        private void RenderTextBlocksWithDrawingVisual()
        {
            if (Canvas3DLayout == null)
                return;
            if ((int)Canvas3DLayout.ActualWidth > 0 && (int)Canvas3DLayout.ActualHeight > 0)
            {
                DrawingVisual drawingVisual = new();
                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    foreach (var textBlockInfo in TextBlockInfos)
                    {
                        var typeface = new Typeface(
                            new FontFamily("Arial"),
                            FontStyles.Normal,
                            FontWeights.Normal,
                            FontStretches.Normal);

                        FormattedText formattedText = new(
                            textBlockInfo.TextBlock.Text,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            textBlockInfo.TextBlock.FontSize,
                            textBlockInfo.TextBlock.Foreground,
                            VisualTreeHelper.GetDpi(Canvas3DLayout).PixelsPerDip
                        );
                        dc.PushTransform(new ScaleTransform(1.0, textBlockInfo.ScaleY, textBlockInfo.X, textBlockInfo.Y));
                        dc.PushTransform(new RotateTransform(textBlockInfo.TextAngle, textBlockInfo.X, textBlockInfo.Y));
                        dc.DrawText(formattedText, new Point(textBlockInfo.X, textBlockInfo.Y));
                        dc.Pop();
                        dc.Pop();
                    }
                }

                RenderTargetBitmap renderBitmap = new((int)Canvas3DLayout.ActualWidth, (int)Canvas3DLayout.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(drawingVisual);

                RenderOptions.SetBitmapScalingMode(renderBitmap, BitmapScalingMode.HighQuality);

                // _textLayerImageがnullまたはCanvasから消えていたら再生成
                if (_textLayerImage == null || !Canvas3DLayout.Children.Contains(_textLayerImage))
                {
                    _textLayerImage = new Image();
                    Canvas.SetLeft(_textLayerImage, 0);
                    Canvas.SetTop(_textLayerImage, 0);
                    Canvas3DLayout.Children.Add(_textLayerImage);
                }
                _textLayerImage.Source = renderBitmap;

                TextBlockInfos.Clear();
            }
        }

        // バブル・矢印描画
        private void DrawBubbleAndArrow(ObservableCollection<Point3D> points, ObservableCollection<double> values, string title, string unit)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (values.Count == 0) return;

            List<ColorBaredGeometry> colorBaredGeometries = GetColorBarGeometries(values); // カラーバージオメトリ取得

            if (viewModel.IsBubbleVisible) // バブル
            { UpdateValueBubbles(points, values, colorBaredGeometries); }

            if (viewModel.IsArrowVisible) // 矢印
            { UpdateValueArrows(points, values, colorBaredGeometries); }

            if (viewModel.IsDeformedElementVisible) // 変形後要素
            { UpdateDeformedGeneralelement3D(values); }

            UpdateValueTexts(points, values, colorBaredGeometries); // テキスト

            foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
            { colorBaredGeometry.DrawPathes(Canvas3DLayout); }
            ColorBar.DrawStepColorBar(
                ColorBarCanvas,
                colorBaredGeometries,
                title,
                unit,
                values.Min(),
                values.Max(),
                "{0:N" + viewModel.DecimalPlaces + "}"
                );
        }

        // 解析結果更新メソッド
        private void UpdateAnalysisResult3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (string.IsNullOrEmpty(viewModel.AnalysisResultContent))
                return;

            var anaModel = viewModel.CurrentModel;
            if (anaModel == null || anaModel.Beams == null)
                return;

            string unit;


            if (viewModel.AnalysisResultContent != "沈下")
            {
                ColorBarCanvas.Children.Clear();
            }
            if (viewModel.AnalysisResultContent == "沈下")
            {
                string title = $"{viewModel.AnalysisResultContent}|{viewModel.AnalysisResultSettlementType}|{viewModel.SelectedLoadCaseName}";
                unit = "mm";
                ObservableCollection<Point3D> points = [];
                ObservableCollection<double> values = [];

                if (viewModel.AnalysisResultSettlementType == "単杭")
                {

                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;
                    foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            values.Add(pilelocation.SinglePileSettlementVL * 1000); // 沈下量
                            points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pilelocation.SinglePileSettlementLevel1s[i] * 1000); // 沈下量
                                    points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pilelocation.SinglePileSettlementLevel2s[i] * 1000); // 沈下量
                                    points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                        }
                    }
                }
                if (viewModel.AnalysisResultSettlementType == "群杭")
                {
                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

                    foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                        values.Add(pilelocation.GroupPileSettlement); // 沈下量
                    }
                }
                else if (viewModel.AnalysisResultSettlementType == "単杭+群杭")
                {
                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;
                    foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            values.Add(pilelocation.SinglePileSettlementVL * 1000 + pilelocation.GroupPileSettlement); // 沈下量
                            points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pilelocation.SinglePileSettlementLevel1s[i] * 1000 + pilelocation.GroupPileSettlement); // 沈下量
                                    points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pilelocation.SinglePileSettlementLevel2s[i] * 1000 + pilelocation.GroupPileSettlement); // 沈下量
                                    points.Add(new Point3D(pilelocation.Point3D.X, pilelocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                        }
                    }
                }
                DrawBubbleAndArrow(points, values, title, unit);
            }

            // 追加: 派生表示フラグ（Mh / Fh）
            bool isDerivedMagnitude = false;
            string derivedMagnitudeType = string.Empty;

            // 応力表示
            if (viewModel.AnalysisResultContent == "梁応力")
            {
                // インデックスと方向ベクトルの決定
                int[] indices;
                Vector<double> forceDirection;

                switch (viewModel.AnalysisResultBeamForceType)
                {
                    case "Fx":
                        indices = [0, 6];
                        forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
                        unit = "kN";
                        break;
                    case "Fy":
                        indices = [1, 7];
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]);
                        unit = "kN";
                        break;
                    case "Fz":
                        indices = [2, 8];
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
                        unit = "kN";
                        break;
                    case "Mx":
                        indices = [3, 9];
                        forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
                        unit = "kNm";
                        break;
                    case "My":
                        indices = [4, 10];
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
                        unit = "kNm";
                        break;
                    case "Mz":
                        indices = [5, 11];
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]);
                        unit = "kNm";
                        break;

                    // 追加: 曲げ合成モーメント表示 Mh = sqrt(My^2 + Mz^2)
                    case "Mh":
                        indices = [3, 9]; // placeholder index（大きさは下で直接算出）
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); // デフォルト方向（Y優先）
                        unit = "kNm";
                        isDerivedMagnitude = true;
                        derivedMagnitudeType = "Mh";
                        break;

                    // 追加: 水平力合成 Fh = sqrt(Fy^2 + Fz^2)
                    case "Fh":
                        indices = [0, 6];
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); // デフォルト方向（Y優先）
                        unit = "kN";
                        isDerivedMagnitude = true;
                        derivedMagnitudeType = "Fh";
                        break;
                    default:
                        return;
                }

                var selectedLoadCase = LoadCases.GetLoadCase(
                    viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                if (selectedLoadCase == null) return;

                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
                if (selectedLoadCombination == null) return;

                // 1回のループで最大値と描画を行う
                double maxAbsValue = 0;
                var beamResults = new List<(Beam beam, double forceI, double forceJ, double originalForceI, double originalForceJ)>();

                ObservableCollection<double> allValues = [];

                foreach (var beam in anaModel.Beams)
                {
                    var beamResult = beam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    if (beamResult == null) continue;

                    double originalForceI;
                    double originalForceJ;

                    if (isDerivedMagnitude)
                    {
                        if (derivedMagnitudeType == "Mh")
                        {
                            double MyI = beamResult.CumulativeForce.GetByIndex(4);
                            double MzI = beamResult.CumulativeForce.GetByIndex(5);
                            double MyJ = beamResult.CumulativeForce.GetByIndex(10);
                            double MzJ = beamResult.CumulativeForce.GetByIndex(11);
                            originalForceI = Math.Sqrt(MyI * MyI + MzI * MzI);
                            originalForceJ = Math.Sqrt(MyJ * MyJ + MzJ * MzJ);
                        }
                        else // "Fh"
                        {
                            double FyI = beamResult.CumulativeForce.GetByIndex(1);
                            double FzI = beamResult.CumulativeForce.GetByIndex(2);
                            double FyJ = beamResult.CumulativeForce.GetByIndex(7);
                            double FzJ = beamResult.CumulativeForce.GetByIndex(8);
                            originalForceI = Math.Sqrt(FyI * FyI + FzI * FzI);
                            originalForceJ = Math.Sqrt(FyJ * FyJ + FzJ * FzJ);
                        }
                    }
                    else
                    {
                        originalForceI = beamResult.CumulativeForce.GetByIndex(indices[0]);
                        originalForceJ = beamResult.CumulativeForce.GetByIndex(indices[1]);
                    }


                    double absForceI = Math.Abs(originalForceI);
                    double absForceJ = Math.Abs(originalForceJ);
                    allValues.Add(absForceI);
                    allValues.Add(absForceJ);
                    maxAbsValue = Math.Max(maxAbsValue, Math.Max(absForceI, absForceJ));

                    beamResults.Add((beam, absForceI, absForceJ, originalForceI, originalForceJ));
                }

                // カラーバー用ジオメトリを一度だけ生成（描画ループの前）
                var colorBaredGeometries = GetColorBarGeometries(allValues);

                //Matrix<double> t = Utils.GetNodeTransformMatrix(new Vector3D(0, 0, -1));
                //var transformedForceDirection = t.Transpose() * forceDirection;
                // 変換行列（要素局所系→表示系）
                Matrix<double> t = Utils.GetNodeTransformMatrix(new Vector3D(0, 0, -1));

                // ヘルパ: ビーム結果から端点ごとの 3 成分ベクトルを取得する
                static Vector<double> GetEnd3Vector(BeamForce bf, bool isMoment, bool isIend, string derivedType)
                {
                    // bf のインデックス対応: I端(0..5)、J端(6..11)
                    int baseIdx = isIend ? 0 : 6;
                    double fx = bf.GetByIndex(baseIdx + 0);
                    double fy = bf.GetByIndex(baseIdx + 1);
                    double fz = bf.GetByIndex(baseIdx + 2);
                    double mx = bf.GetByIndex(baseIdx + 3);
                    double my = bf.GetByIndex(baseIdx + 4);
                    double mz = bf.GetByIndex(baseIdx + 5);

                    if (!string.IsNullOrEmpty(derivedType))
                    {
                        // 派生タイプ別の比率設定
                        if (derivedType == "Mh")
                        {
                            // 曲げ合成: My, Mz の比率を使う（Mxは無視）
                            return Vector<double>.Build.DenseOfArray([0.0, mz, my]);
                        }
                        else if (derivedType == "Fh")
                        {
                            // 水平力合成: Fx, Fy の比率を使（Fzは無視）
                            return Vector<double>.Build.DenseOfArray([0, fy, fz]);
                        }
                    }

                    // 通常: 力 or モーメントの選択
                    if (isMoment)
                        return Vector<double>.Build.DenseOfArray([mx, mz, my]);
                    else
                        return Vector<double>.Build.DenseOfArray([fx, fy, fz]);
                }

                foreach (var (beam, _, _, originalForceI, originalForceJ) in beamResults)
                {
                    var beamResult = beam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    if (beamResult == null) continue;

                    // 端点ごとに raw ベクトルを取得（派生表示フラグ/タイプを考慮）
                    bool isMomentType = viewModel.AnalysisResultBeamForceType.StartsWith('M');
                    string derivedTypeLocal = isDerivedMagnitude ? derivedMagnitudeType : string.Empty;

                    Vector<double> rawI = GetEnd3Vector(beamResult.CumulativeForce, isMomentType, true, derivedTypeLocal);
                    Vector<double> rawJ = GetEnd3Vector(beamResult.CumulativeForce, isMomentType, false, derivedTypeLocal);

                    // 正規化（ゼロ長は既定方向を使う）
                    Vector<double> dirI;
                    if (rawI.L2Norm() > 1e-12)
                        dirI = rawI / rawI.L2Norm();
                    else
                        dirI = forceDirection; // 既定の方向ベクトル（switch で決めたもの）

                    Vector<double> dirJ;
                    if (rawJ.L2Norm() > 1e-12)
                        dirJ = rawJ / rawJ.L2Norm();
                    else
                        dirJ = forceDirection;

                    // ここを追加: 派生量が"Fh"または "Mh" のときだけ I端を反転する（Mh のみ逆向きにしたい場合）
                    if (!string.IsNullOrEmpty(derivedTypeLocal) && (derivedTypeLocal == "Fh" || derivedTypeLocal == "Mh"))
                    {
                        dirI = -dirI;
                    }

                    // 表示座標系に変換
                    var transformedForceDirectionI = t.Transpose() * dirI;
                    var transformedForceDirectionJ = t.Transpose() * dirJ;

                    // 元のスケーリング処理（maxAbsValue 等に応じたスケールは既存ロジックを使う）
                    double forceI = maxAbsValue == 0 ? 0 : originalForceI / maxAbsValue * viewModel.ForceDiagramMultiplier;
                    double forceJ = maxAbsValue == 0 ? 0 : originalForceJ / maxAbsValue * viewModel.ForceDiagramMultiplier;

                    Point3D nodeI3D = beam.NodeI.Coord;
                    Point3D nodeIForce3D = new(
                        nodeI3D.X + forceI * transformedForceDirectionI[0],
                        nodeI3D.Y + forceI * transformedForceDirectionI[1],
                        nodeI3D.Z + forceI * transformedForceDirectionI[2]);
                    Point3D nodeJ3D = beam.NodeJ.Coord;
                    Point3D nodeJForce3D = new(
                        nodeJ3D.X + forceJ * transformedForceDirectionJ[0],
                        nodeJ3D.Y + forceJ * transformedForceDirectionJ[1],
                        nodeJ3D.Z + forceJ * transformedForceDirectionJ[2]);

                    // 以下、既存の描画コード（投影→色分け→テキスト等）をそのまま使う
                    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                    Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                    Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                    var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                    List<double> values = [Math.Abs(originalForceI), Math.Abs(originalForceI), Math.Abs(originalForceJ), Math.Abs(originalForceJ)];
                    AddColorPolyLineGeometry(points, values, colorBaredGeometries);


                    if (viewModel.IsResultValueVisible)
                    {
                        string format = "{0:N" + viewModel.DecimalPlaces + "}";
                        if (viewModel.IsPileTopResultValueVisibleOnly)
                        {
                            if (beam.IsPileTop)
                            {
                                AddText3D(Brushes.Black, string.Format(format, originalForceI),
                                nodeIForce2D.X, nodeIForce2D.Y, "C", "C", 0.0);
                            }
                        }
                        else
                        {
                            DrawResultValueTexts(
                            viewModel.IsResultValueVisible, Brushes.Black,
                            originalForceI, originalForceJ,
                            nodeIForce2D, nodeJForce2D,
                            nodeJ2D, nodeI2D,
                            format, format);
                        }
                    }
                }

                //var colorBaredGeometries = GetColorBarGeometries(allValues);

                //foreach (var (beam, _, _, originalForceI, originalForceJ) in beamResults)
                //{
                //    double forceI = maxAbsValue == 0 ? 0 : originalForceI / maxAbsValue * viewModel.ForceDiagramMultiplier;
                //    double forceJ = maxAbsValue == 0 ? 0 : -originalForceJ / maxAbsValue * viewModel.ForceDiagramMultiplier;

                //    Point3D nodeI3D = beam.NodeI.Coord;
                //    Point3D nodeIForce3D = new(
                //        nodeI3D.X + forceI * transformedForceDirection[0],
                //        nodeI3D.Y + forceI * transformedForceDirection[1],
                //        nodeI3D.Z + forceI * transformedForceDirection[2]);
                //    Point3D nodeJ3D = beam.NodeJ.Coord;
                //    Point3D nodeJForce3D = new(
                //        nodeJ3D.X + forceJ * transformedForceDirection[0],
                //        nodeJ3D.Y + forceJ * transformedForceDirection[1],
                //        nodeJ3D.Z + forceJ * transformedForceDirection[2]);
                //    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                //    Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                //    Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                //    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);
                //    var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                //    List<double> values = [Math.Abs(originalForceI), Math.Abs(originalForceI), Math.Abs(originalForceJ), Math.Abs(originalForceJ)];
                //    //AddPolyLineGeometry(points, viewModel.CanvasGeometry.PathGeoDisp);

                //    AddColorPolyLineGeometry(points, values, colorBaredGeometries);

                //    if (viewModel.IsResultValueVisible)
                //    {
                //        string format = "{0:N" + viewModel.DecimalPlaces + "}";
                //        if (viewModel.IsPileTopResultValueVisibleOnly)
                //        {
                //            if (beam.IsPileTop)
                //            {
                //                AddText3D(Brushes.Black, string.Format(format, originalForceI),
                //                nodeIForce2D.X, nodeIForce2D.Y, "C", "C", 0.0);
                //            }
                //        }
                //        else
                //        {
                //            DrawResultValueTexts(
                //            viewModel.IsResultValueVisible, Brushes.Black,
                //            originalForceI, originalForceJ,
                //            nodeIForce2D, nodeJForce2D,
                //            nodeJ2D, nodeI2D,
                //            format, format);
                //        }
                //    }
                //}

                foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                {
                    colorBaredGeometry.DrawPathes(Canvas3DLayout);
                }

                // 空チェック＋min/maxの順を修正
                if (allValues.Count > 0)
                {
                    ColorBar.DrawStepColorBar(
                        ColorBarCanvas,
                        colorBaredGeometries,
                        viewModel.AnalysisResultBeamForceType,
                        unit,
                        allValues.Min(), // 先に最小
                        allValues.Max(), // 次に最大
                        "{0:N" + viewModel.DecimalPlaces + "}"
                    );
                }
                else
                {
                    ColorBarCanvas.Children.Clear();
                }
            }

            //string unit = "unit";
            // 変位表示
            if (viewModel.AnalysisResultContent == "節点変位")
            {
                string format = "{0:N" + viewModel.DecimalPlaces + "}";
                Vector<double> effectiveVector;
                double multiplier;
                switch (viewModel.AnalysisResultNodeDisplacementType)
                {
                    case "UH":
                        effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 0, 0, 0, 0]);
                        unit = "mm";
                        multiplier = 1000;
                        break;
                    case "θH":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 1, 0]);
                        unit = "rad";
                        multiplier = 1;
                        break;
                    case "UX":
                        effectiveVector = Vector<double>.Build.DenseOfArray([1, 0, 0, 0, 0, 0]);
                        unit = "mm";
                        multiplier = 1000;
                        break;
                    case "UY":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 1, 0, 0, 0, 0]);
                        unit = "mm";
                        multiplier = 1000;
                        break;
                    case "UZ":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 1, 0, 0, 0]);
                        unit = "mm";
                        multiplier = 1000;
                        break;

                    case "θX":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 0, 0]);
                        unit = "rad";
                        multiplier = 1;
                        break;
                    case "θY":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 1, 0]);
                        unit = "rad";
                        multiplier = 1;
                        break;
                    case "θZ":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 0, 1]);
                        unit = "rad";
                        multiplier = 1;
                        break;

                    default:
                        return;
                }

                var selectedLoadCase = LoadCases.GetLoadCase(
                    viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

                // 1. 全てのvalueを収集（必ず multiplier を掛ける）
                ObservableCollection<double> allValues = [];

                // DummyBeams
                if (viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane != null)
                {
                    foreach (var dummyBeam in anaModel.DummyBeams)
                    {
                        NodeDisp nodeDispI = dummyBeam.NodeI.GetNodeResult(
                            anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                        double dispI = Math.Sqrt(
                            Math.Pow(nodeDispI.Ux * effectiveVector[0], 2) +
                            Math.Pow(nodeDispI.Uy * effectiveVector[1], 2) +
                            Math.Pow(nodeDispI.Uz * effectiveVector[2], 2) +
                            Math.Pow(nodeDispI.Rx * effectiveVector[3], 2) +
                            Math.Pow(nodeDispI.Ry * effectiveVector[4], 2) +
                            Math.Pow(nodeDispI.Rz * effectiveVector[5], 2));
                        NodeDisp nodeDispJ = dummyBeam.NodeJ.GetNodeResult(
                            anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                        double dispJ = Math.Sqrt(
                            Math.Pow(nodeDispJ.Ux * effectiveVector[0], 2) +
                            Math.Pow(nodeDispJ.Uy * effectiveVector[1], 2) +
                            Math.Pow(nodeDispJ.Uz * effectiveVector[2], 2) +
                            Math.Pow(nodeDispJ.Rx * effectiveVector[3], 2) +
                            Math.Pow(nodeDispJ.Ry * effectiveVector[4], 2) +
                            Math.Pow(nodeDispJ.Rz * effectiveVector[5], 2));
                        allValues.Add(Math.Abs(dispI) * multiplier); // mm
                        allValues.Add(Math.Abs(dispJ) * multiplier); // mm
                    }
                }

                // Beams
                foreach (var beam in anaModel.Beams)
                {
                    NodeDisp nodeDispI = beam.NodeI.GetNodeResult(
                        anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                    double dispI = Math.Sqrt(
                            Math.Pow(nodeDispI.Ux * effectiveVector[0], 2) +
                            Math.Pow(nodeDispI.Uy * effectiveVector[1], 2) +
                            Math.Pow(nodeDispI.Uz * effectiveVector[2], 2) +
                            Math.Pow(nodeDispI.Rx * effectiveVector[3], 2) +
                            Math.Pow(nodeDispI.Ry * effectiveVector[4], 2) +
                            Math.Pow(nodeDispI.Rz * effectiveVector[5], 2));
                    NodeDisp nodeDispJ = beam.NodeJ.GetNodeResult(
                        anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                    double dispJ = Math.Sqrt(
                            Math.Pow(nodeDispJ.Ux * effectiveVector[0], 2) +
                            Math.Pow(nodeDispJ.Uy * effectiveVector[1], 2) +
                            Math.Pow(nodeDispJ.Uz * effectiveVector[2], 2) +
                            Math.Pow(nodeDispJ.Rx * effectiveVector[3], 2) +
                            Math.Pow(nodeDispJ.Ry * effectiveVector[4], 2) +
                            Math.Pow(nodeDispJ.Rz * effectiveVector[5], 2));
                    allValues.Add(Math.Abs(dispI) * multiplier); // mm
                    allValues.Add(Math.Abs(dispJ) * multiplier); // mm
                }

                // 2. カラーバーを一度だけ生成
                var colorBaredGeometries = GetColorBarGeometries(allValues);

                // 1回のループで最大値と描画を行う（maxAbsValue を multiplier 適用後で統一）
                double maxAbsValue = 0;

                // 根入れ部
                if (viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane != null)
                {
                    var dummyBeamResults = new List<(
                        DummyBeam dummyBeam,
                        NodeDisp nodeDispI,
                        NodeDisp nodeDispJ,
                        double dispI,
                        double dispJ,
                        double originalDispI,
                        double originalDispJ)
                        >();

                    // 根入れ部ダミー要素
                    foreach (var dummyBeam in anaModel.DummyBeams)
                    {
                        NodeDisp originalNodeDispI = dummyBeam.NodeI.GetNodeResult(
                            anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                        double originalDispI = Math.Sqrt(
                            Math.Pow(originalNodeDispI.Ux * effectiveVector[0], 2) +
                            Math.Pow(originalNodeDispI.Uy * effectiveVector[1], 2) +
                            Math.Pow(originalNodeDispI.Uz * effectiveVector[2], 2) +
                            Math.Pow(originalNodeDispI.Rx * effectiveVector[3], 2) +
                            Math.Pow(originalNodeDispI.Ry * effectiveVector[4], 2) +
                            Math.Pow(originalNodeDispI.Rz * effectiveVector[5], 2));
                        NodeDisp originalNodeDispJ = dummyBeam.NodeJ.GetNodeResult(
                            anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                        double originalDispJ = Math.Sqrt(
                            Math.Pow(originalNodeDispJ.Ux * effectiveVector[0], 2) +
                            Math.Pow(originalNodeDispJ.Uy * effectiveVector[1], 2) +
                            Math.Pow(originalNodeDispJ.Uz * effectiveVector[2], 2) +
                            Math.Pow(originalNodeDispJ.Rx * effectiveVector[3], 2) +
                            Math.Pow(originalNodeDispJ.Ry * effectiveVector[4], 2) +
                            Math.Pow(originalNodeDispJ.Rz * effectiveVector[5], 2));
                        double absDispI = Math.Abs(originalDispI) * multiplier;
                        double absDispJ = Math.Abs(originalDispJ) * multiplier;
                        maxAbsValue = Math.Max(maxAbsValue, Math.Max(absDispI, absDispJ));
                        //allValues.Add(absDispI);
                        //allValues.Add(absDispJ);

                        dummyBeamResults.Add((dummyBeam, originalNodeDispI, originalNodeDispJ, absDispI, absDispJ, originalDispI, originalDispJ));
                    }

                    // 根入れ部ダミー要素結果
                    foreach (var (dummyBeam, originalNodeDispI, originalNodeDispJ, absDispI, absDispJ, originalDispI, originalDispJ) in dummyBeamResults)
                    {
                        if (maxAbsValue == 0) continue;

                        Point3D nodeI3D = dummyBeam.NodeI.Coord;
                        Point3D nodeIDisp3D = new(
                            nodeI3D.X + originalNodeDispI.Ux * effectiveVector[0] * viewModel.DispDiagramMultiplier,
                            nodeI3D.Y + originalNodeDispI.Uy * effectiveVector[1] * viewModel.DispDiagramMultiplier,
                            nodeI3D.Z + originalNodeDispI.Uz * effectiveVector[2] * viewModel.DispDiagramMultiplier);
                        Point3D nodeJ3D = dummyBeam.NodeJ.Coord;
                        Point3D nodeJDisp3D = new(
                           nodeJ3D.X + originalNodeDispJ.Ux * effectiveVector[0] * viewModel.DispDiagramMultiplier,
                           nodeJ3D.Y + originalNodeDispJ.Uy * effectiveVector[1] * viewModel.DispDiagramMultiplier,
                           nodeJ3D.Z + originalNodeDispJ.Uz * effectiveVector[2] * viewModel.DispDiagramMultiplier);
                        Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                        Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
                        Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
                        Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                        var points = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
                        // AddColorPolyLineGeometry に渡す値は必ず multiplier 適用済み
                        List<double> values = [absDispI, absDispI, absDispJ, absDispJ];
                        //double absDispI = Math.Abs(originalDispI);
                        //double absDispJ = Math.Abs(originalDispJ);
                        ////List<double> values = [absDispI, absDispI, absDispJ, absDispJ];
                        //List<double> values = new() {
                        //    absDispI * multiplier, absDispI * multiplier, absDispJ * multiplier, absDispJ * multiplier };
                        AddColorPolyLineGeometry(points, values, colorBaredGeometries);

                        if (viewModel.IsResultValueVisible)
                        {
                            DrawResultValueTexts(
                                viewModel.IsResultValueVisible, Brushes.Black,
                                originalDispI * multiplier, originalDispJ * multiplier,
                                nodeIDisp2D, nodeJDisp2D,
                                nodeJ2D, nodeI2D,
                                format, format);
                        }
                    }
                }

                var beamResults = new List<(
                    Beam dummyBeam,
                    NodeDisp nodeDispI,
                    NodeDisp nodeDispJ,
                    double dispI,
                    double dispJ,
                    double originalDispI,
                    double originalDispJ
                    )>();

                // 梁要素
                foreach (var beam in anaModel.Beams)
                {
                    NodeDisp originalNodeDispI = beam.NodeI.GetNodeResult(
                        anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                    double originalDispI = Math.Sqrt(
                            Math.Pow(originalNodeDispI.Ux * effectiveVector[0], 2) +
                            Math.Pow(originalNodeDispI.Uy * effectiveVector[1], 2) +
                            Math.Pow(originalNodeDispI.Uz * effectiveVector[2], 2) +
                            Math.Pow(originalNodeDispI.Rx * effectiveVector[3], 2) +
                            Math.Pow(originalNodeDispI.Ry * effectiveVector[4], 2) +
                            Math.Pow(originalNodeDispI.Rz * effectiveVector[5], 2));
                    NodeDisp originalNodeDispJ = beam.NodeJ.GetNodeResult(
                        anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction).CumulativeDisp;
                    double originalDispJ = Math.Sqrt(
                            Math.Pow(originalNodeDispJ.Ux * effectiveVector[0], 2) +
                            Math.Pow(originalNodeDispJ.Uy * effectiveVector[1], 2) +
                            Math.Pow(originalNodeDispJ.Uz * effectiveVector[2], 2) +
                            Math.Pow(originalNodeDispJ.Rx * effectiveVector[3], 2) +
                            Math.Pow(originalNodeDispJ.Ry * effectiveVector[4], 2) +
                            Math.Pow(originalNodeDispJ.Rz * effectiveVector[5], 2));
                    double absDispI = Math.Abs(originalDispI) * 1000;
                    double absDispJ = Math.Abs(originalDispJ) * 1000;
                    maxAbsValue = Math.Max(maxAbsValue, Math.Max(absDispI, absDispJ));

                    beamResults.Add((beam, originalNodeDispI, originalNodeDispJ, absDispI, absDispJ, originalDispI, originalDispJ));
                }

                // 梁要素結果
                foreach (var (beam, originalNodeDispI, originalNodeDispJ, absDispI, absDispJ, originalDispI, originalDispJ) in beamResults)
                {
                    if (maxAbsValue == 0) continue;

                    Point3D nodeI3D = beam.NodeI.Coord;
                    Point3D nodeIDisp3D = new(
                        nodeI3D.X + originalNodeDispI.Ux * effectiveVector[0] * viewModel.DispDiagramMultiplier,
                        nodeI3D.Y + originalNodeDispI.Uy * effectiveVector[1] * viewModel.DispDiagramMultiplier,
                        nodeI3D.Z + originalNodeDispI.Uz * effectiveVector[2] * viewModel.DispDiagramMultiplier);
                    Point3D nodeJ3D = beam.NodeJ.Coord;
                    Point3D nodeJDisp3D = new(
                        nodeJ3D.X + originalNodeDispJ.Ux * effectiveVector[0] * viewModel.DispDiagramMultiplier,
                        nodeJ3D.Y + originalNodeDispJ.Uy * effectiveVector[1] * viewModel.DispDiagramMultiplier,
                        nodeJ3D.Z + originalNodeDispJ.Uz * effectiveVector[2] * viewModel.DispDiagramMultiplier);
                    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                    Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
                    Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
                    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                    var points = new[] { nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D };
                    List<double> values = [absDispI, absDispI, absDispJ, absDispJ];
                    //double absDispI = Math.Abs(originalDispI) * multiplier;
                    //double absDispJ = Math.Abs(originalDispJ) * multiplier;
                    //List<double> values = [absDispI, absDispI, absDispJ, absDispJ];
                    AddColorPolyLineGeometry(points, values, colorBaredGeometries);

                    if (viewModel.IsResultValueVisible)
                    {
                        //string format = "{0:N" + viewModel.DecimalPlaces + "}";
                        if (viewModel.IsPileTopResultValueVisibleOnly)
                        {
                            if (beam.IsPileTop)
                            {
                                AddText3D(Brushes.Black, string.Format(format, originalDispI * multiplier),
                                nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
                            }
                        }
                        else
                        {
                            DrawResultValueTexts(
                                viewModel.IsResultValueVisible, Brushes.Black,
                                originalDispI * multiplier, originalDispJ * multiplier,
                                nodeIDisp2D, nodeJDisp2D,
                                nodeJ2D, nodeI2D,
                                format, format);
                        }
                    }
                }

                // カラーバー描画
                foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                {
                    colorBaredGeometry.DrawPathes(Canvas3DLayout);
                }
                ColorBar.DrawStepColorBar(
                    ColorBarCanvas,
                    colorBaredGeometries,
                    viewModel.AnalysisResultNodeDisplacementType,
                    unit,
                    allValues.Min(),
                    allValues.Max(),
                    "{0:N" + viewModel.DecimalPlaces + "}"
                );
            }
        }

        private void DrawResultValueTexts(
            bool isVisible, Brush solidColorBrush,
            double valueI, double valueJ,
            Point pointI, Point pointJ,
            Point nodeJ2D, Point nodeI2D,
            string formatI, string formatJ)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (!isVisible) return;
            if (viewModel.IsMidSpanResultValueVisibleOnly)
            {
                double value = (Math.Abs(valueI) + Math.Abs(valueJ)) / 2;
                AddText3D(solidColorBrush, string.Format(formatI, value),
                    (pointI.X + pointJ.X) / 2, (pointI.Y + pointJ.Y) / 2, "C", "C", 0.0);
            }
            else
            {
                Point textAdjustUnit = GetAdjustUnit(nodeJ2D, nodeI2D);
                double textAdjustX = textAdjustUnit.X * viewModel.TextPosiitonAdjuster;
                double textAdjustY = textAdjustUnit.Y * viewModel.TextPosiitonAdjuster;
                AddText3D(solidColorBrush, string.Format(formatI, valueI),
                    pointI.X - textAdjustX, pointI.Y - textAdjustY, "C", "C", 0.0);
                AddText3D(solidColorBrush, string.Format(formatJ, valueJ),
                    pointJ.X + textAdjustX, pointJ.Y + textAdjustY, "C", "C", 0.0);
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

            double z = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;
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
                z, viewModel.DispDiagramMultiplier,
                sumX, sumY, sumS);

            bool needRebuild = viewModel.SettlementWorldCache.Fingerprint == null ||
                               !viewModel.SettlementWorldCache.Fingerprint.Equals(fp);

            if (needRebuild)
            {
                // 再構築
                var cache = new PileDesign.ViewModels.SettlementGridRenderCache();

                // カラーバー帯（色と範囲）
                var allValues = new ObservableCollection<double>(items.Select(x => x.Settlement));
                var colorBarGeometries = GetColorBarGeometries(allValues);
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
                        var s1 = z - p1.Settlement * viewModel.DispDiagramMultiplier;
                        var s2 = z - p2.Settlement * viewModel.DispDiagramMultiplier;
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
                        var s1 = z - p1.Settlement * viewModel.DispDiagramMultiplier;
                        var s2 = z - p2.Settlement * viewModel.DispDiagramMultiplier;
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
                                    double zz = z - cellPts[v].Settlement * viewModel.DispDiagramMultiplier;
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
                                        var p = Interpolate3D(cellPts[a], cellPts[b], level, z, viewModel);
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
                                contour3D.Add(Interpolate3D(p00, p10, contour, z, viewModel));
                            if ((v10 - contour) * (v11 - contour) < 0)
                                contour3D.Add(Interpolate3D(p10, p11, contour, z, viewModel));
                            if ((v11 - contour) * (v01 - contour) < 0)
                                contour3D.Add(Interpolate3D(p11, p01, contour, z, viewModel));
                            if ((v01 - contour) * (v00 - contour) < 0)
                                contour3D.Add(Interpolate3D(p01, p00, contour, z, viewModel));

                            if (contour3D.Count >= 2)
                            {
                                cache.Contours3D.Add(contour3D);
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

                if (!_settlementBandPaths.TryGetValue(color, out var path))
                {
                    path = new Path
                    {
                        Fill = new SolidColorBrush(color),
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
                    "{0:N" + viewModel.DecimalPlaces + "}"
                );
            }

            // 線形補間（3D座標生成）
            static Point3D Interpolate3D(SettlementGridDataItem p1, SettlementGridDataItem p2, double contour, double z, MainWindowViewModel viewModel)
            {
                double t = (contour - p1.Settlement) / (p2.Settlement - p1.Settlement);
                double x = p1.X + t * (p2.X - p1.X);
                double y = p1.Y + t * (p2.Y - p1.Y);
                double zz = z - (p1.Settlement + t * (p2.Settlement - p1.Settlement)) * viewModel.DispDiagramMultiplier;
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

        // XYZ軸の更新メソッド
        private void UpdateAxes3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double length = 300;
            Point3D point3D0 = viewModel.CurrentInputModel.FundamentalInput.Point3D0;
            var axisPoints = new (Point3D start, Point3D end,
                Brush color, string name, PathGeometry pathGeometry)[]
            {
                (point3D0, new(point3D0.X + length, point3D0.Y, point3D0.Z),
                    Brushes.Red, "AxisX", viewModel.CanvasGeometry.PathGeoAxisX),
                (point3D0, new(point3D0.X, point3D0.Y + length,  point3D0.Z),
                    Brushes.Green, "AxisY", viewModel.CanvasGeometry.PathGeoAxisY),
                (point3D0, new(point3D0.X, point3D0.Y,  point3D0.Z + length),
                    Brushes.Blue, "AxisZ", viewModel.CanvasGeometry.PathGeoAxisZ),
                (point3D0, new(point3D0.X - length, point3D0.Y,  point3D0.Z),
                    Brushes.DarkRed, "AxisXN", viewModel.CanvasGeometry.PathGeoAxisXM),
                (point3D0, new(point3D0.X, point3D0.Y - length,  point3D0.Z),
                    Brushes.DarkGreen, "AxisYN", viewModel.CanvasGeometry.PathGeoAxisYM),
                (point3D0, new(point3D0.X, point3D0.Y,  point3D0.Z - length),
                    Brushes.DarkBlue, "AxisZN", viewModel.CanvasGeometry.PathGeoAxisZM)
            };

            // 軸ごとにPathGeometryへ追加
            foreach (var (start3D, end3D, color, name, pathGeometry) in axisPoints)
            {
                Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                Point end = viewModel.CanvasThreeDView.Transformation(end3D);

                // ここでPathGeoAxesに追加
                AddAxisLine3D(start, end, color, name, pathGeometry);
            }
        }

        // XYZ軸の追加メソッド
        private static void AddAxisLine3D(Point start, Point end, Brush color, string name, PathGeometry pathGeometry)
        {
            // 無効な値が含まれている場合は、Lineを作成しない
            if (double.IsNaN(start.X) || double.IsNaN(start.Y) || double.IsNaN(end.X) || double.IsNaN(end.Y) ||
                double.IsInfinity(start.X) || double.IsInfinity(start.Y) || double.IsInfinity(end.X) || double.IsInfinity(end.Y))
            { return; }

            var lineGeometry = new LineGeometry
            {
                StartPoint = start,
                EndPoint = end
            };
            pathGeometry.AddGeometry(lineGeometry);
        }

        // 荷重面描画の更新
        private void UpdateSettlementLoad3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            List<PathFigure> rectangleGeometries = [];

            if (viewModel.CurrentInputModel.PileGroupSettlement.LoadingType == "個別十字")
            {
                if (viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    //MessageBox.Show("個別十字荷重を作成するには地盤杭セットが作られている必要があります。キャンセルします。");
                    return;
                }

                foreach (PileLayoutDataItem pileLayout in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    Point3D loc1 = new(pileLayout.Point3D.X, pileLayout.Point3D.Y, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude);
                    double radius = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pileLayout.SoilPileAltNo - 1].GroupPileLoadDia * 0.5;

                    List<(Point3D, Point3D)> rectangles = PileGroupSettlement.GetFiveRectsPoints(loc1, radius);
                    rectangleGeometries.AddRange(viewModel.CanvasThreeDView.RectsTranformation(rectangles));
                }
            }
            else if (viewModel.CurrentInputModel.PileGroupSettlement.LoadingType == "任意矩形")
            {
                foreach (Models.InputData.RectLoad rectLoad in viewModel.CurrentInputModel.PileGroupSettlement.RectLoads)
                {
                    Point3D loc1 = new(rectLoad.X1, rectLoad.Y1, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude);
                    Point3D loc3 = new(rectLoad.X2, rectLoad.Y2, viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude);

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

        // 杭周地盤描画の更新
        private void UpdateGround3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            foreach (PileLayoutDataItem pilelocation in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pilelocation.IsVisible)
                { continue; }

                int pileBodyIndex = pilelocation.PileBodyNo - 1;
                int groundIndex = pilelocation.GroundNo - 1;

                // インデックス範囲チェック
                if (pileBodyIndex < 0 || pileBodyIndex >= viewModel.CurrentInputModel.PileBodies.Count)
                    continue; // またはエラー通知

                if (groundIndex < 0 || groundIndex >= viewModel.CurrentInputModel.GroundsInput.Count)
                    continue; // またはエラー通知

                // 地盤dia
                double soilDia;
                if (viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileToeDia == 0)
                {
                    soilDia = 2.0 * viewModel.CanvasThreeDView.Scale;
                }
                else
                {
                    soilDia = viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileToeDia / 1000.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
                }

                // 地表
                double groundTopAltitude = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundTopAltitude;
                Point3D loc1 = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, groundTopAltitude);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                EllipseGeometry ellipse1 = new(coord1, soilDia * 0.5, soilDia * 0.5 * flattening);
                viewModel.CanvasGeometry.PathGeoPileSoils.AddGeometry(ellipse1);

                // 地下水位
                double groundWaterTableAltitude = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundWaterTableAltitude;
                Point3D loc3 = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, groundWaterTableAltitude);
                Point coord3 = viewModel.CanvasThreeDView.Transformation(loc3);

                EllipseGeometry ellipse3 = new(coord3, soilDia * 0.5, soilDia * 0.5 * flattening);
                viewModel.CanvasGeometry.PathGeoPileGroundWater.AddGeometry(ellipse3);

                foreach (GroundLayerInput groundLayerInput in viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers)
                {
                    double zBtm = groundLayerInput.BottomAltitude;
                    double zTop = groundLayerInput.LayerThickness + zBtm;
                    string granularityClass = groundLayerInput.GranularityClass;

                    Point3D top = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, zTop);
                    Point3D btm = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, zBtm);
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
        //private void UpdateNValue3D()
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


        //        List<Point> points = [];
        //        // N値
        //        foreach (GroundMassDataInput groundMassDataInput in viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundMassesData)
        //        {
        //            double altitudeDepth = groundMassDataInput.AltitudeDepth;
        //            Point3D loc = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, altitudeDepth);
        //            Point coord = viewModel.CanvasThreeDView.Transformation(loc);
        //            double xShift = groundMassDataInput.NValue / 60.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
        //            coord.X += xShift;
        //            points.Add(coord);
        //        }

        //        double groundTopAltitude = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundTopAltitude;
        //        double groundBtmAltitude = viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers[^1].BottomAltitude;

        //        AddPolyLineGeometryWithMarkers(points, pathGeometry, false, 2, pathGeometry);

        //        Point3D topLoc = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, groundTopAltitude);
        //        Point3D btmLoc = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, groundBtmAltitude);
        //        Point topCoord0 = viewModel.CanvasThreeDView.Transformation(topLoc);
        //        Point btmCoord0 = viewModel.CanvasThreeDView.Transformation(btmLoc);
        //        Point topCoordEnd = new();
        //        double xShiftEnd = 0;
        //        // 0-60
        //        for (int i = 0; i <= 60; i += 10)
        //        {
        //            double xShift = i / 60.0 * 2.0 * viewModel.CanvasThreeDView.Scale;
        //            Point topCoord = new() { X = topCoord0.X + xShift, Y = topCoord0.Y };
        //            Point btmCoord = new() { X = btmCoord0.X + xShift, Y = btmCoord0.Y };

        //            AddLineGeometry(topCoord, btmCoord, pathGeometryGrids);

        //            if(i == 60)
        //            {
        //                xShiftEnd = xShift;
        //            }
        //        }

        //        topCoordEnd = new() { X = topCoord0.X + xShiftEnd, Y = topCoord0.Y };
        //        AddLineGeometry(topCoord0, topCoordEnd, pathGeometryGrids);

        //        foreach(GroundLayerInput groundLayerInput in viewModel.CurrentInputModel.GroundsInput[pilelocation.GroundNo - 1].GroundLayers)
        //        {
        //            Point3D loc = new(pilelocation.Point3D.X, pilelocation.Point3D.Y, groundLayerInput.BottomAltitude);
        //            Point coord0 = viewModel.CanvasThreeDView.Transformation(loc);
        //            Point coordEnd = new() { X = coord0.X + xShiftEnd, Y = coord0.Y };
        //            AddLineGeometry(coord0, coordEnd, pathGeometryGrids);
        //        }
        //    }
        //}

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

                    double value = (type == "density") ? layer.Density : (type == "cohesive") ? layer.Cohesive : (type == "Vs") ? layer.Vs : (type == "Es") ? layer.Es : layer.Es;
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


        // 要素描画の更新
        private void UpdateGeneralElement3D()
        {
            if (Canvas3DLayout == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel == null) return;

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                if (element.Nodes.Count == 0 || element.Nodes.Count == 1) { continue; }

                Point3D loc0;
                Point3D loc1;
                double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

                if (viewModel.IsElementShownAtSettlementPlane)
                {
                    loc0 = new(element.Nodes[0].Point3D.X, element.Nodes[0].Point3D.Y, loadingPlaneAlt);
                    loc1 = new(element.Nodes[1].Point3D.X, element.Nodes[1].Point3D.Y, loadingPlaneAlt);
                }
                else
                {
                    loc0 = element.Nodes[0].Point3D;
                    loc1 = element.Nodes[1].Point3D;
                }

                if (viewModel.IsShrinkElementMode)
                {
                    (loc0, loc1) = GetShrinkedElementPoints(loc0, loc1);
                }

                Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathElements.AddGeometry(lineGeometry);

                // 要素番号
                if (viewModel.IsElementNoVisible)
                {
                    double x = (coord0.X + coord1.X) * 0.5;
                    double y = (coord0.Y + coord1.Y) * 0.5;
                    double theta;
                    if (coord1.X != coord0.X)
                    {
                        theta = 180 / Math.PI * Math.Atan((coord1.Y - coord0.Y) / (coord1.X - coord0.X));
                    }
                    else
                    {
                        theta = 90;
                    }
                    AddText3D(Brushes.SaddleBrown, GetElementNoText(element), x, y, "C", "B", theta);
                }
            }
        }

        // 変形後の要素描画の更新
        private void UpdateDeformedGeneralelement3D(
            ObservableCollection<double> values)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;

            if (viewModel == null || values.Count == 0)
            { return; }

            double flattening = viewModel.CanvasThreeDView.Flattening;
            double flattening0 = Math.Sqrt(1 - Math.Pow(flattening, 2));
            double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

            if (absMaxValue == 0.0) return;

            if (Canvas3DLayout == null) return;

            if (viewModel.CurrentInputModel == null) return;

            Point3D loc0;
            Point3D loc1;
            double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                if (element.Nodes.Count == 0 || element.Nodes.Count == 1) { continue; }

                if (viewModel.IsElementShownAtSettlementPlane)
                {
                    loc0 = new(element.Nodes[0].Point3D.X, element.Nodes[0].Point3D.Y, loadingPlaneAlt);
                    loc1 = new(element.Nodes[1].Point3D.X, element.Nodes[1].Point3D.Y, loadingPlaneAlt);
                }
                else
                {
                    loc0 = element.Nodes[0].Point3D;
                    loc1 = element.Nodes[1].Point3D;
                }

                double y0 = maxArrowLength2D * values[element.Nodes[0].No - 1] / absMaxValue;
                double y1 = maxArrowLength2D * values[element.Nodes[1].No - 1] / absMaxValue;

                Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0) + new Vector(0, y0 * flattening0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1) + new Vector(0, y1 * flattening0);

                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathElements.AddGeometry(lineGeometry);
            }
        }

        // 短縮要素の節点を返すメソッド
        private static (Point3D, Point3D) GetShrinkedElementPoints(Point3D point0, Point3D point1, double factor = 0.8)
        {
            // factor 
            double factorV = 0.5 + 0.5 * factor;
            Vector3D vector = point1 - point0;
            return (point1 - factorV * vector, point0 + factorV * vector);
        }

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
                    EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), acturalNodeSize * 0.5, acturalNodeSize * 0.5);
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

                            EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), acturalNodeSize * 0.75, acturalNodeSize * 0.75);
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
                EllipseGeometry ellipse1 = new(coord001, acturalNodeSize * 0.5, acturalNodeSize * 0.5);
                EllipseGeometry ellipse2 = new(coord002, acturalNodeSize * 0.5, acturalNodeSize * 0.5);
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

        // 杭要素の更新メソッド
        private void UpdatePileElement(PileLayoutDataItem pilelocation)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel.PileBodies.Count == 0) return;

            ObservableCollection<PileBodySegment> pileBodySegments;
            PileBodyInput pileBody = viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1];
            var zs = new ObservableCollection<double>();

            if (!viewModel.IsElementSplit) // 要素未分割の場合
            {
                pileBodySegments = viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileBodySegments;
                zs.Add(pilelocation.Point3D.Z);
                foreach (var segment in pileBodySegments)
                {
                    zs.Add(pilelocation.Point3D.Z - segment.SegmentDepth);
                }
            }

            else // 要素分割済の場合
            {
                var soilPile = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pilelocation.SoilPileAltNo - 1];
                pileBodySegments = soilPile.PileBodySegments;
                zs = new ObservableCollection<double>(soilPile.ZDataItems.Select(zDataItem => zDataItem.Z));
            }

            double x = pilelocation.Point3D.X;
            double y = pilelocation.Point3D.Y;

            var pointT = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[0]));
            var pointB = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[^1]));

            AddLineGeometry(pointT, pointB, viewModel.IsElementSplit ? viewModel.CanvasGeometry.PathGeoPileDividedElems : viewModel.CanvasGeometry.PathGeoPileElems);

            if (pileBodySegments.Count == 0) return;

            double pileBottomDia = pileBodySegments[^1].PileSection.PileDiameter / 1000.0;
            double pileToeDia = viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileToeDia / 1000.0;
            double pileToeAngle = pileBody.InsituPileToeAngle;
            double pileToeHeight = pileBody.InsituPileToeHeight / 1000.0;
            double pileToeHeightRatio = pileBody.PrecastConcretePileToeHeightRatio;

            double zToeTop = pileToeDia <= pileBottomDia ? zs[^1] :
                (viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭"
                ? zs[^1] + (pileToeDia - pileBottomDia) * 0.5 / Math.Tan(pileToeAngle * Math.PI / 180) + pileToeHeight
                : zs[^1] + pileToeDia * pileToeHeightRatio);

            for (int i = 0; i < zs.Count - 1; i++)
            {
                double z1 = zs[i];
                double z2 = Math.Max(zs[i + 1], zToeTop);

                var point1 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z1));
                var point2 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z2));
                var point3 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[i + 1]));

                AddEllipseGeometry(point2, point3, viewModel.IsElementSplit ? viewModel.CanvasGeometry.PathGeoPileDividedNonTopNodes : viewModel.CanvasGeometry.PathGeoPileNonTopNodes);

                // 
                if (viewModel.IsPileSectionVisible)
                {
                    double pileDia2D = pileBodySegments[i].PileSection.PileDiameter / 1000.0 * viewModel.CanvasThreeDView.Scale;///
                    double pileDia = pileBodySegments[i].PileSection.PileDiameter / 1000.0;
                    double flattening = viewModel.CanvasThreeDView.Flattening;

                    AddPileSectionGeometry(point1, point2, pileDia2D, flattening);

                    if (viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭")
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            // 拡底部ジオメトリ
                            AddInsituPileToeGeometry(
                                pointB, pileToeDia, pileDia, flattening,
                                viewModel.CurrentInputModel.PileBodies[pilelocation.PileBodyNo - 1].PileConstructionType, pileBodySegments,
                                pileToeAngle, pileToeHeight);
                        }
                    }
                    else
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            string pileConstructionType = "aaa";
                            // 先端球根ジオメトリ
                            AddConcretePrecastPileToeGeometry(pointB, pileToeDia, pileDia, flattening, pileConstructionType, pileBodySegments);
                        }
                    }

                    //要素座標系
                    if (viewModel.IsBeamLocalAxesVisible)
                    {
                        //double length = 0.01 * viewModel.CanvasThreeDView.Scale; // 軸の長さ
                        double length = viewModel.LabelSize / 20.0 * 0.5;
                        Point3D point3D0 = new(x, y, (zs[i] + zs[i + 1]) * 0.5); // 要素中心
                        Matrix<double> t = Utils.GetNodeTransformMatrix(new Vector3D(0, 0, -1));
                        var globalX = Vector<double>.Build.DenseOfArray([length, 0, 0]); // X軸方向
                        var globalY = Vector<double>.Build.DenseOfArray([0, length, 0]); // Y軸方向
                        var globalZ = Vector<double>.Build.DenseOfArray([0, 0, length]); // Z軸方向
                        var localX = t.Transpose() * globalX;
                        var localY = t.Transpose() * globalY;
                        var localZ = t.Transpose() * globalZ;

                        Point3D end3DX = new(point3D0.X + localX[0], point3D0.Y + localX[1], point3D0.Z + localX[2]);
                        Point3D end3DY = new(point3D0.X + localY[0], point3D0.Y + localY[1], point3D0.Z + localY[2]);
                        Point3D end3DZ = new(point3D0.X + localZ[0], point3D0.Y + localZ[1], point3D0.Z + localZ[2]);


                        var axisPoints = new (Point3D start, Point3D end, Brush color, string name, PathGeometry pathGeometry)[]
                        {
                        (point3D0, end3DX, Brushes.Red, "AxisX", viewModel.CanvasGeometry.PathGeoAxisX),
                        (point3D0, end3DY, Brushes.Green, "AxisY", viewModel.CanvasGeometry.PathGeoAxisY),
                        (point3D0, end3DZ, Brushes.Blue, "AxisZ", viewModel.CanvasGeometry.PathGeoAxisZ),
                        };

                        foreach (var (start3D, end3D, color, name, pathGeometry) in axisPoints)
                        {
                            Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                            Point end = viewModel.CanvasThreeDView.Transformation(end3D);
                            AddAxisLine3D(start, end, color, name, pathGeometry);
                        }
                    }
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
            var ellipse = new EllipseGeometry(new Point(point2.X, point3.Y), acturalNodeSize * 0.5, acturalNodeSize * 0.5);
            pathGeometry.AddGeometry(ellipse);
        }

        // 杭断面ジオメトリの追加メソッド
        private void AddPileSectionGeometry(Point point1, Point point2, double pileDia2D, double flattening)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            var ellipse1 = new EllipseGeometry(point1, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            var ellipse2 = new EllipseGeometry(point2, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            path.AddGeometry(ellipse1);
            path.AddGeometry(ellipse2);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(point1.X + pileDia2D * 0.5 * j, point1.Y),
                    new Point(point2.X + pileDia2D * 0.5 * j, point2.Y)
                );
                path.AddGeometry(lineGeometry);
            }
        }
        //private void AddPileSectionGeometry(Point point1, Point point2, double pileDia2D, double flattening)
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;
        //    var ellipse1 = new EllipseGeometry(new Point(point1.X, point1.Y), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
        //    var ellipse2 = new EllipseGeometry(new Point(point2.X, point2.Y), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

        //    if (viewModel.IsElementSplit)
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse1);
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse2);
        //    }
        //    else
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse1);
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse2);
        //    }

        //    for (int j = -1; j <= 1; j += 2)
        //    {
        //        var lineGeometry = new LineGeometry
        //        {
        //            StartPoint = new Point(point1.X + pileDia2D * 0.5 * j, point1.Y),
        //            EndPoint = new Point(point2.X + pileDia2D * 0.5 * j, point2.Y)
        //        };


        //        if (viewModel.IsElementSplit)
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(lineGeometry);
        //        }
        //        else
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(lineGeometry);
        //        }
        //    }
        //}

        // 杭先端ジオメトリの追加メソッド
        private void AddInsituPileToeGeometry(
            Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments,
            double insituPileToeAngle, double insituPileToeHeight)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double factoredToeCylinderHeight2D = Math.Cos(phiRad) * insituPileToeHeight * viewModel.CanvasThreeDView.Scale;
            double coneHeight = (pileToeDia - pileDia) * 0.5 / Math.Tan(insituPileToeAngle * Math.PI / 180);
            double factoredConeHeight2D = Math.Cos(phiRad) * coneHeight * viewModel.CanvasThreeDView.Scale;

            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipse3 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredConeHeight2D - factoredToeCylinderHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipse3);

            var coneGeneratrixes = GetConeGeneratrixes(
                new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D),
                pileToeDia2D * 0.5, pileDia2D * 0.5, factoredConeHeight2D, flattening);
            foreach (var lineGeometryConeGeneratrix in coneGeneratrixes)
            {
                path.AddGeometry(lineGeometryConeGeneratrix);
            }

            for (int sign = -1; sign <= 1; sign += 2)
            {
                Point start = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y);
                Point end = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y - factoredToeCylinderHeight2D);
                AddLineGeometry(start, end, path);
            }
        }
        //private void AddInsituPileToeGeometry(
        //    Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments,
        //    double insituPileToeAngle, double insituPileToeHeight)
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;

        //    double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
        //    var ellipse0 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);

        //    if (viewModel.IsElementSplit)
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse0);
        //    }
        //    else
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse0);
        //    }


        //    double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;

        //    double factoredToeCylinderHeight2D = Math.Cos(Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0) * insituPileToeHeight * viewModel.CanvasThreeDView.Scale;
        //    double coneHeight = (pileToeDia - pileDia) * 0.5 / Math.Tan(insituPileToeAngle * Math.PI / 180);
        //    double factoredConeHeight2D = Math.Cos(Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0) * coneHeight * viewModel.CanvasThreeDView.Scale;

        //    var ellipse3 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredConeHeight2D - factoredToeCylinderHeight2D),
        //        pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

        //    var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D),
        //        pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);

        //    var ellipseBtm = new EllipseGeometry(pointBtm,
        //        pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);


        //    if (viewModel.IsElementSplit)
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse3);
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipseTop);
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipseBtm);
        //    }
        //    else
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse3);
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipseTop);
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipseBtm);
        //    }


        //    var coneGeneratrixes = GetConeGeneratrixes(new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D),
        //        pileToeDia2D * 0.5, pileDia2D * 0.5, factoredConeHeight2D, flattening);
        //    foreach (var lineGeometryConeGeneratrix in coneGeneratrixes)
        //    {
        //        if (viewModel.IsElementSplit)
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(lineGeometryConeGeneratrix);
        //        }
        //        else
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(lineGeometryConeGeneratrix);
        //        }
        //    }

        //    for (int sign = -1; sign <= 1; sign += 2)
        //    {
        //        Point start = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y);
        //        Point end = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y - factoredToeCylinderHeight2D);

        //        if (viewModel.IsElementSplit)
        //        {
        //            AddLineGeometry(start, end, viewModel.CanvasGeometry.PathGeoPileDividedDias);
        //        }
        //        else
        //        {
        //            AddLineGeometry(start, end, viewModel.CanvasGeometry.PathGeoPileDias);
        //        }
        //    }
        //}


        // 杭先端ジオメトリの追加メソッド
        private void AddConcretePrecastPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double height = pileToeDia * 2.0;
            double factoredHeight2D = Math.Cos(phiRad) * height * viewModel.CanvasThreeDView.Scale;

            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseCore = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipseCore);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D),
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y)
                );
                path.AddGeometry(lineGeometry);
            }
        }
        //private void AddConcretePrecastPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments)
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;

        //    double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
        //    var ellipse0 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);

        //    if (viewModel.IsElementSplit)
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse0);
        //    }
        //    else
        //    {
        //        viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse0);
        //    }

        //    double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
        //    // 拡大球根
        //    {
        //        double height = pileToeDia * 2.0;
        //        double factoredHeight2D = Math.Cos(Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0) * height * viewModel.CanvasThreeDView.Scale;

        //        var ellipse3 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
        //        var ellipse5 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);



        //        if (viewModel.IsElementSplit)
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse3);
        //            viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(ellipse5);
        //        }
        //        else
        //        {
        //            viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse3);
        //            viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(ellipse5);
        //        }

        //        for (int j = -1; j <= 1; j += 2)
        //        {
        //            var lineGeometry = new LineGeometry
        //            {
        //                StartPoint = new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D),
        //                EndPoint = new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y)
        //            };
        //            if (viewModel.IsElementSplit)
        //            {
        //                viewModel.CanvasGeometry.PathGeoPileDividedDias.AddGeometry(lineGeometry);
        //            }
        //            else
        //            {
        //                viewModel.CanvasGeometry.PathGeoPileDias.AddGeometry(lineGeometry);
        //            }
        //        }
        //    }
        //}

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
            double dia = markerDiameter ?? acturalNodeSize;
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

                    EllipseGeometry ellipse = new(new Point(coord.X, coord.Y), acturalNodeSize * 1, acturalNodeSize * 1);
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
        private string GetLabelText(PileLayoutDataItem pilelocation)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            string label = string.Empty;

            if (viewModel.IsPileRefVisible) label += pilelocation.PileBodyNo.ToString() + ", ";
            if (viewModel.IsSoilRefVisible) label += pilelocation.GroundNo.ToString() + ", ";
            if (viewModel.IsPileTopLevelVisible) label += pilelocation.Point3D.Z.ToString("N3") + ", ";
            if (viewModel.IsGroupPileFactorLabelVisible) label += pilelocation.GroupPileFactor.ToString("N3") + ", ";
            if (viewModel.IsPileDiaSpacingRatioLabelVisible) label += pilelocation.PileSpacingFactor.ToString("N3") + ", ";
            if (viewModel.IsFrontPileLabelVisible)
            {
                var selectedLoadCase = LoadCases.GetLoadCase(viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);

                if (selectedLoadCase.Level == 1)
                {
                    var loadCases1 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1;
                    for (int i = 0; i < loadCases1.Count; i++)
                    {
                        if (loadCases1[i].LoadName == selectedLoadCase.LoadName)
                        {
                            label += pilelocation.IsFrontPiles[i] == true ? "前, " : "後, ";
                            break;
                        }
                    }
                }
                else if (selectedLoadCase.Level == 2)
                {
                    var loadCases2 = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2;
                    for (int i = 0; i < loadCases2.Count; i++)
                    {
                        if (loadCases2[i].LoadName == selectedLoadCase.LoadName)
                        {
                            label += pilelocation.IsFrontPiles[i] == true ? "前, " : "後, ";
                            break;
                        }
                    }
                }
            }
            if (label.Length > 0)
            {
                label = label[..^2]; // 最後のカンマとスペースを削除
            }
            return label;
        }

        // 沈下検討用土層描画メソッド
        private void UpdateSettlementGround3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double loadingPlaneAltitude = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

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

        // 共通メソッド
        private void DrawTickMarks(
            int minIndex, int maxIndex, double tickSpacing,
            Func<int, double> getValue,
            Func<double, Point3D> get3DLocation,
            Func<Point3D, Point> to2D,
            Action<Point, string> drawTick)
        {
            for (int i = minIndex; i <= maxIndex; i++)
            {
                double value = getValue(i);
                Point3D loc = get3DLocation(value);
                Point coord = to2D(loc);
                if (0 < coord.X && coord.X < Canvas3DWidth && 0 < coord.Y && coord.Y < Canvas3DHeight)
                {
                    drawTick(coord, $"{value}m");
                }
            }
        }

        private void UpdateTickMarks3DElevation()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                z => new Point3D(0, 0, z),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkY(coord.Y, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DYofYZ()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                y => new Point3D(0, y, viewModel.CurrentInputModel.FundamentalInput.Z0),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkX(coord.X, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DXofXZ()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                x => new Point3D(x, 0, viewModel.CurrentInputModel.FundamentalInput.Z0),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkX(coord.X, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DPlan()
        {
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            for (int i = -1000 / (int)tickSpacing; i <= 1000 / (int)tickSpacing; i++)
            {
                double tickValue = i * tickSpacing;
                DrawTickMarksForValue(tickValue, tickLength, textPos, solidColorBrush);
            }
        }


        private void AddTickMarkY(double y, double tickLength, double textPos, SolidColorBrush brush, string text)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            LineGeometry lineGeometry = new()
            {
                StartPoint = new(Canvas3DWidth - tickLength, y),
                EndPoint = new(Canvas3DWidth, y)
            };

            viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);
            AddText3D(brush, text, Canvas3DWidth, y, "R", "B", 0.0);
        }

        // チェックマーク描画メソッド
        private void AddTickMarkX(double x, double tickLength, double textPos, SolidColorBrush brush, string text)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            LineGeometry lineGeometry = new()
            {
                StartPoint = new(x, Canvas3DHeight - tickLength),
                EndPoint = new(x, Canvas3DHeight)
            };

            viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);
            AddText3D(brush, text, x, Canvas3DHeight - textPos, "R", "B", -90);
        }

        // 指定された値のTickMarkを描画するメソッド
        private void DrawTickMarksForValue(
            double tickValue, double tickLength, double textPos, SolidColorBrush solidColorBrush
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            Point3D[] locs =
            [
                new Point3D(tickValue, 0, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(tickValue, 10, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(0, tickValue, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(10, tickValue, viewModel.CurrentInputModel.FundamentalInput.Z0)
            ];

            Point[] coords = [.. locs.Select(loc => viewModel.CanvasThreeDView.Transformation(loc))];

            AddTickMark3DPlan(coords[0], coords[1], tickLength, textPos, solidColorBrush, tickValue, true);
            AddTickMark3DPlan(coords[2], coords[3], tickLength, textPos, solidColorBrush, tickValue, false);
        }

        // TickMarkを追加するメソッド
        private void AddTickMark3DPlan(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (coord1.Y != coord2.Y)
            {
                AddVerticalTickMark(coord1, coord2, tickLength, textPos, brush, tickValue, isXAxis);
            }

            if (coord1.X != coord2.X)
            {
                AddHorizontalTickMark(coord1, coord2, tickLength, textPos, brush, tickValue, isXAxis);
            }
        }

        // 垂直TickMarkを追加するメソッド
        private void AddVerticalTickMark(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double xx1 = CalculateTickPosition(coord1, coord2, Canvas3DHeight - tickLength, 'Y');
            double xx2 = CalculateTickPosition(coord1, coord2, Canvas3DHeight, 'Y');

            if (IsInCanvasWidth(xx1))
            {
                LineGeometry lineGeometry = new()
                {
                    StartPoint = new(xx1, Canvas3DHeight - tickLength),
                    EndPoint = new(xx2, Canvas3DHeight)
                };
                viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);

                AddText3D(brush, $"{tickValue:F1}", xx2, Canvas3DHeight - textPos, "R", "B", isXAxis ? -90.0 : 0.0);
            }
        }

        // 水平TickMarkを追加するメソッド
        private void AddHorizontalTickMark(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double yy1 = CalculateTickPosition(coord1, coord2, Canvas3DWidth - tickLength, 'X');
            double yy2 = CalculateTickPosition(coord1, coord2, Canvas3DWidth, 'X');

            if (IsInCanvasHeight(yy1))
            {
                LineGeometry lineGeometry = new()
                {
                    StartPoint = new(Canvas3DWidth - tickLength, yy1),
                    EndPoint = new(Canvas3DWidth, yy2)
                };
                viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);

                AddText3D(brush, $"{tickValue:F1}", Canvas3DWidth, yy2, "R", "B", isXAxis ? -90.0 : 0.0);
            }
        }

        // TickMarkの位置を計算するメソッド
        private double CalculateTickPosition(Point coord1, Point coord2, double targetValue, char axis)
        {
            double delta;
            if (axis == 'Y')
            {
                delta = coord2.Y - coord1.Y;
                return (targetValue - coord1.Y) * (coord2.X - coord1.X) / delta + coord1.X;
            }
            else // ;
            {
                delta = coord2.X - coord1.X;
                return (targetValue - coord1.X) * (coord2.Y - coord1.Y) / delta + coord1.Y;
            }
        }

        // TickMarkがキャンバスの幅に収まっているかチェックするメソッド
        private bool IsInCanvasWidth(double x)
        {
            return 0 < x && x < Canvas3DWidth;
        }

        // TickMarkがキャンバスの高さに収まっているかチェックするメソッド
        private bool IsInCanvasHeight(double y)
        {
            return 0 < y && y < Canvas3DHeight;
        }

        private void UpdateGridLinesAndDimensionsXforXZ()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            //CurrentInputModel inputModel = CurrentInputModel.Instance;

            SolidColorBrush solidColorBrush = Brushes.Purple;
            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                {
                    if (0 < coord.X && coord.X < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, 0),
                            EndPoint = new(coord.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridX.Name, coord.X, Canvas3DHeight - SymbolPos, "C", "C", 0.0);

                    }
                }
            }

            if (DataContext == null) { return; }

            bool first = true;

            if (viewModel.CurrentInputModel.GridXItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridXItems.Count; i++)
                {
                    GridDataItem gridX = viewModel.CurrentInputModel.GridXItems[i];
                    Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), acturalTickPointSize * 0.5, acturalTickPointSize * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridX0 = viewModel.CurrentInputModel.GridXItems[i - 1];
                        Point3D loc0 = new(gridX0.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridXItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }
        }

        private void UpdateGridLinesAndDimensionsYforYZ()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            SolidColorBrush solidColorBrush = Brushes.Purple;
            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                {
                    if (0 < coord.X && coord.X < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, 0),
                            EndPoint = new(coord.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridY.Name, coord.X, Canvas3DHeight - SymbolPos, "C", "C", 0.0);
                    }
                }
            }

            if (DataContext == null) { return; }

            bool first = true;

            if (viewModel.CurrentInputModel.GridYItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridYItems.Count; i++)
                {
                    GridDataItem gridY = viewModel.CurrentInputModel.GridYItems[i];
                    Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridY0 = viewModel.CurrentInputModel.GridYItems[i - 1];
                        Point3D loc0 = new(0, gridY0.Coord, 0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridYItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }
        }


        private void UpdateGridLines3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel == null) return;
            if (viewModel.CanvasThreeDView == null) return;

            var gridXItems = viewModel.CurrentInputModel.GridXItems;
            var gridYItems = viewModel.CurrentInputModel.GridYItems;
            if (gridXItems == null || gridYItems == null) return;
            if (gridXItems.Count == 0 && gridYItems.Count == 0) return;

            SolidColorBrush solidColorBrush = Brushes.Purple;

            double lineEndPosTickSide = viewModel.GridSymbolZoneWidth;
            double lineEndPos = viewModel.GridSymbolZoneWidth;
            double gridSymbolPosTickSide = viewModel.GridSymbolZoneWidth * 0.5;
            double gridSymbolPos = viewModel.GridSymbolZoneWidth * 0.5;
            double gridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            if (viewModel.CanvasThreeDView.Phi == 90 && viewModel.IsTickMarkVisible)
            {
                lineEndPosTickSide += viewModel.TickZoneWidth;
                gridSymbolPosTickSide += viewModel.TickZoneWidth;
            }

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc1 = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(gridX.Coord, 10, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                DrawGridLineAndSymbol(
                    coord1, coord2,
                    lineEndPos, lineEndPosTickSide,
                    gridSymbolPos, gridSymbolPosTickSide,
                    gridSymbolCircleDia, solidColorBrush, gridX.Name, flattening);
            }

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc1 = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(10, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                DrawGridLineAndSymbol(
                    coord1, coord2,
                    lineEndPos, lineEndPosTickSide,
                    gridSymbolPos, gridSymbolPosTickSide,
                    gridSymbolCircleDia, solidColorBrush, gridY.Name, flattening);
            }
        }

        private void DrawGridLineAndSymbol(
            Point coord1, Point coord2,
            double lineEndPos, double lineEndPosTickSide,
            double gridSymbolPos, double gridSymbolPosTickSide,
            double gridSymbolCircleDia, SolidColorBrush solidColorBrush,
            string name, double flattening)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 長方形の境界
            double minX = lineEndPos;
            double maxX = Canvas3DWidth - lineEndPosTickSide;
            double minY = lineEndPos;
            double maxY = Canvas3DHeight - lineEndPosTickSide;

            // 長方形の境界
            double minXGS = gridSymbolPos;
            double maxXGS = Canvas3DWidth - gridSymbolPosTickSide;
            double minYGS = gridSymbolPos;
            double maxYGS = Canvas3DHeight - gridSymbolPosTickSide;

            // 線分の端点が長方形の外にある場合、クリッピングする
            List<Point> intersections = [];
            List<Point> intersectionsGS = [];


            // 左右辺
            if (!coord1.X.Equals(coord2.X))
            {
                double yL = (minX - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minY <= yL && yL <= maxY) intersections.Add(new Point(minX, yL));

                double yR = (maxX - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minY <= yR && yR <= maxY) intersections.Add(new Point(maxX, yR));

                double yLGS = (minXGS - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minYGS <= yLGS && yLGS <= maxYGS) intersectionsGS.Add(new Point(minXGS, yLGS));

                double yRGS = (maxXGS - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minYGS <= yRGS && yRGS <= maxYGS) intersectionsGS.Add(new Point(maxXGS, yRGS));
            }

            // 上下辺
            if (!coord1.Y.Equals(coord2.Y))
            {
                double xT = (minY - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minX <= xT && xT <= maxX) intersections.Add(new Point(xT, minY));

                double xB = (maxY - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minX <= xB && xB <= maxX) intersections.Add(new Point(xB, maxY));

                double xTGS = (minYGS - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minXGS <= xTGS && xTGS <= maxXGS) intersectionsGS.Add(new Point(xTGS, minYGS));

                double xBGS = (maxYGS - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minXGS <= xBGS && xBGS <= maxXGS) intersectionsGS.Add(new Point(xBGS, maxYGS));
            }

            // 必要条件を満たさない場合は描画しない（落ちないように）
            if (intersections.Count != 2 || intersectionsGS.Count != 2) return;

            var lineGeometry = new LineGeometry
            {
                StartPoint = intersections[0],
                EndPoint = intersections[1]
            };
            viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

            var ellipse1 = new EllipseGeometry(intersectionsGS[0], gridSymbolCircleDia * 0.5, gridSymbolCircleDia * 0.5 * flattening);
            viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse1);

            var ellipse2 = new EllipseGeometry(intersectionsGS[1], gridSymbolCircleDia * 0.5, gridSymbolCircleDia * 0.5 * flattening);
            viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse2);

            AddText3D(solidColorBrush, name ?? string.Empty, intersectionsGS[0].X, intersectionsGS[0].Y, "C", "C", 0.0, flattening);
            AddText3D(solidColorBrush, name ?? string.Empty, intersectionsGS[1].X, intersectionsGS[1].Y, "C", "C", 0.0, flattening);
        }


        // 通り心描画メソッド
        private void UpdateGridLines3DPlan()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            SolidColorBrush solidColorBrush = Brushes.Purple;

            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;
            double flattening = viewModel.CanvasThreeDView.Flattening;

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc1 = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(gridX.Coord, 10, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                // canvas x軸との交点
                if (coord1.Y != coord2.Y)
                {
                    double xx1 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (0 - coord1.Y) + coord1.X;
                    double xx2 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - LineEndPos - coord1.Y) + coord1.X;
                    double xxSymbol = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - SymbolPos - coord1.Y) + coord1.X;
                    if (0 < xxSymbol && xxSymbol < Canvas3DWidth)
                    //if (0 < xx1 && xx1 < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(xx1, 0),
                            EndPoint = new(xx2, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(xxSymbol, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridX.Name, xxSymbol, Canvas3DHeight - SymbolPos, "C", "C", 0.0, flattening);
                    }
                }

                // canvas y軸との交点
                if (coord1.X != coord2.X)
                {
                    double yy1 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (0 - coord1.X) + coord1.Y;
                    double yy2 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - LineEndPos - coord1.X) + coord1.Y;
                    double yySymbol = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - SymbolPos - coord1.X) + coord1.Y;
                    if (0 < yySymbol && yySymbol < Canvas3DHeight)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(0, yy1),
                            EndPoint = new(Canvas3DWidth - LineEndPos, yy2)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(Canvas3DWidth - SymbolPos, yySymbol), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridX.Name, Canvas3DWidth - SymbolPos, yySymbol, "C", "C", 0.0, flattening);
                    }
                }
            }

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc1 = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(10, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                // canvas x軸との交点
                if (coord1.Y != coord2.Y)
                {
                    double xx1 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (0 - coord1.Y) + coord1.X;
                    double xx2 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - LineEndPos - coord1.Y) + coord1.X;
                    double xxSymbol = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - SymbolPos - coord1.Y) + coord1.X;

                    if (0 < xxSymbol && xxSymbol < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(xx1, 0),
                            EndPoint = new(xx2, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(xxSymbol, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridY.Name, xxSymbol, Canvas3DHeight - SymbolPos, "C", "C", 0.0, flattening);
                    }
                }

                // canvas y軸との交点
                if (coord1.X != coord2.X)
                {
                    double yy1 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (0 - coord1.X) + coord1.Y;
                    double yy2 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - LineEndPos - coord1.X) + coord1.Y;
                    double yySymbol = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - SymbolPos - coord1.X) + coord1.Y;

                    if (0 < yySymbol && yySymbol < Canvas3DHeight)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(0, yy1),
                            EndPoint = new(Canvas3DWidth - LineEndPos, yy2)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(Canvas3DWidth - SymbolPos, yySymbol), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridY.Name, Canvas3DWidth - SymbolPos, yySymbol, "C", "C", 0.0, flattening);
                    }
                }
            }
        }

        // 寸法線描画メソッド
        private void UpdateDimensionLines3DPlan()
        {
            if (DataContext == null) { return; }
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;

            bool first = true;

            if (viewModel.CurrentInputModel.GridXItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridXItems.Count; i++)
                {
                    GridDataItem gridX = viewModel.CurrentInputModel.GridXItems[i];

                    Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridX0 = viewModel.CurrentInputModel.GridXItems[i - 1];
                        Point3D loc0 = new(gridX0.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridXItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }


            if (viewModel.CurrentInputModel.GridYItems != null)
            {
                first = true;
                for (int i = 0; i < viewModel.CurrentInputModel.GridYItems.Count; i++)
                {
                    GridDataItem gridY = viewModel.CurrentInputModel.GridYItems[i];

                    Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(Canvas3DWidth - LineEndPos, coord.Y), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridY0 = viewModel.CurrentInputModel.GridYItems[i - 1];
                        Point3D loc0 = new(0, gridY0.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(Canvas3DWidth - LineEndPos, coord.Y),
                            EndPoint = new(Canvas3DWidth - LineEndPos, coord0.Y)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasYpos = (coord.Y + coord0.Y) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridYItems[i].Spacing * 1000).ToString();

                        AddText3D(solidColorBrush, spacing, Canvas3DWidth - LineEndPos, canvasYpos, "C", "B", -90);
                    }
                }
            }
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
            UpdateCanvas3D();
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
                UpdateCanvas3D();
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
                    UpdateCanvas3D();
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
            UpdateCanvas3D();
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