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
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Node = PileDesign.FEM.Node;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {
        //// 解析結果更新（分岐オーケストレーションのみ）
        //private void UpdateAnalysisResult3D()
        //{


        //    if (DataContext is not MainWindowViewModel vm) return;
        //    ColorBarCanvas.Children.Clear();


        //    switch (vm.AnalysisResultContent)
        //    {
        //        case "梁応力":
        //            PrepareUiForBeamForces(vm);
        //            DrawBeamForcesResult3D(vm);
        //            break;

        //        case "節点変位":
        //            PrepareUiForNodeDisplacements(vm);
        //            DrawNodeDisplacementsResult3D(vm);
        //            break;

        //        case "地盤ばね":
        //            PrepareUiForSoilSprings(vm);
        //            DrawSoilSpringsResult3D(vm);
        //            break;

        //        case "沈下":
        //            PrepareUiForSettlement(vm);
        //            DrawSettlementResult3D(vm);
        //            break;
        //    }

        //    vm.CanvasGeometry.DrawAllPaths(Canvas3DLayout, vm.PileStrokeThickness, vm.SoilStrokeThickness);
        //    RenderTextBlocksWithDrawingVisual();
        //}


        //// UIの補助切替（例）
        //private static void PrepareUiForBeamForces(MainWindowViewModel vm)
        //{
        //    //vm.IsPileOptionVisible = true;
        //    //vm.IsLoadCaseOptionVisible = true;
        //    //vm.IsLoadCombinationOptionVisible = true;
        //    //vm.IsLiquefactionOptionVisible = true;
        //}

        //private static void PrepareUiForNodeDisplacements(MainWindowViewModel vm)
        //{
        //    //vm.IsPileOptionVisible = true;
        //    //vm.IsLoadCaseOptionVisible = true;
        //    //vm.IsLoadCombinationOptionVisible = true;
        //    //vm.IsLiquefactionOptionVisible = true;
        //}

        //private static void PrepareUiForSoilSprings(MainWindowViewModel vm)
        //{
        //    //vm.IsPileOptionVisible = true;
        //    //vm.IsLoadCaseOptionVisible = true;
        //    //vm.IsLoadCombinationOptionVisible = true;
        //    //vm.IsLiquefactionOptionVisible = true;
        //}

        //private static void PrepareUiForSettlement(MainWindowViewModel vm)
        //{
        //    //vm.IsLoadCaseOptionVisible = true;
        //    //vm.IsGridOptionVisible = true;
        //    //vm.IsLiquefactionOptionVisible = false;
        //    //vm.IsPileOptionVisible = false;
        //}

        //// 沈下（重複分岐整理 + values 未追加修正）
        //private void DrawSettlementResult3D(MainWindowViewModel vm)
        //{
        //    string title = $"{vm.AnalysisResultContent}|{vm.AnalysisResultSettlementType}|{vm.SelectedLoadCaseName}";
        //    string unit = "mm";
        //    var points = new ObservableCollection<Point3D>();
        //    var values = new ObservableCollection<double>();

        //    double loadingPlaneAlt = vm.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

        //    switch (vm.AnalysisResultSettlementType)
        //    {
        //        case "単杭":
        //            foreach (var p in vm.CurrentInputModel.PileLayoutItems)
        //            {
        //                if (vm.SelectedLoadCaseName == "VL")
        //                {
        //                    values.Add(p.SinglePileSettlementVL);
        //                    points.Add(new Point3D(p.Point3D.X, p.Point3D.Y, loadingPlaneAlt));
        //                }
        //                else
        //                {
        //                    AddSettlementIfMatchLoadCase(vm, p, loadingPlaneAlt, values, points);
        //                }
        //            }
        //            break;

        //        case "群杭":
        //            foreach (var p in vm.CurrentInputModel.PileLayoutItems)
        //            {
        //                values.Add(p.GroupPileSettlement); // 修正: 値追加
        //                points.Add(new Point3D(p.Point3D.X, p.Point3D.Y, loadingPlaneAlt));
        //            }
        //            break;

        //        case "単杭+群杭":
        //            foreach (var p in vm.CurrentInputModel.PileLayoutItems)
        //            {
        //                if (vm.SelectedLoadCaseName == "VL")
        //                {
        //                    values.Add(p.SinglePileSettlementVL + p.GroupPileSettlement);
        //                    points.Add(new Point3D(p.Point3D.X, p.Point3D.Y, loadingPlaneAlt));
        //                }
        //                else
        //                {
        //                    AddSettlementIfMatchLoadCase(vm, p, loadingPlaneAlt, values, points, addGroup: true);
        //                }
        //            }
        //            break;
        //    }

        //    _pendingSettlementPoints = points;
        //    _pendingSettlementValues = values;
        //    _pendingSettlementTitle = title;
        //    _pendingSettlementUnit = unit;
        //}

        //// 単杭/単杭+群杭用 LoadCase 一致時の沈下値追加ヘルパ
        //private static void AddSettlementIfMatchLoadCase(
        //    MainWindowViewModel vm,
        //    PileLayoutDataItem pile,
        //    double loadingPlaneAlt,
        //    ObservableCollection<double> values,
        //    ObservableCollection<Point3D> points,
        //    bool addGroup = false)
        //{
        //    // Level1
        //    for (int i = 0; i < vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
        //    {
        //        var lc = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
        //        if (vm.SelectedLoadCaseName == lc.LoadName)
        //        {
        //            values.Add(pile.SinglePileSettlementLevel1s[i] + (addGroup ? pile.GroupPileSettlement : 0.0));
        //            points.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, loadingPlaneAlt));
        //        }
        //    }
        //    // Level2
        //    for (int i = 0; i < vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
        //    {
        //        var lc = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
        //        if (vm.SelectedLoadCaseName == lc.LoadName)
        //        {
        //            values.Add(pile.SinglePileSettlementLevel2s[i] + (addGroup ? pile.GroupPileSettlement : 0.0));
        //            points.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, loadingPlaneAlt));
        //        }
        //    }
        //}
        //// 沈下
        ////private void DrawSettlementResult3D(MainWindowViewModel viewModel)
        ////{
        ////    string title = $"{viewModel.AnalysisResultContent}|{viewModel.AnalysisResultSettlementType}|{viewModel.SelectedLoadCaseName}";
        ////    string unit = "mm";
        ////    ObservableCollection<Point3D> points = [];
        ////    ObservableCollection<double> values = [];

        ////    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

        ////    if (viewModel.AnalysisResultSettlementType == "単杭")
        ////    {
        ////        foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
        ////        {
        ////            if (viewModel.SelectedLoadCaseName == "VL")
        ////            {
        ////                // 単杭沈下は mm をそのまま使用
        ////                values.Add(pileLocation.SinglePileSettlementVL);
        ////                points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////            }
        ////            else
        ////            {
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
        ////                {
        ////                    LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel1s[i]); // mm
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
        ////                {
        ////                    LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel2s[i]); // mm
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////            }
        ////        }
        ////    }
        ////    if (viewModel.AnalysisResultSettlementType == "群杭")
        ////    {
        ////        foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
        ////            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////    }

        ////    else if (viewModel.AnalysisResultSettlementType == "単杭+群杭")
        ////    {
        ////        foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
        ////        {
        ////            if (viewModel.SelectedLoadCaseName == "VL")
        ////            {
        ////                values.Add(pileLocation.SinglePileSettlementVL + pileLocation.GroupPileSettlement); // mm
        ////                points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////            }
        ////            else
        ////            {
        ////                // L1/L2どちらかに一致したら追加
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
        ////                {
        ////                    var loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel1s[i]);
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
        ////                {
        ////                    var loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel2s[i]);
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////            }
        ////        }
        ////    }
        ////    else if (viewModel.AnalysisResultSettlementType == "群杭")
        ////    {
        ////        // 修正: 群杭でも values を追加（従来欠落）
        ////        foreach (var pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
        ////        {
        ////            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////            values.Add(pileLocation.GroupPileSettlement);
        ////        }
        ////    }
        ////    else if (viewModel.AnalysisResultSettlementType == "単杭+群杭")
        ////    {
        ////        foreach (var pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
        ////        {
        ////            if (viewModel.SelectedLoadCaseName == "VL")
        ////            {
        ////                values.Add(pileLocation.SinglePileSettlementVL + pileLocation.GroupPileSettlement);
        ////                points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////            }
        ////            else
        ////            {
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
        ////                {
        ////                    var loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel1s[i] + pileLocation.GroupPileSettlement);
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////                for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
        ////                {
        ////                    var loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
        ////                    if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
        ////                    {
        ////                        values.Add(pileLocation.SinglePileSettlementLevel2s[i] + pileLocation.GroupPileSettlement);
        ////                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
        ////                    }
        ////                }
        ////            }
        ////        }
        ////    }
        ////    _pendingSettlementPoints = points;
        ////    _pendingSettlementValues = values;
        ////    _pendingSettlementTitle = title;
        ////    _pendingSettlementUnit = unit;
        ////}

        //// 梁応力
        //private void DrawBeamForcesResult3D(MainWindowViewModel viewModel)
        //{

        //    // 派生表示フラグ（Mh / Fh）
        //    bool isDerivedMagnitude = false;
        //    string derivedMagnitudeType = string.Empty;
        //    var anaModel = viewModel.CurrentModel;
        //    if (anaModel == null || anaModel.Beams == null)
        //        return;

        //    // インデックスと方向ベクトルの決定
        //    int[] indices;
        //    Vector<double> forceDirection;
        //    string unit;

        //    switch (viewModel.AnalysisResultBeamForceType)
        //    {
        //        case "Fx":
        //            indices = [0, 6];
        //            forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
        //            unit = "kN";
        //            break;
        //        case "Fy":
        //            indices = [1, 7];
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]);
        //            unit = "kN";
        //            break;
        //        case "Fz":
        //            indices = [2, 8];
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
        //            unit = "kN";
        //            break;
        //        case "Mx":
        //            indices = [3, 9];
        //            forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
        //            unit = "kNm";
        //            break;
        //        case "My":
        //            indices = [4, 10];
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
        //            unit = "kNm";
        //            break;
        //        case "Mz":
        //            indices = [5, 11];
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]);
        //            unit = "kNm";
        //            break;

        //        // 曲げ合成モーメント表示 Mh = sqrt(My^2 + Mz^2)
        //        case "Mh":
        //            indices = [3, 9]; // placeholder index（大きさは下で直接算出）
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); // デフォルト方向（Y優先）
        //            unit = "kNm";
        //            isDerivedMagnitude = true;
        //            derivedMagnitudeType = "Mh";
        //            break;

        //        // 水平力合成 Fh = sqrt(Fy^2 + Fz^2)
        //        case "Fh":
        //            indices = [0, 6];
        //            forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); // デフォルト方向（Y優先）
        //            unit = "kN";
        //            isDerivedMagnitude = true;
        //            derivedMagnitudeType = "Fh";
        //            break;
        //        default:
        //            return;
        //    }

        //    var selectedLoadCase = LoadCases.GetLoadCase(
        //        viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
        //    if (selectedLoadCase == null) return;

        //    var selectedLoadCombination = LoadCombinations.GetLoadCombination(
        //        viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
        //    if (selectedLoadCombination == null) return;

        //    // 1回のループで最大値と描画を行う
        //    double maxAbsValue = 0;
        //    var beamResults = new List<(Beam beam, double forceI, double forceJ, double originalForceI, double originalForceJ)>();

        //    ObservableCollection<double> allValues = [];

        //    foreach (var beam in anaModel.Beams)
        //    {
        //        var beamResult = beam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //        if (beamResult == null) continue;

        //        double originalForceI;
        //        double originalForceJ;

        //        if (isDerivedMagnitude)
        //        {
        //            if (derivedMagnitudeType == "Mh")
        //            {
        //                double MyI = beamResult.CumulativeForce.GetByIndex(4);
        //                double MzI = beamResult.CumulativeForce.GetByIndex(5);
        //                double MyJ = beamResult.CumulativeForce.GetByIndex(10);
        //                double MzJ = beamResult.CumulativeForce.GetByIndex(11);
        //                originalForceI = Math.Sqrt(MyI * MyI + MzI * MzI);
        //                originalForceJ = Math.Sqrt(MyJ * MyJ + MzJ * MzJ);
        //            }
        //            else // "Fh"
        //            {
        //                double FyI = beamResult.CumulativeForce.GetByIndex(1);
        //                double FzI = beamResult.CumulativeForce.GetByIndex(2);
        //                double FyJ = beamResult.CumulativeForce.GetByIndex(7);
        //                double FzJ = beamResult.CumulativeForce.GetByIndex(8);
        //                originalForceI = Math.Sqrt(FyI * FyI + FzI * FzI);
        //                originalForceJ = Math.Sqrt(FyJ * FyJ + FzJ * FzJ);
        //            }
        //        }
        //        else
        //        {
        //            originalForceI = beamResult.CumulativeForce.GetByIndex(indices[0]);
        //            originalForceJ = beamResult.CumulativeForce.GetByIndex(indices[1]);
        //        }

        //        double absForceI = Math.Abs(originalForceI);
        //        double absForceJ = Math.Abs(originalForceJ);

        //        // ここを絶対値追加から符号付き追加へ変更
        //        allValues.Add(originalForceI);
        //        allValues.Add(originalForceJ); // ← ここで J端を反転

        //        maxAbsValue = Math.Max(maxAbsValue, Math.Max(absForceI, absForceJ));
        //        beamResults.Add((beam, absForceI, absForceJ, originalForceI, originalForceJ));

        //        //beamResults.Add((beam, absForceI, absForceJ, originalForceI, originalForceJ));
        //        // 既存の beamResults に入れるtupleはそのままにしておく（必要に応じて表示側でabs/符号を使い分ける）
        //        //beamResults.Add((beam, Math.Abs(originalForceI), Math.Abs(originalForceJ), originalForceI, originalForceJ));
        //    }

        //    // カラーバー用ジオメトリを一度だけ生成（描画ループの前）
        //    //var colorBaredGeometries = GetColorBarGeometries(allValues);
        //    var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

        //    // 変換行列（要素局所系→表示系）
        //    Matrix<double> t = Utils.GetNodeTransformMatrix(new Vector3D(0, 0, -1));

        //    foreach (var (beam, _, _, originalForceI, originalForceJ) in beamResults)
        //    {
        //        var beamResult = beam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //        if (beamResult == null) continue;

        //        bool isMomentType = viewModel.AnalysisResultBeamForceType.StartsWith('M');
        //        string derivedTypeLocal = isDerivedMagnitude ? derivedMagnitudeType : string.Empty;

        //        // BeamForceExtensions の GetEnd3Vector を利用
        //        Vector<double> rawI = beamResult.CumulativeForce.GetEnd3Vector(isMomentType, true, derivedTypeLocal);
        //        Vector<double> rawJ = beamResult.CumulativeForce.GetEnd3Vector(isMomentType, false, derivedTypeLocal);

        //        Vector<double> dirI = rawI.L2Norm() > 1e-12 ? rawI / rawI.L2Norm() : forceDirection;
        //        Vector<double> dirJ = rawJ.L2Norm() > 1e-12 ? rawJ / rawJ.L2Norm() : forceDirection;

        //        if (!string.IsNullOrEmpty(derivedTypeLocal) && (derivedTypeLocal == "Fh" || derivedTypeLocal == "Mh"))
        //        {
        //            // 元の実装では I端のみ反転する挙動を維持
        //            dirI = -dirI;
        //        }

        //        var transformedForceDirectionI = t.Transpose() * dirI;
        //        var transformedForceDirectionJ = t.Transpose() * dirJ;

        //        // 描画位置は符号を保持した originalForce を使う（向きは変えない）
        //        double forceI = maxAbsValue == 0 ? 0 : originalForceI / maxAbsValue * viewModel.ForceDiagramMultiplier;
        //        double forceJ = maxAbsValue == 0 ? 0 : originalForceJ / maxAbsValue * viewModel.ForceDiagramMultiplier;

        //        Point3D nodeI3D = beam.NodeI.Coord;
        //        Point3D nodeIForce3D = new(
        //            nodeI3D.X + forceI * transformedForceDirectionI[0],
        //            nodeI3D.Y + forceI * transformedForceDirectionI[1],
        //            nodeI3D.Z + forceI * transformedForceDirectionI[2]);
        //        Point3D nodeJ3D = beam.NodeJ.Coord;
        //        Point3D nodeJForce3D = new(
        //            nodeJ3D.X + forceJ * transformedForceDirectionJ[0],
        //            nodeJ3D.Y + forceJ * transformedForceDirectionJ[1],
        //            nodeJ3D.Z + forceJ * transformedForceDirectionJ[2]);

        //        Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //        Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
        //        Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
        //        Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //        var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };

        //        // 元の描画挙動に合わせ、値配列は符号付き originalForce を渡す（テキスト表示も同じ値を使う）
        //        var polyValues = new List<double> { originalForceI, originalForceI, originalForceJ, originalForceJ };
        //        AddColorPolyLineAreaGeometry(points, polyValues, colorBaredGeometries);

        //        if (viewModel.IsResultValueVisible)
        //        {
        //            string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //            if (viewModel.IsPileTopResultValueVisibleOnly)
        //            {
        //                if (beam.IsPileTop)
        //                {
        //                    AddText3D(Brushes.Black, string.Format(format, originalForceI),
        //                        nodeIForce2D.X, nodeIForce2D.Y, "C", "C", 0.0);
        //                }
        //            }
        //            else
        //            {
        //                DrawResultValueTexts(
        //                    viewModel.IsResultValueVisible, Brushes.Black,
        //                    originalForceI, originalForceJ,
        //                    nodeIForce2D, nodeJForce2D,
        //                    nodeJ2D, nodeI2D,
        //                    format, format);
        //            }
        //        }
        //    }

        //    foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
        //    {
        //        colorBaredGeometry.DrawPathes(Canvas3DLayout);
        //    }

        //    if (allValues.Count > 0)
        //    {
        //        ColorBar.DrawStepColorBar(
        //            ColorBarCanvas,
        //            colorBaredGeometries,
        //            viewModel.AnalysisResultBeamForceType,
        //            unit,
        //            allValues.Min(),
        //            allValues.Max(),
        //            "{0:N" + viewModel.DecimalPlaces + "}",
        //            viewModel.LabelSize
        //        );
        //    }
        //    else
        //    {
        //        ColorBarCanvas.Children.Clear();
        //    }
        //}

        //// 節点変位
        //private void DrawNodeDisplacementsResult3D(MainWindowViewModel viewModel)
        //{

        //    var anaModel = viewModel.CurrentModel;
        //    if (anaModel == null || anaModel.Beams == null)
        //        return;

        //    string format = "{0:N" + viewModel.DecimalPlaces + "}";
        //    Vector<double> effectiveVector;
        //    double multiplier;
        //    bool isThetaLocal;
        //    switch (viewModel.AnalysisResultNodeDisplacementType)
        //    {
        //        case "UH":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 0, 0, 0, 0]);
        //            multiplier = 1000;
        //            isThetaLocal = false;
        //            break;
        //        case "UX":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([1, 0, 0, 0, 0, 0]);
        //            multiplier = 1000;
        //            isThetaLocal = false;
        //            break;
        //        case "UY":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 1, 0, 0, 0, 0]);
        //            multiplier = 1000;
        //            isThetaLocal = false;
        //            break;
        //        case "UZ":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 1, 0, 0, 0]);
        //            multiplier = 1000;
        //            isThetaLocal = false;
        //            break;
        //        case "θH":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 1, 0]);
        //            multiplier = 1;
        //            isThetaLocal = true;
        //            break;
        //        case "θX":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 0, 0]);
        //            multiplier = 1;
        //            isThetaLocal = true;
        //            break;
        //        case "θY":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 1, 0]);
        //            multiplier = 1;
        //            isThetaLocal = true;
        //            break;
        //        case "θZ":
        //            effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 0, 1]);
        //            multiplier = 1;
        //            isThetaLocal = true;
        //            break;
        //        default:
        //            return;
        //    }

        //    // 単位設定
        //    string unit = isThetaLocal ? "rad" : "mm";

        //    // DrawNodeDisplacementsResult3D の冒頭付近（unit の決定後あたり）
        //    double displayScale = viewModel.DisplacementDiagramMultiplier == 0.0 ? 1.0 : viewModel.DisplacementDiagramMultiplier;

        //    // 選択ケース/組合せ（既存）
        //    var selectedLoadCase = LoadCases.GetLoadCase(
        //        viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
        //    var selectedLoadCombination = LoadCombinations.GetLoadCombination(
        //        viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
        //    if (selectedLoadCase == null || selectedLoadCombination == null) return;

        //    // 1) 全節点（Dummy含むBeams端点）を一意に取り、必要な「表示値」を収集する
        //    var nodeSet = new HashSet<Node>();
        //    if (anaModel?.Nodes != null && anaModel.Nodes.Count > 0)
        //    {
        //        foreach (var n in anaModel.Nodes) if (n != null) nodeSet.Add(n);
        //    }
        //    else
        //    {
        //        if (anaModel?.Beams != null)
        //        {
        //            foreach (var b in anaModel.Beams)
        //            {
        //                if (b?.NodeI != null) nodeSet.Add(b.NodeI);
        //                if (b?.NodeJ != null) nodeSet.Add(b.NodeJ);
        //            }
        //        }
        //        if (anaModel?.DummyBeams != null)
        //        {
        //            foreach (var db in anaModel.DummyBeams)
        //            {
        //                if (db?.NodeI != null) nodeSet.Add(db.NodeI);
        //                if (db?.NodeJ != null) nodeSet.Add(db.NodeJ);
        //            }
        //        }
        //    }

        //    var allValues = new ObservableCollection<double>();
        //    // U系: ノルム（multiplier を掛けたユーザー表示値）
        //    // θ系: 回転量（rad 等）を multiplier（=1）・表示倍率でスケールしてカラーバーに使う
        //    foreach (var node in nodeSet)
        //    {
        //        if (node == null) continue;
        //        var nr = node.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //        if (nr == null) continue;
        //        var nd = nr.CumulativeDisp;
        //        // 値の抽出（effectiveVector に従う）
        //        double val = Math.Sqrt(
        //            Math.Pow(nd.Ux * effectiveVector[0], 2) +
        //            Math.Pow(nd.Uy * effectiveVector[1], 2) +
        //            Math.Pow(nd.Uz * effectiveVector[2], 2) +
        //            Math.Pow(nd.Rx * effectiveVector[3], 2) +
        //            Math.Pow(nd.Ry * effectiveVector[4], 2) +
        //            Math.Pow(nd.Rz * effectiveVector[5], 2));
        //        if (isThetaLocal)
        //        {
        //            // 既存: θ系は表示倍率を乗じていた（そのまま）
        //            allValues.Add(Math.Abs(val) * multiplier * displayScale);
        //        }
        //        else
        //        {
        //            // 変更点: U系も表示倍率を乗じてカラーバーを表示倍率後の値に揃える
        //            allValues.Add(Math.Abs(val) * multiplier * displayScale);
        //        }
        //    }

        //    // 2) カラーバー作成
        //    //var colorBaredGeometries = GetColorBarGeometries(allValues);
        //    var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

        //    // 3) 描画：U系 と θ系で分岐
        //    if (!isThetaLocal)
        //    {
        //        // U 系（従来の変形ポリライン / 値ラベル描画に近い処理）
        //        // Beam/DummyBeam 毎に端点の変位を取得してポリライン描画
        //        double maxAbsValue = allValues.Count > 0 ? Math.Max(Math.Abs(allValues.Min()), Math.Abs(allValues.Max())) : 0.0;

        //        // DummyBeams（根入れ部）描画（従来通り）
        //        if (viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane != null && anaModel?.DummyBeams != null)
        //        {
        //            foreach (var dummyBeam in anaModel.DummyBeams)
        //            {
        //                var nrI = dummyBeam.NodeI?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //                var nrJ = dummyBeam.NodeJ?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //                if (nrI == null || nrJ == null) continue;

        //                var ndI = nrI.CumulativeDisp;
        //                var ndJ = nrJ.CumulativeDisp;
        //                double origI = Math.Sqrt(
        //                    Math.Pow(ndI.Ux * effectiveVector[0], 2) +
        //                    Math.Pow(ndI.Uy * effectiveVector[1], 2) +
        //                    Math.Pow(ndI.Uz * effectiveVector[2], 2) +
        //                    Math.Pow(ndI.Rx * effectiveVector[3], 2) +
        //                    Math.Pow(ndI.Ry * effectiveVector[4], 2) +
        //                    Math.Pow(ndI.Rz * effectiveVector[5], 2));
        //                double origJ = Math.Sqrt(
        //                    Math.Pow(ndJ.Ux * effectiveVector[0], 2) +
        //                    Math.Pow(ndJ.Uy * effectiveVector[1], 2) +
        //                    Math.Pow(ndJ.Uz * effectiveVector[2], 2) +
        //                    Math.Pow(ndJ.Rx * effectiveVector[3], 2) +
        //                    Math.Pow(ndJ.Ry * effectiveVector[4], 2) +
        //                    Math.Pow(ndJ.Rz * effectiveVector[5], 2));

        //                // 変位量をモデル座標でスケール（multiplier はユーザー単位 -> 描画には DisplacementDiagramMultiplier を使う）
        //                Point3D nI = dummyBeam.NodeI.Coord;
        //                Point3D nJ = dummyBeam.NodeJ.Coord;
        //                Point3D nIDisp3D = new(
        //                    nI.X + ndI.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
        //                    nI.Y + ndI.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
        //                    nI.Z + ndI.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);
        //                Point3D nJDisp3D = new(
        //                    nJ.X + ndJ.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
        //                    nJ.Y + ndJ.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
        //                    nJ.Z + ndJ.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);

        //                Point pI = viewModel.CanvasThreeDView.Transformation(nI);
        //                Point pIDisp = viewModel.CanvasThreeDView.Transformation(nIDisp3D);
        //                Point pJDisp = viewModel.CanvasThreeDView.Transformation(nJDisp3D);
        //                Point pJ = viewModel.CanvasThreeDView.Transformation(nJ);

        //                if (!double.IsNaN(pI.X) && !double.IsNaN(pJ.Y))
        //                {
        //                    if (!viewModel.AnalysisResultNodeDisplacementType.StartsWith('θ'))
        //                    {
        //                        AddColorPolyLineGeometry(
        //                            [pI, pIDisp, pJDisp, pJ],
        //                            [Math.Abs(origI) * multiplier * displayScale,
        //                             Math.Abs(origI) * multiplier * displayScale,
        //                             Math.Abs(origJ) * multiplier * displayScale,
        //                             Math.Abs(origJ) * multiplier * displayScale],
        //                             colorBaredGeometries,
        //                             isClosed: false);
        //                    }
        //                }

        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black, origI * multiplier, origJ * multiplier, pIDisp, pJDisp, pJ, pI, format, format);
        //                }
        //            }
        //        }

        //        // Beams（杭要素）描画
        //        foreach (var beam in anaModel.Beams)
        //        {
        //            var nrI = beam.NodeI?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //            var nrJ = beam.NodeJ?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //            if (nrI == null || nrJ == null) continue;

        //            var ndI = nrI.CumulativeDisp;
        //            var ndJ = nrJ.CumulativeDisp;

        //            double origI = Math.Sqrt(
        //                Math.Pow(ndI.Ux * effectiveVector[0], 2) +
        //                Math.Pow(ndI.Uy * effectiveVector[1], 2) +
        //                Math.Pow(ndI.Uz * effectiveVector[2], 2) +
        //                Math.Pow(ndI.Rx * effectiveVector[3], 2) +
        //                Math.Pow(ndI.Ry * effectiveVector[4], 2) +
        //                Math.Pow(ndI.Rz * effectiveVector[5], 2));
        //            double origJ = Math.Sqrt(
        //                Math.Pow(ndJ.Ux * effectiveVector[0], 2) +
        //                Math.Pow(ndJ.Uy * effectiveVector[1], 2) +
        //                Math.Pow(ndJ.Uz * effectiveVector[2], 2) +
        //                Math.Pow(ndJ.Rx * effectiveVector[3], 2) +
        //                Math.Pow(ndJ.Ry * effectiveVector[4], 2) +
        //                Math.Pow(ndJ.Rz * effectiveVector[5], 2));

        //            Point3D nodeI3D = beam.NodeI.Coord;
        //            Point3D nodeJ3D = beam.NodeJ.Coord;
        //            Point3D nodeIDisp3D = new(
        //                nodeI3D.X + ndI.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
        //                nodeI3D.Y + ndI.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
        //                nodeI3D.Z + ndI.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);
        //            Point3D nodeJDisp3D = new(
        //                nodeJ3D.X + ndJ.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
        //                nodeJ3D.Y + ndJ.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
        //                nodeJ3D.Z + ndJ.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);

        //            Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
        //            Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
        //            Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
        //            Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

        //            if (!isThetaLocal)
        //            {
        //                AddColorPolyLineGeometry(
        //                    [nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D],
        //                    [Math.Abs(origI) * multiplier * displayScale,
        //                     Math.Abs(origI) * multiplier * displayScale,
        //                     Math.Abs(origJ) * multiplier * displayScale,
        //                     Math.Abs(origJ) * multiplier * displayScale],
        //                     colorBaredGeometries);
        //            }

        //            if (viewModel.IsResultValueVisible)
        //            {
        //                if (viewModel.IsPileTopResultValueVisibleOnly)
        //                {
        //                    if (beam.IsPileTop)
        //                    {
        //                        AddText3D(Brushes.Black, string.Format(format, origI * multiplier), nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
        //                    }
        //                }
        //                else
        //                {
        //                    DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black, origI * multiplier, origJ * multiplier, nodeIDisp2D, nodeJDisp2D, nodeJ2D, nodeI2D, format, format);
        //                }
        //            }
        //        }
        //    }
        //    else
        //    {
        //        // θ 系：全節点に対して楕円を描く（ProjectionUtils を利用）
        //        double flattening = viewModel.CanvasThreeDView.Flattening;

        //        // カラーバー用 allValues は既に「rot * multiplier * DisplacementDiagramMultiplier」で作成済み（上で）
        //        // colorBaredGeometries を使って楕円を色分けする
        //        foreach (var node in nodeSet)
        //        {
        //            if (node == null) continue;
        //            var nr = node.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
        //            if (nr == null) continue;
        //            var nd = nr.CumulativeDisp;

        //            // 回転量と軸
        //            double rot = 0.0;
        //            Vector3D axis = new(0, 0, 1);
        //            switch (viewModel.AnalysisResultNodeDisplacementType)
        //            {
        //                case "θH":
        //                    rot = Math.Sqrt(nd.Rx * nd.Rx + nd.Ry * nd.Ry);
        //                    axis = new Vector3D(nd.Rx, nd.Ry, 0);
        //                    break;
        //                case "θX":
        //                    rot = Math.Abs(nd.Rx);
        //                    axis = new Vector3D(nd.Rx, 0, 0);
        //                    break;
        //                case "θY":
        //                    rot = Math.Abs(nd.Ry);
        //                    axis = new Vector3D(0, nd.Ry, 0);
        //                    break;
        //                case "θZ":
        //                    rot = Math.Abs(nd.Rz);
        //                    axis = new Vector3D(0, 0, nd.Rz);
        //                    break;
        //            }
        //            if (rot <= 1e-15) continue;

        //            double displayedMagnitude = rot * multiplier;
        //            double targetPixelDiameter = Math.Abs(displayedMagnitude) * viewModel.CanvasThreeDView.Scale * viewModel.DisplacementDiagramMultiplier;
        //            if (targetPixelDiameter <= 0) continue;

        //            var proj = ProjectionUtils.ProjectCircleAsEllipseExact(node.Coord, axis, 1.0, viewModel.CanvasThreeDView.Transformation);
        //            if (proj == null) continue;
        //            var (center2DUnit, majorUnitPx, minorUnitPx, angleDegUnit) = proj.Value;
        //            if (majorUnitPx <= 1e-9) continue;

        //            double scale = (targetPixelDiameter * 0.5) / majorUnitPx;
        //            double finalMajor = majorUnitPx * scale;
        //            double finalMinor = minorUnitPx * scale;

        //            EllipseGeometry ellipse = new(center2DUnit, finalMajor, finalMinor);
        //            Geometry geometryToAdd;
        //            if (Math.Abs(angleDegUnit) > 1e-6)
        //            {
        //                var gg = new GeometryGroup();
        //                gg.Children.Add(ellipse);
        //                gg.Transform = new RotateTransform(angleDegUnit, center2DUnit.X, center2DUnit.Y);
        //                geometryToAdd = gg;
        //            }
        //            else
        //            {
        //                geometryToAdd = ellipse;
        //            }

        //            // midValue はカラーバーに合わせたスケール（同じスケールで allValues を作っているのでそれを使う）
        //            double midValue = Math.Abs(displayedMagnitude) * viewModel.DisplacementDiagramMultiplier;

        //            //var picked = PickColorGeometry(midValue, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);
        //            var picked = ColorBarUtils.PickColorGeometry(midValue, colorBaredGeometries)
        //                        ?? ColorBarUtils.PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);

        //            if (picked != null)
        //                picked.PathGeometry.AddGeometry(geometryToAdd);

        //            if (viewModel.IsResultValueVisible)
        //                AddText3D(Brushes.Black, GetNumberString(rot * multiplier, viewModel.DecimalPlaces), center2DUnit.X, center2DUnit.Y - finalMajor, "C", "B", 0.0);
        //        }
        //    }

        //    // 最後にカラーバーと Path を描画
        //    if (colorBaredGeometries != null)
        //    {
        //        foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
        //        {
        //            colorBaredGeometry.DrawPathes(Canvas3DLayout);
        //        }

        //        if (allValues.Count > 0)
        //        {
        //            ColorBar.DrawStepColorBar(
        //                ColorBarCanvas,
        //                colorBaredGeometries,
        //                viewModel.AnalysisResultNodeDisplacementType,
        //                unit,
        //                allValues.Min(),
        //                allValues.Max(),
        //                "{0:N" + viewModel.DecimalPlaces + "}",
        //                viewModel.LabelSize
        //            );
        //        }
        //        else
        //        {
        //            ColorBarCanvas.Children.Clear();
        //        }
        //    }
        //}

        //// 地盤ばね
        //private void DrawSoilSpringsResult3D(MainWindowViewModel viewModel)
        //{

        //    var anaModel = viewModel.CurrentModel;
        //    if (anaModel == null || anaModel.Beams == null)
        //        return;

        //    {
        //        if (viewModel == null || anaModel == null) return;
        //        if (anaModel.HorizontalSoilSprings == null || anaModel.HorizontalSoilSprings.Count == 0) return;
        //        if (Canvas3DLayout == null || ColorBarCanvas == null) return;

        //        // 1) 全ばねの力大きさを収集（カラーバー用）
        //        var allForceMags = new ObservableCollection<double>();
        //        foreach (var s in anaModel.HorizontalSoilSprings)
        //        {
        //            try
        //            {
        //                // 最新の要素内力をセット（secant を想定）
        //                s.SetBeamDispAndForce(isTan: false);

        //                // I端の並進力 (0..2 が I端の Fx,Fy,Fz)
        //                double fx = s.CumulativeForce.GetByIndex(0);
        //                double fy = s.CumulativeForce.GetByIndex(1);
        //                double fz = s.CumulativeForce.GetByIndex(2);
        //                var fv = new System.Windows.Media.Media3D.Vector3D(fx, fy, fz);
        //                allForceMags.Add(fv.Length);
        //            }
        //            catch
        //            {
        //                // 念のため無視して続行
        //            }
        //        }

        //        if (allForceMags.Count == 0)
        //        {
        //            ColorBarCanvas.Children.Clear();
        //            return;
        //        }

        //        // カラーバージオメトリ（力大きさに基づく）
        //        //var colorBaredGeometries = GetColorBarGeometries(allForceMags);
        //        var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allForceMags);

        //        // 2) 各ばねについて、I点（head）と tail ( = head - scaled (dispI - dispJ)) を求めて描画
        //        foreach (var s in anaModel.HorizontalSoilSprings)
        //        {
        //            if (s?.NodeI == null || s.NodeJ == null) continue;

        //            try
        //            {
        //                // 要素内力を更新（安全）
        //                s.SetBeamDispAndForce(isTan: false);

        //                // I端の力（並進成分）
        //                double fx = s.CumulativeForce.GetByIndex(0);
        //                double fy = s.CumulativeForce.GetByIndex(1);
        //                double fz = s.CumulativeForce.GetByIndex(2);
        //                var forceVec = new System.Windows.Media.Media3D.Vector3D(fx, fy, fz);
        //                double forceMag = forceVec.Length;

        //                // ノードの変位差 (I - J)（並進成分のみ）
        //                var di = s.NodeI.CumulativeDisp;
        //                var dj = s.NodeJ.CumulativeDisp;
        //                var dispDiff = new System.Windows.Media.Media3D.Vector3D(
        //                    di.Ux - dj.Ux,
        //                    di.Uy - dj.Uy,
        //                    0
        //                );

        //                // 表示スケール: viewModel.DisplacementDiagramMultiplier を使う（必要に応じて調整してください）
        //                var scaledDisp = dispDiff * viewModel.ForceDiagramMultiplier * 1000;

        //                // 矢印の頂点（I点）と尾（頂点 - scaledDisp）
        //                var head3D = s.NodeI.Coord;
        //                var tail3D = new System.Windows.Media.Media3D.Point3D(
        //                    head3D.X - scaledDisp.X,
        //                    head3D.Y - scaledDisp.Y,
        //                    head3D.Z - scaledDisp.Z
        //                );

        //                // 2D投影
        //                Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
        //                Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);

        //                // カラー帯の選択（力大きさで色分け）
        //                // midValue として力大きさをそのまま使う
        //                //var picked = PickColorGeometry(forceMag, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(forceMag, colorBaredGeometries) ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
        //                var picked = ColorBarUtils.PickColorGeometry(forceMag, colorBaredGeometries)
        //                             ?? ColorBarUtils.PickColorGeometryInclusiveTop(forceMag, colorBaredGeometries)
        //                             ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);

        //                if (picked == null) continue;

        //                // 線分（尾 -> 頭）
        //                var line = new LineGeometry(tail2D, head2D);
        //                picked.PathGeometry.AddGeometry(line);

        //                // 矢印頭（小楕円）と簡易ヘッド線を描く
        //                double arrowHeadDia2D = viewModel.ArrowHeadDia;
        //                // 楕円中心を頭に少し引いた位置に置く（見た目調整）
        //                Vector dir = head2D - tail2D;
        //                double dirLen = dir.Length;
        //                Vector dirNorm = dirLen > 1e-9 ? dir / dirLen : new Vector(0, -1);
        //                Point centerEllipse = head2D - dirNorm * (viewModel.ArrowHeadLength * 0.4);

        //                var ellipse = new EllipseGeometry(centerEllipse, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * viewModel.CanvasThreeDView.Flattening);
        //                picked.PathGeometry.AddGeometry(ellipse);

        //                // 簡易なヘッドの母線（2本）
        //                Vector ortho = GetUnitOrthogonalVector(dirNorm);
        //                Point side1 = centerEllipse - dirNorm * (viewModel.ArrowHeadLength * 0.6) + ortho * (arrowHeadDia2D * 0.5);
        //                Point side2 = centerEllipse - dirNorm * (viewModel.ArrowHeadLength * 0.6) - ortho * (arrowHeadDia2D * 0.5);
        //                picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side1));
        //                picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side2));

        //                // 値ラベル（任意、力の大きさを表示）
        //                if (viewModel.IsResultValueVisible)
        //                {
        //                    string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
        //                    //AddText3D(Brushes.Black, string.Format(fmt, forceMag), (head2D.X + tail2D.X) * 0.5, (head2D.Y + tail2D.Y) * 0.5, "C", "C", GetAngle(dir));
        //                    AddText3D(Brushes.Black, string.Format(fmt, forceMag), (head2D.X + tail2D.X) * 0.5, (head2D.Y + tail2D.Y) * 0.5, "C", "C", 0);
        //                }
        //            }
        //            catch
        //            {
        //                // 個別失敗は無視して続行
        //            }
        //        }

        //        // 3) Path を Canvas に描画
        //        foreach (var geo in colorBaredGeometries)
        //        {
        //            geo.DrawPathes(Canvas3DLayout);
        //        }

        //        // 4) カラーバー表示（力の最小/最大）
        //        if (allForceMags.Count > 0)
        //        {
        //            ColorBar.DrawStepColorBar(
        //                ColorBarCanvas,
        //                colorBaredGeometries,
        //                "地盤ばね力",
        //                "kN",
        //                allForceMags.Min(),
        //                allForceMags.Max(),
        //                "{0:N" + viewModel.DecimalPlaces + "}",
        //                viewModel.LabelSize
        //            );
        //        }
        //        else
        //        {
        //            ColorBarCanvas.Children.Clear();
        //        }
        //    }
        //}


        //// 小ヘルパ（例）
        ////private static double SelectForceByOption(string type, BeamForce f)
        ////    => type switch
        ////    {
        ////        "Fx" => f.Fxi,
        ////        "Fy" => f.Fyi,
        ////        "Fz" => f.Fzi,
        ////        "Mx" => f.Mxi,
        ////        "My" => f.Myi,
        ////        "Mz" => f.Mzi,
        ////        "Fh" => f.Fi,
        ////        "Mh" => f.Mi,
        ////        _ => 0.0
        ////    };

        ////private static double SelectDispByOption(string type, BeamDisp d)
        ////    => type switch
        ////    {
        ////        "UX" => d.Ux,
        ////        "UY" => d.Uy,
        ////        "UZ" => d.Uz,
        ////        "θX" => d.Rx,
        ////        "θY" => d.Ry,
        ////        "θZ" => d.Rz,
        ////        "UH" => d.Uh,
        ////        "θH" => d.Rh,
        ////        _ => 0.0
        ////    };

        ////private static Vector3D BuildDispVector(string type, BeamDisp d, double loadAngleDeg)
        ////{
        ////    double rad = loadAngleDeg * Math.PI / 180.0;
        ////    return type switch
        ////    {
        ////        "UH" => new Vector3D(d.Uh * Math.Cos(rad), d.Uh * Math.Sin(rad), 0),
        ////        "UX" => new Vector3D(d.Ux, 0, 0),
        ////        "UY" => new Vector3D(0, d.Uy, 0),
        ////        "UZ" => new Vector3D(0, 0, d.Uz),
        ////        "θH" => new Vector3D(d.Rh * Math.Cos(rad), d.Rh * Math.Sin(rad), 0),
        ////        "θX" => new Vector3D(d.Rx, 0, 0),
        ////        "θY" => new Vector3D(0, d.Ry, 0),
        ////        "θZ" => new Vector3D(0, 0, d.Rz),
        ////        _ => new Vector3D(0, 0, 0)
        ////    };
        ////}

        private static IEnumerable<LoadCase> GetSelectedLoadCases(MainWindowViewModel vm)
            => vm.CurrentInputModel.LoadCasesInput.AllSeismicLoadCases
               .Where(lc => vm.LoadCaseNameOption.Contains(lc.LoadName));

        private static IEnumerable<LoadCombination> GetSelectedLoadCombinations(MainWindowViewModel vm)
            => vm.CurrentInputModel.LoadCasesInput.LoadCombinations
               .Where(c => vm.LoadCombinationNameOption.Contains(c.GetName()));


        // 解析結果更新メソッド
        //[Obsolete("UpdateAnalysisResult3D_prev は退避版。UpdateAnalysisResult3D を使用してください。", false)]
        private void UpdateAnalysisResult3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (string.IsNullOrEmpty(viewModel.AnalysisResultContent))
                return;

            //var anaModel = viewModel.CurrentModel;
            //if (anaModel == null || anaModel.Beams == null)
            //return;

            string unit; // 単位

            // アクティブ（IsVisible）な杭のビーム・節点セットを構築
            // 非アクティブ杭のダイアグラムを非表示にするためのフィルタ
            var visibleBeams = new HashSet<Beam>();
            var visibleFemNodes = new HashSet<Node>();
            var visibleSoilSprings = new HashSet<HorizontalSoilSpring>();
            bool hasInvisiblePile = false;
            if (viewModel.CurrentInputModel?.PileLayoutItems != null)
            {
                foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    if (pile.IsVisible)
                    {
                        foreach (var beam in pile.Beams) visibleBeams.Add(beam);
                        foreach (var node in pile.PileNodes) visibleFemNodes.Add(node);
                        foreach (var spring in pile.HorizontalSoilSprings) visibleSoilSprings.Add(spring);
                    }
                    else
                    {
                        hasInvisiblePile = true;
                    }
                }
            }

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
                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
                    foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            // 単杭沈下は m で格納されている → mm に変換
                            values.Add(pileLocation.SinglePileSettlementVL * 1000);
                            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel1s[i] * 1000); // m → mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel2s[i] * 1000); // m → mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                        }
                    }
                }
                if (viewModel.AnalysisResultSettlementType == "群杭")
                {
                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
                    foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                        values.Add(pileLocation.GroupPileSettlement); // mmのまま
                    }
                }
                else if (viewModel.AnalysisResultSettlementType == "単杭+群杭")
                {
                    double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
                    foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            values.Add(pileLocation.SinglePileSettlementVL * 1000 + pileLocation.GroupPileSettlement); // m→mm + mm
                            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel1s[i] * 1000 + pileLocation.GroupPileSettlement); // m→mm + mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel2s[i] * 1000 + pileLocation.GroupPileSettlement); // m→mm + mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                        }
                    }
                }

                _pendingSettlementPoints = points;
                _pendingSettlementValues = values;
                _pendingSettlementTitle = title;
                _pendingSettlementUnit = unit;
            }

            // 追加: 派生表示フラグ（Mh / Fh）
            bool isDerivedMagnitude = false;
            string derivedMagnitudeType = string.Empty;

            // 応力表示
            if (viewModel.AnalysisResultContent == "梁応力")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                {
                    return;
                }

                // インデックスと方向ベクトルの決定
                int[] indices;
                Vector<double> forceDirection;

                switch (viewModel.AnalysisResultBeamForceType)
                {
                    case "Fx":
                        indices = [0, 6];
                        //forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
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
                        //forceDirection = Vector<double>.Build.DenseOfArray([1, 0, 0]);
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]);
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
                if (selectedLoadCase == null)
                {
                    return;
                }

                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
                if (selectedLoadCombination == null)
                {
                    return;
                }

                // 1回のループで最大値と描画を行う
                double maxAbsValue = 0;
                var beamResults = new List<(Beam beam, double forceI, double forceJ, double originalForceI, double originalForceJ)>();

                ObservableCollection<double> allValues = [];

                int beamCount = 0;
                int validResultCount = 0;
                foreach (var beam in anaModel.Beams)
                {
                    // 非アクティブ杭のビームはスキップ（非アクティブ杭が存在する場合のみフィルタリング）
                    if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam)) continue;

                    beamCount++;
                    var beamResult = beam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    if (beamResult == null) continue;
                    validResultCount++;

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
                            originalForceJ = -Math.Sqrt(MyJ * MyJ + MzJ * MzJ);
                        }
                        else // "Fh"
                        {
                            double FyI = beamResult.CumulativeForce.GetByIndex(1);
                            double FzI = beamResult.CumulativeForce.GetByIndex(2);
                            double FyJ = beamResult.CumulativeForce.GetByIndex(7);
                            double FzJ = beamResult.CumulativeForce.GetByIndex(8);
                            originalForceI = Math.Sqrt(FyI * FyI + FzI * FzI);
                            originalForceJ = -Math.Sqrt(FyJ * FyJ + FzJ * FzJ);
                        }
                    }
                    else
                    {
                        originalForceI = beamResult.CumulativeForce.GetByIndex(indices[0]);
                        originalForceJ = beamResult.CumulativeForce.GetByIndex(indices[1]);
                    }


                    double absForceI = Math.Abs(originalForceI);
                    double absForceJ = Math.Abs(originalForceJ);
                    //allValues.Add(absForceI);
                    //allValues.Add(absForceJ);

                    // ここを絶対値追加から符号付き追加へ変更(カラーバー用ジオメトリ)
                    allValues.Add(originalForceI);
                    allValues.Add(-originalForceJ);

                    maxAbsValue = Math.Max(maxAbsValue, Math.Max(absForceI, absForceJ));

                    //beamResults.Add((beam, absForceI, absForceJ, originalForceI, originalForceJ));
                    // 既存の beamResults に入れるtupleはそのままにしておく（必要に応じて表示側でabs/符号を使い分ける）
                    beamResults.Add((beam, Math.Abs(originalForceI), Math.Abs(originalForceJ), originalForceI, originalForceJ));
                }

                // カラーバー用ジオメトリを一度だけ生成（描画ループの前）
                //var colorBaredGeometries = GetColorBarGeometries(allValues);
                var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

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
                    // 個別成分（Fx,Fy,Fz,Mx,My,Mz）は固定の forceDirection を使用
                    // 合成量（Fh,Mh）のみ実際の力方向ベクトルを使用
                    Vector<double> dirI;
                    if (isDerivedMagnitude && rawI.L2Norm() > 1e-12)
                        dirI = rawI / rawI.L2Norm();
                    else
                        dirI = forceDirection;

                    Vector<double> dirJ;
                    if (isDerivedMagnitude && rawJ.L2Norm() > 1e-12)
                        dirJ = rawJ / rawJ.L2Norm();
                    else
                        dirJ = forceDirection;

                    // 派生量（Fh,Mh）はI端方向を反転（絶対値のため符号規約の影響なし）
                    if (isDerivedMagnitude)
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
                    Point3D nodeJForce3D;
                    if (isDerivedMagnitude)
                    {
                        // 派生量（Fh,Mh）: 絶対値なので方向ベクトルで描画、J端も符号反転
                        nodeJForce3D = new(
                            nodeJ3D.X + -forceJ * transformedForceDirectionJ[0],
                            nodeJ3D.Y + -forceJ * transformedForceDirectionJ[1],
                            nodeJ3D.Z + -forceJ * transformedForceDirectionJ[2]);
                    }
                    else
                    {
                        // 個別成分（Fx,Fy,Fz,Mx,My,Mz）: 力値の符号でダイアグラムの側を決定
                        // J端は符号規約（I端とJ端で符号反転）に対応して-forceJで描画
                        nodeJForce3D = new(
                            nodeJ3D.X + -forceJ * transformedForceDirectionJ[0],
                            nodeJ3D.Y + -forceJ * transformedForceDirectionJ[1],
                            nodeJ3D.Z + -forceJ * transformedForceDirectionJ[2]);
                    }

                    // 以下、既存の描画コード（投影→色分け→テキスト等）をそのまま使う
                    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                    Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                    Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                    var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                    //List<double> values = [Math.Abs(originalForceI), Math.Abs(originalForceI), Math.Abs(originalForceJ), Math.Abs(originalForceJ)];
                    List<double> values = [originalForceI, originalForceI, -originalForceJ, -originalForceJ];
                    AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);



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
                            // 
                            DrawResultValueTexts(
                            viewModel.IsResultValueVisible, Brushes.Black,
                            originalForceI, -originalForceJ,
                            nodeIForce2D, nodeJForce2D,
                            nodeJ2D, nodeI2D,
                            format, format);
                        }
                    }
                }


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
                        "{0:N" + viewModel.DecimalPlaces + "}",
                        viewModel.LabelSize
                    );
                }
                else
                {
                    ColorBarCanvas.Children.Clear();
                }
            }

            if (viewModel.AnalysisResultContent == "節点変位")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;

                string format = "{0:N" + viewModel.DecimalPlaces + "}";
                Vector<double> effectiveVector;
                double multiplier;
                bool isThetaLocal;
                switch (viewModel.AnalysisResultNodeDisplacementType)
                {
                    case "UH":
                        effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 0, 0, 0, 0]);
                        multiplier = 1000;
                        isThetaLocal = false;
                        break;
                    case "UX":
                        effectiveVector = Vector<double>.Build.DenseOfArray([1, 0, 0, 0, 0, 0]);
                        multiplier = 1000;
                        isThetaLocal = false;
                        break;
                    case "UY":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 1, 0, 0, 0, 0]);
                        multiplier = 1000;
                        isThetaLocal = false;
                        break;
                    case "UZ":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 1, 0, 0, 0]);
                        multiplier = 1000;
                        isThetaLocal = false;
                        break;
                    case "θH":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 1, 0]);
                        multiplier = 1;
                        isThetaLocal = true;
                        break;
                    case "θX":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 0, 0]);
                        multiplier = 1;
                        isThetaLocal = true;
                        break;
                    case "θY":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 1, 0]);
                        multiplier = 1;
                        isThetaLocal = true;
                        break;
                    case "θZ":
                        effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 0, 1]);
                        multiplier = 1;
                        isThetaLocal = true;
                        break;
                    default:
                        return;
                }

                // 単位設定
                unit = isThetaLocal ? "rad" : "mm";

                // 選択ケース/組合せ（既存）
                var selectedLoadCase = LoadCases.GetLoadCase(
                    viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
                if (selectedLoadCase == null || selectedLoadCombination == null) return;

                // 1) 全節点（Dummy含むBeams端点）を一意に取り、必要な「表示値」を収集する
                //    非アクティブ杭の節点はフィルタで除外する
                var nodeSet = new HashSet<Node>();
                if (anaModel?.Nodes != null && anaModel.Nodes.Count > 0)
                {
                    foreach (var n in anaModel.Nodes)
                    {
                        if (n != null && (!hasInvisiblePile || visibleFemNodes.Count == 0 || visibleFemNodes.Contains(n)))
                            nodeSet.Add(n);
                    }
                }
                else
                {
                    if (anaModel?.Beams != null)
                    {
                        foreach (var b in anaModel.Beams)
                        {
                            if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(b)) continue;
                            if (b?.NodeI != null) nodeSet.Add(b.NodeI);
                            if (b?.NodeJ != null) nodeSet.Add(b.NodeJ);
                        }
                    }
                    if (!hasInvisiblePile && anaModel?.DummyBeams != null)
                    {
                        foreach (var db in anaModel.DummyBeams)
                        {
                            if (db?.NodeI != null) nodeSet.Add(db.NodeI);
                            if (db?.NodeJ != null) nodeSet.Add(db.NodeJ);
                        }
                    }
                }

                var allValues = new ObservableCollection<double>();
                // U系: ノルム（multiplier を掛けたユーザー表示値）
                // θ系: 回転量（rad 等）を multiplier（=1）・表示倍率でスケールしてカラーバーに使う
                foreach (var node in nodeSet)
                {
                    if (node == null) continue;
                    var nr = node.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    if (nr == null) continue;
                    var nd = nr.CumulativeDisp;
                    // 値の抽出（effectiveVector に従う）
                    double val = Math.Sqrt(
                        Math.Pow(nd.Ux * effectiveVector[0], 2) +
                        Math.Pow(nd.Uy * effectiveVector[1], 2) +
                        Math.Pow(nd.Uz * effectiveVector[2], 2) +
                        Math.Pow(nd.Rx * effectiveVector[3], 2) +
                        Math.Pow(nd.Ry * effectiveVector[4], 2) +
                        Math.Pow(nd.Rz * effectiveVector[5], 2));
                    if (isThetaLocal)
                    {
                        // θ 系は表示上の値領域とカラーバーを揃えるため DisplacementDiagramMultiplier を乗ずる
                        allValues.Add(Math.Abs(val) * multiplier * viewModel.DisplacementDiagramMultiplier);
                    }
                    else
                    {
                        allValues.Add(Math.Abs(val) * multiplier);
                    }
                }

                // 2) カラーバー作成
                //var colorBaredGeometries = GetColorBarGeometries(allValues);
                var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

                // 3) 描画：U系 と θ系で分岐
                if (!isThetaLocal)
                {
                    // U 系（従来の変形ポリライン / 値ラベル描画に近い処理）
                    // Beam/DummyBeam 毎に端点の変位を取得してポリライン描画
                    double maxAbsValue = allValues.Count > 0 ? Math.Max(Math.Abs(allValues.Min()), Math.Abs(allValues.Max())) : 0.0;

                    // DummyBeams（根入れ部）描画 — 非アクティブ杭が存在する場合はスキップ
                    if (!hasInvisiblePile && viewModel.CurrentInputModel.ElementDivision.DoatsuGoryokuBane != null && anaModel?.DummyBeams != null)
                    {
                        foreach (var dummyBeam in anaModel.DummyBeams)
                        {
                            var nrI = dummyBeam.NodeI?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                            var nrJ = dummyBeam.NodeJ?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                            if (nrI == null || nrJ == null) continue;

                            var ndI = nrI.CumulativeDisp;
                            var ndJ = nrJ.CumulativeDisp;
                            double origI = Math.Sqrt(
                                Math.Pow(ndI.Ux * effectiveVector[0], 2) +
                                Math.Pow(ndI.Uy * effectiveVector[1], 2) +
                                Math.Pow(ndI.Uz * effectiveVector[2], 2) +
                                Math.Pow(ndI.Rx * effectiveVector[3], 2) +
                                Math.Pow(ndI.Ry * effectiveVector[4], 2) +
                                Math.Pow(ndI.Rz * effectiveVector[5], 2));
                            double origJ = Math.Sqrt(
                                Math.Pow(ndJ.Ux * effectiveVector[0], 2) +
                                Math.Pow(ndJ.Uy * effectiveVector[1], 2) +
                                Math.Pow(ndJ.Uz * effectiveVector[2], 2) +
                                Math.Pow(ndJ.Rx * effectiveVector[3], 2) +
                                Math.Pow(ndJ.Ry * effectiveVector[4], 2) +
                                Math.Pow(ndJ.Rz * effectiveVector[5], 2));

                            // 変位量をモデル座標でスケール（multiplier はユーザー単位 -> 描画には DisplacementDiagramMultiplier を使う）
                            Point3D nI = dummyBeam.NodeI.Coord;
                            Point3D nJ = dummyBeam.NodeJ.Coord;
                            Point3D nIDisp3D = new(
                                nI.X + ndI.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
                                nI.Y + ndI.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
                                nI.Z + ndI.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);
                            Point3D nJDisp3D = new(
                                nJ.X + ndJ.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
                                nJ.Y + ndJ.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
                                nJ.Z + ndJ.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);

                            Point pI = viewModel.CanvasThreeDView.Transformation(nI);
                            Point pIDisp = viewModel.CanvasThreeDView.Transformation(nIDisp3D);
                            Point pJDisp = viewModel.CanvasThreeDView.Transformation(nJDisp3D);
                            Point pJ = viewModel.CanvasThreeDView.Transformation(nJ);

                            if (!double.IsNaN(pI.X) && !double.IsNaN(pJ.Y))
                            {
                                if (!viewModel.AnalysisResultNodeDisplacementType.StartsWith('θ'))
                                {
                                    AddColorPolyLineGeometry([pI, pIDisp, pJDisp, pJ], [Math.Abs(origI) * multiplier, Math.Abs(origI) * multiplier, Math.Abs(origJ) * multiplier, Math.Abs(origJ) * multiplier], colorBaredGeometries, isClosed: false);
                                }
                            }

                            if (viewModel.IsResultValueVisible)
                            {
                                DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black, origI * multiplier, origJ * multiplier, pIDisp, pJDisp, pJ, pI, format, format);
                            }
                        }
                    }

                    // Beams（杭要素）描画 — 非アクティブ杭のビームはスキップ（非アクティブ杭が存在する場合のみフィルタリング）
                    foreach (var beam in anaModel.Beams)
                    {
                        if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam)) continue;

                        var nrI = beam.NodeI?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                        var nrJ = beam.NodeJ?.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                        if (nrI == null || nrJ == null) continue;

                        var ndI = nrI.CumulativeDisp;
                        var ndJ = nrJ.CumulativeDisp;

                        double origI = Math.Sqrt(
                            Math.Pow(ndI.Ux * effectiveVector[0], 2) +
                            Math.Pow(ndI.Uy * effectiveVector[1], 2) +
                            Math.Pow(ndI.Uz * effectiveVector[2], 2) +
                            Math.Pow(ndI.Rx * effectiveVector[3], 2) +
                            Math.Pow(ndI.Ry * effectiveVector[4], 2) +
                            Math.Pow(ndI.Rz * effectiveVector[5], 2));
                        double origJ = Math.Sqrt(
                            Math.Pow(ndJ.Ux * effectiveVector[0], 2) +
                            Math.Pow(ndJ.Uy * effectiveVector[1], 2) +
                            Math.Pow(ndJ.Uz * effectiveVector[2], 2) +
                            Math.Pow(ndJ.Rx * effectiveVector[3], 2) +
                            Math.Pow(ndJ.Ry * effectiveVector[4], 2) +
                            Math.Pow(ndJ.Rz * effectiveVector[5], 2));

                        Point3D nodeI3D = beam.NodeI.Coord;
                        Point3D nodeJ3D = beam.NodeJ.Coord;
                        Point3D nodeIDisp3D = new(
                            nodeI3D.X + ndI.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
                            nodeI3D.Y + ndI.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
                            nodeI3D.Z + ndI.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);
                        Point3D nodeJDisp3D = new(
                            nodeJ3D.X + ndJ.Ux * effectiveVector[0] * viewModel.DisplacementDiagramMultiplier,
                            nodeJ3D.Y + ndJ.Uy * effectiveVector[1] * viewModel.DisplacementDiagramMultiplier,
                            nodeJ3D.Z + ndJ.Uz * effectiveVector[2] * viewModel.DisplacementDiagramMultiplier);

                        Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                        Point nodeIDisp2D = viewModel.CanvasThreeDView.Transformation(nodeIDisp3D);
                        Point nodeJDisp2D = viewModel.CanvasThreeDView.Transformation(nodeJDisp3D);
                        Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                        if (!isThetaLocal)
                        {
                            AddColorPolyLineGeometry([nodeI2D, nodeIDisp2D, nodeJDisp2D, nodeJ2D], [Math.Abs(origI) * multiplier, Math.Abs(origI) * multiplier, Math.Abs(origJ) * multiplier, Math.Abs(origJ) * multiplier], colorBaredGeometries);
                        }

                        if (viewModel.IsResultValueVisible)
                        {
                            if (viewModel.IsPileTopResultValueVisibleOnly)
                            {
                                if (beam.IsPileTop)
                                {
                                    AddText3D(Brushes.Black, string.Format(format, origI * multiplier), nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
                                }
                            }
                            else
                            {
                                DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black, origI * multiplier, origJ * multiplier, nodeIDisp2D, nodeJDisp2D, nodeJ2D, nodeI2D, format, format);
                            }
                        }
                    }
                }
                else
                {
                    // θ 系：全節点に対して楕円を描く（ProjectionUtils を利用）
                    double flattening = viewModel.CanvasThreeDView.Flattening;

                    // カラーバー用 allValues は既に「rot * multiplier * DisplacementDiagramMultiplier」で作成済み（上で）
                    // colorBaredGeometries を使って楕円を色分けする
                    foreach (var node in nodeSet)
                    {
                        if (node == null) continue;
                        var nr = node.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                        if (nr == null) continue;
                        var nd = nr.CumulativeDisp;

                        // 回転量と軸
                        double rot = 0.0;
                        Vector3D axis = new(0, 0, 1);
                        switch (viewModel.AnalysisResultNodeDisplacementType)
                        {
                            case "θH":
                                rot = Math.Sqrt(nd.Rx * nd.Rx + nd.Ry * nd.Ry);
                                axis = new Vector3D(nd.Rx, nd.Ry, 0);
                                break;
                            case "θX":
                                rot = Math.Abs(nd.Rx);
                                axis = new Vector3D(nd.Rx, 0, 0);
                                break;
                            case "θY":
                                rot = Math.Abs(nd.Ry);
                                axis = new Vector3D(0, nd.Ry, 0);
                                break;
                            case "θZ":
                                rot = Math.Abs(nd.Rz);
                                axis = new Vector3D(0, 0, nd.Rz);
                                break;
                        }
                        if (rot <= 1e-15) continue;

                        double displayedMagnitude = rot * multiplier;
                        double targetPixelDiameter = Math.Abs(displayedMagnitude) * viewModel.CanvasThreeDView.Scale * viewModel.DisplacementDiagramMultiplier;
                        if (targetPixelDiameter <= 0) continue;

                        var proj = ProjectionUtils.ProjectCircleAsEllipseExact(node.Coord, axis, 1.0, viewModel.CanvasThreeDView.Transformation);
                        if (proj == null) continue;
                        var (center2DUnit, majorUnitPx, minorUnitPx, angleDegUnit) = proj.Value;
                        if (majorUnitPx <= 1e-9) continue;

                        double scale = (targetPixelDiameter * 0.5) / majorUnitPx;
                        double finalMajor = majorUnitPx * scale;
                        double finalMinor = minorUnitPx * scale;

                        EllipseGeometry ellipse = new(center2DUnit, finalMajor, finalMinor);
                        Geometry geometryToAdd;
                        if (Math.Abs(angleDegUnit) > 1e-6)
                        {
                            var gg = new GeometryGroup();
                            gg.Children.Add(ellipse);
                            gg.Transform = new RotateTransform(angleDegUnit, center2DUnit.X, center2DUnit.Y);
                            geometryToAdd = gg;
                        }
                        else
                        {
                            geometryToAdd = ellipse;
                        }

                        // midValue はカラーバーに合わせたスケール（同じスケールで allValues を作っているのでそれを使う）
                        double midValue = Math.Abs(displayedMagnitude) * viewModel.DisplacementDiagramMultiplier;

                        //var picked = PickColorGeometry(midValue, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);
                        var picked = ColorBarUtils.PickColorGeometry(midValue, colorBaredGeometries)
                                    ?? ColorBarUtils.PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);

                        if (picked != null)
                            picked.PathGeometry.AddGeometry(geometryToAdd);

                        if (viewModel.IsResultValueVisible)
                        {
                            AddText3D(Brushes.Black, GetNumberString(rot * multiplier, viewModel.DecimalPlaces), center2DUnit.X, center2DUnit.Y - finalMajor, "C", "B", 0.0);
                        }
                    }
                }

                // 最後にカラーバーと Path を描画
                if (colorBaredGeometries != null)
                {
                    foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                    {
                        colorBaredGeometry.DrawPathes(Canvas3DLayout);
                    }

                    if (allValues.Count > 0)
                    {
                        ColorBar.DrawStepColorBar(
                            ColorBarCanvas,
                            colorBaredGeometries,
                            viewModel.AnalysisResultNodeDisplacementType,
                            unit,
                            allValues.Min(),
                            allValues.Max(),
                            "{0:N" + viewModel.DecimalPlaces + "}",
                            viewModel.LabelSize
                        );
                    }
                    else
                    {
                        ColorBarCanvas.Children.Clear();
                    }
                }
            }
            else if (viewModel.AnalysisResultContent == "地盤ばね")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;
                DrawHorizontalSoilSpringsResult3D(viewModel, anaModel, hasInvisiblePile ? visibleSoilSprings : null);
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
                double textAdjustX = textAdjustUnit.X * viewModel.TextPositionAdjuster;
                double textAdjustY = textAdjustUnit.Y * viewModel.TextPositionAdjuster;
                AddText3D(solidColorBrush, string.Format(formatI, valueI),
                    pointI.X - textAdjustX, pointI.Y - textAdjustY, "C", "C", 0.0);
                AddText3D(solidColorBrush, string.Format(formatJ, valueJ),
                    pointJ.X + textAdjustX, pointJ.Y + textAdjustY, "C", "C", 0.0);
            }
        }

        // 追加: 地盤ばね描画ヘルパー（UpdateAnalysisResult3D 内から呼び出してください）
        private void DrawHorizontalSoilSpringsResult3D(MainWindowViewModel viewModel, AnaModel anaModel, HashSet<HorizontalSoilSpring> visibleSoilSprings = null)
        {
            if (viewModel == null || anaModel == null) return;
            if (anaModel.HorizontalSoilSprings == null || anaModel.HorizontalSoilSprings.Count == 0) return;
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            // 選択されたタイプを取得
            string springType = viewModel.AnalysisResultSoilSpringType ?? "RH";

            // 1) 全ばねの値を収集（カラーバー用） — 非アクティブ杭のばねはスキップ
            var allValues = new ObservableCollection<double>();
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (visibleSoilSprings != null && visibleSoilSprings.Count > 0 && !visibleSoilSprings.Contains(s)) continue;

                try
                {
                    // 最新の要素内力をセット（secant を想定）
                    s.SetBeamDispAndForce(isTan: false);

                    // 選択されたタイプに応じた値を取得
                    double value = GetSoilSpringValue(s, springType);
                    allValues.Add(value);
                }
                catch
                {
                    // 念のため無視して続行
                }
            }

            if (allValues.Count == 0)
            {
                ColorBarCanvas.Children.Clear();
                return;
            }

            // カラーバージオメトリ（選択されたタイプの値に基づく）
            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

            // 単位を決定（モーメント系はkNm、力系はkN）
            string unit = (springType == "MX" || springType == "MY" || springType == "MZ" || springType == "MH") ? "kNm" : "kN";
            string colorBarTitle = GetSoilSpringTypeName(springType);

            // 2) 各ばねについて、I点（head）と tail ( = head - scaled (dispI - dispJ)) を求めて描画
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (visibleSoilSprings != null && visibleSoilSprings.Count > 0 && !visibleSoilSprings.Contains(s)) continue;
                if (s?.NodeI == null || s.NodeJ == null) continue;

                try
                {
                    // 要素内力を更新（安全）
                    s.SetBeamDispAndForce(isTan: false);

                    // 選択されたタイプに応じた値を取得
                    double displayValue = GetSoilSpringValue(s, springType);

                    // ノードの変位差 (I - J)（並進成分のみ）
                    var di = s.NodeI.CumulativeDisp;
                    var dj = s.NodeJ.CumulativeDisp;
                    double dx = di.Ux - dj.Ux;
                    double dy = di.Uy - dj.Uy;

                    // 選択されたタイプに応じて変位成分をフィルタリング
                    var dispDiff = springType switch
                    {
                        "RX" => new System.Windows.Media.Media3D.Vector3D(dx, 0, 0),  // X成分のみ
                        "RY" => new System.Windows.Media.Media3D.Vector3D(0, dy, 0),  // Y成分のみ
                        _ => new System.Windows.Media.Media3D.Vector3D(dx, dy, 0)     // RH等はXY両方
                    };

                    // 表示スケール: viewModel.DisplacementDiagramMultiplier を使う（必要に応じて調整してください）
                    var scaledDisp = dispDiff * viewModel.ForceDiagramMultiplier * 1000;

                    // 矢印の頂点（I点）と尾（頂点 - scaledDisp）
                    var head3D = s.NodeI.Coord;
                    var tail3D = new System.Windows.Media.Media3D.Point3D(
                        head3D.X - scaledDisp.X,
                        head3D.Y - scaledDisp.Y,
                        head3D.Z - scaledDisp.Z
                    );

                    // 2D投影
                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);

                    // カラー帯の選択（選択されたタイプの値で色分け）
                    var picked = ColorBarUtils.PickColorGeometry(displayValue, colorBaredGeometries)
                                 ?? ColorBarUtils.PickColorGeometryInclusiveTop(displayValue, colorBaredGeometries)
                                 ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                    if (picked == null) continue;

                    // 線分（尾 -> 頭）
                    var line = new LineGeometry(tail2D, head2D);
                    picked.PathGeometry.AddGeometry(line);

                    // 矢印頭（小楕円）と簡易ヘッド線を描く
                    double arrowHeadDia2D = viewModel.ArrowHeadDia;
                    // 楕円中心を頭に少し引いた位置に置く（見た目調整）
                    Vector dir = head2D - tail2D;
                    double dirLen = dir.Length;
                    Vector dirNorm = dirLen > 1e-9 ? dir / dirLen : new Vector(0, -1);
                    Point centerEllipse = head2D - dirNorm * (viewModel.ArrowHeadLength * 0.4);

                    var ellipse = new EllipseGeometry(centerEllipse, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * viewModel.CanvasThreeDView.Flattening);
                    picked.PathGeometry.AddGeometry(ellipse);

                    // 簡易なヘッドの母線（2本）
                    Vector ortho = GetUnitOrthogonalVector(dirNorm);
                    Point side1 = centerEllipse - dirNorm * (viewModel.ArrowHeadLength * 0.6) + ortho * (arrowHeadDia2D * 0.5);
                    Point side2 = centerEllipse - dirNorm * (viewModel.ArrowHeadLength * 0.6) - ortho * (arrowHeadDia2D * 0.5);
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side1));
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side2));

                    // 値ラベル（任意、選択されたタイプの値を表示）
                    if (viewModel.IsResultValueVisible)
                    {
                        string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                        AddText3D(Brushes.Black, string.Format(fmt, displayValue), (head2D.X + tail2D.X) * 0.5, (head2D.Y + tail2D.Y) * 0.5, "C", "C", 0);
                    }
                }
                catch
                {
                    // 個別失敗は無視して続行
                }
            }

            // 3) Path を Canvas に描画
            foreach (var geo in colorBaredGeometries)
            {
                geo.DrawPathes(Canvas3DLayout);
            }

            // 4) カラーバー表示（選択されたタイプの最小/最大）
            if (allValues.Count > 0)
            {
                ColorBar.DrawStepColorBar(
                    ColorBarCanvas,
                    colorBaredGeometries,
                    colorBarTitle,
                    unit,
                    allValues.Min(),
                    allValues.Max(),
                    "{0:N" + viewModel.DecimalPlaces + "}",
                    viewModel.LabelSize
                );
            }
            else
            {
                ColorBarCanvas.Children.Clear();
            }
        }

        /// <summary>
        /// 地盤ばねから選択されたタイプに応じた値を取得
        /// </summary>
        private static double GetSoilSpringValue(FEM.HorizontalSoilSpring spring, string springType)
        {
            // I端の力・モーメント成分を取得
            // Index 0-2: Fx, Fy, Fz (並進力)
            // Index 3-5: Mx, My, Mz (モーメント)
            double fx = spring.CumulativeForce.GetByIndex(0);
            double fy = spring.CumulativeForce.GetByIndex(1);
            double fz = spring.CumulativeForce.GetByIndex(2);
            double mx = spring.CumulativeForce.GetByIndex(3);
            double my = spring.CumulativeForce.GetByIndex(4);
            double mz = spring.CumulativeForce.GetByIndex(5);

            return springType switch
            {
                "RX" => fx,
                "RY" => fy,
                "RZ" => fz,
                "RH" => Math.Sqrt(fx * fx + fy * fy),  // 水平反力
                "MX" => mx,
                "MY" => my,
                "MZ" => mz,
                "MH" => Math.Sqrt(mx * mx + my * my),  // 水平モーメント
                _ => Math.Sqrt(fx * fx + fy * fy)       // デフォルトはRH
            };
        }

        /// <summary>
        /// 地盤ばねタイプの表示名を取得
        /// </summary>
        private static string GetSoilSpringTypeName(string springType)
        {
            return springType switch
            {
                "RX" => "地盤ばねRX",
                "RY" => "地盤ばねRY",
                "RZ" => "地盤ばねRZ",
                "RH" => "地盤ばねRH",
                "MX" => "地盤ばねMX",
                "MY" => "地盤ばねMY",
                "MZ" => "地盤ばねMZ",
                "MH" => "地盤ばねMH",
                _ => "地盤ばねRH"
            };
        }

        // ========================================
        // 応力図・変位図 ツールチップ機能
        // ========================================

        // ツールチップ用フィールド
        private System.Windows.Controls.Primitives.Popup? _beamResultTooltipPopup;
        private System.Windows.Controls.TextBlock? _beamResultTooltipText;

        // サンプル位置マーカー用フィールド
        private System.Windows.Shapes.Ellipse? _samplePositionMarker;

        /// <summary>
        /// マウス位置から応力/変位値を取得してツールチップを表示
        /// </summary>
        private void UpdateBeamResultTooltip(Point mousePos)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 梁応力または節点変位表示が有効かチェック
            if (viewModel.AnalysisResultContent != "梁応力" && viewModel.AnalysisResultContent != "節点変位")
            {
                HideBeamResultTooltip();
                return;
            }

            var anaModel = viewModel.CurrentModel;
            if (anaModel?.Beams == null || anaModel.Beams.Count == 0)
            {
                HideBeamResultTooltip();
                return;
            }

            // 選択ケース/組合せを取得
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel?.LoadCasesInput?.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel?.LoadCasesInput?.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (selectedLoadCase == null || selectedLoadCombination == null)
            {
                HideBeamResultTooltip();
                return;
            }

            // 最も近いビーム要素を探す
            Beam? closestBeam = null;
            double closestDistance = double.MaxValue;
            double closestT = 0; // ビーム上の位置（0～1）
            Point closestNodeI2D = new(), closestNodeJ2D = new();
            const double hitThreshold = 20.0; // ピクセル単位の許容距離

            foreach (var beam in anaModel.Beams)
            {
                if (beam?.NodeI == null || beam.NodeJ == null) continue;

                // ビーム端点を2D座標に変換
                Point3D nodeI3D = new(beam.NodeI.Coord.X, beam.NodeI.Coord.Y, beam.NodeI.Coord.Z);
                Point3D nodeJ3D = new(beam.NodeJ.Coord.X, beam.NodeJ.Coord.Y, beam.NodeJ.Coord.Z);

                Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                // マウス位置から線分への最短距離と位置を計算
                var (distance, t) = PointToLineSegmentDistance(mousePos, nodeI2D, nodeJ2D);

                if (distance < closestDistance && distance < hitThreshold)
                {
                    closestDistance = distance;
                    closestBeam = beam;
                    closestT = t;
                    closestNodeI2D = nodeI2D;
                    closestNodeJ2D = nodeJ2D;
                }
            }

            if (closestBeam == null)
            {
                HideBeamResultTooltip();
                return;
            }

            // 応力/変位値を取得してツールチップを表示
            var beamResult = closestBeam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            if (beamResult == null)
            {
                HideBeamResultTooltip();
                return;
            }

            // 深度を計算
            double depthI = closestBeam.NodeI?.Coord.Z ?? 0;
            double depthJ = closestBeam.NodeJ?.Coord.Z ?? 0;
            double depth = depthI * (1 - closestT) + depthJ * closestT;

            // 表示内容を構築
            string tooltipContent;
            if (viewModel.AnalysisResultContent == "梁応力")
            {
                tooltipContent = BuildBeamForceTooltip(viewModel, beamResult, closestT, depth);
            }
            else // 節点変位
            {
                tooltipContent = BuildNodeDisplacementTooltip(viewModel, closestBeam, anaModel, selectedLoadCase, selectedLoadCombination, closestT, depth);
            }

            // サンプル位置を計算（ビーム上の線形補間位置）
            Point samplePos = new(
                closestNodeI2D.X * (1 - closestT) + closestNodeJ2D.X * closestT,
                closestNodeI2D.Y * (1 - closestT) + closestNodeJ2D.Y * closestT);

            ShowBeamResultTooltip(mousePos, tooltipContent, samplePos);
        }

        /// <summary>
        /// 梁応力のツールチップテキストを構築
        /// </summary>
        private static string BuildBeamForceTooltip(MainWindowViewModel viewModel, BeamResult beamResult, double t, double depth)
        {
            var bf = beamResult.CumulativeForce;
            if (bf == null) return $"深度: {depth:F2} m";

            // I端とJ端の値を取得して線形補間
            double valueI, valueJ;
            string unit;
            string typeName = viewModel.AnalysisResultBeamForceType;

            switch (typeName)
            {
                case "Fx":
                    valueI = bf.GetByIndex(0);
                    valueJ = bf.GetByIndex(6);
                    unit = "kN";
                    break;
                case "Fy":
                    valueI = bf.GetByIndex(1);
                    valueJ = bf.GetByIndex(7);
                    unit = "kN";
                    break;
                case "Fz":
                    valueI = bf.GetByIndex(2);
                    valueJ = bf.GetByIndex(8);
                    unit = "kN";
                    break;
                case "Mx":
                    valueI = bf.GetByIndex(3);
                    valueJ = bf.GetByIndex(9);
                    unit = "kNm";
                    break;
                case "My":
                    valueI = bf.GetByIndex(4);
                    valueJ = bf.GetByIndex(10);
                    unit = "kNm";
                    break;
                case "Mz":
                    valueI = bf.GetByIndex(5);
                    valueJ = bf.GetByIndex(11);
                    unit = "kNm";
                    break;
                case "Mh":
                    double MyI = bf.GetByIndex(4);
                    double MzI = bf.GetByIndex(5);
                    double MyJ = bf.GetByIndex(10);
                    double MzJ = bf.GetByIndex(11);
                    valueI = Math.Sqrt(MyI * MyI + MzI * MzI);
                    valueJ = Math.Sqrt(MyJ * MyJ + MzJ * MzJ);
                    unit = "kNm";
                    break;
                case "Fh":
                    double FyI = bf.GetByIndex(1);
                    double FzI = bf.GetByIndex(2);
                    double FyJ = bf.GetByIndex(7);
                    double FzJ = bf.GetByIndex(8);
                    valueI = Math.Sqrt(FyI * FyI + FzI * FzI);
                    valueJ = Math.Sqrt(FyJ * FyJ + FzJ * FzJ);
                    unit = "kN";
                    break;
                default:
                    return $"深度: {depth:F2} m";
            }

            // 線形補間
            double interpolatedValue = valueI * (1 - t) + (-valueJ) * t;

            return $"{typeName}: {interpolatedValue:F1} {unit}\n深度: {depth:F2} m";
        }

        /// <summary>
        /// 節点変位のツールチップテキストを構築
        /// </summary>
        private string BuildNodeDisplacementTooltip(MainWindowViewModel viewModel, Beam beam, AnaModel anaModel,
            LoadCase loadCase, LoadCombination loadCombination, double t, double depth)
        {
            // I端とJ端の節点結果を取得
            var nrI = beam.NodeI?.GetNodeResult(anaModel, loadCase, loadCombination, viewModel.IsLiquefaction);
            var nrJ = beam.NodeJ?.GetNodeResult(anaModel, loadCase, loadCombination, viewModel.IsLiquefaction);
            if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null)
                return $"深度: {depth:F2} m";

            var ndI = nrI.CumulativeDisp;
            var ndJ = nrJ.CumulativeDisp;

            string typeName = viewModel.AnalysisResultNodeDisplacementType;
            double valueI, valueJ;
            string unit;
            double multiplier = 1.0;

            switch (typeName)
            {
                case "UH":
                    valueI = Math.Sqrt(ndI.Ux * ndI.Ux + ndI.Uy * ndI.Uy);
                    valueJ = Math.Sqrt(ndJ.Ux * ndJ.Ux + ndJ.Uy * ndJ.Uy);
                    multiplier = 1000;
                    unit = "mm";
                    break;
                case "UX":
                    valueI = ndI.Ux;
                    valueJ = ndJ.Ux;
                    multiplier = 1000;
                    unit = "mm";
                    break;
                case "UY":
                    valueI = ndI.Uy;
                    valueJ = ndJ.Uy;
                    multiplier = 1000;
                    unit = "mm";
                    break;
                case "UZ":
                    valueI = ndI.Uz;
                    valueJ = ndJ.Uz;
                    multiplier = 1000;
                    unit = "mm";
                    break;
                case "θH":
                    valueI = Math.Sqrt(ndI.Rx * ndI.Rx + ndI.Ry * ndI.Ry);
                    valueJ = Math.Sqrt(ndJ.Rx * ndJ.Rx + ndJ.Ry * ndJ.Ry);
                    unit = "rad";
                    break;
                case "θX":
                    valueI = ndI.Rx;
                    valueJ = ndJ.Rx;
                    unit = "rad";
                    break;
                case "θY":
                    valueI = ndI.Ry;
                    valueJ = ndJ.Ry;
                    unit = "rad";
                    break;
                case "θZ":
                    valueI = ndI.Rz;
                    valueJ = ndJ.Rz;
                    unit = "rad";
                    break;
                default:
                    return $"深度: {depth:F2} m";
            }

            // 線形補間
            double interpolatedValue = (valueI * (1 - t) + valueJ * t) * multiplier;

            return $"{typeName}: {interpolatedValue:F2} {unit}\n深度: {depth:F2} m";
        }

        /// <summary>
        /// 点から線分への最短距離と線分上の位置(0～1)を計算
        /// </summary>
        private static (double distance, double t) PointToLineSegmentDistance(Point p, Point a, Point b)
        {
            Vector ab = b - a;
            double abLenSq = ab.LengthSquared;

            if (abLenSq < 1e-10)
            {
                // a と b がほぼ同じ点
                return ((p - a).Length, 0);
            }

            // 線分上の最近点を求める
            Vector ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / abLenSq;
            t = Math.Clamp(t, 0, 1);

            Point closest = a + t * ab;
            double distance = (p - closest).Length;

            return (distance, t);
        }

        /// <summary>
        /// 応力/変位ツールチップを表示
        /// </summary>
        private void ShowBeamResultTooltip(Point mousePos, string content, Point samplePos)
        {
            // ポップアップが未作成なら作成
            if (_beamResultTooltipPopup == null)
            {
                _beamResultTooltipText = new System.Windows.Controls.TextBlock
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 50, 50, 50)),
                    Foreground = Brushes.White,
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 12
                };

                var border = new System.Windows.Controls.Border
                {
                    BorderBrush = Brushes.DarkGray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = _beamResultTooltipText
                };

                _beamResultTooltipPopup = new System.Windows.Controls.Primitives.Popup
                {
                    Child = border,
                    AllowsTransparency = true,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                    PlacementTarget = Canvas3DLayout,
                    IsHitTestVisible = false
                };
            }

            // ツールチップのテキストを更新
            _beamResultTooltipText!.Text = content;

            // 位置を更新（マウスの右下に表示）
            _beamResultTooltipPopup.HorizontalOffset = mousePos.X + 15;
            _beamResultTooltipPopup.VerticalOffset = mousePos.Y + 15;
            _beamResultTooltipPopup.IsOpen = true;

            // サンプル位置マーカーを表示
            ShowSamplePositionMarker(samplePos);
        }

        /// <summary>
        /// サンプル位置マーカーを表示
        /// </summary>
        private void ShowSamplePositionMarker(Point pos)
        {
            const double markerSize = 10;

            // マーカーが未作成なら作成
            if (_samplePositionMarker == null)
            {
                _samplePositionMarker = new System.Windows.Shapes.Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = new SolidColorBrush(Color.FromArgb(200, 255, 100, 100)),
                    Stroke = Brushes.DarkRed,
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };
                Canvas3DLayout.Children.Add(_samplePositionMarker);
            }

            // マーカーの位置を更新（中心がサンプル位置になるように）
            System.Windows.Controls.Canvas.SetLeft(_samplePositionMarker, pos.X - markerSize / 2);
            System.Windows.Controls.Canvas.SetTop(_samplePositionMarker, pos.Y - markerSize / 2);
            _samplePositionMarker.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 応力/変位ツールチップを非表示
        /// </summary>
        private void HideBeamResultTooltip()
        {
            if (_beamResultTooltipPopup != null)
            {
                _beamResultTooltipPopup.IsOpen = false;
            }

            // サンプル位置マーカーも非表示
            if (_samplePositionMarker != null)
            {
                _samplePositionMarker.Visibility = Visibility.Collapsed;
            }
        }
    }
}
