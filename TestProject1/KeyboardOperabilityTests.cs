using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// キーボードだけで操作できること: どのウィンドウも Esc に応える・入力部品をタブで飛ばさない・
    /// 同じウィンドウのボタンのアクセスキー (Alt+文字) が重ならない。
    ///
    /// <para>2026-10-02 に全ウィンドウを調べたとき、計算の進み具合の窓 (キャンセルのボタンがあるのに Esc が効かない)・
    /// 群杭沈下解析 (一般)・Chang の式・断面のひずみ度分布・起動時の案内で Esc が何もしなかった。</para>
    /// </summary>
    [TestClass]
    public class KeyboardOperabilityTests
    {
        /// <summary>Esc に応えなくてよいウィンドウと、その理由。</summary>
        private static readonly Dictionary<string, string> NoEscape = new()
        {
            ["LoadingMainWindow.xaml"] = "起動中の表示 (操作を受け付ける前に閉じる)",
            ["ParallelMonitorWindow.xaml"] = "解析中の進み具合の表示 (解析の窓の側で止める)",
        };

        private static IEnumerable<(string Name, string Xaml, string Code)> Windows()
        {
            string views = Path.Combine(TestSource.Dir("Graphics_r1"), "Views");
            foreach (var path in Directory.GetFiles(views, "*.xaml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                string xaml = File.ReadAllText(path);
                // 一番外の要素が Window のもの (UserControl・リソース辞書は除く)
                var root = Regex.Match(xaml, @"<(?!\?|!)([\w:]+)");
                if (!root.Success || !root.Groups[1].Value.EndsWith("Window", StringComparison.Ordinal) || root.Groups[1].Value.Contains("Window.")) continue;
                string code = File.Exists(path + ".cs") ? File.ReadAllText(path + ".cs") : "";
                yield return (Path.GetFileName(path), xaml, code);
            }
        }

        [TestMethod]
        public void EveryWindow_RespondsToEscape()
        {
            var missing = new List<string>();
            int scanned = 0;
            foreach (var (name, xaml, code) in Windows())
            {
                scanned++;
                bool responds = xaml.Contains("EscapeKeyBehavior.Enable=\"True\"", StringComparison.Ordinal)
                    || xaml.Contains("IsCancel=\"True\"", StringComparison.Ordinal)
                    || Regex.IsMatch(xaml, @"Key=""Esc(ape)?""")
                    || code.Contains("Key.Escape", StringComparison.Ordinal);
                if (responds && NoEscape.ContainsKey(name)) missing.Add($"{name}: Esc に応えるようになったので、除外の一覧から外してください");
                if (!responds && !NoEscape.ContainsKey(name)) missing.Add($"{name}: Esc に応えません (EscapeKeyBehavior.Enable か、キャンセル・閉じるのボタンに IsCancel を付ける)");
            }
            TestSource.AssertScanned(scanned, 40, "ウィンドウ");
            Assert.AreEqual(0, missing.Count, string.Join("\n", missing));
        }

        /// <summary>キャンセル・閉じるのボタンがあるウィンドウで Esc が「閉じる」以外の意味 (ボタンを押す) を持つなら、そのボタンに IsCancel を付ける。</summary>
        [TestMethod]
        public void TheProgressWindow_CancelsWithEscape()
        {
            var progress = Windows().Single(w => w.Name == "ProgressWindow.xaml");
            // 共通の「Esc で閉じる」は切ってある (計算中に閉じさせない) ので、キャンセルのボタンが Esc を受ける
            StringAssert.Contains(progress.Xaml, "EscapeKeyBehavior.Enable=\"False\"");
            var cancel = Regex.Match(progress.Xaml, @"<Button\b[^>]*Content=""キャンセル""[^>]*>", RegexOptions.Singleline);
            Assert.IsTrue(cancel.Success, "キャンセルのボタンが見つかりません");
            StringAssert.Contains(cancel.Value, "IsCancel=\"True\"");
        }

        [TestMethod]
        public void InputControls_AreNotSkippedByTab()
        {
            var skipped = new List<string>();
            int controls = 0;
            foreach (var (name, xaml, _) in Windows())
                foreach (Match m in Regex.Matches(xaml, @"<(TextBox|ComboBox|Button|CheckBox|RadioButton|DataGrid)\b[^>]*>", RegexOptions.Singleline))
                {
                    controls++;
                    if (Regex.IsMatch(m.Value, @"(IsTabStop|Focusable)=""False"""))
                        skipped.Add($"{name}: {m.Groups[1].Value} がタブで止まりません");
                }
            TestSource.AssertScanned(controls, 300, "入力部品");
            Assert.AreEqual(0, skipped.Count, string.Join("\n", skipped));
        }

        [TestMethod]
        public void AccessKeys_DoNotCollideWithinAWindow()
        {
            var collisions = new List<string>();
            int keys = 0;
            foreach (var (name, xaml, _) in Windows())
            {
                var labels = Regex.Matches(xaml, @"<(?:Button|CheckBox|RadioButton)\b[^>]*?Content=""([^""]*\(_([A-Za-z0-9])\)[^""]*)""", RegexOptions.Singleline)
                    .Select(m => (Label: m.Groups[1].Value, Key: char.ToUpperInvariant(m.Groups[2].Value[0])))
                    .ToList();
                keys += labels.Count;
                foreach (var g in labels.GroupBy(l => l.Key).Where(g => g.Count() > 1))
                    collisions.Add($"{name}: Alt+{g.Key} が {string.Join("・", g.Select(l => l.Label))} で重なっています");
            }
            TestSource.AssertScanned(keys, 20, "アクセスキー");
            Assert.AreEqual(0, collisions.Count, string.Join("\n", collisions));
        }
    }
}
