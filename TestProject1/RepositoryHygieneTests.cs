using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// リポジトリに残ってはいけないもの。
    ///
    /// 消し忘れのファイルは、検索でひっかかって<b>生きているコードと取り違えられる</b>。
    /// 式が誤ったまま放置された死にコードは、そのまま再利用されると被害が出る。
    /// </summary>
    [TestClass]
    public class RepositoryHygieneTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(RepositoryHygieneTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>控え・退避のファイルを置いたままにしないこと。</summary>
        [TestMethod]
        public void NoBackupFilesAreKept()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");

            var leftovers = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Where(f => f.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".orig", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(root, f))
                .ToList();

            Assert.AreEqual(0, leftovers.Count,
                "控えのファイルが残っています (検索で本物と紛れます):\n  " + string.Join("\n  ", leftovers));
        }

        /// <summary>
        /// <c>Compile Remove</c> が実在しないファイルを指していないこと。
        ///
        /// 指す先が無い行は、消したはずのファイルがまだ除外されているように見せる。
        /// 逆に「除外したつもりで実は取り込まれている」場合もここで気付ける。
        /// </summary>
        [TestMethod]
        public void CompileRemoveEntriesPointAtRealFiles()
        {
            string project = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            string csproj = File.ReadAllText(Path.Combine(project, "PileDesign.csproj"));

            var missing = new List<string>();
            foreach (Match m in Regex.Matches(csproj, @"<Compile\s+Remove=""([^""]+)""\s*/>"))
            {
                string rel = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                if (rel.Contains('*')) continue;   // ワイルドカードは対象外

                if (!File.Exists(Path.Combine(project, rel)))
                    missing.Add(rel);
            }

            Assert.AreEqual(0, missing.Count,
                "Compile Remove が実在しないファイルを指しています:\n  " + string.Join("\n  ", missing));
        }
    }
}
