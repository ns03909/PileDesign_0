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

        [TestMethod]
        public void Ns_IsCappedAt30()
        {
            Assert.AreEqual(30.0, SoilPile.HybridClampNs(45.0), 1e-12);
            Assert.AreEqual(18.0, SoilPile.HybridClampNs(18.0), 1e-12);
        }

        [TestMethod]
        public void ExcavationRatio_IsLimitedByExpansionRatio()
        {
            // es ≦ e
            var pile = BuildPile(e: 1.2, es: 1.8);
            Assert.AreEqual(1.2, pile.HybridEs, 1e-12, "es は e を超えられない");

            // e ≧ 1.7 のとき es の上限は 1.6
            var large = BuildPile(e: 2.0, es: 2.0);
            Assert.AreEqual(1.6, large.HybridEs, 1e-12, "e ≧ 1.7 のとき es の上限は 1.6");
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

        // ── 7. 周面: 節部径周長 と 杭下長の除外 ────────────────────

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

        // ── 8. 沈下との整合 ──────────────────────────────────────

        [TestMethod]
        public void SettlementUsesTheMethodUltimateToeBearing()
        {
            var pile = BuildPile(e: 1.5, es: 1.0);

            Assert.AreEqual(pile.Rpu, pile.SettleRpu, 1e-6,
                "沈下曲線の極限先端支持力が工法の値と一致しない");
            Assert.AreEqual(1800.0, pile.Dp, 1e-6, "沈下曲線の先端径に根固め部径 D3 を使う");
        }

        // ── 9. 適用範囲チェック ──────────────────────────────────

        [TestMethod]
        public void RangeCheck_PassesForATypicalDesign()
        {
            var warnings = BuildPile(e: 1.5, es: 1.0).ValidateHybridKneadingRange().ToList();
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
