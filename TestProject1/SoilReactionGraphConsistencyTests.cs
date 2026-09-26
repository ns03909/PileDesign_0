using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 水平地盤反力の深度分布・p-y 曲線のグラフが、解析と同じ条件の値を描くこと。
    ///
    /// 2026-09-26 のレビューで 3 件:
    /// - ばねごとに「そのばねにある最新ステップ」を拾っていたので、一部が欠けると別ステップの値が 1 本に混ざる
    /// - 分布表示で結果の無い節点が初期値 0 のまま反力度の計算に入り、反力ゼロとして描かれる
    /// - p-y 曲線とマーカーの前方杭・後方杭が常に 1 番目の荷重ケースの判定だった (解析はケースごと)
    /// </summary>
    [TestClass]
    public class SoilReactionGraphConsistencyTests
    {
        private static LoadCase Case(int no) => new() { Level = 1, No = no };

        [TestMethod]
        public void OnlyTheFinalStepIsUsed()
        {
            var lc = Case(1);
            var cmb = new LoadCombination(1, 1.0, 1.0, 1.0);
            var spring = new HorizontalSoilSpring();
            spring.HorizontalSpringResults.Add(new HorizontalSpringResult { LoadCase = lc, LoadCombination = cmb, Step = 3 });
            spring.HorizontalSpringResults.Add(new HorizontalSpringResult { LoadCase = lc, LoadCombination = cmb, Step = 5 });

            Assert.AreEqual(5, GraphViewModel.FinalSpringResult(spring, lc, cmb, false, 5)?.Step);
            Assert.IsNull(GraphViewModel.FinalSpringResult(spring, lc, cmb, false, 7),
                "最終ステップの結果が無いばねで、手前のステップの値を拾っています");
            Assert.IsNull(GraphViewModel.FinalSpringResult(spring, lc, cmb, true, 5), "液状化の別を見ていません");
            Assert.IsNull(GraphViewModel.FinalSpringResult(spring, Case(2), cmb, false, 5), "荷重ケースを見ていません");
            Assert.IsNull(GraphViewModel.FinalSpringResult(null, lc, cmb, false, 5));
        }

        /// <summary>要素 3 つ・節点 4 つで、節点 2 の結果が欠けたとき、その節点が受け持つ半区間を描かない。</summary>
        [TestMethod]
        public void AMissingNodeLeavesAGapInsteadOfZero()
        {
            var segments = new List<(double ZTop, double ZBtm)> { (0, -1), (-1, -2), (-2, -3) };
            bool[] hasResult = { true, true, false, true };
            var points = GraphViewModel.RectangleDistributionPoints(segments, hasResult,
                j => 10.0 + j, j => 20.0 + j);

            var drawn = points.Where(p => p != null).Select(p => p!.Value).ToList();
            Assert.IsFalse(drawn.Any(p => p.V == 0), "欠けた節点の値がゼロとして描かれています");
            // 要素 1 の下半分 (-1.5〜-2) と要素 2 の上半分 (-2〜-2.5) が抜ける
            Assert.IsFalse(drawn.Any(p => p.Z < -1.5 && p.Z > -2.5), "欠けた節点の区間に点があります");

            var runs = GraphViewModel.SplitAtGaps(points);
            Assert.AreEqual(2, runs.Count, "欠けた区間で線が切れていません");
            CollectionAssert.AreEqual(new[] { 0.0, -0.5, -0.5, -1.0, -1.0, -1.5 }, runs[0].Zs);
            CollectionAssert.AreEqual(new[] { -2.5, -3.0 }, runs[1].Zs);
            CollectionAssert.AreEqual(new[] { 22.0, 22.0 }, runs[1].Values, "要素 2 の下半分 (節点 3 の値) で描きます");
        }

        [TestMethod]
        public void AllNodesPresentGiveOneLine()
        {
            var segments = new List<(double ZTop, double ZBtm)> { (0, -1), (-1, -2) };
            var points = GraphViewModel.RectangleDistributionPoints(segments, new[] { true, true, true }, j => 1, j => 2);
            Assert.IsTrue(points.All(p => p != null));
            Assert.AreEqual(1, GraphViewModel.SplitAtGaps(points).Count);
            Assert.AreEqual(0, GraphViewModel.SplitAtGaps(new (double, double)?[] { null, null }).Count);
        }

        [TestMethod]
        public void TheMissingMessageSaysItIsNotZero()
        {
            var series = Enumerable.Range(1, 7).Select(i => $"杭 #{i}").ToList();
            string msg = GraphViewModel.DescribePartiallyMissingSpringResults(series);
            StringAssert.Contains(msg, "ゼロという意味ではありません");
            StringAssert.Contains(msg, "杭 #5");
            Assert.IsFalse(msg.Contains("杭 #6"));
            StringAssert.Contains(msg, "ほか 2 件");
        }

        [TestMethod]
        public void FrontOrRearFollowsTheLoadCase()
        {
            var pile = new PileLayoutDataItem { IsFrontPiles = new ObservableCollection<bool> { true, false } };
            Assert.IsTrue(pile.IsFrontFor(Case(1)));
            Assert.IsFalse(pile.IsFrontFor(Case(2)), "2 番目のケースで 1 番目の判定を使っています");
            Assert.IsFalse(pile.IsFrontFor(Case(3)), "範囲外は解析と同じく後方杭");
            Assert.IsFalse(pile.IsFrontAt(-1), "鉛直ケース (-1) は後方杭");
            Assert.IsFalse(new PileLayoutDataItem { IsFrontPiles = null! }.IsFrontFor(Case(1)));
        }

        /// <summary>
        /// 解析・グラフで前後判定を自前で引かず、同じ関数を通していること
        /// (入力の編集で IsFrontPiles に書き込む画面側は対象外)。
        /// </summary>
        [TestMethod]
        public void NobodyReadsTheFrontFlagsByHand()
        {
            string vmDir = TestSource.Dir("Graphics_r1", "ViewModels");
            var files = Directory.GetFiles(vmDir, "HorizontalCalculationViewModel*.cs")
                .Concat(Directory.GetFiles(vmDir, "GraphViewModel*.cs")).ToArray();
            TestSource.AssertScanned(files.Length, 8, "水平解析とグラフのソース");
            var offenders = files
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"IsFrontPiles\s*(\?\.|\[|\.FirstOrDefault|!=\s*null)"))
                .Select(Path.GetFileName).ToList();
            Assert.AreEqual(0, offenders.Count,
                "前方杭の判定を IsFrontPiles から直接引いています (IsFrontFor / IsFrontAt を使う): " + string.Join(", ", offenders));
        }

        /// <summary>グラフがばねの結果を「最新のもの」で拾っていないこと。</summary>
        [TestMethod]
        public void TheGraphsDoNotPickTheLatestAvailableStep()
        {
            foreach (var name in new[] { "GraphViewModel.ElevationGraphs.cs", "GraphViewModel.CurveGraphs.cs" })
            {
                string src = TestSource.Read("Graphics_r1", "ViewModels", name);
                StringAssert.Contains(src, "FinalSpringResult(", name);
                Assert.IsFalse(Regex.IsMatch(src, @"HorizontalSpringResults\?[\s\S]{0,400}?OrderByDescending\(r => r\.Step\)"),
                    $"{name}: ばねの結果をそのばねにある最新ステップで拾っています (最終ステップに限る)");
            }
        }
    }
}
