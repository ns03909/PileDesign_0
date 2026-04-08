using PileDesign.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

            this.Loaded += HorizontalCalculationWindow_Loaded;
            this.Unloaded += HorizontalCalculationWindow_Unloaded;
            this.Closing += HorizontalCalculationWindow_Closing;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is HorizontalCalculationViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// ウィンドウが閉じられる際に呼び出される
        /// 実行中の解析をキャンセルし、クリーンアップを行う
        /// </summary>
        private async void HorizontalCalculationWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;  // 最初にフラグを立てて再入を防止

            if (this.DataContext is HorizontalCalculationViewModel vm && vm.IsAnalysisRunning)
            {
                // 解析中なら一旦閉じるのをキャンセルし、クリーンアップ後に再度閉じる
                e.Cancel = true;

                // 解析をキャンセルして待機
                await vm.CleanupAsync();

                // クリーンアップ完了後、ウィンドウを閉じる
                // _isClosingHandledは既にtrueなので、再度Closingイベントが発生してもスキップされる
                this.Close();
            }
            // 解析が実行中でない場合はそのまま閉じる（_isClosingHandledはtrueのまま）
        }

        private void CalculationLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // TextBoxの末尾にスクロール
            LogTextBox?.Dispatcher.BeginInvoke(() =>
            {
                LogTextBox.ScrollToEnd();
            });
        }

        private async void HorizontalCalculationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // DataContext が ViewModel の場合はイベントを購読
            if (this.DataContext is HorizontalCalculationViewModel vm)
            {
                // イベントの多重購読を防ぐため、一度解除してから購読
                vm.RequestClearProgressAnimation -= Vm_RequestClearProgressAnimation;
                vm.RequestClearProgressAnimation += Vm_RequestClearProgressAnimation;

                vm.RequestShowWarning -= OnRequestShowWarning;
                vm.RequestShowWarning += OnRequestShowWarning;

                vm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
                vm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;

                vm.RequestClose -= OnRequestClose;
                vm.RequestClose += OnRequestClose;

                // ウィンドウ表示後にバックグラウンドでFEMモデルを作成
                await vm.InitializeModelAsync();
            }
            else
            {
                // DataContext が後からセットされる可能性に備えて監視
                this.DataContextChanged += HorizontalCalculationWindow_DataContextChanged;
            }
        }

        /// <summary>
        /// RequestCloseイベントハンドラ（匿名ラムダの代わりに名前付きメソッドを使用）
        /// </summary>
        private void OnRequestClose(object sender, EventArgs e)
        {
            // すでにクローズ処理中なら何もしない
            if (_isClosingHandled) return;
            // 注: ここでは_isClosingHandledをtrueにしない
            // Closingイベントハンドラで解析キャンセル後にtrueにする

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


        private void HorizontalCalculationWindow_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is HorizontalCalculationViewModel vm)
            {
                vm.RequestClearProgressAnimation -= Vm_RequestClearProgressAnimation;
                vm.RequestShowWarning -= OnRequestShowWarning;
                vm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
                vm.RequestClose -= OnRequestClose;
                vm.UnsubscribeEvents(); // LoadCase/LoadCombinationのPropertyChanged購読を解除
            }
            this.Loaded -= HorizontalCalculationWindow_Loaded;
            this.Unloaded -= HorizontalCalculationWindow_Unloaded;
            this.Closing -= HorizontalCalculationWindow_Closing;
            this.DataContextChanged -= HorizontalCalculationWindow_DataContextChanged;
        }

        private void HorizontalCalculationWindow_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is HorizontalCalculationViewModel oldVm)
            {
                oldVm.RequestClearProgressAnimation -= Vm_RequestClearProgressAnimation;
                oldVm.RequestShowWarning -= OnRequestShowWarning;
                oldVm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
                oldVm.RequestClose -= OnRequestClose;
            }
            if (e.NewValue is HorizontalCalculationViewModel newVm)
            {
                newVm.RequestClearProgressAnimation += Vm_RequestClearProgressAnimation;
                newVm.RequestShowWarning += OnRequestShowWarning;
                newVm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
                newVm.RequestClose += OnRequestClose;
            }
        }

        private void Vm_RequestClearProgressAnimation()
        {
            // UI スレッドで実行
            Dispatcher.Invoke(() =>
            {
                if (ProgressBarMain == null) return;

                // 現在値から 0 へ滑らかにアニメ
                var duration = TimeSpan.FromMilliseconds(400);
                var anim = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = new Duration(duration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                // 完了時に ViewModel.CurrentProgress を 0 に合わせる
                anim.Completed += (s, ev) =>
                {
                    if (this.DataContext is HorizontalCalculationViewModel vm)
                    {
                        vm.CurrentProgress = 0;
                    }
                };

                ProgressBarMain.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
            });
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
            // 注: CalculationLog.CollectionChangedはHorizontalCalculationWindow_Loadedで
            //     CalculationLog_CollectionChangedとして購読済みのため、ここでは追加購読しない
            //     （多重購読によるメモリリークとパフォーマンス低下を防止）
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

        // TextBoxに変更したため、Ctrl+A（全選択）/ Ctrl+C（コピー）は標準機能で動作


        // View がメッセージ表示を担当
        private void OnRequestShowWarning(string message)
        {
            // UI スレッドで確実に表示
            MessageBox.Show(message, "解析中止", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // TextBox の数値入力制限（小数点と数字のみ許可）
        private static readonly Regex _numericRegex = new(@"^[0-9]*(\.[0-9]*)?$", RegexOptions.Compiled);

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 既存テキストに追加したときに有効な数値かをチェック
            if (sender is TextBox tb)
            {
                string prospective = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                     .Insert(tb.SelectionStart, e.Text);
                e.Handled = !_numericRegex.IsMatch(prospective);
            }
        }

        private void NumericOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                e.CancelCommand();
                return;
            }
            var text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!_numericRegex.IsMatch(text))
                e.CancelCommand();
        }
    }
}
