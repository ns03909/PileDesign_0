using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using TestProject1.ConvergenceRegression;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 本番の HorizontalCalculationViewModel.OnExecuteAnalysisCore を、
    /// UI 確認 dialog を skip した状態で走らせるテスト用ヘルパー。
    ///
    /// 設計判断:
    ///   - 本番と完全に同じ NR + bisection + line search + state 遷移経路を通すため、
    ///     HeadlessAnalysisRunner (Cli) は使わず HCVM を直接呼ぶ
    ///   - HCVM.BypassUiPromptsForTesting=true で UI 系の MessageService / Preflight を抑止
    ///   - Application.Current は null のまま (HCVM 内の Dispatcher.BeginInvoke は null-safe)
    ///   - 解析後 HCVM.StepSummariesSnapshot() で per-case 反復数を取得 → スナップショット組立
    /// </summary>
    public static class HeadlessHorizontalRunner
    {
        public sealed class RunOptions
        {
            public int? Level1Steps { get; set; }
            public int? Level2Steps { get; set; }
            public HorizontalCalculationViewModel.LiquefactionOptionType LiquefactionMode { get; set; }
                = HorizontalCalculationViewModel.LiquefactionOptionType.Yes;
            public bool UseLineSearch { get; set; } = true;
            public int Parallelism { get; set; } = 1;
            /// <summary>true: 全 LoadCase の IsPileNonLinear/IsSoilNonLinear を強制 ON
            /// (M-φ / bisection / line search / state 遷移などの非線形収束経路をテスト対象に含める)</summary>
            public bool ForceNonLinear { get; set; } = true;
        }

        /// <summary>
        /// 例題 (ground + pile) をロードして水平解析を実行し、スナップショットを返す。
        /// </summary>
        public static ConvergenceSnapshot RunExample(
            string groundExampleName, string pileExampleName, RunOptions options)
        {
            var (_, hcvm) = Run(groundExampleName, pileExampleName, options);

            // ステップサマリ + 物理量 → スナップショット組立
            return AggregateSnapshot(hcvm, groundExampleName, options);
        }

        /// <summary>
        /// 例題をロードして水平解析を実行し、解析後の <see cref="MainWindowViewModel"/> を返す。
        ///
        /// 検定テキストのように「解析後の VM 全体」を入力とする検査で使う。
        /// 実機と同じく解析結果を main 側へ移し、解析済みフラグも立てる。
        /// </summary>
        public static MainWindowViewModel RunExampleForViewModel(
            string groundExampleName, string pileExampleName, RunOptions options)
        {
            var (mainVm, hcvm) = Run(groundExampleName, pileExampleName, options);

            mainVm.CurrentModel = hcvm.CurrentModel;
            mainVm.IsHorizontalAnalysisDone = hcvm.CurrentModel != null;
            return mainVm;
        }

        /// <summary>例題ロード → VM 構築 → 解析実行までの共通部分。</summary>
        private static (MainWindowViewModel MainVm, HorizontalCalculationViewModel Hcvm) Run(
            string groundExampleName, string pileExampleName, RunOptions options)
        {
            // 1. InputModel 構築 (IntegrationTests と同じビルダーを再利用)
            var (inputModel, error) = TestProject1.IntegrationTests.BuildExampleInputModel(
                groundExampleName, pileExampleName);
            if (inputModel == null)
                throw new InvalidOperationException($"例題ロード失敗 {groundExampleName}+{pileExampleName}: {error}");

            // 2. 非線形強制 ON: 全 LoadCase で IsPileNonLinear/IsSoilNonLinear を true に
            //    これにより M-φ / bisection / line search / state 遷移などの非線形収束パスが走り、
            //    v22-v29 系の収束改善退化を確実に捕捉できるようになる。
            if (options.ForceNonLinear && inputModel.LoadCasesInput != null)
            {
                foreach (var lc in inputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                {
                    lc.IsPileNonLinear = true;
                    lc.IsSoilNonLinear = true;
                }
            }

            // 3. MainWindowViewModel をテストモードで構築 (CurrentInputModel をセット)
            var mainVm = new MainWindowViewModel();
            mainVm.CurrentInputModel = inputModel;

            // 3. HorizontalCalculationViewModel を構築
            var hcvm = new HorizontalCalculationViewModel(mainVm);
            hcvm.BypassUiPromptsForTesting = true;

            // オプション適用
            if (options.Level1Steps.HasValue)
                hcvm.Level1CalculationStepsCount = options.Level1Steps.Value;
            if (options.Level2Steps.HasValue)
                hcvm.Level2CalculationStepsCount = options.Level2Steps.Value;
            hcvm.LiquefactionOption = options.LiquefactionMode;
            hcvm.UseLineSearch = options.UseLineSearch;
            hcvm.MaxCaseDegreeOfParallelism = options.Parallelism;

            // 4. 解析実行 (ExecuteAnalysisCommand は RelayCommand — Execute(null) で同期実行開始 → 内部で Task.Run)
            //    本来は await 待ち合わせ不能だが、IsAnalysisRunning フラグの off を待つことで完了検知
            hcvm.ExecuteAnalysisCommand.Execute(null);

            // IsAnalysisRunning が false になるまで待機 (タイムアウト 10 分)
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (hcvm.IsAnalysisRunning && DateTime.UtcNow < deadline)
            {
                Task.Delay(100).Wait();
            }
            if (hcvm.IsAnalysisRunning)
                throw new TimeoutException($"水平解析が 10 分以内に完了しませんでした: {groundExampleName}+{pileExampleName}");

            return (mainVm, hcvm);
        }

        /// <summary>
        /// 指定 case に該当する NodeResult を Node から取得 (見つからなければ null)。
        /// </summary>
        private static NodeResult? FindNodeResult(Node node, int level, int loadCaseNo, int comboNo, bool isLiq)
        {
            if (node?.NodeResults == null) return null;
            return node.NodeResults.LastOrDefault(r =>
                r.LoadCase != null && r.LoadCase.Level == level && r.LoadCase.No == loadCaseNo &&
                r.LoadCombination != null && r.LoadCombination.No == comboNo &&
                r.IsLiquefaction == isLiq);
        }

        /// <summary>
        /// 指定 case に該当する HorizontalSpringResult を取得 (見つからなければ null)。
        /// </summary>
        private static HorizontalSpringResult? FindSpringResult(HorizontalSoilSpring spring, int level, int loadCaseNo, int comboNo, bool isLiq)
        {
            if (spring?.HorizontalSpringResults == null) return null;
            return spring.HorizontalSpringResults.LastOrDefault(r =>
                r.LoadCase != null && r.LoadCase.Level == level && r.LoadCase.No == loadCaseNo &&
                r.LoadCombination != null && r.LoadCombination.No == comboNo &&
                r.IsLiquefaction == isLiq);
        }

        /// <summary>
        /// HCVM.StepSummariesSnapshot() を per-case に集計してスナップショット化。
        /// 集計キー: (Level, LoadCaseNo, ComboNo, IsLiquefaction)
        /// </summary>
        public static ConvergenceSnapshot AggregateSnapshot(
            HorizontalCalculationViewModel hcvm, string exampleName, RunOptions options)
        {
            var snapshot = new ConvergenceSnapshot
            {
                ExampleName = exampleName,
                ExampleDisplayName = hcvm.InputModel?.GroundsInput?.FirstOrDefault()?.GroundRef ?? exampleName,
                CapturedAt = DateTime.Now.ToString("yyyy-MM-dd"),
                Options = new SnapshotOptions
                {
                    Level1Steps = hcvm.Level1CalculationStepsCount,
                    Level2Steps = hcvm.Level2CalculationStepsCount,
                    LiquefactionMode = hcvm.LiquefactionOption.ToString(),
                    UseLineSearch = hcvm.UseLineSearch,
                    Parallelism = hcvm.MaxCaseDegreeOfParallelism,
                }
            };

            var summaries = hcvm.StepSummariesSnapshot();
            // (Level, LoadCaseNo, ComboNo, IsLiquefaction) でグループ化
            var grouped = summaries
                .GroupBy(s => (s.Level, s.LoadCaseNo, s.ComboNo, s.IsLiquefaction))
                .OrderBy(g => g.Key.Level)
                .ThenBy(g => g.Key.LoadCaseNo)
                .ThenBy(g => g.Key.ComboNo)
                .ThenBy(g => g.Key.IsLiquefaction);

            // 物理量取得用: HCVM の主モデル (case-local merge 済) を参照
            var anaModel = hcvm.CurrentModel;

            foreach (var grp in grouped)
            {
                var steps = grp.ToList();
                int totalIter = steps.Sum(s => s.Iterations);
                int totalSteps = steps.Count;
                int maxBisection = steps.Max(s => s.BisectionAttempt);
                bool allConverged = steps.All(s => s.Status == StepStatus.Converged);
                double finalResidual = steps.LastOrDefault()?.FinalResidual ?? 0.0;

                string liq = grp.Key.IsLiquefaction ? "Liq" : "NoLq";
                string caseKey = $"L{grp.Key.Level}-{grp.Key.LoadCaseNo}.C{grp.Key.ComboNo}.{liq}";

                // 代表点 (AP = Nodes[0]) の累積変位を当該 case の NodeResults から取得
                double apUx = 0, apUy = 0, apUz = 0, apRx = 0, apRy = 0, apRz = 0;
                double maxAbsHorizDisp = 0;
                double maxAbsHorizSpringReaction = 0;
                if (anaModel?.Nodes != null && anaModel.Nodes.Count > 0)
                {
                    var apResult = FindNodeResult(anaModel.Nodes[0],
                        grp.Key.Level, grp.Key.LoadCaseNo, grp.Key.ComboNo, grp.Key.IsLiquefaction);
                    if (apResult?.CumulativeDisp != null)
                    {
                        apUx = apResult.CumulativeDisp.Ux;
                        apUy = apResult.CumulativeDisp.Uy;
                        apUz = apResult.CumulativeDisp.Uz;
                        apRx = apResult.CumulativeDisp.Rx;
                        apRy = apResult.CumulativeDisp.Ry;
                        apRz = apResult.CumulativeDisp.Rz;
                    }

                    // 全節点中の最大絶対水平変位 (杭頭シア応答の代表値)
                    foreach (var node in anaModel.Nodes)
                    {
                        var nr = FindNodeResult(node,
                            grp.Key.Level, grp.Key.LoadCaseNo, grp.Key.ComboNo, grp.Key.IsLiquefaction);
                        if (nr?.CumulativeDisp == null) continue;
                        double horiz = Math.Sqrt(nr.CumulativeDisp.Ux * nr.CumulativeDisp.Ux
                                               + nr.CumulativeDisp.Uy * nr.CumulativeDisp.Uy);
                        if (horiz > maxAbsHorizDisp) maxAbsHorizDisp = horiz;
                    }
                }

                // 全水平地盤ばねの最大絶対反力 (杭周地盤の最大応答)
                if (anaModel?.HorizontalSoilSprings != null)
                {
                    foreach (var sp in anaModel.HorizontalSoilSprings)
                    {
                        var sr = FindSpringResult(sp,
                            grp.Key.Level, grp.Key.LoadCaseNo, grp.Key.ComboNo, grp.Key.IsLiquefaction);
                        if (sr?.CumulativeForce == null) continue;
                        double fx = sr.CumulativeForce.GetByIndex(0);
                        double fy = sr.CumulativeForce.GetByIndex(1);
                        double horizForce = Math.Sqrt(fx * fx + fy * fy);
                        if (horizForce > maxAbsHorizSpringReaction)
                            maxAbsHorizSpringReaction = horizForce;
                    }
                }

                snapshot.Cases.Add(new CaseRecord
                {
                    CaseKey = caseKey,
                    Level = grp.Key.Level,
                    LoadCaseNo = grp.Key.LoadCaseNo,
                    CombinationNo = grp.Key.ComboNo,
                    IsLiquefaction = grp.Key.IsLiquefaction,
                    Converged = allConverged,
                    TotalIterations = totalIter,
                    TotalSteps = totalSteps,
                    BisectionRetries = maxBisection,
                    FinalResidual = finalResidual,
                    ApUx = apUx, ApUy = apUy, ApUz = apUz,
                    ApRx = apRx, ApRy = apRy, ApRz = apRz,
                    MaxAbsHorizDisp = maxAbsHorizDisp,
                    MaxAbsHorizSpringReaction = maxAbsHorizSpringReaction,
                });
            }

            snapshot.SummaryStats = new Summary
            {
                TotalCases = snapshot.Cases.Count,
                ConvergedCases = snapshot.Cases.Count(c => c.Converged),
                TotalIterations = snapshot.Cases.Sum(c => c.TotalIterations),
                TotalSteps = snapshot.Cases.Sum(c => c.TotalSteps),
                MaxResidualOverAllCases = snapshot.Cases.Count > 0
                    ? snapshot.Cases.Max(c => c.FinalResidual) : 0.0,
            };

            return snapshot;
        }
    }
}
