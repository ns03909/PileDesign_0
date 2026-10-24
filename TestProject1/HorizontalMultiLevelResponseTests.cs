using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 杭頭２水準・地盤２種類の水平実解析を、最大値だけでなく節点・要素・ばねごとに検査する。
    /// 比例・反転は線形域だけ、座標原点の変更は非線形・液状化・地盤変位を含めて成り立つ。
    /// </summary>
    [TestClass]
    public class HorizontalMultiLevelResponseTests
    {
        private static void SetUpMixedGrounds(InputModel input, bool stressFromPileHead)
        {
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();
            var second = input.GroundsInput[0].DeepCopy();
            second.GroundTopAltitude -= 0.6;
            second.SyncAltitudesFromGLDepths();
            input.GroundsInput.Add(second);
            foreach (var ground in input.GroundsInput)
                ground.IsStressFromPileFoundationBottom = stressFromPileHead;
            for (int i = 0; i < input.PileLayoutItems.Count; i++)
            {
                var pile = input.PileLayoutItems[i];
                if (i % 2 == 0) pile.Z -= 1.5;
                pile.GroundNo = 1 + i / 2 % 2;
            }
            input.GenerateSoilPiles();
            Assert.AreEqual(4, input.ElementDivision.SoilPiles.Count,
                "前提: 同じ杭体で杭頭２水準・地盤２種類の４組が必要です");
        }

        private static string CaseKey(ICaseStepResult result)
            => $"L{result.LoadCase.Level}-{result.LoadCase.No}.C{result.LoadCombination.No}."
                + (result.IsLiquefaction ? "Liq" : "NoLq");

        private static T Final<T>(IEnumerable<T> results, string key) where T : ICaseStepResult
            => results.Where(r => CaseKey(r) == key).OrderBy(r => r.Step).Last();

        private static HorizontalCalculationViewModel Run(bool nonlinear, Action<InputModel>? change = null)
        {
            var options = nonlinear
                ? new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4,
                    Level2Steps = 8,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Both,
                }
                : AnalysisOutputInvariantTests.LinearOptions();
            var prepare = options.Customize;
            options.Customize = input =>
            {
                prepare?.Invoke(input);
                SetUpMixedGrounds(input, nonlinear);
                foreach (var load in input.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                    load.LoadAngle = 30;
                change?.Invoke(input);
            };
            var vm = HeadlessHorizontalRunner.RunExampleForHcvm("Example10", "PileExample10", options);
            Assert.IsNotNull(vm.CurrentModel, "水平解析モデルが作られていません");
            var snapshot = HeadlessHorizontalRunner.AggregateSnapshot(vm, "Example10", options);
            Assert.AreEqual(nonlinear ? 4 : 2, snapshot.Cases.Count,
                "レベル１・２と指定した液状化条件が実行されていません");
            foreach (var c in snapshot.Cases)
            {
                Assert.IsTrue(c.Converged, $"{c.CaseKey}: 未収束です");
                Assert.AreEqual(0, c.RelaxedSteps, $"{c.CaseKey}: 緩和した基準で受理されています");
                Assert.IsTrue(double.IsFinite(c.FinalResidual), $"{c.CaseKey}: 残差が有限でありません");
            }
            AssertSoilDisplacements(vm, snapshot.Cases.Select(c => c.CaseKey), nonlinear);
            AssertHorizontalBalance(vm, snapshot.Cases.Select(c => c.CaseKey));
            AssertMomentBalance(vm, snapshot.Cases.Select(c => c.CaseKey));
            return vm;
        }

        private static void AssertSoilDisplacements(HorizontalCalculationViewModel vm,
            IEnumerable<string> cases, bool requireDisplacement)
        {
            double largest = 0;
            foreach (var key in cases)
                foreach (var pile in vm.InputModel.PileLayoutItems)
                {
                    var soilPile = pile.SoilPileAt(vm.InputModel)!;
                    var nodes = vm.CurrentModel.GetSoilNodes(pile);
                    Assert.AreEqual(soilPile.ZDataItems.Count, nodes.Count, $"杭{pile.No}: 地盤節点が欠けています");
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var r = Final(nodes[i].NodeResults, key);
                        var z = soilPile.ZDataItems[i];
                        double displacement = r.LoadCase.Level == 1
                            ? (r.IsLiquefaction ? z.GroundDisp1L : z.GroundDisp1)
                            : (r.IsLiquefaction ? z.GroundDisp2L : z.GroundDisp2);
                        displacement *= r.LoadCombination.Alpha1 / 1000;
                        largest = Math.Max(largest, Math.Abs(displacement));
                        double radians = r.LoadCase.LoadAngle * Math.PI / 180;
                        Assert.AreEqual(displacement * Math.Cos(radians), r.CumulativeDisp.Ux, 1e-9,
                            $"{key}/杭{pile.No}/地盤節点{i}: 自分の地盤・杭頭に対応する地盤変位Xではありません");
                        Assert.AreEqual(displacement * Math.Sin(radians), r.CumulativeDisp.Uy, 1e-9,
                            $"{key}/杭{pile.No}/地盤節点{i}: 自分の地盤・杭頭に対応する地盤変位Yではありません");
                    }
                }
            if (requireDisplacement)
                Assert.IsTrue(largest > 1e-9, "前提: 地盤変位を加えた実解析になっていません");
        }

        private static void AssertHorizontalBalance(HorizontalCalculationViewModel vm, IEnumerable<string> cases)
        {
            foreach (var key in cases)
            {
                var applied = Final(vm.CurrentModel.Nodes[0].NodeResults, key);
                var load = applied.LoadCase;
                var combination = applied.LoadCombination;
                double force = combination.Beta1 * load.UpperMassForce
                    + combination.Beta2 * load.FoundationMassForce;
                double radians = load.LoadAngle * Math.PI / 180;
                var expected = new[] { force * Math.Cos(radians), force * Math.Sin(radians) };
                var springResults = vm.CurrentModel.HorizontalSoilSprings
                    .Select(s => Final(s.HorizontalSpringResults, key)).ToList();
                for (int axis = 0; axis < 2; axis++)
                {
                    double reaction = springResults.Sum(r => r.CumulativeForce.GetByIndex(axis));
                    Assert.AreEqual(expected[axis], applied.CumulativedLoad.GetByIndex(axis),
                        Math.Abs(force) * 1e-8, $"{key}: 入力荷重が最終ステップまで載っていません (軸{axis})");
                    Assert.AreEqual(expected[axis], reaction, Math.Abs(force) * 1e-3,
                        $"{key}: 水平外力と地盤ばね反力の合計が釣り合いません (軸{axis})");
                }

            }
        }

        private static void AssertMomentBalance(HorizontalCalculationViewModel vm, IEnumerable<string> cases)
        {
            var model = vm.CurrentModel;
            var origin = model.Nodes[0];
            Assert.IsFalse(origin.Boundary.Rx || origin.Boundary.Ry || origin.Boundary.Rz,
                "前提: 作用点の回転拘束があるモデルは拘束反力も集計する必要があります");
            foreach (var key in cases)
            {
                Vector3D moment = new();
                double scale = 0;
                void Add(Node node, double fx, double fy, double fz, double mx, double my, double mz)
                {
                    var contribution = Vector3D.CrossProduct(node.Coord - origin.Coord, new Vector3D(fx, fy, fz))
                        + new Vector3D(mx, my, mz);
                    moment += contribution;
                    scale += contribution.Length;
                }
                // ばねの杭・根入部側の抵抗と、杭先端の拘束反力を全体座標で集計する。
                foreach (var spring in model.HorizontalSoilSprings)
                {
                    var f = Final(spring.HorizontalSpringResults, key).CumulativeForce;
                    Add(spring.NodeI, f.Fxi, f.Fyi, f.Fzi, f.Mxi, f.Myi, f.Mzi);
                }
                foreach (var pile in vm.InputModel.PileLayoutItems)
                {
                    var beam = model.GetPileBeams(pile).Last();
                    var f = beam.GetCachedCoordTransform().Transpose()
                        * Final(beam.BeamResults, key).CumulativeForce.GetVector();
                    var tip = beam.NodeJ;
                    Assert.IsTrue(tip.Boundary.Uz && !tip.HasMasterSlave,
                        $"杭{pile.No}: 前提の鉛直固定・独立した杭先端ではありません");
                    Add(tip, 0, 0, -f[8], 0, 0, 0);
                }
                Assert.IsTrue(scale > 1, $"{key}: 釣合いを検査できる反力モーメントがありません");
                Assert.IsTrue(moment.Length <= scale * 1e-3,
                    $"{key}: 作用点回りのモーメントが釣り合いません。"
                    + $"残差 ({moment.X:E6}, {moment.Y:E6}, {moment.Z:E6}) kNm / 寄与絶対値合計 {scale:E6} kNm");
            }
        }

        private static Dictionary<string, (string Family, double[] Values)> Responses(HorizontalCalculationViewModel vm)
        {
            var values = new Dictionary<string, (string Family, double[] Values)>();
            var expectedCases = vm.CurrentModel.Nodes[0].NodeResults
                .Where(r => r.LoadCase.Level is 1 or 2).Select(r => CaseKey(r)).Distinct().ToArray();
            void Add<T>(string name, string family, IEnumerable<T> results, Func<T, double[]> extract)
                where T : ICaseStepResult
            {
                var groups = results.Where(r => r.LoadCase.Level is 1 or 2).GroupBy(r => CaseKey(r)).ToList();
                CollectionAssert.AreEquivalent(expectedCases, groups.Select(g => g.Key).ToArray(),
                    $"{name}: 荷重条件ごとの結果が欠けています");
                foreach (var group in groups)
                {
                    var r = group.OrderBy(r => r.Step).Last();
                    var response = extract(r);
                    Assert.IsTrue(response.All(double.IsFinite), $"{name}/{group.Key}: 非有限の応答があります");
                    values.Add($"{family}/{name}/{group.Key}", (family, response));
                }
            }

            foreach (var pile in vm.InputModel.PileLayoutItems)
            {
                var nodes = vm.CurrentModel.GetPileNodes(pile);
                var beams = vm.CurrentModel.GetPileBeams(pile);
                Assert.IsTrue(nodes.Count >= 2 && beams.Count >= 1, $"杭{pile.No}: 節点・要素がありません");
                foreach (var node in nodes)
                    Add(node.Name, "変位", node.NodeResults,
                        r => [r.CumulativeDisp.Ux, r.CumulativeDisp.Uy]);
                foreach (var beam in beams)
                {
                    string name = $"{beam.NodeI.Name}/{beam.NodeJ.Name}";
                    Add(name, "せん断力", beam.BeamResults,
                        r => [r.CumulativeForce.Fyi, r.CumulativeForce.Fzi, r.CumulativeForce.Fyj, r.CumulativeForce.Fzj]);
                    Add(name, "曲げモーメント", beam.BeamResults,
                        r => [r.CumulativeForce.Myi, r.CumulativeForce.Mzi, r.CumulativeForce.Myj, r.CumulativeForce.Mzj]);
                }
            }
            foreach (var spring in vm.CurrentModel.HorizontalSoilSprings)
                Add($"{spring.Name}/{spring.NodeI.Name}", "地盤反力", spring.HorizontalSpringResults,
                    r => [r.CumulativeForce.Fxi, r.CumulativeForce.Fyi]);
            TestSource.AssertScanned(values.Count, 100, "比較する水平解析の応答");
            foreach (var family in new[] { "変位", "せん断力", "曲げモーメント", "地盤反力" })
                Assert.IsTrue(values.Values.Where(v => v.Family == family)
                    .SelectMany(v => v.Values).Any(v => Math.Abs(v) > 1e-6),
                    $"{family}: 全応答がゼロでは比較できません");
            return values;
        }

        private static void Compare(HorizontalCalculationViewModel before, HorizontalCalculationViewModel after,
            double factor, double relativeTolerance)
        {
            var a = Responses(before);
            var b = Responses(after);
            CollectionAssert.AreEquivalent(a.Keys.ToArray(), b.Keys.ToArray(),
                "変換の前後で、結果のある節点・要素・ばね・荷重条件が変わりました");
            var scales = a.Values.GroupBy(v => v.Family)
                .ToDictionary(g => g.Key, g => g.SelectMany(v => v.Values).Max(Math.Abs));
            var failures = new List<string>();
            foreach (var (key, response) in a)
                for (int i = 0; i < response.Values.Length; i++)
                {
                    double expected = response.Values[i] * factor;
                    double actual = b[key].Values[i];
                    // ゼロ近傍には同じ物理量の最大応答の 1/100000 を絶対許容差として使う。
                    double tolerance = Math.Abs(factor) * scales[response.Family] * 1e-5
                        + Math.Abs(expected) * relativeTolerance;
                    if (Math.Abs(expected - actual) > tolerance)
                        failures.Add($"{key}[{i}]: 期待 {expected:E6} / 実際 {actual:E6} / 許容差 {tolerance:E3}");
                }
            Assert.AreEqual(0, failures.Count,
                $"変換に従わない応答 {failures.Count} 件:\n" + string.Join("\n", failures.Take(15)));
        }

        [DataTestMethod]
        [DataRow(-1.0)]
        [DataRow(2.0)]
        public void LinearMixedGrounds_ResponsesFollowTheLoad(double factor)
        {
            var before = Run(false);
            var after = Run(false, input =>
            {
                foreach (var load in input.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                {
                    load.UpperMassForce *= factor;
                    load.FoundationMassForce *= factor;
                }
            });
            Compare(before, after, factor, 1e-6);
        }

        [TestMethod]
        public void NonlinearMixedGrounds_TranslatingVerticallyPreservesEveryResponse()
        {
            var before = Run(true);
            var after = Run(true, input => AnalysisTranslationInvarianceTests.TranslateVertically(input, -0.75));
            Compare(before, after, 1, 1e-3);
        }

        [TestMethod]
        public void NonlinearMixedGrounds_ReorderingGroundsPreservesEveryResponse()
        {
            var before = Run(true);
            var after = Run(true, input =>
            {
                var first = input.GroundsInput[0];
                input.GroundsInput[0] = input.GroundsInput[1];
                input.GroundsInput[1] = first;
                foreach (var pile in input.PileLayoutItems)
                    pile.GroundNo = 3 - pile.GroundNo;
                input.GenerateSoilPiles();
            });
            Compare(before, after, 1, 1e-3);
        }
    }
}
