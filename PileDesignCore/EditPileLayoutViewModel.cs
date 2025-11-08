using System.Collections.ObjectModel;

namespace PileDesignCore
{
    internal class EditPileLayoutViewModel: BaseViewModel
    {

        // PileRefNo

        private bool isApplicablePileRefNo;
        public bool IsApplicablePileRefNo
        {
            get { return isApplicablePileRefNo; }
            set
            {
                if (isApplicablePileRefNo != value)
                {
                    isApplicablePileRefNo = value;
                    OnPropertyChanged(nameof(IsApplicablePileRefNo));
                }
            }
        }

        private ObservableCollection<int> pileRefNos;
        public ObservableCollection<int> PileRefNos
        {
            get { return pileRefNos; }
            set
            {
                if (pileRefNos != value)
                {
                    pileRefNos = value;
                    OnPropertyChanged(nameof(PileRefNos));
                }
            }
        }

        private int selectedPileRefNo;
        public int SelectedPileRefNo
        {
            get { return selectedPileRefNo; }
            set
            {
                if (selectedPileRefNo != value)
                {
                    selectedPileRefNo = value;
                    OnPropertyChanged(nameof(SelectedPileRefNo));
                }
            }
        }

        // GroundRefNo

        private bool isApplicableGroundRefNo;
        public bool IsApplicableGroundRefNo
        {
            get { return isApplicableGroundRefNo; }
            set
            {
                if (isApplicableGroundRefNo != value)
                {
                    isApplicableGroundRefNo = value;
                    OnPropertyChanged(nameof(IsApplicableGroundRefNo));
                }
            }
        }

        private ObservableCollection<int> groundRefNos;
        public ObservableCollection<int> GroundRefNos
        {
            get { return groundRefNos; }
            set
            {
                if (groundRefNos != value)
                {
                    groundRefNos = value;
                    OnPropertyChanged(nameof(GroundRefNos));
                }
            }
        }

        private int selectedGroundRefNo;
        public int SelectedGroundRefNo
        {
            get { return selectedGroundRefNo; }
            set
            {
                if (selectedGroundRefNo != value)
                {
                    selectedGroundRefNo = value;
                    OnPropertyChanged(nameof(SelectedGroundRefNo));
                }
            }
        }

        // PileTopLevel

        private bool isApplicablePileTopLevel;
        public bool IsApplicablePileTopLevel
        {
            get { return isApplicablePileTopLevel; }
            set
            {
                if (isApplicablePileTopLevel != value)
                {
                    isApplicablePileTopLevel = value;
                    OnPropertyChanged(nameof(IsApplicablePileTopLevel));
                }
            }
        }

        private double pileTopLevel;
        public double PileTopLevel
        {
            get { return pileTopLevel; }
            set
            {
                if (pileTopLevel != value)
                {
                    pileTopLevel = value;
                    OnPropertyChanged(nameof(PileTopLevel));
                }
            }
        }

        private bool isReplacePileTopLevel = true;
        public bool IsReplacePileTopLevel
        {
            get { return isReplacePileTopLevel; }
            set
            {
                if (isReplacePileTopLevel != value)
                {
                    isReplacePileTopLevel = value;
                    OnPropertyChanged(nameof(IsReplacePileTopLevel));
                }
            }
        }

        private bool isAddPileTopLevel;
        public bool IsAddPileTopLevel
        {
            get { return isAddPileTopLevel; }
            set
            {
                if (isAddPileTopLevel != value)
                {
                    isAddPileTopLevel = value;
                    OnPropertyChanged(nameof(IsAddPileTopLevel));
                }
            }
        }

        // PileGroupFactor

        private bool isApplicablePileGroupFactor;
        public bool IsApplicablePileGroupFactor
        {
            get { return isApplicablePileGroupFactor; }
            set
            {
                if (isApplicablePileGroupFactor != value)
                {
                    isApplicablePileGroupFactor = value;
                    OnPropertyChanged(nameof(IsApplicablePileGroupFactor));
                }
            }
        }

