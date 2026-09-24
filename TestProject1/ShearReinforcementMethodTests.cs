using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち RC 杭の高強度せん断補強筋の工法 (エムケーパイルリング785 / ウルボンスパイラル)。
    ///
    /// 工法を選ぶと<b>せん断耐力の算定式そのもの</b>が替わる。材料強度だけ差し替える
    /// 実装にすると、等価正方形断面の取り方 (b = (B/2)·√π と b = πD/4) の違いで
    /// 13% ずれたまま「それらしい」値が出て気づけない。そこで
    ///
    ///   1. 指針の検証用試験体の計算値と突き合わせる (式の形・b・j・dt・pt をまとめて縛る)
    ///   2. 頭打ち・適用範囲が実際に効いているかを、値を揺らして確かめる
    ///   3. 登録した工法が全部曲線を返すことを、工法の一覧から機械的に見張る
    ///
    /// の 3 段で網を張る。
    ///
    /// 文献値は「エムケーパイルリング785 設計施工指針・同解説 (2023年4月)」解表5.2
    /// 『杭径の検討用試験体の計算値と実験値』による。試験体の杭径は 400mm / 700mm で、
    /// 指針の適用範囲 (800〜2600mm) の外だが、指針自身が式の検証に使った値なので
    /// 算定式の照合にはこれが使える。
    /// </summary>
    [TestClass]
    public class ShearReinforcementMethodTests
    {
        private int _savedCase;

        [TestInitialize]
        public void SetUp()
        {
            // 工法の許容せん断応力度は告示1113(第8 第一号) の区分に依存する。
            // 静的オプションなので、退避してから既定の区分(1) に固定する。
            _savedCase = ConcreteModelOptions.Notification1113CompressionCase;
            ConcreteModelOptions.Notification1113CompressionCase = 1;
        }

        [TestCleanup]
        public void TearDown()
        {
            ConcreteModelOptions.Notification1113CompressionCase = _savedCase;
        }

        // ── 断面の作り方 ──────────────────────────────────────────────

        /// <summary>
        /// 指針の試験体に合わせた場所打ち RC 断面を作る。
        /// ConcreteOutDia → MainBarCenterCover の順に設定すること (かぶりから配筋径を作るため)。
        /// </summary>
        private static PileSection Section(
            string method, string hoopSize, double hoopSpacing,
            double pileDia, double fc, int mainBarNum, string mainBarSize, double mainBarCover)
            => new()
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
                ConcreteOutDia = pileDia,
                PileDiameter = pileDia,
                ConcreteFc = fc,
                // ξ は工法の式には現れない (指針は Fc をそのまま使う)。
                // 曲線の軸力の掃引範囲にだけ効くので 1.0 で固定する。
                ConcreteGsi = 1.0,
                MainBarNum = mainBarNum,
                MainBarSize = mainBarSize,
                MainBarSpec = "SD390",
                MainBarCenterCover = mainBarCover,
                HoopMethod = method,
                HoopSize = hoopSize,
                HoopSpacing = hoopSpacing,
                HoopCenterCover = mainBarCover,
            };

        private static InsituReinforcedConcreteSection Calc(PileSection s)
        {
            var calc = s.CreateSectionCalculator() as InsituReinforcedConcreteSection;
            Assert.IsNotNull(calc, "場所打ち RC の断面計算が作れません");
            return calc!;
        }

        /// <summary>
        /// せん断補強筋比が <paramref name="pw"/> ちょうどになる間隔 (mm)。
        /// pw = 2·aw/(b·x)、b = (B/2)·√π。文献値と突き合わせるので端数のまま使う。
        /// </summary>
        private static double SpacingFor(double pw, double barArea, double pileDia)
            => 2.0 * barArea / (pileDia / 2.0 * Math.Sqrt(Math.PI)) / pw;

        /// <summary>
        /// Q-N 曲線から軸力 <paramref name="n"/> [N] のせん断力 [N] を線形補間で引く。
        /// 工法の式は N に対して一次なので、掃引点の間を直線で結べば厳密に一致する。
        /// </summary>
        private static double ShearAt((List<double> Qs, List<double> Ns) curve, double n)
        {
            var (qs, ns) = curve;
            Assert.IsTrue(ns.Count >= 2, "曲線の点が足りません");
            Assert.IsTrue(n >= ns.Min() && n <= ns.Max(),
                $"軸力 {n:N0} N が曲線の範囲 ({ns.Min():N0}〜{ns.Max():N0}) の外です");
            for (int i = 1; i < ns.Count; i++)
            {
                if (n > ns[i]) continue;
                double span = ns[i] - ns[i - 1];
                if (Math.Abs(span) < 1e-9) return qs[i];
                double t = (n - ns[i - 1]) / span;
                return qs[i - 1] + t * (qs[i] - qs[i - 1]);
            }
            return qs[^1];
        }

        private static void AssertClose(double expectedKn, double actualN, double tolPercent, string what)
        {
            double actualKn = actualN / 1000.0;
            double diff = Math.Abs(actualKn - expectedKn) / expectedKn * 100.0;
            Assert.IsTrue(diff <= tolPercent,
                $"{what}: 文献 {expectedKn:N1} kN に対しプログラム {actualKn:N2} kN (差 {diff:N2}%、許容 {tolPercent}%)");
        }

        // ── 1. 指針の検証用試験体との突合 ────────────────────────────────

        /// <summary>
        /// エムケーパイルリング785 解表5.2 の試験体 No.4 (杭径 400mm、σB=29.8N/mm²)。
        ///
        /// 使用限界 QAL = Lfs·Ac/κ、損傷限界 QAs = sfs·Ac/κ はどちらも軸力にも
        /// せん断補強筋にも依らない。ここが合えば Ac と κ=4/3 と告示1113 の fs が合っている。
        /// </summary>
        [TestMethod]
        public void MkPileRing785_ServiceAndDamageShear_MatchGuidelineTableNo4()
        {
            var s = Section(ShearReinforcementMethods.MkPileRing785, "MD10", 200.0,
                pileDia: 400.0, fc: 29.8, mainBarNum: 20, mainBarSize: "D13", mainBarCover: 40.0);
            var calc = Calc(s);

            // 解表5.2 No.4: QAL = 55.7 kN、QAS = 83.6 kN
            AssertClose(55.7, calc.GetServiceLimitQNInteraction(2.54, true).Item1[0], 0.3, "使用限界せん断力 QAL");
            AssertClose(83.6, calc.GetDamageLimitQNInteraction(2.54, true).Item1[0], 0.3, "損傷限界せん断力 QAs");
        }

        /// <summary>
        /// 同じく解表5.2 の試験体 No.12 (杭径 700mm、σB=36.6N/mm²)。
        /// 杭径が変わっても Ac と κ の扱いが合っていることを見る。
        /// </summary>
        [TestMethod]
        public void MkPileRing785_ServiceAndDamageShear_MatchGuidelineTableNo12()
        {
            var s = Section(ShearReinforcementMethods.MkPileRing785, "MD10", 114.0,
                pileDia: 700.0, fc: 36.6, mainBarNum: 20, mainBarSize: "D25", mainBarCover: 40.0);
            var calc = Calc(s);

            // 解表5.2 No.12: QAL = 185.3 kN、QAS = 278.0 kN
            AssertClose(185.3, calc.GetServiceLimitQNInteraction(2.54, true).Item1[0], 0.3, "使用限界せん断力 QAL");
            AssertClose(278.0, calc.GetDamageLimitQNInteraction(2.54, true).Item1[0], 0.3, "損傷限界せん断力 QAs");
        }

        /// <summary>
        /// 終局限界せん断力 (4.1式、大野・荒川 min 式)。
        ///
        /// 解表5.2 No.4 の QSU = 235.7 kN は<b>実験の実測 σwy = 834 N/mm²</b> で算定した値で、
        /// 寸法効果の低減係数 β=0.9 を掛ける前の値である (解表4.1 の 211 kN は β を掛けた後)。
        /// 実装は指針が定める設計値 σwy = 785 N/mm² を使うので、第2項だけが小さくなり
        ///
        ///   235.766 − 0.85·(√(0.002·834) − √(0.002·785))·b·j / 1000 = 232.572 kN
        ///
        /// になる。低減前がこの値、低減後はその 0.9 倍であることを固定する。
        /// b·j・dt・pt (＝全主筋本数/4+1 本を引張鉄筋とする置換) がどれか 1 つでも
        /// ずれるとこの値にはならない。
        /// </summary>
        [TestMethod]
        public void MkPileRing785_UltimateShear_FollowsGuidelineFormula()
        {
            const double pileDia = 400.0, fc = 29.8;
            double spacing = SpacingFor(0.002, 71.33, pileDia);   // MD10 で pw = 0.20% ちょうど
            var s = Section(ShearReinforcementMethods.MkPileRing785, "MD10", spacing,
                pileDia, fc, mainBarNum: 20, mainBarSize: "D13", mainBarCover: 40.0);
            Assert.AreEqual(0.002, s.HoopPw, 1e-9, "せん断補強筋比 pw");

            var calc = Calc(s);
            // 解表5.2 の試験体は軸力比 η = 0.15 → σ0 = 0.15·σB
            double n = 0.15 * fc * (Math.PI * pileDia * pileDia / 4.0);

            AssertClose(232.572, ShearAt(calc.GetUltimateQNInteraction(2.54, s.HoopPw, s.HoopSigmay, false), n),
                0.05, "安全限界せん断力 (低減前、β なし)");
            AssertClose(232.572 * 0.9, ShearAt(calc.GetUltimateQNInteraction(2.54, s.HoopPw, s.HoopSigmay, true), n),
                0.05, "安全限界せん断力 (低減後、β=0.9)");
        }

        /// <summary>
        /// ウルボンの短期 (②式) はせん断補強筋の項を持つ。
        /// QAS = b·j·{ sfs + 0.5·wft·(pw − 0.001) }。
        ///
        /// 期待値はエムケーパイルリング785 解表5.2 No.4 の QA = 115.1 kN。
        /// 両工法のこの式は同形 (RC規準 (15.6) 式の第2項を (pw−0.001) にしたもの) で
        /// wft も同じ 590 N/mm² なので、同じ諸元なら同じ値になる。
        /// ここが合えば b·j と告示1113 の短期 fs が合っている。
        /// </summary>
        [TestMethod]
        public void UlbonSpiral_DamageShear_IncludesHoopTerm()
        {
            const double pileDia = 400.0, fc = 29.8;
            double spacing = SpacingFor(0.002, 63.6, pileDia);    // U9.0 で pw = 0.20% ちょうど
            var s = Section(ShearReinforcementMethods.UlbonSpiral, "U9.0", spacing,
                pileDia, fc, mainBarNum: 20, mainBarSize: "D13", mainBarCover: 40.0);
            Assert.AreEqual(0.002, s.HoopPw, 1e-9, "せん断補強筋比 pw");

            var calc = Calc(s);
            AssertClose(115.1, calc.GetDamageLimitQNInteraction(2.54, true).Item1[0], 0.3,
                "損傷限界せん断力 (ウルボン ②式)");

            // 補強筋を増やせば損傷限界も上がる (標準の式では上がらない)。
            var thicker = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6", spacing,
                pileDia, fc, 20, "D13", 40.0);
            Assert.IsTrue(Calc(thicker).GetDamageLimitQNInteraction(2.54, true).Item1[0]
                        > calc.GetDamageLimitQNInteraction(2.54, true).Item1[0],
                "ウルボンの損傷限界は補強筋量に応じて増えるはずです");
        }

        // ── 1b. 指針が「どちらかによる」とする式の選択 ─────────────────

        /// <summary>
        /// エムケーパイルリング785 の (3.3) 式「安全性確保のための短期許容せん断力」。
        ///
        /// (3.2) 式はコンクリートだけの式で、せん断補強筋が一次設計に効かない。
        /// (3.3) 式を選ぶと補強筋の項 0.5·wft·(pw − 0.001) が入る代わりに、
        /// <b>設計用せん断力を水平荷重時の 1.5 倍に割り増す</b>のが指針の前提になる。
        /// 耐力だけ上げて応答をそのままにすると、指針より緩い検定になる。
        ///
        /// 期待値は解表5.2 No.4 の QA = 115.1 kN。
        /// </summary>
        [TestMethod]
        public void MkPileRing785_SafetyShortTermFormula_AddsHoopTermAndRequires1_5xDesignShear()
        {
            const double pileDia = 400.0, fc = 29.8;
            double spacing = SpacingFor(0.002, 71.33, pileDia);
            var damageLimit = Section(ShearReinforcementMethods.MkPileRing785, "MD10", spacing,
                pileDia, fc, 20, "D13", 40.0);
            var safety = Section(ShearReinforcementMethods.MkPileRing785, "MD10", spacing,
                pileDia, fc, 20, "D13", 40.0);
            safety.HoopDamageFormula = ShearReinforcementMethods.DamageFormulaSafetyShortTerm;

            // (3.2) 式は補強筋が効かない / (3.3) 式は効く
            double q32 = Calc(damageLimit).GetDamageLimitQNInteraction(2.54, true).Item1[0];
            double q33 = Calc(safety).GetDamageLimitQNInteraction(2.54, true).Item1[0];
            AssertClose(83.6, q32, 0.3, "損傷限界せん断力 QAs (3.2式)");
            AssertClose(115.1, q33, 0.3, "安全性確保のための短期許容せん断力 QA (3.3式)");
            Assert.IsTrue(q33 > q32, "(3.3) 式が (3.2) 式を上回っていません");

            // 割増は式とセット。片方だけ効くと検定が指針より緩くなる
            Assert.AreEqual(1.0, damageLimit.ShearDesignMagnification, 1e-12, "(3.2) 式では割増なし");
            Assert.AreEqual(1.5, safety.ShearDesignMagnification, 1e-12, "(3.3) 式では 1.5 倍");

            // 工法を外したら割増も式も既定へ戻ること
            safety.HoopMethod = ShearReinforcementMethods.Standard;
            Assert.AreEqual(1.0, safety.ShearDesignMagnification, 1e-12, "標準に戻して割増が残っています");
            Assert.AreEqual(ShearReinforcementMethods.DamageFormulaDamageLimit, safety.HoopDamageFormula);
        }

        /// <summary>
        /// 割増が検定の応答側へ実際に渡っていること。
        ///
        /// 係数を用意しただけで掛け忘れても、曲線も判定も「それらしい」まま通る。
        /// 検定を丸ごと動かす代わりに、応答を作る場所が係数を読んでいることを走査で固定する。
        /// </summary>
        [TestMethod]
        public void ShearDesignMagnification_IsUsedByTheEvaluation()
        {
            string source = File.ReadAllText(Path.Combine(
                TestSource.Root(), "Graphics_r1", "ViewModels", "EvaluationService.cs"));

            StringAssert.Contains(source, nameof(PileSection.ShearDesignMagnification),
                "検定が設計用せん断力の割増係数を読んでいません（掛け忘れても静かに通ります）");
            StringAssert.Contains(source, "LimitState.Damage ? section.ShearDesignMagnification",
                "割増が損傷限界せん断だけに掛かる形になっていません");
        }

        /// <summary>
        /// ウルボンの ④ 式（トラス・アーチ機構）。アーチ項は安全側に 0 として算定する。
        ///
        ///   Qsu2 = b·jt·pw·σwy （上限 (ν·Fc/3)·b·jt）、ν = 0.7 − Fc/200、jt = b − 2·dt
        ///
        /// 杭径 1000mm・Fc30・dt150・pw 0.3% で
        ///   b = 886.2269、jt = 586.2269、pw·σwy = 3.825 → 1987.20 kN
        /// ③ 式と違い<b>軸力の項を持たない</b>ので、曲線は N によらず一定になる。
        /// </summary>
        [TestMethod]
        public void UlbonSpiral_TrussArchFormula_UsesTrussTermOnly()
        {
            const double pileDia = 1000.0, fc = 30.0;
            double spacing = SpacingFor(0.003, 124.7, pileDia);   // U12.6 で pw = 0.30% ちょうど
            var s = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6", spacing,
                pileDia, fc, 20, "D25", 150.0);
            s.HoopUltimateFormula = ShearReinforcementMethods.UltimateFormulaTrussArch;
            Assert.AreEqual(0.003, s.HoopPw, 1e-9, "せん断補強筋比 pw");

            var curve = Calc(s).GetUltimateQNInteraction(2.0, s.HoopPw, s.HoopSigmay, true);
            AssertClose(1987.2026, curve.Item1[0], 0.01, "終局せん断強度 (④式、アーチ項 0)");

            // 軸力の項が無いので、曲線は端から端まで同じ高さ
            Assert.AreEqual(curve.Item1.Min(), curve.Item1.Max(), 1e-6,
                "④式は軸力に依らないはずが、曲線が傾いています");

            // ③式 は軸力で増える (2 つの式が取り違えられていないことの裏取り)
            var arakawa = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6", spacing,
                pileDia, fc, 20, "D25", 150.0);
            var arakawaCurve = Calc(arakawa).GetUltimateQNInteraction(2.0, arakawa.HoopPw, arakawa.HoopSigmay, true);
            Assert.IsTrue(arakawaCurve.Item1.Max() > arakawaCurve.Item1.Min() * 1.05,
                "③式が軸力で変わっていません");
        }

        /// <summary>
        /// ③ 式と ④ 式は、せん断補強筋比で有利・不利が入れ替わる。
        ///
        /// ④ 式のトラス項は pw·σwy に比例し、③ 式の補強筋項は √(pw·σwy) にしか比例しない。
        /// pw が小さいと ③ が有利（④ にはコンクリートの項が無い）、大きいと ④ が有利になる。
        /// どちらを使うかは指針が設計者の選択としているので、プログラムが勝手に
        /// 大きい方を採っていないこと（選んだ式がそのまま効くこと）も確かめる。
        /// </summary>
        [TestMethod]
        public void UlbonSpiral_FormulaChoiceChangesWhichIsFavourable()
        {
            const double pileDia = 1000.0, fc = 30.0;
            double Ultimate(string formula, double pw)
            {
                var s = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6",
                    SpacingFor(pw, 124.7, pileDia), pileDia, fc, 20, "D25", 150.0);
                s.HoopUltimateFormula = formula;
                return Calc(s).GetUltimateQNInteraction(2.0, s.HoopPw, s.HoopSigmay, true).Item1[0];
            }

            double lowArakawa = Ultimate(ShearReinforcementMethods.UltimateFormulaArakawa, 0.001);
            double lowTruss = Ultimate(ShearReinforcementMethods.UltimateFormulaTrussArch, 0.001);
            double highArakawa = Ultimate(ShearReinforcementMethods.UltimateFormulaArakawa, 0.003);
            double highTruss = Ultimate(ShearReinforcementMethods.UltimateFormulaTrussArch, 0.003);

            Assert.IsTrue(lowTruss < lowArakawa,
                $"pw=0.1% では ③式 が有利なはず (③={lowArakawa / 1000:N0} ④={lowTruss / 1000:N0} kN)");
            Assert.IsTrue(highTruss > highArakawa,
                $"pw=0.3% では ④式 が有利なはず (③={highArakawa / 1000:N0} ④={highTruss / 1000:N0} kN)");
        }

        /// <summary>
        /// 算定式の選択肢は工法ごとに決まり、選べない式が残らないこと。
        /// </summary>
        [TestMethod]
        public void FormulaOptions_MatchWhatEachGuidelineOffers()
        {
            var std = Section(ShearReinforcementMethods.Standard, "D13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);
            Assert.IsFalse(std.IsHoopDamageFormulaSelectable, "標準では損傷限界の式は選べない");
            Assert.IsFalse(std.IsHoopUltimateFormulaSelectable, "標準では終局の式は選べない");

            var mk = Section(ShearReinforcementMethods.MkPileRing785, "MD13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);
            Assert.IsTrue(mk.IsHoopDamageFormulaSelectable, "エムケーは 3.2式 と 3.3式 から選べる");
            Assert.IsFalse(mk.IsHoopUltimateFormulaSelectable, "エムケーの終局は 4.1式 のみ");

            var ulbon = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6", 150.0, 1000.0, 30.0, 20, "D25", 150.0);
            Assert.IsFalse(ulbon.IsHoopDamageFormulaSelectable, "ウルボンの短期は ②式 のみ");
            Assert.IsTrue(ulbon.IsHoopUltimateFormulaSelectable, "ウルボンの終局は ③式 と ④式 から選べる");

            // 工法を替えたとき、前の工法でしか選べない式が残らないこと
            ulbon.HoopUltimateFormula = ShearReinforcementMethods.UltimateFormulaTrussArch;
            ulbon.HoopMethod = ShearReinforcementMethods.MkPileRing785;
            Assert.AreEqual(ShearReinforcementMethods.UltimateFormulaArakawa, ulbon.HoopUltimateFormula,
                "エムケーに切り替えてもトラス・アーチ式が残っています");
        }

        // ── 2. 頭打ちと適用範囲 ───────────────────────────────────────

        /// <summary>
        /// 終局の pw は工法ごとの上限で頭打ちになる (エムケー 0.4% / ウルボン 0.3%)。
        ///
        /// 頭打ちを入れ忘れても曲線は滑らかに伸びるだけで、例外にも段差にもならない。
        /// 上限より少し下と、上限を大きく超えた配筋で<b>同じ値</b>になることを見る。
        /// </summary>
        [TestMethod]
        public void UltimateShear_IsCappedByGuidelinePw()
        {
            foreach (var name in new[] { ShearReinforcementMethods.MkPileRing785, ShearReinforcementMethods.UlbonSpiral })
            {
                var spec = ShearReinforcementMethods.Get(name);
                Assert.IsNotNull(spec);
                string size = spec!.DefaultBarSize;
                double area = spec.BarArea(size);
                const double pileDia = 1000.0, fc = 30.0;

                var atCap = Section(name, size, SpacingFor(spec.UltimatePwCap, area, pileDia),
                    pileDia, fc, 20, "D25", 150.0);
                var overCap = Section(name, size, SpacingFor(spec.UltimatePwCap * 2.0, area, pileDia),
                    pileDia, fc, 20, "D25", 150.0);

                double n = 0.1 * fc * (Math.PI * pileDia * pileDia / 4.0);
                double qAtCap = ShearAt(Calc(atCap).GetUltimateQNInteraction(2.0, atCap.HoopPw, atCap.HoopSigmay, true), n);
                double qOverCap = ShearAt(Calc(overCap).GetUltimateQNInteraction(2.0, overCap.HoopPw, overCap.HoopSigmay, true), n);

                Assert.AreEqual(qAtCap, qOverCap, qAtCap * 1e-9,
                    $"{name}: 終局の pw が上限 {spec.UltimatePwCap * 100:N1}% で頭打ちになっていません");

                // 上限より下では効く (頭打ちを「常に上限」と書き間違えると、ここが落ちる)
                var underCap = Section(name, size, SpacingFor(spec.UltimatePwCap * 0.5, area, pileDia),
                    pileDia, fc, 20, "D25", 150.0);
                double qUnderCap = ShearAt(Calc(underCap).GetUltimateQNInteraction(2.0, underCap.HoopPw, underCap.HoopSigmay, true), n);
                Assert.IsTrue(qUnderCap < qAtCap,
                    $"{name}: 上限未満のせん断補強筋比が耐力に効いていません");
            }
        }

        /// <summary>
        /// 工法を選ぶと、せん断補強筋比 pw と有効せい d の<b>定義</b>が替わる。
        ///
        /// 等価な幅 b が πD/4 (標準) から (B/2)·√π (工法) に替わるので、
        /// 同じ配筋でも pw は πD/4 ÷ (B/2)·√π = 0.886 倍になる。
        /// ここを替え忘れると、pw も M/(Q·d) も別の断面の値のまま式に入る。
        /// </summary>
        [TestMethod]
        public void SelectingMethod_SwitchesEquivalentSectionDefinition()
        {
            const double pileDia = 1000.0;
            var std = Section(ShearReinforcementMethods.Standard, "D13", 150.0, pileDia, 30.0, 20, "D25", 150.0);
            var mk = Section(ShearReinforcementMethods.MkPileRing785, "MD13", 150.0, pileDia, 30.0, 20, "D25", 150.0);

            double expectedRatio = (Math.PI / 4.0) / (Math.Sqrt(Math.PI) / 2.0);   // = 0.8862
            // 呼び名が違うので断面積で正規化してから比べる
            double stdPwPerArea = std.HoopPw / std.HoopBarArea;
            double mkPwPerArea = mk.HoopPw / mk.HoopBarArea;
            Assert.AreEqual(expectedRatio, mkPwPerArea / stdPwPerArea, 1e-9,
                "工法の pw が等価正方形断面の幅で作られていません");

            Assert.AreEqual(pileDia * 0.9, std.EffectiveDepth, 1e-9, "標準の有効せい d = 0.9D");
            Assert.AreEqual(pileDia / 2.0 * Math.Sqrt(Math.PI) - 150.0, mk.EffectiveDepth, 1e-9,
                "工法の有効せい d = (B/2)·√π − dt");
        }

        /// <summary>
        /// 工法の σwy は指針が決めた値で、画面で選んだ規格には依らない。
        /// </summary>
        [TestMethod]
        public void MethodFixesHoopYieldStrength()
        {
            var mk = Section(ShearReinforcementMethods.MkPileRing785, "MD13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);
            var ulbon = Section(ShearReinforcementMethods.UlbonSpiral, "U12.6", 150.0, 1000.0, 30.0, 20, "D25", 150.0);

            Assert.AreEqual(785.0, mk.HoopSigmay, 1e-9, "MK785 の終局用 σwy");
            Assert.AreEqual(1275.0, ulbon.HoopSigmay, 1e-9, "ウルボンの規格降伏点 σwy");
            Assert.AreEqual(590.0, mk.HoopWft, 1e-9, "MK785 の短期許容引張応力度 wft");
            Assert.AreEqual(590.0, ulbon.HoopWft, 1e-9, "ウルボンの短期許容引張応力度 wft");

            // 規格の選択肢は工法で 1 つに決まり、画面から別の値を選べない
            CollectionAssert.AreEqual(new[] { "MK785" }, mk.HoopSpecOption);
            CollectionAssert.AreEqual(new[] { "SBPD1275/1420" }, ulbon.HoopSpecOption);
            Assert.IsFalse(mk.IsStandardHoopMethod);
        }

        /// <summary>
        /// 工法を切り替えても、呼び名と規格が選択肢の外に取り残されないこと。
        ///
        /// ComboBox は選択肢を差し替えた瞬間に、候補に無くなった現在値を null で書き戻す。
        /// 受けると工法や呼び名が消えて、式が静かに標準へ戻る。
        /// 選択肢・現在値・null の書き戻しの 3 つをまとめて確かめる。
        /// </summary>
        [TestMethod]
        public void SwitchingMethod_KeepsSizeAndGradeInsideTheNewOptions()
        {
            var s = Section(ShearReinforcementMethods.Standard, "D13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);

            foreach (string name in ShearReinforcementMethods.Options.Concat(ShearReinforcementMethods.Options))
            {
                s.HoopMethod = name;
                Assert.AreEqual(name, s.HoopMethod, $"{name}: 工法が入っていません");
                CollectionAssert.Contains(s.HoopSizeOption, s.HoopSize, $"{name}: 呼び名が選択肢の外です");
                CollectionAssert.Contains(s.HoopSpecOption, s.HoopSpec, $"{name}: 規格が選択肢の外です");
                Assert.IsTrue(s.HoopBarArea > 0, $"{name}: 呼び名の断面積が引けていません");
            }

            // null / 空文字の書き戻しは無視する
            string method = s.HoopMethod, size = s.HoopSize, grade = s.HoopSpec;
            s.HoopMethod = null!;
            s.HoopSize = null!;
            s.HoopSpec = "";
            Assert.AreEqual(method, s.HoopMethod, "工法が null で消えました");
            Assert.AreEqual(size, s.HoopSize, "呼び名が null で消えました");
            Assert.AreEqual(grade, s.HoopSpec, "規格が空文字で消えました");

            // 控え (キャンセル用) にも工法が乗ること
            Assert.AreEqual(s.HoopMethod, s.DeepCopy().HoopMethod, "DeepCopy が工法を落としています");
        }

        /// <summary>
        /// 読み込みで工法と呼び名が食い違っても、せん断補強筋比が 0 にならないこと。
        ///
        /// 保存ファイルはプロパティが書かれた順に設定されるので、工法より後に呼び名が来ると
        /// 工法の表に無い呼び名（「標準」の D13 など）が残りうる。公称断面積が引けないと
        /// pw が 0 になり、安全限界せん断の √(pw·σwy) から補強筋の項が黙って消える。
        /// 読み終わりの処理で工法側の既定へ寄せる。
        /// </summary>
        [TestMethod]
        public void LoadingMismatchedSizeAndMethod_DoesNotLeavePwAtZero()
        {
            var s = Section(ShearReinforcementMethods.MkPileRing785, "MD13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);

            // 保存ファイルの読み込みで起きうる食い違いを作る。
            // 読み込みの最中でなければ、選択肢の外の値は setter が拒否する (SetterRejectsValuesOutsideTheOptions)
            s.OnDeserializingHandler(default);
            s.HoopSize = "D13";
            s.HoopSpec = "SD295";
            CollectionAssert.DoesNotContain(s.HoopSizeOption, s.HoopSize,
                "前提: 呼び名が工法の選択肢の外にある状態を作れていません");

            s.OnDeserializedHandler(default);

            Assert.AreEqual("MD10", s.HoopSize, "工法の既定の呼び名へ寄せていません");
            Assert.AreEqual("MK785", s.HoopSpec, "工法の材料規格へ寄せていません");
            Assert.IsTrue(s.HoopPw > 0, "せん断補強筋比が 0 のままです");
        }

        /// <summary>
        /// 知らない規格名でも降伏点を 0 にしない。
        ///
        /// 0 が返ると安全限界せん断の √(pw·σwy) から補強筋の項が丸ごと消え、
        /// 例外にも警告にもならないまま耐力だけが静かに下がる。以前の実装がこれだった。
        /// </summary>
        [TestMethod]
        public void UnknownHoopGrade_DoesNotSilentlyBecomeZero()
        {
            var s = Section(ShearReinforcementMethods.Standard, "D13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);
            // 表に無い規格は、画面やプログラムからは入れられない (setter が拒否する)。
            // 入りうるのは古い保存ファイルの読み込みの途中なので、その状態を作る
            s.OnDeserializingHandler(default);
            s.HoopSpec = "SD1234";      // 表に無い規格
            Assert.IsTrue(s.HoopSigmay > 0, "知らない規格で σwy が 0 になっています");
        }

        /// <summary>
        /// 工法・呼び名・規格・算定式は、いまの工法で選べない値を setter が拒否すること。
        ///
        /// 以前は読み込みのあとにだけ整合を取っていて、setter は何でも受け入れた。画面の外から入れると、
        /// 表示は入れた値なのに耐力は別の式で計算される、という食い違いが起きえた。
        /// </summary>
        [TestMethod]
        public void SetterRejectsValuesOutsideTheOptions()
        {
            var s = Section(ShearReinforcementMethods.Standard, "D13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);

            AssertRejected(() => s.HoopMethod = "知らない工法", "せん断補強筋の工法");
            AssertRejected(() => s.HoopSize = "MD13", "せん断補強筋の呼び名");         // 工法の呼び名は標準では選べない
            AssertRejected(() => s.HoopSpec = "MK785", "せん断補強筋の規格");
            AssertRejected(() => s.HoopDamageFormula = ShearReinforcementMethods.DamageFormulaSafetyShortTerm, "損傷限界の算定式");
            AssertRejected(() => s.HoopUltimateFormula = ShearReinforcementMethods.UltimateFormulaTrussArch, "終局の算定式");

            Assert.AreEqual(ShearReinforcementMethods.Standard, s.HoopMethod, "拒否したのに工法が変わりました");
            Assert.AreEqual("D13", s.HoopSize, "拒否したのに呼び名が変わりました");
            Assert.AreEqual(ShearReinforcementMethods.DamageFormulaDamageLimit, s.HoopDamageFormula);

            // 工法を選べば、その工法の値は入る
            s.HoopMethod = ShearReinforcementMethods.MkPileRing785;
            s.HoopSize = "MD13";
            s.HoopDamageFormula = ShearReinforcementMethods.DamageFormulaSafetyShortTerm;
            Assert.AreEqual("MD13", s.HoopSize);
            Assert.AreEqual(ShearReinforcementMethods.DamageFormulaSafetyShortTerm, s.HoopDamageFormula);

            static void AssertRejected(Action set, string what)
            {
                var ex = Assert.ThrowsException<ArgumentException>(set, $"{what}: 選択肢の外の値が入りました");
                StringAssert.Contains(ex.Message, what, "どの項目の値が悪いのかが示されていません");
                StringAssert.Contains(ex.Message, "選べるのは", "何なら選べるのかが示されていません");
            }
        }

        /// <summary>読み込みの最中は受け入れ、読み終わりで工法に合わせて寄せ直すこと (工法より前に呼び名が来るファイル)。</summary>
        [TestMethod]
        public void DuringLoadingTheOrderOfPropertiesDoesNotMatter()
        {
            var s = Section(ShearReinforcementMethods.Standard, "D13", 150.0, 1000.0, 30.0, 20, "D25", 150.0);

            s.OnDeserializingHandler(default);
            s.HoopSize = "MD13";                                         // 工法より前に呼び名が来る
            s.HoopMethod = ShearReinforcementMethods.MkPileRing785;
            s.HoopSize = "MD13";
            s.OnDeserializedHandler(default);

            Assert.AreEqual(ShearReinforcementMethods.MkPileRing785, s.HoopMethod);
            Assert.AreEqual("MD13", s.HoopSize, "読み込みの途中で、正しい呼び名が捨てられました");
        }

        // ── 3. 登録した工法を取りこぼさない ───────────────────────────────

        /// <summary>
        /// 一覧に載っている工法は、すべて 3 つの限界状態の曲線を返すこと。
        ///
        /// 工法を足したのに断面側の分岐や呼び名の表を足し忘れると、
        /// 画面には出るのに耐力だけ標準の式のまま、という壊れ方をする。
        /// 一覧を手で書き写さず <see cref="ShearReinforcementMethods.Options"/> から回す。
        /// </summary>
        [TestMethod]
        public void EveryRegisteredMethod_ProducesShearCurves()
        {
            int scanned = 0;
            foreach (string name in ShearReinforcementMethods.Options)
            {
                var spec = ShearReinforcementMethods.Get(name);
                string size = spec?.DefaultBarSize ?? "D13";
                var s = Section(name, size, 150.0, 1000.0, 30.0, 20, "D25", 150.0);

                Assert.AreEqual(name, s.HoopMethod, $"{name}: 工法が設定できていません");
                CollectionAssert.Contains(s.HoopSizeOption, s.HoopSize,
                    $"{name}: 呼び名が工法の選択肢の中にありません");
                Assert.IsTrue(s.HoopPw > 0, $"{name}: せん断補強筋比が 0 です (呼び名の断面積が引けていない)");
                Assert.IsTrue(s.HoopSigmay > 0, $"{name}: せん断補強筋の降伏点が 0 です");

                var calc = Calc(s);
                foreach (var (label, curve) in new (string, (List<double> Qs, List<double> Ns))[]
                {
                    ("使用限界", calc.GetServiceLimitQNInteraction(2.0, true)),
                    ("損傷限界", calc.GetDamageLimitQNInteraction(2.0, true)),
                    ("安全限界", calc.GetUltimateQNInteraction(2.0, s.HoopPw, s.HoopSigmay, true)),
                })
                {
                    Assert.IsTrue(curve.Qs.Count > 0, $"{name} の{label}曲線が空です");
                    Assert.IsTrue(curve.Qs.All(q => double.IsFinite(q) && q > 0),
                        $"{name} の{label}曲線に 0 以下か非数の点があります");
                }

                // 工法では 3 つの限界状態が順に大きくなる
                double qService = calc.GetServiceLimitQNInteraction(2.0, true).Item1[0];
                double qDamage = calc.GetDamageLimitQNInteraction(2.0, true).Item1[0];
                Assert.IsTrue(qDamage > qService,
                    $"{name}: 損傷限界が使用限界を上回っていません ({qDamage:N0} ≦ {qService:N0})");
                scanned++;
            }
            Assert.AreEqual(ShearReinforcementMethods.Options.Length, scanned, "工法を全部見ていません");
            Assert.IsTrue(scanned >= 3, "工法の一覧が空か、減っています");
        }

        /// <summary>
        /// 計算書の設計条件に、どの区間がどの工法かと「式が替わる」ことが載ること。
        ///
        /// 工法を選ぶと算定式が替わるのに、計算書が既定の式の説明だけを載せていると、
        /// 使っていない式が根拠として残る。工法を使う区間があるときだけ行を出す
        /// (使っていないモデルで仮定一覧を無用に伸ばさない)。
        /// </summary>
        [TestMethod]
        public void Report_ListsHoopMethodPerSegment_OnlyWhenUsed()
        {
            static InputModel Model(string method, string hoopSize)
            {
                // PileBodies も PileBodySegments も既定で null なので、入れ物から用意する。
                var input = new InputModel
                {
                    PileBodies = new System.Collections.ObjectModel.ObservableCollection<PileBodyInput>(),
                };
                // PileBodySegments は既定で null。空のコレクションを入れてから区間を足す
                // (足した時点で PileBodyType の転記と ResetSectionProperties が走る)。
                var body = new PileBodyInput
                {
                    PileBodyRef = "P1",
                    PileBodyType = PileTypeNames.InsituRc,
                    PileBodySegments = new System.Collections.ObjectModel.ObservableCollection<PileBodySegment>(),
                };
                body.PileBodySegments.Add(new PileBodySegment
                {
                    PileSection = Section(method, hoopSize, 150.0, 1200.0, 30.0, 20, "D25", 150.0),
                });
                input.PileBodies.Add(body);
                return input;
            }

            var rows = PileDesign.Output.WordDocument.BuildDesignConditionRows(
                Model(ShearReinforcementMethods.MkPileRing785, "MD13"));
            var row = rows.Single(r => r.Item.Contains("せん断補強筋の工法"));
            StringAssert.Contains(row.Value, ShearReinforcementMethods.MkPileRing785);
            StringAssert.Contains(row.Value, "MD13");
            StringAssert.Contains(row.Note, "工法の設計施工指針の式");
            StringAssert.Contains(row.Note, "GBRC性能証明 第23-01号");

            var standardRows = PileDesign.Output.WordDocument.BuildDesignConditionRows(
                Model(ShearReinforcementMethods.Standard, "D13"));
            Assert.IsFalse(standardRows.Any(r => r.Item.Contains("せん断補強筋の工法")),
                "工法を使っていないモデルでは行を出さないこと");

            // 既定の式の説明にも「工法を指定した区間は別の式による」と断りが要る。
            // この行だけ読んで全杭に当てはめられると誤るため。
            var materialRows = PileDesign.Output.WordDocument.BuildMaterialOptionRows();
            var shearRow = materialRows.Single(r => r.Item.Contains("許容せん断"));
            StringAssert.Contains(shearRow.Note, "工法を指定した区間");
        }

        /// <summary>
        /// 「標準」では従来の式のまま (工法の分岐が既定の経路に染み出していないこと)。
        /// 期待値は <c>InsituRcShearQNTests</c> と同じ断面の実測値。
        /// </summary>
        [TestMethod]
        public void StandardMethod_KeepsExistingFormula()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
                ConcreteOutDia = 1000.0,
                ConcreteFc = 27.0,
                ConcreteGsi = 0.75,
                MainBarNum = 20,
                MainBarSize = "D25",
                MainBarSpec = "SD390",
                MainBarDr = 600.0,
                HoopSize = "D13",
                HoopSpacing = 150.0,
                HoopSpec = "SD295",
                HoopCenterCover = 150.0,
                PileDiameter = 1000.0,
            };
            Assert.AreEqual(ShearReinforcementMethods.Standard, s.HoopMethod, "既定は「標準」であること");
            Assert.AreEqual(295.0, s.HoopSigmay, 1e-9);

            var calc = Calc(s);
            Assert.AreEqual(264742.55452046875, calc.GetServiceLimitQNInteraction(3.0, false).Item1[0], 1e-6,
                "標準の使用限界せん断力が変わっています");
        }
    }
}
