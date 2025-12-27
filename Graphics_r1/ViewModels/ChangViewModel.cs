using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SkiaSharp;
using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace PileDesign.ViewModels
{
    public partial class ChangViewModel : BaseViewModel
    {
        private readonly InputModel _inputModel;

        public ChangWindow ChangWindowInstance { get; set; } // GroundWindow のインスタンスを保持するプロパティを追加

        // 依存注入コンストラクタ（推奨）
        public ChangViewModel(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            InitializeCommon();
        }

        // 互換用：既存コードが無引数で作っている場合のフォールバック（可能なら呼び出し元を修正してください）
        public ChangViewModel() : this(App.InputModel) { }

        private void InitializeCommon()
        {
            // 初期化：イベント登録
            Changs.CollectionChanged += Changs_CollectionChanged;
            ChangSoilPiles.CollectionChanged += ChangSoilPiles_CollectionChanged;

            // 初期コレクション内の要素に対して PropertyChanged 登録
            foreach (var p in ChangSoilPiles) p.PropertyChanged += ChangSoilPile_PropertyChanged;

            // 起動時にインデックスリストを作成（1..N）
            UpdateChangSoilPileIndices();

            // 既定の Chang を作成して一つ追加する
            var defaultChang = new Chang(
                _EI: 1.0e8,
                _beta: 1000.0,
                _h: 0.0,
                _horizontalLoad: 500.0,
                _ar: 1.0
                );

            // 杭地盤セット番号を 1 に設定（ChangSoilPiles に要素があれば）
            if (ChangSoilPiles != null && ChangSoilPiles.Count >= 1)
            {
                defaultChang.SelectedSoilPileIndex = 1;
                // AssignedSoilPile セッターが EI/Kh0/Beta0 を設定するため、ここで割当てる
                defaultChang.AssignedSoilPile = ChangSoilPiles[0];
            }

            // PropertyChanged 登録とコレクション追加
            defaultChang.PropertyChanged += Chang_PropertyChanged;
            Changs.Add(defaultChang);
        }

        // 追加: SoilPile コレクション（DataGridSoilPile の ItemsSource）
        private ObservableCollection<ChangSoilPile> _changSoilPiles = [new ChangSoilPile()];
        public ObservableCollection<ChangSoilPile> ChangSoilPiles
        {
            get => _changSoilPiles;
            set
            {
                if (_changSoilPiles == value) return;
                if (_changSoilPiles != null)
                {
                    _changSoilPiles.CollectionChanged -= ChangSoilPiles_CollectionChanged;
                    foreach (var p in _changSoilPiles) p.PropertyChanged -= ChangSoilPile_PropertyChanged;
                }
                SetProperty(ref _changSoilPiles, value);
                if (_changSoilPiles != null)
                {
                    _changSoilPiles.CollectionChanged += ChangSoilPiles_CollectionChanged;
                    foreach (var p in _changSoilPiles) p.PropertyChanged += ChangSoilPile_PropertyChanged;
                }
                UpdateChangSoilPileIndices();
            }
        }
        private ObservableCollection<Chang> _changs = [];

        public ObservableCollection<Chang> Changs
        {
            get => _changs;
            set
            {
                if (_changs == value) return;
                if (_changs != null)
                {
                    _changs.CollectionChanged -= Changs_CollectionChanged;
                    foreach (var c in _changs) c.PropertyChanged -= Chang_PropertyChanged;
                }

                SetProperty(ref _changs, value);

                if (_changs != null)
                {
                    _changs.CollectionChanged += Changs_CollectionChanged;
                    foreach (var c in _changs) c.PropertyChanged += Chang_PropertyChanged;
                }
            }
        }

        // 追加: 選択された荷重ケース（LoadCase オブジェクトを格納）
        private LoadCase? _selectedLoadCase;
        public LoadCase? SelectedLoadCase
        {
            get => _selectedLoadCase;
            set => SetProperty(ref _selectedLoadCase, value);
        }

        // 追加: 選択された荷重組合せ
        private LoadCombination? _selectedLoadCombination;
        public LoadCombination? SelectedLoadCombination
        {
            get => _selectedLoadCombination;
            set => SetProperty(ref _selectedLoadCombination, value);
        }

        // 追加: 液状化フラグ
        private bool _isLiquefaction;
        public bool IsLiquefaction
        {
            get => _isLiquefaction;
            set => SetProperty(ref _isLiquefaction, value);
        }

        private double _totalHorizontalLoad = 100;
        public double TotalHorizontalLoad
        {
            get => _totalHorizontalLoad;
            set
            {
                if (_totalHorizontalLoad == value) return;
                _totalHorizontalLoad = value;
                OnPropertyChanged(nameof(TotalHorizontalLoad));
            }
        }

        public static Crosshair MyCrosshair_M { get; private set; }

        private string _crosshairPositionText_M;
        public string CrosshairPositionText_M
        {
            get => _crosshairPositionText_M;
            set => SetProperty(ref _crosshairPositionText_M, value);
        }

        public static Crosshair MyCrosshair_Q { get; private set; }

        private string _crosshairPositionText_Q;
        public string CrosshairPositionText_Q
        {
            get => _crosshairPositionText_Q;
            set => SetProperty(ref _crosshairPositionText_Q, value);
        }

        public static Crosshair MyCrosshair_D { get; private set; }

        private string _crosshairPositionText_D;
        public string CrosshairPositionText_D
        {
            get => _crosshairPositionText_D;
            set => SetProperty(ref _crosshairPositionText_D, value);
        }

        // ChangSoilPiles のコレクション変更ハンドラ
        private void ChangSoilPiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateChangSoilPileIndices();

            if (e.OldItems != null)
            {
                foreach (ChangSoilPile old in e.OldItems.OfType<ChangSoilPile>())
                    old.PropertyChanged -= ChangSoilPile_PropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (ChangSoilPile ni in e.NewItems.OfType<ChangSoilPile>())
                {
                    ni.PropertyChanged += ChangSoilPile_PropertyChanged;
                }
            }

            // コレクション変更時、既存の Chang の選択インデックスに応じて Assigned を更新
            foreach (var chang in Changs)
            {
                ApplySelectedIndexToAssignedChangSoilPile(chang);
            }

            // SoilPile 変更でパラメータが変わる可能性があるためグラフ再描画
            RefreshPlots();
        }


        // ChangSoilPile のプロパティが変わったときのハンドラ
        private void ChangSoilPile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ChangSoilPile pile) return;

            // Kh0 または EI に影響を与えるプロパティの変更を検出したら、
            // その pile を参照している Chang を更新する
            if (e.PropertyName == nameof(ChangSoilPile.Kh0) ||
                e.PropertyName == nameof(ChangSoilPile.EI) ||
                e.PropertyName == nameof(ChangSoilPile.OuterDiameter) ||
                e.PropertyName == nameof(ChangSoilPile.Xi) ||
                e.PropertyName == nameof(ChangSoilPile.E0) ||
                e.PropertyName == nameof(ChangSoilPile.Alpha) ||
                e.PropertyName == nameof(ChangSoilPile.SteelThickness) ||
                e.PropertyName == nameof(ChangSoilPile.Thickness))
            {
                foreach (var chang in Changs)
                {
                    if (ReferenceEquals(chang.AssignedSoilPile, pile))
                    {
                        // 割当られた pile の変更を反映
                        // 安全にチェックして代入（ゼロ除算回避）
                        if (double.IsFinite(pile.EI) && pile.EI > 0.0)
                        {
                            chang.EI = pile.EI;
                        }
                        chang.Kh0 = pile.Kh0;

                        // Beta0 を再計算（外径が m 単位で使われる実装に合わせる）
                        if (pile.OuterDiameter > 0.0 && chang.EI > 0.0)
                        {
                            double d = pile.OuterDiameter / 1000.0; // m
                            try
                            {
                                chang.Beta0 = Math.Pow(chang.Kh0 * d / (4 * chang.EI), 1.0 / 4.0);
                            }
                            catch
                            {
                                // 安全: 非数やゼロ除算は無視
                            }
                        }

                        chang.Update();
                    }
                }
                DrawGraph();
            }
        }

        // 1..N のインデックスを DataGridChang の ComboBox ItemsSource にバインドするためのコレクション
        private ObservableCollection<int> _changSoilPileIndices = [];
        public ObservableCollection<int> ChangSoilPileIndices
        {
            get => _changSoilPileIndices;
            private set => SetProperty(ref _changSoilPileIndices, value);
        }


        // ItemsSource を ViewModel 経由で返すプロパティ（ComboBox の SelectedItem と同一インスタンスを使うため）
        public ObservableCollection<LoadCase> AllSeismicLoadCases
        {
            get
            {
                return _inputModel?.LoadCasesInput?.AllSeismicLoadCases
                    ?? new ObservableCollection<LoadCase>();
            }
        }

        public ObservableCollection<LoadCombination> AllLoadCombinations
        {
            get
            {
                return _inputModel?.LoadCasesInput?.AllLoadCombinations
                    ?? new ObservableCollection<LoadCombination>();
            }
        }

        // ヘルパ：LoadCase を名前から探す
        private LoadCase? ResolveLoadCaseByName(string? name)
        {
            var input = _inputModel;
            if (input?.LoadCasesInput == null || string.IsNullOrEmpty(name)) return null;

            // 特別扱い: VL / VL0 / VLadd は LoadCasesInput の専用プロパティがある
            if (name == "VL0") return input.LoadCasesInput.LoadCaseVL0;
            if (name == "VLadd") return input.LoadCasesInput.LoadCaseVLadd;
            if (name == "VL") return input.LoadCasesInput.LoadCaseVL;

            // それ以外は AllLoadCases の LoadName と比較
            foreach (var lc in input.LoadCasesInput.AllLoadCases)
            {
                if (lc.LoadName == name) return lc;
            }
            return null;
        }

        // ヘルパ：LoadCombination を名前から探す（GetName 形式を想定）
        private LoadCombination? ResolveLoadCombinationByName(string? name)
        {
            var input = _inputModel;
            if (input?.LoadCasesInput?.LoadCombinations == null || string.IsNullOrEmpty(name)) return null;

            foreach (var comb in input.LoadCasesInput.LoadCombinations)
            {
                if (comb.GetName() == name || comb.Name == name) return comb;
            }
            return null;
        }

        // ヘルパ：単一杭の軸力を取得（選択 loadCase に応じて）
        private double GetPileAxialForLoadCase(Models.InputData.PileLayoutDataItem pile, LoadCase lc)
        {
            if (lc == null) return 0.0;

            switch (lc.LoadName)
            {
                case "VL0":
                    return pile.AxialForceVL0;
                case "VLadd":
                    return pile.AxialForceVLAdditional;
                case "VL":
                    return pile.AxialForceVL0 + pile.AxialForceVLAdditional;
            }

            if (lc.Level == 1)
            {
                for (int i = 0; i < _inputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                {
                    if (_inputModel.LoadCasesInput.LoadCasesLevel1[i].LoadName == lc.LoadName)
                        return pile.AxialForceLevel1s[i];
                }
            }
            else if (lc.Level == 2)
            {
                for (int i = 0; i < _inputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                {
                    if (_inputModel.LoadCasesInput.LoadCasesLevel2[i].LoadName == lc.LoadName)
                        return pile.AxialForceLevel2s[i];
                }
            }
            return 0.0;
        }

        // コンストラクタ
        //public ChangViewModel()
        //{
        //    // 初期化：イベント登録
        //    Changs.CollectionChanged += Changs_CollectionChanged;
        //    ChangSoilPiles.CollectionChanged += ChangSoilPiles_CollectionChanged;

        //    // 初期コレクション内の要素に対して PropertyChanged 登録
        //    foreach (var p in ChangSoilPiles) p.PropertyChanged += ChangSoilPile_PropertyChanged;

        //    // 起動時にインデックスリストを作成（1..N）
        //    UpdateChangSoilPileIndices();

        //    // 既定の Chang を作成して一つ追加する
        //    var defaultChang = new Chang(
        //        _EI: 1.0e8,
        //        _beta: 1000.0,
        //        _h: 0.0,
        //        _horizontalLoad: 500.0,
        //        _ar: 1.0
        //        );

        //    // 杭地盤セット番号を 1 に設定（ChangSoilPiles に要素があれば）
        //    if (ChangSoilPiles != null && ChangSoilPiles.Count >= 1)
        //    {
        //        defaultChang.SelectedSoilPileIndex = 1;
        //        // AssignedSoilPile セッターが EI/Kh0/Beta0 を設定するため、ここで割当てる
        //        defaultChang.AssignedSoilPile = ChangSoilPiles[0];
        //    }

        //    // PropertyChanged 登録とコレクション追加
        //    defaultChang.PropertyChanged += Chang_PropertyChanged;
        //    Changs.Add(defaultChang);
        //}

        private void Changs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Chang old in e.OldItems.OfType<Chang>())
                    old.PropertyChanged -= Chang_PropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (Chang ni in e.NewItems.OfType<Chang>())
                {
                    ni.PropertyChanged += Chang_PropertyChanged;
                    // 初期選択を 0 にする（未選択）
                    if (ni.SelectedSoilPileIndex <= 0) ni.SelectedSoilPileIndex = 0;
                }
            }

            // コレクション変更時はグラフを更新
            RefreshPlots();
        }

        private void UpdateChangSoilPileIndices()
        {
            var list = new ObservableCollection<int>();
            for (int i = 1; i <= ChangSoilPiles.Count; i++) list.Add(i);
            ChangSoilPileIndices = list;
        }


        private void Chang_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not Chang chang) return;

            // SelectedSoilPileIndex の変更は割当を更新してグラフ再描画
            if (e.PropertyName == nameof(Chang.SelectedSoilPileIndex))
            {
                ApplySelectedIndexToAssignedChangSoilPile(chang);
                RefreshPlots();
                return;
            }

            // グラフに影響する可能性があるプロパティ群（変更時は再描画）
            var plotRelated = new[]
            {
                nameof(Chang.EI),
                nameof(Chang.Kh0),
                nameof(Chang.Beta0),
                nameof(Chang.Kh),
                nameof(Chang.Beta),
                nameof(Chang.HorizontalLoad),
                nameof(Chang.H),
                nameof(Chang.Ar),
                nameof(Chang.Number),
                nameof(Chang.PileHeadDisplacement),
                nameof(Chang.PileHeadMoment),
                nameof(Chang.MaxBendingMoment),
                nameof(Chang.DepthOfMaxBendingMoment)
    };

            if (e.PropertyName == null || plotRelated.Contains(e.PropertyName))
            {
                RefreshPlots();
            }
        }

        private void ApplySelectedIndexToAssignedChangSoilPile(Chang chang)
        {
            int idx = chang.SelectedSoilPileIndex;
            if (idx >= 1 && idx <= ChangSoilPiles.Count)
            {
                chang.AssignedSoilPile = ChangSoilPiles[idx - 1];
            }
            else
            {
                chang.AssignedSoilPile = null;
            }
        }

        [RelayCommand]
        private void DeleteChang(Chang? item)
        {
            if (item == null) return;
            if (!Changs.Contains(item)) return;

            var action = new CollectionRemoveAction<Chang>(Changs, item);
            action.Redo();
            UndoService.Instance.Push(action);
        }

        [RelayCommand]
        private void DeleteChangSoilPile(ChangSoilPile? item)
        {
            if (item == null) return;
            if (!ChangSoilPiles.Contains(item)) return;

            var action = new CollectionRemoveAction<ChangSoilPile>(ChangSoilPiles, item);
            action.Redo();
            UndoService.Instance.Push(action);
            UpdateChangSoilPileIndices();
        }

        [RelayCommand]
        public void AddChang()
        {
            // 既定値は適宜調整してください
            var newChang = new Chang(
                _EI: 1.0e8,
                _beta: 1000.0,
                _h: 0.0,
                _horizontalLoad: 500.0,
                _ar: 1.0
            );

            newChang.SelectedSoilPileIndex = ChangSoilPiles != null && ChangSoilPiles.Count >= 1 ? 1 : 0;
            if (newChang.SelectedSoilPileIndex >= 1 && ChangSoilPiles.Count >= newChang.SelectedSoilPileIndex)
            {
                newChang.AssignedSoilPile = ChangSoilPiles[newChang.SelectedSoilPileIndex - 1];
            }

            var action = new CollectionAddAction<Chang>(Changs, newChang);
            action.Redo(); // 実行
            UndoService.Instance.Push(action);
        }

        [RelayCommand]
        public void AddChangSoilPile()
        {
            var newChangSoilPile = new ChangSoilPile();
            var action = new CollectionAddAction<ChangSoilPile>(ChangSoilPiles, newChangSoilPile);
            action.Redo();
            UndoService.Instance.Push(action);
            UpdateChangSoilPileIndices(); // インデックス更新
        }

        // 選択用プロパティ（ComboBox にバインド）
        private string? _selectedLoadCaseName;
        public string? SelectedLoadCaseName
        {
            get => _selectedLoadCaseName;
            set => SetProperty(ref _selectedLoadCaseName, value);
        }

        private string? _selectedLoadCombinationName;
        public string? SelectedLoadCombinationName
        {
            get => _selectedLoadCombinationName;
            set => SetProperty(ref _selectedLoadCombinationName, value);
        }

        // 末尾付近、クラス内の適切な位置に追加してください
        // ItemsSource を ViewModel 経由で返すプロパティ（ComboBox の SelectedItem と同一インスタンスを使うため）
        //public ObservableCollection<LoadCase> AllSeismicLoadCases
        //{
        //    get
        //    {
        //        return App.InputModel?.LoadCasesInput?.AllSeismicLoadCases
        //            ?? new ObservableCollection<LoadCase>();
        //    }
        //}

        //public ObservableCollection<LoadCombination> AllLoadCombinations
        //{
        //    get
        //    {
        //        // LoadCasesInput.AllLoadCombinations は AllLoadCombinations プロパティ名に合わせる
        //        return App.InputModel?.LoadCasesInput?.AllLoadCombinations
        //            ?? new ObservableCollection<LoadCombination>();
        //    }
        //}


        // 集計プロパティ（UI で参照可能）
        private double _sumAxialForSelectedLoadCase;
        public double SumAxialForSelectedLoadCase
        {
            get => _sumAxialForSelectedLoadCase;
            private set => SetProperty(ref _sumAxialForSelectedLoadCase, value);
        }

        // ヘルパ：LoadCase を名前から探す
        //private LoadCase? ResolveLoadCaseByName(string? name)
        //{
        //    var input = App.InputModel;
        //    if (input?.LoadCasesInput == null || string.IsNullOrEmpty(name)) return null;

        //    // 特別扱い: VL / VL0 / VLadd は LoadCasesInput の専用プロパティがある
        //    if (name == "VL0") return input.LoadCasesInput.LoadCaseVL0;
        //    if (name == "VLadd") return input.LoadCasesInput.LoadCaseVLadd;
        //    if (name == "VL") return input.LoadCasesInput.LoadCaseVL;

        //    // それ以外は AllLoadCases の LoadName と比較
        //    foreach (var lc in input.LoadCasesInput.AllLoadCases)
        //    {
        //        if (lc.LoadName == name) return lc;
        //    }
        //    return null;
        //}

        //// ヘルパ：LoadCombination を名前から探す（GetName 形式を想定）
        //private LoadCombination? ResolveLoadCombinationByName(string? name)
        //{
        //    var input = App.InputModel;
        //    if (input?.LoadCasesInput?.LoadCombinations == null || string.IsNullOrEmpty(name)) return null;

        //    foreach (var comb in input.LoadCasesInput.LoadCombinations)
        //    {
        //        if (comb.GetName() == name || comb.Name == name) return comb;
        //    }
        //    return null;
        //}

        //// ヘルパ：単一杭の軸力を取得（選択 loadCase に応じて）
        //private double GetPileAxialForLoadCase(Models.InputData.PileLayoutDataItem pile, LoadCase lc)
        //{
        //    if (lc == null) return 0.0;

        //    switch (lc.LoadName)
        //    {
        //        case "VL0":
        //            return pile.AxialForceVL0;
        //        case "VLadd":
        //            return pile.AxialForceVLAdditional;
        //        case "VL":
        //            return pile.AxialForceVL0 + pile.AxialForceVLAdditional;
        //    }

        //    if (lc.Level == 1)
        //    {
        //        for (int i = 0; i < App.InputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
        //        {
        //            if (App.InputModel.LoadCasesInput.LoadCasesLevel1[i].LoadName == lc.LoadName)
        //                return pile.AxialForceLevel1s[i];
        //        }
        //    }
        //    else if (lc.Level == 2)
        //    {
        //        for (int i = 0; i < App.InputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
        //        {
        //            if (App.InputModel.LoadCasesInput.LoadCasesLevel2[i].LoadName == lc.LoadName)
        //                return pile.AxialForceLevel2s[i];
        //        }
        //    }
        //    return 0.0;
        //}

        /// <summary>
        /// App.InputModel に定義された PileBody / PileSection 情報を
        /// Chang 用の ChangSoilPiles コレクションへ反映します。
        /// - 各 PileBody の代表セグメント（先頭セグメント）から外径等を読み取り、
        ///   ChangSoilPile の対応するプロパティへマッピングします。
        /// - マッピング可能な値が無い場合は既定値を残します。
        /// </summary>
        [RelayCommand]
        public void ApplyInputModel()
        {
            var input = _inputModel;
            if (input == null)
            {
                MessageBox.Show("InputModel が見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (input.PileBodies == null || input.PileBodies.Count == 0)
            {
                MessageBox.Show("入力に杭体情報が含まれていません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // --- ChangSoilPiles を PileBodies に合わせて構築 ---
            ChangSoilPiles.Clear();

            for (int i = 0; i < input.PileBodies.Count; i++)
            {
                var pb = input.PileBodies[i];
                var seg = pb.PileBodySegments?.FirstOrDefault();
                var ps = seg?.PileSection;

                var cs = new ChangSoilPile();

                if (ps != null && ps.PileDiameter > 0.0)
                    cs.OuterDiameter = ps.PileDiameter;

                if (ps != null)
                {
                    cs.EI = ps.EI;
                    cs.IsHollow = ps.PipeDia > 0.0 || ps.PipeTs > 0.0;
                    if (ps.ConcreteThickness > 0.0) cs.Thickness = ps.ConcreteThickness;
                    if (ps.PipeTs > 0.0) cs.SteelThickness = ps.PipeTs;
                    if (ps.ConcreteFc > 0.0) cs.Fc = ps.ConcreteFc;
                    if (ps.ConcreteGamma > 0.0) cs.Gamma = ps.ConcreteGamma;
                }

                // 修正: PileTop は PileBodyInput 内の PileTop を使う
                var pileTop = pb?.PileTop;
                cs.ApplyPileTop(pileTop);

                ChangSoilPiles.Add(cs);
            }

            // インデックスを更新して、既存の Chang があれば割当処理を実行
            UpdateChangSoilPileIndices();

            // --- 選択された荷重情報（オブジェクト優先、名前でフォールバック） ---
            var selectedLC = SelectedLoadCase ?? ResolveLoadCaseByName(SelectedLoadCaseName);
            var selectedComb = SelectedLoadCombination ?? ResolveLoadCombinationByName(SelectedLoadCombinationName);

            // --- Changs を杭配置（PileLayoutItems）に合わせて作成 ---
            Changs.Clear();
            if (input.PileLayoutItems != null && input.PileLayoutItems.Count > 0)
            {
                foreach (var pile in input.PileLayoutItems)
                {
                    var newChang = new Chang(
                        _EI: 1.0e8,
                        _beta: 1000.0,
                        _h: 0.0,
                        _horizontalLoad: 0.0,
                        _ar: 1.0
                    );

                    newChang.Number = 1;

                    if (selectedLC != null)
                    {
                        newChang.AxialForce = GetPileAxialForLoadCase(pile, selectedLC);
                    }
                    else
                    {
                        newChang.AxialForce = 0.0;
                    }

                    if (ChangSoilPiles != null && ChangSoilPiles.Count >= 1)
                    {
                        newChang.SelectedSoilPileIndex = 1;
                        newChang.AssignedSoilPile = ChangSoilPiles[0];
                    }
                    else
                    {
                        newChang.SelectedSoilPileIndex = 0;
                        newChang.AssignedSoilPile = null;
                    }

                    newChang.PropertyChanged += Chang_PropertyChanged;
                    Changs.Add(newChang);
                }
            }

            // --- 全体水平力（組合せ・荷重ケースが選択されていれば）を算出して各 Chang に按分 ---
            if (selectedLC != null && selectedComb != null && Changs.Count > 0)
            {
                double force = selectedLC.UpperMassForce * selectedComb.Beta1 + selectedLC.FoundationMassForce * selectedComb.Beta2;
                double angleRad = selectedLC.LoadAngle * Math.PI / 180.0;
                double forceX = force * Math.Cos(angleRad);
                TotalHorizontalLoad = Math.Abs(forceX);

                double totalPilesCount = Changs.Sum(c => (double)Math.Max(1, c.Number));
                if (totalPilesCount <= 0) totalPilesCount = 1.0;

                foreach (var chang in Changs)
                {
                    double share = (double)Math.Max(1, chang.Number) / totalPilesCount;
                    chang.HorizontalLoad = TotalHorizontalLoad * share;
                    chang.Update();
                }
            }

            // --- 選択荷重ケースに対応する杭軸力合計を算出 ---
            double sumAxial = 0.0;
            if (selectedLC != null && input.PileLayoutItems != null)
            {
                foreach (var pile in input.PileLayoutItems)
                {
                    sumAxial += GetPileAxialForLoadCase(pile, selectedLC);
                }
            }
            SumAxialForSelectedLoadCase = sumAxial;

            RefreshPlots();

            MessageBox.Show("InputModel を適用しました。\n" +
                (selectedLC != null && selectedComb != null
                    ? $"選択荷重: {selectedLC.LoadName} / 組合せ: {selectedComb.GetName()}\n全体水平力 (X 方向): {TotalHorizontalLoad:N0} kN\n"
                    : "荷重ケース／組合せが未選択のため水平力の設定は行っていません。\n") +
                (selectedLC != null ? $"選択荷重の杭軸力合計: {SumAxialForSelectedLoadCase:N0} kN" : ""),
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        [RelayCommand]
        public void Analysis()
        {
            // Changs が空なら中止
            if (Changs == null || Changs.Count == 0)
            {
                MessageBox.Show(
                    "解析対象の Chang が1件も存在しません。解析を中止します。",
                    "計算中止",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 前処理: Kh0 または Beta0 が 0/非数 の Chang を検出して中止する
            var invalids = Changs
                .Select((c, i) => new { Chang = c, Index = i })
                .Where(x =>
                    !double.IsFinite(x.Chang.Kh0) || x.Chang.Kh0 <= 0.0 ||
                    !double.IsFinite(x.Chang.Beta0) || x.Chang.Beta0 <= 0.0)
                .ToList();

            if (invalids.Count != 0)
            {
                var sb = new StringBuilder();
                foreach (var it in invalids)
                {
                    sb.AppendLine($"行 {it.Index + 1}: Kh0 = {it.Chang.Kh0:G6}, β0 = {it.Chang.Beta0:G6}");
                }

                MessageBox.Show(
                    $"以下の Chang で Kh0 または β0 が 0 または無効です。計算を中止します。\n\n{sb}",
                    "計算中止",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // 登録済みのチェックをパスしたら通常の解析を行う
            double pileTopDisplacement = 0.0; // 杭頭変位
            const double deltaPileTopDisplacement = 0.00001;
            double computedTotalHorizontalForce = 0.0;
            double computedTotalHorizontalForcePlus = 0.0;

            while (TotalHorizontalLoad - computedTotalHorizontalForce > 0.0000001)
            {
                computedTotalHorizontalForce = 0.0;
                computedTotalHorizontalForcePlus = 0.0;

                foreach (var chang in Changs)
                {
                    double pileTopForce = chang.GetHorizontalForce(pileTopDisplacement);
                    double pileTopForcePlusDelta = chang.GetHorizontalForce(pileTopDisplacement + deltaPileTopDisplacement);
                    computedTotalHorizontalForce += pileTopForce * chang.Number;
                    computedTotalHorizontalForcePlus += pileTopForcePlusDelta * chang.Number;
                }
                double stiffness = (computedTotalHorizontalForcePlus - computedTotalHorizontalForce) / deltaPileTopDisplacement;

                // 安全チェック: stiffness が不正なら中止
                if (!double.IsFinite(stiffness) || Math.Abs(stiffness) < 1e-12)
                {
                    MessageBox.Show("剛性が不正のため解析を中止します。", "計算エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                pileTopDisplacement += (TotalHorizontalLoad - computedTotalHorizontalForce) / stiffness;
            }

            // 各 Chang の結果を更新
            foreach (var chang in Changs)
            {
                chang.HorizontalLoad = chang.GetHorizontalForce(pileTopDisplacement);
                chang.Update();
                
            }
            DrawGraph();
        }


        // 追加フィールド（クラス内、既存フィールド群の近くに置いてください）
        private bool _hookedMouseMoveM = false;
        private bool _hookedMouseMoveQ = false;
        private bool _hookedMouseMoveD = false;


        // 既存クラス内に追加
        public void RefreshPlots()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(DrawGraph), DispatcherPriority.Background);
                }
                else
                {
                    DrawGraph();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshPlots failed: {ex}");
            }
            //try
            //{
            //    // UI スレッドに描画処理を委譲（AvalonDock 下でも安全）
            //    var disp = System.Windows.Application.Current?.Dispatcher;
            //    if (disp != null && !disp.CheckAccess())
            //    {
            //        disp.BeginInvoke(new Action(() => DrawGraph()), System.Windows.Threading.DispatcherPriority.Background);
            //    }
            //    else
            //    {
            //        DrawGraph();
            //    }
            //}
            //catch
            //{
            //    // 何らかの理由で Dispatcher が使えない場合は直接呼ぶ（保険）
            //    DrawGraph();
            //}
        }

        // 既存 DrawGraph を統一版に差し替え
        private void DrawGraph()
        {
            int n = 20;
            // M: 曲げモーメント
            if (ChangWindowInstance?.wpfPlotM is WpfPlot plotM)
                DrawSeries(
                    n: n,
                    valueSelector: (chang, x) => chang.GetBendingMoment(x),
                    targetPlot: plotM,
                    title: "曲げモーメント",
                    xLabel: "曲げモーメント (kNm)",
                    yLabel: "GL基準深さ(m)",
                    crosshairPropName: nameof(CrosshairPositionText_M),
                    hookFlagSetter: () => _hookedMouseMoveM,
                    setHookedFlag: () => _hookedMouseMoveM = true);

            // Q: せん断力
            if (ChangWindowInstance?.wpfPlotQ is WpfPlot plotQ)
                DrawSeries(
                    n: n,
                    valueSelector: (chang, x) => chang.GetShearForce(x),
                    targetPlot: plotQ,
                    title: "せん断力",
                    xLabel: "せん断力 (kN)",
                    yLabel: "GL基準深さ(m)",
                    crosshairPropName: nameof(CrosshairPositionText_Q),
                    hookFlagSetter: () => _hookedMouseMoveQ,
                    setHookedFlag: () => _hookedMouseMoveQ = true);

            // D: 変位
            if (ChangWindowInstance?.wpfPlotD is WpfPlot plotD)
                DrawSeries(
                    n: n,
                    valueSelector: (chang, x) => chang.GetDeflection(x),
                    targetPlot: plotD,
                    title: "変位",
                    xLabel: "変位 (m)",
                    yLabel: "GL基準深さ(m)",
                    crosshairPropName: nameof(CrosshairPositionText_D),
                    hookFlagSetter: () => _hookedMouseMoveD,
                    setHookedFlag: () => _hookedMouseMoveD = true);
        }


        // 汎用描画メソッド
        private void DrawSeries(int n,
            Func<Chang, double, double> valueSelector,
            WpfPlot targetPlot,
            string title,
            string xLabel,
            string yLabel,
            string crosshairPropName,
            Func<bool> hookFlagSetter,
            Action setHookedFlag)
        {
            if (ChangWindowInstance == null || targetPlot == null) return;
            if (n <= 0) n = 100;

            // y軸データ
            double yStart = 0.0;
            double yStep = 0.1; // 任意の間隔
            double[] ys = Enumerable.Range(0, (int)(n / yStep))
                                    .Select(i => yStart + i * yStep)
                                    .ToArray();

            // 各 Chang ごとの y 値配列を作る
            var seriesList = new List<double[]>();
            foreach (var chang in Changs)
            {
                var xs = ys.Select(y => valueSelector(chang, y)).ToArray();
                seriesList.Add(xs);
            }

            // UI スレッドでプロット操作を実行（AvalonDock 内でも安全）
            targetPlot.Dispatcher.Invoke(() =>
            {
                // プロット初期化（全消去）
                targetPlot.Plot.Clear();

                // 重要：クロスヘアは Clear() で消えるため、必ず再初期化する
                if (crosshairPropName == nameof(CrosshairPositionText_M))
                    MyCrosshair_M = PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
                else if (crosshairPropName == nameof(CrosshairPositionText_Q))
                    MyCrosshair_Q = PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
                else if (crosshairPropName == nameof(CrosshairPositionText_D))
                    MyCrosshair_D = PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

                // 各 Chang を系列としてプロット（凡例に番号を設定）
                int idx = 0;
                foreach (var chang in Changs)
                {
                    var xs = seriesList[idx];
                    if (xs == null || xs.Length == 0 || xs.Length != ys.Length)
                    {
                        idx++;
                        continue;
                    }

                    var scatter = targetPlot.Plot.Add.Scatter(xs, ys);
                    scatter.LegendText = $"{idx + 1}";
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 0;
                    idx++;
                }

                // 軸ラベル・タイトル
                targetPlot.Plot.Title(title);
                targetPlot.Plot.XLabel(xLabel);
                targetPlot.Plot.YLabel(yLabel);

                targetPlot.Plot.Axes.AutoScale();
                targetPlot.Plot.Axes.AutoScaleExpandX();
                targetPlot.Plot.Axes.AutoScaleExpandY();

                targetPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);
                targetPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
                targetPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

                // 自動スケールして Y 軸を反転（上が 0、下が正）
                targetPlot.Plot.Axes.InvertY();

                // MouseMove 登録（必要なら一度だけ）
                if (!hookFlagSetter())
                {
                    targetPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, crosshairPropName, xLabel, yLabel, 1, 3);
                    setHookedFlag();
                }

                targetPlot.Refresh();
            });
        }
        //    // プロット初期化
        //    targetPlot.Plot.Clear();

        //    // 各 Chang を系列としてプロット（凡例に番号を設定）
        //    int idx = 0;
        //    foreach (var chang in Changs)
        //    {
        //        var xs = ys.Select(y => valueSelector(chang, y)).ToArray();
        //        // ラベルに Chang の番号（1-based）を入れる
        //        var scatter = targetPlot.Plot.Add.Scatter(xs, ys);
        //        scatter.LegendText = $"{idx + 1}";
        //        scatter.LineWidth = 2;
        //        scatter.MarkerSize = 0;
        //        idx++;
        //    }

        //    // 軸ラベル・タイトル
        //    targetPlot.Plot.Title(title);

        //    targetPlot.Plot.XLabel(xLabel);
        //    targetPlot.Plot.YLabel(yLabel);

        //    targetPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);
        //    targetPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
        //    targetPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

        //    targetPlot.Plot.Axes.AutoScale();
        //    targetPlot.Plot.Axes.AutoScaleExpandX();
        //    targetPlot.Plot.Axes.AutoScaleExpandY();

        //    // 自動スケールして Y 軸を反転（上が 0、下が正）
        //    targetPlot.Plot.Axes.InvertY();

        //    // クロスヘア初期化とマウスムーブ登録（重複登録防止）
        //    // --- 追加: targetPlot の DataContext を確実に ViewModel にする（PlotHelper が反射で参照するため）
        //    //try
        //    //{
        //    //    // ViewModel を WpfPlot の DataContext に明示設定（継承が効かないケース対処）
        //    //    targetPlot.DataContext = this;
        //    //}
        //    //catch
        //    //{
        //    //    // 何らかの理由で設定できない場合は無視（デバッグで確認）
        //    //}

        //    if (crosshairPropName == nameof(CrosshairPositionText_M))
        //    {
        //        MyCrosshair_M ??= PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
        //    }
        //    else if (crosshairPropName == nameof(CrosshairPositionText_Q))
        //    {
        //        MyCrosshair_Q ??= PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
        //    }
        //    else if (crosshairPropName == nameof(CrosshairPositionText_D))
        //    {
        //        MyCrosshair_D ??= PlotHelper.InitCrosshair(targetPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
        //    }

        //    if (!hookFlagSetter())
        //    {
        //        // 登録前に DataContext を確認するデバッグログを入れると原因把握しやすいです
        //        // Debug.WriteLine($"Register MouseMove: DataContext={targetPlot.DataContext?.GetType().FullName}, prop={crosshairPropName}");
        //        targetPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, crosshairPropName, xLabel, yLabel, 1, 3);
        //        setHookedFlag();
        //    }

        //    targetPlot.Refresh();
        //}
    }
}