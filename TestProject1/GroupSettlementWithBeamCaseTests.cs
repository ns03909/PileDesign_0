using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 基礎梁を考慮した群杭沈下解析 (2026-09-27 のレビュー)。
    /// 2. 杭ごとの結果の欠けを 0 で埋めない (以前は「反力 0 kN・沈下 0 mm」として保存し得た)
    /// 1. 解析対象・適用を外した荷重ケースを解かない (以前はレベルの荷重ケースをすべて並べた)
    /// </summary>
    [TestClass]
    public class GroupSettlementWithBeamCaseTests
    {
        private static (GroupSettlementWithBeamCalculationViewModel vm, InputModel input) Build()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, error);
            var main = new MainWindowViewModel { CurrentInputModel = input };
            return (new GroupSettlementWithBeamCalculationViewModel(main), input);
        }

        private static IterativeBeamSettlementResult ResultFor(IEnumerable<int> pileNos, int? missingPile)
        {
            var r = new IterativeBeamSettlementResult { Converged = true };
            foreach (int no in pileNos)
            {
                if (no == missingPile) continue;
                r.PileReactions[no] = 100;
                r.BeamSettlement[no] = 0.001;
                r.SteinbrennerSettlement[no] = 0.002;
                r.SpringStiffness[no] = 5000;
            }
            return r;
        }

        [TestMethod]
        public void AMissingPileIsRecordedNotFilledWithZero()
        {
            var (vm, input) = Build();
            var pileNos = input.PileLayoutItems.Select(p => p.PileNo).ToList();
            int missing = pileNos[1];
            var ppi = pileNos.ToDictionary(n => n, _ => 1000.0);

            var result = vm.ToCaseResult("VL", ppi, ResultFor(pileNos, missing));

            CollectionAssert.AreEqual(new[] { missing }, result.MissingPileNos, "欠けた杭を記録していません");
            var row = result.PileResults.Single(p => p.PileNo == missing);
            Assert.IsTrue(row.IsMissing);
            Assert.IsTrue(double.IsNaN(row.Reaction_kN) && double.IsNaN(row.Settlement_mm),
                "欠けた杭の反力・沈下を 0 で埋めています (本当に 0 の結果と区別できない)");
            StringAssert.Contains(result.DisplayName, "結果欠損");
            StringAssert.Contains(result.MissingWarning, $"杭No.{missing}");

            var complete = vm.ToCaseResult("VL", ppi, ResultFor(pileNos, null));
            Assert.AreEqual(0, complete.MissingPileNos.Count);
            Assert.AreEqual("", complete.MissingWarning);
        }

        [TestMethod]
        public void ACaseWithMissingPilesIsNotSaved()
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var (vm, input) = Build();
                var pileNos = input.PileLayoutItems.Select(p => p.PileNo).ToList();
                var ppi = pileNos.ToDictionary(n => n, _ => 1000.0);
                vm.CaseResults.Add(vm.ToCaseResult("VL", ppi, ResultFor(pileNos, pileNos[0])));
                int before = input.PileGroupSettlement.CaseRecords.Count;

                vm.OkCommand.Execute(null);

                Assert.AreEqual(before, input.PileGroupSettlement.CaseRecords.Count, "結果の欠けたケースを確定しています");
                Assert.IsFalse(vm.IsSaved);
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }

        [TestMethod]
        public void OnlyTargetAndApplicableCasesAreSolved()
        {
            var (vm, input) = Build();
            input.LoadCasesInput.LoadCasesLevel1 = new System.Collections.ObjectModel.ObservableCollection<LoadCase>(
                Enumerable.Range(1, 3).Select(i => new LoadCase { Level = 1, No = i, LoadName = $"E{i}", IsAnalysisTarget = true, IsApplicable = true }));
            var level1 = input.LoadCasesInput.LoadCasesLevel1;
            level1[1].IsAnalysisTarget = false;   // 解析対象から外す
            level1[2].IsApplicable = false;       // 適用しない

            vm.LoadSource = "杭軸力";
            vm.AnalyzeLevel1 = true;
            vm.AnalyzeLevel2 = false;

            var planned = vm.PlannedSeismicCases();
            Assert.IsTrue(planned.Contains(level1[0]));
            Assert.IsFalse(planned.Contains(level1[1]), "解析対象から外したケースを解いています");
            Assert.IsFalse(planned.Contains(level1[2]), "適用しないケースを解いています");
            Assert.IsTrue(planned.All(c => c.Level == 1), "選んでいないレベルのケースを解いています");

            StringAssert.Contains(vm.PlannedCasesText, $"L1-{level1[0].No}: {level1[0].LoadName}", "解くケースの名前を示していません");
            Assert.IsFalse(vm.PlannedCasesText.Contains($"L1-{level1[1].No}:"));
        }
    }
}
