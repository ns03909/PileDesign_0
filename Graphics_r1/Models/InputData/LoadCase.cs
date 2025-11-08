using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class LoadCase : BaseModel
    {
        // MainWindowViewModelはデシリアライズ後にセットする
        private MainWindowViewModel? _mainWindowViewModel;
        public InputModel? InputModel => _mainWindowViewModel?.CurrentInputModel;

        // プロパティ
        private bool _isApplicable;
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

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private int _level;
        public int Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        private string _loadName = string.Empty;
        public string LoadName
        {
            get => _loadName;
            set => SetProperty(ref _loadName, value);
        }

        private double _loadAngle;
        public double LoadAngle
        {
            get => _loadAngle;
            set => SetProperty(ref _loadAngle, value);
        }

        private bool _isSoilNonLinear;
        public bool IsSoilNonLinear
        {
            get => _isSoilNonLinear;
            set => SetProperty(ref _isSoilNonLinear, value);
        }

        private bool _isPileNonLinear;
        public bool IsPileNonLinear
        {
            get => _isPileNonLinear;
            set => SetProperty(ref _isPileNonLinear, value);
        }

        private double _upperMassForce;
        public double UpperMassForce
        {
            get => _upperMassForce;
            set
            {
                if (_upperMassForce != value)
                {
                    _upperMassForce = value;
                    OnPropertyChanged(nameof(UpperMassForce));
                }
            }
        }

        private double _foundationMassForce;
        public double FoundationMassForce
        {
            get => _foundationMassForce;
            set
            {
                if (_foundationMassForce != value)
                {
                    _foundationMassForce = value;
                    OnPropertyChanged(nameof(FoundationMassForce));
                }
            }
        }

        private double _forceActionPointX;
        public double ForceActionPointX
        {
            get => _forceActionPointX;
            set => SetProperty(ref _forceActionPointX, value);
        }

        private double _forceActionPointY;
        public double ForceActionPointY
        {
            get => _forceActionPointY;
            set => SetProperty(ref _forceActionPointY, value);
        }

        private double _forceActionPointAltitude;
        public double ForceActionPointAltitude
        {
            get => _forceActionPointAltitude;
            set => SetProperty(ref _forceActionPointAltitude, value);
        }


        public Point3D ForceActionPoint
        {
            get => new(ForceActionPointX, ForceActionPointY, ForceActionPointAltitude);
            set
            {
                ForceActionPointX = value.X;
                ForceActionPointY = value.Y;
                ForceActionPointAltitude = value.Z;
                OnPropertyChanged();
            }
        }

        // デフォルトコンストラクタ（デシリアライズ用）
        public LoadCase() { }

        // MainWindowViewModel付きコンストラクタ（アプリ内生成用）
        public LoadCase(
            MainWindowViewModel mainWindowViewModel,
            bool isApplicable, int level, int no, string loadName, double loadAngle,
            bool isSoilNonLinear, bool isPileNonLinear,
            double upperMassForce, double foundationMassForce,
            double forceActionPointX, double forceActionPointY, double forceActionPointAltitude)
        {
            _mainWindowViewModel = mainWindowViewModel;
            IsApplicable = isApplicable;
            Level = level;
            No = no;
            LoadName = loadName;
            LoadAngle = loadAngle;
            IsSoilNonLinear = isSoilNonLinear;
            IsPileNonLinear = isPileNonLinear;
            UpperMassForce = upperMassForce;
            FoundationMassForce = foundationMassForce;
            ForceActionPointX = forceActionPointX;
            ForceActionPointY = forceActionPointY;
            ForceActionPointAltitude = forceActionPointAltitude;
        }

        // MainWindowViewModelを後からセットするメソッド
        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }

        // 重複チェック
        private bool IsDuplicateLoadName(string value)
        {
            var allLoadCases = InputModel?.LoadCasesInput.AllLoadCases;
            if (allLoadCases == null) return false;
            foreach (var loadCase in allLoadCases)
            {
                if (loadCase != this && loadCase.LoadName == value)
                    return true;
            }
            return false;
        }

        public string GetLoadName() => LoadName;

        // 深いコピー
        public LoadCase DeepCopy()
        {
            return (LoadCase)this.MemberwiseClone();
        }
    }

    public static class LoadCases
    {
        public static LoadCase? GetLoadCase(ObservableCollection<LoadCase> loadCases, string loadName)
        {
            foreach (var loadCase in loadCases)
            {
                if (loadName == loadCase.GetLoadName())
                {
                    return loadCase;
                }
            }
            return null;
        }
    }
}
