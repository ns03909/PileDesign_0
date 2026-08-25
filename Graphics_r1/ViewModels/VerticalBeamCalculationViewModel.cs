using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 基礎梁鉛直解析の制御ViewModel。
    /// Newton-Raphson反復で非線形鉛直杭ばね＋基礎梁の連成解析を行う。
    ///
    /// 解析フロー:
    ///   Phase 1: VL（常時+追加）をゼロから増分解析 → 収束状態を保存
    ///   Phase 2: 1-1～1-4, 2-1～2-4 を VL 収束状態から差分増分解析
    /// </summary>
    public partial class VerticalBeamCalculationViewModel : ObservableObject, Common.ICloseable
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // ── 保存/破棄 ──

        public event EventHandler RequestClose;

        /// <summary>解析結果を保存したかどうか（閉じた後にMainWindowViewModelが確認する）</summary>
        public bool IsSaved { get; private set; }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        private void SaveAndClose()
        {
            IsSaved = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void DiscardAndClose()
        {
            CaseResults.Clear();
            IsSaved = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // ── 解析パラメータ ──

        private int _loadStepsCount = 5;
        public int LoadStepsCount
        {
            get => _loadStepsCount;
            set => SetProperty(ref _loadStepsCount, Math.Max(1, value));
        }

        private double _convergenceTolerance = 1e-6;
        public double ConvergenceTolerance
        {
            get => _convergenceTolerance;
            set => SetProperty(ref _convergenceTolerance, Math.Max(1e-12, value));
        }

        private int _maxIterations = 100;
        public int MaxIterations
        {
            get => _maxIterations;
            set => SetProperty(ref _maxIterations, Math.Max(1, value));
        }

        private double _relaxationFactor = 0.8;
        public double RelaxationFactor
        {
            get => _relaxationFactor;
            set => SetProperty(ref _relaxationFactor, Math.Clamp(value, 0.1, 1.0));
        }

        // ── 荷重ケース選択 ──

        private bool _analyzeVL = true;
        public bool AnalyzeVL
        {
            get => _analyzeVL;
            set => SetProperty(ref _analyzeVL, value);
        }

        private bool _analyzeLevel1 = true;
        public bool AnalyzeLevel1
        {
            get => _analyzeLevel1;
            set => SetProperty(ref _analyzeLevel1, value);
        }

        private bool _analyzeLevel2 = true;
        public bool AnalyzeLevel2
        {
            get => _analyzeLevel2;
            set => SetProperty(ref _analyzeLevel2, value);
        }

        // ── 状態 ──

        private bool _isAnalysisRunning;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set => SetProperty(ref _isAnalysisRunning, value);
        }

        private bool _isAnalysisExecuted;
        public bool IsAnalysisExecuted
        {
            get => _isAnalysisExecuted;
            set
            {
                if (SetProperty(ref _isAnalysisExecuted, value))
                {
                    (OkCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private double _currentProgress;
        public double CurrentProgress
        {
            get => _currentProgress;
            set => SetProperty(ref _currentProgress, value);
        }

        private string _statusText = "準備完了";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ── 結果 ──

        public ObservableCollection<VerticalBeamCaseResult> CaseResults { get; } = [];

        private VerticalBeamCaseResult _selectedCaseResult;
        public VerticalBeamCaseResult SelectedCaseResult
        {
            get => _selectedCaseResult;
            set => SetProperty(ref _selectedCaseResult, value);
        }

        // ── ログ ──

        private ObservableCollection<string> _calculationLog = [];
        public ObservableCollection<string> CalculationLog
        {
            get => _calculationLog;
            set => SetProperty(ref _calculationLog, value);
        }

        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly System.Timers.Timer _logFlushTimer = new(200) { AutoReset = true };
        private volatile bool _logTimerStarted;
        private System.Timers.ElapsedEventHandler? _logFlushHandler;

        private CancellationTokenSource _cancellationTokenSource;

        // ── コンストラクタ ──

        public VerticalBeamCalculationViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
            OkCommand = new PileDesign.ViewModels.RelayCommand(_ => SaveAndClose(), _ => IsAnalysisExecuted);
            CancelCommand = new PileDesign.ViewModels.RelayCommand(_ => DiscardAndClose());
        }

        // ── ログ ──

        private void StartLogTimerIfNeeded()
        {
            if (_logTimerStarted) return;
            _logFlushHandler ??= (_, __) => FlushLogsToUi();
            _logFlushTimer.Elapsed += _logFlushHandler;
            _logFlushTimer.Start();
            _logTimerStarted = true;
        }

        private void FlushLogsToUi()
        {
            if (_logQueue.IsEmpty) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                while (_logQueue.TryDequeue(out var line))
                    CalculationLog.Add(line);
            });
        }

        private Task AddLogAsync(string message)
        {
            StartLogTimerIfNeeded();
            _logQueue.Enqueue(message);
            return Task.CompletedTask;
        }

        // ── コマンド ──

        [RelayCommand]
        private async Task ExecuteAnalysis()
        {
            // 入力データの整合性ゲート (杭体・地盤・寸法・配筋など)
            if (!PileDesign.Models.CheckInputData.ValidateForAnalysis(InputModel, "単杭沈下解析（基礎梁考慮）"))
                return;

            string error = ValidateInput();
            if (error != null)
            {
                MessageService.Show(error, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsAnalysisRunning = true;
            IsAnalysisExecuted = false;
            CaseResults.Clear();
            CalculationLog.Clear();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await Task.Run(async () =>
                {
                    await RunAsync(_cancellationTokenSource.Token);
                });
                IsAnalysisExecuted = true;
                _mainWindowViewModel.IsVerticalBeamAnalysisDone = true;
                _mainWindowViewModel.CaptureAnalysisResultSet();

                if (CaseResults.Count > 0)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        SelectedCaseResult = CaseResults[0];
                    });
                }
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("解析がキャンセルされました。");
            }
            catch (Exception ex)
            {
                await AddLogAsync($"解析中にエラーが発生しました: {ex.Message}");
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageService.ShowError($"解析エラー", ex, "エラー");
                });
            }
            finally
            {
                IsAnalysisRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                FlushLogsToUi();
                _logFlushTimer.Stop();
                if (_logFlushHandler != null)
                    _logFlushTimer.Elapsed -= _logFlushHandler;
                _logTimerStarted = false;
            }
        }

        [RelayCommand]
        private void CancelAnalysis()
        {
            _cancellationTokenSource?.Cancel();
        }

        // ── バリデーション ──

        private string ValidateInput()
        {
            if (InputModel.PileLayoutItems == null || InputModel.PileLayoutItems.Count == 0)
                return "杭配置データが定義されていません。";

            if (InputModel.FoundationBeamInput?.Beams == null || InputModel.FoundationBeamInput.Beams.Count == 0)
                return "基礎梁が定義されていません。";

            foreach (var pile in InputModel.PileLayoutItems)
            {
                var soilPile = pile.SoilPile;
                if (soilPile?.LoadDisplacements == null || soilPile.LoadDisplacements.Count == 0)
                    return "沈下解析が未実行の杭があります。先に沈下解析を実行してください。";
            }

            if (!AnalyzeVL && !AnalyzeLevel1 && !AnalyzeLevel2)
                return "少なくとも1つの荷重ケースを選択してください。";

            if (AnalyzeLevel1)
            {
                var level1Cases = InputModel.LoadCasesInput?.LoadCasesLevel1;
                if (level1Cases == null || level1Cases.Count == 0)
                    return "レベル1荷重ケースが定義されていません。";
            }

            if (AnalyzeLevel2)
            {
                var level2Cases = InputModel.LoadCasesInput?.LoadCasesLevel2;
                if (level2Cases == null || level2Cases.Count == 0)
                    return "レベル2荷重ケースが定義されていません。";
            }

            return null;
        }

        // ══════════════════════════════════════════════════════
        //  メイン解析ループ
        // ══════════════════════════════════════════════════════

        private async Task RunAsync(CancellationToken token)
        {
            await AddLogAsync("単杭沈下解析（基礎梁考慮）を開始します...");

            // モデル構築
            await AddLogAsync("FEMモデルを構築中...");
            var modelling = new VerticalBeamModelling(InputModel);

            await AddLogAsync($"  節点数: {modelling.Nodes.Count}");
            await AddLogAsync($"  梁要素数: {modelling.Beams.Count}");
            await AddLogAsync($"  杭ばね数: {modelling.VerticalPileSprings.Count}");
            await AddLogAsync($"  杭ばね曲線数: {modelling.SpringCurves.Count}");

            var anaModel = modelling.BuildAnaModel();
            await AddLogAsync($"  自由度数: {anaModel.CountFree}");

            // 解析対象ケース数を計算（進捗計算用）
            int totalCases = 0;
            if (AnalyzeVL) totalCases++;
            if (AnalyzeLevel1) totalCases += InputModel.LoadCasesInput.LoadCasesLevel1.Count;
            if (AnalyzeLevel2) totalCases += InputModel.LoadCasesInput.LoadCasesLevel2.Count;
            int caseIndex = 0;

            // ─── Phase 1: VL ベースケース ───
            // VLは常に最初に解析（地震時ケースの初期状態として使用）
            bool needVLBase = AnalyzeLevel1 || AnalyzeLevel2;
            AnalysisStateSnapshot vlSnapshot = null;

            if (AnalyzeVL || needVLBase)
            {
                caseIndex++;
                string caseName = "VL (常時+追加)";

                await AddLogAsync($"\n━━━ Phase 1: {caseName} ({caseIndex}/{totalCases}) ━━━");

                // 状態初期化
                anaModel.InitializeStates();

                // VL荷重設定
                double totalLoad = SetIncrementalLoads(modelling, anaModel, p => p.AxialForceVL);
                anaModel.MapOnVectorDF();
                await AddLogAsync($"  合計荷重: {totalLoad:F1} kN");

                // 杭ばね初期剛性設定
                SetInitialSpringStiffness(modelling);

                // 荷重ステップ解析
                var caseResult = await RunLoadSteps(
                    modelling, anaModel, caseName, caseIndex, totalCases, token);

                // 結果収集
                CollectResults(modelling, anaModel, caseResult, p => p.AxialForceVL);
                await LogPileResultSummary(caseResult, totalLoad);

                if (AnalyzeVL)
                {
                    Application.Current?.Dispatcher.Invoke(() => CaseResults.Add(caseResult));
                }

                // VL状態スナップショット保存（地震時ケース用）
                if (needVLBase)
                {
                    vlSnapshot = SaveState(anaModel);
                    await AddLogAsync("  VL収束状態を保存しました。");
                }
            }

            // ─── Phase 2: 地震時ケース（VL状態から差分増分） ───

            if (AnalyzeLevel1 && vlSnapshot != null)
            {
                var level1Cases = InputModel.LoadCasesInput.LoadCasesLevel1;
                for (int i = 0; i < level1Cases.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    caseIndex++;

                    string caseName = $"1-{i + 1}: {level1Cases[i].LoadName}";
                    await AddLogAsync($"\n━━━ Phase 2: {caseName} ({caseIndex}/{totalCases}) ━━━");

                    // VL状態復元
                    RestoreState(anaModel, vlSnapshot);
                    await AddLogAsync("  VL収束状態から開始");

                    // 差分荷重設定: delta = AxialForceLevel1s[i] - AxialForceVL
                    int idx = i;
                    double totalDelta = SetIncrementalLoads(modelling, anaModel,
                        p => (p.AxialForceLevel1s != null && idx < p.AxialForceLevel1s.Count
                            ? p.AxialForceLevel1s[idx] : p.AxialForceVL) - p.AxialForceVL);
                    anaModel.MapOnVectorDF();
                    await AddLogAsync($"  差分荷重合計: {totalDelta:F1} kN");

                    // 杭ばね剛性をVL状態の値に更新
                    UpdateSpringTangentStiffness(modelling, anaModel);
                    UpdateSpringSecantStiffness(modelling, anaModel);

                    // 荷重ステップ解析
                    var caseResult = await RunLoadSteps(
                        modelling, anaModel, caseName, caseIndex, totalCases, token);

                    // 結果収集（最終状態 = VL + delta）
                    CollectResults(modelling, anaModel, caseResult,
                        p => p.AxialForceLevel1s != null && idx < p.AxialForceLevel1s.Count
                            ? p.AxialForceLevel1s[idx] : p.AxialForceVL);

                    double totalLoad = caseResult.PileResults.Sum(r => r.InputLoad_kN);
                    await LogPileResultSummary(caseResult, totalLoad);

                    Application.Current?.Dispatcher.Invoke(() => CaseResults.Add(caseResult));
                }
            }

            if (AnalyzeLevel2 && vlSnapshot != null)
            {
                var level2Cases = InputModel.LoadCasesInput.LoadCasesLevel2;
                for (int i = 0; i < level2Cases.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    caseIndex++;

                    string caseName = $"2-{i + 1}: {level2Cases[i].LoadName}";
                    await AddLogAsync($"\n━━━ Phase 2: {caseName} ({caseIndex}/{totalCases}) ━━━");

                    // VL状態復元
                    RestoreState(anaModel, vlSnapshot);
                    await AddLogAsync("  VL収束状態から開始");

                    // 差分荷重設定: delta = AxialForceLevel2s[i] - AxialForceVL
                    int idx = i;
                    double totalDelta = SetIncrementalLoads(modelling, anaModel,
                        p => (p.AxialForceLevel2s != null && idx < p.AxialForceLevel2s.Count
                            ? p.AxialForceLevel2s[idx] : p.AxialForceVL) - p.AxialForceVL);
                    anaModel.MapOnVectorDF();
                    await AddLogAsync($"  差分荷重合計: {totalDelta:F1} kN");

                    // 杭ばね剛性をVL状態の値に更新
                    UpdateSpringTangentStiffness(modelling, anaModel);
                    UpdateSpringSecantStiffness(modelling, anaModel);

                    // 荷重ステップ解析
                    var caseResult = await RunLoadSteps(
                        modelling, anaModel, caseName, caseIndex, totalCases, token);

                    // 結果収集
                    CollectResults(modelling, anaModel, caseResult,
                        p => p.AxialForceLevel2s != null && idx < p.AxialForceLevel2s.Count
                            ? p.AxialForceLevel2s[idx] : p.AxialForceVL);

                    double totalLoad = caseResult.PileResults.Sum(r => r.InputLoad_kN);
                    await LogPileResultSummary(caseResult, totalLoad);

                    Application.Current?.Dispatcher.Invoke(() => CaseResults.Add(caseResult));
                }
            }

            CurrentProgress = 100;
            StatusText = "解析完了";
            await AddLogAsync("\n単杭沈下解析（基礎梁考慮）が完了しました。");
        }

        // ══════════════════════════════════════════════════════
        //  荷重設定
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 各杭の接合節点に増分荷重を設定する。
        /// getLoad で各杭の荷重を取得し、LoadStepsCount で分割した増分を設定。
        /// 戻り値は合計荷重。
        /// </summary>
        private double SetIncrementalLoads(
            VerticalBeamModelling modelling, AnaModel anaModel,
            Func<PileLayoutDataItem, double> getLoad)
        {
            double totalLoad = 0;
            foreach (var pile in InputModel.PileLayoutItems)
            {
                if (!modelling.ConnectionNodes.TryGetValue(pile.No, out var connNode))
                    continue;

                double load = getLoad(pile);
                double incrementalLoad = load / LoadStepsCount;
                totalLoad += load;

                connNode.SetIncrementalLoad(new NodeLoad(0, 0, -incrementalLoad, 0, 0, 0));
            }
            return totalLoad;
        }

        /// <summary>
        /// 杭ばね初期剛性を設定する。
        /// </summary>
        private void SetInitialSpringStiffness(VerticalBeamModelling modelling)
        {
            foreach (var pile in InputModel.PileLayoutItems)
            {
                if (!modelling.PileSpringMap.TryGetValue(pile.No, out var spring)) continue;
                if (!modelling.SpringCurves.TryGetValue(pile.No, out var curve)) continue;

                double kz0 = curve.InitialTangentStiffness;
                spring.SetKe(0, 0, kz0, 0, 0, 0, true);
                spring.SetKe(0, 0, kz0, 0, 0, 0, false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  荷重ステップ + Newton-Raphson ループ
        // ══════════════════════════════════════════════════════

        private async Task<VerticalBeamCaseResult> RunLoadSteps(
            VerticalBeamModelling modelling, AnaModel anaModel,
            string caseName, int caseIndex, int totalCases,
            CancellationToken token)
        {
            var caseResult = new VerticalBeamCaseResult { LoadCaseName = caseName };
            bool caseConverged = true;

            for (int step = 0; step < LoadStepsCount; step++)
            {
                token.ThrowIfCancellationRequested();

                CurrentProgress = (caseIndex - 1.0 + (step + 1.0) / LoadStepsCount) / totalCases * 100;
                StatusText = $"{caseName} ステップ {step + 1}/{LoadStepsCount}";

                await AddLogAsync($"\n  ── ステップ {step + 1}/{LoadStepsCount} ──");

                // 累積荷重を更新
                foreach (var node in anaModel.Nodes)
                {
                    if (node.IsLoaded)
                        node.UpdateCumulativeLoad();
                }
                anaModel.UpdateVectorF();
                anaModel.MapOnVectorF();

                // 残差初期化
                anaModel.InitializeNormsqR_onNormsqFint();
                anaModel.SetR();

                int iteration = 0;
                double residual = anaModel.NormsROnNormsFint;

                // Newton-Raphson 反復
                while (residual >= ConvergenceTolerance && iteration < MaxIterations)
                {
                    iteration++;

                    await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();

                        UpdateSpringTangentStiffness(modelling, anaModel);
                        anaModel.MapOnKtanMat();
                        Solver.SolveDisp(anaModel, RelaxationFactor);
                        UpdateSpringSecantStiffness(modelling, anaModel);

                        foreach (var beam in anaModel.Beams)
                            beam.SetBeamDispAndForce();
                        foreach (var spring in anaModel.HorizontalSoilSprings)
                            spring.SetBeamDispAndForce();

                        anaModel.MapOnKsecMat();
                        anaModel.SetT();
                        anaModel.FindR();
                    }, token);

                    residual = anaModel.NormsROnNormsFint;

                    string convSymbol = residual < ConvergenceTolerance ? "≦" : "＞";
                    await AddLogAsync($"    iter {iteration}: ||R||²/||F||² = {residual:E2} {convSymbol} {ConvergenceTolerance:E2}");
                }

                bool stepConverged = residual < ConvergenceTolerance;
                if (!stepConverged)
                {
                    await AddLogAsync($"    ⚠ ステップ {step + 1}: 最大反復回数 {MaxIterations} で収束せず (残差={residual:E3})");
                    if (residual > ConvergenceTolerance * 1000)
                        caseConverged = false;
                }
                else
                {
                    await AddLogAsync($"    → {iteration} 回で収束");
                }

                caseResult.StepResults.Add(new VerticalBeamStepResult(
                    caseName, step + 1, iteration, residual, stepConverged));
            }

            caseResult.IsConverged = caseConverged;
            return caseResult;
        }

        // ══════════════════════════════════════════════════════
        //  杭ばね剛性更新
        // ══════════════════════════════════════════════════════

        private static void UpdateSpringTangentStiffness(VerticalBeamModelling modelling, AnaModel anaModel)
        {
            foreach (var kvp in modelling.PileSpringMap)
            {
                int pileNo = kvp.Key;
                var spring = kvp.Value;

                if (!modelling.SpringCurves.TryGetValue(pileNo, out var curve)) continue;
                if (!modelling.ConnectionNodes.TryGetValue(pileNo, out var connNode)) continue;

                double settlement_m = -connNode.CumulativeDisp.Uz;
                double kzTan = curve.GetTangentStiffness(settlement_m);
                spring.SetKe(0, 0, kzTan, 0, 0, 0, true);
            }
        }

        private static void UpdateSpringSecantStiffness(VerticalBeamModelling modelling, AnaModel anaModel)
        {
            foreach (var kvp in modelling.PileSpringMap)
            {
                int pileNo = kvp.Key;
                var spring = kvp.Value;

                if (!modelling.SpringCurves.TryGetValue(pileNo, out var curve)) continue;
                if (!modelling.ConnectionNodes.TryGetValue(pileNo, out var connNode)) continue;

                double settlement_m = -connNode.CumulativeDisp.Uz;
                double kzSec = curve.GetSecantStiffness(settlement_m);
                spring.SetKe(0, 0, kzSec, 0, 0, 0, false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  結果収集
        // ══════════════════════════════════════════════════════

        private void CollectResults(VerticalBeamModelling modelling, AnaModel anaModel,
            VerticalBeamCaseResult caseResult, Func<PileLayoutDataItem, double> getInputLoad)
        {
            foreach (var pile in InputModel.PileLayoutItems)
            {
                if (!modelling.ConnectionNodes.TryGetValue(pile.No, out var connNode)) continue;

                double settlement_m = -connNode.CumulativeDisp.Uz;
                double settlement_mm = settlement_m * 1000;

                double reaction = 0;
                if (modelling.SpringCurves.TryGetValue(pile.No, out var curve))
                    reaction = curve.GetForce(settlement_m);

                double inputLoad = getInputLoad(pile);

                caseResult.PileResults.Add(new VerticalBeamPileResult(
                    pile.No, pile.X, pile.Y, inputLoad, reaction, settlement_mm));
            }

            foreach (var node in anaModel.Nodes)
            {
                if (node.Name.StartsWith("GroundNode-")) continue;

                caseResult.NodeResults.Add(new VerticalBeamNodeResult(
                    node.Name, node.Coord.X, node.Coord.Y, node.Coord.Z,
                    node.CumulativeDisp.Uz * 1000,
                    node.CumulativeDisp.Rx,
                    node.CumulativeDisp.Ry));
            }

            foreach (var beam in anaModel.Beams)
            {
                caseResult.BeamResults.Add(new VerticalBeamBeamResult(
                    beam.Name, beam.CumulativeForce));
            }
        }

        private async Task LogPileResultSummary(VerticalBeamCaseResult caseResult, double totalLoad)
        {
            await AddLogAsync($"\n  ── 結果サマリ ({caseResult.LoadCaseName}) ──");
            double totalReaction = 0;
            foreach (var pr in caseResult.PileResults)
            {
                await AddLogAsync($"    杭{pr.PileNo}: 反力={pr.Reaction_kN:F1}kN, 沈下={pr.Settlement_mm:F2}mm");
                totalReaction += pr.Reaction_kN;
            }
            await AddLogAsync($"    反力合計: {totalReaction:F1} kN (入力合計: {totalLoad:F1} kN)");
        }

        // ══════════════════════════════════════════════════════
        //  解析状態スナップショット (Save / Restore)
        // ══════════════════════════════════════════════════════

        private class AnalysisStateSnapshot
        {
            public Dictionary<string, NodeSnapshot> NodeStates { get; set; }
            public Dictionary<string, (BeamDisp CumDisp, BeamForce CumForce)> BeamStates { get; set; }
            public Dictionary<string, SpringSnapshot> SpringStates { get; set; }
            public Vector<double> VectorF { get; set; }
            public Vector<double> VectorD { get; set; }
        }

        private class NodeSnapshot
        {
            public NodeDisp CumulativeDisp { get; set; }
            public NodeLoad CumulativedLoad { get; set; }
            public bool IsLoaded { get; set; }
        }

        private class SpringSnapshot
        {
            public BeamDisp CumulativeDisp { get; set; }
            public BeamForce CumulativeForce { get; set; }
            public Matrix<double> KeTan { get; set; }
            public Matrix<double> KeSec { get; set; }
        }

        private static AnalysisStateSnapshot SaveState(AnaModel anaModel)
        {
            var snapshot = new AnalysisStateSnapshot
            {
                NodeStates = [],
                BeamStates = [],
                SpringStates = [],
                VectorF = anaModel.VectorF.Clone(),
                VectorD = anaModel.VectorD.Clone()
            };

            foreach (var node in anaModel.Nodes)
            {
                snapshot.NodeStates[node.Name] = new NodeSnapshot
                {
                    CumulativeDisp = node.CumulativeDisp.Clone(),
                    CumulativedLoad = node.CumulativedLoad.Clone(),
                    IsLoaded = node.IsLoaded
                };
            }

            foreach (var beam in anaModel.Beams)
            {
                snapshot.BeamStates[beam.Name] = (
                    beam.CumulativeDisp.Clone(),
                    beam.CumulativeForce.Clone()
                );
            }

            foreach (var spring in anaModel.HorizontalSoilSprings)
            {
                snapshot.SpringStates[spring.Name] = new SpringSnapshot
                {
                    CumulativeDisp = spring.CumulativeDisp.Clone(),
                    CumulativeForce = spring.CumulativeForce.Clone(),
                    KeTan = spring.KeTan?.Clone(),
                    KeSec = spring.KeSec?.Clone()
                };
            }

            return snapshot;
        }

        private static void RestoreState(AnaModel anaModel, AnalysisStateSnapshot snapshot)
        {
            // AnaModel ベクトル復元
            for (int i = 0; i < anaModel.VectorF.Count; i++)
                anaModel.VectorF[i] = snapshot.VectorF[i];
            for (int i = 0; i < anaModel.VectorD.Count; i++)
                anaModel.VectorD[i] = snapshot.VectorD[i];

            // VectorDF, VectorDD, VectorR をゼロクリア
            anaModel.VectorDF.Clear();
            anaModel.VectorDD?.Clear();
            anaModel.VectorR?.Clear();

            // 節点状態復元
            foreach (var node in anaModel.Nodes)
            {
                if (snapshot.NodeStates.TryGetValue(node.Name, out var ns))
                {
                    node.CumulativeDisp = ns.CumulativeDisp.Clone();
                    node.CumulativedLoad = ns.CumulativedLoad.Clone();
                    node.IsLoaded = ns.IsLoaded;
                    node.IncrementalDisp = new NodeDisp(0, 0, 0, 0, 0, 0);
                    node.IncrementalLoad = new NodeLoad(0, 0, 0, 0, 0, 0);
                }
            }

            // 梁状態復元
            foreach (var beam in anaModel.Beams)
            {
                if (snapshot.BeamStates.TryGetValue(beam.Name, out var bs))
                {
                    beam.CumulativeDisp = bs.CumDisp.Clone();
                    beam.CumulativeForce = bs.CumForce.Clone();
                    beam.IncrementalDisp = new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    beam.IncrementalForce = new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                }
            }

            // ばね状態復元
            foreach (var spring in anaModel.HorizontalSoilSprings)
            {
                if (snapshot.SpringStates.TryGetValue(spring.Name, out var ss))
                {
                    spring.CumulativeDisp = ss.CumulativeDisp.Clone();
                    spring.CumulativeForce = ss.CumulativeForce.Clone();
                    if (ss.KeTan != null) spring.SetKe(
                        ss.KeTan[0, 0], ss.KeTan[1, 1], ss.KeTan[2, 2],
                        ss.KeTan[3, 3], ss.KeTan[4, 4], ss.KeTan[5, 5], true);
                    if (ss.KeSec != null) spring.SetKe(
                        ss.KeSec[0, 0], ss.KeSec[1, 1], ss.KeSec[2, 2],
                        ss.KeSec[3, 3], ss.KeSec[4, 4], ss.KeSec[5, 5], false);
                }
            }
        }
    }
}
