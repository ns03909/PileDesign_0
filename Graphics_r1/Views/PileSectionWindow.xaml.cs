using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using ScottPlot.Plottables;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// PileSectionWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class PileSectionWindow : Window
    {
        private readonly PileSectionViewModel _viewModel;
        public PileSectionViewModel ViewModel => _viewModel;

        // コンストラクタ
        public PileSectionWindow(MainWindowViewModel mainWIndowViewModel, PileSection pileSection, int pileBodyNo, int segmentNo)
        {
            try
            {
                InitializeComponent();

                _viewModel = new PileSectionViewModel(mainWIndowViewModel, pileSection, pileBodyNo, segmentNo);
                DataContext = _viewModel;

                // CanvasのSizeChangedイベントにハンドラを追加
                Canvas.SizeChanged += Canvas_SizeChanged;
                _viewModel.RequestClose += (s, e) =>
                {
                    {
                        // すでにクローズ処理中なら何もしない
                        if (_isClosingHandled) return;
                        _isClosingHandled = true;

                        if (this.IsLoaded && this.IsVisible)
                        {
                            DialogResult = e;
                            this.Close();
                        }
                    }
                    ;
                };

                // WindowのLoadedイベントにハンドラを追加
                //Loaded += PileSectionWindow_Loaded;
                _viewModel.PileSectionWindowInstance = this;
                SetComboBoxSelectedItem();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました: {ex.Message}");
            }

            // コンストラクタ末尾の登録を修正
            _viewModel.PropertyChanged += SharedViewModel_PropertyChanged;
            _viewModel.PileSection.PropertyChanged += PileSection_PropertyChanged;

            // CSVエクスポートメニューを追加
            PlotHelper.AddCsvExportMenu(wpfPlotMN, "MN曲線");
            PlotHelper.AddCsvExportMenu(wpfPlotMphi, "Mφ曲線");
            PlotHelper.AddCsvExportMenu(wpfPlotMtheta, "Mθ曲線");
            PlotHelper.AddCsvExportMenu(wpfPlotNQ, "NQ曲線");
        }


        private void PileSection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsRedrawProperty(e.PropertyName))
            {
                _viewModel.RedrawShapes();
                _viewModel.ChartUpdate();
            }
        }

        // nameof の対象をモデル型で書く
        private static bool IsRedrawProperty(string propertyName)
        {
            return propertyName switch
            {
                nameof(PileDesign.Models.InputData.PileSection.PileDiameter) => true,
                nameof(PileDesign.Models.InputData.PileSection.ConcreteOutDia) => true,
                nameof(PileDesign.Models.InputData.PileSection.ConcreteGamma) => true,
                nameof(PileDesign.Models.InputData.PileSection.ConcreteGsi) => true,
                nameof(PileDesign.Models.InputData.PileSection.ConcreteFc) => true,
                nameof(PileDesign.Models.InputData.PileSection.PipeDia) => true,
                nameof(PileDesign.Models.InputData.PileSection.PipeTs) => true,
                nameof(PileDesign.Models.InputData.PileSection.MainBarNum) => true,
                nameof(PileDesign.Models.InputData.PileSection.MainBarSize) => true,
                nameof(PileDesign.Models.InputData.PileSection.MainBarCenterCover) => true,
                nameof(PileDesign.Models.InputData.PileSection.HoopSize) => true,
                nameof(PileDesign.Models.InputData.PileSection.HoopCenterCover) => true,
                nameof(PileDesign.Models.InputData.PileSection.PileSectionType) => true,
                nameof(PileDesign.Models.InputData.PileSection.SelectedSteelPipePileName) => true,
                _ => false,
            };
        }

        private bool _isClosingHandled = false;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;


            // 解除を追加
            _viewModel.PropertyChanged -= SharedViewModel_PropertyChanged;
            if (_viewModel.PileSection != null)
                _viewModel.PileSection.PropertyChanged -= PileSection_PropertyChanged;

            if (DataContext is PileSectionViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
        }

        private void PileSectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ComboBoxの選択を再設定
            SetComboBoxSelectedItem();
        }

        private void SetComboBoxSelectedItem()
        {
            Console.WriteLine($"Before setting ComboBox: {_viewModel.PileSection.SelectedPrecastPile?.Name}");

            if (_viewModel.PileSection.PileSectionType == "PHC杭")
            {
                ComboBoxPHCPileType.ItemsSource = PileSection.PHCOption;
                ComboBoxPHCPileType.SelectedItem = _viewModel.PileSection.SelectedPrecastPile.Name;
            }
            else if (_viewModel.PileSection.PileSectionType == "PRC杭")
            {
                ComboBoxPRCPileType.ItemsSource = PileSection.PRCOption;
                ComboBoxPRCPileType.SelectedItem = _viewModel.PileSection.SelectedPrecastPile.Name;
            }
            else if (_viewModel.PileSection.PileSectionType == "SC杭")
            {
                ComboBoxSCPileType.ItemsSource = PileSection.SCOption;
                ComboBoxSCPileType.SelectedItem = _viewModel.PileSection.SelectedPrecastPile.Name;
            }

            Console.WriteLine($"After setting ComboBox: {_viewModel.PileSection.SelectedPrecastPile.Name}");
        }

        // キャンバスのサイズが変更されたときに描画を行うメソッド
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Canvasのサイズが変更された後にViewModelに設定し、描画を行う
            // Cnavasの定義は、Windows_Loaded、Canvas_Loadedではうまくいかない。なぜかCanvas.ActualHeight、ActualWidthが0となり計算が収束しない。
            _viewModel.Canvas = Canvas;
            _viewModel.RedrawShapes();

        }
        // プロパティ変更イベントハンドラ
        private void SharedViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var viewModel = (PileSectionViewModel)sender;

            if (IsRedrawProperty(e.PropertyName))
            {
                viewModel.RedrawShapes();
                viewModel.ChartUpdate();
            }
        }

        // キャンバス右クリック時のイベントハンドラ
        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) { }

        //画像保存アイテムクリック時のイベントハンドラ
        private void ImageCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel.CopyCanvasToClipboard(Canvas);
        }

        //画像保存アイテムクリック時のイベントハンドラ
        private void ImageSaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel.SaveImage(Canvas);
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }


        private void ComboBoxPrecastConcretePileType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }


        private void ComboBoxSteelPileType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.PileSection.RecalculateSelectedPrecastPile();

            if (viewModel.PileSection.PileSectionType == "鋼管杭")
            {
                viewModel.PileSection.SelectedSteelPipe = viewModel.PileSection.SteelPipePileSectionTypeOption[0];
            }

            viewModel.RedrawShapes();
            viewModel.ChartUpdate();
        }


        private void ComboBox_PileSectionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // senderをComboBoxにキャスト
            ComboBox comboBox = sender as ComboBox;

            // 選択された項目を取得
            var selectedItem = comboBox.SelectedItem;

            // 選択された項目を使用して処理を行う
            if (selectedItem != null)
            {
                PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
                //viewModel.PileSection.SelectedPrecastPileName = selectedItem.ToString();
                viewModel.PileSection.SelectedPrecastPile.Name = selectedItem.ToString();
                viewModel.PileSection.RecalculateSelectedPrecastPile();

                viewModel.RedrawShapes();
                viewModel.ChartUpdate();
            }
        }


        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.RedrawShapes();
            viewModel.ChartUpdate();
        }

        // データグリッドの全コピー
        private static void CopyDataGridToClipboard(DataGrid dataGrid, bool isSelectedOnly)
        {
            if (dataGrid == null || dataGrid.Items.Count == 0) return;

            var sb = new StringBuilder();

            // ヘッダー行
            foreach (var column in dataGrid.Columns)
            {
                sb.Append(column.Header);
                sb.Append('\t');
            }
            sb.Length = Math.Max(0, sb.Length - 1); // 最後のタブ削除
            sb.AppendLine();

            if (!isSelectedOnly)
            {
                // データ行
                foreach (var item in dataGrid.Items)
                {
                    if (item == CollectionView.NewItemPlaceholder) continue;
                    foreach (var column in dataGrid.Columns)
                    {
                        var cellContent = column.GetCellContent(item);
                        string text = "";
                        if (cellContent is TextBlock tb)
                            text = tb.Text;
                        else if (cellContent != null)
                            text = cellContent.ToString();
                        sb.Append(text);
                        sb.Append('\t');
                    }
                    sb.Length = Math.Max(0, sb.Length - 1);
                    sb.AppendLine();
                }
            }
            else
            {
                // 選択行のみ
                foreach (var item in dataGrid.SelectedItems)
                {
                    if (item == CollectionView.NewItemPlaceholder) continue;
                    foreach (var column in dataGrid.Columns)
                    {
                        var cellContent = column.GetCellContent(item);
                        string text = "";
                        if (cellContent is TextBlock tb)
                            text = tb.Text;
                        else if (cellContent != null)
                            text = cellContent.ToString();
                        sb.Append(text);
                        sb.Append('\t');
                    }
                    sb.Length = Math.Max(0, sb.Length - 1);
                    sb.AppendLine();
                }
            }

            Clipboard.SetText(sb.ToString());
        }

        // 散布図
        private void ExportScatterCsv_Click(object sender, RoutedEventArgs e)
        {
            var scatterPlots = wpfPlotMN.Plot.GetPlottables()
                .OfType<Scatter>()
                .ToList();

            // 各scatterのデータ取得
            var dataList = scatterPlots
                .Select(scatter =>
                {
                    var ds = (ScottPlot.IDataSource)scatter;
                    int count = ds.Length;
                    var xs = new double[count];
                    var ys = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        xs[i] = ds.GetX(i);
                        ys[i] = ds.GetY(i);
                    }
                    return (xs, ys, legend: scatter.Label ?? "");
                })
                .ToList();

            // 最大データ数
            int maxCount = dataList.Count > 0 ? dataList.Max(d => d.xs.Length) : 0;

            var sb = new StringBuilder();

            // 1行目: legend
            for (int i = 0; i < dataList.Count; i++)
            {
                sb.Append(dataList[i].legend);
                sb.Append(',');
                sb.Append(',');
            }
            sb.Length = Math.Max(0, sb.Length - 1); // 最後のカンマ削除
            sb.AppendLine();

            // 2行目: X,Yラベル
            for (int i = 0; i < dataList.Count; i++)
            {
                sb.Append("X,Y,");
            }
            sb.Length = Math.Max(0, sb.Length - 1);
            sb.AppendLine();

            // 3行目以降: データ
            for (int row = 0; row < maxCount; row++)
            {
                for (int i = 0; i < dataList.Count; i++)
                {
                    var xs = dataList[i].xs;
                    var ys = dataList[i].ys;
                    if (row < xs.Length)
                    {
                        sb.Append(xs[row]);
                        sb.Append(',');
                        sb.Append(ys[row]);
                        sb.Append(',');
                    }
                    else
                    {
                        sb.Append(",,");
                    }
                }
                sb.Length = Math.Max(0, sb.Length - 1);
                sb.AppendLine();
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv",
                FileName = "scatter.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("CSV出力が完了しました。", "出力", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private void ComvoBox_DropDownOpened(object sender, EventArgs e)
        {
            var viewModel = (PileSectionViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileSection.DeepCopy());
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var viewModel = (PileSectionViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileSection.DeepCopy());
        }

        private void ComboBox_DropDownOpened(object sender, EventArgs e)
        {
            var viewModel = (PileSectionViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileSection.DeepCopy());
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                {
                    var viewModel = (PileSectionViewModel)DataContext;
                    viewModel._undoManager.SaveState(viewModel.PileSection.DeepCopy());
                }
            }
        }

        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var viewModel = (PileSectionViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileSection.DeepCopy());
        }

        // ContextMenuが開かれたときにDataGridをCommandParameterに設定するイベントハンドラ
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is DataGrid dataGrid)
                {
                    foreach (MenuItem menuItem in contextMenu.Items)
                    {
                        menuItem.CommandParameter = dataGrid;
                    }
                }
            }
        }

        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is DataGrid dataGrid)
            {
                var data = dataGrid.ItemsSource.Cast<object>();
                {
                    DataGridCsv.Export(data, dataGrid);
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.RedrawShapes();
            viewModel.ChartUpdate();
        }
    }
}


