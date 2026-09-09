using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ソース走査の道具を書き写さないこと。
    ///
    /// このリポジトリのガードテストは「ソースを読んで悪い書き方を探す」形なので、
    /// どれも「ルートを探す」「ファイルを読む」「メソッド本体を切り出す」を必要とする。
    /// これを各ファイルで書き写した結果、同じ名前の関数が数十通りに増え、
    /// <b>実装が食い違って</b>いた。
    ///
    /// <list type="bullet">
    /// <item>ルートの探し方が 4 系統。うち 1 つは <c>".."</c> の決め打ちで、
    ///   対象フレームワークが変わると黙って存在しないパスを返す</item>
    /// <item>本体の切り出しの 1 つは括弧を数えず<b>先頭から 800 文字</b>だった
    ///   (対象が長ければ後半を見落とす)</item>
    /// </list>
    ///
    /// 実装は <see cref="TestSource"/> に 1 つだけ置く。既存のものは順次そこへ委譲する。
    /// ここでは<b>数が増えないこと</b>だけを見張る。減らすときは下の基準値も下げること。
    /// </summary>
    [TestClass]
    public class TestHelperDuplicationTests
    {
        /// <summary>
        /// 現状の基準値。<b>減らすのはよい。増やすときは TestSource へ寄せること。</b>
        /// 委譲する 1 行 (<c>=&gt; TestSource.Xxx(...)</c>) は数に入れない。
        /// </summary>
        private static readonly (string Name, Regex Pattern, int AtMost)[] Helpers =
        [
            ("FindSolutionRoot", new Regex(@"static\s+string\s+FindSolutionRoot\s*\("), 43),
            ("ReadSource",       new Regex(@"static\s+string\s+ReadSource\s*\("),       8),
            ("ExtractMethodBody",new Regex(@"static\s+string\s+ExtractMethodBody\s*\("), 8),
        ];

        [TestMethod]
        public void TheScanningHelpers_AreNotCopiedFurther()
        {
            var files = Directory.GetFiles(TestSource.Dir("TestProject1"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals("TestSource.cs", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            TestSource.AssertScanned(files.Length, 150, "テストファイル");

            var over = new List<string>();
            foreach (var (name, pattern, atMost) in Helpers)
            {
                int count = 0;
                foreach (var file in files)
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        if (!pattern.IsMatch(line)) continue;
                        // 委譲する 1 行はこの場で本体を持たないので数えない
                        if (line.Contains("=>")) continue;
                        count++;
                    }
                }
                if (count > atMost)
                    over.Add($"{name}: {count} 個 (基準 {atMost} 個以下)");
            }

            Assert.AreEqual(0, over.Count,
                "ソース走査の道具を書き写しています。TestSource に寄せてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", over));
        }

        /// <summary>
        /// 本体の切り出しは<b>括弧を数える</b>こと。文字数で切ると読み落とす。
        /// </summary>
        [TestMethod]
        public void NoHelperCutsBodiesByLength()
        {
            var bad = new List<string>();
            var cut = new Regex(@"Math\.Min\(\s*\w+\.Length\s*,\s*\w+\s*\+\s*\d{3,}\s*\)");
            var body = new Regex(@"static\s+string\s+ExtractMethodBody\s*\(");

            foreach (var file in Directory.GetFiles(TestSource.Dir("TestProject1"), "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!body.IsMatch(lines[i])) continue;

                    // 本体を切り出す道具の中だけを見る。
                    // 「この目印の周りを見る」ための切り出し (TestSource.Region) は正しい使い方で、
                    // ここで咎めると本当の誤りが埋もれる。
                    for (int k = i; k < Math.Min(lines.Length, i + 12); k++)
                    {
                        if (!cut.IsMatch(lines[k])) continue;
                        bad.Add($"{Path.GetFileName(file)}:{k + 1}  {lines[k].Trim()}");
                        break;
                    }
                }
            }

            Assert.AreEqual(0, bad.Count,
                "メソッド本体を文字数で切っています。TestSource.MethodBody を使ってください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", bad));
        }
    }
}
