using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using PileDesign.Services;

namespace TestProject1
{
    [TestClass]
    public class GroupSettlementCaseLabelTests
    {
        [TestMethod]
        public void DescribesTheActiveCaseAndAnalysisType()
        {
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "L2",
                LoadingType = "個別矩形（基礎梁考慮）",
            };

            StringAssert.Contains(GroupSettlementCaseLabel.Describe(record), "L2");
            // 計算書には保存の値 (個別矩形…) ではなく画面と同じ名前で出す
            StringAssert.Contains(GroupSettlementCaseLabel.Describe(record), "荷重タイプ: 杭矩形（基礎梁考慮）");
            Assert.IsFalse(GroupSettlementCaseLabel.Describe(record).Contains("個別"), "保存の値がそのまま計算書に出ます");
            record.IsBeamAware = true;
            StringAssert.Contains(GroupSettlementCaseLabel.Describe(record), "基礎梁あり");
            StringAssert.Contains(GroupSettlementCaseLabel.Describe(null), "未計算");
        }
    }
}
