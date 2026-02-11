using System;
using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    // 場所打ち鋼管コンクリート杭断面クラス
    internal class InsituSteelPipeReinforcedConcreteSection : AbstractPileSection
    {
        public CircularSolidSection CircularSolidSectionConcrete { get; private set; }
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

            CircularSolidSectionConcrete = new CircularSolidSection(PileDia - 2 * PipeT);
            CircularPipeSectionMainbars = new CircularPipeSection(MainBarPCD, MainBarArea / Math.PI / MainBarPCD);
            CircularPipeSectionSteelPipe = new CircularPipeSection(PileDia - PipeT, PipeT);

            double concreteDia = PileDia - PipeT * 2;
            //double PipeCenterDia = PileDia - PipeT;
            PositionCs = [-(PileDia - PipeT * 0.5) * 0.5, -concreteDia * 0.5, -MainBarPCD * 0.5,];
            PositionTs = [(PileDia - PipeT * 0.5) * 0.5, concreteDia * 0.5, MainBarPCD * 0.5,];

            SetZeFtIe();

            // プレストレスひずみ度
            Prestrains = [insituSteelPipe.Prestrain, insituConcrete.Prestrain, mainBars.Prestrain];

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [insituSteelPipe.ServiceLimitStrainC, insituConcrete.ServiceLimitStrainC, mainBars.ServiceLimitStrainC,];
            ServiceLimitStrainTs = [insituSteelPipe.ServiceLimitStrainT, insituConcrete.ServiceLimitStrainT, mainBars.ServiceLimitStrainT,];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [insituSteelPipe.DamageLimitStrainC, insituConcrete.DamageLimitStrainC, mainBars.DamageLimitStrainC,];
            DamageLimitStrainTs = [insituSteelPipe.DamageLimitStrainT, insituConcrete.DamageLimitStrainT, mainBars.DamageLimitStrainT,];

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = [];

            // 使用限界曲げモーメント低減率
            ServiceLimitBeta = [1.0];

            // 損傷限界軸力閾値
            DamageLimitAxialForceThresholds = [];

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = [1.0];

            // 安全限界軸力閾値

            UltimateLimitAxialForceThresholds = [
                -0.2 * (mainBars.RSigmaY * mainBars.Ag + insituSteelPipe.F * insituSteelPipe.AMinus),
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0
            ];

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 1.0, 0.0];

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInteraction();

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = [];

            // 損傷限界閾値
            DamageLimitBendingMomentThresholds = GetDamageLimitBendingMomentThresholds();

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 低減後使用限界NMインタラクション
            FactoredServiceNM = GetFactoredMNInteraction(UnfactoredServiceNM, (ServiceLimitAxialForceThresholds, ServiceLimitBendingMomentThresholds), ServiceLimitBeta);


            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBeta);

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInteraction(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNQ = GetUltimateQNInteraction();

            // 低減前使用限界NMインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction();

            // 低減前損傷限界NMインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction();

            // 低減前安全限界NMインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction();

            //

        }

        private double GetServiceLimitShear()
        {
            double beta1 = 1.0;

            double area = Math.PI * (Math.Pow(InsituSteelPipe.OutDiaMinus, 2) - Math.Pow(InsituSteelPipe.OutDiaMinus - InsituSteelPipe.TMinus, 2)) / 4.0;
            double kappa = 2.0;
            double sfss = InsituSteelPipe.F / 1.5 / Math.Sqrt(3);
            return beta1 * area / kappa * sfss;
        }

        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(int level)
        {
            double beta1 = 1.0;
            double beta2 = 1.0;
            double beta = level == 1 ? beta1 : beta1 * beta2;
            double area = Math.PI * (Math.Pow(InsituSteelPipe.OutDiaMinus, 2) - Math.Pow(InsituSteelPipe.OutDiaMinus - InsituSteelPipe.TMinus, 2)) / 4.0;
            double kappa = 2.0;
            double sfsd = InsituSteelPipe.F / Math.Sqrt(3);
            return beta1 * area / kappa * sfsd;
        }

        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double n)
        {
            double beta1 = 1.0;
            double beta2 = 1.0;
            double ts = InsituSteelPipe.TMinus;
            double d = InsituSteelPipe.OutDiaMinus;
            double area = Math.PI * (Math.Pow(InsituSteelPipe.OutDiaMinus, 2) - Math.PI * Math.Pow(InsituSteelPipe.OutDiaMinus - InsituSteelPipe.TMinus, 2)) / 4.0;
            double fcy = 1.1 * InsituSteelPipe.F;
            double ns;
            if (n >= 0)
            {
                ns = n * fcy * area / (InsituConcrete.Gsi * InsituConcrete.Fc * InsituConcrete.Ac + fcy * area);
            }
            else
            {
                ns = n;
            }
            double p = ns / (fcy * area);

            return beta1 * beta2 * 2.0 / 3.0 * Math.PI * ts * (d - ts) * fcy / Math.Sqrt(3) * Math.Sqrt(1 - p * p);
        }


        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear();
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 損傷限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetDamageLimitQNInteraction(int level = 1, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetDamageLimitShear(level);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetUltimateLimitShear(n);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        // Ze, Ft, Ieのセット
        internal void SetZeFtIe()
        {
            double rc = (PileDia - 2 * InsituSteelPipe.T) / 2.0;
            double Ic = Math.PI * Math.Pow(rc, 4) / 4.0;
            double nr = MainBars.Er / InsituConcrete.Ec;
            double ns = InsituSteelPipe.SE1 / InsituConcrete.Ec;

            Ae = InsituConcrete.Ac + (nr - 1) * MainBars.Ag + ns * InsituSteelPipe.AMinus;
            Ie = Ic + 1.0 / 2.0 * (nr - 1) * MainBars.Ag * Math.Pow(MainBars.PCD / 2.0, 2) + ns * InsituSteelPipe.IMinus;
            Ze = Ie / rc;
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
            double Nnext1;
            double curvature = MainBars.RSigmaY / MainBars.Er / (PileDia / 2.0 + MainBars.PCD / 2);
            double deltaCurvature = curvature / 100.0;
            int maxIter = 50;
            int iter = 0;

            while (Math.Abs(Ntarget - Nnext) > 0.1 && iter < maxIter)
            {
                (Nnext, Mnext) = GetYieldForceAndMoment(curvature);
                (Nnext1, _) = GetYieldForceAndMoment(curvature + deltaCurvature);

                double deltaN = Nnext1 - Nnext;
                if (Math.Abs(deltaN) < 1e-8)
                    break; // 収束不能

                double step = deltaCurvature / deltaN * (Ntarget - Nnext);

                // ステップ幅制限
                if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
                    step = Math.Sign(step) * Math.Abs(curvature) * 0.5;

                curvature += step;
                iter++;
            }
            // 収束しなかった場合の対策
            if (iter >= maxIter)
            {
                // 必要なら例外や警告
                throw new InvalidOperationException("Newton-Raphson法が収束しませんでした。");
            }
            return (Mnext, curvature);
        }

        // C点を返すメソッド
        //internal static double GetPhiC(double phiCr, double Mcr, double phiY, double My, double Mu0, double beta1)
        //{
        //    double phiC = phiCr + (phiY - phiCr) * (beta1 * Mu0 - Mcr) / (My - Mcr);
        //    return phiC;
        //}

        // IPileSectionCalculation インターフェース実装（親クラスのオーバーライド）
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            return GetMPhiRelationshipInternal(axialN, 1.0);
        }

        // ある軸力時のM-φ関係を得るメソッド（内部実装）
        internal (List<double>, List<double>) GetMPhiRelationshipInternal(double Ntarget, double beta1 = 1.0)
        {
            (double MCr, double phiCr) = GetCrackMoment(Ntarget);
            (double MY, double phiY) = GetYieldMoment(Ntarget);
            (double Mu0, double phiU) = GetUltimateMomentForSpecificN(Ntarget);
            //double phiC = GetPhiC(phiCr, MCr, phiY, MY, Mu0, beta1);
            List<double> phis = [0.0, phiCr, phiY, phiU];
            List<double> Ms = [0.0, MCr, MY, beta1 * Mu0];

            return (phis, Ms);
        }

        // 最外縁の鋼管または杭主筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonCReinf = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia * 0.5 + MainBars.PCD * 0.5);
            double epsilonCpipe = -InsituSteelPipe.SEpsilonY + curvature * (PileDia - 1);
            double epsilonC = Math.Min(epsilonCReinf, epsilonCpipe);
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
            double epsilon0 = epsilonC - (PileDia * 0.5 - PipeT) * curvature;

            double N, M;

            var result0 = CircularPipeSectionSteelPipe.GetForceAndMoment("linear", InsituSteelPipe, epsilon0, curvature);
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment("linear", InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment("linear", MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment("linear", InsituConcrete, epsilon0, curvature);

            N = result0.Item1 + result1.Item1 + result2.Item1 - result3.Item1;
            M = result0.Item2 + result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M, epsilonC);
        }

        // 安全限界軸力、曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - (PileDia * 0.5 - PipeT) * curvature;
            double N, M;
            string type = "bilinear";
            var result0 = CircularPipeSectionSteelPipe.GetForceAndMoment(type, InsituSteelPipe, epsilon0, curvature);
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(type, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);

            N = result0.Item1 + result1.Item1 + result2.Item1 - result3.Item1;
            M = result0.Item2 + result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }
    }
}
