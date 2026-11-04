using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Output;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 計算書の杭の応力・変位図は、解析結果の無い要素・節点を 0 として描かず、線を切って題名に断ること (2026-11-04 外部レビュー)。
    /// 以前は結果が取れない要素の曲げ・せん断・変位を 0.0 として足しており、一部の結果が欠けたデータでは、
    /// 欠けた位置が応力・変位 0 の位置に読めた。
    /// </summary>
    [TestClass]
    public class ReportChartMissingResultsTests
    {
        [TestMethod]
        public void LinesAreCutAtMissingValues()
        {
            var plot = new ScottPlot.Plot();
            var runs = WordDocument.AddFiniteRuns(plot, [1, 2, double.NaN, 4, 5], [0, -1, -2, -3, -4]);
            Assert.AreEqual(2, runs.Count, "結果の無い点で線を切っていません (0 として描くか、またいで結んでいる)");
            Assert.AreEqual(runs[0].LineStyle.Color, runs[1].LineStyle.Color, "同じ系列の線の色がそろっていません");

            Assert.AreEqual(1, WordDocument.AddFiniteRuns(new ScottPlot.Plot(), [1, 2, 3], [0, -1, -2]).Count,
                "結果がそろっているのに線が 1 本ではありません (以前と同じ図にならない)");
            Assert.AreEqual(0, WordDocument.AddFiniteRuns(new ScottPlot.Plot(), [double.NaN, double.NaN], [0, -1]).Count);
        }

        [TestMethod]
        public void TheCaptionSaysMissingSectionsAreNotDrawn()
        {
            Assert.AreEqual("", WordDocument.MissingResultsNote(0));
            StringAssert.Contains(WordDocument.MissingResultsNote(3), "描いていません");
        }

        /// <summary>計算書の図の作成で、解析結果の値 (曲げ・せん断・変位) を 0 で埋める書き方が戻っていないこと。</summary>
        [TestMethod]
        public void ReportChartsDoNotFillMissingResultsWithZero()
        {
            var zeroFill = new Regex(@"\.(Mi|Mj|Fi|Fj|Uh)\s*\?\?\s*0(\.0)?\b");
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", "Output"), "WordDocument*.cs"))
            {
                scanned++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (zeroFill.IsMatch(lines[i].Split("//")[0])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
            TestSource.AssertScanned(scanned, 3, "計算書のソース");
            Assert.AreEqual(0, offenders.Count, "解析結果の無い値を 0 で埋めています (NaN にして描かない): " + string.Join(", ", offenders));
        }
    }
}
