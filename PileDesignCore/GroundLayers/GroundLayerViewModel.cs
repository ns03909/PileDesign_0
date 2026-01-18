using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Input;


namespace PileDesignCore
{

    public class Liquefaction : BaseDataItem // 液状化
    {
        internal static bool IsLiquefactionLayer(double _groundWaterGLDepth, double _z, double Fc)
        // _groundWaterGLDepth 負の数
        // _z 負の数
        {
            if (_groundWaterGLDepth < _z)　// 地下水位より高い場合
            { return false; }
            else if (_z <= -20)
            { return false; }
            else if (Fc > 35)
            { return false; }
            else { return true; }
        }

        internal static double CalculateGammaCy(double _Na, double _TauDonSigmaZPrime)
        {
            double _gammaCy = 0.0;
            double[] _TauDonSigmaZPrimeSet = { 0.05, 0.08, 0.10, 0.15, 0.20, 0.30, 0.40, 0.50, 0.60 };
            double[] _NaSet_0005 = { 0.00, 5.75, 8.45, 15.75, 20.35, 25.00, 27.25, 28.00, 28.00 };
            double[] _NaSet_0510 = { 0.00, 5.25, 7.70, 13.00, 16.60, 20.75, 22.25, 22.75, 22.50 };
            double[] _NaSet_1020 = { 0.00, 4.75, 6.95, 11.25, 13.85, 16.50, 17.25, 17.50, 17.00 };
            double[] _NaSet_2040 = { 0.00, 4.25, 5.75, 08.50, 10.00, 11.05, 11.20, 11.10, 10.75 };
            double[] _NaSet_4080 = { 0.00, 3.20, 4.50, 05.85, 06.50, 06.55, 06.25, 06.00, 05.70 };

            double _Na_0005 = 0.0;
            double _Na_0510 = 0.0;
            double _Na_1020 = 0.0;
            double _Na_2040 = 0.0;
            double _Na_4080 = 0.0;
            if (_TauDonSigmaZPrime < _TauDonSigmaZPrimeSet[0])
            {
                _gammaCy = 0.0;
            }
            else
            {
                for (int i = 0; i < _TauDonSigmaZPrimeSet.Length; i++)
                {
                    if (i == _TauDonSigmaZPrimeSet.Length - 1)
                    {
                        _Na_0005 = _NaSet_0005[i];
                        _Na_0510 = _NaSet_0510[i];
                        _Na_1020 = _NaSet_1020[i];
                        _Na_2040 = _NaSet_2040[i];
                        _Na_4080 = _NaSet_4080[i];
                        break;
                    }

                    else if (_TauDonSigmaZPrimeSet[i] <= _TauDonSigmaZPrime && _TauDonSigmaZPrime < _TauDonSigmaZPrimeSet[i + 1])
                    {
                        _Na_0005 = (_NaSet_0005[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
                            + _NaSet_0005[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
                            / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

                        _Na_0510 = (_NaSet_0510[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
                            + _NaSet_0510[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
                            / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

                        _Na_1020 = (_NaSet_1020[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
                            + _NaSet_1020[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
                            / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

                        _Na_2040 = (_NaSet_2040[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
                            + _NaSet_2040[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
                            / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

                        _Na_4080 = (_NaSet_4080[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
                            + _NaSet_4080[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
                            / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

                        break;
                    }
                }
            }
            if (_Na_0005 <= _Na) { _gammaCy = 0.0; }
            else if(_Na_0510 <= _Na) { _gammaCy = 0.5; }
            else if (_Na_1020 <= _Na) { _gammaCy = 1.0; }
            else if (_Na_2040 <= _Na) { _gammaCy = 2.0; }
            else if (_Na_4080 <= _Na) { _gammaCy = 4.0; }
            else { _gammaCy = 8.0; }
            
            return _gammaCy;
        }

        internal static double CalculateBetaL(double _z, double _Na)
        {
            double[] _betaLSet = { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
            double[] _Na0010 = { 0.0, 10.0, 15.7, 19.0, 21.0, 22.4, 23.4, 24.0, 24.5, 24.8, 25.0 };
            double[] _Na1020 = { 0.0, 6.0, 10.0, 12.5, 14.5, 16.0, 17.3, 18.3, 19.0, 19.6, 20.0 };
            double _betaL = 1.0;

            if (-10 < _z && _z <= 0)
            {
                for (int i = 0; i < _betaLSet.Length; ++i)
                {
                    if (i == _betaLSet.Length - 1)
                    {
                        _betaL = 1.0;
                        break;
                    }
                    else if (_Na0010[i] <= _Na && _Na < _Na0010[i + 1])
                    {
                        _betaL = (_betaLSet[i + 1] * (_Na - _Na0010[i])
                            + _betaLSet[i] * (-_Na + _Na0010[i + 1]))
                            / (_Na0010[i + 1] - _Na0010[i]);
                        break;
                    }

                }
            }
            else
            {
                for (int i = 0; i < _betaLSet.Length; ++i)
                {
                    if (i == _betaLSet.Length - 1)
                    {
                        _betaL = 1.0;
                        break;
                    }
                    else if (_Na1020[i] <= _Na && _Na < _Na1020[i + 1])
                    {
                        _betaL = (_betaLSet[i + 1] * (_Na - _Na1020[i])
                            + _betaLSet[i] * (-_Na + _Na1020[i + 1]))
                            / (_Na1020[i + 1] - _Na1020[i]);
                        break;
                    }
                }
            }
            return _betaL;
        }
    }


    [Serializable]
    public class GroundMassDataItem : BaseDataItem
    {
        private readonly GroundLayerViewModel viewModel;

        public GroundMassDataItem(GroundLayerViewModel viewModel)
        {
            this.viewModel = viewModel;
        }

        private double _gLDepth;
        public double GLDepth // 深さ
        {
            get => _gLDepth;
            set => SetProperty(ref _gLDepth, value);
        }

        private double _altitudeDepth;
        public double AltitudeDepth // 標高深さ
        {
            get => _altitudeDepth;
            set => SetProperty(ref _altitudeDepth, value);
        }

        private double _spacing = 1.0;
        public double Spacing// 間隔
        {
            get => _spacing;
            set => SetPropertyWithAction(ref _spacing, value, viewModel.Update);
        }

        private Nullable<double> _H;
        public Nullable<double> H // 土質点厚
        {
            get => _H;
            set => SetProperty(ref _H, value);
        }
        private bool _isEngineeringBedrock; // 工学的基盤か否か
        public bool IsEngineeringBedrock
        {
            get => _isEngineeringBedrock;
            set => SetProperty(ref _isEngineeringBedrock, value);
        }

        private string _name;
        public string Name // 名称
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _NValue = 15;
        public double NValue // N値
        {
            get => _NValue;
            set => SetPropertyWithAction(ref _NValue, value, viewModel.Update);
        }

        private double _Fc = 35.0;
        public double Fc // 細粒分含有率
        {
            get => _Fc;
            set
            {
                SetProperty(ref _Fc, value);
                viewModel.Update();
            }
        }

        private double _sigmaZ;
        public double SigmaZ // 全応力
        {
            get => _sigmaZ;
            set => SetProperty(ref _sigmaZ, value);
        }

        private double _sigmaZPrime;
        public double SigmaZPrime // 有効応力
        {
            get => _sigmaZPrime;
            set => SetProperty(ref _sigmaZPrime, value);
        }

        private bool _isLiquefactionLayer;
        public bool IsLiquefactionLayer // 液状化対象層か否か
        {
            get => _isLiquefactionLayer;
            set => SetProperty(ref _isLiquefactionLayer, value);
        }

        private Nullable<double> _rD;
        public Nullable<double> RD // 低減係数
        {
            get => _rD;
            set => SetProperty(ref _rD, value);
        }

        private Nullable<double> _N1;
        public Nullable<double> N1 // 換算N値
        {
            get => _N1;
            set => SetProperty(ref _N1, value);
        }

        private Nullable<double> _deltaNf;
        public Nullable<double> DeltaNf // N値増分
        {
            get => _deltaNf;
            set => SetProperty(ref _deltaNf, value);
        }

        private Nullable<double> _NL;
        public Nullable<double> NL // 補正N値
        {
            get => _NL;
            set => SetProperty(ref _NL, value);
        }

        private Nullable<double> _tauLonSigmaZPrime;
        public Nullable<double> TauLonSigmaZPrime // 液状化抵抗比
        {
            get => _tauLonSigmaZPrime;
            set => SetProperty(ref _tauLonSigmaZPrime, value);
        }

        private Nullable<double> _tauDonSigmaZPrime;
        public Nullable<double> TauDonSigmaZPrime // 繰り返しせん断応力比
        {
            get => _tauDonSigmaZPrime;
            set => SetProperty(ref _tauDonSigmaZPrime, value);
        }

        private Nullable<double> _FL = 99.0;
        public Nullable<double> FL // 安全率
        {
            get => _FL;
            set => SetProperty(ref _FL, value);
        }

        private Nullable<double> _betaL;
        public Nullable<double> BetaL // 低減率
        {
            get => _betaL;
            set => SetProperty(ref _betaL, value);
        }

        private Nullable<double> _gammaCy;
        public Nullable<double> GammaCy // 繰り返しせん断ひずみ
        {
            get => _gammaCy;
            set => SetProperty(ref _gammaCy, value);
        }

        private Nullable<double> _sigmaGammaCyH;
        public Nullable<double> SigmaGammaCyH // 液状化水平変位
        {
            get => _sigmaGammaCyH;
            set => SetProperty(ref _sigmaGammaCyH, value);
        }

        private double _density;
        public double Density // 単位体積重量
        {
            get => _density;
            set => SetPropertyWithAction(ref _density, value, viewModel.Update);
        }

        private double _mass;
        public double Mass // 質量
        {
            get => _mass;
            set => SetProperty(ref _mass, value);
        }

        private double _VS0 = 250.0;
        public double VS0 // 初期S波速度
        {
            get => _VS0;
            set => SetPropertyWithAction(ref _VS0, value, viewModel.Update);
        }

        private double _VSE;
        public double VSE // 等価S波速度
        {
            get => _VSE;
            set => SetProperty(ref _VSE, value);
        }

        private double _K;
        public double K // 等価せん断ばね剛性
        {
            get => _K;
            set => SetProperty(ref _K, value);
        }

        private double _u;
        public double U // 仮の無次元化水平変位
        {
            get => _u;
            set => SetProperty(ref _u, value);
        }

        private double _uStar;
        public double UStar // 調整後無次元化水平変位
        {
            get => _uStar;
            set => SetProperty(ref _uStar, value);
        }


        private double _DmaxUStar;
        public double DmaxUStar // 水平変位
        {
            get => _DmaxUStar;
            set => SetProperty(ref _DmaxUStar, value);
        }

        private double _DmaxUStarSigmaGammaCyH;
        public double DmaxUStarSigmaGammaCyH // 水平変位
        {
            get => _DmaxUStarSigmaGammaCyH;
            set => SetProperty(ref _DmaxUStarSigmaGammaCyH, value);
        }
    }

    [Serializable]
    public class GroundLayerDataItem : BaseDataItem //INotifyPropertyChanged
    {
        private readonly GroundLayerViewModel viewModel;

        public GroundLayerDataItem(GroundLayerViewModel viewModel)
        {
            this.viewModel = viewModel;
        }

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private GroundLayerDataItem _selectedLayer;
        public GroundLayerDataItem SelectedLayer
        {
            get => _selectedLayer;
            set => SetProperty(ref _selectedLayer, value);
        }

        private ObservableCollection<GroundLayerDataItem> _selectedGroundLayerCollection;
        public ObservableCollection<GroundLayerDataItem> SelectedGroundLayerCollection
        {
            get => _selectedGroundLayerCollection;
            set => SetProperty(ref _selectedGroundLayerCollection, value);
        }

        private double _bottomGLDepth;
        public double BottomGLDepth // 下端深さ
        {
            get => _bottomGLDepth;
            set => SetProperty(ref _bottomGLDepth, value);
        }

        private double _layerThickness = 3.0;
        public double LayerThickness // 層厚
        {
            get => _layerThickness;
            set => SetPropertyWithAction(ref _layerThickness, value, viewModel.Update);
        }

        private double _bottomElevation;
        public double BottomElevation // 下端標高
        {
            get => BottomGLDepth + viewModel.GroundTopAltitude;
            set => SetProperty(ref _bottomElevation, value);
        }

        private string _layerName = "As#"; // 土層名
        public string LayerName
        {
            get => _layerName;
            set => SetPropertyWithAction(ref _layerName, value, viewModel.Update);
        }

        private string _selectedLayerClass = "砂質土"; // 土層分類
        public string SelectedLayerClass
        {
            get => _selectedLayerClass;
            set => SetProperty(ref _selectedLayerClass, value);
        }

        public ObservableCollection<string> LayerClassOption { get; } = new ObservableCollection<string>()
        {
            "粘性土",
            "砂質土",
            "礫質土"
        };

        private double _density = 17.0; // 土層単位体積重量
        public double Density
        {
            get => _density;
            set => SetPropertyWithAction(ref _density, value, viewModel.Update);
        }

        public ObservableCollection<string> LayerAgesOption { get; } = new ObservableCollection<string>()
        {
            "沖積層",
            "洪積層"
        };

        private string _selectedLayerAge = "沖積層"; // 選択土層年代
        public string SelectedLayerAge
        {
            get => _selectedLayerAge;
            set => SetProperty(ref _selectedLayerAge, value);
        }

        private bool _isEngineeringBedrock = true; // 工学的基盤か否か
        public bool IsEngineeringBedrock
        {
            get => _isEngineeringBedrock;
            set => SetProperty(ref _isEngineeringBedrock, value);
        }

        private double _nValue; // N値
        public double NValue
        {
            get => _nValue;
            set => SetProperty(ref _nValue, value);
        }

        private double _cohesive = 0.0; // 粘着力
        public double Cohesive
        {
            get => _cohesive;
            set => SetPropertyWithAction(ref _cohesive, value, viewModel.Update);
        }

        private double _vs; // Vs
        public double Vs
        {
            get => _vs;
            set => SetProperty(ref _vs, value);
        }

        private double _es; // 変形係数
        public double Es
        {
            get => _es;
            set => SetProperty(ref _es, value);
        }

        private bool _isPositiveCircumResistance; // 押込み方向周面抵抗
        public bool IsPositiveCircumResistance
        {
            get => _isPositiveCircumResistance;
            set => SetProperty(ref _isPositiveCircumResistance, value);
        }

        private bool _isNegativeCircumResistance; // 引抜き方向周面抵抗
        public bool IsNegativeCircumResistance
        {
            get => _isNegativeCircumResistance;
            set => SetProperty(ref _isNegativeCircumResistance, value);
        }
    }
    /// <summary>
    /// 
    /// </summary>
    [Serializable]
    public class GroundLayerViewModel : BaseViewModel
    {
        private ObservableCollection<GroundMassDataItem> _selectedGroundMassCollection1;
        public ObservableCollection<GroundMassDataItem> SelectedGroundMassCollection1
        {
            get => _selectedGroundMassCollection1;
            set => SetProperty(ref _selectedGroundMassCollection1, value);
        }

        private ObservableCollection<GroundMassDataItem> _selectedGroundMassCollection2;
        public ObservableCollection<GroundMassDataItem> SelectedGroundMassCollection2
        {
            get => _selectedGroundMassCollection2;
            set => SetProperty(ref _selectedGroundMassCollection2, value);
        }

        private ObservableCollection<GroundMassDataItem> _selectedGroundMassCollection;
        public ObservableCollection<GroundMassDataItem> SelectedGroundMassCollection
        {
            get => _selectedGroundMassCollection;
            set => SetProperty(ref _selectedGroundMassCollection, value);

        }

        private ObservableCollection<GroundLayerDataItem> _selectedGroundLayerCollection;
        public ObservableCollection<GroundLayerDataItem> SelectedGroundLayerCollection
        {
            get => _selectedGroundLayerCollection;
            set => SetProperty(ref _selectedGroundLayerCollection, value);
        }

        // データグリッド
        public ObservableCollection<ObservableCollection<ObservableCollection<GroundMassDataItem>>> DataGridMassLayers { get; }
            = new ObservableCollection<ObservableCollection<ObservableCollection<GroundMassDataItem>>>();

        public ObservableCollection<ObservableCollection<GroundLayerDataItem>> DataGridGroundLayers { get; }
            = new ObservableCollection<ObservableCollection<GroundLayerDataItem>>();

        // DataGridMassLayersにアクセスするためのメソッド
        public ObservableCollection<ObservableCollection<ObservableCollection<GroundMassDataItem>>> GetDataGridMassLayers()
        {
            return DataGridMassLayers;
        }

        // DataGridGroundLayersにアクセスするためのメソッド
        public ObservableCollection<ObservableCollection<GroundLayerDataItem>> GetDataGridGroundLayers()
        {
            return DataGridGroundLayers;
        }


        //readonly FundamentalViewModel DataContextFundamental = new FundamentalViewModel();

        // 選択地盤番号
        private int _selectedGroundNo = 1;
        public int SelectedGroundNo
        {
            get => _selectedGroundNo;
            set => SetProperty(ref _selectedGroundNo, value);
        }

        // 選択レベル
        public ObservableCollection<int> SelectedLevels { get; } = new ObservableCollection<int>();
        private int _selectedLevel = 2;
        public int SelectedLevel
        {
            get => _selectedLevel;
            set => SetProperty(ref _selectedLevel, value);
        }

        // 地盤符号
        public ObservableCollection<string> GroundRefs { get; } = new ObservableCollection<string>();
        private string _groundRef;
        public string GroundRef
        {
            get => _groundRef;
            set => SetProperty(ref _groundRef, value);
        }

        // 孔口標高
        public ObservableCollection<double> GroundTopAltitudes { get; } = new ObservableCollection<double>();
        private double _groundTopAltitude;
        public double GroundTopAltitude
        {
            get => _groundTopAltitude;
            set
            {
                if (SetProperty(ref _groundTopAltitude, value))
                {
                    OnPropertyChanged(nameof(GroundWaterGLDepth));
                    foreach (var item in SelectedGroundLayerCollection)
                    {
                        item.OnPropertyChanged(nameof(item.BottomElevation));
                    }
                }
            }
        }

        // 地下水位標高
        public ObservableCollection<double> GroundWaterTableAltitudes { get; } = new ObservableCollection<double>();
        private double _groundWaterTableAltitude;
        public double GroundWaterTableAltitude
        {
            get => _groundWaterTableAltitude;
            set
            {
                if (SetProperty(ref _groundWaterTableAltitude, value))
                {
                    OnPropertyChanged(nameof(GroundWaterTableAltitude));
                    OnPropertyChanged(nameof(GroundWaterGLDepth));
                }
            }
        }

        // 応力標高
        public ObservableCollection<double> StressAltitudes { get; } = new ObservableCollection<double>();
        private double _stressAltitude = 0;
        public double StressAltitude
        {
            get => _stressAltitude;
            set => SetProperty(ref _stressAltitude, value);
        }

        // 地表深さ = 0
        public ObservableCollection<double> GLDepths { get; } = new ObservableCollection<double>();
        private double _gLDepth = 0;
        public double GLDepth
        {
            get => _gLDepth;
            set => SetProperty(ref _gLDepth, value);
        }

        // 地下水位深さ
        public ObservableCollection<double> GroundWaterGLDepths { get; } = new ObservableCollection<double>();
        private double _groundWaterGLDepth = -1.0;
        public double GroundWaterGLDepth
        {
            get => _groundWaterGLDepth;
            set => SetProperty(ref _groundWaterGLDepth, value);
        }

        // 応力計算用深さ
        public ObservableCollection<double> StressGLDepths { get; } = new ObservableCollection<double>();
        private double _stressGLDepth;
        public double StressGLDepth
        {
            get => _stressGLDepth;
            set => SetProperty(ref _stressGLDepth, value);
        }

        // 地表面における設計用水平加速度
        public ObservableCollection<double> GroundAccelerations1 { get; } = new ObservableCollection<double>();
        private double _groundAcceleration1 = 1.5;
        public double GroundAcceleration1
        {
            get => _groundAcceleration1;
            set => SetProperty(ref _groundAcceleration1, value);
        }

        public ObservableCollection<double> GroundAccelerations2 { get; } = new ObservableCollection<double>();
        private double _groundAcceleration2 = 3.5;
        public double GroundAcceleration2
        {
            get => _groundAcceleration2;
            set => SetProperty(ref _groundAcceleration2, value);
        }

        // 表層の土質
        public ObservableCollection<string> ShallowSoilTypes { get; } = new ObservableCollection<string>();
        private string _shallowSoilType = "砂質土";
        public string ShallowSoilType
        {
            get => _shallowSoilType;
            set => SetProperty(ref _shallowSoilType, value);
        }

        public ObservableCollection<string> ShallowSoilTypeOption { get; } = new ObservableCollection<string>()
            {
                "粘性土",
                "砂質土"
            };


        // 算定法
        public ObservableCollection<string> CalculationMethods { get; } = new ObservableCollection<string>();
        private string _calculationMethod = "a1(b1)";
        public string CalculationMethod
        {
            get => _calculationMethod;
            set => SetProperty(ref _calculationMethod, value);
        }

        public ObservableCollection<string> CalculationMethodOption { get; } = new ObservableCollection<string>()
            {
                "a1(b1)",
                "a2(b2)"
            };

        // 工学的基盤の単位体積重量
        public ObservableCollection<double> BedrockDensities { get; } = new ObservableCollection<double>();
        private double _bedrockDensity = 19;
        public double BedrockDensity
        {
            get => _bedrockDensity;
            set => SetProperty(ref _bedrockDensity, value);
        }

        // 工学的基盤のせん断波速度
        public ObservableCollection<double> BedrockShearWaveVelocities { get; } = new ObservableCollection<double>();
        private double _bedrockShearWaveVelocity = 400.0;
        public double BedrockShearWaveVelocity
        {
            get => _bedrockShearWaveVelocity;
            set => SetProperty(ref _bedrockShearWaveVelocity, value);
        }

        // グラフ1内容
        public ObservableCollection<string> Chart1ContentOption { get; } = new ObservableCollection<string>()
            {
                "N値",
                "粘着力Cu",
                "N値, 粘着力Cu"
            };
        public ObservableCollection<string> Chart1Contents { get; } = new ObservableCollection<string>();
        private string _Chart1Content = "N値, 粘着力Cu";
        public string Chart1Content
        {
            get => _Chart1Content;
            set => SetProperty(ref _Chart1Content, value);
        }
        // グラフ2内容
        public ObservableCollection<string> Chart2ContentOption { get; } = new ObservableCollection<string>()
            {
                "DmaxU*",
                "DmaxU*+∑γcyH",
                "DmaxU*, DmaxU*+∑γcyH",
                "FL",
                "DmaxU*, FL",
                "DmaxU*, DmaxU*+∑γcyH, FL"
            };
        public ObservableCollection<string> Chart2Contents { get; } = new ObservableCollection<string>();
        private string _Chart2Content = "DmaxU*, DmaxU*+∑γcyH, FL";
        public string Chart2Content
        {
            get => _Chart2Content;
            set => SetProperty(ref _Chart2Content, value);
        }

        private object _dataContextFundamental;
        public object DataContextFundamental
        {
            get => _dataContextFundamental;
            set => SetProperty(ref _dataContextFundamental, value);
        }

        // GroundLayerViewModelクラスにSelectedLayerプロパティを追加する
        public GroundLayerDataItem SelectedLayer { get; set; }

        // GroundLayerViewModelクラスにSelectedMassプロパティを追加する
        public GroundMassDataItem SelectedMass { get; set; }

        //コマンド
        public ICommand AddBtn_Pushed { get; set; }
        public ICommand ClearBtn_Pushed { get; set; }

        // Chart関連
        //[NonSerialized]
        // コンストラクタ
        public GroundLayerViewModel()
        {
            // SelectedMassとSelectedGroundMassCollectionを初期化
            SelectedMass = new GroundMassDataItem(this);

            SelectedGroundMassCollection = new ObservableCollection<GroundMassDataItem>();
            SelectedGroundMassCollection1 = new ObservableCollection<GroundMassDataItem>();
            SelectedGroundMassCollection2 = new ObservableCollection<GroundMassDataItem>();

            // SelectedLayerとSelectedGroundLayerCollectionを初期化
            SelectedLayer = new GroundLayerDataItem(this);
            SelectedGroundLayerCollection = new ObservableCollection<GroundLayerDataItem>();

            // Initialize GroundRefs, GroundTopAltitudes, GroundWaterTableAltitudes, StressAltitudes, GLDepths, GroundWaterGLDepths, StressGLDepths
            for (int i = 0; i < 5; i++)
            {
                SelectedLevels.Add(2);
                GroundRefs.Add("(GR" + (i + 1).ToString() + ")");
                GroundTopAltitudes.Add(0);
                GroundWaterTableAltitudes.Add(-1.0);
                StressAltitudes.Add(0.0);
                GLDepths.Add(0);
                GroundWaterGLDepths.Add(0);
                StressGLDepths.Add(0);

                GroundAccelerations1.Add(1.5);
                GroundAccelerations2.Add(3.5);

                ShallowSoilTypes.Add("砂質土");
                CalculationMethods.Add("a1(b1)");
                BedrockDensities.Add(19);
                BedrockShearWaveVelocities.Add(400);

                Chart1Contents.Add("N値, 粘着力Cu");
                Chart2Contents.Add("DmaxU*, DmaxU*+∑γcyH, FL");
            }

            DataGridMassLayers = new ObservableCollection<ObservableCollection<ObservableCollection<GroundMassDataItem>>>();

            for (int i = 0; i < 5; i++)
            {
                DataGridMassLayers.Add(new ObservableCollection<ObservableCollection<GroundMassDataItem>>());
                for (int j = 0; j < 2; j++)
                {
                    DataGridMassLayers[i].Add(new ObservableCollection<GroundMassDataItem>());
                }
            }

            for (int i = 0; i < 5; i++)
            {
                DataGridGroundLayers.Add(new ObservableCollection<GroundLayerDataItem>());
            }

            //チャート初期化
            ChartInitialize();
        }

        internal void Update()
        {
            if (SelectedGroundLayerCollection.Count != 0)
            {
                RecalculateBottomGLDepth();
                RecalculateBottomElevation();
            }

            if (SelectedGroundMassCollection1.Count != 0 && SelectedGroundMassCollection2.Count != 0)
            {
                RecalculateGLDepth();
                RecalculateAltitude();
                RecalculateName();
                RecalculateDensityIsEngineeringBedrock();
                RecalculateH();
                RecalculateSigmaZ();
                RecalculateSigmaZPrime();
                RecalculateIsLiquefaction();
                RecalculateNL();
                RecalculateTauLonSigmaZPrime();
                RecalculateTauDonSigmaZprime();
                RecalculateFL();
                RecalculateBetaL();
                RecalculateGammaCy();
                RecalculateSigmaGammaCyH();
                RecalculateMass();
                RecalculateVSE();
            }

            //ClearSeries(chart1);
            //ClearSeries(chart2);
            //DrawNValuesAndCohesive();
            //DrawGroundLayers(chart1);
            //DrawDmaxUStar();
            //DrawGroundLayers(chart2);

            SelectedGroundMassCollection1 = DataGridMassLayers[SelectedGroundNo - 1][0];
            SelectedGroundMassCollection2 = DataGridMassLayers[SelectedGroundNo - 1][1];
        }

        internal void RecalculateBottomElevation()
        {
            foreach (var item in SelectedGroundLayerCollection)
            {
                item.BottomElevation = item.BottomGLDepth + GroundTopAltitude;
            }
        }
        // 深さの再計算
        internal void RecalculateGLDepth()
        {
            //{
            //    foreach (var item in SelectedGroundMassCollection)
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                double totalThickness = 0;
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    totalThickness += item.Spacing;
                    item.GLDepth = -totalThickness;
                }
            }
        }

        internal void RecalculateBottomGLDepth()
        {
            double totalThickness = 0;
            foreach (var item in SelectedGroundLayerCollection)
            {
                totalThickness += item.LayerThickness;
                item.BottomGLDepth = -totalThickness;
            }
        }

        // 標高の再計算
        internal void RecalculateAltitude()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    item.AltitudeDepth = item.GLDepth + GroundTopAltitude;
                }
            }
        }

        internal void RecalculateH()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    if(DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock==true)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H = null;
                    }
                    else if (i == 0 && DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count == 1 )
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].Spacing;
                    }
                    else if (i == 0 && DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][1].IsEngineeringBedrock == true)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].Spacing;
                    }
                    else if (i == 0)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][0].Spacing
                            + DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][1].Spacing / 2;
                    }
                    else if (i == DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Spacing / 2;
                    }
                    else if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i + 1].IsEngineeringBedrock == true)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Spacing / 2;
                    }
                    else
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].Spacing / 2
                            + DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Spacing / 2;
                    }
                }
            }
        }

        internal void RecalculateDensityIsEngineeringBedrock()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    for (int j = 0; j < SelectedGroundLayerCollection.Count; j++)
                    {
                        if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].GLDepth >= SelectedGroundLayerCollection[j].BottomGLDepth)
                        {
                            DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Density = SelectedGroundLayerCollection[j].Density;
                            DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock = SelectedGroundLayerCollection[j].IsEngineeringBedrock;
                            break;
                        }
                    }
                }
            }
        }

        // 液状化検討必要性の再計算
        internal void RecalculateIsLiquefaction()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsEngineeringBedrock ==true)
                    {
                        item.IsLiquefactionLayer = false;
                    }
                    else
                    {
                        double Fc = item.Fc;
                        double _z = item.GLDepth;
                        double _groundWaterGLDepth = GroundWaterGLDepth;
                        item.IsLiquefactionLayer = Liquefaction.IsLiquefactionLayer(_groundWaterGLDepth, _z, Fc);
                    }
                }
            }
        }

        // NL値の再計算
        internal void RecalculateNL()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        double CN = Math.Sqrt(100.0 / item.SigmaZPrime);
                        item.N1 = CN * item.NValue;
                        item.DeltaNf = 0.0;
                        if (5.0 < item.Fc && item.Fc < 10.0) { item.DeltaNf = 6.0 / 5.0 * (item.Fc - 5.0); }
                        else if (item.Fc < 20.0) { item.DeltaNf = 0.2 * (item.Fc - 10.0) + 6.0; }
                        else if (item.Fc < 50.0) { item.DeltaNf = 0.1 * (item.Fc - 20.0) + 8.0; }
                        item.NL = item.N1 + item.DeltaNf;
                    }
                    else
                    {
                        item.N1 = null;
                        item.DeltaNf = null;
                        item.NL = null;
                    }
                }
            }
        }

        internal void RecalculateTauLonSigmaZPrime()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        double _NL = item.NL.GetValueOrDefault();
                        item.TauLonSigmaZPrime = 0.0410 * (Math.Sqrt(_NL) + 0.00903 * Math.Pow(_NL / 10, 7));
                    }
                    else
                    {
                        item.TauLonSigmaZPrime = null;
                    }
                }
            }
        }

        internal void RecalculateTauDonSigmaZprime()
        {
            double magnitude = 7.5;
            double rn = 0.1 * (magnitude - 1.0);
            double alphaMax = 3.5;
            double gravity = 9.8;

            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {   if(GroundAccelerations1.Count!=0)
                {                 
                    if (LevelIndex == 0)
                    { alphaMax = GroundAccelerations1[SelectedGroundNo - 1]; }
                    else if (LevelIndex == 1)
                    { alphaMax = GroundAccelerations2[SelectedGroundNo - 1]; }
                }


                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        item.RD = 1.0 - 0.015 * Math.Abs(item.GLDepth);
                        double sigmaZ = item.SigmaZ;
                        double sigmaZPrime = item.SigmaZPrime;
                        item.TauDonSigmaZPrime = rn * alphaMax / gravity * sigmaZ / sigmaZPrime * item.RD.GetValueOrDefault();
                    }
                    else
                    {
                        item.TauDonSigmaZPrime = null;
                    }

                }
            }
        }

        internal void RecalculateFL()
        {
            //for (int i = 0; i < this.SelectedGroundMassCollection.Count; i++)
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        item.FL = item.TauLonSigmaZPrime / item.TauDonSigmaZPrime;
                    }
                    else
                    {
                        item.FL = null;
                    }
                }
            }
        }

        internal void RecalculateGammaCy()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        item.GammaCy = Liquefaction.CalculateGammaCy(item.NL.GetValueOrDefault(), item.TauDonSigmaZPrime.GetValueOrDefault());
                    }
                    else
                    {
                        item.GammaCy = null;
                    }
                }
            }
        }

        internal void RecalculateSigmaGammaCyH()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                double _sigmaGammaCyH = 0;
                for (int i = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1; i >= 0; i--)
                {
                    if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock == true)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].SigmaGammaCyH = null;
                    }
                    else if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock == false)
                    {
                        _sigmaGammaCyH += DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].GammaCy.GetValueOrDefault() / 100.0
                            * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H.GetValueOrDefault() * 1000.0;
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].SigmaGammaCyH = _sigmaGammaCyH;
                    }
                }
            }
        }

        internal void RecalculateBetaL()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    if (item.IsLiquefactionLayer)
                    {
                        item.BetaL = Liquefaction.CalculateBetaL(item.GLDepth, item.NL.GetValueOrDefault());
                    }
                    else
                    {
                        item.BetaL = null;
                    }
                }
            }
        }

        internal void RecalculateName()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    for (int j = 0; j < SelectedGroundLayerCollection.Count; j++)
                    {
                        if (item.GLDepth >= SelectedGroundLayerCollection[j].BottomGLDepth)
                        {
                            item.Name = SelectedGroundLayerCollection[j].LayerName;
                            break;
                        }
                        else if (j == SelectedGroundLayerCollection.Count - 1)
                        {
                            item.Name = "";
                            break;
                        }
                    }
                }
            }
        }

        internal void RecalculateSigmaZ()
        {
            //for (int i = 0; i < this.SelectedGroundMassCollection.Count; i++)
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    item.SigmaZ = 0.0;

                    for (int j = 0; j < SelectedGroundLayerCollection.Count; j++)
                    {
                        if (item.GLDepth <= SelectedGroundLayerCollection[j].BottomGLDepth)
                        {
                            item.SigmaZ += SelectedGroundLayerCollection[j].Density * SelectedGroundLayerCollection[j].LayerThickness;
                        }
                        else
                        {
                            if (j == 0)
                            {
                                item.SigmaZ += SelectedGroundLayerCollection[j].Density
                                    * (0 - item.GLDepth);
                            }
                            else
                            {
                                item.SigmaZ += SelectedGroundLayerCollection[j].Density
                                    * Math.Max(0, (SelectedGroundLayerCollection[j - 1].BottomGLDepth - item.GLDepth));
                            }
                            break;
                        }
                    }
                }
            }
        }

        internal void RecalculateSigmaZPrime()
        {
            //for (int i = 0; i < this.SelectedGroundMassCollection.Count; i++)
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                foreach (var item in DataGridMassLayers[SelectedGroundNo - 1][LevelIndex])
                {
                    item.SigmaZPrime = 0.0;

                    for (int j = 0; j < SelectedGroundLayerCollection.Count; j++)
                    {
                        if (item.GLDepth <= SelectedGroundLayerCollection[j].BottomGLDepth)
                        {
                            item.SigmaZPrime += SelectedGroundLayerCollection[j].Density * SelectedGroundLayerCollection[j].LayerThickness;
                        }
                        else
                        {
                            if (j == 0)
                            {
                                item.SigmaZPrime += SelectedGroundLayerCollection[j].Density
                                    * (0 - item.GLDepth);
                            }
                            else
                            {
                                item.SigmaZPrime += SelectedGroundLayerCollection[j].Density
                                    * Math.Max(0, (SelectedGroundLayerCollection[j - 1].BottomGLDepth - item.GLDepth));
                            }

                            //break;
                        }
                    }
                    item.SigmaZPrime -= 10.0 * Math.Max(0.0, (GroundWaterGLDepth - item.GLDepth));
                }
            }
        }

        internal void RecalculateMass()
        {
            double zi1;
            double zi2;
            double zj1;
            double zj2;
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Mass = 0.0;
                    if (i == 0)
                    {
                        zi1 = 0;
                    }
                    else
                    {
                        zi1 = (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].GLDepth + DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].GLDepth) / 2.0;
                    }
                    if (i != DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        zi2 = (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].GLDepth + DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i + 1].GLDepth) / 2.0;
                    }
                    else
                    {
                        zi2 = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].GLDepth;
                    }

                    for (int j = 0; j < SelectedGroundLayerCollection.Count; j++)
                    {
                        zj1 = SelectedGroundLayerCollection[j].BottomGLDepth + SelectedGroundLayerCollection[j].LayerThickness;
                        zj2 = SelectedGroundLayerCollection[j].BottomGLDepth;

                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0) * SelectedGroundLayerCollection[j].Density / 9.806665;
                    }
                }
            }
        }

        internal void RecalculateVSE()
        {
            for (int LevelIndex = 0; LevelIndex < 2; LevelIndex++)
            {
                double L = 1.0;
                if (LevelIndex == 0)
                { L = 0.2; }
                else if (LevelIndex == 1)
                { L = 1.0; }

                double Z = 1.0;
                double CAlpha = 25.0;
                if (this.ShallowSoilType == "粘性土") { CAlpha = 25.0; }
                else if (this.ShallowSoilType == "砂質土") { CAlpha = 40.0; }

                //for (int i = 0; i < this.SelectedGroundMassCollection.Count; i++)
                double T0 = 0.0;
                double SigmaH = 0.0;
                double SigmaGammaVS0H = 0.0;
                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock== true){ break; }
                    T0 += 4 * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H.GetValueOrDefault() / DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VS0;
                    SigmaH += DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H.GetValueOrDefault();
                    SigmaGammaVS0H += DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Density
                        * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VS0
                        * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].H.GetValueOrDefault();

                    if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock == true && i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        break;
                    }
                }

                double alpha = Math.Min(1 + L * Z * CAlpha * T0 / SigmaH, 4.0);

                double Rz0 = SigmaGammaVS0H / (BedrockDensity * BedrockShearWaveVelocity * SigmaH);
                double beta = 3.0 / 4.0 * (1.0 - 1.0 / Math.Pow(2.0, (alpha - 1.0))) / (1 - Rz0);
                double mu = 0.0;
                double uNPlusOne = 0.0;
                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VSE
                        = Math.Pow(DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Density * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VS0
                        / this.BedrockDensity / this.BedrockShearWaveVelocity, beta) * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VS0;
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].K
                        = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Density / 9.80665
                        * Math.Pow(DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].VSE, 2.0)
                        / DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].Spacing;

                    if (i == 0)
                    {
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].U = 1.0; // 地表における変位
                    }
                    else
                    {
                        mu += DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].Mass * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].U;
                        DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].U = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].U - 40.0 / DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i - 1].K / Math.Pow(alpha * T0, 2.0) * mu;
                    }

                    if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock == true && i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        uNPlusOne = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].U;
                        for (int j = i + 1; j < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; j++)
                        {
                            DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][j].U = 0.0;
                        }
                        break;
                    }
                    else if (i == DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        uNPlusOne = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].U;
                    }
                }

                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].UStar = (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].U - uNPlusOne) / (1 - uNPlusOne);
                    if (DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].IsEngineeringBedrock == true && i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count - 1)
                    {
                        for (int j = i + 1; j < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; j++)
                        {
                            DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][j].UStar = 0.0;
                        }
                        break;
                    }

                }

                double fA = Math.Min(1.6 * alpha * T0, 1);
                double C1 = 0.0;
                double C2 = 0.0;
                if (ShallowSoilType == "粘性土")
                {
                    C1 = 0.0028;
                    C2 = 0.53;
                }

                else if (ShallowSoilType == "砂質土")
                {
                    C1 = 0.0015;
                    C2 = 0.666;
                }
                double Dmax = 0;

                if (CalculationMethod == "a1(b1)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH * (C2 * (1 - 1 / Math.Pow(alpha, 2.0)) + 2.0 * Rz0 / alpha);
                }
                else if (CalculationMethod == "a2(b2)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH;
                }

                for (int i = 0; i < DataGridMassLayers[SelectedGroundNo - 1][LevelIndex].Count; i++)
                {
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].DmaxUStar = Dmax * DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].UStar * 1000.0;
                    DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].DmaxUStarSigmaGammaCyH
                        = DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].DmaxUStar
                        + DataGridMassLayers[SelectedGroundNo - 1][LevelIndex][i].SigmaGammaCyH.GetValueOrDefault();
                }
            }
        }


        //チャート初期化
        private void ChartInitialize()
        {
        }

