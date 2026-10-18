using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.Collections.Generic;
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
        /// 一覧の絞り込み (2026-10-17 のレビュー): NG のみ・検定できない項目あり・検定比がしきい値以上。
        /// 杭が多いと NG や検定できない杭を探すのに手間取った。
        /// </summary>
        [TestMethod]
        public void TheFilter_NarrowsThePileList()
        {
            PileEvaluationEntry E(int no, double ratio, PileRatioBand band, bool unconverged = false, bool unavailable = false) =>
                new() { PileNo = no, MaxRatio = ratio, Band = band, HasUnconverged = unconverged, HasUnavailable = unavailable };
            var piles = new List<PileEvaluationEntry>
            {
                E(1, 1.20, PileRatioBand.Ng),
                E(2, 0.95, PileRatioBand.Tight, unavailable: true),
                E(3, 0.85, PileRatioBand.Tight),
                E(4, double.NaN, PileRatioBand.Unconverged, unconverged: true),
                E(5, 0.40, PileRatioBand.Safe),
            };
            int[] Nos(PileDesign.Views.ResultDashboardView.PileFilter f, double t = 0.8) =>
                PileDesign.Views.ResultDashboardView.FilterPiles(piles, f, t).Select(e => e.PileNo).ToArray();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Nos(PileDesign.Views.ResultDashboardView.PileFilter.All));
            CollectionAssert.AreEqual(new[] { 1 }, Nos(PileDesign.Views.ResultDashboardView.PileFilter.NgOnly));
            CollectionAssert.AreEqual(new[] { 2, 4 }, Nos(PileDesign.Views.ResultDashboardView.PileFilter.NotEvaluable),
                "検定比が数の杭でも、検定できない項目があれば出す");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Nos(PileDesign.Views.ResultDashboardView.PileFilter.AtOrAboveThreshold, 0.85),
                "しきい値ちょうどは含め、検定比の無い (未収束だけの) 杭は含めない");

            string xaml = TestSource.Read("Graphics_r1", "Views", "ResultDashboardView.xaml");
            StringAssert.Contains(xaml, "x:Name=\"PileFilterBox\"");
            StringAssert.Contains(xaml, "x:Name=\"PileThresholdBox\"");
        }

        /// <summary>
        /// 続けて来た通知 (解析の完了では完了時刻・解析済み・杭要素分割などが続く) は、作り直し 1 回にまとめる (2026-10-17 のレビュー)。
        /// 以前は通知ごとに予約し、そのたびに検定のまとめを作り直していた (杭の多いモデルで重い)。
        /// </summary>
        [TestMethod]
        public void BurstsOfNotifications_RefreshOnce()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            int before = -1, after = -1;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                var view = new PileDesign.Views.ResultDashboardView(vm);
                var window = new System.Windows.Window
                {
                    Content = view, Width = 600, Height = 900, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false,
                };
                void Settle() { for (int i = 0; i < 4; i++) { window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); } }
                try
                {
                    window.Show(); Settle();
                    before = view.RefreshCount;
                    bool split = vm.IsElementSplit;
                    vm.IsElementSplit = !split;   // 通知が続く (杭要素分割・状態表示ほか)
                    vm.IsElementSplit = split;
                    view.RequestRefresh();
                    Settle();
                    after = view.RefreshCount;
                }
                finally { view.Detach(); window.Close(); MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());
            Assert.IsTrue(before >= 1, "(前提) 開いたときに作っていること");
            Assert.AreEqual(1, after - before, "続けて来た通知で、作り直しを何度もしています");
        }

        /// <summary>表示を更新しても (解析の完了・絞り込みの変更でも)、確かめていた杭を選んだままにする。以前は毎回先頭へ戻った。</summary>
        [TestMethod]
        public void TheSelectedPile_SurvivesARefresh()
        {
            var rows = new List<PileDesign.Views.ResultDashboardView.PileRow>
            {
                new(7, "NG", "1.20", "", "", "", "", ""),
                new(3, "OK", "0.90", "", "", "", "", ""),
                new(5, "OK", "0.50", "", "", "", "", ""),
            };
            Assert.AreEqual(1, PileDesign.Views.ResultDashboardView.SelectionAfterRefresh(rows, 3), "前に選んでいた杭を選び直していません");
            Assert.AreEqual(0, PileDesign.Views.ResultDashboardView.SelectionAfterRefresh(rows, 9), "前の杭が無ければ先頭 (最も厳しい杭)");
            Assert.AreEqual(0, PileDesign.Views.ResultDashboardView.SelectionAfterRefresh(rows, null));
            Assert.AreEqual(-1, PileDesign.Views.ResultDashboardView.SelectionAfterRefresh([], 3));

            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vm = built.Value.vm;
            int? before = null, after = null; int count = 0;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                var view = new PileDesign.Views.ResultDashboardView(vm);
                var window = new System.Windows.Window { Content = view };
                try
                {
                    view.RefreshNow();
                    count = view.PileRatioGrid.Items.Count;
                    if (count < 2) return;
                    view.PileRatioGrid.SelectedIndex = 1;
                    before = ((PileDesign.Views.ResultDashboardView.PileRow)view.PileRatioGrid.SelectedItem).PileNo;
                    view.RefreshNow();
                    after = (view.PileRatioGrid.SelectedItem as PileDesign.Views.ResultDashboardView.PileRow)?.PileNo;
                }
                finally { view.Detach(); window.Close(); MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());
            if (count < 2) { Assert.Inconclusive("(前提) 検定した杭が 2 本以上あること"); return; }
            Assert.AreEqual(before, after, "表示を更新したら、選んでいた杭から先頭へ戻りました");
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
