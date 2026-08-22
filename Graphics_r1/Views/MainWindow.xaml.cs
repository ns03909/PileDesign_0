using AvalonDock.Layout;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using MenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Shapes;
using System.Windows.Threading;

using Serilog;
namespace PileDesign.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Fluent.RibbonWindow, INotifyPropertyChanged
    {
        // クラス内フィールドを追加
        private readonly Dictionary<(object item, string path), object?> _dgOldValues = [];

        private object _prevLoadingType;

        public event PropertyChangedEventHandler PropertyChanged;

        // プロパティ変更通知を発行するヘルパーメソッド
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        //public MainCanvasGeometry MainCanvasGeometry { get; set; } = new();
        public List<TextBlockInfo> TextBlockInfos { get; set; } = [];

        // Undo/Redo
        private Stack<UndoAction> undoStack = [];

        private readonly double tickSpacing = 5.0;
        private readonly double actualTickPointSize = 2.0;
        private readonly double actualNodeSize = 3.0;

        private Point previousMousePosition;
        private bool IsMouseWheelPressed = false;
        private Point startPoint = new(0, 0);
        private Point endPoint = new(0, 0);
        private Rectangle selectionRectangle;

        private bool hasViewportAxes = true;
        private bool hasViewportGrid = true;

        private const double SelectionTolerance = 10.0;
        private double _lastSnappedZ = double.NaN; // 最後にスナップした杭/一般節点のZ

        public double Canvas3DHeight { get; set; }
        public double Canvas3DWidth { get; set; }
        public CanvasThreeDView CanvasThreeDViewModel { get; set; }

        private bool _startupQuickHintShown = false;
        private readonly Services.LayoutService _layoutService = new();

        // MainWindowクラスコンストラクタ
        public MainWindow()
        {
            InitializeComponent();
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;

            // ViewModelインスタンスを生成し、フィールドとDataContext両方にセット
            _mainWindowViewModel = new MainWindowViewModel();
            // DataContextをViewModelに設定
            DataContext = _mainWindowViewModel;

            var viewModel = _mainWindowViewModel;

            // 追加: ZoomFitAction をコードビハインド実装に接続
            viewModel.ZoomFitAction = ZoomFit;

            // 沈下土層ON時: 群杭荷重タブ→土層タブを表示
            viewModel.ActivateSettlementSoilTabAction = () =>
            {
                ActivateGroupPileLoadTab();
                // 土層タブ（インデックス1）を選択
                if (GroupPileTabControl != null && GroupPileTabControl.Items.Count > 1)
                    GroupPileTabControl.SelectedIndex = 1;
            };

            // （任意）アニメーション角度用も接続したい場合
            viewModel.AnimateViewAnglesAction = async (tht, phi) =>
            {
                await AnimateToAnglesAsync(tht, phi);
            };


            InitializeViewModels();
            SetupEventHandlers();
            UpdatePerspectiveView();

            var loadingMainWindow = new LoadingMainWindow();
            loadingMainWindow.ShowDialog();

            Loaded += MainWindow_Loaded;
            // Ctrl+Shift+P でコマンドパレット起動 (C.9)
            PreviewKeyDown += MainWindow_GlobalShortcutPreviewKeyDown;

            // Backstage (ファイルメニュー) のフェード差し替え: Fluent の既定 200ms より滑らかな
            //   300ms CubicEase Out をコードビハインドで適用する。
            //   AreAnimationsEnabled="False" を XAML 側に設定済みのため、ここでカスタム制御。
            if (MainBackstage != null)
            {
                MainBackstage.IsOpenChanged += MainBackstage_IsOpenChanged;
            }

            // ViewModelのActionにUpdateCanvas3Dを設定
            CanvasThreeDViewModel = viewModel.CanvasThreeDView;
            CanvasThreeDViewModel.UpdateCanvas3DAction = UpdateCanvas3D;

            // ViewModelのActionにを設定
            viewModel.UpdateWindowAction = UpdateWindow;

            // デリゲートの設定
            viewModel.UpdateCanvas3DAction = UpdateCanvas3D;
            viewModel.ShowToastAction = (msg, type) => ShowToast(msg, (ToastType)type);

            // データグリッドの選択変更イベントを設定
            DataGridPileLayout.SelectionChanged += DataGridPileLayout_SelectionChanged;
            DataGridPileAxialForce.SelectionChanged += DataGridPileAxialForce_SelectionChanged;
            DataGridIsFrontPile.SelectionChanged += DataGridIsFrontPile_SelectionChanged;


            // Window の KeyDown イベントを設定
            this.KeyDown += MainWindow_KeyDown;

            // Window の PreviewKeyDown イベントを設定（Alt+数字キー等のグローバルショートカット用）
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Canvas3DLayout の PreviewKeyDown イベントを設定
            Canvas3DLayout.PreviewKeyDown += Canvas3DLayout_PreviewKeyDown;


            // Canvas3DLayout の MouseLeftButtonDown イベントでフォーカスを設定   
            Canvas3DLayout.MouseLeftButtonDown += (s, e) => Canvas3DLayout.Focus();

            viewModel.Canvas3DLayout = Canvas3DLayout;
        }

        // 選択アイテムが変更されたときのイベントハンドラ
        private void SelectedPileLayoutItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            {
                if (isUpdatingSelection) return;

                isUpdatingSelection = true;
                try
                {
                    // DataGridの選択をクリア
                    DataGridPileLayout.SelectedItems.Clear();
                    DataGridPileAxialForce.SelectedItems.Clear();
                    DataGridIsFrontPile.SelectedItems.Clear();

                    var viewModel = _mainWindowViewModel;

                    // 新しい選択アイテムをDataGridに追加
                    foreach (var item in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        if (item.IsSelected)
                        {
                            DataGridPileLayout.SelectedItems.Add(item);
                            DataGridPileAxialForce.SelectedItems.Add(item);
                            DataGridIsFrontPile.SelectedItems.Add(item);
                        }
                    }
                }
                finally
                {
                    isUpdatingSelection = false;
                }

                // コレクション変更後の処理
                UpdateCanvas3D();
            }
        }

        // 移動中の更新
        public void UpdateWhileMouseAction()
        {
            //UpdateCanvas3D();
            isLightweightDrawing = true;
            UpdateCanvas3D();
            isLightweightDrawing = false;
        }

        // 更新
        public void UpdateWindow()
        {
            UpdateCanvas3D();
            UpdatePerspectiveView();
        }

        // トースト通知
        private System.Windows.Threading.DispatcherTimer? _toastTimer;

        public void ShowToast(string message, ToastType type = ToastType.Success)
        {
            ToastText.Text = message;

            switch (type)
            {
                case ToastType.Success:
                    ToastIcon.Text = "\u2714"; // ✔
                    ToastIcon.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    break;
                case ToastType.Info:
                    ToastIcon.Text = "\u2139"; // ℹ
                    ToastIcon.Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    break;
                case ToastType.Warning:
                    ToastIcon.Text = "\u26A0"; // ⚠
                    ToastIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                    break;
            }

            // フェードイン
            ToastBorder.Visibility = Visibility.Visible;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            ToastBorder.BeginAnimation(OpacityProperty, fadeIn);

            // スライドイン
            var slideIn = new System.Windows.Media.Animation.DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideIn);

            // 自動非表示タイマー
            _toastTimer?.Stop();
            _toastTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                fadeOut.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
                ToastBorder.BeginAnimation(OpacityProperty, fadeOut);
            };
            _toastTimer.Start();
        }

        public enum ToastType { Success, Info, Warning }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 「形状確認ビュー（凍結中）」タブをレイアウトから除去 (HelixViewport3D 機能凍結中のため非表示)
            // 名前付きコントロールはコードビハインドが参照するため、要素自体は XAML に残置している。
            try
            {
                FrozenShapeViewDocument?.Close();
            }
            catch { /* 既にクローズ済み等は無視 */ }

            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged += VmOnPropertyChanged;
            }
            UpdateCanvasRightBlankClip();
        }
        private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.RightBlankWidthPx))
                UpdateCanvasRightBlankClip();

            // IsAnalysisResultVisible が true になったとき、解析結果タブを選択
            if (e.PropertyName == nameof(MainWindowViewModel.IsAnalysisResultVisible))
            {
                if (DataContext is MainWindowViewModel vm && vm.IsAnalysisResultVisible)
                {
                    AnalysisResultRibbonTab.IsSelected = true;
                }
            }

            // INPUT フロートウィンドウの表示/非表示
            if (e.PropertyName == nameof(MainWindowViewModel.IsInputVisualizerVisible))
            {
                UpdateInputVisualizerWindow();
            }
        }

        private InputVisualizerWindow? _inputVisualizerWindow;

        private void UpdateInputVisualizerWindow()
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (vm.IsInputVisualizerVisible)
            {
                if (_inputVisualizerWindow == null)
                {
                    _inputVisualizerWindow = new InputVisualizerWindow(this);
                    _inputVisualizerWindow.Closed += (s, e) => _inputVisualizerWindow = null;
                    _inputVisualizerWindow.Show();
                }
            }
            else
            {
                _inputVisualizerWindow?.Close();
                _inputVisualizerWindow = null;
            }
        }

        // cancel-and-reclose パターン: 保存完了後に再度 Close() を呼び戻す際、Window_Closing が
        // 2 回発火する。2 回目は確認ダイアログを出さず素通りさせるためのフラグ。
        private bool _isClosingAfterSave;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingAfterSave)
            {
                // 保存完了後の再 Close: レイアウトを保存してそのまま閉じる
                _layoutService.SaveDockLayout(dockingManager);
                return;
            }

            // 確認ダイアログを表示
            var result = MessageService.Show(
                "現在のデータを保存しますか？",
                "確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // 保存して閉じる: close を一度キャンセルし、Dispatcher で延期して保存処理を実行。
                    // 注: Window_Closing 内で await SaveInputModelFile() を直接実行すると、
                    //     内部の SaveFileDialog.ShowDialog() が「Window が閉じている場合は呼べない」
                    //     例外を出すことがある (e.Cancel=true でも WPF は本ハンドラから抜けるまで
                    //     キャンセル処理を完了しないため)。Dispatcher.BeginInvoke で次のメッセージ
                    //     ループに延期することで、ハンドラ完了後の安定した状態で保存を行う。
                    if (DataContext is MainWindowViewModel viewModel)
                    {
                        e.Cancel = true;
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            await viewModel.SaveInputModelFile();
                            _isClosingAfterSave = true;
                            Close();
                        }));
                    }
                    return; // この経路ではレイアウト保存は 2 回目の Closing 発火時に行う
                case MessageBoxResult.No:
                    // 保存せずに閉じる
                    break;
                case MessageBoxResult.Cancel:
                    // 閉じるのをキャンセル
                    e.Cancel = true;
                    return; // レイアウト保存しない
            }

            // レイアウトを保存（キャンセル時以外）
            _layoutService.SaveDockLayout(dockingManager);
        }

        /// <summary>
        /// ドラッグエンター時の処理
        /// </summary>
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            // .pdj / .json ファイルのドラッグを受け入れる
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && IsPileDesignProjectFile(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// PileDesign プロジェクトファイル (.pdj 推奨 / .json 旧形式) かを判定する。
        /// </summary>
        private static bool IsPileDesignProjectFile(string path) =>
            path.EndsWith(".pdj", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// ドロップ時の処理: .pdj / .json ファイルを開く。複数ドロップ時は先頭のみ。
        /// 非対応形式 / 複数 / 空のドロップに対してステータスバーで簡易フィードバック。
        /// </summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) return;

                // .pdj / .json 以外を含むなら警告 (先頭が対応形式ならそれだけ採用)
                if (!IsPileDesignProjectFile(files[0]))
                {
                    _mainWindowViewModel.StatusMessage = "ドロップされたファイルは PileDesign プロジェクト (.pdj / .json) ではありません。";
                    return;
                }

                if (files.Length > 1)
                {
                    _mainWindowViewModel.StatusMessage = $"複数ファイルが選ばれましたが先頭のみ開きます: {System.IO.Path.GetFileName(files[0])}";
                }

                // ViewModel の OpenFromMru メソッドを使用してファイルを開く
                // (post-load protocol で AutoSave / Undo クリア等まで実行される)
                _mainWindowViewModel.OpenFromMru(files[0]);
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // 自分以外で開いている全てのウィンドウを閉じる (グラフ・テーブル・ログ等)
            // Application.Current.Windows をスナップショットしてから順次 Close (列挙中の変更を回避)
            if (Application.Current != null)
            {
                var others = Application.Current.Windows
                    .OfType<Window>()
                    .Where(w => !ReferenceEquals(w, this))
                    .ToList();
                foreach (var w in others)
                {
                    try { w.Close(); }
                    catch { /* 個別ウィンドウの Close 失敗は無視 (アプリ終了優先) */ }
                }
            }

            // ViewModel への購読を解除 (メモリリーク防止)
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged -= VmOnPropertyChanged;
            }
        }

        // =========================================================================
        // Backstage (ファイルメニュー) カスタムフェード
        //   Fluent.Ribbon の既定 200ms フェードを XAML で無効化し、300ms CubicEase Out に置換。
        //   閉じる時は Back ボタンの PreviewMouseLeftButtonDown を捕捉して
        //   フェードアウト完了後に IsOpen=false を設定する。
        // =========================================================================
        private bool _backstageClosingInProgress;
        private FrameworkElement _hookedBackButton;
        private static readonly Duration _backstageFadeDuration = new Duration(System.TimeSpan.FromMilliseconds(300));

        private static IEasingFunction CreateBackstageEase() =>
            new CubicEase { EasingMode = EasingMode.EaseOut };

        private void MainBackstage_IsOpenChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (MainBackstage == null) return;

            if (MainBackstage.IsOpen)
            {
                _backstageClosingInProgress = false;
                // 視覚ツリー上で Adorner が現れるのを待ってからフェードイン
                Dispatcher.BeginInvoke(new System.Action(ApplyBackstageFadeIn), DispatcherPriority.Render);
            }
        }

        private void ApplyBackstageFadeIn()
        {
            var adorner = FindBackstageAdorner();
            if (adorner == null) return;

            adorner.BeginAnimation(UIElement.OpacityProperty, null);
            var anim = new DoubleAnimation(0.0, 1.0, _backstageFadeDuration)
            {
                EasingFunction = CreateBackstageEase()
            };
            adorner.BeginAnimation(UIElement.OpacityProperty, anim);

            // Back ボタンをフックしてフェードアウトを差し込む (Uid は Fluent.Ribbon 内部の定数)
            var backButton = FindByUid(adorner, "BackstageBackButtonUid") as FrameworkElement;
            if (backButton != null && backButton != _hookedBackButton)
            {
                if (_hookedBackButton != null)
                    _hookedBackButton.PreviewMouseLeftButtonDown -= BackstageBackButton_PreviewMouseLeftButtonDown;
                _hookedBackButton = backButton;
                backButton.PreviewMouseLeftButtonDown += BackstageBackButton_PreviewMouseLeftButtonDown;
            }
        }

        private void BackstageBackButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_backstageClosingInProgress) return;
            if (MainBackstage == null || !MainBackstage.IsOpen) return;

            var adorner = FindBackstageAdorner();
            if (adorner == null) return;

            _backstageClosingInProgress = true;
            e.Handled = true; // 既定の閉じる動作を抑止

            var anim = new DoubleAnimation(1.0, 0.0, _backstageFadeDuration)
            {
                EasingFunction = CreateBackstageEase()
            };
            anim.Completed += (_, _) =>
            {
                if (MainBackstage != null) MainBackstage.IsOpen = false;
                _backstageClosingInProgress = false;
            };
            adorner.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>Window の Visual ツリーをたどり、Fluent.BackstageAdorner を探す。</summary>
        private UIElement FindBackstageAdorner()
        {
            return FindVisualDescendant(this, e => e?.GetType().Name == "BackstageAdorner") as UIElement;
        }

        private static DependencyObject FindVisualDescendant(DependencyObject root, System.Func<DependencyObject, bool> predicate)
        {
            if (root == null) return null;
            if (predicate(root)) return root;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindVisualDescendant(child, predicate);
                if (found != null) return found;
            }
            return null;
        }

        private static DependencyObject FindByUid(DependencyObject root, string uid)
        {
            if (root == null) return null;
            if (root is FrameworkElement fe && fe.Uid == uid) return root;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindByUid(VisualTreeHelper.GetChild(root, i), uid);
                if (found != null) return found;
            }
            return null;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCanvasTransformGroup();
            SetupDataBindings();
            InitializePanelToggleSync();
            var viewModel = _mainWindowViewModel;

            // 起動時にアイソメトリックビューを強制設定（Slider初期化で上書きされる場合の対策）
            viewModel.CanvasThreeDView.Tht = -45;
            viewModel.CanvasThreeDView.Phi = 45;

            // 左ペインの「杭」タブを選択状態にする
            PileLayoutDocument.IsSelected = true;

            // Canvas にフォーカスを設定
            Canvas3DLayout.Focus();

            // HelixViewport3Dの内蔵コンテキストメニューを上書き
            // （内蔵のCopy to ClipboardがClipboard.SetImageを使いBitmapMetadata例外を起こすため）
            SetupHelixViewportContextMenu();

            // SizeChanged イベントを登録
            Canvas3DLayout.SizeChanged += ColorBarCanvas_SizeChanged;

            // 親Gridサイズ変更に追随
            if (Canvas3DLayout.Parent is FrameworkElement parent)
                parent.SizeChanged += (_, __) => UpdateCanvasRightBlankClip();

            UpdateCanvasRightBlankClip(); // 初期適用

            // コマンドライン引数で指定されたファイルを起動時にロード
            //   PileDesign.exe project.json
            //   PileDesign.exe --open project.json
            // App.StartupFilePath は OnStartup で解析済み。
            if (!string.IsNullOrEmpty(App.StartupFilePath) && System.IO.File.Exists(App.StartupFilePath))
            {
                // OpenFromMru は完全な ProjectData ロード + AutoSave + Undo クリア等の post-load 処理を行う
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { viewModel.OpenFromMruCommand.Execute(App.StartupFilePath); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Startup file load failed: {Path}", App.StartupFilePath); }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // --- 起動時の QuickHintPopup 表示を無効化 ---
            if (false && !_startupQuickHintShown)
            {
                _startupQuickHintShown = true;
                _ = Task.Run(async () =>
                {
                    // レイアウト確定のための短い遅延
                    await Task.Delay(300);

                    // 表示（UI スレッド）
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (DataContext is MainWindowViewModel vm)
                            {
                                if (!vm.IsQuickHintVisible)
                                    vm.IsQuickHintVisible = true;
                            }
                            else if (this.FindName("QuickHintPopup1") is System.Windows.Controls.Primitives.Popup popup)
                            {
                                popup.IsOpen = true;
                            }
                        }
                        catch { /* 無害に握りつぶす */ }
                    });

                    // 表示時間（ミリ秒） — 好きな値（例: 5000 = 5秒）に変更可
                    await Task.Delay(5000);

                    // 自動で閉じる（UI スレッド）
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (DataContext is MainWindowViewModel vm)
                            {
                                if (vm.IsQuickHintVisible)
                                    vm.IsQuickHintVisible = false;
                            }
                            else if (this.FindName("QuickHintPopup1") is System.Windows.Controls.Primitives.Popup popup)
                            {
                                popup.IsOpen = false;
                            }
                        }
                        catch (Exception ex) { Log.Warning(ex, "QuickHint close"); }
                    });
                });
            }
            // --- 追加ここまで ---

            // 自動保存ファイルの復元チェック
            // ダブルクリック等で起動ファイル指定がある場合はスキップ
            // (ユーザーは明示的に X を開こうとしているのに、別ファイル Y の autosave を提案するのを防ぐ)
            if (string.IsNullOrEmpty(App.StartupFilePath))
            {
                viewModel.CheckAutoSaveRestore();
            }

            // レイアウトを復元
            //_layoutService.RestoreDockLayout(dockingManager);
        }

        private void ColorBarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // キャンバスのサイズが変更されたときに DrawColorBar を呼び出す
            //ColorBar.DrawStepColorBar(ColorBarCanvas);
        }

        private void InitializeViewModels()
        {
            //var viewModel = _mainWindowViewModel;
            //DataContext3D = new ThreeDViewModel(DataContext);
        }

        private void InitializeCanvasTransformGroup()
        {
            Canvas3DHeight = Canvas3DLayout.ActualHeight;
            Canvas3DWidth = Canvas3DLayout.ActualWidth;
        }

        private void SetupDataBindings()
        {
            //DataGridPileLayout.ItemsSource = DataContext.PileLayoutViewModel.PileLayoutCollection;
            //DataGridEmbedment.ItemsSource = DataContext.EmbedmentViewModel.EmbedmentCollection;
        }

        private void SetupEventHandlers()
        {
            DataGridPileLayout.Loaded += DataGridPileLayout_Loaded;
            Canvas3DLayout.SizeChanged += Canvas3DLayout_SizeChanged;
        }



        private void ToggleButtonXYGrid_Checked(object sender, RoutedEventArgs e)
        {
            hasViewportGrid = true;
            UpdatePerspectiveView();
        }

        private void ToggleButtonXYGrid_UnClicked(object sender, RoutedEventArgs e)
        {
            hasViewportGrid = false;
            UpdatePerspectiveView();
        }

        // 軸ボタンを有効にした場合のメソッド
        private void ToggleButtonXYZAxes_Checked(object sender, RoutedEventArgs e)
        {
            hasViewportAxes = true;
            UpdatePerspectiveView();
        }

        // 軸ボタンを無効にした場合のメソッド
        private void ToggleButtonXYZAxes_UnClicked(object sender, RoutedEventArgs e)
        {
            hasViewportAxes = false;
            UpdatePerspectiveView();
        }



        private void ComboBoxLabelSize_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ComboBoxLabelSize_OnSelectionChangedCommand.Execute(e);
        }

        private void DataGridPileLayout_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "AxialForceEX" || e.PropertyName == "AxialForceEY" ||
                e.PropertyName == "AxialForceLevel1s[0]" || e.PropertyName == "AxialForceLevel1s[1]" ||
                e.PropertyName == "AxialForceLevel1s[2]" || e.PropertyName == "AxialForceLevel1s[3]")
            {
                var dataGrid = sender as DataGrid;

                if (dataGrid.DataContext is MainWindowViewModel viewModel)
                {
                    var binding = new Binding("PileLayoutViewModel.IsElastic")
                    {
                        Source = viewModel,
                        Mode = BindingMode.OneWay
                    };

                    if (e.Column is DataGridTextColumn dataGridColumn)
                    {
                        //var bindingProxy = new BindingProxy { Data = viewModel.PileLayoutViewModel.IsElastic };
                        //BindingOperations.SetBinding(bindingProxy, BindingProxy.DataProperty, binding);

                        //dataGridColumn.Visibility = (Visibility)bindingProxy.Data;
                    }
                }
            }
        }

        private void DataGridGridX_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridGridX_OnPreviewKeyDownCommand.Execute(e);

            //if (e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) || e.Key == Key.Right || e.Key == Key.Left)
            //{
            //    var viewModel = _mainWindowViewModel;
            //    var collection = viewModel.PileLayoutViewModel.GridX;

            //    RecalculateGrid(collection);

            //    isDataGridGridYCellEditEnding = false;
            //}
        }

        private void DataGridGridY_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridGridY_OnPreviewKeyDownCommand.Execute(e);

            //if (e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) || e.Key == Key.Right || e.Key == Key.Left)
            //{
            //    var viewModel = _mainWindowViewModel;
            //    var collection = viewModel.PileLayoutViewModel.GridY;

            //    RecalculateGrid(collection);

            //    isDataGridGridYCellEditEnding = false;
            //}
        }




        //// Mouse Event ////


        // マウスがプレスされたときの処理
        private void Canvas3DLayout_MouseDown(object sender, MouseButtonEventArgs e)
        {
            {
                if (e.MiddleButton == MouseButtonState.Pressed)
                {
                    IsMouseWheelPressed = true;
                    previousMousePosition = e.GetPosition(Canvas3DLayout);
                }
            }
        }

        // 置換: Canvas外へ出た/キャプチャロスト時の後始末（ドラッグフラグもクリア）
        private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        {
            IsMouseWheelPressed = false;
            IsRightButtonClicked = false;

            _isRotatingView = false;
            _rightDragged = false;
            UnhookRendering();
            isLightweightDrawing = false;

            // 沈下マップツールチップを非表示
            HideSettlementTooltip();

            if (e.LeftButton == MouseButtonState.Released)
            {
                Canvas3DLayout.ReleaseMouseCapture();
            }

            if (selectionRectangle != null)
            {
                endPoint = e.GetPosition(Canvas3DLayout);
                ConfirmSelection3D();
                Canvas3DLayout.Children.Remove(selectionRectangle);
                selectionRectangle = null;
            }
        }

        private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        {
            IsMouseWheelPressed = false;
            IsRightButtonClicked = false;

            _isRotatingView = false;
            _rightDragged = false;
            UnhookRendering();
            isLightweightDrawing = false;
        }

        //// 置換: Canvas外へ出た/キャプチャロスト時の後始末
        //private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    _isRotatingView = false;
        //    UnhookRendering();
        //    isLightweightDrawing = false;

        //    if (e.LeftButton == MouseButtonState.Released)
        //    {
        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }

        //    if (selectionRectangle != null)
        //    {
        //        endPoint = e.GetPosition(Canvas3DLayout);
        //        ConfirmSelection3D();
        //        Canvas3DLayout.Children.Remove(selectionRectangle);
        //        selectionRectangle = null;
        //    }
        //}


        //private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    _isRotatingView = false;
        //    UnhookRendering();
        //    isLightweightDrawing = false;
        //}

        //// マウスがCanvasの範囲外に出た時の処理
        //private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    if (e.LeftButton == MouseButtonState.Released)
        //    {
        //        // マウスキャプチャを解除
        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }

        //    // マウスがCanvasの範囲外に出た時の処理
        //    if (selectionRectangle != null)
        //    {
        //        // 選択範囲を確定する
        //        endPoint = e.GetPosition(Canvas3DLayout);
        //        ConfirmSelection3D();

        //        // SelectionRectangleを消す
        //        Canvas3DLayout.Children.Remove(selectionRectangle);
        //        selectionRectangle = null;
        //    }
        //}

        private int? FindNearestNodeIndex(Point pos)
        {
            var viewModel = _mainWindowViewModel;
            double minDist = 15.0; // ピクセル閾値
            int? nearestIndex = null;
            for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
            {
                var node = viewModel.CurrentInputModel.PileLayoutItems[i];
                var screenPt = viewModel.CanvasThreeDView.Transformation(node.Point3D);
                double dist = (screenPt - pos).Length;
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestIndex = i;
                }
            }
            return nearestIndex;
        }
        // マウス左ボタンが押された時のメソッド
        private void Canvas3DLayout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ダブルクリック: 選択中の杭/梁のプロパティ編集
            if (e.ClickCount == 2)
            {
                var viewModel = _mainWindowViewModel;
                var selectedPile = viewModel.CurrentInputModel.PileLayoutItems.FirstOrDefault(p => p.IsSelected);
                var selectedBeam = viewModel.CurrentInputModel.FoundationBeamInput?.Beams.FirstOrDefault(b => b.IsSelected);
                if (selectedPile != null)
                {
                    viewModel.EditAddPilesCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (selectedBeam != null)
                {
                    viewModel.EditBeamElementsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // 基礎梁ビジュアル編集モードの処理
            if (HandleFoundationBeamEditMode(e))
            {
                return; // 編集モードで処理された場合は早期リターン
            }

            startPoint = e.GetPosition(Canvas3DLayout);

            IsRightButtonClicked = false;
            IsMouseWheelPressed = false;
            // マウスキャプチャを設定
            Canvas3DLayout.CaptureMouse();

            // Canvas にキーボードフォーカスを設定
            Canvas3DLayout.Focus();

            // Shiftキーが押されている場合の処理
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // クリック位置の周辺に節点があるかチェック
                SelectNode3DIfNearby(startPoint, true);
            }
            // Shiftキーが押されていない場合の処理
            else
            {
                ClearCanvasSelection();

                // クリック位置の周辺に節点があるかチェック
                SelectNode3DIfNearby(startPoint, false);

                var elementToRemove = Canvas3DLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == "Selection");
                if (elementToRemove != null)
                {
                    Canvas3DLayout.Children.Remove(elementToRemove);
                }
            }

            // 左ボタンプレスの場合
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 選択窓
                startPoint = e.GetPosition(Canvas3DLayout);
                selectionRectangle = new Rectangle
                {
                    //Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Opacity = 0.3,
                    Fill = Brushes.LightBlue,
                    Stroke = Brushes.Black

                };

                Canvas.SetLeft(selectionRectangle, startPoint.X);
                Canvas.SetTop(selectionRectangle, startPoint.Y);

                // ★常に最前面にする
                Panel.SetZIndex(selectionRectangle, 10000);

                Canvas3DLayout.Children.Add(selectionRectangle);
            }
        }

        private const double DragThreshold = 5.0; // ドラッグとみなす移動距離の閾値


        // マウス左ボタンが離された時のメソッド
        private void Canvas3DLayout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスキャプチャを解除
            Canvas3DLayout.ReleaseMouseCapture();

            // 編集モード中は選択処理を抑制
            if (DataContext is MainWindowViewModel vm && vm.CurrentEditMode != CanvasEditMode.None)
            {
                Canvas3DLayout.Children.Remove(selectionRectangle);
                selectionRectangle = null;
                return;
            }

            // マウスの左ボタンが離された時の処理
            endPoint = e.GetPosition(Canvas3DLayout);

            // 移動距離を計算
            double distance = (endPoint - startPoint).Length;

            if (distance > DragThreshold)
            {
                // ドラッグとみなす処理
                ConfirmSelection3D();
            }
            else
            {
                // クリックとみなす処理
                //HandleClick();
            }

            // SelectionRectangleを消す
            Canvas3DLayout.Children.Remove(selectionRectangle);
            selectionRectangle = null;
        }


        // 追加フィールド（クラス内に追加）
        private Point _rightDragAnchorPoint;
        private double _anchorTht;
        private double _anchorPhi;
        private bool _isRotatingView = false;
        private bool _isRenderingHooked = false;
        private Point _latestMousePos;

        // ★ 追加: レンダリングキャッシュ用フィールド
        private double _lastRenderedTht = double.NaN;
        private double _lastRenderedPhi = double.NaN;

        // 回転感度（px -> degree）
        //private const double RotateDegPerPixelX = 0.50; // 横移動: θ
        //private const double RotateDegPerPixelY = 0.50; // 縦移動: φ
        private const double RotateDegPerPixelX = 0.35; // より細かい制御
        private const double RotateDegPerPixelY = 0.35;

        // 追加ヘルパー（クラス内に追加）
        private void HookRendering()
        {
            if (_isRenderingHooked) return;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _isRenderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_isRenderingHooked) return;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isRenderingHooked = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_isRotatingView) return;

            var viewModel = (MainWindowViewModel)DataContext;
            var delta = _latestMousePos - _rightDragAnchorPoint;

            double newTht = _anchorTht - delta.X * RotateDegPerPixelX;
            double newPhi = _anchorPhi + delta.Y * RotateDegPerPixelY;

            // φの範囲制限
            newPhi = Math.Clamp(newPhi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);

            // 角度が変わっていない場合はスキップ（無駄な再描画を防止）
            if (Math.Abs(newTht - _lastRenderedTht) < 0.01 &&
                Math.Abs(newPhi - _lastRenderedPhi) < 0.01)
            {
                return;
            }

            _lastRenderedTht = newTht;
            _lastRenderedPhi = newPhi;

            // 軽量描画モードで更新
            isLightweightDrawing = true;
            try
            {
                viewModel.CanvasThreeDView.Tht = newTht;
                viewModel.CanvasThreeDView.Phi = newPhi;
                UpdateCanvas3D();
            }
            finally
            {
                isLightweightDrawing = false;
            }
        }
        //// φの過回転を軽く制限（必要に応じて調整）
        //newPhi = Math.Max(-89.9, Math.Min(89.9, newPhi));

        //    // 右ドラッグ中は軽量描画
        //    isLightweightDrawing = true;
        //    try
        //    {
        //        // セッター側で再描画が走らない場合も明示更新
        //        viewModel.CanvasThreeDView.Tht = newTht;
        //        viewModel.CanvasThreeDView.Phi = newPhi;
        //        UpdateCanvas3D();
        //    }
        //    finally
        //    {
        //        isLightweightDrawing = false;
        //    }
        //}

        private const double RotationThreshold = 0.5; //1ピクセルごとに回転
        private const double RotationAngle = 5.0; // 1度回転

        private DateTime lastUpdate = DateTime.Now;
        private readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(50); // 更新間隔10ミリ秒

        private bool _rightDragged = false;
        private Point _rightDownPoint;
        //private const double RightClickDragThreshold = 6.0; // px: 右クリックとドラッグの判定閾値
        private const double RightClickDragThreshold = 3.0; // より反応を良くする

        // マウス移動
        private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

            // ステータスバーにマウス座標を表示（スナップZ平面上の逆変換）
            var screenPos = e.GetPosition(Canvas3DLayout);
            if (!double.IsNaN(_lastSnappedZ))
            {
                var worldPos = viewModel.CanvasThreeDView.InverseTransformationAtZ(screenPos, _lastSnappedZ);
                viewModel.MouseCoordinateText = $"X={worldPos.X:F3}  Y={worldPos.Y:F3}  Z={worldPos.Z:F3}";
            }
            else
            {
                var worldPos = viewModel.CanvasThreeDView.InverseTransformation(screenPos);
                viewModel.MouseCoordinateText = $"X={worldPos.X:F3}  Y={worldPos.Y:F3}  Z={worldPos.Z:F3}";
            }

            // 基礎梁追加モード: プレビュー線を描画
            if (viewModel.CurrentEditMode == CanvasEditMode.AddElement && viewModel.TempStartNode != null)
            {
                DrawFoundationBeamPreview(e.GetPosition(Canvas3DLayout));
            }

            // Shift+右でのパン中は中ボタンパンと同様の処理
            if (_isPanningWithRight && e.RightButton == MouseButtonState.Pressed)
            {
                Point currentMousePosition = e.GetPosition(Canvas3DLayout);
                Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

                viewModel.CanvasThreeDView.ViewTransition = new Point(
                    viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
                    viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
                );

                previousMousePosition = currentMousePosition;

                UpdateWhileMouseAction();
                return;
            }

            // 既存の右ボタンドラッグ（回転）処理はそのまま（以下省略せず既存処理を維持）
            // 既存実装のまま続行...
            if (e.RightButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(Canvas3DLayout);

                // ドラッグ判定（しきい値超えでドラッグ開始）
                if (!_isRotatingView)
                {
                    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                    {
                        _rightDragged = true;
                        _isRotatingView = true;

                        // ★ 重要: 閾値を超えた位置を新しいアンカーにする
                        _rightDragAnchorPoint = pos;
                        _anchorTht = viewModel.CanvasThreeDView.Tht;
                        _anchorPhi = viewModel.CanvasThreeDView.Phi;

                        // レンダリングフック開始
                        HookRendering();
                        isLightweightDrawing = true;
                    }
                }
                //if (!_isRotatingView)
                //{
                //    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                //    {
                //        _rightDragged = true;
                //        _isRotatingView = true;

                //        // 回転開始時にフック・軽量描画ON
                //        HookRendering();
                //        isLightweightDrawing = true;
                //    }
                //}
                //{
                //    var viewModel = (MainWindowViewModel)DataContext;

                //    // 右ドラッグ中はイベントを合流させる（ここでは位置だけ記録）
                //    if (e.RightButton == MouseButtonState.Pressed)
                //    {
                //        var pos = e.GetPosition(Canvas3DLayout);

                // ドラッグ判定（しきい値超えでドラッグ開始）
                if (!_isRotatingView)
                {
                    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                    {
                        _rightDragged = true;
                        _isRotatingView = true;

                        // 回転開始時にフック・軽量描画ON
                        HookRendering();
                        isLightweightDrawing = true;
                    }
                }

                if (_isRotatingView)
                {
                    _latestMousePos = pos; // Renderingで1フレームに1回更新
                    return;               // 重い再描画はここでしない
                }
            }

            // 以降は既存の処理（左ドラッグ・中ドラッグ・スロットリング等）
            if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
            lastUpdate = DateTime.Now;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // ...（既存の矩形選択更新処理）...
                Point currentPoint = e.GetPosition(Canvas3DLayout);
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);

                if (selectionRectangle != null)
                {
                    selectionRectangle.Width = width;
                    selectionRectangle.Height = height;

                    Canvas.SetLeft(selectionRectangle, x);
                    Canvas.SetTop(selectionRectangle, y);

                    if (currentPoint.X >= startPoint.X)
                    {
                        selectionRectangle.Fill = Brushes.LightBlue;
                        selectionRectangle.StrokeDashArray = null;
                        viewModel.IsCrossSelectionMode = false;
                    }
                    else
                    {
                        selectionRectangle.Fill = Brushes.LightGreen;
                        selectionRectangle.StrokeDashArray = [4, 2];
                        viewModel.IsCrossSelectionMode = true;
                    }
                }
            }

            if (IsMouseWheelPressed)
            {
                Point currentMousePosition = e.GetPosition(Canvas3DLayout);
                Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

                viewModel.CanvasThreeDView.ViewTransition = new Point(
                    viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
                    viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
                );

                previousMousePosition = currentMousePosition;

                UpdateWhileMouseAction();
            }

            // ツールチップとホバーハイライトの更新（ボタンが押されていない時のみ）
            if (e.LeftButton == MouseButtonState.Released &&
                e.RightButton == MouseButtonState.Released &&
                !IsMouseWheelPressed)
            {
                Point mousePos = e.GetPosition(Canvas3DLayout);
                // ホバーハイライト + スナップZ更新
                var snappedZ = UpdateHoverHighlight(mousePos);
                if (snappedZ.HasValue)
                    _lastSnappedZ = snappedZ.Value;
                // 沈下マップツールチップ
                UpdateSettlementTooltip(mousePos);
                // 応力図・変位図ツールチップ
                try
                {
                    UpdateBeamResultTooltip(mousePos);
                }
                catch
                {
                    // 例外時は無視（描画に影響しないように）
                }
            }
            else
            {
                ClearHoverHighlight();
                HideSettlementTooltip();
                try { HideBeamResultTooltip(); } catch (Exception ex) { Log.Warning(ex, "HideBeamResultTooltip"); }
            }
        }

        //// 置換: マウス移動
        //private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        //{
        //    var viewModel = (MainWindowViewModel)DataContext;

        //    // 右ドラッグ中はイベントを合流させる（ここでは位置だけ記録）
        //    if (e.RightButton == MouseButtonState.Pressed && _isRotatingView)
        //    {
        //        _latestMousePos = e.GetPosition(Canvas3DLayout);
        //        // 右ドラッグ時はここで重い再描画をしない
        //        return;
        //    }

        //    // 右ドラッグ以外は既存のスロットリングを適用
        //    if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
        //    lastUpdate = DateTime.Now;

        //    if (viewModel.IsElementAddMode)
        //    {
        //        UpdateEditingElement3D(e);
        //    }

        //    // 左ボタン: 矩形選択の更新（既存処理を維持）
        //    if (e.LeftButton == MouseButtonState.Pressed)
        //    {
        //        Point currentPoint = e.GetPosition(Canvas3DLayout);

        //        double x = Math.Min(startPoint.X, currentPoint.X);
        //        double y = Math.Min(startPoint.Y, currentPoint.Y);
        //        double width = Math.Abs(currentPoint.X - startPoint.X);
        //        double height = Math.Abs(currentPoint.Y - startPoint.Y);

        //        if (selectionRectangle != null)
        //        {
        //            selectionRectangle.Width = width;
        //            selectionRectangle.Height = height;

        //            Canvas.SetLeft(selectionRectangle, x);
        //            Canvas.SetTop(selectionRectangle, y);

        //            if (currentPoint.X >= startPoint.X)
        //            {
        //                selectionRectangle.Fill = Brushes.LightBlue;
        //                selectionRectangle.StrokeDashArray = null; // 実線
        //                viewModel.IsCrossSelectionMode = false;
        //            }
        //            else
        //            {
        //                selectionRectangle.Fill = Brushes.LightGreen;
        //                selectionRectangle.StrokeDashArray = new DoubleCollection { 4, 2 }; // 破線
        //                viewModel.IsCrossSelectionMode = true;
        //            }
        //        }
        //    }

        //    // 中ボタン: 平行移動（既存処理を維持）
        //    if (IsMouseWheelPressed)
        //    {
        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        viewModel.CanvasThreeDView.ViewTransition = new Point(
        //            viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
        //            viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
        //        );

        //        previousMousePosition = currentMousePosition;

        //        UpdateWhileMouseAction();
        //    }

        //    // 旧: 右ドラッグでの逐次回転処理は削除（Renderingでまとめて描画）
        //}

        //// マウスが移動した時のメソッド
        //private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        //{
        //    // 一定の間隔でのみUIを更新
        //    if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
        //    lastUpdate = DateTime.Now;

        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    IsRightButtonClicked = false;

        //    if (viewModel.IsElementAddMode)
        //    {
        //        UpdateEditingElement3D(e); // 編集中要素の更新
        //    }

        //    // 左ボタンが押されている場合の処理
        //    if (e.LeftButton == MouseButtonState.Pressed)
        //    {
        //        Point currentPoint = e.GetPosition(Canvas3DLayout);

        //        double x = Math.Min(startPoint.X, currentPoint.X);
        //        double y = Math.Min(startPoint.Y, currentPoint.Y);
        //        double width = Math.Abs(currentPoint.X - startPoint.X);
        //        double height = Math.Abs(currentPoint.Y - startPoint.Y);

        //        if (selectionRectangle != null)
        //        {
        //            selectionRectangle.Width = width;
        //            selectionRectangle.Height = height;

        //            Canvas.SetLeft(selectionRectangle, x);
        //            Canvas.SetTop(selectionRectangle, y);

        //            // 選択窓の色を動的に変更
        //            if (currentPoint.X >= startPoint.X)
        //            {
        //                selectionRectangle.Fill = Brushes.LightBlue;
        //                selectionRectangle.StrokeDashArray = null; // 実線
        //                viewModel.IsCrossSelectionMode = false;
        //            }
        //            else
        //            {
        //                selectionRectangle.Fill = Brushes.LightGreen;
        //                selectionRectangle.StrokeDashArray = [4, 2]; // 破線
        //                viewModel.IsCrossSelectionMode = true;
        //            }
        //        }
        //    }

        //    // ホイールが押されている場合の処理
        //    if (IsMouseWheelPressed)
        //    {

        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        // 水平方向の移動成分をViewTransition.Xに変換
        //        viewModel.CanvasThreeDView.ViewTransition = new Point(
        //            viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
        //            viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
        //        );

        //        previousMousePosition = currentMousePosition;

        //        UpdateWhileMouseAction();
        //        // await Task.Run(() => UpdateWhileMouseAction()); // 非同期に実行
        //    }

        //    // 右ボタンが押されている場合の処理
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        bool rotated = false;

        //        // ここで軽量描画フラグを先に立てる
        //        isLightweightDrawing = true;
        //        try
        //        {
        //            // 左右方向の移動成分を左右回転θに変換
        //            if (Math.Abs(delta.X) >= RotationThreshold) // 回転速度の調整係数
        //        {
        //            viewModel.CanvasThreeDView.Tht -= Math.Sign(delta.X) * RotationAngle;
        //            previousMousePosition.X = currentMousePosition.X; // 更新
        //        }

        //        // 上下方向の移動成分を上下回転φに変換
        //        if (Math.Abs(delta.Y) >= RotationThreshold) // 回転速度の調整係数
        //        {
        //            viewModel.CanvasThreeDView.Phi += Math.Sign(delta.Y) * RotationAngle;
        //            previousMousePosition.Y = currentMousePosition.Y; // 更新
        //        }
        //            // setter 内の自動再描画が走るため必須ではないが、
        //            // スロットリングのタイミングで1回明示的に描画しておくと安定
        //            if (rotated)
        //            {
        //                UpdateCanvas3D();
        //            }
        //        }
        //        finally
        //        {
        //            isLightweightDrawing = false; // 元に戻す
        //        }
        //    }
        //}
        //UpdateWhileMouseAction();
        //        //await Task.Run(() => UpdateWhileMouseAction()); // 非同期に実行
        //    }
        //}

        bool IsRightButtonClicked { get; set; } = false;

        //// マウス右ボタンが押された時のメソッド
        //private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;
        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //    }
        //}

        // 追加フィールド（既存の追加フィールド群の近くに挿入）
        private bool _isPanningWithRight = false;

        // 置換: マウス右ボタン押下
        private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // Shift + 右ボタン => 中ボタンと同様にパン（平行移動）
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    _isPanningWithRight = true;
                    IsMouseWheelPressed = true;
                    IsRightButtonClicked = false; // コンテキストメニュー抑止
                    previousMousePosition = e.GetPosition(Canvas3DLayout);

                    // マウスキャプチャして移動中の描画を許可
                    Canvas3DLayout.CaptureMouse();
                    return;
                }

                // 既存の回転開始処理（Shift無しの右ボタン）
                IsRightButtonClicked = true;

                var viewModel = (MainWindowViewModel)DataContext;

                previousMousePosition = e.GetPosition(Canvas3DLayout);
                _rightDownPoint = previousMousePosition;
                _rightDragAnchorPoint = previousMousePosition;

                _anchorTht = viewModel.CanvasThreeDView.Tht;
                _anchorPhi = viewModel.CanvasThreeDView.Phi;

                _latestMousePos = _rightDragAnchorPoint;
                _isRotatingView = false;        // ここではまだ回転開始しない（移動量が閾値超えたら開始）
                _rightDragged = false;

                Canvas3DLayout.CaptureMouse();
            }
        }
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;

        //        var viewModel = (MainWindowViewModel)DataContext;

        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //        _rightDownPoint = previousMousePosition;
        //        _rightDragAnchorPoint = previousMousePosition;

        //        _anchorTht = viewModel.CanvasThreeDView.Tht;
        //        _anchorPhi = viewModel.CanvasThreeDView.Phi;

        //        _latestMousePos = _rightDragAnchorPoint;
        //        _isRotatingView = false;        // ここではまだ回転開始しない（移動量が閾値超えたら開始）
        //        _rightDragged = false;

        //        Canvas3DLayout.CaptureMouse();
        //    }
        //}

        //// 置換: マウス右ボタン押下
        //private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;

        //        var viewModel = (MainWindowViewModel)DataContext;

        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //        _rightDragAnchorPoint = previousMousePosition;

        //        _anchorTht = viewModel.CanvasThreeDView.Tht;
        //        _anchorPhi = viewModel.CanvasThreeDView.Phi;

        //        _latestMousePos = _rightDragAnchorPoint;
        //        _isRotatingView = true;

        //        // 右ドラッグ中は1フレームに1回の更新
        //        HookRendering();

        //        // 軽量描画を有効化（重いラベル等を抑制）
        //        isLightweightDrawing = true;

        //        // マウスキャプチャ
        //        Canvas3DLayout.CaptureMouse();
        //    }
        //}

        //// 置換: マウス右ボタン解放
        //private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        IsRightButtonClicked = false;

        //        _isRotatingView = false;
        //        UnhookRendering();

        //        // 軽量描画を解除して最終状態をフル描画
        //        isLightweightDrawing = false;
        //        UpdateCanvas3D();

        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }
        //}
        // 置換: マウス右ボタン解放（回転の後始末＋ドラッグフラグのクリア）
        private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                if (_isPanningWithRight)
                {
                    // Shift+右でのパンを終了
                    _isPanningWithRight = false;
                    IsMouseWheelPressed = false;

                    // 最終更新
                    UpdateCanvas3D();

                    // 後始末
                    Canvas3DLayout.ReleaseMouseCapture();
                    IsRightButtonClicked = false;
                    _rightDragged = false;
                    return;
                }

                // 既存の回転終了処理
                _isRotatingView = false;
                UnhookRendering();

                isLightweightDrawing = false;
                UpdateCanvas3D();

                IsRightButtonClicked = false;
                _rightDragged = false;

                Canvas3DLayout.ReleaseMouseCapture();

                // レンダリングキャッシュをリセット
                _lastRenderedTht = double.NaN;
                _lastRenderedPhi = double.NaN;
            }
        }
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        _isRotatingView = false;
        //        UnhookRendering();

        //        isLightweightDrawing = false;
        //        UpdateCanvas3D();

        //        IsRightButtonClicked = false;
        //        _rightDragged = false;

        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }
        //}

        // マウス右ボタンが離された時のメソッド
        //private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        IsRightButtonClicked = false;
        //    }
        //}

        // ズーム後のフル描画デバウンスタイマー
        private System.Windows.Threading.DispatcherTimer? _zoomFullRenderTimer;

        // マウスホイールイベント: 軽量描画 + デバウンスでフル描画
        private void Canvas3DLayout_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                IsMouseWheelPressed = true;
            }

            IsRightButtonClicked = false;

            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            Point mousePosition = e.GetPosition(Canvas3DLayout);

            double scale = viewModel.CanvasThreeDView.Scale;
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Math.Max(0.1, Math.Min(scale * zoomFactor, 100));

            Point originalFocalPoint = viewModel.CanvasThreeDView.Transformation(viewModel.CanvasThreeDView.Ct);
            Point newFocalPoint = new(
                (originalFocalPoint.X - mousePosition.X) * zoomFactor + mousePosition.X,
                (originalFocalPoint.Y - mousePosition.Y) * zoomFactor + mousePosition.Y);

            Point originalViewPosition = viewModel.CanvasThreeDView.ViewTransition;
            Point newViewPosition = new(
                originalViewPosition.X + (newFocalPoint.X - originalFocalPoint.X),
                originalViewPosition.Y + (newFocalPoint.Y - originalFocalPoint.Y));

            viewModel.CanvasThreeDView.Scale = newScale;
            viewModel.CanvasThreeDView.ViewTransition = newViewPosition;

            IsMouseWheelPressed = false;

            // 軽量描画（ラベル・地盤・解析結果を省略）
            UpdateWhileMouseAction();
            viewModel.RaisePropertyChanged(nameof(viewModel.ZoomText));

            // 200ms後にフル描画を1回実行（連続ホイール操作ではリセット）
            if (_zoomFullRenderTimer == null)
            {
                _zoomFullRenderTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _zoomFullRenderTimer.Tick += (s, _) =>
                {
                    _zoomFullRenderTimer.Stop();
                    UpdateCanvas3D();
                };
            }
            _zoomFullRenderTimer.Stop();
            _zoomFullRenderTimer.Start();
        }

        // 置換: プレビューMouseUpでのメニュー表示判定
        private void Canvas3DLayout_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Released)
            {
                IsMouseWheelPressed = false;
            }

            // 右クリックメニューは「ドラッグしていない場合のみ」表示
            if (IsRightButtonClicked && !_rightDragged)
            {
                // 選択状態を判定
                bool hasSelectedNodes = false;
                bool hasSelectedBeams = false;

                if (DataContext is MainWindowViewModel vm)
                {
                    hasSelectedNodes = vm.CurrentInputModel?.PileLayoutItems?
                        .Any(p => p.IsSelected) ?? false;
                    if (!hasSelectedNodes)
                        hasSelectedNodes = vm.CurrentInputModel?.InputNodes?
                            .Any(n => n.IsSelected) ?? false;

                    hasSelectedBeams = vm.CurrentInputModel?.FoundationBeamInput?.Beams?
                        .Any(b => b.IsSelected) ?? false;
                }

                ContextMenu contextMenu;

                if (hasSelectedNodes && hasSelectedBeams)
                {
                    // 両方選択されている場合: 統合メニューを動的に構築
                    contextMenu = new ContextMenu();

                    // 杭節点メニュー項目
                    if (FindResource("NodeContextMenu") is ContextMenu nodeMenu)
                    {
                        foreach (var item in nodeMenu.Items)
                        {
                            if (item is MenuItem mi)
                            {
                                contextMenu.Items.Add(CloneMenuItemForContextMerge(mi));
                            }
                            else if (item is Separator)
                            {
                                contextMenu.Items.Add(new Separator());
                            }
                        }
                    }

                    contextMenu.Items.Add(new Separator());

                    // 梁要素メニュー項目（画像コピー/保存は杭側に含まれるので省略）
                    if (FindResource("BeamElementContextMenu") is ContextMenu beamMenu)
                    {
                        foreach (var item in beamMenu.Items)
                        {
                            if (item is MenuItem mi)
                            {
                                // 画像コピー/保存は重複するのでスキップ
                                var header = mi.Header?.ToString() ?? "";
                                if (header.Contains("画像")) continue;

                                contextMenu.Items.Add(CloneMenuItemForContextMerge(mi));
                            }
                        }
                    }
                }
                else if (hasSelectedBeams)
                {
                    contextMenu = FindResource("BeamElementContextMenu") as ContextMenu;
                }
                else
                {
                    contextMenu = FindResource("NodeContextMenu") as ContextMenu;
                }

                if (contextMenu != null)
                {
                    contextMenu.PlacementTarget = sender as UIElement;
                    contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    contextMenu.IsOpen = true;
                }

                startPoint = e.GetPosition(Canvas3DLayout);
            }
            else
            {
                // 右クリック以外、または右ドラッグの場合は非表示
                Canvas3DLayout.ContextMenu = null;
                UpdateCanvas3D();
            }

            // 後始末
            IsRightButtonClicked = false;
            IsMouseWheelPressed = false;
            _rightDragged = false;
        }

        //// マウスホイールドラッグ完了時のメソッド マウスホイールが離された時の処理
        //private void Canvas3DLayout_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        //{
        /// <summary>
        /// ContextMenu を動的にマージ構築する際に MenuItem を「正しく」複製する。
        ///
        /// 注意: 元の MenuItem の Command プロパティは XAML で {Binding ...} 経由で指定されている場合、
        /// 元 ContextMenu が一度も Open されないと binding が評価されず mi.Command は null になる。
        /// したがって `new MenuItem { Command = mi.Command }` だと null コマンドの MenuItem が生成され、
        /// クリックしても無反応になる。
        ///
        /// 本ヘルパでは BindingOperations.GetBinding で元の Binding を取得して新 MenuItem に再設定し、
        /// PlacementTarget の DataContext から binding が評価されるようにする。
        /// </summary>
        private static MenuItem CloneMenuItemForContextMerge(MenuItem source)
        {
            var newItem = new MenuItem { Header = source.Header };

            // Command (Binding を保持)
            var cmdBinding = System.Windows.Data.BindingOperations.GetBinding(source, MenuItem.CommandProperty);
            if (cmdBinding != null) newItem.SetBinding(MenuItem.CommandProperty, cmdBinding);
            else if (source.Command != null) newItem.Command = source.Command;

            // CommandParameter (Binding を保持)
            var paramBinding = System.Windows.Data.BindingOperations.GetBinding(source, MenuItem.CommandParameterProperty);
            if (paramBinding != null) newItem.SetBinding(MenuItem.CommandParameterProperty, paramBinding);
            else if (source.CommandParameter != null) newItem.CommandParameter = source.CommandParameter;

            return newItem;
        }

        //    if (e.MiddleButton == MouseButtonState.Released)
        //    {
        //        IsMouseWheelPressed = false;
        //    }

        //    if (IsRightButtonClicked == true)
        //    {
        //        // マウス位置で ContextMenu を表示
        //        if (FindResource("NodeContextMenu") is ContextMenu contextMenu)
        //        {
        //            contextMenu.PlacementTarget = sender as UIElement;
        //            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        //            contextMenu.IsOpen = true;
        //        }
        //        else
        //        {
        //            // デバッグログ
        //            Console.WriteLine("ContextMenu is null");
        //        }
        //        startPoint = e.GetPosition(Canvas3DLayout);
        //        IsRightButtonClicked = false;
        //        IsMouseWheelPressed = false;
        //    }
        //    else
        //    {
        //        // 右クリック以外の場合は ContextMenu を非表示にする
        //        Canvas3DLayout.ContextMenu = null;
        //        UpdateCanvas3D();
        //    }
        //}

        //private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;
        //}

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyDown(e);
        }

        private void Canvas3DLayout_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyDown(e);
        }

        // -------------------------------------------------------
        // プロパティパネル イベントハンドラー
        // -------------------------------------------------------

        /// <summary>TextBox でフォーカスを得たら全選択する。</summary>
        private void PropertyPanel_TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        /// <summary>プロパティパネル内で Enter キーを押したら TextBox の変更を確定し、フォーカスを外す。</summary>
        private void PropertyPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox tb)
            {
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        // キーを押したときの処理
        /// <summary>
        /// 現在のキーボードフォーカスがDataGrid内にあるかを判定します。
        /// </summary>
        private static bool IsFocusInDataGrid()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is DataGrid) return true;
                // Run などの Visual でない要素は LogicalTree にフォールバック (例外回避)
                if (focused is System.Windows.Media.Visual || focused is System.Windows.Media.Media3D.Visual3D)
                {
                    focused = VisualTreeHelper.GetParent(focused);
                }
                else
                {
                    focused = LogicalTreeHelper.GetParent(focused);
                }
            }
            return false;
        }

        private void HandleKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + A が押されたときの処理: すべてアクティブ
                ShowAllNodes();
                e.Handled = true;
            }
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + T が押されたときの処理
                ButtonXYPlane_Clicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + R が押されたときの処理
                ButtonYZPlane_Clicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + R が押されたときの処理
                ButtonXZPlane_Clicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + I が押されたときの処理
                ButtonIsometric_Clicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.None)
            {
                // F12: INPUT 表示の切替
                if (_mainWindowViewModel != null)
                    _mainWindowViewModel.IsInputVisualizerVisible = !_mainWindowViewModel.IsInputVisualizerVisible;
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // DataGrid内にフォーカスがある場合は標準のCtrl+A（セル全選択）を優先
                if (IsFocusInDataGrid()) return;

                // Ctrl + A が押されたときの処理: すべて選択（節点）
                SelectAllNodes();
                e.Handled = true;
            }
            else if (
                ((e.Key == Key.D1 || e.Key == Key.NumPad1) && Keyboard.Modifiers == ModifierKeys.Alt)
                || (e.Key == Key.System && e.SystemKey == Key.D1 && Keyboard.Modifiers == ModifierKeys.Alt)
            )
            {
                // Alt + 1が押されたときの処理
                // 要素追加モードに切り替え（トグルではない）
                MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
                viewModel.CurrentEditMode = CanvasEditMode.AddElement;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mainWindow = (MainWindow)Application.Current.MainWindow;
                    mainWindow.GeneralBeamElementDocument.IsSelected = true;
                });
                e.Handled = true;
            }

            else if (
                ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Alt)
                || (e.Key == Key.System && e.SystemKey == Key.D0 && Keyboard.Modifiers == ModifierKeys.Alt)
            )
            {
                // Alt + 0が押されたときの処理
                // 選択モード（None）に切り替え
                MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
                viewModel.CurrentEditMode = CanvasEditMode.None;
                e.Handled = true;
            }

            else if (
                ((e.Key == Key.D7 || e.Key == Key.NumPad7) && Keyboard.Modifiers == ModifierKeys.Alt)
                || (e.Key == Key.System && e.SystemKey == Key.D7 && Keyboard.Modifiers == ModifierKeys.Alt)
                )
            {
                // Alt + 7が押されたときの処理
                // 要素の節点分割
                MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
                viewModel.OnSplitElementsByNodes();
                e.Handled = true;
            }

            else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift + F2が押されたときの処理
                ShowUnselectedNodes();
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                // F2 が押されたときの処理
                ShowSelectedNodes();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                // Deleteが押されたときの処理（Undo対応）
                DeleteSelectedItems();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Escapeが押されたときの処理
                ClearCanvasSelection();
                var viewModel = DataContext as MainWindowViewModel;

                // 要素追加モードで1点目が選択されている場合、それもキャンセル
                if (viewModel.TempStartNode != null)
                {
                    viewModel.TempStartNode = null;
                    viewModel.StatusMessage = string.Empty;
                    ClearFoundationBeamPreview(); // プレビュー線をクリア
                }

                viewModel.CurrentEditMode = CanvasEditMode.None;
                //e.Handled = true;
            }

            else if (
                ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control)
                || (e.Key == Key.System && e.SystemKey == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
                )
            {
                // Ctrl + 0 が押されたときの処理
                ZoomFit();
                e.Handled = true;
            }

        }

        // ズームフィット
        private void ZoomFit()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            InputModel inputModel = viewModel.CurrentInputModel;

            if (inputModel.PileLayoutItems.Count == 0) return;

            // 節点の座標を取得
            var points = inputModel.PileLayoutItems.Select(p => p.Point3D).ToList();

            double canvasWidth = Canvas3DLayout.ActualWidth;
            double canvasHeight = Canvas3DLayout.ActualHeight;

            // 中心点を計算
            Point center2D = viewModel.CanvasThreeDView.Transformation(viewModel.CanvasThreeDView.Ct);

            // ビューの移動量を計算
            double offsetX = canvasWidth / 2 - center2D.X;
            double offsetY = canvasHeight / 2 - center2D.Y;

            // 水平方向の移動成分をViewTransition.Xに変換
            viewModel.CanvasThreeDView.ViewTransition = new Point(
                viewModel.CanvasThreeDView.ViewTransition.X + offsetX,
                viewModel.CanvasThreeDView.ViewTransition.Y + offsetY
            );

            if (viewModel.CurrentInputModel == null || viewModel.CurrentInputModel.PileLayoutItems.Count <= 1)
            {
                UpdateCanvas3D();
                return;
            }

            double xMax = double.MinValue, yMax = double.MinValue;
            double xMin = double.MaxValue, yMin = double.MaxValue;

            foreach (PileLayoutDataItem pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                Point point = viewModel.CanvasThreeDView.Transformation(pileLayoutItem.Point3D);
                if (point.X > xMax) xMax = point.X;
                if (point.Y > yMax) yMax = point.Y;
                if (point.X < xMin) xMin = point.X;
                if (point.Y < yMin) yMin = point.Y;
            }

            // スケールを計算
            double scale = Math.Min(canvasWidth / (xMax - xMin), canvasHeight / (yMax - yMin)) * 0.7;

            viewModel.CanvasThreeDView.Scale *= scale;

            // Canvasを更新
            UpdateCanvas3D();
            viewModel.RaisePropertyChanged(nameof(viewModel.ZoomText));
        }

        // すべてのノードを選択するメソッド
        private void SelectAllNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;
            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.IsVisible = true;
                pileLayoutItem.IsSelected = true;
            }
            // 一般節点
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General)
                    {
                        node.IsVisible = true;
                        node.IsSelected = true;
                    }
                }
            }
            // 梁要素
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                {
                    beam.IsVisible = true;
                    beam.IsSelected = true;
                }
            }
            // 基礎梁節点
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    node.IsVisible = true;
                    node.IsSelected = true;
                }
            }
            UpdateWindow();
        }

        // すべてのノードを表示するメソッド
        private void ShowAllNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;
            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.IsVisible = true;
            }
            // 一般節点
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General)
                        node.IsVisible = true;
                }
            }
            // 梁要素
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsVisible = true;
            }
            // 基礎梁節点
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsVisible = true;
            }
            UpdateWindow();
        }

        // 選択されたノードを表示するメソッド
        private void ShowSelectedNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;

            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.IsVisible = pileLayoutItem.IsSelected;
            }
            // 一般節点
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General)
                        node.IsVisible = node.IsSelected;
                }
            }
            // 梁要素
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsVisible = beam.IsSelected;
            }
            // 基礎梁節点
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsVisible = node.IsSelected;
            }
            UpdateWindow();
        }

        // 選択されていないノードを表示するメソッド
        private void ShowUnselectedNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;
            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.IsVisible = !pileLayoutItem.IsSelected;
            }
            // 一般節点
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General)
                        node.IsVisible = !node.IsSelected;
                }
            }
            // 梁要素
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in viewModel.CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsVisible = !beam.IsSelected;
            }
            // 基礎梁節点
            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsVisible = !node.IsSelected;
            }
            UpdateWindow();
        }


        // 選択された杭配置データを削除するメソッド (デッドコード — UI から呼び出されていない)
        // 接続梁のカスケード削除を持たないため、有効化する場合は DeletePiles (MainWindowViewModel.cs)
        // のように beam cascade ロジックを追加してから使うこと。
        //private void DeleteSelectedPileLayouts()
        //{
        //    var vm = _mainWindowViewModel;
        //    var col = vm.CurrentInputModel.PileLayoutItems;
        //
        //    var itemsToRemove = col.Where(x => x.IsSelected).ToList();
        //    if (itemsToRemove.Count == 0) return;
        //
        //    // まとめて1ステップに
        //    var scope = new PileDesign.Common.Undo.CompositeUndoAction("Delete piles");
        //    foreach (var item in itemsToRemove)
        //    {
        //        int index = col.IndexOf(item);
        //        if (index < 0) continue;
        //        scope.Add(
        //            PileDesign.Common.Undo.CollectionChangeAction<PileLayoutDataItem>
        //                .ForRemove(col, item, index)
        //        );
        //    }
        //    UndoService.Instance.Push(scope);
        //
        //    // 実削除
        //    foreach (var item in itemsToRemove)
        //        col.Remove(item);
        //
        //    vm.UpdatePileLayoutNo();
        //}

        /// <summary>
        /// 選択された杭・節点・要素をまとめて削除（Undo対応）
        /// 節点を削除する場合、接続された要素も自動削除
        /// </summary>
        private void DeleteSelectedItems()
        {
            var vm = _mainWindowViewModel;
            var input = vm.CurrentInputModel;

            // 削除対象の収集
            var pilesToRemove = input.PileLayoutItems.Where(x => x.IsSelected).ToList();
            var inputNodesToRemove = input.InputNodes?.Where(x => x.IsSelected && x.Type == NodeType.General).ToList() ?? [];
            var beamsToRemove = input.FoundationBeamInput?.Beams?.Where(x => x.IsSelected).ToList() ?? [];

            // 削除対象の一般節点のUniqueIdを収集（接続要素の検索用）
            var deletedNodeIds = new HashSet<Guid>(inputNodesToRemove.Select(n => n.UniqueId));
            // 削除対象の杭のUniqueIdも収集
            var deletedPileIds = new HashSet<Guid>(pilesToRemove.Select(p => p.UniqueId));

            // 削除される節点/杭に接続された基礎梁を追加
            if (input.FoundationBeamInput?.Beams != null && (deletedNodeIds.Count > 0 || deletedPileIds.Count > 0))
            {
                foreach (var beam in input.FoundationBeamInput.Beams)
                {
                    if (beamsToRemove.Contains(beam)) continue;

                    bool connected = false;
                    // 一般節点参照のチェック
                    if (beam.NodeI_Type == NodeReferenceType.GeneralNode && deletedNodeIds.Contains(beam.NodeI_Id))
                        connected = true;
                    if (beam.NodeJ_Type == NodeReferenceType.GeneralNode && deletedNodeIds.Contains(beam.NodeJ_Id))
                        connected = true;
                    // 杭参照のチェック
                    if (beam.NodeI_Type == NodeReferenceType.PileLayout && deletedPileIds.Contains(beam.NodeI_Id))
                        connected = true;
                    if (beam.NodeJ_Type == NodeReferenceType.PileLayout && deletedPileIds.Contains(beam.NodeJ_Id))
                        connected = true;

                    if (connected) beamsToRemove.Add(beam);
                }
            }

            // 削除するものがなければ何もしない
            if (pilesToRemove.Count == 0 && inputNodesToRemove.Count == 0 &&
                beamsToRemove.Count == 0)
                return;

            // Undoスナップショットを保存（削除前の状態）
            vm.SaveUndoState();

            // 実削除
            foreach (var item in pilesToRemove)
                input.PileLayoutItems.Remove(item);

            if (input.InputNodes != null)
            {
                foreach (var node in inputNodesToRemove)
                    input.InputNodes.Remove(node);
            }

            if (input.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in beamsToRemove)
                    input.FoundationBeamInput.Beams.Remove(beam);
            }

            // 杭番号を更新
            if (pilesToRemove.Count > 0)
                vm.UpdatePileLayoutNo();

            // LoadingRow ベースの行番号は Remove 時に再評価されないため、
            // 関連 DataGrid を強制リフレッシュして即時再描画する。
            if (beamsToRemove.Count > 0)
                DataGridFoundationBeams?.Items.Refresh();

            UpdateWindow();
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
                textBox.Focus();
                textBox.SelectAll();
                e.Handled = true;
            }
        }

        private void TextBoxAltitude_TextChanged(object sender, TextChangedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.TextBoxAltitude_OnTextChangedCommand.Execute(e);
        }

        private void ComboBoxEmbedmentNums_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ComboBoxEmbedmentNums_OnPreviewMouseDownCommand.Execute(e);
        }

        private void ComboBoxEmbedmentGroundNo_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ComboBoxEmbedmentGroundNo_OnPreviewMouseDownCommand.Execute(e);
        }

        private void TextBoxBottomAltitude_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.TextBoxBottomAltitude_OnPreviewMouseDownCommand.Execute(e);
        }

        private void DataGridEmbedment_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridEmbedment_OnBeginningEditCommand.Execute(e);
        }

        private void ButtonGround_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ButtonGround_OnPreviewMouseDownCommand.Execute(e);
        }

        private void ButtonPileBody_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ButtonPileBody_OnPreviewMouseDownCommand.Execute(e);
        }

        private void ButtonButtonSettlement_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.ButtonSettlement_OnPreviewMouseDownCommand.Execute(e);
        }

        private void CheckBoxCommonActionPoint3D_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            if ((bool)CheckBoxCommonActionPoint3D.IsChecked)
            {
                viewModel.IsActionPointVisible = true;
            }
            else
            {
                viewModel.IsActionPointVisible = false;
            }
        }

        private void DataGridEmbedment_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void UpdateEmbedmentAltitudes()
        {
            //var InputModel = MainWindowViewModel.InputModel;
            var viewModel = DataContext as MainWindowViewModel;
            InputModel InputModel = viewModel.CurrentInputModel;

            for (int i = InputModel.EmbedmentInput.EmbedmentLayers.Count - 1; i >= 0; i--)
            {
                if (i == InputModel.EmbedmentInput.EmbedmentLayers.Count - 1)
                {
                    InputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = InputModel.EmbedmentInput.BottomAltitude;
                }
                else
                {
                    InputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = InputModel.EmbedmentInput.EmbedmentLayers[i + 1].TopAltitude;
                }
                InputModel.EmbedmentInput.EmbedmentLayers[i].TopAltitude
                = InputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude + InputModel.EmbedmentInput.EmbedmentLayers[i].LayerThickness;
            }
        }

        // 群杭荷重タイプ変化時のメソッド
        private void ComboBoxLoadingType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _prevLoadingType ??= ComboBoxLoadingType.SelectedItem;

            // ComboBoxの選択変更後の内容を取得
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox?.SelectedItem as string;

            var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;

            // 個別十字系・個別矩形系に切り替わった場合は RectLoads を自動生成で置換
            if (vm != null && (selectedItem == "個別十字" || selectedItem == "個別十字（基礎梁反力）"
                            || selectedItem == "個別矩形" || selectedItem == "個別矩形（基礎梁考慮）"))
            {
                // 既存の RectLoads (任意矩形等で入力済) があれば、上書き確認ダイアログを表示
                var existingRectLoads = vm.CurrentInputModel?.PileGroupSettlement?.RectLoads;
                int existingCount = existingRectLoads?.Count ?? 0;
                if (existingCount > 0)
                {
                    var prevName = _prevLoadingType as string ?? "(以前の荷重タイプ)";
                    string shapeDesc = (selectedItem == "個別矩形" || selectedItem == "個別矩形（基礎梁考慮）")
                        ? "杭頭ごとの正方形荷重"
                        : "杭頭ごとの十字形矩形荷重";
                    var msg = $"現在「{prevName}」で {existingCount} 件の矩形荷重が登録されています。\n\n" +
                              $"「{selectedItem}」へ切替えると、これらは破棄され、{shapeDesc}で上書きされます。\n\n" +
                              "切替えを続行しますか? (キャンセルで元の荷重タイプに戻ります)";
                    var result = PileDesign.Services.MessageService.Show(
                        msg, "荷重タイプ切替確認",
                        System.Windows.MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Warning);
                    if (result != System.Windows.MessageBoxResult.OK)
                    {
                        // キャンセル: 前回値に戻す (SelectionChanged 再発火は短絡される)
                        if (_prevLoadingType != null)
                            comboBox.SelectedItem = _prevLoadingType;
                        return;
                    }
                }

                // UpdateSourceTrigger=LostFocus のためモデル側 LoadingType が
                // まだ古い値の可能性 → 先にソース更新してから再生成
                comboBox?.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
                vm.RebuildAutoCrossRectLoadsIfNeeded();
            }

            // 群杭表示
            if (vm != null) vm.IsSettlementGroundVisible = true;

            // 変更を確定し、前回値を更新
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;

            UpdateWindow();
        }

        // 群杭沈下解析結果のアクティブケース切替: 選択ケースの SettlementGridData / RectLoads /
        // 各杭沈下を legacy フィールドへ反映してキャンバスを再描画する。
        private void ComboBoxGroupSettlementActiveCase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not PileDesign.ViewModels.MainWindowViewModel vm) return;
            var pgs = vm.CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null || pgs.CaseRecords.Count == 0) return;
            int idx = pgs.ActiveCaseIndex;
            if (idx < 0 || idx >= pgs.CaseRecords.Count) return;

            PileDesign.ViewModels.GroupSettlementWithBeamCalculationViewModel
                .ApplyActiveCaseToLegacyFields(pgs, pgs.CaseRecords[idx]);

            // バッジ・キャンバス更新
            vm.RaisePropertyChanged(nameof(PileDesign.ViewModels.MainWindowViewModel.IsGroupSettlementActiveCaseBeamAware));
            UpdateWindow();
        }

        private void ComboBoxEmbedmentNums_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (HelixViewport == null)
        //    {
        //        // HelixViewportが初期化されていない場合の処理をスキップする
        //        return;
        //    }

        //    if (ComboBoxEmbedmentNums.SelectedItem is int selectedValue)
        //    {
        //        var viewModel = DataContext as MainWindowViewModel;
        //        InputModel InputModel = viewModel.CurrentInputModel;
        //        int currentCollectionSize = InputModel.EmbedmentInput.EmbedmentLayers.Count;

        //        // Remove excess items if selectedValue is less than the current collection size
        //        for (int i = currentCollectionSize - 1; i >= selectedValue; i--)
        //        {
        //            InputModel.EmbedmentInput.EmbedmentLayers.RemoveAt(i);
        //        }

        //        // Add new rows only if selectedValue is greater than the current collection size
        //        for (int i = currentCollectionSize; i < selectedValue; i++)
        //        {
        //            EmbedmentDataItem newItem = CreateNewEmbedmentDataItem(i, currentCollectionSize);
        //            InputModel.EmbedmentInput.EmbedmentLayers.Add(newItem);
        //        }

        //        viewModel.UpdateEmbedment();
        //        UpdateWindow();
        //    }
        //}
        {
            if (HelixViewport == null)
            {
                // HelixViewportが初期化されていない場合の処理をスキップする
                return;
            }

            if (ComboBoxEmbedmentNums.SelectedItem is int selectedValue)
            {
                var viewModel = DataContext as MainWindowViewModel;
                InputModel InputModel = viewModel.CurrentInputModel;
                int currentCollectionSize = InputModel.EmbedmentInput.EmbedmentLayers.Count;

                // Remove excess items if selectedValue is less than the current collection size
                for (int i = currentCollectionSize - 1; i >= selectedValue; i--)
                {
                    InputModel.EmbedmentInput.EmbedmentLayers.RemoveAt(i);
                }

                // Add new rows only if selectedValue is greater than the current collection size
                for (int i = currentCollectionSize; i < selectedValue; i++)
                {
                    // EmbedmentInputのファクトリメソッドを利用
                    EmbedmentDataItem newItem = InputModel.EmbedmentInput.CreateNewEmbedmentDataItem(i);
                    InputModel.EmbedmentInput.EmbedmentLayers.Add(newItem);
                }

                viewModel.UpdateEmbedment();

                viewModel.IsEmbedmentBoxVisible = true;
                UpdateWindow();
            }
        }

        //private EmbedmentDataItem CreateNewEmbedmentDataItem(int index, int currentCollectionSize)
        //{
        //    var viewModel = DataContext as MainWindowViewModel;
        //    InputModel InputModel = viewModel.CurrentInputModel;
        //    EmbedmentDataItem newItem;
        //    if (currentCollectionSize > 0 && index > 0)
        //    {
        //        EmbedmentDataItem lastItem = InputModel.EmbedmentInput.EmbedmentLayers[index - 1];
        //        newItem = new EmbedmentDataItem
        //        {
        //            No = index + 1,
        //            LayerThickness = lastItem.LayerThickness,
        //            //TopAltitude = lastItem.TopAltitude,
        //            //BottomAltitude = lastItem.BottomAltitude,
        //            X1 = lastItem.X1,
        //            X2 = lastItem.X2,
        //            Y1 = lastItem.Y1,
        //            Y2 = lastItem.Y2,
        //        };
        //    }
        //    else
        //    {
        //        newItem = new EmbedmentDataItem
        //        {
        //            No = index + 1,
        //            LayerThickness = 5.0,
        //            //TopAltitude = 5.0,
        //            //BottomAltitude = 0.0,
        //            X1 = 0.0,
        //            X2 = 50.0,
        //            Y1 = 0.0,
        //            Y2 = 50.0,
        //        };
        //    }

        //    return newItem;
        //}

        // キャンバス3Dサイズ変化時のイベントハンドラ 
        private void Canvas3DLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Canvas3DHeight = Canvas3DLayout.ActualHeight;
            Canvas3DWidth = Canvas3DLayout.ActualWidth;
            UpdateCanvas3D();

            // 右余白クリップ更新
            UpdateCanvasRightBlankClip();
        }

        private void DataGridPileLayout_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridPileLayout_OnLoadingRowCommand.Execute(e); // ビューモデルのコマンドを実行

            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        // 行番号を設定するメソッド
        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        //杭レイアウトコレクションが変化した場合のメソッド
        private void PileLayoutCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            { }

            if (e.Action != NotifyCollectionChangedAction.Add)
            {
                DataGridPileLayout.Items.Refresh();
                //DataGridElements.Items.Refresh();
            }
        }

        // 杭レイアウトデータグリッドがロードされた場合のメソッド
        private void DataGridPileLayout_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridPileLayout.ItemsSource is ObservableCollection<PileLayoutDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            }
        }

        // データグリッドのセルが編集された場合のメソッド
        private void ButtonPileLayoutDelete_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            var input = viewModel.CurrentInputModel;

            // ボタンのTagから対象アイテムを取得
            if (sender is Button button && button.Tag is PileLayoutDataItem selectedItem)
            {
                var col = input.PileLayoutItems;
                int index = col.IndexOf(selectedItem);
                if (index < 0) return;

                // Undo: Remove の逆操作を保持
                UndoService.Instance.Push(
                    PileDesign.Common.Undo.CollectionChangeAction<PileLayoutDataItem>
                        .ForRemove(col, selectedItem, index, "Delete pile")
                );

                col.RemoveAt(index);
                viewModel.UpdatePileLayoutNo();
                UpdateWindow();
            }
            else
            {
                MessageService.Show("削除対象のアイテムが正しく取得できませんでした。");
            }
        }

        private void ButtonInputNodeDelete_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            var input = viewModel.CurrentInputModel;

            // ボタンのTagから対象アイテムを取得
            if (sender is Button button && button.Tag is InputNode selectedItem)
            {
                var col = input.InputNodes;
                if (col == null) return;

                int index = col.IndexOf(selectedItem);
                if (index < 0) return;

                // Undo: Remove の逆操作を保持
                UndoService.Instance.Push(
                    PileDesign.Common.Undo.CollectionChangeAction<InputNode>
                        .ForRemove(col, selectedItem, index, "Delete input node")
                );

                col.RemoveAt(index);
                UpdateWindow();
            }
            else
            {
                MessageService.Show("削除対象のアイテムが正しく取得できませんでした。");
            }
        }
        //{
        //    var viewModel = DataContext as MainWindowViewModel;
        //    InputModel InputModel = viewModel.CurrentInputModel;

        //    if (DataGridPileLayout.SelectedItem != null)
        //    {
        //        // 選択されたアイテムが正しい型であることを確認する
        //        if (DataGridPileLayout.SelectedItem is PileLayoutDataItem selectedItem)
        //        {
        //            InputModel.PileLayoutItems.Remove(selectedItem);
        //            //NumberingNewPileNumber(false);
        //            viewModel.UpdatePileLayoutNo();
        //            UpdateWindow();
        //        }
        //        else
        //        {
        //            // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
        //            MessageService.Show("選択されたアイテムの型が正しくありません。");
        //        }
        //    }
        //}


        private void DataGridPileLayout_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;
        }

        // データグリッドのセルが編集された場合のメソッド
        //private void NumberingNewPileNumber(bool isCopy)
        //{
        //    var viewModel = DataContext as MainWindowViewModel;
        //    InputModel InputModel = viewModel.CurrentInputModel;
        //    var collectionView = CollectionViewSource.GetDefaultView(DataGridPileLayout.ItemsSource) as IEditableCollectionView;

        //    // コレクションビューがトランザクション中でないかをチェック
        //    if (!collectionView.IsAddingNew && !collectionView.IsEditingItem)
        //    {
        //        ObservableCollection<PileLayoutDataItem> _collection = InputModel.PileLayoutItems;
        //        bool isSolved = false;
        //        if (_collection.Count == 0)
        //        {
        //            return;
        //        }
        //        else if (_collection.Count == 1)
        //        {
        //            _collection[0].PileNo = 1;
        //        }
        //        else
        //        {
        //            if (isCopy == false)
        //            {
        //                _collection[^1].X = _collection[^2].X + 10;
        //                _collection[^1].Y = _collection[^2].Y;
        //            }

        //            for (int i = 0; i < _collection.Count; i++) // 番号0から
        //            {
        //                for (int j = 0; j < _collection.Count; j++)
        //                {
        //                    if (_collection[j].PileNo == i + 1) { break; }
        //                    if (j == _collection.Count - 1)
        //                    {
        //                        //_collection[_collection.Count - 1].PileNumber = _collection.Count;
        //                        _collection[^1].PileNo = i + 1;
        //                        isSolved = true;
        //                        break;
        //                    }
        //                }
        //                if (isSolved == true) { break; }
        //            }
        //        }
        //        DataGridPileLayout.Items.Refresh();
        //    }
        //    UpdateWindow();
        //}

        // RadioButton 弾性/非弾性の選択メソッド
        private void RadioButtonIsElastic_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                // RadioButtonが選択されているかどうかをチェックし、それに応じてIsElasticプロパティを設定する
                if (radioButton.IsChecked == true) { }
                else if (radioButton.IsChecked == false) { }
            }
        }

        // Y方向通り心変更時更新メソッド
        private void DataGridGridY_CurrentCellChanged(object sender, EventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridGridY_CurrentCellChanged();
        }

        // X方向通り心変更時更新メソッド
        private void DataGridGridX_CurrentCellChanged(object sender, EventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridGridX_CurrentCellChanged();
        }

        private bool isUpdatingSelection = false;

        // 選択された杭配置データを更新するメソッド
        private void UpdateSelectedPileLayoutItems(DataGrid dataGrid)
        {
            if (isUpdatingSelection)
                return;

            if (this.DataContext is MainWindowViewModel viewModel)
            {
                // イベントを一時的に無効にする
                viewModel.CurrentInputModel.PileLayoutItems.CollectionChanged -= SelectedPileLayoutItems_CollectionChanged;

                isUpdatingSelection = true;
                try
                {
                    // すべてのアイテムの選択状態をリセット
                    foreach (PileLayoutDataItem pileLayoutDataItem in viewModel.CurrentInputModel.PileLayoutItems)
                    {
                        pileLayoutDataItem.IsSelected = false;
                    }

                    // DataGridの選択アイテムを更新
                    foreach (var item in dataGrid.SelectedItems)
                    {
                        foreach (PileLayoutDataItem pileLayoutDataItem in viewModel.CurrentInputModel.PileLayoutItems)
                        {
                            if (item == pileLayoutDataItem)
                            {
                                pileLayoutDataItem.IsSelected = true;
                            }
                        }
                    }
                }
                finally
                {
                    isUpdatingSelection = false;

                    // イベントを再度有効にする
                    viewModel.CurrentInputModel.PileLayoutItems.CollectionChanged += SelectedPileLayoutItems_CollectionChanged;
                    UpdateCanvas3D();
                    viewModel.UpdatePropertyPanel();
                }
            }
        }

        private void DataGridIsFrontPile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSelectionChanging) return;
            UpdateSelectedPileLayoutItems(DataGridIsFrontPile);
        }

        private void DataGridPileAxialForce_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSelectionChanging) return;
            UpdateSelectedPileLayoutItems(DataGridPileAxialForce);
        }

        private void DataGridPileLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSelectionChanging) return;
            UpdateSelectedPileLayoutItems(DataGridPileLayout);

            if (this.DataContext is MainWindowViewModel viewModel)
                if (viewModel.IsElementSplit)
                { return; }
                else
                {
                    viewModel.CurrentInputModel.GenerateSoilPiles();////////////////////////////////////////////////////////////////////////////////////
                }
        }

        // InputNode用イベントハンドラ
        private void DataGridInputNodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSelectionChanging) return;
            UpdateSelectedInputNodes(DataGridInputNodes);
        }

        private void DataGridInputNodes_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private void DataGridInputNodes_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && !vm.CheckAndResetElementSplit("一般節点"))
            {
                e.Cancel = true;
                return;
            }

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;
        }

        private void DataGridInputNodes_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Commitのみ処理
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            // Commit後の新値をリフレッシュして取得
            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                // Undo登録
                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));
            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridInputNodes_OnCellEditEndingCommand.Execute(e);
        }

        private void UpdateSelectedInputNodes(DataGrid dataGrid)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel?.InputNodes == null) return;

            isSelectionChanging = true;

            try
            {
                // すべての選択を解除
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    node.IsSelected = false;
                }

                // DataGridで選択された項目を選択状態に
                foreach (var selectedItem in dataGrid.SelectedItems)
                {
                    if (selectedItem is InputNode node)
                    {
                        node.IsSelected = true;
                    }
                }

                UpdateCanvas3D();
            }
            finally
            {
                isSelectionChanging = false;
                viewModel.UpdatePropertyPanel();
            }
        }

        private void CheckBoxAnalysisResult_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateWindow();
        }

        private void ShowAllNodesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAllNodes();
        }

        private void ShowSelectedNodesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSelectedNodes();
        }

        private void ShowUnselectedNodesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowUnselectedNodes();
        }

        private void DeselectAllNodesButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCanvasSelection();
        }

        // ショートカット
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;


            // ファイルを開く
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.NewInputModelFile();
                e.Handled = true;
            }

            // ファイルを開く
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenInputModelFile();
                e.Handled = true;
            }

            // ファイル保存
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.SaveInputModelFile();
                e.Handled = true;
            }

            // 名前をつけてファイル保存
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                viewModel?.SaveInputModelFileAs();
                e.Handled = true;
            }

            // 荷重条件編集
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenLoadCaseWindow();
                e.Handled = true;
            }

            // 地盤編集
            else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenGroundWindow();
                e.Handled = true;
            }

            // 軸力確認
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OnAxialForceCheck();
                e.Handled = true;
            }

            // 自動梁要素生成
            else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                viewModel?.AutoGenerateFoundationBeamsCommand.Execute(null);
                e.Handled = true;
            }
            // 基本設定
            else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenFundamentalWindowCommand.Execute(null);
                e.Handled = true;
            }
            // 杭体編集
            else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenPileBodyWindow();
                e.Handled = true;
            }
            // 杭要素分割 (Ctrl+D / F4)・水平解析 (F5)・単杭沈下 (F6) は Window.InputBindings で処理する。
            // ここで VM のメソッドを直接呼ぶと、コマンドの CanExecute (杭要素分割済みか) を
            // 迂回してしまい、「ボタンは灰色なのにキーでは実行できて、直後にダイアログで叱られる」
            // 状態になる。

            // 基礎梁考慮沈下解析
            else if (e.Key == Key.F7)
            {
                viewModel?.OpenVerticalBeamCalculationCommand.Execute(null);
                e.Handled = true;
            }

            // 群杭沈下解析
            else if (e.Key == Key.F8)
            {
                ButtonGroupPileSettlement_Click(null, null);
                e.Handled = true;
            }

            // クイックヒント
            else if (e.Key == Key.F1 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                viewModel.IsQuickHintVisible = true;
            }

            // ヘルプ
            else if (e.Key == Key.F1)
            {
                MainWindowViewModel.OpenHelpWindow();
            }

            // ショートカット一覧
            else if (
                ((e.Key == Key.Oem2) || (e.Key == Key.Divide))
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            { MainWindowViewModel.OpenShortcutKeysWindow(); }

        }

        // ステータスバーのショートカットヒントクリック → ShortcutKeysWindow を開く
        // (F1 でも同等の機能だが、マウスユーザー向けに視認可能なボタンとして提供)
        private void ShortcutHintButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.OpenShortcutKeysWindow();
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


        private void SelectAllNodesButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAllNodes();
        }

        private void InvertActiveNodesButton_click(object sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;
            foreach (var item in vm.CurrentInputModel.PileLayoutItems)
                item.IsVisible = !item.IsVisible;

            // 一般節点
            if (vm.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in vm.CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General)
                        node.IsVisible = !node.IsVisible;
                }
            }
            // 梁要素
            if (vm.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in vm.CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsVisible = !beam.IsVisible;
            }
            // 基礎梁節点
            if (vm.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in vm.CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsVisible = !node.IsVisible;
            }

            UpdateWindow();
        }

        private void MergeElementsButton_click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            MainWindowViewModel.DeleteDuplicatedElements();
        }

        private void MergeNodesButton_click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel.DeleteDuplicatedPiles();
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomFit();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCanvas3D();
        }

        private void ButtonGroupPileSettlement_Click(object sender, RoutedEventArgs e)
        {
            // 入力は主画面の「群杭荷重」タブに集約。基礎梁無し用の「一回解析」サブタブをアクティブ化。
            // 基礎梁有りの反復解析は別リボンボタン (OpenGroupSettlementWithBeamWindowCommand) から起動。
            ActivateGroupPileLoadTab();
            if (TabItemLoadNonBeam != null && GroupPileTabControl != null)
            {
                GroupPileTabControl.SelectedItem = TabItemLoadNonBeam;
            }
        }

        private void ActivateGroupPileLoadTab()
        {
            // "土層沈下"タブを探してアクティブ化
            foreach (var doc in dockingManager.Layout.Descendents().OfType<LayoutDocument>())
            {
                if (doc.Title == "土層沈下")
                {
                    doc.IsSelected = true;
                    doc.IsActive = true;
                    break;
                }
            }
        }

        private void DataGridPileAxialForce_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            //var dataGrid = sender as DataGrid;
            //if (dataGrid == null || dataGrid.SelectedCells.Count == 0) return;

            //// 最初の選択セルの列を取得
            //var cell = dataGrid.SelectedCells[0];
            //var column = cell.Column as DataGridColumn;
            //if (column == null) return;

            //// 列ヘッダーやバインディング名で判定
            //string header = column.Header?.ToString() ?? "";
            //string bindingPath = "";
            //if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            //{
            //    bindingPath = binding.Path.Path;
            //}

            //// 荷重ケース名を判定（例: VL0, VLadd, 1-1, 1-2, ...）
            //string loadCaseName = null;
            //if (header.Contains("VL0") || bindingPath.Contains("AxialForceVL0"))
            //    loadCaseName = "VL0";
            //else if (header.Contains("VLadd") || bindingPath.Contains("AxialForceVLAdditional"))
            //    loadCaseName = "VLadd";
            //else if (header.Contains("1-1") || bindingPath.Contains("AxialForceLevel1s[0]"))
            //    loadCaseName = "1-1";
            //else if (header.Contains("1-2") || bindingPath.Contains("AxialForceLevel1s[1]"))
            //    loadCaseName = "1-2";
            //else if (header.Contains("1-3") || bindingPath.Contains("AxialForceLevel1s[2]"))
            //    loadCaseName = "1-3";
            //else if (header.Contains("1-4") || bindingPath.Contains("AxialForceLevel1s[3]"))
            //    loadCaseName = "1-4";
            //else if (header.Contains("2-1") || bindingPath.Contains("AxialForceLevel2s[0]"))
            //    loadCaseName = "2-1";
            //else if (header.Contains("1-2") || bindingPath.Contains("AxialForceLevel2s[1]"))
            //    loadCaseName = "2-2";
            //else if (header.Contains("1-3") || bindingPath.Contains("AxialForceLevel2s[2]"))
            //    loadCaseName = "2-3";
            //else if (header.Contains("1-4") || bindingPath.Contains("AxialForceLevel2s[3]"))
            //    loadCaseName = "2-4";

            //if (loadCaseName != null)
            //{
            //    // ViewModelのSelectedLoadCaseNameを変更
            //    var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;
            //    if (vm != null && vm.LoadCaseNameOption.Contains(loadCaseName))
            //    {
            //        vm.IsAxialForceLabelVisible = true;
            //        vm.SelectedLoadCaseName = loadCaseName;
            //    }

            //}
        }

        private void GroupPileTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tabControl = sender as TabControl;
            if (tabControl?.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header?.ToString() == "グリッド")
                {
                    // ViewModel取得
                    if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
                    {
                        vm.IsGroupPileGridVisible = true;
                    }
                }

                if (selectedTab.Header?.ToString() == "荷重")
                {
                    // ViewModel取得
                    if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
                    {
                        vm.IsSettlementLoadVisible = true;
                    }
                }
            }
        }

        private void DataGridPileAxialForce_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;

            if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
            {
                if (!vm.CheckAndResetElementSplit("杭軸力"))
                {
                    e.Cancel = true;
                    return;
                }

                // Undo はセル単位とするためデバウンスは使用しない。
                // CellEditEnding (DataGridPileAxialForce_OnCellEditEnding) で SaveUndoState が
                // セル毎に呼ばれるため、Ctrl+Z で 1 セルずつ巻き戻し可能。
            }
        }

        private void DataGridIsFrontPile_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;

            if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
            {
                if (!vm.CheckAndResetElementSplit("前後方杭"))
                {
                    e.Cancel = true;
                }
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                var binding = textBox?.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                e.Handled = true;
            }
        }

        private void DataGridGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;
            var (ok, oldVal) = TryGetPropertyValue(item, path);
            if (ok) _dgOldValues[(item, path)] = oldVal;
        }

        private void DataGridGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // 追加: Commitのみ処理
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            // Commit後の新値をリフレッシュして取得
            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                // 型合わせ（double/nullable等はPropertyChangeAction<object>で十分）
                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));

                if (DataContext is MainWindowViewModel vm)
                    vm.RequestUpdateWindow();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void EmbedmentAdjustButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsEmbedmentBoxVisible = true;
            }
        }

        //private void QuickHintToggle_Checked(object sender, RoutedEventArgs e)
        //{
        //    if (_isViewInteracting) return; // ビュー操作中なら無視
        //    var vm = DataContext as MainWindowViewModel;
        //    if (vm != null) vm.IsQuickHintVisible = true;
        //}
        private void QuickHintToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // 「杭」ドキュメントを前面に
                PileLayoutDocument.IsSelected = true;

                // 「配置」タブを選択（0番）
                var tc = this.FindName("PileTabControl") as TabControl;
                if (tc != null && tc.SelectedIndex != 0)
                    tc.SelectedIndex = 0;
            }
            catch
            {
                // 失敗してもアプリ動作には影響しないよう握りつぶす
            }
        }
        private void QuickHintToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm != null) vm.IsQuickHintVisible = false;
        }

        private void UpdateCanvasRightBlankClip()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel vm) return;
            if (Canvas3DLayout.Parent is not FrameworkElement parent) return;

            double parentWidth = parent.ActualWidth;
            if (parentWidth <= 0) return;

            double blank = vm.RightBlankWidthPx;
            blank = Math.Clamp(blank, 0, parentWidth - 1);

            double usable = parentWidth - blank;
            if (usable < 0) usable = 0;

            Canvas3DLayout.Width = usable;
            Canvas3DLayout.Clip = null; // Clip不要
        }

        #region 基礎梁ビジュアル編集 - イベントハンドラ

        /// <summary>
        /// 基礎梁追加のプレビュー線を描画
        /// </summary>
        private void DrawFoundationBeamPreview(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            if (vm.TempStartNode == null) return;

            // 既存のプレビュー線を削除
            ClearFoundationBeamPreview();

            // 開始ノードの座標を解決
            var coords = vm.CurrentInputModel.GetNodeCoordinates(vm.TempStartNode.Type, vm.TempStartNode.Id);
            if (coords == null) return; // 座標が見つからない場合は何もしない

            // 開始ノードの画面座標を取得
            var startNodeLoc3D = new Point3D(coords.Value.X, coords.Value.Y, coords.Value.Z);
            var startScreenPos = vm.CanvasThreeDView.Transformation(startNodeLoc3D);

            // プレビュー線を作成（破線スタイル）
            var previewLine = new Line
            {
                X1 = startScreenPos.X,
                Y1 = startScreenPos.Y,
                X2 = mousePos.X,
                Y2 = mousePos.Y,
                Stroke = Brushes.Orange,
                StrokeThickness = 2.0,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Tag = "FoundationBeamPreview",
                IsHitTestVisible = false // マウスイベントに反応しない
            };

            Canvas3DLayout.Children.Add(previewLine);
        }

        /// <summary>
        /// プレビュー線をクリア
        /// </summary>
        private void ClearFoundationBeamPreview()
        {
            var existingPreview = Canvas3DLayout.Children.OfType<Line>()
                .FirstOrDefault(l => l.Tag?.ToString() == "FoundationBeamPreview");
            if (existingPreview != null)
            {
                Canvas3DLayout.Children.Remove(existingPreview);
            }
        }

        /// <summary>
        /// 基礎梁編集モードのマウスクリック処理
        /// </summary>
        /// <returns>処理された場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleFoundationBeamEditMode(MouseButtonEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 編集モードがNoneの場合は何もしない
            if (vm.CurrentEditMode == CanvasEditMode.None)
            {
                return false;
            }

            Point mousePos = e.GetPosition(Canvas3DLayout);

            switch (vm.CurrentEditMode)
            {
                case CanvasEditMode.AddNode:
                    HandleAddNode(mousePos);
                    break;

                case CanvasEditMode.AddElement:
                    HandleAddElement(mousePos);
                    break;

                case CanvasEditMode.Delete:
                    HandleDelete(mousePos);
                    break;
            }

            // 編集モード中は常にtrueを返して選択処理を抑制
            return true;
        }

        /// <summary>
        /// ノード追加処理
        /// </summary>
        /// <returns>常にtrue（必ず処理を行う）</returns>
        private bool HandleAddNode(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 基礎梁入力データの初期化
            if (vm.CurrentInputModel.FoundationBeamInput == null)
            {
                vm.CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;

            // 一点目の場合は(0, 0, ΔZc=1.0)に固定
            if (nodes.Count == 0)
            {
                var firstNode = new FoundationNode
                {
                    No = 1,
                    X = 0,
                    Y = 0,
                    Z = 1.0, // デフォルトΔZc
                    Name = "Node-1"
                };
                nodes.Add(firstNode);
                vm.RequestUpdateWindow();
                return true;
            }

            // 二点目以降: マウス位置→3D座標変換
            Point3D? rawPos = GetXYFromMousePosition(mousePos);
            if (rawPos == null) return true; // 座標変換に失敗しても処理したとみなす

            // 杭位置にスナップ
            Point3D finalPos = ApplyPileSnap(rawPos.Value);

            // スナップした杭を見つけて、ΔZcを適用
            var piles = vm.CurrentInputModel.PileLayoutItems;
            double finalZ = finalPos.Z; // デフォルトはrawPosのZ

            foreach (var pile in piles)
            {
                double dx = pile.X - finalPos.X;
                double dy = pile.Y - finalPos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // スナップした杭が見つかった場合（XY座標が一致）
                if (dist < 0.01) // 1cm以内なら一致とみなす
                {
                    // v2 セマンティクス: pile.Z は接合節点 Z (FoundationNode の Z はそのまま)
                    finalZ = pile.Z;
                    break;
                }
            }

            // 新しいノードを作成
            var newNode = new FoundationNode
            {
                No = nodes.Count + 1,
                X = finalPos.X,
                Y = finalPos.Y,
                Z = finalZ,
                Name = $"Node-{nodes.Count + 1}"
            };

            nodes.Add(newNode);

            // Undo登録（今後実装）
            // _undoManager.RegisterAction(new AddNodeAction(newNode));

            // 3D更新
            vm.RequestUpdateWindow();
            return true;
        }

        /// <summary>
        /// 要素追加処理（2クリック方式）
        /// </summary>
        /// <returns>処理した場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleAddElement(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 基礎梁入力データの初期化
            if (vm.CurrentInputModel.FoundationBeamInput == null)
            {
                vm.CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;

            // ヒットテストして節点参照を取得
            NodeReference? hitRef = null;
            string hitNodeName = "";

            // まず既存の基礎梁ノードをヒットテスト
            FoundationNode? hitFoundationNode = HitTestNode(mousePos);
            if (hitFoundationNode != null)
            {
                hitRef = new NodeReference(NodeReferenceType.FoundationNode, hitFoundationNode.Id);
                hitNodeName = hitFoundationNode.Name;
            }

            // 既存ノードがない場合、InputNode（一般節点）をヒットテスト
            if (hitRef == null)
            {
                var hitInputNode = HitTestInputNode(mousePos);
                if (hitInputNode != null)
                {
                    // Pile型のInputNodeはクリック不可（無視）
                    if (hitInputNode.Type == NodeType.Pile)
                    {
                        return false; // Pile型は無視するが、処理していないのでfalse
                    }

                    // General型のInputNodeを直接参照
                    hitRef = new NodeReference(NodeReferenceType.GeneralNode, hitInputNode.UniqueId);
                    hitNodeName = $"一般節点-{hitInputNode.No}";
                }
            }

            // 既存ノードがない場合、接合節点をヒットテスト
            if (hitRef == null && vm.IsConnectionNodeVisible)
            {
                var hitPile = HitTestConnectionNode(mousePos);
                if (hitPile != null)
                {
                    // 杭配置を直接参照
                    hitRef = new NodeReference(NodeReferenceType.PileLayout, hitPile.UniqueId);
                    hitNodeName = $"杭配置-{hitPile.PileNo}";
                }
            }

            if (hitRef == null)
            {
                // ノードがクリックされていない場合は何もしない
                return false;
            }

            if (vm.TempStartNode == null)
            {
                // 1回目のクリック: 開始ノードを記録
                vm.TempStartNode = hitRef;
                vm.StatusMessage = $"開始ノード: {hitNodeName} → 終了ノードをクリック";
            }
            else
            {
                // 2回目のクリック: 要素を作成
                if (hitRef.Type == vm.TempStartNode.Type && hitRef.Id == vm.TempStartNode.Id)
                {
                    MessageService.Show("同じノードは接続できません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    vm.TempStartNode = null;
                    vm.StatusMessage = string.Empty;
                    ClearFoundationBeamPreview(); // プレビュー線をクリア
                    return true; // 処理したのでtrue
                }

                var beams = vm.CurrentInputModel.FoundationBeamInput.Beams;

                // デフォルトの材料・断面番号 (1-based 位置インデックス)
                int defaultMaterialNo = vm.CurrentInputModel.FoundationBeamInput.Materials.Count > 0 ? 1 : 1;
                int defaultSectionNo = vm.CurrentInputModel.FoundationBeamInput.Sections.Count > 0 ? 1 : 1;

                var newBeam = new FoundationBeam
                {
                    // No プロパティ廃止 (位置 = ID)
                    // 新方式: Type + Guid で参照
                    NodeI_Type = vm.TempStartNode.Type,
                    NodeI_Id = vm.TempStartNode.Id,
                    NodeJ_Type = hitRef.Type,
                    NodeJ_Id = hitRef.Id,
                    // 材料・断面番号
                    MaterialNo = defaultMaterialNo,
                    SectionNo = defaultSectionNo,
                    SectionName = $"Beam-{beams.Count + 1}",
                    Width = 0.5,
                    Height = 0.8,
                    YoungModulus = 2.5e7,
                    ShearModulus = 1.04e7
                };

                beams.Add(newBeam);

                // 参照先 (MaterialNo / SectionNo) のデフォルトを保証
                vm.CurrentInputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();

                // リセット
                vm.TempStartNode = null;
                vm.StatusMessage = string.Empty;
                ClearFoundationBeamPreview(); // プレビュー線をクリア

                // Undo登録（今後実装）

                // 3D更新
                vm.RequestUpdateWindow();
            }

            return true; // 処理したのでtrue
        }

        /// <summary>
        /// 削除処理
        /// </summary>
        /// <returns>削除した場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleDelete(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // ノードをヒットテスト
            FoundationNode? hitNode = HitTestNode(mousePos);

            if (hitNode == null) return false;

            // 確認ダイアログ
            var result = MessageService.Show(
                $"ノード '{hitNode.Name}' を削除しますか？\n接続されている要素も削除されます。",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return false;

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;
            var beams = vm.CurrentInputModel.FoundationBeamInput.Beams;

            // 接続されている要素を削除（カスケード削除）
            var beamsToRemove = beams.Where(b =>
                (b.NodeI_Type == NodeReferenceType.FoundationNode && b.NodeI_Id == hitNode.Id) ||
                (b.NodeJ_Type == NodeReferenceType.FoundationNode && b.NodeJ_Id == hitNode.Id)).ToList();
            foreach (var beam in beamsToRemove)
            {
                beams.Remove(beam);
            }

            // ノードを削除
            nodes.Remove(hitNode);

            // 節点 No は振り直し (FoundationNode.No は廃止対象外)。
            // 梁要素 No は廃止 (位置 = ID)
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].No = i + 1;
            }

            // Undo登録（今後実装）

            // 3D更新
            vm.RequestUpdateWindow();
            return true;
        }

        #endregion

        #region 基礎梁ビジュアル編集 - 座標変換

        /// <summary>
        /// 現在のビューが平面図（Phi≒90°）かどうかを判定
        /// </summary>
        private bool IsPlanView()
        {
            var vm = (MainWindowViewModel)DataContext;
            // Phi=90°±5°を平面図とみなす
            return Math.Abs(vm.CanvasThreeDView.Phi - 90.0) < 5.0;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）から3D座標（XY）を取得（平面図専用、Z固定）
        /// </summary>
        /// <param name="mousePos">Canvas上のマウス位置</param>
        /// <returns>3D座標（Z=DefaultFoundationBeamZ）</returns>
        private Point3D? GetXYFromMousePosition(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 既存のInverseTransformation()を使用
            Point3D worldPos = vm.CanvasThreeDView.InverseTransformation(mousePos);

            // Z座標は常にDefaultFoundationBeamZを使用（どのビューでも）
            // 杭位置にスナップする場合は、HandleAddNode内でΔZcが適用される
            return new Point3D(worldPos.X, worldPos.Y, vm.DefaultFoundationBeamZ);
        }

        /// <summary>
        /// 杭位置に自動スナップ（tolerance以内の杭があればその位置に補正）
        /// </summary>
        /// <param name="rawPos">生の3D座標</param>
        /// <param name="tolerance">スナップ許容距離（m）</param>
        /// <returns>スナップ後の3D座標</returns>
        private Point3D ApplyPileSnap(Point3D rawPos, double tolerance = 0.5)
        {
            var vm = (MainWindowViewModel)DataContext;
            var piles = vm.CurrentInputModel.PileLayoutItems;

            // 最も近い杭を検索
            double minDist = double.MaxValue;
            Point3D? snapPos = null;

            foreach (var pile in piles)
            {
                double dx = pile.X - rawPos.X;
                double dy = pile.Y - rawPos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist < minDist && dist < tolerance)
                {
                    minDist = dist;
                    snapPos = new Point3D(pile.X, pile.Y, rawPos.Z);
                }
            }

            return snapPos ?? rawPos;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）で基礎梁ノードをヒットテスト
        /// </summary>
        /// <param name="mousePos">Canvas上のマウス位置</param>
        /// <param name="hitRadius">ヒット半径（ピクセル）</param>
        /// <returns>ヒットしたノード（なければnull）</returns>
        private FoundationNode? HitTestNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var nodes = vm.CurrentInputModel.FoundationBeamInput?.Nodes;

            if (nodes == null || nodes.Count == 0) return null;

            // 各ノードの画面座標を計算してヒットテスト
            foreach (var node in nodes)
            {
                var nodeLoc3D = new Point3D(node.X, node.Y, node.Z);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）で接合節点（杭頭+ΔZc）をヒットテスト
        /// </summary>
        /// <returns>ヒットした杭のPileLayoutDataItem（なければnull）</returns>
        private PileLayoutDataItem? HitTestConnectionNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var piles = vm.CurrentInputModel?.PileLayoutItems;

            if (piles == null || piles.Count == 0) return null;

            // 各杭の接合節点位置でヒットテスト (v2 セマンティクス: pile.Z は接合節点 Z)
            foreach (var pile in piles)
            {
                double connectionZ = pile.Z;
                var nodeLoc3D = new Point3D(pile.X, pile.Y, connectionZ);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return pile;
                }
            }

            return null;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）でInputNode（一般節点）をヒットテスト
        /// </summary>
        /// <returns>ヒットしたInputNode（なければnull）</returns>
        private InputNode? HitTestInputNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var nodes = vm.CurrentInputModel?.InputNodes;

            if (nodes == null || nodes.Count == 0) return null;

            // 各InputNodeの画面座標を計算してヒットテスト
            foreach (var node in nodes)
            {
                if (!node.IsVisible) continue;

                var nodeLoc3D = new Point3D(node.X, node.Y, node.Z);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return node;
                }
            }

            return null;
        }

        #endregion

        // ---- E.18 / C.9 Dashboard / Command Palette ハンドラ ----------------

        private void OpenResultDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var w = new ResultDashboardWindow(vm) { Owner = this };
            w.ShowDialog();
        }

        private void OpenCommandPalette_Click(object sender, RoutedEventArgs e) => OpenCommandPalette();

        private void OpenCommandPalette()
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var w = new CommandPaletteWindow(vm, this) { Owner = this };
            w.ShowDialog();
        }

        private void OpenEditHistoryPanel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var w = new HistoryPanelWindow(vm, vm.UndoManager) { Owner = this };
            w.Show();
        }

        /// <summary>
        /// Ctrl+Shift+P でコマンドパレットを起動。テキストボックス入力中でも有効。
        /// (テキストボックス内の Ctrl+Shift+P をユーザがコマンドとして使うことは想定していないため
        /// PreviewKeyDown で先取りして OK)
        /// </summary>
        private void MainWindow_GlobalShortcutPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P
                && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                // 同期的に ShowDialog を呼ぶと現在の PreviewKeyDown 伝搬が止まり、
                // InputVisualizer 等の後続ハンドラに届かなくなる (P が記録されない)。
                // 1 ティック遅延させて、現在のキーイベントを完走させてからモーダルを開く。
                Dispatcher.BeginInvoke(new Action(OpenCommandPalette), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}

