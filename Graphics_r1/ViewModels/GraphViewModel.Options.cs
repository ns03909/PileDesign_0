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
using Serilog;
using PileDesign.Services;
//using System.Windows.Forms;

namespace PileDesign.ViewModels
{
    // グラフオプション/選択状態: ホバー詳細・杭要素選択・液状化ケース・凡例表示・選択中ケース/組合せ/杭配置の解決。GraphViewModel.cs からの物理分割 (純粋移動)。
    public partial class GraphViewModel
    {
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
        /// 現在 NMINT グラフで選択中の杭区間の PileSection を返す (NMINT でない/未選択なら null)。
        /// 解析結果グラフからの断面ひずみ・応力プロファイル表示に用いる。
        /// </summary>
        public PileSection? GetSelectedNmintPileSection()
        {
            if (SelectedGraphOption == null || !SelectedGraphOption.StartsWith("NMINT")) return null;
            var pileBody = InputModel?.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
            if (pileBody?.PileBodySegments == null ||
                SelectedPileSegmentNo < 1 || SelectedPileSegmentNo > pileBody.PileBodySegments.Count)
                return null;
            return pileBody.PileBodySegments[SelectedPileSegmentNo - 1].PileSection;
        }

        /// <summary>
        /// 現在のグラフ種別に合わせて PileSegmentOptions を再構築する。
        /// p-y 以外（NMINT/QNINT/M-φ/EI-φ 等）は入力杭体セグメント数を、
        /// p-y は杭要素分割後の HorizontalSoilReactions 数を使う。
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

            // 選択肢が同じでも、選択値は見直す。
            // グラフ種別を p-y (「すべて」あり) から NMINT (「すべて」なし) へ切替えたときは
            // 区間数が同じでも一覧の中身が変わるので、数だけでなく<b>先頭が「すべて」かどうか</b>も
            // 見る。ここで抜けると NMINT の一覧に「すべて」が残り、選ぶと空グラフになる。
            var wanted = BuildPileSegmentOptions(count);
            if (PileSegmentOptions == null
                || PileSegmentOptions.Count != wanted.Count
                || (PileSegmentOptions.Count > 0 && PileSegmentOptions[0] != wanted[0]))
            {
                PileSegmentOptions = wanted;
            }

            string resolved = ResolvePileSegmentOption(PileSegmentOptions);
            if (resolved != SelectedPileSegmentOption)
                SelectedPileSegmentOption = resolved;
        }

        private void UpdatePileSegmentDetails()
        {
            if (SelectedGraphOption != "水平地盤反力度p-y"
                || string.IsNullOrEmpty(SelectedPileSegmentOption)
                || SelectedPileSegmentOption == UiText.All
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

        // 性能グレード（NMINT/QNINT グラフで限界曲線の選択に使用）
        // "A": レベル1 損傷限界 + レベル2 安全限界
        // "S": レベル2 損傷限界（安全限界は描画しない）
        private string _selectedSeismicGrade = "A";
        public string SelectedSeismicGrade
        {
            get => _selectedSeismicGrade;
            set
            {
                if (SetProperty(ref _selectedSeismicGrade, value))
                {
                    OnPropertyChanged(nameof(IsSeismicGradeA));
                    OnPropertyChanged(nameof(IsSeismicGradeS));
                    UpdateGraph();
                }
            }
        }
        // RadioButton 用ヘルパー
        public bool IsSeismicGradeA
        {
            get => SelectedSeismicGrade == "A";
            set { if (value) SelectedSeismicGrade = "A"; }
        }
        public bool IsSeismicGradeS
        {
            get => SelectedSeismicGrade == "S";
            set { if (value) SelectedSeismicGrade = "S"; }
        }

        // 性能グレードオプション表示（NMINT/QNINT 時のみ）
        private bool _isSeismicGradeOptionVisible;
        public bool IsSeismicGradeOptionVisible
        {
            get => _isSeismicGradeOptionVisible;
            set => SetProperty(ref _isSeismicGradeOptionVisible, value);
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
            if (SelectedLoadCaseOption == UiText.All)
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
            if (SelectedLoadCombinationOption == UiText.All)
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
            if (SelectedPileOption == UiText.All)
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

    }
}
