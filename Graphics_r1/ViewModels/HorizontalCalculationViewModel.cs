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
        //{
        //    try { Control.UseNativeMKL(); }          // MathNet.Numerics.MKL パッケージ導入時に有効
        //    catch { Control.UseBestProviders(); }    // 利用可能な最適プロバイダを選択
        //    Control.MaxDegreeOfParallelism = Environment.ProcessorCount;
        //}
        {
            try
            {
                // 利用可能なら最適プロバイダを自動選択（安全な通常ルート）
                Control.UseBestProviders();
            }
            catch (NotSupportedException nse)
            {
                System.Diagnostics.Debug.WriteLine($"MathNet provider selection failed (NotSupported): {nse}");
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                    System.Diagnostics.Debug.WriteLine($"MathNet fallback to managed failed: {inner}");
                }
            }
            catch (Exception ex)
            {
                // 想定外の例外も捕捉して管理実装にフォールバック
                System.Diagnostics.Debug.WriteLine($"MathNet provider selection unexpected error: {ex}");
                try
                {
                    Control.UseManaged();
                }
                catch (Exception inner)
                {
                    System.Diagnostics.Debug.WriteLine($"MathNet fallback to managed failed: {inner}");
                }
            }

            // スレッドプール並列度はプロセッサ数に合わせる
            Control.MaxDegreeOfParallelism = Environment.ProcessorCount;
        }
        // ログのバッファリング用（UIポストを間引く）
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly System.Timers.Timer _logFlushTimer = new(200) { AutoReset = true };
        private volatile bool _logTimerStarted;

        private void StartLogTimerIfNeeded()
        {
            if (_logTimerStarted) return;
            _logFlushTimer.Elapsed += (_, __) => FlushLogsToUi();
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

        // 計算ステップレベル2荷重
        private int _level2CalculationStepsCount = 4;
        public int Level2CalculationStepsCount
        {
            get => _level2CalculationStepsCount;
            set
            {
                SetProperty(ref _level2CalculationStepsCount, value);
                OnPropertyChanged(nameof(TotalCalculationCount));
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

        // 剛体連結数
        [ObservableProperty]
        private int rigidBodiesCount;

        public AnalysisModelling AnalysisModelling { get; set; }

        // 液状化の考慮
        public enum LiquefactionOptionType
        {
            None,
            Yes,
            Both
        }

        private LiquefactionOptionType _liquefactionOption = LiquefactionOptionType.Both;
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

        // 解析実行済みフラグ
        private bool _isAnalysisExecuted = false;
        public bool IsAnalysisExecuted
        {
            get => _isAnalysisExecuted;
            set => SetProperty(ref _isAnalysisExecuted, value);
        }

        // 解析ケース数
        public int TotalCalculationCount
        {
            get
            {
                // 液状化オプション: あり=1, なし=1, 両方=2
                int liquefactionFactor = LiquefactionOption == LiquefactionOptionType.Both ? 2 : 1;

                // 適用されている荷重ケース1, 2の数
                int level1Count = InputModel.LoadCasesInput.LoadCasesLevel1?.Count(x => x.IsApplicable) ?? 0;
                int level2Count = InputModel.LoadCasesInput.LoadCasesLevel2?.Count(x => x.IsApplicable) ?? 0;

                // 適用されている荷重組み合わせの数
                int combinationCount = InputModel.LoadCasesInput.AllLoadCombinations?.Count(x => x.IsApplicable) ?? 0;

                // 1荷重あたりレベル1解析計算ステップ数
                int level1Steps = Level1CalculationStepsCount;

                // 1荷重あたりレベル2解析計算ステップ数
                int level2Steps = Level2CalculationStepsCount;

                // 計算式
                return liquefactionFactor * (level1Count * level1Steps + level2Count * level2Steps) * combinationCount;
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
        private bool _stopOnMaxDisplacement = true;
        public bool StopOnMaxDisplacement
        {
            get => _stopOnMaxDisplacement;
            set => SetProperty(ref _stopOnMaxDisplacement, value);
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

        // コンストラクタ
        public HorizontalCalculationViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            OnAnalysisModeling();

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

        // コレクション変更時のハンドラ
        private void LoadCasesLevel1_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LoadCase item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
            }
        }
        private void LoadCasesLevel2_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LoadCase item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
            }
        }
        private void LoadCombinations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LoadCombination item in e.NewItems)
                    item.PropertyChanged += LoadCase_PropertyChanged;
            }
        }

        private void LoadCase_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.IsApplicable))
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

        // 全レベル1荷重適用
        [RelayCommand]
        private void ApplyAllLoadCasesLevel1()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel1 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.IsApplicable = true;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        // 全レベル1荷重非適用
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel1()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel1 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel1)
                item.IsApplicable = false;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        // 全レベル2荷重適用
        [RelayCommand]
        private void ApplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsApplicable = true;
            OnPropertyChanged(nameof(TotalCalculationCount));
        }

        // 全レベル2荷重非適用
        [RelayCommand]
        private void UnapplyAllLoadCasesLevel2()
        {
            if (InputModel.LoadCasesInput.LoadCasesLevel2 == null) return;
            foreach (var item in InputModel.LoadCasesInput.LoadCasesLevel2)
                item.IsApplicable = false;
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
            if (Application.Current.MainWindow.DataContext is MainWindowViewModel vm)
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

            // MainWindowViewModelにCurrentModelをセット
            if (Application.Current.MainWindow.DataContext is MainWindowViewModel mainWindowViewModel)
            {
                mainWindowViewModel.CurrentModel = this.CurrentModel; // AnaModels[0]など
            }

            // ダイアログを閉じる
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // 水平解析モデルの作成
        [RelayCommand]
        private void OnAnalysisModeling()
        {
            AnalysisModelling = new AnalysisModelling(InputModel);

            NodesCount = AnalysisModelling.Nodes.Count;
            BeamsCount = AnalysisModelling.Beams.Count;
            RigidBodiesCount = AnalysisModelling.RigidBodies.Count;

            // 編集用モデルを新規作成
            var editModel = new AnaModel(
                _mainWindowViewModel,
                AnalysisModelling.Nodes,
                AnalysisModelling.Beams,
                AnalysisModelling.DummyBeams,
                AnalysisModelling.RigidBodies,
                AnalysisModelling.HorizontalSoilSprings,
                AnalysisModelling.RotationalSprings
            )
            {
                RotationalSprings = AnalysisModelling.RotationalSprings
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
        }

        // 水平解析の実行
        [RelayCommand]
        private async Task OnExecuteAnalysis()
        {
            IsAnalysisRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            try
            {
                await RunAsync(_cancellationTokenSource.Token);
                IsAnalysisExecuted = true; // 解析実行済みフラグをセット
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("計算がキャンセルされました。");
                IsAnalysisExecuted = false;
                // UI にアニメーションでクリアするよう要求
                RequestClearProgressAnimation?.Invoke();
            }
            catch (Exception ex)
            {
                // ログ出力
                System.Diagnostics.Debug.WriteLine($"解析中に例外: {ex}");
                // ユーザー通知
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"解析中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error));
                // 必要なら状態リセット
                IsAnalysisExecuted = false;
                RequestClearProgressAnimation?.Invoke();
            }
            finally
            {
                IsAnalysisRunning = false;
            }
        }

        // 荷重ケースの代表軸力Nで PileSection.GetMphi/MPhiRelationship を呼び、各梁にセット
        // こちらも安全なヘルパに統一（例外発生源を除去）
        private void SetupMphiFromPileSectionForLoadCase(AnaModel model, LoadCase loadCase)
        {
            if (model == null) return;
            if (!loadCase.IsPileNonLinear) return;

            double axialN = loadCase.GetType().GetProperty("NonlinearAxialForceN")?.GetValue(loadCase) is double n ? n : 0.0;

            foreach (var beam in model.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg) continue;
                if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

                var pileBody = InputModel.PileBodies[pb - 1];
                if (seg < 0 || seg >= pileBody.PileBodySegments.Count) continue;

                var section = pileBody.PileBodySegments[seg].PileSection;
                if (section == null) continue;

                var curve = TryCallMphiRelationship(section, axialN);
                if (curve is null) continue;

                beam.SetResolvedCombinedMphi(curve.Value.Phis, curve.Value.Moments);
            }
        }

        // M-φ関係
        private static (IList<double> Phis, IList<double> Moments)? TryCallMphiRelationship(object pileSection, double axialN)
        {
            if (pileSection == null)
            {
                System.Diagnostics.Debug.WriteLine("TryCallMphiRelationship: pileSection is null");
                return null;
            }

            var t = pileSection.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 候補名（大小両対応）
            string[] candidateNames = ["GetMphiRelationship", "GetMPhiRelationship"];

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
            System.Diagnostics.Debug.WriteLine($"TryCallMphiRelationship: type={t.FullName}, foundMethod={foundName}, params={methodInfo.GetParameters().Length}");

            // 呼び出し
            object? ret;
            try
            {
                ret = methodInfo.GetParameters().Length switch
                {
                    1 => methodInfo.Invoke(pileSection, new object[] { axialN }),
                    2 => methodInfo.Invoke(pileSection, new object[] { axialN, 1.0 }),
                    _ => methodInfo.Invoke(pileSection, new object[] { axialN })
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryCallMphiRelationship: invoking {foundName} threw: {ex}");
                return null;
            }

            if (ret == null) return null;

            var rtype = ret.GetType();

            // 1) Tuple-like: Item1/Item2
            var itm1Prop = rtype.GetProperty("Item1");
            var itm2Prop = rtype.GetProperty("Item2");
            if (itm1Prop != null && itm2Prop != null)
            {
                try
                {
                    var v1 = itm1Prop.GetValue(ret) as System.Collections.IEnumerable;
                    var v2 = itm2Prop.GetValue(ret) as System.Collections.IEnumerable;
                    var phis = v1?.Cast<object?>().Where(x => x != null).Select(x => Convert.ToDouble(x)).ToList();
                    var ms = v2?.Cast<object?>().Where(x => x != null).Select(x => Convert.ToDouble(x)).ToList();
                    if (phis != null && ms != null && phis.Count >= 2 && phis.Count == ms.Count)
                        return (phis, ms);
                }
                catch { /* fallthrough */ }
            }

            // 2) Points プロパティ（MomentCurvatureCurve等）
            var pointsProp = rtype.GetProperty("Points") ?? rtype.GetProperty("points");
            if (pointsProp != null)
            {
                try
                {
                    var ptsObj = pointsProp.GetValue(ret) as System.Collections.IEnumerable;
                    if (ptsObj != null)
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

            System.Diagnostics.Debug.WriteLine($"TryCallMphiRelationship: unable to parse return type {rtype.FullName}");
            return null;
        }

        // 荷重ケース用の M-θ（非線形ON/OFFに応じて線形Kを必ず設定、曲線はON時のみ使用）
        private void SetupNonlinearMThetaForLoadCase(AnaModel model, LoadCase loadCase)
        {
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0) return;

            double axialN = loadCase.GetType().GetProperty("NonlinearAxialForceN")?.GetValue(loadCase) is double n ? n : 0.0;
            const double Kmin = 1e-6;   // 特異化回避用の下限
            const double Kbig = 1e12;   // 剛体相当

            foreach (var spring in model.RotationalSprings)
            {
                int pb = (spring.PileBodyNo is int v && v > 0) ? v : 1;
                if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

                var pileBody = InputModel.PileBodies[pb - 1];
                var def = pileBody.GetMThetaRelationship(axialN);

                // 非線形OFF: つねに剛体相当
                if (!loadCase.IsPileNonLinear)
                {
                    spring.Mode = RotationalSpringMode.CombinedXY;
                    spring.CurveXY = null;
                    spring.KthetaXY = Kbig;
                    continue;
                }

                // 非線形ON
                switch (def.Mode)
                {
                    case PileHeadRotationMode.Rigid:
                        // 非線形ONでも「剛」は剛のまま扱う
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = null;
                        spring.KthetaXY = Kbig;
                        break;

                    case PileHeadRotationMode.CombinedXY:
                        spring.Mode = RotationalSpringMode.CombinedXY;
                        spring.CurveXY = def.CurveXY;
                        // sec 側の代替として KthetaXY を設定（優先順位: def.KthetaXY → 曲線の初期接線 → Kmin）
                        if (def.KthetaXY.HasValue && def.KthetaXY.Value > 0.0)
                        {
                            spring.KthetaXY = def.KthetaXY;
                        }
                        else if (spring.CurveXY != null)
                        {
                            double k0 = Math.Max(spring.CurveXY.EvaluateTangent(1e-6), 0.0);
                            spring.KthetaXY = Math.Max(k0, Kmin);
                        }
                        else
                        {
                            spring.KthetaXY = Kmin;
                        }
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
                                spring.Ktheta = Math.Max(k0, Kmin);
                            }
                            else
                            {
                                spring.Ktheta = Kmin;
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
                                spring.Ktheta = Math.Max(k0, Kmin);
                            }
                            else
                            {
                                spring.Ktheta = Kmin;
                            }
                        }
                        break;
                }
            }
        }
        //private void SetupNonlinearMThetaForLoadCase(AnaModel model, LoadCase loadCase)
        //{
        //    if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0) return;

        //    double axialN = loadCase.GetType().GetProperty("NonlinearAxialForceN")?.GetValue(loadCase) is double n ? n : 0.0;
        //    const double Kmin = 1e-6;   // 特異化回避用の下限
        //    const double Kbig = 1e12;   // 非線形OFF時に「最大の線形剛性」として使用する大きな値（剛体相当）

        //    foreach (var spring in model.RotationalSprings)
        //    {
        //        int pb = (spring.PileBodyNo is int v && v > 0) ? v : 1;
        //        if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

        //        var pileBody = InputModel.PileBodies[pb - 1];
        //        var def = pileBody.GetMThetaRelationship(axialN);

        //        if (!loadCase.IsPileNonLinear)
        //        {
        //            // 非線形OFF: 曲線は使わず「大きな線形剛性」で剛性を代替（剛体に近い扱い）
        //            if (def.Mode == PileHeadRotationMode.CombinedXY || def.Mode == PileHeadRotationMode.Rigid)
        //            {
        //                spring.Mode = RotationalSpringMode.CombinedXY;
        //                spring.CurveXY = null;
        //                // 本来の定義値があれば尊重するが、非線形OFFでは最大値を優先
        //                spring.KthetaXY = Kbig;
        //            }
        //            else // Separate
        //            {
        //                spring.Mode = RotationalSpringMode.SingleDof;
        //                spring.Curve = null;
        //                if (spring.Dof == RotationalDof.Rx)
        //                {
        //                    spring.Ktheta = Kbig;
        //                }
        //                else if (spring.Dof == RotationalDof.Ry)
        //                {
        //                    spring.Ktheta = Kbig;
        //                }
        //            }
        //            continue;
        //        }

        //        // 非線形ON: 曲線/線形いずれか（ゼロなら下限）
        //        switch (def.Mode)
        //        {
        //            case PileHeadRotationMode.Rigid:
        //            case PileHeadRotationMode.CombinedXY:
        //                spring.Mode = RotationalSpringMode.CombinedXY;
        //                spring.CurveXY = def.CurveXY;
        //                spring.KthetaXY = def.KthetaXY;
        //                if (spring.CurveXY == null && (!spring.KthetaXY.HasValue || spring.KthetaXY.Value <= 0.0))
        //                    spring.KthetaXY = Kmin;
        //                break;

        //            case PileHeadRotationMode.Separate:
        //                spring.Mode = RotationalSpringMode.SingleDof;
        //                if (spring.Dof == RotationalDof.Rx)
        //                {
        //                    spring.Curve = def.CurveX;
        //                    spring.Ktheta = def.Kx;
        //                    if (spring.Curve == null && (!spring.Ktheta.HasValue || spring.Ktheta.Value <= 0.0))
        //                        spring.Ktheta = Kmin;
        //                }
        //                else if (spring.Dof == RotationalDof.Ry)
        //                {
        //                    spring.Curve = def.CurveY;
        //                    spring.Ktheta = def.Ky;
        //                    if (spring.Curve == null && (!spring.Ktheta.HasValue || spring.Ktheta.Value <= 0.0))
        //                        spring.Ktheta = Kmin;
        //                }
        //                break;
        //        }
        //    }
        //}



        // 直近のばね剛性最小/最大を保持（PrepareKmatで更新）
        private double _lastSpringKMin = double.NaN;
        private double _lastSpringKMax = double.NaN;

        public async Task RunAsync(CancellationToken token)
        {
            CalculationLog.Clear();
            await AddLogAsync("計算開始");
            await Task.Yield();

            // 計算対象モデルを決定（編集用があればそれ、なければ本体）
            var targetModel = AnaModels.Count > 1 ? AnaModels[1] : AnaModels[0];
            if (targetModel == null)
            {
                await AddLogAsync("計算モデルが存在しません。");
                return;
            }

            targetModel.SetSlaveNodes(); // 剛体連結のスレーブ節点のセット
            int calcNo = 0;

            double alpha = 1.0 * Math.Pow(10, -6);
            var level1 = InputModel.LoadCasesInput.LoadCasesLevel1
                .Select((lc, idx) => (loadCase: lc, index: idx, level: 1));
            var level2 = InputModel.LoadCasesInput.LoadCasesLevel2
                .Select((lc, idx) => (loadCase: lc, index: idx, level: 2));
            foreach (var loadCaseitem in InputModel.LoadCasesInput.AllSeismicLoadCases)
            {
                LoadCase loadCase = loadCaseitem;
                int iLC = loadCaseitem.No - 1;
                int level = loadCaseitem.Level;

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
                        targetModel.InitializeStates();

                        // 荷重ケース固有の剛体スレーブ割当を適用（回転ばねの有効/無効を切替）
                        ApplyPileHeadRigidBindingForLoadCase(targetModel, loadCase);

                        // 杭非線形ONのときだけ M–φ/M–θ をセット
                        if (loadCase.IsPileNonLinear)
                        {
                            SetupMphiFromPileSectionForLoadCase(targetModel, loadCase);
                        }
                        // M–θ は常にセット（非線形OFFは剛 KthetaXY=Krigid）
                        SetupNonlinearMThetaForLoadCase(targetModel, loadCase);

                        int nstep = (!loadCase.IsSoilNonLinear && !loadCase.IsPileNonLinear) ? 1 :
                            loadCase.Level == 1 ? Level1CalculationStepsCount :
                            loadCase.Level == 2 ? Level2CalculationStepsCount :
                            1;

                        SetVectorDF(targetModel, loadCase, loadCombination, level, iLC, nstep);
                        targetModel.MapOnVectorDF();
                        InitializeSoilDispIncrement(targetModel, loadCase, loadCombination, level, isLiquefaction, nstep);

                        for (int step = 0; step < nstep; step++)
                        {
                            await Task.Yield(); // ここでUIスレッドを解放
                            token.ThrowIfCancellationRequested();
                            _pauseEvent.Wait(token); // ここで一時停止を考慮

                            calcNo += 1;
                            CurrentProgress = calcNo; // 進捗を更新
                            await AddLogAsync($"[{calcNo}/{TotalCalculationCount}]" + "荷重ケース：" + level + "-" + $"{iLC + 1}" + ", " + "液状化" + (isLiquefaction ? "考慮, " : "非考慮, ") +
                                $"[{iLCOM + 1}]" +
                                "αL:" + $"{loadCombination.Alpha1:N2}" +
                                ", βU:" + $"{loadCombination.Beta1:N2}" +
                                ", βL:" + $"{loadCombination.Beta2:N2}" +
                                ",　荷重ステップ" + (step + 1) + "/" + nstep);
                            targetModel.InitializeNormsqR_onNormsqFint();

                            int n_iter = 1;
                            UpdateSoilDisp();
                            UpdateF(targetModel);

                            // 現ステップ軸力での M–φ 再解決は、杭非線形ONのときのみ
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiByCurrentAxialForces(targetModel);
                            }

                            targetModel.SetR();

                            while (targetModel.NormsROnNormsFint >= alpha)
                            {
                                double diagKMin = double.NaN, diagKMax = double.NaN;
                                double springKMin = double.NaN, springKMax = double.NaN;
                                double dispMaxAbs = double.NaN;

                                // 重い計算をバックグラウンドで実行（診断値もここで算出）
                                await Task.Run(() =>
                                {
                                    // トークンを投げて途中キャンセルを可能にする
                                    token.ThrowIfCancellationRequested();

                                    // N は荷重ケース一定だが、簡便に毎回解決しても可（コストは小）
                                    //SetupNonlinearMThetaForLoadCase(targetModel, loadCase);

                                    // 接線剛性更新は杭非線形ONのときのみ
                                    if (loadCase.IsPileNonLinear)
                                        UpdateBeamMPhiTangent(targetModel);

                                    // Ktan 組立（内部で _lastSpringKMin/_lastSpringKMax を更新）
                                    FindK(iLC, targetModel);

                                    // 1ステップ解く
                                    SolveDdAndUpdateX(targetModel);

                                    // 断面力・T更新と残差更新
                                    FindT(iLC, targetModel);

                                    // 割線剛性更新も杭非線形ONのときのみ
                                    if (loadCase.IsPileNonLinear)
                                        UpdateBeamMPhiSecant(targetModel);

                                    targetModel.FindR();

                                    // 診断値: ばね剛性min/max（PrepareKmatで集計済み）
                                    springKMin = _lastSpringKMin;
                                    springKMax = _lastSpringKMax;

                                    // 診断値: K 対角min/max（現時点のKtanを取得）
                                    (diagKMin, diagKMax) = GetKDiagonalMinMax(targetModel, isTan: true);

                                    // 診断値: 代表自由度の |d| 最大値（節点の増分変位から）
                                    dispMaxAbs = GetMaxAbsIncrementalDisp(targetModel);

                                    // ループ内の要所で再チェック（重い処理の長い段階がある場合はここに複数入れる）
                                    //token.ThrowIfCancellationRequested();

                                }, token);

                                if (StopOnMaxDisplacement && !double.IsNaN(dispMaxAbs) && Math.Abs(dispMaxAbs) > MaxAllowedDisplacement)
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


                                // 残差ログ
                                if (targetModel.NormsROnNormsFint < alpha)
                                {
                                    await AddLogAsync("　　" + "||R||**2 / ||Fint||**2 = " + $"{targetModel.NormsROnNormsFint:E2}" + "≦" + $"{alpha:E2} Converged");
                                }
                                else
                                {
                                    await AddLogAsync("　　" + "||R||**2 / ||Fint||**2 = " + $"{targetModel.NormsROnNormsFint:E2}" + "＞" + $"{alpha:E2}");
                                }

                                // 診断ログ
                                await AddLogAsync($"　　diag(K)[min,max]=[{diagKMin:E3}, {diagKMax:E3}], spring k[min,max]=[{springKMin:E3}, {springKMax:E3}], max|d|={dispMaxAbs:E3}");

                                await Task.Yield(); // UIスレッドを解放
                                n_iter += 1;
                            }

                            targetModel.AnalysisStepResults.Add(new(loadCase, loadCombination, isLiquefaction, step, n_iter, targetModel.NormsROnNormsFint));
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
                        }
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            await AddLogAsync("計算終了");
            MessageBox.Show("計算が終了しました。");
        }

        // RunAsync 内の荷重ケース処理の先頭に以下ヘルパを呼ぶか、そのまま挿入してください。
        // 杭頭回転角helper を別メソッドとして定義する例を示します。
        private void ApplyPileHeadRigidBindingForLoadCase(AnaModel targetModel, LoadCase loadCase)
        {
            if (targetModel?.RigidBodies == null || targetModel.RigidBodies.Count == 0) return;

            var rb0 = targetModel.RigidBodies[0];

            // すべての pile head を一旦削除（ViewModel 側で一貫して操作するため）
            foreach (var pile in InputModel.PileLayoutItems)
            {
                var head = pile.PileNodes?.FirstOrDefault();
                if (head == null) continue;
                rb0.RemoveSlaveNode(head);
            }

            // 非線形 OFF (回転ばねを無効にしたい) 場合は RigidBodies[0] にスレーブして剛結にする
            if (!loadCase.IsPileNonLinear)
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    var head = pile.PileNodes?.FirstOrDefault();
                    if (head == null) continue;
                    rb0.AddSlaveNode(head);
                }
            }

            // 変更を反映：転送行列等を更新
            targetModel.SetSlaveNodes();
        }


        // 接線剛性用: 端部回転から要素中央曲率を評価し、dM/dφ を EI_eff として Ktan（倍率）に反映
        private void UpdateBeamMPhiTangent(AnaModel model)
        {
            foreach (var beam in model.Beams)
            {
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

                var (EIy_eff, EIz_eff) = beam.EvaluateEIeff(phiY, phiZ);

                // 初期 EI
                double EI0y = beam.Section.Material.E * beam.Section.IY;
                double EI0z = beam.Section.Material.E * beam.Section.IZ;

                // 倍率に変換（数値安定化のため上下限）
                double ratioY = (double.IsNaN(EIy_eff) || EI0y <= 0) ? 1.0 : Math.Clamp(EIy_eff / EI0y, 1e-4, 1.0);
                double ratioZ = (double.IsNaN(EIz_eff) || EI0z <= 0) ? 1.0 : Math.Clamp(EIz_eff / EI0z, 1e-4, 1.0);

                System.Diagnostics.Debug.WriteLine($"UpdateBeamMPhiTangent: Beam={beam.Name}, phiY={phiY:E6}, phiZ={phiZ:E6}, EIy_eff={EIy_eff:E6}, EI0y={EI0y:E6}, ratioY={ratioY:E6}, EIz_eff={EIz_eff:E6}, EI0z={EI0z:E6}, ratioZ={ratioZ:E6}");

                beam.Ktan_y = ratioY;
                beam.Ktan_z = ratioZ;
                beam.SetKe(true); // KeTan 再構築
            }
        }

        // 割線剛性用（必要なら接線と同手順でKsecも更新）
        private void UpdateBeamMPhiSecant(AnaModel model)
        {
            foreach (var beam in model.Beams)
            {
                var dI = beam.NodeI.CumulativeDisp.GetVector();
                var dJ = beam.NodeJ.CumulativeDisp.GetVector();
                var disp = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(dI.Count + dJ.Count);
                disp.SetSubVector(0, dI.Count, dI);
                disp.SetSubVector(dI.Count, dJ.Count, dJ);

                var T = PileDesign.FEM.Utils.GetTransformMatrix(beam.NodeI, beam.NodeJ);
                var d = T * disp;

                double thetaYi = d[4], thetaYj = d[10];
                double thetaZi = d[5], thetaZj = d[11];
                double L = Math.Max(beam.Length, 1e-12);

                double phiY = (thetaYj - thetaYi) / L;
                double phiZ = (thetaZj - thetaZi) / L;

                var (EIy_eff, EIz_eff) = beam.EvaluateEIeff(phiY, phiZ);

                double EI0y = beam.Section.Material.E * beam.Section.IZ;
                double EI0z = beam.Section.Material.E * beam.Section.IY;

                double ratioY = (double.IsNaN(EIy_eff) || EI0y <= 0) ? 1.0 : Math.Clamp(EIy_eff / EI0y, 1e-4, 1.0);
                double ratioZ = (double.IsNaN(EIz_eff) || EI0z <= 0) ? 1.0 : Math.Clamp(EIz_eff / EI0z, 1e-4, 1.0);

                beam.Ksec_y = ratioY;
                beam.Ksec_z = ratioZ;
                beam.SetKe(false); // KeSec 再構築
            }
        }

        //private static (IList<double> Phis, IList<double> Moments)? TryCallMphiRelationship(object pileSection, double axialN)
        //{
        //    if (pileSection == null) return null;
        //    var t = pileSection.GetType();
        //    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        //    // 修正: 小文字p→大文字P の順でフォールバック
        //    var mi = t.GetMethod("GetMphiRelationship", flags)
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
        private void SetupMPhiByCurrentAxialForces(AnaModel model)
        {
            if (model == null) return;
            // 各杭について
            foreach (var pile in InputModel.PileLayoutItems)
            {
                // 現ステップの解析軸力（UpdateF で積み上がっている）
                double axialN = pile.AxialForce;

                // セクションは PileBody から取得（セグメントごとに同一 PileSection 系なら同じ曲線が返る）
                int pbIndex = pile.PileBodyNo - 1;
                if (pbIndex < 0 || InputModel.PileBodies == null || pbIndex >= InputModel.PileBodies.Count) continue;
                var pileBody = InputModel.PileBodies[pbIndex];

                // この杭に属する全梁（AnalysisModelling 時に PileLayoutDataItem.Beams に格納済み）
                foreach (var beam in pile.Beams)
                {
                    // セグメント番号は Beam 側に保持している前提（無い場合は 0 扱いでも可）
                    int seg = beam.SegmentIndex ?? 0;
                    if (seg < 0 || seg >= pileBody.PileBodySegments.Count) continue;

                    var pileSection = pileBody.PileBodySegments[seg].PileSection;
                    if (pileSection == null) continue;

                    //var curve = TryCallMphiRelationship(pileSection, axialN);
                    //if (curve is null) continue;

                    //// 合成 M–φ を梁へセット（EvaluateEIeff → Ktan/Ksec に反映される）
                    //beam.SetResolvedCombinedMphi(curve.Value.Phis, curve.Value.Moments);
                    var curve = TryCallMphiRelationship(pileSection, axialN);
                    if (curve is null)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetupMPhiByCurrentAxialForces: Beam={beam.Name}, pileNo={pile.No}, seg={seg}, axialN={axialN:E} -> curve=null");
                        continue;
                    }
                    // ログ: 点数と先頭数点を出力
                    try
                    {
                        var phisArr = curve.Value.Phis.ToArray();
                        var msArr = curve.Value.Moments.ToArray();
                        int cnt = phisArr.Length;
                        var first5 = string.Join(", ", phisArr.Take(5).Select(x => x.ToString("E6")));
                        var first5m = string.Join(", ", msArr.Take(5).Select(x => x.ToString("E6")));
                        System.Diagnostics.Debug.WriteLine($"SetupMPhiByCurrentAxialForces: Beam={beam.Name}, pileNo={pile.No}, seg={seg}, axialN={axialN:E}, Points={cnt}, phis_first5=[{first5}], moms_first5=[{first5m}]");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetupMPhiByCurrentAxialForces: Beam={beam.Name} logging error: {ex}");
                    }

                    beam.SetResolvedCombinedMphi(curve.Value.Phis, curve.Value.Moments);
                }
            }
        }

        // 荷重ケース用の M-θ 曲線/剛性を「N に応じて」解決して各回転ばねにセット
        //private void SetupNonlinearMThetaForLoadCase(AnaModel model, LoadCase loadCase)
        //{
        //    if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0) return;

        //    double axialN = loadCase.GetType().GetProperty("NonlinearAxialForceN")?.GetValue(loadCase) is double n ? n : 0.0;
        //    const double Kmin = 1e-6; // ゼロ剛性の機構化回避用下限（必要に応じて調整）

        //    foreach (var spring in model.RotationalSprings)
        //    {
        //        int pb = (spring.PileBodyNo is int v && v > 0) ? v : 1;
        //        if (pb <= 0 || pb > InputModel.PileBodies.Count) continue;

        //        var pileBody = InputModel.PileBodies[pb - 1];
        //        var def = pileBody.GetMThetaRelationship(axialN);

        //        if (!loadCase.IsPileNonLinear)
        //        {
        //            // 非線形OFF: 線形Kのみを適用（曲線は使わない）
        //            if (def.Mode == PileHeadRotationMode.CombinedXY)
        //            {
        //                double k = def.KthetaXY ?? (def.CurveXY != null ? Math.Max(def.CurveXY.EvaluateTangent(1e-6), 0.0) : 0.0);
        //                spring.Mode = RotationalSpringMode.CombinedXY;
        //                spring.CurveXY = null;
        //                spring.KthetaXY = Math.Max(k, Kmin);
        //            }
        //            else // Separate
        //            {
        //                spring.Mode = RotationalSpringMode.SingleDof;

        //                if (spring.Dof == RotationalDof.Rx)
        //                {
        //                    double kx = def.Kx ?? (def.CurveX != null ? Math.Max(def.CurveX.EvaluateTangent(1e-6), 0.0) : 0.0);
        //                    spring.Curve = null;
        //                    spring.Ktheta = Math.Max(kx, Kmin);
        //                }
        //                else if (spring.Dof == RotationalDof.Ry)
        //                {
        //                    double ky = def.Ky ?? (def.CurveY != null ? Math.Max(def.CurveY.EvaluateTangent(1e-6), 0.0) : 0.0);
        //                    spring.Curve = null;
        //                    spring.Ktheta = Math.Max(ky, Kmin);
        //                }
        //            }
        //            continue;
        //        }

        //        // 非線形ON: 従来どおり曲線/線形Kを適用
        //        switch (def.Mode)
        //        {
        //            case PileHeadRotationMode.Rigid:
        //            case PileHeadRotationMode.CombinedXY:
        //                spring.Mode = RotationalSpringMode.CombinedXY;
        //                spring.CurveXY = def.CurveXY;
        //                spring.KthetaXY = def.KthetaXY;
        //                // 念のためゼロ回避
        //                if (spring.CurveXY == null && (!spring.KthetaXY.HasValue || spring.KthetaXY.Value <= 0.0))
        //                    spring.KthetaXY = Kmin;
        //                break;

        //            case PileHeadRotationMode.Separate:
        //                spring.Mode = RotationalSpringMode.SingleDof;
        //                if (spring.Dof == RotationalDof.Rx)
        //                {
        //                    spring.Curve = def.CurveX;
        //                    spring.Ktheta = def.Kx;
        //                    if (spring.Curve == null && (!spring.Ktheta.HasValue || spring.Ktheta.Value <= 0.0))
        //                        spring.Ktheta = Kmin;
        //                }
        //                else if (spring.Dof == RotationalDof.Ry)
        //                {
        //                    spring.Curve = def.CurveY;
        //                    spring.Ktheta = def.Ky;
        //                    if (spring.Curve == null && (!spring.Ktheta.HasValue || spring.Ktheta.Value <= 0.0))
        //                        spring.Ktheta = Kmin;
        //                }
        //                break;
        //        }
        //    }
        //}


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

        // 要素剛性マトリクスの計算メソッド（Ktanの組立: ばね剛性 min/max を集計）
        private void FindK(int iLC, AnaModel targetModel)
        {
            PrepareKmat(iLC, true, targetModel); // node.TangentSpringのセット _lastSpringKMin/_lastSpringKMax を内部で更新
            targetModel.MapOnKtanMat(); // 要素剛性、節点剛性の剛性マトリクスKmatへのマッピング
        }

        private static void SolveDdAndUpdateX(AnaModel targetModel) // Solve Ku = -R 全体剛性方程式の求解, Update x = x + u 配置更新 // R = R - dF
                                                                    // >> Solve Ku = R にする。
        {
            Solver.SolveDisp(targetModel); // 増分変位  [d] = inv([Kaa_tan])[-R]
        }

        // K対角の最小/最大を取得（isTan=trueで接線剛性）
        private static (double min, double max) GetKDiagonalMinMax(AnaModel model, bool isTan)
        {
            // GetForcedDispOnLoadVectorAndStiffnessMatrix: (K, rhs) を返す前提
            var (K, _) = model.GetForcedDispOnLoadVectorAndStiffnessMatrix(isTan);
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            int n = K.RowCount;
            for (int i = 0; i < n; i++)
            {
                double v = K[i, i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (double.IsInfinity(min)) min = double.NaN;
            if (double.IsInfinity(max)) max = double.NaN;
            return (min, max);
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

        // Find T 内力ベクトルの計算メソッド
        private void FindT(int iLC, AnaModel targetModel)
        {
            PrepareKsecMat(iLC, targetModel); // node.SecantSpringのセット // 要素応力の計算
            foreach (Beam beam in targetModel.Beams) beam.SetBeamDispAndForce();
            foreach (HorizontalSoilSpring horizontalSoilSpring in targetModel.HorizontalSoilSprings)
                horizontalSoilSpring.SetBeamDispAndForce();

            targetModel.MapOnKsecMat();
            targetModel.SetT();
        }

        private void InitializeSoilDispIncrement(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, bool isLiquefaction, double nstep)
        {
            double loadAngle = loadCase.LoadAngle;
            double alpha1 = loadCombination.Alpha1;
            NodeDisp initialCumulativeSoilDisp = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

            // 共通の地盤変位計算ローカル関数
            static NodeDisp CalcDisp(double disp1, double disp2, int level, double alpha1, double nstep, double loadAngle)
            {
                double groundDisp = (level == 1 ? disp1 : disp2) * alpha1 / nstep / 1000.0;

                double rad = loadAngle * Math.PI / 180.0;
                double groundDispX = groundDisp * Math.Cos(rad);
                double groundDispY = groundDisp * Math.Sin(rad);
                return new NodeDisp(groundDispX, groundDispY, 0.0, 0.0, 0.0, 0.0);
            }

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1];
                for (int i = 0; i < soilPile.ZDataItems.Count; i++)
                {
                    var zData = soilPile.ZDataItems[i];
                    double groundDisp1 = isLiquefaction ? zData.GroundDisp1L : zData.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zData.GroundDisp2L : zData.GroundDisp2;
                    NodeDisp dd = CalcDisp(groundDisp1, groundDisp2, level, alpha1, nstep, loadAngle);

                    pileLayoutItem.SoilNodes[i].SetIncrementalForcedDisp(dd);
                    pileLayoutItem.SoilNodes[i].SetCumulativeForcedDisp(initialCumulativeSoilDisp);
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
                    NodeDisp dd = CalcDisp(groundDisp1, groundDisp2, level, alpha1, nstep, loadAngle);
                    FEM.Node soilNode = targetModel.FindNode("SoilNode", null, null, z);
                    soilNode.SetIncrementalForcedDisp(dd);
                    soilNode.SetCumulativeForcedDisp(initialCumulativeSoilDisp);
                }
            }
        }

        // 増分荷重の取得 慣性力の節点荷重へのセット
        private void SetVectorDF(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, int iLC, double nstep) // PileDesign
        {
            double loadAngle = loadCase.LoadAngle;
            double beta1 = loadCombination.Beta1; // 荷重組合せ上部構造慣性力の荷重係数β1 
            double beta2 = loadCombination.Beta2; // 荷重組合せ基礎構造慣性力の荷重係数β2 

            double upperMassForce = loadCase.UpperMassForce; // 上部構造質量荷重 [kN]
            double foundationMassForce = loadCase.FoundationMassForce; // 基礎構造質量荷重 [kN]

            double force = beta1 * upperMassForce + beta2 * foundationMassForce; // 上部構造質量荷重 + 基礎構造質量荷重[kN]
            double deltaForce = force / nstep; // 増分荷重 [kN]
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
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nstep; // レベル1の杭軸力増分 [kN]
                }
                else //(level == 2)
                {
                    pileLayoutItem.AxialForceIncrement = (pileLayoutItem.AxialForceLevel2s[iLC]
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional)) / nstep; // レベル2の杭軸力増分 [kN]
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

        private void PrepareKmat(int iLC, bool isTan, AnaModel model)
        {
            // 診断: ばね剛性の min/max を集計
            double springMin = double.PositiveInfinity;
            double springMax = double.NegativeInfinity;

            // 土圧合力ばね
            if (InputModel.ElementDivision.DoatsuGoryokuBane != null)
            {
                for (int i = 0; i < InputModel.ElementDivision.DoatsuGoryokuBane.Items.Count; i++)
                {
                    var item = InputModel.ElementDivision.DoatsuGoryokuBane.Items[i];

                    var reldispTop = item.TopEmbedmentNode.CumulativeDisp - item.TopSoilNode.CumulativeDisp;
                    var kvecTop = isTan ? item.GetTangentStiffnessVector(reldispTop) : item.GetSecantStiffnessVector(reldispTop);
                    double kxTop = SafeK(kvecTop.Kx);
                    double kyTop = SafeK(kvecTop.Ky);
                    item.TopHorizontalSoilSpring.SetKe(kxTop, kyTop, 0, 0, 0, 0, isTan);
                    springMin = Math.Min(springMin, Math.Min(kxTop, kyTop));
                    springMax = Math.Max(springMax, Math.Max(kxTop, kyTop));

                    var reldispBtm = item.BtmEmbedmentNode.CumulativeDisp - item.BtmSoilNode.CumulativeDisp;
                    var kvecBtm = isTan ? item.GetTangentStiffnessVector(reldispBtm) : item.GetSecantStiffnessVector(reldispBtm);
                    double kxBtm = SafeK(kvecBtm.Kx);
                    double kyBtm = SafeK(kvecBtm.Ky);
                    item.BtmHorizontalSoilSpring.SetKe(kxBtm, kyBtm, 0, 0, 0, 0, isTan);
                    springMin = Math.Min(springMin, Math.Min(kxBtm, kyBtm));
                    springMax = Math.Max(springMax, Math.Max(kxBtm, kyBtm));
                }
            }

            // 杭ばね
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var horizontalReactions = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1].HorizontalSoilReactions;
                var isFrontPile = pileLayoutItem.IsFrontPiles[iLC];

                for (int i = 0; i < pileLayoutItem.PileNodes.Count; i++)
                {
                    var pileNode = pileLayoutItem.PileNodes[i];
                    var soilNode = pileLayoutItem.SoilNodes[i];
                    var reldisp = pileNode.CumulativeDisp - soilNode.CumulativeDisp;
                    // NaN防止
                    double abs = (double.IsFinite(reldisp.Ux) && double.IsFinite(reldisp.Uy))
                        ? Math.Sqrt(reldisp.Ux * reldisp.Ux + reldisp.Uy * reldisp.Uy)
                        : 0.0;

                    double k = 0.0;
                    if (i > 0)
                    {
                        bool isTop = false;
                        k += isTan
                            ? horizontalReactions[i - 1].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile)
                            : horizontalReactions[i - 1].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile);
                    }
                    if (i < pileLayoutItem.PileNodes.Count - 1)
                    {
                        bool isTop = true;
                        k += isTan
                            ? horizontalReactions[i].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile)
                            : horizontalReactions[i].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile);
                    }

                    k = SafeK(k); // NaN/負値→0
                    pileLayoutItem.HorizontalSoilSprings[i].SetKe(k, k, 0, 0, 0, 0, isTan);

                    springMin = Math.Min(springMin, k);
                    springMax = Math.Max(springMax, k);
                }
            }

            // 追加: 杭頭 M-θ を RotationalSpring の Ke に反映
            if (model?.RotationalSprings != null && model.RotationalSprings.Count > 0)
            {
                const double KminRot = 1e-6;
                // const double Kbig = 1e12; // 旧: 並進・Rz の剛結用の巨大値

                foreach (var pile in InputModel.PileLayoutItems)
                {
                    var topBeam = pile.Beams?.FirstOrDefault(b => b.IsPileTop);
                    if (topBeam == null) continue;

                    var pileHeadNode = topBeam.NodeI;

                    var rxy = model.RotationalSprings
                        .FirstOrDefault(rs => rs.NodeJ == pileHeadNode);
                    if (rxy == null) continue;

                    var capNode = rxy.NodeI;
                    double dRx = (pileHeadNode.CumulativeDisp?.Rx ?? 0.0) - (capNode.CumulativeDisp?.Rx ?? 0.0);
                    double dRy = (pileHeadNode.CumulativeDisp?.Ry ?? 0.0) - (capNode.CumulativeDisp?.Ry ?? 0.0);

                    double kRx = 0.0, kRy = 0.0;
                    if (rxy.Mode == RotationalSpringMode.CombinedXY)
                    {
                        double theta = Math.Sqrt(dRx * dRx + dRy * dRy);
                        double kxy = isTan
                            ? (rxy.CurveXY != null ? SafeK(rxy.CurveXY.EvaluateTangent(theta)) : SafeK(rxy.KthetaXY ?? 0.0))
                            : (rxy.KthetaXY.HasValue ? SafeK(rxy.KthetaXY.Value) : (rxy.CurveXY != null ? SafeK(rxy.CurveXY.EvaluateTangent(theta)) : 0.0));
                        kxy = Math.Max(kxy, KminRot);
                        kRx = kxy; kRy = kxy;
                    }
                    else
                    {
                        if (rxy.Dof == RotationalDof.Rx)
                        {
                            double k = isTan
                                ? (rxy.Curve != null ? SafeK(rxy.Curve.EvaluateTangent(dRx)) : SafeK(rxy.Ktheta ?? 0.0))
                                : (rxy.Ktheta.HasValue ? SafeK(rxy.Ktheta.Value) : (rxy.Curve != null ? SafeK(rxy.Curve.EvaluateTangent(dRx)) : 0.0));
                            kRx = Math.Max(k, KminRot);
                            kRy = KminRot;
                        }
                        else if (rxy.Dof == RotationalDof.Ry)
                        {
                            double k = isTan
                                ? (rxy.Curve != null ? SafeK(rxy.Curve.EvaluateTangent(dRy)) : SafeK(rxy.Ktheta ?? 0.0))
                                : (rxy.Ktheta.HasValue ? SafeK(rxy.Ktheta.Value) : (rxy.Curve != null ? SafeK(rxy.Curve.EvaluateTangent(dRy)) : 0.0));
                            kRy = Math.Max(k, KminRot);
                            kRx = KminRot;
                        }
                    }

                    // 変更: RotationalSpring は回転成分のみ設定（並進/その他を巨大値で固定しない）
                    // 引数は (kx, ky, kz, kxx(Rx), kyy(Ry), kzz(Rz), isTan)
                    System.Diagnostics.Debug.WriteLine($"PrepareKmat: RotSpring={rxy.Name}, Mode={rxy.Mode}, dRx={dRx:E3}, dRy={dRy:E3}, set_kRx={kRx:E3}, set_kRy={kRy:E3}, isTan={isTan}");

                    const double Kbig = 1e12; // 数値不安定なら 1e9～1e11 に調整
                    double kx = rxy.TieUx ? Kbig : 0.0;
                    double ky = rxy.TieUy ? Kbig : 0.0;
                    double kz = rxy.TieUz ? Kbig : 0.0;
                    double kRz = rxy.TieRz ? Kbig : 0.0;
                    // Rx/Ry は M–θ に基づき算出した kRx/kRy を用いる
                    rxy.SetKe(kx, ky, kz, kRx, kRy, kRz, isTan);

                }
            }

            _lastSpringKMin = double.IsInfinity(springMin) ? double.NaN : springMin;
            _lastSpringKMax = double.IsNegativeInfinity(springMax) ? double.NaN : springMax;
        }


        // ばね剛性の安全化ヘルパ
        //private static double SafeK(double v)
        //    => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        // 変更: ラッパーに model 引数を追加
        private void PrepareKtanMat(int iLC, AnaModel model) => PrepareKmat(iLC, true, model);
        private void PrepareKsecMat(int iLC, AnaModel model) => PrepareKmat(iLC, false, model);

        // 既存: K組立本体（model を受け取る版）
        //private void PrepareKmat(int iLC, bool isTan, AnaModel model)
    }
}