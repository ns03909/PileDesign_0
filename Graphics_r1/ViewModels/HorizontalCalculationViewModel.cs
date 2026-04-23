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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

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
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                }
            }
            catch (Exception ex)
            {
                // 想定外の例外も捕捉して管理実装にフォールバック
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
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
            Application.Current?.Dispatcher.Invoke(() =>
            {
                while (_logQueue.TryDequeue(out var line))
                    CalculationLog.Add(line);
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
            }
        }

        // Newton-Raphson緩和係数（0.0-1.0: 1.0=フル更新、小さいほど安定だが収束遅い）
        // Full NR: 1.0推奨、Modified NR: 0.5推奨
        private double _relaxationFactor = 1.0;  // Full NRデフォルト: ω=1.0（ラインサーチで調整）
        public double RelaxationFactor
        {
            get => _relaxationFactor;
            set => SetProperty(ref _relaxationFactor, Math.Clamp(value, 0.1, 1.0));
        }

        // Newton-Raphsonモード選択
        // - Full NR (OFF): 毎反復で接線剛性+Kマトリクス更新（収束が速いが計算コスト高）
        // - Modified NR (ON): 適応的 - 最初の数回はFull NR、その後はKマトリクス再利用（高速化）
        // デフォルトはModified NR（ON）
        private bool _useModifiedNewtonRaphson = false;  // Full NRがデフォルト（収束速度が大幅に向上）
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
            set => SetProperty(ref _fullNRIterations, Math.Clamp(value, 1, 99));
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
        private int _maxCaseDegreeOfParallelism = 1;
        public int MaxCaseDegreeOfParallelism
        {
            get => _maxCaseDegreeOfParallelism;
            // Phase 3.1 未完のため 1 に丸めて保存する（UI 表示用に値は維持）
            set => SetProperty(ref _maxCaseDegreeOfParallelism, Math.Max(1, Math.Min(1, value)));
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

        /// <summary>ログ全体をテキストとして返す（TextBoxバインド用）</summary>
        public string CalculationLogText => string.Join(Environment.NewLine, CalculationLog);

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

        // 解析ケース数（基本値 + 再試行追加）
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

        private CancellationTokenSource _cancellationTokenSource;
        private readonly ManualResetEventSlim _pauseEvent = new(true); // trueで初期状態は「進行」

        public IRelayCommand PauseAnalysisCommand { get; }
        public IRelayCommand ResumeAnalysisCommand { get; }
        public IRelayCommand CancelAnalysisCommand { get; }

        private int _currentProgress;
        public int CurrentProgress
        {
            get => _currentProgress;
            set
            {
                SetProperty(ref _currentProgress, value);
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string ProgressText => $"{CurrentProgress}/{TotalCalculationCount}";

        // コンストラクタ（軽量: UIが先に表示されるようにモデル作成は遅延）
        public HorizontalCalculationViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // 重い処理(OnAnalysisModeling)はInitializeModelAsync()に移動
            // → ウィンドウのLoadedイベントから呼び出す

            PauseAnalysisCommand = new ToolkitRelayCommand(OnPauseAnalysis, () => IsAnalysisRunning);
            ResumeAnalysisCommand = new ToolkitRelayCommand(OnResumeAnalysis, () => IsAnalysisRunning);
            CancelAnalysisCommand = new ToolkitRelayCommand(OnCancelAnalysis, () => IsAnalysisRunning);

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
                    MessageBox.Show(errorMessage ?? "モデル作成に失敗しました。", "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (!hasBeams)
            {
                ConnectionMode = FoundationBeamConnectionMode.RigidBody;
                return true;
            }

            // 基礎梁はあるが節点参照が不正な場合はエラー
            bool hasFoundationNodes = fbInput.Nodes != null && fbInput.Nodes.Count > 0;
            bool hasPileReferences = fbInput.Beams.Any(b =>
                b.NodeI_Type == NodeReferenceType.PileLayout || b.NodeJ_Type == NodeReferenceType.PileLayout);
            if (!hasFoundationNodes && !hasPileReferences)
            {
                MessageBox.Show("剛床連結モードでは基礎梁節点が必要です。\n基礎梁入力で節点を定義するか、杭配置を参照してください。",
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

            if (AnaModels.Count > 1)
                AnaModels[1] = editModel;
            else if (AnaModels.Count == 1)
                AnaModels.Add(editModel);
            else
                AnaModels.Add(editModel);
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
            if (e.PropertyName == nameof(LoadCase.IsAnalysisTarget))
                OnPropertyChanged(nameof(TotalCalculationCount));
            if (e.PropertyName == nameof(LoadCombination.IsApplicable))
                OnPropertyChanged(nameof(TotalCalculationCount));
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
        }

        // 全レベル1荷重を解析対象から除外
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel1()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel1 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.IsAnalysisTarget = false;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        // 全レベル2荷重を解析対象に設定
        [RelayCommand]
        private void ApplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsAnalysisTarget = true;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        // 全レベル2荷重を解析対象から除外
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsAnalysisTarget = false;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        [RelayCommand]
        private void OnOk()
        {
            // 解析が未実行の場合は警告を表示して終了
            if (!IsAnalysisExecuted)
            {
                MessageBox.Show("解析が未了です。解析を実行してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                var result = MessageBox.Show(
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
                    MessageBox.Show("基礎梁要素が定義されていないため、剛体連結モードに切り替えて解析を実行します。",
                        "接続モード変更", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // 基礎梁はあるが節点参照が不正な場合はエラー
                    bool hasFoundationNodes = fbInput.Nodes != null && fbInput.Nodes.Count > 0;
                    bool hasPileReferences = fbInput.Beams.Any(b =>
                        b.NodeI_Type == NodeReferenceType.PileLayout || b.NodeJ_Type == NodeReferenceType.PileLayout);
                    if (!hasFoundationNodes && !hasPileReferences)
                    {
                        MessageBox.Show("剛床連結モードでは基礎梁節点が必要です。\n基礎梁入力で節点を定義するか、杭配置を参照してください。",
                            "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }

            try
            {
                AnalysisModelling = new AnalysisModelling(InputModel);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "モデル作成エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // 水平解析の実行
        [RelayCommand]
        private async Task OnExecuteAnalysis()
        {
            // 既存の解析結果がある場合は警告
            if (_mainWindowViewModel.IsHorizontalAnalysisDone)
            {
                var result = MessageBox.Show(
                    "既に水平解析の結果が存在します。\n再実行すると既存の結果は上書きされます。\n\n解析を実行しますか？",
                    "解析結果の上書き確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
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
                MessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

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

                    var result = MessageBox.Show(message, "杭頭半剛接合の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

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

                    var result = MessageBox.Show(message, "解析ステップ数の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
                        MessageBox.Show($"解析ステップ数を更新しました。\n" +
                                         $"レベル1: {Level1CalculationStepsCount}\n" +
                                         $"レベル2: {Level2CalculationStepsCount}",
                                         "設定更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }

            // モデル作成（進捗ウィンドウ表示前に実施。失敗時はここで中止）
            if (!TryCreateAnalysisModel())
                return;

            IsAnalysisRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // ボタン押下直後にログを表示
            await AddLogAsync("計算モデル作成開始");

            var progress = new Progress<Models.AnalysisProgress>();

            try
            {
                // 解析実行を非同期で行う
                await Task.Run(async () => {
                    await RunAsync(_cancellationTokenSource.Token, progress);
                });

                IsAnalysisExecuted = true; // 解析実行済みフラグをセット

                // 計算完了通知（UIスレッドで直接表示）
                MessageBox.Show("計算が終了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    MessageBox.Show($"解析中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                IsAnalysisExecuted = false;
                RequestClearProgressAnimation?.Invoke();
            }
            finally
            {
                IsAnalysisRunning = false;

                // CancellationTokenSourceをDisposeしてリソース解放
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
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

                // SoilPile.PileBodySegments を使用（要素分割後のセグメント）
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
                // 注: AxialForce は N 単位で格納されているが、
                //     GetMPhiRelationship は kN 単位を期待するため変換
                double axialN_kN = 0.0;
                if (pileByPileBodyNo.TryGetValue(pb, out var pile))
                {
                    axialN_kN = pile.AxialForce / 1000.0; // N → kN
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
                double axialN = 0.0;
                if (spring.Name != null && spring.Name.Contains('-'))
                {
                    var parts = spring.Name.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                    {
                        var pile = InputModel.PileLayoutItems?.FirstOrDefault(p => p.No == pileNo);
                        if (pile != null)
                        {
                            axialN = pile.AxialForce; // kN単位（PileBodyInput内でN単位に変換される）
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
                        // System.Diagnostics.Debug.WriteLine(
                        //     $"[SetupMTheta] {spring.Name}: → Rigid (KBig={KBig:E2})");
                        break;

                    case PileHeadRotationMode.CombinedXY:
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = def.CurveXY;
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

        // v21 Phase 3 prep: ばね剛性 min/max はインスタンスフィールドを廃し、
        // FindK / PrepareKmat の戻り値（out パラメータ）で局所管理する。

        public async Task RunAsync(CancellationToken token, IProgress<Models.AnalysisProgress>? progress = null)
        {
            // 既に「計算モデル作成開始」が出ているので、ここでは「計算開始」を追記する
            await AddLogAsync("解析計算処理開始");
            await Task.Yield();

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
            int calcNo = 0;

            // v19: 解析開始時に再試行による追加ステップ数をリセット
            _bisectionExtraSteps = 0;
            OnPropertyChanged(nameof(TotalCalculationCount));
            OnPropertyChanged(nameof(ProgressText));

            // 初期進捗を報告
            progress?.Report(new Models.AnalysisProgress
            {
                Percentage = 0,
                CurrentStep = "解析計算を開始しています...",
                CurrentStepNumber = 0,
                TotalSteps = TotalCalculationCount,
                StartTime = startTime
            });

            const double alpha = 1e-5;

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
                                _bisectionExtraSteps += (baseNStep - configuredNStep);
                                OnPropertyChanged(nameof(TotalCalculationCount));
                                OnPropertyChanged(nameof(ProgressText));
                                string directionLabel = loadDirection == LoadCombinationDirection.CounterLoading ? "逆方向組合せ" : "順方向組合せ";
                                await AddLogAsync($"  🔎 荷重方向事前検出: {directionLabel} (αL={loadCombination.Alpha1:N2}, βU={loadCombination.Beta1:N2}, βL={loadCombination.Beta2:N2}) → 初期 nStep={baseNStep} (設定値 {configuredNStep} の代わり, 総ステップ数: {TotalCalculationCount})");
                            }
                        }
                        int nStep = baseNStep;
                        int bisectionAttempt = 0;
                        bool caseConverged = false;

                        // v28 (2026-04-23) 改善ゲート: 前 attempt の平均反復数を保持し、
                        // retry 後にほとんど改善しない場合 (細分化が無効な構造的ラインサーチ制約)
                        // は以降の retry を抑制して無駄な計算を避ける。
                        double prevAttemptAvgIter = double.PositiveInfinity;
                        bool retryGateDisabled = false;

                        while (true)
                        {
                            // 再試行時の巻き戻し用に、結果リストのサイズをスナップショット
                            int snapAnaStepResults = targetModel.AnalysisStepResults?.Count ?? 0;
                            var snapNodeResults = new int[targetModel.Nodes.Count];
                            for (int i_ = 0; i_ < targetModel.Nodes.Count; i_++)
                                snapNodeResults[i_] = targetModel.Nodes[i_].NodeResults.Count;
                            var snapBeamResults = new int[targetModel.Beams.Count];
                            for (int i_ = 0; i_ < targetModel.Beams.Count; i_++)
                                snapBeamResults[i_] = targetModel.Beams[i_].BeamResults.Count;
                            var snapHSpringResults = new int[targetModel.HorizontalSoilSprings.Count];
                            for (int i_ = 0; i_ < targetModel.HorizontalSoilSprings.Count; i_++)
                                snapHSpringResults[i_] = targetModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count;
                            int[]? snapRotSpringResults = null;
                            if (targetModel.RotationalSprings != null)
                            {
                                snapRotSpringResults = new int[targetModel.RotationalSprings.Count];
                                for (int i_ = 0; i_ < targetModel.RotationalSprings.Count; i_++)
                                    snapRotSpringResults[i_] = targetModel.RotationalSprings[i_].RotationalSpringResults.Count;
                            }

                            targetModel.InitializeStates();

                            // 荷重ケース固有の剛体スレーブ割当を適用（回転ばねの有効/無効を切替）
                            ApplyPileHeadRigidBindingForLoadCase(targetModel, loadCase);

                            // 杭非線形ONのときだけ M–φ/M–θ をセット
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiFromPileSectionForLoadCase(targetModel, loadCase);
                            }
                            // M–θ は常にセット（非線形OFFは剛 KThetaXY=KRigid）
                            SetupNonlinearMThetaForLoadCase(targetModel, loadCase);

                            SetVectorDF(targetModel, loadCase, loadCombination, level, iLC, nStep);
                            targetModel.MapOnVectorDF();
                            InitializeSoilDisplacementIncrement(targetModel, loadCase, loadCombination, level, isLiquefaction, nStep);

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

                            // v26 (案 B): 早期適応検出用の反復数累積。最初の EARLY_ADAPTIVE_OBS_STEPS
                            // ステップの平均反復数が閾値を超えたら即 retry。30 反復停滞の検出より早く発火する。
                            // v28 (2026-04-23): retry attempt でも計測し、改善ゲートで無効 retry を検出する。
                            int iterSumFirstSteps = 0;

                            for (int step = 0; step < nStep; step++)
                        {
                            await Task.Yield(); // ここでUIスレッドを解放
                            token.ThrowIfCancellationRequested();
                            _pauseEvent.Wait(token); // ここで一時停止を考慮

                            // v15: ステップ開始時の変位を記録（予測器用）
                            var vectorDAtStepStart = targetModel.VectorD?.Clone();

                            calcNo += 1;
                            CurrentProgress = calcNo; // 進捗を更新

                            // 進捗を報告
                            progress?.Report(new Models.AnalysisProgress
                            {
                                Percentage = TotalCalculationCount > 0 ? (calcNo * 100.0 / TotalCalculationCount) : 0,
                                CurrentStep = $"レベル{level}-{iLC + 1}, {(isLiquefaction ? "液状化考慮" : "液状化非考慮")}, " +
                                             $"組合せ[{iLCOM + 1}], ステップ{step + 1}/{nStep}",
                                CurrentStepNumber = calcNo,
                                TotalSteps = TotalCalculationCount,
                                StartTime = startTime
                            });

                            string retryTag = bisectionAttempt > 0 ? $" 再試行{bisectionAttempt}/{MAX_STEP_BISECTIONS}" : "";
                            await AddLogAsync($"[{calcNo}/{TotalCalculationCount}{retryTag}]" + "荷重ケース：" + level + "-" + $"{iLC + 1}" + ", " + "液状化" + (isLiquefaction ? "考慮, " : "非考慮, ") +
                                $"[{iLCOM + 1}]" +
                                "αL:" + $"{loadCombination.Alpha1:N2}" +
                                ", βU:" + $"{loadCombination.Beta1:N2}" +
                                ", βL:" + $"{loadCombination.Beta2:N2}" +
                                ",　荷重ステップ" + (step + 1) + "/" + nStep +
                                (RelaxationFactor < 1.0 ? $", 緩和係数={RelaxationFactor:N2}" : ""));

                            // v15/v23: 予測ステップ（前ステップの変位増分があれば適用）
                            if (step > 0 && prevStepDispIncrement != null && targetModel.VectorD != null)
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
                                targetModel.ApplyDispIncrement(predictorIncrement);

                                // 節点変位も更新（既存のラインサーチ用メソッドを流用）
                                UpdateNodeDisplacementsForLineSearch(targetModel, predictorIncrement);
                            }

                            targetModel.InitializeNormsqR_onNormsqFint();

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

                            UpdateSoilDisp();
                            UpdateF(targetModel);

                            // 入力値＋応力解析結果モード: 前ステップのFxiを入力軸力に加算
                            if (UseAnalysisAxialForce && step > 0)
                            {
                                UpdateAxialForceFromAnalysis(targetModel);
                            }

                            // 現ステップ軸力での M–φ 再解決は、杭非線形ONのときのみ
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiByCurrentAxialForMiddleBeam(targetModel);
                            }

                            targetModel.SetR();

                            // 反復なし簡易法の場合は1回で終了
                            int maxIterations = SkipIteration ? 1 : 100;
                            // 適応的緩和係数の初期化
                            double currentRelaxFactor = SkipIteration ? 1.0 : RelaxationFactor; // 簡易法は緩和なし
                            double prevResidual = targetModel.NormsROnNormsFint;
                            int consecutiveDecrease = 0; // 連続減少カウント

                            // v11: 停滞検出用の変数
                            int stagnationCount = 0;           // 停滞カウント（残差がほぼ変化しない回数）
                            const int STAGNATION_LIMIT = 15;   // 停滞判定の閾値回数
                            const double STAGNATION_RATIO = 0.98; // 残差比がこれ以上なら停滞とみなす
                            const double RELAXED_ALPHA = 1e-4; // 停滞時の緩和収束基準
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

                            // v18: 長期未改善検出（counter-loading で残差が振動するケース対策）
                            // minResidualSeen が一定回数更新されない場合、収束基準を minSeen * 1.2 に緩和
                            int iterationsSinceMinUpdated = 0;
                            const int NO_IMPROVEMENT_LIMIT = 30;

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

                            while (targetModel.NormsROnNormsFint >= effectiveAlpha && n_iteration <= maxIterations)
                            {
                                // v21 Phase 3 prep: 効果的ライン探索フラグを「ユーザー設定 ∪ 自動切替」の union として毎反復評価
                                // （旧コード: auto-switch 時に _useLineSearch フィールドを true に書換 → UseLineSearch プロパティが true になる
                                //   を field 書換なしで再現。インスタンスフィールドを汚さないため並列化への準備になる）
                                bool effectiveUseLineSearch = UseLineSearch || autoSwitchedToLineSearch;
                                double usedRelaxFactor = currentRelaxFactor; // このステップで使う値を保存

                                // v28: Mcr 同期 Mode 切替で新規クラック検出したばね名を収集 (Task.Run 外でログ出力するため)
                                var newlyCrackedSprings = new List<(string Name, double M, double Mcr)>();

                                // v28 問題 A 診断: 反復内で状態変化した要素を収集 (Beam は List idx で一意識別)
                                var mphiChanges = new List<(int BeamIdx, int Prev, int Curr)>();
                                var newlyYieldedSoilSprings = new List<string>();  // 新規降伏した p-y ばね
                                var newlyUnyieldedSoilSprings = new List<string>(); // 降伏解除された p-y ばね
                                int[] currentBeamSegments = null;  // index = beam idx
                                var currentYieldedSoilSprings = new HashSet<string>();

                                // 重い計算をバックグラウンドで実行（診断値もここで算出）
                                await Task.Run(() =>
                                {
                                    // トークンを投げて途中キャンセルを可能にする
                                    token.ThrowIfCancellationRequested();

                                    // N は荷重ケース一定だが、簡便に毎回解決しても可（コストは小）
                                    //SetupNonlinearMThetaForLoadCase(targetModel, loadCase);

                                    // Newton-Raphsonモード:
                                    // - Full NR: 常に毎反復で接線剛性+Kマトリクス更新
                                    // - Modified NR: 最初の FullNRIterations 回は Full NR、以降は K 再利用
                                    bool useFullNR = !UseModifiedNewtonRaphson || n_iteration <= FullNRIterations;

                                    if (loadCase.IsPileNonLinear && useFullNR)
                                    {
                                        // Full NR: ダンピングなし（正確なヤコビアンで2次収束）
                                        // Modified NR の初期反復: ダンピングあり（安定化）
                                        bool relaxTangent = UseModifiedNewtonRaphson;
                                        UpdateBeamMPhiTangent(targetModel, useRelaxation: relaxTangent);
                                    }

                                    // KTan 組立（戻り値で springK の min/max を受け取る）
                                    // v17: Modified NRモードの適応フェーズではKマトリクス組立をスキップ（高速化）
                                    if (useFullNR || !loadCase.IsPileNonLinear || n_iteration == 1)
                                    {
                                        long _tsFindK = System.Diagnostics.Stopwatch.GetTimestamp();
                                        (springKMin, springKMax) = FindK(iLC, targetModel);
                                        profFindKTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindK;
                                        profFindKCalls++;

                                        // 初回反復時のみ剛性マトリクスの安定性チェック
                                        if (n_iteration == 1)
                                        {
                                            targetModel.ValidateStability(useEigenvalueCheck: false);
                                        }
                                    }
                                    else
                                    {
                                    }

                                    // ラインサーチ or 通常の更新
                                    if (effectiveUseLineSearch)
                                    {
                                        // ラインサーチ: Newton方向を計算し、最適なステップ長を探索
                                        long _tsSolve = System.Diagnostics.Stopwatch.GetTimestamp();
                                        var newtonDir = SolveNewtonDirection(targetModel);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        double currentRes = targetModel.NormsROnNormsFint;

                                        // バックトラッキングラインサーチで最適αを見つける
                                        long _tsLS = System.Diagnostics.Stopwatch.GetTimestamp();
                                        double optimalAlpha = BacktrackingLineSearch(
                                            targetModel, newtonDir, currentRes, iLC, loadCase.IsPileNonLinear, out int _lsTrials);
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
                                        SolveDdAndUpdateX(targetModel, usedRelaxFactor);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        // 割線剛性更新（FindTの前に実行して、最新のK_secで内力を計算）
                                        if (loadCase.IsPileNonLinear)
                                            UpdateBeamMPhiSecant(targetModel);

                                        // 断面力・T更新と残差更新
                                        long _tsFindT = System.Diagnostics.Stopwatch.GetTimestamp();
                                        FindT(iLC, targetModel);
                                        profFindTTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindT;

                                        targetModel.FindR();
                                    }

                                    /* NaN診断: 反復ごとのチェック
                                    FEM.NaNDiagnostics.SetIteration(n_iteration);
                                    if (!double.IsFinite(targetModel.NormsROnNormsFint))
                                    {
                                        FEM.NaNDiagnostics.LogNaN($"NormsROnNormsFint is NaN at iteration {n_iteration}!");
                                        FEM.NaNDiagnostics.CheckNodeDisplacements(targetModel.Nodes);
                                        FEM.NaNDiagnostics.CheckBeamForces(targetModel.Beams);
                                    } */

                                    // v21 Phase 3 prep: ばね剛性 min/max は FindK の戻り値から直接取得するため
                                    // ここでの再代入は不要（FindK を呼ばない分岐では NaN のまま）

                                    // v17: 診断値K対角は重い処理なので、最初の反復と5反復ごとのみ計算
                                    // Modified NRモードの適応フェーズではKが更新されないため計算頻度を下げる
                                    if (n_iteration == 1 || n_iteration % 5 == 0)
                                    {
                                        (diagKMin, diagKMax) = GetKDiagonalMiNMax(targetModel, isTan: true);
                                    }

                                    // 診断値: 代表自由度の |d| 最大値（節点の増分変位から）
                                    dispMaxAbs = GetMaxAbsIncrementalDisp(targetModel);

                                    // v27: 振動診断用 — 上位 3 DOF を取得（Ux/Uy/Uz/Rx/Ry/Rz 全成分対象）
                                    dominantDofs = GetTopIncrementalDofs(targetModel, 3);

                                    // v27: 案 A — CumulativeDisp スナップショットをキューに追加（Aitken 平均化用）
                                    var snap = new Dictionary<string, NodeDisp>(targetModel.Nodes.Count);
                                    foreach (var nd in targetModel.Nodes)
                                        snap[nd.Name] = nd.CumulativeDisp.Clone();
                                    recentCumulativeDisp.Enqueue(snap);
                                    while (recentCumulativeDisp.Count > AITKEN_HISTORY)
                                        recentCumulativeDisp.Dequeue();

                                    // v28: Mcr 同期 Mode 切替 (ヒステリシス付き)
                                    // 場所打ち RC 杭の杭頭回転ばねで |M| が Mcr を初めて超えた瞬間を検出し、
                                    // HasCrackedXY = true にラッチ。以降は post-crack curve を使用 (除荷しても戻らない)。
                                    // 閾値 0.999×Mcr で若干緩めてヒステリシスラッチを安定化。
                                    if (targetModel.RotationalSprings != null)
                                    {
                                        foreach (var rs in targetModel.RotationalSprings)
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
                                    if (targetModel.Beams != null)
                                    {
                                        int beamCount = targetModel.Beams.Count;
                                        currentBeamSegments = new int[beamCount];
                                        for (int idx = 0; idx < beamCount; idx++)
                                        {
                                            var beam = targetModel.Beams[idx];
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
                                            for (int i = 0; i < pli.PileNodes.Count && i < pli.SoilNodes.Count; i++)
                                            {
                                                var pn = pli.PileNodes[i];
                                                var sn = pli.SoilNodes[i];
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

                                }, token);

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

                                    // View に警告表示を依頼（UI スレッドでイベントを発火）
                                    string warnMsg = $"解析を中止しました。\n代表変位が閾値 {MaxAllowedDisplacement} m を超えました（{dispMaxAbs:E3}）。";
                                    Application.Current?.Dispatcher.Invoke(() => RequestShowWarning?.Invoke(warnMsg));

                                    // キャンセルを発行して呼び出し側で OperationCanceledException を処理させる
                                    _cancellationTokenSource?.Cancel();
                                    RequestClearProgressAnimation?.Invoke();
                                    throw new OperationCanceledException(token);
                                }


                                // 残差ログ（ラインサーチ判定はステップ局所 flag を使用。旧 _useLineSearch 書き換え時と同じ挙動）
                                string relaxInfo;
                                if (effectiveUseLineSearch && usedRelaxFactor < 0.99)
                                    relaxInfo = $" (α={usedRelaxFactor:N2})";  // ラインサーチのステップ長
                                else if (currentRelaxFactor < 0.99)
                                    relaxInfo = $" (ω={currentRelaxFactor:N2})"; // 緩和係数
                                else
                                    relaxInfo = "";
                                if (targetModel.NormsROnNormsFint < alpha)
                                {
                                    await AddLogAsync("　　" + "||R||**2 / ||Fint||**2 = " + $"{targetModel.NormsROnNormsFint:E2}" + "≦" + $"{alpha:E2} Converged" + relaxInfo);
                                }
                                else
                                {
                                    await AddLogAsync("　　" + "||R||**2 / ||Fint||**2 = " + $"{targetModel.NormsROnNormsFint:E2}" + "＞" + $"{alpha:E2}" + relaxInfo);
                                }

                                // 診断ログ
                                await AddLogAsync($"　　diag(K)[min,max]=[{diagKMin:E3}, {diagKMax:E3}], spring k[min,max]=[{springKMin:E3}, {springKMax:E3}], max|d|={dispMaxAbs:E3}");

                                // v27: 振動診断 — 支配 DOF (上位 3) とフリップフロップ検出
                                // リミットサイクル (残差が周期的に跳ね返って収束しない) の原因 DOF を特定する。
                                // 前反復と同じ key が最大 |δu| を示し、かつ符号が逆転 → flip としてカウント。
                                // |δu| < FLIP_THRESHOLD はノイズとみなして無視。
                                if (dominantDofs != null && dominantDofs.Count > 0)
                                {
                                    const double FLIP_THRESHOLD = 1e-10;
                                    var top = dominantDofs[0];
                                    int curSign = Math.Sign(top.Value);
                                    string flipInfo = "";
                                    if (Math.Abs(top.Value) > FLIP_THRESHOLD
                                        && prevDominantDofKey == top.Key
                                        && prevDominantSign != 0 && curSign != 0
                                        && curSign != prevDominantSign)
                                    {
                                        flipFlopCount++;
                                        flipInfo = $" ⚠ flip#{flipFlopCount}";
                                    }
                                    else if (prevDominantDofKey != top.Key)
                                    {
                                        // 支配 DOF が変わった → リセット
                                        flipFlopCount = 0;
                                    }
                                    prevDominantDofKey = top.Key;
                                    prevDominantSign = curSign;

                                    static string SignedExp(double v) => (v >= 0 ? "+" : "") + v.ToString("E2");
                                    string topStr = string.Join(", ",
                                        dominantDofs.Select(x => $"{x.Key}={SignedExp(x.Value)}"));
                                    await AddLogAsync($"　　　dominant δu: {topStr}{flipInfo}");
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
                                    await Task.Run(() =>
                                    {
                                        int historyCount = recentCumulativeDisp.Count;
                                        foreach (var nd in targetModel.Nodes)
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
                                        FindT(iLC, targetModel);
                                        targetModel.FindR();
                                    }, token);

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

                                    await AddLogAsync($"　　🔄 Aitken 平均化 #{aitkenFiredCount}/{AITKEN_MAX_FIRE} 発動: 直近 {AITKEN_HISTORY} 反復の CumulativeDisp 平均で書換 → 残差={targetModel.NormsROnNormsFint:E2}");
                                }

                                // 適応的緩和係数の更新（UseAdaptiveRelaxation=trueの場合のみ）
                                double currentResidual = targetModel.NormsROnNormsFint;
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
                            }

                            // Maximum iteration check
                            string dispInfo = !double.IsNaN(dispMaxAbs) ? $", max|d|={dispMaxAbs:E3}m" : "";
                            bool converged = !(n_iteration > maxIterations && targetModel.NormsROnNormsFint >= effectiveAlpha);
                            if (!converged)
                            {
                                double finalResidual = targetModel.NormsROnNormsFint;
                                await AddLogAsync($"  → 未収束: 最大反復回数 {maxIterations} に到達。残差ノルム={finalResidual:E3} (許容値={effectiveAlpha:E3}){dispInfo}");
                                caseFailedThisAttempt = true;
                            }
                            else
                            {
                                string relaxedNote = effectiveAlpha > alpha ? $" (緩和基準α={effectiveAlpha:E2})" : "";
                                await AddLogAsync($"  → Converged in {n_iteration} iterations. Residual norm={targetModel.NormsROnNormsFint:E3}{relaxedNote}{dispInfo}");

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
                                await AddLogAsync($"    ⏱ プロファイル: K組立={_findKMs:F0}ms×{profFindKCalls}, Solve={_solveMs:F0}ms {_solverTag} (CSC={_cscMs:F0} 分解={_factMs:F0} 代入={_backSubMs:F0}), " +
                                    (profLineSearchCalls > 0
                                        ? $"LS={_lsMs:F0}ms×{profLineSearchCalls} (avg trial={_lsTrialAvg:F1}), "
                                        : $"FindT={_findTMs:F0}ms, ") +
                                    $"total={_totalSec:F1}s");

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

                            // v21 Phase 3 prep: 自動ライン探索はステップ局所の effectiveUseLineSearch で
                            // 処理するため、インスタンスフィールド _useLineSearch の復元は不要

                            // v15/v23: このステップの変位増分を記録（次ステップの予測器用）
                            // 2 次外挿のため前々ステップの増分も保持する
                            if (vectorDAtStepStart != null && targetModel.VectorD != null)
                            {
                                prevPrevStepDispIncrement = prevStepDispIncrement;
                                prevStepDispIncrement = targetModel.VectorD - vectorDAtStepStart;
                            }

                            // デバッグ: 杭頭変位・M-θばねの確認
                            if (step == 0 || step == nStep - 1)
                            {
                                var actionPt = targetModel.Nodes[0];
                                // System.Diagnostics.Debug.WriteLine(
                                //     $"[Step{step}] ActionPoint Ux={actionPt.CumulativeDisp?.Ux:E3} Rx={actionPt.CumulativeDisp?.Rx:E3} Ry={actionPt.CumulativeDisp?.Ry:E3}");
                                foreach (var pile in InputModel.PileLayoutItems.Take(2))
                                {
                                    var rxy = pile.PileTopRotationalSpring;
                                    var capNode = rxy?.NodeI;
                                    var pileHead = pile.PileNodes?.FirstOrDefault();
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

                            targetModel.AnalysisStepResults.Add(new(loadCase, loadCombination, isLiquefaction, step, n_iteration, targetModel.NormsROnNormsFint));
                            foreach (var node in targetModel.Nodes)
                                node.NodeResults.Add(new(loadCase, loadCombination, isLiquefaction, step, node));
                            foreach (var beam in targetModel.Beams)
                                beam.BeamResults.Add(new(loadCase, loadCombination, isLiquefaction, step, beam));
                            foreach (var spring in targetModel.HorizontalSoilSprings)
                                spring.HorizontalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, spring));
                            //foreach (var rotationalSpring in targetModel.RotationalSprings)
                            //    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                            if (targetModel.RotationalSprings != null)
                            {
                                foreach (var rotationalSpring in targetModel.RotationalSprings)
                                {
                                    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                                    // else: この荷重ケースでは回転ばねは存在するが「使用されなかった」ため結果を保存しない
                                }
                            }

                            // v19: このステップが完了したことを記録
                            stepsExecutedInAttempt = step + 1;

                            // v26 (案 B): 早期適応検出 — 最初 EARLY_ADAPTIVE_OBS_STEPS ステップの
                            // 平均反復数が閾値を超えたら即 retry。30 反復停滞検出より早く発火する。
                            // v28 (2026-04-23): 改善ゲート追加。retry 後 attempt で avg iter が前 attempt より
                            // 十分改善していない場合 (構造的ラインサーチ制約等で細分化が無効) は
                            // 再 retry を抑制し、現 nStep で完遂させる。
                            const int EARLY_ADAPTIVE_OBS_STEPS = 2;
                            const double EARLY_ADAPTIVE_ITER_THRESHOLD = 18.0;
                            const double RETRY_IMPROVEMENT_MIN_RATIO = 0.10;  // 10% 以上改善必要
                            if (step < EARLY_ADAPTIVE_OBS_STEPS)
                            {
                                iterSumFirstSteps += Math.Min(n_iteration, maxIterations);
                            }
                            if (step + 1 == EARLY_ADAPTIVE_OBS_STEPS
                                && !caseFailedThisAttempt && !physicallyUnconvergeable
                                && !retryGateDisabled
                                && bisectionAttempt < MAX_STEP_BISECTIONS)
                            {
                                double avgIter = iterSumFirstSteps / (double)EARLY_ADAPTIVE_OBS_STEPS;
                                bool threshExceeded = avgIter >= EARLY_ADAPTIVE_ITER_THRESHOLD;

                                if (bisectionAttempt == 0)
                                {
                                    // 初回 attempt: 閾値ベース判定 (従来ロジック)
                                    if (threshExceeded)
                                    {
                                        await AddLogAsync($"  🚨 早期適応検出: 最初 {EARLY_ADAPTIVE_OBS_STEPS} ステップの平均反復数 {avgIter:N1} が閾値 {EARLY_ADAPTIVE_ITER_THRESHOLD:N0} を超過 → ステップ分割を増やして再試行");
                                        caseFailedThisAttempt = true;
                                    }
                                }
                                else
                                {
                                    // retry attempt: 改善ゲート — 閾値超過 AND 前 attempt 比 10% 以上改善 の両方で再 retry
                                    double improvement = prevAttemptAvgIter > 0 && double.IsFinite(prevAttemptAvgIter)
                                        ? (prevAttemptAvgIter - avgIter) / prevAttemptAvgIter
                                        : 1.0;

                                    if (threshExceeded && improvement >= RETRY_IMPROVEMENT_MIN_RATIO)
                                    {
                                        await AddLogAsync($"  🚨 早期適応検出 (retry {bisectionAttempt}/{MAX_STEP_BISECTIONS}): 平均反復数 {avgIter:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) → さらに分割して再試行");
                                        caseFailedThisAttempt = true;
                                    }
                                    else if (threshExceeded)
                                    {
                                        // 閾値は超えているが改善が 10% 未満 → 細分化が無効な構造 → retry 抑制
                                        await AddLogAsync($"  ✋ 改善ゲート: retry 後平均反復数 {avgIter:N1} (前 attempt {prevAttemptAvgIter:N1}, 改善 {improvement * 100:F1}%) が最小改善率 {RETRY_IMPROVEMENT_MIN_RATIO * 100:F0}% 未満 → 以降の retry を抑制、現 nStep={nStep} で完遂");
                                        retryGateDisabled = true;
                                    }
                                }

                                prevAttemptAvgIter = avgIter;
                            }

                            // v20 Phase 2: 物理的未収束なら直ちに中止（再試行しない）
                            if (physicallyUnconvergeable)
                            {
                                int remainingPhys = nStep - (step + 1);
                                if (remainingPhys > 0)
                                {
                                    await AddLogAsync($"  ⛔ 物理的未収束のため残り {remainingPhys} ステップをスキップ");
                                    // v25: 未実行ステップ分を総ステップ数から差し引き、進捗バーを 100% 到達させる
                                    _bisectionExtraSteps -= remainingPhys;
                                    OnPropertyChanged(nameof(TotalCalculationCount));
                                    OnPropertyChanged(nameof(ProgressText));
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
                                _bisectionExtraSteps -= remainingAtMax;
                                OnPropertyChanged(nameof(TotalCalculationCount));
                                OnPropertyChanged(nameof(ProgressText));
                            }
                            break;  // 諦めて次のケースへ
                        }

                        // 失敗アテンプトの結果を巻き戻し
                        while (targetModel.AnalysisStepResults.Count > snapAnaStepResults)
                            targetModel.AnalysisStepResults.RemoveAt(targetModel.AnalysisStepResults.Count - 1);
                        for (int i_ = 0; i_ < targetModel.Nodes.Count; i_++)
                            while (targetModel.Nodes[i_].NodeResults.Count > snapNodeResults[i_])
                                targetModel.Nodes[i_].NodeResults.RemoveAt(targetModel.Nodes[i_].NodeResults.Count - 1);
                        for (int i_ = 0; i_ < targetModel.Beams.Count; i_++)
                            while (targetModel.Beams[i_].BeamResults.Count > snapBeamResults[i_])
                                targetModel.Beams[i_].BeamResults.RemoveAt(targetModel.Beams[i_].BeamResults.Count - 1);
                        for (int i_ = 0; i_ < targetModel.HorizontalSoilSprings.Count; i_++)
                            while (targetModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count > snapHSpringResults[i_])
                                targetModel.HorizontalSoilSprings[i_].HorizontalSpringResults.RemoveAt(targetModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count - 1);
                        if (targetModel.RotationalSprings != null && snapRotSpringResults != null)
                        {
                            for (int i_ = 0; i_ < targetModel.RotationalSprings.Count; i_++)
                                while (targetModel.RotationalSprings[i_].RotationalSpringResults.Count > snapRotSpringResults[i_])
                                    targetModel.RotationalSprings[i_].RotationalSpringResults.RemoveAt(targetModel.RotationalSprings[i_].RotationalSpringResults.Count - 1);
                        }

                        // v19: 総ステップ数の調整
                        // baseline は旧 nStep (=oldNStep) を計上済み
                        // 実際にこのアテンプトで実行したのは stepsExecutedInAttempt ステップ
                        // 次のアテンプトで新 nStep (=oldNStep*2) ステップを実行する
                        // → 調整 = (実行済 + 新 nStep) - 旧 nStep = stepsExecutedInAttempt + newNStep - oldNStep
                        int oldNStep = nStep;
                        bisectionAttempt++;
                        nStep *= 2;
                        _bisectionExtraSteps += stepsExecutedInAttempt + nStep - oldNStep;
                        OnPropertyChanged(nameof(TotalCalculationCount));
                        OnPropertyChanged(nameof(ProgressText));
                    }  // end retry while-loop

                    _ = caseConverged; // 抑制: 未使用警告（将来診断に利用する可能性）

                        // NaN診断: 荷重ケース完了
                        // FEM.NaNDiagnostics.End();
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            await AddLogAsync("計算終了");

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
            // SoilPile（要素分割後）をPileBodyNoで検索するための辞書
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
                // 現ステップの解析軸力（N → kN）
                double axialN_kN = pile.AxialForce / 1000.0;

                int pb = pile.PileBodyNo;
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) continue;

                foreach (var beam in pile.Beams)
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

        // UIスレッドでログを追加
        private Task AddLogAsync(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            _logQueue.Enqueue($"[{timestamp}] {message}");
            StartLogTimerIfNeeded();
            return Task.CompletedTask;
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
                pileLayoutItem.AxialForce += pileLayoutItem.AxialForceIncrement; // 杭軸力の更新 [kN]
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
                pile.AxialForce -= fxiAnalysis; // 圧縮増 → Fxi負 → -(-) = 加算
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
                var x = CsparseLinearSolver.Solve(targetModel.KAA_tan, targetModel.VectorR, isSpd: false);
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

        private void InitializeSoilDisplacementIncrement(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, bool isLiquefaction, double nStep)
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
                for (int i = 0; i < soilPile.ZDataItems.Count; i++)
                {
                    var zData = soilPile.ZDataItems[i];
                    double groundDisp1 = isLiquefaction ? zData.GroundDisp1L : zData.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zData.GroundDisp2L : zData.GroundDisp2;
                    NodeDisp dd = CalcDisplacement(groundDisp1, groundDisp2, level, alpha1, nStep, loadAngle);

                    pileLayoutItem.SoilNodes[i].SetIncrementalForcedDisp(dd);
                    pileLayoutItem.SoilNodes[i].SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
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
                    soilNode.SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
                }
            }
        }

        // 増分荷重の取得 慣性力の節点荷重へのセット
        private void SetVectorDF(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, int iLC, double nStep) // PileDesign
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

            targetModel.Nodes[0].SetCumulativeLoad(new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0)); // 荷重ベクトル [kN]
            targetModel.MapOnVectorF();

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                if (level == 1)
                {
                    pileLayoutItem.AxialForceIncrement = (pileLayoutItem.AxialForceLevel1s[iLC]
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nStep; // レベル1の杭軸力増分 [kN]
                }
                else //(level == 2)
                {
                    pileLayoutItem.AxialForceIncrement = (pileLayoutItem.AxialForceLevel2s[iLC]
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nStep; // レベル2の杭軸力増分 [kN]
                }
            }
        }

        // 地盤変位の更新
        private void UpdateSoilDisp()
        {
            // DoatsuGoryokuBaneの節点の地盤変位を更新
            var doatsuGoryokuBane = InputModel.ElementDivision.DoatsuGoryokuBane;
            if (doatsuGoryokuBane != null)
            {
                for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
                {
                    if (i == 0)
                    {
                        doatsuGoryokuBane.Items[i].TopSoilNode.CumulativeForcedDisp
                            += doatsuGoryokuBane.Items[i].TopSoilNode.IncrementalForcedDisp;
                    }
                    doatsuGoryokuBane.Items[i].BtmSoilNode.CumulativeForcedDisp
                        += doatsuGoryokuBane.Items[i].BtmSoilNode.IncrementalForcedDisp;
                }
            }

            // PileLayoutItemsの節点の地盤変位を更新
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                if (pileLayoutItem?.SoilNodes == null) continue;
                foreach (var node in pileLayoutItem.SoilNodes)
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

                    var relDispTop = item.TopEmbedmentNode.CumulativeDisp - item.TopSoilNode.CumulativeDisp;
                    var kVecTop = isTan ? item.GetTangentStiffnessVector(relDispTop) : item.GetSecantStiffnessVector(relDispTop);
                    AddContribution(item.TopHorizontalSoilSpring, SafeK(kVecTop.Kx), SafeK(kVecTop.Ky));

                    var relDisplacementBtm = item.BtmEmbedmentNode.CumulativeDisp - item.BtmSoilNode.CumulativeDisp;
                    var kVecBtm = isTan ? item.GetTangentStiffnessVector(relDisplacementBtm) : item.GetSecantStiffnessVector(relDisplacementBtm);
                    AddContribution(item.BtmHorizontalSoilSpring, SafeK(kVecBtm.Kx), SafeK(kVecBtm.Ky));
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

                int reactionCount = horizontalReactions.Count;
                for (int i = 0; i < pileLayoutItem.PileNodes.Count; i++)
                {
                    var pileNode = pileLayoutItem.PileNodes[i];
                    var soilNode = pileLayoutItem.SoilNodes[i];
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
                    if (i < pileLayoutItem.PileNodes.Count - 1 && i < reactionCount)
                    {
                        bool isTop = true;
                        kTan += horizontalReactions[i].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile);
                        kSec += horizontalReactions[i].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile);
                    }

                    kTan = SafeK(kTan);
                    kSec = SafeK(kSec);

                    // 最下端節点で k=0 の場合、隣接要素の剛性を使用（剛性マトリクス特異防止）
                    if ((isTan ? kTan : kSec) <= 0.0 && i > 0 && i - 2 >= 0 && i - 2 < reactionCount)
                    {
                        double kFallbackTan = horizontalReactions[i - 2].GetSoilTangentReactionCoefficient(abs, false, isFrontPile);
                        double kFallbackSec = horizontalReactions[i - 2].GetSoilSecantReactionCoefficient(abs, false, isFrontPile);
                        if (kTan <= 0.0) kTan = SafeK(kFallbackTan);
                        if (kSec <= 0.0) kSec = SafeK(kFallbackSec);
                        System.Diagnostics.Debug.WriteLine(
                            $"[UpdateSoilSprings] WARNING: Pile-{pileLayoutItem.PileNo} node {i} k=0 → 隣接要素の剛性 tan={kTan:E3}/sec={kSec:E3} を代用");
                    }

                    var spring = pileLayoutItem.HorizontalSoilSprings[i];

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
                    var rxy = pile.PileTopRotationalSpring;
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

                            double kParTan, kParSec;  // n 方向の接線/割線剛性
                            if (thetaProj >= rxy.ThetaProjMax - 1e-15)
                            {
                                // 前進: post-crack curve (1e-8 の急勾配はバイパス)
                                double absProj = Math.Abs(thetaProj);
                                kParTan = SafeK(rxy.CurveXY.EvaluatePostCrackTangent(absProj));
                                kParSec = SafeK(rxy.CurveXY.EvaluateSecant(absProj));
                            }
                            else
                            {
                                // 除荷 (θ_proj < θ_proj_max): 線形戻り (剛)
                                // (0, 0) → (θ_proj_max, M_max) の直線 → K = M_max / θ_proj_max
                                double thetaMax = rxy.ThetaProjMax;
                                double mMax = rxy.CurveXY.EvaluateMoment(thetaMax);
                                double kUnload = (thetaMax > 1e-15) ? SafeK(Math.Abs(mMax) / thetaMax) : SafeK(rxy.CurveXY.EvaluateTangent(0));
                                kParTan = kUnload;
                                kParSec = kUnload;  // 線形なので接線 = 割線
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