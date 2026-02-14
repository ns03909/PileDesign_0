using System.Windows;

namespace PileDesign.ViewModels
{
    public class MoveCopyViewModel : BaseViewModel
    {
        private bool _isCopySelected;
        public bool IsCopySelected
        //{
        //    get => _isCopySelected;
        //    set => SetProperty(ref _isCopySelected, value);
        //}
        {
            get => _isCopySelected;
            set
            {
                if (SetProperty(ref _isCopySelected, value))
                {
                    // IsCopySelected が変更された場合、IsMoveSelected を反転
                    if (value) IsMoveSelected = false;
                    OnPropertyChanged(nameof(IsCopySelected));
                }
            }
        }

        private bool _isMoveSelected = true;
        public bool IsMoveSelected
        //{
        //    get => _isMoveSelected;
        //    set => SetProperty(ref _isMoveSelected, value);
        //}
        {
            get => _isMoveSelected;
            set
            {
                if (SetProperty(ref _isMoveSelected, value))
                {
                    // IsMoveSelected が変更された場合、IsCopySelected を反転
                    if (value) IsCopySelected = false;
                    OnPropertyChanged(nameof(IsMoveSelected));
                }
            }
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

        private bool _isInputNodesIncluded = true;
        public bool IsInputNodesIncluded
        {
            get => _isInputNodesIncluded;
            set => SetProperty(ref _isInputNodesIncluded, value);
        }

        private bool _isPileLayoutIncluded = true;
        public bool IsPileLayoutIncluded
        {
            get => _isPileLayoutIncluded;
            set => SetProperty(ref _isPileLayoutIncluded, value);
        }

        internal void ResetStatus()
        {
            DX = 0;
            DY = 0;
            RepetitionNumber = 1;
            IsInputNodesIncluded = true;
            IsPileLayoutIncluded = true;
        }


    }
}


