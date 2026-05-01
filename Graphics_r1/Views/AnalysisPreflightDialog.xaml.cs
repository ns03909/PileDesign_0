using System;
using System.Windows;

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
        }

        /// <summary>
        /// プリフライトを表示し、ユーザーが「実行」を押せば true、キャンセルなら false。
        /// User 設定で「次回から表示しない」が ON のときは何も表示せず true を返す。
        /// </summary>
        public static bool Confirm(Window? owner, AnalysisPreflightSummary summary)
        {
            if (!Properties.Settings.Default.ShowAnalysisPreflight)
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

            var result = dlg.ShowDialog();
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
        int MaxParallelism);
}
