using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

    /// <summary>解析が止まったことを利用者に知らせる文と、問題の杭の手掛かり。</summary>
    public static class AnalysisFailure
    {
        /// <summary>
        /// 例外 (包まれたものを含む) の文から、関係する杭の番号を拾う。
        /// 杭要素の節点名 (<c>杭節点-{杭番号}-{順}</c>) と「杭No.{番号}」の書き方に当たる。重複は除き、出た順。
        /// </summary>
        public static IReadOnlyList<int> PileNosIn(Exception ex)
        {
            var nos = new List<int>();
            for (var e = ex; e != null; e = e.InnerException)
            {
                foreach (Match m in Regex.Matches(e.Message ?? "", @"杭節点-(\d+)-|杭\s*No\.\s*(\d+)"))
                {
                    string digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (int.TryParse(digits, out int no) && !nos.Contains(no)) nos.Add(no);
                }
            }
            return nos;
        }

        /// <summary>
        /// 知らせの文から、関係する杭の番号を拾う。杭番号 (「杭No.n」「杭節点-n-」) と、杭体 (「杭体n」) を使っている杭。
        /// 解析前の検査の知らせから、直す場所 (杭) へ案内するのに使う。
        /// </summary>
        public static IReadOnlyList<int> PileNosIn(string? text, Models.InputData.InputModel? input)
        {
            var nos = PileNosIn(new Exception(text ?? "")).ToList();
            var bodies = Regex.Matches(text ?? "", @"杭体\s*(\d+)")
                .Select(m => int.TryParse(m.Groups[1].Value, out int b) ? b : -1).Where(b => b > 0).ToHashSet();
            foreach (var pile in input?.PileLayoutItems ?? [])
                if (pile != null && bodies.Contains(pile.PileBodyNo) && !nos.Contains(pile.PileNo)) nos.Add(pile.PileNo);
            return nos;
        }

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
        /// 解析が止まったことを知らせる文。止まった荷重ケース・段階・理由、関係する杭、次にすること。
        /// </summary>
        public static string Describe(Exception ex)
        {
            var failure = FindCaseFailure(ex);
            var reason = failure?.InnerException ?? ex;
            var lines = new List<string> { "解析が途中で止まりました。" };
            if (failure != null)
            {
                lines.Add($"荷重ケース: {failure.CaseTag}");
                if (!string.IsNullOrEmpty(failure.Stage)) lines.Add($"段階: {failure.Stage}");
            }
            lines.Add($"理由: {FirstLine(reason.Message)}");
            var piles = PileNosIn(ex);
            if (piles.Count > 0)
                lines.Add($"関係する杭: No.{string.Join(", ", piles.Take(10))}"
                          + (piles.Count > 10 ? $" ほか {piles.Count - 10} 本" : "")
                          + " (メイン画面で選択しています)");
            lines.Add("");
            lines.Add("解析ウィンドウのログの最後に、止まるまでの経過が残っています。詳細はログファイルにも記録しています。");
            return string.Join("\n", lines);
        }

        private static string FirstLine(string? message)
        {
            string line = (message ?? "").Split('\n')[0].Trim();
            return line.Length == 0 ? "(理由の記録がありません)" : line;
        }
    }
}
