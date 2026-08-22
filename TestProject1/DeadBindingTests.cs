using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 「無言で失敗するリンク」の検出。
    ///
    /// WPF の Binding 失敗は例外にならないため、存在しないコマンドへのバインドは
    /// 「押しても何も起きないボタン」「効かないショートカット」として静かに残る。
    /// help.html のアンカーも同様に、外れていても「開くがスクロールしない」だけになる。
    /// どちらもビルドを通ってしまうので、ここで機械的に検出する。
    /// </summary>
    [TestClass]
    public class DeadBindingTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DeadBindingTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// XAML が参照する {Binding XxxCommand} が、そのウィンドウの ViewModel に実在すること。
        ///
        /// 対象は ViewModel の型が特定できるウィンドウだけ:
        ///   ・MainWindow.xaml            → MainWindowViewModel
        ///   ・d:DataContext="{d:DesignInstance Type=local:XxxViewModel}" を宣言している XAML
        /// DataTemplate 等で別の型が DataContext になる場合を避けるため、
        /// 相対パス (Foo.Bar) を含むバインドは対象外。
        /// </summary>
        [TestMethod]
        public void CommandBindings_ResolveOnTheirViewModel()
        {
            string root = FindSolutionRoot();
            var assembly = typeof(PileDesign.ViewModels.MainWindowViewModel).Assembly;

            var missing = new List<string>();
            int checkedCount = 0;

            foreach (string xamlPath in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1", "Views"), "*.xaml", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(xamlPath);

                // コメントアウトされたバインドは実際には評価されないので除く。
                string text = Regex.Replace(File.ReadAllText(xamlPath),
                                            @"<!--.*?-->", "", RegexOptions.Singleline);

                Type? vmType = ResolveViewModelType(assembly, fileName, text);
                if (vmType == null) continue;

                var names = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                                  .Select(p => p.Name)
                                  .ToHashSet(StringComparer.Ordinal);

                foreach (Match m in Regex.Matches(text, @"\{Binding\s+(?<path>[A-Za-z_][A-Za-z0-9_]*Command)\b"))
                {
                    string name = m.Groups["path"].Value;
                    checkedCount++;
                    if (!names.Contains(name))
                        missing.Add($"{fileName}: {{Binding {name}}} が {vmType.Name} に無い");
                }
            }

            Assert.IsTrue(checkedCount >= 100,
                $"検査したコマンドバインドが {checkedCount} 件しかない (収集が壊れている可能性)");

            Assert.AreEqual(0, missing.Count,
                "存在しないコマンドにバインドしています (押しても無言で何も起きません):\n  "
                + string.Join("\n  ", missing.Distinct()));
        }

        private static Type? ResolveViewModelType(Assembly assembly, string fileName, string xaml)
        {
            if (fileName == "MainWindow.xaml")
                return assembly.GetType("PileDesign.ViewModels.MainWindowViewModel");

            var m = Regex.Match(xaml, @"d:DataContext=""\{d:DesignInstance\s+Type=\w+:(?<vm>\w+ViewModel)");
            return m.Success
                ? assembly.GetType($"PileDesign.ViewModels.{m.Groups["vm"].Value}")
                : null;
        }

        /// <summary>
        /// help.html 内の内部リンク (href="#...") がすべて実在する id を指すこと。
        /// 見出しの id を消したり綴りを変えたりすると、リンクが静かに効かなくなる。
        /// </summary>
        [TestMethod]
        public void HelpInternalLinks_PointToExistingIds()
        {
            string root = FindSolutionRoot();
            string help = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));

            // <script> の中はコード。href="#anchor" のような説明文が混ざるので除く。
            string markup = Regex.Replace(help, @"<script\b.*?</script>", "", RegexOptions.Singleline);

            var ids = Regex.Matches(markup, @"\bid=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var dead = Regex.Matches(markup, @"href=""#([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .Where(a => !ids.Contains(a))
                .Distinct()
                .ToList();

            Assert.AreEqual(0, dead.Count,
                "help.html の内部リンクが存在しない id を指しています:\n  " + string.Join("\n  ", dead));
        }

        /// <summary>
        /// h1〜h3 の見出しがすべて固定 id を持つこと。
        ///
        /// id の無い見出しは JS が <c>auto-section-N</c> を振るため、
        /// 見出しを 1 つ足すと以降の URL が全部ずれる。
        /// </summary>
        [TestMethod]
        public void MajorHeadings_HaveStableIds()
        {
            string root = FindSolutionRoot();
            string help = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));

            var withoutId = Regex.Matches(help, @"<(h[123])\b([^>]*)>(.*?)</\1>", RegexOptions.Singleline)
                .Where(m => !m.Groups[2].Value.Contains("id=", StringComparison.Ordinal))
                .Select(m => Regex.Replace(m.Groups[3].Value, "<[^>]*>", "").Trim())
                .ToList();

            Assert.AreEqual(0, withoutId.Count,
                "固定 id の無い見出しがあります (見出しを足すと以降の URL がずれます):\n  "
                + string.Join("\n  ", withoutId.Take(20)));
        }

        /// <summary>
        /// help.html の id が重複していないこと。
        /// 重複すると getElementById がどちらを返すか実装依存になる。
        /// </summary>
        [TestMethod]
        public void HelpIds_AreUnique()
        {
            string root = FindSolutionRoot();
            string help = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));

            var duplicates = Regex.Matches(help, @"\bid=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({g.Count()} 回)")
                .ToList();

            Assert.AreEqual(0, duplicates.Count,
                "help.html に重複した id があります:\n  " + string.Join("\n  ", duplicates));
        }
    }
}
