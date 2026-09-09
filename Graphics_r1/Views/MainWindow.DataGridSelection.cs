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
    /// MainWindow — 左の表の操作。
    ///
    /// 杭配置・軸力・前後方杭・一般節点・根入部・通り心の各表で、
    /// 行番号を振り、編集の開始と終了を受け、選択を画面の選択と行き来させる。
    ///
    /// <b>表の選択と画面の選択は双方向。</b> 片方を書き換えるともう片方の通知が返り、
    /// そのまま書き戻すと往復し続ける。<c>isUpdatingSelection</c> で止めている。
    ///
    /// <b>行番号は行ヘッダーに入れる。</b> セルにすると、選択セルのコピーに混ざって
    /// 貼り付け先で列がずれる。
    ///
    /// 以前は <c>MainWindow.xaml.cs</c> (4,090 行) の中にあった。
    /// </summary>
    public partial class MainWindow
    {
        internal void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        internal void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
            if (CheckBoxCommonActionPoint3D.IsChecked == true)
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
                .ApplyActiveCaseToLegacyFields(pgs, pgs.CaseRecords[idx], vm.CurrentInputModel?.PileLayoutItems);

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
        internal void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
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

                // Undo は VM のスナップショット履歴に積む。
                // ここは UndoService (ChangWindow 専用の別スタック) に積んでいたため、
                // メイン画面の Ctrl+Z では戻せず、ChangWindow の Ctrl+Z で戻ってしまっていた。
                // 大規模操作なので変更前に 1 段保存する (杭の追加・分割等と同じ扱い)。
                viewModel.SaveUndoState("杭配置 削除");

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

                // Undo は VM のスナップショット履歴に積む (杭配置の削除と同じ理由)。
                viewModel.SaveUndoState("節点 削除");

                col.RemoveAt(index);
                UpdateWindow();
            }
            else
            {
                MessageService.Show("削除対象のアイテムが正しく取得できませんでした。");
            }
        }


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
    }
}
