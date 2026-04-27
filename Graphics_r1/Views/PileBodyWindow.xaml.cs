using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    public partial class PileBodyWindow : Window
    {
        private readonly PileBodyViewModel _viewModel;
        private bool _isLoaded = false; // フラグを追加
        private bool _isClosingHandled = false;
        // 既存のMainWindowViewModelを受け取るコンストラクタは削除せず、追加する
        public PileBodyWindow()
        {
            InitializeComponent();
            // DataContextはOpenDialogWindowでセットされるので、ここでは何もしない
            Loaded += PileBodyWindow_Loaded;
        }

        // 必要ならMainWindowViewModelを受け取るコンストラクタも残す
        public PileBodyWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();
            _viewModel = new PileBodyViewModel(mainWindowViewModel);
            DataContext = _viewModel;
            Loaded += PileBodyWindow_Loaded;
        }

        // コンストラクタ
        //public PileBodyWindow(MainWindowViewModel mainWindowViewModel)
        //{
        //    InitializeComponent();

        //    _viewModel = new PileBodyViewModel(mainWindowViewModel);
        //    DataContext = _viewModel;

        //    Loaded += PileBodyWindow_Loaded;
        //}

        private void PileBodyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return; // 既にロード済みなら処理をスキップ

            var viewModel = (PileBodyViewModel)DataContext;
            viewModel.Canvas = Canvas;
            viewModel.DrawShapes(); // Canvasに描画
            // イベントハンドラを追加
            Canvas.SizeChanged += Canvas_SizeChanged;

            _isLoaded = true; // フラグを設定
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
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var viewModel = (PileBodyViewModel)DataContext;
            viewModel.DrawShapes();
            viewModel.UpdateTemporarySoilPile();
        }

        private void DataGridPileBody_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var viewModel = (PileBodyViewModel)DataContext;
                viewModel.PileBodies[viewModel.PileBodyNo - 1].PileBodySegmentsUpdate();
                viewModel.DrawShapes();
                viewModel.UpdateTemporarySoilPile();
            }
        }

        private void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // DataGridの高さを行数に合わせて調整する
            if (sender is DataGrid grid && grid.Items.Count > 0)
            {
                double rowHeight = grid.RowHeight;
                int rowCount = grid.Items.Count;
                double totalHeight = rowHeight * rowCount;
                grid.Height = totalHeight;
            }
        }

        private void DataGridPileBody_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void PileBodyCollection_CollectionChanged(
            object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridPileBody.Items.Count; i++)
                {
                    if (DataGridPileBody.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
        }

        private void DataGridPileBody_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridPileBody.ItemsSource is ObservableCollection<PileBodySegment> observableCollection)
            {
                observableCollection.CollectionChanged += PileBodyCollection_CollectionChanged;
            }
        }


        // 杭体番号選択が変化した場合のメソッド
        //private void ComboBoxPileBodyNoSelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    //if (DataContext is PileBodyViewModel viewModel &&
        //    //    ComboBoxPileBodyNo.SelectedIndex + 1 is int selectedPileBodyNo)
        //        //{
        //        //    int previousSelectedGroundNo = e.RemovedItems.Count > 0 ? (int)e.RemovedItems[0] : -1;
        //        //    viewModel.ComboBoxPileBodyNo_SelectionChanged(
        //        //        selectedPileBodyNo, previousSelectedGroundNo);
        //        //}
        //        if (DataContext is PileBodyViewModel viewModel)
        //        {
        //            int selectedIndex = ComboBoxPileBodyNo.SelectedIndex;
        //            int previousSelectedIndex = -1;
        //            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string removedItem)
        //            {
        //                previousSelectedIndex = ComboBoxPileBodyNo.Items.IndexOf(removedItem);
        //            }
        //            viewModel.ComboBoxPileBodyNo_SelectionChanged(selectedIndex, previousSelectedIndex);
        //        }
        //}

        // PileBodyWindow.xaml.cs
        //private void ComboBoxPileBodyNoSelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (DataContext is PileBodyViewModel vm)
        //    {
        //        int previous = e.RemovedItems.Count > 0
        //            ? vm.PileBodyNo
        //            : -1;
        //        int selectedIndex = ((ComboBox)sender).SelectedIndex;

        //        vm.ComboBoxPileBodyNo_SelectionChanged(selectedIndex, previous);

        //        // (New)が選択された直後は、必ず新しいアイテムを選択状態にする
        //        if (selectedIndex == vm.PileBodiesCountPlusOneList.Count -1) ///- 1)
        //        {
        //            // ItemsSource更新後、SelectedIndexを明示的に新しいアイテムにセット
        //            ComboBoxPileBodyNo.SelectedIndex = vm.PileBodies.Count-1 /*- 1*/;
        //        }
        //    }
        //}
        private void ComboBoxPileBodyNoSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not PileBodyViewModel vm)
                return;

            // Dispatcherで遅延実行し、選択状態が確定した後に処理する
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                int selectedIndex = ComboBoxPileBodyNo.SelectedIndex;
                int previousSelectedIndex = -1;
                if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string removedItem)
                {
                    previousSelectedIndex = ComboBoxPileBodyNo.Items.IndexOf(removedItem);
                }
                vm.ComboBoxPileBodyNo_SelectionChanged(selectedIndex/*, previousSelectedIndex*/);

                // (New)が選択された直後は、必ず新しいアイテムを選択状態にする
                if (selectedIndex == vm.PileBodiesCountPlusOneList.Count - 1)
                {
                    ComboBoxPileBodyNo.SelectedIndex = vm.PileBodies.Count - 1;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is PileBodyViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is PileBodyViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
        }

        private void PileTopAltitude_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // TextBoxのバインディングを即時反映
                if (sender is TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }

                if (DataContext is PileBodyViewModel viewModel)
                {
                    viewModel.DrawShapes();
                    viewModel.UpdateTemporarySoilPile();
                    e.Handled = true; // 必要ならイベントのバブリングを止める
                }
            }
        }

        private void OnPileBodyLostFocus(object sender, RoutedEventArgs e)
        {

        }

        private void ComboBox_DropDownOpened(object sender, System.EventArgs e)
        {
            var viewModel = (PileBodyViewModel)DataContext;
            viewModel._undoManager.SaveState(new ObservableCollection<PileBodyInput>(
                viewModel.PileBodies.Select(pb => pb.DeepCopy())
            ));
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var viewModel = (PileBodyViewModel)DataContext;
                viewModel._undoManager.SaveState(new ObservableCollection<PileBodyInput>(
                    viewModel.PileBodies.Select(pb => pb.DeepCopy())
                ));
            }
        }

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var viewModel = (PileBodyViewModel)DataContext;
            viewModel._undoManager.SaveState(new ObservableCollection<PileBodyInput>(
                viewModel.PileBodies.Select(pb => pb.DeepCopy())
            ));
        }


        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var viewModel = (PileBodyViewModel)DataContext;
            viewModel._undoManager.SaveState(new ObservableCollection<PileBodyInput>(
                viewModel.PileBodies.Select(pb => pb.DeepCopy())
            ));
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void OnPileBodyPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {

        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                textBox.SelectAll();
                e.Handled = true;
            }
        }

        // v29 (2026-04-27): AvalonDock 各ペインの最大化トグル
        // 既定の DockWidth は 0.5* / 0.25* / 0.25*。最大化対象ペインは 0.999*、他ペインは 0.0005* で
        // 実質非表示状態にする (0 にすると AvalonDock が再描画でレイアウトを破壊するため僅かに残す)。
        // 同じボタンを再度押すと既定の比率に戻す。
        private string _maximizedPane = null; // null=通常, "Input"/"Shape"/"Result"=最大化中

        private void ButtonMaximizePane_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag) return;

            const double inputDefault = 0.5, shapeDefault = 0.25, resultDefault = 0.25;
            const double maximized = 0.999, minimized = 0.0005;

            if (_maximizedPane == tag)
            {
                // 同じボタン → 元に戻す
                if (PileBodyInputPaneGroup != null) PileBodyInputPaneGroup.DockWidth = new System.Windows.GridLength(inputDefault, System.Windows.GridUnitType.Star);
                if (PileShapeImagePaneGroup != null) PileShapeImagePaneGroup.DockWidth = new System.Windows.GridLength(shapeDefault, System.Windows.GridUnitType.Star);
                if (ResultPaneGroup != null) ResultPaneGroup.DockWidth = new System.Windows.GridLength(resultDefault, System.Windows.GridUnitType.Star);
                _maximizedPane = null;
            }
            else
            {
                // 別ペイン or 未最大化 → tag のペインを最大化
                double inputW = (tag == "Input") ? maximized : minimized;
                double shapeW = (tag == "Shape") ? maximized : minimized;
                double resultW = (tag == "Result") ? maximized : minimized;
                if (PileBodyInputPaneGroup != null) PileBodyInputPaneGroup.DockWidth = new System.Windows.GridLength(inputW, System.Windows.GridUnitType.Star);
                if (PileShapeImagePaneGroup != null) PileShapeImagePaneGroup.DockWidth = new System.Windows.GridLength(shapeW, System.Windows.GridUnitType.Star);
                if (ResultPaneGroup != null) ResultPaneGroup.DockWidth = new System.Windows.GridLength(resultW, System.Windows.GridUnitType.Star);
                _maximizedPane = tag;
            }
        }
    }
}