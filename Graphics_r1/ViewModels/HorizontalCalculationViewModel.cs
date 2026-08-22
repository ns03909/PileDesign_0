using PileDesign.Constants;
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
                    Serilog.Log.Debug($"[HorizontalCalcVM init] UseManaged も失敗: {inner.GetType().Name}: {inner.Message}");
                }
            }
            catch (Exception ex)
            {
                // 想定外の例外も捕捉して管理実装にフォールバック
                Serilog.Log.Debug($"[HorizontalCalcVM init] 想定外 ({ex.GetType().Name}: {ex.Message}) → UseManaged フォールバック");
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                    Serilog.Log.Debug($"[HorizontalCalcVM init] UseManaged も失敗: {inner.GetType().Name}: {inner.Message}");
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
        public enum StepStatus { Converged, Unconverged, PhysicallyUnconverged }
        public sealed record StepSummary(
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

        /// <summary>
        /// テスト用 (収束リグレッションテスト等): ステップ別収束サマリのスナップショット。
        /// 解析完了後に呼ぶことを想定。ケース順は実行順 (CaseTag でソートして使う)。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<StepSummary> StepSummariesSnapshot()
            => _stepSummaries.ToArray();

        /// <summary>
        /// テスト用フラグ: true に設定すると OnExecuteAnalysisCore 内の UI 確認ダイアログ
        /// (上書き確認 / ステップ数提案 / 杭頭半剛接合確認 / 偏心確認 等) を全てスキップし、
        /// "デフォルト承認" 動作で進める。本番ビルドでは常に false。
        /// </summary>
        public bool BypassUiPromptsForTesting { get; set; } = false;

        /// <summary>
        /// テストモード時は <paramref name="defaultForTest"/> を返す。本番モードは MessageService.Show をそのまま実行。
        /// </summary>
        private System.Windows.MessageBoxResult ConfirmOrDefault(
            string text, string caption,
            System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage image,
            System.Windows.MessageBoxResult defaultForTest)
        {
            if (BypassUiPromptsForTesting) return defaultForTest;
            return PileDesign.Services.MessageService.Show(text, caption, button, image);
        }

        /// <summary>
        /// 情報通知の MessageService.Show をテストモード時はスキップ。
        /// </summary>
        private void ShowInfoOrSkip(string text, string caption,
            System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage image)
        {
            if (BypassUiPromptsForTesting) return;
            PileDesign.Services.MessageService.Show(text, caption, button, image);
        }

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
                NotifyExecuteAnalysisToolTipChanged();
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
                NotifyExecuteAnalysisToolTipChanged();
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
                NotifyExecuteAnalysisToolTipChanged();
            }
        }

        // Newton-Raphsonモード選択
        // - Full NR (OFF): 毎反復で接線剛性+Kマトリクス更新（収束が速いが計算コスト高）
        // - Modified NR (ON): 適応的 - 最初の数回はFull NR、その後はKマトリクス再利用（高速化）
        // v29 (2026-04-27): Cholesky 因子再利用と組合せると Modified NR 後期反復で
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
                NotifyExecuteAnalysisToolTipChanged();
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

        // 杭先端 Z 境界: P-S 非線形ばね使用フラグ (InputModel に委譲)
        public bool UsePsSpringAtPileTip
        {
            get => InputModel?.UsePsSpringAtPileTip ?? false;
            set
            {
                if (InputModel != null && InputModel.UsePsSpringAtPileTip != value)
                {
                    InputModel.UsePsSpringAtPileTip = value;
                    OnPropertyChanged();
                    // P-S 非線形ばね ON 時は、FEM が N0+ΔN を含む軸力を出すため、
                    // M-φ N 評価には「入力値＋応力解析結果」しか整合しない。
                    // 自動で UseAnalysisAxialForce を ON にする (OFF 切替時はユーザ任意)。
                    if (value && !UseAnalysisAxialForce)
                        UseAnalysisAxialForce = true;
                    OnPropertyChanged(nameof(CanSelectInputOnlyAxialMode));
                    // VL 単独ケースの ON/OFF 可否が変わる → TotalCalculationCount の表示も更新
                    OnPropertyChanged(nameof(TotalCalculationCount));
                    OnPropertyChanged(nameof(TotalLoadCaseCount));
                }
            }
        }

        /// <summary>
        /// 「杭軸力: 入力値」を選択可能か (P-S 非線形ばね OFF 時のみ true)。
        /// P-S 非線形ばね ON 時は強制的に「入力値＋応力解析結果」となる。
        /// </summary>
        public bool CanSelectInputOnlyAxialMode => !UsePsSpringAtPileTip;

        /// <summary>
        /// VL (常時) 単独ケース解析フラグ。P-S 非線形ばね ON 時のみ有効化可能。
        /// ON: 各杭頭に AxialForceVL を外力として適用した「水平荷重なし」ケースを追加解析。
        /// 結果は LoadName="VL" として結果ビューアに表示される。
        /// </summary>
        public bool IsVLAnalysisEnabled
        {
            get => InputModel?.IsVLAnalysisEnabled ?? false;
            set
            {
                if (InputModel != null && InputModel.IsVLAnalysisEnabled != value)
                {
                    InputModel.IsVLAnalysisEnabled = value;
                    OnPropertyChanged();
                    // VL ケース分のステップ数を加減算するため、表示も更新
                    OnPropertyChanged(nameof(TotalCalculationCount));
                    OnPropertyChanged(nameof(TotalLoadCaseCount));
                }
            }
        }

        // P-S 曲線ソース (InputModel に委譲)
        public PsSpringSourceMode PsSpringSource
        {
            get => InputModel?.PsSpringSource ?? PsSpringSourceMode.Normal;
            set
            {
                if (InputModel != null && InputModel.PsSpringSource != value)
                {
                    InputModel.PsSpringSource = value;
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
        // 'const' ではなく 'static readonly' とすることで、CS0162「到達できないコード」の警告を抑止し
        // 再有効化時のコードを保持する。
        private static readonly bool _useStepLevelCutback = false;
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
                NotifyExecuteAnalysisToolTipChanged();
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

        // 基礎梁が存在し、使用する材料・断面が定義済みか（剛床連結の選択可否）
        public bool HasFoundationBeams
        {
            get
            {
                var fbInput = InputModel.FoundationBeamInput;
                if (fbInput?.Beams == null || fbInput.Beams.Count == 0) return false;

                // 全梁要素の材料No・断面Noに対応する定義 (1-based 位置インデックス) が存在するかチェック
                int materialCount = fbInput.Materials?.Count ?? 0;
                int sectionCount = fbInput.Sections?.Count ?? 0;

                foreach (var beam in fbInput.Beams)
                {
                    if (beam.MaterialNo < 1 || beam.MaterialNo > materialCount) return false;
                    if (beam.SectionNo < 1 || beam.SectionNo > sectionCount) return false;
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
                    return "基礎梁が定義されていません。";

                int materialCount = fbInput.Materials?.Count ?? 0;
                int sectionCount = fbInput.Sections?.Count ?? 0;

                var missingMats = fbInput.Beams.Where(b => b.MaterialNo < 1 || b.MaterialNo > materialCount).Select(b => b.MaterialNo).Distinct().ToList();
                var missingSecs = fbInput.Beams.Where(b => b.SectionNo < 1 || b.SectionNo > sectionCount).Select(b => b.SectionNo).Distinct().ToList();

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

        // 解析実行済みフラグ (UI ボタン有効化等に使用。ウィンドウオープン時に既存結果が転写されると true になる)
        private bool _isAnalysisExecuted = false;
        public bool IsAnalysisExecuted
        {
            get => _isAnalysisExecuted;
            set => SetProperty(ref _isAnalysisExecuted, value);
        }

        // このウィンドウセッション内で新規/追加解析を回したか (キャンセル確認用)。
        // 単にウィンドウを開いて既存結果を転写しただけでは true にしない。
        private bool _hasUnsavedAnalysisChange = false;

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
        /// <summary>実行ボタンのツールチップ (実行できない理由) の再評価を促す。</summary>
        private void NotifyExecuteAnalysisToolTipChanged()
        {
            OnPropertyChanged(nameof(ExecuteAnalysisDisabledReason));
            OnPropertyChanged(nameof(ExecuteAnalysisToolTip));
        }

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

                // 適用されている荷重組合せの数
                int combinationCount = InputModel.LoadCasesInput.AllLoadCombinations?.Count(x => x.IsApplicable) ?? 0;

                // 1荷重あたりレベル1解析計算ステップ数
                int level1Steps = Level1CalculationStepsCount;

                // 1荷重あたりレベル2解析計算ステップ数
                int level2Steps = Level2CalculationStepsCount;

                // 計算式（基本 + 再試行分）
                int baseTotal = liquefactionFactor * (level1Count * level1Steps + level2Count * level2Steps) * combinationCount;

                // VL 単独擬似ケース (P-S 非線形ばね有効 + VL 単独解析オプション ON)
                // 構成: 1 ケース × 1 組合せ (先頭固定) × 液状化 false 固定 (1) × Level1 ステップ数
                if (InputModel?.UsePsSpringAtPileTip == true && InputModel?.IsVLAnalysisEnabled == true)
                {
                    baseTotal += level1Steps;
                }

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
                int total = liquefactionFactor * (level1Count + level2Count) * combinationCount;
                // VL 単独擬似ケース: 1 ケース × 1 組合せ × 液状化 false 固定 → +1
                if (InputModel?.UsePsSpringAtPileTip == true && InputModel?.IsVLAnalysisEnabled == true)
                    total += 1;
                return total;
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
                NotifyExecuteAnalysisToolTipChanged();
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
                NotifyExecuteAnalysisToolTipChanged();
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
                // Serilog.Log.Debug($"[InitializeModelAsync] AnalysisModelling total: {swTotal.ElapsedMilliseconds}ms");

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

            // 解析後に入力が編集されていれば転写しない。
            // 既存結果は解析時の入力に対するもので、現在の入力に対しては無効。
            // 転写すると「済」列が既に解析済みと主張し、追加実行で新旧の結果が混ざる。
            // (以前は入力変更時に結果を破棄していたのでこの判定は不要だったが、
            //  結果を保持するようになったため明示的に見る必要がある)
            if (_mainWindowViewModel?.InputChangedSinceAnalysis == true) return;

            // 構造一致チェック (件数のみ)
            if (mainModel.Nodes?.Count != editModel.Nodes?.Count) return;
            if (mainModel.Beams?.Count != editModel.Beams?.Count) return;

            // ステップ結果コピー (LoadCase/LoadCombination は InputModel 経由で共有)
            editModel.AnalysisStepResults.Clear();
            foreach (var r in mainModel.AnalysisStepResults)
                editModel.AnalysisStepResults.Add(r);

            // LastRunConfig 転写 (互換性検証で参照される)
            editModel.LastRunConfig = mainModel.LastRunConfig;

            // 転写した結果は main 側の解析実行時オプションによるもの → 記録も一緒に転写
            editModel.ConcreteOptionsSignature = mainModel.ConcreteOptionsSignature;

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
                // 入力ごと複製して切り離す。以降 vm.CurrentModel は複製を指し、
                // 入力を編集しても結果は影響を受けない。
                vm.CaptureAnalysisResultSet();
                vm.RefreshResultTablesFromLastStep(); // 追加

                // 解析した 1 つ目の荷重ケースをメイン画面で選択状態にする
                // (既定の "VL" は地震時水平解析の対象ではないため、結果表示が空にならないよう切替える)
                var firstAnalyzedCase = this.CurrentModel?.AnalysisStepResults?
                    .FirstOrDefault()?.LoadCase?.LoadName;
                if (!string.IsNullOrEmpty(firstAnalyzedCase))
                    vm.SelectedLoadCaseName = firstAnalyzedCase;
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // このセッションで新規/追加解析を実行した場合のみ確認メッセージを表示。
            // ウィンドウを開いただけ (= 既存結果が転写されただけ) なら確認なしで閉じる。
            if (_hasUnsavedAnalysisChange)
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
                    MessageService.Show("基礎梁が定義されていないため、剛体連結モードに切り替えて解析を実行します。",
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

                // v2 セマンティクス: pile.Z は接合節点 Z なので、杭頭は PileHeadZ
                double pileTopZ = pile.PileHeadZ;
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
            var blocker = DescribeAnalysisBlocker();
            if (blocker != null)
            {
                // 診断ログ — 実行ボタンが灰色のままになる原因を即座に追跡できるよう毎回出力
                // Warning レベルで出力 (Info より確実にログに残る + filter で見つけやすい)
                Serilog.Log.Warning("[水平解析 disabled] {Reason}", blocker.Value.Detail);
                return false;
            }
            // 活性時も 1 度ログを出して、CanExecute が確実に呼ばれていることを確認できるように
            Serilog.Log.Information("[水平解析 enabled] CanExecuteAnalysis returned true");
            return true;
        }

        /// <summary>
        /// 水平解析を実行できない理由 (利用者向けの言葉)。実行できるときは null。
        /// 実行ボタンのツールチップにそのまま出す。
        ///
        /// 以前は理由を組み立てていながら出力先が Serilog だけで、
        /// 画面には灰色のボタンしか見えなかった。
        /// </summary>
        public string? ExecuteAnalysisDisabledReason => DescribeAnalysisBlocker()?.User;

        /// <summary>実行ボタンのツールチップ。実行できるときは操作の説明を出す。</summary>
        public string ExecuteAnalysisToolTip =>
            ExecuteAnalysisDisabledReason ?? "選択した荷重ケース・荷重組合せについて水平解析を実行します (F5)。";

        /// <summary>
        /// 実行を止めている条件を 1 つ返す。User は画面に出す文、Detail はログ用。
        ///
        /// User には内部の識別子 (PileLayoutItems・IsAnalysisTarget など) を出さず、
        /// 「どこで何をすれば直るか」を書くこと。
        /// </summary>
        private (string User, string Detail)? DescribeAnalysisBlocker()
        {
            if (IsAnalysisRunning)
                return ("解析を実行中です。完了するかキャンセルするまで待ってください。",
                        "IsAnalysisRunning=true (前回解析が完了/キャンセルしないまま固着の可能性)");

            if (Level1CalculationStepsCount < 1 || Level1CalculationStepsCount > 256)
                return ($"レベル1 の計算ステップ数を 1〜256 にしてください (現在 {Level1CalculationStepsCount})。",
                        $"Level1CalculationStepsCount={Level1CalculationStepsCount} (1-256 範囲外)");

            if (Level2CalculationStepsCount < 1 || Level2CalculationStepsCount > 256)
                return ($"レベル2 の計算ステップ数を 1〜256 にしてください (現在 {Level2CalculationStepsCount})。",
                        $"Level2CalculationStepsCount={Level2CalculationStepsCount} (1-256 範囲外)");

            if (MaxCaseDegreeOfParallelism < 1)
                return ($"ケース並列数を 1 以上にしてください (現在 {MaxCaseDegreeOfParallelism})。",
                        $"MaxCaseDegreeOfParallelism={MaxCaseDegreeOfParallelism} (<1)");

            if (FullNRIterations < 0)
                return ($"完全 Newton-Raphson の反復回数を 0 以上にしてください (現在 {FullNRIterations})。",
                        $"FullNRIterations={FullNRIterations} (<0)");

            if (RelaxationFactor <= 0 || RelaxationFactor > 1)
                return ($"緩和係数を 0 より大きく 1 以下にしてください (現在 {RelaxationFactor})。",
                        $"RelaxationFactor={RelaxationFactor} (0<x≤1 範囲外)");

            if (InputModel == null)
                return ("入力データが読み込まれていません。",
                        "InputModel == null");

            if ((InputModel.PileLayoutItems?.Count ?? 0) == 0)
                return ("杭が 1 本も配置されていません。メイン画面の「杭」タブで杭を追加してください。",
                        "PileLayoutItems が空");

            if ((InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases?.Count ?? 0) == 0)
                return ("解析対象の荷重ケースが 1 件もありません。荷重条件ウィンドウで「解析対象」にチェックを入れてください。",
                        "AnalysisTargetSeismicLoadCases が 0 件 (荷重ケースの IsAnalysisTarget=true を確認)");

            if ((InputModel.LoadCasesInput.AllLoadCombinations?.Count(c => c.IsApplicable) ?? 0) == 0)
                return ("適用する荷重組合せが 1 件もありません。荷重条件ウィンドウで「適用」にチェックを入れてください。",
                        "AllLoadCombinations の IsApplicable=true が 0 件");

            if (TotalCalculationCount == 0)
                return ("解析する計算が 1 件もありません。荷重が 0 のケースだけが選ばれていないか確認してください。",
                        "TotalCalculationCount == 0");

            return null;
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

            var inputWarnings = PileDesign.Models.CheckInputData.CollectInputWarnings(InputModel);

            return new Views.AnalysisPreflightSummary(
                AnalysisName: "水平解析",
                TotalSteps: totalSteps,
                LoadCaseCountText: loadCaseText,
                CombinationCount: combinationCount,
                CounterLoadingCount: counterLoadingCount,
                NonLinearLoadCaseCount: nonLinearCases,
                MaxParallelism: parallelism,
                InputWarnings: inputWarnings);
        }

        private async Task OnExecuteAnalysisCore(bool additive)
        {
            // 入力データの整合性ゲート (杭体・地盤・寸法・配筋など)
            if (!PileDesign.Models.CheckInputData.ValidateForAnalysis(
                    _mainWindowViewModel.CurrentInputModel, "水平解析"))
                return;

            // 既存の解析結果がある場合は警告 (新規実行のみ。追加実行は既存結果保持が前提なのでスキップ)
            // メイン側 (OK 済み) または、現セッションでロードした「済」結果のいずれかがあれば確認
            bool hasExistingResults = _mainWindowViewModel.IsHorizontalAnalysisDone
                || (TryGetTargetAnaModel()?.AnalysisStepResults?.Count > 0);
            if (!additive && hasExistingResults)
            {
                var result = ConfirmOrDefault(
                    "既に水平解析の結果が存在します。\n再実行すると既存の結果は上書きされます。\n\n解析を実行しますか？",
                    "解析結果の上書き確認", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.Yes);
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
                var fbResult = ConfirmOrDefault(fbMsg, "基礎梁 材料・断面 未登録",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.OK);
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
                ShowInfoOrSkip(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

                // すべての荷重ケースがゼロの場合は解析を中止
                if (zeroForceLoadCases.Count == InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases.Count)
                {
                    return;
                }
            }

            // 2026-05-13: 偏心警告
            //   VL 時の軸力分布重心 (= 建物自重の作用中心) と慣性力中心 (ForceActionPoint) の距離を測り、
            //   バウンディングボックス対角長に対する比率で評価。
            //   偏心が大きいと limit cycle 等で収束が困難になるためユーザーに事前警告。
            //   OK/キャンセルダイアログでユーザーが確認の上で続行できる。
            if (!CheckEccentricityAndConfirm())
                return;

            // 場所打ち鋼管コンクリート杭の最上段区間が「鉄筋コンクリート部」になっていないかチェック
            // (通常、最上段は「鋼管コンクリート部」を選択する)
            var insituSteelPipeIssues = PileBodyViewModel.CheckInsituSteelPipeTopSection(InputModel.PileBodies);
            if (insituSteelPipeIssues.Count > 0)
            {
                string list = string.Join(", ", insituSteelPipeIssues);
                var result = ConfirmOrDefault(
                    $"以下の杭体で、最上段区間が「鉄筋コンクリート部」になっています:\n{list}\n\n" +
                    "場所打ち鋼管コンクリート杭の最上段は通常「鋼管コンクリート部」を選択します。\n" +
                    "このまま解析を続行しますか？",
                    "杭断面タイプの確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (result != MessageBoxResult.Yes) return;
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
                                   $"以下の荷重ケースで「杭の非線形」が無効になっています:\n{string.Join(", ", levelNames)}\n\n" +
                                   "半剛接合の効果を考慮するには杭の非線形を有効にする必要があります。\n" +
                                   "すべての荷重ケースで杭の非線形を有効にしますか？";

                    // テスト時は既存設定をそのまま使う (No 既定) ことで、スナップショットの再現性を担保
                    var result = ConfirmOrDefault(message, "杭頭半剛接合の確認",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

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

            // 杭の非線形が有効で解析ステップ数が少ない場合の警告
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
                    var message = $"杭の非線形解析が有効ですが、解析ステップ数が少ない可能性があります。\n\n" +
                                   suggestedAction +
                                   "\n収束性や精度を向上させるため、解析ステップ数を増やすことをお勧めします。\n" +
                                   "（推奨: レベル1は4以上、レベル2は16〜32）\n\n" +
                                   "解析ステップ数を変更しますか？";

                    // テスト時はステップ数を変更しない (既存設定を尊重)
                    var result = ConfirmOrDefault(message, "解析ステップ数の確認",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

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
                        ShowInfoOrSkip($"解析ステップ数を更新しました。\n" +
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
                    var choice = ConfirmOrDefault(
                        $"追加実行できません。前回設定と相違があります。\n\n{reason}\n\n" +
                        "「はい」: 既存結果を破棄して新規実行に切替\n" +
                        "「いいえ」: キャンセル",
                        "互換性チェック失敗", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.Yes);
                    if (choice != MessageBoxResult.Yes) return;
                    additive = false;
                }
            }

            // プリフライト: ステップ数 / 並列度 / CounterLoading / 推定時間をユーザーに提示し、
            // 実行可否を最終確認する (User 設定で無効化可)。追加実行はスキップ (差分のため意味が薄い)。
            if (!additive && !BypassUiPromptsForTesting)
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

            // 断面計算フォールバック（計算失敗→既定値代替）の区間集計を開始
            PileDesign.Common.CalcFallbackTracker.Reset();

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
                _hasUnsavedAnalysisChange = true; // 新規解析の結果を保持。キャンセル時の確認対象

                // 解析実行時点の材料モデル化オプションを記録 (docx 出力時の照合用)
                if (AnaModels.Count > 0)
                    AnaModels[^1].ConcreteOptionsSignature = ConcreteModelOptions.Signature();

                // 断面計算が既定値で代替された件数は、解析ログとログファイルにだけ残す。
                // 完了ダイアログには出さない (実装都合の用語で、読んでも次の操作が決まらないため)。
                long fallbackCount = PileDesign.Common.CalcFallbackTracker.TotalCount;
                string doneMessage = "計算が終了しました。";
                if (fallbackCount > 0)
                {
                    string fallbackSummary = PileDesign.Common.CalcFallbackTracker.BuildSummary();
                    await AddLogAsync($"断面計算を既定値で代替した箇所: {fallbackCount} 件\n{fallbackSummary}");
                }

                // 計算完了通知（UIスレッドで直接表示）
                // owner を HorizontalCalculationWindow に明示固定して、解析完了直後にフォーカスが
                // MainWindow に移っていてもダイアログが水平解析ウィンドウの上に表示されるようにする。
                if (!BypassUiPromptsForTesting)
                {
                    var doneIcon = MessageBoxImage.Information;
                    var horizontalWindow = System.Windows.Application.Current?.Windows
                        .Cast<System.Windows.Window>()
                        .FirstOrDefault(w => ReferenceEquals(w.DataContext, this));
                    if (horizontalWindow != null)
                    {
                        horizontalWindow.Activate();
                        MessageService.Show(horizontalWindow, doneMessage, "完了", MessageBoxButton.OK, doneIcon);
                    }
                    else
                    {
                        MessageService.Show(doneMessage, "完了", MessageBoxButton.OK, doneIcon);
                    }
                }
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


        /// <summary>
        /// 慣性力中心 (荷重ケースの ForceActionPoint) と 2 種類の杭群中心を比較し、
        /// 偏心率 (BBox 対角長比) が大きい場合はユーザーに警告ダイアログを表示する。
        ///   1) VL 時の杭軸力分布重心 (= 建物自重の作用中心、質量中心相当)
        ///   2) 杭頭 kh0 で重み付けした剛心 (= 水平剛性中心)
        /// 偏心が大きいと limit cycle 等で水平解析の収束が困難になるため事前確認用。
        /// </summary>
        /// <returns>true: 解析続行 OK / false: ユーザーがキャンセル選択</returns>
        private bool CheckEccentricityAndConfirm()
        {
            if (InputModel?.PileLayoutItems == null || InputModel.PileLayoutItems.Count == 0)
                return true;

            var soilPiles = InputModel.ElementDivision?.SoilPiles;

            // 杭群バウンディングボックス & 2 種の重心
            double sumNX = 0, sumNY = 0, sumN = 0;         // (1) VL 軸力重み
            double sumKX = 0, sumKY = 0, sumK = 0;         // (2) Kh0 重み (剛心)
            double sumX = 0, sumY = 0;                      // フォールバック用 幾何重心
            int count = 0;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            foreach (var pli in InputModel.PileLayoutItems)
            {
                if (pli == null) continue;
                sumX += pli.X;
                sumY += pli.Y;
                count++;
                minX = Math.Min(minX, pli.X);
                maxX = Math.Max(maxX, pli.X);
                minY = Math.Min(minY, pli.Y);
                maxY = Math.Max(maxY, pli.Y);

                // (1) VL 軸力 (圧縮を正とする)。引張杭 / ゼロは除外して「自重を受ける杭」を抽出
                double nVL = pli.AxialForceVL0 + pli.AxialForceVLAdditional;
                if (nVL > 0)
                {
                    sumNX += pli.X * nVL;
                    sumNY += pli.Y * nVL;
                    sumN += nVL;
                }

                // (2) 杭頭 kh0 (= SoilPile の最上層 HorizontalSoilReaction.Kh0)
                if (soilPiles != null)
                {
                    int soilIdx = pli.SoilPileAltNo - 1;
                    if (soilIdx >= 0 && soilIdx < soilPiles.Count)
                    {
                        var sp = soilPiles[soilIdx];
                        if (sp?.HorizontalSoilReactions != null && sp.HorizontalSoilReactions.Count > 0)
                        {
                            double kh0 = sp.HorizontalSoilReactions[0].Kh0;
                            if (kh0 > 0)
                            {
                                sumKX += pli.X * kh0;
                                sumKY += pli.Y * kh0;
                                sumK += kh0;
                            }
                        }
                    }
                }
            }
            if (count == 0) return true;

            // 重心 (フォールバック付き)
            double geomCx = sumX / count, geomCy = sumY / count;
            double vlCx = (sumN > 1e-6) ? sumNX / sumN : geomCx;
            double vlCy = (sumN > 1e-6) ? sumNY / sumN : geomCy;
            double rgCx = (sumK > 1e-9) ? sumKX / sumK : geomCx;
            double rgCy = (sumK > 1e-9) ? sumKY / sumK : geomCy;
            bool hasRigidity = sumK > 1e-9;

            // 弾性半径 r_e = √(K_R / K_total)
            //   K_R  = Σ(kh0_i × r_i²)   (r_i: pile_i から剛心までの距離)
            //   K_total = Σ(kh0_i)        (等方水平剛性仮定)
            //   → 杭群の kh0 重み付け二乗平均半径 (= 回転剛性 / 並進剛性 の比のルート)
            //   基準法施行令 82 条の 6 の 偏心率 Re = e / r_e の計算基盤。
            double rE = double.NaN;
            if (hasRigidity)
            {
                double sumKR2 = 0;
                foreach (var pli in InputModel.PileLayoutItems)
                {
                    if (pli == null) continue;
                    int soilIdx = pli.SoilPileAltNo - 1;
                    if (soilPiles == null || soilIdx < 0 || soilIdx >= soilPiles.Count) continue;
                    var sp = soilPiles[soilIdx];
                    if (sp?.HorizontalSoilReactions == null || sp.HorizontalSoilReactions.Count == 0) continue;
                    double kh0 = sp.HorizontalSoilReactions[0].Kh0;
                    if (kh0 <= 0) continue;
                    double dx = pli.X - rgCx;
                    double dy = pli.Y - rgCy;
                    sumKR2 += kh0 * (dx * dx + dy * dy);
                }
                if (sumK > 1e-9 && sumKR2 > 0)
                {
                    rE = Math.Sqrt(sumKR2 / sumK);
                }
            }

            // 適用対象荷重ケースで偏心率を計測 → 最大値で警告レベル判定
            //   Re_x = |apY - rgCy| / r_e   (X 方向加力の偏心率: Y 方向偏心がねじり起因)
            //   Re_y = |apX - rgCx| / r_e   (Y 方向加力の偏心率)
            //   両者の最大値で評価。基準法閾値: 0.15
            double maxRe = 0;             // max(Re_x, Re_y)
            double maxReX = 0, maxReY = 0;
            double maxEx = 0, maxEy = 0;
            double maxEVLDist = 0;
            string worstCaseName = "";
            double worstApX = 0, worstApY = 0;
            var targetCases = InputModel.LoadCasesInput?.AnalysisTargetSeismicLoadCases;
            if (targetCases == null) return true;
            foreach (var lc in targetCases)
            {
                if (lc == null) continue;
                double apX = lc.ForceActionPointX;
                double apY = lc.ForceActionPointY;

                // VL 重心からの距離 (参考情報)
                double dxVL = apX - vlCx, dyVL = apY - vlCy;
                double eVL = Math.Sqrt(dxVL * dxVL + dyVL * dyVL);

                // 偏心率 Re (剛心ベース、基準法施行令 82 条の 6)
                double reX = 0, reY = 0;
                double ex = 0, ey = 0;
                if (hasRigidity && double.IsFinite(rE) && rE > 1e-9)
                {
                    ex = Math.Abs(apX - rgCx);  // X 方向の偏心距離
                    ey = Math.Abs(apY - rgCy);  // Y 方向の偏心距離
                    reX = ey / rE;              // X 加力での偏心率 (Y偏心が起因)
                    reY = ex / rE;              // Y 加力での偏心率 (X偏心が起因)
                }

                double thisRe = Math.Max(reX, reY);
                if (thisRe > maxRe)
                {
                    maxRe = thisRe;
                    maxReX = reX;
                    maxReY = reY;
                    maxEx = ex;
                    maxEy = ey;
                    maxEVLDist = eVL;
                    worstCaseName = lc.LoadName ?? "";
                    worstApX = apX;
                    worstApY = apY;
                }
            }

            // 偏心率が剛心計算不可なら警告できない (旧 BBox 比率での簡易判定にフォールバック)
            if (!hasRigidity || !double.IsFinite(rE) || rE < 1e-9)
            {
                double bboxW = maxX - minX;
                double bboxH = maxY - minY;
                double lChar = Math.Sqrt(bboxW * bboxW + bboxH * bboxH);
                if (lChar < 1e-6 || maxEVLDist / lChar < 0.10) return true;
                string fmsg =
                    $"慣性力中心が VL 重心から離れています。\n\n" +
                    $"  偏心量 = {maxEVLDist:F2} m  (代表長 {lChar:F2} m)\n" +
                    $"  最悪荷重ケース: {worstCaseName}\n\n" +
                    $"(土層情報未設定のため剛心ベースの偏心率は計算できません)\n\n" +
                    $"このまま解析を続行しますか？\n" +
                    $"OK:続行　キャンセル：モデルを見直す";
                return ConfirmOrDefault(fmsg, "偏心の確認", MessageBoxButton.OKCancel,
                    MessageBoxImage.Information, MessageBoxResult.OK) == MessageBoxResult.OK;
            }

            // 偏心率閾値: 0.15 未満は警告なし
            const double RE_THRESHOLD = 0.15;
            if (maxRe < RE_THRESHOLD) return true;

            // 警告レベル判定
            //   Re < 0.15 : 標準範囲 (警告なし)
            //   Re < 0.30 : 中程度
            //   Re < 0.45 : 大
            //   Re ≥ 0.45 : 極大
            string severity;
            MessageBoxImage icon;
            if (maxRe < 0.30) { severity = "中程度"; icon = MessageBoxImage.Information; }
            else if (maxRe < 0.45) { severity = "大"; icon = MessageBoxImage.Warning; }
            else { severity = "極大"; icon = MessageBoxImage.Warning; }

            string msg =
                $"偏心レベル: {severity} (最大 Re = {maxRe:F2})\n" +
                $"  慣性力中心 = ({worstApX:F2}, {worstApY:F2}) m\n" +
                $"  剛心       = ({rgCx:F2}, {rgCy:F2}) m  ← kh0 重み付け\n" +
                $"  VL 重心   = ({vlCx:F2}, {vlCy:F2}) m  (慣性力中心との距離 {maxEVLDist:F2} m)\n\n" +
                $"  偏心率:\n" +
                $"    X 加力時 Re_x = {maxReX:F2}  (Y偏心 {maxEy:F2} m / r_e)\n" +
                $"    Y 加力時 Re_y = {maxReY:F2}  (X偏心 {maxEx:F2} m / r_e)\n\n" +
                $"大偏心ではねじれモーメントが発生し、水平解析の収束に時間がかかったり\n" +
                $"局所的に未収束となる可能性があります\n\n" +
                $"このまま解析を続行しますか？\n" +
                $"OK:続行　キャンセル：モデルを見直す";

            var result = ConfirmOrDefault(msg, "偏心率の確認",
                MessageBoxButton.OKCancel, icon, MessageBoxResult.OK);
            return result == MessageBoxResult.OK;
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

        private void ApplyPileHeadRigidBindingForLoadCase(AnaModel targetModel, LoadCase _loadCase)
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

        // 杭接合節点の解決 (案 Z: P-S 非線形ばねモード時に Z 外力を載せる/累積する節点)
        // 優先順位: ConnectionNode "FoundationNode-P{No}" (基礎梁設定時) → CapNode "CapNode-{No}" (フォールバック)
        // SetVectorDF と UpdateF で同じ resolution を使うために共通化。
        private static FEM.Node ResolvePileJointNodeInModel(AnaModel targetModel, int pileNo)
        {
            var conn = targetModel.Nodes.FirstOrDefault(n => n != null && n.Name == $"FoundationNode-P{pileNo}");
            if (conn != null) return conn;
            return targetModel.Nodes.FirstOrDefault(n => n != null && n.Name == $"CapNode-{pileNo}");
        }

        // 荷重ベクトルの更新メソッド　F = F + dF
        private void UpdateF(AnaModel targetModel)
        {

            //AnaModel.MapOnVectorF();  // node.load, による F 更新
            targetModel.Nodes[0].UpdateCumulativeLoad(); // (kN) 節点荷重の代表節点へのセット

            // 案 Z (P-S 非線形ばね 有効時): 杭軸力は各杭の接合節点に外力として与えているため、
            // それらの IncrementalLoad も CumulativeLoad に反映する必要がある。
            // (これをしないと VectorF が cap Z 荷重を欠落させ、残差計算が破綻して 1e30 を返す)
            // 杭体自重も各杭節点に分布外力として与えているため、同様に累積反映する。
            if (InputModel.UsePsSpringAtPileTip && InputModel.PileLayoutItems != null)
            {
                foreach (var pli in InputModel.PileLayoutItems)
                {
                    var jointNode = ResolvePileJointNodeInModel(targetModel, pli.No);
                    jointNode?.UpdateCumulativeLoad();

                    // 杭節点自重の累積反映 (SetVectorDF の自重注入と対称)
                    // k=0 の自重は jointNode に集約済みなので k=0 はスキップ (上の jointNode.UpdateCumulativeLoad で反映済み)
                    var pileNodesForWeight = targetModel.GetPileNodes(pli);
                    var modelsForWeight = pli.VerticalNodeSpringModels;
                    if (pileNodesForWeight == null || modelsForWeight == null) continue;
                    int nw = Math.Min(pileNodesForWeight.Count, modelsForWeight.Count);
                    for (int k = 1; k < nw; k++)
                    {
                        var pn = pileNodesForWeight[k];
                        var md = modelsForWeight[k];
                        if (pn == null || md == null || md.Weight <= 0.0) continue;
                        pn.UpdateCumulativeLoad();
                    }
                }
            }

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
        // internal はテスト用 (AxialForceSourceAuditTests が「解析軸力を毎ステップ累積しない」ことを検証)
        internal void UpdateAxialForceFromAnalysis(AnaModel targetModel)
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

                if (InputModel.UsePsSpringAtPileTip)
                {
                    // 案 Z モード: 杭軸力 N0 は SetVectorDF で AP に外力として与えられているため、
                    // FEM Fxi は (N0 + ΔN_seis) を含む合計軸力を表す (compression negative)。
                    // 既存式 current - fxiAnalysis は N0 を二重計上するので、直接 -fxiAnalysis を代入。
                    // 結果: pile.AxialForce = -fxiAnalysis = N0 + ΔN_seis (compression positive) ✓
                    targetModel.SetAxialForce(pile, -fxiAnalysis);
                }
                else
                {
                    // 通常モード: 入力軸力（圧縮が正）に解析結果（圧縮が負）を加算 → 符号反転
                    // AxialForce = 入力値による軸力 + (-Fxi_analysis)
                    //
                    // Fxi は「そのステップまでの累積軸力」であって増分ではない。単純に毎ステップ
                    // 引くと過去ステップ分の Fxi が積み上がり、解析軸力の寄与が約 (nStep+1)/2 倍に
                    // 膨らむ (2026-08-21 修正。Example10 L1 4 ステップで 17.0 → 33.9 と 2 倍を実測)。
                    // そこで前ステップで適用済みの分を打ち消してから今ステップの値を適用する。
                    // E3b: CaseLocalSnapshot 経由で読書き。主モデルでは pile.AxialForce を直接更新 (従来挙動)、
                    // case-local コピーでは snapshot.AxialForces[pile] を更新。
                    double current = targetModel.GetAxialForce(pile);
                    double appliedPrev = targetModel.GetAppliedAnalysisAxialForce(pile);
                    targetModel.SetAxialForce(pile, current + appliedPrev - fxiAnalysis); // 圧縮増 → Fxi負 → -(-) = 加算
                }

                targetModel.SetAppliedAnalysisAxialForce(pile, fxiAnalysis);
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
                // Serilog.Log.Debug(log.ToString());
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
        internal enum LoadCombinationDirection { Forward, CounterLoading }

        /// <summary>
        /// v28 (2026-04-23): βU × βL の符号のみで分類。
        /// 物理的根拠: 逆方向組合せは杭頭と杭体下部で反対向きの曲げ (S字曲げ) が発生し、
        /// 逆符号の塑性ヒンジが同時形成 → Newton 方向が接線不連続で振動しやすいため、
        /// 最初から小さな荷重ステップで開始する。
        /// Approach I で杭頭 Ry リミットサイクルが解決済みのため、高 αL や強 βU 液状化等の
        /// 静的分類は廃止。早期適応検出 (v26 案 B) が実測ベースで救済する。
        /// </summary>
        internal static LoadCombinationDirection ClassifyLoadCombinationDirection(LoadCase _lc, LoadCombination combo, bool _isLiq)
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
        internal static string BuildCaseTag(int level, int iLC, int iLCOM, bool isLiquefaction)
            => $"[L{level}-{iLC + 1}.C{iLCOM + 1}.{(isLiquefaction ? "Liq" : "NoLq")}] ";

        /// <summary>VL 仮想ケース対応のオーバーロード。VL なら [VL.C{iLCOM+1}.NoLq]。</summary>
        internal static string BuildCaseTag(LoadCase loadCase, int level, int iLC, int iLCOM, bool isLiquefaction)
            => loadCase != null && loadCase.LoadName == "VL"
                ? $"[VL.C{iLCOM + 1}] "
                : BuildCaseTag(level, iLC, iLCOM, isLiquefaction);

        /// <summary>
        /// v29 (2026-05-05): 退化トレンド検出 (複合条件版) の結果。
        ///   CaseFailedThisAttempt: 退化トレンド発火で本 attempt を失敗扱いにすべきか
        ///   RetryGateDisabled:     以降の retry を抑制すべきか (改善ゲート不通過)
        ///   PrevAttemptAvgIter:    本 attempt の平均反復数 (改善ゲート判定用に次 attempt へ持ち越し)
        ///   LogMessage:            検出時に出力すべきログ文 (なければ null)
        /// </summary>
        internal readonly record struct TrendCheckResult(
            bool CaseFailedThisAttempt,
            bool RetryGateDisabled,
            double PrevAttemptAvgIter,
            string? LogMessage);

        /// <summary>
        /// 退化トレンド検出 — 「直近 3 ステップで反復数が単調増加」AND「最新ステップが ≥ 60 反復」
        /// の両方を満たした場合のみ「真の退化トレンド」と判定して retry を要求する。
        /// 単に反復数が多いだけのケース (非線形性が強いだけのモデル) を誤検知しない。
        ///
        /// 改善ゲート (retry 中の attempt): 平均反復数が前 attempt 比で 10% 以上改善
        /// していなければ細分化が無効と判断し、以降の retry を抑制する。
        ///
        /// 引数の現状値をそのまま戻り値の初期値とし、変更が必要な分だけ書き換えて返す。
        /// </summary>
        internal static TrendCheckResult CheckDegenerationTrend(
            List<int> stepIterHistory,
            bool caseFailedThisAttempt,
            bool retryGateDisabled,
            bool physicallyUnconvergeable,
            int bisectionAttempt,
            int maxStepBisections,
            double prevAttemptAvgIter,
            int nStep)
        {
            const int TREND_OBS_STEPS = 3;
            const int TREND_HIGH_ITER_THRESHOLD = 60;
            const double RETRY_IMPROVEMENT_MIN_RATIO = 0.10;

            // 早期 return: 監視窓未満、既に失敗確定、物理的未収束、retry ゲート無効、retry 上限到達
            if (stepIterHistory.Count < TREND_OBS_STEPS
                || caseFailedThisAttempt || physicallyUnconvergeable
                || retryGateDisabled
                || bisectionAttempt >= maxStepBisections)
            {
                return new TrendCheckResult(caseFailedThisAttempt, retryGateDisabled, prevAttemptAvgIter, null);
            }

            int n = stepIterHistory.Count;
            int latest = stepIterHistory[n - 1];
            int prev = stepIterHistory[n - 2];
            int prevPrev = stepIterHistory[n - 3];

            bool monotonicIncrease = prevPrev < prev && prev < latest;
            bool absoluteHigh = latest >= TREND_HIGH_ITER_THRESHOLD;
            if (!(monotonicIncrease && absoluteHigh))
            {
                return new TrendCheckResult(caseFailedThisAttempt, retryGateDisabled, prevAttemptAvgIter, null);
            }

            if (bisectionAttempt == 0)
            {
                // 初回 attempt: 退化トレンド検出 → retry
                return new TrendCheckResult(
                    CaseFailedThisAttempt: true,
                    RetryGateDisabled: retryGateDisabled,
                    PrevAttemptAvgIter: prevAttemptAvgIter,
                    LogMessage: $"  🚨 退化トレンド検出: 反復数 [{prevPrev}→{prev}→{latest}] 単調増加 かつ 最新 ≥ {TREND_HIGH_ITER_THRESHOLD} → ステップ分割を増やして再試行");
            }

            // retry attempt: 改善ゲート — 平均反復数が前 attempt 比で十分改善していれば再 retry
            double currentAvg = stepIterHistory.Average();
            double improvement = prevAttemptAvgIter > 0 && double.IsFinite(prevAttemptAvgIter)
                ? (prevAttemptAvgIter - currentAvg) / prevAttemptAvgIter
                : 1.0;

            if (improvement >= RETRY_IMPROVEMENT_MIN_RATIO)
            {
                return new TrendCheckResult(
                    CaseFailedThisAttempt: true,
                    RetryGateDisabled: retryGateDisabled,
                    PrevAttemptAvgIter: currentAvg,
                    LogMessage: $"  🚨 退化トレンド検出 (retry {bisectionAttempt}/{maxStepBisections}): [{prevPrev}→{prev}→{latest}] 平均 {currentAvg:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) → さらに分割して再試行");
            }

            // 退化トレンド継続中だが改善が 10% 未満 → 細分化が無効と判断
            return new TrendCheckResult(
                CaseFailedThisAttempt: caseFailedThisAttempt,
                RetryGateDisabled: true,
                PrevAttemptAvgIter: currentAvg,
                LogMessage: $"  ✋ 改善ゲート: 退化トレンド継続中だが平均反復数 {currentAvg:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) が最小改善率 {RETRY_IMPROVEMENT_MIN_RATIO * 100:F0}% 未満 → 以降の retry を抑制、現 nStep={nStep} で完遂");
        }

        /// <summary>
        /// 失敗アテンプトで蓄積された Result コレクション (AnalysisStepResults / NodeResults /
        /// BeamResults / HorizontalSpringResults / RotationalSpringResults) を、
        /// 開始時のスナップショット長まで巻き戻す。
        /// retry 前に呼び出すことで、不完全な結果が以降の処理に混入しないようにする。
        /// </summary>
        private static void RollbackAttemptResults(
            AnaModel caseModel,
            int snapAnaStepResults,
            int[] snapNodeResults,
            int[] snapBeamResults,
            int[] snapHSpringResults,
            int[]? snapRotSpringResults)
        {
            while (caseModel.AnalysisStepResults.Count > snapAnaStepResults)
                caseModel.AnalysisStepResults.RemoveAt(caseModel.AnalysisStepResults.Count - 1);
            for (int i = 0; i < caseModel.Nodes.Count; i++)
                while (caseModel.Nodes[i].NodeResults.Count > snapNodeResults[i])
                    caseModel.Nodes[i].NodeResults.RemoveAt(caseModel.Nodes[i].NodeResults.Count - 1);
            for (int i = 0; i < caseModel.Beams.Count; i++)
                while (caseModel.Beams[i].BeamResults.Count > snapBeamResults[i])
                    caseModel.Beams[i].BeamResults.RemoveAt(caseModel.Beams[i].BeamResults.Count - 1);
            for (int i = 0; i < caseModel.HorizontalSoilSprings.Count; i++)
                while (caseModel.HorizontalSoilSprings[i].HorizontalSpringResults.Count > snapHSpringResults[i])
                    caseModel.HorizontalSoilSprings[i].HorizontalSpringResults.RemoveAt(caseModel.HorizontalSoilSprings[i].HorizontalSpringResults.Count - 1);
            if (caseModel.RotationalSprings != null && snapRotSpringResults != null)
            {
                for (int i = 0; i < caseModel.RotationalSprings.Count; i++)
                    while (caseModel.RotationalSprings[i].RotationalSpringResults.Count > snapRotSpringResults[i])
                        caseModel.RotationalSprings[i].RotationalSpringResults.RemoveAt(caseModel.RotationalSprings[i].RotationalSpringResults.Count - 1);
            }
        }

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
        internal static bool IsLiqSuperset(LiquefactionOptionType cur, string prevString)
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


        #endregion

    }
}
