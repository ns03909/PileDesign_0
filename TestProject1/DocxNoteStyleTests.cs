using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 表の下に置く注記（「※ …」）は、専用の入口から出すこと。
    ///
    /// 本文と同じ <c>AddText</c> で出すと、本文と同じ書体・大きさになり、
    /// 直前の表への注なのか本文の続きなのか読み分けられない。
    /// <c>AddTableNote</c> は明朝・小さめで組む。
    /// </summary>
    [TestClass]
    public class DocxNoteStyleTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DocxNoteStyleTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void EveryTableNote_UsesTheNoteHelper()
        {
            string dir = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output");
            var violations = new List<string>();
            int notes = 0;

            foreach (string cs in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    // 注記の本文が始まる行を探す（同じ行 / 次の行に書かれる両方の形がある）
                    if (!lines[i].Contains("\"※", StringComparison.Ordinal)) continue;
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                    notes++;
                    string context = string.Join(" ", lines[Math.Max(0, i - 1)..(i + 1)]);
                    if (!context.Contains("AddTableNote(", StringComparison.Ordinal))
                        violations.Add($"{Path.GetFileName(cs)}:{i + 1}  {lines[i].Trim()}");
                }
            }

            Assert.IsTrue(notes >= 5, $"注記が {notes} 件しか見つかりません");
            Assert.AreEqual(0, violations.Count,
                "表下の注記が AddTableNote を通っていません:\n  " + string.Join("\n  ", violations));
        }

        /// <summary>
        /// 役割ごとに書体を分けていること。
        ///
        /// ・本文と見出し … 別書体 (階層が一目で分かる)
        /// ・本文と注記 … 別書体 (本文の続きと読み違えない)
        /// ・表 … <b>等幅</b>。プロポーショナル書体にすると数表の桁が揃わない
        /// </summary>
        [TestMethod]
        public void TheFontsAreSplitByRole()
        {
            string code = File.ReadAllText(
                Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output", "WordDocument.cs"));

            string Face(string name)
            {
                var m = Regex.Match(code, $@"public const string {name} = ""(?<v>[^""]+)""");
                Assert.IsTrue(m.Success, $"{name} が見つかりません");
                return m.Groups["v"].Value;
            }

            string body = Face("FontName");
            string heading = Face("HeadingFontName");
            string table = Face("TableFontName");
            string note = Face("NoteFontName");

            Assert.AreNotEqual(body, heading, "見出しが本文と同じ書体です");
            Assert.AreNotEqual(body, note, "注記が本文と同じ書体です");

            // 等幅であること。和文の等幅は「Ｐ」が付かない方 (ＭＳ Ｐゴシックは
            // プロポーショナル)。ここを取り違えると数表の桁が崩れる
            Assert.IsFalse(table.Contains('Ｐ'), $"表の書体 {table} がプロポーショナルです");
            CollectionAssert.Contains(
                new[] { "ＭＳ ゴシック", "ＭＳ 明朝", "Consolas" }, table,
                $"表の書体 {table} が等幅として確認できません");

            var noteSize = Regex.Match(code, @"public const double NoteFontSize = (?<v>[\d.]+)");
            Assert.IsTrue(noteSize.Success && double.Parse(noteSize.Groups["v"].Value) < 10.5,
                "注記が本文より小さくなっていません");
        }

        /// <summary>
        /// 桁を揃えて読ませる固定行 (検定結果テキスト) が等幅のままであること。
        /// 本文を明朝にしたときに巻き込まれると、そこだけ桁が崩れる。
        /// </summary>
        [TestMethod]
        public void TheFixedPitchReport_KeepsTheMonospaceFace()
        {
            string code = File.ReadAllText(
                Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output", "WordDocument.SummaryTables.cs"));

            StringAssert.Contains(code, "CreateRunFonts(Layout.TableFontName)",
                "検定結果テキストが等幅を指定していません");
        }
    }
}
