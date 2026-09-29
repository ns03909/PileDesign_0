using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;

namespace TestProject1
{
    [TestClass]
    public class PileSettlementTableValuesTests
    {
        [TestMethod]
        public void CompleteResultsAreConvertedToMillimetres()
        {
            var values = PileSettlementTableValues.Build(true, 0.012, true, 3.0);
            Assert.AreEqual(12.0, values.SingleMm);
            Assert.AreEqual(3.0, values.GroupMm);
            Assert.AreEqual(15.0, values.TotalMm);
        }

        [TestMethod]
        public void MissingResultsAreNotShownAsZero()
        {
            var singleOnly = PileSettlementTableValues.Build(true, 0.012, false, 0);
            Assert.AreEqual(12.0, singleOnly.SingleMm);
            Assert.IsNull(singleOnly.GroupMm);
            Assert.IsNull(singleOnly.TotalMm);

            var none = PileSettlementTableValues.Build(false, 0, false, 0);
            Assert.IsNull(none.SingleMm);
            Assert.IsNull(none.GroupMm);
            Assert.IsNull(none.TotalMm);

            var zero = PileSettlementTableValues.Build(true, 0, true, 0);
            Assert.AreEqual(0.0, zero.TotalMm);
        }
    }
}
