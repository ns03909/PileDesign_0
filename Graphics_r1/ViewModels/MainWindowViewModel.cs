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
    /// <summary>
    /// キャンバス編集モード（基礎梁ビジュアル編集）
    /// </summary>
    public enum CanvasEditMode
    {
        /// <summary>通常モード（編集なし）</summary>
        None,
        /// <summary>ノード追加モード</summary>
        AddNode,
        /// <summary>要素追加モード（2クリック方式）</summary>
        AddElement,
        /// <summary>削除モード</summary>
        Delete
    }

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

        /// <summary>
        /// 現在のInputModelのスナップショットをUndo履歴に保存します。
        /// 破壊的な操作（削除など）の前に呼び出してください。
        /// </summary>
        public void SaveUndoState()
        {
            _undoManager.SaveState(CurrentInputModel.DeepCopy());
            RaiseUndoStateChanged();
        }
        private readonly FileOperationService _fileOperationService;
        private readonly PileLayoutService _pileLayoutService;
        private readonly SettlementAnalysisService _settlementAnalysisService;
        private readonly AutoSaveService _autoSaveService;
        private readonly MruService _mruService;

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
            SaveUndoState();

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
            ObservableCollection<SettlementSoilLayer> layers,
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
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
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
                    if (p.GetValue(this) is not ICommand cmdObj) continue;

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

                    // PileLayoutItems の CollectionChanged を再購読
                    if (_currentInputModel?.PileLayoutItems != null)
                    {
                        _currentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
                        _currentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
                    }

                    // 注: UpdateWindowImmediate() はここでは呼ばない。
                    // 全ての代入元（ファイル読込、Undo/Redo等）がフラグリセット後に
                    // 明示的に UpdateWindowImmediate() を呼ぶため、ここで呼ぶと
                    // 不完全な状態での二重描画になる。
                    RaiseAllCommandsCanExecute();

                    OnPropertyChanged(nameof(CurrentInputModel));
                    OnPropertyChanged(nameof(PileCountText));
                    OnPropertyChanged(nameof(AnalysisStatusText));
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
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        // アセンブリからバージョン文字列を取得
        private static readonly string _appVersion =
            System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "不明";

        /// <summary>アプリケーションバージョン（他のクラスからも参照可能）</summary>
        public static string AppVersion => _appVersion;

        // ウィンドウタイトル（ファイル名表示）
        public string WindowTitle
        {
            get
            {
                const string appName = "杭基礎検討プログラム";
                string ver = $"v{_appVersion}";
                if (string.IsNullOrEmpty(CurrentFilePath))
                    return $"{appName} {ver} - [新規]";
                return $"{appName} {ver} - {System.IO.Path.GetFileName(CurrentFilePath)}";
            }
        }

        // 修正例: Canvas3DLayout
        private Canvas? _canvas3DLayout;

        public Canvas? Canvas3DLayout
        {
            get => _canvas3DLayout;
            set => SetProperty(ref _canvas3DLayout, value);
        }

        // エクスポート用キャプチャ中フラグ（SetCtの自動上書きをスキップする）
        public bool IsCapturingForExport { get; set; }

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
            // θ=-90, φ=90、透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
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
            // 透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
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
            // 透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
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
                OnPropertyChanged(nameof(GroupPileSettlementXMin));
                OnPropertyChanged(nameof(GroupPileSettlementXMax));
                OnPropertyChanged(nameof(GroupPileSettlementYMin));
                OnPropertyChanged(nameof(GroupPileSettlementYMax));
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
                    IsVerticalBeamAnalysisDone = false;
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

        // 通り心選択対象距離 (m)
        private double _gridSelectionDistance = 0.1;
        public double GridSelectionDistance
        {
            get => _gridSelectionDistance;
            set => SetProperty(ref _gridSelectionDistance, value);
        }

        // GridX追加メソッド
        [RelayCommand]
        private void AddGridX()
        {
            // Undoポイントを追加（1回の追加を1ステップで戻せるようにする）
            TrySaveUndoSnapshotSafely();

            // 防波堤: null の場合はここで生成
            CurrentInputModel.GridXItems ??= [];
            AddGrid(CurrentInputModel.GridXItems, "X1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridXItems));
        }

        // GridY追加メソッド
        [RelayCommand]
        private void AddGridY()
        {
            TrySaveUndoSnapshotSafely();
            CurrentInputModel.GridYItems ??= [];
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
        private void SelectGridX(object parameter)
        {
            if (parameter is not GridDataItem gridItem) return;
            double coord = gridItem.Coord;
            double tolerance = GridSelectionDistance;

            ClearAllSelections();

            // 通り上の杭配置を選択
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                if (Math.Abs(pile.X - coord) <= tolerance)
                    pile.IsSelected = true;
            }

            // 通り上の一般節点を選択
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General && Math.Abs(node.X - coord) <= tolerance)
                        node.IsSelected = true;
                }
            }

            RequestUpdateWindow();
        }

        [RelayCommand]
        private void SelectGridY(object parameter)
        {
            if (parameter is not GridDataItem gridItem) return;
            double coord = gridItem.Coord;
            double tolerance = GridSelectionDistance;

            ClearAllSelections();

            // 通り上の杭配置を選択
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                if (Math.Abs(pile.Y - coord) <= tolerance)
                    pile.IsSelected = true;
            }

            // 通り上の一般節点を選択
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General && Math.Abs(node.Y - coord) <= tolerance)
                        node.IsSelected = true;
                }
            }

            RequestUpdateWindow();
        }

        /// <summary>
        /// すべての選択状態をクリアするヘルパー
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (var pile in CurrentInputModel.PileLayoutItems)
                pile.IsSelected = false;
            if (CurrentInputModel.InputNodes != null)
                foreach (var node in CurrentInputModel.InputNodes)
                    node.IsSelected = false;
            if (CurrentInputModel.FoundationBeamInput != null)
            {
                foreach (var node in CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsSelected = false;
                foreach (var beam in CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsSelected = false;
            }
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
            if (!CheckAndResetAnalysisResults()) return;

            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

        [RelayCommand]
        private void OnComputePileSpacingFactor()
        {
            if (!CheckAndResetAnalysisResults()) return;

            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

        // 要素の節点位置での分割
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null ||
                CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            var nodes = CurrentInputModel.FoundationBeamInput.Nodes;
            var newBeams = new List<FoundationBeamElement>();
            var toRemove = new List<FoundationBeamElement>();

            int originalCount = beams.Count;

            foreach (var beam in beams.ToList())
            {
                if (beam.IsSelected)
                {
                    var splitBeams = SplitBeamByNodes(beam, nodes);
                    if (splitBeams.Count > 1)
                    {
                        toRemove.Add(beam);
                        newBeams.AddRange(splitBeams);
                    }
                }
            }

            foreach (var beam in toRemove)
                beams.Remove(beam);

            foreach (var beam in newBeams)
                beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            int newCount = beams.Count;
            System.Windows.MessageBox.Show(
                $"{toRemove.Count} 個の要素を {newCount - originalCount + toRemove.Count} 個に分割しました。",
                "節点分割完了",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        // 選択された梁要素を等分割するコマンド
        [RelayCommand]
        private void EqualDivideElements()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0)
            {
                MessageBox.Show("分割する梁要素を選択してください。");
                return;
            }

            SaveUndoState();

            int n = EqualDivisionCount;
            var toRemove = new List<FoundationBeamElement>();
            var toAdd = new List<FoundationBeamElement>();

            foreach (var beam in selectedBeams)
            {
                // 始終点の座標を取得（NodeI_Type/NodeI_Id 方式）
                var coordsI = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var coordsJ = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (coordsI == null || coordsJ == null) continue;

                // 分割点に一般節点を生成
                var divisionNodes = new List<InputNode>();
                for (int i = 1; i < n; i++)
                {
                    double t = (double)i / n;
                    var newNode = new InputNode
                    {
                        No = CurrentInputModel.InputNodes.Count + divisionNodes.Count + 1,
                        Type = NodeType.General,
                        X = coordsI.Value.X + (coordsJ.Value.X - coordsI.Value.X) * t,
                        Y = coordsI.Value.Y + (coordsJ.Value.Y - coordsI.Value.Y) * t,
                        Z = coordsI.Value.Z + (coordsJ.Value.Z - coordsI.Value.Z) * t
                    };
                    divisionNodes.Add(newNode);
                }

                foreach (var node in divisionNodes)
                    CurrentInputModel.InputNodes.Add(node);

                // 分割ビームを生成（I → div1 → div2 → ... → J）
                // 最初のセグメント: 元のNodeI → 最初の分割節点
                toAdd.Add(new FoundationBeamElement
                {
                    NodeI_Type = beam.NodeI_Type,
                    NodeI_Id = beam.NodeI_Id,
                    NodeJ_Type = NodeReferenceType.GeneralNode,
                    NodeJ_Id = divisionNodes[0].UniqueId,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });

                // 中間セグメント
                for (int i = 0; i < divisionNodes.Count - 1; i++)
                {
                    toAdd.Add(new FoundationBeamElement
                    {
                        NodeI_Type = NodeReferenceType.GeneralNode,
                        NodeI_Id = divisionNodes[i].UniqueId,
                        NodeJ_Type = NodeReferenceType.GeneralNode,
                        NodeJ_Id = divisionNodes[i + 1].UniqueId,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        AngleBeta = beam.AngleBeta,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        SectionName = beam.SectionName
                    });
                }

                // 最後のセグメント: 最後の分割節点 → 元のNodeJ
                toAdd.Add(new FoundationBeamElement
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = divisionNodes.Last().UniqueId,
                    NodeJ_Type = beam.NodeJ_Type,
                    NodeJ_Id = beam.NodeJ_Id,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });

                toRemove.Add(beam);
            }

            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            MessageBox.Show(
                $"{toRemove.Count} 個の要素を {n} 等分しました（{toAdd.Count} 個の要素、{toRemove.Count * (n - 1)} 個の節点を生成）。",
                "等分割完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 梁要素を節点で分割するメソッド
        private List<FoundationBeamElement> SplitBeamByNodes(FoundationBeamElement beam, ObservableCollection<FoundationNode> allNodes)
        {
            var result = new List<FoundationBeamElement>();

            // 始点・終点の節点を取得
            var nodeI = allNodes.FirstOrDefault(n => n.No == beam.NodeI_No);
            var nodeJ = allNodes.FirstOrDefault(n => n.No == beam.NodeJ_No);

            if (nodeI == null || nodeJ == null) return [beam]; // 節点が見つからない場合は分割しない

            Point3D pointI = new(nodeI.X, nodeI.Y, nodeI.Z);
            Point3D pointJ = new(nodeJ.X, nodeJ.Y, nodeJ.Z);

            // 線上にある中間節点を探す
            var intermediateNodes = new List<(FoundationNode node, double distance)>();

            foreach (var node in allNodes)
            {
                if (node.No == beam.NodeI_No || node.No == beam.NodeJ_No) continue; // 始点・終点は除外

                Point3D point = new(node.X, node.Y, node.Z);
                double dist = PointToLineDistance(point, pointI, pointJ);

                if (dist <= EditDistanceThreshold)
                {
                    double alongDist = DistanceAlongLine(point, pointI, pointJ);
                    if (alongDist > 0 && alongDist < (pointJ - pointI).Length)
                    {
                        intermediateNodes.Add((node, alongDist));
                    }
                }
            }

            // 中間節点がない場合は分割しない
            if (intermediateNodes.Count == 0) return [beam];

            // 距離順にソート
            var sortedNodes = intermediateNodes.OrderBy(n => n.distance).Select(n => n.node).ToList();

            // 始点から各中間節点、最後の中間節点から終点まで梁を作成
            var allSplitNodes = new List<FoundationNode> { nodeI };
            allSplitNodes.AddRange(sortedNodes);
            allSplitNodes.Add(nodeJ);

            for (int i = 0; i < allSplitNodes.Count - 1; i++)
            {
                result.Add(new FoundationBeamElement
                {
                    NodeI_No = allSplitNodes[i].No,
                    NodeJ_No = allSplitNodes[i + 1].No,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });
            }

            return result;
        }

        // 点から線分への距離を計算
        private static double PointToLineDistance(Point3D point, Point3D lineStart, Point3D lineEnd)
        {
            Vector3D line = lineEnd - lineStart;
            Vector3D pointVector = point - lineStart;

            double lineLength = line.Length;
            if (lineLength == 0) return (point - lineStart).Length;

            double t = Vector3D.DotProduct(pointVector, line) / (lineLength * lineLength);
            t = Math.Max(0, Math.Min(1, t)); // clamp to [0, 1]

            Point3D projection = lineStart + t * line;
            return (point - projection).Length;
        }

        // 線分に沿った距離を計算
        private static double DistanceAlongLine(Point3D point, Point3D lineStart, Point3D lineEnd)
        {
            Vector3D line = lineEnd - lineStart;
            Vector3D pointVector = point - lineStart;

            double lineLength = line.Length;
            if (lineLength == 0) return 0;

            double t = Vector3D.DotProduct(pointVector, line) / (lineLength * lineLength);
            return t * lineLength;
        }

        private static int GetIndexOfNthSmallestValue(List<double> distances, int n)
        {
            var indexedDistances = distances
                .Select((value, index) => new { Value = value, Index = index })
                .OrderBy(pair => pair.Value)
                .ToList();

            return indexedDistances[n].Index;
        }

        /// <summary>
        /// 2つの3D線分の最近接点を求め、交差判定を行う。
        /// 端点同士の交差（t≈0,1 or s≈0,1）は除外する。
        /// </summary>
        /// <returns>交差点と各線分上のパラメータ t, s。交差しない場合は null。</returns>
        private (Point3D point, double t, double s)? FindSegmentIntersection(
            Point3D p1, Point3D p2, Point3D p3, Point3D p4, double tolerance)
        {
            var d1 = p2 - p1; // 線分Aの方向ベクトル
            var d2 = p4 - p3; // 線分Bの方向ベクトル
            var r = p1 - p3;

            double a = Vector3D.DotProduct(d1, d1); // |d1|^2
            double e = Vector3D.DotProduct(d2, d2); // |d2|^2
            double f = Vector3D.DotProduct(d2, r);

            // 両方の線分が点に退化している場合
            if (a < 1e-12 && e < 1e-12) return null;

            double b = Vector3D.DotProduct(d1, d2);
            double c = Vector3D.DotProduct(d1, r);
            double denom = a * e - b * b;

            // 平行（または非常に近い）線分
            if (Math.Abs(denom) < 1e-12) return null;

            double t = (b * f - c * e) / denom;
            double s = (a * f - b * c) / denom;

            // 端点付近は除外（端点での接続は交差ではない）
            const double endEps = 1e-6;
            if (t <= endEps || t >= 1.0 - endEps) return null;
            if (s <= endEps || s >= 1.0 - endEps) return null;

            // 最近接点
            var closestA = p1 + t * d1;
            var closestB = p3 + s * d2;
            double dist = (closestA - closestB).Length;

            if (dist > tolerance) return null;

            // 交差点は両最近接点の中点
            var intersection = new Point3D(
                (closestA.X + closestB.X) * 0.5,
                (closestA.Y + closestB.Y) * 0.5,
                (closestA.Z + closestB.Z) * 0.5);

            return (intersection, t, s);
        }

        /// <summary>
        /// 1つの梁要素を複数の交差点(InputNode)で分割し、分割後の要素リストを返す。
        /// </summary>
        private List<FoundationBeamElement> SplitBeamAtPoints(
            FoundationBeamElement beam,
            List<(InputNode node, double t)> splitPoints)
        {
            if (splitPoints.Count == 0) return [beam];

            // tの昇順にソート
            var sorted = splitPoints.OrderBy(sp => sp.t).ToList();

            var result = new List<FoundationBeamElement>();

            // 最初のセグメント: 元のNodeI → 最初の分割節点
            result.Add(new FoundationBeamElement
            {
                NodeI_Type = beam.NodeI_Type,
                NodeI_Id = beam.NodeI_Id,
                NodeJ_Type = NodeReferenceType.GeneralNode,
                NodeJ_Id = sorted[0].node.UniqueId,
                MaterialNo = beam.MaterialNo,
                SectionNo = beam.SectionNo,
                AngleBeta = beam.AngleBeta,
                Width = beam.Width,
                Height = beam.Height,
                YoungModulus = beam.YoungModulus,
                ShearModulus = beam.ShearModulus,
                SectionName = beam.SectionName
            });

            // 中間セグメント
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                result.Add(new FoundationBeamElement
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = sorted[i].node.UniqueId,
                    NodeJ_Type = NodeReferenceType.GeneralNode,
                    NodeJ_Id = sorted[i + 1].node.UniqueId,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });
            }

            // 最後のセグメント: 最後の分割節点 → 元のNodeJ
            result.Add(new FoundationBeamElement
            {
                NodeI_Type = NodeReferenceType.GeneralNode,
                NodeI_Id = sorted.Last().node.UniqueId,
                NodeJ_Type = beam.NodeJ_Type,
                NodeJ_Id = beam.NodeJ_Id,
                MaterialNo = beam.MaterialNo,
                SectionNo = beam.SectionNo,
                AngleBeta = beam.AngleBeta,
                Width = beam.Width,
                Height = beam.Height,
                YoungModulus = beam.YoungModulus,
                ShearModulus = beam.ShearModulus,
                SectionName = beam.SectionName
            });

            return result;
        }

        // 交差点で要素分割
        [RelayCommand]
        private void SplitElementsAtIntersections()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count < 2)
            {
                MessageBox.Show("交差判定するには梁要素を2本以上選択してください。",
                    "交差点分割", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveUndoState();

            double tolerance = EditDistanceThreshold;

            // 各要素の座標を事前取得
            var beamCoords = new Dictionary<FoundationBeamElement, (Point3D pi, Point3D pj)>();
            foreach (var beam in selectedBeams)
            {
                var ci = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var cj = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (ci == null || cj == null) continue;
                beamCoords[beam] = (new Point3D(ci.Value.X, ci.Value.Y, ci.Value.Z),
                                    new Point3D(cj.Value.X, cj.Value.Y, cj.Value.Z));
            }

            // 各要素ごとの分割点リスト
            var beamSplitPoints = new Dictionary<FoundationBeamElement, List<(InputNode node, double t)>>();

            // 全ペアの交差判定
            var beamList = beamCoords.Keys.ToList();
            int intersectionCount = 0;

            for (int i = 0; i < beamList.Count; i++)
            {
                for (int j = i + 1; j < beamList.Count; j++)
                {
                    var beamA = beamList[i];
                    var beamB = beamList[j];
                    var (pi1, pi2) = beamCoords[beamA];
                    var (pj1, pj2) = beamCoords[beamB];

                    var result = FindSegmentIntersection(pi1, pi2, pj1, pj2, tolerance);
                    if (result == null) continue;

                    var (point, tA, tB) = result.Value;

                    // 同座標に既存節点があるかチェック（重複防止）
                    bool alreadyExists = false;

                    // 既にこの要素ペアで同じ位置に分割点が登録されていないかチェック
                    if (beamSplitPoints.TryGetValue(beamA, out var existingA))
                    {
                        if (existingA.Any(sp => (new Point3D(sp.node.X, sp.node.Y, sp.node.Z) - point).Length < tolerance))
                            alreadyExists = true;
                    }

                    if (alreadyExists) continue;

                    // 交差点に一般節点を生成
                    var newNode = new InputNode
                    {
                        No = CurrentInputModel.InputNodes.Count + 1,
                        Type = NodeType.General,
                        X = point.X,
                        Y = point.Y,
                        Z = point.Z
                    };
                    CurrentInputModel.InputNodes.Add(newNode);

                    // 要素Aの分割点リストに追加
                    if (!beamSplitPoints.ContainsKey(beamA))
                        beamSplitPoints[beamA] = [];
                    beamSplitPoints[beamA].Add((newNode, tA));

                    // 要素Bの分割点リストに追加
                    if (!beamSplitPoints.ContainsKey(beamB))
                        beamSplitPoints[beamB] = [];
                    beamSplitPoints[beamB].Add((newNode, tB));

                    intersectionCount++;
                }
            }

            if (intersectionCount == 0)
            {
                ShowToast("選択要素間に交差点が見つかりませんでした。", 2); // Warning
                return;
            }

            // 交差が検出された要素を分割
            var toRemove = new List<FoundationBeamElement>();
            var toAdd = new List<FoundationBeamElement>();

            foreach (var (beam, splitPoints) in beamSplitPoints)
            {
                var splitBeams = SplitBeamAtPoints(beam, splitPoints);
                toRemove.Add(beam);
                toAdd.AddRange(splitBeams);
            }

            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            ShowToast($"{intersectionCount} 個の交差点で {toRemove.Count} → {toAdd.Count} 要素に分割");
        }

        // 基礎梁節点削除
        [RelayCommand]
        private void DeleteFoundationNode(FoundationNode node)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            // 使用中かチェック（この節点を参照する梁要素があるか）
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b =>
                b.NodeI_No == node.No || b.NodeJ_No == node.No);

            if (isUsed)
            {
                System.Windows.MessageBox.Show(
                    $"節点 {node.No} は梁要素で使用されているため削除できません。\n先に関連する梁要素を削除してください。",
                    "削除エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            TrySaveUndoSnapshotSafely();
            CurrentInputModel.FoundationBeamInput.Nodes.Remove(node);
            RenumberFoundationNodes();
            RequestUpdateWindow();
        }

        // 基礎梁要素削除
        [RelayCommand]
        private void DeleteFoundationBeam(FoundationBeamElement beam)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            TrySaveUndoSnapshotSafely();
            CurrentInputModel.FoundationBeamInput.Beams.Remove(beam);
            RenumberFoundationBeams();
            RequestUpdateWindow();
        }

        // 重複要素削除
        [RelayCommand]
        private void OnDeleteDupulicateElements()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            SaveUndoState();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            var toRemove = new List<FoundationBeamElement>();
            // 既に確認済みのペアを記録（順序なし）
            var seenPairs = new HashSet<(NodeReferenceType, Guid, NodeReferenceType, Guid)>();

            foreach (var beam in beams)
            {
                // 順序を正規化して比較（I,J と J,I を同一視）
                var key1 = (beam.NodeI_Type, beam.NodeI_Id, beam.NodeJ_Type, beam.NodeJ_Id);
                var key2 = (beam.NodeJ_Type, beam.NodeJ_Id, beam.NodeI_Type, beam.NodeI_Id);

                if (seenPairs.Contains(key1) || seenPairs.Contains(key2))
                {
                    toRemove.Add(beam);
                }
                else
                {
                    seenPairs.Add(key1);
                }
            }

            foreach (var beam in toRemove)
                beams.Remove(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            System.Windows.MessageBox.Show(
                $"{toRemove.Count} 個の重複要素を削除しました。",
                "重複削除完了",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        // 自動梁要素生成（X同一・Y同一の杭配置を基礎梁で連結）
        [RelayCommand]
        private void OnAutoGenerateFoundationBeams()
        {
            if (CurrentInputModel?.PileLayoutItems == null ||
                CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            var piles = CurrentInputModel.PileLayoutItems;
            if (piles.Count < 2) return;

            TrySaveUndoSnapshotSafely();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            int addedCount = 0;
            const double tolerance = 1e-3; // 座標一致の許容誤差 (m)

            // 既存ビームのペアセット（重複チェック用）
            var existingPairs = new HashSet<(Guid, Guid)>();
            foreach (var b in beams)
            {
                if (b.NodeI_Type == NodeReferenceType.PileLayout && b.NodeJ_Type == NodeReferenceType.PileLayout)
                {
                    var pair1 = (b.NodeI_Id, b.NodeJ_Id);
                    var pair2 = (b.NodeJ_Id, b.NodeI_Id);
                    existingPairs.Add(pair1);
                    existingPairs.Add(pair2);
                }
            }

            // X座標が同一の杭をグルーピング → Y座標昇順でソートし隣接杭間にビーム生成
            var xGroups = piles
                .GroupBy(p => Math.Round(p.X / tolerance) * tolerance)
                .Where(g => g.Count() >= 2);

            foreach (var group in xGroups)
            {
                var sorted = group.OrderBy(p => p.Y).ToList();
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    var p2 = sorted[i + 1];
                    var pair = (p1.UniqueId, p2.UniqueId);
                    if (existingPairs.Contains(pair)) continue;

                    beams.Add(new FoundationBeamElement
                    {
                        No = beams.Count + 1,
                        NodeI_Type = NodeReferenceType.PileLayout,
                        NodeI_Id = p1.UniqueId,
                        NodeJ_Type = NodeReferenceType.PileLayout,
                        NodeJ_Id = p2.UniqueId,
                        MaterialNo = 1,
                        SectionNo = 1,
                        AngleBeta = 0.0
                    });
                    existingPairs.Add(pair);
                    existingPairs.Add((p2.UniqueId, p1.UniqueId));
                    addedCount++;
                }
            }

            // Y座標が同一の杭をグルーピング → X座標昇順でソートし隣接杭間にビーム生成
            var yGroups = piles
                .GroupBy(p => Math.Round(p.Y / tolerance) * tolerance)
                .Where(g => g.Count() >= 2);

            foreach (var group in yGroups)
            {
                var sorted = group.OrderBy(p => p.X).ToList();
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    var p2 = sorted[i + 1];
                    var pair = (p1.UniqueId, p2.UniqueId);
                    if (existingPairs.Contains(pair)) continue;

                    beams.Add(new FoundationBeamElement
                    {
                        No = beams.Count + 1,
                        NodeI_Type = NodeReferenceType.PileLayout,
                        NodeI_Id = p1.UniqueId,
                        NodeJ_Type = NodeReferenceType.PileLayout,
                        NodeJ_Id = p2.UniqueId,
                        MaterialNo = 1,
                        SectionNo = 1,
                        AngleBeta = 0.0
                    });
                    existingPairs.Add(pair);
                    existingPairs.Add((p2.UniqueId, p1.UniqueId));
                    addedCount++;
                }
            }

            RenumberFoundationBeams();
            RequestUpdateWindow();

            MessageBox.Show(
                $"{addedCount} 本の基礎梁要素を自動生成しました。",
                "自動梁要素生成完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // 基礎梁節点番号振り直し
        private void RenumberFoundationNodes()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            for (int i = 0; i < CurrentInputModel.FoundationBeamInput.Nodes.Count; i++)
                CurrentInputModel.FoundationBeamInput.Nodes[i].No = i + 1;
        }

        // 基礎梁要素番号振り直し
        private void RenumberFoundationBeams()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            for (int i = 0; i < CurrentInputModel.FoundationBeamInput.Beams.Count; i++)
                CurrentInputModel.FoundationBeamInput.Beams[i].No = i + 1;
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
            if (!CheckAndResetAnalysisResults()) return;

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

        // 慣性力作用点をすべての杭接合点の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {
            if (!CheckAndResetAnalysisResults()) return;

            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageBox.Show("杭配置データがありません。");
                return;
            }

            TrySaveUndoSnapshotSafely();

            // 杭接合点（杭頭 + ΔZc）の図心を計算
            var piles = CurrentInputModel.PileLayoutItems;
            double centerX = piles.Average(p => p.X);
            double centerY = piles.Average(p => p.Y);
            double centerZ = piles.Average(p => p.Z + p.FoundationBeamDeltaZc);

            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointY = centerY;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointAltitude = centerZ;

            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointY = centerY;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointAltitude = centerZ;

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
                loadCase.ForceActionPointAltitude = centerZ;
            }

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
                loadCase.ForceActionPointAltitude = centerZ;
            }

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        [RelayCommand]
        private void AutoIsFrontPiles()
        {
            if (!CheckAndResetAnalysisResults()) return;

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
            // 群杭荷重（矩形荷重）が定義されているかチェック
            var rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
            if (rectLoads == null || rectLoads.Count == 0)
            {
                MessageBox.Show("群杭荷重（矩形荷重）が定義されていません。\n荷重タブで矩形荷重を追加してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 荷重値が全て0かチェック
            if (rectLoads.All(r => r.QA == 0))
            {
                MessageBox.Show("値が0の群杭荷重（矩形荷重）しか定義されていません。\n荷重タブで荷重値を設定してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = _settlementAnalysisService.PerformSettlementAnalysis(
                CurrentInputModel.PileGroupSettlement,
                CurrentInputModel.PileLayoutItems,
                CurrentInputModel.ElementDivision.SoilPiles,
                CurrentInputModel.GridXItems,
                CurrentInputModel.GridYItems,
                GroupPileSettlementXMin,
                GroupPileSettlementXMax,
                GroupPileSettlementYMin,
                GroupPileSettlementYMax,
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
                    ShowToast("保存が完了しました。");

                    // MRUに追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, CurrentModel);
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
                    ShowToast("保存が完了しました。");
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

            // 自動保存を停止
            _autoSaveService.Stop();

            CurrentInputModel.Reset();
            this.CurrentModel = null; // AnaModelもリセット
            CurrentFilePath = null;

            // ここで初期状態をUndoスタックに積む
            SaveUndoState();

            // UpdateWindow() 内で UpdateTreeView() も実行されるため別途呼ばない
            UpdateWindowImmediate();
        }

        private void TrySaveUndoSnapshotSafely()
        {
            try
            {
                var snapshot = CurrentInputModel?.DeepCopy();
                if (snapshot != null)
                {
                    _undoManager.SaveState(snapshot);
                    RaiseUndoStateChanged();
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
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
                    return false;
                }

                CurrentInputModel = loaded;
                CurrentInputModel.AttachViewModel(this);
                CurrentFilePath = filePath;
                CurrentInputModel.GridXItems ??= [];
                CurrentInputModel.GridYItems ??= [];

                // 要素分割・解析状態をリセット
                IsElementSplit = false;
                IsHorizontalAnalysisDone = false;
                IsVerticalAnalysisDone = false;
                IsGroupPileSettlementAnalysisDone = false;
                IsVerticalBeamAnalysisDone = false;

                // M-φキャッシュをクリア（新プロジェクト読込時）
                PileSection.ClearMphiCache();

                // 群杭沈下解析結果をクリア
                CurrentInputModel.PileGroupSettlement?.SettlementGridData?.Clear();
                CurrentInputModel.PileGroupSettlement?.SettlementGridX?.Clear();
                CurrentInputModel.PileGroupSettlement?.SettlementGridY?.Clear();

                // UpdateWindow() 内で UpdateTreeView() も実行されるため別途呼ばない
                UpdateWindowImmediate();
                ShowToast("読込が完了しました。");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル読込中にエラーが発生しました。\n{ex.Message}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    SaveUndoState();

                    var projectData = _fileOperationService.LoadProjectData(openFileDialog.FileName);

                    if (projectData != null)
                    {
                        CurrentInputModel = projectData.InputModel;
                        CurrentModel = projectData.AnaModel;

                        // コレクションを ObservableCollection に変換
                        _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

                        // 旧データとの互換性: InputNodesがnullの場合は空のコレクションを作成
                        CurrentInputModel.InputNodes ??= [];

                        // 旧データとの互換性: Element → FoundationBeamElement への自動変換
                        CurrentInputModel.MigrateElementsToFoundationBeams();

                        // 旧データとの互換性: Materials/Sections の初期化
                        CurrentInputModel.EnsureFoundationBeamDefaults();
                        CurrentInputModel.EnsureAnalysisTargetDefaults();

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
                    IsVerticalBeamAnalysisDone = false;

                    // M-φキャッシュをクリア（新プロジェクト読込時）
                    PileSection.ClearMphiCache();

                    // 群杭沈下解析結果をクリア
                    CurrentInputModel.PileGroupSettlement?.SettlementGridData?.Clear();
                    CurrentInputModel.PileGroupSettlement?.SettlementGridX?.Clear();
                    CurrentInputModel.PileGroupSettlement?.SettlementGridY?.Clear();

                    UpdateWindowImmediate();
                    ShowToast("読込が完了しました。");

                    // MRUに追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, CurrentModel);
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
                    MessageBox.Show($"docxファイルが作成されました。\n{saveFileDialog.FileName}\nMSWordでファイルを開き、ctrl + aで全選択した後, F9によりフィールドを更新してください。");

                    // 作成したdocxファイルを自動的に開く
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Word出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Rhino 3D (.3dm) ファイルにエクスポートするメソッド
        [RelayCommand]
        public void Export3dmFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "Rhino 3D (*.3dm)|*.3dm|All files (*.*)|*.*",
                DefaultExt = ".3dm",
                FileName = "PileModel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".3dm"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.Rhino3dmExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"3dmファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"3dm出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // DXF ファイルにエクスポートするメソッド
        [RelayCommand]
        public void ExportDxfFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
                DefaultExt = ".dxf",
                FileName = "PileModel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".dxf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.DxfExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"DXFファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"DXF出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // midas Gen MGT ファイルにエクスポートするメソッド
        [RelayCommand]
        public void ExportMgtFile()
        {
            if (CurrentModel == null)
            {
                MessageBox.Show("水平解析が実行されていません。\n解析モデルをエクスポートするには、先に水平解析を実行してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "midas Gen MGT (*.mgt)|*.mgt|All files (*.*)|*.*",
                DefaultExt = ".mgt",
                FileName = "FEM_Model_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mgt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.MgtExporter(CurrentModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"MGTファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"MGT出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // 解析結果が1つでも存在するか（バインド用プロパティ）
        public bool HasAnyAnalysisResult
            => IsHorizontalAnalysisDone || IsVerticalAnalysisDone || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone;

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
                if (!HasAnyAnalysisResult) return;

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
        private bool CanOpenGraphWindow() => HasAnyAnalysisResult;


        // フィールド追加
        private readonly AnalysisResultTableService _tableService = new();

        // プロパティ
        public IReadOnlyList<ResultTable> LatestResultTables { get; private set; } = [];

        // Latest analysis logs
        public ObservableCollection<string> LatestAnalysisLogs { get; private set; } = [];

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
                !HasAnyAnalysisResult)
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
                return;
            }

            // デバッグ: AnalysisStepResultsの内容を確認
            foreach (var r in CurrentModel.AnalysisStepResults)
            {
            }

            // 全ての解析結果から一意の組み合わせ（LoadCase, LoadCombination, IsLiquefaction）を取得
            // 各組み合わせについて最終ステップのテーブルを生成
            var allTables = new List<ResultTable>();

            var uniqueCombinations = CurrentModel.AnalysisStepResults
                .GroupBy(r => new
                {
                    LoadCaseName = r.LoadCase?.LoadName ?? "",
                    LoadCombinationName = r.LoadCombination?.Name ?? "",
                    r.IsLiquefaction
                })
                .Select(g => g.OrderByDescending(r => r.Step).First()) // 各組み合わせの最終ステップを取得
                .ToList();

            foreach (var c in uniqueCombinations)
            {
            }

            foreach (var stepResult in uniqueCombinations)
            {
                var tables = _tableService.BuildTables(
                    CurrentModel,
                    stepResult.LoadCase,
                    stepResult.LoadCombination,
                    stepResult.IsLiquefaction,
                    stepResult.Step);

                allTables.AddRange(tables);
            }

            foreach (var t in allTables)
            {
            }

            LatestResultTables = allTables;

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

        // 設計例によるプログラムの検証ウィンドウ表示
        [RelayCommand]
        public static void OpenVerificationWindow()
        {
            try
            {
                var window = new VerificationWindow();
                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"検証ウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void OnQuickHint()
        {
            IsQuickHintVisible = true;
        }

        // 再入防止フラグ（原子的に扱う）
        private readonly int _isChangWindowOpeningFlag = 0;

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
                // 杭配置または一般節点のいずれかが選択されていればOK
                bool hasPileLayoutSelected = CurrentInputModel?.PileLayoutItems?.Any(p => p.IsSelected) ?? false;
                bool hasGeneralNodesSelected = CurrentInputModel?.InputNodes?.Any(n => n.Type == NodeType.General && n.IsSelected) ?? false;

                if (!hasPileLayoutSelected && !hasGeneralNodesSelected)
                {
                    MessageBox.Show("杭配置または一般節点が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Undoポイントを追加
                SaveUndoState();

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
                        OnPropertyChanged(nameof(GroupPileSettlementXMin));
                        OnPropertyChanged(nameof(GroupPileSettlementXMax));
                        OnPropertyChanged(nameof(GroupPileSettlementYMin));
                        OnPropertyChanged(nameof(GroupPileSettlementYMax));

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
                MoveNodes(e.DX, e.DY, e.DZ, e.IsInputNodesIncluded, e.IsPileLayoutIncluded);// 移動操作の処理
            else if (e.IsCopy)
                await CopyNodesAsync(e.DX, e.DY, e.DZ, e.RepetitionNumber, e.IsInputNodesIncluded, e.IsPileLayoutIncluded); // 複製操作の処理
        }

        private async Task CopyNodesAsync(double dX, double dY, double dZ, int repetitionNumber, bool isInputNodesIncluded, bool isPileLayoutIncluded)
        {
            // 変更を行う前に、選択されたアイテムのリストを作成
            var selectedItems = isPileLayoutIncluded
                ? CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList()
                : new List<PileLayoutDataItem>();
            var selectedInputNodes = isInputNodesIncluded
                ? (CurrentInputModel.InputNodes?.Where(n => n.IsSelected).ToList() ?? new List<InputNode>())
                : new List<InputNode>();
            int totalCount = (selectedItems.Count + selectedInputNodes.Count) * repetitionNumber;

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
                    dZ,
                    repetitionNumber,
                    item => item.SetMainWindowViewModel(this));

                // InputNodes（一般節点）のコピー
                var newInputNodes = new List<InputNode>();
                foreach (var selectedNode in selectedInputNodes)
                {
                    for (int i = 0; i < repetitionNumber; i++)
                    {
                        var newNode = new InputNode
                        {
                            No = CurrentInputModel.InputNodes.Count + newInputNodes.Count + 1,
                            Type = selectedNode.Type,
                            X = selectedNode.X + dX * (i + 1),
                            Y = selectedNode.Y + dY * (i + 1),
                            Z = selectedNode.Z + dZ * (i + 1),
                            LinkedPileNo = selectedNode.LinkedPileNo,
                            IsVisible = selectedNode.IsVisible
                        };
                        newInputNodes.Add(newNode);
                    }
                }

                // ★ UIスレッドで一括置換（CollectionChangedを1回だけ発火）
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // コレクション全体を置換（CollectionChangedは1回のみ）
                    CurrentInputModel.PileLayoutItems = combined;
                    CurrentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
                    CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
                    OnPropertyChanged(nameof(PileCountText));

                    // InputNodes を追加
                    foreach (var newNode in newInputNodes)
                    {
                        CurrentInputModel.InputNodes.Add(newNode);
                    }

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
        private void MoveNodes(double dX, double dY, double dZ, bool isInputNodesIncluded, bool isPileLayoutIncluded)
        {
            // 杭配置の移動
            if (isPileLayoutIncluded)
            {
                _pileLayoutService.MoveSelectedPiles(CurrentInputModel.PileLayoutItems, dX, dY, dZ);
            }

            // InputNodes（一般節点）の移動
            if (isInputNodesIncluded)
            {
                var selectedInputNodes = CurrentInputModel.InputNodes?.Where(n => n.IsSelected).ToList();
                if (selectedInputNodes != null && selectedInputNodes.Count > 0)
                {
                    foreach (var node in selectedInputNodes)
                    {
                        node.X += dX;
                        node.Y += dY;
                        node.Z += dZ;
                    }
                }
            }
        }

        // コピーを作成して操作を行う
        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            CurrentInputModel.PileLayoutItems = _pileLayoutService.CopySelectedPiles(
                CurrentInputModel.PileLayoutItems,
                dX,
                dY,
                0,
                repetitionNumber,
                item => item.SetMainWindowViewModel(this));
            CurrentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
            CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
            OnPropertyChanged(nameof(PileCountText));

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

                ApplyFoundationBeamDeltaZc = e.IsApplicableFoundationBeamDeltaZc,
                IsAddFoundationBeamDeltaZc = e.IsAddFoundationBeamDeltaZc,
                FoundationBeamDeltaZc = e.FoundationBeamDeltaZc,

                ApplyPileGroupFactor = e.IsApplicablePileGroupFactor,
                IsAddPileGroupFactor = e.IsAddPileGroupFactor,
                PileGroupFactor = e.PileGroupFactor,

                ApplyAxialForceVL = e.IsApplicableVL,
                IsAddAxialForceVL = e.IsAddVL,
                AxialForceVL = e.VL,

                ApplyAxialForceVLAdditional = e.IsApplicableVLadd,
                IsAddAxialForceVLAdditional = e.IsAddVLadd,
                AxialForceVLAdditional = e.VLadd,

                ApplyLevel1 =
                [
                    e.IsApplicableE1_1, e.IsApplicableE1_2, e.IsApplicableE1_3, e.IsApplicableE1_4
                ],
                IsAddLevel1 =
                [
                    e.IsAddE1_1, e.IsAddE1_2, e.IsAddE1_3, e.IsAddE1_4
                ],
                Level1Values =
                [
                    e.E1_1, e.E1_2, e.E1_3, e.E1_4
                ],

                ApplyLevel2 =
                [
                    e.IsApplicableE2_1, e.IsApplicableE2_2, e.IsApplicableE2_3, e.IsApplicableE2_4
                ],
                IsAddLevel2 =
                [
                    e.IsAddE2_1, e.IsAddE2_2, e.IsAddE2_3, e.IsAddE2_4
                ],
                Level2Values =
                [
                    e.E2_1, e.E2_2, e.E2_3, e.E2_4
                ]
            };

            _pileLayoutService.BulkEditSelectedPiles(CurrentInputModel.PileLayoutItems, options);

            // IsFrontPile フラグの処理
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            ApplyIsFrontPileFlags(
                selectedItems,
                [e.IsApplicableIsFrontPile1, e.IsApplicableIsFrontPile2, e.IsApplicableIsFrontPile3, e.IsApplicableIsFrontPile4],
                [e.IsFrontPile1, e.IsFrontPile2, e.IsFrontPile3, e.IsFrontPile4]);
        }

        [RelayCommand]
        private void Undo()
        {
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);

                // UpdateWindow() 内で UpdateTreeView() も実行されるため updateTree: false
                NotifyUIChanged(updateTree: false, immediate: true);
                OnPropertyChanged(nameof(CurrentInputModel));
            }
            RaiseUndoStateChanged();
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);

                // UpdateWindow() 内で UpdateTreeView() も実行されるため updateTree: false
                NotifyUIChanged(updateTree: false, immediate: true);
                OnPropertyChanged(nameof(CurrentInputModel));
            }
            RaiseUndoStateChanged();
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
            CurrentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
            CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
            OnPropertyChanged(nameof(PileCountText));
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

        // 基礎梁ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenFoundationBeamWindow()
        {
            OpenDialogWindowWithUndo<FoundationBeamViewModel, FoundationBeamWindow>();
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

                    if (pileSection.FactoredServiceNMax < force)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n 使用限界軸力適用範囲Max{pileSection.FactoredServiceNMax:N0}kN < {force:N0}kN\n";
                    }
                    if (force < pileSection.FactoredServiceNMin)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n {force:N0}kN < 使用限界軸力適用範囲Min{pileSection.FactoredServiceNMin:N0}kN\n";
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
                SaveUndoState();

                // ウィンドウを即座に表示（重い初期化はContentRenderedイベントで実行）
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
        public async Task OpenLateralLoadAnalysisWindowAsync()
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
                        try
                        {
                            Mouse.OverrideCursor = Cursors.Wait;

                            // Undoポイントを追加（読込前の状態を保存）
                            SaveUndoState();

                            var viewModel = new HorizontalCalculationViewModel(this);
                            var window = new HorizontalCalculationWindow { DataContext = viewModel };

                            if (viewModel is ICloseable closeableViewModel)
                            {
                                if (window.IsLoaded && window.IsVisible)
                                    window.Close();
                            }

                            // ウィンドウを即座に表示（FEMモデル作成はLoadedイベントでバックグラウンド実行）
                            Mouse.OverrideCursor = null;
                            window.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            // 念のため砂時計を戻す
                            Mouse.OverrideCursor = null;
                        }

                        // 変更: 即時実行
                        UpdateWindowImmediate();
                        UpdateTreeView();

                    }
                }
            }
        }

        // 基礎梁鉛直解析ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenVerticalBeamCalculation()
        {
            // バリデーション
            if (CurrentInputModel.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageBox.Show("杭配置が定義されていません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CurrentInputModel.FoundationBeamInput?.Beams == null || CurrentInputModel.FoundationBeamInput.Beams.Count == 0)
            {
                MessageBox.Show("基礎梁要素が定義されていません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // LoadDisplacementsが計算済みか確認
            bool anyMissing = false;
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                var soilPile = pile.SoilPile;
                if (soilPile?.LoadDisplacements == null || soilPile.LoadDisplacements.Count == 0)
                {
                    anyMissing = true;
                    break;
                }
            }
            if (anyMissing)
            {
                MessageBox.Show("単杭沈下解析が未実行の杭があります。先に単杭沈下解析を実行してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var viewModel = new VerticalBeamCalculationViewModel(this);
                var window = new Views.VerticalBeamCalculationWindow { DataContext = viewModel };
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

            SaveUndoState();

            var groundInput = CurrentInputModel.GroundsInput[SelectedGroundInputModelNo - 1];
            double loadingPlaneAltitude = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            // バッチ構築（1件ずつAddするとCollectionChanged連発で遅い）
            var list = new List<SettlementSoilLayer>();
            foreach (var layer in groundInput.GroundLayers)
            {
                if (layer.BottomAltitude < loadingPlaneAltitude)
                {
                    list.Add(new SettlementSoilLayer
                    {
                        BottomAltitude = layer.BottomAltitude,
                        Ek = layer.Es,
                        PoissonsRatio = DeterminePoissonsRatio(layer.GranularityClass),
                        Thickness = 0
                    });
                }
            }

            // 層厚を計算
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Thickness = i == 0
                    ? loadingPlaneAltitude - list[i].BottomAltitude
                    : list[i - 1].BottomAltitude - list[i].BottomAltitude;
            }

            // 一括代入（CollectionChanged は1回だけ）
            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers =
                new ObservableCollection<SettlementSoilLayer>(list);

            // 変更: 即時実行
            UpdateWindowImmediate();
        }

        // AutoOverturningMomentCommand - 転倒モーメント自動計算
        [RelayCommand]
        private void AutoOverturningMoment()
        {
            // Undoポイントを追加
            SaveUndoState();

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
            if (!CheckAndResetAnalysisResults()) return;

            var col = CurrentInputModel.PileLayoutItems;
            var itemsToRemove = col.Where(x => x.IsSelected).ToList();
            if (itemsToRemove.Count == 0) return;

            // Undoポイントを追加
            SaveUndoState();

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
        /// 選択された杭接合節点⇔一般節点を相互変換するコマンド
        /// </summary>
        [RelayCommand]
        private void ConvertNodeType()
        {
            if (!CheckAndResetAnalysisResults()) return;

            var selectedPiles = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            var selectedNodes = CurrentInputModel.InputNodes?
                .Where(n => n.IsSelected && n.Type == NodeType.General).ToList()
                ?? [];

            if (selectedPiles.Count == 0 && selectedNodes.Count == 0) return;

            SaveUndoState();

            // 杭接合節点 → 一般節点: (x, y, z + ΔZc) の位置に一般節点を配置
            foreach (var pile in selectedPiles)
            {
                var newNode = new InputNode
                {
                    No = CurrentInputModel.InputNodes.Count + 1,
                    Type = NodeType.General,
                    X = pile.X,
                    Y = pile.Y,
                    Z = pile.Z + pile.FoundationBeamDeltaZc,
                };

                CurrentInputModel.PileLayoutItems.Remove(pile);
                CurrentInputModel.InputNodes.Add(newNode);
            }

            // 一般節点 → 杭接合節点: (x, y, z - ΔZc) の位置に杭頭節点を配置
            // ΔZc は既存杭配置の最頻値を使用（杭配置がない場合はデフォルト 1.0）
            var deltaZc = GetMostCommonDeltaZc();
            foreach (var node in selectedNodes)
            {
                var newPile = new PileLayoutDataItem
                {
                    X = node.X,
                    Y = node.Y,
                    Z = node.Z - deltaZc,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    FoundationBeamDeltaZc = deltaZc,
                };
                newPile.SetMainWindowViewModel(this);

                CurrentInputModel.InputNodes.Remove(node);
                CurrentInputModel.PileLayoutItems.Add(newPile);
            }

            UpdatePileLayoutNo();
            if (selectedNodes.Count > 0) RequestGenerateSoilPiles();
            RequestUpdateWindow();
        }

        /// <summary>
        /// 既存の杭配置でもっとも多いΔZcを返す（杭配置がない場合はデフォルト 1.0）
        /// </summary>
        private double GetMostCommonDeltaZc()
        {
            var piles = CurrentInputModel.PileLayoutItems;
            if (piles == null || piles.Count == 0) return 1.0;

            return piles
                .GroupBy(p => p.FoundationBeamDeltaZc)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
        }

        /// <summary>
        /// Canvas3D の画像を保存するコマンド
        /// </summary>
        [RelayCommand]
        private void ImageSave(string scaleParam)
        {
            if (Canvas3DLayout == null) return;

            // スケールファクターをパラメータから取得（デフォルト1.0）
            double scale = 1.0;
            if (!string.IsNullOrEmpty(scaleParam) && double.TryParse(scaleParam, out double parsedScale))
            {
                scale = parsedScale;
            }

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
                    int width = (int)(Canvas3DLayout.ActualWidth * scale);
                    int height = (int)(Canvas3DLayout.ActualHeight * scale);

                    // Canvas を RenderTargetBitmap でキャプチャ
                    var rtb = new RenderTargetBitmap(
                        width,
                        height,
                        96 * scale, 96 * scale,
                        PixelFormats.Pbgra32);

                    // DrawingVisualを使用して背景とCanvasを合成
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        // 背景を白で塗りつぶし（Canvasの背景がTransparentなため）
                        dc.DrawRectangle(Brushes.White, null,
                            new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));

                        // VisualBrushでCanvasを描画
                        var vb = new VisualBrush(Canvas3DLayout);
                        dc.DrawRectangle(vb, null,
                            new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));
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

                    StatusMessage = $"画像を保存しました ({width}x{height}): {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"画像の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Canvas3D の画像をクリップボードにコピーするコマンド
        /// </summary>
        [RelayCommand]
        private void ImageCopy(string scaleParam)
        {
            if (Canvas3DLayout == null) return;

            try
            {
                // スケールファクターをパラメータから取得（デフォルト1.0）
                double scale = 1.0;
                if (!string.IsNullOrEmpty(scaleParam) && double.TryParse(scaleParam, out double parsedScale))
                {
                    scale = parsedScale;
                }
                int width = (int)(Canvas3DLayout.ActualWidth * scale);
                int height = (int)(Canvas3DLayout.ActualHeight * scale);

                // Canvas を RenderTargetBitmap でキャプチャ
                var rtb = new RenderTargetBitmap(
                    width,
                    height,
                    96 * scale, 96 * scale,
                    PixelFormats.Pbgra32);

                // DrawingVisualを使用して背景とCanvasを合成
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 背景を白で塗りつぶし（Canvasの背景がTransparentなため）
                    dc.DrawRectangle(Brushes.White, null,
                        new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));

                    // VisualBrushでCanvasを描画
                    var vb = new VisualBrush(Canvas3DLayout);
                    dc.DrawRectangle(vb, null,
                        new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));
                }
                rtb.Render(dv);

                // クリップボードにコピー
                System.Windows.Clipboard.SetImage(rtb);

                StatusMessage = $"画像をクリップボードにコピーしました ({width}x{height})";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"画像のコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アイソメトリック表示でモデル全体（杭先端含む）をキャプチャし、PNGバイト配列を返す。
        /// Word出力用。キャプチャ後にカメラ状態は元に戻す。
        /// </summary>
        public byte[]? CaptureIsometricModelImageBytes()
        {
            if (Canvas3DLayout == null || CurrentInputModel == null || CurrentInputModel.PileLayoutItems.Count == 0)
                return null;

            // --- 1. 現在の状態を保存 ---
            var savedTht = CanvasThreeDView.Tht;
            var savedPhi = CanvasThreeDView.Phi;
            var savedScale = CanvasThreeDView.Scale;
            var savedViewTransition = CanvasThreeDView.ViewTransition;
            var savedCt = CanvasThreeDView.Ct;
            var savedDv0 = CanvasThreeDView.Dv0;
            var savedTickMark = IsTickMarkVisible;
            var savedAxes = IsXYZAxesVisible;

            try
            {
                // SetCt自動上書きをスキップするフラグをON
                IsCapturingForExport = true;

                // --- 2. 杭頭＋杭先端＋地盤範囲を含む全3D点を収集 ---
                var allPoints = new System.Collections.ObjectModel.ObservableCollection<Point3D>();
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                {
                    allPoints.Add(pile.Point3D); // 杭頭

                    int idx = pile.PileBodyNo - 1;
                    if (idx >= 0 && CurrentInputModel.PileBodies != null && idx < CurrentInputModel.PileBodies.Count)
                    {
                        var pileBody = CurrentInputModel.PileBodies[idx];
                        if (pileBody.PileBodySegments != null && pileBody.PileBodySegments.Count > 0)
                        {
                            double totalLen = pileBody.PileBodySegments.Sum(s => s.SegmentLength);
                            allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, pile.Point3D.Z - totalLen));
                        }
                    }

                    int gIdx = pile.GroundNo - 1;
                    if (gIdx >= 0 && CurrentInputModel.GroundsInput != null && gIdx < CurrentInputModel.GroundsInput.Count)
                    {
                        var ground = CurrentInputModel.GroundsInput[gIdx];
                        allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, ground.GroundTopAltitude));
                        if (ground.GroundLayers != null && ground.GroundLayers.Count > 0)
                        {
                            double btmAlt = ground.GroundLayers[^1].BottomAltitude;
                            allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmAlt));
                        }
                    }
                }

                // --- 3. 装飾要素を設定（通り芯は残す） ---
                IsTickMarkVisible = false;
                IsXYZAxesVisible = false;

                // --- 4. 全点を中心にカメラ設定 ---
                CanvasThreeDView.SetCt(allPoints);
                CanvasThreeDView.ViewTransition = new Point(0, 0);

                // --- 5. アイソメ視点に設定（SetCtはスキップされる） ---
                CanvasThreeDView.Tht = -45;
                CanvasThreeDView.Phi = 45;
                Canvas3DLayout.UpdateLayout();
                Canvas3DLayout.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

                // --- 6. 実際のCanvasサイズを取得 ---
                double canvasW = Canvas3DLayout.ActualWidth;
                double canvasH = Canvas3DLayout.ActualHeight;
                if (canvasW <= 0 || canvasH <= 0) return null;

                // --- 7. 全点の2Dバウンディングボックスを計算 ---
                double xMax = double.MinValue, yMax = double.MinValue;
                double xMin = double.MaxValue, yMin = double.MaxValue;
                foreach (var pt3d in allPoints)
                {
                    Point pt2d = CanvasThreeDView.Transformation(pt3d);
                    if (pt2d.X > xMax) xMax = pt2d.X;
                    if (pt2d.Y > yMax) yMax = pt2d.Y;
                    if (pt2d.X < xMin) xMin = pt2d.X;
                    if (pt2d.Y < yMin) yMin = pt2d.Y;
                }
                double bbW = xMax - xMin;
                double bbH = yMax - yMin;
                if (bbW <= 0 || bbH <= 0) return null;

                // --- 8. スケールをフィットさせ、中央に配置 ---
                double gridMargin = GridSymbolZoneWidth * 2; // 通り芯符号用マージン
                double availW = canvasW - gridMargin * 2;
                double availH = canvasH - gridMargin * 2;
                double fitRatio = Math.Min(availW / bbW, availH / bbH);

                // 中央補正: スケール変更後のBB中心がCanvas中心に来るようVTを設定
                double bbCenterX = (xMin + xMax) / 2;
                double bbCenterY = (yMin + yMax) / 2;
                double orgX = canvasW / 2;
                double orgY = canvasH / 2;
                CanvasThreeDView.ViewTransition = new Point(
                    (orgX - bbCenterX) * fitRatio,
                    (orgY - bbCenterY) * fitRatio);
                CanvasThreeDView.Scale *= fitRatio; // re-render triggered

                Canvas3DLayout.UpdateLayout();
                Canvas3DLayout.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

                // --- 9. コンテンツ領域をVisualBrushで切り出してキャプチャ ---
                // 最終的な2D BBを再計算（スケール・VT適用後）
                xMax = double.MinValue; yMax = double.MinValue;
                xMin = double.MaxValue; yMin = double.MaxValue;
                foreach (var pt3d in allPoints)
                {
                    Point pt2d = CanvasThreeDView.Transformation(pt3d);
                    if (pt2d.X > xMax) xMax = pt2d.X;
                    if (pt2d.Y > yMax) yMax = pt2d.Y;
                    if (pt2d.X < xMin) xMin = pt2d.X;
                    if (pt2d.Y < yMin) yMin = pt2d.Y;
                }

                // 通り芯符号用のマージンを追加
                double captureMargin = GridSymbolZoneWidth * 1.5;
                double cropX = Math.Max(0, xMin - captureMargin);
                double cropY = Math.Max(0, yMin - captureMargin);
                double cropR = Math.Min(canvasW, xMax + captureMargin);
                double cropB = Math.Min(canvasH, yMax + captureMargin);
                double cropW = cropR - cropX;
                double cropH = cropB - cropY;
                if (cropW <= 0 || cropH <= 0) return null;

                double capScale = 2.0;
                int outW = (int)(cropW * capScale);
                int outH = (int)(cropH * capScale);
                var rtb = new RenderTargetBitmap(outW, outH, 96 * capScale, 96 * capScale, PixelFormats.Pbgra32);

                // 白背景
                var bgVisual = new DrawingVisual();
                using (var dc = bgVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cropW, cropH));
                }
                rtb.Render(bgVisual);

                // VisualBrushでコンテンツ領域を切り出し
                var contentVisual = new DrawingVisual();
                using (var dc = contentVisual.RenderOpen())
                {
                    var vb = new VisualBrush(Canvas3DLayout)
                    {
                        Viewbox = new Rect(cropX, cropY, cropW, cropH),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Uniform
                    };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, cropW, cropH));
                }
                rtb.Render(contentVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            finally
            {
                // --- フラグをOFF ---
                IsCapturingForExport = false;

                // --- 装飾復元 ---
                IsTickMarkVisible = savedTickMark;
                IsXYZAxesVisible = savedAxes;

                // --- カメラ復元 ---
                CanvasThreeDView.Dv0 = savedDv0;
                CanvasThreeDView.Ct = savedCt;
                CanvasThreeDView.ViewTransition = savedViewTransition;
                CanvasThreeDView.Scale = savedScale;
                CanvasThreeDView.Tht = savedTht;
                CanvasThreeDView.Phi = savedPhi;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        /// <summary>
        /// 自動保存完了時のイベントハンドラ
        /// </summary>
        private void OnAutoSaveCompleted(object? sender, AutoSaveEventArgs e)
        {
            if (e.Success)
            {
                StatusMessage = $"自動保存完了 ({e.Timestamp:HH:mm:ss})";
            }
            else
            {
                StatusMessage = $"自動保存失敗: {e.ErrorMessage}";
            }
        }

        /// <summary>
        /// MRUリスト変更時のイベントハンドラ
        /// </summary>
        private void OnMruListChanged(object? sender, EventArgs e)
        {
            // ObservableCollectionを更新
            MruItems.Clear();
            foreach (var item in _mruService.Items)
            {
                MruItems.Add(item);
            }
        }

        /// <summary>
        /// MRUからファイルを開く
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        [RelayCommand]
        public void OpenFromMru(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show($"ファイルが見つかりません。\n{filePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                _mruService.RemoveFile(filePath);
                return;
            }

            try
            {
                // Undoポイントを追加
                SaveUndoState();

                var projectData = _fileOperationService.LoadProjectData(filePath);

                if (projectData != null)
                {
                    CurrentInputModel = projectData.InputModel;
                    CurrentModel = projectData.AnaModel;

                    _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

                    // 旧データとの互換性
                    CurrentInputModel.InputNodes ??= [];
                    CurrentInputModel.MigrateElementsToFoundationBeams();
                    CurrentInputModel.EnsureFoundationBeamDefaults();
                    CurrentInputModel.EnsureAnalysisTargetDefaults();

                    OnPropertyChanged(nameof(CurrentInputModel));
                }
                else
                {
                    var ok = TryLoadInputModelFileUsingInputModelLoader(filePath);
                    if (!ok)
                        throw new InvalidOperationException("ファイル形式が不正です。");
                    return;
                }

                CurrentInputModel.AttachViewModel(this);
                CurrentFilePath = filePath;

                // MRUに追加
                _mruService.AddFile(filePath);

                IsElementSplit = false;
                IsHorizontalAnalysisDone = false;
                IsVerticalAnalysisDone = false;
                IsGroupPileSettlementAnalysisDone = false;
                IsVerticalBeamAnalysisDone = false;

                PileSection.ClearMphiCache();

                CurrentInputModel.PileGroupSettlement?.SettlementGridData?.Clear();
                CurrentInputModel.PileGroupSettlement?.SettlementGridX?.Clear();
                CurrentInputModel.PileGroupSettlement?.SettlementGridY?.Clear();

                UpdateWindowImmediate();
                ShowToast("読込が完了しました。");

                // 自動保存を開始
                _autoSaveService.Start(CurrentFilePath, CurrentInputModel, CurrentModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読込に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 起動時に自動保存ファイルの復元を確認
        /// </summary>
        public void CheckAutoSaveRestore()
        {
            var latestAutoSave = _autoSaveService.GetLatestAutoSaveFile();
            if (string.IsNullOrEmpty(latestAutoSave))
                return;

            var fileInfo = new System.IO.FileInfo(latestAutoSave);
            var timeSinceAutoSave = DateTime.Now - fileInfo.CreationTime;

            // 24時間以内の自動保存ファイルのみ復元提案
            if (timeSinceAutoSave.TotalHours > 24)
                return;

            var result = MessageBox.Show(
                $"自動保存されたファイルが見つかりました。\n\n" +
                $"保存日時: {fileInfo.CreationTime:yyyy/MM/dd HH:mm:ss}\n" +
                $"ファイル: {System.IO.Path.GetFileName(latestAutoSave)}\n\n" +
                $"このファイルを復元しますか？",
                "自動保存ファイルの復元",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var projectData = _fileOperationService.LoadProjectData(latestAutoSave);
                    if (projectData != null)
                    {
                        CurrentInputModel = projectData.InputModel;
                        CurrentModel = projectData.AnaModel;

                        _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

                        // 旧データとの互換性
                        CurrentInputModel.InputNodes ??= [];
                        CurrentInputModel.MigrateElementsToFoundationBeams();
                        CurrentInputModel.EnsureFoundationBeamDefaults();
                        CurrentInputModel.EnsureAnalysisTargetDefaults();

                        OnPropertyChanged(nameof(CurrentInputModel));

                        CurrentInputModel.AttachViewModel(this);

                        // ファイルパスは元のファイル名から推測（自動保存ファイル名から取得）
                        var originalFileName = System.IO.Path.GetFileNameWithoutExtension(latestAutoSave);
                        var autoSaveIndex = originalFileName.IndexOf("_autosave_");
                        if (autoSaveIndex > 0)
                        {
                            originalFileName = originalFileName[..autoSaveIndex];
                            // 元のファイルパスを推測（未保存ならnull）
                            CurrentFilePath = originalFileName != "Untitled" ? originalFileName + ".json" : null;
                        }

                        IsElementSplit = false;
                        IsHorizontalAnalysisDone = false;
                        IsVerticalAnalysisDone = false;
                        IsGroupPileSettlementAnalysisDone = false;

                        PileSection.ClearMphiCache();

                        CurrentInputModel.PileGroupSettlement?.SettlementGridData?.Clear();
                        CurrentInputModel.PileGroupSettlement?.SettlementGridX?.Clear();
                        CurrentInputModel.PileGroupSettlement?.SettlementGridY?.Clear();

                        UpdateWindowImmediate();
                        ShowToast("自動保存ファイルの復元が完了しました。");

                        // 復元後は自動保存を開始
                        if (!string.IsNullOrEmpty(CurrentFilePath))
                        {
                            _autoSaveService.Start(CurrentFilePath, CurrentInputModel, CurrentModel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"自動保存ファイルの復元に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ==================== InputNode 管理機能 ====================

        /// <summary>
        /// 選択中の一般節点
        /// </summary>
        private InputNode? _selectedInputNode;
        public InputNode? SelectedInputNode
        {
            get => _selectedInputNode;
            set => SetProperty(ref _selectedInputNode, value);
        }

        /// <summary>
        /// 一般節点を追加
        /// </summary>
        [RelayCommand]
        private void AddInputNode()
        {
            if (CurrentInputModel?.InputNodes == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            // 次の節点位置を決定
            Point3D nextPoint3D;
            if (CurrentInputModel.InputNodes.Count == 0)
            {
                // 一点目は(0, 0, ΔZc=1.0)に配置
                nextPoint3D = new Point3D(0, 0, 1.0);
            }
            else
            {
                // 二点目以降は直前の一般節点からX方向に7.2mオフセット
                var lastNode = CurrentInputModel.InputNodes.Last();
                nextPoint3D = new Point3D(lastNode.X, lastNode.Y, lastNode.Z) + new Vector3D() { X = 7.2 };
            }

            var newNode = new InputNode
            {
                No = CurrentInputModel.InputNodes.Count + 1,
                Type = NodeType.General,
                X = nextPoint3D.X,
                Y = nextPoint3D.Y,
                Z = nextPoint3D.Z
            };

            CurrentInputModel.InputNodes.Add(newNode);
            SaveUndoState();
            RequestUpdateWindow();
        }

        /// <summary>
        /// 重複一般節点を削除（同一座標の節点を統合し、梁要素の参照を付け替える）
        /// </summary>
        [RelayCommand]
        private void DeleteDuplicateInputNodes()
        {
            if (CurrentInputModel?.InputNodes == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            const double tol = 1e-6;
            var nodes = CurrentInputModel.InputNodes;
            var toRemove = new List<InputNode>();
            // 削除される節点 → 残す節点 のマッピング
            var mergeMap = new Dictionary<Guid, Guid>();

            for (int i = 0; i < nodes.Count; i++)
            {
                if (toRemove.Contains(nodes[i])) continue;

                for (int j = i + 1; j < nodes.Count; j++)
                {
                    if (toRemove.Contains(nodes[j])) continue;

                    if (Math.Abs(nodes[i].X - nodes[j].X) < tol &&
                        Math.Abs(nodes[i].Y - nodes[j].Y) < tol &&
                        Math.Abs(nodes[i].Z - nodes[j].Z) < tol)
                    {
                        mergeMap[nodes[j].UniqueId] = nodes[i].UniqueId;
                        toRemove.Add(nodes[j]);
                    }
                }
            }

            if (toRemove.Count == 0)
            {
                MessageBox.Show("重複する一般節点はありませんでした。", "重複節点削除",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveUndoState();

            // 梁要素の参照を付け替え
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams != null)
            {
                foreach (var beam in beams)
                {
                    if (beam.NodeI_Type == NodeReferenceType.GeneralNode &&
                        mergeMap.TryGetValue(beam.NodeI_Id, out var newI))
                    {
                        beam.NodeI_Id = newI;
                    }
                    if (beam.NodeJ_Type == NodeReferenceType.GeneralNode &&
                        mergeMap.TryGetValue(beam.NodeJ_Id, out var newJ))
                    {
                        beam.NodeJ_Id = newJ;
                    }
                }
            }

            // 重複節点を削除
            foreach (var node in toRemove)
                nodes.Remove(node);

            // 番号を振り直し
            for (int i = 0; i < nodes.Count; i++)
                nodes[i].No = i + 1;

            RequestUpdateWindow();

            MessageBox.Show(
                $"{toRemove.Count} 個の重複一般節点を削除しました。",
                "重複節点削除", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 選択した一般節点をコピー
        /// </summary>
        [RelayCommand]
        private void CopyInputNode(InputNode? sourceNode)
        {
            if (sourceNode == null) return;

            var newNode = new InputNode
            {
                No = CurrentInputModel.InputNodes.Count + 1,
                Type = sourceNode.Type,
                X = sourceNode.X,
                Y = sourceNode.Y,
                Z = sourceNode.Z,
                LinkedPileNo = sourceNode.LinkedPileNo
            };

            CurrentInputModel.InputNodes.Add(newNode);
            SaveUndoState();
            RequestUpdateWindow();
        }

        /// <summary>
        /// 一般節点を削除
        /// </summary>
        [RelayCommand]
        private void DeleteInputNode(InputNode? node)
        {
            if (node == null) return;

            var result = MessageBox.Show(
                $"節点 No.{node.No} を削除してもよろしいですか？",
                "確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
            {
                CurrentInputModel.InputNodes.Remove(node);
                SaveUndoState();
                RequestUpdateWindow();
            }
        }

        // 材料ウィザードコマンド
        [RelayCommand]
        private void OpenMaterialWizard()
        {
            if (CurrentInputModel.FoundationBeamInput == null)
            {
                CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var wizard = new Views.BeamMaterialWizardWindow(CurrentInputModel.FoundationBeamInput.Materials);

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    int? addedMaterialNo = null;

                    if (wizard.SelectedMaterialNo.HasValue)
                    {
                        // 既存材料の編集
                        var material = CurrentInputModel.FoundationBeamInput.Materials.FirstOrDefault(m => m.No == wizard.SelectedMaterialNo.Value);
                        if (material != null)
                        {
                            material.Name = wizard.Result.Name;
                            material.YoungModulus = wizard.Result.YoungModulus;
                            material.ShearModulus = wizard.Result.ShearModulus;
                            material.PoissonRatio = wizard.Result.PoissonRatio;
                        }
                    }
                    else
                    {
                        // 新規材料の追加
                        var newMaterial = new BeamMaterial
                        {
                            No = (CurrentInputModel.FoundationBeamInput.Materials.Count > 0)
                                ? CurrentInputModel.FoundationBeamInput.Materials.Max(m => m.No) + 1
                                : 1,
                            Name = wizard.Result.Name,
                            YoungModulus = wizard.Result.YoungModulus,
                            ShearModulus = wizard.Result.ShearModulus,
                            PoissonRatio = wizard.Result.PoissonRatio
                        };
                        CurrentInputModel.FoundationBeamInput.Materials.Add(newMaterial);
                        addedMaterialNo = newMaterial.No;
                    }

                    SaveUndoState();
                    RequestUpdateWindow();

                    // Applyボタンが押された場合は編集を継続
                    if (wizard.IsApplyClicked)
                    {
                        // 新規作成の場合、作成された材料を選択状態にする
                        if (addedMaterialNo.HasValue)
                        {
                            wizard = new Views.BeamMaterialWizardWindow(CurrentInputModel.FoundationBeamInput.Materials);
                            // 追加された材料を選択（ComboBoxのインデックスは「新規」の分だけオフセット）
                            var addedMaterial = CurrentInputModel.FoundationBeamInput.Materials.FirstOrDefault(m => m.No == addedMaterialNo.Value);
                            if (addedMaterial != null)
                            {
                                wizard.SelectMaterial(addedMaterial);
                            }
                        }
                        continueEditing = true;
                    }
                    else
                    {
                        continueEditing = false;
                    }
                }
                else
                {
                    // キャンセルされた
                    continueEditing = false;
                }
            }
        }

        // 全断面ねじれ剛性無視コマンド
        [RelayCommand]
        private void ClearAllTorsionalStiffness()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Sections == null ||
                CurrentInputModel.FoundationBeamInput.Sections.Count == 0) return;

            if (!CheckAndResetAnalysisResults()) return;

            TrySaveUndoSnapshotSafely();

            foreach (var section in CurrentInputModel.FoundationBeamInput.Sections)
            {
                section.IxxFactor = 0.0;
                // Ixx係数=0 に合わせて TorsionalMoment も再計算
                section.TorsionalMoment = 0.0;
            }

            RequestUpdateWindow();
        }

        // 断面ウィザードコマンド
        [RelayCommand]
        private void OpenSectionWizard()
        {
            if (CurrentInputModel.FoundationBeamInput == null)
            {
                CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var wizard = new Views.BeamSectionWizardWindow(CurrentInputModel.FoundationBeamInput.Sections);

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    int? addedSectionNo = null;

                    if (wizard.SelectedSectionNo.HasValue)
                    {
                        // 既存断面の編集
                        var section = CurrentInputModel.FoundationBeamInput.Sections.FirstOrDefault(s => s.No == wizard.SelectedSectionNo.Value);
                        if (section != null)
                        {
                            section.Name = wizard.Result.Name;
                            section.Width = wizard.Result.Width;
                            section.Height = wizard.Result.Height;
                            section.Area = wizard.Result.Area;
                            section.ShearAreaY = wizard.Result.ShearAreaY;
                            section.ShearAreaZ = wizard.Result.ShearAreaZ;
                            section.TorsionalMoment = wizard.Result.TorsionalMoment;
                            section.MomentOfInertiaYY = wizard.Result.MomentOfInertiaYY;
                            section.MomentOfInertiaZZ = wizard.Result.MomentOfInertiaZZ;
                            section.AFactor = wizard.Result.AFactor;
                            section.AyFactor = wizard.Result.AyFactor;
                            section.AzFactor = wizard.Result.AzFactor;
                            section.IxxFactor = wizard.Result.IxxFactor;
                            section.IyyFactor = wizard.Result.IyyFactor;
                            section.IzzFactor = wizard.Result.IzzFactor;
                        }
                    }
                    else
                    {
                        // 新規断面の追加
                        var newSection = new BeamSection
                        {
                            No = (CurrentInputModel.FoundationBeamInput.Sections.Count > 0)
                                ? CurrentInputModel.FoundationBeamInput.Sections.Max(s => s.No) + 1
                                : 1,
                            Name = wizard.Result.Name,
                            Width = wizard.Result.Width,
                            Height = wizard.Result.Height,
                            Area = wizard.Result.Area,
                            ShearAreaY = wizard.Result.ShearAreaY,
                            ShearAreaZ = wizard.Result.ShearAreaZ,
                            TorsionalMoment = wizard.Result.TorsionalMoment,
                            MomentOfInertiaYY = wizard.Result.MomentOfInertiaYY,
                            MomentOfInertiaZZ = wizard.Result.MomentOfInertiaZZ,
                            AFactor = wizard.Result.AFactor,
                            AyFactor = wizard.Result.AyFactor,
                            AzFactor = wizard.Result.AzFactor,
                            IxxFactor = wizard.Result.IxxFactor,
                            IyyFactor = wizard.Result.IyyFactor,
                            IzzFactor = wizard.Result.IzzFactor
                        };
                        CurrentInputModel.FoundationBeamInput.Sections.Add(newSection);
                        addedSectionNo = newSection.No;
                    }

                    SaveUndoState();
                    RequestUpdateWindow();

                    // Applyボタンが押された場合は編集を継続
                    if (wizard.IsApplyClicked)
                    {
                        // 新規作成の場合、作成された断面を選択状態にする
                        if (addedSectionNo.HasValue)
                        {
                            wizard = new Views.BeamSectionWizardWindow(CurrentInputModel.FoundationBeamInput.Sections);
                            var addedSection = CurrentInputModel.FoundationBeamInput.Sections.FirstOrDefault(s => s.No == addedSectionNo.Value);
                            if (addedSection != null)
                            {
                                wizard.SelectSection(addedSection);
                            }
                        }
                        continueEditing = true;
                    }
                    else
                    {
                        continueEditing = false;
                    }
                }
                else
                {
                    // キャンセルされた
                    continueEditing = false;
                }
            }
        }

        // 材料削除コマンド
        [RelayCommand]
        private void DeleteBeamMaterial(object parameter)
        {
            // DataGridの新規行からの呼び出しを無視
            if (parameter is not BeamMaterial material) return;

            if (CurrentInputModel.FoundationBeamInput?.Materials == null) return;

            // 使用中かチェック
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.MaterialNo == material.No);
            if (isUsed)
            {
                System.Windows.MessageBox.Show(
                    $"材料No.{material.No}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            CurrentInputModel.FoundationBeamInput.Materials.Remove(material);
            SaveUndoState();
            RequestUpdateWindow();
        }

        // 断面削除コマンド
        [RelayCommand]
        private void DeleteBeamSection(object parameter)
        {
            // DataGridの新規行からの呼び出しを無視
            if (parameter is not BeamSection section) return;

            if (CurrentInputModel.FoundationBeamInput?.Sections == null) return;

            // 使用中かチェック
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.SectionNo == section.No);
            if (isUsed)
            {
                System.Windows.MessageBox.Show(
                    $"断面No.{section.No}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            CurrentInputModel.FoundationBeamInput.Sections.Remove(section);
            SaveUndoState();
            RequestUpdateWindow();
        }

        // 一般梁要素プロパティ変更コマンド
        [RelayCommand]
        private void EditBeamElements()
        {
            if (!CheckAndResetAnalysisResults()) return;

            // 選択された一般梁要素がない場合はメッセージを表示
            var selectedBeams = CurrentInputModel?.FoundationBeamInput?.Beams?.Where(b => b.IsSelected).ToList();
            if (selectedBeams == null || selectedBeams.Count == 0)
            {
                MessageBox.Show("一般梁要素が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CurrentInputModel.FoundationBeamInput == null)
            {
                return;
            }

            // ViewModelを作成
            var viewModel = new EditBeamElementViewModel(
                selectedBeams.Count,
                CurrentInputModel.FoundationBeamInput.Materials,
                CurrentInputModel.FoundationBeamInput.Sections);

            // ウィンドウを表示
            var window = new Views.EditBeamElementWindow(viewModel);
            if (window.ShowDialog() == true)
            {
                var result = window.Result;

                // 選択された梁要素のプロパティを一括変更
                foreach (var beam in selectedBeams)
                {
                    if (result.IsApplicableMaterialNo && result.MaterialNo.HasValue)
                    {
                        beam.MaterialNo = result.MaterialNo.Value;
                    }

                    if (result.IsApplicableSectionNo && result.SectionNo.HasValue)
                    {
                        beam.SectionNo = result.SectionNo.Value;
                    }
                }

                SaveUndoState();
                RequestUpdateWindow();
            }
        }
    }
}
