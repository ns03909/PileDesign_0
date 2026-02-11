using System;

namespace PileDesign.Models.InputData
{

    // 鋼管杭断面クラス
    internal class SteelPipeSection
    {
        public double D { get; }
        public double T { get; }
        public double F { get; }
        public double Beta1 { get; }
        public double Nsc { get; protected set; }
        public double Nst { get; protected set; }
        public double SNsc1 { get; protected set; }
        public double SNst { get; protected set; }
        public double Sfc1 { get; protected set; }
        public double Sft { get; protected set; }
        public double SAp { get; protected set; }
        public double SZe { get; protected set; }
        public double SNdc1 { get; protected set; }
        public double SNdt { get; protected set; }


        // コンストラクタ
        internal SteelPipeSection(double _D, double _T, double _F, double _beta1)
        {
            D = _D;
            T = _T;
            F = _F;
            Beta1 = _beta1;
            GetSectionProperties();
        }

        // 断面プロパティ取得メソッド
        internal void GetSectionProperties()
        {
            SAp = (Math.Pow(D, 2) - Math.Pow(D - 2 * T, 2)) / 4.0 * Math.PI;
            if (25 < D / T)
            {
                Sfc1 = F / 1.5 * (0.5 + 5.0 / (D / T));
            }
            else
            {
                Sfc1 = F / 1.5;
            }
            Sft = F / 1.5;
            SNsc1 = Sfc1 * SAp;
            SNst = Sft * SAp;
            Nsc = Beta1 * SNsc1;
            Nst = Beta1 * SNst;
            SZe = (Math.Pow(D, 4) - Math.Pow(D - 2 * T, 4)) / (32.0 * D) * Math.PI;
            SNdc1 = 1.5 * Sfc1 * SAp;
            SNdt = 1.5 * Sft * SAp;
        }

        // 使用限界モーメント取得メソッド
        internal double GetServiceLimitMoment(double Nsd)
        {
            double sf;
            if (0 <= Nsd) { sf = Sfc1; } else { sf = Sft; }
            return Beta1 * (sf - Math.Abs(Nsd) / SAp) * SZe;
        }

        // 損傷限界モーメント取得メソッド
        internal double GetDamageLimitMoment(double Ndd)
        {
            return Beta1 * (1.5 * Sfc1 - Math.Abs(Ndd) / SAp) * SZe;
        }

        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double nud, bool isFactored)
        {
            // 鋼管杭の安全限界せん断
            double beta1 = 1.0;
            double beta2 = 1.0;
            double sSigmaY = F;         // N/mm²
            double sNy = sSigmaY * SAp;  // N（降伏軸力）

            // sNy が 0 の場合は計算不能
            if (Math.Abs(sNy) < 1e-10)
                return 0.0;

            double eta = nud / sNy;  // 軸力比 η = N / Ny

            // η >= 1 の場合、sqrt(1 - η²) が虚数になるため、0 を返す
            // （軸力が降伏軸力以上のとき、せん断耐力は 0）
            if (Math.Abs(eta) >= 1.0)
                return 0.0;

            double sQ0 = 2 * T * (D - T) * sSigmaY / Math.Sqrt(3);  // N
            double unfactoredQu = sQ0 * Math.Sqrt(1 - eta * eta);

            return isFactored ? beta1 * beta2 * unfactoredQu : unfactoredQu;
        }
    }

}
