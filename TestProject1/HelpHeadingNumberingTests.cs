using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの節番号は、見出しのレベル (h1〜h6) からスクリプト (numberHeadings) が振る。
    /// レベルを飛ばして見出しを置くと、飛ばした段の番号が 0 のまま出る
    /// (章 h2 の直下に h4 を置くと「3.0.1」、h5 を置くと「3.0.0.1」)。
    /// 設計式・計算理論編の杭種の章 (第3〜7章) で 34 か所、利用マニュアルの「杭モデル」で
    /// 23 か所この形になっていた。見出しを足すときに見た目の大きさで h4 や h5 を選びがちなので、
    /// 直前の見出しより 2 段以上深い見出しが無いことを見張る。
    /// </summary>
    [TestClass]
    public class HelpHeadingNumberingTests
    {
        [TestMethod]
        public void HeadingLevelsDoNotSkip()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            // numberHeadings が数えるのは本文 (mainText) の見出しだけ。コメントアウトされた見出しは数えない
            string main = help[help.IndexOf("id=\"mainText\"", System.StringComparison.Ordinal)..];
            main = Regex.Replace(main, "<!--.*?-->", "", RegexOptions.Singleline);

            int previous = 1;
            int scanned = 0;
            var skipped = new List<string>();
            foreach (Match m in Regex.Matches(main, @"<h([1-6])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline))
            {
                int level = int.Parse(m.Groups[1].Value);
                string text = Regex.Replace(m.Groups[2].Value, "<[^>]+>", "").Trim();
                if (level > previous + 1)
                    skipped.Add($"「{text}」(h{previous} の次に h{level})");
                previous = level;
                scanned++;
            }

            TestSource.AssertScanned(scanned, 500, "ヘルプの見出し");
            Assert.AreEqual(0, skipped.Count,
                "見出しのレベルが飛んでいて、節番号に 0 が入ります (例: 3.0.1)。1 段ずつ深くしてください:\n"
                + string.Join("\n", skipped));
        }

        /// <summary>
        /// 振り直しのために前の番号を外すとき、番号の後ろの空白を必須にする。空白を要らないとしていたので、
        /// 版の見出し「1.0.38-beta（2026-10-25）」の「1.0.38」を番号とみなして消し、目次と検索に
        /// 「-beta（2026-10-25）」と出ていた (2026-11-21 に検索の絞り込みを確かめていて発見)。
        /// </summary>
        [TestMethod]
        public void StrippingTheOldNumberKeepsVersionHeadings()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            string body = TestSource.MethodBody(help, "function numberHeadings(");
            var strips = Regex.Matches(body, @"\.replace\(/\^(\\d\+\\\.)+\\d\+\\s(.)/").Cast<Match>().ToList();
            Assert.AreEqual(4, strips.Count, "(前提) 番号を外す正規表現 4 つを読めていない");
            foreach (var m in strips)
                Assert.AreEqual("+", m.Groups[2].Value, $"番号の後ろの空白を必須にしていません: {m.Value}");

            // 版の見出しが本当にある (無くなったらこの検査の意味を見直す)
            StringAssert.Contains(help, ">1.0.38-beta（2026-10-25）</h4>");
        }
    }
}
