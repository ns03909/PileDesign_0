using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 計算書の N-M / Q-N 図は、見出しに書いた杭体・杭区間の結果だけを描くこと。
    ///
    /// 図の見出しは「杭体符号:○○ | 杭区間番号:○」で 1 つの断面を指している。
    /// ところが地震時の散布点は<b>杭体で絞っていなかった</b>ため、区間番号さえ一致すれば
    /// 別の杭体の杭の M-N まで同じ図に乗っていた。区間番号 1 はどの杭体にもあるので、
    /// 杭体が複数あるモデルでは常に混ざる。常時 (VL) だけは元から絞られていた。
    ///
    /// 画面のグラフ (GraphViewModel の MNINT) は杭体で絞っている。計算書だけが違っていた。
    /// </summary>
    [TestClass]
    public class DocxNmChartScopeTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DocxNmChartScopeTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// 杭のループに入ったら、荷重ケースを回す前に杭体で絞っていること。
        /// 絞りが後ろにあると、そのぶんの点が既に集計に入っている。
        /// </summary>
        [TestMethod]
        public void EveryPileLoop_FiltersByPileBodyBeforeCollectingPoints()
        {
            string path = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output", "WordDocument.Charts.cs");
            string[] lines = File.ReadAllLines(path);

            var loopStarts = new System.Collections.Generic.List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"foreach \(var pli in inputModel\.PileLayoutItems\)"))
                    loopStarts.Add(i);
            }

            Assert.IsTrue(loopStarts.Count >= 2,
                $"杭のループが {loopStarts.Count} 個しか見つかりません (N-M と Q-N で 2 個以上のはず)");

            int checkedLoops = 0;
            foreach (int start in loopStarts)
            {
                int end = Math.Min(lines.Length, start + 40);

                // pileBody を持たないループ (全杭の応力図など) は対象外
                bool scopedToPileBody = false;
                for (int i = start; i < end; i++)
                    if (lines[i].Contains("pileBody", StringComparison.Ordinal)) scopedToPileBody = true;
                if (!scopedToPileBody) continue;

                checkedLoops++;

                int guard = -1, firstLoadCase = -1;
                for (int i = start; i < end; i++)
                {
                    if (guard < 0 && lines[i].Contains("!= pileBody", StringComparison.Ordinal))
                    {
                        // 絞り込みは continue で抜けること (フラグを立てるだけでは点が入る)
                        string tail = string.Join(" ", lines[i..Math.Min(lines.Length, i + 3)]);
                        Assert.IsTrue(tail.Contains("continue;", StringComparison.Ordinal),
                            $"{path}:{i + 1} の杭体判定が continue になっていません");
                        guard = i;
                    }
                    if (firstLoadCase < 0 && Regex.IsMatch(lines[i], @"foreach \(var (loadCase|lc) in "))
                        firstLoadCase = i;
                }

                Assert.IsTrue(guard >= 0,
                    $"{path}:{start + 1} の杭ループに杭体の絞り込みがありません");
                Assert.IsTrue(firstLoadCase < 0 || guard < firstLoadCase,
                    $"{path}:{start + 1} の杭ループで、杭体の絞り込みが荷重ケースのループより後ろにあります");
            }

            Assert.IsTrue(checkedLoops >= 3,
                $"検査できた杭ループが {checkedLoops} 個です (N-M / Q-N / M-φ の 3 個以上のはず)");
        }

        /// <summary>
        /// 地震時の散布点の横軸は、N-M 図も Q-N 図も、画面と同じく
        /// 「解析軸力を使う」を反映すること。
        ///
        /// 反映していないと、同じ杭・同じケースなのに図によって軸力が違い、
        /// 画面と計算書、N-M と Q-N を並べて読めない。
        /// </summary>
        [TestMethod]
        public void SeismicScatter_UsesTheAnalysisAxialForceOptionLikeTheScreen()
        {
            string root = FindSolutionRoot();
            string docxPath = Path.Combine(root, "Graphics_r1", "Output", "WordDocument.Charts.cs");
            string docx = File.ReadAllText(docxPath);
            string screen = File.ReadAllText(Path.Combine(root, "Graphics_r1", "ViewModels", "GraphViewModel.cs"));

            // 画面側の式。これが変わったら計算書側も見直す必要がある
            StringAssert.Contains(screen, "UseAnalysisAxialForce",
                "画面の軸力オプションが見つかりません (名前が変わった可能性)");
            Assert.IsTrue(Regex.Matches(screen, Regex.Escape("axialForce - analysisFxi")).Count >= 3,
                "画面で解析軸力を反映していない図があります (NMINT / QNINT / 杭頭)");

            Assert.AreEqual(2, Regex.Matches(docx, Regex.Escape("inputModel.UseAnalysisAxialForce")).Count,
                "計算書で解析軸力を反映していない図があります (N-M と Q-N の 2 つのはず)");

            // 生の axialForce を直接積んでいる箇所が残っていないこと
            foreach (Match m in Regex.Matches(docx, @"axialForceResultsLevel[12]\.Add\(([^)]*)\);"))
            {
                Assert.AreEqual("plotAxialForce", m.Groups[1].Value.Trim(),
                    $"{docxPath}: 地震時の軸力に {m.Groups[1].Value.Trim()} を直接使っています");
            }
        }
    }
}
