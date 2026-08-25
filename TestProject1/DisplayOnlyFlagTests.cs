using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 表示専用のフラグを FEM のコードが見ていないこと。
    ///
    /// <c>IsVisible</c> は画面の表示・非表示を切り替えるだけのフラグで、
    /// <b>解析モデルの中身を変えてはいけない</b> (README「暗黙の前提」)。
    /// これを解析側で参照すると「非表示にした節点が解析モデルから消える」、
    /// つまり<b>画面の見た目で計算結果が変わる</b>。
    /// ビルドもテストも通ってしまい、結果を見ても気付けない種類の不具合なので、
    /// ソースを機械的に検査する。
    ///
    /// 実際に <c>AnalysisModelling.AddInputNodes</c> が
    /// <c>Type == General &amp;&amp; inputNode.IsVisible</c> で絞っており、
    /// 表示 OFF の一般節点が FEM 節点・MGT 出力から消えていた。
    /// </summary>
    [TestClass]
    public class DisplayOnlyFlagTests
    {
        /// <summary>
        /// 解析モデルを組み立てる領域。ここでの表示フラグ参照を禁じる。
        ///
        /// <c>Services</c> は対象外。<c>PileLayoutService</c> のように
        /// 「選択した杭を削除する」といった<b>編集操作</b>を担うものがあり、
        /// そこで <c>IsSelected</c> を見るのは正しい。
        /// </summary>
        private static readonly string[] AnalysisDirectories = ["FEM"];

        /// <summary>
        /// 表示専用のフラグ。ここに足したものは解析系から参照できなくなる。
        ///
        /// <c>IsSelected</c> は入れない。編集の対象を表すフラグで、
        /// 解析系が見ないのは <c>IsVisible</c> と同じだが、
        /// 「選択中のものを操作する」正当な用途と機械的に区別できない。
        /// </summary>
        private static readonly string[] DisplayOnlyFlags = ["IsVisible"];

        /// <summary>
        /// グラフ描画ライブラリ (ScottPlot) のプロパティは同名だが別物。
        /// <c>plot.Legend.IsVisible</c> などは描画の話なので対象外。
        /// </summary>
        private static readonly string[] PlottingReceivers =
            ["plot.", "Legend.", "MarkerStyle.", "LineStyle.", "Axis.", "Label."];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DisplayOnlyFlagTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void AnalysisCode_DoesNotReadDisplayOnlyFlags()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            var violations = new List<string>();

            foreach (string subDir in AnalysisDirectories)
            {
                string dir = Path.Combine(root, subDir);
                if (!Directory.Exists(dir)) continue;

                foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];

                        // コメントは対象外 (「IsVisible で絞らないこと」と書けるように)
                        string code = StripComment(line);
                        if (PlottingReceivers.Any(code.Contains)) continue;

                        foreach (string flag in DisplayOnlyFlags)
                        {
                            if (!Regex.IsMatch(code, $@"\.{flag}\b")) continue;

                            violations.Add(
                                $"{subDir}/{Path.GetFileName(file)}:{i + 1}  {code.Trim()}");
                        }
                    }
                }
            }

            Assert.AreEqual(0, violations.Count,
                "解析系のコードが表示専用フラグを見ている " +
                "(画面の見た目で計算結果が変わる):\n  " + string.Join("\n  ", violations));
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
