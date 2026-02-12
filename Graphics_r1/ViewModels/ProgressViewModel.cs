using CommunityToolkit.Mvvm.ComponentModel;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 進捗ウィンドウのViewModel
    /// </summary>
    public partial class ProgressViewModel : ObservableObject
    {
        [ObservableProperty]
        private double _percentage;

        [ObservableProperty]
        private string _currentStep = "計算を開始しています...";

        [ObservableProperty]
        private string _percentageText = "0.0% (0/0)";

        [ObservableProperty]
        private string _estimatedRemainingTimeText = "計算中...";

        [ObservableProperty]
        private bool _canCancel = true;
    }
}
