using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Common;

namespace PileDesign.Services
{
    /// <summary>
    /// 解析が途中で止まったとき、どの荷重ケースのどの段階で止まったかを持つ例外。
    ///
    /// <para>以前は「解析中にエラーが発生しました」と例外の文だけを出していた。ケースは並列に解くので、
    /// ログをさかのぼらないとどのケースで止まったか分からず、止まった段階 (準備・何ステップ目の反復) も
    /// 分からなかった。1 ケースの解析をこれで包み、止まった位置を添えて知らせる。</para>
    /// </summary>
    public sealed class AnalysisCaseFailedException : Exception
    {
        public AnalysisCaseFailedException(string caseTag, string? stage, Exception inner)
            : base($"{caseTag} の{(string.IsNullOrEmpty(stage) ? "解析" : stage)}で止まりました: {inner.Message}", inner)
        {
            CaseTag = caseTag;
            Stage = stage;
        }

        /// <summary>荷重ケースの表示名 (レベル・ケース・組合せ・液状化)。</summary>
        public string CaseTag { get; }

        /// <summary>止まった段階 (「準備」「荷重ステップ 3/12」など)。分からなければ null。</summary>
        public string? Stage { get; }
    }

    /// <summary>
    /// 解析が止まったことを利用者に知らせる文と、問題の場所。
    ///
    /// <para>場所は例外の文から拾わず、例外が持つ問題 (<see cref="DiagnosticException"/>) から取る。以前は
    /// 文の中の「杭No.n」「杭節点-n-」を正規表現で拾っていて、文の書き方が変わると黙って外れた。
    /// 場所の分からない例外 (連立方程式が解けないなど) は、止まったケースと段階からログで追うよう案内する。</para>
    /// </summary>
    public static class AnalysisFailure
    {
        /// <summary>例外が持つ問題 (場所つき)。止まったケース・段階を、場所を持たない問題にも添える。</summary>
        public static IReadOnlyList<Diagnostic> DiagnosticsIn(Exception ex)
        {
            var found = DiagnosticException.CollectFrom(ex);
            var failure = FindCaseFailure(ex);
            if (failure == null) return found;
            // 場所の分からない問題には止まったケース・段階を場所として添える (ログのどこを見ればよいか)
            return found.Select(d => d.Target.Kind == DiagnosticTargetKind.None
                ? d with { Target = DiagnosticTarget.LoadCase(failure.CaseTag, failure.Stage) }
                : d).ToList();
        }

        /// <summary>例外が持つ問題に出てくる杭の番号 (重複なし・出た順)。</summary>
        public static IReadOnlyList<int> PileNosIn(Exception ex)
            => DiagnosticsIn(ex).SelectMany(d => d.Targets)
                .Where(t => t.Kind == DiagnosticTargetKind.Pile && t.PileNo.HasValue)
                .Select(t => t.PileNo!.Value).Distinct().ToList();

        /// <summary>止まったケースの例外を探す (並列に解いたときは AggregateException に包まれる)。</summary>
        public static AnalysisCaseFailedException? FindCaseFailure(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is AnalysisCaseFailedException f) return f;
                if (e is AggregateException agg)
                    return agg.Flatten().InnerExceptions.OfType<AnalysisCaseFailedException>().FirstOrDefault();
            }
            return null;
        }

        /// <summary>
        /// 解析が止まったことを知らせる文。止まった荷重ケース・段階・理由、関係する場所、途中の結果の扱い、次にすること。
        /// <paramref name="selection"/> はメイン画面で選んだ範囲 (<see cref="DiagnosticSelection.Resolve"/>)。
        /// </summary>
        public static string Describe(Exception ex, ReviewSelection? selection = null)
        {
            var failure = FindCaseFailure(ex);
            var reason = failure?.InnerException ?? ex;
            var diagnostics = DiagnosticsIn(ex);
            var lines = new List<string> { "解析が途中で止まりました。" };
            if (failure != null)
            {
                lines.Add($"荷重ケース: {failure.CaseTag}");
                if (!string.IsNullOrEmpty(failure.Stage)) lines.Add($"段階: {failure.Stage}");
            }
            lines.Add($"理由: {FirstLine(reason.Message)}");

            var located = diagnostics.SelectMany(d => d.Targets).Where(t => t.Kind is not (DiagnosticTargetKind.None or DiagnosticTargetKind.LoadCase))
                                     .Select(t => t.Label).Distinct().ToList();
            if (located.Count > 0)
                lines.Add("関係する場所: " + string.Join("、", located.Take(10)) + (located.Count > 10 ? $" ほか {located.Count - 10} か所" : ""));
            if (selection != null && DiagnosticSelection.DescribeSelection(selection) is { } scope)
                lines.Add(scope);

            lines.Add("");
            lines.Add("途中までの結果はメイン画面に登録していません。メイン画面の結果は、前に登録した解析のまま (あれば) です。");
            if (located.Count == 0)
            {
                // 場所の分からない止まり方は、止まった位置 (ケース・段階) からログで追う
                lines.Add(failure != null
                    ? $"止まった場所を入力の番号で特定できませんでした。解析ウィンドウのログで、荷重ケース「{failure.CaseTag}」"
                      + (string.IsNullOrEmpty(failure.Stage) ? "" : $"の「{failure.Stage}」") + " の直前の記録を確認してください。"
                    : "止まった場所を入力の番号で特定できませんでした。解析ウィンドウのログの最後 (モデルの作成・準備の記録) を確認してください。");
            }
            else
            {
                lines.Add("解析ウィンドウのログの最後に、止まるまでの経過が残っています。");
            }
            lines.Add("詳細はログファイルにも記録しています。");
            return string.Join("\n", lines);
        }

        private static string FirstLine(string? message)
        {
            string line = (message ?? "").Split('\n')[0].Trim();
            return line.Length == 0 ? "(理由の記録がありません)" : line;
        }
    }
}
