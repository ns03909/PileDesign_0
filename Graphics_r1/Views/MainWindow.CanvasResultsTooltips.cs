using MathNet.Numerics.LinearAlgebra;
using PileDesign.Common;
using PileDesign.FEM;
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
using Node = PileDesign.FEM.Node;
using Point = System.Windows.Point;

using Serilog;

namespace PileDesign.Views
{
    // 結果ツールチップ: 応力/変位/部材角のマウスオーバー表示とサンプル位置マーカー。MainWindow.CanvasResults.cs からの物理分割 (純粋移動)。
    public partial class MainWindow
    {
        // サンプル位置マーカー用フィールド
        private System.Windows.Shapes.Ellipse? _samplePositionMarker;

        /// <summary>
        /// マウス位置から応力/変位値を取得してツールチップを表示
        /// </summary>
        private void UpdateBeamResultTooltip(Point mousePos)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 梁応力、節点変位、部材角表示が有効かチェック
            string effContent = viewModel.EffectiveSettlementContent;
            bool isMemberAngle = effContent is "単杭沈下部材角" or "群杭沈下部材角" or "単杭+群杭沈下部材角"
                or "基礎梁考慮沈下部材角" or "基礎梁考慮+群杭沈下部材角";
            if (viewModel.AnalysisResultContent != "梁応力（水平）" && viewModel.AnalysisResultContent != "節点変位（水平）" && !isMemberAngle)
            {
                HideBeamResultTooltip();
                return;
            }

            // 部材角モードでは基礎梁の直接ヒットテストを使用
            if (isMemberAngle)
            {
                UpdateMemberAngleTooltip(viewModel, mousePos);
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
                viewModel.ResultInputModel?.LoadCasesInput?.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel?.LoadCasesInput?.LoadCombinations, viewModel.SelectedLoadCombinationName);
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
            if (viewModel.AnalysisResultContent == "梁応力（水平）")
            {
                tooltipContent = BuildBeamForceTooltip(viewModel, beamResult, closestT, depth, closestBeam);
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
        private static string BuildBeamForceTooltip(MainWindowViewModel viewModel, BeamResult beamResult, double t, double depth, Beam beam = null)
        {
            var bf = beamResult.CumulativeForce;
            if (bf == null) return $"Z: {depth:F2} m";

            string typeName = viewModel.AnalysisResultBeamForceType;
            bool isFoundationBeam = beam?.Name.StartsWith("FoundationBeam-") ?? false;

            // Mh選択時の基礎梁: MyとMz両方を表示
            if (typeName == "Mh" && isFoundationBeam)
            {
                double myI = bf.GetByIndex(4);
                double myJ = bf.GetByIndex(10);
                double mzI = bf.GetByIndex(5);
                double mzJ = bf.GetByIndex(11);
                double interpMy = myI * (1 - t) + (-myJ) * t;
                double interpMz = mzI * (1 - t) + (-mzJ) * t;
                double interpMh = Math.Sqrt(interpMy * interpMy + interpMz * interpMz);
                return $"Mh: {interpMh:F1} kNm\nMy: {interpMy:F1} kNm\nMz: {interpMz:F1} kNm\nZ: {depth:F2} m";
            }

            // Fh選択時の基礎梁: FyとFz両方を表示
            if (typeName == "Fh" && isFoundationBeam)
            {
                double fyI = bf.GetByIndex(1);
                double fyJ = bf.GetByIndex(7);
                double fzI = bf.GetByIndex(2);
                double fzJ = bf.GetByIndex(8);
                double interpFy = fyI * (1 - t) + (-fyJ) * t;
                double interpFz = fzI * (1 - t) + (-fzJ) * t;
                double interpFh = Math.Sqrt(interpFy * interpFy + interpFz * interpFz);
                return $"Fh: {interpFh:F1} kN\nFy: {interpFy:F1} kN\nFz: {interpFz:F1} kN\nZ: {depth:F2} m";
            }

            // I端とJ端の値を取得して線形補間
            double valueI, valueJ;
            string unit;

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
                    return $"Z: {depth:F2} m";
            }

            // 線形補間
            double interpolatedValue = valueI * (1 - t) + (-valueJ) * t;

            return $"{typeName}: {interpolatedValue:F1} {unit}\nZ: {depth:F2} m";
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
                return $"Z: {depth:F2} m";

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
                case "U":
                    valueI = Math.Sqrt(ndI.Ux * ndI.Ux + ndI.Uy * ndI.Uy + ndI.Uz * ndI.Uz);
                    valueJ = Math.Sqrt(ndJ.Ux * ndJ.Ux + ndJ.Uy * ndJ.Uy + ndJ.Uz * ndJ.Uz);
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
                    return $"Z: {depth:F2} m";
            }

            // 線形補間
            double interpolatedValue = (valueI * (1 - t) + valueJ * t) * multiplier;

            return $"{typeName}: {interpolatedValue:F2} {unit}\nZ: {depth:F2} m";
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
                    Fill = BrushErrorFill,
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

