using PileDesign.Common;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

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

            // 杭先端沈下グラフを描画（α, nのデフォルト値で）
            viewModel.AddComponent(viewModel.PileBody.SettleAlpha, viewModel.PileBody.SettleN);

            // 全SoilPileを一括解析
            viewModel.AnalyzeAllSoilPiles();

            // 最初に表示されるSoilPileのランプを点灯（ユーザーが確認済み）
            viewModel.MarkCurrentPileAsConfirmed();

            // 杭姿図を描画
            viewModel.DrawShapes();

            // CSVエクスポートメニューを追加
            PlotHelper.AddCsvExportMenu(wpfPlotSettlement, "杭頭沈下");
            PlotHelper.AddCsvExportMenu(wpfPlotSettlementToe, "杭先端沈下");
            PlotHelper.AddCsvExportMenu(wpfPlotCircum, "周面抵抗");
            PlotHelper.AddCsvExportMenu(wpfPlotPileToe, "杭先端支持力");
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

        // 杭周面抵抗力表の選択解除（選択行を再クリックで解除）
        private void DataGridCircum_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;

            // クリックされた要素からDataGridRowを取得（VisualTreeを使用）
            var clickedRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

            if (clickedRow != null && clickedRow.IsSelected)
            {
                // 既に選択されている行をクリックした場合は選択解除
                dataGrid.SelectedItem = null;
                e.Handled = true;
            }
        }

        private void DataGridLoadDisplacement_SelecitonChanged(object sender, SelectionChangedEventArgs e)
        {
            // 選択が変わったらチャートを更新して選択位置を強調表示
            if (DataContext is SettlementViewModel viewModel)
                viewModel.UpdateSettlementChartSelection();
        }

        // 沈下曲線表の選択解除（選択行を再クリックで解除）
        private void DataGridLoadDisplacement_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;

            // クリックされた要素からDataGridRowを取得（VisualTreeを使用）
            var clickedRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

            if (clickedRow != null && clickedRow.IsSelected)
            {
                // 既に選択されている行をクリックした場合は選択解除
                dataGrid.SelectedItem = null;
                e.Handled = true;
            }
        }

        // VisualTree上で指定した型の親要素を検索するヘルパーメソッド
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void ComboBoxPresetSettlementParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                // 選択された値を取得
                var selectedParameter = (sender as ComboBox)?.SelectedItem as string;

                // 必要に応じて処理を実行（ViewModelのExecuteAnalysisで解析とグラフ更新を行う）
                if (!string.IsNullOrEmpty(selectedParameter))
                {
                    viewModel.PresetSettlementParametersChangedCommand?.Execute(selectedParameter);
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

        // ランプクリック時に該当SoilPileを選択
        private void SoilPileLamp_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettlementViewModel vm) return;
            if (sender is not Button btn) return;
            if (btn.DataContext is LampState lamp)
            {
                vm.SelectSoilPileIndex(lamp.Index);
                // ComboBoxの選択も同期
                ComboBoxSoilPileNo.SelectedIndex = lamp.Index;
            }
        }

        // 押込み/引抜き周面抵抗考慮チェックボックス変更時の処理
        private void CircumResistanceCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettlementViewModel viewModel)
            {
                // 支持力を再計算（Rfu, Rt_SLS, Rt_DLS, Rt_ULS等）とUI通知
                viewModel.RecalculateResistances();
                // 沈下を再計算
                viewModel.ExecuteAnalysis();
            }
        }
    }
}
