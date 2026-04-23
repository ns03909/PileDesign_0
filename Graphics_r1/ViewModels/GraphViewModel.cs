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
        public bool IsVerticalBeamAnalysisDone { get; set; }

        private string _graphErrorMessage;
        public string GraphErrorMessage
        {
            get => _graphErrorMessage;
            set => SetProperty(ref _graphErrorMessage, value);
        }

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
                    // パネル切替（単一⇔3分割）を先に通知
                    OnPropertyChanged(nameof(IsMultiGraphVisible));
                    OnPropertyChanged(nameof(IsSingleGraphVisible));
                    OnPropertyChanged(nameof(PileSegmentLabel));
                    OnPropertyChanged(nameof(IsDistributedModeOptionVisible));
                    UpdateGraph();
                    UpdatePileSegmentDetails();
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

        // 案 C (2026-04-24): 水平地盤反力 / ばね割線剛性 を「単位長さあたり」表示に切替。
        // false: 総量 (kN, kN/m) + 折線 (従来挙動)
        // true : 単位長さあたり (kN/m, kN/m²) + ステップグラフ (分布風)
        private bool _isDistributedMode;
        public bool IsDistributedMode
        {
            get => _isDistributedMode;
            set
            {
                if (SetProperty(ref _isDistributedMode, value))
                {
                    UpdateGraph();
                }
            }
        }

        // XAML から Visibility 制御用
        public bool IsDistributedModeOptionVisible => SelectedGraphOption == "杭周地盤変位反力";

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
                    UpdatePileSegmentDetails();
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
                    int segmentsCount = InputModel.GetPileBodyByPileBodyRef(_selectedPileBodyRef).PileBodySegments.Count;
                    var options = new ObservableCollection<string> { "All" };
                    foreach (int i in Enumerable.Range(1, segmentsCount)) options.Add(i.ToString());
                    PileSegmentOptions = options;
                    SelectedPileSegmentOption = "All";
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

        private ObservableCollection<string> _pileSegmentOptions;
        public ObservableCollection<string> PileSegmentOptions
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


        private string _selectedPileSegmentOption = "All";
        public string SelectedPileSegmentOption
        {
            get => _selectedPileSegmentOption;
            set
            {
                if (SetProperty(ref _selectedPileSegmentOption, value))
                {
                    UpdateGraph();
                    UpdatePileSegmentDetails();
                }
            }
        }

        /// <summary>選択中の杭区間番号（0-based）。"All" の場合は -1 を返す。</summary>
        public int SelectedPileSegmentNo
        {
            get => int.TryParse(_selectedPileSegmentOption, out int n) ? n : 0;
            set => SelectedPileSegmentOption = value <= 0 ? "All" : value.ToString();
        }

        // p-y グラフ時は「杭要素番号」、他（杭体区間を指す場合）は「杭区間番号」
        public string PileSegmentLabel =>
            SelectedGraphOption == "水平地盤反力度p-y" ? "杭要素番号:" : "杭区間番号:";

        private string _selectedPileSegmentDetails = string.Empty;
        /// <summary>水平地盤反力度p-y グラフで選択中杭要素に対応する地盤/深さ等の詳細。
        /// 複数杭選択時は共通する値のみ表示、差異がある項目は「—」。</summary>
        public string SelectedPileSegmentDetails
        {
            get => _selectedPileSegmentDetails;
            private set => SetProperty(ref _selectedPileSegmentDetails, value);
        }

        // 各グラフ共通のホバー用: Scatter → 詳細文字列（p-y, M-φ/EI-φ, 杭頭M-θ 等で利用）
        private readonly Dictionary<ScottPlot.Plottables.Scatter, string> _graphHoverMap = new();

        /// <summary>
        /// 描画メソッドが Scatter を登録するためのマップ。
        /// </summary>
        internal Dictionary<ScottPlot.Plottables.Scatter, string> GraphHoverMap => _graphHoverMap;

        /// <summary>
        /// 指定した Scatter に対応する詳細文字列を返す（ホバーポップアップ用）。
        /// </summary>
        public bool TryGetGraphHoverDetails(ScottPlot.Plottables.Scatter scatter, out string details)
        {
            details = string.Empty;
            if (scatter == null) return false;
            if (_graphHoverMap.TryGetValue(scatter, out var s))
            {
                details = s;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 現在のグラフ種別に合わせて PileSegmentOptions を再構築する。
        /// p-y 以外（NMINT/QNINT/M-φ/EI-φ 等）は入力杭体セグメント数を、
        /// p-y は要素分割後の HorizontalSoilReactions 数を使う。
        /// </summary>
        private void EnsurePileSegmentOptionsForCurrentGraph()
        {
            int count = 0;
            if (SelectedGraphOption == "水平地盤反力度p-y")
            {
                var firstPile = GetSelectedPileLayouts().FirstOrDefault();
                if (firstPile != null
                    && firstPile.SoilPileAltNo > 0
                    && firstPile.SoilPileAltNo <= InputModel.ElementDivision.SoilPiles.Count)
                {
                    count = InputModel.ElementDivision.SoilPiles[firstPile.SoilPileAltNo - 1]
                        .HorizontalSoilReactions?.Count ?? 0;
                }
            }
            else
            {
                var pb = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                count = pb?.PileBodySegments?.Count ?? 0;
            }

            if (count <= 0) return;
            if (PileSegmentOptions != null && PileSegmentOptions.Count == count + 1) return;

            var opts = new ObservableCollection<string> { "All" };
            foreach (int i in Enumerable.Range(1, count)) opts.Add(i.ToString());
            PileSegmentOptions = opts;
            if (!opts.Contains(SelectedPileSegmentOption))
                SelectedPileSegmentOption = "All";
        }

        private void UpdatePileSegmentDetails()
        {
            if (SelectedGraphOption != "水平地盤反力度p-y"
                || string.IsNullOrEmpty(SelectedPileSegmentOption)
                || SelectedPileSegmentOption == "All"
                || !int.TryParse(SelectedPileSegmentOption, out int oneBased))
            {
                SelectedPileSegmentDetails = string.Empty;
                return;
            }
            int idx = oneBased - 1;

            var reactions = new List<HorizontalSoilReactionItem>();
            foreach (var pile in GetSelectedPileLayouts())
            {
                if (pile.SoilPileAltNo <= 0 || pile.SoilPileAltNo > InputModel.ElementDivision.SoilPiles.Count) continue;
                var sp = InputModel.ElementDivision.SoilPiles[pile.SoilPileAltNo - 1];
                if (sp?.HorizontalSoilReactions == null) continue;
                if (idx < 0 || idx >= sp.HorizontalSoilReactions.Count) continue;
                reactions.Add(sp.HorizontalSoilReactions[idx]);
            }

            if (reactions.Count == 0)
            {
                SelectedPileSegmentDetails = string.Empty;
                return;
            }

            static string CommonStr(IEnumerable<string> vals)
            {
                var list = vals.ToList();
                return list.All(v => v == list[0]) ? (list[0] ?? "") : "—";
            }
            static string CommonNum(IEnumerable<double> vals, string format)
            {
                var list = vals.ToList();
                return list.All(v => Math.Abs(v - list[0]) < 1e-9) ? list[0].ToString(format) : "—";
            }

            string name = CommonStr(reactions.Select(r => r.Name));
            string soilType = CommonStr(reactions.Select(r => r.SoilType));
            string zTop = CommonNum(reactions.Select(r => r.ZTop), "F3");
            string zBtm = CommonNum(reactions.Select(r => r.ZBtm), "F3");
            string b = CommonNum(reactions.Select(r => r.B * 1000.0), "F0");
            string nValue = CommonNum(reactions.Select(r => r.NValue), "F1");

            SelectedPileSegmentDetails =
                $"地盤層: {name}\n" +
                $"土質: {soilType}\n" +
                $"標高: {zTop} ~ {zBtm} m\n" +
                $"杭径 B: {b} mm\n" +
                $"N 値: {nValue}";
        }

        // M/Qdスライダー表示
        private bool _isMonQdSliderVisible;
        public bool IsMonQdSliderVisible
        {
            get => _isMonQdSliderVisible;
            set => SetProperty(ref _isMonQdSliderVisible, value);
        }

        private double _monQd = 3.0;
        public double MonQd
        {
            get => _monQd;
            set
            {
                if (SetProperty(ref _monQd, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _monQdReference = "";
        public string MonQdReference
        {
            get => _monQdReference;
            set => SetProperty(ref _monQdReference, value);
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

        // 液状化オプション（解析結果に応じて動的に更新）
        private ObservableCollection<string> _liquefactionOptions = ["両方", "液状化考慮", "液状化非考慮"];
        public ObservableCollection<string> LiquefactionOptions
        {
            get => _liquefactionOptions;
            set => SetProperty(ref _liquefactionOptions, value);
        }

        private string _selectedLiquefaction = "両方";
        public string SelectedLiquefaction
        {
            get => _selectedLiquefaction;
            set
            {
                if (SetProperty(ref _selectedLiquefaction, value))
                {
                    UpdateSelectedLiquefactionCases();
                }
            }
        }

        /// <summary>
        /// 選択された液状化オプションに基づいてSelectedLiquefactionCasesを更新
        /// </summary>
        private void UpdateSelectedLiquefactionCases()
        {
            var available = GetAvailableLiquefactionCases();

            if (SelectedLiquefaction == "両方")
            {
                // 「両方」でも実際に利用可能なケースのみを設定
                SelectedLiquefactionCases = new ObservableCollection<bool>(available);
            }
            else if (SelectedLiquefaction == "液状化考慮")
            {
                // 液状化ケースが存在する場合のみ設定
                if (available.Contains(true))
                    SelectedLiquefactionCases = [true];
                else
                    SelectedLiquefactionCases = new ObservableCollection<bool>(available);
            }
            else if (SelectedLiquefaction == "液状化非考慮")
            {
                // 非液状化ケースが存在する場合のみ設定
                if (available.Contains(false))
                    SelectedLiquefactionCases = [false];
                else
                    SelectedLiquefactionCases = new ObservableCollection<bool>(available);
            }
            else
            {
                // 単一オプションの場合、利用可能なケースを設定
                SelectedLiquefactionCases = new ObservableCollection<bool>(available);
            }
        }

        /// <summary>
        /// 解析結果から利用可能な液状化ケースを取得
        /// </summary>
        private List<bool> GetAvailableLiquefactionCases()
        {
            var available = new List<bool>();
            if (AnaModel?.AnalysisStepResults == null || AnaModel.AnalysisStepResults.Count == 0)
                return [true, false]; // デフォルト

            bool hasLiquefaction = AnaModel.AnalysisStepResults.Any(r => r.IsLiquefaction);
            bool hasNonLiquefaction = AnaModel.AnalysisStepResults.Any(r => !r.IsLiquefaction);

            if (hasLiquefaction) available.Add(true);
            if (hasNonLiquefaction) available.Add(false);

            return available.Count > 0 ? available : [true, false];
        }

        /// <summary>
        /// 解析結果に基づいて液状化オプションを更新
        /// </summary>
        private void UpdateLiquefactionOptions()
        {
            var available = GetAvailableLiquefactionCases();
            bool hasLiquefaction = available.Contains(true);
            bool hasNonLiquefaction = available.Contains(false);

            var newOptions = new ObservableCollection<string>();

            if (hasLiquefaction && hasNonLiquefaction)
            {
                newOptions.Add("両方");
                newOptions.Add("液状化考慮");
                newOptions.Add("液状化非考慮");
            }
            else if (hasLiquefaction)
            {
                newOptions.Add("液状化考慮");
            }
            else if (hasNonLiquefaction)
            {
                newOptions.Add("液状化非考慮");
            }
            else
            {
                // デフォルト
                newOptions.Add("両方");
                newOptions.Add("液状化考慮");
                newOptions.Add("液状化非考慮");
            }

            LiquefactionOptions = newOptions;

            // 選択を更新（現在の選択が利用可能でない場合は最初のオプションを選択）
            if (!LiquefactionOptions.Contains(SelectedLiquefaction))
            {
                SelectedLiquefaction = LiquefactionOptions.FirstOrDefault() ?? "両方";
            }
            else
            {
                // 選択は変わらないがSelectedLiquefactionCasesを更新
                UpdateSelectedLiquefactionCases();
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

        // 限界状態オプション
        public string[] LimitStateOptions { get; } =
        [
            "なし",
            "低減前使用限界状態",
            "低減後使用限界状態",
            "低減前損傷限界状態",
            "低減後損傷限界状態",
            "低減前安全限界状態",
            "低減後安全限界状態",
        ];

        private string _selectedLimitState = "なし";
        public string SelectedLimitState
        {
            get => _selectedLimitState;
            set
            {
                if (SetProperty(ref _selectedLimitState, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 限界状態オプション表示
        private bool _isLimitStateOptionVisible;
        public bool IsLimitStateOptionVisible
        {
            get => _isLimitStateOptionVisible;
            set => SetProperty(ref _isLimitStateOptionVisible, value);
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
            // 「杭」「水平地盤反力」のとき三分割表示
            get => SelectedGraphOption == "杭変位応力" || SelectedGraphOption == "杭周地盤変位反力";
        }

        public bool IsSingleGraphVisible
        {
            get => !IsMultiGraphVisible;
        }

        // グラフフィルタ: レベル絞込み用の特別オプション名 (他のケース名と衝突しないよう L1/L2 接頭辞)
        public const string LoadCaseFilterLevel1 = "L1 (地震荷重レベル1)";
        public const string LoadCaseFilterLevel2 = "L2 (地震荷重レベル2)";

        private ObservableCollection<LoadCase> GetSelectedLoadCases()
        {
            ObservableCollection<LoadCase> selectedLoadCases = [];
            if (SelectedLoadCaseOption == "All")
            {
                selectedLoadCases = InputModel.LoadCasesInput.AllSeismicLoadCases;
            }
            else if (SelectedLoadCaseOption == LoadCaseFilterLevel1)
            {
                foreach (var loadCase in InputModel.LoadCasesInput.AllSeismicLoadCases)
                {
                    if (loadCase.Level == 1) selectedLoadCases.Add(loadCase);
                }
            }
            else if (SelectedLoadCaseOption == LoadCaseFilterLevel2)
            {
                foreach (var loadCase in InputModel.LoadCasesInput.AllSeismicLoadCases)
                {
                    if (loadCase.Level == 2) selectedLoadCases.Add(loadCase);
                }
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
            IsVerticalBeamAnalysisDone = _mainWindowViewModel.IsVerticalBeamAnalysisDone;

            // フィルタ: 全部 / レベル絞り込み / 個別ケース
            LoadCaseOptions = ["All", LoadCaseFilterLevel1, LoadCaseFilterLevel2];
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
                GraphOptions.Add("慣性力作用点荷重変形関係");
                //GraphOptions.Add("杭頭応力変形関係F");
                //GraphOptions.Add("杭頭応力変形関係M");
                //GraphOptions.Add("杭応力F");
                //GraphOptions.Add("杭応力M");
                //GraphOptions.Add("杭変位U");
                GraphOptions.Add("杭変位応力");
                GraphOptions.Add("NMINT");
                GraphOptions.Add("QNINT");
                GraphOptions.Add("杭頭M-θ");
                GraphOptions.Add("杭体M-φ");
                GraphOptions.Add("杭体EI-φ");
                GraphOptions.Add("杭周地盤変位反力");
                GraphOptions.Add("水平地盤反力度p-y");
                // 土圧合力ばねが存在する場合のみ追加
                if (AnaModel.HorizontalSoilSprings.Any(s => s.NodeI?.Name == "根入部節点"))
                    GraphOptions.Add("土圧合力ばね");

                // FT-Pile / キャプテンパイル M-θ グラフ（該当する杭頭タイプが存在する場合のみ）
                if (InputModel.PileBodies.Any(pb => pb.PileTopType?.Contains("FT-Pile構法") == true))
                    GraphOptions.Add("FTPileM-θ");
                if (InputModel.PileBodies.Any(pb => pb.PileTopType?.Contains("キャプテンパイル工法") == true))
                    GraphOptions.Add("キャプテンパイルM-θ");

                // 鉄筋定着工法・定着筋方式で、杭頭が設定済みの場合のみ定着部NMINT
                if (InputModel.PileBodies.Any(pb =>
                    (pb.PileTopType?.Contains("鉄筋定着工法") == true ||
                     pb.PileTopType?.Contains("定着筋方式") == true) &&
                    pb.PileTop?.ConcreteOutDia > 0 &&
                    pb.PileTop?.MainBarNum1 > 0))
                    GraphOptions.Add("定着部NMINT");

            }
            if (IsVerticalAnalysisDone)
            {
                GraphOptions.Add("荷重沈下曲線");
                GraphOptions.Add("沈下 単杭");
            }
            if (IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 群杭");
            }
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 単杭+群杭");
            }
            if (IsVerticalBeamAnalysisDone)
            {
                GraphOptions.Add("沈下 基礎梁考慮単杭");
            }
            if (IsVerticalBeamAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 基礎梁考慮単杭+群杭");
            }

            if (GraphOptions.Count > 0 && string.IsNullOrEmpty(SelectedGraphOption))
            {
                SelectedGraphOption = GraphOptions[0];
            }

            // 解析結果に基づいて液状化オプションを更新
            UpdateLiquefactionOptions();
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

        // M-φ曲線から指定曲率に対応するモーメントを線形補間で取得
        private static double InterpolateMomentFromCurve(List<double> phis, List<double> moments, double phi)
        {
            if (phis == null || moments == null || phis.Count < 2 || phis.Count != moments.Count)
                return 0.0;

            phi = Math.Abs(phi); // 曲率は正値で評価

            // 範囲外チェック
            if (phi <= phis[0]) return moments[0];
            if (phi >= phis[^1]) return moments[^1];

            // 線形補間
            for (int i = 0; i < phis.Count - 1; i++)
            {
                if (phi >= phis[i] && phi <= phis[i + 1])
                {
                    double t = (phi - phis[i]) / (phis[i + 1] - phis[i]);
                    return moments[i] + t * (moments[i + 1] - moments[i]);
                }
            }

            return moments[^1];
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
                if (xLabel.StartsWith('F') || xLabel.StartsWith('Q'))
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

                // 凡例フォント: 日本語テキスト（「最終」等）を含むため、日本語対応フォントを使用
                wpfPlot.Plot.Legend.FontName = Fonts.Detect("最終");

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
            else if (SelectedGraphOption.StartsWith("NMINT") || SelectedGraphOption.StartsWith("QNINT"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("慣性力作用点荷重変形関係"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("杭頭M-θ") ||
                     SelectedGraphOption == "FTPileM-θ" || SelectedGraphOption == "キャプテンパイルM-θ")
            {
                decimalPlacesX = 4;
                decimalPlacesY = 1;
            }

            wpfPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, CrosshairPositionText, xLabel, yLabel, decimalPlacesX, decimalPlacesY);
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
            {
                wpfPlot.Refresh();
            }
        }

        // UpdateGraph 再入防止フラグ
        // Is*Visible などの property setter がここから再帰的に UpdateGraph を呼ぶと
        // Plot.Clear 後に scatter が累積してレジェンドが多重表示されるため、
        // 最外側の呼び出しのみ実処理を行う。
        private bool _isUpdatingGraph;

        // グラフ更新メソッド
        public void UpdateGraph()
        {
            if (WpfPlot == null) return;
            if (SelectedGraphOption == null) return;
            if (_isUpdatingGraph) return; // 再帰・多重呼び出しを抑制
            _isUpdatingGraph = true;
            try
            {
                UpdateGraphCore();
            }
            finally
            {
                _isUpdatingGraph = false;
            }
        }

        private void UpdateGraphCore()
        {
            WpfPlot.Plot.Clear();
            WpfPlot1.Plot.Clear();
            WpfPlot2.Plot.Clear();
            WpfPlot3.Plot.Clear();

            GraphErrorMessage = null;

            // 限界状態オプションをデフォルトで非表示
            IsLimitStateOptionVisible = false;
            IsMonQdSliderVisible = false;

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
                                // 解析結果がない場合はスキップ
                                if (lastStep < 0) continue;

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
            else if (SelectedGraphOption.StartsWith("慣性力作用点荷重変形関係"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                SelectedPileOption = "All";

                // 杭地盤ばねと土圧合力ばねを分類
                var pileSoilSprings = AnaModel.HorizontalSoilSprings
                    .Where(s => s.Name.StartsWith("杭地盤ばね")).ToList();
                var doatsuSoilSprings = AnaModel.HorizontalSoilSprings
                    .Where(s => s.NodeI?.Name == "根入部節点").ToList();

                // 重複描画防止: 同一 (LoadName, Angle, Combination.Name, IsLiq) を二重にプロットしない
                var plottedKeys = new HashSet<string>();

                // ホバーポップアップ用マップをクリア
                _graphHoverMap.Clear();

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations()
                    )
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            // 解析結果がない場合はスキップ
                            if (lastStep < 0) continue;

                            // 重複スキップ
                            string dedupKey = $"{loadCase?.LoadName}@{loadCase?.LoadAngle:F2}|{loadCombination?.Name}|{isLiquefaction}";
                            if (!plottedKeys.Add(dedupKey)) continue;

                            List<double> disps = [0];
                            List<double> forces = [0];
                            List<double> pileSoilForces = [0];
                            List<double> doatsuSoilForces = [0];

                            for (int step = 0; step <= lastStep; step++)
                            {
                                NodeResult nodeResult = AnaModel.Nodes[0].GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction, step);
                                double dispMm = nodeResult.CumulativeDisp.Uh * 1000.0;
                                disps.Add(dispMm);
                                forces.Add(nodeResult.CumulativedLoad.GetHorizontalAbsLoad());

                                // 杭地盤ばね反力の合計（作用荷重方向成分）
                                double pileSumFx = 0, pileSumFy = 0;
                                foreach (var spring in pileSoilSprings)
                                {
                                    var result = spring.HorizontalSpringResults?
                                        .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                          && r.LoadCombination?.No == loadCombination.No
                                                          && r.IsLiquefaction == isLiquefaction
                                                          && r.Step == step);
                                    if (result?.CumulativeForce != null)
                                    {
                                        pileSumFx += result.CumulativeForce.Fxi;
                                        pileSumFy += result.CumulativeForce.Fyi;
                                    }
                                }
                                // 載荷方向への射影
                                double radLC = loadCase.LoadAngle * Math.PI / 180.0;
                                double cosLC = Math.Cos(radLC);
                                double sinLC = Math.Sin(radLC);
                                pileSoilForces.Add(pileSumFx * cosLC + pileSumFy * sinLC);

                                // 土圧合力ばね反力の合計（作用荷重方向成分）
                                double doatsuSumFx = 0, doatsuSumFy = 0;
                                foreach (var spring in doatsuSoilSprings)
                                {
                                    var result = spring.HorizontalSpringResults?
                                        .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                          && r.LoadCombination?.No == loadCombination.No
                                                          && r.IsLiquefaction == isLiquefaction
                                                          && r.Step == step);
                                    if (result?.CumulativeForce != null)
                                    {
                                        doatsuSumFx += result.CumulativeForce.Fxi;
                                        doatsuSumFy += result.CumulativeForce.Fyi;
                                    }
                                }
                                doatsuSoilForces.Add(doatsuSumFx * cosLC + doatsuSumFy * sinLC);
                            }

                            string legend = GetGeneralLegendText(loadCase, loadCombination, isLiquefaction);

                            // ケースごとに共通の色を決定（3 系列を同色に揃え、線種で区別）
                            var caseColor = GetCaseColor(plottedKeys.Count - 1);

                            // ホバー共通情報
                            string hoverHeader = $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} "
                                + $"(α={loadCombination.Alpha1:F2}/β₁={loadCombination.Beta1:F2}/β₂={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                + $"ステップ数: {lastStep}";

                            var scatter = WpfPlot.Plot.Add.Scatter(disps, forces);
                            scatter.LegendText = $"慣性力 {legend}";
                            scatter.Color = caseColor;
                            scatter.MarkerStyle.FillColor = caseColor;
                            scatter.MarkerStyle.LineColor = caseColor;
                            _graphHoverMap[scatter] = hoverHeader
                                + $"\n系列: 作用点水平荷重"
                                + $"\n最終 変位: {disps[^1]:N2} mm"
                                + $"\n最終 荷重: {forces[^1]:N1} kN";

                            if (pileSoilSprings.Count > 0)
                            {
                                var scatterPile = WpfPlot.Plot.Add.Scatter(disps, pileSoilForces);
                                scatterPile.LegendText = $"杭地盤ばね反力 {legend}";
                                scatterPile.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                                scatterPile.Color = caseColor;
                                scatterPile.MarkerStyle.FillColor = caseColor;
                                scatterPile.MarkerStyle.LineColor = caseColor;
                                _graphHoverMap[scatterPile] = hoverHeader
                                    + $"\n系列: 杭地盤ばね反力合計"
                                    + $"\n最終 変位: {disps[^1]:N2} mm"
                                    + $"\n最終 反力: {pileSoilForces[^1]:N1} kN"
                                    + $"\nばね本数: {pileSoilSprings.Count}";
                            }

                            if (doatsuSoilSprings.Count > 0)
                            {
                                var scatterDoatsu = WpfPlot.Plot.Add.Scatter(disps, doatsuSoilForces);
                                scatterDoatsu.LegendText = $"土圧合力ばね反力 {legend}";
                                scatterDoatsu.LineStyle.Pattern = ScottPlot.LinePattern.Dotted;
                                scatterDoatsu.Color = caseColor;
                                scatterDoatsu.MarkerStyle.FillColor = caseColor;
                                scatterDoatsu.MarkerStyle.LineColor = caseColor;
                                _graphHoverMap[scatterDoatsu] = hoverHeader
                                    + $"\n系列: 土圧合力ばね反力合計"
                                    + $"\n最終 変位: {disps[^1]:N2} mm"
                                    + $"\n最終 反力: {doatsuSoilForces[^1]:N1} kN"
                                    + $"\nばね本数: {doatsuSoilSprings.Count}";
                            }
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

            else if (SelectedGraphOption.StartsWith("杭変位") && SelectedGraphOption != "杭変位応力")
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
                _graphHoverMap.Clear();
                EnsurePileSegmentOptionsForCurrentGraph();

                // 杭体と杭区間の取得（境界チェック付き）
                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileBodySegments == null || SelectedPileSegmentNo < 1 || SelectedPileSegmentNo > pileBody.PileBodySegments.Count)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileSection = pileBody.PileBodySegments[SelectedPileSegmentNo - 1].PileSection;
                if (pileSection == null)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                // 共通ホバー詳細（NMINT: 杭体/区間/PileSection）
                string nmSectionDetails =
                    $"杭体: {SelectedPileBodyRef} / 入力杭区間 No: {SelectedPileSegmentNo}\n" +
                    $"杭種: {pileSection.PileBodyType}\n" +
                    $"断面種別: {pileSection.PileSectionType}\n" +
                    $"杭径 D: {pileSection.PileDiameter:F0} mm\n" +
                    $"杭断面: {pileSection.PileDescription}";

                // NM曲線データが有効な場合のみ描画
                // 低減後（実線）を先に描画、低減前（破線）に同じ色を適用
                // 限界ごとに指定色を付ける: 使用=DeepBlue, 損傷=Green, 安全=PaleRed
                var serviceColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x32 << 16) | (0x71 << 8) | 0xAD))); // NikkenDeepBlue #3271AD
                var damageColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x23 << 16) | (0x89 << 8) | 0x66))); // NikkenGreen #238966
                var ultimateColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0xE9 << 16) | (0x55 << 8) | 0x41))); // NikkenPaleRed #E95541

                // 使用限界
                if (pileSection.FactoredServiceNM.N?.Count > 0 && pileSection.FactoredServiceNM.M?.Count > 0)
                {
                    var scatterFaService = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.FactoredServiceNM.N.ToArray(), pileSection.FactoredServiceNM.M.ToArray());
                    scatterFaService.LegendText = "低減後使用限界";
                    scatterFaService.LineStyle.Color = serviceColor;
                    _graphHoverMap[scatterFaService] = "低減後使用限界\n" + nmSectionDetails;
                }
                if (pileSection.UnfactoredServiceNM.N?.Count > 0 && pileSection.UnfactoredServiceNM.M?.Count > 0)
                {
                    var scatterUnService = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.UnfactoredServiceNM.N.ToArray(), pileSection.UnfactoredServiceNM.M.ToArray());
                    scatterUnService.LegendText = "低減前使用限界";
                    scatterUnService.LineStyle.Pattern = LinePattern.Dashed;
                    scatterUnService.LineStyle.Color = serviceColor;
                    _graphHoverMap[scatterUnService] = "低減前使用限界\n" + nmSectionDetails;
                }

                // 損傷限界
                if (pileSection.FactoredDamageNM.N?.Count > 0 && pileSection.FactoredDamageNM.M?.Count > 0)
                {
                    var scatterFaDamage = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.FactoredDamageNM.N.ToArray(), pileSection.FactoredDamageNM.M.ToArray());
                    scatterFaDamage.LegendText = "低減後損傷限界";
                    scatterFaDamage.LineStyle.Color = damageColor;
                    _graphHoverMap[scatterFaDamage] = "低減後損傷限界\n" + nmSectionDetails;
                }
                if (pileSection.UnfactoredDamageNM.N?.Count > 0 && pileSection.UnfactoredDamageNM.M?.Count > 0)
                {
                    var scatterUnDamage = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.UnfactoredDamageNM.N.ToArray(), pileSection.UnfactoredDamageNM.M.ToArray());
                    scatterUnDamage.LegendText = "低減前損傷限界";
                    scatterUnDamage.LineStyle.Pattern = LinePattern.Dashed;
                    scatterUnDamage.LineStyle.Color = damageColor;
                    _graphHoverMap[scatterUnDamage] = "低減前損傷限界\n" + nmSectionDetails;
                }

                // 安全限界
                if (pileSection.FactoredUltimateNM.N?.Count > 0 && pileSection.FactoredUltimateNM.M?.Count > 0)
                {
                    var scatterFaUltimate = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.FactoredUltimateNM.N.ToArray(), pileSection.FactoredUltimateNM.M.ToArray());
                    scatterFaUltimate.LegendText = "低減後安全限界";
                    scatterFaUltimate.LineStyle.Color = ultimateColor;
                    _graphHoverMap[scatterFaUltimate] = "低減後安全限界\n" + nmSectionDetails;
                }
                if (pileSection.UnfactoredUltimateNM.N?.Count > 0 && pileSection.UnfactoredUltimateNM.M?.Count > 0)
                {
                    var scatterUnUltimate = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.UnfactoredUltimateNM.N.ToArray(), pileSection.UnfactoredUltimateNM.M.ToArray());
                    scatterUnUltimate.LegendText = "低減前安全限界";
                    scatterUnUltimate.LineStyle.Pattern = LinePattern.Dashed;
                    scatterUnUltimate.LineStyle.Color = ultimateColor;
                    _graphHoverMap[scatterUnUltimate] = "低減前安全限界\n" + nmSectionDetails;
                }

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
                                    // 解析結果がない場合はスキップ
                                    int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                    if (lastStep < 0) continue;

                                    double moment = double.MinValue;
                                    double analysisFxi = 0; // 解析結果の軸力

                                    // PileBodySegmentループ
                                    bool isAllSegs = SelectedPileSegmentNo <= 0;
                                    var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                                    for (int i = 0; i < soilPile.PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = soilPile.PileBodySegments[i];
                                        if (isAllSegs || pileBodySegment.No == SelectedPileSegmentNo)
                                        {
                                            // Beamsのnullチェックとインデックス範囲チェック
                                            if (pileLayoutDataItem.Beams != null && i < pileLayoutDataItem.Beams.Count)
                                            {
                                                var beamResult = pileLayoutDataItem.Beams[i]?.GetBeamResult(
                                                    AnaModel, loadCase, loadCombination, isLiquefaction);
                                                if (beamResult?.CumulativeForce != null)
                                                {
                                                    moment = Math.Max(moment, beamResult.CumulativeForce.MabsMax);
                                                    analysisFxi = beamResult.CumulativeForce.Fxi;
                                                }
                                            }
                                        }
                                    }

                                    // 入力値＋応力解析結果モード: Fxi（圧縮負）を符号反転して加算
                                    double plotAxialForce = InputModel.UseAnalysisAxialForce
                                        ? axialForce - analysisFxi
                                        : axialForce;

                                    // セグメントループの外で結果を追加
                                    if (loadCase.Level == 1)
                                    {
                                        axialForceResultsLevel1.Add(plotAxialForce);
                                        momentResultsLevel1.Add(moment);
                                    }
                                    else if (loadCase.Level == 2)
                                    {
                                        axialForceResultsLevel2.Add(plotAxialForce);
                                        momentResultsLevel2.Add(moment);
                                    }
                                }
                            }
                        }
                    }

                    // 全杭分のデータを 2 系列（Level1/Level2）にまとめて一度だけ描画
                    if (axialForceResultsLevel1.Count > 0)
                    {
                        var scatterResultLevel1 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel1.ToArray(), [.. momentResultsLevel1]);
                        scatterResultLevel1.LegendText = "レベル1地震時";
                        scatterResultLevel1.LineStyle.Width = 0;
                    }
                    if (axialForceResultsLevel2.Count > 0)
                    {
                        var scatterResultLevel2 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel2.ToArray(), [.. momentResultsLevel2]);
                        scatterResultLevel2.LegendText = "レベル2地震時";
                        scatterResultLevel2.LineStyle.Width = 0;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
            }
            else if (SelectedGraphOption == "定着部NMINT")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                WpfPlot.Plot.Clear();

                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileTop == null ||
                    (pileBody.PileTopType?.Contains("鉄筋定着工法") != true &&
                     pileBody.PileTopType?.Contains("定着筋方式") != true))
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "定着部NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileTop = pileBody.PileTop;

                // 使用限界
                ScottPlot.Color serviceColor = default;
                if (pileTop.UnfactoredServiceNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredServiceNM.N.ToArray(), pileTop.UnfactoredServiceNM.M.ToArray());
                    s.LegendText = "使用限界";
                    serviceColor = s.LineStyle.Color;
                }

                // 損傷限界
                if (pileTop.UnfactoredDamageNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredDamageNM.N.ToArray(), pileTop.UnfactoredDamageNM.M.ToArray());
                    s.LegendText = "損傷限界";
                }

                // 安全限界
                if (pileTop.UnfactoredUltimateNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredUltimateNM.N.ToArray(), pileTop.UnfactoredUltimateNM.M.ToArray());
                    s.LegendText = "安全限界";
                }

                // 解析結果プロット（杭頭N,M）
                List<double> axialForceResultsL1 = [];
                List<double> momentResultsL1 = [];
                List<double> axialForceResultsL2 = [];
                List<double> momentResultsL2 = [];

                foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                {
                    if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        continue;

                    foreach (LoadCase loadCase in GetSelectedLoadCases())
                    {
                        var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                        foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                if (lastStep < 0) continue;

                                double moment = double.MinValue;
                                double analysisFxi = 0;

                                // 杭頭（最上端要素の上端）の応力を取得
                                if (pileLayoutDataItem.Beams != null && pileLayoutDataItem.Beams.Count > 0)
                                {
                                    var beamResult = pileLayoutDataItem.Beams[0]?.GetBeamResult(
                                        AnaModel, loadCase, loadCombination, isLiquefaction);
                                    if (beamResult?.CumulativeForce != null)
                                    {
                                        moment = beamResult.CumulativeForce.MabsMax;
                                        analysisFxi = beamResult.CumulativeForce.Fxi;
                                    }
                                }

                                if (moment <= double.MinValue) continue;

                                double plotAxialForce = InputModel.UseAnalysisAxialForce
                                    ? axialForce - analysisFxi
                                    : axialForce;

                                if (loadCase.Level == 1)
                                {
                                    axialForceResultsL1.Add(plotAxialForce);
                                    momentResultsL1.Add(moment);
                                }
                                else if (loadCase.Level == 2)
                                {
                                    axialForceResultsL2.Add(plotAxialForce);
                                    momentResultsL2.Add(moment);
                                }
                            }
                        }
                    }
                }

                if (axialForceResultsL1.Count > 0)
                {
                    var sc = WpfPlot.Plot.Add.Scatter(axialForceResultsL1.ToArray(), momentResultsL1.ToArray());
                    sc.LegendText = "レベル1地震時";
                    sc.LineStyle.Width = 0;
                }
                if (axialForceResultsL2.Count > 0)
                {
                    var sc = WpfPlot.Plot.Add.Scatter(axialForceResultsL2.ToArray(), momentResultsL2.ToArray());
                    sc.LegendText = "レベル2地震時";
                    sc.LineStyle.Width = 0;
                }

                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "定着部NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();
            }
            else if (SelectedGraphOption.StartsWith("QNINT"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;
                IsMonQdSliderVisible = true;

                WpfPlot.Plot.Clear();
                _graphHoverMap.Clear();
                EnsurePileSegmentOptionsForCurrentGraph();

                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileBodySegments == null || SelectedPileSegmentNo < 1 || SelectedPileSegmentNo > pileBody.PileBodySegments.Count)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileSection = pileBody.PileBodySegments[SelectedPileSegmentNo - 1].PileSection;
                if (pileSection == null)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Refresh();
                    return;
                }

                // 共通ホバー詳細（QNINT: 杭体/区間/PileSection/MonQd）
                string qnSectionDetails =
                    $"杭体: {SelectedPileBodyRef} / 入力杭区間 No: {SelectedPileSegmentNo}\n" +
                    $"杭種: {pileSection.PileBodyType}\n" +
                    $"断面種別: {pileSection.PileSectionType}\n" +
                    $"杭径 D: {pileSection.PileDiameter:F0} mm\n" +
                    $"杭断面: {pileSection.PileDescription}\n" +
                    $"M/(Q·d): {MonQd:N2}";

                // QN曲線データ描画（MonQdスライダー値で再計算、低減後=実線、低減前=同色破線）
                var qnCurves = pileSection.ComputeQNForMonQd(MonQd);
                // 既製杭でない場合はキャッシュ値にフォールバック
                if (qnCurves.UnfactoredService.N == null)
                {
                    qnCurves = (
                        pileSection.UnfactoredServiceNQ, pileSection.FactoredServiceNQ,
                        pileSection.UnfactoredDamageNQ, pileSection.FactoredDamageNQ,
                        pileSection.UnfactoredUltimateNQ, pileSection.FactoredUltimateNQ
                    );
                }

                // 限界ごとの色: 使用=DeepBlue, 損傷=Green, 安全=PaleRed
                var qnServiceColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x32 << 16) | (0x71 << 8) | 0xAD))); // NikkenDeepBlue #3271AD
                var qnDamageColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x23 << 16) | (0x89 << 8) | 0x66))); // NikkenGreen #238966
                var qnUltimateColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0xE9 << 16) | (0x55 << 8) | 0x41))); // NikkenPaleRed #E95541

                void DrawQNCurvePair(
                    (List<double> N, List<double> Q) factored,
                    (List<double> N, List<double> Q) unfactored,
                    string label,
                    ScottPlot.Color color)
                {
                    if (factored.N?.Count > 0 && factored.Q?.Count > 0)
                    {
                        var sc = WpfPlot.Plot.Add.ScatterLine(factored.N.ToArray(), factored.Q.ToArray());
                        sc.LegendText = $"低減後{label}";
                        sc.LineStyle.Color = color;
                        _graphHoverMap[sc] = $"低減後{label}\n" + qnSectionDetails;
                    }
                    if (unfactored.N?.Count > 0 && unfactored.Q?.Count > 0)
                    {
                        var sc = WpfPlot.Plot.Add.ScatterLine(unfactored.N.ToArray(), unfactored.Q.ToArray());
                        sc.LegendText = $"低減前{label}";
                        sc.LineStyle.Pattern = LinePattern.Dashed;
                        sc.LineStyle.Color = color;
                        _graphHoverMap[sc] = $"低減前{label}\n" + qnSectionDetails;
                    }
                }

                DrawQNCurvePair(qnCurves.FactoredService, qnCurves.UnfactoredService, "使用限界", qnServiceColor);
                DrawQNCurvePair(qnCurves.FactoredDamage, qnCurves.UnfactoredDamage, "損傷限界", qnDamageColor);
                DrawQNCurvePair(qnCurves.FactoredUltimate, qnCurves.UnfactoredUltimate, "安全限界", qnUltimateColor);

                // 解析結果プロット
                List<double> axialForceResultsLevel1Q = [];
                List<double> shearResultsLevel1 = [];
                List<double> axialForceResultsLevel2Q = [];
                List<double> shearResultsLevel2 = [];

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    // 常時荷重: せん断力=0
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                            continue;

                        double axialForce = SelectedLoadCaseOption == "VL0" ? pileLayoutDataItem.AxialForceVL0
                            : SelectedLoadCaseOption == "VLadd" ? pileLayoutDataItem.AxialForceVLAdditional
                            : pileLayoutDataItem.AxialForceVL;

                        var scatter = WpfPlot.Plot.Add.Scatter(new double[] { axialForce }, new double[] { 0.0 });
                        scatter.LegendText = $"P{pileLayoutDataItem.PileNo}";
                        scatter.LineStyle.Width = 0;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
                else
                {
                    double globalMaxM = 0, globalMaxQ = 0;

                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                            continue;

                        foreach (LoadCase loadCase in GetSelectedLoadCases())
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                            {
                                foreach (var isLiquefaction in SelectedLiquefactionCases)
                                {
                                    int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                    if (lastStep < 0) continue;

                                    double shear = double.MinValue;

                                    bool isAllSegsQ = SelectedPileSegmentNo <= 0;
                                    var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                                    for (int i = 0; i < soilPile.PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = soilPile.PileBodySegments[i];
                                        if (isAllSegsQ || pileBodySegment.No == SelectedPileSegmentNo)
                                        {
                                            if (pileLayoutDataItem.Beams != null && i < pileLayoutDataItem.Beams.Count)
                                            {
                                                var beamResult = pileLayoutDataItem.Beams[i]?.GetBeamResult(
                                                    AnaModel, loadCase, loadCombination, isLiquefaction);
                                                if (beamResult?.CumulativeForce != null)
                                                {
                                                    shear = Math.Max(shear, beamResult.CumulativeForce.FabsMax);
                                                }
                                            }
                                        }
                                    }

                                    if (loadCase.Level == 1)
                                    {
                                        axialForceResultsLevel1Q.Add(axialForce);
                                        shearResultsLevel1.Add(shear);
                                    }
                                    else if (loadCase.Level == 2)
                                    {
                                        axialForceResultsLevel2Q.Add(axialForce);
                                        shearResultsLevel2.Add(shear);
                                    }
                                }
                            }
                        }

                        // 該当杭体全 beam の最大 M / Q を集計（MonQd 算定用）
                        if (pileLayoutDataItem.Beams != null)
                        {
                            foreach (var b in pileLayoutDataItem.Beams)
                            {
                                foreach (var br in b.BeamResults)
                                {
                                    if (br.CumulativeForce == null) continue;
                                    globalMaxM = Math.Max(globalMaxM, br.CumulativeForce.MabsMax);
                                    globalMaxQ = Math.Max(globalMaxQ, br.CumulativeForce.FabsMax);
                                }
                            }
                        }
                    }

                    // 全杭分のデータを 2 系列（Level1/Level2）にまとめて一度だけ描画
                    if (axialForceResultsLevel1Q.Count > 0)
                    {
                        var scatterLevel1 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel1Q.ToArray(), [.. shearResultsLevel1]);
                        scatterLevel1.LegendText = "レベル1地震時";
                        scatterLevel1.LineStyle.Width = 0;
                    }
                    if (axialForceResultsLevel2Q.Count > 0)
                    {
                        var scatterLevel2 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel2Q.ToArray(), [.. shearResultsLevel2]);
                        scatterLevel2.LegendText = "レベル2地震時";
                        scatterLevel2.LineStyle.Width = 0;
                    }

                    // MonQd 参照値
                    double maxMonQd = 0;
                    double d = pileSection.EffectiveDepth; // [mm]
                    if (d > 0 && globalMaxQ > 0)
                        maxMonQd = (globalMaxM * 1e6) / (globalMaxQ * 1e3 * d);
                    MonQdReference = maxMonQd > 0 ? $"解析結果最大値: {maxMonQd:N2}" : "";

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
            }
            else if (SelectedGraphOption == "杭変位応力")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;
                IsLimitStateOptionVisible = true;

                // ホバーポップアップ用マップをクリア（3 パネル共通）
                _graphHoverMap.Clear();

                try { DrawPileDisp(WpfPlot1, MyCrosshair1, "CrosshairPositionText1", "U", "mm"); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[杭変位応力/Disp] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"変位グラフ描画エラー: {ex.Message}"; }
                try { DrawPileForce(WpfPlot2, MyCrosshair2, "CrosshairPositionText2", "F", "kN"); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[杭変位応力/Force] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"せん断力グラフ描画エラー: {ex.Message}"; }
                try { DrawPileForce(WpfPlot3, MyCrosshair3, "CrosshairPositionText3", "M", "kNm"); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[杭変位応力/Moment] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"曲げモーメントグラフ描画エラー: {ex.Message}"; }

            }
            else if (SelectedGraphOption == "荷重沈下曲線")
            {
                IsLoadCaseOptionVisible = false;
                IsLoadCombinationOptionVisible = false;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                DrawLoadSettlementCurve();
            }
            else if (SelectedGraphOption == "沈下 単杭" ||
                SelectedGraphOption == "沈下 群杭" ||
                SelectedGraphOption == "沈下 単杭+群杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
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
                // 杭頭M-θ: 任意の荷重ケース、任意の杭の杭頭M-θ曲線を描画（最終ステップマーカー付き）
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawMThetaCurvesWithMarker(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "FTPileM-θ" || SelectedGraphOption == "キャプテンパイルM-θ")
            {
                // FT-Pile / キャプテンパイル M-θ: N値ごとの曲線群 + 最終ステップマーカー
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawPileHeadTypeMTheta(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "杭体M-φ" || SelectedGraphOption == "杭体EI-φ")
            {
                // 杭体M-φ / EI-φ: 任意の荷重ケース、任意の杭、任意の要素のM-φ曲線を描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                EnsurePileSegmentOptionsForCurrentGraph();
                DrawMPhiCurves(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "杭周地盤変位反力")
            {
                // 水平地盤反力: 深さ-相対変位、深さ-水平地盤反力、深さ-ばね割線剛性の3パネル描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                // ホバーポップアップ用マップをクリア（3 パネル共通）
                _graphHoverMap.Clear();

                DrawHorizontalSoilReaction(WpfPlot1, MyCrosshair1, "CrosshairPositionText1", "RelativeDisp", "mm");
                // 案 C (v2): IsDistributedMode で単位表示を切替
                //   OFF: 総反力 [kN] / 総剛性 [kN/m]
                //   ON : 反力度 [kN/m²] (L, B で除算) / 反力係数 [kN/m³]
                string reactionUnit = IsDistributedMode ? "kN/m²" : "kN";
                string stiffnessUnit = IsDistributedMode ? "kN/m³" : "kN/m";
                DrawHorizontalSoilReaction(WpfPlot2, MyCrosshair2, "CrosshairPositionText2", "Reaction", reactionUnit);
                DrawHorizontalSoilReaction(WpfPlot3, MyCrosshair3, "CrosshairPositionText3", "SecantStiffness", stiffnessUnit);
            }
            else if (SelectedGraphOption == "水平地盤反力度p-y")
            {
                // 水平地盤反力度p-y: 各要素の理論P-y曲線 + 最終ステップマーカー
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                // 杭区間オプションを要素分割後のセグメント数に更新
                EnsurePileSegmentOptionsForCurrentGraph();

                DrawPyCurvesWithMarker(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "土圧合力ばね")
            {
                // 土圧合力ばね: 1パネルに最上点・最下点の2系列を描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawDoatsuGoryokuBane(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            // レジェンド描画
            UpdateLegendVisibility();

        }

        // 土圧合力ばね: 1つのグラフに最上点・最下点の相対変位をX軸とした2系列を描画
        private void DrawDoatsuGoryokuBane(WpfPlot wpfPlot, Crosshair crosshair, string crosshairPositionText)
        {
            wpfPlot.Plot.Clear();

            var doatsuSprings = AnaModel.HorizontalSoilSprings
                .Where(s => s.NodeI?.Name == "根入部節点").ToList();
            if (doatsuSprings.Count == 0) return;

            // 最上点・最下点のばねを特定（Z座標で判定）
            var topSpring = doatsuSprings.OrderByDescending(s => s.NodeI.Coord.Z).FirstOrDefault();
            var btmSpring = doatsuSprings.OrderBy(s => s.NodeI.Coord.Z).FirstOrDefault();
            if (topSpring == null || btmSpring == null) return;

            double maxDispMm = 0; // 全系列の最大相対変位を追跡
            int caseIndex = 0; // 同一ケース (LC/Comb/Liq) の 3 系列を同色にするためのカウンタ

            foreach (LoadCase loadCase in GetSelectedLoadCases())
            {
                foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                {
                    foreach (var isLiquefaction in SelectedLiquefactionCases)
                    {
                        int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                        if (lastStep < 0) continue;

                        var caseColor = GetCaseColor(caseIndex++);

                        List<double> topRelDisps = [0];
                        List<double> btmRelDisps = [0];
                        List<double> totalForcesTop = [0];
                        List<double> totalForcesBtm = [0];

                        for (int step = 0; step <= lastStep; step++)
                        {
                            // 全土圧合力ばねの水平反力合計
                            double sumFx = 0, sumFy = 0;
                            foreach (var spring in doatsuSprings)
                            {
                                var result = spring.HorizontalSpringResults?
                                    .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                      && r.LoadCombination?.No == loadCombination.No
                                                      && r.IsLiquefaction == isLiquefaction
                                                      && r.Step == step);
                                if (result?.CumulativeForce != null)
                                {
                                    sumFx += result.CumulativeForce.Fxi;
                                    sumFy += result.CumulativeForce.Fyi;
                                }
                            }
                            // 載荷方向への射影
                            double radA = loadCase.LoadAngle * Math.PI / 180.0;
                            double cosA = Math.Cos(radA);
                            double sinA = Math.Sin(radA);
                            double totalForce = sumFx * cosA + sumFy * sinA;
                            totalForcesTop.Add(totalForce);
                            totalForcesBtm.Add(totalForce);

                            // 最上点の相対変位
                            var topResult = topSpring.HorizontalSpringResults?
                                .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                  && r.LoadCombination?.No == loadCombination.No
                                                  && r.IsLiquefaction == isLiquefaction
                                                  && r.Step == step);
                            if (topResult?.CumulativeDisp != null)
                            {
                                double dx = topResult.CumulativeDisp.Dxi - topResult.CumulativeDisp.Dxj;
                                double dy = topResult.CumulativeDisp.Dyi - topResult.CumulativeDisp.Dyj;
                                topRelDisps.Add(Math.Sqrt(dx * dx + dy * dy) * 1000.0);
                            }
                            else topRelDisps.Add(0);

                            // 最下点の相対変位
                            var btmResult = btmSpring.HorizontalSpringResults?
                                .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                  && r.LoadCombination?.No == loadCombination.No
                                                  && r.IsLiquefaction == isLiquefaction
                                                  && r.Step == step);
                            if (btmResult?.CumulativeDisp != null)
                            {
                                double dx = btmResult.CumulativeDisp.Dxi - btmResult.CumulativeDisp.Dxj;
                                double dy = btmResult.CumulativeDisp.Dyi - btmResult.CumulativeDisp.Dyj;
                                btmRelDisps.Add(Math.Sqrt(dx * dx + dy * dy) * 1000.0);
                            }
                            else btmRelDisps.Add(0);
                        }

                        string legend = GetGeneralLegendText(loadCase, loadCombination, isLiquefaction);

                        // 最大相対変位を更新
                        double seriesMax = Math.Max(topRelDisps.Max(), btmRelDisps.Max());
                        if (seriesMax > maxDispMm) maxDispMm = seriesMax;

                        var scatterTop = wpfPlot.Plot.Add.Scatter(topRelDisps, totalForcesTop);
                        scatterTop.LegendText = $"最上点 {legend}";
                        scatterTop.Color = caseColor;
                        scatterTop.MarkerStyle.FillColor = caseColor;
                        scatterTop.MarkerStyle.LineColor = caseColor;

                        var scatterBtm = wpfPlot.Plot.Add.Scatter(btmRelDisps, totalForcesBtm);
                        scatterBtm.LegendText = $"最下点 {legend}";
                        scatterBtm.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                        scatterBtm.Color = caseColor;
                        scatterBtm.MarkerStyle.FillColor = caseColor;
                        scatterBtm.MarkerStyle.LineColor = caseColor;

                        // 等変形時の理論曲線（loadCase依存、最大変位×1.5まで描画）
                        var dgb = InputModel.ElementDivision?.DoatsuGoryokuBane;
                        if (dgb != null && dgb.Items.Count > 0 && dgb.DeltaP > 0 && seriesMax > 0)
                        {
                            double radT = loadCase.LoadAngle * Math.PI / 180.0;
                            double cosT = Math.Cos(radT);
                            double sinT = Math.Sin(radT);
                            double theorMaxM = seriesMax * 1.5 / 1000.0; // mm→m、1.5倍

                            var theorDisps = new List<double>();
                            var theorForces = new List<double>();
                            int nPoints = 100;
                            for (int i = 0; i <= nPoints; i++)
                            {
                                double d = theorMaxM * i / nPoints; // m単位
                                double dx = d * cosT;
                                double dy = d * sinT;
                                double dgbFx = 0, dgbFy = 0;
                                foreach (var item in dgb.Items)
                                {
                                    double dz = item.ZTop - item.ZBtm;
                                    dgbFx += item.GetPressure(dx) * dz * Math.Abs(item.Y1 - item.Y2);
                                    dgbFy += item.GetPressure(dy) * dz * Math.Abs(item.X1 - item.X2);
                                }
                                theorDisps.Add(d * 1000.0); // mm
                                theorForces.Add(dgbFx * cosT + dgbFy * sinT);
                            }
                            var scatterTheor = wpfPlot.Plot.Add.Scatter(theorDisps, theorForces);
                            scatterTheor.LegendText = $"等変形時（理論） {legend}";
                            scatterTheor.LineStyle.Pattern = ScottPlot.LinePattern.Dotted;
                            scatterTheor.MarkerSize = 0;
                            scatterTheor.Color = caseColor;
                        }
                    }
                }
            }

            ConfigurePlot(wpfPlot, crosshair, crosshairPositionText,
                "土圧合力ばね反力",
                "相対変位 (mm)", "土圧合力ばね反力合計 (kN)");

            // X軸の最大値を最終ステップの最大変位の1.5倍に制限
            if (maxDispMm > 0)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxDispMm * 1.5);
            }

            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // M-φ関係描画（任意の杭の任意の要素について軸力に応じたM-φ曲線と最終ステップマーカー）
        private void DrawMPhiCurves(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            var model = AnaModel;
            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();


            foreach (var pileLayout in targetPiles)
            {
                // 杭体取得
                if (pileLayout.PileBodyNo <= 0 || pileLayout.PileBodyNo > InputModel.PileBodies.Count)
                {
                    continue;
                }
                var pileBody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                if (pileBody.PileBodyRef != SelectedPileBodyRef)
                {
                    continue;
                }

                // 対応するBeam要素を見つける
                // SoilPileの要素分割（地層境界・0.5D分割）でBeam数 > 入力セグメント数のため、
                // SegmentIndexからSoilPileのセグメント番号で逆引きする
                SoilPile soilPile = null;
                {
                    int soilPileAltNo = pileLayout.SoilPileAltNo;
                    if (InputModel.ElementDivision?.SoilPiles != null
                        && soilPileAltNo - 1 >= 0
                        && soilPileAltNo - 1 < InputModel.ElementDivision.SoilPiles.Count)
                    {
                        soilPile = InputModel.ElementDivision.SoilPiles[soilPileAltNo - 1];
                    }
                }

                bool isAllSegments = SelectedPileSegmentNo <= 0;
                var matchedBeams = new List<Beam>();

                foreach (var beam in pileLayout.Beams)
                {
                    if (beam.SegmentIndex is not int seg) continue;

                    if (isAllSegments)
                    {
                        // All: すべてのbeamを追加
                        matchedBeams.Add(beam);
                        continue;
                    }

                    // SoilPileのPileBodySegments[seg].No は入力セグメント番号と一致（DeepCopy由来）
                    int inputSegNo = -1;
                    if (soilPile != null && seg >= 0 && seg < soilPile.PileBodySegments.Count)
                        inputSegNo = soilPile.PileBodySegments[seg].No;

                    if (inputSegNo == SelectedPileSegmentNo)
                    {
                        matchedBeams.Add(beam);
                    }
                }
                if (matchedBeams.Count == 0)
                {
                    continue;
                }

                foreach (var targetBeam in matchedBeams)
                {
                int segLabel = targetBeam.SegmentIndex.HasValue ? targetBeam.SegmentIndex.Value + 1 : 0;

                foreach (var loadCase in selectedLoadCases)
                {
                    foreach (var loadCombination in selectedCombinations)
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析未実行の (LoadCase, LoadCombination, Liquefaction) はこの組合せ全てスキップ
                            int lastStepForSet = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStepForSet < 0) continue;

                            // 軸力推定
                            double axialN = 0.0;
                            var prop = loadCase.GetType().GetProperty("NonlinearAxialForceN");
                            if (prop?.GetValue(loadCase) is double nlc && double.IsFinite(nlc) && nlc != 0.0)
                            {
                                axialN = nlc;
                            }
                            else
                            {
                                try
                                {
                                    double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0)
                                        axialN = nSeis;
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GraphVM] GetSeismicAxialForce: {ex.Message}"); }
                                if (axialN == 0.0 && double.IsFinite(pileLayout.AxialForce))
                                    axialN = pileLayout.AxialForce;
                            }

                            // M-φ曲線取得（解析で使用したものを優先）
                            List<double> phis = null;
                            List<double> moments = null;
                            string curveSource = "none";

                            // 解析結果からM-φ曲線を取得（解析で実際に使用したもの）
                            int lastStep = lastStepForSet;
                            BeamResult beamResultForCurve = null;
                            if (lastStep >= 0)
                            {
                                beamResultForCurve = targetBeam.GetBeamResult(model, loadCase, loadCombination, isLiquefaction, lastStep);

                                // 方法0: BeamResultに保存されたM-φ曲線を使用（最優先 - 解析で実際に使用したもの）
                                if (beamResultForCurve?.MPhiCurve_Phis != null && beamResultForCurve.MPhiCurve_Phis.Count >= 2)
                                {
                                    phis = beamResultForCurve.MPhiCurve_Phis;
                                    moments = beamResultForCurve.MPhiCurve_Moments;
                                    curveSource = "BeamResult";
                                }
                            }

                            // 方法1: 解析時に解決済みのキャッシュ曲線を使用（BeamResultに保存されていない場合）
                            if ((phis == null || phis.Count < 2) && targetBeam.ResolvedCombinedCurve?.Points != null)
                            {
                                var cachedCurve = targetBeam.ResolvedCombinedCurve;
                                if (cachedCurve.Points.Count >= 2)
                                {
                                    phis = [.. cachedCurve.Points.Select(p => p.Phi)];
                                    moments = [.. cachedCurve.Points.Select(p => p.Moment)];
                                    curveSource = "ResolvedCombinedCurve";
                                }
                            }

                            // 方法2: フォールバック - PileSectionから新規取得（解析結果がない場合のみ）
                            if ((phis == null || phis.Count < 2) && targetBeam.SegmentIndex is int fallbackSeg
                                && soilPile != null && fallbackSeg >= 0 && fallbackSeg < soilPile.PileBodySegments.Count)
                            {
                                var pileSegment = soilPile.PileBodySegments[fallbackSeg];
                                var pileSection = pileSegment.PileSection;
                                if (pileSection != null)
                                {
                                    try
                                    {
                                        var mPhi = pileSection.GetMPhiRelationship(axialN);
                                        var rawPhis = mPhi.Phis?.ToList();
                                        var rawMoments = mPhi.Moments?.ToList();
                                        if (rawPhis != null && rawMoments != null && rawPhis.Count >= 2)
                                        {
                                            phis = rawPhis;
                                            moments = rawMoments;
                                            curveSource = "PileSection(fallback)";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                    }
                                }
                            }

                            if (phis == null || moments == null || phis.Count < 2)
                            {
                                continue;
                            }

                            // 曲線プロット
                            bool isEIMode = SelectedGraphOption == "杭体EI-φ";
                            double[] plotXValues;
                            double[] plotYValues;
                            if (isEIMode)
                            {
                                // EI = M/φ は M-φ 区間内で双曲線状に変化する。
                                // ScatterLine は直線近似なので、分割を細かくして描画ポリラインを真の曲線に近づける。
                                // nDiv=100 で M-φ 折点付近の湾曲もほぼ折線で追従する（計算コスト微小）。
                                var eiPhis = new List<double>();
                                var eiValues = new List<double>();
                                for (int seg2 = 0; seg2 < phis.Count - 1; seg2++)
                                {
                                    double phi0 = phis[seg2], phi1 = phis[seg2 + 1];
                                    double m0 = moments[seg2], m1 = moments[seg2 + 1];
                                    const int nDiv = 100;
                                    int jStart = (seg2 == 0) ? 1 : 0; // φ=0はスキップ
                                    for (int j = jStart; j <= nDiv; j++)
                                    {
                                        double t = (double)j / nDiv;
                                        double phi = phi0 + t * (phi1 - phi0);
                                        double m = m0 + t * (m1 - m0);
                                        if (phi > 1e-15)
                                        {
                                            eiPhis.Add(phi);
                                            eiValues.Add(m / phi);
                                        }
                                    }
                                }
                                plotXValues = eiPhis.ToArray();
                                plotYValues = eiValues.ToArray();
                            }
                            else
                            {
                                plotXValues = phis.ToArray();
                                plotYValues = [.. moments];
                            }

                            string legend = $"LC:{loadCase.LoadName}|Comb:{loadCombination.No}|LIQ:{isLiquefaction}|N:{axialN:F0}|Pile:{pileLayout.No}|Seg:{segLabel}";
                            var scatter = wpfPlot.Plot.Add.Scatter(plotXValues, plotYValues);
                            scatter.LineStyle.Width = 2;
                            scatter.MarkerSize = isEIMode ? 0 : 5; // EIモードはマーカー不要
                            scatter.LegendText = legend;

                            // ホバー詳細: 杭/要素/入力杭区間/荷重条件/軸力/曲線出典
                            int inputSegForDetails = -1;
                            string sectionDesc = "";
                            if (targetBeam.SegmentIndex is int segForDetails
                                && soilPile != null && segForDetails >= 0 && segForDetails < soilPile.PileBodySegments.Count)
                            {
                                var pbSeg = soilPile.PileBodySegments[segForDetails];
                                inputSegForDetails = pbSeg.No;
                                sectionDesc = pbSeg.PileSection?.PileDescription ?? "";
                            }
                            string mphiDetails =
                                $"杭 No: {pileLayout.No} / 要素 Seg{segLabel}\n" +
                                $"入力杭区間 No: {(inputSegForDetails > 0 ? inputSegForDetails.ToString() : "—")}\n" +
                                $"杭断面: {(string.IsNullOrEmpty(sectionDesc) ? "—" : sectionDesc)}\n" +
                                $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                $"軸力 N: {axialN:F1} kN\n" +
                                $"曲線出典: {curveSource}";
                            _graphHoverMap[scatter] = mphiDetails;

                            // 最終ステップの曲率・モーメント取得（lastStepとbeamResultForCurveは上で取得済み）
                            if (lastStep >= 0 && beamResultForCurve != null)
                            {
                                // 曲率：解析で保存した値を使用
                                double phiFinal = beamResultForCurve.Curvature;
                                string phiSource = "Curvature";

                                // フォールバック：Curvatureが0以下の場合、回転角差から計算
                                if (phiFinal <= 0.0 && beamResultForCurve.CumulativeDisp != null)
                                {
                                    double length = targetBeam.Length;
                                    if (length > 0)
                                    {
                                        // 正しい曲率計算: 各成分の差から合成
                                        double dRyi = beamResultForCurve.CumulativeDisp.Ryj - beamResultForCurve.CumulativeDisp.Ryi;
                                        double dRzi = beamResultForCurve.CumulativeDisp.Rzj - beamResultForCurve.CumulativeDisp.Rzi;
                                        phiFinal = Math.Sqrt(dRyi * dRyi + dRzi * dRzi) / length;
                                        phiSource = "CumulativeDisp(fallback)";
                                    }
                                }

                                // モーメント：BeamResultに保存されたM-φ曲線から評価した値を使用
                                // 梁要素の剛性マトリクスから計算される端部モーメント(CumulativeForce)は
                                // M-φ曲線の断面モーメントとは異なるため、曲線から直接評価した値を使用
                                double mFinal = beamResultForCurve.Moment;
                                // フォールバック: Momentが0以下の場合、曲線から補間
                                if (mFinal <= 0.0)
                                {
                                    mFinal = InterpolateMomentFromCurve(phis, moments, phiFinal);
                                }
                                double mFem = beamResultForCurve.CumulativeForce?.MabsMax ?? 0;

                                // マーカープロット
                                double markerY = isEIMode && phiFinal > 1e-15 ? mFinal / phiFinal : mFinal;
                                if (double.IsFinite(phiFinal) && double.IsFinite(markerY) && markerY > 0)
                                {
                                    Scatter marker = wpfPlot.Plot.Add.Scatter([phiFinal], new[] { markerY });
                                    marker.LineStyle.Width = 0;
                                    marker.MarkerSize = 12;
                                    marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                    marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                    marker.LegendText = $"最終:{legend}";
                                    _graphHoverMap[marker] =
                                        mphiDetails + "\n" +
                                        $"最終 φ: {phiFinal:F6} rad/m\n" +
                                        $"最終 M: {mFinal:F1} kN·m" +
                                        (isEIMode ? $"\n最終 EI: {markerY:F0} kN·m²" : "");
                                }
                            }
                        }
                    }
                }
                } // foreach targetBeam in matchedBeams
            }

            bool isEIGraph = SelectedGraphOption == "杭体EI-φ";
            string plotTitle = isEIGraph ? "EI-φ関係" : "M-φ関係";
            string yLabel = isEIGraph ? "EI (kN·m²)" : "M (kN·m)";
            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, plotTitle, "φ (rad/m)", yLabel);
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // M-θ関係描画（任意の杭の杭頭について軸力に応じたM-θ曲線と最終ステップマーカー）
        /// <summary>
        /// FT-Pile / キャプテンパイル M-θ グラフ描画
        /// N値ごとの曲線群を描画し、最終ステップ位置にマーカーを配置
        /// </summary>
        private void DrawPileHeadTypeMTheta(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            wpfPlot.Plot.Clear();
            var model = AnaModel;
            if (model == null) { wpfPlot.Refresh(); return; }

            bool isFTPile = SelectedGraphOption == "FTPileM-θ";
            string targetType = isFTPile ? "FT-Pile構法" : "キャプテンパイル工法";

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();

            double maxThetaMarker = 0; // マーカーのθ最大値を追跡

            foreach (var pileLayout in targetPiles)
            {
                if (pileLayout.PileBodyNo <= 0 || pileLayout.PileBodyNo > InputModel.PileBodies.Count) continue;
                var pileBody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                if (pileBody.PileTopType?.Contains(targetType) != true) continue;

                var pileTop = pileBody.PileTop;
                if (pileTop == null) continue;

                // 各荷重ケースごとに、その杭の軸力に応じた1本のM-θ曲線を描画
                foreach (var loadCase in selectedLoadCases)
                {
                    // この杭・荷重ケースの軸力を取得（kN）
                    double axialN_kN;
                    try { axialN_kN = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level); }
                    catch { axialN_kN = pileLayout.AxialForce; }
                    double axialN_N = axialN_kN * 1000.0; // kN → N

                    // その軸力に対応するM-θ曲線を1本取得
                    ObservableCollection<double> thetas = null;
                    ObservableCollection<double> ms = null;

                    if (isFTPile && pileTop.FTPile != null)
                    {
                        var result = pileTop.FTPile.GetMThetaRelationship(axialN_N);
                        thetas = result.Item1;
                        ms = result.Item2;
                    }
                    else if (!isFTPile && pileTop.CaptainPile != null)
                    {
                        var result = pileTop.CaptainPile.GetMThetaRelationship(axialN_N);
                        thetas = result.Item1;
                        ms = result.Item2;
                    }

                    if (thetas == null || ms == null || thetas.Count < 2) continue;

                    // 曲線描画（N·mm → kN·m: 1 kN·m = 1e6 N·mm、マーカーなしライン）
                    var scatter = wpfPlot.Plot.Add.Scatter(
                        thetas.Select(t => (double)t).ToArray(),
                        ms.Select(m => m / 1e6).ToArray());
                    scatter.MarkerSize = 0;
                    scatter.LegendText = $"杭{pileLayout.No} {loadCase.LoadName} N={axialN_kN:N0}kN";

                    // 最終ステップマーカー
                    foreach (var loadCombination in selectedCombinations)
                    {
                        foreach (var isLiq in SelectedLiquefactionCases)
                        {
                            int lastStep = model.GetAnalysisLastStep(loadCase, loadCombination, isLiq);
                            if (lastStep < 0) continue;

                            var rs = model.RotationalSprings?.FirstOrDefault(r =>
                                r.Name == $"RθXY-{pileLayout.No}");
                            if (rs == null) continue;

                            var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                                r.LoadCase?.No == loadCase.No &&
                                r.LoadCombination?.No == loadCombination.No &&
                                r.IsLiquefaction == isLiq &&
                                r.Step == lastStep);
                            if (rsResult?.CumulativeDisp == null || rsResult.CumulativeForce == null) continue;

                            double dRx = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                            double dRy = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                            double thetaFinal = Math.Sqrt(dRx * dRx + dRy * dRy);
                            double mFinal = Math.Sqrt(
                                rsResult.CumulativeForce.Mxi * rsResult.CumulativeForce.Mxi +
                                rsResult.CumulativeForce.Myi * rsResult.CumulativeForce.Myi);

                            if (double.IsFinite(thetaFinal) && double.IsFinite(mFinal) && thetaFinal > 0)
                            {
                                var marker = wpfPlot.Plot.Add.Scatter(new[] { thetaFinal }, new[] { mFinal });
                                marker.LineStyle.Width = 0;
                                marker.MarkerSize = 12;
                                marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                marker.LegendText = $"解析結果 杭{pileLayout.No} {loadCase.LoadName}|{(isLiq ? "LIQ" : "非LIQ")}";
                                if (thetaFinal > maxThetaMarker) maxThetaMarker = thetaFinal;
                            }
                        }
                    }
                }
            }

            string title = isFTPile ? "FT-Pile M-θ関係" : "キャプテンパイル M-θ関係";
            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, title, "θ (rad)", "M (kN·m)", decimalPlacesX: 3);

            // X軸の表示範囲: 0 ～ マーカー最大θの1.5倍
            if (maxThetaMarker > 1e-10)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxThetaMarker * 1.5);
            }

            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        private void DrawMThetaCurvesWithMarker(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            var model = AnaModel;
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0)
            {
                wpfPlot.Plot.Clear();
                _graphHoverMap.Clear();
                wpfPlot.Refresh();
                return;
            }

            var targetPiles = GetSelectedPileLayouts();
            var targetPileNos = new HashSet<int>(targetPiles.Select(p => p.No));
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();

            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            foreach (var loadCase in selectedLoadCases)
            {
                foreach (var loadCombination in selectedCombinations)
                {
                    foreach (var isLiquefaction in SelectedLiquefactionCases)
                    {
                        // 解析未実行の (LoadCase, LoadCombination, Liquefaction) はこの組合せ全てスキップ
                        int lastStepForSet = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                        if (lastStepForSet < 0) continue;

                        foreach (var rs in model.RotationalSprings)
                        {
                            // 対応杭レイアウト探索
                            // バネ名形式: "RθXY-{pileNo}" から杭番号を抽出
                            PileLayoutDataItem pileLayout = null;
                            if (rs.Name != null && rs.Name.Contains('-'))
                            {
                                var parts = rs.Name.Split('-');
                                if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                                {
                                    pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.No == pileNo);
                                }
                            }
                            // フォールバック: NodeJから探索
                            if (pileLayout == null && rs.NodeJ != null)
                            {
                                pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileNodes.Count > 0 && ReferenceEquals(pl.PileNodes[0], rs.NodeJ));
                            }
                            // フォールバック: PileBodyNoから探索（最初の杭のみ）
                            if (pileLayout == null && rs.PileBodyNo is int pb && pb > 0 && pb <= InputModel.PileBodies.Count)
                            {
                                pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileBodyNo == pb);
                            }

                            if (pileLayout == null) continue;
                            if (SelectedPileOption != "All" && !targetPileNos.Contains(pileLayout.No)) continue;

                            // 軸力推定
                            double axialN = 0.0;
                            var prop = loadCase.GetType().GetProperty("NonlinearAxialForceN");
                            if (prop?.GetValue(loadCase) is double nlc && double.IsFinite(nlc) && nlc != 0.0)
                            {
                                axialN = nlc;
                            }
                            else
                            {
                                try
                                {
                                    double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0)
                                        axialN = nSeis;
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GraphVM] GetSeismicAxialForce: {ex.Message}"); }
                                if (axialN == 0.0 && double.IsFinite(pileLayout.AxialForce))
                                    axialN = pileLayout.AxialForce;
                            }

                            // 曲線取得
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
                                double? k = rs.Mode == RotationalSpringMode.CombinedXY ? rs.KthetaXY : rs.Ktheta;
                                if (!k.HasValue || k.Value <= 0.0) continue;
                                const double thetaMax = 0.02;
                                int nDiv = 50;
                                thetas = [.. Enumerable.Range(0, nDiv).Select(i => i * thetaMax / (nDiv - 1))];
                                moments = [.. thetas.Select(t => k.Value * t)];
                                modeTag = rs.Mode == RotationalSpringMode.CombinedXY ? "XY" : rs.Dof.ToString();
                            }
                            if (thetas.Length == 0 || moments.Length == 0) continue;

                            // 曲線プロット
                            string legend = $"LC:{loadCase.LoadName}|Comb:{loadCombination.No}|LIQ:{isLiquefaction}|N:{axialN:F0}|Pile:{pileLayout.No}|Mode:{modeTag}";
                            var scatter = wpfPlot.Plot.Add.Scatter(thetas, moments);
                            scatter.LegendText = legend;

                            // 杭頭詳細: 杭No、断面、回転ばね構成、軸力、荷重条件
                            double pileHeadZ = pileLayout.PileNodes != null && pileLayout.PileNodes.Count > 0
                                ? pileLayout.PileNodes[0].Coord.Z : double.NaN;
                            string headSectionDesc = "";
                            if (pileLayout.PileBodyNo > 0 && pileLayout.PileBodyNo <= InputModel.PileBodies.Count)
                            {
                                var pbody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                                if (pbody?.PileBodySegments != null && pbody.PileBodySegments.Count > 0)
                                    headSectionDesc = pbody.PileBodySegments[0].PileSection?.PileDescription ?? "";
                            }
                            double kUsed = rs.Mode == RotationalSpringMode.CombinedXY ? (rs.KthetaXY ?? 0.0) : (rs.Ktheta ?? 0.0);
                            string mthetaDetails =
                                $"杭 No: {pileLayout.No}  (X={pileLayout.X:F3}, Y={pileLayout.Y:F3})\n" +
                                $"杭頭 Z: {(double.IsFinite(pileHeadZ) ? pileHeadZ.ToString("F3") + " m" : "—")}\n" +
                                $"杭頭断面: {(string.IsNullOrEmpty(headSectionDesc) ? "—" : headSectionDesc)}\n" +
                                $"回転ばね: {rs.Name} / Mode: {modeTag} / Kθ: {kUsed:0.###E+0}\n" +
                                $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                $"軸力 N: {axialN:F1} kN";
                            _graphHoverMap[scatter] = mthetaDetails;

                            // 最終ステップの回転角・モーメント取得
                            int lastStep = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep >= 0)
                            {
                                // RotationalSpringResultから該当する結果を取得（Beam.GetBeamResultと同様のパターン）
                                var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                                    r.LoadCase?.No == loadCase.No &&
                                    r.LoadCombination?.No == loadCombination.No &&
                                    r.IsLiquefaction == isLiquefaction &&
                                    r.Step == lastStep);

                                if (rsResult?.CumulativeDisp != null && rsResult.CumulativeForce != null)
                                {
                                    // 回転角（回転ばねの相対回転量から直接取得）
                                    // CumulativeDispはNodeI,NodeJの変位を格納（Rxi=NodeI.Rx, Rxj=NodeJ.Rx）
                                    double dRx = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                                    double dRy = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                                    double dRz = rsResult.CumulativeDisp.Rzj - rsResult.CumulativeDisp.Rzi;
                                    double mxi = rsResult.CumulativeForce.Mxi;
                                    double myi = rsResult.CumulativeForce.Myi;
                                    double mzi = rsResult.CumulativeForce.Mzi;

                                    double thetaFinal;
                                    double mFinal;
                                    bool isPeakPlot = false;
                                    if (rs.Mode == RotationalSpringMode.CombinedXY)
                                    {
                                        // v28 アプローチ I: post-crack で方向ロック + ヒステリシスされた杭は
                                        // **ピーク履歴値 (ThetaProjMax, curve(ThetaProjMax))** をプロット。
                                        // 現在値 (θ_proj, M_proj) は線形除荷経路上の点で、monotonic loading
                                        // curve 上には乗らない。設計的にはピーク時の最大 demand を包絡線で
                                        // 示す方が意味のある可視化。
                                        if (rsResult.HasCracked
                                            && rsResult.CrackNx.HasValue
                                            && rsResult.CrackNy.HasValue
                                            && rsResult.ThetaProjMax > 0.0
                                            && rs.CurveXY != null)
                                        {
                                            thetaFinal = rsResult.ThetaProjMax;
                                            mFinal = Math.Abs(rs.CurveXY.EvaluateMoment(thetaFinal));
                                            isPeakPlot = true;
                                        }
                                        else
                                        {
                                            thetaFinal = Math.Sqrt(dRx * dRx + dRy * dRy);
                                            mFinal = Math.Sqrt(mxi * mxi + myi * myi);
                                        }
                                    }
                                    else
                                    {
                                        thetaFinal = rs.Dof == RotationalDof.Rx ? Math.Abs(dRx)
                                            : rs.Dof == RotationalDof.Ry ? Math.Abs(dRy)
                                            : Math.Abs(dRz);
                                        mFinal = rs.Dof == RotationalDof.Rx ? Math.Abs(mxi)
                                            : rs.Dof == RotationalDof.Ry ? Math.Abs(myi)
                                            : Math.Abs(mzi);
                                    }

                                    // マーカープロット
                                    if (double.IsFinite(thetaFinal) && double.IsFinite(mFinal) && thetaFinal > 0)
                                    {
                                        var marker = wpfPlot.Plot.Add.Scatter([thetaFinal], new[] { mFinal });
                                        marker.LineStyle.Width = 0;
                                        marker.MarkerSize = 12;
                                        marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                        marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                        marker.LegendText = $"最終:{legend}";

                                        if (isPeakPlot)
                                        {
                                            // ピーク表示: 現在値 (θ_proj, M_proj) もホバーに併記
                                            double dRxH = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                                            double dRyH = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                                            double thetaProjNow = dRxH * rsResult.CrackNx.Value + dRyH * rsResult.CrackNy.Value;
                                            double mProjNow = mxi * rsResult.CrackNx.Value + myi * rsResult.CrackNy.Value;
                                            _graphHoverMap[marker] =
                                                mthetaDetails + "\n" +
                                                $"ピーク θ_proj_max (n=({rsResult.CrackNx:F3},{rsResult.CrackNy:F3})): {thetaFinal:F6} rad\n" +
                                                $"ピーク M: {mFinal:F1} kN·m\n" +
                                                $"現在 θ_proj: {thetaProjNow:F6} rad / M_proj: {mProjNow:F1} kN·m\n" +
                                                "(post-crack 方向ロック: n 方向ピーク履歴値を表示)";
                                        }
                                        else
                                        {
                                            _graphHoverMap[marker] =
                                                mthetaDetails + "\n" +
                                                $"最終 θ: {thetaFinal:F6} rad\n" +
                                                $"最終 M: {mFinal:F1} kN·m";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, "M-θ関係", "θ (rad)", "M (kN·m)", decimalPlacesX: 3);
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 水平地盤反力度p-y関係描画（理論P-y曲線 + 最終ステップマーカー）
        private void DrawPyCurvesWithMarker(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();
            bool isAllSegments = SelectedPileSegmentOption == "All";
            int singleSegIdx = SelectedPileSegmentNo - 1; // 0-based（All以外）

            double maxMarkerDisp = 0;

            foreach (var pileLayout in targetPiles)
            {
                int altNo = pileLayout.SoilPileAltNo;
                if (altNo <= 0 || altNo > InputModel.ElementDivision.SoilPiles.Count) continue;
                var soilPile = InputModel.ElementDivision.SoilPiles[altNo - 1];
                var reactions = soilPile.HorizontalSoilReactions;
                if (reactions == null || reactions.Count == 0) continue;

                // All: 全区間、それ以外: 選択区間のみ
                var segIndices = isAllSegments
                    ? Enumerable.Range(0, reactions.Count).ToList()
                    : (singleSegIdx >= 0 && singleSegIdx < reactions.Count ? new List<int> { singleSegIdx } : new List<int>());

                bool isFront = pileLayout.IsFrontPiles?.FirstOrDefault() ?? true;

                foreach (int segIdx in segIndices)
                {
                var reaction = reactions[segIdx];

                // 理論P-y曲線（Top/Btm）を描画
                double pyTop = isFront ? reaction.PyFrontTop : reaction.PyRearTop;
                double pyBtm = isFront ? reaction.PyFrontBtm : reaction.PyRearBtm;

                // P-y曲線のサンプリング点（小変位域を細かく、大変位域は粗く）
                var yValues = new List<double>();
                for (double y = 0.0; y < 0.01; y += 0.0001) yValues.Add(y);   // 0-10mm: 0.1mm刻み
                for (double y = 0.01; y < 0.05; y += 0.001) yValues.Add(y);   // 10-50mm: 1mm刻み
                for (double y = 0.05; y < 0.50; y += 0.005) yValues.Add(y);   // 50-500mm: 5mm刻み

                // ホバー詳細文字列（共通部分）
                string pyDetails =
                    $"杭 No: {pileLayout.No} / 要素 #{segIdx + 1}\n" +
                    $"地盤層: {reaction.Name}\n" +
                    $"土質: {reaction.SoilType}\n" +
                    $"標高: {reaction.ZTop:F3} ~ {reaction.ZBtm:F3} m\n" +
                    $"杭径 B: {reaction.B * 1000.0:F0} mm\n" +
                    $"N 値: {reaction.NValue:F1}";

                // Top曲線
                var xsT = yValues.Select(y => y * 1000.0).ToArray();
                var ysT = yValues.Select(y => reaction.GetP(y, pyTop)).ToArray();
                var curveT = wpfPlot.Plot.Add.ScatterLine(xsT, ysT);
                curveT.LegendText = $"P{pileLayout.No}|Seg{segIdx + 1}|Top";
                _graphHoverMap[curveT] = pyDetails;

                // Btm曲線
                var xsB = xsT; // 同じX値
                var ysB = yValues.Select(y => reaction.GetP(y, pyBtm)).ToArray();
                var curveB = wpfPlot.Plot.Add.ScatterLine(xsB, ysB);
                curveB.LegendText = $"P{pileLayout.No}|Seg{segIdx + 1}|Btm";
                curveB.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                _graphHoverMap[curveB] = pyDetails;

                // 最終ステップのマーカーを描画（i端・j端）
                // X軸: 解析結果の相対変位、Y軸: 理論P-y曲線上の値（必ず曲線上に乗る）
                var springs = pileLayout.HorizontalSoilSprings;
                if (springs == null) continue;

                // i端 = node segIdx, j端 = node segIdx+1
                var endNodes = new List<(int nodeIdx, string label, double py, ScottPlot.MarkerShape shape)>();
                if (segIdx < springs.Count)
                    endNodes.Add((segIdx, "i端", pyTop, ScottPlot.MarkerShape.FilledCircle));
                if (segIdx + 1 < springs.Count)
                    endNodes.Add((segIdx + 1, "j端", pyBtm, ScottPlot.MarkerShape.FilledSquare));

                foreach (var (nodeIdx, endLabel, py, shape) in endNodes)
                {
                    var spring = springs[nodeIdx];

                    foreach (var loadCase in selectedLoadCases)
                    {
                        foreach (var loadCombination in selectedCombinations)
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                if (lastStep < 0) continue;

                                var result = spring.HorizontalSpringResults?
                                    .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                             && r.LoadCombination?.No == loadCombination.No
                                             && r.IsLiquefaction == isLiquefaction)
                                    .OrderByDescending(r => r.Step)
                                    .FirstOrDefault();

                                if (result?.CumulativeDisp == null) continue;

                                double relDispX = result.CumulativeDisp.Dxi - result.CumulativeDisp.Dxj;
                                double relDispY = result.CumulativeDisp.Dyi - result.CumulativeDisp.Dyj;
                                double relDisp = Math.Sqrt(relDispX * relDispX + relDispY * relDispY);
                                double relDispMm = relDisp * 1000.0;

                                // Y軸は理論値（P-y曲線上の値）
                                double pTheory = reaction.GetP(relDisp, py);

                                string legend = $"LC:{loadCase.LoadName}|LIQ:{isLiquefaction}|P{pileLayout.No}|{endLabel}";

                                if (double.IsFinite(relDispMm) && relDispMm > 0 && double.IsFinite(pTheory))
                                {
                                    maxMarkerDisp = Math.Max(maxMarkerDisp, relDispMm);

                                    var marker = wpfPlot.Plot.Add.Scatter(new[] { relDispMm }, new[] { pTheory });
                                    marker.LineStyle.Width = 0;
                                    marker.MarkerSize = 12;
                                    marker.MarkerStyle.Shape = shape;
                                    marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                    marker.LegendText = $"最終:{legend}";
                                    _graphHoverMap[marker] =
                                        pyDetails + "\n" +
                                        $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                        $"{endLabel}: 相対変位 {relDispMm:F2} mm, p = {pTheory:F1} kN/m²";
                                }
                            }
                        }
                    }
                }
                } // foreach segIdx
            }

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, "水平地盤反力度p-y関係", "相対変位 (mm)", "反力度 p (kN/m²)");
            // X軸の定義域をマーカー最大値 × 1.5 に設定
            if (maxMarkerDisp > 0)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxMarkerDisp * 1.5);
            }
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
                if (SelectedGraphOption == "沈下 単杭" ||
                    SelectedGraphOption == "沈下 単杭+群杭")
                {
                    if (SelectedLoadCaseOption == "VL")
                    {
                        if (SelectedGraphOption == "沈下 単杭")
                        {
                            ys.Add(pile.SinglePileSettlementVL * 1000); // m → mm
                        }
                        else // if (SelectedGraphOption == "沈下 単杭+群杭")
                        {
                            ys.Add(pile.SinglePileSettlementVL * 1000 + pile.GroupPileSettlement); // m→mm + mm
                        }

                    }
                    else // VL以外
                    {
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel1[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "沈下 単杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i] * 1000); // m → mm
                                    break;
                                }
                                else // if (SelectedGraphOption == "沈下 単杭+群杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i] * 1000 + pile.GroupPileSettlement); // m→mm + mm
                                    break;
                                }
                            }
                        }
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel2[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "沈下 単杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i] * 1000); // m → mm
                                    break;
                                }
                                else // if (SelectedGraphOption == "沈下 単杭+群杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i] * 1000 + pile.GroupPileSettlement); // m→mm + mm
                                    break;
                                }
                            }
                            return;
                        }
                    }
                }
                else if (SelectedGraphOption == "沈下 群杭")
                {
                    ys.Add(pile.GroupPileSettlement);
                }
                else if (SelectedGraphOption == "沈下 基礎梁考慮単杭" ||
                         SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
                {
                    double vbSettle = GetVBSettlement(pile.No, SelectedLoadCaseOption);
                    if (SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
                        vbSettle += pile.GroupPileSettlement;
                    ys.Add(vbSettle);
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
                scatterAngle.LegendText = GetSettlementAngleLegendText(angle);
                scatterAngle.MarkerSize = 0;
                scatterAngle.LineWidth = 0;
            }

            if (SelectedGraphOption == "沈下 群杭" || SelectedGraphOption == "沈下 単杭+群杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
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
                    scatterAngleGround.LegendText = GetSettlementAngleLegendText(angle);
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

            // 限界状態表示が有効かどうか
            bool showLimitState = SelectedLimitState != "なし" &&
                (forceType == "M" || forceType == "My" || forceType == "Mz" || forceType == "F" || forceType == "Fy" || forceType == "Fz");

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var beams = pileLayoutDataItem.Beams;
                var soilPile = pileLayoutDataItem.SoilPile;

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

                            // 杭頭
                            List<double> beamZs = [beams[0]?.NodeI?.Coord.Z ?? 0];
                            List<double> beamForces = [0];

                            foreach (var beam in beams)
                            {
                                if (beam?.NodeI?.Coord == null || beam?.NodeJ?.Coord == null) continue;
                                // SegmentIndex未設定の梁（RigidLink等）はスキップ
                                if (beam.SegmentIndex is null) continue;

                                var result = beam.GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result?.CumulativeForce == null) continue;

                                beamZs.Add(beam.NodeI.Coord.Z);
                                beamZs.Add(beam.NodeJ.Coord.Z);

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

                            // 杭先端: RigidLinkではなく、最後の杭要素のNodeJを使用
                            var lastPileBeam = beams.LastOrDefault(b => b.SegmentIndex != null);
                            if (lastPileBeam != null)
                            {
                                beamZs.Add(lastPileBeam.NodeJ.Coord.Z);
                            }
                            else
                            {
                                beamZs.Add(beams[^1].NodeJ.Coord.Z);
                            }
                            beamForces.Add(0);

                            var scatter = wpfPlot.Plot.Add.Scatter(beamForces.ToArray(), beamZs.ToArray());
                            scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                            var stressColor = scatter.LineStyle.Color; // 応力ラインの色を取得

                            // ホバーポップアップ用詳細
                            double absMax = beamForces.Count > 0 ? beamForces.Max(Math.Abs) : 0;
                            _graphHoverMap[scatter] =
                                $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} (α={loadCombination.Alpha1:F2}/β₁={loadCombination.Beta1:F2}/β₂={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                + $"系列: {forceType} ({unit})\n"
                                + $"最大絶対値: {absMax:N2} {unit}\n"
                                + $"節点数: {beamZs.Count}";

                            // 限界状態の破線ステップライン描画（同じ色で描画）
                            if (showLimitState && soilPile?.PileBodySegments != null)
                            {
                                // 限界値のステップラインを構築
                                List<double> limitZs = [];
                                List<double> limitValues = [];

                                foreach (var beam in beams)
                                {
                                    if (beam?.NodeI?.Coord == null || beam?.NodeJ?.Coord == null) continue;

                                    var result = beam.GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                    if (result?.CumulativeForce == null) continue;

                                    // 軸力を取得（要素中央の平均軸力、X方向が軸方向）
                                    double axialForceN = (result.CumulativeForce.Fxi + result.CumulativeForce.Fxj) / 2.0;

                                    // 杭断面を取得（SoilPile.PileBodySegmentsを使用）
                                    int segmentIndex = beam.SegmentIndex ?? 0;
                                    if (segmentIndex < 0 || segmentIndex >= soilPile.PileBodySegments.Count) continue;

                                    var pileSection = soilPile.PileBodySegments[segmentIndex].PileSection;
                                    if (pileSection == null) continue;

                                    // 限界値を取得
                                    double limitValue;
                                    if (forceType == "M" || forceType == "My" || forceType == "Mz")
                                    {
                                        var (nValues, mValues) = GetLimitStateNMCurve(pileSection, SelectedLimitState);
                                        if (nValues == null || mValues == null || nValues.Count == 0) continue;
                                        limitValue = InterpolateLimitValue(nValues, mValues, axialForceN);
                                    }
                                    else // F, Fy, Fz
                                    {
                                        var (nValues, qValues) = GetLimitStateNQCurve(pileSection, SelectedLimitState);
                                        if (nValues == null || qValues == null || nValues.Count == 0) continue;
                                        limitValue = InterpolateLimitValue(nValues, qValues, axialForceN);
                                    }

                                    if (double.IsNaN(limitValue) || limitValue <= 0) continue;

                                    // ステップライン用のデータ（各要素で一定値、正側のみ）
                                    limitZs.Add(beam.NodeI.Coord.Z);
                                    limitZs.Add(beam.NodeJ.Coord.Z);
                                    limitValues.Add(limitValue);
                                    limitValues.Add(limitValue);
                                }

                                // 限界値ライン（正側のみ、同じ色で破線）
                                if (limitZs.Count > 0)
                                {
                                    var scatterLimit = wpfPlot.Plot.Add.Scatter(limitValues.ToArray(), limitZs.ToArray());
                                    scatterLimit.LineStyle.Pattern = LinePattern.Dashed;
                                    scatterLimit.LineStyle.Width = 1.5f;
                                    scatterLimit.LineStyle.Color = stressColor; // 応力と同じ色
                                    scatterLimit.MarkerStyle.IsVisible = false;
                                    scatterLimit.LegendText = SelectedLimitState;
                                }
                            }
                        }
                    }
                }
            }

            // せん断力の場合はFをQに置換（Q = Shear Force）
            string axisLabel = forceType.Replace("F", "Q");
            string axisX = axisLabel + " " + unit;

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

            var selectedPiles = GetSelectedPileLayouts();
            if (AnaModel == null) return;

            foreach (PileLayoutDataItem pileLayoutDataItem in selectedPiles)
            {
                var beams = pileLayoutDataItem.Beams;
                var pileNodes = pileLayoutDataItem.PileNodes;
                var soilNodes = pileLayoutDataItem.SoilNodes;
                if (pileNodes == null || pileNodes.Count == 0)
                {
                    continue;
                }

                var loadCases = GetSelectedLoadCases();

                foreach (LoadCase loadCase in loadCases)
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

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
                                    pileDisps.Add(0);
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
                                if (result?.CumulativeDisp == null)
                                {
                                    soilDisps.Add(0.0);
                                    continue;
                                }
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

                            var scatterPile = wpfPlot.Plot.Add.Scatter(pileDisps.ToArray(), pileZs.ToArray());
                            var pileColor = scatterPile.LineStyle.Color; // 杭変位の色を取得

                            var scatterSoil = wpfPlot.Plot.Add.Scatter(soilDisps.ToArray(), soilZs.ToArray());
                            scatterSoil.LineStyle.Pattern = LinePattern.Dashed;
                            scatterSoil.LineStyle.Color = pileColor; // 杭変位と同じ色を適用
                            scatterSoil.MarkerStyle.FillColor = pileColor;

                            scatterPile.LegendText = "(PILE), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                            scatterSoil.LegendText = "(SOIL), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);

                            // ホバーポップアップ用詳細
                            string hoverHeader = $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} (α={loadCombination.Alpha1:F2}/β₁={loadCombination.Beta1:F2}/β₂={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}";
                            double pileMax = pileDisps.Count > 0 ? pileDisps.Max(Math.Abs) : 0;
                            double soilMax = soilDisps.Count > 0 ? soilDisps.Max(Math.Abs) : 0;
                            _graphHoverMap[scatterPile] = hoverHeader
                                + $"\n系列: 杭変位 {dispType} ({unit})"
                                + $"\n最大絶対値: {pileMax:N2} {unit}"
                                + $"\n節点数: {pileZs.Count}";
                            _graphHoverMap[scatterSoil] = hoverHeader
                                + $"\n系列: 地盤変位 {dispType} ({unit})"
                                + $"\n最大絶対値: {soilMax:N2} {unit}"
                                + $"\n節点数: {soilZs.Count}";
                        }
                    }
                }
            }

            string axisX = dispType + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, SelectedGraphOption, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 水平地盤反力描画（相対変位、地盤反力、ばね割線剛性）
        private void DrawHorizontalSoilReaction(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string dataType, string unit)
        {
            IsPileOptionVisible = true;
            wpfPlot.Plot.Clear();

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Refresh();
                return;
            }

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var horizontalSoilSprings = pileLayoutDataItem.HorizontalSoilSprings;
                if (horizontalSoilSprings == null || horizontalSoilSprings.Count == 0) continue;

                // 案 B: ノードごとのトリビュータリ長を求めるため、対応する HorizontalSoilReactions
                // (地盤セグメント定義) を取得。SoilPileAltNo が無効な場合は reactions を null にして
                // フォールバック (division スキップ、= 旧挙動の kN/kN/m を表示)
                List<Models.InputData.HorizontalSoilReactionItem>? reactions = null;
                if (pileLayoutDataItem.SoilPileAltNo > 0
                    && pileLayoutDataItem.SoilPileAltNo <= InputModel.ElementDivision.SoilPiles.Count)
                {
                    var sp = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    if (sp?.HorizontalSoilReactions != null && sp.HorizontalSoilReactions.Count > 0)
                        reactions = sp.HorizontalSoilReactions.ToList();
                }

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

                            List<double> springZs = [];
                            List<double> springValues = [];

                            int nSprings = horizontalSoilSprings.Count;

                            // 案 C (v3): 分布モード + 反力/反力係数 は セグメント単位で上下半分に分けて長方形分布を作る。
                            //   - 上半分 [Z_top, Z_mid] は ノード j の相対変位 y_j と セグメント j の top 側 py を使う
                            //   - 下半分 [Z_mid, Z_btm] は ノード j+1 の相対変位 y_{j+1} と セグメント j の bottom 側 py を使う
                            //   計算は HorizontalSoilReactionItem.GetP(y, py) を利用 (設計モデルと厳密一致)
                            bool useRectDist = IsDistributedMode && reactions != null
                                && (dataType == "Reaction" || dataType == "SecantStiffness");

                            if (useRectDist)
                            {
                                // 全節点の 相対変位 と FEM 実測ばね反力 をキャッシュ
                                double[] nodeRelDisps = new double[nSprings];
                                double[] nodeActualForces = new double[nSprings]; // |F|_FEM [kN]
                                for (int k = 0; k < nSprings; k++)
                                {
                                    var sp = horizontalSoilSprings[k];
                                    if (sp == null) continue;
                                    var res = sp.HorizontalSpringResults?
                                        .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                 && r.LoadCombination?.No == loadCombination.No
                                                 && r.IsLiquefaction == isLiquefaction)
                                        .OrderByDescending(r => r.Step)
                                        .FirstOrDefault();
                                    if (res?.CumulativeDisp == null) continue;
                                    double dx = res.CumulativeDisp.Dxi - res.CumulativeDisp.Dxj;
                                    double dy = res.CumulativeDisp.Dyi - res.CumulativeDisp.Dyj;
                                    nodeRelDisps[k] = Math.Sqrt(dx * dx + dy * dy);
                                    if (res.CumulativeForce != null)
                                    {
                                        double fx = res.CumulativeForce.Fxi;
                                        double fy = res.CumulativeForce.Fyi;
                                        nodeActualForces[k] = Math.Sqrt(fx * fx + fy * fy);
                                    }
                                }

                                // isFront: 当該荷重ケースでのこの杭の前後判定 (p-y 計算に影響)
                                int iLC = loadCase.No - 1;
                                bool isFront = pileLayoutDataItem.IsFrontPiles != null
                                            && iLC >= 0
                                            && iLC < pileLayoutDataItem.IsFrontPiles.Count
                                            && pileLayoutDataItem.IsFrontPiles[iLC];

                                // 各節点 k の理論 上/下 寄与 (FEM と同じモデルで再計算) と、FEM 実測値に合わせた
                                // 比例スケール factor を計算
                                //   F_above_k: 節点 k の上方セグメント (k-1) の下半分寄与 (isTop=false)
                                //   F_below_k: 節点 k の下方セグメント k の上半分寄与   (isTop=true)
                                //   F_above_k + F_below_k = F_node_theory → scale_k = F_actual / F_theory
                                double[] fAboveScaled = new double[nSprings];
                                double[] fBelowScaled = new double[nSprings];
                                for (int k = 0; k < nSprings; k++)
                                {
                                    double y = nodeRelDisps[k];
                                    double fAboveTh = 0, fBelowTh = 0;
                                    if (k > 0 && (k - 1) < reactions.Count)
                                        fAboveTh = Math.Abs(reactions[k - 1].GetSoilReaction(y, isTop: false, isFront));
                                    if (k < reactions.Count)
                                        fBelowTh = Math.Abs(reactions[k].GetSoilReaction(y, isTop: true, isFront));

                                    double sum = fAboveTh + fBelowTh;
                                    double scale = sum > 1e-10 ? nodeActualForces[k] / sum : 1.0;
                                    fAboveScaled[k] = fAboveTh * scale;
                                    fBelowScaled[k] = fBelowTh * scale;
                                }

                                // セグメントごとに 4 隅の点を追加して長方形を作る
                                //   セグメント j の 上半分 [Z_top, Z_mid] = 節点 j の F_below (scaled)
                                //   セグメント j の 下半分 [Z_mid, Z_btm] = 節点 j+1 の F_above (scaled)
                                // 各半分の half-tributary area = L/2 × B でスケールして圧力/反力係数に
                                for (int j = 0; j < reactions.Count; j++)
                                {
                                    double zTop = reactions[j].ZTop;
                                    double zBtm = reactions[j].ZBtm;
                                    double zMid = 0.5 * (zTop + zBtm);
                                    double L = zTop - zBtm;
                                    if (L <= 0) continue;
                                    double B = reactions[j].B > 0 ? reactions[j].B : 1.0;
                                    double halfArea = 0.5 * L * B; // kN → kN/m² 変換用

                                    // 上半分: 節点 j の 下方寄与 (F_below_j) がこのセグメントの上半分に対応
                                    double fUpper = (j < nSprings) ? fBelowScaled[j] : 0;
                                    // 下半分: 節点 j+1 の 上方寄与 (F_above_{j+1}) がこのセグメントの下半分に対応
                                    double fLower = ((j + 1) < nSprings) ? fAboveScaled[j + 1] : 0;

                                    double pUpperPa = halfArea > 0 ? fUpper / halfArea : 0; // kN/m²
                                    double pLowerPa = halfArea > 0 ? fLower / halfArea : 0;

                                    double yUp = (j < nSprings) ? nodeRelDisps[j] : 0;
                                    double yLo = ((j + 1) < nSprings) ? nodeRelDisps[j + 1] : 0;

                                    double vUp, vLo;
                                    if (dataType == "Reaction")
                                    {
                                        vUp = pUpperPa;
                                        vLo = pLowerPa;
                                    }
                                    else // SecantStiffness
                                    {
                                        vUp = yUp > 1e-10 ? pUpperPa / yUp : 0; // kN/m³
                                        vLo = yLo > 1e-10 ? pLowerPa / yLo : 0;
                                    }

                                    // 長方形を作る 4 点
                                    springValues.Add(vUp); springZs.Add(zTop);
                                    springValues.Add(vUp); springZs.Add(zMid);
                                    springValues.Add(vLo); springZs.Add(zMid);
                                    springValues.Add(vLo); springZs.Add(zBtm);
                                }
                            }
                            else
                            {
                                // 従来の節点ベース処理 (RelativeDisp または IsDistributedMode=OFF)
                                for (int i = 0; i < nSprings; i++)
                                {
                                    var spring = horizontalSoilSprings[i];
                                    if (spring?.NodeI?.Coord == null) continue;

                                    // 深度（杭節点のZ座標）
                                    double z = spring.NodeI.Coord.Z;

                                    // 結果を取得（最終ステップ）
                                    var result = spring.HorizontalSpringResults?
                                        .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                 && r.LoadCombination?.No == loadCombination.No
                                                 && r.IsLiquefaction == isLiquefaction)
                                        .OrderByDescending(r => r.Step)
                                        .FirstOrDefault();

                                    if (result?.CumulativeDisp == null || result?.CumulativeForce == null) continue;

                                    // 相対変位（杭節点 - 地盤節点）のX,Y合成
                                    double relDispX = result.CumulativeDisp.Dxi - result.CumulativeDisp.Dxj;
                                    double relDispY = result.CumulativeDisp.Dyi - result.CumulativeDisp.Dyj;
                                    double relDisp = Math.Sqrt(relDispX * relDispX + relDispY * relDispY);

                                    // ばね反力 (resultant) [kN]
                                    double forceX = result.CumulativeForce.Fxi;
                                    double forceY = result.CumulativeForce.Fyi;
                                    double force = Math.Sqrt(forceX * forceX + forceY * forceY);

                                    // ばね全体剛性 [kN/m] = 反力 [kN] / 変位 [m]
                                    double springStiffness = relDisp > 1e-10 ? force / relDisp : 0;

                                    springZs.Add(z);
                                    if (dataType == "RelativeDisp")
                                        springValues.Add(relDisp * 1000.0); // mm
                                    else if (dataType == "Reaction")
                                        springValues.Add(force); // kN
                                    else if (dataType == "SecantStiffness")
                                        springValues.Add(springStiffness); // kN/m
                                }
                            }

                            if (springZs.Count > 0)
                            {
                                var scatter = wpfPlot.Plot.Add.Scatter(springValues, springZs);
                                scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                                // 案 C v3: 長方形分布モードでは 4 点/セグメントを直線で結ぶだけで長方形が描ける
                                // (ConnectStyle の調整は不要)

                                // ホバーポップアップ用詳細
                                double absMax = springValues.Count > 0 ? springValues.Max(Math.Abs) : 0;
                                string seriesLabel = dataType switch
                                {
                                    "RelativeDisp" => "相対変位",
                                    "Reaction" => "水平地盤反力",
                                    "SecantStiffness" => "水平地盤反力係数",
                                    _ => dataType
                                };
                                _graphHoverMap[scatter] =
                                    $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                    + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                    + $"組合せ: cmb{loadCombination.No} (α={loadCombination.Alpha1:F2}/β₁={loadCombination.Beta1:F2}/β₂={loadCombination.Beta2:F2})\n"
                                    + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                    + $"系列: {seriesLabel} ({unit})\n"
                                    + $"最大絶対値: {absMax:N2} {unit}\n"
                                    + $"節点数: {springZs.Count}";
                            }
                        }
                    }
                }
            }

            string title = dataType switch
            {
                "RelativeDisp" => "相対変位",
                "Reaction" => "水平地盤反力",
                "SecantStiffness" => "水平地盤反力係数",
                _ => dataType
            };
            string axisX = title + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, title, axisX, "Z(m)");
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
            // 同名ケース（LoadAngle 違い）を区別するため角度を付記
            // LoadCombination は No 以外にも α₁/β₁/β₂ で同定されるため組合せ係数も付記して一意化
            return $"{loadCase.LoadName}@{loadCase.LoadAngle:F0}°|cmb{loadCombination.No}"
                 + $"(α={loadCombination.Alpha1:F2}/β₁={loadCombination.Beta1:F2}/β₂={loadCombination.Beta2:F2})"
                 + $"|LIQ:{isLiquefaction}";
        }

        /// <summary>
        /// 慣性力作用点荷重変形関係グラフで、ケースごと（同一 LoadCase/Combination/Liq）に共通の色を返す。
        /// 3 系列（作用点荷重 / 杭地盤ばね反力 / 土圧合力ばね反力）を同色にして線種で区別するのに使用。
        /// </summary>
        private static ScottPlot.Color GetCaseColor(int caseIndex)
        {
            // 視認性の高い定番パレット。ケースが多い場合はインデックスで循環
            var palette = new ScottPlot.Color[]
            {
                ScottPlot.Color.FromHex("#1F77B4"), // 青
                ScottPlot.Color.FromHex("#D62728"), // 赤
                ScottPlot.Color.FromHex("#2CA02C"), // 緑
                ScottPlot.Color.FromHex("#FF7F0E"), // 橙
                ScottPlot.Color.FromHex("#9467BD"), // 紫
                ScottPlot.Color.FromHex("#8C564B"), // 茶
                ScottPlot.Color.FromHex("#E377C2"), // ピンク
                ScottPlot.Color.FromHex("#17BECF"), // シアン
            };
            int idx = ((caseIndex % palette.Length) + palette.Length) % palette.Length;
            return palette[idx];
        }


        // 沈下傾斜レジェンド取得メソッド
        // ys は mm、xs は m のため、Δy/Δx (mm/m) はそのまま「rad × 1/1000」の表記値に一致する。
        private static string GetSettlementAngleLegendText(double angle)
        {
            return $"傾斜角{angle:N1}/1000";
        }

        /// <summary>
        /// 基礎梁考慮鉛直解析結果から指定杭の沈下量を取得する
        /// </summary>
        private double GetVBSettlement(int pileNo, string loadCaseName)
        {
            var vbResults = _mainWindowViewModel.VerticalBeamCaseResults;
            if (vbResults == null || vbResults.Count == 0) return 0;

            // 荷重ケース名でマッチするケースを探す（VLの場合は最初のケース）
            FEM.VerticalBeamCaseResult caseResult = null;
            foreach (var cr in vbResults)
            {
                if (cr.LoadCaseName == loadCaseName ||
                    (loadCaseName == "VL" && cr.LoadCaseName.Contains("VL")))
                {
                    caseResult = cr;
                    break;
                }
            }
            // マッチしなければ最初のケースを使用
            caseResult ??= vbResults[0];

            if (caseResult.PileResults == null) return 0;
            foreach (var pr in caseResult.PileResults)
            {
                if (pr.PileNo == pileNo)
                    return pr.Settlement_mm;
            }
            return 0;
        }

        /// <summary>
        /// NM/NQ曲線から指定軸力Nに対応する値（M or Q）を線形補間で取得
        /// </summary>
        private static double InterpolateLimitValue(List<double> nValues, List<double> mOrQValues, double targetN)
        {
            if (nValues == null || mOrQValues == null || nValues.Count == 0 || nValues.Count != mOrQValues.Count)
                return double.NaN;

            // N値の範囲外チェック
            double minN = nValues.Min();
            double maxN = nValues.Max();

            if (targetN <= minN) return mOrQValues[nValues.IndexOf(minN)];
            if (targetN >= maxN) return mOrQValues[nValues.IndexOf(maxN)];

            // N値でソートされたインデックスを作成
            var sortedIndices = nValues.Select((n, i) => new { N = n, Index = i })
                                        .OrderBy(x => x.N)
                                        .ToList();

            // 線形補間
            for (int i = 0; i < sortedIndices.Count - 1; i++)
            {
                double n1 = sortedIndices[i].N;
                double n2 = sortedIndices[i + 1].N;

                if (n1 <= targetN && targetN <= n2)
                {
                    double m1 = mOrQValues[sortedIndices[i].Index];
                    double m2 = mOrQValues[sortedIndices[i + 1].Index];

                    // 線形補間
                    double ratio = (n2 - n1) != 0 ? (targetN - n1) / (n2 - n1) : 0;
                    return m1 + ratio * (m2 - m1);
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// 選択された限界状態に対応するNM曲線を取得
        /// </summary>
        private static (List<double> N, List<double> M) GetLimitStateNMCurve(PileSection pileSection, string limitState)
        {
            return limitState switch
            {
                "低減前使用限界状態" => (pileSection.UnfactoredServiceNM.N, pileSection.UnfactoredServiceNM.M),
                "低減後使用限界状態" => (pileSection.FactoredServiceNM.N, pileSection.FactoredServiceNM.M),
                "低減前損傷限界状態" => (pileSection.UnfactoredDamageNM.N, pileSection.UnfactoredDamageNM.M),
                "低減後損傷限界状態" => (pileSection.FactoredDamageNM.N, pileSection.FactoredDamageNM.M),
                "低減前安全限界状態" => (pileSection.UnfactoredUltimateNM.N, pileSection.UnfactoredUltimateNM.M),
                "低減後安全限界状態" => (pileSection.FactoredUltimateNM.N, pileSection.FactoredUltimateNM.M),
                _ => (null, null)
            };
        }

        /// <summary>
        /// 選択された限界状態に対応するNQ曲線を取得
        /// </summary>
        private static (List<double> N, List<double> Q) GetLimitStateNQCurve(PileSection pileSection, string limitState)
        {
            return limitState switch
            {
                "低減前使用限界状態" => (pileSection.UnfactoredServiceNQ.N, pileSection.UnfactoredServiceNQ.Q),
                "低減後使用限界状態" => (pileSection.FactoredServiceNQ.N, pileSection.FactoredServiceNQ.Q),
                "低減前損傷限界状態" => (pileSection.UnfactoredDamageNQ.N, pileSection.UnfactoredDamageNQ.Q),
                "低減後損傷限界状態" => (pileSection.FactoredDamageNQ.N, pileSection.FactoredDamageNQ.Q),
                "低減前安全限界状態" => (pileSection.UnfactoredUltimateNQ.N, pileSection.UnfactoredUltimateNQ.Q),
                "低減後安全限界状態" => (pileSection.FactoredUltimateNQ.N, pileSection.FactoredUltimateNQ.Q),
                _ => (null, null)
            };
        }

        /// <summary>
        /// 荷重-杭頭沈下曲線・荷重-杭先端沈下曲線を描画
        /// SoilPile.LoadDisplacementsから杭頭荷重(PileTopLoad) vs 沈下量(DD0s/DDns)をプロット
        /// </summary>
        private void DrawLoadSettlementCurve()
        {
            var soilPiles = InputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            // 選択杭の SoilPileAltNo を取得
            var selectedPiles = GetSelectedPileLayouts();
            var soilPileIndices = new HashSet<int>();
            foreach (var pile in selectedPiles)
            {
                int idx = pile.SoilPileAltNo - 1;
                if (idx >= 0 && idx < soilPiles.Count)
                    soilPileIndices.Add(idx);
            }
            if (soilPileIndices.Count == 0)
            {
                // All の場合は全SoilPile
                for (int i = 0; i < soilPiles.Count; i++)
                    soilPileIndices.Add(i);
            }

            var colors = new[] {
                new ScottPlot.Color(0, 114, 189),    // 青
                new ScottPlot.Color(217, 83, 25),     // オレンジ
                new ScottPlot.Color(119, 172, 48),    // 緑
                new ScottPlot.Color(126, 47, 142),    // 紫
                new ScottPlot.Color(162, 20, 47),     // 赤
                new ScottPlot.Color(77, 190, 238),    // 水色
            };

            int colorIdx = 0;
            foreach (int spIdx in soilPileIndices)
            {
                var sp = soilPiles[spIdx];
                if (sp.LoadDisplacements == null || sp.LoadDisplacements.Count == 0) continue;

                var sorted = sp.LoadDisplacements.OrderBy(ld => ld.PileTopLoad).ToList();
                double[] loads = sorted.Select(ld => ld.PileTopLoad).ToArray();
                double[] headSettlements = sorted.Select(ld => ld.DD0s).ToArray();
                double[] toeSettlements = sorted.Select(ld => ld.DDns).ToArray();

                var color = colors[colorIdx % colors.Length];
                string label = $"杭セット{spIdx + 1}";

                // 杭頭沈下曲線（実線）
                var scatterHead = WpfPlot.Plot.Add.Scatter(headSettlements, loads);
                scatterHead.Color = color;
                scatterHead.LegendText = $"{label} 杭頭";
                scatterHead.MarkerSize = 5;

                // 杭先端沈下曲線（破線）
                var scatterToe = WpfPlot.Plot.Add.Scatter(toeSettlements, loads);
                scatterToe.Color = color;
                scatterToe.LegendText = $"{label} 杭先端";
                scatterToe.MarkerSize = 3;
                scatterToe.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;

                colorIdx++;
            }

            ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "荷重沈下曲線", "沈下量 (mm)", "荷重 (kN)");
        }
    }
}