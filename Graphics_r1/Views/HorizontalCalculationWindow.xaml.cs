using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
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

        // 並列モニタウィンドウ (案 B, 2026-04-24)。解析中のみ表示。
        private ParallelMonitorWindow? _parallelMonitorWindow;

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

            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HorizontalCalculationWindow_Closing] {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show($"ウィンドウ終了処理でエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ScrollToEnd の BeginInvoke coalesce 用フラグ (UI スレッドのみアクセスなので volatile 不要)
        private bool _scrollToEndPending;
        // smart scroll: 追記の直前にユーザーが最下段付近に居たかどうか。
        // 居た場合のみ追記後に自動スクロールする。手動で上を見ている時は引き戻さない。
        private const double AutoScrollThresholdPx = 32.0;

        // ケースタグ抽出正規表現: [L2-1.C1.Liq] 等、[Lx-x.Cx.(Liq|Dry)]
        private static readonly Regex CaseTagPattern = new(
            @"\[L\d+-\d+\.C\d+\.(?:Liq|Dry)\]", RegexOptions.Compiled);

        // 動的生成したケースタブの TextBox (ケースタグ → TextBox)
        private readonly Dictionary<string, TextBox> _caseTabTextBoxes = new();

        private static string? ExtractCaseTag(string? line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var match = CaseTagPattern.Match(line);
            return match.Success ? match.Value : null;
        }

        private bool IsTextBoxAtBottom(TextBox? tb)
        {
            if (tb == null) return true;
            double offset = tb.VerticalOffset;
            double viewport = tb.ViewportHeight;
            double extent = tb.ExtentHeight;
            if (extent <= 0) return true;
            return offset + viewport >= extent - AutoScrollThresholdPx;
        }

        /// <summary>現在表示中のタブに含まれる TextBox。All タブは LogTextBox。</summary>
        private TextBox? GetVisibleTabTextBox()
        {
            if (LogTabControl?.SelectedItem is TabItem tab && tab.Content is TextBox tb) return tb;
            return LogTextBox;
        }

        /// <summary>指定ケースタグ用の TextBox を返す (必要なら新規タブ生成)。</summary>
        private TextBox EnsureCaseTab(string caseTag)
        {
            if (_caseTabTextBoxes.TryGetValue(caseTag, out var existing))
                return existing;

            var tb = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("MS Gothic"),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
                AcceptsReturn = true,
            };
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "コピー (Ctrl+C)", Command = ApplicationCommands.Copy });
            menu.Items.Add(new MenuItem { Header = "全選択 (Ctrl+A)", Command = ApplicationCommands.SelectAll });
            tb.ContextMenu = menu;

            var tab = new TabItem { Header = caseTag, Content = tb };
            LogTabControl.Items.Add(tab);
            _caseTabTextBoxes[caseTag] = tb;
            return tb;
        }

        /// <summary>「すべて」以外のケースタブを全削除してキャッシュもクリア。</summary>
        private void ClearCaseTabs()
        {
            if (LogTabControl == null) return;
            // index 0 = "すべて" タブ。後ろから削除
            for (int i = LogTabControl.Items.Count - 1; i >= 1; i--)
                LogTabControl.Items.RemoveAt(i);
            _caseTabTextBoxes.Clear();
        }

        private void CalculationLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // UI フリーズ対策: Text binding は廃止し、AppendText で増分追記。
            // 1 行追加あたり O(1) で TextContainer に追記するため UI をブロックしない。
            // Option B (2026-04-24): ケースタグ [Lx-x.Cx.(Liq|Dry)] が含まれる行は
            // 「すべて」タブと併せて対応するケースタブにも追記する。
            // 案 X (2026-04-24): MDOP=1 (逐次) では並列追跡の必要がないためケースタブを作らない。
            if (LogTextBox == null || LogTabControl == null) return;

            // 追記前に visible タブの TextBox が最下段付近に居たか判定 (smart scroll)
            var visibleTextBox = GetVisibleTabTextBox();
            bool wasAtBottom = IsTextBoxAtBottom(visibleTextBox);

            // ケースタブを作るかどうか: MDOP > 1 の時のみ作成
            bool createCaseTabs = (DataContext as HorizontalCalculationViewModel)?.MaxCaseDegreeOfParallelism > 1;

            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (string item in e.NewItems)
                {
                    string line = item + Environment.NewLine;
                    LogTextBox.AppendText(line);  // 常に「すべて」タブへ追記

                    if (createCaseTabs)
                    {
                        string? caseTag = ExtractCaseTag(item);
                        if (caseTag != null)
                        {
                            var caseTb = EnsureCaseTab(caseTag);
                            caseTb.AppendText(line);
                        }
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                LogTextBox.Clear();
                ClearCaseTabs();
            }

            if (!wasAtBottom) return;
            if (_scrollToEndPending) return;
            _scrollToEndPending = true;
            LogTextBox.Dispatcher.BeginInvoke(() =>
            {
                _scrollToEndPending = false;
                // 現在表示中のタブの TextBox を末尾へスクロール
                GetVisibleTabTextBox()?.ScrollToEnd();
            });
        }

        /// <summary>
        /// LogTextBox とケースタブを CalculationLog の現在内容で一度に再構築する。
        /// DataContext 変更時や初回 subscribe 時に使う。
        /// </summary>
        private void PopulateLogTextBox(System.Collections.Generic.IEnumerable<string>? items)
        {
            if (LogTextBox == null) return;
            LogTextBox.Clear();
            ClearCaseTabs();
            if (items == null) return;

            bool createCaseTabs = (DataContext as HorizontalCalculationViewModel)?.MaxCaseDegreeOfParallelism > 1;

            foreach (var item in items)
            {
                string line = item + Environment.NewLine;
                LogTextBox.AppendText(line);
                if (createCaseTabs)
                {
                    string? caseTag = ExtractCaseTag(item);
                    if (caseTag != null)
                    {
                        var caseTb = EnsureCaseTab(caseTag);
                        caseTb.AppendText(line);
                    }
                }
            }
            LogTextBox.ScrollToEnd();
        }

        /// <summary>VM から要求: 並列モニタを表示 (MDOP>=2 時の解析開始時)</summary>
        private void OnRequestShowParallelMonitor()
        {
            if (this.DataContext is not HorizontalCalculationViewModel vm) return;
            // 既に開いていれば何もしない (多重 Show 防止)
            if (_parallelMonitorWindow != null && _parallelMonitorWindow.IsVisible) return;

            _parallelMonitorWindow = new ParallelMonitorWindow(vm, this);
            _parallelMonitorWindow.Closed += (_, __) => _parallelMonitorWindow = null;
            _parallelMonitorWindow.Show();
        }

        /// <summary>VM から要求: 並列モニタを閉じる (解析終了/キャンセル/エラー時)</summary>
        private void OnRequestHideParallelMonitor()
        {
            if (_parallelMonitorWindow != null)
            {
                _parallelMonitorWindow.Close();
                _parallelMonitorWindow = null;
            }
        }

        private async void HorizontalCalculationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
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

                    vm.RequestShowParallelMonitor -= OnRequestShowParallelMonitor;
                    vm.RequestShowParallelMonitor += OnRequestShowParallelMonitor;
                    vm.RequestHideParallelMonitor -= OnRequestHideParallelMonitor;
                    vm.RequestHideParallelMonitor += OnRequestHideParallelMonitor;

                    // ウィンドウ表示後にバックグラウンドでFEMモデルを作成
                    await vm.InitializeModelAsync();
                }
                else
                {
                    // DataContext が後からセットされる可能性に備えて監視
                    this.DataContextChanged += HorizontalCalculationWindow_DataContextChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HorizontalCalculationWindow_Loaded] {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show($"水平解析ウィンドウの初期化でエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                vm.RequestShowParallelMonitor -= OnRequestShowParallelMonitor;
                vm.RequestHideParallelMonitor -= OnRequestHideParallelMonitor;
                vm.UnsubscribeEvents(); // LoadCase/LoadCombinationのPropertyChanged購読を解除
            }
            // 並列モニタウィンドウが開きっぱなしなら閉じる
            if (_parallelMonitorWindow != null)
            {
                _parallelMonitorWindow.Close();
                _parallelMonitorWindow = null;
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
                oldVm.RequestShowParallelMonitor -= OnRequestShowParallelMonitor;
                oldVm.RequestHideParallelMonitor -= OnRequestHideParallelMonitor;
            }
            if (e.NewValue is HorizontalCalculationViewModel newVm)
            {
                newVm.RequestClearProgressAnimation += Vm_RequestClearProgressAnimation;
                newVm.RequestShowWarning += OnRequestShowWarning;
                newVm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
                newVm.RequestClose += OnRequestClose;
                newVm.RequestShowParallelMonitor += OnRequestShowParallelMonitor;
                newVm.RequestHideParallelMonitor += OnRequestHideParallelMonitor;
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
            {
                // Run などの Visual でない要素は LogicalTree にフォールバック (例外回避)
                if (element is System.Windows.Media.Visual || element is System.Windows.Media.Media3D.Visual3D)
                {
                    element = VisualTreeHelper.GetParent(element);
                }
                else
                {
                    element = LogicalTreeHelper.GetParent(element);
                }
            }
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
