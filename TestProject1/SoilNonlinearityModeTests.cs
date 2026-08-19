using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 地盤 (p-y) 非線形性の 3 段階 (線形 / kh 低減のみ / kh 低減 + py 頭打ち) の検証。
    /// </summary>
    [TestClass]
    public class SoilNonlinearityModeTests
    {
        private const double Y0 = 0.01; // m (= 1cm)

        /// <summary>kh0 = 60000 kN/m³ 相当、py = κ·Kp·σz' = 3×3×10 = 90 kN/m² となるテスト土層。</summary>
        private static HorizontalSoilReactionItem BuildItem()
        {
            var item = new HorizontalSoilReactionItem();
            item.SetParameters(
                name: "Test", soilType: "砂質土", gamma: 10, b: 1.0, e0: 1000,
                zTop: 1, zBtm: 0, xi: 1, rOnB: 1, nValue: 10, phi: 30, cu: 0,
                sigmaZPrimeTop: 10, sigmaZPrimeBtm: 10);
            return item;
        }

        private static double YieldDisp(HorizontalSoilReactionItem item)
            => Math.Pow(item.PyFrontTop / item.Kh0, 2) / Y0;

        // ── 線形モード ──────────────────────────────────────────────

        [TestMethod]
        public void Linear_P_IsProportionalToY_WithKh0()
        {
            var item = BuildItem();
            double py = item.PyFrontTop;

            // 変位に依らず p = kh0 × y（原点を通る直線）
            foreach (double y in new[] { 0.0001, 0.001, Y0, 0.05, 0.5 })
            {
                double p = item.GetP(y, py, SoilNonlinearityMode.Linear);
                Assert.AreEqual(item.Kh0 * y, p, item.Kh0 * y * 1e-9,
                    $"線形モードでは p = kh0×y であるべき (y={y})");
            }
        }

        [TestMethod]
        public void Linear_TangentEqualsSecant_AtAllDisplacements()
        {
            var item = BuildItem();
            double py = item.PyFrontTop;

            foreach (double y in new[] { 0.0001, 0.001, Y0, 0.5 })
            {
                double kTan = item.GetSoilTangentReactionCoefficient(y, isTop: true, isFront: true, SoilNonlinearityMode.Linear);
                double kSec = item.GetSoilSecantReactionCoefficient(y, isTop: true, isFront: true, SoilNonlinearityMode.Linear);
                Assert.AreEqual(kSec, kTan, kSec * 1e-12, $"線形なら接線 = 割線 (y={y})");
            }
        }

        [TestMethod]
        public void Linear_IsNotYielded_EvenBeyondYieldDisplacement()
        {
            var item = BuildItem();
            double yWayPastYield = 10.0 * YieldDisp(item);
            Assert.IsFalse(item.IsYieldedAtY(yWayPastYield, isTop: true, isFront: true, SoilNonlinearityMode.Linear));
        }

        // ── kh 低減のみ ─────────────────────────────────────────────

        [TestMethod]
        public void KhReduction_FollowsSqrtLaw_AndIgnoresPy()
        {
            var item = BuildItem();
            double py = item.PyFrontTop;
            double yy = YieldDisp(item);

            // 降伏変位を大きく超えた点でも sqrt 則のまま: p = kh0 × √(y0 × y)
            double y = 5.0 * yy;
            double expected = item.Kh0 * Math.Sqrt(Y0 * y);
            double p = item.GetP(y, py, SoilNonlinearityMode.KhReduction);

            Assert.AreEqual(expected, p, expected * 1e-9, "kh 低減のみでは py で頭打ちしない");
            Assert.IsTrue(p > py, $"py={py} を上回るはず（実測 p={p}）");
        }

        [TestMethod]
        public void KhReduction_IsNotYielded_EvenBeyondYieldDisplacement()
        {
            var item = BuildItem();
            double yWayPastYield = 10.0 * YieldDisp(item);
            Assert.IsFalse(item.IsYieldedAtY(yWayPastYield, isTop: true, isFront: true, SoilNonlinearityMode.KhReduction));
        }

        // ── kh 低減 + py 頭打ち ────────────────────────────────────

        [TestMethod]
        public void KhReductionWithPy_CapsAtPy_WithinOnePercent()
        {
            var item = BuildItem();
            double py = item.PyFrontTop;
            double yy = YieldDisp(item);

            // |y| = 10·yy でも py 超過は 0.9% 以内 (PostYieldTangentRatio = 0.002)
            double p = item.GetP(10.0 * yy, py, SoilNonlinearityMode.KhReductionWithPy);
            Assert.IsTrue(p >= py, "降伏後は py 以上");
            Assert.IsTrue(p <= py * 1.01, $"降伏後の py 超過は 1% 未満であるべき（実測 {p / py:P2}）");
        }

        [TestMethod]
        public void KhReductionWithPy_IsYielded_BeyondYieldDisplacement()
        {
            var item = BuildItem();
            double yy = YieldDisp(item);
            Assert.IsFalse(item.IsYieldedAtY(0.9 * yy, isTop: true, isFront: true, SoilNonlinearityMode.KhReductionWithPy));
            Assert.IsTrue(item.IsYieldedAtY(1.1 * yy, isTop: true, isFront: true, SoilNonlinearityMode.KhReductionWithPy));
        }

        // ── モード間の関係 ─────────────────────────────────────────

        [TestMethod]
        public void AtReferenceDisplacement_LinearAndKhReduction_Coincide()
        {
            // kh0 は y = y0 (= 1cm) における水平地盤反力係数として定義されるので、
            // その点では「線形 (kh0 固定)」と「kh 低減」が一致する。
            var item = BuildItem();
            double py = item.PyFrontTop;

            double pLinear = item.GetP(Y0, py, SoilNonlinearityMode.Linear);
            double pReduction = item.GetP(Y0, py, SoilNonlinearityMode.KhReduction);

            Assert.AreEqual(pLinear, pReduction, pLinear * 1e-9);
        }

        [TestMethod]
        public void StiffnessOrdering_LinearIsSofterThanKhReduction_BelowReferenceDisplacement()
        {
            // y < y0 では sqrt 則の方が硬い (kh = kh0/√(y/y0) > kh0)、
            // y > y0 では逆転する。
            var item = BuildItem();
            double py = item.PyFrontTop;

            double ySmall = 0.1 * Y0;
            Assert.IsTrue(item.GetP(ySmall, py, SoilNonlinearityMode.KhReduction)
                        > item.GetP(ySmall, py, SoilNonlinearityMode.Linear),
                "y < y0 では kh 低減モードの方が p が大きい");

            double yLarge = 10.0 * Y0;
            Assert.IsTrue(item.GetP(yLarge, py, SoilNonlinearityMode.KhReduction)
                        < item.GetP(yLarge, py, SoilNonlinearityMode.Linear),
                "y > y0 では線形モードの方が p が大きい");
        }

        [TestMethod]
        public void PyCap_OnlyAffectsKhReductionWithPy()
        {
            var item = BuildItem();
            double py = item.PyFrontTop;
            double y = 5.0 * YieldDisp(item);

            double pWithCap = item.GetP(y, py, SoilNonlinearityMode.KhReductionWithPy);
            double pNoCap = item.GetP(y, py, SoilNonlinearityMode.KhReduction);

            Assert.IsTrue(pWithCap < pNoCap, "py 頭打ちありの方が小さい反力になる");
        }

        // ── グラフ (GetP) と FEM (割線剛性) の一致 ────────────────

        [TestMethod]
        public void GetP_MatchesSecantStiffness_InAllModes()
        {
            // 「計算とグラフを同じ内容にする」ための回帰テスト。
            // GetP (表示用) と GetSoilSecantReactionCoefficient (FEM 用) は
            // 同一の p-y 曲線から導かれ、p = K_sec × y / (B·Δz/2) で厳密一致する。
            var item = BuildItem();
            double py = item.PyFrontTop;
            double areaScale = item.B * (item.ZTop - item.ZBtm) * 0.5;
            double yy = YieldDisp(item);

            foreach (var mode in SoilNonlinearityModes.All)
            {
                foreach (double y in new[] { 0.0005, 0.002, Y0, 0.9 * yy, 1.2 * yy, 5.0 * yy, 0.3 })
                {
                    double pFromGraph = item.GetP(y, py, mode);
                    double kSec = item.GetSoilSecantReactionCoefficient(y, isTop: true, isFront: true, mode);
                    double pFromFem = kSec * y / areaScale;
                    Assert.AreEqual(pFromFem, pFromGraph, Math.Max(pFromFem, 1.0) * 1e-9,
                        $"表示用 GetP と FEM 割線剛性が不一致 (mode={mode}, y={y})");
                }
            }
        }

        // ── 降伏境界の接線連続性 (post-yield 勾配を 0.2% に下げた代償の吸収) ──

        [TestMethod]
        public void GetkhTan_YieldBoundary_IsSmoothed()
        {
            var item = BuildItem();
            double kh0 = item.Kh0;
            double py = item.PyFrontTop;
            double yy = YieldDisp(item);

            double justBelow = HorizontalSoilReactionItem.GetkhTan(kh0, 0.999 * yy, py);
            double justAbove = HorizontalSoilReactionItem.GetkhTan(kh0, 1.001 * yy, py);

            Assert.IsTrue(justAbove > 0, "降伏直後も接線は正値");
            // 降伏境界を跨いでも接線は 2× 以内（ブレンドなしなら 500× 落ちる）
            double jump = justBelow / justAbove;
            Assert.IsTrue(jump < 2.0, $"降伏境界の接線ジャンプは 2× 未満であるべき（実測 {jump:F1}×）");

            // ブレンド区間を抜けた後は post-yield 一定勾配 (= 降伏境界接線の 0.2%)
            double plateau = HorizontalSoilReactionItem.GetkhTan(kh0, 2.0 * yy, py);
            double yieldBoundaryTangent = kh0 * kh0 * Y0 / (2.0 * py);
            Assert.AreEqual(0.002 * yieldBoundaryTangent, plateau, 0.002 * yieldBoundaryTangent * 1e-9);
        }

        [TestMethod]
        public void GetkhTan_IsMonotonicallyDecreasing_AcrossYieldBoundary()
        {
            var item = BuildItem();
            double kh0 = item.Kh0;
            double py = item.PyFrontTop;
            double yy = YieldDisp(item);

            double prev = double.MaxValue;
            for (double f = 0.5; f <= 3.0; f += 0.02)
            {
                double t = HorizontalSoilReactionItem.GetkhTan(kh0, f * yy, py);
                Assert.IsTrue(t <= prev + 1e-9, $"接線は単調減少であるべき (|y|/yy={f:F2}: {t:E3} > {prev:E3})");
                prev = t;
            }
        }

        // ── 旧 JSON 互換 ───────────────────────────────────────────

        [TestMethod]
        public void LegacyJson_IsSoilNonLinearTrue_MapsToKhReductionWithPy()
        {
            var lc = JsonSerializer.Deserialize<LoadCase>("""{"LoadName":"U1","IsSoilNonLinear":true}""");
            Assert.IsNotNull(lc);
            Assert.AreEqual(SoilNonlinearityMode.KhReductionWithPy, lc!.SoilNonlinearityMode);
        }

        [TestMethod]
        public void LegacyJson_IsSoilNonLinearFalse_MapsToLinear()
        {
            var lc = JsonSerializer.Deserialize<LoadCase>("""{"LoadName":"U1","IsSoilNonLinear":false}""");
            Assert.IsNotNull(lc);
            Assert.AreEqual(SoilNonlinearityMode.Linear, lc!.SoilNonlinearityMode);
        }

        [TestMethod]
        public void NewJson_RoundTripsSoilNonlinearityMode_AndDropsLegacyKey()
        {
            var lc = new LoadCase { LoadName = "U1", SoilNonlinearityMode = SoilNonlinearityMode.KhReduction };
            string json = JsonSerializer.Serialize(lc);

            Assert.IsFalse(json.Contains("IsSoilNonLinear"),
                "新しい保存ファイルには旧 bool キーを書き出さない（読み戻しで上書きされるのを防ぐため）");

            var restored = JsonSerializer.Deserialize<LoadCase>(json);
            Assert.AreEqual(SoilNonlinearityMode.KhReduction, restored!.SoilNonlinearityMode);
        }

        [TestMethod]
        public void LegacyBoolProperty_MapsBothWays()
        {
            var lc = new LoadCase { SoilNonlinearityMode = SoilNonlinearityMode.KhReduction };
            Assert.IsTrue(lc.IsSoilNonLinear, "Linear 以外は旧 API で true");

            lc.IsSoilNonLinear = false;
            Assert.AreEqual(SoilNonlinearityMode.Linear, lc.SoilNonlinearityMode);

            lc.IsSoilNonLinear = true;
            Assert.AreEqual(SoilNonlinearityMode.KhReductionWithPy, lc.SoilNonlinearityMode);
        }

        // ── 表示名 / UI 用リソース ─────────────────────────────────

        [TestMethod]
        public void DisplayTexts_AreDistinctAndNonEmpty_ForAllModes()
        {
            Assert.AreEqual(3, SoilNonlinearityModes.All.Count);

            var conv = new PileDesign.Converters.SoilNonlinearityModeToTextConverter();
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var mode in SoilNonlinearityModes.All)
            {
                string full = (string)conv.Convert(mode, typeof(string), null!, null!);
                string @short = (string)conv.Convert(mode, typeof(string), "short", null!);

                Assert.IsFalse(string.IsNullOrWhiteSpace(full), $"{mode} の表示名が空");
                Assert.IsFalse(string.IsNullOrWhiteSpace(@short), $"{mode} の短縮表示名が空");
                Assert.AreEqual(SoilNonlinearityModes.ToText(mode), full);
                Assert.AreEqual(SoilNonlinearityModes.ToShortText(mode), @short);
                Assert.IsTrue(seen.Add(full), $"表示名が重複: {full}");
            }
        }

        [TestMethod]
        public void SoilNonlinearityMode_Change_RaisesLegacyPropertyNotification()
        {
            var lc = new LoadCase();
            var changed = new System.Collections.Generic.List<string>();
            lc.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            lc.SoilNonlinearityMode = SoilNonlinearityMode.Linear;

            CollectionAssert.Contains(changed, nameof(LoadCase.SoilNonlinearityMode));
            CollectionAssert.Contains(changed, nameof(LoadCase.IsSoilNonLinear));
        }
    }
}
