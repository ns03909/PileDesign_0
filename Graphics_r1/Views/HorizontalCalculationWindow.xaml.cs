using PileDesign.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PileDesign.Views
{
    /// <summary>
    /// HorizontalCalculationWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class HorizontalCalculationWindow : Window
    {
        private readonly bool _isAnalysisResultCheckShown = false; // フラグ追加
        private bool _isClosingHandled = false;

        public HorizontalCalculationWindow()
        {
            InitializeComponent();

            if (DataContext is HorizontalCalculationViewModel viewModel)
            {
                viewModel.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
                viewModel.RequestClose += (s, e) =>
                {
                    {
                        // すでにクローズ処理中なら何もしない
                        if (_isClosingHandled) return;
                        _isClosingHandled = true;

                        // メインウィンドウの TabAnalysisResult を選択
                        try
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                var mainWin = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "MainWindow");
                                if (mainWin != null)
                                {
                                    var tab = mainWin.FindName("TabAnalysisResult") as TabItem;
                                    if (tab != null)
                                    {
                                        tab.IsSelected = true;
                                    }
                                }
                            });
                        }
                        catch
                        {
                            // 選択失敗しても処理は続行
                        }

                        if (this.IsLoaded && this.IsVisible)
                        {
                            this.Close();
                        }
                    }
                    ;
                };
            }
            else
            {
                // DataContextがまだセットされていない場合、Loadedイベントで購読
                this.Loaded += HorizontalCalculationWindow_Loaded;
            }
        }

        private void CalculationLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogListBox.Items.Count > 0)
            {
                // UIスレッドの描画が終わった後にスクロール
                LogListBox.Dispatcher.BeginInvoke(() =>
                {
                    LogListBox.UpdateLayout();
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                });
            }
        }

        private void HorizontalCalculationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is HorizontalCalculationViewModel viewModel)
            {
                viewModel.RequestClose += (s, ev) =>
                {
                    // メインウィンドウの TabAnalysisResult を選択
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            var mainWin = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "MainWindow");
                            if (mainWin != null)
                            {
                                var tab = mainWin.FindName("TabAnalysisResult") as TabItem;
                                if (tab != null)
                                {
                                    tab.IsSelected = true;
                                }
                            }
                        });
                    }
                    catch
                    {
                        // ignore
                    }

                    this.Close();
                };
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


        private void ListBox_Loaded(object sender, RoutedEventArgs e)
        {

            var listBox = sender as ListBox;
            if (DataContext is PileDesign.ViewModels.HorizontalCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged += (s, args) =>
                {
                    if (listBox.Items.Count > 0)
                    {
                        listBox.ScrollIntoView(listBox.Items[^1]);
                    }
                };
            }
        }


        private bool _isDraggingSelection;
        private int _anchorIndex = -1;

        private void LogListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox lb) return;

            // クリック位置のアイテムを取得
            var item = GetListBoxItemFromPoint(lb, e.GetPosition(lb));
            if (item != null)
            {
                _anchorIndex = lb.ItemContainerGenerator.IndexFromContainer(item);
                if (_anchorIndex >= 0)
                {
                    // アンカーを初期選択に
                    lb.SelectedItems.Clear();
                    lb.SelectedIndex = _anchorIndex;
                    _isDraggingSelection = true;
                    lb.CaptureMouse();
                    e.Handled = true; // 既定の単一選択を抑制
                }
            }
        }

        private void LogListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingSelection || e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not ListBox lb) return;

            var item = GetListBoxItemFromPoint(lb, e.GetPosition(lb));
            if (item == null) return;

            int idx = lb.ItemContainerGenerator.IndexFromContainer(item);
            if (idx < 0 || _anchorIndex < 0) return;

            // 範囲選択を更新
            int start = Math.Min(_anchorIndex, idx);
            int end = Math.Max(_anchorIndex, idx);

            lb.SelectedItems.Clear();
            for (int i = start; i <= end; i++)
            {
                var cont = lb.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (cont != null) lb.SelectedItems.Add(cont.DataContext);
            }
        }

        private void LogListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox lb) return;

            if (_isDraggingSelection)
            {
                _isDraggingSelection = false;
                _anchorIndex = -1;
                if (lb.IsMouseCaptured) lb.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // ヒットテストから ListBoxItem を取得
        private static ListBoxItem? GetListBoxItemFromPoint(ListBox lb, Point p)
        {
            var element = lb.InputHitTest(p) as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = VisualTreeHelper.GetParent(element);
            return element as ListBoxItem;
        }

        // 既存: Ctrl+A/C のハンドラ（前回答の通り）
        private void LogListBox_SelectAllCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = LogListBox != null && LogListBox.Items.Count > 0;
            e.Handled = true;
        }

        private void LogListBox_SelectAllExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            LogListBox.SelectedItems.Clear();
            foreach (var item in LogListBox.Items)
                LogListBox.SelectedItems.Add(item);
            e.Handled = true;
        }

        private void LogListBox_CopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = LogListBox != null && LogListBox.Items.Count > 0;
            e.Handled = true;
        }

        private void LogListBox_CopyExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            var source = LogListBox.SelectedItems.Count > 0
                ? LogListBox.SelectedItems.Cast<object>()
                : LogListBox.Items.Cast<object>(); // 未選択時は全行コピー

            var text = string.Join(Environment.NewLine, source.Select(x => x?.ToString() ?? string.Empty));
            Clipboard.SetText(text);
            e.Handled = true;
        }
    }
}
