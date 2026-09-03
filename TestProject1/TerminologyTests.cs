using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 統一した呼び名が元に戻っていないかを検査する。
    ///
    /// 沈下系 4 解析は、リボン・ウィンドウタイトル・ヘルプ・グラフで
    /// 4 通りの名前が付いていた。「単杭 / 群杭」体系に統一したので、
    /// 引退した呼び名が画面文字列に復活したら落とす。
    ///
    /// 対象は<b>利用者に見える文字列だけ</b>。クラス名・メソッド名・
    /// ファイル名 (VerticalBeamCalculationWindow など) は対象外。
    /// </summary>
    [TestClass]
    public class TerminologyTests
    {
        /// <summary>引退した呼び名 → 代わりに使う呼び名</summary>
        private static readonly (string Retired, string Use)[] RetiredNames =
        {
            ("土層沈下",             "群杭沈下"),
            ("荷重沈下関係解析",     "単杭沈下解析"),
            ("基礎梁考慮沈下解析",   "単杭沈下解析（基礎梁考慮）"),
            // 同じ解析に 3 つ目の呼び名が付いていた。計算書の見出しにも出ていた
            ("基礎梁考慮鉛直解析",   "単杭沈下解析（基礎梁考慮）"),
            ("Export to File",       "ファイルに出力"),
            // 既製杭のせん断耐力の 2 式は、コード内では「斜め引張破壊 / ウェブ破壊」と
            // 呼んでいたが、出典の「基礎部材の強度と変形性能」は
            // 「斜めひび割れ / 縦ひび割れ」と書いている。図の凡例に出すにあたり出典に合わせた。
            ("斜め引張破壊",         "斜めひび割れ"),
            ("斜め引張り破壊",       "斜めひび割れ"),
            ("ウェブ破壊",           "縦ひび割れ"),
        };

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(TerminologyTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// XAML の表示文字列になる属性。
        ///
        /// <c>Tag</c> も含める。ツールチップの本文を Tag に置いてスタイル側で表示する書き方があり、
        /// ここを見ていなかったため、リボンの説明に引退した呼び名が残っていた。
        /// </summary>
        private static readonly Regex DisplayAttribute =
            new("(?:Header|Content|Title|ToolTip|Text|Tag)=\"([^\"]*)\"", RegexOptions.Compiled);

        [TestMethod]
        public void RetiredTerms_DoNotAppearInXamlDisplayStrings()
        {
            string root = FindSolutionRoot();
            var hits = new List<string>();

            foreach (string xaml in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1", "Views"), "*.xaml", SearchOption.AllDirectories))
            {
                int lineNo = 0;
                foreach (string line in File.ReadLines(xaml))
                {
                    lineNo++;
                    foreach (Match m in DisplayAttribute.Matches(line))
                    {
                        string text = m.Groups[1].Value;
                        foreach (var (retired, use) in RetiredNames)
                        {
                            if (text.Contains(retired, StringComparison.Ordinal))
                                hits.Add($"{Path.GetFileName(xaml)}:{lineNo}  \"{text}\"  → 「{use}」を使う");
                        }
                    }
                }
            }

            Assert.AreEqual(0, hits.Count,
                "引退した呼び名が画面文字列に残っています:\n  " + string.Join("\n  ", hits));
        }

        [TestMethod]
        public void RetiredTerms_DoNotAppearInHelp()
        {
            string root = FindSolutionRoot();
            string help = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));

            var hits = RetiredNames
                .Where(p => help.Contains(p.Retired, StringComparison.Ordinal))
                .Select(p => $"「{p.Retired}」 → 「{p.Use}」を使う")
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "引退した呼び名が help.html に残っています:\n  " + string.Join("\n  ", hits));
        }

        /// <summary>
        /// 「絞り込みなし」の選択肢が日本語で 1 つに揃っていること。
        /// 以前はグラフが "All"、テーブルが "ALL" と綴りまで分かれていた。
        /// </summary>
        [TestMethod]
        public void AllOption_IsSingleJapaneseTerm()
        {
            Assert.AreEqual("すべて", PileDesign.Common.UiText.All);

            // 旧綴りも判定は通ること (保存済みの値が混ざっても壊れない)
            Assert.IsTrue(PileDesign.Common.UiText.IsAll("すべて"));
            Assert.IsTrue(PileDesign.Common.UiText.IsAll("All"));
            Assert.IsTrue(PileDesign.Common.UiText.IsAll("ALL"));
            Assert.IsFalse(PileDesign.Common.UiText.IsAll("レベル1"));
            Assert.IsFalse(PileDesign.Common.UiText.IsAll(null));
        }

        /// <summary>
        /// C# の文字列リテラルにも引退した呼び名を残さないこと。
        ///
        /// 画面に出る文字列は XAML だけではない。ショートカット一覧・ダイアログの文面・
        /// 計算書 (docx) の見出しはコードの文字列で書かれており、
        /// XAML と help.html しか見ていなかったため<b>同じ機能に 3 つの呼び名</b>が並んでいた
        /// (「基礎梁考慮鉛直解析」は計算書の章題にまで出ていた)。
        ///
        /// コメントとログの書式は対象外。利用者の目に触れない。
        /// </summary>
        [TestMethod]
        public void RetiredTerms_DoNotAppearInCSharpStringLiterals()
        {
            string root = FindSolutionRoot();
            var literal = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);
            var logCall = new Regex(@"Log\.(Information|Debug|Warning|Error|Verbose|Fatal)", RegexOptions.Compiled);
            var hits = new List<string>();

            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                int lineNo = 0;
                foreach (string raw in File.ReadLines(cs))
                {
                    lineNo++;

                    int comment = raw.IndexOf("//", StringComparison.Ordinal);
                    string line = comment >= 0 ? raw[..comment] : raw;
                    if (logCall.IsMatch(line)) continue;

                    foreach (Match m in literal.Matches(line))
                    {
                        string text = m.Groups[1].Value;
                        foreach (var (retired, use) in RetiredNames)
                        {
                            if (text.Contains(retired, StringComparison.Ordinal))
                                hits.Add($"{Path.GetFileName(cs)}:{lineNo}  {text}  → 「{use}」を使う");
                        }
                    }
                }
            }

            Assert.AreEqual(0, hits.Count,
                "引退した呼び名がコードの文字列に残っています:\n  " + string.Join("\n  ", hits));
        }
    }
}
