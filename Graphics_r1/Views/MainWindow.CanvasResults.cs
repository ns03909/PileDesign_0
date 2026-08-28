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
    public partial class MainWindow
    {
 
        private static IEnumerable<LoadCase> GetSelectedLoadCases(MainWindowViewModel vm)
            => vm.ResultInputModel.LoadCasesInput.AllSeismicLoadCases
               .Where(lc => vm.LoadCaseNameOption.Contains(lc.LoadName));

        private static IEnumerable<LoadCombination> GetSelectedLoadCombinations(MainWindowViewModel vm)
            => vm.ResultInputModel.LoadCasesInput.LoadCombinations
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
            HashSet<Beam> visibleBeams = null;
            HashSet<Node> visibleFemNodes = null;
            HashSet<HorizontalSoilSpring> visibleSoilSprings = null;
            bool hasInvisiblePile = false;
            if (viewModel.ResultInputModel?.PileLayoutItems != null)
            {
                // まず非表示杭があるかだけ確認（全杭可視ならセット構築を省略）
                foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                {
                    if (!pile.IsVisible) { hasInvisiblePile = true; break; }
                }

                if (hasInvisiblePile)
                {
                    visibleBeams = new HashSet<Beam>();
                    visibleFemNodes = new HashSet<Node>();
                    visibleSoilSprings = new HashSet<HorizontalSoilSpring>();
                    int visiblePileCount = 0;
                    foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        if (pile.IsVisible)
                        {
                            visiblePileCount++;
                            foreach (var beam in pile.Beams) visibleBeams.Add(beam);
                            foreach (var node in pile.PileNodes) visibleFemNodes.Add(node);
                            foreach (var spring in pile.HorizontalSoilSprings) visibleSoilSprings.Add(spring);
                            // 杭Zばね (P-S 非線形ばね) も pile 単位の可視性に従う
                            if (pile.VerticalNodeSprings != null)
                                foreach (var spring in pile.VerticalNodeSprings) visibleSoilSprings.Add(spring);
                        }
                    }

                    // 表示する杭はあるのに、その杭と FEM の対応付けが 1 件も無い場合
                    // (対応表を持たない旧ファイル、対応付けの復元に失敗した等)。
                    // このまま絞り込むと、表示中の杭のぶんまで含めて結果が丸ごと消え、
                    // 利用者には「一部だけ表示すると結果が出ない」としか見えない。
                    // 絞り込めないものは絞り込まず、代わりにログへ残す。
                    if (visiblePileCount > 0
                        && visibleBeams.Count == 0
                        && visibleFemNodes.Count == 0
                        && visibleSoilSprings.Count == 0)
                    {
                        Serilog.Log.Warning(
                            "[結果表示] 表示中の杭 {Count} 本と解析結果の対応付けが取れないため、"
                            + "杭ごとの絞り込みをやめて全体を描画します。", visiblePileCount);
                        visibleBeams = null;
                        visibleFemNodes = null;
                        visibleSoilSprings = null;
                        hasInvisiblePile = false;
                    }
                }
            }

            // 非アクティブ基礎梁のFEM梁名セットを構築（応力図非表示用）
            var invisibleFBNames = new HashSet<string>();
            if (viewModel.ResultInputModel?.FoundationBeamInput?.Beams != null)
            {
                var beams = viewModel.ResultInputModel.FoundationBeamInput.Beams;
                for (int i = 0; i < beams.Count; i++)
                {
                    if (!beams[i].IsVisible)
                        invisibleFBNames.Add($"FoundationBeam-{i + 1}");
                }
            }

            string effectiveContent = viewModel.EffectiveSettlementContent;

            if (effectiveContent != "沈下")
            {
                ColorBarCanvas.Children.Clear();
            }
            if (effectiveContent == "沈下")
            {
                string title = $"{viewModel.AnalysisResultContent}|{viewModel.AnalysisResultSettlementType}|{viewModel.SelectedLoadCaseName}";
                unit = "mm";
                ObservableCollection<Point3D> points = [];
                ObservableCollection<double> values = [];

                if (viewModel.AnalysisResultSettlementType == "単杭")
                {
                    var pgs = viewModel.ResultInputModel.PileGroupSettlement;
                    double loadingPlaneAlt = pgs.LoadingPlaneAltitude;
                    foreach (PileLayoutDataItem pileLocation in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            // 単杭沈下は m で格納されている → mm に変換
                            values.Add(pileLocation.SinglePileSettlementVL * 1000);
                            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel1s[i] * 1000); // m → mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel2[i];
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
                    // 群杭沈下は VL 荷重ケースでのみ意味を持つため、他のケースでは表示しない
                    if (viewModel.SelectedLoadCaseName == "VL")
                    {
                        var pgs = viewModel.ResultInputModel.PileGroupSettlement;
                        double loadingPlaneAlt = pgs.LoadingPlaneAltitude;
                        foreach (PileLayoutDataItem pileLocation in viewModel.ResultInputModel.PileLayoutItems)
                        {
                            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                            values.Add(pgs.SettlementOf(pileLocation.PileNo)); // mmのまま
                        }
                    }
                }
                else if (viewModel.AnalysisResultSettlementType == "単杭+群杭")
                {
                    var pgs = viewModel.ResultInputModel.PileGroupSettlement;
                    double loadingPlaneAlt = pgs.LoadingPlaneAltitude;
                    foreach (PileLayoutDataItem pileLocation in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        if (viewModel.SelectedLoadCaseName == "VL")
                        {
                            values.Add(pileLocation.SinglePileSettlementVL * 1000 + pgs.SettlementOf(pileLocation.PileNo)); // m→mm + mm
                            points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                        }
                        else
                        {
                            for (int i = 0; i < viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                            {
                                LoadCase loadCase = viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel1[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel1s[i] * 1000 + pgs.SettlementOf(pileLocation.PileNo)); // m→mm + mm
                                    points.Add(new Point3D(pileLocation.Point3D.X, pileLocation.Point3D.Y, loadingPlaneAlt));
                                }
                            }
                            for (int i = 0; i < viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                            {
                                LoadCase loadCase = viewModel.ResultInputModel.LoadCasesInput.LoadCasesLevel2[i];
                                if (viewModel.SelectedLoadCaseName == loadCase.LoadName)
                                {
                                    values.Add(pileLocation.SinglePileSettlementLevel2s[i] * 1000 + pgs.SettlementOf(pileLocation.PileNo)); // m→mm + mm
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
            if (viewModel.AnalysisResultContent == "梁応力（水平）")
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
                    viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                if (selectedLoadCase == null)
                {
                    return;
                }

                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
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

                // J 端の符号規約:
                //   FEM の CumulativeForce は「節点に作用する力」規約で、軸力でも I 端と J 端で符号反対。
                //   ユーザに見せる「要素内力」規約 (I/J 同符号) に変換するには J を反転する。
                //   これは Fx 軸力・Fy/Fz せん断・Mx/My/Mz モーメント全てに共通。
                int signJ = -1;
                // Fx (軸力) のみ: FEM 一般規約「引張正・圧縮負」で表示するため、後段で両端を符号反転する。
                bool isAxialN = viewModel.AnalysisResultBeamForceType == "Fx";

                // 案 Z モードでは FEM 解析自体に N0 を外力として適用するため、
                // Fx 表示は FEM が出した値そのまま (N0 加算は不要)。
                // (旧 Phase Y で使用していた NodalAxialForcesOp 加算ロジックは削除済み)

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

                    // 応力表示の有無 (チェック OFF でスキップ):
                    //   - 杭応力非表示: 基礎梁以外 (杭体・RigidLink 含む) を全部スキップ
                    //   - 基礎梁応力非表示: 基礎梁 (FoundationBeam-) を全部スキップ
                    bool isFoundationBeam = beam.Name.StartsWith("FoundationBeam-");
                    if (!viewModel.IsPileStressVisible && !isFoundationBeam) continue;
                    if (!viewModel.IsFoundationBeamStressVisible && isFoundationBeam) continue;

                    // 接合節点が非表示でもRigidLinkの応力図は描画する（スキップしない）

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

                    // Fx (軸力) のみ: FEM 一般規約「引張正・圧縮負」で表示するため両端を符号反転。
                    // この計算機内部の生 Fxi/Fxj は pile 向きの local X が +Z 方向で、圧縮時に正値を返すため、
                    // ユーザの「圧縮 = 負値」期待と逆になる。表示直前にここで反転して規約を揃える。
                    if (isAxialN)
                    {
                        originalForceI = -originalForceI;
                        originalForceJ = -originalForceJ;
                    }

                    double absForceI = Math.Abs(originalForceI);
                    double absForceJ = Math.Abs(originalForceJ);

                    // NaN/Infinity防止: 不正な値を持つビームはスキップ（maxAbsValue汚染を防止）
                    if (!double.IsFinite(originalForceI) || !double.IsFinite(originalForceJ))
                    {
                        Serilog.Log.Debug(
                            $"[CanvasResults] WARNING: NaN/Inf beam force skipped: {beam.Name} I={originalForceI} J={originalForceJ}");
                        continue;
                    }

                    // ここを絶対値追加から符号付き追加へ変更(カラーバー用ジオメトリ)
                    // 軸力 Fx は signJ=+1 (I/J 同符号)、その他は signJ=-1 (I/J 逆符号で同方向描画)
                    allValues.Add(originalForceI);
                    allValues.Add(signJ * originalForceJ);

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

                // 「杭MaxMin」モード用の事前計算: 各杭の (max, min) 値の出現位置 (beam, I/J) を特定する。
                // 表示時の値規約: I 端 = originalForceI, J 端 = signJ * originalForceJ
                // (Fx は signJ=+1、それ以外は signJ=-1)
                var pileMaxMinShow = new Dictionary<Beam, (bool showI, bool showJ)>();
                if (viewModel.IsPileMaxMinResultValueVisibleOnly)
                {
                    var beamForceMap = beamResults.ToDictionary(t => t.beam, t => (t.originalForceI, t.originalForceJ));
                    foreach (var pile in viewModel.ResultInputModel.PileLayoutItems)
                    {
                        var pileBeams = anaModel.GetPileBeams(pile);
                        if (pileBeams == null || pileBeams.Count == 0) continue;

                        Beam maxBeam = null; bool maxIsI = false; double maxVal = double.NegativeInfinity;
                        Beam minBeam = null; bool minIsI = false; double minVal = double.PositiveInfinity;
                        foreach (var b in pileBeams)
                        {
                            if (!beamForceMap.TryGetValue(b, out var force)) continue;
                            double valI = force.originalForceI;
                            double valJ = signJ * force.originalForceJ;
                            if (double.IsFinite(valI))
                            {
                                if (valI > maxVal) { maxVal = valI; maxBeam = b; maxIsI = true; }
                                if (valI < minVal) { minVal = valI; minBeam = b; minIsI = true; }
                            }
                            if (double.IsFinite(valJ))
                            {
                                if (valJ > maxVal) { maxVal = valJ; maxBeam = b; maxIsI = false; }
                                if (valJ < minVal) { minVal = valJ; minBeam = b; minIsI = false; }
                            }
                        }

                        void Mark(Beam b, bool isI)
                        {
                            if (b == null) return;
                            (bool showI, bool showJ) cur = pileMaxMinShow.TryGetValue(b, out var v) ? v : (false, false);
                            if (isI) cur.showI = true; else cur.showJ = true;
                            pileMaxMinShow[b] = cur;
                        }
                        Mark(maxBeam, maxIsI);
                        Mark(minBeam, minIsI);
                    }
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
                        // 個別成分: 力値の符号でダイアグラムの側を決定。J端は signJ で規約に応じて符号調整。
                        //   Fx (軸力): I/J 同符号 → signJ=+1 (反転なし)
                        //   Fy,Fz,Mx,My,Mz: I/J 逆符号 → signJ=-1 (反転)
                        nodeJForce3D = new(
                            nodeJ3D.X + signJ * forceJ * transformedForceDirectionJ[0],
                            nodeJ3D.Y + signJ * forceJ * transformedForceDirectionJ[1],
                            nodeJ3D.Z + signJ * forceJ * transformedForceDirectionJ[2]);
                    }

                    // 以下、既存の描画コード（投影→色分け→テキスト等）をそのまま使う
                    Point nodeI2D = viewModel.CanvasThreeDView.Transformation(nodeI3D);
                    Point nodeIForce2D = viewModel.CanvasThreeDView.Transformation(nodeIForce3D);
                    Point nodeJForce2D = viewModel.CanvasThreeDView.Transformation(nodeJForce3D);
                    Point nodeJ2D = viewModel.CanvasThreeDView.Transformation(nodeJ3D);

                    var points = new[] { nodeI2D, nodeIForce2D, nodeJForce2D, nodeJ2D };
                    List<double> values = [originalForceI, originalForceI, signJ * originalForceJ, signJ * originalForceJ];
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
                        if (viewModel.IsPileMaxMinResultValueVisibleOnly && !isFoundationBeam)
                        {
                            // 各杭で算出した (max, min) の発生位置のみ描画
                            if (pileMaxMinShow.TryGetValue(beam, out var show))
                            {
                                if (show.showI)
                                    AddText3D(Brushes.Black, string.Format(format, originalForceI),
                                        nodeIForce2D.X, nodeIForce2D.Y, "C", "C", 0.0);
                                if (show.showJ)
                                    AddText3D(Brushes.Black, string.Format(format, signJ * originalForceJ),
                                        nodeJForce2D.X, nodeJForce2D.Y, "C", "C", 0.0);
                            }
                        }
                        else if (viewModel.IsPileTopResultValueVisibleOnly && !isFoundationBeam)
                        {
                            if (beam.IsPileHeadElement)
                            {
                                AddText3D(Brushes.Black, string.Format(format, originalForceI),
                                nodeIForce2D.X, nodeIForce2D.Y, "C", "C", 0.0);
                            }
                        }
                        else
                        {
                            DrawResultValueTexts(
                            viewModel.IsResultValueVisible, Brushes.Black,
                            originalForceI, signJ * originalForceJ,
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

            if (viewModel.AnalysisResultContent == "節点変位（水平）")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;

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
                    case "U":
                        effectiveVector = Vector<double>.Build.DenseOfArray([1, 1, 1, 0, 0, 0]);
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

                // 単位設定・フォーマット
                unit = isThetaLocal ? "rad" : "mm";
                string format = isThetaLocal
                    ? "{0:F5}"
                    : "{0:N" + viewModel.DecimalPlaces + "}";

                // 選択ケース/組合せ（既存）
                var selectedLoadCase = LoadCases.GetLoadCase(
                    viewModel.ResultInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
                var selectedLoadCombination = LoadCombinations.GetLoadCombination(
                    viewModel.ResultInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
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

                    // 変位ダイアグラムのスケール: 共通スケール（地盤変位と揃える）優先、なければ最大変位で正規化
                    double maxRawDisp = multiplier > 0 ? maxAbsValue / multiplier : 0;
                    double dispScale = _sharedDispScaleMtoModel > 1e-15
                        ? _sharedDispScaleMtoModel
                        : (maxRawDisp > 1e-15
                            ? viewModel.DisplacementDiagramRatio * viewModel.ModelExtent / maxRawDisp
                            : 0);

                    // DummyBeams（根入れ部）描画 — 非アクティブ杭が存在する場合はスキップ。
                    // 杭変位非表示モードでは丸ごとスキップ (根入れ部は杭側の表現)。
                    if (!hasInvisiblePile && viewModel.IsPileDisplacementVisible
                        && viewModel.ResultInputModel.ElementDivision.DoatsuGoryokuBane != null && anaModel?.DummyBeams != null)
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

                    // Beams（杭要素 + 基礎梁）描画 — 非アクティブ杭/基礎梁のビームはスキップ
                    foreach (var beam in anaModel.Beams)
                    {
                        bool isFoundationBeam = beam.Name.StartsWith("FoundationBeam-");
                        if (isFoundationBeam)
                        {
                            if (invisibleFBNames.Contains(beam.Name)) continue;
                        }
                        else if (hasInvisiblePile && visibleBeams.Count > 0 && !visibleBeams.Contains(beam)) continue;

                        // 変位表示の有無 (チェック OFF でスキップ)
                        if (!viewModel.IsPileDisplacementVisible && !isFoundationBeam) continue;
                        if (!viewModel.IsFoundationBeamDisplacementVisible && isFoundationBeam) continue;

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
                            // isFoundationBeam は外側ループで宣言済み (再宣言不要)
                            if (viewModel.IsPileTopResultValueVisibleOnly && !isFoundationBeam)
                            {
                                if (beam.IsPileHeadElement)
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

                    // 最大回転角で正規化し、ModelExtentに対する相対サイズで楕円を描く
                    double maxRotValue = allValues.Count > 0 ? allValues.Max() : 0;
                    if (maxRotValue <= 1e-15) return; // 回転角がすべてゼロなら何も描かない

                    // 最大楕円径をModelExtentの一定割合とする（変位スケールスライダーで調整可能）
                    double maxEllipseDiameter = viewModel.ModelExtent * viewModel.CanvasThreeDView.Scale * 0.15 * viewModel.DisplacementDiagramRatio;

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
                        // 最大回転角で正規化し、最大楕円径に対する割合でサイズ決定
                        double targetPixelDiameter = (displayedMagnitude / maxRotValue) * maxEllipseDiameter;
                        if (targetPixelDiameter <= 0.5) continue;

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

                        // midValue はカラーバーに合わせたスケール（allValues と同じ単位: rot * multiplier）
                        double midValue = Math.Abs(displayedMagnitude);

                        //var picked = PickColorGeometry(midValue, colorBaredGeometries) ?? PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);
                        var picked = ColorBarUtils.PickColorGeometry(midValue, colorBaredGeometries)
                                    ?? ColorBarUtils.PickColorGeometryInclusiveTop(midValue, colorBaredGeometries);

                        picked?.PathGeometry.AddGeometry(geometryToAdd);

                        if (viewModel.IsResultValueVisible)
                        {
                            AddText3D(Brushes.Black, string.Format(format, rot * multiplier), center2DUnit.X, center2DUnit.Y - finalMajor, "C", "B", 0.0);
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
                            format,
                            viewModel.LabelSize
                        );
                    }
                    else
                    {
                        ColorBarCanvas.Children.Clear();
                    }
                }

            }
            else if (viewModel.AnalysisResultContent == "地盤反力（水平）")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;
                DrawHorizontalSoilSpringsResult3D(viewModel, anaModel, hasInvisiblePile ? visibleSoilSprings : null);
            }
            else if (viewModel.AnalysisResultContent == "地盤反力（分布）")
            {
                var anaModel = viewModel.CurrentModel;
                if (anaModel == null || anaModel.Beams == null)
                    return;
                DrawHorizontalSoilReactionDistribution3D(viewModel, anaModel, hasInvisiblePile ? visibleSoilSprings : null);
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
            else if (effectiveContent == "基礎梁考慮沈下梁応力")
            {
                DrawVerticalBeamResults(viewModel);
            }
            else if (effectiveContent is "基礎梁考慮沈下" or "基礎梁考慮+群杭沈下"
                                      or "基礎梁考慮反力（地盤）" or "基礎梁考慮反力（杭頭集約）")
            {
                PrepareVBSettlementPending(viewModel);
            }
            else if (effectiveContent is "単杭反力（地盤）" or "単杭反力（杭頭集約）")
            {
                PrepareSinglePileReactionPending(viewModel);
            }
            else if (effectiveContent is "単杭沈下部材角" or "群杭沈下部材角" or "単杭+群杭沈下部材角"
                     or "基礎梁考慮沈下部材角" or "基礎梁考慮+群杭沈下部材角")
            {
                DrawBeamMemberAngle(viewModel);
            }

            // 全モード共通: 変形後形状の描画
            bool alreadyDrawnDeformed = viewModel.AnalysisResultContent == "節点変位（水平）"
                && !viewModel.AnalysisResultNodeDisplacementType.StartsWith("θ");
            if (viewModel.IsDeformedElementVisible && !alreadyDrawnDeformed)
            {
                bool isVBContent = effectiveContent is "基礎梁考慮沈下梁応力"
                    or "基礎梁考慮沈下" or "基礎梁考慮+群杭沈下"
                    or "基礎梁考慮反力（地盤）" or "基礎梁考慮反力（杭頭集約）"
                    or "基礎梁考慮沈下部材角" or "基礎梁考慮+群杭沈下部材角";
                // 個別矩形（基礎梁考慮） の CaseRecord も基礎梁変形後形状を描画する
                if (!isVBContent && viewModel.IsGroupSettlementActiveCaseBeamAware
                    && effectiveContent is "沈下" or "群杭沈下" or "単杭+群杭沈下")
                {
                    isVBContent = true;
                }
                bool isSinglePileSettlement =
                    (effectiveContent == "沈下" && viewModel.AnalysisResultSettlementType == "単杭")
                    || effectiveContent is "単杭反力（地盤）" or "単杭反力（杭頭集約）"
                    || effectiveContent == "単杭沈下部材角";

                // 沈下量（"沈下"/"基礎梁考慮沈下"/"基礎梁考慮+群杭沈下"）のみ沈下量の大きさで色分け。
                // 沈下部材角・沈下反力・沈下応力は変形後形状を水平解析と同じ半透明グレーで描く。
                bool colorizeBySettlement = effectiveContent is "沈下" or "基礎梁考慮沈下" or "基礎梁考慮+群杭沈下";

                if (isVBContent)
                    DrawVBDeformedElements(viewModel, colorizeBySettlement);
                else if (isSinglePileSettlement)
                    DrawSinglePileDeformedElements(viewModel, colorizeBySettlement);
                else
                    UpdateDeformedElementsStandalone(viewModel, hasInvisiblePile, visibleBeams, invisibleFBNames);
            }
        }

    }
}
