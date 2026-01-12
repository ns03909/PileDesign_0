using PileDesign.ViewModels;
using System.Windows;

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
            LogScrollViewer.ScrollToEnd();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DataContext is LogWindowViewModel viewModel)
            {
                viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            }
            base.OnClosed(e);
        }
    }
}
