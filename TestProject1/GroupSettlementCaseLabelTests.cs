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
            StringAssert.Contains(GroupSettlementCaseLabel.Describe(record), "個別矩形（基礎梁考慮）");
            StringAssert.Contains(GroupSettlementCaseLabel.Describe(null), "未計算");
        }
    }
}
