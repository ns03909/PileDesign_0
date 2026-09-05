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
    // 変形後形状の描画: 独立変形図・梁/杭断面の変形描画・剛体/リンク/ダミー梁。MainWindow.CanvasResults.cs からの物理分割 (純粋移動)。
    public partial class MainWindow
    {
        /// <summary>
        /// 解析結果表示と独立して変形後形状を描画する
        /// </summary>
        private void UpdateDeformedElementsStandalone(
            MainWindowViewModel viewModel = null,
            bool? hasInvisiblePileOverride = null,
            HashSet<Beam> visibleBeamsOverride = null,
            HashSet<string> invisibleFBNamesOverride = null)
        {
            viewModel ??= DataContext as MainWindowViewModel;
            if (viewModel == null) return;

            var anaModelDef = viewModel.CurrentModel;
            if (anaModelDef?.Beams == null) return;

            // visibleBeams/invisibleFBNames が未指定の場合は自前で構築
            bool hasInvisiblePile = hasInvisiblePileOverride ?? false;
            HashSet<Beam> visibleBeams = visibleBeamsOverride;
            HashSet<string> invisibleFBNames = invisibleFBNamesOverride;

            if (visibleBeams == null || invisibleFBNames == null)
            {
                visibleBeams = new HashSet<Beam>();
                invisibleFBNames = new HashSet<string>();
                // 可視性は現在の入力に従う (理由は BuildLivePileVisibility の説明を参照)
                var livePileVisibility = BuildLivePileVisibility(viewModel);
                hasInvisiblePile = false;

                if (viewModel.ResultInputModel?.PileLayoutItems != null)
                {
                    foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        if (IsPileVisibleForResult(livePileVisibility, pile))
                            foreach (var beam in pile.Beams) visibleBeams.Add(beam);
                        else
                            hasInvisiblePile = true;
                    }
                }
                if (viewModel.ResultInputModel?.FoundationBeamInput?.Beams != null)
                {
                    var beams = viewModel.ResultInputModel.FoundationBeamInput.Beams;
                    for (int i = 0; i < beams.Count; i++)
                    {
                        if (!IsFoundationBeamVisibleForResult(viewModel, i, beams[i]))
                            invisibleFBNames.Add($"FoundationBeam-{i + 1}");
                    }
                }
            }

            var lc = LoadCases.GetLoadCase(
                viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var lcomb = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (lc == null || lcomb == null) return;

            double maxDisp = 0;
            foreach (var node in anaModelDef.Nodes)
            {
                var nr = node?.GetNodeResult(anaModelDef, lc, lcomb, viewModel.IsLiquefaction);
                if (nr?.CumulativeDisp == null) continue;
                var nd = nr.CumulativeDisp;
                double uh = Math.Sqrt(nd.Ux * nd.Ux + nd.Uy * nd.Uy + nd.Uz * nd.Uz);
                if (uh > maxDisp) maxDisp = uh;
            }
            double ds = _sharedDispScaleMtoModel > 1e-15
                ? _sharedDispScaleMtoModel
                : (maxDisp > 1e-15
                    ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxDisp
                    : 0);

            DrawDeformedElements(viewModel, anaModelDef, lc, lcomb, ds,
                hasInvisiblePile, visibleBeams, invisibleFBNames);
        }

        /// <summary>
        /// 変形後形状を3次Hermite補間で描画
        /// Beam要素（杭体・FoundationBeam・RigidLink）、DummyBeamの変形後形状を描画
        /// </summary>
        private void DrawDeformedElements(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale,
            bool hasInvisiblePile, HashSet<Beam> visibleBeams, HashSet<string> invisibleFBNames)
        {
            if (Canvas3DLayout == null) return;

            var brush = BrushSemiTransparentGray;
            var pen = new Pen(brush, 1.0);
            pen.Freeze();

            // 節点変位モード時は節点→表示値マップとカラーバーを構築し、色分け描画へ切替
            var (nodeToValue, deformColorGeoms) = BuildDeformedNodeColorInfo(
                viewModel, anaModel, selectedLoadCase, selectedLoadCombination);
            bool colorize = nodeToValue != null && deformColorGeoms != null;

            // 既定（色分け無効時）用と、色別（色分け有効時）用の PathGeometry
            var defaultPathGeo = new PathGeometry();
            var pathByColor = new Dictionary<Color, PathGeometry>();

            // I/J 節点の平均表示値から色を決めて対応する PathGeometry を返す
            PathGeometry ResolvePath(Node nI, Node nJ)
            {
                if (!colorize) return defaultPathGeo;
                double vI = nI != null && nodeToValue.TryGetValue(nI, out var a) ? a : 0;
                double vJ = nJ != null && nodeToValue.TryGetValue(nJ, out var b) ? b : 0;
                Color c = PickColorFromGeoms(0.5 * (vI + vJ), deformColorGeoms, Colors.Gray);
                if (!pathByColor.TryGetValue(c, out var geo))
                {
                    geo = new PathGeometry();
                    pathByColor[c] = geo;
                }
                return geo;
            }

            // Beam要素（杭体 + FoundationBeam + RigidLink）
            if (anaModel.Beams != null)
            {
                foreach (var beam in anaModel.Beams)
                {
                    // 表示フィルタ
                    bool isFoundationBeam = beam.Name.StartsWith("FoundationBeam-");
                    if (isFoundationBeam)
                    {
                        if (invisibleFBNames.Contains(beam.Name)) continue;
                    }
                    else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam))
                        continue;

                    // 変位表示の有無 (チェック OFF でスキップ)
                    if (!viewModel.IsPileDisplacementVisible && !isFoundationBeam) continue;
                    if (!viewModel.IsFoundationBeamDisplacementVisible && isFoundationBeam) continue;

                    DrawDeformedBeam(viewModel, anaModel, beam, selectedLoadCase, selectedLoadCombination,
                        dispScale, ResolvePath(beam.NodeI, beam.NodeJ));
                }
            }

            // DummyBeam（座標変換なし → 直線補間で変形後位置を描画）
            // 杭変位非表示モードでは丸ごとスキップ (根入れ部は杭側の表現)
            if (!hasInvisiblePile && viewModel.IsPileDisplacementVisible && anaModel.DummyBeams != null)
            {
                foreach (var db in anaModel.DummyBeams)
                {
                    DrawDeformedDummyBeam(viewModel, anaModel, db, selectedLoadCase, selectedLoadCombination,
                        dispScale, ResolvePath(db.NodeI, db.NodeJ));
                }
            }

            // 表示対象杭の杭番号セットを構築 (hasInvisiblePile 時のみ。RotationalSpring と RigidBody の
             // 杭連結フィルタに使用 — 非表示杭の代表節点〜接合節点〜杭頭リンクを描かないため)
            HashSet<int> visiblePileNos = null;
            if (hasInvisiblePile && viewModel.ResultInputModel?.PileLayoutItems != null)
            {
                visiblePileNos = new HashSet<int>();
                foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                {
                    if (IsPileVisibleForResult(viewModel, pile)) visiblePileNos.Add(pile.No);
                }
            }

            // ノード名 (CapNode-{No} / FoundationNode-P{No}) から杭番号を抽出して可視性判定
            bool IsPileNodeOfVisiblePile(Node node)
            {
                if (visiblePileNos == null) return true;  // 全杭可視
                if (node == null) return true;
                string name = node.Name ?? "";
                string prefixCap = "CapNode-";
                string prefixFn = "FoundationNode-P";
                if (name.StartsWith(prefixCap, StringComparison.Ordinal))
                {
                    if (int.TryParse(name.Substring(prefixCap.Length), out int no)) return visiblePileNos.Contains(no);
                }
                else if (name.StartsWith(prefixFn, StringComparison.Ordinal))
                {
                    if (int.TryParse(name.Substring(prefixFn.Length), out int no)) return visiblePileNos.Contains(no);
                }
                // 杭体ノードは visibleFemNodes 側で扱われる。それ以外 (一般節点等) は常に表示。
                return true;
            }

            // RotationalSpring（杭頭～接合節点のリンク要素）
            if (anaModel.RotationalSprings != null)
            {
                foreach (var rs in anaModel.RotationalSprings)
                {
                    // 非表示杭に属する杭頭〜接合節点のリンクはスキップ
                    if (!IsPileNodeOfVisiblePile(rs.NodeI) && !IsPileNodeOfVisiblePile(rs.NodeJ)) continue;
                    DrawDeformedTwoNodeLink(viewModel, anaModel, rs.NodeI, rs.NodeJ,
                        selectedLoadCase, selectedLoadCombination,
                        dispScale, ResolvePath(rs.NodeI, rs.NodeJ));
                }
            }

            // RigidBody（剛体連結: Master→各Slave）
            if (anaModel.RigidBodies != null)
            {
                var capNodeToJointZ = new Dictionary<string, double>();
                if (viewModel.ResultInputModel?.PileLayoutItems != null)
                {
                    foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        // v2 セマンティクス: pile.Z は接合節点 Z (= JointZ)
                        capNodeToJointZ[$"CapNode-{pile.No}"] = pile.Z;
                    }
                }

                foreach (var rb in anaModel.RigidBodies)
                {
                    if (rb.MasterNode == null || rb.SlaveNodes == null) continue;
                    foreach (var slave in rb.SlaveNodes)
                    {
                        // 非表示杭の代表節点〜接合節点 / 代表節点〜杭頭 リンクはスキップ
                        if (!IsPileNodeOfVisiblePile(slave)) continue;

                        PathGeometry target = ResolvePath(rb.MasterNode, slave);
                        if (slave.Name.StartsWith("CapNode-") &&
                            capNodeToJointZ.TryGetValue(slave.Name, out double jointZ) &&
                            Math.Abs(slave.Coord.Z - jointZ) > 0.001)
                        {
                            DrawDeformedRigidBodyViaJoint(viewModel, anaModel, rb.MasterNode, slave,
                                jointZ, selectedLoadCase, selectedLoadCombination, dispScale, target);
                        }
                        else
                        {
                            DrawDeformedTwoNodeLink(viewModel, anaModel, rb.MasterNode, slave,
                                selectedLoadCase, selectedLoadCombination, dispScale, target);
                        }
                    }
                }
            }

            // Canvas に描画
            if (colorize)
            {
                foreach (var (color, geo) in pathByColor)
                {
                    if (geo.IsEmpty()) continue;
                    var colorBrush = new SolidColorBrush(color);
                    colorBrush.Freeze();
                    Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                    {
                        Stroke = colorBrush,
                        StrokeThickness = 1.5,
                        Data = geo
                    });
                }
            }
            else if (!defaultPathGeo.IsEmpty())
            {
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 1.0,
                    Data = defaultPathGeo
                });
            }

            // 変形後杭体形状の描画
            if (viewModel.IsPileSectionVisible && viewModel.ResultInputModel?.PileLayoutItems != null)
            {
                DrawDeformedPileSections(viewModel, anaModel, selectedLoadCase, selectedLoadCombination,
                    dispScale, brush, nodeToValue, deformColorGeoms);
            }

            // 変形後基礎梁断面形状の描画
            if (viewModel.IsBeamElementSectionVisible && anaModel.Beams != null)
            {
                DrawDeformedBeamSections(viewModel, anaModel, selectedLoadCase, selectedLoadCombination,
                    dispScale, invisibleFBNames, brush, nodeToValue, deformColorGeoms);
            }
        }

        /// <summary>
        /// 節点変位モード時の (Node → 表示値) マップとカラーバーを生成。非該当は (null, null)。
        /// </summary>
        private (Dictionary<Node, double>? map, List<ColorBaredGeometry>? geoms) BuildDeformedNodeColorInfo(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase lc, LoadCombination lcomb)
        {
            if (viewModel.AnalysisResultContent != "節点変位（水平）" || anaModel?.Nodes == null)
                return (null, null);

            Vector<double> effectiveVector;
            double multiplier;
            switch (viewModel.AnalysisResultNodeDisplacementType)
            {
                case "UH": effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 0, 0, 0, 0]); multiplier = 1000; break;
                case "U": effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 1, 0, 0, 0]); multiplier = 1000; break;
                case "UX": effectiveVector = Vector<double>.Build.DenseOfArray([1, 0, 0, 0, 0, 0]); multiplier = 1000; break;
                case "UY": effectiveVector = Vector<double>.Build.DenseOfArray([0, 1, 0, 0, 0, 0]); multiplier = 1000; break;
                case "UZ": effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 1, 0, 0, 0]); multiplier = 1000; break;
                case "θH": effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 1, 0]); multiplier = 1; break;
                case "θX": effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 1, 0, 0]); multiplier = 1; break;
                case "θY": effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 1, 0]); multiplier = 1; break;
                case "θZ": effectiveVector = Vector<double>.Build.DenseOfArray([0, 0, 0, 0, 0, 1]); multiplier = 1; break;
                default: return (null, null);
            }

            var map = new Dictionary<Node, double>();
            foreach (var n in anaModel.Nodes)
            {
                if (n == null) continue;
                var nr = n.GetNodeResult(anaModel, lc, lcomb, viewModel.IsLiquefaction);
                if (nr?.CumulativeDisp == null) continue;
                var nd = nr.CumulativeDisp;
                double val = Math.Sqrt(
                    Math.Pow(nd.Ux * effectiveVector[0], 2) +
                    Math.Pow(nd.Uy * effectiveVector[1], 2) +
                    Math.Pow(nd.Uz * effectiveVector[2], 2) +
                    Math.Pow(nd.Rx * effectiveVector[3], 2) +
                    Math.Pow(nd.Ry * effectiveVector[4], 2) +
                    Math.Pow(nd.Rz * effectiveVector[5], 2));
                map[n] = val * multiplier;
            }

            if (map.Count == 0) return (null, null);

            var geoms = ColorBarUtils.GetColorBarGeometries(
                map.Values, steps: 12, mode: ColorBarUtils.ColorBarMode.Rainbow);
            return (map, geoms);
        }

        /// <summary>
        /// 変形後の基礎梁断面形状を描画する
        /// Hermite補間で変形後の中心線点列を生成し、各点で断面の4隅を計算して
        /// 曲がった梁の断面形状を描画する（nodeToValue/colorGeoms 指定時は沈下量で色分け）
        /// </summary>
        private void DrawDeformedBeamSections(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, HashSet<string> invisibleFBNames, Brush brush,
            Dictionary<Node, double>? nodeToValue = null,
            List<ColorBaredGeometry>? colorGeoms = null)
        {
            var fbInput = viewModel.ResultInputModel?.FoundationBeamInput;
            if (fbInput?.Beams == null || fbInput.Sections == null) return;

            var fbElemDict = new Dictionary<string, FoundationBeam>();
            for (int i = 0; i < fbInput.Beams.Count; i++)
                fbElemDict[$"FoundationBeam-{i + 1}"] = fbInput.Beams[i];

            // Section は SectionNo (1-based) → 当該 section のマップ
            var secDict = new Dictionary<int, BeamSection>();
            for (int i = 0; i < fbInput.Sections.Count; i++)
                secDict[i + 1] = fbInput.Sections[i];

            bool colorize = nodeToValue != null && colorGeoms != null;
            var defaultPathGeo = new PathGeometry();
            var pathByColor = new Dictionary<Color, PathGeometry>();
            var transform = viewModel.CanvasThreeDView;

            foreach (var beam in anaModel.Beams)
            {
                if (!beam.Name.StartsWith("FoundationBeam-")) continue;
                if (invisibleFBNames.Contains(beam.Name)) continue;
                if (beam.NodeI == null || beam.NodeJ == null) continue;

                if (!fbElemDict.TryGetValue(beam.Name, out var fbElem)) continue;

                // この梁用の PathGeometry（色分け時は I/J 平均値に応じたバケット）
                PathGeometry sectionPathGeo;
                if (colorize)
                {
                    double vI = nodeToValue.TryGetValue(beam.NodeI, out var a) ? a : 0;
                    double vJ = nodeToValue.TryGetValue(beam.NodeJ, out var b) ? b : 0;
                    Color col = PickColorFromGeoms(0.5 * (vI + vJ), colorGeoms, Colors.Gray);
                    if (!pathByColor.TryGetValue(col, out var existing))
                    {
                        existing = new PathGeometry();
                        pathByColor[col] = existing;
                    }
                    sectionPathGeo = existing;
                }
                else
                {
                    sectionPathGeo = defaultPathGeo;
                }
                double bw = fbElem.Width;
                double bh = fbElem.Height;
                if (secDict.TryGetValue(fbElem.SectionNo, out var sec))
                {
                    bw = sec.Width;
                    bh = sec.Height;
                }
                if (bw <= 0 || bh <= 0) continue;

                var nrI = beam.NodeI.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                var nrJ = beam.NodeJ.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null) continue;

                // Hermite補間で変形後3D中心線点列を取得
                var points3D = Common.HermiteBeamInterpolation.GetDeformedPoints(
                    beam, nrI.CumulativeDisp, nrJ.CumulativeDisp, dispScale);
                if (points3D.Count < 2) continue;

                double hw = bw / 2.0;
                double hh = bh / 2.0;
                double angleBetaDeg = fbElem.AngleBeta;

                // 各補間点で4隅の2D座標を計算
                var allCorners2D = new Point[points3D.Count][];
                bool hasInvalid = false;

                for (int k = 0; k < points3D.Count; k++)
                {
                    // 接線方向を前後の差分で算出
                    Vector3D tangent;
                    if (k == 0)
                        tangent = points3D[1] - points3D[0];
                    else if (k == points3D.Count - 1)
                        tangent = points3D[k] - points3D[k - 1];
                    else
                        tangent = points3D[k + 1] - points3D[k - 1];

                    double tLen = tangent.Length;
                    if (tLen < 1e-12) tangent = new Vector3D(1, 0, 0);
                    else tangent.Normalize();

                    // 局所座標系（接線方向に追従）
                    Vector3D up = new(0, 0, 1);
                    Vector3D localZ;
                    if (Math.Abs(Vector3D.DotProduct(tangent, up)) > 0.999)
                        localZ = new Vector3D(0, 1, 0);
                    else
                    {
                        localZ = up - Vector3D.DotProduct(up, tangent) * tangent;
                        localZ.Normalize();
                    }
                    Vector3D localY = Vector3D.CrossProduct(localZ, tangent);
                    localY.Normalize();

                    // AngleBeta 回転
                    if (Math.Abs(angleBetaDeg) > 1e-9)
                    {
                        double rad = angleBetaDeg * Math.PI / 180.0;
                        double cosB = Math.Cos(rad);
                        double sinB = Math.Sin(rad);
                        Vector3D newY = cosB * localY + sinB * localZ;
                        Vector3D newZ = -sinB * localY + cosB * localZ;
                        localY = newY;
                        localZ = newZ;
                    }

                    var c = points3D[k];
                    allCorners2D[k] =
                    [
                        transform.Transformation(new Point3D(c.X - hw * localY.X - hh * localZ.X, c.Y - hw * localY.Y - hh * localZ.Y, c.Z - hw * localY.Z - hh * localZ.Z)),
                        transform.Transformation(new Point3D(c.X + hw * localY.X - hh * localZ.X, c.Y + hw * localY.Y - hh * localZ.Y, c.Z + hw * localY.Z - hh * localZ.Z)),
                        transform.Transformation(new Point3D(c.X + hw * localY.X + hh * localZ.X, c.Y + hw * localY.Y + hh * localZ.Y, c.Z + hw * localY.Z + hh * localZ.Z)),
                        transform.Transformation(new Point3D(c.X - hw * localY.X + hh * localZ.X, c.Y - hw * localY.Y + hh * localZ.Y, c.Z - hw * localY.Z + hh * localZ.Z)),
                    ];

                    foreach (var pt in allCorners2D[k])
                    {
                        if (!double.IsFinite(pt.X) || !double.IsFinite(pt.Y)) { hasInvalid = true; break; }
                    }
                    if (hasInvalid) break;
                }
                if (hasInvalid) continue;

                // 端面の矩形（I端, J端のみ）
                for (int e = 0; e < allCorners2D.Length; e += allCorners2D.Length - 1)
                {
                    for (int i = 0; i < 4; i++)
                        sectionPathGeo.AddGeometry(new LineGeometry(allCorners2D[e][i], allCorners2D[e][(i + 1) % 4]));
                }

                // 4本の稜線（Hermite補間に沿った曲線）
                for (int corner = 0; corner < 4; corner++)
                {
                    for (int k = 0; k < allCorners2D.Length - 1; k++)
                        sectionPathGeo.AddGeometry(new LineGeometry(allCorners2D[k][corner], allCorners2D[k + 1][corner]));
                }
            }

            if (colorize)
            {
                foreach (var (color, geo) in pathByColor)
                {
                    if (geo.IsEmpty()) continue;
                    var cb = new SolidColorBrush(color); cb.Freeze();
                    Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                    { Stroke = cb, StrokeThickness = 0.7, Data = geo });
                }
            }
            else if (!defaultPathGeo.IsEmpty())
            {
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                { Stroke = brush, StrokeThickness = 0.7, Data = defaultPathGeo });
            }
        }

        /// <summary>
        /// 変形後の杭体形状を描画する
        /// Hermite補間の変形後中心線に沿って、法線方向にオフセットした左右の輪郭線＋端部楕円を描画
        /// （nodeToValue/colorGeoms 指定時は、杭頭 Uz の平均値で杭単位に色分け）
        /// </summary>
        private void DrawDeformedPileSections(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            // hasInvisiblePile は受け取っていたが本文で使っておらず、
            // 「非表示杭があると描き方が変わる」と読めるだけだったので外した
            double dispScale, Brush brush,
            Dictionary<Node, double>? nodeToValue = null,
            List<ColorBaredGeometry>? colorGeoms = null)
        {
            bool colorize = nodeToValue != null && colorGeoms != null;
            var defaultPathGeo = new PathGeometry();
            var pathByColor = new Dictionary<Color, PathGeometry>();
            double flattening = viewModel.CanvasThreeDView.Flattening;
            double scale = viewModel.CanvasThreeDView.Scale;

            foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;
                if (pile.PileNodes == null || pile.PileNodes.Count < 2) continue;
                if (pile.Beams == null || pile.Beams.Count == 0) continue;

                // 非 colorize 時は単色用（従来通り）
                PathGeometry sectionPathGeo = colorize ? null : defaultPathGeo;

                // 杭要素分割後のセグメント情報を取得（SoilPile経由）
                ObservableCollection<PileBodySegment> soilPileSegments = null;
                if (pile.SoilPileAltNo > 0 &&
                    pile.SoilPileAltNo <= viewModel.ResultInputModel.ElementDivision.SoilPiles.Count)
                {
                    soilPileSegments = viewModel.ResultInputModel.ElementDivision
                        .SoilPiles[pile.SoilPileAltNo - 1].PileBodySegments;
                }

                // 杭全体の左右輪郭線用ポイントリスト
                var leftPoints = new List<Point>();
                var rightPoints = new List<Point>();
                // 各輪郭点に対応する中心点と接線（楕円描画用）
                var centerPoints = new List<Point>();
                var tangentVectors = new List<Vector>();
                var radiusList = new List<double>();
                // 要素境界のインデックス（楕円を描画する位置）
                var boundaryIndices = new List<int>();
                // 各輪郭点に対応する「表示値」（色分け用の補間値。colorize=false 時は未使用）
                var pointValues = new List<double>();

                // 各Beam要素の変形後中心線を連結して輪郭線を生成
                for (int i = 0; i < pile.Beams.Count; i++)
                {
                    var beam = pile.Beams[i];
                    if (beam.NodeI == null || beam.NodeJ == null) continue;

                    // この梁の端部表示値（colorize 時のみ意味あり）
                    double vBeamI = 0, vBeamJ = 0;
                    if (colorize)
                    {
                        nodeToValue.TryGetValue(beam.NodeI, out vBeamI);
                        nodeToValue.TryGetValue(beam.NodeJ, out vBeamJ);
                    }

                    // 杭径を取得
                    double pileDia = 0;
                    if (beam.SegmentIndex.HasValue && soilPileSegments != null)
                    {
                        int segIdx = beam.SegmentIndex.Value;
                        if (segIdx >= 0 && segIdx < soilPileSegments.Count)
                            pileDia = soilPileSegments[segIdx].PileSection.PileDiameter / 1000.0;
                    }
                    if (pileDia <= 0) continue;

                    double radius2D = pileDia * 0.5 * scale;

                    var nrI = beam.NodeI.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    var nrJ = beam.NodeJ.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                    if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null) continue;

                    // Hermite補間で変形後3D点列を取得
                    var points3D = Common.HermiteBeamInterpolation.GetDeformedPoints(
                        beam, nrI.CumulativeDisp, nrJ.CumulativeDisp, dispScale);
                    if (points3D.Count < 2) continue;

                    // 3D → 2D変換
                    var pts2D = new List<Point>(points3D.Count);
                    bool hasInvalid = false;
                    foreach (var p3 in points3D)
                    {
                        var p2 = viewModel.CanvasThreeDView.Transformation(p3);
                        if (!double.IsFinite(p2.X) || !double.IsFinite(p2.Y)) { hasInvalid = true; break; }
                        pts2D.Add(p2);
                    }
                    if (hasInvalid || pts2D.Count < 2) continue;

                    // 各補間点で法線方向にオフセットして左右の輪郭点を算出
                    bool isFirstBeam = (leftPoints.Count == 0);
                    int startK = isFirstBeam ? 0 : 1; // 2要素目以降は始点を重複させない

                    // 要素I端（最初の要素のみ）の境界インデックスを記録
                    if (isFirstBeam)
                        boundaryIndices.Add(0);

                    for (int k = startK; k < pts2D.Count; k++)
                    {
                        // 接線方向を前後の差分で算出
                        Vector tangent;
                        if (k == 0)
                            tangent = pts2D[1] - pts2D[0];
                        else if (k == pts2D.Count - 1)
                            tangent = pts2D[k] - pts2D[k - 1];
                        else
                            tangent = pts2D[k + 1] - pts2D[k - 1];

                        double len = tangent.Length;
                        if (len < 1e-10)
                        {
                            tangent = new Vector(0, 1);
                            len = 1;
                        }

                        // 2D画面上の法線（接線を90度回転）
                        Vector normal = new Vector(-tangent.Y / len, tangent.X / len);

                        leftPoints.Add(new Point(pts2D[k].X + normal.X * radius2D, pts2D[k].Y + normal.Y * radius2D));
                        rightPoints.Add(new Point(pts2D[k].X - normal.X * radius2D, pts2D[k].Y - normal.Y * radius2D));
                        centerPoints.Add(pts2D[k]);
                        tangentVectors.Add(tangent);
                        radiusList.Add(radius2D);

                        // 色分け用: 梁軸方向の位置比率 t で vBeamI と vBeamJ を線形補間
                        if (colorize)
                        {
                            double t = (pts2D.Count > 1) ? (double)k / (pts2D.Count - 1) : 0.0;
                            pointValues.Add((1.0 - t) * vBeamI + t * vBeamJ);
                        }
                    }

                    // 要素J端の境界インデックスを記録
                    boundaryIndices.Add(centerPoints.Count - 1);
                }

                if (leftPoints.Count < 2) continue;

                // 左右の輪郭線を PathGeometry に追加
                // colorize=true: 各セグメントの中間値で色を決定し、色別 PathGeometry に振り分け
                // colorize=false: すべて defaultPathGeo に集約
                for (int k = 0; k < leftPoints.Count - 1; k++)
                {
                    PathGeometry targetGeo;
                    if (colorize)
                    {
                        double vMid = 0.5 * (pointValues[k] + pointValues[k + 1]);
                        Color col = PickColorFromGeoms(vMid, colorGeoms, Colors.Gray);
                        if (!pathByColor.TryGetValue(col, out var existing))
                        {
                            existing = new PathGeometry();
                            pathByColor[col] = existing;
                        }
                        targetGeo = existing;
                    }
                    else
                    {
                        targetGeo = sectionPathGeo;
                    }
                    targetGeo.AddGeometry(new LineGeometry(leftPoints[k], leftPoints[k + 1]));
                    targetGeo.AddGeometry(new LineGeometry(rightPoints[k], rightPoints[k + 1]));
                }

                // 全要素境界に楕円を描画（節点値そのもので色付け）
                foreach (int idx in boundaryIndices)
                {
                    PathGeometry targetGeo;
                    if (colorize)
                    {
                        double vNode = pointValues[idx];
                        Color col = PickColorFromGeoms(vNode, colorGeoms, Colors.Gray);
                        if (!pathByColor.TryGetValue(col, out var existing))
                        {
                            existing = new PathGeometry();
                            pathByColor[col] = existing;
                        }
                        targetGeo = existing;
                    }
                    else
                    {
                        targetGeo = sectionPathGeo;
                    }
                    AddDeformedEllipse(targetGeo, centerPoints[idx], tangentVectors[idx], radiusList[idx], flattening);
                }
            }

            if (colorize)
            {
                foreach (var (color, geo) in pathByColor)
                {
                    if (geo.IsEmpty()) continue;
                    var cb = new SolidColorBrush(color); cb.Freeze();
                    Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                    { Stroke = cb, StrokeThickness = 0.7, Data = geo });
                }
            }
            else if (!defaultPathGeo.IsEmpty())
            {
                Canvas3DLayout.Children.Add(new System.Windows.Shapes.Path
                { Stroke = brush, StrokeThickness = 0.7, Data = defaultPathGeo });
            }
        }

        /// <summary>
        /// 変形後の楕円を中心線の傾きに合わせて回転して描画
        /// 楕円の長軸は梁軸に直交する方向に配置する
        /// </summary>
        private static void AddDeformedEllipse(
            PathGeometry pathGeo, Point center, Vector tangent, double radius2D, double flattening)
        {
            // 梁軸の角度を求め、楕円の長軸を直交方向に配置するため +90°
            double angle = Math.Atan2(tangent.Y, tangent.X) * 180.0 / Math.PI + 90.0;

            var ellipse = new EllipseGeometry(center, radius2D, radius2D * flattening);
            ellipse.Transform = new RotateTransform(angle, center.X, center.Y);
            pathGeo.AddGeometry(ellipse);
        }

        /// <summary>
        /// 変形後中心線点列と断面寸法から、4隅の稜線と両端矩形を pathGeo に追加する。
        /// DrawDeformedBeamSections のコア描画を点列ベースで汎用化したヘルパー。
        /// </summary>
        private static void AddBeamSectionGeometryFromPoints(
            PathGeometry sectionPathGeo,
            IReadOnlyList<Point3D> points3D,
            double width, double height, double angleBetaDeg,
            CanvasThreeDView transform)
        {
            if (points3D.Count < 2 || width <= 0 || height <= 0) return;
            double hw = width / 2.0;
            double hh = height / 2.0;

            var allCorners2D = new Point[points3D.Count][];
            bool hasInvalid = false;

            for (int k = 0; k < points3D.Count; k++)
            {
                Vector3D tangent;
                if (k == 0) tangent = points3D[1] - points3D[0];
                else if (k == points3D.Count - 1) tangent = points3D[k] - points3D[k - 1];
                else tangent = points3D[k + 1] - points3D[k - 1];

                if (tangent.Length < 1e-12) tangent = new Vector3D(1, 0, 0);
                else tangent.Normalize();

                Vector3D up = new(0, 0, 1);
                Vector3D localZ;
                if (Math.Abs(Vector3D.DotProduct(tangent, up)) > 0.999)
                    localZ = new Vector3D(0, 1, 0);
                else
                {
                    localZ = up - Vector3D.DotProduct(up, tangent) * tangent;
                    localZ.Normalize();
                }
                Vector3D localY = Vector3D.CrossProduct(localZ, tangent);
                localY.Normalize();

                if (Math.Abs(angleBetaDeg) > 1e-9)
                {
                    double rad = angleBetaDeg * Math.PI / 180.0;
                    double cosB = Math.Cos(rad);
                    double sinB = Math.Sin(rad);
                    Vector3D newY = cosB * localY + sinB * localZ;
                    Vector3D newZ = -sinB * localY + cosB * localZ;
                    localY = newY;
                    localZ = newZ;
                }

                var c = points3D[k];
                allCorners2D[k] =
                [
                    transform.Transformation(new Point3D(c.X - hw * localY.X - hh * localZ.X, c.Y - hw * localY.Y - hh * localZ.Y, c.Z - hw * localY.Z - hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X + hw * localY.X - hh * localZ.X, c.Y + hw * localY.Y - hh * localZ.Y, c.Z + hw * localY.Z - hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X + hw * localY.X + hh * localZ.X, c.Y + hw * localY.Y + hh * localZ.Y, c.Z + hw * localY.Z + hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X - hw * localY.X + hh * localZ.X, c.Y - hw * localY.Y + hh * localZ.Y, c.Z - hw * localY.Z + hh * localZ.Z)),
                ];
                foreach (var pt in allCorners2D[k])
                {
                    if (!double.IsFinite(pt.X) || !double.IsFinite(pt.Y)) { hasInvalid = true; break; }
                }
                if (hasInvalid) break;
            }
            if (hasInvalid) return;

            // 端面の矩形（I端, J端のみ）
            for (int e = 0; e < allCorners2D.Length; e += allCorners2D.Length - 1)
            {
                for (int i = 0; i < 4; i++)
                    sectionPathGeo.AddGeometry(new LineGeometry(allCorners2D[e][i], allCorners2D[e][(i + 1) % 4]));
            }
            // 4本の稜線
            for (int corner = 0; corner < 4; corner++)
            {
                for (int k = 0; k < allCorners2D.Length - 1; k++)
                    sectionPathGeo.AddGeometry(new LineGeometry(allCorners2D[k][corner], allCorners2D[k + 1][corner]));
            }
        }

        /// <summary>
        /// 1 杭区間の変形後中心線 3D 点列から、左右輪郭線と両端の楕円を pathGeo に追加する。
        /// DrawDeformedPileSections のコア描画を点列ベースで汎用化したヘルパー。
        /// </summary>
        private static void AddPileSegmentSectionGeometryFromPoints(
            PathGeometry sectionPathGeo,
            IReadOnlyList<Point3D> points3D,
            double pileDiameterInMeters,
            CanvasThreeDView transform,
            double flattening, double scale,
            bool drawTopEllipse, bool drawBottomEllipse)
        {
            if (points3D.Count < 2 || pileDiameterInMeters <= 0) return;
            double radius2D = pileDiameterInMeters * 0.5 * scale;

            var pts2D = new Point[points3D.Count];
            for (int k = 0; k < points3D.Count; k++)
            {
                pts2D[k] = transform.Transformation(points3D[k]);
                if (!double.IsFinite(pts2D[k].X) || !double.IsFinite(pts2D[k].Y)) return;
            }

            var leftPoints = new Point[points3D.Count];
            var rightPoints = new Point[points3D.Count];
            Vector tangentFirst = default, tangentLast = default;

            for (int k = 0; k < points3D.Count; k++)
            {
                Vector tangent;
                if (k == 0) tangent = pts2D[1] - pts2D[0];
                else if (k == points3D.Count - 1) tangent = pts2D[k] - pts2D[k - 1];
                else tangent = pts2D[k + 1] - pts2D[k - 1];
                double len = tangent.Length;
                if (len < 1e-10) { tangent = new Vector(0, 1); len = 1; }
                Vector normal = new(-tangent.Y / len, tangent.X / len);
                leftPoints[k] = new Point(pts2D[k].X + normal.X * radius2D, pts2D[k].Y + normal.Y * radius2D);
                rightPoints[k] = new Point(pts2D[k].X - normal.X * radius2D, pts2D[k].Y - normal.Y * radius2D);
                if (k == 0) tangentFirst = tangent;
                if (k == points3D.Count - 1) tangentLast = tangent;
            }

            for (int k = 0; k < leftPoints.Length - 1; k++)
            {
                sectionPathGeo.AddGeometry(new LineGeometry(leftPoints[k], leftPoints[k + 1]));
                sectionPathGeo.AddGeometry(new LineGeometry(rightPoints[k], rightPoints[k + 1]));
            }

            if (drawTopEllipse)
                AddDeformedEllipse(sectionPathGeo, pts2D[0], tangentFirst, radius2D, flattening);
            if (drawBottomEllipse)
                AddDeformedEllipse(sectionPathGeo, pts2D[^1], tangentLast, radius2D, flattening);
        }

        /// <summary>
        /// 1つのBeam要素の変形後形状を3次Hermite補間で描画
        /// </summary>
        private void DrawDeformedBeam(
            MainWindowViewModel viewModel, AnaModel anaModel,
            Beam beam,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, PathGeometry pathGeo)
        {
            if (beam.NodeI == null || beam.NodeJ == null) return;

            var nrI = beam.NodeI.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            var nrJ = beam.NodeJ.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null) return;

            var points3D = Common.HermiteBeamInterpolation.GetDeformedPoints(
                beam, nrI.CumulativeDisp, nrJ.CumulativeDisp, dispScale);

            if (points3D.Count < 2) return;

            // 3D → 2D変換してPathGeometryに追加
            for (int k = 0; k < points3D.Count - 1; k++)
            {
                var p1 = viewModel.CanvasThreeDView.Transformation(points3D[k]);
                var p2 = viewModel.CanvasThreeDView.Transformation(points3D[k + 1]);

                if (!double.IsFinite(p1.X) || !double.IsFinite(p1.Y) ||
                    !double.IsFinite(p2.X) || !double.IsFinite(p2.Y))
                    continue;

                pathGeo.AddGeometry(new LineGeometry(p1, p2));
            }
        }

        /// <summary>
        /// 2節点間（RotationalSpring/RigidBody）の変形後形状を直線で描画
        /// </summary>
        private void DrawDeformedTwoNodeLink(
            MainWindowViewModel viewModel, AnaModel anaModel,
            Node nodeI, Node nodeJ,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, PathGeometry pathGeo)
        {
            if (nodeI == null || nodeJ == null) return;

            var nrI = nodeI.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            var nrJ = nodeJ.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null) return;

            var ndI = nrI.CumulativeDisp;
            var ndJ = nrJ.CumulativeDisp;

            Point3D pI3D = new(
                nodeI.Coord.X + ndI.Ux * dispScale,
                nodeI.Coord.Y + ndI.Uy * dispScale,
                nodeI.Coord.Z + ndI.Uz * dispScale);
            Point3D pJ3D = new(
                nodeJ.Coord.X + ndJ.Ux * dispScale,
                nodeJ.Coord.Y + ndJ.Uy * dispScale,
                nodeJ.Coord.Z + ndJ.Uz * dispScale);

            var p1 = viewModel.CanvasThreeDView.Transformation(pI3D);
            var p2 = viewModel.CanvasThreeDView.Transformation(pJ3D);

            if (!double.IsFinite(p1.X) || !double.IsFinite(p1.Y) ||
                !double.IsFinite(p2.X) || !double.IsFinite(p2.Y))
                return;

            pathGeo.AddGeometry(new LineGeometry(p1, p2));
        }

        /// <summary>
        /// RigidBodyの放射線を接合節点位置を経由して描画する。
        /// 接合節点はFEMモデルに含まれないため、MasterNodeの剛体変位（並進+回転）から位置を算出する。
        /// MasterNode → 接合節点位置 → CapNode の2本の線を描画。
        /// </summary>
        private void DrawDeformedRigidBodyViaJoint(
            MainWindowViewModel viewModel, AnaModel anaModel,
            Node masterNode, Node capNode, double jointZ,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, PathGeometry pathGeo)
        {
            var nrMaster = masterNode.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            var nrCap = capNode.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            if (nrMaster?.CumulativeDisp == null || nrCap?.CumulativeDisp == null) return;

            var ndM = nrMaster.CumulativeDisp;
            var ndC = nrCap.CumulativeDisp;

            // 接合節点の原点位置（CapNodeのXY、接合節点Z = pile.Z + ΔZc）
            double jx0 = capNode.Coord.X;
            double jy0 = capNode.Coord.Y;
            double jz0 = jointZ;

            // MasterNodeから接合節点へのアームベクトル
            double ax = jx0 - masterNode.Coord.X;
            double ay = jy0 - masterNode.Coord.Y;
            double az = jz0 - masterNode.Coord.Z;

            // 剛体変位: u_joint = u_master + θ_master × arm
            // θ × arm = (θy*az - θz*ay, θz*ax - θx*az, θx*ay - θy*ax)
            double ujx = ndM.Ux + (ndM.Ry * az - ndM.Rz * ay);
            double ujy = ndM.Uy + (ndM.Rz * ax - ndM.Rx * az);
            double ujz = ndM.Uz + (ndM.Rx * ay - ndM.Ry * ax);

            // 変形後座標
            Point3D masterPos = new(
                masterNode.Coord.X + ndM.Ux * dispScale,
                masterNode.Coord.Y + ndM.Uy * dispScale,
                masterNode.Coord.Z + ndM.Uz * dispScale);

            Point3D jointPos = new(
                jx0 + ujx * dispScale,
                jy0 + ujy * dispScale,
                jz0 + ujz * dispScale);

            Point3D capPos = new(
                capNode.Coord.X + ndC.Ux * dispScale,
                capNode.Coord.Y + ndC.Uy * dispScale,
                capNode.Coord.Z + ndC.Uz * dispScale);

            var ptM = viewModel.CanvasThreeDView.Transformation(masterPos);
            var ptJ = viewModel.CanvasThreeDView.Transformation(jointPos);
            var ptC = viewModel.CanvasThreeDView.Transformation(capPos);

            if (!double.IsFinite(ptM.X) || !double.IsFinite(ptJ.X) || !double.IsFinite(ptC.X)) return;

            // MasterNode → 接合節点位置
            pathGeo.AddGeometry(new LineGeometry(ptM, ptJ));
            // 接合節点位置 → CapNode
            pathGeo.AddGeometry(new LineGeometry(ptJ, ptC));
        }

        /// <summary>
        /// 1つのDummyBeamの変形後形状を直線補間で描画（座標変換なし）
        /// </summary>
        private void DrawDeformedDummyBeam(
            MainWindowViewModel viewModel, AnaModel anaModel,
            DummyBeam db,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, PathGeometry pathGeo)
        {
            if (db.NodeI == null || db.NodeJ == null) return;

            var nrI = db.NodeI.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            var nrJ = db.NodeJ.GetNodeResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
            if (nrI?.CumulativeDisp == null || nrJ?.CumulativeDisp == null) return;

            var ndI = nrI.CumulativeDisp;
            var ndJ = nrJ.CumulativeDisp;

            Point3D pI3D = new(
                db.NodeI.Coord.X + ndI.Ux * dispScale,
                db.NodeI.Coord.Y + ndI.Uy * dispScale,
                db.NodeI.Coord.Z + ndI.Uz * dispScale);
            Point3D pJ3D = new(
                db.NodeJ.Coord.X + ndJ.Ux * dispScale,
                db.NodeJ.Coord.Y + ndJ.Uy * dispScale,
                db.NodeJ.Coord.Z + ndJ.Uz * dispScale);

            var p1 = viewModel.CanvasThreeDView.Transformation(pI3D);
            var p2 = viewModel.CanvasThreeDView.Transformation(pJ3D);

            if (!double.IsFinite(p1.X) || !double.IsFinite(p1.Y) ||
                !double.IsFinite(p2.X) || !double.IsFinite(p2.Y))
                return;

            pathGeo.AddGeometry(new LineGeometry(p1, p2));
        }

    }
}
