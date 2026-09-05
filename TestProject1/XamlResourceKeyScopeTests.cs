using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// XAML の <c>{StaticResource キー}</c> / <c>{DynamicResource キー}</c> が、
    /// <b>その画面から見える範囲</b>に定義されていること。
    ///
    /// キーの綴り違いも、別の画面にしか無いキーの参照も<b>ビルドを通る</b>。
    /// StaticResource は実行時に例外で落ち、DynamicResource は<b>黙ってスタイルが当たらない</b>。
    /// 後者は気付きにくく、実際 TableWindow の CSV 出力アイコンが
    /// 「他の画面にしか無い SvgIconStyle16」を参照していて、大きさの指定が効いていなかった。
    ///
    /// 画面ごとのスモークテストは 47 画面中 7 画面にしかない。この検査は XAML を読むだけなので、
    /// ウィンドウを開かずに全画面を一度に見られる。
    /// </summary>
    [TestClass]
    public class XamlResourceKeyScopeTests
    {
        private static readonly Regex KeyDef = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex KeyUse =
            new(@"\{(?:StaticResource|DynamicResource)\s+([^}\s,]+)\s*\}", RegexOptions.Compiled);
        private static readonly Regex DictSource =
            new(@"<ResourceDictionary[^>]*Source=""([^""]+)""", RegexOptions.Compiled);

        /// <summary>コードで動的に足しているキー（XAML には現れない）。</summary>
        private static readonly Regex CodeKeyDef =
            new(@"Resources\[\s*""([^""]+)""\s*\]\s*=", RegexOptions.Compiled);

        [TestMethod]
        public void EveryResourceKeyIsVisibleFromTheFileThatUsesIt()
        {
            string? root = FindSolutionRootOrNull();
            Assert.IsNotNull(root, "ソリューションルートが見つかりません");

            string appDir = Path.Combine(root!, "Graphics_r1");
            var xamls = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList();
            Assert.IsTrue(xamls.Count > 20, $"XAML が見つかりません ({xamls.Count} 件)");

            var byName = xamls.ToDictionary(p => Path.GetFileName(p), p => p, StringComparer.OrdinalIgnoreCase);
            var textCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Text(string path)
            {
                if (!textCache.TryGetValue(path, out string? t))
                {
                    t = File.ReadAllText(path);
                    textCache[path] = t;
                }
                return t;
            }

            HashSet<string> KeysOf(string path) =>
                [.. KeyDef.Matches(Text(path)).Select(m => m.Groups[1].Value)];

            IEnumerable<string> MergedInto(string path) =>
                DictSource.Matches(Text(path))
                    .Select(m => m.Groups[1].Value)
                    .Select(s => s.Contains(";component/", StringComparison.Ordinal)
                        ? s[(s.IndexOf(";component/", StringComparison.Ordinal) + ";component/".Length)..]
                        : s)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrEmpty(s))!;

            HashSet<string> Resolve(string fileName, HashSet<string> seen)
            {
                var keys = new HashSet<string>();
                if (!byName.TryGetValue(fileName, out string? path) || !seen.Add(fileName)) return keys;

                keys.UnionWith(KeysOf(path));
                foreach (string merged in MergedInto(path)) keys.UnionWith(Resolve(merged, seen));
                return keys;
            }

            // アプリケーション全体から見えるキー (App.xaml とそこにマージされた辞書)
            var appScope = Resolve("App.xaml", []);
            Assert.IsTrue(appScope.Count > 50,
                $"App.xaml からキーが辿れません ({appScope.Count} 件)。マージの書き方が変わっていないか");

            // コードで Resources[...] に足しているキーも定義済みとみなす
            foreach (string cs in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                foreach (Match m in CodeKeyDef.Matches(File.ReadAllText(cs)))
                    appScope.Add(m.Groups[1].Value);
            }

            var problems = new List<string>();
            foreach (string path in xamls)
            {
                if (string.Equals(Path.GetFileName(path), "App.xaml", StringComparison.OrdinalIgnoreCase)) continue;

                var scope = new HashSet<string>(appScope);
                scope.UnionWith(KeysOf(path));
                foreach (string merged in MergedInto(path)) scope.UnionWith(Resolve(merged, []));

                foreach (Match m in KeyUse.Matches(Text(path)))
                {
                    string key = m.Groups[1].Value;
                    if (key.StartsWith('{')) continue;   // {StaticResource {x:Static ...}}
                    if (scope.Contains(key)) continue;

                    problems.Add($"{Path.GetFileName(path)}: {key}");
                }
            }

            Assert.AreEqual(0, problems.Count,
                "この画面からは見えないリソースキーを参照しています。"
                + "共通で使うなら Styles.xaml へ、その画面だけなら画面の Resources へ置いてください:"
                + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", problems.Distinct()));
        }

        private static string? FindSolutionRootOrNull()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(XamlResourceKeyScopeTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
