using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 検定の結果から杭へ移る: ダッシュボードの杭の行から、平面図でその杭を表示する・その杭の検定を開く。
    /// 以前は杭の一覧を見るだけで、NG の杭を平面図や検定の詳細で探し直す必要があった。
    /// </summary>
    [TestClass]
    public class EvaluationToPileNavigationTests
    {
        private static EvaluationItem Item(int pileNo, double ratio) => new()
        {
            Kind = EvaluationKind.PileSectionShear,
            Level = 2,
            Category = "杭体曲げ (安全限界)",
            PileNo = pileNo,
            LoadCaseName = "L2-1",
            Response = ratio,
            Limit = 1.0,
            IsOk = ratio <= 1.0,
        };

        [TestMethod]
        public void ThePileFilter_KeepsOnlyThatPilesEvaluationRows_AndClears()
        {
            var evaluation = new ResultTable { Name = "検定 (低減後)", SpansAllConditions = true, Rows = new List<object> { Item(1, 0.9), Item(2, 1.2), Item(2, 0.5) } };
            var perCase = new ResultTable { Name = "杭の断面力 L2-1", LoadCaseName = "L2-1", Rows = new List<object> { new object() } };
            var vm = new TableWindowViewModel();
            vm.LoadTables([evaluation, perCase]);
            Assert.AreEqual(2, vm.FilteredTables.Count, "前提: 絞り込む前は両方の表が出る");

            vm.PileNoFilter = 2;
            Assert.AreEqual(1, vm.FilteredTables.Count, "杭で絞り込んだら、杭ごとの行を持つ表 (検定) だけを出す");
            var rows = vm.FilteredTables[0].Rows.Cast<EvaluationItem>().ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.All(r => r.PileNo == 2), "ほかの杭の行が混ざっています");
            Assert.IsTrue(vm.IsPileFiltered);
            StringAssert.Contains(vm.PileFilterText, "杭 No.2");

            vm.ClearPileFilterCommand.Execute(null);
            Assert.IsFalse(vm.IsPileFiltered);
            Assert.AreEqual(2, vm.FilteredTables.Count, "解除しても表が戻りません");
        }

        /// <summary>杭番号 (検定の項目と同じ PileNo) の杭を選び、平面図の中央に寄せる処理を呼ぶ。無い番号なら何もしない。</summary>
        [TestMethod]
        public void ShowPileOnPlan_SelectsAndCentersThePile()
        {
            var input = new InputModel
            {
                PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
                {
                    new() { No = 1, PileNo = 11, X = 0, Y = 0 },
                    new() { No = 2, PileNo = 12, X = 3, Y = 0 },
                },
            };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            IReadOnlyList<PileLayoutDataItem>? centered = null;
            vm.CenterOnPilesAction = p => centered = p;

            Assert.AreEqual(1, vm.ShowPileOnPlan(12));
            Assert.IsTrue(input.PileLayoutItems[1].IsSelected, "杭が選ばれていません");
            Assert.IsFalse(input.PileLayoutItems[0].IsSelected, "ほかの杭まで選んでいます");
            Assert.IsNotNull(centered);
            Assert.AreSame(input.PileLayoutItems[1], centered!.Single(), "平面図で寄せる杭が違います");

            centered = null;
            Assert.AreEqual(0, vm.ShowPileOnPlan(99));
            Assert.IsNull(centered, "無い杭に寄せようとしています");
        }

        [TestMethod]
        public void TheDashboard_OffersBothWaysFromThePileRow()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "ResultDashboardView.xaml");
            string code = TestSource.Read("Graphics_r1", "Views", "ResultDashboardView.xaml.cs");
            StringAssert.Contains(xaml, "Click=\"ShowPileButton_Click\"");
            StringAssert.Contains(xaml, "Click=\"OpenPileEvaluationButton_Click\"");
            StringAssert.Contains(xaml, "MouseDoubleClick=\"PileRatioGrid_MouseDoubleClick\"");
            StringAssert.Contains(code, "_vm.ShowPileOnPlan(no)");
            StringAssert.Contains(code, "_vm.OpenEvaluationForPile(no)");
            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            StringAssert.Contains(main, "viewModel.CenterOnPilesAction = CenterOnPiles;");
        }
    }
}