        private double pileGroupFactor;
        public double PileGroupFactor
        {
            get { return pileGroupFactor; }
            set
            {
                if (pileGroupFactor != value)
                {
                    pileGroupFactor = value;
                    OnPropertyChanged(nameof(PileGroupFactor));
                }
            }
        }

        private bool isReplacePileGroupFactor = true;
        public bool IsReplacePileGroupFactor
        {
            get { return isReplacePileGroupFactor; }
            set
            {
                if (isReplacePileGroupFactor != value)
                {
                    isReplacePileGroupFactor = value;
                    OnPropertyChanged(nameof(IsReplacePileGroupFactor));
                }
            }
        }

        private bool isAddPileGroupFactor;
        public bool IsAddPileGroupFactor
        {
            get { return isAddPileGroupFactor; }
            set
            {
                if (isAddPileGroupFactor != value)
                {
                    isAddPileGroupFactor = value;
                    OnPropertyChanged(nameof(IsAddPileGroupFactor));
                }
            }
        }

        // VL

        private bool isApplicableVL;
        public bool IsApplicableVL
        {
            get { return isApplicableVL; }
            set
            {
                if (isApplicableVL != value)
                {
                    isApplicableVL = value;
                    OnPropertyChanged(nameof(IsApplicableVL));
                }
            }
        }

        private double _VL;
        public double VL
        {
            get { return _VL; }
            set
            {
                if (_VL != value)
                {
                    _VL = value;
                    OnPropertyChanged(nameof(VL));
                }
            }
        }

        private bool isReplaceVL = true;
        public bool IsReplaceVL
        {
            get { return isReplaceVL; }
            set
            {
                if (isReplaceVL != value)
                {
                    isReplaceVL = value;
                    OnPropertyChanged(nameof(IsReplaceVL));
                }
            }
        }

        private bool isAddVL;
        public bool IsAddVL
        {
            get { return isAddVL; }
            set
            {
                if (isAddVL != value)
                {
                    isAddVL = value;
                    OnPropertyChanged(nameof(IsAddVL));
                }
            }
        }

        // VLadd

        private bool isApplicableVLadd;
        public bool IsApplicableVLadd
        {
            get { return isApplicableVLadd; }
            set
            {
                if (isApplicableVLadd != value)
                {
                    isApplicableVLadd = value;
                    OnPropertyChanged(nameof(IsApplicableVLadd));
                }
            }
        }

        private double _VLadd;
        public double VLadd
        {
            get { return _VLadd; }
            set
            {
                if (_VLadd != value)
                {
                    _VLadd = value;
                    OnPropertyChanged(nameof(VLadd));
                }
            }
        }

        private bool isReplaceVLadd = true;
        public bool IsReplaceVLadd
        {
            get { return isReplaceVLadd; }
            set
            {
                if (isReplaceVLadd != value)
                {
                    isReplaceVLadd = value;
                    OnPropertyChanged(nameof(IsReplaceVLadd));
                }
            }
        }

        private bool isAddVLadd;
        public bool IsAddVLadd
        {
            get { return isAddVLadd; }
            set
            {
                if (isAddVLadd != value)
                {
                    isAddVLadd = value;
                    OnPropertyChanged(nameof(IsAddVLadd));
                }
            }
        }

        // E1

        private bool isApplicableE1;
        public bool IsApplicableE1
        {
            get { return isApplicableE1; }
            set
            {
                if (isApplicableE1 != value)
                {
                    isApplicableE1 = value;
                    OnPropertyChanged(nameof(IsApplicableE1));
                }
            }
        }

        private double _E1;
        public double E1
        {
            get { return _E1; }
            set
            {
                if (_E1 != value)
                {
                    _E1 = value;
                    OnPropertyChanged(nameof(E1));
                }
            }
        }

