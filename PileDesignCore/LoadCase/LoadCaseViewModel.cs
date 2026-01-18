using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesignCore

{

    [Serializable]
    public class LoadCaseBase : INotifyPropertyChanged
    {
        public bool IsApplicable { get; set; }
        public int LoadCaseNumber { get; set; }
        public string LoadName { get; set; }
        public double LoadAngle { get; set; }
        public bool IsSoilNonLinear { get; set; }
        public bool IsPileNonLinear { get; set; }
        public double UpperMassForce { get; set; }
        public double FoundationMassForce { get; set; }
        public double ForceActionPointX { get; set; }
        public double ForceActionPointY { get; set; }

        // 共通コンストラクタ
        protected LoadCaseBase(bool isApplicable, int loadCaseNumber, string loadName, double loadAngle,
                             bool isSoilNonLinear, bool isPileNonLinear,
                             double upperMassForce, double foundationMassForce,
                             double forceActionPointX, double forceActionPointY)
        {
            IsApplicable = isApplicable;
            LoadCaseNumber = loadCaseNumber;
            LoadName = loadName;
            LoadAngle = loadAngle;
            IsSoilNonLinear = isSoilNonLinear;
            IsPileNonLinear = isPileNonLinear;
            UpperMassForce = upperMassForce;
            FoundationMassForce = foundationMassForce;
            ForceActionPointX = forceActionPointX;
            ForceActionPointY = forceActionPointY;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    [Serializable]
    public class LoadCase1 : LoadCaseBase
    {
        public LoadCase1(bool isApplicable, int loadCaseNumber, string loadName, double loadAngle,
                         bool isSoilNonLinear, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce, double forceActionPointX,
                         double forceActionPointY)
            : base(isApplicable, loadCaseNumber, loadName, loadAngle, isSoilNonLinear, isPileNonLinear,
                   upperMassForce, foundationMassForce, forceActionPointX, forceActionPointY)
        {
        }
    }

    [Serializable]
    public class LoadCase2 : LoadCaseBase
    {
        public LoadCase2(bool isApplicable, int loadCaseNumber, string loadName, double loadAngle,
                         bool isSoilNonLinear, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce, double forceActionPointX,
                         double forceActionPointY)
            : base(isApplicable, loadCaseNumber, loadName, loadAngle, isSoilNonLinear, isPileNonLinear,
                   upperMassForce, foundationMassForce, forceActionPointX, forceActionPointY)
        {
        }
    }

    [Serializable]
    public class CommonLoadCaseBase
    {
        public bool IsSoilNonLinear { get; set; }
        public bool IsPileNonLinear { get; set; }
        public double UpperMassForce { get; set; }
        public double FoundationMassForce { get; set; }
        public double ForceActionPointX { get; set; }
        public double ForceActionPointY { get; set; }

        // 共通コンストラクタ
        protected CommonLoadCaseBase(bool isSoilNonLinear, bool isPileNonLinear,
                                   double upperMassForce, double foundationMassForce,
                                   double forceActionPointX, double forceActionPointY)
        {
            IsSoilNonLinear = isSoilNonLinear;
            IsPileNonLinear = isPileNonLinear;
            UpperMassForce = upperMassForce;
            FoundationMassForce = foundationMassForce;
            ForceActionPointX = forceActionPointX;
            ForceActionPointY = forceActionPointY;
        }
    }

    [Serializable]
    public class CommonLoadCase1 : CommonLoadCaseBase
    {
        public CommonLoadCase1(bool isSoilNonLinear, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce,
                         double forceActionPointX, double forceActionPointY)
            : base(isSoilNonLinear, isPileNonLinear, upperMassForce, foundationMassForce,
                   forceActionPointX, forceActionPointY)
        {
        }
    }

    [Serializable]
    public class CommonLoadCase2 : CommonLoadCaseBase
    {
        public CommonLoadCase2(bool isSoilNonLinear, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce,
                         double forceActionPointX, double forceActionPointY)
            : base(isSoilNonLinear, isPileNonLinear, upperMassForce, foundationMassForce,
                   forceActionPointX, forceActionPointY)
        {
        }
    }

    [Serializable]
    public class LoadCaseViewModel : BaseViewModel
    {
        public ObservableCollection<LoadCase1> LoadCases1 { get; set; }
        public ObservableCollection<LoadCase2> LoadCases2 { get; set; }
        public ObservableCollection<CommonLoadCase1> CommonLoadCases1 { get; set; }
        public ObservableCollection<CommonLoadCase2> CommonLoadCases2 { get; set; }

        public ObservableCollection<string> AllLoadCases { get; set; }
        // コンストラクタ
        public LoadCaseViewModel()
        {
            LoadCases1 = new ObservableCollection<LoadCase1>();
            LoadCases2 = new ObservableCollection<LoadCase2>();
            CommonLoadCases1 = new ObservableCollection<CommonLoadCase1>();
            CommonLoadCases2 = new ObservableCollection<CommonLoadCase2>();

            LoadCases1.Add(new LoadCase1(false, 1, "+E1", 0.0, false, false, 1000.0, 1000.0, 10.0, 10.0));
            LoadCases1.Add(new LoadCase1(false, 2, "+E2", 90.0, false, false, 1000.0, 1000.0, 10.0, 10.0));
            LoadCases1.Add(new LoadCase1(false, 3, "-E1", 180.0, false, false, 1000.0, 1000.0, 10.0, 10.0));
            LoadCases1.Add(new LoadCase1(false, 4, "-E2", 270.0, false, false, 1000.0, 1000.0, 10.0, 10.0));

            LoadCases2.Add(new LoadCase2(false, 1, "U1", 0.0, false, false, 2000.0, 2000.0, 10.0, 10.0));
            LoadCases2.Add(new LoadCase2(false, 2, "U2", 90.0, false, false, 2000.0, 2000.0, 10.0, 10.0));
            LoadCases2.Add(new LoadCase2(false, 3, "U5", 180.0, false, false, 2000.0, 2000.0, 10.0, 10.0));
            LoadCases2.Add(new LoadCase2(false, 4, "U6", 270.0, false, false, 2000.0, 2000.0, 10.0, 10.0));

            CommonLoadCases1.Add(new CommonLoadCase1(false, false, 1000.0, 1000.0, 10.0, 10.0));
            CommonLoadCases2.Add(new CommonLoadCase2(false, false, 2000.0, 2000.0, 10.0, 10.0));

            SetAllLoadCases();

        }

        public void SetAllLoadCases()
        {
            AllLoadCases = new ObservableCollection<string>(); // AllLoadCases の初期化

            ObservableCollection<LoadCase1> loadCases1 = LoadCases1;
            ObservableCollection<LoadCase2> loadCases2 = LoadCases2;

            AllLoadCases.Add("VL");
            AllLoadCases.Add("VLadd");
            foreach (LoadCase1 loadCase1 in loadCases1)
            {
                AllLoadCases.Add(loadCase1.LoadName);
            }
            foreach (LoadCase2 loadCase2 in loadCases2)
            {
                AllLoadCases.Add(loadCase2.LoadName);
            }
        }

    }
}


