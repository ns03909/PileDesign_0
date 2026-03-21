using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace PileDesign.Models.InputData
{
    public class GroundInput : BaseModel
    {
        public bool IsErrorGroundWaterTableAltitude => GroundWaterTableAltitude > GroundTopAltitude;
        public bool IsErrorStressAltitude => StressAltitude > GroundTopAltitude;
        public bool IsErrorGroundWaterGLDepth => GroundWaterGLDepth > 0;
        public bool IsErrorStressGLDepth => StressGLDepth > 0;

        // 地盤参照
        private string _groundRef;
        public string GroundRef
        {
            get => _groundRef;
            set => SetProperty(ref _groundRef, value);
        }

        // 孔口Z
        private double _groundTopAltitude;
        public double GroundTopAltitude
        {
            get => _groundTopAltitude;
            set
            {
                if (SetProperty(ref _groundTopAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterTableAltitude));
                    OnPropertyChanged(nameof(IsErrorStressAltitude));
                }
            }
        }

        // 地下水位Z
        private double _groundWaterTableAltitude;
        public double GroundWaterTableAltitude
        {
            get => _groundWaterTableAltitude;
            set
            {
                if (SetProperty(ref _groundWaterTableAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterTableAltitude));
                    OnPropertyChanged(nameof(IsErrorGroundWaterGLDepth)); // 深さ連動して再計算する場合
                }
            }
        }

        // 応力Z
        private double _stressAltitude;
        public double StressAltitude
        {
            get => _stressAltitude;
            set
            {
                if (SetProperty(ref _stressAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorStressAltitude));
                }
            }
        }

        // 地表深さ = 0
        private double _gLDepth;
        public double GLDepth
        {
            get => _gLDepth;
            set => SetProperty(ref _gLDepth, value);
        }

        // 地下水位深さ
        private double _groundWaterGLDepth;
        public double GroundWaterGLDepth
        {
            get => _groundWaterGLDepth;
            set
            {
                if (SetProperty(ref _groundWaterGLDepth, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterGLDepth));
                }
            }
        }

        // 応力計算用深さ
        private double _stressGLDepth;
        public double StressGLDepth
        {
            get => _stressGLDepth;
            set
            {
                if (SetProperty(ref _stressGLDepth, value))
                {
                    OnPropertyChanged(nameof(IsErrorStressGLDepth));
                }
            }
        }

        // 地表面における設計用レベル1地震水平加速度
        private double _groundAcceleration1;
        public double GroundAcceleration1
        {
            get => _groundAcceleration1;
            set => SetProperty(ref _groundAcceleration1, value);
        }

        // 地表面における設計用レベル2地震水平加速度
        private double _groundAcceleration2;
        public double GroundAcceleration2
        {
            get => _groundAcceleration2;
            set => SetProperty(ref _groundAcceleration2, value);
        }

        // 浅層地盤のタイプ
        private string _shallowSoilType;
        public string ShallowSoilType
        {
            get => _shallowSoilType;
            set => SetProperty(ref _shallowSoilType, value);
        }

        // 算定法
        private string _calculationMethod;
        public string CalculationMethod
        {
            get => _calculationMethod;
            set => SetProperty(ref _calculationMethod, value);
        }

        // 工学的基盤の単位体積重量
        private double _bedrockDensity;
        public double BedrockDensity
        {
            get => _bedrockDensity;
            set => SetProperty(ref _bedrockDensity, value);
        }

        // 工学的基盤のせん断波速度
        private double _bedrockShearWaveVelocity;
        public double BedrockShearWaveVelocity
        {
            get => _bedrockShearWaveVelocity;
            set => SetProperty(ref _bedrockShearWaveVelocity, value);
        }

        // 土層データ（デシリアライズ時はコンストラクタのデフォルト値を上書きする）
        private ObservableCollection<GroundLayerInput> _groundLayers;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<GroundLayerInput> GroundLayers
        {
            get => _groundLayers;
            set => SetProperty(ref _groundLayers, value);
        }

        // 土質点データ（デシリアライズ時はコンストラクタのデフォルト値を上書きする）
        private ObservableCollection<GroundMassDataInput> _groundMassesData;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<GroundMassDataInput> GroundMassesData
        {
            get => _groundMassesData;
            set => SetProperty(ref _groundMassesData, value);
        }

        // 内部摩擦角とN値の関係　p30
        public List<string> SelectedInternalFrictionAngleCalcumationMethodOption { get; } =
        [
            "大崎式",
            "畑中式",
        ];

        // 内部摩擦角とN値の関係　p30
        private string _selectedInternalFrictionAngleCalcumationMethod = "大崎式";
        public string SelectedInternalFrictionAngleCalcumationMethod
        {
            get => _selectedInternalFrictionAngleCalcumationMethod;
            set => SetProperty(ref _selectedInternalFrictionAngleCalcumationMethod, value);
        }

        //微小変形時の地盤の固有周期
        private double _naturalPeriod;
        public double NaturalPeriod
        {
            get => _naturalPeriod;
            set => SetProperty(ref _naturalPeriod, value);
        }

        //地震時の地盤の固有周期
        private ObservableCollection<double> _naturalPeriods = [0.0, 0.0];
        public ObservableCollection<double> NaturalPeriods
        {
            get => _naturalPeriods;
            set => SetProperty(ref _naturalPeriods, value);
        }

        // コンストラクタ
        public GroundInput()
        {
            GroundRef = "(GR1)";
            GroundTopAltitude = 0.0;
            GroundWaterTableAltitude = 0.0;
            StressAltitude = 0.0;
            GLDepth = 0.0;
            GroundWaterGLDepth = 0.0;
            StressGLDepth = 0.0;

            GroundAcceleration1 = 1.5;
            GroundAcceleration2 = 3.5;

            ShallowSoilType = "砂質土";
            CalculationMethod = "a1(b1)";

            BedrockDensity = 19;
            BedrockShearWaveVelocity = 400;

            GroundLayers = [new GroundLayerInput() { No = 1, }];
            GroundMassesData = [new GroundMassDataInput() { No = 1, }];
        }

        public double GetFrictionAngle(double nValue, double sigmaZPrime)
        {
            if (SelectedInternalFrictionAngleCalcumationMethod == "大崎式")
            {
                return Math.Min(Math.Sqrt(20 * nValue) + 15, 40); // 大崎式
            }
            else if (SelectedInternalFrictionAngleCalcumationMethod == "畑中式")
            {
                double n1 = nValue / Math.Sqrt(sigmaZPrime / 100);
                return Math.Min(Math.Sqrt(20 * n1) + 20, 40); // N1が3.5以下の場合を含む
            }
            else
            {
                return Math.Min(Math.Sqrt(20 * nValue) + 15, 40); // 大崎式
            }
        }

        // 浅いコピーを作成するメソッド
        public GroundInput ShallowCopy()
        {
            return (GroundInput)this.MemberwiseClone();
        }

        // 深いコピーを作成するメソッド
        public GroundInput DeepCopy()
        {
            var copy = (GroundInput)this.MemberwiseClone();
            copy.GroundLayers = new ObservableCollection<GroundLayerInput>(this.GroundLayers.Select(layer => layer.DeepCopy()));
            copy.GroundMassesData = new ObservableCollection<GroundMassDataInput>(this.GroundMassesData.Select(mass => mass.DeepCopy()));
            return copy;
        }

        //  解析のための確認メソッド
        public bool ValidateForAnalysis(out string warningMessage)
        {
            bool hasWarning = false;
            warningMessage = "\n";
            {
                for (int i = 0; i < GroundLayers.Count; i++)
                {
                    var groundLayer = GroundLayers[i];

                    if (groundLayer.Es == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 変形係数が0です。\n";
                    }
                    if (groundLayer.GranularityClass == "粘性土" && groundLayer.Cohesive == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 粘性土で粘着力が0です。\n";
                    }
                    if (i >= 1 && groundLayer.BottomGLDepth >= GroundLayers[i - 1].BottomGLDepth)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 下端深度が直上層の下端深度より高いです。\n";
                    }

                }
                for (int i = 0; i < GroundMassesData.Count; i++)
                {
                    var groundMassData = GroundMassesData[i];
                    if (groundMassData.NValue == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: N値が0です。\n";
                    }
                    if (i >= 1 && groundMassData.GLDepth >= GroundMassesData[i - 1].GLDepth)
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: 深度が直上層の深度より高いです。\n";
                    }
                }
                return !hasWarning;
            }
        }
    }
}

