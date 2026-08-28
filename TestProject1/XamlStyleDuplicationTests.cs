using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 同じスタイルを画面ごとに書かないこと。
    ///
    /// 同じ <c>x:Key</c> の定義が散らばると、直したつもりが一部の画面に効かない。
    /// 実際 <c>YellowHeaderStyle</c> は 6 箇所にあり、中身が 4 通りに食い違っていた
    /// (しかも 5 箇所は誰にも使われていなかった)。
    ///
    /// 共有するものは <c>Styles.xaml</c> に 1 つだけ置く。
    /// 画面固有のものは、その画面にだけあれば重複ではない。
    /// </summary>
    [TestClass]
    public class XamlStyleDuplicationTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(XamlStyleDuplicationTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>既に重複している名前。<b>増やさないための一覧</b>で、直したらここから消すこと。</summary>
        private static readonly HashSet<string> KnownDuplicates =
        [
            "CustomDataGridCellStyle",
            "DeepBlueHeaderStyle",
            "FormulaControlStyle",
            "GranularityClassTextBlockStyle",
            "SvgIconStyle16",
            "SvgIconStyle24"
        ];

        /// <summary>
        /// まだ使われていない名前。<b>増やさないための一覧</b>。
        ///
        /// 残っている 7 件はいずれも <c>Styles.xaml</c> にあり、
        /// 画面側の色・サイズ直書きを寄せるために<b>用意してある</b>もの。
        /// 消すのではなく、直書きを見つけたらそちらへ寄せて、この一覧から外す。
        /// </summary>
        private static readonly HashSet<string> KnownUnused =
        [
            "DialogSecondaryButtonStyle",
            "ErrorTextStyle",
            "FadeInBorderStyle",
            "FormLabelStyle",
            "MutedTextStyle",
            "SectionSubHeaderStyle",
            "SignedIntegerTextBoxStyle",
            "WarningTextStyle"
        ];

        private static readonly Regex StyleKey =
            new(@"<Style\s+x:Key=""([^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// 同じ名前のスタイルが 2 つ以上の XAML で定義されていないこと。
        /// </summary>
        [TestMethod]
        public void NoStyleKeyIsDefinedInMoreThanOneFile()
        {
            string root = FindSolutionRoot();
            var byKey = new Dictionary<string, List<string>>();

            foreach (string xaml in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1"), "*.xaml", SearchOption.AllDirectories))
            {
                if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                foreach (Match m in StyleKey.Matches(File.ReadAllText(xaml)))
                {
                    string key = m.Groups[1].Value;
                    if (!byKey.TryGetValue(key, out var files)) byKey[key] = files = [];
                    string name = Path.GetFileName(xaml);
                    if (!files.Contains(name)) files.Add(name);
                }
            }

            var duplicated = byKey.Where(kv => kv.Value.Count > 1)
                .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}")
                .OrderBy(x => x)
                .ToList();

            AssertAgainstBaseline(
                duplicated.Select(d => d.Split(':')[0]).ToList(),
                KnownDuplicates,
                "同じスタイルが複数の XAML で定義されています (共有するなら Styles.xaml に 1 つだけ置くこと)",
                duplicated);
        }

        /// <summary>
        /// 誰にも使われていないスタイルを置いたままにしないこと。
        /// 使われていない定義は、直す価値のあるものと見分けがつかない。
        /// </summary>
        [TestMethod]
        public void NoStyleIsDefinedWithoutBeingUsed()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");

            var sources = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                          || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();

            string all = string.Join("\n", sources.Select(File.ReadAllText));

            var unused = new List<string>();
            foreach (Match m in StyleKey.Matches(all))
            {
                string key = m.Groups[1].Value;

                // 参照の書き方は 4 通りある。
                //   {StaticResource Key} / {DynamicResource Key}
                //   <StaticResource ResourceKey="Key"/>
                //   FindResource("Key") / Resources["Key"]  … code-behind から
                string escaped = Regex.Escape(key);
                bool used = Regex.IsMatch(all, @"(Static|Dynamic)Resource\s+" + escaped + @"\s*[}\s]")
                         || Regex.IsMatch(all, @"ResourceKey=""" + escaped + @"""")
                         || Regex.IsMatch(all, @"Resource\(""" + escaped + @"""\)")
                         || Regex.IsMatch(all, @"Resources\[""" + escaped + @"""\]");
                if (!used && !unused.Contains(key)) unused.Add(key);
            }

            AssertAgainstBaseline(unused, KnownUnused,
                "どこからも参照されていないスタイルがあります", unused);
        }

        /// <summary>
        /// 一覧に載っていないものが出たら失敗、一覧にあるのに解消済みのものも失敗。
        ///
        /// 既存の重複をすべて直すのは画面の見た目に関わるため一度には踏み込めない。
        /// 一方で放っておくと増える。そこで<b>今ある分は一覧で許し、増えたら止める</b>。
        /// 直したときに一覧から外し忘れると一覧が腐るので、そちらも失敗させる
        /// (この repo の警告ベースラインと同じ考え方)。
        /// </summary>
        private static void AssertAgainstBaseline(
            IReadOnlyCollection<string> found, HashSet<string> known, string what, IEnumerable<string> detail)
        {
            var added = found.Where(f => !known.Contains(f)).OrderBy(x => x).ToList();
            var fixedUp = known.Where(k => !found.Contains(k)).OrderBy(x => x).ToList();

            var messages = new List<string>();
            if (added.Count > 0)
            {
                var rows = detail.Where(d => added.Any(a => d.StartsWith(a, StringComparison.Ordinal)));
                messages.Add($"{what}:\n  " + string.Join("\n  ", rows));
            }
            if (fixedUp.Count > 0)
            {
                messages.Add("解消済みなのに一覧に残っています (一覧から外してください):\n  "
                    + string.Join("\n  ", fixedUp));
            }

            Assert.AreEqual(0, messages.Count, string.Join("\n\n", messages));
        }
    }
}
