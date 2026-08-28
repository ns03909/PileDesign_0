using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// N-M / Q-N 図の 1 点は、<b>同じ要素の</b> M（Q）と N から作ること。
    ///
    /// 区間に複数の要素があるとき、M は区間内の最大値を取る。ところが軸力は
    /// 最大とは無関係に「最後に見た要素の値」で上書きしていたため、
    /// 3 本目の要素の M と 7 本目の要素の N が 1 つの点になりえた。
    /// 断面の検討として組にならない。
    ///
    /// 不変条件: 軸力を取る行の直前で Math.Max による更新をしていないこと。
    /// Math.Max だと「更新したかどうか」が分からず、軸力を対応付けられない。
    /// </summary>
    [TestClass]
    public class AxialForcePairingTests
    {
        private static readonly string[] Files =
        [
            Path.Combine("Graphics_r1", "ViewModels", "GraphViewModel.cs"),
            Path.Combine("Graphics_r1", "Output", "WordDocument.Charts.cs"),
        ];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(AxialForcePairingTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void AxialForce_IsTakenFromTheElementThatGoverns()
        {
            string root = FindSolutionRoot();
            var violations = new List<string>();
            int checkedSites = 0;

            foreach (string relative in Files)
            {
                string[] lines = File.ReadAllLines(Path.Combine(root, relative));

                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("analysisFxi =", StringComparison.Ordinal)) continue;
                    // 宣言 (double analysisFxi = 0;) は対象外
                    if (lines[i].Contains("double analysisFxi", StringComparison.Ordinal)) continue;
                    checkedSites++;

                    // 直前 6 行に Math.Max があれば、最大の更新と軸力の取得が別々になっている
                    for (int j = Math.Max(0, i - 6); j < i; j++)
                    {
                        if (lines[j].Contains("Math.Max(", StringComparison.Ordinal))
                        {
                            violations.Add($"{relative}:{i + 1} — 直前 ({j + 1} 行目) で Math.Max により更新している");
                            break;
                        }
                    }
                }
            }

            Assert.IsTrue(checkedSites >= 4,
                $"軸力を取る箇所が {checkedSites} 件しか見つかりません (NMINT / QNINT / 杭頭 / 計算書 で 4 件以上のはず)");
            Assert.AreEqual(0, violations.Count,
                "M（Q）と軸力が別の要素から来ています:\n" + string.Join("\n", violations));
        }
    }
}
