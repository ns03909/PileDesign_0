using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// UI の「ヘルプ」リンク（Hyperlink Tag=アンカー id → HelpLink_Click）と
    /// help.html 側のアンカー（id 属性）の対応を検査する。
    /// アンカー名の typo や help.html 側の id 削除があると、リンクは「ヘルプは開くが
    /// スクロールしない」形で静かに壊れるため、ビルド時に検出する（HelpCoverageTests と同趣旨）。
    /// </summary>
    [TestClass]
    public class HelpAnchorTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(HelpAnchorTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません (Graphics_r1/Help/help.html)");
        }

        [TestMethod]
        public void HelpLinkAnchors_ExistInHelpHtml()
        {
            string root = FindSolutionRoot();
            string helpHtml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Help", "help.html"));

            // XAML から HelpLink_Click 付き Hyperlink の Tag（アンカー名）を収集
            // 注: Tag と Click が同一行にある前提（現状の記法）。別行に分かれた場合は検出対象から
            //     外れるが、下の「1 つ以上存在」検査で検査自体の消失は検出できる。
            var anchors = new List<(string File, string Anchor)>();
            foreach (string xaml in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1", "Views"), "*.xaml"))
            {
                foreach (string line in File.ReadLines(xaml))
                {
                    if (!line.Contains("HelpLink_Click")) continue;
                    var m = Regex.Match(line, "Tag=\"([^\"]+)\"");
                    if (m.Success)
                        anchors.Add((Path.GetFileName(xaml), m.Groups[1].Value));
                }
            }

            Assert.IsTrue(anchors.Count > 0,
                "HelpLink_Click 付きの Hyperlink が 1 つも見つかりません（記法変更で検査対象が消失した可能性）");

            var missing = anchors
                .Where(a => !helpHtml.Contains($"id=\"{a.Anchor}\""))
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "help.html に存在しないアンカーを参照するヘルプリンクがあります:\n" +
                string.Join("\n", missing.Select(a => $"  ・ {a.File}: Tag=\"{a.Anchor}\"")) +
                "\n→ help.html に id を追加するか、XAML の Tag を修正してください。");
        }
    }
}
