using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesign.Models.InputData
{
    class GroundHorizontalDisplacementInput : INotifyPropertyChanged
    {
        // プロパティ変更イベント
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        // 地震地域係数
        private double _z;
        public double Z
        {
            get => _z;
            set
            {
                if (_z != value)
                {
                    _z = value;
                    OnPropertyChanged(nameof(Z));
                }
            }
        }

        // 地盤の密度
        private ObservableCollection<double> _densities;
        public ObservableCollection<double> Densities
        {
            get => _densities;
            set
            {
                if (_densities != value)
                {
                    _densities = value;
                    OnPropertyChanged(nameof(Densities));
                }
            }
        }

        // 地盤の厚さ
        private ObservableCollection<double> _hs;
        public ObservableCollection<double> Hs
        {
            get => _hs;
            set
            {
                if (_hs != value)
                {
                    _hs = value;
                    OnPropertyChanged(nameof(Hs));
                }
            }
        }

        // 地盤の層数
        private int _layerNum;
        public int LayerNum
        {
            get => _layerNum;
            set
            {
                if (_layerNum != value)
                {
                    _layerNum = value;
                    OnPropertyChanged(nameof(LayerNum));
                }
            }
        }

        // 地震レベル
        //private int _seismicLevel;
        //public int SeismicLevel
        //{
        //    get => _seismicLevel;
        //    set
        //    {
        //        if (_seismicLevel != value)
        //        {
        //            _seismicLevel = value;
        //            OnPropertyChanged(nameof(SeismicLevel));
        //        }
        //    }
        //}

        public static List<int> SeismicLevelOption { get; } = [1, 2];


        // 工学的基盤最上位土層番号


        // 等価S波速度
        private ObservableCollection<double> _vS0s;
        public ObservableCollection<double> VS0s
        {
            get => _vS0s;
            set
            {
                if (_vS0s != value)
                {
                    _vS0s = value;
                    OnPropertyChanged(nameof(VS0s));
                }
            }
        }

        // 等価S波速度
        private ObservableCollection<double> _vSEs;
        public ObservableCollection<double> VSEs
        {
            get => _vSEs;
            set
            {
                if (_vSEs != value)
                {
                    _vSEs = value;
                    OnPropertyChanged(nameof(VSEs));
                }
            }
        }


        // 等価せん断ばね剛性
        private ObservableCollection<double> _ks;
        public ObservableCollection<double> Ks
        {
            get => _ks;
            set
            {
                if (_ks != value)
                {
                    _ks = value;
                    OnPropertyChanged(nameof(Ks));
                }
            }
        }

        // 仮の無次元化水平変位
        private ObservableCollection<double> _us;
        public ObservableCollection<double> Us
        {
            get => _us;
            set
            {
                if (_us != value)
                {
                    _us = value;
                    OnPropertyChanged(nameof(Us));
                }
            }
        }

        // 調整後無次元化水平変位
        private ObservableCollection<double> _uStars;
        public ObservableCollection<double> UStars
        {
            get => _uStars;
            set
            {
                if (_uStars != value)
                {
                    _uStars = value;
                    OnPropertyChanged(nameof(UStars));
                }
            }
        }

        // 水平変位
        private ObservableCollection<double> _dmaxUStars;
        public ObservableCollection<double> DmaxUStars
        {
            get => _dmaxUStars;
            set
            {
                if (_dmaxUStars != value)
                {
                    _dmaxUStars = value;
                    OnPropertyChanged(nameof(DmaxUStars));
                }
            }
        }


        // 水平変位
        private ObservableCollection<double> _dmaxUStarSigmaGammaCyHs;
        public ObservableCollection<double> DmaxUStarSigmaGammaCyHs
        {
            get => _dmaxUStarSigmaGammaCyHs;
            set
            {
                if (_dmaxUStarSigmaGammaCyHs != value)
                {
                    _dmaxUStarSigmaGammaCyHs = value;
                    OnPropertyChanged(nameof(DmaxUStarSigmaGammaCyHs));
                }
            }
        }

        // 工学的基盤か否か
        private ObservableCollection<bool> _isEngineeringBedrocks;
        public ObservableCollection<bool> IsEngineeringBedrocks
        {
            get => _isEngineeringBedrocks;
            set
            {
                if (_isEngineeringBedrocks != value)
                {
                    _isEngineeringBedrocks = value;
                    OnPropertyChanged(nameof(IsEngineeringBedrocks));
                }
            }
        }

        // 工学的基盤の密度
        private double _bedrockDensity;
        public double BedrockDensity
        {
            get => _bedrockDensity;
            set
            {
                if (_bedrockDensity != value)
                {
                    _bedrockDensity = value;
                    OnPropertyChanged(nameof(BedrockDensity));
                }
            }
        }

        // 工学的基盤のS波速度
        private double _bedrockShearWaveVelocity;
        public double BedrockShearWaveVelocity
        {
            get => _bedrockShearWaveVelocity;
            set
            {
                if (_bedrockShearWaveVelocity != value)
                {
                    _bedrockShearWaveVelocity = value;
                    OnPropertyChanged(nameof(BedrockShearWaveVelocity));
                }
            }
        }

        // 浅層地盤タイプ
        private string _shallowSoilType;
        public string ShallowSoilType
        {
            get => _shallowSoilType;
            set
            {
                if (_shallowSoilType != value)
                {
                    _shallowSoilType = value;
                    OnPropertyChanged(nameof(ShallowSoilType));
                }
            }
        }

        // 計算方法
        private string _calculationMethod;
        public string CalculationMethod
        {
            get => _calculationMethod;
            set
            {
                if (_calculationMethod != value)
                {
                    _calculationMethod = value;
                    OnPropertyChanged(nameof(CalculationMethod));
                }
            }
        }

        // 土質質量
        private ObservableCollection<double> _masses;
        public ObservableCollection<double> Masses
        {
            get => _masses;
            set
            {
                if (_masses != value)
                {
                    _masses = value;
                    OnPropertyChanged(nameof(Masses));
                }
            }
        }

        // 
        private ObservableCollection<double> _sigmaGammaCyHs;
        public ObservableCollection<double> SigmaGammaCyHs
        {
            get => _sigmaGammaCyHs;
            set
            {
                if (_sigmaGammaCyHs != value)
                {
                    _sigmaGammaCyHs = value;
                    OnPropertyChanged(nameof(SigmaGammaCyHs));
                }
            }
        }

        // コンストラクタ
        public GroundHorizontalDisplacementInput(
            double z,
            string calculationMethod,
            string shallowSoilType,
            double bedrockDensity,
            double bedrockShearWaveVelocity,
            ObservableCollection<double> hs,
            ObservableCollection<double> densities,
            ObservableCollection<double> vS0s,
            ObservableCollection<bool> isEngineeringBedrocks
        )
        {
            VSEs = [];
            Ks = [];
            Us = [];
            UStars = [];
            DmaxUStars = [];
            DmaxUStarSigmaGammaCyHs = [];
            Masses = [];
            SigmaGammaCyHs = [];

            Update(z, calculationMethod, shallowSoilType, bedrockDensity, bedrockShearWaveVelocity,
                hs, densities, vS0s, isEngineeringBedrocks);
        }

        // 更新
        internal void Update(
            double z,
            string calculationMethod,
            string shallowSoilType,
            double bedrockDensity,
            double bedrockShearWaveVelocity,
            ObservableCollection<double> hs,
            ObservableCollection<double> densities,
            ObservableCollection<double> vS0s,
            ObservableCollection<bool> isEngineeringBedrocks
            )
        {
            if (hs.Count == densities.Count &&
                   hs.Count == vS0s.Count &&
                   hs.Count == isEngineeringBedrocks.Count)
            {
                LayerNum = hs.Count;
                Hs = hs;
                Densities = densities;
                VS0s = vS0s;
                IsEngineeringBedrocks = isEngineeringBedrocks;
            }

            Z = z;
            CalculationMethod = calculationMethod;
            ShallowSoilType = shallowSoilType;
            BedrockDensity = bedrockDensity;
            BedrockShearWaveVelocity = bedrockShearWaveVelocity;

            for (int seismicLevel = 0; seismicLevel < 2; seismicLevel++)
            {
                double L = 0.2;
                if (seismicLevel == 0)
                { L = 0.2; }
                else if (seismicLevel == 1)
                { L = 1.0; }

                double CAlpha = 25.0; // 表層の土質の動的変形特性から決まる定数

                if (ShallowSoilType == "粘性土")
                { CAlpha = 25.0; }
                else if (ShallowSoilType == "砂質土")
                { CAlpha = 40.0; }

                double T0 = 0.0;
                double SigmaH = 0.0;
                double SigmaGammaVS0H = 0.0;
                for (int i = 0; i < LayerNum; i++)
                {
                    if (IsEngineeringBedrocks[i] == true)
                    { break; }
                    T0 += 4 * DmaxUStarSigmaGammaCyHs[i] / VS0s[i];
                    SigmaH += Hs[i];
                    SigmaGammaVS0H += Densities[i]
                        * VS0s[i]
                        * Hs[i];

                    if (IsEngineeringBedrocks[i] == true && i < LayerNum - 1)
                    {
                        break;
                    }
                }

                double alpha = Math.Min(1 + L * Z * CAlpha * T0 / SigmaH, 4.0);

                double Rz0 = SigmaGammaVS0H / (BedrockDensity * BedrockShearWaveVelocity * SigmaH);
                double beta = 3.0 / 4.0 * (1.0 - 1.0 / Math.Pow(2.0, alpha - 1.0)) / (1 - Rz0);
                double mu = 0.0;
                double uNPlusOne = 0.0;
                for (int i = 0; i < LayerNum; i++)
                {

                    VSEs[i]
                        = Math.Pow(Densities[i] * VS0s[i] / BedrockDensity / BedrockShearWaveVelocity, beta) * VS0s[i];

                    double spacing = (Hs[i] + Hs[i + 1]) * 0.5;

                    Ks[i]
                        = Densities[i] / 9.80665 * Math.Pow(VSEs[i], 2.0) / Hs[i];

                    if (i == 0)
                    {
                        Us[i] = 1.0; // 地表における変位
                    }
                    else
                    {
                        mu += Masses[i - 1] * Us[i - 1];
                        Us[i] = Us[i - 1] - 40.0 / Ks[i - 1] / Math.Pow(alpha * T0, 2.0) * mu;
                    }

                    if (IsEngineeringBedrocks[i] == true && i < LayerNum - 1)
                    {
                        uNPlusOne = Us[i];
                        for (int j = i + 1; j < LayerNum; j++)
                        {
                            Us[j] = 0.0;
                        }
                        break;
                    }
                    else if (i == LayerNum - 1)
                    {
                        uNPlusOne = Us[i];
                    }
                }

                for (int i = 0; i < LayerNum; i++)
                {
                    UStars[i] = (Us[i] - uNPlusOne) / (1 - uNPlusOne);
                    if (IsEngineeringBedrocks[i] == true && i < LayerNum - 1)
                    {
                        for (int j = i + 1; j < LayerNum; j++)
                        {
                            UStars[j] = 0.0;
                        }
                        break;
                    }
                }

                double fA = Math.Min(1.6 * alpha * T0, 1);
                double C1 = 0.0; // 表層の土層のG-γ関係から決まる定数
                double C2 = 0.0; // 表層の土質の減衰特性から決まる定数
                if (ShallowSoilType == "粘性土")
                {
                    C1 = 0.0028;
                    C2 = 0.53;
                }
                else if (ShallowSoilType == "砂質土")
                {
                    C1 = 0.0015;
                    C2 = 0.66;
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

                for (int i = 0; i < hs.Count; i++)
                {
                    DmaxUStars[i] = Dmax * UStars[i] * 1000.0;
                    DmaxUStarSigmaGammaCyHs[i]
                        = DmaxUStars[i]
                        + SigmaGammaCyHs[i];
                }
            }
        }
    }
}
