using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 無効なリボンボタンでも、押せない理由が読めること。
    ///
    /// WPF の ToolTip は既定で<b>無効なコントロールには出ない</b>。
    /// 「押す前に分かる」ためにボタンを灰色にしても、その理由を書いた ToolTip が
    /// まさに読みたいときに出ないのでは意味がない。
    /// <c>ToolTipService.ShowOnDisabled="True"</c> を付けて初めて読める。
    ///
    /// リボンでは 37 個のボタンが「コマンドを持つ = 無効になりうる」うえに
    /// ToolTip を持っていたが、そのうち 35 個でこの指定が抜けていた。
    /// </summary>
    [TestClass]
    public class RibbonDisabledTooltipTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(RibbonDisabledTooltipTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void RibbonButtonsWithACommandShowTheirTooltipWhenDisabled()
        {
            string xaml = File.ReadAllText(
                Path.Combine(FindSolutionRoot(), "Graphics_r1", "Views", "MainWindow.xaml"));

            var openingTag = new Regex(@"<Fluent:(?:Toggle)?Button\b[^>]*?/?>", RegexOptions.Singleline);

            var offenders = new List<string>();
            int checkedCount = 0;

            foreach (Match m in openingTag.Matches(xaml))
            {
                string tag = m.Value;

                // ToolTip が無いものは対象外 (読ませる文が無い)
                if (!tag.Contains("ToolTip=", StringComparison.Ordinal)) continue;
                // Command が無いものは無効にならない
                if (!tag.Contains("Command=", StringComparison.Ordinal)) continue;

                checkedCount++;
                if (!tag.Contains("ShowOnDisabled", StringComparison.Ordinal))
                {
                    var name = Regex.Match(tag, @"x:Name=""([^""]+)""");
                    var tip = Regex.Match(tag, @"ToolTip=""([^""]{0,40})");
                    offenders.Add(name.Success ? name.Groups[1].Value
                                : tip.Success ? tip.Groups[1].Value + "..."
                                : tag[..Math.Min(60, tag.Length)]);
                }
            }

            Assert.IsTrue(checkedCount > 20,
                $"検査対象が {checkedCount} 個しかない (抽出が壊れている可能性)");
            Assert.AreEqual(0, offenders.Count,
                "無効になりうるのに、無効時は ToolTip が出ないボタンがあります "
                + "(ToolTipService.ShowOnDisabled=\"True\" を付けてください):\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
