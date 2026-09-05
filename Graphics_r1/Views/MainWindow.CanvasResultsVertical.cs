using MathNet.Numerics.LinearAlgebra;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
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
    // 基礎梁鉛直解析結果の描画: VB 変形図・沈下色分け・単杭変形図・VB 梁応力。MainWindow.CanvasResults.cs からの物理分割 (純粋移動)。
    public partial class MainWindow
    {
        // ========================================
        // 基礎梁鉛直解析結果の描画
        // ========================================

        /// <summary>
        /// 基礎梁考慮沈下解析の変形後形状を描画（鉛直変位Uzのみ）
        /// colorizeBySettlement=true で沈下量の大きさで色分け、false で半透明グレー単色
        /// </summary>
        private void DrawVBDeformedElements(MainWindowViewModel viewModel, bool colorizeBySettlement = true)
        {
            if (Canvas3DLayout == null) return;

            // VerticalBeamCaseResults または GroupSettlementCaseRecord (基礎梁考慮反復) どちらでも描画可能
            var caseResult = viewModel.GetActiveVerticalBeamCaseResult();
            if (caseResult == null) return;
            var nodeResults = caseResult.NodeResults;
            if (nodeResults == null || nodeResults.Count == 0) return;

            // 節点名→(Uz[m], Rx[rad], Ry[rad]) マップを構築
            var nodeDispMap = new Dictionary<string, (double Uz, double Rx, double Ry)>();
            double maxDisp = 0;
            foreach (var nr in nodeResults)
            {
                double uz_m = nr.Uz_mm / 1000.0;
                nodeDispMap[nr.NodeName] = (uz_m, nr.Rx_rad, nr.Ry_rad);
                double absUz = Math.Abs(uz_m);
                if (absUz > maxDisp) maxDisp = absUz;
            }

            double dispScale = maxDisp > 1e-15
                ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxDisp
                : 0;
            if (dispScale == 0) return;

            var inputModel = viewModel.ResultInputModel;

            // 沈下量の大きさで色分け: 全節点の |Uz| [mm] からカラーバーを生成（バブルと同じ Rainbow）
            // colorizeBySettlement=false の場合はグレーバイアスのダミー colorGeoms にフォールバック
            List<ColorBaredGeometry> colorGeoms;
            if (colorizeBySettlement)
            {
                var allUzMm = nodeDispMap.Values.Select(v => Math.Abs(v.Uz) * 1000.0).ToList();
                colorGeoms = ColorBarUtils.GetColorBarGeometries(
                    allUzMm, steps: 12, mode: ColorBarUtils.ColorBarMode.Rainbow);
            }
            else
            {
                // 半透明グレー単色用のダミー colorGeoms（全値が同じ色を返す）
                colorGeoms = ColorBarUtils.GetMonoColorBarGeometries(
                    BrushSemiTransparentGray.Color, new[] { 0.0, 1.0 });
            }

            // 色バケットごとに PathGeometry を用意
            var pathByColor = new Dictionary<Color, PathGeometry>();
            static PathGeometry GetOrAdd(Dictionary<Color, PathGeometry> d, Color c)
            {
                if (!d.TryGetValue(c, out var g)) { g = new PathGeometry(); d[c] = g; }
                return g;
            }

            // 基礎梁の変形後形状: I/J 端の (Uz, Rx, Ry) から 3次 Hermite 補間で曲線描画
            if (inputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var fbBeam in inputModel.FoundationBeamInput.Beams)
                {
                    if (!IsFoundationBeamVisibleForResult(viewModel, fbBeam)) continue;

                    var coordsI = inputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    var coordsJ = inputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                    if (coordsI == null || coordsJ == null) continue;

                    string nameI = ResolveVBFemNodeName(inputModel, fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    string nameJ = ResolveVBFemNodeName(inputModel, fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);

                    (double Uz, double Rx, double Ry) defaultD = (0.0, 0.0, 0.0);
                    var dI = nameI != null && nodeDispMap.TryGetValue(nameI, out var ri) ? ri : defaultD;
                    var dJ = nameJ != null && nodeDispMap.TryGetValue(nameJ, out var rj) ? rj : defaultD;

                    var dispI = new FEM.NodeDisp(0, 0, dI.Uz, dI.Rx, dI.Ry, 0);
                    var dispJ = new FEM.NodeDisp(0, 0, dJ.Uz, dJ.Rx, dJ.Ry, 0);

                    Point3D pI = new(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                    Point3D pJ = new(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                    var points3D = Common.HermiteBeamInterpolation.GetDeformedPoints(
                        pI, pJ, dispI, dispJ, dispScale);

                    // 梁 I/J の平均 |Uz| で色決定（1 要素 = 1 色）
                    double beamAvgUzMm = 0.5 * (Math.Abs(dI.Uz) + Math.Abs(dJ.Uz)) * 1000.0;
                    Color beamColor = PickColorFromGeoms(beamAvgUzMm, colorGeoms, Colors.Red);
                    var beamGeo = GetOrAdd(pathByColor, beamColor);

                    for (int k = 0; k < points3D.Count - 1; k++)
                    {
                        Point p1 = viewModel.CanvasThreeDView.Transformation(points3D[k]);
                        Point p2 = viewModel.CanvasThreeDView.Transformation(points3D[k + 1]);
                        if (!double.IsFinite(p1.X) || !double.IsFinite(p1.Y) ||
                            !double.IsFinite(p2.X) || !double.IsFinite(p2.Y))
                            continue;
                        beamGeo.AddGeometry(new LineGeometry(p1, p2));
                    }
                }
            }

            // 杭体の変形後形状: 杭頭荷重 (VB の Reaction_kN) で単杭沈下曲線から per-node Uz を取得し、
            // 各杭節点の位置を決定する（剛体並進ではない）
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            var perPileDeform = ComputeVBPerPileDeformation(viewModel, caseResult, dispScale);

            foreach (var (pile, zs, uzsSpPositiveDown) in perPileDeform)
            {
                // 接合節点 → 杭頭（剛体リンク部分）の区間を rigid で描く
                // v2 セマンティクス: pile.Z は接合節点 Z
                string connName = $"FoundationNode-P{pile.No}";
                double uzTopVb = nodeDispMap.TryGetValue(connName, out var r) ? r.Uz : 0; // VB 規約: 負=下向き
                double connectionZ = pile.Z;
                Point3D connPt3D = new(pile.X, pile.Y, connectionZ + uzTopVb * dispScale);

                Point prev2D = viewModel.CanvasThreeDView.Transformation(connPt3D);
                double prevUzMm = Math.Abs(uzTopVb) * 1000.0;

                for (int i = 0; i < zs.Count; i++)
                {
                    // 単杭規約（正=下向き）→ 3D Z: 減算
                    Point3D pt3D = new(pile.X, pile.Y, zs[i] - uzsSpPositiveDown[i] * dispScale);
                    Point cur2D = viewModel.CanvasThreeDView.Transformation(pt3D);

                    if (double.IsFinite(prev2D.X) && double.IsFinite(prev2D.Y) &&
                        double.IsFinite(cur2D.X) && double.IsFinite(cur2D.Y))
                    {
                        double curUzMm = Math.Abs(uzsSpPositiveDown[i]) * 1000.0;
                        Color segColor = PickColorFromGeoms(0.5 * (prevUzMm + curUzMm), colorGeoms, Colors.Red);
                        GetOrAdd(pathByColor, segColor).AddGeometry(new LineGeometry(prev2D, cur2D));
                    }
                    prev2D = cur2D;
                    prevUzMm = Math.Abs(uzsSpPositiveDown[i]) * 1000.0;
                }
            }

            foreach (var (color, geo) in pathByColor)
            {
                if (geo.Figures.Count == 0) continue;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    Data = geo
                });
            }

            // 一般梁形状フラグ ON: 基礎梁断面を Hermite 点列から描画（沈下量で色分け）
            if (viewModel.IsBeamElementSectionVisible)
                DrawVBDeformedBeamSections(viewModel, nodeDispMap, dispScale, colorGeoms);

            // 杭形状フラグ ON: 杭体断面を per-node Uz で描画（沈下量で色分け）
            if (viewModel.IsPileSectionVisible)
                DrawVBDeformedPileSections(viewModel, perPileDeform, dispScale, colorGeoms);
        }

        /// <summary>
        /// VB 解析モードで、各杭の杭頭荷重から単杭沈下曲線の per-node Uz を取得して返す。
        /// </summary>
        private static List<(PileLayoutDataItem pile, List<double> nodeZ, List<double> uzSpPositiveDown)>
            ComputeVBPerPileDeformation(
                MainWindowViewModel viewModel,
                FEM.VerticalBeamCaseResult caseResult,
                double dispScale)
        {
            var result = new List<(PileLayoutDataItem, List<double>, List<double>)>();
            var inputModel = viewModel.ResultInputModel;
            var soilPiles = inputModel?.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return result;

            foreach (var pile in inputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;

                int spIdx = pile.SoilPileAltNo - 1;
                if (spIdx < 0 || spIdx >= soilPiles.Count) continue;
                var soilPile = soilPiles[spIdx];
                var circumVerticals = soilPile.PileCircumVerticals;
                if (circumVerticals == null || circumVerticals.Count == 0) continue;

                var pr = caseResult.PileResults?.FirstOrDefault(p => p.PileNo == pile.No);
                if (pr == null) continue;

                var dispVector = soilPile.GetFullDisplacementForLoad(pr.Reaction_kN);
                int pileNodesCount = circumVerticals.Count + 1;

                var zs = new List<double>(pileNodesCount);
                var uzs = new List<double>(pileNodesCount);
                for (int i = 0; i < pileNodesCount; i++)
                {
                    double nodeZ = (i == 0) ? pile.PileHeadZ : circumVerticals[i - 1].Bottom;
                    double uz = dispVector != null ? dispVector[2 * i] : 0; // m, 正=下向き
                    zs.Add(nodeZ);
                    uzs.Add(uz);
                }
                result.Add((pile, zs, uzs));
            }
            return result;
        }

        /// <summary>
        /// VB 解析モードで 基礎梁断面の変形後形状を描画する（Hermite 曲線に沿った箱断面／沈下量で色分け）
        /// </summary>
        private void DrawVBDeformedBeamSections(
            MainWindowViewModel viewModel,
            Dictionary<string, (double Uz, double Rx, double Ry)> nodeDispMap,
            double dispScale,
            List<ColorBaredGeometry> colorGeoms)
        {
            var inputModel = viewModel.ResultInputModel;
            var fbInput = inputModel?.FoundationBeamInput;
            if (fbInput?.Beams == null || fbInput.Sections == null) return;

            // SectionNo (1-based 位置インデックス) → BeamSection マップ
            var secDict = new Dictionary<int, BeamSection>();
            for (int i = 0; i < fbInput.Sections.Count; i++)
                secDict[i + 1] = fbInput.Sections[i];
            var transform = viewModel.CanvasThreeDView;
            var pathByColor = new Dictionary<Color, PathGeometry>();

            foreach (var fbBeam in fbInput.Beams)
            {
                if (!IsFoundationBeamVisibleForResult(viewModel, fbBeam)) continue;

                double bw = fbBeam.Width;
                double bh = fbBeam.Height;
                if (secDict.TryGetValue(fbBeam.SectionNo, out var sec))
                {
                    bw = sec.Width;
                    bh = sec.Height;
                }
                if (bw <= 0 || bh <= 0) continue;

                var coordsI = inputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                var coordsJ = inputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                if (coordsI == null || coordsJ == null) continue;

                string nameI = ResolveVBFemNodeName(inputModel, fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                string nameJ = ResolveVBFemNodeName(inputModel, fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);

                (double Uz, double Rx, double Ry) defaultD = (0.0, 0.0, 0.0);
                var dI = nameI != null && nodeDispMap.TryGetValue(nameI, out var ri) ? ri : defaultD;
                var dJ = nameJ != null && nodeDispMap.TryGetValue(nameJ, out var rj) ? rj : defaultD;

                var dispI = new FEM.NodeDisp(0, 0, dI.Uz, dI.Rx, dI.Ry, 0);
                var dispJ = new FEM.NodeDisp(0, 0, dJ.Uz, dJ.Rx, dJ.Ry, 0);

                Point3D pI = new(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                Point3D pJ = new(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                var points3D = Common.HermiteBeamInterpolation.GetDeformedPoints(
                    pI, pJ, dispI, dispJ, dispScale);

                // I/J 端の平均 |Uz| [mm] で 1 要素 1 色
                double avgUzMm = 0.5 * (Math.Abs(dI.Uz) + Math.Abs(dJ.Uz)) * 1000.0;
                Color color = PickColorFromGeoms(avgUzMm, colorGeoms, Colors.Red);
                if (!pathByColor.TryGetValue(color, out var geo))
                {
                    geo = new PathGeometry();
                    pathByColor[color] = geo;
                }
                AddBeamSectionGeometryFromPoints(geo, points3D, bw, bh, fbBeam.AngleBeta, transform);
            }

            foreach (var (color, geo) in pathByColor)
            {
                if (geo.IsEmpty()) continue;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 0.7,
                    Data = geo
                });
            }
        }

        /// <summary>
        /// VB 解析モードで 杭体断面の変形後形状を描画する。
        /// 各杭の杭頭荷重から単杭沈下曲線の per-node Uz を求めて各節点位置を決定し、沈下量で色分けする。
        /// </summary>
        private void DrawVBDeformedPileSections(
            MainWindowViewModel viewModel,
            List<(PileLayoutDataItem pile, List<double> nodeZ, List<double> uzSpPositiveDown)> perPileDeform,
            double dispScale,
            List<ColorBaredGeometry> colorGeoms)
        {
            if (perPileDeform.Count == 0) return;

            var inputModel = viewModel.ResultInputModel;
            var soilPiles = inputModel?.ElementDivision?.SoilPiles;
            if (soilPiles == null) return;

            var transform = viewModel.CanvasThreeDView;
            double flattening = transform.Flattening;
            double scale = transform.Scale;
            var pathByColor = new Dictionary<Color, PathGeometry>();

            foreach (var (pile, zs, uzs) in perPileDeform)
            {
                int spIdx = pile.SoilPileAltNo - 1;
                if (spIdx < 0 || spIdx >= soilPiles.Count) continue;
                var circumVerticals = soilPiles[spIdx].PileCircumVerticals;
                if (circumVerticals == null || circumVerticals.Count == 0) continue;

                for (int i = 0; i < circumVerticals.Count; i++)
                {
                    if (i + 1 >= zs.Count) break;
                    double segDia = circumVerticals[i].D;
                    if (segDia <= 0) continue;

                    // 区間 i は node i（上端）→ node i+1（下端）。単杭規約: 正=下向き → 3D Z に減算
                    var points3D = new List<Point3D>
                    {
                        new(pile.X, pile.Y, zs[i]     - uzs[i]     * dispScale),
                        new(pile.X, pile.Y, zs[i + 1] - uzs[i + 1] * dispScale)
                    };

                    double avgUzMm = 0.5 * (Math.Abs(uzs[i]) + Math.Abs(uzs[i + 1])) * 1000.0;
                    Color color = PickColorFromGeoms(avgUzMm, colorGeoms, Colors.Red);
                    if (!pathByColor.TryGetValue(color, out var geo))
                    {
                        geo = new PathGeometry();
                        pathByColor[color] = geo;
                    }

                    AddPileSegmentSectionGeometryFromPoints(
                        geo, points3D, segDia, transform, flattening, scale,
                        drawTopEllipse: i == 0,
                        drawBottomEllipse: true);
                }
            }

            foreach (var (color, geo) in pathByColor)
            {
                if (geo.IsEmpty()) continue;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 0.7,
                    Data = geo
                });
            }
        }

        /// <summary>
        /// 単杭沈下モードの変形後形状を描画する。
        /// 各杭について、選択荷重ケースに対応する軸力で <see cref="SoilPile.GetFullDisplacementForLoad"/>
        /// から各節点の鉛直変位ベクトルを取得し、ポリラインで変形形状を描く。
        /// </summary>
        private void DrawSinglePileDeformedElements(MainWindowViewModel viewModel, bool colorizeBySettlement = true)
        {
            if (Canvas3DLayout == null) return;
            var inputModel = viewModel.ResultInputModel;
            if (inputModel == null) return;
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            // 1 巡目: 最大変位を取得してスケールを決定
            double maxDisp = 0;
            var cached = new List<(PileLayoutDataItem pile, List<double> nodeZ, List<double> uz)>();
            foreach (var pile in inputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;
                double? forceOpt = GetSelectedCaseAxialForce(pile, viewModel);
                if (forceOpt == null) continue;

                int spIdx = pile.SoilPileAltNo - 1;
                if (spIdx < 0 || spIdx >= soilPiles.Count) continue;
                var soilPile = soilPiles[spIdx];

                var dispVector = soilPile.GetFullDisplacementForLoad(forceOpt.Value);
                if (dispVector == null) continue;

                var circumVerticals = soilPile.PileCircumVerticals;
                if (circumVerticals == null || circumVerticals.Count == 0) continue;
                int pileNodesCount = circumVerticals.Count + 1;

                var zs = new List<double>(pileNodesCount);
                var uzs = new List<double>(pileNodesCount);
                for (int i = 0; i < pileNodesCount; i++)
                {
                    double nodeZ = (i == 0) ? pile.PileHeadZ : circumVerticals[i - 1].Bottom;
                    double uz = dispVector[2 * i]; // m
                    zs.Add(nodeZ);
                    uzs.Add(uz);
                    if (Math.Abs(uz) > maxDisp) maxDisp = Math.Abs(uz);
                }
                cached.Add((pile, zs, uzs));
            }

            double dispScale = maxDisp > 1e-15
                ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxDisp
                : 0;
            if (dispScale == 0 || cached.Count == 0) return;

            // 沈下量の大きさで色分け: 全節点の |uz| [mm] からカラーバーを生成（バブルと同じ Rainbow）
            // colorizeBySettlement=false の場合は半透明グレー単色にフォールバック
            List<ColorBaredGeometry> colorGeoms;
            if (colorizeBySettlement)
            {
                var allUzMm = cached.SelectMany(c => c.uz).Select(u => Math.Abs(u) * 1000.0).ToList();
                colorGeoms = ColorBarUtils.GetColorBarGeometries(
                    allUzMm, steps: 12, mode: ColorBarUtils.ColorBarMode.Rainbow);
            }
            else
            {
                colorGeoms = ColorBarUtils.GetMonoColorBarGeometries(
                    BrushSemiTransparentGray.Color, new[] { 0.0, 1.0 });
            }

            // 色バケットごとに PathGeometry を用意して Path 数を抑える
            var pathByColor = new Dictionary<Color, PathGeometry>();

            // 単杭沈下解析の uz は正=下向き（沈下）なので 3D 座標（Z は上向き正）に適用する際は減算する
            foreach (var (pile, zs, uzs) in cached)
            {
                for (int i = 0; i < zs.Count - 1; i++)
                {
                    Point3D a = new(pile.X, pile.Y, zs[i] - uzs[i] * dispScale);
                    Point3D b = new(pile.X, pile.Y, zs[i + 1] - uzs[i + 1] * dispScale);
                    Point pa = viewModel.CanvasThreeDView.Transformation(a);
                    Point pb = viewModel.CanvasThreeDView.Transformation(b);
                    if (!double.IsFinite(pa.X) || !double.IsFinite(pa.Y) ||
                        !double.IsFinite(pb.X) || !double.IsFinite(pb.Y))
                        continue;

                    double segValueMm = 0.5 * (Math.Abs(uzs[i]) + Math.Abs(uzs[i + 1])) * 1000.0;
                    Color segColor = PickColorFromGeoms(segValueMm, colorGeoms, Colors.Red);

                    if (!pathByColor.TryGetValue(segColor, out var geo))
                    {
                        geo = new PathGeometry();
                        pathByColor[segColor] = geo;
                    }
                    geo.AddGeometry(new LineGeometry(pa, pb));
                }
            }

            foreach (var (color, geo) in pathByColor)
            {
                if (geo.Figures.Count == 0) continue;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    Data = geo
                });
            }

            // 杭形状フラグ ON: 各杭区間を per-node Uz で変形させた筒断面で描画（沈下量で色分け）
            if (viewModel.IsPileSectionVisible)
                DrawSinglePileDeformedPileSections(viewModel, cached, dispScale, colorGeoms);
        }

        /// <summary>
        /// 値をカラーバーのビンから探し、色を返す（範囲外は fallback）
        /// </summary>
        private static Color PickColorFromGeoms(double value, List<ColorBaredGeometry> geoms, Color fallback)
        {
            if (geoms == null || geoms.Count == 0) return fallback;
            var picked = ColorBarUtils.PickColorGeometryInclusiveTop(value, geoms);
            if (picked != null) return picked.Color;
            // 範囲外は端の色
            if (value < geoms[0].BottomRange) return geoms[0].Color;
            return geoms[^1].Color;
        }

        /// <summary>
        /// 単杭沈下モードで杭体断面を per-node Uz で変形させて描画する（沈下量で色分け）。
        /// </summary>
        private void DrawSinglePileDeformedPileSections(
            MainWindowViewModel viewModel,
            List<(PileLayoutDataItem pile, List<double> nodeZ, List<double> uz)> cached,
            double dispScale,
            List<ColorBaredGeometry> colorGeoms)
        {
            var inputModel = viewModel.ResultInputModel;
            var soilPiles = inputModel?.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            var transform = viewModel.CanvasThreeDView;
            double flattening = transform.Flattening;
            double scale = transform.Scale;
            var pathByColor = new Dictionary<Color, PathGeometry>();

            foreach (var (pile, zs, uzs) in cached)
            {
                int spIdx = pile.SoilPileAltNo - 1;
                if (spIdx < 0 || spIdx >= soilPiles.Count) continue;
                var soilPile = soilPiles[spIdx];
                var circumVerticals = soilPile.PileCircumVerticals;
                if (circumVerticals == null || circumVerticals.Count == 0) continue;

                // 区間 i は node i (上端) ～ node i+1 (下端) をつなぐ
                for (int i = 0; i < circumVerticals.Count; i++)
                {
                    if (i + 1 >= zs.Count) break;
                    double segDia = circumVerticals[i].D;
                    if (segDia <= 0) continue;

                    var points3D = new List<Point3D>
                    {
                        new(pile.X, pile.Y, zs[i] - uzs[i] * dispScale),
                        new(pile.X, pile.Y, zs[i + 1] - uzs[i + 1] * dispScale)
                    };

                    double avgUzMm = 0.5 * (Math.Abs(uzs[i]) + Math.Abs(uzs[i + 1])) * 1000.0;
                    Color color = PickColorFromGeoms(avgUzMm, colorGeoms, Colors.Red);
                    if (!pathByColor.TryGetValue(color, out var geo))
                    {
                        geo = new PathGeometry();
                        pathByColor[color] = geo;
                    }

                    AddPileSegmentSectionGeometryFromPoints(
                        geo, points3D, segDia, transform, flattening, scale,
                        drawTopEllipse: i == 0,
                        drawBottomEllipse: true);
                }
            }

            foreach (var (color, geo) in pathByColor)
            {
                if (geo.IsEmpty()) continue;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 0.7,
                    Data = geo
                });
            }
        }

        /// <summary>
        /// 指定座標に最も近い杭のNoを返す
        /// </summary>
        private static int GetPileNoAtCoord(InputModel inputModel, double x, double y)
        {
            int bestNo = 0;
            double bestDist = double.MaxValue;
            foreach (var pile in inputModel.PileLayoutItems)
            {
                double dx = pile.X - x;
                double dy = pile.Y - y;
                double dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestNo = pile.No;
                }
            }
            return bestNo;
        }

        /// <summary>
        /// VB解析のFEM節点名を解決する（VerticalBeamModelling.GetFemNodeNameと同じ命名規則）
        /// </summary>
        private static string ResolveVBFemNodeName(InputModel inputModel, NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.FoundationNode:
                    var fnode = inputModel.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? $"FoundationNode-{fnode.No}" : null;
                case NodeReferenceType.PileLayout:
                    var pile = inputModel.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    return pile != null ? $"FoundationNode-P{pile.No}" : null;
                case NodeReferenceType.GeneralNode:
                    var gnode = inputModel.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return gnode != null ? $"InputNode-{gnode.No}" : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 基礎梁考慮沈下梁応力 / 基礎梁考慮沈下 / 基礎梁考慮反力 を描画
        /// (個別矩形（基礎梁考慮） の CaseRecord も同じパスで描画される)
        /// </summary>
        private void DrawVerticalBeamResults(MainWindowViewModel viewModel)
        {
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;
            var caseResult = viewModel.GetActiveVerticalBeamCaseResult();
            if (caseResult == null) return;
            string format = "{0:N" + viewModel.DecimalPlaces + "}";
            DrawVBBeamForce(viewModel, caseResult, format);
        }

        /// <summary>
        /// 基礎梁考慮沈下/反力のデータを _pendingSettlement に準備し、
        /// 沈下/単杭と同じ DrawBubbleAndArrow で描画する
        /// </summary>
        /// <summary>
        /// VB 解析の LoadCaseName は "1-1: U1" / "2-1: U6" / "VL (常時+追加)" のように
        /// 装飾付きで保存されているため、ベース名（U1 / U6 / VL）を抽出する。
        /// </summary>
        private static string ExtractVBCaseBaseName(string caseName)
        {
            if (string.IsNullOrEmpty(caseName)) return caseName ?? string.Empty;

            // "1-1: U1" → "U1"
            int colonIdx = caseName.IndexOf(": ", StringComparison.Ordinal);
            string stripped = colonIdx >= 0 ? caseName[(colonIdx + 2)..].Trim() : caseName;

            // "VL (常時+追加)" → "VL"
            int parenIdx = stripped.IndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0) stripped = stripped[..parenIdx].Trim();

            return stripped;
        }

        private void PrepareVBSettlementPending(MainWindowViewModel viewModel)
        {
            var caseResults = viewModel.VerticalBeamCaseResults;
            if (caseResults == null || caseResults.Count == 0) return;

            // 選択中の荷重ケース名に対応する VB 結果を取得（なければ先頭にフォールバック）
            string selectedName = viewModel.SelectedLoadCaseName;
            var caseResult = caseResults.FirstOrDefault(c => ExtractVBCaseBaseName(c.LoadCaseName) == selectedName)
                             ?? caseResults[0];
            string content = viewModel.EffectiveSettlementContent;

            ObservableCollection<Point3D> points = [];
            ObservableCollection<double> values = [];
            string title;
            string unit;

            if (content is "基礎梁考慮沈下" or "基礎梁考慮+群杭沈下")
            {
                title = content == "基礎梁考慮+群杭沈下" ? "基礎梁考慮+群杭沈下" : "基礎梁考慮沈下";
                unit = "mm";

                // 杭位置の沈下量（LoadingPlaneAltitudeで統一、単杭沈下と同じ高さ）
                double loadingPlaneAlt = viewModel.ResultInputModel.PileGroupSettlement.LoadingPlaneAltitude;
                var pileResults = caseResult.PileResults;
                if (pileResults != null)
                {
                    foreach (var pr in pileResults)
                    {
                        var pile = viewModel.ResultInputModel.PileLayoutItems.FirstOrDefault(p => p.No == pr.PileNo);
                        if (pile == null || !IsPileVisibleForResult(viewModel, pile)) continue;
                        points.Add(new Point3D(pr.X, pr.Y, loadingPlaneAlt));
                        values.Add(Math.Abs(pr.Settlement_mm));
                    }
                }

                // 基礎梁考慮+群杭: VB沈下量に群杭沈下量を加算して表示
                if (content == "基礎梁考慮+群杭沈下")
                {
                    // VBのPileResult沈下量に群杭沈下量を加算した値で杭位置のバブルを追加
                    // v2 セマンティクス: pile.Z は接合節点 Z
                    foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        if (!IsPileVisibleForResult(viewModel, pile)) continue;
                        var pr = caseResult.PileResults?.FirstOrDefault(p => p.PileNo == pile.No);
                        double vbSettlement = pr?.Settlement_mm ?? 0;
                        double combined = Math.Abs(vbSettlement
                            + viewModel.ResultInputModel.PileGroupSettlement.SettlementOf(pile.PileNo)); // 両方mm
                        double connectionZ = pile.Z;
                        points.Add(new Point3D(pile.X, pile.Y, connectionZ));
                        values.Add(combined);
                    }
                }
            }
            else if (content == "基礎梁考慮反力（杭頭集約）")
            {
                // 杭頭集約: VB 解析の杭頭反力のみを基礎梁連結レベルに表示
                title = "基礎梁考慮反力（杭頭集約）";
                unit = "kN";

                var pileResults = caseResult.PileResults;
                if (pileResults != null)
                {
                    foreach (var pr in pileResults)
                    {
                        var pile = viewModel.ResultInputModel.PileLayoutItems.FirstOrDefault(p => p.No == pr.PileNo);
                        if (pile == null || !IsPileVisibleForResult(viewModel, pile)) continue;

                        // v2 セマンティクス: pile.Z は接合節点 Z
                        points.Add(new Point3D(pr.X, pr.Y, pile.Z));
                        values.Add(Math.Abs(pr.Reaction_kN));
                    }
                }
            }
            else // 基礎梁考慮反力（地盤）
            {
                // 地盤: 各杭の各節点における地盤→杭への反力分布のみ表示
                title = "基礎梁考慮反力（地盤）";
                unit = "kN";

                // 杭の各節点の反力分布のみ追加（杭頭集約バブルは出さない）
                AddPerPileNodeData(viewModel, caseResult, points, values, isDisplacement: false);
            }

            if (points.Count > 0)
            {
                _pendingSettlementPoints = points;
                _pendingSettlementValues = values;
                _pendingSettlementTitle = title;
                _pendingSettlementUnit = unit;
            }
        }

        /// <summary>
        /// 選択中の荷重ケースに対応する杭頭作用軸力を取得する。
        /// VL: AxialForceVL0 + AxialForceVLAdditional,
        /// Level1/Level2: 各 LoadCase の LoadName と一致する index の値。
        /// </summary>
        private static double? GetSelectedCaseAxialForce(PileLayoutDataItem pile, MainWindowViewModel vm)
        {
            string caseName = vm.SelectedLoadCaseName;
            if (caseName == "VL")
                return pile.AxialForceVL0 + pile.AxialForceVLAdditional;

            var lcs = vm.ResultInputModel?.LoadCasesInput;
            if (lcs == null) return null;

            for (int i = 0; i < lcs.LoadCasesLevel1.Count; i++)
                if (lcs.LoadCasesLevel1[i].LoadName == caseName && i < pile.AxialForceLevel1s.Count)
                    return pile.AxialForceLevel1s[i];
            for (int i = 0; i < lcs.LoadCasesLevel2.Count; i++)
                if (lcs.LoadCasesLevel2[i].LoadName == caseName && i < pile.AxialForceLevel2s.Count)
                    return pile.AxialForceLevel2s[i];
            return null;
        }

        /// <summary>
        /// 単杭沈下解析結果から杭頭集約または地盤節点反力分布を _pendingSettlement に準備する。
        /// 杭頭集約: 選択荷重ケースに対応する軸力そのものを杭頭（載荷面）高さに表示。
        /// 地盤: 同じ軸力で SoilPile.GetFullReactionForLoad を引き、各節点の地盤反力を表示。
        /// </summary>
        private void PrepareSinglePileReactionPending(MainWindowViewModel viewModel)
        {
            var inputModel = viewModel.ResultInputModel;
            if (inputModel == null) return;

            string content = viewModel.EffectiveSettlementContent;
            bool isPerNode = content == "単杭反力（地盤）";

            ObservableCollection<Point3D> points = [];
            ObservableCollection<double> values = [];
            string title = content;
            string unit = "kN";

            if (!isPerNode)
            {
                // 杭頭集約: 各杭の選択荷重ケース軸力を載荷面高さに表示
                double loadingPlaneAlt = inputModel.PileGroupSettlement.LoadingPlaneAltitude;
                foreach (var pile in inputModel.PileLayoutItems)
                {
                    if (!IsPileVisibleForResult(viewModel, pile)) continue;
                    double? forceOpt = GetSelectedCaseAxialForce(pile, viewModel);
                    if (forceOpt == null) continue;
                    points.Add(new Point3D(pile.X, pile.Y, loadingPlaneAlt));
                    values.Add(Math.Abs(forceOpt.Value));
                }
            }
            else
            {
                // 地盤: 各杭の各節点反力を SoilPile.GetFullReactionForLoad(軸力) から取得
                var soilPiles = inputModel.ElementDivision?.SoilPiles;
                if (soilPiles == null || soilPiles.Count == 0) return;

                foreach (var pile in inputModel.PileLayoutItems)
                {
                    if (!IsPileVisibleForResult(viewModel, pile)) continue;
                    double? forceOpt = GetSelectedCaseAxialForce(pile, viewModel);
                    if (forceOpt == null) continue;

                    int soilPileIdx = pile.SoilPileAltNo - 1;
                    if (soilPileIdx < 0 || soilPileIdx >= soilPiles.Count) continue;
                    var soilPile = soilPiles[soilPileIdx];

                    var reactionVector = soilPile.GetFullReactionForLoad(forceOpt.Value);
                    if (reactionVector == null)
                    {
                        Serilog.Log.Debug($"[SinglePile-PerNode] reactionVector null for pile={pile.No}, force={forceOpt.Value}");
                        continue;
                    }

                    var circumVerticals = soilPile.PileCircumVerticals;
                    if (circumVerticals == null || circumVerticals.Count == 0) continue;

                    int pileNodesCount = circumVerticals.Count + 1;
                    for (int i = 0; i < pileNodesCount; i++)
                    {
                        double nodeZ = (i == 0) ? pile.PileHeadZ : circumVerticals[i - 1].Bottom;
                        double nodeValue = Math.Abs(reactionVector[2 * i]);
                        points.Add(new Point3D(pile.X, pile.Y, nodeZ));
                        values.Add(nodeValue);
                    }
                }
            }

            if (points.Count > 0)
            {
                _pendingSettlementPoints = points;
                _pendingSettlementValues = values;
                _pendingSettlementTitle = title;
                _pendingSettlementUnit = unit;
            }
        }

        /// <summary>
        /// 杭の各節点の変位分布または反力（軸力）分布データを追加する
        /// VB解析で得た杭頭反力に対応する単杭解析の全節点データを線形補間で取得
        /// </summary>
        private static void AddPerPileNodeData(
            MainWindowViewModel viewModel,
            FEM.VerticalBeamCaseResult caseResult,
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            bool isDisplacement)
        {
            var pileResults = caseResult.PileResults;
            if (pileResults == null) { Serilog.Log.Debug("[VB-PerNode] pileResults is null"); return; }

            var inputModel = viewModel.ResultInputModel;
            var soilPiles = inputModel?.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) { Serilog.Log.Debug("[VB-PerNode] soilPiles null or empty"); return; }

            Serilog.Log.Debug($"[VB-PerNode] soilPiles.Count={soilPiles.Count}, pileResults.Count={pileResults.Count}");

            foreach (var pr in pileResults)
            {
                var pile = inputModel.PileLayoutItems.FirstOrDefault(p => p.No == pr.PileNo);
                if (pile == null || !IsPileVisibleForResult(viewModel, pile)) { Serilog.Log.Debug($"[VB-PerNode] pile not found or invisible: PileNo={pr.PileNo}"); continue; }

                // SoilPileの取得
                int soilPileIdx = pile.SoilPileAltNo - 1;
                if (soilPileIdx < 0 || soilPileIdx >= soilPiles.Count) { Serilog.Log.Debug($"[VB-PerNode] soilPileIdx out of range: {soilPileIdx}"); continue; }
                var soilPile = soilPiles[soilPileIdx];

                Serilog.Log.Debug($"[VB-PerNode] pile={pile.No}, soilPileIdx={soilPileIdx}, " +
                    $"NodeDisplacements={soilPile.NodeDisplacements?.Count}, " +
                    $"LoadDisplacements={soilPile.LoadDisplacements?.Count}, " +
                    $"CircumVerticals={soilPile.PileCircumVerticals?.Count}");

                // VB解析の杭頭反力に対応する全節点ベクトルを取得
                double pileTopForce = pr.Reaction_kN;
                var dispVector = soilPile.GetFullDisplacementForLoad(pileTopForce);
                if (dispVector == null) { Serilog.Log.Debug($"[VB-PerNode] dispVector is null for force={pileTopForce}"); continue; }

                // 反力表示時は節点反力ベクトル（地盤から杭への力）も取得
                MathNet.Numerics.LinearAlgebra.Vector<double>? reactionVector = null;
                if (!isDisplacement)
                {
                    reactionVector = soilPile.GetFullReactionForLoad(pileTopForce);
                    if (reactionVector == null)
                    {
                        Serilog.Log.Debug($"[VB-PerNode] reactionVector is null for force={pileTopForce} — 再解析が必要な可能性");
                        continue;
                    }
                }

                var circumVerticals = soilPile.PileCircumVerticals;
                if (circumVerticals == null || circumVerticals.Count == 0) continue;

                int pileNodesCount = circumVerticals.Count + 1;

                // 杭頭ノード（index=0）はVB解析結果と重複するため、index=1から開始
                for (int i = 1; i < pileNodesCount; i++)
                {
                    double nodeZ = circumVerticals[i - 1].Bottom;
                    double nodeValue;

                    if (isDisplacement)
                    {
                        // 杭節点変位（偶数インデックス）をmm単位に変換
                        nodeValue = Math.Abs(dispVector[2 * i]) * 1000.0;
                    }
                    else
                    {
                        // 各杭節点の地盤反力（偶数インデックス = 杭 DOF）[kN]
                        // 単杭沈下解析の VectorRz から線形補間で取得
                        nodeValue = Math.Abs(reactionVector![2 * i]);
                    }

                    points.Add(new Point3D(pile.X, pile.Y, nodeZ));
                    values.Add(nodeValue);
                }
            }
        }


        /// <summary>
        /// 基礎梁考慮沈下梁応力: 各梁要素にダイアグラムで応力を表示
        /// AnalysisResultBeamForceType に応じて Fx/Fy/Fz/Mx/My/Mz/Fh/Mh を切替
        /// </summary>
        private void DrawVBBeamForce(MainWindowViewModel viewModel, FEM.VerticalBeamCaseResult caseResult, string format)
        {
            var beamResults = caseResult.BeamResults;
            if (beamResults == null || beamResults.Count == 0) return;

            string forceType = viewModel.AnalysisResultBeamForceType;
            bool isDerived = forceType == "Mh" || forceType == "Fh";

            // 応力種別ごとの方向ベクトル・単位（水平解析の梁応力と同じ）
            Vector<double> forceDirection;
            string unit;
            switch (forceType)
            {
                case "Fx": forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]); unit = "kN"; break;
                case "Fy": forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); unit = "kN"; break;
                case "Fz": forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]); unit = "kN"; break;
                case "Mx": forceDirection = Vector<double>.Build.DenseOfArray([0, 0, 1]); unit = "kNm"; break;
                case "My": forceDirection = Vector<double>.Build.DenseOfArray([0, 0, -1]); unit = "kNm"; break;
                case "Mz": forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); unit = "kNm"; break;
                case "Fh": forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); unit = "kN"; break;
                case "Mh": forceDirection = Vector<double>.Build.DenseOfArray([0, 1, 0]); unit = "kNm"; break;
                default: forceDirection = Vector<double>.Build.DenseOfArray([0, 0, -1]); unit = "kNm"; break;
            }

            // VerticalBeamBeamResult から指定成分を取得するローカル関数
            // 合成（Mh/Fh）の場合は磁気値（正符号）を返し、J端は符号反転して返す
            static (double fi, double fj) GetForceComponent(FEM.VerticalBeamBeamResult br, string type)
            {
                return type switch
                {
                    "Fx" => (br.Ni, br.Nj),
                    "Fy" => (br.Qyi, br.Qyj),
                    "Fz" => (br.Qzi, br.Qzj),
                    "Mx" => (br.Mxi, br.Mxj),
                    "My" => (br.Myi, br.Myj),
                    "Mz" => (br.Mzi, br.Mzj),
                    "Fh" => (Math.Sqrt(br.Qyi * br.Qyi + br.Qzi * br.Qzi),
                             -Math.Sqrt(br.Qyj * br.Qyj + br.Qzj * br.Qzj)),
                    "Mh" => (Math.Sqrt(br.Myi * br.Myi + br.Mzi * br.Mzi),
                             -Math.Sqrt(br.Myj * br.Myj + br.Mzj * br.Mzj)),
                    _ => (br.Myi, br.Myj),
                };
            }

            // 1) 全梁要素の応力値を収集（符号付き、カラーバー用）
            var allValues = new ObservableCollection<double>();
            double maxAbsValue = 0;
            var drawEntries = new List<(FEM.VerticalBeamBeamResult br, Point3D nodeI3D, Point3D nodeJ3D, Vector3D beamDir)>();

            foreach (var br in beamResults)
            {
                if (!br.BeamName.StartsWith("FoundationBeam-")) continue;

                var beamNoStr = br.BeamName.Replace("FoundationBeam-", "");
                if (!int.TryParse(beamNoStr, out int beamNo)) continue;

                // beamNo (1-based) は Beams コレクション内の位置インデックス + 1
                var fbBeams = viewModel.ResultInputModel.FoundationBeamInput?.Beams;
                var fbBeam = (fbBeams != null && beamNo >= 1 && beamNo <= fbBeams.Count)
                    ? fbBeams[beamNo - 1] : null;
                if (fbBeam == null || !IsFoundationBeamVisibleForResult(viewModel, fbBeam)) continue;

                var coordsI = viewModel.ResultInputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                var coordsJ = viewModel.ResultInputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                if (coordsI == null || coordsJ == null) continue;

                Point3D nodeI3D = new(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                Point3D nodeJ3D = new(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                var beamDir = new Vector3D(nodeJ3D.X - nodeI3D.X, nodeJ3D.Y - nodeI3D.Y, nodeJ3D.Z - nodeI3D.Z);

                var (fi, fj) = GetForceComponent(br, forceType);
                if (!double.IsFinite(fi) || !double.IsFinite(fj)) continue;

                // 合成量（Mh/Fh）は実際に描画される個別成分（My/Mz または Qy/Qz）を allValues に入れ、
                // 符号付きでカラーバー（青赤 Diverging）を選ばせる。
                // ダイアグラムの大きさは合成絶対値（fi, fj）でスケール統一する。
                if (isDerived)
                {
                    if (forceType == "Mh")
                    {
                        allValues.Add(br.Myi);
                        allValues.Add(-br.Myj);
                        allValues.Add(br.Mzi);
                        allValues.Add(-br.Mzj);
                    }
                    else // Fh
                    {
                        allValues.Add(br.Qyi);
                        allValues.Add(-br.Qyj);
                        allValues.Add(br.Qzi);
                        allValues.Add(-br.Qzj);
                    }
                }
                else
                {
                    allValues.Add(fi);
                    allValues.Add(-fj);
                }
                maxAbsValue = Math.Max(maxAbsValue, Math.Max(Math.Abs(fi), Math.Abs(fj)));

                drawEntries.Add((br, nodeI3D, nodeJ3D, beamDir));
            }

            if (allValues.Count == 0 || maxAbsValue < 1e-10) return;

            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;

            // 2) 描画ループ
            foreach (var (br, nodeI3D, nodeJ3D, beamDir) in drawEntries)
            {
                Matrix<double> t = Utils.GetNodeTransformMatrix(beamDir);

                if (isDerived)
                {
                    // Mh/Fh: 合成量を単一の矢印ではなく、構成成分を独立したダイアグラムで描く
                    // （水平解析の DrawFoundationBeamMyMz / DrawFoundationBeamFyFz と同じ方針）
                    var components = forceType == "Mh"
                        ? new (double origI, double origJ, Vector<double> dir)[]
                          {
                              (br.Myi, br.Myj, Vector<double>.Build.DenseOfArray([0.0, 0.0, -1.0])),
                              (br.Mzi, br.Mzj, Vector<double>.Build.DenseOfArray([0.0, 1.0, 0.0])),
                          }
                        : new (double origI, double origJ, Vector<double> dir)[]
                          {
                              (br.Qyi, br.Qyj, Vector<double>.Build.DenseOfArray([0.0, 1.0, 0.0])),
                              (br.Qzi, br.Qzj, Vector<double>.Build.DenseOfArray([0.0, 0.0, 1.0])),
                          };

                    foreach (var (origI, origJ, dir) in components)
                    {
                        if (!double.IsFinite(origI) || !double.IsFinite(origJ)) continue;

                        var transformedDir = t.Transpose() * dir;
                        double fI = origI / maxAbsValue * forceScale;
                        double fJ = origJ / maxAbsValue * forceScale;

                        Point3D nodeIForce3D = new(
                            nodeI3D.X + fI * transformedDir[0],
                            nodeI3D.Y + fI * transformedDir[1],
                            nodeI3D.Z + fI * transformedDir[2]);
                        Point3D nodeJForce3D = new(
                            nodeJ3D.X + -fJ * transformedDir[0],
                            nodeJ3D.Y + -fJ * transformedDir[1],
                            nodeJ3D.Z + -fJ * transformedDir[2]);

                        Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                        Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                        Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                        Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                        var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                        List<double> values = [origI, origI, -origJ, -origJ];
                        AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);

                        if (viewModel.IsResultValueVisible)
                        {
                            DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black,
                                origI, -origJ, nodeIForce2D, nodeJForce2D, nodeJ2D, nodeI2D, format, format, true);
                        }
                    }
                }
                else
                {
                    var (originalForceI, originalForceJ) = GetForceComponent(br, forceType);
                    var transformedForceDirection = t.Transpose() * forceDirection;

                    double forceI = originalForceI / maxAbsValue * forceScale;
                    double forceJ = originalForceJ / maxAbsValue * forceScale;

                    Point3D nodeIForce3D = new(
                        nodeI3D.X + forceI * transformedForceDirection[0],
                        nodeI3D.Y + forceI * transformedForceDirection[1],
                        nodeI3D.Z + forceI * transformedForceDirection[2]);
                    Point3D nodeJForce3D = new(
                        nodeJ3D.X + -forceJ * transformedForceDirection[0],
                        nodeJ3D.Y + -forceJ * transformedForceDirection[1],
                        nodeJ3D.Z + -forceJ * transformedForceDirection[2]);

                    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                    Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                    Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                    var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                    List<double> values = [originalForceI, originalForceI, -originalForceJ, -originalForceJ];
                    AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);

                    if (viewModel.IsResultValueVisible)
                    {
                        DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black,
                            originalForceI, -originalForceJ, nodeIForce2D, nodeJForce2D, nodeJ2D, nodeI2D, format, format, true);
                    }
                }
            }

            // 3) Path描画 + カラーバー
            foreach (var geo in colorBaredGeometries)
                geo.DrawPathes(Canvas3DLayout);

            if (allValues.Count > 0)
            {
                ColorBar.DrawStepColorBar(ColorBarCanvas, colorBaredGeometries,
                    forceType, unit, allValues.Min(), allValues.Max(), format, viewModel.LabelSize);
            }
        }
    }
}
