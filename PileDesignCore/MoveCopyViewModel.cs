using System;
using System.Windows;

namespace PileDesignCore
{
    [Serializable]
    public class MoveCopyViewModel: BaseViewModel
    {
        private bool _isCopySelected;
        public bool IsCopySelected
        {
            get => _isCopySelected;
            set => SetProperty(ref _isCopySelected, value);
        }

        private bool _isMoveSelected = true;
        public bool IsMoveSelected
        {
            get => _isMoveSelected;
            set => SetProperty(ref _isMoveSelected, value);
        }

        private double _dX;
        public double DX
        {
            get => _dX;
            set => SetProperty(ref _dX, value);
        }

        private double _dY;
        public double DY
        {
            get => _dY;
            set => SetProperty(ref _dY, value);
        }

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

        internal void ResetStatus()
        {
            DX = 0;
            DY = 0;
            RepetitionNumber = 1;
        }

        // コンストラクタ
        public MoveCopyViewModel()
        {
        }
    }
}


