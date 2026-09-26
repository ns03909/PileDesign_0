using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 計算書のまとめ表 (杭検討結果まとめ一覧・水平反力合計) が、集めるべき結果をすべて集め、
    /// 欠けた結果を数値として出さないこと。
    /// </summary>
    [TestClass]
    public class ReportSummaryTableTests
    {
        private static MainWindowViewModel? _analyzed;
        private static readonly object Gate = new();

        /// <summary>計算例9 (杭体の区間 2 つが要素 3 つと 6 つに分かれる) を解析したもの。</summary>
        private static MainWindowViewModel Analyzed()
        {
            lock (Gate)
            {
                if (_analyzed != null) return _analyzed;
                try
                {
                    _analyzed = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 2, Level2Steps = 2, UseLineSearch = true, Parallelism = 1,
                        LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
                {
                    Assert.Inconclusive("例題ファイルなし");
                }
                _analyzed!.DocxOutput.IncludeOutputLiquefactionYes = true;
                _analyzed.DocxOutput.IncludeOutputLiquefactionNo = true;
                return _analyzed;
            }
        }

        private static WordDocument Doc(MainWindowViewModel vm) => new(vm.ResultInputModel, vm.CurrentModel!, vm);

        /// <summary>
        /// 区間の最大値が、その区間に入る<b>すべての要素</b>から集められていること。
        /// 区間への割り当ては実装 (区間番号) と独立に、要素の深さで決めて突き合わせる。
        /// </summary>
        [TestMethod]
        public void SectionMaximaCoverEveryElementOfTheSection()
        {
            var vm = Analyzed();
            var input = vm.ResultInputModel;
            var model = vm.CurrentModel!;
            var rows = Doc(vm).CollectPileForceSummary();

            var elementsPerSection = new Dictionary<(string, int), int>();
            foreach (var row in rows)
            {
                var bodyNo = input.PileBodies.ToList().FindIndex(b => b.PileBodyRef == row.PileBodyRef) + 1;
                for (int k = 0; k < 2; k++)
                {
                    int level = k + 1;
                    double expected = double.NaN;
                    int elements = 0;
                    foreach (var pile in input.PileLayoutItems!.Where(p => p.PileBodyNo == bodyNo))
                    {
                        var divided = input.ElementDivision!.SoilPiles[pile.SoilPileAltNo - 1].PileBodySegments;
                        foreach (var beam in pile.Beams)
                        {
                            var seg = divided[beam.SegmentIndex!.Value];
                            double mid = seg.SegmentDepth - seg.SegmentLength / 2;
                            if (mid < row.Top || mid > row.Bottom) continue;   // 深さで区間を決める
                            elements++;
                            foreach (var lc in input.LoadCasesInput.AllSeismicLoadCases.Where(c => c.Level == level))
                                foreach (var comb in input.LoadCasesInput.AllLoadCombinations)
                                    foreach (var liq in new[] { true, false })
                                    {
                                        var f = beam.GetBeamResult(model, lc, comb, liq)?.CumulativeForce;
                                        if (f != null) expected = double.IsNaN(expected) ? f.MabsMax : Math.Max(expected, f.MabsMax);
                                    }
                        }
                    }
                    elementsPerSection[(row.PileBodyRef, row.SegmentNo)] = elements;
                    if (double.IsNaN(expected)) continue;
                    Assert.AreEqual(expected, row.Mmax[k]!.Value, 1e-9 * Math.Max(1, expected),
                        $"{row.PileBodyRef} 区間{row.SegmentNo} レベル{level}: 最大モーメントが区間のすべての要素から集められていません");
                }
            }
            Assert.IsTrue(elementsPerSection.Values.Any(n => n > 1),
                "(前提) 1 区間が複数要素に分かれるモデルであること");
        }

        /// <summary>
        /// ある区間・レベルの結果が無いときは値を持たせない (表では「—」)。
        /// 以前は初期値 (double.MinValue) をそのまま数値で書き出していた。
        /// </summary>
        [TestMethod]
        public void ASectionWithoutResultsHasNoValue()
        {
            var vm = Analyzed();
            var input = vm.ResultInputModel;
            // 杭体 1 の区間 1 に属する要素の結果をすべて外す
            var removed = new List<(Beam Beam, List<BeamResult> Results)>();
            foreach (var pile in input.PileLayoutItems!.Where(p => p.PileBodyNo == 1))
            {
                var divided = input.ElementDivision!.SoilPiles[pile.SoilPileAltNo - 1].PileBodySegments;
                foreach (var beam in pile.Beams.Where(b => divided[b.SegmentIndex!.Value].No == 1))
                {
                    removed.Add((beam, beam.BeamResults.ToList()));
                    beam.BeamResults.Clear();
                }
            }
            try
            {
                var rows = Doc(vm).CollectPileForceSummary();
                var first = rows.First(r => r.PileBodyRef == input.PileBodies[0].PileBodyRef && r.SegmentNo == 1);
                Assert.IsNull(first.Mmax[0], "結果の無い区間に値があります (初期値が数値として出る)");
                Assert.IsNull(first.Qmax[1]);
                Assert.IsTrue(first.HasMissing(0), "結果の無いセルがあるのに、表の注記の対象になりません");
                Assert.IsTrue(rows.Where(r => r != first).Any(r => r.Mmax[1] is double v && v > 0), "(前提) ほかの区間には結果があること");
            }
            finally
            {
                foreach (var (beam, results) in removed) beam.BeamResults.AddRange(results);
            }
        }

        /// <summary>水平反力の合計で、結果の無いばねを数え、欠けたときは表で示すこと。</summary>
        [TestMethod]
        public void MissingSpringResultsAreCountedInTheReactionTotal()
        {
            var vm = Analyzed();
            var model = vm.CurrentModel!;
            var springs = model.HorizontalSoilSprings.Where(s => s.NodeJ?.Name?.StartsWith("杭地盤節点-") == true).ToList();
            var last = model.AnalysisStepResults.OrderBy(s => s.Step).Last();

            var (full, foundAll) = WordDocument.SumHorizontalReaction(springs, last.LoadCase, last.LoadCombination, last.IsLiquefaction, last.Step);
            Assert.AreEqual(springs.Count, foundAll, "(前提) すべてのばねに結果があること");

            var saved = springs[0].HorizontalSpringResults.ToList();
            try
            {
                springs[0].HorizontalSpringResults.Clear();
                var (_, found) = WordDocument.SumHorizontalReaction(springs, last.LoadCase, last.LoadCombination, last.IsLiquefaction, last.Step);
                Assert.AreEqual(springs.Count - 1, found, "結果の無いばねを数えていません");
            }
            finally
            {
                springs[0].HorizontalSpringResults.AddRange(saved);
            }

            Assert.AreEqual("L1: 12.3", WordDocument.ReactionCellEntry("L1", 12.34, 10, 10));
            Assert.AreEqual("L1: 12.3 ※(9/10)", WordDocument.ReactionCellEntry("L1", 12.34, 9, 10), "欠けたばねがあるのに完全な合計のように出します");
            Assert.AreEqual("L1: 結果なし", WordDocument.ReactionCellEntry("L1", 0, 0, 10));
        }
    }
}
