using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace PileDesign.ViewModels
{
    public partial class MoveCopyViewModel : BaseViewModel
    {
        // IsCopySelected / IsMoveSelected は相互排他（どちらかが true なら他方は false）
        [ObservableProperty]
        private bool _isCopySelected;

        partial void OnIsCopySelectedChanged(bool value)
        {
            if (value) IsMoveSelected = false;
        }

        [ObservableProperty]
        private bool _isMoveSelected = true;

        partial void OnIsMoveSelectedChanged(bool value)
        {
            if (value) IsCopySelected = false;
        }

        [ObservableProperty]
        private double _dX;

        [ObservableProperty]
        private double _dY;

        [ObservableProperty]
        private double _dZ;

        // RepetitionNumber: 自然数以外は拒否する必要があるため手書きの setter を維持
        private int _repetitionNumber = 1;
        public int RepetitionNumber
        {
            get => _repetitionNumber;
            set
            {
                if (value <= 0)
                {
                    // 入力された値が自然数でない場合
                    MessageBox.Show("回数は自然数で入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 自然数の場合は値をセット
                SetProperty(ref _repetitionNumber, value);
            }
        }

        [ObservableProperty]
        private bool _isInputNodesIncluded = true;

        [ObservableProperty]
        private bool _isPileLayoutIncluded = true;

        internal void ResetStatus()
        {
            DX = 0;
            DY = 0;
            DZ = 0;
            RepetitionNumber = 1;
            IsInputNodesIncluded = true;
            IsPileLayoutIncluded = true;
        }
    }
}
