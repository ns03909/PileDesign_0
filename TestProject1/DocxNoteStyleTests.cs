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

        /// <summary>注記は本文より小さく、書体も本文と変えること。</summary>
        [TestMethod]
        public void TheNoteStyle_IsSmallerAndADifferentFace()
        {
            string code = File.ReadAllText(
                Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output", "WordDocument.cs"));

            var body = Regex.Match(code, @"public const string FontName = ""(?<v>[^""]+)""");
            var noteFace = Regex.Match(code, @"public const string NoteFontName = ""(?<v>[^""]+)""");
            var noteSize = Regex.Match(code, @"public const double NoteFontSize = (?<v>[\d.]+)");

            Assert.IsTrue(body.Success && noteFace.Success && noteSize.Success, "定数が見つかりません");
            Assert.AreNotEqual(body.Groups["v"].Value, noteFace.Groups["v"].Value,
                "注記が本文と同じ書体になっています");
            Assert.IsTrue(double.Parse(noteSize.Groups["v"].Value) < 10.5,
                "注記が本文より小さくなっていません");
        }
    }
}
