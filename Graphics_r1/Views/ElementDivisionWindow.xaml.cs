using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using PileDesign.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    public partial class ElementDivisionWindow : Window
    {
        private readonly ElementDivisionViewModel _viewModel;
        private bool _isLoaded = false; // フラグを追加
        private bool _isClosingHandled = false;

        public ElementDivisionWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();

            _viewModel = new ElementDivisionViewModel(mainWindowViewModel);
            DataContext = _viewModel;

            Loaded += ElementDivisionWindow_Loaded;
        }

        private void ElementDivisionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;

            var viewModel = (ElementDivisionViewModel)DataContext;
            viewModel.ElementDivisionWindowInstance = this;
            viewModel.Canvas = Canvas;
            viewModel.CanvasEmbedment = CanvasEmbedment;

            // DataGridZs の既存設定（省略せずそのまま）
            DataGridZs.SelectionUnit = DataGridSelectionUnit.FullRow;
            DataGridZs.SelectionMode = DataGridSelectionMode.Single;

            viewModel.Initialize();

            Canvas.SizeChanged += Canvas_SizeChanged;
            CanvasEmbedment.SizeChanged += CanvasEmbedment_SizeChanged;

            _isLoaded = true;

            _viewModel.RequestClose += (s, e2) =>
            {
                if (_isClosingHandled) return;
                _isClosingHandled = true;
                if (this.IsLoaded && this.IsVisible) this.Close();
            };
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
    }
}