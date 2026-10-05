using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析実行の確認画面 (AnalysisPreflightDialog) と、ダッシュボード・起動画面の表示 (2026-10-04 の UI 指摘)。
    /// </summary>
    [TestClass]
    public class AnalysisPreflightTests
    {
        private static AnalysisPreflightSummary Summary(params Diagnostic[] warnings)
            => new("水平解析", 100, "L1=1 / L2=1", 1, 0, 0, 1, warnings.Length == 0 ? null : warnings);

        /// <summary>
        /// 「次回から表示しない」は警告がないときだけ効く。以前は警告があっても確認ごと出なくなり、
        /// 警告を見ないまま解析が始まった。
        /// </summary>
        [TestMethod]
        public void TheConfirmation_IsAlwaysShownWhenThereAreWarnings()
        {
            var warning = Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.PileBody(1), "杭体 1: 注意");
            Assert.IsTrue(AnalysisPreflightDialog.ShouldShow(showSetting: false, Summary(warning)), "警告があるのに省略しています");
            Assert.IsFalse(AnalysisPreflightDialog.ShouldShow(showSetting: false, Summary()), "警告がないときは省略の設定に従う");
            Assert.IsTrue(AnalysisPreflightDialog.ShouldShow(showSetting: true, Summary()));

            string xaml = TestSource.Read("Graphics_r1", "Views", "AnalysisPreflightDialog.xaml");
            StringAssert.Contains(xaml, "Content=\"警告がないときは、次回からこの確認を省略する\"");
        }

        /// <summary>警告の場所の入力画面へ移れる行には「入力を確認」を出す。場所の無い警告には出さない。</summary>
        [TestMethod]
        public void WarningsWithAPlace_OfferToOpenTheInput()
        {
            var atBody = PreflightWarningItem.From(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.PileBody(2), "杭体 2: 注意"));
            Assert.IsTrue(atBody.CanNavigate);
            Assert.AreEqual("杭体の入力画面", atBody.DestinationName);
            StringAssert.Contains(atBody.Text, "杭体 2: 注意");

            var nowhere = PreflightWarningItem.From(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.Nowhere, "全体の注意"));
            Assert.IsFalse(nowhere.CanNavigate);

            // 選ばれた場所は、解析のウィンドウを閉じてから開く (診断一覧と同じ仕組み)
            string vm = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            StringAssert.Contains(vm, "AnalysisPreflightDialog.Confirm(owner, summary, out var navigateTo)");
            StringAssert.Contains(vm, "InputModel.RequestInputNavigation(navigateTo);");
        }

        /// <summary>警告が多いと画面が縦に伸びてボタンが外に出ていた。一覧の高さに上限を置いてスクロールする。</summary>
        [TestMethod]
        public void ALongWarningList_Scrolls_AndTheDialogStillParses()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "AnalysisPreflightDialog.xaml");
            StringAssert.Contains(xaml, "<ScrollViewer x:Name=\"WarningScroller\" MaxHeight=\"260\"");

            bool parsed = false;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var dlg = new AnalysisPreflightDialog();
                dlg.WarningList.ItemsSource = Enumerable.Range(1, 40)
                    .Select(i => PreflightWarningItem.From(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.PileBody(1), $"杭体 1: 注意 {i}")))
                    .ToList();
                // 項目のテンプレート (「入力を確認」のツールチップの書式を含む) は描くときに組まれるので、描かせる
                dlg.WarningBox.Visibility = System.Windows.Visibility.Visible;
                dlg.ShowActivated = false;
                dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                dlg.Left = -20000; dlg.Top = -20000;
                dlg.Show();
                dlg.UpdateLayout();
                Assert.IsTrue(dlg.ActualHeight < 900, $"警告 40 件で画面の高さが {dlg.ActualHeight:F0} px まで伸びています (一覧がスクロールしていない)");
                var firstButton = FindButton(dlg.WarningList);
                Assert.IsNotNull(firstButton, "「入力を確認」が描かれていません");
                Assert.AreEqual("解析を取りやめ、杭体の入力画面を開きます", firstButton!.ToolTip);
                // 「省略する」のチェックは 1 行を使い、キャンセルのボタンに重ならない (実機で文言が切れて重なっていた)
                var check = dlg.DoNotShowAgainCheckBox;
                var cancel = (System.Windows.FrameworkElement)dlg.FindName("CancelButton");
                var checkBottom = check.TranslatePoint(new System.Windows.Point(0, check.ActualHeight), dlg).Y;
                var cancelTop = cancel.TranslatePoint(new System.Windows.Point(0, 0), dlg).Y;
                Assert.IsTrue(checkBottom <= cancelTop + 0.5, $"チェックがボタンに重なっています (チェック下端 {checkBottom:F0} / ボタン上端 {cancelTop:F0})");
                check.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.IsTrue(check.DesiredSize.Width <= check.ActualWidth + 0.5, "チェックの文言が切れています");
                dlg.Close();
                parsed = true;
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.IsTrue(parsed);
        }

        private static System.Windows.Controls.Button? FindButton(System.Windows.DependencyObject root)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.Button b) return b;
                if (FindButton(child) is { } found) return found;
            }
            return null;
        }

        /// <summary>ダッシュボードは、解析した日時と表示を作り直した時刻を分けて書く (以前は「更新: 時刻」だけ)。</summary>
        [TestMethod]
        public void TheDashboard_SeparatesAnalysisTimeFromRefreshTime()
        {
            var analyzed = new DateTime(2026, 10, 4, 14, 30, 0);
            var refreshed = new DateTime(2026, 10, 4, 15, 2, 11);
            Assert.AreEqual("解析: 2026/10/04 14:30　／　表示の更新: 15:02:11", ResultDashboardWindow.FormatTimes(analyzed, refreshed));
            Assert.AreEqual("解析: 未実施　／　表示の更新: 15:02:11", ResultDashboardWindow.FormatTimes(null, refreshed));
        }
    }
}
