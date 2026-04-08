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
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Node = PileDesign.FEM.Node;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : RibbonWindow
    {
 
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

            // 非アクティブ基礎梁のFEM梁名セットを構築（応力図非表示用）
            var invisibleFBNames = new HashSet<string>();
            if (viewModel.CurrentInputModel?.FoundationBeamInput?.Beams != null)
            {
                foreach (var fb in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                {
                    if (!fb.IsVisible)
                        invisibleFBNames.Add($"FoundationBeam-{fb.No}");
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
                        forceDirection = Vector<double>.Build.DenseOfArray([0, 0, -1]);
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
                // RigidLink-杭頭境界ライン用ジオメトリ
                var rigidLinkBoundaryGeo = new PathGeometry();

                ObservableCollection<double> allValues = [];

                int beamCount = 0;
                int validResultCount = 0;
                foreach (var beam in anaModel.Beams)
                {
                    // 非アクティブ杭のビームはスキップ（非アクティブ杭が存在する場合のみフィルタリング）
                    // 非アクティブ基礎梁もスキップ
                    if (beam.Name.StartsWith("FoundationBeam-"))
                    {
                        if (invisibleFBNames.Contains(beam.Name)) continue;
                    }
                    else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam))
                    {
                        continue;
                    }

                    // 接続用節点が非表示でもRigidLinkの応力図は描画する（スキップしない）

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

                    // NaN/Infinity防止: 不正な値を持つビームはスキップ（maxAbsValue汚染を防止）
                    if (!double.IsFinite(originalForceI) || !double.IsFinite(originalForceJ))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[CanvasResults] WARNING: NaN/Inf beam force skipped: {beam.Name} I={originalForceI} J={originalForceJ}");
                        continue;
                    }

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

                // 変換行列は各ビームの方向に応じて個別に計算する（per-beam）
                // （水平基礎梁の応力図を正しく描画するため）

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
                            // 曲げ合成: -Mz, My の比率を使う（Mxは無視）
                            return Vector<double>.Build.DenseOfArray([0.0, -mz, my]);
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

                    bool isFoundationBeam = beam.Name.StartsWith("FoundationBeam-");

                    // Mh/Fh選択時の基礎梁: 個別成分を描画
                    if (isDerivedMagnitude && isFoundationBeam)
                    {
                        if (derivedMagnitudeType == "Mh")
                            DrawFoundationBeamMyMz(viewModel, beam, beamResult, maxAbsValue,
                                colorBaredGeometries, rigidLinkBoundaryGeo);
                        else if (derivedMagnitudeType == "Fh")
                            DrawFoundationBeamFyFz(viewModel, beam, beamResult, maxAbsValue,
                                colorBaredGeometries, rigidLinkBoundaryGeo);
                        continue;
                    }

                    // ビームの方向ベクトルから変換行列を計算（各ビーム固有）
                    var beamDir = new Vector3D(
                        beam.NodeJ.Coord.X - beam.NodeI.Coord.X,
                        beam.NodeJ.Coord.Y - beam.NodeI.Coord.Y,
                        beam.NodeJ.Coord.Z - beam.NodeI.Coord.Z);
                    Matrix<double> t = Utils.GetNodeTransformMatrix(beamDir);

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

                    // 表示座標系に変換（ビーム固有の変換行列を使用）
                    var transformedForceDirectionI = t.Transpose() * dirI;
                    var transformedForceDirectionJ = t.Transpose() * dirJ;

                    // 元のスケーリング処理（maxAbsValue 等に応じたスケールは既存ロジックを使う）
                    double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;
                    double forceI = maxAbsValue == 0 ? 0 : originalForceI / maxAbsValue * forceScale;
                    double forceJ = maxAbsValue == 0 ? 0 : originalForceJ / maxAbsValue * forceScale;

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
                    List<double> values = [originalForceI, originalForceI, -originalForceJ, -originalForceJ];
                    AddColorPolyLineAreaGeometry(points, values, colorBaredGeometries);

                    // RigidLinkの杭頭側（J端）と接合点側（I端）に境界ラインを追加
                    if (beam.Name.StartsWith("RigidLink-"))
                    {
                        if (double.IsFinite(nodeJ2D.X) && double.IsFinite(nodeJForce2D.X))
                            rigidLinkBoundaryGeo.AddGeometry(new LineGeometry(nodeJ2D, nodeJForce2D));
                        if (double.IsFinite(nodeI2D.X) && double.IsFinite(nodeIForce2D.X))
                            rigidLinkBoundaryGeo.AddGeometry(new LineGeometry(nodeI2D, nodeIForce2D));
                    }

                    if (viewModel.IsResultValueVisible)
                    {
                        string format = "{0:N" + viewModel.DecimalPlaces + "}";
                        if (viewModel.IsPileTopResultValueVisibleOnly && !isFoundationBeam)
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
                            originalForceI, -originalForceJ,
                            nodeIForce2D, nodeJForce2D,
                            nodeJ2D, nodeI2D,
                            format, format, isFoundationBeam);
                        }
                    }
                }


                foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                {
                    colorBaredGeometry.DrawPathes(Canvas3DLayout);
                }

                // RigidLink境界ラインを描画（杭頭とRigidLinkの接点に白いラインを表示）
                if (rigidLinkBoundaryGeo.Figures.Count > 0)
                {
                    var boundaryPath = new System.Windows.Shapes.Path
                    {
                        Data = rigidLinkBoundaryGeo,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                    };
                    Canvas3DLayout.Children.Add(boundaryPath);
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
                            if (b.Name.StartsWith("FoundationBeam-"))
                            {
                                if (invisibleFBNames.Contains(b.Name)) continue;
                            }
                            else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(b)) continue;
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
                    if (!double.IsFinite(val)) continue; // NaN/Infinity防止
                    allValues.Add(Math.Abs(val) * multiplier);
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

                    // 変位ダイアグラムのスケール: 最大変位で正規化し、比率×ModelExtentを適用
                    double maxRawDisp = multiplier > 0 ? maxAbsValue / multiplier : 0;
                    double dispScale = maxRawDisp > 1e-15
                        ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxRawDisp
                        : 0;

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

                            // 変位量をモデル座標でスケール（最大変位で正規化、比率×ModelExtentを適用）
                            Point3D nI = dummyBeam.NodeI.Coord;
                            Point3D nJ = dummyBeam.NodeJ.Coord;
                            Point3D nIDisp3D = new(
                                nI.X + ndI.Ux * effectiveVector[0] * dispScale,
                                nI.Y + ndI.Uy * effectiveVector[1] * dispScale,
                                nI.Z + ndI.Uz * effectiveVector[2] * dispScale);
                            Point3D nJDisp3D = new(
                                nJ.X + ndJ.Ux * effectiveVector[0] * dispScale,
                                nJ.Y + ndJ.Uy * effectiveVector[1] * dispScale,
                                nJ.Z + ndJ.Uz * effectiveVector[2] * dispScale);

                            Point pI = viewModel.CanvasThreeDView.Transformation(nI);
                            Point pIDisp = viewModel.CanvasThreeDView.Transformation(nIDisp3D);
                            Point pJDisp = viewModel.CanvasThreeDView.Transformation(nJDisp3D);
                            Point pJ = viewModel.CanvasThreeDView.Transformation(nJ);

                            if (double.IsFinite(pI.X) && double.IsFinite(pI.Y)
                                && double.IsFinite(pIDisp.X) && double.IsFinite(pIDisp.Y)
                                && double.IsFinite(pJDisp.X) && double.IsFinite(pJDisp.Y)
                                && double.IsFinite(pJ.X) && double.IsFinite(pJ.Y))
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

                    // Beams（杭要素）描画 — 非アクティブ杭/基礎梁のビームはスキップ
                    foreach (var beam in anaModel.Beams)
                    {
                        if (beam.Name.StartsWith("FoundationBeam-"))
                        {
                            if (invisibleFBNames.Contains(beam.Name)) continue;
                        }
                        else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam)) continue;

                        // RigidLinkの変位図も描画する（スキップしない）

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
                            nodeI3D.X + ndI.Ux * effectiveVector[0] * dispScale,
                            nodeI3D.Y + ndI.Uy * effectiveVector[1] * dispScale,
                            nodeI3D.Z + ndI.Uz * effectiveVector[2] * dispScale);
                        Point3D nodeJDisp3D = new(
                            nodeJ3D.X + ndJ.Ux * effectiveVector[0] * dispScale,
                            nodeJ3D.Y + ndJ.Uy * effectiveVector[1] * dispScale,
                            nodeJ3D.Z + ndJ.Uz * effectiveVector[2] * dispScale);

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
                            bool isFoundationBeam = beam.Name.StartsWith("FoundationBeam-");
                            if (viewModel.IsPileTopResultValueVisibleOnly && !isFoundationBeam)
                            {
                                if (beam.IsPileTop)
                                {
                                    AddText3D(Brushes.Black, string.Format(format, origI * multiplier), nodeIDisp2D.X, nodeIDisp2D.Y, "C", "C", 0.0);
                                }
                            }
                            else
                            {
                                DrawResultValueTexts(viewModel.IsResultValueVisible, Brushes.Black, origI * multiplier, origJ * multiplier, nodeIDisp2D, nodeJDisp2D, nodeJ2D, nodeI2D, format, format, isFoundationBeam);
                            }
                        }
                    }

                    // 変形後形状の描画（3次Hermite補間）- U系のみ
                    if (viewModel.IsDeformedElementVisible)
                    {
                        DrawDeformedElements(viewModel, anaModel, selectedLoadCase, selectedLoadCombination, dispScale,
                            hasInvisiblePile, visibleBeams, invisibleFBNames);
                    }
                }
                else
                {
                    // θ 系：全節点に対して楕円を描く（ProjectionUtils を利用）
                    double flattening = viewModel.CanvasThreeDView.Flattening;

                    // カラーバー用 allValues は既に「rot * multiplier * ratio * modelExtent」で作成済み（上で）
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
                        double targetPixelDiameter = Math.Abs(displayedMagnitude) * viewModel.CanvasThreeDView.Scale * viewModel.DisplacementDiagramRatio * viewModel.ModelExtent;
                        if (targetPixelDiameter <= 0) continue;

                        var proj = ProjectionUtils.ProjectCircleAsEllipseExact(node.Coord, axis, 1.0, viewModel.CanvasThreeDView.Transformation);
                        if (proj == null) continue;
                        var (center2DUnit, majorUnitPx, minorUnitPx, angleDegUnit) = proj.Value;
                        if (majorUnitPx <= 1e-9) continue;
                        if (!double.IsFinite(center2DUnit.X) || !double.IsFinite(center2DUnit.Y)
                            || !double.IsFinite(majorUnitPx) || !double.IsFinite(minorUnitPx)) continue;

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
                        double midValue = Math.Abs(displayedMagnitude) * viewModel.DisplacementDiagramRatio * viewModel.ModelExtent;

                        //var picked = PickColorGeometry(midValue, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);
                        var picked = ColorBarUtils.PickColorGeometry(midValue, colorBaredGeometries)
                                    ?? ColorBarUtils.PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);

                        picked?.PathGeometry.AddGeometry(geometryToAdd);

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
            else if (viewModel.AnalysisResultContent == "地盤反力")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;
                DrawHorizontalSoilSpringsResult3D(viewModel, anaModel, hasInvisiblePile ? visibleSoilSprings : null);
            }
            else if (viewModel.AnalysisResultContent == "杭頭Mマップ" ||
                     viewModel.AnalysisResultContent == "杭頭Qマップ")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel?.Beams == null) return;
                DrawPileHeadForceMap(viewModel, anaModel);
            }
            else if (viewModel.AnalysisResultContent == "接合点Mマップ" ||
                     viewModel.AnalysisResultContent == "接合点Qマップ")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel?.Beams == null) return;
                DrawConnectionPointForceMap(viewModel, anaModel);
            }

            // 全モード共通: 変形後形状の描画
            // （節点変位U系では既に描画済みなので重複を避ける）
            bool alreadyDrawnDeformed = viewModel.AnalysisResultContent == "節点変位"
                && !viewModel.AnalysisResultNodeDisplacementType.StartsWith("θ");
            if (viewModel.IsDeformedElementVisible && !alreadyDrawnDeformed)
            {
                UpdateDeformedElementsStandalone(viewModel, hasInvisiblePile, visibleBeams, invisibleFBNames);
            }
        }

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
                hasInvisiblePile = false;

                if (viewModel.CurrentInputModel?.PileLayoutItems != null)
                {
                    foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (pile.IsVisible)
                            foreach (var beam in pile.Beams) visibleBeams.Add(beam);
                        else
                            hasInvisiblePile = true;
                    }
                }
                if (viewModel.CurrentInputModel?.FoundationBeamInput?.Beams != null)
                {
                    foreach (var fb in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    {
                        if (!fb.IsVisible)
                            invisibleFBNames.Add($"FoundationBeam-{fb.No}");
                    }
                }
            }

            var lc = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var lcomb = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
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
            double ds = maxDisp > 1e-15
                ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxDisp
                : 0;

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

            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 100, 100, 100)); // 半透明グレー
            var pen = new Pen(brush, 1.0);

            var pathGeo = new PathGeometry();

            // Beam要素（杭体 + FoundationBeam + RigidLink）
            if (anaModel.Beams != null)
            {
                foreach (var beam in anaModel.Beams)
                {
                    // 表示フィルタ
                    if (beam.Name.StartsWith("FoundationBeam-"))
                    {
                        if (invisibleFBNames.Contains(beam.Name)) continue;
                    }
                    else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam))
                        continue;

                    DrawDeformedBeam(viewModel, anaModel, beam, selectedLoadCase, selectedLoadCombination, dispScale, pathGeo);
                }
            }

            // DummyBeam（座標変換なし → 直線補間で変形後位置を描画）
            if (!hasInvisiblePile && anaModel.DummyBeams != null)
            {
                foreach (var db in anaModel.DummyBeams)
                {
                    DrawDeformedDummyBeam(viewModel, anaModel, db, selectedLoadCase, selectedLoadCombination, dispScale, pathGeo);
                }
            }

            // RotationalSpring（杭頭～接合節点のリンク要素）
            if (anaModel.RotationalSprings != null)
            {
                foreach (var rs in anaModel.RotationalSprings)
                {
                    DrawDeformedTwoNodeLink(viewModel, anaModel, rs.NodeI, rs.NodeJ, selectedLoadCase, selectedLoadCombination, dispScale, pathGeo);
                }
            }

            // RigidBody（剛体連結: Master→各Slave）
            if (anaModel.RigidBodies != null)
            {
                foreach (var rb in anaModel.RigidBodies)
                {
                    if (rb.MasterNode == null || rb.SlaveNodes == null) continue;
                    foreach (var slave in rb.SlaveNodes)
                    {
                        DrawDeformedTwoNodeLink(viewModel, anaModel, rb.MasterNode, slave, selectedLoadCase, selectedLoadCombination, dispScale, pathGeo);
                    }
                }
            }

            // PathGeometryをCanvasに描画
            if (!pathGeo.IsEmpty())
            {
                var path = new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 1.0,
                    Data = pathGeo
                };
                Canvas3DLayout.Children.Add(path);
            }

            // 変形後杭体形状の描画
            if (viewModel.IsPileSectionVisible && viewModel.CurrentInputModel?.PileLayoutItems != null)
            {
                DrawDeformedPileSections(viewModel, anaModel, selectedLoadCase, selectedLoadCombination,
                    dispScale, hasInvisiblePile, brush);
            }

            // 変形後基礎梁断面形状の描画
            if (viewModel.IsBeamElementSectionVisible && anaModel.Beams != null)
            {
                DrawDeformedBeamSections(viewModel, anaModel, selectedLoadCase, selectedLoadCombination,
                    dispScale, invisibleFBNames, brush);
            }
        }

        /// <summary>
        /// 変形後の基礎梁断面形状を描画する
        /// Hermite補間で変形後の中心線点列を生成し、各点で断面の4隅を計算して
        /// 曲がった梁の断面形状を描画する
        /// </summary>
        private void DrawDeformedBeamSections(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, HashSet<string> invisibleFBNames, Brush brush)
        {
            var fbInput = viewModel.CurrentInputModel?.FoundationBeamInput;
            if (fbInput?.Beams == null || fbInput.Sections == null) return;

            var fbElemDict = new Dictionary<string, FoundationBeamElement>();
            foreach (var fb in fbInput.Beams)
                fbElemDict[$"FoundationBeam-{fb.No}"] = fb;

            var secDict = fbInput.Sections.ToDictionary(s => s.No, s => s);

            var sectionPathGeo = new PathGeometry();
            var transform = viewModel.CanvasThreeDView;

            foreach (var beam in anaModel.Beams)
            {
                if (!beam.Name.StartsWith("FoundationBeam-")) continue;
                if (invisibleFBNames.Contains(beam.Name)) continue;
                if (beam.NodeI == null || beam.NodeJ == null) continue;

                if (!fbElemDict.TryGetValue(beam.Name, out var fbElem)) continue;
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

            if (!sectionPathGeo.IsEmpty())
            {
                var path = new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 0.7,
                    Data = sectionPathGeo
                };
                Canvas3DLayout.Children.Add(path);
            }
        }

        /// <summary>
        /// 変形後の杭体形状を描画する
        /// Hermite補間の変形後中心線に沿って、法線方向にオフセットした左右の輪郭線＋端部楕円を描画
        /// </summary>
        private void DrawDeformedPileSections(
            MainWindowViewModel viewModel, AnaModel anaModel,
            LoadCase selectedLoadCase, LoadCombination selectedLoadCombination,
            double dispScale, bool hasInvisiblePile, Brush brush)
        {
            var sectionPathGeo = new PathGeometry();
            double flattening = viewModel.CanvasThreeDView.Flattening;
            double scale = viewModel.CanvasThreeDView.Scale;

            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;
                if (pile.PileNodes == null || pile.PileNodes.Count < 2) continue;
                if (pile.Beams == null || pile.Beams.Count == 0) continue;

                // 要素分割後のセグメント情報を取得（SoilPile経由）
                ObservableCollection<PileBodySegment> soilPileSegments = null;
                if (pile.SoilPileAltNo > 0 &&
                    pile.SoilPileAltNo <= viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count)
                {
                    soilPileSegments = viewModel.CurrentInputModel.ElementDivision
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

                // 各Beam要素の変形後中心線を連結して輪郭線を生成
                for (int i = 0; i < pile.Beams.Count; i++)
                {
                    var beam = pile.Beams[i];
                    if (beam.NodeI == null || beam.NodeJ == null) continue;

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
                    }

                    // 要素J端の境界インデックスを記録
                    boundaryIndices.Add(centerPoints.Count - 1);
                }

                if (leftPoints.Count < 2) continue;

                // 左右の輪郭線をPathGeometryに追加
                for (int k = 0; k < leftPoints.Count - 1; k++)
                    sectionPathGeo.AddGeometry(new LineGeometry(leftPoints[k], leftPoints[k + 1]));
                for (int k = 0; k < rightPoints.Count - 1; k++)
                    sectionPathGeo.AddGeometry(new LineGeometry(rightPoints[k], rightPoints[k + 1]));

                // 全要素境界に楕円を描画
                foreach (int idx in boundaryIndices)
                {
                    AddDeformedEllipse(sectionPathGeo, centerPoints[idx], tangentVectors[idx], radiusList[idx], flattening);
                }
            }

            if (!sectionPathGeo.IsEmpty())
            {
                var path = new System.Windows.Shapes.Path
                {
                    Stroke = brush,
                    StrokeThickness = 0.7,
                    Data = sectionPathGeo
                };
                Canvas3DLayout.Children.Add(path);
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

            foreach (var (name, idxI, idxJ, dir) in components)
            {
                double origI = bf.GetByIndex(idxI);
                double origJ = bf.GetByIndex(idxJ);
                if (!double.IsFinite(origI) || !double.IsFinite(origJ)) continue;

                var transformedDir = t.Transpose() * dir;

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
                List<double> values = [origI, origI, -origJ, -origJ];
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

            foreach (var (name, idxI, idxJ, dir) in components)
            {
                double origI = bf.GetByIndex(idxI);
                double origJ = bf.GetByIndex(idxJ);
                if (!double.IsFinite(origI) || !double.IsFinite(origJ)) continue;

                var transformedDir = t.Transpose() * dir;

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
                List<double> values = [origI, origI, -origJ, -origJ];
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

        // 地盤反力描画ヘルパー（UpdateAnalysisResult3D 内から呼び出してください）
        private void DrawHorizontalSoilSpringsResult3D(MainWindowViewModel viewModel, AnaModel anaModel, HashSet<HorizontalSoilSpring> visibleSoilSprings = null)
        {
            if (viewModel == null || anaModel == null) return;
            if (anaModel.HorizontalSoilSprings == null || anaModel.HorizontalSoilSprings.Count == 0) return;
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            // 選択された荷重ケース・組み合わせを取得
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

            // 荷重ケースに対応する結果を検索するヘルパー
            HorizontalSpringResult FindSpringResult(HorizontalSoilSpring spring)
            {
                if (spring.HorizontalSpringResults == null || selectedLoadCase == null || selectedLoadCombination == null)
                    return null;
                return spring.HorizontalSpringResults.FirstOrDefault(r =>
                    r.LoadCase?.LoadName == selectedLoadCase.LoadName &&
                    r.LoadCombination?.Name == selectedLoadCombination.Name &&
                    r.IsLiquefaction == viewModel.IsLiquefaction);
            }

            // 選択されたタイプを取得
            string springType = viewModel.AnalysisResultSoilSpringType ?? "R";

            // 1) 全ばねの値を収集（カラーバー用） — 非アクティブ杭のばねはスキップ
            var allValues = new ObservableCollection<double>();
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (visibleSoilSprings != null && visibleSoilSprings.Count > 0 && !visibleSoilSprings.Contains(s)) continue;

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
                if (visibleSoilSprings != null && visibleSoilSprings.Count > 0 && !visibleSoilSprings.Contains(s)) continue;
                try
                {
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue;
                    double v = Math.Abs(GetSoilSpringValue(s, springType));
                    if (double.IsFinite(v) && v > maxAbsValue) maxAbsValue = v;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CanvasResults] ばね反力取得失敗: {ex.Message}"); }
            }
            double forceScale = viewModel.ForceDiagramRatio * viewModel.ModelExtent;

            // 3) 各ばねについて、反力の方向と大きさに基づいて矢印を描画
            foreach (var s in anaModel.HorizontalSoilSprings)
            {
                if (visibleSoilSprings != null && visibleSoilSprings.Count > 0 && !visibleSoilSprings.Contains(s)) continue;
                if (s?.NodeI == null || s.NodeJ == null) continue;

                try
                {
                    // 選択された荷重ケースの結果がない場合はスキップ
                    var result = FindSpringResult(s);
                    if (result?.CumulativeForce == null) continue;

                    // 選択されたタイプに応じた値を取得
                    double displayValue = GetSoilSpringValue(s, springType);
                    if (!double.IsFinite(displayValue)) continue; // NaN/Infinity防止

                    // 反力値から矢印方向と長さを決定
                    double fx = s.CumulativeForce.GetByIndex(0);
                    double fy = s.CumulativeForce.GetByIndex(1);
                    double fz = s.CumulativeForce.GetByIndex(2);

                    // 選択されたタイプに応じた反力方向ベクトル
                    var forceDir = springType switch
                    {
                        "RX" => new System.Windows.Media.Media3D.Vector3D(fx, 0, 0),
                        "RY" => new System.Windows.Media.Media3D.Vector3D(0, fy, 0),
                        "RZ" => new System.Windows.Media.Media3D.Vector3D(0, 0, fz),
                        "R" => new System.Windows.Media.Media3D.Vector3D(fx, fy, fz),
                        _ => new System.Windows.Media.Media3D.Vector3D(fx, fy, fz)
                    };

                    // 表示スケール: 最大反力値で正規化し、比率×ModelExtentを適用
                    double arrowLength3D = maxAbsValue > 1e-15
                        ? displayValue / maxAbsValue * forceScale
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

            // 3b) 杭先端反力の描画（R または RZ 選択時）
            if (springType == "R" || springType == "RZ")
            {
                foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    if (!pile.IsVisible) continue;
                    var tipNode = pile.PileNodes?.LastOrDefault();
                    if (tipNode?.CumulativeReaction == null) continue;

                    double tipFx = tipNode.CumulativeReaction.Fx;
                    double tipFy = tipNode.CumulativeReaction.Fy;
                    double tipFz = tipNode.CumulativeReaction.Fz;

                    // R選択時: 水平成分 sqrt(Fx²+Fy²) と鉛直成分 Fz を分けて描画
                    // RZ選択時: Fz のみ描画
                    var components = new System.Collections.Generic.List<(double value, System.Windows.Media.Media3D.Vector3D dir)>();

                    if (springType == "R")
                    {
                        double tipRH = Math.Sqrt(tipFx * tipFx + tipFy * tipFy);
                        if (double.IsFinite(tipRH) && tipRH > 1e-15)
                            components.Add((tipRH, new System.Windows.Media.Media3D.Vector3D(tipFx, tipFy, 0)));
                        if (double.IsFinite(tipFz) && Math.Abs(tipFz) > 1e-15)
                            components.Add((tipFz, new System.Windows.Media.Media3D.Vector3D(0, 0, tipFz)));
                    }
                    else // RZ
                    {
                        if (double.IsFinite(tipFz) && Math.Abs(tipFz) > 1e-15)
                            components.Add((tipFz, new System.Windows.Media.Media3D.Vector3D(0, 0, tipFz)));
                    }

                    foreach (var (tipValue, tipForceDir) in components)
                    {
                        allValues.Add(tipValue);

                        double tipForceDirLen = tipForceDir.Length;
                        var tipDirNorm = tipForceDirLen > 1e-15
                            ? tipForceDir / tipForceDirLen
                            : new System.Windows.Media.Media3D.Vector3D(0, 0, -1);

                        double tipArrowLen = maxAbsValue > 1e-15 ? tipValue / maxAbsValue * forceScale : 0;

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
                "R" => Math.Sqrt(fx * fx + fy * fy + fz * fz),  // 全方向反力
                "MX" => mx,
                "MY" => my,
                "MZ" => mz,
                "MH" => Math.Sqrt(mx * mx + my * my),  // 水平モーメント
                _ => Math.Sqrt(fx * fx + fy * fy + fz * fz)     // デフォルトはR
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
                "R" => "地盤反力R",
                "MX" => "地盤反力MX",
                "MY" => "地盤反力MY",
                "MZ" => "地盤反力MZ",
                "MH" => "地盤反力MH",
                _ => "地盤反力R"
            };
        }

        // ========================================
        // 杭頭M/Qマップ描画
        // ========================================

        /// <summary>
        /// 杭頭Mマップ / 杭頭Qマップ を描画
        /// 最も上の杭要素（IsPileTop）のi端応力を使用
        /// </summary>
        private void DrawPileHeadForceMap(MainWindowViewModel viewModel, AnaModel anaModel)
        {
            if (Canvas3DLayout == null || ColorBarCanvas == null) return;

            string content = viewModel.AnalysisResultContent;
            bool isMoment = content.Contains('M');

            // 選択された荷重ケース・組み合わせ
            var selectedLoadCase = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);

            var entries = new System.Collections.Generic.List<(Point3D location, double valueX, double valueY, double valueMag)>();

            // 杭頭Beam要素のリスト（IsPileTop）
            var pileTopBeams = anaModel.Beams?.Where(b => b.IsPileTop).ToList()
                               ?? new System.Collections.Generic.List<FEM.Beam>();

            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;

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
                viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (selectedLoadCase == null || selectedLoadCombination == null) return;

            var entries = new System.Collections.Generic.List<(Point3D location, double valueX, double valueY, double valueMag)>();

            // RigidLinkビームを検索
            var rigidLinkBeams = anaModel.Beams?.Where(b => b.Name.StartsWith("RigidLink-")).ToList()
                                 ?? new System.Collections.Generic.List<FEM.Beam>();

            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (!pile.IsVisible) continue;

                var rigidLink = rigidLinkBeams.FirstOrDefault(b =>
                    b.Name == $"RigidLink-{pile.No}" && b.NodeI != null);
                if (rigidLink == null) continue;

                var beamResult = rigidLink.GetBeamResult(anaModel, selectedLoadCase, selectedLoadCombination, viewModel.IsLiquefaction);
                var bf = beamResult?.CumulativeForce;
                if (bf == null) continue; // 結果がない場合はスキップ

                var node = rigidLink.NodeI; // ConnectionNode
                if (node == null) continue;

                // 要素座標系 → 全体座標系に変換
                var f_local = bf.GetVector();
                var T = rigidLink.GetCachedCoordTransform();
                var f_global = T.Transpose() * f_local;

                double vx, vy;
                if (isMoment)
                {
                    vx = f_global[3]; // Mxi_global
                    vy = f_global[4]; // Myi_global
                }
                else
                {
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
            if (bf == null) return $"深度: {depth:F2} m";

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
                return $"Mh: {interpMh:F1} kNm\nMy: {interpMy:F1} kNm\nMz: {interpMz:F1} kNm\n深度: {depth:F2} m";
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
                return $"Fh: {interpFh:F1} kN\nFy: {interpFy:F1} kN\nFz: {interpFz:F1} kN\n深度: {depth:F2} m";
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
