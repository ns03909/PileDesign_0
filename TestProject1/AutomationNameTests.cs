using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// アイコンだけのボタンに<b>読み上げ用の名前</b>があること。
    ///
    /// 文字を持たないボタンは、名前を付けないと UI オートメーションからは
    /// 「名前の無いボタン」になる。スクリーンリーダーからも、UIA を使った操作記録からも
    /// 区別が付かない。
    ///
    /// 名前の出どころは <c>Header</c>（リボンの短い表示名）を優先し、
    /// 無ければ <c>ToolTip</c> の 1 行目を使う。
    /// <c>Content</c> に文字を持つボタンは WPF が本文から名前を作るので対象外。
    /// </summary>
    [TestClass]
    public class AutomationNameTests
    {
        private static readonly Regex ButtonTag = new(
            @"<(Fluent:Button|Fluent:ToggleButton|Fluent:DropDownButton|Fluent:SplitButton|Button|ToggleButton|RepeatButton)\b[^>]*?/?>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        [TestMethod]
        public void IconOnlyButtonsHaveAnAutomationName()
        {
            string root = FindSolutionRoot();

            var missing = new List<string>();
            foreach (string xaml in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1"), "*.xaml", SearchOption.AllDirectories))
            {
                if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                string text = File.ReadAllText(xaml);
                foreach (Match m in ButtonTag.Matches(text))
                {
                    string tag = m.Value;
                    if (tag.Contains("AutomationProperties.Name", StringComparison.Ordinal)) continue;

                    // 本文に文字があるものは WPF が名前を作る
                    if (Regex.IsMatch(tag, @"\bContent=""[^""{]")) continue;

                    // 名前の材料があるのに付けていないものだけを咎める。
                    // 材料が何も無いボタンは、文言を決める判断が要るのでここでは扱わない。
                    if (!Regex.IsMatch(tag, @"\bHeader=""[^""{]") && !Regex.IsMatch(tag, @"\bToolTip=""[^""{]"))
                        continue;

                    int line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    missing.Add($"{Path.GetFileName(xaml)}:{line}");
                }
            }

            Assert.AreEqual(0, missing.Count,
                "アイコンだけのボタンに読み上げ用の名前がありません。"
                + "AutomationProperties.Name を付けてください (Header 優先、無ければ ToolTip の 1 行目):"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(AutomationNameTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }
    }
}
