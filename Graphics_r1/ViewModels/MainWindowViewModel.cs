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
        private bool _hasUnsavedWork;

        /// <summary>
        /// 保存していない作業があるか。true になるのは次の 2 つ。
        ///   ・入力を編集した     (<see cref="SaveUndoState"/> が全編集の集約点)
        ///   ・解析が完了した     (<see cref="SetLatestAnalysisCompleted"/>)
        /// 保存・読み込み・新規作成・計算例ロードの直後は false に戻る。
        ///
        /// 「現状の入力内容は削除されます」「現在のデータを保存しますか？」の確認は、
        /// これが true のときだけ出す。失うものが無いのに出す確認は、
        /// 内容を確かめずに押すだけのものになり、
        /// 本当に失うものがあるときにも読まれなくなる。
        /// </summary>
        public bool HasUnsavedWork => _hasUnsavedWork;

        /// <summary>
        /// 保存・読み込み・新規作成・計算例ロードの直後に呼ぶ。
        /// 「今この状態を捨てても失うものが無い」に戻す。
        /// </summary>
        public void MarkWorkSaved() => _hasUnsavedWork = false;

        /// <summary>
        /// 編集された<b>かもしれない</b>ことを記録する。
        ///
        /// 入力ウィンドウ (基本設定・荷重・地盤・杭体・基礎梁・杭要素分割) は自前の Undo を持ち、
        /// 共有の <see cref="CurrentInputModel"/> を直接書き換えるため、
        /// <see cref="SaveUndoState"/> を通らない。変更の有無を確実に知る手立てが無いので、
        /// これらを開いたら編集されたものとして扱う。
        ///
        /// キャンセルで閉じても確認が出るが、
        /// 「編集したのに確認が出ない」でデータを失うよりはよい。
        /// <b>編集できるウィンドウを追加したら、ここを呼ぶこと。</b>
        /// </summary>
        public void MarkPossiblyEdited() => _hasUnsavedWork = true;

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

                // 編集が入ったので、以降は破棄・保存の確認を出す。
                _hasUnsavedWork = true;

                // 入力が編集された = 表示中の解析結果は現在の入力と一致しない。
                // 結果は破棄しない (解析時の入力ごと切り離してあるため表示は整合している)。
                // ここは DataGrid のセル確定 (SaveUndoStateDebounced 経由) も含む全編集の集約点。
                MarkInputChangedSinceAnalysis();
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

        // アプリ全体のユーザー設定 (LocalAppData の PileDesign フォルダ内 user_settings.json)。
        // 下の保存オプションのプロパティが getter/setter で直接読み書きするため、
        // コンストラクタを待たずフィールド初期化子で生成する。
        private readonly UserSettingsService _userSettingsService = new();

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
        /// <param name="undoDescription">
        /// メイン画面の Undo 履歴に残す説明。渡すと、ダイアログを閉じたあとに 1 段積む。
        /// 省略すると<b>そのダイアログの編集はメイン画面の Ctrl+Z で戻せない</b>ので、
        /// 入力を編集するウィンドウでは必ず渡すこと (現在は 5 つとも渡している)。
        /// ※ 以前はここに「未使用」と書いてあったが、DeepCopy の高速化 (2026-05-25) で
        ///    Undo の記録が復活しており、実装と正反対になっていた。
        /// </param>
        private void OpenDialogWindowWithUndo<TViewModel, TWindow>(Action postDialogAction = null, string? undoDescription = null)
            where TViewModel : ObservableObject
            where TWindow : Window, new()
        {
            // ダイアログを開く
            OpenDialogWindow<TViewModel, TWindow>(this);

            MarkPossiblyEdited();

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
        //
        // 手動保存は既定 ON: 開き直したときに再計算なしで前回結果を確認できるのを標準とする。
        // 自動保存は既定 OFF: 定期実行なので ON だと数十 MB の書込が繰り返し発生する。
        // どちらもファイルが肥大する場合があるため OFF に切り替えられる。

        // 値はユーザー設定ファイルに永続化する (起動のたびに OFF へ戻らない)。
        // バッキングフィールドを置かず UserSettings を直接読み書きするのは、
        // 「画面の状態」と「保存された設定」の二重管理でずれるのを防ぐため。

        // 手動保存 (Ctrl+S / 名前を付けて保存) に解析結果を含めるか
        public bool IsSaveAnalysisResultsManual
        {
            get => _userSettingsService.Settings.IsSaveAnalysisResultsManual;
            set
            {
                if (_userSettingsService.Settings.IsSaveAnalysisResultsManual == value) return;
                _userSettingsService.Settings.IsSaveAnalysisResultsManual = value;
                _userSettingsService.Save();
                OnPropertyChanged();
            }
        }

        // 自動保存に解析結果を含めるか (定期保存のため既定 OFF 推奨。ON だと毎回数秒・大容量書込)
        public bool IsSaveAnalysisResultsAutoSave
        {
            get => _userSettingsService.Settings.IsSaveAnalysisResultsAutoSave;
            set
            {
                if (_userSettingsService.Settings.IsSaveAnalysisResultsAutoSave == value) return;
                _userSettingsService.Settings.IsSaveAnalysisResultsAutoSave = value;
                _userSettingsService.Save();
                OnPropertyChanged();
            }
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
                // 差し替え前のモデルから購読を外す。外さないと、捨てたはずのモデルの編集で
                // 集計が走り続け、そのモデルも解放されない。
                var previous = _currentInputModel;

                // SetProperty は ObservableObject のユーティリティ（CommunityToolkit）
                if (SetProperty(ref _currentInputModel, value))
                {
                    if (previous?.PileLayoutItems is { } previousPiles)
                    {
                        previousPiles.CollectionChanged -= PileLayoutItems_CollectionChanged;
                        foreach (var pile in previousPiles)
                            pile.PropertyChanged -= PileLayoutItem_PropertyChanged;
                    }

                    // VM 再アタッチなどはここで一度だけ行う
                    _currentInputModel?.AttachViewModel(this);

                    // PileLayoutItems の CollectionChanged を再購読
                    if (_currentInputModel?.PileLayoutItems is { } piles)
                    {
                        piles.CollectionChanged -= PileLayoutItems_CollectionChanged;
                        piles.CollectionChanged += PileLayoutItems_CollectionChanged;

                        // 既存の杭の PropertyChanged も購読する。
                        // 杭ごとの購読は「コレクションに追加されたとき」にしか張られないので、
                        // ファイル読込や Undo でコレクションごと差し替わると、
                        // 中身が入った状態で購読の無い杭が残る。そうなると軸力や座標を編集しても
                        // ΣVL・転倒モーメントの表示が古いままになる (基礎梁は同じ理由で下に張り直している)。
                        foreach (var pile in piles)
                        {
                            pile.PropertyChanged -= PileLayoutItem_PropertyChanged;
                            pile.PropertyChanged += PileLayoutItem_PropertyChanged;
                        }
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
                InvalidateBeamAwareResultsSilently("基礎梁の変更により、群杭沈下解析（反復）の結果を破棄しました。");
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
                InvalidateBeamAwareResultsSilently("基礎梁の変更により、群杭沈下解析（反復）の結果を破棄しました。");
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
            // ログ・テーブル・グラフのコマンド有効状態を更新
            (OpenLogWindowCommand as CommunityToolkit.Mvvm.Input.IRelayCommand)?.NotifyCanExecuteChanged();
            (OpenTableWindowCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
            (OpenGraphWindowCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
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

            // (5) CaseRecords を持たない旧ファイル: 複製しか残っていないので、そこから 1 件復元する。
            //     表示系は ActiveRecord を読むようになったため、これが無いと旧ファイルの
            //     沈下コンタが出なくなる。
            if ((pgs.CaseRecords == null || pgs.CaseRecords.Count == 0)
                && (pgs.SettlementGridData?.Count ?? 0) > 0)
            {
                pgs.CaseRecords =
                [
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "VL",
                        LoadingType = string.IsNullOrEmpty(pgs.LoadingType) ? "任意矩形" : pgs.LoadingType,
                        IsBeamAware = false,
                        IsConverged = true,
                        RectLoads = [.. (pgs.RectLoads ?? []).Select(r => r.Clone())],
                        SettlementGridData = [.. pgs.SettlementGridData.Select(g => g.Clone())],
                    }
                ];
                pgs.ActiveCaseIndex = 0;
            }

            if (pgs.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

            // 表示するケースが決まっていない旧ファイルは先頭を選ぶ
            if (pgs.ActiveCaseIndex < 0 || pgs.ActiveCaseIndex >= pgs.CaseRecords.Count)
                pgs.ActiveCaseIndex = 0;

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
            ObservableCollection<SettlementGridDataItem> gridData,
            Dictionary<int, double> pileSettlementsMm)
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
                // 要素まで複製する (表示用の複製と同じインスタンスを共有しない)
                SettlementGridData = [.. (gridData ?? []).Select(g => g.Clone())],
                // 解析が返した値をそのまま持つ。以前は各杭の複製から拾い直しており、
                // 結果の正が入力側にある状態だった。
                PileSettlements_mm = new Dictionary<int, double>(pileSettlementsMm ?? []),
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
                MessageService.Show(GuardMessages.NoPileLayout, "確認", MessageBoxButton.OK, MessageBoxImage.Information);
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
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n {ConcreteModelOptions.MapLimitStateText("使用限界")}軸力適用範囲Max{nMax:N0}kN < {force:N0}kN\n";
                    }
                    if (force < nMin)
                    {
                        hasWarning = true;
                        warningMessage += $"- 杭配置番号{pileNo} セグメント{i + 1} 荷重ケース:VL:\n {force:N0}kN < {ConcreteModelOptions.MapLimitStateText("使用限界")}軸力適用範囲Min{nMin:N0}kN\n";
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
                    // ここでは確認を出さない。開くだけなら入力は変わらず
                    // (編集はすべて複製に対して行い、保存で初めて書き戻す)、
                    // 分割の中身を見たいだけのときに結果の破棄を聞かれてしまう。
                    // 確認は ElementDivisionViewModel の「保存」で出す。

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

                    // 保存せずに閉じたときは入力を触っていないので、
                    // 編集済みの印も Undo の記録も残さない。残すと「見ただけ」で
                    // 保存を促され、Undo 履歴にも空の 1 手が積まれる。
                    if (window.IsSaved)
                    {
                        MarkPossiblyEdited();

                        if (undoCopy != null)
                        {
                            _undoManager.SaveState(undoCopy, "杭要素分割");
                            RaiseUndoStateChanged();
                        }
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
        // 杭要素分割が前提。CanExecute に持たせることで、ボタン・キーボード (F6)・
        // コマンドパレットのどこから呼んでも同じ条件になる。
        private bool CanOpenSettlementWindow() => IsElementSplit;

        [RelayCommand(CanExecute = nameof(CanOpenSettlementWindow))]
        public void OpenSettlementWindow()
        {
            if (IsPreparedForAnalysis())
            {
                if (CurrentInputModel.ElementDivision.SoilPiles == null || CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    MessageService.Show(GuardMessages.NoPileLayout);
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

        /// <summary>
        /// 水平解析ウィンドウを開けるか。杭要素分割が済んでいることが前提。
        ///
        /// この判定をコマンド側に持たせることで、リボンのボタンもキーボード (F5) も
        /// 同じ条件で無効になる。以前はボタンにだけ IsEnabled を掛けていたため、
        /// キーからは実行できてしまい、直後に「杭要素分割を行ってください。」と叱られていた。
        /// </summary>
        private bool CanOpenLateralLoadAnalysisWindow() => IsElementSplit;

        // 水平荷重解析ウィンドウを開くメソッド
        [RelayCommand(CanExecute = nameof(CanOpenLateralLoadAnalysisWindow))]
        public async Task OpenLateralLoadAnalysisWindowAsync()
        {
            if (IsPreparedForAnalysis())
            {
                if (CurrentInputModel.ElementDivision.SoilPiles == null || CurrentInputModel.ElementDivision.SoilPiles.Count == 0)
                {
                    MessageService.Show(GuardMessages.NoPileLayout);
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
                            Serilog.Log.Error(ex, "ダイアログの表示に失敗");
                            MessageService.Show(GuardMessages.WindowOpenFailed("ウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageService.Show(GuardMessages.NoPileLayout, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    "単杭沈下解析（基礎梁考慮）には、各杭の荷重-沈下関係（単杭沈下解析の結果）が必要です。\n" +
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
                Serilog.Log.Error(ex, "ダイアログの表示に失敗");
                            MessageService.Show(GuardMessages.WindowOpenFailed("ウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 解析準備ができているかを確認するメソッド
        private bool IsPreparedForAnalysis()
        {
            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                PileDesign.Services.MessageService.Show(GuardMessages.NoPileLayout);
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
                    PileDesign.Services.MessageService.ShowError($"画像の保存に失敗しました", ex, "エラー");
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
                PileDesign.Services.MessageService.ShowError($"画像のコピーに失敗しました", ex, "エラー");
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
                    MessageService.ShowError($"自動保存ファイルの復元に失敗しました。", ex, "エラー");
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