        private bool isReplaceE1 = true;
        public bool IsReplaceE1
        {
            get { return isReplaceE1; }
            set
            {
                if (isReplaceE1 != value)
                {
                    isReplaceE1 = value;
                    OnPropertyChanged(nameof(IsReplaceE1));
                }
            }
        }

        private bool isAddE1;
        public bool IsAddE1
        {
            get { return isAddE1; }
            set
            {
                if (isAddE1 != value)
                {
                    isAddE1 = value;
                    OnPropertyChanged(nameof(IsAddE1));
                }
            }
        }

        // E2

        private bool isApplicableE2;
        public bool IsApplicableE2
        {
            get { return isApplicableE2; }
            set
            {
                if (isApplicableE2 != value)
                {
                    isApplicableE2 = value;
                    OnPropertyChanged(nameof(IsApplicableE2));
                }
            }
        }

        private double _E2;
        public double E2
        {
            get { return _E2; }
            set
            {
                if (_E2 != value)
                {
                    _E2 = value;
                    OnPropertyChanged(nameof(E2));
                }
            }
        }

        private bool isReplaceE2 = true;
        public bool IsReplaceE2
        {
            get { return isReplaceE2; }
            set
            {
                if (isReplaceE2 != value)
                {
                    isReplaceE2 = value;
                    OnPropertyChanged(nameof(IsReplaceE2));
                }
            }
        }

        private bool isAddE2;
        public bool IsAddE2
        {
            get { return isAddE2; }
            set
            {
                if (isAddE2 != value)
                {
                    isAddE2 = value;
                    OnPropertyChanged(nameof(IsAddE2));
                }
            }
        }


        // E1_1

        private bool isApplicableE1_1;
        public bool IsApplicableE1_1
        {
            get { return isApplicableE1_1; }
            set
            {
                if (isApplicableE1_1 != value)
                {
                    isApplicableE1_1 = value;
                    OnPropertyChanged(nameof(IsApplicableE1_1));
                }
            }
        }

        private double _E1_1;
        public double E1_1
        {
            get { return _E1_1; }
            set
            {
                if (_E1_1 != value)
                {
                    _E1_1 = value;
                    OnPropertyChanged(nameof(E1_1));
                }
            }
        }

        private bool isReplaceE1_1 = true;
        public bool IsReplaceE1_1
        {
            get { return isReplaceE1_1; }
            set
            {
                if (isReplaceE1_1 != value)
                {
                    isReplaceE1_1 = value;
                    OnPropertyChanged(nameof(IsReplaceE1_1));
                }
            }
        }

        private bool isAddE1_1;
        public bool IsAddE1_1
        {
            get { return isAddE1_1; }
            set
            {
                if (isAddE1_1 != value)
                {
                    isAddE1_1 = value;
                    OnPropertyChanged(nameof(IsAddE1_1));
                }
            }
        }

        // E1_2

        private bool isApplicableE1_2;
        public bool IsApplicableE1_2
        {
            get { return isApplicableE1_2; }
            set
            {
                if (isApplicableE1_2 != value)
                {
                    isApplicableE1_2 = value;
                    OnPropertyChanged(nameof(IsApplicableE1_2));
                }
            }
        }

        private double _E1_2;
        public double E1_2
        {
            get { return _E1_2; }
            set
            {
                if (_E1_2 != value)
                {
                    _E1_2 = value;
                    OnPropertyChanged(nameof(E1_2));
                }
            }
        }

        private bool isReplaceE1_2 = true;
        public bool IsReplaceE1_2
        {
            get { return isReplaceE1_2; }
            set
            {
                if (isReplaceE1_2 != value)
                {
                    isReplaceE1_2 = value;
                    OnPropertyChanged(nameof(IsReplaceE1_2));
                }
            }
        }

        private bool isAddE1_2;
        public bool IsAddE1_2
        {
            get { return isAddE1_2; }
            set
            {
                if (isAddE1_2 != value)
                {
                    isAddE1_2 = value;
                    OnPropertyChanged(nameof(IsAddE1_2));
                }
            }
        }

