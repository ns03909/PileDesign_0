using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 解析結果ダッシュボードの並べ方 (2026-10-15 のレビュー)。
    /// - 杭の一覧は「杭・判定・検定比・検定項目」に絞り、応答値・限界値・荷重条件は選んだ行の詳細欄に出す
    ///   (以前は 6 列で、固定幅の列だけで 425 あり、荷重条件の列が窮屈だった)。
    /// - メイン画面の右側にドッキングして、モデル図と並べて見る (以前は別ウィンドウで、モデル図を覆っていた)。
    /// </summary>
    [TestClass]
    public class ResultDashboardLayoutTests
    {
        [TestMethod]
        public void PileList_HasFourColumns_AndTheDetailIsBelow()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "ResultDashboardView.xaml");
            var grid = Regex.Match(xaml, @"<DataGrid x:Name=""PileRatioGrid"".*?</DataGrid>", RegexOptions.Singleline).Value;
            var headers = Regex.Matches(grid, @"Header=""([^""]+)""").Select(m => m.Groups[1].Value).ToArray();
            CollectionAssert.AreEqual(new[] { "杭No.", "判定", "検定比", "検定項目" }, headers, "一覧の列が「杭・判定・検定比・検定項目」ではありません");
            foreach (var name in new[] { "DetailTargetText", "DetailResponseText", "DetailLimitText", "DetailConditionText" })
                StringAssert.Contains(xaml, $"x:Name=\"{name}\"", $"詳細欄に {name} がありません");
        }

        /// <summary>解析した例題で、一覧の先頭 (最も厳しい杭) が選ばれ、詳細欄に応答値・限界値・荷重条件が出る。</summary>
        [TestMethod]
        public void TheMostSevereRow_IsSelected_AndItsDetailIsShown()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            string? response = null, limit = null, condition = null; int selected = -2; int rows = 0;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try
                {
                    var view = new PileDesign.Views.ResultDashboardView(vm);
                    var window = new System.Windows.Window { Content = view };
                    view.RefreshNow();
                    rows = view.PileRatioGrid.Items.Count;
                    selected = view.PileRatioGrid.SelectedIndex;
                    response = view.DetailResponseText.Text;
                    limit = view.DetailLimitText.Text;
                    condition = view.DetailConditionText.Text;
                    view.Detach();
                    window.Close();
                }
                finally { MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());

            Assert.IsTrue(rows > 0, "(前提) 検定した杭があること");
            Assert.AreEqual(0, selected, "最も厳しい杭 (一覧の先頭) を選んでいません");
            Assert.AreNotEqual("—", response, "詳細欄に応答値が出ていません");
            Assert.AreNotEqual("—", limit, "詳細欄に限界値が出ていません");
            Assert.AreNotEqual("—", condition, "詳細欄に荷重条件が出ていません");
        }

        /// <summary>
        /// 選んだ行 (開いたときは先頭の最も厳しい杭) でも、検定比は右寄せのまま、判定は色付きのまま。
        /// 共通の DataGridCell スタイルは選んだセルを編集中にするので、この表だけ選んでも編集に切り替えない (2026-10-17)。
        /// 以前は先頭の行だけ編集用の部品になり、検定比が左に寄り「OK」の緑が外れていた (利用者の指摘)。
        /// </summary>
        [TestMethod]
        public void TheSelectedRow_IsNotSwitchedToEditing()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            bool? editing = null; System.Windows.HorizontalAlignment? ratioAlignment = null; int selected = -2;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                var view = new PileDesign.Views.ResultDashboardView(vm);
                var window = new System.Windows.Window
                {
                    Content = view, Width = 600, Height = 900, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    view.RefreshNow();
                    for (int i = 0; i < 4; i++) { window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
                    selected = view.PileRatioGrid.SelectedIndex;
                    var row = (System.Windows.Controls.DataGridRow)view.PileRatioGrid.ItemContainerGenerator.ContainerFromIndex(0);
                    var ratioColumn = view.PileRatioGrid.Columns.First(c => (string)c.Header == "検定比");
                    var content = ratioColumn.GetCellContent(row);
                    var cell = content?.Parent as System.Windows.Controls.DataGridCell;
                    editing = cell?.IsEditing;
                    ratioAlignment = (content as System.Windows.Controls.TextBlock)?.HorizontalAlignment;
                }
                finally { view.Detach(); window.Close(); MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());

            Assert.AreEqual(0, selected, "(前提) 先頭の行を選んでいること");
            Assert.AreEqual(false, editing, "選んだ行のセルが編集中になっています (検定比が左に寄り、判定の色が外れます)");
            Assert.AreEqual(System.Windows.HorizontalAlignment.Right, ratioAlignment, "選んだ行の検定比が右寄せではありません");
        }

        /// <summary>
        /// メイン画面の右側 (モデル図とプロパティ欄の間) に、高さいっぱいの列としてドッキングする。二重には開かない。
        /// プロパティ欄の列 (ミニマップと縦に分けている) にタブで足すと、総括が窮屈でスクロールが要った (実機確認)。
        /// </summary>
        [TestMethod]
        public void TheDashboard_IsDockedBesideTheModel()
        {
            string code = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            string show = TestSource.MethodBody(code, "internal void ShowResultDashboard(");
            StringAssert.Contains(show, "new LayoutAnchorablePaneGroup(new LayoutAnchorablePane(anchorable))", "独立した列にしていません");
            StringAssert.Contains(show, "root.IndexOfChild(propertiesColumn)", "プロパティ欄の列の手前 (モデル図の右) に入れていません");
            StringAssert.Contains(show, "DockWidth = new GridLength(ResultDashboardDockWidth)", "列の幅を決めていません");
            StringAssert.Contains(show, "_resultDashboard.RefreshNow();", "開いているときに作り直して前に出していません");
            Assert.IsFalse(Regex.IsMatch(code + TestSource.Read("Graphics_r1", "Views", "CommandPaletteWindow.xaml.cs"), @"new\s+ResultDashboardWindow\b"),
                "別ウィンドウのダッシュボードを開く入口が残っています");
        }
    }
}
