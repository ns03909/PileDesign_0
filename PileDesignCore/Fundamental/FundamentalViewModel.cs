using System;
using System.Collections.ObjectModel;


namespace PileDesignCore
{
    [Serializable]
    public class FundamentalViewModel : BaseViewModel
    // FundamentalViewModel変数の初期値
    {
        private string _refLevel = "TP";

        public string RefLevel
        {
            get => _refLevel;
            set => SetProperty(ref _refLevel, value);
        }

        private double _loadCombinationFactor = 1.00;

        public double LoadCombinationFactor
        {
            get => _loadCombinationFactor;
            set => SetProperty(ref _loadCombinationFactor, value);
        }

        private ObservableCollection<LoadCombinaiton> _loadCombinations = new ObservableCollection<LoadCombinaiton>();
        public ObservableCollection<LoadCombinaiton> LoadCombinations
        {
            get => _loadCombinations;
            set => SetProperty(ref _loadCombinations, value);
        }
    }

    public class LoadCombinaiton
    {
        public double Alpha1 { get; private set; }
        public double Beta1 { get; private set; }
        public double Beta2 { get; private set; }

        public LoadCombinaiton(double alpha1, double beta1, double beta2)
        {
            Alpha1 = alpha1;
            Beta1 = beta1;
            Beta2 = beta2;
        }
    }
}