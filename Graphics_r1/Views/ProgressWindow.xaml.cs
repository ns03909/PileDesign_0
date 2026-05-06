using PileDesign.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Views
{
    public partial class ProgressWindow : Window
    {
        private readonly ProgressViewModel _viewModel;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public ProgressWindow(CancellationTokenSource cancellationTokenSource)
        {
            InitializeComponent();

            _cancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
            _viewModel = new ProgressViewModel();
            DataContext = _viewModel;
        }

        /// <summary>
        /// 進捗を更新
        /// </summary>
        public void UpdateProgress(Models.AnalysisProgress progress)
        {
            if (progress == null)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(progress));
                return;
            }

            _viewModel.Percentage = progress.Percentage;
            _viewModel.CurrentStep = progress.CurrentStep;
            _viewModel.PercentageText = $"{progress.Percentage:F1}% ({progress.CurrentStepNumber}/{progress.TotalSteps})";
            _viewModel.EstimatedRemainingTimeText = progress.EstimatedRemainingTimeText;
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageService.Show(
                "計算を中断しますか？\n\n中断すると、ここまでの計算結果は破棄されます。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.CanCancel = false;
                _viewModel.CurrentStep = "キャンセル中...";
                _cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// ウィンドウクローズを防止（キャンセルボタン経由のみ許可）
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            // 以下の場合にクローズを許可：
            // 1. 計算が100%完了した
            // 2. キャンセルボタンが押された（CanCancel = false）
            // 3. CancellationTokenがキャンセルされた（エラー時やプログラムからのクローズ）
            if (_viewModel.Percentage < 100 &&
                _viewModel.CanCancel &&
                !_cancellationTokenSource.IsCancellationRequested)
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }
    }
}
