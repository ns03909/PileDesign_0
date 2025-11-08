using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    internal class EditPileLayoutViewModel : BaseViewModel
    {
        //public InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        private bool _isApplicablePileRefNo;
        public bool IsApplicablePileRefNo
        {
            get => _isApplicablePileRefNo;
            set => SetProperty(ref _isApplicablePileRefNo, value);
        }

        private ObservableCollection<int> _pileRefNos;
        public ObservableCollection<int> PileRefNos
        {
            get => _pileRefNos;
            set => SetProperty(ref _pileRefNos, value);
        }

        private int _selectedPileRefNo;
        public int SelectedPileRefNo
        {
            get => _selectedPileRefNo;
            set => SetProperty(ref _selectedPileRefNo, value);
        }

        private bool _isApplicableGroundRefNo;
        public bool IsApplicableGroundRefNo
        {
            get => _isApplicableGroundRefNo;
            set => SetProperty(ref _isApplicableGroundRefNo, value);
        }

        private ObservableCollection<int> _groundRefNos;
        public ObservableCollection<int> GroundRefNos
        {
            get => _groundRefNos;
            set => SetProperty(ref _groundRefNos, value);
        }

        private int _selectedGroundRefNo;
        public int SelectedGroundRefNo
        {
            get => _selectedGroundRefNo;
            set => SetProperty(ref _selectedGroundRefNo, value);
        }

        // コンストラクタ
        public EditPileLayoutViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
            PileRefNos = InputModel.PileBodiesCountList;
            GroundRefNos = InputModel.GroundsInputCountList;
            PileGroupFactor = 1.0;
            ResetStatus();
        }

        // PileTopLevel
        private bool _isApplicablePileTopLevel;
        public bool IsApplicablePileTopLevel
        {
            get => _isApplicablePileTopLevel;
            set => SetProperty(ref _isApplicablePileTopLevel, value);
        }

        private double _pileTopLevel;
        public double PileTopLevel
        {
            get => _pileTopLevel;
            set => SetProperty(ref _pileTopLevel, value);
        }

        private bool _isReplacePileTopLevel = true;
        public bool IsReplacePileTopLevel
        {
            get => _isReplacePileTopLevel;
            set => SetProperty(ref _isReplacePileTopLevel, value);
        }

        private bool _isAddPileTopLevel;
        public bool IsAddPileTopLevel
        {
            get => _isAddPileTopLevel;
            set => SetProperty(ref _isAddPileTopLevel, value);
        }

        // PileGroupFactor
        private bool _isApplicablePileGroupFactor;
        public bool IsApplicablePileGroupFactor
        {
            get => _isApplicablePileGroupFactor;
            set => SetProperty(ref _isApplicablePileGroupFactor, value);
        }

        private double _pileGroupFactor;
        public double PileGroupFactor
        {
            get => _pileGroupFactor;
            set => SetProperty(ref _pileGroupFactor, value);
        }

        private bool _isReplacePileGroupFactor = true;
        public bool IsReplacePileGroupFactor
        {
            get => _isReplacePileGroupFactor;
            set => SetProperty(ref _isReplacePileGroupFactor, value);
        }

        private bool _isAddPileGroupFactor;
        public bool IsAddPileGroupFactor
        {
            get => _isAddPileGroupFactor;
            set => SetProperty(ref _isAddPileGroupFactor, value);
        }

        // VL
        private bool _isApplicableVL;
        public bool IsApplicableVL
        {
            get => _isApplicableVL;
            set => SetProperty(ref _isApplicableVL, value);
        }

        private double _VL;
        public double VL
        {
            get => _VL;
            set => SetProperty(ref _VL, value);
        }

        private bool _isReplaceVL = true;
        public bool IsReplaceVL
        {
            get => _isReplaceVL;
            set => SetProperty(ref _isReplaceVL, value);
        }

        private bool _isAddVL;
        public bool IsAddVL
        {
            get => _isAddVL;
            set => SetProperty(ref _isAddVL, value);
        }

        // VLadd
        private bool _isApplicableVLadd;
        public bool IsApplicableVLadd
        {
            get => _isApplicableVLadd;
            set => SetProperty(ref _isApplicableVLadd, value);
        }

        private double _VLadd;
        public double VLadd
        {
            get => _VLadd;
            set => SetProperty(ref _VLadd, value);
        }

        private bool _isReplaceVLadd = true;
        public bool IsReplaceVLadd
        {
            get => _isReplaceVLadd;
            set => SetProperty(ref _isReplaceVLadd, value);
        }

        private bool _isAddVLadd;
        public bool IsAddVLadd
        {
            get => _isAddVLadd;
            set => SetProperty(ref _isAddVLadd, value);
        }

        // E1_1
        private bool _isApplicableE1_1;
        public bool IsApplicableE1_1
        {
            get => _isApplicableE1_1;
            set => SetProperty(ref _isApplicableE1_1, value);
        }

        private double _E1_1;
        public double E1_1
        {
            get => _E1_1;
            set => SetProperty(ref _E1_1, value);
        }

        private bool _isReplaceE1_1 = true;
        public bool IsReplaceE1_1
        {
            get => _isReplaceE1_1;
            set => SetProperty(ref _isReplaceE1_1, value);
        }

        private bool _isAddE1_1;
        public bool IsAddE1_1
        {
            get => _isAddE1_1;
            set => SetProperty(ref _isAddE1_1, value);
        }

        // E1_2
        private bool _isApplicableE1_2;
        public bool IsApplicableE1_2
        {
            get => _isApplicableE1_2;
            set => SetProperty(ref _isApplicableE1_2, value);
        }

        private double _E1_2;
        public double E1_2
        {
            get => _E1_2;
            set => SetProperty(ref _E1_2, value);
        }

        private bool _isReplaceE1_2 = true;
        public bool IsReplaceE1_2
        {
            get => _isReplaceE1_2;
            set => SetProperty(ref _isReplaceE1_2, value);
        }

        private bool _isAddE1_2;
        public bool IsAddE1_2
        {
            get => _isAddE1_2;
            set => SetProperty(ref _isAddE1_2, value);
        }

        // E1_3
        private bool _isApplicableE1_3;
        public bool IsApplicableE1_3
        {
            get => _isApplicableE1_3;
            set => SetProperty(ref _isApplicableE1_3, value);
        }

        private double _E1_3;
        public double E1_3
        {
            get => _E1_3;
            set => SetProperty(ref _E1_3, value);
        }

        private bool _isReplaceE1_3 = true;
        public bool IsReplaceE1_3
        {
            get => _isReplaceE1_3;
            set => SetProperty(ref _isReplaceE1_3, value);
        }

        private bool _isAddE1_3;
        public bool IsAddE1_3
        {
            get => _isAddE1_3;
            set => SetProperty(ref _isAddE1_3, value);
        }

        // E1_4
        private bool _isApplicableE1_4;
        public bool IsApplicableE1_4
        {
            get => _isApplicableE1_4;
            set => SetProperty(ref _isApplicableE1_4, value);
        }

        private double _E1_4;
        public double E1_4
        {
            get => _E1_4;
            set => SetProperty(ref _E1_4, value);
        }

        private bool _isReplaceE1_4 = true;
        public bool IsReplaceE1_4
        {
            get => _isReplaceE1_4;
            set => SetProperty(ref _isReplaceE1_4, value);
        }

        private bool _isAddE1_4;
        public bool IsAddE1_4
        {
            get => _isAddE1_4;
            set => SetProperty(ref _isAddE1_4, value);
        }

        // E2_1
        private bool _isApplicableE2_1;
        public bool IsApplicableE2_1
        {
            get => _isApplicableE2_1;
            set => SetProperty(ref _isApplicableE2_1, value);
        }

        private double _E2_1;
        public double E2_1
        {
            get => _E2_1;
            set => SetProperty(ref _E2_1, value);
        }

        private bool _isReplaceE2_1 = true;
        public bool IsReplaceE2_1
        {
            get => _isReplaceE2_1;
            set => SetProperty(ref _isReplaceE2_1, value);
        }

        private bool _isAddE2_1;
        public bool IsAddE2_1
        {
            get => _isAddE2_1;
            set => SetProperty(ref _isAddE2_1, value);
        }

        // E2_2
        private bool _isApplicableE2_2;
        public bool IsApplicableE2_2
        {
            get => _isApplicableE2_2;
            set => SetProperty(ref _isApplicableE2_2, value);
        }

        private double _E2_2;
        public double E2_2
        {
            get => _E2_2;
            set => SetProperty(ref _E2_2, value);
        }

        private bool _isReplaceE2_2 = true;
        public bool IsReplaceE2_2
        {
            get => _isReplaceE2_2;
            set => SetProperty(ref _isReplaceE2_2, value);
        }

        private bool _isAddE2_2;
        public bool IsAddE2_2
        {
            get => _isAddE2_2;
            set => SetProperty(ref _isAddE2_2, value);
        }

        // E2_3
        private bool _isApplicableE2_3;
        public bool IsApplicableE2_3
        {
            get => _isApplicableE2_3;
            set => SetProperty(ref _isApplicableE2_3, value);
        }


        private double _E2_3;
        public double E2_3
        {
            get => _E2_3;
            set => SetProperty(ref _E2_3, value);
        }

        private bool _isReplaceE2_3 = true;
        public bool IsReplaceE2_3
        {
            get => _isReplaceE2_3;
            set => SetProperty(ref _isReplaceE2_3, value);
        }

        private bool _isAddE2_3;
        public bool IsAddE2_3
        {
            get => _isAddE2_3;
            set => SetProperty(ref _isAddE2_3, value);
        }

        // E2_4
        private bool _isApplicableE2_4;
        public bool IsApplicableE2_4
        {
            get => _isApplicableE2_4;
            set => SetProperty(ref _isApplicableE2_4, value);
        }


        private double _E2_4;
        public double E2_4
        {
            get => _E2_4;
            set => SetProperty(ref _E2_4, value);
        }

        private bool _isReplaceE2_4 = true;
        public bool IsReplaceE2_4
        {
            get => _isReplaceE2_4;
            set => SetProperty(ref _isReplaceE2_4, value);
        }

        private bool _isAddE2_4;
        public bool IsAddE2_4
        {
            get => _isAddE2_4;
            set => SetProperty(ref _isAddE2_4, value);
        }

        // IsFrontPile1
        private bool _isApplicableIsFrontPile1;
        public bool IsApplicableIsFrontPile1
        {
            get => _isApplicableIsFrontPile1;
            set => SetProperty(ref _isApplicableIsFrontPile1, value);
        }

        private bool _isFrontPile1 = true;
        public bool IsFrontPile1
        {
            get => _isFrontPile1;
            set => SetProperty(ref _isFrontPile1, value);
        }

        private bool _isBackPile1;
        public bool IsBackPile1
        {
            get => _isBackPile1;
            set => SetProperty(ref _isBackPile1, value);
        }

        // IsFrontPile2
        private bool _isApplicableIsFrontPile2;
        public bool IsApplicableIsFrontPile2
        {
            get => _isApplicableIsFrontPile2;
            set => SetProperty(ref _isApplicableIsFrontPile2, value);
        }

        private bool _isFrontPile2 = true;
        public bool IsFrontPile2
        {
            get => _isFrontPile2;
            set => SetProperty(ref _isFrontPile2, value);
        }

        private bool _isBackPile2;
        public bool IsBackPile2
        {
            get => _isBackPile2;
            set => SetProperty(ref _isBackPile2, value);
        }

        // IsFrontPile3
        private bool _isApplicableIsFrontPile3;
        public bool IsApplicableIsFrontPile3
        {
            get => _isApplicableIsFrontPile3;
            set => SetProperty(ref _isApplicableIsFrontPile3, value);
        }

        private bool _isFrontPile3 = true;
        public bool IsFrontPile3
        {
            get => _isFrontPile3;
            set => SetProperty(ref _isFrontPile3, value);
        }

        private bool _isBackPile3;
        public bool IsBackPile3
        {
            get => _isBackPile3;
            set => SetProperty(ref _isBackPile3, value);
        }

        // IsFrontPile4
        private bool _isApplicableIsFrontPile4;
        public bool IsApplicableIsFrontPile4
        {
            get => _isApplicableIsFrontPile4;
            set => SetProperty(ref _isApplicableIsFrontPile4, value);
        }

        private bool _isFrontPile4 = true;
        public bool IsFrontPile4
        {
            get => _isFrontPile4;
            set => SetProperty(ref _isFrontPile4, value);
        }

        private bool _isBackPile4;
        public bool IsBackPile4
        {
            get => _isBackPile4;
            set => SetProperty(ref _isBackPile4, value);
        }

        // リセットステータス
        public void ResetStatus()
        {
            IsApplicablePileRefNo = false;
            SelectedPileRefNo = 1;

            IsApplicableGroundRefNo = false;
            SelectedGroundRefNo = 1;

            IsApplicablePileGroupFactor = false;
            PileGroupFactor = 1;
            IsReplacePileGroupFactor = true;
            IsAddPileGroupFactor = false;

            IsApplicableVL = false;
            VL = 0;
            IsReplaceVL = true;
            IsAddVL = false;

            IsApplicableVLadd = false;
            VLadd = 0;
            IsReplaceVLadd = true;
            IsAddVLadd = false;

            IsApplicableE1_1 = false;
            E1_1 = 0;
            IsReplaceE1_1 = true;
            IsAddE1_1 = false;

            IsApplicableE1_2 = false;
            E1_2 = 0;
            IsReplaceE1_2 = true;
            IsAddE1_2 = false;

            IsApplicableE1_3 = false;
            E1_3 = 0;
            IsReplaceE1_3 = true;
            IsAddE1_3 = false;

            IsApplicableE1_4 = false;
            E1_4 = 0;
            IsReplaceE1_4 = true;
            IsAddE1_4 = false;

            IsApplicableE2_1 = false;
            E2_1 = 0;
            IsReplaceE2_1 = true;
            IsAddE2_1 = false;

            IsApplicableE2_2 = false;
            E2_2 = 0;
            IsReplaceE2_2 = true;
            IsAddE2_2 = false;

            IsApplicableE2_3 = false;
            E2_3 = 0;
            IsReplaceE2_3 = true;
            IsAddE2_3 = false;

            IsApplicableE2_4 = false;
            E2_4 = 0;
            IsReplaceE2_4 = true;
            IsAddE2_4 = false;

            IsApplicableIsFrontPile1 = false;
            IsFrontPile1 = true;
            IsBackPile1 = false;

            IsApplicableIsFrontPile2 = false;
            IsFrontPile2 = true;
            IsBackPile2 = false;

            IsApplicableIsFrontPile3 = false;
            IsFrontPile3 = true;
            IsBackPile3 = false;

            IsApplicableIsFrontPile4 = false;
            IsFrontPile4 = true;
            IsBackPile4 = false;
        }
    }
}
