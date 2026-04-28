//using DocumentFormat.OpenXml.Math;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    public partial class LoadCaseWindow : Window
    {
        private bool _isClosingHandled = false;

        public LoadCaseWindow()
        {
            InitializeComponent();
            DataContextChanged += LoadCaseWindow_DataContextChanged;
            Loaded += LoadCaseWindow_Loaded; // ウィンドウがロードされたときにイベントを追加
            this.Loaded += RegisterRequestCloseEvent;
        }

        private void RegisterRequestCloseEvent(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.RequestClose -= LoadCaseViewModel_RequestClose;
                vm.RequestClose += LoadCaseViewModel_RequestClose;
            }
        }


        private void LoadCaseWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            RefreshBindingProxy();
        }


        public void RefreshBindingProxy()
        {
            if (Resources["ViewModelProxy"] is PileDesign.Common.BindingProxy proxy)
            {
                proxy.Data = DataContext;
            }
        }

        private void LoadCaseViewModel_RequestClose(object sender, EventArgs e)
        {
            if (_isClosingHandled) return; // すでにクローズ処理中なら何もしない
            _isClosingHandled = true;

            if (this.IsLoaded && this.IsVisible)
            {
                this.Close();
            }
        }

        private void LoadCaseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is PileDesign.ViewModels.LoadCaseViewModel vm)
            {
                vm.LoadCaseWindowInstance = this;
            }
            CanvasDrawingService.DrawLoadCombination(LoadCombinationCanvas/*, GetLoadCombinationData()*/);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LoadCombinationCanvas.Children.Clear(); // 既存の描画をクリア
            CanvasDrawingService.DrawLoadCombination(LoadCombinationCanvas/*, GetLoadCombinationData()*/);
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.PushUndoState();
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.SetTotalMassForces();
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is LoadCaseViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
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
                textBox.Focus();
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                    binding?.UpdateSource();
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is LoadCaseViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void DataGridLoadCombination_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.PushUndoState();
            }
        }

        private void DataGridLoadCasesLevel1_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.PushUndoState();
            }
        }

        private void DataGridLoadCasesLevel2_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.PushUndoState();
            }
        }

        private void DataGridCommonLoadCase1_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // 編集内容が反映された後にUndo履歴を積む
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (DataContext is LoadCaseViewModel vm)
                    {
                        vm.PushUndoState();
                    }
                }));
            }
        }

        private void DataGridCommonLoadCase2_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is LoadCaseViewModel vm)
            {
                vm.PushUndoState();
            }
        }

        private bool _isSplit;

        private void ButtonToggleSplit_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSplit)
            {
                // タブ → 縦分割
                var ovr = TabItemOverlap.Content as System.Windows.UIElement;
                var lc = TabItemLoadCase.Content as System.Windows.UIElement;
                TabItemOverlap.Content = null;
                TabItemLoadCase.Content = null;
                SplitOverlapHost.Content = ovr;
                SplitLoadCaseHost.Content = lc;
                MainTabControl.Visibility = Visibility.Collapsed;
                SplitView.Visibility = Visibility.Visible;
                ButtonToggleSplit.Content = "タブ表示に戻す";
                _isSplit = true;
            }
            else
            {
                // 縦分割 → タブ
                var ovr = SplitOverlapHost.Content as System.Windows.UIElement;
                var lc = SplitLoadCaseHost.Content as System.Windows.UIElement;
                SplitOverlapHost.Content = null;
                SplitLoadCaseHost.Content = null;
                TabItemOverlap.Content = ovr;
                TabItemLoadCase.Content = lc;
                SplitView.Visibility = Visibility.Collapsed;
                MainTabControl.Visibility = Visibility.Visible;
                ButtonToggleSplit.Content = "両タブ前面表示";
                _isSplit = false;
            }
        }
    }
}