        // E1_3

        private bool isApplicableE1_3;
        public bool IsApplicableE1_3
        {
            get { return isApplicableE1_3; }
            set
            {
                if (isApplicableE1_3 != value)
                {
                    isApplicableE1_3 = value;
                    OnPropertyChanged(nameof(IsApplicableE1_3));
                }
            }
        }

        private double _E1_3;
        public double E1_3
        {
            get { return _E1_3; }
            set
            {
                if (_E1_3 != value)
                {
                    _E1_3 = value;
                    OnPropertyChanged(nameof(E1_3));
                }
            }
        }

        private bool isReplaceE1_3 = true;
        public bool IsReplaceE1_3
        {
            get { return isReplaceE1_3; }
            set
            {
                if (isReplaceE1_3 != value)
                {
                    isReplaceE1_3 = value;
                    OnPropertyChanged(nameof(IsReplaceE1_3));
                }
            }
        }

        private bool isAddE1_3;
        public bool IsAddE1_3
        {
            get { return isAddE1_3; }
            set
            {
                if (isAddE1_3 != value)
                {
                    isAddE1_3 = value;
                    OnPropertyChanged(nameof(IsAddE1_3));
                }
            }
        }

        // E1_4

        private bool isApplicableE1_4;
        public bool IsApplicableE1_4
        {
            get { return isApplicableE1_4; }
            set
            {
                if (isApplicableE1_4 != value)
                {
                    isApplicableE1_4 = value;
                    OnPropertyChanged(nameof(IsApplicableE1_4));
                }
            }
        }

        private double _E1_4;
        public double E1_4
        {
            get { return _E1_4; }
            set
            {
                if (_E1_4 != value)
                {
                    _E1_4 = value;
                    OnPropertyChanged(nameof(E1_4));
                }
            }
        }

        private bool isReplaceE1_4 = true;
        public bool IsReplaceE1_4
        {
            get { return isReplaceE1_4; }
            set
            {
                if (isReplaceE1_4 != value)
                {
                    isReplaceE1_4 = value;
                    OnPropertyChanged(nameof(IsReplaceE1_4));
                }
            }
        }

        private bool isAddE1_4;
        public bool IsAddE1_4
        {
            get { return isAddE1_4; }
            set
            {
                if (isAddE1_4 != value)
                {
                    isAddE1_4 = value;
                    OnPropertyChanged(nameof(IsAddE1_4));
                }
            }
        }

        // E2_1

        private bool isApplicableE2_1;
        public bool IsApplicableE2_1
        {
            get { return isApplicableE2_1; }
            set
            {
                if (isApplicableE2_1 != value)
                {
                    isApplicableE2_1 = value;
                    OnPropertyChanged(nameof(IsApplicableE2_1));
                }
            }
        }

        private double _E2_1;
        public double E2_1
        {
            get { return _E2_1; }
            set
            {
                if (_E2_1 != value)
                {
                    _E2_1 = value;
                    OnPropertyChanged(nameof(E2_1));
                }
            }
        }

        private bool isReplaceE2_1 = true;
        public bool IsReplaceE2_1
        {
            get { return isReplaceE2_1; }
            set
            {
                if (isReplaceE2_1 != value)
                {
                    isReplaceE2_1 = value;
                    OnPropertyChanged(nameof(IsReplaceE2_1));
                }
            }
        }

        private bool isAddE2_1;
        public bool IsAddE2_1
        {
            get { return isAddE2_1; }
            set
            {
                if (isAddE2_1 != value)
                {
                    isAddE2_1 = value;
                    OnPropertyChanged(nameof(IsAddE2_1));
                }
            }
        }

        // E2_2

