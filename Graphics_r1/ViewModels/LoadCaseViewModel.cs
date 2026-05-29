using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace PileDesign.ViewModels

{
    public partial class LoadCaseViewModel : BaseViewModel
    {
        private readonly UndoManager _undoManager = new();
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

            LoadCombinations = GetCombinations(_loadCombinationFactor);
            LoadCombinationsPlus = GetCombinationsPlus(_loadCombinationFactor);

            SetAllLoadCases();
            SetTotalMassForces(); // 初期データの設定

            SubscribeLoadCasePropertyChanged();
        }

        // 状態を保存
        public void PushUndoState()
        {
            var currentState = new LoadCaseState
            {
                LoadCasesLevel1 = new ObservableCollection<LoadCase>(LoadCasesLevel1.Select(x => x.DeepCopy())),
                LoadCasesLevel2 = new ObservableCollection<LoadCase>(LoadCasesLevel2.Select(x => x.DeepCopy())),
                LoadCombinations = new ObservableCollection<LoadCombination>(LoadCombinations.Select(x => x.DeepCopy())),
                LoadCombinationsPlus = new ObservableCollection<LoadCombination>(LoadCombinationsPlus.Select(x => x.DeepCopy())),
                LoadCombinationFactor = LoadCombinationFactor,
                LoadCasesLevel1Common = new ObservableCollection<LoadCaseCommon>(LoadCasesLevel1Common.Select(x => x.DeepCopy())),
                LoadCasesLevel2Common = new ObservableCollection<LoadCaseCommon>(LoadCasesLevel2Common.Select(x => x.DeepCopy()))
            };
            _undoManager.SaveState(currentState);
        }

        // 状態を復元
        private void RestoreState(LoadCaseState state)
        {
            LoadCasesLevel1 = new ObservableCollection<LoadCase>(state.LoadCasesLevel1.Select(x => x.DeepCopy()));
            LoadCasesLevel2 = new ObservableCollection<LoadCase>(state.LoadCasesLevel2.Select(x => x.DeepCopy()));
            LoadCombinations = new ObservableCollection<LoadCombination>(state.LoadCombinations.Select(x => x.DeepCopy()));
            LoadCombinationsPlus = new ObservableCollection<LoadCombination>(state.LoadCombinationsPlus.Select(x => x.DeepCopy()));
            LoadCombinationFactor = state.LoadCombinationFactor;
            LoadCasesLevel1Common = new ObservableCollection<LoadCaseCommon>(state.LoadCasesLevel1Common.Select(x => x.DeepCopy()));
            LoadCasesLevel2Common = new ObservableCollection<LoadCaseCommon>(state.LoadCasesLevel2Common.Select(x => x.DeepCopy()));

            SetTotalMassForces();
            OnPropertyChanged(nameof(LoadCasesLevel1));
            OnPropertyChanged(nameof(LoadCasesLevel1Common));
            OnPropertyChanged(nameof(LoadCasesLevel2));
            OnPropertyChanged(nameof(LoadCasesLevel2Common));
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

        partial void OnLoadCombinationFactorChanged(double value)
        {
            PushUndoState();
            LoadCombinationFactor = value;
            LoadCombinations = GetCombinations(value);
            LoadCombinationsPlus = GetCombinationsPlus(value);
            SetTotalMassForces();
            // 依存プロパティの更新通知を追加
            OnPropertyChanged(nameof(LoadCombinations)); // 追加
            RefreshDataGrids();
        }


        [RelayCommand]
        private void OnOk()
        {
            // 荷重条件の変更はジオメトリ (メッシュ) に影響しないため、杭要素分割は保持する。
            // 解析結果のみリセットする (旧 CheckAndResetElementSplit は分割も破棄していた)。
            if (!_mainWindowViewModel.CheckAndResetAnalysisResultsKeepingSplit("荷重条件"))
                return;

            InputModel.LoadCasesInput.LoadCombinationFactor = LoadCombinationFactor;
            InputModel.LoadCasesInput.LoadCombinations = new ObservableCollection<LoadCombination>(LoadCombinations);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            InputModel.LoadCasesInput = PrevLoadCasesInput.DeepCopy();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void SliderValueChanged()
        {
            LoadCombinations = GetCombinations(LoadCombinationFactor);
            LoadCombinationsPlus = GetCombinationsPlus(LoadCombinationFactor);
            SetTotalMassForces();
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
        private void LoadCase1CommonIsSoilNonLinear()
        {
            ApplyCommonProperty(
                LoadCasesLevel1,
                common => common.IsSoilNonLinear,
                (loadCase, value) => loadCase.IsSoilNonLinear = value
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
        private void LoadCase2CommonIsSoilNonLinear()
        {
            ApplyCommonProperty(
                LoadCasesLevel2,
                common => common.IsSoilNonLinear,
                (loadCase, value) => loadCase.IsSoilNonLinear = value
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


