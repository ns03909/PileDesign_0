using PileDesign.Common;
using PileDesign.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// GraphWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GraphWindow : Window
    {
        private EventHandler _requestCloseHandler;

        public GraphWindow(GraphViewModel viewModel)
        {
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

                    _requestCloseHandler = (sender, args) => this.Close();
                    vm.RequestClose += _requestCloseHandler;

                    // CSVエクスポートメニューを追加
                    PlotHelper.AddCsvExportMenu(wpfPlot, "解析結果");
                    PlotHelper.AddCsvExportMenu(wpfPlot1, "解析結果1");
                    PlotHelper.AddCsvExportMenu(wpfPlot2, "解析結果2");
                    PlotHelper.AddCsvExportMenu(wpfPlot3, "解析結果3");

                    // WpfPlot1がsize=0→有効サイズになった時に再レンダリング（フォールバック）
                    wpfPlot1.SizeChanged += (sender2, args2) =>
                    {
                        if (args2.PreviousSize.Width == 0 && args2.NewSize.Width > 0)
                        {
                            wpfPlot1.Refresh();
                            wpfPlot2.Refresh();
                            wpfPlot3.Refresh();
                        }
                    };
                }
            };

            Closed += (s, e) =>
            {
                if (DataContext is GraphViewModel vm && _requestCloseHandler != null)
                {
                    vm.RequestClose -= _requestCloseHandler;
                    _requestCloseHandler = null;
                }
            };

            GraphComboBox.SelectionChanged += (s, e) =>
            {
                if (DataContext is GraphViewModel vm)
                {
                    vm.SelectedGraphOption = GraphComboBox.SelectedItem as string;
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
