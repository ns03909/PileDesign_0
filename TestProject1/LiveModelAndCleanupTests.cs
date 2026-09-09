using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 「起動時に作った器が、そのまま取り残される」型の欠陥をまとめて押さえる。
    ///
    /// <list type="number">
    /// <item><c>App.InputModel</c> が起動時の空モデルを持ち続け、標高の入力欄が
    ///   その基準標高で Z へ逆変換していた</item>
    /// <item>ケース並列のキャンセルで、走行中のケースを待たずに同期オブジェクトを捨て、
    ///   その解放が未観測例外になってアプリが無言で終了していた</item>
    /// <item>緊急保存ファイルの再表示防止が自動保存の名前しか置換できず、
    ///   一度落ちると 24 時間、起動のたびに同じ復元確認が出ていた</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class LiveModelAndCleanupTests
    {
        // ───────── (1) 標高の書き戻し ─────────

        /// <summary>
        /// <c>App.InputModel</c> は、いま編集しているモデルを返すこと。
        ///
        /// 起動時に別インスタンスを作って持ち続けていたため、ファイルを開いても
        /// 計算例を読んでも中身は空のままだった。
        /// </summary>
        [TestMethod]
        public void AppInputModel_FollowsTheModelBeingEdited()
        {
            var vm = new MainWindowViewModel();
            PileDesign.App.CurrentMainViewModel = vm;
            try
            {
                Assert.AreSame(vm.CurrentInputModel, PileDesign.App.InputModel,
                    "App.InputModel が編集中のモデルを指していない");

                // ファイルを開いたときと同じように差し替える
                var loaded = new InputModel();
                vm.CurrentInputModel = loaded;

                Assert.AreSame(loaded, PileDesign.App.InputModel,
                    "モデルを差し替えても App.InputModel が古いままになっている");
            }
            finally
            {
                PileDesign.App.CurrentMainViewModel = null;
            }
        }

        /// <summary>
        /// 標高の逆変換が、いまの基準標高を使うこと。
        ///
        /// 表示は「Z + 基準標高」で正しく出るのに、書き戻しだけ基準標高 0 で計算していた。
        /// そのため基準標高を 0 以外にしていると、入力した標高がそのまま Z として保存され、
        /// 表示は直後に基準標高ぶんずれた値へ変わっていた。
        /// </summary>
        [TestMethod]
        public void EnteringAnElevation_ConvertsWithTheCurrentReferenceAltitude()
        {
            var vm = new MainWindowViewModel();
            PileDesign.App.CurrentMainViewModel = vm;
            try
            {
                vm.CurrentInputModel!.FundamentalInput.ReferenceAltitude = 10.0;

                var converter = new PileDesign.Converters.ZElevationConverter();
                var back = converter.ConvertBack(8.5, [typeof(double), typeof(double)], null!, CultureInfo.InvariantCulture);

                Assert.AreEqual(-1.5, (double)back[0], 1e-9,
                    "標高 8.5 は 基準標高 10.0 のもとで Z = -1.5。"
                    + "基準標高 0 で計算すると 8.5 がそのまま保存される");

                // 表示側と往復すること
                var shown = converter.Convert([(double)back[0], 10.0], typeof(double), null!, CultureInfo.InvariantCulture);
                Assert.AreEqual(8.5, (double)shown, 1e-9, "入力した標高が表示に戻らない");
            }
            finally
            {
                PileDesign.App.CurrentMainViewModel = null;
            }
        }

        // ───────── (2) ケース並列の後始末 ─────────

        /// <summary>
        /// 同期オブジェクトを捨てる前に、走行中のケースを待つこと。
        ///
        /// 待たずに捨てると、そのケースの解放が「破棄済み」例外になる。
        /// この Task は誰も待っていないので未観測例外となり、
        /// App の未観測ハンドラが良性一覧に無い例外として続行不可へ流す。
        /// キャンセルとは無関係に見えるタイミングでアプリが落ちる。
        /// </summary>
        [TestMethod]
        public void CaseCleanup_WaitsForRunningCasesBeforeDisposing()
        {
            var body = ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");

            int disposeAt = body.IndexOf("_caseSemaphore?.Dispose();", StringComparison.Ordinal);
            Assert.IsTrue(disposeAt >= 0, "semaphore の破棄が見つからない");

            // 後始末ブロックの中だけを見る。正常系の待機 (try の側) を数えてしまうと、
            // 後始末で待たなくなっても検査が通ってしまう。
            int finallyAt = body.LastIndexOf("finally", disposeAt, StringComparison.Ordinal);
            Assert.IsTrue(finallyAt >= 0, "後始末ブロックが見つからない");

            string cleanupBlock = body[finallyAt..disposeAt];
            StringAssert.Contains(cleanupBlock, "Task.WhenAll(_caseTasks)",
                "走行中のケースを待たずに semaphore を捨てている。"
                + "キャンセル後、無関係なタイミングでアプリが落ちる");
        }

        /// <summary>
        /// タスクのキャンセルは良性として握るのに、破棄済み例外は握らない。
        /// だから (2) は「握り潰す」ではなく「起こさない」で直す必要がある、という確認。
        /// </summary>
        [TestMethod]
        public void ADisposedObjectException_IsNotTreatedAsBenign()
        {
            var source = ReadSource("Graphics_r1", "App.xaml.cs");
            int at = source.IndexOf("private static bool IsBenignSuppressible", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "良性判定が見つからない");

            string body = source[at..];
            Assert.IsFalse(body.Contains("ObjectDisposedException"),
                "破棄済み例外が良性として握られるようになった。"
                + "後始末の順序で防ぐ設計と食い違っていないか確認すること");
        }

        // ───────── (3) 緊急保存の再表示防止 ─────────

        /// <summary>
        /// 復元確認を見送ったファイルは、自動保存でも緊急保存でも印が付くこと。
        ///
        /// 置換の形をそのまま検証する。以前は "_autosave_" だけを見ていたので、
        /// 緊急保存のファイル名は 1 文字も変わらなかった。
        /// </summary>
        [TestMethod]
        public void DismissingARestore_MarksBothKindsOfFile()
        {
            const string pattern = "_(autosave|emergency)_";
            const string replacement = "_$1_dismissed_";

            foreach (var (original, expected) in new[]
            {
                (@"C:\A\Foo_autosave_20260909_101112.pdj",  @"C:\A\Foo_autosave_dismissed_20260909_101112.pdj"),
                (@"C:\A\Foo_emergency_20260909_101112.pdj", @"C:\A\Foo_emergency_dismissed_20260909_101112.pdj"),
            })
            {
                var renamed = Regex.Replace(original, pattern, replacement);

                Assert.AreEqual(expected, renamed, $"{original} の印の付け方が違う");
                StringAssert.Contains(renamed, "_dismissed_",
                    "印が付かない。候補を探す側が拾い続け、起動のたびに同じ確認が出る");
            }
        }

        /// <summary>実際の復元処理が、その置換を使っていること。</summary>
        [TestMethod]
        public void TheRestorePrompt_UsesThatReplacement()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");

            StringAssert.Contains(source, "\"_(autosave|emergency)_\"",
                "復元の見送りが自動保存の名前しか置換していない。緊急保存が消えない");
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
    }
}
