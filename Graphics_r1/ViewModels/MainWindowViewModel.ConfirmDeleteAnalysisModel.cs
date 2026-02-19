using CommunityToolkit.Mvvm.Input;
using System.Windows;

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
            var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, icon);
            if (result != MessageBoxResult.Yes) return false;

            if (resetModel)
            {
                // 解析関連フラグをリセット
                IsElementSplit = false;
                IsHorizontalAnalysisDone = false;
                IsVerticalAnalysisDone = false;
                IsGroupPileSettlementAnalysisDone = false;
                IsAnalysisResultVisible = false;

                // AnaModel の破棄
                CurrentModel = null;

                // 表示の更新
                UpdateWindowImmediate();
                UpdateTreeView();
            }

            return true;
        }

        // 置換: 解析結果の削除確認（以前の実装と同等の動作をヘルパ経由で）
        private bool CheckAndResetAnalysisResults()
        {
            if (IsHorizontalAnalysisDone || IsVerticalAnalysisDone)
            {
                // 既存と同等のメッセージ／アイコンを渡してヘルパを呼ぶ
                string msg = "要素分割内容、水平解析結果、単杭沈下解析結果が削除されます。続けますか？";
                return ConfirmDeleteAnalysisModel(message: msg, caption: "確認", icon: MessageBoxImage.Warning, resetModel: true);
            }
            return true; // 操作を続ける
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