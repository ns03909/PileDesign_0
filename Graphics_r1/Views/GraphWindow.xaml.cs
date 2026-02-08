using PileDesign.Common;
using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// GraphWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GraphWindow : Window
    {
        public GraphWindow(GraphViewModel viewModel)
        {
            // 必要なら mainWindowViewModel をフィールドに保存

            InitializeComponent();
            this.DataContext = viewModel;
            Loaded += (s, e) =>
            {
                if (DataContext is GraphViewModel vm)
                {
                    vm.WpfPlot = wpfPlot;
                    vm.WpfPlot1 = wpfPlot1;
                    vm.WpfPlot2 = wpfPlot2;
                    vm.WpfPlot3 = wpfPlot3;
                    vm.UpdateGraph(); // 初期表示
                    vm.RequestClose += (sender, args) => this.Close(); // RequestCloseイベント

                    // CSVエクスポートメニューを追加
                    PlotHelper.AddCsvExportMenu(wpfPlot, "解析結果");
                    PlotHelper.AddCsvExportMenu(wpfPlot1, "解析結果1");
                    PlotHelper.AddCsvExportMenu(wpfPlot2, "解析結果2");
                    PlotHelper.AddCsvExportMenu(wpfPlot3, "解析結果3");
                }
            };

            GraphComboBox.SelectionChanged += (s, e) =>
            {
                if (DataContext is GraphViewModel vm)
                {
                    vm.SelectedGraphOption = GraphComboBox.SelectedItem as string;
                    vm.UpdateGraph();
                }
            };
        }

        private void GroundWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
