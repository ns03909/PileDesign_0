using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 解析の実行中に画面の設定を変えても、走っている解析と、残す実行の記録は変わらないこと (2026-10-11)。
    ///
    /// <para>以前は 3 つの解析 (水平解析・単杭沈下解析（基礎梁考慮）・群杭沈下解析（基礎梁あり）) が、
    /// 画面の設定をケースごと・ステップごと・反復ごとにその場で読んでいた。設定の欄は実行中も触れたので、
    /// 途中で変えると以降のケースやステップだけが別の設定で解かれた。水平解析は実行の記録
    /// (LastRunConfig・計算書の実行条件) を<b>終わったとき</b>の値で取っていたので、使っていない設定が記録に残った。</para>
    ///
    /// <para>直したあとは、開始時に設定の写しを 1 回だけ取り、解析も記録もその写しだけを読む。
    /// ここでは「実行中に変える」をテスト用の差し込み口 (ケース・ステップの直前) で再現し、
    /// 何も変えない実行と結果が同じであることを見る。</para>
    /// </summary>
    [TestClass]
    public class RunSettingsSnapshotTests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        // ── 水平解析 ─────────────────────────────

        private static HeadlessHorizontalRunner.RunOptions HorizontalOptions(Action<HorizontalCalculationViewModel>? configure) => new()
        {
            Level1Steps = 2,
            Level2Steps = 4,
            LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
            UseLineSearch = true,
            Parallelism = 1,
            Configure = configure,
        };

        /// <summary>実行中に変える操作 (最初のケースを解き始める直前に 1 回だけ)。どれも、効けば解析か記録が変わる値にする。</summary>
        private static void EditEverySettingOnce(HorizontalCalculationViewModel vm, ref bool done)
        {
            if (done) return;
            done = true;
            vm.LiquefactionOption = HorizontalCalculationViewModel.LiquefactionOptionType.Both;
            vm.Level1CalculationStepsCount = 7;
            vm.Level2CalculationStepsCount = 9;
            vm.UseModifiedNewtonRaphson = !vm.UseModifiedNewtonRaphson;
            vm.FullNRIterations = 2;
            vm.SelectedConvergenceMethod = HorizontalCalculationViewModel.ConvergenceMethod.AdaptiveRelaxation;
            vm.RelaxationFactor = 0.3;
            vm.SkipIteration = true;
            vm.UseAnalysisAxialForce = !vm.UseAnalysisAxialForce;
            vm.StopONMaxDisplacement = true;
            vm.MaxAllowedDisplacement = 1e-9;   // 効けば最初の反復で解析を止める
            vm.MaxCaseDegreeOfParallelism = 4;
        }

        /// <summary>ステップごとの反復数・残差・状態を、ケースとステップの順に並べたもの (比べる用)。</summary>
        private static List<string> Steps(HorizontalCalculationViewModel vm)
            => vm.StepSummariesSnapshot()
                .OrderBy(s => s.Level).ThenBy(s => s.LoadCaseNo).ThenBy(s => s.ComboNo).ThenBy(s => s.IsLiquefaction)
                .ThenBy(s => s.BisectionAttempt).ThenBy(s => s.Step)
                .Select(s => $"L{s.Level}-{s.LoadCaseNo} C{s.ComboNo} Liq={s.IsLiquefaction} try{s.BisectionAttempt} " +
                             $"{s.Step}/{s.NStep} iter={s.Iterations} R={s.FinalResidual:R} {s.Status}")
                .ToList();

        [TestMethod]
        public void Horizontal_EditingTheSettingsDuringARun_ChangesNeitherTheRunNorItsRecord()
        {
            HorizontalCalculationViewModel baseline, edited;
            try
            {
                baseline = HeadlessHorizontalRunner.RunExampleForHcvm("Example9", "PileExample9", HorizontalOptions(null));
                bool done = false;
                edited = HeadlessHorizontalRunner.RunExampleForHcvm("Example9", "PileExample9", HorizontalOptions(
                    vm => vm.BeforeEachCaseForTesting = v => EditEverySettingOnce(v, ref done)));
                Assert.IsTrue(done, "(前提) 実行中に設定を変える差し込み口が呼ばれていません");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return;
            }

            var expected = Steps(baseline);
            Assert.IsTrue(expected.Count >= 2, "(前提) 比べるステップがありません");
            CollectionAssert.AreEqual(expected, Steps(edited),
                "実行中に変えた設定 (液状化・ステップ数・反復なし・緩和係数・収束の手法・修正 NR・杭軸力モード・変位閾値) で解析が変わりました");
            Assert.IsTrue(edited.IsAnalysisExecuted, "実行中に変えた変位閾値で解析が止まりました");

            // 記録 (追加実行の照合に使う前回の条件・計算書の実行条件) は、実行に使った設定
            var cfg = edited.AnaModels[^1].LastRunConfig;
            var baseCfg = baseline.AnaModels[^1].LastRunConfig;
            Assert.IsNotNull(cfg);
            Assert.IsNotNull(baseCfg);
            Assert.AreEqual(baseCfg.LiquefactionOption, cfg.LiquefactionOption, "前回の条件に、実行中に変えた液状化の選択が残りました");
            Assert.AreEqual(2, cfg.Level1StepsCount, "前回の条件に、実行中に変えたステップ数が残りました");
            Assert.AreEqual(4, cfg.Level2StepsCount);
            Assert.AreEqual(baseCfg.SkipIteration, cfg.SkipIteration);
            Assert.AreEqual(baseCfg.RelaxationFactor, cfg.RelaxationFactor);
            Assert.AreEqual(baseCfg.UseModifiedNewtonRaphson, cfg.UseModifiedNewtonRaphson);
            Assert.AreEqual(baseCfg.FullNRIterations, cfg.FullNRIterations);
            Assert.AreEqual(baseCfg.UseLineSearch, cfg.UseLineSearch);
            Assert.AreEqual(baseCfg.UseAnalysisAxialForce, cfg.UseAnalysisAxialForce, "前回の条件に、実行中に変えた杭軸力モードが残りました");

            var meta = edited.SettingsAtLastRunForTesting;
            var baseMeta = baseline.SettingsAtLastRunForTesting;
            Assert.IsNotNull(meta);
            Assert.IsNotNull(baseMeta);
            Assert.AreEqual(baseMeta.DescribeSettings(), meta.DescribeSettings(),
                "計算書の「解析の実行条件」に、実行中に変えた設定が載りました");
        }

        // ── 単杭沈下解析（基礎梁考慮） ─────────────────────────────

        /// <summary>杭 3 本を 1 列に並べ、基礎梁 2 本でつないだ入力 (<see cref="VerticalBeamHandCalculationTests"/> と同じ形)。</summary>
        private static InputModel ThreePilesOnABeam()
        {
            var piles = Enumerable.Range(0, 3).Select(i => new PileLayoutDataItem { No = i + 1, X = i * 5.0, Y = 0, Z = 0 }).ToArray();
            var input = new InputModel
            {
                PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles),
                FoundationBeamInput = new FoundationBeamInput
                {
                    Materials = [new BeamMaterial { YoungModulus = 2.5e7, PoissonRatio = 0.2 }],
                    Sections = [],
                    Beams = [],
                },
                FundamentalInput = new FundamentalInput(),
            };
            for (int i = 0; i < 2; i++)
                input.FoundationBeamInput.Beams.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = piles[i].UniqueId,
                    NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = piles[i + 1].UniqueId,
                    MaterialNo = 1, Width = 0.5, Height = 1.0, YoungModulus = 2.5e7,
                });
            return input;
        }

        /// <summary>非線形の荷重-沈下曲線 (反復が 1 回では収まらない)。</summary>
        private static VerticalPileSpringCurve Curve() => new(new ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement>
        {
            new() { PileTopLoad = 0, DD0s = 0 },
            new() { PileTopLoad = 1000, DD0s = 2 },
            new() { PileTopLoad = 2000, DD0s = 6 },
            new() { PileTopLoad = 3000, DD0s = 15 },
        });

        /// <summary>VL 相当の荷重 (中央の杭に重く) を、設定の写しで荷重ステップに分けて解く。</summary>
        private static async Task<(VerticalBeamCaseResult Result, double[] Settlements)> SolveVerticalBeam(
            Action<VerticalBeamCalculationViewModel>? beforeEachStep)
        {
            var input = ThreePilesOnABeam();
            var vm = new VerticalBeamCalculationViewModel(new MainWindowViewModel { CurrentInputModel = input })
            {
                LoadStepsCount = 5, MaxIterations = 100, ConvergenceTolerance = 1e-8, RelaxationFactor = 0.8,
            };
            var settings = vm.CaptureSettings();
            vm.BeforeEachStepForTesting = beforeEachStep;

            var modelling = new VerticalBeamModelling(input);
            foreach (var no in modelling.PileSpringMap.Keys) modelling.SpringCurves[no] = Curve();
            var anaModel = modelling.BuildAnaModel();
            anaModel.InitializeStates();
            var loads = new Dictionary<int, double> { [1] = 1200, [2] = 2500, [3] = 900 };
            vm.SetIncrementalLoads(modelling, anaModel, settings.LoadStepsCount, p => loads[p.No]);
            anaModel.MapOnVectorDF();
            foreach (var (no, spring) in modelling.PileSpringMap)
            {
                double k0 = modelling.SpringCurves[no].InitialTangentStiffness;
                spring.SetKe(0, 0, k0, 0, 0, 0, true);
                spring.SetKe(0, 0, k0, 0, 0, 0, false);
            }

            var result = await vm.RunLoadSteps(modelling, anaModel, settings, "VL", 1, 1, CancellationToken.None);
            var settlements = modelling.ConnectionNodes.OrderBy(kv => kv.Key).Select(kv => -kv.Value.CumulativeDisp.Uz).ToArray();
            return (result, settlements);
        }

        [TestMethod]
        public async Task VerticalBeam_EditingTheSettingsDuringARun_DoesNotChangeTheSteps()
        {
            var (baseline, baseSettlements) = await SolveVerticalBeam(null);
            Assert.AreEqual(5, baseline.StepResults.Count, "(前提) ステップ数どおりに解いていません");
            Assert.IsTrue(baseline.StepResults.Any(s => s.Iterations > 1), "(前提) 反復が 1 回で収まる問題では、反復の設定を変えても差が出ません");

            int calls = 0;
            var (edited, editedSettlements) = await SolveVerticalBeam(vm =>
            {
                if (++calls != 2) return;   // 2 ステップ目の直前に変える
                vm.LoadStepsCount = 2;
                vm.MaxIterations = 1;
                vm.ConvergenceTolerance = 1e-2;
                vm.RelaxationFactor = 0.2;
            });

            Assert.AreEqual(baseline.StepResults.Count, edited.StepResults.Count,
                "実行中に変えたステップ数でステップのループが切れました (増分はケースの始めに決めているので、荷重が全部は載りません)");
            for (int i = 0; i < baseline.StepResults.Count; i++)
            {
                Assert.AreEqual(baseline.StepResults[i].Iterations, edited.StepResults[i].Iterations,
                    $"ステップ {i + 1}: 実行中に変えた反復の設定 (上限・基準・緩和係数) で反復が変わりました");
                Assert.AreEqual(baseline.StepResults[i].Residual, edited.StepResults[i].Residual, $"ステップ {i + 1} の残差");
            }
            CollectionAssert.AreEqual(baseSettlements, editedSettlements, "実行中に設定を変えると沈下が変わりました");
        }

        [TestMethod]
        public void VerticalBeam_TheRecordedConditionsComeFromTheSameSnapshot()
        {
            var vm = new VerticalBeamCalculationViewModel(new MainWindowViewModel()) { LoadStepsCount = 7, AnalyzeLevel2 = false };
            var settings = vm.CaptureSettings();
            vm.LoadStepsCount = 3;
            vm.AnalyzeLevel2 = true;
            var record = VerticalBeamCalculationViewModel.CaptureRunSettings(settings);
            StringAssert.Contains(record.DescribeSettings(), "荷重ステップ数 7");
            StringAssert.Contains(record.DescribeSettings(), "荷重 VL・L1");

            string src = TestSource.Read("Graphics_r1", "ViewModels", "VerticalBeamCalculationViewModel.cs");
            string run = TestSource.MethodBody(src, "private async Task RunAsync(");
            foreach (var name in new[] { "AnalyzeVL", "AnalyzeLevel1", "AnalyzeLevel2", "LoadStepsCount" })
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run, $@"(?<![\w.]){name}\b"),
                    $"解析の本体が画面の {name} をその場で読んでいます (開始時の写しを読むこと)");
        }

        // ── 群杭沈下解析（基礎梁あり） ─────────────────────────────

        [TestMethod]
        public async Task GroupWithBeam_EditingTheIterationSettingsDuringARun_DoesNotReachLaterCases()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            model.LoadCasesInput.LoadCasesLevel1 = new ObservableCollection<LoadCase>(
                [new LoadCase { Level = 1, No = 1, LoadName = "E1", IsAnalysisTarget = true, IsApplicable = true },
                 new LoadCase { Level = 1, No = 2, LoadName = "E2", IsAnalysisTarget = true, IsApplicable = true }]);
            foreach (var pile in model.PileLayoutItems)
            {
                pile.AxialForceVL0 = 1000;
                pile.AxialForceLevel1s = [800.0, 1200.0];
            }
            var vm = new GroupSettlementWithBeamCalculationViewModel(new MainWindowViewModel { CurrentInputModel = model })
            {
                LoadSource = GroupSettlementWithBeamCalculationViewModel.SourceAxialForce,
                AnalyzeVL = true,
                AnalyzeLevel1 = true,
                MaxIterations = 50,
                ConvergenceTolerance = 1e-5,
                KMin = 2e3,
                KMax = 3e9,
            };
            Assert.IsNull(vm.ValidateInput(), "(前提) 入力の検査を通りません");

            var seen = new List<(string Label, GroupSettlementWithBeamCalculationViewModel.GroupBeamRunParameters P)>();
            vm.CaseRunnerForTesting = (label, ppi, p) =>
            {
                seen.Add((label, p));
                if (seen.Count == 1)
                {
                    // 最初のケースを解いている間に、画面で反復の設定を書き換える
                    vm.MaxIterations = 1;
                    vm.ConvergenceTolerance = 0.5;
                    vm.KMin = 1;
                    vm.KMax = 2;
                }
                var r = new IterativeBeamSettlementResult { Converged = true };
                foreach (var no in ppi.Keys)
                {
                    r.PileReactions[no] = ppi[no];
                    r.BeamSettlement[no] = 0.001;
                    r.SteinbrennerSettlement[no] = 0.001;
                    r.SpringStiffness[no] = 1e6;
                }
                return r;
            };

            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ExecuteAnalysisCommand).ExecuteAsync(null);

            Assert.IsTrue(seen.Count >= 3, $"(前提) ケースが 3 つ以上ありません ({seen.Count})");
            var expected = new GroupSettlementWithBeamCalculationViewModel.GroupBeamRunParameters(50, 1e-5, 2e3, 3e9);
            foreach (var (label, p) in seen)
                Assert.AreEqual(expected, p, $"{label}: 実行中に変えた反復の設定が、あとのケースに渡りました");

            Assert.IsNotNull(vm.LastRunSettings, "(前提) 解析が完了していません");
            Assert.AreEqual(50, vm.LastRunSettings.MaximumIterations, "記録した条件が、解析に使った値と違います");
            Assert.AreEqual(1e-5, vm.LastRunSettings.BaseResidualTolerance);
            StringAssert.Contains(vm.LastRunSettings.DescribeSettings(), $"{2e3:G} 〜 {3e9:G} kN/m");
        }
    }
}
