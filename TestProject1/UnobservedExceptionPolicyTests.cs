using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 未観測の非同期例外でアプリを終了しないこと。
    ///
    /// 誰も受け取らなかった <c>Task</c> の例外は、.NET Core ではプロセスを落とさないのが仕様。
    /// しかもこの通知は「その例外がごみ集めで回収されたとき」に来るので、原因となった処理が
    /// 終わったずっと後、無関係な操作の最中に届く。それを致命的エラーとして終了に流すと
    ///
    /// <list type="bullet">
    /// <item>利用者には、直前の操作と無関係なタイミングで落ちたように見える</item>
    /// <item>通知はファイナライザのスレッドで来るため、親ウィンドウを決める走査が例外になり、
    ///   <b>ダイアログすら出ないまま</b>終了することがある</item>
    /// </list>
    ///
    /// という形になる。実際、ケース並列の解析をキャンセルするとこの経路に入っていた。
    /// 記録と作業の退避までにとどめて続行する。
    ///
    /// 例外ハンドラはアプリ起動が要るので単体では動かせない。方針をソースで押さえる。
    /// </summary>
    [TestClass]
    public class UnobservedExceptionPolicyTests
    {
        /// <summary>
        /// 未観測の例外を、致命的エラーの共通処理へ流さないこと。
        /// あそこへ流すと続行不可としてプロセスを終了する。
        /// </summary>
        [TestMethod]
        public void AnUnobservedException_DoesNotGoToTheFatalPath()
        {
            var body = ExtractMethodBody(ReadAppSource(),
                "private void TaskScheduler_UnobservedTaskException(");

            Assert.IsFalse(body.Contains("HandleFatalException"),
                "未観測の例外を致命的エラーへ流している。無関係なタイミングでアプリが終了する");
            StringAssert.Contains(body, "e.SetObserved();",
                "観測済みにしていない。CLR の設定次第でプロセスが落ちる");
        }

        /// <summary>記録は残すこと。黙って捨てると原因が追えない。</summary>
        [TestMethod]
        public void AnUnobservedException_IsStillRecorded()
        {
            var body = ExtractMethodBody(ReadAppSource(),
                "private void TaskScheduler_UnobservedTaskException(");

            StringAssert.Contains(body, "Log.Error(e.Exception",
                "未観測の例外が記録されていない");
        }

        /// <summary>作業の退避は試みること。</summary>
        [TestMethod]
        public void AnUnobservedException_StillTriesTheEmergencySave()
        {
            var body = ExtractMethodBody(ReadAppSource(),
                "private void TaskScheduler_UnobservedTaskException(");

            StringAssert.Contains(body, "TryEmergencyAutoSave",
                "未観測の例外で作業の退避を試みていない");
        }

        /// <summary>
        /// 致命的エラーのダイアログを画面のスレッドで出すこと。
        ///
        /// 親ウィンドウを決める走査は画面のスレッドからしか触れない。
        /// 別のスレッドから呼ぶと例外になり、ダイアログが出ないまま終了する。
        /// </summary>
        [TestMethod]
        public void TheFatalDialog_IsShownOnTheUiThread()
        {
            var source = ReadAppSource();

            var body = ExtractMethodBody(source, "private static MessageBoxResult ShowFatalDialog(");
            StringAssert.Contains(body, "dispatcher.CheckAccess()",
                "呼び出し元のスレッドを見ていない");
            StringAssert.Contains(body, "dispatcher.Invoke(",
                "画面のスレッドへ載せ替えていない");
            StringAssert.Contains(body, "TimeSpan.FromSeconds(",
                "待ち時間の上限が無い。画面のスレッドが止まっていると永久に待つ");

            var fatal = ExtractMethodBody(source,
                "private void HandleFatalException(Exception ex, string source, bool canContinue = false)");
            StringAssert.Contains(fatal, "ShowFatalDialog(msg, title)",
                "致命的エラーがダイアログを直接出している (呼び出し元のスレッドのまま)");
        }

        // ── ソース走査の道具 ──

        private static string ReadAppSource()
        {
            var root = FindSolutionRoot();
            return File.ReadAllText(Path.Combine(root, "Graphics_r1", "App.xaml.cs"));
        }

        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

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
