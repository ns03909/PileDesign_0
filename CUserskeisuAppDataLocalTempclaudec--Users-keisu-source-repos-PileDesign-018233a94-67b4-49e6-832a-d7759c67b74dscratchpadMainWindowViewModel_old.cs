using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PileDesign.Views.AutoIsFrontPilesWindow;
using static PileDesign.Views.EditPileLayoutWindow;
using static PileDesign.Views.MoveCopyWindow;
using Point = System.Windows.Point;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly UndoManager _undoManager = new();
        // JsonSerializerOptions をキャッシュ
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        private double _rightBlankWidthPx = 100.0;
        public double RightBlankWidthPx
        {
            get => _rightBlankWidthPx;
            set
            {
                if (Math.Abs(_rightBlankWidthPx - value) < double.Epsilon) return;
                _rightBlankWidthPx = value;
                OnPropertyChanged(nameof(RightBlankWidthPx));
                // スライダー変更時に再描画
                UpdateCanvas3DAction?.Invoke();
            }
        }

        public InputModel CurrentInputModel { get; set; }

        public Canvas Canvas3DLayout { get; set; }

        // アクション
        public Action UpdateWindowAction { get; set; }
        public Action UpdateCanvas3DAction { get; set; }

        // ファイルパス
        public string CurrentFilePath { get; set; }

        // サブViewModelの初期化

        // イベントの宣言
        public event EventHandler<DataGridCellEditEndingEventArgs> DataGridSettlementSoilLayersCellEditEnding;

        // イベントを発火するメソッド
        public virtual void OnDataGridSettlementSoilLayersCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            DataGridSettlementSoilLayersCellEditEnding?.Invoke(this, e);
        }

        // コマンドの実装
        private ICommand _dataGridSettlementSoilLayersCellEditEndingCommand;
        public ICommand DataGridSettlementSoilLayersCellEditEndingCommand
        {
            get
            {
                _dataGridSettlementSoilLayersCellEditEndingCommand ??= new RelayCommand<DataGridCellEditEndingEventArgs>(OnDataGridSettlementSoilLayersCellEditEnding);
                return _dataGridSettlementSoilLayersCellEditEndingCommand;
            }
        }

        // 追加: ビュー操作用デリゲート（コードビハインド側でセット）
        public Action? ZoomFitAction { get; set; }
        public Action<double, double>? AnimateViewAnglesAction { get; set; }

        // ズームフィット
        [RelayCommand]
        private void ZoomFit()
        {
            ZoomFitAction?.Invoke();
        }

        // XY平面
        [RelayCommand]
        private void ViewXYPlane()
        {
            // θ=-90, φ=90
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-90, 90);
            else
            {
                CanvasThreeDView.Tht = -90;
                CanvasThreeDView.Phi = 90;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // YZ平面
        [RelayCommand]
        private void ViewYZPlane()
        {
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(0, 0);
            else
            {
                CanvasThreeDView.Tht = 0;
                CanvasThreeDView.Phi = 0;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // XZ平面
        [RelayCommand]
        private void ViewXZPlane()
        {
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-90, 0);
            else
            {
                CanvasThreeDView.Tht = -90;
                CanvasThreeDView.Phi = 0;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // アイソメ
        [RelayCommand]
        private void ViewIsometric()
        {
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-45, 45);
            else
            {
                CanvasThreeDView.Tht = -45;
                CanvasThreeDView.Phi = 45;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // イベントハンドラの実装
        private void HandleDataGridSettlementSoilLayersCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column is DataGridTextColumn && e.Column.Header.ToString().Contains("下端Z"))
            {
                var dataGrid = sender as DataGrid;
                var editedItem = e.Row.Item as SettlementSoilLayer; // SettlementSoilLayer は適切なモデルクラスに置き換えてください
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
                        }
                    }
                }
            }
        }

        public ICommand OpenDoatsuGoryokuBaneWindowCommand { get; }
        public ICommand ComboBoxLabelSize_OnSelectionChangedCommand { get; }

        [RelayCommand]
        private static void DataGridPileLayout_OnLoadingRow(DataGridRowEventArgs e)
        {
            if (e.Row.Item is PileLayoutDataItem)
            {
                e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
            }
        }

        // 杭配置更新時更新メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                IsElementSplit = false;

                CurrentInputModel.GenerateSoilPiles(); ////////////////////////

                // ウィンドウの更新
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // コレクション自体の変更通知
                OnPropertyChanged(nameof(GroupPileSettlementXmin));
                OnPropertyChanged(nameof(GroupPileSettlementXmax));
                OnPropertyChanged(nameof(GroupPileSettlementYmin));
                OnPropertyChanged(nameof(GroupPileSettlementYmax));
                //OnPropertyChanged(nameof(GroupPileSettlementXCount));
                //OnPropertyChanged(nameof(GroupPileSettlementYCount));
                //OnPropertyChanged(nameof(GroupPileSettlementCount));
            }
        }

        // 杭軸力更新時更新メソッド
        [RelayCommand]
        private void DataGridPileAxialForce_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                // ウィンドウの更新
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

            }
        }

        // 前後杭更新メソッド
        [RelayCommand]
        private void DataGridIsFrontPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                // ウィンドウの更新
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

            }
        }

        // 杭配置表編集開始時メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (!CheckAndResetElementSplit("杭配置"))
            {
                e.Cancel = true;
            }
        }

        // 要素分割解除確認メソッド
        public bool CheckAndResetElementSplit(string text)
        {
            if (IsElementSplit == true)
            {
                MessageBoxResult result = MessageBox.Show(
                    $"{text}を編集、確定するには、入力済みの要素分割および、" +
                    $"\n解析結果が存在する場合は解析結果を削除する必要があります。" +
                    $"\nよろしいですか。",
                    "確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    return false;
                }
                else
                {
                    IsElementSplit = false;
                    IsVerticalAnalysisDone = false;
                    IsHorizontalAnalysisDone = false;
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
            return true;
        }

        // 杭配置表マウス右ボタン押メソッド
        [RelayCommand]
        private static void DataGridPileLayout_OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // マウス位置で ContextMenu を表示
                //startPoint = e.GetPosition(null);
            }
        }
        [RelayCommand]
        private void DataGridPileLayout_AutoGeneratingColumn(DataGridAutoGeneratingColumnEventArgs e)
        {
            // カラム名をチェックし、適宜処理を行う
            if (e.PropertyName == "AxialForceEX" || e.PropertyName == "AxialForceEY" ||
                e.PropertyName == "AxialForceLevel1s[0]" || e.PropertyName == "AxialForceLevel1s[1]" ||
                e.PropertyName == "AxialForceLevel1s[2]" || e.PropertyName == "AxialForceLevel1s[3]")
            {
                if (e.Column is DataGridTextColumn dataGridColumn)
                {
                    // Visibility を制御するバインディングを設定
                    var isElastic = IsElastic ? Visibility.Visible : Visibility.Collapsed;
                    dataGridColumn.Visibility = isElastic;
                }
            }
        }
        [RelayCommand]
        private void ComboBoxEmbedmentNums_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Handled = true;
            }
        }
        [RelayCommand]
        private void ComboBoxEmbedmentGroundNo_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Handled = true;
            }
        }
        [RelayCommand]
        private void TextBoxBottomAltitude_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Handled = true;
            }
        }
        [RelayCommand]
        private void DataGridEmbedment_OnBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Cancel = true;
            }
        }
        [RelayCommand]
        private static void ButtonGround_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            //if (!CheckAndResetElementSplit("地盤"))
            //{
            //    e.Handled = true;
            //}
        }
        [RelayCommand]
        private static void ButtonPileBody_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            //if (!CheckAndResetElementSplit("杭体"))
            //{
            //    e.Handled = true;
            //}
        }
        [RelayCommand]
        private static void ButtonSettlement_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }
        [RelayCommand]
        private void ComboBoxEmbedmentNums_OnSelectionChanged(SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is int selectedValue)
            {
                int currentCollectionSize = CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count;

                // Remove excess items if selectedValue is less than the current collection size
                for (int i = currentCollectionSize - 1; i >= selectedValue; i--)
                {
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers.RemoveAt(i);
                }

                // Add new rows only if selectedValue is greater than the current collection size
                for (int i = currentCollectionSize; i < selectedValue; i++)
                {
                    EmbedmentDataItem newItem = CreateNewEmbedmentDataItem(i, currentCollectionSize);
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers.Add(newItem);
                }

                UpdateEmbedment();
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();
            }
        }
        [RelayCommand]
        private void TextBoxAltitude_OnTextChanged(TextChangedEventArgs e)
        {
            UpdateEmbedment();
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }


        private EmbedmentDataItem CreateNewEmbedmentDataItem(int index, int currentCollectionSize)
        {
            EmbedmentDataItem newItem;
            if (currentCollectionSize > 0 && index > 0)
            {
                EmbedmentDataItem lastItem = CurrentInputModel.EmbedmentInput.EmbedmentLayers[index - 1];
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = lastItem.LayerThickness,
                    //TopAltitude = lastItem.TopAltitude,
                    //BottomAltitude = lastItem.BottomAltitude,
                    X1 = lastItem.X1,
                    X2 = lastItem.X2,
                    Y1 = lastItem.Y1,
                    Y2 = lastItem.Y2,
                };
            }
            else
            {
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = 5.0,
                    //TopAltitude = 0.0,
                    //BottomAltitude = 0.0,
                    X1 = 0.0,
                    X2 = 50.0,
                    Y1 = 0.0,
                    Y2 = 50.0,
                };
            }
            return newItem;
        }
        [RelayCommand]
        private void DataGridEmbedment_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                UpdateEmbedment();

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            }
        }
        [RelayCommand]
        private void DataGridSoilPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            }
        }

        // 根入部データグリッド更新メソッド
        public void UpdateEmbedment()
        {
            // EmbedmentCollection の更新
            for (int i = CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1; i >= 0; i--)
            {
                if (i == CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1)
                {
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.BottomAltitude;
                }
                else
                {
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i + 1].TopAltitude;
                }
                CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].TopAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude
                    + CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].LayerThickness;
            }
        }
        [RelayCommand]
        private void DataGridRectLoads_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                IsGroupPileSettlementAnalysisDone = false;

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            }
        }


        private void DataGridSettlementSoilLayers_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                if (e.Column is DataGridTextColumn && e.Column.Header.ToString().Contains("下端Z"))
                {
                    var dataGrid = sender as DataGrid;
                    var editedItem = e.Row.Item as SettlementSoilLayer; // SettlementSoilLayer は適切なモデルクラスに置き換えてください
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
                            }
                        }
                    }
                }
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            }
        }

        // GridX追加メソッド
        [RelayCommand]
        private void AddGridX()
        {
            // Undoポイントを追加（1回の追加を1ステップで戻せるようにする）
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // 防波堤: null の場合はここで生成
            CurrentInputModel.GridXItems ??= [];
            AddGrid(CurrentInputModel.GridXItems, "Y1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridXItems));
        }

        // GridY追加メソッド
        [RelayCommand]
        private void AddGridY()
        {
            // Undoポイントを追加（1回の追加を1ステップで戻せるようにする）
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // 防波堤: null の場合はここで生成
            CurrentInputModel.GridYItems ??= [];
            AddGrid(CurrentInputModel.GridYItems, "X1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridYItems));
        }

        // Grid追加メソッド
        private void AddGrid(ObservableCollection<GridDataItem> collection, string name, double spacing)
        {
            collection.Add(new GridDataItem());
            if (collection.Count == 1)
            {
                collection[^1].Name = name;
            }
            // 複数のアイテムがある場合、前のアイテムの設定をコピー
            else if (collection.Count == 2)
            {
                collection[^1].Spacing = spacing;
                collection[^1].Name = StringTransformer.TransformLastCharacter(collection[^2].Name);
            }
            else if (collection.Count >= 3)
            {
                collection[^1].Spacing = collection[^2].Spacing;
                collection[^1].Name = StringTransformer.TransformLastCharacter(collection[^2].Name);
            }
            RecalculateGrid(collection);
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        private void RecalculateGrid(Collection<GridDataItem> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (i == 0)
                {
                    collection[i].Spacing = 0;
                    collection[i].SpacingForeground = Brushes.Gray;
                    collection[i].CoordForeground = Brushes.Black;
                }
                else
                {
                    collection[i].Coord = collection[i - 1].Coord + collection[i].Spacing;
                    collection[i].SpacingForeground = Brushes.Black;
                    collection[i].CoordForeground = Brushes.Gray;
                }
            }
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 矩形荷重追加メソッド
        [RelayCommand]
        private void AddRectLoad()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            if (!CheckAndResetPostAnalysisMode())
            { return; }

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad());

            IsGroupPileSettlementAnalysisDone = false;
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 解析後処理モードの場合の確認
        private bool CheckAndResetPostAnalysisMode()
        {
            if (IsPostAnalysisMode)
            {
                var result = MessageBox.Show("解析前処理モードにしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    return false; // 操作をキャンセル
                }
                IsPostAnalysisMode = false; // 解析前処理モードに変更
            }
            return true; // 操作を続行
        }

        // 群杭沈下検討用検討用土層追加メソッド
        [RelayCommand]
        private void AddSettlementSoilLayer()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            double bottomAlt;
            double ek;
            double poissonsRatio;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;

            if (!CheckAndResetPostAnalysisMode())
            { return; }

            if (CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Count == 0)
            {
                bottomAlt = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude - 10.0;
                ek = 100_000_000;
                poissonsRatio = 0.3;
            }
            else
            {
                bottomAlt = settlementSoilLayers[^1].BottomAltitude - 10.0;
                ek = settlementSoilLayers[^1].Ek;
                poissonsRatio = settlementSoilLayers[^1].PoissonsRatio;
            }

            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Add(
                new SettlementSoilLayer()
                {
                    BottomAltitude = bottomAlt,
                    Ek = ek,
                    PoissonsRatio = poissonsRatio
                });

            UpdateSettlementSoilLayer(); // 更新

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 全土層削除メソッド
        [RelayCommand]
        private void DeleteAllSettlementSoilLayers()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            var settlement = CurrentInputModel?.PileGroupSettlement;
            if (settlement == null)
            {
                return;
            }

            // 土層コレクションをクリア
            settlement.SettlementSoilLayers?.Clear();

            // 解析に用いるグリッドデータをクリア
            try
            {
                settlement.SettlementGridData?.Clear();
                settlement.SettlementGridX?.Clear();
                settlement.SettlementGridY?.Clear();
            }
            catch
            {
                // 念のため例外は無視（コレクションが null の可能性など）
            }

            // モデル側のグリッドデータ削除用メソッドがあれば呼ぶ
            try
            {
                settlement.RemoveGridDataSettlement();
            }
            catch
            {
                // 実装がない場合や例外は無視
            }

            // 解析フラグと表示フラグをリセット
            IsGroupPileSettlementAnalysisDone = false;
            IsGroupPileGridDeformationVisible = false;
            IsBubbleVisible = false;
            IsArrowVisible = false;

            // 必要ならプロパティ更新通知
            OnPropertyChanged(nameof(CurrentInputModel));

            // ウィンドウ更新
            UpdateWindowAction?.Invoke();
            UpdateTreeView();
        }

        // 群杭沈下検討用検討用土層削除メソッド
        [RelayCommand]
        private void DeleteSettlementSoilLayer(object sender)
        {
            // sender が GridDataItem であることを確認
            if (sender is not SettlementSoilLayer itemToDelete) return;

            // コレクションから削除
            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Remove(itemToDelete);

            UpdateSettlementSoilLayer(); // 更新

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 群杭沈下検討用検討用土層データグリッド更新メソッド
        private void UpdateSettlementSoilLayer()
        {
            // SettlementCollection の更新
            double loadingPlaneAltitude = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                    settlementSoilLayers[i].Thickness = loadingPlaneAltitude - settlementSoilLayers[i].BottomAltitude;
                else
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude; ;
            }
        }


        public void DataGridGridX_CurrentCellChanged()
        {
            RecalculateGrid(CurrentInputModel.GridXItems);
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        public void DataGridGridY_CurrentCellChanged()
        {
            RecalculateGrid(CurrentInputModel.GridYItems);
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        [RelayCommand]
        private void DataGridGridX_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
            {
                RecalculateGrid(CurrentInputModel.GridXItems);
            }
        }
        [RelayCommand]
        private void DataGridGridY_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
            {
                RecalculateGrid(CurrentInputModel.GridYItems);
            }
        }

        [RelayCommand]
        private void DeleteGridX(object sender)
        {
            // Undoポイント
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            DeleteGridItem(sender, CurrentInputModel.GridXItems);
            RecalculateGrid(CurrentInputModel.GridXItems);
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }
        [RelayCommand]
        private void DeleteGridY(object sender)
        {
            // Undoポイント
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            DeleteGridItem(sender, CurrentInputModel.GridYItems);
            RecalculateGrid(CurrentInputModel.GridYItems);
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }
        [RelayCommand]
        private void DeleteElement(object sender)
        {
            // sender が GridDataItem であることを確認
            if (sender is not Element itemToDelete) return;

            // コレクションから削除
            CurrentInputModel.Elements.Remove(itemToDelete);

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        private static void DeleteGridItem(object sender, ObservableCollection<GridDataItem> collection)
        {
            // sender が GridDataItem であることを確認
            if (sender is not GridDataItem itemToDelete) return;

            // コレクションから削除
            collection.Remove(itemToDelete);
        }

        [RelayCommand]
        private void DeleteRectLoad(object sender)
        {
            // sender が GridDataItem であることを確認
            if (sender is not RectLoad itemToDelete) return;

            // コレクションから削除
            CurrentInputModel.PileGroupSettlement.RectLoads.Remove(itemToDelete);

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        //[RelayCommand]
        //private void ComboBox3DLabelContent_OnSelectionChanged(SelectionChangedEventArgs e)
        //{
        //    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        //}

        [RelayCommand]
        private void ComboBox3DAnalysisResultContent_OnSelectionChanged(SelectionChangedEventArgs e)
        {
            UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        [RelayCommand]
        private void ComboBox3DLabelSize_OnSelectionChanged(SelectionChangedEventArgs e)
        {
            UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 杭配置追加コマンドの実行メソッド
        [RelayCommand]
        private void OnAddPile()
        {
            if (!CheckAndResetPostAnalysisMode()) return;
            if (!CheckAndResetAnalysisResults()) return;

            // スナップショットを保存
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            Point3D nextPoint3D = new();
            if (CurrentInputModel.PileLayoutItems.Count != 0)
            {
                // 直前の杭から X 方向に 7.2m オフセット
                nextPoint3D = CurrentInputModel.PileLayoutItems.Last().Point3D + new Vector3D() { X = 7.2 };
            }

            // UI スレッドでコレクションへ追加
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem() { X = nextPoint3D.X, Y = nextPoint3D.Y, Z = nextPoint3D.Z });
                CurrentInputModel.PileLayoutItems[^1].SetMainWindowViewModel(this);
                // 要素未分割の場合は自動で SoiPile を再生成
                if (!IsElementSplit)
                {
                    CurrentInputModel.GenerateSoilPiles();//////////////////////////////////////////
                }

                // 画面更新と通し番号のふり直し
                UpdateWindowAction?.Invoke();
                UpdatePileLayoutNo();
                UpdateTreeView();
            });
        }
        [RelayCommand]
        private void OnComputePileGroupFactor()
        {
            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0) { return; }
        }

        [RelayCommand]
        private void OnComputePileSpacingFactor()
        {
            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0) { return; }
        }

        // 重複要素の削除
        [RelayCommand]
        private void OnDeleteDupulicateElements()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            var uniqueElements = new HashSet<Element>(CurrentInputModel.Elements);
            CurrentInputModel.Elements = new ObservableCollection<Element>(uniqueElements);

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 要素の節点位置での分割
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            var newElements = new ObservableCollection<Element>();

            foreach (var element in CurrentInputModel.Elements)
            {
                if (element.IsSelected)
                {
                    var splitElements = SplitTwoNodeElementByNodes(element);
                    foreach (var splitElement in splitElements)
                    {
                        newElements.Add(splitElement);
                    }
                }
                else
                {
                    newElements.Add(element);
                }
            }

            CurrentInputModel.Elements = newElements;
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        // 要素を分割するメソッド
        public ObservableCollection<Element> SplitTwoNodeElementByNodes(Element element, double threshold = 0.005)
        {
            var newElements = new ObservableCollection<Element>();
            var splitNodes = element.Nodes;
            Point3D pointS = element.Nodes[0].Point3D;
            Point3D pointE = element.Nodes[1].Point3D;

            Point nodeS = CanvasThreeDView.Transformation(pointS);
            Point nodeE = CanvasThreeDView.Transformation(pointE);

            foreach (var pileLayout in CurrentInputModel.PileLayoutItems)
            {
                Point3D point3D = pileLayout.Point3D;
                Point node = CanvasThreeDView.Transformation(point3D);

                if (GetDistance.BetweenNodeAndLine(nodeS, nodeE, node) <= threshold && !splitNodes.Contains(pileLayout))
                {
                    splitNodes.Add(pileLayout);
                }
            }

            // splitNodes[0] と splitNodes[^1] の間に並べ替える
            List<double> distances = [];
            for (int i = 0; i < splitNodes.Count; i++)
            {
                distances.Add(GetDistance.BetweenTwoPoint3Ds(splitNodes[0].Point3D, splitNodes[i].Point3D));
            }

            // distances の中で i 番目に小さな値のインデックスを取得
            List<int> indeces = [];

            for (int i = 0; i < distances.Count; i++)
            {
                indeces.Add(GetIndexOfNthSmallestValue(distances, i));
                // 必要な処理をここに追加
            }


            for (int i = 0; i < splitNodes.Count - 1; i++)
            {
                newElements.Add(new Element(element.ElementType,
                    (PileLayoutDataItem)splitNodes[indeces[i]],
                    (PileLayoutDataItem)splitNodes[indeces[i + 1]]));
            }

            return newElements;
        }

        private static int GetIndexOfNthSmallestValue(List<double> distances, int n)
        {
            var indexedDistances = distances
                .Select((value, index) => new { Value = value, Index = index })
                .OrderBy(pair => pair.Value)
                .ToList();

            return indexedDistances[n].Index;
        }

        // 杭配置番号の更新
        public void UpdatePileLayoutNo()
        {
            for (int i = 0; i < CurrentInputModel.PileLayoutItems.Count; i++)
            {
                CurrentInputModel.PileLayoutItems[i].No = i + 1;
            }
        }

        // 荷重面の自動生成
        [RelayCommand]
        private void OnAdjustRectLoadPlan()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            double maxX = double.MinValue;
            double minX = double.MaxValue;
            double maxY = double.MinValue;
            double minY = double.MaxValue;

            foreach (var pileLayoutDataItem in CurrentInputModel.PileLayoutItems)
            {
                double x = pileLayoutDataItem.Point3D.X;
                double y = pileLayoutDataItem.Point3D.Y;
                if (x > maxX) maxX = x;
                if (x < minX) minX = x;
                if (y > maxY) maxY = y;
                if (y < minY) minY = y;
            }

            double adjustedMinX = minX - RectLoadPileDistance;
            double adjustedMaxX = maxX + RectLoadPileDistance;
            double adjustedMinY = minY - RectLoadPileDistance;
            double adjustedMaxY = maxY + RectLoadPileDistance;

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad()
            {
                X1 = adjustedMinX,
                X2 = adjustedMaxX,
                Y1 = adjustedMinY,
                Y2 = adjustedMaxY,
                QA = 0.0
            }
            );

            IsGroupPileSettlementAnalysisDone = false;

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }




        // 根入部平面の自動調整
        [RelayCommand]
        private void OnAdjustEmbedmentPlan()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0 || CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count == 0)
            {
                return;
            }

            double maxX = double.MinValue;
            double minX = double.MaxValue;
            double maxY = double.MinValue;
            double minY = double.MaxValue;

            foreach (var pileLayoutDataItem in CurrentInputModel.PileLayoutItems)
            {
                double x = pileLayoutDataItem.Point3D.X;
                double y = pileLayoutDataItem.Point3D.Y;
                if (x > maxX) maxX = x;
                if (x < minX) minX = x;
                if (y > maxY) maxY = y;
                if (y < minY) minY = y;
            }

            double adjustedMinX = minX - EmbedmentPileDistance;
            double adjustedMaxX = maxX + EmbedmentPileDistance;
            double adjustedMinY = minY - EmbedmentPileDistance;
            double adjustedMaxY = maxY + EmbedmentPileDistance;

            foreach (var embedmentDataItem in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                embedmentDataItem.X1 = adjustedMinX;
                embedmentDataItem.X2 = adjustedMaxX;
                embedmentDataItem.Y1 = adjustedMinY;
                embedmentDataItem.Y2 = adjustedMaxY;
            }

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }

        // 慣性力作用点を杭配置の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            List<double> xs = [];
            List<double> ys = [];

            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageBox.Show("杭配置データがありません。");
                return;
            }

            foreach (PileLayoutDataItem pileLayoutInput in CurrentInputModel.PileLayoutItems)
            {
                xs.Add(pileLayoutInput.Point3D.X);
                ys.Add(pileLayoutInput.Point3D.Y);
            }

            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX = xs.Average();
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointY = ys.Average();

            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointX = xs.Average();
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointY = ys.Average();

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
            {
                loadCase.ForceActionPointX = xs.Average();
                loadCase.ForceActionPointY = ys.Average();
            }

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
            {
                loadCase.ForceActionPointX = xs.Average();
                loadCase.ForceActionPointY = ys.Average();
            }

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        }

        [RelayCommand]
        private void AutoIsFrontPiles()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // ViewModel を作成
            var viewModel = new AutoIsFrontPileViewModel();

            // Windowをインスタンス化して表示
            AutoIsFrontPilesWindow autoIsFrontPilesWindow = new();
            autoIsFrontPilesWindow.AutoIsFrontPileCompleted += AutoIsFrontPilesWindow_AutoIsFrontPileCompleted;

            autoIsFrontPilesWindow.ShowDialog(); // モーダルダイアログとして表示

            IsFrontPileLabelVisible = true;

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }

        //群杭係数ウィンドウを開くメソッド
        [RelayCommand]
        private void GroupPileFactor()
        {
            // Windowをインスタンス化して表示
            GroupPileFactorWindow groupPileFactorWindow = new(this);

            groupPileFactorWindow.ShowDialog(); // モーダルダイアログとして表示

            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }


        // 群杭沈下解析の実行メソッド
        [RelayCommand]
        private void PileGroupSettlementAnalysis()
        {
            // 土層が0の場合は警告を出して処理を中断
            if (CurrentInputModel.PileGroupSettlement.SettlementSoilLayers == null ||
                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Count == 0)
            {
                MessageBox.Show("群杭沈下解析用の土層が1層以上必要です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ObservableCollection<RectLoad> rectLoads = [];

            if (CurrentInputModel.PileGroupSettlement.LoadingType == "任意矩形")
            {
                rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
            }
            else if (CurrentInputModel.PileGroupSettlement.LoadingType == "個別十字")
            {
                foreach (PileLayoutDataItem pileLayoutDataItem in CurrentInputModel.PileLayoutItems)
                {
                    SoilPile soilPile = CurrentInputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                    double qa = pileLayoutDataItem.AxialForceVL0 + pileLayoutDataItem.AxialForceVLAdditional;

                    ObservableCollection<RectLoad> eachRectLoads
                        = PileGroupSettlement.GetCrossRectLoads(point, radius, qa);

                    foreach (var rectLoad in eachRectLoads)
                    {
                        rectLoads.Add(rectLoad);
                    }
                }
            }

            foreach (PileLayoutDataItem pileLayoutDataItem in CurrentInputModel.PileLayoutItems)
            {
                Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                pileLayoutDataItem.GroupPileSettlement = Steinnbrener.CalcSettlement(
                    point, rectLoads, CurrentInputModel.PileGroupSettlement.SettlementSoilLayers) * 1000;
            }

            double xMin = GroupPileSettlementXmin;
            double xMax = GroupPileSettlementXmax;
            double yMin = GroupPileSettlementYmin;
            double yMax = GroupPileSettlementYmax;
            double xOffset = GroupPileSettlementXOffset;
            double yOffset = GroupPileSettlementYOffset;
            double xSpacing = GroupPileSettlementXSpacing;
            double ySpacing = GroupPileSettlementYSpacing;

            CurrentInputModel.PileGroupSettlement.SetGridX(xMin, xMax, xOffset, xSpacing, CurrentInputModel.GridXItems);
            CurrentInputModel.PileGroupSettlement.SetGridY(yMin, yMax, yOffset, ySpacing, CurrentInputModel.GridYItems);

            ObservableCollection<double> xs = CurrentInputModel.PileGroupSettlement.SettlementGridX;
            ObservableCollection<double> ys = CurrentInputModel.PileGroupSettlement.SettlementGridY;

            CurrentInputModel.PileGroupSettlement.SettlementGridData = [];
            foreach (var x in xs)
            {
                foreach (var y in ys)
                {
                    Point point = new() { X = x, Y = y };
                    var settlement = Steinnbrener.CalcSettlement(
                        point, rectLoads, CurrentInputModel.PileGroupSettlement.SettlementSoilLayers) * 1000;
                    CurrentInputModel.PileGroupSettlement.SettlementGridData.Add(new());
                    CurrentInputModel.PileGroupSettlement.SettlementGridData[^1].X = x;
                    CurrentInputModel.PileGroupSettlement.SettlementGridData[^1].Y = y;
                    CurrentInputModel.PileGroupSettlement.SettlementGridData[^1].Settlement = settlement;
                }
            }

            MessageBox.Show("スタインブレナーの近似式による解析が終了しました。");

            IsGroupPileGridDeformationVisible = true;
            IsGroupPileSettlementAnalysisDone = true;
            //IsAnalysisResultVisible = true;
            IsBubbleVisible = true;
            IsArrowVisible = true;

        }

        // 自動前方杭設定の処理メソッド
        private void AutoIsFrontPilesWindow_AutoIsFrontPileCompleted(object sender, AutoIsFrontEventArgs e)
        {
            double cosAlpha = Math.Cos((e.Angle * Math.PI / 180.0));

            for (int i = 0; i < 4; i++)
            {
                if (e.IsChecked[i])
                {
                    foreach (PileLayoutDataItem pileLayout0 in CurrentInputModel.PileLayoutItems)
                    {
                        pileLayout0.IsFrontPiles[i] = true; // 全ての杭を前方杭として初期化

                        foreach (PileLayoutDataItem pileLayout1 in CurrentInputModel.PileLayoutItems)
                        {
                            if (pileLayout0 == pileLayout1) { continue; }

                            Point positionVector1 = new(pileLayout1.Point3D.X, pileLayout1.Point3D.Y);
                            Point positionVector0 = new(pileLayout0.Point3D.X, pileLayout0.Point3D.Y);
                            Vector directionVector = positionVector1 - positionVector0;

                            LoadCase loadCase = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];

                            // 荷重方向ベクトルを計算
                            Vector loadDirectionVector = PileDesign.Converters.VectorConverter.ConvertAngleToUnitVector(loadCase.LoadAngle);

                            // 内積を計算
                            double dotProduct = Vector.Multiply(directionVector, loadDirectionVector);

                            // ベクトルの大きさを計算
                            double magnitudeDirection = directionVector.Length;
                            double magnitudeLoadDirection = loadDirectionVector.Length;

                            // 余弦を計算
                            double cosTheta = dotProduct / (magnitudeDirection * magnitudeLoadDirection);

                            // 余弦が指定角度より大きい場合 
                            if (cosAlpha < cosTheta)
                            {
                                pileLayout0.IsFrontPiles[i] = false;
                                goto NextPileLayout0;
                            }
                        }
                    NextPileLayout0:;
                    }
                }
            }
        }

        // 名前をつけて保存
        [RelayCommand]
        public void SaveInputModelFileAs()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CurrentFilePath = saveFileDialog.FileName;
                try
                {
                    var projectData = new ProjectData
                    {
                        InputModel = this.CurrentInputModel,
                        AnaModel = this.CurrentModel // MainWindowViewModelにAnaModelがある場合
                    };
                    //var options = new JsonSerializerOptions
                    //{
                    //    WriteIndented = true,
                    //    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
                    //};
                    //string json = JsonSerializer.Serialize(projectData, options);
                    string json = JsonSerializer.Serialize(projectData, _jsonOptions);
                    File.WriteAllText(CurrentFilePath, json);
                    MessageBox.Show("保存が完了しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void SaveInputModelFile()
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
            {
                SaveInputModelFileAs();
            }
            else
            {
                try
                {
                    var projectData = new ProjectData
                    {
                        InputModel = this.CurrentInputModel,
                        AnaModel = this.CurrentModel
                    };
                    //var options = new JsonSerializerOptions
                    //{
                    //    WriteIndented = true,
                    //    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
                    //};
                    //string json = JsonSerializer.Serialize(projectData, options);
                    string json = JsonSerializer.Serialize(projectData, _jsonOptions);
                    File.WriteAllText(CurrentFilePath, json);
                    MessageBox.Show("保存が完了しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void NewInputModelFile()
        {
            var result = MessageBox.Show(
                "現在のデータを保存しますか？",
                "確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }
            else if (result == MessageBoxResult.Yes)
            {
                SaveInputModelFile();
            }

            CurrentInputModel.Reset();
            this.CurrentModel = null; // AnaModelもリセット
            CurrentFilePath = null;
            UpdateWindowAction?.Invoke();
            UpdateTreeView();
        }

        // CurrentInputModelの読込
        [RelayCommand]
        public void OpenInputModelFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                DefaultExt = "json"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    //var options = new JsonSerializerOptions
                    //{
                    //    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
                    //};
                    //string json = File.ReadAllText(openFileDialog.FileName);
                    //var projectData = JsonSerializer.Deserialize<ProjectData>(json, options);
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var projectData = JsonSerializer.Deserialize<ProjectData>(json, _jsonOptions);
                    if (projectData != null)
                    {
                        this.CurrentInputModel = projectData.InputModel;
                        this.CurrentModel = projectData.AnaModel;

                        // --- ここから修正 ---
                        // SettlementSoilLayers
                        if (CurrentInputModel?.PileGroupSettlement?.SettlementSoilLayers != null &&
                            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.GetType() != typeof(ObservableCollection<SettlementSoilLayer>))
                        {
                            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers =
                                new ObservableCollection<SettlementSoilLayer>(CurrentInputModel.PileGroupSettlement.SettlementSoilLayers);
                        }
                        // RectLoads
                        if (CurrentInputModel?.PileGroupSettlement?.RectLoads != null &&
                            CurrentInputModel.PileGroupSettlement.RectLoads.GetType() != typeof(ObservableCollection<RectLoad>))
                        {
                            CurrentInputModel.PileGroupSettlement.RectLoads =
                                new ObservableCollection<RectLoad>(CurrentInputModel.PileGroupSettlement.RectLoads);
                        }
                        if (CurrentInputModel?.PileGroupSettlement?.SettlementSoilLayers != null &&
                            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.GetType() != typeof(ObservableCollection<SettlementSoilLayer>))
                        {
                            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers =
                                new ObservableCollection<SettlementSoilLayer>(CurrentInputModel.PileGroupSettlement.SettlementSoilLayers);
                        }

                        if (CurrentInputModel?.PileGroupSettlement?.RectLoads != null &&
                            CurrentInputModel.PileGroupSettlement.RectLoads.GetType() != typeof(ObservableCollection<RectLoad>))
                        {
                            CurrentInputModel.PileGroupSettlement.RectLoads =
                                new ObservableCollection<RectLoad>(CurrentInputModel.PileGroupSettlement.RectLoads);
                        }

                        if (CurrentInputModel?.PileGroupSettlement?.SettlementGridX != null &&
                            CurrentInputModel.PileGroupSettlement.SettlementGridX.GetType() != typeof(ObservableCollection<double>))
                        {
                            CurrentInputModel.PileGroupSettlement.SettlementGridX =
                                new ObservableCollection<double>(CurrentInputModel.PileGroupSettlement.SettlementGridX);
                        }

                        if (CurrentInputModel?.PileGroupSettlement?.SettlementGridY != null &&
                            CurrentInputModel.PileGroupSettlement.SettlementGridY.GetType() != typeof(ObservableCollection<double>))
                        {
                            CurrentInputModel.PileGroupSettlement.SettlementGridY =
                                new ObservableCollection<double>(CurrentInputModel.PileGroupSettlement.SettlementGridY);
                        }

                        if (CurrentInputModel?.PileGroupSettlement?.SettlementGridData != null &&
                            CurrentInputModel.PileGroupSettlement.SettlementGridData.GetType() != typeof(ObservableCollection<SettlementGridDataItem>))
                        {
                            CurrentInputModel.PileGroupSettlement.SettlementGridData =
                                new ObservableCollection<SettlementGridDataItem>(CurrentInputModel.PileGroupSettlement.SettlementGridData);
                        }

                        if (CurrentInputModel?.PileLayoutItems != null &&
                            CurrentInputModel.PileLayoutItems.GetType() != typeof(ObservableCollection<PileLayoutDataItem>))
                        {
                            CurrentInputModel.PileLayoutItems =
                                new ObservableCollection<PileLayoutDataItem>(CurrentInputModel.PileLayoutItems);
                        }

                        if (CurrentInputModel?.Elements != null &&
                            CurrentInputModel.Elements.GetType() != typeof(ObservableCollection<Element>))
                        {
                            CurrentInputModel.Elements =
                                new ObservableCollection<Element>(CurrentInputModel.Elements);
                        }

                        if (CurrentInputModel?.GridXItems != null &&
                            CurrentInputModel.GridXItems.GetType() != typeof(ObservableCollection<GridDataItem>))
                        {
                            CurrentInputModel.GridXItems =
                                new ObservableCollection<GridDataItem>(CurrentInputModel.GridXItems);
                        }

                        if (CurrentInputModel?.GridYItems != null &&
                            CurrentInputModel.GridYItems.GetType() != typeof(ObservableCollection<GridDataItem>))
                        {
                            CurrentInputModel.GridYItems =
                                new ObservableCollection<GridDataItem>(CurrentInputModel.GridYItems);
                        }

                        if (CurrentInputModel?.PileBodies != null &&
                            CurrentInputModel.PileBodies.GetType() != typeof(ObservableCollection<PileBodyInput>))
                        {
                            CurrentInputModel.PileBodies =
                                new ObservableCollection<PileBodyInput>(CurrentInputModel.PileBodies);
                        }

                        if (CurrentInputModel?.GroundsInput != null &&
                            CurrentInputModel.GroundsInput.GetType() != typeof(ObservableCollection<GroundInput>))
                        {
                            CurrentInputModel.GroundsInput =
                                new ObservableCollection<GroundInput>(CurrentInputModel.GroundsInput);
                        }

                        if (CurrentInputModel?.EmbedmentInput?.EmbedmentLayers != null &&
                            CurrentInputModel.EmbedmentInput.EmbedmentLayers.GetType() != typeof(ObservableCollection<EmbedmentDataItem>))
                        {
                            CurrentInputModel.EmbedmentInput.EmbedmentLayers =
                                new ObservableCollection<EmbedmentDataItem>(CurrentInputModel.EmbedmentInput.EmbedmentLayers);
                        }

                        if (CurrentInputModel?.LoadCasesInput?.LoadCasesLevel1 != null &&
                            CurrentInputModel.LoadCasesInput.LoadCasesLevel1.GetType() != typeof(ObservableCollection<LoadCase>))
                        {
                            CurrentInputModel.LoadCasesInput.LoadCasesLevel1 =
                                new ObservableCollection<LoadCase>(CurrentInputModel.LoadCasesInput.LoadCasesLevel1);
                        }

                        if (CurrentInputModel?.LoadCasesInput?.LoadCasesLevel2 != null &&
                            CurrentInputModel.LoadCasesInput.LoadCasesLevel2.GetType() != typeof(ObservableCollection<LoadCase>))
                        {
                            CurrentInputModel.LoadCasesInput.LoadCasesLevel2 =
                                new ObservableCollection<LoadCase>(CurrentInputModel.LoadCasesInput.LoadCasesLevel2);
                        }

                        // ネストされたコレクション
                        if (CurrentInputModel?.GroundsInput != null)
                        {
                            foreach (var ground in CurrentInputModel.GroundsInput)
                            {
                                if (ground.GroundLayers != null &&
                                    ground.GroundLayers.GetType() != typeof(ObservableCollection<GroundLayerInput>))
                                {
                                    ground.GroundLayers = new ObservableCollection<GroundLayerInput>(ground.GroundLayers);
                                }
                                if (ground.GroundMassesData != null &&
                                    ground.GroundMassesData.GetType() != typeof(ObservableCollection<GroundMassDataInput>))
                                {
                                    ground.GroundMassesData = new ObservableCollection<GroundMassDataInput>(ground.GroundMassesData);
                                }
                            }
                        }

                        if (CurrentInputModel?.PileBodies != null)
                        {
                            foreach (var pileBody in CurrentInputModel.PileBodies)
                            {
                                if (pileBody.PileBodySegments != null &&
                                    pileBody.PileBodySegments.GetType() != typeof(ObservableCollection<PileBodySegment>))
                                {
                                    pileBody.PileBodySegments = new ObservableCollection<PileBodySegment>(pileBody.PileBodySegments);
                                }
                            }
                        }

                        // 他にも必要なコレクションがあれば同様に包み直す
                        // --- ここまで修正 ---

                        // 必要ならプロパティ変更通知
                        OnPropertyChanged(nameof(CurrentInputModel));
                    }
                    // フォールバック: null の場合は空コレクションで初期化
                    CurrentInputModel.GridXItems ??= [];
                    CurrentInputModel.GridYItems ??= [];

                    // VM 再アタッチ
                    CurrentInputModel.AttachViewModel(this);

                    UpdateWindowAction?.Invoke();
                    MessageBox.Show("読込が完了しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"読込に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Word ファイルに保存するメソッド
        [RelayCommand]
        public void OutputWordFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*",
                FileName = "document_" + DateTime.Now.ToString("yyMMdd_HHmmss") + ".docx"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var doc = new Output.WordDocument(CurrentInputModel, CurrentModel, this);

                doc.CreateWordDocument(CurrentInputModel, saveFileDialog.FileName);
                //Output.WordDocument.CreateWordDocument(CurrentInputModel, saveFileDialog.FileName);
                MessageBox.Show($"docsファイルが作成されました。\n{saveFileDialog.FileName}\nMSWordでファイルを開き、ctrl + aで全選択した後, F9によりフィールドを更新してください。");
            }
        }

        // プロパティのコピー
        private static void CopyProperties(object source, object destination)
        {
            if (source == null || destination == null)
            {
                throw new ArgumentNullException(nameof(source), "Source or destination cannot be null.");
            }

            Type sourceType = source.GetType();
            Type destinationType = destination.GetType();

            PropertyInfo[] properties = sourceType.GetProperties();

            foreach (PropertyInfo property in properties)
            {
                PropertyInfo destinationProperty = destinationType.GetProperty(property.Name);
                if (destinationProperty != null && property.CanRead && destinationProperty.CanWrite)
                {
                    object value = property.GetValue(source);
                    destinationProperty.SetValue(destination, value);
                }
            }
        }
        // 計算書出力ウィンドウ表示メソッド
        [RelayCommand]
        private void OpenDocxOutputWindow()
        {
            var dockxOutputOptionWindow = new DocxOutputWindow(this);
            dockxOutputOptionWindow.Show();
        }

        // オプション表示メソッド
        [RelayCommand]
        private static void OpenOptionWindow()
        {
            var optionWindow = new OptionWindow();
            optionWindow.Show();
        }

        // 解析結果が1つでも存在するか
        private bool HasAnyAnalysisResult()
            => IsHorizontalAnalysisDone || IsVerticalAnalysisDone || IsGroupPileSettlementAnalysisDone;

        // コマンド状態一括更新ヘルパ
        private void RaiseResultCommandsCanExecute()
        {
            if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
            OpenGraphWindowCommand?.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanOpenGraphWindow))]
        private void OpenGraphWindow()
        {
            // CanExecute で制御されるため通常は不要だが保険として
            if (!HasAnyAnalysisResult()) return;

            // MainWindowViewModelのインスタンス(this)を必ず渡す
            var viewModel = new GraphViewModel(this)
            {
                IsHorizontalAnalysisDone = this.IsHorizontalAnalysisDone,
                IsVerticalAnalysisDone = this.IsVerticalAnalysisDone,
                IsGroupPileSettlementAnalysisDone = this.IsGroupPileSettlementAnalysisDone
            };
            viewModel.Initialize();

            var graphWindow = new GraphWindow(viewModel);
            graphWindow.Show();
        }
        private bool CanOpenGraphWindow() => HasAnyAnalysisResult();


        // フィールド追加
        private readonly AnalysisResultTableService _tableService = new();

        // プロパティ
        public IReadOnlyList<ResultTable> LatestResultTables { get; private set; } = [];

        // 解析完了後 (既存処理内末尾に追加)
        private void OnAnalysisFinished(AnalysisStepResult result)
        {
            // AnaModel が未セットなら結果テーブル生成をスキップ
            if (CurrentModel == null)
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                RaiseResultCommandsCanExecute();
                return;
            }

            LatestResultTables = _tableService.BuildTables(
                CurrentModel,
                result.LoadCase,
                result.LoadCombination,
                result.IsLiquefaction,
                result.Step);

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

        public ICommand OpenTableWindowCommand { get; private set; }

        private void OpenTableWindow()
        {
            var vm = new TableWindowViewModel();
            vm.LoadTables(LatestResultTables);
            var w = new Views.TableWindow { DataContext = vm };
            w.Show();
        }

        // 解析結果テーブル再生成
        public void RefreshResultTablesFromLastStep()
        {
            // AnaModel または AnalysisStepResults が null/空の場合は早期リターン
            if (CurrentModel == null || 
                CurrentModel.AnalysisStepResults == null || 
                CurrentModel.AnalysisStepResults.Count == 0 ||
                !HasAnyAnalysisResult())
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
                return;
            }

            // 最終ステップ結果を取得
            var last = CurrentModel.AnalysisStepResults.LastOrDefault();
            if (last == null)
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                RaiseResultCommandsCanExecute();
                return;
            }

            LatestResultTables = _tableService.BuildTables(
                CurrentModel,
                last.LoadCase,
                last.LoadCombination,
                last.IsLiquefaction,
                last.Step);

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

        // ヘルプメウィンドウ表示メソッド
        [RelayCommand]
        public static void OpenHelpWindow()
        {
            var helpWindow = new HelpWindow();
            helpWindow.Show();
        }

        [RelayCommand]
        public void OnQuickHint()
        {
            IsQuickHintVisible = true;
        }

        // 再入防止フラグ（原子的に扱う）
        private int _isChangWindowOpeningFlag = 0;

        [RelayCommand]
        public void OpenChangWindow()
        {
            // ChangViewModel に現在の InputModel を注入して作成
            var vm = new ChangViewModel(CurrentInputModel);
            //var win = new ChangWindow();
            var win = new ChangWindow { DataContext = vm };

            // イベントハンドラを設定
            if (vm is ICloseable closeableViewModel)
            {
                if (win.IsLoaded && win.IsVisible)
                {
                    win.Close();
                }
            }

            try
            {
                // ★ 重要: ダイアログを開く前に現在のフォーカスをクリア
                // これにより IME/TextStore が解放され、COMException を回避できる
                var focusedElement = Keyboard.FocusedElement;
                if (focusedElement is TextBox)
                {
                    // フォーカスを MainWindow に移動
                    Application.Current.MainWindow?.Focus();

                    // Dispatcher で UI を更新して IME を解放する時間を与える
                    Application.Current.Dispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => { }));
                }

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdateWindowAction?.Invoke();
            UpdateTreeView();
        }

        [RelayCommand]
        public void OpenPileSectionLibraryWindow()
        {
            try
            {
                var win = new PileDesign.Views.PileLibraryWindow
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"杭ライブラリ表示に失敗しました: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        //{
        //    // 0 -> 1 にできなければ既に実行中とみなす
        //    if (System.Threading.Interlocked.CompareExchange(ref _isChangWindowOpeningFlag, 1, 0) != 0)
        //    {
        //        Debug.WriteLine("OpenChangWindow: already opening, ignored.");
        //        return;
        //    }

        //    try
        //    {
        //        Debug.WriteLine("OpenChangWindow: start");

        //        // ChangViewModel に現在の InputModel を注入してウィンドウを生成・表示
        //        var vm = new ChangViewModel(this.CurrentInputModel);
        //        var win = new ChangWindow
        //        {
        //            DataContext = vm,
        //            Owner = Application.Current.MainWindow,
        //            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        //            ShowInTaskbar = false
        //        };

        //        // ViewModel から閉じる要求を受け取る場合があるなら購読（インターフェイス名は環境に合わせて）
        //        if (vm is ICloseable closeableVm)
        //        {
        //            closeableVm.RequestClose += (s, e) =>
        //            {
        //                if (win.IsVisible) win.Close();
        //            };
        //        }

        //        // モーダル表示（既存コードに合わせる）
        //        try
        //        {
        //            win.ShowDialog();
        //        }
        //        catch (Exception ex)
        //        {
        //            MessageBox.Show($"Changウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        //            Debug.WriteLine(ex);
        //        }
        //    }
        //    finally
        //    {
        //        System.Threading.Interlocked.Exchange(ref _isChangWindowOpeningFlag, 0);
        //        Debug.WriteLine("OpenChangWindow: end");
        //    }
        //}
        //{
        //    var vm = new ChangViewModel();
        //    //vm.LoadSample();
        //    var win = new ChangWindow { DataContext = vm };
        //    win.ShowDialog();
        //}

        //[RelayCommand]
        //private void DeleteAnalysisResults()
        //{
        //    var result = MessageBox.Show(
        //        "解析結果を削除します。よろしいですか？",
        //        "確認",
        //        MessageBoxButton.YesNo,
        //        MessageBoxImage.Question);
        //    if (result == MessageBoxResult.Yes)
        //    {
        //        // 解析結果の削除
        //        IsHorizontalAnalysisDone = false;
        //        IsVerticalAnalysisDone = false;
        //        IsGroupPileSettlementAnalysisDone = false;
        //        if (CurrentInputModel != null)
        //        {
        //            //foreach (var pile in CurrentInputModel.PileLayoutItems)
        //            //{
        //            //    pile.ResetAnalysisResults();
        //            //}
        //        }
        //        CurrentModel = null; // AnaModelもリセット
        //        UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
        //        UpdateTreeView();
        //    }
        //}

        [RelayCommand]
        public static void OpenShortcutKeysWindow()
        {
            var w = new PileDesign.Views.ShortcutKeysWindow
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            w.ShowDialog();
        }

        // データ入力ウィンドウ表示メソッド
        //[RelayCommand]
        //private void OpenInputDataAnchorable()
        //{
        //    InputDataAnchorable.Show();
        //}

        [RelayCommand]
        private async void MoveCopyPiles()
        {
            // 選択節点がない場合は処理を中止してメッセージ表示
            if (CurrentInputModel == null ||
                CurrentInputModel.PileLayoutItems == null ||
                !CurrentInputModel.PileLayoutItems.Any(p => p.IsSelected))
            {
                MessageBox.Show("杭配置が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // MoveWindowをインスタンス化して表示
            MoveCopyWindow moveCopyWindow = new();

            var tcs = new TaskCompletionSource<bool>();
            moveCopyWindow.MoveCopyCompleted += async (sender, e) =>
            {
                await MoveCopyWindow_MoveCopyCompletedAsync(sender, e);
                tcs.SetResult(true);
            };

            moveCopyWindow.ShowDialog(); // モーダルダイアログとして表示

            // ★ 待機カーソルを表示
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await tcs.Task; // 非同期に完了を待つ

                // コレクション自体の変更通知
                OnPropertyChanged(nameof(GroupPileSettlementXmin));
                OnPropertyChanged(nameof(GroupPileSettlementXmax));
                OnPropertyChanged(nameof(GroupPileSettlementYmin));
                OnPropertyChanged(nameof(GroupPileSettlementYmax));

                UpdateWindowAction?.Invoke();
                UpdateTreeView();
            }
            finally
            {
                // ★ カーソルを元に戻す
                Mouse.OverrideCursor = null;
            }
        }

        private async Task MoveCopyWindow_MoveCopyCompletedAsync(object sender, MoveCopyEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsMove)
            {
                // 移動操作の処理
                MoveNodes(e.DX, e.DY);
            }
            else if (e.IsCopy)
            {
                // 複製操作の処理
                await CopyNodesAsync(e.DX, e.DY, e.RepetitionNumber);
            }
        }

        private async Task CopyNodesAsync(double dX, double dY, int repetitionNumber)
        {
            // 変更を行う前に、選択されたアイテムのリストを作成
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            int totalCount = selectedItems.Count * repetitionNumber;

            // ★ 大量コピー時は待機カーソルを表示
            bool showWaitCursor = totalCount > 10;
            if (showWaitCursor)
            {
                Mouse.OverrideCursor = Cursors.Wait;
            }

            try
            {
                // ★ 一時リストに事前にすべて作成（容量を事前確保して高速化）
                var newItems = new List<PileLayoutDataItem>(totalCount);

                foreach (var pilelocation in selectedItems)
                {
                    for (int i = 0; i < repetitionNumber; i++)
                    {
                        var newItem = new PileLayoutDataItem
                        {
                            X = pilelocation.X + dX * (i + 1),
                            Y = pilelocation.Y + dY * (i + 1)
                        };
                        newItems.Add(newItem);
                    }
                    pilelocation.IsSelected = false;
                }

                // ★ UIスレッドで一括追加
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 通知を一時抑制
                    CurrentInputModel.SuppressNotifications();
                    try
                    {
                        // ★ 一括追加（SetMainWindowViewModel も含めて処理）
                        foreach (var item in newItems)
                        {
                            item.SetMainWindowViewModel(this);
                            CurrentInputModel.PileLayoutItems.Add(item);
                        }
                    }
                    finally
                    {
                        // 通知を再開し、SoilPiles を1回だけ再生成
                        CurrentInputModel.ResumeAndNotify();
                    }

                    UpdatePileLayoutNo();
                    UpdateWindowAction?.Invoke();
                    UpdateTreeView();
                });
            }
            finally
            {
                if (showWaitCursor)
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        // 移動操作を行う
        private void MoveNodes(double dX, double dY)
        {
            foreach (PileLayoutDataItem pilelocation in CurrentInputModel.PileLayoutItems)
            {
                if (pilelocation.IsSelected)
                {
                    pilelocation.X += dX;
                    pilelocation.Y += dY;
                    pilelocation.IsSelected = false;
                }
            }
        }

        // コピーを作成して操作を行う
        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            // 変更を行う前に、選択されたアイテムのリストを作成
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(pilelocation => pilelocation.IsSelected).ToList();

            // 通知を一時抑制して高速化
            CurrentInputModel.SuppressNotifications();
            try
            {
                foreach (PileLayoutDataItem pilelocation in selectedItems)
                {
                    for (int i = 0; i < repetitionNumber; i++)
                    {
                        // コピーしたコレクションに新しい要素を追加
                        CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem()
                        {
                            X = pilelocation.X + dX * (i + 1),
                            Y = pilelocation.Y + dY * (i + 1)
                        });
                        CurrentInputModel.PileLayoutItems[^1].SetMainWindowViewModel(this);
                    }
                    pilelocation.IsSelected = false;
                }
            }
            finally
            {
                // 通知を再開し、SoilPiles を1回だけ再生成
                CurrentInputModel.ResumeAndNotify();
            }

            UpdatePileLayoutNo();
        }

        // 杭配置の編集・追加コマンド
        [RelayCommand]
        private void EditAddPiles()
        {
            var editPileLayoutWindow = new EditPileLayoutWindow(this);

            editPileLayoutWindow.EditPileLayoutCompleted += EditPileLayoutWindow_EditPileLayoutCompleted;

            editPileLayoutWindow.ShowDialog();
            UpdateWindowAction?.Invoke();
        }

        private void EditPileLayoutWindow_EditPileLayoutCompleted(object sender, EditPileLayoutEventArgs e)
        {
            ObservableCollection<PileLayoutDataItem> selectedItems = [];

            foreach (PileLayoutDataItem pilelocation in CurrentInputModel.PileLayoutItems)
            {
                if (pilelocation.IsSelected)
                {
                    selectedItems.Add(pilelocation);
                }
            }

            if (e.IsApplicablePileRefNo)
                foreach (var selectedItem in selectedItems)
                    selectedItem.PileBodyNo = e.SelectedPileRefNo;

            if (e.IsApplicableGroundRefNo)
                foreach (var selectedItem in selectedItems)
                    selectedItem.GroundNo = e.SelectedGroundRefNo;

            if (e.IsApplicablePileTopLevel)
            {
                bool isAdd = e.IsAddPileTopLevel;
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.X = selectedItem.Point3D.X;
                    selectedItem.Y = selectedItem.Point3D.Y;
                    selectedItem.Z = isAdd ? selectedItem.Point3D.Z + e.PileTopLevel : e.PileTopLevel;
                }
            }

            if (e.IsApplicablePileGroupFactor)
            {
                bool isAdd = e.IsAddPileGroupFactor;
                foreach (var selectedItem in selectedItems)
                    selectedItem.GroupPileFactor = isAdd ? selectedItem.GroupPileFactor + e.PileGroupFactor : e.PileGroupFactor;
            }

            if (e.IsApplicableVL)
            {
                bool isAdd = e.IsAddVL;
                foreach (var selectedItem in selectedItems)
                    selectedItem.AxialForceVL0 = isAdd ? selectedItem.AxialForceVL0 + e.VL : e.VL;
            }

            if (e.IsApplicableVLadd)
            {
                bool isAdd = e.IsAddVLadd;
                foreach (var selectedItem in selectedItems)
                    selectedItem.AxialForceVLAdditional = isAdd ? selectedItem.AxialForceVLAdditional + e.VLadd : e.VLadd;
            }

            // E1_1 ~ E2_4 の処理（簡略化）
            Action<bool, bool, double, int, bool> applyLevel1 = (applicable, isAdd, val, idx, _) =>
            {
                if (!applicable) return;
                foreach (var item in selectedItems)
                    item.AxialForceLevel1s[idx] = isAdd ? item.AxialForceLevel1s[idx] + val : val;
            };

            Action<bool, bool, double, int, bool> applyLevel2 = (applicable, isAdd, val, idx, _) =>
            {
                if (!applicable) return;
                foreach (var item in selectedItems)
                    item.AxialForceLevel2s[idx] = isAdd ? item.AxialForceLevel2s[idx] + val : val;
            };

            applyLevel1(e.IsApplicableE1_1, e.IsAddE1_1, e.E1_1, 0, false);
            applyLevel1(e.IsApplicableE1_2, e.IsAddE1_2, e.E1_2, 1, false);
            applyLevel1(e.IsApplicableE1_3, e.IsAddE1_3, e.E1_3, 2, false);
            applyLevel1(e.IsApplicableE1_4, e.IsAddE1_4, e.E1_4, 3, false);

            applyLevel2(e.IsApplicableE2_1, e.IsAddE2_1, e.E2_1, 0, false);
            applyLevel2(e.IsApplicableE2_2, e.IsAddE2_2, e.E2_2, 1, false);
            applyLevel2(e.IsApplicableE2_3, e.IsAddE2_3, e.E2_3, 2, false);
            applyLevel2(e.IsApplicableE2_4, e.IsAddE2_4, e.E2_4, 3, false);

            if (e.IsApplicableIsFrontPile1)
                foreach (var item in selectedItems)
                    item.IsFrontPiles[0] = e.IsFrontPile1;
            if (e.IsApplicableIsFrontPile2)
                foreach (var item in selectedItems)
                    item.IsFrontPiles[1] = e.IsFrontPile2;
            if (e.IsApplicableIsFrontPile3)
                foreach (var item in selectedItems)
                    item.IsFrontPiles[2] = e.IsFrontPile3;
            if (e.IsApplicableIsFrontPile4)
                foreach (var item in selectedItems)
                    item.IsFrontPiles[3] = e.IsFrontPile4;
        }

        [RelayCommand]
        private void Undo()
        {
            _undoManager.Undo();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);
                UpdateWindowAction?.Invoke();
                UpdateTreeView();
                OnPropertyChanged(nameof(CurrentInputModel));
            }
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.Redo();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);
                UpdateWindowAction?.Invoke();
                UpdateTreeView();
                OnPropertyChanged(nameof(CurrentInputModel));
            }
        }

        private void RemoveElementsContainingPileLayoutItem(PileLayoutDataItem oldItem)
        {
            var elementsToRemove = CurrentInputModel.Elements.Where(element => element.Nodes.Contains(oldItem)).ToList();

            foreach (var element in elementsToRemove)
            {
                CurrentInputModel.Elements.Remove(element);
            }

            UpdateWindowAction?.Invoke();
        }

        public void DeleteDuplicatedPiles()
        {
            var uniquePileLayoutDataItems = new ObservableCollection<PileLayoutDataItem>();

            foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
            {
                bool isDuplicate = uniquePileLayoutDataItems.Any(existingItem =>
                    existingItem.X == pileLayoutItem.X &&
                    existingItem.Y == pileLayoutItem.Y &&
                    existingItem.Z == pileLayoutItem.Z);

                if (!isDuplicate)
                {
                    uniquePileLayoutDataItems.Add(pileLayoutItem);
                }
            }

            CurrentInputModel.PileLayoutItems = uniquePileLayoutDataItems;
            UpdateWindowAction?.Invoke();
        }

        public void DeleteDuplicatedElements()
        {
            // 重複要素の削除ロジック
        }

        // ウィンドウを開くメソッド
        private void OpenDialogWindow<TViewModel, TWindow>(MainWindowViewModel mainWindowViewModel)
            where TViewModel : ObservableObject
            where TWindow : Window, new()
        {
            var focusedElement = Keyboard.FocusedElement;
            if (focusedElement is TextBox)
            {
                Application.Current.MainWindow?.Focus();
                Application.Current.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
            }

            var viewModel = (TViewModel)Activator.CreateInstance(typeof(TViewModel), mainWindowViewModel);
            var window = new TWindow { DataContext = viewModel };

            var appMain = Application.Current?.MainWindow;
            if (appMain != null)
            {
                try { window.Owner = appMain; }
                catch { }
            }

            window.ShowDialog();
            UpdateWindowAction?.Invoke();
            UpdateTreeView();
        }

        // 基本設定ウィンドウを開くメソッド
        [RelayCommand]
        private void OpenFundamentalWindow()
        {
            OpenDialogWindow<FundamentalViewModel, FundamentalWindow>(this);
        }

        // 荷重条件ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenLoadCaseWindow()
        {
            OpenDialogWindow<LoadCaseViewModel, LoadCaseWindow>(this);
            UpdateLoadCaseOption();
            UpdateLoadCombinationOption();
        }

        // 地盤ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenGroundWindow()
        {
            OpenDialogWindow<GroundLayerViewModel, GroundWindow>(this);
        }

        // 杭体ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenPileBodyWindow()
        {
            OpenDialogWindow<PileBodyViewModel, PileBodyWindow>(this);
        }

        // 軸力チェック
        [RelayCommand]
        public void OnAxialForceCheck()
        {
            if (CurrentInputModel == null || CurrentInputModel.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageBox.Show("杭配置が存在しません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasWarning = false;
            string warningMessage = "以下の項目に問題があります:\n";

            foreach (var pileLayout in CurrentInputModel.PileLayoutItems)
            {
                var force = pileLayout.AxialForceVL;

                for (int i = 0; i < CurrentInputModel.PileBodies[pileLayout.PileBodyNo - 1].PileBodySegments.Count; i++)
                {
                    var pileSection = CurrentInputModel.PileBodies[pileLayout.PileBodyNo - 1].PileBodySegments[i].PileSection;

                    if (pileSection.FactoredServiceNmax < force)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{i + 1} 荷重ケース:VL:\n 使用限界軸力適用範囲Max{pileSection.FactoredServiceNmax:N0}kN < {force:N0}kN\n";
                    }
                    if (force < pileSection.FactoredServiceNmin)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{i + 1} 荷重ケース:VL:\n {force:N0}kN < 使用限界軸力適用範囲Min{pileSection.FactoredServiceNmin:N0}kN\n";
                    }
                }
            }

            if (hasWarning)
            {
                MessageBox.Show(warningMessage, "警告", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("各杭配置の軸力は各断面の軸力適用範囲内です。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // 要素分割ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenElementDivisionWindow()
        {
            if (IsPreparedForAnalysis())
            {
                CurrentInputModel.GenerateSoilPiles();
                CurrentInputModel.GenerateSoilEmbedment();

                var window = new ElementDivisionWindow(this);
                window.ShowDialog();

                UpdateCanvas3DAction?.Invoke();
                UpdateTreeView();
            }
        }

        // 沈下ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenSettlementWindow()
        {
            if (IsPreparedForAnalysis())
            {
                if (CurrentInputModel.ElementDivision.SoilPiles == null || CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    MessageBox.Show("杭配置が存在しません。");
                    return;
                }
                else
                {
                    if (IsElementSplit == false)
                    {
                        System.Windows.MessageBox.Show("要素分割を行ってください。");
                    }
                    else
                    {
                        OpenDialogWindow<SettlementViewModel, SettlementWindow>(this);
                    }
                }
            }
        }

        // 水平荷重解析ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenLateralLoadAnalysisWindow()
        {
            if (IsPreparedForAnalysis())
            {
                if (CurrentInputModel.ElementDivision.SoilPiles == null || CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    MessageBox.Show("杭配置が存在しません。");
                    return;
                }
                else
                {
                    if (IsElementSplit == false)
                    {
                        System.Windows.MessageBox.Show("要素分割を行ってください。");
                    }
                    else
                    {
                        var viewModel = new HorizontalCalculationViewModel(this);
                        var window = new HorizontalCalculationWindow { DataContext = viewModel };

                        if (viewModel is ICloseable closeableViewModel)
                        {
                            if (window.IsLoaded && window.IsVisible)
                            {
                                window.Close();
                            }
                        }

                        try
                        {
                            window.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }

                        UpdateWindowAction?.Invoke();
                        UpdateTreeView();
                    }
                }
            }
        }

        // 解析準備ができているかを確認するメソッド
        private bool IsPreparedForAnalysis()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                System.Windows.MessageBox.Show("杭配置が存在しません。");
                return false;
            }
            return true;
        }

        [RelayCommand]
        private void UpdateView()
        {
            UpdateCanvas3DAction?.Invoke();
        }

        [RelayCommand]
        private void GroundInputCopyToSettlementGroundLayers()
        {
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            if (SelectedGroundInputModelNo == null || SelectedGroundInputModelNo == 0)
            {
                MessageBox.Show("地盤データが存在しません。");
                return;
            }

            var groundInput = CurrentInputModel.GroundsInput[SelectedGroundInputModelNo - 1];
            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Clear();

            double loadingPlaneAltitude = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude;

            foreach (var layer in groundInput.GroundLayers)
            {
                if (layer.BottomAltitude < loadingPlaneAltitude)
                {
                    double poissonsRatio = 0.33;
                    switch (layer.GranularityClass)
                    {
                        case "粘性土":
                            poissonsRatio = 0.4;
                            break;
                        case "砂質土":
                        case "礫質土":
                            poissonsRatio = 0.3;
                            break;
                    }

                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Add(new SettlementSoilLayer
                    {
                        BottomAltitude = layer.BottomAltitude,
                        Ek = layer.Es,
                        PoissonsRatio = poissonsRatio,
                        Thickness = 0
                    });
                }
            }

            for (int i = 0; i < CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Count; i++)
            {
                if (i == 0)
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers[i].Thickness =
                        loadingPlaneAltitude - CurrentInputModel.PileGroupSettlement.SettlementSoilLayers[i].BottomAltitude;
                else
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers[i].Thickness =
                        CurrentInputModel.PileGroupSettlement.SettlementSoilLayers[i - 1].BottomAltitude -
                        CurrentInputModel.PileGroupSettlement.SettlementSoilLayers[i].BottomAltitude;
            }

            UpdateWindowAction?.Invoke();
        }
    }
}
