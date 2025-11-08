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
    }

}
