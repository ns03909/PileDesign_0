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
    public class BeamSection : BaseModel, System.Text.Json.Serialization.IJsonOnDeserialized
    {
        /// <summary>
        /// 矩形断面のねじり定数 J = β·a·b³ (a: 長辺、b: 短辺、β = 1/3 − 0.21·(b/a)·(1 − (b/a)⁴/12))。
        /// 断面の表・断面ウィザード・断面の無い梁要素のフォールバック (水平解析・鉛直解析) の 4 か所がこれを使う。
        ///
        /// 以前は断面の表とウィザードの 2 か所が長辺と短辺を取り違え (a³·b)、幅 0.5 m・せい 1.0 m の梁で
        /// J を約 4 倍大きく見積もっていた。断面の無い梁要素のフォールバックだけが正しい式だった。
        /// </summary>
        public static double RectangularTorsionConstant(double width, double height)
        {
            double a = Math.Max(width, height);
            double c = Math.Min(width, height);
            return a * c * c * c * (1.0 / 3.0 - 0.21 * c / a * (1 - c * c * c * c / (12 * a * a * a * a)));
        }

        /// <summary>
        /// 読込の直後に断面諸元を幅・高さ・係数から求め直す (どれも読み取り専用の派生値)。
        /// ねじり定数の式を直す前に保存したファイルの値を、正しい式の値にそろえるため。
        /// </summary>
        void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() => RecalculateProperties();

        /// <summary>
        /// 既定の幅・高さから断面諸元を求めておく。以前は丸めた既定値 (J = 1.597 など) を直に持っており、
        /// 既定と同じ幅・高さを代入しても求め直されず (値が変わらないので)、複製 (読込で求め直す) と値が食い違った
        /// (元に戻すのあとに入力の署名が変わり「再解析が必要」が誤って付いた。J は取り違えた式の値でもあった)。
        /// </summary>
        public BeamSection() => RecalculateProperties();

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

        // 断面積 (m²)。既定値はコンストラクタで幅・高さから求める
        private double _area;
        public double Area
        {
            get => _area;
            set => SetProperty(ref _area, value);
        }

        // せん断断面積 Y方向 (m²)
        private double _shearAreaY;
        public double ShearAreaY
        {
            get => _shearAreaY;
            set => SetProperty(ref _shearAreaY, value);
        }

        // せん断断面積 Z方向 (m²)
        private double _shearAreaZ;
        public double ShearAreaZ
        {
            get => _shearAreaZ;
            set => SetProperty(ref _shearAreaZ, value);
        }

        // 断面ねじりモーメント (m⁴)
        private double _torsionalMoment;
        public double TorsionalMoment
        {
            get => _torsionalMoment;
            set => SetProperty(ref _torsionalMoment, value);
        }

        // 断面二次モーメント Y軸周り (m⁴)
        private double _momentOfInertiaYY;
        public double MomentOfInertiaYY
        {
            get => _momentOfInertiaYY;
            set => SetProperty(ref _momentOfInertiaYY, value);
        }

        // 断面二次モーメント Z軸周り (m⁴)
        private double _momentOfInertiaZZ;
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

            // 断面ねじりモーメント Ixx（矩形断面。長辺 a・短辺 b で β·a·b³）
            TorsionalMoment = RectangularTorsionConstant(Width, Height) * IxxFactor;

            // 断面二次モーメント
            MomentOfInertiaYY = Width * Math.Pow(Height, 3) / 12.0 * IyyFactor;
            MomentOfInertiaZZ = Height * Math.Pow(Width, 3) / 12.0 * IzzFactor;
        }
    }
}
