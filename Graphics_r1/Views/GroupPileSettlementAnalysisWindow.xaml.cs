using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// GroupPileSettlementAnalysisWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GroupPileSettlementAnalysisWindow : Window
    {
        private readonly MainWindowViewModel viewModel;

        public GroupPileSettlementAnalysisWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();
            viewModel = mainWindowViewModel;
            DataContext = viewModel;
        }

        // 群杭荷重タイプ変化時のメソッド
        private void ComboBoxLoadingType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox?.SelectedItem as string;
            // 個別十字系に切り替わった場合は RectLoads を自動生成で置換
            if (selectedItem == "個別十字" || selectedItem == "個別十字（基礎梁考慮）")
            {
                // UpdateSourceTrigger=LostFocus のためモデル側 LoadingType が
                // まだ古い値の可能性 → 先にソース更新してから再生成
                comboBox?.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
                viewModel.RebuildAutoCrossRectLoadsIfNeeded();
            }

            viewModel.UpdateWindowAction();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                //e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBoxBottomAltitude_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.TextBoxBottomAltitude_OnPreviewMouseDownCommand.Execute(e);
        }

        private void TextBoxAltitude_TextChanged(object sender, TextChangedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.TextBoxAltitude_OnTextChangedCommand.Execute(e);
        }

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void DataGridSoilPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridSoilPile_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridSettlementSoilLayers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column is DataGridTextColumn textColumn)
            {
                // Header が StackPanel である場合、その中の TextBlock の内容を確認
                if (textColumn.Header is StackPanel headerPanel)
                {
                    foreach (var child in headerPanel.Children)
                    {
                        if (child is TextBlock textBlock && textBlock.Text.Contains("下端Z"))
                        {
                            // 下端標高列のセルが編集された場合の処理
                            var dataGrid = sender as DataGrid;
                            var editedItem = e.Row.Item as SettlementSoilLayer;
                            var editedTextBox = e.EditingElement as TextBox;

                            if (double.TryParse(editedTextBox.Text, out double newValue))
                            {
                                int rowIndex = dataGrid.Items.IndexOf(editedItem);
                                if (rowIndex > 0)
                                {
                                    var previousItem = dataGrid.Items[rowIndex - 1] as SettlementSoilLayer; // SettlementSoilLayer は適切なモデルクラスに置き換えてください
                                    if (newValue >= previousItem.BottomAltitude)
                                    {
                                        MessageBox.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                                        e.Cancel = true;

                                        // 編集内容を元に戻す
                                        editedTextBox.Text = editedItem.BottomAltitude.ToString("F2");
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }

            // 厚さ列の再計算
            RecalculateThickness();
        }

        private void RecalculateThickness()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var settlementSoilLayers = viewModel.CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            if (settlementSoilLayers == null || settlementSoilLayers.Count == 0) return;

            double loadSurfaceAltitude = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                {
                    // 1行目の厚さは荷重面標高から下端標高の深さ
                    settlementSoilLayers[i].Thickness = loadSurfaceAltitude - settlementSoilLayers[i].BottomAltitude;
                }
                else
                {
                    // 2行目以降の厚さは直上の下端標高からその行の下端標高の深さ
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude;
                }
            }
        }
        private void DataGridRectLoads_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridRectLoads_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridPileSettlement_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void DataGridPileSettlement_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {

        }

        private void DataGridPileSettlement_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {

        }

        private void DataGridPileAxialForce_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void DataGridPileSettlement_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {

        }

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
    }
}