        private bool isApplicableE2_2;
        public bool IsApplicableE2_2
        {
            get { return isApplicableE2_2; }
            set
            {
                if (isApplicableE2_2 != value)
                {
                    isApplicableE2_2 = value;
                    OnPropertyChanged(nameof(IsApplicableE2_2));
                }
            }
        }

        private double _E2_2;
        public double E2_2
        {
            get { return _E2_2; }
            set
            {
                if (_E2_2 != value)
                {
                    _E2_2 = value;
                    OnPropertyChanged(nameof(E2_2));
                }
            }
        }

        private bool isReplaceE2_2 = true;
        public bool IsReplaceE2_2
        {
            get { return isReplaceE2_2; }
            set
            {
                if (isReplaceE2_2 != value)
                {
                    isReplaceE2_2 = value;
                    OnPropertyChanged(nameof(IsReplaceE2_2));
                }
            }
        }

        private bool isAddE2_2;
        public bool IsAddE2_2
        {
            get { return isAddE2_2; }
            set
            {
                if (isAddE2_2 != value)
                {
                    isAddE2_2 = value;
                    OnPropertyChanged(nameof(IsAddE2_2));
                }
            }
        }

        // E2_3

        private bool isApplicableE2_3;
        public bool IsApplicableE2_3
        {
            get { return isApplicableE2_3; }
            set
            {
                if (isApplicableE2_3 != value)
                {
                    isApplicableE2_3 = value;
                    OnPropertyChanged(nameof(IsApplicableE2_3));
                }
            }
        }

        private double _E2_3;
        public double E2_3
        {
            get { return _E2_3; }
            set
            {
                if (_E2_3 != value)
                {
                    _E2_3 = value;
                    OnPropertyChanged(nameof(E2_3));
                }
            }
        }

        private bool isReplaceE2_3 = true;
        public bool IsReplaceE2_3
        {
            get { return isReplaceE2_3; }
            set
            {
                if (isReplaceE2_3 != value)
                {
                    isReplaceE2_3 = value;
                    OnPropertyChanged(nameof(IsReplaceE2_3));
                }
            }
        }

        private bool isAddE2_3;
        public bool IsAddE2_3
        {
            get { return isAddE2_3; }
            set
            {
                if (isAddE2_3 != value)
                {
                    isAddE2_3 = value;
                    OnPropertyChanged(nameof(IsAddE2_3));
                }
            }
        }

        // E2_4

        private bool isApplicableE2_4;
        public bool IsApplicableE2_4
        {
            get { return isApplicableE2_4; }
            set
            {
                if (isApplicableE2_4 != value)
                {
                    isApplicableE2_4 = value;
                    OnPropertyChanged(nameof(IsApplicableE2_4));
                }
            }
        }

        private double _E2_4;
        public double E2_4
        {
            get { return _E2_4; }
            set
            {
                if (_E2_4 != value)
                {
                    _E2_4 = value;
                    OnPropertyChanged(nameof(E2_4));
                }
            }
        }

        private bool isReplaceE2_4 = true;
        public bool IsReplaceE2_4
        {
            get { return isReplaceE2_4; }
            set
            {
                if (isReplaceE2_4 != value)
                {
                    isReplaceE2_4 = value;
                    OnPropertyChanged(nameof(IsReplaceE2_4));
                }
            }
        }

        private bool isAddE2_4;
        public bool IsAddE2_4
        {
            get { return isAddE2_4; }
            set
            {
                if (isAddE2_4 != value)
                {
                    isAddE2_4 = value;
                    OnPropertyChanged(nameof(IsAddE2_4));
                }
            }
        }


        // IsFrontPile1

        private bool isApplicableIsFrontPile1;
        public bool IsApplicableIsFrontPile1
        {
            get { return isApplicableIsFrontPile1; }
            set
            {
                if (isApplicableIsFrontPile1 != value)
                {
                    isApplicableIsFrontPile1 = value;
                    OnPropertyChanged(nameof(IsApplicableIsFrontPile1));
                }
            }
        }

