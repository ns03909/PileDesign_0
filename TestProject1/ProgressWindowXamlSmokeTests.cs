using PileDesign.Models;
using PileDesign.Views;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestProject1;

/// <summary>
/// 進み具合の窓 (<see cref="ProgressWindow"/>)。計算書の作成・交差点分割が使う。
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProgressWindowXamlSmokeTests
{
    /// <summary>
    /// <b>本題。</b> キャンセルのボタンが窓の中に収まって見える。以前は高さを 200 に固定していて、表題の帯の分だけ足りず、
    /// ボタンが下で切れて押せなかった (計算書の作成で「キャンセルできない」と報告された)。
    /// 段階の文が 2 行に折り返しても収まること。
    /// </summary>
    [TestMethod]
    public void TheCancelButtonIsInsideTheWindow()
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            XamlSmokeTestSupport.EnsureApplicationResources();
            using var cancellation = new CancellationTokenSource();
            var window = new ProgressWindow(cancellation) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                window.UpdateProgress(new AnalysisProgress
                {
                    CurrentStep = "荷重と解析結果の章（見出し・図表 1234 個）" + new string('あ', 60),
                    CurrentStepNumber = 3, TotalSteps = 5, Percentage = 60,
                });
                window.Show();
                window.UpdateLayout();

                var button = FindButton(window);
                Assert.IsNotNull(button, "キャンセルのボタンがありません");
                // 窓の表示できる領域 (表題の帯を除いた、窓の見た目の根) と比べる。中身の Grid は収まらなくても
                // 自分の望む高さで並べられ、はみ出た分が切れるだけなので、Grid と比べても切れていることは分からない
                var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                double bottom = button!.TranslatePoint(new Point(0, button.ActualHeight), root).Y;
                Assert.IsTrue(bottom <= root.ActualHeight + 0.5,
                    $"キャンセルのボタンが窓の下で切れています (ボタンの下端 {bottom:F1} / 表示できる高さ {root.ActualHeight:F1})");
                Assert.IsTrue(button.ActualHeight > 0 && button.IsVisible);
            }
            finally
            {
                cancellation.Cancel();   // 中止の求めがあれば閉じられる
                window.Close();
            }
        }, out bool timedOut);
        Assert.IsFalse(timedOut);
        Assert.IsNull(error, error?.ToString());
    }

    /// <summary>計算書の作成では、残り時間の見込みを出さない (段階ごとの長さが大きく違い、当てにならない)。</summary>
    [TestMethod]
    public void TheReportDoesNotShowARemainingTimeEstimate()
    {
        StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ReportProgress.cs"),
            "ShowsRemainingTime = false");
        StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "ProgressWindow.xaml"), "SizeToContent=\"Height\"");
    }

    private static Button? FindButton(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b) return b;
            if (FindButton(child) is { } found) return found;
        }
        return null;
    }
}
