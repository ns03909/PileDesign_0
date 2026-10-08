using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;

namespace TestProject1
{
    /// <summary>
    /// テストの実行中は<b>ダイアログを出さない</b>。
    ///
    /// <para>回帰テストは水平解析の入口 (<c>HorizontalCalculationViewModel</c>) を通る。
    /// そこには <c>MessageService.Show</c> が 20 か所あり、どれかに当たると
    /// <b>誰も押せないダイアログの前で実行が止まる</b>。落ちないので、待っているのか
    /// 計算しているのかも分からない。</para>
    ///
    /// <para>2026-09-12、例題ビルダーを実機の読込に寄せた拍子に「基礎梁が定義されて
    /// いないため剛体連結モードに切り替えて解析を実行します」が出て、全体実行が固まった。
    /// 出た理由 (基礎梁の既定が剛床連結) は実機と同じ扱いで正しいので、
    /// ダイアログの側を無人実行モードにする。</para>
    /// </summary>
    [TestClass]
    public static class UnattendedRunSetup
    {
        /// <summary>この実行でアプリの利用者ファイル (自動保存・ログ・設定など) を置く一時フォルダ。</summary>
        internal static string AppDataRoot { get; private set; } = "";

        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            MessageService.IsUnattended = true;
            // 利用者の実際のフォルダ (%LocalAppData%\PileDesign) に書かない。
            // 以前は自動保存のテストが利用者の自動保存フォルダに書き、古いファイルの掃除まで走らせていた
            AppDataRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PileDesignTests", System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(AppDataRoot);
            PileDesign.Common.AppDataPaths.OverrideRootForTests(AppDataRoot);
        }

        [AssemblyCleanup]
        public static void Cleanup()
        {
            try { System.IO.Directory.Delete(AppDataRoot, recursive: true); }
            catch { /* ログが開いたままなど。一時フォルダなので残っても害はない */ }
        }
    }

    /// <summary>無人実行モードが実際に効いていること。</summary>
    [TestClass]
    public class UnattendedRunSetupTests
    {
        /// <summary>
        /// テストは利用者の実際のフォルダ (自動保存・ログ・設定) に書かない。
        /// 書き先はすべて <see cref="PileDesign.Common.AppDataPaths"/> を通す (自分で %LocalAppData% を組み立てる箇所を作らない)。
        /// </summary>
        [TestMethod]
        public void TheRunDoesNotWriteToTheUsersAppDataFolder()
        {
            string real = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PileDesign");
            Assert.AreEqual(UnattendedRunSetup.AppDataRoot, PileDesign.Common.AppDataPaths.Root);
            Assert.AreNotEqual(System.IO.Path.GetFullPath(real), System.IO.Path.GetFullPath(PileDesign.Common.AppDataPaths.Root));

            var auto = new AutoSaveService(new FileOperationService(new System.Text.Json.JsonSerializerOptions()));
            StringAssert.StartsWith(auto.AutoSaveFolder, UnattendedRunSetup.AppDataRoot, "自動保存が利用者のフォルダを向いています");
            StringAssert.StartsWith(PileDesign.Common.Logging.AppLog.LogDirectory, UnattendedRunSetup.AppDataRoot);
            StringAssert.StartsWith(PileDesign.Common.Logging.CrashReporter.ReportDirectory, UnattendedRunSetup.AppDataRoot);

            var offenders = new System.Collections.Generic.List<string>();
            int scanned = 0;
            foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(TestSource.Root(), "Graphics_r1"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar)
                    || file.Contains(System.IO.Path.DirectorySeparatorChar + "bin" + System.IO.Path.DirectorySeparatorChar)) continue;
                scanned++;
                if (file.EndsWith("AppDataPaths.cs")) continue;
                if (System.IO.File.ReadAllText(file).Contains("SpecialFolder.LocalApplicationData"))
                    offenders.Add(System.IO.Path.GetFileName(file));
            }
            TestSource.AssertScanned(scanned, 100, "本体のソース");
            Assert.AreEqual(0, offenders.Count, "AppDataPaths を通さずに利用者のフォルダを組み立てています: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void DialogsAreSuppressedDuringTheRun()
        {
            Assert.IsTrue(MessageService.IsUnattended,
                "無人実行モードが立っていません。解析の入口のダイアログでテストが止まります");
        }
    }
}
