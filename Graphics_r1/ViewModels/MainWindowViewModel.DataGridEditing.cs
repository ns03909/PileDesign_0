using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

using Serilog;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel — 表の編集を受ける処理。
    ///
    /// 画面の各表 (杭配置・軸力・前後方杭・一般節点・根入部・杭要素・矩形荷重・
    /// 沈下地層) で、セルの編集が終わったとき / 編集が始まるときに呼ばれる。
    ///
    /// <b>編集を受けたら、要素分割の状態を見直すこと。</b> 杭の位置や本数を変えると
    /// 分割済みの結果と辻褄が合わなくなる。<see cref="CheckAndResetElementSplit"/> が
    /// 判断する。ここを通さずに書き換えると、古い分割のまま解析が走る。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // イベントの宣言
        public event EventHandler<DataGridCellEditEndingEventArgs> DataGridSettlementSoilLayersCellEditEnding;

        // イベントを発火するメソッド
        public virtual void OnDataGridSettlementSoilLayersCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            DataGridSettlementSoilLayersCellEditEnding?.Invoke(this, e);
        }

        private ICommand _dataGridSettlementSoilLayersCellEditEndingCommand;
        private Action zoomFitAction;

        public ICommand DataGridSettlementSoilLayersCellEditEndingCommand
        {
            get
            {
                _dataGridSettlementSoilLayersCellEditEndingCommand ??= new RelayCommand<DataGridCellEditEndingEventArgs>(OnDataGridSettlementSoilLayersCellEditEnding);
                return _dataGridSettlementSoilLayersCellEditEndingCommand;
            }
        }

        public Action? ZoomFitAction { get => zoomFitAction; set => zoomFitAction = value; }
        public Action<double, double>? AnimateViewAnglesAction { get; set; }
        public Action? ActivateSettlementSoilTabAction { get; set; }

        /// <summary>
        /// トースト通知を表示するデリゲート（code-behind で設定）
        /// type: 0=Success, 1=Info, 2=Warning
        /// </summary>
        public Action<string, int>? ShowToastAction { get; set; }

        /// <summary>
        /// トースト通知を表示します
        /// </summary>
        public void ShowToast(string message, int type = 0) => ShowToastAction?.Invoke(message, type);


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
                            MessageService.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        // 杭配置更新時更新メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            // 反復解析結果が保存されている場合は警告 (杭位置の変更で結果無効化)
            if (e.EditAction == DataGridEditAction.Commit
                && !ConfirmAnalysisConditionChange("反復", "杭配置編集"))
            {
                e.Cancel = true;
                return;
            }

            HandleDataGridCellEditEnding(e, () =>
            {
                IsElementSplit = false;
                RequestGenerateSoilPiles();

                // コレクション自体の変更通知
                OnPropertyChanged(nameof(GroupPileSettlementXMin));
                OnPropertyChanged(nameof(GroupPileSettlementXMax));
                OnPropertyChanged(nameof(GroupPileSettlementYMin));
                OnPropertyChanged(nameof(GroupPileSettlementYMax));
            });
        }

        // 杭軸力更新時更新メソッド
        // Undo はセル単位 (デバウンスなし) — Ctrl+Z で 1 セルずつ巻き戻し可能。
        // Phase D-2 のハイブリッド手書き Clone により DeepCopy は ~25ms と高速、セル単位でも体感無感。
        [RelayCommand]
        private void DataGridPileAxialForce_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // 反復解析 (群杭沈下「反復」ルート) の CaseRecord 確認
                if (!ConfirmAnalysisConditionChange("反復", "杭軸力編集"))
                {
                    e.Cancel = true;
                    return;
                }
                // 水平/単杭/基礎梁考慮鉛直 解析結果も同じ理由で陳腐化するため
                // 編集確定前にユーザー確認の上クリアする (要素分割は保持)。
                if (!CheckAndResetAnalysisResultsKeepingSplit("杭軸力編集"))
                {
                    e.Cancel = true;
                    return;
                }
            }

            HandleDataGridCellEditEnding(e);

            // ΣV は全杭配置の地震時軸力の合計。荷重ケース側から見ると外の値なので、
            // 編集したここから知らせないと、水平解析ウィンドウの ΣV / ΣH/ΣV 列が
            // 古いまま残る。
            if (CurrentInputModel?.LoadCasesInput?.AllSeismicLoadCases != null)
            {
                foreach (var lc in CurrentInputModel.LoadCasesInput.AllSeismicLoadCases)
                    lc?.RaiseForceSummaryChanged();
            }
        }

        // 前後杭更新メソッド
        [RelayCommand]
        private void DataGridIsFrontPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e);
        }

        // 一般節点更新メソッド
        [RelayCommand]
        private void DataGridInputNodes_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e);
        }

        // 杭配置表編集開始時メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (!CheckAndResetElementSplit("杭配置"))
                e.Cancel = true;
        }

        // 杭要素分割解除確認メソッド
        public bool CheckAndResetElementSplit(string text)
        {
            if (IsElementSplit == true)
            {
                MessageBoxResult result = MessageService.Show(
                    $"{text}を編集、確定するには、入力済みの杭要素分割および、" +
                    $"\n解析結果が存在する場合は解析結果を削除する必要があります。" +
                    $"\nよろしいですか。",
                    "確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                    return false;
                else
                {
                    // 群杭沈下・結果セット・入力モデル内の沈下結果まで含めて捨てる。
                    // フラグ 4 つだけ消していた頃は、消えたはずの結果が残っていた。
                    ClearAllAnalysisState(includeElementSplit: true);
                    // 変更後（以下の箇所で適用）
                    RequestUpdateWindow();
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
                return;
            }
            SaveUndoState("根入部 区分数切替");
        }
        [RelayCommand]
        private void ComboBoxEmbedmentGroundNo_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Handled = true;
                return;
            }
            SaveUndoState("根入部 地盤番号切替");
        }
        [RelayCommand]
        private void TextBoxBottomAltitude_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
            {
                e.Handled = true;
                return;
            }
            SaveUndoState("根入部 下端Z変更");
        }
        [RelayCommand]
        private void DataGridEmbedment_OnBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
                e.Cancel = true;
        }
        [RelayCommand]
        private static void ButtonGround_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }
        [RelayCommand]
        private static void ButtonPileBody_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
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
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers.RemoveAt(i);

                // Add new rows only if selectedValue is greater than the current collection size
                for (int i = currentCollectionSize; i < selectedValue; i++)
                {
                    EmbedmentDataItem newItem = CreateNewEmbedmentDataItem(i, currentCollectionSize);
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers.Add(newItem);
                }

                UpdateEmbedment();
                // 変更後（以下の箇所で適用）
                NotifyUIChanged();
            }
        }
        [RelayCommand]
        private void TextBoxAltitude_OnTextChanged(TextChangedEventArgs e)
        {
            UpdateEmbedment();
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
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
            HandleDataGridCellEditEnding(e, () => UpdateEmbedment());
        }
        [RelayCommand]
        private void DataGridSoilPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e, () =>
            {
                // GroupPileLoadDia 等の編集後、個別十字系なら矩形荷重を再生成
                RebuildAutoCrossRectLoadsIfNeeded();
            });
        }

        // 根入部データグリッド更新メソッド
        public void UpdateEmbedment()
        {
            // EmbedmentCollection の更新
            for (int i = CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1; i >= 0; i--)
            {
                if (i == CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1)
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.BottomAltitude;
                else
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i + 1].TopAltitude;
                CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].TopAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude
                    + CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].LayerThickness;
            }
        }
        [RelayCommand]
        private void DataGridRectLoads_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (e.EditAction == DataGridEditAction.Commit
                && !ConfirmAnalysisConditionChange("両方", "矩形荷重 (編集)"))
            {
                e.Cancel = true;
                return;
            }

            // 矩形荷重は群杭沈下だけが読む。水平解析の結果を陳腐化させない
            HandleDataGridCellEditEnding(e, scope: AnalysisInputScope.Settlement, customAction: () =>
            {
                IsGroupPileSettlementAnalysisDone = false;
                // ユーザ編集時は個別十字系から「任意矩形」に切替
                SwitchToAnyRectIfCrossType();
                // 個別矩形系では編集後 GroupPileLoadDia DataGrid を非表示にするためフラグ更新
                var lt = CurrentInputModel?.PileGroupSettlement?.LoadingType;
                if (lt == "個別矩形" || lt == "個別矩形（基礎梁考慮）")
                {
                    IsRectLoadFreshFromAutoGen = false;
                }
            });
        }


        private void DataGridSettlementSoilLayers_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 解析結果が保存されている場合は警告 (土層は両ルート共通入力)
            if (!ConfirmAnalysisConditionChange("両方", "土層 (編集)"))
            {
                e.Cancel = true;
                return;
            }

            // 「下端Z」列はバリデーションが必要 (一つ上のセル値より小さい必要あり)。
            // バリデーションは TextBox.Text から先に行い、不正値ならコミットせず Undo にも残さない。
            if (e.Column is DataGridTextColumn && e.Column.Header.ToString().Contains("下端Z"))
            {
                var dataGrid = sender as DataGrid;
                var editedItem = e.Row.Item as SettlementSoilLayer;
                var editedTextBox = e.EditingElement as TextBox;

                if (editedTextBox != null && double.TryParse(editedTextBox.Text, out double newValue))
                {
                    int rowIndex = dataGrid?.Items.IndexOf(editedItem) ?? -1;
                    if (rowIndex > 0
                        && dataGrid.Items[rowIndex - 1] is SettlementSoilLayer previousItem
                        && newValue >= previousItem.BottomAltitude)
                    {
                        MessageService.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Cancel = true;
                        return; // commit せず Undo にも残さない
                    }
                }
            }

            // pre-edit 状態を Undo スナップショットに保存 (binding.UpdateSource より前に実行)
            // 沈下用の土層は群杭沈下だけが読む。水平解析の結果は陳腐化しない
            SaveUndoState(AnalysisInputScope.Settlement);

            // バインディングソースの更新 (= コミット)
            var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();

            // 変更後の UI 更新
            RequestUpdateWindow();
        }
    }
}
