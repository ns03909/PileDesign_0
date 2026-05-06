using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;
using PileDesign.Services;
namespace PileDesign.ViewModels
{
    public partial class HorizontalCalculationViewModel : ObservableObject
    {
        static HorizontalCalculationViewModel()
        {
            try
            {
                // 利用可能なら最適プロバイダを自動選択（安全な通常ルート）
                Control.UseBestProviders();
            }
            catch (NotSupportedException nse)
            {
                Log.Warning(nse, "[HorizontalCalcVM init] BestProviders 不可 () → UseManaged フォールバック");
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                    System.Diagnostics.Debug.WriteLine($"[HorizontalCalcVM init] UseManaged も失敗: {inner.GetType().Name}: {inner.Message}");
                }
            }
            catch (Exception ex)
            {
                // 想定外の例外も捕捉して管理実装にフォールバック
                System.Diagnostics.Debug.WriteLine($"[HorizontalCalcVM init] 想定外 ({ex.GetType().Name}: {ex.Message}) → UseManaged フォールバック");
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                    System.Diagnostics.Debug.WriteLine($"[HorizontalCalcVM init] UseManaged も失敗: {inner.GetType().Name}: {inner.Message}");
                }
            }

            // スレッドプール並列度はプロセッサ数に合わせる
            Control.MaxDegreeOfParallelism = Environment.ProcessorCount;
        }
        // ログのバッファリング用（UIポストを間引く）
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly System.Timers.Timer _logFlushTimer = new(200) { AutoReset = true };
        private volatile bool _logTimerStarted;
        private System.Timers.ElapsedEventHandler? _logFlushHandler;

        // ログテキストのキャッシュ: string.Join を毎回実行すると O(N²) の GC 圧迫で
        // UI フリーズ原因となる。StringBuilder にインクリメンタル追記してキャッシュ保持。
        private readonly StringBuilder _logTextBuilder = new();
        private volatile string _cachedLogText = string.Empty;

        // v29 (2026-04-27): 解析終了時にステップ単位の収束状況サマリーを表示するため、
        // 各ステップの結果を蓄積。ConcurrentBag でケース並列実行下でも安全に append。
        private enum StepStatus { Converged, Unconverged, PhysicallyUnconverged }
        private sealed record StepSummary(
            string CaseTag, int Level, int LoadCaseNo, int ComboNo, bool IsLiquefaction,
            int Step, int NStep, int BisectionAttempt, int Iterations,
            double FinalResidual, double EffectiveAlpha, double MaxDisp,
            StepStatus Status, double ElapsedSec,
            // 2026-05-06: NR モード追跡。
            //   KRebuildCount = ステップ内で K 行列を組み直した反復数 (= Full NR 反復数)
            //   KReuseCount   = K を再利用した反復数 (= Modified NR 反復数)
            //   合計は Iterations に一致 (Iterations = KRebuildCount + KReuseCount)。
            int KRebuildCount = 0, int KReuseCount = 0);
        private readonly System.Collections.Concurrent.ConcurrentBag<StepSummary> _stepSummaries = new();

        private void StartLogTimerIfNeeded()
        {
            if (_logTimerStarted) return;
            _logFlushHandler = (_, __) => FlushLogsToUi();
            _logFlushTimer.Elapsed += _logFlushHandler;
            _logFlushTimer.Start();
            _logTimerStarted = true;
        }

        private void FlushLogsToUi()
        {
            if (_logQueue.IsEmpty) return;
            // 先にワーカースレッドで queue をドレイン → UI dispatch 内の作業を最小化
            var newLines = new List<string>(64);
            while (_logQueue.TryDequeue(out var line))
                newLines.Add(line);
            if (newLines.Count == 0) return;

            // E3c-3 hang 対策 (2026-04-26): Dispatcher.Invoke (sync) → BeginInvoke (async)。
            // System.Timers.Timer の Elapsed は ThreadPool worker で発火するため、ここで
            // sync Dispatcher.Invoke すると UI thread を待つ間 ThreadPool worker を 1 本ブロック。
            // 8 並列時はワーカー枯渇 → ケース worker と starvation 競合。BeginInvoke なら
            // キューに積むだけで即時 return、ThreadPool 圧迫を回避。
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                foreach (var line in newLines)
                {
                    CalculationLog.Add(line);
                    if (_logTextBuilder.Length > 0) _logTextBuilder.Append(Environment.NewLine);
                    _logTextBuilder.Append(line);
                }
                _cachedLogText = _logTextBuilder.ToString();
                OnPropertyChanged(nameof(CalculationLogText));
            });
        }

        public ObservableCollection<AnaModel> AnaModels { get; } = [];
        public AnaModel CurrentModel => AnaModels.Count > 0 ? AnaModels[0] : null;

        //public static InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // DataGrid上の選択中のGroundLayerデータ
        private GroundLayerInput _selectedGroundLayerOnDataGrid;
        public GroundLayerInput SelectedGroundLayerOnDataGrid
        {
            get => _selectedGroundLayerOnDataGrid;
            set => SetProperty(ref _selectedGroundLayerOnDataGrid, value);
        }

        // GroundLayer
        private ObservableCollection<GroundLayerInput> _selectedGroundLayerCollection = [];
        public ObservableCollection<GroundLayerInput> SelectedGroundLayerCollection
        {
            get => _selectedGroundLayerCollection;
            set => SetProperty(ref _selectedGroundLayerCollection, value);
        }

        // 計算ステップレベル1荷重
        private int _level1CalculationStepsCount = 2;
        public int Level1CalculationStepsCount
        {
            get => _level1CalculationStepsCount;
            set
            {
                SetProperty(ref _level1CalculationStepsCount, value);
                OnPropertyChanged(nameof(TotalCalculationCount));
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // 計算ステップレベル2荷重（v7: 収束性改善のため16に増加）
        private int _level2CalculationStepsCount = 8;  // v15: デフォルトを16→8に変更
        public int Level2CalculationStepsCount
        {
            get => _level2CalculationStepsCount;
            set
            {
                SetProperty(ref _level2CalculationStepsCount, value);
                OnPropertyChanged(nameof(TotalCalculationCount));
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // Newton-Raphson緩和係数（0.0-1.0: 1.0=フル更新、小さいほど安定だが収束遅い）
        // Full NR: 1.0推奨、Modified NR: 0.5推奨
        // v29: Modified NR デフォルト化に合わせ ω=0.5 をデフォルトに変更（ラインサーチで調整）
        private double _relaxationFactor = 0.5;
        public double RelaxationFactor
        {
            get => _relaxationFactor;
            set
            {
                SetProperty(ref _relaxationFactor, Math.Clamp(value, 0.1, 1.0));
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // Newton-Raphsonモード選択
        // - Full NR (OFF): 毎反復で接線剛性+Kマトリクス更新（収束が速いが計算コスト高）
        // - Modified NR (ON): 適応的 - 最初の数回はFull NR、その後はKマトリクス再利用（高速化）
        // v29 (2026-04-27): Cholesky 因子再利用と組み合わせると Modified NR 後期反復で
        //   Solve コストもほぼゼロになるため、デフォルトを Full→Modified NR (ON) に変更。
        private bool _useModifiedNewtonRaphson = true;
        public bool UseModifiedNewtonRaphson
        {
            get => _useModifiedNewtonRaphson;
            set
            {
                if (SetProperty(ref _useModifiedNewtonRaphson, value))
                {
                    // NRモード切り替え時に適切な緩和係数を自動設定
                    // Full NR (OFF): ω=1.0, Modified NR (ON): ω=0.5
                    RelaxationFactor = value ? 0.5 : 1.0;
                }
            }
        }

        // Modified NR モード時の Full NR 初期反復数
        // Modified NR では最初の N 回を Full NR（接線剛性更新）で実行し、以降は K を再利用
        private int _fullNRIterations = 5;
        public int FullNRIterations
        {
            get => _fullNRIterations;
            set
            {
                SetProperty(ref _fullNRIterations, Math.Clamp(value, 1, 99));
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // 反復なし簡易法（1ステップ=1回解析、最も安定だがステップ数を増やす必要あり）
        private bool _skipIteration = false;
        public bool SkipIteration
        {
            get => _skipIteration;
            set => SetProperty(ref _skipIteration, value);
        }

        // 杭軸力モード: InputModelに委譲（グラフ・テーブルからも参照可能にするため）
        public bool UseAnalysisAxialForce
        {
            get => InputModel?.UseAnalysisAxialForce ?? false;
            set
            {
                if (InputModel != null && InputModel.UseAnalysisAxialForce != value)
                {
                    InputModel.UseAnalysisAxialForce = value;
                    OnPropertyChanged();
                }
            }
        }

        // 収束安定化手法のenum型（排他的選択）
        public enum ConvergenceMethod
        {
            FixedRelaxation,    // 固定緩和係数
            AdaptiveRelaxation, // 適応的緩和
            LineSearch          // ラインサーチ
        }

        private ConvergenceMethod _selectedConvergenceMethod = ConvergenceMethod.LineSearch;  // v15: デフォルトをラインサーチに変更
        public ConvergenceMethod SelectedConvergenceMethod
        {
            get => _selectedConvergenceMethod;
            set
            {
                if (SetProperty(ref _selectedConvergenceMethod, value))
                {
                    // 既存のboolプロパティも更新（互換性維持）
                    _useAdaptiveRelaxation = (value == ConvergenceMethod.AdaptiveRelaxation);
                    _useLineSearch = (value == ConvergenceMethod.LineSearch);
                    OnPropertyChanged(nameof(UseAdaptiveRelaxation));
                    OnPropertyChanged(nameof(UseLineSearch));
                }
            }
        }

        // 適応的緩和係数（残差の変化に応じてωを自動調整）
        // 既存コードとの互換性のため維持
        private bool _useAdaptiveRelaxation = false;
        public bool UseAdaptiveRelaxation
        {
            get => _useAdaptiveRelaxation;
            set
            {
                if (SetProperty(ref _useAdaptiveRelaxation, value) && value)
                    SelectedConvergenceMethod = ConvergenceMethod.AdaptiveRelaxation;
            }
        }

        // ラインサーチ（線探索）- Newton方向に沿って最適なステップ長を自動探索
        // 既存コードとの互換性のため維持
        private bool _useLineSearch = true;  // v15: デフォルトをtrueに変更

        // Phase 3 (2026-05-06) step-level cut-back retry 機構の有効化フラグ。
        // false: 従来挙動 — ステップ未収束時は attempt 全体を破棄して nStep×2 で再試行。
        // true: end-of-step k チェックポイントから巻き戻して失敗ステップだけ細分化 (1/M)。
        // 各 attempt で許容する cut-back 回数は MAX_CUTBACKS_PER_ATTEMPT で制限。
        // 2026-05-06: 実機検証で counter-loading 系が極端に遅くなる問題が判明し一時的に false に戻す。
        private const bool _useStepLevelCutback = false;
        private const int MAX_CUTBACKS_PER_ATTEMPT = 3;
        private const int CUTBACK_DIVISOR = 2;  // 失敗ステップを 1/M に分割 (M=2 で半分)
        public bool UseLineSearch
        {
            get => _useLineSearch;
            set
            {
                if (SetProperty(ref _useLineSearch, value) && value)
                    SelectedConvergenceMethod = ConvergenceMethod.LineSearch;
            }
        }

        // v21 Phase 3 prep: ケース並列度の設定（現状は 1 固定）
        //
        // 将来 2 以上を指定可能にするための予約プロパティ。以下の Phase 3.1 実装が揃うまで、
        // setter 側でハード的に 1 に丸める（誤設定による即時クラッシュを防ぐ安全装置）。
        //
        // Phase 3.1 で必要な作業（別 session 想定・2–4 日規模）:
        //   1. InputModel 部分グラフ（PileLayoutItems / ElementDivision.{DoatsuGoryokuBane,SoilPiles}）の
        //      per-worker 深クローン基盤。PileLayoutItem.AxialForce / SoilNodes / DoatsuGoryokuBane.Items
        //      のノード参照を、DeepCopy 済み AnaModel のノードに再バインドする fixup が必要。
        //   2. 以下の private メソッドを AnaModel 受け取り形に徹底リファクタ（現状は this.InputModel を直読）:
        //      UpdateSoilDisp / UpdateF / UpdateAxialForceFromAnalysis / PrepareKmat /
        //      SetupMPhiFromPileSectionForLoadCase / SetupNonlinearMThetaForLoadCase /
        //      SetupMPhiByCurrentAxialForMiddleBeam / ApplyPileHeadRigidBindingForLoadCase /
        //      InitializeSoilDisplacementIncrement / SetVectorDF。
        //   3. MathNet Control.MaxDegreeOfParallelism を case-parallel 実行時のみ 1 に絞る
        //      （内部 solver の over-subscription 防止。現在 Environment.ProcessorCount のまま）。
        //   4. ログ / 進捗 / _bisectionExtraSteps のスレッドセーフ化（インターロック or lock）。
        //   5. Parallel.ForEachAsync で (LoadCase, LoadCombination, isLiquefaction) トリプルを
        //      並列処理し、完了後に (LoadCase.No, LoadCombination.No, isLiq, step) キーで
        //      AnalysisStepResults / NodeResults / BeamResults / Spring*Results を決定的にマージ。
        // E3c-3 (2026-04-23): ケース並列度。1 = 逐次 (従来と同一挙動)、2 以上で並列実行。
        // 2026-04-26: hang 解消 + 既定値を 16 に引き上げ済み。
        //   - Dispatcher.Invoke → BeginInvoke 化で ThreadPool starvation 解消
        //   - MKL_NUM_THREADS=1 / OMP_NUM_THREADS=1 で MKL ネイティブ過剰スレッド抑制
        //   - 実機 8 並列で 16 ケース 6 秒完走を確認 (6.3× 高速化、80% 並列効率)
        // 上限は Environment.ProcessorCount (論理プロセッサ数) で clamp する。それ以上に
        // しても Task は待機するだけで意味がなく、メモリ圧迫で hang リスクが増える。
        public int ProcessorCount => Environment.ProcessorCount;

        // 既定値 16 (2026-04-26): MDOP=8 で 16 ケース 6 秒完走を実機検証済 (hang 解消)。
        //   既定値 2 → 16 に引き上げ。Math.Clamp で論理プロセッサ数に自動制限されるため、
        //   8 コア機なら 8 に、16 コア機なら 16 に、それぞれ安全な上限に収まる。
        //   hang 対策の主因は Dispatcher.BeginInvoke 化 (2376fbe) と MKL_NUM_THREADS=1。
        private int _maxCaseDegreeOfParallelism = 16;
        public int MaxCaseDegreeOfParallelism
        {
            get => _maxCaseDegreeOfParallelism;
            set
            {
                SetProperty(ref _maxCaseDegreeOfParallelism,
                    Math.Clamp(value, 1, Environment.ProcessorCount));
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // 選択地盤番号
        private int _selectedGroundNo = 1;
        public int SelectedGroundNo
        {
            get => _selectedGroundNo;
            set => SetProperty(ref _selectedGroundNo, value);
        }

        // 節点数
        [ObservableProperty]
        private int nodesCount;

        // 要素数
        [ObservableProperty]
        private int beamsCount;

        // 接続モード（剛体連結 / 剛床連結）
        public FoundationBeamConnectionMode ConnectionMode
        {
            get => InputModel.FoundationBeamInput?.ConnectionMode ?? FoundationBeamConnectionMode.RigidBody;
            set
            {
                if (InputModel.FoundationBeamInput != null && InputModel.FoundationBeamInput.ConnectionMode != value)
                {
                    InputModel.FoundationBeamInput.ConnectionMode = value;
                    OnPropertyChanged();
                }
            }
        }

        // 基礎梁要素が存在し、使用する材料・断面が定義済みか（剛床連結の選択可否）
        public bool HasFoundationBeamElements
        {
            get
            {
                var fbInput = InputModel.FoundationBeamInput;
                if (fbInput?.Beams == null || fbInput.Beams.Count == 0) return false;

                // 全梁要素の材料No・断面Noに対応する定義が存在するかチェック
                var materialNos = fbInput.Materials?.Select(m => m.No).ToHashSet() ?? new HashSet<int>();
                var sectionNos = fbInput.Sections?.Select(s => s.No).ToHashSet() ?? new HashSet<int>();

                foreach (var beam in fbInput.Beams)
                {
                    if (!materialNos.Contains(beam.MaterialNo) || !sectionNos.Contains(beam.SectionNo))
                        return false;
                }

                return true;
            }
        }

        public string FoundationBeamValidationMessage
        {
            get
            {
                var fbInput = InputModel.FoundationBeamInput;
                if (fbInput?.Beams == null || fbInput.Beams.Count == 0)
                    return "基礎梁要素が定義されていません。";

                var materialNos = fbInput.Materials?.Select(m => m.No).ToHashSet() ?? new HashSet<int>();
                var sectionNos = fbInput.Sections?.Select(s => s.No).ToHashSet() ?? new HashSet<int>();

                var missingMats = fbInput.Beams.Where(b => !materialNos.Contains(b.MaterialNo)).Select(b => b.MaterialNo).Distinct().ToList();
                var missingSecs = fbInput.Beams.Where(b => !sectionNos.Contains(b.SectionNo)).Select(b => b.SectionNo).Distinct().ToList();

                var msgs = new List<string>();
                if (missingMats.Count > 0)
                    msgs.Add($"材料No {string.Join(",", missingMats)} が未定義です。");
                if (missingSecs.Count > 0)
                    msgs.Add($"断面No {string.Join(",", missingSecs)} が未定義です。");

                return msgs.Count > 0 ? string.Join("\n", msgs) : "";
            }
        }

        public AnalysisModelling AnalysisModelling { get; set; }

        // 液状化の考慮
        public enum LiquefactionOptionType
        {
            None,
            Yes,
            Both
        }

        private LiquefactionOptionType _liquefactionOption = LiquefactionOptionType.Yes;
        public LiquefactionOptionType LiquefactionOption
        {
            get => _liquefactionOption;
            set
            {
                SetProperty(ref _liquefactionOption, value);
                OnPropertyChanged(nameof(TotalCalculationCount));
                OnPropertyChanged(nameof(TotalLoadCaseCount));
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        // ステータスメッセージ
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // 履歴
        private ObservableCollection<string> _calculationLog = [];
        public ObservableCollection<string> CalculationLog
        {
            get => _calculationLog;
            set => SetProperty(ref _calculationLog, value);
        }

        /// <summary>ログ全体をテキストとして返す（TextBoxバインド用）。
        /// FlushLogsToUi がキャッシュを更新するので getter は O(1)。</summary>
        public string CalculationLogText => _cachedLogText;

        // 並列モニタ (案 B, 2026-04-24): 現在実行中のケースを表示するウィンドウ用。
        // RunThisCaseAsync の開始/終了で UI スレッド上から Add/Remove される。
        public ObservableCollection<CaseMonitorItem> ActiveCases { get; } = [];

        private int _activeCasesCount;
        /// <summary>ActiveCases.Count の明示プロパティ。ObservableCollection.Count の
        /// binding が一部環境で更新されないため、Add/Remove と同時にこちらも更新する。</summary>
        public int ActiveCasesCount
        {
            get => _activeCasesCount;
            set => SetProperty(ref _activeCasesCount, value);
        }

        private int _completedCaseCount;
        public int CompletedCaseCount
        {
            get => _completedCaseCount;
            set { if (SetProperty(ref _completedCaseCount, value)) OnPropertyChanged(nameof(PendingCaseCount)); }
        }

        private int _totalPlannedCaseCount;
        public int TotalPlannedCaseCount
        {
            get => _totalPlannedCaseCount;
            set { if (SetProperty(ref _totalPlannedCaseCount, value)) OnPropertyChanged(nameof(PendingCaseCount)); }
        }

        public int PendingCaseCount => Math.Max(0, TotalPlannedCaseCount - CompletedCaseCount - ActiveCasesCount);

        // 解析実行済みフラグ
        private bool _isAnalysisExecuted = false;
        public bool IsAnalysisExecuted
        {
            get => _isAnalysisExecuted;
            set => SetProperty(ref _isAnalysisExecuted, value);
        }

        // モデル作成中フラグ（ウィンドウ表示後の非同期初期化用）
        private bool _isModelCreating = false;
        public bool IsModelCreating
        {
            get => _isModelCreating;
            set => SetProperty(ref _isModelCreating, value);
        }

        // v19: 自動ステップ二分による追加ステップ数（再試行時に加算されるステップの累計）
        // 解析実行中のみ増加。解析開始時にリセット。
        private int _bisectionExtraSteps = 0;

        /// <summary>
        /// `_bisectionExtraSteps` 変更時に並列モニタとメインプログレスバー両方に
        /// 反映するため、関連プロパティ全部の PropertyChanged を一括で発火。
        /// (旧実装は TotalCalculationCount と ProgressText だけ発火しており、
        /// 並列モニタが直接バインドしている EffectiveProgressTotal が更新されなかった)
        /// </summary>
        private void NotifyProgressPropertiesChanged()
        {
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(EffectiveProgressTotal));
            OnPropertyChanged(nameof(EffectiveProgress));
            OnPropertyChanged(nameof(ProgressText));
        }

        // E3c-3 (2026-04-23): ケース並列化用。
        // DeepCopy 直後の主モデル結果件数 snapshot と、case body 完了後の
        // AppendCaseResultsToMain 呼出は共に targetModel の ObservableCollection
        // を参照/変更するため、複数ワーカー間でレースする。両フェーズを
        // _caseMergeLock で atomic 化する。逐次モード (MaxCaseDegreeOfParallelism=1) では
        // 競合がないため lock は no-op として機能。
        private readonly object _caseMergeLock = new();

        // 解析ケース数（基本値 + 再試行追加） — step 単位の総数 (1 ケース × N step)
        public int TotalCalculationCount
        {
            get
            {
                // 液状化オプション: あり=1, なし=1, 両方=2
                int liquefactionFactor = LiquefactionOption == LiquefactionOptionType.Both ? 2 : 1;

                // 解析対象の荷重ケース1, 2の数（荷重ゼロのケースを除外）
                int level1Count = InputModel.LoadCasesInput.LoadCasesLevel1?.Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;
                int level2Count = InputModel.LoadCasesInput.LoadCasesLevel2?.Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;

                // 適用されている荷重組み合わせの数
                int combinationCount = InputModel.LoadCasesInput.AllLoadCombinations?.Count(x => x.IsApplicable) ?? 0;

                // 1荷重あたりレベル1解析計算ステップ数
                int level1Steps = Level1CalculationStepsCount;

                // 1荷重あたりレベル2解析計算ステップ数
                int level2Steps = Level2CalculationStepsCount;

                // 計算式（基本 + 再試行分）
                int baseTotal = liquefactionFactor * (level1Count * level1Steps + level2Count * level2Steps) * combinationCount;
                return baseTotal + _bisectionExtraSteps;
            }
        }

        /// <summary>
        /// 並列実行される load case 単位の総数 (= step 数を含まない)。
        /// プログレスバーと並列モニタの「完了 / 総数」表示に使用 (CompletedCaseCount と同じ単位)。
        /// </summary>
        public int TotalLoadCaseCount
        {
            get
            {
                int liquefactionFactor = LiquefactionOption == LiquefactionOptionType.Both ? 2 : 1;
                int level1Count = InputModel.LoadCasesInput.LoadCasesLevel1?.Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;
                int level2Count = InputModel.LoadCasesInput.LoadCasesLevel2?.Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;
                int combinationCount = InputModel.LoadCasesInput.AllLoadCombinations?.Count(x => x.IsApplicable) ?? 0;
                return liquefactionFactor * (level1Count + level2Count) * combinationCount;
            }
        }

        private bool _isAnalysisRunning;
        public bool IsAnalysisRunning
        {
            get => _isAnalysisRunning;
            set
            {
                SetProperty(ref _isAnalysisRunning, value);
                PauseAnalysisCommand.NotifyCanExecuteChanged();
                ResumeAnalysisCommand.NotifyCanExecuteChanged();
                CancelAnalysisCommand.NotifyCanExecuteChanged();
                ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
                ExecuteAdditiveAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        // 代表変位閾値で解析を強制終了する機能
        // Unit: 同プロジェクトの他ロジックに合わせて m 単位想定（必要ならUIで説明を）
        private bool _stopONMaxDisplacement = true;
        public bool StopONMaxDisplacement
        {
            get => _stopONMaxDisplacement;
            set => SetProperty(ref _stopONMaxDisplacement, value);
        }

        private double _maxAllowedDisplacement = 1.0; // デフォルト: 1.0 (m)
        public double MaxAllowedDisplacement
        {
            get => _maxAllowedDisplacement;
            set => SetProperty(ref _maxAllowedDisplacement, value);
        }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        // 追加: View側へ「プログレスクリアアニメーション開始」を要求するためのイベント
        public event Action? RequestClearProgressAnimation;

        // 追加: Viewに警告表示を依頼するイベント（MessageBoxを直接呼ばない）
        public event Action<string>? RequestShowWarning;

        // 並列モニタ (案 B, 2026-04-24): View に 並列モニタウィンドウの開閉を依頼。
        // MDOP>=2 で解析開始時に Show、解析終了/キャンセル時に Hide。
        public event Action? RequestShowParallelMonitor;
        public event Action? RequestHideParallelMonitor;

        private CancellationTokenSource _cancellationTokenSource;
        private readonly ManualResetEventSlim _pauseEvent = new(true); // trueで初期状態は「進行」

        public IRelayCommand PauseAnalysisCommand { get; }
        public IRelayCommand ResumeAnalysisCommand { get; }
        public IRelayCommand CancelAnalysisCommand { get; }

        /// <summary>
        /// 「追加実行 (段階追加再解析)」コマンド。既存結果を保持し、選択中ケースのうち
        /// 未計算分のみを実行する。前回設定 (LastRunConfig) と互換性チェック有り。
        /// </summary>
        public IAsyncRelayCommand ExecuteAdditiveAnalysisCommand { get; }

        /// <summary>
        /// 完了済みケースキー (LoadName|CombName|IsLiquefaction) の集合。
        /// UI DataGrid 「済」列バインディング用。
        /// </summary>
        public ObservableCollection<string> CompletedCaseKeys { get; } = new();

        private int _currentProgress;
        public int CurrentProgress
        {
            get => _currentProgress;
            set
            {
                SetProperty(ref _currentProgress, value);
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(ElapsedTimeText));
                OnPropertyChanged(nameof(EstimatedRemainingText));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(EffectiveProgress));
            }
        }

        /// <summary>
        /// 追加実行モード時のステップ基準値。RunAsync 開始時に「既存結果のステップ数」を入れ、
        /// プログレスバーと進捗テキストから差し引くことで「未計算分のみの進捗」を表示する。
        /// 通常実行 (additive=false) では 0。
        /// </summary>
        private int _additiveBaselineSteps = 0;

        /// <summary>
        /// プログレスバー Value バインド用。追加実行時は baseline 分を引いて 0 始まりとする。
        /// </summary>
        public int EffectiveProgress => Math.Max(0, CurrentProgress - _additiveBaselineSteps);

        /// <summary>
        /// プログレスバー Maximum バインド用。追加実行時は既存ステップ数を除外した「未計算分」を返す。
        /// 通常実行時は TotalCalculationCount と同じ。
        /// </summary>
        public int EffectiveProgressTotal => Math.Max(0, TotalCalculationCount - _additiveBaselineSteps);

        public string ProgressText
        {
            get
            {
                // 単位「ステップ評価」= 各荷重ケースのステップ × 再試行回数 の総和。
                // 並列モニタの「ステップ X/Y (再試行含む)」と同じ意味で、再試行で総数が増える点を明示。
                // 詳細は ProgressTextTooltip 参照 (ステータスバー TextBlock の ToolTip にバインド)。
                string baseText = $"ステップ評価 {EffectiveProgress}/{EffectiveProgressTotal} (再試行含む)";
                string elapsed = ElapsedTimeText;
                string remaining = EstimatedRemainingText;
                if (string.IsNullOrEmpty(elapsed) && string.IsNullOrEmpty(remaining))
                    return baseText;
                if (string.IsNullOrEmpty(remaining))
                    return $"{baseText}  ┃  {elapsed}";
                return $"{baseText}  ┃  {elapsed} / {remaining}";
            }
        }

        /// <summary>
        /// プログレスバーテキストのツールチップ。「○○/○○」が何を意味するかをユーザーに説明する。
        /// </summary>
        public string ProgressTextTooltip =>
            $"全荷重ケースの全ステップ評価数 (再試行含む)\n" +
            $"  - 各ケースの計画ステップ数 (counter-loading 検出時は 2× 自動増加)\n" +
            $"  - ステップ未収束で再試行発動時は nStep が倍増し総数も増える\n" +
            $"  - 例: 2 ケース × 16 ステップ → 各ケース retry で 32 に倍増 → 計 80 評価";

        // v29 (2026-04-27): 解析開始時刻 (経過/残り時間表示用)。RunAsync 冒頭でセット。
        private DateTime? _analysisStartUtc;
        private System.Timers.Timer? _elapsedTimer;

        private void StartElapsedTimer()
        {
            StopElapsedTimer();
            _elapsedTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _elapsedTimer.Elapsed += (_, __) =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    OnPropertyChanged(nameof(ElapsedTimeText));
                    OnPropertyChanged(nameof(EstimatedRemainingText));
                    OnPropertyChanged(nameof(ProgressText));
                });
            };
            _elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            if (_elapsedTimer != null)
            {
                _elapsedTimer.Stop();
                _elapsedTimer.Dispose();
                _elapsedTimer = null;
            }
        }

        /// <summary>解析開始からの経過時間 (mm:ss)。未開始時は空文字。</summary>
        public string ElapsedTimeText
        {
            get
            {
                if (!_analysisStartUtc.HasValue) return string.Empty;
                var elapsed = DateTime.UtcNow - _analysisStartUtc.Value;
                return $"経過 {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            }
        }

        /// <summary>線形外挿による残り時間推定 (mm:ss)。進捗 0 / 完了時は空文字。</summary>
        public string EstimatedRemainingText
        {
            get
            {
                if (!_analysisStartUtc.HasValue) return string.Empty;
                int total = TotalCalculationCount;
                int done = CurrentProgress;
                if (done <= 0 || total <= 0 || done >= total) return string.Empty;
                var elapsed = DateTime.UtcNow - _analysisStartUtc.Value;
                double secPerStep = elapsed.TotalSeconds / done;
                double remainingSec = secPerStep * (total - done);
                int rm = (int)(remainingSec / 60);
                int rs = (int)(remainingSec % 60);
                return $"残り {rm:D2}:{rs:D2} (推定)";
            }
        }

        // コンストラクタ（軽量: UIが先に表示されるようにモデル作成は遅延）
        public HorizontalCalculationViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // 起動診断ログ — HCM が新規生成された瞬間とその時の入力状態を記録
            try
            {
                int piles = mainWindowViewModel?.CurrentInputModel?.PileLayoutItems?.Count ?? -1;
                int activeCases = mainWindowViewModel?.CurrentInputModel?.LoadCasesInput?.AnalysisTargetSeismicLoadCases?.Count ?? -1;
                int applicableCombos = mainWindowViewModel?.CurrentInputModel?.LoadCasesInput?.AllLoadCombinations?.Count(c => c.IsApplicable) ?? -1;
                Serilog.Log.Warning(
                    "[HCM ctor] new HorizontalCalculationViewModel: piles={P}, activeCases={C}, applicableCombos={K}, isHorizontalAnalysisDone={D}",
                    piles, activeCases, applicableCombos, mainWindowViewModel?.IsHorizontalAnalysisDone);
            }
            catch { /* 診断ログ失敗は無視 */ }

            // LoadCase / LoadCombination の IsApplicable 変更時に F9 ボタン活性 + 派生カウント表示を再評価
            // (これがないと、ユーザがチェックボックスを変更しても Command が再評価されず F9 が灰色のまま、
            //  かつ「全解析ステップ数」表示も更新されない)
            try
            {
                var lcInput = _mainWindowViewModel?.CurrentInputModel?.LoadCasesInput;
                if (lcInput != null)
                {
                    void OnApplicabilityChanged()
                    {
                        ExecuteAnalysisCommand?.NotifyCanExecuteChanged();
                        OnPropertyChanged(nameof(TotalCalculationCount));
                        OnPropertyChanged(nameof(TotalLoadCaseCount));
                        OnPropertyChanged(nameof(TotalPlannedCaseCount));
                        OnPropertyChanged(nameof(PendingCaseCount));
                    }
                    void HookApplicabilityChanged(System.ComponentModel.INotifyPropertyChanged item)
                    {
                        if (item == null) return;
                        item.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(LoadCase.IsApplicable)
                                || e.PropertyName == nameof(LoadCombination.IsApplicable)
                                || e.PropertyName == nameof(LoadCase.IsAnalysisTarget))
                            {
                                OnApplicabilityChanged();
                            }
                        };
                    }
                    foreach (var lc in lcInput.LoadCasesLevel1) HookApplicabilityChanged(lc);
                    foreach (var lc in lcInput.LoadCasesLevel2) HookApplicabilityChanged(lc);
                    foreach (var c in lcInput.LoadCombinations) HookApplicabilityChanged(c);
                }
            }
            catch { /* 購読失敗は致命的でない */ }

            // 重い処理(OnAnalysisModeling)はInitializeModelAsync()に移動
            // → ウィンドウのLoadedイベントから呼び出す

            PauseAnalysisCommand = new ToolkitRelayCommand(OnPauseAnalysis, () => IsAnalysisRunning);
            ResumeAnalysisCommand = new ToolkitRelayCommand(OnResumeAnalysis, () => IsAnalysisRunning);
            CancelAnalysisCommand = new ToolkitRelayCommand(OnCancelAnalysis, () => IsAnalysisRunning);

            // 追加実行 (段階追加再解析) コマンド
            ExecuteAdditiveAnalysisCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(
                OnExecuteAdditiveAnalysis, CanExecuteAdditiveAnalysis);

            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.PropertyChanged += LoadCase_PropertyChanged;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.PropertyChanged += LoadCase_PropertyChanged;
            foreach (var item in InputModel.LoadCasesInput.AllLoadCombinations)
                item.PropertyChanged += LoadCase_PropertyChanged;

            // コレクション変更時にも購読
            InputModel.LoadCasesInput.LoadCasesLevel1.CollectionChanged += LoadCasesLevel1_CollectionChanged;
            InputModel.LoadCasesInput.LoadCasesLevel2.CollectionChanged += LoadCasesLevel2_CollectionChanged;
            InputModel.LoadCasesInput.LoadCombinations.CollectionChanged += LoadCombinations_CollectionChanged;
        }

        /// <summary>
        /// ウィンドウ表示後に呼び出す非同期モデル初期化。
        /// バリデーションはUIスレッドで実行し、FEMモデル作成はバックグラウンドで実行。
        /// </summary>
        public async Task InitializeModelAsync()
        {
            IsModelCreating = true;
            try
            {
                // バリデーション（UIスレッド: MessageBox使用のため）
                if (!ValidateForAnalysisModeling())
                {
                    RequestClose?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // PileLayoutItems の No/PileNo 振り直しは UI スレッドで実施する。
                // (AnalysisModelling 内で振り直すと bg スレッドから item プロパティが変更され、
                //  PileLayoutItems がバインドされた DataGrid の CollectionView が
                //  CollectionChanged を bg スレッドから発火し NotSupportedException が発生するため)
                PileDesign.FEM.AnalysisModelling.EnsurePileNumbersSequential(InputModel);

                // 重いFEMモデル作成をバックグラウンドで実行
                AnalysisModelling? modelling = null;
                string? errorMessage = null;
                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                await Task.Run(() =>
                {
                    try
                    {
                        modelling = new AnalysisModelling(InputModel);
                    }
                    catch (InvalidOperationException ex)
                    {
                        errorMessage = ex.Message;
                    }
                });
                // System.Diagnostics.Debug.WriteLine($"[InitializeModelAsync] AnalysisModelling total: {swTotal.ElapsedMilliseconds}ms");

                if (errorMessage != null || modelling == null)
                {
                    MessageService.Show(errorMessage ?? "モデル作成に失敗しました。", "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    RequestClose?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // UIスレッドでモデルをセットアップ
                AnalysisModelling = modelling;
                SetupAnalysisModel();
            }
            finally
            {
                IsModelCreating = false;
            }
        }

        /// <summary>
        /// モデル作成前のバリデーション（UIスレッドで実行）
        /// </summary>
        private bool ValidateForAnalysisModeling()
        {
            if (ConnectionMode != FoundationBeamConnectionMode.RigidFloor)
                return true;

            var fbInput = InputModel.FoundationBeamInput;

            // 基礎梁入力がない、または梁要素が定義されていない場合は剛体連結に自動切替
            bool hasBeams = fbInput?.Beams != null && fbInput.Beams.Count > 0;
            if (!hasBeams || fbInput == null)
            {
                ConnectionMode = FoundationBeamConnectionMode.RigidBody;
                return true;
            }

            // 基礎梁はあるが節点参照が不正な場合はエラー
            bool hasFoundationNodes = fbInput.Nodes != null && fbInput.Nodes.Count > 0;
            bool hasPileReferences = fbInput.Beams!.Any(b =>
                b.NodeI_Type == NodeReferenceType.PileLayout || b.NodeJ_Type == NodeReferenceType.PileLayout);
            if (!hasFoundationNodes && !hasPileReferences)
            {
                MessageService.Show("剛床連結モードでは基礎梁節点が必要です。\n基礎梁入力で節点を定義するか、杭配置を参照してください。",
                    "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        /// <summary>
        /// AnalysisModelling完了後にAnaModelをセットアップ（UIスレッドで実行）
        /// </summary>
        private void SetupAnalysisModel()
        {
            NodesCount = AnalysisModelling.Nodes.Count;
            BeamsCount = AnalysisModelling.Beams.Count;

            var editModel = new AnaModel(
                InputModel,
                AnalysisModelling.Nodes,
                AnalysisModelling.Beams,
                AnalysisModelling.DummyBeams,
                AnalysisModelling.RigidBodies,
                AnalysisModelling.HorizontalSoilSprings,
                AnalysisModelling.RotationalSprings
            )
            {
                RotationalSprings = AnalysisModelling.RotationalSprings,
                PenaltySprings = AnalysisModelling.PenaltySprings
            };

            // メイン側に保存された既存の水平解析結果を editModel に転写
            // (ウィンドウ再オープン時に「済」列表示や追加実行を機能させるため)
            SeedEditModelFromMainCurrentModel(editModel);

            if (AnaModels.Count > 1)
                AnaModels[1] = editModel;
            else if (AnaModels.Count == 1)
                AnaModels.Add(editModel);
            else
                AnaModels.Add(editModel);

            // 「済」列を更新 + 追加実行ボタンの enable 状態再評価
            RefreshCompletedCaseKeys();

            // CommandManager.RequerySuggested を待たず即座に CanExecute を再評価
            // (シード後 AnalysisStepResults が増えても WPF はフォーカス変化等まで気付かない)
            ExecuteAdditiveAnalysisCommand?.NotifyCanExecuteChanged();

            // 既存結果があれば「実行済み」として扱う (サマリーコピー等のボタンも有効化)
            if (editModel.AnalysisStepResults?.Count > 0)
                IsAnalysisExecuted = true;
        }

        /// <summary>
        /// MainWindowViewModel.CurrentModel に既存の水平解析結果があれば、
        /// 新規 editModel に転写する。既存結果保持と「済」列表示・追加実行のために使用。
        /// 既存モデルとノード/ビーム件数が一致しない場合は転写を行わない (構造変更検出)。
        /// </summary>
        private void SeedEditModelFromMainCurrentModel(AnaModel editModel)
        {
            var mainModel = _mainWindowViewModel?.CurrentModel;
            if (mainModel == null) return;
            if (mainModel.AnalysisStepResults == null || mainModel.AnalysisStepResults.Count == 0) return;

            // 構造一致チェック (件数のみ。InputModel が変わっていれば既存の
            // CheckAndResetAnalysisResults が main 側結果を破棄しているはず)
            if (mainModel.Nodes?.Count != editModel.Nodes?.Count) return;
            if (mainModel.Beams?.Count != editModel.Beams?.Count) return;

            // ステップ結果コピー (LoadCase/LoadCombination は InputModel 経由で共有)
            editModel.AnalysisStepResults.Clear();
            foreach (var r in mainModel.AnalysisStepResults)
                editModel.AnalysisStepResults.Add(r);

            // LastRunConfig 転写 (互換性検証で参照される)
            editModel.LastRunConfig = mainModel.LastRunConfig;

            // 各要素の結果も転写 (個別グラフ・テーブル表示の整合性のため)
            // 件数チェック後なのでインデックスで対応
            for (int i = 0; i < editModel.Nodes.Count && i < mainModel.Nodes.Count; i++)
            {
                var src = mainModel.Nodes[i].NodeResults;
                if (src == null) continue;
                editModel.Nodes[i].NodeResults.Clear();
                foreach (var nr in src) editModel.Nodes[i].NodeResults.Add(nr);
            }
            for (int i = 0; i < editModel.Beams.Count && i < mainModel.Beams.Count; i++)
            {
                var src = mainModel.Beams[i].BeamResults;
                if (src == null) continue;
                editModel.Beams[i].BeamResults.Clear();
                foreach (var br in src) editModel.Beams[i].BeamResults.Add(br);
            }
            if (mainModel.HorizontalSoilSprings != null && editModel.HorizontalSoilSprings != null
                && mainModel.HorizontalSoilSprings.Count == editModel.HorizontalSoilSprings.Count)
            {
                for (int i = 0; i < editModel.HorizontalSoilSprings.Count; i++)
                {
                    var src = mainModel.HorizontalSoilSprings[i].HorizontalSpringResults;
                    if (src == null) continue;
                    editModel.HorizontalSoilSprings[i].HorizontalSpringResults.Clear();
                    foreach (var sr in src) editModel.HorizontalSoilSprings[i].HorizontalSpringResults.Add(sr);
                }
            }
            if (mainModel.RotationalSprings != null && editModel.RotationalSprings != null
                && mainModel.RotationalSprings.Count == editModel.RotationalSprings.Count)
            {
                for (int i = 0; i < editModel.RotationalSprings.Count; i++)
                {
                    var src = mainModel.RotationalSprings[i].RotationalSpringResults;
                    if (src == null) continue;
                    editModel.RotationalSprings[i].RotationalSpringResults.Clear();
                    foreach (var rr in src) editModel.RotationalSprings[i].RotationalSpringResults.Add(rr);
                }
            }
        }

        // コレクション変更時のハンドラ
        private void LoadCasesLevel1_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (LoadCase item in e.OldItems)
                    item.PropertyChanged -= LoadCase_PropertyChanged;
            if (e.NewItems != null)
                foreach (LoadCase item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
        }
        private void LoadCasesLevel2_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (LoadCase item in e.OldItems)
                    item.PropertyChanged -= LoadCase_PropertyChanged;
            if (e.NewItems != null)
                foreach (LoadCase item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
        }
        private void LoadCombinations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (LoadCombination item in e.OldItems)
                    item.PropertyChanged -= LoadCase_PropertyChanged;
            if (e.NewItems != null)
                foreach (LoadCombination item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
        }

        /// <summary>
        /// イベントハンドラの購読を解除してメモリリークを防止する。
        /// ウィンドウクローズ時に呼び出す。
        /// </summary>
        public void UnsubscribeEvents()
        {
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.PropertyChanged -= LoadCase_PropertyChanged;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.PropertyChanged -= LoadCase_PropertyChanged;
            foreach (var item in InputModel.LoadCasesInput.AllLoadCombinations)
                item.PropertyChanged -= LoadCase_PropertyChanged;

            InputModel.LoadCasesInput.LoadCasesLevel1.CollectionChanged -= LoadCasesLevel1_CollectionChanged;
            InputModel.LoadCasesInput.LoadCasesLevel2.CollectionChanged -= LoadCasesLevel2_CollectionChanged;
            InputModel.LoadCasesInput.LoadCombinations.CollectionChanged -= LoadCombinations_CollectionChanged;
        }

        private void LoadCase_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.IsAnalysisTarget) ||
                e.PropertyName == nameof(LoadCombination.IsApplicable))
            {
                OnPropertyChanged(nameof(TotalCalculationCount));
                OnPropertyChanged(nameof(TotalLoadCaseCount));
                OnPropertyChanged(nameof(ProgressText));
                // 選択ケース数変化に伴い「追加実行」ボタンの enable 状態も再評価
                ExecuteAdditiveAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        private void OnPauseAnalysis() => _pauseEvent.Reset();
        private void OnResumeAnalysis() => _pauseEvent.Set();
        private void OnCancelAnalysis()
        {
            _cancellationTokenSource?.Cancel();
            // ユーザー操作でキャンセルしたときにプログレスを即座にリセット
            RequestClearProgressAnimation?.Invoke();
            //CurrentProgress = 0;
        }

        // 全レベル1荷重を解析対象に設定
        [RelayCommand]
        private void ApplyAllLoadCasesLevel1()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel1 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.IsAnalysisTarget = true;
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(TotalLoadCaseCount));
            OnPropertyChanged(nameof(ProgressText));
        }

        // 全レベル1荷重を解析対象から除外
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel1()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel1 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.IsAnalysisTarget = false;
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(TotalLoadCaseCount));
            OnPropertyChanged(nameof(ProgressText));
        }

        // 全レベル2荷重を解析対象に設定
        [RelayCommand]
        private void ApplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsAnalysisTarget = true;
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(TotalLoadCaseCount));
            OnPropertyChanged(nameof(ProgressText));
        }

        // 全レベル2荷重を解析対象から除外
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsAnalysisTarget = false;
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(TotalLoadCaseCount));
            OnPropertyChanged(nameof(ProgressText));
        }

        [RelayCommand]
        private void OnOk()
        {
            // 解析が未実行の場合は警告を表示して終了
            if (!IsAnalysisExecuted)
            {
                MessageService.Show("解析が未了です。解析を実行してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (AnaModels.Count > 1)
            {
                // 本体モデル（AnaModels[0]）を削除し、編集モデル（AnaModels[1]）を本体に昇格
                AnaModels.RemoveAt(0);
            }

            // MainWindowViewModelにCurrentModelをセット
            if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                vm.CurrentModel = this.CurrentModel; // AnaModels[0]など
                vm.IsHorizontalAnalysisDone = this.CurrentModel != null;
                vm.RefreshResultTablesFromLastStep(); // 追加
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // 解析実行済みの場合は確認メッセージを表示
            if (IsAnalysisExecuted)
            {
                var result = MessageService.Show(
                    "解析結果を登録せずにウィンドウを閉じますか？\n（「はい」を選ぶと結果は破棄されます）",
                    "確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    // 「いいえ」なら何もしない
                    return;
                }
            }

            // 編集用モデル（AnaModels[1]）が存在する場合は削除
            if (AnaModels.Count > 1)
            {
                AnaModels.RemoveAt(1);
            }

            // 「破棄して閉じる」はメイン側の既存結果 (RESULTS_OLD) を保持するのが
            // 本来の意味。ここで CurrentModel を上書きすると AnaModels[0] に入っている
            // 空の編集用 AnaModel がメインに流れ込み、既存結果が消失する。
            // → メインの CurrentModel には一切触れない。

            // ダイアログを閉じる
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // 水平解析モデルの作成（成功時 true、失敗時 false を返す）
        private bool TryCreateAnalysisModel()
        {
            // 剛床連結モードの事前バリデーション
            if (ConnectionMode == FoundationBeamConnectionMode.RigidFloor)
            {
                var fbInput = InputModel.FoundationBeamInput;
                bool hasBeams = fbInput?.Beams != null && fbInput.Beams.Count > 0;

                if (!hasBeams)
                {
                    // 基礎梁がない場合は自動的に剛体連結に切り替える
                    ConnectionMode = FoundationBeamConnectionMode.RigidBody;
                    MessageService.Show("基礎梁要素が定義されていないため、剛体連結モードに切り替えて解析を実行します。",
                        "接続モード変更", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (fbInput != null)
                {
                    // 基礎梁はあるが節点参照が不正な場合はエラー
                    bool hasFoundationNodes = fbInput.Nodes != null && fbInput.Nodes.Count > 0;
                    bool hasPileReferences = fbInput.Beams!.Any(b =>
                        b.NodeI_Type == NodeReferenceType.PileLayout || b.NodeJ_Type == NodeReferenceType.PileLayout);
                    if (!hasFoundationNodes && !hasPileReferences)
                    {
                        MessageService.Show("剛床連結モードでは基礎梁節点が必要です。\n基礎梁入力で節点を定義するか、杭配置を参照してください。",
                            "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }

            try
            {
                // UI スレッドでの構築だが、AnalysisModelling 内部の事前条件 (No/PileNo 連番) を満たすため
                // ここでも EnsurePileNumbersSequential を呼出す。
                PileDesign.FEM.AnalysisModelling.EnsurePileNumbersSequential(InputModel);
                AnalysisModelling = new AnalysisModelling(InputModel);
            }
            catch (InvalidOperationException ex)
            {
                MessageService.Show(ex.Message, "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            NodesCount = AnalysisModelling.Nodes.Count;
            BeamsCount = AnalysisModelling.Beams.Count;

            // 編集用モデルを新規作成
            var editModel = new AnaModel(
                InputModel,
                AnalysisModelling.Nodes,
                AnalysisModelling.Beams,
                AnalysisModelling.DummyBeams,
                AnalysisModelling.RigidBodies,
                AnalysisModelling.HorizontalSoilSprings,
                AnalysisModelling.RotationalSprings
            )
            {
                RotationalSprings = AnalysisModelling.RotationalSprings,
                PenaltySprings = AnalysisModelling.PenaltySprings
            };

            // 既に編集用モデルが存在する場合は入れ替え
            if (AnaModels.Count > 1)
            {
                AnaModels[1] = editModel;
            }
            else if (AnaModels.Count == 1)
            {
                AnaModels.Add(editModel);
            }
            else // 初回（本体モデルも未作成）
            {
                // 本体モデルとしても追加
                AnaModels.Add(editModel);
            }

            return true;
        }

        // コマンド用ラッパー（コンストラクタ・手動呼び出し用）
        [RelayCommand]
        private void OnAnalysisModeling() => TryCreateAnalysisModel();

        /// <summary>
        /// 各杭の Z 範囲と対応する地盤の Z 範囲が交差しているかを検証する。
        /// 交差しない杭があれば、その位置情報を文字列リストで返す（空なら問題なし）。
        /// 交差しない場合は水平土ばねの剛性がゼロになり、剛性マトリクスが特異になるため解析不可。
        /// </summary>
        private List<string> CheckPileGroundOverlap()
        {
            var errors = new List<string>();
            if (InputModel?.PileLayoutItems == null || InputModel.GroundsInput == null || InputModel.PileBodies == null)
                return errors;

            foreach (var pile in InputModel.PileLayoutItems)
            {
                int groundIdx = pile.GroundNo - 1;
                int pileBodyIdx = pile.PileBodyNo - 1;
                if (groundIdx < 0 || groundIdx >= InputModel.GroundsInput.Count) continue;
                if (pileBodyIdx < 0 || pileBodyIdx >= InputModel.PileBodies.Count) continue;

                var ground = InputModel.GroundsInput[groundIdx];
                var pileBody = InputModel.PileBodies[pileBodyIdx];
                if (ground?.GroundLayers == null || ground.GroundLayers.Count == 0) continue;
                if (pileBody?.PileBodySegments == null || pileBody.PileBodySegments.Count == 0) continue;

                double pileTopZ = pile.Point3D.Z;
                double pileBottomZ = pileTopZ - pileBody.PileBodySegments[^1].SegmentDepth;
                double groundTopZ = ground.GroundLayers[0].BottomAltitude + ground.GroundLayers[0].LayerThickness;
                double groundBottomZ = ground.GroundLayers[^1].BottomAltitude;

                // 杭が地盤範囲と全く交差しない (pile が完全に地盤より上 or 下)
                bool noOverlap = pileBottomZ >= groundTopZ || pileTopZ <= groundBottomZ;
                if (noOverlap)
                {
                    errors.Add($"杭No.{pile.No} (杭頭Z={pileTopZ:N3}, 杭底Z={pileBottomZ:N3}) と " +
                               $"地盤No.{pile.GroundNo} (上端Z={groundTopZ:N3}, 下端Z={groundBottomZ:N3})");
                }
            }

            return errors;
        }

        // 水平解析の実行
        [RelayCommand(CanExecute = nameof(CanExecuteAnalysis))]
        private async Task OnExecuteAnalysis() => await OnExecuteAnalysisCore(additive: false);

        /// <summary>
        /// F9 ボタンの活性条件。NumericInput でクランプされていれば常に妥当な値だが、
        /// バインド失敗 (古い値が残っている / プログラム的に外れた値) に対する最後の防壁。
        /// D.13 即時バリデーション一環として「解析実行に必要な前提条件」も含める:
        ///   - 杭が 1 本以上配置されている
        ///   - 解析対象荷重ケースが 1 件以上選択されている
        ///   - 適用される荷重組合せが 1 件以上ある
        /// これらが満たされない間 F9 ボタンが灰色になり、ユーザに「設定不足」が
        /// 視覚的にフィードバックされる。
        /// </summary>
        private bool CanExecuteAnalysis()
        {
            string DisableReason()
            {
                if (IsAnalysisRunning) return "IsAnalysisRunning=true (前回解析が完了/キャンセルしないまま固着の可能性)";
                if (Level1CalculationStepsCount < 1 || Level1CalculationStepsCount > 256) return $"Level1CalculationStepsCount={Level1CalculationStepsCount} (1-256 範囲外)";
                if (Level2CalculationStepsCount < 1 || Level2CalculationStepsCount > 256) return $"Level2CalculationStepsCount={Level2CalculationStepsCount} (1-256 範囲外)";
                if (MaxCaseDegreeOfParallelism < 1) return $"MaxCaseDegreeOfParallelism={MaxCaseDegreeOfParallelism} (<1)";
                if (FullNRIterations < 0) return $"FullNRIterations={FullNRIterations} (<0)";
                if (RelaxationFactor <= 0 || RelaxationFactor > 1) return $"RelaxationFactor={RelaxationFactor} (0<x≤1 範囲外)";
                if (InputModel == null) return "InputModel == null";
                if ((InputModel.PileLayoutItems?.Count ?? 0) == 0) return "PileLayoutItems が空";
                int activeCases = (InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases?.Count ?? 0);
                if (activeCases == 0) return "AnalysisTargetSeismicLoadCases が 0 件 (荷重ケースの IsAnalysisTarget=true を確認)";
                int activeCombinations = InputModel.LoadCasesInput.AllLoadCombinations?.Count(c => c.IsApplicable) ?? 0;
                if (activeCombinations == 0) return "AllLoadCombinations の IsApplicable=true が 0 件";
                return null;
            }

            var reason = DisableReason();
            if (reason != null)
            {
                // 診断ログ — F9 が灰色のままになる原因を即座に追跡できるよう毎回出力
                // Warning レベルで出力 (Info より確実にログに残る + filter で見つけやすい)
                Serilog.Log.Warning("[F9 disabled] {Reason}", reason);
                return false;
            }
            // 活性時も 1 度ログを出して、CanExecute が確実に呼ばれていることを確認できるように
            Serilog.Log.Information("[F9 enabled] CanExecuteAnalysis returned true");
            return true;
        }

        /// <summary>追加実行 (段階追加再解析)。既存結果を保持し、未計算ケースのみ実行。</summary>
        private async Task OnExecuteAdditiveAnalysis() => await OnExecuteAnalysisCore(additive: true);

        /// <summary>
        /// 解析対象 AnaModel を安全に取得 (Count==0 で IndexOutOfRange を出さない)。
        /// AnaModels[1] (編集用) があればそれ、なければ [0] (本体)、空なら null。
        /// </summary>
        private AnaModel? TryGetTargetAnaModel()
        {
            if (AnaModels == null) return null;
            if (AnaModels.Count > 1) return AnaModels[1];
            if (AnaModels.Count > 0) return AnaModels[0];
            return null;
        }

        private bool CanExecuteAdditiveAnalysis()
        {
            if (IsAnalysisRunning) return false;
            if (TotalCalculationCount <= 0) return false;
            var target = TryGetTargetAnaModel();
            return target?.AnalysisStepResults?.Count > 0;
        }

        /// <summary>
        /// プリフライト用サマリを構築する。AnalysisPreflightDialog にそのまま渡せる形。
        /// </summary>
        private Views.AnalysisPreflightSummary BuildPreflightSummary()
        {
            int level1Count = InputModel.LoadCasesInput.LoadCasesLevel1?
                .Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;
            int level2Count = InputModel.LoadCasesInput.LoadCasesLevel2?
                .Count(x => x.IsAnalysisTarget && (x.UpperMassForce != 0 || x.FoundationMassForce != 0)) ?? 0;
            int liquefactionFactor = LiquefactionOption == LiquefactionOptionType.Both ? 2 : 1;

            int combinationCount = InputModel.LoadCasesInput.AllLoadCombinations?
                .Count(x => x.IsApplicable) ?? 0;

            // CounterLoading: βU × βL < 0 の組合せ数
            int counterLoadingCount = InputModel.LoadCasesInput.AllLoadCombinations?
                .Count(x => x.IsApplicable && x.Beta1 * x.Beta2 < 0) ?? 0;

            int nonLinearCases = InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases?
                .Count(lc => lc.IsPileNonLinear) ?? 0;

            int parallelism = Math.Max(1, MaxCaseDegreeOfParallelism);
            int totalSteps = TotalCalculationCount;

            string loadCaseText = liquefactionFactor == 2
                ? $"L1={level1Count} / L2={level2Count} (液状化: あり/なし両方)"
                : $"L1={level1Count} / L2={level2Count}";

            return new Views.AnalysisPreflightSummary(
                AnalysisName: "水平解析",
                TotalSteps: totalSteps,
                LoadCaseCountText: loadCaseText,
                CombinationCount: combinationCount,
                CounterLoadingCount: counterLoadingCount,
                NonLinearLoadCaseCount: nonLinearCases,
                MaxParallelism: parallelism);
        }

        private async Task OnExecuteAnalysisCore(bool additive)
        {
            // 既存の解析結果がある場合は警告 (新規実行のみ。追加実行は既存結果保持が前提なのでスキップ)
            // メイン側 (OK 済み) または、現セッションでロードした「済」結果のいずれかがあれば確認
            bool hasExistingResults = _mainWindowViewModel.IsHorizontalAnalysisDone
                || (TryGetTargetAnaModel()?.AnalysisStepResults?.Count > 0);
            if (!additive && hasExistingResults)
            {
                var result = MessageService.Show(
                    "既に水平解析の結果が存在します。\n再実行すると既存の結果は上書きされます。\n\n解析を実行しますか？",
                    "解析結果の上書き確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            // 杭と地盤の Z 範囲整合性チェック（杭がすべて地盤外の場合は解析不可）
            var pileGroundErrors = CheckPileGroundOverlap();
            if (pileGroundErrors.Count > 0)
            {
                var msg = "以下の杭は地盤と重なっていないため、水平解析を実行できません。\n" +
                          "基本設定の「Z=0 の標高」を確認するか、杭頭 Z を見直してください。\n\n" +
                          string.Join("\n", pileGroundErrors);
                MessageService.Show(msg, "杭-地盤位置の不整合", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 剛床仮定 (RigidFloor) で基礎梁の材料・断面が空の場合の警告
            var fbInput = InputModel?.FoundationBeamInput;
            if (fbInput != null
                && fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidFloor
                && (fbInput.Materials.Count == 0 || fbInput.Sections.Count == 0))
            {
                var fbMsg = "剛床仮定（RigidFloor）で水平解析を実行しようとしていますが、基礎梁の" +
                            (fbInput.Materials.Count == 0 && fbInput.Sections.Count == 0 ? "材料一覧・断面一覧"
                             : fbInput.Materials.Count == 0 ? "材料一覧" : "断面一覧") +
                            "が登録されていません。\n\n" +
                            "このまま続行するとデフォルト値（FC30 / 0.8m×2.0m 矩形断面）で解析されます。\n" +
                            "材料・断面を入力してから解析する場合はキャンセルを押してください。";
                var fbResult = MessageService.Show(fbMsg, "基礎梁 材料・断面 未登録", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (fbResult != MessageBoxResult.OK)
                    return;
            }

            // 荷重がゼロの荷重ケースをチェック
            var zeroForceLoadCases = InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases
                .Where(lc => lc.UpperMassForce == 0 && lc.FoundationMassForce == 0)
                .ToList();

            if (zeroForceLoadCases.Count > 0)
            {
                var levelNames = zeroForceLoadCases
                    .Select(lc => $"レベル{lc.Level}-{lc.No}")
                    .Distinct();
                var message = $"以下の荷重ケースで荷重がゼロのため解析をスキップします:\n{string.Join(", ", levelNames)}\n\n" +
                               "荷重ケースウィンドウで上部構造質量荷重または基礎構造質量荷重を設定してください。";
                MessageService.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

                // すべての荷重ケースがゼロの場合は解析を中止
                if (zeroForceLoadCases.Count == InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases.Count)
                {
                    return;
                }
            }

            // キャプテンパイル工法またはFT-Pile構法を使用している場合のチェック
            bool hasSemiRigidPileTop = InputModel.PileBodies?.Any(pb =>
                pb.PileTopType?.Contains("キャプテンパイル工法") == true ||
                pb.PileTopType?.Contains("FT-Pile構法") == true) ?? false;

            if (hasSemiRigidPileTop)
            {
                // IsPileNonLinear=false の荷重ケースがあるかチェック
                var nonLinearOffLoadCases = InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases
                    .Where(lc => !lc.IsPileNonLinear)
                    .ToList();

                if (nonLinearOffLoadCases.Count > 0)
                {
                    var levelNames = nonLinearOffLoadCases
                        .Select(lc => $"レベル{lc.Level}-{lc.No}")
                        .Distinct();
                    var message = $"杭頭半剛接合（キャプテンパイル工法またはFT-Pile構法）を使用していますが、" +
                                   $"以下の荷重ケースで「杭体の非線形」が無効になっています:\n{string.Join(", ", levelNames)}\n\n" +
                                   "半剛接合の効果を考慮するには杭体の非線形を有効にする必要があります。\n" +
                                   "すべての荷重ケースで杭体の非線形を有効にしますか？";

                    var result = MessageService.Show(message, "杭頭半剛接合の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // すべての荷重ケースで IsPileNonLinear を true に設定
                        foreach (var lc in InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                        {
                            lc.IsPileNonLinear = true;
                        }
                        // 共通設定も更新
                        if (InputModel.LoadCasesInput.LoadCaseLevel1Common != null)
                            InputModel.LoadCasesInput.LoadCaseLevel1Common.IsPileNonLinear = true;
                        if (InputModel.LoadCasesInput.LoadCaseLevel2Common != null)
                            InputModel.LoadCasesInput.LoadCaseLevel2Common.IsPileNonLinear = true;
                    }
                }
            }

            // 杭体の非線形が有効で解析ステップ数が少ない場合の警告
            var nonLinearOnLoadCases = InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases
                .Where(lc => lc.IsPileNonLinear)
                .ToList();

            if (nonLinearOnLoadCases.Count > 0)
            {
                bool needsMoreSteps = false;
                string suggestedAction = "";

                // レベル1の非線形解析がある場合、ステップ数をチェック
                bool hasLevel1NonLinear = nonLinearOnLoadCases.Any(lc => lc.Level == 1);
                bool hasLevel2NonLinear = nonLinearOnLoadCases.Any(lc => lc.Level == 2);

                if (hasLevel1NonLinear && Level1CalculationStepsCount < 4)
                {
                    needsMoreSteps = true;
                    suggestedAction += $"レベル1の解析ステップ数が {Level1CalculationStepsCount} と少なくなっています。\n";
                }
                if (hasLevel2NonLinear && Level2CalculationStepsCount < 8)
                {
                    needsMoreSteps = true;
                    suggestedAction += $"レベル2の解析ステップ数が {Level2CalculationStepsCount} と少なくなっています。\n";
                }

                if (needsMoreSteps)
                {
                    var message = $"杭体の非線形解析が有効ですが、解析ステップ数が少ない可能性があります。\n\n" +
                                   suggestedAction +
                                   "\n収束性や精度を向上させるため、解析ステップ数を増やすことをお勧めします。\n" +
                                   "（推奨: レベル1は4以上、レベル2は16〜32）\n\n" +
                                   "解析ステップ数を変更しますか？";

                    var result = MessageService.Show(message, "解析ステップ数の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 推奨値に設定
                        if (hasLevel1NonLinear && Level1CalculationStepsCount < 4)
                        {
                            Level1CalculationStepsCount = 4;
                        }
                        if (hasLevel2NonLinear && Level2CalculationStepsCount < 8)
                        {
                            Level2CalculationStepsCount = 16;
                        }
                        MessageService.Show($"解析ステップ数を更新しました。\n" +
                                         $"レベル1: {Level1CalculationStepsCount}\n" +
                                         $"レベル2: {Level2CalculationStepsCount}",
                                         "設定更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }

            // モデル作成（進捗ウィンドウ表示前に実施。失敗時はここで中止）
            // 追加実行モードでは既存 editModel (前回 RunAsync の結果と LastRunConfig を保持)
            // を再利用するため TryCreateAnalysisModel は呼ばない (呼ぶと AnaModels[1] が
            // フレッシュなモデルで上書きされ、前回情報が失われる)
            if (!additive)
            {
                if (!TryCreateAnalysisModel())
                    return;
            }
            else
            {
                // 追加実行: 既存 editModel に結果が残っていることを確認
                var existingTarget = TryGetTargetAnaModel();
                if (existingTarget == null || existingTarget.AnalysisStepResults == null ||
                    existingTarget.AnalysisStepResults.Count == 0)
                {
                    MessageService.Show(
                        "追加実行のための既存結果が見つかりません。\n通常実行でやり直してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 追加実行モードのみ: 前回設定との互換性チェック
            if (additive)
            {
                if (!ValidateIncrementalCompatibility(out var reason))
                {
                    var choice = MessageService.Show(
                        $"追加実行できません。前回設定と相違があります。\n\n{reason}\n\n" +
                        "「はい」: 既存結果を破棄して新規実行に切替\n" +
                        "「いいえ」: キャンセル",
                        "互換性チェック失敗", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes) return;
                    additive = false;
                }
            }

            // プリフライト: ステップ数 / 並列度 / CounterLoading / 推定時間をユーザーに提示し、
            // 実行可否を最終確認する (User 設定で無効化可)。追加実行はスキップ (差分のため意味が薄い)。
            if (!additive)
            {
                var summary = BuildPreflightSummary();
                var owner = Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
                if (!Views.AnalysisPreflightDialog.Confirm(owner, summary))
                    return;
            }

            IsAnalysisRunning = true;
            // 新規実行時のみ「未実行」状態に戻す (追加実行は実行済み状態を維持)
            if (!additive) IsAnalysisExecuted = false;
            _cancellationTokenSource = new CancellationTokenSource();

            // ボタン押下直後にログを表示
            await AddLogAsync(additive ? "追加実行: 計算モデル作成開始" : "計算モデル作成開始");

            var progress = new Progress<Models.AnalysisProgress>();

            // ローカル変数化: ラムダで closure するため
            bool isAdditive = additive;
            try
            {
                // 解析実行を非同期で行う
                await Task.Run(async () => {
                    await RunAsync(_cancellationTokenSource.Token, progress, additive: isAdditive);
                });

                IsAnalysisExecuted = true; // 解析実行済みフラグをセット

                // 計算完了通知（UIスレッドで直接表示）
                MessageService.Show("計算が終了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("計算がキャンセルされました。");
                IsAnalysisExecuted = false;
                // キャンセル時もログをメイン画面に保存（後で確認可能にする）
                Application.Current?.Dispatcher.Invoke(() =>
                    _mainWindowViewModel.SetLatestAnalysisLogs(CalculationLog));
                RequestClearProgressAnimation?.Invoke();
            }
            catch (Exception ex)
            {
                await AddLogAsync($"解析中にエラーが発生しました: {ex.Message}");
                await AddLogAsync($"スタックトレース: {ex.StackTrace}");

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _mainWindowViewModel.SetLatestAnalysisLogs(CalculationLog);
                    MessageService.Show($"解析中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                IsAnalysisExecuted = false;
                RequestClearProgressAnimation?.Invoke();
            }
            finally
            {
                IsAnalysisRunning = false;

                // v29: 経過時間タイマーを停止 (キャンセル/エラー時の取りこぼし防止)
                StopElapsedTimer();
                OnPropertyChanged(nameof(ElapsedTimeText));
                OnPropertyChanged(nameof(EstimatedRemainingText));
                OnPropertyChanged(nameof(ProgressText));

                // 並列モニタが開いていれば閉じる (正常終了/キャンセル/エラー全てで共通)
                Application.Current?.Dispatcher.Invoke(() => RequestHideParallelMonitor?.Invoke());

                // CancellationTokenSourceをDisposeしてリソース解放
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // 解析完了後の追加実行ボタン enable 状態を再評価
                // (RunAsync で AnalysisStepResults が増えても自動再評価されないため明示)
                ExecuteAdditiveAnalysisCommand?.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// ウィンドウクローズ時に呼び出されるクリーンアップメソッド
        /// 実行中の解析をキャンセルし、完了を待機する
        /// </summary>
        public async Task CleanupAsync()
        {
            // タイマーを停止してUIへのポストを止める
            if (_logTimerStarted)
            {
                _logFlushTimer.Stop();
                if (_logFlushHandler != null)
                    _logFlushTimer.Elapsed -= _logFlushHandler;
                _logFlushTimer.Dispose();
            }

            if (IsAnalysisRunning && _cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();

                // 解析が終了するまで少し待機（最大3秒）
                int waitCount = 0;
                while (IsAnalysisRunning && waitCount < 30)
                {
                    await Task.Delay(100);
                    waitCount++;
                }

                if (IsAnalysisRunning)
                {
                }
                else
                {
                }
            }
        }

        // 荷重ケースの代表軸力Nで PileSection.GetMPhi/MPhiRelationship を呼び、各梁にセット
        // こちらも安全なヘルパに統一（例外発生源を除去）
        // v6: InputModel.PileBodies ではなく SoilPile.PileBodySegments を使用
        private void SetupMPhiFromPileSectionForLoadCase(AnaModel model, LoadCase loadCase)
        {

            if (model == null)
            {
                return;
            }
            if (!loadCase.IsPileNonLinear)
            {
                return;
            }

            int totalBeams = model.Beams.Count;
            int skippedNoPileBody = 0;
            int skippedNoSoilPile = 0;
            int skippedInvalidSeg = 0;
            int skippedNoSection = 0;
            int skippedNoCurve = 0;
            int successCount = 0;

            // SoilPileをPileBodyNoでキャッシュ（同じPileBodyNoを持つ最初のSoilPileを使用）
            var soilPileByPileBodyNo = new Dictionary<int, SoilPile>();
            if (InputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in InputModel.ElementDivision.SoilPiles)
                {
                    if (sp.PileBodyNo > 0 && !soilPileByPileBodyNo.ContainsKey(sp.PileBodyNo))
                    {
                        soilPileByPileBodyNo[sp.PileBodyNo] = sp;
                    }
                }
            }

            // PileLayoutDataItemをPileBodyNoでキャッシュ（軸力取得用）
            // 注: 複数のPileが同じPileBodyNoを使用する場合は代表値（最初のもの）を使用
            var pileByPileBodyNo = new Dictionary<int, PileLayoutDataItem>();
            if (InputModel.PileLayoutItems != null)
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    if (pile.PileBodyNo > 0 && !pileByPileBodyNo.ContainsKey(pile.PileBodyNo))
                    {
                        pileByPileBodyNo[pile.PileBodyNo] = pile;
                    }
                }
            }

            foreach (var beam in model.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                {
                    skippedNoPileBody++;
                    continue;
                }

                // SoilPileを取得（PileBodyNoで検索）
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile))
                {
                    skippedNoSoilPile++;
                    continue;
                }

                // SoilPile.PileBodySegments を使用（杭要素分割後のセグメント）
                if (seg < 0 || seg >= soilPile.PileBodySegments.Count)
                {
                    skippedInvalidSeg++;
                    continue;
                }

                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null)
                {
                    skippedNoSection++;
                    continue;
                }

                // 軸力を取得（PileLayoutItemから）
                // 注: pile.AxialForce / model.GetAxialForce は kN 単位で格納されている
                //     (UI 入力 (kN), SetAxialForce コメント [kN], AxialForceLevel{1,2}s [kN] と整合)。
                //     PileSection.GetMPhiRelationship は kN を期待 (内部で *1000 して N に変換)。
                //     旧実装は誤って /1000.0 で「N→kN 変換」していたため、軸力が 1/1000 で
                //     M-φ が 24% 程度過小評価される単位バグがあった (検証テスト: PileSectionMPhiUnitTests)。
                // 初期セットアップではケース固有の入力軸力 (AxialForceLevel{1,2}s) を優先。
                // (per-step の SetupMPhiByCurrentAxialForMiddleBeam がステップごとに再解決するため、
                //  ここでの値は step 0 の K 行列構築時に効く)
                double axialN_kN = 0.0;
                if (pileByPileBodyNo.TryGetValue(pb, out var pile))
                {
                    try
                    {
                        double nSeis = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                        if (double.IsFinite(nSeis) && nSeis != 0.0)
                            axialN_kN = nSeis;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[SetupMPhi] GetSeismicAxialForce(loadCaseNo={No}, level={Lv}) failed, fallback to gravity baseline.",
                            loadCase.No, loadCase.Level);
                    }
                    if (axialN_kN == 0.0)
                    {
                        axialN_kN = model.GetAxialForce(pile); // kN フォールバック (重力ベース)
                    }
                }

                // 場所打ち鋼管コンクリート杭: 杭頭部と杭中間部で異なるM-φを適用
                (IList<double> Phis, IList<double> Moments)? curve;
                if (!beam.IsPileTop
                    && section.PileBodyType == "場所打ち鋼管コンクリート杭"
                    && section.PileSectionType == "鋼管コンクリート部")
                {
                    var sprcSection = new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(section.PipeGrade, section.PipeDia, section.PipeTs, section.CorrosionDepth),
                        new InsituConcrete(section.ConcreteOutDia, section.ConcreteGsi, section.ConcreteFc),
                        new MainBars(section.MainBarDr, section.MainBarNum, section.MainBarSpec, section.MainBarSize));
                    // 単位変換: kN → N（断面計算はN単位を期待）
                    var middle = sprcSection.GetMPhiRelationshipForMiddle(axialN_kN * 1000.0);
                    // 単位変換: φ [1/mm] → [1/m], M [N·mm] → [kN·m]
                    var phisConverted = middle.Phis.Select(p => p * 1000.0).ToList();
                    var msConverted = middle.Moments.Select(m => m * 1e-6).ToList();
                    curve = ((IList<double>)phisConverted, (IList<double>)msConverted);
                }
                else
                {
                    curve = TryCallMPhiRelationship(section, axialN_kN);
                }

                if (curve is null)
                {
                    skippedNoCurve++;
                    continue;
                }

                beam.SetResolvedCombinedMPhi(curve.Value.Phis, curve.Value.Moments);
                successCount++;
            }

            // キャッシュ統計を出力
            var (hits, misses, cacheSize) = PileSection.GetMphiCacheStats();
        }

        // M-φ関係
        private static (IList<double> Phis, IList<double> Moments)? TryCallMPhiRelationship(object pileSection, double axialN)
        {
            if (pileSection == null)
            {
                return null;
            }

            var t = pileSection.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 候補名（大小両対応）
            string[] candidateNames = ["GetMPhiRelationship", "GetMPhiRelationship"];

            // すべての候補を列挙し、(double) または (double,double) のものを優先選択
            MethodInfo? methodInfo = null;
            string foundName = "<none>";

            var methods = t.GetMethods(flags)
                           .Where(m => candidateNames.Contains(m.Name))
                           .ToArray();

            // 優先順位: (double) → (double,double) → それ以外の最初
            methodInfo = methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 1 && ps[0].ParameterType == typeof(double);
            })
            ?? methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 2 && ps.All(p => p.ParameterType == typeof(double));
            })
            ?? methods.FirstOrDefault();

            if (methodInfo == null)
                return null;

            foundName = methodInfo.Name;

            // 呼び出し
            object? ret;
            try
            {
                ret = methodInfo.GetParameters().Length switch
                {
                    1 => methodInfo.Invoke(pileSection, [axialN]),
                    2 => methodInfo.Invoke(pileSection, [axialN, 1.0]),
                    _ => methodInfo.Invoke(pileSection, [axialN])
                };
            }
            catch (Exception ex)
            {
                return null;
            }

            if (ret == null) return null;

            var rtype = ret.GetType();

            // 1) Tuple-like: Item1/Item2 (ValueTupleはフィールド、Tupleはプロパティ)
            var itm1Prop = rtype.GetProperty("Item1");
            var itm2Prop = rtype.GetProperty("Item2");
            var itm1Field = rtype.GetField("Item1");
            var itm2Field = rtype.GetField("Item2");

            bool hasProperty = itm1Prop != null && itm2Prop != null;
            bool hasField = itm1Field != null && itm2Field != null;


            if (hasProperty || hasField)
            {
                try
                {
                    object? v1, v2;
                    if (hasField)
                    {
                        // ValueTuple: フィールドとして取得
                        v1 = itm1Field!.GetValue(ret);
                        v2 = itm2Field!.GetValue(ret);
                    }
                    else
                    {
                        // Tuple: プロパティとして取得
                        v1 = itm1Prop!.GetValue(ret);
                        v2 = itm2Prop!.GetValue(ret);
                    }

                    // List<double> を直接処理（最優先）
                    if (v1 is List<double> concreteList1 && v2 is List<double> concreteList2)
                    {
                        if (concreteList1.Count >= 2 && concreteList1.Count == concreteList2.Count)
                        {
                            return (concreteList1, concreteList2);
                        }
                    }

                    // IList<double> を試す
                    if (v1 is IList<double> list1 && v2 is IList<double> list2)
                    {
                        if (list1.Count >= 2 && list1.Count == list2.Count)
                        {
                            return (list1.ToList(), list2.ToList());
                        }
                    }

                    // IEnumerable<double> を試す
                    if (v1 is IEnumerable<double> enum1 && v2 is IEnumerable<double> enum2)
                    {
                        var phis2 = enum1.ToList();
                        var ms2 = enum2.ToList();
                        if (phis2.Count >= 2 && phis2.Count == ms2.Count)
                        {
                            return (phis2, ms2);
                        }
                    }

                    // IEnumerable経由のフォールバック
                    if (v1 is System.Collections.IEnumerable e1 && v2 is System.Collections.IEnumerable e2)
                    {
                        var phis = new List<double>();
                        var ms = new List<double>();
                        foreach (var item in e1)
                        {
                            if (item is double d) phis.Add(d);
                            else if (item is IConvertible c) phis.Add(Convert.ToDouble(c));
                        }
                        foreach (var item in e2)
                        {
                            if (item is double d) ms.Add(d);
                            else if (item is IConvertible c) ms.Add(Convert.ToDouble(c));
                        }
                        if (phis.Count >= 2 && phis.Count == ms.Count)
                        {
                            return (phis, ms);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtractMPhiCurve] List プロパティ抽出失敗: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // 2) Points プロパティ（MomentCurvatureCurve等）
            var pointsProp = rtype.GetProperty("Points") ?? rtype.GetProperty("points");
            if (pointsProp != null)
            {
                try
                {
                    if (pointsProp.GetValue(ret) is System.Collections.IEnumerable ptsObj)
                    {
                        var phis = new List<double>();
                        var ms = new List<double>();
                        foreach (var p in ptsObj)
                        {
                            if (p == null) continue;
                            var ptType = p.GetType();
                            var propPhi = ptType.GetProperty("Phi") ?? ptType.GetProperty("phi") ?? ptType.GetProperty("Item1");
                            var propMoment = ptType.GetProperty("Moment") ?? ptType.GetProperty("moment") ?? ptType.GetProperty("Item2");
                            if (propPhi?.GetValue(p) is IConvertible pvc && propMoment?.GetValue(p) is IConvertible mvc)
                            {
                                phis.Add(Convert.ToDouble(pvc));
                                ms.Add(Convert.ToDouble(mvc));
                                continue;
                            }
                            var f1 = ptType.GetField("Item1")?.GetValue(p);
                            var f2 = ptType.GetField("Item2")?.GetValue(p);
                            if (f1 is IConvertible fc1 && f2 is IConvertible fc2)
                            {
                                phis.Add(Convert.ToDouble(fc1));
                                ms.Add(Convert.ToDouble(fc2));
                                continue;
                            }
                        }
                        if (phis.Count >= 2 && phis.Count == ms.Count) return (phis, ms);
                    }
                }
                catch { /* fallthrough */ }
            }

            // 3) 列挙 of ペア
            if (ret is System.Collections.IEnumerable retSeq)
            {
                try
                {
                    var phis = new List<double>();
                    var ms = new List<double>();
                    foreach (var item in retSeq)
                    {
                        if (item == null) continue;
                        var itType = item.GetType();
                        var f1 = itType.GetProperty("Item1")?.GetValue(item) ?? itType.GetField("Item1")?.GetValue(item);
                        var f2 = itType.GetProperty("Item2")?.GetValue(item) ?? itType.GetField("Item2")?.GetValue(item);
                        if (f1 is IConvertible c1 && f2 is IConvertible c2)
                        {
                            phis.Add(Convert.ToDouble(c1));
                            ms.Add(Convert.ToDouble(c2));
                            continue;
                        }
                        var propPhi = itType.GetProperty("Phi") ?? itType.GetProperty("phi") ?? itType.GetProperty("X");
                        var propMoment = itType.GetProperty("Moment") ?? itType.GetProperty("moment") ?? itType.GetProperty("Y");
                        if (propPhi?.GetValue(item) is IConvertible pvc && propMoment?.GetValue(item) is IConvertible mvc)
                        {
                            phis.Add(Convert.ToDouble(pvc));
                            ms.Add(Convert.ToDouble(mvc));
                            continue;
                        }
                    }
                    if (phis.Count >= 2 && phis.Count == ms.Count) return (phis, ms);
                }
                catch { /* fallthrough */ }
            }

            return null;
        }

        // 荷重ケース用の M-θ（非線形ON/OFFに応じて線形Kを必ず設定、曲線はON時のみ使用）
        private void SetupNonlinearMThetaForLoadCase(AnaModel model, LoadCase loadCase)
        {
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0) return;

            const double KMin = 1e-6;   // 特異化回避用の下限
            const double KBig = 1e10;   // 剛体相当（杭断面 4EI/L ≈ 1e8 に対して十分大きい値）

            foreach (var spring in model.RotationalSprings)
            {
                // v28: 各ケースの setup 時にクラック履歴をリセット (ケース間独立)
                spring.ResetCrackState();

                int pb = (spring.PileBodyNo is int v && v > 0) ? v : 1;
                if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

                // 回転バネの名前から杭番号を抽出して軸力を取得
                // 名前形式: "RθXY-{pileNo}"
                // L1/L2 地震ケースでは「地震時軸力」(GetSeismicAxialForce) を使うべき。
                // pile.AxialForce は重力のみのベース軸力で、L2 の鉛直地震成分や上部構造慣性力
                // による軸力増分が反映されないため、M-θ 曲線が誤った (低めの) N で構築される。
                // GraphViewModel の popup と同じ優先順位に揃える:
                //   GetSeismicAxialForce (case/level 別) → model.GetAxialForce (重力ベースフォールバック)
                double axialN = 0.0;
                if (spring.Name != null && spring.Name.Contains('-'))
                {
                    var parts = spring.Name.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                    {
                        var pile = InputModel.PileLayoutItems?.FirstOrDefault(p => p.No == pileNo);
                        if (pile != null)
                        {
                            try
                            {
                                double nSeis = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                if (double.IsFinite(nSeis) && nSeis != 0.0)
                                    axialN = nSeis;
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "[SetupMTheta] GetSeismicAxialForce(loadCaseNo={No}, level={Lv}) failed, fallback to gravity baseline.",
                                    loadCase.No, loadCase.Level);
                            }
                            if (axialN == 0.0)
                            {
                                // E3b: case-local AxialForce 経由 (主モデルでは pile.AxialForce と同値)
                                axialN = model.GetAxialForce(pile); // kN
                            }
                        }
                    }
                }

                var pileBody = InputModel.PileBodies[pb - 1];
                var def = pileBody.GetMThetaRelationship(axialN);

                // System.Diagnostics.Debug.WriteLine(
                //     $"[SetupMTheta] {spring.Name}: IsPileNonLinear={loadCase.IsPileNonLinear}, " +
                //     $"def.Mode={def.Mode}, axialN={axialN:F1}kN");

                // 非線形OFF: つねに剛体相当
                if (!loadCase.IsPileNonLinear)
                {
                    spring.Mode = RotationalSpringMode.CombinedXY;
                    spring.CurveXY = null;
                    spring.KthetaXY = KBig;
                    spring.McrXY = null; // Mode 切替は非線形ケースでのみ有効
                    spring.LastSetupReason = $"Rigid(IsPileNonLinear=false, axialN={axialN:F0}kN)";
                    continue;
                }

                // 非線形ON
                switch (def.Mode)
                {
                    case PileHeadRotationMode.Rigid:
                        // 非線形ONでも「剛」は剛のまま扱う
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = null;
                        spring.KthetaXY = KBig;
                        spring.LastSetupReason = $"Rigid(def.Mode=Rigid, PileTop='{pileBody.PileTopType}', PileBody='{pileBody.PileBodyType}', axialN={axialN:F0}kN)";
                        break;

                    case PileHeadRotationMode.CombinedXY:
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = def.CurveXY;
                        spring.LastSetupReason = $"CombinedXY({(def.CurveXY != null ? def.CurveXY.Points.Count + "pts" : "null")}, Mcr={(def.McrXY?.ToString("F0") ?? "null")}, axialN={axialN:F0}kN)";
                        // v28: Mcr 同期 Mode 切替 (ヒステリシス付き) 用。場所打ち RC 杭のみ非 null。
                        spring.McrXY = def.McrXY;
                        // 状態はケース開始時にリセットするため念のためクリア
                        spring.ResetCrackState();
                        // sec 側の代替として KThetaXY を設定（優先順位: def.KThetaXY → Mcr 有りなら KBig → 曲線の初期接線 → KMin）
                        if (def.KthetaXY.HasValue && def.KthetaXY.Value > 0.0)
                        {
                            spring.KthetaXY = def.KthetaXY;
                        }
                        else if (def.McrXY.HasValue)
                        {
                            // Mcr 同期 Mode 切替が有効 → 未クラック時は剛 (KBig) 扱いで開始
                            spring.KthetaXY = KBig;
                        }
                        else if (spring.CurveXY != null)
                        {
                            double k0 = Math.Max(spring.CurveXY.EvaluateTangent(1e-6), 0.0);
                            spring.KthetaXY = Math.Max(k0, KMin);
                        }
                        else
                        {
                            spring.KthetaXY = KMin;
                        }
                        // System.Diagnostics.Debug.WriteLine(
                        //     $"[SetupMTheta] {spring.Name}: → CombinedXY, CurveXY={(spring.CurveXY != null ? $"{spring.CurveXY.Points.Count}pts" : "null")}, " +
                        //     $"KthetaXY={spring.KthetaXY:E3}");
                        break;

                    case PileHeadRotationMode.Separate:
                        spring.Mode = RotationalSpringMode.SingleDof;
                        if (spring.Dof == RotationalDof.Rx)
                        {
                            spring.Curve = def.CurveX;
                            if (def.Kx.HasValue && def.Kx.Value > 0.0)
                            {
                                spring.Ktheta = def.Kx;
                            }
                            else if (spring.Curve != null)
                            {
                                double k0 = Math.Max(spring.Curve.EvaluateTangent(1e-6), 0.0);
                                spring.Ktheta = Math.Max(k0, KMin);
                            }
                            else
                            {
                                spring.Ktheta = KMin;
                            }
                        }
                        else if (spring.Dof == RotationalDof.Ry)
                        {
                            spring.Curve = def.CurveY;
                            if (def.Ky.HasValue && def.Ky.Value > 0.0)
                            {
                                spring.Ktheta = def.Ky;
                            }
                            else if (spring.Curve != null)
                            {
                                double k0 = Math.Max(spring.Curve.EvaluateTangent(1e-6), 0.0);
                                spring.Ktheta = Math.Max(k0, KMin);
                            }
                            else
                            {
                                spring.Ktheta = KMin;
                            }
                        }
                        break;
                }

                if (spring.CurveXY != null && spring.CurveXY.Points.Count > 0)
                {
                    var pts = spring.CurveXY.Points;
                }
            }
        }

        /// <summary>
        /// Y 案: caseModel (DeepCopy 済) のばね M-θ 構成を、永続側 targetModel の
        /// 同インデックスばねの CaseMThetaSnapshots 辞書へ書き戻す。
        /// SetupNonlinearMThetaForLoadCase 直後に呼ぶ。
        /// 同じ (LoadCase, LoadCombination, IsLiquefaction) で再試行が走った場合は上書き。
        /// </summary>
        private static void SnapshotMThetaToOriginalSprings(
            FEM.AnaModel caseModel,
            FEM.AnaModel targetModel,
            Models.InputData.LoadCase loadCase,
            Models.InputData.LoadCombination loadCombination,
            bool isLiquefaction)
        {
            var src = caseModel?.RotationalSprings;
            var dst = targetModel?.RotationalSprings;
            if (src == null || dst == null) return;
            int n = Math.Min(src.Count, dst.Count);
            string key = FEM.RotationalSpring.MakeCaseKey(
                loadCase?.LoadName, loadCombination?.No ?? 0, isLiquefaction);
            for (int i = 0; i < n; i++)
            {
                var s = src[i];
                var d = dst[i];
                d.CaseMThetaSnapshots[key] = new FEM.MThetaCaseSnapshot
                {
                    Mode = s.Mode,
                    CurveXY = s.CurveXY,
                    Curve = s.Curve,
                    KthetaXY = s.KthetaXY,
                    Ktheta = s.Ktheta,
                    McrXY = s.McrXY,
                    SetupReason = s.LastSetupReason ?? "",
                };
            }
        }

        // v21 Phase 3 prep: ばね剛性 min/max はインスタンスフィールドを廃し、
        // FindK / PrepareKmat の戻り値（out パラメータ）で局所管理する。

        public async Task RunAsync(CancellationToken token, IProgress<Models.AnalysisProgress>? progress = null, bool additive = false)
        {
            // additive=true: 既存結果を保持し、未計算ケースのみを実行 (段階追加再解析)
            // additive=false: 通常実行 (既存結果を明示クリアして全ケース計算)
            await Task.Yield();

            // === 追加実行モード用: 既存ケースキー集合 ===
            // RunAsync 開始時に 1 回スナップショットを取り、ループ内で skip 判定に使用。
            // 並列ループ実行中は targetModel.AnalysisStepResults へ append が走るため
            // 並列実行内で再評価しない (ロック保護下のスナップショット)。
            HashSet<FEM.AnalysisRunSnapshot.CaseKey> existingKeys = new();

            var preTargetModel = TryGetTargetAnaModel();
            if (preTargetModel != null)
            {
                lock (_caseMergeLock)
                {
                    if (additive && preTargetModel.LastRunConfig != null)
                    {
                        existingKeys = preTargetModel.LastRunConfig.ExecutedCaseKeys.ToHashSet();
                    }
                    else if (additive && preTargetModel.AnalysisStepResults?.Count > 0)
                    {
                        // 防御: LastRunConfig が null だが結果がある旧データの場合、結果から復元
                        existingKeys = preTargetModel.AnalysisStepResults
                            .Select(r => new FEM.AnalysisRunSnapshot.CaseKey(
                                r.LoadCase.LoadName, r.LoadCombination.Name, r.IsLiquefaction))
                            .ToHashSet();
                    }
                }
            }

            if (additive)
            {
                await AddLogAsync($"=== 追加実行: 既存 {existingKeys.Count} ケースを保持し、未計算分のみ計算します ===");
                // _stepSummaries.Clear() は呼ばない (既存ログを保持)
                // 既存結果のステップ数を baseline として記録 (プログレスバー分母から除外)
                _additiveBaselineSteps = preTargetModel?.AnalysisStepResults?.Count ?? 0;
            }
            else
            {
                // 既に「計算モデル作成開始」が出ているので、ここでは「計算開始」を追記する
                await AddLogAsync("解析計算処理開始");
                // v29: ステップサマリーをリセット (前回解析の結果をクリア)
                _stepSummaries.Clear();
                // 結果と前回設定を明示クリア (新規実行時)
                if (preTargetModel != null)
                {
                    lock (_caseMergeLock)
                    {
                        preTargetModel.ClearAllAnalysisResults();
                    }
                }
                // baseline をリセット (通常実行)
                _additiveBaselineSteps = 0;
            }
            OnPropertyChanged(nameof(EffectiveProgressTotal));
            OnPropertyChanged(nameof(EffectiveProgress));
            OnPropertyChanged(nameof(ProgressText));

            // v29: 経過/残り時間表示用に開始時刻記録 + 1 秒タイマーで定期更新
            _analysisStartUtc = DateTime.UtcNow;
            StartElapsedTimer();

            // 並列モニタ (案 B): 新解析のたびに Active/Completed をリセット
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ActiveCases.Clear();
                ActiveCasesCount = 0;
                CompletedCaseCount = 0;
                // load case 単位 (= CompletedCaseCount と同じ単位) で完了率を表示
                TotalPlannedCaseCount = TotalLoadCaseCount;
                // setter 内で PendingCaseCount PropertyChanged も発火するが念のため再度発火
                OnPropertyChanged(nameof(PendingCaseCount));
            });

            // MDOP>=2 ならモニタを表示
            bool showMonitor = MaxCaseDegreeOfParallelism > 1;
            if (showMonitor)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RequestShowParallelMonitor?.Invoke());
            }

            // 進捗報告用の開始時刻を記録
            var startTime = DateTime.Now;

            // 計算対象モデルを決定（編集用があればそれ、なければ本体）
            var targetModel = AnaModels.Count > 1 ? AnaModels[1] : AnaModels[0];
            if (targetModel == null)
            {
                await AddLogAsync("計算モデルが存在しません。");
                return;
            }

            targetModel.SetSlaveNodes(); // 剛体連結のスレーブ節点のセット

            // E3c-3 (2026-04-23): ケース並列化対応。calcNo を StrongBox に包み Interlocked で
            // atomic にインクリメントする。MDOP=1 (逐次) でも同じ経路 (overhead は無視できる)。
            var calcNoBox = new System.Runtime.CompilerServices.StrongBox<int>(0);

            // v19: 解析開始時に再試行による追加ステップ数をリセット
            _bisectionExtraSteps = 0;
            NotifyProgressPropertiesChanged();
            OnPropertyChanged(nameof(TotalLoadCaseCount));

            // 初期進捗を報告
            progress?.Report(new Models.AnalysisProgress
            {
                Percentage = 0,
                CurrentStep = "解析計算を開始しています...",
                CurrentStepNumber = 0,
                TotalSteps = TotalCalculationCount,
                StartTime = startTime
            });

            const double alpha = 1e-6;

            // v21 Phase 3 prep: 将来のケース並列化に向けた設計メモ（このループを並列実行する場合の必要要件）
            //
            // ■ 現状はスレッド安全ではない以下の共有状態に注意:
            //   - InputModel.PileLayoutItems[].AxialForce   → InitializeAxialForces / UpdateAxialForceFromAnalysis が書換
            //   - InputModel.PileLayoutItems[].SoilNodes[].CumulativeForcedDisp  → UpdateSoilDisp が書換
            //   - InputModel.ElementDivision.DoatsuGoryokuBane.Items[].*SoilNode.CumulativeForcedDisp  → 同上
            //   - InputModel.ElementDivision.SoilPiles[].HorizontalSoilReactions  → PrepareKmat が評価（読み取り中に他 worker が AxialForce 経由で値を変える可能性）
            //   - targetModel.AnalysisStepResults / Nodes[].NodeResults / Beams[].BeamResults / HorizontalSoilSprings[].HorizontalSpringResults / RotationalSprings[].RotationalSpringResults  → 結果 append
            //
            // ■ 並列化手順（Phase 3.1 本実装時）:
            //   (a) 事前に cases = [(lc, combo, isLiq), ...] を全列挙
            //   (b) 各 case ごとに InputModel+AnaModel の「スレッド固有コピー」を構築
            //       （PileLayoutItem / DoatsuGoryokuBane / SoilPiles の書換対象を clone し、
            //        AnaModel.DeepCopy 済みのノードへ参照 fixup）
            //   (c) Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = MaxCaseDegreeOfParallelism }, ...)
            //   (d) 完了後、key=(lc.No, combo.No, isLiq, step) で AnalysisStepResults / 各要素 Results を
            //       決定的順序でマージ（乱序混入を防ぐ）
            //   (e) MathNet Control.MaxDegreeOfParallelism を並列実行中は 1 にクランプ
            //   (f) AddLogAsync / CurrentProgress / _bisectionExtraSteps を Interlocked or lock で保護

            // E3c-3 (2026-04-23): ケース並列化中は MathNet 内部並列度を 1 に clamp。
            // 並列ケース × 並列 MathNet の掛け算でスレッド過剰を防ぐ。MDOP=1 (逐次) では clamp 不要。
            int _caseMDOP = Math.Max(1, MaxCaseDegreeOfParallelism);
            int _origMathNetMDOP = MathNet.Numerics.Control.MaxDegreeOfParallelism;
            // 元の MKL/OMP 環境変数を退避 (try/finally で復元)
            string? _origMklNumThreads = Environment.GetEnvironmentVariable("MKL_NUM_THREADS");
            string? _origOmpNumThreads = Environment.GetEnvironmentVariable("OMP_NUM_THREADS");
            if (_caseMDOP > 1)
            {
                MathNet.Numerics.Control.MaxDegreeOfParallelism = 1;
                // hang 対策 (2026-04-26): MKL/OMP ネイティブ層の並列度を強制 1 に。
                // MathNet.Numerics.Control の clamp は managed 層のみで、MKL ネイティブが
                // 内部スレッドプールを別途持つ場合 oversubscription が起き ThreadPool starvation
                // → hang の一因。環境変数で強制クランプしておく。
                Environment.SetEnvironmentVariable("MKL_NUM_THREADS", "1");
                Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");
            }

            // E3c-3-enable: MDOP > 1 のとき、ケースを Task.Run で並行実行し SemaphoreSlim で
            // 同時実行数を _caseMDOP に制限。MDOP=1 では null のまま、従来通り逐次実行。
            var _caseTasks = new List<System.Threading.Tasks.Task>();
            System.Threading.SemaphoreSlim? _caseSemaphore = _caseMDOP > 1 ? new System.Threading.SemaphoreSlim(_caseMDOP) : null;

            try
            {
            foreach (var loadCaseItem in InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
            {
                LoadCase loadCase = loadCaseItem;
                int iLC = loadCaseItem.No - 1;
                int level = loadCaseItem.Level;

                // 荷重がゼロの場合はスキップ
                if (loadCase.UpperMassForce == 0 && loadCase.FoundationMassForce == 0)
                {
                    await AddLogAsync($"レベル{level}-{iLC + 1}: 荷重がゼロのためスキップ");
                    continue;
                }

                foreach (var loadCombination in InputModel.LoadCasesInput.AllLoadCombinations)
                {
                    int iLCOM = loadCombination.No - 1;

                    IEnumerable<bool> liquefactionCases = LiquefactionOption switch
                    {
                        LiquefactionOptionType.Both => [true, false],
                        LiquefactionOptionType.Yes => [true],
                        LiquefactionOptionType.None => [false],
                        _ => new[] { false }
                    };

                    foreach (bool isLiquefaction in liquefactionCases)
                    {
                        // 追加実行モード: 既存結果に同じキーがあるケースはスキップ
                        if (additive)
                        {
                            var caseKey = new FEM.AnalysisRunSnapshot.CaseKey(
                                loadCase.LoadName, loadCombination.Name, isLiquefaction);
                            if (existingKeys.Contains(caseKey))
                            {
                                await AddLogAsync($"[skip] {BuildCaseTag(level, iLC, iLCOM, isLiquefaction)} は既存結果あり (追加実行モード)");
                                continue;
                            }
                        }

                        // E3c-3-enable: ケース 1 件分の body を local function に wrap。
                        // MDOP=1 ではこのまま直接 await で呼出、MDOP>1 では Task.Run に投げて並行実行。
                        // foreach 変数 (loadCase, loadCombination, isLiquefaction, iLC, iLCOM, level) は
                        // C# 5.0 以降の仕様により各反復で fresh な変数として cap される。
                        async System.Threading.Tasks.Task RunThisCaseAsync()
                        {
                        // E3c-3 tune (2026-04-23): inner Task.Run ネスト回避用ヘルパー。
                        // MDOP=1 (逐次): 従来通り Task.Run に投げて UI をブロックしない。
                        // MDOP>1 (並列): 既に外側 Task.Run 内 (ThreadPool worker) で実行中なので
                        //   同期実行で十分。inner Task.Run を挟むと ThreadPool 消費量が
                        //   MDOP × (NR 反復数) 倍に膨らみ starvation を招く。
                        async System.Threading.Tasks.Task RunWork(System.Action work)
                        {
                            if (_caseMDOP > 1)
                            {
                                work();
                            }
                            else
                            {
                                await System.Threading.Tasks.Task.Run(work, token);
                            }
                        }

                        // E2 (2026-04-23): 並列化後のログ混在対策。反復/収束/プロファイルログの先頭に付与
                        string caseTag = BuildCaseTag(level, iLC, iLCOM, isLiquefaction);

                        // 並列モニタ (案 B, 2026-04-24): ケース開始時に Active リストへ追加。
                        // TotalSteps は nStep 確定後 (後続の baseNStep 計算後) に更新される。
                        // BeginInvoke: ワーカースレッドをブロックしない (Invoke は UI 処理完了まで待機)
                        var monitorItem = new CaseMonitorItem(caseTag, 0);
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            ActiveCases.Add(monitorItem);
                            ActiveCasesCount = ActiveCases.Count;
                            OnPropertyChanged(nameof(PendingCaseCount));
                        });
                        // 診断ログ (hang 調査用、2026-04-26): ケース開始時刻と thread id を記録。
                        // MDOP > 1 時のみ。完了せず固まった場合、最後に開始したケースが特定可能。
                        var _diagCaseStartUtc = DateTime.UtcNow;
                        if (_caseMDOP > 1)
                        {
                            int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                            await AddLogAsync($"{caseTag} 🟢 開始 (Tid={tid}, ActiveCases={ActiveCasesCount})");
                        }
                        try
                        {

                        // E3c-2 (2026-04-23): ケース固有モデル = targetModel の DeepCopy。
                        // E3c-3 (2026-04-23): snapshot + DeepCopy は targetModel の state を同時に
                        // 参照するため atomic である必要あり。逐次モードでは lock は no-op。
                        int caseSnapAnaStepResults;
                        int[] caseSnapNodeResultCounts, caseSnapBeamResultCounts, caseSnapHSpringResultCounts;
                        int[]? caseSnapRotSpringResultCounts = null;
                        AnaModel caseModel;
                        lock (_caseMergeLock)
                        {
                            caseSnapAnaStepResults = targetModel.AnalysisStepResults?.Count ?? 0;
                            caseSnapNodeResultCounts = targetModel.Nodes.Select(n => n.NodeResults.Count).ToArray();
                            caseSnapBeamResultCounts = targetModel.Beams.Select(b => b.BeamResults.Count).ToArray();
                            caseSnapHSpringResultCounts = targetModel.HorizontalSoilSprings.Select(s => s.HorizontalSpringResults.Count).ToArray();
                            if (targetModel.RotationalSprings != null)
                                caseSnapRotSpringResultCounts = targetModel.RotationalSprings.Select(rs => rs.RotationalSpringResults.Count).ToArray();

                            caseModel = targetModel.DeepCopy();
                        }

                        // ── 軸剛性 0 (Uz 解放): 引張定着筋なし半剛接合 (キャプテン/F.T.Pile/キャプリング) で
                        //    入力軸力が引張となる杭について、case-local モデルの杭頭 Uz master-slave を
                        //    解放する。「軸剛性 0、曲げ剛性 0」(Mu=0 と併用) でピン接合的挙動を実現する。
                        //    判定は入力軸力ベース (UseAnalysisAxialForce=true でも初期推定値で固定)。
                        //    詳細は help.html「引張軸力時の杭頭軸剛性解放」を参照。
                        var axialReleasePileNos = GetPileNosForAxialReleaseInCase(loadCase);
                        if (axialReleasePileNos.Count > 0)
                        {
                            try
                            {
                                caseModel.ApplyAxialReleaseAtPileHeads(axialReleasePileNos);
                                await AddLogAsync(
                                    $"  {caseTag} 軸剛性 0 適用: 杭No.[{string.Join(",", axialReleasePileNos)}] " +
                                    "(引張定着筋なし半剛接合 × 引張軸力 → 杭頭 Uz 解放)");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "[ApplyAxialReleaseAtPileHeads] 失敗: {CaseTag}", caseTag);
                                await AddLogAsync($"  {caseTag} ⚠ 軸剛性 0 適用に失敗: {ex.Message}");
                            }
                        }

                        // v20: 荷重方向の事前検出 — counter-loading (逆方向組合せ) を検出し、
                        // 最初から小さな荷重ステップで実行することで失敗試行のムダを回避
                        //
                        // v28 (2026-04-23): Approach I で杭頭 Ry リミットサイクルが根本解決したため、
                        // 分類を βU × βL の符号のみに簡素化、CounterLoading 時の nStep を 16 → 12 に緩和。
                        // 物理的根拠: 逆方向組合せは S 字曲げ (杭頭と杭体下部で逆符号の塑性ヒンジが
                        // 同時形成) が発生し、Newton 方向が接線不連続で振動しやすい。その他の難しさは
                        // Approach I で除去済み、または早期適応検出 (v26 案 B) が実測ベースで救済。
                        //
                        // 仕組み:
                        //   - Forward (βU × βL ≥ 0): configured nStep で開始、retry 最大 3
                        //   - CounterLoading (βU × βL < 0): 基本 ×2 (min 12) で開始、retry 最大 2
                        int configuredNStep = (!loadCase.IsSoilNonLinear && !loadCase.IsPileNonLinear) ? 1 :
                            loadCase.Level == 1 ? Level1CalculationStepsCount :
                            loadCase.Level == 2 ? Level2CalculationStepsCount :
                            1;
                        var loadDirection = ClassifyLoadCombinationDirection(loadCase, loadCombination, isLiquefaction);
                        int baseNStep = configuredNStep;
                        int MAX_STEP_BISECTIONS = 3;
                        if (loadCase.IsSoilNonLinear || loadCase.IsPileNonLinear)
                        {
                            // 非線形ケースのみ事前検出を適用
                            switch (loadDirection)
                            {
                                case LoadCombinationDirection.CounterLoading:
                                    baseNStep = Math.Max(configuredNStep * 2, 12);
                                    MAX_STEP_BISECTIONS = 2;
                                    break;
                                case LoadCombinationDirection.Forward:
                                default:
                                    // 基本 nStep のまま (3 回 retry)
                                    break;
                            }
                            if (baseNStep != configuredNStep)
                            {
                                // v22: Phase 1 事前検出による初期 nStep 増加分も TotalCalculationCount に
                                // 反映する。旧実装は再試行時のみ _bisectionExtraSteps を加算していたため、
                                // 事前検出で初期から大きな nStep を選んだ場合、進捗カウンタが超過表示
                                // （例: 207/180）になっていた。
                                System.Threading.Interlocked.Add(ref _bisectionExtraSteps, baseNStep - configuredNStep);
                                NotifyProgressPropertiesChanged();
                                string directionLabel = loadDirection == LoadCombinationDirection.CounterLoading ? "逆方向組合せ" : "順方向組合せ";
                                await AddLogAsync($"  🔎 荷重方向事前検出: {directionLabel} (αL={loadCombination.Alpha1:N2}, βU={loadCombination.Beta1:N2}, βL={loadCombination.Beta2:N2}) → 初期 nStep={baseNStep} (設定値 {configuredNStep} の代わり, 総ステップ数: {TotalCalculationCount})");
                            }
                        }
                        int nStep = baseNStep;
                        int bisectionAttempt = 0;
                        bool caseConverged = false;

                        // 並列モニタ: 初期 nStep 確定後に TotalSteps を更新 (BeginInvoke で fire-and-forget)
                        {
                            int initialNStep = nStep;
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => monitorItem.TotalSteps = initialNStep);
                        }

                        // v28 (2026-04-23) 改善ゲート: 前 attempt の平均反復数を保持し、
                        // retry 後にほとんど改善しない場合 (細分化が無効な構造的ラインサーチ制約)
                        // は以降の retry を抑制して無駄な計算を避ける。
                        double prevAttemptAvgIter = double.PositiveInfinity;
                        bool retryGateDisabled = false;

                        while (true)
                        {
                            // 再試行時の巻き戻し用に、結果リストのサイズをスナップショット
                            int snapAnaStepResults = caseModel.AnalysisStepResults?.Count ?? 0;
                            var snapNodeResults = new int[caseModel.Nodes.Count];
                            for (int i_ = 0; i_ < caseModel.Nodes.Count; i_++)
                                snapNodeResults[i_] = caseModel.Nodes[i_].NodeResults.Count;
                            var snapBeamResults = new int[caseModel.Beams.Count];
                            for (int i_ = 0; i_ < caseModel.Beams.Count; i_++)
                                snapBeamResults[i_] = caseModel.Beams[i_].BeamResults.Count;
                            var snapHSpringResults = new int[caseModel.HorizontalSoilSprings.Count];
                            for (int i_ = 0; i_ < caseModel.HorizontalSoilSprings.Count; i_++)
                                snapHSpringResults[i_] = caseModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count;
                            int[]? snapRotSpringResults = null;
                            if (caseModel.RotationalSprings != null)
                            {
                                snapRotSpringResults = new int[caseModel.RotationalSprings.Count];
                                for (int i_ = 0; i_ < caseModel.RotationalSprings.Count; i_++)
                                    snapRotSpringResults[i_] = caseModel.RotationalSprings[i_].RotationalSpringResults.Count;
                            }

                            caseModel.InitializeStates();

                            // 荷重ケース固有の剛体スレーブ割当を適用（回転ばねの有効/無効を切替）
                            ApplyPileHeadRigidBindingForLoadCase(caseModel, loadCase);

                            // 杭非線形ONのときだけ M–φ/M–θ をセット
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiFromPileSectionForLoadCase(caseModel, loadCase);
                            }
                            // M–θ は常にセット（非線形OFFは剛 KThetaXY=KRigid）
                            SetupNonlinearMThetaForLoadCase(caseModel, loadCase);
                            // Y 案: caseModel のばね構成を targetModel のばねへスナップショット書き戻し。
                            // GraphViewModel / AnalysisResultTableService がケース別構成を可視化できるようにする。
                            SnapshotMThetaToOriginalSprings(caseModel, targetModel, loadCase, loadCombination, isLiquefaction);

                            // Phase 3: ロード増分の denominator を別変数で管理。
                            // ・nStep: for-loop 連番上限 (cut-back で「残りステップ分」だけ増える)
                            // ・effectiveDenom: SetVectorDF / InitializeSoilDisp に渡す増分分母 (cut-back で ×M)
                            // フラグ off ではどちらも nStep のまま動作 (= 従来挙動)。
                            int effectiveDenom = nStep;
                            SetVectorDF(caseModel, loadCase, loadCombination, level, iLC, effectiveDenom);
                            caseModel.MapOnVectorDF();
                            InitializeSoilDisplacementIncrement(caseModel, loadCase, loadCombination, level, isLiquefaction, effectiveDenom);

                            if (bisectionAttempt > 0)
                                await AddLogAsync($"  ♻ ケース再試行 ({bisectionAttempt}/{MAX_STEP_BISECTIONS}): ステップ数を {baseNStep} → {nStep} に増やして再計算 (以降の総ステップ数: {TotalCalculationCount})");

                            // このアテンプト内で未収束ステップが発生したか
                            bool caseFailedThisAttempt = false;

                            // v20 Phase 2: 物理的未収束と判定された場合、再試行ループから直接抜ける
                            bool physicallyUnconvergeable = false;

                            // v19: このアテンプト内で実際に実行されたステップ数（早期脱出時に使用）
                            int stepsExecutedInAttempt = 0;

                            // v15: 変位予測器用の変数（前ステップの変位増分を記録）
                            // v23 (C): 2 ステップ外挿のため前々ステップの増分も保持。
                            // 3 ステップ目以降は du_predict = du_prev + 0.5×(du_prev − du_prev_prev) で
                            // 加速度項（変位の曲率）を考慮した 2 次予測に切替。1 反復目の残差を 1 桁下げる効果。
                            MathNet.Numerics.LinearAlgebra.Vector<double>? prevStepDispIncrement = null;
                            MathNet.Numerics.LinearAlgebra.Vector<double>? prevPrevStepDispIncrement = null;

                            // v29 (2026-05-05): 退化トレンド検出 — 各ステップの反復数を履歴に蓄積し、
                            // 「直近 3 ステップで反復数が単調増加」かつ「最新 ≥ 60 反復」の複合条件で発火。
                            // v26 (案 B) の「最初 2 ステップの平均反復数 ≥ 18」は「収束しているが反復多い」
                            // を退化と誤判定する事案 (例題 9 + 基礎梁) があったため、複合条件で誤検知を避ける。
                            var stepIterHistory = new List<int>();

                            // Phase 3 (2026-05-06): step-level cut-back retry のための状態変数。
                            // ・lastSuccessCheckpoint: 直近成功ステップ終了時点の状態。null = 未捕捉。
                            // ・cutbackCount: この attempt 内で発動した cut-back 回数 (上限 MAX_CUTBACKS_PER_ATTEMPT)。
                            // フラグ off (`_useStepLevelCutback==false`) では capture せず、cut-back 経路も使用しない。
                            FEM.StepCheckpoint? lastSuccessCheckpoint = null;
                            int cutbackCount = 0;

                            // 引張定着筋なし半剛接合杭で引張軸力 NG をログ済みの杭番号 (1 ケース内重複抑制)。
                            // ローカル変数のため MDOP>1 並列でも安全。
                            var loggedNgTensionPileNos = new HashSet<int>();

                            for (int step = 0; step < nStep; step++)
                        {
                            await Task.Yield(); // ここでUIスレッドを解放
                            token.ThrowIfCancellationRequested();
                            _pauseEvent.Wait(token); // ここで一時停止を考慮

                            // 並列モニタ: 現ステップ / 総ステップ を更新 (BeginInvoke で UI をブロックしない)
                            {
                                int stepDisplay = step + 1;
                                int nStepDisplay = nStep;
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    monitorItem.CurrentStep = stepDisplay;
                                    monitorItem.TotalSteps = nStepDisplay;
                                });
                            }

                            // v15: ステップ開始時の変位を記録（予測器用）
                            var vectorDAtStepStart = caseModel.VectorD?.Clone();

                            // E3c-3: Interlocked で atomic にインクリメント (MDOP>1 対応)。
                            // curCalcNo は以降このステップ内で使用する (他スレッドが更新しても読み取りは安定)。
                            int curCalcNo = System.Threading.Interlocked.Increment(ref calcNoBox.Value);
                            // CurrentProgress 更新は WPF UI binding を触るため Dispatcher 経由で安全に
                            // (UI スレッド上なら即時実行、pool スレッドなら UI キューへ投げる)。
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => CurrentProgress = curCalcNo);

                            // 進捗を報告
                            progress?.Report(new Models.AnalysisProgress
                            {
                                Percentage = TotalCalculationCount > 0 ? (curCalcNo * 100.0 / TotalCalculationCount) : 0,
                                CurrentStep = $"レベル{level}-{iLC + 1}, {(isLiquefaction ? "液状化考慮" : "液状化非考慮")}, " +
                                             $"組合せ[{iLCOM + 1}], ステップ{step + 1}/{nStep}",
                                CurrentStepNumber = curCalcNo,
                                TotalSteps = TotalCalculationCount,
                                StartTime = startTime
                            });

                            string retryTag = bisectionAttempt > 0 ? $" 再試行{bisectionAttempt}/{MAX_STEP_BISECTIONS}" : "";
                            await AddLogAsync($"[{curCalcNo}/{TotalCalculationCount}{retryTag}]" + "荷重ケース：" + level + "-" + $"{iLC + 1}" + ", " + "液状化" + (isLiquefaction ? "考慮, " : "非考慮, ") +
                                $"[{iLCOM + 1}]" +
                                "αL:" + $"{loadCombination.Alpha1:N2}" +
                                ", βU:" + $"{loadCombination.Beta1:N2}" +
                                ", βL:" + $"{loadCombination.Beta2:N2}" +
                                ",　荷重ステップ" + (step + 1) + "/" + nStep +
                                (RelaxationFactor < 1.0 ? $", 緩和係数={RelaxationFactor:N2}" : ""));

                            // v15/v23: 予測ステップ（前ステップの変位増分があれば適用）
                            if (step > 0 && prevStepDispIncrement != null && caseModel.VectorD != null)
                            {
                                MathNet.Numerics.LinearAlgebra.Vector<double> predictorIncrement;
                                if (step > 1 && prevPrevStepDispIncrement != null)
                                {
                                    // v23 (C): 2 次外挿 u_{n+1} − u_n ≈ Δu_prev + 0.5×(Δu_prev − Δu_prev_prev)
                                    // 変位増分の変化率（加速度項）を取り込み、非線形が強いステップでも良い初期点になる。
                                    // 係数 0.5 は過剰外挿を避けるためのダンピング（全振幅 1.0 で共振する可能性あり）。
                                    var accel = prevStepDispIncrement - prevPrevStepDispIncrement;
                                    predictorIncrement = prevStepDispIncrement + 0.5 * accel;
                                }
                                else
                                {
                                    // 初回はデータ不足で 1 次予測（前ステップ増分の 80%）
                                    const double predictorFactor = 0.8;
                                    predictorIncrement = predictorFactor * prevStepDispIncrement;
                                }
                                caseModel.ApplyDispIncrement(predictorIncrement);

                                // 節点変位も更新（既存のラインサーチ用メソッドを流用）
                                UpdateNodeDisplacementsForLineSearch(caseModel, predictorIncrement);
                            }

                            caseModel.InitializeNormsqR_onNormsqFint();

                            // NaN診断: ステップ開始
                            // if (step == 0)
                            //     FEM.NaNDiagnostics.Begin();
                            // FEM.NaNDiagnostics.Log($"=== Step {step + 1}/{nStep}, LC={loadCase.LoadName}, Liq={isLiquefaction} ===");

                            int n_iteration = 1;

                            // v28 D: プロファイリング用 Stopwatch (ステップ局所、並列化時もワーカー内で安全)
                            var profStepTimer = System.Diagnostics.Stopwatch.StartNew();
                            long profFindKTicks = 0;
                            long profSolveTicks = 0;
                            long profLineSearchTicks = 0;
                            long profFindTTicks = 0;
                            int profFindKCalls = 0;
                            int profLineSearchCalls = 0;
                            int profLineSearchTrialsTotal = 0;

                            // v28 F-new: CsparseLinearSolver の内部タイマー (CSC 変換 / 分解 / 代入) をリセット
                            FEM.CsparseLinearSolver.ResetInternalTimers();

                            UpdateSoilDisp(caseModel);
                            UpdateF(caseModel);

                            // 入力値＋応力解析結果モード: 前ステップのFxiを入力軸力に加算
                            if (UseAnalysisAxialForce && step > 0)
                            {
                                UpdateAxialForceFromAnalysis(caseModel);
                            }

                            // 引張定着筋なし半剛接合 (キャプテン/F.T.Pile/キャプリング) で軸力モードに応じた
                            // 軸力が引張になった杭を NG としてログに残す (杭ごとに 1 ケース内 1 回のみ)
                            await LogTensionForSemiRigidPilesAsync(caseModel, caseTag, step + 1, nStep, loggedNgTensionPileNos);

                            // 現ステップ軸力での M–φ 再解決は、杭非線形ONのときのみ
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiByCurrentAxialForMiddleBeam(caseModel);
                            }

                            caseModel.SetR();

                            // 反復なし簡易法の場合は1回で終了
                            int maxIterations = SkipIteration ? 1 : 100;
                            // 適応的緩和係数の初期化
                            double currentRelaxFactor = SkipIteration ? 1.0 : RelaxationFactor; // 簡易法は緩和なし
                            double prevResidual = caseModel.NormsROnNormsFint;
                            int consecutiveDecrease = 0; // 連続減少カウント

                            // v11: 停滞検出用の変数
                            int stagnationCount = 0;           // 停滞カウント（残差がほぼ変化しない回数）
                            const int STAGNATION_LIMIT = 15;   // 停滞判定の閾値回数
                            const double STAGNATION_RATIO = 0.98; // 残差比がこれ以上なら停滞とみなす
                            const double RELAXED_ALPHA = 1e-5; // 停滞時の緩和収束基準
                            double effectiveAlpha = alpha;     // 実効収束基準（停滞時に緩和）

                            // v12: 発散検出用の変数
                            double initialResidual = prevResidual;  // 初期残差（発散判定の基準）
                            double minResidualSeen = prevResidual;  // これまでの最小残差
                            int divergenceCount = 0;                // 発散検出回数
                            const double DIVERGENCE_RATIO = 100.0;  // 残差がこの倍率を超えたら発散とみなす
                            bool autoSwitchedToLineSearch = false;  // 自動でライン探索に切り替えたか
                            // v21 Phase 3 prep: インスタンスフィールド _useLineSearch の書き換えを廃止。
                            // 効果的ライン探索フラグは「ユーザー設定 UseLineSearch OR 自動切替 autoSwitchedToLineSearch」の union として
                            // 反復の各タイミングで再評価する（旧 _useLineSearch フィールド書き換え時と同じ意味論）。

                            // v13: 緩やかな発散検出用の変数
                            int slowDivergenceCount = 0;            // 連続増加回数
                            const int SLOW_DIVERGENCE_LIMIT = 10;   // この回数連続増加で緩やかな発散と判定

                            // v18: 長期未改善検出（counter-loading で residual が振動するケース対策）
                            // minResidualSeen が一定回数更新されない場合、収束基準を minSeen * 1.2 に緩和
                            int iterationsSinceMinUpdated = 0;
                            const int NO_IMPROVEMENT_LIMIT = 30;

                            // 2026-05-06: NR モード追跡 (Full NR / Modified NR)
                            //   K 行列を組み直した反復 = Full NR 反復 (kRebuildCount)
                            //   K を再利用した反復     = Modified NR 反復 (kReuseCount)
                            //   サマリーレポート / iter ログ / per-step プロファイル行に表示。
                            int kRebuildCount = 0;
                            int kReuseCount = 0;

                            // 2026-05-06 (D): 改善率ベース緩和判定。
                            // 残差が「微減し続ける」ケースでは minResidualSeen が更新され続け、
                            // iterationsSinceMinUpdated が NO_IMPROVEMENT_LIMIT に到達せず緩和が発火しない問題対策。
                            // 過去 SLOW_IMPROVEMENT_WINDOW 反復前の残差と現在残差を比較し、改善率が
                            // SLOW_IMPROVEMENT_THRESHOLD 未満なら「実質停滞」とみなして緩和を発火する。
                            const int SLOW_IMPROVEMENT_WINDOW = 30;       // 比較ウィンドウ
                            const double SLOW_IMPROVEMENT_THRESHOLD = 0.10; // 30 反復で 10% 未満の減少なら停滞
                            const double SLOW_IMPROVEMENT_RELAX_CAP = 1e-2; // この残差を超えていたら緩和しない (発散領域なので)
                            var residualHistory = new Queue<double>();

                            // v17: 長時間反復時の収束基準緩和（40反復以上 + 残差≦RELAXED_ALPHA で緩和）

                            // v16: 診断値をループ外に宣言（Modified NRフェーズでスキップしても前回値を保持）
                            double diagKMin = double.NaN, diagKMax = double.NaN;
                            double dispMaxAbs = double.NaN;
                            // v21 Phase 3 prep: 旧 _lastSpringKMin/_lastSpringKMax（インスタンスフィールド）は
                            // 反復間／ステップ間で値を保持する副作用があり、診断ログに前回値を表示していた。
                            // 同じ挙動を保つため、ここでステップ局所として宣言し、FindK を呼ばない反復では
                            // 前回反復の値をそのまま引き継ぐ。
                            double springKMin = double.NaN, springKMax = double.NaN;

                            // v27: 振動診断 — 支配 DOF (最大 |δu| の自由度) の符号反転 (flip-flop) 検出
                            // リミットサイクル型の発散（残差が単調減少せず周期的に跳ね返る）の原因 DOF を特定する。
                            // 反復間で同じ key が連続して現れ、かつ符号が逆転したら flip としてカウント。
                            List<(string Key, string NodeName, string DofName, double Value)> dominantDofs = null;
                            string prevDominantDofKey = null;
                            int prevDominantSign = 0;   // +1 / -1 / 0
                            int flipFlopCount = 0;

                            // v27: 案 A — リミットサイクル Aitken 平均化
                            // flip# が AITKEN_FLIP_TRIGGER に達したら、直近 AITKEN_HISTORY 反復の CumulativeDisp を
                            // 単純平均で置換し、周期振動のアトラクタ平均点に飛ばす。暴走防止のため MAX_FIRE 回まで。
                            // 検証結果 (2026-04-23): TRIGGER=2 (A-plus) や MAX_FIRE=4+リセット (A-rev) を試したが
                            // いずれも案 A より悪化。TRIGGER=3 / MAX_FIRE=2 / リセットなしが最適だった。
                            // 理由: 長期未改善検出が Aitken 後の最小残差 (≈10.9) をそのまま採用することで
                            // 緩和基準判定が有利になる。minResidualSeen をリセットすると逆効果。
                            const int AITKEN_HISTORY = 3;
                            const int AITKEN_FLIP_TRIGGER = 3;
                            const int AITKEN_MAX_FIRE = 2;
                            var recentCumulativeDisp = new Queue<Dictionary<string, NodeDisp>>();
                            int aitkenFiredCount = 0;

                            // v28 問題 A 診断: 反復間の状態変化 (M-φ セグメント, 土ばね降伏) を検出するスナップショット
                            // 各反復の末尾で現在状態を記録、次反復の Task.Run 外で差分ログを出力
                            // Beam.Name が全 beam 共通のため List インデックスで一意識別
                            int[] prevBeamSegments = null;  // index = beam idx, value = segment index
                            var prevYieldedSoilSprings = new HashSet<string>();    // "pileNo-nodeIdx-side" で識別
                            bool isFirstIterSnapshot = true;

                            // v29 (2026-04-27): Modified NR で K を再利用していると、M-φ セグメント変化や
                            // p-y 降伏状態変化が起きた直後に K が物理的に無効化され、Newton 方向が暴れて
                            // ラインサーチが α=0.05 級の小ステップに張り付き収束停滞する。
                            // 状態変化を検知したら次反復で K を強制再構築 (Full NR 化) して立て直す。
                            bool forceFullNRNextIter = false;

                            while (caseModel.NormsROnNormsFint >= effectiveAlpha && n_iteration <= maxIterations)
                            {
                                // v21 Phase 3 prep: 効果的ライン探索フラグを「ユーザー設定 ∪ 自動切替」の union として毎反復評価
                                // （旧コード: auto-switch 時に _useLineSearch フィールドを true に書換 → UseLineSearch プロパティが true になる
                                //   を field 書換なしで再現。インスタンスフィールドを汚さないため並列化への準備になる）
                                bool effectiveUseLineSearch = UseLineSearch || autoSwitchedToLineSearch;
                                double usedRelaxFactor = currentRelaxFactor; // このステップで使う値を保存

                                // 2026-05-06: NR モード追跡用フラグ。RunWork 内で更新、ログ出力 (RunWork 外) で参照。
                                bool kRebuiltThisIter = false;

                                // v28: Mcr 同期 Mode 切替で新規クラック検出したばね名を収集 (Task.Run 外でログ出力するため)
                                var newlyCrackedSprings = new List<(string Name, double M, double Mcr)>();

                                // v28 問題 A 診断: 反復内で状態変化した要素を収集 (Beam は List idx で一意識別)
                                var mphiChanges = new List<(int BeamIdx, int Prev, int Curr)>();
                                var newlyYieldedSoilSprings = new List<string>();  // 新規降伏した p-y ばね
                                var newlyUnyieldedSoilSprings = new List<string>(); // 降伏解除された p-y ばね
                                int[] currentBeamSegments = null;  // index = beam idx
                                var currentYieldedSoilSprings = new HashSet<string>();

                                // 重い計算をバックグラウンドで実行（診断値もここで算出）
                                // E3c-3 tune: MDOP>1 時は RunWork が同期実行に切替えて Task.Run nesting を回避
                                await RunWork(() =>
                                {
                                    // トークンを投げて途中キャンセルを可能にする
                                    token.ThrowIfCancellationRequested();

                                    // N は荷重ケース一定だが、簡便に毎回解決しても可（コストは小）
                                    //SetupNonlinearMThetaForLoadCase(caseModel, loadCase);

                                    // Newton-Raphsonモード:
                                    // - Full NR: 常に毎反復で接線剛性+Kマトリクス更新
                                    // - Modified NR: 最初の FullNRIterations 回は Full NR、以降は K 再利用
                                    // v29: 直前反復で M-φ セグメント変化 / p-y 降伏変化を検知した場合は強制 Full NR
                                    bool useFullNR = !UseModifiedNewtonRaphson || n_iteration <= FullNRIterations || forceFullNRNextIter;
                                    forceFullNRNextIter = false; // フラグ消費

                                    if (loadCase.IsPileNonLinear && useFullNR)
                                    {
                                        // Full NR: ダンピングなし（正確なヤコビアンで2次収束）
                                        // Modified NR の初期反復: ダンピングあり（安定化）
                                        bool relaxTangent = UseModifiedNewtonRaphson;
                                        UpdateBeamMPhiTangent(caseModel, useRelaxation: relaxTangent);
                                    }

                                    // KTan 組立（戻り値で springK の min/max を受け取る）
                                    // v17: Modified NRモードの適応フェーズではKマトリクス組立をスキップ（高速化）
                                    if (useFullNR || !loadCase.IsPileNonLinear || n_iteration == 1)
                                    {
                                        long _tsFindK = System.Diagnostics.Stopwatch.GetTimestamp();
                                        (springKMin, springKMax) = FindK(iLC, caseModel);
                                        profFindKTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindK;
                                        profFindKCalls++;
                                        kRebuiltThisIter = true;
                                        kRebuildCount++;

                                        // 初回反復時のみ剛性マトリクスの安定性チェック
                                        if (n_iteration == 1)
                                        {
                                            caseModel.ValidateStability(useEigenvalueCheck: false);
                                        }
                                    }
                                    else
                                    {
                                        kRebuiltThisIter = false;
                                        kReuseCount++;
                                    }

                                    // ラインサーチ or 通常の更新
                                    if (effectiveUseLineSearch)
                                    {
                                        // ラインサーチ: Newton方向を計算し、最適なステップ長を探索
                                        long _tsSolve = System.Diagnostics.Stopwatch.GetTimestamp();
                                        var newtonDir = SolveNewtonDirection(caseModel);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        double currentRes = caseModel.NormsROnNormsFint;

                                        // バックトラッキングラインサーチで最適αを見つける
                                        long _tsLS = System.Diagnostics.Stopwatch.GetTimestamp();
                                        double optimalAlpha = BacktrackingLineSearch(
                                            caseModel, newtonDir, currentRes, iLC, loadCase.IsPileNonLinear, out int _lsTrials);
                                        profLineSearchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsLS;
                                        profLineSearchCalls++;
                                        profLineSearchTrialsTotal += _lsTrials;

                                        // 最適αでの状態は既にEvaluateResidualAtAlpha内で適用済み
                                        usedRelaxFactor = optimalAlpha; // ログ用に記録
                                    }
                                    else
                                    {
                                        // 通常の更新（緩和係数適用）
                                        long _tsSolve = System.Diagnostics.Stopwatch.GetTimestamp();
                                        SolveDdAndUpdateX(caseModel, usedRelaxFactor);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        // 割線剛性更新（FindTの前に実行して、最新のK_secで内力を計算）
                                        if (loadCase.IsPileNonLinear)
                                            UpdateBeamMPhiSecant(caseModel);

                                        // 断面力・T更新と残差更新
                                        long _tsFindT = System.Diagnostics.Stopwatch.GetTimestamp();
                                        FindT(iLC, caseModel);
                                        profFindTTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindT;

                                        caseModel.FindR();
                                    }

                                    /* NaN診断: 反復ごとのチェック
                                    FEM.NaNDiagnostics.SetIteration(n_iteration);
                                    if (!double.IsFinite(caseModel.NormsROnNormsFint))
                                    {
                                        FEM.NaNDiagnostics.LogNaN($"NormsROnNormsFint is NaN at iteration {n_iteration}!");
                                        FEM.NaNDiagnostics.CheckNodeDisplacements(caseModel.Nodes);
                                        FEM.NaNDiagnostics.CheckBeamForces(caseModel.Beams);
                                    } */

                                    // v21 Phase 3 prep: ばね剛性 min/max は FindK の戻り値から直接取得するため
                                    // ここでの再代入は不要（FindK を呼ばない分岐では NaN のまま）

                                    // v17: 診断値K対角は重い処理なので、最初の反復と5反復ごとのみ計算
                                    // Modified NRモードの適応フェーズではKが更新されないため計算頻度を下げる
                                    if (n_iteration == 1 || n_iteration % 5 == 0)
                                    {
                                        (diagKMin, diagKMax) = GetKDiagonalMiNMax(caseModel, isTan: true);
                                    }

                                    // 診断値: 代表自由度の |d| 最大値（節点の増分変位から）
                                    dispMaxAbs = GetMaxAbsIncrementalDisp(caseModel);

                                    // v27: 振動診断用 — 上位 3 DOF を取得（Ux/Uy/Uz/Rx/Ry/Rz 全成分対象）
                                    dominantDofs = GetTopIncrementalDofs(caseModel, 3);

                                    // v27: 案 A — CumulativeDisp スナップショットをキューに追加（Aitken 平均化用）
                                    var snap = new Dictionary<string, NodeDisp>(caseModel.Nodes.Count);
                                    foreach (var nd in caseModel.Nodes)
                                        snap[nd.Name] = nd.CumulativeDisp.Clone();
                                    recentCumulativeDisp.Enqueue(snap);
                                    while (recentCumulativeDisp.Count > AITKEN_HISTORY)
                                        recentCumulativeDisp.Dequeue();

                                    // v28: Mcr 同期 Mode 切替 (ヒステリシス付き)
                                    // 場所打ち RC 杭の杭頭回転ばねで |M| が Mcr を初めて超えた瞬間を検出し、
                                    // HasCrackedXY = true にラッチ。以降は post-crack curve を使用 (除荷しても戻らない)。
                                    // 閾値 0.999×Mcr で若干緩めてヒステリシスラッチを安定化。
                                    if (caseModel.RotationalSprings != null)
                                    {
                                        foreach (var rs in caseModel.RotationalSprings)
                                        {
                                            if (rs?.McrXY is null || rs.HasCrackedXY) continue;
                                            double mx = rs.CumulativeForce?.Mxj ?? 0.0;
                                            double my = rs.CumulativeForce?.Myj ?? 0.0;
                                            double mRes = Math.Sqrt(mx * mx + my * my);
                                            if (mRes >= rs.McrXY.Value * 0.999)
                                            {
                                                // v28 アプローチ I: クラック発生時点のモーメント方向 (= 回転方向) を記録
                                                rs.MarkCracked(mx, my);
                                                newlyCrackedSprings.Add((rs.Name ?? "<unnamed>", mRes, rs.McrXY.Value));
                                            }
                                        }
                                    }

                                    // v28 問題 A 診断: 杭体 M-φ セグメント変化 と 土ばね降伏状態変化を収集
                                    // iter 22→23 の残差爆発原因を特定する。Beam.Name が共通のため List idx で識別。
                                    if (caseModel.Beams != null)
                                    {
                                        int beamCount = caseModel.Beams.Count;
                                        currentBeamSegments = new int[beamCount];
                                        for (int idx = 0; idx < beamCount; idx++)
                                        {
                                            var beam = caseModel.Beams[idx];
                                            int curSeg = (beam.ResolvedCombinedCurve != null) ? beam.CurrentMPhiSegmentIndex : -1;
                                            currentBeamSegments[idx] = curSeg;
                                            if (!isFirstIterSnapshot
                                                && prevBeamSegments != null && idx < prevBeamSegments.Length
                                                && prevBeamSegments[idx] != curSeg
                                                && curSeg >= 0 && prevBeamSegments[idx] >= 0)  // -1 (no curve) はスキップ
                                            {
                                                mphiChanges.Add((idx, prevBeamSegments[idx], curSeg));
                                            }
                                        }
                                    }

                                    // 土ばね降伏状態: 各杭ノードについて |y| が yy を超えているか判定
                                    if (InputModel?.PileLayoutItems != null)
                                    {
                                        foreach (var pli in InputModel.PileLayoutItems)
                                        {
                                            if (pli.PileNodes == null || pli.SoilNodes == null) continue;
                                            var reactions = InputModel.ElementDivision?.SoilPiles?[pli.SoilPileAltNo - 1]?.HorizontalSoilReactions;
                                            if (reactions == null) continue;
                                            bool isFront = pli.IsFrontPiles != null && iLC < pli.IsFrontPiles.Count && pli.IsFrontPiles[iLC];
                                            // E3b: case-local な PileNodes / SoilNodes を取得
                                            var pliPileNodes = caseModel.GetPileNodes(pli);
                                            var pliSoilNodes = caseModel.GetSoilNodes(pli);
                                            for (int i = 0; i < pliPileNodes.Count && i < pliSoilNodes.Count; i++)
                                            {
                                                var pn = pliPileNodes[i];
                                                var sn = pliSoilNodes[i];
                                                if (pn?.CumulativeDisp is null || sn?.CumulativeDisp is null) continue;
                                                var rel = pn.CumulativeDisp - sn.CumulativeDisp;
                                                double abs = Math.Sqrt(rel.Ux * rel.Ux + rel.Uy * rel.Uy);
                                                // i-1 (bottom side) と i (top side) の 2 層
                                                if (i > 0 && i - 1 < reactions.Count && reactions[i - 1].IsYieldedAtY(abs, isTop: false, isFront))
                                                {
                                                    string key = $"{pli.No}-{i}-btm";
                                                    currentYieldedSoilSprings.Add(key);
                                                }
                                                if (i < reactions.Count && reactions[i].IsYieldedAtY(abs, isTop: true, isFront))
                                                {
                                                    string key = $"{pli.No}-{i}-top";
                                                    currentYieldedSoilSprings.Add(key);
                                                }
                                            }
                                        }
                                    }

                                    // 変化集計 (2 反復目以降)
                                    if (!isFirstIterSnapshot)
                                    {
                                        foreach (var key in currentYieldedSoilSprings)
                                        {
                                            if (!prevYieldedSoilSprings.Contains(key))
                                                newlyYieldedSoilSprings.Add(key);
                                        }
                                        foreach (var key in prevYieldedSoilSprings)
                                        {
                                            if (!currentYieldedSoilSprings.Contains(key))
                                                newlyUnyieldedSoilSprings.Add(key);
                                        }
                                    }

                                    // ループ内の要所で再チェック（重い処理の長い段階がある場合はここに複数入れる）
                                    token.ThrowIfCancellationRequested();

                                });

                                // v28: Task.Run 外で新規クラックを UI ログに出力 (AddLogAsync はキュー方式で非同期 flush)
                                // 複数杭が同時クラックする場合 (重荷重で 1 反復目から全杭が Mcr 到達する等) は
                                // 集約して 1 行に圧縮する。単独クラックは従来通り詳細行で出力。
                                if (newlyCrackedSprings.Count == 1)
                                {
                                    var (name, mRes, mcr) = newlyCrackedSprings[0];
                                    await AddLogAsync($"　　📌 杭頭 RotSpring {name}: Mcr 到達 → クラック判定 (|M|={mRes:E3} ≥ Mcr={mcr:E3} kNm)、以降 post-crack curve 使用");
                                }
                                else if (newlyCrackedSprings.Count > 1)
                                {
                                    double mMin = newlyCrackedSprings.Min(x => x.Item2);
                                    double mMax = newlyCrackedSprings.Max(x => x.Item2);
                                    double mcrRef = newlyCrackedSprings[0].Item3;
                                    await AddLogAsync($"　　📌 杭頭 Mcr 到達 → クラック判定 {newlyCrackedSprings.Count} 本 (|M|=[{mMin:E3}, {mMax:E3}] ≥ Mcr={mcrRef:E3} kNm)、以降 post-crack curve 使用");
                                }

                                // v28 問題 A 診断: 杭体 M-φ セグメント変化 (集計ログ、最大 5 件の詳細)
                                if (mphiChanges.Count > 0 && currentBeamSegments != null)
                                {
                                    // セグメント分布サマリ (curve 持ちの beam のみ集計、-1 はスキップ)
                                    var segDist = new Dictionary<int, int>();
                                    foreach (int seg in currentBeamSegments)
                                    {
                                        if (seg < 0) continue;
                                        if (!segDist.ContainsKey(seg)) segDist[seg] = 0;
                                        segDist[seg]++;
                                    }
                                    string distStr = string.Join(", ", segDist.OrderBy(k => k.Key).Select(kv => $"seg{kv.Key}:{kv.Value}本"));
                                    int advances = mphiChanges.Count(c => c.Curr > c.Prev);
                                    int recedes = mphiChanges.Count(c => c.Curr < c.Prev);
                                    var topChanges = mphiChanges.Take(5).Select(c => $"beam[{c.BeamIdx}]:{c.Prev}→{c.Curr}");
                                    string detailStr = string.Join(", ", topChanges);
                                    string suffix = mphiChanges.Count > 5 ? $" ...他 {mphiChanges.Count - 5} 件" : "";
                                    await AddLogAsync($"　　  ▼ M-φ セグメント変化 {mphiChanges.Count} 件 (進行:{advances}, 戻り:{recedes}): [{detailStr}{suffix}] / 分布 [{distStr}]");
                                }

                                // v28 問題 A 診断: 土ばね p-y 降伏状態変化
                                if (newlyYieldedSoilSprings.Count > 0 || newlyUnyieldedSoilSprings.Count > 0)
                                {
                                    string yieldedSample = newlyYieldedSoilSprings.Count > 0
                                        ? string.Join(", ", newlyYieldedSoilSprings.Take(3)) + (newlyYieldedSoilSprings.Count > 3 ? $" ...他 {newlyYieldedSoilSprings.Count - 3}" : "")
                                        : "-";
                                    string unyieldedSample = newlyUnyieldedSoilSprings.Count > 0
                                        ? string.Join(", ", newlyUnyieldedSoilSprings.Take(3)) + (newlyUnyieldedSoilSprings.Count > 3 ? $" ...他 {newlyUnyieldedSoilSprings.Count - 3}" : "")
                                        : "-";
                                    await AddLogAsync($"　　  ▼ 土ばね p-y 降伏状態変化: 新規降伏 {newlyYieldedSoilSprings.Count} 件 [{yieldedSample}], 降伏解除 {newlyUnyieldedSoilSprings.Count} 件 [{unyieldedSample}] / 現時点降伏 {currentYieldedSoilSprings.Count} 件");
                                }

                                // v29: 状態変化を検知したら次反復で K を強制再構築 (Modified NR の K 再利用を一時停止)
                                if (UseModifiedNewtonRaphson && !isFirstIterSnapshot &&
                                    (mphiChanges.Count > 0 || newlyYieldedSoilSprings.Count > 0 || newlyUnyieldedSoilSprings.Count > 0))
                                {
                                    forceFullNRNextIter = true;
                                }

                                // スナップショット更新
                                prevBeamSegments = currentBeamSegments;
                                prevYieldedSoilSprings = currentYieldedSoilSprings;
                                isFirstIterSnapshot = false;

                                // v28 アプローチ I 採用後の 2026-04-23 更新:
                                // ω=0.01 強制低減 + forceDisableLineSearchNextIter の workaround は削除済。
                                // 方向ロック + ヒステリシス (CrackNx/CrackNy/ThetaProjMax) で根本解決したため、
                                // Newton は通常のライン探索 + adaptive relaxation で安定動作する。

                                if (StopONMaxDisplacement && !double.IsNaN(dispMaxAbs) && Math.Abs(dispMaxAbs) > MaxAllowedDisplacement)
                                {
                                    // ログに残す
                                    await AddLogAsync($"解析中止: 代表変位が閾値を超えました max|d|={dispMaxAbs:E3} > threshold={MaxAllowedDisplacement:E3}");

                                    // View に警告表示を依頼（UI スレッドでイベントを発火、BeginInvoke で worker をブロックしない）
                                    string warnMsg = $"解析を中止しました。\n代表変位が閾値 {MaxAllowedDisplacement} m を超えました（{dispMaxAbs:E3}）。";
                                    Application.Current?.Dispatcher.BeginInvoke(() => RequestShowWarning?.Invoke(warnMsg));

                                    // キャンセルを発行して呼び出し側で OperationCanceledException を処理させる
                                    _cancellationTokenSource?.Cancel();
                                    RequestClearProgressAnimation?.Invoke();
                                    throw new OperationCanceledException(token);
                                }


                                // v29 (2026-04-27): 反復ログを 1 行に統合 (旧: 残差/診断/支配DOF の 3 行)。
                                //   ‖R‖² / max|δu| / K range / spring k range / 支配 DOF を 1 行にまとめる。
                                //   行数 1/3 化でログのスクロール量が大幅減、視認性向上。
                                string stepSuffix;
                                if (effectiveUseLineSearch && usedRelaxFactor < 0.99)
                                    stepSuffix = $"α={usedRelaxFactor:N2}";
                                else if (currentRelaxFactor < 0.99)
                                    stepSuffix = $"ω={currentRelaxFactor:N2}";
                                else
                                    stepSuffix = "ω=1.00";

                                bool isConverged = caseModel.NormsROnNormsFint < alpha;
                                string compareSym = isConverged ? "≦" : ">";
                                string convergeFlag = isConverged ? " ✓Converged" : "";

                                // 支配 DOF (flip 検出を含む) — リミットサイクル原因 DOF の特定
                                // 案 A (2026-05-06): flip カウントを「DOF 種別 (Ry/Rx/Rz/Ux/Uy/Uz)」単位で集計。
                                // 旧実装は「同一 DOF キー (例: "InputNode-6:Ry") のみで連続カウント」だったため、
                                // InputNode-3:Ry ↔ InputNode-6:Ry ↔ FoundationNode-P28:Ry の交互支配で
                                // カウンタが毎回リセットされ、Aitken トリガに到達しない事案があった。
                                // 同種別 DOF 間の符号反転もリミットサイクルとして数えるよう拡張。
                                string dominantStr = "";
                                if (dominantDofs != null && dominantDofs.Count > 0)
                                {
                                    const double FLIP_THRESHOLD = 1e-10;
                                    var top = dominantDofs[0];
                                    int curSign = Math.Sign(top.Value);
                                    string curDofType = ExtractDofType(top.Key);
                                    string? prevDofType = prevDominantDofKey != null ? ExtractDofType(prevDominantDofKey) : null;

                                    string flipInfo = "";
                                    if (Math.Abs(top.Value) > FLIP_THRESHOLD
                                        && prevDofType != null && prevDofType == curDofType
                                        && prevDominantSign != 0 && curSign != 0
                                        && curSign != prevDominantSign)
                                    {
                                        flipFlopCount++;
                                        flipInfo = $" ⚠flip#{flipFlopCount}";
                                    }
                                    else if (prevDofType != curDofType)
                                    {
                                        // DOF 種別境界 (例: Ry ⇄ Ux) でリセット
                                        flipFlopCount = 0;
                                    }
                                    prevDominantDofKey = top.Key;
                                    prevDominantSign = curSign;

                                    static string SignedExp(double v) => (v >= 0 ? "+" : "") + v.ToString("E2");
                                    dominantStr = $"  ▶{top.Key}={SignedExp(top.Value)}{flipInfo}";
                                }

                                // 案 C (2026-05-05): 並列実行時 (MDOP > 1) は iter ログを間引いて UI 負荷を軽減。
                                //   常時ログ: 最初 3 反復、収束時、flip 検出時、5 の倍数反復
                                //   それ以外は省略 (詳細トレースは MDOP=1 で逐次実行時に取得する)
                                bool isParallelMode = MaxCaseDegreeOfParallelism > 1;
                                bool isFlipDetected = !string.IsNullOrEmpty(dominantStr) && dominantStr.Contains("⚠flip");
                                bool shouldLogIter = !isParallelMode
                                    || isConverged
                                    || n_iteration <= 3
                                    || isFlipDetected
                                    || n_iteration % 5 == 0;

                                if (shouldLogIter)
                                {
                                    // 2026-05-06: NR モード可視化。K 行列を組み直した反復 (Full NR) は "[NR]"、
                                    // 再利用した反復 (Modified NR) は "[MNR]" を末尾に付与。
                                    string nrTag = kRebuiltThisIter ? "[NR]" : "[MNR]";
                                    await AddLogAsync(
                                        $"{caseTag}iter {n_iteration,2} {nrTag} {stepSuffix}  " +
                                        $"‖R‖²={caseModel.NormsROnNormsFint:E2}{compareSym}{alpha:E1}{convergeFlag}  " +
                                        $"max|δu|={dispMaxAbs:E2}  " +
                                        $"K=[{diagKMin:E1},{diagKMax:E1}]  " +
                                        $"k=[{springKMin:E1},{springKMax:E1}]" +
                                        dominantStr);
                                }

                                // v27: 案 A — リミットサイクル Aitken 平均化
                                // 支配 DOF の flip が AITKEN_FLIP_TRIGGER 回連続検出されたら、
                                // 直近 AITKEN_HISTORY 反復の CumulativeDisp を単純平均で置き換えて周期の中心点に飛ばす。
                                // これで周期 2〜3 のリミットサイクルを破って収束に向かわせる。
                                // 暴走防止のため AITKEN_MAX_FIRE 回までに制限。
                                if (flipFlopCount >= AITKEN_FLIP_TRIGGER
                                    && aitkenFiredCount < AITKEN_MAX_FIRE
                                    && recentCumulativeDisp.Count >= AITKEN_HISTORY)
                                {
                                    // E3c-3 tune: MDOP>1 時は RunWork が同期実行 (nesting 回避)
                                    await RunWork(() =>
                                    {
                                        int historyCount = recentCumulativeDisp.Count;
                                        foreach (var nd in caseModel.Nodes)
                                        {
                                            double ux = 0, uy = 0, uz = 0, rx = 0, ry = 0, rz = 0;
                                            foreach (var hist in recentCumulativeDisp)
                                            {
                                                if (!hist.TryGetValue(nd.Name, out var d)) continue;
                                                ux += d.Ux; uy += d.Uy; uz += d.Uz;
                                                rx += d.Rx; ry += d.Ry; rz += d.Rz;
                                            }
                                            nd.CumulativeDisp = new NodeDisp(
                                                ux / historyCount, uy / historyCount, uz / historyCount,
                                                rx / historyCount, ry / historyCount, rz / historyCount);
                                            // 増分変位は 0 にリセット（平均化後は新規スタート扱い）
                                            nd.IncrementalDisp = new NodeDisp(0, 0, 0, 0, 0, 0);
                                        }
                                        // 内力と残差を再計算（次の反復で新しい残差が評価される）
                                        FindT(iLC, caseModel);
                                        caseModel.FindR();
                                    });

                                    aitkenFiredCount++;
                                    flipFlopCount = 0;
                                    prevDominantDofKey = null;
                                    prevDominantSign = 0;
                                    recentCumulativeDisp.Clear();

                                    // 注: minResidualSeen / iterationsSinceMinUpdated はリセットしない。
                                    // A-rev で検証したが、リセットすると長期未改善検出が機能しなくなり、
                                    // Aitken 後に一時的に到達した低い残差がそのまま最終値として採用されなくなって悪化した。
                                    // 現状はリセット無しで、Aitken → 減衰 → 停滞 → 長期未改善検出 → 緩和基準の
                                    // シーケンスで最小残差が最終値になる。

                                    await AddLogAsync($"　　🔄 Aitken 平均化 #{aitkenFiredCount}/{AITKEN_MAX_FIRE} 発動: 直近 {AITKEN_HISTORY} 反復の CumulativeDisp 平均で書換 → 残差={caseModel.NormsROnNormsFint:E2}");
                                }

                                // 適応的緩和係数の更新（UseAdaptiveRelaxation=trueの場合のみ）
                                double currentResidual = caseModel.NormsROnNormsFint;
                                if (UseAdaptiveRelaxation)
                                {
                                    double residualRatio = prevResidual > 1e-20 ? currentResidual / prevResidual : 1.0;

                                    if (residualRatio < 0.8) // 残差が20%以上減少 → 良好な収束
                                    {
                                        consecutiveDecrease++;
                                        // 2回連続で大幅減少したらω回復（最大1.0）
                                        if (consecutiveDecrease >= 2 && currentRelaxFactor < 1.0)
                                        {
                                            currentRelaxFactor = Math.Min(currentRelaxFactor * 1.3, 1.0);
                                            consecutiveDecrease = 0;
                                        }
                                    }
                                    else if (residualRatio > 1.02) // 残差が2%以上増加 → 即座にω減少
                                    {
                                        // 増加量に応じてω減少幅を調整（より積極的に）
                                        double reductionFactor = residualRatio > 1.5 ? 0.25 : (residualRatio > 1.1 ? 0.5 : 0.7);
                                        currentRelaxFactor = Math.Max(currentRelaxFactor * reductionFactor, 0.1);
                                        consecutiveDecrease = 0;
                                    }
                                    else if (residualRatio < 1.0) // 微減（0-20%）
                                    {
                                        consecutiveDecrease++;
                                        // 3回連続微減でもω小幅回復
                                        if (consecutiveDecrease >= 3 && currentRelaxFactor < 0.7)
                                        {
                                            currentRelaxFactor = Math.Min(currentRelaxFactor * 1.1, 0.7);
                                            consecutiveDecrease = 0;
                                        }
                                    }
                                    else // 停滞（1.0-1.02）
                                    {
                                        consecutiveDecrease = 0;
                                        // 停滞時もωを少し下げる
                                        currentRelaxFactor = Math.Max(currentRelaxFactor * 0.85, 0.15);
                                    }
                                }

                                // v12/v13: 発散検出 - 残差が初期値や最小値から大幅に増加した場合
                                if (currentResidual < minResidualSeen)
                                {
                                    minResidualSeen = currentResidual;  // 最小残差を更新
                                    slowDivergenceCount = 0;  // 最小更新時にリセット
                                    iterationsSinceMinUpdated = 0;  // v18: 改善があったのでリセット
                                }
                                else
                                {
                                    iterationsSinceMinUpdated++;  // v18: 未改善カウント進行
                                }

                                // v18: 長期未改善検出 - 振動パターンで stagnation/slow-divergence の
                                // どちらも triggers しない場合に最後のセーフティネットとして働く
                                // 緩和値は「現在残差 × 1.1」「最小残差 × 1.2」の大きい方を採用し、
                                // 今この反復でループが確実に抜けられるようにする（振動残差では minSeen*1.2 が不十分）
                                if (iterationsSinceMinUpdated >= NO_IMPROVEMENT_LIMIT && !SkipIteration)
                                {
                                    double relaxed = Math.Max(
                                        Math.Max(minResidualSeen * 1.2, currentResidual * 1.1),
                                        RELAXED_ALPHA);
                                    if (relaxed > effectiveAlpha)
                                    {
                                        effectiveAlpha = relaxed;
                                        await AddLogAsync($"  ⚠ 長期未改善検出: {NO_IMPROVEMENT_LIMIT}反復で最小残差 {minResidualSeen:E2} が更新されません。収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                        iterationsSinceMinUpdated = 0;  // 再カウント
                                    }
                                }

                                // 2026-05-06 (D): 改善率ベース緩和判定
                                // 「微減し続ける」ケース (毎反復 minSeen 更新でも改善が遅い) を救済する。
                                // 過去 SLOW_IMPROVEMENT_WINDOW 反復前の残差と比較し、改善率が閾値未満で停滞判定。
                                residualHistory.Enqueue(currentResidual);
                                if (residualHistory.Count > SLOW_IMPROVEMENT_WINDOW)
                                {
                                    double residualPast = residualHistory.Dequeue();
                                    if (!SkipIteration
                                        && currentResidual > effectiveAlpha
                                        && currentResidual <= SLOW_IMPROVEMENT_RELAX_CAP
                                        && residualPast > 1e-30)
                                    {
                                        double improvement = (residualPast - currentResidual) / residualPast;
                                        if (improvement < SLOW_IMPROVEMENT_THRESHOLD)
                                        {
                                            // 改善率不足 → 現状を accept できるよう緩和
                                            double relaxed = Math.Max(currentResidual * 1.1, RELAXED_ALPHA);
                                            if (relaxed > effectiveAlpha)
                                            {
                                                effectiveAlpha = relaxed;
                                                await AddLogAsync($"  ⚠ 改善率不足: {SLOW_IMPROVEMENT_WINDOW} 反復で残差 {residualPast:E2}→{currentResidual:E2} ({improvement * 100:F1}% 減のみ)。収束基準を緩和 ({alpha:E2}→{effectiveAlpha:E2})");
                                                residualHistory.Clear();  // 緩和発火後はリセット
                                            }
                                        }
                                    }
                                }

                                // v17: 長時間反復時の収束基準緩和
                                // 40反復以上で残差が RELAXED_ALPHA 以下なら、十分収束したとみなす
                                // （M-φ/M-θ非線形で残差が振動停滞するケースへの対策）
                                if (n_iteration >= 40 && currentResidual <= RELAXED_ALPHA
                                    && effectiveAlpha < RELAXED_ALPHA)
                                {
                                    effectiveAlpha = RELAXED_ALPHA;
                                    await AddLogAsync($"  → 長時間反復: 残差 {currentResidual:E2} ≦ {RELAXED_ALPHA:E2} で収束基準を緩和 ({alpha:E2}→{effectiveAlpha:E2})");
                                }

                                bool isDiverging = currentResidual > initialResidual * DIVERGENCE_RATIO ||
                                                   currentResidual > minResidualSeen * DIVERGENCE_RATIO;

                                // v13: 緩やかな発散の検出（残差が連続して増加）
                                if (currentResidual > prevResidual * 1.001)  // 0.1%以上の増加
                                {
                                    slowDivergenceCount++;
                                    if (slowDivergenceCount >= SLOW_DIVERGENCE_LIMIT && !SkipIteration)
                                    {
                                        await AddLogAsync($"  ⚠ 緩やかな発散検出: {slowDivergenceCount}回連続で残差が増加しています (最小:{minResidualSeen:E2} → 現在:{currentResidual:E2})");

                                        // すでにライン探索を使用している場合は、収束基準を緩和
                                        if (effectiveUseLineSearch)
                                        {
                                            // 最小残差から5%以内なら収束とみなす
                                            double relaxedTarget = minResidualSeen * 1.05;
                                            if (currentResidual <= relaxedTarget && relaxedTarget > effectiveAlpha)
                                            {
                                                effectiveAlpha = Math.Max(currentResidual * 1.1, relaxedTarget);
                                                await AddLogAsync($"  → 収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                            }
                                        }
                                        slowDivergenceCount = 0;  // リセットして再カウント
                                    }
                                }
                                else if (currentResidual < prevResidual * 0.999)  // 0.1%以上の減少
                                {
                                    slowDivergenceCount = 0;  // 改善があればリセット
                                }

                                if (isDiverging && !autoSwitchedToLineSearch && !effectiveUseLineSearch && !SkipIteration)
                                {
                                    divergenceCount++;
                                    if (divergenceCount >= 2)  // 2回連続で発散検出
                                    {
                                        // 自動的にライン探索に切り替え（ステップ局所の effectiveUseLineSearch のみ更新）
                                        effectiveUseLineSearch = true;
                                        autoSwitchedToLineSearch = true;
                                        currentRelaxFactor = 1.0;  // ライン探索時は緩和係数をリセット
                                        await AddLogAsync($"  ⚠ 発散検出: 残差が大幅に増加しました (初期:{initialResidual:E2} → 現在:{currentResidual:E2})。ライン探索に自動切り替えします。");
                                    }
                                }
                                else if (!isDiverging)
                                {
                                    divergenceCount = 0;  // 発散していなければカウントリセット
                                }

                                // v11: 停滞検出 - 残差がほぼ変化しない場合をカウント
                                double residualRatioForStagnation = prevResidual > 1e-20 ? currentResidual / prevResidual : 1.0;
                                if (residualRatioForStagnation >= STAGNATION_RATIO && residualRatioForStagnation <= 1.05)
                                {
                                    stagnationCount++;
                                    // 停滞が続き、残差が緩和基準以下なら収束基準を緩和
                                    if (stagnationCount >= STAGNATION_LIMIT && currentResidual <= RELAXED_ALPHA)
                                    {
                                        effectiveAlpha = RELAXED_ALPHA;
                                        await AddLogAsync($"  ⚠ 停滞検出: {stagnationCount}回連続で残差が改善しません。収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                    }
                                    else if (stagnationCount >= STAGNATION_LIMIT * 2)
                                    {
                                        // 長期停滞の場合は強制的に収束基準を緩和
                                        effectiveAlpha = Math.Max(currentResidual * 1.1, RELAXED_ALPHA);
                                        await AddLogAsync($"  ⚠ 長期停滞検出: 収束基準を現在の残差に合わせて緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                    }
                                }
                                else if (residualRatioForStagnation < STAGNATION_RATIO)
                                {
                                    // 改善があればカウントリセット
                                    stagnationCount = 0;
                                }

                                prevResidual = currentResidual;

                                await Task.Yield(); // UIスレッドを解放
                                n_iteration += 1;

                                // 並列モニタ: NR 反復数を更新 (BeginInvoke、ワーカー非ブロック)
                                {
                                    int iterDisplay = n_iteration;
                                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                        monitorItem.CurrentIteration = iterDisplay);
                                }
                            }

                            // Maximum iteration check
                            string dispInfo = !double.IsNaN(dispMaxAbs) ? $", max|d|={dispMaxAbs:E3}m" : "";
                            bool converged = !(n_iteration > maxIterations && caseModel.NormsROnNormsFint >= effectiveAlpha);
                            if (!converged)
                            {
                                double finalResidual = caseModel.NormsROnNormsFint;
                                await AddLogAsync($"  → 未収束: 最大反復回数 {maxIterations} に到達。残差ノルム={finalResidual:E3} (許容値={effectiveAlpha:E3}){dispInfo}");

                                // Phase 3 step-level cut-back: 直近成功ステップに巻き戻して、失敗ステップだけ 1/M に分割して再試行。
                                // 条件: フラグ on, checkpoint 取得済 (= step >= 1 で前ステップ収束), cut-back 上限未達。
                                //
                                // インデックスは「連番」を採用。step は常に +1 ずつ進む (cut-back でジャンプしない)。
                                // 結果リストの Step 番号も連番で隙間なく埋まる (下流コンシューマ互換のため)。
                                // 内部の「ロード増分分母」は effectiveDenom が別途管理し cut-back で ×M される。
                                if (_useStepLevelCutback
                                    && lastSuccessCheckpoint != null
                                    && cutbackCount < MAX_CUTBACKS_PER_ATTEMPT)
                                {
                                    int oldStep = step;             // 失敗ステップの連番 (= 直近成功ステップ + 1)
                                    int curNStep = nStep;            // 連番上限 (cut-back で増える)
                                    int oldEffDenom = effectiveDenom; // ロード増分分母 (cut-back で ×M)
                                    int newEffDenom = effectiveDenom * CUTBACK_DIVISOR;
                                    int remainingSteps = curNStep - oldStep;  // 失敗時の残ステップ (失敗自身を含む)
                                    int addedSteps = (CUTBACK_DIVISOR - 1) * remainingSteps;  // 細分化で追加されるステップ数

                                    await AddLogAsync($"  ↩ cut-back ({cutbackCount + 1}/{MAX_CUTBACKS_PER_ATTEMPT}): step{step + 1}/{curNStep} 未収束 → end-of-step{step} へ巻き戻し、残 {remainingSteps} ステップを 1/{CUTBACK_DIVISOR} 細分化 (denom {oldEffDenom}→{newEffDenom}, 連番上限 {curNStep}→{curNStep + addedSteps})");

                                    // ① state を end-of-step{step-1} に巻き戻し
                                    lastSuccessCheckpoint.Restore(caseModel);

                                    // ② 結果リストを成功ステップ末尾までトリム (= step エントリ分残す。0-indexed で step 個)
                                    int keepCount = lastSuccessCheckpoint.StepIndex + 1;
                                    while (caseModel.AnalysisStepResults.Count > keepCount)
                                        caseModel.AnalysisStepResults.RemoveAt(caseModel.AnalysisStepResults.Count - 1);
                                    foreach (var n in caseModel.Nodes)
                                        while (n.NodeResults.Count > keepCount)
                                            n.NodeResults.RemoveAt(n.NodeResults.Count - 1);
                                    foreach (var b in caseModel.Beams)
                                        while (b.BeamResults.Count > keepCount)
                                            b.BeamResults.RemoveAt(b.BeamResults.Count - 1);
                                    foreach (var hs in caseModel.HorizontalSoilSprings)
                                        while (hs.HorizontalSpringResults.Count > keepCount)
                                            hs.HorizontalSpringResults.RemoveAt(hs.HorizontalSpringResults.Count - 1);
                                    if (caseModel.RotationalSprings != null)
                                        foreach (var rs in caseModel.RotationalSprings)
                                            while (rs.RotationalSpringResults.Count > keepCount)
                                                rs.RotationalSpringResults.RemoveAt(rs.RotationalSpringResults.Count - 1);

                                    // ③ ロード増分分母を ×M、連番上限を +addedSteps で更新
                                    effectiveDenom = newEffDenom;
                                    nStep = curNStep + addedSteps;

                                    // ④ 増分を新分母で書換 (累積側は保持)
                                    SetVectorDF(caseModel, loadCase, loadCombination, level, iLC, effectiveDenom, resetCumulative: false);
                                    caseModel.MapOnVectorDF();
                                    InitializeSoilDisplacementIncrement(caseModel, loadCase, loadCombination, level, isLiquefaction, effectiveDenom, resetCumulative: false);

                                    // ⑤ 進捗カウンタを追加ステップ分インクリメント
                                    System.Threading.Interlocked.Add(ref _bisectionExtraSteps, addedSteps);
                                    NotifyProgressPropertiesChanged();

                                    // ⑥ 予測器・トレンド検出履歴をクリア (増分が変わったので無効)
                                    prevStepDispIncrement = null;
                                    prevPrevStepDispIncrement = null;
                                    stepIterHistory.Clear();

                                    // ⑦ for-loop の step を 1 巻き戻し → post-incr で oldStep に戻り、失敗ステップを最初の substep として再実行
                                    step = oldStep - 1;

                                    cutbackCount++;
                                    continue;  // step++ → 次イテレーションで substep 開始
                                }

                                caseFailedThisAttempt = true;
                            }
                            else
                            {
                                string relaxedNote = effectiveAlpha > alpha ? $" (緩和基準α={effectiveAlpha:E2})" : "";
                                await AddLogAsync($"{caseTag}  → Converged in {n_iteration} iterations. Residual norm={caseModel.NormsROnNormsFint:E3}{relaxedNote}{dispInfo}");

                                // v28 D: プロファイリング情報をステップ収束時に出力
                                profStepTimer.Stop();
                                double _tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                                double _findKMs = profFindKCalls > 0 ? (profFindKTicks * _tickToMs) / profFindKCalls : 0.0;
                                double _solveMs = profLineSearchCalls > 0 ? (profSolveTicks * _tickToMs) / profLineSearchCalls : (profSolveTicks * _tickToMs);
                                double _lsMs = profLineSearchCalls > 0 ? (profLineSearchTicks * _tickToMs) / profLineSearchCalls : 0.0;
                                double _findTMs = profFindTTicks * _tickToMs;
                                double _lsTrialAvg = profLineSearchCalls > 0 ? profLineSearchTrialsTotal / (double)profLineSearchCalls : 0.0;
                                double _totalSec = profStepTimer.Elapsed.TotalSeconds;
                                string _solverTag = $"[{FEM.CsparseLinearSolver.LastSuccessfulSolver}]";
                                double _cscMs = FEM.CsparseLinearSolver.CscBuildTicks * _tickToMs;
                                double _factMs = FEM.CsparseLinearSolver.FactorizeTicks * _tickToMs;
                                double _backSubMs = FEM.CsparseLinearSolver.SolveBackSubTicks * _tickToMs;
                                long _cholReuse = FEM.CsparseLinearSolver.CholeskyReuseCount;
                                string lsOrFindT = profLineSearchCalls > 0
                                    ? $"LS {_lsMs:F0}ms×{profLineSearchCalls} (avg {_lsTrialAvg:F1})"
                                    : $"FindT {_findTMs:F0}ms";
                                await AddLogAsync(
                                    $"{caseTag}  ⏱ total {_totalSec:F1}s ┃ " +
                                    $"K組立 {_findKMs:F0}ms×{profFindKCalls} ┃ " +
                                    $"Solve {_solveMs:F0}ms {_solverTag} (CSC={_cscMs:F0} 分解={_factMs:F0} 代入={_backSubMs:F0} re={_cholReuse}) ┃ " +
                                    lsOrFindT);

                                // v19: 緩和基準が本来の許容値から大きく逸脱している場合は、再試行対象とする
                                // （長期未改善／停滞検出で緩和はしたが、物理的にはまだ収束していない状態）
                                // ただし最終試行でない場合に限る（最終試行では緩和収束を受け入れる）
                                const double RELAX_ACCEPT_THRESHOLD = 1e-2;  // 残差 1% 未満なら緩和収束を受け入れる

                                // v20 Phase 2: 物理的未収束の判定
                                // 緩和基準が極端に大きい（>10）かつ bisectionAttempt>=1 (既に 1 度倍増している)なら
                                // これ以上倍増しても収束する見込みなし → 中止（「耐力超過の可能性」として記録）
                                const double PHYSICALLY_UNCONVERGEABLE_THRESHOLD = 10.0;
                                if (effectiveAlpha > PHYSICALLY_UNCONVERGEABLE_THRESHOLD && bisectionAttempt >= 1)
                                {
                                    await AddLogAsync($"    ⛔ 緩和基準 {effectiveAlpha:E2} が物理的未収束閾値 {PHYSICALLY_UNCONVERGEABLE_THRESHOLD:E2} を超過。耐力超過の可能性があり、これ以上の倍増は無効と判断");
                                    await AddLogAsync($"    → このケースは物理的未収束として記録します（解析は続行）");
                                    physicallyUnconvergeable = true;
                                    caseFailedThisAttempt = false;  // 再試行しない
                                }
                                else if (effectiveAlpha > RELAX_ACCEPT_THRESHOLD && bisectionAttempt < MAX_STEP_BISECTIONS && !retryGateDisabled)
                                {
                                    await AddLogAsync($"    → 緩和基準 {effectiveAlpha:E2} が許容水準 {RELAX_ACCEPT_THRESHOLD:E2} を超過。ステップ分割を増やして再試行対象とします");
                                    caseFailedThisAttempt = true;
                                }
                            }

                            // v29 (2026-04-27): ステップ単位の収束サマリー記録 (解析終了時にレポート出力)
                            {
                                StepStatus _status = !converged ? StepStatus.Unconverged
                                    : (physicallyUnconvergeable ? StepStatus.PhysicallyUnconverged
                                        : StepStatus.Converged);
                                double _elapsedSec = profStepTimer.Elapsed.TotalSeconds;
                                _stepSummaries.Add(new StepSummary(
                                    CaseTag: caseTag,
                                    Level: level,
                                    LoadCaseNo: iLC + 1,
                                    ComboNo: iLCOM + 1,
                                    IsLiquefaction: isLiquefaction,
                                    Step: step + 1,
                                    NStep: nStep,
                                    BisectionAttempt: bisectionAttempt,
                                    Iterations: n_iteration > maxIterations ? maxIterations : n_iteration,
                                    FinalResidual: caseModel.NormsROnNormsFint,
                                    EffectiveAlpha: effectiveAlpha,
                                    MaxDisp: double.IsNaN(dispMaxAbs) ? 0.0 : dispMaxAbs,
                                    Status: _status,
                                    ElapsedSec: _elapsedSec,
                                    KRebuildCount: kRebuildCount,
                                    KReuseCount: kReuseCount));
                            }

                            // v21 Phase 3 prep: 自動ライン探索はステップ局所の effectiveUseLineSearch で
                            // 処理するため、インスタンスフィールド _useLineSearch の復元は不要

                            // v15/v23: このステップの変位増分を記録（次ステップの予測器用）
                            // 2 次外挿のため前々ステップの増分も保持する
                            if (vectorDAtStepStart != null && caseModel.VectorD != null)
                            {
                                prevPrevStepDispIncrement = prevStepDispIncrement;
                                prevStepDispIncrement = caseModel.VectorD - vectorDAtStepStart;
                            }

                            // デバッグ: 杭頭変位・M-θばねの確認
                            if (step == 0 || step == nStep - 1)
                            {
                                var actionPt = caseModel.Nodes[0];
                                // System.Diagnostics.Debug.WriteLine(
                                //     $"[Step{step}] ActionPoint Ux={actionPt.CumulativeDisp?.Ux:E3} Rx={actionPt.CumulativeDisp?.Rx:E3} Ry={actionPt.CumulativeDisp?.Ry:E3}");
                                foreach (var pile in InputModel.PileLayoutItems.Take(2))
                                {
                                    var rxy = caseModel.GetPileTopRotationalSpring(pile);
                                    var capNode = rxy?.NodeI;
                                    var pileHead = caseModel.GetPileNodes(pile)?.FirstOrDefault();
                                    double capRx = capNode?.CumulativeDisp?.Rx ?? 0;
                                    double pileRx = pileHead?.CumulativeDisp?.Rx ?? 0;
                                    double kRx = rxy?.KeTan?[3, 3] ?? -1;
                                    double springMx = rxy?.CumulativeForce?.Mxi ?? 0;
                                    // System.Diagnostics.Debug.WriteLine(
                                    //     $"[Step{step}] Pile{pile.No} " +
                                    //     $"CapRx={capRx:E3} PileRx={pileRx:E3} dRx={pileRx - capRx:E3} " +
                                    //     $"kRx={kRx:E3} CurveXY={(rxy?.CurveXY != null ? $"{rxy.CurveXY.Points.Count}pts" : "null")} " +
                                    //     $"KthetaXY={rxy?.KthetaXY:E3} " +
                                    //     $"SpringMxI={rxy?.CumulativeForce?.Mxi:E3} SpringMxJ={rxy?.CumulativeForce?.Mxj:E3} " +
                                    //     $"SpringMyI={rxy?.CumulativeForce?.Myi:E3} SpringMyJ={rxy?.CumulativeForce?.Myj:E3}");
                                }
                            }

                            caseModel.AnalysisStepResults.Add(new(loadCase, loadCombination, isLiquefaction, step, n_iteration, caseModel.NormsROnNormsFint));
                            foreach (var node in caseModel.Nodes)
                                node.NodeResults.Add(new(loadCase, loadCombination, isLiquefaction, step, node));
                            foreach (var beam in caseModel.Beams)
                                beam.BeamResults.Add(new(loadCase, loadCombination, isLiquefaction, step, beam));
                            foreach (var spring in caseModel.HorizontalSoilSprings)
                                spring.HorizontalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, spring));
                            //foreach (var rotationalSpring in caseModel.RotationalSprings)
                            //    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                            if (caseModel.RotationalSprings != null)
                            {
                                foreach (var rotationalSpring in caseModel.RotationalSprings)
                                {
                                    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                                    // else: この荷重ケースでは回転ばねは存在するが「使用されなかった」ため結果を保存しない
                                }
                            }

                            // v19: このステップが完了したことを記録
                            stepsExecutedInAttempt = step + 1;

                            // Phase 3: cut-back retry のため、収束したステップ終了時点を checkpoint として保持
                            // (フラグ off では capture しない — メモリ/CPU コストゼロ)
                            if (_useStepLevelCutback)
                            {
                                lastSuccessCheckpoint = FEM.StepCheckpoint.Capture(caseModel, step);
                            }

                            // v29 (2026-05-05): 退化トレンド検出 — 複合条件版
                            // 「直近 3 ステップで反復数が単調増加」AND「最新ステップが ≥ 60 反復」
                            // の両方を満たした場合のみ「真の退化トレンド」と判定して retry。
                            // 単に反復数が多いだけのケース (非線形性が強いだけのモデル) を誤検知しない。
                            //
                            // 履歴は閾値を効かせず全ステップ記録 (改善ゲート用)。
                            // 改善ゲート: retry 後の attempt で同条件発火しても、平均反復数が前 attempt 比
                            // 10% 以上改善していなければ細分化が無効と判断して以降抑制する。
                            const int TREND_OBS_STEPS = 3;
                            const int TREND_HIGH_ITER_THRESHOLD = 60;
                            const double RETRY_IMPROVEMENT_MIN_RATIO = 0.10;

                            stepIterHistory.Add(Math.Min(n_iteration, maxIterations));

                            if (stepIterHistory.Count >= TREND_OBS_STEPS
                                && !caseFailedThisAttempt && !physicallyUnconvergeable
                                && !retryGateDisabled
                                && bisectionAttempt < MAX_STEP_BISECTIONS)
                            {
                                int n = stepIterHistory.Count;
                                int latest = stepIterHistory[n - 1];
                                int prev = stepIterHistory[n - 2];
                                int prevPrev = stepIterHistory[n - 3];

                                bool monotonicIncrease = prevPrev < prev && prev < latest;
                                bool absoluteHigh = latest >= TREND_HIGH_ITER_THRESHOLD;

                                if (monotonicIncrease && absoluteHigh)
                                {
                                    if (bisectionAttempt == 0)
                                    {
                                        // 初回 attempt: 退化トレンド検出 → retry
                                        await AddLogAsync($"  🚨 退化トレンド検出: 反復数 [{prevPrev}→{prev}→{latest}] 単調増加 かつ 最新 ≥ {TREND_HIGH_ITER_THRESHOLD} → ステップ分割を増やして再試行");
                                        caseFailedThisAttempt = true;
                                    }
                                    else
                                    {
                                        // retry attempt: 改善ゲート — 平均反復数が前 attempt 比で十分改善していれば再 retry
                                        double currentAvg = stepIterHistory.Average();
                                        double improvement = prevAttemptAvgIter > 0 && double.IsFinite(prevAttemptAvgIter)
                                            ? (prevAttemptAvgIter - currentAvg) / prevAttemptAvgIter
                                            : 1.0;

                                        if (improvement >= RETRY_IMPROVEMENT_MIN_RATIO)
                                        {
                                            await AddLogAsync($"  🚨 退化トレンド検出 (retry {bisectionAttempt}/{MAX_STEP_BISECTIONS}): [{prevPrev}→{prev}→{latest}] 平均 {currentAvg:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) → さらに分割して再試行");
                                            caseFailedThisAttempt = true;
                                        }
                                        else
                                        {
                                            // 退化トレンド継続中だが改善が 10% 未満 → 細分化が無効と判断
                                            await AddLogAsync($"  ✋ 改善ゲート: 退化トレンド継続中だが平均反復数 {currentAvg:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) が最小改善率 {RETRY_IMPROVEMENT_MIN_RATIO * 100:F0}% 未満 → 以降の retry を抑制、現 nStep={nStep} で完遂");
                                            retryGateDisabled = true;
                                        }

                                        prevAttemptAvgIter = currentAvg;
                                    }
                                }
                            }

                            // v20 Phase 2: 物理的未収束なら直ちに中止（再試行しない）
                            if (physicallyUnconvergeable)
                            {
                                int remainingPhys = nStep - (step + 1);
                                if (remainingPhys > 0)
                                {
                                    await AddLogAsync($"  ⛔ 物理的未収束のため残り {remainingPhys} ステップをスキップ");
                                    // v25: 未実行ステップ分を総ステップ数から差し引き、進捗バーを 100% 到達させる
                                    System.Threading.Interlocked.Add(ref _bisectionExtraSteps, -remainingPhys);
                                    NotifyProgressPropertiesChanged();
                                }
                                break;
                            }

                            // v19: 早期脱出 - 既に再試行対象と判定されており、再試行回数に余裕がある場合
                            // 残りステップは truncate で破棄されるので計算時間の無駄。即座に再試行へ移行する
                            // （再試行時は後段の _bisectionExtraSteps 調整で計上/相殺されるので、ここでは減算しない）
                            if (caseFailedThisAttempt && bisectionAttempt < MAX_STEP_BISECTIONS)
                            {
                                int remaining = nStep - (step + 1);
                                if (remaining > 0)
                                {
                                    await AddLogAsync($"  ⏩ 残り {remaining} ステップをスキップして再試行へ移行");
                                }
                                break;
                            }
                        }

                        // v19: ケース完了判定と再試行ロジック
                        // v20 Phase 2: 物理的未収束が検出された場合は即中止（再試行しない）
                        if (physicallyUnconvergeable)
                        {
                            await AddLogAsync($"  ⛔ 物理的未収束として確定（耐力超過の可能性）。このケースは再試行せず次へ進みます");
                            break;  // 諦めて次のケースへ
                        }
                        if (!caseFailedThisAttempt)
                        {
                            caseConverged = true;
                            break;  // retry while-loop を抜けて次のケースへ
                        }
                        if (bisectionAttempt >= MAX_STEP_BISECTIONS)
                        {
                            await AddLogAsync($"  ❌ 最大再試行回数 ({MAX_STEP_BISECTIONS}) 到達。このケースは未収束で確定 (最終 nStep={nStep})");
                            // v25: 最終アテンプトで未実行に終わったステップ分を総ステップ数から差し引く
                            int remainingAtMax = nStep - stepsExecutedInAttempt;
                            if (remainingAtMax > 0)
                            {
                                System.Threading.Interlocked.Add(ref _bisectionExtraSteps, -remainingAtMax);
                                NotifyProgressPropertiesChanged();
                            }
                            break;  // 諦めて次のケースへ
                        }

                        // 失敗アテンプトの結果を巻き戻し
                        while (caseModel.AnalysisStepResults.Count > snapAnaStepResults)
                            caseModel.AnalysisStepResults.RemoveAt(caseModel.AnalysisStepResults.Count - 1);
                        for (int i_ = 0; i_ < caseModel.Nodes.Count; i_++)
                            while (caseModel.Nodes[i_].NodeResults.Count > snapNodeResults[i_])
                                caseModel.Nodes[i_].NodeResults.RemoveAt(caseModel.Nodes[i_].NodeResults.Count - 1);
                        for (int i_ = 0; i_ < caseModel.Beams.Count; i_++)
                            while (caseModel.Beams[i_].BeamResults.Count > snapBeamResults[i_])
                                caseModel.Beams[i_].BeamResults.RemoveAt(caseModel.Beams[i_].BeamResults.Count - 1);
                        for (int i_ = 0; i_ < caseModel.HorizontalSoilSprings.Count; i_++)
                            while (caseModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count > snapHSpringResults[i_])
                                caseModel.HorizontalSoilSprings[i_].HorizontalSpringResults.RemoveAt(caseModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count - 1);
                        if (caseModel.RotationalSprings != null && snapRotSpringResults != null)
                        {
                            for (int i_ = 0; i_ < caseModel.RotationalSprings.Count; i_++)
                                while (caseModel.RotationalSprings[i_].RotationalSpringResults.Count > snapRotSpringResults[i_])
                                    caseModel.RotationalSprings[i_].RotationalSpringResults.RemoveAt(caseModel.RotationalSprings[i_].RotationalSpringResults.Count - 1);
                        }

                        // v19: 総ステップ数の調整
                        // baseline は旧 nStep (=oldNStep) を計上済み
                        // 実際にこのアテンプトで実行したのは stepsExecutedInAttempt ステップ
                        // 次のアテンプトで新 nStep ステップを実行する
                        // → 調整 = (実行済 + 新 nStep) - 旧 nStep = stepsExecutedInAttempt + newNStep - oldNStep
                        //
                        // 2026-05-06 cut-back 対応: cut-back で nStep が膨張した状態 (例 16→23) で
                        // ×2 すると 46 になり、cut-back の効果と複合して指数的に成長する問題があった。
                        // 修正: 標準 retry では baseNStep × 2^attempt で常に「クリーンな」 nStep を使う。
                        // cut-back 内の膨張は今回 attempt 内で完結し、次 attempt には引き継がない。
                        int oldNStep = nStep;
                        bisectionAttempt++;
                        nStep = baseNStep * (1 << bisectionAttempt);  // 2^attempt × base (16,32,64,128,…)
                        System.Threading.Interlocked.Add(ref _bisectionExtraSteps, stepsExecutedInAttempt + nStep - oldNStep);
                        NotifyProgressPropertiesChanged();
                    }  // end retry while-loop

                    _ = caseConverged; // 抑制: 未使用警告（将来診断に利用する可能性）

                        // NaN診断: 荷重ケース完了
                        // FEM.NaNDiagnostics.End();

                        // E3c-2: caseModel (DeepCopy) で蓄積した結果を targetModel (主モデル) に merge
                        // E3c-3: 複数ワーカーが targetModel の ObservableCollection に append する
                        // 可能性があるため lock で atomic 化。逐次モードでは no-op。
                        lock (_caseMergeLock)
                        {
                            AnaModel.AppendCaseResultsToMain(
                                targetModel, caseModel,
                                caseSnapAnaStepResults,
                                caseSnapNodeResultCounts,
                                caseSnapBeamResultCounts,
                                caseSnapHSpringResultCounts,
                                caseSnapRotSpringResultCounts);
                        }
                        } // end try
                        finally
                        {
                            // 並列モニタ: ケース終了 (正常/例外/キャンセルいずれも) で Active から除去し Completed++
                            // BeginInvoke でワーカーをブロックせずに UI 更新を要求
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                ActiveCases.Remove(monitorItem);
                                ActiveCasesCount = ActiveCases.Count;
                                CompletedCaseCount++;  // setter 内で PendingCaseCount PropertyChanged も発火
                            });
                            // 診断ログ (hang 調査用): ケース完了時刻と所要時間を記録
                            if (_caseMDOP > 1)
                            {
                                double elapsedSec = (DateTime.UtcNow - _diagCaseStartUtc).TotalSeconds;
                                int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                                await AddLogAsync($"{caseTag} ⚪ 完了 (Tid={tid}, {elapsedSec:F1}s, Active={ActiveCasesCount - 1})");
                            }
                        }
                        } // end local function RunThisCaseAsync

                        // E3c-3-enable: MDOP>1 なら Task.Run に投げ semaphore で throttle、
                        // MDOP=1 なら直接 await して逐次挙動を維持
                        if (_caseSemaphore != null)
                        {
                            await _caseSemaphore.WaitAsync(token);
                            _caseTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                try { await RunThisCaseAsync(); }
                                finally { _caseSemaphore.Release(); }
                            }, token));
                        }
                        else
                        {
                            await RunThisCaseAsync();
                        }
                    }
                }
            }

            // E3c-3-enable: 並列投入された全ケースの完了を待つ (MDOP=1 では _caseTasks は空)
            if (_caseTasks.Count > 0)
            {
                await System.Threading.Tasks.Task.WhenAll(_caseTasks);
            }
            }
            finally
            {
                // E3c-3: MathNet 並列度を元に戻す + semaphore 解放
                MathNet.Numerics.Control.MaxDegreeOfParallelism = _origMathNetMDOP;
                _caseSemaphore?.Dispose();
                // hang 対策 (2026-04-26): MKL/OMP 環境変数を元に戻す
                if (_caseMDOP > 1)
                {
                    Environment.SetEnvironmentVariable("MKL_NUM_THREADS", _origMklNumThreads);
                    Environment.SetEnvironmentVariable("OMP_NUM_THREADS", _origOmpNumThreads);
                }
            }

            token.ThrowIfCancellationRequested();

            // === LastRunConfig 更新 (追加実行の互換性比較に使用) ===
            // 解析が正常完了した場合のみ更新。中断/エラーは既存値を保持。
            if (preTargetModel != null)
            {
                lock (_caseMergeLock)
                {
                    var snap = CaptureCurrentRunSnapshot();
                    snap.ExecutedCaseKeys = preTargetModel.AnalysisStepResults
                        .Select(r => new FEM.AnalysisRunSnapshot.CaseKey(
                            r.LoadCase.LoadName, r.LoadCombination.Name, r.IsLiquefaction))
                        .Distinct()
                        .ToList();
                    preTargetModel.LastRunConfig = snap;
                }
                RefreshCompletedCaseKeys();
            }

            // v29: ステップ単位の収束サマリーをレポート出力
            await OutputStepSummaryReport();

            // v29: 経過時間タイマー停止 (これ以降は最終値で固定表示)
            StopElapsedTimer();
            OnPropertyChanged(nameof(ElapsedTimeText));
            OnPropertyChanged(nameof(EstimatedRemainingText));

            await AddLogAsync("計算終了");

            // 並列モニタを閉じる (開いていれば)
            System.Windows.Application.Current?.Dispatcher.Invoke(() => RequestHideParallelMonitor?.Invoke());

            // 検定比の計算（解析完了後に一括処理）- 未完成のため一時無効化
            try
            {
                //ComputeCapacityRatiosForAllResults(targetModel);
                //await AddLogAsync("検定比の計算完了");
            }
            catch (Exception ex)
            {
                await AddLogAsync($"検定比の計算でエラー: {ex.Message}");
            }

            // ペナルティばねの精度検証
            await VerifyPenaltySpringAccuracy(targetModel);

            // 最終進捗を報告（100%完了）
            progress?.Report(new Models.AnalysisProgress
            {
                Percentage = 100,
                CurrentStep = "解析計算が完了しました",
                CurrentStepNumber = TotalCalculationCount,
                TotalSteps = TotalCalculationCount,
                StartTime = startTime
            });

            // Pass logs to MainWindowViewModel & Ribbon Tab selection
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // 2026-05-06: サマリーレポートがメインウィンドウのログに反映されない問題対策。
                        // OutputStepSummaryReport は AddLogAsync でキューに追加するだけ (Timer 駆動 flush)。
                        // SetLatestAnalysisLogs 時点で _logQueue に未 flush 行が残っていると欠落するため、
                        // ここで同期的にキューをドレインして CalculationLog に確実に反映してから渡す。
                        while (_logQueue.TryDequeue(out var line))
                        {
                            CalculationLog.Add(line);
                            if (_logTextBuilder.Length > 0) _logTextBuilder.Append(Environment.NewLine);
                            _logTextBuilder.Append(line);
                        }
                        _cachedLogText = _logTextBuilder.ToString();
                        OnPropertyChanged(nameof(CalculationLogText));

                        _mainWindowViewModel.SetLatestAnalysisLogs(CalculationLog);

                        if (Application.Current.MainWindow is PileDesign.Views.MainWindow mainWin)
                        {
                            // 明示的にタブを選択
                            mainWin.AnalysisResultRibbonTab.IsSelected = true;

                            // 必要ならフォーカスも当てる（視覚的に選択状態を確実にする）
                            mainWin.AnalysisResultRibbonTab.Focus();

                            // 表示用テーブルを最新化（MainWindowViewModel のメソッドを呼ぶ）
                            if (mainWin.DataContext is PileDesign.ViewModels.MainWindowViewModel mwvm)
                                mwvm.RefreshResultTablesFromLastStep();
                        }
                    }
                    catch
                    {
                        // UI 更新失敗は非致命。
                    }
                });
            }
            catch
            {
                // Dispatcher が利用できない場面は無視
            }

        }

        // RunAsync 内の荷重ケース処理の先頭に以下ヘルパを呼ぶか、そのまま挿入してください。
        // 杭頭回転角helper を別メソッドとして定義する例を示します。
        /// <summary>
        /// 解析完了後に全BeamResultの検定比を一括計算する
        /// </summary>
        /*
        private void ComputeCapacityRatiosForAllResults(AnaModel targetModel)
        {
            // SoilPile（杭要素分割後）をPileBodyNoで検索するための辞書
            var soilPileDict = new Dictionary<int, Models.InputData.SoilPile>();
            if (InputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in InputModel.ElementDivision.SoilPiles)
                {
                    if (sp.PileBodyNo > 0 && !soilPileDict.ContainsKey(sp.PileBodyNo))
                        soilPileDict[sp.PileBodyNo] = sp;
                }
            }

            // beam → PileLayoutDataItem のマッピング
            var beamToPile = new Dictionary<Beam, Models.InputData.PileLayoutDataItem>();
            if (InputModel.PileLayoutItems != null)
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    if (pile.Beams == null) continue;
                    foreach (var b in pile.Beams)
                        beamToPile[b] = pile;
                }
            }

            // 第1パス: 杭ごと・荷重ケースごとの maxM, maxQ を集計
            // キー: (PileLayoutDataItem, LoadCase) → (maxM [kNm], maxQ [kN])
            var maxForces = new Dictionary<(Models.InputData.PileLayoutDataItem pile, LoadCase lc), (double maxM, double maxQ)>();

            foreach (var pile in InputModel.PileLayoutItems ?? [])
            {
                if (pile.Beams == null) continue;
                foreach (var beam in pile.Beams)
                {
                    foreach (var result in beam.BeamResults)
                    {
                        var force = result.CumulativeForce;
                        if (force == null || result.LoadCase == null) continue;
                        var key = (pile, result.LoadCase);
                        double m = force.MabsMax; // max(Mi, Mj)
                        double q = force.FabsMax; // max(Fi, Fj)
                        if (maxForces.TryGetValue(key, out var prev))
                            maxForces[key] = (Math.Max(prev.maxM, m), Math.Max(prev.maxQ, q));
                        else
                            maxForces[key] = (m, q);
                    }
                }
            }

            // 第2パス: 検定比計算（MonQdを反映したQN曲線を使用）
            foreach (var beam in targetModel.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                    continue;
                if (!soilPileDict.TryGetValue(pb, out var soilPile))
                    continue;
                if (seg < 0 || seg >= soilPile.PileBodySegments.Count)
                    continue;
                var pileSection = soilPile.PileBodySegments[seg].PileSection;
                if (pileSection == null) continue;

                beamToPile.TryGetValue(beam, out var pileItem);

                foreach (var result in beam.BeamResults)
                {
                    var force = result.CumulativeForce;
                    if (force == null) continue;

                    int lcLevel = result.LoadCase?.Level ?? 0;
                    int lcNo = result.LoadCase?.No ?? 0;
                    if (lcLevel < 1 || lcLevel > 2 || lcNo < 1) continue;

                    // 軸力
                    double axialN_kN = 0.0;
                    if (pileItem != null)
                    {
                        try { axialN_kN = pileItem.GetSeismicAxialForce(lcNo, lcLevel); }
                        catch { axialN_kN = pileItem.AxialForce; }
                    }

                    // MonQd = maxM / (maxQ × d)
                    double monQd = 3.0; // フォールバック
                    double d = pileSection.EffectiveDepth; // [mm]
                    if (pileItem != null && result.LoadCase != null && d > 0)
                    {
                        var key = (pileItem, result.LoadCase);
                        if (maxForces.TryGetValue(key, out var mf) && mf.maxQ > 0)
                        {
                            // maxM [kNm] → [Nmm] = ×1e6, maxQ [kN] → [N] = ×1e3, d [mm]
                            monQd = (mf.maxM * 1e6) / (mf.maxQ * 1e3 * d);
                        }
                    }

                    // MonQdでQN曲線を再計算
                    var qnCurves = pileSection.ComputeQNForMonQd(monQd);
                    (List<double> N, List<double> Q) unfNQ, facNQ;
                    if (qnCurves.UnfactoredService.N != null)
                    {
                        unfNQ = lcLevel == 1 ? qnCurves.UnfactoredDamage : qnCurves.UnfactoredUltimate;
                        facNQ = lcLevel == 1 ? qnCurves.FactoredDamage : qnCurves.FactoredUltimate;
                    }
                    else
                    {
                        // 場所打ち杭等: キャッシュ値にフォールバック
                        unfNQ = lcLevel == 1 ? pileSection.UnfactoredDamageNQ : pileSection.UnfactoredUltimateNQ;
                        facNQ = lcLevel == 1 ? pileSection.FactoredDamageNQ : pileSection.FactoredUltimateNQ;
                    }

                    var ratios = FEM.Utils.ComputeCapacityRatios(force, pileSection, lcLevel, axialN_kN, unfNQ, facNQ);
                    result.AxialForceForRatio = axialN_kN;
                    result.MonQdForRatio = d > 0 ? monQd : -1;
                    result.UnfactoredMiRatio = ratios.UnfactoredMiRatio;
                    result.FactoredMiRatio = ratios.FactoredMiRatio;
                    result.UnfactoredQiRatio = ratios.UnfactoredQiRatio;
                    result.FactoredQiRatio = ratios.FactoredQiRatio;
                    result.UnfactoredMjRatio = ratios.UnfactoredMjRatio;
                    result.FactoredMjRatio = ratios.FactoredMjRatio;
                    result.UnfactoredQjRatio = ratios.UnfactoredQjRatio;
                    result.FactoredQjRatio = ratios.FactoredQjRatio;
                }
            }
        }
        */

        private void ApplyPileHeadRigidBindingForLoadCase(AnaModel targetModel, LoadCase loadCase)
        {
            // PileNode-0 には境界条件を設定しない。
            // 接続構造:
            //   ActionPoint (master) → RigidBody[0] → CapNode (slave)
            //                                              ↕ RotationalSpring
            //                                          PileNode-0 (常に自由)
            //
            // PileNode-0 の並進は RotationalSpring のペナルティ剛性 (Kbig=1e10) で CapNode に追従。
            // PileNode-0 の回転は RotationalSpring の M-θ曲線 or 高回転剛性で決まる。
            // 非線形ON/OFFの切替は SetupNonlinearMThetaForLoadCase で
            // RotationalSpring の回転剛性を変更することで行う。

            if (targetModel?.RigidBodies == null || targetModel.RigidBodies.Count == 0) return;

            // 変更を反映：転送行列等を更新
            targetModel.SetSlaveNodes();
        }


        // 接線剛性用: 端部回転から要素中央曲率を評価し、dM/dφ を EI_eff として KTan（倍率）に反映
        // useRelaxation=false: Full NR（正確なヤコビアンで2次収束）
        // useRelaxation=true:  Modified NR の初期反復（安定化のためダンピング）
        private static void UpdateBeamMPhiTangent(AnaModel model, bool useRelaxation = false)
            => UpdateBeamMPhi(model, isTangent: true, useRelaxation: useRelaxation);

        // 割線剛性用（必要なら接線と同手順でKsecも更新）
        private static void UpdateBeamMPhiSecant(AnaModel model) => UpdateBeamMPhi(model, isTangent: false);

        // 統合されたM-φ更新メソッド: 接線剛性と割線剛性の両方に対応
        private static void UpdateBeamMPhi(AnaModel model, bool isTangent, bool useRelaxation = false)
        {
            int beamIdx = 0;
            foreach (var beam in model.Beams)
            {
                beamIdx++;
                bool hasCurve = beam.ResolvedCombinedCurve != null;
                bool hasMaterial = beam.Section?.Material != null;
                if (beamIdx <= 3)  // 最初の3本だけ詳細出力
                {
                }
                // 端部変位（全体）→要素座標系
                var dI = beam.NodeI.CumulativeDisp.GetVector();
                var dJ = beam.NodeJ.CumulativeDisp.GetVector();
                var disp = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(dI.Count + dJ.Count);
                disp.SetSubVector(0, dI.Count, dI);
                disp.SetSubVector(dI.Count, dJ.Count, dJ);

                var T = PileDesign.FEM.Utils.GetTransformMatrix(beam.NodeI, beam.NodeJ);
                var d = T * disp;

                // 端部回転（要素座標）: Ry(i)=d[4], Rz(i)=d[5], Ry(j)=d[10], Rz(j)=d[11] とみなす
                double thetaYi = d[4], thetaYj = d[10];
                double thetaZi = d[5], thetaZj = d[11];
                double L = Math.Max(beam.Length, 1e-12);

                double phiY = (thetaYj - thetaYi) / L;
                double phiZ = (thetaZj - thetaZi) / L;

                // 接線剛性の場合は dM/dφ、割線剛性の場合は M/φ を使用
                var (EIy_eff, EIz_eff) = isTangent
                    ? beam.EvaluateEIeff(phiY, phiZ)
                    : beam.EvaluateEIeffSecant(phiY, phiZ);

                // 初期 EI（断面から計算）
                double EI0y = beam.Section.Material.E * beam.Section.IY;
                double EI0z = beam.Section.Material.E * beam.Section.IZ;

                // 曲線の初期接線剛性を基準にして ratio を計算
                // これにより、φ→0 では ratio=1.0 となり、曲率増加で ratio<1.0 となる
                double EI_base = beam.InitialCurveTangent;
                bool useCurveBase = (EI_base > 1e-6);

                // 倍率に変換（数値安定化のため上下限）
                // v10: 下限を 0.05 (5%) に設定して数値安定性を確保
                // 長い杭・多要素の場合に剛性が低すぎると振動の原因になる
                const double RATIO_MIN = 0.05;
                double ratioY, ratioZ;

                // デバッグ: E*I と EI_base の比較（初回のみ）
                if (beamIdx == 0 && isTangent)
                {
                }

                // v10: 常にE*Iを基準にしてratioを計算
                // SetKeでは EI_used = E*I * ratio なので、
                // ratio = EI_sec / E*I とすることで EI_used = EI_sec となる
                ratioY = (double.IsNaN(EIy_eff) || EI0y <= 0) ? 1.0 : Math.Clamp(EIy_eff / EI0y, RATIO_MIN, 1.0);
                ratioZ = (double.IsNaN(EIz_eff) || EI0z <= 0) ? 1.0 : Math.Clamp(EIz_eff / EI0z, RATIO_MIN, 1.0);

                // 要素中央の曲率を保存（合成値）- 接線/割線の両方で更新
                double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
                beam.CurrentCurvature = phiRes;

                // 要素中央のモーメント（M-φ曲線から直接評価）
                if (beam.ResolvedCombinedCurve != null)
                {
                    beam.CurrentMoment = beam.ResolvedCombinedCurve.EvaluateMoment(phiRes);
                    // v28 問題 A 診断: M-φ セグメントインデックス (接線更新時のみ、毎反復 1 回記録)
                    if (isTangent)
                    {
                        beam.CurrentMPhiSegmentIndex = beam.ResolvedCombinedCurve.GetSegmentIndex(phiRes);
                    }
                }

                if (isTangent)
                {
                    if (useRelaxation)
                    {
                        // Modified NR の初期反復: 緩和係数で安定性を確保
                        const double RELAXATION = 0.3;
                        double prevKy = beam.KTan_y;
                        double prevKz = beam.KTan_z;
                        double newKy = (prevKy > 0.01) ? prevKy * (1 - RELAXATION) + ratioY * RELAXATION : ratioY;
                        double newKz = (prevKz > 0.01) ? prevKz * (1 - RELAXATION) + ratioZ * RELAXATION : ratioZ;
                        beam.KTan_y = newKy;
                        beam.KTan_z = newKz;
                    }
                    else
                    {
                        // Full NR: 正確なヤコビアン（2次収束に必要）
                        beam.KTan_y = ratioY;
                        beam.KTan_z = ratioZ;
                    }
                    beam.SetKe(true); // KeTan 再構築
                }
                else
                {
                    // 割線剛性: 緩和なし（正確な値を使用）
                    // M(φ)/φ は常に正値で滑らかに変化するため、緩和は不要。
                    // 緩和(0.5)は内力の不正確さを生み、大変形時の収束を著しく遅延させる。
                    beam.KSec_y = ratioY;
                    beam.KSec_z = ratioZ;
                    beam.SetKe(false); // KeSec 再構築
                }
            }
        }

        //private static (IList<double> Phis, IList<double> Moments)? TryCallMPhiRelationship(object pileSection, double axialN)
        //{
        //    if (pileSection == null) return null;
        //    var t = pileSection.GetType();
        //    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        //    // 修正: 小文字p→大文字P の順でフォールバック
        //    var mi = t.GetMethod("GetMPhiRelationship", flags)
        //          ?? t.GetMethod("GetMPhiRelationship", flags);
        //    if (mi == null) return null;

        //    object? ret;
        //    try
        //    {
        //        ret = mi.GetParameters().Length switch
        //        {
        //            1 => mi.Invoke(pileSection, new object[] { axialN }),
        //            2 => mi.Invoke(pileSection, new object[] { axialN, 1.0 }),
        //            _ => null
        //        };
        //    }
        //    catch { return null; }
        //    if (ret == null) return null;

        //    var rt = ret.GetType();
        //    var item1 = rt.GetProperty("Item1")?.GetValue(ret) as System.Collections.IEnumerable;
        //    var item2 = rt.GetProperty("Item2")?.GetValue(ret) as System.Collections.IEnumerable;
        //    if (item1 == null || item2 == null) return null;

        //    var phis = item1.Cast<object>().Select(Convert.ToDouble).ToList();
        //    var ms = item2.Cast<object>().Select(Convert.ToDouble).ToList();
        //    if (phis.Count >= 2 && phis.Count == ms.Count) return (phis, ms);
        //    return null;
        //}

        // 現ステップの「各杭の軸力」を用いて、対応する全梁の M–φ（合成）を解決してセット
        private void SetupMPhiByCurrentAxialForMiddleBeam(AnaModel model)
        {
            if (model == null) return;

            // SoilPileをPileBodyNoでキャッシュ（初期M-φ設定と同じマッチ済みセグメントを使用）
            var soilPileByPileBodyNo = new Dictionary<int, SoilPile>();
            if (InputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in InputModel.ElementDivision.SoilPiles)
                {
                    soilPileByPileBodyNo.TryAdd(sp.PileBodyNo, sp);
                }
            }

            foreach (var pile in InputModel.PileLayoutItems)
            {
                // 現ステップの解析軸力 [kN]
                // pile.AxialForce / model.GetAxialForce は kN 単位 (UI 入力, SetAxialForce コメント,
                // AxialForceLevel{1,2}s 全て kN)。PileSection.GetMPhiRelationship も kN を期待。
                // 旧実装は誤って /1000.0 で「N→kN 変換」していたため、軸力が 1/1000 で
                // M-φ が 24% 程度過小評価される単位バグがあった (検証: PileSectionMPhiUnitTests)。
                double axialN_kN = model.GetAxialForce(pile);

                int pb = pile.PileBodyNo;
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) continue;

                foreach (var beam in model.GetPileBeams(pile))
                {
                    if (beam.SegmentIndex is not int seg) continue;
                    // SoilPile.PileBodySegments はマッチ済み（要素ごとに1エントリ）
                    if (seg < 0 || seg >= soilPile.PileBodySegments.Count) continue;

                    var section = soilPile.PileBodySegments[seg].PileSection;
                    if (section == null) continue;

                    // 場所打ち鋼管コンクリート杭: 杭頭部と杭中間部で異なるM-φを適用
                    (IList<double> Phis, IList<double> Moments)? curve;
                    if (!beam.IsPileTop
                        && section.PileBodyType == "場所打ち鋼管コンクリート杭"
                        && section.PileSectionType == "鋼管コンクリート部")
                    {
                        var sprcSection = new InsituSteelPipeReinforcedConcreteSection(
                            new InsituSteelPipe(section.PipeGrade, section.PipeDia, section.PipeTs, section.CorrosionDepth),
                            new InsituConcrete(section.ConcreteOutDia, section.ConcreteGsi, section.ConcreteFc),
                            new MainBars(section.MainBarDr, section.MainBarNum, section.MainBarSpec, section.MainBarSize));
                        var middle = sprcSection.GetMPhiRelationshipForMiddle(axialN_kN * 1000.0);
                        var phisConverted = middle.Phis.Select(p => p * 1000.0).ToList();
                        var msConverted = middle.Moments.Select(m => m * 1e-6).ToList();
                        curve = ((IList<double>)phisConverted, (IList<double>)msConverted);
                    }
                    else
                    {
                        curve = TryCallMPhiRelationship(section, axialN_kN);
                    }

                    if (curve is null) continue;
                    beam.SetResolvedCombinedMPhi(curve.Value.Phis, curve.Value.Moments);
                }
            }
        }

        /// <summary>
        /// DOF キー "InputNode-6:Ry" / "杭節点-3-2:Ux" 等から DOF 種別の接尾辞 ("Ry" / "Ux") を抽出。
        /// flip カウントを DOF 種別単位で集計するために使用 (リミットサイクルが
        /// 複数の同種ノード間で起きた場合にもトリガできるようにする)。
        /// </summary>
        private static string ExtractDofType(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int colonIdx = key.LastIndexOf(':');
            return colonIdx >= 0 && colonIdx < key.Length - 1
                ? key.Substring(colonIdx + 1)
                : key;
        }

        // UIスレッドでログを追加
        private Task AddLogAsync(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            _logQueue.Enqueue($"[{timestamp}] {message}");
            StartLogTimerIfNeeded();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 杭が「引張定着筋なし」の杭頭半剛接合工法 (キャプテン/F.T.Pile/キャプリング) を採用しているかを判定。
        /// 該当する場合は工法名を返し、そうでなければ null を返す。引張軸力時に最大抵抗モーメント Mu = 0 と
        /// なるため、入力軸力 / 解析軸力が引張に転じた場合に注意喚起すべきケースの判別に使用する。
        /// </summary>
        public static string? GetSemiRigidWithoutTensionBarPileTopName(PileBodyInput pileBody)
        {
            if (pileBody == null) return null;
            var top = pileBody.PileTopType;
            if (string.IsNullOrEmpty(top)) return null;
            if (top.Contains("キャプテンパイル工法"))
            {
                bool has = pileBody.PileTop?.CaptainPile?.CTPTensionRebars?.HasTensionRebars ?? false;
                return has ? null : "キャプテンパイル工法";
            }
            if (top.Contains("FT-Pile構法"))
            {
                bool has = pileBody.PileTop?.FTPile?.FTPileTensionBars?.IsTensionType ?? false;
                return has ? null : "FT-Pile構法";
            }
            if (top.Contains("キャプリングパイル工法"))
            {
                bool has = pileBody.PileTop?.CapringPile?.HasTensionBars ?? false;
                return has ? null : "キャプリングパイル工法";
            }
            return null;
        }

        /// <summary>
        /// 当該荷重ケースで「引張定着筋なし半剛接合 (キャプテン/F.T.Pile/キャプリング) かつ
        /// 入力軸力が引張」となる杭の番号一覧を返す。case-local モデルに対する Uz 軸剛性解放
        /// (ApplyAxialReleaseAtPileHeads) の対象選定に使用する。
        ///
        /// 注: UseAnalysisAxialForce=true (入力 + 解析結果モード) であっても、判定は入力軸力
        /// (常時 + L1/L2 地震時) のみを根拠とする。軸力モードに応じた解析中ステップ別動的解放は
        /// 方程式番号の再構築が必要で実装コストが高いため v1 では非対応。
        /// </summary>
        private List<int> GetPileNosForAxialReleaseInCase(LoadCase loadCase)
        {
            var result = new List<int>();
            if (InputModel?.PileLayoutItems == null || InputModel.PileBodies == null)
                return result;
            if (loadCase == null) return result;

            foreach (var pile in InputModel.PileLayoutItems)
            {
                int idx = pile.PileBodyNo - 1;
                if (idx < 0 || idx >= InputModel.PileBodies.Count) continue;
                var pileBody = InputModel.PileBodies[idx];
                if (GetSemiRigidWithoutTensionBarPileTopName(pileBody) == null) continue;

                // 当該ケースの入力軸力 (kN, 圧縮を正とする符号規約)
                double n_kN;
                try
                {
                    n_kN = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                }
                catch
                {
                    n_kN = pile.AxialForceVL;
                }
                if (n_kN < 0) result.Add(pile.No);
            }
            return result;
        }

        /// <summary>
        /// 引張定着筋なしの半剛接合杭で軸力が引張になっている場合、NG として一度だけログに残す。
        /// 1 ケース内で複数ステップにわたって引張が継続しても、各杭につき最初の発生のみを記録する。
        /// </summary>
        private async Task LogTensionForSemiRigidPilesAsync(
            AnaModel caseModel, string caseTag, int stepDisplay, int nStepDisplay,
            HashSet<int> loggedPileNos)
        {
            if (InputModel?.PileLayoutItems == null || InputModel.PileBodies == null) return;
            foreach (var pile in InputModel.PileLayoutItems)
            {
                if (loggedPileNos.Contains(pile.No)) continue;
                int idx = pile.PileBodyNo - 1;
                if (idx < 0 || idx >= InputModel.PileBodies.Count) continue;
                var pileBody = InputModel.PileBodies[idx];
                var typeName = GetSemiRigidWithoutTensionBarPileTopName(pileBody);
                if (typeName == null) continue;
                double n_kN = caseModel.GetAxialForce(pile);
                if (n_kN < 0)
                {
                    loggedPileNos.Add(pile.No);
                    await AddLogAsync(
                        $"  NG (引張軸力): 杭No.{pile.No} ({typeName}, 引張定着筋なし) " +
                        $"N={n_kN:N1}kN — 杭頭を「軸剛性 0、曲げ剛性 0」で解析 " +
                        $"({caseTag} ステップ{stepDisplay}/{nStepDisplay})");
                }
            }
        }

        // 荷重ベクトルの更新メソッド　F = F + dF 
        private void UpdateF(AnaModel targetModel)
        {

            //AnaModel.MapOnVectorF();  // node.load, による F 更新
            targetModel.Nodes[0].UpdateCumulativeLoad(); // (kN) 節点荷重の代表節点へのセット

            targetModel.UpdateVectorF(); // 節点荷重の更新
            targetModel.MapOnVectorF();  // node.load, による F 更新

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                // E3b: case-local AxialForce / AxialForceIncrement 経由で update
                double current = targetModel.GetAxialForce(pileLayoutItem);
                double increment = targetModel.GetAxialForceIncrement(pileLayoutItem);
                targetModel.SetAxialForce(pileLayoutItem, current + increment); // 杭軸力の更新 [kN]
            }
        }

        /// <summary>
        /// 入力値＋応力解析結果モード: 各杭の杭頭Beam要素のFxi（解析結果）を入力軸力に加算する
        /// 入力値は圧縮が正、解析値Fxiは圧縮が負なので、符号を反転して加算
        /// </summary>
        private void UpdateAxialForceFromAnalysis(AnaModel targetModel)
        {
            if (targetModel.Beams == null || InputModel.PileLayoutItems == null) return;

            foreach (var pile in InputModel.PileLayoutItems)
            {
                // この杭の杭頭Beam要素を検索（各杭の最上段要素）
                var topBeam = targetModel.Beams.FirstOrDefault(b =>
                    b.IsPileHeadElement &&
                    b.NodeI != null &&
                    Math.Abs(b.NodeI.Coord.X - pile.Point3D.X) < 0.01 &&
                    Math.Abs(b.NodeI.Coord.Y - pile.Point3D.Y) < 0.01);

                if (topBeam?.CumulativeForce == null) continue;

                // Fxi: ローカル座標系の軸力（圧縮が負）
                double fxiAnalysis = topBeam.CumulativeForce.Fxi; // kN（ローカル軸方向）

                // 入力軸力（圧縮が正）に解析結果（圧縮が負）を加算 → 符号反転
                // AxialForce = 入力値による軸力 + (-Fxi_analysis)
                // E3b: CaseLocalSnapshot 経由で読書き。主モデルでは pile.AxialForce を直接更新 (従来挙動)、
                // case-local コピーでは snapshot.AxialForces[pile] を更新。
                double current = targetModel.GetAxialForce(pile);
                targetModel.SetAxialForce(pile, current - fxiAnalysis); // 圧縮増 → Fxi負 → -(-) = 加算
            }
        }

        // 要素剛性マトリクスの計算メソッド（KTanの組立: ばね剛性 min/max を集計）
        private (double springKMin, double springKMax) FindK(int iLC, AnaModel targetModel)
        {
            PrepareKmat(iLC, true, targetModel, out double springKMin, out double springKMax); // node.TangentSpring のセット
            targetModel.MapOnKtanMat(); // 要素剛性、節点剛性の剛性マトリクスKmatへのマッピング
            return (springKMin, springKMax);
        }

        private static void SolveDdAndUpdateX(AnaModel targetModel, double relaxationFactor = 1.0) // Solve Ku = -R 全体剛性方程式の求解, Update x = x + u 配置更新 // R = R - dF
                                                                                                   // >> Solve Ku = R にする。
        {
            Solver.SolveDisp(targetModel, relaxationFactor); // 増分変位  [d] = inv([Kaa_tan])[-R] * relaxationFactor
        }

        // K対角の最小/最大を取得（isTan=trueで接線剛性）
        // 強制変位DOF（diag=1.0に設定されるもの）を除外して構造的な最小値を報告
        private static (double min, double max) GetKDiagonalMiNMax(AnaModel model, bool isTan)
        {
            var (K, _) = model.GetForcedDispOnLoadVectorAndStiffnessMatrix(isTan);

            // 強制変位DOFの方程式番号を集める
            var forcedDispEqs = new HashSet<int>();
            foreach (var node in model.Nodes)
            {
                if (node.IsForcedDisped)
                {
                    for (int k = 0; k < 2; k++) // Ux, Uy のみ強制変位
                    {
                        int eq = node.EquationNumber[k];
                        if (eq >= 0) forcedDispEqs.Add(eq);
                    }
                }
            }

            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            int minIdx = -1, maxIdx = -1;
            int n = K.RowCount;
            for (int i = 0; i < n; i++)
            {
                if (forcedDispEqs.Contains(i)) continue; // 強制変位DOFを除外
                double v = K[i, i];
                if (v < min) { min = v; minIdx = i; }
                if (v > max) { max = v; maxIdx = i; }
            }

            // 常に最小/最大のDOFを特定して出力
            if (minIdx >= 0)
            {
                var log = new System.Text.StringBuilder();
                log.AppendLine("=== K対角 診断（強制変位DOF除外） ===");

                string minDofName = IdentifyDof(model, minIdx);
                string maxDofName = IdentifyDof(model, maxIdx);
                log.AppendLine($"min diag[{minIdx}]={min:E3} → {minDofName}");
                log.AppendLine($"max diag[{maxIdx}]={max:E3} → {maxDofName}");
                log.AppendLine($"条件数(概算): {max / Math.Max(min, 1e-30):E3}");
                log.AppendLine($"除外した強制変位DOF数: {forcedDispEqs.Count}");

                // 小さい対角値のDOFをリストアップ（上位10個）
                var smallDiags = new List<(int idx, double val)>();
                for (int i = 0; i < n; i++)
                {
                    if (forcedDispEqs.Contains(i)) continue;
                    double v = K[i, i];
                    if (v < 1e6)
                        smallDiags.Add((i, v));
                }
                smallDiags.Sort((a, b) => a.val.CompareTo(b.val));

                if (smallDiags.Count > 0)
                {
                    log.AppendLine($"diag(K) < 1e6 のDOF数: {smallDiags.Count}/{n} (強制変位除外後)");
                    foreach (var (idx, val) in smallDiags.Take(15))
                    {
                        string dofName = IdentifyDof(model, idx);
                        log.AppendLine($"  diag[{idx}]={val:E3} → {dofName}");
                    }
                }
                else
                {
                    log.AppendLine("diag(K) < 1e6 のDOFはありません（良好）");
                }

                log.AppendLine("=== K対角 診断終了 ===");
                // System.Diagnostics.Debug.WriteLine(log.ToString());
            }

            if (double.IsInfinity(min)) min = double.NaN;
            if (double.IsInfinity(max)) max = double.NaN;
            return (min, max);
        }

        // 方程式番号からノード名:DOF名を特定するヘルパ
        private static string IdentifyDof(AnaModel model, int eqIndex)
        {
            string[] dofNames = { "Ux", "Uy", "Uz", "Rx", "Ry", "Rz" };
            foreach (var node in model.Nodes)
            {
                for (int d = 0; d < 6; d++)
                {
                    if (node.EquationNumber[d] == eqIndex)
                        return $"{node.Name}:{dofNames[d]}";
                }
            }
            return $"eq{eqIndex}(不明)";
        }

        // 代表自由度の |d| 最大値（節点の増分変位 Ux,Uy,Uz の最大絶対値）
        private static double GetMaxAbsIncrementalDisp(AnaModel model)
        {
            double maxAbs = 0.0;
            foreach (var nd in model.Nodes)
            {
                var d = nd.IncrementalDisp;
                if (d is null) continue;
                // 代表DOFとして平行移動3成分の絶対最大を採用
                maxAbs = Math.Max(maxAbs, Math.Abs(d.Ux));
                maxAbs = Math.Max(maxAbs, Math.Abs(d.Uy));
                maxAbs = Math.Max(maxAbs, Math.Abs(d.Uz));
            }
            return maxAbs;
        }

        // v27: 振動診断用 — |δu| 絶対値が大きい順に DOF を取得。
        // リミットサイクル (flip-flop) の原因となっている DOF を特定するため、
        // Ux/Uy/Uz/Rx/Ry/Rz の 6 成分すべてを対象に取る。
        // 返り値: (Key="Node名:DOF名", NodeName, DofName, 符号付き値) のリスト。
        private static List<(string Key, string NodeName, string DofName, double Value)>
            GetTopIncrementalDofs(AnaModel model, int topN)
        {
            string[] dofNames = { "Ux", "Uy", "Uz", "Rx", "Ry", "Rz" };
            var list = new List<(string Key, string NodeName, string DofName, double Value)>();
            foreach (var nd in model.Nodes)
            {
                var d = nd.IncrementalDisp;
                if (d is null) continue;
                for (int i = 0; i < 6; i++)
                {
                    double v = d.GetByIndex(i);
                    if (v == 0.0) continue;
                    list.Add(($"{nd.Name}:{dofNames[i]}", nd.Name, dofNames[i], v));
                }
            }
            list.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            if (list.Count > topN) list.RemoveRange(topN, list.Count - topN);
            return list;
        }

        // Find T 内力ベクトルの計算メソッド
        private void FindT(int iLC, AnaModel targetModel)
        {
            PrepareKSecMat(iLC, targetModel); // node.SecantSpringのセット // 要素応力の計算
            foreach (Beam beam in targetModel.Beams) beam.SetBeamDispAndForce();
            foreach (HorizontalSoilSpring horizontalSoilSpring in targetModel.HorizontalSoilSprings)
                horizontalSoilSpring.SetBeamDispAndForce();
            // 回転ばねの内力も計算
            if (targetModel.RotationalSprings != null)
            {
                foreach (RotationalSpring rotationalSpring in targetModel.RotationalSprings)
                    rotationalSpring.SetBeamDispAndForce();
            }
            // ペナルティばねの内力も計算
            if (targetModel.PenaltySprings != null)
            {
                foreach (HorizontalSoilSpring penaltySpring in targetModel.PenaltySprings)
                    penaltySpring.SetBeamDispAndForce();
            }

            // MapOnKsecMat 削除: KAA_sec はNR反復中に参照されないため不要
            // SetT() は要素レベルの CumulativeForce から直接 VectorT を組み立てる
            targetModel.SetT();
        }

        #region Load Combination Direction Classifier (v20 Phase 1, v28 simplified)

        /// <summary>
        /// 荷重組合せの載荷方向分類。
        /// Forward (順方向組合せ): βU × βL ≥ 0 — 上部・基礎慣性力が同方向
        /// CounterLoading (逆方向組合せ): βU × βL &lt; 0 — 上部・基礎慣性力が逆方向 (S字曲げ)
        /// </summary>
        private enum LoadCombinationDirection { Forward, CounterLoading }

        /// <summary>
        /// v28 (2026-04-23): βU × βL の符号のみで分類。
        /// 物理的根拠: 逆方向組合せは杭頭と杭体下部で反対向きの曲げ (S字曲げ) が発生し、
        /// 逆符号の塑性ヒンジが同時形成 → Newton 方向が接線不連続で振動しやすいため、
        /// 最初から小さな荷重ステップで開始する。
        /// Approach I で杭頭 Ry リミットサイクルが解決済みのため、高 αL や強 βU 液状化等の
        /// 静的分類は廃止。早期適応検出 (v26 案 B) が実測ベースで救済する。
        /// </summary>
        private static LoadCombinationDirection ClassifyLoadCombinationDirection(LoadCase lc, LoadCombination combo, bool isLiq)
        {
            return (combo.Beta1 * combo.Beta2 < 0.0)
                ? LoadCombinationDirection.CounterLoading
                : LoadCombinationDirection.Forward;
        }

        /// <summary>
        /// E2 (2026-04-23): ケース識別子の短いタグ。並列化後にログが混在しても
        /// どのケース由来か即座に分かるようにするため、反復・収束・プロファイル
        /// などの反復性ログの先頭に付与する。
        /// 形式: [L{level}-{iLC+1}.C{iLCOM+1}.{Liq|NoLq}]  (例: [L2-1.C4.Liq] / [L2-1.C4.NoLq])
        /// </summary>
        private static string BuildCaseTag(int level, int iLC, int iLCOM, bool isLiquefaction)
            => $"[L{level}-{iLC + 1}.C{iLCOM + 1}.{(isLiquefaction ? "Liq" : "NoLq")}] ";

        /// <summary>
        /// 「追加実行 (段階追加再解析)」用のヘルパ群。
        /// </summary>

        /// <summary>
        /// 現在の VM 状態から AnalysisRunSnapshot を構築する。
        /// 解析完了時に呼び、AnaModel.LastRunConfig として保存。
        /// 次回 ValidateIncrementalCompatibility で前回値と比較する。
        /// </summary>
        private FEM.AnalysisRunSnapshot CaptureCurrentRunSnapshot()
        {
            return new FEM.AnalysisRunSnapshot
            {
                LiquefactionOption = LiquefactionOption.ToString(),
                Level1StepsCount = Level1CalculationStepsCount,
                Level2StepsCount = Level2CalculationStepsCount,
                UseModifiedNewtonRaphson = UseModifiedNewtonRaphson,
                FullNRIterations = FullNRIterations,
                SkipIteration = SkipIteration,
                UseLineSearch = UseLineSearch,
                RelaxationFactor = RelaxationFactor,
                UseAnalysisAxialForce = UseAnalysisAxialForce,
                ConnectionMode = ConnectionMode.ToString(),
                ExecutedCaseKeys = new List<FEM.AnalysisRunSnapshot.CaseKey>(),
                InputModelHash = null  // Phase 1 は null 許容、Phase 2 で SHA256 等
            };
        }

        /// <summary>
        /// LiquefactionOption に応じたフラグ列挙。三重ループと CountPendingCases で共通利用。
        /// </summary>
        private IEnumerable<bool> EnumerateLiquefactionCases() =>
            LiquefactionOption switch
            {
                LiquefactionOptionType.Both => new[] { true, false },
                LiquefactionOptionType.Yes => new[] { true },
                LiquefactionOptionType.None => new[] { false },
                _ => new[] { false }
            };

        /// <summary>
        /// 「実行予定 (現在選択中) だが既存結果にない」ケースの件数。
        /// 追加実行モードの TotalPlannedCaseCount 表示用。
        /// </summary>
        private int CountPendingCases(HashSet<FEM.AnalysisRunSnapshot.CaseKey> existingKeys)
        {
            int n = 0;
            foreach (var lc in InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
            {
                if (lc.UpperMassForce == 0 && lc.FoundationMassForce == 0) continue;
                foreach (var com in InputModel.LoadCasesInput.AllLoadCombinations)
                {
                    foreach (var liq in EnumerateLiquefactionCases())
                    {
                        var k = new FEM.AnalysisRunSnapshot.CaseKey(lc.LoadName, com.Name, liq);
                        if (!existingKeys.Contains(k)) n++;
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// 「追加実行」の互換性検証。前回設定 (AnaModel.LastRunConfig) と現在 VM の状態を比較し、
        /// 差分があれば false + 理由を out で返す。
        /// 規則:
        ///   - 解析パラメータ (ステップ数, NR モード, Full NR 反復, 反復なし簡易, ライン
        ///     サーチ, 緩和係数, 杭軸力モード, 接続方式) は完全一致が必要
        ///   - 液状化選択は前回をカバーするスーパーセットなら可 (Both は Yes/None を内包)
        ///   - InputModelHash は Phase 1 では null 許容、未来拡張用
        /// </summary>
        private bool ValidateIncrementalCompatibility(out string reason)
        {
            var target = TryGetTargetAnaModel();
            var prev = target?.LastRunConfig;
            if (prev == null)
            {
                reason = "前回実行情報がありません。新規実行が必要です。";
                return false;
            }

            var diffs = new List<string>();

            if (prev.Level1StepsCount != Level1CalculationStepsCount)
                diffs.Add($"レベル1ステップ数 {prev.Level1StepsCount}→{Level1CalculationStepsCount}");
            if (prev.Level2StepsCount != Level2CalculationStepsCount)
                diffs.Add($"レベル2ステップ数 {prev.Level2StepsCount}→{Level2CalculationStepsCount}");
            if (prev.UseModifiedNewtonRaphson != UseModifiedNewtonRaphson)
                diffs.Add($"NR モード切替 (Modified={prev.UseModifiedNewtonRaphson}→{UseModifiedNewtonRaphson})");
            if (prev.FullNRIterations != FullNRIterations)
                diffs.Add($"Full NR 初期反復数 {prev.FullNRIterations}→{FullNRIterations}");
            if (prev.SkipIteration != SkipIteration)
                diffs.Add($"反復なし簡易法切替 ({prev.SkipIteration}→{SkipIteration})");
            if (prev.UseLineSearch != UseLineSearch)
                diffs.Add($"ラインサーチ切替 ({prev.UseLineSearch}→{UseLineSearch})");
            if (Math.Abs(prev.RelaxationFactor - RelaxationFactor) > 1e-9)
                diffs.Add($"緩和係数 {prev.RelaxationFactor:F2}→{RelaxationFactor:F2}");
            if (prev.UseAnalysisAxialForce != UseAnalysisAxialForce)
                diffs.Add($"杭軸力モード切替 ({prev.UseAnalysisAxialForce}→{UseAnalysisAxialForce})");
            if (prev.ConnectionMode != ConnectionMode.ToString())
                diffs.Add($"接続方式 {prev.ConnectionMode}→{ConnectionMode}");

            // 液状化スーパーセットチェック
            if (!IsLiqSuperset(LiquefactionOption, prev.LiquefactionOption))
                diffs.Add($"液状化選択 {prev.LiquefactionOption}→{LiquefactionOption} (現在が前回をカバーしていません)");

            if (diffs.Count == 0) { reason = ""; return true; }
            reason = "差分:\n  - " + string.Join("\n  - ", diffs);
            return false;
        }

        /// <summary>
        /// 現在の液状化選択が前回をカバーするスーパーセットかどうか。
        /// Both はすべての値をカバー、それ以外は完全一致のみ可。
        /// </summary>
        private static bool IsLiqSuperset(LiquefactionOptionType cur, string prevString)
        {
            // 前回値が Both なら、現在は Both のみ可 (Yes/None ではカバーしきれない)
            if (prevString == nameof(LiquefactionOptionType.Both))
                return cur == LiquefactionOptionType.Both;
            // 前回値が Yes/None なら、現在も同じか Both なら可
            return cur == LiquefactionOptionType.Both || cur.ToString() == prevString;
        }

        /// <summary>
        /// CompletedCaseKeys を最新の AnalysisStepResults から再構築する。
        /// UI の「済」列バインディング更新のために OnPropertyChanged も発火。
        /// </summary>
        private void RefreshCompletedCaseKeys()
        {
            Application.Current?.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                CompletedCaseKeys.Clear();
                var target = TryGetTargetAnaModel();
                if (target?.AnalysisStepResults == null) return;
                foreach (var k in target.AnalysisStepResults
                                       .Where(r => r.LoadCase != null && r.LoadCombination != null)
                                       .Select(r => $"{r.LoadCase.LoadName}|{r.LoadCombination.Name}|{r.IsLiquefaction}")
                                       .Distinct())
                {
                    CompletedCaseKeys.Add(k);
                }
                OnPropertyChanged(nameof(CompletedCaseKeys));
            }));
        }

        /// <summary>
        /// MS Gothic 等幅フォント上での視覚幅を計算する (col 数)。
        /// ASCII → 1 col、CJK (Hiragana / Katakana / 漢字 / 全角) → 2 col。
        /// East Asian Ambiguous (Greek α β δ ω, ✓ ✗, Box Drawing, 矢印 等) は MS Gothic では
        /// **全角 2 col** で描画されるため、VisualWidth でも 2 col として扱う。
        /// 例外: '|' (ASCII vertical bar) と '-' (ASCII dash) は確実に半角なので ASCII 同様 1 col。
        /// </summary>
        private static int VisualWidth(string s)
        {
            int w = 0;
            foreach (char c in s)
            {
                if (c < 0x0080) { w += 1; continue; }                       // ASCII (含 '|' '-' '+')
                if (c >= 0x0370 && c <= 0x03FF) { w += 2; continue; }       // Greek → MS Gothic で全角
                if (c == '‖' || c == '·') { w += 2; continue; }             // ‖ はサマリー外で使用
                if (c == '✓' || c == '✗' || c == '▶') { w += 2; continue; } // チェック / 矢印 → 全角
                if (c == '⛔' || c == '⏱' || c == '♻' || c == '⚠') { w += 2; continue; }
                if (c >= 0x2500 && c <= 0x257F) { w += 2; continue; }       // Box Drawing → 全角
                if ((c >= 0x3000 && c <= 0x9FFF) || (c >= 0xFF00 && c <= 0xFFEF)) { w += 2; continue; } // CJK / 全角
                w += 1;
            }
            return w;
        }

        /// <summary>視覚幅ベースで指定列幅にパディングする (左寄せ既定、rightAlign で右寄せ)。</summary>
        private static string VisualPad(string s, int targetVisualWidth, bool rightAlign = false)
        {
            int cur = VisualWidth(s);
            int pad = Math.Max(0, targetVisualWidth - cur);
            string padStr = new string(' ', pad);
            return rightAlign ? padStr + s : s + padStr;
        }

        /// <summary>
        /// v29: ステップサマリーを TSV / CSV 形式に整形して返す。
        /// 集計サマリー (header) + 全ステップの 1 行 1 レコード形式。
        /// </summary>
        private string BuildStepSummaryText(string sep)
        {
            var snapshot = _stepSummaries.ToArray();
            if (snapshot.Length == 0) return string.Empty;
            var sorted = snapshot
                .OrderBy(s => s.Level).ThenBy(s => s.LoadCaseNo).ThenBy(s => s.ComboNo)
                .ThenBy(s => s.IsLiquefaction ? 1 : 0).ThenBy(s => s.Step).ThenBy(s => s.BisectionAttempt)
                .ToList();

            int convergedCount = sorted.Count(s => s.Status == StepStatus.Converged);
            int unconvergedCount = sorted.Count(s => s.Status == StepStatus.Unconverged);
            int physicallyUnconvergedCount = sorted.Count(s => s.Status == StepStatus.PhysicallyUnconverged);
            int retryCount = sorted.Count(s => s.BisectionAttempt > 0);
            double totalElapsed = sorted.Sum(s => s.ElapsedSec);

            var sb = new StringBuilder();
            sb.AppendLine($"# 解析サマリーレポート (生成: {DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine($"# ステップ総数 {sorted.Count} (再試行含む)、合計時間 {totalElapsed:F1}s");
            sb.AppendLine($"# 収束 {convergedCount} 件 / 未収束 {unconvergedCount} 件 / 物理的未収束 {physicallyUnconvergedCount} 件 / 再試行発生 {retryCount} 件");
            sb.AppendLine();
            // ヘッダ行
            sb.AppendLine(string.Join(sep, new[] {
                "ケース", "Level", "荷重ケース", "組合せ", "液状化",
                "Step", "NStep", "試行", "反復", "残差", "α許容", "max|d|", "状態", "時間(s)"
            }));
            foreach (var s in sorted)
            {
                string statusStr = s.Status switch
                {
                    StepStatus.Converged => "Converged",
                    StepStatus.Unconverged => "Unconverged",
                    StepStatus.PhysicallyUnconverged => "PhysUnconverged",
                    _ => "?"
                };
                sb.AppendLine(string.Join(sep, new[] {
                    s.CaseTag.Replace(",", " "),  // CSV 用にカンマ除去
                    s.Level.ToString(),
                    s.LoadCaseNo.ToString(),
                    s.ComboNo.ToString(),
                    s.IsLiquefaction ? "Liq" : "NoLq",
                    s.Step.ToString(),
                    s.NStep.ToString(),
                    s.BisectionAttempt.ToString(),
                    s.Iterations.ToString(),
                    s.FinalResidual.ToString("E3"),
                    s.EffectiveAlpha.ToString("E2"),
                    s.MaxDisp.ToString("E3"),
                    statusStr,
                    s.ElapsedSec.ToString("F1")
                }));
            }
            return sb.ToString();
        }

        [RelayCommand]
        private void CopySummaryToClipboard()
        {
            var text = BuildStepSummaryText("\t");
            if (string.IsNullOrEmpty(text))
            {
                MessageService.Show("サマリーデータがありません。先に解析を実行してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { System.Windows.Clipboard.SetText(text); }
            catch (Exception ex)
            {
                MessageService.Show($"クリップボードへのコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            StatusMessage = $"サマリー {_stepSummaries.Count} 行をクリップボードにコピーしました";
        }

        [RelayCommand]
        private void ExportSummaryToCsv()
        {
            var text = BuildStepSummaryText(",");
            if (string.IsNullOrEmpty(text))
            {
                MessageService.Show("サマリーデータがありません。先に解析を実行してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"AnalysisSummary_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // Excel での文字化け回避のため UTF-8 BOM 付きで保存
                System.IO.File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(true));
                StatusMessage = $"サマリーを {System.IO.Path.GetFileName(dialog.FileName)} に保存しました";
            }
            catch (Exception ex)
            {
                MessageService.Show($"CSV 保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// v29: 解析終了時にステップ単位の収束サマリーをレポート出力する。
        /// 全ステップ (再試行含む) を表形式で表示し、未収束件数を集計表示。
        /// </summary>
        private async Task OutputStepSummaryReport()
        {
            var snapshot = _stepSummaries.ToArray();
            if (snapshot.Length == 0) return;

            // テキスト全体を 1 度ビルド → ログとキャッシュに出す (docx 出力用)
            string summaryText = BuildStepSummaryReportText(snapshot);
            _mainWindowViewModel.LastAnalysisSummaryText = summaryText;
            foreach (var line in summaryText.Split('\n'))
            {
                // BuildStepSummaryReportText は \r\n を出さない前提
                await AddLogAsync(line.TrimEnd('\r'));
            }
        }

        // docx 出力でも同内容を再利用するため、テキスト全体を 1 つの string として返す
        private static string BuildStepSummaryReportText(StepSummary[] snapshot)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string line) => sb.AppendLine(line);
            Add_LegacyOutputStepSummary(snapshot, Add);
            return sb.ToString();
        }

        // 旧 OutputStepSummaryReport の本体ロジックを Action<string> 経由で出力するように汎化
        private static void Add_LegacyOutputStepSummary(StepSummary[] snapshot, Action<string> emit)
        {

            // ケースタグ → 荷重ステップ番号 → 試行番号 の順でソート
            var sorted = snapshot
                .OrderBy(s => s.Level)
                .ThenBy(s => s.LoadCaseNo)
                .ThenBy(s => s.ComboNo)
                .ThenBy(s => s.IsLiquefaction ? 1 : 0)
                .ThenBy(s => s.Step)
                .ThenBy(s => s.BisectionAttempt)
                .ToList();

            int totalCount = sorted.Count;
            int convergedCount = sorted.Count(s => s.Status == StepStatus.Converged);
            int unconvergedCount = sorted.Count(s => s.Status == StepStatus.Unconverged);
            int physicallyUnconvergedCount = sorted.Count(s => s.Status == StepStatus.PhysicallyUnconverged);
            int retryCount = sorted.Count(s => s.BisectionAttempt > 0);
            double totalElapsed = sorted.Sum(s => s.ElapsedSec);

            // 罫線文字 (Box-Drawing) で表組み。LogWindow は MS Gothic 等幅フォント。
            // MS Gothic では ━ (U+2501) と CJK は全角 2 col、ASCII は半角 1 col。
            // 表本体は 103 visual cols (2 leading + 各列 + " | " 区切り) — 罫線も同幅に揃える:
            //   topRule    = 20 ━ + "  解析サマリーレポート  " + 20 ━ + " ━" = 40 + 22 + 40 + 2 = 103 cols
            //   (実際は ━ 単位で偶数幅しか作れないため bottomRule = 52 × 2 = 104 col で許容)
            const string topRule    = "━━━━━━━━━━━━━━━━━━━━  解析サマリーレポート  ━━━━━━━━━━━━━━━━━━━━";
            const string bottomRule = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

            emit("");
            emit(topRule);
            emit($"ステップ総数 {totalCount} (再試行含む)  ┃  合計時間 {totalElapsed:F1}s");
            emit($"  ✓ 収束 {convergedCount} 件" +
                (unconvergedCount > 0 ? $"  /  ✗ 未収束 (反復上限到達) {unconvergedCount} 件" : "") +
                (physicallyUnconvergedCount > 0 ? $"  /  ⛔ 物理的未収束 (耐力超過の可能性) {physicallyUnconvergedCount} 件" : "") +
                (retryCount > 0 ? $"  /  ♻ 再試行発生 {retryCount} 件" : ""));
            emit("");

            // 列の視覚幅 (MS Gothic 上での col 数)。データの最大幅以上に確保すること:
            //   wRes/wMaxD: "X.XXE-NNN" = 9 col 必要、wAlpha: "X.XE-NNN" = 8 col 必要
            //   wTime: " XX.Xs" = 6 col、wIter: 3 桁反復まで = 4 col
            //   wNRMode: "FNR=NN/MNR=NN" 等を表示するため "NR/MNR" = 8 col 確保
            const int wCase = 18, wStep = 5, wRetry = 4, wIter = 4, wNRMode = 9, wRes = 9, wAlpha = 8, wMaxD = 9, wStat = 14, wTime = 6;

            // 表ヘッダ + 区切り行 (視覚幅でパディング)
            // ASCII-only ヘッダ — Consolas/MS Gothic 混在環境で CJK 幅が ASCII×2 と
            // 厳密に一致しないため、列構造はすべて ASCII で統一する
            emit(
                "  " + VisualPad("Case", wCase) + " | " + VisualPad("Step", wStep) +
                " | " + VisualPad("Try", wRetry) + " | " + VisualPad("Iter", wIter) +
                " | " + VisualPad("NR/MNR", wNRMode) +
                " | " + VisualPad("Resid", wRes) + " | " + VisualPad("a_tol", wAlpha) +
                " | " + VisualPad("max|du|", wMaxD) + " | " + VisualPad("Status", wStat) +
                " | " + VisualPad("Time", wTime));
            emit(
                "  " + new string('-', wCase + 1) + "+" + new string('-', wStep + 2) +
                "+" + new string('-', wRetry + 2) + "+" + new string('-', wIter + 2) +
                "+" + new string('-', wNRMode + 2) +
                "+" + new string('-', wRes + 2) + "+" + new string('-', wAlpha + 2) +
                "+" + new string('-', wMaxD + 2) + "+" + new string('-', wStat + 2) +
                "+" + new string('-', wTime + 1));
            foreach (var s in sorted)
            {
                string statusStr = s.Status switch
                {
                    StepStatus.Converged => "OK Converged",
                    StepStatus.Unconverged => "NG Unconverged",
                    StepStatus.PhysicallyUnconverged => "!! Phys.Unconv",
                    _ => "?"
                };
                string retryTag = s.BisectionAttempt > 0 ? $"#{s.BisectionAttempt}" : "-";
                string stepStr = $"{s.Step,2}/{s.NStep,-2}";
                // NR/MNR 列: "K 行列を再計算した反復数 / 再利用した反復数" を表示
                string nrModeStr = $"{s.KRebuildCount}/{s.KReuseCount}";
                emit(
                    "  " + VisualPad(s.CaseTag, wCase) + " | " + VisualPad(stepStr, wStep) +
                    " | " + VisualPad(retryTag, wRetry, rightAlign: true) +
                    " | " + VisualPad(s.Iterations.ToString(), wIter, rightAlign: true) +
                    " | " + VisualPad(nrModeStr, wNRMode, rightAlign: true) +
                    " | " + VisualPad(s.FinalResidual.ToString("E2"), wRes, rightAlign: true) +
                    " | " + VisualPad(s.EffectiveAlpha.ToString("E1"), wAlpha, rightAlign: true) +
                    " | " + VisualPad(s.MaxDisp.ToString("E2"), wMaxD, rightAlign: true) +
                    " | " + VisualPad(statusStr, wStat) +
                    " | " + VisualPad($"{s.ElapsedSec:F1}s", wTime, rightAlign: true));
            }

            // 未収束ステップのみの再掲 (見落とし防止)
            var failures = sorted.Where(s => s.Status != StepStatus.Converged).ToList();
            if (failures.Count > 0)
            {
                emit("");
                emit("  ─ 未収束 / 物理的未収束のステップ ─");
                foreach (var s in failures)
                {
                    string statusStr = s.Status switch
                    {
                        StepStatus.Unconverged => "✗ 未収束",
                        StepStatus.PhysicallyUnconverged => "⛔ 物理的未収束",
                        _ => "?"
                    };
                    emit($"    {s.CaseTag} step {s.Step}/{s.NStep} (試行#{s.BisectionAttempt})  {statusStr}  残差={s.FinalResidual:E2}  max|δu|={s.MaxDisp:E2}m");
                }
            }
            emit(bottomRule);
            emit("");
        }

        #endregion

        #region Line Search (線探索)

        /// <summary>
        /// Newton方向を解く（変位を更新しない）
        /// </summary>
        /// <returns>増分変位ベクトル（Newton方向）</returns>
        private static MathNet.Numerics.LinearAlgebra.Vector<double> SolveNewtonDirection(AnaModel targetModel)
        {
            targetModel.SetForcedDispOnLoadVectorAndStiffnessMatrix(true); // KAA_tanとVectorRを取得

            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection;
            try
            {
                // cache を渡すと K 不変な反復で Cholesky 因子を再利用 (CSC + 分解をスキップ)
                var x = CsparseLinearSolver.Solve(targetModel.KAA_tan, targetModel.VectorR, isSpd: false, cache: targetModel.SolverCache);
                newtonDirection = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(x);
            }
            catch
            {
                newtonDirection = targetModel.KAA_tan.Solve(targetModel.VectorR);
            }

            // 変位増分制限（発散防止）
            const double maxDispIncrement = 0.05; // 最大増分 50mm
            double maxAbsIncrement = newtonDirection.AbsoluteMaximum();
            if (maxAbsIncrement > maxDispIncrement)
            {
                double scaleFactor = maxDispIncrement / maxAbsIncrement;
                newtonDirection *= scaleFactor;
            }

            return newtonDirection;
        }

        /// <summary>
        /// 指定したステップ長αで変位を更新し、残差を評価する
        /// </summary>
        /// <param name="targetModel">解析モデル</param>
        /// <param name="savedVectorD">保存された累積変位</param>
        /// <param name="newtonDirection">Newton方向</param>
        /// <param name="alpha">ステップ長</param>
        /// <param name="iLC">荷重ケース番号</param>
        /// <param name="isPileNonLinear">杭非線形フラグ</param>
        /// <param name="isLightweight">軽量評価モード（割線剛性更新をスキップ）</param>
        /// <returns>残差ノルム ||R||²/||Fint||²</returns>
        private double EvaluateResidualAtAlpha(
            AnaModel targetModel,
            MathNet.Numerics.LinearAlgebra.Vector<double> savedVectorD,
            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection,
            double alpha,
            int iLC,
            bool isPileNonLinear,
            bool isLightweight = false)
        {
            // αを適用した増分変位
            var incrementalDisp = newtonDirection * alpha;

            // 累積変位を更新（ラインサーチ用メソッドで直接設定）
            targetModel.SetDispVectorDirect(savedVectorD + incrementalDisp, incrementalDisp);

            // 節点変位を更新（Solver.SolveDisp内のロジックを再現）
            UpdateNodeDisplacementsForLineSearch(targetModel, incrementalDisp);

            // 割線剛性更新（非線形の場合）
            // 軽量モードでは現在の割線剛性を使用（近似的だが高速）
            if (isPileNonLinear && !isLightweight)
                UpdateBeamMPhiSecant(targetModel);

            // 内力計算
            FindT(iLC, targetModel);

            // 残差計算
            targetModel.FindR();

            return targetModel.NormsROnNormsFint;
        }

        /// <summary>
        /// ラインサーチ用: 増分変位から節点変位を更新する
        /// Solver.SolveDispのロジックを再現
        /// </summary>
        private static void UpdateNodeDisplacementsForLineSearch(AnaModel targetModel, MathNet.Numerics.LinearAlgebra.Vector<double> incrementalDispVector)
        {
            foreach (var node in targetModel.Nodes)
            {
                double[] ddisp = new double[6];

                if (node.ResolvedDofMap != null)
                {
                    // ResolvedDofMap 方式
                    for (int i = 0; i < 6; i++)
                    {
                        var terms = node.ResolvedDofMap[i];
                        if (terms == null || terms.Length == 0) { ddisp[i] = 0; continue; }
                        double disp = 0;
                        foreach (var term in terms)
                        {
                            if (term.Eq >= 0)
                                disp += term.Coeff * incrementalDispVector[term.Eq];
                        }
                        ddisp[i] = disp;
                    }
                }
                else
                {
                    // フォールバック: 従来ロジック
                    (int crossIdx, Func<Vector3S, double> arm, double sign)[][] crossTerms =
                    [
                        [(4, v => v.Z, 1.0), (5, v => v.Y, -1.0)],
                        [(5, v => v.X, 1.0), (3, v => v.Z, -1.0)],
                        [(3, v => v.Y, 1.0), (4, v => v.X, -1.0)],
                        [], [], [],
                    ];
                    for (int i = 0; i < 6; i++)
                    {
                        int e_num = node.EquationNumber[i];
                        if (node.MasterNodes[i] != null)
                        {
                            int eq = node.MasterNodes[i].EquationNumber[i];
                            ddisp[i] = eq < 0 ? 0 : incrementalDispVector[eq];
                            foreach (var (crossIdx, arm, sign) in crossTerms[i])
                            {
                                if (node.MasterNodes[crossIdx] != null)
                                {
                                    int crossEq = node.MasterNodes[crossIdx].EquationNumber[crossIdx];
                                    double armVal = arm(node.SlaveArm);
                                    ddisp[i] += (crossEq >= 0 ? incrementalDispVector[crossEq] : 0.0) * armVal * sign;
                                }
                            }
                        }
                        else
                        {
                            ddisp[i] = e_num < 0 ? 0 : incrementalDispVector[e_num];
                        }
                    }
                }

                var incDisp = new NodeDisp(ddisp[0], ddisp[1], ddisp[2], ddisp[3], ddisp[4], ddisp[5]);
                node.IncrementalDisp = incDisp;

                // 累積変位の更新
                if (node.CumulativeDisp == null)
                    node.CumulativeDisp = incDisp;
                else
                    node.CumulativeDisp = new NodeDisp(
                        node.CumulativeDisp.Ux + incDisp.Ux,
                        node.CumulativeDisp.Uy + incDisp.Uy,
                        node.CumulativeDisp.Uz + incDisp.Uz,
                        node.CumulativeDisp.Rx + incDisp.Rx,
                        node.CumulativeDisp.Ry + incDisp.Ry,
                        node.CumulativeDisp.Rz + incDisp.Rz
                    );
            }
        }

        /// <summary>
        /// 2次補間を用いた高速ラインサーチ（v14 → v25 G+ 改良版）
        /// v25 G+: f'(0) ≈ -2f(0) の線形化勾配を使った 2 点 quadratic fit を先頭で試す。
        /// 成功すれば中間点 trial を省略できる（α=1 の 1 回のみで閉形式解）。
        /// 失敗時は従来の 3 点 quadratic fit（中間点を full eval に精度向上）にフォールバック。
        /// </summary>
        /// <param name="targetModel">解析モデル</param>
        /// <param name="newtonDirection">Newton方向</param>
        /// <param name="currentResidual">現在の残差</param>
        /// <param name="iLC">荷重ケース番号</param>
        /// <param name="isPileNonLinear">杭非線形フラグ</param>
        /// <returns>最適なステップ長α</returns>
        private double BacktrackingLineSearch(
            AnaModel targetModel,
            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection,
            double currentResidual,
            int iLC,
            bool isPileNonLinear,
            out int trialCount)
        {
            // 現在の累積変位と節点変位を保存
            var savedVectorD = targetModel.VectorD.Clone();
            var savedNodeDisps = SaveNodeDisplacements(targetModel);

            // 試行回数カウンタ (EvaluateResidualAtAlpha 呼出し回数を集計)
            trialCount = 0;

            // Step 1: α=1.0で完全評価
            double alpha1 = 1.0;
            trialCount++;
            double f1 = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alpha1, iLC, isPileNonLinear, isLightweight: false);

            // α=1.0で残差が減少すれば即採用
            if (f1 <= currentResidual)
            {
                _lastAcceptedAlpha = 1.0;
                return alpha1;
            }

            double f0 = currentResidual;

            // v25 G+: 勾配情報による 2 点 quadratic fit（閉形式）
            // Newton 方向 Δu は K_tan Δu = -R を満たす。f(α) = ||R(u+αΔu)||² とおくと
            // linearize: R(u+αΔu) ≈ R(u) - α R(u) = (1-α) R(u)
            // ⇒ f(α) ≈ (1-α)² f(0), f'(0) ≈ -2 f(0)
            // Quadratic fit: a α² + b α + c with c=f0, b=-2 f0, a=f1+f0
            // α* = -b/(2a) = f0 / (f0 + f1), clamped to [0.05, 0.95]
            // 3 点 fit と比べて中間点 trial が不要なので、成功時は評価 1 回節約できる。
            if (f0 > 0 && f1 > 0)
            {
                double alphaGrad = f0 / (f0 + f1);
                alphaGrad = Math.Clamp(alphaGrad, 0.05, 0.95);

                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double fGrad = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alphaGrad, iLC, isPileNonLinear, isLightweight: true);

                if (fGrad < f0)
                {
                    RestoreNodeDisplacements(targetModel, savedNodeDisps);
                    trialCount++;
                    EvaluateResidualAtAlpha(
                        targetModel, savedVectorD, newtonDirection, alphaGrad, iLC, isPileNonLinear, isLightweight: false);
                    _lastAcceptedAlpha = alphaGrad;
                    return alphaGrad;
                }
            }

            // Step 2: α=0.5 で評価（v25 G+: lightweight → full に変更し fit 精度向上）
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            double alpha2 = 0.5;
            trialCount++;
            double f2 = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alpha2, iLC, isPileNonLinear, isLightweight: false);

            // α=0.5で残差が減少すれば採用（full eval 済みなので再評価不要）
            if (f2 < currentResidual)
            {
                _lastAcceptedAlpha = alpha2;
                return alpha2;
            }

            // Step 3: 3 点 quadratic fit（f0, f2, f1 から α* を推定）
            // f(α) ≈ a*α² + b*α + c の係数を推定
            // 3点: (0, f0=currentResidual), (0.5, f2), (1.0, f1)
            // f(0) = c = f0
            // f(0.5) = 0.25a + 0.5b + c = f2
            // f(1) = a + b + c = f1
            // 解くと:
            // a = 2*f1 - 4*f2 + 2*f0
            // b = -3*f0 + 4*f2 - f1
            double a = 2 * f1 - 4 * f2 + 2 * f0;
            double b = -3 * f0 + 4 * f2 - f1;

            // 2次関数の頂点 α* = -b / (2a)（a > 0 の場合のみ有効な最小値）
            double alphaOpt;
            if (a > 1e-12)
            {
                alphaOpt = -b / (2 * a);
                alphaOpt = Math.Clamp(alphaOpt, 0.05, 0.95);  // 範囲制限
            }
            else
            {
                // 2次係数が小さい/負の場合は線形補間でα=0.25を試す
                alphaOpt = 0.25;
            }

            // Step 4: 最適αで評価
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            trialCount++;
            double fOpt = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alphaOpt, iLC, isPileNonLinear, isLightweight: true);

            if (fOpt < currentResidual)
            {
                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double finalResidual = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alphaOpt, iLC, isPileNonLinear, isLightweight: false);
                _lastAcceptedAlpha = alphaOpt;
                return alphaOpt;
            }

            // Step 5: 補間が失敗した場合、フォールバックとして幾何縮小
            double bestAlpha = (f1 < f2) ? alpha1 : alpha2;
            double bestResidual = Math.Min(f1, f2);
            if (fOpt < bestResidual)
            {
                bestResidual = fOpt;
                bestAlpha = alphaOpt;
            }

            // 追加試行: α=0.25, 0.125
            double[] fallbackAlphas = [0.25, 0.125, 0.0625];
            foreach (double alpha in fallbackAlphas)
            {
                if (Math.Abs(alpha - alphaOpt) < 0.05) continue; // 既に試したαはスキップ

                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double trialResidual = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alpha, iLC, isPileNonLinear, isLightweight: true);

                if (trialResidual < bestResidual)
                {
                    bestResidual = trialResidual;
                    bestAlpha = alpha;
                }

                if (trialResidual < currentResidual)
                {
                    RestoreNodeDisplacements(targetModel, savedNodeDisps);
                    trialCount++;
                    double finalResidual = EvaluateResidualAtAlpha(
                        targetModel, savedVectorD, newtonDirection, alpha, iLC, isPileNonLinear, isLightweight: false);
                    _lastAcceptedAlpha = alpha;
                    return alpha;
                }
            }

            // すべて失敗した場合、最良のαを使用
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            trialCount++;
            EvaluateResidualAtAlpha(targetModel, savedVectorD, newtonDirection, bestAlpha, iLC, isPileNonLinear, isLightweight: false);
            _lastAcceptedAlpha = bestAlpha;

            return bestAlpha;
        }

        // 前回のライン探索で採用されたα（次回の参考用）
        private double _lastAcceptedAlpha = 1.0;

        /// <summary>
        /// 節点変位を保存
        /// </summary>
        private static Dictionary<FEM.Node, (NodeDisp incremental, NodeDisp cumulative)> SaveNodeDisplacements(AnaModel targetModel)
        {
            var saved = new Dictionary<FEM.Node, (NodeDisp, NodeDisp)>();
            foreach (var node in targetModel.Nodes)
            {
                saved[node] = (
                    node.IncrementalDisp?.Clone(),
                    node.CumulativeDisp?.Clone()
                );
            }
            return saved;
        }

        /// <summary>
        /// 節点変位を復元
        /// </summary>
        private static void RestoreNodeDisplacements(AnaModel targetModel, Dictionary<FEM.Node, (NodeDisp incremental, NodeDisp cumulative)> saved)
        {
            foreach (var node in targetModel.Nodes)
            {
                if (saved.TryGetValue(node, out var displacement))
                {
                    node.IncrementalDisp = displacement.incremental?.Clone();
                    node.CumulativeDisp = displacement.cumulative?.Clone();
                }
            }
        }

        #endregion

        // Phase 2 (step-level cut-back): resetCumulative パラメータを追加。
        //   resetCumulative=true (default): 従来挙動 — IncrementalForcedDisp と CumulativeForcedDisp を両方上書き (= ステップ 1 から開始)
        //   resetCumulative=false: substep モード — IncrementalForcedDisp のみ上書きし、CumulativeForcedDisp は保持 (=チェックポイント復元後の継続実行)
        private void InitializeSoilDisplacementIncrement(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, bool isLiquefaction, double nStep, bool resetCumulative = true)
        {
            double loadAngle = loadCase.LoadAngle;
            double alpha1 = loadCombination.Alpha1;
            NodeDisp initialCumulativeSoilDisplacement = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

            // 共通の地盤変位計算ローカル関数
            static NodeDisp CalcDisplacement(double displacement1, double displacement2, int level, double alpha1, double nStep, double loadAngle)
            {
                double groundDisp = (level == 1 ? displacement1 : displacement2) * alpha1 / nStep / 1000.0;

                double rad = loadAngle * Math.PI / 180.0;
                double groundDisplacementX = groundDisp * Math.Cos(rad);
                double groundDisplacementY = groundDisp * Math.Sin(rad);
                return new NodeDisp(groundDisplacementX, groundDisplacementY, 0.0, 0.0, 0.0, 0.0);
            }

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1];
                // E3b: case-local SoilNodes 経由 (主モデルでは InputModel.PileLayoutItems.SoilNodes と同一参照)
                var soilNodes = targetModel.GetSoilNodes(pileLayoutItem);
                for (int i = 0; i < soilPile.ZDataItems.Count; i++)
                {
                    var zData = soilPile.ZDataItems[i];
                    double groundDisp1 = isLiquefaction ? zData.GroundDisp1L : zData.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zData.GroundDisp2L : zData.GroundDisp2;
                    NodeDisp dd = CalcDisplacement(groundDisp1, groundDisp2, level, alpha1, nStep, loadAngle);

                    soilNodes[i].SetIncrementalForcedDisp(dd);
                    if (resetCumulative)
                        soilNodes[i].SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
                }
            }

            if (InputModel.ElementDivision.DoatsuGoryokuBane != null &&
                InputModel.ElementDivision.DoatsuGoryokuBane.Items.Count > 1)
            {
                for (int i = 0; i < InputModel.ElementDivision.SoilEmbedment.ZDataItems.Count; i++)
                {
                    var zDataItem = InputModel.ElementDivision.SoilEmbedment.ZDataItems[i];
                    var z = zDataItem.Z;
                    double groundDisp1 = isLiquefaction ? zDataItem.GroundDisp1L : zDataItem.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zDataItem.GroundDisp2L : zDataItem.GroundDisp2;
                    NodeDisp dd = CalcDisplacement(groundDisp1, groundDisp2, level, alpha1, nStep, loadAngle);
                    FEM.Node soilNode = targetModel.FindNode("根入部地盤節点", null, null, z);
                    soilNode.SetIncrementalForcedDisp(dd);
                    if (resetCumulative)
                        soilNode.SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
                }
            }
        }

        // 増分荷重の取得 慣性力の節点荷重へのセット
        // Phase 2 (step-level cut-back): resetCumulative パラメータを追加。
        //   resetCumulative=true (default): 従来挙動 — IncrementalLoad/VectorDF と CumulativeLoad/VectorF を初期化 (=ステップ 1 から)
        //   resetCumulative=false: substep モード — IncrementalLoad/VectorDF のみ更新、累積側は保持 (=チェックポイント復元後の継続実行)
        // AxialForceIncrement は常に書換 (per-step 値、累積はモデル内で別管理)。
        private void SetVectorDF(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, int iLC, double nStep, bool resetCumulative = true) // PileDesign
        {
            double loadAngle = loadCase.LoadAngle;
            double beta1 = loadCombination.Beta1; // 荷重組合せ上部構造慣性力の荷重係数β1
            double beta2 = loadCombination.Beta2; // 荷重組合せ基礎構造慣性力の荷重係数β2

            double upperMassForce = loadCase.UpperMassForce; // 上部構造質量荷重 [kN]
            double foundationMassForce = loadCase.FoundationMassForce; // 基礎構造質量荷重 [kN]

            double force = beta1 * upperMassForce + beta2 * foundationMassForce; // 上部構造質量荷重 + 基礎構造質量荷重[kN]
            double deltaForce = force / nStep; // 増分荷重 [kN]
            double x = deltaForce * Math.Cos(loadAngle * Math.PI / 180.0); // x方向の増分荷重 [kN]
            double y = deltaForce * Math.Sin(loadAngle * Math.PI / 180.0); // y方向の増分荷重 [kN]

            targetModel.Nodes[0].SetIncrementalLoad(new(x, y, 0.0, 0.0, 0.0, 0.0)); // 増分荷重ベクトル [kN]
            targetModel.MapOnVectorDF();

            if (resetCumulative)
            {
                targetModel.Nodes[0].SetCumulativeLoad(new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0)); // 荷重ベクトル [kN]
                targetModel.MapOnVectorF();
            }

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                // E3b: case-local AxialForceIncrement 経由で書込
                double increment;
                if (level == 1)
                {
                    increment = (pileLayoutItem.AxialForceLevel1s[iLC]
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nStep; // レベル1の杭軸力増分 [kN]
                }
                else //(level == 2)
                {
                    increment = (pileLayoutItem.AxialForceLevel2s[iLC]
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nStep; // レベル2の杭軸力増分 [kN]
                }
                targetModel.SetAxialForceIncrement(pileLayoutItem, increment);
            }
        }

        // 地盤変位の更新
        // E3b: targetModel 引数を受取り、AnaModel ヘルパー経由で case-local な
        // Node を書換えるよう変更。主モデルでは従来通り InputModel 側 Node を更新、
        // case-local コピーでは snapshot 上の Node を更新する。
        private void UpdateSoilDisp(AnaModel targetModel)
        {
            // DoatsuGoryokuBaneの節点の地盤変位を更新
            var doatsuGoryokuBane = InputModel.ElementDivision.DoatsuGoryokuBane;
            if (doatsuGoryokuBane != null)
            {
                for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
                {
                    var dgbItem = doatsuGoryokuBane.Items[i];
                    if (i == 0)
                    {
                        var topSoilNode = targetModel.GetDoatsuTopSoilNode(dgbItem);
                        if (topSoilNode != null)
                            topSoilNode.CumulativeForcedDisp += topSoilNode.IncrementalForcedDisp;
                    }
                    var btmSoilNode = targetModel.GetDoatsuBtmSoilNode(dgbItem);
                    if (btmSoilNode != null)
                        btmSoilNode.CumulativeForcedDisp += btmSoilNode.IncrementalForcedDisp;
                }
            }

            // PileLayoutItemsの節点の地盤変位を更新
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                if (pileLayoutItem == null) continue;
                var soilNodes = targetModel.GetSoilNodes(pileLayoutItem);
                if (soilNodes == null) continue;
                foreach (var node in soilNodes)
                {
                    if (node?.IncrementalForcedDisp != null)
                    {
                        node.CumulativeForcedDisp += node.IncrementalForcedDisp;
                    }
                }
            }
        }

        // ばね剛性の安全化ヘルパ
        private static double SafeK(double v)
            => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        /// <summary>
        /// ペナルティばね（RotationalSpring・PenaltySprings）の両端相対変位を検証し、
        /// 全体変位に対して十分小さいことを確認する。
        /// 閾値を超えた場合はログに警告を出力する。
        /// </summary>
        private async Task VerifyPenaltySpringAccuracy(AnaModel model)
        {
            const double threshold = 0.001; // 0.1%
            if (model == null) return;

            // 全節点の最大変位を取得（基準値）
            double maxGlobalDisp = 0;
            double maxGlobalRot = 0;
            foreach (var node in model.Nodes)
            {
                var d = node.CumulativeDisp;
                if (d == null) continue;
                maxGlobalDisp = Math.Max(maxGlobalDisp, Math.Max(Math.Abs(d.Ux), Math.Max(Math.Abs(d.Uy), Math.Abs(d.Uz))));
                maxGlobalRot = Math.Max(maxGlobalRot, Math.Max(Math.Abs(d.Rx), Math.Max(Math.Abs(d.Ry), Math.Abs(d.Rz))));
            }

            if (maxGlobalDisp < 1e-15 && maxGlobalRot < 1e-15) return; // 変位がない

            var warnings = new List<string>();

            // RotationalSpring の検証
            if (model.RotationalSprings != null)
            {
                foreach (var rs in model.RotationalSprings)
                {
                    if (rs.NodeI?.CumulativeDisp == null || rs.NodeJ?.CumulativeDisp == null) continue;
                    var di = rs.NodeI.CumulativeDisp;
                    var dj = rs.NodeJ.CumulativeDisp;

                    // 並進方向の相対変位（ペナルティで拘束されている場合）
                    if (rs.TieUx && maxGlobalDisp > 1e-15)
                    {
                        double relUx = Math.Abs(di.Ux - dj.Ux);
                        if (relUx / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Ux相対変位={relUx:E3} ({relUx / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieUy && maxGlobalDisp > 1e-15)
                    {
                        double relUy = Math.Abs(di.Uy - dj.Uy);
                        if (relUy / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Uy相対変位={relUy:E3} ({relUy / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieUz && maxGlobalDisp > 1e-15)
                    {
                        double relUz = Math.Abs(di.Uz - dj.Uz);
                        if (relUz / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Uz相対変位={relUz:E3} ({relUz / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieRz && maxGlobalRot > 1e-15)
                    {
                        double relRz = Math.Abs(di.Rz - dj.Rz);
                        if (relRz / maxGlobalRot > threshold)
                            warnings.Add($"{rs.Name}: Rz相対変位={relRz:E3} ({relRz / maxGlobalRot * 100:F2}%)");
                    }
                }
            }

            // PenaltySprings の検証
            if (model.PenaltySprings != null)
            {
                foreach (var ps in model.PenaltySprings)
                {
                    if (ps.NodeI?.CumulativeDisp == null || ps.NodeJ?.CumulativeDisp == null) continue;
                    var di = ps.NodeI.CumulativeDisp;
                    var dj = ps.NodeJ.CumulativeDisp;

                    double relDisp = Math.Sqrt(
                        Math.Pow(di.Ux - dj.Ux, 2) + Math.Pow(di.Uy - dj.Uy, 2) + Math.Pow(di.Uz - dj.Uz, 2));
                    if (maxGlobalDisp > 1e-15 && relDisp / maxGlobalDisp > threshold)
                        warnings.Add($"{ps.Name}: 相対変位={relDisp:E3} ({relDisp / maxGlobalDisp * 100:F2}%)");
                }
            }

            if (warnings.Count > 0)
            {
                await AddLogAsync($"⚠ ペナルティばね精度警告: {warnings.Count}件のばねで相対変位が閾値({threshold * 100:F1}%)を超えています");
                foreach (var w in warnings.Take(20))
                    await AddLogAsync($"  {w}");
                if (warnings.Count > 20)
                    await AddLogAsync($"  ...他{warnings.Count - 20}件");
                await AddLogAsync("  → ペナルティ定数(KBig)の増加を検討してください。");
            }
            else
            {
                await AddLogAsync("ペナルティばね精度検証: OK（全ばねで相対変位 < 0.1%）");
            }
        }

        private void PrepareKmat(int iLC, bool isTan, AnaModel model, out double springKMin, out double springKMax)
        {
            // 診断: ばね剛性の min/max を集計（out で呼び出し元に返す）
            double springMin = double.PositiveInfinity;
            double springMax = double.NegativeInfinity;

            // 土圧合力ばね
            //
            // v22 修正（バグ#1）: 隣接する DoatsuGoryoku Item は「内部節点のスプリング」を共有する
            // （AnalysisModelling.cs: Items[i+1].TopSpring === Items[i].BtmSpring）。
            // 旧実装は Items を素直に反復し SetKe を呼んでいたため、共有スプリングは後続 Item の
            // 半分面積 (DY × DZ × 0.5) で上書きされ、**もう一方の層の寄与が失われていた**。
            //
            // 結果として:
            //   - 内部節点の K/F が本来の ~半分になる（両層の半面積合算であるべきところ）
            //   - 構造側が土圧合力の一部しか受け取らず、解析結果が物理的に不正確
            //
            // 修正: 一旦各ユニークスプリングへの寄与を (kx, ky) ペアで集計し、最後に 1 回だけ SetKe する。
            // これで内部節点は「上層の下半分 + 下層の上半分」の寄与を正しく合算できる。
            if (InputModel.ElementDivision.DoatsuGoryokuBane != null)
            {
                var items = InputModel.ElementDivision.DoatsuGoryokuBane.Items;
                // ユニークスプリング → 累積 (kx, ky)
                var accum = new Dictionary<FEM.HorizontalSoilSpring, (double kx, double ky)>(ReferenceEqualityComparer.Instance);

                void AddContribution(FEM.HorizontalSoilSpring spring, double kx, double ky)
                {
                    if (spring == null) return;
                    if (accum.TryGetValue(spring, out var prev))
                        accum[spring] = (prev.kx + kx, prev.ky + ky);
                    else
                        accum[spring] = (kx, ky);
                }

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];

                    // E3b: case-local な Node/Spring を取得 (主モデルでは item.TopEmbedmentNode などと同一参照)
                    var topEmb = model.GetDoatsuTopEmbedmentNode(item);
                    var topSoil = model.GetDoatsuTopSoilNode(item);
                    var btmEmb = model.GetDoatsuBtmEmbedmentNode(item);
                    var btmSoil = model.GetDoatsuBtmSoilNode(item);
                    var topHs = model.GetDoatsuTopHorizontalSoilSpring(item);
                    var btmHs = model.GetDoatsuBtmHorizontalSoilSpring(item);

                    var relDispTop = topEmb.CumulativeDisp - topSoil.CumulativeDisp;
                    var kVecTop = isTan ? item.GetTangentStiffnessVector(relDispTop) : item.GetSecantStiffnessVector(relDispTop);
                    AddContribution(topHs, SafeK(kVecTop.Kx), SafeK(kVecTop.Ky));

                    var relDisplacementBtm = btmEmb.CumulativeDisp - btmSoil.CumulativeDisp;
                    var kVecBtm = isTan ? item.GetTangentStiffnessVector(relDisplacementBtm) : item.GetSecantStiffnessVector(relDisplacementBtm);
                    AddContribution(btmHs, SafeK(kVecBtm.Kx), SafeK(kVecBtm.Ky));
                }

                foreach (var kvp in accum)
                {
                    double kxTotal = kvp.Value.kx;
                    double kyTotal = kvp.Value.ky;
                    kvp.Key.SetKe(kxTotal, kyTotal, 0, 0, 0, 0, isTan);
                    springMin = Math.Min(springMin, Math.Min(kxTotal, kyTotal));
                    springMax = Math.Max(springMax, Math.Max(kxTotal, kyTotal));
                }
            }

            // 杭ばね
            //
            // v23 (B-1): 接線剛性では 2D Jacobian（交差項あり）を使用する。
            // 旧実装は K_tan を対角 (k, k) として set していたが、p–y 曲線の力は
            // f = p(|u|) × u/|u| の形で「変位方向に沿う」ため、非線形領域では
            // df/du は 対称 2x2 ブロック [[kxx, kxy],[kxy, kyy]] になる。
            // この真の Jacobian を使うことで Newton 方向が改善し α=1.0 に近い収束を期待できる。
            // 割線剛性（isTan=false）側は F_int = K_sec(|u|) × u（等方）で正しく力を表現するため
            // 従来通り対角で OK（secant × disp がそのまま force を返す性質を維持）。
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var horizontalReactions = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1].HorizontalSoilReactions;
                var isFrontPile = pileLayoutItem.IsFrontPiles[iLC];

                // E3b: case-local な PileNodes / SoilNodes / HorizontalSoilSprings を取得
                var pileNodes = model.GetPileNodes(pileLayoutItem);
                var soilNodes = model.GetSoilNodes(pileLayoutItem);
                var pileSprings = model.GetPileHorizontalSoilSprings(pileLayoutItem);

                int reactionCount = horizontalReactions.Count;
                for (int i = 0; i < pileNodes.Count; i++)
                {
                    var pileNode = pileNodes[i];
                    var soilNode = soilNodes[i];
                    var relDisplacement = pileNode.CumulativeDisp - soilNode.CumulativeDisp;
                    // NaN防止
                    double abs = (double.IsFinite(relDisplacement.Ux) && double.IsFinite(relDisplacement.Uy))
                        ? Math.Sqrt(relDisplacement.Ux * relDisplacement.Ux + relDisplacement.Uy * relDisplacement.Uy)
                        : 0.0;

                    // 接線・割線両方を蓄積（2D Jacobian 用にどちらも必要）
                    double kTan = 0.0;
                    double kSec = 0.0;
                    if (i > 0 && i - 1 < reactionCount)
                    {
                        bool isTop = false;
                        kTan += horizontalReactions[i - 1].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile);
                        kSec += horizontalReactions[i - 1].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile);
                    }
                    if (i < pileNodes.Count - 1 && i < reactionCount)
                    {
                        bool isTop = true;
                        kTan += horizontalReactions[i].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile);
                        kSec += horizontalReactions[i].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile);
                    }

                    kTan = SafeK(kTan);
                    kSec = SafeK(kSec);

                    // 2026-05-06 簡素化: 単一節点で k=0 となるケース (砂質土の有効上載圧 σv'=0 等) は、
                    // 杭が他深さの地盤ばねで支持されているため解析の安定性に問題はない。
                    // ただし「杭全体で k=0」(杭全体が地表より上にある等) の極端なケースでは
                    // 剛性マトリクスが特異化して解析が解けなくなるため、最上端節点 (i=0) のみ
                    // 物理的に無視できる極小値 1e-3 kN/m で代用する保険的処置を残す。
                    if ((isTan ? kTan : kSec) <= 0.0 && i == 0)
                    {
                        const double KFloor = 1.0e-3; // kN/m, 物理的に無視できる値
                        if (kTan <= 0.0) kTan = KFloor;
                        if (kSec <= 0.0) kSec = KFloor;
                    }

                    var spring = pileSprings[i];

                    if (isTan)
                    {
                        // v23 (B-1) 接線剛性: 2D Jacobian
                        // |u| が極小なら方向が不定なので等方に縮退（従来通り）
                        double kxxDiag, kxyOff, kyyDiag;
                        if (abs < 1e-12)
                        {
                            kxxDiag = kTan; kyyDiag = kTan; kxyOff = 0.0;
                        }
                        else
                        {
                            double cosT = relDisplacement.Ux / abs;
                            double sinT = relDisplacement.Uy / abs;
                            kxxDiag = kTan * cosT * cosT + kSec * sinT * sinT;
                            kyyDiag = kTan * sinT * sinT + kSec * cosT * cosT;
                            kxyOff = (kTan - kSec) * cosT * sinT;
                        }
                        spring.SetKeWithXYCoupling(kxxDiag, kxyOff, kyyDiag, 0, 0, 0, 0, isTan: true);
                        springMin = Math.Min(springMin, Math.Min(kxxDiag, kyyDiag));
                        springMax = Math.Max(springMax, Math.Max(kxxDiag, kyyDiag));
                    }
                    else
                    {
                        // 割線剛性: 等方 (K_sec × disp がそのまま force を向ける)
                        spring.SetKe(kSec, kSec, 0, 0, 0, 0, isTan: false);
                        springMin = Math.Min(springMin, kSec);
                        springMax = Math.Max(springMax, kSec);
                    }
                }
            }

            // 追加: 杭頭 M-θ を RotationalSpring の Ke に反映
            if (model?.RotationalSprings != null && model.RotationalSprings.Count > 0)
            {
                // M-θ曲線から接線/割線剛性を評価（クランプなし: K/F整合性を保つ）
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    // E3b: case-local な RotationalSpring を取得
                    var rxy = model.GetPileTopRotationalSpring(pile);
                    if (rxy == null) continue;

                    var pileHeadNode = rxy.NodeJ;
                    var capNode = rxy.NodeI;
                    double dRx = (pileHeadNode.CumulativeDisp?.Rx ?? 0.0) - (capNode.CumulativeDisp?.Rx ?? 0.0);
                    double dRy = (pileHeadNode.CumulativeDisp?.Ry ?? 0.0) - (capNode.CumulativeDisp?.Ry ?? 0.0);

                    double kRx = 0.0, kRy = 0.0;
                    double kRxy = 0.0;       // v28 問題 B: Rx-Ry off-diagonal (2D Jacobian)
                    bool useRxRyCoupling = false;
                    if (rxy.Mode == RotationalSpringMode.CombinedXY)
                    {
                        const double KBigRigid = 1e10;  // SetupNonlinearMThetaForLoadCase の KBig と同値

                        // v28 アプローチ I: 場所打ち RC 杭 post-crack で方向ロック + ヒステリシス
                        if (rxy.McrXY.HasValue && rxy.HasCrackedXY
                            && rxy.CrackNx.HasValue && rxy.CrackNy.HasValue && rxy.CurveXY != null)
                        {
                            double nx = rxy.CrackNx.Value;
                            double ny = rxy.CrackNy.Value;
                            // n 方向への投影 (符号付き): forward なら +、reverse なら -
                            double thetaProj = dRx * nx + dRy * ny;

                            // ヒステリシス: θ_proj_max の更新 (前進時のみ大きくなる)
                            if (thetaProj > rxy.ThetaProjMax) rxy.ThetaProjMax = thetaProj;

                            // 2026-05-06 (A): forward / unloading branch の K_tan が境界 (thetaProj = thetaMax) で
                            // 100× ジャンプして Newton 方向を毎反復激変させる問題の対策。
                            // K_tan を境界周辺で smooth blend し連続化する (K_sec は元々連続なので変更不要)。
                            //   forward (thetaProj >= thetaMax)               → K_post_crack
                            //   transition (thetaMax - δ <= thetaProj < thetaMax) → smoothstep blend
                            //   pure unloading (thetaProj < thetaMax - δ)      → K_unload
                            // δ = max(5% × thetaMax, 1e-6) の幅で smoothstep (3t² − 2t³)。
                            // F_int は K_sec × disp で計算されるため物理的整合性は保たれる
                            // (transition zone でも K_tan は単に「Newton 方向の安定化用近似 Jacobian」)。
                            double thetaMax = rxy.ThetaProjMax;
                            double mMaxLock = rxy.CurveXY.EvaluateMoment(thetaMax);
                            double kUnload = (thetaMax > 1e-15)
                                ? SafeK(Math.Abs(mMaxLock) / thetaMax)
                                : SafeK(rxy.CurveXY.EvaluateTangent(0));

                            double kParTan, kParSec;  // n 方向の接線/割線剛性
                            if (thetaProj >= thetaMax - 1e-15)
                            {
                                // 前進: post-crack curve (1e-8 の急勾配はバイパス)
                                double absProj = Math.Abs(thetaProj);
                                kParTan = SafeK(rxy.CurveXY.EvaluatePostCrackTangent(absProj));
                                kParSec = SafeK(rxy.CurveXY.EvaluateSecant(absProj));
                            }
                            else
                            {
                                // 除荷: K_sec は線形戻り (M_max/thetaMax)、K_tan は境界周辺で smooth blend
                                kParSec = kUnload;
                                double delta = thetaMax - thetaProj;  // > 0 in unloading
                                double transitionWidth = Math.Max(0.05 * Math.Abs(thetaMax), 1e-6);
                                if (delta < transitionWidth && transitionWidth > 0.0)
                                {
                                    // smooth blend: K_post_crack (delta=0) → K_unload (delta=transitionWidth)
                                    double t = delta / transitionWidth;
                                    double s = t * t * (3.0 - 2.0 * t);  // smoothstep
                                    double kFwdAtBoundary = SafeK(rxy.CurveXY.EvaluatePostCrackTangent(thetaMax));
                                    kParTan = (1.0 - s) * kFwdAtBoundary + s * kUnload;
                                }
                                else
                                {
                                    kParTan = kUnload;
                                }
                            }

                            // ランク 1 + 小さな直交剛性 (数値的安定化)
                            // n 方向: kParallel (剛性フル)
                            // 直交方向: kPerp = kParallel × 0.05 (5%, 特異行列防止)
                            const double PERP_RATIO = 0.05;
                            double kParallel = isTan ? kParTan : kParSec;
                            double kPerp = kParallel * PERP_RATIO;
                            double nx2 = nx * nx;
                            double ny2 = ny * ny;
                            double nxy = nx * ny;
                            kRx = kParallel * nx2 + kPerp * ny2;    // kRxx
                            kRy = kParallel * ny2 + kPerp * nx2;    // kRyy
                            kRxy = (kParallel - kPerp) * nxy;        // off-diagonal
                            useRxRyCoupling = true;
                        }
                        else
                        {
                            // 未クラック / 他杭種: 従来の等方モデル (2D Jacobian)
                            double theta = Math.Sqrt(dRx * dRx + dRy * dRy);
                            double kTanIso, kSecIso;
                            if (rxy.CurveXY != null)
                            {
                                if (rxy.McrXY.HasValue && !rxy.HasCrackedXY)
                                {
                                    kTanIso = KBigRigid;
                                    kSecIso = KBigRigid;
                                }
                                else
                                {
                                    kTanIso = SafeK(rxy.CurveXY.EvaluateTangent(theta));
                                    kSecIso = SafeK(rxy.CurveXY.EvaluateSecant(theta));
                                }
                            }
                            else
                            {
                                kTanIso = kSecIso = SafeK(rxy.KthetaXY ?? 0.0);
                            }

                            // isotropic 2D Jacobian: K_tan ≠ K_sec で off-diagonal 発生
                            if (isTan && theta >= 1e-12 && Math.Abs(kTanIso - kSecIso) > 1e-6 * Math.Max(kTanIso, kSecIso))
                            {
                                double cosA = dRx / theta;
                                double sinA = dRy / theta;
                                double cos2 = cosA * cosA;
                                double sin2 = sinA * sinA;
                                kRx = kTanIso * cos2 + kSecIso * sin2;
                                kRy = kTanIso * sin2 + kSecIso * cos2;
                                kRxy = (kTanIso - kSecIso) * cosA * sinA;
                                useRxRyCoupling = true;
                            }
                            else
                            {
                                double k = isTan ? kTanIso : kSecIso;
                                kRx = k; kRy = k; kRxy = 0.0;
                            }
                        }
                    }
                    else
                    {
                        // SingleDof モード
                        if (rxy.Dof == RotationalDof.Rx)
                        {
                            double k;
                            if (rxy.Curve != null)
                            {
                                k = isTan
                                    ? SafeK(rxy.Curve.EvaluateTangent(dRx))
                                    : SafeK(rxy.Curve.EvaluateSecant(dRx));
                            }
                            else
                            {
                                k = SafeK(rxy.Ktheta ?? 0.0);
                            }
                            kRx = k;
                        }
                        else if (rxy.Dof == RotationalDof.Ry)
                        {
                            double k;
                            if (rxy.Curve != null)
                            {
                                k = isTan
                                    ? SafeK(rxy.Curve.EvaluateTangent(dRy))
                                    : SafeK(rxy.Curve.EvaluateSecant(dRy));
                            }
                            else
                            {
                                k = SafeK(rxy.Ktheta ?? 0.0);
                            }
                            kRy = k;
                        }
                    }

                    // 並進(Ux,Uy,Uz)・Rz の拘束:
                    // PileNode-0 は CapNode の master-slave として拘束されるべきだが、
                    // Boundaryの設定タイミングにより拘束が不完全な場合がある。
                    // そのため常にペナルティ剛性を適用して安全にする。
                    // Uz はペナルティのみで CapNode.Uz に追従させるため、
                    // Beam 軸剛性 EA/L (~2.7E+8) に対して十分大きい値が必要。
                    // Kbig=1e8 では EA/L の36%しかなく収束が100反復以上必要だった。
                    const double KBig = 1e8;
                    double kx = rxy.TieUx ? KBig : 0.0;
                    double ky = rxy.TieUy ? KBig : 0.0;
                    double kz = rxy.TieUz ? KBig : 0.0;
                    double kRz = rxy.TieRz ? KBig : 0.0;
                    // Rx/Ry は M–θ に基づき算出した kRx/kRy を用いる
                    // v28 問題 B: 2D Jacobian 有効時は Rx-Ry off-diagonal 付き K を使用
                    if (useRxRyCoupling)
                        rxy.SetKeWithRxRyCoupling(kx, ky, kz, kRx, kRxy, kRy, kRz, isTan);
                    else
                        rxy.SetKe(kx, ky, kz, kRx, kRy, kRz, isTan);
                }
            }

            springKMin = double.IsInfinity(springMin) ? double.NaN : springMin;
            springKMax = double.IsNegativeInfinity(springMax) ? double.NaN : springMax;
        }


        // ばね剛性の安全化ヘルパ
        //private static double SafeK(double v)
        //    => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        // 変更: ラッパーに model 引数を追加
        private void PrepareKTanMat(int iLC, AnaModel model) => PrepareKmat(iLC, true, model, out _, out _);
        private void PrepareKSecMat(int iLC, AnaModel model) => PrepareKmat(iLC, false, model, out _, out _);

        // 既存: K組立本体（model を受け取る版）
        //private void PrepareKMat(int iLC, bool isTan, AnaModel model)
    }
}