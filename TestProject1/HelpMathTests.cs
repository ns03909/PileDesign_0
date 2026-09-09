using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの数式が壊れていないこと。
    ///
    /// help.html の数式は MathJax で描く。<b>バックスラッシュが消えると
    /// 「Math input error」とだけ表示される。</b>ビルドもテストも通り、
    /// ヘルプを開いてその行を見た人だけが気づく。
    ///
    /// 実際に 8 か所が壊れていた。原因はどれも同じで、ファイルを書き換えるときに
    /// <b>バックスラッシュのエスケープが解釈された</b>こと。
    ///
    /// <list type="bullet">
    /// <item><c>\varepsilon</c> → 垂直タブ + <c>arepsilon</c></item>
    /// <item><c>\frac{</c> → 改ページ + <c>rac{</c></item>
    /// <item><c>\times</c> → タブ + <c>imes</c></item>
    /// <item><c>\beta_2</c> → バックスペース + <c>eta_2</c></item>
    /// </list>
    ///
    /// 1 件は「β2 の既定を 0.75 にする」作業で新たに入れてしまったもの。
    /// 手で書き換えている限り再発するので、<b>制御文字が入っていないこと</b>で見張る。
    /// </summary>
    [TestClass]
    public class HelpMathTests
    {
        /// <summary>
        /// 本文に制御文字が入っていないこと。
        ///
        /// 改行 (LF/CR) 以外の制御文字は、まともな HTML には現れない。
        /// タブは字下げに使えなくもないが、このファイルは空白で字下げしているので
        /// 混ざっていたらエスケープが解釈された跡と見てよい。
        /// </summary>
        [TestMethod]
        public void TheHelp_HasNoControlCharacters()
        {
            var path = Path.Combine(TestSource.Root(), "Graphics_r1", "Help", "help.html");
            var text = File.ReadAllText(path);

            var found = new List<string>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c >= ' ' || c == '\r') continue;
                    string what = c switch
                    {
                        '\t' => @"タブ (\t を解釈した跡。\times など)",
                        '\v' => @"垂直タブ (\v を解釈した跡。\varepsilon など)",
                        '\f' => @"改ページ (\f を解釈した跡。\frac など)",
                        '\b' => @"バックスペース (\b を解釈した跡。\beta など)",
                        _ => $"制御文字 U+{(int)c:X4}",
                    };
                    found.Add($"{i + 1} 行目: {what}  …{Excerpt(lines[i])}");
                    break;
                }
            }

            Assert.AreEqual(0, found.Count,
                "ヘルプに制御文字が入っています。数式のバックスラッシュが"
                + "書き換えのときに解釈された跡です。画面には「Math input error」とだけ出ます:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", found.Take(10)));

            static string Excerpt(string line) =>
                line.Length <= 60 ? line.Trim() : line.Trim()[..60] + "…";
        }

        /// <summary>
        /// 数式が実際に読み込まれる形になっていること。
        ///
        /// 制御文字が無くても、<c>\</c> がまるごと落ちていれば同じことになる。
        /// よく使う命令が最低限の数だけ残っていることを見る。
        /// </summary>
        [TestMethod]
        public void TheHelp_StillHasItsFormulas()
        {
            var text = File.ReadAllText(
                Path.Combine(TestSource.Root(), "Graphics_r1", "Help", "help.html"));

            foreach (var (command, atLeast) in new[]
            {
                (@"\frac", 40), (@"\varepsilon", 30), (@"\sigma", 60),
                (@"\phi", 40), (@"\times", 5), (@"\beta", 20),
            })
            {
                int count = CountOccurrences(text, command);
                Assert.IsTrue(count >= atLeast,
                    $"{command} が {count} 件しかありません（最低 {atLeast} 件のはず）。"
                    + "バックスラッシュが落ちていないか確認してください");
            }

            static int CountOccurrences(string s, string needle)
            {
                int n = 0, i = 0;
                while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
                return n;
            }
        }
    }
}
