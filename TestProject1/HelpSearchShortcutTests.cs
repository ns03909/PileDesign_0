using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの中の Ctrl+F はヘルプ内検索を開き、検索中の Esc は目次へ戻る (利用者の要望 2026-11-08)。
    ///
    /// <para>キーはアプリ側 (HelpWindow) と本文側 (help.html) のどちらに届くかが WebView2 しだいなので、両方で受けて
    /// 同じ関数を呼ぶ。名前を片方だけ変えると、もう片方からは何も起きなくなる (例外にもならない)。
    /// 目次のときの Esc は、これまでどおりヘルプを閉じる (EscapeKeyBehavior)。</para>
    /// </summary>
    [TestClass]
    public class HelpSearchShortcutTests
    {
        private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(TestSource.Root(), Path.Combine(parts)));

        [TestMethod]
        public void TheHelpPageDefinesTheFunctionsTheWindowCalls()
        {
            string html = Read("Graphics_r1", "Help", "help.html");
            string window = Read("Graphics_r1", "Views", "HelpWindow.xaml.cs");

            foreach (var fn in new[] { "helpOpenSearch", "helpBackToToc" })
            {
                StringAssert.Contains(html, $"function {fn}()", $"help.html に {fn} がありません");
                StringAssert.Contains(window, $"\"{fn}()\"", $"HelpWindow が {fn} を呼んでいません");
            }
            // 本文側でもキーを受ける (アプリ側に届かなかったとき)
            StringAssert.Contains(html, "document.addEventListener('keydown'");
            // 今のタブの通知と、アプリ側の受け取りの文字が合っている
            StringAssert.Contains(html, "postMessage('tab:' + tabId)");
            StringAssert.Contains(window, "\"tab:search\"");
            // 検索タブと検索欄の識別子 (関数が使う)
            StringAssert.Contains(html, "id=\"search\"");
            StringAssert.Contains(html, "id=\"searchInput\"");
            // 目次の欄を開く関数を、狭い幅のときのために公開している
            StringAssert.Contains(html, "window.helpOpenSidebar =");
        }

        [TestMethod]
        public void TheShortcutIsListed()
        {
            StringAssert.Contains(Read("Graphics_r1", "Views", "ShortcutKeysWindow.xaml.cs"), "\"Ctrl + F\"");
            StringAssert.Contains(Read("Graphics_r1", "Help", "help.html"), "<td>Ctrl+F</td>");
        }
    }
}
