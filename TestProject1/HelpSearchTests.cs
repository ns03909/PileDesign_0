using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの検索 (<see cref="HelpSearchService"/>) が、本文だけを索引にし、見出しの一致する節を上に出すこと。
    /// </summary>
    [TestClass]
    public class HelpSearchTests
    {
        /// <summary>
        /// スクリプト・スタイルの中身を本文にしないこと。以前はタグだけを除いていたので、help.html の末尾の
        /// 長い &lt;script&gt; の中身が最後の見出しの本文になり、JavaScript の語で最後の節が検索に出た。
        /// </summary>
        [TestMethod]
        public void ScriptsAndStylesAreNotIndexed()
        {
            const string html = "<h2 id='a'>最初</h2><p>本文A</p><style>.x{color:red}</style>"
                              + "<h3 id='b'>最後</h3><p>本文B</p><!-- 注記 --><script>function hideAll(){ var 検索語 = 1; }</script>";
            var sections = HelpSearchService.ParseSections(html);
            Assert.AreEqual("本文B", sections[^1].PlainText, "スクリプト・コメントの中身が最後の節の本文に入っています");
            Assert.AreEqual("本文A", sections[0].PlainText, "スタイルの中身が本文に入っています");

            var real = HelpSearchService.ParseSections(TestSource.Read("Graphics_r1", "Help", "help.html"));
            Assert.IsTrue(real.Count > 100, "(前提) ヘルプの節を読めていること");
            foreach (var word in new[] { "function", "querySelector", "addEventListener" })
                Assert.IsFalse(real.Any(s => s.PlainText.Contains(word, StringComparison.Ordinal)),
                    $"ヘルプのスクリプトの語「{word}」が本文として索引に入っています");
        }

        /// <summary>
        /// 語を何度も含むだけの長い節が、見出しの一致する短い節より上に来ないこと (本文の点に上限を置いた)。
        /// </summary>
        [TestMethod]
        public void ALongSectionDoesNotOutrankATitleMatch()
        {
            string longBody = string.Concat(Enumerable.Repeat("杭頭固定度の説明を繰り返す。", 40));
            string html = $"<h3 id='long'>長い節</h3><p>{longBody}</p><h3 id='short'>杭頭固定度</h3><p>短い説明。</p>";
            var sections = HelpSearchService.ParseSections(html);

            var results = HelpSearchService.Search(sections, "杭頭固定度", 5, out _);
            Assert.AreEqual("杭頭固定度", results[0].Section.Title, "語を繰り返すだけの長い節が、見出しの一致する節より上に来ています");
        }

        /// <summary>実際のヘルプで検索例を比べる。入力した語をそのまま含む節が上位に来ること。</summary>
        [TestMethod]
        public void RealQueriesPutTheMatchingSectionFirst()
        {
            var sections = HelpSearchService.ParseSections(TestSource.Read("Graphics_r1", "Help", "help.html"));
            foreach (var query in new[] { "自動保存", "液状化判定", "杭頭回転角", "M-θ" })
            {
                var top = HelpSearchService.Search(sections, query, 1, out _).Single();
                StringAssert.Contains(top.Section.Title, query, $"「{query}」の検索で、見出しに含む節が先頭に来ません (先頭: {top.Section.Title})");
            }

            // 見出しに無い語は、2 文字ずつに分けた一部 (「ボード」) だけが一致する節より、語をそのまま含む節を上に
            var dashboard = HelpSearchService.Search(sections, "ダッシュボード", 1, out _).Single();
            StringAssert.Contains(dashboard.Section.PlainText, "ダッシュボード",
                $"「ダッシュボード」の検索で、その語を含まない節が先頭に来ます (先頭: {dashboard.Section.Title})");
        }

        /// <summary>表示件数と総ヒット件数を区別すること。以前は上限で絞った件数を「○件見つかりました」と出した。</summary>
        [TestMethod]
        public void TheShownCountIsNotReportedAsTheTotal()
        {
            Assert.AreEqual("関連する項目が 35 件見つかりました (関連の強い上位 20 件を表示):",
                PileDesign.Views.HelpChatWindow.DescribeResultCount(20, 35));
            Assert.AreEqual("関連する項目が 7 件見つかりました:", PileDesign.Views.HelpChatWindow.DescribeResultCount(7, 7));

            var sections = HelpSearchService.ParseSections(TestSource.Read("Graphics_r1", "Help", "help.html"));
            var shown = HelpSearchService.Search(sections, "杭", 20, out int total);
            Assert.AreEqual(20, shown.Count);
            Assert.IsTrue(total > 20, "(前提) 「杭」は 21 件以上の節に出ること");
        }
    }
}
