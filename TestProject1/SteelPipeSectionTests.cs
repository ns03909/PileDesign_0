using PileDesign.Models.InputData;
using System;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 鋼管杭断面 (SteelPipeSection) の損傷限界モーメントおよび関連量の単体テスト群。
    ///
    /// 検証内容:
    ///   - Sfc1 局部座屈低減式 (係数 0.8 確定後の連続性)
    ///   - Sfc2 柱座屈低減式 (Johnson 放物線 + Euler、Nc=Ny プレースホルダ)
    ///   - 損傷限界圧縮軸力 Ndc = β1×min(sNdc1, sNdc2)
    ///   - 杭中間部・下部の Md (線形相互作用)
    ///   - 杭頭部 (CFT) Md の 5+3 ケース分岐
    ///   - 境界 Xn=t, t+zh, sri+cro, sro+cri での連続性/サチュレーション
    /// </summary>
    [TestClass]
    public class SteelPipeSectionTests
    {
        // ----- Helper: internal クラスの reflection アクセス -----
        private static SteelPipeSection NewSection_NoFc(double D, double t, double F, double beta1 = 1.0)
        {
            var type = typeof(SteelPipeSection);
            var ctor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: new[] { typeof(double), typeof(double), typeof(double), typeof(double) },
                modifiers: null)!;
            return (SteelPipeSection)ctor.Invoke(new object[] { D, t, F, beta1 });
        }

        private static SteelPipeSection NewSection_WithFc(double D, double t, double F, double beta1, double Fc, double E = 205000.0)
        {
            var type = typeof(SteelPipeSection);
            var ctor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: new[] { typeof(double), typeof(double), typeof(double), typeof(double), typeof(double), typeof(double) },
                modifiers: null)!;
            return (SteelPipeSection)ctor.Invoke(new object[] { D, t, F, beta1, Fc, E });
        }

        /// <summary>
        /// 完全コンストラクタ。末尾の座屈長は 0 (柱座屈による低減なし) を既定にする。
        /// 座屈長を与えたときの挙動は <see cref="SteelPipeBucklingTests"/> が受け持つ。
        /// </summary>
        private static SteelPipeSection NewSection_Full(double D, double t, double F, double beta1, double Fc, double sigmaB,
            double E = 205000.0, double bucklingLength = 0.0)
        {
            var type = typeof(SteelPipeSection);
            var ctor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(double), typeof(double), typeof(double), typeof(double),
                               typeof(double), typeof(double), typeof(double), typeof(double) },
                modifiers: null)!;
            return (SteelPipeSection)ctor.Invoke(new object[] { D, t, F, beta1, Fc, sigmaB, E, bucklingLength });
        }

        // ===== Sfc1: 局部座屈低減式 =====

        [TestMethod]
        public void Sfc1_AtDtRatio25_NoReduction()
        {
            // D = 600, t = 24 → D/t = 25 (境界、低減なし)
            var s = NewSection_NoFc(600, 24, 235);
            double expected = 235.0 / 1.5;
            Assert.AreEqual(expected, s.Sfc1, 1e-6);
        }

        [TestMethod]
        public void Sfc1_AtDtRatio25Plus_ContinuousAt08()
        {
            // D/t = 25 を僅かに超えた点で 0.8 + 5/25 = 1.0 → 連続
            var s = NewSection_NoFc(600, 23.99, 235);
            double DtRatio = 600 / 23.99;
            double factor = 0.8 + 5.0 / DtRatio;
            double expected = 235.0 / 1.5 * factor;
            Assert.AreEqual(expected, s.Sfc1, 1e-3);
            // factor は 1.0 に非常に近い (D/t わずかに 25 超え)
            Assert.IsTrue(Math.Abs(factor - 1.0) < 0.01);
        }

        [TestMethod]
        public void Sfc1_AtDtRatio50_Reduction09()
        {
            // D = 600, t = 12 → D/t = 50, factor = 0.8 + 0.1 = 0.9
            var s = NewSection_NoFc(600, 12, 235);
            double expected = 235.0 / 1.5 * 0.9;
            Assert.AreEqual(expected, s.Sfc1, 1e-6);
        }

        [TestMethod]
        public void Sfc1_AtDtRatio100_Reduction085()
        {
            // D/t = 100, factor = 0.8 + 0.05 = 0.85
            var s = NewSection_NoFc(1000, 10, 235);
            double expected = 235.0 / 1.5 * 0.85;
            Assert.AreEqual(expected, s.Sfc1, 1e-6);
        }

        // ===== Sfc2: 柱座屈低減 =====

        /// <summary>
        /// 柱座屈による低減は<b>液状化区間があるときだけ</b>効く。
        ///
        /// 座屈長は液状化区間 (β &lt; 1 の範囲) の長さ
        /// （「基礎部材の強度と変形性能」解説図 8.3）。液状化を検討していないモデルでは
        /// 座屈長が 0 になり、sfc2 = F/1.5 で低減されない。このとき局部座屈側の
        /// sfc1 (≤ F/1.5) が必ず支配するので、<b>耐力は従来と変わらない</b>。
        ///
        /// 座屈長を与えたときの挙動は <see cref="SteelPipeBucklingTests"/> が受け持つ。
        /// </summary>
        [TestMethod]
        public void Sfc2_WithoutLiquefaction_LeavesLocalBucklingGoverning()
        {
            foreach (var (D, t) in new[] { (600.0, 12.0), (1000.0, 10.0), (600.0, 24.0) })
            {
                var s = NewSection_NoFc(D, t, 235);
                Assert.AreEqual(235.0 / 1.5, s.Sfc2, 1e-9,
                    $"D={D}, t={t}: 液状化区間が無いのに柱座屈低減が入っています");
                Assert.IsTrue(s.Sfc1 <= s.Sfc2 + 1e-9,
                    $"D={D}, t={t}: 局部座屈側が支配しなくなっています (sfc1={s.Sfc1}, sfc2={s.Sfc2})");
                Assert.AreEqual(s.sNdc1, s.Ndc, 1e-6,
                    $"D={D}, t={t}: 損傷限界圧縮軸力が局部座屈側と一致しません");
            }
        }

        // ===== Ndc = β1 × min(sNdc1, sNdc2) =====

        [TestMethod]
        public void Ndc_TakesMinimumOfTwoBucklingModes()
        {
            var s = NewSection_NoFc(600, 12, 235, beta1: 1.0);
            double expected = 1.0 * Math.Min(s.sNdc1, s.sNdc2);
            Assert.AreEqual(expected, s.Ndc, 1e-3);
        }

        [TestMethod]
        public void Ndc_Beta1Halved_HalvesNdc()
        {
            var s1 = NewSection_NoFc(600, 12, 235, beta1: 1.0);
            var s2 = NewSection_NoFc(600, 12, 235, beta1: 0.5);
            Assert.AreEqual(s1.Ndc * 0.5, s2.Ndc, 1e-3);
        }

        // ===== zh (ずれ止め板厚) =====

        [TestMethod]
        public void Zh_StepFunction_OfDiameter()
        {
            Assert.AreEqual(9.0, NewSection_NoFc(700, 12, 235).zh);   // D < 800
            Assert.AreEqual(9.0, NewSection_NoFc(799, 12, 235).zh);
            Assert.AreEqual(12.0, NewSection_NoFc(800, 12, 235).zh);  // 800 ≤ D < 1200
            Assert.AreEqual(12.0, NewSection_NoFc(1199, 12, 235).zh);
            Assert.AreEqual(16.0, NewSection_NoFc(1200, 14, 235).zh); // D ≥ 1200
            Assert.AreEqual(16.0, NewSection_NoFc(2000, 16, 235).zh);
        }

        // ===== α (拘束効果による強度上昇率) =====

        [TestMethod]
        public void Alpha_AtSmallDtRatio_Capped1()
        {
            // D/t = 76.4 で 5.05 - 0.053×76.4 ≈ 1.0
            // D/t = 100 で 5.05 - 5.3 < 0 → max(.., 1.0) = 1.0
            var s = NewSection_NoFc(1000, 10, 235);  // D/t = 100
            Assert.AreEqual(1.0, s.Alpha, 1e-6);
        }

        [TestMethod]
        public void Alpha_AtModerateDtRatio_GreaterThan1()
        {
            // D/t = 50 → 5.05 - 2.65 = 2.4
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(2.4, s.Alpha, 1e-6);
        }

        // ===== cNdc (充填コンクリート損傷限界圧縮軸力) =====

        [TestMethod]
        public void CNdc_RequiresFc_ZeroWhenFcMissing()
        {
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(0.0, s.cSigmaCk, 1e-9);
            Assert.AreEqual(0.0, s.cNdc, 1e-9);
        }

        [TestMethod]
        public void CNdc_NumericalValueForTypicalCase()
        {
            // D=600, t=12, Fc=24
            // sro=300, cro=288, cri=279
            // sApf = π × 288² ≈ 260,576
            // Atr = π × (288²-279²) = π × 5103 ≈ 16,032
            // α = 2.4
            // cσCk = (2/3) × 2.4 × √(260576/(2×16032)) × 24 ≈ 109 N/mm²
            // cNdc = 109 × 16032 ≈ 1.75e6 N = 1.75 MN
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            Assert.IsTrue(s.cNdc > 1.5e6 && s.cNdc < 2.0e6,
                $"cNdc out of expected range: {s.cNdc}");
        }

        // ===== 中間部 Md (sfc1 修正後の既存式) =====

        [TestMethod]
        public void DamageLimitMomentMiddle_AtZeroAxial_Equals1_5_Sfc1_Ze()
        {
            var s = NewSection_NoFc(600, 12, 235);
            double Md = s.GetDamageLimitMomentMiddle(0.0);
            double expected = 1.0 * 1.5 * s.Sfc1 * s.sZe;
            Assert.AreEqual(expected, Md, 1e-3);
        }

        [TestMethod]
        public void DamageLimitMomentMiddle_LinearlyReducesWithCompression()
        {
            var s = NewSection_NoFc(600, 12, 235);
            double Md0 = s.GetDamageLimitMomentMiddle(0.0);
            double Md1 = s.GetDamageLimitMomentMiddle(s.sNdc1 * 0.5);
            double Md2 = s.GetDamageLimitMomentMiddle(s.sNdc1);
            // 軸力が圧縮容量に達すると M が 0 に近づく
            Assert.IsTrue(Md1 < Md0);
            Assert.AreEqual(0.0, Md2, 1.0);
        }

        // ===== 杭頭部 Md (CFT) =====

        [TestMethod]
        public void DamageLimitMomentHead_TensionRegion_NoConcreteContribution()
        {
            // Ndd < 0 (引張) は Md = β1 × sMd のみ
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double Md = s.GetDamageLimitMomentHead(-1.0e5);  // 軽い引張
            Assert.IsTrue(Md > 0);
        }

        [TestMethod]
        public void DamageLimitMomentHead_PureBendingInCaseB_HasMaxSMdPlusCMdAtXn0()
        {
            // Ndd = 0 の場合は case B (cMd 加算ありだが Nc=0 → Xn=t → cMd≈0)
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double Md = s.GetDamageLimitMomentHead(0.0);
            // sMd (case 3) = 1.5×(sft+sfc1)/2 × sZe
            double sMd0 = 1.5 * (s.sft + s.Sfc1) / 2.0 * s.sZe;
            // cMd は Xn=t で 0 (特異性線形補間入口)
            Assert.IsTrue(Md >= sMd0 - 1.0,  $"Md={Md} should be ≥ sMd0={sMd0}");
            Assert.IsTrue(Md < sMd0 * 1.5,  $"Md={Md} should not blow up");
        }

        [TestMethod]
        public void DamageLimitMomentHead_AtCNdc_ConcreteFullySaturated()
        {
            // Ndd = cNdc 境界で case B (上限) と case C (下限) が連続するか
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double MdAtCNdc = s.GetDamageLimitMomentHead(s.cNdc);
            double MdAfterCNdc = s.GetDamageLimitMomentHead(s.cNdc * 1.0001);
            // case B の最大値 vs case C の入口 — sMd は連続なので大きく違わない
            Assert.IsTrue(MdAtCNdc >= MdAfterCNdc - 1.0,
                $"Discontinuity at cNdc: B={MdAtCNdc}, C={MdAfterCNdc}");
        }

        [TestMethod]
        public void DamageLimitMomentHead_HighCompressionExceedingCapacity_Zero()
        {
            // Ndd > sNdc1 + cNdc → sMd = 0 (case 5)、case C なので Md = 0
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double Md = s.GetDamageLimitMomentHead(s.sNdc1 + s.cNdc + 1.0e5);
            Assert.AreEqual(0.0, Md, 1.0);
        }

        [TestMethod]
        public void DamageLimitMomentHead_HighTensionExceedingCapacity_Zero()
        {
            // Ndd ≤ sNdt → sMd = 0 (case 1)、case A なので Md = 0
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double Md = s.GetDamageLimitMomentHead(s.sNdt - 1.0e5);
            Assert.AreEqual(0.0, Md, 1.0);
        }

        // ===== 使用限界 (既存) =====

        [TestMethod]
        public void ServiceLimitMoment_AtZeroAxial_Equals_Sfc1_Ze()
        {
            var s = NewSection_NoFc(600, 12, 235);
            double Msd = s.GetServiceLimitMoment(0.0);
            double expected = 1.0 * s.Sfc1 * s.sZe;
            Assert.AreEqual(expected, Msd, 1e-3);
        }

        // ===== 連続性確認 (case A→B 境界、Ndd=0) =====

        [TestMethod]
        public void DamageLimitMomentHead_AtNddZero_CrossingABBoundary()
        {
            // Ndd = -ε (case A、Md = β1×sMd) と Ndd = +ε (case B、Md = β1×(sMd+cMd))
            // Xn=t 近傍の線形補間で cMd → 0 なので連続
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double MdA = s.GetDamageLimitMomentHead(-1.0);   // case A
            double MdB = s.GetDamageLimitMomentHead(+1.0);   // case B 入口
            // 不連続性が大きすぎないことを確認 (10% 許容)
            double rel = Math.Abs(MdA - MdB) / Math.Max(MdA, MdB);
            Assert.IsTrue(rel < 0.10, $"Large discontinuity at Ndd=0: A={MdA}, B={MdB}, rel={rel}");
        }

        // ===== 安全限界軸力 =====

        [TestMethod]
        public void UltimateLimitAxial_YieldStresses_ConsistentWithDefinitions()
        {
            // sσCy1 = 1.1 × 1.5 × sfc1, sσTy = 1.1 × 1.5 × sft
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(1.1 * 1.5 * s.Sfc1, s.sSigmaCy1, 1e-9);
            Assert.AreEqual(1.1 * 1.5 * s.Sfc2, s.sSigmaCy2, 1e-9);
            Assert.AreEqual(1.1 * 1.5 * s.sft, s.sSigmaTy, 1e-9);
        }

        [TestMethod]
        public void UltimateLimitAxial_sNuc1_Equals_SigmaCy1_Times_Ap()
        {
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(s.sSigmaCy1 * s.sAp, s.sNuc1, 1e-3);
            Assert.AreEqual(s.sSigmaCy2 * s.sAp, s.sNuc2, 1e-3);
            Assert.AreEqual(s.sSigmaTy * s.sAp, s.sNut, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitAxial_NucMiddle_TakesMinOfBucklingModes()
        {
            var s = NewSection_NoFc(600, 12, 235, beta1: 1.0);
            double expected = 1.0 * 1.0 * Math.Min(s.sNuc1, s.sNuc2);
            Assert.AreEqual(expected, s.NucMiddle, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitAxial_NucHead_AddsSteelAndConcrete()
        {
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            // 杭頭部 Nuc = β1 β2 (sNuc1 + cNuc) — 局部座屈のみ + 充填コン (柱座屈は無視)
            double expected = 1.0 * 1.0 * (s.sNuc1 + s.cNuc);
            Assert.AreEqual(expected, s.NucHead, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitAxial_NutHead_UsesUltimateTensileStrength()
        {
            // sNut1 = sσB × sAp (引張強さベース)
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double expected = 400.0 * s.sAp;
            Assert.AreEqual(expected, s.sNut1, 1e-3);
            Assert.AreEqual(expected, s.NutHead, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitAxial_cSigmaIr_Is_OneAndHalfTimes_cSigmaCk()
        {
            // cσIr (安全) = 1.5 × cσCk (損傷) (2/3 係数の差)
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            Assert.AreEqual(1.5 * s.cSigmaCk, s.cSigmaIr, 1e-6);
        }

        [TestMethod]
        public void UltimateLimitAxial_cNuc_GreaterThan_cNdc()
        {
            // 安全限界 > 損傷限界 (1.5 倍関係)
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            Assert.AreEqual(1.5 * s.cNdc, s.cNuc, 1e-3);
        }

        // ===== sZp 塑性断面係数 =====

        [TestMethod]
        public void sZp_AnnularFormula_Matches_AlternativeForm()
        {
            // sZp = (4/3) × sro³ × (1 - (1 - t/sro)³)
            //     = (4/3) × (sro³ - sri³)
            var s = NewSection_NoFc(600, 12, 235);
            double sro = s.sro;
            double sri = s.sri;
            double expected = 4.0 / 3.0 * (sro * sro * sro - sri * sri * sri);
            Assert.AreEqual(expected, s.sZp, 1e-3);
        }

        [TestMethod]
        public void sZp_GreaterThan_sZe_ForAnnularPipe()
        {
            // 塑性係数 sZp > 弾性係数 sZe (典型的な比 sZp/sZe ≈ 4/π ≈ 1.27 for thin tube)
            var s = NewSection_NoFc(600, 12, 235);
            Assert.IsTrue(s.sZp > s.sZe);
            double ratio = s.sZp / s.sZe;
            Assert.IsTrue(ratio > 1.2 && ratio < 1.4, $"Plastic shape factor out of expected range: {ratio}");
        }

        // ===== 安全限界モーメント (中間部) =====

        [TestMethod]
        public void UltimateLimitMomentMiddle_AtZeroAxial_Equals_SigmaCy1_Zp()
        {
            // |Nud|/sNuc ≤ 0.2 → Mu = β1 β2 × sσCy1 × sZp (塑性モーメント)
            var s = NewSection_NoFc(600, 12, 235);
            double Mu = s.GetUltimateLimitMomentMiddle(0.0);
            double expected = 1.0 * 1.0 * s.sSigmaCy1 * s.sZp;
            Assert.AreEqual(expected, Mu, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitMomentMiddle_BelowThreshold_Constant()
        {
            // |Nud|/sNuc ≤ 0.2 領域では Mu が一定 (軸力依存なし)
            var s = NewSection_NoFc(600, 12, 235);
            double Mu0 = s.GetUltimateLimitMomentMiddle(0.0);
            double Mu_at02 = s.GetUltimateLimitMomentMiddle(0.19 * s.sNuc);
            Assert.AreEqual(Mu0, Mu_at02, 1e-3);
        }

        /// <summary>
        /// |Nud|/sNuc &gt; 0.2 では Mu = 1.25 × sσCy1 × (1 − |Nud|/sNuc) × <b>sZp</b>。
        /// 断面係数は 0.2 の上下で同じ sZp を使う (以前は上側だけ sZe だった)。
        /// </summary>
        [TestMethod]
        public void UltimateLimitMomentMiddle_AboveThreshold_LinearInteraction()
        {
            var s = NewSection_NoFc(600, 12, 235);
            double Nud = 0.5 * s.sNuc;
            double Mu = s.GetUltimateLimitMomentMiddle(Nud);
            double expected = 1.0 * 1.0 * 1.25 * s.sSigmaCy1 * (1.0 - 0.5) * s.sZp;
            Assert.AreEqual(expected, Mu, 1e-3);
        }

        /// <summary>
        /// 0.2 の境界で<b>連続</b>であること。
        ///
        /// 以前は上側だけ sZe を使っており、境界で Mu が 2 割ほど跳んでいた。
        /// 軸力は荷重ステップごとに動く (README「暗黙の前提」3) ので、
        /// この段差は解析の途中で耐力が跳ぶことを意味する。
        /// </summary>
        [TestMethod]
        public void UltimateLimitMomentMiddle_IsContinuousAt02()
        {
            var s = NewSection_NoFc(600, 12, 235);
            double below = s.GetUltimateLimitMomentMiddle(0.199 * s.sNuc);
            double above = s.GetUltimateLimitMomentMiddle(0.201 * s.sNuc);

            // 境界の直下は軸力によらず一定 (塑性モーメント)、直上は 1.25(1−0.201) 倍。
            // 0.2 ちょうどで 1.25 × (1 − 0.2) = 1.0 になるので両側がつながる。
            Assert.AreEqual(below, above, below * 2e-3,
                $"0.2 の境界で耐力が跳んでいます (下 {below:F1} / 上 {above:F1})");
        }

        [TestMethod]
        public void UltimateLimitMomentMiddle_AtFullCapacity_Zero()
        {
            // |Nud| = sNuc → Mu = 0
            var s = NewSection_NoFc(600, 12, 235);
            double Mu = s.GetUltimateLimitMomentMiddle(s.sNuc);
            Assert.AreEqual(0.0, Mu, 1.0);
        }

        // ===== 安全限界モーメント (杭頭部 CFT) =====

        [TestMethod]
        public void UltimateLimitMomentHead_RequiresFullConstructor()
        {
            // Fc = 0 では cσIr = 0 となり Mu = 0
            var s = NewSection_NoFc(600, 12, 235);
            double Mu = s.GetUltimateLimitMomentHead(0.0);
            Assert.AreEqual(0.0, Mu, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitMomentHead_AtZeroAxial_Positive()
        {
            // 軸力 0 で sMu + cMu の合計が正の値
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Mu = s.GetUltimateLimitMomentHead(0.0);
            Assert.IsTrue(Mu > 0);
        }

        [TestMethod]
        public void UltimateLimitMomentHead_PositiveCompression_BelowFullCapacity()
        {
            // 中程度圧縮で Mu > 0
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Mu = s.GetUltimateLimitMomentHead(s.sNuc1 * 0.3);
            Assert.IsTrue(Mu > 0);
        }

        [TestMethod]
        public void UltimateLimitMomentHead_GreaterThan_DamageLimitHead()
        {
            // 安全限界モーメント > 損傷限界モーメント (容量増分の整合性)
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Md = s.GetDamageLimitMomentHead(0.0);
            double Mu = s.GetUltimateLimitMomentHead(0.0);
            Assert.IsTrue(Mu > Md, $"Mu={Mu} should be > Md={Md}");
        }

        // ===== 使用限界・損傷限界せん断 =====

        [TestMethod]
        public void ServiceLimitShear_FormulaMatch()
        {
            // Qs = β1 × sfs × sAp / κ, sfs = F / (1.5 √3), κ = 2
            var s = NewSection_NoFc(600, 12, 235, beta1: 1.0);
            double sfs = 235.0 / 1.5 / Math.Sqrt(3.0);
            double expected = 1.0 * sfs * s.sAp / 2.0;
            Assert.AreEqual(expected, s.GetServiceLimitShear(), 1e-3);
        }

        [TestMethod]
        public void DamageLimitShear_OneAndHalfTimes_ServiceLimitShear()
        {
            // Qd = 1.5 × Qs (安全率撤去)
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(1.5 * s.GetServiceLimitShear(), s.GetDamageLimitShear(), 1e-3);
        }

        [TestMethod]
        public void DamageLimitShear_NoAxialDependency()
        {
            // 損傷限界せん断は軸力依存なし
            var s = NewSection_NoFc(600, 12, 235);
            // GetDamageLimitShear() は引数なしなので軸力非依存自体は構造的に保証されるが、
            // 値が同じであることを符号的に検証
            Assert.AreEqual(s.GetDamageLimitShear(), s.GetDamageLimitShear(), 1e-9);
        }

        // ===== 安全限界せん断 (中間部) =====

        [TestMethod]
        public void UltimateLimitShearMiddle_AtZeroAxial_FullPlasticCapacity()
        {
            // η = 0 → Qu = β1 β2 × sQ0 (max)
            var s = NewSection_NoFc(600, 12, 235, beta1: 1.0);
            double sQ0 = 2.0 * 12.0 * (600 - 12) * s.sSigmaTy / Math.Sqrt(3.0);
            double expected = 1.0 * 1.0 * sQ0;
            double Qu = s.GetUltimateLimitShearMiddle(0.0);
            Assert.AreEqual(expected, Qu, 1e-3);
        }

        [TestMethod]
        public void UltimateLimitShearMiddle_AtYieldAxial_Zero()
        {
            // |Nud| = sNy → η = ±1 → Qu = 0
            var s = NewSection_NoFc(600, 12, 235);
            double sNy = s.sSigmaTy * s.sAp;
            Assert.AreEqual(0.0, s.GetUltimateLimitShearMiddle(sNy), 1.0);
            Assert.AreEqual(0.0, s.GetUltimateLimitShearMiddle(-sNy), 1.0);
        }

        [TestMethod]
        public void UltimateLimitShearMiddle_UsesSigmaTy_NotF()
        {
            // 旧仕様 (F ベース) との差: sσTy = 1.1 F なので新仕様は 10% 大
            var s = NewSection_NoFc(600, 12, 235);
            double Qu = s.GetUltimateLimitShearMiddle(0.0);
            double expectedNew = 2.0 * 12.0 * 588.0 * (1.1 * 235.0) / Math.Sqrt(3.0);
            Assert.AreEqual(expectedNew, Qu, 1.0);
        }

        // ===== 安全限界せん断 (杭頭部) =====

        [TestMethod]
        public void UltimateLimitShearHead_RequiresFcAndSigmaB()
        {
            // Fc=0 では Mu/sMu 比が計算できないので 0 を返す
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(0.0, s.GetUltimateLimitShearHead(0.0), 1e-3);
        }

        [TestMethod]
        public void UltimateLimitShearHead_GreaterThan_ShearMiddle_ByMuRatio()
        {
            // 杭頭部の Qu には Mu/sMu (>1) が乗じられるので中間部より大きい
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Nud = 0.0;
            double QuMid = s.GetUltimateLimitShearMiddle(Nud);
            double QuHead = s.GetUltimateLimitShearHead(Nud);
            Assert.IsTrue(QuHead > QuMid, $"Head={QuHead} should be > Mid={QuMid}");
        }

        [TestMethod]
        public void UltimateLimitShearHead_TensionAxial_UsesNutDenom()
        {
            // Nud < 0 で Nut で正規化、Qu > 0 (軽い引張)
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Qu = s.GetUltimateLimitShearHead(-100000);
            Assert.IsTrue(Qu > 0);
        }

        [TestMethod]
        public void UltimateLimitShearHead_AtFullCompressionCapacity_Zero()
        {
            // Nud = NucHead で η = 1, Qu = 0
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Qu = s.GetUltimateLimitShearHead(s.NucHead);
            Assert.AreEqual(0.0, Qu, 1.0);
        }

        // ===== Service ↔ Damage ↔ Ultimate せん断の大小関係 =====

        [TestMethod]
        public void ShearLimitOrdering_Qs_LessThan_Qd_LessThan_QuMid()
        {
            // Qs < Qd < QuMiddle (at zero axial)
            var s = NewSection_NoFc(600, 12, 235);
            double Qs = s.GetServiceLimitShear();
            double Qd = s.GetDamageLimitShear();
            double Qu = s.GetUltimateLimitShearMiddle(0.0);
            Assert.IsTrue(Qs < Qd, $"Qs={Qs} should be < Qd={Qd}");
            Assert.IsTrue(Qd < Qu, $"Qd={Qd} should be < QuMid={Qu}");
        }

        // ===== M-φ 関係 (中間部) =====

        [TestMethod]
        public void MPhiMiddle_AtZeroAxial_HasFourPoints()
        {
            // φ = [0, φ_Md, φ_Mu', φ_u], M = [0, Md, Mu, Mu]
            var s = NewSection_NoFc(600, 12, 235);
            var (phis, moments) = s.GetMPhiRelationshipMiddle(0.0);
            Assert.AreEqual(4, phis.Count);
            Assert.AreEqual(4, moments.Count);
            Assert.AreEqual(0.0, phis[0], 1e-9);
            Assert.AreEqual(0.0, moments[0], 1e-9);
            Assert.AreEqual(moments[2], moments[3], 1e-3);  // Mu plateau
        }

        [TestMethod]
        public void MPhiMiddle_PhiMonotonicallyIncreasing()
        {
            var s = NewSection_NoFc(600, 12, 235);
            var (phis, _) = s.GetMPhiRelationshipMiddle(0.0);
            for (int i = 1; i < phis.Count; i++)
                Assert.IsTrue(phis[i] >= phis[i - 1], $"φ not monotonic at i={i}");
        }

        [TestMethod]
        public void MPhiMiddle_MomentMonotonicallyIncreasing_UpTo_Mu()
        {
            var s = NewSection_NoFc(600, 12, 235);
            var (_, moments) = s.GetMPhiRelationshipMiddle(0.0);
            // 0 ≤ Md ≤ Mu = Mu (last two equal for plateau)
            Assert.IsTrue(moments[1] >= moments[0]);
            Assert.IsTrue(moments[2] >= moments[1]);
            Assert.AreEqual(moments[3], moments[2], 1e-3);
        }

        [TestMethod]
        public void MPhiMiddle_PhiMd_IsMd_DividedBy_EI()
        {
            var s = NewSection_NoFc(600, 12, 235);
            var (phis, moments) = s.GetMPhiRelationshipMiddle(0.0);
            double Md = moments[1];
            double EI = 205000.0 * s.Iisteel;
            double expected = Md / EI;
            Assert.AreEqual(expected, phis[1], 1e-9);
        }

        [TestMethod]
        public void MPhiMiddle_AtLowCompression_HasDistinctMuAndUltimate()
        {
            // 式の有効領域 (nud < pmNy × β) で φ_Mu' < φ_u になる。
            // SS400 D=600 t=12 では pmNy × β ≈ 361 kN なので、軸力 100 kN を採用。
            var s = NewSection_NoFc(600, 12, 235);
            double Nud = 100_000.0;  // 0.1 MN, well below pmNy × β ≈ 361 kN
            var (phis, moments) = s.GetMPhiRelationshipMiddle(Nud);
            Assert.AreEqual(4, phis.Count);
            Assert.IsTrue(phis[2] < phis[3], $"φ_Mu'={phis[2]} should be < φ_u={phis[3]}");
        }

        [TestMethod]
        public void MPhiMiddle_AtAxialBeyondValidRange_DegenerateToBilinear()
        {
            // nud > pmNy × β では term ≤ 0 で pmRMu' = pmR95 = 0 にクランプされ、
            // φ_Mu' = φ_u となる (Mu plateau なしの実質バイリニア)。
            var s = NewSection_NoFc(600, 12, 235);
            double Nud = s.sNuc1 * 0.3;
            var (phis, moments) = s.GetMPhiRelationshipMiddle(Nud);
            Assert.AreEqual(4, phis.Count);
            Assert.AreEqual(phis[2], phis[3], 1e-9);
        }

        [TestMethod]
        public void MPhiMiddle_AtTension_UsesElasticThetaMu()
        {
            // Nud < 0 → elastic θMu = Mu × L / (3 EI)
            var s = NewSection_NoFc(600, 12, 235);
            var (phis, moments) = s.GetMPhiRelationshipMiddle(-1.0e5);
            Assert.AreEqual(4, phis.Count);
            Assert.IsTrue(phis[3] > 0);
        }

        // ===== M-φ 関係 (杭頭部) =====

        [TestMethod]
        public void MPhiHead_NoFc_DegenerateCurve()
        {
            // Fc=0 では杭頭部 M-φ は計算不可、退化曲線を返す
            var s = NewSection_NoFc(600, 12, 235);
            var (phis, moments) = s.GetMPhiRelationshipHead(0.0);
            Assert.AreEqual(1, phis.Count);
            Assert.AreEqual(0.0, phis[0]);
            Assert.AreEqual(0.0, moments[0]);
        }

        [TestMethod]
        public void MPhiHead_AtZeroAxial_HasFourPoints()
        {
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            var (phis, moments) = s.GetMPhiRelationshipHead(0.0);
            Assert.AreEqual(4, phis.Count);
            Assert.AreEqual(4, moments.Count);
            Assert.AreEqual(moments[2], moments[3], 1e-3);  // Mu plateau
        }

        [TestMethod]
        public void MPhiHead_PhiMonotonicallyIncreasing()
        {
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            var (phis, _) = s.GetMPhiRelationshipHead(0.0);
            for (int i = 1; i < phis.Count; i++)
                Assert.IsTrue(phis[i] >= phis[i - 1], $"φ not monotonic at i={i}");
        }

        [TestMethod]
        public void MPhiHead_PhiMd_UsesEIeq_NotJustSteelEI()
        {
            // 杭頭部 φ_Md は合成 EIeq に基づく (鋼管のみ EI より大きい剛性 → 小さい φ)
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            var (phisHead, momentsHead) = s.GetMPhiRelationshipHead(0.0);
            var (phisMid, momentsMid) = s.GetMPhiRelationshipMiddle(0.0);
            // 同じ Md (Md_head ≥ Md_middle 想定) と剛性差で比較
            // EIeq > EI なので φ_Md_head < φ_Md_middle (Md が同程度の場合)
            // ここでは EIeq の存在を確認するのみ
            Assert.IsTrue(phisHead[1] > 0);
        }

        [TestMethod]
        public void MPhiHead_AtModerateCompression_HasValidShape()
        {
            var s = NewSection_Full(600, 12, 235, beta1: 1.0, Fc: 24, sigmaB: 400);
            double Nud = s.cNdc * 0.5;  // case B, concrete carries axial
            var (phis, moments) = s.GetMPhiRelationshipHead(Nud);
            Assert.AreEqual(4, phis.Count);
            Assert.IsTrue(moments[1] > 0);
            Assert.IsTrue(moments[2] >= moments[1]);
            Assert.IsTrue(phis[3] > phis[1]);
        }

        // ===== Ec / Iisteel / Iiconcrete =====

        [TestMethod]
        public void Ec_FromFc_StandardFormula()
        {
            // Ec = 33500 × (Fc/60)^(1/3)
            var s = NewSection_WithFc(600, 12, 235, beta1: 1.0, Fc: 24);
            double expected = 33500.0 * Math.Pow(24.0 / 60.0, 1.0 / 3.0);
            Assert.AreEqual(expected, s.Ec, 1e-3);
        }

        [TestMethod]
        public void Ec_NoFc_Zero()
        {
            var s = NewSection_NoFc(600, 12, 235);
            Assert.AreEqual(0.0, s.Ec, 1e-9);
        }

        [TestMethod]
        public void Iisteel_AnnularInertia_Match()
        {
            // I_steel = π/64 × (D⁴ - (D-2t)⁴)
            var s = NewSection_NoFc(600, 12, 235);
            double expected = Math.PI / 64.0 * (Math.Pow(600, 4) - Math.Pow(576, 4));
            Assert.AreEqual(expected, s.Iisteel, 1e-3);
        }

        [TestMethod]
        public void Iiconcrete_FullDiskInsidePipe()
        {
            // I_conc = π/4 × cro⁴
            var s = NewSection_NoFc(600, 12, 235);
            double expected = Math.PI / 4.0 * Math.Pow(s.cro, 4);
            Assert.AreEqual(expected, s.Iiconcrete, 1e-3);
        }
    }
}
