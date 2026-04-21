using PileDesign.Common;
using PileDesign.ViewModels;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.Linq;
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

                    // マウスホバー詳細ポップアップを全プロットに対応
                    AttachHoverDetails(wpfPlot);
                    AttachHoverDetails(wpfPlot1);
                    AttachHoverDetails(wpfPlot2);
                    AttachHoverDetails(wpfPlot3);
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

        /// <summary>
        /// 指定した WpfPlot にマウスホバー詳細ポップアップのハンドラを取り付ける。
        /// </summary>
        private void AttachHoverDetails(ScottPlot.WPF.WpfPlot plot)
        {
            if (plot == null) return;
            plot.MouseMove += (s, args) => HandleHoverMove(plot, args);
            plot.MouseLeave += (s, args) => HoverDetailsPopup.IsOpen = false;
        }

        private void HandleHoverMove(ScottPlot.WPF.WpfPlot plot, MouseEventArgs e)
        {
            if (DataContext is not GraphViewModel vm) return;
            if (vm.GraphHoverMap.Count == 0)
            {
                HoverDetailsPopup.IsOpen = false;
                return;
            }

            var p = e.GetPosition(plot);
            var mousePixel = new Pixel(p.X * plot.DisplayScale, p.Y * plot.DisplayScale);
            var mouseLocation = plot.Plot.GetCoordinates(mousePixel);

            double minDist = double.MaxValue;
            Scatter nearest = null;
            foreach (var scatter in plot.Plot.GetPlottables().OfType<Scatter>())
            {
                if (!vm.GraphHoverMap.ContainsKey(scatter)) continue;
                var pt = scatter.Data.GetNearest(mouseLocation, plot.Plot.LastRender);
                if (!pt.IsReal) continue;
                double dx = pt.Coordinates.X - mouseLocation.X;
                double dy = pt.Coordinates.Y - mouseLocation.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = scatter;
                }
            }

            if (nearest != null && vm.TryGetGraphHoverDetails(nearest, out string details))
            {
                // ポップアップ位置は Window 相対に変換
                var pWin = e.GetPosition(this);
                HoverDetailsText.Text = details;
                HoverDetailsPopup.HorizontalOffset = pWin.X + 16;
                HoverDetailsPopup.VerticalOffset = pWin.Y + 16;
                if (!HoverDetailsPopup.IsOpen) HoverDetailsPopup.IsOpen = true;
            }
            else
            {
                HoverDetailsPopup.IsOpen = false;
            }
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
