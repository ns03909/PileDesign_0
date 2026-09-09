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


        public event PropertyChangedEventHandler? PropertyChanged;

        // プロパティ変更通知を発行するヘルパーメソッド
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Backstage を開き「計算例」タブを選択する。
        /// 起動時の案内ダイアログで「計算例を開く」を選んだときの遷移先。
        /// </summary>
        public void ShowExamplesBackstage()
        {
            if (BackstageExamplesTab == null || MainBackstage == null) return;

            BackstageExamplesTab.IsSelected = true;
            MainBackstage.IsOpen = true;
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

            // ViewModelインスタンスを生成し、フィールドとDataContext両方にセット
            _mainWindowViewModel = new MainWindowViewModel();
            // DataContextをViewModelに設定
            DataContext = _mainWindowViewModel;

            // 画面が実際に使うのはこの ViewModel。緊急保存と App.InputModel の宛先にする。
            App.CurrentMainViewModel = _mainWindowViewModel;

            // 自動保存はここで 1 回だけ始める。ViewModel のコンストラクタで始めると、
            // テストなどで作られた分まで動き、同じ名前の一時ファイルを取り合って
            // 「別のプロセスが使用中」で落ちる。
            _mainWindowViewModel.BeginAutoSaveSession();

            var viewModel = _mainWindowViewModel;

            // 追加: ZoomFitAction をコードビハインド実装に接続
            viewModel.ZoomFitAction = ZoomFit;

            // 沈下土層ON時: 群杭沈下ウィンドウの土層タブを表示
            viewModel.ActivateSettlementSoilTabAction = () =>
                ShowGroupSettlementWindow(MainWindowViewModel.GroupSettlementInputTab.SoilLayers);

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
            viewModel.HideTransientOverlaysAction = HideBeamResultTooltip;
            viewModel.ShowToastAction = (msg, type) => ShowToast(msg, (ToastType)type);

            // 群杭沈下の入力タブを開く (実行できない理由を出すときに、直す場所を見せる)
            viewModel.ActivateGroupSettlementInputTabAction = ShowGroupSettlementWindow;

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
                // 保存完了後の再 Close: そのまま閉じる
                return;
            }

            // 保存していない作業が無ければ確認せずに閉じる。
            // (起動して何もしなかった / 保存した直後 / 読み込んだだけ)
            if (DataContext is MainWindowViewModel vm && !vm.HasUnsavedWork)
                return;

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
                    return; // 実際に閉じるのは保存後の 2 回目の Closing
                case MessageBoxResult.No:
                    // 保存せずに閉じる
                    break;
                case MessageBoxResult.Cancel:
                    // 閉じるのをキャンセル
                    e.Cancel = true;
                    return;
            }
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
                _mainWindowViewModel.OpenFromMruCommand.Execute(files[0]);
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
            // 解析のキーが効かないときは理由を出す (実行は InputBindings が行う)。
            // <b>PreviewKeyDown 側に置く。</b>F2・Delete・Alt+数字がここで拾えている以上、
            // この経路はキーが確実に届く。バブリングの KeyDown は途中で握り潰されうる。
            // 二重に出さないよう、既に処理済みのイベントには触らない
            // (Canvas3DLayout と Window の両方から呼ばれる)。
            if (!e.Handled && ExplainIfAnalysisKeyIsBlocked(e, DataContext as MainWindowViewModel)) return;

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
        /// <summary>
        /// 解析のキー (F5 / F6 / Shift+F6 / F7) が<b>効かないときに理由を出す</b>。
        ///
        /// 実行そのものは <c>Window.InputBindings</c> が行う。ところが実行できない状態だと
        /// InputBinding は<b>黙って何もしない</b>。ボタンなら灰色と説明で分かるが、
        /// キーには押した感触が無く「押しても何も起きない」としか見えない。
        ///
        /// ここでは<b>理由を出すだけ</b>で実行はしない (CanExecute を迂回しないため)。
        /// 実行できる状態なら何もせず、InputBinding にそのまま任せる。
        /// </summary>
        private bool ExplainIfAnalysisKeyIsBlocked(KeyEventArgs e, MainWindowViewModel? viewModel)
        {
            if (viewModel == null) return false;

            (System.Windows.Input.ICommand command, string title, string? reason)? target =
                (e.Key, Keyboard.Modifiers) switch
                {
                    (Key.F5, ModifierKeys.None) =>
                        (viewModel.OpenLateralLoadAnalysisWindowCommand, "水平解析", null),
                    (Key.F6, ModifierKeys.None) =>
                        (viewModel.OpenSettlementWindowCommand, "単杭沈下解析", null),
                    (Key.F6, ModifierKeys.Shift) =>
                        (viewModel.OpenVerticalBeamCalculationCommand, "単杭沈下解析（基礎梁考慮）", null),
                    _ => null,
                };

            // 群杭沈下だけは、理由を出すついでに<b>直す場所のタブを開く</b>ので VM に任せる
            if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (!viewModel.ShowGroupSettlementBlockerIfAny()) return false;
                e.Handled = true;
                return true;
            }

            if (target is not { } t) return false;
            if (t.command.CanExecute(null)) return false;   // 実行できる → InputBinding に任せる

            // 理由が用意されていないもの (ウィンドウを開く系) は共通の前提を出す
            PileDesign.Services.MessageService.Show(
                t.reason ?? PileDesign.Services.GuardMessages.NotElementSplit,
                t.title, MessageBoxButton.OK, MessageBoxImage.Information);
            e.Handled = true;
            return true;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;


            // ファイルを開く
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // 保存 → Reset の順序はメソッド内で await して守られている。
                _ = viewModel?.NewInputModelFile();
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
            // 杭要素分割 (Ctrl+D / F4)・水平解析 (F5)・単杭沈下 (F6)・基礎梁考慮沈下 (F7) は
            // Window.InputBindings で処理する。ここで VM のメソッドやコマンドを直接呼ぶと
            // CanExecute を迂回してしまい、「ボタンは灰色なのにキーでは実行できて、
            // 直後にダイアログで叱られる」状態になる (Execute は CanExecute を見ない)。

            // 群杭沈下ウィンドウを開く (解析の実行ではないので CanExecute は要らない)
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
        internal void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
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
        internal void ContextMenu_Opened(object sender, RoutedEventArgs e)
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
            // 入力は群杭沈下ウィンドウに集約。基礎梁無し用の「解析（一般）」タブを開く。
            // 基礎梁有りの反復解析は別リボンボタン (OpenGroupSettlementWithBeamWindowCommand) から起動。
            ShowGroupSettlementWindow(MainWindowViewModel.GroupSettlementInputTab.GeneralAnalysis);
        }

        /// <summary>
        /// 「解析条件設定」の群杭沈下ボタン。<b>条件から</b>開くので土層タブを出す。
        /// 実行側 (「群杭沈下解析」グループ) は解析タブを出す。入口を役割で分けている。
        /// </summary>
        private void OpenGroupSettlementWindow_Click(object sender, RoutedEventArgs e)
            => ShowGroupSettlementWindow(MainWindowViewModel.GroupSettlementInputTab.SoilLayers);

        private GroupSettlementWindow? _groupSettlementWindow;

        /// <summary>
        /// 群杭沈下ウィンドウを開き、指定のタブを選ぶ。
        /// 実行できない理由を出す前にも呼び、直す場所を見せる。
        ///
        /// メイン画面の図 (グリッド・荷重面) を見ながら編集できるようモードレスで開く。
        /// 二重に開かず、既に開いていれば前面に出してタブだけ切り替える。
        /// </summary>
        internal void ShowGroupSettlementWindow(MainWindowViewModel.GroupSettlementInputTab tab)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (_groupSettlementWindow is not { IsLoaded: true })
            {
                var w = new GroupSettlementWindow(this, vm);
                w.Closed += (_, __) => _groupSettlementWindow = null;
                _groupSettlementWindow = w;
                w.Show();
            }
            else
            {
                _groupSettlementWindow.Activate();
            }

            _groupSettlementWindow.SelectTab(tab);
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

        internal void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                var binding = textBox?.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                e.Handled = true;
            }
        }

        internal void DataGridGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
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


        // ---- E.18 / C.9 Dashboard / Command Palette ハンドラ ----------------

        private ResultDashboardWindow? _resultDashboard;

        /// <summary>
        /// ダッシュボードはモードレスで開く。
        /// 「杭配置を色分け」をダッシュボードから切り替えて、メインビューの色を見ながら
        /// 一覧を読むためで、モーダルだとメインビューを回転もできない。
        /// 二重に開かず、既に開いていれば前面に出す。
        /// </summary>
        private void OpenResultDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (_resultDashboard is { IsLoaded: true })
            {
                _resultDashboard.RefreshNow();
                _resultDashboard.Activate();
                return;
            }
            var w = new ResultDashboardWindow(vm) { Owner = this };
            w.Closed += (_, __) => _resultDashboard = null;
            _resultDashboard = w;
            w.Show();
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

