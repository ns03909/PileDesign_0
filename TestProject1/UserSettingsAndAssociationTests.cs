using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// ユーザー設定の保存と、.pdj の関連付けの判定。
    /// </summary>
    [TestClass]
    public class UserSettingsAndAssociationTests
    {
        /// <summary>
        /// 設定は一時ファイルに書き切ってから差し替えること。以前は設定ファイルへ直接書いていたので、
        /// 書き込みの途中で終了すると JSON が壊れ、次の起動で設定が既定値へ戻った。
        /// </summary>
        [TestMethod]
        public void SettingsAreSavedThroughATemporaryFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"UserSettings_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "user_settings.json");
                var service = new UserSettingsService(path);
                service.Settings.IsSaveAnalysisResultsAutoSave = true;
                service.Save();

                Assert.IsTrue(new UserSettingsService(path).Settings.IsSaveAnalysisResultsAutoSave, "保存した設定が読み戻せません");
                CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(dir), "一時ファイルが残っています");
                Assert.IsFalse(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "BOM が付きました (従来は BOM なし)");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }

            string src = TestSource.Read("Graphics_r1", "Services", "UserSettingsService.cs");
            StringAssert.Contains(TestSource.MethodBody(src, "public void Save()"), "WriteAtomically(",
                "設定を一時ファイル経由で保存していません");
        }

        /// <summary>
        /// 関連付けのコマンドが今の実行ファイルを起動するかは、コマンドから実行ファイルの部分を取り出して、
        /// 完全なパスとして比べること。以前は「含まれるか」で判定し、似たパスや引数に含むだけのコマンドも一致とみなした。
        /// </summary>
        [TestMethod]
        public void TheRegisteredExecutableIsComparedAsAWholePath()
        {
            const string exe = @"C:\Apps\PileDesign\PileDesign.exe";
            Assert.IsTrue(FileAssociationService.CommandRunsExecutable($"\"{exe}\" --open \"%1\"", exe));
            Assert.IsTrue(FileAssociationService.CommandRunsExecutable($"\"{exe.ToUpperInvariant()}\" --open \"%1\"", exe), "大文字小文字で区別しています");
            Assert.IsTrue(FileAssociationService.CommandRunsExecutable(@"C:\Apps\PileDesign\PileDesign.exe --open ""%1""", exe), "引用符の無い登録を読めていません");
            Assert.IsTrue(FileAssociationService.CommandRunsExecutable(@"C:\Program Files\PD\PileDesign.exe --open ""%1""", @"C:\Program Files\PD\PileDesign.exe"),
                "空白を含む引用符の無いパスを読めていません");

            Assert.IsFalse(FileAssociationService.CommandRunsExecutable(@"""C:\Apps\PileDesign\PileDesign.exe.old\PileDesign.exe"" --open ""%1""", exe),
                "似た名前の別のパスを一致とみなしています");
            Assert.IsFalse(FileAssociationService.CommandRunsExecutable($"\"C:\\Tools\\Launcher.exe\" \"{exe}\" \"%1\"", exe),
                "今の実行ファイルを引数に含むだけの別のコマンドを一致とみなしています");
            Assert.IsFalse(FileAssociationService.CommandRunsExecutable(null, exe));
        }
    }
}
