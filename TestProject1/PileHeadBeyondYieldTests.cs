using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 杭頭回転角で、杭頭の回転ばねが降伏後の枝にあることを知らせる。
    ///
    /// <para>2026-10-02 に調べたところ、計算例8 の安全限界の杭頭回転角は、要素を細かくするたびに検定比が
    /// 0.62 → 1.06 → 0.90 → 1.13 と行き来した。大部分は解法の経路への依存で、直した
    /// (<see cref="PileHeadSpringPathIndependenceTests"/>)。直したあとも降伏後の枝ではモーメントの差が回転角に約 5 倍で効き、
    /// 要素分割で 1 割ほど動く (0.64〜0.77)。該当する検定に「降伏後」と示し、まとめと計算書で注意する。</para>
    /// </summary>
    [TestClass]
    public class PileHeadBeyondYieldTests
    {
        [TestMethod]
        public void TheYieldRotation_IsTheBreakBeforeTheLastPoint()
        {
            var rc = new MomentRotationCurve();
            rc.CurvePoints = [new() { Theta = 0, Moment = 0 }, new() { Theta = 1e-8, Moment = 400 }, new() { Theta = 1.3e-3, Moment = 1480 }, new() { Theta = 0.01, Moment = 2100 }];
            Assert.AreEqual(1.3e-3, EvaluationService.YieldRotationOf(rc));

            var linear = new MomentRotationCurve();
            linear.CurvePoints = [new() { Theta = 0, Moment = 0 }, new() { Theta = 0.01, Moment = 1000 }];
            Assert.IsNull(EvaluationService.YieldRotationOf(linear), "折れ点の無い曲線に降伏点はない");
            Assert.IsNull(EvaluationService.YieldRotationOf(null));
        }

        [TestMethod]
        [Timeout(600000)]
        public void TheExample_FlagsTheRotationsBeyondYield_AndTheSummarySaysSo()
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("ExampleK8", "PileExampleK8", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4,
                    Level2Steps = 8,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            vm.ApplyConcreteModelOptions();
            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);

            var rotations = result.Items.Where(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileHeadRotation && i.IsJudged).ToList();
            TestSource.AssertScanned(rotations.Count, 2, "杭頭回転角の検定");
            var beyond = rotations.Where(i => i.IsPileHeadBeyondYield).ToList();
            Assert.IsTrue(beyond.Count > 0, "計算例8 の安全限界では杭頭のばねが降伏しているのに、「降伏後」と示していません");
            Assert.IsTrue(beyond.All(i => i.BasisText.Contains("降伏後", StringComparison.Ordinal)), "根拠の欄に「降伏後」がありません");
            Assert.IsTrue(rotations.Any(i => !i.IsPileHeadBeyondYield && i.BasisText.Contains("降伏前", StringComparison.Ordinal)),
                "降伏前の杭頭回転角まで「降伏後」にしています (またはθy を示していません)");
            Assert.AreEqual(beyond.Count, result.PileHeadBeyondYieldCount);

            string text = EvaluationService.BuildEvaluationText(vm, factored: true, displayFilter: 2);
            StringAssert.Contains(text, "杭頭のばねが降伏後の枝にある杭頭回転角");
        }
    }
}
