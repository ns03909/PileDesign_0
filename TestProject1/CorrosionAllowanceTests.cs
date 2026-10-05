using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 鋼管の肉厚が腐食代以下の断面を、解析の前に止める。
    ///
    /// 腐食を考えた肉厚 (t − 腐食代) が 0 以下になり、腐食を考えた鋼管の断面積が負になる。
    /// コンクリートを持つ断面は EA・EI がコンクリートの分で正のまま残るので、剛性の検査では捕まらず、
    /// 杭断面ウィンドウで管厚 0.1 mm・腐食代 1.0 mm にすると As' = −3,390 mm² のまま計算が進んでいた (実機で確認)。
    /// </summary>
    [TestClass]
    public class CorrosionAllowanceTests
    {
        private static PileSection SteelPipeConcrete(double ts, double corrosion) => new()
        {
            PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
            PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            PipeDia = 1200,
            PipeTs = ts,
            CorrosionDepth = corrosion,
        };

        private static readonly DiagnosticTarget At = DiagnosticTarget.PileBodySegment(1, 1);

        [TestMethod]
        public void WallNotThickerThanTheAllowance_IsAnError()
        {
            var problems = CheckInputData.CollectCorrosionProblems(SteelPipeConcrete(0.1, 1.0), At);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems[0].Message, "鋼管の肉厚 (0.1 mm) が腐食代 (1 mm) 以下です");

            Assert.AreEqual(1, CheckInputData.CollectCorrosionProblems(SteelPipeConcrete(1.0, 1.0), At).Count, "等しいときも腐食後の肉厚は 0");
            Assert.AreEqual(0, CheckInputData.CollectCorrosionProblems(SteelPipeConcrete(16, 1.0), At).Count);
        }

        /// <summary>鋼管を持たない断面 (場所打ち RC・場所打ち鋼管コンクリート杭の鉄筋コンクリート部) は対象外。</summary>
        [TestMethod]
        public void SectionsWithoutAPipe_AreNotChecked()
        {
            var rc = new PileSection { PileBodyType = PileTypeNames.InsituRc, PileSectionType = PileTypeNames.RcSection, PipeTs = 0, CorrosionDepth = 1 };
            var sprcRc = new PileSection { PileBodyType = PileTypeNames.InsituSteelPipeConcrete, PileSectionType = PileTypeNames.RcSection, PipeTs = 0, CorrosionDepth = 1 };
            Assert.AreEqual(0, CheckInputData.CollectCorrosionProblems(rc, At).Count);
            Assert.AreEqual(0, CheckInputData.CollectCorrosionProblems(sprcRc, At).Count);

            var steel = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = PileTypeNames.SteelPipeSection, PipeDia = 600, PipeTs = 1, CorrosionDepth = 1 };
            Assert.AreEqual(1, CheckInputData.CollectCorrosionProblems(steel, At).Count, "鋼管杭も対象");
        }

        /// <summary>杭断面ウィンドウの諸元表でも、腐食代の行に赤字で出る。1 mm 未満の肉厚が「0」に見えないこと。</summary>
        [TestMethod]
        public void TheSpecTable_FlagsTheAllowanceAndShowsThinWalls()
        {
            var section = SteelPipeConcrete(0.1, 1.0);
            section.SetSpecs();
            var corrosion = section.SelectedPileSectionSpecification.Single(s => s.Item == "腐食代");
            StringAssert.Contains(corrosion.Note, "肉厚 Ts 以上");
            Assert.AreEqual("0.1", section.SelectedPileSectionSpecification.Single(s => s.Item == "鋼管厚").Value);

            var ok = SteelPipeConcrete(16, 1.0);
            ok.SetSpecs();
            Assert.AreEqual("", ok.SelectedPileSectionSpecification.Single(s => s.Item == "腐食代").Note);
        }

        /// <summary>解析の前の検査 (入力の診断と同じ入口) で、実際に止まる。</summary>
        [TestMethod]
        public void TheAnalysis_IsBlocked()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive("例題ロード失敗: " + error); return; }
            var s = model.PileBodies[0].PileBodySegments[0].PileSection;
            s.PileBodyType = PileTypeNames.InsituSteelPipeConcrete;
            s.PileSectionType = PileTypeNames.SteelPipeConcreteSection;
            s.PipeDia = 1200;
            s.CorrosionDepth = 1.0;
            s.PipeTs = 0.5;
            var blockers = CheckInputData.CollectAnalysisBlockers(model);
            Assert.IsTrue(blockers.Any(d => d.Message.Contains("が腐食代 (1 mm) 以下です")),
                "肉厚が腐食代以下なのに解析の前に止まりません:\n" + string.Join("\n", blockers.Select(d => d.Message)));
        }
    }
}
