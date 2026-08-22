using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの章立ての検査。
    ///
    /// 第1部は UI の構造順 (リボンの並び順) で書かれていて作業順に読めず、
    /// 「リボンメニュー」章だけで 4,022 行 (第1部の 57%) あった。
    /// 作業順に並べ替えて章を分割したので、元に戻っていないかを見る。
    /// </summary>
    [TestClass]
    public class HelpStructureTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(HelpStructureTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private sealed record Chapter(string Title, int Start, int LineCount);

        /// <summary>第1部 (h1「利用マニュアル」〜 次の h1) の h2 章を順に返す。</summary>
        private static List<Chapter> PartOneChapters()
        {
            string help = File.ReadAllText(Path.Combine(FindSolutionRoot(), "Graphics_r1", "Help", "help.html"));

            // コメントアウトされた見出しは章ではない
            string markup = Regex.Replace(help, "<!--.*?-->", "", RegexOptions.Singleline);

            var heads = Regex.Matches(markup, @"<(h[12])\b[^>]*>(.*?)</\1>", RegexOptions.Singleline)
                .Select(m => (Level: m.Groups[1].Value,
                              Title: Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Trim(),
                              Pos: m.Index))
                .ToList();

            int first = heads.FindIndex(h => h.Level == "h1");
            int next = heads.FindIndex(first + 1, h => h.Level == "h1");
            Assert.IsTrue(first >= 0 && next > first, "第1部の範囲を特定できません");

            var result = new List<Chapter>();
            for (int i = first + 1; i < next; i++)
            {
                if (heads[i].Level != "h2") continue;
                int end = (i + 1 < heads.Count) ? heads[i + 1].Pos : markup.Length;
                int lines = markup.Substring(heads[i].Pos, end - heads[i].Pos).Count(c => c == '\n');
                result.Add(new Chapter(heads[i].Title, heads[i].Pos, lines));
            }
            return result;
        }

        /// <summary>
        /// 入門 (クイックスタート) が、告示解釈の注記より前にあること。
        /// F1 は文書の先頭に着地するため、順番がそのまま「最初に読まされるもの」になる。
        /// </summary>
        [TestMethod]
        public void QuickStart_ComesBeforeTheDesignPracticeNote()
        {
            var chapters = PartOneChapters();
            int quickstart = chapters.FindIndex(c => c.Title.Contains("クイックスタート", StringComparison.Ordinal));
            int practice = chapters.FindIndex(c => c.Title.Contains("設計実務上の扱い", StringComparison.Ordinal));

            Assert.IsTrue(quickstart >= 0, "クイックスタートガイドの章が見つかりません");
            Assert.IsTrue(practice >= 0, "「設計実務上の扱いについて」の章が見つかりません");
            Assert.IsTrue(quickstart < practice,
                $"クイックスタート ({quickstart + 1} 章) が「設計実務上の扱いについて」({practice + 1} 章) より後にあります。"
                + "初めての人が告示解釈から読まされることになります。");
        }

        /// <summary>
        /// 入力 → 解析 → 結果 の順で並んでいること。
        /// </summary>
        [TestMethod]
        public void PartOne_FollowsTheWorkOrder()
        {
            var titles = PartOneChapters().Select(c => c.Title).ToList();

            int Index(string keyword)
            {
                int i = titles.FindIndex(t => t.Contains(keyword, StringComparison.Ordinal));
                Assert.IsTrue(i >= 0, $"「{keyword}」を含む章が見つかりません:\n  " + string.Join("\n  ", titles));
                return i;
            }

            int input = Index("― 入力");
            int run = Index("― 解析の実行");
            int result = Index("解析結果メニュー");

            Assert.IsTrue(input < run, "入力より先に解析の実行が来ています");
            Assert.IsTrue(run < result, "解析の実行より先に解析結果が来ています");
        }

        /// <summary>
        /// 1 つの章が大きくなりすぎていないこと。
        ///
        /// 目次から見つけられなくなるうえ、章の中で迷子になる。
        /// 元の「リボンメニュー」章は 4,022 行あった。
        /// </summary>
        [TestMethod]
        public void NoChapterIsOversized()
        {
            const int limit = 2000;

            var oversized = PartOneChapters()
                .Where(c => c.LineCount > limit)
                .Select(c => $"{c.Title} ({c.LineCount} 行)")
                .ToList();

            Assert.AreEqual(0, oversized.Count,
                $"第1部に {limit} 行を超える章があります。節に分けるか章を分割してください:\n  "
                + string.Join("\n  ", oversized));
        }
    }
}
