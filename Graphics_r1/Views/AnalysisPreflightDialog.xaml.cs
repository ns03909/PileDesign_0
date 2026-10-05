using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PileDesign.Common;
using PileDesign.Services;

namespace PileDesign.Views
{
    /// <summary>
    /// 解析実行直前のサマリ確認ダイアログ。
    /// 既存の警告 MessageBox とは別レイヤーで「これから何ステップ実行されるのか」を可視化し、
    /// 想定外の長時間解析や設定ミスを未然に防ぐ。
    /// </summary>
    public partial class AnalysisPreflightDialog : Window
    {
        public AnalysisPreflightDialog()
        {
            InitializeComponent();

            // ダイアログ表示直後に「実行」ボタンへフォーカスを移し、
            // どのキー (Enter) が何を実行するか視覚的に明示する。
            // Loaded で行うのは XAML 構築直後だと Visual Tree が未完成のため。
            this.Loaded += (_, _) => OkButton?.Focus();
        }

        /// <summary>
        /// プリフライトを表示し、ユーザーが「実行」を押せば true、キャンセルなら false。
        /// User 設定で「次回から表示しない」が ON のときは何も表示せず true を返す。
        /// </summary>
        public static bool Confirm(Window? owner, AnalysisPreflightSummary summary)
            => Confirm(owner, summary, out _);

        /// <summary>
        /// <see cref="Confirm(Window?, AnalysisPreflightSummary)"/> に、警告の「入力を確認」で選ばれた場所を返す形。
        /// 場所が返ったときは false (解析は取りやめ)。呼び出し側がその入力画面を開く。
        /// </summary>
        public static bool Confirm(Window? owner, AnalysisPreflightSummary summary, out DiagnosticTarget? navigateTo)
        {
            navigateTo = null;
            // 省略してよいのは警告がないときだけ。以前は「次回から表示しない」を選ぶと警告があっても
            // 確認ごと出なくなり、警告を見ないまま解析が始まった
            if (!ShouldShow(Properties.Settings.Default.ShowAnalysisPreflight, summary))
                return true;

            var dlg = new AnalysisPreflightDialog
            {
                Owner = owner,
                HeaderText = { Text = $"{summary.AnalysisName}を実行します" },
                TotalStepsText = { Text = $"{summary.TotalSteps:N0} ステップ" },
                LoadCaseCountText = { Text = summary.LoadCaseCountText },
                CombinationCountText = { Text = $"{summary.CombinationCount} 組" },
                CounterLoadingText = { Text = summary.CounterLoadingCount > 0
                    ? $"{summary.CounterLoadingCount} 組 (収束に時間がかかる可能性あり)"
                    : "なし" },
                NonLinearText = { Text = summary.NonLinearLoadCaseCount > 0
                    ? $"{summary.NonLinearLoadCaseCount} ケース"
                    : "なし" },
                ParallelismText = { Text = summary.MaxParallelism > 1
                    ? $"{summary.MaxParallelism} 並列"
                    : "逐次実行" },
            };

            // 入力警告（注意レベル）があればハイライト枠で表示
            if (summary.InputWarnings != null && summary.InputWarnings.Count > 0)
            {
                dlg.WarningHeader.Text = $"入力に注意点があります ({summary.InputWarnings.Count} 件)";
                dlg.WarningList.ItemsSource = summary.InputWarnings.Select(PreflightWarningItem.From).ToList();
                dlg.WarningBox.Visibility = Visibility.Visible;
            }

            var result = dlg.ShowDialog();
            if (dlg._navigateTo != null)
            {
                navigateTo = dlg._navigateTo;
                return false;
            }
            if (result != true)
                return false;

            if (dlg.DoNotShowAgainCheckBox.IsChecked == true)
            {
                Properties.Settings.Default.ShowAnalysisPreflight = false;
                Properties.Settings.Default.Save();
            }
            return true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        /// <summary>警告の「入力を確認」で選ばれた場所。</summary>
        private DiagnosticTarget? _navigateTo;

        private void NavigateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PreflightWarningItem { Target: { } target } })
            {
                _navigateTo = target;
                DialogResult = false;
            }
        }

        /// <summary>確認を表示するか。警告があるときは、省略の設定に関係なく表示する。</summary>
        internal static bool ShouldShow(bool showSetting, AnalysisPreflightSummary summary)
            => showSetting || summary.InputWarnings is { Count: > 0 };

    }

    /// <summary>
    /// プリフライトに渡す解析サマリ。Compute 時のスナップショット (immutable record)。
    /// </summary>
    public record AnalysisPreflightSummary(
        string AnalysisName,
        int TotalSteps,
        string LoadCaseCountText,
        int CombinationCount,
        int CounterLoadingCount,
        int NonLinearLoadCaseCount,
        int MaxParallelism,
        IReadOnlyList<Diagnostic>? InputWarnings = null);

    /// <summary>確認画面の警告の 1 行。入力画面を開ける場所なら「入力を確認」を出す。</summary>
    public sealed record PreflightWarningItem(string Text, DiagnosticTarget? Target, string? DestinationName)
    {
        public bool CanNavigate => Target != null;

        /// <summary>「入力を確認」のツールチップ。行は作ったら変わらない (一覧ごと作り直す)。</summary>
        public string? NavigateToolTip { get; set; }

        internal static PreflightWarningItem From(Diagnostic d)
        {
            var destination = DiagnosticSelection.DestinationOf(d.Target);
            return destination == InputDestination.None
                ? new PreflightWarningItem(DiagnosticSelection.FormatForList(d), null, null)
                : new PreflightWarningItem(DiagnosticSelection.FormatForList(d), d.Target, DiagnosticSelection.DestinationName(destination))
                {
                    NavigateToolTip = $"解析を取りやめ、{DiagnosticSelection.DestinationName(destination)}を開きます",
                };
        }
    }
}
