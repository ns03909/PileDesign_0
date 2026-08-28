
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PileDesign.Services;


namespace PileDesign.ViewModels
{
    public partial class ElementDivisionViewModel : ObservableObject, ICloseable
    {
        const double epsilon = 1e-5; // 許容誤差（必要に応じて調整）

        private readonly UndoManager _undoManager = new();

        private bool _suppressUndoSave = false;
        public ElementDivisionWindow ElementDivisionWindowInstance { get; set; }
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private ObservableCollection<SoilPile> _soilPiles;

        [ObservableProperty]
        private ObservableCollection<int> _soilPileNumberOption;

        [ObservableProperty]
        private SoilEmbedment _soilEmbedment;

        [ObservableProperty]
        private double _firstDistance;

        [ObservableProperty]
        private double _maxPileSpacing;

        [ObservableProperty]
        private double _maxEmbedmentSpacing;


        public ObservableCollection<PileDesign.Models.LampState> SoilPileLampStates { get; } = new();
        public ObservableCollection<PileDesign.Models.LampState> EmbedmentLampStates { get; } = new();


        private int _selectedSoilPileNo = 1;
        public int SelectedSoilPileNo
        {
            get => _selectedSoilPileNo;
            set
            {
                _previousSelectedSoilPileNo = _selectedSoilPileNo;
                _selectedSoilPileNo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviousSelectedSoilPileNo));
                UpdateSelectedSoilPileProperties();
            }
        }

        private int _previousSelectedSoilPileNo;
        public int PreviousSelectedSoilPileNo
        {
            get => _previousSelectedSoilPileNo;
            set => SetProperty(ref _previousSelectedSoilPileNo, value);
        }

        [ObservableProperty]
        private int _pileGroundNo;

        [ObservableProperty]
        private int _pileBodyNo;

        [ObservableProperty]
        private double _z;

        [ObservableProperty]
        private double _pileBottomAltitude;

        [ObservableProperty]
        private DoatsuGoryokuBane _doatsuGoryokuBane;

        private DoatsuGoryokuBaneItem _selectedDoatsuGoryokuBaneItem;
        public DoatsuGoryokuBaneItem SelectedDoatsuGoryokuBaneItem
        {
            get => _selectedDoatsuGoryokuBaneItem;
            set
            {
                SetProperty(ref _selectedDoatsuGoryokuBaneItem, value);
                DrawDoatsuGoryokuBaneGraph();
            }
        }

        private ObservableCollection<HorizontalSoilReactionItem> _selectedHorizontalSoilReactions = [];
        public ObservableCollection<HorizontalSoilReactionItem> SelectedHorizontalSoilReactions
        {
            get => _selectedHorizontalSoilReactions;
            set => SetProperty(ref _selectedHorizontalSoilReactions, value);
        }

        // 選択中の杭のzs
        private ObservableCollection<PileZDataItem> _selectedZDataItems = [];
        public ObservableCollection<PileZDataItem> SelectedZDataItems
        {
            get => _selectedZDataItems;
            set => SetProperty(ref _selectedZDataItems, value);
        }

        private ZDataItem _selectedZDataItem;
        public ZDataItem SelectedZDataItem
        {
            get => _selectedZDataItem;
            set
            {
                SetProperty(ref _selectedZDataItem, value);
                DrawShapes();
            }
        }

        // 杭のデフォルトzs
        //private ObservableCollection<ObservableCollection<PileZDataItem>> _zsOriginalCollection = [];
        //public ObservableCollection<ObservableCollection<PileZDataItem>> ZsOriginalCollection
        //{
        //    get => _zsOriginalCollection;
        //    set => SetProperty(ref _zsOriginalCollection, value);
        //}

        public List<string> DirectionOption { get; set; } = ["X", "Y"];

        private string _selectedDirection = "X";
        public string SelectedDirection
        {
            get => _selectedDirection;
            set
            {
                SetProperty(ref _selectedDirection, value);
                DrawSoilEmbedment();
            }
        }

        // 杭のzs
        private ObservableCollection<ObservableCollection<PileZDataItem>> _zsCollection = [];
        public ObservableCollection<ObservableCollection<PileZDataItem>> ZsCollection
        {
            get => _zsCollection;
            set => SetProperty(ref _zsCollection, value);
        }

        // 根入れ部のデフォルトZs
        private ObservableCollection<EmbedmentZDataItem> _embedmentZsOriginalCollection = [];
        public ObservableCollection<EmbedmentZDataItem> EmbedmentZsOriginalCollection
        {
            get => _embedmentZsOriginalCollection;
            set => SetProperty(ref _embedmentZsOriginalCollection, value);
        }

        // 根入れ部のZs
        private ObservableCollection<EmbedmentZDataItem> _embedmentZsCollection = [];
        public ObservableCollection<EmbedmentZDataItem> EmbedmentZsCollection
        {
            get => _embedmentZsCollection;
            set => SetProperty(ref _embedmentZsCollection, value);
        }

        // 選択中の根入れ部のZs
        private EmbedmentZDataItem _selectedEmbedmentZ;
        public EmbedmentZDataItem SelectedEmbedmentZ
        {
            get => _selectedEmbedmentZ;
            set
            {
                SetProperty(ref _selectedEmbedmentZ, value);
                DrawSoilEmbedment();
            }
        }

        // 表示用群杭係数
        private double _xi = 1;
        public double Xi
        {
            get => _xi;
            set
            {
                if (value > 0 && value <= 1)
                {
                    SetProperty(ref _xi, value);
                }
                else
                {
                    // バリデーションエラーの処理
                    MessageService.Show("Xiの値は0より大きく、1以下でなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 表示用R/B
        private double _rOnB = 10;
        public double ROnB
        {
            get => _rOnB;
            set
            {
                if (value > 0)
                {
                    SetProperty(ref _rOnB, value);
                }
                else
                {
                    // バリデーションエラーの処理
                    MessageService.Show("R/Bの値は0より大きくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        // 土圧合力ばねタブの表示非表示
        public bool IsDoatsuGoryokuBaneVisible
        {
            //get => DoatsuGoryokuBane != null && DoatsuGoryokuBane.Items.Count > 0;
            get => InputModel.EmbedmentInput.EmbedmentLayersCount != 0;
        }

        // 赤ランプが1つでもあるかどうか（IsOn == false が赤）
        public bool HasAnyRedLamp
        {
            get
            {
                bool hasSoilPileRed = SoilPileLampStates != null && SoilPileLampStates.Any(lamp => !lamp.IsOn);
                bool hasEmbedmentRed = IsDoatsuGoryokuBaneVisible && EmbedmentLampStates != null && EmbedmentLampStates.Any(lamp => !lamp.IsOn);
                return hasSoilPileRed || hasEmbedmentRed;
            }
        }

        // 警告メッセージ
        public string WarningMessage
        {
            get
            {
                if (IsDoatsuGoryokuBaneVisible)
                {
                    return "すべての土層-杭セット、土層-根入れセットの杭要素分割を確認してください。";
                }
                else
                {
                    return "すべての土層-杭セットの杭要素分割を確認してください。";
                }
            }
        }

        public int SoilLayerPileSetCount => _mainWindowViewModel.CurrentInputModel?.ElementDivision?.SoilPiles?.Count ?? 0;



        private void SoilPiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(SoilLayerPileSetCount));
        }

        public static Crosshair MyCrosshair_kh0 { get; private set; }

        private string _crosshairPositionText_kh0;
        public string CrosshairPositionText_kh0
        {
            get => _crosshairPositionText_kh0;
            set => SetProperty(ref _crosshairPositionText_kh0, value);
        }

        public static Crosshair MyCrosshair_Front { get; private set; }

        private string _crosshairPositionText_Front;
        public string CrosshairPositionText_Front
        {
            get => _crosshairPositionText_Front;
            set => SetProperty(ref _crosshairPositionText_Front, value);
        }

        public static Crosshair MyCrosshair_Rear { get; private set; }

        private string _crosshairPositionText_Rear;
        public string CrosshairPositionText_Rear
        {
            get => _crosshairPositionText_Rear;
            set => SetProperty(ref _crosshairPositionText_Rear, value);
        }

        public static Crosshair MyCrosshair_SoilPressureProfile { get; private set; }

        private string _crosshairPositionText_SoilPressureProfile;
        public string CrosshairPositionText_SoilPressureProfile
        {
            get => _crosshairPositionText_SoilPressureProfile;
            set => SetProperty(ref _crosshairPositionText_SoilPressureProfile, value);
        }

        public static Crosshair MyCrosshair_DGperM { get; private set; }

        private string _crosshairPositionText_DGperM;
        public string CrosshairPositionText_DGperM
        {
            get => _crosshairPositionText_DGperM;
            set => SetProperty(ref _crosshairPositionText_DGperM, value);
        }

        public static Crosshair MyCrosshair_DG { get; private set; }

        private string _crosshairPositionText_DG;
        public string CrosshairPositionText_DG
        {
            get => _crosshairPositionText_DG;
            set => SetProperty(ref _crosshairPositionText_DG, value);
        }

        // DataGridで選択しているデータ
        //public PileZDataItem SelectedZonDataGrids { get; set; }
        // DataGridで選択しているデータ

        private HorizontalSoilReactionItem _selectedLayeronDataGrid;
        public HorizontalSoilReactionItem SelectedLayeronDataGrid
        {
            get => _selectedLayeronDataGrid;
            set
            {
                SetProperty(ref _selectedLayeronDataGrid, value);
                DrawHorizontalSoilReacitonGraph();
            }
        }

        // xamlフィールド
        public Canvas Canvas { get; set; }
        public Canvas CanvasEmbedment { get; set; }

        // Undo/Redoコマンド

        //public IRelayCommand UndoCommand { get; }
        //public IRelayCommand RedoCommand { get; }

        //private ICommand _resetPileCommand;
        //public ICommand ResetPileCommand
        //{
        //    get
        //    {
        //        _resetPileCommand ??= new RelayCommand(() => this.ResetPile());
        //        return _resetPileCommand;
        //    }
        //}

        //private void ResetPile()
        //{
        //    // IsChangeableがtrueの行を削除
        //    var itemsToRemove = SelectedZDataItems.Where(z => z.IsChangeable).ToList();
        //    foreach (var item in itemsToRemove)
        //    {
        //        SelectedZDataItems.Remove(item);
        //    }

        //    OnZDataItemsChanged();
        //}

        [RelayCommand]
        private void ResetPile()
        {
            // IsChangeableがtrueの行を削除
            var itemsToRemove = SelectedZDataItems.Where(z => z.IsChangeable).ToList();
            foreach (var item in itemsToRemove)
            {
                SelectedZDataItems.Remove(item);
            }

            OnZDataItemsChanged();
        }

        // 杭追加メソッド
        [RelayCommand]
        private void AddZs()
        {
            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());

            for (int i = 0; i < SelectedZDataItems.Count - 1; i++)
            {
                if (SelectedZDataItem == SelectedZDataItems[i])
                {
                    double upperValue = SelectedZDataItems[i].Z;
                    double lowerValue = SelectedZDataItems[i + 1].Z;
                    double averageValue = (upperValue + lowerValue) * 0.5;

                    PileZDataItem newItem = new()
                    {
                        Z = averageValue,
                        IsChangeable = true,
                        //GroundLayerNo = PileGroundNo,
                        //GroundInput = InputModel.GroundsInput[PileGroundNo - 1],
                        GroundInput = SoilPiles[SelectedSoilPileNo - 1].GroundInput,
                    };

                    // 選択された行の下に新しい行を追加
                    SelectedZDataItems.Insert(i + 1, newItem);

                    // SetSoilDisplacementを呼び出す
                    newItem.SetSoilDisplacement(/*InputModel.Instance*/);

                    OnZDataItemsChanged();
                }
            }
        }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        private ObservableCollection<SoilPile> PrevSoilPiles { get; set; }
        private SoilEmbedment PrevSoilEmbedment { get; set; }

        private double originalValue;
        private bool isEditingCancelled = false;

        /// <summary>ウィンドウを開く前のIsElementSplit状態（キャンセル時に復元用）</summary>
        public bool WasElementSplitBeforeOpen { get; set; }

        /// <summary>OKボタンが押されたかどうか</summary>
        public bool IsOkPressed { get; private set; }

        // コンストラクタ //
        /// <summary>
        /// 標準コンストラクタ（UIスレッドでDeepCopyを実行）
        /// </summary>
        public ElementDivisionViewModel(MainWindowViewModel mainWindowViewModel)
            : this(mainWindowViewModel, null, null)
        {
        }

        /// <summary>
        /// 高速コンストラクタ（事前にバックグラウンドスレッドでDeepCopy済みのデータを受け取る）
        /// </summary>
        public ElementDivisionViewModel(
            MainWindowViewModel mainWindowViewModel,
            IList<SoilPile>? preCopiedPiles,
            SoilEmbedment? preCopiedEmbedment)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            var elementDivision = InputModel.ElementDivision;

            // 現在のモデルがあれば購読（CurrentInputModel 変更に合わせて再購読すること）
            var coll = _mainWindowViewModel.CurrentInputModel?.ElementDivision?.SoilPiles;
            if (coll != null) coll.CollectionChanged += SoilPiles_CollectionChanged;

            // DeepCopy済みデータがあればそのまま使用、なければここでコピー
            if (preCopiedPiles != null)
            {
                SoilPiles = new ObservableCollection<SoilPile>(preCopiedPiles);
            }
            else
            {
                var soilPilesSnapshot = elementDivision?.SoilPiles?.ToList() ?? new List<SoilPile>();
                SoilPiles = new ObservableCollection<SoilPile>(soilPilesSnapshot.Select(sp => sp.DeepCopy()));
            }

            if (preCopiedEmbedment != null)
            {
                SoilEmbedment = preCopiedEmbedment;
            }
            else if (elementDivision?.SoilEmbedment != null)
            {
                SoilEmbedment = elementDivision.SoilEmbedment.DeepCopy();
            }

            MaxPileSpacing = elementDivision?.MaxPileSpacing ?? MaxPileSpacing;
            FirstDistance = elementDivision?.FirstDistance ?? FirstDistance;
            MaxEmbedmentSpacing = elementDivision?.MaxEmbedmentSpacing ?? MaxEmbedmentSpacing;

            // 参照共有を避けたい場合はスナップショット化（任意）
            SoilPileNumberOption = elementDivision?.SoilPileNumberOption != null
                ? new ObservableCollection<int>(elementDivision.SoilPileNumberOption.ToList())
                : new ObservableCollection<int>();

            DoatsuGoryokuBane = elementDivision?.DoatsuGoryokuBane;

            UpdateZsCollection();

            if (DoatsuGoryokuBane != null)
            {
                DoatsuGoryokuBane.Items.CollectionChanged -= OnDoatsuGoryokuBaneItemsChanged;
                DoatsuGoryokuBane.Items.CollectionChanged += OnDoatsuGoryokuBaneItemsChanged;
            }

            // SoilPile ランプ初期化（土層-杭セット数に合わせて false で作成）
            SoilPileLampStates.Clear();
            for (int i = 0; i < SoilPiles.Count; i++)
            {
                SoilPileLampStates.Add(new PileDesign.Models.LampState(i, false));
            }

            // 既存ランプのイベント解除
            foreach (var lamp in SoilPileLampStates)
                lamp.PropertyChanged -= OnLampPropertyChanged;

            // ランプのPropertyChanged購読（AllLampsLit通知用）
            foreach (var lamp in SoilPileLampStates)
            {
                lamp.PropertyChanged += OnLampPropertyChanged;
            }

            // 根入れランプ（Embedment）が存在する場合は 1 つ以上作成する（ここでは単一扱い）
            foreach (var lamp in EmbedmentLampStates)
                lamp.PropertyChanged -= OnLampPropertyChanged;
            EmbedmentLampStates.Clear();
            if (SoilEmbedment != null && InputModel?.EmbedmentInput != null && InputModel.EmbedmentInput.EmbedmentLayersCount > 0)
            {
                var embedmentLamp = new PileDesign.Models.LampState(0, false);
                embedmentLamp.PropertyChanged += OnLampPropertyChanged;
                EmbedmentLampStates.Add(embedmentLamp);
            }
        }

        // ランプのPropertyChanged購読用ハンドラ
        private void OnLampPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PileDesign.Models.LampState.IsOn))
            {
                OnPropertyChanged(nameof(AllLampsLit));
                OnPropertyChanged(nameof(HasAnyRedLamp));
            }
        }

        // 全てのランプが点灯しているかどうか
        public bool AllLampsLit
        {
            get
            {
                // 土層-杭セットのランプが全て点灯しているか
                bool allPilesLit = SoilPileLampStates == null || SoilPileLampStates.Count == 0
                    || SoilPileLampStates.All(lamp => lamp.IsOn);

                // 根入れランプが全て点灯しているか（存在する場合）
                bool allEmbedmentsLit = EmbedmentLampStates == null || EmbedmentLampStates.Count == 0
                    || EmbedmentLampStates.All(lamp => lamp.IsOn);

                return allPilesLit && allEmbedmentsLit;
            }
        }

        // 初期ランプ点灯（UIバインディング完了後に呼び出される）
        private void EnsureLampInitialState()
        {
            if (SoilPileLampStates != null && SoilPileLampStates.Count > 0)
            {
                int idx = Math.Max(0, Math.Min(SoilPileLampStates.Count - 1, SelectedSoilPileNo - 1));
                SoilPileLampStates[idx].IsOn = true;
            }
        }

        // 土層-杭セットのランプを点灯
        private void MarkPileAsShown(int pileIndex)
        {
            if (pileIndex < 0 || SoilPileLampStates == null || pileIndex >= SoilPileLampStates.Count) return;
            if (!SoilPileLampStates[pileIndex].IsOn)
            {
                SoilPileLampStates[pileIndex].IsOn = true;
            }
        }

        // 根入れランプを点灯
        private void MarkEmbedmentAsShown(int idx = 0)
        {
            if (idx >= 0 && EmbedmentLampStates != null && idx < EmbedmentLampStates.Count)
            {
                if (!EmbedmentLampStates[idx].IsOn)
                {
                    EmbedmentLampStates[idx].IsOn = true;
                }
            }
        }

        // メソッド //


        [RelayCommand]
        private void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is List<SoilPile> state)
            {
                SoilPiles = new ObservableCollection<SoilPile>(state.Select(p => p.DeepCopy()));
                SelectedZDataItems = new ObservableCollection<PileZDataItem>(
                    SoilPiles[SelectedSoilPileNo - 1].ZDataItems.Select(item => item.DeepCopy()));
                OnZDataItemsChanged();
            }
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is List<SoilPile> state)
            {
                SoilPiles = new ObservableCollection<SoilPile>(state.Select(p => p.DeepCopy()));
                SelectedZDataItems = new ObservableCollection<PileZDataItem>(
                    SoilPiles[SelectedSoilPileNo - 1].ZDataItems.Select(item => item.DeepCopy()));
                OnZDataItemsChanged();
            }
        }

        private void OnDoatsuGoryokuBaneItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (DoatsuGoryokuBane != null)
            {
                OnPropertyChanged(nameof(IsDoatsuGoryokuBaneVisible));
            }
        }

        // 初期化メソッド
        public void Initialize()
        {
            OnZDataItemsChanged();

            UpdateSoilEmbedmentProperties();
            SetDoatsuGoryokuBane();
            DrawDoatsuGoryokuBaneGraph();
            DrawSoilEmbedment();

            // UI バインディング／初期描画が完了した後にランプの初期点灯を保証する
            EnsureLampInitialState();

            // 土層-根入れセットタブは初期状態で隠れているため、ランプは赤（未確認）にリセット
            foreach (var lamp in EmbedmentLampStates)
            {
                lamp.IsOn = false;
            }
        }


        // 水平地盤反力係数のセットメソッド
        public void SetHorizontalSoilReaction()
        {
            if (SoilPiles == null || SoilPiles.Count == 0) return;
            if (SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count) return;
            if (SelectedZDataItems == null || SelectedZDataItems.Count < 2) return;


            SelectedHorizontalSoilReactions.Clear();

            GroundInput groundInput = InputModel.GroundsInput[SoilPiles[SelectedSoilPileNo - 1].GroundNo - 1];
            PileBodyInput pileBody = InputModel.PileBodies[SoilPiles[SelectedSoilPileNo - 1].PileBodyNo - 1];

            for (int i = 0; i < SelectedZDataItems.Count - 1; i++)
            {
                double zTop = SelectedZDataItems[0].Z;
                double zDataTop = SelectedZDataItems[i].Z;
                double zDataBtm = SelectedZDataItems[i + 1].Z;

                double b = 0;
                double cohesive = 0;
                double nValue = 0;
                double gamma = 0;
                double phi = 0;
                double stressTop = 0;
                double stressBtm = 0;
                string soilType = string.Empty;
                double e0 = 0;
                string name = string.Empty;
                double xi = Xi;
                double rOnB = ROnB;

                foreach (PileBodySegment pileBodySegment in pileBody.PileBodySegments)
                {
                    double segmentTop = -pileBodySegment.SegmentDepth + pileBodySegment.SegmentLength;
                    double segmentBtm = -pileBodySegment.SegmentDepth;

                    double upper = zDataTop - zTop;
                    double lower = zDataBtm - zTop;

                    if (segmentBtm - epsilon <= lower && upper <= segmentTop + epsilon)
                    {
                        b = pileBodySegment.PileSection.PileDiameter / 1000.0;
                        break;
                    }
                }

                foreach (GroundLayerInput groundLayer in groundInput.GroundLayers)
                {
                    double top = groundLayer.LayerThickness + groundLayer.BottomAltitude;
                    double bottom = groundLayer.BottomAltitude;

                    // 浮動小数点誤差を考慮した比較（epsilonを使用）
                    if (bottom - epsilon <= zDataBtm && zDataTop <= top + epsilon)
                    {
                        cohesive = groundLayer.Cohesive;
                        nValue = groundLayer.NValue;
                        gamma = groundLayer.Density;
                        //phi = Math.Min(Math.Sqrt(20 * groundLayer.NValue) + 15, 40); // 大崎式
                        stressTop = GetEffectiveStress(groundInput, zDataTop);
                        stressBtm = GetEffectiveStress(groundInput, zDataBtm);
                        phi = groundInput.GetFrictionAngle(nValue, (stressTop + stressBtm) * 0.5);
                        soilType = groundLayer.GranularityClass;
                        e0 = groundLayer.Es;
                        name = groundLayer.Name;
                        break;
                    }
                }

                HorizontalSoilReactionItem horizontalSoilReactionItem = new();
                horizontalSoilReactionItem.SetParameters(
                name, soilType, gamma, b, e0,
                zDataTop, zDataBtm,
                xi, rOnB, nValue, phi, cohesive, stressTop, stressBtm);

                // 土層ごとの kh0 手入力オーバーライドがあれば適用（自動計算値を上書き）
                double? kh0Override = SoilPiles[SelectedSoilPileNo - 1].GetKh0Override(name);
                if (kh0Override.HasValue)
                {
                    horizontalSoilReactionItem.Kh0 = kh0Override.Value;
                    horizontalSoilReactionItem.IsKh0Manual = true;
                }

                SelectedHorizontalSoilReactions.Add(horizontalSoilReactionItem);
            }
            DrawHorizontalSoilReacitonGraph();
        }

        /// <summary>
        /// 水平地盤反力グリッドで kh0 セルが編集されたときに呼ぶ。編集行の土層に対して
        /// 手入力オーバーライドを設定し（土層単位＝同一土層の全要素に適用）、
        /// 該当土層-杭セットの水平地盤反力を再構築して表示・グラフを更新する。
        /// </summary>
        public void ApplyKh0Edit(HorizontalSoilReactionItem editedItem, double newValue)
        {
            if (editedItem == null) return;
            if (SoilPiles == null || SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count) return;
            if (string.IsNullOrEmpty(editedItem.Name)) return;
            if (!double.IsFinite(newValue) || newValue <= 0) { SetHorizontalSoilReaction(); return; }

            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());

            // モデル側（解析が参照する SoilPile.HorizontalSoilReactions）を再構築
            SoilPiles[SelectedSoilPileNo - 1].SetKh0Override(editedItem.Name, newValue);
            // 表示側を再構築
            SetHorizontalSoilReaction();
        }

        /// <summary>選択中の土層（グリッド選択行）の kh0 を自動計算に戻す。</summary>
        [RelayCommand]
        private void ResetKh0ForSelectedLayer()
        {
            if (SelectedLayeronDataGrid == null) return;
            if (SoilPiles == null || SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count) return;
            if (string.IsNullOrEmpty(SelectedLayeronDataGrid.Name)) return;

            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());
            SoilPiles[SelectedSoilPileNo - 1].ClearKh0Override(SelectedLayeronDataGrid.Name);
            SetHorizontalSoilReaction();
        }

        /// <summary>この土層-杭セットの全土層の kh0 を自動計算に戻す。</summary>
        [RelayCommand]
        private void ResetAllKh0()
        {
            if (SoilPiles == null || SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count) return;

            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());
            SoilPiles[SelectedSoilPileNo - 1].ClearAllKh0Overrides();
            SetHorizontalSoilReaction();
        }

        // 土圧合力ばねのセットメソッド
        public void SetDoatsuGoryokuBane()
        {
            DoatsuGoryokuBane = new();

            if (SoilEmbedment == null)
            { return; }

            GroundInput groundInput = InputModel.GroundsInput[SoilEmbedment.GroundNo - 1];

            for (int i = 0; i < EmbedmentZsCollection.Count - 1; i++)
            {
                double density = 0;
                double cohesive = 0;
                double phi = 0;
                double stressTop = 0;
                double stressBtm = 0;
                string name = string.Empty;
                double zTop = EmbedmentZsCollection[i].Z;
                double zBtm = EmbedmentZsCollection[i + 1].Z;
                ZDataItem zDataItemTop = EmbedmentZsCollection[i];
                ZDataItem zDataItemBtm = EmbedmentZsCollection[i + 1];
                double x1 = 0;
                double x2 = 0;
                double y1 = 0;
                double y2 = 0;

                foreach (EmbedmentDataItem data in InputModel.EmbedmentInput.EmbedmentLayers)
                {
                    if (data.BottomAltitude <= EmbedmentZsCollection[i + 1].Z &&
                        EmbedmentZsCollection[i].Z <= data.TopAltitude)
                    {
                        x1 = data.X1;
                        x2 = data.X2;
                        y1 = data.Y1;
                        y2 = data.Y2;
                        break;
                    }
                }

                foreach (GroundLayerInput groundLayer in groundInput.GroundLayers)
                {
                    double top = groundLayer.LayerThickness + groundLayer.BottomAltitude;
                    double bottom = groundLayer.BottomAltitude;

                    if (top >= zTop && zBtm >= bottom)
                    {
                        density = groundLayer.Density;
                        cohesive = groundLayer.Cohesive;
                        //phi = Math.Min(Math.Sqrt(20 * groundLayer.NValue) + 15, 40); // 大崎式

                        stressTop = GetEffectiveStress(groundInput, zTop);
                        stressBtm = GetEffectiveStress(groundInput, zBtm);
                        phi = groundInput.GetFrictionAngle(groundLayer.NValue, (stressTop + stressBtm) * 0.5);
                        name = groundLayer.Name;
                        break;
                    }
                }
                DoatsuGoryokuBane.Items.Add(new DoatsuGoryokuBaneItem(
                    zDataItemTop, zDataItemBtm, zTop, zBtm, x1, x2, y1, y2,
                    name, density, stressTop, stressBtm, phi, cohesive));
            }
            DoatsuGoryokuBane.UpdateDeltaPYsp(0.05);
            DrawDoatsuGoryokuBaneGraph();
        }

        // 有効応力を得るメソッド
        private static double GetEffectiveStress(GroundInput groundInput, double z)
        {
            double stressLevel = groundInput.StressAltitude;
            double waterLevel = groundInput.GroundWaterTableAltitude;

            double effectiveStress = 0;

            foreach (var groundLayer in groundInput.GroundLayers)
            {
                double density = groundLayer.Density;
                double topAltitude = groundLayer.BottomAltitude + groundLayer.LayerThickness;
                double layerTopLevel = Math.Min(stressLevel, topAltitude);
                double layerBottomLevel = Math.Max(z, groundLayer.BottomAltitude);

                effectiveStress += Math.Max((layerTopLevel - layerBottomLevel) * density, 0);
            }
            effectiveStress -= Math.Max(waterLevel - z, 0) * 10;

            return effectiveStress;
        }

        // 水平地盤反力を描くメソッド
        private void DrawHorizontalSoilReacitonGraph()
        {
            if (ElementDivisionWindowInstance == null)
            { return; }

            List<double> x1 = [];
            List<double> y1 = [];
            foreach (HorizontalSoilReactionItem item in SelectedHorizontalSoilReactions)
            {
                double kh0 = item.Kh0;
                double yTop = item.ZTop;
                double yBtm = item.ZBtm;
                x1.Add(kh0);
                x1.Add(kh0);
                y1.Add(yTop);
                y1.Add(yBtm);
            }

            var dataX1 = x1.ToArray();
            var dataY1 = y1.ToArray();
            var wpf = ElementDivisionWindowInstance.wpfPlotKh0;
            wpf.Plot.Clear();

            foreach (HorizontalSoilReactionItem item in SelectedHorizontalSoilReactions)
            {
                var coordinate = new CoordinateRect(0, item.Kh0, item.ZBtm, item.ZTop);
                var rectangle = wpf.Plot.Add.Rectangle(coordinate);
                rectangle.FillColor = Color.FromSKColor(NikkenSKColor.SkyBlue);
                rectangle.LineColor = new(0, 0, 0, 255); // 黒色
                rectangle.LineWidth = 1;
            }

            double xMaxKh0 = dataX1.Length > 0 ? dataX1.Max() : 1.0;
            for (int i = 0; i < dataX1.Length; i++)
            {
                PlotHelper.AddText(wpf.Plot, $"{dataX1[i]:N0}", dataX1[i], dataY1[i], xMaxKh0);
            }

            if (x1.Count == 0)
            {
                // データが無ければグラフを空のままにする。
                // ここは再描画のたびに通るため、ダイアログを出すと連続表示になって操作できなくなる
                // (以前あったメッセージが無効化されていたのはそのため)。
                // 「水平地盤反力が無い」ことは杭要素分割のランプ表示で分かる。
                return;
            }

            if (SelectedLayeronDataGrid != null)
            {
                double selectedKh0 = SelectedLayeronDataGrid.Kh0;
                double selectedZBtm = SelectedLayeronDataGrid.ZBtm;
                double selectedZTop = SelectedLayeronDataGrid.ZTop;
                var coordinate = new CoordinateRect(0, selectedKh0, selectedZBtm, selectedZTop);
                var rectangle = wpf.Plot.Add.Rectangle(coordinate);
                rectangle.LineColor = new(0, 0, 0, 255); // 黒色
                rectangle.FillColor = Color.FromSKColor(NikkenSKColor.DeepBlue);
                rectangle.LineWidth = 1;
            }

            double maxK = Math.Max(-dataX1.Min(), dataX1.Max()); ;

            string title = "基準水平地盤反力係数分布";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "基準水平地盤反力係数 kₕ₀ (kN/m³)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "Z(m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            Color grayColor = new(128, 128, 128, 255);
            wpf.Plot.Add.VerticalLine(0, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Plot.Axes.Bottom.Min = 0.0;
            wpf.Plot.Axes.Bottom.Max = maxK * 1.2;

            wpf.Refresh();

            // 
            double maxY = 0;
            var wpfPlotPFront = ElementDivisionWindowInstance.wpfPlotPFront;
            maxY = DrawHorizontalReactionForceGraph(wpfPlotPFront, maxY, true);

            var wpfPlotPRear = ElementDivisionWindowInstance.wpfPlotPRear;
            maxY = DrawHorizontalReactionForceGraph(wpfPlotPRear, maxY, false);


            // クロスヘアの初期化
            MyCrosshair_kh0 = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove_RectangleVertexFocus(s, e, "CrosshairPositionText_kh0", "kₕ₀(kN/m³)", "Z(m)");

            // クロスヘアの初期化
            MyCrosshair_Front = PlotHelper.InitCrosshair(wpfPlotPFront, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpfPlotPFront.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Front", "相対変位(mm)", "p(kN/m2)");

            MyCrosshair_Rear = PlotHelper.InitCrosshair(wpfPlotPRear, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpfPlotPRear.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Rear", "相対変位(mm)", "p(kN/m2)");
        }

        // 水平反力グラフ描画メソッド
        private double DrawHorizontalReactionForceGraph(WpfPlot wpfPlot, double maxY, bool Isfront)
        {
            wpfPlot.Plot.Clear();

            List<double> xs;
            List<double> ys;
            double[] dataX;
            double[] dataY;
            List<HorizontalSoilReactionItem> items = [];
            foreach (HorizontalSoilReactionItem item in SelectedHorizontalSoilReactions)
            {
                items.Add(item);
            }

            for (int j = items.Count - 1; j >= 0; j--)
            {
                // bottom
                xs = [];
                ys = [];

                for (double factor = 0.00; factor < 0.50; factor += 0.01)
                {
                    xs.Add(factor);
                    double pyBtm = Isfront ? items[j].PyFrontBtm : items[j].PyRearBtm;
                    ys.Add(items[j].GetP(xs[^1], pyBtm));
                }

                dataX = [.. xs.Select(x => x * 1000)]; // xsの値を1000倍にする m -> mm
                dataY = [.. ys];
                maxY = Math.Max(maxY, ys.Max());

                var scatterLineB = wpfPlot.Plot.Add.ScatterLine(dataX, dataY);
                scatterLineB.LegendText = $"{j + 1}B";
                PlotHelper.AddText(wpfPlot.Plot, $"{j + 1}B", dataX[^1] + 50, dataY[^1], 600);

                if (SelectedLayeronDataGrid == items[j])
                {
                    scatterLineB.LineWidth = 2;
                }

                // top
                xs = [];
                ys = [];

                for (double factor = 0.00; factor < 0.50; factor += 0.01)
                {
                    xs.Add(factor);
                    double pyTop = Isfront ? items[j].PyFrontTop : items[j].PyRearTop;
                    ys.Add(items[j].GetP(xs[^1], pyTop));
                }

                dataX = [.. xs.Select(x => x * 1000)]; // xsの値を1000倍にする m -> mm
                dataY = [.. ys];
                maxY = Math.Max(maxY, ys.Max());

                var scatterLineT = wpfPlot.Plot.Add.ScatterLine(dataX, dataY);
                scatterLineT.LegendText = $"{j + 1}T";
                PlotHelper.AddText(wpfPlot.Plot, $"{j + 1}T", dataX[^1], dataY[^1], 600);

                if (SelectedLayeronDataGrid == items[j])
                {
                    scatterLineT.LineWidth = 3;
                }
            }

            string title = Isfront ? "前方杭" : "後方杭";
            wpfPlot.Plot.Axes.Title.Label.Text = title;
            wpfPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string yAxisTitle = "水平地盤反力度p (kN/m2)";
            wpfPlot.Plot.Axes.Left.Label.Text = yAxisTitle;
            wpfPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yAxisTitle);

            string xAxisTitle = "相対変位(mm)";
            wpfPlot.Plot.Axes.Bottom.Label.Text = xAxisTitle;
            wpfPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xAxisTitle);

            Color grayColor = new(128, 128, 128, 255);
            wpfPlot.Plot.Add.VerticalLine(0, 1, grayColor);
            wpfPlot.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpfPlot.Plot.Axes.AutoScale(); // 軸の範囲を自動的に調整
            wpfPlot.Plot.Axes.AutoScaleExpandX();
            wpfPlot.Plot.Axes.AutoScaleExpandY();
            wpfPlot.Plot.Axes.Left.Max = maxY * 1.1;
            wpfPlot.Plot.Axes.Bottom.Max = 600;
            wpfPlot.Plot.Legend.IsVisible = false;

            wpfPlot.Refresh();

            return maxY;
        }

        // 土圧合力ばねグラフ描画メソッド
        private void DrawDoatsuGoryokuBaneGraph()
        {
            if (ElementDivisionWindowInstance == null)
            { return; }

            // 1. 土圧分布用データ作成
            var xList = new List<double>();
            var yList = new List<double>();
            for (int i = 0; i < DoatsuGoryokuBane.Items.Count; i++)
            {
                var item = DoatsuGoryokuBane.Items[i];
                if (i == 0)
                {
                    xList.Add(0.0);
                    yList.Add(item.ZTop);
                }
                xList.Add((item.PpTop - item.P0Top) / 3);
                xList.Add((item.PpBtm - item.P0Btm) / 3);
                yList.Add(item.ZTop);
                yList.Add(item.ZBtm);
                if (i == DoatsuGoryokuBane.Items.Count - 1)
                {
                    xList.Add(0.0);
                    yList.Add(item.ZBtm);
                }
            }
            var dataX1 = xList.ToArray();
            var dataY1 = yList.ToArray();

            // 2. 土圧分布グラフ
            var wpf = ElementDivisionWindowInstance.wpfPlotPpPo;
            wpf.Plot.Clear();
            var scatter = wpf.Plot.Add.Scatter(dataX1, dataY1);
            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);

            if (SelectedDoatsuGoryokuBaneItem != null)
            {
                var xSelectedList = new List<double>();
                var ySelectedList = new List<double>();
                xSelectedList.Add((SelectedDoatsuGoryokuBaneItem.PpTop - SelectedDoatsuGoryokuBaneItem.P0Top) / 3);
                xSelectedList.Add((SelectedDoatsuGoryokuBaneItem.PpBtm - SelectedDoatsuGoryokuBaneItem.P0Btm) / 3);
                ySelectedList.Add(SelectedDoatsuGoryokuBaneItem.ZTop);
                ySelectedList.Add(SelectedDoatsuGoryokuBaneItem.ZBtm);

                var dataXSelected = xSelectedList.ToArray();
                var dataYSelected = ySelectedList.ToArray();
                var scatterSelected = wpf.Plot.Add.Scatter(dataXSelected, dataYSelected);
                scatterSelected.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                scatterSelected.LineWidth = 2;
            }

            wpf.Plot.Axes.Title.Label.Text = "基準等相対変位時土圧分布";
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(wpf.Plot.Axes.Title.Label.Text);
            wpf.Plot.Axes.Bottom.Label.Text = "土圧 (pₚ−p₀)/3 (kN/m²)";
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(wpf.Plot.Axes.Bottom.Label.Text);
            wpf.Plot.Axes.Left.Label.Text = "Z (m)";
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(wpf.Plot.Axes.Left.Label.Text);

            var grayColor = new Color(128, 128, 128, 255);
            wpf.Plot.Add.VerticalLine(0, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Refresh();

            var xList2 = new List<double>();
            var yList2 = new List<double>();
            var dgbXList = new List<double>();
            var dgbYList = new List<double>();
            for (double factor = 0.00; factor < 1.1; factor += 0.01)
            {
                double x = DoatsuGoryokuBane.DeltaP * factor;
                double dgbPerM = 0, dgbX = 0, dgbY = 0;
                foreach (var item in DoatsuGoryokuBane.Items)
                {
                    double dz = item.ZTop - item.ZBtm;
                    double p = item.GetPressure(x);
                    dgbPerM += p * dz;
                    dgbX += p * dz * Math.Abs(item.Y1 - item.Y2);
                    dgbY += p * dz * Math.Abs(item.X1 - item.X2);
                }
                xList2.Add(x * 1000); // mm単位に変換
                yList2.Add(dgbPerM);
                dgbXList.Add(dgbX);
                dgbYList.Add(dgbY);
            }
            var dataX2 = xList2.ToArray();
            var dataY2 = yList2.ToArray();
            var dataDgbX = dgbXList.ToArray();
            var dataDgbY = dgbYList.ToArray();

            // 4. 合力グラフ描画（wpfDGBperM, wpfDGB）...（以降は元のロジックを整理して流用）

            // (1) 単位幅土圧合力グラフ
            var wpfDGBperM = ElementDivisionWindowInstance.wpfPlotDGBperM;
            wpfDGBperM.Plot.Clear();
            var scatter2 = wpfDGBperM.Plot.Add.Scatter(dataX2, dataY2);
            scatter2.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter2.LineWidth = 1;
            scatter2.MarkerSize = 0;

            wpfDGBperM.Plot.Axes.Title.Label.Text = "等変形時の相対変位と基礎根入れ部の土圧合力の関係";
            wpfDGBperM.Plot.Axes.Title.Label.FontName = Fonts.Detect(wpfDGBperM.Plot.Axes.Title.Label.Text);
            wpfDGBperM.Plot.Axes.Left.Label.Text = "単位幅土圧合力 (kN/m)";
            wpfDGBperM.Plot.Axes.Left.Label.FontName = Fonts.Detect(wpfDGBperM.Plot.Axes.Left.Label.Text);
            wpfDGBperM.Plot.Axes.Bottom.Label.Text = "相対変位(mm)";
            wpfDGBperM.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(wpfDGBperM.Plot.Axes.Bottom.Label.Text);

            //var grayColor = new Color(128, 128, 128, 255);
            wpfDGBperM.Plot.Add.VerticalLine(0, 1, grayColor);
            wpfDGBperM.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpfDGBperM.Plot.Axes.AutoScale();
            wpfDGBperM.Plot.Axes.AutoScaleExpandX();
            wpfDGBperM.Plot.Axes.AutoScaleExpandY();
            wpfDGBperM.Refresh();

            // (2) 土圧合力グラフ
            var wpfDGB = ElementDivisionWindowInstance.wpfPlotDGB;
            wpfDGB.Plot.Clear();

            var scatterX = wpfDGB.Plot.Add.Scatter(dataX2, dataDgbX);
            scatterX.Color = Color.FromSKColor(NikkenSKColor.PaleRed);
            scatterX.LineWidth = 1;
            scatterX.MarkerSize = 0;

            var scatterY = wpfDGB.Plot.Add.Scatter(dataX2, dataDgbY);
            scatterY.Color = Color.FromSKColor(NikkenSKColor.Yellow);
            scatterY.LineWidth = 1;
            scatterY.MarkerSize = 0;

            wpfDGB.Plot.Axes.Title.Label.Text = "等変形時の相対変位と基礎根入れ部の土圧合力の関係";
            wpfDGB.Plot.Axes.Title.Label.FontName = Fonts.Detect(wpfDGB.Plot.Axes.Title.Label.Text);
            wpfDGB.Plot.Axes.Left.Label.Text = "土圧合力 (kN)";
            wpfDGB.Plot.Axes.Left.Label.FontName = Fonts.Detect(wpfDGB.Plot.Axes.Left.Label.Text);
            wpfDGB.Plot.Axes.Bottom.Label.Text = "相対変位(mm)";
            wpfDGB.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(wpfDGB.Plot.Axes.Bottom.Label.Text);

            wpfDGB.Plot.Add.VerticalLine(0, 1, grayColor);
            wpfDGB.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpfDGB.Plot.Axes.AutoScale();
            wpfDGB.Plot.Axes.AutoScaleExpandX();
            wpfDGB.Plot.Axes.AutoScaleExpandY();

            scatterX.LegendText = "X";
            scatterY.LegendText = "Y";
            wpfDGB.Plot.Add.Legend();
            wpfDGB.Refresh();

            // クロスヘアの初期化
            MyCrosshair_SoilPressureProfile = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_SoilPressureProfile", "土圧(kNm/2)", "Z(m)");

            MyCrosshair_DGperM = PlotHelper.InitCrosshair(wpfDGBperM, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpfDGBperM.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_DGperM", "相対変位(mm)", "単位幅土圧合力(kN/m)");

            MyCrosshair_DG = PlotHelper.InitCrosshair(wpfDGB, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpfDGB.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_DG", "相対変位(mm)", "土圧合力(kN)");
        }

        // 地盤杭自動分割メソッド
        [RelayCommand]

        private void AutoZs()
        {
            // 1回だけ Undo スナップショットを作る（コストの高い DeepCopy を一度に）
            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());

            // 以前の自動分割で追加されたノード（IsChangeable=true）を除去してから再分割
            var original = SelectedZDataItems.Where(z => !z.IsChangeable).ToList();
            var merged = new List<PileZDataItem>(original.Count * 2);

            for (int i = 0; i < original.Count - 1; i++)
            {
                // 現要素を追加
                merged.Add(original[i]);

                double interval = original[i].Z - original[i + 1].Z;
                if (interval > MaxPileSpacing)
                {
                    int divider = (int)Math.Ceiling(interval / MaxPileSpacing);
                    double step = interval / divider;

                    // 中間要素を計算して追加（UI更新は後で一括）
                    for (int j = 1; j < divider; j++)
                    {
                        var newItem = new PileZDataItem
                        {
                            Z = original[i].Z - step * j,
                            IsChangeable = true,
                            GroundInput = SoilPiles[SelectedSoilPileNo - 1].GroundInput
                        };
                        // 必要なら局所的に土の変位を計算（軽量）
                        newItem.SetSoilDisplacement();
                        merged.Add(newItem);
                    }
                }
            }

            // 最後の要素を追加（元のリストの末尾）
            if (original.Count > 0) merged.Add(original.Last());

            // 先頭の FirstDistance チェック（既存ロジックを保持）
            if (merged.Count >= 2 && merged[0].Z - merged[1].Z > FirstDistance)
            {
                var firstInsert = new PileZDataItem
                {
                    Z = merged[0].Z - FirstDistance,
                    IsChangeable = true,
                    GroundInput = SoilPiles[SelectedSoilPileNo - 1].GroundInput
                }
               ;
                firstInsert.SetSoilDisplacement();
                merged.Insert(1, firstInsert);
            }

            // Zの降順（上から下）に並べ替え
            merged = merged.OrderByDescending(item => item.Z).ToList();


            //// 一括差替え（コレクションオブジェクトは置換せず、既存コレクションを更新）
            //_suppressUndoSave = true;
            //SelectedZDataItems.Clear();
            //foreach (var item in merged)
            //{
            //    SelectedZDataItems.Add(item.DeepCopy());
            //}
            //_suppressUndoSave = false;

            //// レイアウトや DataGrid の再計算が落ち着いてから後処理を実行する（描画は Render 優先で遅延）
            //Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            //{
            //    OnZDataItemsChanged();
            //}), System.Windows.Threading.DispatcherPriority.Background);

            // バルク置換：OnZDataItemsChanged 内の SaveState 重複を抑止してから反映し、最後に一度だけ処理を実行
            _suppressUndoSave = true;
            SelectedZDataItems = new ObservableCollection<PileZDataItem>(merged.Select(item => item.DeepCopy()));
            _suppressUndoSave = false;

            // 1 回だけ後処理を実行（DataModel・グラフ等の更新）
            OnZDataItemsChanged();
        }

        // 全土層-杭セット一括自動分割
        [RelayCommand]
        private void AutoZsAllPiles()
        {
            if (SoilPiles == null || SoilPiles.Count == 0) return;

            // Undo スナップショットを1回だけ保存
            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());

            // 現在選択中の杭データを SoilPiles に反映
            if (SelectedSoilPileNo > 0 && SelectedSoilPileNo <= SoilPiles.Count)
            {
                SetZdataItemsAndHorizontalSoilReactionItems(SelectedSoilPileNo);
            }

            // 全杭セットに対して自動分割を適用
            for (int pileIdx = 0; pileIdx < SoilPiles.Count; pileIdx++)
            {
                var soilPile = SoilPiles[pileIdx];
                // 以前の自動分割で追加されたノード（IsChangeable=true）を除去してから再分割
                var original = soilPile.ZDataItems.Where(z => !z.IsChangeable).ToList();
                var merged = new List<PileZDataItem>(original.Count * 2);

                for (int i = 0; i < original.Count - 1; i++)
                {
                    merged.Add(original[i]);

                    double interval = original[i].Z - original[i + 1].Z;
                    if (interval > MaxPileSpacing)
                    {
                        int divider = (int)Math.Ceiling(interval / MaxPileSpacing);
                        double step = interval / divider;

                        for (int j = 1; j < divider; j++)
                        {
                            var newItem = new PileZDataItem
                            {
                                Z = original[i].Z - step * j,
                                IsChangeable = true,
                                GroundInput = soilPile.GroundInput
                            };
                            newItem.SetSoilDisplacement();
                            merged.Add(newItem);
                        }
                    }
                }

                if (original.Count > 0) merged.Add(original.Last());

                // FirstDistance チェック
                if (merged.Count >= 2 && merged[0].Z - merged[1].Z > FirstDistance)
                {
                    var firstInsert = new PileZDataItem
                    {
                        Z = merged[0].Z - FirstDistance,
                        IsChangeable = true,
                        GroundInput = soilPile.GroundInput
                    };
                    firstInsert.SetSoilDisplacement();
                    merged.Insert(1, firstInsert);
                }

                merged = merged.OrderByDescending(item => item.Z).ToList();
                soilPile.ZDataItems = new ObservableCollection<PileZDataItem>(merged);
                soilPile.OnZDataItemsChanged(soilPile.ZDataItems);

                // ランプを緑に点灯
                MarkPileAsShown(pileIdx);
            }

            // 現在表示中の杭の UI を更新
            _suppressUndoSave = true;
            var currentPile = SoilPiles[SelectedSoilPileNo - 1];
            SelectedZDataItems = new ObservableCollection<PileZDataItem>(
                currentPile.ZDataItems.Select(item => item.DeepCopy()));
            _suppressUndoSave = false;

            OnZDataItemsChanged();

            // 根入れ部がある場合は根入れ部も自動分割
            if (SoilEmbedment != null && EmbedmentZsOriginalCollection != null && EmbedmentZsOriginalCollection.Count >= 2)
            {
                AutoEmbedmentZs();
            }
        }

        // 自動番号づけメソッド
        private static void AutoNumberingDataGrid(DataGrid dataGrid)
        {
            foreach (var item in dataGrid.Items)
            {
                var row = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromItem(item);
                if (row != null)
                {
                    row.Header = (row.GetIndex() + 1).ToString();
                }
            }
        }

        // 根入れ節点自動分割メソッド
        [RelayCommand]
        private void AutoEmbedmentZs()
        {
            _undoManager.SaveState(EmbedmentZsCollection.Select(z => z.DeepCopy()).ToList());

            // 元リスト（Original）から作業用リストを作成
            // EmbedmentZsOriginalCollection は変更されない前提で、これをベースに新しいリストを構築します
            var originalList = EmbedmentZsOriginalCollection.Select(
                item => new EmbedmentZDataItem
                {
                    Z = item.Z,
                    IsChangeable = item.IsChangeable,
                    GroundInput = item.GroundInput,
                    GroundDisp1 = item.GroundDisp1,
                    GroundDisp2 = item.GroundDisp2,
                    GroundDisp1L = item.GroundDisp1L,
                    GroundDisp2L = item.GroundDisp2L
                }).ToList();

            var merged = new List<EmbedmentZDataItem>();

            // 上から順に走査して分割点を挿入
            for (int i = 0; i < originalList.Count - 1; i++)
            {
                // 現在の節点を追加
                merged.Add(originalList[i]);

                double interval = originalList[i].Z - originalList[i + 1].Z;
                if (interval > MaxEmbedmentSpacing)
                {
                    int divider = (int)Math.Ceiling(interval / MaxEmbedmentSpacing);
                    double step = interval / divider;

                    // 中間要素を計算して追加（浅い方から順に）
                    for (int j = 1; j < divider; j++)
                    {
                        var newItem = new EmbedmentZDataItem
                        {
                            Z = originalList[i].Z - step * j,
                            IsChangeable = true,
                            GroundInput = InputModel.GroundsInput[SoilEmbedment.GroundNo - 1],
                        };

                        // SetSoilDisplacementを呼び出す
                        newItem.SetSoilDisplacement();

                        merged.Add(newItem);
                    }
                }
            }

            // 最後の節点を追加
            if (originalList.Count > 0)
            {
                merged.Add(originalList.Last());
            }

            // Zの降順（上から下）に並べ替え
            merged = merged.OrderByDescending(item => item.Z).ToList();

            // 一括更新
            EmbedmentZsCollection = new ObservableCollection<EmbedmentZDataItem>(merged);

            OnPropertyChanged(nameof(SelectedZDataItems));
            AutoNumberingDataGrid(ElementDivisionWindowInstance.DataGridEmbedmentZs);

            UpdateSoilEmbedmentProperties();
            SetDoatsuGoryokuBane();
            DrawSoilEmbedment();
        }

        // 地盤杭節点削除メソッド
        [RelayCommand]
        private void DeleteZs(object parameter)
        {
            _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());

            for (int i = SelectedZDataItems.Count - 1; i >= 0; i--)
            {
                if (parameter == SelectedZDataItems[i] && SelectedZDataItems[i].IsChangeable == true)
                {
                    SelectedZDataItems.RemoveAt(i);
                }
                else if (parameter == SelectedZDataItems[i] && SelectedZDataItems[i].IsChangeable == false)
                {
                    MessageService.Show("この節点は削除できません。");
                }
            }
            OnZDataItemsChanged();
        }

        // SelectedZDataItems操作後に行うメソッド
        private void OnZDataItemsChanged()
        {
            // バルク更新時は外部で既に SaveState しているので重複して取らない
            if (!_suppressUndoSave)
            {
                _undoManager.SaveState(SoilPiles.Select(p => p.DeepCopy()).ToList());
            }

            if (SoilPiles == null || SoilPiles.Count == 0) return;
            if (SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count) return;

            var selectedSoilPile = SoilPiles[SelectedSoilPileNo - 1];

            if (selectedSoilPile == null) return;
            if (ElementDivisionWindowInstance == null || ElementDivisionWindowInstance.DataGridZs == null) return;

            // Zの降順（上から下）に並べ替え
            var sortedItems = SelectedZDataItems.OrderByDescending(item => item.Z).ToList();
            if (!SelectedZDataItems.Select(x => x.Z).SequenceEqual(sortedItems.Select(x => x.Z)))
            {
                // 選択状態を保持（PileZDataItemとして保持）
                var currentSelectedItem = SelectedZDataItem as PileZDataItem;

                // 並び替えが必要な場合のみコレクションを更新
                SelectedZDataItems.Clear();
                foreach (var item in sortedItems)
                {
                    SelectedZDataItems.Add(item);
                }

                // 選択状態を復元
                if (currentSelectedItem != null && SelectedZDataItems.Contains(currentSelectedItem))
                {
                    SelectedZDataItem = currentSelectedItem;
                }
            }

            SoilPiles[SelectedSoilPileNo - 1].ZDataItems = new ObservableCollection<PileZDataItem>
                    (SelectedZDataItems.Select(item => item.DeepCopy()));
            SoilPiles[SelectedSoilPileNo - 1].OnZDataItemsChanged(SoilPiles[SelectedSoilPileNo - 1].ZDataItems); // SoilPileのプロパティ更新を委譲

            PileGroundNo = SoilPiles[SelectedSoilPileNo - 1].GroundNo;
            PileBodyNo = SoilPiles[SelectedSoilPileNo - 1].PileBodyNo;
            Z = SoilPiles[SelectedSoilPileNo - 1].Z;
            PileBottomAltitude = SoilPiles[SelectedSoilPileNo - 1].PileBottomAltitude;

            // DataGrid の行ヘッダ再番号付けは可能な限り一度だけ行う
            AutoNumberingDataGrid(ElementDivisionWindowInstance.DataGridZs);

            UpdateSelectedSoilPileProperties();

            SetHorizontalSoilReaction();
            DrawShapes();

            // ItemsSourceはXAMLのバインディングで設定されているため、直接設定しない
            // （直接設定するとバインディングが解除されてしまう）
            // 代わりに、コレクションの変更通知を発行する
            OnPropertyChanged(nameof(SelectedZDataItems));
            OnPropertyChanged(nameof(SelectedHorizontalSoilReactions));

            OnPropertyChanged(nameof(PileGroundNo));
            OnPropertyChanged(nameof(PileBodyNo));
            OnPropertyChanged(nameof(Z));
            OnPropertyChanged(nameof(PileBottomAltitude));
        }

        // 根入れ節点削除メソッド
        [RelayCommand]
        private void DeleteEmbedmentZs(object parameter)
        {
            _undoManager.SaveState(EmbedmentZsCollection.Select(z => z.DeepCopy()).ToList());

            for (int i = EmbedmentZsCollection.Count - 1; i >= 0; i--)
            {
                if (parameter == EmbedmentZsCollection[i] && EmbedmentZsCollection[i].IsChangeable == true)
                {
                    EmbedmentZsCollection.RemoveAt(i);
                }
                else if (parameter == EmbedmentZsCollection[i] && EmbedmentZsCollection[i].IsChangeable == false)
                {
                    MessageService.Show("この節点は削除できません。");
                }
            }

            AutoNumberingDataGrid(ElementDivisionWindowInstance.DataGridEmbedmentZs);

            UpdateSoilEmbedmentProperties();
            SetDoatsuGoryokuBane();
            DrawSoilEmbedment();
        }

        // 根入れ節点初期化メソッド
        [RelayCommand]
        public void ResetEmbedment()
        {
            _undoManager.SaveState(EmbedmentZsCollection.Select(z => z.DeepCopy()).ToList());

            // IsChangeableがtrueの行を削除
            var itemsToRemove = EmbedmentZsCollection.Where(z => z.IsChangeable).ToList();
            foreach (var item in itemsToRemove)
            {
                EmbedmentZsCollection.Remove(item);
            }
            AutoNumberingDataGrid(ElementDivisionWindowInstance.DataGridEmbedmentZs);

            UpdateSoilEmbedmentProperties();
            SetDoatsuGoryokuBane();
            DrawSoilEmbedment();
        }

        // 根入節点追加メソッド
        [RelayCommand]
        private void AddEmbedmentZs()
        {
            _undoManager.SaveState(EmbedmentZsCollection.Select(z => z.DeepCopy()).ToList());

            for (int i = 0; i < EmbedmentZsCollection.Count - 1; i++)
            {
                if (SelectedEmbedmentZ == EmbedmentZsCollection[i])
                {
                    double upperValue = EmbedmentZsCollection[i].Z;
                    double lowerValue = EmbedmentZsCollection[i + 1].Z;
                    double averageValue = (upperValue + lowerValue) * 0.5;

                    EmbedmentZDataItem newItem = new()
                    {
                        Z = averageValue,
                        IsChangeable = true,
                        //GroundLayerNo = SoilEmbedment.GroundNo
                        GroundInput = InputModel.GroundsInput[SoilEmbedment.GroundNo - 1]
                    };

                    // 選択された行の下に新しい行を追加
                    EmbedmentZsCollection.Insert(i + 1, newItem);

                    // SetSoilDisplacementを呼び出す
                    newItem.SetSoilDisplacement(/*InputModel.Instance*/);
                }
            }
            AutoNumberingDataGrid(ElementDivisionWindowInstance.DataGridEmbedmentZs);

            UpdateSoilEmbedmentProperties();
            SetDoatsuGoryokuBane();
            DrawDoatsuGoryokuBaneGraph();
            DrawSoilEmbedment();
        }

        public void OnDataGridZsCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (isEditingCancelled)
            {
                isEditingCancelled = false;
                return;
            }

            if (e.Column is DataGridTextColumn textColumn)
            {
                if (textColumn.Header is StackPanel headerPanel)
                {
                    var headerTextBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (headerTextBlock != null && headerTextBlock.Text == "Z")
                    {
                        if (e.Row.Item is ZDataItem editedElement)
                        {
                            if (e.EditingElement is TextBox textBox)
                            {
                                if (double.TryParse(textBox.Text, out double newValue))
                                {
                                    // SelectedZDataItemsを直接使用（ItemsSourceのキャストは型の不一致でnullになる可能性がある）
                                    var items = SelectedZDataItems;
                                    if (items == null) return;
                                    int index = items.IndexOf(editedElement as PileZDataItem);
                                    if (index < 0) return; // アイテムが見つからない場合は終了

                                    if (index > 0 && newValue >= items[index - 1].Z)
                                    {
                                        MessageService.Show("直上のZより小さい値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                                        e.Cancel = true;
                                        isEditingCancelled = true;
                                        // 元の値に戻す
                                        textBox.Text = originalValue.ToString();
                                        return;
                                    }

                                    if (index < items.Count - 1 && newValue <= items[index + 1].Z)
                                    {
                                        MessageService.Show("直下のZより大きい値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                                        e.Cancel = true;
                                        isEditingCancelled = true;
                                        // 元の値に戻す
                                        textBox.Text = originalValue.ToString();
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void OnDataGridZsBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (e.Column is DataGridTextColumn textColumn)
            {
                if (textColumn.Header is StackPanel headerPanel)
                {
                    var headerTextBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (headerTextBlock != null && headerTextBlock.Text == "Z")
                    {
                        var editedElement = e.Row.Item as ZDataItem;
                        if (editedElement != null && !editedElement.IsChangeable)
                        {
                            MessageService.Show("この値は変更できません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                            e.Cancel = true;
                        }
                        else if (editedElement != null)
                        {
                            // 元の値を保存
                            originalValue = editedElement.Z;
                        }
                    }
                }
            }
        }

        // ComboBoxSoilPileSelectionが変更されたときのメソッド
        public void OnComboBoxSoilPileSelectionChanged(SelectionChangedEventArgs e)
        {
            if (SoilPiles == null || SoilPiles.Count == 0) return;

            int? oldValue = null;
            int? newValue = null;

            // 古い選択値を取得
            if (e.RemovedItems.Count > 0)
            {
                oldValue = e.RemovedItems[0] as int?;
            }

            // 新しい選択値を取得
            if (e.AddedItems.Count > 0)
            {
                newValue = e.AddedItems[0] as int?;
            }

            // 変更後のデータの表示
            if (newValue.HasValue && newValue.Value > 0 && newValue.Value <= SoilPiles.Count)
            {
                // 前の杭のデータを保存（切り替え前に実行）
                if (oldValue.HasValue && oldValue.Value > 0 && oldValue.Value <= SoilPiles.Count)
                {
                    SetZdataItemsAndHorizontalSoilReactionItems(oldValue.Value);
                }

                PreviousSelectedSoilPileNo = oldValue ?? _selectedSoilPileNo;
                SelectedSoilPileNo = newValue.Value;

                var selectedSoilPile = SoilPiles[newValue.Value - 1];
                SelectedZDataItems = new ObservableCollection<PileZDataItem>
                    (selectedSoilPile.ZDataItems.Select(item => item.DeepCopy()));

                OnZDataItemsChanged();

                // 切り替え先のランプを点灯
                MarkPileAsShown(newValue.Value - 1);
            }
        }

        public void SetZdataItemsAndHorizontalSoilReactionItems(int soilPileNo)
        {
            // ZDataItemsの更新
            SoilPiles[soilPileNo - 1].ZDataItems = new ObservableCollection<PileZDataItem>
                (SelectedZDataItems.Select(item => item.DeepCopy()));

            SoilPiles[soilPileNo - 1].OnZDataItemsChanged(SoilPiles[soilPileNo - 1].ZDataItems);

            // HorizontalSoilReactionsの更新
            SoilPiles[soilPileNo - 1].HorizontalSoilReactions = new ObservableCollection<HorizontalSoilReactionItem>
                (SelectedHorizontalSoilReactions.Select(horizontalSoilReaction => horizontalSoilReaction.DeepCopy()));
        }

        [RelayCommand]
        private void OnOk()
        {
            // 入力の書き換えはここから始まる。無効になるもの (沈下解析結果など) の確認も
            // ウィンドウを開くときではなく<b>ここ</b>で行う。
            // 開くだけなら入力は変わらない (保存しなければ Closing で元に戻す) ので、
            // 「見るだけ」で結果を失う旨を聞かれるのはおかしい。
            // 断られたら閉じない。閉じるとこのウィンドウでの編集がすべて消える。
            if (!_mainWindowViewModel.ConfirmSaveElementDivision()) return;

            // 現在選択中の杭のデータを保存（OKボタン押下前に確実に反映）
            if (SelectedSoilPileNo > 0 && SelectedSoilPileNo <= SoilPiles.Count)
            {
                SetZdataItemsAndHorizontalSoilReactionItems(SelectedSoilPileNo);
            }

            // 現在の SoilPiles と SoilEmbedment の内容を DeepCopy して InputModel.ElementDivision に設定
            InputModel.ElementDivision.SoilPiles = new ObservableCollection<SoilPile>(
                SoilPiles.Select(pile => pile.DeepCopy())
            );
            if (SoilEmbedment != null)
            {
                InputModel.ElementDivision.SoilEmbedment = SoilEmbedment.DeepCopy();
                InputModel.ElementDivision.DoatsuGoryokuBane = DoatsuGoryokuBane.DeepCopy();
            }

            // MainWindowViewModelのインスタンスにアクセスしてIsElementSplitをtrueに設定
            if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel mainWindowViewModel)
            {
                mainWindowViewModel.IsElementSplit = true;
            }

            IsOkPressed = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // soilZsの更新
        public void UpdateZsCollection()
        {

            var soilEmbedmentZs = new ObservableCollection<EmbedmentZDataItem>();
            if (SoilEmbedment != null && SoilEmbedment.ZDataItems != null)
            {
                for (int j = 0; j < SoilEmbedment.ZDataItems.Count; j++)
                {
                    soilEmbedmentZs.Add(new EmbedmentZDataItem
                    {
                        Z = SoilEmbedment.ZDataItems[j].Z,
                        IsChangeable = false,
                        //GroundLayerNo = SoilEmbedment.GroundNo,
                        GroundInput = InputModel.GroundsInput[SoilEmbedment.GroundNo - 1],
                        GroundDisp1 = SoilEmbedment.ZDataItems[j].GroundDisp1, // レベル1地盤変位
                        GroundDisp2 = SoilEmbedment.ZDataItems[j].GroundDisp2, // レベル2地盤変位
                        GroundDisp1L = SoilEmbedment.ZDataItems[j].GroundDisp1L, // レベル1液状化地盤変位
                        GroundDisp2L = SoilEmbedment.ZDataItems[j].GroundDisp2L, // レベル2液状化地盤変位
                    });
                }

                EmbedmentZsOriginalCollection = new ObservableCollection<EmbedmentZDataItem>(soilEmbedmentZs.Select(z => new EmbedmentZDataItem
                {
                    Z = z.Z,
                    IsChangeable = z.IsChangeable,
                    SegmentNo = z.SegmentNo,
                    //GroundLayerNo = z.GroundLayerNo,
                    GroundInput = z.GroundInput,
                    GroundDisp1 = z.GroundDisp1, // レベル1地盤変位
                    GroundDisp2 = z.GroundDisp2, // レベル2地盤変位
                    GroundDisp1L = z.GroundDisp1L, // レベル1液状化地盤変位
                    GroundDisp2L = z.GroundDisp2L, // レベル2液状化地盤変位
                }));

                EmbedmentZsCollection = new ObservableCollection<EmbedmentZDataItem>(soilEmbedmentZs.Select(z => new EmbedmentZDataItem
                {
                    Z = z.Z,
                    IsChangeable = z.IsChangeable,
                    SegmentNo = z.SegmentNo,
                    //GroundLayerNo = z.GroundLayerNo,
                    GroundInput = z.GroundInput,
                    GroundDisp1 = z.GroundDisp1, // レベル1地盤変位
                    GroundDisp2 = z.GroundDisp2, // レベル2地盤変位
                    GroundDisp1L = z.GroundDisp1L, // レベル1液状化地盤変位
                    GroundDisp2L = z.GroundDisp2L, // レベル2液状化地盤変位
                }));
                SelectedEmbedmentZ = EmbedmentZsCollection.FirstOrDefault();
            }

            // ZsCollectionの初期化
            for (int i = 0; i < SoilPiles.Count; i++)
            {
                var zs = new ObservableCollection<PileZDataItem>();
                var groundInput = SoilPiles[i].GroundInput ?? InputModel.GroundsInput[SoilPiles[i].GroundNo - 1];
                for (int j = 0; j < SoilPiles[i].ZDataItems.Count; j++)
                {
                    zs.Add(new PileZDataItem
                    {
                        Z = SoilPiles[i].ZDataItems[j].Z,
                        IsChangeable = false,
                        //GroundLayerNo = PileGroundNo,
                        GroundInput = SoilPiles[i].ZDataItems[j].GroundInput,
                        GroundDisp1 = SoilPiles[i].ZDataItems[j].GroundDisp1, // レベル1地盤変位
                        GroundDisp2 = SoilPiles[i].ZDataItems[j].GroundDisp2, // レベル2地盤変位
                        GroundDisp1L = SoilPiles[i].ZDataItems[j].GroundDisp1L, // レベル1液状化地盤変位
                        GroundDisp2L = SoilPiles[i].ZDataItems[j].GroundDisp2L, // レベル2液状化地盤変位
                    });
                }
                //ZsOriginalCollection.Add(zs);
                ZsCollection.Add(zs);
            }
            SelectedSoilPileNo = 1;

            SelectedZDataItems = new ObservableCollection<PileZDataItem>
                    (ZsCollection[SelectedSoilPileNo - 1].Select(item => item.DeepCopy()));

            // 初期選択を設定
            if (SelectedZDataItems.Count > 0)
            {
                SelectedZDataItem = SelectedZDataItems[0];
            }

            OnZDataItemsChanged();
        }

        // PileGroundNo PileBodyNo PileBodyNo Z SelectedZDataItems PileBottomAltitude更新メソッド //
        public void UpdateSelectedSoilPileProperties()
        {
            if (SoilPiles == null || SoilPiles.Count == 0 || SelectedSoilPileNo < 1 || SelectedSoilPileNo > SoilPiles.Count)
            { return; }

            var selectedSoilPile = SoilPiles[SelectedSoilPileNo - 1];
            if (selectedSoilPile == null)
            { return; }

            // PileGroundNoの更新
            PileGroundNo = selectedSoilPile.GroundNo;

            // PileBodyNoの更新
            PileBodyNo = selectedSoilPile.PileBodyNo;

            // Zの更新
            Z = selectedSoilPile.Z;

            // PileBottomAltitudeの更新
            PileBottomAltitude = selectedSoilPile.PileBottomAltitude;
        }

        public void UpdateSoilEmbedmentProperties()
        {
            DrawSoilEmbedment();
        }

        // 土層-根入分割データグリッドの変更イベントハンドラ
        public void OnDataGridEmbedmentZsChanged()
        {
            SetDoatsuGoryokuBane();
            DrawSoilEmbedment();
        }

        // 土層-杭分割データグリッドの変更イベントハンドラ
        public void OnDataGridZsChanged()
        {
            SetHorizontalSoilReaction();
            DrawShapes();
        }

        // ξ、R/Bの変更イベントハンドラ
        public void OnXiROnChanged()
        {
            SetHorizontalSoilReaction();
            DrawShapes();
        }

        public void DrawShapes()
        {
            GroundInput groundInput = SoilPiles[SelectedSoilPileNo - 1].GroundInput;
            double pileTopAltitude = Z;
            if (SoilPiles == null) return;

            List<double> zs = [];
            foreach (var selectedZDataItem in SelectedZDataItems)
            {
                zs.Add(selectedZDataItem.Z);
            }

            double? selectedZ = SelectedZDataItem?.Z; // 選択節点

            ShapeDrawer.DrawPileElevation(
                Canvas,
                SoilPiles[SelectedSoilPileNo - 1].PileBodySegments,
                SoilPiles[SelectedSoilPileNo - 1].D * 1000,
                InputModel.PileBodies[PileBodyNo - 1].InsituPileToeHeight,
                InputModel.PileBodies[PileBodyNo - 1].InsituPileToeAngle,
                InputModel.PileBodies[PileBodyNo - 1].PrecastConcretePileToeHeightRatio,
                SoilPiles[SelectedSoilPileNo - 1].PileConstructionType,
                pileTopAltitude,
                groundInput,
                true,
                zs,
                selectedZ,
                smartMagnumLL: InputModel.PileBodies[PileBodyNo - 1].SmartMagnumLL,
                smartMagnumDes: InputModel.PileBodies[PileBodyNo - 1].SmartMagnumDes,
                smartMagnumWingLength: InputModel.PileBodies[PileBodyNo - 1].SmartMagnumWingLength,
                hybridE: InputModel.PileBodies[PileBodyNo - 1].HybridExpansionRatio,
                hybridEs: InputModel.PileBodies[PileBodyNo - 1].HybridExcavationRatio,
                hybridLu: InputModel.PileBodies[PileBodyNo - 1].HybridPileBelowLength);

            // 現在表示した土層-杭セットのランプを点灯
            MarkPileAsShown(SelectedSoilPileNo - 1);

        }

        public void DrawSoilEmbedment()
        {
            // 前提チェック（ヌル安全）
            if (SoilEmbedment == null) return;
            if (InputModel?.GroundsInput == null) return;
            if (SoilEmbedment.GroundNo <= 0 || SoilEmbedment.GroundNo > InputModel.GroundsInput.Count) return;
            if (DoatsuGoryokuBane == null) return;
            if (EmbedmentZsCollection == null || EmbedmentZsCollection.Count == 0) return;
            if (CanvasEmbedment == null) return;

            // 安全に GroundInput を取得
            GroundInput groundInput;
            try
            {
                groundInput = InputModel.GroundsInput[SoilEmbedment.GroundNo - 1];
            }
            catch
            {
                // 範囲外や別の問題があれば描画中止
                return;
            }

            // Z 座標列を安全に作成
            var zs = EmbedmentZsCollection
                .Where(e => e != null)
                .Select(e => e.Z)
                .ToList();

            if (zs.Count == 0) return;

            double? selectedZ = SelectedEmbedmentZ?.Z; // 選択節点

            // SelectedDirection のフォールバック
            var direction = string.IsNullOrEmpty(SelectedDirection) ? "X" : SelectedDirection;

            ShapeDrawer.DrawEmbedmentElevation(
                CanvasEmbedment,
                DoatsuGoryokuBane,
                groundInput,
                direction,
                zs,
                selectedZ);

            // 根入れ表示でランプを点灯（単一扱い）
            MarkEmbedmentAsShown(0);
        }

        // 追加: ElementDivisionViewModel クラス内の適切な位置に貼ってください
        public void SelectSoilPileIndex(int index)
        {
            if (SoilPiles == null || SoilPiles.Count == 0) return;
            if (index < 0 || index >= SoilPiles.Count) return;

            SelectedSoilPileNo = index + 1;

            if (ZsCollection != null && index >= 0 && index < ZsCollection.Count)
            {
                SelectedZDataItems = new ObservableCollection<PileZDataItem>(
                    ZsCollection[index].Select(item => item.DeepCopy())
                );
                OnZDataItemsChanged();
            }

            DrawShapes();
        }

        public void SelectEmbedmentIndex(int index)
        {
            if (EmbedmentZsCollection != null && index >= 0 && index < EmbedmentZsCollection.Count)
            {
                SelectedEmbedmentZ = EmbedmentZsCollection[index];
            }
            DrawSoilEmbedment();
        }
    }
}
