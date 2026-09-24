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
                // 検定が組めなくても解析の状態は出す。ただし「検定なし」「すべて OK」と読めないよう、
                // 組めなかったことをまとめに持たせて渡す
                Serilog.Log.Warning(ex, "[ダッシュボード] 検定サマリーの生成に失敗");
                summary = PileEvaluationSummary.FromResults(null, null, new EvaluationResult([]), "A",
                    [new PileEvaluationSummary.BuildFailure(PileEvaluationSummary.WholePart, ex.Message)]);
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
            else if (summary.HorizontalFailed)
            {
                // 組めなかったことを「検定なし」と区別して出す
                SetWarn(HorizontalCountsText, "検定を組めませんでした (OK / NG を判定できません)");
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
                HorizontalCountsText.Style = CountsStyle(h);

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

                if (summary.HorizontalUnfactoredFailed)
                {
                    SetWarn(UnfactoredText, "検定を組めませんでした");
                }
                else if (u == null || u.IsEmpty)
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
            if (summary.BearingFailed)
            {
                SetWarn(BearingCountsText, "検定を組めませんでした (OK / NG を判定できません)");
                SetMuted(BearingGoverningText, "—");
            }
            else if (b.IsEmpty)
            {
                SetMuted(BearingCountsText, _vm.IsElementSplit ? "検定なし" : "未実施 (杭要素分割を行うと検定します)");
                SetMuted(BearingGoverningText, "—");
            }
            else
            {
                BearingCountsText.Text = CountsText(b);
                BearingCountsText.Style = CountsStyle(b);
                BearingGoverningText.Text = b.MaxRatio is double br
                    ? $"{br.ToString("F2", CultureInfo.InvariantCulture)}　{Describe(b.Governing)}"
                    : "—";
                BearingGoverningText.Style = RatioStyle(b.MaxRatio);
                BearingGoverningText.FontWeight = FontWeights.Normal;
            }

            // 判定バッジ
            var verdict = DecideVerdict(summary, horizontalDone);
            SetVerdict(verdict.Text, verdict.Kind switch
            {
                VerdictKind.NotRun => "#999999",
                VerdictKind.Ng => BrushHex("ErrorBrush"),
                VerdictKind.CannotJudge => BrushHex("StatusWarningDarkBrush"),
                _ => BrushHex("NikkenGreenBrush"),
            }, verdict.Note);
        }

        /// <summary>判定バッジの種類 (色の選び分け)。</summary>
        internal enum VerdictKind { NotRun, Ng, CannotJudge, Ok }

        /// <summary>判定バッジの中身。</summary>
        internal sealed record Verdict(string Text, VerdictKind Kind, string Note);

        /// <summary>
        /// 総合判定を決める (画面に依らない部分。テストはここを直接見る)。
        ///
        /// 「OK」を出してよいのは、組めた検定に NG が無く、<b>判定できない項目も無い</b>ときだけ。
        /// 判定できない項目は 3 通りある。
        /// <list type="bullet">
        /// <item>検定の組み立てに失敗した。以前は失敗を「検定なし」として扱い、水平解析が済んでいるのに
        ///   支持力だけを見て「すべて OK」を出した。</item>
        /// <item>収束しなかった荷重ケースの項目。</item>
        /// <item>算定式 (高強度せん断補強筋の工法) の適用範囲の外の項目。以前は総合判定がこれを数えず、
        ///   杭ごとの一覧には「適用範囲外」と出ているのに、上に緑の「すべて OK」を出した。</item>
        /// </list>
        /// NG は判定できた事実なので、判定できない項目があっても NG を先に出す (説明に書き添える)。
        /// </summary>
        internal static Verdict DecideVerdict(PileEvaluationSummary summary, bool horizontalDone)
        {
            var h = horizontalDone ? summary.Horizontal : null;
            var b = summary.Bearing;
            bool horizontalFailed = horizontalDone && summary.HorizontalFailed;
            bool bearingFailed = summary.BearingFailed;

            if (!horizontalDone && b.IsEmpty && !bearingFailed)
                return new Verdict("未実施", VerdictKind.NotRun,
                    "水平解析または杭要素分割を実行すると、検定の総括がここに出ます。");

            var failedParts = new List<string>();
            if (horizontalFailed) failedParts.Add(PileEvaluationSummary.HorizontalPart);
            if (bearingFailed) failedParts.Add(PileEvaluationSummary.BearingPart);
            string failedNote = failedParts.Count == 0 ? ""
                : $"{string.Join("・", failedParts)}の検定を組めませんでした"
                  + (summary.Failures.Count > 0 ? $" ({summary.Failures[0].Message})" : "") + "。";

            int ngCount = (h?.NgCount ?? 0) + b.NgCount;
            int unconvergedCount = (h?.UnconvergedCount ?? 0) + b.UnconvergedCount;
            int outOfScopeCount = (h?.OutOfScopeCount ?? 0) + b.OutOfScopeCount;

            if (ngCount > 0)
                return new Verdict($"NG {ngCount} 件", VerdictKind.Ng,
                    "限界値を超えた項目があります。下の一覧で杭を確認してください。"
                    + (failedNote.Length > 0 ? " " + failedNote : ""));

            if (failedParts.Count > 0)
                return new Verdict("判定できません", VerdictKind.CannotJudge,
                    failedNote + " OK / NG を判定できません。再計算するか、解析をやり直してください"
                    + "（詳しい理由はログに記録しています）。");

            if (unconvergedCount > 0)
                return new Verdict($"未収束 {unconvergedCount} 件", VerdictKind.CannotJudge,
                    "収束しなかった荷重ケースがあり、その項目は OK / NG を判定できません。"
                    + "計算ステップ数を増やして再解析するか、耐力が足りているかを確認してください。"
                    + (outOfScopeCount > 0 ? $" ほかに適用範囲外の項目が {outOfScopeCount} 件あります。" : ""));

            if (outOfScopeCount > 0)
                return new Verdict($"適用範囲外 {outOfScopeCount} 件", VerdictKind.CannotJudge,
                    "せん断耐力の算定式 (高強度せん断補強筋の工法) の適用範囲の外の項目があり、OK / NG を判定できません。"
                    + "下の一覧で杭を確認し、工法の適用範囲に収まる断面に見直すか、工法を「標準」にして検討してください。");

            if (!horizontalDone)
                return new Verdict("支持力 OK", VerdictKind.Ok,
                    "杭の鉛直支持力はすべて OK です。水平解析は未実施です。");

            double max = Math.Max(h?.MaxRatio ?? 0, b.MaxRatio ?? 0);
            return new Verdict("すべて OK", VerdictKind.Ok,
                $"最大検定比 {max.ToString("F2", CultureInfo.InvariantCulture)}。");
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
            $"OK {r.OkCount} / NG {r.NgCount}"
            + (r.UnconvergedCount > 0 ? $" / 未収束 {r.UnconvergedCount}" : "")
            + (r.OutOfScopeCount > 0 ? $" / 適用範囲外 {r.OutOfScopeCount}" : "")
            + $"　(全 {r.Items.Count} 件)";

        /// <summary>件数の色。NG は赤、判定できない項目 (未収束・適用範囲外) があれば注意色、それ以外は緑。</summary>
        private System.Windows.Style CountsStyle(EvaluationResult r) =>
            r.NgCount > 0 ? StyleResource("DashWarnStyle")
            : r.UnconvergedCount > 0 || r.OutOfScopeCount > 0 ? StyleResource("DashCautionStyle")
            : StyleResource("DashOkStyle");

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
            int outOfScope = piles.Count(e => e.Band == PileRatioBand.OutOfScope);
            int safe = piles.Count(e => e.Band == PileRatioBand.Safe);
            PileBandCountsText.Text =
                $"検定した杭 {piles.Count} 本 / 全 {total} 本 ─ NG {ng} 本、余裕小 (0.8 超) {tight} 本、未収束 {unconverged} 本、"
                + (outOfScope > 0 ? $"適用範囲外 {outOfScope} 本、" : "")
                + $"余裕あり {safe} 本";
            PileBandCountsText.Style = ng > 0 ? StyleResource("DashWarnStyle") : StyleResource("DashValueStyle");
            PileBandCountsText.FontWeight = FontWeights.Normal;

            PileRatioGrid.ItemsSource = piles.Select(ToRow).ToList();
        }

        private static PileRow ToRow(PileEvaluationEntry e)
        {
            var g = e.Governing;
            string ratio = double.IsNaN(e.MaxRatio) ? "—" : e.MaxRatio.ToString("F2", CultureInfo.InvariantCulture);
            string category = g?.Category
                ?? (e.HasUnconverged ? "(未収束のケースのみ)" : e.HasOutOfScope ? "(適用範囲外の項目のみ)" : "");
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

        private void SetWarn(System.Windows.Controls.TextBlock block, string text)
        {
            block.Text = text;
            block.Style = StyleResource("DashWarnStyle");
        }
    }
}
