
using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace PileDesign.Views
{
    /// <summary>
    /// VerticalCalculationWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class VerticalCalculationWindow : Window
    {
        // コンストラクタ
        public VerticalCalculationWindow()
        {
            InitializeComponent();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                //textBox.Focus();
                //e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        // 行番号を設定するメソッド
        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridPileLayout_OnLoadingRowCommand.Execute(e); // ビューモデルのコマンドを実行

            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void DataGridGroundLayer_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is VerticalCalculationViewModel viewModel)
            {
                //viewModel.DataGridGroundLayer_CellEditEnding();
            }
        }

        private void GroundTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is VerticalCalculationViewModel viewModel)
            {
                if (sender is TextBox textBox)
                {
                    //viewModel.GroundTextBox_TextChanged(textBox.Name, textBox.Text);
                }
            }
        }

        private void GroundTopAltitudeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is VerticalCalculationViewModel viewModel)
            {
                if (sender is TextBox textBox)
                {
                    //viewModel.GroundTopAltitudeTextBox_TextChanged(textBox.Text);
                }
            }
        }

        // GroundNoのComboBoxの選択が変更されたときの処理
        private void ComboBoxGroundNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is VerticalCalculationViewModel viewModel)
            {
                if (ComboBoxGroundNo.SelectedItem != null)
                {
                    int previousSelectedGroundNo = -1;
                    if (e.RemovedItems.Count > 0)
                    {
                        previousSelectedGroundNo = (int)e.RemovedItems[0];
                    }
                    int selectedGroundNo = (int)ComboBoxGroundNo.SelectedItem;
                    viewModel.ComboBoxGroundNo_SelectionChanged(selectedGroundNo, previousSelectedGroundNo);
                }
            }
        }

        private void DataGridPileLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //UpdateSelectedPileLayoutItems(DataGridPileLayout);
        }


        // DataGridPileLayoutの右クリックメソッド
        private void DataGridPileLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel != null && e.RightButton == MouseButtonState.Pressed)
            {
                var cm = FindResource("NodeContextMenu") as ContextMenu;
            }
        }

        private void DataGridPileAxialForce_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            //viewModel?.DataGridPileLayout_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            //viewModel?.DataGridPileLayout_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridPileLayout_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {

        }
    }
}
