using CommunityToolkit.Mvvm.Input;
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
                // 解析関連フラグをリセット
                IsElementSplit = false;
                IsHorizontalAnalysisDone = false;
                IsVerticalAnalysisDone = false;
                IsGroupPileSettlementAnalysisDone = false;
                IsVerticalBeamAnalysisDone = false;
                IsAnalysisResultVisible = false;

                // 土層沈下 (反復) の CaseRecord も破棄 (杭配置/軸力/基礎梁の変更で結果無効化のため)
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs?.CaseRecords != null && pgs.CaseRecords.Count > 0)
                {
                    pgs.CaseRecords.Clear();
                    pgs.ActiveCaseIndex = -1;
                    pgs.SettlementGridData = [];
                    if (CurrentInputModel?.PileLayoutItems != null)
                        foreach (var pile in CurrentInputModel.PileLayoutItems) pile.GroupPileSettlement = 0;
                    OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
                    OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
                    OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
                    OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
                    OnPropertyChanged(nameof(GroupSettlementRouteOptions));
                    OnPropertyChanged(nameof(GroupSettlementRouteSelector));
                }

                // AnaModel の破棄
                CurrentModel = null;

                // 解析結果セット (解析時の入力スナップショット) も破棄する。
                // これを残すと ResultInputModel が解析時の入力を返し続け、
                // 結果を消したのにステータスバーやグラフの基準切替が残る。
                ClearAnalysisResultSetState();

                // 表示の更新
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

            if (IsElementSplit)
            {
                return ConfirmResetElementSplitOnly("杭要素分割内容が削除されます。続けますか？");
            }
            return true; // 操作を続ける
        }

        /// <summary>
        /// 杭要素分割のみを確認のうえ無効化する（解析結果には触れない）。
        /// 併せて、杭配置や基礎梁の変更で無効になる土層沈下（反復）の CaseRecord も破棄する。
        /// これらは InputModel 側に持つ「入力に紐づく結果」なので、現在の入力からは消すが、
        /// 解析結果セットのスナップショットには残るため結果表示は保たれる。
        /// </summary>
        private bool ConfirmResetElementSplitOnly(string message)
        {
            var result = MessageService.Show(message, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return false;

            IsElementSplit = false;
            ClearGroupSettlementCaseRecords();
            UpdateWindowImmediate();
            return true;
        }

        /// <summary>土層沈下（反復）の CaseRecord を現在の入力から破棄する。</summary>
        private void ClearGroupSettlementCaseRecords()
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

            pgs.CaseRecords.Clear();
            pgs.ActiveCaseIndex = -1;
            pgs.SettlementGridData = [];
            if (CurrentInputModel?.PileLayoutItems != null)
                foreach (var pile in CurrentInputModel.PileLayoutItems) pile.GroupPileSettlement = 0;
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
            // 解析結果は破棄しない (CheckAndResetAnalysisResults と同じ方針)。
            MarkInputChangedSinceAnalysis();
            if (!IsElementSplit) return true;

            return ConfirmResetElementSplitOnly(
                $"{reason}により、杭要素分割がキャンセルされます。\nよろしいですか？");
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
            return true;
        }

        /// <summary>
        /// 材料・断面の変更時など、確認ダイアログなしで解析結果を自動削除する。
        /// 解析結果が存在する場合のみリセットを実行する。
        /// </summary>
        public void ResetAnalysisResultsSilently()
        {
            if (!IsHorizontalAnalysisDone && !IsVerticalAnalysisDone && !IsGroupPileSettlementAnalysisDone && !IsVerticalBeamAnalysisDone)
                return;

            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            IsAnalysisResultVisible = false;
            CurrentModel = null;

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