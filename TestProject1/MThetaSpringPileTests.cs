using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭頭の回転ばねがどの杭のものかを決めること (<see cref="RotationalSpring.FindPileLayout"/>)。
    ///
    /// M-θ のグラフと計算書は、ばねの名前 → 杭頭の節点 → 杭体番号の順に杭を探していた。最後の杭体番号では
    /// 同じ杭体の<b>最初の杭</b>を採っていたので、同じ杭体を複数の杭が使うモデルでは、曲線に別の杭の番号と軸力が付き得た。
    /// 杭を 1 本に決められなければ描かずに知らせる。
    /// </summary>
    [TestClass]
    public class MThetaSpringPileTests
    {
        private static List<PileLayoutDataItem> Piles(params (int No, int Body)[] piles)
            => piles.Select(p => new PileLayoutDataItem { No = p.No, PileBodyNo = p.Body }).ToList();

        [TestMethod]
        public void ThePileIsFoundByNameThenNodeThenAnOnlyPileOfTheBody()
        {
            var piles = Piles((1, 1), (2, 1), (3, 2));
            var node = new Node();
            piles[1].PileNodes.Add(node);

            Assert.AreSame(piles[2], new RotationalSpring { Name = "RθXY-3" }.FindPileLayout(piles), "名前で杭を決めていません");
            Assert.AreSame(piles[1], new RotationalSpring { NodeJ = node, PileBodyNo = 1 }.FindPileLayout(piles), "杭頭の節点で杭を決めていません");
            Assert.AreSame(piles[2], new RotationalSpring { PileBodyNo = 2 }.FindPileLayout(piles), "杭体を使う杭が 1 本なら、その杭に決まります");
        }

        [TestMethod]
        public void ASharedPileBodyIsNotEnoughToPickAPile()
        {
            var piles = Piles((1, 1), (2, 1), (3, 2));
            Assert.IsNull(new RotationalSpring { PileBodyNo = 1 }.FindPileLayout(piles),
                "同じ杭体を 2 本の杭が使っているのに、どちらかの杭に決めています (別の杭の番号・軸力が付く)");
            Assert.IsNull(new RotationalSpring { Name = "RθXY-9", PileBodyNo = 1 }.FindPileLayout(piles),
                "名前の杭番号が無いとき、同じ杭体の杭に決めています");
        }

        [TestMethod]
        public void UnidentifiedSpringsAreNamedInTheGraph()
        {
            string message = GraphViewModel.DescribeUnidentifiedMThetaSprings(["Rθ-b", "Rθ-a"]);
            StringAssert.Contains(message, "どの杭の杭頭か決められない");
            StringAssert.Contains(message, "Rθ-a, Rθ-b");
        }

        /// <summary>杭の探し方の写し (杭体番号で最初の杭を採る) が、グラフ・計算書に戻っていないこと。</summary>
        [TestMethod]
        public void TheLookupIsNotCopiedBack()
        {
            foreach (var (dir, file) in new[] { ("ViewModels", "GraphViewModel.CurveGraphs.cs"), ("Output", "WordDocument.Charts.cs") })
            {
                string src = TestSource.Read("Graphics_r1", dir, file);
                StringAssert.Contains(src, ".FindPileLayout(", $"{file}: 回転ばねの杭を FindPileLayout で探していません");
                Assert.IsFalse(Regex.IsMatch(src, @"FirstOrDefault\(pl => pl\.PileBodyNo == pb\)"),
                    $"{file}: 杭体番号で最初の杭を採る探し方が残っています");
            }
        }
    }
}
