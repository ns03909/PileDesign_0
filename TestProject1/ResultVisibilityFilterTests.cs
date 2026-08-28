using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭を一部だけ表示したときの結果ダイアグラム。
    ///
    /// 結果の描画は「表示中の杭に属する要素」の集合を作って絞り込む。
    /// この集合は杭 → FEM 要素の対応付け (<c>PileLayoutDataItem.Beams</c> 等) から作るが、
    /// その対応付けは解析ランタイム状態で、複製や保存往復のたびに張り直す必要がある。
    ///
    /// 張り直しに抜けがあると集合が空になり、<b>表示中の杭のぶんまで含めて結果が
    /// 丸ごと消える</b>。利用者からは「一部だけ表示すると結果が出ない」としか見えない。
    /// </summary>
    [TestClass]
    public class ResultVisibilityFilterTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ResultVisibilityFilterTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        // ── 杭Zばねの対応付け ───────────────────────────────

        /// <summary>
        /// 杭Zばね (P-S 非線形ばね) が、保存の対応表に含まれること。
        ///
        /// 可視セットには杭Zばねも入るので、ここが抜けているとファイルを開き直したとき
        /// 杭Zばねだけ絞り込みから漏れて消える。
        /// </summary>
        [TestMethod]
        public void PileFemLinkTable_CarriesTheVerticalSprings()
        {
            var fields = typeof(PileFemLink).GetProperties().Select(p => p.Name).ToList();

            CollectionAssert.Contains(fields, nameof(PileFemLink.VerticalNodeSpringIndices),
                "杭Zばねの対応付けが保存されない");
            CollectionAssert.Contains(fields, nameof(PileFemLink.HorizontalSoilSpringIndices));
        }

        /// <summary>
        /// 杭 → FEM の対応付けを張り直す 2 つの経路が、同じ項目を扱っていること。
        ///
        /// 片方だけ増やすと、解析直後は出るのにファイルを開き直すと出ない
        /// （あるいはその逆）という差が生まれる。
        /// </summary>
        [TestMethod]
        public void BothRelinkPathsCoverTheSameAssociations()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Models");
            string snapshot = File.ReadAllText(Path.Combine(root, "AnalysisResultSet.cs"));
            string table = File.ReadAllText(Path.Combine(root, "PileFemLinkTable.cs"));

            foreach (string member in new[]
            {
                "PileNodes", "SoilNodes", "Beams",
                "HorizontalSoilSprings", "VerticalNodeSprings", "PileTopRotationalSpring",
            })
            {
                StringAssert.Contains(snapshot, member,
                    $"解析直後の複製で {member} を張り直していない");
                StringAssert.Contains(table, member,
                    $"保存/読込の対応表に {member} が無い");
            }
        }

        // ── 絞り込みを諦める条件 ───────────────────────────

        /// <summary>
        /// 「表示中の杭はあるが対応付けが空」のときは、絞り込みをやめて全体を描くこと。
        ///
        /// ここで絞り込むと、表示中の杭のぶんまで消えて「結果が出ない」ように見える。
        /// 一方「表示中の杭が 0 本」は従来どおり全部消してよい (利用者が全部隠したのだから)。
        /// </summary>
        [TestMethod]
        public void FilteringIsAbandonedWhenTheAssociationsAreEmpty()
        {
            string source = File.ReadAllText(Path.Combine(
                FindSolutionRoot(), "Graphics_r1", "Views", "MainWindow.CanvasResults.cs"));

            int start = source.IndexOf("if (hasInvisiblePile)", StringComparison.Ordinal);
            Assert.IsTrue(start > 0, "可視セットの構築が見つからない");
            string block = source[start..Math.Min(source.Length, start + 2600)];

            StringAssert.Contains(block, "visiblePileCount",
                "表示中の杭が何本あるかを数えていない (0 本と『対応付けが空』を区別できない)");
            StringAssert.Contains(block, "visiblePileCount > 0",
                "表示中の杭があるかどうかで場合分けしていない");
            StringAssert.Contains(block, "Serilog.Log.Warning",
                "絞り込みを諦めたことを記録していない (無言で挙動が変わる)");
        }

        // ── 結果描画が見る入力モデル ───────────────────────

        /// <summary>
        /// 結果の描画は「解析を実行した時点の入力」を見ること。
        ///
        /// 可視セットは解析時スナップショットから作るのに、一部の描画だけ編集中の
        /// 生モデルを見ていた。両者は解析後に別インスタンスになるため、
        /// 参照で突き合わせる箇所が 1 件もヒットせず結果が消える。
        /// </summary>
        [TestMethod]
        public void ResultDrawingUsesTheAnalysisSnapshot()
        {
            string views = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Views");
            var offenders = new List<string>();

            foreach (string cs in Directory.EnumerateFiles(views, "MainWindow.CanvasResults*.cs"))
            {
                var lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? lines[i][..comment] : lines[i];
                    if (Regex.IsMatch(code, @"viewModel\.CurrentInputModel\b"))
                        offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "結果の描画が編集中の入力を見ています (ResultInputModel を使ってください):\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
