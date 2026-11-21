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
    /// とくに指針の αL・βL は「組合せ係数」と「液状化の補正係数」の 2 つの意味がある。
    /// 利用者の判断 (2026-11-21) で、組合せ係数を αL・βL のまま、液状化側を αLq・βLq と書き分けた。</para>
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

            // 組合せ係数は αL・βL、液状化の補正係数は αLq・βLq
            Assert.IsTrue(rows.Any(r => r.Symbol.StartsWith("$\\beta_L$") && r.Meaning.Contains("組合せ係数")), "βL (組合せ係数) がありません");
            Assert.IsTrue(rows.Any(r => r.Symbol.StartsWith("$\\beta_{Lq}$") && r.Meaning.Contains("液状化")), "βLq (液状化の低減率) がありません");
            Assert.IsTrue(rows.Any(r => r.Symbol.StartsWith("$\\alpha_{Lq}$") && r.Meaning.Contains("液状化")), "αLq (液状化の py の補正係数) がありません");
            Assert.IsFalse(rows.Any(r => r.Symbol.StartsWith("$\\beta_L$") && r.Meaning.Contains("液状化")), "液状化の低減率を βL と書いています (βLq)");
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

        /// <summary>
        /// 液状化の補正係数を、組合せ係数と同じ βL と書いたところが戻っていない (2026-11-21 に βLq へ書き分けた)。
        /// 「低減率」「補正係数」の直後の βL を、ヘルプと本体の .cs / .xaml から探す。
        /// </summary>
        [TestMethod]
        public void TheLiquefactionFactorIsWrittenBetaLq()
        {
            // 「水平地盤反力係数の低減係数 β」「液状化区間（β < 1）」も液状化の βLq (SC 杭の「低減係数 β」は別の量なので拾わない)
            var bad = new Regex(@"(低減率?|補正係数)\s*(\$\\beta_L\$|βL(?!q)|<em>β<sub>L</sub>|β<_L>)"
                + @"|地盤反力係数の低減係数\s*(\$\\beta\$|β(?!Lq))|液状化区間\s*[（(]\s*β(?!Lq)|\$\\beta\s*&lt;\s*1\$");
            var sep = System.IO.Path.DirectorySeparatorChar;
            string app = System.IO.Path.Combine(TestSource.Root(), "Graphics_r1");
            var files = System.IO.Directory.GetFiles(app, "*.*", System.IO.SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml"))
                    && !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}TestResults{sep}"))
                .Append(System.IO.Path.Combine(app, "Help", "help.html"))
                .ToList();
            TestSource.AssertScanned(files.Count, 300, "本体の .cs / .xaml とヘルプ");

            int betaLq = 0;
            var hits = new List<string>();
            foreach (var f in files)
            {
                string text = System.IO.File.ReadAllText(f);
                betaLq += Regex.Matches(text, @"βLq|\\beta_\{Lq\}|<sub>Lq</sub>|β<_Lq>").Count;
                hits.AddRange(bad.Matches(text).Select(m => $"{System.IO.Path.GetFileName(f)}: {m.Value}"));
            }
            // 見張りが空振りしていない (書き換えた先が見つかる)
            TestSource.AssertScanned(betaLq, 30, "βLq の表記");
            Assert.AreEqual(0, hits.Count, "液状化の低減率を βL と書いています (組合せ係数と紛れるので βLq):\n" + string.Join("\n", hits));
        }

        /// <summary>
        /// βLq の出典は基礎指針'19 図3.2.13。表4.5.1 は「液状化しない地盤における水平変位の算定法」で、
        /// 2026-11-21 まで βLq の出典を誤って表4.5.1 と書いていた (利用者が指針で確認)。
        /// </summary>
        [TestMethod]
        public void TheBetaLqSourceIsFigure3_2_13()
        {
            string help = Help();
            Assert.IsFalse(Regex.IsMatch(help, @"表\s?4\.5\.1\s?[（(]\$N_a\$"), "βLq の出典を表4.5.1 と書いています (図3.2.13)");
            Assert.IsFalse(help.Contains("表4.5.1・図3.2.6"), "βLq の出典を表4.5.1 と書いています (図3.2.13)");
            StringAssert.Contains(help, "図3.2.13（$N_a$ と深さ $z$ の関数");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Models", "InputData", "Liquefaction.cs"), "基礎指針'19 図3.2.13");
        }
    }
}
