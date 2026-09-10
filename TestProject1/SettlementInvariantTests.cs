using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下（荷重伝達解析）の出口側を、<b>変換への追従</b>で検査する。
    ///
    /// <para>水平解析には出口側の網（符号反転・比例・メッシュ収束・平行移動/鏡映）を
    /// 入れてあるが、<b>単杭沈下には無かった</b>。群杭沈下は応力積分の側だけ
    /// <see cref="SettlementAnalysisTests"/> が見ている（荷重 0 → 沈下 0・対称性・
    /// 中心 &gt; 遠方・硬い地盤 → 小さい沈下・荷重 2 倍 → 沈下 2 倍）が、
    /// 杭の荷重-沈下曲線を作る側は素通りだった。</para>
    ///
    /// <para>ここで見るのは、答えの正しさではなく<b>物理として崩れていないこと</b>。
    /// 「荷重を増やしたのに沈下が戻る」「同じ入力で二度走らせて値が違う」
    /// 「地盤を硬くしたのに沈下が増える」は、答えを知らなくても誤りと言える。</para>
    /// </summary>
    [TestClass]
    public class SettlementInvariantTests
    {
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]        // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]    // 既製杭
        [DataRow("Example3_5", "PileExample3_5")]    // 鋼管杭
        public void TheLoadSettlementCurve_NeverFoldsBack(string groundName, string pileName)
        {
            var model = Build(groundName, pileName);
            if (model == null) return;

            var offenders = new List<string>();
            int checked_ = 0;

            foreach (var (label, curve) in Curves(model))
            {
                if (curve.Count < 2)
                {
                    offenders.Add($"{label}: 曲線が {curve.Count} 点しかありません");
                    continue;
                }

                for (int i = 1; i < curve.Count; i++)
                {
                    checked_++;

                    // 荷重制御なので荷重は増える一方
                    if (curve[i].F0s < curve[i - 1].F0s - 1e-9)
                        offenders.Add($"{label}[{i}]: 荷重が戻りました "
                            + $"{curve[i - 1].F0s:F3} → {curve[i].F0s:F3}");

                    // 荷重が増えたのに沈下が戻ることはない
                    if (curve[i].D0s < curve[i - 1].D0s - 1e-6)
                        offenders.Add($"{label}[{i}]: 荷重 {curve[i].F0s:F1} kN で沈下が戻りました "
                            + $"{curve[i - 1].D0s:F4} → {curve[i].D0s:F4} mm");
                }
            }

            TestSource.AssertScanned(checked_, 50, $"{groundName} で見比べた曲線の刻み");
            AssertNone(offenders, "荷重-沈下曲線が折り返しています");
        }

        /// <summary>
        /// 沈下の向きが荷重の向きと合っていること。
        ///
        /// <para><b>ゼロ交差は原点ではない。</b>杭は自重で沈むので、頭部荷重が
        /// わずかに負（引抜き側）でも沈下は下向きに出る。だから不感帯を
        /// <b>杭の自重</b>に結び付ける（固定値で置くと、自重の大きい
        /// 場所打ちRC だけ誤検出になる。実際に 100kN で置いて Example9 が引っかかった）。</para>
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_5", "PileExample3_5")]
        public void TheSettlement_HasTheSameSignAsTheLoad(string groundName, string pileName)
        {
            var model = Build(groundName, pileName);
            if (model == null) return;

            var soilPile = model!.ElementDivision.SoilPiles[0];
            double pileWeight = PileWeightOf(soilPile);
            Assert.IsTrue(pileWeight > 0, $"杭の自重が {pileWeight} です (定義に届いていない)");

            var offenders = new List<string>();
            int checked_ = 0;

            foreach (var (label, curve) in Curves(model))
            {
                // 荷重の刻み幅ぶんは、交差点の読み取り誤差として見逃す
                double step = curve.Count > 1
                    ? Math.Abs(curve[1].F0s - curve[0].F0s) : 0.0;
                double band = pileWeight + step;

                foreach (var p in curve)
                {
                    if (Math.Abs(p.F0s) <= band) continue;

                    checked_++;
                    if (Math.Sign(p.F0s) != Math.Sign(p.D0s) && Math.Abs(p.D0s) > 1e-6)
                        offenders.Add($"{label}: 荷重 {p.F0s:F1} kN に対して沈下 {p.D0s:F4} mm "
                            + $"（自重 {pileWeight:F1} kN + 刻み {step:F1} kN の外側）");
                }
            }

            TestSource.AssertScanned(checked_, 20, $"{groundName} で見比べた点");
            AssertNone(offenders, "沈下の向きが荷重の向きと合っていません");
        }

        /// <summary>
        /// 頭部荷重 0 で、杭が浮かないこと。自重は沈下させる方向にしか働かない。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_5", "PileExample3_5")]
        public void WithNoHeadLoad_ThePileDoesNotRise(string groundName, string pileName)
        {
            var model = Build(groundName, pileName);
            if (model == null) return;

            int checked_ = 0;
            foreach (var (label, curve) in Curves(model))
            {
                var points = curve.Select(p => (f: p.F0s, d: p.D0s)).ToList();
                if (points.Count < 2 || points.First().f > 0 || points.Last().f < 0) continue;

                checked_++;
                double atZero = Interpolate(points, 0.0);

                Assert.IsTrue(atZero >= -1e-6,
                    $"{label}: 頭部荷重 0 で杭が {atZero:F4} mm 浮いています。"
                    + "自重は沈下させる方向にしか働きません");
            }

            TestSource.AssertScanned(checked_, 1, $"{groundName} で 0 をまたぐ曲線");
        }

        /// <summary>
        /// 同じ入力で二度走らせて、同じ答えになること。
        ///
        /// このリポジトリでは<b>並列に集めた寄与の加算順</b>で答えが揺れた前例がある
        /// （水平解析、2026-08-21）。許容差は置かない——揺れないことが要求。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_5", "PileExample3_5")]
        public void RunningItTwice_GivesTheSameAnswer(string groundName, string pileName)
        {
            var model = Build(groundName, pileName);
            if (model == null) return;

            var soilPile = model!.ElementDivision.SoilPiles[0];
            var first = Snapshot(new VerticalLoadTransferMethod(model, soilPile));
            var second = Snapshot(new VerticalLoadTransferMethod(model, soilPile));

            TestSource.AssertScanned(first.Count, 50, $"{groundName} の曲線の点");

            Assert.AreEqual(first.Count, second.Count,
                $"二度目の点数が違います ({first.Count} → {second.Count})");

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].f, second[i].f, 0.0,
                    $"[{i}] 荷重が揺れました {first[i].f} → {second[i].f}");
                Assert.AreEqual(first[i].d, second[i].d, 0.0,
                    $"[{i}] 沈下が揺れました {first[i].d} → {second[i].d}");
            }
        }

        /// <summary>
        /// 地盤を硬くしたら、同じ荷重での沈下は増えないこと。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_5", "PileExample3_5")]
        public void StifferGround_DoesNotSettleMore(string groundName, string pileName)
        {
            var soft = Build(groundName, pileName);
            if (soft == null) return;

            var stiff = Build(groundName, pileName);
            if (stiff == null) return;

            int raised = 0;
            foreach (var ground in stiff!.GroundsInput ?? [])
            {
                foreach (var layer in ground.GroundLayers ?? [])
                {
                    if (layer.NValue <= 0) continue;
                    layer.NValue *= 2.0;
                    raised++;
                }
            }

            TestSource.AssertScanned(raised, 3, $"{groundName} で硬くした土層");

            // 土層を変えたので、土層-杭セットを組み直す
            stiff.GenerateSoilPiles();

            var softCurve = Snapshot(new VerticalLoadTransferMethod(soft!, soft!.ElementDivision.SoilPiles[0]));
            var stiffCurve = Snapshot(new VerticalLoadTransferMethod(stiff, stiff.ElementDivision.SoilPiles[0]));

            // 両方の曲線が覆っている荷重の範囲で見比べる
            double from = Math.Max(softCurve.First().f, stiffCurve.First().f);
            double to = Math.Min(softCurve.Last().f, stiffCurve.Last().f);
            Assert.IsTrue(to > from, $"見比べられる荷重の範囲がありません ({from:F1} 〜 {to:F1})");

            var offenders = new List<string>();
            int compared = 0;

            for (int k = 1; k <= 9; k++)
            {
                double f = from + (to - from) * k / 10.0;
                if (f <= 0) continue;   // 引抜き側は周面抵抗の向きが変わるので見ない

                double dSoft = Interpolate(softCurve, f);
                double dStiff = Interpolate(stiffCurve, f);
                compared++;

                // 丸めと刻みの差を吸収する程度の余裕だけ持たせる
                if (dStiff > dSoft + Math.Max(1e-6, Math.Abs(dSoft) * 1e-6))
                    offenders.Add($"荷重 {f:F1} kN: N 値 2 倍で沈下が {dSoft:F4} → {dStiff:F4} mm に増えました");
            }

            TestSource.AssertScanned(compared, 3, $"{groundName} で見比べた荷重");
            AssertNone(offenders, "地盤を硬くしたのに沈下が増えています");

            // N 値が沈下に効いていること。
            // 効いていなければ両者は同一になり、上の不等式は成立してしまう
            // （掃引した値が渡っていない誤りは、こうして静かに通る）。
            double mid = from + (to - from) * 0.5;
            if (mid > 0)
            {
                double dSoftMid = Interpolate(softCurve, mid);
                double dStiffMid = Interpolate(stiffCurve, mid);
                Assert.AreNotEqual(dSoftMid, dStiffMid,
                    $"N 値を 2 倍にしても荷重 {mid:F1} kN の沈下が {dSoftMid:F6} mm から動きません。"
                    + "N 値が沈下解析に渡っていない可能性があります");
            }
        }

        /// <summary>
        /// 土層を同じ性質のまま 2 分割しても、答えが変わらないこと。
        ///
        /// <para>物理は「土の profile」で決まるので、それを何枚に区切って入力したかで
        /// 答えが動いてはいけない。層を割ると杭の要素分割境界も増えるため、
        /// 厳密には離散化誤差ぶん動く。<b>実測すると全層を 2 分割しても
        /// 最大 0.32%、ほとんどは 0.05% 未満</b>だった（杭区間 9→16 / 5→8）。
        /// 水平解析が要素分割で 10〜27% 動くのとは対照的で、沈下は頑健である。</para>
        ///
        /// <para>だから 1% を上限に置く。実測の 3 倍の余裕があり、
        /// 層と杭区間の対応付けを取り違える類の誤りはこれを軽々と超える。</para>
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_5", "PileExample3_5")]
        public void SplittingEveryLayerInHalf_BarelyMovesTheAnswer(string groundName, string pileName)
        {
            var plain = Build(groundName, pileName);
            if (plain == null) return;

            var split = Build(groundName, pileName);
            if (split == null) return;

            int divided = SplitEveryLayerInHalf(split!);
            TestSource.AssertScanned(divided, 3, $"{groundName} で 2 分割した土層");

            // 土層を変えたので、土層-杭セットを組み直す
            split.GenerateSoilPiles();

            var before = Snapshot(new VerticalLoadTransferMethod(plain!, plain!.ElementDivision.SoilPiles[0]));
            var after = Snapshot(new VerticalLoadTransferMethod(split, split.ElementDivision.SoilPiles[0]));

            double from = Math.Max(before.First().f, after.First().f);
            double to = Math.Min(before.Last().f, after.Last().f);
            Assert.IsTrue(to > from, $"見比べられる荷重の範囲がありません ({from:F1} 〜 {to:F1})");

            var offenders = new List<string>();
            int compared = 0;

            for (int k = 1; k <= 9; k++)
            {
                double f = from + (to - from) * k / 10.0;
                if (f <= 0) continue;

                double dBefore = Interpolate(before, f);
                double dAfter = Interpolate(after, f);

                // 沈下がごく小さいところは相対差が荒れるので見ない
                if (Math.Abs(dBefore) < 0.01) continue;

                compared++;
                double diff = Math.Abs(dAfter - dBefore) / Math.Abs(dBefore) * 100.0;
                if (diff > 1.0)
                    offenders.Add($"荷重 {f:F1} kN: {dBefore:F4} → {dAfter:F4} mm ({diff:F2}% 違う)");
            }

            TestSource.AssertScanned(compared, 3, $"{groundName} で見比べた荷重");
            AssertNone(offenders, "土層の区切り方を変えただけで答えが動いています");
        }

        /// <summary>
        /// すべての土層を、同じ性質の 2 枚に割る。割った枚数を返す。
        ///
        /// 層境界は下端GL深度・下端Z・層厚の<b>3 つが独立に持たれている</b>
        /// （どれかを書けば他が追従する作りではない）ので、3 つとも揃えて書く。
        /// </summary>
        private static int SplitEveryLayerInHalf(InputModel model)
        {
            int divided = 0;

            foreach (var ground in model.GroundsInput ?? [])
            {
                if (ground.GroundLayers == null) continue;

                var result = new List<GroundLayerInput>();
                foreach (var layer in ground.GroundLayers.ToList())
                {
                    double thickness = layer.LayerThickness;
                    if (thickness <= 0.02) { result.Add(layer); continue; }

                    var upper = layer.DeepCopy();
                    upper.LayerThickness = thickness / 2.0;
                    upper.BottomGLDepth = layer.BottomGLDepth - thickness / 2.0;
                    upper.BottomAltitude = layer.BottomAltitude + thickness / 2.0;

                    layer.LayerThickness = thickness / 2.0;

                    result.Add(upper);
                    result.Add(layer);
                    divided++;
                }

                for (int i = 0; i < result.Count; i++) result[i].No = i + 1;
                ground.GroundLayers = new ObservableCollection<GroundLayerInput>(result);
            }

            return divided;
        }

        // ---- 道具 ----

        private static InputModel? Build(string groundName, string pileName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return null; }

            var soilPiles = model.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0)
            {
                Assert.Inconclusive("要素分割された地盤杭セットがありません");
                return null;
            }
            return model;
        }

        private static IEnumerable<(string Label, ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> Curve)>
            Curves(InputModel model)
        {
            var vltm = new VerticalLoadTransferMethod(model, model.ElementDivision.SoilPiles[0]);
            yield return ("常時", vltm.LoadDisplacements);
            yield return ("限界", vltm.LoadDisplacementsLimit);
        }

        /// <summary>
        /// 杭の自重。<c>VerticalLoadTransferMethod.Initialize</c> と同じ定義
        /// （区間ごとの断面自重 × 区間長の総和）。
        /// </summary>
        private static double PileWeightOf(SoilPile soilPile)
            => (soilPile.PileCircumVerticals ?? []).Sum(
                v => v.PileBodySegment.PileSection.W * v.L);

        private static List<(double f, double d)> Snapshot(VerticalLoadTransferMethod vltm)
            => vltm.LoadDisplacements.Select(p => (p.F0s, p.D0s)).ToList();

        /// <summary>荷重 <paramref name="f"/> における沈下を、曲線から線形に読む。</summary>
        private static double Interpolate(List<(double f, double d)> curve, double f)
        {
            for (int i = 1; i < curve.Count; i++)
            {
                if (curve[i].f < f) continue;

                double span = curve[i].f - curve[i - 1].f;
                if (Math.Abs(span) < 1e-12) return curve[i].d;

                double t = (f - curve[i - 1].f) / span;
                return curve[i - 1].d + (curve[i].d - curve[i - 1].d) * t;
            }
            return curve[^1].d;
        }

        private static void AssertNone(List<string> offenders, string what)
        {
            Assert.AreEqual(0, offenders.Count,
                what + ":" + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", offenders.Take(20))
                + (offenders.Count > 20 ? $"{Environment.NewLine}  ...ほか {offenders.Count - 20} 件" : ""));
        }
    }
}
