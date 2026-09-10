using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 基礎梁を考慮した<b>反復</b>沈下解析を、成り立っているかで検査する。
    ///
    /// <para>この解析は「地盤側の沈下 (スタインブレナー)」と「梁側のたわみ」を
    /// 交互に解き、両者が一致するまで杭ばねの剛性を更新する。
    /// 単発の解析 (<see cref="GroupSettlementInvariantTests"/>) と違って
    /// <b>収束したかどうか</b>という状態を持つので、見る観点が増える。</para>
    ///
    /// <list type="bullet">
    /// <item>収束したと言うなら、<b>報告された 2 つの沈下から計算した残差</b>が許容値以下であること
    ///   （残差だけ持ち帰って沈下の中身が古い、という食い違いを塞ぐ）</item>
    /// <item>杭反力の総和が柱荷重の総和に等しいこと（力の釣り合い）</item>
    /// <item>ばね剛性が与えた範囲に収まり、正であること</item>
    /// <item>同じ入力で二度走らせて一致すること</item>
    /// <item>柱荷重を 2 倍にしたら反力の総和も 2 倍になること</item>
    /// <item>地盤を硬くしたら沈下は増えないこと</item>
    /// </list>
    ///
    /// <para>この経路を叩くテストは無かった。反復は「収束しなかったときに黙って
    /// 途中の値を返す」形になりやすいので、状態と中身の対応を見張る。</para>
    /// </summary>
    [TestClass]
    public class IterativeBeamSettlementInvariantTests
    {
        private const double Tol = 1e-3;

        [TestMethod]
        public void ItConverges_AndTheReportedSettlementsAgreeWithTheResidual()
        {
            var (model, ppi) = Scene();
            if (model == null) return;

            var result = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            Assert.IsTrue(result.Converged,
                $"収束しませんでした (反復 {result.IterationCount} 回、残差 {result.FinalResidual:E3})。"
                + "この例題で収束しなくなったら、反復の作りが変わっています");

            // 収束を宣言したなら、持ち帰った沈下からも同じ結論が出ること。
            // 残差だけ更新して沈下の中身が前の反復のまま、という食い違いを塞ぐ。
            double maxAbs = Math.Max(
                result.SteinbrennerSettlement.Values.DefaultIfEmpty(0).Max(Math.Abs),
                result.BeamSettlement.Values.DefaultIfEmpty(0).Max(Math.Abs));
            Assert.IsTrue(maxAbs > 1e-12, "沈下がすべて 0 です（揺らせていない）");

            double maxDiff = 0;
            int compared = 0;
            foreach (var kv in result.SteinbrennerSettlement)
            {
                if (!result.BeamSettlement.TryGetValue(kv.Key, out double s2)) continue;
                compared++;
                maxDiff = Math.Max(maxDiff, Math.Abs(s2 - kv.Value));
            }

            TestSource.AssertScanned(compared, 4, "見比べた杭");

            double recomputed = maxDiff / maxAbs;
            Assert.IsTrue(recomputed <= Tol,
                $"収束したと報告しているのに、持ち帰った沈下から計算した残差が {recomputed:E3} で"
                + $"許容値 {Tol:E2} を超えています。"
                + "地盤側の沈下と梁側のたわみが噛み合っていません");

            Assert.AreEqual(result.FinalResidual, recomputed, Math.Max(1e-9, recomputed * 1e-6),
                $"報告された残差 {result.FinalResidual:E3} と、持ち帰った沈下から計算した "
                + $"{recomputed:E3} が違います。どちらかが前の反復のものです");
        }

        [TestMethod]
        public void ThePileReactions_BalanceTheColumnLoads()
        {
            var (model, ppi) = Scene();
            if (model == null) return;

            var result = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            double sumReactions = result.PileReactions.Values.Sum();
            double sumColumns = ppi.Values.Sum();

            Assert.IsTrue(Math.Abs(sumColumns) > 1e-9, "柱荷重が 0 です（揺らせていない）");

            Assert.AreEqual(sumColumns, sumReactions, Math.Abs(sumColumns) * 1e-6,
                $"杭反力の総和 {sumReactions:F3} kN が柱荷重の総和 {sumColumns:F3} kN と釣り合いません");
        }

        [TestMethod]
        public void EveryPile_GetsAReactionAStiffnessAndTwoSettlements()
        {
            var (model, ppi) = Scene();
            if (model == null) return;

            var result = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            int piles = model!.PileLayoutItems.Count;
            TestSource.AssertScanned(piles, 4, "杭");

            foreach (var (label, dict) in new (string, Dictionary<int, double>)[]
            {
                ("杭反力", result.PileReactions),
                ("ばね剛性", result.SpringStiffness),
                ("地盤側の沈下", result.SteinbrennerSettlement),
                ("梁側の沈下", result.BeamSettlement),
            })
            {
                Assert.AreEqual(piles, dict.Count,
                    $"{label} が杭 {piles} 本に対して {dict.Count} 件しかありません。"
                    + "杭番号が重なっていると静かに上書きされます");

                foreach (var pile in model.PileLayoutItems)
                    Assert.IsTrue(dict.ContainsKey(pile.PileNo),
                        $"{label} に杭番号 {pile.PileNo} がありません");
            }
        }

        /// <summary>
        /// ばね剛性が与えた範囲に収まり、正であること。
        ///
        /// この剛性は Pi/S1 から作るので、沈下がほぼ 0 のときに暴走する。
        /// だから上下限を渡す作りになっている。範囲外の値が出たら、保護が効いていない。
        /// </summary>
        [TestMethod]
        public void TheSpringStiffness_StaysInsideTheGivenRange()
        {
            var (model, ppi) = Scene();
            if (model == null) return;

            // 範囲を意図的に狭くする。既定の [1e3, 1e10] だと素の Pi/S1 がそのまま
            // 収まってしまい、保護が効いているのか要らなかったのか区別できない
            // (実際に既定値では、クランプを外しても検査が通ってしまった)。
            const double kMin = 1e6, kMax = 1e7;
            var result = IterativeBeamSettlementService.Run(
                model!, ppi, "検査", tol: Tol, kMin: kMin, kMax: kMax);

            var offenders = new List<string>();
            foreach (var kv in result.SpringStiffness)
            {
                if (!double.IsFinite(kv.Value))
                    offenders.Add($"杭 {kv.Key}: {kv.Value}（有限でない）");
                else if (kv.Value < kMin * (1 - 1e-9) || kv.Value > kMax * (1 + 1e-9))
                    offenders.Add($"杭 {kv.Key}: {kv.Value:E3} kN/m が [{kMin:E0}, {kMax:E0}] の外");
            }

            TestSource.AssertScanned(result.SpringStiffness.Count, 4, "ばね剛性");

            Assert.AreEqual(0, offenders.Count,
                "ばね剛性が与えた範囲に収まっていません。"
                + "沈下がほぼ 0 のときの保護が効いていない可能性があります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(10)));
        }

        [TestMethod]
        public void RunningItTwice_GivesTheSameAnswer()
        {
            var (model, ppi) = Scene();
            if (model == null) return;

            var first = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            var (model2, ppi2) = Scene();
            var second = IterativeBeamSettlementService.Run(model2!, ppi2, "検査", tol: Tol);

            Assert.AreEqual(first.Converged, second.Converged, "収束の有無が揺れました");
            Assert.AreEqual(first.IterationCount, second.IterationCount,
                $"反復回数が揺れました ({first.IterationCount} → {second.IterationCount})");

            foreach (var kv in first.PileReactions)
            {
                double other = second.PileReactions[kv.Key];
                Assert.AreEqual(kv.Value, other, 0.0,
                    $"杭 {kv.Key} の反力が揺れました {kv.Value} → {other}");
            }
        }

        /// <summary>
        /// 柱荷重を 2 倍にしたら、杭反力の総和も 2 倍になること（釣り合い）。
        ///
        /// ばねが非線形なので<b>杭ごとの配分は 2 倍にならない</b>。総和で見る。
        /// 沈下は増える側でなければならない。
        /// </summary>
        [TestMethod]
        public void DoublingTheColumnLoads_DoublesTheTotalReaction()
        {
            var (model, ppi) = Scene();
            if (model == null) return;
            var single = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            var (model2, ppi2) = Scene();
            foreach (int key in ppi2.Keys.ToList()) ppi2[key] *= 2.0;
            var doubled = IterativeBeamSettlementService.Run(model2!, ppi2, "検査", tol: Tol);

            double a = single.PileReactions.Values.Sum();
            double b = doubled.PileReactions.Values.Sum();

            Assert.AreEqual(a * 2.0, b, Math.Abs(a) * 1e-6,
                $"柱荷重 2 倍で杭反力の総和が {a:F3} → {b:F3} kN。2 倍になっていません");

            double sA = single.SteinbrennerSettlement.Values.Max();
            double sB = doubled.SteinbrennerSettlement.Values.Max();
            Assert.IsTrue(sB > sA,
                $"柱荷重 2 倍で最大沈下が {sA * 1000:F4} → {sB * 1000:F4} mm。増えていません");
        }

        /// <summary>
        /// 地盤を硬くしたら沈下は増えないこと。かつ、硬さが効いていること。
        /// </summary>
        [TestMethod]
        public void StifferGround_SettlesLess()
        {
            var (model, ppi) = Scene();
            if (model == null) return;
            var soft = IterativeBeamSettlementService.Run(model!, ppi, "検査", tol: Tol);

            var (model2, ppi2) = Scene();
            int raised = 0;
            foreach (var layer in model2!.PileGroupSettlement.SettlementSoilLayers)
            {
                layer.Ek *= 2.0;
                raised++;
            }
            TestSource.AssertScanned(raised, 2, "硬くした土層");

            var stiff = IterativeBeamSettlementService.Run(model2, ppi2, "検査", tol: Tol);

            double softMax = soft.SteinbrennerSettlement.Values.Max();
            double stiffMax = stiff.SteinbrennerSettlement.Values.Max();

            Assert.IsTrue(stiffMax <= softMax + Math.Abs(softMax) * 1e-9,
                $"Ek 2 倍で最大沈下が {softMax * 1000:F4} → {stiffMax * 1000:F4} mm に増えました");

            Assert.AreNotEqual(softMax, stiffMax,
                "Ek を 2 倍にしても沈下が動きません。"
                + "変形係数が反復沈下解析に渡っていない可能性があります");
        }

        // ---- 道具 ----

        /// <summary>
        /// 検査用の場面。基礎梁を持つ計算例5 に、群杭沈下の条件だけを足す。
        ///
        /// <para>本体の例題読込 (<c>Example5Pile</c>) と同じ順序でなぞる。
        /// 順序を変えると、杭 Z の意味の移行 (v1→v2) や土層-杭セットの生成が
        /// 噛み合わなくなる。</para>
        /// </summary>
        private static (InputModel?, Dictionary<int, double>) Scene()
        {
            var vm = new MainWindowViewModel();
            var model = vm.CurrentInputModel;
            if (model == null) { Assert.Inconclusive("既定の入力がありません"); return (null, []); }

            var groupData = GroupSettlementExampleLoader.LoadFromFile("GroupSettlement5");

            if (!string.IsNullOrEmpty(groupData.GroundExampleName))
            {
                var groundVm = new GroundLayerViewModel(vm);
                var groundData = GroundExampleLoader.LoadFromFile(groupData.GroundExampleName);
                GroundExampleLoader.ApplyToGroundInput(groundVm.GroundInput, groundData);
                model.GroundsInput[0] = groundVm.GroundInput.DeepCopy();
            }

            var pileData = PileExampleLoader.LoadFromFile("PileExample5");
            PileExampleLoader.ApplyToInputModel(model, pileData, vm);

            // 杭配置や杭体は上書きせず、沈下検討用の条件だけを足す
            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(model, groupData);

            model.MigratePileZSemantics_v1_to_v2();
            vm.UpdatePileLayoutNo();
            model.GenerateSoilPiles();

            if (model.FoundationBeamInput?.Beams == null || model.FoundationBeamInput.Beams.Count == 0)
            {
                Assert.Inconclusive("基礎梁が読み込まれていません");
                return (null, []);
            }

            // 柱軸力は全杭に同じ値を置く。配分の妥当性ではなく、
            // 変換への追従 (2 倍にしたら / 硬くしたら) を見るための場面。
            var ppi = model.PileLayoutItems.ToDictionary(p => p.PileNo, _ => 1000.0);
            return (model, ppi);
        }
    }
}
