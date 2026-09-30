using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// 起動の確認 (<c>PileDesign.exe --self-check</c>)。発行した単一ファイル版を実際に起動し、例題を開く・解析する・
    /// 検定する・計算書を出すまでを通して終了コードで返す。リリースの確認 (tools/release-check.ps1) が使う。
    /// 実際に起動して通すのはリリースの確認で、ここではその配線が外れていないことを見る。
    /// </summary>
    [TestClass]
    public class SelfCheckTests
    {
        [TestMethod]
        public void TheSwitchIsParsed()
        {
            Assert.IsNull(SelfCheckRunner.ParseDirectory(null));
            Assert.IsNull(SelfCheckRunner.ParseDirectory(["project.pdj"]));
            Assert.AreEqual(Path.GetFullPath(@"C:\tmp\check"), SelfCheckRunner.ParseDirectory(["--self-check", @"C:\tmp\check"]));
            Assert.AreEqual(Path.GetFullPath(@"C:\tmp\check"), SelfCheckRunner.ParseDirectory(["--SELF-CHECK", @"C:\tmp\check"]));
            // 出力先を省くと一時フォルダ
            StringAssert.StartsWith(SelfCheckRunner.ParseDirectory(["--self-check"]), Path.GetTempPath());
            StringAssert.StartsWith(SelfCheckRunner.ParseDirectory(["--self-check", "--open"]), Path.GetTempPath());
        }

        /// <summary>
        /// 起動の確認は、画面と同じ入口を通す (例題の読み込み・解析の実行と登録・検定・計算書)。
        /// 別の近道を通すと、配布した形で画面から使ったときの不具合を見逃す。
        /// </summary>
        [TestMethod]
        public void TheCheckUsesTheSameEntryPointsAsTheScreen()
        {
            string runner = TestSource.Read("Graphics_r1", "ViewModels", "SelfCheckRunner.cs");
            foreach (var entry in new[]
            {
                "vm.Example3_1Command.ExecuteAsync(null)",
                "horizontal.ExecuteAnalysisCommand.ExecuteAsync(null)",
                "horizontal.OkCommand.Execute(null)",
                "EvaluationService.BuildEvaluationResult(vm, factored: true)",
                "doc.CreateWordDocument(input, report)",
                "doc.OmittedItems.Count > 0",
            })
                StringAssert.Contains(runner, entry);
        }

        /// <summary>確認のあいだはダイアログ・起動時の案内・自動保存の復元を出さず、関連付けも書き換えない (確認が止まる・残る)。</summary>
        [TestMethod]
        public void TheAppRunsTheCheckWithoutDialogs()
        {
            string app = TestSource.Read("Graphics_r1", "App.xaml.cs");
            StringAssert.Contains(app, "SelfCheckDirectory = PileDesign.ViewModels.SelfCheckRunner.ParseDirectory(e.Args);");
            StringAssert.Contains(app, "PileDesign.Services.MessageService.IsUnattended = true;");
            StringAssert.Contains(app, "if (!IsSelfCheck && FileAssociationService.IsRegistered()");
            StringAssert.Contains(app, "Shutdown(code);");
            StringAssert.Contains(app, "else if (string.IsNullOrEmpty(StartupFilePath))");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs"),
                "if (string.IsNullOrEmpty(App.StartupFilePath) && !App.IsSelfCheck)");
        }

        /// <summary>リリースの確認は、発行した exe を起動の確認で起動し、終了コードで合否を見る。</summary>
        [TestMethod]
        public void TheReleaseCheckRunsThePublishedExe()
        {
            string release = File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "release-check.ps1"));
            StringAssert.Contains(release, "-ArgumentList \"--self-check\"");
            StringAssert.Contains(release, "$proc.ExitCode -ne 0");
            StringAssert.Contains(release, SelfCheckRunner.ResultFileName);
        }
    }
}
