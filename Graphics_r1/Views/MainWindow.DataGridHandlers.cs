using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PileDesign.Services;

namespace PileDesign.Views
{
    // DataGrid のセル編集関連イベントハンドラ（Beginning/CellEditEnding/PasteCompleted 等）を提供する partial。
    public partial class MainWindow
    {
        // 材料・断面DataGridのセル編集時に解析結果を自動削除
        private void DataGridFoundationBeams_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && !vm.CheckAndResetElementSplit("梁要素"))
            {
                e.Cancel = true;
            }
        }

        private void DataGridBeamMaterialsOrSections_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ResetAnalysisResultsSilently();
                    vm.RequestUpdateWindow();
                }
            });
        }

        private void DataGridFoundationBeams_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.RequestUpdateWindow();
                }
            });
        }

        private void DataGridEmbedment_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));
            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridEmbedment_OnCellEditEndingCommand.Execute(e);
        }

        private void DataGridSoilPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

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
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (e.Column is DataGridTextColumn textColumn)
            {
                if (textColumn.Header is StackPanel headerPanel)
                {
                    foreach (var child in headerPanel.Children)
                    {
                        if (child is TextBlock textBlock && textBlock.Text.Contains("下端Z"))
                        {
                            var dataGrid = sender as DataGrid;
                            var editedItem = e.Row.Item as SettlementSoilLayer;
                            var editedTextBox = e.EditingElement as TextBox;

                            if (double.TryParse(editedTextBox.Text, out double newValue))
                            {
                                int rowIndex = dataGrid.Items.IndexOf(editedItem);
                                if (rowIndex > 0)
                                {
                                    var previousItem = dataGrid.Items[rowIndex - 1] as SettlementSoilLayer;
                                    if (newValue >= previousItem.BottomAltitude)
                                    {
                                        MessageService.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                                        e.Cancel = true;

                                        editedTextBox.Text = editedItem.BottomAltitude.ToString("F2");
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }

            RecalculateThickness();

            var viewModel = DataContext as MainWindowViewModel;
            viewModel.IsGroupPileSettlementAnalysisDone = false;
        }

        private void RecalculateThickness()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var settlementSoilLayers = viewModel.CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            if (settlementSoilLayers == null || settlementSoilLayers.Count == 0) return;

            // 厚さは「土層上端 (SoilLayersTopAltitude)」基準で算出
            double topAltitude = viewModel.CurrentInputModel.PileGroupSettlement.SoilLayersTopAltitude;

            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                {
                    settlementSoilLayers[i].Thickness = topAltitude - settlementSoilLayers[i].BottomAltitude;
                }
                else
                {
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude;
                }
            }
        }

        private void DataGridPileAxialForce_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            // 杭本数が多い場合 (例: 108 杭) に変更確定 (UndoService.Push, OnCellEditEndingCommand,
            // UpdateSumAndOTM, 変動↔絶対 同期) で時間が掛かるため、砂時計カーソルを表示。
            // リセットは Background 優先度のディスパッチが完了した後 (ApplicationIdle) に実行。
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            Dispatcher.BeginInvoke(() =>
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (sender is not DataGrid dataGrid || dataGrid.SelectedCells.Count == 0) return;

            var column = e.Column;
            if (column == null) return;

            string header = "";
            if (column.Header is StackPanel headerPanel)
            {
                foreach (var child in headerPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        header += textBlock.Text;
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

            vm.IsMassLoadingVisible = true;
            vm.IsAxialLoadingVisible = true;

            vm?.DataGridPileAxialForce_OnCellEditEndingCommand.Execute(e);

            Dispatcher.BeginInvoke(() => vm?.UpdateSumAndOTM(),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void DataGridPileAxialForce_PasteCompleted(object sender, EventArgs e)
        {
            // ペーストは複数セルを一気に書換えるため、CellEditEnding より重くなりやすい。
            // 砂時計カーソルを表示し、UpdateSumAndOTM 完了後 (ApplicationIdle) にリセット。
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    // ペースト/Delete クリアは編集サイクルを経由せず CellEditEnding が発火しないため、
                    // ここで 1 つの Undo スナップショットを保存し、一括で元に戻せるようにする。
                    vm.SaveUndoState("杭軸力 貼り付け");
                    vm.UpdateSumAndOTM();
                }
            }
            finally
            {
                Dispatcher.BeginInvoke(() =>
                {
                    System.Windows.Input.Mouse.OverrideCursor = null;
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void DataGridPileLayout_PasteCompleted(object sender, EventArgs e)
        {
            // 配置グリッドのペースト/Delete クリアも CellEditEnding を経由しないため、
            // ここで 1 つの Undo スナップショットを保存する。X/Y/軸力の変更は集計値にも影響する。
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SaveUndoState("杭配置 貼り付け");
                vm.UpdateSumAndOTM();
            }
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

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
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                UndoService.Instance.Push(new PropertyChangeAction<object?>(item, path, oldVal, newVal, $"Edit {path}"));
            }, System.Windows.Threading.DispatcherPriority.Background);

            if (sender is not DataGrid dataGrid || dataGrid.SelectedCells.Count == 0) return;

            var column = e.Column;
            if (column == null) return;

            string header = "";
            if (column.Header is StackPanel headerPanel)
            {
                foreach (var child in headerPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        header += textBlock.Text;
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
            vm?.DataGridIsFrontPile_OnCellEditEndingCommand.Execute(e);
        }
    }
}
