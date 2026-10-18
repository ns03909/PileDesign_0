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
            Serilog.Log.Information("[Docx] 出力設定ウィンドウ: OK 押下");
            // 未選択（荷重ケース/液状化）の確認。No のときはウィンドウを閉じずに戻す。
            if (DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
            {
                if (!vm.DocxOutput.ValidateDocxSelectionOrConfirm()) return;
                // キャンセル・失敗のときは開いたまま (選び直して、そのままやり直せる)。以前は結末によらず閉じていた
                if (!MainWindowViewModel.ShouldCloseReportOptions(vm.OutputWordFile())) return;
            }
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
