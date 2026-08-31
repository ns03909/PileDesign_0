using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 計算書の図表番号と見出しの規約。
    ///
    /// 計算書には「図目次」と「表目次」を出している。番号 (キャプション) を付け忘れた
    /// 図表はそこに載らず、本文からも「表 3.2 参照」と書けない。
    /// 表は長く 9 件しか番号が付いておらず、表目次がほぼ空だった。
    /// </summary>
    [TestClass]
    public class DocxCaptionTests
    {
        private static string OutputDir()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DocxCaptionTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Graphics_r1", "Output");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("Graphics_r1/Output が見つかりません");
        }

        private static IEnumerable<(string File, string[] Lines)> SourceFiles()
            => Directory.EnumerateFiles(OutputDir(), "*.cs", SearchOption.AllDirectories)
                        .Select(f => (Path.GetFileName(f), File.ReadAllLines(f)));

        private static bool IsLiveCode(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal);

        /// <summary>
        /// 表目次を出す以上、表題を付ける箇所がまとまった数あること。
        /// 図に比べて極端に少ないと、表目次がほぼ空のまま出る。
        /// </summary>
        [TestMethod]
        public void TablesAreNumbered()
        {
            int tableCaptions = 0, figureCaptions = 0;
            foreach (var (_, lines) in SourceFiles())
            {
                foreach (string line in lines.Where(IsLiveCode))
                {
                    if (line.Contains("AddTableCaption(", StringComparison.Ordinal)
                        && !line.Contains("void AddTableCaption", StringComparison.Ordinal))
                        tableCaptions++;
                    else if (Regex.IsMatch(line, @"AddAutoFigureCaption\(.*""表"""))
                        tableCaptions++;
                    else if (Regex.IsMatch(line, @"AddAutoFigureCaption\(.*""図"""))
                        figureCaptions++;
                }
            }

            Assert.IsTrue(figureCaptions >= 15, $"図番号が {figureCaptions} 件しかありません");
            Assert.IsTrue(tableCaptions >= 25,
                $"表番号が {tableCaptions} 件しかありません (図は {figureCaptions} 件)。"
                + "表目次を出しているので、主要な表には表題を付けること。");
        }

        /// <summary>
        /// 見出しレベルを省略しないこと。既定 (0→1) に頼ると、章にしたいのか
        /// 節にしたいのかがコードから読めず、番号付けの誤りに気づけない。
        /// </summary>
        [TestMethod]
        public void HeadingLevelsAreExplicit()
        {
            var violations = new List<string>();
            foreach (var (file, lines) in SourceFiles())
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!IsLiveCode(line)) continue;
                    if (!line.Contains("AddHeader1(", StringComparison.Ordinal)) continue;
                    if (line.Contains("void AddHeader1", StringComparison.Ordinal)) continue;
                    // 3 引数以上あること (body, title, level, ...)
                    if (!Regex.IsMatch(line, @"AddHeader1\([^,]+,[^,]+,[^)]+\)"))
                        violations.Add($"{file}:{i + 1}  {line.Trim()}");
                }
            }

            Assert.AreEqual(0, violations.Count,
                "AddHeader1 の見出しレベルが省略されています:\n  " + string.Join("\n  ", violations));
        }

        /// <summary>
        /// 目次に同じ名前の見出しが 2 つ並ばないこと。
        /// どちらがデータでどちらが算定式か読み分けられなくなる。
        /// </summary>
        [TestMethod]
        public void HeadingTitlesAreUnique()
        {
            var titles = new Dictionary<string, List<string>>();
            foreach (var (file, lines) in SourceFiles())
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!IsLiveCode(lines[i])) continue;
                    var m = Regex.Match(lines[i], @"AddHeader[123]\(body, ""(?<t>[^""$]+)""");
                    if (!m.Success) continue;
                    string t = m.Groups["t"].Value;
                    if (!titles.TryGetValue(t, out var where)) titles[t] = where = [];
                    where.Add($"{file}:{i + 1}");
                }
            }

            var dup = titles.Where(kv => kv.Value.Count > 1)
                            .Select(kv => $"「{kv.Key}」 … {string.Join(", ", kv.Value)}")
                            .ToList();

            Assert.AreEqual(0, dup.Count,
                "同じ名前の見出しが複数あります:\n  " + string.Join("\n  ", dup));
        }
    }
}
