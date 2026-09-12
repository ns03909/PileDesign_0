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
    }
}
