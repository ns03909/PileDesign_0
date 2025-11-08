using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// GroundWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GroundWindow : Window

    {
        private readonly GroundLayerViewModel _viewModel;
        private bool _isClosingHandled = false;

        // コンストラクタ
        public GroundWindow()
        {
            InitializeComponent();
            Loaded += GroundWindow_Loaded;
        }

        private void GroundWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                var _viewModel = viewModel;
                _viewModel.GroundWindowInstance = this; // MainWindow のインスタンスを渡す

                //_viewModel.RequestClose += (s, e) =>
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

                _viewModel.Initialize();
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
                // セル/行コミット完了後に Update()（確実に編集値が反映された後）
                grid.Dispatcher.InvokeAsync(() =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);

                    if (DataContext is GroundLayerViewModel vm)
                        vm.Update();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        // 行編集終了でも保険として再計算（行コミット後）
        private void DataGridGroundLayer_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            grid.Dispatcher.InvokeAsync(() =>
            {
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (DataContext is GroundLayerViewModel vm)
                    vm.Update();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void DataGridGroundMass_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            grid.Dispatcher.InvokeAsync(() =>
            {
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (DataContext is GroundLayerViewModel vm)
                    vm.Update();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void GroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (sender is ComboBox)
                {
                    viewModel.Update();
                }
            }
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
                    viewModel._undoManager.PushState([.. viewModel.GroundsInput.Select(x => x.DeepCopy())]);
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

            // 編集中 TextBox のバインディングをソースへ確定
            if (e.EditingElement is TextBox tb)
                BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();

            // 対象列（propertyName）編集時のみ、全行でエラーチェック
            bool targetColumn =
                e.Column is DataGridBoundColumn editedColumn &&
                editedColumn.Binding is Binding binding &&
                binding.Path?.Path == propertyName;

            if (sender is DataGrid dg)
            {
                dg.Dispatcher.InvokeAsync(() =>
                {
                    // 値のコミットを先に完了させる
                    dg.CommitEdit(DataGridEditingUnit.Cell, true);
                    dg.CommitEdit(DataGridEditingUnit.Row, true);

                    if (targetColumn)
                    {
                        int count = dg.Items.Count;

                        for (int i = 0; i < count; i++)
                        {
                            if (dg.Items[i] is not T rowItem) continue; // 追加行プレースホルダ等はスキップ

                            double value = GetDepthValue(rowItem, propertyName);
                            bool isError = false;

                            if (double.IsNaN(value))
                            {
                                isError = true; // 未設定/不正値はエラー
                            }
                            else
                            {
                                // 先頭行: GLは負値前提（>=0 はエラー）
                                if (i == 0) isError |= value >= 0;

                                // 一つ上: 上端の深さ > 現在の深さ（上<=現はエラー）
                                if (i > 0)
                                {
                                    double above = GetDepthValue(dg.Items[i - 1] as T, propertyName);
                                    if (!double.IsNaN(above)) isError |= above <= value;
                                }

                                // 一つ下: 現在の深さ > 下端の深さ（現<=下はエラー）
                                if (i < count - 1)
                                {
                                    double below = GetDepthValue(dg.Items[i + 1] as T, propertyName);
                                    if (!double.IsNaN(below)) isError |= value <= below;
                                }
                            }

                            SetIsError(rowItem, isError);
                        }
                    }

                    if (DataContext is GroundLayerViewModel vm) vm.Update();

                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

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

        // GroundNoのComboBoxの選択が変更されたときの処理
        //private void ComboBoxGroundNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (DataContext is GroundLayerViewModel viewModel)
        //    {
        //        if (ComboBoxGroundNo.SelectedItem != null)
        //        {
        //            int previousSelectedGroundNo = -1;
        //            if (e.RemovedItems.Count > 0)
        //            {
        //                previousSelectedGroundNo = (int)e.RemovedItems[0];
        //            }
        //            int selectedGroundNo = (int)ComboBoxGroundNo.SelectedItem;
        //            viewModel.ComboBoxGroundNo_SelectionChanged(selectedGroundNo, previousSelectedGroundNo);
        //        }
        //    }
        //}

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

        //private void OnGroundComboBoxSelectionChanged(SelectionChangedEventArgs e)
        //{
        //    var viewModel = (GroundLayerViewModel)DataContext;
        //    if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
        //    {

        //    }
        //    viewModel.Update();

        //}

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
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
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
                    viewModel._undoManager.PushState([.. viewModel.GroundsInput.Select(x => x.DeepCopy())]);
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
                    viewModel._undoManager.PushState([.. viewModel.GroundsInput.Select(x => x.DeepCopy())]);
                }
            }
        }
    }
}