        private bool _IsFrontPile1 = true;
        public bool IsFrontPile1
        {
            get { return _IsFrontPile1; }
            set
            {
                if (_IsFrontPile1 != value)
                {
                    _IsFrontPile1 = value;
                    OnPropertyChanged(nameof(IsFrontPile1));
                }
            }
        }

        private bool _IsBackPile1;
        public bool IsBackPile1
        {
            get { return _IsBackPile1; }
            set
            {
                if (_IsBackPile1 != value)
                {
                    _IsBackPile1 = value;
                    OnPropertyChanged(nameof(IsBackPile1));
                }
            }
        }
        // IsFrontPile2

        private bool isApplicableIsFrontPile2 = true;
        public bool IsApplicableIsFrontPile2
        {
            get { return isApplicableIsFrontPile2; }
            set
            {
                if (isApplicableIsFrontPile2 != value)
                {
                    isApplicableIsFrontPile2 = value;
                    OnPropertyChanged(nameof(IsApplicableIsFrontPile2));
                }
            }
        }

        private bool _IsFrontPile2 = true;
        public bool IsFrontPile2
        {
            get { return _IsFrontPile2; }
            set
            {
                if (_IsFrontPile2 != value)
                {
                    _IsFrontPile2 = value;
                    OnPropertyChanged(nameof(IsFrontPile2));
                }
            }
        }

        private bool _IsBackPile2;
        public bool IsBackPile2
        {
            get { return _IsBackPile2; }
            set
            {
                if (_IsBackPile2 != value)
                {
                    _IsBackPile2 = value;
                    OnPropertyChanged(nameof(IsBackPile2));
                }
            }
        }
        // IsFrontPile3

        private bool isApplicableIsFrontPile3;
        public bool IsApplicableIsFrontPile3
        {
            get { return isApplicableIsFrontPile3; }
            set
            {
                if (isApplicableIsFrontPile3 != value)
                {
                    isApplicableIsFrontPile3 = value;
                    OnPropertyChanged(nameof(IsApplicableIsFrontPile3));
                }
            }
        }

        private bool _IsFrontPile3 = true;
        public bool IsFrontPile3
        {
            get { return _IsFrontPile3; }
            set
            {
                if (_IsFrontPile3 != value)
                {
                    _IsFrontPile3 = value;
                    OnPropertyChanged(nameof(IsFrontPile3));
                }
            }
        }

        private bool _IsBackPile3;
        public bool IsBackPile3
        {
            get { return _IsBackPile3; }
            set
            {
                if (_IsBackPile3 != value)
                {
                    _IsBackPile3 = value;
                    OnPropertyChanged(nameof(IsBackPile3));
                }
            }
        }
        // IsFrontPile4

        private bool isApplicableIsFrontPile4;
        public bool IsApplicableIsFrontPile4
        {
            get { return isApplicableIsFrontPile4; }
            set
            {
                if (isApplicableIsFrontPile4 != value)
                {
                    isApplicableIsFrontPile4 = value;
                    OnPropertyChanged(nameof(IsApplicableIsFrontPile4));
                }
            }
        }

        private bool _IsFrontPile4 = true;
        public bool IsFrontPile4
        {
            get { return _IsFrontPile4; }
            set
            {
                if (_IsFrontPile4 != value)
                {
                    _IsFrontPile4 = value;
                    OnPropertyChanged(nameof(IsFrontPile4));
                }
            }
        }

        private bool _IsBackPile4;
        public bool IsBackPile4
        {
            get { return _IsBackPile4; }
            set
            {
                if (_IsBackPile4 != value)
                {
                    _IsBackPile4 = value;
                    OnPropertyChanged(nameof(IsBackPile4));
                }
            }
        }

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

            IsApplicableE1 = false;
            E1 = 0;
            IsReplaceE1 = true;
            IsAddE1 = false;

            IsApplicableE2 = false;
            E2 = 0;
            IsReplaceE2 = true;
            IsAddE2 = false;

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
