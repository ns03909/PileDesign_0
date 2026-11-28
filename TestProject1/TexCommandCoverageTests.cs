using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 計算書の数式に書いた TeX のコマンドが、変換表に載っていること。
    ///
    /// <para>変換器 (<c>WordDocument.TeX.cs</c> の <c>MapTexCmd</c>) は<b>知らないコマンドを
    /// そのままの文字として出す</b>。そのため <c>\quad</c> は数式の中に「quad」と並び、
    /// <c>\text{〜}</c> は「text〜」になる。落ちないので気づけない。実際に計算書の 16 か所で
    /// quad が、9 か所で text が文字として出ていた (2026-09-12 に発見)。</para>
    ///
    /// <para>この検査は、計算書のソースに書かれた TeX コマンドを集めて、変換表または
    /// 変換器が構造として扱うコマンド (frac・sqrt・left・right・sum・int) に入っているかを見る。
    /// 関数名として読めるもの (min・max・log・tan など) と 1 文字の変数は、そのまま文字で出て
    /// 正しいので除く。</para>
    /// </summary>
    [TestClass]
    public class TexCommandCoverageTests
    {
        /// <summary>変換器が構造として扱うコマンド (MapTexCmd を通らない)。</summary>
        private static readonly string[] Structural =
            ["frac", "dfrac", "tfrac", "sqrt", "left", "right", "sum", "int", "begin", "end", "cases"];

        /// <summary>そのまま文字で出て正しいもの (数式の中で立体の関数名として読む)。</summary>
        private static readonly string[] OperatorNames =
            ["min", "max", "log", "ln", "exp", "sin", "cos", "tan"];

        [TestMethod]
        public void EveryTexCommandInTheReportIsKnownToTheConverter()
        {
            string converter = TestSource.Read("Graphics_r1", "Output", "WordDocument.TeX.cs");
            var mapped = Regex.Matches(converter, @"^\s*""([a-zA-Z,:;!]+)""\s*=>", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            TestSource.AssertScanned(mapped.Count, 25, "変換表のコマンド");

            var unknown = new SortedDictionary<string, List<string>>();
            int scannedFiles = 0;

            foreach (string file in Directory.EnumerateFiles(
                         TestSource.Dir("Graphics_r1", "Output"), "WordDocument*.cs"))
            {
                string text = File.ReadAllText(file);
                if (!text.Contains("AddEq(") && !text.Contains("Tex(")) continue;
                scannedFiles++;

                // 数式を書いている行だけを見る (コメントや通常の文の \n などを拾わないため)
                foreach (string line in text.Split('\n'))
                {
                    if (!line.Contains("AddEq(") && !line.Contains("Tex(")
                        && !line.Contains("\\frac") && !line.Contains("\\left")) continue;
                    if (line.TrimStart().StartsWith("//")) continue;

                    foreach (Match m in Regex.Matches(line, @"\\([a-zA-Z]+)"))
                    {
                        string cmd = m.Groups[1].Value;
                        if (cmd.Length == 1) continue;                       // 1 文字は変数の添字等
                        if (mapped.Contains(cmd)) continue;
                        if (Structural.Contains(cmd)) continue;
                        if (OperatorNames.Contains(cmd)) continue;

                        if (!unknown.TryGetValue(cmd, out var where))
                            unknown[cmd] = where = [];
                        if (where.Count < 2) where.Add(Path.GetFileName(file));
                    }
                }
            }

            TestSource.AssertScanned(scannedFiles, 3, "数式を書いている計算書のファイル");

            Assert.AreEqual(0, unknown.Count,
                "変換表に無い TeX コマンドがあります (数式の中に命令の名前が文字として出ます):\n  "
                + string.Join("\n  ", unknown.Select(kv => $"\\{kv.Key} ({string.Join(", ", kv.Value)})")));
        }

        /// <summary>
        /// 空白のコマンドが幅を持つ文字に写っていること。
        /// 文字として出ると数式の中に「quad」と並ぶ。
        /// </summary>
        [TestMethod]
        public void SpacingCommandsMapToRealSpaces()
        {
            string converter = TestSource.Read("Graphics_r1", "Output", "WordDocument.TeX.cs");

            foreach (string cmd in new[] { "quad", "qquad" })
            {
                var m = Regex.Match(converter, "\"" + cmd + "\"\\s*=>\\s*\"([^\"]*)\"");
                Assert.IsTrue(m.Success, $"\\{cmd} が変換表にありません");
                string mapped = Regex.Unescape(m.Groups[1].Value);
                Assert.IsFalse(mapped.Contains(cmd),
                    $"\\{cmd} が文字のまま出ます: {m.Groups[1].Value}");
                Assert.IsTrue(mapped.Length > 0 && mapped.All(char.IsWhiteSpace),
                    $"\\{cmd} が空白文字に写っていません: {m.Groups[1].Value}");
            }
        }

        /// <summary>
        /// 英字でない 1 文字の空白の命令 (\, \: \; \! と "\ ") が空白になり、記号が文字として残らないこと。
        /// 以前は命令の名前を英字だけで読んだので名前が空になり、「,」がそのまま出ていた
        /// (計算書の「D = 300〜1200,mm」「θu = 0.03,rad」。2026-11-28 に発見)。
        /// </summary>
        [TestMethod]
        public void SymbolSpacingCommands_DoNotLeaveTheirCharacter()
        {
            foreach (string tex in new[] { @"300\text{〜}1200\,\mathrm{mm}", @"a\:b", @"a\;b", @"a\!b", @"\min(a,\ b)" })
            {
                string text = PileDesign.Output.WordDocument.Tex(tex).InnerText;
                string withoutCommaInArgs = tex.Contains("min") ? text.Replace("a,", "a") : text;
                Assert.IsFalse(withoutCommaInArgs.Contains(',') || text.Contains(':') || text.Contains(';') || text.Contains('!'),
                    $"{tex} → 「{text}」: 空白の命令の記号が文字として残っています");
            }
            StringAssert.Contains(PileDesign.Output.WordDocument.Tex(@"1200\,\mathrm{mm}").InnerText, "1200 mm");
            // 改行 (\\) は従来どおり文字にしない
            Assert.IsFalse(PileDesign.Output.WordDocument.Tex(@"a \\ b").InnerText.Contains('\\'));
        }

        /// <summary>
        /// エスケープした区切り (\left\{ … \right\}、\left\lbrace) が波括弧の区切りになり、後ろの式を飲み込まないこと。
        /// 以前は \left の直後の「\」を区切りとして読み、\right\} と対応が取れずに括弧の構造が崩れていた
        /// (キャプテンパイルのコンクリートの応力度式。2026-11-28 の指摘)。
        /// </summary>
        [TestMethod]
        public void EscapedDelimiters_BecomeBraces()
        {
            foreach (string tex in new[] { @"6.75\left\{ e^{-x} - e^{-y} \right\}\,\sigma", @"6.75\left\lbrace e^{-x} - e^{-y} \right\rbrace\,\sigma" })
            {
                var math = PileDesign.Output.WordDocument.Tex(tex);
                var d = math.Descendants<DocumentFormat.OpenXml.Math.Delimiter>().Single();
                var pr = d.GetFirstChild<DocumentFormat.OpenXml.Math.DelimiterProperties>()!;
                Assert.AreEqual("{", pr.BeginChar?.Val?.Value, tex);
                Assert.AreEqual("}", pr.EndChar?.Val?.Value, tex);
                StringAssert.Contains(d.InnerText, "e", "括弧の中身がありません");
                Assert.IsFalse(d.InnerText.Contains('σ'), "括弧の後ろの σ を括弧の中に飲み込んでいます: " + d.InnerText);
                StringAssert.EndsWith(math.InnerText, "σ", "括弧の後ろが落ちています");
                Assert.IsFalse(math.InnerText.Contains('\\') || math.InnerText.Contains("brace"), "命令が文字として残っています: " + math.InnerText);
            }
        }

        /// <summary>
        /// \begin{cases} が、左を { で囲んだ行ごとの数式配列になること。以前は begin・cases・& が文字のまま出て、
        /// 場合分けを読み分けられなかった (キャプリングの M-θ 関係と引張時の回転剛性。2026-11-28 の指摘)。
        /// </summary>
        [TestMethod]
        public void Cases_BecomeABracedEquationArray()
        {
            var math = PileDesign.Output.WordDocument.Tex(
                @"M = \begin{cases} K\cdot\theta & (0 \le \theta \le \theta_{y}) \\ M_{u} & (\theta_{y} < \theta) \end{cases}, \quad \theta_{y} = \min\left(a,\ b\right)");
            var array = math.Descendants<DocumentFormat.OpenXml.Math.EquationArray>().Single();
            Assert.AreEqual(2, array.Elements<DocumentFormat.OpenXml.Math.Base>().Count(), "場合分けの行の数");
            var brace = math.Descendants<DocumentFormat.OpenXml.Math.Delimiter>()
                .First(d => d.Descendants<DocumentFormat.OpenXml.Math.EquationArray>().Any());
            Assert.AreEqual("{", brace.GetFirstChild<DocumentFormat.OpenXml.Math.DelimiterProperties>()!.BeginChar?.Val?.Value);
            string text = math.InnerText;
            foreach (string leaked in new[] { "begin", "end", "cases", "&", "\\" })
                Assert.IsFalse(text.Contains(leaked, StringComparison.Ordinal), $"「{leaked}」が文字として残っています: {text}");
            StringAssert.Contains(text, "θy= min", "場合分けの後ろの式が落ちています");

            CollectionAssert.AreEqual(new[] { "a & b", @"c\{d\}" }, PileDesign.Output.WordDocument.SplitTeXRows(@" a & b \\ c\{d\} ").ToArray(),
                "行の区切りは波括弧の外の \\\\ だけ");
        }
    }
}
