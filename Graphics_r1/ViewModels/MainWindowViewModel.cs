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
    /// - 解析実行制御（杭要素分割、解析実行前チェック）
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
        /// description を省略した場合は呼び出し元メソッド名 ([CallerMemberName]) を
        /// 履歴ラベルに使う。可能なら明示的に日本語の説明を渡してください
        /// (例: SaveUndoState("杭追加"))。
        /// </summary>
        public void SaveUndoState([System.Runtime.CompilerServices.CallerMemberName] string? description = null)
        {
            // 直接呼出は「大規模操作」を意味するため、進行中のデバウンスセッションを終了させる
            // (これ以降の編集は新しい Undo ステップに割り当てられる)
            if (_undoBatchActive) FlushPendingUndoSnapshot();

            var copy = CurrentInputModel.DeepCopy();
            if (copy != null)
            {
                _undoManager.SaveState(copy, FormatHistoryDescription(description));
                RaiseUndoStateChanged();
            }
        }

        // ─────────────── Phase A: Undo スナップショットのデバウンス ───────────────
        // 同じセッション内 (連続編集) では SaveUndoState を 1 回だけ実行し、
        // それ以降の編集は同じ Undo ステップにまとめる。
        // 重い DeepCopy を毎セルで実行する代わりに、連続編集の最初の 1 回のみ実行する。
        //
        // 使い方:
        //   - 高頻度な小編集 (DataGrid セル確定等) は SaveUndoStateDebounced を呼ぶ
        //   - 大規模操作 (杭追加/削除、ペースト、ファイルロード等) は従来通り SaveUndoState を呼ぶ
        //     (大規模操作前に FlushPendingUndoSnapshot を呼んで未確定のデバウンスセッションを終了させる)
        private System.Windows.Threading.DispatcherTimer _undoBatchTimer;
        private bool _undoBatchActive;
        // 杭軸力編集など、セル間で 1〜3 秒の navigation が入る用途では 500ms だと毎セル新規 snapshot
        // になってしまうため、2000ms (2 秒) に延長。Undo 粒度は粗くなるが、1 編集セッション = 1 Undo
        // ステップというユーザー期待値とも整合する。
        private const int DefaultUndoBatchDebounceMs = 2000;

        public void SaveUndoStateDebounced(
            [System.Runtime.CompilerServices.CallerMemberName] string? description = null,
            int debounceMs = DefaultUndoBatchDebounceMs)
        {
            // セッション開始時のみ重い DeepCopy を実行 (pre-edit 状態を捕捉)。
            // 2 回目以降の連続編集では SaveUndoState をスキップし、デバウンスタイマーだけ更新。
            if (!_undoBatchActive)
            {
                SaveUndoState(description);
                _undoBatchActive = true;
            }

            // 既存タイマーがあれば破棄、新規タイマーで debounceMs 後にセッション終了
            if (_undoBatchTimer == null)
            {
                _undoBatchTimer = new System.Windows.Threading.DispatcherTimer();
                _undoBatchTimer.Tick += (s, e) =>
                {
                    _undoBatchTimer.Stop();
                    _undoBatchActive = false;
                };
            }
            _undoBatchTimer.Stop();
            _undoBatchTimer.Interval = TimeSpan.FromMilliseconds(debounceMs);
            _undoBatchTimer.Start();
        }

        /// <summary>
        /// 進行中のデバウンスセッションを即時終了する。
        /// 大規模操作 (ファイル保存・解析実行・ダイアログオープン等) の直前に呼ぶことで、
        /// セッション中の全編集が 1 つの Undo ステップにまとまり、続く操作は新しい Undo ステップになる。
        /// </summary>
        public void FlushPendingUndoSnapshot()
        {
            if (_undoBatchTimer != null)
            {
                _undoBatchTimer.Stop();
            }
            _undoBatchActive = false;
        }

        /// <summary>
        /// 進行中のデバウンスセッションを延長する (タイマーをリスタート)。
        /// セッション開始や snapshot 取得は行わない (active でなければ no-op)。
        /// DataGrid の BeginningEdit 等から呼んで、ユーザーが連続編集中であることを通知する。
        /// </summary>
        public void ExtendUndoBatchSession(int debounceMs = DefaultUndoBatchDebounceMs)
        {
            if (!_undoBatchActive) return;
            if (_undoBatchTimer == null) return;
            _undoBatchTimer.Stop();
            _undoBatchTimer.Interval = TimeSpan.FromMilliseconds(debounceMs);
            _undoBatchTimer.Start();
        }

        /// <summary>
        /// 自動取得されたメソッド名 (例: "DeletePiles") を編集履歴に出す表示文字列に変換する。
        /// 既知のメソッド名は日本語に置換、それ以外は空白区切りのキャメルケース展開に留める。
        /// </summary>
        private static string FormatHistoryDescription(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "編集";
            return raw switch
            {
                // 杭関連
                "AddPile" or "AddPileFromCanvas" or "OnAddPile" => "杭 追加",
                "DeletePiles" or "DeletePile" => "杭 削除",
                "EditAddPiles" or "EditPiles" => "杭 プロパティ変更",
                "MoveCopyPiles" => "杭 移動・コピー",
                "SortPileLayoutCore" => "杭配置 並べ替え",

                // 一般節点
                "AddInputNode" or "AddInputNodeFromCanvas" => "一般節点 追加",
                "DeleteInputNode" or "DeleteInputNodes" => "一般節点 削除",
                "DeleteDuplicateInputNodes" => "重複一般節点の整理",
                "SortInputNodesCore" => "一般節点 並べ替え",
                "ConvertNodeType" => "節点種別変換",

                // 通り心
                "AddGridX" => "通り心 (X) 追加",
                "AddGridY" => "通り心 (Y) 追加",
                "DeleteGridX" => "通り心 (X) 削除",
                "DeleteGridY" => "通り心 (Y) 削除",

                // 矩形荷重
                "AddRectLoad" => "矩形荷重 追加",
                "DeleteRectLoad" => "矩形荷重 削除",
                "ResetBeamAwareRectLoads" => "基礎梁考慮矩形荷重 再生成",
                "AdjustRectLoadPlan" => "矩形荷重 平面調整",

                // 沈下層
                "AddSettlementSoilLayer" => "沈下層 追加",
                "DeleteAllSettlementSoilLayers" => "沈下層 一括削除",

                // 基礎梁
                "AutoGenerateFoundationBeams" or "OnAutoGenerateFoundationBeams" => "基礎梁 自動生成",
                "AddBeamElement" or "EditBeamElements" => "梁要素 編集",
                "DeleteFoundationNode" => "基礎節点 削除",
                "DeleteFoundationBeam" => "基礎梁 削除",
                "DeleteDupulicateElements" => "要素重複整理",
                "OnSplitElementsByNodes" => "梁要素 節点分割",
                "ClearAllTorsionalStiffness" => "全ねじり剛性リセット",

                // 根入部
                "AdjustEmbedmentPlan" => "根入部 平面調整",

                // 前後杭・OTM・慣性力
                "AutoIsFrontPiles" => "前後杭 自動判定",
                "AutoOverturningMoment" => "OTM 自動入力",
                "OnMoveForceActionPointToAverageCenter" => "慣性力作用点を平均中心へ移動",

                // ファイル
                "OpenInputModelFileSimple" or "OpenInputModelFile" => "ファイル読込",

                // クリップボード
                "Paste" or "PasteFromClipboard" => "貼り付け",

                // プロパティパネル編集 (Make*Commit 由来)
                "MakeDoubleCommit" or "MakeIntCommit" or "MakeBoolCommit" or "MakeStringCommit" => "プロパティ 編集",

                // DataGrid 編集系 (HandleDataGridCellEditEnding 経由)
                "DataGridPileLayout_OnCellEditEnding" => "杭配置 編集",
                "DataGridPileAxialForce_OnCellEditEnding" => "杭軸力 編集",
                "DataGridIsFrontPile_OnCellEditEnding" => "前後杭 編集",
                "DataGridInputNodes_OnCellEditEnding" => "一般節点 編集",
                "DataGridEmbedment_OnCellEditEnding" => "根入部 編集",
                "DataGridSoilPile_OnCellEditEnding" => "土層・杭 編集",
                "DataGridRectLoads_OnCellEditEnding" => "矩形荷重 編集",

                _ => raw, // メソッド名そのまま (将来辞書追加候補)
            };
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
        /// </summary>
        /// <param name="immediate">即座に実行するか（デフォルト: false、デバウンス付き）</param>
        private void NotifyUIChanged(bool immediate = false)
        {
            if (immediate)
                UpdateWindowImmediate();
            else
                RequestUpdateWindow();
        }

        /// <summary>
        /// DataGridセルエディット完了時の共通処理
        /// バインディング更新とUI更新を一元的に処理します。
        /// </summary>
        /// <param name="e">DataGridセルエディットイベント引数</param>
        /// <param name="customAction">追加のカスタム処理（オプション）</param>
        /// <returns>Commitアクションの場合true、それ以外false</returns>
        private bool HandleDataGridCellEditEnding(DataGridCellEditEndingEventArgs e,
            Action customAction = null,
            bool useDebouncedUndo = false,
            [System.Runtime.CompilerServices.CallerMemberName] string? undoDescription = null)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // 編集確定の直前に Undo スナップショットを保存
                // (binding.UpdateSource() より前 = まだモデルは旧値のため pre-edit が捕捉される)
                // useDebouncedUndo=true の場合、連続編集セッションの最初の 1 回のみ DeepCopy 実行 (Phase A)
                if (useDebouncedUndo)
                    SaveUndoStateDebounced(undoDescription);
                else
                    SaveUndoState(undoDescription);

                // バインディングソースの更新
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                // カスタム処理の実行
                customAction?.Invoke();

                // UI更新
                NotifyUIChanged();

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
        /// <param name="immediate">即座に実行するか（デフォルト: false）</param>
        /// <returns>削除に成功した場合true</returns>
        private bool DeleteCollectionItem<T>(
            object sender,
            ObservableCollection<T> collection,
            Action postDeleteAction = null,
            bool saveUndo = false,
            bool immediate = false)
        {
            if (sender is not T itemToDelete)
                return false;

            if (saveUndo)
                TrySaveUndoSnapshotSafely();

            collection.Remove(itemToDelete);

            postDeleteAction?.Invoke();

            NotifyUIChanged(immediate);

            return true;
        }

        /// <summary>
        /// ダイアログウィンドウを開く共通処理（Undo保存付き）
        /// </summary>
        /// <typeparam name="TViewModel">ViewModelの型</typeparam>
        /// <typeparam name="TWindow">Windowの型</typeparam>
        /// <param name="postDialogAction">ダイアログ終了後のカスタム処理（オプション）</param>
        /// <param name="undoDescription">未使用 (互換のため残置)。ダイアログ系ウィンドウは独自 Undo を持つため main 履歴には記録しない。</param>
        private void OpenDialogWindowWithUndo<TViewModel, TWindow>(Action postDialogAction = null, string? undoDescription = null)
            where TViewModel : ObservableObject
            where TWindow : Window, new()
        {
            // ダイアログを開く
            OpenDialogWindow<TViewModel, TWindow>(this);

            // 追加処理の実行
            postDialogAction?.Invoke();

            // ダイアログ閉じた後 (Save or Cancel) に undo state を push して、
            // メイン画面 Undo でダイアログ前の状態に戻れるようにする。
            // Cancel の場合は CurrentInputModel が変わっていないため、直前の history entry と
            // 重複するが副作用は無い (一回 Undo しても画面は変わらないだけ)。
            // 旧版は DeepCopy が 28s かかったため省略していたが、PileSection の重い computed
            // プロパティに [JsonIgnore] を付けた後は数百 ms 以下に短縮されており支障なし。
            if (!string.IsNullOrEmpty(undoDescription))
            {
                SaveUndoState(undoDescription);
            }
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
        // WriteIndented=false でシリアライズ時間 約 1/3、ファイルサイズ 約 50% 縮小
        // (デシリアライズ側はインデント有無に依存しないため、旧 indented ファイルもそのまま読込可能)
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // 解析結果 (AnaModel の節点/梁結果, VerticalBeamCaseResults) を保存に含めるか。
        // 手動保存・自動保存それぞれで独立に選択できる (オプションタブのチェックボックス)。
        // 既定はいずれも OFF: 入力のみ保存し軽量・高速。ON にすると結果も保存するがファイルが
        // 数十 MB 級に肥大する場合があり読み書きに時間がかかる。

        // 手動保存 (Ctrl+S / 名前を付けて保存) に解析結果を含めるか
        private bool _isSaveAnalysisResultsManual = false;
        public bool IsSaveAnalysisResultsManual
        {
            get => _isSaveAnalysisResultsManual;
            set => SetProperty(ref _isSaveAnalysisResultsManual, value);
        }

        // 自動保存に解析結果を含めるか (定期保存のため既定 OFF 推奨。ON だと毎回数秒・大容量書込)
        private bool _isSaveAnalysisResultsAutoSave = false;
        public bool IsSaveAnalysisResultsAutoSave
        {
            get => _isSaveAnalysisResultsAutoSave;
            set => SetProperty(ref _isSaveAnalysisResultsAutoSave, value);
        }

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

                    // 基礎梁の CollectionChanged 再購読 → 基礎梁考慮沈下解析ボタンの活性化条件再評価
                    // FoundationBeamInput.Beams 自体が新インスタンスで置換された場合 (FoundationBeamViewModel
                    // 経由の編集確定など) にも再購読するため、Input 側の PropertyChanged も併せて監視する
                    if (_currentInputModel?.FoundationBeamInput is { } fbInput)
                    {
                        fbInput.PropertyChanged -= FoundationBeamInput_PropertyChanged;
                        fbInput.PropertyChanged += FoundationBeamInput_PropertyChanged;
                        if (fbInput.Beams is { } beams)
                        {
                            beams.CollectionChanged -= FoundationBeams_CollectionChanged;
                            beams.CollectionChanged += FoundationBeams_CollectionChanged;
                            // 既存梁要素の PropertyChanged を購読 (β / Width / NodeI_Id 等の変更で
                            // 反復解析結果が無効になるため自動破棄するために必要)
                            foreach (var beam in beams)
                            {
                                beam.PropertyChanged -= FoundationBeam_PropertyChanged;
                                beam.PropertyChanged += FoundationBeam_PropertyChanged;
                            }
                        }
                    }

                    // LoadCases/LoadCombinations の CollectionChanged も再購読 + コンボボックス再構築。
                    // LoadCaseWindow.Save / Undo/Redo / ファイルロード で LoadCombinations は新インスタンス
                    // に置換されるため、コンストラクタ時点の subscription だけだとメイン画面の組合せ
                    // ComboBox が古い (1 件しか出ない等) 状態のままになる。
                    if (_currentInputModel?.LoadCasesInput != null)
                    {
                        SubscribeLoadCaseApplicabilityChanged();
                        UpdateLoadCaseOption();
                        UpdateLoadCombinationOption();
                    }

                    // 群杭沈下 (PileGroupSettlement) の PropertyChanged も再購読。
                    // CurrentInputModel 置換でこれも新インスタンスになるため、再アタッチしないと
                    // 沈下コンターキャッシュ無効化・荷重面標高/LoadingType の UI プロキシ更新が止まる。
                    SubscribeSettlementChanged();

                    // 注: UpdateWindowImmediate() はここでは呼ばない。
                    // 全ての代入元（ファイル読込、Undo/Redo等）がフラグリセット後に
                    // 明示的に UpdateWindowImmediate() を呼ぶため、ここで呼ぶと
                    // 不完全な状態での二重描画になる。
                    RaiseAllCommandsCanExecute();

                    OnPropertyChanged(nameof(CurrentInputModel));
                    OnPropertyChanged(nameof(PileCountText));
                    OnPropertyChanged(nameof(AnalysisStatusText));
            OnPropertyChanged(nameof(AnalysisStatusItems));
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
                    // 実ファイルへの保存/読込が確定した時点で例題名は不要 (タイトルバー優先順位的にも下位扱い)
                    if (!string.IsNullOrEmpty(value))
                        _loadedExampleName = null;
                    RaiseAllCommandsCanExecute();
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        // 計算例ロード時にタイトルバーへ表示する例題名 (保存またはファイルロードでクリア)
        private string? _loadedExampleName;
        public string? LoadedExampleName
        {
            get => _loadedExampleName;
            set
            {
                if (SetProperty(ref _loadedExampleName, value))
                    OnPropertyChanged(nameof(WindowTitle));
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
        // 優先順位: 保存ファイル名 > 計算例名 > [新規]
        public string WindowTitle
        {
            get
            {
                const string appName = "杭基礎検討プログラム";
                string ver = $"v{_appVersion}";
                if (!string.IsNullOrEmpty(CurrentFilePath))
                    return $"{appName} {ver} - {System.IO.Path.GetFileName(CurrentFilePath)}";
                if (!string.IsNullOrEmpty(_loadedExampleName))
                    return $"{appName} {ver} - [{_loadedExampleName}]";
                return $"{appName} {ver} - [新規]";
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
                // 反復解析 (土層沈下「反復」ルート) の CaseRecord 確認
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

            HandleDataGridCellEditEnding(e, () =>
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
            SaveUndoState();

            // バインディングソースの更新 (= コミット)
            var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();

            // 変更後の UI 更新
            RequestUpdateWindow();
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
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (!ConfirmAnalysisConditionChange("両方", "矩形荷重 (追加)")) return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad());

            // 個別十字系で手動追加された場合は「任意矩形」に切り替え
            SwitchToAnyRectIfCrossType();

            IsGroupPileSettlementAnalysisDone = false;
            RequestUpdateWindow();
        }

        /// <summary>
        /// 反復解析タブの矩形荷重リセット: 個別矩形 (杭ごとに 1 矩形) を初期生成する。
        /// 各矩形の DX/DY は荷重面等価径から (√π·r) で算出 (取得不可なら 2.0m)、
        /// QA は VL 軸力 (= AxialForceVL0 + AxialForceVLAdditional)、LinkedPileNo は pile.PileNo。
        /// 既存の編集内容は破棄するため確認ダイアログを表示する。
        /// </summary>
        [RelayCommand]
        private void ResetBeamAwareRectLoads()
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            var piles = CurrentInputModel?.PileLayoutItems;
            if (pgs == null || piles == null || piles.Count == 0) return;

            int existingCount = pgs.RectLoads?.Count ?? 0;
            if (existingCount > 0)
            {
                var res = MessageService.Show(
                    $"現在の矩形荷重 ({existingCount} 件) を破棄し、各杭に対して個別矩形を再生成します。\n続行しますか?",
                    "矩形荷重リセット確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (res != MessageBoxResult.OK) return;
            }

            TrySaveUndoSnapshotSafely();

            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            var newList = new System.Collections.ObjectModel.ObservableCollection<RectLoad>();
            foreach (var pile in piles)
            {
                double radius = 0;
                if (soilPiles != null && pile.SoilPileAltNo - 1 >= 0 && pile.SoilPileAltNo - 1 < soilPiles.Count)
                    radius = soilPiles[pile.SoilPileAltNo - 1].GroupPileLoadDia * 0.5;
                double side = radius > 0 ? Math.Sqrt(Math.PI) * radius : 2.0;
                double half = side * 0.5;
                double qa = pile.AxialForceVL0 + pile.AxialForceVLAdditional;
                newList.Add(new RectLoad
                {
                    X1 = pile.Point3D.X - half,
                    X2 = pile.Point3D.X + half,
                    Y1 = pile.Point3D.Y - half,
                    Y2 = pile.Point3D.Y + half,
                    QA = qa,
                    LinkedPileNo = pile.PileNo,
                });
            }
            pgs.RectLoads = newList;

            // 反復解析タブ用に LoadingType を「個別矩形（基礎梁考慮）」へ確定
            pgs.LoadingType = "個別矩形（基礎梁考慮）";

            IsGroupPileSettlementAnalysisDone = false;
            RequestUpdateWindow();
            ShowToast($"矩形荷重をリセットしました ({newList.Count} 件)。");
        }

        // 自動生成による RectLoads 置換中のフラグ（ユーザ編集と区別するため）
        private bool _suppressRectLoadAutoSwitch;

        /// <summary>
        /// 「個別十字」「個別十字（基礎梁反力）」「個別矩形」「個別矩形（基礎梁考慮）」が選択されている場合、
        /// RectLoads を自動生成値で置き換える。
        /// 「個別矩形」系では既存矩形の DX/DY (寸法) は維持し、中心座標と荷重 QA のみ最新値で更新する。
        /// </summary>
        public void RebuildAutoCrossRectLoadsIfNeeded()
        {
            if (CurrentInputModel?.PileGroupSettlement == null) return;
            var lt = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (lt != "個別十字" && lt != "個別十字（基礎梁反力）"
             && lt != "個別矩形" && lt != "個別矩形（基礎梁考慮）") return;

            // 個別十字（基礎梁反力）のみ VB 解析の杭反力を使用するため必須。
            // 個別矩形（基礎梁考慮）は VB 解析を要求しない (将来の反復実装で内部解析する)。
            if (lt == "個別十字（基礎梁反力）"
                && (!IsVerticalBeamAnalysisDone || VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0))
            {
                return;
            }

            var piles = CurrentInputModel.PileLayoutItems;
            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            if (piles == null || piles.Count == 0 || soilPiles == null || soilPiles.Count == 0) return;

            var generated = SettlementAnalysisService.BuildAutoCrossRectLoads(
                CurrentInputModel.PileGroupSettlement, piles, soilPiles, VerticalBeamCaseResults);

            _suppressRectLoadAutoSwitch = true;
            try
            {
                CurrentInputModel.PileGroupSettlement.RectLoads = new ObservableCollection<RectLoad>(generated);
                IsRectLoadFreshFromAutoGen = true; // 自動生成直後はクリーン状態
            }
            finally
            {
                _suppressRectLoadAutoSwitch = false;
            }
        }

        /// <summary>
        /// 現在の荷重タイプが個別十字系の場合、「任意矩形」に切り替える。
        /// ユーザが荷重データグリッドを手動で編集 (位置 X1/X2/Y1/Y2 や荷重 QA) したときに呼ぶ。
        /// 個別矩形は DX/DY のみ編集 OK (位置・QA は自動再生成で復元) なので、ここでは切替えない。
        /// </summary>
        public void SwitchToAnyRectIfCrossType()
        {
            if (_suppressRectLoadAutoSwitch) return;
            if (CurrentInputModel?.PileGroupSettlement == null) return;
            var lt = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (lt == "個別十字" || lt == "個別十字（基礎梁反力）")
            {
                CurrentInputModel.PileGroupSettlement.LoadingType = "任意矩形";
            }
        }

        // 群杭沈下検討用検討用土層追加メソッド
        [RelayCommand]
        private void AddSettlementSoilLayer()
        {
            if (!ConfirmAnalysisConditionChange("両方", "土層 (追加)")) return;

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
        }

        // 群杭沈下検討用検討用土層削除メソッド
        [RelayCommand]
        private void DeleteSettlementSoilLayer(object sender)
        {
            if (!ConfirmAnalysisConditionChange("両方", "土層 (削除)")) return;

            DeleteCollectionItem(
                sender,
                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers,
                () => UpdateSettlementSoilLayer());
        }

        // 群杭沈下検討用検討用土層データグリッド更新メソッド
        private void UpdateSettlementSoilLayer()
        {
            // 厚さは「土層上端 (SoilLayersTopAltitude)」基準で算出
            double topAltitude = CurrentInputModel.PileGroupSettlement.SoilLayersTopAltitude;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                    settlementSoilLayers[i].Thickness = topAltitude - settlementSoilLayers[i].BottomAltitude;
                else
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude;
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

            // 通り上の基礎梁節点を選択
            SelectFoundationNodesOnGrid(c => c.X, coord, tolerance);

            // 両端が通り上にある基礎梁を選択
            SelectFoundationBeamsOnGrid(c => c.X, coord, tolerance);

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

            // 通り上の基礎梁節点を選択
            SelectFoundationNodesOnGrid(c => c.Y, coord, tolerance);

            // 両端が通り上にある基礎梁を選択
            SelectFoundationBeamsOnGrid(c => c.Y, coord, tolerance);

            RequestUpdateWindow();
        }

        /// <summary>
        /// 通り上の基礎梁節点（FoundationNode）を選択
        /// </summary>
        private void SelectFoundationNodesOnGrid(Func<(double X, double Y, double Z), double> getCoord, double coord, double tolerance)
        {
            var fbNodes = CurrentInputModel.FoundationBeamInput?.Nodes;
            if (fbNodes == null) return;

            foreach (var fnode in fbNodes)
            {
                if (Math.Abs(getCoord((fnode.X, fnode.Y, 0)) - coord) <= tolerance)
                    fnode.IsSelected = true;
            }
        }

        /// <summary>
        /// 両端が通り上にある基礎梁を選択
        /// </summary>
        private void SelectFoundationBeamsOnGrid(Func<(double X, double Y, double Z), double> getCoord, double coord, double tolerance)
        {
            var fbBeams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (fbBeams == null) return;

            foreach (var beam in fbBeams)
            {
                var coordsI = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var coordsJ = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (!coordsI.HasValue || !coordsJ.HasValue) continue;

                bool iOnGrid = Math.Abs(getCoord(coordsI.Value) - coord) <= tolerance;
                bool jOnGrid = Math.Abs(getCoord(coordsJ.Value) - coord) <= tolerance;

                if (iOnGrid && jOnGrid)
                    beam.IsSelected = true;
            }
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
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (!ConfirmAnalysisConditionChange("両方", "矩形荷重 (削除)")) return;

            DeleteCollectionItem(sender, CurrentInputModel.PileGroupSettlement.RectLoads, immediate: true);
            // ユーザ手動削除時は個別十字系から「任意矩形」に切替
            SwitchToAnyRectIfCrossType();
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
            if (!CheckAndResetAnalysisResults()) return;

            // スナップショットを保存
            TrySaveUndoSnapshotSafely();

            Point3D nextPoint3D = new();
            if (CurrentInputModel.PileLayoutItems.Count != 0)
            {
                // 直前の杭から X 方向に 7.2m オフセット
                nextPoint3D = CurrentInputModel.PileLayoutItems.Last().Point3D + new Vector3D() { X = 7.2 };
            }

            // UIスレッドから呼ばれるため直接実行
            CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem() { X = nextPoint3D.X, Y = nextPoint3D.Y, Z = nextPoint3D.Z });
            CurrentInputModel.PileLayoutItems[^1].SetMainWindowViewModel(this);
            // 要素未分割の場合は自動で SoiPile を再生成
            if (!IsElementSplit)
                RequestGenerateSoilPiles();

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
            UpdatePileLayoutNo();
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

        /// <summary>X優先整列: X昇順 → Y昇順でPileLayoutItemsをソート</summary>
        [RelayCommand]
        private void SortPileLayoutXFirst()
        {
            SortPileLayoutCore(piles => piles.OrderBy(p => p.X).ThenBy(p => p.Y));
        }

        /// <summary>Y優先整列: Y昇順 → X昇順でPileLayoutItemsをソート</summary>
        [RelayCommand]
        private void SortPileLayoutYFirst()
        {
            SortPileLayoutCore(piles => piles.OrderBy(p => p.Y).ThenBy(p => p.X));
        }

        /// <summary>杭配置ソート共通処理: Move方式で最小限のイベント発火</summary>
        private void SortPileLayoutCore(Func<IEnumerable<PileLayoutDataItem>, IOrderedEnumerable<PileLayoutDataItem>> orderFunc)
        {
            var col = CurrentInputModel.PileLayoutItems;
            if (col.Count == 0) return;
            if (!CheckAndResetAnalysisResults()) return;
            TrySaveUndoSnapshotSafely();

            // 旧No→新Noマッピングを構築
            var sorted = orderFunc(col).ToList();
            var oldToNewNo = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++)
                oldToNewNo[sorted[i].No] = i + 1;

            // Move方式: Clear+Addの大量イベント発火を回避
            for (int i = 0; i < sorted.Count; i++)
            {
                int currentIndex = col.IndexOf(sorted[i]);
                if (currentIndex != i)
                    col.Move(currentIndex, i);
            }

            UpdatePileLayoutNo();

            // 一般節点のLinkedPileNoを追従更新
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.LinkedPileNo.HasValue && oldToNewNo.TryGetValue(node.LinkedPileNo.Value, out int newPileNo))
                        node.LinkedPileNo = newPileNo;
                }
            }

            RequestUpdateWindow();
        }

        /// <summary>一般節点: X優先整列</summary>
        [RelayCommand]
        private void SortInputNodesXFirst()
        {
            SortInputNodesCore(nodes => nodes.OrderBy(n => n.X).ThenBy(n => n.Y));
        }

        /// <summary>一般節点: Y優先整列</summary>
        [RelayCommand]
        private void SortInputNodesYFirst()
        {
            SortInputNodesCore(nodes => nodes.OrderBy(n => n.Y).ThenBy(n => n.X));
        }

        /// <summary>一般節点ソート共通処理: Move方式で最小限のイベント発火</summary>
        private void SortInputNodesCore(Func<IEnumerable<InputNode>, IOrderedEnumerable<InputNode>> orderFunc)
        {
            var col = CurrentInputModel.InputNodes;
            if (col == null || col.Count == 0) return;
            if (!CheckAndResetAnalysisResults()) return;
            TrySaveUndoSnapshotSafely();

            var sorted = orderFunc(col).ToList();

            // Move方式: Clear+Addの大量イベント発火を回避
            for (int i = 0; i < sorted.Count; i++)
            {
                int currentIndex = col.IndexOf(sorted[i]);
                if (currentIndex != i)
                    col.Move(currentIndex, i);
            }

            // No振り直し
            for (int i = 0; i < col.Count; i++)
                col[i].No = i + 1;

            RequestUpdateWindow();
        }

        /// <summary>梁要素: 要素番号昇順で整列（表示順のみ変更、解析結果に影響なし）</summary>
        [RelayCommand]
        private void SortBeamsByNo()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;
            TrySaveUndoSnapshotSafelyOptimized();

            // 旧 No プロパティ廃止につき、現状並びを維持 (No-op)。
            // 将来この整列コマンドが必要な場合は別の基準 (Node 順等) に基づいて実装する。
            RequestUpdateWindow();
        }

        /// <summary>
        /// 梁要素: 選択要素（無選択なら全要素）の I/J 節点参照を入れ替える。
        /// 併せて AngleBeta を (180° − β) に反転し、局所 y 軸の世界空間向きを保つ。
        /// </summary>
        [RelayCommand]
        private void SwapBeamIJ()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;

            var targets = beams.Where(b => b.IsSelected).ToList();
            if (targets.Count == 0) targets = beams.ToList();

            TrySaveUndoSnapshotSafelyOptimized();

            foreach (var b in targets)
            {
                (b.NodeI_Type, b.NodeJ_Type) = (b.NodeJ_Type, b.NodeI_Type);
                (b.NodeI_Id, b.NodeJ_Id) = (b.NodeJ_Id, b.NodeI_Id);

                // ローカル x 軸反転で y 軸が 180° 回る分を β で相殺し、物理的に同じ断面向きを維持する
                b.AngleBeta = ((180.0 - b.AngleBeta) % 360.0 + 360.0) % 360.0;
            }

            RequestUpdateWindow();
        }

        /// <summary>梁要素: I端節点→J端節点昇順で整列（表示順のみ変更、解析結果に影響なし）</summary>
        [RelayCommand]
        private void SortBeamsByNode()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;
            TrySaveUndoSnapshotSafelyOptimized();

            var sorted = beams
                .OrderBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeI_Type, b.NodeI_Id))
                .ThenBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeJ_Type, b.NodeJ_Id))
                .ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int cur = beams.IndexOf(sorted[i]);
                if (cur != i) beams.Move(cur, i);
            }
            // 旧 No プロパティは廃止: 番号 = 位置インデックスとして自動的に追従

            RequestUpdateWindow();
        }

        // 要素の節点位置での分割
        // 旧実装は FoundationNode (基礎梁節点) のみ参照していたが、ToolTip 「重なる一般節点で分割」の通り
        // PileLayout (杭頭)・InputNode (一般)・FoundationNode の全種類を対象にする。
        // 端点参照は NodeReferenceType + Guid の現代式で生成する。
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            var beams = fb.Beams;
            double tolerance = EditDistanceThreshold;

            // 候補ノード一覧 (Type + Guid + 位置) を共通ヘルパで列挙 (PileLayout / GeneralNode / FoundationNode 全種)
            var candidates = EnumerateAllCandidateNodes(includeFoundationNodes: true).ToList();

            var newBeams = new List<FoundationBeam>();
            var toRemove = new List<FoundationBeam>();
            const double endEps = 1e-6;

            foreach (var beam in beams.Where(b => b.IsSelected).ToList())
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                var pI = posI.Value;
                var pJ = posJ.Value;
                Vector3D line = pJ - pI;
                double lineLengthSq = line.LengthSquared;
                if (lineLengthSq < 1e-18) continue;

                // 線上にある中間ノードを探す (端点除外、線分上の t∈(0, 1)、距離 ≤ tolerance)
                var splits = new List<(NodeReferenceType Type, Guid Id, double T)>();
                foreach (var cand in candidates)
                {
                    // 自分の端点はスキップ
                    if (cand.Type == beam.NodeI_Type && cand.Id == beam.NodeI_Id) continue;
                    if (cand.Type == beam.NodeJ_Type && cand.Id == beam.NodeJ_Id) continue;

                    Vector3D v = cand.Pos - pI;
                    double t = Vector3D.DotProduct(v, line) / lineLengthSq;
                    if (t <= endEps || t >= 1.0 - endEps) continue;

                    Point3D projection = pI + t * line;
                    double dist = (cand.Pos - projection).Length;
                    if (dist > tolerance) continue;

                    splits.Add((cand.Type, cand.Id, t));
                }

                if (splits.Count == 0) continue;

                // t の昇順でソート
                splits.Sort((a, b) => a.T.CompareTo(b.T));

                // 同一 t に近い候補は重複扱い (杭頭+ΔZc と一般節点が同位置にある場合等)
                var dedupedSplits = new List<(NodeReferenceType Type, Guid Id, double T)>();
                foreach (var s in splits)
                {
                    if (dedupedSplits.Count > 0 && Math.Abs(dedupedSplits[^1].T - s.T) < endEps)
                        continue;
                    dedupedSplits.Add(s);
                }

                // 分割セグメントを生成
                var endpoints = new List<(NodeReferenceType Type, Guid Id)>
                {
                    (beam.NodeI_Type, beam.NodeI_Id)
                };
                foreach (var s in dedupedSplits)
                    endpoints.Add((s.Type, s.Id));
                endpoints.Add((beam.NodeJ_Type, beam.NodeJ_Id));

                for (int i = 0; i < endpoints.Count - 1; i++)
                {
                    newBeams.Add(new FoundationBeam
                    {
                        NodeI_Type = endpoints[i].Type,
                        NodeI_Id = endpoints[i].Id,
                        NodeJ_Type = endpoints[i + 1].Type,
                        NodeJ_Id = endpoints[i + 1].Id,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        SectionName = beam.SectionName,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        AngleBeta = beam.AngleBeta,
                        IsVisible = beam.IsVisible,
                    });
                }
                toRemove.Add(beam);
            }

            foreach (var beam in toRemove)
                beams.Remove(beam);
            foreach (var beam in newBeams)
                beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            if (toRemove.Count == 0)
            {
                ShowToast("選択要素上に分割できる中間節点が見つかりませんでした。", 2);
            }
            else
            {
                PileDesign.Services.MessageService.Show(
                    $"{toRemove.Count} 個の要素を {newBeams.Count} 個に分割しました。",
                    "節点分割完了",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
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
                MessageService.Show("分割する梁要素を選択してください。");
                return;
            }

            SaveUndoState();

            int n = EqualDivisionCount;
            var toRemove = new List<FoundationBeam>();
            var toAdd = new List<FoundationBeam>();

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
                toAdd.Add(new FoundationBeam
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
                    toAdd.Add(new FoundationBeam
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
                toAdd.Add(new FoundationBeam
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

            MessageService.Show(
                $"{toRemove.Count} 個の要素を {n} 等分しました（{toAdd.Count} 個の要素、{toRemove.Count * (n - 1)} 個の節点を生成）。",
                "等分割完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 梁要素を節点で分割するメソッド (両端が FoundationNode のときのみ動作)
        private List<FoundationBeam> SplitBeamByNodes(FoundationBeam beam, ObservableCollection<FoundationNode> allNodes)
        {
            var result = new List<FoundationBeam>();

            // 両端が FoundationNode でない場合は分割しない (PileLayout / GeneralNode 経由は対象外)
            if (beam.NodeI_Type != NodeReferenceType.FoundationNode ||
                beam.NodeJ_Type != NodeReferenceType.FoundationNode)
                return [beam];

            // 始点・終点の節点を取得
            var nodeI = allNodes.FirstOrDefault(n => n.Id == beam.NodeI_Id);
            var nodeJ = allNodes.FirstOrDefault(n => n.Id == beam.NodeJ_Id);

            if (nodeI == null || nodeJ == null) return [beam]; // 節点が見つからない場合は分割しない

            Point3D pointI = new(nodeI.X, nodeI.Y, nodeI.Z);
            Point3D pointJ = new(nodeJ.X, nodeJ.Y, nodeJ.Z);

            // 線上にある中間節点を探す
            var intermediateNodes = new List<(FoundationNode node, double distance)>();

            foreach (var node in allNodes)
            {
                if (node.Id == beam.NodeI_Id || node.Id == beam.NodeJ_Id) continue; // 始点・終点は除外

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
                result.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.FoundationNode,
                    NodeI_Id = allSplitNodes[i].Id,
                    NodeJ_Type = NodeReferenceType.FoundationNode,
                    NodeJ_Id = allSplitNodes[i + 1].Id,
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
        private List<FoundationBeam> SplitBeamAtPoints(
            FoundationBeam beam,
            List<(InputNode node, double t)> splitPoints)
        {
            if (splitPoints.Count == 0) return [beam];

            // tの昇順にソート
            var sorted = splitPoints.OrderBy(sp => sp.t).ToList();

            var result = new List<FoundationBeam>();

            // 最初のセグメント: 元のNodeI → 最初の分割節点
            result.Add(new FoundationBeam
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
                result.Add(new FoundationBeam
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
            result.Add(new FoundationBeam
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

        // 交差点で杭要素分割
        [RelayCommand]
        private void SplitElementsAtIntersections()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count < 2)
            {
                MessageService.Show("交差判定するには梁要素を2本以上選択してください。",
                    "交差点分割", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveUndoState();

            double tolerance = EditDistanceThreshold;

            // 各要素の座標を事前取得
            var beamCoords = new Dictionary<FoundationBeam, (Point3D pi, Point3D pj)>();
            foreach (var beam in selectedBeams)
            {
                var ci = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var cj = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (ci == null || cj == null) continue;
                beamCoords[beam] = (new Point3D(ci.Value.X, ci.Value.Y, ci.Value.Z),
                                    new Point3D(cj.Value.X, cj.Value.Y, cj.Value.Z));
            }

            // 各要素ごとの分割点リスト
            var beamSplitPoints = new Dictionary<FoundationBeam, List<(InputNode node, double t)>>();

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
            var toRemove = new List<FoundationBeam>();
            var toAdd = new List<FoundationBeam>();

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

        // 基礎梁節点削除 (接続された梁要素もカスケード削除)
        [RelayCommand]
        private void DeleteFoundationNode(FoundationNode node)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            // 接続されている梁要素を抽出 (NodeI/J_Type=FoundationNode かつ Id が一致するもの)
            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            var connectedBeams = beams.Where(b =>
                (b.NodeI_Type == NodeReferenceType.FoundationNode && b.NodeI_Id == node.Id) ||
                (b.NodeJ_Type == NodeReferenceType.FoundationNode && b.NodeJ_Id == node.Id)
            ).ToList();

            if (connectedBeams.Count > 0)
            {
                var beamNos = connectedBeams.Select(b => beams.IndexOf(b) + 1).OrderBy(n => n).ToList();
                string list = string.Join(", ", beamNos.Take(20).Select(n => $"#{n}"));
                if (beamNos.Count > 20) list += $" ほか {beamNos.Count - 20} 件";
                var result = PileDesign.Services.MessageService.Show(
                    $"節点 {node.No} を削除します。\n" +
                    $"同時に接続された一般梁要素 {beamNos.Count} 本 ({list}) も削除されます。\n" +
                    $"よろしいですか?",
                    "削除確認",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            TrySaveUndoSnapshotSafely();
            foreach (var beam in connectedBeams)
                beams.Remove(beam);
            CurrentInputModel.FoundationBeamInput.Nodes.Remove(node);
            RenumberFoundationNodes();
            RequestUpdateWindow();
        }

        // 基礎梁削除
        [RelayCommand]
        private void DeleteFoundationBeam(FoundationBeam beam)
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
            var toRemove = new List<FoundationBeam>();
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

            PileDesign.Services.MessageService.Show(
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

            string message = "梁要素を自動生成しますか？\n\n選択中の杭配置について、X成分・Y成分がそれぞれ同一の隣り合う杭配置の接合節点を基礎梁で連結します。";
            if (HasAnyAnalysisResult)
                message += "\n\n※ 既存の解析結果は消去されます。";

            var result = PileDesign.Services.MessageService.Show(
                message,
                "自動梁要素生成",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            if (!CheckAndResetAnalysisResults()) return;

            var piles = CurrentInputModel.PileLayoutItems;
            if (piles.Count < 2) return;

            TrySaveUndoSnapshotSafely();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            const double tolerance = 1e-3; // 座標一致の許容誤差 (m)

            // 既存ビームのペアセット（重複チェック用）
            var existingPairs = new HashSet<(Guid, Guid)>();
            foreach (var b in beams)
            {
                if (b.NodeI_Type == NodeReferenceType.PileLayout && b.NodeJ_Type == NodeReferenceType.PileLayout)
                {
                    existingPairs.Add((b.NodeI_Id, b.NodeJ_Id));
                    existingPairs.Add((b.NodeJ_Id, b.NodeI_Id));
                }
            }

            // 新規要素を一時リストに蓄積（ObservableCollection への逐次Add を回避）
            var newBeams = new List<FoundationBeam>();

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

                    newBeams.Add(new FoundationBeam
                    {
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

                    newBeams.Add(new FoundationBeam
                    {
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
                }
            }

            int addedCount = newBeams.Count;

            // 既存 + 新規を結合して一括セット（CollectionChanged を1回だけ発火）
            var allBeams = new ObservableCollection<FoundationBeam>(beams.Concat(newBeams));
            CurrentInputModel.FoundationBeamInput.Beams = allBeams;

            // 自動生成梁は MaterialNo=1 / SectionNo=1 を参照するため、参照先のデフォルトを保証
            if (addedCount > 0)
            {
                CurrentInputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();
            }

            RenumberFoundationBeams();
            // 個別矩形（基礎梁考慮）の表示可否を即座に再評価 (Beams コレクション置換後の保険)
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            RequestUpdateWindow();

            MessageService.Show(
                $"{addedCount} 本の基礎梁を自動生成しました。",
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

        // 基礎梁番号振り直し: No プロパティ廃止により実体は何もしない (位置 = ID)。
        // 既存呼び出しサイトの互換維持のためメソッドは残置 (将来呼び出し側を整理して削除可)。
        private void RenumberFoundationBeams()
        {
            // No-op: 番号は Beams コレクションの位置から自動算出されるため不要
        }

        // 杭配置番号の更新
        public void UpdatePileLayoutNo()
        {
            for (int i = 0; i < CurrentInputModel.PileLayoutItems.Count; i++)
            {
                CurrentInputModel.PileLayoutItems[i].No = i + 1;
                CurrentInputModel.PileLayoutItems[i].PileNo = i + 1;
            }
        }

        // 荷重面の自動生成
        [RelayCommand]
        private void OnAdjustRectLoadPlan()
        {
            // 荷重面等価径 (GroupPileLoadDia) が 0 の地盤・杭・レベルセットがある場合は警告。
            // 0 のものは群杭沈下解析でスキップされるため、ユーザーに気付かせる。
            // ただし任意矩形モードでは GroupPileLoadDia は使われないため警告不要。
            var loadingType = CurrentInputModel?.PileGroupSettlement?.LoadingType;
            bool needsGroupPileLoadDia = loadingType == "個別十字" || loadingType == "個別十字（基礎梁反力）"
                                       || loadingType == "個別矩形" || loadingType == "個別矩形（基礎梁考慮）";
            var soilPiles = CurrentInputModel?.ElementDivision?.SoilPiles;
            if (needsGroupPileLoadDia && soilPiles != null && soilPiles.Count > 0)
            {
                var zeroDiaPiles = soilPiles.Where(sp => sp.GroupPileLoadDia <= 0.0).ToList();
                if (zeroDiaPiles.Count > 0)
                {
                    var sampleLines = zeroDiaPiles
                        .Take(10)
                        .Select(sp => $"  ・地盤{sp.GroundNo}・杭体{sp.PileBodyNo} (No.{sp.No})");
                    var moreNote = zeroDiaPiles.Count > 10
                        ? $"\n  …他 {zeroDiaPiles.Count - 10} 件"
                        : "";
                    var msg = $"荷重面等価径 (GroupPileLoadDia) が 0 (未入力) の地盤・杭・レベルセットが {zeroDiaPiles.Count} 件あります:\n" +
                              string.Join("\n", sampleLines) + moreNote +
                              "\n\n対象の杭は群杭沈下解析でスキップされます。\n続行しますか?";
                    var result = MessageService.Show(msg, "荷重面等価径未入力の確認",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }
            }

            // Undoポイントを追加
            TrySaveUndoSnapshotSafelyOptimized();

            // BoundingBoxCalculator を使用して境界を計算
            var boundingBox = BoundingBoxCalculator.Calculate(
                CurrentInputModel.PileLayoutItems,
                RectLoadPileDistance
            );

            // 全杭のVL軸力合計を荷重として設定
            double totalVL = 0;
            foreach (var pile in CurrentInputModel.PileLayoutItems)
                totalVL += pile.AxialForceVL;

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad()
            {
                X1 = boundingBox.MinX,
                X2 = boundingBox.MaxX,
                Y1 = boundingBox.MinY,
                Y2 = boundingBox.MaxY,
                QA = totalVL
            }
            );

            // 個別十字系で手動自動生成された場合は「任意矩形」に切り替え
            SwitchToAnyRectIfCrossType();

            IsGroupPileSettlementAnalysisDone = false;

            UpdateWindowImmediate();
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

            foreach (var embedmentDataItem in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                embedmentDataItem.X1 = boundingBox.MinX;
                embedmentDataItem.X2 = boundingBox.MaxX;
                embedmentDataItem.Y1 = boundingBox.MinY;
                embedmentDataItem.Y2 = boundingBox.MaxY;
            }

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 慣性力作用点をすべての接合節点の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {
            if (!CheckAndResetAnalysisResults()) return;

            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show("杭配置データがありません。");
                return;
            }

            TrySaveUndoSnapshotSafely();

            // 接合節点（接合節点 = pile.Z）の図心を計算 (v2 セマンティクス)
            var piles = CurrentInputModel.PileLayoutItems;
            double centerX = piles.Average(p => p.X);
            double centerY = piles.Average(p => p.Y);
            double centerZ = piles.Average(p => p.Z);

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
        }


        // 群杭沈下解析の実行メソッド
        [RelayCommand]
        private void PileGroupSettlementAnalysis()
        {
            // 荷重タイプ別の事前チェック
            var loadingType = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (loadingType == "任意矩形")
            {
                // 群杭荷重（矩形荷重）が定義されているかチェック
                var rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
                if (rectLoads == null || rectLoads.Count == 0)
                {
                    MessageService.Show("群杭荷重（矩形荷重）が定義されていません。\n荷重タブで矩形荷重を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 荷重値が全て0かチェック
                if (rectLoads.All(r => r.QA == 0))
                {
                    MessageService.Show("値が0の群杭荷重（矩形荷重）しか定義されていません。\n荷重タブで荷重値を設定してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別十字" || loadingType == "個別矩形")
            {
                // 個別十字・個別矩形は杭位置と軸力から矩形荷重を自動生成するため、杭が必要
                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (piles.All(p => (p.AxialForceVL0 + p.AxialForceVLAdditional) == 0))
                {
                    MessageService.Show("全ての杭の軸力（VL0+VLadd）が0です。\n杭タブで軸力を設定してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別十字（基礎梁反力）")
            {
                if (!IsVerticalBeamAnalysisDone || VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0)
                {
                    MessageService.Show("基礎梁考慮鉛直解析が実行されていません。\n先に基礎梁考慮鉛直解析を実行してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別矩形（基礎梁考慮）")
            {
                // 個別矩形（基礎梁考慮）は基礎梁が必須 (将来の反復ばね解析用)
                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var beams = CurrentInputModel.FoundationBeamInput?.Beams;
                if (beams == null || beams.Count == 0)
                {
                    MessageService.Show("基礎梁が定義されていません。\n基礎梁を入力してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
                if (rectLoads == null || rectLoads.Count == 0 || rectLoads.All(r => r.QA == 0))
                {
                    MessageService.Show("矩形荷重が定義されていません (または全て 0)。\n荷重面等価径を入力すると自動生成されます。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                // "なし" またはその他
                MessageService.Show("荷重タイプが設定されていません。\n荷重タブで荷重タイプを選択してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 荷重面位置と土層プロファイルの整合性チェック
            var pgs = CurrentInputModel.PileGroupSettlement;
            if (pgs.SettlementSoilLayers == null || pgs.SettlementSoilLayers.Count == 0)
            {
                MessageService.Show("群杭沈下解析用の土層が1層以上必要です。\n土層タブで土層を追加してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // 一回解析 (基礎梁無し) の荷重面標高を採用 (per-route フィールドから現在値にコピー)
            // ※ 個別矩形（基礎梁考慮）は別ルート (反復解析) で OpenGroupSettlementWithBeamWindow が起動時にコピー
            string loadingTypeNow = pgs.LoadingType ?? "";
            if (loadingTypeNow != "個別矩形（基礎梁考慮）" && !double.IsNaN(pgs.LoadingPlaneAltitudeNonBeam))
                pgs.LoadingPlaneAltitude = pgs.LoadingPlaneAltitudeNonBeam;

            // 一般解析実行時に pgs.RectLoads が反復で書き換えられた状態 (= 現在 反復モード) なら、
            // ユーザー入力スナップショットから 一般入力を復元してから Steinbrenner を回す。
            // (反復後に直接「一般解析実行」を押した場合に、収束反力で一般を再計算してしまう問題への対策)
            if (loadingType != "個別矩形（基礎梁考慮）"
                && pgs.ActiveLoadingType == "個別矩形（基礎梁考慮）"
                && pgs.NonBeamRectLoadsSnapshot != null
                && pgs.NonBeamRectLoadsSnapshot.Count > 0)
            {
                pgs.RectLoads = new System.Collections.ObjectModel.ObservableCollection<Models.InputData.RectLoad>(
                    pgs.NonBeamRectLoadsSnapshot.Select(r => new Models.InputData.RectLoad
                    {
                        X1 = r.X1, X2 = r.X2, Y1 = r.Y1, Y2 = r.Y2,
                        QA = r.QA, LinkedPileNo = r.LinkedPileNo,
                    }));
            }

            double topAlt = pgs.SoilLayersTopAltitude;
            double loadAlt = pgs.LoadingPlaneAltitude;
            double bottomAlt = pgs.SettlementSoilLayers[^1].BottomAltitude;
            if (loadAlt > topAlt + NumericalConstants.NEAR_ZERO_EPSILON)
            {
                MessageService.Show($"荷重面 Z ({loadAlt:N3} m) が土層上端 Z ({topAlt:N3} m) より高くなっています。\n荷重面を土層上端以下に設定してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (loadAlt < bottomAlt - NumericalConstants.NEAR_ZERO_EPSILON)
            {
                MessageService.Show($"荷重面 Z ({loadAlt:N3} m) が最下層下端 Z ({bottomAlt:N3} m) より低くなっています。\n荷重面を最下層下端以上に設定してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 個別矩形（基礎梁考慮）は反復解析ウィンドウで実行 → 確定後に Steinbrenner グリッドコンタを更新
            if (loadingType == "個別矩形（基礎梁考慮）")
            {
                OpenGroupSettlementWithBeamWindow();
                // ウィンドウが OK で閉じられた場合は確定された RectLoads / 杭沈下が反映済みなので
                // 後続のグリッドコンター生成へ進む。Cancel された場合は IsSaved=false → 何もせず終了。
                // 簡略化のため確定/破棄に関わらず後続フローを継続 (Cancel 時は元の RectLoads が残る)。
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
                GroupPileSettlementYSpacing,
                VerticalBeamCaseResults);

            if (!result.Success)
            {
                MessageService.Show(result.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CurrentInputModel.PileGroupSettlement.SettlementGridData = result.SettlementGridData;

            // 個別矩形（基礎梁考慮）以外の解析結果を CaseRecord として永続化
            // (個別矩形（基礎梁考慮）は OpenGroupSettlementWithBeamWindow 側で既に保存済み)
            if (loadingType != "個別矩形（基礎梁考慮）")
            {
                UpsertNonBeamAwareCaseRecord(loadingType, result.SettlementGridData);
                // 一般解析は VL ケース 1 件のみ保存するため、解析直後は表示荷重ケースを VL に切替えて
                // 結果コンタを表示する。setter 経由で ActiveCase 同期 + 再描画が走るが、既に VL を
                // 選択中の場合は setter が発火しないため、明示的にも同期しておく。
                SelectedLoadCaseName = "VL";
                SyncGroupSettlementActiveCaseFromLoadCase("VL");
            }

            ShowToast("スタインブレナーの近似式による解析が終了しました。");

            IsGroupPileGridDeformationVisible = true;
            IsGroupPileSettlementAnalysisDone = true;
            //IsAnalysisResultVisible = true;
            IsBubbleVisible = true;
            IsArrowVisible = true;
            DisplacementDiagramRatio = 0.3;
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
        public async Task SaveInputModelFileAs()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "pdj"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CurrentFilePath = saveFileDialog.FileName;
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "保存中...";
                    // 解析結果保存フラグ OFF の場合は AnaModel/VerticalBeamCaseResults を null にして
                    // 入力のみの軽量ファイルとして保存する
                    var anaModelToSave = IsSaveAnalysisResultsManual ? CurrentModel : null;
                    var vbcrToSave = IsSaveAnalysisResultsManual ? VerticalBeamCaseResults : null;
                    await _fileOperationService.SaveProjectDataAsync(CurrentFilePath, CurrentInputModel, anaModelToSave, vbcrToSave);
                    ShowToast("保存が完了しました。");

                    // MRUに追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
                }
                catch (Exception ex)
                {
                    MessageService.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        [RelayCommand]
        public async Task SaveInputModelFile()
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
                await SaveInputModelFileAs();
            else
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "保存中...";
                    var anaModelToSave = IsSaveAnalysisResultsManual ? CurrentModel : null;
                    var vbcrToSave = IsSaveAnalysisResultsManual ? VerticalBeamCaseResults : null;
                    await _fileOperationService.SaveProjectDataAsync(CurrentFilePath, CurrentInputModel, anaModelToSave, vbcrToSave);
                    ShowToast("保存が完了しました。");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        [RelayCommand]
        public void NewInputModelFile()
        {
            var result = MessageService.Show(
                "現在のデータを保存しますか？",
                "確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return;
            else if (result == MessageBoxResult.Yes)
                _ = SaveInputModelFile();

            // 自動保存を停止
            _autoSaveService.Stop();

            CurrentInputModel.Reset();
            this.CurrentModel = null; // AnaModelもリセット
            CurrentFilePath = null;
            LoadedExampleName = null;  // 新規作成時はタイトルバーを [新規] に戻す

            // バイリニアコンクリート・オプションを既定 (false) へ戻し、キャッシュを破棄
            ApplyConcreteModelOptions();

            // ここで初期状態をUndoスタックに積む
            SaveUndoState();

            UpdateWindowImmediate();
        }

        private void TrySaveUndoSnapshotSafely([System.Runtime.CompilerServices.CallerMemberName] string? description = null)
        {
            try
            {
                var snapshot = CurrentInputModel?.DeepCopy();
                if (snapshot != null)
                {
                    _undoManager.SaveState(snapshot, FormatHistoryDescription(description));
                    RaiseUndoStateChanged();
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[TrySaveUndoSnapshotSafely] Exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// ファイル読込後の共通プロトコル。CurrentInputModel と CurrentModel を事前に設定してから呼ぶ。
        /// ProjectData 経由/InputModel 単体の両パスで同一のセットアップを実行することで
        /// 呼び忘れによる不具合（ComboBox が空白になる・Undo 履歴が残る等）を防ぐ。
        /// </summary>
        /// <param name="projectData">ProjectData 由来ロードの場合は非 null。InputModel 単体ロードの場合は null。</param>
        /// <param name="filePath">UI 表示用ファイルパス。Untitled/未確定の場合は null 可。</param>
        /// <param name="successMessage">読込成功時にトースト表示するメッセージ。</param>
        /// <summary>
        /// 基本設定のバイリニアコンクリート・オプション（引張無視 / 圧縮 0.85·Gsi·Fc）を
        /// 静的な <see cref="Models.InputData.ConcreteModelOptions"/> へ同期し、影響する全キャッシュを破棄する。
        ///
        /// これらのオプションは安全限界 NM 曲線だけでなく M-φ（→ 非線形 FEM 解析）にも効くため、
        /// 値を反映するには M-φ 静的キャッシュと各断面の NM/降伏/ひび割れキャッシュの両方をクリアする必要がある。
        /// モデル読込・新規作成・例題読込・基本設定での変更時に呼ぶ。
        /// </summary>
        public void ApplyConcreteModelOptions()
        {
            var f = CurrentInputModel?.FundamentalInput;
            Models.InputData.ConcreteModelOptions.IgnoreTensileStrength = f?.IgnoreConcreteTensileStrength ?? false;
            Models.InputData.ConcreteModelOptions.UseReducedCompression = f?.UseReducedConcreteCompressiveStrength ?? false;
            Models.InputData.ConcreteModelOptions.RebarYieldAt11F = f?.RebarYieldAt11F ?? false;
            Models.InputData.ConcreteModelOptions.SteelPipeYieldAt11F = f?.SteelPipeYieldAt11F ?? false;
            Models.InputData.ConcreteModelOptions.UseUnitGsiForConcreteE = f?.UseUnitGsiForConcreteE ?? false;

            // M-φ 静的キャッシュ（全断面共有）
            PileSection.ClearMphiCache();

            // 各断面インスタンスの NM/降伏/ひび割れキャッシュ
            if (CurrentInputModel?.PileBodies != null)
            {
                foreach (var pb in CurrentInputModel.PileBodies)
                {
                    if (pb?.PileBodySegments == null) continue;
                    foreach (var seg in pb.PileBodySegments)
                    {
                        var sec = seg?.PileSection;
                        if (sec == null) continue;
                        sec.InvalidateComputedCaches();
                        // ξ→Ec オプションは PileSection.ConcreteE（諸元表示・EA/EI）にも効くため、
                        // 場所打ち系（既製杭以外＝式ベース Ec）で再計算し諸元も更新する。
                        if (sec.PileBodyType != "既製コンクリート杭")
                        {
                            sec.RecalculateConcreteE();
                            sec.SetSpecs();
                        }
                    }
                }
            }
        }

        private void ApplyPostLoadProtocol(Models.ProjectData? projectData, string? filePath, string successMessage)
        {
            // ObservableCollection 変換（idempotent なので既に ObservableCollection なら維持）
            _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

            // 旧データとの互換性マイグレーション
            CurrentInputModel.InputNodes ??= [];
            CurrentInputModel.GridXItems ??= [];
            CurrentInputModel.GridYItems ??= [];
            CurrentInputModel.EnsureFoundationBeamDefaults();
            CurrentInputModel.EnsureAnalysisTargetDefaults();

            // PileZ セマンティクス v1 → v2: pile.Z を「杭頭節点」から「接合節点」へシフト
            // 適用条件: ProjectData.FormatVersion < 2 (旧ファイル) または InputModel 単独ロード (projectData == null)
            if (projectData == null || projectData.FormatVersion < 2)
            {
                CurrentInputModel.MigratePileZSemantics_v1_to_v2();
            }

            // CaseRecord.LoadingType の旧データ互換マイグレーション
            // 旧ファイルでは LoadingType フィールドが空文字 → IsBeamAware から推定して補完
            MigrateCaseRecordLoadingType(CurrentInputModel.PileGroupSettlement);

            // 梁要素 ComboBox 用の節点候補リストを再構築 (deserialize 直後は空のため)
            CurrentInputModel.RefreshAvailableNodeReferenceOptions();

            OnPropertyChanged(nameof(CurrentInputModel));

            // ViewModel アタッチ・ファイルパス確定
            CurrentInputModel.AttachViewModel(this);
            CurrentFilePath = filePath;

            // ComboBox 用カウントリスト再構築・M-φ キャッシュクリア
            CurrentInputModel.UpdateCountLists();
            // バイリニアコンクリート・オプションを同期し M-φ/NM キャッシュを破棄
            ApplyConcreteModelOptions();

            // 杭配置番号の同期（PileNo が未設定の旧ファイルに備える）
            UpdatePileLayoutNo();

            // 地震時軸力モード (絶対 / 変動) を InputModel から復元し、AxialForceModeContext + UI に反映。
            // VL/L1/L2 の値は既にロード済みなので、Context フラグの設定で即時に変動列の表示も切替可能。
            // 各杭の変動軸力コレクションを絶対値ベースで再構築 (deserialize 順序に依存しない安定状態を作る)。
            Common.AxialForceModeContext.IsVariationMode = CurrentInputModel.IsAxialForceVariationMode;
            if (CurrentInputModel.PileLayoutItems != null)
            {
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                    pile.RebuildVariationFromAbsolute();
            }
            OnPropertyChanged(nameof(IsAxialForceVariationMode));

            // 解析結果の復元（projectData=null の場合はフラグのみリセット）
            RestoreAnalysisState(projectData);

            // 水平解析結果を含むファイルをロードした場合、結果テーブル (LatestResultTables) を
            // AnaModel.AnalysisStepResults から再構築する。これがないと「テーブル出力」「グラフ」等の
            // 結果系コマンドの CanExecute が false のまま (ボタンが押せない) になる。
            // (RefreshResultTablesFromLastStep 内で CurrentModel/AnalysisStepResults/HasAnyAnalysisResult を
            //  チェックし、結果が無ければ LatestResultTables=[] にして安全にスキップする)
            RefreshResultTablesFromLastStep();
            RaiseResultCommandsCanExecute();

            // 結果タイプ ComboBox / バッジ用プロパティを再評価
            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(SelectedActiveLoadingType));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            // 群杭荷重「基礎梁:有/無」セレクタも基礎梁有無に連動するため再評価
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            OnPropertyChanged(nameof(AvailableLoadingTypeOptionsNonBeam));
            OnPropertyChanged(nameof(GroupSettlementBeamSelectorOptions));
            OnPropertyChanged(nameof(GroupSettlementBeamSelector));
            OnPropertyChanged(nameof(GroupSettlementLoadTypeOptions));
            OnPropertyChanged(nameof(GroupSettlementLoadType));
            OnPropertyChanged(nameof(IsManualRectLoadEditingEnabled));

            // 基礎梁考慮 群杭沈下解析リボンボタン等の CanExecute を再評価
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            OpenGroupSettlementWithBeamWindowCommand?.NotifyCanExecuteChanged();

            // Undo 履歴をクリアして読込状態を初期状態として保存
            _undoManager.Clear();
            SaveUndoState();

            // 最終描画＆通知
            UpdateWindowImmediate();
            ShowToast(successMessage);

            // 既製コンクリート杭ライブラリの整合性チェック (デフォルト径 1200mm にフォールバックして
            // 描画・解析が意図せず狂うのを防ぐため、ロード後に一括で検証して警告する)
            ShowPrecastPileNameWarningsIfAny(CurrentInputModel);
        }

        /// <summary>
        /// 全 PileSection の SelectedPrecastPile.Name がライブラリに存在するかチェックし、
        /// 不一致があれば箇所のリストを返す。副作用なし。
        /// </summary>
        private static List<string> ValidatePrecastPileNames(InputModel model)
        {
            var issues = new List<string>();
            if (model?.PileBodies == null) return issues;

            for (int i = 0; i < model.PileBodies.Count; i++)
            {
                var pb = model.PileBodies[i];
                if (pb?.PileBodyType != "既製コンクリート杭") continue; // 既製のみ対象
                if (pb.PileBodySegments == null) continue;

                for (int j = 0; j < pb.PileBodySegments.Count; j++)
                {
                    var seg = pb.PileBodySegments[j];
                    var sec = seg?.PileSection;
                    if (sec == null) continue;

                    if (!sec.IsSelectedPrecastPileInLibrary())
                    {
                        var name = sec.SelectedPrecastPile?.Name ?? "(empty)";
                        var refLabel = string.IsNullOrEmpty(pb.PileBodyRef) ? "" : $" {pb.PileBodyRef}";
                        issues.Add($"杭体 No.{i + 1}{refLabel} 区間 No.{j + 1}: 「{name}」が {sec.PileSectionType} ライブラリに存在しません。");
                    }
                }
            }
            return issues;
        }

        /// <summary>
        /// ValidatePrecastPileNames の結果を MessageBox で表示する。問題がなければ何もしない。
        /// </summary>
        private static void ShowPrecastPileNameWarningsIfAny(InputModel model)
        {
            var issues = ValidatePrecastPileNames(model);
            if (issues.Count == 0) return;

            string body =
                "次の杭断面名が既製杭ライブラリに存在しません。\n" +
                "PileDiameter はデフォルト値 1200mm のままになり、描画や応力検定が意図しない挙動になる可能性があります。\n" +
                "杭体ウィンドウで杭断面を選び直してください。\n\n" +
                string.Join("\n", issues);

            PileDesign.Services.MessageService.Show(
                body,
                "杭断面名の不一致",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        // InputModel 単体ロード（ProjectData ラッパーでない旧形式ファイル用フォールバック）
        public bool TryLoadInputModelFileUsingInputModelLoader(string filePath)
        {
            try
            {
                var loaded = InputModel.LoadFromFile(filePath, this);
                if (loaded == null)
                {
                    MessageService.Show($"ファイルの読込に失敗しました。\n{filePath}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                CurrentInputModel = loaded;
                // InputModel 単体ロードには解析結果が含まれないため projectData=null で渡す。
                // CurrentModel と解析フラグは RestoreAnalysisState 内でリセットされる。
                ApplyPostLoadProtocol(projectData: null, filePath: filePath, successMessage: "読込が完了しました。");
                return true;
            }
            catch (Exception ex)
            {
                MessageService.Show($"ファイル読込中にエラーが発生しました。\n{ex.Message}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        [RelayCommand]
        public void OpenInputModelFileSimple()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "PileDesign プロジェクト (*.pdj;*.json)|*.pdj;*.json|PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json", DefaultExt = "pdj" };
            if (ofd.ShowDialog() != true) return;
            // Undo 保存は安全ヘルパを使用
            TrySaveUndoSnapshotSafely();
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                TryLoadInputModelFileUsingInputModelLoader(ofd.FileName);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        [RelayCommand]
        public async Task OpenInputModelFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PileDesign プロジェクト (*.pdj;*.json)|*.pdj;*.json|PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json",
                DefaultExt = "pdj"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "読込中...";
                    var projectData = await _fileOperationService.LoadProjectDataAsync(openFileDialog.FileName);

                    if (projectData != null)
                    {
                        CurrentInputModel = projectData.InputModel;
                        CurrentModel = projectData.AnaModel;
                        ApplyPostLoadProtocol(projectData, openFileDialog.FileName, "読込が完了しました。");
                    }
                    else
                    {
                        // ProjectDataでない場合を想定して InputModel 単体で読めるか試す
                        var ok = TryLoadInputModelFileUsingInputModelLoader(openFileDialog.FileName);
                        if (!ok)
                            throw new InvalidOperationException("ファイル形式が不正です。ProjectData でも InputModel でもありません。");
                        return;
                    }

                    // MRU に追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
                }
                catch (Exception ex)
                {
                    HandleFileLoadError(ex, openFileDialog.FileName);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        /// <summary>
        /// ファイル読込時に解析結果の状態を復元する
        /// </summary>
        private void RestoreAnalysisState(Models.ProjectData? projectData)
        {
            // まずすべてリセット
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            VerticalBeamCaseResults = null;

            // projectData が null（InputModel 単体ロード）の場合は
            // リセットのみで完了。CurrentModel の以前の値を消すために null を代入する。
            if (projectData == null)
            {
                CurrentModel = null;
                return;
            }

            var anaModel = CurrentModel;
            var soilPiles = CurrentInputModel?.ElementDivision?.SoilPiles;

            // 杭要素分割済み判定: AnaModelにノードが存在する
            if (anaModel?.Nodes != null && anaModel.Nodes.Count > 0)
            {
                IsElementSplit = true;
            }

            // 水平解析済み判定: AnalysisStepResultsが存在する
            if (anaModel?.AnalysisStepResults != null && anaModel.AnalysisStepResults.Count > 0)
            {
                IsHorizontalAnalysisDone = true;
            }

            // 単杭沈下解析済み判定: SoilPilesにLoadDisplacementsが存在する
            if (soilPiles != null && soilPiles.Count > 0)
            {
                bool anyHasLoadDisp = false;
                foreach (var sp in soilPiles)
                {
                    if (sp.LoadDisplacements != null && sp.LoadDisplacements.Count > 0)
                    {
                        anyHasLoadDisp = true;
                        break;
                    }
                }
                if (anyHasLoadDisp)
                    IsVerticalAnalysisDone = true;
            }

            // 群杭沈下解析済み判定: SettlementGridDataが存在する
            var settlement = CurrentInputModel?.PileGroupSettlement;
            if (settlement?.SettlementGridData != null && settlement.SettlementGridData.Count > 0)
            {
                IsGroupPileSettlementAnalysisDone = true;
            }

            // 基礎梁鉛直解析結果の復元
            if (projectData.VerticalBeamCaseResults != null && projectData.VerticalBeamCaseResults.Count > 0)
            {
                VerticalBeamCaseResults = new ObservableCollection<FEM.VerticalBeamCaseResult>(projectData.VerticalBeamCaseResults);
                IsVerticalBeamAnalysisDone = true;
            }
        }

        // Word ファイルに保存するメソッド
        [RelayCommand]
        public void OutputWordFile()
        {
            // ファイル名: (yyMMdd)_(HHmm)_構造計算書_(本体ファイル名).docx
            // 本体ファイル名がない (未保存) 場合は「Untitled」をフォールバック。
            string projectBaseName = !string.IsNullOrEmpty(CurrentFilePath)
                ? System.IO.Path.GetFileNameWithoutExtension(CurrentFilePath)
                : "Untitled";
            string defaultDocxName = $"{DateTime.Now:yyMMdd_HHmm}_構造計算書_{projectBaseName}.docx";

            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*",
                FileName = defaultDocxName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // 砂時計カーソル + ステータス更新で「ビジー中」を可視化
                var prevCursor = System.Windows.Input.Mouse.OverrideCursor;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try
                {
                    StatusMessage = "計算書作成中... (大規模モデルでは数十秒〜数分かかる場合があります)";
                    Serilog.Log.Information("[Docx] 開始: {File}", System.IO.Path.GetFileName(saveFileDialog.FileName));

                    var doc = new Output.WordDocument(CurrentInputModel, CurrentModel, this);
                    doc.CreateWordDocument(CurrentInputModel, saveFileDialog.FileName);

                    sw.Stop();
                    Serilog.Log.Information("[Docx] 完了: {Elapsed:N1} 秒, ファイル: {File}",
                        sw.Elapsed.TotalSeconds, System.IO.Path.GetFileName(saveFileDialog.FileName));

                    ShowToast($"docxファイル作成完了 ({sw.Elapsed.TotalSeconds:N1}秒)。Wordで開き、目次上をクリック→F9 でフィールドを更新してください。");

                    // 作成したdocxファイルを自動的に開く
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Serilog.Log.Warning(ex, "[Docx] 失敗 ({Elapsed:N1}秒経過時点)", sw.Elapsed.TotalSeconds);
                    MessageService.Show($"Word出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    System.Windows.Input.Mouse.OverrideCursor = prevCursor;
                    StatusMessage = "準備完了";
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
                    MessageService.Show($"3dm出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageService.Show($"DXF出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportDxfPlanFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
                DefaultExt = ".dxf",
                FileName = "PilePlan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".dxf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.DxfPlanExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"伏図DXFファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"伏図DXF出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // midas Gen MGT ファイルにエクスポートするメソッド
        [RelayCommand]
        public void ExportMgtFile()
        {
            if (CurrentModel == null)
            {
                MessageService.Show("水平解析が実行されていません。\n解析モデルをエクスポートするには、先に水平解析を実行してください。",
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
                    MessageService.Show($"MGT出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // 水平解析済みの荷重ケース・荷重組合せ・液状化条件を判定
                UpdateDocxOutputAnalyzedFlags();

                var dockxOutputOptionWindow = new DocxOutputWindow(this)
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                };
                dockxOutputOptionWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageService.Show($"計算書出力ウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// AnalysisStepResults を参照し、各荷重ケース・荷重組合せ・液状化条件の解析済みフラグを設定。
        ///
        /// ⚠ 注意: IsApplicable には触らない。
        ///   - IsApplicable は「ユーザーが解析対象に含めたい」という入力意図 (CanExecuteAnalysis でも参照)
        ///   - IsAnalyzed は「実際に解析が済んでいるか」の結果状態
        ///   両者を混同して IsApplicable=false に強制すると、未解析時に DocxOutputWindow を開いただけで
        ///   F9 ボタンが永久に押せなくなる。docx 出力でフィルタする場合は IsAnalyzed (もしくは
        ///   解析結果存在チェック) のみで判定する。
        /// </summary>
        private void UpdateDocxOutputAnalyzedFlags()
        {
            var results = CurrentModel?.AnalysisStepResults;
            if (results == null || results.Count == 0)
            {
                // 解析結果なし → すべて IsAnalyzed=false に
                foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel1) lc.IsAnalyzed = false;
                foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel2) lc.IsAnalyzed = false;
                foreach (var comb in CurrentInputModel.LoadCasesInput.LoadCombinations) comb.IsAnalyzed = false;
                IsLiquefactionYesAnalyzed = false;
                IsLiquefactionNoAnalyzed = false;
                IncludeOutputLiquefactionYes = false;
                IncludeOutputLiquefactionNo = false;
                return;
            }

            // 解析済み荷重ケース名のセット
            var analyzedLoadCaseNames = new HashSet<string>(
                results.Where(r => r.LoadCase != null).Select(r => r.LoadCase.LoadName));

            foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
                lc.IsAnalyzed = analyzedLoadCaseNames.Contains(lc.LoadName);
            foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
                lc.IsAnalyzed = analyzedLoadCaseNames.Contains(lc.LoadName);

            // 解析済み荷重組合せ名のセット
            var analyzedCombNames = new HashSet<string>(
                results.Where(r => r.LoadCombination != null).Select(r => r.LoadCombination.Name));

            foreach (var comb in CurrentInputModel.LoadCasesInput.LoadCombinations)
                comb.IsAnalyzed = analyzedCombNames.Contains(comb.Name);

            // 液状化条件
            IsLiquefactionYesAnalyzed = results.Any(r => r.IsLiquefaction);
            IsLiquefactionNoAnalyzed = results.Any(r => !r.IsLiquefaction);
            IncludeOutputLiquefactionYes = IsLiquefactionYesAnalyzed;
            IncludeOutputLiquefactionNo = IsLiquefactionNoAnalyzed;
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
                MessageService.Show($"オプションウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 解析結果が1つでも存在するか（バインド用プロパティ）
        // 反復解析 (HasGroupSettlementBeamAwareCases) も含めて判定する
        public bool HasAnyAnalysisResult
            => IsHorizontalAnalysisDone || IsVerticalAnalysisDone
               || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone
               || HasGroupSettlementBeamAwareCases;

        // コマンド状態一括更新ヘルパ
        private void RaiseResultCommandsCanExecute()
        {
            if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
            OpenGraphWindowCommand?.NotifyCanExecuteChanged();
            OpenLogWindowCommand?.NotifyCanExecuteChanged();
            OpenEvaluationWindowCommand?.NotifyCanExecuteChanged();
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
                MessageService.Show($"グラフウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool CanOpenGraphWindow() => HasAnyAnalysisResult;


        // フィールド追加
        private readonly AnalysisResultTableService _tableService = new();

        // プロパティ
        public IReadOnlyList<ResultTable> LatestResultTables { get; private set; } = [];

        // 解析ログ（種別別に保持）
        public ObservableCollection<string> LatestAnalysisLogs { get; private set; } = [];
        public ObservableCollection<string> HorizontalAnalysisLogs { get; private set; } = [];
        public ObservableCollection<string> VerticalBeamAnalysisLogs { get; private set; } = [];

        public void SetLatestAnalysisLogs(IReadOnlyList<string> logs)
        {
            HorizontalAnalysisLogs.Clear();
            foreach (var log in logs)
                HorizontalAnalysisLogs.Add(log);

            // 統合ログも更新
            RebuildLatestAnalysisLogs();
        }

        public void SetVerticalBeamAnalysisLogs(IReadOnlyList<string> logs)
        {
            VerticalBeamAnalysisLogs.Clear();
            foreach (var log in logs)
                VerticalBeamAnalysisLogs.Add(log);

            RebuildLatestAnalysisLogs();
        }

        private void RebuildLatestAnalysisLogs()
        {
            LatestAnalysisLogs.Clear();
            if (HorizontalAnalysisLogs.Count > 0)
            {
                foreach (var log in HorizontalAnalysisLogs)
                    LatestAnalysisLogs.Add(log);
            }
            if (VerticalBeamAnalysisLogs.Count > 0)
            {
                if (LatestAnalysisLogs.Count > 0) LatestAnalysisLogs.Add("");
                LatestAnalysisLogs.Add("=== 基礎梁考慮沈下解析 ===");
                foreach (var log in VerticalBeamAnalysisLogs)
                    LatestAnalysisLogs.Add(log);
            }
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
                result.Step,
                CurrentInputModel);

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

        public ICommand OpenTableWindowCommand { get; private set; }

        private void OpenTableWindow()
        {
            try
            {
                var vm = new TableWindowViewModel();
                vm.AllSeismicLoadCases = CurrentInputModel.LoadCasesInput.AllSeismicLoadCases;
                vm.AnaModel = CurrentModel;

                // 水平解析等の結果テーブルと基礎梁鉛直解析の結果テーブルを統合
                var allTables = new List<ResultTable>(LatestResultTables);
                if (VerticalBeamCaseResults != null)
                {
                    allTables.AddRange(BuildVerticalBeamResultTables());
                }
                // 土層沈下解析（反復）の結果テーブル
                allTables.AddRange(BuildGroupSettlementBeamAwareTables());
                // 土層沈下解析（一般）の結果テーブル
                allTables.AddRange(BuildGroupSettlementNonBeamAwareTables());
                vm.LoadTables(allTables);

                var w = new Views.TableWindow { DataContext = vm };
                w.Show();
            }
            catch (Exception ex)
            {
                MessageService.Show($"テーブルウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 基礎梁鉛直解析結果からResultTableリストを生成する
        /// </summary>
        private List<ResultTable> BuildVerticalBeamResultTables()
        {
            var tables = new List<ResultTable>();
            if (VerticalBeamCaseResults == null) return tables;

            foreach (var caseResult in VerticalBeamCaseResults)
            {
                // 杭結果テーブル
                if (caseResult.PileResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "杭No", Order = 0, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.PileNo))! },
                        new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.X))!, Format = "N3" },
                        new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Y))!, Format = "N3" },
                        new() { Header = "入力荷重 (kN)", Order = 3, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.InputLoad_kN))!, Format = "N1" },
                        new() { Header = "杭反力 (kN)", Order = 4, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Reaction_kN))!, Format = "N1" },
                        new() { Header = "沈下量 (mm)", Order = 5, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Settlement_mm))!, Format = "N2" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 杭結果 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.PileResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }

                // 節点変位テーブル
                if (caseResult.NodeResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "節点名", Order = 0, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.NodeName))! },
                        new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.X))!, Format = "N3" },
                        new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Y))!, Format = "N3" },
                        new() { Header = "Z (m)", Order = 3, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Z))!, Format = "N3" },
                        new() { Header = "Uz (mm)", Order = 4, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Uz_mm))!, Format = "N3" },
                        new() { Header = "Rx (rad)", Order = 5, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Rx_rad))!, Format = "F5" },
                        new() { Header = "Ry (rad)", Order = 6, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Ry_rad))!, Format = "F5" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 節点変位 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.NodeResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }

                // 梁応力テーブル
                if (caseResult.BeamResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "梁名", Order = 0, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.BeamName))! },
                        new() { Header = "Ni (kN)", Order = 1, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Ni))!, Format = "N1" },
                        new() { Header = "Qyi (kN)", Order = 2, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyi))!, Format = "N1" },
                        new() { Header = "Qzi (kN)", Order = 3, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzi))!, Format = "N1" },
                        new() { Header = "Mxi (kNm)", Order = 4, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxi))!, Format = "N1" },
                        new() { Header = "Myi (kNm)", Order = 5, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myi))!, Format = "N1" },
                        new() { Header = "Mzi (kNm)", Order = 6, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzi))!, Format = "N1" },
                        new() { Header = "Nj (kN)", Order = 7, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Nj))!, Format = "N1" },
                        new() { Header = "Qyj (kN)", Order = 8, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyj))!, Format = "N1" },
                        new() { Header = "Qzj (kN)", Order = 9, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzj))!, Format = "N1" },
                        new() { Header = "Mxj (kNm)", Order = 10, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxj))!, Format = "N1" },
                        new() { Header = "Myj (kNm)", Order = 11, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myj))!, Format = "N1" },
                        new() { Header = "Mzj (kNm)", Order = 12, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzj))!, Format = "N1" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 梁応力 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.BeamResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }
            }

            return tables;
        }

        /// <summary>
        /// 土層沈下解析（反復）(個別矩形（基礎梁考慮）反復) の結果テーブルを生成。
        /// 杭結果 / 節点変位 / 梁応力 / 土層グリッド変位 を全ケース分。
        /// </summary>
        private List<ResultTable> BuildGroupSettlementBeamAwareTables()
        {
            var tables = new List<ResultTable>();
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return tables;

            const string category = "土層沈下解析（反復）";
            const string prefix = "土層沈下解析（反復）";

            foreach (var rec in pgs.CaseRecords.Where(r => r.IsBeamAware))
            {
                string caseName = rec.LoadCaseName ?? "";

                // 杭結果テーブル: PileSettlements_mm / PileReactions_kN / SpringStiffness を統合した行
                if (CurrentInputModel.PileLayoutItems != null && rec.PileSettlements_mm.Count > 0)
                {
                    var rows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        rec.PileReactions_kN.TryGetValue(pile.PileNo, out double r);
                        rec.SpringStiffness.TryGetValue(pile.PileNo, out double k);
                        rows.Add(new BeamAwarePileResultRow
                        {
                            PileNo = pile.PileNo,
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Reaction_kN = r,
                            Settlement_mm = s,
                            SpringStiffness_kN_per_m = k,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 杭結果",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "杭No", Order = 0, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.PileNo))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Y))!, Format = "N3" },
                            new() { Header = "基礎反力 (kN)", Order = 3, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Reaction_kN))!, Format = "N1" },
                            new() { Header = "沈下量 (mm)", Order = 4, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Settlement_mm))!, Format = "N2" },
                            new() { Header = "ばね (kN/m)", Order = 5, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.SpringStiffness_kN_per_m))!, Format = "E2" },
                        ],
                        Rows = rows,
                        LoadCaseName = caseName,
                    });
                }

                // 節点変位
                if (rec.NodeResults?.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 節点変位",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "節点名", Order = 0, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.NodeName))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Y))!, Format = "N3" },
                            new() { Header = "Z (m)", Order = 3, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Z))!, Format = "N3" },
                            new() { Header = "Uz (mm)", Order = 4, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Uz_mm))!, Format = "N3" },
                            new() { Header = "Rx (rad)", Order = 5, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Rx_rad))!, Format = "F5" },
                            new() { Header = "Ry (rad)", Order = 6, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Ry_rad))!, Format = "F5" },
                        ],
                        Rows = rec.NodeResults.Cast<object>().ToList(),
                        LoadCaseName = caseName,
                    });
                }

                // 梁応力
                if (rec.BeamResults?.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 梁応力",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "梁名", Order = 0, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.BeamName))! },
                            new() { Header = "Ni (kN)",   Order = 1,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Ni))!, Format = "N1" },
                            new() { Header = "Qyi (kN)",  Order = 2,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyi))!, Format = "N1" },
                            new() { Header = "Qzi (kN)",  Order = 3,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzi))!, Format = "N1" },
                            new() { Header = "Mxi (kNm)", Order = 4,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxi))!, Format = "N1" },
                            new() { Header = "Myi (kNm)", Order = 5,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myi))!, Format = "N1" },
                            new() { Header = "Mzi (kNm)", Order = 6,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzi))!, Format = "N1" },
                            new() { Header = "Nj (kN)",   Order = 7,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Nj))!, Format = "N1" },
                            new() { Header = "Qyj (kN)",  Order = 8,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyj))!, Format = "N1" },
                            new() { Header = "Qzj (kN)",  Order = 9,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzj))!, Format = "N1" },
                            new() { Header = "Mxj (kNm)", Order = 10, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxj))!, Format = "N1" },
                            new() { Header = "Myj (kNm)", Order = 11, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myj))!, Format = "N1" },
                            new() { Header = "Mzj (kNm)", Order = 12, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzj))!, Format = "N1" },
                        ],
                        Rows = rec.BeamResults.Cast<object>().ToList(),
                        LoadCaseName = caseName,
                    });
                }

                // 土層グリッド変位 (反復)
                if (rec.SettlementGridData?.Count > 0)
                {
                    tables.Add(BuildSettlementGridTable(
                        $"{prefix} 土層グリッド変位",
                        category, caseName, rec.SettlementGridData));
                }
            }
            return tables;
        }

        /// <summary>
        /// 土層沈下解析（一般）(個別矩形（基礎梁考慮）以外) の結果テーブルを生成。
        /// 杭結果 / 節点変位 / 土層グリッド変位 を全ケース分。
        /// 一般解析は梁解析を行わないため、節点変位は杭頭沈下のみを示す簡易表となる。
        /// </summary>
        private List<ResultTable> BuildGroupSettlementNonBeamAwareTables()
        {
            var tables = new List<ResultTable>();
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return tables;

            const string category = "土層沈下解析（一般）";
            const string prefix = "土層沈下解析（一般）";

            foreach (var rec in pgs.CaseRecords.Where(r => !r.IsBeamAware))
            {
                string caseName = rec.LoadCaseName ?? "";

                // 杭結果: 杭ごとの 沈下量 (反力やばねは無いが LinkedPileNo に紐付く矩形荷重 QA を表示)
                if (CurrentInputModel.PileLayoutItems != null && rec.PileSettlements_mm.Count > 0)
                {
                    var loadByPile = new Dictionary<int, double>();
                    if (rec.RectLoads != null)
                    {
                        foreach (var rl in rec.RectLoads)
                        {
                            if (rl.LinkedPileNo > 0)
                            {
                                loadByPile.TryGetValue(rl.LinkedPileNo, out double agg);
                                loadByPile[rl.LinkedPileNo] = agg + rl.QA;
                            }
                        }
                    }
                    var rows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        loadByPile.TryGetValue(pile.PileNo, out double load);
                        rows.Add(new NonBeamAwarePileResultRow
                        {
                            PileNo = pile.PileNo,
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Load_kN = load,
                            Settlement_mm = s,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 杭結果",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "杭No", Order = 0, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.PileNo))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Y))!, Format = "N3" },
                            new() { Header = "荷重 (kN)", Order = 3, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Load_kN))!, Format = "N1" },
                            new() { Header = "沈下量 (mm)", Order = 4, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Settlement_mm))!, Format = "N2" },
                        ],
                        Rows = rows,
                        LoadCaseName = caseName,
                    });

                    // 節点変位 (一般): 梁解析を行わないため、杭頭位置 (= 節点) と Uz のみ。
                    var nodeRows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        nodeRows.Add(new NonBeamAwareNodeRow
                        {
                            NodeName = $"Pile-{pile.PileNo}",
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Z = pile.Point3D.Z,
                            Uz_mm = s,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 節点変位",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "節点名", Order = 0, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.NodeName))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Y))!, Format = "N3" },
                            new() { Header = "Z (m)", Order = 3, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Z))!, Format = "N3" },
                            new() { Header = "Uz (mm)", Order = 4, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Uz_mm))!, Format = "N3" },
                        ],
                        Rows = nodeRows,
                        LoadCaseName = caseName,
                    });
                }

                // 土層グリッド変位 (一般)
                if (rec.SettlementGridData?.Count > 0)
                {
                    tables.Add(BuildSettlementGridTable(
                        $"{prefix} 土層グリッド変位",
                        category, caseName, rec.SettlementGridData));
                }
            }
            return tables;
        }

        /// <summary>
        /// 土層グリッド変位テーブルを共通で構築 (X, Y, 沈下量 mm)。
        /// </summary>
        private static ResultTable BuildSettlementGridTable(
            string name, string category, string caseName,
            System.Collections.Generic.IEnumerable<Models.InputData.SettlementGridDataItem> grid)
        {
            return new ResultTable
            {
                Name = name,
                Category = category,
                Columns =
                [
                    new() { Header = "X (m)", Order = 0, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.X))!, Format = "N3" },
                    new() { Header = "Y (m)", Order = 1, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.Y))!, Format = "N3" },
                    new() { Header = "沈下量 (mm)", Order = 2, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.Settlement))!, Format = "N3" },
                ],
                Rows = grid.Cast<object>().ToList(),
                LoadCaseName = caseName,
            };
        }

        /// <summary>テーブル出力用の杭結果行 (土層沈下解析（反復）)。</summary>
        public class BeamAwarePileResultRow
        {
            public int PileNo { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Reaction_kN { get; set; }
            public double Settlement_mm { get; set; }
            public double SpringStiffness_kN_per_m { get; set; }
        }

        /// <summary>テーブル出力用の杭結果行 (土層沈下解析（一般）)。</summary>
        public class NonBeamAwarePileResultRow
        {
            public int PileNo { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Load_kN { get; set; }
            public double Settlement_mm { get; set; }
        }

        /// <summary>テーブル出力用の節点行 (土層沈下解析（一般）: 杭頭のみ Uz)。</summary>
        public class NonBeamAwareNodeRow
        {
            public string NodeName { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Uz_mm { get; set; }
        }

        // ケースタグ抽出正規表現 (例: [L2-1.C1.Liq], [L1-2.C3.NoLq])
        // HorizontalCalculationWindow.xaml.cs の CaseTagPattern と同等。
        private static readonly System.Text.RegularExpressions.Regex CaseTagPattern =
            new(@"\[L\d+-\d+\.C\d+\.(?:Liq|NoLq)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        [RelayCommand(CanExecute = nameof(CanOpenLogWindow))]
        private void OpenLogWindow()
        {
            if (!CanOpenLogWindow()) return;

            // ログ種別リストを構築。複数ケース解析時はケース別フィルタ済ビューも追加する。
            var logSources = new Dictionary<string, IEnumerable<string>>();
            if (HorizontalAnalysisLogs.Count > 0)
            {
                logSources["水平解析 (全体)"] = HorizontalAnalysisLogs;

                // ログ内のユニークなケースタグを抽出して、ケース別カテゴリを追加
                var caseTags = HorizontalAnalysisLogs
                    .Select(line => CaseTagPattern.Match(line))
                    .Where(m => m.Success)
                    .Select(m => m.Value)
                    .Distinct()
                    .OrderBy(t => t, System.StringComparer.Ordinal)
                    .ToList();

                foreach (var tag in caseTags)
                {
                    // 該当ケースタグを含む行のみ抽出 (open 時点のスナップショット)
                    var filtered = HorizontalAnalysisLogs
                        .Where(line => line.Contains(tag))
                        .ToList();
                    logSources[$"水平解析 {tag}"] = filtered;
                }
            }
            if (VerticalBeamAnalysisLogs.Count > 0)
                logSources["基礎梁考慮沈下解析"] = VerticalBeamAnalysisLogs;

            // 個別矩形（基礎梁考慮）反復解析の永続化ログ
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords != null)
            {
                foreach (var rec in pgs.CaseRecords.Where(r => r.IsBeamAware && r.IterationLog?.Count > 0))
                {
                    logSources[$"土層沈下解析（反復） [{rec.LoadCaseName}]"] = rec.IterationLog;
                }
            }

            var vm = new LogWindowViewModel(logSources);
            var w = new Views.LogWindow { DataContext = vm };
            w.Show();
        }

        private bool CanOpenLogWindow()
        {
            if (HorizontalAnalysisLogs.Count > 0) return true;
            if (VerticalBeamAnalysisLogs.Count > 0) return true;
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords?.Any(r => r.IsBeamAware && r.IterationLog?.Count > 0) == true) return true;
            return false;
        }

        private Views.EvaluationWindow? _evaluationWindow;

        [RelayCommand(CanExecute = nameof(CanOpenEvaluationWindow))]
        private void OpenEvaluationWindow()
        {
            if (_evaluationWindow is { IsVisible: true })
            {
                _evaluationWindow.Activate();
                return;
            }

            var vm = new EvaluationWindowViewModel(this);
            _evaluationWindow = new Views.EvaluationWindow { DataContext = vm, Topmost = true };
            _evaluationWindow.Closed += (_, _) => _evaluationWindow = null;
            _evaluationWindow.Show();
        }

        private bool CanOpenEvaluationWindow()
        {
            if (IsHorizontalAnalysisDone && CurrentModel != null) return true;
            // 個別矩形（基礎梁考慮）反復解析の結果でも 傾斜角検定 を許可
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords?.Any(r => r.IsBeamAware) == true) return true;
            return false;
        }

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

            // 全ての解析結果から一意の組合せ（LoadCase, LoadCombination, IsLiquefaction）を取得
            // 各組合せについて最終ステップのテーブルを生成
            var allTables = new List<ResultTable>();

            var uniqueCombinations = CurrentModel.AnalysisStepResults
                .GroupBy(r => new
                {
                    LoadCaseName = r.LoadCase?.LoadName ?? "",
                    LoadCombinationName = r.LoadCombination?.Name ?? "",
                    r.IsLiquefaction
                })
                .Select(g => g.OrderByDescending(r => r.Step).First()) // 各組合せの最終ステップを取得
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
                    stepResult.Step,
                    CurrentInputModel);

                allTables.AddRange(tables);
            }

            foreach (var t in allTables)
            {
            }

            LatestResultTables = allTables;

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

        // メインと別の STA UI スレッドで動かす補助ウィンドウのホスト。
        // これにより、メイン側の ShowDialog()（モーダル）中でも対象ウィンドウをスクロール・クリックできる。
        private sealed class SeparateUiWindowHost
        {
            public Window? Window;
            public System.Windows.Threading.Dispatcher? Dispatcher;
            public System.Threading.Thread? Thread;
            public readonly object Lock = new();
        }

        private static readonly SeparateUiWindowHost _helpWindowHost = new();
        private static readonly SeparateUiWindowHost _helpChatWindowHost = new();
        private static readonly SeparateUiWindowHost _verificationWindowHost = new();
        private static readonly SeparateUiWindowHost _shortcutKeysWindowHost = new();

        // 旧参照を維持（キーダウン等の外部参照で存在すれば使う）
        private static HelpWindow? _helpWindow => _helpWindowHost.Window as HelpWindow;
        private static VerificationWindow? _verificationWindow => _verificationWindowHost.Window as VerificationWindow;
        private static PileDesign.Views.ShortcutKeysWindow? _shortcutKeysWindow => _shortcutKeysWindowHost.Window as PileDesign.Views.ShortcutKeysWindow;

        /// <summary>
        /// STA バックグラウンドスレッド上でウィンドウを表示する。
        /// モーダルダイアログ中でも独立して入力を受け付けられる。
        /// 既に開いていれば対象スレッドで Activate するだけ。
        /// </summary>
        private static void OpenOnSeparateUiThread(SeparateUiWindowHost host, Func<Window> factory, string errorPrefix, Action<Window>? onActivate = null)
        {
            try
            {
                lock (host.Lock)
                {
                    if (host.Dispatcher != null && host.Window != null)
                    {
                        var existing = host.Window;
                        var dispatcher = host.Dispatcher;
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                existing.Activate();
                                onActivate?.Invoke(existing);
                            }
                            catch { /* ウィンドウが閉じ中の場合は無視 */ }
                        }));
                        return;
                    }

                    host.Thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            var window = factory();
                            host.Window = window;
                            host.Dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                            window.Closed += (_, _) =>
                            {
                                lock (host.Lock)
                                {
                                    host.Window = null;
                                    host.Dispatcher = null;
                                }
                                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                            };
                            window.Show();
                            System.Windows.Threading.Dispatcher.Run();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[{errorPrefix}Thread]");
                            lock (host.Lock)
                            {
                                host.Window = null;
                                host.Dispatcher = null;
                            }
                        }
                    });
                    host.Thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    host.Thread.IsBackground = true;
                    host.Thread.Start();
                }
            }
            catch (Exception ex)
            {
                MessageService.Show($"{errorPrefix}ウィンドウの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public static void OpenHelpWindow()
        {
            OpenOnSeparateUiThread(_helpWindowHost,
                () => new HelpWindow(),
                "ヘルプ");
        }

        /// <summary>
        /// ヘルプウィンドウを指定 anchor または見出しタイトルへスクロールして開く (チャットからの遷移用)。
        /// 既に開いていれば NavigateTo で更新、未オープンなら新規作成。
        /// </summary>
        public static void OpenHelpWindowAt(string? anchor, string? scrollToTitle)
        {
            OpenOnSeparateUiThread(_helpWindowHost,
                () => new HelpWindow(anchor, scrollToTitle),
                "ヘルプ",
                existing =>
                {
                    if (existing is HelpWindow hw)
                        hw.NavigateTo(anchor, scrollToTitle);
                });
        }

        [RelayCommand]
        public static void OpenHelpChatWindow()
        {
            OpenOnSeparateUiThread(_helpChatWindowHost,
                () => new HelpChatWindow { Topmost = true },
                "ヘルプチャット");
        }

        // 設計例によるプログラムの検証ウィンドウ表示
        [RelayCommand]
        public static void OpenVerificationWindow()
        {
            OpenOnSeparateUiThread(_verificationWindowHost,
                () => new VerificationWindow { Topmost = true },
                "検証");
        }

        // 2026-05-19: PileDesign.Mcp (prototype) を廃止したため RegisterMcpServer コマンドと
        // FindClaudeDesktopConfigPath を削除。UI へのバインドも元々存在せず orphan だった。

        [RelayCommand]
        public void OnQuickHint()
        {
            IsQuickHintVisible = true;
        }

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
                MessageService.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 変更後（以下の箇所で適用）
            UpdateWindowImmediate();
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
                PileDesign.Services.MessageService.Show($"杭ライブラリ表示に失敗しました: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public static void OpenShortcutKeysWindow()
        {
            // 別スレッド上のウィンドウに対して、メインウィンドウを Owner に設定することはできない。
            // WindowStartupLocation は CenterScreen で代替する。
            OpenOnSeparateUiThread(_shortcutKeysWindowHost,
                () => new PileDesign.Views.ShortcutKeysWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = false,
                    Topmost = true
                },
                "ショートカット一覧");
        }

        /// <summary>
        /// .pdj 拡張子を現在のユーザーで PileDesign に関連付ける。
        /// Portable (zip 配布) でも admin 権限不要。HKCU\Software\Classes に書込後、
        /// Windows の「既定のアプリ」設定ページを開いてユーザーに最終選択を促す。
        /// </summary>
        [RelayCommand]
        public void RegisterPdjAssociation()
        {
            var ok = PileDesign.Services.FileAssociationService.Register();
            if (!ok)
            {
                MessageService.Show(
                    "拡張子 .pdj の関連付け登録に失敗しました。詳細はログを参照してください。",
                    "関連付け", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var msg =
                ".pdj ファイルが PileDesign に関連付けられました (現在のユーザー)。\n\n" +
                "ダブルクリックで PileDesign を既定アプリとして開くには、\n" +
                "Windows の「既定のアプリ」設定で .pdj に対して PileDesign を選択してください。\n\n" +
                "今すぐ設定画面を開きますか？";

            var result = MessageService.Show(msg, "関連付け完了",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (result == MessageBoxResult.Yes)
            {
                PileDesign.Services.FileAssociationService.OpenDefaultAppsSettings();
            }
        }

        [RelayCommand]
        private async Task MoveCopyPiles()
        {
            try
            {
                // 選択節点がない場合は処理を中止してメッセージ表示
                // 杭配置・一般節点・梁要素のいずれかが選択されていればOK
                bool hasPileLayoutSelected = CurrentInputModel?.PileLayoutItems?.Any(p => p.IsSelected) ?? false;
                bool hasGeneralNodesSelected = CurrentInputModel?.InputNodes?.Any(n => n.Type == NodeType.General && n.IsSelected) ?? false;
                bool hasBeamsSelected = CurrentInputModel?.FoundationBeamInput?.Beams?.Any(b => b.IsSelected) ?? false;

                if (!hasPileLayoutSelected && !hasGeneralNodesSelected && !hasBeamsSelected)
                {
                    MessageService.Show("杭配置・一般節点・梁要素のいずれも選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageService.Show($"杭の移動・複製中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveCopyWindow_MoveCopyCompletedAsync(object sender, MoveCopyEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsMove)
            {
                MoveNodes(e.DX, e.DY, e.DZ, e.IsInputNodesIncluded, e.IsPileLayoutIncluded);
                if (e.IsBeamsIncluded) MoveBeams(e.DX, e.DY, e.DZ, EditDistanceThreshold);
            }
            else if (e.IsCopy)
            {
                await CopyNodesAsync(e.DX, e.DY, e.DZ, e.RepetitionNumber, e.IsInputNodesIncluded, e.IsPileLayoutIncluded);
                if (e.IsBeamsIncluded) CopyBeams(e.DX, e.DY, e.DZ, e.RepetitionNumber, EditDistanceThreshold);
            }
        }

        // ───────── 梁要素の移動・コピー (端点ノード解決ロジック付き) ─────────
        // 端点解決の優先順位 (ResolveOrCreateNodeAt):
        //   1. 杭頭節点 (PileLayout の杭頭+ΔZc 位置) との距離 ≤ tolerance → そこを参照
        //   2. 一般節点 (InputNode, Type=General) との距離 ≤ tolerance → そこを参照
        //   3. どちらも見つからなければ新規 InputNode を destination 位置に生成し、それを参照
        //
        // 移動 (Move): 元の梁の NodeI/J 参照を destination の参照に付け替える。
        //   元の端点ノード (FoundationNode / InputNode) はそのまま残す (ユーザー仕様)。
        //   杭頭節点は移動しない (杭自体は元位置のまま)。
        // コピー (Copy): 同じロジックで新規 FoundationBeam を生成して追加。

        private void MoveBeams(double dX, double dY, double dZ, double tolerance)
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;
            var selectedBeams = fb.Beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0) return;

            foreach (var beam in selectedBeams)
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                var destI = new Point3D { X = posI.Value.X + dX, Y = posI.Value.Y + dY, Z = posI.Value.Z + dZ };
                var destJ = new Point3D { X = posJ.Value.X + dX, Y = posJ.Value.Y + dY, Z = posJ.Value.Z + dZ };
                var (typeI, idI) = ResolveOrCreateNodeAt(destI, tolerance);
                var (typeJ, idJ) = ResolveOrCreateNodeAt(destJ, tolerance);

                beam.NodeI_Type = typeI;
                beam.NodeI_Id = idI;
                beam.NodeJ_Type = typeJ;
                beam.NodeJ_Id = idJ;
            }
        }

        private void CopyBeams(double dX, double dY, double dZ, int repetitionNumber, double tolerance)
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;
            var selectedBeams = fb.Beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0) return;

            foreach (var beam in selectedBeams)
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                for (int rep = 1; rep <= repetitionNumber; rep++)
                {
                    var destI = new Point3D { X = posI.Value.X + dX * rep, Y = posI.Value.Y + dY * rep, Z = posI.Value.Z + dZ * rep };
                    var destJ = new Point3D { X = posJ.Value.X + dX * rep, Y = posJ.Value.Y + dY * rep, Z = posJ.Value.Z + dZ * rep };
                    var (typeI, idI) = ResolveOrCreateNodeAt(destI, tolerance);
                    var (typeJ, idJ) = ResolveOrCreateNodeAt(destJ, tolerance);

                    var newBeam = new FoundationBeam
                    {
                        // No プロパティ廃止 (位置 = ID)
                        NodeI_Type = typeI,
                        NodeI_Id = idI,
                        NodeJ_Type = typeJ,
                        NodeJ_Id = idJ,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        SectionName = beam.SectionName,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        AngleBeta = beam.AngleBeta,
                        IsVisible = beam.IsVisible,
                    };
                    fb.Beams.Add(newBeam);
                }
            }
        }

        /// <summary>
        /// 節点参照タイプ + Id から、その節点の実際の取付位置 (3D 座標) を返す。
        /// PileLayout: 接合節点 (X,Y,Z) — v2 セマンティクスでは pile.Z は接合節点 Z
        /// GeneralNode: InputNode の Point3D
        /// FoundationNode: FoundationNode の Point3D
        /// </summary>
        private Point3D? GetNodeAttachPosition(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.PileLayout:
                {
                    var pile = CurrentInputModel?.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    if (pile == null) return null;
                    return new Point3D { X = pile.X, Y = pile.Y, Z = pile.Z };
                }
                case NodeReferenceType.GeneralNode:
                {
                    var node = CurrentInputModel?.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return node?.Point3D;
                }
                case NodeReferenceType.FoundationNode:
                {
                    var fn = CurrentInputModel?.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fn != null ? new Point3D { X = fn.X, Y = fn.Y, Z = fn.Z } : null;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// 梁要素の端点候補となるノードを (Type + Guid + Position) のタプルで列挙する。
        /// 列挙順は ResolveOrCreateNodeAt の優先順位に対応:
        ///   1. PileLayout (接合節点位置 — v2 セマンティクスでは pile.Z 自体)
        ///   2. GeneralNode (InputNode, Type=General)
        ///   3. FoundationNode (基礎梁節点) ※ includeFoundationNodes=true のときのみ
        /// </summary>
        private IEnumerable<(NodeReferenceType Type, Guid Id, Point3D Pos)> EnumerateAllCandidateNodes(
            bool includeFoundationNodes = true)
        {
            if (CurrentInputModel?.PileLayoutItems != null)
            {
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                {
                    yield return (NodeReferenceType.PileLayout, pile.UniqueId,
                        new Point3D(pile.X, pile.Y, pile.Z));
                }
            }
            if (CurrentInputModel?.InputNodes != null)
            {
                foreach (var n in CurrentInputModel.InputNodes)
                {
                    if (n.Type != NodeType.General) continue;
                    yield return (NodeReferenceType.GeneralNode, n.UniqueId, n.Point3D);
                }
            }
            if (includeFoundationNodes && CurrentInputModel?.FoundationBeamInput?.Nodes != null)
            {
                foreach (var fn in CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    yield return (NodeReferenceType.FoundationNode, fn.Id,
                        new Point3D(fn.X, fn.Y, fn.Z));
                }
            }
        }

        /// <summary>
        /// 指定位置にある既存節点を解決、無ければ新規 InputNode (一般節点) を生成して返す。
        /// 優先順位: PileLayout (杭頭+ΔZc) → GeneralNode → 新規 InputNode 生成。
        /// FoundationNode は対象外 (snap 先として基礎梁節点を選ぶのは利用シーンとして想定外のため)。
        /// </summary>
        private (NodeReferenceType type, Guid id) ResolveOrCreateNodeAt(Point3D pos, double tolerance)
        {
            foreach (var (type, id, candPos) in EnumerateAllCandidateNodes(includeFoundationNodes: false))
            {
                if (Distance3D(candPos.X, candPos.Y, candPos.Z, pos.X, pos.Y, pos.Z) <= tolerance)
                    return (type, id);
            }
            // 該当なし → 新規 InputNode を生成
            var newNode = new InputNode
            {
                No = (CurrentInputModel?.InputNodes?.Count ?? 0) + 1,
                Type = NodeType.General,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                IsVisible = true
            };
            if (CurrentInputModel != null)
            {
                CurrentInputModel.InputNodes ??= [];
                CurrentInputModel.InputNodes.Add(newNode);
            }
            return (NodeReferenceType.GeneralNode, newNode.UniqueId);
        }

        private static double Distance3D(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            double dx = x1 - x2, dy = y1 - y2, dz = z1 - z2;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
            ApplyCurrentUndoSnapshot();
        }

        /// <summary>
        /// UndoManager の CurrentState を取り込んで CurrentInputModel を再構築する。
        /// Undo / Redo 共通処理 + D.16 HistoryPanel からの任意ジャンプにも使う。
        /// </summary>
        public void ApplyCurrentUndoSnapshot()
        {
            if (_undoManager.CurrentState is InputModel state)
            {
                CurrentInputModel = state.DeepCopy();
                CurrentInputModel.AttachViewModel(this);

                NotifyUIChanged(immediate: true);
                OnPropertyChanged(nameof(CurrentInputModel));

                // Undo/Redo 後に SelectedItemProperties が古いインスタンスを参照したままに
                // ならないよう、現在の選択状態に基づいてプロパティパネルを再構築する。
                // (DeepCopy で杭/梁等のインスタンスが入れ替わるため必須)
                UpdatePropertyPanel();
            }
            RaiseUndoStateChanged();
        }

        /// <summary>D.16 HistoryPanel が UndoManager 参照を取得するためのアクセサ。</summary>
        public Common.Undo.UndoManager UndoManager => _undoManager;

        /// <summary>基礎梁コレクション変更時のハンドラ (基礎梁考慮沈下解析ボタン活性化条件・荷重タイプ ComboBox の再評価)。</summary>
        private void FoundationBeams_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            OpenGroupSettlementWithBeamWindowCommand?.NotifyCanExecuteChanged();
            // 個別矩形（基礎梁考慮）の表示可否は基礎梁の有無に連動するため再評価
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            OnPropertyChanged(nameof(GroupSettlementBeamSelectorOptions));
            OnPropertyChanged(nameof(GroupSettlementBeamSelector));
            OnPropertyChanged(nameof(GroupSettlementLoadTypeOptions));

            // 追加・削除された梁要素の PropertyChanged 購読を更新
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is Models.InputData.FoundationBeam b)
                        b.PropertyChanged -= FoundationBeam_PropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is Models.InputData.FoundationBeam b)
                    {
                        b.PropertyChanged -= FoundationBeam_PropertyChanged;
                        b.PropertyChanged += FoundationBeam_PropertyChanged;
                    }
                }
            }
            // Reset の場合は全要素を再購読
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                && CurrentInputModel?.FoundationBeamInput?.Beams is { } resetBeams)
            {
                foreach (var beam in resetBeams)
                {
                    beam.PropertyChanged -= FoundationBeam_PropertyChanged;
                    beam.PropertyChanged += FoundationBeam_PropertyChanged;
                }
            }

            // 基礎梁の変更で 反復解析結果が無効になるため自動破棄 + トースト通知
            // (FoundationBeamWindow 編集中は CollectionChanged が頻繁に発火するため
            //  ダイアログでなくトーストで通知)
            if (HasGroupSettlementBeamAwareCases)
            {
                InvalidateBeamAwareResultsSilently("基礎梁の変更により、土層沈下解析（反復）の結果を破棄しました。");
            }
        }

        /// <summary>
        /// 基礎梁の個別プロパティ (β/Width/NodeI_Id 等) 変更時に
        /// 反復解析結果を破棄して通知する。
        /// 解析後に書き込まれる結果プロパティ (MemberAngle 等) は無視。
        /// </summary>
        private void FoundationBeam_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 解析結果として書き込まれるプロパティは無視 (解析自身が結果を破棄しないため)
            if (e.PropertyName == nameof(Models.InputData.FoundationBeam.MemberAngle))
                return;
            // ComboBox SelectedValuePath 用の派生キーは Type/Id 変更時にも別途発火するためスキップ
            if (e.PropertyName == nameof(Models.InputData.FoundationBeam.NodeI_Key)
                || e.PropertyName == nameof(Models.InputData.FoundationBeam.NodeJ_Key))
                return;

            if (HasGroupSettlementBeamAwareCases)
            {
                InvalidateBeamAwareResultsSilently("基礎梁の変更により、土層沈下解析（反復）の結果を破棄しました。");
            }
        }

        /// <summary>反復解析結果を確認なしで破棄 (杭軸力・基礎梁等の編集連動用)。</summary>
        private void InvalidateBeamAwareResultsSilently(string toastMessage)
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return;
            var doomed = pgs.CaseRecords.Where(r => r.IsBeamAware).ToList();
            if (doomed.Count == 0) return;
            foreach (var r in doomed) pgs.CaseRecords.Remove(r);

            // ActiveCase が無効なら -1
            if (pgs.ActiveLoadingType == "個別矩形（基礎梁考慮）")
            {
                pgs.ActiveCaseIndex = -1;
                pgs.SettlementGridData = [];
                if (CurrentInputModel?.PileLayoutItems != null)
                    foreach (var pile in CurrentInputModel.PileLayoutItems) pile.GroupPileSettlement = 0;
            }
            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            OnPropertyChanged(nameof(HasAnyAnalysisResult));
            OnPropertyChanged(nameof(AnalysisStatusText));
            OnPropertyChanged(nameof(AnalysisStatusItems));
            UpdateCanvas3DAction?.Invoke();
            ShowToast(toastMessage);
        }

        /// <summary>FoundationBeamInput の Beams プロパティ自体が置換された場合の再購読。</summary>
        private void FoundationBeamInput_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Models.InputData.FoundationBeamInput.Beams)) return;
            if (CurrentInputModel?.FoundationBeamInput?.Beams is { } beams)
            {
                beams.CollectionChanged -= FoundationBeams_CollectionChanged;
                beams.CollectionChanged += FoundationBeams_CollectionChanged;
                // 新コレクション内の各梁要素の PropertyChanged も再購読
                foreach (var beam in beams)
                {
                    beam.PropertyChanged -= FoundationBeam_PropertyChanged;
                    beam.PropertyChanged += FoundationBeam_PropertyChanged;
                }
            }
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            OpenGroupSettlementWithBeamWindowCommand?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            // 群杭荷重の「基礎梁:有/無」セレクタも基礎梁有無で内容が変わるため再評価
            OnPropertyChanged(nameof(GroupSettlementBeamSelectorOptions));
            OnPropertyChanged(nameof(GroupSettlementBeamSelector));
            OnPropertyChanged(nameof(GroupSettlementLoadTypeOptions));
        }

        [RelayCommand]
        private void Redo()
        {
            _undoManager.RedoSnapshot();
            ApplyCurrentUndoSnapshot();
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
                catch (InvalidOperationException) { }
            }

            window.ShowDialog();

            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
        }

        // 基本設定ウィンドウを開くメソッド
        [RelayCommand]
        private void OpenFundamentalWindow()
        {
            OpenDialogWindowWithUndo<FundamentalViewModel, FundamentalWindow>(undoDescription: "基本設定 編集");
        }

        // 荷重条件ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenLoadCaseWindow()
        {
            OpenDialogWindowWithUndo<LoadCaseViewModel, LoadCaseWindow>(() =>
            {
                UpdateLoadCaseOption();
                UpdateLoadCombinationOption();
            }, undoDescription: "荷重ケース 編集");
        }

        // 地盤ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenGroundWindow()
        {
            // ダイアログ前後で InputModel.GroundsInput のインスタンス列が変わったか判定する。
            // OnSave: Clear+Add で全インスタンスが入れ替わる → 変更ありと判定
            // OnCancel: 何もしない (インスタンス据え置き) → 変更なしと判定
            // 変更がない場合は IsElementSplit を保持し、SoilPiles 再生成も省略する。
            var prevInstances = CurrentInputModel?.GroundsInput?.ToArray() ?? Array.Empty<GroundInput>();

            OpenDialogWindowWithUndo<GroundLayerViewModel, GroundWindow>(() =>
            {
                var nowInstances = CurrentInputModel?.GroundsInput?.ToArray() ?? Array.Empty<GroundInput>();
                bool changed = nowInstances.Length != prevInstances.Length
                    || !nowInstances.Zip(prevInstances).All(p => ReferenceEquals(p.First, p.Second));
                if (!changed) return;

                // 地盤変更後は杭要素分割を再生成（地層境界の節点追加が必要）
                IsElementSplit = false;
                RequestGenerateSoilPiles();
            }, undoDescription: "地盤 編集");
        }

        // 基礎梁考慮 群杭沈下解析ウィンドウを開く (個別矩形（基礎梁考慮） モード用)
        [RelayCommand(CanExecute = nameof(CanOpenGroupSettlementWithBeamWindow))]
        public void OpenGroupSettlementWithBeamWindow()
        {
            // 反復解析の荷重面標高を採用 (per-route フィールドから現在値にコピー)
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs != null && !double.IsNaN(pgs.LoadingPlaneAltitudeBeamAware))
                pgs.LoadingPlaneAltitude = pgs.LoadingPlaneAltitudeBeamAware;

            // 一般モード (反復以外) で表示中の RectLoads をスナップショット。
            // 反復解析後に pgs.RectLoads は収束反力で上書きされるため、
            // 一般モードに戻った際に復元できるよう事前に保存する。
            // 既に反復モードを表示中だった場合 (再実行) はスナップショットを更新しない (既存スナップが原入力)。
            if (pgs != null
                && pgs.ActiveLoadingType != "個別矩形（基礎梁考慮）"
                && pgs.RectLoads != null
                && pgs.RectLoads.Count > 0)
            {
                pgs.NonBeamRectLoadsSnapshot = new System.Collections.ObjectModel.ObservableCollection<Models.InputData.RectLoad>(
                    pgs.RectLoads.Select(r => new Models.InputData.RectLoad
                    {
                        X1 = r.X1, X2 = r.X2, Y1 = r.Y1, Y2 = r.Y2,
                        QA = r.QA, LinkedPileNo = r.LinkedPileNo,
                    }));
            }

            var vm = new GroupSettlementWithBeamCalculationViewModel(this);
            var win = new GroupSettlementWithBeamWindow
            {
                DataContext = vm,
                Owner = Application.Current?.MainWindow
            };
            win.ShowDialog();
            // 保存して閉じた場合は VM 内で InputModel.PileGroupSettlement.CaseRecords が更新済み
            // ケース選択 ComboBox とバッジを再評価するため PropertyChanged を発火
            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(HasAnyAnalysisResult));
            OnPropertyChanged(nameof(AnalysisStatusText));
            OnPropertyChanged(nameof(AnalysisStatusItems));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(SelectedActiveLoadingType));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));

            // 反復解析が保存された場合、グリッド変位の自動表示のみ ON にする
            // (IsGroupPileSettlementAnalysisDone は一回解析専用フラグなので触らない。
            //  グリッド変位ボタンの IsEnabled は HasGroupSettlementCaseRecords にバインド)
            if (HasGroupSettlementBeamAwareCases)
            {
                IsGroupPileGridDeformationVisible = true;
                // 現在の SelectedLoadCaseName と保存ケース名が一致しなければコンタを表示しない
                // (例: 反復は VL ケースのみ保存、ユーザーは VL+E1 を選択中 → コンタは VL を選んだ時に表示)
                SyncGroupSettlementActiveCaseFromLoadCase(SelectedLoadCaseName);
            }
            // 沈下応力 が 解析結果 内容オプションに現れるよう再評価
            UpdateSettlementCategoriesPublic();
            // ログ・テーブル・グラフ・検定ウィンドウのコマンド有効状態を更新
            (OpenLogWindowCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
            (OpenTableWindowCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
            (OpenGraphWindowCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
            (OpenEvaluationWindowCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 基礎梁考慮群杭沈下解析ウィンドウを開けるか。
        /// 基礎梁が 1 件以上定義されていることが条件 (基礎梁が無いと反復解析が成立しない)。
        /// </summary>
        private bool CanOpenGroupSettlementWithBeamWindow()
            => (CurrentInputModel?.FoundationBeamInput?.Beams?.Count ?? 0) > 0;

        /// <summary>
        /// 旧ファイル互換マイグレーション:
        /// (1) "個別十字（基礎梁考慮）" → "個別十字（基礎梁反力）" の名称変更
        /// (2) CaseRecord.LoadingType が空文字のレコードを IsBeamAware から推定して補完
        /// (3) ActiveLoadingType が空ならアクティブレコード or 先頭レコードから推定
        /// (4) LoadingPlaneAltitudeNonBeam / BeamAware が NaN (新フィールド未設定) なら旧 LoadingPlaneAltitude をコピー
        /// </summary>
        private static void MigrateCaseRecordLoadingType(PileGroupSettlement pgs)
        {
            if (pgs == null) return;

            // (1) 名称変更マイグレーション
            const string oldName = "個別十字（基礎梁考慮）";
            const string newName = "個別十字（基礎梁反力）";
            if (pgs.LoadingType == oldName) pgs.LoadingType = newName;
            if (pgs.ActiveLoadingType == oldName) pgs.ActiveLoadingType = newName;

            // (4) 荷重面標高の per-route フィールド初期化 (旧データ互換)
            if (double.IsNaN(pgs.LoadingPlaneAltitudeNonBeam))
                pgs.LoadingPlaneAltitudeNonBeam = pgs.LoadingPlaneAltitude;
            if (double.IsNaN(pgs.LoadingPlaneAltitudeBeamAware))
                pgs.LoadingPlaneAltitudeBeamAware = pgs.LoadingPlaneAltitude;

            if (pgs.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

            string fallback = string.IsNullOrEmpty(pgs.LoadingType) ? "任意矩形" : pgs.LoadingType;
            foreach (var rec in pgs.CaseRecords)
            {
                if (rec.LoadingType == oldName) rec.LoadingType = newName;
                if (string.IsNullOrEmpty(rec.LoadingType))
                {
                    rec.LoadingType = rec.IsBeamAware ? "個別矩形（基礎梁考慮）" : fallback;
                }
            }

            // (3) ActiveLoadingType の推定
            if (string.IsNullOrEmpty(pgs.ActiveLoadingType))
            {
                int idx = pgs.ActiveCaseIndex;
                if (idx >= 0 && idx < pgs.CaseRecords.Count)
                    pgs.ActiveLoadingType = pgs.CaseRecords[idx].LoadingType;
                else
                    pgs.ActiveLoadingType = pgs.CaseRecords[0].LoadingType;
            }
        }

        /// <summary>
        /// 基礎梁考慮以外の解析タイプの結果を CaseRecord として永続化する。
        /// 同じ LoadingType の既存レコードは置換、他タイプのレコードは保持。
        /// 単杭沈下・矩形荷重・グリッドコンタを 1 レコードにまとめ、ActiveLoadingType を更新する。
        /// </summary>
        private void UpsertNonBeamAwareCaseRecord(string loadingType,
            ObservableCollection<SettlementGridDataItem> gridData)
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs == null) return;

            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = loadingType,
                IsBeamAware = false,
                IsConverged = true,
                IterationCount = 0,
                FinalResidual = 0.0,
                RectLoads = new ObservableCollection<RectLoad>(
                    pgs.RectLoads?.Select(r => new RectLoad
                    {
                        X1 = r.X1, X2 = r.X2, Y1 = r.Y1, Y2 = r.Y2,
                        QA = r.QA, LinkedPileNo = r.LinkedPileNo,
                    }) ?? []),
                SettlementGridData = new ObservableCollection<SettlementGridDataItem>(gridData ?? []),
                PileSettlements_mm = CurrentInputModel.PileLayoutItems?
                    .ToDictionary(p => p.PileNo, p => p.GroupPileSettlement) ?? [],
            };

            // 2 スロットモデル: 基礎梁無しスロットの既存 record (= IsBeamAware=false) を全削除し、
            // 今回の 1 件で置換。基礎梁有りスロット (IsBeamAware=true) は保持。
            if (pgs.CaseRecords == null)
                pgs.CaseRecords = [];
            for (int i = pgs.CaseRecords.Count - 1; i >= 0; i--)
            {
                if (!pgs.CaseRecords[i].IsBeamAware)
                    pgs.CaseRecords.RemoveAt(i);
            }
            pgs.CaseRecords.Add(record);

            // ActiveLoadingType を今回解析したタイプに切替 (アクティブケースもこの 1 件)
            pgs.ActiveLoadingType = loadingType;
            pgs.ActiveCaseIndex = pgs.CaseRecords.IndexOf(record);

            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(SelectedActiveLoadingType));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            OnPropertyChanged(nameof(HasAnyAnalysisResult));
            OnPropertyChanged(nameof(AnalysisStatusText));
            OnPropertyChanged(nameof(AnalysisStatusItems));
        }

        /// <summary>UpdateSettlementCategories を外部から呼ぶための薄いラッパ。</summary>
        public void UpdateSettlementCategoriesPublic()
        {
            // 既存の private 実装を呼びたいが、partial class なのでここからリフレクションなしで呼べる
            var method = typeof(MainWindowViewModel).GetMethod("UpdateSettlementCategories",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(this, null);
        }

        // 基礎梁ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenFoundationBeamWindow()
        {
            OpenDialogWindowWithUndo<FoundationBeamViewModel, FoundationBeamWindow>(() =>
            {
                // ダイアログ確定/破棄後に基礎梁有無に応じて荷重タイプ ComboBox を再評価
                // (FoundationBeamInput / Beams 置換が PropertyChanged 経路で取りこぼされた場合の保険)
                if (CurrentInputModel?.FoundationBeamInput is { } fbInput)
                {
                    // 新インスタンス参照に再購読
                    fbInput.PropertyChanged -= FoundationBeamInput_PropertyChanged;
                    fbInput.PropertyChanged += FoundationBeamInput_PropertyChanged;
                    if (fbInput.Beams is { } beams)
                    {
                        beams.CollectionChanged -= FoundationBeams_CollectionChanged;
                        beams.CollectionChanged += FoundationBeams_CollectionChanged;
                    }
                }
                OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            }, undoDescription: "基礎梁 編集");
        }

        // 杭体ウィンドウを開くメソッド
        [RelayCommand]
        public void OpenPileBodyWindow()
        {
            // ダイアログ前後で InputModel.PileBodies のインスタンス (コレクション参照) が変わったか判定する。
            // OnOk: 新しい PileBodies に差し替える → 変更ありと判定
            // OnCancel: 何もしない (参照据え置き) → 変更なしと判定
            // 変更がない場合は IsElementSplit を保持し、SoilPiles 再生成も省略する。
            var prevReference = CurrentInputModel?.PileBodies;

            OpenDialogWindowWithUndo<PileBodyViewModel, PileBodyWindow>(() =>
            {
                bool changed = !ReferenceEquals(CurrentInputModel?.PileBodies, prevReference);
                if (!changed) return;

                // 杭体変更後は杭要素分割を再生成（地層境界の節点追加が必要）
                IsElementSplit = false;
                RequestGenerateSoilPiles();
            }, undoDescription: "杭体 編集");
        }

        // 軸力チェック
        [RelayCommand]
        public void OnAxialForceCheck()
        {
            if (CurrentInputModel == null || CurrentInputModel.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show("杭配置が存在しません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
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

                    // 使用限界軸力チェック
                    // N-M曲線を1回取得して内部プロパティを初期化（ServiceLimitNMax等が転送される）
                    _ = pileSection.FactoredServiceNM;
                    double nMax = pileSection.ServiceLimitNMax;
                    double nMin = pileSection.ServiceLimitNMin;
                    // ServiceLimitNMax/NMinが0の場合はFactoredServiceNMax/NMinにフォールバック
                    if (nMax == 0 && nMin == 0)
                    {
                        nMax = pileSection.FactoredServiceNMax;
                        nMin = pileSection.FactoredServiceNMin;
                    }

                    if (nMax < force)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n 使用限界軸力適用範囲Max{nMax:N0}kN < {force:N0}kN\n";
                    }
                    if (force < nMin)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n {force:N0}kN < 使用限界軸力適用範囲Min{nMin:N0}kN\n";
                    }
                }
            }

            if (hasWarning)
                MessageService.Show(warningMessage, "警告", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                ShowToast("各杭配置の軸力は各断面の軸力適用範囲内です。");
        }

        // 杭要素分割ウィンドウの再入ガード
        // F4 連打や Ctrl+D 連打で await Task.Run(DeepCopy) 中に次の呼出しが入ると、
        // 1 本目の ShowDialog 終了後に 2 本目が開いてしまうのを防ぐ
        private bool _isElementDivisionWindowOpening;

        // 杭要素分割ウィンドウを開くメソッド
        [RelayCommand]
        public async Task OpenElementDivisionWindowAsync()
        {
            if (_isElementDivisionWindowOpening) return;
            _isElementDivisionWindowOpening = true;
            try
            {
                if (IsPreparedForAnalysis())
                {
                    // 解析結果が存在する場合、削除確認ダイアログを表示
                    if (!CheckAndResetAnalysisResults()) return;

                    // 杭下端より下方に土層・土質点が存在するかチェック
                    var validationError = ValidatePileAndGroundDepth();
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        MessageService.Show(validationError, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Undo用DeepCopyをダイアログ表示前に完了させる
                    // （DeepCopy内のSoilPiles一時退避finallyがダイアログ中のSoilPiles変更と競合するのを防ぐ）
                    var undoCopy = await Task.Run(() => CurrentInputModel.DeepCopy());

                    // ウィンドウを即座に表示（重い初期化はContentRenderedイベントで実行）
                    var window = new ElementDivisionWindow(this);
                    window.ShowDialog();
                    if (undoCopy != null)
                    {
                        _undoManager.SaveState(undoCopy, "杭要素分割");
                        RaiseUndoStateChanged();
                    }

                    UpdateWindowImmediate();
                }
            }
            finally
            {
                _isElementDivisionWindowOpening = false;
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

                // 杭下端標高を計算 (v2 セマンティクス: pile.Z は接合節点 Z なので、杭頭は PileHeadZ)
                double pileTopAltitude = pileLayout.PileHeadZ;
                double pileLength = pileBody.PileBodySegments.Sum(seg => seg.SegmentLength);
                double pileBottomAltitude = pileTopAltitude - pileLength;

                // 土層の最下層標高をチェック
                if (groundInput.GroundLayers != null && groundInput.GroundLayers.Count > 0)
                {
                    double groundBottomAltitude = groundInput.GroundLayers.Min(layer => layer.BottomAltitude);
                    double groundTopAltitude = groundInput.GroundLayers.Max(layer => layer.BottomAltitude + layer.LayerThickness);

                    if (pileBottomAltitude < groundBottomAltitude)
                    {
                        errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 杭下端Z({pileBottomAltitude:F2}m)が土層の最下層Z({groundBottomAltitude:F2}m)より下にあります。");
                    }
                    // 杭頭が地盤上端より上に浮いているケース（水平土ばねが効かなくなる）
                    if (pileTopAltitude > groundTopAltitude)
                    {
                        errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 杭頭Z({pileTopAltitude:F2}m)が土層の最上層Z({groundTopAltitude:F2}m)より上にあります。");
                    }
                    // 完全に交差しない場合（杭が地盤の完全に上 or 下）
                    if (pileBottomAltitude >= groundTopAltitude || pileTopAltitude <= groundBottomAltitude)
                    {
                        errors.AppendLine($"杭配置No.{pileLayout.PileNo}: 杭 Z 範囲 [{pileBottomAltitude:F2}〜{pileTopAltitude:F2}] が地盤 Z 範囲 [{groundBottomAltitude:F2}〜{groundTopAltitude:F2}] と全く重なっていません。基本設定の「Z=0 の標高」を確認してください。");
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

            // 根入部の地盤カバーチェック
            var embedment = CurrentInputModel.EmbedmentInput;
            if (embedment?.EmbedmentLayers != null && embedment.EmbedmentLayers.Count > 0)
            {
                int embGroundNo = embedment.GroundNo;
                if (embGroundNo < 1 || embGroundNo > CurrentInputModel.GroundsInput.Count)
                {
                    errors.AppendLine($"根入部: 地盤番号{embGroundNo}が存在しません。");
                }
                else
                {
                    var embGround = CurrentInputModel.GroundsInput[embGroundNo - 1];
                    double embTop = embedment.EmbedmentLayers[0].TopAltitude;
                    double embBottom = embedment.EmbedmentLayers[^1].BottomAltitude;

                    if (embGround.GroundLayers != null && embGround.GroundLayers.Count > 0)
                    {
                        double groundTop = embGround.GroundLayers.Max(layer => layer.BottomAltitude + layer.LayerThickness);
                        double groundBottom = embGround.GroundLayers.Min(layer => layer.BottomAltitude);

                        if (embTop > groundTop)
                        {
                            errors.AppendLine($"根入部: 根入上端標高({embTop:F2}m)が地盤No.{embGroundNo}の最上層標高({groundTop:F2}m)より上にあります。");
                        }
                        if (embBottom < groundBottom)
                        {
                            errors.AppendLine($"根入部: 根入下端標高({embBottom:F2}m)が地盤No.{embGroundNo}の最下層標高({groundBottom:F2}m)より下にあります。");
                        }
                    }
                    else
                    {
                        errors.AppendLine($"根入部: 地盤No.{embGroundNo}に土層データがありません。");
                    }
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
                    MessageService.Show("杭配置が存在しません。");
                    return;
                }
                else
                {
                    if (IsElementSplit == false)
                        PileDesign.Services.MessageService.Show("杭要素分割を行ってください。");
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
                    MessageService.Show("杭配置が存在しません。");
                    return;
                }
                else
                {
                    if (IsElementSplit == false)
                    {
                        PileDesign.Services.MessageService.Show("杭要素分割を行ってください。");
                    }
                    else
                    {
                        try
                        {
                            // 杭配置番号を確実に同期
                            UpdatePileLayoutNo();

                            // 引張定着筋なしの半剛接合 (キャプテン/F.T.Pile/キャプリング) で
                            // 入力軸力が引張となっている杭がある場合は警告ダイアログを表示
                            WarnTensionInputAxialForSemiRigidPiles();

                            var viewModel = new HorizontalCalculationViewModel(this);
                            var window = new HorizontalCalculationWindow { DataContext = viewModel };

                            if (viewModel is ICloseable closeableViewModel)
                            {
                                if (window.IsLoaded && window.IsVisible)
                                    window.Close();
                            }

                            // ウィンドウを即座に表示（FEMモデル作成はLoadedイベントでバックグラウンド実行）
                            window.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageService.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            // 念のため砂時計を戻す
                            Mouse.OverrideCursor = null;
                        }

                        // 解析結果がある場合、最初の解析ケースを選択
                        SelectFirstAnalysisResult();

                        // 変更: 即時実行
                        UpdateWindowImmediate();

                    }
                }
            }
        }

        /// <summary>
        /// 引張定着筋なしの杭頭半剛接合工法 (キャプテン/F.T.Pile/キャプリング) を採用する杭で、
        /// 入力軸力 (常時/L1/L2) が引張となっているケースがあればまとめて警告ダイアログを表示する。
        /// 該当する場合 M-θ の最大抵抗モーメント Mu = 0 となり、杭頭は事実上ピン接合扱いになるため、
        /// 解析を続行する前にユーザに気付かせる。
        /// </summary>
        private void WarnTensionInputAxialForSemiRigidPiles()
        {
            if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileBodies == null) return;
            var warnings = new List<string>();
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                int idx = pile.PileBodyNo - 1;
                if (idx < 0 || idx >= CurrentInputModel.PileBodies.Count) continue;
                var pileBody = CurrentInputModel.PileBodies[idx];
                var typeName = ViewModels.HorizontalCalculationViewModel.GetSemiRigidWithoutTensionBarPileTopName(pileBody);
                if (typeName == null) continue;

                // 入力軸力 (kN) — 圧縮を正、引張を負とする符号規約
                var tensileCases = new List<string>();
                if (pile.AxialForceVL < 0)
                    tensileCases.Add($"常時 ({pile.AxialForceVL:N1}kN)");
                if (pile.AxialForceLevel1s != null)
                {
                    for (int i = 0; i < pile.AxialForceLevel1s.Count; i++)
                        if (pile.AxialForceLevel1s[i] < 0)
                            tensileCases.Add($"L1-{i + 1} ({pile.AxialForceLevel1s[i]:N1}kN)");
                }
                if (pile.AxialForceLevel2s != null)
                {
                    for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
                        if (pile.AxialForceLevel2s[i] < 0)
                            tensileCases.Add($"L2-{i + 1} ({pile.AxialForceLevel2s[i]:N1}kN)");
                }
                if (tensileCases.Count > 0)
                {
                    warnings.Add($"  ・杭No.{pile.No} ({typeName}): {string.Join(", ", tensileCases)}");
                }
            }
            if (warnings.Count == 0) return;

            var msg =
                "以下の杭で『引張定着筋なし』の杭頭半剛接合工法を採用していますが、入力軸力が引張となっています。\n" +
                "この場合、解析中は当該杭の杭頭を「軸剛性 0、曲げ剛性 0」(Uz 並進 master-slave 解放 + Mu=0) " +
                "として扱います (詳細はヘルプ「引張軸力時の杭頭軸剛性解放」参照)。\n\n" +
                string.Join("\n", warnings) +
                "\n\n対応案:\n" +
                "  ① 引張定着筋を設置する\n" +
                "  ② 入力軸力を見直す\n" +
                "  ③ 杭頭工法を変更する";
            PileDesign.Services.MessageService.Show(
                msg, "杭頭半剛接合 軸力チェック",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>
        /// 解析結果の最初のケースの荷重ケース・荷重組合せ・液状化状態を選択する
        /// </summary>
        private void SelectFirstAnalysisResult()
        {
            var firstResult = CurrentModel?.AnalysisStepResults?.FirstOrDefault();
            if (firstResult == null) return;

            // 荷重ケースを選択
            if (firstResult.LoadCase != null)
            {
                SelectedLoadCaseName = firstResult.LoadCase.LoadName;
            }

            // 荷重組合せを選択
            if (firstResult.LoadCombination != null)
            {
                SelectedLoadCombinationName = firstResult.LoadCombination.GetName();
            }

            // 液状化状態を選択
            IsLiquefaction = firstResult.IsLiquefaction;
        }

        /// <summary>
        /// 基礎梁考慮沈下解析ボタンの活性条件:
        ///   - 杭が 1 本以上配置されている
        ///   - 基礎梁が 1 件以上定義されている
        ///   - 単杭沈下解析が完了している (各杭の LoadDisplacements が計算済み)
        /// いずれか満たさない間 UI 上はボタン灰色化 (D.13 の一環)。
        /// </summary>
        public bool CanOpenVerticalBeamCalculation()
        {
            if (CurrentInputModel?.PileLayoutItems is not { Count: > 0 }) return false;
            if (CurrentInputModel.FoundationBeamInput?.Beams is not { Count: > 0 }) return false;

            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return false;
            foreach (var sp in soilPiles)
            {
                if (sp.LoadDisplacements == null || sp.LoadDisplacements.Count == 0) return false;
            }
            return true;
        }

        // 基礎梁鉛直解析ウィンドウを開くメソッド
        [RelayCommand(CanExecute = nameof(CanOpenVerticalBeamCalculation))]
        public void OpenVerticalBeamCalculation()
        {
            // バリデーション
            if (CurrentInputModel.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show("杭配置が定義されていません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CurrentInputModel.FoundationBeamInput?.Beams == null || CurrentInputModel.FoundationBeamInput.Beams.Count == 0)
            {
                MessageService.Show("基礎梁が定義されていません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // LoadDisplacementsが計算済みか確認（ElementDivision.SoilPilesから直接チェック）
            bool anyMissing = false;
            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0)
            {
                anyMissing = true;
            }
            else
            {
                foreach (var sp in soilPiles)
                {
                    if (sp.LoadDisplacements == null || sp.LoadDisplacements.Count == 0)
                    {
                        anyMissing = true;
                        break;
                    }
                }
            }
            if (anyMissing)
            {
                MessageService.Show("単杭沈下解析が未実行の杭があります。\n\n" +
                    "基礎梁考慮沈下解析には、各杭の荷重-沈下関係（単杭沈下解析の結果）が必要です。\n" +
                    "先に「単杭沈下解析」を実行してください。\n\n" +
                    "※群杭沈下解析とは別の解析です。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 杭配置番号を確実に同期
                UpdatePileLayoutNo();

                var viewModel = new VerticalBeamCalculationViewModel(this);
                var window = new Views.VerticalBeamCalculationWindow { DataContext = viewModel };
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();

                // 解析結果をメイン画面に転送（保存して閉じた場合のみ）
                if (viewModel.IsSaved && viewModel.CaseResults.Count > 0)
                {
                    VerticalBeamCaseResults = new ObservableCollection<FEM.VerticalBeamCaseResult>(viewModel.CaseResults);
                    IsVerticalBeamAnalysisDone = true;

                    // 計算ログを別途保存
                    SetVerticalBeamAnalysisLogs(viewModel.CalculationLog);
                    RaiseResultCommandsCanExecute();

                    UpdateWindowImmediate();
                }
            }
            catch (Exception ex)
            {
                MessageService.Show($"ダイアログの表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 解析準備ができているかを確認するメソッド
        private bool IsPreparedForAnalysis()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                PileDesign.Services.MessageService.Show("杭配置が存在しません。");
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
                MessageService.Show("地盤データが存在しません。");
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
                        Ek = layer.Es0,                  // 初期変形係数 Es0 = 2(1+νs)×Gs0
                        PoissonsRatio = layer.PoissonsRatio,
                        Thickness = 0,
                        Note = BuildSoilLayerNote(layer),
                        GranularityClass = layer.GranularityClass ?? ""  // 土層分類を引き継ぐ
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

        // 備考文字列: 元地盤層の Es0 と νs を記載
        private static string BuildSoilLayerNote(GroundLayerInput layer)
        {
            var parts = new List<string>(2);
            if (layer.Es0 > 0) parts.Add($"Es0={layer.Es0:N0} kN/m²");
            if (layer.PoissonsRatio > 0) parts.Add($"νs={layer.PoissonsRatio:N2}");
            return string.Join(" / ", parts);
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
                catch (InvalidOperationException) { }
            }

            window.ShowDialog();

            UpdateSumAndOTM();
            // 変更: 即時実行
            UpdateWindowImmediate();
        }

        // AutoActionPointXYCommand - 作用点XY自動設定
        [RelayCommand]
        private void AutoActionPointXY()
        {
            // 作用点を杭配置の重心に移動
            OnMoveForceActionPointToAverageCenter();
        }

        /// <summary>
        /// 地震時軸力の入力/表示モード: false = 絶対 (VL + 変動 を直接編集)、true = 変動 (= 絶対 − VL)。
        /// ファイル別に永続化 (InputModel.IsAxialForceVariationMode) し、PileLayoutDataItem の setter は
        /// 静的フラグ AxialForceModeContext.IsVariationMode を参照する (両者を同時に更新)。
        ///
        /// 切替時のデータ変化: なし (絶対値は不変、変動値は派生値として表示するだけ)。
        /// 切替後の編集動作:
        ///   - 絶対モード: 絶対値を直接編集、VL 変更時は変動が再計算される (絶対は不変)
        ///   - 変動モード: 変動値を編集すると絶対値が自動更新、VL 変更時は絶対値が Δ VL シフト (変動は不変)
        /// </summary>
        public bool IsAxialForceVariationMode
        {
            get => CurrentInputModel?.IsAxialForceVariationMode ?? false;
            set
            {
                if (CurrentInputModel == null) return;
                if (CurrentInputModel.IsAxialForceVariationMode == value) return;
                CurrentInputModel.IsAxialForceVariationMode = value;
                Common.AxialForceModeContext.IsVariationMode = value;
                OnPropertyChanged();
                // プロパティパネルの軸力ラベル/値表記が絶対⇔変動で切り替わるため再構築
                UpdatePropertyPanel();
            }
        }

        /// <summary>
        /// 全杭の L1/L2 軸力 (AxialForceLevel1s / AxialForceLevel2s) に各杭の VL (VL0 + VLadd) を加算する。
        /// 入力データが「地震時 ΔN」(増分) の場合に「VL + 地震時」(全軸力) へ変換するためのユーティリティ。
        /// </summary>
        [RelayCommand]
        private void AddVLToL1L2AxialForce()
        {
            if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                return;
            if (!CheckAndResetAnalysisResults()) return;

            var result = MessageService.Show(
                "全杭の L1/L2 軸力に VL (常時軸力) を加算します。\n" +
                "現在値が「地震時増分 ΔN」のときに「VL + 地震時 = 全軸力」へ変換するために使用します。\n\n" +
                "実行してよろしいですか?\n" +
                "(元に戻すには Undo (Ctrl+Z) または「VL を減算」ボタンを使用してください)",
                "L1/L2 軸力に VL を加算", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            SaveUndoState();
            ApplyVLOffsetToL1L2(+1.0);
            UpdateSumAndOTM();
            RequestUpdateWindow();
        }

        /// <summary>
        /// 全杭の L1/L2 軸力 (AxialForceLevel1s / AxialForceLevel2s) から各杭の VL (VL0 + VLadd) を減算する。
        /// 「VL を加算」を誤適用した場合の取り消しや、「VL + 地震時」を「地震時 ΔN」へ戻す変換に使用。
        /// </summary>
        [RelayCommand]
        private void SubtractVLFromL1L2AxialForce()
        {
            if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                return;
            if (!CheckAndResetAnalysisResults()) return;

            var result = MessageService.Show(
                "全杭の L1/L2 軸力から VL (常時軸力) を減算します。\n" +
                "現在値が「VL + 地震時 = 全軸力」のときに「地震時増分 ΔN」へ変換するために使用します。\n\n" +
                "実行してよろしいですか?",
                "L1/L2 軸力から VL を減算", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            SaveUndoState();
            ApplyVLOffsetToL1L2(-1.0);
            UpdateSumAndOTM();
            RequestUpdateWindow();
        }

        /// <summary>
        /// 全杭の L1/L2 軸力配列に sign × AxialForceVL を加算する内部ヘルパ。
        /// sign=+1 で加算、-1 で減算。
        /// </summary>
        private void ApplyVLOffsetToL1L2(double sign)
        {
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                double vl = pile.AxialForceVL;
                if (pile.AxialForceLevel1s != null)
                {
                    for (int i = 0; i < pile.AxialForceLevel1s.Count; i++)
                        pile.AxialForceLevel1s[i] += sign * vl;
                }
                if (pile.AxialForceLevel2s != null)
                {
                    for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
                        pile.AxialForceLevel2s[i] += sign * vl;
                }
            }
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

            // 削除対象の杭に接合された梁要素も同時に削除
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams != null)
            {
                var pileIds = new HashSet<Guid>(itemsToRemove.Select(p => p.UniqueId));
                var beamsToRemove = beams.Where(b =>
                    (b.NodeI_Type == Models.InputData.NodeReferenceType.PileLayout && pileIds.Contains(b.NodeI_Id)) ||
                    (b.NodeJ_Type == Models.InputData.NodeReferenceType.PileLayout && pileIds.Contains(b.NodeJ_Id))
                ).ToList();

                foreach (var beam in beamsToRemove)
                    beams.Remove(beam);
            }

            // 杭の実削除
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
        /// 選択された梁要素 (FoundationBeamInput.Beams) を削除するコマンド。
        /// 削除対象の端点ノードは残す (他梁が参照している可能性があるため、孤立ノードはユーザーが手動整理)。
        /// </summary>
        [RelayCommand]
        private void DeleteBeams()
        {
            if (!CheckAndResetAnalysisResults()) return;

            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;
            var toRemove = beams.Where(b => b.IsSelected).ToList();
            if (toRemove.Count == 0) return;

            SaveUndoState();
            foreach (var beam in toRemove)
                beams.Remove(beam);

            // 旧 No プロパティ廃止: 番号 = 位置インデックスで自動的に追従

            RequestUpdateWindow();
        }

        /// <summary>
        /// すべての梁要素の選択を解除するコマンド。
        /// </summary>
        [RelayCommand]
        private void DeselectBeams()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;
            foreach (var beam in beams)
                beam.IsSelected = false;

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

            // 杭接合節点 → 一般節点: 接合節点位置 (= pile.Z) に一般節点を配置 (v2 セマンティクス)
            foreach (var pile in selectedPiles)
            {
                var newNode = new InputNode
                {
                    No = CurrentInputModel.InputNodes.Count + 1,
                    Type = NodeType.General,
                    X = pile.X,
                    Y = pile.Y,
                    Z = pile.Z,
                };

                CurrentInputModel.PileLayoutItems.Remove(pile);
                CurrentInputModel.InputNodes.Add(newNode);
            }

            // 一般節点 → 杭接合節点: 一般節点 (X,Y,Z) をそのまま接合節点とする (v2 セマンティクス)
            // ΔZc は既存杭配置の最頻値を使用（杭配置がない場合はデフォルト 1.0）
            var deltaZc = GetMostCommonDeltaZc();
            foreach (var node in selectedNodes)
            {
                var newPile = new PileLayoutDataItem
                {
                    X = node.X,
                    Y = node.Y,
                    Z = node.Z,
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
                    // システムDPIを取得
                    var dpiInfo = VisualTreeHelper.GetDpi(Canvas3DLayout);
                    double dpiX = dpiInfo.PixelsPerInchX;
                    double dpiY = dpiInfo.PixelsPerInchY;

                    int width = (int)(Canvas3DLayout.ActualWidth * dpiX / 96.0 * scale);
                    int height = (int)(Canvas3DLayout.ActualHeight * dpiY / 96.0 * scale);

                    // Canvas を RenderTargetBitmap でキャプチャ（システムDPI考慮）
                    var rtb = new RenderTargetBitmap(
                        width,
                        height,
                        dpiX * scale, dpiY * scale,
                        PixelFormats.Pbgra32);

                    // 背景を白で描画してからCanvasを直接レンダリング
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null,
                            new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));
                    }
                    rtb.Render(dv);
                    rtb.Render(Canvas3DLayout);

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
                    PileDesign.Services.MessageService.Show($"画像の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // システムDPIを取得
                var dpiInfo = VisualTreeHelper.GetDpi(Canvas3DLayout);
                double dpiX = dpiInfo.PixelsPerInchX;
                double dpiY = dpiInfo.PixelsPerInchY;

                int width = (int)(Canvas3DLayout.ActualWidth * dpiX / 96.0 * scale);
                int height = (int)(Canvas3DLayout.ActualHeight * dpiY / 96.0 * scale);

                // Canvas を RenderTargetBitmap でキャプチャ（システムDPI考慮）
                var rtb = new RenderTargetBitmap(
                    width,
                    height,
                    dpiX * scale, dpiY * scale,
                    PixelFormats.Pbgra32);

                // 背景を白で描画してからCanvasを直接レンダリング
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null,
                        new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));
                }
                rtb.Render(dv);
                rtb.Render(Canvas3DLayout);

                // Clipboard.SetImage()はStringMetadata非対応で例外になる環境があるため
                // BitmapSourceを一切渡さず、生バイトストリームのみでクリップボードに設定
                var pngEnc = new PngBitmapEncoder();
                pngEnc.Frames.Add(BitmapFrame.Create(rtb));
                var pngStream = new System.IO.MemoryStream();
                pngEnc.Save(pngStream);

                var bmpEnc = new BmpBitmapEncoder();
                bmpEnc.Frames.Add(BitmapFrame.Create(rtb));
                var bmpStream = new System.IO.MemoryStream();
                bmpEnc.Save(bmpStream);
                // DIB = BMPからファイルヘッダ(14バイト)を除いたもの
                bmpStream.Position = 14;
                var dibBytes = new byte[bmpStream.Length - 14];
                bmpStream.Read(dibBytes, 0, dibBytes.Length);

                var dataObject = new DataObject();
                dataObject.SetData("PNG", pngStream, false);
                dataObject.SetData(DataFormats.Dib, new System.IO.MemoryStream(dibBytes), false);
                Common.ClipboardHelper.TrySetDataObject(dataObject, true);

                StatusMessage = $"画像をクリップボードにコピーしました ({width}x{height})";
            }
            catch (Exception ex)
            {
                PileDesign.Services.MessageService.Show($"画像のコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
        /// アプリの致命的例外時に呼び出される緊急 AutoSave。成功時はファイルパス、
        /// 失敗時 (または初期化前) は null を返す。例外を投げない。
        /// </summary>
        public string? TryEmergencyAutoSave()
        {
            try { return _autoSaveService?.TryEmergencyAutoSave(); }
            catch { return null; }
        }

        /// <summary>
        /// 自動保存完了時のイベントハンドラ
        /// </summary>
        private void OnAutoSaveCompleted(object? sender, AutoSaveEventArgs e)
        {
            // StatusMessage は他の一過性メッセージ用に温存。AutoSave 状態は専用の
            // LastAutoSaveText に出すことで衝突を避ける。
            if (e.Success)
            {
                LastAutoSaveText = $"自動保存: {e.Timestamp:HH:mm:ss}";
                LastAutoSaveBrush = Brushes.Gray;
            }
            else
            {
                LastAutoSaveText = $"自動保存失敗 ({e.Timestamp:HH:mm:ss})";
                LastAutoSaveBrush = Brushes.Red;

                // 連続失敗が 3 回以上に達したら Toast でエスカレーション通知。
                // 3 分間隔 × 3 回 = ~9 分間 AutoSave が動作していない計算で、ユーザーに気付かせるべきタイミング。
                if (e.ConsecutiveFailures >= 3)
                {
                    ShowToast(
                        $"自動保存が {e.ConsecutiveFailures} 回連続で失敗しています。\n{e.ErrorMessage}",
                        type: 2);  // Warning
                }
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
                MessageService.Show($"ファイルが見つかりません。\n{filePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                _mruService.RemoveFile(filePath);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var projectData = _fileOperationService.LoadProjectData(filePath);

                if (projectData != null)
                {
                    CurrentInputModel = projectData.InputModel;
                    CurrentModel = projectData.AnaModel;
                    ApplyPostLoadProtocol(projectData, filePath, "読込が完了しました。");
                }
                else
                {
                    var ok = TryLoadInputModelFileUsingInputModelLoader(filePath);
                    if (!ok)
                        throw new InvalidOperationException("ファイル形式が不正です。");
                    return;
                }

                // MRU に追加
                _mruService.AddFile(filePath);

                // 自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
            }
            catch (Exception ex)
            {
                HandleFileLoadError(ex, filePath);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// ファイル読込失敗時の共通ハンドラー。例外種別ごとにユーザーフレンドリーなメッセージを表示し、
        /// 互換性問題やファイル不存在の場合は MRU から該当ファイルを除去する。
        /// </summary>
        private void HandleFileLoadError(Exception ex, string filePath)
        {
            string message;
            bool removeFromMru = false;
            string fileName = !string.IsNullOrEmpty(filePath) ? System.IO.Path.GetFileName(filePath) : "(不明)";

            // 旧バージョンで保存されたファイルとの互換性問題 ($id/$ref のチェーンが現スキーマと不整合)
            if (ex is System.Text.Json.JsonException jsonRefEx
                && jsonRefEx.Message.Contains("Reference") && jsonRefEx.Message.Contains("was not found"))
            {
                message = $"このファイルは現バージョンと互換性がありません。\n" +
                          $"以前のバージョンで保存された解析結果データの形式が変更されています。\n\n" +
                          $"ファイル: {fileName}\n\n" +
                          $"対応: 例題ファイルから始めて、新しく保存し直してください。\n" +
                          $"このファイルは「最近使ったファイル」一覧から自動的に削除されました。";
                removeFromMru = true;
            }
            else if (ex is System.Text.Json.JsonException)
            {
                message = $"ファイルの JSON 形式が不正です。\n{fileName}\n\n詳細: {ex.Message}";
                removeFromMru = true;
            }
            else if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                message = $"ファイルが見つかりません。\n{filePath}";
                removeFromMru = true;
            }
            else
            {
                message = $"読込に失敗しました。\n{ex.Message}";
            }

            MessageService.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

            if (removeFromMru && !string.IsNullOrEmpty(filePath))
            {
                _mruService.RemoveFile(filePath);
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

            var result = MessageService.Show(
                $"自動保存されたファイルが見つかりました。\n\n" +
                $"保存日時: {fileInfo.CreationTime:yyyy/MM/dd HH:mm:ss}\n" +
                $"ファイル: {System.IO.Path.GetFileName(latestAutoSave)}\n" +
                $"場所: {System.IO.Path.GetDirectoryName(latestAutoSave)}\n\n" +
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

                        // ファイルパスは元のファイル名から推測（自動保存ファイル名から取得）
                        string? inferredFilePath = null;
                        var originalFileName = System.IO.Path.GetFileNameWithoutExtension(latestAutoSave);
                        var autoSaveIndex = originalFileName.IndexOf("_autosave_");
                        if (autoSaveIndex > 0)
                        {
                            originalFileName = originalFileName[..autoSaveIndex];
                            // 元のファイルパスを推測（未保存ならnull）
                            inferredFilePath = originalFileName != "Untitled" ? originalFileName + ".pdj" : null;
                        }

                        ApplyPostLoadProtocol(projectData, inferredFilePath, "自動保存ファイルの復元が完了しました。");

                        // 復元後は自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                        if (!string.IsNullOrEmpty(CurrentFilePath))
                        {
                            _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageService.Show($"自動保存ファイルの復元に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // はい・いいえどちらでもリネームして再表示を防止（データは残すので手動復元可能）
            try
            {
                var dismissed = latestAutoSave.Replace("_autosave_", "_autosave_dismissed_");
                System.IO.File.Move(latestAutoSave, dismissed);
            }
            catch (Exception ex) { Log.Warning(ex, "[AutoSave] リネーム失敗"); }
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
                ShowToast("重複する一般節点はありませんでした。");
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

            MessageService.Show(
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
        /// 一般節点を削除 (接続された一般梁要素もカスケード削除)
        /// </summary>
        [RelayCommand]
        private void DeleteInputNode(InputNode? node)
        {
            if (node == null) return;

            // 接続されている一般梁要素を抽出 (NodeI/J_Type=GeneralNode かつ Id が一致するもの)
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            var connectedBeams = beams?
                .Where(b => b != null
                            && ((b.NodeI_Type == NodeReferenceType.GeneralNode && b.NodeI_Id == node.UniqueId)
                             || (b.NodeJ_Type == NodeReferenceType.GeneralNode && b.NodeJ_Id == node.UniqueId)))
                .ToList() ?? new List<FoundationBeam>();

            string confirmMsg;
            if (connectedBeams.Count > 0)
            {
                var beamNos = connectedBeams.Select(b => beams!.IndexOf(b) + 1).OrderBy(n => n).ToList();
                string list = string.Join(", ", beamNos.Take(20).Select(n => $"#{n}"));
                if (beamNos.Count > 20) list += $" ほか {beamNos.Count - 20} 件";
                confirmMsg = $"節点 No.{node.No} を削除します。\n" +
                             $"同時に接続された一般梁要素 {beamNos.Count} 本 ({list}) も削除されます。\n" +
                             $"よろしいですか?";
            }
            else
            {
                confirmMsg = $"節点 No.{node.No} を削除してもよろしいですか？";
            }

            var result = MessageService.Show(
                confirmMsg,
                connectedBeams.Count > 0 ? "削除確認" : "確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
            {
                // Undo スナップショットは「削除前」の状態を保存する必要があるため、
                // 実際の Remove 操作より先に呼ぶ。
                SaveUndoState();

                // 接続梁を先に除去 → 節点を除去
                if (beams != null)
                {
                    foreach (var beam in connectedBeams)
                        beams.Remove(beam);
                }
                CurrentInputModel.InputNodes.Remove(node);
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
                        // 既存材料の編集 (SelectedMaterialNo は 1-based の位置インデックス)
                        var material = wizard.SelectedMaterialNo.Value >= 1
                            ? CurrentInputModel.FoundationBeamInput.Materials.ElementAtOrDefault(wizard.SelectedMaterialNo.Value - 1)
                            : null;
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
                        // 新規材料の追加 (No プロパティは廃止: 位置 = ID)
                        var newMaterial = new BeamMaterial
                        {
                            Name = wizard.Result.Name,
                            YoungModulus = wizard.Result.YoungModulus,
                            ShearModulus = wizard.Result.ShearModulus,
                            PoissonRatio = wizard.Result.PoissonRatio
                        };
                        CurrentInputModel.FoundationBeamInput.Materials.Add(newMaterial);
                        addedMaterialNo = CurrentInputModel.FoundationBeamInput.Materials.Count;
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
                            var addedMaterial = addedMaterialNo.Value >= 1
                                ? CurrentInputModel.FoundationBeamInput.Materials.ElementAtOrDefault(addedMaterialNo.Value - 1)
                                : null;
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
                        // 既存断面の編集 (SelectedSectionNo は 1-based の位置インデックス)
                        var section = wizard.SelectedSectionNo.Value >= 1
                            ? CurrentInputModel.FoundationBeamInput.Sections.ElementAtOrDefault(wizard.SelectedSectionNo.Value - 1)
                            : null;
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
                        // 新規断面の追加 (No プロパティは廃止: 位置 = ID)
                        var newSection = new BeamSection
                        {
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
                        addedSectionNo = CurrentInputModel.FoundationBeamInput.Sections.Count;
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
                            var addedSection = addedSectionNo.Value >= 1
                                ? CurrentInputModel.FoundationBeamInput.Sections.ElementAtOrDefault(addedSectionNo.Value - 1)
                                : null;
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

            // 使用中かチェック (1-based 位置インデックス基準)
            int materialNo = CurrentInputModel.FoundationBeamInput.GetMaterialNo(material);
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.MaterialNo == materialNo);
            if (isUsed)
            {
                PileDesign.Services.MessageService.Show(
                    $"材料No.{materialNo}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            int deletedNo = materialNo;
            CurrentInputModel.FoundationBeamInput.Materials.Remove(material);
            // 残存材料を再採番し、梁要素の MaterialNo 参照を新しい番号に追従させる
            RenumberMaterialsAndUpdateReferences(deletedNo);
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

            // 使用中かチェック (1-based 位置インデックス基準)
            int sectionNo = CurrentInputModel.FoundationBeamInput.GetSectionNo(section);
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.SectionNo == sectionNo);
            if (isUsed)
            {
                PileDesign.Services.MessageService.Show(
                    $"断面No.{sectionNo}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            int deletedNo = sectionNo;
            CurrentInputModel.FoundationBeamInput.Sections.Remove(section);
            // 残存断面を再採番し、梁要素の SectionNo 参照を新しい番号に追従させる
            RenumberSectionsAndUpdateReferences(deletedNo);
            SaveUndoState();
            RequestUpdateWindow();
        }

        // 材料削除後の梁要素 MaterialNo 参照調整。
        // 削除位置 (deletedNo, 1-based) より後ろの材料は位置が 1 つ前にシフトするため、
        // それを参照していた梁要素の MaterialNo を 1 つデクリメントする。
        // No プロパティ廃止に伴い、Material 自身の番号書き換えは不要 (位置 = ID)。
        private void RenumberMaterialsAndUpdateReferences(int deletedNo)
        {
            var fbi = CurrentInputModel?.FoundationBeamInput;
            if (fbi?.Beams == null) return;

            foreach (var beam in fbi.Beams)
            {
                if (beam.MaterialNo > deletedNo)
                    beam.MaterialNo--;
            }
        }

        // 断面削除後の梁要素 SectionNo 参照調整。
        private void RenumberSectionsAndUpdateReferences(int deletedNo)
        {
            var fbi = CurrentInputModel?.FoundationBeamInput;
            if (fbi?.Beams == null) return;

            foreach (var beam in fbi.Beams)
            {
                if (beam.SectionNo > deletedNo)
                    beam.SectionNo--;
            }
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
                MessageService.Show("一般梁要素が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
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
