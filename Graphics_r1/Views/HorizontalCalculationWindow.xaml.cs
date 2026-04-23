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
            else if (e.Key == Key.Escape)
            {
                if (DataContext is HorizontalCalculationViewModel vm)
                {
                    if (vm.IsAnalysisRunning && vm.CancelAnalysisCommand.CanExecute(null))
                        vm.CancelAnalysisCommand.Execute(null);
                    else if (vm.CancelCommand?.CanExecute(null) == true)
                        vm.CancelCommand.Execute(null);
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

        // ScrollToEnd の BeginInvoke coalesce 用フラグ (UI スレッドのみアクセスなので volatile 不要)
        private bool _scrollToEndPending;
        // smart scroll: 追記の直前にユーザーが最下段付近に居たかどうか。
        // 居た場合のみ追記後に自動スクロールする。手動で上を見ている時は引き戻さない。
        private const double AutoScrollThresholdPx = 32.0;

        private bool IsLogTextBoxAtBottom()
        {
            if (LogTextBox == null) return true;
            // VerticalOffset + ViewportHeight >= ExtentHeight - threshold なら最下段付近
            double offset = LogTextBox.VerticalOffset;
            double viewport = LogTextBox.ViewportHeight;
            double extent = LogTextBox.ExtentHeight;
            // 初期状態 (extent=0) も bottom 扱い → 最初の追記ではスクロールする
            if (extent <= 0) return true;
            return offset + viewport >= extent - AutoScrollThresholdPx;
        }

        private void CalculationLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // UI フリーズ対策: Text binding は廃止し、AppendText で増分追記。
            // これにより WPF TextBox の全文再レイアウト O(total text) を避け、
            // 1 行追加あたり O(1) の処理で済む。
            if (LogTextBox == null) return;

            // 追記「前」にユーザーが最下段付近に居たかを記録 (smart scroll 判定)
            bool wasAtBottom = IsLogTextBoxAtBottom();

            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (string item in e.NewItems)
                    LogTextBox.AppendText(item + Environment.NewLine);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                LogTextBox.Clear();
            }
            // Replace/Remove/Move 等は未対応 (この VM では Add/Reset のみ発生)

            // smart scroll: 追記前に最下段に居なかった場合はスクロールしない
            // (手動でログを遡って読んでいるユーザーの位置を維持)
            if (!wasAtBottom) return;

            // ScrollToEnd は O(text size) のため多数回呼ばない。1 フラッシュ 1 回に coalesce。
            if (_scrollToEndPending) return;
            _scrollToEndPending = true;
            LogTextBox.Dispatcher.BeginInvoke(() =>
            {
                _scrollToEndPending = false;
                LogTextBox?.ScrollToEnd();
            });
        }

        /// <summary>
        /// LogTextBox を CalculationLog の現在内容で一度に再構築する。
        /// DataContext 変更時や初回 subscribe 時に使う。
        /// </summary>
        private void PopulateLogTextBox(System.Collections.Generic.IEnumerable<string>? items)
        {
            if (LogTextBox == null) return;
            LogTextBox.Clear();
            if (items == null) return;
            foreach (var item in items)
                LogTextBox.AppendText(item + Environment.NewLine);
            LogTextBox.ScrollToEnd();
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
                // 既存ログがあれば初期表示 (通常は空)
                PopulateLogTextBox(vm.CalculationLog);

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
                // 新 VM のログを LogTextBox に反映 (Text binding 廃止対応)
                PopulateLogTextBox(newVm.CalculationLog);
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
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    // アニメ終了時に Value を「描画保持」せず、ローカル/バインディング値が再び有効になるようにする
                    FillBehavior = FillBehavior.Stop
                };

                // 完了時に ViewModel.CurrentProgress を 0 にし、アニメーション自体も解除してバインディングを復帰
                anim.Completed += (s, ev) =>
                {
                    if (this.DataContext is HorizontalCalculationViewModel vm)
                    {
                        vm.CurrentProgress = 0;
                    }
                    // null を渡すことで Animated 値を解除。以降 Value は Binding の値（CurrentProgress）で更新される。
                    ProgressBarMain?.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
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
        private void CopyToClipboardFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is DataGrid dataGrid)
            {
                Output.DataGridCsv.CopyToClipboard(dataGrid);
            }
        }
    }
}
