using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの「記号一覧」と、記号だけの表示に名前を添えたこと (外部レビュー #27 2026-11-21)。
    ///
    /// <para>文献の記号をそのまま使うので、β が組合せ係数・液状化の低減率・耐力の低減係数・梁の角度などを表す。
    /// とくに βL は「基礎部の慣性力の組合せ係数」と「液状化の低減率」の 2 つの意味がある。</para>
    /// </summary>
    [TestClass]
    public class HelpSymbolListTests
    {
        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        private static string SymbolTable()
        {
            string table = Regex.Match(Help(), "<table class=\"table-doc\" id=\"symbol-list-table\">.*?</table>", RegexOptions.Singleline).Value;
            Assert.IsFalse(string.IsNullOrEmpty(table), "記号一覧の表がありません");
            return table;
        }

        [TestMethod]
        public void TheListExists_AndCoversTheConfusedSymbols()
        {
            StringAssert.Contains(Help(), "<h2 id=\"h-記号一覧\" data-purpose=\"theory\">");
            var rows = Regex.Matches(SymbolTable(), "<tr><td>(.*?)</td><td>(.*?)</td>").Select(m => (Symbol: m.Groups[1].Value, Meaning: m.Groups[2].Value)).ToList();
            Assert.IsTrue(rows.Count >= 30, $"(前提) 記号一覧の行を読めていない: {rows.Count}");

            // βL の 2 つの意味が両方ある
            Assert.IsTrue(rows.Any(r => r.Symbol.StartsWith("$\\beta_L$") && r.Meaning.Contains("組合せ係数")), "βL (組合せ係数) がありません");
            Assert.IsTrue(rows.Any(r => r.Symbol.StartsWith("$\\beta_L$") && r.Meaning.Contains("液状化")), "βL (液状化の低減率) がありません");
            // 単独の β の意味が 3 つ以上
            Assert.IsTrue(rows.Count(r => r.Symbol == "$\\beta$") >= 3, "単独の β の意味が揃っていません");
            // ξ の 2 つの意味
            Assert.IsTrue(rows.Count(r => r.Symbol == "$\\xi$") >= 2, "ξ (群杭係数・施工品質管理係数) が揃っていません");
            Assert.IsTrue(rows.Count(r => r.Symbol == "$F_c$") >= 2, "Fc (コンクリート強度・細粒分含有率) が揃っていません");
        }

        [TestMethod]
        public void TheLinksResolve()
        {
            string help = Regex.Replace(Help(), "<!--.*?-->", "", RegexOptions.Singleline);
            var ids = new HashSet<string>(Regex.Matches(help, "\\bid=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
            var targets = Regex.Matches(SymbolTable(), "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.IsTrue(targets.Count >= 20, $"(前提) リンクを読めていない: {targets.Count}");
            var broken = targets.Where(t => !ids.Contains(t)).Distinct().ToList();
            Assert.AreEqual(0, broken.Count, "記号一覧のリンク先が見つかりません: " + string.Join(", ", broken));
        }

        /// <summary>記号一覧を作るときに見つけた誤り 5 件が戻っていない。</summary>
        [TestMethod]
        public void TheSymbolErrorsFoundOnTheWayStayFixed()
        {
            string help = Help();
            Assert.IsFalse(help.Contains("単杭支持力低減"), "群杭係数 ξ を支持力の低減と説明しています (kh0 の低減)");
            Assert.IsFalse(help.Contains("<td>累積損傷度</td>"), "Dcy を累積損傷度と説明しています (液状化による地表面の変位)");
            Assert.IsFalse(help.Contains("細粒分含有率$F_L$"), "細粒分含有率を F_L と書いています (F_c)");
            Assert.IsFalse(Regex.IsMatch(help, "symbol-label\">\\$\\r?\\nu\\$"), "ν が改行と u に壊れています");
            Assert.IsFalse(help.Contains("<span class=\"symbol-label\">$E_c$</span>杭体のヤング係数"), "キャプテンパイルの E_c を杭体のヤング係数と説明しています (パイルキャップのコンクリート)");
        }

        /// <summary>画面で記号だけを見出しにしていたところに名前を添えた。</summary>
        [TestMethod]
        public void BareSymbolsOnScreensHaveNames()
        {
            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(main, "Header=\"角度β\"");
            StringAssert.Contains(main, "<TextBlock Text=\"角度\" TextAlignment=\"Center\"/>");

            string loadCase = TestSource.Read("Graphics_r1", "Views", "LoadCaseWindow.xaml");
            foreach (var name in new[] { "地盤変位", "上部構造", "基礎部" })
                StringAssert.Contains(loadCase, $"<TextBlock Text=\"{name}\" TextAlignment=\"Center\"/>", $"組合せ係数の見出しに「{name}」がありません");
            // 分離入力を足した後も「βU・βL を考慮しない」とだけ書いていた
            StringAssert.Contains(loadCase, "「分離入力」にすると");
        }
    }
}
