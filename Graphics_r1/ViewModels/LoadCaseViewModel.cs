using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using PileDesign.Common;

namespace PileDesign.ViewModels

{
    public partial class LoadCaseViewModel : BaseViewModel, PileDesign.Common.Undo.ISnapshotUndoHost, IDialogAppliedState
    {
        private readonly UndoManager _undoManager = new();

        // 窓の段で控えを取る仕組み (UndoCapture) に、履歴と控えの取り方を渡す
        UndoManager PileDesign.Common.Undo.ISnapshotUndoHost.UndoManager => _undoManager;
        object PileDesign.Common.Undo.ISnapshotUndoHost.CaptureUndoSnapshot() => CaptureUndoState();
        private readonly MainWindowViewModel _mainWindowViewModel;
        // ViewModelにView参照用プロパティを追加
        public LoadCaseWindow LoadCaseWindowInstance { get; set; }

        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private LoadCaseCommon _loadCaseLevel1Common;

        [ObservableProperty]
        private LoadCaseCommon _loadCaseLevel2Common;

        [ObservableProperty]
        private ObservableCollection<LoadCase> _loadCasesLevel1;

        [ObservableProperty]
        private ObservableCollection<LoadCase> _loadCasesLevel2;

        [ObservableProperty]
        private ObservableCollection<LoadCombination> _loadCombinations;

        [ObservableProperty]
        private ObservableCollection<LoadCombination> _loadCombinationsPlus;

        [ObservableProperty]
        private double _loadCombinationFactor;

        public ObservableCollection<LoadCaseCommon> LoadCasesLevel1Common { get; set; }
        public ObservableCollection<LoadCaseCommon> LoadCasesLevel2Common { get; set; }
        public ObservableCollection<string> AllLoadCases { get; set; }

        public ObservableCollection<ObservableCollection<double>> FactoredTotalMassForces1 { get; set; }
        public ObservableCollection<ObservableCollection<double>> FactoredTotalMassForces2 { get; set; }


        [ObservableProperty]
        private ObservableCollection<ObservableCollection<double>> _massForceCombinations1;

        [ObservableProperty]
        private ObservableCollection<ObservableCollection<double>> _massForceCombinations2;

        public int LoadCombinationsCount => LoadCombinations?.Count ?? 0;

        public ObservableCollection<double> MassForceCombination1 => MassForceCombinations1 != null && MassForceCombinations1.Count > 0 ? MassForceCombinations1[0] : null;
        public ObservableCollection<double> MassForceCombination2 => MassForceCombinations1 != null && MassForceCombinations1.Count > 1 ? MassForceCombinations1[1] : null;

        public int MassForceCombinations1FirstCount =>
            (MassForceCombinations1 != null && MassForceCombinations1.Count > 0 && MassForceCombinations1[0] != null) ?
            MassForceCombinations1[0].Count
            : 0;

        public int MassForceCombinations2FirstCount =>
            (MassForceCombinations2 != null && MassForceCombinations2.Count > 0 && MassForceCombinations2[0] != null) ?
            MassForceCombinations2[0].Count
            : 0;

        // Undo/Redo用の状態クラス
        private class LoadCaseState
        {
            public ObservableCollection<LoadCase> LoadCasesLevel1 { get; set; }
            public ObservableCollection<LoadCase> LoadCasesLevel2 { get; set; }
            public ObservableCollection<LoadCombination> LoadCombinations { get; set; }
            public ObservableCollection<LoadCombination> LoadCombinationsPlus { get; set; }
            public double LoadCombinationFactor { get; set; }
            public ObservableCollection<LoadCaseCommon> LoadCasesLevel1Common { get; set; }
            public ObservableCollection<LoadCaseCommon> LoadCasesLevel2Common { get; set; }
        }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;
        private readonly LoadCasesInput PrevLoadCasesInput;

        // コンストラクタ
        public LoadCaseViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _suppressUndoSave = true;   // 組み立て中は控えを取らない (最後に 1 段だけ積む)

            PrevLoadCasesInput = InputModel.LoadCasesInput.DeepCopy();

            _loadCaseLevel1Common = InputModel.LoadCasesInput.LoadCaseLevel1Common;
            _loadCaseLevel2Common = InputModel.LoadCasesInput.LoadCaseLevel2Common;

