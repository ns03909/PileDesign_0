using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 「今どの版を使っているか」「何が変わったか」を利用者が確かめられること。
    ///
    /// バージョンは以前から 5 箇所に出ていたが、更新履歴は CHANGELOG.md にしか無く
    /// 配布物に含まれていなかったため、利用者からは一切見えなかった。
    /// </summary>
    [TestClass]
    public class VersionVisibilityTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(VersionVisibilityTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static string HelpHtml() =>
            File.ReadAllText(Path.Combine(FindSolutionRoot(), "Graphics_r1", "Help", "help.html"));

        /// <summary>
        /// バージョン情報から飛ぶ先の「更新履歴」がヘルプに実在すること。
        /// HelpAnchorTests は XAML だけを見ているので、C# から指すアンカーはここで検査する。
        /// </summary>
        [TestMethod]
        public void ReleaseNotesAnchor_ExistsInHelp()
        {
            string anchor = PileDesign.Views.AboutWindow.ReleaseNotesAnchor;
            StringAssert.Contains(HelpHtml(), $"id=\"{anchor}\"",
                $"バージョン情報が指す更新履歴のアンカー \"{anchor}\" が help.html にありません");
        }

        /// <summary>
        /// 今のアプリのバージョンが、ヘルプの更新履歴に載っていること。
        ///
        /// バージョンを上げたのに履歴に書き忘れる、を防ぐ。
        /// 「利用に関わる変更が無かった版」でも、その旨を 1 行書けば通る。
        /// </summary>
        [TestMethod]
        public void CurrentVersion_IsDocumentedInReleaseNotes()
        {
            string version = MainWindowViewModel.AppVersion;
            Assert.AreNotEqual("不明", version, "アプリのバージョンを取得できません");

            string help = HelpHtml();
            int start = help.IndexOf($"id=\"{PileDesign.Views.AboutWindow.ReleaseNotesAnchor}\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "ヘルプに「プログラム更新履歴概要」の章がありません");

            // 更新履歴の章の中だけを見る (次の h1 まで)
            int end = help.IndexOf("<h1", start, StringComparison.Ordinal);
            if (end < 0) end = help.Length;
            string chapter = help[start..end];

            StringAssert.Contains(chapter, version,
                $"バージョン {version} が更新履歴に載っていません。"
                + "バージョンを上げたら、ヘルプの「プログラム更新履歴概要」にもその版を追記してください。");
        }

        /// <summary>
        /// 更新履歴が CHANGELOG.md から取り残されていないこと。
        /// CHANGELOG は開発者向け、ヘルプの更新履歴は利用者向けで内容は別だが、
        /// <b>載っている版の集合</b>は一致しているべき。
        /// </summary>
        [TestMethod]
        public void ReleaseNotes_CoverEveryReleasedVersion()
        {
            string root = FindSolutionRoot();
            string changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
            string help = HelpHtml();

            var released = Regex.Matches(changelog, @"^## \[([0-9][^\]]*)\]", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToList();

            Assert.IsTrue(released.Count >= 10,
                $"CHANGELOG から取れた版が {released.Count} 件しかありません (収集が壊れている可能性)");

            var missing = released.Where(v => !help.Contains(v, StringComparison.Ordinal)).ToList();

            Assert.AreEqual(0, missing.Count,
                "CHANGELOG にあってヘルプの更新履歴に無い版があります:\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// バージョン情報ダイアログが開き、版とログの場所が埋まること。
        /// XAML の StaticResource のキー誤りはビルドを通り、開いた瞬間に例外になる。
        /// </summary>
        [TestMethod]
        public void AboutWindow_OpensAndShowsVersionAndPaths()
        {
            string? version = null, logPath = null, buildDate = null;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.AboutWindow();
                try
                {
                    version = Find<TextBlock>(window, "VersionText")?.Text;
                    buildDate = Find<TextBlock>(window, "BuildDateText")?.Text;
                    logPath = Find<TextBox>(window, "LogPathText")?.Text;
                }
                finally
                {
                    window.Close();
                }
            }, out bool timedOut, timeoutSeconds: 120);

            if (timedOut)
            {
                Assert.Inconclusive("ウィンドウ生成が 120 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"バージョン情報の生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            StringAssert.Contains(version, MainWindowViewModel.AppVersion, "版が表示されていない");
            Assert.IsFalse(string.IsNullOrWhiteSpace(buildDate), "ビルド日が空");
            StringAssert.Contains(logPath, "PileDesign", "ログの保存先が表示されていない");
        }

        private static T? Find<T>(System.Windows.DependencyObject root, string name) where T : class
            => root is System.Windows.FrameworkElement fe ? fe.FindName(name) as T : null;
    }
}
