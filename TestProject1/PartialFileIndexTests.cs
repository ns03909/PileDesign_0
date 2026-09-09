using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 分けたファイルの一覧が、実物と合っていること。
    ///
    /// <c>MainWindowViewModel</c> は 21 個の <c>partial</c> に分かれている。
    /// どこに何があるかは、本体ファイル先頭の一覧が頼り。
    /// <b>足したのに書かなければ、一覧は嘘になる。</b> 一覧が嘘になると、
    /// 探す人は結局すべてのファイルを開くことになり、分けた意味がなくなる。
    ///
    /// 実際、分ける前の一覧には <c>MainWindowViewModel.TreeView.cs</c> という
    /// 存在しないファイルが載っていた。逆に、あとから足した 8 個は載っていなかった。
    /// </summary>
    [TestClass]
    public class PartialFileIndexTests
    {
        [TestMethod]
        public void TheIndex_ListsEveryPartialFile()
        {
            var dir = TestSource.Dir("Graphics_r1", "ViewModels");
            var files = Directory.GetFiles(dir, "MainWindowViewModel*.cs")
                .Select(Path.GetFileName)
                .Where(n => n != "MainWindowViewModel.cs")
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            TestSource.AssertScanned(files.Count, 15, "MainWindowViewModel の分割ファイル");

            var index = File.ReadAllText(Path.Combine(dir, "MainWindowViewModel.cs"));
            // 一覧は本体先頭の <summary> にある。クラス宣言より後ろは見ない。
            int classAt = index.IndexOf("public partial class MainWindowViewModel", StringComparison.Ordinal);
            Assert.IsTrue(classAt > 0, "クラス宣言が見つかりません");
            string head = index[..classAt];

            var missing = files
                .Where(name => !head.Contains(name!.Replace("MainWindowViewModel", ""), StringComparison.Ordinal))
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "分けたファイルが本体先頭の一覧に載っていません。"
                + "どこに何があるか分からなくなるので、足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        [TestMethod]
        public void TheIndex_HasNoStaleEntries()
        {
            var dir = TestSource.Dir("Graphics_r1", "ViewModels");
            var index = File.ReadAllText(Path.Combine(dir, "MainWindowViewModel.cs"));
            int classAt = index.IndexOf("public partial class MainWindowViewModel", StringComparison.Ordinal);
            string head = index[..classAt];

            // 一覧に書かれている「.Xxx.cs」を拾う
            var listed = System.Text.RegularExpressions.Regex
                .Matches(head, @"\.([A-Za-z]+)\.cs")
                .Select(m => "MainWindowViewModel" + m.Value)
                .Distinct()
                .ToList();

            Assert.IsTrue(listed.Count >= 15, $"一覧から {listed.Count} 個しか読み取れません");

            var stale = listed
                .Where(name => !File.Exists(Path.Combine(dir, name)))
                .ToList();

            Assert.AreEqual(0, stale.Count,
                "一覧に、もう無いファイルが載っています。探しに行った先が空になります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stale));
        }

        /// <summary>
        /// 分けたはずのファイルが、また 4,000 行級に育っていないこと。
        ///
        /// 分けても、次から次へと同じファイルに足していけば元に戻る。
        /// 上限に当たったら、名前で分けられるまとまりを探すこと。
        ///
        /// ViewModels と Views の両方を見る。画面のコードビハインドも <c>partial</c> なので、
        /// 同じように分けられる (MainWindow.xaml.cs は 4,090 行あった)。
        /// </summary>
        [TestMethod]
        public void NoScreenFile_GrowsBackToFourThousandLines()
        {
            const int Limit = 3500;
            var oversized = new List<string>();
            int scanned = 0;

            foreach (var dir in new[] { "ViewModels", "Views" })
                foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", dir), "*.cs"))
                {
                    scanned++;
                    int lines = File.ReadAllLines(file).Length;
                    if (lines > Limit)
                        oversized.Add($"{dir}/{Path.GetFileName(file)}: {lines:N0} 行");
                }

            TestSource.AssertScanned(scanned, 60, "画面まわりのソース");
            Assert.AreEqual(0, oversized.Count,
                $"{Limit:N0} 行を超えたファイルがあります。名前で分けられるまとまりを探してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", oversized));
        }
    }
}
