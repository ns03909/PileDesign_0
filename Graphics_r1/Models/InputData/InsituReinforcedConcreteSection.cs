using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // 場所打ち鉄筋コンクリート杭断面クラス
    internal class InsituReinforcedConcreteSection : AbstractPileSection
    {
        public CircularSolidSection CircularSolidSectionConcrete { get; private set; }
        public CircularPipeSection CircularPipeSectionMainbars { get; private set; }

        public InsituConcrete InsituConcrete { get; private set; }
        public MainBars MainBars { get; private set; }

        public double MainBarArea { get; private set; }
        public double MainBarPCD { get; private set; }

        public double Ae { get; private set; }
        public double Ze { get; private set; }
        public double Ie { get; private set; }
        public double Ft { get; private set; }

        public List<double> ServiceLimitShearAxialForceThresholds { get; private set; }

        // コンストラクタ
        // applyBodyMaterialOptions: 杭体断面のみ true。鉄筋 1.1F 完全バイリニア型オプションを適用する。
        //   杭頭接合部（PileTop の定着筋断面）などは false を渡してオプション対象外とする。
        internal InsituReinforcedConcreteSection(
            InsituConcrete insituConcrete, MainBars mainBars, bool applyBodyMaterialOptions = true)
        {
            // 鉄筋 1.1F 完全バイリニア型オプション（降伏応力度 σy → 1.1σy）を適用（限界ひずみ再計算より前）
            if (applyBodyMaterialOptions)
                mainBars.YieldAt11F = ConcreteModelOptions.RebarYieldAt11F;

            PileDia = insituConcrete.DO;
            MainBarArea = mainBars.Ag; //mainBarArea;
            MainBarPCD = mainBars.PCD;  //mainBarPcd;

            CircularSolidSectionConcrete = new CircularSolidSection(PileDia);
            CircularPipeSectionMainbars = new CircularPipeSection(MainBarPCD, MainBarArea / Math.PI / MainBarPCD);

            InsituConcrete = insituConcrete;
            MainBars = mainBars;

            PositionCs = [-PileDia / 2.0, -MainBarPCD / 2.0,];
            PositionTs = [PileDia / 2.0, MainBarPCD / 2.0,];

            // プレストレスひずみ度
            Prestrains = [insituConcrete.Prestrain, mainBars.Prestrain];

            SetZeFtIe();

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [insituConcrete.ServiceLimitStrainC, mainBars.ServiceLimitStrainC,];
            ServiceLimitStrainTs = [insituConcrete.ServiceLimitStrainT, mainBars.ServiceLimitStrainT,];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [insituConcrete.DamageLimitStrainC, mainBars.DamageLimitStrainC,];
            DamageLimitStrainTs = [insituConcrete.DamageLimitStrainT, mainBars.DamageLimitStrainT,];

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 使用限界最大曲率時の軸力
            AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            // 損傷限界最大曲率時の軸力
            AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = [];

            // 使用限界曲げモーメント低減率
            ServiceLimitBeta = [1.0];

            // 損傷限界軸力閾値
            DamageLimitAxialForceThresholds =
            [
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 3.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
            ];

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = [0.0, 1.0, 0.65, 0.0];        // L2: β1=1.0, β2={1.0, 0.65}
            DamageLimitBetaL1 = [0.0, 1.0, 1.0, 0.0];       // L1: β2 を乗じない（β1=1.0 のみ）

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds =
            [
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 4.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 3.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
            ];

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 0.95 * 1.0, 0.95 * 1.0, 0.80 * 0.65, 0.0];

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInteraction();

            // 損傷限界閾値
            ServiceLimitBendingMomentThresholds = [];

            // 損傷限界閾値
            DamageLimitBendingMomentThresholds = GetDamageLimitBendingMomentThresholds();

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 低減後使用限界NMインタラクション
            FactoredServiceNM = GetFactoredMNInteraction(UnfactoredServiceNM, (ServiceLimitAxialForceThresholds, ServiceLimitBendingMomentThresholds), ServiceLimitBeta);

            // 低減後損傷限界NMインタラクション（レベル2: β1×β2）
            FactoredDamageNM = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBeta);
            // レベル1: β2 を乗じない（β1 のみ）
            FactoredDamageNMLevel1 = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBetaL1);

            // 低減後安全限界NMインタラクション
            // 指針(案) 準拠オプション時は安全限界曲げ強度をそのまま採用し、低減・軸力制限を課さない
            // （低減後＝低減前の全体を描く）。既定は基礎部材の低減率(β1/β1β2)・軸力制限を適用。
            FactoredUltimateNM = ConcreteModelOptions.UseInsituUltimateEFunction
                ? UnfactoredUltimateNM
                : GetFactoredMNInteraction(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            // 低減前使用限界NQインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction(3.0, false);

            // 低減前損傷限界NQインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction(3.0, false);

            // 低減前安全限界NQインタラクション
            UnfactoredUltimateNQ = GetUltimateQNInteraction(3.0, 0.002, 295, false);

            // 低減前使用限界NQインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction(3.0, true);

            // 低減前損傷限界NQインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction(3.0, true);

            // 低減前安全限界NQインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction(3.0, 0.002, 295, true);


            // NOTE: ここで SteelYieldNM を重い計算で生成しない（ウィンドウ起動を阻害するため）
            // SteelYieldNM = GetSteelYieldMNInteraction();
            //

        }

        /// <summary>
        /// 使用限界せん断力を返す。
        /// </summary>
        private double GetServiceLimitShear(double MonQd, double sigma0, bool isFactored)
        {
            double beta1 = 0.9;
            double kc = 0.72;
            double b = Math.PI * PileDia / 4.0;
            double d = 0.9 * PileDia;
            double j = 7.0 / 8.0 * d;
            if (ConcreteModelOptions.UseNotification1113Shear)
            {
                // 告示1113(第8): 使用限界=長期許容せん断応力度 fs。許容せん断力 Q = fs·b·j
                // （軸力・M/(Q·d) 非依存、低減係数を乗じないため 低減前/低減後 は同値）。
                return GetNotification1113LongTermShearStress() * b * j;
            }
            return (isFactored ? beta1 : 1.0) * 2.0 / 3.0 * 0.065 * kc * (49.0 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 1.7) * (1 + sigma0 / 14.7) * b * j;
        }

        /// <summary>
        /// 告示 平13国交告第1113号(第8) の場所打ちコンクリート長期許容せん断応力度 fs（N/mm²）。
        /// fs = min( Fc/40 ［区分2 は Fc/45］, (3/4)(0.49 + Fc/100) )。短期はこの 1.5 倍。
        /// </summary>
        private double GetNotification1113LongTermShearStress()
        {
            double Fc = InsituConcrete.Fc;
            double baseTerm = ConcreteModelOptions.Notification1113CompressionCase == 2 ? Fc / 45.0 : Fc / 40.0;
            double archTerm = 0.75 * (0.49 + Fc / 100.0);
            return Math.Min(baseTerm, archTerm);
        }

        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(double MonQd, double sigma0, int level, bool isFactored)
        {
            double beta1 = 0.9;
            double beta2 = sigma0 <= 1.0 / 3.0 * InsituConcrete.Gsi * InsituConcrete.Fc ? 0.75 : 0.65;
            double beta = level == 1 ? beta1 : level == 2 ? beta1 * beta2 : beta1 * beta2;
            double kc = 0.72;
            double b = Math.PI * PileDia / 4.0;
            double d = 0.9 * PileDia;
            double j = 7.0 / 8.0 * d;
            if (ConcreteModelOptions.UseNotification1113Shear)
            {
                // 告示1113(第8): 損傷限界=短期許容せん断応力度 = 長期の 1.5 倍。Q = fs_短期·b·j
                // （レベル・軸力・M/(Q·d) 非依存、低減係数なし）。
                return 1.5 * GetNotification1113LongTermShearStress() * b * j;
            }
            return (isFactored ? beta : 1.0) * 0.065 * kc * (49.0 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 1.7) * (1 + sigma0 / 14.7) * b * j;
        }

        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double MonQd, double sigma0, double pt, double pw, double sigmaWy, bool isFactored)
        {
            double beta1 = 0.8;
            double beta2 = sigma0 <= 1.0 / 3.0 * InsituConcrete.Gsi * InsituConcrete.Fc ? 0.75 : 0.65;
            double b = Math.PI * PileDia / 4.0;
            double d = 0.9 * PileDia;
            double j = 7.0 / 8.0 * d;
            return (isFactored ? beta1 * beta2 : 1.0) * (0.053 * Math.Pow(pt, 0.23) * (18 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 0.12) + 0.85 * Math.Sqrt(pw * sigmaWy) + 0.1 * sigma0) * b * j;
        }

        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear(MonQd, n / Ae, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 損傷限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetDamageLimitQNInteraction(double MonQd, bool isFactored, int level = 1, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetDamageLimitShear(MonQd, n / Ae, level, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(double MonQd, double pw, double sigmaWy, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double pg = MainBarArea / InsituConcrete.Ac;
            double pt = 100 * pg / 4.0;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetUltimateLimitShear(MonQd, n / Ae, pt, pw, sigmaWy, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }


        /// <summary>
        /// 引張側主筋が降伏し始めるとき（降伏開始）のNMインタラクションで、軸力が最大の(N,M)を返す。
        /// φ 上限スケール取得目的（降伏条件: εs = εc − φ·lever = −εy）
        /// </summary>
        private (double N, double M, double epsilonC, double phi) GetSteelYieldNMax()
        {
            double epsY = MainBars.RSigmaY / MainBars.Er; // 正: 降伏ひずみ
            double lever = (PileDia * 0.5 + MainBars.PCD * 0.5);
            double epsilonC = PileDesign.Constants.SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN; // 終局側の代表圧縮縁ひずみ εcu
            double phi = (epsilonC + epsY) / Math.Max(lever, 1e-9); // φ = (εc + εy)/lever
            var (N, M) = GetUltimateForceAndMoment(epsilonC, phi);
            return (N, M, epsilonC, phi);
        }

        internal (List<double> axialForces, List<double> bendingMoments, List<double> epsilonCs, List<double> curvatures)
        GetCrackMNInteraction(bool isLinear = false)
        {
            var axialForces = new List<double>();
            var bendingMoments = new List<double>();
            var epsilonCs = new List<double>();
            var curvatures = new List<double>();

            int div = DivisionNum > 0 ? DivisionNum : 60;

            // 走査する軸力範囲（表示に使っている閾値をそのまま採用）
            double NMin = UltimateLimitAxialForceThresholds[0];
            double NMax = UltimateLimitAxialForceThresholds[3];

            // 断面特性
            double lever = PileDia; // 圧縮縁-引張縁距離（直径）

            for (int i = 0; i <= div; i++)
            {
                double Ntarget = NMin + (NMax - NMin) * i / div;

                // 平均応力度（軸力による引張側応力度の増減をMcrへ反映）
                double sigma0e = Ntarget / Ae;

                // ひび割れ引張強度（Ft = 0.56*sqrt(ξ·Fc)）
                double FtLoc = Ft;

                double Mcr, phiCr, epsTcr;

                if (isLinear)
                {
                    // 線形法: εt,cr = Ft/Ec, Mcr = Ze*(Ft + σ0e), φcr = Mcr/(Ec*Ie)
                    epsTcr = FtLoc / InsituConcrete.Ec;
                    Mcr = Ze * (FtLoc + sigma0e);
                    if (Mcr < 0) { Mcr = 0; phiCr = 0; }
                    else { phiCr = Mcr / (InsituConcrete.Ec * Ie); }
                }
                else
                {
                    // e関数法:
                    // 1) εt,cr を e関数から逆算（σ=Ft を満たす ε）
                    epsTcr = InsituConcrete.GetEFuncEpsilon(FtLoc);
                    (Mcr, phiCr) = GetCrackMoment(Ntarget, isLinear);
                }

                // 参考: その時の圧縮縁ひずみ（εt = -epsTcr を満たすように εc = -εt + φ·lever）
                double epsC = -epsTcr + phiCr * lever;

                axialForces.Add(Ntarget);
                bendingMoments.Add(Mcr);
                epsilonCs.Add(epsC);
                curvatures.Add(phiCr);
            }

            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        /// <summary>
        /// 引張側主筋が降伏し始めるとき（降伏開始）のNMインタラクションを取得する。
        /// 形式は (axialForces, bendingMoments, epsilonCs, curvatures) で、FactoredUltimateNM 等と同じ。
        /// 生成方針:
        ///  1) 先に曲率φをパラメトリックに走査し、降伏条件(εs = -σy/Es)を満たす状態の N(φ) 範囲[minN,maxN]を把握
        ///  2) その範囲で Ntarget を等分割し、GetYieldMoment(Ntarget) を用いて (M, φ) を解く
        ///  3) その (φ) に対応する圧縮縁ひずみ εc = -σy/Es + φ*(D/2 + PCD/2) を計算
        /// 備考:
        ///  - 2)で得た (M, φ) から再度 N, M を算出して返却するため、Ntarget≒N実値 になる
        ///  - 一部 Ntarget で解が安定しない場合、事前走査結果から最も近いφを用いるフォールバックあり
        /// </summary>
        internal (List<double> axialForces, List<double> bendingMoments, List<double> epsilonCs, List<double> curvatures)
            GetSteelYieldMNInteraction()
        {
            var axialForces = new List<double>();
            var bendingMoments = new List<double>();
            var epsilonCs = new List<double>();
            var curvatures = new List<double>();

            // パラメータ
            int div = DivisionNum > 0 ? DivisionNum : 60;
            double epsY = MainBars.RSigmaY / MainBars.Er; // >0
            double lever = (PileDia * 0.5 + MainBars.PCD * 0.5);

            (double MMin, double phiMin) = GetSteelYieldMoment(UltimateLimitAxialForceThresholds[0]);
            (double MMax, double phiMax) = GetSteelYieldMoment(UltimateLimitAxialForceThresholds[3]);

            // 1) φ走査で降伏線上の N(φ) 範囲を得る
            var scanNs = new List<double>();
            var scanPhis = new List<double>();
            var scanMs = new List<double>();
            var scanEpsC = new List<double>();
            for (int i = 0; i <= div * 2; i++)
            {
                double phi = phiMin + (phiMax - phiMin) * i / (div * 2.0);
                // 降伏条件: εs = εc - φ*lever = -epsY → εc = -epsY + φ*lever
                double epsC = -epsY + phi * lever;

                var (N, M) = GetUltimateForceAndMoment(epsC, phi);
                scanNs.Add(N);
                scanPhis.Add(phi);
                scanMs.Add(M);
                scanEpsC.Add(epsC);
            }
            return (scanNs, scanMs, scanEpsC, scanPhis);
        }

        // Ze, Ft, Ieのセット
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
        internal (double, double) GetCrackMoment(double Ntarget, bool isLinear)
        {
            // 安全ガード
            if (Ae <= 0) return (0, 0);

            // 平均応力度が -Ft 未満（強い引張）のときは Mcr=0
            if (Ntarget / Ae < -Ft) return (0, 0);

            if (isLinear)
            {
                double sigma0e = Ntarget / Ae; // N/mm2
                double Mcr = Ze * (Ft + sigma0e); // Nmm
                if (Mcr < 0) { return (0, 0); }
                double phiCr = Mcr / InsituConcrete.Ec / Ie;
                return (Mcr, phiCr);
            }
            else
            {
                // e関数法: 引張縁のひずみ閾値を Ft から逆算（堅牢）
                double epsTcr = InsituConcrete.GetEFuncEpsilon(Ft); // >0（引張ひずみの絶対値）
                //double epsilonCT = InsituConcrete.EpsilonCr_eFunction;
                if (epsTcr <= 0) return (0, 0);
                double lever = PileDia;

                // 初期値
                double phi0 = Math.Max(1e-7, epsTcr / lever * 0.5);
                double phiMin = Math.Max(1e-8, phi0 * 0.1);
                double phiMaxFromCrack = (Math.Max(InsituConcrete.EpsilonCu, 0.003) + epsTcr) / lever;
                // フォールバック（万一）
                double phiMax = double.IsFinite(phiMaxFromCrack) && phiMaxFromCrack > phiMin * 1.2
                    ? phiMaxFromCrack
                    : phiMin * 100.0;

                // 収束条件
                const int maxIter = 60;
                double tolN = Math.Max(PileDesign.Constants.SectionSolverTolerances.CRACK_AXIAL_ABS_N,
                                       PileDesign.Constants.SectionSolverTolerances.CRACK_AXIAL_REL * Math.Abs(Ntarget)); // N
                const double tolPhiRel = 1e-6;

                double phi = phi0;
                for (int iter = 0; iter < maxIter; iter++)
                {
                    // ひび割れ条件: εt = -epsTcr → εc = -εt + φ*lever = -epsTcr + φ*lever
                    double epsC = -epsTcr + phi * lever;

                    (double N, double M) = GetUltimateForceAndMoment(epsC, phi);
                    double f = N - Ntarget;
                    if (Math.Abs(f) < tolN)
                        return (M, phi);

                    // 数値微分
                    double dPhi = Math.Max(phi * 1e-4, 1e-10);
                    double epsC2 = -epsTcr + (phi + dPhi) * lever;
                    (double N2, _) = GetUltimateForceAndMoment(epsC2, phi + dPhi);
                    double dNdPhi = (N2 - N) / dPhi;

                    if (Math.Abs(dNdPhi) < 1e-12) break; // 勾配消失

                    // Newton ステップ
                    double step = f / dNdPhi;
                    // ステップ制限（暴走抑止）
                    double limit = Math.Max(phi * 0.5, (phiMax - phiMin) * 0.1);
                    if (Math.Abs(step) > limit) step = Math.Sign(step) * limit;

                    double phiNext = Math.Clamp(phi - step, phiMin, phiMax);

                    if (Math.Abs(phiNext - phi) / (Math.Abs(phi) + 1e-12) < tolPhiRel)
                        return (M, phiNext);

                    phi = phiNext;
                }

                // 不収束：端点評価で返す
                double epsCedge = -epsTcr + phi * lever;
                (_, double Medge) = GetUltimateForceAndMoment(epsCedge, phi);
                return (Medge, phi);
            }
        }

        /// <summary>
        /// 2変数（圧縮縁ひずみ εc, 曲率 φ）同時未知数のニュートン法で
        /// 「指定軸力 Ntarget」かつ「最外縁主筋が引張降伏（εs = -σy/Es）」となる降伏点 (My, φy) を求める。
        /// 戻り値: (hasYield, My, phiY)
        /// - 収束しなければ hasYield=false を返し、既存の近似手法にフォールバック可能とする。
        /// - 解析上の符号規約（圧縮ひずみ正／引張ひずみ負）を前提。
        /// </summary>
        private (bool hasYield, double My, double phiY) SolveYieldPoint2DNewton(double Ntarget)
        {
            // 引張降伏ひずみ（絶対値）：σy / Es
            double epsY = MainBars.RSigmaY / MainBars.Er;              // >0
            double cSteel = (PileDia * 0.5 + MainBarPCD * 0.5);         // 圧縮縁から主筋重心までの距離
            if (cSteel <= 0) return (false, 0, 0);

            // 初期推定
            // 旧手法の推定曲率を利用し、圧縮縁ひずみは一旦コンクリートの 0.003 程度から開始
            double phi0 = epsY / Math.Max(cSteel, 1e-9);                // 旧: epsY/lever
            if (phi0 <= 0) phi0 = 1e-5;
            //double epsC0 = epsY + phi0 * cSteel;                        // εs = εc - φ*cSteel = -epsY → εc = -epsY + φ*cSteel
            double epsC0 = -epsY + phi0 * cSteel;                        // εs = εc - φ*cSteel = -epsY → εc = -epsY + φ*cSteel
            epsC0 = Math.Max(epsC0, 0.001);                             // 過小初期値防止

            double epsC = epsC0;
            double phi = phi0;

            const int maxIter = 40;
            double tolF = 1e-3 * Math.Max(Math.Abs(Ntarget), 1.0);      // f1 許容（軸力残差）
            double tolPhiRel = 1e-4;                                    // φ 相対更新許容
            double damping = 1.0;

            // 数値差分ステップ
            double dEps = 1e-6;
            double dPhi = 1e-6;

            double My = 0;
            bool converged = false;

            for (int iter = 0; iter < maxIter; iter++)
            {
                // f1, f2 の評価
                (double Nval, double Mval) = GetUltimateForceAndMoment(epsC, phi);
                My = Mval;
                double f1 = Nval - Ntarget;                     // 軸力条件
                double epsSteel = epsC - phi * cSteel;          // 主筋ひずみ（圧縮縁基準）
                double f2 = epsSteel + epsY;                    // εs = -epsY ⇒ εs + epsY = 0

                // 収束判定
                if (Math.Abs(f1) < tolF &&
                    Math.Abs(f2) < 1e-6 &&
                    iter > 1)
                {
                    converged = true;
                    break;
                }

                // ヤコビアン(J) の数値差分
                // ∂f1/∂epsC
                (double N_deps, _) = GetUltimateForceAndMoment(epsC + dEps, phi);
                double df1_deps = (N_deps - Nval) / dEps;

                // ∂f1/∂phi
                (double N_dphi, _) = GetUltimateForceAndMoment(epsC, phi + dPhi);
                double df1_dphi = (N_dphi - Nval) / dPhi;

                // f2 の解析的導関数
                // f2 = (epsC - phi*cSteel + epsY)
                double df2_deps = 1.0;
                double df2_dphi = -cSteel;

                // 2x2 連立解く: J * Δx = -f
                // | df1_deps  df1_dphi | | dEpsC | = | -f1 |
                // | df2_deps  df2_dphi | | dPhi  |   | -f2 |
                double det = df1_deps * df2_dphi - df1_dphi * df2_deps;
                if (Math.Abs(det) < 1e-16)
                {
                    // ヤコビアン特異 → ダンピング or 終了
                    damping *= 0.5;
                    if (damping < 1e-3) break;
                    continue;
                }

                double dEpsC = (-f1 * df2_dphi - (-f2) * df1_dphi) / det;
                double dPhi_ = (df1_deps * (-f2) - df2_deps * (-f1)) / det;

                // ダンピング（暴走抑制）
                double scale = 1.0;
                // 大きすぎるステップを抑制
                double maxRel = Math.Max(Math.Abs(dEpsC / (Math.Abs(epsC) + 1e-9)),
                                         Math.Abs(dPhi_ / (Math.Abs(phi) + 1e-9)));
                if (maxRel > 0.25) scale = 0.25 / maxRel;

                dEpsC *= damping * scale;
                dPhi_ *= damping * scale;

                // 更新
                epsC += dEpsC;
                phi += dPhi_;

                // 物理制約
                if (phi <= 0) phi = Math.Abs(phi) + 1e-9;
                if (epsC <= 0) epsC = 1e-7;

                // 進捗が乏しい→ダンプ
                if (Math.Abs(dPhi_) / (Math.Abs(phi) + 1e-12) < tolPhiRel &&
                    Math.Abs(f1) < 5 * tolF)
                {
                    // f2 がまだ大きければ降伏未達の可能性
                    if (Math.Abs(f2) > 5e-5)
                        break;
                }
            }

            if (!converged)
            {
                // 降伏が成立しない（高軸力圧縮等）と判定
                return (false, 0, 0);
            }

            return (true, My, phi);
        }

        /// <summary>
        /// 2変数ニュートン法を優先的に使って降伏点を取得し、失敗時は既存手法にフォールバックするラッパ。
        /// </summary>
        internal (bool hasYield, double My, double phiY) GetSteelYieldPoint(double Ntarget)
        {
            var r = SolveYieldPoint2DNewton(Ntarget);
            if (r.hasYield) return r;

            // フォールバック（既存1変数近似）
            var (MyApprox, phiApprox) = GetSteelYieldMoment(Ntarget);
            if (phiApprox > 0 && MyApprox > 0)
                return (true, MyApprox, phiApprox);

            return (false, 0, 0);
        }

        /// <summary>
        /// 最外縁主筋が引張降伏する状態に対応する (M, φ) を与軸力 Ntarget で求める改良版
        /// </summary>
        internal (double M, double curvature) GetSteelYieldMoment(double Ntarget)
        {
            double epsY = MainBars.RSigmaY / MainBars.Er;
            double lever = (PileDia * 0.5 + MainBars.PCD * 0.5);

            // 初期値
            double phi = Math.Max(1e-7, epsY / lever * 0.5);
            double phiMin = Math.Max(1e-8, phi * 0.1);
            (_, _, _, double phiMax) = GetSteelYieldNMax();

            // 収束条件
            const int maxIter = 50;
            const double tolN = 1e-2; // kN
            const double tolPhiRel = 1e-6;

            for (int iter = 0; iter < maxIter; iter++)
            {
                double epsC = -epsY + phi * lever;
                (double N, double M) = GetUltimateForceAndMoment(epsC, phi);

                double f = N - Ntarget;
                if (Math.Abs(f) < tolN)
                    return (M, phi);

                // 数値微分
                double dPhi = Math.Max(phi * 1e-4, 1e-10);
                double epsC2 = -epsY + (phi + dPhi) * lever;
                (double N2, _) = GetUltimateForceAndMoment(epsC2, phi + dPhi);
                double df_dphi = (N2 - N) / dPhi;

                // 極端な場合は収束不能
                if (Math.Abs(df_dphi) < 1e-12)
                    break;

                // Newtonステップ
                double step = f / df_dphi;
                // ステップ幅制限
                if (Math.Abs(step) > phi * 0.5)
                    step = Math.Sign(step) * phi * 0.5;

                double phiNext = phi - step;
                phiNext = Math.Clamp(phiNext, phiMin, phiMax);

                // 収束判定
                if (Math.Abs(phiNext - phi) / (Math.Abs(phi) + 1e-12) < tolPhiRel)
                    return (M, phiNext);

                phi = phiNext;
            }

            // 収束しない場合は端点値
            double epsCedge = -epsY + phi * lever;
            (double _, double Medge) = GetUltimateForceAndMoment(epsCedge, phi);
            return (Medge, phi);
        }

        // C点を返すメソッド
        internal static double GetPhiC(double phiCr, double Mcr, double phiY, double My, double Mu0, double beta1)
        {
            double a1 = phiY - phiCr;
            double a2 = beta1 * Mu0 - Mcr;
            double a3 = (My - Mcr);
            // My≈Mcr（低鉄筋比・高軸力）でのゼロ除算→Inf を防ぐ。分母が微小なら降伏点曲率で代替。
            if (Math.Abs(a3) < 1e-9) return phiY;
            double phiC = phiCr + (phiY - phiCr) * (beta1 * Mu0 - Mcr) / (My - Mcr);
            return phiC;
        }

        // ある軸力時のM-φ関係を得るメソッド（IPileSectionCalculationインターフェース実装）
        // 解析用のため、e関数オプション時でも全区間（MCr・MY・Mu0）をバイリニアで算定し、
        // M-φ が単調（β1·Mu0 ≥ MY）＝正勾配ばねとなることを保証する（FEM 収束のため）。
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double Ntarget)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;
            try
            {
                (double MCr, double phiCr) = GetCrackMoment(Ntarget, false);
                (double MY, double phiY) = GetSteelYieldMoment(Ntarget);
                (double Mu0, double _) = GetUltimateMomentForSpecificN(Ntarget);

                double ag = Math.PI * PileDia * PileDia / 4.0;
                double beta1 = (Ntarget / ag <= (1.0 / 3.0) * InsituConcrete.Gsi * InsituConcrete.Fc) ? 0.95 : 0.80;
                double phiC = GetPhiC(phiCr, MCr, phiY, MY, Mu0, beta1);
                List<double> phis;
                List<double> Ms;

                if (Ntarget / ag <= (1.0 / 3.0) * InsituConcrete.Gsi * InsituConcrete.Fc)
                {
                    phis = [0.0, phiCr, phiY, phiC];
                    Ms = [0.0, MCr, MY, beta1 * Mu0];
                }
                else
                {
                    double beta2 = 0.65;
                    double phiCshort = phiCr + (phiC - phiCr) * (beta1 * beta2 * Mu0 - MCr) / (beta1 * Mu0 - MCr);
                    phis = [0.0, phiCr, phiCshort];
                    Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                }

                return (phis, Ms);
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        // ファイバーモデル M-φ（GetMPhiRelationshipFiber）は AbstractPileSection の共通実装を使用。
        // 掃引終点は既定（安全限界ソルバ、εc=0.003）、材料はガードにより常にバイリニア。

        //// ある軸力時のM-θ関係を得るメソッド
        /// 4点折線: [0, 極小値, θy, θu]
        /// 極小値(1e-8)により初期勾配 Mcr/1e-8 ≈ 実質剛体
        internal (List<double>, List<double>) GetMThetaRelationship(double Ntarget, double alpha = 32)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;   // 解析用のため常にバイリニア
            try
            {
            double beta1 = 0.95;
            (double MCr, double _) = GetCrackMoment(Ntarget, false);
            (double MY, double phiY) = GetSteelYieldMoment(Ntarget);
            (double Mu0, double _2) = GetUltimateMomentForSpecificN(Ntarget);

            // θy = 0.5 * α * D_bar * φy
            double D_bar = ExtractBarSizeNumber(MainBars.BarSize);
            double thetaY = 0.5 * alpha * D_bar * phiY;

            // θu = 1/100 rad（固定）
            double thetaU = 1.0 / 100.0;

            // θ[1] は初期勾配を十分大きくするための固定微小値
            double thetaSmall = 1e-8;

            // 安全ガード
            if (thetaY <= thetaSmall) thetaY = thetaSmall * 1.5;
            if (thetaU <= thetaY) thetaU = thetaY * 2.0;

            List<double> thetas = [0.0, thetaSmall, thetaY, thetaU];
            List<double> Ms = [0.0, MCr, MY, beta1 * Mu0];

            return (thetas, Ms);
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        public static double ExtractBarSizeNumber(string barSize)
        {
            if (string.IsNullOrEmpty(barSize)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(barSize, @"\d+(\.\d+)?");
            if (match.Success && double.TryParse(match.Value, out double value))
                return value;
            return 0;
        }

        // 最外縁の杭主筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonC = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia * 0.5 + MainBars.PCD * 0.5);
            // 最外縁の杭主筋が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        /// <summary>
        /// 純引張時のひずみ度: 鉄筋の引張降伏ひずみ
        /// </summary>
        internal override double GetPureTensionStrain()
            => -MainBars.RSigmaY / MainBars.Er;

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            MaterialLaw type = MaterialLaw.Linear;
            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(type, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M, epsilonC);
        }

        // 解析用 M-φ/M-θ 端点算定・ファイバー掃引用のバイリニア固定ガード _forceBilinearUltimate は
        // AbstractPileSection の protected フィールドを使用。

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            // 指針(案) 5.4.1 準拠オプション時はコンクリートを e関数法、既定はバイリニア。
            // ただし解析 M-φ 端点算定中（_forceBilinearUltimate）は収束のため常にバイリニア。
            MaterialLaw type = (ConcreteModelOptions.UseInsituUltimateEFunction && !_forceBilinearUltimate)
                ? MaterialLaw.EFunction : MaterialLaw.Bilinear;
            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(type, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }

        /// <summary>
        /// 指定した圧縮縁ひずみ εc と曲率 φ に対する断面のひずみ度・応力度分布を返す。
        /// コンクリート(実心)＋主筋(リング)。ultimate=true でバイリニア、false で線形。
        /// </summary>
        internal override SectionStrainStressProfile GetStrainStressProfile(
            double epsilonC, double curvature, bool ultimate, int division = 200)
        {
            double r = PileDia * 0.5;
            double epsilon0 = epsilonC - r * curvature;
            MaterialLaw type = ultimate ? MaterialLaw.Bilinear : MaterialLaw.Linear;

            var profile = new SectionStrainStressProfile { Radius = r };
            profile.Materials.Add(BuildSolidProfile(SectionMaterialKind.Concrete, "コンクリート",
                InsituConcrete, type, epsilon0, curvature, r, 0.0, 0.0, division));
            profile.Materials.Add(BuildRingProfile(SectionMaterialKind.MainBar, "主筋",
                MainBars, type, epsilon0, curvature, MainBarPCD * 0.5, 0.0, division));

            profile.CompressionEdgeStrain = epsilon0 + curvature * r;
            profile.TensionEdgeStrain = epsilon0 - curvature * r;
            return profile;
        }

        // N-M グラフは「ひび割れ開始」「引張鉄筋降伏開始」も描くため、その曲線も対象に加える。
        internal override IEnumerable<(string Name, (List<double> N, List<double> M, List<double> Eps, List<double> Phi) Curve, bool Ultimate)> GetProfileSourceCurves()
        {
            foreach (var c in base.GetProfileSourceCurves())
                yield return c;
            yield return ("ひび割れ開始", GetCrackMNInteraction(), false);
            yield return ("引張鉄筋降伏開始", GetSteelYieldMNInteraction(), true);
        }
    }


}
