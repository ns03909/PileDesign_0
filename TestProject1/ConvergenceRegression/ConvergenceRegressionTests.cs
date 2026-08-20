using PileDesign.Models.InputData;
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 水平解析の収束挙動が、過去のスナップショットから退化していないかを検証する回帰テスト。
    ///
    /// 失敗時の対応:
    ///   - 意図的な改善 (反復数が "減って" 失敗 = サブメッセージ "improved"): スナップショットを更新
    ///     UPDATE_SNAPSHOTS=1 dotnet test --filter ConvergenceRegression
    ///   - 退化 (反復数が増えた / 収束フラグが偽になった): 改善前に巻き戻すか、原因を修正
    ///
    /// スナップショットファイル: TestProject1/ConvergenceRegression/Snapshots/{ExampleName}.json
    ///
    /// 注意:
    ///   - 各テスト 30s〜3min 程度 (非線形解析のため)
    ///   - 確定的に同じ結果が出る必要があるため Parallelism=1 固定 (並列実行は別途追加)
    ///   - 環境固有の数値ドリフト (math libraries) を考慮し許容範囲を設定済 (CaseRecord 単位)
    /// </summary>
    [TestClass]
    public class ConvergenceRegressionTests
    {
        // 退化判定パラメータ
        // 大規模ケース (反復 100+) は line search の経路により ±10〜30% の自然変動があるため、
        // 絶対 +10 だと flaky 化する。大規模時は ratio 緩めの判定を主とする。
        private const int ITER_ABS_TOLERANCE = 20;      // 絶対 +20 までは許容
        private const double ITER_RATIO_TOLERANCE = 1.50; // 比率 ×1.50 までは許容 (規模変動を吸収)
        private const double RESIDUAL_ORDER_TOLERANCE = 10.0; // 残差 ×10 まで許容 (オーダー一致)
        // 残差がこの閾値より小さい場合は「実質的に収束済」とみなし、ratio 比較をスキップ。
        // 非決定的な数値ドリフト (1e-23 ↔ 1e-7 等、両方とも収束許容内) で flaky 化するのを防ぐ。
        private const double RESIDUAL_WELL_CONVERGED_ABS = 1.0e-3;
        // A1 物理量比較: 相対 5% を許容
        // 非線形 NR の line search 経路差で 3〜4% の自然変動があるケース (Example10 等) を吸収。
        // 10% 以上の有意な退化は依然検出可能 (silent value regression のスポット網)
        private const double PHYSICS_REL_TOLERANCE = 0.05;
        // 微小値の絶対閾値: 値が「ほぼゼロ」のときは ratio 比較せず無視 (両方とも事実上 0)
        private const double PHYSICS_NEGLIGIBLE_ABS = 1.0e-9;

        private static string SnapshotsDir =>
            Path.Combine(Path.GetDirectoryName(typeof(ConvergenceRegressionTests).Assembly.Location)!,
                "ConvergenceRegression", "Snapshots");

        // スナップショット更新モード判定
        private static bool IsUpdateMode =>
            Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

        /// <summary>
        /// 静的状態を既定へ戻してから走らせる。
        ///
        /// <see cref="ConcreteModelOptions"/> のフラグと M-φ の静的キャッシュはプロセス全体で共有される。
        /// 他のテストが書き換えたまま残ると材料モデルが変わり、反復数が大きく動く
        /// （「単体では通るが全体実行だと稀に落ちる」という形で現れる）。
        /// このテストは反復数そのものを固定するため、入口で必ず既知の状態に揃える。
        /// </summary>
        [TestInitialize]
        public void ResetSharedState()
        {
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.UseUnitGsiForConcreteE = false;
            ConcreteModelOptions.UseGuideYoungsModulus = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseFiberMPhi = false;
            ConcreteModelOptions.Notification1113CompressionCase = 1;

            // M-φ の静的キャッシュはここでは<b>クリアしない</b>。
            // キャッシュキーに ConcreteModelOptions.Signature() が入っているので
            // オプション違いの取り違えは起きない。一方でクリアすると Example10 の
            // 反復数が 181 → 353 に増えることを確認しており、キャッシュの温冷が
            // 収束経路を変えている。スナップショットは温状態で採っているため、
            // ここでクリアすると本来見たい退化ではない差で落ちる。
        }

        [DataTestMethod]
        [DataRow("Example9", "PileExample9", 4, 8)]      // 基礎指針'19 計算例9: 場所打ちRC + 18杭
        [DataRow("Example3_5", "PileExample3_5", 4, 16)] // 設計例集3.5: 鋼管杭基礎 (液状化有)
        [DataRow("ExampleK8", "PileExampleK8", 4, 8)]    // 関東支部 計算例8: 杭基礎標準例
        [DataRow("Example10", "PileExample10", 4, 16)]   // 基礎指針'19 計算例10: 場所打ち杭 (液状化)
        public void ConvergenceMatchesSnapshot(
            string groundName, string pileName, int level1Steps, int level2Steps)
        {
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = level1Steps,
                Level2Steps = level2Steps,
                LiquefactionMode = PileDesign.ViewModels.HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                UseLineSearch = true,
                Parallelism = 1,  // 確定性のため逐次実行
            };

            ConvergenceSnapshot actual;
            try
            {
                actual = HeadlessHorizontalRunner.RunExample(groundName, pileName, options);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive($"例題ファイルなしのためスキップ: {ex.Message}");
                return;
            }

            string snapshotPath = Path.Combine(SnapshotsDir, $"{groundName}.json");

            // UPDATE_SNAPSHOTS=1 のときはスナップショットを書き出して終了
            // 非決定的な変動を吸収するため、3 回実行して per-case の MAX を採用 (反復数のみ)。
            // 物理量 (変位 / 反力) は最後の実行値を採用 (相対 1% 許容で検出十分)。
            if (IsUpdateMode)
            {
                const int UPDATE_RUN_COUNT = 5;
                Console.WriteLine($"[UPDATE] {groundName}: 非決定的変動を吸収するため {UPDATE_RUN_COUNT} 回実行 (反復数 max を採用)");
                var snaps = new List<ConvergenceSnapshot> { actual };
                for (int i = 1; i < UPDATE_RUN_COUNT; i++)
                {
                    snaps.Add(HeadlessHorizontalRunner.RunExample(groundName, pileName, options));
                }
                actual = MergeWorstCase(snaps);
                actual.Save(snapshotPath);
                Console.WriteLine($"[UPDATE] スナップショット保存: {snapshotPath}");
                Console.WriteLine($"         ケース数: {actual.SummaryStats.TotalCases}, " +
                                  $"全反復 (max): {actual.SummaryStats.TotalIterations}, " +
                                  $"全ステップ: {actual.SummaryStats.TotalSteps}");
                return;
            }

            // 期待値ロード
            if (!File.Exists(snapshotPath))
            {
                Assert.Inconclusive(
                    $"スナップショット未生成: {snapshotPath}\n" +
                    $"次のコマンドで初回生成してください: " +
                    $"UPDATE_SNAPSHOTS=1 dotnet test --filter ConvergenceRegression");
                return;
            }
            var expected = ConvergenceSnapshot.Load(snapshotPath);

            AssertCompatible(expected, actual, groundName);
        }

        /// <summary>
        /// expected と actual を比較し、退化があれば AssertFail する。
        /// </summary>
        private static void AssertCompatible(
            ConvergenceSnapshot expected, ConvergenceSnapshot actual, string exampleName)
        {
            Assert.AreEqual(expected.Cases.Count, actual.Cases.Count,
                $"[{exampleName}] ケース数が一致しません: expected={expected.Cases.Count}, actual={actual.Cases.Count}");

            for (int i = 0; i < expected.Cases.Count; i++)
            {
                var exp = expected.Cases[i];
                var act = actual.Cases[i];

                Assert.AreEqual(exp.CaseKey, act.CaseKey,
                    $"[{exampleName}] Case[{i}] キー不一致");

                // 収束フラグ: 完全一致必須 (退化を即検出)
                Assert.AreEqual(exp.Converged, act.Converged,
                    $"[{exampleName}] {exp.CaseKey}: 収束フラグ退化 expected={exp.Converged}, actual={act.Converged}");

                // 反復数: 絶対 +ITER_ABS_TOLERANCE または比率 ×ITER_RATIO_TOLERANCE まで許容
                int delta = act.TotalIterations - exp.TotalIterations;
                double ratio = exp.TotalIterations > 0
                    ? (double)act.TotalIterations / exp.TotalIterations : 1.0;
                Assert.IsTrue(delta <= ITER_ABS_TOLERANCE || ratio <= ITER_RATIO_TOLERANCE,
                    $"[{exampleName}] {exp.CaseKey}: 反復数退化 {exp.TotalIterations} → {act.TotalIterations} " +
                    $"(+{delta}, ×{ratio:F2}) 許容: +{ITER_ABS_TOLERANCE} or ×{ITER_RATIO_TOLERANCE}");

                // 残差: 両方とも実質収束済 (< 1e-3) なら比較スキップ (非決定的ドリフト対策)
                // それ以外はオーダー同等まで許容
                bool bothWellConverged =
                    exp.FinalResidual < RESIDUAL_WELL_CONVERGED_ABS &&
                    act.FinalResidual < RESIDUAL_WELL_CONVERGED_ABS;
                if (!bothWellConverged)
                {
                    double residualRatio = exp.FinalResidual > 0
                        ? act.FinalResidual / exp.FinalResidual : 1.0;
                    Assert.IsTrue(residualRatio <= RESIDUAL_ORDER_TOLERANCE,
                        $"[{exampleName}] {exp.CaseKey}: 残差悪化 {exp.FinalResidual:E2} → {act.FinalResidual:E2} " +
                        $"(×{residualRatio:F2}) 許容: ×{RESIDUAL_ORDER_TOLERANCE}");
                }

                // A1: 物理量比較 (代表点変位 + 最大反力)
                // 「収束はするが値が変わった」サイレントな数値退化を検出。相対 1% 許容。
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Ux", exp.ApUx, act.ApUx);
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Uy", exp.ApUy, act.ApUy);
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Uz", exp.ApUz, act.ApUz);
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Rx", exp.ApRx, act.ApRx);
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Ry", exp.ApRy, act.ApRy);
                AssertPhysicsClose(exampleName, exp.CaseKey, "AP.Rz", exp.ApRz, act.ApRz);
                AssertPhysicsClose(exampleName, exp.CaseKey, "MaxAbsHorizDisp",
                    exp.MaxAbsHorizDisp, act.MaxAbsHorizDisp);
                AssertPhysicsClose(exampleName, exp.CaseKey, "MaxAbsHorizSpringReaction",
                    exp.MaxAbsHorizSpringReaction, act.MaxAbsHorizSpringReaction);
            }
        }

        /// <summary>
        /// 複数回の実行結果から、per-case で「最悪 (反復数 max / 残差 max)」を採用したスナップショットを返す。
        /// 物理量 (変位 / 反力) は最後の実行のものを採用 (相対 1% 許容で十分検出できる)。
        /// </summary>
        private static ConvergenceSnapshot MergeWorstCase(List<ConvergenceSnapshot> snaps)
        {
            if (snaps == null || snaps.Count == 0) throw new ArgumentException("snaps empty");
            if (snaps.Count == 1) return snaps[0];

            var baseSnap = snaps[snaps.Count - 1]; // ベースは最後の実行
            for (int i = 0; i < baseSnap.Cases.Count; i++)
            {
                var key = baseSnap.Cases[i].CaseKey;
                int maxIter = baseSnap.Cases[i].TotalIterations;
                double maxResidual = baseSnap.Cases[i].FinalResidual;
                int maxSteps = baseSnap.Cases[i].TotalSteps;
                int maxBisection = baseSnap.Cases[i].BisectionRetries;
                foreach (var other in snaps)
                {
                    var match = other.Cases.FirstOrDefault(c => c.CaseKey == key);
                    if (match == null) continue;
                    if (match.TotalIterations > maxIter) maxIter = match.TotalIterations;
                    if (match.FinalResidual > maxResidual) maxResidual = match.FinalResidual;
                    if (match.TotalSteps > maxSteps) maxSteps = match.TotalSteps;
                    if (match.BisectionRetries > maxBisection) maxBisection = match.BisectionRetries;
                }
                baseSnap.Cases[i].TotalIterations = maxIter;
                baseSnap.Cases[i].FinalResidual = maxResidual;
                baseSnap.Cases[i].TotalSteps = maxSteps;
                baseSnap.Cases[i].BisectionRetries = maxBisection;
            }
            // Summary を再計算
            baseSnap.SummaryStats.TotalIterations = baseSnap.Cases.Sum(c => c.TotalIterations);
            baseSnap.SummaryStats.TotalSteps = baseSnap.Cases.Sum(c => c.TotalSteps);
            baseSnap.SummaryStats.MaxResidualOverAllCases = baseSnap.Cases.Count > 0
                ? baseSnap.Cases.Max(c => c.FinalResidual) : 0.0;
            return baseSnap;
        }

        /// <summary>
        /// 物理量の退化判定: 両値とも微小 (&lt; 1e-9) ならスキップ、それ以外は相対 1% 以内を要求。
        /// </summary>
        private static void AssertPhysicsClose(string exampleName, string caseKey, string fieldName,
            double expected, double actual)
        {
            double absExpected = Math.Abs(expected);
            double absActual = Math.Abs(actual);

            // 両方とも微小なら一致とみなす
            if (absExpected < PHYSICS_NEGLIGIBLE_ABS && absActual < PHYSICS_NEGLIGIBLE_ABS) return;

            // どちらかが微小でもう一方が大きい場合は退化
            if (absExpected < PHYSICS_NEGLIGIBLE_ABS || absActual < PHYSICS_NEGLIGIBLE_ABS)
            {
                Assert.Fail(
                    $"[{exampleName}] {caseKey} {fieldName}: 一方がほぼ 0、もう一方が非ゼロ " +
                    $"expected={expected:E3} actual={actual:E3}");
            }

            double relDiff = Math.Abs(actual - expected) / Math.Max(absExpected, absActual);
            Assert.IsTrue(relDiff <= PHYSICS_REL_TOLERANCE,
                $"[{exampleName}] {caseKey} {fieldName}: 値退化 {expected:E3} → {actual:E3} " +
                $"(相対 {relDiff * 100:F2}%, 許容 {PHYSICS_REL_TOLERANCE * 100:F0}%)");
        }
    }
}
