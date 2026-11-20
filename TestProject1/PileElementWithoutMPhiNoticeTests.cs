using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 杭の非線形を選んでいるのに M-φ を作れなかった杭の要素は、初期剛性のまま (線形で) 解かれる。
    /// 以前は数えるだけでどこにも出さず、結果が黙って線形になっていた (外部レビュー #12 2026-11-20)。
    /// いまは完了の知らせ (ケースごと) に、杭番号と要素数を出す。
    /// </summary>
    [TestClass]
    public class PileElementWithoutMPhiNoticeTests
    {
        private static readonly HeadlessHorizontalRunner.RunOptions Options = new() { Level1Steps = 2, Level2Steps = 2 };

        [TestMethod]
        public void ElementsLeftLinearAreReportedOncePerCase()
        {
            MphiCurveResolver.FailForTesting.Value = _ => true;
            PileDesign.ViewModels.HorizontalCalculationViewModel hcvm;
            try
            {
                hcvm = HeadlessHorizontalRunner.RunExampleForHcvm("Example3_1", "PileExample3_1", Options);
            }
            finally
            {
                MphiCurveResolver.FailForTesting.Value = null;
            }

            var notices = hcvm._caseNotices.Where(n => n.Contains("M-φ 関係を作れず")).ToList();
            Assert.IsTrue(notices.Count > 0, "M-φ を作れず線形で解いた要素を知らせていない");
            Assert.AreEqual(notices.Count, notices.Distinct().Count(), "荷重を刻み直すたびに同じ知らせを積んでいる");
            var pileNos = hcvm.InputModel.PileLayoutItems.Select(p => $"No.{p.No}").ToList();
            foreach (string n in notices)
            {
                foreach (string no in pileNos) StringAssert.Contains(n, no, "杭番号が抜けている");
                StringAssert.Contains(n, "初期剛性のまま");
                Assert.IsFalse(n.Contains("杭が特定できない"), "例題の要素はすべて杭が分かるはず");
            }
        }

        [TestMethod]
        public void NothingIsReportedWhenEveryElementHasACurve()
        {
            var hcvm = HeadlessHorizontalRunner.RunExampleForHcvm("Example3_1", "PileExample3_1", Options);
            Assert.IsFalse(hcvm._caseNotices.Any(n => n.Contains("M-φ 関係を作れず")),
                string.Join(" / ", hcvm._caseNotices));
        }
    }
}
