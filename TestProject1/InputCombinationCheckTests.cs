using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 欄ごとには範囲内でも、組み合わせると断面・地盤がありえない形になる入力を、解析の前に止める (2026-10-04 の横断)。
    /// どれも以前は検査が無く、ありえない値のまま計算が進んでいた。
    /// </summary>
    [TestClass]
    public class InputCombinationCheckTests
    {
        private static readonly DiagnosticTarget At = DiagnosticTarget.PileBodySegment(1, 1);

        private static PileSection SteelPipeConcrete() => new()
        {
            PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
            PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            PipeDia = 1200, PipeTs = 19, CorrosionDepth = 1, MainBarNum = 40, MainBarCenterCover = 150,
        };

        /// <summary>
        /// 鋼管コンクリート部の主筋が、コンクリートの外に出ていないこと。
        /// 以前は場所打ち RC と鉄筋コンクリート部だけを見ていた (設計例集 3.8 の読み込みで配置直径 2894 mm > コンクリート径 2162 mm)。
        /// </summary>
        [TestMethod]
        public void MainBarsOutsideTheConcrete_InASteelPipeConcreteSection_AreAnError()
        {
            var ok = SteelPipeConcrete();
            Assert.AreEqual(0, CheckInputData.CollectMainBarPlacementProblems(ok, At).Count);

            var bad = SteelPipeConcrete();
            bad.MainBarDr = bad.ConcreteOutDia + 100;   // 読み込みでかぶり厚が負になった形
            var problems = CheckInputData.CollectMainBarPlacementProblems(bad, At);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems[0].Message, "重心かぶり厚 (-50 mm)");

            var rc = new PileSection { PileBodyType = PileTypeNames.InsituRc, PileSectionType = PileTypeNames.RcSection, ConcreteOutDia = 1000, MainBarNum = 20, MainBarCenterCover = 600 };
            Assert.AreEqual(1, CheckInputData.CollectMainBarPlacementProblems(rc, At).Count, "場所打ち RC は従来どおり見る");
        }

        [TestMethod]
        public void PipeWallOfHalfTheDiameterOrMore_IsAnError()
        {
            var s = SteelPipeConcrete();
            s.PipeDia = 100; s.PipeTs = 60;   // 欄ごとには範囲内 (外径 100〜5000・肉厚 0.1〜200)
            Assert.IsTrue(CheckInputData.CollectCorrosionProblems(s, At).Any(d => d.Message.Contains("外径 (100 mm) の半分以上")));
            Assert.AreEqual(0, CheckInputData.CollectCorrosionProblems(SteelPipeConcrete(), At).Count);
        }

        [TestMethod]
        public void ImpossibleSoilProperties_AreAnError()
        {
            var at = DiagnosticTarget.GroundLayer(1, 1);
            var good = new GroundLayerInput { Density = 18, Vs = 150, Es = 5000, Cohesive = 0, PoissonsRatio = 0.3 };
            Assert.AreEqual(0, CheckInputData.CollectSoilPropertyProblems(good, at).Count);

            var unset = new GroundLayerInput { Density = 0, Vs = 0, Es = 0, PoissonsRatio = 0.3 };
            Assert.AreEqual(0, CheckInputData.CollectSoilPropertyProblems(unset, at).Count, "0 (未入力) は既存の注意に任せる");

            var bad = new GroundLayerInput { Density = -18, Vs = -1, Es = -5000, Cohesive = -1, PoissonsRatio = 0.5 };
            var problems = CheckInputData.CollectSoilPropertyProblems(bad, at);
            Assert.AreEqual(5, problems.Count, string.Join("\n", problems.Select(p => p.Message)));
        }

        /// <summary>
        /// 鋼管杭 + 鉄筋定着工法 で杭頭接合部の回転ばねを組めないとき、剛接合で続けることを知らせる。
        /// 以前はデバッグの記録にしか残らなかった。設計例集 3.5 (鋼管杭・鉄筋定着工法) で確かめる。
        /// </summary>
        [TestMethod]
        public void SteelPileHeadJointFallingBackToRigid_IsAnnounced()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (model == null) { Assert.Inconclusive("例題ロード失敗: " + error); return; }
            Assert.AreEqual(0, CheckInputData.CollectSteelPileHeadJointNotices(model).Count,
                "例題のままなら回転ばねを組める (杭頭部のコンクリート径は読み込みで杭断面から導く)");

            model.PileBodies[0].PileTop.MainBarNum1 = 0;
            var notices = CheckInputData.CollectSteelPileHeadJointNotices(model);
            Assert.AreEqual(1, notices.Count, "定着筋が無いのに知らせていません");
            StringAssert.Contains(notices[0].Message, "剛接合として計算します");
            Assert.IsTrue(CheckInputData.CollectInputWarningDiagnostics(model).Any(d => d.Message.Contains("剛接合として計算します")),
                "解析前の注意の一覧に出ていません");
        }

        /// <summary>計算例9 に入れると、実際に解析の前に止まる (検査の入口に繋がっている)。</summary>
        [TestMethod]
        public void TheNewChecks_BlockTheAnalysis()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive("例題ロード失敗: " + error); return; }
            model.GroundsInput[0].GroundLayers[0].PoissonsRatio = 0.6;
            var blockers = CheckInputData.CollectAnalysisBlockers(model);
            Assert.IsTrue(blockers.Any(d => d.Message.Contains("ポアソン比")), string.Join("\n", blockers.Select(d => d.Message)));
        }
    }
}
