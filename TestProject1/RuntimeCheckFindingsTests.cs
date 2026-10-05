using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-05 に実機 (UI Automation) で確かめて見つかったもの。
    /// </summary>
    [TestClass]
    public class RuntimeCheckFindingsTests
    {
        /// <summary>
        /// キャンセルで閉じた入力画面は「何も適用していない」と伝える。
        /// 以前は基本設定しか伝えておらず、地盤・荷重・杭体・プロジェクト情報はキャンセルでも「再解析が必要」が立ち、
        /// 入力変更後の帯に「地盤 編集」と出た (実機で確認)。
        /// </summary>
        [TestMethod]
        public void CancelledDialogs_ReportThatNothingWasApplied()
        {
            foreach (var vm in new[] { "GroundLayerViewModel", "LoadCaseViewModel", "PileBodyViewModel", "ProjectInfoViewModel", "FundamentalViewModel" })
            {
                var type = typeof(MainWindowViewModel).Assembly.GetTypes().Single(t => t.Name == vm);
                Assert.IsTrue(type.GetInterfaces().Any(i => i.Name == "IDialogAppliedState"),
                    $"{vm} はキャンセルを伝えていません (キャンセルでも「再解析が必要」が立つ)");
            }

            var input = new InputModel { FundamentalInput = new FundamentalInput { ProjectName = "名前" } };
            var main = new MainWindowViewModel { CurrentInputModel = input };
            var project = new ProjectInfoViewModel(main);
            Assert.IsTrue(MainWindowViewModel.DialogAppliedChanges(project), "閉じるまでは適用した扱い (× などは従来どおり)");
            project.CancelCommand.Execute(null);
            Assert.IsFalse(MainWindowViewModel.DialogAppliedChanges(project), "キャンセルしたのに適用した扱いです");
        }
    }
}
