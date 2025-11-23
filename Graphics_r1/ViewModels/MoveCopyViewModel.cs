using System;
using System.Windows;
using System.Windows.Input;

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

        internal void ResetStatus()
        {
            DX = 0;
            DY = 0;
            RepetitionNumber = 1;
        }

        private bool _isNodePairSelectionMode;
        /// <summary>メイン画面で2つの節点クリックを待機中</summary>
        public bool IsNodePairSelectionMode
        {
            get => _isNodePairSelectionMode;
            set => SetProperty(ref _isNodePairSelectionMode, value);
        }

        private int? _firstNodeIndex;
        public int? FirstNodeIndex
        {
            get => _firstNodeIndex;
            set => SetProperty(ref _firstNodeIndex, value);
        }

        private int? _secondNodeIndex;
        public int? SecondNodeIndex
        {
            get => _secondNodeIndex;
            set => SetProperty(ref _secondNodeIndex, value);
        }

        public ICommand StartNodePairSelectionCommand => _startNodePairSelectionCommand ??=
            new RelayCommand(_ =>
            {
                // 初期化してモード開始
                FirstNodeIndex = null;
                SecondNodeIndex = null;
                IsNodePairSelectionMode = true;
            },
            _ => !IsNodePairSelectionMode);

        public ICommand CancelNodePairSelectionCommand => _cancelNodePairSelectionCommand ??=
            new RelayCommand(_ =>
            {
                FirstNodeIndex = null;
                SecondNodeIndex = null;
                IsNodePairSelectionMode = false;
            },
            _ => IsNodePairSelectionMode);

        private ICommand _startNodePairSelectionCommand;
        private ICommand _cancelNodePairSelectionCommand;

        /// <summary>
        /// メインウィンドウ側が節点をクリックした際に呼び出すヘルパ。
        /// 戻り値: true なら2点目まで完了しモード終了。
        /// </summary>
        public bool TryRegisterClickedNode(int nodeIndex, double x, double y)
        {
            if (!IsNodePairSelectionMode) return false;

            if (FirstNodeIndex == null)
            {
                FirstNodeIndex = nodeIndex;
                // 1点目座標を基準 → DX,DY 一旦0
                DX = 0;
                DY = 0;
                return false;
            }

            if (SecondNodeIndex == null)
            {
                SecondNodeIndex = nodeIndex;
                // 差分算出
                // 第一点座標はメイン側保持の値を渡す方式にするなら、こちらは第二点のみでOK
                // ここでは第一点座標を別途保持せず、第一点の座標を ViewModel に残さない設計にしているため
                // 差分はメイン側が計算して渡してもよい。ここでは x,y は第二点座標、第一点は内部保持しないので
                // メイン側から差分を渡す運用を採用するならシグネチャ変更が必要。
                // → 今回は第一点座標も一緒に渡すように変更する簡易案:
            }
            return SecondNodeIndex != null;
        }

        /// <summary>外部(メイン)で差分計算後に設定する API</summary>
        public void SetDiff(double dx, double dy)
        {
            DX = dx;
            DY = dy;
            IsNodePairSelectionMode = false;
        }

        // 簡易 RelayCommand （プロジェクトに既存があればそちらへ差し替え可）
        private sealed class RelayCommand : ICommand
        {
            private readonly Predicate<object> _canExecute;
            private readonly Action<object> _execute;
            public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }
            public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) != false;
            public void Execute(object parameter) => _execute(parameter);
            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}


