using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesign.Models.InputData
{
    public class LoadCombination(int no, double alpha1, double beta1, double beta2) : INotifyPropertyChanged
    {
        private bool _isApplicable = true;
        public bool IsApplicable
        {
            get => _isApplicable;
            set
            {
                if (_isApplicable != value)
                {
                    _isApplicable = value;
                    OnPropertyChanged(nameof(IsApplicable));
                }
            }
        }

        private int _no = no;
        public int No
        {
            get => _no;
            set
            {
                if (_no != value)
                {
                    _no = value;
                    OnPropertyChanged(nameof(No));
                }
            }
        }

        private double _alpha1 = alpha1;
        public double Alpha1
        {
            get => _alpha1;
            set
            {
                if (_alpha1 != value)
                {
                    _alpha1 = value;
                    OnPropertyChanged(nameof(Alpha1));
                }
            }
        }
        private double _beta1 = beta1;
        public double Beta1
        {
            get => _beta1;
            set
            {
                if (_beta1 != value)
                {
                    _beta1 = value;
                    OnPropertyChanged(nameof(Beta1));
                }
            }
        }


        private double _beta2 = beta2;
        public double Beta2
        {
            get => _beta2;
            set
            {
                if (_beta2 != value)
                {
                    _beta2 = value;
                    OnPropertyChanged(nameof(Beta2));
                }
            }
        }



        public string Name => "αL:" + Alpha1.ToString("F2") + "/" + "βU:" + Beta1.ToString("F2") + "/" + "βL:" + Beta2.ToString("F2");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        public string GetName()
        {
            return Alpha1.ToString("F2") + "/" + Beta1.ToString("F2") + "/" + Beta2.ToString("F2");
        }

        // 深いコピーを作成するメソッド
        public LoadCombination DeepCopy()
        {
            return (LoadCombination)this.MemberwiseClone();
        }
    }

    // 名前からLoadCombinationを返すメソッド
    public static class LoadCombinations
    {
        public static LoadCombination GetLoadCombination(ObservableCollection<LoadCombination> loadCombinations, string name)
        {
            foreach (var loadCombination in loadCombinations)
            {
                if (name == loadCombination.GetName())
                {
                    return loadCombination;
                }
            }
            return null;
        }
    }
}


