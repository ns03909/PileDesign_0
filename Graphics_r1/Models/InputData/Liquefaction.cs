using PileDesign.FEM;
using System;

using PileDesign.Common;

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
        }
    }

}
