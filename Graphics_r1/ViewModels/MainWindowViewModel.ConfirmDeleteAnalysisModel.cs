using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.ConfirmDeleteAnalysisModel.cs
    ///
    /// 責任範囲:
    /// - 解析結果削除の確認ダイアログ処理
    /// - 解析モデルのリセット処理
    /// - 解析関連フラグの一括リセット
    /// </summary>
    public partial class MainWindowViewModel
    {
        // 共通ヘルパ: 解析モデル削除の確認と（必要なら）リセット実行
        private bool ConfirmDeleteAnalysisModel(
            string message = "解析結果を削除します。よろしいですか？",
            string caption = "確認",
            MessageBoxImage icon = MessageBoxImage.Question,
            bool resetModel = true)
        {
            var result = MessageService.Show(message, caption, MessageBoxButton.YesNo, icon);
            if (result != MessageBoxResult.Yes) return false;

            if (resetModel)
            {
                // 解析に由来する状態は 1 か所にまとめてある。
                // ここで個別に消していた頃は、経路ごとに消し漏らしが出ていた。
                ClearAllAnalysisState(includeElementSplit: true);
                UpdateWindowImmediate();
            }

            return true;
        }

        /// <summary>
        /// 入力を変更するコマンドの入口で呼ぶ。
        ///
        /// 解析結果は<b>破棄しない</b>。実務では結果を横目に見ながら入力を変えていくため、
        /// 少しでも触ると結果が消える運用は成り立たない。解析完了時に入力ごと複製して
        /// 切り離してあるので、入力を編集しても結果表示は解析時のままで整合が崩れない。
        /// 代わりに「入力が変更された = 再解析が必要」であることを記録する。
        ///
        /// 杭要素分割は解析結果ではなく入力側の状態なので、従来どおり確認のうえ無効化する。
        /// </summary>
        private bool CheckAndResetAnalysisResults()
        {
            MarkInputChangedSinceAnalysis();
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: true);
        }

        /// <summary>
        /// 杭要素分割ウィンドウの「保存」で呼ぶ。
        ///
        /// 確認を<b>開くときではなく保存するとき</b>に出すためのもの。開くだけなら入力は
        /// 変わらない (編集はすべて複製に対して行う) ので、中を見たいだけの人に結果の破棄を聞くことになっていた。
        ///
        /// 杭要素分割そのものは破棄の対象ではない。ここで保存される分割が画面に出ている
        /// もので、捨てられるわけではないため、文面にも出さない。
        ///
        /// 水平解析の結果は残る (解析時の入力ごと切り離してある) が、分割が変われば
        /// 再解析が要る。<b>何も消えなくても、影響があるなら知らせる</b>。
        /// 分割の中身が実際に変わったかは見ていない。見落として黙るより、
        /// 保存を押したときに毎回知らせるほうが安全側。
        /// </summary>
        public bool ConfirmSaveElementDivision()
        {
            bool discardSettlement = HasSettlementResults();
            bool horizontalGoesStale = IsHorizontalAnalysisDone || HasAnalysisResultSet;

            // 影響を受ける結果が無ければ黙って通す
            if (discardSettlement || horizontalGoesStale)
            {
                var parts = new List<string>();
                if (discardSettlement) parts.Add("・沈下解析の結果は削除されます");
                if (horizontalGoesStale) parts.Add("・水平解析の結果は残りますが、再解析が必要になります");

                string msg = "杭要素分割を保存すると、解析結果に次の影響があります。\n\n"
                           + string.Join("\n", parts)
                           + "\n\n保存しますか？";

                var result = MessageService.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return false;

                if (discardSettlement) ClearSettlementResults();
            }

            MarkInputChangedSinceAnalysis();
            UpdateWindowImmediate();
            return true;
        }

        /// <summary>
        /// 入力変更で無効になるものを 1 回のダイアログで確認し、破棄する。
        ///
        /// <b>水平解析の結果は破棄しない。</b>解析完了時に入力ごと複製して切り離してあるので、
        /// 入力を編集しても結果表示は解析時のまま整合する。
        ///
        /// 一方、次の 2 つは入力側の状態なので従来どおり破棄する。
        /// <list type="bullet">
        /// <item>杭要素分割 — ジオメトリが変われば分割は無効</item>
        /// <item>沈下解析の結果 — <see cref="PileGroupSettlement"/> の CaseRecords /
        ///   SettlementGridData や各杭の GroupPileSettlement のように<b>入力モデルの中に</b>
        ///   格納されており、解析結果セットで切り離せない。残すと杭配置グリッドなど入力系の表示に
        ///   古い値がそのまま出て、しかも傾斜角検定の可否判定にも使われる</item>
        /// </list>
        /// 破棄するものが無ければダイアログを出さずに true を返す
        /// （沈下解析を使っていない場合や、既に分割を取り消してある場合は何も出ない）。
        /// </summary>
        /// <param name="includeElementSplit">杭要素分割も対象にするか（ジオメトリを変える編集で true）。</param>
        /// <param name="reason">「〜により、」として文頭に付ける理由（省略可）。</param>
        private bool ConfirmDiscardInvalidatedByInputChange(bool includeElementSplit, string? reason = null)
        {
            bool discardSplit = includeElementSplit && IsElementSplit;
            bool discardSettlement = HasSettlementResults();

            if (!discardSplit && !discardSettlement) return true;

            var parts = new List<string>();
            if (discardSettlement) parts.Add("沈下解析結果");
            if (discardSplit) parts.Add("杭要素分割");

            string msg = (string.IsNullOrEmpty(reason) ? string.Empty : $"{reason}により、")
                       + string.Join("と", parts)
                       + "が削除されます。続けますか？\n（水平解析の結果は保持されます）";

            var result = MessageService.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return false;

            if (discardSplit) IsElementSplit = false;
            if (discardSettlement) ClearSettlementResults();

            UpdateWindowImmediate();
            return true;
        }

        /// <summary>入力モデル 1 つぶんの沈下結果を消す。現在の入力とスナップショットの両方に使う。</summary>
        private static void ClearSettlementResultsIn(Models.InputData.InputModel? input)
        {
            var pgs = input?.PileGroupSettlement;
            if (pgs == null) return;

            pgs.CaseRecords?.Clear();
            pgs.ActiveCaseIndex = -1;
            pgs.SettlementGridData = [];
            if (input!.PileLayoutItems != null)
                foreach (var pile in input.PileLayoutItems) pile.GroupPileSettlement = 0;
        }

        // テスト用フック (内部ロジックをそのまま検証する)
        internal bool HasSettlementResultsForTest() => HasSettlementResults();
        internal void ClearSettlementResultsForTest() => ClearSettlementResults();

        /// <summary>破棄対象になる沈下解析の結果を持っているか。</summary>
        private bool HasSettlementResults()
        {
            // 複製 (SettlementGridData) は見ない。読込時のマイグレーションで
            // 旧ファイルからも記録が建つので、記録だけ見れば足りる。
            var pgs = CurrentInputModel?.PileGroupSettlement;
            return (pgs?.CaseRecords?.Count ?? 0) > 0
                || IsVerticalAnalysisDone
                || IsGroupPileSettlementAnalysisDone;
        }

        /// <summary>
        /// 沈下解析の結果を破棄する。
        ///
        /// 沈下の結果は入力モデルの中に格納されているので、現在の入力と
        /// <b>解析時のスナップショット</b>の両方から消す。結果表示はスナップショットを読むため、
        /// 現在の入力だけ消すと「削除しました」と言いながら結果テーブルに残る。
        /// </summary>
        private void ClearSettlementResults()
        {
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;

            ClearSettlementResultsIn(CurrentResultSet?.InputSnapshot);
            ClearSettlementResultsIn(CurrentInputModel);

            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
        }

        /// <summary>
        /// 基本設定の Z=0 標高など、ジオメトリに影響する変更時に呼ぶ。
        /// 解析結果は破棄せず、杭要素分割のみキャンセル対象。
        /// 杭要素分割が無ければダイアログなしで true。
        /// </summary>
        public bool ConfirmResetAllForGeometryChange(string reason)
        {
            // 水平解析の結果は破棄しない (CheckAndResetAnalysisResults と同じ方針)。
            MarkInputChangedSinceAnalysis();
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: true, reason: reason);
        }

        /// <summary>
        /// 荷重条件など、ジオメトリを変更しない編集で呼ぶヘルパ。
        ///
        /// 解析結果は<b>破棄しない</b>。荷重条件のように頻繁に触る入力でダイアログを出すと
        /// 「結果を見ながら条件を変える」使い方ができなくなる。解析完了時に入力ごと複製して
        /// 切り離してあるので、編集しても結果表示は解析時のまま整合する。
        /// </summary>
        public bool CheckAndResetAnalysisResultsKeepingSplit(string text)
        {
            MarkInputChangedSinceAnalysis();
            // 杭要素分割は保持する。沈下解析の結果は入力の中にあるので従来どおり破棄する
            // (沈下解析を使っていなければダイアログは出ない)。
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: false, reason: text);
        }

        /// <summary>
        /// 材料・断面の変更時など、確認ダイアログなしで解析結果を自動削除する。
        /// 解析結果が存在する場合のみリセットを実行する。
        /// </summary>
        public void ResetAnalysisResultsSilently()
        {
            // フラグが全部 false でも、結果セットや入力モデル内の沈下結果が残っていることがある
            // (フラグだけ消す経路が過去にあったため)。残っていたら消す。
            if (!IsHorizontalAnalysisDone && !IsVerticalAnalysisDone
                && !IsGroupPileSettlementAnalysisDone && !IsVerticalBeamAnalysisDone
                && !HasAnalysisResultSet && !HasSettlementResults())
                return;

            ClearAllAnalysisState(includeElementSplit: true);
            UpdateWindowImmediate();
        }

        [RelayCommand]
        private void DeleteAnalysisResults()
        {
            // ユーザーが Yes を選んだらリセットを行う（従来挙動を踏襲）
            if (!ConfirmDeleteAnalysisModel(message: "解析結果を削除します。よろしいですか？", caption: "確認", icon: MessageBoxImage.Question, resetModel: true))
                return;
        }
    }
}