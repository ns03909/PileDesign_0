using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{

    /// <summary>
    /// internal abstract class PrecastPileSection : AbstractPileSection
    //  internal class PHCSection : PrecastPileSection
    //  internal class PRCSection : PrecastPileSection
    //  internal class SCSection : PrecastPileSection
    /// </summary>

    // 既製コンクリート杭断面抽象クラス /////////////////////////////////////////
    internal abstract class PrecastPileSection : AbstractPileSection
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
        public double Ae { get; protected set; } // 等価断面積
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

        // 限界モーメント取得メソッド
        internal double GetServiceLimitMoment(double beta, double Sigma0E)
        {
            double Ms1 = Ze * (-Fts + SigmaE + Sigma0E);
            double Ms2 = Ze * (Fcs - SigmaE - Sigma0E);
            return beta * Math.Min(Ms1, Ms2);
        }

        // 安全限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInteraction()
        {
            return GetAllowableMNInteraction(CurvatureMaxUltimateLimit, 2);
        }

        // 安全限界曲げモーメント閾値を返すメソッド
        internal override List<double> GetUltimateLimitBendingMomentThresholds()
        {
            List<double> Ms = [];
            foreach (double Ntarget in UltimateLimitAxialForceThresholds)
            {
                double targetM = GetAllowableMomentForSpecificN(2, Ntarget);
                Ms.Add(targetM);
            }
            return Ms;
        }


    }

    // PHC杭断面クラス
    internal class PHCSection : PrecastPileSection ////////////////////////////////////////////////////////////////////////////////////////////
    {
        public CircularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CircularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectionTendons { get; private set; }
        public CircularPipeSection CircularPipeSectionConcrete { get; private set; }

        // コンストラクタ
        internal PHCSection(PrecastPHCConcrete precastConcrete, Tendons tendons, double prestress)
        {
            // 安全限界軸力閾値
            UltimateLimitAxialForceThresholds = [];

            PrecastConcrete = precastConcrete;
            Tendons = tendons;

            PileDia = precastConcrete.DO;
            Ro = precastConcrete.DO * 0.5;
            Ri = precastConcrete.DI * 0.5;
            Ap = tendons.Ap;
            Ag = 0.0;
            Rp = tendons.PCD * 0.5;
            Rg = 0.0;
            Fc = precastConcrete.Fc;
            SigmaE = prestress;
            SetSectionParameters();

            // プレストレスひずみ度の設定
            SetEpsilonPi(Ac, Ap, 0.0, PrecastConcrete.Ec, Tendons.Ep, 1.0, SigmaE);
            SetEpsilonE(PrecastConcrete.Ec, SigmaE);

            CircularSolidSectionConcreteOut = new CircularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CircularSolidSection(precastConcrete.DI);
            CircularPipeSectionTendons = new CircularPipeSection(tendons.PCD, tendons.Ap / Math.PI / tendons.PCD);

            PositionCs = [-PileDia * 0.5, -Tendons.PCD * 0.5];
            PositionTs = [PileDia * 0.5, Tendons.PCD * 0.5];

            // プレストレスひずみ度
            Prestrains = [PrecastConcrete.Prestrain, Tendons.Prestrain]; // concrete: positive, tendon: negative

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [PrecastConcrete.ServiceLimitStrainC - PrecastConcrete.Prestrain, Tendons.ServiceLimitStrainC - Tendons.Prestrain,];
            ServiceLimitStrainTs = [PrecastConcrete.ServiceLimitStrainT - PrecastConcrete.Prestrain, Tendons.ServiceLimitStrainT - Tendons.Prestrain,];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [PrecastConcrete.DamageLimitStrainC - PrecastConcrete.Prestrain, Tendons.DamageLimitStrainC - Tendons.Prestrain,];
            DamageLimitStrainTs = [PrecastConcrete.DamageLimitStrainT - PrecastConcrete.Prestrain, Tendons.DamageLimitStrainT - Tendons.Prestrain,];

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = [PrecastConcrete.UltimateLimitStrainC - PrecastConcrete.Prestrain, Tendons.UltimateLimitStrainC - Tendons.Prestrain,];
            UltimateLimitStrainTs = [PrecastConcrete.UltimateLimitStrainT - PrecastConcrete.Prestrain, Tendons.UltimateLimitStrainT - Tendons.Prestrain,];

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
            UnfactoredUltimateNM = GetUltimateMNInteraction();


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
            UltimateLimitAxialForceThresholds =
            [
                (4.0 - SigmaE) * Ae,
                (10.0 - SigmaE) * Ae,
                (65.0 - SigmaE) * Ae,
            ];

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 0.80 * 0.75, 0.80 * 0.65, 0.0];

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInteraction(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            // 低減前使用限界NQインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction(3.0, false);

            // 低減前損傷限界NQインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction(3.0, false);

            // 低減前安全限界NQインタラクション
            UnfactoredUltimateNQ = GetUltimateQNInteraction(3.0, false);

            // 低減前使用限界NQインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction(3.0, true);

            // 低減前損傷限界NQインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction(3.0, true);

            // 低減前安全限界NQインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction(3.0, true);
        }

        /// <summary>
        /// 使用限界せん断力を返す。
        /// </summary>
        private double GetServiceLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaS = 1.2;
            double tauS = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaS, 2) - Math.Pow(sigmaG, 2));

            double Qs1 = 0.6 * alpha * 2.0 * t * I / s0 * 2.0 / 3.0 * tauS;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qs2 = 0.6 * alpha * eta1 * 2.0 * t * I / s0 * 2.0 / 3.0 * tauV;
            double Qs = Math.Min(Qs1, Qs2);
            return isFactored ? beta1 * Qs : Qs;
        }
        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double beta2 = 0.65;
            double beta = beta1 * beta2;
            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaD = 1.8;
            double tauD = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaD, 2) - Math.Pow(sigmaG, 2));

            double Qd1 = 0.6 * alpha * 2.0 * t * I / s0 * tauD;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qd2 = 0.6 * alpha * eta1 * 2.0 * t * I / s0 * tauV;
            double Qd = Math.Min(Qd1, Qd2);
            return isFactored ? beta * Qd : Qd;
        }
        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double beta2 = 0.65;

            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaD = 1.8;
            double tauD = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaD, 2) - Math.Pow(sigmaG, 2));

            double Qu1 = 0.75 * alpha * 2.0 * t * I / s0 * tauD;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qu2 = 0.75 * alpha * eta1 * 2.0 * t * I / s0 * tauV;
            double Qu = Math.Min(Qu1, Qu2);
            return isFactored ? beta1 * beta2 * Qu : Qu;
        }


        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear(MonQd, isFactored);
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
            double NMin = 0.0;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetDamageLimitShear(MonQd, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetUltimateLimitShear(MonQd, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
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

        // IPileSectionCalculation インターフェース実装（親クラスのオーバーライド）
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            return GetMPhiRelationshipInternal(axialN, 0.8);
        }

        // ある軸力時のM-φ関係を得るメソッド（内部実装）
        internal (List<double>, List<double>) GetMPhiRelationshipInternal(double Ntarget, double beta1 = 0.8)
        {
            (double MCr, double phiCr) = GetCrackMoment(Ntarget);
            (double MY, double phiY) = GetMomentCurvatureForN(Ntarget, "TendonYield");
            (double MCf, double phiCf) = GetMomentCurvatureForN(Ntarget, "ConcreteCompressiveFailure");
            double beta2 = GetBeta2(Ntarget);

            double phiD;
            if (phiCr < phiY && phiY < phiCf) // a. コンクリートのひび割れの後にPC鋼材の降伏が発生する場合
            {
                double Mu0 = Math.Min(MCf, MY);
                phiD = phiCr + (phiY - phiCr) * (beta1 * Mu0 - MCr) / (MY - MCr);
                double phi_final = phiCr + (phiD - phiCr) / (beta1 * Mu0 - MCr) * (beta1 * beta2 * Mu0 - MCr);

                // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                if (MCr <= 0 || phiCr <= 0)
                {
                    List<double> phis = [0.0, phi_final];
                    List<double> Ms = [0.0, beta1 * beta2 * Mu0];
                    return (phis, Ms);
                }
                else
                {
                    List<double> phis = [0.0, phiCr, phi_final];
                    List<double> Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                    return (phis, Ms);
                }
            }
            else
            {
                (double Mu0, double phiU0) = GetUltimateMomentForSpecificN(Ntarget);

                if (MY < beta1 * Mu0) // b. PC鋼材が引張降伏せずに、コンクリートの曲げひび割れと圧壊が発生する場合
                {
                    phiD = phiCr + (phiU0 - phiCr) * (beta1 * Mu0 - MCr) / (Mu0 - MCr);
                    double phi_final = phiCr + (phiD - phiCr) / (beta1 * Mu0 - MCr) * (beta1 * beta2 * Mu0 - MCr);

                    // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                    if (MCr <= 0 || phiCr <= 0)
                    {
                        List<double> phis = [0.0, phi_final];
                        List<double> Ms = [0.0, beta1 * beta2 * Mu0];
                        return (phis, Ms);
                    }
                    else
                    {
                        List<double> phis = [0.0, phiCr, phi_final];
                        List<double> Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                        return (phis, Ms);
                    }
                }
                else // c. コンクリートの圧壊のみが発生する場合
                {
                    phiD = beta1 * Mu0 / (PrecastConcrete.Ec * Ie);
                    List<double> phis = [0.0, beta2 * phiD];
                    List<double> Ms = [0.0, beta1 * beta2 * Mu0];
                    return (phis, Ms);
                }
            }
        }

        private double GetBeta2(double Ntarget)
        {
            double sigma0e = Ntarget / Ae;
            return (SigmaE + sigma0e) < 10 ? 0.75 : 0.65;
        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget)
        {
            double sigma0e = Ntarget / Ae;
            double Mcr = Ze * (Ftd + SigmaE + sigma0e);
            double phiCr = Mcr / (PrecastConcrete.Ec * Ie);
            return (Mcr, phiCr);
        }

        // 最外縁のPC鋼材が引張降伏するときの曲げモーメント、曲率を返すメソッド
        //internal (double, double) GetMomentCurvatureForN(double Ntarget, string type)
        //{
        //    double Nnext = double.MaxValue;
        //    double Mnext = double.MaxValue;
        //    double Nnext1;
        //    double curvature = Tendons.EpsilonPy / (PileDia * 0.5 + Tendons.PCD * 0.5);
        //    double deltaCurvature = curvature / 100.0;
        //    int maxIter = 50;
        //    int iter = 0;

        //    while (Math.Abs(Ntarget - Nnext) > 0.1 && iter < maxIter)
        //    {
        //        if (type == "TendonYield")
        //        {
        //            (Nnext, Mnext) = GetYieldForceAndMoment(curvature);
        //            (Nnext1, _) = GetYieldForceAndMoment(curvature + deltaCurvature);
        //        }
        //        else // (type == "ConcreteCompressiveFailure")
        //        {
        //            (Nnext, Mnext) = GetCompressiveFailureForceAndMoment(curvature);
        //            (Nnext1, _) = GetCompressiveFailureForceAndMoment(curvature + deltaCurvature);
        //        }

        //        double deltaN = Nnext1 - Nnext;
        //        if (Math.Abs(deltaN) < 1e-8 || double.IsNaN(deltaN))
        //            break; // 収束不能・異常値

        //        double step = deltaCurvature / deltaN * (Ntarget - Nnext);

        //        // ステップ幅制限
        //        if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
        //            step = Math.Sign(step) * Math.Abs(curvature) * 0.5;

        //        curvature += step;

        //        // NaNチェック
        //        if (double.IsNaN(curvature) || double.IsNaN(Nnext) || double.IsNaN(Mnext))
        //            break;

        //        iter++;
        //    }
        //    // 収束しなかった場合の対策
        //    if (iter >= maxIter)
        //    {
        //        // 必要なら例外や警告
        //        throw new InvalidOperationException("Newton-Raphson法が収束しませんでした。");
        //    }
        //    return (Mnext, curvature);
        //}
        internal (double, double) GetMomentCurvatureForN(double Ntarget, string type)
        {
            double Nnext = double.MaxValue;
            double Mnext = double.MaxValue;
            double Nnext1;
            // 初期曲率（0以上に保つ）
            double curvature = Math.Max(1e-12, Tendons.EpsilonPy / (PileDia * 0.5 + Tendons.PCD * 0.5));
            double deltaCurvature = Math.Max(1e-12, curvature / 100.0);
            int maxIter = 200;
            int iter = 0;

            // 最良解を記憶
            double bestDiff = double.MaxValue;
            double bestN = Nnext;
            double bestM = Mnext;
            double bestCurv = curvature;

            for (iter = 0; iter < maxIter; iter++)
            {
                if (type == "TendonYield")
                {
                    (Nnext, Mnext) = GetYieldForceAndMoment(curvature);
                    (Nnext1, _) = GetYieldForceAndMoment(curvature + deltaCurvature);
                }
                else // ConcreteCompressiveFailure
                {
                    (Nnext, Mnext) = GetCompressiveFailureForceAndMoment(curvature);
                    (Nnext1, _) = GetCompressiveFailureForceAndMoment(curvature + deltaCurvature);
                }

                // 非有限値は安全に小さい前進で回避
                if (!double.IsFinite(Nnext) || !double.IsFinite(Mnext) || !double.IsFinite(Nnext1))
                {
                    curvature = Math.Max(curvature * 0.5, 1e-6);
                    deltaCurvature = Math.Max(Math.Abs(curvature) * 1e-4, 1e-12);
                    continue;
                }

                double diff = Math.Abs(Ntarget - Nnext);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestN = Nnext;
                    bestM = Mnext;
                    bestCurv = curvature;
                }

                // 目標に十分近ければ成功とみなす
                if (diff <= 0.1)
                {
                    return (Mnext, curvature);
                }

                double deltaN = Nnext1 - Nnext;

                // deltaN が小さすぎる／NaN の場合はデルタ・ステップを変えてフォールバック
                if (double.IsNaN(deltaN) || Math.Abs(deltaN) < 1e-12)
                {
                    // 少し大きめの差分で再試行する
                    deltaCurvature = Math.Min(Math.Max(deltaCurvature * 10.0, 1e-12), Math.Max(Math.Abs(curvature) * 0.5, 1e-6));

                    // 安全な符号判定（NaN を直接 Math.Sign に渡さない）
                    double d = Ntarget - Nnext;
                    double sign = 1.0;
                    if (double.IsFinite(d) && d != 0.0)
                        sign = Math.Sign(d);
                    else if (double.IsFinite(Ntarget) && double.IsFinite(bestN))
                        sign = Math.Sign(Ntarget - bestN);
                    else
                        sign = 1.0;

                    double fallbackStep = sign * Math.Max(Math.Abs(curvature) * 1e-3, 1e-6);
                    curvature += fallbackStep;
                    curvature = Math.Max(curvature, 0.0);
                    // 発散防止の上限
                    curvature = Math.Min(curvature, 1e-1);
                    continue;
                }

                // Newton風のステップ
                double step = deltaCurvature / deltaN * (Ntarget - Nnext);

                // ステップ幅制限（安全側）
                double maxStep = Math.Max(Math.Abs(curvature) * 0.5, 1e-6);
                if (Math.Abs(step) > maxStep)
                    step = Math.Sign(step) * maxStep;

                curvature += step;

                // 数値が壊れたら安全に抜ける
                if (double.IsNaN(curvature) || double.IsInfinity(curvature))
                    break;

                // 曲率は負にならないようにする（設計上許容されるなら調整）
                curvature = Math.Max(curvature, 0.0);

                // 適応的に deltaCurvature を更新
                deltaCurvature = Math.Max(Math.Abs(curvature) * 1e-4, 1e-12);
            }

            // 収束しなかった場合は例外を投げずに最良近似を返す（デバッグ出力）
            return (bestM, bestCurv);
        }

        // 最外縁のPC鋼材が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonC = -Tendons.EpsilonPy + curvature * (PileDia * 0.5 + Tendons.PCD * 0.5) - Prestrains[1];

            // 最外縁のPC鋼材が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // コンクリートが圧壊するときのN,Mを返すメソッド
        internal (double, double) GetCompressiveFailureForceAndMoment(double curvature)
        {
            double epsilonC = PrecastConcrete.EpsilonCu - Prestrains[0];

            // 最外縁のPC鋼材が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            string type = "linear";
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);
            var result3 = CircularPipeSectionTendons.GetForceAndMoment(type, Tendons, epsilon0 + Prestrains[1], curvature);
            var result4 = CircularPipeSectionTendons.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);

            //var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(true, PrecastConcrete, epsilon0, curvature);
            //var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(true, PrecastConcrete, epsilon0, curvature);
            //var result3 = CircularPipeSectionTendons.GetForceAndMoment(true, Tendons, epsilon0, curvature);
            //var result4 = CircularPipeSectionTendons.GetForceAndMoment(true, PrecastConcrete, epsilon0, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2;
            return (N, M, epsilonC);
        }

        // 使用限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((Fts - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) * 0.5 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(1.0, (Fcs + Fts) * 0.5 - SigmaE));
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
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) * 0.5 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(0.9, 4.0 - SigmaE));
            Ms.Add(GetServiceLimitMoment(0.9, (Fcs + Fts) * 0.5 - SigmaE));
            Ms.Add(GetServiceLimitMoment(0.9, Fcs - SigmaE));
            Ms.Add(0.0);

            for (int i = 0; i < 5; i++)
            {
                epsilonCs.Add(0.0);
                curvatures.Add(0.0);
            }
            return (Ns, Ms, epsilonCs, curvatures);
        }

        // 損傷限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetDamageLimitMNInteraction()
        {
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((Ftd - SigmaE) * Ae);
            Ns.Add(((Fcd + Ftd) * 0.5 - SigmaE) * Ae);
            Ns.Add((Fcd - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetDamageLimitMoment(1.0, (Fcd + Ftd) * 0.5 - SigmaE));
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
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((10.0 - SigmaE) * Ae);
            Ns.Add((10.0 - SigmaE) * Ae);
            Ns.Add(((Fcd + Ftd) * 0.5 - SigmaE) * Ae);
            Ns.Add((35.0 - SigmaE) * Ae);
            Ns.Add((35.0 - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetDamageLimitMoment(0.65, 4.0 - SigmaE));
            Ms.Add(GetDamageLimitMoment(0.65, 10.0 - SigmaE));
            Ms.Add(GetDamageLimitMoment(0.75, 10.0 - SigmaE));
            Ms.Add(GetDamageLimitMoment(0.75, (Fcd + Ftd) * 0.5 - SigmaE));
            Ms.Add(GetDamageLimitMoment(0.75, 35.0 - SigmaE));
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
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            double N, M;
            string type = "bilinear";
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);
            var result3 = CircularPipeSectionTendons.GetForceAndMoment(type, Tendons, epsilon0 + Prestrains[1], curvature);
            var result4 = CircularPipeSectionTendons.GetForceAndMoment(type, PrecastConcrete, epsilon0 + Prestrains[0], curvature);

            //var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(false, PrecastConcrete, epsilon0, curvature);
            //var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(false, PrecastConcrete, epsilon0, curvature);
            //var result3 = CircularPipeSectionTendons.GetForceAndMoment(false, Tendons, epsilon0, curvature);
            //var result4 = CircularPipeSectionTendons.GetForceAndMoment(false, PrecastConcrete, epsilon0, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2;
            return (N, M);


        }
    }

    // PRCSection杭クラス ////////////////////////////////////////////////////////////////////////////////////////////
    internal class PRCSection : PrecastPileSection
    {
        public CircularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CircularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectionTendons { get; private set; }
        public CircularPipeSection CircularPipeSectionMainBars { get; private set; }

        //public PrecastConcrete PrecastConcrete { get; private set; }
        //public Tendons Tendons { get; private set; }
        //public MainBars MainBars { get; private set; }

        // コンストラクタ
        internal PRCSection(PrecastPRCConcrete precastConcrete, MainBars mainBars, Tendons tendons, double prestress)
        {
            // 安全限界軸力閾値
            UltimateLimitAxialForceThresholds = [];

            Console.WriteLine($"MainBars.PCD: {mainBars.PCD}, MainBars.Number: {mainBars.Number}, MainBars.Grade: {mainBars.Grade}, MainBars.BarSize: {mainBars.BarSize}, MainBars.Ag: {mainBars.Ag}");


            PrecastConcrete = precastConcrete;
            MainBars = mainBars;
            Tendons = tendons;

            PileDia = precastConcrete.DO;

            Ro = precastConcrete.DO * 0.5;
            Ri = precastConcrete.DI * 0.5;
            Ap = tendons.Ap;
            Ag = mainBars.Ag;
            Rp = tendons.PCD * 0.5;
            Rg = mainBars.PCD * 0.5;
            Fc = precastConcrete.Fc;
            SigmaE = prestress;
            SetSectionParameters();

            SetEpsilonPi(Ac, Ap, Ag, PrecastConcrete.Ec, Tendons.Ep, MainBars.Er, SigmaE);
            SetEpsilonE(PrecastConcrete.Ec, SigmaE);
            SetEpsilonSi(PrecastConcrete.Ec, SigmaE);

            CircularSolidSectionConcreteOut = new CircularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CircularSolidSection(precastConcrete.DI);
            CircularPipeSectionTendons = new CircularPipeSection(tendons.PCD, tendons.Ap / Math.PI / tendons.PCD);
            CircularPipeSectionMainBars = new CircularPipeSection(mainBars.PCD, mainBars.Ag / Math.PI / mainBars.PCD);

            PositionCs = [-PileDia * 0.5, -MainBars.PCD * 0.5, -Tendons.PCD * 0.5];
            PositionTs = [PileDia * 0.5, MainBars.PCD * 0.5, Tendons.PCD * 0.5];

            // プレストレスひずみ度
            Prestrains = [PrecastConcrete.Prestrain, mainBars.Prestrain, Tendons.Prestrain];

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [PrecastConcrete.ServiceLimitStrainC - PrecastConcrete.Prestrain, mainBars.ServiceLimitStrainC - mainBars.Prestrain, Tendons.ServiceLimitStrainC - Tendons.Prestrain,];
            ServiceLimitStrainTs = [PrecastConcrete.ServiceLimitStrainT - PrecastConcrete.Prestrain, mainBars.ServiceLimitStrainT - mainBars.Prestrain, Tendons.ServiceLimitStrainT - Tendons.Prestrain,];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [PrecastConcrete.DamageLimitStrainC - PrecastConcrete.Prestrain, mainBars.DamageLimitStrainC - mainBars.Prestrain, Tendons.DamageLimitStrainC - Tendons.Prestrain,];
            DamageLimitStrainTs = [PrecastConcrete.DamageLimitStrainT - PrecastConcrete.Prestrain, mainBars.DamageLimitStrainT - mainBars.Prestrain, Tendons.DamageLimitStrainT - Tendons.Prestrain];

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = [PrecastConcrete.UltimateLimitStrainC - PrecastConcrete.Prestrain, mainBars.UltimateLimitStrainC - mainBars.Prestrain, Tendons.UltimateLimitStrainC - Tendons.Prestrain,];
            UltimateLimitStrainTs = [PrecastConcrete.UltimateLimitStrainT - PrecastConcrete.Prestrain, mainBars.UltimateLimitStrainT - mainBars.Prestrain, Tendons.UltimateLimitStrainT - Tendons.Prestrain,];

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
            UnfactoredUltimateNM = GetUltimateMNInteraction();

            // 使用限界最大曲率時の軸力
            AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            // 損傷限界最大曲率時の軸力
            AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            //安全限界最大曲率時の軸力
            AxialForceCurvatureMaxUltimateLimit = GetAllowableForceAndMoment(2, true, CurvatureMaxUltimateLimit).Item1;

            // 低減前使用限界NMインタラクション
            FactoredServiceNM = GetFactoredServiceLimitMNInteraction();

            // 損傷限界軸力閾値
            DamageLimitAxialForceThresholds =
            [
                (4.0 - SigmaE) * Ae,
                (10.0 - SigmaE) * Ae,
                (35.0 - SigmaE) * Ae,
            ];

            // 損傷限界閾値
            DamageLimitBendingMomentThresholds = GetDamageLimitBendingMomentThresholds();

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = [0.0, 0.8 * 0.65, 0.80 * 0.75, 0.0];

            // 低減後損傷限界NMインタラクション
            FactoredDamageNM = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBeta);

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds =
            [
                -0.27 * (MainBars.Ag * MainBars.RSigmaY + Tendons.Ap * Tendons.Fpy),
                (10.0 - SigmaE) * Ae,
                (60.0 - SigmaE) * Ae,
            ];

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 0.80 * 0.75, 0.80 * 0.65, 0.0];

            // 低減後安全限界NMインタラクション
            FactoredUltimateNM = GetFactoredMNInteraction(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);


            // 低減前使用限界NQインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction(3.0, false);

            // 低減前損傷限界NQインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction(3.0, false);

            // 低減前安全限界NQインタラクション
            UnfactoredUltimateNQ = GetUltimateQNInteraction(3.0, false);

            // 低減前使用限界NQインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction(3.0, true);

            // 低減前損傷限界NQインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction(3.0, true);

            // 低減前安全限界NQインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction(3.0, true);
        }

        /// <summary>
        /// 使用限界せん断力を返す。
        /// </summary>
        private double GetServiceLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaS = 1.2;
            double tauS = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaS, 2) - Math.Pow(sigmaG, 2));

            double Qs1 = 0.6 * alpha * 2.0 * t * I / s0 * 2.0 / 3.0 * tauS;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qs2 = 0.6 * alpha * eta1 * 2.0 * t * I / s0 * 2.0 / 3.0 * tauV;
            double Qs = Math.Min(Qs1, Qs2);
            return isFactored ? beta1 * Qs : Qs;
        }
        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double beta2 = 0.65;
            double beta = beta1 * beta2;
            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaD = 1.8;
            double tauD = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaD, 2) - Math.Pow(sigmaG, 2));

            double Qd1 = 0.6 * alpha * 2.0 * t * I / s0 * tauD;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qd2 = 0.6 * alpha * eta1 * 2.0 * t * I / s0 * tauV;
            double Qd = Math.Min(Qd1, Qd2);
            return isFactored ? beta * Qd : Qd;
        }
        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double MonQd, bool isFactored)
        {
            double beta1 = 1.0;
            double beta2 = 0.65;

            double s0 = 2.0 * (Math.Pow(Ro, 3) - Math.Pow(Ri, 3)) / 3.0;
            double t = (Ro - Ri) / 2.0;
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double sigmaG = SigmaE + Sigma0E;
            double sigmaD = 1.8;
            double tauD = 0.5 * Math.Sqrt(Math.Pow(sigmaG + 2.0 * sigmaD, 2) - Math.Pow(sigmaG, 2));

            double Qu1 = 0.75 * alpha * 2.0 * t * I / s0 * tauD;

            double dpc = 15; // PC鋼線の径を15mmとする
            double eta1 = (t - dpc) / t;
            double tauV = 1.9 * Math.Pow(Fc, 0.323);
            double Qu2 = 0.75 * alpha * eta1 * 2.0 * t * I / s0 * tauV;
            double Qu = Math.Min(Qu1, Qu2);
            return isFactored ? beta1 * beta2 * Qu : Qu;
        }


        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear(MonQd, isFactored);
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
            double NMin = 0.0;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetDamageLimitShear(MonQd, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetUltimateLimitShear(MonQd, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
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

        // IPileSectionCalculation インターフェース実装（親クラスのオーバーライド）
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            return GetMPhiRelationshipInternal(axialN, 0.8);
        }

        // ある軸力時のM-φ関係を得るメソッド（内部実装）
        internal (List<double>, List<double>) GetMPhiRelationshipInternal(double Ntarget, double beta1 = 0.8)
        {
            // コンクリートのひび割れモーメント、曲率を取得
            (double MCr, double phiCr) = GetCrackMoment(Ntarget);

            // 軸方向鉄筋が引張降伏する時の曲げモーメント、曲率を取得
            (double MYT, double phiYT) = GetMomentCurvatureForN(Ntarget, "RebarTensionYield");

            // 軸方向鉄筋が圧縮降伏する時の曲げモーメント、曲率を取得
            (double MYC, double phiYC) = GetMomentCurvatureForN(Ntarget, "ReBarCompressionYield");

            // 圧縮縁がコンクリートの圧壊限界ひずみに達する時の曲げモーメント、曲率を取得　
            (double MCu, double phiCu) = GetMomentCurvatureForN(Ntarget, "ConcreteCompressiveFailure");

            // PC鋼材が引張限界ひずみに達するときの曲げモーメント、曲率を取得
            (double MY0, double phiU0) = GetMomentCurvatureForN(Ntarget, "TendonTensileFailure");

            double beta2 = GetBeta2(Ntarget);

            // 安全限界曲げモーメント、曲率を取得
            (double Mu0, double _) = GetUltimateMomentForSpecificN(Ntarget);

            double phiD;
            List<double> phis;
            List<double> Ms;
            //if (phiCr < phiYT) // a コンクリートのひび割れの後に軸方向鉄筋の引張降伏が先行する場合
            //if (phiCr < phiU0 && phiU0 < phiYT && phiU0 < phiYC) // a コンクリートのひび割れの後に軸方向鉄筋の引張降伏が先行する場合
            if (phiCr < phiYT && phiYT < phiYC /*&& phiU0 < phiYC*/) // a コンクリートのひび割れの後に軸方向鉄筋の引張降伏が先行する場合
            {
                phiD = phiCr + (phiYT - phiCr) * (beta1 * Mu0 - MCr) / (MYT - MCr);
                double phi_final = phiCr + (phiD - phiCr) / (beta1 * Mu0 - MCr) * (beta1 * beta2 * Mu0 - MCr);

                // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                if (MCr <= 0 || phiCr <= 0)
                {
                    phis = [0.0, phi_final];
                    Ms = [0.0, beta1 * beta2 * Mu0];
                }
                else
                {
                    phis = [0.0, phiCr, phi_final];
                    Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                }
                return (phis, Ms);
            }
            //else if (phiCr < phiYC && phiYC < phiU0 && phiYC < phiYT) // b コンクリートひび割れ後、軸方向鉄筋の圧縮降伏が先行する場合
            else if (phiCr < phiYC && phiYC < phiYT) // b コンクリートひび割れ後、軸方向鉄筋の圧縮降伏が先行する場合
            {
                phiD = phiCr + (phiYC - phiCr) * (beta1 * Mu0 - MCr) / (MYC - MCr);
                double phi_final = phiCr + (phiD - phiCr) / (beta1 * Mu0 - MCr) * (beta1 * beta2 * Mu0 - MCr);

                if (MCr < beta1 * beta2 * Mu0)
                {
                    // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                    if (MCr <= 0 || phiCr <= 0)
                    {
                        phis = [0.0, phi_final];
                        Ms = [0.0, beta1 * beta2 * Mu0];
                    }
                    else
                    {
                        phis = [0.0, phiCr, phi_final];
                        Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                    }
                }
                else
                {
                    // MCr <= 0 の場合は空の曲線（原点のみ）
                    if (MCr <= 0 || phiCr <= 0)
                    {
                        phis = [0.0];
                        Ms = [0.0];
                    }
                    else
                    {
                        phis = [0.0, phiCr];
                        Ms = [0.0, MCr];
                    }
                }
                return (phis, Ms);
            }
            else // 軸方向鉄筋の圧縮降伏が先行する場合、軸力のみで軸方向鉄筋が圧縮降伏し、コンクリートの圧壊に至る場合
            {
                phiD = beta1 * Mu0 / (PrecastConcrete.Ec * Ie);
                phis = [0.0, /*beta1 * */beta2 * phiD];
                Ms = [0.0, beta1 * beta2 * Mu0];
                return (phis, Ms);
            }
        }

        private double GetBeta2(double nTarget) ///////////////////////////////
        {
            double sigma0e = nTarget / Ae;
            return (SigmaE + sigma0e) < 10 ? 0.75 : 0.65;
        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget)
        {
            double sigma0e = Ntarget / Ae;
            double Mcr = Ze * (Ftd + SigmaE + sigma0e);
            double phiCr = Mcr / (PrecastConcrete.Ec * Ie);
            return (Mcr, phiCr);
        }

        // 最外縁のPC鋼材が引張降伏するときの曲げモーメント、曲率を返すメソッド
        //internal (double, double) GetMomentCurvatureForN(double Ntarget, string type)///////////////////////////
        //{
        //    double Nnext = double.MaxValue;
        //    double Mnext = double.MaxValue;
        //    double Nnext1;
        //    double curvature = MainBars.RSigmaY / MainBars.Er / (PileDia * 0.5 + MainBars.PCD * 0.5);
        //    double deltaCurvature = curvature / 100.0;
        //    int maxIter = 100;
        //    int iter = 0;

        //    while (Math.Abs(Ntarget - Nnext) > 0.1 && iter < maxIter)
        //    {
        //        if (type == "RebarTensionYield")
        //        {
        //            (Nnext, Mnext) = GetMainBarTensionYieldForceAndMoment(curvature);
        //            (Nnext1, _) = GetMainBarTensionYieldForceAndMoment(curvature + deltaCurvature);
        //        }

        //        else if (type == "MainBarCompressionYield")
        //        {
        //            (Nnext, Mnext) = GetMainBarCompressionYieldForceAndMoment(curvature);
        //            (Nnext1, _) = GetMainBarCompressionYieldForceAndMoment(curvature + deltaCurvature);
        //        }
        //        else if (type == "ConcreteCompressiveFailure")
        //        {
        //            (Nnext, Mnext) = GetConcreteCompressiveFailureForceAndMoment(curvature);
        //            (Nnext1, _) = GetConcreteCompressiveFailureForceAndMoment(curvature + deltaCurvature);
        //        }
        //        else // (type == "TendonTensileFailure")
        //        {
        //            (Nnext, Mnext) = GetTendonTensileFailureForceAndMoment(curvature);
        //            (Nnext1, _) = GetTendonTensileFailureForceAndMoment(curvature + deltaCurvature);
        //        }

        //        double deltaN = Nnext1 - Nnext;
        //        if (Math.Abs(deltaN) < 1e-8 || double.IsNaN(deltaN))
        //            break; // 収束不能・異常値

        //        double step = deltaCurvature / deltaN * (Ntarget - Nnext);

        //        // ステップ幅制限
        //        if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
        //            step = Math.Sign(step) * Math.Abs(curvature) * 0.5;

        //        curvature += step;

        //        // NaNチェック
        //        if (double.IsNaN(curvature) || double.IsNaN(Nnext) || double.IsNaN(Mnext))
        //            break;

        //        iter++;
        //    }
        //    // 収束しなかった場合の対策
        //    if (iter >= maxIter)
        //    {
        //        // 必要なら例外や警告
        //        throw new InvalidOperationException("Newton-Raphson法が収束しませんでした。");
        //    }
        //    return (Mnext, curvature);
        //}
        internal (double, double) GetMomentCurvatureForN(double Ntarget, string type)///////////////////////////
        {
            double Nnext = double.MaxValue;
            double Mnext = double.MaxValue;
            double Nnext1;

            // 初期曲率（ゼロ・非有限回避）
            double denom = (PileDia * 0.5 + Math.Max(1e-12, MainBars.PCD * 0.5));
            double cur0 = (MainBars.Er > 0 && denom > 0) ? MainBars.RSigmaY / MainBars.Er / denom : double.NaN;
            double curvature = (double.IsFinite(cur0) && cur0 > 0) ? cur0 : 1e-4; // 安全な初期値
            double deltaCurvature = Math.Max(1e-12, curvature / 100.0);
            int maxIter = 200;

            // 最良解を記憶
            double bestDiff = double.MaxValue;
            double bestN = Nnext;
            double bestM = Mnext;
            double bestCurv = curvature;

            for (int iter = 0; iter < maxIter; iter++)
            {
                // 1) N(φ), M(φ) の評価
                if (type == "RebarTensionYield")
                {
                    (Nnext, Mnext) = GetMainBarTensionYieldForceAndMoment(curvature);
                    (Nnext1, _) = GetMainBarTensionYieldForceAndMoment(curvature + deltaCurvature);
                }
                else if (type == "ReBarCompressionYield")
                {
                    (Nnext, Mnext) = GetMainBarCompressionYieldForceAndMoment(curvature);
                    (Nnext1, _) = GetMainBarCompressionYieldForceAndMoment(curvature + deltaCurvature);
                }
                else if (type == "ConcreteCompressiveFailure")
                {
                    (Nnext, Mnext) = GetConcreteCompressiveFailureForceAndMoment(curvature);
                    (Nnext1, _) = GetConcreteCompressiveFailureForceAndMoment(curvature + deltaCurvature);
                }
                else // "TendonTensileFailure"
                {
                    (Nnext, Mnext) = GetTendonTensileFailureForceAndMoment(curvature);
                    (Nnext1, _) = GetTendonTensileFailureForceAndMoment(curvature + deltaCurvature);
                }

                // 非有限値の早期対処（安全側で小さく前進）
                if (!double.IsFinite(Nnext) || !double.IsFinite(Mnext) || !double.IsFinite(Nnext1))
                {
                    curvature = Math.Max(curvature * 0.5, 1e-6);
                    deltaCurvature = Math.Max(Math.Abs(curvature) * 1e-4, 1e-12);
                    continue;
                }

                double diff = Math.Abs(Ntarget - Nnext);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestN = Nnext;
                    bestM = Mnext;
                    bestCurv = curvature;
                }

                // 目標に十分近い
                if (diff <= 0.1)
                    return (Mnext, curvature);

                double deltaN = Nnext1 - Nnext;

                // 2) 微分が小さい/NaN のときのフォールバック
                if (double.IsNaN(deltaN) || Math.Abs(deltaN) < 1e-12)
                {
                    // 差分を強める
                    deltaCurvature = Math.Min(
                        Math.Max(deltaCurvature * 10.0, 1e-12),
                        Math.Max(Math.Abs(curvature) * 0.5, 1e-6));

                    // 安全側の前進。符号取得に NaN を使わない
                    double sign = 1.0;
                    double d = Ntarget - Nnext;
                    if (double.IsFinite(d) && d != 0.0) sign = Math.Sign(d);

                    double fallbackStep = sign * Math.Max(Math.Abs(curvature) * 1e-3, 1e-6);
                    curvature = Math.Max(curvature + fallbackStep, 0.0);
                    // 上限（発散防止）
                    curvature = Math.Min(curvature, 1e-1);
                    continue;
                }

                // 3) Newton 風ステップ
                double step = deltaCurvature / deltaN * (Ntarget - Nnext);

                // ステップ幅制限（安全側）
                double maxStep = Math.Max(Math.Abs(curvature) * 0.5, 1e-6);
                if (!double.IsFinite(step) || Math.Abs(step) > maxStep)
                    step = Math.Sign(step) * maxStep;

                curvature += step;

                // 数値破綻検出
                if (!double.IsFinite(curvature))
                    break;

                // 曲率は負にしない
                curvature = Math.Max(curvature, 0.0);
                // 上限（過大曲率の暴走抑制）
                curvature = Math.Min(curvature, 1e-1);

                // 適応的に deltaCurvature を更新
                deltaCurvature = Math.Max(Math.Abs(curvature) * 1e-4, 1e-12);
            }

            // 収束しなかった場合は最良近似を返す
            return (bestM, bestCurv);
        }

        // 鉄筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetMainBarTensionYieldForceAndMoment(double curvature) /////////////////////
        {
            double epsilonC = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia * 0.5 + MainBars.PCD * 0.5) - MainBars.Prestrain;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 鉄筋が圧縮降伏するときのN,Mを返すメソッド
        internal (double, double) GetMainBarCompressionYieldForceAndMoment(double curvature)
        {
            double epsilonC = MainBars.RSigmaY / MainBars.Er + curvature * (PileDia * 0.5 - MainBars.PCD * 0.5) - MainBars.Prestrain;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // コンクリートが圧壊するときのN,Mを返すメソッド
        internal (double, double) GetConcreteCompressiveFailureForceAndMoment(double curvature)
        {
            double epsilonC = PrecastConcrete.EpsilonCu - PrecastConcrete.Prestrain;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // PC鋼材が引張強さに達するするときのN,Mを返すメソッド
        internal (double, double) GetTendonTensileFailureForceAndMoment(double curvature)
        {
            double epsilonC = -Tendons.EpsilonPu + curvature * (PileDia * 0.5 + Tendons.PCD * 0.5) - Tendons.Prestrain;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 使用限界MNインタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((Fts - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) * 0.5 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(1.0, (Fcs + Fts) * 0.5 - SigmaE));
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
            List<double> Ns = [];
            List<double> Ms = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add((4.0 - SigmaE) * Ae);
            Ns.Add(((Fcs + Fts) * 0.5 - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);
            Ns.Add((Fcs - SigmaE) * Ae);

            Ms.Add(0.0);
            Ms.Add(GetServiceLimitMoment(0.8, 4.0 - SigmaE));
            Ms.Add(GetServiceLimitMoment(0.8, (Fcs + Fts) * 0.5 - SigmaE));
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
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            string type = "linear";
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result3 = CircularPipeSectionMainBars.GetForceAndMoment(type, MainBars, epsilon0 + MainBars.Prestrain, curvature);
            var result4 = CircularPipeSectionMainBars.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result5 = CircularPipeSectionTendons.GetForceAndMoment(type, Tendons, epsilon0 + Tendons.Prestrain, curvature);
            var result6 = CircularPipeSectionTendons.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1 + result5.Item1 - result6.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2 + result5.Item2 - result6.Item2;
            return (N, M, epsilonC);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            string type = "bilinear";
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result3 = CircularPipeSectionMainBars.GetForceAndMoment(type, MainBars, epsilon0 + MainBars.Prestrain, curvature);
            var result4 = CircularPipeSectionMainBars.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);
            var result5 = CircularPipeSectionTendons.GetForceAndMoment(type, Tendons, epsilon0 + Tendons.Prestrain, curvature);
            var result6 = CircularPipeSectionTendons.GetForceAndMoment(type, PrecastConcrete, epsilon0 + PrecastConcrete.Prestrain, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1 - result4.Item1 + result5.Item1 - result6.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2 - result4.Item2 + result5.Item2 - result6.Item2;
            return (N, M);
        }
    }

    // SCSection杭クラス
    internal class SCSection : PrecastPileSection
    {
        public CircularSolidSection CircularSolidSectionConcreteOut { get; private set; }
        public CircularSolidSection CircularSolidSectionConcreteIn { get; private set; }
        public CircularPipeSection CircularPipeSectionSteelPipe { get; private set; }
        //public double Tc { get; private set; }
        public double Asp { get; private set; }

        // コンストラクタ
        internal SCSection(PrecastSCConcrete precastConcrete, PrecastSteelPipe precastPipe) ///////////////////////
        {
            // 安全限界軸力閾値
            UltimateLimitAxialForceThresholds = [];

            PrecastConcrete = precastConcrete;
            PrecastSteelPipe = precastPipe;

            PileDia = precastPipe.OutDia;
            if (PileDia <= 0)
                throw new InvalidOperationException("SCSection: PileDia が 0 以下です。入力値(鋼管径/コンクリート径)を確認してください。");

            // ガード：鋼管厚がゼロの場合は SC 部分としての初期化をスキップして早期リターン

            if (PrecastSteelPipe == null || PrecastSteelPipe.T == 0.0)
            {
                // 最低限の値を設定して不整合での例外を減らす
                Ro = precastConcrete.DO * 0.5; // コンクリート外半径
                Ri = precastConcrete.DI * 0.5; // コンクリート内半径
                T = Ro - Ri;　// コンクリート肉厚
                Ac = Ro * Ro * Math.PI - Ri * Ri * Math.PI; // コンクリート断面積
                Fc = precastConcrete?.Fc ?? 0.0;

                // 最低限の断面オブジェクトは作っておく（他コードの呼び出しに備える）
                CircularSolidSectionConcreteOut = new CircularSolidSection(precastConcrete?.DO ?? 0.0);
                CircularSolidSectionConcreteIn = new CircularSolidSection(precastConcrete?.DI ?? 0.0);
                CircularPipeSectionSteelPipe = null;

                Console.WriteLine("SCSection: PrecastSteelPipe.T is zero or PrecastSteelPipe is null. SCSection initialization skipped.");
                return;
            }

            // SCSection コンストラクタ内の該当部分
            Ro = precastConcrete.DO * 0.5; // コンクリート外半径
            Ri = precastConcrete.DI * 0.5; // コンクリート内半径
            T = Ro - Ri;                  // コンクリート肉厚
            Ac = Math.PI * (Ro * Ro - Ri * Ri); // コンクリート断面積
            Fc = precastConcrete.Fc;

            // 先に基底の基礎量（Fts/Fcs/Ftd/Fcd など）を設定
            SetSectionParameters();

            // 鋼管寄与を組み込む（n: 換算係数）
            double n = 5.0;
            Asp = PrecastSteelPipe.As; // プロパティに代入
            double Rspo = PrecastSteelPipe.OutDia * 0.5;
            double Rspi = Rspo - PrecastSteelPipe.T;

            // 等価断面積（鋼管寄与込み）
            Ae = Ac + n * Asp;

            // 断面二次モーメント（コンクリート + 換算鋼管）
            I = Math.PI * (Math.Pow(Ro, 4) - Math.Pow(Ri, 4)) / 4.0;
            double Isp = Math.PI * (Math.Pow(Rspo, 4) - Math.Pow(Rspi, 4)) / 4.0;
            Ie = I + n * Isp;
            Ze = Ie / Ro;

            // 鋼管の中心径（板厚中央）と断面モデル
            double pipeCenterDia = PrecastSteelPipe.OutDia - PrecastSteelPipe.T; // 中央径
            double pipeT = PrecastSteelPipe.T;

            CircularSolidSectionConcreteOut = new CircularSolidSection(precastConcrete.DO);
            CircularSolidSectionConcreteIn = new CircularSolidSection(precastConcrete.DI);
            CircularPipeSectionSteelPipe = new CircularPipeSection(pipeCenterDia, pipeT);

            PositionCs = [-Ro, -pipeCenterDia * 0.5,];
            PositionTs = [Ro, pipeCenterDia * 0.5,];

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [PrecastConcrete.ServiceLimitStrainC, PrecastSteelPipe.ServiceLimitStrainC];
            ServiceLimitStrainTs = [PrecastConcrete.ServiceLimitStrainT, PrecastSteelPipe.ServiceLimitStrainT];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [PrecastConcrete.DamageLimitStrainC, PrecastSteelPipe.DamageLimitStrainC];
            DamageLimitStrainTs = [PrecastConcrete.DamageLimitStrainT, PrecastSteelPipe.DamageLimitStrainT];

            // 安全限界状態ひずみ度
            UltimateLimitStrainCs = [PrecastConcrete.UltimateLimitStrainC, PrecastSteelPipe.UltimateLimitStrainC];
            UltimateLimitStrainTs = [PrecastConcrete.UltimateLimitStrainT, PrecastSteelPipe.UltimateLimitStrainT];

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 安全限界最大曲率 ///////////////////kakunin//////////////////////////
            CurvatureMaxUltimateLimit = GetAllowableMaxCurvature(UltimateLimitStrainCs, PositionCs, UltimateLimitStrainTs, PositionTs);

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInteraction();

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
            UltimateLimitAxialForceThresholds =
            [
                -0.4 * PrecastSteelPipe.F * 1.1 * PrecastSteelPipe.As,
                1.0 * PrecastSteelPipe.F * 1.1 * PrecastSteelPipe.As + Ac * Fc,
            ];

            //// 安全限界閾値
            //UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            //// 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 1.0, 0.0];

            //// 低減後安全限界NMインタラクション
            //FactoredUltimateNM = GetFactoredMNInterection(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

            // 低減前使用限界NQインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction(3.0, false);

            // 低減前損傷限界NQインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction(3.0, false);

            // 低減前安全限界NQインタラクション
            UnfactoredUltimateNQ = GetUltimateQNInteraction(3.0, false);

            // 低減前使用限界NQインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction(3.0, true);

            // 低減前損傷限界NQインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction(3.0, true);

            // 低減前安全限界NQインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction(3.0, true);
        }

        /// <summary>
        /// 使用限界せん断力を返す。
        /// </summary>
        private double GetServiceLimitShear(bool isFactored)
        {
            double beta1 = 1.0;
            double kappaS = 2.0;
            double fs = PrecastSteelPipe.F / (1.5 * Math.Sqrt(3));
            double As = PrecastSteelPipe.As;

            double unfactoredQs = beta1 / kappaS * fs * As;
            return isFactored ? beta1 * unfactoredQs : unfactoredQs;
        }
        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(bool isFactored)
        {
            double beta1 = 1.0;
            double kappaS = 2.0;
            double fd = PrecastSteelPipe.F / (Math.Sqrt(3));
            double As = PrecastSteelPipe.As;

            double unfactoredQd = beta1 / kappaS * fd * As;
            return isFactored ? beta1 * unfactoredQd : unfactoredQd;
        }
        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double nud, bool isFactored)
        {
            // 鋼管杭の安全限界せん断
            double beta1 = 1.0;
            double beta2 = 1.0;
            double sSigmaY = PrecastSteelPipe.F;         // N/mm²
            double sNy = sSigmaY * PrecastSteelPipe.As;  // N（降伏軸力）

            // sNy が 0 の場合は計算不能
            if (Math.Abs(sNy) < 1e-10)
                return 0.0;

            double eta = nud / sNy;  // 軸力比 η = N / Ny

            // η >= 1 の場合、sqrt(1 - η²) が虚数になるため、0 を返す
            // （軸力が降伏軸力以上のとき、せん断耐力は 0）
            if (Math.Abs(eta) >= 1.0)
                return 0.0;

            double t = PrecastSteelPipe.T;
            double D = PrecastSteelPipe.OutDia;

            double sQ0 = 2 * t * (D - t) * sSigmaY / Math.Sqrt(3);  // N
            double unfactoredQu = sQ0 * Math.Sqrt(1 - eta * eta);

            return isFactored ? beta1 * beta2 * unfactoredQu : unfactoredQu;
        }

        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear(isFactored);
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
            double NMin = 0.0;
            double NMax = 45 * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetDamageLimitShear(isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = 4 * Ae; // N
            double NMax = 45 * Ae; // N
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetUltimateLimitShear(n, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        // IPileSectionCalculation インターフェース実装（親クラスのオーバーライド）
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            return GetMPhiRelationshipInternal(axialN, 0.9);
        }

        // ある軸力時のM-φ関係を得るメソッド（内部実装）
        internal (List<double>, List<double>) GetMPhiRelationshipInternal(double Ntarget, double beta1 = 0.9)
        {
            (double MCr, double phiCr) = GetCrackMoment(Ntarget);
            (double MYT, double phiYT) = GetMomentCurvatureForN(Ntarget, "SteelPipeTensionYield");
            (double MYC, double phiYC) = GetMomentCurvatureForN(Ntarget, "SteelPipeCompressionYield_b");
            (double Mu0, double _) = GetUltimateMomentForSpecificN(Ntarget);

            // デバッグ: 特性値を出力
            string caseType = "";
            double phiD;
            List<double> phis;
            List<double> Ms;

            if (phiCr < phiYT && phiYT < phiYC) // a 鋼管が引張降伏する場合
            {
                caseType = "a";
                // ゼロ除算防止
                double denom = MYT - MCr;
                if (Math.Abs(denom) < 1e-6) denom = Math.Sign(denom) * 1e-6;
                phiD = phiCr + (phiYT - phiCr) * (beta1 * Mu0 - MCr) / denom;

                // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                if (MCr <= 0 || phiCr <= 0)
                {
                    phis = [0.0, phiYT, phiD];
                    Ms = [0.0, MYT, beta1 * Mu0];
                }
                else
                {
                    phis = [0.0, phiCr, phiYT, phiD];
                    Ms = [0.0, MCr, MYT, beta1 * Mu0];
                }
            }
            else if (phiCr < phiYC && phiYC < phiYT) // b 曲げひび割れ後、鋼管が圧縮降伏する場合
            {
                caseType = "b";
                double denom = MYC - MCr;
                if (Math.Abs(denom) < 1e-6) denom = Math.Sign(denom) * 1e-6;
                phiD = phiCr + (phiYC - phiCr) * (beta1 * Mu0 - MCr) / denom;
                double beta2 = 0.75;
                if (beta1 * beta2 * Mu0 < MYT)
                {
                    double denom2 = MYT - MCr;
                    if (Math.Abs(denom2) < 1e-6) denom2 = Math.Sign(denom2) * 1e-6;
                    double phi2 = phiCr + (phiYT - phiCr) / denom2 * (beta1 * beta2 * Mu0 - MCr);

                    // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                    if (MCr <= 0 || phiCr <= 0)
                    {
                        phis = [0.0, phi2];
                        Ms = [0.0, beta1 * beta2 * Mu0];
                    }
                    else
                    {
                        phis = [0.0, phiCr, phi2];
                        Ms = [0.0, MCr, beta1 * beta2 * Mu0];
                    }
                }
                else
                {
                    double denom3 = beta1 * Mu0 - MYT;
                    if (Math.Abs(denom3) < 1e-6) denom3 = Math.Sign(denom3) * 1e-6;
                    double phi3 = phiYT + (phiD - phiYT) / denom3 * (beta1 * beta2 * Mu0 - MYT);

                    // MCr <= 0 の場合は (phiCr, MCr) をスキップ
                    if (MCr <= 0 || phiCr <= 0)
                    {
                        phis = [0.0, phiYT, phi3];
                        Ms = [0.0, MYT, beta1 * beta2 * Mu0];
                    }
                    else
                    {
                        phis = [0.0, phiCr, phiYT, phi3];
                        Ms = [0.0, MCr, MYT, beta1 * beta2 * Mu0];
                    }
                }
            }
            else if (phiYC < phiCr && phiYC < phiYT) // c 鋼管が圧縮降伏する場合
            {
                caseType = "c";
                double ratioYC = (phiYC > 1e-12) ? MYC / phiYC : 1e6;
                phiD = beta1 * Mu0 / ratioYC;
                double beta2 = 0.75;
                if (beta1 * beta2 * Mu0 < MYC)
                {
                    // 元のポリリニア形状を維持（2点の線形曲線）
                    double phi1 = (MYC > 1e-6) ? phiYC / MYC * beta1 * beta2 * Mu0 : phiYC * 0.5;
                    phis = [0.0, phi1];
                    Ms = [0.0, beta1 * beta2 * Mu0];
                }
                else
                {
                    double denom4 = beta1 * Mu0 - MYC;
                    if (Math.Abs(denom4) < 1e-6) denom4 = Math.Sign(denom4) * 1e-6;
                    double phi2 = phiYC + (phiD - phiYC) / denom4 * (beta1 * beta2 * Mu0 - MYC);
                    phis = [0.0, phiYC, phi2];
                    Ms = [0.0, MYC, beta1 * beta2 * Mu0];
                }
            }
            else // d 軸力のみで鋼管が降伏する場合
            {
                caseType = "d";
                double EcIe = PrecastConcrete.Ec * Ie;
                if (EcIe < 1e-6) EcIe = 1e6; // 最小剛性
                phiD = beta1 * Mu0 / EcIe;
                double beta2 = 0.75;
                // 元のポリリニア形状を維持（2点の線形曲線）
                phis = [0.0, beta1 * beta2 * phiD];
                Ms = [0.0, beta1 * beta2 * Mu0];
            }

            // 曲率が単調増加するように補正
            for (int i = 1; i < phis.Count; i++)
            {
                if (phis[i] <= phis[i - 1])
                {
                    phis[i] = phis[i - 1] + 1e-9;
                }
            }

            return (phis, Ms);
        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget)
        {
            double sigma0 = Ntarget / Ae;
            double Mcr = Ze * (Ftd + sigma0);
            double phiCr = Mcr / PrecastConcrete.Ec / Ie;
            return (Mcr, phiCr);
        }

        internal (double, double) GetMomentCurvatureForN(double Ntarget, string type)
        {
            // 1. 評価用ローカル関数（N(φ), M(φ) を返す）
            (double N, double M) Eval(double curvature)
            {
                return type switch
                {
                    "SteelPipeTensionYield" => GetSteelPipeTensionYieldYieldForceAndMoment(curvature),
                    "SteelPipeCompressionYield_b" => GetSteelPipeCompressionYield_b_YieldForceAndMoment(curvature),
                    "SteelPipeCompressionYield_c" => GetSteelPipeCompressionYield_c_YieldForceAndMoment(curvature),
                    "SteelPipeCompressionYield_d" => GetSteelPipeCompressionYield_d_YieldForceAndMoment(curvature),
                    _ => GetSteelPipeTensionYieldYieldForceAndMoment(curvature)
                };
            }

            // 2. 曲率探索範囲設定
            double phiMaxTheoretical = (PrecastSteelPipe.EpsilonY * 3.0) / Math.Max(1e-9, (PileDia * 0.5)); // 少し余裕
            double phiMax = Math.Clamp(phiMaxTheoretical, 1e-6, 0.2); // 上限安全側
            double phiMin = 0.0;

            // 3. 事前スキャンでブラケット探索
            const int scanDiv = 60;
            double bestDiff = double.MaxValue;
            double bestPhi = 0.0;
            double bestM = 0.0;

            double prevPhi = phiMin;
            var (prevN, prevM) = Eval(prevPhi);
            double prevF = prevN - Ntarget;

            if (Math.Abs(prevF) < 0.1)
                return (prevM, prevPhi);

            for (int i = 1; i <= scanDiv; i++)
            {
                double phi = phiMin + (phiMax - phiMin) * i / scanDiv;
                var (Ni, Mi) = Eval(phi);
                double fi = Ni - Ntarget;

                double diff = Math.Abs(fi);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestPhi = phi;
                    bestM = Mi;
                    if (bestDiff < 0.1) // 既に十分
                        return (bestM, bestPhi);
                }

                // 符号変化で bracket 確保
                if (Math.Sign(fi) != Math.Sign(prevF))
                {
                    phiMin = prevPhi;
                    phiMax = phi;
                    prevN = Ni; prevM = Mi;
                    prevF = fi;
                    break;
                }

                prevPhi = phi;
                prevF = fi;
            }

            // 符号変化を見つけられなかった場合 → 最良近似を返す
            if (Math.Abs(phiMax - phiMin) < 1e-12 || phiMin == 0.0 && phiMax == (PrecastSteelPipe.EpsilonY * 3.0) / (PileDia * 0.5))
            {
                return (bestM, bestPhi);
            }

            // 4. Bracket 内で収束（Hybrid: 二分法 + セカント）
            var (Nlow, Mlow) = Eval(phiMin);
            var (Nhigh, Mhigh) = Eval(phiMax);
            double Flow = Nlow - Ntarget;
            double Fhigh = Nhigh - Ntarget;

            const int maxIter = 80;
            const double tolF = 0.1;
            const double tolPhiRel = 1e-6;

            double phiLow = phiMin;
            double phiHigh = phiMax;
            double bestBracketDiff = bestDiff;

            for (int iter = 0; iter < maxIter; iter++)
            {
                // セカント候補
                double phiSecant;
                if (Math.Abs(Fhigh - Flow) > 1e-14)
                {
                    phiSecant = phiHigh - Fhigh * (phiHigh - phiLow) / (Fhigh - Flow);
                    // bracket外に飛び出したら二分法
                    if (phiSecant <= phiLow || phiSecant >= phiHigh || !double.IsFinite(phiSecant))
                        phiSecant = 0.5 * (phiLow + phiHigh);
                }
                else
                {
                    phiSecant = 0.5 * (phiLow + phiHigh);
                }

                var (Nmid, Mmid) = Eval(phiSecant);
                double Fmid = Nmid - Ntarget;
                double absFmid = Math.Abs(Fmid);

                if (absFmid < bestBracketDiff)
                {
                    bestBracketDiff = absFmid;
                    bestPhi = phiSecant;
                    bestM = Mmid;
                }

                if (absFmid < tolF)
                    return (Mmid, phiSecant);

                // 更新（符号で領域縮小）
                if (Math.Sign(Fmid) == Math.Sign(Flow))
                {
                    phiLow = phiSecant;
                    Flow = Fmid;
                }
                else
                {
                    phiHigh = phiSecant;
                    Fhigh = Fmid;
                }

                // 相対幅収束
                if (Math.Abs(phiHigh - phiLow) < tolPhiRel * Math.Max(1.0, phiSecant))
                    break;
            }

            return (bestM, bestPhi);
        }

        // a.鋼管が引張降伏する場合
        internal (double, double) GetSteelPipeTensionYieldYieldForceAndMoment(double curvature) /////////////////////
        {
            double epsilonC = -PrecastSteelPipe.EpsilonY + curvature * PileDia;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // b.鋼管が圧縮降伏する場合
        internal (double, double) GetSteelPipeCompressionYield_b_YieldForceAndMoment(double curvature) /////////////////////
        {
            double epsilonC = PrecastSteelPipe.EpsilonY;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // c.鋼管が圧縮降伏する場合
        internal (double, double) GetSteelPipeCompressionYield_c_YieldForceAndMoment(double curvature) /////////////////////
        {
            double epsilonC = PrecastSteelPipe.EpsilonY;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // d.鋼管が圧縮降伏する場合
        internal (double, double) GetSteelPipeCompressionYield_d_YieldForceAndMoment(double curvature) /////////////////////
        {
            double epsilonC = PrecastSteelPipe.EpsilonY;

            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);
            double epsilon0 = epsilonC - (PileDia * 0.5 - T) * curvature;
            string type = "linear";
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0, curvature);
            var result3 = CircularPipeSectionSteelPipe.GetForceAndMoment(type, PrecastSteelPipe, epsilon0, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2;
            return (N, M, epsilonC);
        }

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            string type = "linear";
            double N, M;
            var result1 = CircularSolidSectionConcreteOut.GetForceAndMoment(type, PrecastConcrete, epsilon0, curvature);
            var result2 = CircularSolidSectionConcreteIn.GetForceAndMoment(type, PrecastConcrete, epsilon0, curvature);
            var result3 = CircularPipeSectionSteelPipe.GetForceAndMoment(type, PrecastSteelPipe, epsilon0, curvature);

            N = result1.Item1 - result2.Item1 + result3.Item1;
            M = result1.Item2 - result2.Item2 + result3.Item2;
            return (N, M);
        }

        // 安全限界MN インタラクション取得メソッド
        internal override (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInteraction()
        {
            List<double> axialForces = [];
            List<double> bendingMoments = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            double maxCurvature = (0.003 + 0.0025) * 20.0 / Math.Max(1e-6, PileDia);
            double maxEpsilonCu = 0.003; // 圧縮縁最大ひずみ固定上限（純圧縮用）

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                double curvature;
                double epsilonC;          // 圧縮縁ひずみ（鋼管側）
                double epsilonCu;         // コンクリート限界（内縁? 想定）
                double N = 0.0;
                double M = 0.0;

                // 1) 曲率設定
                if (i == 0) // 純引張
                {
                    curvature = 0.0;
                    // ここでは鋼管引張側が降伏に近づく状況を想定し εC を鋼管引張側降伏で代表
                    epsilonC = -PrecastSteelPipe.EpsilonY;
                    epsilonCu = 0.003; // ダミー（使わない）
                }
                else if (i == DivisionNum * 2) // 純圧縮
                {
                    curvature = 0.0;
                    epsilonC = maxEpsilonCu;
                    epsilonCu = maxEpsilonCu; // ここでは εC=εcu
                }
                else
                {
                    double ratio = (double)(DivisionNum * 2 - i) / (DivisionNum * 2);
                    curvature = maxCurvature * ratio;

                    // 初期推定値
                    epsilonCu = 0.003;
                    // 初期 εC（鋼管圧縮縁）: コンクリート εcu + ϕ * t
                    epsilonC = epsilonCu + curvature * PrecastSteelPipe.T;

                    // 2) 固定点反復で εcu 更新
                    const int maxIter = 30;
                    const double tol = 0.00001;
                    double lastEps = epsilonCu;
                    bool converged = false;

                    for (int iter = 0; iter < maxIter; iter++)
                    {
                        // 現在 εC で N,M 計算
                        (double Ntmp, double Mtmp) = GetUltimateForceAndMoment(epsilonC, curvature);
                        N = Ntmp; M = Mtmp;

                        double N0 = (N > 0)
                            ? Math.Abs(PrecastSteelPipe.As * PrecastSteelPipe.Fys + Ac * Fc)
                            : PrecastSteelPipe.As * PrecastSteelPipe.Fys;

                        // 次の εcu 推定式 (元コードの式をクランプ)////////////////////////////
                        //double candidate = 0.006 - 0.010 * N / Math.Max(1e-12, N0);
                        //candidate = Math.Clamp(candidate, 0.003, 0.004);
                        double candidate = 0.003;

                        // 収束判定
                        if (Math.Abs(candidate - lastEps) < tol)
                        {
                            epsilonCu = candidate;
                            converged = true;
                            break;
                        }

                        // 更新・再計算用 εC 再構築
                        lastEps = candidate;
                        epsilonCu = candidate;
                        epsilonC = epsilonCu + curvature * PrecastSteelPipe.T;

                        if (!double.IsFinite(epsilonC))
                        {
                            Console.WriteLine("Warning: epsilonC 非有限値 (SCSection.GetUltimateMNInterection). 反復中断。");
                            break;
                        }
                    }

                    if (!converged)
                    {
                        // 最終反復での値を採用（N,M は最後の計算結果）
                        // 必要ならログ
                        // Console.WriteLine("Info: εcu 収束未達 (SCSection).");
                    }
                }

                // 必要なら最終 N,M 再計算（純引張・純圧縮ケース）
                if (i == 0 || i == DivisionNum * 2)
                {
                    (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
                }

                axialForces.Add(N);
                bendingMoments.Add(M);
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }

            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }
        //internal override (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInterection()
        //{
        //    List<double> axialForces = [];
        //    List<double> bendingMoments = [];
        //    List<double> epsilonCs = [];
        //    List<double> curvatures = [];
        //    double epsilonC = 0.0;
        //    double curvature = 0.0;
        //    double maxCurvature = (0.003 + 0.0025) * 20.0 / PileDia;
        //    if (!double.IsFinite(maxCurvature) || maxCurvature <= 0)
        //    {
        //        // フォールバック：外径で計算し直し
        //        maxCurvature = (0.003 + 0.0025) * 20.0 / Math.Max(1e-6, PileDia);
        //    }
        //    double maxEpsilonCu = 0.003;
        //    double epsilonCu; // コンクリートの限界ひずみ
        //    double epsilonCu_next;
        //    double N0;
        //    double N = 0.0;
        //    double M = 0.0;

        //    epsilonCu_next = 0.004;
        //    for (int i = 0; i <= DivisionNum * 2; i++)
        //    {
        //        epsilonCu = double.MaxValue;

        //        //while (epsilonCu_next - epsilonCu > 0.000001) //0.00001) epsilonCu_nextがepsilonCuよりも大きい場合ループ
        //        while (epsilonCu_next < epsilonCu)
        //            while (epsilonCu_next != epsilonCu)
        //            {
        //                epsilonCu = epsilonCu_next;
        //                if (i == 0) // φ=0、純引張
        //                {
        //                    //epsilonCu = maxEpsilonCu;
        //                    epsilonC = -PrecastSteelPipe.EpsilonY;
        //                    curvature = 0.0;
        //                }
        //                else if (i != DivisionNum * 2)
        //                {
        //                    //epsilonCu = maxEpsilonCu;
        //                    curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
        //                    epsilonC = epsilonCu + curvature * PrecastSteelPipe.T; // 鋼管の圧縮縁ひずみ
        //                }
        //                else // 純圧縮
        //                {
        //                    //epsilonCu = maxEpsilonCu;
        //                    epsilonC = maxEpsilonCu;
        //                    curvature = 0.0;
        //                }

        //                var result = GetUltimateForceAndMoment(epsilonC, curvature); // 引張側 純引張～
        //                N = result.Item1;
        //                M = result.Item2;

        //                if (N > 0) // 圧縮時
        //                {
        //                    N0 = Math.Abs(PrecastSteelPipe.As * PrecastSteelPipe.Fys + Ac * Fc);
        //                }
        //                else // (N <= 0)　引張時
        //                {
        //                    N0 = PrecastSteelPipe.As * PrecastSteelPipe.Fys;
        //                }
        //                if ((PileDia - 2 * PrecastSteelPipe.T) / T <= 6)
        //                {
        //                    epsilonCu_next = Math.Max(0.003, Math.Min(0.004, 0.006 - 0.010 * N / N0));
        //                    if (0.003 < epsilonCu_next && epsilonCu_next < 0.004)
        //                    { i++; }
        //                }
        //                else // ((PileDia - 2 * PrecastSteelPipe.T) / Tc <= 6)
        //                {
        //                    epsilonCu_next = epsilonCu;
        //                }
        //            }

        //        axialForces.Add(N); //  * Math.Pow(10, -3));
        //        bendingMoments.Add(M); // * Math.Pow(10, -6));
        //        epsilonCs.Add(epsilonC);
        //        curvatures.Add(curvature);
        //    }
        //    return (axialForces, bendingMoments, epsilonCs, curvatures);
        //}
    }
}
