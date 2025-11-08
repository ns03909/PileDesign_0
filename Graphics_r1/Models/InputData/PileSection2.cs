using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // 材料の基底クラス
    internal abstract class Material
    {
        public double Prestrain { get; internal set; }

        public double ServiceLimitStrainC { get; protected set; }
        public double DamageLimitStrainC { get; protected set; }
        public double UltimateLimitStrainC { get; protected set; }

        public double ServiceLimitStrainT { get; protected set; }
        public double DamageLimitStrainT { get; protected set; }
        public double UltimateLimitStrainT { get; protected set; }

        internal abstract double GetStress(string type, double epsilon);
    }

    // 現地打ちコンクリートクラス
    internal class InsituConcrete : Material
    {
        public double DO { get; }
        public string Type { get; }
        public double Gsi { get; }
        public double Fc { get; }
        public double Ec { get; private set; }
        public double Ac { get; private set; }
        public double Epsilon { get; private set; }
        public double EpsilonM { get; }
        public double EpsilonCu { get; }
        public double EpsilonCr_bilinear { get; private set; }
        public double EpsilonCr_eFunction { get; private set; }
        public double SigmaCr { get; }

        // コンストラクタ
        public InsituConcrete(double _DO, double gsi, double _Fc, string type = "普通", double epsilonM = 0.002, double epsilonCu = 0.003)
        {
            DO = _DO;
            Gsi = gsi;
            Fc = _Fc;
            EpsilonM = epsilonM;
            EpsilonCu = epsilonCu;
            Type = type;
            Ec = GetEc();
            Ac = Math.PI * Math.Pow(DO, 2) / 4.0;
            SigmaCr = 0.56 * Math.Sqrt(Gsi * Fc);
            SetEpsilonCr();
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = 1.0 / 3.0 * Gsi * Fc; // 使用限界圧縮応力度
            double damageLimitStressC = 2.0 / 3.0 * Gsi * Fc;// 損傷限界圧縮応力度
            ServiceLimitStrainC = serviceLimitStressC / Ec; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / Ec; // 損傷限界圧縮ひずみ度
            ServiceLimitStrainT = double.MinValue; // 使用限界引張ひずみ度
            DamageLimitStrainT = double.MinValue; // 損傷限界引張ひずみ度
        }

        // 密度を計算するメソッド
        internal static double GetDensity(double _Fc = 27, string type = "普通")
        {
            if (type == "軽量1種")
            {
                if (_Fc <= 27) { return 20.0 - 1.0; }
                else if (_Fc <= 36) { return 22.0 - 1.0; }
                else { return 25.5 - 1.0; }
            }
            else if (type == "軽量2種")
            {
                if (_Fc <= 27) { return 18.0 - 1.0; }
                else { return 25.5 - 1.0; }
            }
            else // (type == "普通")
            {
                if (_Fc <= 36) { return 24.0 - 1.0; }
                else if (_Fc <= 48) { return 24.5 - 1.0; }
                else if (_Fc <= 60) { return 25.0 - 1.0; }
                else { return 25.5 - 1.0; }
            }
        }

        // 弾性係数を計算するメソッド
        internal double GetEc(double gamma = 0)
        {
            if (gamma == 0)
            {
                gamma = GetDensity(Fc);
            }
            return 3.35 * Math.Pow(10, 4) * Math.Pow(gamma / 24, 2) * Math.Pow(Gsi * Fc / 60, 1.0 / 3.0);
        }

        internal void SetEpsilonCr()
        {
            EpsilonCr_bilinear = SigmaCr / Ec;
            EpsilonCr_eFunction = GetEFuncEpsilon(SigmaCr);
        }

        // ひずみ度から応力を計算するメソッド// ひずみ度から応力を計算するメソッド 使用限界、損傷限界用
        internal override double GetStress(string type, double epsilon)
        {
            if (type == "linear")
            {
                //if (epsilon > -EpsilonCr_linear) { return Ec * epsilon; } // 引張側を無視した線形弾性
                if (epsilon > 0) { return Ec * epsilon; } // 引張側を無視した線形弾性

                else
                { return 0.0; }
            }
            else if (type == "eFunction")
            {
                if (-EpsilonCr_eFunction <= epsilon && epsilon <= 0.003)
                {
                    epsilon = Math.Min(epsilon, EpsilonCu);
                    return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
                }
                else
                { return 0.0; }
            }
            else // (type == "bilinear")
            {
                if (-EpsilonCr_bilinear <= epsilon && epsilon <= 0.003)
                {
                    return Math.Min(Ec * epsilon, Fc);
                }
                else
                { return 0.0; }
            }
        }

        internal double GetEFuncSigma(double epsilon)
        {
            return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
        }

        internal double GetEFuncDSonDEpsilon(double epsilon)
        {
            return 6.75 * (-0.812 / EpsilonM * Math.Exp(-0.812 * epsilon / EpsilonM) - (-1.218 / EpsilonM) * Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
        }

        internal double GetEFuncEpsilon(double sigma)
        {
            // 初期値（線形近似）
            double epsilon = Math.Max(0.0, Math.Min(sigma / Ec, EpsilonCu));
            const int maxIter = 30;
            const double tol = 1e-6;

            for (int i = 0; i < maxIter; i++)
            {
                double f = GetEFuncSigma(epsilon) - sigma;
                if (Math.Abs(f) < tol)
                    return epsilon;

                double df = GetEFuncDSonDEpsilon(epsilon);
                if (Math.Abs(df) < 1e-12)
                    break;

                // Newtonステップ
                double step = f / df;
                // ステップ幅制限
                if (Math.Abs(step) > Math.Abs(epsilon) * 0.5)
                    step = Math.Sign(step) * Math.Abs(epsilon) * 0.5;

                double epsilonNext = epsilon - step;
                epsilonNext = Math.Max(0.0, Math.Min(epsilonNext, EpsilonCu));

                if (Math.Abs(epsilonNext - epsilon) < tol)
                    return epsilonNext;

                epsilon = epsilonNext;
            }
            // 収束しない場合は端点
            return Math.Max(0.0, Math.Min(epsilon, EpsilonCu));
        }
    }

    // 主筋クラス
    internal class MainBars : Material
    {
        public double PCD { get; }
        public int Number { get; }
        public string Grade { get; }
        public string BarSize { get; }
        public double Ag { get; private set; }
        public double Er { get; private set; }
        public double RSigmaY { get; private set; }

        public double EpsilonE { get; private set; }
        public double EpsilonSi { get; set; }

        // コンストラクタ
        internal MainBars(double _PCD, int _Number, string _Grade, string _BarSize)
        {
            PCD = Math.Max(_PCD, 100); ///////////
            Number = Math.Max(_Number, 4); ///////////
            if (_Grade != "") { Grade = _Grade; } else { Grade = "SD345"; }
            ;
            PCD = Math.Max(_PCD, 100);
            Number = Math.Max(_Number, 4);
            Grade = !string.IsNullOrEmpty(_Grade) ? _Grade : "SD345";
            BarSize = !string.IsNullOrEmpty(_BarSize) ? _BarSize : "D25";

            SetRSigmaY();
            SetAg();
            Er = 205000;
            EpsilonSi = 0.0;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = 195.0;  // 使用限界圧縮応力度 鋼管コンクリート杭では(2/3)RSigmaY
            double damageLimitStressC = RSigmaY; // 損傷限界圧縮応力度
            double serviceLimitStressT = -195.0; // 使用限界圧縮応力度 鋼管コンクリート杭では-(2/3)RSigmaY
            double damageLimitStressT = -RSigmaY; // 損傷限界圧縮応力度

            ServiceLimitStrainC = serviceLimitStressC / Er; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / Er; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = serviceLimitStressT / Er; // 使用限界引張ひずみ度
            DamageLimitStrainT = damageLimitStressT / Er; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = double.MinValue; // 安全限界引張ひずみ度
        }

        // 降伏応力度を設定するメソッド
        internal void SetRSigmaY()
        {
            if (Grade == "SD295") { RSigmaY = 295.0; }
            else if (Grade == "SD345") { RSigmaY = 345.0; }
            else if (Grade == "SD390") { RSigmaY = 390.0; }
            else if (Grade == "SD490") { RSigmaY = 490.0; }
            else if (Grade == "SD685") { RSigmaY = 685.0; }
            else { RSigmaY = 295.0; }
        }

        // 断面積を返すメソッド
        internal void SetAg()
        {
            double area;
            if (BarSize == "D10") { area = 71.3; }
            else if (BarSize == "D13") { area = 127.0; }
            else if (BarSize == "D16") { area = 199.0; }
            else if (BarSize == "D19") { area = 287.0; }
            else if (BarSize == "D22") { area = 387.0; }
            else if (BarSize == "D25") { area = 507.0; }
            else if (BarSize == "D29") { area = 642.0; }
            else if (BarSize == "D32") { area = 794.0; }
            else if (BarSize == "D35") { area = 957.0; }
            else if (BarSize == "D38") { area = 1140.0; }
            else if (BarSize == "D41") { area = 1340.0; }
            else if (BarSize == "D51") { area = 2027.0; }
            else
            {
                area = 0.0;
                Console.WriteLine($"Warning: Invalid BarSize '{BarSize}' detected.");
            }
            Ag = Number * area;
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (RSigmaY / Er < epsilon + EpsilonSi) { return RSigmaY; }
            else if (epsilon + EpsilonSi < -RSigmaY / Er) { return -RSigmaY; }
            else { return Er * (epsilon + EpsilonSi); }
        }
    }

    // 現場打ち鋼管クラス
    internal class InsituSteelPipe : Material
    {
        public string Grade { get; }
        public double OutDia { get; }
        public double T { get; }
        public double Tminus => T - 1.0;
        public double SSigmaU { get; private set; }
        public double F { get; private set; }
        public double Fcy => 1.1 * F;
        public double OutDiaminus => OutDia - 2.0;
        public double Aminus => Math.PI * (OutDia - Tminus) * Tminus;
        public double SSigmaY { get; private set; }　// 材料強度 1.1F
        public double SEpsilonY { get; private set; }
        public double SEpsilonU { get; private set; }
        public double SE1 { get; private set; } = 205000.0;
        public double SE2 { get; private set; } = 205000.0 / 30.0;
        public double Iminus => Math.PI * (Math.Pow(OutDiaminus, 4) - Math.Pow(OutDiaminus - 2 * Tminus, 4)) / 64.0;

        // コンストラクタ
        public InsituSteelPipe(string _Grade, double _OutDia, double _T, double _corrosionDepth)
        {
            Grade = _Grade;
            OutDia = _OutDia - 2 * _corrosionDepth;
            T = _T - _corrosionDepth;

            SetMaterialProperties();
        }

        private void SetMaterialProperties()
        {
            if (Grade == "SKK400") { SSigmaU = 400.0; F = 235; }
            else if (Grade == "SKK490") { SSigmaU = 490.0; F = 325; }
            SSigmaY = 1.1 * F;
            SEpsilonY = SSigmaY / SE1;
            SEpsilonU = (SSigmaU - SSigmaY) / SE2 + SEpsilonY;

            SetAllowableStrain(F);
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain(double F)
        {
            double serviceLimitStressC = F / 1.5;
            double damageLimitStressC = F;
            double serviceLimitStressT = -F / 1.5;
            double damageLimitStressT = -F;
            ServiceLimitStrainC = serviceLimitStressC / SE1; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / SE1; // 損傷限界圧縮ひずみ度
            ServiceLimitStrainT = serviceLimitStressT / SE1; // 使用限界引張ひずみ度
            DamageLimitStrainT = damageLimitStressT / SE1; // 損傷限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (SEpsilonU < epsilon) { return SSigmaU; }
            else if (SEpsilonY < epsilon) { return SSigmaY + SE2 * (epsilon - SEpsilonY); }
            else if (epsilon < -SEpsilonU) { return -SSigmaU; }
            else if (epsilon < -SEpsilonY) { return -SSigmaY + SE2 * (epsilon + SEpsilonY); }
            else { return epsilon * SE1; }
        }
    }

    // プレキャストコンクリートクラス
    internal class PrecastConcrete : Material
    {
        public double Fc { get; protected set; }
        public double EpsilonCu { get; protected set; }
        public double EpsilonCy { get; protected set; }
        public double Ec { get; protected set; } = 40000.0;

        public double DO { get; protected set; }
        public double DI { get; protected set; }
        public double EpsilonE { get; set; }

        // コンストラクタ
        public PrecastConcrete()
        {

        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (EpsilonCy <= EpsilonE + epsilon) // && (EpsilonE + epsilon) <= EpsilonCu) // 降伏域
            {
                return Fc;
            }
            else if (0 < EpsilonE + epsilon && EpsilonE + epsilon < EpsilonCy)
            {
                return Ec * (EpsilonE + epsilon); // 弾性範囲
            }
            else
            {
                return 0.0;
            }
        }
    }

    internal class PrecastPHCConcrete : PrecastConcrete
    {

        // コンストラクタ
        public PrecastPHCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            if (Fc < 105) // FC = 85
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else // FC = 105
            {
                EpsilonCu = Fc / Ec;
                EpsilonCy = Fc / Ec;
            }

            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = Fc / 3.5;
            double damageLimitStressC = Fc * 2.0 / 3.5;
            double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
            double damageLimitStressT = -0.56 * Math.Sqrt(Fc);

            ServiceLimitStrainC = serviceLimitStressC / Ec;
            DamageLimitStrainC = damageLimitStressC / Ec;
            UltimateLimitStrainC = EpsilonCu;

            ServiceLimitStrainT = serviceLimitStressT / Ec;
            DamageLimitStrainT = damageLimitStressT / Ec;
            UltimateLimitStrainT = double.MinValue;
        }
    }

    internal class PrecastPRCConcrete : PrecastConcrete
    {
        // コンストラクタ
        public PrecastPRCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            if (Fc < 105) // FC = 85
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else // FC = 105
            {
                EpsilonCu = Fc / Ec;
                EpsilonCy = Fc / Ec;
            }

            //DO = _DO;
            //DI = _DI;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = Fc / 3.5;
            double damageLimitStressC = Fc * 2.0 / 3.5;

            double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
            double damageLimitStressT = double.MinValue; // <<<<<

            ServiceLimitStrainC = serviceLimitStressC / Ec;
            DamageLimitStrainC = damageLimitStressC / Ec; // <<<<<
            UltimateLimitStrainC = EpsilonCu;

            ServiceLimitStrainT = serviceLimitStressT / Ec;
            DamageLimitStrainT = damageLimitStressT / Ec;
            UltimateLimitStrainT = double.MinValue;
        }
    }

    internal class PrecastSCConcrete : PrecastConcrete
    {
        // コンストラクタ
        public PrecastSCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            //double ts = (DO - DI) * 0.5;
            //if ((DO - 2 * ts) / ts <= 6)
            //{
            EpsilonCu = 0.0030; // とりあえず3000マイクロとする。(7.6)
            EpsilonCy = Fc / Ec;
            //}
            //else //  ((DO - 2 * ts) / ts > 6)
            //{
            //    EpsilonCu = 0.0030;
            //    EpsilonCy = Fc / Ec;
            //}
            //DO = _DO;
            //DI = _DI;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = Fc / 3.5;
            double damageLimitStressC = Fc * 2.0 / 3.5;

            //double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
            /* double damageLimitStressT = Double.MinValue;*/ // <<<<<

            ServiceLimitStrainC = serviceLimitStressC / Ec;
            DamageLimitStrainC = damageLimitStressC / Ec; // <<<<<
            //UltimateLimitStrainC = EpsilonCu;

            ServiceLimitStrainT = double.MinValue;
            DamageLimitStrainT = double.MinValue;
            UltimateLimitStrainT = double.MinValue;
        }
    }

    // PC鋼材クラス
    internal class Tendons : Material
    {
        public double Fpy { get; }
        public double Fpu { get; }
        public double PCD { get; }
        public double Ap { get; }
        public double Ep { get; private set; } = 200000.0;
        public double Ep2 { get; private set; }
        public double EpsilonPu { get; private set; }
        public double EpsilonPy { get; private set; }
        public double SigmaE { get; private set; }
        public double EpsilonPi { get; set; }

        // コンストラクタ
        public Tendons(double _PCD, double _ap, double _fpy = 1226.0, double _fpu = 1418.0, double _epsilonPu = 0.02)
        {
            if (_fpy == 0.0) { Fpy = 1226.0; }
            else { Fpy = _fpy; }
            if (_fpu == 0.0) { Fpy = 1418.0; }
            else { Fpu = _fpu; }

            PCD = _PCD;
            Ap = _ap;
            EpsilonPu = _epsilonPu; // 正値
            EpsilonPy = Fpy / Ep; // 正値
            Ep2 = (Fpu - Fpy) / (0.015 - EpsilonPy);

            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            ServiceLimitStrainC = double.MaxValue; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = double.MaxValue; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = double.MinValue; // 使用限界引張ひずみ度
            DamageLimitStrainT = double.MinValue; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = -EpsilonPu; // 安全限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (0 < EpsilonPi + epsilon) { return 0.0; } // 圧縮の場合
            else if (-EpsilonPy < EpsilonPi + epsilon) // 第一勾配
            {
                return Ep * (EpsilonPi + epsilon);
            }
            else if (-0.015 < EpsilonPi + epsilon) // 第二勾配
            {
                return -Fpy + (EpsilonPi + epsilon + EpsilonPy) * Ep2;
            }
            else
            {
                return -Fpu;
            }
        }
    }

    // プレキャスト鋼管クラス
    internal class PrecastSteelPipe : Material
    {
        public string Grade { get; private set; }
        public double OutDia { get; private set; }
        public double T { get; private set; }
        public double F { get; private set; }
        public double SE1 { get; private set; } = 205000.0;
        public double Ftsp { get; private set; }
        public double Fcsp { get; private set; }
        public double Ftdp { get; private set; }
        public double Fcdp { get; private set; }
        public double Fys { get; private set; }
        public double EpsilonY { get; private set; }
        public double As { get; private set; }

        // コンストラクタ
        public PrecastSteelPipe(string _Grade, double _OutDia, double _T, double _corrosionDepth)
        {
            Grade = _Grade;
            OutDia = _OutDia - 2 * _corrosionDepth;
            T = _T - _corrosionDepth;

            SetMaterialProperties();
        }

        //プロパティをセットするメソッド
        private void SetMaterialProperties()
        {
            // 鋼管の設計基準強度
            if (Grade == "SKK400")
            {
                F = 235.0;
            }
            else if (Grade == "SKK490")
            {
                F = 325.0;
            }
            As = (OutDia - T) * Math.PI * T;
            Ftsp = -F / 1.5; //鋼管の使用限界引張応力度
            Fcsp = F / 1.5; //鋼管の使用限界圧縮応力度
            Ftdp = -F; //鋼管の使用限界引張応力度
            Fcdp = F; //鋼管の使用限界圧縮応力度
            Fys = 1.1 * F; // 鋼管の降伏強度
            EpsilonY = Fys / SE1; // 降伏ひずみ

            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            ServiceLimitStrainC = Fcsp / SE1; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = Fcdp / SE1; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = Ftsp / SE1; // 使用限界引張ひずみ度
            DamageLimitStrainT = Ftdp / SE1; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = double.MinValue; // 安全限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (EpsilonY < epsilon) { return Fys; }
            else if (epsilon < -EpsilonY) { return -Fys; }
            else { return epsilon * SE1; }
        }
    }

    /// <summary>
    /// /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// </summary>
    // 杭断面抽象クラス
    internal abstract class AbstractPileSection
    {
        public double CurvatureMaxServiceLimit { get; protected set; }
        public double CurvatureMaxDamageLimit { get; protected set; }

        public double AxialForceCurvatureMaxServiceLimit { get; protected set; }
        public double AxialForceCurvatureMaxDamageLimit { get; protected set; }
        public double AxialForceCurvatureMaxUltimateLimit { get; protected set; }

        public List<double> Prestrains { get; protected set; }

        public List<double> ServiceLimitStrainCs { get; protected set; }
        public List<double> DamageLimitStrainCs { get; protected set; }
        public List<double> UltimateLimitStrainCs { get; protected set; }
        public List<double> PositionCs { get; protected set; }

        public List<double> ServiceLimitStrainTs { get; protected set; }
        public List<double> DamageLimitStrainTs { get; protected set; }
        public List<double> UltimateLimitStrainTs { get; protected set; }

        public List<double> PositionTs { get; protected set; }

        public double PileDia { get; protected set; }

        public List<double> ServiceLimitAxialForceThresholds { get; protected set; }
        public List<double> ServiceLimitBendingMomentThresholds { get; protected set; }
        public List<double> ServiceLimitBeta { get; protected set; }

        public List<double> DamagaLimitAxialForceThresholds { get; protected set; }
        public List<double> DamagaLimitBendingMomentThresholds { get; protected set; }
        public List<double> DamageLimitBeta { get; protected set; }

        public List<double> UltimateLimitAxialForceThresholds { get; protected set; }
        public List<double> UltimateLimitBendingMomentThresholds { get; protected set; }
        public List<double> UltimateLimitBeta { get; protected set; }

        public int DivisionNum { get; protected set; } = 100;
        public double DeltaCurvature { get; protected set; }

        public (List<double>, List<double>, List<double>, List<double>) UnfactoredServiceNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) UnfactoredDamageNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) UnfactoredUltimateNM { get; protected set; }

        public (List<double>, List<double>, List<double>, List<double>) FactoredServiceNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) FactoredDamageNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) FactoredUltimateNM { get; protected set; }

        public (List<double>, List<double>, List<double>, List<double>) SteelYieldNM { get; protected set; }

        public (List<double>, List<double>) UnfactoredServiceNQ { get; protected set; }
        public (List<double>, List<double>) UnfactoredDamageNQ { get; protected set; }
        public (List<double>, List<double>) UnfactoredUltimateNQ { get; protected set; }

        public (List<double>, List<double>) FactoredServiceNQ { get; protected set; }
        public (List<double>, List<double>) FactoredDamageNQ { get; protected set; }
        public (List<double>, List<double>) FactoredUltimateNQ { get; protected set; }

        // コンストラクタ
        internal AbstractPileSection()
        {
        }

        // 損傷限界曲げモーメント閾値を返すメソッド
        internal List<double> GetDamagaLimitBendingMomentThresholds()
        {
            List<double> Ms = [];
            foreach (double Ntarget in DamagaLimitAxialForceThresholds)
            {
                double targetM = GetAllowableMomentForSpecificN(1, Ntarget);
                Ms.Add(targetM);
            }
            return Ms;
        }

        // 安全限界曲げモーメント閾値を返すメソッド
        internal virtual List<double> GetUltimateLimitBendingMomentThresholds()
        {
            List<double> Ms = [];
            foreach (double Ntarget in UltimateLimitAxialForceThresholds)
            {
                (double targetM, double _) = GetUltimateMomentForSpecificN(Ntarget);
                Ms.Add(targetM);
            }
            return Ms;
        }

        // 特定の軸力時の使用、損傷限界曲げモーメントを返すメソッド
        internal double GetAllowableMomentForSpecificN(int limitStateNo, double Ntarget)
        {
            double N = 0.0;
            double N1;
            double M = 0.0;
            bool isCompressionSide;
            double curvature = Math.Pow(10, -4);
            double deltaCurvature = curvature / 100;
            List<double> Ns, Ms;
            List<double> curvatures;
            double axialForceCurvatureMax;

            if (limitStateNo == 0)
            {
                Ns = UnfactoredServiceNM.Item1;
                Ms = UnfactoredServiceNM.Item2;
                curvatures = UnfactoredServiceNM.Item4;
                axialForceCurvatureMax = AxialForceCurvatureMaxServiceLimit;
            }
            else if (limitStateNo == 1)
            {
                Ns = UnfactoredDamageNM.Item1;
                Ms = UnfactoredDamageNM.Item2;
                curvatures = UnfactoredDamageNM.Item4;
                axialForceCurvatureMax = AxialForceCurvatureMaxDamageLimit;
            }
            else // (limitStateNo == 2)
            {
                Ns = UnfactoredUltimateNM.Item1;
                Ms = UnfactoredUltimateNM.Item2;
                curvatures = UnfactoredUltimateNM.Item4;
                axialForceCurvatureMax = AxialForceCurvatureMaxUltimateLimit;
            }

            for (int i = 0; i < Ns.Count; i++)
            {
                if (Ntarget < Ns[i])
                {
                    if (i == 0) { return 0.0; }
                    N = (Ns[i - 1] + Ns[i]) / 2.0;
                    M = (Ms[i - 1] + Ms[i]) / 2.0;
                    curvature = (curvatures[i - 1] + curvatures[i]) / 2.0;
                    deltaCurvature = (curvatures[i] - curvatures[i - 1]) / 100;
                    break;
                }
                else if (i == Ns.Count - 1)
                { return 0.0; }
            }

            if (axialForceCurvatureMax < Ntarget) { isCompressionSide = true; } else { isCompressionSide = false; }

            while (Math.Abs(N - Ntarget) > 0.1) // 0.1N 以上の差がある場合
            {
                N1 = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature + deltaCurvature).Item1;
                curvature = deltaCurvature / (N1 - N) * (Ntarget - N) + curvature;
                (double, double, double) forceAndMoment = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature);
                N = forceAndMoment.Item1;
                M = forceAndMoment.Item2;
            }
            return M;
        }

        // 特定の軸力時の安全限界曲げモーメントを返すメソッド
        internal (double, double) GetUltimateMomentForSpecificN(double Ntarget)
        {
            double N = 0.0; double N1;
            double M = 0.0;
            double epsilonC = 0.003;
            //bool isCompressionSide;
            double curvature = 1.0 * Math.Pow(10, -6);
            double deltaCurvature = curvature / 500.0;

            List<double> Ns = UnfactoredUltimateNM.Item1;
            List<double> Ms = UnfactoredUltimateNM.Item2;
            List<double> curvatures = UnfactoredUltimateNM.Item4;

            // 初期値の設定
            for (int i = 0; i < Ns.Count; i++)
            {
                if (Ntarget < Ns[i])
                {
                    if (i == 0) { return (0.0, 0.0); }
                    N = (Ns[i - 1] + Ns[i]) * 0.5;
                    M = (Ms[i - 1] + Ms[i]) * 0.5;
                    curvature = (curvatures[i - 1] + curvatures[i]) * 0.5;
                    deltaCurvature = (curvatures[i] - curvatures[i - 1]) / 100;
                    break;
                }
                else if (i == Ns.Count - 1)
                { return (0.0, 0.0); }
            }

            int maxIter = 50;
            int iter = 0;
            while (Math.Abs(N - Ntarget) > 0.1 && iter < maxIter)
            {
                N1 = GetUltimateForceAndMoment(epsilonC, curvature + deltaCurvature).Item1;
                double deltaN = N1 - N;
                if (Math.Abs(deltaN) < 1e-8)
                    break; // 収束不能

                double step = deltaCurvature / deltaN * (Ntarget - N);

                // ステップ幅制限
                if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
                    step = Math.Sign(step) * Math.Abs(curvature) * 0.5;

                curvature += step;
                (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
                iter++;
            }

            // 収束しなかった場合の対策
            if (iter >= maxIter)
            {
                // 必要なら例外や警告
                // throw new InvalidOperationException("Newton-Raphson法が収束しませんでした。");
            }

            return (M, curvature);
            //while (Math.Abs(N - Ntarget) > 0.1) // 0.1N以上の場合
            //{
            //    N1 = GetUltimateForceAndMoment(epsilonC, curvature + deltaCurvature).Item1;
            //    curvature = deltaCurvature / (N1 - N) * (Ntarget - N) + curvature;
            //    (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            //}
            //return M;
        }

        // 限界ひずみ状態を超えない最大曲率取得メソッド 
        internal static double GetAllowableMaxCurvature(
            List<double> allowableStrainCs, List<double> positionCs, List<double> allowableStrainTs, List<double> positionTs)
        {
            double maxCurvature = double.MaxValue;
            for (int i = 0; i < allowableStrainCs.Count; i++)
            {
                for (int j = 0; j < allowableStrainTs.Count; j++)
                {
                    double curvature = -(allowableStrainTs[j] - allowableStrainCs[i]) / (positionTs[j] - positionCs[i]);
                    if (curvature < maxCurvature) { maxCurvature = curvature; }
                }
            }
            return maxCurvature;
        }

        // <抽象> 軸力、曲げモーメント取得メソッド
        internal abstract (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature);

        // <抽象> 安全限界軸力、曲げモーメント取得メソッド
        internal abstract (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature);

        // ある曲率時の圧縮縁ひずみ度取得メソッド
        internal double GetAllowableCompressionEdgeStrain(
           int limitStateNo, bool isCompressionSide, double curvature)
        {
            var allowablesSrains = GetAllowableStrains(limitStateNo);
            List<double> allowableStrainCs = allowablesSrains.Item1;
            List<double> allowableStrainTs = allowablesSrains.Item2;

            double epsilonC;
            if (isCompressionSide)
            {
                epsilonC = double.MaxValue;
                foreach (var pair in allowableStrainCs.Zip(PositionCs, (allowableStrainC, positionC) => (allowableStrainC, positionC)))
                    epsilonC = Math.Min(epsilonC, -curvature * (-PileDia / 2 - pair.positionC) + pair.allowableStrainC);
            }
            else // (isCompressionSide == false)
            {
                epsilonC = double.MinValue;
                foreach (var pair in allowableStrainTs.Zip(PositionTs, (allowableStrainT, positionT) => (allowableStrainT, positionT)))
                    epsilonC = Math.Max(epsilonC, -curvature * (-PileDia / 2 - pair.positionT) + pair.allowableStrainT);
            }
            return epsilonC;
        }

        // 使用損傷限界ひずみ度取得メソッド
        internal (List<double>, List<double>) GetAllowableStrains(int limitStateNo)
        {
            if (limitStateNo == 0)
            {
                return (ServiceLimitStrainCs, ServiceLimitStrainTs);
            }
            else if (limitStateNo == 1)
            {
                return (DamageLimitStrainCs, DamageLimitStrainTs);
            }
            else //if (limitStateNo == 2)
            {
                return (UltimateLimitStrainCs, UltimateLimitStrainTs);
            }
        }

        // 使用限界MNインタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            return GetAllowableMNInterection(CurvatureMaxServiceLimit, 0);
        }

        // 損傷限界MNインタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetDamageLimitMNInteraction()
        {
            return GetAllowableMNInterection(CurvatureMaxDamageLimit, 1);
        }

        // 使用損傷限界MNインタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetAllowableMNInterection(double maxCurvature, int LimitStateNo)
        {
            List<double> axialForces = [];
            List<double> bendingMoments = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            for (int i = 0; i <= DivisionNum; i++)
            {
                double curvature = maxCurvature * i / DivisionNum;
                var result = GetAllowableForceAndMoment(LimitStateNo, false, curvature); // 引張側 純引張～
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(result.Item3);
                curvatures.Add(curvature);
            }

            for (int i = DivisionNum; i >= 0; i--)
            {
                double curvature = maxCurvature * i / DivisionNum;
                var result = GetAllowableForceAndMoment(LimitStateNo, true, curvature); // 圧縮側 ～純圧縮
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(result.Item3);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        // 安全限界MN インタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInterection()
        {
            List<double> axialForces = [];
            List<double> bendingMoments = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];
            double epsilonC;
            double curvature;
            double maxCurvature = (0.003 + 0.0025) * 20.0 / PileDia;
            double maxEpsilonC = 0.003;

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                if (i == 0)
                {
                    epsilonC = -0.006;
                    curvature = 0.0;
                }
                else if (i != DivisionNum * 2)
                {
                    epsilonC = maxEpsilonC;
                    curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
                }
                else { epsilonC = maxEpsilonC; curvature = 0.0; }

                var result = GetUltimateForceAndMoment(epsilonC, curvature); // 引張側 純引張～
                axialForces.Add(result.Item1); //  * Math.Pow(10, -3));
                bendingMoments.Add(result.Item2); // * Math.Pow(10, -6));
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        // 軸力制限の組み込みメソッド
        internal static (List<double>, List<double>, List<double>, List<double>)
            GetFactoredMNInterection(
            (List<double>, List<double>, List<double>, List<double>) unfactoredNM,
            (List<double>, List<double>) additionalNM, List<double> factor)
        {
            List<double> factoredNs = [];
            List<double> factoredMs = [];

            List<double> unfactoredNs = unfactoredNM.Item1;
            List<double> unfactoredMs = unfactoredNM.Item2;
            List<double> epsilonCs = unfactoredNM.Item3;
            List<double> curvatures = unfactoredNM.Item4;

            List<double> additionalNs = additionalNM.Item1;
            List<double> additionalMs = additionalNM.Item2;

            int j = 0;
            for (int i = 0; i < unfactoredNs.Count; i++)
            {
                if (j < additionalNs.Count)
                {
                    while (additionalNs[j] < unfactoredNs[i])
                    {
                        factoredNs.Add(additionalNs[j]);
                        factoredMs.Add(additionalMs[j] * factor[j]);
                        factoredNs.Add(additionalNs[j]);
                        factoredMs.Add(additionalMs[j] * factor[j + 1]);
                        j += 1;

                        if (j >= additionalNs.Count) { break; }
                    }
                }
                factoredNs.Add(unfactoredNs[i]);
                factoredMs.Add(unfactoredMs[i] * factor[j]);
            }
            return (factoredNs, factoredMs, epsilonCs, curvatures);
        }
    }

    // 円形断面クラス
    internal class CirclularSolidSection
    {
        private double Dia { get; }

        // コンストラクタ
        internal CirclularSolidSection(double diameter)
        {
            Dia = diameter;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(string type, Material material, double epsilon0, double curvature, int division = 100)
        {
            double z;
            double dz = Dia / division;
            double epsilon;
            double sigma;
            double width;

            double axialForce = 0.0;
            double bendingMoment = 0.0;

            // 圧縮縁ひずみ度 epsilonC
            // 中心ひずみ度 epsilon0
            for (int i = 0; i < division; i++)
            {
                z = -Dia * 0.5 + (0.5 + i) * dz;
                width = 2.0 * Math.Sqrt(Math.Pow(Dia * 0.5, 2) - Math.Pow(z, 2));
                //epsilon = epsilonC - curvature * (z + Dia / 2.0);
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(type, epsilon);
                axialForce += width * sigma * dz;
                bendingMoment += width * sigma * dz * -z;
            }
            return (axialForce, bendingMoment);
        }
    }

    // 円環断面クラス
    internal class CircularPipeSection(double diameter, double t)
    {
        private double Dia { get; } = diameter;
        private double T { get; } = t;

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(string type, Material material, double epsilon0, double curvature, int division = 100)
        {
            double z;
            double dCirc = Math.PI * Dia / division;
            double epsilon;
            double sigma;

            double axialForce = 0.0;
            double bendingMoment = 0.0;

            // 圧縮縁ひずみ度 epsilonC
            // 中心ひずみ度 epsilon0
            for (int i = 0; i < division; i++)
            {
                z = Dia * 0.5 * Math.Cos(2.0 * Math.PI * i / division);
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(type, epsilon);
                axialForce += T * sigma * dCirc;
                bendingMoment += T * sigma * dCirc * -z;
            }
            return (axialForce, bendingMoment);
        }
    }
}