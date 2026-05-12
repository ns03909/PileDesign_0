using System;
using System.Text.Json.Serialization;
using PileDesign.Models;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 梁材料（ヤング係数、せん断弾性係数、ポアソン比）
    /// </summary>
    public class BeamMaterial : BaseModel
    {
        // No プロパティは廃止。番号は所属コレクション内の位置 (1-based) として扱う。
        // 必要なときは FoundationBeamInput.GetMaterialNo(this) で取得する。

        private string _name = "FC30";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _youngModulus = 2.6589e7;  // デフォルト 2.6589×10^7 kN/m² (コンクリートFC30相当)
        public double YoungModulus
        {
            get => _youngModulus;
            set
            {
                if (SetProperty(ref _youngModulus, value))
                {
                    RecalculateShearModulus();
                }
            }
        }

        private double _shearModulus = 1.1079e7;  // デフォルト 1.1079×10^7 kN/m² (E/2.4, ポアソン比0.2)
        public double ShearModulus
        {
            get => _shearModulus;
            set => SetProperty(ref _shearModulus, value);
        }

        private double _poissonRatio = 0.2;  // デフォルト 0.2（コンクリート）
        public double PoissonRatio
        {
            get => _poissonRatio;
            set
            {
                // ポアソン比の有効範囲: 0.0 ～ 0.5
                var clamped = Math.Max(0.0, Math.Min(0.5, value));
                if (SetProperty(ref _poissonRatio, clamped))
                {
                    RecalculateShearModulus();
                }
            }
        }

        /// <summary>
        /// ヤング係数とポアソン比からせん断弾性係数を再計算: G = E / (2(1+ν))
        /// </summary>
        private void RecalculateShearModulus()
        {
            ShearModulus = YoungModulus / (2.0 * (1.0 + PoissonRatio));
        }
    }

    /// <summary>
    /// 梁断面（幅、高さ、断面性能）
    /// </summary>
    public class BeamSection : BaseModel
    {
        // No プロパティは廃止。番号は所属コレクション内の位置 (1-based) として扱う。

        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _width = 0.8;  // デフォルト 0.8m
        public double Width
        {
            get => _width;
            set { if (SetProperty(ref _width, value)) RecalculateProperties(); }
        }

        private double _height = 2.0;  // デフォルト 2.0m
        public double Height
        {
            get => _height;
            set { if (SetProperty(ref _height, value)) RecalculateProperties(); }
        }

        // 断面積 (m²) — デフォルト: 0.8 * 2.0 = 1.6
        private double _area = 1.6;
        public double Area
        {
            get => _area;
            set => SetProperty(ref _area, value);
        }

        // せん断断面積 Y方向 (m²) — デフォルト: 1.6 * 5/6 ≈ 1.333
        private double _shearAreaY = 1.333;
        public double ShearAreaY
        {
            get => _shearAreaY;
            set => SetProperty(ref _shearAreaY, value);
        }

        // せん断断面積 Z方向 (m²) — デフォルト: 1.6 * 5/6 ≈ 1.333
        private double _shearAreaZ = 1.333;
        public double ShearAreaZ
        {
            get => _shearAreaZ;
            set => SetProperty(ref _shearAreaZ, value);
        }

        // 断面ねじりモーメント (m⁴) — デフォルト: beta(0.4) * 0.8 * 2.0^3 ≈ 1.597
        private double _torsionalMoment = 1.597;
        public double TorsionalMoment
        {
            get => _torsionalMoment;
            set => SetProperty(ref _torsionalMoment, value);
        }

        // 断面二次モーメント Y軸周り (m⁴) — デフォルト: 0.8 * 2.0^3 / 12 ≈ 0.5333
        private double _momentOfInertiaYY = 0.5333;
        public double MomentOfInertiaYY
        {
            get => _momentOfInertiaYY;
            set => SetProperty(ref _momentOfInertiaYY, value);
        }

        // 断面二次モーメント Z軸周り (m⁴) — デフォルト: 2.0 * 0.8^3 / 12 ≈ 0.0853
        private double _momentOfInertiaZZ = 0.0853;
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
