using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Int32Value = DocumentFormat.OpenXml.Int32Value;
using NumberingFormat = DocumentFormat.OpenXml.Wordprocessing.NumberingFormat;
using Point = System.Windows.Point;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using WpStyle = DocumentFormat.OpenXml.Wordprocessing.Style;

using Serilog;

namespace PileDesign.Output
{
    // TeX パーサ: TeX 文字列 → OfficeMath 変換 (Tex / AddInlineTeXParagraph / ParseTeXToOfficeMath) とセル・数式段落ヘルパ。物理分割 partial (純粋移動)。
    internal partial class WordDocument
    {
        // 追加ヘルパ: TeX 文字列から OfficeMath を作る短縮ラッパ
        public static DocumentFormat.OpenXml.Math.OfficeMath Tex(string tex)
        {
            return ParseTeXToOfficeMath(tex ?? string.Empty);
        }

        // 追加ヘルパ: 単一 TeX 式を含む段落を追加する簡易メソッド（旧 AddInlineMathParagraph と同様の振る舞い）
        public static void AddInlineTeXParagraph(
            Body body,
            string tex,
            double fontSize = 10.5,
            int leftIndentMm = 0,
            int firstLineIndentMm = 0,
            int hangingIndentMm = 0)
        {
            if (body == null) return;
            var math = ParseTeXToOfficeMath(tex ?? string.Empty);
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = CreateIndentation(leftIndentMm, firstLineIndentMm, hangingIndentMm)
                }
            };
            paragraph.Append(math);
            body.Append(paragraph);
        }

        // 追加: 簡易 TeX -> OfficeMath パーサ（WordDocument クラス内の任意の場所に追加）
        public static DocumentFormat.OpenXml.Math.OfficeMath ParseTeXToOfficeMath(string tex)
        {
            if (string.IsNullOrWhiteSpace(tex))
                return GetCombinedRunToMath([GetRun("")]);

            // $...$ があれば取り除く
            if (tex.Length >= 2 && tex[0] == '$' && tex[^1] == '$')
                tex = tex[1..^1];

            int pos = 0;
            int len = tex.Length;

            void SkipSpaces()
            {
                while (pos < len && char.IsWhiteSpace(tex[pos])) pos++;
            }

            char Peek() => pos < len ? tex[pos] : '\0';

            string ReadWhileLetter()
            {
                int s = pos;
                while (pos < len && char.IsLetter(tex[pos])) pos++;
                return tex[s..pos];
            }

            DocumentFormat.OpenXml.Math.Run CombineRunsToRun(List<DocumentFormat.OpenXml.Math.Run> runs)
            {
                if (runs == null || runs.Count == 0) return GetRun("");
                if (runs.Count == 1) return runs[0];
                return GetCombinedRun(runs);
            }

            string MapTexCmd(string cmd) => cmd switch
            {
                "alpha" => "α",
                "beta" => "β",
                "gamma" => "γ",
                "delta" => "δ",
                "eta" => "η",
                "Delta" => "Δ",
                "theta" => "θ",
                "phi" => "φ",
                "pi" => "π",
                "psi" => "ψ",
                "xi" => "ξ",
                "kappa" => "κ",
                "zeta" => "ζ",
                "sigma" => "σ",
                "mu" => "μ",
                "nu" => "ν",
                "lambda" => "λ",
                "epsilon" => "ε",
                "varepsilon" => "ϵ",
                "tau" => "τ",
                "times" => "×",
                "cdot" => "·",
                // degree symbol for \circ
                "circ" => "°",
                // TeX の空白コマンドを Unicode 空白へマップ
                //   -> thin space (U+2009)
                // \:  -> medium/figure space (U+2005)
                // \;  -> em space (U+2003)
                // \!  -> negative thin (扱いづらいので空文字にする)
                "," => "\u2009",
                ":" => "\u2005",
                ";" => "\u2003",
                "!" => "",
                // 不等号コマンド
                "ge" => "≥",
                "geq" => "≥",
                "le" => "≤",
                "leq" => "≤",
                _ => cmd
            };

            // 匹配用ペアを返す（left の開始文字に対する期待終了文字）
            char MatchingRight(char left)
            {
                return left switch
                {
                    '(' => ')',
                    '[' => ']',
                    '{' => '}',
                    '<' => '>',
                    '|' => '|',
                    _ => left
                };
            }

            // Parse a sequence until a terminating char (endChar == '\0' means EOF).
            // expectedRightChar を渡すと、"\right<ch>" を見つけたらそこで終了する挙動を行う（\left...\right...用）
            List<DocumentFormat.OpenXml.Math.Run> ParseSequence(char endChar, char expectedRightChar = '\0')
            {
                var runs = new List<DocumentFormat.OpenXml.Math.Run>();
                while (pos < len && (endChar == '\0' || tex[pos] != endChar))
                {
                    SkipSpaces();
                    if (pos >= len || (endChar != '\0' && tex[pos] == endChar)) break;

                    // For \rightX termination when expectedRightChar is given:
                    if (expectedRightChar != '\0' && pos < len && tex[pos] == '\\')
                    {
                        // match \right (case-insensitive) at current position
                        var remaining = tex[pos..];
                        var m = System.Text.RegularExpressions.Regex.Match(remaining, @"^\\right\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            int checkPos = pos + m.Length;
                            // skip optional spaces after \right
                            while (checkPos < len && char.IsWhiteSpace(tex[checkPos])) checkPos++;

                            if (checkPos < len)
                            {
                                // direct single-char delimiter: \right}
                                if (tex[checkPos] == expectedRightChar)
                                {
                                    pos = checkPos + 1; // consume delimiter
                                    break;
                                }

                                // escaped delimiter: \right\}
                                if (tex[checkPos] == '\\' && checkPos + 1 < len && tex[checkPos + 1] == expectedRightChar)
                                {
                                    pos = checkPos + 2; // consume backslash and delimiter
                                    break;
                                }

                                // invisible delimiter \right.
                                if (tex[checkPos] == '.')
                                {
                                    pos = checkPos + 1;
                                    break;
                                }

                                // named delimiter like \rbrace, \rangle, \vert, \rvert
                                if (tex[checkPos] == '\\')
                                {
                                    int namePos = checkPos + 1;
                                    int nameStart = namePos;
                                    while (namePos < len && char.IsLetter(tex[namePos])) namePos++;
                                    if (namePos > nameStart)
                                    {
                                        string name = tex[nameStart..namePos];
                                        var mapping = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
                                        {
                                            ["rbrace"] = '}',
                                            ["lbrace"] = '{',
                                            ["rangle"] = '>',
                                            ["lang"] = '<',
                                            ["vert"] = '|',
                                            ["rvert"] = '|',
                                        };
                                        if (mapping.TryGetValue(name, out char mapped) && mapped == expectedRightChar)
                                        {
                                            pos = namePos; // consume the name token
                                            break;
                                        }
                                    }
                                }

                                // If none matched, fall through and continue parsing normally.
                            }
                        }
                    }

                    // parse base atom
                    DocumentFormat.OpenXml.Math.Run baseRun;
                    if (tex[pos] == '{')
                    {
                        pos++; // consume '{'
                        var inner = ParseSequence('}', '\0');
                        if (pos < len && tex[pos] == '}') pos++;
                        baseRun = CombineRunsToRun(inner);
                    }
                    else if (tex[pos] == '\\')
                    {
                        pos++; // consume '\'
                        string cmd = ReadWhileLetter();

                        if (string.Equals(cmd, "frac", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cmd, "dfrac", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cmd, "tfrac", StringComparison.OrdinalIgnoreCase))
                        {
                            // expect {num}{den}
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var numList = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var denList = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetFraction(CombineRunsToRun(numList), CombineRunsToRun(denList));
                        }
                        else if (string.Equals(cmd, "sqrt", StringComparison.OrdinalIgnoreCase))
                        {
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var inner = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetRadicalRun(CombineRunsToRun(inner));
                        }
                        else if (string.Equals(cmd, "overline", StringComparison.OrdinalIgnoreCase))
                        {
                            // \overline{...}
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var inner = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetTopBarredRun(CombineRunsToRun(inner));
                        }
                        else if (string.Equals(cmd, "left", StringComparison.OrdinalIgnoreCase))
                        {
                            // \left<delim> ... \right<matching>
                            SkipSpaces();
                            if (pos >= len) { baseRun = GetRun(""); }
                            else
                            {
                                char beginChar = tex[pos];
                                pos++;
                                char expected = MatchingRight(beginChar);
                                // parse until \right<expected>
                                var inner = ParseSequence('\0', expected);
                                // create delimitered run (beginChar, expected)
                                baseRun = GetDelimiteredRun(CombineRunsToRun(inner), beginChar.ToString(), expected.ToString());
                            }
                        }
                        else if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(cmd, "int", StringComparison.OrdinalIgnoreCase))
                        {
                            // parse optional sub/sup in either order for \sum and \int
                            SkipSpaces();
                            DocumentFormat.OpenXml.Math.Run subRun = GetRun("");
                            DocumentFormat.OpenXml.Math.Run superRun = GetRun("");

                            // allow up to two markers (_ or ^) immediately following
                            for (int k = 0; k < 2; k++)
                            {
                                if (pos < len && (tex[pos] == '_' || tex[pos] == '^'))
                                {
                                    char op = tex[pos++];
                                    SkipSpaces();
                                    DocumentFormat.OpenXml.Math.Run argRun;
                                    if (pos < len && tex[pos] == '{')
                                    {
                                        pos++;
                                        var seq = ParseSequence('}');
                                        if (pos < len && tex[pos] == '}') pos++;
                                        argRun = CombineRunsToRun(seq);
                                    }
                                    else if (pos < len && tex[pos] == '\\')
                                    {
                                        pos++;
                                        string cmd2 = ReadWhileLetter();
                                        argRun = GetRun(MapTexCmd(cmd2));
                                    }
                                    else if (pos < len)
                                    {
                                        argRun = GetRun(tex[pos].ToString());
                                        pos++;
                                    }
                                    else
                                    {
                                        argRun = GetRun("");
                                    }

                                    if (op == '_') subRun = argRun;
                                    else superRun = argRun;

                                    SkipSpaces();
                                    continue;
                                }
                                break;
                            }

                            // After optional sub/sup, if a braced body follows treat that as the base (本文)
                            SkipSpaces();
                            if (pos < len && tex[pos] == '{')
                            {
                                pos++; // consume '{'
                                var bodySeq = ParseSequence('}');
                                if (pos < len && tex[pos] == '}') pos++;
                                // base is the body content
                                if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase))
                                    baseRun = GetSummationRun(CombineRunsToRun(bodySeq), subRun, superRun);
                                else
                                    baseRun = GetIntegralRun(CombineRunsToRun(bodySeq), subRun, superRun);
                            }
                            else
                            {
                                // no explicit braced body -> create nary with empty base (symbol only)
                                if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase))
                                    baseRun = GetSummationRun(GetRun(""), subRun, superRun);
                                else
                                    baseRun = GetIntegralRun(GetRun(""), subRun, superRun);
                            }
                        }
                        else
                        {
                            // greek or plain command -> map to character(s)
                            baseRun = GetRun(MapTexCmd(cmd));
                        }
                    }
                    else
                    {
                        // normal characters until special char
                        int s = pos;
                        while (pos < len && !"{}^_\\$".Contains(tex[pos]))
                            pos++;
                        string txt = tex[s..pos];
                        baseRun = GetRun(txt);
                    }

                    SkipSpaces();
                    // handle ^ and _ possibly repeated
                    while (pos < len && (tex[pos] == '^' || tex[pos] == '_'))
                    {
                        char op = tex[pos++];
                        SkipSpaces();
                        DocumentFormat.OpenXml.Math.Run subSuperRun;
                        if (pos < len && tex[pos] == '{')
                        {
                            pos++;
                            var seq = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            subSuperRun = CombineRunsToRun(seq);
                        }
                        else if (pos < len && tex[pos] == '\\')
                        {
                            pos++;
                            string cmd = ReadWhileLetter();
                            subSuperRun = GetRun(MapTexCmd(cmd));
                        }
                        else
                        {
                            // single char
                            if (pos < len)
                            {
                                subSuperRun = GetRun(tex[pos].ToString());
                                pos++;
                            }
                            else
                            {
                                subSuperRun = GetRun("");
                            }
                        }

                        if (op == '^')
                            baseRun = GetSuperscript(baseRun, subSuperRun);
                        else
                            baseRun = GetSubscript(baseRun, subSuperRun);

                        SkipSpaces();
                    }

                    runs.Add(baseRun);
                }
                return runs;
            }

            var runsList = ParseSequence('\0');
            var combined = CombineRunsToRun(runsList);
            return GetCombinedRunToMath([combined]);
        }
        //public static DocumentFormat.OpenXml.Math.OfficeMath ParseTeXToOfficeMath(string tex)
        //{
        //    if (string.IsNullOrWhiteSpace(tex))
        //        return GetCombinedRunToMath([GetRun("")]);

        //    // $...$ があれば取り除く
        //    if (tex.Length >= 2 && tex[0] == '$' && tex[^1] == '$')
        //        tex = tex.Substring(1, tex.Length - 2);

        //    int pos = 0;
        //    int len = tex.Length;

        //    void SkipSpaces()
        //    {
        //        while (pos < len && char.IsWhiteSpace(tex[pos])) pos++;
        //    }

        //    char Peek() => pos < len ? tex[pos] : '\0';

        //    string ReadWhileLetter()
        //    {
        //        int s = pos;
        //        while (pos < len && char.IsLetter(tex[pos])) pos++;
        //        return tex.Substring(s, pos - s);
        //    }

        //    DocumentFormat.OpenXml.Math.Run CombineRunsToRun(List<DocumentFormat.OpenXml.Math.Run> runs)
        //    {
        //        if (runs == null || runs.Count == 0) return GetRun("");
        //        if (runs.Count == 1) return runs[0];
        //        return GetCombinedRun(runs);
        //    }

        //    string MapTexCmd(string cmd) => cmd switch
        //    {
        //        "alpha" => "α",
        //        "beta" => "β",
        //        "gamma" => "γ",
        //        "delta" => "δ",
        //        "theta" => "θ",
        //        "phi" => "φ",
        //        "pi" => "π",
        //        "sum" => "∑",
        //        "int" => "∫",
        //        _ => cmd
        //    };

        //    // Parse a sequence until a terminating char (endChar == '\0' means EOF)
        //    List<DocumentFormat.OpenXml.Math.Run> ParseSequence(char endChar)
        //    {
        //        var runs = new List<DocumentFormat.OpenXml.Math.Run>();
        //        while (pos < len && (endChar == '\0' || tex[pos] != endChar))
        //        {
        //            SkipSpaces();
        //            if (pos >= len || (endChar != '\0' && tex[pos] == endChar)) break;

        //            // parse base atom
        //            DocumentFormat.OpenXml.Math.Run baseRun;
        //            if (tex[pos] == '{')
        //            {
        //                pos++; // consume '{'
        //                var inner = ParseSequence('}');
        //                if (pos < len && tex[pos] == '}') pos++;
        //                baseRun = CombineRunsToRun(inner);
        //            }
        //            else if (tex[pos] == '\\')
        //            {
        //                pos++; // consume '\'
        //                string cmd = ReadWhileLetter();
        //                if (string.Equals(cmd, "frac", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    // expect {num}{den}
        //                    // numerator
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var numList = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    // denominator
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var denList = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    baseRun = GetFraction(CombineRunsToRun(numList), CombineRunsToRun(denList));
        //                }
        //                else if (string.Equals(cmd, "sqrt", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var inner = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    baseRun = GetRadicalRun(CombineRunsToRun(inner));
        //                }
        //                else
        //                {
        //                    // greek or plain command
        //                    baseRun = GetRun(MapTexCmd(cmd));
        //                }
        //            }
        //            else
        //            {
        //                // normal characters until special char
        //                int s = pos;
        //                while (pos < len && !"{}^_\\$".Contains(tex[pos]))
        //                    pos++;
        //                string txt = tex[s..pos];
        //                baseRun = GetRun(txt);
        //            }

        //            SkipSpaces();
        //            // handle ^ and _ possibly repeated
        //            while (pos < len && (tex[pos] == '^' || tex[pos] == '_'))
        //            {
        //                char op = tex[pos++];
        //                SkipSpaces();
        //                DocumentFormat.OpenXml.Math.Run subSuperRun;
        //                if (pos < len && tex[pos] == '{')
        //                {
        //                    pos++;
        //                    var seq = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    subSuperRun = CombineRunsToRun(seq);
        //                }
        //                else if (pos < len && tex[pos] == '\\')
        //                {
        //                    pos++;
        //                    string cmd = ReadWhileLetter();
        //                    subSuperRun = GetRun(MapTexCmd(cmd));
        //                }
        //                else
        //                {
        //                    // single char
        //                    if (pos < len)
        //                    {
        //                        subSuperRun = GetRun(tex[pos].ToString());
        //                        pos++;
        //                    }
        //                    else
        //                    {
        //                        subSuperRun = GetRun("");
        //                    }
        //                }

        //                if (op == '^')
        //                    baseRun = GetSuperscript(baseRun, subSuperRun);
        //                else
        //                    baseRun = GetSubscript(baseRun, subSuperRun);

        //                SkipSpaces();
        //            }

        //            runs.Add(baseRun);
        //        }
        //        return runs;
        //    }

        //    var runsList = ParseSequence('\0');
        //    var combined = CombineRunsToRun(runsList);
        //    return GetCombinedRunToMath([combined]);
        //}

        // 修正: CreateTableCell 内の string 処理を拡張して $...$ を TeX として変換できるようにする
        private static TableCell CreateTableCell(
            List<object> contents,
            double fontSize = 8,
            string alignment = "center",
            string verticalAlignment = "center",
            bool bold = false
        )
        {
            var cell = new TableCell();

            var cellProperties = new TableCellProperties();
            cellProperties.Append(new TableCellVerticalAlignment
            {
                Val = verticalAlignment.ToLower() switch
                {
                    "top" => TableVerticalAlignmentValues.Top,
                    "bottom" => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Center
                }
            });
            cell.Append(cellProperties);

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification
                    {
                        Val = alignment.ToLower() switch
                        {
                            "center" or "centre" => JustificationValues.Center,
                            "right" => JustificationValues.Right,
                            _ => JustificationValues.Left
                        }
                    }
                }
            };

            if (contents != null)
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    var item = contents[i];

                    switch (item)
                    {
                        case string s:
                            foreach (var e in BuildInlineMixedRuns(s, fontSize))
                            {
                                if (bold && e is Run r) { r.RunProperties ??= new RunProperties(); r.RunProperties.Bold ??= new Bold(); }
                                paragraph.Append(e);
                            }
                            break;

                        case Run run:
                            if (bold) { run.RunProperties ??= new RunProperties(); run.RunProperties.Bold ??= new Bold(); }
                            paragraph.Append(run);
                            break;

                        case DocumentFormat.OpenXml.Math.OfficeMath math:
                            paragraph.Append(math);
                            break;

                        case null:
                            break;

                        default:
                            var txt = item.ToString();
                            if (!string.IsNullOrEmpty(txt))
                            {
                                foreach (var e in BuildInlineMixedRuns(txt, fontSize))
                                {
                                    if (bold && e is Run r2) { r2.RunProperties ??= new RunProperties(); r2.RunProperties.Bold ??= new Bold(); }
                                    paragraph.Append(e);
                                }
                            }
                            break;
                    }

                    if (i < contents.Count - 1)
                        paragraph.Append(new Run(new Break()));
                }
            }

            // 空セル対策: Run が 1 つも追加されなかった場合、要求された fontSize の
            // 空 Run を 1 つ追加する。これがないと Word が文書デフォルトフォントサイズ
            // (通常 10.5pt) で段落を描画し、空セルを含む行だけ高さが大きくなる。
            if (!paragraph.Elements<Run>().Any() && !paragraph.Elements<DocumentFormat.OpenXml.Math.OfficeMath>().Any())
            {
                var emptyRun = new Run();
                var emptyRunProps = new RunProperties();
                int half = (int)Math.Round(fontSize * 2);
                emptyRunProps.Append(new FontSize { Val = half.ToString() });
                emptyRunProps.Append(new FontSizeComplexScript { Val = half.ToString() });
                emptyRunProps.Append(CreateDefaultRunFonts());
                emptyRun.RunProperties = emptyRunProps;
                emptyRun.Append(new Text(string.Empty) { Space = SpaceProcessingModeValues.Preserve });
                paragraph.Append(emptyRun);
            }

            cell.Append(paragraph);
            return cell;
        }

        // 修正: AddInlineMathParagraph を更新し、parts 内の文字列に含まれる $...$ を TeX として自動変換するようにする
        public static void AddInlineMathParagraph(
            Body body,
            object[] parts,
            double fontSize = 10.5,
            int leftIndentMm = 0,
            int firstLineIndentMm = 0,
            int hangingIndentMm = 0
        )
        {
            if (body == null) return;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = CreateIndentation(leftIndentMm, firstLineIndentMm, hangingIndentMm)
                }
            };

            if (parts == null || parts.Length == 0)
            {
                body.Append(paragraph);
                return;
            }

            foreach (var part in parts)
            {
                switch (part)
                {
                    case null:
                        continue;

                    case string s:
                        {
                            // 改行分割し各行を BuildInlineMixedRuns で処理
                            var lines = s.Replace("\r\n", "\n").Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                foreach (var e in BuildInlineMixedRuns(lines[i], fontSize))
                                    paragraph.Append(e);

                                if (i < lines.Length - 1)
                                    paragraph.Append(new Run(new Break()));
                            }
                            break;
                        }

                    case Run run:
                        paragraph.Append(run);
                        break;

                    case DocumentFormat.OpenXml.Math.OfficeMath math:
                        paragraph.Append(math);
                        break;

                    default:
                        {
                            var txt = part.ToString();
                            if (string.IsNullOrEmpty(txt)) break;
                            var lines = txt.Replace("\r\n", "\n").Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                foreach (var e in BuildInlineMixedRuns(lines[i], fontSize))
                                    paragraph.Append(e);

                                if (i < lines.Length - 1)
                                    paragraph.Append(new Run(new Break()));
                            }
                            break;
                        }
                }
            }

            body.Append(paragraph);
        }


    }
}
