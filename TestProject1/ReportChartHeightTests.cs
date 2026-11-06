using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Output;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>計算書の結果のグラフの高さを 1 ページ 3 枚 / 2 枚から選ぶ (利用者の要望 2026-11-07)。</summary>
    [TestClass]
    public class ReportChartHeightTests
    {
        [TestMethod]
        public void TheDefaultIsThreeChartsPerPage()
        {
            var vm = new MainWindowViewModel { IsHorizontalAnalysisDone = true };
            Assert.AreEqual(3, vm.DocxOutput.ChartsPerPage);
            vm.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
            Assert.AreEqual(3, vm.DocxOutput.ChartsPerPage, "「すべて選択」で高さが変わりました");
        }

        [TestMethod]
        public void TwoPerPageIsTallerThanThreePerPage()
        {
            Assert.AreEqual(WordDocument.Layout.ChartHeightMm3PerPage, WordDocument.Layout.ChartHeightMmFor(3));
            Assert.AreEqual(WordDocument.Layout.ChartHeightMm2PerPage, WordDocument.Layout.ChartHeightMmFor(2));
            Assert.AreEqual(WordDocument.Layout.ChartHeightMm3PerPage, WordDocument.Layout.ChartHeightMmFor(0), "想定外の数は 3 枚として扱う");
            Assert.IsTrue(WordDocument.Layout.ChartHeightMm2PerPage > WordDocument.Layout.ChartHeightMm3PerPage * 1.5);
        }
    }
}
