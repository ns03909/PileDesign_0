using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using PileDesign.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PileDesign.Services;

namespace PileDesign.Views
{
    public partial class ElementDivisionWindow : Window
    {
        private ElementDivisionViewModel? _viewModel;
        private readonly MainWindowViewModel _mainWindowViewModel;
        private bool _isLoaded = false; // フラグを追加
        private bool _isClosingHandled = false;
        private bool _wasElementSplitBeforeOpen; // ウィンドウを開く前のIsElementSplit状態

        /// <summary>
        /// 「保存」で閉じたか。閉じたあとに読む。
        ///
        /// 見るだけで閉じた場合は入力を触っていない (編集はすべて複製に対して行う) ので、
        /// 呼び出し側は編集済みの印も Undo の記録も残してはいけない。
        /// </summary>
        public bool IsSaved => _viewModel?.IsOkPressed == true;

        public ElementDivisionWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();
            InitializePaneMaximizer();
            _mainWindowViewModel = mainWindowViewModel;

            Loaded += ElementDivisionWindow_Loaded;
            ContentRendered += ElementDivisionWindow_ContentRendered;
        }

        /// <summary>
        /// ウィンドウが描画された後に重い初期化を実行する
        /// </summary>
        private async void ElementDivisionWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= ElementDivisionWindow_ContentRendered;

