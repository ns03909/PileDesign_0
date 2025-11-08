using PileDesign.FEM;
using PileDesign.ViewModels;
using System;

namespace PileDesign.Models.InputData
{
    [Serializable]
    public class Liquefaction : BaseDataItem // 液状化
    {
        private static readonly double[] TauDonSigmaZPrimeSet = [0.05, 0.08, 0.10, 0.15, 0.20, 0.30, 0.40, 0.50, 0.60];
        private static readonly double[] NaSet_0005 = [0.00, 5.75, 8.45, 15.75, 20.35, 25.00, 27.25, 28.00, 28.00];
        private static readonly double[] NaSet_0510 = [0.00, 5.25, 7.70, 13.00, 16.60, 20.75, 22.25, 22.75, 22.50];
        private static readonly double[] NaSet_1020 = [0.00, 4.75, 6.95, 11.25, 13.85, 16.50, 17.25, 17.50, 17.00];
        private static readonly double[] NaSet_2040 = [0.00, 4.25, 5.75, 08.50, 10.00, 11.05, 11.20, 11.10, 10.75];
        private static readonly double[] NaSet_4080 = [0.00, 3.20, 4.50, 05.85, 06.50, 06.55, 06.25, 06.00, 05.70];

        private static readonly double[] betaLSet = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
        private static readonly double[] Na0010 = [0.0, 10.0, 15.7, 19.0, 21.0, 22.4, 23.4, 24.0, 24.5, 24.8, 25.0];
        private static readonly double[] Na1020 = [0.0, 6.0, 10.0, 12.5, 14.5, 16.0, 17.3, 18.3, 19.0, 19.6, 20.0];

        internal static bool IsLiquefactionLayer(double groundWaterGLDepth, double z, double Fc)
        // _groundWaterGLDepth 負の数
        // _z 負の数
        {
            if (groundWaterGLDepth < z)　// 地下水位より高い場合
            { return false; }
            else if (z <= -20) // 深さが20m以深の場合
            { return false; }
            else if (Fc > 35) // Fcが35より大きい場合
            { return false; }
            else { return true; }
        }

        internal static double CalculateGammaCy(double Na, double tauDonSigmaZPrime)
        {
            double gammaCy = 0.0;
            //double[] _TauDonSigmaZPrimeSet = [0.05, 0.08, 0.10, 0.15, 0.20, 0.30, 0.40, 0.50, 0.60];
            //double[] _NaSet_0005 = [0.00, 5.75, 8.45, 15.75, 20.35, 25.00, 27.25, 28.00, 28.00];
            //double[] _NaSet_0510 = [0.00, 5.25, 7.70, 13.00, 16.60, 20.75, 22.25, 22.75, 22.50];
            //double[] _NaSet_1020 = [0.00, 4.75, 6.95, 11.25, 13.85, 16.50, 17.25, 17.50, 17.00];
            //double[] _NaSet_2040 = [0.00, 4.25, 5.75, 08.50, 10.00, 11.05, 11.20, 11.10, 10.75];
            //double[] _NaSet_4080 = [0.00, 3.20, 4.50, 05.85, 06.50, 06.55, 06.25, 06.00, 05.70];


            if (tauDonSigmaZPrime < TauDonSigmaZPrimeSet[0])
                return 0.0;

            double Na_0005 = Utils.Interpolate(TauDonSigmaZPrimeSet, NaSet_0005, tauDonSigmaZPrime);
            double Na_0510 = Utils.Interpolate(TauDonSigmaZPrimeSet, NaSet_0510, tauDonSigmaZPrime);
            double Na_1020 = Utils.Interpolate(TauDonSigmaZPrimeSet, NaSet_1020, tauDonSigmaZPrime);
            double Na_2040 = Utils.Interpolate(TauDonSigmaZPrimeSet, NaSet_2040, tauDonSigmaZPrime);
            double Na_4080 = Utils.Interpolate(TauDonSigmaZPrimeSet, NaSet_4080, tauDonSigmaZPrime);


            //double _Na_0005 = 0.0;
            //double _Na_0510 = 0.0;
            //double _Na_1020 = 0.0;
            //double _Na_2040 = 0.0;
            //double _Na_4080 = 0.0;
            //if (_TauDonSigmaZPrime < _TauDonSigmaZPrimeSet[0])
            //{
            //    _gammaCy = 0.0;
            //}
            //else
            //{
            //    for (int i = 0; i < _TauDonSigmaZPrimeSet.Length; i++)
            //    {
            //        if (i == _TauDonSigmaZPrimeSet.Length - 1)
            //        {
            //            _Na_0005 = _NaSet_0005[i];
            //            _Na_0510 = _NaSet_0510[i];
            //            _Na_1020 = _NaSet_1020[i];
            //            _Na_2040 = _NaSet_2040[i];
            //            _Na_4080 = _NaSet_4080[i];
            //            break;
            //        }

            //        else if (_TauDonSigmaZPrimeSet[i] <= _TauDonSigmaZPrime && _TauDonSigmaZPrime < _TauDonSigmaZPrimeSet[i + 1])
            //        {
            //            _Na_0005 = (_NaSet_0005[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
            //                + _NaSet_0005[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
            //                / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

            //            _Na_0510 = (_NaSet_0510[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
            //                + _NaSet_0510[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
            //                / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

            //            _Na_1020 = (_NaSet_1020[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
            //                + _NaSet_1020[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
            //                / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

            //            _Na_2040 = (_NaSet_2040[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
            //                + _NaSet_2040[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
            //                / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

            //            _Na_4080 = (_NaSet_4080[i + 1] * (_TauDonSigmaZPrime - _TauDonSigmaZPrimeSet[i])
            //                + _NaSet_4080[i] * (-_TauDonSigmaZPrime + _TauDonSigmaZPrimeSet[i + 1]))
            //                / (_TauDonSigmaZPrimeSet[i + 1] - _TauDonSigmaZPrimeSet[i]);

            //            break;
            //        }
            //    }
            //}
            //if (_Na_0005 <= _Na) { _gammaCy = 0.0; }
            //else if (_Na_0510 <= _Na) { _gammaCy = 0.5; }
            //else if (_Na_1020 <= _Na) { _gammaCy = 1.0; }
            //else if (_Na_2040 <= _Na) { _gammaCy = 2.0; }
            //else if (_Na_4080 <= _Na) { _gammaCy = 4.0; }
            //else { _gammaCy = 8.0; }
            gammaCy = Na switch
            {
                var na when na >= Na_0005 => 0.0,
                var na when na >= Na_0510 => 0.5,
                var na when na >= Na_1020 => 1.0,
                var na when na >= Na_2040 => 2.0,
                var na when na >= Na_4080 => 4.0,
                _ => 8.0
            };

            return gammaCy;
        }

