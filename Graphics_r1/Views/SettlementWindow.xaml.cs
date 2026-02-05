using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// SettlementWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettlementWindow : Window
    {
        // クラスフィールドに追加
        private bool _isClosingHandled = false;

        // コンストラクタ
        //public SettlementWindow()
        //{
        //    InitializeComponent();
        //    DataContext = new SettlementViewModel();
        //    Loaded += SettlementWindow_Loaded;
        //}
        //public SettlementWindow(MainWindowViewModel mainWindowViewModel)
        //{
        //    InitializeComponent();
        //    DataContext = new SettlementViewModel(mainWindowViewModel);
        //    Loaded += SettlementWindow_Loaded;
        //}
        // パラメータなしコンストラクタ（必須）
        public SettlementWindow()
        {
            InitializeComponent();
            // DataContextはOpenDialogWindowでセットされるので、ここでは何もしない
            Loaded += SettlementWindow_Loaded;
        }

        // 既存のMainWindowViewModelを受け取るコンストラクタは必要なら残してもOK
        public SettlementWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();
            DataContext = new SettlementViewModel(mainWindowViewModel);
            Loaded += SettlementWindow_Loaded;
        }

        private void SettlementWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettlementViewModel viewModel) return;
            viewModel.SettlementWindowInstance = this;
            viewModel.ComboBoxPresetSettlementParameters = ComboBoxPresetSettlementParameters;
            viewModel.Canvas = Canvas;

            // CanvasのSizeChangedイベントにハンドラを追加
            Canvas.SizeChanged += Canvas_SizeChanged;
            viewModel.RequestClose += (s, e) =>
            {
                {
                    // すでにクローズ処理中なら何もしない
                    if (_isClosingHandled) return;
                    _isClosingHandled = true;

                    if (this.IsLoaded && this.IsVisible)
                    {
                        this.Close();
                    }
                }
                ;
            };

            // グラフ描画を実行
            viewModel.ExecuteAnalysis();
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                viewModel.DrawShapes(); // Canvasに描画
            }
        }

        // 杭体番号選択が変化した場合のメソッド
        private void ComboBoxSoilPileBodyNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel && ComboBoxSoilPileNo.SelectedIndex + 1 is int selectedSoilPileNo)
            {
                int previousSelectedSoilPileNo = e.RemovedItems.Count > 0 ? (int)e.RemovedItems[0] : -1;
                viewModel.ComboBoxSoilPileNo_SelectionChanged(/*selectedSoilPileNo, */previousSelectedSoilPileNo);
            }
        }

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {

        }

        private void DataGridCircum_SelecitonChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
                viewModel.UpdateCircumstanceSeries();
        }

        private void DataGridLoadDisplacement_SelecitonChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
                viewModel.ExecuteAnalysis();
        }

        private void ComboBoxPresetSettlementParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                // 選択された値を取得
                var selectedParameter = (sender as ComboBox)?.SelectedItem as string;

                // 必要に応じて処理を実行
                if (!string.IsNullOrEmpty(selectedParameter))
                {
                    viewModel.PresetSettlementParametersChangedCommand?.Execute(selectedParameter);

                    // 解析結果タブを前面に
                    viewModel.SelectedTabIndex = 0;
                    var wpf = wpfPlotSettlement;
                    var wpfToe = wpfPlotSettlementToe;
                    wpf.Plot.Clear();
                    wpfToe.Plot.Clear();
                    wpf.Refresh();
                    wpfToe.Refresh();
                }
            }
        }

        private void OnPileBodyLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                viewModel.OnPileBodyLostFocus(sender);
            }
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                textBox.SelectAll(); // ← 追加
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {

                var textBox = sender as TextBox;
                var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                binding?.UpdateSource();
            }
        }

        private void TextBox_PreviewLostKeyboartFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                viewModel.UndoManager.SaveState(
                    new ObservableCollection<SoilPile>(viewModel.SoilPiles.Select(p => p.DeepCopy()))
                );

            }
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

        // CSVエクスポートのコンテキストメニュークリックイベントハンドラ
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

        //private void TextBoxSettleAlpha_TextChanged(object sender, TextChangedEventArgs e)
        //{
        //    if (DataContext is SettlementViewModel viewModel)
        //    {
        //        viewModel.AddComponent(InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha, InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN);
        //    }

        //}
    }
}
