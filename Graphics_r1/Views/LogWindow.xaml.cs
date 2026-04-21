using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is LogWindowViewModel viewModel)
            {
                viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            }

            if (e.OldValue is LogWindowViewModel oldViewModel)
            {
                oldViewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            }
        }

        private void OnScrollToEndRequested(object? sender, System.EventArgs e)
        {
            // ListBox を末尾までスクロール（最後の項目を表示）
            if (LogListBox != null && LogListBox.Items.Count > 0)
            {
                int lastIndex = LogListBox.Items.Count - 1;
                LogListBox.ScrollIntoView(LogListBox.Items[lastIndex]);
            }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DataContext is LogWindowViewModel viewModel)
            {
                viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
                viewModel.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
