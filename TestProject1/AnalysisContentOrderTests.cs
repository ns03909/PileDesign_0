using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析結果コンテンツの候補を並べ替える処理が、<b>古い並び順を渡されても落ちない</b>こと。
    ///
    /// この並べ替えは <c>CollectionChanged</c> から <c>BeginInvoke</c> で遅らせて呼ばれる。
    /// 並び順を決めてから当てはめるまでの間に、別のところが候補を足したり消したりしうる。
    /// 以前は <c>IndexOf</c> が −1 を返した項目に対して <c>Move(-1, i)</c> を呼び、
    /// <c>ArgumentOutOfRangeException</c> になっていた。
    ///
    /// 投げ放しの処理なので誰も受け取らず、<b>テストホストごと落ちる</b>。
    /// 実際にテストの全体実行が 1 日で 3 回止まった（1,342 件・1,336 件・1,308 件）。
    /// <c>dotnet test</c> はそれでも「成功!」と表示するので、件数を見るまで気づけなかった。
    /// 実機でも、解析が終わって候補が入れ替わる瞬間に同じことが起こりうる。
    ///
    /// 本物のすれ違いは時機次第で再現しないので、<b>古い並び順を渡して同じ状況を作る</b>。
    /// </summary>
    [TestClass]
    public class AnalysisContentOrderTests
    {
        private static ObservableCollection<string> Live(params string[] items) => new(items);

        /// <summary>決められた順に並べ替えること。</summary>
        [TestMethod]
        public void TheReorder_SortsIntoTheDesiredOrder()
        {
            var live = Live("沈下応力", "杭頭Mマップ", "梁応力（水平）", "沈下量");
            var desired = new[] { "梁応力（水平）", "杭頭Mマップ", "沈下量", "沈下応力" };

            MainWindowViewModel.ApplyContentOrder(live, desired);

            CollectionAssert.AreEqual(desired, live.ToArray(),
                "並べ替えられていません。実際: " + string.Join(" / ", live));
        }

        /// <summary>
        /// <b>並び順に、もう無い項目が混ざっていても落ちないこと。</b>
        /// 並び順を決めたあとに候補が消えた場合がこれ。
        /// </summary>
        [TestMethod]
        public void TheReorder_SkipsItemsThatVanished()
        {
            var live = Live("沈下量", "梁応力（水平）");
            var desired = new[] { "梁応力（水平）", "節点変位（水平）", "沈下量" };  // 真ん中はもう無い

            MainWindowViewModel.ApplyContentOrder(live, desired);

            CollectionAssert.AreEqual(new[] { "梁応力（水平）", "沈下量" }, live.ToArray(),
                "残っている項目だけで並べ替えるはず。実際: " + string.Join(" / ", live));
        }

        /// <summary>並び順のほうが長くても落ちないこと。</summary>
        [TestMethod]
        public void TheReorder_SurvivesAShorterCollection()
        {
            var live = Live("梁応力（水平）");
            var desired = new[] { "梁応力（水平）", "杭頭Mマップ", "沈下量", "沈下応力" };

            MainWindowViewModel.ApplyContentOrder(live, desired);

            CollectionAssert.AreEqual(new[] { "梁応力（水平）" }, live.ToArray());
        }

        /// <summary>空でも、並び順が空でも落ちないこと。</summary>
        [TestMethod]
        public void TheReorder_SurvivesEmptyInputs()
        {
            var empty = Live();
            MainWindowViewModel.ApplyContentOrder(empty, new[] { "沈下量" });
            Assert.AreEqual(0, empty.Count);

            var live = Live("沈下量", "梁応力（水平）");
            MainWindowViewModel.ApplyContentOrder(live, Array.Empty<string>());
            Assert.AreEqual(2, live.Count, "並び順が空なら触らないはず");
        }

        /// <summary>
        /// 並び順に無い項目が混ざっていても落ちず、<b>並べ替えは中断しない</b>こと。
        ///
        /// 飛ばした分だけ以降の位置がずれるので、1 回では完全に整いません。
        /// それでよく、次の変更でまた呼ばれて仕上がります。
        /// ここで見たいのは「落ちないこと」と「途中で投げ出さないこと」です。
        /// </summary>
        [TestMethod]
        public void TheReorder_KeepsGoingPastMissingItems()
        {
            var live = Live("沈下応力", "沈下量", "杭頭Mマップ", "梁応力（水平）", "節点変位（水平）");
            var desired = new[]
            {
                "梁応力（水平）", "節点変位（水平）", "知らない項目",
                "杭頭Mマップ", "沈下量", "消えた項目", "沈下応力",
            };

            int notifications = 0;
            live.CollectionChanged += (_, _) => notifications++;

            MainWindowViewModel.ApplyContentOrder(live, desired);

            Assert.IsTrue(notifications > 0, "並べ替えが 1 度も起きていません");
            Assert.AreEqual(5, live.Count, "項目が増減しています");
            CollectionAssert.AreEquivalent(
                new[] { "沈下応力", "沈下量", "杭頭Mマップ", "梁応力（水平）", "節点変位（水平）" },
                live.ToArray(), "中身が変わっています");

            // 先頭 2 つは、無い項目に当たる前なので確実に整う
            Assert.AreEqual("梁応力（水平）", live[0]);
            Assert.AreEqual("節点変位（水平）", live[1]);
        }

        /// <summary>
        /// 2 度呼べば整うこと。1 回で整わなくても、次の変更で仕上がる前提が成り立つこと。
        /// </summary>
        [TestMethod]
        public void TheReorder_SettlesWhenCalledAgain()
        {
            var live = Live("沈下応力", "沈下量", "杭頭Mマップ", "梁応力（水平）", "節点変位（水平）");
            var canonical = new[] { "梁応力（水平）", "節点変位（水平）", "杭頭Mマップ", "沈下量", "沈下応力" };

            for (int pass = 0; pass < 3; pass++)
            {
                var desired = live.OrderBy(x => Array.IndexOf(canonical, x)).ToList();
                MainWindowViewModel.ApplyContentOrder(live, desired);
            }

            CollectionAssert.AreEqual(canonical, live.ToArray(),
                "繰り返しても整いません。実際: " + string.Join(" / ", live));
        }
    }
}
