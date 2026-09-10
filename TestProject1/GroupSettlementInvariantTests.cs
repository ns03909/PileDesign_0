using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下（スタインブレナー）の<b>サービス層</b>を、変換への追従で検査する。
    ///
    /// <para><see cref="SettlementAnalysisTests"/> は積分の素の関数
    /// (<c>Steinnbrener.CalcSettlement</c>) を見ているが、そこから上、
    /// <b>荷重の生成・層の切り詰め・杭位置とグリッドの 2 経路</b>を通す
    /// <c>SettlementAnalysisService.PerformSettlementAnalysis</c> を叩くテストは
    /// 1 つも無かった。単杭沈下と同じ形の網をここに置く。</para>
    ///
    /// <para>見るのは答えの正しさではなく、物理と手続きとして崩れていないこと。
    /// 「荷重を 2 倍にしたのに沈下が 2 倍にならない」「地盤を硬くしたのに沈下が増える」
    /// 「土層の区切り方を変えたら答えが動く」「模型を平行移動したら沈下が変わる」は、
    /// 答えを知らなくても誤りと言える。</para>
    ///
    /// <para><b>杭位置とグリッドは別の経路で計算されている</b>
    /// (<c>CalculatePileSettlements</c> と <c>CalculateGridSettlements</c>)。
    /// 同じ点なら同じ値でなければならないので、そこも突き合わせる。</para>
    /// </summary>
    [TestClass]
    public class GroupSettlementInvariantTests
    {
        [TestMethod]
        public void RunningItTwice_GivesTheSameAnswer()
        {
            var (gs, piles) = Scene();
            var first = Run(gs, piles);
            var (gs2, piles2) = Scene();
            var second = Run(gs2, piles2);

            AssertSamePileSettlements(first, second, 0.0, "二度目で答えが揺れました");
        }

        /// <summary>
        /// 荷重を 2 倍にしたら沈下も 2 倍になること（弾性なので線形）。
        /// </summary>
        [TestMethod]
        public void DoublingTheLoad_DoublesTheSettlement()
        {
            var (gs, piles) = Scene();
            var single = Run(gs, piles);

            var (gs2, piles2) = Scene();
            foreach (var load in gs2.RectLoads) load.QA *= 2.0;
            var doubled = Run(gs2, piles2);

            int compared = 0;
            foreach (var kv in single.PileSettlements_mm)
            {
                double d = doubled.PileSettlements_mm[kv.Key];
                compared++;
                Assert.AreEqual(kv.Value * 2.0, d, Math.Max(1e-9, Math.Abs(kv.Value) * 1e-9),
                    $"杭 {kv.Key}: 荷重 2 倍で沈下が {kv.Value:F6} → {d:F6} mm。2 倍になっていません");
            }

            TestSource.AssertScanned(compared, 3, "見比べた杭");
        }

        /// <summary>
        /// 地盤を硬くしたら沈下は増えないこと。<b>かつ、硬さが効いていること</b>。
        /// 効いていなければ両者は同一になり、不等式だけでは通ってしまう。
        /// </summary>
        [TestMethod]
        public void StifferGround_SettlesLess()
        {
            var (gs, piles) = Scene();
            var soft = Run(gs, piles);

            var (gs2, piles2) = Scene();
            foreach (var layer in gs2.SettlementSoilLayers) layer.Ek *= 2.0;
            var stiff = Run(gs2, piles2);

            var offenders = new List<string>();
            int moved = 0;

            foreach (var kv in soft.PileSettlements_mm)
            {
                double s = stiff.PileSettlements_mm[kv.Key];
                if (s > kv.Value + Math.Max(1e-9, Math.Abs(kv.Value) * 1e-9))
                    offenders.Add($"杭 {kv.Key}: Ek 2 倍で沈下が {kv.Value:F6} → {s:F6} mm に増えました");
                if (Math.Abs(s - kv.Value) > 1e-9) moved++;
            }

            AssertNone(offenders, "地盤を硬くしたのに沈下が増えています");

            Assert.IsTrue(moved > 0,
                "Ek を 2 倍にしても沈下が動きません。変形係数が沈下解析に渡っていない可能性があります");
        }

        /// <summary>
        /// 土層を同じ性質のまま 2 分割しても、答えが変わらないこと。
        ///
        /// スタインブレナーは層ごとの寄与を足し合わせるので、同じ profile を
        /// 何枚に区切って入力したかで答えが動いてはいけない。
        /// 層の上端・下端の取り違えはここで露見する。
        /// </summary>
        [TestMethod]
        public void SplittingEveryLayerInHalf_DoesNotChangeTheAnswer()
        {
            var (gs, piles) = Scene();
            var plain = Run(gs, piles);

            var (gs2, piles2) = Scene();
            int divided = SplitEveryLayerInHalf(gs2);
            TestSource.AssertScanned(divided, 2, "2 分割した土層");
            var split = Run(gs2, piles2);

            AssertSamePileSettlements(plain, split, 1e-6,
                "土層の区切り方を変えただけで答えが動いています");
        }

        /// <summary>
        /// 模型ごと平行移動しても、杭ごとの沈下が変わらないこと。
        /// </summary>
        [TestMethod]
        public void TranslatingEverything_DoesNotChangeTheSettlement()
        {
            var (gs, piles) = Scene();
            var here = Run(gs, piles);

            const double dx = 123.5, dy = -87.25;
            var (gs2, piles2) = Scene();
            foreach (var load in gs2.RectLoads)
            {
                load.X1 += dx; load.X2 += dx;
                load.Y1 += dy; load.Y2 += dy;
            }
            foreach (var p in piles2) { p.X += dx; p.Y += dy; }
            var there = Run(gs2, piles2, xMin: -6 + dx, xMax: 6 + dx, yMin: -6 + dy, yMax: 6 + dy);

            AssertSamePileSettlements(here, there, 1e-6,
                $"({dx}, {dy}) 平行移動しただけで沈下が変わっています");
        }

        /// <summary>
        /// X を反転したら、沈下も反転して対応すること。
        /// </summary>
        [TestMethod]
        public void MirroringX_MirrorsTheSettlement()
        {
            var (gs, piles) = Scene();
            var plain = Run(gs, piles);

            var (gs2, piles2) = Scene();
            foreach (var load in gs2.RectLoads)
            {
                (load.X1, load.X2) = (-load.X2, -load.X1);
            }
            foreach (var p in piles2) p.X = -p.X;
            var mirrored = Run(gs2, piles2);

            // 杭番号は位置に紐づくので、同じ番号どうしが対応する
            AssertSamePileSettlements(plain, mirrored, 1e-6,
                "X を反転したのに沈下が対応しません");
        }

        /// <summary>
        /// 結果が杭の本数ぶんあること。
        ///
        /// 杭ごとの沈下は<b>杭番号を鍵にした辞書</b>で返る。番号が重なっていると
        /// 静かに上書きされ、件数が減る。しかも受け側は件数が 2 未満だと
        /// そのケースを黙って飛ばす (<c>EvaluationService</c>) ので、
        /// 減ったことに気づけない。
        /// </summary>
        [TestMethod]
        public void EveryPile_GetsItsOwnSettlement()
        {
            var (gs, piles) = Scene();
            var result = Run(gs, piles);

            Assert.AreEqual(piles.Count, result.PileSettlements_mm.Count,
                $"杭 {piles.Count} 本に対して沈下が {result.PileSettlements_mm.Count} 件しかありません。"
                + "杭番号が重なっていると静かに上書きされます");

            foreach (var p in piles)
                Assert.IsTrue(result.PileSettlements_mm.ContainsKey(p.PileNo),
                    $"杭番号 {p.PileNo} の沈下がありません");
        }

        /// <summary>
        /// 杭位置とグリッドは別の経路で計算されているので、<b>同じ点なら同じ値</b>であること。
        /// </summary>
        [TestMethod]
        public void APileOnAGridNode_MatchesThatGridValue()
        {
            var (gs, piles) = Scene();
            var result = Run(gs, piles);

            Assert.IsTrue(result.SettlementGridData.Count > 0, "グリッドの結果がありません");

            int compared = 0;
            foreach (var p in piles)
            {
                var node = result.SettlementGridData.FirstOrDefault(
                    g => Math.Abs(g.X - p.X) < 1e-9 && Math.Abs(g.Y - p.Y) < 1e-9);
                if (node == null) continue;

                compared++;
                double atPile = result.PileSettlements_mm[p.PileNo];

                // どちらも mm (積分は m で返り、両経路が 1000 を掛けている)
                Assert.AreEqual(atPile, node.Settlement, Math.Max(1e-9, Math.Abs(atPile) * 1e-12),
                    $"杭 {p.PileNo} ({p.X}, {p.Y}) の沈下 {atPile:F9} mm が、"
                    + $"同じ点のグリッド値 {node.Settlement:F9} mm と違います。"
                    + "杭位置とグリッドは別の経路で計算されています");
            }

            TestSource.AssertScanned(compared, 1, "グリッド点に載っている杭");
        }

        // ---- 道具 ----

        /// <summary>
        /// 検査用の場面。2 層地盤 + 任意矩形の荷重 1 枚 + 杭 4 本。
        ///
        /// 杭番号は本体側 (<c>UpdatePileLayoutNo</c>) と同じく 1 から振る。
        /// 杭ごとの沈下はこの番号を鍵にした辞書で返るので、振らないと 1 件に潰れる。
        /// </summary>
        private static (PileGroupSettlement, ObservableCollection<PileLayoutDataItem>) Scene()
        {
            var gs = new PileGroupSettlement
            {
                LoadingType = "任意矩形",
                SoilLayersTopAltitude = 0.0,
                LoadingPlaneAltitude = 0.0,
            };

            gs.SettlementSoilLayers =
            [
                new() { Thickness = 5.0,  Ek = 10_000.0, PoissonsRatio = 0.3, BottomAltitude = -5.0 },
                new() { Thickness = 10.0, Ek = 30_000.0, PoissonsRatio = 0.3, BottomAltitude = -15.0 },
            ];

            gs.RectLoads =
            [
                new() { X1 = -3, X2 = 3, Y1 = -3, Y2 = 3, QA = 200 },
            ];

            var piles = new ObservableCollection<PileLayoutDataItem>
            {
                new() { X = 0, Y = 0 },
                new() { X = 3, Y = 0 },
                new() { X = 0, Y = 3 },
                new() { X = -3, Y = -3 },
            };
            for (int i = 0; i < piles.Count; i++)
            {
                piles[i].No = i + 1;
                piles[i].PileNo = i + 1;
            }

            return (gs, piles);
        }

        private static SettlementAnalysisService.SettlementAnalysisResult Run(
            PileGroupSettlement gs,
            ObservableCollection<PileLayoutDataItem> piles,
            double xMin = -6, double xMax = 6, double yMin = -6, double yMax = 6)
        {
            var service = new SettlementAnalysisService();
            var result = service.PerformSettlementAnalysis(
                gs, piles,
                new ObservableCollection<SoilPile>(),
                new ObservableCollection<GridDataItem>(),
                new ObservableCollection<GridDataItem>(),
                xMin, xMax, yMin, yMax, 0, 0, 3, 3);

            Assert.IsTrue(result.Success, $"解析が成功しませんでした: {result.ErrorMessage}");
            Assert.IsNotNull(result.PileSettlements_mm, "杭ごとの沈下が返っていません");
            Assert.IsTrue(result.PileSettlements_mm.Count > 0, "杭ごとの沈下が空です");
            return result;
        }

        /// <summary>すべての土層を、同じ性質の 2 枚に割る。割った枚数を返す。</summary>
        private static int SplitEveryLayerInHalf(PileGroupSettlement gs)
        {
            var result = new List<SettlementSoilLayer>();
            int divided = 0;

            foreach (var layer in gs.SettlementSoilLayers.ToList())
            {
                double thickness = layer.Thickness;
                if (thickness <= 0.02) { result.Add(layer); continue; }

                var upper = new SettlementSoilLayer
                {
                    Thickness = thickness / 2.0,
                    Ek = layer.Ek,
                    PoissonsRatio = layer.PoissonsRatio,
                    BottomAltitude = layer.BottomAltitude + thickness / 2.0,
                };
                layer.Thickness = thickness / 2.0;

                result.Add(upper);
                result.Add(layer);
                divided++;
            }

            gs.SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>(result);
            return divided;
        }

        private static void AssertSamePileSettlements(
            SettlementAnalysisService.SettlementAnalysisResult a,
            SettlementAnalysisService.SettlementAnalysisResult b,
            double tolerance,
            string what)
        {
            Assert.AreEqual(a.PileSettlements_mm.Count, b.PileSettlements_mm.Count,
                what + "（件数が違います）");

            var offenders = new List<string>();
            foreach (var kv in a.PileSettlements_mm)
            {
                if (!b.PileSettlements_mm.TryGetValue(kv.Key, out double other))
                {
                    offenders.Add($"杭 {kv.Key} が片方にありません");
                    continue;
                }
                if (Math.Abs(other - kv.Value) > Math.Max(tolerance, Math.Abs(kv.Value) * tolerance))
                    offenders.Add($"杭 {kv.Key}: {kv.Value:F9} → {other:F9} mm");
            }

            AssertNone(offenders, what);
        }

        private static void AssertNone(List<string> offenders, string what)
        {
            Assert.AreEqual(0, offenders.Count,
                what + ":" + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", offenders.Take(20)));
        }
    }
}
