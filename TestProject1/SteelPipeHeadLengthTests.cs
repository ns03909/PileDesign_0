using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 鋼管杭の杭頭部 (コンクリート充填鋼管部) の長さは杭径 D と仮定している (入力できない)。
    /// 実際の充填長と違いうるので、採用した長さを杭断面の諸元に出す (外部レビュー #10 2026-11-20)。
    /// 計算書の「計算条件・仮定」にも書く (WordDocument.NoteSteelPipeColumnBuckling)。
    /// </summary>
    [TestClass]
    public class SteelPipeHeadLengthTests
    {
        [TestMethod]
        public void TheFilledHeadSectionShowsTheAssumedLength()
        {
            // 鋼管杭の杭径は鋼管外径から腐食代を引いた有効径。解析が杭頭部の境に使うのもこの値 (InputModel の分割点)
            var head = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.CftSection, PipeDia = 800, PipeTs = 12 };
            Assert.IsTrue(head.PileDiameter > 700, $"(前提) 杭径が鋼管外径から決まる: {head.PileDiameter}");
            head.SetSpecs();
            var row = head.SelectedPileSectionSpecification.SingleOrDefault(s => s.Item == "杭頭部の長さ (仮定)");
            Assert.IsNotNull(row, "杭頭部の諸元に採用した長さがありません");
            Assert.AreEqual($"{head.PileDiameter:N0}", row.Value, "解析で使う杭径と違う長さを出しています");
            StringAssert.Contains(row.Note, "杭径 D");

            var middle = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.SteelPipeSection, PipeDia = 800, PipeTs = 12 };
            middle.SetSpecs();
            Assert.IsFalse(middle.SelectedPileSectionSpecification.Any(s => s.Item == "杭頭部の長さ (仮定)"), "鋼管部にも出しています");
        }

        [TestMethod]
        public void TheReportStatesTheAssumedLength()
        {
            string source = TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs");
            StringAssert.Contains(source, "杭頭部 (コンクリート充填鋼管部) の長さは、杭頭から杭径 D と仮定した");
            StringAssert.Contains(source, "引張軸力のときの曲げ耐力は、「基礎部材の強度と変形性能」の式と異なる");
        }
    }
}
