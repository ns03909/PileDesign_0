using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    // PileSection のうち、断面計算オブジェクトの生成と、鋼管杭の N-M・N-Q 曲線の組立。
    public partial class PileSection
    {
        /// <summary>
        /// 鋼管杭 (鋼管部 / コンクリート充填鋼管部) の使用・損傷・安全限界軸力を
        /// SteelPipeSection から直接算定し、ServiceLimitNMin/NMax,
        /// DamageLimitAxialForceThresholds, UltimateLimitAxialForceThresholds
        /// に反映する。β1=β2=1.0 として以下の式に従う:
        ///
        ///   使用限界:
        ///     Nsc = β1·sNsc1 = β1·sfc1·sAp
        ///     Nst = β1·sNst  = β1·sft·sAp
        ///
        ///   損傷限界 (鋼管部):
        ///     Ndc = β1·sNdc1 = β1·1.5·sfc1·sAp
        ///     Ndt = β1·sNdt  = β1·1.5·sft·sAp
        ///
        ///   損傷限界 (コンクリート充填鋼管部):
        ///     Ndc = β1·(sNdc1 + cNdc),  cNdc = cσck·Air
        ///     Ndt = β1·sNdt
        ///
        ///   安全限界 (両方共通):
        ///     Nuc = β1·β2·sNuc1 = β1·β2·sσCy1·sAp,  sσCy1 = 1.1·1.5·sfc1
        ///     Nut = β1·β2·sNut  = β1·β2·sσTy ·sAp,  sσTy  = 1.1·1.5·sft
        ///
        /// 単位: ServiceLimitNMin/NMax は kN、Damage/Ultimate Thresholds は N。
        /// 必要入力が欠落している場合は何もしない。
        /// </summary>
        /// <summary>
        /// 鋼管杭用の SteelPipeSection (基礎部材の強度と変形性能 2022 準拠) を構築する。
        /// PileSectionType=PileTypeNames.SteelPipeSection は Fc=0、PileTypeNames.CftSection は Fc=ConcreteFc を渡す。
        /// β1=1.0、e=205000 N/mm² 固定。必要入力が欠落していれば null。
        /// </summary>
        // 杭断面ウィンドウの杭頭部 M-φ 図もここを通す (杭頭部の合成 EI を渡すため。組み立て方を 1 か所にする)
        internal SteelPipeSection? TryCreateSteelPipeSection()
        {
            if (PileBodyType != PileTypeNames.SteelPipe) return null;
            if (PileSectionType != PileTypeNames.SteelPipeSection && PileSectionType != PileTypeNames.CftSection) return null;
            if (PileDiameter <= 0 || CorrodedPipeTs <= 0) return null;

            var (sigmaU, f) = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400");
            double fcForSection = PileSectionType == PileTypeNames.CftSection ? ConcreteFc : 0.0;

            return new SteelPipeSection(
                PileDiameter, CorrodedPipeTs, f,
                _beta1: 1.0,
                fc: fcForSection,
                sigmaB: sigmaU,
                e: 205000.0,
                bucklingLength: EffectiveBucklingLength)
            {
                // 杭頭部 (コンクリート充填鋼管部) の合成 EI は杭断面の EI (腐食後) を使う。kNm² → N·mm²
                CompositeHeadEI = PileSectionType == PileTypeNames.CftSection ? EI * 1e9 : null,
            };
        }

        /// <summary>
        /// 鋼管杭の損傷限界 M-N 曲線を構築する。
        /// ユーザ指定式に従い β1 = 1.0:
        ///
        /// 鋼管部 (杭中間部・杭下部、コンクリート充填なし):
        ///   Md = β1·(1.5·sfc1 − |Ndd|/sAp)·sZe
        ///
        /// コンクリート充填鋼管部 (杭頭部):
        ///   Ndd &lt; 0:                    Md = β1·sMd
        ///   0 ≤ Ndd ≤ cNdc:              Md = β1·(sMd + cMd)
        ///   cNdc &lt; Ndd:                 Md = β1·sMd
        ///
        ///   sMd: 鋼管部寄与 (5 ケース分岐、ユーザ指定式)
        ///   cMd: 充填コンクリート部寄与 (Xn 依存、ユーザ指定式)
        ///   cNdc = cσck·Air, cσck = (2/3)·α·√(sApf/(zn·Atr))·Fc
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeDamageNMRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            // 軸力スイープ範囲 = 損傷限界軸力閾値 (UpdateSteelPipeAxialThresholds で設定済)
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            Func<double, double> getMd = isFilledTube
                ? sps.GetDamageLimitMomentHead
                : sps.GetDamageLimitMomentMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Ndd = nMin + (nMax - nMin) * i / nDiv;
                double m = Math.Max(0.0, getMd(Ndd));
                ns.Add(Ndd);
                ms.Add(m);
            }
            return (ns, ms);
        }

        /// <summary>
        /// 鋼管杭の安全限界 M-N 曲線を構築する。
        /// ユーザ指定式に従い β1 = β2 = 1.0:
        ///
        /// 鋼管部 (杭中間部・杭下部):
        ///   |Nud|/sNuc &gt; 0.2:  Mu = β1·β2·1.25·sσCy1·(1 − |Nud|/sNuc)·sZe
        ///   |Nud|/sNuc ≤ 0.2:   Mu = β1·β2·sσCy1·sZp        (塑性プラトー)
        ///   sNuc = min(sNuc1, sNuc2)
        ///
        /// コンクリート充填鋼管部 (杭頭部):
        ///   Mu = β1·β2·(sMu + cMu)
        ///   sMu = 4·srm²·t·sin(sθO)·sσU,   sσU = ((π−sθO)·sσTy + sθO·sσCy1)/π
        ///   cMu = (2/3)·cσIr·(cro³·sin³(cθO) − cri³·sin³(cθI))
        ///   sθO, cθO, cθI は中立軸位置 Xn から導出 (Xn は Ns(Xn) + Nc(Xn) = Nud から二分法で解く)。
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeUltimateNMRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            if (UltimateLimitAxialForceThresholds == null
                || UltimateLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = UltimateLimitAxialForceThresholds[0];
            double nMax = UltimateLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            Func<double, double> getMu = isFilledTube
                ? sps.GetUltimateLimitMomentHead
                : sps.GetUltimateLimitMomentMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Nud = nMin + (nMax - nMin) * i / nDiv;
                double m = Math.Max(0.0, getMu(Nud));
                ns.Add(Nud);
                ms.Add(m);
            }
            return (ns, ms);
        }

        /// <summary>
        /// 鋼管杭 (鋼管部 / コンクリート充填鋼管部 共通) の使用限界 M-N 曲線を構築する。
        /// ユーザ指定式:
        ///   Ms = β1·(sf − |Nsd|/sAp)·sZe,   β1 = 1
        ///   sf = sfc1 (Nsd ≥ 0)、sft (Nsd &lt; 0)
        ///   sZe = π·(D⁴ − (D−2t)⁴) / (32·D)   (腐食代考慮済み有効断面係数)
        ///   sAp = π·(D² − (D−2t)²) / 4
        ///   sft = F/1.5
        ///   sfc1 = F/1.5            (D/t ≤ 25)
        ///        = F/1.5·(0.8 + 5/(D/t))  (D/t &gt; 25、局部座屈低減)
        /// 戻り値は (N [N単位], M [N·mm単位])。範囲外で M&lt;0 は 0 にクランプ。
        /// </summary>
        private (List<double> N, List<double> M) BuildSteelPipeServiceNMRaw()
        {
            if (PileBodyType != PileTypeNames.SteelPipe) return ([], []);
            if (PileSectionType != PileTypeNames.SteelPipeSection && PileSectionType != PileTypeNames.CftSection) return ([], []);
            double D = PileDiameter;
            double t = CorrodedPipeTs;
            if (D <= 0 || t <= 0 || D <= 2 * t) return ([], []);

            (_, double F) = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400");

            double sAp = Math.PI * (D * D - (D - 2 * t) * (D - 2 * t)) / 4.0;
            double sZe = Math.PI * (Math.Pow(D, 4) - Math.Pow(D - 2 * t, 4)) / (32.0 * D);
            double sft = F / 1.5;
            double sfc1 = D / t > 25.0
                ? F / 1.5 * (0.8 + 5.0 / (D / t))
                : F / 1.5;

            const double beta1 = 1.0;
            double Nst = beta1 * sft  * sAp;   // 引張容量 (正値)
            double Nsc = beta1 * sfc1 * sAp;   // 圧縮容量

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var ms = new List<double>(nDiv + 1);
            double nMin = -Nst;
            double nMax =  Nsc;
            for (int i = 0; i <= nDiv; i++)
            {
                double Nsd = nMin + (nMax - nMin) * i / nDiv;
                double sf = (Nsd >= 0) ? sfc1 : sft;
                double m = beta1 * (sf - Math.Abs(Nsd) / sAp) * sZe;
                ns.Add(Nsd);
                ms.Add(Math.Max(0.0, m));
            }
            return (ns, ms);
        }

        // ========== 鋼管杭 N-Q 曲線 ==========
        // SteelPipeSection は AbstractPileSection を継承していないため、GetNQScaled の
        // 通常パスでは ([], []) が返り、せん断力グラフで限界状態線が描画されなかった。
        // 以下 3 メソッドで NM と同じ建付けで N-Q 曲線を構築する。
        // 戻り値は (N [N単位], Q [N単位]) — GetNQScaled 側で N→kN, Q→kN に変換される。

        /// <summary>
        /// 鋼管杭 (鋼管部・コンクリート充填鋼管部 共通) の使用限界 N-Q 曲線を構築する。
        /// Qs = β1 × sfs × sAp / κ は軸力非依存なので、損傷限界軸力範囲で水平線を引く。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeServiceNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            double q = sps.GetServiceLimitShear();
            return ([nMin, nMax], [q, q]);
        }

        /// <summary>
        /// 鋼管杭 の損傷限界 N-Q 曲線。Qd = 1.5 × Qs (軸力非依存) → 水平線。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeDamageNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (DamageLimitAxialForceThresholds == null
                || DamageLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = DamageLimitAxialForceThresholds[0];
            double nMax = DamageLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            double q = sps.GetDamageLimitShear();
            return ([nMin, nMax], [q, q]);
        }

        /// <summary>
        /// 鋼管杭 の安全限界 N-Q 曲線。
        /// 鋼管部 (中間部・下部): Qu = β1 β2 × sQ0 × √(1 − η²),  η = Nud / sNy
        /// コンクリート充填鋼管部 (杭頭部): 上式に Mu/sMu 比を乗じる
        /// 軸力依存なので安全限界軸力範囲で多点プロット。
        /// </summary>
        private (List<double> N, List<double> Q) BuildSteelPipeUltimateNQRaw()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return ([], []);
            if (UltimateLimitAxialForceThresholds == null
                || UltimateLimitAxialForceThresholds.Count < 2) return ([], []);
            double nMin = UltimateLimitAxialForceThresholds[0];
            double nMax = UltimateLimitAxialForceThresholds[1];
            if (nMax <= nMin) return ([], []);

            bool isFilledTube = PileSectionType == PileTypeNames.CftSection;
            Func<double, double> getQu = isFilledTube
                ? sps.GetUltimateLimitShearHead
                : sps.GetUltimateLimitShearMiddle;

            const int nDiv = 100;
            var ns = new List<double>(nDiv + 1);
            var qs = new List<double>(nDiv + 1);
            for (int i = 0; i <= nDiv; i++)
            {
                double Nud = nMin + (nMax - nMin) * i / nDiv;
                double q = Math.Max(0.0, getQu(Nud));
                ns.Add(Nud);
                qs.Add(q);
            }
            return (ns, qs);
        }

        private void UpdateSteelPipeAxialThresholds()
        {
            var sps = TryCreateSteelPipeSection();
            if (sps == null) return;

            const double beta1 = 1.0;
            const double beta2 = 1.0;

            // 使用限界 (kN)
            double sNsc1 = sps.Sfc1 * sps.sAp;   // = sfc1·sAp [N]
            double sNst  = sps.sft  * sps.sAp;   // = sft ·sAp [N]
            ServiceLimitNMin = -beta1 * sNst  * 1e-3;  // 引張 (負)
            ServiceLimitNMax =  beta1 * sNsc1 * 1e-3;  // 圧縮

            // 損傷限界 (N)
            double sNdc1 = 1.5 * sps.Sfc1 * sps.sAp;
            double sNdtPos = 1.5 * sps.sft  * sps.sAp;
            double Ndt = -beta1 * sNdtPos;
            double Ndc = PileSectionType == PileTypeNames.CftSection
                ? beta1 * (sNdc1 + sps.cNdc)
                : beta1 * sNdc1;
            DamageLimitAxialForceThresholds = [Ndt, Ndc];

            // 安全限界 (N)
            // 鋼管部 (杭中間部・杭下部):
            //   Nuc = β1·β2·min(sNuc1, sNuc2),    sNuci = sσCyi·sAp,  sσCyi = 1.1·1.5·sfci
            //   Nut = β1·β2·sNut,                 sNut  = sσTy·sAp,   sσTy  = 1.1·1.5·sft
            // コンクリート充填鋼管部 (杭頭部):
            //   Nuc = β1·β2·(sNuc1 + cNuc),       cNuc = cσIr·Air, cσIr = α·√(sApf/(zn·Air))·Fc
            //   Nut = β1·β2·sNut1,                sNut1 = σB·sAp   (ultimate tensile strength)
            double Nuc, Nut;
            if (PileSectionType == PileTypeNames.CftSection)
            {
                Nuc = beta1 * beta2 * (sps.sNuc1 + sps.cNuc);
                Nut = beta1 * beta2 * sps.sNut1;
            }
            else
            {
                Nuc = beta1 * beta2 * Math.Min(sps.sNuc1, sps.sNuc2);
                Nut = beta1 * beta2 * sps.sNut;
            }
            UltimateLimitAxialForceThresholds = [-Nut, Nuc];
        }

        /// <summary>
        /// 軸力制限 [nMin, nMax] (どちらも N 単位) で N-M 曲線をクリップする。
        /// 範囲外では M=0、境界では (nMin, 0) → (nMin, M_補間) → ... → (nMax, M_補間) → (nMax, 0)
        /// と垂直降下/上昇する点列を返す。PrecastPileSection.ApplyAxialForceLimitsToNM と同等。
        /// </summary>
        private static (List<double> ns, List<double> ms) ClipNMByThresholds(
            List<double> uN, List<double> uM, double nMin, double nMax)
        {
            var ns = new List<double>();
            var ms = new List<double>();

            ns.Add(nMin);
            ms.Add(0.0);

            double mAtMin = InterpolateMAtN(uN, uM, nMin);
            if (mAtMin > 0)
            {
                ns.Add(nMin);
                ms.Add(mAtMin);
            }

            for (int i = 0; i < uN.Count; i++)
            {
                if (uN[i] >= nMin && uN[i] <= nMax)
                {
                    ns.Add(uN[i]);
                    ms.Add(uM[i]);
                }
            }

            double mAtMax = InterpolateMAtN(uN, uM, nMax);
            if (mAtMax > 0)
            {
                ns.Add(nMax);
                ms.Add(mAtMax);
            }

            ns.Add(nMax);
            ms.Add(0.0);

            return (ns, ms);
        }

        /// <summary>
        /// N-M 曲線上で軸力 nTarget に対応する M を線形補間で算定する。
        /// 単調でない曲線でも複数交点のうち最大の M を返す (耐力包絡線として安全側)。
        /// </summary>
        private static double InterpolateMAtN(List<double> ns, List<double> ms, double nTarget)
        {
            double maxM = 0.0;
            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                if ((n0 - nTarget) * (n1 - nTarget) <= 0 && n0 != n1)
                {
                    double t = (nTarget - n0) / (n1 - n0);
                    double mInterp = ms[i] + t * (ms[i + 1] - ms[i]);
                    maxM = Math.Max(maxM, mInterp);
                }
            }
            return maxM;
        }

        /// <summary>
        /// 現在の断面パラメータに基づいて断面計算オブジェクトを生成します。
        /// </summary>
        /// <returns>断面計算オブジェクト。生成できない場合は null。</returns>
        internal IPileSectionCalculation? CreateSectionCalculator()
        {
            return (PileBodyType, PileSectionType) switch
            {
                // 場所打ちRC杭
                // 帯筋 (HoopPw / HoopSigmay) を渡す。渡さないと安全限界せん断が
                // 断面クラスの仮値 (pw=0.002 / σwy=295) で作られる。
                // 工法 (HoopMethod) も渡す。渡さないとせん断耐力の式が標準のままになり、
                // 高強度せん断補強筋を選んでも耐力が指針どおりにならない。
                (PileTypeNames.InsituRc, _) =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc, gamma: ConcreteGamma),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        hoopPw: HoopPw, hoopSigmaWy: HoopSigmay, hoopMethod: HoopMethod,
                        hoopDamageFormula: HoopDamageFormula, hoopUltimateFormula: HoopUltimateFormula),

                // 場所打ち鋼管RC杭 - RC部
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.RcSection) =>
                    new InsituReinforcedConcreteSection(
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc, gamma: ConcreteGamma),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        hoopPw: HoopPw, hoopSigmaWy: HoopSigmay, hoopMethod: HoopMethod,
                        hoopDamageFormula: HoopDamageFormula, hoopUltimateFormula: HoopUltimateFormula),

                // 場所打ち鋼管RC杭 - 鋼管RC部
                // 終局ひずみ 5,000μ オプション時はコンクリートにも同じ εcu を渡す。
                // 断面側 (UltimateCompressiveStrain) だけ広げても、材料側の有効範囲が 0.003 のままだと
                // ε>0.003 で σ=0 に脱落して終局曲げが静かに小さくなるため、必ずセットで渡すこと。
                (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) =>
                    new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth),
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc,
                            epsilonCu: ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                                ? SectionDesignConstants.KCTB_ULTIMATE_COMPRESSIVE_STRAIN
                                : SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN,
                            gamma: ConcreteGamma),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize)),

                // PHC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Phc) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PHC節杭
                // カタログの断面性能はすべて軸部の中空円形断面基準なので、断面耐力の計算は
                // PHC杭 と完全に同一。専用クラスは作らず PHCSection をそのまま使う。
                // (節部径 NodeDiameter は断面耐力には一切効かない)
                (PileTypeNames.PrecastConcrete, PileTypeNames.PhcNodular) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC節杭 (PHC部)。異形棒鋼を持たない区間なので PHC杭 と同一。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodularPhcPart) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // BF.S (頭部厚型節付き杭)。PC 鋼棒のみの中空断面なので PHC杭 と同一。
                // 先端軸部はカタログに耐力の記載が無く、ここで計算した値がそのまま設計値になる。
                (PileTypeNames.PrecastConcrete, PileTypeNames.BfsHead or PileTypeNames.BfsTip)
                        when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PHCSection(
                        new PrecastPHCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC節杭 (PRC部)。断面性能は軸部基準なので PRC杭 と同一。
                (PileTypeNames.PrecastConcrete, PileTypeNames.PrcNodular) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PRCSection(
                        new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // PRC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Prc) when TendonAp > 0 && PileDiameter != 2 * ConcreteThickness =>
                    new PRCSection(
                        new PrecastPRCConcrete(PileDiameter, PileDiameter - 2 * ConcreteThickness, ConcreteFc),
                        new MainBars(MainBarDr, MainBarNum, MainBarSpec, MainBarSize),
                        new Tendons(TendonDp, TendonAp, TendonSigmaPy, TendonSigmaPu),
                        Prestress),

                // SC杭
                (PileTypeNames.PrecastConcrete, PileTypeNames.Sc) when PileDiameter != 2 * ConcreteThickness =>
                    new SCSection(
                        new PrecastSCConcrete(PileDiameter - 2 * PipeTs, PileDiameter - 2 * PipeTs - 2 * ConcreteThickness, ConcreteFc),
                        new PrecastSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth)),

                // 鋼管杭 - コンクリート充填鋼管部 (杭頭部、鉄筋定着工法用)
                // 鋼管 + 充填コンクリートの SPRC 断面として扱う。鉄筋は配置可だが
                // M-φ の耐力には参入しない方針のため MainBarNum=0 で渡す。
                // (杭頭ノード固定の RC 円形 NM 計算は PileTop 側で 2 段断面として別途実施)
                (PileTypeNames.SteelPipe, PileTypeNames.CftSection) =>
                    new InsituSteelPipeReinforcedConcreteSection(
                        new InsituSteelPipe(PipeGrade, PipeDia, PipeTs, CorrosionDepth),
                        new InsituConcrete(ConcreteOutDia, ConcreteGsi, ConcreteFc, gamma: ConcreteGamma),
                        new MainBars(MainBarDr, 0, MainBarSpec, MainBarSize),
                        // 鋼管杭は鋼管 1.1F 完全バイリニア型オプションの対象外
                        isInsituSteelPipeConcretePile: false),

                // 鋼管杭 - 鋼管部 / 旧サブ名 (純粋な鋼管区間 → 既存挙動に合わせ M-φ は null)
                (PileTypeNames.SteelPipe, _) => null,

                _ => null
            };
        }
    }
}
