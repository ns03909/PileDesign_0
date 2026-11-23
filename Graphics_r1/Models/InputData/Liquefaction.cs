using PileDesign.FEM;
using System;

using PileDesign.Common;

namespace PileDesign.Models.InputData
{
    [Serializable]
    public class Liquefaction : BaseDataItem // 液状化
    {
        // 基礎指針'19 図3.2.6 (補正 N 値と繰返しせん断ひずみの関係) の読み取り。
        // NaSet_xxyy は γcy = xx〜yy% の範囲の境 — <b>曲線そのものではなく、隣り合う曲線の中間の線</b>
        // (例: NaSet_0510 は 0.5% と 1% の曲線の中間。τd/σz' = 0.3 で図の 0.5% ≈ 21.5、1% ≈ 19 に対し 20.75)。
        // NaSet_0005 は 0.5% の曲線より右へ間隔の半分ほどの線で、これより右は γcy = 0。
        // だから CalculateGammaCy は「もっとも近い曲線」を採る。表を曲線と読み違えると
        // 「ひずみの小さい側に切り下げている (危険側)」と誤解する (2026-11-23 に一度そう誤読した)。
        private static readonly double[] TauDonSigmaZPrimeSet = [0.05, 0.08, 0.10, 0.15, 0.20, 0.30, 0.40, 0.50, 0.60];
        private static readonly double[] NaSet_0005 = [0.00, 5.75, 8.45, 15.75, 20.35, 25.00, 27.25, 28.00, 28.00];
        private static readonly double[] NaSet_0510 = [0.00, 5.25, 7.70, 13.00, 16.60, 20.75, 22.25, 22.75, 22.50];
        private static readonly double[] NaSet_1020 = [0.00, 4.75, 6.95, 11.25, 13.85, 16.50, 17.25, 17.50, 17.00];
        private static readonly double[] NaSet_2040 = [0.00, 4.25, 5.75, 08.50, 10.00, 11.05, 11.20, 11.10, 10.75];
        private static readonly double[] NaSet_4080 = [0.00, 3.20, 4.50, 05.85, 06.50, 06.55, 06.25, 06.00, 05.70];

        private static readonly double[] betaLSet = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
        private static readonly double[] Na0010 = [0.0, 10.0, 15.7, 19.0, 21.0, 22.4, 23.4, 24.0, 24.5, 24.8, 25.0];
        private static readonly double[] Na1020 = [0.0, 6.0, 10.0, 12.5, 14.5, 16.0, 17.3, 18.3, 19.0, 19.6, 20.0];

        /// <summary>
        /// 液状化の判定の対象か。基礎指針'19 3.2 (1) の「原則的に地表面から 20 m 程度以浅、細粒分含有率 35% 以下の飽和土層」。
        /// 指針は例外 (造成地盤が 20 m 以深まで続くなら下端まで、粘土分 10% 以下か塑性指数 15 以下の埋立・盛土は Fc &gt; 35% でも、
        /// 洪積層でも N 値が小さい層・透水性の低い層に囲まれた礫も検討) を挙げており、それは設計者が土質点ごとに
        /// <paramref name="judgement"/> で決める (<see cref="GroundMassDataInput.LiquefactionJudgement"/>)。
        /// 「対象にする」でも地下水位より上は飽和していないので対象にしない。工学的基盤は呼び出し側で外す。
        /// </summary>
        /// <param name="groundWaterGLDepth">地下水位の GL 深さ (下向き負)</param>
        /// <param name="z">土質点の GL 深さ (下向き負)</param>
        internal static bool IsLiquefactionLayer(double groundWaterGLDepth, double z, double Fc, string? judgement = null)
        {
            if (judgement == GroundMassDataInput.LiquefactionExclude) return false;
            if (judgement == GroundMassDataInput.LiquefactionInclude) return groundWaterGLDepth >= z;

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

        /// <summary>
        /// 液状化による地盤反力係数の低減率 βLq (基礎指針'19 図3.2.13。指針の記号は βL)。
        /// 図の 2 本の曲線 (0 ≦ z &lt; 10 m・z ≧ 10 m) を βLq = 0.1 刻みで読み取った Na (<see cref="Na0010"/>・<see cref="Na1020"/>) から線形補間する。
        /// </summary>
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
