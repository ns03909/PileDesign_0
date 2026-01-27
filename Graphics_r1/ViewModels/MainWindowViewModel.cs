using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// <summary>
    /// MainWindowViewModel (メインファイル)
    ///
    /// 責任範囲:
    /// - ファイル操作（新規作成、開く、保存、エクスポート）
    /// - コレクション管理（杭配置、通り心、荷重面、土層の追加・削除）
    /// - ウィンドウ表示制御（各種ダイアログウィンドウの開閉）
    /// - DataGrid編集イベント処理
    /// - 解析実行制御（要素分割、解析実行前チェック）
    /// - Undo/Redo機能
    /// - UI更新制御（デバウンス処理を含む）
    ///
    /// その他のpartialクラス:
    /// - MainWindowViewModel.Constructor.cs : プロパティ定義とコンストラクタ
    /// - MainWindowViewModel.Examples.cs : 設計例集データ生成
    /// - MainWindowViewModel.Improvements.cs : パフォーマンス最適化機能
    /// - MainWindowViewModel.TreeView.cs : TreeView制御
    /// - MainWindowViewModel.SettlementGridCache.cs : 沈下グリッドキャッシュ
    /// - MainWindowViewModel.ConfirmDeleteAnalysisModel.cs : 解析モデル削除確認
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly UndoManager _undoManager = new();
        private readonly FileOperationService _fileOperationService;
        private readonly PileLayoutService _pileLayoutService;
        private readonly SettlementAnalysisService _settlementAnalysisService;

        private System.Windows.Threading.DispatcherTimer? _generateSoilPilesDebounceTimer;
        private bool _soilPilesGenerationPending = false;

        private static void Debounce(ref System.Windows.Threading.DispatcherTimer? timer, int milliseconds, Action action)
        {
            timer?.Stop();
            var localTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds)
            };
            timer = localTimer;
            localTimer.Tick += (s, e) =>
            {
                localTimer.Stop();
                action();
            };
            localTimer.Start();
        }

        /// <summary>
        /// SoilPiles の生成をデバウンス付きでリクエストします。
        /// 短時間に複数回呼ばれても、最後の呼び出しから一定時間後に1回だけ実行されます。
        /// </summary>
        public void RequestGenerateSoilPiles()
        {
            if (IsElementSplit) return;
            _soilPilesGenerationPending = true;
            Debounce(ref _generateSoilPilesDebounceTimer, 50, () =>
            {
                if (_soilPilesGenerationPending)
                {
                    _soilPilesGenerationPending = false;
                    CurrentInputModel?.GenerateSoilPiles();
                }
            });
        }
        /// <summary>
        /// SoilPiles の生成を即座に実行します（デバウンスをスキップ）。
        /// 明示的に即時実行が必要な場合に使用します。
        /// </summary>
        public void GenerateSoilPilesImmediate()
        {
            // 保留中のデバウンスをキャンセル
            _generateSoilPilesDebounceTimer?.Stop();
            _generateSoilPilesDebounceTimer = null;
            _soilPilesGenerationPending = false;

            if (!IsElementSplit)
                CurrentInputModel?.GenerateSoilPiles();
        }

        // クラスの先頭付近のフィールドに追加（既存のフィールドの近くに）
        private System.Windows.Threading.DispatcherTimer? _updateWindowDebounceTimer;
        private bool _updateWindowPending = false;
        /// <summary>
        /// ウィンドウ更新をデバウンス付きでリクエストします。
        /// 短時間に複数回呼ばれても、最後の呼び出しから一定時間後に1回だけ実行されます。
        /// </summary>


        public void RequestUpdateWindow()
        {
            _updateWindowPending = true;
            Debounce(ref _updateWindowDebounceTimer, 30, () =>
            {
                if (_updateWindowPending)
                {
                    _updateWindowPending = false;
                    UpdateWindowAction?.Invoke();
                }
            });
        }

        /// <summary>
        /// ウィンドウ更新を即座に実行します（デバウンスをスキップ）。
        /// ダイアログを閉じた後など、即時更新が必要な場合に使用します。
        /// </summary>
        public void UpdateWindowImmediate()
        {
            // 保留中のデバウンスをキャンセル
            _updateWindowDebounceTimer?.Stop();
            _updateWindowDebounceTimer = null;
            _updateWindowPending = false;

            UpdateWindowAction?.Invoke();
        }

        /// <summary>
        /// UI更新を一元的に通知します。
        /// ウィンドウ更新とTreeView更新を統一的に処理します。
        /// </summary>
        /// <param name="updateTree">TreeViewも更新するか（デフォルト: true）</param>
        /// <param name="immediate">即座に実行するか（デフォルト: false、デバウンス付き）</param>
        private void NotifyUIChanged(bool updateTree = true, bool immediate = false)
        {
            if (immediate)
                UpdateWindowImmediate();
            else
                RequestUpdateWindow();

            if (updateTree)
                UpdateTreeView();
        }

        /// <summary>
        /// DataGridセルエディット完了時の共通処理
        /// バインディング更新とUI更新を一元的に処理します。
        /// </summary>
        /// <param name="e">DataGridセルエディットイベント引数</param>
        /// <param name="customAction">追加のカスタム処理（オプション）</param>
        /// <param name="updateTree">TreeViewも更新するか（デフォルト: true）</param>
        /// <returns>Commitアクションの場合true、それ以外false</returns>
        private bool HandleDataGridCellEditEnding(DataGridCellEditEndingEventArgs e, Action customAction = null, bool updateTree = true)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                // カスタム処理の実行
                customAction?.Invoke();

                // UI更新
                NotifyUIChanged(updateTree);

                return true;
            }
            return false;
        }

        /// <summary>
        /// コレクションからアイテムを削除する共通処理
        /// </summary>
        /// <typeparam name="T">アイテムの型</typeparam>
        /// <param name="sender">削除対象のアイテム</param>
        /// <param name="collection">削除元のコレクション</param>
        /// <param name="postDeleteAction">削除後のカスタム処理（オプション）</param>
        /// <param name="saveUndo">Undo保存するか（デフォルト: false）</param>
        /// <param name="updateTree">TreeViewも更新するか（デフォルト: false）</param>
        /// <param name="immediate">即座に実行するか（デフォルト: false）</param>
        /// <returns>削除に成功した場合true</returns>
        private bool DeleteCollectionItem<T>(
            object sender,
            ObservableCollection<T> collection,
            Action postDeleteAction = null,
            bool saveUndo = false,
            bool updateTree = false,
            bool immediate = false)
        {
            if (sender is not T itemToDelete)
                return false;

            if (saveUndo)
                TrySaveUndoSnapshotSafely();

            collection.Remove(itemToDelete);

            postDeleteAction?.Invoke();

            NotifyUIChanged(updateTree, immediate);

            return true;
        }

        /// <summary>
        /// ダイアログウィンドウを開く共通処理（Undo保存付き）
        /// </summary>
        /// <typeparam name="TViewModel">ViewModelの型</typeparam>
        /// <typeparam name="TWindow">Windowの型</typeparam>
        /// <param name="postDialogAction">ダイアログ終了後のカスタム処理（オプション）</param>
        private void OpenDialogWindowWithUndo<TViewModel, TWindow>(Action postDialogAction = null)
            where TViewModel : ObservableObject
            where TWindow : Window, new()
        {
            // Undoポイントを追加（読込前の状態を保存）
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // ダイアログを開く
            OpenDialogWindow<TViewModel, TWindow>(this);

            // 追加処理の実行
            postDialogAction?.Invoke();
        }

        /// <summary>
        /// IsFrontPileフラグを選択されたアイテムに適用
        /// </summary>
        /// <param name="selectedItems">選択されたアイテムのリスト</param>
        /// <param name="isApplicable">各レベルの適用可否（4要素の配列）</param>
        /// <param name="values">各レベルの値（4要素の配列）</param>
        private static void ApplyIsFrontPileFlags(
            IEnumerable<PileLayoutDataItem> selectedItems,
            bool[] isApplicable,
            bool[] values)
        {
            for (int i = 0; i < 4; i++)
            {
                if (isApplicable[i])
                {
                    foreach (var item in selectedItems)
                    {
                        item.IsFrontPiles[i] = values[i];
                    }
                }
            }
        }

        /// <summary>
        /// 粒度区分からポアソン比を決定
        /// </summary>
        /// <param name="granularityClass">粒度区分</param>
        /// <returns>ポアソン比</returns>
        private static double DeterminePoissonsRatio(string granularityClass)
        {
            return granularityClass switch
            {
                "粘性土" => 0.4,
                "砂質土" or "礫質土" => 0.3,
                _ => 0.33
            };
        }

        /// <summary>
        /// SettlementSoilLayerのThickness（層厚）を計算
        /// </summary>
        /// <param name="layers">沈下土層のリスト</param>
        /// <param name="loadingPlaneAltitude">載荷面標高</param>
        private static void CalculateLayerThicknesses(
            IList<SettlementSoilLayer> layers,
            double loadingPlaneAltitude)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                layers[i].Thickness = i == 0
                    ? loadingPlaneAltitude - layers[i].BottomAltitude
                    : layers[i - 1].BottomAltitude - layers[i].BottomAltitude;
            }
        }

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

        // 追加: コマンド更新一括ヘルパ
        private void RaiseAllCommandsCanExecute()
        {
            // リフレクションで "Command" で終わるすべてのコマンドプロパティを列挙し、
            // CommunityToolkit の IRelayCommand は NotifyCanExecuteChanged() を呼び、
            // 自前 RelayCommand 等は RaiseCanExecuteChanged() を呼び出す。
            var props = this.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
                .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType));

            foreach (var p in props)
            {
                try
                {
                    var cmdObj = p.GetValue(this) as ICommand;
                    if (cmdObj == null) continue;

                    // CommunityToolkit の IRelayCommand を優先して扱う
                    if (cmdObj is CommunityToolkit.Mvvm.Input.IRelayCommand toolkitCmd)
                    {
                        toolkitCmd.NotifyCanExecuteChanged();
                        continue;
                    }

                    // 自前 RelayCommand の RaiseCanExecuteChanged() を探して呼び出す
                    var raiseMethod = cmdObj.GetType().GetMethod("RaiseCanExecuteChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (raiseMethod != null)
                    {
                        raiseMethod.Invoke(cmdObj, null);
                        continue;
                    }

                    // 互換性のため NotifyCanExecuteChanged メソッドも試す（まれなケース）
                    var notifyMethod = cmdObj.GetType().GetMethod("NotifyCanExecuteChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    notifyMethod?.Invoke(cmdObj, null);
                }
                catch
                {
                    // 個別コマンドの状態更新で例外が起きても他は続行する
                }
            }
        }

        private InputModel? _currentInputModel;
        public InputModel? CurrentInputModel
        {
            get => _currentInputModel;
            set
            {
                // SetProperty は ObservableObject のユーティリティ（CommunityToolkit）
                if (SetProperty(ref _currentInputModel, value))
                {
                    // VM 再アタッチなどはここで一度だけ行う
                    _currentInputModel?.AttachViewModel(this);

                    UpdateWindowImmediate();
                    RaiseAllCommandsCanExecute();

                    OnPropertyChanged(nameof(CurrentInputModel));
                }
            }
        }

        // 修正例: CurrentFilePath
        private string? _currentFilePath;

        public string? CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (SetProperty(ref _currentFilePath, value))
                {
                    RaiseAllCommandsCanExecute();
                }
            }
        }

        // 修正例: Canvas3DLayout
        private Canvas? _canvas3DLayout;

        public Canvas? Canvas3DLayout
        {
            get => _canvas3DLayout;
            set => SetProperty(ref _canvas3DLayout, value);
        }

        private Action? _updateWindowAction;

        // 修正例: アクションをプロパティ化（必要なら）
        public Action? UpdateWindowAction
        {
            get => _updateWindowAction;
            set => SetProperty(ref _updateWindowAction, value);
        }

        private Action? _updateCanvas3DAction;
        public Action? UpdateCanvas3DAction
        {
            get => _updateCanvas3DAction;
            set => SetProperty(ref _updateCanvas3DAction, value);
        }

        // イベントの宣言
        public event EventHandler<DataGridCellEditEndingEventArgs> DataGridSettlementSoilLayersCellEditEnding;

        // イベントを発火するメソッド
        public virtual void OnDataGridSettlementSoilLayersCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            DataGridSettlementSoilLayersCellEditEnding?.Invoke(this, e);
        }

        private ICommand _dataGridSettlementSoilLayersCellEditEndingCommand;
        public ICommand DataGridSettlementSoilLayersCellEditEndingCommand
        {
            get
            {
                _dataGridSettlementSoilLayersCellEditEndingCommand ??= new RelayCommand<DataGridCellEditEndingEventArgs>(OnDataGridSettlementSoilLayersCellEditEnding);
                return _dataGridSettlementSoilLayersCellEditEndingCommand;
            }
        }

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
                e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        // 杭配置更新時更新メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e, () =>
            {
                IsElementSplit = false;
                RequestGenerateSoilPiles();

                // コレクション自体の変更通知
                OnPropertyChanged(nameof(GroupPileSettlementXmin));
                OnPropertyChanged(nameof(GroupPileSettlementXmax));
                OnPropertyChanged(nameof(GroupPileSettlementYmin));
                OnPropertyChanged(nameof(GroupPileSettlementYmax));
            });
        }

        // 杭軸力更新時更新メソッド
        [RelayCommand]
        private void DataGridPileAxialForce_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e);
        }

        // 前後杭更新メソッド
        [RelayCommand]
        private void DataGridIsFrontPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
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
                    return false;
                else
                {
                    IsElementSplit = false;
                    IsVerticalAnalysisDone = false;
                    IsHorizontalAnalysisDone = false;
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
                e.Handled = true;
        }
        [RelayCommand]
        private void ComboBoxEmbedmentGroundNo_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
                e.Handled = true;
        }
        [RelayCommand]
        private void TextBoxBottomAltitude_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
                e.Handled = true;
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
            HandleDataGridCellEditEnding(e, () => UpdateEmbedment(), updateTree: false);
        }
        [RelayCommand]
        private void DataGridSoilPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e, updateTree: false);
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
            HandleDataGridCellEditEnding(e, () => IsGroupPileSettlementAnalysisDone = false, updateTree: false);
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
                // 変更後（以下の箇所で適用）
                RequestUpdateWindow();
            }
        }

        // GridX追加メソッド
        [RelayCommand]
        private void AddGridX()
        {
            // Undoポイントを追加（1回の追加を1ステップで戻せるようにする）
            TrySaveUndoSnapshotSafely();

            // 防波堤: null の場合はここで生成
            CurrentInputModel.GridXItems ??= new ObservableCollection<GridDataItem>();
            AddGrid(CurrentInputModel.GridXItems, "X1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridXItems));
        }

        // GridY追加メソッド
        [RelayCommand]
        private void AddGridY()
        {
            TrySaveUndoSnapshotSafely();
            CurrentInputModel.GridYItems ??= new ObservableCollection<GridDataItem>();
            AddGrid(CurrentInputModel.GridYItems, "Y1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridYItems));
        }

        // Grid追加メソッド
        private void AddGrid(ObservableCollection<GridDataItem> collection, string name, double spacing)
        {
            collection.Add(new GridDataItem());
            if (collection.Count == 1)
                collection[^1].Name = name;
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
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
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
            // 変更: デバウンス付きで更新
            RequestUpdateWindow();
        }

        // 矩形荷重追加メソッド
        [RelayCommand]
        private void AddRectLoad()
        {
            if (!CheckAndResetPostAnalysisMode())
                return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad());

            IsGroupPileSettlementAnalysisDone = false;
            RequestUpdateWindow();
        }

        // 解析後処理モードの場合の確認
        private bool CheckAndResetPostAnalysisMode()
        {
            if (IsPostAnalysisMode)
            {
                var result = MessageBox.Show("解析前処理モードにしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                    return false; // 操作をキャンセル
                IsPostAnalysisMode = false; // 解析前処理モードに変更
            }
            return true; // 操作を続行
        }

        // 群杭沈下検討用検討用土層追加メソッド
        [RelayCommand]
        private void AddSettlementSoilLayer()
        {
            if (!CheckAndResetPostAnalysisMode())
                return;

            TrySaveUndoSnapshotSafely();

            double bottomAlt;
            double ek;
            double poissonsRatio;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;



            if (CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Count == 0)
            {
                bottomAlt = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude - 10.0;
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

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 全土層削除メソッド
        [RelayCommand]
        private void DeleteAllSettlementSoilLayers()
        {
            var settlement = CurrentInputModel?.PileGroupSettlement;
            if (settlement == null)
                return;

            TrySaveUndoSnapshotSafely();

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

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
            UpdateTreeView();
        }

        // 群杭沈下検討用検討用土層削除メソッド
        [RelayCommand]
        private void DeleteSettlementSoilLayer(object sender)
        {
            DeleteCollectionItem(
                sender,
                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers,
                () => UpdateSettlementSoilLayer());
        }

        // 群杭沈下検討用検討用土層データグリッド更新メソッド
        private void UpdateSettlementSoilLayer()
        {
            // SettlementCollection の更新
            double loadingPlaneAltitude = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;
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
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        public void DataGridGridY_CurrentCellChanged()
        {
            RecalculateGrid(CurrentInputModel.GridYItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        [RelayCommand]
        private void DataGridGridX_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
                RecalculateGrid(CurrentInputModel.GridXItems);
        }
        [RelayCommand]
        private void DataGridGridY_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
                RecalculateGrid(CurrentInputModel.GridYItems);
        }

        [RelayCommand]
        private void DeleteGridX(object sender)
        {
            // Undoポイント
            TrySaveUndoSnapshotSafely();

            DeleteGridItem(sender, CurrentInputModel.GridXItems);
            RecalculateGrid(CurrentInputModel.GridXItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }
        [RelayCommand]
        private void DeleteGridY(object sender)
        {
            // Undoポイント
            TrySaveUndoSnapshotSafely();

            DeleteGridItem(sender, CurrentInputModel.GridYItems);
            RecalculateGrid(CurrentInputModel.GridYItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }
        [RelayCommand]
        private void DeleteElement(object sender)
        {
            DeleteCollectionItem(sender, CurrentInputModel.Elements);
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
            DeleteCollectionItem(sender, CurrentInputModel.PileGroupSettlement.RectLoads, immediate: true);
        }

        [RelayCommand]
        private void ComboBox3DAnalysisResultContent_OnSelectionChanged(SelectionChangedEventArgs e)
        {
            UpdateWindowImmediate(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
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
            TrySaveUndoSnapshotSafely();

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
                    RequestGenerateSoilPiles();

                // 変更後（以下の箇所で適用）
                RequestUpdateWindow();
                UpdatePileLayoutNo();
                UpdateTreeView();
            });
        }

        [RelayCommand]
        private void OnComputePileGroupFactor()
        {
            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

        [RelayCommand]
        private void OnComputePileSpacingFactor()
        {
            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

        // 重複要素の削除
        [RelayCommand]
        private void OnDeleteDuplicateElements()
        {
            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            var uniqueElements = new HashSet<Element>(CurrentInputModel.Elements);
            CurrentInputModel.Elements = new ObservableCollection<Element>(uniqueElements);

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 要素の節点位置での分割
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            var newElements = new ObservableCollection<Element>();

            foreach (var element in CurrentInputModel.Elements)
            {
                if (element.IsSelected)
                {
                    var splitElements = SplitTwoNodeElementByNodes(element);
                    foreach (var splitElement in splitElements)
                        newElements.Add(splitElement);
                }
                else
                    newElements.Add(element);
            }

            CurrentInputModel.Elements = newElements;
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
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
                    splitNodes.Add(pileLayout);
            }

            var distances = new List<double>();
            for (int i = 0; i < splitNodes.Count; i++)
                distances.Add(GetDistance.BetweenTwoPoint3Ds(splitNodes[0].Point3D, splitNodes[i].Point3D));

            var indeces = new List<int>();
            for (int i = 0; i < distances.Count; i++)
                indeces.Add(GetIndexOfNthSmallestValue(distances, i));

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
                CurrentInputModel.PileLayoutItems[i].No = i + 1;
        }

        // 荷重面の自動生成
        [RelayCommand]
        private void OnAdjustRectLoadPlan()
        {
            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            // BoundingBoxCalculator を使用して境界を計算
            var boundingBox = BoundingBoxCalculator.Calculate(
                CurrentInputModel.PileLayoutItems,
                RectLoadPileDistance
            );

            double adjustedMinX = boundingBox.MinX;
            double adjustedMaxX = boundingBox.MaxX;
            double adjustedMinY = boundingBox.MinY;
            double adjustedMaxY = boundingBox.MaxY;

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

            // 変更後（以下の箇所で適用）
            UpdateWindowImmediate();
            UpdateTreeView();
        }




        // 根入部平面の自動調整
        [RelayCommand]
        private void OnAdjustEmbedmentPlan()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0 || CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count == 0)
                return;

            // BoundingBoxCalculator を使用して境界を計算
            var boundingBox = BoundingBoxCalculator.Calculate(
                CurrentInputModel.PileLayoutItems,
                EmbedmentPileDistance
            );

            double adjustedMinX = boundingBox.MinX;
            double adjustedMaxX = boundingBox.MaxX;
            double adjustedMinY = boundingBox.MinY;
            double adjustedMaxY = boundingBox.MaxY;

            foreach (var embedmentDataItem in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                embedmentDataItem.X1 = adjustedMinX;
                embedmentDataItem.X2 = adjustedMaxX;
                embedmentDataItem.Y1 = adjustedMinY;
                embedmentDataItem.Y2 = adjustedMaxY;
            }

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
            UpdateTreeView();
        }

        // 慣性力作用点を杭配置の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageBox.Show("杭配置データがありません。");
                return;
            }

            TrySaveUndoSnapshotSafely();

            // BoundingBoxCalculator を使用して平均中心を計算
            var (centerX, centerY) = BoundingBoxCalculator.CalculateAverageCenter(CurrentInputModel.PileLayoutItems);

            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointY = centerY;

            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointY = centerY;

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
            }

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
            }

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        [RelayCommand]
        private void AutoIsFrontPiles()
        {
            TrySaveUndoSnapshotSafely();

            var viewModel = new AutoIsFrontPileViewModel();
            var autoIsFrontPilesWindow = new AutoIsFrontPilesWindow();
            autoIsFrontPilesWindow.AutoIsFrontPileCompleted += AutoIsFrontPilesWindow_AutoIsFrontPileCompleted;
            autoIsFrontPilesWindow.ShowDialog();
            IsFrontPileLabelVisible = true;
            RequestUpdateWindow();
            UpdateTreeView();
        }

        //群杭係数ウィンドウを開くメソッド
        [RelayCommand]
        private void GroupPileFactor()
        {
            // Windowをインスタンス化して表示
            GroupPileFactorWindow groupPileFactorWindow = new(this);

            groupPileFactorWindow.ShowDialog(); // モーダルダイアログとして表示

            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
            UpdateTreeView();
        }


        // 群杭沈下解析の実行メソッド
        [RelayCommand]
        private void PileGroupSettlementAnalysis()
        {
            var result = _settlementAnalysisService.PerformSettlementAnalysis(
                CurrentInputModel.PileGroupSettlement,
                CurrentInputModel.PileLayoutItems,
                CurrentInputModel.ElementDivision.SoilPiles,
                CurrentInputModel.GridXItems,
                CurrentInputModel.GridYItems,
                GroupPileSettlementXmin,
                GroupPileSettlementXmax,
                GroupPileSettlementYmin,
                GroupPileSettlementYmax,
                GroupPileSettlementXOffset,
                GroupPileSettlementYOffset,
                GroupPileSettlementXSpacing,
                GroupPileSettlementYSpacing);

            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CurrentInputModel.PileGroupSettlement.SettlementGridData = result.SettlementGridData;

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
                    LoadCase loadCase = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];

                    foreach (PileLayoutDataItem pileLayout0 in CurrentInputModel.PileLayoutItems)
                    {
                        // 前方杭かどうかを判定
                        pileLayout0.IsFrontPiles[i] = IsFrontPile(pileLayout0, loadCase, cosAlpha);
                    }
                }
            }
        }

        /// <summary>
        /// 指定された杭が前方杭かどうかを判定
        /// </summary>
        private bool IsFrontPile(PileLayoutDataItem targetPile, LoadCase loadCase, double cosAlpha)
        {
            Point targetPosition = new(targetPile.Point3D.X, targetPile.Point3D.Y);
            Vector loadDirectionVector = PileDesign.Converters.VectorConverter.ConvertAngleToUnitVector(loadCase.LoadAngle);

            foreach (PileLayoutDataItem otherPile in CurrentInputModel.PileLayoutItems)
            {
                if (targetPile == otherPile)
                    continue;

                Point otherPosition = new(otherPile.Point3D.X, otherPile.Point3D.Y);
                Vector directionVector = otherPosition - targetPosition;

                // 内積を計算
                double dotProduct = Vector.Multiply(directionVector, loadDirectionVector);

                // 余弦を計算
                double cosTheta = dotProduct / (directionVector.Length * loadDirectionVector.Length);

                // 余弦が指定角度より大きい場合、前方杭ではない
                if (cosAlpha < cosTheta)
                {
                    return false;
                }
            }

            // すべての杭に対してチェックを通過したら前方杭
            return true;
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
                    _fileOperationService.SaveProjectData(CurrentFilePath, CurrentInputModel, CurrentModel);
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
                SaveInputModelFileAs();
            else
            {
                try
                {
                    _fileOperationService.SaveProjectData(CurrentFilePath, CurrentInputModel, CurrentModel);
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
                return;
            else if (result == MessageBoxResult.Yes)
                SaveInputModelFile();

            CurrentInputModel.Reset();
            this.CurrentModel = null; // AnaModelもリセット
            CurrentFilePath = null;

            // ここで初期状態をUndoスタックに積む
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            UpdateWindowImmediate();
            UpdateTreeView();
        }

        private void TrySaveUndoSnapshotSafely()
        {
            try
            {
                var snapshot = CurrentInputModel?.DeepCopy();
                if (snapshot != null)
                {
                    _undoManager.SaveState(snapshot);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("TrySaveUndoSnapshotSafely: DeepCopy returned null, skipping undo snapshot.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrySaveUndoSnapshotSafely例外: {ex}");
            }
        }

        // 修正: null チェックと空コレクション初期化の共通化
        public bool TryLoadInputModelFileUsingInputModelLoader(string filePath)
        {
            try
            {
                var loaded = InputModel.LoadFromFile(filePath, this);
                if (loaded == null)
                {
                    MessageBox.Show($"ファイルの読込に失敗しました。\n{filePath}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    System.Diagnostics.Debug.WriteLine($"LoadFromFile returned null: {filePath}");
                    return false;
                }

                CurrentInputModel = loaded;
                CurrentInputModel.AttachViewModel(this);
                CurrentFilePath = filePath;
                CurrentInputModel.GridXItems ??= new ObservableCollection<GridDataItem>();
                CurrentInputModel.GridYItems ??= new ObservableCollection<GridDataItem>();

                // 要素分割・解析状態をリセット
                IsElementSplit = false;
                IsHorizontalAnalysisDone = false;
                IsVerticalAnalysisDone = false;
                IsGroupPileSettlementAnalysisDone = false;

                UpdateWindowImmediate();
                UpdateTreeView();
                MessageBox.Show("読込が完了しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル読込中にエラーが発生しました。\n{ex.Message}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"TryLoadInputModelFileUsingInputModelLoader例外: {ex}");
                return false;
            }
        }

        [RelayCommand]
        public void OpenInputModelFileSimple()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "JSON Files (*.json)|*.json", DefaultExt = "json" };
            if (ofd.ShowDialog() != true) return;
            // Undo 保存は安全ヘルパを使用
            TrySaveUndoSnapshotSafely();
            TryLoadInputModelFileUsingInputModelLoader(ofd.FileName);
        }

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
                    // Undoポイントを追加（読込前の状態を保存）
                    _undoManager.SaveState(CurrentInputModel.DeepCopy());

                    var projectData = _fileOperationService.LoadProjectData(openFileDialog.FileName);

                    if (projectData != null)
                    {
                        CurrentInputModel = projectData.InputModel;
                        CurrentModel = projectData.AnaModel;

                        // コレクションを ObservableCollection に変換
                        _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

                        // プロパティ変更通知
                        OnPropertyChanged(nameof(CurrentInputModel));
                    }
                    else
                    {
                        // ProjectDataでない場合を想定して InputModel 単体で読めるか試す
                        var ok = TryLoadInputModelFileUsingInputModelLoader(openFileDialog.FileName);
                        if (!ok)
                            throw new InvalidOperationException("ファイル形式が不正です。ProjectData でも InputModel でもありません。");
                        return;
                    }

                    // VM 再アタッチ
                    CurrentInputModel.AttachViewModel(this);
                    CurrentFilePath = openFileDialog.FileName;

                    // 要素分割・解析状態をリセット
                    IsElementSplit = false;
                    IsHorizontalAnalysisDone = false;
                    IsVerticalAnalysisDone = false;
                    IsGroupPileSettlementAnalysisDone = false;

                    UpdateWindowImmediate();
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
                try
                {
                    var doc = new Output.WordDocument(CurrentInputModel, CurrentModel, this);
                    doc.CreateWordDocument(CurrentInputModel, saveFileDialog.FileName);
                    MessageBox.Show($"docsファイルが作成されました。\n{saveFileDialog.FileName}\nMSWordでファイルを開き、ctrl + aで全選択した後, F9によりフィールドを更新してください。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Word出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            try
            {
                var dockxOutputOptionWindow = new DocxOutputWindow(this);
                dockxOutputOptionWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"計算書出力ウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // オプション表示メソッド
        [RelayCommand]
        private static void OpenOptionWindow()
        {
            try
            {
                var optionWindow = new OptionWindow();
                optionWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"オプションウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 解析結果が1つでも存在するか
        private bool HasAnyAnalysisResult()
            => IsHorizontalAnalysisDone || IsVerticalAnalysisDone || IsGroupPileSettlementAnalysisDone;

        // コマンド状態一括更新ヘルパ
        private void RaiseResultCommandsCanExecute()
        {
            if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
            OpenGraphWindowCommand?.NotifyCanExecuteChanged();
            OpenLogWindowCommand?.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanOpenGraphWindow))]
        private void OpenGraphWindow()
        {
            try
            {
                if (!HasAnyAnalysisResult()) return;

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
            catch (Exception ex)
            {
                MessageBox.Show($"グラフウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool CanOpenGraphWindow() => HasAnyAnalysisResult();


        // フィールド追加
        private readonly AnalysisResultTableService _tableService = new();

        // プロパティ
        public IReadOnlyList<ResultTable> LatestResultTables { get; private set; } = Array.Empty<ResultTable>();

        // Latest analysis logs
        public ObservableCollection<string> LatestAnalysisLogs { get; private set; } = new();

        public void SetLatestAnalysisLogs(IReadOnlyList<string> logs)
        {
            LatestAnalysisLogs.Clear();
            foreach (var log in logs)
                LatestAnalysisLogs.Add(log);
            OnPropertyChanged(nameof(LatestAnalysisLogs));
            OpenLogWindowCommand?.NotifyCanExecuteChanged();
        }

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
            try
            {
                var vm = new TableWindowViewModel();
                vm.LoadTables(LatestResultTables);
                var w = new Views.TableWindow { DataContext = vm };
                w.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"テーブルウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanOpenLogWindow))]
        private void OpenLogWindow()
        {
            if (!CanOpenLogWindow()) return;
            var vm = new LogWindowViewModel(LatestAnalysisLogs);
            var w = new Views.LogWindow { DataContext = vm };
            w.Show();

            //try
            //{
            //    if (!CanOpenLogWindow()) return;
            //    var vm = new LogWindowViewModel(LatestAnalysisLogs);
            //    var w = new Views.LogWindow { DataContext = vm };
            //    w.Show();
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show($"ログウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            //}
        }

        private bool CanOpenLogWindow() => LatestAnalysisLogs != null && LatestAnalysisLogs.Count > 0;

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
            try
            {
                var helpWindow = new HelpWindow();
                helpWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ヘルプウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    win.Close();
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

            // 変更後（以下の箇所で適用）
            UpdateWindowImmediate();
            UpdateTreeView();
        }

        [RelayCommand]
        public static void OpenPileSectionLibraryWindow()
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

        [RelayCommand]
        public static void OpenShortcutKeysWindow()
        {
            try
            {
                var w = new PileDesign.Views.ShortcutKeysWindow
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false
                };
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ショートカット一覧ウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task MoveCopyPiles()
        {
            try
            {
                // 選択節点がない場合は処理を中止してメッセージ表示
                if (CurrentInputModel == null ||
                CurrentInputModel.PileLayoutItems == null ||
                !CurrentInputModel.PileLayoutItems.Any(p => p.IsSelected))
                {
                    MessageBox.Show("杭配置が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // MoveWindowをインスタンス化して表示
                MoveCopyWindow moveCopyWindow = new();

                var tcs = new TaskCompletionSource<bool>();
                bool operationExecuted = false;

                moveCopyWindow.MoveCopyCompleted += async (sender, e) =>
                {
                    operationExecuted = true;
                    await MoveCopyWindow_MoveCopyCompletedAsync(sender, e);
                    tcs.TrySetResult(true);
                };

                // ウィンドウが閉じられたら（キャンセル含む）TaskCompletionSourceを完了させる
                moveCopyWindow.Closed += (sender, e) =>
                {
                    tcs.TrySetResult(false);
                };

                moveCopyWindow.ShowDialog(); // モーダルダイアログとして表示

                // 操作が実行された場合のみ待機と更新を行う
                if (operationExecuted)
                {
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

                        // 変更: デバウンス付きで更新
                        RequestUpdateWindow();
                        UpdateTreeView();
                    }
                    finally
                    {
                        // ★ カーソルを元に戻す
                        Mouse.OverrideCursor = null;
                    }
                }
            }
            catch (Exception ex)
            {
                // 例外発生時もカーソルをリセット
                Mouse.OverrideCursor = null;
                MessageBox.Show($"杭の移動・複製中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveCopyWindow_MoveCopyCompletedAsync(object sender, MoveCopyEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsMove)
                MoveNodes(e.DX, e.DY);// 移動操作の処理
            else if (e.IsCopy)
                await CopyNodesAsync(e.DX, e.DY, e.RepetitionNumber); // 複製操作の処理
        }

        private async Task CopyNodesAsync(double dX, double dY, int repetitionNumber)
        {
            // 変更を行う前に、選択されたアイテムのリストを作成
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            int totalCount = selectedItems.Count * repetitionNumber;

            // ★ 大量コピー時は待機カーソルを表示
            bool showWaitCursor = totalCount > 10;
            if (showWaitCursor)
                Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // サービスを使ってコピー実行
                var combined = _pileLayoutService.CopySelectedPiles(
                    CurrentInputModel.PileLayoutItems,
                    dX,
                    dY,
                    repetitionNumber,
                    item => item.SetMainWindowViewModel(this));

                // ★ UIスレッドで一括置換（CollectionChangedを1回だけ発火）
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // コレクション全体を置換（CollectionChangedは1回のみ）
                    CurrentInputModel.PileLayoutItems = combined;

                    // SoilPiles を1回だけ再生成
                    if (!IsElementSplit)
                        RequestGenerateSoilPiles();

                    UpdatePileLayoutNo();
                    NotifyUIChanged();
                });
            }
            finally
            {
                if (showWaitCursor)
                    Mouse.OverrideCursor = null;
            }
        }

        // 移動操作を行う
        private void MoveNodes(double dX, double dY)
        {
            _pileLayoutService.MoveSelectedPiles(CurrentInputModel.PileLayoutItems, dX, dY);
        }

        // コピーを作成して操作を行う
        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            CurrentInputModel.PileLayoutItems = _pileLayoutService.CopySelectedPiles(
                CurrentInputModel.PileLayoutItems,
                dX,
                dY,
                repetitionNumber,
                item => item.SetMainWindowViewModel(this));

            UpdatePileLayoutNo();
        }

        // 杭配置の編集・追加コマンド
        [RelayCommand]
        private void EditAddPiles()
        {
            var editPileLayoutWindow = new EditPileLayoutWindow(this);

            editPileLayoutWindow.EditPileLayoutCompleted += EditPileLayoutWindow_EditPileLayoutCompleted;

            editPileLayoutWindow.ShowDialog();
            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
        }

        private void EditPileLayoutWindow_EditPileLayoutCompleted(object sender, EditPileLayoutEventArgs e)
        {
            var options = new PileLayoutService.BulkEditOptions
            {
                ApplyPileBodyNo = e.IsApplicablePileRefNo,
                PileBodyNo = e.SelectedPileRefNo,

                ApplyGroundNo = e.IsApplicableGroundRefNo,
                GroundNo = e.SelectedGroundRefNo,

                ApplyPileTopLevel = e.IsApplicablePileTopLevel,
                IsAddPileTopLevel = e.IsAddPileTopLevel,
                PileTopLevel = e.PileTopLevel,

                ApplyPileGroupFactor = e.IsApplicablePileGroupFactor,
                IsAddPileGroupFactor = e.IsAddPileGroupFactor,
                PileGroupFactor = e.PileGroupFactor,

                ApplyAxialForceVL = e.IsApplicableVL,
                IsAddAxialForceVL = e.IsAddVL,
                AxialForceVL = e.VL,

                ApplyAxialForceVLAdditional = e.IsApplicableVLadd,
                IsAddAxialForceVLAdditional = e.IsAddVLadd,
                AxialForceVLAdditional = e.VLadd,

                ApplyLevel1 = new[]
                {
                    e.IsApplicableE1_1, e.IsApplicableE1_2, e.IsApplicableE1_3, e.IsApplicableE1_4
                },
                IsAddLevel1 = new[]
                {
                    e.IsAddE1_1, e.IsAddE1_2, e.IsAddE1_3, e.IsAddE1_4
                },
                Level1Values = new[]
                {
                    e.E1_1, e.E1_2, e.E1_3, e.E1_4
                },

                ApplyLevel2 = new[]
                {
                    e.IsApplicableE2_1, e.IsApplicableE2_2, e.IsApplicableE2_3, e.IsApplicableE2_4
                },
                IsAddLevel2 = new[]
                {
                    e.IsAddE2_1, e.IsAddE2_2, e.IsAddE2_3, e.IsAddE2_4
                },
                Level2Values = new[]
                {
                    e.E2_1, e.E2_2, e.E2_3, e.E2_4
                }
            };

            _pileLayoutService.BulkEditSelectedPiles(CurrentInputModel.PileLayoutItems, options);

            // IsFrontPile フラグの処理
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            ApplyIsFrontPileFlags(
                selectedItems,
                new[] { e.IsApplicableIsFrontPile1, e.IsApplicableIsFrontPile2, e.IsApplicableIsFrontPile3, e.IsApplicableIsFrontPile4 },
                new[] { e.IsFrontPile1, e.IsFrontPile2, e.IsFrontPile3, e.IsFrontPile4 });
        }

        [RelayCommand]
        private void Undo()
        {
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);

                // 変更: 即時実行
                NotifyUIChanged(immediate: true);
                OnPropertyChanged(nameof(CurrentInputModel));
            }
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);

                // 変更: 即時実行
                NotifyUIChanged(immediate: true);
                OnPropertyChanged(nameof(CurrentInputModel));
            }
        }

        private void RemoveElementsContainingPileLayoutItem(PileLayoutDataItem oldItem)
        {
            var elementsToRemove = CurrentInputModel.Elements.Where(element => element.Nodes.Contains(oldItem)).ToList();

            foreach (var element in elementsToRemove)
                CurrentInputModel.Elements.Remove(element);


            // 変更: デバウンス付きで更新
            RequestUpdateWindow();
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
                    uniquePileLayoutDataItems.Add(pileLayoutItem);
            }

            CurrentInputModel.PileLayoutItems = uniquePileLayoutDataItems;
            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
        }

        public static void DeleteDuplicatedElements()
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

            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
            UpdateTreeView();
        }

        // 基本設定ウィンドウを開くメソッド
        [RelayCommand]
        private void OpenFundamentalWindow()
        {
            OpenDialogWindowWithUndo<FundamentalViewModel, FundamentalWindow>();
        }

        // 荷重条件ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenLoadCaseWindow()
        {
            OpenDialogWindowWithUndo<LoadCaseViewModel, LoadCaseWindow>(() =>
            {
                UpdateLoadCaseOption();
                UpdateLoadCombinationOption();
            });
        }

        // 地盤ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenGroundWindow()
        {
            OpenDialogWindowWithUndo<GroundLayerViewModel, GroundWindow>();
        }

        // 杭体ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenPileBodyWindow()
        {
            OpenDialogWindowWithUndo<PileBodyViewModel, PileBodyWindow>();
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
                var pileNo = pileLayout.PileNo;

                var pileBody = CurrentInputModel.PileBodies[pileLayout.PileBodyNo - 1];
                for (int i = 0; i < pileBody.PileBodySegments.Count; i++)
                {
                    var pileSection = pileBody.PileBodySegments[i].PileSection;

                    if (pileSection.FactoredServiceNmax < force)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n 使用限界軸力適用範囲Max{pileSection.FactoredServiceNmax:N0}kN < {force:N0}kN\n";
                    }
                    if (force < pileSection.FactoredServiceNmin)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n {force:N0}kN < 使用限界軸力適用範囲Min{pileSection.FactoredServiceNmin:N0}kN\n";
                    }
                }
            }

            if (hasWarning)
                MessageBox.Show(warningMessage, "警告", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("各杭配置の軸力は各断面の軸力適用範囲内です。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 要素分割ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenElementDivisionWindow()
        {
            if (IsPreparedForAnalysis())
            {
                // 杭下端より下方に土層・土質点が存在するかチェック
                var validationError = ValidatePileAndGroundDepth();
                if (!string.IsNullOrEmpty(validationError))
                {
                    MessageBox.Show(validationError, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Undoポイントを追加（読込前の状態を保存）
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                GenerateSoilPilesImmediate();  // 即時実行に変更
                CurrentInputModel.GenerateSoilEmbedment();

                var window = new ElementDivisionWindow(this);
                window.ShowDialog();

                UpdateCanvas3DAction?.Invoke();
                UpdateTreeView();
            }
        }

        /// <summary>
        /// 杭下端より下方に土層・土質点が存在するかを検証する
        /// </summary>
        /// <returns>エラーメッセージ（問題なければnull）</returns>
        private string? ValidatePileAndGroundDepth()
        {
            var errors = new System.Text.StringBuilder();

            foreach (var pileLayout in CurrentInputModel.PileLayoutItems)
            {
                int groundNo = pileLayout.GroundNo;
                int pileBodyNo = pileLayout.PileBodyNo;

                if (groundNo < 1 || groundNo > CurrentInputModel.GroundsInput.Count) continue;
                if (pileBodyNo < 1 || pileBodyNo > CurrentInputModel.PileBodies.Count) continue;

                var groundInput = CurrentInputModel.GroundsInput[groundNo - 1];
                var pileBody = CurrentInputModel.PileBodies[pileBodyNo - 1];

                // 杭下端標高を計算
                double pileTopAltitude = pileLayout.Z;
                double pileLength = pileBody.PileBodySegments.Sum(seg => seg.SegmentLength);
                double pileBottomAltitude = pileTopAltitude - pileLength;

                // 土層の最下層標高をチェック
                if (groundInput.GroundLayers != null && groundInput.GroundLayers.Count > 0)
                {
                    double groundBottomAltitude = groundInput.GroundLayers.Min(layer => layer.BottomAltitude);
                    if (pileBottomAltitude < groundBottomAltitude)
                    {
                        errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 杭下端標高({pileBottomAltitude:F2}m)が土層の最下層標高({groundBottomAltitude:F2}m)より下にあります。");
                    }
                }
                else
                {
                    errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 地盤No.{groundNo}に土層データがありません。");
                }

                // 土質点の最深深度をチェック
                if (groundInput.GroundMassesData != null && groundInput.GroundMassesData.Count > 0)
                {
                    double massBottomAltitude = groundInput.GroundMassesData.Min(mass => mass.AltitudeDepth);
                    if (pileBottomAltitude < massBottomAltitude)
                    {
                        errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 杭下端標高({pileBottomAltitude:F2}m)が土質点の最深標高({massBottomAltitude:F2}m)より下にあります。");
                    }
                }
                else
                {
                    errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 地盤No.{groundNo}に土質点データがありません。");
                }
            }

            return errors.Length > 0 ? errors.ToString() : null;
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
                        System.Windows.MessageBox.Show("要素分割を行ってください。");
                    else
                        OpenDialogWindow<SettlementViewModel, SettlementWindow>(this);
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
                        // Undoポイントを追加（読込前の状態を保存）
                        _undoManager.SaveState(CurrentInputModel.DeepCopy());

                        var viewModel = new HorizontalCalculationViewModel(this);
                        var window = new HorizontalCalculationWindow { DataContext = viewModel };

                        if (viewModel is ICloseable closeableViewModel)
                        {
                            if (window.IsLoaded && window.IsVisible)
                                window.Close();
                        }

                        try
                        {
                            window.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }

                        // 変更: 即時実行
                        UpdateWindowImmediate();
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
            if (SelectedGroundInputModelNo == 0)
            {
                MessageBox.Show("地盤データが存在しません。");
                return;
            }

            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            var groundInput = CurrentInputModel.GroundsInput[SelectedGroundInputModelNo - 1];
            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Clear();

            double loadingPlaneAltitude = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            foreach (var layer in groundInput.GroundLayers)
            {
                if (layer.BottomAltitude < loadingPlaneAltitude)
                {
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Add(new SettlementSoilLayer
                    {
                        BottomAltitude = layer.BottomAltitude,
                        Ek = layer.Es,
                        PoissonsRatio = DeterminePoissonsRatio(layer.GranularityClass),
                        Thickness = 0
                    });
                }
            }

            CalculateLayerThicknesses(
                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers,
                loadingPlaneAltitude);

            // 変更: 即時実行
            UpdateWindowImmediate();
        }

        // AutoOverturningMomentCommand - 転倒モーメント自動計算
        [RelayCommand]
        private void AutoOverturningMoment()
        {
            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            var window = new AutoOverturningMomentWindow(this);

            var appMain = Application.Current?.MainWindow;
            if (appMain != null)
            {
                try { window.Owner = appMain; }
                catch { }
            }

            window.ShowDialog();

            UpdateSumAndOTM();
            // 変更: 即時実行
            UpdateWindowImmediate();
            UpdateTreeView();
        }

        // AutoActionPointXYCommand - 作用点XY自動設定
        [RelayCommand]
        private void AutoActionPointXY()
        {
            // 作用点を杭配置の重心に移動
            OnMoveForceActionPointToAverageCenter();
        }

        /// <summary>
        /// 選択された杭を削除するコマンド
        /// </summary>
        [RelayCommand]
        private void DeletePiles()
        {
            var col = CurrentInputModel.PileLayoutItems;
            var itemsToRemove = col.Where(x => x.IsSelected).ToList();
            if (itemsToRemove.Count == 0) return;

            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // Undo用にまとめる
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

            UpdatePileLayoutNo();
            RequestUpdateWindow();
        }

        /// <summary>
        /// すべての杭の選択を解除するコマンド
        /// </summary>
        [RelayCommand]
        private void DeselectPiles()
        {
            foreach (var item in CurrentInputModel.PileLayoutItems)
                item.IsSelected = false;

            RequestUpdateWindow();
        }

        /// <summary>
        /// Canvas3D の画像を保存するコマンド
        /// </summary>
        [RelayCommand]
        private void ImageSave()
        {
            if (Canvas3DLayout == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                DefaultExt = ".png",
                FileName = "Canvas3D_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Canvas を RenderTargetBitmap でキャプチャ
                    var bounds = VisualTreeHelper.GetDescendantBounds(Canvas3DLayout);
                    var rtb = new RenderTargetBitmap(
                        (int)Canvas3DLayout.ActualWidth,
                        (int)Canvas3DLayout.ActualHeight,
                        96, 96,
                        PixelFormats.Pbgra32);

                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        var vb = new VisualBrush(Canvas3DLayout);
                        dc.DrawRectangle(vb, null, new Rect(new Point(), new Size(Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight)));
                    }
                    rtb.Render(dv);

                    // エンコーダーを選択
                    BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLower() switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                        ".bmp" => new BmpBitmapEncoder(),
                        _ => new PngBitmapEncoder()
                    };

                    encoder.Frames.Add(BitmapFrame.Create(rtb));

                    using var fs = new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Create);
                    encoder.Save(fs);

                    StatusMessage = $"画像を保存しました: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"画像の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
