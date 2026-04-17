using CommunityToolkit.Mvvm.ComponentModel;
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


        // SumVL0 / SumVLadd / SumVL は getter で PileLayoutItems から毎回計算するため
        // [ObservableProperty] にせず手書きの get/set を維持する（set は View からの逆方向
        // バインディング許容だが常に再計算される仕様）
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

        [ObservableProperty]
        private double _buildingWeight;

        partial void OnBuildingWeightChanged(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _effectiveHeight;

        partial void OnEffectiveHeightChanged(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _shearCoefficient1;

        partial void OnShearCoefficient1Changed(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _overturningMoment1;

        [ObservableProperty]
        private double _shearCoefficient2;

        partial void OnShearCoefficient2Changed(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _overturningMoment2;

        private void SetOverturningMoment()
        {
            OverturningMoment1 = BuildingWeight * EffectiveHeight * ShearCoefficient1 * 0.001; // MNm
            OverturningMoment2 = BuildingWeight * EffectiveHeight * ShearCoefficient2 * 0.001; // MNm
        }

        [ObservableProperty]
        private bool _isApplicableE1 = true;

        partial void OnIsApplicableE1Changed(bool value) => SetApplicableAllE1();

        private void SetApplicableAllE1()
        {
            for (int i = 0; i < IsApplicableE1s.Count; i++)
            {
                IsApplicableE1s[i] = IsApplicableE1;
            }
        }

        [ObservableProperty]
        private ObservableCollection<bool> _isApplicableE1s = [true, true, true, true];

        [ObservableProperty]
        private bool _isApplicableE2 = true;

        partial void OnIsApplicableE2Changed(bool value) => SetApplicableAllE2();

        private void SetApplicableAllE2()
        {
            for (int i = 0; i < IsApplicableE2s.Count; i++)
            {
                IsApplicableE2s[i] = IsApplicableE2;
            }
        }

        [ObservableProperty]
        private ObservableCollection<bool> _isApplicableE2s = [true, true, true, true];

        [ObservableProperty]
        private bool _isApplicableVL = true;

        // コンストラクタ
        public AutoOverturningMomentViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }
    }
}
