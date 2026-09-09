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
    /// MainWindowViewModel — 元に戻す / やり直す。
    ///
    /// 入力の状態を丸ごと控えて積む方式。控えるのは <c>SnapshotForSaving</c> ではなく
    /// <c>DeepCopy</c> で、保存ファイルとは形が違ってよい（画面に戻せればよい）。
    ///
    /// <b>SaveUndoState は全編集の集約点。</b> 表の編集・ダイアログ・読込のいずれからも
    /// 通るので、ここで立つ印（編集済み）は読込の仕上げで戻すこと。戻し忘れると、
    /// ファイルを開いただけで「保存されていない作業があります」と出る。
    ///
    /// <b>まとめて 1 手にする仕組みがある。</b> 表を続けて打つと 1 打鍵ごとに積まれて
    /// しまうので、<see cref="SaveUndoStateDebounced"/> が一定時間まとめる。
    /// ダイアログのように「開いて閉じるまでで 1 手」にしたい場合は
    /// <see cref="ExtendUndoBatchSession"/> で延長する。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の先頭にあり、
    /// 通知の 2 つは <c>MainWindow.ViewModel.Undo.cs</c> という別名のファイルにあった。
    /// </summary>
    public partial class MainWindowViewModel
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
        public void MarkPossiblyEdited()
        {
            _hasUnsavedWork = true;
            InputEditVersion++;
        }

        public void SaveUndoState([System.Runtime.CompilerServices.CallerMemberName] string? description = null)
            => SaveUndoState(AnalysisInputScope.All, description);

        /// <summary>
        /// Undo スナップショットを積み、入力が編集されたことを記録する。
        ///
        /// <paramref name="scope"/> は<b>この編集がどの解析に効くか</b>。
        /// 群杭沈下の入力しか触らない画面は <see cref="AnalysisInputScope.Settlement"/> を渡す。
        /// 既定 (<see cref="AnalysisInputScope.All"/>) は水平解析の結果も陳腐化させる。
        /// </summary>
        public void SaveUndoState(AnalysisInputScope scope,
            [System.Runtime.CompilerServices.CallerMemberName] string? description = null)
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
                InputEditVersion++;

                // 入力が編集された = 表示中の解析結果は現在の入力と一致しない。
                // 結果は破棄しない (解析時の入力ごと切り離してあるため表示は整合している)。
                // ここは DataGrid のセル確定 (SaveUndoStateDebounced 経由) も含む全編集の集約点。
                MarkInputChangedSinceAnalysis(scope);
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
            => SaveUndoStateDebounced(AnalysisInputScope.All, description, debounceMs);

        /// <summary>
        /// <see cref="SaveUndoStateDebounced(string?, int)"/> に編集の効く範囲を付けたもの。
        /// </summary>
        public void SaveUndoStateDebounced(AnalysisInputScope scope,
            [System.Runtime.CompilerServices.CallerMemberName] string? description = null,
            int debounceMs = DefaultUndoBatchDebounceMs)
        {
            // セッション開始時のみ重い DeepCopy を実行 (pre-edit 状態を捕捉)。
            // 2 回目以降の連続編集では SaveUndoState をスキップし、デバウンスタイマーだけ更新。
            if (!_undoBatchActive)
            {
                SaveUndoState(scope, description);
                _undoBatchActive = true;
            }
            else
            {
                // 2 回目以降でも「効く範囲」だけは記録する
                MarkInputChangedSinceAnalysis(scope);
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

    /// <summary>
    /// 外部（code-behind等）からPropertyChanged通知を発火するための公開メソッド
    /// </summary>
    public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

    private void RaiseUndoStateChanged()
    {
        // CommunityToolkit の IRelayCommand を使って CanExecute 再評価を通知
        (UndoCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RedoCommand as IRelayCommand)?.NotifyCanExecuteChanged();

        // ステータスバー情報の更新
        OnPropertyChanged(nameof(UndoRedoStatusText));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
        OnPropertyChanged(nameof(PileCountText));
        OnPropertyChanged(nameof(AnalysisStatusText));
        OnPropertyChanged(nameof(AnalysisStatusItems));
    }
    }
}
