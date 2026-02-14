using System;
using PileDesign.Models;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 梁材料（ヤング係数、せん断弾性係数、ポアソン比）
    /// </summary>
    public class BeamMaterial : BaseModel
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _youngModulus = 2.5e7;  // デフォルト 2.5×10^7 kN/m² (コンクリートC24相当)
        public double YoungModulus
        {
            get => _youngModulus;
            set => SetProperty(ref _youngModulus, value);
        }

        private double _shearModulus = 1.04e7;  // デフォルト 1.04×10^7 kN/m² (E/2.4, ポアソン比0.2)
        public double ShearModulus
        {
            get => _shearModulus;
            set => SetProperty(ref _shearModulus, value);
        }

        private double _poissonRatio = 0.2;  // デフォルト 0.2（コンクリート）
        public double PoissonRatio
        {
            get => _poissonRatio;
            set => SetProperty(ref _poissonRatio, value);
        }
    }

    /// <summary>
    /// 梁断面（幅、高さ、断面性能）
    /// </summary>
    public class BeamSection : BaseModel
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _width = 0.5;  // デフォルト 0.5m
        public double Width
        {
            get => _width;
            set { if (SetProperty(ref _width, value)) RecalculateProperties(); }
        }

        private double _height = 0.8;  // デフォルト 0.8m
        public double Height
        {
            get => _height;
            set { if (SetProperty(ref _height, value)) RecalculateProperties(); }
        }

        // 断面積 (m²)
        private double _area = 0.4;
        public double Area
        {
            get => _area;
            set => SetProperty(ref _area, value);
        }

        // せん断断面積 Y方向 (m²)
        private double _shearAreaY = 0.333;
        public double ShearAreaY
        {
            get => _shearAreaY;
            set => SetProperty(ref _shearAreaY, value);
        }

        // せん断断面積 Z方向 (m²)
        private double _shearAreaZ = 0.333;
        public double ShearAreaZ
        {
            get => _shearAreaZ;
            set => SetProperty(ref _shearAreaZ, value);
        }

        // 断面ねじりモーメント (m⁴)
        private double _torsionalMoment = 0.0107;
        public double TorsionalMoment
        {
            get => _torsionalMoment;
            set => SetProperty(ref _torsionalMoment, value);
        }

        // 断面二次モーメント Y軸周り (m⁴)
        private double _momentOfInertiaYY = 0.0213;
        public double MomentOfInertiaYY
        {
            get => _momentOfInertiaYY;
            set => SetProperty(ref _momentOfInertiaYY, value);
        }

        // 断面二次モーメント Z軸周り (m⁴)
        private double _momentOfInertiaZZ = 0.0083;
        public double MomentOfInertiaZZ
        {
            get => _momentOfInertiaZZ;
            set => SetProperty(ref _momentOfInertiaZZ, value);
        }

        // 増減係数（ウィザードで設定、デフォルト1.0）
        private double _aFactor = 1.0;
        public double AFactor
        {
            get => _aFactor;
            set { if (SetProperty(ref _aFactor, value)) RecalculateProperties(); }
        }

        private double _ayFactor = 1.0;
        public double AyFactor
        {
            get => _ayFactor;
            set { if (SetProperty(ref _ayFactor, value)) RecalculateProperties(); }
        }

        private double _azFactor = 1.0;
        public double AzFactor
        {
            get => _azFactor;
            set { if (SetProperty(ref _azFactor, value)) RecalculateProperties(); }
        }

        private double _ixxFactor = 1.0;
        public double IxxFactor
        {
            get => _ixxFactor;
            set { if (SetProperty(ref _ixxFactor, value)) RecalculateProperties(); }
        }

        private double _iyyFactor = 1.0;
        public double IyyFactor
        {
            get => _iyyFactor;
            set { if (SetProperty(ref _iyyFactor, value)) RecalculateProperties(); }
        }

        private double _izzFactor = 1.0;
        public double IzzFactor
        {
            get => _izzFactor;
            set { if (SetProperty(ref _izzFactor, value)) RecalculateProperties(); }
        }

        /// <summary>
        /// 幅・高さ・係数から断面諸元を再計算（ウィザードと同じロジック）
        /// </summary>
        public void RecalculateProperties()
        {
            // 断面積 A = b × h × 係数A
            Area = Width * Height * AFactor;

            // せん断断面積（矩形断面: 5/6 × b × h）
            ShearAreaY = (5.0 / 6.0) * Width * Height * AyFactor;
            ShearAreaZ = (5.0 / 6.0) * Width * Height * AzFactor;

            // 断面ねじりモーメント Ixx（矩形断面）
            double ratio = Width / Height;
            double beta;
            if (ratio <= 1.0)
            {
                beta = (1.0 / 3.0) - 0.21 * ratio * (1.0 - Math.Pow(ratio, 4) / 12.0);
                TorsionalMoment = beta * Width * Math.Pow(Height, 3) * IxxFactor;
            }
            else
            {
                double invRatio = Height / Width;
                beta = (1.0 / 3.0) - 0.21 * invRatio * (1.0 - Math.Pow(invRatio, 4) / 12.0);
                TorsionalMoment = beta * Height * Math.Pow(Width, 3) * IxxFactor;
            }

            // 断面二次モーメント
            MomentOfInertiaYY = Width * Math.Pow(Height, 3) / 12.0 * IyyFactor;
            MomentOfInertiaZZ = Height * Math.Pow(Width, 3) / 12.0 * IzzFactor;
        }
    }
}