            try
            {
                // UIスレッドを一旦解放してウィンドウを完全に描画
                await System.Threading.Tasks.Task.Yield();

                // Step 1: SoilPiles/SoilEmbedmentデータ生成（UIスレッド：ObservableCollection操作を含むため）
                _wasElementSplitBeforeOpen = _mainWindowViewModel.IsElementSplit;

                // 分割済みなら再生成しない。保存されている分割をそのまま開く。
                //
                // 再生成は「杭体・地盤を変えたあとも地層境界節点を作り直す」ためのもので、
                // ジオメトリを変える編集では IsElementSplit が落ちる (分割は無効になる) から、
                // ここに来て true ということは<b>保存されている分割が今の形状に対して有効</b>。
                // それを最小分割で上書きすると、解析に使った各要素の水平ばねを見に来ただけの人が
                // 別物を見せられることになる。
                if (!_wasElementSplitBeforeOpen)
                {
                    _mainWindowViewModel.IsElementSplit = false;
                    _mainWindowViewModel.GenerateSoilPilesImmediate();
                    _mainWindowViewModel.CurrentInputModel.GenerateSoilEmbedment();
                }

                // Step 2: DeepCopyをバックグラウンドスレッドで実行（純粋な計算のため安全）
                var elementDivision = _mainWindowViewModel.CurrentInputModel.ElementDivision;
                var sourcePiles = elementDivision?.SoilPiles?.ToList() ?? new List<SoilPile>();
                var sourceEmbedment = elementDivision?.SoilEmbedment;

                var (copiedPiles, copiedEmbedment) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var piles = sourcePiles.Select(sp => sp.DeepCopy()).ToList();
                    var embedment = sourceEmbedment?.DeepCopy();
                    return (piles, embedment);
                });

                // Step 3: ViewModel作成（UIスレッド、事前コピー済みデータを渡す）
                _viewModel = new ElementDivisionViewModel(_mainWindowViewModel, copiedPiles, copiedEmbedment);
                _viewModel.WasElementSplitBeforeOpen = _wasElementSplitBeforeOpen;
                DataContext = _viewModel;

                // UI初期化
                InitializeViewModelAndUI();

                // 分割済みを開いたときは全セット確認済みとして扱う。
                // ランプは「再生成された分割を 1 セットずつ目視した」ことを表す印なので、
                // 保存済みのものを開いた場合に消灯から始めると、見るだけのつもりでも
                // 全セットを巡らないと保存できない。
                if (_wasElementSplitBeforeOpen) _viewModel?.MarkAllSetsAsReviewed();
            }
            catch (Exception ex)
            {
                MessageService.ShowError("杭要素分割ウィンドウを開けませんでした。", ex, "初期化エラー");
                Close();
                return;
            }
            finally
            {
                // 読み込み中オーバーレイを非表示
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// ViewModel初期化とUI要素の接続を行う
        /// </summary>
        private void InitializeViewModelAndUI()
        {
            if (_viewModel == null) return;

            _viewModel.ElementDivisionWindowInstance = this;
            _viewModel.Canvas = Canvas;
            _viewModel.CanvasEmbedment = CanvasEmbedment;

            // DataGridZs の既存設定
            DataGridZs.SelectionUnit = DataGridSelectionUnit.FullRow;
            DataGridZs.SelectionMode = DataGridSelectionMode.Single;

            _viewModel.Initialize();

            _viewModel.RequestClose += (s, e2) =>
            {
                if (_isClosingHandled) return;
                _isClosingHandled = true;
                if (this.IsLoaded && this.IsVisible) this.Close();
            };

            // CSVエクスポートメニューを追加
            PlotHelper.AddCsvExportMenu(wpfPlotKh0, "水平地盤反力係数");
            PlotHelper.AddCsvExportMenu(wpfPlotPFront, "前方杭反力");
            PlotHelper.AddCsvExportMenu(wpfPlotPRear, "後方杭反力");
            PlotHelper.AddCsvExportMenu(wpfPlotPpPo, "土圧分布");
            PlotHelper.AddCsvExportMenu(wpfPlotDGBperM, "単位幅土圧合力");
            PlotHelper.AddCsvExportMenu(wpfPlotDGB, "土圧合力");
        }

        /// <summary>
        /// Ctrl+Enter で「保存して閉じる」を実行（DataGridのCtrl+Enter行コピーを抑制）
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel?.OkCommand?.CanExecute(null) == true)
                {
                    _viewModel.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control
                     && Keyboard.FocusedElement is not TextBox)
            {
                // SelectionMode=Single の DataGrid 内蔵 SelectAll が NotSupportedException を投げるため、
                // Window レベルで先取りして AutoZsAllPilesCommand を実行する
                if (_viewModel?.AutoZsAllPilesCommand?.CanExecute(null) == true)
                {
                    _viewModel.AutoZsAllPilesCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void ElementDivisionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;

            Canvas.SizeChanged += Canvas_SizeChanged;
            CanvasEmbedment.SizeChanged += CanvasEmbedment_SizeChanged;

            _isLoaded = true;
        }

        private void CanvasEmbedment_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                try
                {
                    vm.DrawSoilEmbedment();
                }
                catch
                {
                    // 描画エラーはここで吸収（必要ならログ出力を追加）
                }
            }

            // Loaded は一度だけ実行するので解除
            if (sender is FrameworkElement fe)
                fe.Loaded -= CanvasEmbedment_Loaded;
        }

        // Soil pile lamp clicked
        private void SoilPileLamp_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ElementDivisionViewModel vm) return;
            if (sender is not Button btn) return;
            if (btn.DataContext is LampState lamp)
            {
                // LampState.Index を使って該当セットを選択
                vm.SelectSoilPileIndex(lamp.Index);
                // タブを開く（UI 側で）
                MainTabControl.SelectedIndex = 0;
            }
        }

        // Embedment lamp clicked
        private void EmbedmentLamp_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ElementDivisionViewModel vm) return;
            if (sender is not Button btn) return;
            if (btn.DataContext is LampState lamp)
            {
                vm.SelectEmbedmentIndex(lamp.Index);
                MainTabControl.SelectedIndex = 1; // 根入れタブを開く（TabControl の構成に合わせる）
            }
        }

        // 公開ヘルパー（ViewModel の呼び出しに対応）
        public void OpenSoilPileTab(int pileIndex)
        {
            MainTabControl.SelectedIndex = 0;
            ComboBoxSoilPile.SelectedItem = (DataContext as ElementDivisionViewModel)?.SelectedSoilPileNo;
        }

        public void OpenEmbedmentTab()
        {
            MainTabControl.SelectedIndex = 1;
        }

        // 追加: Canvas サイズ変更ハンドラ（ElementDivisionWindow クラス内に挿入）
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                try { vm.DrawShapes(); }
                catch { /* 念のため例外吸収 */ }
            }
        }

        private void CanvasEmbedment_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                try { vm.DrawSoilEmbedment(); }
                catch { /* 念のため例外吸収 */ }
            }
        }



        // UI側（Window）に追加するイベントハンドラ
        // クラス ElementDivisionWindow の中に挿入してください。

        // TabControl の SelectionChanged ハンドラ
        private void TabControl_SelectionChange(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not ElementDivisionViewModel vm) return;
            if (sender is not TabControl tabControl) return;

            // "根入れ" タブが選択されたら描画
            if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header?.ToString()?.Contains("根入れ") == true)
            {
                if (CanvasEmbedment.ActualWidth > 0 && CanvasEmbedment.ActualHeight > 0)
                {
                    vm.DrawSoilEmbedment();
                }
                else
                {
                    // Loaded 時に描画するよう登録
                    CanvasEmbedment.Loaded -= CanvasEmbedment_Loaded;
                    CanvasEmbedment.Loaded += CanvasEmbedment_Loaded;
                }
            }
        }

        // ComboBox の SelectionChanged ハンドラ（土層-杭セット切替）
        private void ComboBoxSoilPile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not ElementDivisionViewModel vm) return;
            if (sender is not ComboBox) return;

            if (e.RemovedItems.Count > 0 && e.AddedItems.Count > 0)
            {
                var previousValue = e.RemovedItems[0] as int?;
                var newValue = e.AddedItems[0] as int?;

                if (previousValue.HasValue && newValue.HasValue)
                {
                    vm.OnComboBoxSoilPileSelectionChanged(e);
                }
            }
        }

        // Xi / ROnB の TextChanged ハンドラ
        private void XiROnB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.OnXiROnChanged();
            }
        }

        // 以下を ElementDivisionWindow クラス内に追加してください。
        // 既に同名メソッドがある場合は重複しないよう既存を置換してください。

        // DataGrid の BeginningEdit
        private void DataGridZs_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.OnDataGridZsBeginningEdit(e);
            }
        }

        // DataGrid の CellEditEnding
        private void DataGridZs_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.OnDataGridZsCellEditEnding(sender, e);
            }
        }

        // DataGrid の SelectionChanged（杭 Z リスト）
        private void DataGridZs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                if (dg.SelectedItem is PileZDataItem selectedItem)
                {
                    vm.SelectedZDataItem = selectedItem;
                }
            }
        }

        // DataGrid の RowEditEnding（杭Z・根入れZ 両方で同名を使っているため両方から来る）
        private void DataGridZs_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.OnDataGridZsChanged();
                // 行番号振り直し（既存 UI 更新と同様）
                if (vm.ElementDivisionWindowInstance?.DataGridZs is DataGrid grid)
                {
                    foreach (var item in grid.Items)
                    {
                        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
                        if (row != null) row.Header = (row.GetIndex() + 1).ToString();
                    }
                }
            }
        }

        // Embedment の RowEditEnding
        private void DataGridEmbedmentZs_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.OnDataGridEmbedmentZsChanged();
                if (vm.ElementDivisionWindowInstance?.DataGridEmbedmentZs is DataGrid grid)
                {
                    foreach (var item in grid.Items)
                    {
                        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
                        if (row != null) row.Header = (row.GetIndex() + 1).ToString();
                    }
                }
            }
        }

        // DataGrid LoadingRow（行番号付与）
        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        // horizon の CellEditEnding（基準水平地盤反力係数 kh0 の手入力を土層単位で適用）
        private void Horizon_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (DataContext is not ElementDivisionViewModel vm) return;
            if (e.Row?.Item is not HorizontalSoilReactionItem item) return;

            // 編集列が kh0 列（Binding パスが "Kh0"）のときのみ処理
            if (e.Column is not DataGridTextColumn col
                || col.Binding is not System.Windows.Data.Binding b
                || b.Path?.Path != nameof(HorizontalSoilReactionItem.Kh0))
                return;

            if (e.EditingElement is not TextBox tb) return;
            if (!double.TryParse(tb.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.CurrentCulture, out double val))
                return;

            // CellEditEnding 中に ItemsSource を再構築すると例外になるため、確定後に遅延実行する
            Dispatcher.BeginInvoke(new Action(() => vm.ApplyKh0Edit(item, val)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // horizon の SelectedCellsChanged（セル選択 → VM へ反映）
        private void Horizon_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                if (dg.SelectedCells != null && dg.SelectedCells.Count > 0)
                {
                    var item = dg.SelectedCells[0].Item as HorizontalSoilReactionItem;
                    if (item != null) vm.SelectedLayeronDataGrid = item;
                }
            }
        }

        // horizon の CurrentCellChanged
        private void Horizon_CurrentCellChanged(object sender, EventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                // DataGridCellInfo は値型なので ?. は使用不可。まずセルを取得してから Item を参照する。
                var cell = dg.CurrentCell;
                var itemObj = cell.Item;
                if (itemObj is HorizontalSoilReactionItem item)
                {
                    vm.SelectedLayeronDataGrid = item;
                }
            }
        }

        // Embedment DataGrid の SelectedCellsChanged（セル選択 → VM へ反映）
        private void Embedment_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                if (dg.SelectedCells != null && dg.SelectedCells.Count > 0)
                {
                    var cellInfo = dg.SelectedCells[0];
                    var item = cellInfo.Item as EmbedmentZDataItem;
                    if (item != null)
                    {
                        // 一致する VM 内インスタンスを探して設定（同一インスタンスでない場合に備える）
                        const double eps = 1e-6;
                        var matched = vm.EmbedmentZsCollection?.FirstOrDefault(z => Math.Abs(z.Z - item.Z) <= eps);
                        vm.SelectedEmbedmentZ = matched ?? item;
                    }
                }
            }
        }

        // Embedment の CurrentCellChanged
        private void Embedment_CurrentCellChanged(object sender, EventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                // 同様に CurrentCell?.Item を避ける
                var cell = dg.CurrentCell;
                var itemObj = cell.Item;
                if (itemObj is EmbedmentZDataItem item)
                {
                    const double eps = 1e-5;
                    var matched = vm.EmbedmentZsCollection?
                        .FirstOrDefault(z => Math.Abs(z.Z - item.Z) <= eps);
                    vm.SelectedEmbedmentZ = matched ?? item;
                }
            }
        }

        // DataGrid選択解除用のPreviewMouseDownハンドラ（選択行を再クリックで解除）
        private void DataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
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
        // Run などの Visual でない要素も安全に扱えるよう LogicalTree フォールバックを併用
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                if (child is System.Windows.Media.Visual || child is System.Windows.Media.Media3D.Visual3D)
                {
                    child = VisualTreeHelper.GetParent(child);
                }
                else
                {
                    child = LogicalTreeHelper.GetParent(child);
                }
            }
            return null;
        }

        private PileDesign.Common.PaneFloatMaximizer? _paneMaximizer;

        private void InitializePaneMaximizer()
        {
            _paneMaximizer = new PileDesign.Common.PaneFloatMaximizer(dockingManager);
            _paneMaximizer.Register("Youso", YousoTab, ButtonMaximizeYouso, "杭要素分割");
            _paneMaximizer.Register("Shape", ShapeTab, ButtonMaximizeShape, "杭姿図");
            _paneMaximizer.Register("Kh",    KhTab,    ButtonMaximizeKh,    "水平地盤反力");
            _paneMaximizer.Register("Graph", GraphTab, ButtonMaximizeGraph, "グラフ");
        }
    }
}