using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 参考文献の一覧が<b>1 か所</b>で決まり、ヘルプと計算書で食い違わないこと。
    ///
    /// <para>計算書 (docx) の「参考文献」表は <see cref="ReferenceCatalog"/> から作る。
    /// ヘルプの「準拠指針・参考文献」は表紙画像を並べたタイルで、こちらは HTML に直接書いてある。
    /// 同じ一覧を 2 か所に手で持つと片方だけ増えて取り残される (このリポジトリで繰り返している形)
    /// ので、書名で突き合わせる (2026-09-12)。</para>
    /// </summary>
    [TestClass]
    public class ReferenceCatalogTests
    {
        /// <summary>ヘルプのタイルに出ている文献は、カタログにもあること。</summary>
        [TestMethod]
        public void TheHelpTilesAndTheCatalogAgree()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            int at = help.IndexOf("id=\"h-準拠指針・参考文献\"", System.StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "ヘルプの「準拠指針・参考文献」の節が見つかりません");
            int end = help.IndexOf("</div>\n\n", at, System.StringComparison.Ordinal);
            string tiles = end > at ? help[at..end] : help[at..];

            // タイルに出ている書名 (カタログ側の書名で探す)。ヘルプは全角空白や改行を挟むので、
            // 見出しになる語で照合する
            var expectedInHelp = new (string Catalog, string HelpKeyword)[]
            {
                ("建築基礎構造設計指針", "建築基礎構造設計指針"),
                ("基礎部材の強度と変形性能", "基礎部材の強度と変形性能"),
                ("建築基礎構造設計例集", "建築基礎構造設計例集"),
                ("基礎構造の設計　学びやすい構造設計", "学びやすい構造設計"),
                ("キャプテンパイル工法 (場所打ち杭用杭頭半固定構法) 設計・施工マニュアル", "キャプテンパイル工法"),
                ("F.T.Pile構法既製コンクリート杭　設計・施工指針【暫定版】", "F.T.Pile構法"),
                ("キャプリングパイル工法 設計マニュアル", "キャプリングパイル工法"),
            };

            var catalogTitles = ReferenceCatalog.All().Select(r => r.Title).ToList();
            var missing = new List<string>();

            foreach (var (catalog, keyword) in expectedInHelp)
            {
                if (!tiles.Contains(keyword, System.StringComparison.Ordinal))
                    missing.Add($"ヘルプのタイルに「{keyword}」がありません");
                if (!catalogTitles.Contains(catalog))
                    missing.Add($"カタログに「{catalog}」がありません");
            }

            Assert.AreEqual(0, missing.Count,
                "ヘルプと計算書の参考文献が食い違っています:\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// どの文献にも、発行者・書名・版・用いどころが埋まっていること。
        /// 「用いどころ」が空だと、読み手はその文献が何に効いているのか辿れない。
        /// </summary>
        [TestMethod]
        public void EveryReferenceIsComplete()
        {
            var all = ReferenceCatalog.All().ToList();
            TestSource.AssertScanned(all.Count, 7, "参考文献");

            foreach (var r in all)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Publisher), $"{r.Title}: 発行者が空です");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Title), "書名が空の文献があります");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Edition), $"{r.Title}: 版が空です");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.UsedFor), $"{r.Title}: 用いどころが空です");
            }

            var duplicated = all.GroupBy(r => r.Title).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.AreEqual(0, duplicated.Count,
                "同じ書名が 2 度入っています: " + string.Join(" / ", duplicated));
        }

        /// <summary>
        /// 常に載せるのは指針と法令だけで、工法の文献は使っているときだけであること。
        /// 使っていない工法のマニュアルを並べると、何に依った計算書なのかが読めなくなる。
        /// </summary>
        [TestMethod]
        public void MethodReferencesAreNotAlwaysListed()
        {
            var always = ReferenceCatalog.Always().ToList();
            Assert.IsTrue(always.Count >= 6, "常に載せる文献が少なすぎます");
            Assert.IsFalse(always.Any(r => r.Kind == ReferenceKind.Method),
                "工法の文献が常時掲載に混ざっています");

            Assert.IsTrue(ReferenceCatalog.Methods.Count >= 4, "工法の文献が足りません");
            Assert.IsTrue(ReferenceCatalog.Methods.Values.All(r => r.Kind == ReferenceKind.Method),
                "工法の一覧に分類違いが混ざっています");
        }
    }
}
