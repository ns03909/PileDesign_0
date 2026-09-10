using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 画面が使う ViewModel は 1 つで、自動保存もそれだけが回すこと。
    ///
    /// 起動時に <see cref="PileDesign.ViewModels.MainWindowViewModel"/> が<b>3 つ</b>作られていた。
    /// アプリのコンストラクタで 1 つ、メイン画面の XAML で 1 つ、その code-behind で 1 つ。
    /// 画面が実際に使うのは 3 つ目で、
    ///
    /// <list type="bullet">
    /// <item>アプリが握っていたのは 1 つ目 (誰も使わない空のモデル)。
    ///   緊急保存も標高の逆変換もそちらを見ていた</item>
    /// <item>自動保存を「起動時にも始める」ようにした結果、3 つとも別々に回りはじめ、
    ///   同じ <c>Untitled_autosave_&lt;秒&gt;.pdj</c> を取り合って
    ///   一時ファイルの作成が「別のプロセスが使用中」で落ちた (実機で発生)</item>
    /// </list>
    ///
    /// 生成は 1 つにし、自動保存を始めるのは画面が使う ViewModel だけにする。
    /// </summary>
    [TestClass]
    public class SingleLiveViewModelTests
    {
        /// <summary>
        /// 起動時に ViewModel を作るのは、メイン画面の code-behind 1 か所だけであること。
        /// </summary>
        [TestMethod]
        public void OnlyOnePlaceCreatesTheMainViewModel()
        {
            var app = ReadSource("Graphics_r1", "App.xaml.cs");
            Assert.IsFalse(app.Contains("new MainWindowViewModel()"),
                "アプリが ViewModel を作っている。画面が使うものと別になり、"
                + "緊急保存と標高の逆変換が空のモデルを見る");

            var xaml = ReadSource("Graphics_r1", "Views", "MainWindow.xaml");
            Assert.IsFalse(xaml.Contains("<local:MainWindowViewModel/>"),
                "XAML が ViewModel を作っている。直後に上書きされるので二重に初期化される");
        }

        /// <summary>
        /// 画面が使う ViewModel を、アプリ側の参照として登録すること。
        /// 緊急保存と <c>App.InputModel</c> の宛先になる。
        /// </summary>
        [TestMethod]
        public void TheLiveViewModel_IsRegisteredWithTheApp()
        {
            var codeBehind = ReadSource("Graphics_r1", "Views", "MainWindow.xaml.cs");

            StringAssert.Contains(codeBehind, "App.CurrentMainViewModel = _mainWindowViewModel;",
                "画面が使う ViewModel がアプリに登録されていない。"
                + "緊急保存が空のモデルを書き、標高の逆変換も基準標高 0 のままになる");
        }

        /// <summary>
        /// 自動保存を始めるのは画面だけで、ViewModel のコンストラクタでは始めないこと。
        ///
        /// コンストラクタで始めると、テストなどで作られた分まで回る。
        /// </summary>
        [TestMethod]
        public void TheAutoSaveSession_IsStartedByTheWindowOnly()
        {
            var ctor = ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.Constructor.cs");
            var body = ExtractMethodBody(ctor, "public MainWindowViewModel()");
            Assert.IsFalse(body.Contains("_autoSaveService.Start"),
                "ViewModel のコンストラクタで自動保存を始めている。"
                + "画面が使わない分まで回り、一時ファイルを取り合う");

            var codeBehind = ReadSource("Graphics_r1", "Views", "MainWindow.xaml.cs");
            StringAssert.Contains(codeBehind, "BeginAutoSaveSession()",
                "画面が自動保存を始めていない。起動して新規に入力しただけの状態が守られない");
        }

        /// <summary>
        /// 同じ秒に 2 回書いても一時ファイルを取り合わないこと。
        /// 自動保存と緊急保存が重なる場合がこれにあたる。
        /// </summary>
        [TestMethod]
        public void WritesAreSerialised()
        {
            var source = ReadSource("Graphics_r1", "Services", "AutoSaveService.cs");
            var body = ExtractMethodBody(source, "private string? SaveSnapshot(string tag, PreparedState prepared)");

            StringAssert.Contains(body, "lock (_saveLock)",
                "書き出しが重なりうる。ファイル名は秒までなので同じ一時ファイルを取り合う");
        }

        // ── ソース走査の道具 ──

        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

        private static string ReadSource(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = FindSolutionRoot();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return File.ReadAllText(Path.Combine(parts));
        }

        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");

            int open = source.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source[open..(i + 1)];
                }
            }
            Assert.Fail($"本体の閉じ括弧が見つかりません: {signatureFragment}");
            return "";
        }
    }
}
