using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeMath = DocumentFormat.OpenXml.Math.OfficeMath;

namespace PileDesign.Output
{
    /// <summary>
    /// 計算書の数式を、本文の順に並べた LaTeX ファイル (.tex) にする。
    ///
    /// <para>利用者の声: 「計算書出力の時に、各種計算式が LaTeX で出力されると自前の計算書への転記が楽」。
    /// 計算書の式はもともと LaTeX で書いて Word の数式へ変換している (<see cref="WordDocument.ParseTeXToOfficeMath"/>)。
    /// 変換のときに元の LaTeX を数式に結び付けておき、出来上がった本文を頭から読んで、見出しと
    /// 数式を含む段落 (説明の文と数値を代入した式も) を書き出す。本文から読むので、順番と見出しは計算書と同じになる。</para>
    ///
    /// <para>計算書の LaTeX には Word への変換だけが分かる書き方が混ざる。変換は知らない命令を「命令の名前の文字」として
    /// 出す (<c>\P_{s}</c> は P に添字 s) が、標準の LaTeX で <c>\P</c> は段落記号 ¶ になる。そこで標準の LaTeX に無い
    /// 命令は、Word と同じく名前の文字に置き換える (<see cref="ToStandardLatex"/>)。</para>
    /// </summary>
    internal static class ReportTexExporter
    {
        // 数式 → 元の LaTeX。弱参照なので、計算書を作り終えて数式が捨てられれば一緒に消える
        private static readonly ConditionalWeakTable<OfficeMath, string> Sources = new();

        /// <summary>Word の数式を作った元の LaTeX を覚える (<see cref="WordDocument.ParseTeXToOfficeMath"/> が呼ぶ)。</summary>
        internal static void Remember(OfficeMath math, string tex) => Sources.AddOrUpdate(math, tex);

        /// <summary>数式の元の LaTeX。LaTeX から作っていない数式は null。</summary>
        internal static string? SourceOf(OfficeMath math) => Sources.TryGetValue(math, out var tex) ? tex : null;

        /// <summary>.tex にしたもの。<paramref name="Equations"/> は書き出した数式の数、<paramref name="Missing"/> は元の LaTeX が見つからなかった数式の数。</summary>
        internal sealed record Result(string Text, int Equations, int Missing);

        /// <summary>
        /// 標準の LaTeX (amsmath まで) にある命令。これ以外の命令は Word の変換と同じく名前の文字に置き換える。
        /// Word の変換が記号に変えている命令 (MapTexCmd) と、構造の命令 (分数・根号など) を含む。
        /// </summary>
        internal static readonly HashSet<string> StandardCommands = new(StringComparer.Ordinal)
        {
            // 構造
            "frac", "dfrac", "tfrac", "sqrt", "overline", "underline", "left", "right", "sum", "int", "prod",
            "text", "mathrm", "mathit", "mathsf", "mathbf", "operatorname", "hat", "bar", "tilde", "vec", "dot",
            // ギリシャ文字
            "alpha", "beta", "gamma", "delta", "epsilon", "varepsilon", "zeta", "eta", "theta", "vartheta", "iota",
            "kappa", "lambda", "mu", "nu", "xi", "pi", "rho", "sigma", "tau", "upsilon", "phi", "varphi", "chi",
            "psi", "omega", "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon", "Phi", "Psi", "Omega",
            // 記号・関係
            "times", "cdot", "circ", "ge", "geq", "le", "leq", "neq", "approx", "pm", "mp", "infty", "partial",
            "rightarrow", "longrightarrow", "leftarrow", "to", "jmath", "imath", "ldots", "cdots", "prime",
            // 空白
            "quad", "qquad", "enspace", "thinspace",
            // 関数名
            "max", "min", "log", "ln", "exp", "sin", "cos", "tan", "sinh", "cosh", "tanh", "lim", "sup", "inf",
        };

        /// <summary>
        /// 計算書の LaTeX を、そのまま貼れる標準の LaTeX にする。前後の $ を外し、標準に無い命令を名前の文字にする
        /// (Word の変換と同じ見え方)。記号の命令 (<c>\,</c> <c>\{</c> など) は触らない。
        /// </summary>
        internal static string ToStandardLatex(string tex)
        {
            string s = (tex ?? "").Trim();
            if (s.Length >= 2 && s[0] == '$' && s[^1] == '$') s = s[1..^1].Trim();
            return Regex.Replace(s, @"\\([A-Za-z]+)", m =>
                StandardCommands.Contains(m.Groups[1].Value) ? m.Value : m.Groups[1].Value);
        }

        /// <summary>本文の文字を LaTeX の文字として書ける形にする。</summary>
        internal static string EscapeText(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(c switch
                {
                    '\\' => @"\textbackslash{}",
                    '{' => @"\{",
                    '}' => @"\}",
                    '$' => @"\$",
                    '&' => @"\&",
                    '#' => @"\#",
                    '%' => @"\%",
                    '_' => @"\_",
                    '^' => @"\^{}",
                    '~' => @"\~{}",
                    _ => c.ToString(),
                });
            }
            return sb.ToString();
        }

