using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// Hybrid ニーディング工法（三谷セキサン）の支持力算定の検証。
    ///
    /// 文献値はカタログ（https://www.m-sekisan.co.jp/download/pdf/cat_HybridKneading.pdf）の
    /// p.3-4（押込み）と p.7-8（引抜き）。
    /// </summary>
    [TestClass]
    public class HybridKneadingBearingCapacityTests
    {
        private const string Sand = "砂質土";
        private const string Gravel = "礫質土";
        private const string Clay = "粘性土";

        // ── 1. 先端支持力係数 α — カタログ p.3 の対比表 11 点 ──────────────

        /// <summary>α = 200e(e+0.2)（砂質・礫質）/ 200e²（粘土質）</summary>
        [DataTestMethod]
        [DataRow(1.0, 240, 200)]
        [DataRow(1.1, 286, 242)]
        [DataRow(1.2, 336, 288)]
        [DataRow(1.3, 390, 338)]
        [DataRow(1.4, 448, 392)]
        [DataRow(1.5, 510, 450)]
        [DataRow(1.6, 576, 512)]
        [DataRow(1.7, 646, 578)]
        [DataRow(1.8, 720, 648)]
        [DataRow(1.9, 798, 722)]
        [DataRow(2.0, 880, 800)]
        public void Alpha_MatchesCatalogTable(double e, int expectedSand, int expectedClay)
        {
            Assert.AreEqual(expectedSand, SoilPile.HybridAlpha(e, isCohesive: false), 0.5, $"砂・礫 e={e:N1}");
            Assert.AreEqual(expectedClay, SoilPile.HybridAlpha(e, isCohesive: true), 0.5, $"粘土 e={e:N1}");
        }

        // ── 2. 周面摩擦力度 τ2 ────────────────────────────────────

        [TestMethod]
        public void Tau2_StraightShape_UsesFlatCoefficients()
        {
            // ストレート形状: 砂質 β = 4.4 / 粘土質 γ = 0.7。es は掛からない
            Assert.AreEqual(4.4 * 20, Tau2(false, isNodular: false, enhanced: false, ns: 20, qu: 0, es: 1.8), 1e-12);
            Assert.AreEqual(4.4 * 20, Tau2(false, isNodular: false, enhanced: true, ns: 20, qu: 0, es: 1.8), 1e-12);
            Assert.AreEqual(0.7 * 150, Tau2(true, isNodular: false, enhanced: false, ns: 0, qu: 150, es: 1.8), 1e-12);
        }

        [TestMethod]
        public void Tau2_NodularStandard_AddsConstantTerm()
        {
            // 節付き 標準型: βNs = 5.0Ns + 20 / γqu = 0.7qu + 20 （es は掛からない）
            Assert.AreEqual(5.0 * 20 + 20, Tau2(false, true, enhanced: false, ns: 20, qu: 0, es: 1.8), 1e-12);
            Assert.AreEqual(0.7 * 150 + 20, Tau2(true, true, enhanced: false, ns: 0, qu: 150, es: 1.8), 1e-12);
        }

        [TestMethod]
        public void Tau2_NodularFrictionEnhanced_ScalesByExcavationRatio()
        {
            // 節付き 摩擦強化型: βNs = (5.0Ns + 30)·es / γqu = (0.7qu + 20)·es
            Assert.AreEqual((5.0 * 20 + 30) * 1.5, Tau2(false, true, enhanced: true, ns: 20, qu: 0, es: 1.5), 1e-12);
            Assert.AreEqual((0.7 * 150 + 20) * 1.5, Tau2(true, true, enhanced: true, ns: 0, qu: 150, es: 1.5), 1e-12);
        }

        private static double Tau2(bool isCohesive, bool isNodular, bool enhanced, double ns, double qu, double es)
            => SoilPile.HybridTau2(isCohesive, isNodular, enhanced, ns, qu, es);

        // ── 3. 引抜きの周面摩擦力度 τT ────────────────────────────

        [TestMethod]
        public void TauT_MatchesUpliftCoefficients()
        {
            // 砂質 ストレート λ = 3.74 / 節付き λNs = 4.25Ns + 17
            Assert.AreEqual(-3.74 * 20, SoilPile.HybridTauT(false, isNodular: false, ns: 20, qu: 0), 1e-12);
            Assert.AreEqual(-(4.25 * 20 + 17), SoilPile.HybridTauT(false, isNodular: true, ns: 20, qu: 0), 1e-12);

            // 粘土質 ストレート μ = 0.59 / 節付き μqu = 0.63qu + 18
            Assert.AreEqual(-0.59 * 150, SoilPile.HybridTauT(true, isNodular: false, ns: 0, qu: 150), 1e-12);
            Assert.AreEqual(-(0.63 * 150 + 18), SoilPile.HybridTauT(true, isNodular: true, ns: 0, qu: 150), 1e-12);
        }

        // ── 4. 適用範囲のクランプ ──────────────────────────────────

        [TestMethod]
        public void Qu_IsTwiceCohesionCappedAt200()
        {
            Assert.AreEqual(150.0, SoilPile.HybridQu(75.0), 1e-12, "qu = 2·Cu");
            Assert.AreEqual(200.0, SoilPile.HybridQu(300.0), 1e-12, "個々の qu は 200 が上限");
        }

        /// <summary>個々の N 値はカタログの上限 100。範囲 0&lt;N̄s≦30 は平均に対するもので、個々の値には掛けない。</summary>
        [TestMethod]
        public void IndividualNs_IsCappedAtOneHundred()
        {
            Assert.AreEqual(45.0, SoilPile.HybridIndividualNs(45.0), 1e-12, "45 は 30 に丸めない (以前は土層ごとに 30 以下へ丸めていた)");
            Assert.AreEqual(100.0, SoilPile.HybridIndividualNs(120.0), 1e-12);
        }

        /// <summary>
        /// 周面の N̄s の範囲 (30 以下) は周面の平均で見る (2026-11-23、カタログで利用者が確認)。
        /// 杭 0〜−25 m: 上の砂質層 0〜−20 m (20 m、うち上 10 m はストレートの PHC で τ2 = 4.4·Ns)・下の礫質層 −20〜−25 m (5 m)。
        /// </summary>
        [DataTestMethod]
        [DataRow(35.0, 10.0, false)]   // 平均 (700 + 50)/25 = 30.0 → 範囲内、各層そのまま (以前は 35 → 30)
        [DataRow(45.0, 25.0, true)]    // 平均 (900 + 125)/25 = 41.0 → 30 にそろえる
        public void ShaftNs_RangeIsCheckedOnTheAverage(double nUpper, double nLower, bool outOfRange)
        {
            var pile = BuildPile(e: 1.5, es: 1.0);
            pile.GroundInput.GroundLayers[0].NValue = nUpper;
            pile.GroundInput.GroundLayers[1].NValue = nLower;
            pile.UpdateProperties();

            double avg = (nUpper * 20 + nLower * 5) / 25;
            Assert.AreEqual(avg, pile.HybridShaftAverages().Ns!.Value, 1e-9, "N̄s は有効長で重み付けた平均");
            double k = outOfRange ? 30.0 / avg : 1.0;
            var top = pile.PileCircumVerticals.OrderByDescending(p => p.Top).First();
            Assert.AreEqual(4.4 * nUpper * k, top.Tau2, 1e-9, "上の区間 (ストレート) の τ2 = 4.4·Ns");
            Assert.AreEqual(outOfRange, pile.ValidateHybridKneadingRange().Any(w => w.Contains("N̄s")), string.Join(" / ", pile.ValidateHybridKneadingRange()));
        }

        [TestMethod]
        public void ExcavationRatio_IsLimitedByExpansionRatio()
        {
            // es ≦ e
            var pile = BuildPile(e: 1.2, es: 1.8);
            Assert.AreEqual(1.2, pile.HybridEs, 1e-12, "es は e を超えられない");

            // e = 2.0 なら es も 2.0 まで (β の枠 1.0〜2.0)。以前は「e ≧ 1.7 なら 1.6」で縛っていたが、
            // カタログに該当する記述が無く外した (2026-11-23 利用者の判断。es ≦ e は p.4 ③ にある)
            var large = BuildPile(e: 2.0, es: 2.0);
            Assert.AreEqual(2.0, large.HybridEs, 1e-12, "e = 2.0 なら es = 2.0 を使える");
            Assert.IsFalse(large.ValidateHybridKneadingRange().Any(w => w.Contains("設計掘削径比")), "砂質・礫質だけなら es = 2.0 は範囲内");
        }

        /// <summary>
        /// 粘土質地盤の周面摩擦力係数 γ の es は 1.0〜1.6 (カタログの γ の枠)。es は杭に 1 つなので、
        /// 粘土質の層の式にだけ 1.6 を上限として掛け、知らせる。砂質・礫質の層は es のまま。
        /// </summary>
        [TestMethod]
        public void ClayLayersUseEsCappedAtOnePointSix()
        {
            var pile = BuildPile(e: 2.0, es: 2.0);
            pile.PileBodyInput.HybridIsFrictionEnhanced = true;
            pile.GroundInput.GroundLayers[1].GranularityClass = Clay;
            pile.GroundInput.GroundLayers[1].Cohesive = 50;   // qu = 100 (平均 q̄u も 100 で範囲内)
            pile.UpdateProperties();

            var bottom = pile.PileCircumVerticals.OrderBy(p => p.Bottom).First();   // −20〜−25 m、節付き (BF.S)、粘土質
            Assert.IsTrue(bottom.PileBodySegment!.PileSection.IsNodularPile, "(前提) 最下段は節付き");
            Assert.AreEqual((0.7 * 100 + 20) * 1.6, bottom.Tau2, 1e-9, "粘土質の層の γ の式は es = 1.6");
            var middle = pile.PileCircumVerticals.OrderBy(p => p.Bottom).Skip(1).First();   // −10〜−20 m、節付き、砂質
            Assert.AreEqual((5.0 * 15 + 30) * 2.0, middle.Tau2, 1e-9, "砂質の層の β の式は es = 2.0 のまま");
            Assert.IsTrue(pile.ValidateHybridKneadingRange().Any(w => w.Contains("周面摩擦力係数 γ")),
                string.Join(" / ", pile.ValidateHybridKneadingRange()));
        }

        // ── 5. 先端支持力 ────────────────────────────────────────

        [TestMethod]
        public void ToeBearing_UsesNodeDiameterArea()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);

            // Ap = π·D1²/4（D1 = 節部径 1200mm）。根固め部径 D3 = 1.5×1.2 = 1.8m ではない
            Assert.AreEqual(Math.PI * 1.2 * 1.2 * 0.25, pile.ApBearing, 1e-9);
            Assert.AreEqual(1.8, pile.HybridD3, 1e-9, "根固め部径 D3 = e·D1");
            Assert.AreEqual(1800.0, pile.PileBodyInput.PileToeDia, 1e-6,
                "導出した D3 が根固め部径として書き戻されていない (姿図・3D が参照する)");

            // Qpu = α·N
            double alpha = SoilPile.HybridAlpha(1.5, isCohesive: false);
            Assert.AreEqual(alpha * pile.HybridToeNValue, pile.Qpu, 1e-6);
            Assert.IsTrue(pile.Rpu > 0);
        }

        [TestMethod]
        public void ToeBearing_IsZeroWhenAverageNIsBelowFive()
        {
            // N < 5 のときは α = 0 とする規定
            var pile = BuildPile(e: 1.5, es: 1.0, toeNValue: 3);

            Assert.IsTrue(pile.HybridToeNValue < SoilPile.HybridToeNMin);
            Assert.AreEqual(0.0, pile.Qpu, 1e-12, "N < 5 で先端支持力が 0 になっていない");
            Assert.AreEqual(0.0, pile.Rpu, 1e-12);
        }

        [TestMethod]
        public void ToeNValueRange_IsOneD1BelowAndBulbTopAbove()
        {
            // e ≦ 1.6 → 根固め部上端は先端支持力算定位置の 2m 上
            var pile = BuildPile(e: 1.5, es: 1.0);
            double basis = pile.HybridToeEvaluationAltitude;

            Assert.AreEqual(basis + 2.0, pile.PileToeNValueAverageRangeUpperAltitude, 1e-9);
            Assert.AreEqual(basis - 1.2, pile.PileToeNValueAverageRangeLowerAltitude, 1e-9, "下方 1·D1");

            // e ≧ 1.7 → 3m 上
            var large = BuildPile(e: 1.8, es: 1.0);
            Assert.AreEqual(large.HybridToeEvaluationAltitude + 3.0,
                large.PileToeNValueAverageRangeUpperAltitude, 1e-9);
        }

        // ── 6. 引抜きの先端項 ────────────────────────────────────

        [TestMethod]
        public void UpliftToeTerm_IsZeroWhenExpansionRatioIsSmall()
        {
            // e ≦ 1.3 のとき κ = 0
            Assert.AreEqual(0.0, BuildPile(e: 1.3, es: 1.0).HybridKappaValue, 1e-12);
            Assert.AreEqual(157.0, BuildPile(e: 1.4, es: 1.0).HybridKappaValue, 1e-12);
        }

        /// <summary>
        /// 引抜きの評定は先端地盤が砂質・礫質地盤だけ (カタログ p.7「適用する地盤の種類」)。
        /// 杭先端が粘土質なら κ = 0 にして知らせる (2026-11-23 利用者の判断。以前は粘土質でも見込んでいた)。
        /// </summary>
        [TestMethod]
        public void UpliftToeTerm_IsZeroWhenTheToeIsInClay()
        {
            var pile = BuildPile(e: 1.8, es: 1.0);
            Assert.AreEqual(157.0, pile.HybridKappaValue, 1e-12, "(前提) 礫質の先端では κ = 157");
            pile.GroundInput.GroundLayers[1].GranularityClass = Clay;
            pile.GroundInput.GroundLayers[1].Cohesive = 100;
            pile.UpdateProperties();
            Assert.AreEqual(Clay, pile.PileToeGranularityClass, "(前提) 杭先端は粘土質");
            Assert.AreEqual(0.0, pile.HybridKappaValue, 1e-12, "粘土質の先端では κ = 0");
            Assert.AreEqual(0.0, pile.HybridUpliftToeResistance, 1e-12);
            Assert.IsTrue(pile.ValidateHybridKneadingRange().Any(w => w.Contains("引抜きの評定")),
                string.Join(" / ", pile.ValidateHybridKneadingRange()));
        }

        /// <summary>
        /// 引抜きの杭先端平均 N 値は、根固め部内の最上部節部から上方 4·D1 の区間 (カタログ p.7・p.8)。
        /// 最上部節部の高さを入力すれば区間がそこから始まり、未入力 (0) なら先端支持力算定位置から数えて知らせる。
        /// 杭先端は標高 −25 m、D1 = 1.2 m (4·D1 = 4.8 m)、Lu = 0。土質点: −23 m は N = 50、−24 m は N = 10。
        /// </summary>
        [TestMethod]
        public void UpliftNValue_StartsAtTheTopNodeInTheBulb()
        {
            var pile = BuildPile(e: 1.8, es: 1.0);
            foreach (var m in pile.GroundInput.GroundMassesData)
                m.NValue = m.AltitudeDepth == -23.0 ? 50 : m.AltitudeDepth == -24.0 ? 10 : 45;
            pile.UpdateProperties();

            Assert.AreEqual(-25.0, pile.HybridUpliftNBasisAltitude, 1e-9, "未入力なら先端支持力算定位置 (Lu = 0 で杭先端)");
            Assert.AreEqual(30.0, pile.HybridUpliftNValue, 1e-9, "−25〜−20.2 m: N = 50・10 の平均");
            Assert.IsTrue(pile.ValidateHybridKneadingRange().Any(w => w.Contains("最上部節部の高さが未入力")));

            pile.PileBodyInput.HybridTopNodeHeight = 1.5;   // 最上部節部は −23.5 m
            Assert.AreEqual(-23.5, pile.HybridUpliftNBasisAltitude, 1e-9);
            Assert.AreEqual(50.0, pile.HybridUpliftNValue, 1e-9, "−23.5〜−18.7 m: N = 50 だけ");
            Assert.IsFalse(pile.ValidateHybridKneadingRange().Any(w => w.Contains("最上部節部")),
                string.Join(" / ", pile.ValidateHybridKneadingRange()));

            pile.PileBodyInput.HybridTopNodeHeight = 3.5;   // 根固め部の上端 (e = 1.8 で 3 m) より上
            Assert.IsTrue(pile.ValidateHybridKneadingRange().Any(w => w.Contains("根固め部の上端") && w.Contains("超えています")));
        }

        /// <summary>杭先端の詳細図の高さは、計算と同じ定義 (先端支持力算定位置・根固め部上端・引抜きの区間) で描く。</summary>
        [TestMethod]
        public void TheToeDiagramUsesTheSameHeightsAsTheCalculation()
        {
            var pile = BuildPile(e: 1.8, es: 1.0, lu: 0.6);
            pile.PileBodyInput.HybridTopNodeHeight = 2.0;
            var g = PileDesign.Views.Controls.HybridToeDiagram.Layout(pile.HybridD1 * 1000, 1.8, 1.0, 0.6, 2.0);
            double toe = pile.PileBottomAltitude;
            Assert.AreEqual(pile.HybridToeEvaluationAltitude - toe, g.Eval, 1e-9, "先端支持力算定位置");
            Assert.AreEqual(pile.HybridToeEvaluationAltitude + pile.HybridBulbTopAboveEvaluation - toe, g.BulbTop, 1e-9, "根固め部上端");
            Assert.AreEqual(pile.HybridUpliftNBasisAltitude - toe, g.UpliftFrom, 1e-9, "引抜きの区間の下端");
            Assert.AreEqual(4 * pile.HybridD1, g.UpliftTo - g.UpliftFrom, 1e-9, "引抜きの区間は 4·D1");
            Assert.AreEqual(pile.HybridD3, g.D3, 1e-9, "根固め部径 D3 = e·D1");
        }

        [TestMethod]
        public void UpliftToeTerm_IsZeroWhenShaftIsExcavated()
        {
            // 軸部を拡大掘削する場合 (es > 1.0) も κ = 0
            Assert.AreEqual(0.0, BuildPile(e: 1.8, es: 1.5).HybridKappaValue, 1e-12);
        }

        [TestMethod]
        public void UpliftResistance_IncludesToeTerm()
        {
            var withToe = BuildPile(e: 1.8, es: 1.0);      // κ = 157
            var withoutToe = BuildPile(e: 1.3, es: 1.0);   // κ = 0

            Assert.IsTrue(withToe.HybridUpliftToeResistance > 0, "先端項が算定されていない");
            Assert.AreEqual(0.0, withoutToe.HybridUpliftToeResistance, 1e-12);

            // 引抜き抵抗は負値で保持する規約。先端項の分だけ絶対値が大きくなる
            Assert.IsTrue(withToe.Rtu < withoutToe.Rtu,
                $"先端項が引抜き抵抗に反映されていない ({withToe.Rtu:N1} vs {withoutToe.Rtu:N1})");

            // 短期 (損傷限界) は極限の 2/3 相当
            double toe = withToe.HybridUpliftToeResistance;
            Assert.AreEqual((2.0 / 3.0) * toe, withoutToe.Rty - withToe.Rty, 1.0,
                "短期側の先端項が 2/3 になっていない");
        }

        // ── 7. 杭下長 Lu ────────────────────────────────────────

        /// <summary>
        /// 杭下長 Lu はカタログが値を公表していないため入力値をそのまま使う。
        /// 推定値で補うと、周面摩擦の有効長と先端平均N値の基準位置が黙って動いてしまう。
        /// </summary>
        [TestMethod]
        public void PileBelowLength_UsesTheInputAsIs()
        {
            var entered = BuildPile(e: 1.5, es: 1.0, lu: 0.5);
            Assert.AreEqual(0.5, entered.HybridLu, 1e-9, "入力した Lu が使われていない");
            Assert.AreEqual(entered.PileBottomAltitude + 0.5, entered.HybridToeEvaluationAltitude, 1e-9);

            var blank = BuildPile(e: 1.5, es: 1.0, lu: 0.0);
            Assert.AreEqual(0.0, blank.HybridLu, 1e-9, "未入力の Lu が勝手に補われている");
            Assert.AreEqual(blank.PileBottomAltitude, blank.HybridToeEvaluationAltitude, 1e-9,
                "Lu = 0 なら先端支持力算定位置は杭先端と同じ");
        }

        // ── 8. 周面: 節部径周長 と 杭下長の除外 ────────────────────

        [TestMethod]
        public void SkinFriction_ExcludesThePileBelowLength()
        {
            var pile = BuildPile(e: 1.5, es: 1.0, lu: 0.5);
            var bottom = pile.PileCircumVerticals.OrderBy(p => p.Bottom).First();

            Assert.AreEqual(0.5, bottom.ExcludedLength, 1e-9,
                "先端支持力算定位置より下が杭周面摩擦から除外されていない");
        }

        [TestMethod]
        public void SkinFriction_UsesNodeDiameterForCircumference()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);
            foreach (var pcv in pile.PileCircumVerticals)
                Assert.IsTrue(pcv.UseNodeDiameterForCircumference, "ψ = π·D1 になっていない");
        }

        // ── 9. 沈下との整合 ──────────────────────────────────────

        [TestMethod]
        public void SettlementUsesTheMethodUltimateToeBearing()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);

            Assert.AreEqual(pile.Rpu, pile.SettleRpu, 1e-6,
                "沈下曲線の極限先端支持力が工法の値と一致しない");
            Assert.AreEqual(1800.0, pile.Dp, 1e-6, "沈下曲線の先端径に根固め部径 D3 を使う");
        }

        // ── 10. 適用範囲チェック ─────────────────────────────────

        [TestMethod]
        public void RangeCheck_PassesForATypicalDesign()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);
            pile.PileBodyInput.HybridTopNodeHeight = 1.0;   // 根固め部内の最上部節部 (未入力なら知らせる。2026-11-23 から)
            var warnings = pile.ValidateHybridKneadingRange().ToList();
            Assert.AreEqual(0, warnings.Count, "警告が出ている: " + string.Join(" / ", warnings));
        }

        [TestMethod]
        public void RangeCheck_FlagsJapanPileProduct()
        {
            // 下杭をジャパンパイルの JP-NPH 節杭に差し替える
            // (BF.S は断面タイプ自体が三谷セキサン専用なので、断面タイプごと変える必要がある)
            var pile = BuildPile(e: 1.5, es: 1.0);
            Configure(pile.PileBodyInput.PileBodySegments[^1].PileSection,
                      PileTypeNames.PhcNodular, 1100, 1200, "NPH-1200-1100-標準-85-A");
            pile.UpdateProperties();

            var warnings = pile.ValidateHybridKneadingRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("ジャパンパイル")),
                "他メーカー製品が検出されない: " + string.Join(" / ", warnings));
        }

        [TestMethod]
        public void RangeCheck_FlagsExcavationRatioAboveExpansionRatio()
        {
            var warnings = BuildPile(e: 1.2, es: 1.8).ValidateHybridKneadingRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("es")), "es ≦ e の違反が検出されない");
        }

        [TestMethod]
        public void RangeCheck_IsSilentForOtherConstructionTypes()
        {
            var pile = BuildPile(e: 1.5, es: 1.0, constructionType: PileConstructionTypeNames.Preboring);
            Assert.AreEqual(0, pile.ValidateHybridKneadingRange().Count());
        }

        // ── 10. 入力側の導出値 (杭体ウィンドウの表示) ─────────────

        [TestMethod]
        public void PileBodyInput_DerivesNodeDiameterFromTheBottomSection()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);

            // D1 は最下段区間の断面から。BF.S 節杭 (節部径 1200 / 軸部径 1100)
            Assert.AreEqual(1200.0, pile.PileBodyInput.HybridD1, 1e-9);
            Assert.AreEqual(1.5 * 1200.0, pile.PileBodyInput.HybridD3, 1e-9);
        }

        [TestMethod]
        public void PileBodyInput_UpdatesToeDiameterAsSoonAsTheRatioChanges()
        {
            // 杭体ウィンドウでは SoilPile を経由しないため、入力側だけで根固め部径が追随する必要がある
            var body = BuildPile(e: 1.5, es: 1.0).PileBodyInput;
            Assert.AreEqual(1800.0, body.PileToeDia, 1e-9);

            body.HybridExpansionRatio = 1.6;

            Assert.AreEqual(1.6 * 1200.0, body.PileToeDia, 1e-9,
                "設計拡径比を変えても根固め部径が古いまま残っている");
        }

        [TestMethod]
        public void PileBodyInput_LeavesToeDiameterAloneForOtherConstructionTypes()
        {
            // 他工法では杭先端径は手入力のまま。導出値で上書きしてはいけない
            var body = BuildPile(e: 1.5, es: 1.0, constructionType: PileConstructionTypeNames.Preboring).PileBodyInput;
            Assert.AreEqual(1500.0, body.PileToeDia, 1e-9);

            body.HybridExpansionRatio = 1.9;
            Assert.AreEqual(1500.0, body.PileToeDia, 1e-9, "他工法の杭先端径が上書きされている");
        }

        // ── ヘルパー ──────────────────────────────────────────────

        /// <summary>
        /// 三谷セキサンの BF.S 節杭 (節部径 1200 / 軸部径 1100) を下杭に持つ杭を組み立てる。
        /// 杭頭 GL-0m、杭長 25m → 杭先端は標高 -25m。
        /// </summary>
        /// <summary>適用条件の表のテスト (FormulaConditionTableTests) から使う。中身は <see cref="BuildPile"/>。</summary>
        internal static SoilPile BuildPileForTables(double e, double es) => BuildPile(e, es);

        private static SoilPile BuildPile(
            double e, double es, double lu = 0.0, double toeNValue = 45,
            string? constructionType = null)
        {
            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileConstructionType = constructionType ?? PileConstructionTypeNames.HybridKneading,
                PileToeDia = 1500,   // Hybrid では e·D1 で上書きされる
                SettlePileToeDia = 1500,
                HybridExpansionRatio = e,
                HybridExcavationRatio = es,
                HybridPileBelowLength = lu,
                HybridIsFrictionEnhanced = false,
            };
            body.PileBodySegments =
            [
                new PileBodySegment { No = 1, SegmentLength = 10, SegmentDepth = 10, PileSection = new PileSection() },
                new PileBodySegment { No = 2, SegmentLength = 15, SegmentDepth = 25, PileSection = new PileSection() },
            ];

            // PileBodySegments の setter が親の杭体タイプを子断面へ同期し既定値に戻すため、
            // 代入後に設定し直す
            Configure(body.PileBodySegments[0].PileSection, PileTypeNames.Phc, 1100, 0, "MS-hi105-1100-標準型-A");
            Configure(body.PileBodySegments[1].PileSection, PileTypeNames.BfsTip, 1100, 1200, "BF.S-1200-1100");

            var ground = new GroundInput { GroundTopAltitude = 0 };
            ground.GroundLayers =
            [
                new GroundLayerInput { No = 1, BottomAltitude = -20, GranularityClass = Sand, NValue = 15, Cohesive = 0,
                                       IsPositiveCircumResistance = true, IsNegativeCircumResistance = true },
                new GroundLayerInput { No = 2, BottomAltitude = -40, GranularityClass = Gravel, NValue = 25, Cohesive = 0,
                                       IsPositiveCircumResistance = true, IsNegativeCircumResistance = true },
            ];
            ground.GroundMassesData =
            [
                new GroundMassDataInput { No = 1, AltitudeDepth = -23.0, NValue = toeNValue },
                new GroundMassDataInput { No = 2, AltitudeDepth = -24.0, NValue = toeNValue },
                new GroundMassDataInput { No = 3, AltitudeDepth = -26.0, NValue = toeNValue },
                new GroundMassDataInput { No = 4, AltitudeDepth = -28.0, NValue = toeNValue },
                new GroundMassDataInput { No = 5, AltitudeDepth = -30.0, NValue = toeNValue },
            ];

            ObservableCollection<PileZDataItem> zs =
                [.. new[] { 0.0, -10.0, -20.0, -25.0 }.Select(z => new PileZDataItem { Z = z })];

            var soilPile = new SoilPile();
            soilPile.Initialize(no: 1, groundNo: 1, groundInput: ground,
                                pileBodyNo: 1, pileBodyInput: body, z: 0.0, zDataItems: zs);
            soilPile.UpdateProperties();
            return soilPile;
        }

        private static void Configure(
            PileSection section, string sectionType, double shaftDia, double nodeDia, string productName)
        {
            section.PileBodyType = PileTypeNames.PrecastConcrete;
            section.PileSectionType = sectionType;
            section.PileDiameter = shaftDia;
            if (nodeDia > 0) section.NodeDiameter = nodeDia;
            section.SelectedPrecastPile.Name = productName;
        }
    }
}
