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
        /// <summary>
        /// 杭配置表の「杭体番号」「地盤番号」のコンボで選んだとき。モデルへは <see cref="MainWindowViewModel.TrySetPileLayoutReference"/> を通して書く
        /// (バインドは表示だけ。直接書くと履歴・再解析が必要の印・杭要素分割の取り消しを通らなかった)。
        /// 断った・書けなかったときはコンボの表示をモデルの値に戻す。
        /// </summary>
        private void PileLayoutReferenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb || cb.DataContext is not PileLayoutDataItem pile) return;
            if (DataContext is not MainWindowViewModel vm) return;
            // 利用者が選んだときだけ (行の再利用・選択肢の差し替えで表示が変わったときは書かない)
            if (!(cb.IsDropDownOpen || cb.IsKeyboardFocusWithin)) return;
            if (cb.SelectedItem is not int value) return;
            bool isPileBody = Equals(cb.Tag, "PileBody");
            if (!vm.TrySetPileLayoutReference(pile, isPileBody, value))
                cb.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
        }

        // 材料・断面DataGridのセル編集時に解析結果を自動削除
        private void DataGridFoundationBeams_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && !vm.CheckAndResetElementSplit("梁要素"))
            {
                e.Cancel = true;
                return;
            }
            // 確定したときに値が変わったかを見るため、編集前の値を控える (β の欄)
            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var (ok, oldVal) = TryGetPropertyValue(e.Row.Item, path);
            if (ok) _dgOldValues[(e.Row.Item, path)] = oldVal;
        }

        /// <summary>
        /// 梁要素の表のコンボ (節点 I・J・材料番号・断面番号) で選んだとき。コンボはモデルへ直接書くので、
        /// 書き終えたあとに履歴へ積み、再解析が必要の印を付ける (直前の履歴の位置が書く前の状態)。
        /// 以前はどれも履歴・印・未保存の印を通らず、基礎梁を変えても古い水平解析の結果が最新のまま出て、Ctrl+Z でも戻せなかった。
        /// </summary>
        private void FoundationBeamCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb || DataContext is not MainWindowViewModel vm) return;
            // 利用者が選んだときだけ (行の再利用・選択肢の差し替えで表示が変わったときは積まない)
            if (!(cb.IsDropDownOpen || cb.IsKeyboardFocusWithin) || e.RemovedItems.Count == 0) return;
            vm.SaveUndoState("梁要素 編集");
            vm.RequestUpdateWindow();
        }

        // 基礎梁の材料・断面の表は、打つたびにモデルへ書く (UpdateSourceTrigger=PropertyChanged)。
        // 変更前の控えは、書き始める前 (編集に入るとき) に取る。
        private PileDesign.Models.InputData.InputModel? _beamMaterialEditBefore;
        private string? _beamMaterialEditFingerprint;

        private void DataGridBeamMaterialsOrSections_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Cancel || DataContext is not MainWindowViewModel vm) return;
            _beamMaterialEditBefore = vm.CaptureInputEdit();
            _beamMaterialEditFingerprint = vm.BeamMaterialFingerprint();
        }

        /// <summary>
        /// 基礎梁の材料・断面のセルを確定したとき。<b>値が変わったときだけ</b>、ほかの入力と同じく
        /// 編集の履歴に積み、解析結果に「再解析が必要」の印を付ける。
        ///
        /// <para>以前は値が変わっていなくても、確定のたびに全解析結果を確認なしで捨て、杭要素分割まで無効にしていた
        /// (セルに入って Enter を押しただけで、長い水平解析の結果が消え、Undo でも戻らなかった)。
        /// 基礎梁の材料・断面は杭要素分割と関係しないので、分割は残す。</para>
        /// </summary>
        private void DataGridBeamMaterialsOrSections_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var before = _beamMaterialEditBefore;
            var fingerprint = _beamMaterialEditFingerprint;
            _beamMaterialEditBefore = null;
            _beamMaterialEditFingerprint = null;
            if (e.EditAction != DataGridEditAction.Commit || before == null) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                if (vm.BeamMaterialFingerprint() == fingerprint) return;   // 触っただけ
                vm.CompleteInputEdit(before, "基礎梁の材料・断面の編集");
            });
        }

        private void DataGridFoundationBeams_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var path = GetBindingPath(e.Column);
            var item = e.Row.Item;

            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                // 値が変わったら履歴に積み、再解析が必要の印を付ける (β は解析の部材座標系に入る)。
                // 以前は描き直すだけで、履歴にも印にも残らなかった
                if (!string.IsNullOrEmpty(path))
                {
                    _dgOldValues.Remove((item, path), out var oldVal);
                    var (ok, newVal) = TryGetPropertyValue(item, path);
                    if (ok && !Equals(oldVal, newVal)) vm.SaveUndoState("梁要素 編集");
                }
                vm.RequestUpdateWindow();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void DataGridEmbedment_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridEmbedment_OnCellEditEndingCommand.Execute(e);
        }

        internal void DataGridSoilPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            // 編集前後の値を突き合わせて「実際に変わったか」を見る。
            // Undo は VM 側のスナップショット履歴 (SaveUndoStateDebounced) が持つので、
            // ここで拾うのは根入部形状の表示切替のためだけ。
            Dispatcher.BeginInvoke(() =>
            {
                var key = (item, path);
                _dgOldValues.TryGetValue(key, out var oldVal);
                var (ok2, newVal) = TryGetPropertyValue(item, path);
                _dgOldValues.Remove(key);

                if (!ok2) return;
                if (Equals(oldVal, newVal)) return;

                // 値が実際に変わったときのみ根入部形状表示を有効化
                if (DataContext is MainWindowViewModel vm2)
                    vm2.IsEmbedmentBoxVisible = true;

            }, System.Windows.Threading.DispatcherPriority.Background);

            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridSoilPile_OnCellEditEndingCommand.Execute(e);
        }

        internal void DataGridRectLoads_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridRectLoads_OnCellEditEndingCommand.Execute(e);
        }

        internal void DataGridSettlementSoilLayers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // 検査・Undo の履歴・層厚の計算は ViewModel 側でまとめて行う (値が変わらない確定では何もしない)
            (DataContext as MainWindowViewModel)?.CommitSettlementSoilLayerCellEdit(e);
        }

        private void DataGridPileAxialForce_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

            // 杭本数が多い場合 (例: 108 杭) に変更確定 (OnCellEditEndingCommand,
            // UpdateSumAndOTM, 変動↔絶対 同期) で時間が掛かるため、砂時計カーソルを表示。
            // リセットは Background 優先度のディスパッチが完了した後 (ApplicationIdle) に実行。
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            Dispatcher.BeginInvoke(() =>
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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
            else if (header.Contains("2-2") || bindingPath.Contains("AxialForceLevel2s[1]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[1].LoadName;
            else if (header.Contains("2-3") || bindingPath.Contains("AxialForceLevel2s[2]"))
                vm.SelectedLoadCaseName = vm.CurrentInputModel.LoadCasesInput.LoadCasesLevel2[2].LoadName;
            else if (header.Contains("2-4") || bindingPath.Contains("AxialForceLevel2s[3]"))
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
            // 杭の位置・杭体・地盤が変わりうるので、セルの確定と同じく杭要素分割を取り消して土層-杭セットを作り直す
            // (以前は取り消さず、分割済みだと変更前のセットのまま解いていた)。
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CompleteGridPaste("杭配置 貼り付け", changesPileGeometry: true);
                vm.UpdateSumAndOTM();
            }
        }

        /// <summary>
        /// 一般節点・梁の材料・梁の断面・梁要素・前後杭・根入部の表の Ctrl+V / Delete (範囲の一括クリア)。
        /// これらはセルの編集 (CellEditEnding) を通らずに値を書く。以前は受け口が杭配置表と軸力表にしか無く、
        /// 履歴にも「再解析が必要」の印にも残らず、Ctrl+Z でも戻せなかった。
        /// </summary>
        private void DataGridInput_PasteCompleted(object sender, EventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            string name = (sender as FrameworkElement)?.Name ?? "";
            var (description, geometry) = name switch
            {
                nameof(DataGridInputNodes) => ("一般節点 貼り付け", false),
                nameof(DataGridBeamMaterials) => ("梁の材料 貼り付け", false),
                nameof(DataGridBeamSections) => ("梁の断面 貼り付け", false),
                nameof(DataGridFoundationBeams) => ("梁要素 貼り付け", false),
                nameof(DataGridIsFrontPile) => ("前後杭 貼り付け", false),
                nameof(DataGridEmbedment) => ("根入部 貼り付け", true),
                _ => ("表 貼り付け", false),
            };
            if (sender == DataGridEmbedment) vm.UpdateEmbedment();
            vm.CompleteGridPaste(description, geometry);
            if (sender is DataGrid grid) grid.Items.Refresh();
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var path = GetBindingPath(e.Column);
            if (string.IsNullOrEmpty(path)) return;
            var item = e.Row.Item;

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
