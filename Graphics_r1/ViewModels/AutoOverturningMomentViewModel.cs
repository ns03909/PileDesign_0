using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    public partial class AutoOverturningMomentViewModel : BaseViewModel
    {
        //public InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;
        //public InputModel InputModel => _mainWindowViewModel.CurrentInputModel ?? throw new InvalidOperationException("CurrentInputModel is null. 必ず初期化してください。");


        private double _sumVL0;
        public double SumVL0
        {
            get => GetSumVL0();
            set => SetProperty(ref _sumVL0, value);
        }

        private double GetSumVL0()
        {
            double sumVL = 0.0;
            foreach (var item in InputModel.PileLayoutItems)
            {
                sumVL += item.AxialForceVL0;
            }
            return sumVL;
        }

        private double _sumVLadd;
        public double SumVLadd
        {
            get => GetSumVLadd();
            set => SetProperty(ref _sumVLadd, value);
        }

        private double GetSumVLadd()
        {
            double sumVLadd = 0.0;
            foreach (var item in InputModel.PileLayoutItems)
            {
                sumVLadd += item.AxialForceVLAdditional;
            }
            return sumVLadd;
        }

        private double _sumVL;
        public double SumVL
        {
            get => GetSumVL();
            set => SetProperty(ref _sumVL, value);
        }

        private double GetSumVL()
        {
            return GetSumVL0() + GetSumVLadd();
        }

        private double _buildingWeight;
        public double BuildingWeight
        {
            get => _buildingWeight;
            set
            {
                SetProperty(ref _buildingWeight, value);
                SetOverturningMoment();
            }
        }

        private double _effectiveHeight;
        public double EffectiveHeight
        {
            get => _effectiveHeight;
            set
            {
                SetProperty(ref _effectiveHeight, value);
                SetOverturningMoment();
            }
        }

        private double _shearCoefficient1;
        public double ShearCoefficient1
        {
            get => _shearCoefficient1;
            set
            {
                SetProperty(ref _shearCoefficient1, value);
                SetOverturningMoment();
            }
        }

        private double _overTurningMoment1;
        public double OverturningMoment1
        {
            get => _overTurningMoment1;
            set => SetProperty(ref _overTurningMoment1, value);
        }

        private double _shearCoefficient2;
        public double ShearCoefficient2
        {
            get => _shearCoefficient2;
            set
            {
                SetProperty(ref _shearCoefficient2, value);
                SetOverturningMoment();
            }
        }

        private double _overTurningMoment2;
        public double OverturningMoment2
        {
            get => _overTurningMoment2;
            set => SetProperty(ref _overTurningMoment2, value);
        }

        private void SetOverturningMoment()
        {
            OverturningMoment1 = BuildingWeight * EffectiveHeight * ShearCoefficient1 * 0.001; // MNm
            OverturningMoment2 = BuildingWeight * EffectiveHeight * ShearCoefficient2 * 0.001; // MNm
        }

        private bool _isApplicableE1 = true;
        public bool IsApplicableE1
        {
            get => _isApplicableE1;
            set
            {
                SetProperty(ref _isApplicableE1, value);
                SetApplicableAllE1();
            }
        }

        private void SetApplicableAllE1()
        {
            for (int i = 0; i < IsApplicableE1s.Count; i++)
            {
                IsApplicableE1s[i] = IsApplicableE1;
            }
        }

        private ObservableCollection<bool> _isApplicableE1s = [true, true, true, true];
        public ObservableCollection<bool> IsApplicableE1s
        {
            get => _isApplicableE1s;
            set => SetProperty(ref _isApplicableE1s, value);
        }

        private bool _isApplicableE2 = true;
        public bool IsApplicableE2
        {
            get => _isApplicableE2;
            set
            {
                SetProperty(ref _isApplicableE2, value);
                SetApplicableAllE2();
            }
        }

        private void SetApplicableAllE2()
        {
            for (int i = 0; i < IsApplicableE2s.Count; i++)
            {
                IsApplicableE2s[i] = IsApplicableE2;
            }
        }

        private ObservableCollection<bool> _isApplicableE2s = [true, true, true, true];
        public ObservableCollection<bool> IsApplicableE2s
        {
            get => _isApplicableE2s;
            set => SetProperty(ref _isApplicableE2s, value);
        }

        private bool _isApplicableVL = true;
        public bool IsApplicableVL
        {
            get => _isApplicableVL;
            set => SetProperty(ref _isApplicableVL, value);
        }

        // コンストラクタ
        public AutoOverturningMomentViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }
    }
}
