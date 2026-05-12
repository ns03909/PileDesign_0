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

                // 表示の更新
                UpdateWindowImmediate();
            }

            return true;
        }

        // 置換: 解析結果の削除確認（以前の実装と同等の動作をヘルパ経由で）
        private bool CheckAndResetAnalysisResults()
        {
            bool hasBeamAware = HasGroupSettlementBeamAwareCases;
            if (IsHorizontalAnalysisDone || IsVerticalAnalysisDone || hasBeamAware)
            {
                string parts = "杭要素分割内容、水平解析結果、単杭沈下解析結果";
                if (hasBeamAware) parts += "、土層沈下解析（反復）結果";
                string msg = parts + "が削除されます。続けますか？";
                return ConfirmDeleteAnalysisModel(message: msg, caption: "確認", icon: MessageBoxImage.Warning, resetModel: true);
            }
            if (IsElementSplit)
            {
                string msg = "杭要素分割内容が削除されます。続けますか？";
                return ConfirmDeleteAnalysisModel(message: msg, caption: "確認", icon: MessageBoxImage.Warning, resetModel: true);
            }
            return true; // 操作を続ける
        }

        /// <summary>
        /// 基本設定の Z=0 標高など、ジオメトリに影響する変更時に呼ぶ。
        /// 解析結果と杭要素分割の両方がキャンセル対象。
        /// 解析結果も杭要素分割も無ければダイアログなしで true。
        /// </summary>
        public bool ConfirmResetAllForGeometryChange(string reason)
        {
            bool hasResults = IsHorizontalAnalysisDone || IsVerticalAnalysisDone
                              || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone
                              || HasGroupSettlementBeamAwareCases;
            if (!hasResults && !IsElementSplit) return true;

            string msg = $"{reason}により、";
            if (hasResults && IsElementSplit) msg += "解析結果と杭要素分割が";
            else if (hasResults) msg += "解析結果が";
            else msg += "杭要素分割が";
            msg += "キャンセルされます。\nよろしいですか？";

            return ConfirmDeleteAnalysisModel(message: msg, caption: "確認", icon: MessageBoxImage.Warning, resetModel: true);
        }

        /// <summary>
        /// 荷重条件など、ジオメトリを変更しない編集で呼ぶ確認ヘルパ。
        /// 解析結果のみリセットし、杭要素分割 (IsElementSplit) は保持する。
        /// 解析結果がなければダイアログを出さず true を返す (編集続行)。
        /// </summary>
        public bool CheckAndResetAnalysisResultsKeepingSplit(string text)
        {
            bool hasResults = IsHorizontalAnalysisDone || IsVerticalAnalysisDone
                              || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone
                              || HasGroupSettlementBeamAwareCases;
            if (!hasResults) return true;

            MessageBoxResult result = MessageService.Show(
                $"{text}を確定するには、既存の解析結果を削除する必要があります。\nよろしいですか。\n（杭要素分割は保持されます）",
                "確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return false;

            // 解析結果のみリセット、IsElementSplit は保持
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            IsAnalysisResultVisible = false;
            CurrentModel = null;

            // 土層沈下 (反復) の CaseRecord も破棄
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

            UpdateWindowImmediate();
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