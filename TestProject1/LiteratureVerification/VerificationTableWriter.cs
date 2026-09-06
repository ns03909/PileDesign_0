using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TestProject1.LiteratureVerification
{
    /// <summary>
    /// カタログの結果から、検証ウィンドウ (verification.html / verification_methods.html)・README・
    /// About 用 JSON を組み立てる。
    ///
    /// 学会の指針 (<see cref="SourceKind.AcademicStandard"/>) と認定工法のカタログ
    /// (<see cref="SourceKind.CertifiedMethod"/>) は別ページ・別表にする。設計法そのものの再現と、
    /// メーカーが公表している算定式の再現とでは、読む側の受け止め方が違うため。
    ///
    /// すべて同じ結果列から作るので、件数や値が食い違うことはない。
    /// 実行時刻は入れない (入れると毎回差分になり、「変わったから更新」が判別できなくなる)。
    /// </summary>
    public static class VerificationTableWriter
    {
        public const string HtmlBegin = "<!-- verification-summary:begin (自動生成: VerificationTableTests が書く。手で編集しない) -->";
        public const string HtmlEnd = "<!-- verification-summary:end -->";
        public const string MethodsHtmlBegin = "<!-- verification-methods:begin (自動生成: VerificationTableTests が書く。手で編集しない) -->";
        public const string MethodsHtmlEnd = "<!-- verification-methods:end -->";
        public const string MarkdownBegin = "<!-- verification-table:begin (自動生成: VerificationTableTests が書く。手で編集しない) -->";
        public const string MarkdownEnd = "<!-- verification-table:end -->";

        public static List<LiteratureCheckResult> Evaluate(IEnumerable<LiteratureCheck> checks) =>
            checks.Select(c => new LiteratureCheckResult(c, c.Program())).ToList();

        public static IReadOnlyList<LiteratureCheckResult> OfKind(IReadOnlyList<LiteratureCheckResult> results, SourceKind kind) =>
            results.Where(r => r.Check.SourceKind == kind).ToList();

        // ── JSON (About 画面が件数を読む) ──

        public static string ToJson(IReadOnlyList<LiteratureCheckResult> results)
        {
            var academic = OfKind(results, SourceKind.AcademicStandard);
            var methods = OfKind(results, SourceKind.CertifiedMethod);
            var doc = new
            {
                summary = new
                {
                    count = results.Count,
                    okCount = results.Count(r => r.Ok),
                    sources = results.Select(r => r.Check.Source).Distinct().ToArray(),
                    academic = new { count = academic.Count, okCount = academic.Count(r => r.Ok), sources = academic.Select(r => r.Check.Source).Distinct().ToArray() },
                    certifiedMethods = new { count = methods.Count, okCount = methods.Count(r => r.Ok), sources = methods.Select(r => r.Check.Source).Distinct().ToArray() },
                },
                items = results.Select(r => new
                {
                    kind = r.Check.SourceKind.ToString(),
                    source = r.Check.Source,
                    section = r.Check.Section,
                    item = r.Check.Item,
                    unit = r.Check.Unit,
                    literature = r.Check.Literature,
                    program = r.ProgramText,
                    tolerance = r.Check.ToleranceLabel,
                    diffPercent = r.RelativeDiffPercent is double d ? Math.Round(d, 2) : (double?)null,
                    ok = r.Ok,
                    note = r.Check.Note,
                }).ToArray(),
            };
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }).Replace("\r\n", "\n") + "\n";
        }

        // ── HTML ──

        /// <summary>学会の指針の一覧 (verification.html の先頭)。</summary>
        public static string ToAcademicHtmlBlock(IReadOnlyList<LiteratureCheckResult> all)
        {
            var results = OfKind(all, SourceKind.AcademicStandard);
            int ok = results.Count(r => r.Ok);
            string lead =
                $"学会の指針・計算例に数値として書かれている {results.Count} 項目を本プログラムで計算し、{ok} 項目が文献の許容内でした。" +
                "この一覧はテスト (<code>VerificationTableTests</code>) が毎回計算して書き出したもので、テストが通る限り表と実装は一致しています。" +
                "認定工法 (杭先端の支持力算定) のカタログとの照合は <a href=\"verification_methods.html\">別ページ</a> にあります。";
            return HtmlBlock(HtmlBegin, HtmlEnd, lead, results);
        }

        /// <summary>認定工法のカタログの一覧 (verification_methods.html)。</summary>
        public static string ToMethodsHtmlBlock(IReadOnlyList<LiteratureCheckResult> all)
        {
            var results = OfKind(all, SourceKind.CertifiedMethod);
            int ok = results.Count(r => r.Ok);
            string lead =
                $"認定工法のカタログ・設計マニュアルに数値として書かれている {results.Count} 項目を本プログラムで計算し、{ok} 項目がカタログの表記と一致しました。" +
                "この一覧はテスト (<code>VerificationTableTests</code>) が毎回計算して書き出したもので、テストが通る限り表と実装は一致しています。";
            return HtmlBlock(MethodsHtmlBegin, MethodsHtmlEnd, lead, results);
        }

        private static string HtmlBlock(string begin, string end, string lead, IReadOnlyList<LiteratureCheckResult> results)
        {
            var sb = new StringBuilder();
            sb.Append(begin).Append('\n');
            sb.Append("            <p class=\"summary-lead\">").Append(lead).Append("</p>\n");

            foreach (var group in results.GroupBy(r => r.Check.Source))
            {
                int gOk = group.Count(r => r.Ok);
                sb.Append($"            <div class=\"item-title\">{H(group.Key)}　<span class=\"summary-count\">{gOk} / {group.Count()} 項目</span></div>\n");
                sb.Append("            <table class=\"comparison-table\">\n");
                sb.Append("                <tr><th>箇所</th><th>項目</th><th>文献値</th><th>プログラム値</th><th>差</th><th>許容</th><th>判定</th></tr>\n");
                foreach (var r in group)
                {
                    string unit = string.IsNullOrEmpty(r.Check.Unit) ? "" : " " + r.Check.Unit;
                    string diff = r.RelativeDiffPercent is double d ? d.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "—";
                    string verdict = r.Ok ? "<span class=\"match\">一致</span>" : "<span class=\"diff\">不一致</span>";
                    string item = H(r.Check.Item) + (r.Check.Note is { } note ? $"<br><small>{H(note)}</small>" : "");
                    sb.Append("                <tr>")
                      .Append($"<td>{H(r.Check.Section)}</td>")
                      .Append($"<td style=\"text-align:left\">{item}</td>")
                      .Append($"<td>{LiteratureCheck.FormatValue(r.Check.Literature)}{H(unit)}</td>")
                      .Append($"<td>{r.ProgramText}{H(unit)}</td>")
                      .Append($"<td>{diff}</td>")
                      .Append($"<td>{H(r.Check.ToleranceLabel)}</td>")
                      .Append($"<td>{verdict}</td>")
                      .Append("</tr>\n");
                }
                sb.Append("            </table>\n");
            }
            sb.Append("            ").Append(end);
            return sb.ToString();
        }

        // ── Markdown (README) ──

        public static string ToMarkdownBlock(IReadOnlyList<LiteratureCheckResult> all)
        {
            var academic = OfKind(all, SourceKind.AcademicStandard);
            var methods = OfKind(all, SourceKind.CertifiedMethod);

            var sb = new StringBuilder();
            sb.Append(MarkdownBegin).Append('\n');
            sb.Append($"文献に数値として書かれている **{all.Count} 項目**を計算し、**{all.Count(r => r.Ok)} 項目**が文献の許容内です")
              .Append("（`TestProject1/LiteratureVerification/LiteratureCatalog.cs` が出典と値を持ち、")
              .Append("`VerificationTableTests` が照合してこの表を書き出します）。\n\n");

            sb.Append($"### 学会の指針・計算例 ({academic.Count(r => r.Ok)} / {academic.Count} 項目)\n\n");
            AppendMarkdownTable(sb, academic);

            sb.Append($"\n### 認定工法のカタログ ({methods.Count(r => r.Ok)} / {methods.Count} 項目)\n\n");
            sb.Append("杭先端の支持力算定を、メーカーが評定・認定の範囲で公表している算定式・対比表と照合したものです。")
              .Append("学会の指針の再現とは性格が違うので、検証ウィンドウでも別ページにしています。\n\n");
            AppendMarkdownTable(sb, methods);

            sb.Append(MarkdownEnd);
            return sb.ToString();
        }

        private static void AppendMarkdownTable(StringBuilder sb, IReadOnlyList<LiteratureCheckResult> results)
        {
            sb.Append("| 出典 | 箇所 | 項目 | 文献値 | プログラム値 | 差 | 許容 | 判定 |\n");
            sb.Append("|---|---|---|---:|---:|---:|---|---|\n");
            foreach (var r in results)
            {
                string unit = string.IsNullOrEmpty(r.Check.Unit) ? "" : " " + r.Check.Unit;
                string diff = r.RelativeDiffPercent is double d ? d.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "—";
                sb.Append($"| {M(r.Check.Source)} | {M(r.Check.Section)} | {M(r.Check.Item)} | ")
                  .Append($"{LiteratureCheck.FormatValue(r.Check.Literature)}{unit} | {r.ProgramText}{unit} | ")
                  .Append($"{diff} | {M(r.Check.ToleranceLabel)} | {(r.Ok ? "一致" : "**不一致**")} |\n");
            }
        }

        // ── ファイルの印の間を差し替える ──

        /// <summary>印の間を <paramref name="block"/> (印を含む) で置き換えた文字列。印が無ければ null。</summary>
        public static string? ReplaceBetween(string text, string begin, string end, string block)
        {
            int b = text.IndexOf(begin, StringComparison.Ordinal);
            int e = text.IndexOf(end, StringComparison.Ordinal);
            if (b < 0 || e < 0 || e < b) return null;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            return text[..b] + block.Replace("\n", nl) + text[(e + end.Length)..];
        }

        /// <summary>印の間 (印を含む) を取り出す。無ければ null。</summary>
        public static string? ExtractBetween(string text, string begin, string end)
        {
            int b = text.IndexOf(begin, StringComparison.Ordinal);
            int e = text.IndexOf(end, StringComparison.Ordinal);
            if (b < 0 || e < 0 || e < b) return null;
            return text[b..(e + end.Length)].Replace("\r\n", "\n");
        }

        private static string H(string s) => WebUtility.HtmlEncode(s);
        private static string M(string s) => s.Replace("|", "\\|");
    }
}
