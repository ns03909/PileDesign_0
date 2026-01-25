using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
//using System.Windows.Forms;

namespace PileDesign.ViewModels
{
    public partial class GraphViewModel : ObservableObject
    {
        private readonly UndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        public bool IsHorizontalAnalysisDone { get; set; }
        public bool IsVerticalAnalysisDone { get; set; }
        public bool IsGroupPileSettlementAnalysisDone { get; set; }

        // RequestCloseイベントの実装
        public event EventHandler RequestClose;

        // MainWindowViewModelからAnaModelを取得
        public AnaModel AnaModel => _mainWindowViewModel?.CurrentModel;

        public ObservableCollection<string> GraphOptions { get; } = [];

        private string _selectedGraphOption;
        public string SelectedGraphOption
        {
            get => _selectedGraphOption;
            set
            {
                if (SetProperty(ref _selectedGraphOption, value))
                {
                    UpdateGraph();
                    OnPropertyChanged(nameof(IsMultiGraphVisible));
                    OnPropertyChanged(nameof(IsSingleGraphVisible));
                }
            }
        }

        public ObservableCollection<string> LoadCaseOptions { get; }

        private string _loadCaseOption;
        public string LoadCaseOption
        {
            get => _loadCaseOption;
            set
            {
                if (SetProperty(ref _loadCaseOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 荷重ケースオプション描画
        private bool _isLoadCaseOptionVisible;
        public bool IsLoadCaseOptionVisible
        {
            get => _isLoadCaseOptionVisible;
            set
            {
                if (SetProperty(ref _isLoadCaseOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedLoadCaseOption;
        public string SelectedLoadCaseOption
        {
            get => _selectedLoadCaseOption;
            set
            {
                if (SetProperty(ref _selectedLoadCaseOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _loadCombinationOptions;
        public ObservableCollection<string> LoadCombinationOptions
        {
            get => _loadCombinationOptions;
            set
            {
                if (SetProperty(ref _loadCombinationOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 荷重組合せオプション描画
        private bool _isLoadCombinationOptionVisible;
        public bool IsLoadCombinationOptionVisible
        {
            get => _isLoadCombinationOptionVisible;
            set
            {
                if (SetProperty(ref _isLoadCombinationOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedLoadCombinationOption;
        public string SelectedLoadCombinationOption
        {
            get => _selectedLoadCombinationOption;
            set
            {
                if (SetProperty(ref _selectedLoadCombinationOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _pileOptions;
        public ObservableCollection<string> PileOptions
        {
            get => _pileOptions;
            set
            {
                if (SetProperty(ref _pileOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedPileOption;
        public string SelectedPileOption
        {
            get => _selectedPileOption;
            set
            {
                if (SetProperty(ref _selectedPileOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _pileBodyRefOptions;
        public ObservableCollection<string> PileBodyRefOptions
        {
            get => _pileBodyRefOptions;
            set
            {
                if (SetProperty(ref _pileBodyRefOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedPileBodyRef;
        public string SelectedPileBodyRef
        {
            get => _selectedPileBodyRef;
            set
            {
                if (SetProperty(ref _selectedPileBodyRef, value))
                {
                    UpdateGraph();
                    SelectedPileSegmentNo = 1;
                    int segmentsCount = InputModel.GetPileBodyByPileBodyRef(_selectedPileBodyRef).PileBodySegments.Count;
                    PileSegmentOptions = new ObservableCollection<int>(Enumerable.Range(1, segmentsCount));
                }
            }
        }

        // 杭体オプション描画
        private bool _isPileBodyOptionVisible;
        public bool IsPileBodyOptionVisible
        {
            get => _isPileBodyOptionVisible;
            set
            {
                if (SetProperty(ref _isPileBodyOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<int> _pileSegmentOptions;
        public ObservableCollection<int> PileSegmentOptions
        {
            get => _pileSegmentOptions;
            set
            {
                if (SetProperty(ref _pileSegmentOptions, value))
                {
                    UpdateGraph();
                }
            }
        }


        private int _selectedPileSegmentNo;
        public int SelectedPileSegmentNo
        {
            get => _selectedPileSegmentNo;
            set
            {
                if (SetProperty(ref _selectedPileSegmentNo, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 杭区間オプション描画
        private bool _isPileSegmentOptionVisible;
        public bool IsPileSegmentOptionVisible
        {
            get => _isPileSegmentOptionVisible;
            set
            {
                if (SetProperty(ref _isPileSegmentOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 杭オプション描画
        private bool _isPileOptionVisible;
        public bool IsPileOptionVisible
        {
            get => _isPileOptionVisible;
            set
            {
                if (SetProperty(ref _isPileOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 液状化コンボボックス描画
        private bool _isLiquefactionOptionVisible;
        public bool IsLiquefactionOptionVisible
        {
            get => _isLiquefactionOptionVisible;
            set
            {
                if (SetProperty(ref _isLiquefactionOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private bool _isLiquefaction;
        public bool IsLiquefaction
        {
            get => _isLiquefaction;
            set
            {
                if (SetProperty(ref _isLiquefaction, value))
                {
                    UpdateGraph();
                }
            }
        }

        public string[] LiquefactionOptions { get; } =
        [
            "両方",
            "液状化考慮",
            "液状化非考慮",
            ];

        private string _selectedLiquefaction = "両方";
        public string SelectedLiquefaction
        {
            get => _selectedLiquefaction;
            set
            {
                if (SetProperty(ref _selectedLiquefaction, value))
                {
                    if (SelectedLiquefaction == LiquefactionOptions[0])
                    {
                        SelectedLiquefactionCases = [true, false];
                    }
                    else if (SelectedLiquefaction == LiquefactionOptions[1])
                    {
                        SelectedLiquefactionCases = [true];
                    }
                    else if (SelectedLiquefaction == LiquefactionOptions[2])
                    {
                        SelectedLiquefactionCases = [false];
                    }
                    else
                    {
                        SelectedLiquefactionCases = [true, false];
                    }
                }
            }
        }

        private ObservableCollection<bool> _selectedLiquefactionCases = [true, false];
        public ObservableCollection<bool> SelectedLiquefactionCases
        {
            get => _selectedLiquefactionCases;
            set
            {
                if (SetProperty(ref _selectedLiquefactionCases, value))
                {
                    UpdateGraph();
                }
            }
        }

        // レジェンド描画
        private bool _isLegendVisible = true;
        public bool IsLegendVisible
        {
            get => _isLegendVisible;
            set
            {
                if (SetProperty(ref _isLegendVisible, value))
                {
                    UpdateLegendVisibility();
                }
            }
        }

        [RelayCommand]
        public void ToggleLegend()
        {
            IsLegendVisible = !IsLegendVisible;
        }

        private void UpdateLegendVisibility()
        {
            // Windows 7.0 以降のみで実行
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0) && WpfPlot != null)
            {
                WpfPlot.Plot.Legend.IsVisible = IsLegendVisible;
                WpfPlot.Refresh();

                WpfPlot1.Plot.Legend.IsVisible = IsLegendVisible;
                WpfPlot1.Refresh();

                WpfPlot2.Plot.Legend.IsVisible = IsLegendVisible;
                WpfPlot2.Refresh();

                WpfPlot3.Plot.Legend.IsVisible = IsLegendVisible;
                WpfPlot3.Refresh();
            }
        }

        // グリッドオプション描画
        private bool _isGridOptionVisible;
        public bool IsGridOptionVisible
        {
            get => _isGridOptionVisible;
            set
            {
                if (SetProperty(ref _isGridOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _gridOptions = [];
        public ObservableCollection<string> GridOptions
        {
            get => _gridOptions;
            set
            {
                if (SetProperty(ref _gridOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedGridOption;
        public string SelectedGridOption
        {
            get => _selectedGridOption;
            set
            {
                if (SetProperty(ref _selectedGridOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        public WpfPlot WpfPlot { get; set; }
        public WpfPlot WpfPlot1 { get; set; }
        public WpfPlot WpfPlot2 { get; set; }
        public WpfPlot WpfPlot3 { get; set; }

        public static Crosshair MyCrosshair { get; private set; }
        public static Crosshair MyCrosshair1 { get; private set; }
        public static Crosshair MyCrosshair2 { get; private set; }
        public static Crosshair MyCrosshair3 { get; private set; }

        private string _crosshairPositionText;
        public string CrosshairPositionText
        {
            get => _crosshairPositionText;
            set => SetProperty(ref _crosshairPositionText, value);
        }

        private string _crosshairPositionText1;
        public string CrosshairPositionText1
        {
            get => _crosshairPositionText1;
            set => SetProperty(ref _crosshairPositionText1, value);
        }

        private string _crosshairPositionText2;
        public string CrosshairPositionText2
        {
            get => _crosshairPositionText2;
            set => SetProperty(ref _crosshairPositionText2, value);
        }

        private string _crosshairPositionText3;
        public string CrosshairPositionText3
        {
            get => _crosshairPositionText3;
            set => SetProperty(ref _crosshairPositionText3, value);
        }

        public bool IsMultiGraphVisible
        {
            // 「杭」のときのみ三分割表示
            get => SelectedGraphOption == "杭";
        }

        public bool IsSingleGraphVisible
        {
            get => !IsMultiGraphVisible;
        }

        private ObservableCollection<LoadCase> GetSelectedLoadCases()
        {
            ObservableCollection<LoadCase> selectedLoadCases = [];
            if (SelectedLoadCaseOption == "All")
            {
                selectedLoadCases = InputModel.LoadCasesInput.AllSeismicLoadCases;
            }
            else
            {
                foreach (var loadCase in InputModel.LoadCasesInput.AllSeismicLoadCases)
                {
                    if (SelectedLoadCaseOption == loadCase.LoadName)
                    {
                        selectedLoadCases.Add(loadCase);
                        return selectedLoadCases;
                    }
                }
            }
            return selectedLoadCases;
        }

        private ObservableCollection<LoadCombination> GetSelectedLoadCombinations()
        {
            ObservableCollection<LoadCombination> selectedLoadCombinations = [];
            if (SelectedLoadCombinationOption == "All")
            {
                selectedLoadCombinations = InputModel.LoadCasesInput.LoadCombinations;
            }
            else
            {
                foreach (var loadCombination in InputModel.LoadCasesInput.LoadCombinations)
                {
                    if (SelectedLoadCombinationOption == loadCombination.GetName())
                    {
                        selectedLoadCombinations.Add(loadCombination);
                        return selectedLoadCombinations;
                    }
                }
            }
            return selectedLoadCombinations;
        }

        private ObservableCollection<PileLayoutDataItem> GetSelectedPileLayouts()
        {
            ObservableCollection<PileLayoutDataItem> selectedPileLayouts = [];
            if (SelectedPileOption == "All")
            {
                selectedPileLayouts = InputModel.PileLayoutItems;
            }
            else
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    if (SelectedPileOption == $"{pile.No}" + "X:" + pile.X + "Y:" + pile.Y + "Z:" + pile.Z)
                    {
                        selectedPileLayouts.Add(pile);
                        return selectedPileLayouts;
                    }
                }
            }
            return selectedPileLayouts;
        }


        // コンストラクタ
        public GraphViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            IsHorizontalAnalysisDone = _mainWindowViewModel.IsHorizontalAnalysisDone;
            IsVerticalAnalysisDone = _mainWindowViewModel.IsVerticalAnalysisDone;
            IsGroupPileSettlementAnalysisDone = _mainWindowViewModel.IsGroupPileSettlementAnalysisDone;

            LoadCaseOptions = ["All"];
            foreach (LoadCase loadCase in InputModel.LoadCasesInput.AllLoadCases)
            {
                LoadCaseOptions.Add(loadCase.LoadName);
            }
            SelectedLoadCaseOption = LoadCaseOptions[0]; // 初期値

            LoadCombinationOptions = ["All"];
            foreach (LoadCombination loadCombination in InputModel.LoadCasesInput.LoadCombinations)
            {
                LoadCombinationOptions.Add(loadCombination.GetName());
            }
            SelectedLoadCombinationOption = LoadCombinationOptions[0]; // 初期値

            PileOptions = ["All"];
            foreach (PileLayoutDataItem pile in InputModel.PileLayoutItems)
            {
                PileOptions.Add($"{pile.No}" + "X:" + pile.X + "Y:" + pile.Y + "Z:" + pile.Z);
            }
            SelectedPileOption = PileOptions[0]; // 初期値

            PileBodyRefOptions = [];
            foreach (PileBodyInput pileBody in InputModel.PileBodies)
            {
                PileBodyRefOptions.Add(pileBody.PileBodyRef);
            }
            SelectedPileBodyRef = PileBodyRefOptions[0]; // 初期値
            SelectedPileSegmentNo = 1; // 初期値

            // 通り心
            foreach (var gridItem in InputModel.GridXItems)
            {
                gridItem.SetPiles(InputModel.PileLayoutItems, 90, 0.1);
            }
            foreach (var gridItem in InputModel.GridYItems)
            {
                gridItem.SetPiles(InputModel.PileLayoutItems, 0, 0.1);
            }

            GridOptions = [];
            foreach (var gridItem in InputModel.GridXItems)
            {
                GridOptions.Add(gridItem.Name + "(X=" + $"{gridItem.Coord:N1}" + ")");
            }
            foreach (var gridItem in InputModel.GridYItems)
            {
                GridOptions.Add(gridItem.Name + "(Y=" + $"{gridItem.Coord:N1}" + ")");
            }
        }

        // イニシャル
        public void Initialize()
        {
            // 状態に応じて初期化や表示内容を切り替える
            if (IsHorizontalAnalysisDone)
            {
                GraphOptions.Clear();
                GraphOptions.Add("作用点荷重変形関係");
                //GraphOptions.Add("杭頭応力変形関係F");
                //GraphOptions.Add("杭頭応力変形関係M");
                //GraphOptions.Add("杭応力F");
                //GraphOptions.Add("杭応力M");
                //GraphOptions.Add("杭変位U");
                GraphOptions.Add("杭");
                GraphOptions.Add("NMINT");
                GraphOptions.Add("杭頭M-θ");

            }
            if (IsVerticalAnalysisDone)
            {
                GraphOptions.Add("単杭沈下");
            }
            if (IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("群杭沈下");
            }
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("単杭+群杭沈下");
            }

            if (GraphOptions.Count > 0 && string.IsNullOrEmpty(SelectedGraphOption))
            {
                SelectedGraphOption = GraphOptions[0];
            }
        }

        [RelayCommand]
        private void Undo() => _undoManager.Undo();

        [RelayCommand]
        private void Redo() => _undoManager.Redo();

        [RelayCommand]
        private void OnClose()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // プロット
        private void ConfigurePlot(
            WpfPlot wpfPlot,
            Crosshair crosshair,
            string CrosshairPositionText,
            string title,
            string xLabel,
            string yLabel,
            int decimalPlacesX = 1,
            int decimalPlacesY = 1)

        {
            if (SelectedGraphOption.StartsWith('杭'))
            {
                if (xLabel.StartsWith('F'))
                { title = "せん断力"; }
                else if (xLabel.StartsWith('M'))
                { title = "曲げモーメント"; }
                else if (xLabel.StartsWith('U'))
                { title = "変位・地盤変位"; }
                else
                { title = xLabel; }
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
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
            }

            //int decimalPlacesX = 3;
            //int decimalPlacesY = 3;
            // クロスヘアの初期化
            crosshair = PlotHelper.InitCrosshair(wpfPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (SelectedGraphOption.StartsWith('杭'))
            {
                if (xLabel.StartsWith('F'))
                { decimalPlacesX = 1; }
                else if (xLabel.StartsWith('M'))
                { decimalPlacesX = 1; }
                else if (xLabel.StartsWith('U'))
                { decimalPlacesX = 1; }
                else
                { decimalPlacesX = 1; }
            }
            else if (SelectedGraphOption.StartsWith("NMINT"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("作用点荷重変形関係"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("杭頭M-θ"))
            {
                // M-θはθ(M-θ)の見やすさ重視でX=4桁/Y=1桁などに設定可（任意）
                decimalPlacesX = 4;
                decimalPlacesY = 1;
            }

            wpfPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, CrosshairPositionText, xLabel, yLabel, decimalPlacesX, decimalPlacesY);
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
            {
                wpfPlot.Refresh();
            }
        }

        // グラフ更新メソッド
        public void UpdateGraph()
        {
            if (WpfPlot == null) return;
            if (SelectedGraphOption == null) return;

            WpfPlot.Plot.Clear();
            WpfPlot1.Plot.Clear();
            WpfPlot2.Plot.Clear();
            WpfPlot3.Plot.Clear();


            if (SelectedGraphOption.StartsWith("杭頭応力変形関係"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                SelectedPileOption = "All";

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
                    {
                        WpfPlot.Plot.Clear();
                        WpfPlot.Refresh();
                    }
                    return;
                }

                foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                {
                    var beams = pileLayoutDataItem.Beams;

                    foreach (LoadCase loadCase in GetSelectedLoadCases())
                    {
                        foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                // 杭頭
                                List<double> beamZs = [beams[0].NodeI.Coord.Z];
                                List<double> beamForces = [0];

                                for (int step = 0; step <= lastStep; step++)
                                {
                                    beamZs.Add(beams[0].NodeI.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction, step).CumulativeDisp.Uh);

                                    var result = beams[0].GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction, step);
                                    if (SelectedGraphOption.EndsWith("Fy"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fyi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("Fz"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fzi);
                                    }
                                    else if (SelectedGraphOption.EndsWith('F'))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("My"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Myi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("Mz"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Mzi);
                                    }
                                    else if (SelectedGraphOption.EndsWith('M'))
                                    {
                                        beamForces.Add(result.CumulativeForce.Mi);
                                    }
                                }

                                if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
                                {
                                    var scatter = WpfPlot.Plot.Add.Scatter(beamZs, beamForces);
                                    scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                                }
                            }
                        }
                    }
                }

                string axisY = SelectedGraphOption switch
                {
                    var s when s.EndsWith("Fy") => "Fy (kN)",
                    var s when s.EndsWith("Fz") => "Fz (kN)",
                    var s when s.EndsWith('F') => "F (kN)",
                    var s when s.EndsWith("My") => "My (kNm)",
                    var s when s.EndsWith("Mz") => "Mz (kNm)",
                    var s when s.EndsWith('M') => "M (kNm)",
                    _ => string.Empty
                };

                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, "水平変位(mm)", axisY);
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();

            }
            else if (SelectedGraphOption.StartsWith("作用点荷重変形関係"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                SelectedPileOption = "All";

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations()
                    )
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);

                            List<double> disps = [0];
                            List<double> forces = [0];

                            for (int step = 0; step <= lastStep; step++)
                            {
                                NodeResult nodeResult = AnaModel.Nodes[0].GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction, step);
                                disps.Add(nodeResult.CumulativeDisp.Uh * 1000.0);
                                forces.Add(nodeResult.CumulativedLoad.GetHorizontalAbsLoad());
                            }
                            var scatter = WpfPlot.Plot.Add.Scatter(disps, forces);
                            scatter.LegendText = GetGeneralLegendText(loadCase, loadCombination, isLiquefaction);
                        }
                    }
                }
                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, "作用点変形 (mm)", "作用点水平荷重(kN)");
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();
            }
            else if (SelectedGraphOption.StartsWith("杭応力"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                (string forceType, string unit) = SelectedGraphOption switch
                {
                    var s when s.EndsWith("Fy") => ("Fy", "kN"),
                    var s when s.EndsWith("Fz") => ("Fz", "kN"),
                    var s when s.EndsWith('F') => ("F", "kN"),
                    var s when s.EndsWith("My") => ("My", "kNm"),
                    var s when s.EndsWith("Mz") => ("Mz", "kNm"),
                    var s when s.EndsWith('M') => ("M", "kNm"),
                    _ => (string.Empty, string.Empty)
                };

                DrawPileForce(WpfPlot, MyCrosshair, "CrosshairPositionText", forceType, unit);
            }

            else if (SelectedGraphOption.StartsWith("杭変位"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                (string dispType, string unit) = SelectedGraphOption switch
                {
                    var s when s.EndsWith("UX") => ("UX", "m"),
                    var s when s.EndsWith("UY") => ("UY", "m"),
                    var s when s.EndsWith('U') => ("U", "m"),
                    _ => (string.Empty, string.Empty)
                };

                DrawPileDisp(WpfPlot, MyCrosshair, "CrosshairPositionText", dispType, unit);
            }

            else if (SelectedGraphOption.StartsWith("NMINT"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                WpfPlot.Plot.Clear();

                var pileSection = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef).PileBodySegments[SelectedPileSegmentNo - 1].PileSection;

                var scatterUnService = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.UnfactoredServiceNM.N.ToArray(), [.. pileSection.UnfactoredServiceNM.M]);
                scatterUnService.LegendText = "低減前使用限界";

                var scatterFaService = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.FactoredServiceNM.N.ToArray(), [.. pileSection.FactoredServiceNM.M]);
                scatterFaService.LegendText = "低減後使用限界";

                var scatterUnDamage = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.UnfactoredDamageNM.N.ToArray(), [.. pileSection.UnfactoredDamageNM.M]);
                scatterUnDamage.LegendText = "低減前損傷限界";

                var scatterFaDamage = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.FactoredDamageNM.N.ToArray(), [.. pileSection.FactoredDamageNM.M]);
                scatterFaDamage.LegendText = "低減後損傷限界";

                var scatterUnUltimate = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.UnfactoredUltimateNM.N.ToArray(), [.. pileSection.UnfactoredUltimateNM.M]);
                scatterUnUltimate.LegendText = "低減前安全限界";

                var scatterFaUltimate = WpfPlot.Plot.Add.ScatterLine(
                    pileSection.FactoredUltimateNM.N.ToArray(), [.. pileSection.FactoredUltimateNM.M]);
                scatterFaUltimate.LegendText = "低減後安全限界";

                List<double> axialForceResultsVL = [];
                List<double> momentResultsVL = [];

                List<double> axialForceResultsLevel1 = [];
                List<double> momentResultsLevel1 = [];

                List<double> axialForceResultsLevel2 = [];
                List<double> momentResultsLevel2 = [];

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        {
                            continue;
                        }
                        LoadCase loadCase;
                        double axialForce;
                        if (SelectedLoadCaseOption == "VL0")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVL0;
                            axialForce = pileLayoutDataItem.AxialForceVL0;
                        }
                        else if (SelectedLoadCaseOption == "VLadd")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVLadd;
                            axialForce = pileLayoutDataItem.AxialForceVLAdditional;
                        }
                        else //if (SelectedLoadCaseOption == "VL")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVL;
                            axialForce = pileLayoutDataItem.AxialForceVL;
                        }

                        axialForceResultsVL.Add(axialForce);
                        momentResultsVL.Add(0);

                        var scatterResult = WpfPlot.Plot.Add.Scatter(axialForceResultsVL.ToArray(), [.. momentResultsVL]);
                        scatterResult.LegendText = GetPileVLLegendText(loadCase, pileLayoutDataItem);
                        scatterResult.LineStyle.Width = 0;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "MNINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }

                else
                {
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        {
                            continue;
                        }

                        foreach (LoadCase loadCase in GetSelectedLoadCases())
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                            {
                                foreach (var isLiquefaction in SelectedLiquefactionCases)
                                {
                                    double moment = double.MinValue;

                                    // PileBodySegmentループ
                                    for (int i = 0; i < InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1].PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1].PileBodySegments[i];
                                        if (pileBodySegment.No == SelectedPileSegmentNo)
                                        {
                                            moment = Math.Max(moment, pileLayoutDataItem.Beams[i].GetBeamResult(
                                                AnaModel, loadCase, loadCombination, isLiquefaction).CumulativeForce.MabsMax);
                                        }

                                        if (loadCase.Level == 1)
                                        {
                                            axialForceResultsLevel1.Add(axialForce);
                                            momentResultsLevel1.Add(moment);
                                        }

                                        else if (loadCase.Level == 2)
                                        {
                                            axialForceResultsLevel2.Add(axialForce);
                                            momentResultsLevel2.Add(moment);
                                        }
                                    }
                                }
                            }
                        }

                        var scatterResultLevel1 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel1.ToArray(), [.. momentResultsLevel1]);
                        scatterResultLevel1.LegendText = "レベル1地震時";
                        scatterResultLevel1.LineStyle.Width = 0;

                        var scatterResultLevel2 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel2.ToArray(), [.. momentResultsLevel2]);
                        scatterResultLevel2.LegendText = "レベル2地震時";
                        scatterResultLevel2.LineStyle.Width = 0;

                        ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                        WpfPlot.Plot.ShowLegend();
                        WpfPlot.Refresh();
                    }
                }
            }
            else if (SelectedGraphOption == "杭")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawPileForce(WpfPlot1, MyCrosshair1, "CrosshairPositionText1", "F", "kN");
                DrawPileForce(WpfPlot2, MyCrosshair2, "CrosshairPositionText2", "M", "kNm");
                DrawPileDisp(WpfPlot3, MyCrosshair3, "CrosshairPositionText3", "U", "mm");

            }
            else if (SelectedGraphOption == "単杭沈下" ||
                SelectedGraphOption == "群杭沈下" ||
                SelectedGraphOption == "単杭+群杭沈下")
            {
                if (GridOptions.Count == 0)
                {
                    System.Windows.MessageBox.Show("沈下グラフを描くには、杭心を通る通り心を定義してください");
                }

                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = false;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = true;


                foreach (var gridItem in InputModel.GridXItems) // Y軸平行通り心
                {
                    var gridID = (gridItem.Name + "(X=" + $"{gridItem.Coord:N1}" + ")");
                    if (SelectedGridOption == gridID)
                    {
                        DrawSettlement(gridItem, "X");
                    }
                }
                foreach (var gridItem in InputModel.GridYItems) // X軸平行通り心
                {
                    var gridID = (gridItem.Name + "(Y=" + $"{gridItem.Coord:N1}" + ")");
                    if (SelectedGridOption == gridID)
                    {
                        DrawSettlement(gridItem, "Y");
                    }
                }
            }

            else if (SelectedGraphOption.StartsWith("杭頭M-θ"))
            {
                // M-θ は水平解析結果に依存
                IsLoadCaseOptionVisible = true;            // 軸力Nをロードケースから選びたい場合に備えてON（曲線自体はAnaModelに既に設定済み）
                IsLoadCombinationOptionVisible = false;
                IsPileOptionVisible = true;                // 対象杭選択
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                DrawMThetaCurves(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            // レジェンド描画
            UpdateLegendVisibility();
        }

        // 杭頭M-θ関係描画
        // 杭頭M-θ関係描画（荷重ケース・組合せ・軸力付きレジェンド）
        private void DrawMThetaCurves(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            var model = AnaModel;
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0)
            {
                wpfPlot.Plot.Clear();
                wpfPlot.Refresh();
                return;
            }

            // 選択された杭（All の場合は全て）
            var targetPiles = GetSelectedPileLayouts();
            var targetPileNos = new HashSet<int>(targetPiles.Select(p => p.No));

            // 対象荷重ケース・荷重組合せ
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();

            wpfPlot.Plot.Clear();

            // 各荷重ケース×組合せで曲線を出し分け（軸力依存を視覚化）
            foreach (var loadCase in selectedLoadCases)
            {
                foreach (var rs in model.RotationalSprings)
                {
                    // 対応杭レイアウト探索
                    PileLayoutDataItem? pileLayout = null;

                    // PileBodyNo 経由
                    if (rs.PileBodyNo is int pb && pb > 0 && pb <= InputModel.PileBodies.Count)
                    {
                        // 杭体 pb に属する杭を一つ選ぶ（複数ある構成なら NodeJ 照合へフォールバック）
                        pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileBodyNo == pb);
                    }
                    // NodeJ 照合フォールバック
                    if (pileLayout == null && rs.NodeJ != null)
                    {
                        pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileNodes.Count > 0 && ReferenceEquals(pl.PileNodes[0], rs.NodeJ));
                    }

                    if (pileLayout == null) continue;
                    if (SelectedPileOption != "All" && !targetPileNos.Contains(pileLayout.No)) continue;

                    // 軸力推定
                    double axialN = 0.0;
                    // (1) LoadCase に NonlinearAxialForceN があれば
                    var prop = loadCase.GetType().GetProperty("NonlinearAxialForceN");
                    if (prop?.GetValue(loadCase) is double nlc && double.IsFinite(nlc) && nlc != 0.0)
                    {
                        axialN = nlc;
                    }
                    else
                    {
                        // (2) 杭個別地震軸力
                        double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                        if (double.IsFinite(nSeis) && nSeis != 0.0)
                            axialN = nSeis;
                        else
                        {
                            // (3) 現在累積軸力
                            if (double.IsFinite(pileLayout.AxialForce) && pileLayout.AxialForce != 0.0)
                                axialN = pileLayout.AxialForce;
                        }
                    }

                    // 曲線 or 線形
                    double[] thetas;
                    double[] moments;
                    string modeTag;
                    if (rs.Mode == RotationalSpringMode.CombinedXY && rs.CurveXY != null)
                    {
                        (thetas, moments) = rs.CurveXY.ToArrays();
                        modeTag = "XY";
                    }
                    else if (rs.Mode == RotationalSpringMode.SingleDof && rs.Curve != null)
                    {
                        (thetas, moments) = rs.Curve.ToArrays();
                        modeTag = rs.Dof.ToString();
                    }
                    else
                    {
                        // 線形補完
                        double? k = rs.Mode == RotationalSpringMode.CombinedXY ? rs.KthetaXY : rs.Ktheta;
                        if (!k.HasValue || k.Value <= 0.0) continue;
                        const double thetaMax = 0.02;
                        int nDiv = 50;
                        thetas = Enumerable.Range(0, nDiv).Select(i => i * thetaMax / (nDiv - 1)).ToArray();
                        moments = thetas.Select(t => k.Value * t).ToArray();
                        modeTag = rs.Mode == RotationalSpringMode.CombinedXY ? "XY" : rs.Dof.ToString();
                    }
                    if (thetas.Length == 0 || moments.Length == 0) continue;

                    // レジェンド（荷重ケース名, 組合せ, 軸力, 杭番号, モード）
                    string legend = $"LC:{loadCase.LoadName}|N:{axialN:F0}|Pile:{pileLayout.No}|Mode:{modeTag}";
                    var scatter = wpfPlot.Plot.Add.Scatter(thetas, moments);
                    scatter.LegendText = legend;
                }
            }

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, "杭頭M-θ", "θ (rad)", "M (kN·m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 変位描画
        private void DrawSettlement(GridDataItem gridItem, string axis)
        {
            string xTitle = $"{axis}(m)";
            string yTitle = "沈下量(mm)";

            List<double> xs = [];
            List<double> ys = [];
            double xOnGrid = 0;
            double yOnGrid = 0;
            foreach (var pile in gridItem.Piles)
            {
                if (axis == "X") // X平行
                {
                    xOnGrid = pile.X;
                    xs.Add(pile.Y); // Y座標追加
                }
                else // axis ="Y"
                {
                    yOnGrid = pile.Y;
                    xs.Add(pile.X); // X座標追加
                }
                if (SelectedGraphOption == "単杭沈下" ||
                    SelectedGraphOption == "単杭+群杭沈下")
                {
                    if (SelectedLoadCaseOption == "VL")
                    {
                        if (SelectedGraphOption == "単杭沈下")
                        {
                            ys.Add(pile.SinglePileSettlementVL);
                        }
                        else // if (SelectedGraphOption == "単杭+群杭沈下")
                        {
                            ys.Add(pile.SinglePileSettlementVL + pile.GroupPileSettlement);
                        }

                    }
                    else // VL以外
                    {
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel1[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "単杭沈下")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i]);
                                    break;
                                }
                                else // if (SelectedGraphOption == "単杭+群杭沈下")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i] + pile.GroupPileSettlement);
                                    break;
                                }
                            }
                        }
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel2[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "単杭沈下")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i]);
                                    break;
                                }
                                else // if (SelectedGraphOption == "単杭+群杭沈下")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i] + pile.GroupPileSettlement);
                                    break;
                                }
                            }
                            return;
                        }
                    }
                }
                else if (SelectedGraphOption == "群杭沈下")
                {
                    ys.Add(pile.GroupPileSettlement);
                }
            }

            /*var scatter = */
            WpfPlot.Plot.Add.Scatter(xs, ys);

            List<double> midxs = [];
            List<double> midys = [];
            for (int i = 0; i < xs.Count - 1; i++)
            {
                midxs.Add((xs[i] + xs[i + 1]) * 0.5);
                midys.Add((ys[i] + ys[i + 1]) * 0.5);
                double angle = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
                double[] xArray = [midxs[^1]];
                double[] yArray = [midys[^1]];
                var scatterAngle = WpfPlot.Plot.Add.Scatter(xArray, yArray);
                scatterAngle.LegendText = GetSettlementAnleLegendText(angle);
                scatterAngle.MarkerSize = 0;
                scatterAngle.LineWidth = 0;
            }

            //InputModel.PileGroupSettlement.GetSpecificSettlementDataItems();

            if (SelectedGraphOption == "群杭沈下" || SelectedGraphOption == "単杭+群杭沈下")
            {
                List<double> xsGround = [];
                List<double> ysGround = [];
                foreach (var settlementGridDataItem in InputModel.PileGroupSettlement.SettlementGridData)
                {
                    if (axis == "X" && xOnGrid == settlementGridDataItem.X) // X平行
                    {
                        xsGround.Add(settlementGridDataItem.Y); // Y座標追加
                        ysGround.Add(settlementGridDataItem.Settlement);
                    }
                    else if (axis == "Y" && yOnGrid == settlementGridDataItem.Y) // 
                    {
                        xsGround.Add(settlementGridDataItem.X); // X座標追加
                        ysGround.Add(settlementGridDataItem.Settlement);
                    }
                }

                /*var scatterGround = */
                WpfPlot.Plot.Add.Scatter(xsGround, ysGround);

                List<double> midxsGround = [];
                List<double> midysGround = [];
                for (int i = 0; i < xsGround.Count - 1; i++)
                {
                    midxsGround.Add((xsGround[i] + xsGround[i + 1]) / 2);
                    midysGround.Add((ysGround[i] + ysGround[i + 1]) / 2);
                    double angle = (ysGround[i + 1] - ysGround[i]) / (xsGround[i + 1] - xsGround[i]);
                    double[] xArrayGround = [midxsGround[^1]];
                    double[] yArrayGround = [midysGround[^1]];
                    var scatterAngleGround = WpfPlot.Plot.Add.Scatter(xArrayGround, yArrayGround);
                    scatterAngleGround.LegendText = GetSettlementAnleLegendText(angle);
                    scatterAngleGround.MarkerSize = 0;
                    scatterAngleGround.LineWidth = 0;
                }
            }

            ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, xTitle, yTitle);

            WpfPlot.Plot.Axes.InvertY();
            WpfPlot.Plot.ShowLegend();
            WpfPlot.Refresh();
        }

        // 杭応力描画
        private void DrawPileForce(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string forceType, string unit)
        {
            IsPileOptionVisible = true;

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Plot.Clear();
                wpfPlot.Refresh();
                return;
            }

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var beams = pileLayoutDataItem.Beams;

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 杭頭
                            List<double> beamZs = [beams[0]?.NodeI?.Coord.Z ?? 0];
                            List<double> beamForces = [0];

                            foreach (var beam in beams)
                            {
                                if (beam?.NodeI?.Coord == null || beam?.NodeJ?.Coord == null) continue;
                                beamZs.Add(beam.NodeI.Coord.Z);
                                beamZs.Add(beam.NodeJ.Coord.Z);

                                var result = beam.GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result?.CumulativeForce == null) continue;

                                if (forceType == "Fy")
                                {
                                    beamForces.Add(result.CumulativeForce.Fyi);
                                    beamForces.Add(-result.CumulativeForce.Fyj);
                                }
                                else if (forceType == "Fz")
                                {
                                    beamForces.Add(result.CumulativeForce.Fzi);
                                    beamForces.Add(-result.CumulativeForce.Fzj);
                                }
                                else if (forceType == "F")
                                {
                                    beamForces.Add(result.CumulativeForce.Fi);
                                    beamForces.Add(result.CumulativeForce.Fj);
                                }
                                else if (forceType == "My")
                                {
                                    beamForces.Add(result.CumulativeForce.Myi);
                                    beamForces.Add(-result.CumulativeForce.Myj);
                                }
                                else if (forceType == "Mz")
                                {
                                    beamForces.Add(result.CumulativeForce.Mzi);
                                    beamForces.Add(-result.CumulativeForce.Mzj);
                                }
                                else if (forceType == "M")
                                {
                                    beamForces.Add(result.CumulativeForce.Mi);
                                    beamForces.Add(result.CumulativeForce.Mj);
                                }
                            }

                            // 杭先端
                            beamZs.Add(beams[^1].NodeJ.Coord.Z);
                            beamForces.Add(0);

                            var scatter = wpfPlot.Plot.Add.Scatter(beamForces, beamZs);
                            scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                        }
                    }
                }
            }

            string axisX = forceType + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, SelectedGraphOption, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 杭変位描画
        private void DrawPileDisp(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string dispType, string unit)

        {
            IsPileOptionVisible = true;

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Plot.Clear();
                wpfPlot.Refresh();
                return;
            }

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var beams = pileLayoutDataItem.Beams;
                var pileNodes = pileLayoutDataItem.PileNodes;
                var soilNodes = pileLayoutDataItem.SoilNodes;

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            List<double> pileZs = [];
                            List<double> pileDisps = [];

                            List<double> soilZs = [];
                            List<double> soilDisps = [];

                            foreach (var pileNode in pileNodes)
                            {
                                pileZs.Add(pileNode.Coord.Z);
                                var result = pileNode.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result == null || result.CumulativeDisp == null)
                                {
                                    pileDisps.Add(0); // または continue; でスキップ
                                    continue;
                                }

                                if (dispType == "UX")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Ux * 1000.0);
                                }
                                else if (dispType == "UY")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uy * 1000.0);
                                }
                                else if (dispType == "U")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uh * 1000.0);
                                }
                                else
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uz * 1000.0);
                                }
                            }

                            foreach (var soilNode in soilNodes)
                            {
                                soilZs.Add(soilNode.Coord.Z);
                                var result = soilNode.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                //soilDisps.Add(result.CumulativeDisp.Uh);
                                if (dispType == "UX")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Ux * 1000.0);
                                }
                                else if (dispType == "UY")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uy * 1000.0);
                                }
                                else if (dispType == "U")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uh * 1000.0);
                                }
                                else
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uz * 1000.0);
                                }
                            }

                            var scatterPile = wpfPlot.Plot.Add.Scatter(pileDisps, pileZs);
                            var scatterSoil = wpfPlot.Plot.Add.Scatter(soilDisps, soilZs);
                            scatterSoil.LineStyle.Pattern = LinePattern.Dashed;

                            scatterPile.LegendText = "(PILE), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                            scatterSoil.LegendText = "(SOIL), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                        }
                    }
                }
            }

            string axisX = dispType + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, SelectedGraphOption, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }


        // 杭レジェンドテキスト取得メソッド
        private static string GetPileVLLegendText(LoadCase loadCase, PileLayoutDataItem pileLayoutDataItem)
        {
            return loadCase.LoadName + "|" + pileLayoutDataItem.No;
        }

        // 杭レジェンドテキスト取得メソッド
        private static string GetPileLegendText(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, PileLayoutDataItem pileLayoutDataItem)
        {
            return loadCase.LoadName + "|" + loadCombination.No + "|LIQ:" + isLiquefaction + "|" + pileLayoutDataItem.No;
        }

        // 一般レジェンド取得メソッド
        private static string GetGeneralLegendText(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction)
        {
            return loadCase.LoadName + "|" + loadCombination.No + "|LIQ:" + isLiquefaction;
        }

        // 沈下傾斜レジェンド取得メソッド
        //private static string GetSettlementLegendText(double settlement)
        //{
        //    return $"沈下量{settlement * 1000:N1}mm";
        //}

        // 沈下傾斜レジェンド取得メソッド
        private static string GetSettlementAnleLegendText(double angle)
        {
            return $"傾斜角{angle * 1000:N1}/1000";
        }
    }
}