using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプが参照している画像が実在すること。
    ///
    /// 参照先が無いと、その場所には<b>壊れた画像のアイコンが出るだけ</b>で、
    /// ブラウザはエラーを出しません。ヘルプを開いてそこまでスクロールした人だけが
    /// 気づきます。実際に 3 件ありました。
    ///
    /// <list type="bullet">
    /// <item><c>images/</c> の付け忘れ（<c>解説図2.2a.png</c>）</item>
    /// <item>ファイル名の食い違い（<c>解説図1.1r1.png</c> / 実物は <c>解説図1.1_r1.png</c>）</item>
    /// <item>拡張子の食い違い（<c>icons/Display.png</c> / 実物は <c>.svg</c>）</item>
    /// </list>
    ///
    /// <b>コメントアウトされた <c>&lt;img&gt;</c> も見ます。</b> 表示はされませんが、
    /// コメントを外したときに壊れます。実際に 9 件が <c>images/</c> 抜きのまま残っていました。
    /// </summary>
    [TestClass]
    public class HelpAssetTests
    {
        [TestMethod]
        public void EveryHelpImage_Exists()
        {
            var dir = Path.Combine(TestSource.Root(), "Graphics_r1", "Help");
            var text = File.ReadAllText(Path.Combine(dir, "help.html"));

            // コメントの中も見る。表示はされないが、コメントを外したときに壊れる。
            // 実際に 9 件が images/ 抜きのまま残っていた。
            var missing = new List<string>();
            int scanned = 0;

            foreach (Match m in Regex.Matches(text, @"src=""([^""]+)"""))
            {
                string src = m.Groups[1].Value;
                if (src.StartsWith("http", StringComparison.Ordinal)
                    || src.StartsWith("data:", StringComparison.Ordinal)
                    || src.StartsWith("pack:", StringComparison.Ordinal)) continue;

                scanned++;
                string path = Path.Combine(dir, src.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) missing.Add(src);
            }

            TestSource.AssertScanned(scanned, 200, "ヘルプが参照する画像");
            Assert.AreEqual(0, missing.Distinct().Count(),
                "ヘルプが参照している画像がありません。壊れた画像のアイコンが出るだけで、"
                + "エラーにはなりません:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing.Distinct()));
        }

        /// <summary>
        /// ヘルプの中のリンクが、実在する見出しを指していること。
        /// <c>DeadBindingTests</c> も同じことを見ているが、あちらは画面側の
        /// アンカーが主なので、ヘルプ内で完結するリンクもここで見る。
        /// </summary>
        [TestMethod]
        public void EveryInternalLink_PointsSomewhere()
        {
            var text = File.ReadAllText(
                Path.Combine(TestSource.Root(), "Graphics_r1", "Help", "help.html"));
            string visible = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
            // <script> の中はコード。href="#..." のような文字列が混ざる
            visible = Regex.Replace(visible, @"<script\b.*?</script>", "", RegexOptions.Singleline);

            var ids = Regex.Matches(visible, @"\bid=""([^""]+)""")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            var dead = Regex.Matches(visible, @"href=""#([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .Where(a => !ids.Contains(a))
                .Distinct()
                .ToList();

            Assert.IsTrue(ids.Count >= 200, $"見出しの id が {ids.Count} 個しかありません");
            Assert.AreEqual(0, dead.Count,
                "ヘルプ内のリンクが、無い見出しを指しています。押しても何も起きません:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", dead));
        }
    }
}
