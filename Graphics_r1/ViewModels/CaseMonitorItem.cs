using CommunityToolkit.Mvvm.ComponentModel;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 並列実行中のケースの進捗を表すアイテム。
    /// ParallelMonitorWindow に ItemsControl で一覧表示する。
    /// ObservableObject なので個別プロパティ変更時に UI が自動更新される。
    /// </summary>
    public partial class CaseMonitorItem : ObservableObject
    {
        [ObservableProperty]
        private string caseTag = string.Empty;

        [ObservableProperty]
        private int currentStep;

        [ObservableProperty]
        private int totalSteps;

        [ObservableProperty]
        private int currentIteration;

        /// <summary>補足情報（残差値、フェーズ等、将来拡張用）。</summary>
        [ObservableProperty]
        private string statusText = string.Empty;

        public CaseMonitorItem(string caseTag, int totalSteps)
        {
            CaseTag = caseTag;
            TotalSteps = totalSteps;
        }
    }
}
