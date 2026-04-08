using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace PileDesign.Views
{
    /// <summary>
    /// GroundWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GroundWindow : Window

    {
        private readonly GroundLayerViewModel _viewModel;
        private bool _isClosingHandled = false;

        // 再入防止フラグ
        private bool _isCellEditEndingInProgress = false;

        // コンストラクタ
        public GroundWindow()
        {
            InitializeComponent();
            Loaded += GroundWindow_Loaded;
            // ★ ContentRendered で Initialize() を呼ぶように変更
            ContentRendered += GroundWindow_ContentRendered;
        }

        private void GroundWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                var _viewModel = viewModel;
                _viewModel.GroundWindowInstance = this;
                _viewModel.ActivateCustomDispTab = () =>
                {
                    // 任意地盤変位タブを前面に表示
                    if (CustomDispTab != null)
                        CustomDispTab.IsSelected = true;
                };

                //_viewModel.RequestClose += (s, e)
                _viewModel.RequestClose += (s, e2) =>
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
                };
                _viewModel.NValueTab = NValueTab;
                _viewModel.CuValueTab = CuValueTab;
                _viewModel.VsValueTab = VsValueTab;
                _viewModel.EsValueTab = EsValueTab;
                _viewModel.DefTab = DefTab;
                _viewModel.FsTab = FsTab;
                // 行コミット後の再計算を確実にする
                // 行コミット後の再計算を確実にする（両DataGridとも同一ハンドラ）
                //DataGridGroundLayer.RowEditEnding += DataGrid_Common_RowEditEnding;
                //DataGridGroundMass.RowEditEnding += DataGrid_Common_RowEditEnding;

                // ★ Initialize() は ContentRendered で呼び出すため、ここでは呼ばない
                // _viewModel.Initialize();
            }
        }

        /// <summary>
        /// ウィンドウが完全にレンダリングされた後に呼ばれる
        /// ScottPlot などのコントロールが完全に初期化された状態で Initialize() を呼ぶ
        /// </summary>
        private async void GroundWindow_ContentRendered(object? sender, EventArgs e)
        {
            // イベントを解除（1回だけ実行）
            ContentRendered -= GroundWindow_ContentRendered;

            // さらに少し待って、すべてのコントロールが完全に初期化されるのを待つ
            await System.Threading.Tasks.Task.Delay(50);

            if (DataContext is GroundLayerViewModel viewModel)
            {
                try
                {
                    viewModel.Initialize();
                }
                catch (System.Runtime.InteropServices.COMException comEx) when (comEx.HResult == unchecked((int)0x80040206))
                {
                    // 初期化時の COMException は無視して再試行
                    await System.Threading.Tasks.Task.Delay(100);
                    try
                    {
                        viewModel.Initialize();
                    }
                    catch
                    {
                        // 最終的に失敗しても続行
                    }
                }

                // CSVエクスポートメニューを追加
                PlotHelper.AddCsvExportMenu(wpfPlotNValue, "N値");
                PlotHelper.AddCsvExportMenu(wpfPlotCu, "Cu");
                PlotHelper.AddCsvExportMenu(wpfPlotVs, "Vs");
                PlotHelper.AddCsvExportMenu(wpfPlotEs, "Es");
                PlotHelper.AddCsvExportMenu(wpfPlotDisplacement, "地盤変位");
                PlotHelper.AddCsvExportMenu(wpfPlotFL, "FL値");
            }
        }


        // 共通: CellEditEnding（Layer/Mass 両方から使用）
        private void DataGrid_Common_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 編集中 TextBox のバインディングを明示的にソース更新
            if (e.EditingElement is TextBox tb)
            {
                var be = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                be?.UpdateSource();
            }

            if (sender is DataGrid grid)
            {
                // セル/行コミット完了後にデバウンス付きUpdate
                grid.Dispatcher.InvokeAsync(() =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);

                    if (DataContext is GroundLayerViewModel vm)
                        vm.ScheduleUpdate();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        // 行編集終了でも保険として再計算（行コミット後）
        private void DataGridGroundLayer_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            // ★ 修正: 行編集終了時に保留中の更新を処理
            // 行編集が終わった時点で IME/TextStore は解放されているはず
            _pendingUpdate = true;
            SchedulePendingUpdate();
        }

        private void DataGridGroundMass_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            // ★ 修正: 行編集終了時に保留中の更新を処理
            _pendingUpdate = true;
            SchedulePendingUpdate();
        }

        /// <summary>
        /// 保留中の更新をスケジュールする
        /// タイマーを使って IME/TextStore が完全に解放された後に実行
        /// </summary>
        private DispatcherTimer? _pendingUpdateTimer;

        private void SchedulePendingUpdate()
        {
            if (!_pendingUpdate) return;

            // 既存のタイマーがあれば停止（連続編集時の重複防止）
            _pendingUpdateTimer?.Stop();

            // ★ 重要: 現在フォーカスがある TextBox からフォーカスを外す
            // これにより IME/TextStore が解放される
            if (Keyboard.FocusedElement is TextBox focusedTextBox)
            {
                // DataGrid 自体にフォーカスを移動
                var parent = focusedTextBox.Parent;
                while (parent != null && parent is not DataGrid)
                {
                    parent = (parent as FrameworkElement)?.Parent;
                }
                if (parent is DataGrid dg)
                {
                    dg.Focus();
                }
            }

            // 十分な遅延を設けて IME/TextStore の解放を待つ（200ms に増加）
            _pendingUpdateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pendingUpdateTimer.Tick += (s, e) =>
            {
                _pendingUpdateTimer?.Stop();
                _pendingUpdateTimer = null;
                if (_pendingUpdate)
                {
                    _pendingUpdate = false;
                    SafeUpdate();
                }
            };
            _pendingUpdateTimer.Start();
        }

        /// <summary>
        /// COMException をキャッチしながら Update() を安全に実行
        /// </summary>
        private void SafeUpdate()
        {
            try
            {
                if (DataContext is GroundLayerViewModel vm)
                {
                    vm.ScheduleUpdate();
                }
            }
            catch (System.Runtime.InteropServices.COMException comEx) when (comEx.HResult == unchecked((int)0x80040206))
            {
                // IME/TextStore の競合による COMException を無視
                // ScheduleUpdate のデバウンスで自然に再試行される
            }
        }

        private void GroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (sender is ComboBox)
                {
                    viewModel.ScheduleUpdate();
                }
            }
        }

        private void CustomDispCase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ケース切り替え時にDataGridのItemsSourceが更新される（VMのPropertyChanged経由）
        }

        private void CustomDispAddRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                vm.SelectedCustomDispProfile.Add(new Models.InputData.DisplacementPoint(0, 0));
            }
        }

        private void CustomDispInsertBelowRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                var profile = vm.SelectedCustomDispProfile;

                // CurrentCellからアイテムを取得してインデックスを特定
                int idx = -1;
                if (DataGridCustomDisplacement.CurrentCell.Item is Models.InputData.DisplacementPoint currentItem)
                {
                    idx = profile.IndexOf(currentItem);
                }
                // フォールバック: SelectedIndex
                if (idx < 0)
                    idx = DataGridCustomDisplacement.SelectedIndex;

                if (idx >= 0 && idx < profile.Count)
                {
                    profile.Insert(idx + 1, new Models.InputData.DisplacementPoint(0, 0));
                }
                else
                {
                    profile.Add(new Models.InputData.DisplacementPoint(0, 0));
                }
            }
        }

        private void CustomDispDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GroundLayerViewModel vm || vm.SelectedCustomDispProfile == null) return;

            // CurrentCellまたはSelectedItemからアイテムを取得
            var point = DataGridCustomDisplacement.CurrentCell.Item as Models.InputData.DisplacementPoint
                        ?? DataGridCustomDisplacement.SelectedItem as Models.InputData.DisplacementPoint;
            if (point != null)
            {
                vm.SelectedCustomDispProfile.Remove(point);
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                vm.SelectedCustomDispProfile.Clear();
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispSortAndDedupe_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                var profile = vm.SelectedCustomDispProfile;
                // 標高Zで降順ソートし、重複するZは最初の1つだけ残す
                var sorted = profile
                    .OrderByDescending(p => p.Z)
                    .GroupBy(p => Math.Round(p.Z, 6))
                    .Select(g => g.First())
                    .ToList();

                profile.Clear();
                foreach (var p in sorted)
                    profile.Add(p);

                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispDataGrid_PasteCompleted(object sender, EventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm)
            {
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is GroundLayerViewModel vm)
                {
                    vm.ScheduleUpdate();
                    vm.ValidateCustomDisplacementProfiles();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void HandleTextBoxValueConfirmed(TextBox textBox)
        {
            var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
            if (DataContext is GroundLayerViewModel viewModel)
            {
                switch (textBox.Name)
                {
                    case "TextBoxGroundTopAltitude":
                        viewModel.GroundTopAltitudeTextBox_LostFocus();
                        break;
                    case "TextBoxGroundWaterTableAltitude":
                        viewModel.TextBoxGroundWaterTableAltitude_LostFocus();
                        break;
                    case "TextBoxGroundStressAltitude":
                        viewModel.TextBoxGroundStressAltitude_LostFocus();
                        break;
                    case "TextBoxGroundWaterGLDepth":
                        viewModel.TextBoxGroundWaterGLDepth_LostFocus();
                        break;
                    case "TextBoxStressGLDepth":
                        viewModel.TextBoxStressGLDepth_LostFocus();
                        break;
                    default:
                        viewModel.GroundTextBox_LostFocus();
                        break;
                }
            }
        }

        private void TextBox_Common_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                HandleTextBoxValueConfirmed(textBox);
            }
        }

        private void TextBox_Common_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                HandleTextBoxValueConfirmed(textBox);
            }
        }

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridPileLayout_OnLoadingRowCommand.Execute(e); // ビューモデルのコマンドを実行

            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 変更前の状態を保存
                //viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());

                viewModel.SliderEngineeringBedrockValueChangedCommand.Execute(e.NewValue);
            }
        }

        private object _originalValue;
        private string _originalPropertyName;

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column is DataGridBoundColumn editedColumn && editedColumn.Binding is Binding binding)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }

                var item = e.Row.Item;
                var propertyInfo = item.GetType().GetProperty(binding.Path.Path);
                if (propertyInfo != null)
                {
                    _originalValue = propertyInfo.GetValue(item);
                    _originalPropertyName = binding.Path.Path;
                }
            }
        }

        private void DataGrid_CellEditEnding<T>(object sender, DataGridCellEditEndingEventArgs e, string propertyName)
    where T : class
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 再入防止
            if (_isCellEditEndingInProgress) return;
            _isCellEditEndingInProgress = true;

            try
            {
                // 編集中 TextBox のバインディングをソースへ確定
                if (e.EditingElement is TextBox tb)
                {
                    var be = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                    be?.UpdateSource();
                }

                // 対象列（propertyName）編集時のみ、全行でエラーチェック
                bool targetColumn =
                    e.Column is DataGridBoundColumn editedColumn &&
                    editedColumn.Binding is Binding binding &&
                    binding.Path?.Path == propertyName;

                if (sender is not DataGrid dg) return;

                // エラーチェックは同期的に行う（Update() は呼ばない）
                if (targetColumn)
                {
                    int count = dg.Items.Count;

                    for (int i = 0; i < count; i++)
                    {
                        if (dg.Items[i] is not T rowItem) continue;

                        double value = GetDepthValue(rowItem, propertyName);
                        bool isError = false;

                        if (double.IsNaN(value))
                        {
                            isError = true;
                        }
                        else
                        {
                            if (i == 0) isError |= value >= 0;
                            if (i > 0)
                            {
                                double above = GetDepthValue(dg.Items[i - 1] as T, propertyName);
                                if (!double.IsNaN(above)) isError |= above <= value;
                            }
                            if (i < count - 1)
                            {
                                double below = GetDepthValue(dg.Items[i + 1] as T, propertyName);
                                if (!double.IsNaN(below)) isError |= value <= below;
                            }
                        }

                        SetIsError(rowItem, isError);
                    }
                }

                // ★ 修正: CellEditEnding 内では Update() を呼ばない
                // 代わりに _pendingUpdate フラグを立てて、タイマーで遅延更新する
                _pendingUpdate = true;
                SchedulePendingUpdate();
            }
            finally
            {
                // 遅延して再入フラグを解除
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    _isCellEditEndingInProgress = false;
                }), DispatcherPriority.Background);
            }
        }

        // 保留中の更新があるかどうか
        private bool _pendingUpdate = false;


        // GroundLayer 用: BottomGLDepth 列
        private void DataGridGroundLayer_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => DataGrid_CellEditEnding<GroundLayerInput>(sender, e, "BottomGLDepth");

        // GroundMass 用: GLDepth 列
        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => DataGrid_CellEditEnding<GroundMassDataInput>(sender, e, "GLDepth");


        // 型ごとに値取得
        private static double GetDepthValue(object row, string propertyName)
        {
            if (row == null) return double.NaN;
            var prop = row.GetType().GetProperty(propertyName);
            if (prop != null)
            {
                var value = prop.GetValue(row);
                if (value is double d) return d;
            }
            return double.NaN;
        }

        // 型ごとにIsErrorセット
        private static void SetIsError(object row, bool isError)
        {
            if (row == null) return;                  // 追加: nullガード
            var prop = row.GetType().GetProperty("IsError");
            if (prop == null || !prop.CanWrite) return;
            prop.SetValue(row, isError);
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

        private void SliderEngineeringBedrock_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                int value = (int)SliderEngineeringBedrock.Value;
                int n = viewModel.GroundInput.GroundLayers.Count;

                // i行のチェックボックスの状態が変更されたとき、1～i-1行のチェックボックスを有効化、i+1行目以降のチェックボックスを無効化
                for (int i = 0; i < n; i++)
                {
                    if (n - 1 - i < value)
                    {
                        viewModel.GroundInput.GroundLayers[i].IsEngineeringBedrock = true;
                    }
                    else
                    {
                        viewModel.GroundInput.GroundLayers[i].IsEngineeringBedrock = false;
                    }
                }
                viewModel.Update();
            }
        }

        private void GroundLayerCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridGroundLayer.Items.Count; i++)
                {
                    if (DataGridGroundLayer.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
        }


        private void ComboBoxGroundNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (ComboBoxGroundNo.SelectedItem is string /*selectedItem*/)
                {
                    // 例: "3 (New)" → 3, "2" → 2
                    int selectedIndex = ComboBoxGroundNo.SelectedIndex;
                    //int previousSelectedIndex = -1;
                    //if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string removedItem)
                    //{
                    //    previousSelectedIndex = ComboBoxGroundNo.Items.IndexOf(removedItem);
                    //}

                    viewModel.ComboBoxGroundNo_SelectionChanged(selectedIndex/*, previousSelectedIndex*/);
                }
            }
        }



        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is GroundLayerViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
        }

        // LevelのComboBoxの選択が変更されたときの処理
        private void ComboBoxLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            viewModel?.Update();
        }

        private void ButtonDeleteGroundMass_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            InputModel InputModel = viewModel.InputModel;
            if (DataGridGroundMass.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundMass.SelectedItem is GroundMassDataInput selectedItem)
                {
                    InputModel.GroundsInput[viewModel.GroundNo - 1].GroundMassesData.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundMass.Items.Refresh();
            }
        }

        private void ButtonDeleteGroundLayer_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            InputModel InputModel = viewModel.InputModel;
            if (DataGridGroundLayer.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundLayer.SelectedItem is GroundLayerInput selectedItem)
                {
                    InputModel.GroundsInput[viewModel.GroundNo - 1].GroundLayers.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundLayer.Items.Refresh();
                viewModel.Update();
            }
        }

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

        // PreviewKeyDownイベントを使用して編集をキャンセルする
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (sender is DataGrid dataGrid && dataGrid.SelectedItem is GroundLayerInput /*selectedItem*/)
                {
                    // 編集をキャンセルする
                    dataGrid.CancelEdit();
                    e.Handled = true;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                comboBox.Style = null; // 一度スタイルを解除
                comboBox.Style = (Style)FindResource("GranularityClassComboBoxStyle"); // 再適用


                // 重要: バインディングのソースへ反映を先に行う
                var be = BindingOperations.GetBindingExpression(comboBox, ComboBox.SelectedItemProperty);
                be?.UpdateSource();
            }

            if (DataContext is GroundLayerViewModel viewModel)
            {
                viewModel.ComboBox_SelectionChangedCommand();
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                binding?.UpdateSource();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void GroundWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is GroundLayerViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Y)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Undoスタック（状態保存）
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 編集前の状態を保存（EnterキーやBackspace/文字入力時など）
                // 必要に応じてキー判定を追加
                if (e.Key == Key.Enter)
                //if (e.Key == Key.Enter || e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space)
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }
            }
        }

        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Undoスタック（状態保存）
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 編集前の状態を保存（EnterキーやBackspace/文字入力時など）
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }
            }
        }

        // Button を押したときに ContextMenu を開く
        private void ButtonExamples_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var cm = btn.ContextMenu;
            if (cm == null) return;

            // ContextMenu をボタン右下に表示
            cm.PlacementTarget = btn;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        // ContextMenu 内の MenuItem が選択されたとき（DataContext は ExampleItem）
        private void ExampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi) return;
            if (mi.DataContext is not PileDesign.ViewModels.ExampleItem exampleItem) return;

            if (DataContext is not GroundLayerViewModel vm)
            {
                // 念のため閉じる
                if (mi.Parent is ContextMenu parentCm) parentCm.IsOpen = false;
                return;
            }

            try
            {
                // 実行前に undo スタックへ現在状態を保存（既存パターンに合わせる）
                vm._undoManager.PushState(vm.GroundsInput.Select(x => x.DeepCopy()).ToList());

                // 実行
                exampleItem.Command?.Execute(null);
            }
            finally
            {
                // メニューを閉じる
                if (mi.Parent is ContextMenu parentCm) parentCm.IsOpen = false;
            }
        }
    }
}