        internal static double CalculateBetaL(double z, double Na)
        {
            //double[] _betaLSet = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
            //double[] _Na0010 = [0.0, 10.0, 15.7, 19.0, 21.0, 22.4, 23.4, 24.0, 24.5, 24.8, 25.0];
            //double[] _Na1020 = [0.0, 6.0, 10.0, 12.5, 14.5, 16.0, 17.3, 18.3, 19.0, 19.6, 20.0];


            if (-10 < z && z <= 0)
                return Utils.Interpolate(Na0010, betaLSet, Na);
            else
                return Utils.Interpolate(Na1020, betaLSet, Na);
            //double _betaL = 1.0;
            //if (-10 < _z && _z <= 0)
            //{
            //    for (int i = 0; i < _betaLSet.Length; ++i)
            //    {
            //        if (i == _betaLSet.Length - 1)
            //        {
            //            _betaL = 1.0;
            //            break;
            //        }
            //        else if (_Na0010[i] <= _Na && _Na < _Na0010[i + 1])
            //        {
            //            _betaL = (_betaLSet[i + 1] * (_Na - _Na0010[i])
            //                + _betaLSet[i] * (-_Na + _Na0010[i + 1]))
            //                / (_Na0010[i + 1] - _Na0010[i]);
            //            break;
            //        }
            //    }
            //}
            //else
            //{
            //    for (int i = 0; i < _betaLSet.Length; ++i)
            //    {
            //        if (i == _betaLSet.Length - 1)
            //        {
            //            _betaL = 1.0;
            //            break;
            //        }
            //        else if (_Na1020[i] <= _Na && _Na < _Na1020[i + 1])
            //        {
            //            _betaL = (_betaLSet[i + 1] * (_Na - _Na1020[i])
            //                + _betaLSet[i] * (-_Na + _Na1020[i + 1]))
            //                / (_Na1020[i + 1] - _Na1020[i]);
            //            break;
            //        }
            //    }
            //}
            //return _betaL;
        }
    }

    // GroundMassDataItemクラス
    [Serializable]
    public class GroundMassDataItem(GroundLayerViewModel viewModel) : BaseDataItem
    {
        private readonly GroundLayerViewModel viewModel = viewModel;
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
            set => SetProperty(ref _spacing, value);
        }

        private double? _H;
        public double? H // 土質点厚
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
            set => SetProperty(ref _NValue, value);
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

        private double? _rD;
        public double? RD // 低減係数
        {
            get => _rD;
            set => SetProperty(ref _rD, value);
        }

        private double? _N1;
        public double? N1 // 換算N値
        {
            get => _N1;
            set => SetProperty(ref _N1, value);
        }

        private double? _deltaNf;
        public double? DeltaNf // N値増分
        {
            get => _deltaNf;
            set => SetProperty(ref _deltaNf, value);
        }

        private double? _NL;
        public double? NL // 補正N値
        {
            get => _NL;
            set => SetProperty(ref _NL, value);
        }

        private double? _tauLonSigmaZPrime;
        public double? TauLonSigmaZPrime // 液状化抵抗比
        {
            get => _tauLonSigmaZPrime;
            set => SetProperty(ref _tauLonSigmaZPrime, value);
        }

        private double? _tauDonSigmaZPrime;
        public double? TauDonSigmaZPrime // 繰り返しせん断応力比
        {
            get => _tauDonSigmaZPrime;
            set => SetProperty(ref _tauDonSigmaZPrime, value);
        }

        private double? _FL = 99.0;
        public double? FL // 安全率
        {
            get => _FL;
            set => SetProperty(ref _FL, value);
        }

        private double? _betaL;
        public double? BetaL // 低減率
        {
            get => _betaL;
            set => SetProperty(ref _betaL, value);
        }

        private double? _gammaCy;
        public double? GammaCy // 繰り返しせん断ひずみ
        {
            get => _gammaCy;
            set => SetProperty(ref _gammaCy, value);
        }

        private double? _sigmaGammaCyH;
        public double? SigmaGammaCyH // 液状化水平変位
        {
            get => _sigmaGammaCyH;
            set => SetProperty(ref _sigmaGammaCyH, value);
        }

        private double _density;
        public double Density // 単位体積重量
        {
            get => _density;
            set => SetProperty(ref _density, value);
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
            set => SetProperty(ref _VS0, value);
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
}