        /// <summary>
        /// 部材角モード時のツールチップ表示（基礎梁にマウスを近づけると部材角をポップアップ）
        /// </summary>
        private void UpdateMemberAngleTooltip(MainWindowViewModel viewModel, Point mousePos)
        {
            var inputModel = viewModel.ResultInputModel;
            var fbBeams = inputModel?.FoundationBeamInput?.Beams;
            if (fbBeams == null || fbBeams.Count == 0) { HideBeamResultTooltip(); return; }

            string content = viewModel.EffectiveSettlementContent;

            // 沈下マップ構築（DrawBeamMemberAngleと同じロジック、全て m 単位）
            var settlementMap = new Dictionary<int, double>();
            var pgs = inputModel.PileGroupSettlement;
            if (content is "基礎梁考慮沈下部材角" or "基礎梁考慮+群杭沈下部材角")
            {
                var vbResults = viewModel.VerticalBeamCaseResults;
                if (vbResults != null && vbResults.Count > 0 && vbResults[0].PileResults != null)
                    foreach (var pr in vbResults[0].PileResults)
                        settlementMap[pr.PileNo] = pr.Settlement_mm / 1000.0;
                if (content == "基礎梁考慮+群杭沈下部材角")
                    foreach (var pile in inputModel.PileLayoutItems)
                        if (settlementMap.ContainsKey(pile.No))
                            settlementMap[pile.No] += pgs.SettlementOf(pile.PileNo) / 1000.0;
            }
            else if (content == "群杭沈下部材角")
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pgs.SettlementOf(pile.PileNo) / 1000.0;
            }
            else if (content == "単杭+群杭沈下部材角")
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pile.SinglePileSettlementVL + pgs.SettlementOf(pile.PileNo) / 1000.0;
            }
            else
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pile.SinglePileSettlementVL;
            }

            // 最も近い基礎梁を探す
            FoundationBeam closestFb = null;
            double closestDist = double.MaxValue;
            const double hitThreshold = 20.0;
            Point closestMid = new();

            foreach (var fb in fbBeams)
            {
                if (!fb.IsVisible) continue;
                var cI = inputModel.GetNodeCoordinates(fb.NodeI_Type, fb.NodeI_Id);
                var cJ = inputModel.GetNodeCoordinates(fb.NodeJ_Type, fb.NodeJ_Id);
                if (cI == null || cJ == null) continue;

                Point pI = viewModel.CanvasThreeDView.Transformation(new Point3D(cI.Value.X, cI.Value.Y, cI.Value.Z));
                Point pJ = viewModel.CanvasThreeDView.Transformation(new Point3D(cJ.Value.X, cJ.Value.Y, cJ.Value.Z));
                var (dist, t) = PointToLineSegmentDistance(mousePos, pI, pJ);

                if (dist < closestDist && dist < hitThreshold)
                {
                    closestDist = dist;
                    closestFb = fb;
                    closestMid = new Point(pI.X * (1 - t) + pJ.X * t, pI.Y * (1 - t) + pJ.Y * t);
                }
            }

            if (closestFb == null) { HideBeamResultTooltip(); return; }

            // 部材角計算
            var coordsI = inputModel.GetNodeCoordinates(closestFb.NodeI_Type, closestFb.NodeI_Id);
            var coordsJ = inputModel.GetNodeCoordinates(closestFb.NodeJ_Type, closestFb.NodeJ_Id);
            int pileNoI = GetPileNoAtCoord(inputModel, coordsI.Value.X, coordsI.Value.Y);
            int pileNoJ = GetPileNoAtCoord(inputModel, coordsJ.Value.X, coordsJ.Value.Y);

            if (!settlementMap.TryGetValue(pileNoI, out double uzI) || !settlementMap.TryGetValue(pileNoJ, out double uzJ))
            { HideBeamResultTooltip(); return; }

            double dx = coordsJ.Value.X - coordsI.Value.X;
            double dy = coordsJ.Value.Y - coordsI.Value.Y;
            double dz = coordsJ.Value.Z - coordsI.Value.Z;
            double beamLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (beamLength < 1e-6) { HideBeamResultTooltip(); return; }

            double angle = (uzI - uzJ) / beamLength;

            int closestFbNo = inputModel.FoundationBeamInput?.GetBeamNo(closestFb) ?? 0;
            string tooltip = $"梁No.{closestFbNo}\n" +
                             $"部材角: {angle:F6} rad\n" +
                             $"  = 1/{(Math.Abs(angle) > 1e-10 ? $"{1.0 / Math.Abs(angle):F0}" : "∞")}\n" +
                             $"沈下I: {uzI * 1000:F2} mm (杭{pileNoI})\n" +
                             $"沈下J: {uzJ * 1000:F2} mm (杭{pileNoJ})\n" +
                             $"梁長: {beamLength:F3} m";

            ShowBeamResultTooltip(mousePos, tooltip, closestMid);
        }

        // ========================================
        // 基礎梁部材角の描画
        // ========================================

        /// <summary>
        /// 基礎梁の部材角 = (Uz_i - Uz_j) / L を描画
        /// AnalysisResultContent に応じて沈下データソースを切替
        /// </summary>
        private void DrawBeamMemberAngle(MainWindowViewModel viewModel)
        {
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            var inputModel = viewModel.ResultInputModel;
            var fbBeams = inputModel?.FoundationBeamInput?.Beams;
            if (fbBeams == null || fbBeams.Count == 0) return;

            string content = viewModel.EffectiveSettlementContent;

            // 杭位置 → 沈下量マップを構築（単位: m）
            var settlementMap = new Dictionary<int, double>();
            var pgs = inputModel.PileGroupSettlement; // PileNo → settlement(m)

            // 沈下量マップ構築（全て m 単位に統一）
            // SinglePileSettlementVL: m, GroupPileSettlement: mm, VB Settlement_mm: mm
            if (content is "基礎梁考慮沈下部材角" or "基礎梁考慮+群杭沈下部材角")
            {
                var vbResults = viewModel.VerticalBeamCaseResults;
                if (vbResults != null && vbResults.Count > 0 && vbResults[0].PileResults != null)
                {
                    foreach (var pr in vbResults[0].PileResults)
                        settlementMap[pr.PileNo] = pr.Settlement_mm / 1000.0; // mm→m
                }
                if (content == "基礎梁考慮+群杭沈下部材角")
                {
                    foreach (var pile in inputModel.PileLayoutItems)
                        if (settlementMap.ContainsKey(pile.No))
                            settlementMap[pile.No] += pgs.SettlementOf(pile.PileNo) / 1000.0; // mm→m
                }
            }
            else if (content == "群杭沈下部材角")
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pgs.SettlementOf(pile.PileNo) / 1000.0; // mm→m
            }
            else if (content == "単杭+群杭沈下部材角")
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pile.SinglePileSettlementVL + pgs.SettlementOf(pile.PileNo) / 1000.0; // m + mm→m
            }
            else // 単杭沈下部材角
            {
                foreach (var pile in inputModel.PileLayoutItems)
                    settlementMap[pile.No] = pile.SinglePileSettlementVL; // m
            }

            if (settlementMap.Count == 0) return;

            string format = "{0:F6}";
            var allValues = new ObservableCollection<double>();
            var drawEntries = new List<(Point3D midPt, Point3D ptI, Point3D ptJ, double angle)>();

            foreach (var fbBeam in fbBeams)
            {
                if (!fbBeam.IsVisible) continue;

                var coordsI = inputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                var coordsJ = inputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                if (coordsI == null || coordsJ == null) continue;

                int pileNoI = GetPileNoAtCoord(inputModel, coordsI.Value.X, coordsI.Value.Y);
                int pileNoJ = GetPileNoAtCoord(inputModel, coordsJ.Value.X, coordsJ.Value.Y);
                if (!settlementMap.TryGetValue(pileNoI, out double uzI)) continue;
                if (!settlementMap.TryGetValue(pileNoJ, out double uzJ)) continue;

                double dx = coordsJ.Value.X - coordsI.Value.X;
                double dy = coordsJ.Value.Y - coordsI.Value.Y;
                double dz = coordsJ.Value.Z - coordsI.Value.Z;
                double beamLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (beamLength < 1e-6) continue;

                double angle = (uzI - uzJ) / beamLength;

                Point3D ptI = new(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                Point3D ptJ = new(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                Point3D midPt = new((ptI.X + ptJ.X) / 2, (ptI.Y + ptJ.Y) / 2, (ptI.Z + ptJ.Z) / 2);

                allValues.Add(angle);
                drawEntries.Add((midPt, ptI, ptJ, angle));
            }

            if (allValues.Count == 0) return;

            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);

            // 梁要素を色付きで描画（Pathを使用してキャンバス再描画時に自動クリアされるようにする）
            foreach (var (midPt, ptI, ptJ, angle) in drawEntries)
            {
                Point p2dI = viewModel.CanvasThreeDView.Transformation(ptI);
                Point p2dJ = viewModel.CanvasThreeDView.Transformation(ptJ);

                var geo = ColorBarUtils.PickColorGeometryInclusiveTop(angle, colorBaredGeometries);
                var brush = geo != null ? new SolidColorBrush(geo.Color) : Brushes.Gray;
                if (brush is SolidColorBrush scb && scb.CanFreeze) scb.Freeze();

                var pathGeo = new PathGeometry();
                pathGeo.AddGeometry(new LineGeometry(p2dI, p2dJ));
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 4.0,
                    Data = pathGeo
                });

                // 値テキスト（小数表示 + "rad"）
                if (viewModel.IsResultValueVisible)
                {
                    Point p2dMid = viewModel.CanvasThreeDView.Transformation(midPt);
                    string text = angle.ToString($"F{viewModel.DecimalPlaces}") + " rad";
                    AddText3D(Brushes.Black, text, p2dMid.X, p2dMid.Y, "C", "C", 0.0);
                }
            }

            // カラーバー
            string title = content;
            ColorBar.DrawStepColorBar(ColorBarCanvas, colorBaredGeometries,
                title, "rad", allValues.Min(), allValues.Max(), format, viewModel.LabelSize);
        }

    }
}
