using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 選択肢を <c>&lt;ComboBoxItem Content="…"/&gt;</c> で並べたコンボボックスを、
    /// <c>SelectedItem</c> で文字列のプロパティに結んでいないこと。
    ///
    /// その形だと選んだ項目 (ComboBoxItem そのもの) が文字列に変換されて入り、値が
    /// 「System.Windows.Controls.ComboBoxItem: キャプリングパイル工法」になる。
    /// Chang の画面の杭頭タイプがこれで、どの工法名とも一致しないため、
    /// 工法ごとの欄 (PC リングなど) が一度も表示できなかった。例外にはならない。
    /// 文字列で持つなら <c>SelectedValue</c> + <c>SelectedValuePath="Content"</c> にする。
    /// </summary>
    [TestClass]
    public class ComboBoxItemBindingTests
    {
        [TestMethod]
        public void ItemListsAreNotBoundBySelectedItem()
        {
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                string xaml = File.ReadAllText(file);

                // 自己終了のもの (<ComboBox ... />) は選択肢を持たないので見ない
                foreach (Match m in Regex.Matches(xaml, @"<ComboBox\b([^>]*?)(/?)>", RegexOptions.Singleline))
                {
                    if (m.Groups[2].Value == "/") continue;
                    int end = xaml.IndexOf("</ComboBox>", m.Index + m.Length, System.StringComparison.Ordinal);
                    if (end < 0) continue;
                    string body = xaml[(m.Index + m.Length)..end];
                    if (!body.Contains("<ComboBoxItem")) continue;
                    scanned++;

                    string head = m.Groups[1].Value;
                    if (head.Contains("SelectedItem=\"{Binding") && !head.Contains("SelectedValuePath"))
                    {
                        int line = xaml[..m.Index].Split('\n').Length;
                        offenders.Add($"{Path.GetFileName(file)}:{line}");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 3, "ComboBoxItem で選択肢を並べたコンボボックス");
            Assert.AreEqual(0, offenders.Count,
                "ComboBoxItem の選択肢を SelectedItem で結んでいます。値が「System.Windows.Controls.ComboBoxItem: …」になります。"
                + "SelectedValue と SelectedValuePath=\"Content\" にしてください:\n  " + string.Join("\n  ", offenders));
        }
    }
}