            _loadCasesLevel1 = InputModel.LoadCasesInput.LoadCasesLevel1;
            _loadCasesLevel2 = InputModel.LoadCasesInput.LoadCasesLevel2;

            _loadCombinations = InputModel.LoadCasesInput.LoadCombinations;
            _loadCombinationsPlus = InputModel.LoadCasesInput.LoadCombinationsPlus;


            _loadCombinationFactor = InputModel.LoadCasesInput.LoadCombinationFactor;

            LoadCasesLevel1Common = [LoadCaseLevel1Common];
            LoadCasesLevel2Common = [LoadCaseLevel2Common];

            LoadCombinations = KeepCombinationSelection(GetCombinations(_loadCombinationFactor));
            LoadCombinationsPlus = GetCombinationsPlus(_loadCombinationFactor);

            SetAllLoadCases();
            SetTotalMassForces(); // 初期データの設定

            SubscribeLoadCasePropertyChanged();

            // 組み立て中に積んだ控えを捨て、開いた時点の姿を 1 段だけ積む。
            // 以前は LoadCombinations の代入が組み立ての途中で控えを積んでいたため、
            // 履歴の先頭が「半分できた状態」になっていた。
            _suppressUndoSave = false;
            _undoManager.Clear();
            PushUndoState();
        }

        // 控えを取らない区間の目印。
        //
        // 戻す処理の中で控えを積むと、UndoManager.SaveState が現在位置より後ろを
        // 切り捨てるので、その場で Redo が消える。組み立て中も同じで、
        // 半分できた状態が履歴の先頭に入ってしまう。
        private bool _suppressUndoSave;

        // 状態を保存
        public void PushUndoState()
        {
            if (_suppressUndoSave) return;
            _undoManager.SaveState(CaptureUndoState());
        }

        // 控えを取る。形を組み立てるのはここだけ
        private LoadCaseState CaptureUndoState()
        {
            return new LoadCaseState
            {
                LoadCasesLevel1 = new ObservableCollection<LoadCase>(LoadCasesLevel1.Select(x => x.DeepCopy())),
                LoadCasesLevel2 = new ObservableCollection<LoadCase>(LoadCasesLevel2.Select(x => x.DeepCopy())),
                LoadCombinations = new ObservableCollection<LoadCombination>(LoadCombinations.Select(x => x.DeepCopy())),
                LoadCombinationsPlus = new ObservableCollection<LoadCombination>(LoadCombinationsPlus.Select(x => x.DeepCopy())),
                LoadCombinationFactor = LoadCombinationFactor,
                LoadCasesLevel1Common = new ObservableCollection<LoadCaseCommon>(LoadCasesLevel1Common.Select(x => x.DeepCopy())),
                LoadCasesLevel2Common = new ObservableCollection<LoadCaseCommon>(LoadCasesLevel2Common.Select(x => x.DeepCopy()))
            };
        }

        // 状態を復元
        /// <summary>
        /// 控えを戻す。
        ///
        /// <para><b>荷重ケースのコレクションを差し替えない。</b>この画面は
        /// <c>InputModel.LoadCasesInput</c> が持つコレクションの実体をそのまま編集しており
        /// (OK は割増係数と荷重組合せしか書き戻さない)、差し替えると実体から外れる。
        /// 外れると、画面には戻った値が出るのに実体は編集後のままになり、
        /// <b>そのあとに打った値もどこにも届かなくなる</b>
        /// (実測: Undo 後に実体 1111 / 画面 1000、その後 777 を打っても実体は 1111)。
        /// 中身だけを写す。</para>
        ///
        /// <para>荷重組合せ (<c>LoadCombinations</c> / <c>LoadCombinationsPlus</c>) は
        /// 割増係数から組み立てる画面側の持ち物で、OK で書き戻す作りなので差し替えてよい。</para>
        /// </summary>
        private void RestoreState(LoadCaseState state)
        {
            bool previous = _suppressUndoSave;
            _suppressUndoSave = true;
            try
            {
                RestoreItemsInPlace(LoadCasesLevel1, state.LoadCasesLevel1);
                RestoreItemsInPlace(LoadCasesLevel2, state.LoadCasesLevel2);
                RestoreItemsInPlace(LoadCasesLevel1Common, state.LoadCasesLevel1Common);
                RestoreItemsInPlace(LoadCasesLevel2Common, state.LoadCasesLevel2Common);

                LoadCombinationFactor = state.LoadCombinationFactor;
                LoadCombinations = new ObservableCollection<LoadCombination>(
                    state.LoadCombinations.Select(x => x.DeepCopy()));
                LoadCombinationsPlus = new ObservableCollection<LoadCombination>(
                    state.LoadCombinationsPlus.Select(x => x.DeepCopy()));

                SubscribeLoadCasePropertyChanged();
                SetTotalMassForces();
                OnPropertyChanged(nameof(LoadCasesLevel1));
                OnPropertyChanged(nameof(LoadCasesLevel1Common));
                OnPropertyChanged(nameof(LoadCasesLevel2));
                OnPropertyChanged(nameof(LoadCasesLevel2Common));
                RefreshDataGrids();
            }
            finally
            {
                _suppressUndoSave = previous;
            }
        }

