using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 結果を出す側が「解析時の入力」を見ていること。
    ///
    /// 解析結果は入力オブジェクトを参照で持つため、解析後に入力を編集すると
    /// 「変位は解析時・断面は編集後」という混在になる。これを避けるために
    /// 解析完了時に入力ごと複製して切り離し、表示系は
    /// <c>MainWindowViewModel.ResultInputModel</c> を見る約束になっている
    /// (<c>AnalysisResultSet</c> の説明を参照)。
    ///
    /// 計算書 (docx) だけがこの約束から漏れ、<c>CurrentInputModel</c> を渡していた。
    /// 画面はスナップショットを見るため、<b>画面と計算書で数値が食い違う</b>状態だった。
    /// 見た目には出ないので、渡す先を機械的に検査する。
    /// </summary>
    [TestClass]
    public class ResultSnapshotConsumerTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ResultSnapshotConsumerTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static IEnumerable<string> ProductSources()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        }

        /// <summary>
        /// 計算書の生成に現在の入力を渡していないこと。
        /// </summary>
        [TestMethod]
        public void WordDocument_IsBuiltFromTheAnalysisSnapshot()
        {
            var construction = new Regex(@"new\s+(?:Output\.)?WordDocument\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);
            var violations = new List<string>();
            int found = 0;

            foreach (string file in ProductSources())
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var m = construction.Match(lines[i]);
                    if (!m.Success) continue;

                    found++;
                    if (m.Groups[1].Value == "CurrentInputModel")
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }

            Assert.AreNotEqual(0, found,
                "WordDocument の生成箇所が見つからない (検査が空振りしている)");
            Assert.AreEqual(0, violations.Count,
                "計算書に現在の入力を渡している (画面と数値が食い違う):\n  "
                + string.Join("\n  ", violations));
        }

        /// <summary>
        /// 計算書の実装が、渡された入力を迂回して現在の入力を読みに行かないこと。
        /// 入口だけ直しても、中で <c>CurrentInputModel</c> を読んでいたら同じことになる。
        /// </summary>
        [TestMethod]
        public void OutputLayer_DoesNotReachForTheLiveInput()
        {
            string outputDir = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output");
            var violations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(outputDir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i]);
                    if (code.Contains("CurrentInputModel"))
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, violations.Count,
                "計算書の実装が現在の入力を直接読んでいる:\n  " + string.Join("\n  ", violations));
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
