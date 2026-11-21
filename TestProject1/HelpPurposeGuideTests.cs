using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの「目的から探す」と、検索の目的による絞り込み (外部レビュー #26 2026-11-21)。
    ///
    /// <para>操作方法・計算方法・更新履歴が 1 つの help.html にあり、目的から探しにくかった。本文は分けず、
    /// 章 (h2) ごとに data-purpose で目的の区分を付け、本文先頭の入口と検索の範囲の選択がその区分を使う。
    /// 区分を付け忘れた章は、目的で絞った検索に出なくなる (例外にはならず黙って抜ける) ので、ここで全数を見る。</para>
    /// </summary>
    [TestClass]
    public class HelpPurposeGuideTests
    {
        private static readonly string[] Purposes = ["start", "input", "result", "theory", "history"];

        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        /// <summary>コメントアウトした部分を除いた本文。</summary>
        private static string Markup() => Regex.Replace(Help(), "<!--.*?-->", "", RegexOptions.Singleline);

        [TestMethod]
        public void EveryChapterHasAPurpose()
        {
            var chapters = Regex.Matches(Markup(), "<h2\\b[^>]*>").Select(m => m.Value).ToList();
            Assert.IsTrue(chapters.Count >= 25, $"(前提) ヘルプの章を読めていない: {chapters.Count}");

            var missing = chapters.Where(h => !Regex.IsMatch(h, "data-purpose=\"(" + string.Join("|", Purposes) + ")\"")).ToList();
            Assert.AreEqual(0, missing.Count,
                "目的の区分 (data-purpose) の無い章があります。目的で絞った検索に出ません:\n  " + string.Join("\n  ", missing));

            foreach (var p in Purposes)
                Assert.IsTrue(chapters.Any(h => h.Contains($"data-purpose=\"{p}\"")), $"区分「{p}」の章が 1 つもありません");
        }

        /// <summary>入口のカード・検索の範囲の選択肢・スクリプトの表示名が、同じ 5 区分をそろえて持つ。</summary>
        [TestMethod]
        public void TheGuideTheScopeAndTheLabelsUseTheSamePurposes()
        {
            string html = Markup();
            string guide = Regex.Match(html, "<nav id=\"purpose-guide\".*?</nav>", RegexOptions.Singleline).Value;
            Assert.IsFalse(string.IsNullOrEmpty(guide), "本文先頭の「目的から探す」がありません");
            string scope = Regex.Match(html, "<select id=\"searchScope\".*?</select>", RegexOptions.Singleline).Value;
            Assert.IsFalse(string.IsNullOrEmpty(scope), "検索の範囲の選択がありません");
            string labels = Regex.Match(html, "const PurposeLabels = \\{.*?\\};", RegexOptions.Singleline).Value;
            Assert.IsFalse(string.IsNullOrEmpty(labels), "スクリプトに PurposeLabels がありません");

            foreach (var p in Purposes)
            {
                StringAssert.Contains(guide, $"class=\"purpose-card\" data-purpose=\"{p}\"", $"入口に「{p}」のカードがありません");
                StringAssert.Contains(guide, $"class=\"purpose-search\" data-purpose=\"{p}\"", $"入口の「{p}」に「この目的で検索」がありません");
                string label = Regex.Match(labels, $"{p}: '([^']+)'").Groups[1].Value;
                Assert.IsFalse(string.IsNullOrEmpty(label), $"PurposeLabels に「{p}」がありません");
                StringAssert.Contains(scope, $"<option value=\"{p}\">{label}</option>", $"検索の範囲の「{p}」の表示が PurposeLabels と違います");
                StringAssert.Contains(guide, $"<p class=\"purpose-name\">{label}</p>", $"入口の「{p}」の名前が PurposeLabels と違います");
            }
            Assert.AreEqual(Purposes.Length, Regex.Matches(guide, "class=\"purpose-card\"").Count, "入口のカードの数が区分の数と違います");
        }

        /// <summary>入口のリンク先がヘルプの中にある (見出しの id を変えると黙って先頭に飛ぶ)。</summary>
        [TestMethod]
        public void TheGuideLinksResolve()
        {
            string html = Markup();
            string guide = Regex.Match(html, "<nav id=\"purpose-guide\".*?</nav>", RegexOptions.Singleline).Value;
            var ids = new HashSet<string>(Regex.Matches(html, "\\bid=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
            var targets = Regex.Matches(guide, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.IsTrue(targets.Count >= 10, $"(前提) 入口のリンクを読めていない: {targets.Count}");
            var broken = targets.Where(t => !ids.Contains(t)).ToList();
            Assert.AreEqual(0, broken.Count, "入口のリンク先が見つかりません: " + string.Join(", ", broken));

            // 「以前の版との差」のカードの説明 (結果が変わる変更の印) が、更新履歴で実際に使われている
            StringAssert.Contains(guide, "「結果が変わります」");
            Assert.IsTrue(Regex.Matches(html, ">結果が変わります<").Count >= 3, "更新履歴に「結果が変わります」の印がありません");
        }

        /// <summary>範囲の切り替えと「この目的で検索」が検索をやり直す (関数名を変えると黙って何も起きない)。</summary>
        [TestMethod]
        public void TheScopeIsWiredToTheSearch()
        {
            string html = Help();
            StringAssert.Contains(html, "getElementById('searchScope').addEventListener('change'");
            StringAssert.Contains(html, "querySelectorAll('.purpose-search')");
            StringAssert.Contains(html, "purpose: _purposeOf(heads, hi)");
            // 一致を見出しごとにまとめるのは文書の順で。main 直下の兄弟だけを見る方式は、<section> の中の見出しを見つけられず
            // 一覧からほとんどの節が落ちていた
            Assert.IsFalse(html.Contains("_findEnclosingHeading"), "一致の見出しを main 直下の兄弟から探す方式に戻っています");
            StringAssert.Contains(html, "allGroups.filter(g => g.purpose === scope)");
            StringAssert.Contains(html, "purposeLink.href = '#purpose-guide'");
        }
    }
}
