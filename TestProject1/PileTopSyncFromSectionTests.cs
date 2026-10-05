using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 杭頭部の値のうち、最上段の杭断面から導くもの (コンクリート径・杭体側の主筋) は、杭頭部ウィンドウを開かなくても入ること。
    ///
    /// 以前は杭頭部ウィンドウを開いたとき (ViewModel の生成時) にだけ導いていたので、
    ///   ・一度も開いていない杭体は値が 0 のまま解析された (鋼管杭 + 鉄筋定着工法は回転ばねを組めず剛接合。設計例集 3.5)
    ///   ・杭断面の径を変えても、開き直すまで杭頭部は古い径のまま解析された
    /// いまは解析の入口・読み込みの仕上げ・例題の読み込みでも同じ導き方 (PileTop.SyncFromTopSection) を通す。
    /// </summary>
    [TestClass]
    public class PileTopSyncFromSectionTests
    {
        [TestMethod]
        public void TheExample_GetsThePileHeadDiameterWithoutOpeningTheWindow()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (model == null) { Assert.Inconclusive("例題ロード失敗: " + error); return; }
            var pb = model.PileBodies[0];
            double expected = System.Math.Round(pb.PileTop.SteelPipePileLowerRcDiameter(pb.PileBodySegments[0].PileSection.PileDiameter), 3);
            Assert.IsTrue(expected > 0);
            Assert.AreEqual(expected, pb.PileTop.ConcreteOutDia, "例題を読み込んだだけでは杭頭部のコンクリート径が入っていません");
        }

        /// <summary>杭断面の径を変えたあと、解析の入口で杭頭部が新しい径になる。</summary>
        [TestMethod]
        public void ChangingTheSection_IsFollowedAtTheAnalysisEntry()
        {
            var pb = new PileBodyInput { PileBodyType = PileTypeNames.InsituSteelPipeConcrete };
            var section = pb.PileBodySegments[0].PileSection;
            section.PileSectionType = PileTypeNames.SteelPipeConcreteSection;
            section.PipeDia = 1200; section.PipeTs = 19; section.MainBarNum = 30; section.MainBarCenterCover = 150;
            pb.SyncPileTopFromTopSection();
            double before = pb.PileTop.ConcreteOutDia;

            section.PipeDia = 1500;
            Assert.AreEqual(before, pb.PileTop.ConcreteOutDia, "前提: 杭断面を変えただけでは杭頭部は変わらない");
            pb.SyncPileTopFromTopSection();
            Assert.AreEqual(System.Math.Round(section.PileDiameter + 200, 3), pb.PileTop.ConcreteOutDia);
            Assert.AreEqual(30, pb.PileTop.MainBarNum2, "杭体側の主筋が最上段の主筋になっていません");

            // 入口に繋がっていること
            string check = TestSource.Read("Graphics_r1", "Services", "CheckInputData.cs");
            string gate = TestSource.MethodBody(check, "public static bool ValidateForAnalysis(InputModel inputModel");
            StringAssert.Contains(gate, "SyncPileTopsFromSections()", "解析の入口で杭頭部を導き直していません");
            string fileIo = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            StringAssert.Contains(TestSource.MethodBody(fileIo, "private void ApplyPostLoadProtocol("), "SyncPileTopsFromSections()",
                "読み込みの仕上げで杭頭部を導き直していません");
        }
    }
}
