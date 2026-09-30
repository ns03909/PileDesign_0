using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>入力の診断の一覧 (<see cref="DiagnosticListViewModel"/>)。モードレスで開き、メイン画面を操作しながら直せる。</summary>
    public partial class DiagnosticListWindow : Window
    {
        public DiagnosticListWindow(DiagnosticListViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>行のダブルクリックは「選んだ指摘へ移動」と同じ。</summary>
        private void Rows_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is DiagnosticListViewModel vm && vm.GoToCommand.CanExecute(null)) vm.GoToCommand.Execute(null);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
