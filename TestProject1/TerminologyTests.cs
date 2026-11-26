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
            // 場所打ち鋼管コンクリート杭の本体部の設計法（BCJ評定-FD0356-08 5.(3)）。評定書は
            // 「SRC 規準 2014 4章2節に準拠」とだけ書き、同規準の呼び名は「累加強度式」。
            // 「単純累加」は終局強度の章で鉄骨を全塑性として扱う語で、文脈がずれる (2026-09-08)。
            ("単純累加",             "累加強度式"),
            // 群杭沈下の 2 つの解析は、計算方法 (一般 / 反復) ではなく、利用者が選ぶもの (基礎梁を考えるか) で呼ぶ (2026-10-08)
            ("解析（一般）",         "解析（基礎梁なし）"),
            ("解析（反復）",         "解析（基礎梁あり）"),
            ("基礎梁考慮・反復",     "基礎梁あり"),
            ("一般 / 反復",          "基礎梁なし / 基礎梁あり"),
            // リボン「表示」タブの表示をまとめて切り替える機能。「組」では何の組か分かりにくかった (2026-10-17)
            ("表示の組",             "表示プリセット"),
            // 鋼管杭の杭頭部の l2 は、出典 (基礎部材の強度と変形性能 図 8.3) の説明「コンクリート充填部下端から鋼管の断面変化部まで」で呼ぶ。
            // 「鋼管厚部」は図の説明と食い違って見えた (利用者の指摘 2026-11-21)
            ("鋼管厚部の長さ",       "充填部下端〜鋼管の断面変化部"),
        };

        /// <summary>
        /// 画面とヘルプでだけ引退した呼び名。コードの中では保存の値として使い続けるので、C# の文字列は見ない。
        /// 荷重タイプは保存の値「個別十字」「個別矩形」のまま、画面では「杭十字」「杭矩形」と呼ぶ (LoadingTypeDisplayName)。
        /// </summary>
        private static readonly (string Retired, string Use)[] RetiredDisplayNames =
        {
            ("個別十字",             "杭十字"),
            ("個別矩形",             "杭矩形"),
        };

        private static IEnumerable<(string Retired, string Use)> RetiredOnScreen => RetiredNames.Concat(RetiredDisplayNames);

        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

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
                        foreach (var (retired, use) in RetiredOnScreen)
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

            var hits = RetiredOnScreen
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

        /// <summary>
        /// 文献を指す「マニュアル」は、どの文献かわかる名前で書く (利用者の指示 2026-11-27)。
        ///
        /// 杭頭の工法はキャプテンパイル・キャプリングパイル・NewJ-BAR とどれも「〇〇マニュアル」が出典で、
        /// 画面・メッセージ・ヘルプに「マニュアル 4.2.1」「マニュアルに規定がない」と裸で書くと、
        /// どの文献の話か読み手が決められない。ここに並べた文献名の一部としてだけ許す。
        /// 文献を足したら、その名前をここに足す。
        /// </summary>
        private static readonly string[] LiteratureTitlesWithManual =
        {
            // 長い名前を先に消す (短い名前が長い名前の一部のときに取り残しを出さないため)
            "開先付き異形棒鋼 NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)",
            "NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)",
            "NewJ-BAR 設計マニュアル (杭頭補強編)",
            "キャプテンパイル工法 (場所打ち杭用杭頭半固定構法) 設計・施工マニュアル",
            "キャプテンパイル工法 (場所打ち杭用杭頭半固定工法) 設計・施工マニュアル",
            "キャプテンパイル工法（場所打ち杭用杭頭半固定構法）設計・施工マニュアル",
            "キャプテンパイル工法 設計・施工マニュアル",
            "キャプリングパイル工法 設計マニュアル",
            "利用マニュアル", // このプログラム自身のヘルプの見出し
        };

        /// <summary>文献名を除いても「マニュアル」が残るか。title は文献名を見つけた数を足す。</summary>
        private static bool HasBareManual(string text, ref int titles)
        {
            foreach (string t in LiteratureTitlesWithManual)
            {
                int n = 0, at = 0;
                while ((at = text.IndexOf(t, at, StringComparison.Ordinal)) >= 0) { n++; at += t.Length; }
                if (n == 0) continue;
                titles += n;
                text = text.Replace(t, "", StringComparison.Ordinal);
            }
            return text.Contains("マニュアル", StringComparison.Ordinal);
        }

        [TestMethod]
        public void Manual_IsAlwaysNamedAsLiterature()
        {
            string root = FindSolutionRoot();
            var literal = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);
            var logCall = new Regex(@"Log\.(Information|Debug|Warning|Error|Verbose|Fatal)", RegexOptions.Compiled);
            var hits = new List<string>();
            int xamlTitles = 0, csTitles = 0, helpTitles = 0;

            foreach (string xaml in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1", "Views"), "*.xaml", SearchOption.AllDirectories))
            {
                int lineNo = 0;
                foreach (string line in File.ReadLines(xaml))
                {
                    lineNo++;
                    foreach (Match m in DisplayAttribute.Matches(line))
                        if (HasBareManual(m.Groups[1].Value, ref xamlTitles))
                            hits.Add($"{Path.GetFileName(xaml)}:{lineNo}  \"{m.Groups[1].Value}\"");
                }
            }

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
                        if (HasBareManual(m.Groups[1].Value, ref csTitles))
                            hits.Add($"{Path.GetFileName(cs)}:{lineNo}  {m.Groups[1].Value}");
                }
            }

            string help = Regex.Replace(
                File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html")), "<!--.*?-->", "", RegexOptions.Singleline);
            int helpLine = 0;
            foreach (string line in help.Split('\n'))
            {
                helpLine++;
                if (HasBareManual(line, ref helpTitles))
                    hits.Add($"help.html (コメントを除いて {helpLine} 行目)  {line.Trim()}");
            }

            // 見張りが空振りしていないこと (文献名を実際に見つけている)
            Assert.IsTrue(xamlTitles > 0 && csTitles > 0 && helpTitles > 0,
                $"文献名が見つかっていません (XAML {xamlTitles}・C# {csTitles}・ヘルプ {helpTitles})。走査先が変わっていないか確認してください");
            Assert.AreEqual(0, hits.Count,
                "「マニュアル」を文献名なしで書いています。どの文献か (例: 「NewJ-BAR 設計マニュアル (杭頭補強編)」) を書いてください:\n  "
                + string.Join("\n  ", hits));
        }
    }
}
