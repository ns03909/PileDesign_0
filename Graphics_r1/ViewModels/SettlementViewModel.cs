using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics.LinearAlgebra;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    public partial class SettlementViewModel : ObservableObject
    {
        public SettlementWindow SettlementWindowInstance { get; set; } // SettlementWindow のインスタンスを保持するプロパティを追加

        private readonly UndoManager _undoManager = new();
        public UndoManager UndoManager => _undoManager;

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // 地盤杭セット
        [ObservableProperty]
        private ObservableCollection<SoilPile> _soilPiles;

        // 選択中の地盤杭セット
        private SoilPile _soilPile;
        public SoilPile SoilPile
        {
            get => _soilPile;
            private set => SetProperty(ref _soilPile, value);
        }

        private PileBodyInput _pileBody;
        public PileBodyInput PileBody
        {
            get => _pileBody;
            private set => SetProperty(ref _pileBody, value);
        }

        // 杭先端閉塞率を使用する工法かどうか
        public bool UsesPileToeEta => SoilPile?.PileConstructionType == "回転貫入杭"
                                    || SoilPile?.PileConstructionType == "打込み杭";

        // 沈下検討用杭先端径 (m単位で表示・編集)
        public double SettlePileToeDiaM
        {
            get => PileBody?.SettlePileToeDia / 1000.0 ?? 0;
            set
            {
                if (PileBody != null)
                {
                    double valueInMm = value * 1000.0;
                    PileBody.SettlePileToeDia = valueInMm;
                    if (SoilPile != null) SoilPile.Dp = valueInMm;
                    OnPropertyChanged(nameof(SettlePileToeDiaM));
                    DrawShapes();
                    _ = ExecuteAnalysis();
                }
            }
        }

        // 地盤杭体数+1リスト
        private ObservableCollection<int> _soilPilesCountList;
        public ObservableCollection<int> SoilPilesCountList
        {
            get => _soilPilesCountList;
            private set => SetProperty(ref _soilPilesCountList, value);
        }

        public void UpdateSoilPilesCountList()
        {
            if (SoilPiles == null) return;
            var countList = new ObservableCollection<int>(Enumerable.Range(1, SoilPiles.Count));
            SoilPilesCountList = countList;
        }

        // ランプ初期化
        private void InitializeLamps()
        {
            SoilPileLampStates.Clear();
            for (int i = 0; i < SoilPiles.Count; i++)
            {
                var lamp = new PileDesign.Models.LampState(i, false);
                lamp.PropertyChanged += OnLampPropertyChanged;
                SoilPileLampStates.Add(lamp);
            }
        }

        // ランプPropertyChangedハンドラ
        private void OnLampPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PileDesign.Models.LampState.IsOn))
            {
                OnPropertyChanged(nameof(HasAnyRedLamp));
            }
        }

        // 指定SoilPileのランプを点灯
        private void MarkPileAsAnalyzed(int pileIndex)
        {
            if (pileIndex < 0 || SoilPileLampStates == null || pileIndex >= SoilPileLampStates.Count) return;
            if (!SoilPileLampStates[pileIndex].IsOn)
            {
                SoilPileLampStates[pileIndex].IsOn = true;
            }
        }

        // ランプクリック時に該当SoilPileを選択（ユーザー確認＝ランプ点灯）
        public void SelectSoilPileIndex(int index)
        {
            if (index < 0 || index >= SoilPiles.Count) return;
            SoilPileNo = index + 1;
            SoilPile = SoilPiles[index];
            PileBody = InputModel.PileBodies[SoilPile.PileBodyNo - 1];
            OnPropertyChanged(nameof(UsesPileToeEta));
            OnPropertyChanged(nameof(SettlePileToeDiaM));
            AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
            _ = ExecuteAnalysis();
            DrawShapes();
            // ユーザーが確認したのでランプを点灯
            MarkPileAsAnalyzed(index);
        }

        // ComboBox選択変更時のランプ点灯（ユーザー確認）
        public void MarkCurrentPileAsConfirmed()
        {
            MarkPileAsAnalyzed(SoilPileNo - 1);
        }

        // 全SoilPile一括解析（ランプは点灯しない＝ユーザー確認待ち）
        public void AnalyzeAllSoilPiles()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                for (int i = 0; i < SoilPiles.Count; i++)
                {
                    var soilPile = SoilPiles[i];

                    // 各SoilPileに対してVerticalLoadTransferMethodを作成・解析
                    var vtm = new VerticalLoadTransferMethod(InputModel, soilPile, SelectedAnalysisMode);

                    // SoilPileに計算結果を保存（ViewModel内コピー）
                    soilPile.LoadDisplacements = vtm.LoadDisplacements;
                    soilPile.LoadDisplacementsLimit = vtm.LoadDisplacementsLimit;
                    soilPile.NodeDisplacements = vtm.Ds;
                    soilPile.NodeReactions = vtm.Rs;

                    // 元のElementDivision.SoilPilesにも結果を反映
                    var originalSoilPiles = InputModel.ElementDivision?.SoilPiles;
                    if (originalSoilPiles != null && i < originalSoilPiles.Count)
                    {
                        originalSoilPiles[i].LoadDisplacements = vtm.LoadDisplacements;
                        originalSoilPiles[i].LoadDisplacementsLimit = vtm.LoadDisplacementsLimit;
                        originalSoilPiles[i].NodeDisplacements = vtm.Ds;
                        originalSoilPiles[i].NodeReactions = vtm.Rs;
                    }

                    // 該当するPileLayoutItemの沈下量を計算
                    int soilPileNo = i + 1;
                    foreach (var pileLayoutItem in InputModel.PileLayoutItems)
                    {
                        if (pileLayoutItem.SoilPileAltNo == soilPileNo)
                        {
                            double force = pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional;
                            var settlementVector = vtm.GetDisplacementForGivenLoad(force);
                            if (settlementVector != null)
                            {
                                pileLayoutItem.SinglePileSettlementVL = settlementVector[0];
                            }

                            for (int j = 0; j < pileLayoutItem.AxialForceLevel1s.Count; j++)
                            {
                                var axialForce = pileLayoutItem.AxialForceLevel1s[j];
                                settlementVector = vtm.GetDisplacementForGivenLoad(axialForce);
                                if (settlementVector != null)
                                {
                                    pileLayoutItem.SinglePileSettlementLevel1s[j] = settlementVector[0];
                                }
                            }

                            for (int j = 0; j < pileLayoutItem.AxialForceLevel2s.Count; j++)
                            {
                                var axialForce = pileLayoutItem.AxialForceLevel2s[j];
                                settlementVector = vtm.GetDisplacementForGivenLoad(axialForce);
                                if (settlementVector != null)
                                {
                                    pileLayoutItem.SinglePileSettlementLevel2s[j] = settlementVector[0];
                                }
                            }
                        }
                    }
                }

                // 現在選択中のSoilPileのVerticalLoadTransferMethodも更新
                VerticalLoadTransferMethod = new VerticalLoadTransferMethod(InputModel, SoilPile, SelectedAnalysisMode);
                UpdateSettlementChart();
                UpdateCircumstanceSeries();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // ランプ状態（各SoilPileの解析済み/未済を表す）
        public ObservableCollection<PileDesign.Models.LampState> SoilPileLampStates { get; } = new();

        // 赤ランプが1つでもあるかどうか（IsOn == false が赤）
        public bool HasAnyRedLamp
        {
            get => SoilPileLampStates != null && SoilPileLampStates.Any(lamp => !lamp.IsOn);
        }

        // 警告メッセージ
        public string WarningMessage => "すべての土層-杭セットの沈下性状を確認してください。";

        // 杭体番号
        [ObservableProperty]
        private int _soilPileNo = 1;

        public ObservableCollection<double> Fs { get; set; } = [];
        public ObservableCollection<double> Ds { get; set; } = [];

        // 選択杭区間
        [ObservableProperty]
        private PileCircumVertical _selectedPileCircumstanceVertical;

        partial void OnSelectedPileCircumstanceVerticalChanged(PileCircumVertical value) => DrawShapes();

        public VerticalLoadTransferMethod.LoadDisplacement SelectedLoadDisplacement { get; set; }

        [ObservableProperty]
        private VerticalLoadTransferMethod _verticalLoadTransferMethod;

        // 解析制御モード選択（荷重制御法をデフォルトに）
        [ObservableProperty]
        private AnalysisControlMode _selectedAnalysisMode = AnalysisControlMode.LoadControl;

        // 解析制御モードリスト（ComboBox用）
        public AnalysisControlMode[] AnalysisModes { get; } =
            [AnalysisControlMode.LoadControl, AnalysisControlMode.DisplacementControl];

        // 解析制御モードが変位制御法かどうか
        public bool IsDisplacementControl
        {
            get => SelectedAnalysisMode == AnalysisControlMode.DisplacementControl;
            set
            {
                SelectedAnalysisMode = value ? AnalysisControlMode.DisplacementControl : AnalysisControlMode.LoadControl;
                OnPropertyChanged(nameof(IsDisplacementControl));
            }
        }

        // xamlフィールド
        public Canvas Canvas { get; set; }

        // xaml
        public ComboBox ComboBoxPileBodyNo { get; set; }
        public TextBox TextBoxPileBodyRef { get; set; }
        public TextBox TextBoxSettlePileToeDia { get; set; }
        public TextBox TextBoxPileTipNonPermeability { get; set; }
        public TextBox TextBoxSettleAlpha { get; set; }
        public TextBox TextBoxSettleN { get; set; }

        [ObservableProperty]
        private int _selectedTabIndex;

        public ComboBox ComboBoxPileTopType { get; set; }
        public ComboBox ComboBoxPresetSettlementParameters { get; set; }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        public static Crosshair MyCrosshair_PileToe { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_PileToe;

        public static Crosshair MyCrosshair_PileCircumstance { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_PileCircumstance;

        public static Crosshair MyCrosshair_PileToeSettlement { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_PileToeSettlement;

        public static Crosshair MyCrosshair_PileTopSettlement { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_PileTopSettlement;

        [ObservableProperty]
        private bool _isSettlementLegendVisible = true;

        partial void OnIsSettlementLegendVisibleChanged(bool value) => ApplyLegendVisibility();

        private void ApplyLegendVisibility()
        {
            var win = SettlementWindowInstance;
            if (win == null) return;
            var wpf = win.wpfPlotSettlement;
            var wpfToe = win.wpfPlotSettlementToe;
            if (wpf != null)
            {
                wpf.Plot.Legend.IsVisible = IsSettlementLegendVisible;
                wpf.Refresh();
            }
            if (wpfToe != null)
            {
                wpfToe.Plot.Legend.IsVisible = IsSettlementLegendVisible;
                wpfToe.Refresh();
            }
        }

        // コンストラクタ
        public SettlementViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            // 元コレクションを先にスナップショット化（この時点では DeepCopy は呼ばれない）
            var soilPilesSnapshot = InputModel.ElementDivision.SoilPiles?.ToList() ?? [];

            // スナップショットを列挙して DeepCopy（元コレクションはもはや触らない）
            SoilPiles = new ObservableCollection<SoilPile>(soilPilesSnapshot.Select(pile => pile.DeepCopy()));

            UpdateSoilPilesCountList();

            // ランプ初期化（各SoilPileに対して赤ランプで作成）
            InitializeLamps();

            // 件数チェックして参照を安全に取得
            SoilPile = SoilPiles.Count > 0 ? SoilPiles[Math.Clamp(SoilPileNo - 1, 0, SoilPiles.Count - 1)] : throw new InvalidOperationException("SoilPiles が空です。");

            PileBody = InputModel.PileBodies[SoilPile.PileBodyNo - 1];

            // 先端平均N値が0の場合に警告メッセージを表示
            if (SoilPile.PileToeNValue == 0)
            {
                PileDesign.Services.MessageService.Show(
                    "杭先端平均N値が0です。地盤データや杭の設定を確認してください。",
                    "警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }

            // xaml
            ComboBoxPileBodyNo = new();
            TextBoxPileBodyRef = new();
            TextBoxSettlePileToeDia = new();
            TextBoxPileTipNonPermeability = new();
            TextBoxSettleAlpha = new();
            TextBoxSettleN = new();

            AddComponent(PileBody.SettleAlpha, PileBody.SettleN);

            UpdateSettlementChart();
            UpdateCircumstanceSeries();
            DrawShapes();

            // 初期状態をUndoManagerに保存（ここもスナップショット→DeepCopyで安全に）
            UndoManager.SaveState(new ObservableCollection<SoilPile>(SoilPiles.Select(p => p.DeepCopy())));
        }

        [RelayCommand]
        private void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                _undoManager.SaveState(new ObservableCollection<SoilPile>(SoilPiles.Select(p => p.DeepCopy())));
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is ObservableCollection<SoilPile> state)
            {
                SoilPiles = new ObservableCollection<SoilPile>(state.Select(p => p.DeepCopy()));
                UpdateSoilPilesCountList();
                SoilPile = SoilPiles[Math.Max(0, SoilPileNo - 1)];
                DrawShapes();
            }
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is ObservableCollection<SoilPile> state)
            {
                SoilPiles = new ObservableCollection<SoilPile>(state.Select(p => p.DeepCopy()));
                UpdateSoilPilesCountList();
                SoilPile = SoilPiles[Math.Max(0, SoilPileNo - 1)];
                DrawShapes();
            }
        }

        [RelayCommand]
        private void OnOk()
        {
            // 逆操作: ViewModelのSoilPilesをモデルに反映
            InputModel.ElementDivision.SoilPiles = new ObservableCollection<SoilPile>(
                SoilPiles.Select(pile => pile.DeepCopy())
            );
            // MainWindowViewModelのインスタンスにアクセスしてIsElementSplitをtrueに設定
            if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel mainWindowViewModel)
            {
                mainWindowViewModel.IsVerticalAnalysisDone = true;
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            //// プロパティを前回の保存時の値に戻す
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // Masing 履歴解析ウィンドウを開く
        [RelayCommand]
        private void OpenMasingHysteresis()
        {
            if (VerticalLoadTransferMethod == null
                || VerticalLoadTransferMethod.LoadDisplacements == null
                || VerticalLoadTransferMethod.LoadDisplacements.Count < 2)
            {
                MessageService.ShowWarning("骨格曲線がまだ算出されていません。先に荷重-沈下解析を実行してください。");
                return;
            }

            // 初期値は現在選択中の SoilPile の代表軸力 (VL) を常時荷重に設定
            double initialConst = 0;
            try
            {
                if (InputModel?.PileLayoutItems != null)
                {
                    var firstMatch = InputModel.PileLayoutItems
                        .FirstOrDefault(p => p.SoilPileAltNo == SoilPileNo);
                    if (firstMatch != null)
                        initialConst = firstMatch.AxialForceVL0 + firstMatch.AxialForceVLAdditional;
                }
            }
            catch { /* 取得できなければ 0 のまま */ }

            var vm = new MasingHysteresisViewModel(VerticalLoadTransferMethod, initialConst, 0);
            var win = new MasingHysteresisWindow(vm)
            {
                Owner = SettlementWindowInstance
            };
            win.Show();
        }

        // 周面抵抗考慮フラグ変更時の支持力再計算
        public void RecalculateResistances()
        {
            SoilPile.CalculateResistances();
            OnPropertyChanged(nameof(SoilPile));
        }

        // 荷重沈下解析実行
        [RelayCommand]
        public async Task ExecuteAnalysis()
        {
            // 入力データの整合性ゲート
            if (!PileDesign.Models.CheckInputData.ValidateForAnalysis(InputModel, "単杭沈下解析"))
                return;

            try
            {
                // 砂時計カーソルを表示
                Mouse.OverrideCursor = Cursors.Wait;

                // バックグラウンドで解析実行（UIスレッドブロッキング防止）
                var inputModelRef = InputModel;
                var soilPileRef = SoilPile;
                var mode = SelectedAnalysisMode;

                var vtm = await Task.Run(() => new VerticalLoadTransferMethod(inputModelRef, soilPileRef, mode));
                VerticalLoadTransferMethod = vtm;

                // UIスレッドでチャートと結果を更新
                UpdateSettlementChart();
                UpdateCircumstanceSeries();

                SoilPile.LoadDisplacements = VerticalLoadTransferMethod.LoadDisplacements;
                SoilPile.LoadDisplacementsLimit = VerticalLoadTransferMethod.LoadDisplacementsLimit;
                SoilPile.NodeDisplacements = VerticalLoadTransferMethod.Ds;
                SoilPile.NodeReactions = VerticalLoadTransferMethod.Rs;

                // 元のElementDivision.SoilPilesにも結果を反映
                int spIdx = SoilPileNo - 1;
                var originalSoilPiles = InputModel.ElementDivision?.SoilPiles;
                if (originalSoilPiles != null && spIdx >= 0 && spIdx < originalSoilPiles.Count)
                {
                    originalSoilPiles[spIdx].LoadDisplacements = VerticalLoadTransferMethod.LoadDisplacements;
                    originalSoilPiles[spIdx].LoadDisplacementsLimit = VerticalLoadTransferMethod.LoadDisplacementsLimit;
                    originalSoilPiles[spIdx].NodeDisplacements = VerticalLoadTransferMethod.Ds;
                    originalSoilPiles[spIdx].NodeReactions = VerticalLoadTransferMethod.Rs;
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // 沈下曲線表の選択変更時にチャートを更新（選択位置を強調表示）
        public void UpdateSettlementChartSelection()
        {
            // VerticalLoadTransferMethodがnullの場合は何もしない
            if (VerticalLoadTransferMethod == null)
                return;

            // チャートを再描画して選択位置を強調表示
            UpdateSettlementChart();
        }

        private void UpdateSettlementChart()
        {
            if (VerticalLoadTransferMethod == null)
                return;

            ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> loadDisplacements = VerticalLoadTransferMethod.LoadDisplacements;
            ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> loadDisplacementsLimit = VerticalLoadTransferMethod.LoadDisplacementsLimit;

            if (SettlementWindowInstance == null)
            { return; }

            List<double> pileLoadsVL = [];
            List<double> settlementVL = [];
            List<double> pileLoadsLevel1 = [];
            List<double> settlementLevel1 = [];
            List<double> pileLoadsLevel2 = [];
            List<double> settlementLevel2 = [];

            var wpf = SettlementWindowInstance.wpfPlotSettlement;
            var wpfToe = SettlementWindowInstance.wpfPlotSettlementToe;
            wpf.Plot.Clear();
            wpfToe.Plot.Clear();

            // Rt_ULS, Rt_DLS, Rt_SLSは負の値で格納されている
            List<double> allowableLoads = [SoilPile.Rt_ULS, SoilPile.Rt_DLS, SoilPile.Rt_SLS, SoilPile.R_SLS, SoilPile.R_DLS, SoilPile.R_ULS];
            List<double> settlements = [];
            List<double> settlementsToe = [];
            List<double> xValues = [];
            List<double> xToeValues = [];
            List<double> yValues = [];
            List<double> yValues2 = [];
            List<double> yValues3 = [];
            List<double> yValues4 = [];
            List<double> yValues5 = [];

            foreach (var displacement in loadDisplacements)
            {
                yValues.Add(displacement.PileTopLoad);
                xValues.Add(displacement.DD0s);
                xToeValues.Add(displacement.DDns);
                yValues2.Add(displacement.RzToe);
                yValues3.Add(displacement.RzCircum);
                yValues4.Add(displacement.Weight);
                yValues5.Add(displacement.PileTopLoad);

                if (allowableLoads.Any(load => Math.Abs(load - displacement.PileTopLoad) <= 0.01))
                {
                    settlements.Add(displacement.DD0s);
                    settlementsToe.Add(displacement.DDns);
                }
            }

            Color verticalLineColor = new(0, 0, 0, 64);
            foreach (var settlement in settlements)
            {
                wpf.Plot.Add.VerticalLine(settlement, 1, verticalLineColor);
            }

            foreach (var settlementToe in settlementsToe)
            {
                wpfToe.Plot.Add.VerticalLine(settlementToe, 1, verticalLineColor);
            }

            if (SelectedLoadDisplacement != null)
            {
                // 選択位置を桃赤の細い縦線で強調表示
                var selectedColor = Color.FromSKColor(NikkenSKColor.PaleRed);

                // 杭頭沈下グラフ: 垂直線（沈下量）のみ
                wpf.Plot.Add.VerticalLine(SelectedLoadDisplacement.D0s, 1, selectedColor);

                // 杭先端沈下グラフ: 垂直線（沈下量）のみ
                wpfToe.Plot.Add.VerticalLine(SelectedLoadDisplacement.Dns, 1, selectedColor);
            }

            AddScatterPlotAllowable(wpf, [.. settlements], [.. allowableLoads], verticalLineColor);

            // 限界状態の予約色 (DeepBlue=使用 / Green=損傷 / PaleRed=終局) と衝突しないよう
            // ライン系列は SkyBlue / LineOrange / Yellow / LineSlate を使用
            AddScatterPlot(wpf, [.. xValues], [.. yValues5], NikkenSKColor.SkyBlue,    "杭頭荷重");
            AddScatterPlot(wpf, [.. xValues], [.. yValues2], NikkenSKColor.LineOrange, "杭先端支持力");
            AddScatterPlot(wpf, [.. xValues], [.. yValues3], NikkenSKColor.Yellow,     "杭周面抵抗力");
            AddScatterPlot(wpf, [.. xValues], [.. yValues4], NikkenSKColor.LineSlate,  "杭自重");


            AddScatterPlotAllowable(wpfToe, [.. settlementsToe], [.. allowableLoads], verticalLineColor);

            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues5], NikkenSKColor.SkyBlue,    "杭頭荷重");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues2], NikkenSKColor.LineOrange, "杭先端支持力");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues3], NikkenSKColor.Yellow,     "杭周面抵抗力");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues4], NikkenSKColor.LineSlate,  "杭自重");

            //InputModel inputModel = InputModel.Instance;
            List<double> forcesVL = [];
            List<double> settlementsVL = [];
            List<double> forcesLevel1 = [];
            List<double> settlementsLevel1 = [];
            List<double> forcesLevel2 = [];
            List<double> settlementsLevel2 = [];

            // デバッグ: VL値の確認

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {

                if (pileLayoutItem.SoilPileAltNo == SoilPileNo)
                {
                    int no = pileLayoutItem.No;
                    double force = pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional;

                    Vector<double>? settlementVector = VerticalLoadTransferMethod.GetDisplacementForGivenLoad(force);
                    if (settlementVector != null)
                    {
                        pileLayoutItem.SinglePileSettlementVL = settlementVector[0];
                        forcesVL.Add(force);
                        settlementsVL.Add(settlementVector[0] * 1000);
                    }

                    for (int i = 0; i < pileLayoutItem.AxialForceLevel1s.Count; i++)
                    {
                        var axialForce = pileLayoutItem.AxialForceLevel1s[i];
                        settlementVector = VerticalLoadTransferMethod.GetDisplacementForGivenLoad(axialForce);
                        if (settlementVector != null)
                        {
                            pileLayoutItem.SinglePileSettlementLevel1s[i] = settlementVector[0];
                            forcesLevel1.Add(axialForce);
                            settlementsLevel1.Add(settlementVector[0] * 1000);
                        }
                    }

                    for (int i = 0; i < pileLayoutItem.AxialForceLevel2s.Count; i++)
                    {
                        var axialForce = pileLayoutItem.AxialForceLevel2s[i];
                        settlementVector = VerticalLoadTransferMethod.GetDisplacementForGivenLoad(axialForce);
                        if (settlementVector != null)
                        {
                            pileLayoutItem.SinglePileSettlementLevel2s[i] = settlementVector[0];
                            forcesLevel2.Add(axialForce);
                            settlementsLevel2.Add(settlementVector[0] * 1000);
                        }
                    }
                }
            }

            // 限界状態の予約色: VL=使用限界 (DeepBlue) / レベル1=損傷限界 (Green) / レベル2=終局限界 (PaleRed)
            AddAxialForceScatterPlot(wpf, [.. settlementsVL],     [.. forcesVL],     NikkenSKColor.DeepBlue, "VL",     MarkerShape.OpenCircle);
            AddAxialForceScatterPlot(wpf, [.. settlementsLevel1], [.. forcesLevel1], NikkenSKColor.Green,    "レベル1", MarkerShape.OpenTriangleDown);
            AddAxialForceScatterPlot(wpf, [.. settlementsLevel2], [.. forcesLevel2], NikkenSKColor.PaleRed,  "レベル2", MarkerShape.OpenDiamond);

            ConfigurePlot(wpf, "荷重-杭頭沈下曲線", "杭頭沈下量(mm)", "荷重 (kN)");
            ConfigurePlot(wpfToe, "荷重-杭先端沈下曲線", "杭先端沈下量(mm)", "荷重 (kN)");

            wpf.Plot.Legend.IsVisible = IsSettlementLegendVisible;
            wpfToe.Plot.Legend.IsVisible = IsSettlementLegendVisible;

            // クロスヘアの初期化
            MyCrosshair_PileTopSettlement = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileTopSettlement", "杭頭沈下量(mm)", "荷重(kN)");

            // クロスヘアの初期化
            MyCrosshair_PileToeSettlement = PlotHelper.InitCrosshair(wpfToe, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpfToe.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileToeSettlement", "杭先端沈下量(mm)", "荷重(kN)");
        }

        private static void AddScatterPlotAllowable(WpfPlot wpfPlot, double[] xValues, double[] yValues, Color color)
        {
            var scatter = wpfPlot.Plot.Add.Scatter(xValues, yValues);
            scatter.Color = color;
            scatter.LineWidth = 0;
            scatter.MarkerLineColor = color;
            scatter.MarkerColor = color;
            scatter.MarkerFillColor = color;
            scatter.MarkerSize = 8;
        }

        private static void AddScatterPlot(WpfPlot wpfPlot, double[] xValues, double[] yValues, SKColor color, string legend)
        {
            var scatter = wpfPlot.Plot.Add.Scatter(xValues, yValues);
            scatter.Color = Color.FromSKColor(color);
            scatter.LineWidth = 2;
            scatter.MarkerShape = ScottPlot.MarkerShape.None;
            scatter.LegendText = legend;
        }

        private static void AddAxialForceScatterPlot(WpfPlot wpfPlot, double[] xValues, double[] yValues, SKColor color, string legend, MarkerShape markerShape)
        {
            var scatter = wpfPlot.Plot.Add.Scatter(xValues, yValues);
            scatter.Color = Color.FromSKColor(color);
            scatter.LineWidth = 0;
            scatter.MarkerSize = 12;
            scatter.MarkerShape = markerShape;
            scatter.LegendText = legend;
        }

        private static void ConfigurePlot(WpfPlot wpfPlot, string title, string xLabel, string yLabel)
        {
            wpfPlot.Plot.Axes.Title.Label.Text = title;
            wpfPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            wpfPlot.Plot.Axes.Bottom.Label.Text = xLabel;
            wpfPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            wpfPlot.Plot.Axes.Left.Label.Text = yLabel;
            wpfPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpfPlot.Plot.Legend.FontName = Fonts.Detect(yLabel);

            Color grayColor = new(128, 128, 128, 255);
            wpfPlot.Plot.Add.VerticalLine(0, 1, grayColor);
            wpfPlot.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpfPlot.Plot.Axes.AutoScale();
            wpfPlot.Plot.Axes.AutoScaleExpandX();
            wpfPlot.Plot.Axes.AutoScaleExpandY();

            wpfPlot.Refresh();
        }

        [RelayCommand]
        public void OnPileBodyLostFocus(object sender)
        {
            if (sender is TextBox textBox)
            {

                if (textBox.Name == "TextBoxPileBodyRef")
                {
                    PileBody.PileBodyRef = textBox.Text;
                }

                else if (textBox.Name == "TextBoxPileTipNonPermeability" && double.TryParse(textBox.Text, out double tipNonPermeability))
                {
                    PileBody.TipNonPermability = tipNonPermeability;
                    // 閉塞率変更時に解析実行
                    _ = ExecuteAnalysis();
                    return;
                }

                else if (textBox.Name == "TextBoxPileToeDiaD" && double.TryParse(textBox.Text, out double pileToeDiaD))
                {
                    // 杭先端径D(m)を更新
                    SoilPile.D = pileToeDiaD;
                    // PileBodyのPileToeDia(mm)も同期
                    PileBody.PileToeDia = pileToeDiaD * 1000.0;
                    // Apが変わるのでRpuの計算に反映される（プロパティで自動計算）
                    // 沈下検討用極限先端支持力度も更新（デフォルトでQpuと同じ）
                    SoilPile.SettleQpu = SoilPile.Qpu;
                    OnPropertyChanged(nameof(SoilPile));
                    DrawShapes();
                    // 支持力の再計算と解析実行
                    _ = ExecuteAnalysis();
                    return;
                }

                else if (textBox.Name == "TextBoxSettleQpu" && double.TryParse(textBox.Text, out double settleQpu))
                {
                    // 沈下検討用極限先端支持力度を更新
                    SoilPile.SettleQpu = settleQpu;
                    // 沈下グラフを更新
                    _ = ExecuteAnalysis();
                    return;
                }

                else if (textBox.Name == "TextBoxSettleAlpha" && double.TryParse(textBox.Text, out double settleAlpha))
                {
                    PileBody.SettleAlpha = settleAlpha;
                    ComboBoxPresetSettlementParameters.SelectedIndex = -1;
                    if (SoilPile != null) SoilPile.IsSettlementPresetInitialized = true;
                    // チャート要素更新と解析実行
                    AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
                    _ = ExecuteAnalysis();
                    return;
                }

                else if (textBox.Name == "TextBoxSettleN" && double.TryParse(textBox.Text, out double settleN))
                {
                    PileBody.SettleN = settleN;
                    ComboBoxPresetSettlementParameters.SelectedIndex = -1;
                    if (SoilPile != null) SoilPile.IsSettlementPresetInitialized = true;
                    // チャート要素更新と解析実行
                    AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
                    _ = ExecuteAnalysis();
                    return;
                }

                //チャート要素クリアコマンド
                AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
            }

            if (sender is string text)
            {
                if (double.TryParse(text, out double value))
                {
                    bool parameterChanged = false;
                    if (text == TextBoxSettleAlpha.Text)
                    {
                        PileBody.SettleAlpha = value;
                        parameterChanged = true;
                    }
                    else if (text == TextBoxSettleN.Text)
                    {
                        PileBody.SettleN = value;
                        parameterChanged = true;
                    }

                    // チャート要素クリアコマンド
                    AddComponent(PileBody.SettleAlpha, PileBody.SettleN);

                    // パラメータ変更時に自動で解析実行
                    if (parameterChanged)
                    {
                        _ = ExecuteAnalysis();
                    }
                }
            }
        }

        [RelayCommand]
        public static void OnTextBoxGotFocus(object parameter)
        {
            if (parameter is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        [RelayCommand]
        public static void OnTextBoxPreviewMouseLeftButtonDown(object parameter)
        {
            if (parameter is TextBox textBox)
            {
                if (!textBox.IsKeyboardFocusWithin)
                {
                    textBox.Focus();
                    // マウスクリックイベントの処理をここで完了させる
                    if (Mouse.PrimaryDevice.LeftButton == MouseButtonState.Pressed)
                    {
                        //MouseButtonEventArgs e = new(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        //{
                        //    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                        //    Source = textBox,
                        //    Handled = true
                        //};
                    }
                }
            }
        }

        [RelayCommand]
        public static void OnTextBoxLostFocus(object sender)
        {
        }

        [RelayCommand]
        private void OnPresetSettlementParametersChanged(object sender)
        {
            // senderは選択されたプリセットパラメータの文字列
            var selectedPresetParameter = sender as string;
            if (string.IsNullOrEmpty(selectedPresetParameter)) return;

            foreach (PileBodyInput.PileTipSettlementPresetParameter parameter in InputModel.PileBodies[SoilPile.PileBodyNo - 1].PileTipSettlementPresetParameters)
            {
                if (selectedPresetParameter.Contains(parameter.Name) && selectedPresetParameter.Contains(parameter.SoilType))
                {
                    InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha = parameter.Alpha;
                    InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN = parameter.N;
                    PileBody.SettleAlphaString = parameter.Alpha.ToString();
                    PileBody.SettleNString = parameter.N.ToString();
                    break;
                }
            }
            // ユーザーが手動で選択したので初期化済みマーク
            if (SoilPile == null) return;
            SoilPile.IsSettlementPresetInitialized = true;
            AddComponent(InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha, InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN);
            // プリセット変更時に自動で解析実行
            _ = ExecuteAnalysis();
        }

        /// <summary>
        /// 杭工法と杭先端土質から既定のプリセット沈下パラメータを決定する。
        /// 対応が無ければ null を返す（プリセット変更しない）。
        /// </summary>
        private static (string Name, string SoilType)? MapDefaultSettlementPreset(string pileConstructionType, string pileToeGranularityClass)
        {
            if (string.IsNullOrEmpty(pileConstructionType) || string.IsNullOrEmpty(pileToeGranularityClass))
                return null;

            // 場所打ちコンクリート杭（杭体種別が 鉄筋 / 鋼管 のいずれでも PileConstructionType は同値）
            if (pileConstructionType == "場所打ちコンクリート杭")
            {
                return pileToeGranularityClass switch
                {
                    "砂質土" => ("(2019)場所打ちコンクリート杭", "砂質土"),
                    "礫質土" => ("(2019)場所打ちコンクリート杭", "礫質土"),
                    "粘性土" => ("(2001)場所打ちコンクリート杭", "粘性土"),
                    _ => null
                };
            }

            // 埋込み杭（プレボーリング）— 表記揺れを含めて判定
            if (pileConstructionType == "埋込み杭（プレボーリング）"
                || pileConstructionType == "埋込み杭（プレボーリング杭）"
                || pileConstructionType == "埋込み杭（プレポーリング）")
            {
                return pileToeGranularityClass switch
                {
                    "砂質土" => ("(2019)既製コンクリート杭", "砂質土"),
                    "礫質土" => ("(2019)既製コンクリート杭", "礫質土"),
                    "粘性土" => ("(2019)既製コンクリート杭", "粘性土"),
                    _ => null
                };
            }

            return null;
        }

        /// <summary>
        /// 現在の SoilPile について、未初期化なら杭工法と杭先端土質に応じたプリセットを適用する。
        /// </summary>
        public void TryApplyDefaultSettlementPreset()
        {
            if (SoilPile == null) return;
            if (SoilPile.IsSettlementPresetInitialized) return;

            var mapped = MapDefaultSettlementPreset(SoilPile.PileConstructionType, SoilPile.PileToeGranularityClass);
            if (!mapped.HasValue)
            {
                // マッピング不可の場合も初期化済みマークして再試行を抑止
                SoilPile.IsSettlementPresetInitialized = true;
                return;
            }
            string name = mapped.Value.Name;
            string soilType = mapped.Value.SoilType;

            var pileBody = InputModel.PileBodies[SoilPile.PileBodyNo - 1];
            foreach (var parameter in pileBody.PileTipSettlementPresetParameters)
            {
                if (parameter.Name == name && parameter.SoilType == soilType)
                {
                    pileBody.SettleAlpha = parameter.Alpha;
                    pileBody.SettleN = parameter.N;
                    pileBody.SettleAlphaString = parameter.Alpha.ToString();
                    pileBody.SettleNString = parameter.N.ToString();

                    // ComboBox の選択を同期
                    if (ComboBoxPresetSettlementParameters != null && pileBody.PileTipSettlementPresetParameterNames != null)
                    {
                        for (int i = 0; i < pileBody.PileTipSettlementPresetParameterNames.Count; i++)
                        {
                            var item = pileBody.PileTipSettlementPresetParameterNames[i];
                            if (item.Contains(name) && item.Contains(soilType))
                            {
                                ComboBoxPresetSettlementParameters.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    break;
                }
            }
            SoilPile.IsSettlementPresetInitialized = true;
        }


        //杭先端沈下チャート要素追加コマンド
        public void AddComponent(double alpha, double n)
        {

            if (SettlementWindowInstance == null)
            { return; }
            var wpf = SettlementWindowInstance.wpfPlotPileToe;

            wpf.Plot.Clear();

            // 新しいデータポイントを追加
            List<double> xValues = [];
            List<double> yValues = [];

            for (double RpOnApRatio = 0; RpOnApRatio <= 1 + 0.01; RpOnApRatio += 0.01)
            {
                double SponDp = 0.1 * (alpha * RpOnApRatio + (1 - alpha) * Math.Pow(RpOnApRatio, n));

                xValues.Add(RpOnApRatio); // 例: fs の最初の要素を x 軸の値として使用
                yValues.Add(-SponDp); // 例: ds の最初の要素を y 軸の値として使用
            }
            var scatter = wpf.Plot.Add.Scatter(xValues.ToArray(), [.. yValues]);
            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;
            scatter.MarkerShape = ScottPlot.MarkerShape.None; // マーカーを描かないように設定

            string title = "杭先端沈下曲線";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            // Unicode 下付き文字 ₚ (U+209A) ᵤ (U+1D64) で数式風表示
            string xLabel = "(Rₚ/Aₚ) / (Rₚ/Aₚ)ᵤ";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "Sₚ / dₚ";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            Color grayColor = new(128, 128, 128, 255);
            wpf.Plot.Add.VerticalLine(0, 1, grayColor);
            wpf.Plot.Add.VerticalLine(1, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(0, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(-0.1, 1, grayColor);

            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_PileToe = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileToe", "Rₚ/Aₚ / (Rₚ/Aₚ)ᵤ", "Sₚ/dₚ");
        }

        // 杭周更新メソッド
        public void UpdateCircumstanceSeries()
        {

            if (SettlementWindowInstance == null)
            { return; }
            var wpf = SettlementWindowInstance.wpfPlotCircum;

            wpf.Plot.Clear();

            List<Point> textPositions = [];
            for (int i = 0; i < SoilPile.PileCircumVerticals.Count; i++)
            {
                var pileCircumstanceVertical = SoilPile.PileCircumVerticals[i];
                double psi = pileCircumstanceVertical.Psi;
                double tauT = pileCircumstanceVertical.TauT;
                double tau1 = pileCircumstanceVertical.Tau1;
                double tau2 = pileCircumstanceVertical.Tau2;
                double s1 = pileCircumstanceVertical.S1;
                double s2 = pileCircumstanceVertical.S2;
                // tau1が0の場合のゼロ除算を回避
                double sT = (tauT == 0 || tau1 == 0) ? 0 : tauT * s1 / tau1;

                // 点を(x, y)のペアとして作成し、x座標でソート
                var points = new List<(double x, double y)>
                {
                    (-50.0, tauT * psi),
                    (sT, tauT * psi),
                    (s1, tau1 * psi),
                    (s2, tau2 * psi),
                    (50.0, tau2 * psi)
                };
                points.Sort((a, b) => a.x.CompareTo(b.x));

                var scatter = wpf.Plot.Add.Scatter(
                    points.Select(p => p.x).ToArray(),
                    points.Select(p => p.y).ToArray());
                scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                scatter.LineWidth = 2;
                scatter.MarkerSize = 0;
            }

            if (SelectedPileCircumstanceVertical != null)
            {
                double selectedPsi = SelectedPileCircumstanceVertical.Psi;
                double selectedTauT = SelectedPileCircumstanceVertical.TauT;
                double selectedTau1 = SelectedPileCircumstanceVertical.Tau1;
                double selectedTau2 = SelectedPileCircumstanceVertical.Tau2;
                double selectedS1 = SelectedPileCircumstanceVertical.S1;
                double selectedS2 = SelectedPileCircumstanceVertical.S2;
                // tau1が0の場合のゼロ除算を回避
                double selectedST = (selectedTauT == 0 || selectedTau1 == 0) ? 0 : selectedTauT * selectedS1 / selectedTau1;

                // 点を(x, y)のペアとして作成し、x座標でソート
                var selectedPoints = new List<(double x, double y)>
                {
                    (-50.0, selectedTauT * selectedPsi),
                    (selectedST, selectedTauT * selectedPsi),
                    (selectedS1, selectedTau1 * selectedPsi),
                    (selectedS2, selectedTau2 * selectedPsi),
                    (50.0, selectedTau2 * selectedPsi)
                };
                selectedPoints.Sort((a, b) => a.x.CompareTo(b.x));

                var selectedScatter = wpf.Plot.Add.Scatter(
                    selectedPoints.Select(p => p.x).ToArray(),
                    selectedPoints.Select(p => p.y).ToArray());
                selectedScatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                selectedScatter.LineWidth = 6;
                selectedScatter.MarkerSize = 0;
            }

            string title = "土層の杭周面抵抗力";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "相対変位(mm)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "単位長さ当たりの杭周面抵抗 (kN/m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            Color grayColor = new(128, 128, 128, 255);
            wpf.Plot.Add.VerticalLine(0, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            //wpf.Plot.Legend.FontSize = 10;

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_PileCircumstance = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileCircumstance", "相対変位(mm)", "単位長さ当たり抵抗力(kN/m)");
        }

        // 選択番号を変えた場合のメソッド
        public void ComboBoxSoilPileNo_SelectionChanged(
            /*int selectedSoilPileNo,*/ int previousSelectedSoilPileNo)
        {
            if (previousSelectedSoilPileNo != -1)
            {
                SoilPile = SoilPiles[SoilPileNo - 1];
                OnPropertyChanged(nameof(UsesPileToeEta));
                // 切替先 SoilPile が未初期化なら既定プリセットを適用
                TryApplyDefaultSettlementPreset();
                // ユーザーが確認したのでランプを点灯
                MarkPileAsAnalyzed(SoilPileNo - 1);
            }

            DrawShapes();
        }

        public void DrawShapes()
        {
            if (Canvas == null) { return; }

            // 選択されたセグメントの位置情報を取得
            double? selectedTop = SelectedPileCircumstanceVertical?.Top;
            double? selectedBottom = SelectedPileCircumstanceVertical?.Bottom;

            ShapeDrawer.DrawPileElevation(
                Canvas, SoilPile.PileBodySegments,
                PileBody.PileToeDia,
                PileBody.InsituPileToeHeight,
                PileBody.InsituPileToeAngle,
                PileBody.PrecastConcretePileToeHeightRatio,
                SoilPile.PileConstructionType,
                SoilPile.Z,
                InputModel.GroundsInput[SoilPile.GroundNo - 1],
                false,           // isElementDivision
                null,            // zs
                null,            // selectedZ
                selectedTop,     // 選択区間上端
                selectedBottom, // 選択区間下端
                showLiquefactionFL: false,
                showGroundDisplacement: false,
                seismicLevelIndex: 0,
                displacementWithLiquefaction: false,
                showUnconfinedCompressiveStrength: true);
        }

        // DataGridSelectionコピーメソッド
        [RelayCommand]
        private static void CopyDataGridSelection(DataGrid dataGrid)
        {
            if (dataGrid == null || dataGrid.SelectedCells.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();

            var selectedCells = dataGrid.SelectedCells.GroupBy(cell => cell.Item).ToList();

            foreach (var row in selectedCells)
            {
                var rowValues = new List<string>();

                foreach (var cell in row)
                {
                    rowValues.Add(Output.DataGridCsv.GetCellValue(cell));
                }

                sb.AppendLine(string.Join("\t", rowValues));
            }

            Common.ClipboardHelper.TrySetText(sb.ToString());
        }
    }
}