        /// <summary>計算書の本文から .tex を作る。見出しと、数式を含む段落だけを本文の順に書く。</summary>
        internal static Result Build(OpenXmlElement body, string sourceName, DateTime date)
        {
            ArgumentNullException.ThrowIfNull(body);
            int equations = 0, missing = 0;
            // 見出しは (段の深さ, 行)、式の段落は (0, 行)
            var entries = new List<(int Level, string[] Lines)>();

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                // 見出しの中の段落や数式の中の段落 (数式の段落) は、外側でまとめて書く
                if (paragraph.Ancestors<OfficeMath>().Any()) continue;

                string? style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                int headingLevel = style != null && style.StartsWith("Heading", StringComparison.Ordinal)
                    && int.TryParse(style.AsSpan("Heading".Length), out int lv) ? lv : 0;
                bool hasMath = paragraph.Descendants<OfficeMath>().Any();
                if (headingLevel == 0 && !hasMath) continue;

                var parts = new List<(bool IsMath, string Text)>();
                Collect(paragraph, parts, ref equations, ref missing);

                if (headingLevel > 0)
                {
                    string title = string.Concat(parts.Select(p => p.IsMath ? "$" + p.Text + "$" : p.Text)).Trim();
                    if (title.Length == 0) continue;
                    string command = headingLevel switch { 1 => "section", 2 => "subsection", 3 => "subsubsection", _ => "paragraph" };
                    entries.Add((headingLevel, ["", $"\\{command}{{{title}}}"]));
                    continue;
                }

                var maths = parts.Where(p => p.IsMath).Select(p => p.Text).ToList();
                bool textOnlySpaces = parts.Where(p => !p.IsMath).All(p => string.IsNullOrWhiteSpace(p.Text));
                if (textOnlySpaces)
                {
                    // 式だけの段落は別行立ての式にする
                    entries.Add((0, [@"\[", "  " + string.Join(@" \quad ", maths), @"\]"]));
                }
                else
                {
                    // 説明の文と式が混ざる段落 (記号の説明など) は、文の中の式にする
                    entries.Add((0, [string.Concat(parts.Select(p => p.IsMath ? "$" + p.Text + "$" : p.Text)).Trim(), ""]));
                }
            }

            // 式を 1 つも含まない見出しは省く (計算書の章の多くは表と図だけで、見出しだけが並ぶと読みにくい)。
            // 見出しは、次の同じ段か浅い段の見出しまでに式があれば残す
            var lines = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var (level, entryLines) = entries[i];
                if (level > 0)
                {
                    bool hasContent = false;
                    for (int k = i + 1; k < entries.Count && (entries[k].Level == 0 || entries[k].Level > level); k++)
                    {
                        if (entries[k].Level == 0) { hasContent = true; break; }
                    }
                    if (!hasContent) continue;
                }
                lines.AddRange(entryLines);
            }

            var sb = new StringBuilder();
            sb.AppendLine("% 計算書の数式 (本文の順)。杭基礎検討プログラムが計算書と一緒に出力しました。");
            sb.AppendLine($"% 計算書: {sourceName}");
            sb.AppendLine($"% 出力日: {date:yyyy-MM-dd}");
            sb.AppendLine("% 見出しと、数式を含む段落 (記号の説明・数値を代入した式) だけを並べています。表・図は含みません。");
            sb.AppendLine("% 1 つの式だけを使うときは、\\[ と \\] の間 (または $ と $ の間) を写してください。");
            sb.AppendLine("% このまま組むときは LuaLaTeX で (例: lualatex 計算書.tex)。");
            if (missing > 0)
                sb.AppendLine($"% 元の LaTeX が見つからなかった数式が {missing} 個あります。その箇所は Word の文字を \\text で書いています。");
            sb.AppendLine(@"\documentclass{ltjsarticle}");
            sb.AppendLine(@"\usepackage{amsmath}");
            sb.AppendLine(@"\usepackage{unicode-math}");
            sb.AppendLine(@"\begin{document}");
            foreach (var line in lines) sb.AppendLine(line);
            sb.AppendLine(@"\end{document}");
            return new Result(sb.ToString(), equations, missing);
        }

        // 段落の中を頭から読み、文字と数式を順に集める。数式の中には入らない
        private static void Collect(OpenXmlElement element, List<(bool IsMath, string Text)> parts, ref int equations, ref int missing)
        {
            foreach (var child in element.ChildElements)
            {
                switch (child)
                {
                    case OfficeMath math:
                        equations++;
                        if (SourceOf(math) is { } tex)
                        {
                            parts.Add((true, ToStandardLatex(tex)));
                        }
                        else
                        {
                            missing++;
                            parts.Add((true, @"\text{" + EscapeText(math.InnerText) + "}"));
                        }
                        break;
                    case Text text:
                        parts.Add((false, EscapeText(text.Text)));
                        break;
                    case TabChar:
                        parts.Add((false, " "));
                        break;
                    case FieldCode:
                    case DeletedText:
                        break;
                    default:
                        Collect(child, parts, ref equations, ref missing);
                        break;
                }
            }
        }
    }
}
