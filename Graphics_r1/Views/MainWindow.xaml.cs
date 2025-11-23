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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace PileDesign.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
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
        private readonly double acturalTickPointSize = 2.0;
        private readonly double acturalNodeSize = 3.0;

        private Point previousMousePosition;
        private bool IsMouseWheelPressed = false;
        private Point startPoint = new(0, 0);
        private Point endPoint = new(0, 0);
        private Rectangle selectionRectangle;

        private bool hasViewportAxes = true;
        private bool hasViewportGrid = true;

        private const double SelectionTolerance = 10.0;

        public double Canvas3DHeight { get; set; }
        public double Canvas3DWidth { get; set; }
        // ツリーメニュー
        public ObservableCollection<CTreeViewData> CTreeViewDatas { get; set; } = [];

        public CanvasThreeDView CanvasThreeDViewModel { get; set; }

        private OptionWindow _optionWindow;

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

            // （任意）アニメーション角度用も接続したい場合
            viewModel.AnimateViewAnglesAction = async (tht, phi) =>
            {
                await AnimateToAnglesAsync(tht, phi);
            };


            // TextBoxElementNodeInput を ViewModel にバインド
            _mainWindowViewModel.TextBoxElementNodeInput = TextBoxElementNodeInput;

            InitializeViewModels();
            SetupEventHandlers();
            UpdatePerspectiveView();

            var loadingMainWindow = new LoadingMainWindow();
            loadingMainWindow.ShowDialog();

            Loaded += MainWindow_Loaded;

            viewModel.TreeViewControl = TreeViewControl;
            // ViewModelのActionにUpdateCanvas3Dを設定
            CanvasThreeDViewModel = viewModel.CanvasThreeDView;
            CanvasThreeDViewModel.UpdateCanvas3DAction = UpdateCanvas3D;

            // ViewModelのActionにを設定
            viewModel.UpdateWindowAction = UpdateWindow;

            // デリゲートの設定
            viewModel.UpdateCanvas3DAction = UpdateCanvas3D;

            // データグリッドの選択変更イベントを設定
            DataGridPileLayout.SelectionChanged += DataGridPileLayout_SelectionChanged;
            DataGridPileAxialForce.SelectionChanged += DataGridPileAxialForce_SelectionChanged;
            DataGridIsFrontPile.SelectionChanged += DataGridIsFrontPile_SelectionChanged;


            // inputDataAnchorable を ViewModel に渡す
            viewModel.InputDataAnchorable = inputDataAnchorable;

            viewModel.TextBoxElementNodeInput = TextBoxElementNodeInput;
            // Window の KeyDown イベントを設定
            this.KeyDown += MainWindow_KeyDown;

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
            var viewModel = _mainWindowViewModel;

            //// 重心の更新
            //UpdateGravityCenters();

            //// 合計荷重の更新
            //UpdateSumLoads();

            //// OTMの更新
            //UpdateOverturningMoment();

            // ツリービューの更新
            viewModel.UpdateTreeView();
        }


        //// 重心の更新メソッド
        //private void UpdateGravityCenters()
        //{
        //    var viewModel = _mainWindowViewModel;
        //    viewModel.GravityCenterVL0 = viewModel.CurrentInputModel.GetVLGravityCenter();
        //    viewModel.GravityCenterVLadd = viewModel.CurrentInputModel.GetVLaddGravityCenter();
        //    viewModel.GravityCenterVLplusVLadd = viewModel.CurrentInputModel.GetVLplusVLaddGravityCenter();
        //}

        //// 合計荷重の更新メソッド
        //private void UpdateSumLoads()
        //{
        //    var viewModel = _mainWindowViewModel;
        //    viewModel.SumVL0 = viewModel.CurrentInputModel.GetSumVL();
        //    viewModel.SumVLadd = viewModel.CurrentInputModel.GetSumVLadd();
        //    viewModel.SumVL = viewModel.CurrentInputModel.GetSumVLplusVLadd();
        //}


        //// OTMの更新メソッド
        //private void UpdateOverturningMoment()
        //{
        //    var viewModel = _mainWindowViewModel;
        //    viewModel.Sum1_1 = viewModel.CurrentInputModel.GetSum(1, 0);
        //    viewModel.Sum1_2 = viewModel.CurrentInputModel.GetSum(1, 1);
        //    viewModel.Sum1_3 = viewModel.CurrentInputModel.GetSum(1, 2);
        //    viewModel.Sum1_4 = viewModel.CurrentInputModel.GetSum(1, 3);

        //    viewModel.Sum2_1 = viewModel.CurrentInputModel.GetSum(2, 0);
        //    viewModel.Sum2_2 = viewModel.CurrentInputModel.GetSum(2, 1);
        //    viewModel.Sum2_3 = viewModel.CurrentInputModel.GetSum(2, 2);
        //    viewModel.Sum2_4 = viewModel.CurrentInputModel.GetSum(2, 3);

        //    (viewModel.OverturningMoment1_1X, viewModel.OverturningMoment1_1Y) = viewModel.CurrentInputModel.GetOverturningMoment(1, 0);
        //    (viewModel.OverturningMoment1_2X, viewModel.OverturningMoment1_2Y) = viewModel.CurrentInputModel.GetOverturningMoment(1, 1);
        //    (viewModel.OverturningMoment1_3X, viewModel.OverturningMoment1_3Y) = viewModel.CurrentInputModel.GetOverturningMoment(1, 2);
        //    (viewModel.OverturningMoment1_4X, viewModel.OverturningMoment1_4Y) = viewModel.CurrentInputModel.GetOverturningMoment(1, 3);

        //    (viewModel.OverturningMoment2_1X, viewModel.OverturningMoment2_1Y) = viewModel.CurrentInputModel.GetOverturningMoment(2, 0);
        //    (viewModel.OverturningMoment2_2X, viewModel.OverturningMoment2_2Y) = viewModel.CurrentInputModel.GetOverturningMoment(2, 1);
        //    (viewModel.OverturningMoment2_3X, viewModel.OverturningMoment2_3Y) = viewModel.CurrentInputModel.GetOverturningMoment(2, 2);
        //    (viewModel.OverturningMoment2_4X, viewModel.OverturningMoment2_4Y) = viewModel.CurrentInputModel.GetOverturningMoment(2, 3);
        //}


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //var proxyElement = (FrameworkElement)Resources["ProxyElement"];
            //proxyElement.DataContext = this.DataContext;

            var layoutAnchorable = inputDataAnchorable;
            layoutAnchorable?.ToggleAutoHide();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // MainWindowが閉じられたときにOptionWindowも閉じる
            _optionWindow?.Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCanvasTransformGroup();
            SetupDataBindings();
            var viewModel = _mainWindowViewModel;
            viewModel.UpdateTreeView();

            // Canvas にフォーカスを設定
            Canvas3DLayout.Focus();

            // SizeChanged イベントを登録
            Canvas3DLayout.SizeChanged += ColorBarCanvas_SizeChanged;
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


        private void DataGridEmbedment_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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
            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridEmbedment_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridSoilPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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

                // ここで根入部形状表示を有効化（値が実際に変わったときのみ）
                if (DataContext is MainWindowViewModel vm2)
                    vm2.IsEmbedmentBoxVisible = true;

            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridSoilPile_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridRectLoads_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridRectLoads_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridSettlementSoilLayers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (e.Column is DataGridTextColumn textColumn)
            {
                // Header が StackPanel である場合、その中の TextBlock の内容を確認
                if (textColumn.Header is StackPanel headerPanel)
                {
                    foreach (var child in headerPanel.Children)
                    {
                        if (child is TextBlock textBlock && textBlock.Text.Contains("下端Z"))
                        {
                            // 下端標高列のセルが編集された場合の処理
                            var dataGrid = sender as DataGrid;
                            var editedItem = e.Row.Item as SettlementSoilLayer;
                            var editedTextBox = e.EditingElement as TextBox;

                            if (double.TryParse(editedTextBox.Text, out double newValue))
                            {
                                int rowIndex = dataGrid.Items.IndexOf(editedItem);
                                if (rowIndex > 0)
                                {
                                    var previousItem = dataGrid.Items[rowIndex - 1] as SettlementSoilLayer; // SettlementSoilLayer は適切なモデルクラスに置き換えてください
                                    if (newValue >= previousItem.BottomAltitude)
                                    {
                                        MessageBox.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                                        e.Cancel = true;

                                        // 編集内容を元に戻す
                                        editedTextBox.Text = editedItem.BottomAltitude.ToString("F2");
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }

            // 厚さ列の再計算
            RecalculateThickness();

            var viewModel = DataContext as MainWindowViewModel;
            viewModel.IsGroupPileSettlementAnalysisDone = false;
        }

        private void RecalculateThickness()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var settlementSoilLayers = viewModel.CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            if (settlementSoilLayers == null || settlementSoilLayers.Count == 0) return;

            double loadSurfaceAltitude = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                {
                    // 1行目の厚さは荷重面標高から下端標高の深さ
                    settlementSoilLayers[i].Thickness = loadSurfaceAltitude - settlementSoilLayers[i].BottomAltitude;
                }
                else
                {
                    // 2行目以降の厚さは直上の下端標高からその行の下端標高の深さ
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude;
                }
            }
        }

        private void DataGridPileAxialForce_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (sender is not DataGrid dataGrid || dataGrid.SelectedCells.Count == 0) return;

            // 最初の選択セルの列を取得
            var column = e.Column;
            if (column == null) return;

            // 列ヘッダーやバインディング名で判定
            string header = "";
            if (column.Header is StackPanel headerPanel)
            {
                foreach (var child in headerPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        header += textBlock.Text; // 必要に応じて区切り文字を追加
                    }
                }
            }
            else
            {
                header = column.Header?.ToString() ?? "";
            }
            string bindingPath = "";
            if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                bindingPath = binding.Path.Path;
            }

            var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;
            if (header.Contains("VL0") || bindingPath.Contains("AxialForceVL0"))
                vm.SelectedLoadCaseName = "VL0";
            else if (header.Contains("VLadd") || bindingPath.Contains("AxialForceVLAdditional"))
                vm.SelectedLoadCaseName = "VLadd";
            else if (header.Contains("1-1") || bindingPath.Contains("AxialForceLevel1s[0]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[0].LoadName;
            else if (header.Contains("1-2") || bindingPath.Contains("AxialForceLevel1s[1]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[1].LoadName;
            else if (header.Contains("1-3") || bindingPath.Contains("AxialForceLevel1s[2]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[2].LoadName;
            else if (header.Contains("1-4") || bindingPath.Contains("AxialForceLevel1s[3]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel1[3].LoadName;
            else if (header.Contains("2-1") || bindingPath.Contains("AxialForceLevel2s[0]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[0].LoadName;
            else if (header.Contains("1-2") || bindingPath.Contains("AxialForceLevel2s[1]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[1].LoadName;
            else if (header.Contains("1-3") || bindingPath.Contains("AxialForceLevel2s[2]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[2].LoadName;
            else if (header.Contains("1-4") || bindingPath.Contains("AxialForceLevel2s[3]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[3].LoadName;

            //vm.IsAxialForceLabelVisible = true;
            //vm.IsLoadingVisible = true;
            vm.IsMassLoadingVisible = true;
            vm.IsAxialLoadingVisible = true;

            vm?.DataGridPileAxialForce_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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
            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridPileLayout_OnCellEditEndingCommand.Execute(e);
        }


        // 汎用: 列のBindingパス取得（Text/Combo/CheckBox列対応の簡易版）
        private static string? GetBindingPath(System.Windows.Controls.DataGridColumn column)
        {
            return column switch
            {
                System.Windows.Controls.DataGridTextColumn tc => (tc.Binding as System.Windows.Data.Binding)?.Path.Path,
                System.Windows.Controls.DataGridCheckBoxColumn cc => (cc.Binding as System.Windows.Data.Binding)?.Path.Path,
                System.Windows.Controls.DataGridComboBoxColumn cb => (cb.SelectedItemBinding as System.Windows.Data.Binding)?.Path.Path
                                        ?? (cb.SelectedValueBinding as System.Windows.Data.Binding)?.Path.Path,
                _ => null,
            };
        }

        // 反射でプロパティ値を取る（"A.B[0].C" 等の配列/インデクサは簡易対応：配列/リストは未展開）
        private static (bool ok, object? value) TryGetPropertyValue(object target, string path)
        {
            try
            {
                object? cur = target;
                foreach (var seg in path.Split('.'))
                {
                    if (cur == null) return (false, null);
                    var pi = cur.GetType().GetProperty(seg, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pi == null) return (false, null);
                    cur = pi.GetValue(cur);
                }
                return (true, cur);
            }
            catch { return (false, null); }
        }


        // 前後配置データグリッドのセル編集が終了したときのイベントハンドラ
        private void DataGridIsFrontPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (sender is not DataGrid dataGrid || dataGrid.SelectedCells.Count == 0) return;

            // 最初の選択セルの列を取得
            var column = e.Column;
            if (column == null) return;

            // 列ヘッダーやバインディング名で判定
            string header = "";
            if (column.Header is StackPanel headerPanel)
            {
                foreach (var child in headerPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        header += textBlock.Text; // 必要に応じて区切り文字を追加
                    }
                }
            }
            else
            {
                header = column.Header?.ToString() ?? "";
            }
            string bindingPath = "";
            if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                bindingPath = binding.Path.Path;
            }

            var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;

            if (header.Contains("方向1"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[0].LoadName;
            else if (header.Contains("方向2"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[1].LoadName;
            else if (header.Contains("方向3"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[2].LoadName;
            else if (header.Contains("方向4"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[3].LoadName;

            vm.IsFrontPileLabelVisible = true;
            //var viewModel = DataContext as MainWindowViewModel;
            vm?.DataGridIsFrontPile_OnCellEditEndingCommand.Execute(e);
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

        //private void ComboBoxLabelContent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
        //    {
        //        var viewModel = DataContext as MainWindowViewModel;
        //        viewModel.ComboBox3DLabelContent_LabelContent = selectedItem.Content.ToString();
        //    }
        //}


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


        // 角度指定でビューをアニメーション切替（角度差に応じて所要時間を調整）
        private async Task AnimateToAnglesAsync(double targetTht, double targetPhi)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            // 前回アニメをキャンセル
            _viewAnimationCts?.Cancel();
            _viewAnimationCts = new System.Threading.CancellationTokenSource();

            // 回転量からアニメ時間を決定（小回転は速く、大回転は少し長め）
            double angDelta =
                Math.Max(Math.Abs(DeltaAngle(vm.CanvasThreeDView.Tht, targetTht)),
                         Math.Abs(targetPhi - vm.CanvasThreeDView.Phi));
            int duration = (int)Math.Clamp(150 + angDelta * 4.0, 220, 700);

            await AnimateViewToAsync(targetTht, Math.Clamp(targetPhi, -89.9, 89.9), duration, _viewAnimationCts.Token);
        }

        // XY平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonXYPlane_Clicked(object sender, RoutedEventArgs e)
        {
            await AnimateToAnglesAsync(-90, 90);
        }

        // YZ平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonYZPlane_Clicked(object sender, RoutedEventArgs e)
        {
            await AnimateToAnglesAsync(0, 0);
        }

        // XZ平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonXZPlane_Clicked(object sender, RoutedEventArgs e)
        {
            await AnimateToAnglesAsync(-90, 0);
        }

        // 3D（アイソメ）モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonIsometric_Clicked(object sender, RoutedEventArgs e)
        {
            await AnimateToAnglesAsync(-45, 45);
        }

        //// XY平面モードに切り替えるボタンがクリックされた時のメソッド
        //private void ButtonXYPlane_Clicked(object sender, RoutedEventArgs e)
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    viewModel.CanvasThreeDView.Tht = -90;
        //    viewModel.CanvasThreeDView.Phi = 90;
        //    UpdateCanvas3D();
        //}

        //// YZ平面モードに切り替えるボタンがクリックされた時のメソッド
        //private void ButtonYZPlane_Clicked(object sender, RoutedEventArgs e)
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    viewModel.CanvasThreeDView.Tht = 0;
        //    viewModel.CanvasThreeDView.Phi = 0;

        //    UpdateCanvas3D();
        //}

        //// XZ平面モードに切り替えるボタンがクリックされた時のメソッド
        //private void ButtonXZPlane_Clicked(object sender, RoutedEventArgs e)
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    viewModel.CanvasThreeDView.Tht = -90;
        //    viewModel.CanvasThreeDView.Phi = 0;
        //    UpdateCanvas3D();
        //}

        //// 3D平面モードに切り替えるボタンがクリックされた時のメソッド
        //private void ButtonIsometric_Clicked(object sender, RoutedEventArgs e)
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    viewModel.CanvasThreeDView.Tht = -45;
        //    viewModel.CanvasThreeDView.Phi = 45;
        //    UpdateCanvas3D();
        //}


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

        private int? elementAddStartNodeIndex = null;
        private int? elementAddEndNodeIndex = null;

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

            if (_mainWindowViewModel.IsElementAddMode)
            {
                var pos = e.GetPosition(Canvas3DLayout);
                int? nodeIndex = FindNearestNodeIndex(pos);
                if (nodeIndex != null)
                {
                    elementAddStartNodeIndex = nodeIndex;
                }
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
                if (SelectNode3DIfNearby(startPoint, true))
                { return; }
            }
            // Shiftキーが押されていない場合の処理
            else
            {
                ClearCanvasSelection();

                // クリック位置の周辺に節点があるかチェック
                if (SelectNode3DIfNearby(startPoint, false))
                { return; }

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
                Canvas3DLayout.Children.Add(selectionRectangle);
            }
        }

        private const double DragThreshold = 5.0; // ドラッグとみなす移動距離の閾値

        // マウス左ボタンが離された時のメソッド
        private void Canvas3DLayout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスキャプチャを解除
            Canvas3DLayout.ReleaseMouseCapture();

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

        // 
        private void RemoveEditingElement()
        {
            var elementsToRemove = Canvas3DLayout.Children.OfType<Path>()
                .Where(p => p.Name == "EditingElement")
                .ToList();

            foreach (var element in elementsToRemove)
            {
                Canvas3DLayout.Children.Remove(element);
            }
        }
        // 編集中要素の更新
        private void UpdateEditingElement3D(MouseEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            List<int> nodeNos = GetTextBoxElementNodeNos();

            // 既存の EditingElement を削除
            RemoveEditingElement();

            // PathGeometry を作成
            PathGeometry pathGeometry = new();
            PathFigure pathFigure = new() { StartPoint = e.GetPosition(Canvas3DLayout) };
            // ポリラインのセグメントを追加
            PolyLineSegment polyLineSegment = new();

            foreach (int nodeNo in nodeNos)
            {
                Point3D point3D = new(
                    viewModel.CurrentInputModel.PileLayoutItems[nodeNo - 1].Point3D.X,
                    viewModel.CurrentInputModel.PileLayoutItems[nodeNo - 1].Point3D.Y,
                    viewModel.CurrentInputModel.PileLayoutItems[nodeNo - 1].Point3D.Z);
                Point coord = viewModel.CanvasThreeDView.Transformation(point3D);
                polyLineSegment.Points.Add(coord);
            }

            pathFigure.Segments.Add(polyLineSegment);
            pathGeometry.Figures.Add(pathFigure);

            // Path を作成し、PathGeometry を設定
            Path path = new()
            {
                Data = pathGeometry,
                Stroke = Brushes.Pink,
                StrokeThickness = 2,
                StrokeDashArray = [4, 2],
                Name = "EditingElement"
            };

            Canvas3DLayout.Children.Add(path);
        }

        // 追加フィールド（クラス内に追加）
        private Point _rightDragAnchorPoint;
        private double _anchorTht;
        private double _anchorPhi;
        private bool _isRotatingView = false;
        private bool _isRenderingHooked = false;
        private Point _latestMousePos;
        // 回転感度（px -> degree）
        private const double RotateDegPerPixelX = 0.50; // 横移動: θ
        private const double RotateDegPerPixelY = 0.50; // 縦移動: φ

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

            // φの過回転を軽く制限（必要に応じて調整）
            newPhi = Math.Max(-89.9, Math.Min(89.9, newPhi));

            // 右ドラッグ中は軽量描画
            isLightweightDrawing = true;
            try
            {
                // セッター側で再描画が走らない場合も明示更新
                viewModel.CanvasThreeDView.Tht = newTht;
                viewModel.CanvasThreeDView.Phi = newPhi;
                UpdateCanvas3D();
            }
            finally
            {
                isLightweightDrawing = false;
            }
        }

        private const double RotationThreshold = 0.5; //1ピクセルごとに回転
        private const double RotationAngle = 5.0; // 1度回転

        private DateTime lastUpdate = DateTime.Now;
        private readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(50); // 更新間隔10ミリ秒

        private bool _rightDragged = false;
        private Point _rightDownPoint;
        private const double RightClickDragThreshold = 6.0; // px: 右クリックとドラッグの判定閾値

        // マウス移動
        private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

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

                        // 回転開始時にフック・軽量描画ON
                        HookRendering();
                        isLightweightDrawing = true;
                    }
                }
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

            if (viewModel.IsElementAddMode)
            {
                UpdateEditingElement3D(e);
            }

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

        // マウスホイールイベント マウスホイールが押された時の処理
        private void Canvas3DLayout_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                IsMouseWheelPressed = true;
            }

            IsRightButtonClicked = false;

            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            // マウスポインタの位置を取得
            Point mousePosition = e.GetPosition(Canvas3DLayout);

            // 現在のスケールを取得
            double scale = viewModel.CanvasThreeDView.Scale;

            // 拡大・縮小の倍率を計算
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;

            // 新しいスケールを計算
            double newScale = scale * zoomFactor;

            // ズームの最小値と最大値を設定
            newScale = Math.Max(0.1, Math.Min(newScale, 100));

            // 注視点位置を取得
            Point originalFocalPoint = viewModel.CanvasThreeDView.Transformation(viewModel.CanvasThreeDView.Ct);

            // 新しいスケールでの注視点位置を計算
            Point newFocalPoint = new(
                (originalFocalPoint.X - mousePosition.X) * zoomFactor + mousePosition.X,
                (originalFocalPoint.Y - mousePosition.Y) * zoomFactor + mousePosition.Y
            );

            // マウスポインタの位置を中心にビューを調整
            Point originalViewPosition = viewModel.CanvasThreeDView.ViewTransition;
            Point newViewPosition = new(
                originalViewPosition.X + (newFocalPoint.X - originalFocalPoint.X),
                originalViewPosition.Y + (newFocalPoint.Y - originalFocalPoint.Y)
            );

            // スケールとビュー位置を更新
            viewModel.CanvasThreeDView.Scale = newScale;
            viewModel.CanvasThreeDView.ViewTransition = newViewPosition;

            IsMouseWheelPressed = false;
            UpdateCanvas3D();
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
                if (FindResource("NodeContextMenu") is ContextMenu contextMenu)
                {
                    contextMenu.PlacementTarget = sender as UIElement;
                    contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    contextMenu.IsOpen = true;
                }
                else
                {
                    Console.WriteLine("ContextMenu is null");
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

        // キーを押したときの処理
        private void HandleKeyDown(KeyEventArgs e)
        {
            Console.WriteLine($"Key pressed: {e.Key}, Modifiers: {Keyboard.Modifiers}");
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Ctrl + Shift + A が押されたときの処理
                SelectAllNodes();
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
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl + A が押されたときの処理
                ShowAllNodes();
                e.Handled = true;
            }
            else if (
                ((e.Key == Key.D1 || e.Key == Key.NumPad1) && Keyboard.Modifiers == ModifierKeys.Alt)
                || (e.Key == Key.System && e.SystemKey == Key.D1 && Keyboard.Modifiers == ModifierKeys.Alt)
            )
            {
                // Alt + 1が押されたときの処理
                // 要素追加モード
                MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
                if (viewModel.IsElementAddMode)
                {
                    viewModel.IsElementAddMode = false;
                    RemoveEditingElement();
                }
                else
                {
                    viewModel.IsElementAddMode = true;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = (MainWindow)Application.Current.MainWindow;
                        mainWindow.ElementDocument.IsSelected = true;
                    });

                }
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
                // Deleteが押されたときの処理
                DeleteSelectedPileLayouts();
                DeleteSelectedElements();

                UpdateWindow();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Escapeが押されたときの処理
                ClearCanvasSelection();
                var viewModel = DataContext as MainWindowViewModel;
                viewModel.IsElementAddMode = false;
                RemoveEditingElement();
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

            else if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl + F7が押されたときの処理
                // 解析後処理モード
                var viewModel = DataContext as MainWindowViewModel;
                viewModel.IsPostAnalysisMode = true;
                e.Handled = true;
            }

            else if (e.Key == Key.F7)
            {
                // F7が押されたときの処理
                // 解析前処理モード
                var viewModel = DataContext as MainWindowViewModel;
                viewModel.IsPostAnalysisMode = false;
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
            UpdateWindow();
        }

        // 選択されたノードを表示するメソッド
        private void ShowSelectedNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;

            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (pileLayoutItem.IsSelected)
                {
                    pileLayoutItem.IsVisible = true;
                }
                else
                {
                    pileLayoutItem.IsVisible = false;
                }
            }
            UpdateWindow();
        }

        // 選択されていないノードを表示するメソッド
        private void ShowUnselectedNodes()
        {
            var viewModel = DataContext as MainWindowViewModel;
            foreach (var pileLayoutItem in viewModel.CurrentInputModel.PileLayoutItems)
            {
                if (pileLayoutItem.IsSelected)
                {
                    pileLayoutItem.IsVisible = false;
                }
                else
                {
                    pileLayoutItem.IsVisible = true;
                }
            }
            //ClearCanvasSelection();
            UpdateWindow();
        }


        // 選択された杭配置データを削除するメソッド
        private void DeleteSelectedPileLayouts()
        //{
        //    var viewModel = _mainWindowViewModel;
        //    InputModel InputModel = viewModel.CurrentInputModel;

        //    // 削除するアイテムのリストを作成
        //    var itemsToRemove = viewModel.CurrentInputModel.PileLayoutItems
        //        .Where(pileLayoutItem => pileLayoutItem.IsSelected)
        //        .ToList();

        //    // リストを列挙してアイテムを削除
        //    foreach (var pileLayoutItem in itemsToRemove)
        //    {
        //        InputModel.PileLayoutItems.Remove(pileLayoutItem);
        //    }
        //}
        {
            var vm = _mainWindowViewModel;
            var col = vm.CurrentInputModel.PileLayoutItems;

            var itemsToRemove = col.Where(x => x.IsSelected).ToList();
            if (itemsToRemove.Count == 0) return;

            // まとめて1ステップに
            var scope = new PileDesign.Common.Undo.CompositeUndoAction("Delete piles");
            foreach (var item in itemsToRemove)
            {
                int index = col.IndexOf(item);
                if (index < 0) continue;
                scope.Add(
                    PileDesign.Common.Undo.CollectionChangeAction<PileLayoutDataItem>
                        .ForRemove(col, item, index)
                );
            }
            UndoService.Instance.Push(scope);

            // 実削除
            foreach (var item in itemsToRemove)
                col.Remove(item);

            vm.UpdatePileLayoutNo();
        }

        // 選択された要素を削除するメソッド
        private void DeleteSelectedElements()
        {
            var viewModel = _mainWindowViewModel;
            InputModel InputModel = viewModel.CurrentInputModel;

            // 削除するアイテムのリストを作成
            var itemsToRemove = viewModel.CurrentInputModel.Elements
                .Where(element => element.IsSelected)
                .ToList();

            // リストを列挙してアイテムを削除
            foreach (var element in itemsToRemove)
            {
                InputModel.Elements.Remove(element);
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
            var selectedItem = comboBox?.SelectedItem;

            // 既存データがある場合のみ確認
            var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;
            if (vm?.CurrentInputModel?.PileGroupSettlement?.RectLoads?.Count > 0)
            {
                // 必要なら確認ダイアログを表示
                // var result = MessageBox.Show(...);
                // if (result != MessageBoxResult.Yes) return;
            }

            // 群杭表示
            vm.IsSettlementGroundVisible = true;

            // 変更を確定し、前回値を更新
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;

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

            if (DataGridPileLayout.SelectedItem is PileLayoutDataItem selectedItem)
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
                MessageBox.Show("選択されたアイテムの型が正しくありません。");
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
        //            MessageBox.Show("選択されたアイテムの型が正しくありません。");
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

            // 杭体編集
            else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenPileBodyWindow();
                e.Handled = true;
            }
            // 要素分割
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel?.OpenElementDivisionWindow();
                e.Handled = true;
            }

            // 水平解析
            else if (e.Key == Key.F5)
            {
                viewModel?.OpenLateralLoadAnalysisWindow();
                e.Handled = true;
            }

            // 単杭沈下
            else if (e.Key == Key.F6)
            {
                viewModel?.OpenSettlementWindow();
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


        private void TextBoxElementNodeInput_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBoxElementNodeInput = sender as TextBox;
        }

        private void TextBoxElementNodeInput_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBoxElementNodeInput.Text = string.Empty;
            TextBoxElementNodeInput = null;
        }

        private void TextBoxElementNodeInput_Changed(object sender, TextChangedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            var inputModel = viewModel.CurrentInputModel;
            if (viewModel.ElementType == "ダミー")
            {
                if (TextBoxElementNodeInput.Text.Count(c => c == ',') == 2)
                {
                    var parts = TextBoxElementNodeInput.Text.Split(',');
                    if (parts.Length == 3)
                    {
                        int.TryParse(parts[0], out int value1);
                        int.TryParse(parts[1], out int value2);

                        inputModel.Elements.Add(new Element(viewModel.ElementType,
                            inputModel.PileLayoutItems[value1 - 1], inputModel.PileLayoutItems[value2 - 1]));

                        TextBoxElementNodeInput.Text = string.Empty;

                        ClearCanvasSelection();
                        //UpdateCanvas3D();
                    }
                }
            }
        }

        // TextBoxElementNodeInputに入力されたnodeNoを返すメソッド
        private List<int> GetTextBoxElementNodeNos()
        {
            // nullチェックを追加
            if (TextBoxElementNodeInput == null || string.IsNullOrEmpty(TextBoxElementNodeInput.Text))
                return [];

            var parts = TextBoxElementNodeInput.Text.Split(',');
            List<int> nodeNos = [];
            for (int i = 0; i < TextBoxElementNodeInput.Text.Count(c => c == ','); i++)
            {
                int.TryParse(parts[i], out int nodeNo);
                nodeNos.Add(nodeNo);
            }
            return nodeNos;
        }

        private void LayoutDocumentElement_IsActiveChanged(object sender, EventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel.IsElementAddMode = false;
        }

        private void LayoutDocumentElement_IsSelectedChanged(object sender, EventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel.IsElementAddMode = false;
        }

        private void SelectAllNodesButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAllNodes();
        }

        private void InvertActiveNodesButton_click(object sender, RoutedEventArgs e)
        {
            //foreach (var pileLayoutItem in ((MainWindowViewModel)DataContext).CurrentInputModel.PileLayoutItems)
            //{
            //    if (pileLayoutItem.IsVisible)
            //    { pileLayoutItem.IsVisible = false; }
            //    else
            //    {
            //        pileLayoutItem.IsVisible = true;
            //    }
            //}
            var vm = (MainWindowViewModel)DataContext;
            foreach (var item in vm.CurrentInputModel.PileLayoutItems)
                item.IsVisible = !item.IsVisible;

            vm.UpdateViewCommand?.Execute(null); // 既存の再描画コマンドがあれば
        }

        private void MergeElementsButton_click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel.DeleteDuplicatedElements();
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

        private void ToggleButtonElementAddMode_Unchecked(object sender, RoutedEventArgs e)
        {
            TextBoxElementNodeInput.Text = string.Empty;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCanvas3D();
        }

        private void ButtonGroupPileSettlement_Click(object sender, RoutedEventArgs e)
        {
            ActivateGroupPileLoadTab();
        }

        private void ActivateGroupPileLoadTab()
        {
            // "群杭荷重"タブを探してアクティブ化
            foreach (var doc in dockingManager.Layout.Descendents().OfType<LayoutDocument>())
            {
                if (doc.Title == "群杭荷重")
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
                }
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
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void EmbedmentAdjustButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsEmbedmentBoxVisible = true;
            }
        }

        private void QuickHintToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isViewInteracting) return; // ビュー操作中なら無視
            var vm = DataContext as MainWindowViewModel;
            if (vm != null) vm.IsQuickHintVisible = true;
        }
        private void QuickHintToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm != null) vm.IsQuickHintVisible = false;
        }
    }
}

