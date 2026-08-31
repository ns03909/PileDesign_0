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
    // 力の描画: 基礎梁 My/Mz・Fy/Fz ダイアグラム、水平地盤ばね・地盤反力分布、杭頭力/接合点力マップ。MainWindow.CanvasResults.cs からの物理分割 (純粋移動)。
    public partial class MainWindow
    {
        /// <summary>
        /// 基礎梁のMh選択時にMyとMzを個別にダイアグラム描画する
        /// </summary>
        private void DrawFoundationBeamMyMz(
            MainWindowViewModel viewModel, Beam beam, BeamResult beamResult,
            double maxAbsValue,
            List<ColorBaredGeometry> colorBaredGeometries,
            PathGeometry rigidLinkBoundaryGeo)
        {
            var bf = beamResult.CumulativeForce;

            var beamDir = new Vector3D(
                beam.NodeJ.Coord.X - beam.NodeI.Coord.X,
                beam.NodeJ.Coord.Y - beam.NodeI.Coord.Y,
                beam.NodeJ.Coord.Z - beam.NodeI.Coord.Z);
            Matrix<double> t = Utils.GetNodeTransformMatrix(beamDir);

            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
            string format = "{0:N" + viewModel.DecimalPlaces + "}";

            // My: indices [4, 10], forceDirection [0, 0, -1]
            // Mz: indices [5, 11], forceDirection [0, 1, 0]
            var components = new[]
            {
                (name: "My", idxI: 4, idxJ: 10, dir: Vector<double>.Build.DenseOfArray(new[] { 0.0, 0.0, -1.0 })),
                (name: "Mz", idxI: 5, idxJ: 11, dir: Vector<double>.Build.DenseOfArray(new[] { 0.0, 1.0, 0.0 })),
            };

            // 上面図モード: dir をローカル X 軸 (= 梁軸) 周りに 90° 回転して水平面に倒す。
            // local 系 (x: 梁軸, y, z) における (0, dy, dz) → (0, -dz, dy)
            bool rotateToHorizontal = viewModel.IsFoundationBeamStressRotatedToHorizontal;

            foreach (var (name, idxI, idxJ, dir) in components)
            {
                double origI = bf.GetByIndex(idxI);
                double origJ = bf.GetByIndex(idxJ);
                if (!double.IsFinite(origI) || !double.IsFinite(origJ)) continue;

                var localDir = rotateToHorizontal
                    ? Vector<double>.Build.DenseOfArray(new[] { dir[0], -dir[2], dir[1] })
                    : dir;
                var transformedDir = t.Transpose() * localDir;

                double fI = maxAbsValue == 0 ? 0 : origI / maxAbsValue * forceScale;
                double fJ = maxAbsValue == 0 ? 0 : origJ / maxAbsValue * forceScale;

                Point3D nodeI3D = beam.NodeI.Coord;
                Point3D nodeJ3D = beam.NodeJ.Coord;

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
                // 色付け値は絶対値で渡す。
                // 親 (DrawCanvasResults) で Mh = sqrt(My²+Mz²) (≥0) を allValues に入れて 0〜max の
                // Rainbow スケールを生成しているため、符号付き My/Mz を渡すと負側が全部
                // スケール下限 (濃い青) にクランプされる ("色が分割されない" 現象)。
                // ダイアグラムが描かれる側 (上下) は既に符号で位置決めされているので、
                // 色は |成分| で大小だけ表現するのが整合的。
                List<double> values = [Math.Abs(origI), Math.Abs(origI), Math.Abs(origJ), Math.Abs(origJ)];
                AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);

                if (viewModel.IsResultValueVisible)
                {
                    DrawResultValueTexts(
                        viewModel.IsResultValueVisible, Brushes.Black,
                        origI, -origJ,
                        nodeIForce2D, nodeJForce2D,
                        nodeJ2D, nodeI2D,
                        format, format, true);
                }
            }
        }

        /// <summary>
        /// 基礎梁のFh選択時にFyとFzを個別にダイアグラム描画する
        /// </summary>
        private void DrawFoundationBeamFyFz(
            MainWindowViewModel viewModel, Beam beam, BeamResult beamResult,
            double maxAbsValue,
            List<ColorBaredGeometry> colorBaredGeometries,
            PathGeometry rigidLinkBoundaryGeo)
        {
            var bf = beamResult.CumulativeForce;

            var beamDir = new Vector3D(
                beam.NodeJ.Coord.X - beam.NodeI.Coord.X,
                beam.NodeJ.Coord.Y - beam.NodeI.Coord.Y,
                beam.NodeJ.Coord.Z - beam.NodeI.Coord.Z);
            Matrix<double> t = Utils.GetNodeTransformMatrix(beamDir);

            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
            string format = "{0:N" + viewModel.DecimalPlaces + "}";

            // Fy: indices [1, 7], forceDirection [0, 1, 0]
            // Fz: indices [2, 8], forceDirection [0, 0, 1]
            var components = new[]
            {
                (name: "Fy", idxI: 1, idxJ: 7, dir: Vector<double>.Build.DenseOfArray(new[] { 0.0, 1.0, 0.0 })),
                (name: "Fz", idxI: 2, idxJ: 8, dir: Vector<double>.Build.DenseOfArray(new[] { 0.0, 0.0, 1.0 })),
            };

            // 上面図モード: dir をローカル X 軸 (= 梁軸) 周りに 90° 回転 (DrawFoundationBeamMyMz と同様)
            bool rotateToHorizontal = viewModel.IsFoundationBeamStressRotatedToHorizontal;

            foreach (var (name, idxI, idxJ, dir) in components)
            {
                double origI = bf.GetByIndex(idxI);
                double origJ = bf.GetByIndex(idxJ);
                if (!double.IsFinite(origI) || !double.IsFinite(origJ)) continue;

                var localDir = rotateToHorizontal
                    ? Vector<double>.Build.DenseOfArray(new[] { dir[0], -dir[2], dir[1] })
                    : dir;
                var transformedDir = t.Transpose() * localDir;

                double fI = maxAbsValue == 0 ? 0 : origI / maxAbsValue * forceScale;
                double fJ = maxAbsValue == 0 ? 0 : origJ / maxAbsValue * forceScale;

                Point3D nodeI3D = beam.NodeI.Coord;
                Point3D nodeJ3D = beam.NodeJ.Coord;

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
                // Mh と同様に Fh も合成量 (≥0) で色スケールが組まれているため
                // 符号付き Fy/Fz を渡すと負側が一律 dark blue になる。|成分| を渡す。
                List<double> values = [Math.Abs(origI), Math.Abs(origI), Math.Abs(origJ), Math.Abs(origJ)];
                AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);

                if (viewModel.IsResultValueVisible)
                {
                    DrawResultValueTexts(
                        viewModel.IsResultValueVisible, Brushes.Black,
                        origI, -origJ,
                        nodeIForce2D, nodeJForce2D,
                        nodeJ2D, nodeI2D,
                        format, format, true);
                }
            }
        }

        private void DrawResultValueTexts(
            bool isVisible, Brush solidColorBrush,
            double valueI, double valueJ,
            Point pointI, Point pointJ,
            Point nodeJ2D, Point nodeI2D,
            string formatI, string formatJ,
            bool isFoundationBeam = false)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (!isVisible) return;
            if (viewModel.IsMidSpanResultValueVisibleOnly && !isFoundationBeam)
            {
                // 符号付き平均: 軸力 (Fx) は圧縮負を維持、ほか応力も I/J 同符号ならその符号で表示。
                // (旧実装は Math.Abs 平均で符号を失っていたが、カラーバー (符号付き) と整合させるため修正)
                double value = (valueI + valueJ) / 2;
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

        // 地盤反力描画ヘルパー（UpdateAnalysisResult3D 内から呼び出してください）
        private void DrawHorizontalSoilSpringsResult3D(MainWindowViewModel viewModel, AnaModel anaModel, HashSet<HorizontalSoilSpring> visibleSoilSprings = null)
        {
            if (viewModel == null || anaModel == null) return;
            if (anaModel.HorizontalSoilSprings == null || anaModel.HorizontalSoilSprings.Count == 0) return;
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            // 選択された荷重ケース・組合せを取得
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

            // 荷重ケースに対応する結果を検索するヘルパー
            // 重要: FirstOrDefault で先頭ステップを拾うと途中段階の小さい値になるため、
            // Step が最大（= 最終収束ステップ）のものを取得する。
            HorizontalSpringResult FindSpringResult(HorizontalSoilSpring spring)
            {
                if (spring.HorizontalSpringResults == null || selectedLoadCase == null || selectedLoadCombination == null)
                    return null;
                return spring.HorizontalSpringResults
                    .Where(r =>
                        r.LoadCase?.LoadName == selectedLoadCase.LoadName &&
                        r.LoadCombination?.Name == selectedLoadCombination.Name &&
                        r.IsLiquefaction == viewModel.IsLiquefaction)
                    .OrderByDescending(r => r.Step)
                    .FirstOrDefault();
            }

            // 選択されたタイプを取得
            string springType = viewModel.AnalysisResultSoilSpringType ?? "RH";

            // AP 非表示時は AP 連結ばね (土圧合力ばね) を全フェーズ (集計/カラーバー/描画) で除外
            bool apHiddenForCollect = viewModel?.IsActionPointVisible == false;
            // ばねの可視性判定: 杭関連はピル可視性、根入部関連 (土圧合力ばね) は AP 可視性
            // ローカル関数化することで 3 箇所のループで共通利用
            bool IsSpringVisible(HorizontalSoilSpring sp)
            {
                if (sp == null) return false;
                bool isDoatsu = sp.Name != null && sp.Name.StartsWith("土圧合力ばね");
                if (isDoatsu) return !apHiddenForCollect;
                // 杭関連: visibleSoilSprings が null (全杭可視、または対応付けが取れず
                //         絞り込みを諦めた場合) なら全部表示。
                //         非 null ならセットに含まれるもののみ表示
                //         (全杭非表示 → セット空 → false で全部スキップ。これは意図どおり)
                return visibleSoilSprings == null || visibleSoilSprings.Contains(sp);
            }

            // 1) 全ばねの値を収集（カラーバー用） — 非アクティブ杭のばねはスキップ
            var allValues = new ObservableCollection<double>();
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (!IsSpringVisible(s)) continue;

                try
                {
                    // 選択された荷重ケースの結果を取得
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue; // 結果がない場合はスキップ

                    // 結果をばねに復元してから値を取得
                    s.CumulativeForce = result.CumulativeForce;
                    if (result.CumulativeDisp != null) s.CumulativeDisp = result.CumulativeDisp;

                    // 選択されたタイプに応じた値を取得
                    double value = GetSoilSpringValue(s, springType);
                    if (double.IsFinite(value)) // NaN/Infinity防止
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

            // 2) 反力の最大絶対値を求める（正規化用プレパス）— 結果は既に復元済み
            double maxAbsValue = 0;
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (!IsSpringVisible(s)) continue;
                try
                {
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue;
                    double v = Math.Abs(GetSoilSpringValue(s, springType));
                    if (double.IsFinite(v) && v > maxAbsValue) maxAbsValue = v;
                }
                catch (Exception ex) { Log.Warning(ex, "[CanvasResults] ばね反力取得失敗"); }
            }
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;

            // 3) 各ばねについて、反力の方向と大きさに基づいて矢印 or バブルを描画
            // バブルモード (IsBubbleVisible=true): NodeI 位置に真円 (ビュー方向に依存しない) を描画
            // 矢印モード (デフォルト): 反力方向に矢印を描画
            // 両モード同時有効も可 (バブル + 矢印重ね描き)
            bool useBubble = viewModel?.IsBubbleVisible == true;
            // デフォルト (両方 OFF) は矢印描画 (従来動作)
            bool useArrow = viewModel?.IsArrowVisible == true || !useBubble;
            double bubbleDia = viewModel?.BubbleDia ?? 30.0;
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (s?.NodeI == null || s.NodeJ == null) continue;
                // 可視性フィルタ (集計フェーズと統一):
                //   杭関連 (杭地盤ばね-* / 杭Zばね-*) は pile 単位の可視性で制御
                //   根入部関連 (土圧合力ばね) は AP 可視性のみで制御 (pile とは独立)
                if (!IsSpringVisible(s)) continue;

                try
                {
                    // 選択された荷重ケースの結果がない場合はスキップ
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue;

                    // 選択されたタイプに応じた値を取得
                    double displayValue = GetSoilSpringValue(s, springType);
                    if (!double.IsFinite(displayValue)) continue; // NaN/Infinity防止

                    // 選択された反力タイプを持たないばねを除外:
                    // 例) RZ 表示時、水平地盤ばね (杭地盤ばね-*) や土圧合力ばねは Fz=0 のため
                    //     "0.0" ラベルが杭周りに乱立する。
                    // 最大絶対値に対して相対 0.1% 未満を「事実上ゼロ」とみなし、矢印もラベルも描画しない。
                    if (maxAbsValue > 1e-15 && Math.Abs(displayValue) / maxAbsValue < 1.0e-3) continue;

                    // CumulativeForce = Ke·disp は「変位状態を保つのに必要な節点等価外力」
                    // ＝ k·(u_pile − u_soil) の符号を持つ。ユーザが期待する「地盤が杭に及ぼす反力」
                    // （= −k·(u_pile − u_soil) = 杭の相対変位を抑制する方向）に揃えるため、
                    // 描画用には符号を反転する。
                    double fx = -s.CumulativeForce.GetByIndex(0);
                    double fy = -s.CumulativeForce.GetByIndex(1);
                    double fz = -s.CumulativeForce.GetByIndex(2);

                    // 選択されたタイプに応じた反力方向ベクトル（反転済みの反力成分）
                    var forceDir = springType switch
                    {
                        "RX" => new System.Windows.Media.Media3D.Vector3D(fx, 0, 0),
                        "RY" => new System.Windows.Media.Media3D.Vector3D(0, fy, 0),
                        "RZ" => new System.Windows.Media.Media3D.Vector3D(0, 0, fz),
                        "RH" => new System.Windows.Media.Media3D.Vector3D(fx, fy, 0),  // 水平合反力 (Z 成分は除外、MH と整合)
                        "R" => new System.Windows.Media.Media3D.Vector3D(fx, fy, fz),  // 3D 合反力
                        _ => new System.Windows.Media.Media3D.Vector3D(fx, fy, 0)
                    };

                    // 表示スケール: 最大反力絶対値で正規化し、比率×ModelExtentを適用
                    // 符号は forceDirNorm が持つため、長さは絶対値ベース。
                    // これにより RX/RY/RZ で正負によって矢印が逆向きに描かれる (符号整合)。
                    double arrowLength3D = maxAbsValue > 1e-15
                        ? Math.Abs(displayValue) / maxAbsValue * forceScale
                        : 0;

                    // 反力方向を正規化
                    double forceDirLen = forceDir.Length;
                    var forceDirNorm = forceDirLen > 1e-15
                        ? forceDir / forceDirLen
                        : new System.Windows.Media.Media3D.Vector3D(1, 0, 0);

                    // 矢印の頂点（杭側ノード = I点）と尾
                    var head3D = s.NodeI.Coord;
                    var tail3D = new System.Windows.Media.Media3D.Point3D(
                        head3D.X - forceDirNorm.X * arrowLength3D,
                        head3D.Y - forceDirNorm.Y * arrowLength3D,
                        head3D.Z - forceDirNorm.Z * arrowLength3D
                    );

                    // 2D投影
                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);

                    // NaN/Infinity座標チェック
                    if (!double.IsFinite(head2D.X) || !double.IsFinite(head2D.Y)
                        || !double.IsFinite(tail2D.X) || !double.IsFinite(tail2D.Y))
                        continue;

                    // カラー帯の選択（選択されたタイプの値で色分け）
                    var picked = ColorBarUtils.PickColorGeometry(displayValue, colorBaredGeometries)
                                 ?? ColorBarUtils.PickColorGeometryInclusiveTop(displayValue, colorBaredGeometries)
                                 ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                    if (picked == null) continue;

                    // ===== バブル描画 (真円、ビュー方向に依存しない) =====
                    if (useBubble)
                    {
                        double bubbleDia2D = maxAbsValue > 1e-15
                            ? bubbleDia * Math.Abs(displayValue) / maxAbsValue
                            : 0;
                        if (bubbleDia2D > 0)
                        {
                            // 真円: flattening を掛けず X/Y 同径
                            var bubble = new EllipseGeometry(head2D, bubbleDia2D * 0.5, bubbleDia2D * 0.5);
                            picked.PathGeometry.AddGeometry(bubble);

                            if (viewModel.IsResultValueVisible)
                            {
                                string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                                AddText3D(Brushes.Black, string.Format(fmt, displayValue), head2D.X, head2D.Y, "C", "C", 0);
                            }
                        }
                    }

                    // ===== 矢印描画 =====
                    if (useArrow)
                    {
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
                    // 矢印の尾側に配置（杭本体との重なりを避けるため head よりも外側）
                    if (viewModel.IsResultValueVisible)
                    {
                        string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                        // 尾から外向きに少しオフセットして重なりを防ぐ
                        double labelOffset = viewModel.ArrowHeadLength * 0.5;
                        Point labelPos = tail2D - dirNorm * labelOffset;
                        AddText3D(Brushes.Black, string.Format(fmt, displayValue), labelPos.X, labelPos.Y, "C", "C", 0);
                    }
                    }
                }
                catch
                {
                    // 個別失敗は無視して続行
                }
            }

            // 3b) 杭先端反力の描画（RH / RZ / R 選択時）
            if (springType == "RH" || springType == "RZ" || springType == "R")
            {
                foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                {
                    if (!IsPileVisibleForResult(viewModel, pile)) continue;
                    var tipNode = pile.PileNodes?.LastOrDefault();
                    if (tipNode?.CumulativeReaction == null) continue;

                    double tipFx = tipNode.CumulativeReaction.Fx;
                    double tipFy = tipNode.CumulativeReaction.Fy;
                    double tipFz = tipNode.CumulativeReaction.Fz;

                    // RH選択時: 水平成分 sqrt(Fx²+Fy²) のみ描画 (鉛直成分は RZ で別途)
                    // RZ選択時: Fz のみ描画
                    // R 選択時: 3D 合反力 sqrt(Fx²+Fy²+Fz²) を描画 (RH/RZ の合算と等価)
                    var components = new System.Collections.Generic.List<(double value, System.Windows.Media.Media3D.Vector3D dir)>();

                    if (springType == "RH")
                    {
                        // 水平合反力のみ描画 (Z は別タイプ RZ で表示する)
                        double tipRH = Math.Sqrt(tipFx * tipFx + tipFy * tipFy);
                        if (double.IsFinite(tipRH) && tipRH > 1e-15)
                            components.Add((tipRH, new System.Windows.Media.Media3D.Vector3D(tipFx, tipFy, 0)));
                    }
                    else if (springType == "RZ")
                    {
                        if (double.IsFinite(tipFz) && Math.Abs(tipFz) > 1e-15)
                            components.Add((tipFz, new System.Windows.Media.Media3D.Vector3D(0, 0, tipFz)));
                    }
                    else // R
                    {
                        double tipR = Math.Sqrt(tipFx * tipFx + tipFy * tipFy + tipFz * tipFz);
                        if (double.IsFinite(tipR) && tipR > 1e-15)
                            components.Add((tipR, new System.Windows.Media.Media3D.Vector3D(tipFx, tipFy, tipFz)));
                    }

                    foreach (var (tipValue, tipForceDir) in components)
                    {
                        allValues.Add(tipValue);

                        double tipForceDirLen = tipForceDir.Length;
                        var tipDirNorm = tipForceDirLen > 1e-15
                            ? tipForceDir / tipForceDirLen
                            : new System.Windows.Media.Media3D.Vector3D(0, 0, -1);

                        // 符号は tipDirNorm が持つため、長さは絶対値ベース (RZ で正負で向き反転)
                        double tipArrowLen = maxAbsValue > 1e-15 ? Math.Abs(tipValue) / maxAbsValue * forceScale : 0;

                        var tipHead3D = tipNode.Coord;
                        var tipTail3D = new System.Windows.Media.Media3D.Point3D(
                            tipHead3D.X - tipDirNorm.X * tipArrowLen,
                            tipHead3D.Y - tipDirNorm.Y * tipArrowLen,
                            tipHead3D.Z - tipDirNorm.Z * tipArrowLen);

                        Point tipHead2D = viewModel.CanvasThreeDView.Transformation(tipHead3D);
                        Point tipTail2D = viewModel.CanvasThreeDView.Transformation(tipTail3D);
                        if (!double.IsFinite(tipHead2D.X) || !double.IsFinite(tipTail2D.X)) continue;

                        var tipPicked = ColorBarUtils.PickColorGeometry(tipValue, colorBaredGeometries)
                                        ?? ColorBarUtils.PickColorGeometryInclusiveTop(tipValue, colorBaredGeometries)
                                        ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                        if (tipPicked == null) continue;

                        tipPicked.PathGeometry.AddGeometry(new LineGeometry(tipTail2D, tipHead2D));

                        // 矢印ヘッド
                        double tipArrowDia = viewModel.ArrowHeadDia;
                        Vector tipDir = tipHead2D - tipTail2D;
                        double tipDirLen2D = tipDir.Length;
                        Vector tipDirNorm2D = tipDirLen2D > 1e-9 ? tipDir / tipDirLen2D : new Vector(0, -1);
                        Point tipCenterEllipse = tipHead2D - tipDirNorm2D * (viewModel.ArrowHeadLength * 0.4);
                        tipPicked.PathGeometry.AddGeometry(new EllipseGeometry(tipCenterEllipse, tipArrowDia * 0.5, tipArrowDia * 0.5 * viewModel.CanvasThreeDView.Flattening));
                        Vector tipOrtho = GetUnitOrthogonalVector(tipDirNorm2D);
                        Point tipSide1 = tipCenterEllipse - tipDirNorm2D * (viewModel.ArrowHeadLength * 0.6) + tipOrtho * (tipArrowDia * 0.5);
                        Point tipSide2 = tipCenterEllipse - tipDirNorm2D * (viewModel.ArrowHeadLength * 0.6) - tipOrtho * (tipArrowDia * 0.5);
                        tipPicked.PathGeometry.AddGeometry(new LineGeometry(tipHead2D, tipSide1));
                        tipPicked.PathGeometry.AddGeometry(new LineGeometry(tipHead2D, tipSide2));

                        if (viewModel.IsResultValueVisible)
                        {
                            string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                            AddText3D(Brushes.Black, string.Format(fmt, tipValue), (tipHead2D.X + tipTail2D.X) * 0.5, (tipHead2D.Y + tipTail2D.Y) * 0.5, "C", "C", 0);
                        }
                    }
                }
            }

            // 3c) Path を Canvas に描画
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
        /// 地盤反力から選択されたタイプに応じた値を取得
        /// </summary>
        private static double GetSoilSpringValue(FEM.HorizontalSoilSpring spring, string springType)
        {
            // I端の力・モーメント成分を取得
            // Index 0-2: Fx, Fy, Fz (並進力)
            // Index 3-5: Mx, My, Mz (モーメント)
            // CumulativeForce = Ke·disp は「変位状態を保つのに必要な節点等価外力」で、
            // ユーザが期待する「地盤が杭に及ぼす反力」（＝杭変位を抑制する向き）とは逆符号。
            // ここでは反力として表示するため符号を反転する。
            // 案 Z モードでは N0 が FEM 外力として与えられているため、CumulativeForce が既に物理反力を表す。
            // (旧 Phase Y では PreLoadForce 加算で N0 寄与を補っていたが、案 Z 採用により不要となり削除)
            double fx = -spring.CumulativeForce.GetByIndex(0);
            double fy = -spring.CumulativeForce.GetByIndex(1);
            double fz = -spring.CumulativeForce.GetByIndex(2);
            double mx = -spring.CumulativeForce.GetByIndex(3);
            double my = -spring.CumulativeForce.GetByIndex(4);
            double mz = -spring.CumulativeForce.GetByIndex(5);

            return springType switch
            {
                "RX" => fx,
                "RY" => fy,
                "RZ" => fz,
                "RH" => Math.Sqrt(fx * fx + fy * fy),  // 水平合反力 (絶対値、Z 成分は除外、MH と整合)
                "R" => Math.Sqrt(fx * fx + fy * fy + fz * fz),  // 全合反力 (絶対値、3D)
                "MX" => mx,
                "MY" => my,
                "MZ" => mz,
                "MH" => Math.Sqrt(mx * mx + my * my),  // 水平モーメント（絶対値）
                _ => Math.Sqrt(fx * fx + fy * fy)      // デフォルトは RH
            };
        }

        /// <summary>
        /// 地盤反力タイプの表示名を取得
        /// </summary>
        private static string GetSoilSpringTypeName(string springType)
        {
            return springType switch
            {
                "RX" => "地盤反力RX",
                "RY" => "地盤反力RY",
                "RZ" => "地盤反力RZ",
                "RH" => "地盤反力RH",
                "R" => "地盤反力R",
                "MX" => "地盤反力MX",
                "MY" => "地盤反力MY",
                "MZ" => "地盤反力MZ",
                "MH" => "地盤反力MH",
                _ => "地盤反力RH"
            };
        }

        // ========================================
        // 地盤反力 分布形状描画 (単位長さ当たり kN/m)
        // ========================================

        /// <summary>
        /// HorizontalSoilSpring の杭側節点 (NodeI) に対する分担長 (m) を返す。
        /// 計算法: 当該節点に接続するすべての FEM 梁要素のうち
        ///   HorizontalSoilReactionItem を持つ梁の <see cref="FEM.Beam"/> 物理長 (NodeI-NodeJ 距離)
        /// を集計し、各梁の半長 (L/2) を合計する。
        /// MgtExporter の `(ZTop-ZBtm)*0.5` は単一の反力アイテムに着目しており、要素分割下では
        /// 元の反力区間長を流用してしまう恐れがあるため、本実装は FEM 上の実梁長 (NodeI.Coord-NodeJ.Coord)
        /// を直接用いて分担長を計算する。
        /// </summary>
        private static double GetSpringTributaryLength(FEM.HorizontalSoilSpring s, FEM.AnaModel anaModel)
        {
            return SoilReactionUtil.GetNodeTributaryLength(s?.NodeI, anaModel);
        }

        /// <summary>
        /// 地盤反力を「単位長さ当たり (kN/m)」の分布形状として描画する。
        ///   各杭ごとに以下を行う:
        ///     1) 杭に紐づく HorizontalSoilSpring を Z 降順 (浅→深) で並べる
        ///     2) 各ばねの kN/m = 節点反力 / 分担長 を計算
        ///     3) ばね Z 位置から、選択タイプに応じた水平方向に距離 (kN/m × scale) のオフセット点を作る
        ///     4) 各ばね位置: 杭軸 → オフセット点 へ短い梯子線
        ///     5) 隣接ばねのオフセット点同士を結んで分布の外輪郭線を描く
        /// 並進反力のみ対応 (RH/RX/RY/RZ)。モーメント (MX/MY/MZ/MH) は RH にフォールバック。
        /// </summary>
        private void DrawHorizontalSoilReactionDistribution3D(MainWindowViewModel viewModel, FEM.AnaModel anaModel, HashSet<FEM.HorizontalSoilSpring> visibleSoilSprings = null)
        {
            if (viewModel == null || anaModel == null) return;
            if (anaModel.HorizontalSoilSprings == null || anaModel.HorizontalSoilSprings.Count == 0) return;
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            // 選択された荷重ケース・組合せ
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

            FEM.HorizontalSpringResult FindSpringResult(FEM.HorizontalSoilSpring spring)
            {
                if (spring.HorizontalSpringResults == null || selectedLoadCase == null || selectedLoadCombination == null)
                    return null;
                return spring.HorizontalSpringResults
                    .Where(r =>
                        r.LoadCase?.LoadName == selectedLoadCase.LoadName &&
                        r.LoadCombination?.Name == selectedLoadCombination.Name &&
                        r.IsLiquefaction == viewModel.IsLiquefaction)
                    .OrderByDescending(r => r.Step)
                    .FirstOrDefault();
            }

            // 並進タイプのみサポート。モーメント系が選択されている場合は RH にフォールバック。
            string springType = viewModel.AnalysisResultSoilSpringType ?? "RH";
            if (springType is "MX" or "MY" or "MZ" or "MH") springType = "RH";

            // 1) 全 (杭, ばね) ペアで kN/m を計算し、カラーバー用に集約
            //    spring → (pile, value_per_m, fxNorm, fyNorm, fzNorm) のリストも構築
            var perPile = new Dictionary<Models.InputData.PileLayoutDataItem, List<(FEM.HorizontalSoilSpring s, double value, double tributary, double fx, double fy, double fz)>>();
            var allValues = new ObservableCollection<double>();

            foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;
                if (pile.PileNodes == null) continue;
                var pileNodeSet = new HashSet<FEM.Node>(pile.PileNodes);

                var pileSprings = anaModel.HorizontalSoilSprings
                    .Where(s => s != null && s.NodeI != null && pileNodeSet.Contains(s.NodeI))
                    .Where(s => visibleSoilSprings == null || visibleSoilSprings.Count == 0 || visibleSoilSprings.Contains(s))
                    .OrderByDescending(s => s.NodeI.Coord.Z)  // 上→下
                    .ToList();
                if (pileSprings.Count == 0) continue;

                var list = new List<(FEM.HorizontalSoilSpring, double, double, double, double, double)>();
                foreach (var s in pileSprings)
                {
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue;
                    s.CumulativeForce = result.CumulativeForce;
                    if (result.CumulativeDisp != null) s.CumulativeDisp = result.CumulativeDisp;

                    double tributary = GetSpringTributaryLength(s, anaModel);
                    if (!(tributary > 1e-9)) continue;

                    // 「地盤が杭に及ぼす反力」の向き (節点等価外力の符号反転)
                    double fx = -s.CumulativeForce.GetByIndex(0);
                    double fy = -s.CumulativeForce.GetByIndex(1);
                    double fz = -s.CumulativeForce.GetByIndex(2);

                    double signed = springType switch
                    {
                        "RX" => fx,
                        "RY" => fy,
                        "RZ" => fz,
                        "RH" => Math.Sqrt(fx * fx + fy * fy),  // 水平合反力 (絶対値)
                        _ => Math.Sqrt(fx * fx + fy * fy),
                    };
                    double valuePerM = signed / tributary;  // kN/m
                    if (!double.IsFinite(valuePerM)) continue;

                    list.Add((s, valuePerM, tributary, fx, fy, fz));
                    allValues.Add(valuePerM);
                }
                if (list.Count > 0) perPile[pile] = list;
            }

            if (allValues.Count == 0)
            {
                ColorBarCanvas.Children.Clear();
                return;
            }

            // 2) カラーバー用ジオメトリと正規化
            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);
            double maxAbsValue = allValues.Select(v => Math.Abs(v)).Where(double.IsFinite).DefaultIfEmpty(0).Max();
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
            string unit = "kN/m";
            string colorBarTitle = GetSoilSpringTypeName(springType) + " (分布)";

            // 3) 各杭ごとに分布外輪を描画
            foreach (var kv in perPile)
            {
                var list = kv.Value;
                // 各ばね位置に対し、3D オフセット点を作成
                var outerPoints3D = new List<(System.Windows.Media.Media3D.Point3D baseP, System.Windows.Media.Media3D.Point3D outerP, double value)>();

                foreach (var (s, value, tributary, fx, fy, fz) in list)
                {
                    // 反力方向ベクトル (kN/m 値の符号は signed = value × tributary の符号と一致)
                    var forceDir = springType switch
                    {
                        "RX" => new System.Windows.Media.Media3D.Vector3D(fx, 0, 0),
                        "RY" => new System.Windows.Media.Media3D.Vector3D(0, fy, 0),
                        "RZ" => new System.Windows.Media.Media3D.Vector3D(0, 0, fz),
                        "RH" => new System.Windows.Media.Media3D.Vector3D(fx, fy, 0),
                        _ => new System.Windows.Media.Media3D.Vector3D(fx, fy, 0),
                    };
                    double dirLen = forceDir.Length;
                    var dirNorm = dirLen > 1e-15
                        ? forceDir / dirLen
                        : new System.Windows.Media.Media3D.Vector3D(1, 0, 0);

                    // 外側オフセット距離 (3D): 値 × scale / maxAbsValue
                    double offset = maxAbsValue > 1e-15 ? value / maxAbsValue * forceScale : 0;
                    var baseP = s.NodeI.Coord;
                    var outerP = new System.Windows.Media.Media3D.Point3D(
                        baseP.X + dirNorm.X * offset,
                        baseP.Y + dirNorm.Y * offset,
                        baseP.Z + dirNorm.Z * offset);
                    outerPoints3D.Add((baseP, outerP, value));
                }

                // 3-a) 各ばね位置: 杭軸 → 外側点 の水平線 (色は値による)
                foreach (var (baseP, outerP, value) in outerPoints3D)
                {
                    Point base2D = viewModel.CanvasThreeDView.Transformation(baseP);
                    Point outer2D = viewModel.CanvasThreeDView.Transformation(outerP);
                    if (!double.IsFinite(base2D.X) || !double.IsFinite(outer2D.X)) continue;

                    var picked = ColorBarUtils.PickColorGeometry(value, colorBaredGeometries)
                                 ?? ColorBarUtils.PickColorGeometryInclusiveTop(value, colorBaredGeometries)
                                 ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                    if (picked == null) continue;

                    picked.PathGeometry.AddGeometry(new LineGeometry(base2D, outer2D));

                    // 値ラベル (オフセット先端付近)
                    if (viewModel.IsResultValueVisible)
                    {
                        string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                        Vector dir2D = outer2D - base2D;
                        double dirLen2D = dir2D.Length;
                        Vector dirN = dirLen2D > 1e-9 ? dir2D / dirLen2D : new Vector(1, 0);
                        Point labelPos = outer2D + dirN * 4.0;
                        AddText3D(Brushes.Black, string.Format(fmt, value), labelPos.X, labelPos.Y, "C", "C", 0);
                    }
                }

                // 3-b) 外輪: 隣接ばねの outer 点を線で結ぶ (杭軸沿いに連結して分布形状の縁を作る)
                for (int i = 0; i + 1 < outerPoints3D.Count; i++)
                {
                    var p1 = outerPoints3D[i];
                    var p2 = outerPoints3D[i + 1];
                    Point a = viewModel.CanvasThreeDView.Transformation(p1.outerP);
                    Point b = viewModel.CanvasThreeDView.Transformation(p2.outerP);
                    if (!double.IsFinite(a.X) || !double.IsFinite(b.X)) continue;

                    // 区間の代表値 = 2 端の平均で色決定
                    double midValue = 0.5 * (p1.value + p2.value);
                    var picked = ColorBarUtils.PickColorGeometry(midValue, colorBaredGeometries)
                                 ?? ColorBarUtils.PickColorGeometryInclusiveTop(midValue, colorBaredGeometries)
                                 ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                    if (picked == null) continue;

                    picked.PathGeometry.AddGeometry(new LineGeometry(a, b));
                }
            }

            // 4) Path を Canvas に描画
            foreach (var geo in colorBaredGeometries)
            {
                geo.DrawPathes(Canvas3DLayout);
            }

            // 5) カラーバー
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

        // ========================================
        // 杭頭M/Qマップ描画
        // ========================================

        /// <summary>
        /// 杭頭Mマップ / 杭頭Qマップ を描画
        /// 各杭の最上段要素（IsPileHeadElement）の i 端応力を使用
        /// </summary>
        private void DrawPileHeadForceMap(MainWindowViewModel viewModel, AnaModel anaModel)
        {
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            string content = viewModel.AnalysisResultContent;
            bool isMoment = content.Contains('M');

            // 選択された荷重ケース・組合せ
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

            var entries = new System.Collections.Generic.List<(Point3D location, double valueX, double valueY, double valueMag)>();

            // 杭頭Beam要素のリスト（各杭の最上段）
            var pileTopBeams = anaModel.Beams?.Where(b => b.IsPileHeadElement).ToList()
                               ?? new System.Collections.Generic.List<FEM.Beam>();

            foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;

                // 杭配置のXY座標に最も近い杭頭Beam要素を検索
                var topBeam = pileTopBeams.FirstOrDefault(b =>
                    b.PileBodyNo is int pb && pb == pile.PileBodyNo &&
                    b.NodeI != null &&
                    Math.Abs(b.NodeI.Coord.X - pile.Point3D.X) < 0.01 &&
                    Math.Abs(b.NodeI.Coord.Y - pile.Point3D.Y) < 0.01);
                if (topBeam == null) continue;
                var node = topBeam.NodeI;
                if (node == null) continue;

                // 選択された荷重ケースの結果を取得
                var beamResult = topBeam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                var bf = beamResult?.CumulativeForce;
                if (bf == null) continue; // 結果がない場合はスキップ

                // 要素座標系 → 全体座標系に変換: f_global = T^T * f_local
                var f_local = bf.GetVector();
                var T = topBeam.GetCachedCoordTransform();
                var f_global = T.Transpose() * f_local;

                // f_global: [Fxi,Fyi,Fzi,Mxi,Myi,Mzi, Fxj,Fyj,Fzj,Mxj,Myj,Mzj] (全体座標系)
                double vx, vy;
                if (isMoment)
                {
                    // 杭頭Mマップ: i端モーメント（全体座標系 Mx, My）
                    vx = f_global[3]; // Mxi_global
                    vy = f_global[4]; // Myi_global
                }
                else
                {
                    // 杭頭Qマップ: i端せん断力（全体座標系 Fx, Fy）
                    vx = f_global[0]; // Fxi_global
                    vy = f_global[1]; // Fyi_global
                }

                double mag = Math.Sqrt(vx * vx + vy * vy);
                if (!double.IsFinite(mag)) continue;

                entries.Add((node.Coord, vx, vy, mag));
            }

            if (entries.Count == 0) { ColorBarCanvas.Children.Clear(); return; }

            // カラーバー
            var allValues = new ObservableCollection<double>(entries.Select(e => e.valueMag));
            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);
            double maxVal = entries.Max(e => e.valueMag);
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
            string unit = isMoment ? "kNm" : "kN";
            string title = content;

            foreach (var (location, vx, vy, mag) in entries)
            {
                if (mag < 1e-15) continue;

                Point center2D = viewModel.CanvasThreeDView.Transformation(location);
                if (!double.IsFinite(center2D.X)) continue;

                double arrowLen = maxVal > 1e-15 ? mag / maxVal * forceScale : 0;

                var picked = ColorBarUtils.PickColorGeometry(mag, colorBaredGeometries)
                             ?? ColorBarUtils.PickColorGeometryInclusiveTop(mag, colorBaredGeometries)
                             ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                if (picked == null) continue;

                if (isMoment)
                {
                    // モーメント: 右ねじの法則に従った方向に二重矢印
                    // M=(Mx,My) → 右ねじ: 回転軸方向 = (Mx,My) の方向
                    var forceDir3D = new Vector3D(vx, vy, 0);
                    double forceDirLen = forceDir3D.Length;
                    var dirNorm3D = forceDirLen > 1e-15 ? forceDir3D / forceDirLen : new Vector3D(1, 0, 0);

                    var tail3D = location;
                    var head3D = new Point3D(
                        location.X + dirNorm3D.X * arrowLen,
                        location.Y + dirNorm3D.Y * arrowLen,
                        location.Z);

                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);
                    if (!double.IsFinite(head2D.X) || !double.IsFinite(tail2D.X)) continue;

                    // 軸線
                    picked.PathGeometry.AddGeometry(new LineGeometry(tail2D, head2D));

                    // 先端に二重矢印
                    double headLen = viewModel.ArrowHeadLength * 0.8;
                    DrawDoubleArrowHead(picked.PathGeometry, head2D, tail2D, headLen);
                }
                else
                {
                    // せん断力: その方向に矢印
                    var forceDir3D = new Vector3D(vx, vy, 0);
                    double forceDirLen = forceDir3D.Length;
                    var dirNorm3D = forceDirLen > 1e-15 ? forceDir3D / forceDirLen : new Vector3D(1, 0, 0);

                    var head3D = location;
                    var tail3D = new Point3D(
                        head3D.X - dirNorm3D.X * arrowLen,
                        head3D.Y - dirNorm3D.Y * arrowLen,
                        head3D.Z - dirNorm3D.Z * arrowLen);

                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);
                    if (!double.IsFinite(head2D.X) || !double.IsFinite(tail2D.X)) continue;

                    picked.PathGeometry.AddGeometry(new LineGeometry(tail2D, head2D));

                    // 矢印ヘッド
                    Vector dir2D = head2D - tail2D;
                    double dirLen2D = dir2D.Length;
                    Vector dirNorm2D = dirLen2D > 1e-9 ? dir2D / dirLen2D : new Vector(0, -1);
                    double headLen = viewModel.ArrowHeadLength;
                    Vector ortho = GetUnitOrthogonalVector(dirNorm2D);
                    Point side1 = head2D - dirNorm2D * headLen + ortho * (headLen * 0.4);
                    Point side2 = head2D - dirNorm2D * headLen - ortho * (headLen * 0.4);
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side1));
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side2));
                }

                // 値ラベル
                if (viewModel.IsResultValueVisible)
                {
                    string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                    AddText3D(Brushes.Black, string.Format(fmt, mag), center2D.X, center2D.Y + 8, "C", "T", 0);
                }
            }

            // Path を Canvas に描画
            foreach (var geo in colorBaredGeometries)
                geo.DrawPathes(Canvas3DLayout);

            // カラーバー
            if (allValues.Count > 0)
            {
                ColorBar.DrawStepColorBar(
                    ColorBarCanvas, colorBaredGeometries, title, unit,
                    allValues.Min(), allValues.Max(),
                    "{0:N" + viewModel.DecimalPlaces + "}", viewModel.LabelSize);
            }
        }

        /// <summary>
        /// 接合点（ConnectionNode）の力マップを描画する
        /// RigidLinkビームのI端（ConnectionNode側）の力/モーメントを使用
        /// </summary>
        private void DrawConnectionPointForceMap(MainWindowViewModel viewModel, AnaModel anaModel)
        {
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            string content = viewModel.AnalysisResultContent;
            bool isMoment = content.Contains('M');

            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (selectedLoadCase == null || selectedLoadCombination == null) return;

            var entries = new System.Collections.Generic.List<(Point3D location, double valueX, double valueY, double valueMag)>();

            // RigidLinkビームを検索
            var rigidLinkBeams = anaModel.Beams?.Where(b => b.Name.StartsWith("RigidLink-")).ToList()
                                 ?? new System.Collections.Generic.List<FEM.Beam>();

            foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
            {
                if (!IsPileVisibleForResult(viewModel, pile)) continue;

                Beam targetBeam = null;
                Node targetNode = null;
                bool usedRigidLink = false;

                // まずRigidLinkビームを探す
                var rigidLink = rigidLinkBeams.FirstOrDefault(b =>
                    b.Name == $"RigidLink-{pile.No}" && b.NodeI != null);
                if (rigidLink != null)
                {
                    targetBeam = rigidLink;
                    targetNode = rigidLink.NodeI; // ConnectionNode
                    usedRigidLink = true;
                }
                else
                {
                    // RigidLinkが無い場合（基礎梁未設定 / ΔZc≈0）: 杭頭ビームのI端力を取得し、
                    // 接合点標高（杭頭 + ΔZc）へ剛オフセット移送して接合点の応力として表示する。
                    var pileTopBeam = pile.Beams?.FirstOrDefault(b => b.IsPileHeadElement);
                    if (pileTopBeam != null)
                    {
                        targetBeam = pileTopBeam;
                        targetNode = pileTopBeam.NodeI;
                    }
                }

                if (targetBeam == null || targetNode == null) continue;

                var beamResult = targetBeam.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                var bf = beamResult?.CumulativeForce;
                if (bf == null) continue;

                // 要素座標系 → 全体座標系に変換
                var f_local = bf.GetVector();
                var T = targetBeam.GetCachedCoordTransform();
                var f_global = T.Transpose() * f_local;

                // 描画位置: RigidLink があれば接合点節点、無ければ杭頭節点を ΔZc 上（接合点標高）へ移動。
                Point3D drawLoc = targetNode.Coord;
                double dZc = pile.FoundationBeamDeltaZc;
                if (!usedRigidLink && dZc != 0)
                    drawLoc = new Point3D(targetNode.Coord.X, targetNode.Coord.Y, targetNode.Coord.Z + dZc);

                double vx, vy;
                if (isMoment)
                {
                    vx = f_global[3]; // Mxi_global
                    vy = f_global[4]; // Myi_global
                    // RigidLink が無い場合は剛オフセット移送で接合点モーメントへ補正する。
                    // M_C = M_H + (H − C) × F,  (H − C) = (0,0,−ΔZc)
                    //   → Mx_C = Mx_H + ΔZc·Fy,  My_C = My_H − ΔZc·Fx
                    if (!usedRigidLink)
                    {
                        vx += dZc * f_global[1]; // + ΔZc·Fy
                        vy -= dZc * f_global[0]; // − ΔZc·Fx
                    }
                }
                else
                {
                    // せん断力は剛オフセット間（荷重なし）で連続のため、接合点でも杭頭と同一。
                    vx = f_global[0]; // Fxi_global
                    vy = f_global[1]; // Fyi_global
                }

                double mag = Math.Sqrt(vx * vx + vy * vy);
                if (!double.IsFinite(mag)) continue;

                entries.Add((drawLoc, vx, vy, mag));
            }

            if (entries.Count == 0) { ColorBarCanvas.Children.Clear(); return; }

            // カラーバー
            var allValues = new ObservableCollection<double>(entries.Select(e => e.valueMag));
            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(allValues);
            double maxVal = entries.Max(e => e.valueMag);
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
            string unit = isMoment ? "kNm" : "kN";
            string title = content;

            foreach (var (location, vx, vy, mag) in entries)
            {
                if (mag < 1e-15) continue;

                Point center2D = viewModel.CanvasThreeDView.Transformation(location);
                if (!double.IsFinite(center2D.X)) continue;

                double arrowLen = maxVal > 1e-15 ? mag / maxVal * forceScale : 0;

                var picked = ColorBarUtils.PickColorGeometry(mag, colorBaredGeometries)
                             ?? ColorBarUtils.PickColorGeometryInclusiveTop(mag, colorBaredGeometries)
                             ?? (colorBaredGeometries.Count > 0 ? colorBaredGeometries.Last() : null);
                if (picked == null) continue;

                if (isMoment)
                {
                    var forceDir3D = new Vector3D(vx, vy, 0);
                    double forceDirLen = forceDir3D.Length;
                    var dirNorm3D = forceDirLen > 1e-15 ? forceDir3D / forceDirLen : new Vector3D(1, 0, 0);

                    var tail3D = location;
                    var head3D = new Point3D(
                        location.X + dirNorm3D.X * arrowLen,
                        location.Y + dirNorm3D.Y * arrowLen,
                        location.Z);

                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);
                    if (!double.IsFinite(head2D.X) || !double.IsFinite(tail2D.X)) continue;

                    picked.PathGeometry.AddGeometry(new LineGeometry(tail2D, head2D));
                    double headLen = viewModel.ArrowHeadLength * 0.8;
                    DrawDoubleArrowHead(picked.PathGeometry, head2D, tail2D, headLen);
                }
                else
                {
                    var forceDir3D = new Vector3D(vx, vy, 0);
                    double forceDirLen = forceDir3D.Length;
                    var dirNorm3D = forceDirLen > 1e-15 ? forceDir3D / forceDirLen : new Vector3D(1, 0, 0);

                    var head3D = location;
                    var tail3D = new Point3D(
                        head3D.X - dirNorm3D.X * arrowLen,
                        head3D.Y - dirNorm3D.Y * arrowLen,
                        head3D.Z - dirNorm3D.Z * arrowLen);

                    Point head2D = viewModel.CanvasThreeDView.Transformation(head3D);
                    Point tail2D = viewModel.CanvasThreeDView.Transformation(tail3D);
                    if (!double.IsFinite(head2D.X) || !double.IsFinite(tail2D.X)) continue;

                    picked.PathGeometry.AddGeometry(new LineGeometry(tail2D, head2D));

                    Vector dir2D = head2D - tail2D;
                    double dirLen2D = dir2D.Length;
                    Vector dirNorm2D = dirLen2D > 1e-9 ? dir2D / dirLen2D : new Vector(0, -1);
                    double headLen = viewModel.ArrowHeadLength;
                    Vector ortho = GetUnitOrthogonalVector(dirNorm2D);
                    Point side1 = head2D - dirNorm2D * headLen + ortho * (headLen * 0.4);
                    Point side2 = head2D - dirNorm2D * headLen - ortho * (headLen * 0.4);
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side1));
                    picked.PathGeometry.AddGeometry(new LineGeometry(head2D, side2));
                }

                if (viewModel.IsResultValueVisible)
                {
                    string fmt = "{0:N" + viewModel.DecimalPlaces + "}";
                    AddText3D(Brushes.Black, string.Format(fmt, mag), center2D.X, center2D.Y + 8, "C", "T", 0);
                }
            }

            foreach (var geo in colorBaredGeometries)
                geo.DrawPathes(Canvas3DLayout);

            if (allValues.Count > 0)
            {
                ColorBar.DrawStepColorBar(
                    ColorBarCanvas, colorBaredGeometries, title, unit,
                    allValues.Min(), allValues.Max(),
                    "{0:N" + viewModel.DecimalPlaces + "}", viewModel.LabelSize);
            }
        }

        /// <summary>
        /// 二重矢印ヘッドを描画（先端に2つの矢じりを並べる）
        /// tip: 矢印の先端, from: 矢印の根元方向
        /// </summary>
        private static void DrawDoubleArrowHead(PathGeometry pathGeo, Point tip, Point from, double headLen)
        {
            Vector dir = tip - from;
            double dirLen = dir.Length;
            if (dirLen < 1e-9) return;
            Vector dirNorm = dir / dirLen;
            Vector ortho = GetUnitOrthogonalVector(dirNorm);
            double w = headLen * 0.4;

            // 1つ目の矢じり（先端）
            pathGeo.AddGeometry(new LineGeometry(tip, tip - dirNorm * headLen + ortho * w));
            pathGeo.AddGeometry(new LineGeometry(tip, tip - dirNorm * headLen - ortho * w));

            // 2つ目の矢じり（少し内側）
            Point tip2 = tip - dirNorm * headLen * 0.7;
            pathGeo.AddGeometry(new LineGeometry(tip2, tip2 - dirNorm * headLen + ortho * w));
            pathGeo.AddGeometry(new LineGeometry(tip2, tip2 - dirNorm * headLen - ortho * w));
        }

        // ========================================
        // 応力図・変位図 ツールチップ機能
        // ========================================

        // ツールチップ用フィールド
        private System.Windows.Controls.Primitives.Popup? _beamResultTooltipPopup;
        private System.Windows.Controls.TextBlock? _beamResultTooltipText;

    }
}
