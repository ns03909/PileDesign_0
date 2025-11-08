using PileDesign.ViewModels;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// DocxOutputOptionWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class DocxOutputWindow : Window
    {
        public DocxOutputWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel; // DataContextにセット
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
