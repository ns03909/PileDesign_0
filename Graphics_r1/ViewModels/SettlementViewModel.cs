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
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        private ObservableCollection<SoilPile> _soilPiles;
        public ObservableCollection<SoilPile> SoilPiles
        {
            get => _soilPiles;
            set => SetProperty(ref _soilPiles, value);
        }

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

        // 杭体番号
        private int _soilPileNo = 1;
        public int SoilPileNo
        {
            get => _soilPileNo;
            set => SetProperty(ref _soilPileNo, value);
        }

        public ObservableCollection<double> Fs { get; set; } = [];
        public ObservableCollection<double> Ds { get; set; } = [];

        // 選択杭区間
        public PileCircumVertical SelectedPileCircumVertical { get; set; }

        public VerticalLoadTransferMethod.LoadDisplacement SelectedLoadDisplacement { get; set; }

        private VerticalLoadTransferMethod _verticalLoadTransferMethod;
        public VerticalLoadTransferMethod VerticalLoadTransferMethod
        {
            get => _verticalLoadTransferMethod;
            set => SetProperty(ref _verticalLoadTransferMethod, value);
        }

        // 解析制御モード選択
        private AnalysisControlMode _selectedAnalysisMode = AnalysisControlMode.DisplacementControl;
        public AnalysisControlMode SelectedAnalysisMode
        {
            get => _selectedAnalysisMode;
            set => SetProperty(ref _selectedAnalysisMode, value);
        }

        // 解析制御モードリスト（ComboBox用）
        public AnalysisControlMode[] AnalysisModes { get; } =
            [AnalysisControlMode.LoadControl, AnalysisControlMode.DisplacementControl];

        // 解析制御モードが変位制御法かどうか
        public bool IsDisplacementControl
        {
            get => _selectedAnalysisMode == AnalysisControlMode.DisplacementControl;
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
        public TextBox TextBoxPileToeDia { get; set; }
        public TextBox TextBoxPrecastPileTipNonPermability { get; set; }
        public TextBox TextBoxSteelPileTipNonPermability { get; set; }
        public TextBox TextBoxSettleAlpha { get; set; }
        public TextBox TextBoxSettleN { get; set; }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public ComboBox ComboBoxPileTopType { get; set; }
        public ComboBox ComboBoxPresetSettlementParameters { get; set; }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        public static Crosshair MyCrosshair_PileToe { get; private set; }

        private string _crosshairPositionText_PileToe;
        public string CrosshairPositionText_PileToe
        {
            get => _crosshairPositionText_PileToe;
            set => SetProperty(ref _crosshairPositionText_PileToe, value);
        }

        public static Crosshair MyCrosshair_PileCircum { get; private set; }

        private string _crosshairPositionText_PileCircum;
        public string CrosshairPositionText_PileCircum
        {
            get => _crosshairPositionText_PileCircum;
            set => SetProperty(ref _crosshairPositionText_PileCircum, value);
        }

        public static Crosshair MyCrosshair_PileToeSettlement { get; private set; }

        private string _crosshairPositionText_PileToeSettlement;
        public string CrosshairPositionText_PileToeSettlement
        {
            get => _crosshairPositionText_PileToeSettlement;
            set => SetProperty(ref _crosshairPositionText_PileToeSettlement, value);
        }

        public static Crosshair MyCrosshair_PileTopSettlement { get; private set; }

        private string _crosshairPositionText_PileTopSettlement;
        public string CrosshairPositionText_PileTopSettlement
        {
            get => _crosshairPositionText_PileTopSettlement;
            set => SetProperty(ref _crosshairPositionText_PileTopSettlement, value);
        }

        // コンストラクタ
        public SettlementViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            // 元コレクションを先にスナップショット化（この時点では DeepCopy は呼ばれない）
            var soilPilesSnapshot = InputModel.ElementDivision.SoilPiles?.ToList() ?? new List<SoilPile>();

            // スナップショットを列挙して DeepCopy（元コレクションはもはや触らない）
            SoilPiles = new ObservableCollection<SoilPile>(soilPilesSnapshot.Select(pile => pile.DeepCopy()));

            UpdateSoilPilesCountList();

            // 件数チェックして参照を安全に取得
            SoilPile = SoilPiles.Count > 0 ? SoilPiles[Math.Clamp(SoilPileNo - 1, 0, SoilPiles.Count - 1)] : throw new InvalidOperationException("SoilPiles が空です。");

            PileBody = InputModel.PileBodies[SoilPile.PileBodyNo - 1];

            // 先端平均N値が0の場合に警告メッセージを表示
            if (SoilPile.PileToeNValue == 0)
            {
                System.Windows.MessageBox.Show(
                    "杭先端平均N値が0です。地盤データや杭の設定を確認してください。",
                    "警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }

            // xaml
            ComboBoxPileBodyNo = new();
            TextBoxPileBodyRef = new();
            TextBoxPileToeDia = new();
            TextBoxPrecastPileTipNonPermability = new();
            TextBoxSteelPileTipNonPermability = new();
            TextBoxSettleAlpha = new();
            TextBoxSettleN = new();

            AddComponent(PileBody.SettleAlpha, PileBody.SettleN);

            UpdateSettlementChart();
            UpdateCircumSeries();
            DrawShapes();

            // 初期状態をUndoManagerに保存（ここもスナップショット→DeepCopyで安全に）
            UndoManager.SaveState(new ObservableCollection<SoilPile>(SoilPiles.Select(p => p.DeepCopy())));
        }

        [RelayCommand]
        private void Undo()
        {
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
            if (Application.Current.MainWindow.DataContext is MainWindowViewModel mainWindowViewModel)
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

        // 荷重沈下解析実行
        [RelayCommand]
        public void ExecuteAnalysis()
        {
            if (Application.Current.MainWindow.DataContext is MainWindowViewModel mainWindowViewModel)
                VerticalLoadTransferMethod = new(mainWindowViewModel, SoilPile, SelectedAnalysisMode);

            // チャートの更新
            UpdateSettlementChart();

            // SoilPileに計算結果を保存
            SoilPile.LoadDisplacements = VerticalLoadTransferMethod.LoadDisplacements;
            SoilPile.LoadDisplacementsLimit = VerticalLoadTransferMethod.LoadDisplacementsLimit;

            // 解析結果タブを前面に
            SelectedTabIndex = 1;
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
                wpf.Plot.Add.VerticalLine(SelectedLoadDisplacement.D0s, 1);
                wpfToe.Plot.Add.VerticalLine(SelectedLoadDisplacement.Dns, 1);
            }

            AddScatterPlotAllowable(wpf, [.. settlements], [.. allowableLoads], verticalLineColor);

            AddScatterPlot(wpf, [.. xValues], [.. yValues5], NikkenSKColor.SkyBlue, "杭頭荷重");
            AddScatterPlot(wpf, [.. xValues], [.. yValues2], NikkenSKColor.DeepBlue, "杭先端支持力");
            AddScatterPlot(wpf, [.. xValues], [.. yValues3], NikkenSKColor.Yellow, "杭周面抵抗力");
            AddScatterPlot(wpf, [.. xValues], [.. yValues4], NikkenSKColor.Green, "杭自重");


            AddScatterPlotAllowable(wpfToe, [.. settlementsToe], [.. allowableLoads], verticalLineColor);

            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues5], NikkenSKColor.SkyBlue, "杭頭荷重");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues2], NikkenSKColor.DeepBlue, "杭先端支持力");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues3], NikkenSKColor.Yellow, "杭周面抵抗力");
            AddScatterPlot(wpfToe, [.. xToeValues], [.. yValues4], NikkenSKColor.Green, "杭自重");

            //InputModel inputModel = InputModel.Instance;
            List<double> forcesVL = [];
            List<double> settlementsVL = [];
            List<double> forcesLevel1 = [];
            List<double> settlementsLevel1 = [];
            List<double> forcesLevel2 = [];
            List<double> settlementsLevel2 = [];

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
                        settlementVL.Add(settlementVector[0] * 1000);
                    }

                    for (int i = 0; i < pileLayoutItem.AxialForceLevel1s.Count; i++)
                    {
                        var axialforce = pileLayoutItem.AxialForceLevel1s[i];
                        settlementVector = VerticalLoadTransferMethod.GetDisplacementForGivenLoad(axialforce);
                        if (settlementVector != null)
                        {
                            pileLayoutItem.SinglePileSettlementLevel1s[i] = settlementVector[0];
                            forcesLevel1.Add(axialforce);
                            settlementsLevel1.Add(settlementVector[0] * 1000);
                        }
                    }

                    for (int i = 0; i < pileLayoutItem.AxialForceLevel2s.Count; i++)
                    {
                        var axialforce = pileLayoutItem.AxialForceLevel2s[i];
                        settlementVector = VerticalLoadTransferMethod.GetDisplacementForGivenLoad(axialforce);
                        if (settlementVector != null)
                        {
                            pileLayoutItem.SinglePileSettlementLevel2s[i] = settlementVector[0];
                            forcesLevel2.Add(axialforce);
                            settlementsLevel2.Add(settlementVector[0] * 1000);
                        }
                    }
                }
            }

            AddAxialForceScatterPlot(wpf, [.. settlementsVL], [.. forcesVL], NikkenSKColor.SkyBlue, "VL", MarkerShape.OpenCircle);
            AddAxialForceScatterPlot(wpf, [.. settlementsLevel1], [.. forcesLevel1], NikkenSKColor.SkyBlue, "レベル1", MarkerShape.OpenTriangleDown);
            AddAxialForceScatterPlot(wpf, [.. settlementsLevel2], [.. forcesLevel2], NikkenSKColor.SkyBlue, "レベル2", MarkerShape.OpenDiamond);

            ConfigurePlot(wpf, "荷重-杭頭沈下曲線", "杭頭沈下量(mm)", "荷重 (kN)");
            ConfigurePlot(wpfToe, "荷重-杭先端沈下曲線", "杭先端沈下量(mm)", "荷重 (kN)");

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

                else if (textBox.Name == "TextBoxPileToeDia" && double.TryParse(textBox.Text, out double toeDia))
                {
                    PileBody.PileToeDia = toeDia;
                    DrawShapes();
                }

                else if (textBox.Name == "TextBoxPrecastPileTipNonPermability" && double.TryParse(textBox.Text, out double precastPileTipNonPermability))
                {
                    PileBody.TipNonPermability = precastPileTipNonPermability;
                }

                else if (textBox.Name == "TextBoxSteelPileTipNonPermability" && double.TryParse(textBox.Text, out double steelPileTipNonPermability))
                {
                    PileBody.TipNonPermability = steelPileTipNonPermability;
                }

                else if (textBox.Name == "TextBoxSettleAlpha" && double.TryParse(textBox.Text, out double settleAlpha))
                {
                    PileBody.SettleAlpha = settleAlpha;
                    ComboBoxPresetSettlementParameters.SelectedIndex = -1;
                    // 解析結果タブを前面に
                    SelectedTabIndex = 0;
                    var wpf = SettlementWindowInstance.wpfPlotSettlement;
                    var wpfToe = SettlementWindowInstance.wpfPlotSettlementToe;
                    wpf.Plot.Clear();
                    wpfToe.Plot.Clear();
                    wpf.Refresh();
                    wpfToe.Refresh();
                }

                else if (textBox.Name == "TextBoxSettleN" && double.TryParse(textBox.Text, out double settleN))
                {
                    PileBody.SettleN = settleN;
                    ComboBoxPresetSettlementParameters.SelectedIndex = -1;
                    // 解析結果タブを前面に
                    SelectedTabIndex = 0;
                    var wpf = SettlementWindowInstance.wpfPlotSettlement;
                    var wpfToe = SettlementWindowInstance.wpfPlotSettlementToe;
                    wpf.Plot.Clear();
                    wpfToe.Plot.Clear();
                    wpf.Refresh();
                    wpfToe.Refresh();
                }

                //チャート要素クリアコマンド
                AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
            }

            if (sender is string text)
            {
                if (double.TryParse(text, out double value))
                {
                    if (text == TextBoxSettleAlpha.Text)
                    {
                        PileBody.SettleAlpha = value;
                    }
                    else if (text == TextBoxSettleN.Text)
                    {
                        PileBody.SettleN = value;
                    }

                    // チャート要素クリアコマンド
                    AddComponent(PileBody.SettleAlpha, PileBody.SettleN);
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
            if (ComboBoxPresetSettlementParameters == null) return;

            var selectedPresetParameter = ComboBoxPresetSettlementParameters.SelectedItem?.ToString();
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
            AddComponent(InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha, InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN);
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

            for (double RponApRatio = 0; RponApRatio <= 1 + 0.01; RponApRatio += 0.01)
            {
                double SponDp = 0.1 * (alpha * RponApRatio + (1 - alpha) * Math.Pow(RponApRatio, n));

                xValues.Add(RponApRatio); // 例: fs の最初の要素を x 軸の値として使用
                yValues.Add(-SponDp); // 例: ds の最初の要素を y 軸の値として使用
            }
            var scatter = wpf.Plot.Add.Scatter(xValues.ToArray(), [.. yValues]);
            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;
            scatter.MarkerShape = ScottPlot.MarkerShape.None; // マーカーを描かないように設定

            string title = "杭先端沈下曲線";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "(Rp/Ap)/(Rp/Ap)u";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "Sp/dp";
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
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileToe", "Rp/Ap / (Rp/Ap)u", "Sp/dp");
        }

        // 杭周更新メソッド
        public void UpdateCircumSeries()
        {

            if (SettlementWindowInstance == null)
            { return; }
            var wpf = SettlementWindowInstance.wpfPlotCircum;

            wpf.Plot.Clear();

            List<Point> textPositions = [];
            for (int i = 0; i < SoilPile.PileCircumVerticals.Count; i++)
            {
                var pileCircumVertical = SoilPile.PileCircumVerticals[i];
                double psi = pileCircumVertical.Psi;
                double tauT = pileCircumVertical.TauT;
                double tau1 = pileCircumVertical.Tau1;
                double tau2 = pileCircumVertical.Tau2;
                double s1 = pileCircumVertical.S1;
                double s2 = pileCircumVertical.S2;
                double sT = tauT == 0 ? 0 : tauT * s1 / tau1;

                List<double> xValues = [-50.0, sT, s1, s2, 50.0,];
                List<double> yValues = [tauT * psi, tauT * psi, tau1 * psi, tau2 * psi, tau2 * psi];
                var scatter = wpf.Plot.Add.Scatter(xValues.ToArray(), [.. yValues]);
                scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                scatter.LineWidth = 2;
                scatter.MarkerSize = 0;
            }

            if (SelectedPileCircumVertical != null)
            {
                double selectedPsi = SelectedPileCircumVertical.Psi;
                double selectedTauT = SelectedPileCircumVertical.TauT;
                double selectedTau1 = SelectedPileCircumVertical.Tau1;
                double selectedTau2 = SelectedPileCircumVertical.Tau2;
                double selectedS1 = SelectedPileCircumVertical.S1;
                double selectedS2 = SelectedPileCircumVertical.S2;
                double selectedST = selectedTauT == 0 ? 0 : selectedTauT * selectedS1 / selectedTau1;

                // 新しいデータポイントを追加
                List<double> selectedXValues = [-50.0, selectedST, selectedS1, selectedS2, 50.0,];
                List<double> selectedYValues = [
                    selectedTauT * selectedPsi,
                    selectedTauT * selectedPsi,
                    selectedTau1 * selectedPsi,
                    selectedTau2 * selectedPsi,
                    selectedTau2 * selectedPsi];
                //CircumChartSeries.Add(seriesToUpdate);
                var selectedScatter = wpf.Plot.Add.Scatter(selectedXValues.ToArray(), [.. selectedYValues]);
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
            MyCrosshair_PileCircum = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_PileCircum", "相対変位(mm)", "単位長さ当たり抵抗力(kN/m)");
        }

        // 選択番号を変えた場合のメソッド
        public void ComboBoxSoilPileNo_SelectionChanged(
            /*int selectedSoilPileNo,*/ int previousSelectedSoilPileNo)
        {
            if (previousSelectedSoilPileNo != -1)
            {
                SoilPile = SoilPiles[SoilPileNo - 1];
            }

            DrawShapes();
        }

        public void DrawShapes()
        {
            if (Canvas == null) { return; }
            ShapeDrawer.DrawPileElevation(
                Canvas, SoilPile.PileBodySegments,
                PileBody.PileToeDia,
                PileBody.InsituPileToeHeight,
                PileBody.InsituPileToeAngle,
                PileBody.PrecastConcretePileToeHeightRatio,
                SoilPile.PileConstructionType,
                SoilPile.Z,
                InputModel.GroundsInput[SoilPile.GroundNo - 1]);
        }

        // DataGridSelecitonコピーメソッド
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
                    if (cell.Column.GetCellContent(cell.Item) is TextBlock textBlock)
                    {
                        rowValues.Add(textBlock.Text);
                    }
                }

                sb.AppendLine(string.Join("\t", rowValues));
            }

            Clipboard.SetText(sb.ToString());
        }
    }
}