        /// <summary>
        /// コレクションの<b>中身だけ</b>を控えの値に戻す。実体は入れ替えない。
        ///
        /// 件数が合わないときだけ作り直す。この画面に荷重ケースの追加・削除は無いので
        /// 通常は通らないが、通ったときに黙って何もしないのは避ける。
        /// </summary>
        private static void RestoreItemsInPlace<T>(
            ObservableCollection<T> target, ObservableCollection<T> saved)
            where T : Models.BaseModel
        {
            if (target == null || saved == null) return;

            if (target.Count != saved.Count)
            {
                target.Clear();
                foreach (var item in saved) target.Add(item);
                return;
            }

            for (int i = 0; i < target.Count; i++)
            {
                if (target[i] == null || saved[i] == null) continue;
                Common.ModelRestore.CopyScalarFields(target[i], saved[i]);
                target[i].OnPropertyChanged(string.Empty);   // 空文字は「全部」の意味
            }
        }

        [RelayCommand]
        private void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                PushUndoState();
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is LoadCaseState state)
            {
                RestoreState(state);
            }
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is LoadCaseState state)
            {
                RestoreState(state);
            }
        }

        // 例: 編集前にPushUndoState()を呼ぶ
        partial void OnLoadCasesLevel1Changed(ObservableCollection<LoadCase> value)
        {
            PushUndoState();
            LoadCasesLevel1 = value;
            SubscribeLoadCasePropertyChanged();
        }

        partial void OnLoadCasesLevel2Changed(ObservableCollection<LoadCase> value)
        {
            PushUndoState();
            LoadCasesLevel2 = value;
            SubscribeLoadCasePropertyChanged();
        }

        private void SubscribeLoadCasePropertyChanged()
        {
            if (LoadCasesLevel1 != null)
            {
                foreach (var loadCase in LoadCasesLevel1)
                {
                    loadCase.PropertyChanged -= LoadCase_PropertyChanged;
                    loadCase.PropertyChanged += LoadCase_PropertyChanged;
                }
            }

            if (LoadCasesLevel2 != null)
            {
                foreach (var loadCase in LoadCasesLevel2)
                {
                    loadCase.PropertyChanged -= LoadCase_PropertyChanged;
                    loadCase.PropertyChanged += LoadCase_PropertyChanged;
                }
            }
        }

        private void LoadCase_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.UpperMassForce) || e.PropertyName == nameof(LoadCase.FoundationMassForce))
            {
                SetTotalMassForces();
            }
        }

        partial void OnLoadCombinationsChanged(ObservableCollection<LoadCombination> value)
        {
            PushUndoState();
            LoadCombinations = value;
            SetTotalMassForces();

            RefreshDataGrids();
        }

        // 割増係数を変える<b>前に</b>控えを取る (係数と荷重組合せが揃った姿)。
        // 以前は変えたあと・組合せを作り直す前に取っていたので、控えは「新しい係数・古い組合せ」という
        // どの時点にも無かった組み合わせになり、ウィンドウの中の Ctrl+Z がそれを戻していた。
        // この画面の控えは「編集の直前の姿」(セルの編集と同じ)。作り直しの途中の組合せの代入
        // (OnLoadCombinationsChanged) でも控えない。代入の時点ではまだ LoadCombinationsPlus が古い係数のもので、
        // やはり揃っていない。作り直したあとの姿は、Ctrl+Z の時点で Undo が積む
        partial void OnLoadCombinationFactorChanging(double value)
        {
            PushUndoState();
        }

        partial void OnLoadCombinationFactorChanged(double value)
        {
            bool previous = _suppressUndoSave;
            _suppressUndoSave = true;
            try
            {
                LoadCombinationFactor = value;
                LoadCombinations = KeepCombinationSelection(GetCombinations(value));
                LoadCombinationsPlus = GetCombinationsPlus(value);
            }
            finally { _suppressUndoSave = previous; }
            SetTotalMassForces();
            // 依存プロパティの更新通知を追加
            OnPropertyChanged(nameof(LoadCombinations)); // 追加
            RefreshDataGrids();
        }


        [RelayCommand]
        private void OnOk()
        {
            // 荷重ケース名が空欄・重複だと、グラフ・表で荷重ケースを見分けられない (選択が名前で行われる)
            var invalidNames = InputModel.LoadCasesInput.DescribeInvalidLoadCaseNames();
            if (invalidNames.Count > 0)
            {
                Services.MessageService.Show(
                    "荷重ケース名を直してください。空欄や重複した名前では、グラフ・表・計算書で荷重ケースを見分けられません。\n\n"
                    + string.Join("\n", invalidNames.Take(10)),
                    "荷重ケース名", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 荷重条件の変更はジオメトリ (メッシュ) に影響しないため、杭要素分割は保持する。
            // 解析結果のみリセットする (旧 CheckAndResetElementSplit は分割も破棄していた)。
            if (!_mainWindowViewModel.CheckAndResetAnalysisResultsKeepingSplit("荷重条件"))
                return;

            InputModel.LoadCasesInput.LoadCombinationFactor = LoadCombinationFactor;
            InputModel.LoadCasesInput.LoadCombinations = new ObservableCollection<LoadCombination>(LoadCombinations);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // キャンセルで閉じたら「何も適用していない」と呼び出し側へ伝える (IDialogAppliedState)。
        // 以前は実装しておらず、キャンセルでも「再解析が必要」が立ち、入力変更後の帯に「…編集」と出た。
        // キャンセル以外 (× など) は従来どおり適用した扱いにする。
        private bool _cancelled;
        public bool AppliedChanges => !_cancelled;

        [RelayCommand]
        private void OnCancel()
        {
            _cancelled = true;
            InputModel.LoadCasesInput = PrevLoadCasesInput.DeepCopy();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void SliderValueChanged()
        {
            LoadCombinations = KeepCombinationSelection(GetCombinations(LoadCombinationFactor));
            LoadCombinationsPlus = GetCombinationsPlus(LoadCombinationFactor);
            SetTotalMassForces();
        }

        /// <summary>
        /// 作り直した荷重組合せに、それまでの「対象」(IsApplicable) と「計算書に載せるか」を番号で引き継ぐ。
        /// 以前は開くたびに組合せを作り直して「対象」を既定 (ON) に戻しており、水平解析ウィンドウで外した組合せが、
        /// 荷重の画面で別の値を直して OK しただけで全部戻り、次の解析で黙って解かれた。
        /// </summary>
        private ObservableCollection<LoadCombination> KeepCombinationSelection(ObservableCollection<LoadCombination> fresh)
        {
            var previous = LoadCombinations ?? InputModel?.LoadCasesInput?.LoadCombinations;
            if (previous == null) return fresh;
            foreach (var c in fresh)
            {
                var old = previous.FirstOrDefault(p => p != null && p.No == c.No);
                if (old == null) continue;
                c.IsApplicable = old.IsApplicable;
                c.IsIncludedInReport = old.IsIncludedInReport;
            }
            return fresh;
        }

        public static ObservableCollection<LoadCombination> GetCombinations(double factor)
        {
            if (factor == 1)
            {
                return [new LoadCombination(1, 1.0, 1.0, 1.0)];
            }
            else if (0.5 <= factor && factor < 1.0)
            {
                return ([
                    new LoadCombination(1, factor, -1.0, factor),
                    new LoadCombination(2, 1.0, -factor, 1.0),
                    new LoadCombination(3, factor, 1.0, factor),
                    new LoadCombination(4, 1.0, factor, 1.0)
                    ]);
            }
            else
            {
                return [new LoadCombination(0, 0.0, 0.0, 0.0)];
            }
        }

        // エラー回避のために1～3までもつ
        public static ObservableCollection<LoadCombination> GetCombinationsPlus(double factor)
        {
            if (factor == 1)
            {
                return ([
                    new LoadCombination(1, 1.0, 1.0, 1.0),
                    new LoadCombination(1, 1.0, 1.0, 1.0),
                    new LoadCombination(1, 1.0, 1.0, 1.0),
                    new LoadCombination(1, 1.0, 1.0, 1.0),
                    ]);
            }
            else if (0.5 <= factor && factor < 1.0)
            {
                return ([
                    new LoadCombination(1, factor, -1.0, factor),
                    new LoadCombination(2, 1.0, -factor, 1.0),
                    new LoadCombination(3, factor, 1.0, factor),
                    new LoadCombination(4, 1.0, factor, 1.0)
                    ]);
            }
            else
            {
                return [new LoadCombination(0, 0.0, 0.0, 0.0)];
            }
        }

        public void SetTotalMassForces()
        {
            MassForceCombinations1 = [];
            foreach (LoadCase loadCase1 in LoadCasesLevel1)
            {
                ObservableCollection<double> combinaiton1 = [];

                foreach (LoadCombination loadCombination in LoadCombinations)
                {
                    combinaiton1.Add(loadCombination.Beta1 * loadCase1.UpperMassForce + loadCombination.Beta2 * loadCase1.FoundationMassForce);
                }
                // 必要な組合せ数に満たない場合は0で埋める（XAMLバインドエラーを回避するため）
                while (combinaiton1.Count < 4)
                {
                    combinaiton1.Add(0.0);
                }
                MassForceCombinations1.Add(combinaiton1);
            }
            // 必要な行数に満たない場合は空行で埋める（XAMLバインドエラーを回避するため）
            while (MassForceCombinations1.Count < 4)
            {
                MassForceCombinations1.Add([0.0, 0.0, 0.0, 0.0]);
            }

            MassForceCombinations2 = [];
            foreach (LoadCase loadCase2 in LoadCasesLevel2)
            {
                ObservableCollection<double> combinaiton2 = [];

                foreach (LoadCombination loadCombination in LoadCombinations)
                {
                    combinaiton2.Add(loadCombination.Beta1 * loadCase2.UpperMassForce + loadCombination.Beta2 * loadCase2.FoundationMassForce);
                }
                // 必要な組合せ数に満たない場合は0で埋める（XAMLバインドエラーを回避するため）
                while (combinaiton2.Count < 4)
                {
                    combinaiton2.Add(0.0);
                }
                MassForceCombinations2.Add(combinaiton2);
            }
            // 必要な行数に満たない場合は空行で埋める（XAMLバインドエラーを回避するため）
            while (MassForceCombinations2.Count < 4)
            {
                MassForceCombinations2.Add([0.0, 0.0, 0.0, 0.0]);
            }

            OnPropertyChanged(nameof(MassForceCombinations1));
            OnPropertyChanged(nameof(MassForceCombinations1FirstCount));
            OnPropertyChanged(nameof(MassForceCombinations2));
            OnPropertyChanged(nameof(MassForceCombinations2FirstCount));
            OnPropertyChanged(nameof(MassForceCombination1));
            OnPropertyChanged(nameof(MassForceCombination2));

            RefreshDataGrids();
        }

        private void RefreshDataGrids()
        {
            // ここから直接ViewのDataGridを更新
            if (LoadCaseWindowInstance != null && LoadCaseWindowInstance.DataGridLevel1ForceCombination0 != null)
            {
                LoadCaseWindowInstance.DataGridLevel1ForceCombination0.ItemsSource = null;
                LoadCaseWindowInstance.DataGridLevel1ForceCombination0.ItemsSource = MassForceCombinations1;
                LoadCaseWindowInstance.DataGridLevel1ForceCombination0.Items.Refresh();
            }

            if (LoadCaseWindowInstance != null && LoadCaseWindowInstance.DataGridLevel1ForceCombination != null)
            {
                LoadCaseWindowInstance.DataGridLevel1ForceCombination.ItemsSource = null;
                LoadCaseWindowInstance.DataGridLevel1ForceCombination.ItemsSource = MassForceCombinations1;
                LoadCaseWindowInstance.DataGridLevel1ForceCombination.Items.Refresh();
            }

            if (LoadCaseWindowInstance != null && LoadCaseWindowInstance.DataGridLevel2ForceCombination0 != null)
            {
                LoadCaseWindowInstance.DataGridLevel2ForceCombination0.ItemsSource = null;
                LoadCaseWindowInstance.DataGridLevel2ForceCombination0.ItemsSource = MassForceCombinations2;
                LoadCaseWindowInstance.DataGridLevel2ForceCombination0.Items.Refresh();
            }

            if (LoadCaseWindowInstance != null && LoadCaseWindowInstance.DataGridLevel2ForceCombination != null)
            {
                LoadCaseWindowInstance.DataGridLevel2ForceCombination.ItemsSource = null;
                LoadCaseWindowInstance.DataGridLevel2ForceCombination.ItemsSource = MassForceCombinations2;
                LoadCaseWindowInstance.DataGridLevel2ForceCombination.Items.Refresh();
            }
        }


        public void SetAllLoadCases()
        {
            AllLoadCases = []; // AllLoadCases の初期化

            ObservableCollection<LoadCase> loadCases1 = LoadCasesLevel1;
            ObservableCollection<LoadCase> loadCases2 = LoadCasesLevel2;

            AllLoadCases.Add("VL");
            AllLoadCases.Add("VLadd");
            foreach (LoadCase loadCase1 in loadCases1)
            {
                AllLoadCases.Add(loadCase1.LoadName);
            }
            foreach (LoadCase loadCase2 in loadCases2)
            {
                AllLoadCases.Add(loadCase2.LoadName);
            }
        }

        private void ApplyCommonProperty<T>(ObservableCollection<LoadCase> loadCases, Func<LoadCaseCommon, T> commonPropertySelector, Action<LoadCase, T> loadCasePropertySetter)
        {
            if (loadCases == null || loadCases.Count == 0) return;

            var commonValue = commonPropertySelector(loadCases == LoadCasesLevel1 ? LoadCasesLevel1Common[0] : LoadCasesLevel2Common[0]);

            foreach (var loadCase in loadCases)
            {
                loadCasePropertySetter(loadCase, commonValue);
            }

            OnPropertyChanged(loadCases == LoadCasesLevel1 ? nameof(LoadCasesLevel1) : nameof(LoadCasesLevel2));
        }

        [RelayCommand]
        private void LoadCase1CommonSoilNonlinearityMode()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.SoilNonlinearityMode,
                (loadCase, value) => loadCase.SoilNonlinearityMode = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonIsPileNonLinear()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.IsPileNonLinear,
                (loadCase, value) => loadCase.IsPileNonLinear = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonForceActionPointX()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.ForceActionPointX,
                (loadCase, value) => loadCase.ForceActionPointX = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonForceActionPointY()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.ForceActionPointY,
                (loadCase, value) => loadCase.ForceActionPointY = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonForceActionPointZ()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.ForceActionPointAltitude,
                (loadCase, value) => loadCase.ForceActionPointAltitude = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonUpperMassForce()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.UpperMassForce,
                (loadCase, value) => loadCase.UpperMassForce = value
            );
        }

        [RelayCommand]
        private void LoadCase1CommonFoundationMassForce()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.FoundationMassForce,
                (loadCase, value) => loadCase.FoundationMassForce = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonSoilNonlinearityMode()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.SoilNonlinearityMode,
                (loadCase, value) => loadCase.SoilNonlinearityMode = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonIsPileNonLinear()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.IsPileNonLinear,
                (loadCase, value) => loadCase.IsPileNonLinear = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonForceActionPointX()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.ForceActionPointX,
                (loadCase, value) => loadCase.ForceActionPointX = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonForceActionPointY()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.ForceActionPointY,
                (loadCase, value) => loadCase.ForceActionPointY = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonForceActionPointZ()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.ForceActionPointAltitude,
                (loadCase, value) => loadCase.ForceActionPointAltitude = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonUpperMassForce()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.UpperMassForce,
                (loadCase, value) => loadCase.UpperMassForce = value
            );
        }

        [RelayCommand]
        private void LoadCase2CommonFoundationMassForce()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.FoundationMassForce,
                (loadCase, value) => loadCase.FoundationMassForce = value
            );
        }


    }
}


