using System;
using System.Collections.Generic;
using System.Linq;

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

        protected override double CompressionEdgePosition => -(PileDia / 2 - PipeT);

        public double Ae { get; private set; }      // 換算断面積 = Ac + (nr-1)·Ag + ns·AMinus（軸力・曲げ・ひび割れ共通）
        public double Ze { get; private set; }
        public double Ie { get; private set; }
        public double Ft { get; private set; }

        // コンストラクタ
        // isInsituSteelPipeConcretePile: 場所打ち鋼管コンクリート杭のとき true（既定）。
        //   鋼管 1.1F 完全バイリニア型オプションはこの杭種のみ適用する（鋼管杭では false を渡す）。
        internal InsituSteelPipeReinforcedConcreteSection(
            InsituSteelPipe insituSteelPipe, InsituConcrete insituConcrete, MainBars mainBars,
            bool isInsituSteelPipeConcretePile = true)
        {
            // 鉄筋 1.1F 完全バイリニア型オプション（限界ひずみ再計算より前に適用）
            mainBars.YieldAt11F = ConcreteModelOptions.RebarYieldAt11F;
            // 鋼管 1.1F 完全バイリニア型オプション（場所打ち鋼管コンクリート杭のみ）
            insituSteelPipe.PerfectBilinear11F = ConcreteModelOptions.SteelPipeYieldAt11F && isInsituSteelPipeConcretePile;

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
            double pipeCenterDia = PileDia - PipeT;
            PositionCs = [-pipeCenterDia * 0.5, -concreteDia * 0.5, -MainBarPCD * 0.5,];
            PositionTs = [pipeCenterDia * 0.5, concreteDia * 0.5, MainBarPCD * 0.5,];

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
            DamageLimitBeta = [1.0];    // L2: β1=β2=1.0
            DamageLimitBetaL1 = [1.0];  // L1: 同値（β2=1.0 の規定）

            // 安全限界軸力閾値
            // 圧縮側制限値 = σ0 × 換算断面積 Ae   （σ0 = 0.4·ξ·Fc, ξ = Gsi）
            // Ae = InsituConcrete.Ac(コンクリート総断面) + (nr−1)·Ag(主筋) + ns·AMinus(腐食後鋼管)
            //   = Ac_net + nr·Ag + ns·AMinus   （文献「コンクリート純断面 + n×鋼管 + n×鉄筋」と一致）
            //   nr = Er/Ec, ns = Es/Ec。SetZeFtIe() 算定のフィールド値を用いる（N-Q 相関 NMax と統一）。
            UltimateLimitAxialForceThresholds = [
                -0.2 * (mainBars.RSigmaY * mainBars.Ag + insituSteelPipe.Fcy * insituSteelPipe.AMinus),
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Ae
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


            // 低減後損傷限界NMインタラクション（L1/L2 とも同値）
            FactoredDamageNM = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBeta);
            FactoredDamageNMLevel1 = FactoredDamageNM;

            // 低減後安全限界NMインタラクション
            // 指針(案) 準拠オプション時は安全限界曲げ強度をそのまま採用し、低減・軸力制限を課さない
            // （低減後＝低減前の全体を描く＝1本の滑らかな耐力包絡線）。既定は基礎部材の低減率・軸力制限を適用。
            FactoredUltimateNM = ConcreteModelOptions.UseInsituUltimateEFunction
                ? UnfactoredUltimateNM
                : GetFactoredMNInteraction(UnfactoredUltimateNM, (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds), UltimateLimitBeta);

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
            double ts = InsituSteelPipe.TMinus;
            double d = InsituSteelPipe.OutDiaMinus;

            if (ConcreteModelOptions.UseInsituUltimateEFunction)
            {
                // 指針(案): Qu = sQ0·√(1-η²)·(scMu/sMu)
                //   sQ0  = 2·t·(D-t)·sσy/√3        （鋼管の純せん断終局。t,D は腐食考慮値）
                //   η    = N/sNcu, sNcu = sσy·sA    （sA=腐食考慮鋼管有効断面 AMinus）
                //   sMu  = sMu0·cos(πη/2)           （鋼管単独の終局曲げ強度）
                //   sMu0 = Zp·sσy, Zp=(4/3)(R³-(R-t)³)（R=鋼管外半径）
                //   scMu = 合成断面の終局曲げ強度   （同一軸力 N での GetUltimateMomentForSpecificN）
                double sSigmaY = InsituSteelPipe.SSigmaY;           // = 1.1F（材料強度）
                double sA = InsituSteelPipe.AMinus;                 // 腐食考慮鋼管有効断面積
                double sNcu = sSigmaY * sA;
                double eta = sNcu > 1e-9 ? n / sNcu : 0.0;
                eta = Math.Max(-0.999, Math.Min(0.999, eta));       // √(1-η²)・cos の破綻防止

                double R = d * 0.5;
                double Zp = 4.0 / 3.0 * (Math.Pow(R, 3) - Math.Pow(R - ts, 3));
                double sMu0 = Zp * sSigmaY;
                double sMu = sMu0 * Math.Cos(Math.PI * eta / 2.0);
                double sQ0 = 2.0 * ts * (d - ts) * sSigmaY / Math.Sqrt(3.0);
                double scMu = GetUltimateMomentForSpecificN(n).Item1;

                double pipeShear = sQ0 * Math.Sqrt(Math.Max(0.0, 1.0 - eta * eta));
                // sMu≈0（純軸圧で鋼管曲げ耐力消失）や scMu 取得失敗時は鋼管単独せん断にフォールバック
                if (sMu <= 1e-6 || scMu <= 1e-6) return pipeShear;
                return pipeShear * (scMu / sMu);
            }

            double beta1 = 1.0;
            double beta2 = 1.0;
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
            double NMin = -0.2 * (MainBars.RSigmaY * MainBars.Ag + InsituSteelPipe.Fcy * InsituSteelPipe.AMinus);
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
            double NMin = -0.2 * (MainBars.RSigmaY * MainBars.Ag + InsituSteelPipe.Fcy * InsituSteelPipe.AMinus);
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
            double NMin = -0.2 * (MainBars.RSigmaY * MainBars.Ag + InsituSteelPipe.Fcy * InsituSteelPipe.AMinus);
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
            // 収束しなかった場合は最後の計算値を返す（警告のみ）
            if (iter >= maxIter)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetYieldMoment] WARNING: NR法が{maxIter}回で収束しませんでした。" +
                    $" Ntarget={Ntarget:E3}, Nnext={Nnext:E3}, curvature={curvature:E6}");
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
        // 解析用のため、指針オプション時でも全区間をバイリニアで算定し、M-φ の単調性
        // （β1·Mu0 が MY を大きく下回らない＝正勾配ばね）を保って FEM 収束を確保する。
        internal (List<double>, List<double>) GetMPhiRelationshipInternal(double Ntarget, double beta1 = 1.0)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;
            try
            {
            (double MCr, double phiCr) = GetCrackMoment(Ntarget);
            (double MY, double phiY) = GetYieldMoment(Ntarget);
            (double Mu0, double phiU) = GetUltimateMomentForSpecificN(Ntarget);

            List<double> phis;
            List<double> Ms;

            if (phiU <= 0 || phiU <= phiCr)
            {
                // 終局曲率がひび割れ曲率以下 → M≈0
                phis = [0.0];
                Ms = [0.0];
            }
            else if (phiU <= phiY || phiY <= phiCr || phiY <= 0)
            {
                // 降伏前に終局到達、または降伏点が不正（高軸力で鋼材が降伏しない）
                // → ひび割れ→終局の2点で直線
                phis = [0.0, phiCr, phiU];
                Ms = [0.0, MCr, beta1 * Mu0];
            }
            else
            {
                phis = [0.0, phiCr, phiY, phiU];
                Ms = [0.0, MCr, MY, beta1 * Mu0];
            }

            return (phis, Ms);
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        // 杭中間部用M-φ関係（終局曲率をひび割れ後勾配の延長で算出）
        public (List<double> Phis, List<double> Moments) GetMPhiRelationshipForMiddle(double axialN)
        {
            var (phis, Ms) = GetMPhiRelationshipInternal(axialN, 1.0);

            if (phis.Count < 3 || Ms.Count < 4)
                return (phis, Ms);

            double denom = Ms[2] - Ms[1]; // MY - MCr
            if (Math.Abs(denom) < 1e-12)
                return (phis, Ms);

            double beta1 = 1.0;
            // ひび割れ後勾配を延長して β1*Mu に到達する曲率を算出
            double phiC = phis[1] + (phis[2] - phis[1]) * (beta1 * Ms[3] - Ms[1]) / denom;

            List<double> middlePhis = [phis[0], phis[1], phis[2], phiC];
            // モーメント値は杭頭部と同じ
            return (middlePhis, Ms);
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

        /// <summary>
        /// 純引張時のひずみ度: 鋼管と鉄筋の引張耐力に至るひずみの大きい方
        /// </summary>
        internal override double GetPureTensionStrain()
        {
            double pipeTensionStrain = -InsituSteelPipe.SEpsilonU; // 鋼管の引張破断ひずみ
            double rebarTensionStrain = -MainBars.RSigmaY / MainBars.Er; // 鉄筋の引張降伏ひずみ
            return Math.Min(pipeTensionStrain, rebarTensionStrain); // 絶対値が大きい方（より負の値）
        }

        /// <summary>
        /// 特定の軸力時の安全限界曲げモーメント（全区間で最大Mを採用）
        /// NM曲線のピーク両側で同じNに対して2つの解がある場合に対応
        /// </summary>
        internal override (double, double) GetUltimateMomentForSpecificN(double NTarget)
        {
            try
            {
                double epsilonC = 0.003;
                List<double> Ns = UnfactoredUltimateNM.Item1;
                List<double> Ms = UnfactoredUltimateNM.Item2;
                List<double> curvatureList = UnfactoredUltimateNM.Item4;

                double bestM = 0.0;
                double bestCurvature = 0.0;

                for (int seg = 0; seg < Ns.Count - 1; seg++)
                {
                    if ((Ns[seg] - NTarget) * (Ns[seg + 1] - NTarget) > 0)
                        continue;

                    double N = (Ns[seg] + Ns[seg + 1]) * 0.5;
                    double M = (Ms[seg] + Ms[seg + 1]) * 0.5;
                    double curvature = (curvatureList[seg] + curvatureList[seg + 1]) * 0.5;
                    double deltaCurvature = (curvatureList[seg + 1] - curvatureList[seg]) / 100;
                    if (Math.Abs(deltaCurvature) < 1e-15)
                        deltaCurvature = curvature / 500.0;

                    for (int iter = 0; iter < 50; iter++)
                    {
                        if (Math.Abs(N - NTarget) <= 0.1) break;
                        double N1 = GetUltimateForceAndMoment(epsilonC, curvature + deltaCurvature).Item1;
                        double deltaN = N1 - N;
                        if (Math.Abs(deltaN) < 1e-8) break;
                        double step = deltaCurvature / deltaN * (NTarget - N);
                        if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
                            step = Math.Sign(step) * Math.Abs(curvature) * 0.5;
                        curvature += step;
                        (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
                    }

                    if (M > bestM) { bestM = M; bestCurvature = curvature; }
                }
                return (bestM, bestCurvature);
            }
            catch { return (0.0, 0.0); }
        }

        // 使用/損傷限界は基底クラスのGetAllowableMNInteractionを使用

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

        // 解析用 M-φ/M-θ 端点 Mu0 の算定中だけ true にして、指針オプション時でも
        // バイリニアで算定させるガード（e関数/0.85上限の軟化で M-φ 非単調＝負勾配ばねとなり
        // FEM 収束不能になるのを防ぐ。安全限界 NM 曲線＝検定の耐力側は本フラグを立てない）。
        private bool _forceBilinearUltimate;

        // 安全限界軸力、曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - (PileDia * 0.5 - PipeT) * curvature;
            double N, M;
            // 指針(案) 準拠オプション時（かつ解析隔離中でない）: 鋼管=0.85×引張強さ上限トリリニア、
            // 鋼管内コンクリート=e関数法、主筋=バイリニア。既定は全材料バイリニア。
            bool guideline = ConcreteModelOptions.UseInsituUltimateEFunction && !_forceBilinearUltimate;
            string typePipe = guideline ? "guidelineUltimate" : "bilinear";
            string typeConc = guideline ? "eFunction" : "bilinear";
            string typeRebar = "bilinear";
            var result0 = CircularPipeSectionSteelPipe.GetForceAndMoment(typePipe, InsituSteelPipe, epsilon0, curvature);
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(typeConc, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(typeRebar, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(typeConc, InsituConcrete, epsilon0, curvature);

            N = result0.Item1 + result1.Item1 + result2.Item1 - result3.Item1;
            M = result0.Item2 + result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }

        // 場所打ち鋼管コンクリート(SPRC): コンクリート(実心)＋鋼管＋主筋。
        // 力・モーメント計算と同様にプレストレスひずみは加味しない (epsilon0 のみ)。
        internal override SectionStrainStressProfile GetStrainStressProfile(
            double epsilonC, double curvature, bool ultimate, int division = 200)
        {
            double epsilon0 = epsilonC - (PileDia * 0.5 - PipeT) * curvature;
            string type = ultimate ? "bilinear" : "linear";
            double rConc = (PileDia - 2 * PipeT) * 0.5;
            double rPipe = (PileDia - PipeT) * 0.5;
            double rOuter = PileDia * 0.5;

            var p = new SectionStrainStressProfile { Radius = rOuter };
            p.Materials.Add(BuildSolidProfile(SectionMaterialKind.Concrete, "コンクリート",
                InsituConcrete, type, epsilon0, curvature, rConc, 0.0, 0.0, division));
            p.Materials.Add(BuildRingProfile(SectionMaterialKind.SteelPipe, "鋼管",
                InsituSteelPipe, type, epsilon0, curvature, rPipe, 0.0, division));
            if (MainBarArea > 0)
                p.Materials.Add(BuildRingProfile(SectionMaterialKind.MainBar, "主筋",
                    MainBars, type, epsilon0, curvature, MainBarPCD * 0.5, 0.0, division));
            p.CompressionEdgeStrain = epsilon0 + curvature * rOuter;
            p.TensionEdgeStrain = epsilon0 - curvature * rOuter;
            return p;
        }
    }
}
