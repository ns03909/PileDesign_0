using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Linq;
using System.Windows.Media.Media3D;
using System.Windows.Forms.Design;
using System.Diagnostics.Eventing.Reader;
using static System.Net.WebRequestMethods;
using System.Security.Cryptography;

namespace PileDesignCore.InsituPileSection
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

        internal abstract double GetStress(bool isLinear, double epsilon);
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

            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = 1.0 / 3.0 * Gsi * Fc; // 使用限界圧縮応力度
            double damageLimitStressC = 2.0 / 3.0 * Gsi * Fc;// 損傷限界圧縮応力度
            ServiceLimitStrainC = serviceLimitStressC / Ec; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / Ec; // 損傷限界圧縮ひずみ度
            ServiceLimitStrainT = Double.MinValue; // 使用限界引張ひずみ度
            DamageLimitStrainT = Double.MinValue; // 損傷限界引張ひずみ度
        }

        // 密度を計算するメソッド
        internal double GetDensity(double _Fc = 27, string type = "普通")
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
                if (_Fc <= 36) { return 24.0-1.0; }
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

        // ひずみ度から応力を計算するメソッド// ひずみ度から応力を計算するメソッド 使用限界、損傷限界用
        internal override double GetStress(bool isLinear, double epsilon)
        {
            if (isLinear)
            {
                if (epsilon > 0)
                {
                    return Ec * epsilon;
                }
                else
                { return 0.0; }
            }
            else
            {
                if (epsilon > 0)
                {
                    epsilon = Math.Min(epsilon, EpsilonCu);
                    return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
                }
                else
                { return 0.0; }
            }
        }
    }

    // 主筋クラス
    internal class MainBars : Material
    {
        public double PCD { get; }
        public int Number { get; }
        public string Grade { get; }
        public string BarSize { get;  }
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
            if (_Grade != "") { Grade = _Grade; } else { Grade = "SD345"; };
            SetRSigmaY();
            if (_BarSize != "") { BarSize = _BarSize; } else { BarSize = "D25"; }
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
            double damageLimitStressT = -RSigmaY;// 損傷限界圧縮応力度

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
            else { area = 0.0; }
            Ag = Number * area;
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(bool isLinear, double epsilon)
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
        public double SSigmaU { get; private set; }
        public double F { get; private set; }
        public double SSigmaY { get; private set; }
        public double SEpsilonY { get; private set; }
        public double SEpsilonU { get; private set; }
        public double SE1 { get; private set; } = 205000.0;
        public double SE2 { get; private set; } = 205000.0 / 30.0;

        // コンストラクタ
        public InsituSteelPipe(string _Grade, double _OutDia, double _T)
        {
            Grade = _Grade;
            OutDia = _OutDia;
            T = _T;

            SetMaterialProperties();
        }

        private void SetMaterialProperties()
        {
            if (Grade == "SKK400"){ SSigmaU = 400.0; F = 235;}
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
        internal override double GetStress(bool isLinear, double epsilon)
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
        internal override double GetStress(bool isLinear, double epsilon)
        {
            if (EpsilonCy <= (EpsilonE + epsilon)) // && (EpsilonE + epsilon) <= EpsilonCu) // 降伏域
            {
                return Fc;
            }
            else if (0 < (EpsilonE + epsilon) && (EpsilonE + epsilon) < EpsilonCy)
            {
                return Ec * (EpsilonE + epsilon); // 弾性範囲
            }
            else
            {
                return 0.0;
            }
        }
    }

    internal class PrecastPHCConcrete: PrecastConcrete
    {

        // コンストラクタ
        public PrecastPHCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            if (Fc < 105)
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else
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
            UltimateLimitStrainT = Double.MinValue;
        }
    }

    internal class PrecastPRCConcrete: PrecastConcrete
    {
        // コンストラクタ
        public PrecastPRCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            if (Fc < 105)
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else
            {
                EpsilonCu = Fc / Ec;
                EpsilonCy = Fc / Ec;
            }


            DO = _DO;
            DI = _DI;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            double serviceLimitStressC = Fc / 3.5;
            double damageLimitStressC = Fc * 2.0 / 3.5;

            double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
            double damageLimitStressT = Double.MinValue; // <<<<<

            ServiceLimitStrainC = serviceLimitStressC / Ec;
            DamageLimitStrainC = damageLimitStressC / Ec; // <<<<<
            UltimateLimitStrainC = EpsilonCu;

            ServiceLimitStrainT = serviceLimitStressT / Ec;
            DamageLimitStrainT = damageLimitStressT / Ec;
            UltimateLimitStrainT = Double.MinValue;
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
            if (Fc < 105)
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else
            {
                EpsilonCu = Fc / Ec;
                EpsilonCy = Fc / Ec;
            }


            DO = _DO;
            DI = _DI;
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
            UltimateLimitStrainC = EpsilonCu;

            ServiceLimitStrainT = Double.MinValue;
            DamageLimitStrainT = Double.MinValue;
            UltimateLimitStrainT = Double.MinValue;
        }
    }

    // PC鋼材クラス
    internal class Tendons : Material
    {
        public double Fpy { get;}
        public double Fpu { get;}
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
            ServiceLimitStrainC = Double.MaxValue; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = Double.MaxValue; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = Double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = Double.MinValue; // 使用限界引張ひずみ度
            DamageLimitStrainT = Double.MinValue; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = -EpsilonPu; // 安全限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(bool isLinear, double epsilon)
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
        public PrecastSteelPipe(string _Grade, double _OutDia, double _T)
        {
            Grade = _Grade;
            OutDia = _OutDia;
            T = _T;

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
            Ftsp = - F / 1.5; //鋼管の使用限界引張応力度
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
            UltimateLimitStrainC = Double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = Ftsp / SE1; // 使用限界引張ひずみ度
            DamageLimitStrainT = Ftdp / SE1; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = Double.MinValue; // 安全限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(bool isLinear, double epsilon)
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
    internal abstract class PileSection
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
        public List<double> ServiceLimitMNBeta { get; protected set; }

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

        // コンストラクタ
        internal PileSection()
        {
        }

        // 損傷限界曲げモーメント閾値を返すメソッド
        internal List<double> GetDamagaLimitBendingMomentThresholds()
        {
            List<double> Ms = new List<double>();
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
            List<double> Ms = new List<double>();
            foreach (double Ntarget in UltimateLimitAxialForceThresholds)
            {
                double targetM = GetUltimateMomentForSpecificN(Ntarget);
                Ms.Add(targetM);
            }
            return Ms;
        }

        // 特定の軸力時の使用、損傷限界曲げモーメントを返すメソッド
        internal double GetAllowableMomentForSpecificN(int limitStateNo, double Ntarget)
        {
            double N = 0.0; double N1;
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
            else if(limitStateNo == 1)
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
                    if (i == 0) { return 0.0;  }
                    N = (Ns[i - 1] + Ns[i]) / 2.0;
                    M = (Ms[i - 1] + Ms[i]) / 2.0;
                    curvature = (curvatures[i - 1] + curvatures[i]) / 2.0;
                    deltaCurvature = (curvatures[i] - curvatures[i-1]) / 100;
                    break;
                }
                else if (i == Ns.Count - 1)
                { return 0.0; }
            }

            if (axialForceCurvatureMax < Ntarget) { isCompressionSide = true; } else { isCompressionSide = false; }

            //while (Math.Abs(N - Ntarget) / Math.Max(Math.Abs(N), Math.Abs(Ntarget)) > Math.Pow(10, -3))
            while (Math.Abs(N - Ntarget) > 0.1) // 0.1N 以上の差がある場合
                {
                N1 = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature + deltaCurvature).Item1;
                curvature =  deltaCurvature / (N1 - N) * (Ntarget - N) + curvature;
                (double, double, double) forceAndMoment = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature);
                N = forceAndMoment.Item1;
                M = forceAndMoment.Item2;
            }
            return M;
        }

        // 特定の軸力時の安全限界曲げモーメントを返すメソッド
        internal double GetUltimateMomentForSpecificN(double Ntarget)
        {
            double N = 0.0; double N1;
            double M = 0.0;
            double epsilonC = 0.003;
            //bool isCompressionSide;
            double curvature = 1.0 * Math.Pow(10,-6);
            double deltaCurvature = curvature / 500.0;

            List<double> Ns = UnfactoredUltimateNM.Item1;
            List<double> Ms = UnfactoredUltimateNM.Item2;
            List<double> curvatures = UnfactoredUltimateNM.Item4;

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

            //while (Math.Abs(N - Ntarget) / Math.Max(Math.Abs(N), Math.Abs(Ntarget)) > Math.Pow(10, -3))
            while (Math.Abs(N - Ntarget) > 0.1) // 0.1N以上の場合
            {
                N1 = GetUltimateForceAndMoment(epsilonC, curvature + deltaCurvature).Item1;
                curvature =  deltaCurvature / (N1 - N) * (Ntarget - N) + curvature;
                (N, M)  = GetUltimateForceAndMoment(epsilonC, curvature);
            }
            return M;
        }

        // 限界ひずみ状態を超えない最大曲率取得メソッド 
        internal double GetAllowableMaxCurvature(
            List<double> allowableStrainCs, List<double> positionCs, List<double> allowableStrainTs, List<double> positionTs) 
        // C (-z, +stress), T(+z, -stress)
        {
            double maxCurvature = double.MaxValue;
            for (int i = 0; i < allowableStrainCs.Count; i++)
            {
                for(int j = 0; j < allowableStrainTs.Count; j++)
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
            List<double> axialForces = new List<double> { };
            List<double> bendingMoments = new List<double> { };
            List<double> epsilonCs = new List<double> { };
            List<double> curvatures = new List<double> { };

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
                double curvature =  maxCurvature * i / DivisionNum;
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
            List<double> axialForces = new List<double> { };
            List<double> bendingMoments = new List<double> { };
            List<double> epsilonCs = new List<double> { };
            List<double> curvatures = new List<double> { };
            double epsilonC;
            double curvature;
            double maxCurvature = (0.003 + 0.0025) * 20.0 / PileDia;
            double maxEpsilonC = 0.003; 

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                if (i == 0) { epsilonC = -0.006; curvature = 0.0; }
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
        internal (List<double>, List<double>, List<double>, List<double>)
            GetFactoredMNInterection((List<double>, List<double>, List<double>, List<double>) unfactoredNM, (List<double>, List<double>) additionalNM, List<double> factor)
        {
            List<double> factoredNs = new List<double>();
            List<double> factoredMs = new List<double>();

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

    // 場所打ち鉄筋コンクリート杭断面クラス
    internal class InsituReinforcedConcreteSection : PileSection
    {
        public CirclularSolidSection CircularSolidSectionConcrete { get; private set; }
        public CircularPipeSection CircularPipeSectionMainbars { get; private set; }

        public InsituConcrete InsituConcrete { get; private set; }
        public MainBars MainBars { get; private set; }

        public double MainBarArea { get; private set; }
        public double MainBarPCD { get; private set; }

        public double Ae { get; private set; }
        public double Ze { get; private set; }
        public double Ie { get; private set; }
        public double Ft { get; private set; }

        // コンストラクタ
        internal InsituReinforcedConcreteSection(
            InsituConcrete insituConcrete, MainBars mainBars)
        {
            PileDia = insituConcrete.DO;
            MainBarArea = mainBars.Ag; //mainBarArea;
            MainBarPCD = mainBars.PCD;  //mainBarPcd;

            CircularSolidSectionConcrete = new CirclularSolidSection(PileDia);
            CircularPipeSectionMainbars = new CircularPipeSection(MainBarPCD, MainBarArea / Math.PI / MainBarPCD);

            InsituConcrete = insituConcrete;
            MainBars = mainBars;

            PositionCs = new List<double> { -PileDia / 2, -MainBarPCD / 2, };
            PositionTs = new List<double> { PileDia / 2, MainBarPCD / 2, };

            // プレストレスひずみ度
            Prestrains = new List<double> {insituConcrete.Prestrain, mainBars.Prestrain };

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = new List<double> { insituConcrete.ServiceLimitStrainC, mainBars.ServiceLimitStrainC, };
            ServiceLimitStrainTs = new List<double> { insituConcrete.ServiceLimitStrainT, mainBars.ServiceLimitStrainT, };

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = new List<double> { insituConcrete.DamageLimitStrainC, mainBars.DamageLimitStrainC, };
            DamageLimitStrainTs = new List<double> { insituConcrete.DamageLimitStrainT, mainBars.DamageLimitStrainT, };

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);
            
            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 使用限界最大曲率時の軸力
            AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;
            
            // 損傷限界最大曲率時の軸力
            AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = new List<double> {};

            // 使用限界曲げモーメント低減率
            ServiceLimitMNBeta = new List<double> { 1.0 };

            // 損傷限界軸力閾値
            DamagaLimitAxialForceThresholds = new List<double>
            {
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                (1.0 / 3.0) * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0, 
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0, 
            };

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = new List<double> { 0.0, 1.0, 0.65, 0.0 };

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds = new List<double>
            {
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0, 
                (1.0 / 3.0) * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0, 
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
            };

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = new List<double> { 0.0, 0.95 * 1.0, 0.80 * 0.65, 0.0 };

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInterection();

            // 損傷限界閾値
            DamagaLimitBendingMomentThresholds = GetDamagaLimitBendingMomentThresholds();

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredMNInterection(UnfactoredDamageNM, (DamagaLimitAxialForceThresholds, DamagaLimitBendingMomentThresholds), DamageLimitBeta);

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            //
            SetZeFtIe();
        }

        internal void SetZeFtIe()
        {
            double Ro = PileDia / 2.0;
            double I = Math.PI * Math.Pow(Ro, 4) / 4.0;
            double n = MainBars.Er / InsituConcrete.Ec;
            Ae = InsituConcrete.Ac + (n - 1) * MainBars.Ag;
            Ie = I + 1.0 / 2.0 * (n - 1) * MainBars.Ag * Math.Pow(MainBars.PCD / 2.0, 2);
            Ze = Ie / Ro;
            Ft = 0.56 * Math.Sqrt(InsituConcrete.Gsi * InsituConcrete.Fc);

        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget)
        {
            double sigma0e = Ntarget / Ae;
            double Mcr = Ze * (Ft + sigma0e);
            double phiCr = Mcr / InsituConcrete.Ec / Ie;
            return (Mcr, phiCr);
        }




        // 最外縁の杭主筋が引張降伏するときの曲げモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetYieldMoment(double Ntarget)
        {
            double Nnext = double.MaxValue;
            double Mnext = double.MaxValue;
            double Nnext1 = double.MaxValue;
            double Mnext1 = double.MaxValue;
            double curvature = MainBars.RSigmaY / MainBars.Er / (PileDia / 2.0 + MainBars.PCD / 2);
            double deltaCurvature = curvature / 100.0;
            while(Math.Abs(Ntarget - Nnext) > 0.1)
            {
                (Nnext, Mnext) = GetYieldForceAndMoment(curvature);
                (Nnext1, Mnext1) = GetYieldForceAndMoment(curvature + deltaCurvature);
                curvature += deltaCurvature / (Nnext1 - Nnext) * (Ntarget - Nnext); 
            }

            return (Mnext, curvature);
        }

        // C点を返すメソッド
        internal double GetPhiC(double phiCr, double Mcr, double phiY, double My, double Mu0, double beta1)
        {
            double phiC = phiCr + (phiY - phiCr) * (beta1 * Mu0 - Mcr) / (My - Mcr);
            return phiC;
        }

        internal (List<double>, List<double>) GetMPhiRelationship(double Ntarget)
        {
            (double phiCr, double MCr) = GetCrackMoment(Ntarget);
            (double phiY, double MY) = GetYieldMoment(Ntarget);
            double Mu0 = GetUltimateMomentForSpecificN(Ntarget);
            double beta1 = 0.9;
            double phiC = GetPhiC(phiCr, MCr, phiY, MY, Mu0, beta1);
            List<double> phis = new List<double> { 0.0, phiCr, phiC };
            List<double> Ms = new List<double> { 0.0, MCr, beta1 * Mu0 };

            return (phis, Ms);
        }

        // 最外縁の杭主筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonC = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia / 2.0 + MainBars.PCD / 2);
            // 最外縁の杭主筋が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);

            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(true, InsituConcrete, epsilonC, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(true, MainBars, epsilonC, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(true, InsituConcrete, epsilonC, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M, epsilonC);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(false, InsituConcrete, epsilonC, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(false, MainBars, epsilonC, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(false, InsituConcrete, epsilonC, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }
    }

    // 場所打ち鋼管コンクリート杭断面クラス
    internal class InsituSteelPipeReinforcedConcreteSection : PileSection
    {
        public CirclularSolidSection CircularSolidSectionConcrete { get; private set; }
        public CircularPipeSection CircularPipeSectionMainbars { get; private set; }
        public CircularPipeSection CircularPipeSectionSteelPipe { get; private set; }

        public InsituConcrete InsituConcrete { get; private set; }
        public MainBars MainBars { get; private set; }
        public InsituSteelPipe InsituSteelPipe { get; private set; }

        public double PipeT { get; private set; }
        public double MainBarArea { get; private set; }
        public double MainBarPCD { get; private set; }


        public double Ae { get; private set; }
        public double Ze { get; private set; }
        public double Ie { get; private set; }
        public double Ft { get; private set; }

        // コンストラクタ
        internal InsituSteelPipeReinforcedConcreteSection(
            InsituSteelPipe insituSteelPipe, InsituConcrete insituConcrete, MainBars mainBars)
        {
            InsituSteelPipe = insituSteelPipe;
            InsituConcrete = insituConcrete;
            MainBars = mainBars;
            
            PileDia = InsituSteelPipe.OutDia;
            PipeT = InsituSteelPipe.T;
            MainBarArea = MainBars.Ag;
            MainBarPCD = MainBars.PCD;

            CircularSolidSectionConcrete = new CirclularSolidSection(PileDia - 2 * PipeT);
            CircularPipeSectionMainbars = new CircularPipeSection(MainBarPCD, MainBarArea / Math.PI / MainBarPCD);
            CircularPipeSectionSteelPipe = new CircularPipeSection(PileDia - PipeT, PipeT);

            double concreteDia = PileDia - PipeT * 2;
            //double PipeCenterDia = PileDia - PipeT;
            PositionCs = new List<double> {-(PileDia - PipeT / 2)/ 2, -concreteDia / 2, -MainBarPCD / 2, };
            PositionTs = new List<double> { (PileDia - PipeT / 2) / 2, concreteDia / 2, MainBarPCD / 2, };

            // プレストレスひずみ度
            Prestrains = new List<double> { insituSteelPipe.Prestrain, insituConcrete.Prestrain, mainBars.Prestrain };

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = new List<double> { insituSteelPipe.ServiceLimitStrainC, insituConcrete.ServiceLimitStrainC, mainBars.ServiceLimitStrainC, };
            ServiceLimitStrainTs = new List<double> { insituSteelPipe.ServiceLimitStrainT, insituConcrete.ServiceLimitStrainT, mainBars.ServiceLimitStrainT, };

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = new List<double> { insituSteelPipe.DamageLimitStrainC, insituConcrete.DamageLimitStrainC, mainBars.DamageLimitStrainC, };
            DamageLimitStrainTs = new List<double> { insituSteelPipe.DamageLimitStrainT, insituConcrete.DamageLimitStrainT, mainBars.DamageLimitStrainT, };

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = new List<double> {  };

            // 使用限界曲げモーメント低減率
            ServiceLimitMNBeta = new List<double> { 1.0 };
            
            // 損傷限界軸力閾値
            DamagaLimitAxialForceThresholds = new List<double> { };
            
            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = new List<double> { 1.0 };

            // 安全限界軸力閾値
            UltimateLimitAxialForceThresholds = new List<double> { };

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = new List<double> { 1.0 };

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInterection();

            // 損傷限界閾値
            DamagaLimitBendingMomentThresholds = GetDamagaLimitBendingMomentThresholds();

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredMNInterection(UnfactoredDamageNM, (DamagaLimitAxialForceThresholds, DamagaLimitBendingMomentThresholds), DamageLimitBeta);

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            //
            SetZeFtIe();
        }

        internal void SetZeFtIe()
        {
            double Ro = PileDia / 2.0;
            double I = Math.PI * Math.Pow(Ro, 4) / 4.0;
            double n = MainBars.Er / InsituConcrete.Ec;
            Ae = InsituConcrete.Ac + (n - 1) * MainBars.Ag;
            Ie = I + 1.0 / 2.0 * (n - 1) * MainBars.Ag * Math.Pow(MainBars.PCD / 2.0, 2);
            Ze = Ie / Ro;
            Ft = 0.56 * Math.Sqrt(InsituConcrete.Gsi * InsituConcrete.Fc);

        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget)
        {
            double sigma0e = Ntarget / Ae;
            double Mcr = Ze * (Ft + sigma0e);
            double phiCr = Mcr / InsituConcrete.Ec / Ie;
            return (Mcr, phiCr);
        }




        // 最外縁の杭主筋が引張降伏するときの曲げモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetYieldMoment(double Ntarget)
        {
            double Nnext = double.MaxValue;
            double Mnext = double.MaxValue;
            double Nnext1 = double.MaxValue;
            double Mnext1 = double.MaxValue;
            double curvature = MainBars.RSigmaY / MainBars.Er / (PileDia / 2.0 + MainBars.PCD / 2);
            double deltaCurvature = curvature / 100.0;
            while (Math.Abs(Ntarget - Nnext) > 0.1)
            {
                (Nnext, Mnext) = GetYieldForceAndMoment(curvature);
                (Nnext1, Mnext1) = GetYieldForceAndMoment(curvature + deltaCurvature);
                curvature += deltaCurvature / (Nnext1 - Nnext) * (Ntarget - Nnext);
            }

            return (Mnext, curvature);
        }

        // C点を返すメソッド
        internal double GetPhiC(double phiCr, double Mcr, double phiY, double My, double Mu0, double beta1)
        {
            double phiC = phiCr + (phiY - phiCr) * (beta1 * Mu0 - Mcr) / (My - Mcr);
            return phiC;
        }

        internal (List<double>, List<double>) GetMPhiRelationship(double Ntarget)
        {
            (double phiCr, double MCr) = GetCrackMoment(Ntarget);
            (double phiY, double MY) = GetYieldMoment(Ntarget);
            double Mu0 = GetUltimateMomentForSpecificN(Ntarget);
            double beta1 = 0.9;
            double phiC = GetPhiC(phiCr, MCr, phiY, MY, Mu0, beta1);
            List<double> phis = new List<double> { 0.0, phiCr, phiC };
            List<double> Ms = new List<double> { 0.0, MCr, beta1 * Mu0 };

            return (phis, Ms);
        }

        // 最外縁の杭主筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonC = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia / 2.0 + MainBars.PCD / 2);
            // 最外縁の杭主筋が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);
            double N , M;

            var result0 = CircularPipeSectionMainbars.GetForceAndMoment(true, InsituSteelPipe, epsilonC, curvature);
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(true, InsituConcrete, epsilonC, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(true, MainBars, epsilonC, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(true, InsituConcrete, epsilonC, curvature);

            N = result0.Item1 + result1.Item1 + result2.Item1 - result3.Item1;
            M = result0.Item2 + result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M, epsilonC);
        }

        // 安全限界軸力、曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double N, M;

            var result0 = CircularPipeSectionMainbars.GetForceAndMoment(false, InsituSteelPipe, epsilonC, curvature);
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(false, InsituConcrete, epsilonC, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(false, MainBars, epsilonC, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(false, InsituConcrete, epsilonC, curvature);

            N = result0.Item1 + result1.Item1 + result2.Item1 - result3.Item1;
            M = result0.Item2 + result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }
    }

    // 既製コンクリート杭断面抽象クラス /////////////////////////////////////////
    internal abstract class PrecastPileSection: PileSection
    {
        //public double PileDia { get; protected set; }
        public double Ro { get; protected set; } // 外半径
        public double Ri { get; protected set; } // 内半径
        public double T { get; protected set; } // 肉厚
        public double Ap { get; protected set; } // PC鋼材断面積
        public double Rp { get; protected set; } // PC鋼材配置半径

        public double As { get; protected set; } // 主筋断面積
        public double Rs { get; protected set; } // 主筋配置半径

        public double Ag { get; protected set; } // 
        public double Rg { get; protected set; } // 
        public double Fc { get; protected set; } // 
        public double SigmaE { get; protected set; } // 有効プレストレス

        public double Ze { get; protected set; } // 換算断面係数
        public double Ie { get; protected set; } //  換算断面二次モーメント
        public double I { get; protected set; } // 断面二次モーメント
        public double Fts { get; protected set; } // 
        public double Sigma0E { get; protected set; } // 平均軸応力度　N/Ae

        public double CurvatureMaxUltimateLimit { get; protected set; }

        public double Fcs { get; protected set; }
        public double Ae { get; protected set; }
        public double Ac { get; protected set; }
        public double Ftd { get; protected set; }
        public double Fcd { get; protected set; }
        public double EpsilonPi { get; protected set; }
        public double EpsilonSi { get; protected set; }

        public double EpsilonCu { get; protected set; }
        public double EpsilonPu { get; protected set; }

        public PrecastConcrete PrecastConcrete { get; protected set; }
        public Tendons Tendons { get; protected set; }
        public MainBars MainBars { get; protected set; }
        public PrecastSteelPipe PrecastSteelPipe { get; protected set; }

        // 断面プロパティ設定メソッド
        internal void SetSectionParameters()
        {
            
            I = Math.PI * (Math.Pow(Ro, 4) - Math.Pow(Ri, 4)) / 4.0;
            double n = 5.0;
            Ie = I + 1.0 / 2.0 * (n - 1) * (Ap * Math.Pow(Rp, 2) + Ag * Math.Pow(Rg, 2));
            Ze = Ie / Ro;

            Fts = -0.56 * Math.Sqrt(Fc) * 1.0 / 2.0;
            Fcs = Fc * 1.0 / 3.5;

            Ac = Math.PI * (Math.Pow(Ro, 2) - Math.Pow(Ri, 2));
            Ae = Ac + (n - 1) * (Ap + Ag);

            Ftd = -0.56 * Math.Sqrt(Fc);
            Fcd = Fc * 2.0 / 3.5;
        }

        // 使用限界モーメント取得メソッド
        internal double GetServiceLimitMoment(double beta, double Sigma0E)
        {
            double Ms1 = Ze * (-Fts + SigmaE + Sigma0E);
            double Ms2 = Ze * (Fcs - SigmaE - Sigma0E);
            return beta * Math.Min(Ms1, Ms2);
        }

        // 安全限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInterection()
        {
            return GetAllowableMNInterection(CurvatureMaxUltimateLimit, 2);
        }

        // 安全限界曲げモーメント閾値を返すメソッド
        internal override List<double> GetUltimateLimitBendingMomentThresholds()
        {
            List<double> Ms = new List<double>();
            foreach (double Ntarget in UltimateLimitAxialForceThresholds)
            {
                double targetM = GetAllowableMomentForSpecificN(2, Ntarget);
                Ms.Add(targetM);
            }
            return Ms;
        }
    }

    // PHC杭断面クラス
    internal class PHCSection: PrecastPileSection
    {
        public CirclularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CirclularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectionTendons { get; private set; }
        public CircularPipeSection CircularPipeSectionConcrete { get; private set; }

        // コンストラクタ
        internal PHCSection(PrecastPHCConcrete precastConcrete, Tendons tendons, double prestress)
        {
            PrecastConcrete = precastConcrete;
            Tendons = tendons;

            PileDia = precastConcrete.DO;
            Ro = precastConcrete.DO / 2.0;
            Ri = precastConcrete.DI / 2.0;
            Ap = tendons.Ap;
            Ag = 0.0;
            Rp = tendons.PCD / 2.0;
            Rg = 0.0;
            Fc = precastConcrete.Fc;
            SigmaE = prestress;
            SetSectionParameters();

            // プレストレスひずみ度の設定
            SetEpsilonPi(Ac, Ap, 0.0, PrecastConcrete.Ec, Tendons.Ep, 1.0, SigmaE);
            SetEpsilonE(PrecastConcrete.Ec, SigmaE);

            CircularSolidSectionConcreteOut = new CirclularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CirclularSolidSection(precastConcrete.DI);
            CircularPipeSectionTendons = new CircularPipeSection(tendons.PCD, tendons.Ap / Math.PI / tendons.PCD);

            PositionCs = new List<double> { -PileDia / 2, -Tendons.PCD / 2 };
            PositionTs = new List<double> { PileDia / 2, Tendons.PCD / 2 };

            // プレストレスひずみ度
            Prestrains = new List<double> { PrecastConcrete.Prestrain, Tendons.Prestrain };

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = new List<double> { PrecastConcrete.ServiceLimitStrainC - PrecastConcrete.Prestrain,  Tendons.ServiceLimitStrainC - Tendons.Prestrain, };
            ServiceLimitStrainTs = new List<double> { PrecastConcrete.ServiceLimitStrainT - PrecastConcrete.Prestrain, Tendons.ServiceLimitStrainT - Tendons.Prestrain, };

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = new List<double> { PrecastConcrete.DamageLimitStrainC - PrecastConcrete.Prestrain, Tendons.DamageLimitStrainC - Tendons.Prestrain, };
            DamageLimitStrainTs = new List<double> { PrecastConcrete.DamageLimitStrainT - PrecastConcrete.Prestrain, Tendons.DamageLimitStrainT - Tendons.Prestrain };

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = new List<double> { PrecastConcrete.UltimateLimitStrainC - PrecastConcrete.Prestrain, Tendons.UltimateLimitStrainC - Tendons.Prestrain, };
            UltimateLimitStrainTs = new List<double> { PrecastConcrete.UltimateLimitStrainT - PrecastConcrete.Prestrain, Tendons.UltimateLimitStrainT - Tendons.Prestrain, };

            //// 使用限界状態最大曲率
            //CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            //// 損傷限界最大曲率
            //CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 安全限界最大曲率
            CurvatureMaxUltimateLimit = GetAllowableMaxCurvature(UltimateLimitStrainCs, PositionCs, UltimateLimitStrainTs, PositionTs);

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInterection();


            // 使用限界最大曲率時の軸力
            //AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            // 損傷限界最大曲率時の軸力
            //AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            //安全限界最大曲率時の軸力
            AxialForceCurvatureMaxUltimateLimit = GetAllowableForceAndMoment(2, true, CurvatureMaxUltimateLimit).Item1;


            // 低減前使用限界NMインタラクション
            FactoredServiceNM = GetFactoredServiceLimitMNInteraction();

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredDamageLimitMNInteraction();

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds = new List<double>
            {
                (4.0 - SigmaE) * Ae,
                (10.0 - SigmaE) * Ae,
                (65.0 - SigmaE) * Ae,
            };

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = new List<double> { 0.0, 0.80 * 0.75, 0.80 * 0.65, 0.0 };

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);
        }

        // コンクリートのひずみ度取得メソッド
        internal void SetEpsilonE(double Ec, double sigmaE)
        {
            PrecastConcrete.EpsilonE = sigmaE / Ec;
            PrecastConcrete.Prestrain = PrecastConcrete.EpsilonE;
        }

        // テンドンのプレストレスひずみ取得メソッド
        internal void SetEpsilonPi(double Ac, double Ap, double As, double Ec, double Ep, double Es, double sigmaE)
        {
            Tendons.EpsilonPi = -(Ac - Ap - As) * sigmaE * (1 / (Ec * (Ac - Ap - As)) + 1 / (Ep * Ap) + Es * As / (Ec * (Ac - Ap - As) * Ep * Ap));
            Tendons.Prestrain = Tendons.EpsilonPi;
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);

            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectionTendons.GetForceAndMoment(true, Tendons, epsilonC, curvature);
            var result4 = CircularPipeSectionTendons.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2;
            return (N, M, epsilonC);
        }

        // 使用限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((Fts - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) / 2.0 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(1.0, (Fcs + Fts) / 2.0 - SigmaE) );
            Ms.Add(0.0);

            epsilonCs.Add(0.0);
            epsilonCs.Add(0.0);
            epsilonCs.Add(0.0);

            curvatures.Add(0.0);
            curvatures.Add(0.0);
            curvatures.Add(0.0);

            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 使用限界MNインタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetFactoredServiceLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) / 2.0 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(0.9, 4.0 - SigmaE) );
            Ms.Add(GetServiceLimitMoment(0.9, (Fcs + Fts) / 2.0 - SigmaE) );
            Ms.Add(GetServiceLimitMoment(0.9, Fcs - SigmaE) );
            Ms.Add(0.0);

            for(int i = 0; i < 5; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }
            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 損傷限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetDamageLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((Ftd - SigmaE) * Ae);
            Ns.Add(((Fcd + Ftd) / 2.0 - SigmaE) * Ae);
            Ns.Add((Fcd - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetDamageLimitMoment(1.0, ((Fcd + Ftd) / 2.0 - SigmaE)));
            Ms.Add(0.0);

            epsilonCs.Add(0.0);
            epsilonCs.Add(0.0);
            epsilonCs.Add(0.0);

            curvatures.Add(0.0);
            curvatures.Add(0.0);
            curvatures.Add(0.0);

            for (int i = 0; i < Ns.Count; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }
            return (Ns, Ms, epsilonCs, curvatures);
        }

        internal (List<double>, List<double>, List<double>, List<double>) GetFactoredDamageLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((10.0 - SigmaE) * Ae);
            Ns.Add((10.0 - SigmaE) * Ae);
            Ns.Add(((Fcd + Ftd) / 2.0 - SigmaE) * Ae);
            Ns.Add((35.0 - SigmaE) * Ae);
            Ns.Add((35.0 - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetDamageLimitMoment(0.65, (4.0 - SigmaE)));
            Ms.Add(GetDamageLimitMoment(0.65, (10.0 - SigmaE)));
            Ms.Add(GetDamageLimitMoment(0.75, (10.0 - SigmaE)));
            Ms.Add(GetDamageLimitMoment(0.75, ((Fcd + Ftd) / 2.0 - SigmaE)));
            Ms.Add(GetDamageLimitMoment(0.75, (35.0 - SigmaE)));
            Ms.Add(0.0);

            for (int i = 0; i < Ns.Count; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }

            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 損傷限界モーメント取得メソッド
        internal double GetDamageLimitMoment(double beta, double Sigma0E)
        {
            double Md1 = Ze * (-Ftd + SigmaE + Sigma0E);
            double Md2 = Ze * (Fcd - SigmaE - Sigma0E);
            return beta * Math.Min(Md1, Md2);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectionTendons.GetForceAndMoment(false, Tendons, epsilonC, curvature);
            var result4 = CircularPipeSectionTendons.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2;
            return (N, M);
        }
    }

    // PRCSection杭クラス
    internal class PRCSection: PrecastPileSection
    {
        public CirclularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CirclularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectionTendons { get; private set; }
        public CircularPipeSection CircularPipeSectionMainBars { get; private set; }

        //public PrecastConcrete PrecastConcrete { get; private set; }
        //public Tendons Tendons { get; private set; }
        //public MainBars MainBars { get; private set; }

        // コンストラクタ
        internal PRCSection(PrecastPRCConcrete precastConcrete, MainBars mainBars, Tendons tendons, double prestress)
        {
            PrecastConcrete = precastConcrete;
            MainBars = mainBars;
            Tendons = tendons;

            PileDia = precastConcrete.DO;

            Ro = precastConcrete.DO / 2.0;
            Ri = precastConcrete.DI / 2.0;
            Ap = tendons.Ap;
            Ag = mainBars.Ag;
            Rp = tendons.PCD / 2.0;
            Rg = mainBars.PCD / 2.0;
            Fc = precastConcrete.Fc;
            SigmaE = prestress;
            SetSectionParameters();

            SetEpsilonPi(Ac, Ap, Ag, PrecastConcrete.Ec, Tendons.Ep, MainBars.Er, SigmaE);
            SetEpsilonE(PrecastConcrete.Ec, SigmaE);
            SetEpsilonSi(PrecastConcrete.Ec, SigmaE);

            CircularSolidSectionConcreteOut = new CirclularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CirclularSolidSection(precastConcrete.DI);
            CircularPipeSectionTendons = new CircularPipeSection(tendons.PCD, tendons.Ap / Math.PI / tendons.PCD);
            CircularPipeSectionMainBars = new CircularPipeSection(mainBars.PCD, mainBars.Ag / Math.PI / mainBars.PCD);

            PositionCs = new List<double> { -PileDia / 2, -MainBars.PCD / 2, -Tendons.PCD / 2 };
            PositionTs = new List<double> { PileDia / 2, MainBars.PCD / 2, Tendons.PCD / 2 };

            // プレストレスひずみ度
            Prestrains = new List<double> { PrecastConcrete.Prestrain, mainBars.Prestrain, Tendons.Prestrain };

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = new List<double> { PrecastConcrete.ServiceLimitStrainC - PrecastConcrete.Prestrain, mainBars.ServiceLimitStrainC - mainBars.Prestrain, Tendons.ServiceLimitStrainC - Tendons.Prestrain, };
            ServiceLimitStrainTs = new List<double> { PrecastConcrete.ServiceLimitStrainT - PrecastConcrete.Prestrain, mainBars.ServiceLimitStrainT - mainBars.Prestrain, Tendons.ServiceLimitStrainT - Tendons.Prestrain, };

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = new List<double> { PrecastConcrete.DamageLimitStrainC - PrecastConcrete.Prestrain, mainBars.DamageLimitStrainC - mainBars.Prestrain, Tendons.DamageLimitStrainC - Tendons.Prestrain, };
            DamageLimitStrainTs = new List<double> { PrecastConcrete.DamageLimitStrainT - PrecastConcrete.Prestrain, mainBars.DamageLimitStrainT - mainBars.Prestrain, Tendons.DamageLimitStrainT - Tendons.Prestrain };

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = new List<double> { PrecastConcrete.UltimateLimitStrainC - PrecastConcrete.Prestrain, mainBars.UltimateLimitStrainC - mainBars.Prestrain, Tendons.UltimateLimitStrainC - Tendons.Prestrain, };
            UltimateLimitStrainTs = new List<double> { PrecastConcrete.UltimateLimitStrainT - PrecastConcrete.Prestrain, mainBars.UltimateLimitStrainT - mainBars.Prestrain, Tendons.UltimateLimitStrainT - Tendons.Prestrain, };

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 安全限界最大曲率
            CurvatureMaxUltimateLimit = GetAllowableMaxCurvature(UltimateLimitStrainCs, PositionCs, UltimateLimitStrainTs, PositionTs);

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInterection();

            // 使用限界最大曲率時の軸力
            AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            // 損傷限界最大曲率時の軸力
            AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            //安全限界最大曲率時の軸力
            AxialForceCurvatureMaxUltimateLimit = GetAllowableForceAndMoment(2, true, CurvatureMaxUltimateLimit).Item1;

            // 低減前使用限界NMインタラクション
            FactoredServiceNM = GetFactoredServiceLimitMNInteraction();

            // 損傷限界軸力閾値
            DamagaLimitAxialForceThresholds = new List<double>
            {
                (4.0 - SigmaE) * Ae,
                (10.0 - SigmaE) * Ae,
                (35.0 - SigmaE) * Ae,
            };

            // 損傷限界閾値
            DamagaLimitBendingMomentThresholds = GetDamagaLimitBendingMomentThresholds();

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = new List<double> { 0.0, 0.8 * 0.65, 0.8 * 0.75, 0.0 };

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredMNInterection(UnfactoredDamageNM, (DamagaLimitAxialForceThresholds, DamagaLimitBendingMomentThresholds), DamageLimitBeta);

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds = new List<double>
            {
                (4.0 - SigmaE) * Ae,
                (10.0 - SigmaE) * Ae,
                (60.0 - SigmaE) * Ae,
            };

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = new List<double> { 0.0, 0.80 * 0.75, 0.80 * 0.65, 0.0 };

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

        }


        // コンクリートのひずみ度取得メソッド
        internal void SetEpsilonE(double Ec, double sigmaE)
        {
            PrecastConcrete.EpsilonE = sigmaE / Ec;
            PrecastConcrete.Prestrain = PrecastConcrete.EpsilonE;
        }

        // テンドンのプレストレスひずみ取得メソッド
        internal void SetEpsilonPi(double Ac, double Ap, double As, double Ec, double Ep, double Es, double sigmaE)
        {
            //Tendons.EpsilonPi = -(Ac - Ap - As) * sigmaE * (1 / (Ec * (Ac - Ap - As)) + 1 / (Ep * Ap) + Es * As / (Ec * (Ac - Ap - As) * Ep * Ap));
            Tendons.EpsilonPi = -(Ac - Ap - As) * sigmaE * (1 / (Ep * Ap) + Es * As / (Ec * (Ac - Ap - As) * Ep * Ap));
            Tendons.Prestrain = Tendons.EpsilonPi;
        }

        // 鉄筋のプレストレスひずみ取得メソッド
        internal void SetEpsilonSi(double Ec, double sigmaE)
        {
            MainBars.EpsilonSi = sigmaE / Ec;
            MainBars.Prestrain = MainBars.EpsilonSi;
        }

        // 使用限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((Fts - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) / 2.0 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(1.0, (Fcs + Fts) / 2.0 - SigmaE));
            Ms.Add(0.0);

            for (int i = 0; i < Ns.Count; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }

            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 使用限界MNインタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetFactoredServiceLimitMNInteraction()
        {
            List<double> Ns = new List<double>();
            List<double> Ms = new List<double>();
            List<double> epsilonCs = new List<double>();
            List<double> curvatures = new List<double>();

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) / 2.0 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(0.8, 4.0 - SigmaE));
            Ms.Add(GetServiceLimitMoment(0.8, (Fcs + Fts) / 2.0 - SigmaE));
            Ms.Add(GetServiceLimitMoment(0.8, Fcs - SigmaE));
            Ms.Add(0.0);

            for (int i = 0; i < Ns.Count; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }
            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);

            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectionMainBars.GetForceAndMoment(true, MainBars, epsilonC, curvature);
            var result4 = CircularPipeSectionMainBars.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);
            var result5 = CircularPipeSectionTendons.GetForceAndMoment(true, Tendons, epsilonC, curvature);
            var result6 = CircularPipeSectionTendons.GetForceAndMoment(true, PrecastConcrete, epsilonC, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1 + result5.Item1 - result6.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2 + result5.Item2 - result6.Item2;
            return (N, M, epsilonC);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectionTendons.GetForceAndMoment(false, Tendons, epsilonC, curvature);
            var result4 = CircularPipeSectionTendons.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result5 = CircularPipeSectionMainBars.GetForceAndMoment(false, MainBars, epsilonC, curvature);
            var result6 = CircularPipeSectionMainBars.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1 + result5.Item1 - result6.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2 + result5.Item2 - result6.Item2;
            return (N, M);
        }
    }

    // PRCSection杭クラス
    internal class SCSection : PrecastPileSection
    {
        public CirclularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CirclularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectioSteelPipe { get; private set; }
        public double Tc { get; private set; }

        // コンストラクタ
        internal SCSection(PrecastSCConcrete precastConcrete, PrecastSteelPipe precastPipe)
        {
            PrecastConcrete = precastConcrete;
            PrecastSteelPipe = precastPipe;

            PileDia = precastConcrete.DO;

            Ro = precastConcrete.DO / 2.0;
            Ri = precastConcrete.DI / 2.0;
            Tc = Ro - Ri;
            Fc = precastConcrete.Fc;
            double pipeCenterDia = PrecastSteelPipe.OutDia - PrecastSteelPipe.T;
            double pipeT = PrecastSteelPipe.T;

            SetSectionParameters();

            CircularSolidSectionConcreteOut = new CirclularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CirclularSolidSection(precastConcrete.DI);
            CircularPipeSectioSteelPipe = new CircularPipeSection(pipeCenterDia, pipeT);

            PositionCs = new List<double> { -PileDia / 2, -pipeCenterDia / 2, };
            PositionTs = new List<double> { PileDia / 2, pipeCenterDia / 2, };

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = new List<double> { PrecastConcrete.ServiceLimitStrainC, PrecastSteelPipe.ServiceLimitStrainC  };
            ServiceLimitStrainTs = new List<double> { PrecastConcrete.ServiceLimitStrainT, PrecastSteelPipe.ServiceLimitStrainT };

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = new List<double> { PrecastConcrete.DamageLimitStrainC, PrecastSteelPipe.DamageLimitStrainC };
            DamageLimitStrainTs = new List<double> { PrecastConcrete.DamageLimitStrainT, PrecastSteelPipe.DamageLimitStrainT };

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = new List<double> { PrecastConcrete.UltimateLimitStrainC, PrecastSteelPipe.UltimateLimitStrainC };
            UltimateLimitStrainTs = new List<double> { PrecastConcrete.UltimateLimitStrainT, PrecastSteelPipe.UltimateLimitStrainT };

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 安全限界最大曲率
            CurvatureMaxUltimateLimit = GetAllowableMaxCurvature(UltimateLimitStrainCs, PositionCs, UltimateLimitStrainTs, PositionTs);

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInterection();

            //// 使用限界最大曲率時の軸力
            //AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            //// 損傷限界最大曲率時の軸力
            //AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            //安全限界最大曲率時の軸力
            AxialForceCurvatureMaxUltimateLimit = GetAllowableForceAndMoment(2, true, CurvatureMaxUltimateLimit).Item1;

            // 低減前使用限界NMインタラクション
            FactoredServiceNM = UnfactoredServiceNM;

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = UnfactoredDamageNM;

            // 安全限界軸力低減率
            //UltimateLimitAxialForceThresholds = new List<double>
            //{
            //    (4.0 - SigmaE) * Ae,
            //    (10.0 - SigmaE) * Ae,
            //    (60.0 - SigmaE) * Ae,
            //};

            //// 安全限界閾値
            //UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            //// 安全限界曲げモーメント低減率
            //UltimateLimitBeta = new List<double> { 0.0, 0.80 * 0.75, 0.80 * 0.65, 0.0 };

            //// 低減後安全限界NMインタラクション
            //FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

        }


        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);

            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectioSteelPipe.GetForceAndMoment(false, PrecastSteelPipe, epsilonC, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 ;
            M = result1.Item2 - result2.Item2 + result3.Item2;
            return (N, M, epsilonC);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(false, PrecastConcrete, epsilonC, curvature);
            var result3 = CircularPipeSectioSteelPipe.GetForceAndMoment(false, PrecastSteelPipe, epsilonC, curvature);


            N = result1.Item1 - result2.Item1 + result3.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2;
            return (N, M);
        }

        // 安全限界MN インタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInterection()
        {
            List<double> axialForces = new List<double> { };
            List<double> bendingMoments = new List<double> { };
            List<double> epsilonCs = new List<double> { };
            List<double> curvatures = new List<double> { };
            double epsilonC = 0.0;
            double curvature = 0.0;
            double maxCurvature = (0.003 + 0.0025) * 20.0 / PileDia;
            double maxEpsilonCu = 0.003;
            double epsilonCu;
            double epsilonCu_next;
            double N0;
            double N = 0.0;
            double M = 0.0;

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                epsilonCu = 0;
                epsilonCu_next = maxEpsilonCu;
                while (epsilonCu_next - epsilonCu > 0.00001)
                {
                    epsilonCu = epsilonCu_next;
                    if (i == 0)
                    {
                        //epsilonCu = maxEpsilonCu;
                        epsilonC = -PrecastSteelPipe.EpsilonY;
                        curvature = 0.0;
                    }
                    else if (i != DivisionNum * 2)
                    {
                        //epsilonCu = maxEpsilonCu;
                        curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
                        epsilonC = epsilonCu + curvature * PrecastSteelPipe.T;
                    }
                    else {
                        //epsilonCu = maxEpsilonCu;
                        epsilonC = maxEpsilonCu;
                        curvature = 0.0;
                    }

                    var result = GetUltimateForceAndMoment(epsilonC, curvature); // 引張側 純引張～
                    N = result.Item1;
                    M = result.Item2;
                     
                    if (N > 0)
                    {
                        N0 = Math.Abs(PrecastSteelPipe.As * PrecastSteelPipe.Fys + Ac * Fc);
                    }
                    else // (N <= 0)
                    {
                        N0 = PrecastSteelPipe.As * PrecastSteelPipe.Fys;
                    }
                    if ((PileDia - 2 * PrecastSteelPipe.T) / Tc <= 6)
                    {
                        epsilonCu_next = Math.Max(0.003, Math.Min(0.004, 0.006 - 0.01 * N / N0));
                    }
                    else
                    {
                        epsilonCu_next = epsilonCu;
                    }
                }

                axialForces.Add(N); //  * Math.Pow(10, -3));
                bendingMoments.Add(M); // * Math.Pow(10, -6));
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }
    }

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
        internal (double, double) GetForceAndMoment(bool isLinear, Material material, double epsilonC, double curvature, int division = 100)
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
                z = -Dia / 2 + (0.5 + i) * dz;
                width = 2.0 * Math.Sqrt(Math.Pow(Dia / 2, 2) - Math.Pow(z, 2));
                epsilon = epsilonC - curvature * (z + Dia / 2.0);
                sigma = material.GetStress(isLinear, epsilon);
                axialForce += width * sigma * dz;
                bendingMoment += width * sigma * dz * (-z);
            }
            return (axialForce, bendingMoment);
        }
    }

    // 円環断面クラス
    internal class CircularPipeSection
    {
        private double Dia { get; }
        private double T { get; }

        // コンストラクタ
        public CircularPipeSection(double diameter, double t)
        {
            Dia = diameter;
            T = t;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(bool isLinear, Material material, double epsilonC, double curvature, int division = 100)
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
                z = Dia / 2.0 * Math.Cos(2.0 * Math.PI * i / division);
                epsilon = epsilonC - curvature * (z + Dia / 2.0);
                sigma = material.GetStress(isLinear, epsilon);
                axialForce += T * sigma * dCirc;
                bendingMoment += T * sigma * dCirc * (-z);
            }
            return (axialForce, bendingMoment);
        }
    }

}