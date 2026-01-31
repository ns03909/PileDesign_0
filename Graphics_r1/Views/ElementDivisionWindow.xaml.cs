using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// ElementDivisionWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class ElementDivisionWindow : Window
    {
        private readonly ElementDivisionViewModel _viewModel;
        private bool _isLoaded = false; // フラグを追加
        private bool _isClosingHandled = false;
        //// コンストラクタ
        //public ElementDivisionWindow()
        //{
        //    InitializeComponent();

        //    _viewModel = new ElementDivisionViewModel();
        //    DataContext = _viewModel;

        //    Loaded += ElementDivisionWindow_Loaded;
        //}
        // MainWindowViewModelを受け取るコンストラクタに変更
        public ElementDivisionWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();

            _viewModel = new ElementDivisionViewModel(mainWindowViewModel);
            DataContext = _viewModel;

            Loaded += ElementDivisionWindow_Loaded;
        }

        private void ElementDivisionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return; // 既にロード済みなら処理をスキップ

            var viewModel = (ElementDivisionViewModel)DataContext;
            viewModel.ElementDivisionWindowInstance = this;
            viewModel.Canvas = Canvas;
            viewModel.CanvasEmbedment = CanvasEmbedment;

            // DataGridのSelectionUnitを明示的にFullRowに設定
            // EnhancedDataGridのコンストラクタでCellOrRowHeaderが設定されるため、ここで上書きする
            DataGridZs.SelectionUnit = DataGridSelectionUnit.FullRow;
            DataGridZs.SelectionMode = DataGridSelectionMode.Single;

            // 初期化メソッドを呼び出す
            viewModel.Initialize();

            // CanvasのSizeChangedイベントにハンドラを追加
            Canvas.SizeChanged += Canvas_SizeChanged;
            CanvasEmbedment.SizeChanged += CanvasEmbedment_SizeChanged;

            // DoatsuGoryokuBaneの表示/非表示を設定
            //DoatsuGoryokuBaneAnchorable.IsVisible = viewModel.IsDoatsuGoryokuBaneVisible;

            // DoatsuGoryokuBaneの変更を監視して表示/非表示を更新
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(viewModel.IsDoatsuGoryokuBaneVisible))
                {
                    //DoatsuGoryokuBaneAnchorable.IsVisible = viewModel.IsDoatsuGoryokuBaneVisible;
                }
            };

            _isLoaded = true; // フラグを設定

            // RequestCloseイベントをハンドル
            _viewModel.RequestClose += (s, e) =>
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

        private void CanvasEmbedment_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var viewModel = (ElementDivisionViewModel)DataContext;
            viewModel.DrawSoilEmbedment();
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var viewModel = (ElementDivisionViewModel)DataContext;
            viewModel.DrawShapes();
        }

        private void DataGridZs_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var viewModel = DataContext as ElementDivisionViewModel;
            viewModel.OnDataGridZsBeginningEdit(e);
        }

        private void DataGridZs_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as ElementDivisionViewModel;
            viewModel.OnDataGridZsCellEditEnding(sender, e);
        }

        private void ButtonResetEmbedment_click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as ElementDivisionViewModel;
            viewModel.ResetEmbedment();
        }

        private void ComboBoxSoilPile_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 現在の選択状態を保存
            if (DataContext is ElementDivisionViewModel viewModel && viewModel.SelectedZDataItems != null)
            {
                var previousSoilPile = viewModel.SoilPiles[viewModel.SelectedSoilPileNo - 1];
                previousSoilPile.ZDataItems = new ObservableCollection<PileZDataItem>(
                    viewModel.SelectedZDataItems.Select(item => item.DeepCopy())
                );
            }
        }

        private void ComboBoxSoilPile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as ElementDivisionViewModel;
            if (e.RemovedItems.Count > 0 && e.AddedItems.Count > 0)
            {
                var previousValue = e.RemovedItems[0] as int?;
                var newValue = e.AddedItems[0] as int?;

                if (previousValue.HasValue && newValue.HasValue)
                {
                    // ViewModelのメソッドを呼び出す
                    viewModel.OnComboBoxSoilPileSelectionChanged(e);
                }
            }
        }

        private void DataGridEmbedmentZs_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel viewModel)
            {
                viewModel.OnDataGridEmbedmentZsChanged();
            }

            // すべての行の番号を振り直す
            foreach (var item in DataGridEmbedmentZs.Items)
            {
                var row = (DataGridRow)DataGridEmbedmentZs.ItemContainerGenerator.ContainerFromItem(item);
                if (row != null)
                {
                    row.Header = (row.GetIndex() + 1).ToString();
                }
            }
        }

        private void DataGridZs_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel viewModel)
            {
                viewModel.OnDataGridZsChanged();
            }

            // すべての行の番号を振り直す
            foreach (var item in DataGridZs.Items)
            {
                var row = (DataGridRow)DataGridZs.ItemContainerGenerator.ContainerFromItem(item);
                if (row != null)
                {
                    row.Header = (row.GetIndex() + 1).ToString();
                }
            }
        }

        private void XiROnB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel viewModel)
            {
                viewModel.OnXiROnChanged();
            }
        }

        // 行番号を設定するメソッド
        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        // タブ切り替え時のメソッド
        private void TabControl_SelectionChange(object sender, SelectionChangedEventArgs e)
        {
            // ViewModel取得
            if (DataContext is not ElementDivisionViewModel vm) return;

            // TabControlのインスタンス取得
            if (sender is not TabControl tabControl) return;

            // "土層-根入れセット"タブが選択された場合のみ描画
            if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header?.ToString()?.Contains("根入れ") == true)
            {
                // CanvasEmbedmentのサイズが0でなければ描画
                if (CanvasEmbedment.ActualWidth > 0 && CanvasEmbedment.ActualHeight > 0)
                {
                    vm.DrawSoilEmbedment();
                }
                else
                {
                    // サイズが0の場合はLoadedイベントで描画
                    CanvasEmbedment.Loaded -= CanvasEmbedment_Loaded;
                    CanvasEmbedment.Loaded += CanvasEmbedment_Loaded;
                }
            }
        }

        private void CanvasEmbedment_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm)
            {
                vm.DrawSoilEmbedment();
            }
            CanvasEmbedment.Loaded -= CanvasEmbedment_Loaded;
        }

        // DataGridZsの選択変更イベントハンドラ
        private void DataGridZs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dataGrid)
            {
                // 選択されたアイテムをViewModelに反映
                if (dataGrid.SelectedItem is PileZDataItem selectedItem)
                {
                    vm.SelectedZDataItem = selectedItem;
                }
            }
        }

        // 追加: horizon のセル選択イベントハンドラ
        private void Horizon_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                // 選択セルの先頭セルから行アイテムを取得して VM に設定
                if (dg.SelectedCells != null && dg.SelectedCells.Count > 0)
                {
                    var item = dg.SelectedCells[0].Item as HorizontalSoilReactionItem;
                    if (item != null)
                    {
                        vm.SelectedLayeronDataGrid = item;
                    }
                }
            }
        }

        private void Horizon_CurrentCellChanged(object sender, EventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                var item = dg.CurrentCell.Item as HorizontalSoilReactionItem;
                if (item != null)
                {
                    vm.SelectedLayeronDataGrid = item;
                }
            }
        }


        // DataGridEmbedmentZs 用（Embedment の選択を VM に反映）
        private void Embedment_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                if (dg.SelectedCells != null && dg.SelectedCells.Count > 0)
                {
                    var item = dg.SelectedCells[0].Item as EmbedmentZDataItem;
                    if (item != null)
                    {
                        vm.SelectedEmbedmentZ = item;
                    }
                    else if (dg.SelectedIndex >= 0 && dg.SelectedIndex < dg.Items.Count && dg.Items[dg.SelectedIndex] is EmbedmentZDataItem byIndex)
                    {
                        vm.SelectedEmbedmentZ = byIndex;
                    }
                }
            }
        }

        private void Embedment_CurrentCellChanged(object sender, EventArgs e)
        {
            if (DataContext is ElementDivisionViewModel vm && sender is DataGrid dg)
            {
                if (dg.CurrentCell != null && dg.CurrentCell.Item is EmbedmentZDataItem item)
                {
                    vm.SelectedEmbedmentZ = item;
                }
            }
        }
    }
}