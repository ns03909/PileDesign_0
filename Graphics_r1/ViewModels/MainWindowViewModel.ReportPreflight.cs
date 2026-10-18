using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>計算書を作る前の確認の重さ。</summary>
    public enum ReportPreflightSeverity
    {
        /// <summary>知らせ (計算書の作り方)。</summary>
        Info,
        /// <summary>確かめたいこと (NG など。計算書としては正しい)。</summary>
        Caution,
        /// <summary>結果が欠ける・古い (計算書の一部が省かれる・判定できない)。</summary>
        Warning,
    }

    /// <summary>確認の項目から移れる先。</summary>
    public enum ReportPreflightTarget
    {
        None,
        /// <summary>解析結果ダッシュボード。</summary>
        Dashboard,
        /// <summary>水平解析の画面。</summary>
        HorizontalAnalysis,
    }

    /// <summary>計算書を作る前の確認の 1 項目。</summary>
    public sealed record ReportPreflightItem(string Text, ReportPreflightSeverity Severity, ReportPreflightTarget Target = ReportPreflightTarget.None)
    {
        public string Mark => Severity switch
        {
            ReportPreflightSeverity.Warning => "⚠",
            ReportPreflightSeverity.Caution => "!",
            _ => "ℹ",
        };

        public string TargetText => Target switch
        {
            ReportPreflightTarget.Dashboard => "ダッシュボードで確認",
            ReportPreflightTarget.HorizontalAnalysis => "水平解析の画面を開く",
            _ => "",
        };

        public bool HasTarget => Target != ReportPreflightTarget.None;
    }

    /// <summary>確認の画面で選ばれたこと。</summary>
    public enum ReportPreflightChoice
    {
        /// <summary>このまま作る。</summary>
        Proceed,
        /// <summary>作らない。</summary>
        Cancel,
        /// <summary>作らずに、その項目の確認先へ移る。</summary>
        Navigate,
    }

    /// <summary>
    /// 計算書の出力の結末 (2026-10-18 のレビュー)。出力設定の画面は、これを見て閉じるかどうかを決める。
    /// 以前は結末によらず閉じたので、保存先の選択をキャンセルしても、保存に失敗しても、設定をし直す必要があった。
    /// </summary>
    public enum ReportOutputResult
    {
        /// <summary>作成した (自動で開けなかったときも含む)。</summary>
        Completed,
        /// <summary>作らなかった (保存先の選択・作る前の確認でキャンセル、作成中に中止)。</summary>
        Cancelled,
        /// <summary>作成に失敗した。</summary>
        Failed,
        /// <summary>作る前の確認から、確認先へ移る。</summary>
        Navigated,
        /// <summary>出せない状態だった (解析結果と編集後の入力が混ざっている。再解析が要る)。</summary>
        Refused,
    }

    /// <summary>杭ごとの検定の件数 (確認に使う分)。</summary>
    public readonly record struct ReportEvaluationCounts(int Evaluated, int Ng, int Unconverged, int OutOfScope, int Unavailable)
    {
        public static ReportEvaluationCounts From(PileEvaluationSummary summary)
        {
            var piles = summary.ByRatioDescending;
            return new ReportEvaluationCounts(piles.Count,
                piles.Count(e => e.Band == PileRatioBand.Ng),
                piles.Count(e => e.HasUnconverged),
                piles.Count(e => e.HasOutOfScope),
                piles.Count(e => e.HasUnavailable));
        }
    }

    /// <summary>
    /// MainWindowViewModel partial: 計算書を作る前の確認 (2026-10-17 のレビュー)。
    ///
    /// <para>以前は、解析のあとに入力を編集していたときに「入力が編集されています」と断るだけで、
    /// 未実施の解析・入力変更前の結果・NG・判定できない項目は、出来上がった計算書を読んで初めて分かった。
    /// 作る前に、それらを 1 つの画面にまとめ、確認先 (ダッシュボード・水平解析の画面) へ移れるようにする。
    /// 確かめることが無ければ画面は出さない。</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>確認の画面を出す (メイン画面が設定する)。選ばれたことと、移る先 (Navigate のとき) を返す。</summary>
        public Func<IReadOnlyList<ReportPreflightItem>, (ReportPreflightChoice Choice, ReportPreflightTarget Target)>? AskReportPreflight { get; set; }

        /// <summary>
        /// 確認の項目を組む (純粋関数)。確かめることが無ければ空。
        /// <paramref name="staleNotice"/> は古くなった結果の帯の文 (古い結果が無ければ null)、
        /// <paramref name="inputEditedSinceAnalysis"/> は解析のあとに入力を編集したか (帯の文が無くても編集はある)。
        /// </summary>
        internal static IReadOnlyList<ReportPreflightItem> BuildReportPreflight(
            IReadOnlyList<RequiredAnalysisState> analyses, string? staleNotice, bool inputEditedSinceAnalysis,
            ReportEvaluationCounts? counts, IReadOnlyList<string>? evaluationFailures = null, bool evaluationUnavailable = false)
        {
            var items = new List<ReportPreflightItem>();
            foreach (var a in analyses.Where(a => !a.Done))
                items.Add(new($"{a.Name}解析が未実施です。計算書ではその章が省かれ、関係する検定は「検定できない項目」になります。",
                    ReportPreflightSeverity.Warning,
                    a.Name == "水平" ? ReportPreflightTarget.HorizontalAnalysis : ReportPreflightTarget.None));

            if (!string.IsNullOrEmpty(staleNotice))
                foreach (var line in staleNotice.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    items.Add(new(line, ReportPreflightSeverity.Warning,
                        line.Contains("水平解析") ? ReportPreflightTarget.HorizontalAnalysis : ReportPreflightTarget.None));
            if (inputEditedSinceAnalysis)
                items.Add(new("計算書は、解析結果と整合させるため、解析を実行した時点の入力で作成します (編集後の入力では作りません)。",
                    ReportPreflightSeverity.Info));

            // 検定のまとめそのものが取れなかった (例外)。件数が無いことを「確かめることが無い」と同じに扱わない (2026-10-18)。
            // 以前はログに残すだけで、ほかに項目が無ければ確認の画面も出さずに作っていた
            if (evaluationUnavailable)
                items.Add(new("検定の状態を確認できませんでした (理由はログに記録しています)。NG や判定できない項目があるかは分かりません。",
                    ReportPreflightSeverity.Warning, ReportPreflightTarget.Dashboard));

            foreach (var part in evaluationFailures ?? [])
                items.Add(new($"「{part}」の検定を組み立てられませんでした。計算書のその部分の検定は判定できません。",
                    ReportPreflightSeverity.Warning, ReportPreflightTarget.Dashboard));

            if (counts is { } c)
            {
                if (c.Ng > 0)
                    items.Add(new($"NG の杭が {c.Ng} 本あります (検定した杭 {c.Evaluated} 本のうち)。",
                        ReportPreflightSeverity.Caution, ReportPreflightTarget.Dashboard));
                if (c.Unconverged > 0)
                    items.Add(new($"収束しなかったケースがある杭が {c.Unconverged} 本あります。そのケースの検定は判定できません。",
                        ReportPreflightSeverity.Warning, ReportPreflightTarget.Dashboard));
                if (c.OutOfScope > 0)
                    items.Add(new($"算定式の適用範囲の外の項目がある杭が {c.OutOfScope} 本あります。",
                        ReportPreflightSeverity.Warning, ReportPreflightTarget.Dashboard));
                if (c.Unavailable > 0)
                    items.Add(new($"データが欠けて検定できなかった項目がある杭が {c.Unavailable} 本あります。",
                        ReportPreflightSeverity.Warning, ReportPreflightTarget.Dashboard));
            }
            return items;
        }

        /// <summary>
        /// 出力設定の画面を閉じるか。キャンセルと失敗は開いたまま (選び直して、そのままやり直せる)。
        /// 確認先へ移るときは閉じる (モーダルなので、閉じないと移った先を操作できない)。
        /// 出せない状態のときも閉じる (再解析が要り、ここでやり直す意味が無い)。
        /// </summary>
        internal static bool ShouldCloseReportOptions(ReportOutputResult result) =>
            result is ReportOutputResult.Completed or ReportOutputResult.Navigated or ReportOutputResult.Refused;

        /// <summary>
        /// 計算書を作る前に確かめる。作ってよければ Proceed。確認先へ移ると選んだとき (Navigate) は、出力の画面が閉じてから移る。
        /// 画面の無い実行 (テスト・起動の確認) では確かめずに作る。
        /// </summary>
        internal ReportPreflightChoice ConfirmReportPreflight()
        {
            ReportEvaluationCounts? counts = null;
            IReadOnlyList<string>? failures = null;
            bool unavailable = false;
            try
            {
                var summary = GetEvaluationSummary(force: true);
                counts = ReportEvaluationCounts.From(summary);
                failures = summary.Failures.Select(f => f.Part).Distinct().ToList();
            }
            catch (Exception ex)
            {
                // 件数が取れなかったことを確認の項目にする (作るかどうかは利用者が決める)
                Serilog.Log.Warning(ex, "[計算書] 作る前の確認で検定の件数を取れませんでした");
                unavailable = true;
            }

            var items = BuildReportPreflight(RequiredAnalyses, ResultStaleNotice, InputChangedSinceAnalysis, counts, failures, unavailable);
            if (items.Count == 0) return ReportPreflightChoice.Proceed;
            if (MessageService.IsUnattended || AskReportPreflight == null)
            {
                Serilog.Log.Information("[計算書] 作る前の確認 ({Count} 件) を出さずに作ります: {Items}", items.Count, string.Join(" / ", items.Select(i => i.Text)));
                return ReportPreflightChoice.Proceed;
            }

            var (choice, target) = AskReportPreflight(items);
            if (choice == ReportPreflightChoice.Proceed) return choice;
            if (choice == ReportPreflightChoice.Navigate)
            {
                // 出力の画面 (モーダル) が閉じてから移る (開いたままだと、移った先がその後ろに隠れる)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    if (target == ReportPreflightTarget.Dashboard) OpenResultDashboardCommand.Execute(null);
                    else if (target == ReportPreflightTarget.HorizontalAnalysis) OpenLateralLoadAnalysisWindowCommand.Execute(null);
                }));
            }
            return choice;
        }
    }
}
