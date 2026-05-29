using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// ケース並列実行 (MaxCaseDegreeOfParallelism) が結果の決定性を壊さないか検証する。
    ///
    /// 仕様: MDOP を変えても、各 (Level, LoadCaseNo, ComboNo, IsLiquefaction) ケースの:
    ///   - 反復数 (TotalIterations)
    ///   - 収束フラグ (Converged)
    /// が完全一致すること。差があれば共有状態 (case-local snapshot からの漏れ) が疑われる。
    ///
    /// 注意:
    ///   - 残差は丸め誤差の累積順序が変わるため小さな差が出る可能性あり → 反復数と収束フラグのみで判定
    ///   - 1 ケース構成だと並列化されないため Example3_5 など複数 LoadCase ある例題で実施したい
    ///     が、現状 BuildExampleInputModel が各 Level 1 ケースのみのため Example9 で smoke test として実施
    /// </summary>
    [TestClass]
    public class ParallelDeterminismTests
    {
        [DataTestMethod]
        [DataRow("Example9", "PileExample9", 4, 8)]
        [DataRow("ExampleK8", "PileExampleK8", 4, 8)]
        public void IterationCountsAreIdenticalAcrossParallelism(
            string groundName, string pileName, int level1Steps, int level2Steps)
        {
            // 同じ例題を MDOP=1, 2, 4 で順次実行し、結果を比較
            var degrees = new[] { 1, 2, 4 };
            var snapshotsByDop = new Dictionary<int, ConvergenceSnapshot>();

            foreach (int dop in degrees)
            {
                var opts = new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = level1Steps,
                    Level2Steps = level2Steps,
                    LiquefactionMode = PileDesign.ViewModels.HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = dop,
                    ForceNonLinear = true,
                };
                ConvergenceSnapshot snap;
                try
                {
                    snap = HeadlessHorizontalRunner.RunExample(groundName, pileName, opts);
                }
                catch (System.InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
                {
                    Assert.Inconclusive($"例題ファイルなし: {ex.Message}");
                    return;
                }
                snapshotsByDop[dop] = snap;
            }

            // 基準: MDOP=1 (逐次)。これに対して MDOP=2, 4 が一致するか検証
            var baseline = snapshotsByDop[1];
            foreach (int dop in degrees.Where(d => d != 1))
            {
                var actual = snapshotsByDop[dop];
                AssertSameConvergenceBehavior(baseline, actual, groundName, dop);
            }
        }

        private static void AssertSameConvergenceBehavior(
            ConvergenceSnapshot baseline, ConvergenceSnapshot actual, string exampleName, int dop)
        {
            Assert.AreEqual(baseline.Cases.Count, actual.Cases.Count,
                $"[{exampleName} MDOP={dop}] ケース数が異なる: baseline={baseline.Cases.Count}, actual={actual.Cases.Count}");

            // ケース順序は並列実行で前後する可能性があるため caseKey で照合
            var baselineByKey = baseline.Cases.ToDictionary(c => c.CaseKey);
            foreach (var act in actual.Cases)
            {
                Assert.IsTrue(baselineByKey.TryGetValue(act.CaseKey, out var bas),
                    $"[{exampleName} MDOP={dop}] {act.CaseKey} が baseline に存在しない");

                Assert.AreEqual(bas.Converged, act.Converged,
                    $"[{exampleName} MDOP={dop}] {act.CaseKey}: 収束フラグ不一致 baseline={bas.Converged}, MDOP{dop}={act.Converged}");

                Assert.AreEqual(bas.TotalIterations, act.TotalIterations,
                    $"[{exampleName} MDOP={dop}] {act.CaseKey}: 反復数不一致 baseline={bas.TotalIterations}, MDOP{dop}={act.TotalIterations} → case-local 状態漏れ疑い");

                Assert.AreEqual(bas.TotalSteps, act.TotalSteps,
                    $"[{exampleName} MDOP={dop}] {act.CaseKey}: ステップ数不一致 baseline={bas.TotalSteps}, MDOP{dop}={act.TotalSteps}");
            }
        }
    }
}
