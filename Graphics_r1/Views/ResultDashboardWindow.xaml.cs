using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PileDesign.Views
{
    /// <summary>
    /// 解析結果ダッシュボード。
    ///
    /// 開いた人が最初に知りたいのは「検定は通ったか、通っていないならどの杭か」なので、
    /// 先頭に検定の総括 (OK / NG / 未収束の件数・支配ケース) と杭ごとの最大検定比を置く。
    /// 反復回数や残差といった解析の統計は、以前は前面に出していたが
    /// 設計の判断に直接は要らないので「解析の詳細」に畳んだ。
    ///
    /// 解析中の数値ではなく、解析完了後に呼び出す前提のスナップショット表示。
    /// 「再計算」ボタンで開いたまま現在の状態を再読込できる。
    /// </summary>
    public partial class ResultDashboardWindow : Window
    {
        private readonly MainWindowViewModel _vm;

        /// <summary>杭ごとの一覧の 1 行。</summary>
        private sealed record PileRow(int PileNo, string Status, string RatioText, string Category, string ValuesText, string Condition);

        public ResultDashboardWindow(MainWindowViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
            // 「杭配置を色分け」と「テーブルで開く」は ViewModel に直接結ぶ
            DataContext = _vm;
            Loaded += (_, __) => Refresh();

            // モードレスなので、開いたまま解析を実行・保存されることがある。
            // 解析の完了や杭要素分割の切替を拾って作り直し、古い総括を見せ続けない。
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            Closed += (_, __) => _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.LastAnalysisTime)
                or nameof(MainWindowViewModel.IsHorizontalAnalysisDone)
                or nameof(MainWindowViewModel.IsVerticalAnalysisDone)
                or nameof(MainWindowViewModel.IsVerticalBeamAnalysisDone)
                or nameof(MainWindowViewModel.IsElementSplit))
            {
                if (IsLoaded) Dispatcher.BeginInvoke(Refresh);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

        /// <summary>
        /// 再計算。リボンから開き直したときと、テスト (Loaded は Show しないと来ない) から呼ぶ。
        /// </summary>
        internal void RefreshNow() => Refresh();

        private void Refresh()
        {
            LastUpdateText.Text = $"更新: {DateTime.Now:HH:mm:ss}";

            PileEvaluationSummary summary;
            try
            {
                summary = _vm.GetEvaluationSummary(force: true);
            }
            catch (Exception ex)
            {
                // 検定が組めなくても解析の状態は出す
                Serilog.Log.Warning(ex, "[ダッシュボード] 検定サマリーの生成に失敗");
                summary = PileEvaluationSummary.FromResults(null, null, new EvaluationResult([]), "A");
            }

            UpdateVerdictSection(summary);
            UpdatePileListSection(summary);
            UpdateStateSection();
            UpdateDetailSection();
        }

        // ---- 検定の総括 ----------------------------------------------------
        private void UpdateVerdictSection(PileEvaluationSummary summary)
        {
            bool horizontalDone = _vm.IsHorizontalAnalysisDone && _vm.CurrentModel != null;
            var h = summary.Horizontal;
            var u = summary.HorizontalUnfactored;
            var b = summary.Bearing;

            SeismicGradeText.Text = summary.SeismicGrade;

            // 水平解析 (低減後)
            if (!horizontalDone)
            {
                SetMuted(HorizontalCountsText, "未実施");
                SetMuted(HorizontalMaxRatioText, "—");
                SetMuted(HorizontalGoverningText, "—");
                SetMuted(UnconvergedCasesText, "—");
                SetMuted(UnfactoredText, "—");
            }
            else if (h == null || h.IsEmpty)
            {
                SetMuted(HorizontalCountsText, "検定なし (検定の対象になる項目がありません)");
                SetMuted(HorizontalMaxRatioText, "—");
                SetMuted(HorizontalGoverningText, "—");
                SetMuted(UnconvergedCasesText, "—");
                SetMuted(UnfactoredText, "—");
            }
            else
            {
                HorizontalCountsText.Text = CountsText(h);
                HorizontalCountsText.Style = h.NgCount > 0 ? StyleResource("DashWarnStyle")
                    : h.UnconvergedCount > 0 ? StyleResource("DashCautionStyle")
                    : StyleResource("DashOkStyle");

                HorizontalMaxRatioText.Text = h.MaxRatio is double r ? r.ToString("F2", CultureInfo.InvariantCulture) : "—";
                HorizontalMaxRatioText.Style = RatioStyle(h.MaxRatio);

                HorizontalGoverningText.Text = Describe(h.Governing);
                HorizontalGoverningText.Style = StyleResource("DashValueStyle");
                HorizontalGoverningText.FontWeight = FontWeights.Normal;

                var names = summary.UnconvergedCaseNames;
                if (names.Count == 0)
                {
                    SetMuted(UnconvergedCasesText, "なし");
                }
                else
                {
                    UnconvergedCasesText.Text = string.Join(" / ", names);
                    UnconvergedCasesText.Style = StyleResource("DashCautionStyle");
                    UnconvergedCasesText.FontWeight = FontWeights.Normal;
                }

                if (u == null || u.IsEmpty)
                {
                    SetMuted(UnfactoredText, "—");
                }
                else
                {
                    UnfactoredText.Text = u.MaxRatio is double ur
                        ? $"{CountsText(u)}、最大検定比 {ur.ToString("F2", CultureInfo.InvariantCulture)}"
                        : CountsText(u);
                    UnfactoredText.Style = StyleResource("DashValueStyle");
                    UnfactoredText.FontWeight = FontWeights.Normal;
                }
            }

            // 杭の鉛直支持力
            if (b.IsEmpty)
            {
                SetMuted(BearingCountsText, _vm.IsElementSplit ? "検定なし" : "未実施 (杭要素分割を行うと検定します)");
                SetMuted(BearingGoverningText, "—");
            }
            else
            {
                BearingCountsText.Text = CountsText(b);
                BearingCountsText.Style = b.NgCount > 0 ? StyleResource("DashWarnStyle") : StyleResource("DashOkStyle");
                BearingGoverningText.Text = b.MaxRatio is double br
                    ? $"{br.ToString("F2", CultureInfo.InvariantCulture)}　{Describe(b.Governing)}"
                    : "—";
                BearingGoverningText.Style = RatioStyle(b.MaxRatio);
                BearingGoverningText.FontWeight = FontWeights.Normal;
            }

            // 判定バッジ
            if (!horizontalDone && b.IsEmpty)
            {
                SetVerdict("未実施", "#999999",
                    "水平解析または杭要素分割を実行すると、検定の総括がここに出ます。");
                return;
            }

            int ngCount = (h?.NgCount ?? 0) + b.NgCount;
            int unconvergedCount = h?.UnconvergedCount ?? 0;
            if (ngCount > 0)
            {
                SetVerdict($"NG {ngCount} 件", BrushHex("ErrorBrush"),
                    "限界値を超えた項目があります。下の一覧で杭を確認してください。");
            }
            else if (unconvergedCount > 0)
            {
                SetVerdict($"未収束 {unconvergedCount} 件", BrushHex("StatusWarningDarkBrush"),
                    "収束しなかった荷重ケースがあり、その項目は OK / NG を判定できません。"
                    + "計算ステップ数を増やして再解析するか、耐力が足りているかを確認してください。");
            }
            else if (!horizontalDone)
            {
                SetVerdict("支持力 OK", BrushHex("NikkenGreenBrush"),
                    "杭の鉛直支持力はすべて OK です。水平解析は未実施です。");
            }
            else
            {
                double max = Math.Max(h?.MaxRatio ?? 0, b.MaxRatio ?? 0);
                SetVerdict("すべて OK", BrushHex("NikkenGreenBrush"),
                    $"最大検定比 {max.ToString("F2", CultureInfo.InvariantCulture)}。");
            }
        }

        private void SetVerdict(string text, string backgroundHex, string note)
        {
            VerdictText.Text = text;
            VerdictBadge.Background = (Brush)new BrushConverter().ConvertFromString(backgroundHex)!;
            VerdictNoteText.Text = note;
        }

        private string BrushHex(string resourceKey) =>
            FindResource(resourceKey) is SolidColorBrush brush ? brush.Color.ToString() : "#999999";

        private static string CountsText(EvaluationResult r) =>
            r.UnconvergedCount > 0
                ? $"OK {r.OkCount} / NG {r.NgCount} / 未収束 {r.UnconvergedCount}　(全 {r.Items.Count} 件)"
                : $"OK {r.OkCount} / NG {r.NgCount}　(全 {r.Items.Count} 件)";

        /// <summary>支配ケースの 1 行。「対象｜検定項目｜荷重条件」。</summary>
        private static string Describe(EvaluationItem? item) =>
            item == null ? "—" : $"{item.TargetDescription}｜{item.Category}｜{item.ConditionDescription}";

        private System.Windows.Style RatioStyle(double? ratio) => ratio switch
        {
            null => StyleResource("DashMutedStyle"),
            > 1.0 => StyleResource("DashWarnStyle"),
            > PileEvaluationSummary.TightThreshold => StyleResource("DashCautionStyle"),
            _ => StyleResource("DashOkStyle"),
        };

        // ---- 杭ごとの最大検定比 --------------------------------------------
        private void UpdatePileListSection(PileEvaluationSummary summary)
        {
            var piles = summary.ByRatioDescending;
            int total = (_vm.ResultInputModel ?? _vm.CurrentInputModel)?.PileLayoutItems?.Count ?? 0;

            if (piles.Count == 0)
            {
                SetMuted(PileBandCountsText, "検定した杭はありません。");
                PileRatioGrid.ItemsSource = null;
                return;
            }

            int ng = piles.Count(e => e.Band == PileRatioBand.Ng);
            int tight = piles.Count(e => e.Band == PileRatioBand.Tight);
            int unconverged = piles.Count(e => e.Band == PileRatioBand.Unconverged);
            int safe = piles.Count(e => e.Band == PileRatioBand.Safe);
            PileBandCountsText.Text =
                $"検定した杭 {piles.Count} 本 / 全 {total} 本 ─ NG {ng} 本、余裕小 (0.8 超) {tight} 本、未収束 {unconverged} 本、余裕あり {safe} 本";
            PileBandCountsText.Style = ng > 0 ? StyleResource("DashWarnStyle") : StyleResource("DashValueStyle");
            PileBandCountsText.FontWeight = FontWeights.Normal;

            PileRatioGrid.ItemsSource = piles.Select(ToRow).ToList();
        }

        private static PileRow ToRow(PileEvaluationEntry e)
        {
            var g = e.Governing;
            string ratio = double.IsNaN(e.MaxRatio) ? "—" : e.MaxRatio.ToString("F2", CultureInfo.InvariantCulture);
            string category = g?.Category ?? (e.HasUnconverged ? "(未収束のケースのみ)" : "");
            string values = g == null ? "" : $"{g.ResponseText} / {g.LimitText} {g.Unit}".TrimEnd();
            string condition = g?.ConditionDescription ?? "";
            return new PileRow(e.PileNo, e.StatusLabel, ratio, category, values, condition);
        }

        // ---- 解析の状態 ----------------------------------------------------
        private void UpdateStateSection()
        {
            var am = _vm.CurrentModel;
            var steps = am?.AnalysisStepResults;
            bool horizontalDone = _vm.IsHorizontalAnalysisDone;

            HorizontalStatusText.Text = horizontalDone ? "✓ 完了" : (steps?.Count > 0 ? "△ 実施済 (未確定)" : "未実施");
            HorizontalStatusText.Style = horizontalDone ? StyleResource("DashOkStyle") : StyleResource("DashMutedStyle");

            if (steps == null || steps.Count == 0)
            {
                HorizontalCaseCountText.Text = "—";
            }
            else
            {
                // ケース数 = (LoadCase, LoadCombination, IsLiquefaction) の組合せ数
                int caseCount = steps
                    .Select(r => (r.LoadCase?.Level ?? 0, r.LoadCase?.No ?? 0,
                                  r.LoadCombination?.No ?? 0, r.IsLiquefaction))
                    .Distinct()
                    .Count();
                HorizontalCaseCountText.Text = $"{caseCount}";
            }

            bool settlementDone = _vm.IsVerticalAnalysisDone;
            SettlementStatusText.Text = settlementDone ? "✓ 完了" : "未実施";
            SettlementStatusText.Style = settlementDone ? StyleResource("DashOkStyle") : StyleResource("DashMutedStyle");

            bool beamDone = _vm.IsVerticalBeamAnalysisDone;
            VerticalBeamStatusText.Text = beamDone ? "✓ 完了" : "未実施";
            VerticalBeamStatusText.Style = beamDone ? StyleResource("DashOkStyle") : StyleResource("DashMutedStyle");

            LastAnalysisText.Text = _vm.LastAnalysisTime is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") : "—";

            CurrentFileText.Text = string.IsNullOrEmpty(_vm.CurrentFilePath)
                ? "(新規)"
                : Path.GetFileName(_vm.CurrentFilePath);
            CurrentFileText.ToolTip = _vm.CurrentFilePath;
        }

        // ---- 解析の詳細 (規模・収束) ---------------------------------------
        private void UpdateDetailSection()
        {
            var im = _vm.CurrentInputModel;
            var am = _vm.CurrentModel;

            PileCountText.Text = im?.PileLayoutItems is { } piles ? $"{piles.Count} 本" : "—";
            InputNodeCountText.Text = im?.InputNodes is { } inodes ? $"{inodes.Count} 個" : "—";
            FemNodeCountText.Text = am?.Nodes is { } nodes ? $"{nodes.Count:N0} 個" : "—";
            FemBeamCountText.Text = am?.Beams is { } beams ? $"{beams.Count:N0} 本" : "—";

            int beamCaseCount = _vm.VerticalBeamCaseResults?.Count ?? 0;
            VerticalBeamCaseCountText.Text = beamCaseCount > 0 ? $"{beamCaseCount}" : "—";

            var steps = am?.AnalysisStepResults;
            if (steps == null || steps.Count == 0)
            {
                HorizontalStepCountText.Text = "—";
                HorizontalAvgIterText.Text = "—";
                HorizontalMaxIterText.Text = "—";
                HorizontalWorstResidualText.Text = "—";
                HorizontalWorstResidualText.Style = StyleResource("DashValueStyle");
                HorizontalMaxDispText.Text = "—";
                return;
            }

            HorizontalStepCountText.Text = $"{steps.Count:N0}";

            double avgIter = steps.Average(r => r.Iteration);
            int maxIter = steps.Max(r => r.Iteration);
            double worstRes = steps.Max(r => r.ResidualValue);

            HorizontalAvgIterText.Text = $"{avgIter:F1}";
            HorizontalMaxIterText.Text = $"{maxIter}";
            HorizontalWorstResidualText.Text = worstRes.ToString("E2");
            HorizontalWorstResidualText.Style = worstRes > 1.0E-3 ? StyleResource("DashWarnStyle") : StyleResource("DashValueStyle");

            // 代表変位 (m): 累積変位の絶対値最大
            double maxAbs = 0;
            if (am?.Nodes != null)
            {
                foreach (var n in am.Nodes)
                {
                    var d = n.CumulativeDisp;
                    if (d == null) continue;
                    double a = Math.Max(Math.Abs(d.Ux), Math.Max(Math.Abs(d.Uy), Math.Abs(d.Uz)));
                    if (a > maxAbs) maxAbs = a;
                }
            }
            HorizontalMaxDispText.Text = maxAbs > 0 ? $"{maxAbs:F4}" : "—";
        }

        // ---- 小道具 --------------------------------------------------------
        private System.Windows.Style StyleResource(string key) => (System.Windows.Style)FindResource(key);

        private void SetMuted(System.Windows.Controls.TextBlock block, string text)
        {
            block.Text = text;
            block.Style = StyleResource("DashMutedStyle");
        }
    }
}
