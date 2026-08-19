using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// Smart-MAGNUM 工法（ジャパンパイル）の支持力算定の検証。
    ///
    /// 文献値はカタログ（https://www.japanpile.co.jp/method/pdf/smart-magnum.pdf）の
    /// p.7-8 の算定式、p.10 の「地盤の長期先端支持力一覧表（掘削径によるωpと先端支持力係数αとの
    /// 対比表）」および同ページの計算例。
    ///
    /// カタログの α は<b>切り捨て</b>表示（例 348.14 → 348）なので、表との照合は Math.Floor で行う。
    /// </summary>
    [TestClass]
    public class SmartMagnumBearingCapacityTests
    {
        private const string Sand = "砂質土";
        private const string Gravel = "礫質土";
        private const string Clay = "粘性土";

        // ── 1. 先端支持力係数 α — カタログ p.10 の対比表と照合 ──────────────

        /// <summary>
        /// α = 240·ωp^1.5 + 45(2+LL')·ωp （砂質・礫質）
        /// α = 210·ωp^1.25 + 45(2+LL')·ωp （粘土質）
        ///
        /// 対比表は 節部径 Don = 650/800/1000/1200 × LL = 0.5/1.0/2.0 × 掘削径 3 種 の構成。
        /// ここでは Don = 1200 (Dsn = 1.25) の列から ωp を逆算した既知点で照合する。
        /// </summary>
        [DataTestMethod]
        // 砂質・礫質地盤（表の上段ブロック）
        [DataRow(1.04, 0.5, false, 348)]
        [DataRow(1.04, 1.0, false, 394)]
        [DataRow(1.04, 2.0, false, 441)]
        [DataRow(1.28, 0.5, false, 462)]
        [DataRow(1.60, 0.5, false, 629)]
        [DataRow(1.60, 1.0, false, 701)]
        [DataRow(1.60, 2.0, false, 773)]
        // 適用範囲の両端
        [DataRow(1.00, 0.5, false, 330)]
        [DataRow(2.00, 2.0, false, 1038)]
        // 粘土質地盤（表の下段ブロック）
        [DataRow(1.04, 0.5, true, 314)]
        [DataRow(1.28, 0.5, true, 401)]
        [DataRow(1.76, 0.5, true, 584)]
        [DataRow(1.76, 2.0, true, 742)]
        [DataRow(1.00, 0.5, true, 300)]
        [DataRow(2.00, 2.0, true, 859)]
        public void Alpha_MatchesCatalogTable(double omegaP, double ll, bool isCohesive, int expected)
        {
            double llEffective = ll <= 0.5 ? 0 : ll;
            double alpha = SoilPile.SmartMagnumAlpha(omegaP, llEffective, isCohesive);

            Assert.AreEqual(expected, (int)Math.Floor(alpha),
                $"ωp={omegaP:N2} LL={ll:N1} 粘土={isCohesive} → α={alpha:N2}");
        }

        /// <summary>LL' は LL ≤ 0.5m で 0、0.5 &lt; LL ≤ 2 で LL そのもの。</summary>
        [TestMethod]
        public void EffectiveLL_IsZeroBelowHalfMetre()
        {
            // LL = 0.5 と LL = 0 は同じ α になる（どちらも LL' = 0）
            double a0 = SoilPile.SmartMagnumAlpha(1.5, 0, false);
            double aHalf = SoilPile.SmartMagnumAlpha(1.5, 0, false);
            Assert.AreEqual(a0, aHalf, 1e-12);

            // LL' が効くと α は増える
            Assert.IsTrue(SoilPile.SmartMagnumAlpha(1.5, 1.0, false) > a0);
        }

        // ── 2. 基準掘削径 Dsn / Dss ──────────────────────────────────

        [TestMethod]
        public void StandardExcavationDia_AddsFiveCentimetres()
        {
            Assert.AreEqual(1.25, SoilPile.SmartMagnumStandardExcavationDia(1.20), 1e-12);
            Assert.AreEqual(0.85, SoilPile.SmartMagnumStandardExcavationDia(0.80), 1e-12);
        }

        [TestMethod]
        public void StandardExcavationDia_HasSpecialCaseAt440mm()
        {
            // カタログの特例: Don (Dos) が 0.44m の場合は 0.50m とする（0.49m ではない）
            Assert.AreEqual(0.50, SoilPile.SmartMagnumStandardExcavationDia(0.44), 1e-12);
        }

        [TestMethod]
        public void Omega_IsClampedToCertifiedRange()
        {
            Assert.AreEqual(1.00, SoilPile.ClampOmega(0.80), 1e-12);
            Assert.AreEqual(2.00, SoilPile.ClampOmega(2.60), 1e-12);
            Assert.AreEqual(1.52, SoilPile.ClampOmega(1.52), 1e-12);
        }

        // ── 3. 周面摩擦力度 τ2 — 標準型/周面強化型 × ストレート/節杭 ────────

        [TestMethod]
        public void Tau2_SandyStraight_UsesFlatBeta()
        {
            // 標準型 β = 5.0、周面強化型 β = 8.0。ストレート杭には ωs は掛からない
            Assert.AreEqual(5.0 * 20, Tau2(isCohesive: false, isNodular: false, isReinforced: false, ns: 20, qu: 0, omegaS: 1.7), 1e-12);
            Assert.AreEqual(8.0 * 20, Tau2(isCohesive: false, isNodular: false, isReinforced: true, ns: 20, qu: 0, omegaS: 1.7), 1e-12);
        }

        [TestMethod]
        public void Tau2_SandyNodular_UsesOmegaS()
        {
            // 標準型 節杭: β·Ns = (30 + 5.5·Ns)·ωs
            Assert.AreEqual((30 + 5.5 * 20) * 1.5,
                Tau2(false, isNodular: true, isReinforced: false, ns: 20, qu: 0, omegaS: 1.5), 1e-12);

            // 周面強化型 節杭: β = 9.5·ωs → β·Ns = 9.5·ωs·Ns
            Assert.AreEqual(9.5 * 1.5 * 20,
                Tau2(false, isNodular: true, isReinforced: true, ns: 20, qu: 0, omegaS: 1.5), 1e-12);
        }

        [TestMethod]
        public void Tau2_ClayStraight_UsesFlatGamma()
        {
            Assert.AreEqual(0.7 * 150, Tau2(true, false, false, ns: 0, qu: 150, omegaS: 1.7), 1e-12);
            Assert.AreEqual(0.9 * 150, Tau2(true, false, true, ns: 0, qu: 150, omegaS: 1.7), 1e-12);
        }

        [TestMethod]
        public void Tau2_ClayNodular_UsesOmegaS()
        {
            // 標準型 節杭: γ·qu = (20 + 0.5·qu)·ωs
            Assert.AreEqual((20 + 0.5 * 150) * 1.5, Tau2(true, true, false, 0, 150, 1.5), 1e-12);
            // 周面強化型 節杭: γ = 1.0·ωs
            Assert.AreEqual(1.0 * 1.5 * 150, Tau2(true, true, true, 0, 150, 1.5), 1e-12);
        }

        private static double Tau2(bool isCohesive, bool isNodular, bool isReinforced, double ns, double qu, double omegaS)
            => SoilPile.SmartMagnumTau2(isCohesive, isNodular, isReinforced, ns, qu, omegaS);

        // ── 4. 引抜き係数 ──────────────────────────────────────────

        [TestMethod]
        public void TauT_AppliesUpliftFactors()
        {
            // tRu = (0.8·β·Ns·Ls + 0.9·γ·qu·Lc)·ψ → 砂質・礫質 0.8 / 粘土質 0.9
            // 符号は既存規約に合わせ負値
            Assert.AreEqual(-0.8 * 100, SoilPile.SmartMagnumTauT(isCohesive: false, tau2: 100), 1e-12);
            Assert.AreEqual(-0.9 * 100, SoilPile.SmartMagnumTauT(isCohesive: true, tau2: 100), 1e-12);
        }

        // ── 5. qu / Ns の丸めと適用範囲 ─────────────────────────────

        [TestMethod]
        public void Qu_IsTwiceCohesionWithCatalogRounding()
        {
            // qu = 2·Cu（アプリは粘着力を保持するため換算する）
            Assert.AreEqual(200.0, SoilPile.SmartMagnumQu(100.0), 1e-12);

            // 個々の値は 16 未満で 0、535 超で 535 に丸める
            Assert.AreEqual(0.0, SoilPile.SmartMagnumQu(7.0), 1e-12, "qu = 14 < 16 → 0");
            Assert.AreEqual(16.0, SoilPile.SmartMagnumQu(8.0), 1e-12, "qu = 16 は残る");
            Assert.AreEqual(535.0, SoilPile.SmartMagnumQu(400.0), 1e-12, "qu = 800 > 535 → 535");
        }

        [TestMethod]
        public void Ns_IsClampedToOneThirty()
        {
            Assert.AreEqual(1.0, SoilPile.SmartMagnumClampNs(0.0), 1e-12);
            Assert.AreEqual(30.0, SoilPile.SmartMagnumClampNs(45.0), 1e-12);
            Assert.AreEqual(18.0, SoilPile.SmartMagnumClampNs(18.0), 1e-12);
        }

        // ── 6. カタログ計算例の再現（end-to-end） ──────────────────────

        /// <summary>
        /// カタログ p.10 の計算例。
        /// 礫質地盤、上杭 φ1100 / 下杭 φ1200-1100（節付きPHC杭）、
        /// 拡大根固め部径 Den = 1.9m、節部径 Don = 1.2m、杭下拡大根固め部長さ LL = 1.0m。
        /// 杭先端付近の N 値: Nu = (30+42)/2 = 36、Nl = (44+47+48+51)/4 = 47.5。
        /// </summary>
        [TestMethod]
        public void CatalogWorkedExample_ReproducesToeBearingCapacity()
        {
            const double don = 1.20;
            const double den = 1.90;
            const double ll = 1.0;

            double dsn = SoilPile.SmartMagnumStandardExcavationDia(don);
            Assert.AreEqual(1.25, dsn, 1e-12, "Dsn = Don + 0.05");

            double omegaP = SoilPile.ClampOmega(den / dsn);
            Assert.AreEqual(1.52, omegaP, 5e-3, "ωp = Den/Dsn = 1.9/1.25");

            double alpha = SoilPile.SmartMagnumAlpha(omegaP, llEffective: ll, isCohesive: false);
            Assert.AreEqual(654, (int)Math.Floor(alpha), "カタログ表記 α = 654");

            // 杭先端平均N値 N = (Nu + 3Nl)/4 （砂質・礫質）
            const double nu = 36.0;
            const double nl = 47.5;
            double n = (nu + 3.0 * nl) / 4.0;
            Assert.AreEqual(44.625, n, 1e-12);

            // Ap = π·Don²/4
            double ap = Math.PI * don * don * 0.25;
            Assert.AreEqual(1.13097, ap, 1e-5);

            // 長期許容先端支持力 Rpa = α·N·Ap/3
            double rpa = alpha * n * ap / 3.0;
            Assert.AreEqual(1.10e4, rpa, 1.0e2,
                $"カタログ計算例の長期許容先端支持力と一致しない (計算値 {rpa:N0} kN)");
        }

        // ── 7. SoilPile への組み込み（工法分岐・沈下との整合） ─────────────

        /// <summary>
        /// カタログ計算例と同じ諸元の SoilPile を組み立て、
        /// 先端面積が節部径 Don 基準に切り替わること、
        /// 沈下曲線の極限先端支持力が Smart-MAGNUM の極限先端支持力そのものになることを確認する。
        /// </summary>
        [TestMethod]
        public void SoilPile_SmartMagnum_UsesNodeDiameterForToeAreaAndSyncsSettlement()
        {
            var soilPile = BuildCatalogExamplePile();

            // Ap は節部径 Don = 1.2m 基準（根固め部径 Den = 1.9m ではない）
            Assert.AreEqual(Math.PI * 1.2 * 1.2 * 0.25, soilPile.ApBearing, 1e-9,
                "先端面積が節部径 Don 基準になっていない");
            Assert.AreEqual(Math.PI * 1.9 * 1.9 * 0.25, soilPile.Ap, 1e-9,
                "Ap（根固め部径基準）は従来の意味のまま残すこと");

            // 沈下側: Dp は根固め部径 Den、極限先端支持力は Smart-MAGNUM の値そのもの
            Assert.AreEqual(1900.0, soilPile.Dp, 1e-9, "沈下曲線の先端径に根固め部径 Den を使う");
            Assert.AreEqual(soilPile.Rpu, soilPile.SettleRpu, 1e-6,
                "沈下曲線の極限先端支持力が Smart-MAGNUM の極限先端支持力と一致しない");
            Assert.IsTrue(soilPile.Rpu > 0, "極限先端支持力が算定されていない");
        }

        [TestMethod]
        public void SoilPile_SmartMagnum_ToeNValueUsesWeightedNuNl()
        {
            var soilPile = BuildCatalogExamplePile();

            Assert.AreEqual(36.0, soilPile.SmartMagnumNu, 1e-9, "Nu = 杭先端から上方 2m の平均");
            Assert.AreEqual(47.5, soilPile.SmartMagnumNl, 1e-9, "Nl = 杭先端から下方 LL+Den+Don の平均");
            Assert.AreEqual((36.0 + 3.0 * 47.5) / 4.0, soilPile.PileToeNValue, 1e-9,
                "砂質・礫質は N = (Nu + 3Nl)/4");
        }

        [TestMethod]
        public void SoilPile_SmartMagnum_NValueRangeFollowsCatalogGeometry()
        {
            var soilPile = BuildCatalogExamplePile();
            double toe = soilPile.PileBottomAltitude;

            Assert.AreEqual(toe + 2.0, soilPile.PileToeNValueAverageRangeUpperAltitude, 1e-9,
                "Nu の範囲は杭先端から上方 2m");
            Assert.AreEqual(toe - (1.0 + 1.9 + 1.2), soilPile.PileToeNValueAverageRangeLowerAltitude, 1e-9,
                "Nl の範囲は杭先端から下方 LL + Den + Don");
        }

        // ── 8. 周面: 節部径周長 と 0.4m 除外 ────────────────────────

        [TestMethod]
        public void Circumference_UsesNodeDiameterForNodularPiles()
        {
            var nodular = new PileCircumVertical
            {
                Top = 0,
                Bottom = -5,
                GroundLayer = new GroundLayerInput(),
                PileBodySegment = new PileBodySegment
                {
                    PileSection = MakeNodularSection(shaftDia: 1100, nodeDia: 1200)
                },
                UseNodeDiameterForCircumference = true,
            };
            Assert.AreEqual(Math.PI * 1.2, nodular.Psi, 1e-9, "節杭の周長は ψ = π×節部径");

            // 既定（既存工法）は軸部径基準のまま
            nodular.UseNodeDiameterForCircumference = false;
            Assert.AreEqual(Math.PI * 1.1, nodular.Psi, 1e-9, "既存工法の周長は軸部径基準のまま");
        }

        [TestMethod]
        public void Circumference_StraightPileIgnoresNodeDiameterFlag()
        {
            var straight = new PileCircumVertical
            {
                Top = 0,
                Bottom = -5,
                GroundLayer = new GroundLayerInput(),
                PileBodySegment = new PileBodySegment
                {
                    PileSection = MakeStraightSection(1000)
                },
                UseNodeDiameterForCircumference = true,
            };
            Assert.AreEqual(Math.PI * 1.0, straight.Psi, 1e-9);
        }

        [TestMethod]
        public void ExcludedLength_ReducesFrictionButNotPhysicalLength()
        {
            var pcv = new PileCircumVertical
            {
                Top = 0,
                Bottom = -5,
                GroundLayer = new GroundLayerInput(),
                PileBodySegment = new PileBodySegment { PileSection = MakeStraightSection(1000) },
                Tau2 = 100,
                TauT = -80,
            };
            double rfFull = pcv.Rf;

            pcv.ExcludedLength = 0.4;
            Assert.AreEqual(5.0, pcv.L, 1e-12, "物理長は変わらない（自重の算定に使う）");
            Assert.AreEqual(4.6, pcv.EffectiveL, 1e-12);
            Assert.AreEqual(rfFull * 4.6 / 5.0, pcv.Rf, 1e-9, "周面抵抗が有効長で按分されていない");
            Assert.AreEqual(Math.PI * 1.0 * 4.6, pcv.PsiL, 1e-9, "τ-s ばねの周面積も有効長に従う");
        }

        [TestMethod]
        public void SmartMagnum_ExcludesBottom400mmFromSkinFriction()
        {
            var soilPile = BuildCatalogExamplePile();
            double toe = soilPile.PileBottomAltitude;

            var bottom = soilPile.PileCircumVerticals.OrderBy(p => p.Bottom).First();
            Assert.AreEqual(toe, bottom.Bottom, 1e-9, "最下段区間の下端が杭先端になっていない");
            Assert.AreEqual(0.4, bottom.ExcludedLength, 1e-9,
                "先端支持力評価位置（杭先端の 0.4m 上）より下が周面摩擦から除外されていない");

            // 上側の区間は除外されない
            var top = soilPile.PileCircumVerticals.OrderByDescending(p => p.Top).First();
            Assert.AreEqual(0.0, top.ExcludedLength, 1e-9);
        }

        // ── 9. 既存工法の非回帰 ────────────────────────────────────

        [DataTestMethod]
        [DataRow(PileConstructionTypeNames.Insitu, Sand, 120.0, 7500.0)]
        [DataRow(PileConstructionTypeNames.Insitu, Gravel, 120.0, 7500.0)]
        [DataRow(PileConstructionTypeNames.Preboring, Sand, 150.0, 9000.0)]
        [DataRow(PileConstructionTypeNames.Preboring, Clay, 150.0, 9000.0)]
        [DataRow(PileConstructionTypeNames.Chubori, Sand, 150.0, 9000.0)]
        public void ExistingConstructionTypes_ToeBearingUnchanged(
            string constructionType, string soilClass, double factor, double cap)
        {
            var soilPile = BuildSimplePile(constructionType, soilClass, nValue: 30);

            double expected = Math.Min(factor * soilPile.PileToeNValue, cap);
            Assert.AreEqual(expected, soilPile.Qpu, 1e-6,
                $"{constructionType} / {soilClass} の極限先端支持力度が変わっている");
        }

        [TestMethod]
        public void ExistingConstructionTypes_LegacyPreboringSpellingsStillMatch()
        {
            // 過去ファイルの表記揺れ 3 種がすべて同じ支持力になること
            double[] qpu =
            [
                BuildSimplePile(PileConstructionTypeNames.Preboring, Sand, 30).Qpu,
                BuildSimplePile(PileConstructionTypeNames.PreboringLegacyPile, Sand, 30).Qpu,
                BuildSimplePile(PileConstructionTypeNames.PreboringLegacyTypo, Sand, 30).Qpu,
            ];

            Assert.IsTrue(qpu[0] > 0, "プレボーリングの支持力が算定されていない");
            Assert.AreEqual(qpu[0], qpu[1], 1e-9);
            Assert.AreEqual(qpu[0], qpu[2], 1e-9);
        }

        [TestMethod]
        public void ExistingConstructionTypes_KeepAxisDiameterCircumferenceAndFullLength()
        {
            var soilPile = BuildSimplePile(PileConstructionTypeNames.Preboring, Sand, 30);

            foreach (var pcv in soilPile.PileCircumVerticals)
            {
                Assert.IsFalse(pcv.UseNodeDiameterForCircumference,
                    "既存工法で節部径周長が有効になっている");
                Assert.AreEqual(0.0, pcv.ExcludedLength, 1e-12,
                    "既存工法で周面摩擦の除外長が入っている");
            }
        }

        // ── 10. 適用範囲チェック ──────────────────────────────────

        [TestMethod]
        public void RangeCheck_PassesForCatalogExample()
        {
            var soilPile = BuildCatalogExamplePile();
            var warnings = soilPile.ValidateSmartMagnumRange().ToList();

            Assert.AreEqual(0, warnings.Count,
                "カタログ計算例の諸元で警告が出ている: " + string.Join(" / ", warnings));
        }

        [TestMethod]
        public void RangeCheck_FlagsExcessiveBulbLength()
        {
            var soilPile = BuildCatalogExamplePile();
            soilPile.PileBodyInput.SmartMagnumLL = 3.0; // 適用範囲 0〜2m の外

            var warnings = soilPile.ValidateSmartMagnumRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("LL")),
                "杭下拡大根固め部長さの範囲外が検出されない");
        }

        [TestMethod]
        public void RangeCheck_FlagsExcessiveOmegaP()
        {
            var soilPile = BuildCatalogExamplePile();
            soilPile.PileBodyInput.PileToeDia = 3200; // Den = 3.2m → ωp = 2.56、かつ 2.5m 超
            soilPile.UpdateProperties();              // Den は UpdatePileProperties で D に取り込まれる

            var warnings = soilPile.ValidateSmartMagnumRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("ωp")), "根固め部の拡大比の範囲外が検出されない");
            Assert.IsTrue(warnings.Any(w => w.Contains("Den")), "拡大根固め部径の上限超過が検出されない");
        }

        // ── 11. 適用メーカー / 杭体タイプの制限 ─────────────────────

        [TestMethod]
        public void ConstructionTypeOptions_OfferSmartMagnumOnlyForPrecastConcrete()
        {
            // 本工法はジャパンパイルの工法なので、既製コンクリート杭でのみ選べるようにする
            CollectionAssert.Contains(
                PileBodyInput.PrecastPileConstructionTypeOption,
                PileConstructionTypeNames.SmartMagnum,
                "既製コンクリート杭で Smart-MAGNUM を選べない");

            CollectionAssert.DoesNotContain(
                PileBodyInput.SteelPileConstructionTypeOption,
                PileConstructionTypeNames.SmartMagnum,
                "鋼管杭で Smart-MAGNUM が選べてしまう");

            CollectionAssert.DoesNotContain(
                PileBodyInput.InsituPileConstructionTypeOption,
                PileConstructionTypeNames.SmartMagnum,
                "場所打ち杭で Smart-MAGNUM が選べてしまう");
        }

        [TestMethod]
        public void MakerCheck_AcceptsJapanPileAndNeutralJisProducts()
        {
            // ジャパンパイルの節杭 (JP-NPH / JP-NPRC)
            var nph = MakeNodularSection(1100, 1200);
            nph.SelectedPrecastPile.Name = "NPH-1200-1100-標準-85-A";
            Assert.IsTrue(SoilPile.IsJapanPileSection(nph, out _));

            // メーカー中立の JIS 規格品は適用対象として扱う
            var jis = MakeStraightSection(1000);
            jis.SelectedPrecastPile.Name = "PHC-1000-標準-80-A";
            Assert.IsTrue(SoilPile.IsJapanPileSection(jis, out _));
        }

        [DataTestMethod]
        [DataRow("MS-hi105-300-標準型-A")]
        [DataRow("Hi-SC105-400-標準型-65-6")]
        [DataRow("DAM105-300-標準型-A-D13")]
        public void MakerCheck_RejectsOtherMakerProducts(string productName)
        {
            var section = MakeStraightSection(1000);
            section.SelectedPrecastPile.Name = productName;

            Assert.IsFalse(SoilPile.IsJapanPileSection(section, out string maker),
                $"{productName} が適用対象と判定されている");
            Assert.AreEqual("三谷セキサン", maker);
        }

        [TestMethod]
        public void MakerCheck_RejectsBfsSectionTypes()
        {
            // BF.S は専用の断面タイプを持つので、製品名に依らず判別できる
            foreach (string sectionType in new[] { PileTypeNames.BfsHead, PileTypeNames.BfsTip })
            {
                var section = new PileSection
                {
                    PileBodyType = PileTypeNames.PrecastConcrete,
                    PileSectionType = sectionType,
                };
                Assert.IsFalse(SoilPile.IsJapanPileSection(section, out string maker), sectionType);
                Assert.AreEqual("三谷セキサン", maker);
            }
        }

        [TestMethod]
        public void RangeCheck_FlagsOtherMakerProduct()
        {
            var soilPile = BuildCatalogExamplePile();
            // 最下段を三谷セキサンの製品に差し替える
            soilPile.PileBodyInput.PileBodySegments[^1].PileSection.SelectedPrecastPile.Name =
                "MS-hi105-1100-標準型-A";
            soilPile.UpdateProperties();

            var warnings = soilPile.ValidateSmartMagnumRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("三谷セキサン")),
                "他メーカー製品が適用範囲外として検出されない: " + string.Join(" / ", warnings));
        }

        [TestMethod]
        public void RangeCheck_FlagsNonPrecastConcreteBodyType()
        {
            var soilPile = BuildCatalogExamplePile();
            soilPile.PileBodyInput.PileBodyType = PileTypeNames.SteelPipe;

            var warnings = soilPile.ValidateSmartMagnumRange().ToList();
            Assert.IsTrue(warnings.Any(w => w.Contains("杭体タイプ")),
                "既製コンクリート杭以外が適用範囲外として検出されない: " + string.Join(" / ", warnings));
        }

        [TestMethod]
        public void RangeCheck_IsSilentForOtherConstructionTypes()
        {
            var soilPile = BuildSimplePile(PileConstructionTypeNames.Preboring, Sand, 30);
            Assert.AreEqual(0, soilPile.ValidateSmartMagnumRange().Count());
        }

        // ── ヘルパー ──────────────────────────────────────────────

        // PileBodyType / PileSectionType を先に決めないと、PileDiameter の setter が呼ぶ
        // RecalculatePileDia() が既定の場所打ち RC として径を上書きしてしまう。
        private static PileSection MakeNodularSection(double shaftDia, double nodeDia) => new()
        {
            PileBodyType = PileTypeNames.PrecastConcrete,
            PileSectionType = PileTypeNames.PhcNodular,
            PileDiameter = shaftDia,
            NodeDiameter = nodeDia,
        };

        private static PileSection MakeStraightSection(double dia) => new()
        {
            PileBodyType = PileTypeNames.PrecastConcrete,
            PileSectionType = PileTypeNames.Phc,
            PileDiameter = dia,
        };

        private static void ConfigureStraightSection(PileSection section, double dia, string productName)
        {
            section.PileBodyType = PileTypeNames.PrecastConcrete;
            section.PileSectionType = PileTypeNames.Phc;
            section.PileDiameter = dia;
            section.SelectedPrecastPile.Name = productName;
        }

        private static void ConfigureNodularSection(
            PileSection section, double shaftDia, double nodeDia, string productName)
        {
            section.PileBodyType = PileTypeNames.PrecastConcrete;
            section.PileSectionType = PileTypeNames.PhcNodular;
            section.PileDiameter = shaftDia;
            section.NodeDiameter = nodeDia;
            section.SelectedPrecastPile.Name = productName;
        }

        /// <summary>
        /// カタログ p.10 の計算例に相当する SoilPile を組み立てる。
        /// 杭頭 GL-1.0m、杭長 25m（上杭 φ1100 10m + 下杭 φ1200-1100 節杭 15m）、
        /// Den = 1900mm、LL = 1.0m。地盤は N 値のみ計算例に合わせる。
        /// </summary>
        private static SoilPile BuildCatalogExamplePile()
        {
            var upper = MakeStraightSection(1100);
            var lower = MakeNodularSection(shaftDia: 1100, nodeDia: 1200);

            var body = new PileBodyInput
            {
                // 杭体タイプを先に決める (PileBodySegments の setter が子断面へ同期するため)
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileConstructionType = PileConstructionTypeNames.SmartMagnum,
                PileToeDia = 1900,          // Den
                SmartMagnumLL = 1.0,
                SmartMagnumDes = 1400,      // 杭周面部の掘削径
                SmartMagnumWingLength = 10, // 拡翼掘削部長さ（LL を含む）
                SmartMagnumIsReinforcedCircum = false, // 標準型
                SettlePileToeDia = 1500,    // Smart-MAGNUM では Den で上書きされる
            };
            body.PileBodySegments =
            [
                new PileBodySegment { No = 1, SegmentLength = 10, SegmentDepth = 10, PileSection = upper },
                new PileBodySegment { No = 2, SegmentLength = 15, SegmentDepth = 25, PileSection = lower },
            ];

            // PileBodySegments の setter が親の杭体タイプを子断面へ同期し
            // ResetSectionProperties() で断面タイプ・径を既定値に戻すため、ここで設定し直す
            ConfigureStraightSection(upper, 1100, "PHC-1100-標準-80-A");
            ConfigureNodularSection(lower, 1100, 1200, "NPH-1200-1100-標準-85-A");

            // 杭頭 GL-1.0m、杭長 25m → 杭先端は標高 -26.0m
            // 砂質土は Ns の適用範囲 1〜30 に収まる値にしておく（範囲外だと警告が出る）
            var ground = MakeGround(
                layers:
                [
                    (bottomAltitude: -24.0, cls: Sand, nValue: 15, cohesive: 0),
                    (bottomAltitude: -30.0, cls: Gravel, nValue: 30, cohesive: 0),
                ],
                // Nu 範囲 (-26.0 〜 -24.0) に 30, 42 / Nl 範囲 (-30.1 〜 -26.0) に 44, 47, 48, 51
                masses:
                [
                    (-24.5, 30), (-25.5, 42),
                    (-26.5, 44), (-27.5, 47), (-28.5, 48), (-29.5, 51),
                ]);

            // 要素境界: 杭頭 / 上杭・下杭の境界 / 土層境界 / 杭先端
            return MakeSoilPile(body, ground, pileTopAltitude: -1.0, zLevels: [-1.0, -11.0, -24.0, -26.0]);
        }

        private static SoilPile BuildSimplePile(string constructionType, string soilClass, double nValue)
        {
            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileConstructionType = constructionType,
                PileToeDia = 1200,
                SettlePileToeDia = 1200,
            };
            body.PileBodySegments =
            [
                new PileBodySegment
                {
                    No = 1, SegmentLength = 20, SegmentDepth = 20, PileSection = MakeStraightSection(1000)
                },
            ];
            ConfigureStraightSection(body.PileBodySegments[0].PileSection, 1000, "PHC-1000-標準-80-A");

            var ground = MakeGround(
                layers: [(-25.0, soilClass, nValue, 100.0)],
                masses: [(-19.0, nValue), (-20.0, nValue), (-21.0, nValue)]);

            return MakeSoilPile(body, ground, pileTopAltitude: 0.0, zLevels: [0.0, -10.0, -20.0]);
        }

        private static GroundInput MakeGround(
            (double bottomAltitude, string cls, double nValue, double cohesive)[] layers,
            (double altitude, double nValue)[] masses)
        {
            var ground = new GroundInput();

            ground.GroundLayers = [.. layers.Select((l, i) => new GroundLayerInput
            {
                No = i + 1,
                BottomAltitude = l.bottomAltitude,
                GranularityClass = l.cls,
                NValue = l.nValue,
                Cohesive = l.cohesive,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true,
            })];

            ground.GroundMassesData = [.. masses.Select((m, i) => new GroundMassDataInput
            {
                No = i + 1,
                AltitudeDepth = m.altitude,
                NValue = m.nValue,
            })];

            return ground;
        }

        /// <summary>
        /// SoilPile を組み立てる。<c>zLevels</c> は杭頭から杭先端までの要素境界標高（降順）で、
        /// これが無いと <c>MatchGroundLayersAndPileBodySegments</c> が杭区間を生成せず
        /// 支持力がすべて 0 になる。
        /// </summary>
        private static SoilPile MakeSoilPile(
            PileBodyInput body, GroundInput ground, double pileTopAltitude, double[] zLevels)
        {
            ObservableCollection<PileZDataItem> zDataItems =
                [.. zLevels.Select(z => new PileZDataItem { Z = z })];

            var soilPile = new SoilPile();
            soilPile.Initialize(no: 1, groundNo: 1, groundInput: ground,
                                pileBodyNo: 1, pileBodyInput: body,
                                z: pileTopAltitude, zDataItems: zDataItems);
            soilPile.UpdateProperties();
            return soilPile;
        }
    }
}
