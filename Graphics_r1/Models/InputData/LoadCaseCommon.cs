using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class LoadCaseCommon : BaseModel
    {
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
            set => SetProperty(ref _upperMassForce, value);
        }

        private double _foundationMassForce;
        public double FoundationMassForce
        {
            get => _foundationMassForce;
            set => SetProperty(ref _foundationMassForce, value);
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

        private Point3D _forceActionPoint;
        public Point3D ForceActionPoint
        {
            get => _forceActionPoint;
            set => SetProperty(ref _forceActionPoint, value);
        }

        public ICommand LoadCase1CommonIsSoilNonLinearCommand { get; }
        public ICommand LoadCase1CommonIsPileNonLinearCommand { get; }
        public ICommand LoadCase1CommonForceActionPointXCommand { get; }
        public ICommand LoadCase1CommonForceActionPointYCommand { get; }
        public ICommand LoadCase1CommonForceActionPointZCommand { get; }
        public ICommand LoadCase1CommonUpperMassForceCommand { get; }
        public ICommand LoadCase1CommonFoundationMassForceCommand { get; }

        public ICommand LoadCase2CommonIsSoilNonLinearCommand { get; }
        public ICommand LoadCase2CommonIsPileNonLinearCommand { get; }
        public ICommand LoadCase2CommonForceActionPointXCommand { get; }
        public ICommand LoadCase2CommonForceActionPointYCommand { get; }
        public ICommand LoadCase2CommonForceActionPointZCommand { get; }
        public ICommand LoadCase2CommonUpperMassForceCommand { get; }
        public ICommand LoadCase2CommonFoundationMassForceCommand { get; }

        // コンストラクタ
        public LoadCaseCommon
            (bool isSoilNonLinear, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce,
                         double forceActionPointX, double forceActionPointY, double forceActionPointAltitude)
        {
            IsSoilNonLinear = isSoilNonLinear;
            IsPileNonLinear = isPileNonLinear;
            UpperMassForce = upperMassForce;
            FoundationMassForce = foundationMassForce;
            ForceActionPointX = forceActionPointX;
            ForceActionPointY = forceActionPointY;
            ForceActionPointAltitude = forceActionPointAltitude;
            ForceActionPoint = new Point3D(forceActionPointX, forceActionPointY, forceActionPointAltitude);
        }

        // 深いコピーを作成するメソッド
        public LoadCaseCommon DeepCopy()
        {
            return (LoadCaseCommon)this.MemberwiseClone();
        }
    }
}
