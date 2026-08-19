using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class LoadCaseCommon : BaseModel
    {
        private SoilNonlinearityMode _soilNonlinearityMode = SoilNonlinearityMode.KhReductionWithPy;
        /// <summary>「適用」ボタンで全荷重ケースへ一括設定する地盤非線形性の段階。</summary>
        public SoilNonlinearityMode SoilNonlinearityMode
        {
            get => _soilNonlinearityMode;
            set
            {
                if (SetProperty(ref _soilNonlinearityMode, value))
                    OnPropertyChanged(nameof(IsSoilNonLinear));
            }
        }

        /// <summary>旧 API 互換。<see cref="LoadCase.IsSoilNonLinear"/> と同じマッピング。</summary>
        [JsonIgnore]
        public bool IsSoilNonLinear
        {
            get => _soilNonlinearityMode.IsNonLinear();
            set => SoilNonlinearityMode = value
                ? SoilNonlinearityMode.KhReductionWithPy
                : SoilNonlinearityMode.Linear;
        }

        /// <summary>旧 JSON 読込用シム (<see cref="LoadCase.LegacyIsSoilNonLinear"/> と同じ役割)。</summary>
        [JsonPropertyName("IsSoilNonLinear")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyIsSoilNonLinear
        {
            get => null;
            set { if (value.HasValue) IsSoilNonLinear = value.Value; }
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

        public ICommand LoadCase1CommonSoilNonlinearityModeCommand { get; }
        public ICommand LoadCase1CommonIsPileNonLinearCommand { get; }
        public ICommand LoadCase1CommonForceActionPointXCommand { get; }
        public ICommand LoadCase1CommonForceActionPointYCommand { get; }
        public ICommand LoadCase1CommonForceActionPointZCommand { get; }
        public ICommand LoadCase1CommonUpperMassForceCommand { get; }
        public ICommand LoadCase1CommonFoundationMassForceCommand { get; }

        public ICommand LoadCase2CommonSoilNonlinearityModeCommand { get; }
        public ICommand LoadCase2CommonIsPileNonLinearCommand { get; }
        public ICommand LoadCase2CommonForceActionPointXCommand { get; }
        public ICommand LoadCase2CommonForceActionPointYCommand { get; }
        public ICommand LoadCase2CommonForceActionPointZCommand { get; }
        public ICommand LoadCase2CommonUpperMassForceCommand { get; }
        public ICommand LoadCase2CommonFoundationMassForceCommand { get; }

        // コンストラクタ
        public LoadCaseCommon
            (SoilNonlinearityMode soilNonlinearityMode, bool isPileNonLinear,
                         double upperMassForce, double foundationMassForce,
                         double forceActionPointX, double forceActionPointY, double forceActionPointAltitude)
        {
            SoilNonlinearityMode = soilNonlinearityMode;
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
