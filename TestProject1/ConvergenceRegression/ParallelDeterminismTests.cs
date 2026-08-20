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

        /// <summary>
        /// 同一入力・同一オプションで 2 回続けて解析し、結果がビット単位で一致することを検証する。
        ///
        /// 2026-08-21 の回帰: AnaModel.MapOnKmat が beam の要素剛性を ConcurrentBag に集めた
        /// thread-local COO リストから加算しており、beam のスレッド割り当てと bag の列挙順が
        /// 実行ごとに変わるため、重複 (row,col) の浮動小数加算順が変わっていた。
        /// K の ULP レベルの揺れが非線形 NR の line search / bisection の分岐を変え、
        /// Example10 / L2-1.C1.Liq では反復数 175 ⇄ 353、代表変位が 3% 振れていた。
        ///
        /// 反復数だけでなく物理量まで完全一致 (AreEqual on double) を要求する。
        /// 許容差を置くと、まさにこの種の「わずかに揺れる」欠陥を見逃すため。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9", 4, 8)]
        [DataRow("ExampleK8", "PileExampleK8", 4, 8)]
        public void RepeatedRunsAreBitIdentical(
            string groundName, string pileName, int level1Steps, int level2Steps)
        {
            ConvergenceSnapshot Run()
            {
                var opts = new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = level1Steps,
                    Level2Steps = level2Steps,
                    LiquefactionMode = PileDesign.ViewModels.HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                    ForceNonLinear = true,
                };
                return HeadlessHorizontalRunner.RunExample(groundName, pileName, opts);
            }

            ConvergenceSnapshot first, second;
            try
            {
                first = Run();
                second = Run();
            }
            catch (System.InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive($"例題ファイルなし: {ex.Message}");
                return;
            }

            Assert.AreEqual(first.Cases.Count, second.Cases.Count,
                $"[{groundName}] ケース数が 2 回の実行で異なる");

            var firstByKey = first.Cases.ToDictionary(c => c.CaseKey);
            foreach (var act in second.Cases)
            {
                Assert.IsTrue(firstByKey.TryGetValue(act.CaseKey, out var exp),
                    $"[{groundName}] {act.CaseKey} が 1 回目に存在しない");

                Assert.AreEqual(exp.Converged, act.Converged,
                    $"[{groundName}] {act.CaseKey}: 収束フラグが再実行で変わった");
                Assert.AreEqual(exp.TotalIterations, act.TotalIterations,
                    $"[{groundName}] {act.CaseKey}: 反復数が再実行で変わった " +
                    $"{exp.TotalIterations} → {act.TotalIterations} (解析が非決定的)");
                Assert.AreEqual(exp.TotalSteps, act.TotalSteps,
                    $"[{groundName}] {act.CaseKey}: ステップ数が再実行で変わった");

                AssertBitIdentical(groundName, act.CaseKey, "AP.Ux", exp.ApUx, act.ApUx);
                AssertBitIdentical(groundName, act.CaseKey, "AP.Uy", exp.ApUy, act.ApUy);
                AssertBitIdentical(groundName, act.CaseKey, "AP.Uz", exp.ApUz, act.ApUz);
                AssertBitIdentical(groundName, act.CaseKey, "AP.Rx", exp.ApRx, act.ApRx);
                AssertBitIdentical(groundName, act.CaseKey, "AP.Ry", exp.ApRy, act.ApRy);
                AssertBitIdentical(groundName, act.CaseKey, "AP.Rz", exp.ApRz, act.ApRz);
                AssertBitIdentical(groundName, act.CaseKey, "MaxAbsHorizDisp",
                    exp.MaxAbsHorizDisp, act.MaxAbsHorizDisp);
                AssertBitIdentical(groundName, act.CaseKey, "MaxAbsHorizSpringReaction",
                    exp.MaxAbsHorizSpringReaction, act.MaxAbsHorizSpringReaction);
            }
        }

        private static void AssertBitIdentical(
            string exampleName, string caseKey, string label, double expected, double actual)
        {
            // -0.0 と +0.0、NaN 同士も区別せず「同じビット列か」で見る
            if (System.BitConverter.DoubleToInt64Bits(expected) == System.BitConverter.DoubleToInt64Bits(actual))
                return;

            Assert.Fail($"[{exampleName}] {caseKey}: {label} が再実行でビット一致しない " +
                        $"{expected:R} → {actual:R} (組立順など実行ごとに変わる要素の混入を疑う)");
        }
    }
}
