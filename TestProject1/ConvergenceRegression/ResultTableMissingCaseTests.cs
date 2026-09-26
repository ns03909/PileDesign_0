using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 解析結果テーブルが、表示中の荷重条件の結果が欠けたときに別の条件の値を出さないこと、
    /// 杭頭応力の杭番号が実際の杭を指すこと。
    ///
    /// 2026-09-26 のレビューで 2 件:
    /// - 結果が無いと要素・節点・ばね本体の CumulativeForce / CumulativeDisp (最後に解いた状態) に切り替えていた。
    ///   それが別の荷重条件の値だと、表の条件名と数値が食い違う
    /// - 杭頭応力の杭番号を、杭頭要素を見つけた順に 1, 2, … と数えていた
    /// </summary>
    [TestClass]
    public class ResultTableMissingCaseTests
    {
        private static MainWindowViewModel? Run()
        {
            try
            {
                return HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Both,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return null;
            }
        }

        [TestMethod]
        public void MissingResultsAreOmittedNotFilledWithAnotherCase()
        {
            var vm = Run();
            if (vm == null) return;
            var model = vm.CurrentModel!;
            var cases = model.AnalysisStepResults
                .GroupBy(s => (s.LoadCase.Level, s.LoadCase.No, s.LoadCombination?.No, s.IsLiquefaction))
                .Select(g => g.OrderBy(s => s.Step).Last())
                .ToList();
            Assert.IsTrue(cases.Count >= 2, "(前提) 荷重条件が 2 つ以上ありません");
            var shown = cases[0];

            var service = new AnalysisResultTableService();
            var full = service.BuildTables(model, shown.LoadCase, shown.LoadCombination, shown.IsLiquefaction, shown.Step);
            Assert.IsTrue(full.All(t => t.OmittedRowCount == 0), "(前提) 結果が揃っているのに行を省いています: "
                + string.Join(", ", full.Where(t => t.OmittedRowCount > 0).Select(t => t.Name)));

            bool Same(LoadCaseResultKey k) => PileDesign.Models.InputData.LoadCase.IsSameCase(k.LoadCase, shown.LoadCase)
                && PileDesign.Models.InputData.LoadCombination.IsSameCombination(k.LoadCombination, shown.LoadCombination)
                && k.IsLiquefaction == shown.IsLiquefaction && k.Step == shown.Step;

            // 表示する条件の結果を、梁 1 本・節点 1 つ・地盤ばね 1 本・杭頭要素 1 本から消す
            var beam = model.Beams.First(b => !b.IsPileHeadElement && b.BeamResults.Count > 0);
            var head = model.Beams.First(b => b.IsPileHeadElement);
            var node = model.Nodes.First(n => n.NodeResults.Count > 0);
            var spring = model.HorizontalSoilSprings.First(s => s.HorizontalSpringResults.Count > 0);
            beam.BeamResults.RemoveAll(r => Same(new(r.LoadCase, r.LoadCombination, r.IsLiquefaction, r.Step)));
            head.BeamResults.RemoveAll(r => Same(new(r.LoadCase, r.LoadCombination, r.IsLiquefaction, r.Step)));
            node.NodeResults.RemoveAll(r => Same(new(r.LoadCase, r.LoadCombination, r.IsLiquefaction, r.Step)));
            spring.HorizontalSpringResults.RemoveAll(r => Same(new(r.LoadCase, r.LoadCombination, r.IsLiquefaction, r.Step)));
            // 本体の「現在の値」は別の条件のものにしておく (目印の大きな値)
            beam.CumulativeForce = new BeamForce(9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999);

            var tables = service.BuildTables(model, shown.LoadCase, shown.LoadCombination, shown.IsLiquefaction, shown.Step);
            ResultTable T(string category) => tables.Single(t => t.Category == category);

            var forces = T("BeamForce");
            Assert.AreEqual(2, forces.OmittedRowCount, "梁断面力: 結果の無い梁の行を省いていません");
            int beamIndex = model.Beams.IndexOf(beam) + 1;
            Assert.IsFalse(forces.Rows.OfType<ElementSectionForceRow>().Any(r => r.ElementIndex == beamIndex),
                "梁断面力: 結果の無い梁に本体の現在値 (別の条件の値) を出しています");
            StringAssert.Contains(forces.DisplayName, "結果の無い 2 行は省略");

            Assert.AreEqual(2, T("BeamDisp").OmittedRowCount);
            Assert.AreEqual(1, T("NodeDisp").OmittedRowCount, "節点変位: 結果の無い節点を省いていません");
            Assert.AreEqual(1, T("SoilSpringForce").OmittedRowCount, "地盤反力: 結果の無いばねを省いていません");
            Assert.AreEqual(1, T("PileHeadForce").OmittedRowCount, "杭頭応力: 結果の無い杭頭を省いていません");
            Assert.AreEqual(full.Single(t => t.Category == "PileHeadForce").Count - 1, T("PileHeadForce").Count);

            var mphi = tables.SingleOrDefault(t => t.Category == "MPhiCurve");
            if (mphi != null)
                Assert.IsFalse(mphi.Rows.OfType<MPhiCurveRow>().Any(r => r.ElementIndex == beamIndex && r.AnalysisAxialForce == -9999),
                    "M-φ: 結果の無い要素の解析軸力に本体の現在値を出しています");
        }

        /// <summary>杭頭応力の杭番号は杭頭要素の i 端 (杭節点-{杭番号}-0) から取る。要素の並びに依らない。</summary>
        [TestMethod]
        public void PileHeadRowsCarryTheActualPileNumber()
        {
            var vm = Run();
            if (vm == null) return;
            var model = vm.CurrentModel!;
            var last = model.AnalysisStepResults.OrderBy(s => s.Step).Last();

            // 要素の並びを逆にしても、杭番号が付け替わらないこと
            model.Beams.Reverse();
            var table = new AnalysisResultTableService()
                .BuildTables(model, last.LoadCase, last.LoadCombination, last.IsLiquefaction, last.Step)
                .Single(t => t.Category == "PileHeadForce");

            var rows = table.Rows.OfType<PileHeadForceRow>().ToList();
            int piles = vm.CurrentInputModel!.PileLayoutItems.Count;
            Assert.AreEqual(piles, rows.Count);
            foreach (var row in rows)
                Assert.AreEqual($"杭節点-{row.PileNo}-0", row.NodeName, "杭頭応力の杭番号が、その杭頭要素の杭を指していません");
            CollectionAssert.AreEqual(Enumerable.Range(1, piles).ToList(), rows.Select(r => r.PileNo).ToList());
        }

        [TestMethod]
        public void PileNoIsReadFromThePileNodeName()
        {
            Node N(string name) { var n = new Node(); n.SetNodeInfo(name, 0, 0, 0); return n; }
            Assert.AreEqual(12, AnalysisResultTableService.PileNoOfPileNode(N("杭節点-12-0")));
            Assert.IsNull(AnalysisResultTableService.PileNoOfPileNode(N("杭地盤節点-12-0")));
            Assert.IsNull(AnalysisResultTableService.PileNoOfPileNode(N("杭節点-x-0")));
            Assert.IsNull(AnalysisResultTableService.PileNoOfPileNode(null));
        }

        private readonly record struct LoadCaseResultKey(
            PileDesign.Models.InputData.LoadCase LoadCase, PileDesign.Models.InputData.LoadCombination LoadCombination,
            bool IsLiquefaction, int Step);
    }
}
