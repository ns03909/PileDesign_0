using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の見直しで足した入力の検査 (以前はどこも見ておらず、誤った値のまま黙って解いていたもの)。
    /// </summary>
    [TestClass]
    public class InputChecks20261009Tests
    {
        /// <summary>矩形荷重の X1 &lt; X2・Y1 &lt; Y2 でない行を見つける (以前は面積が負で荷重 0、両方逆だと約 1/4 の荷重で解いた)。</summary>
        [TestMethod]
        public void RectLoadShape_ReversedCornersAreReported()
        {
            Assert.AreEqual(0, PileGroupSettlement.DescribeRectLoadShapeProblems([new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 1 }]).Count);
            var problems = PileGroupSettlement.DescribeRectLoadShapeProblems([
                new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 1 },
                new RectLoad { X1 = 2, X2 = 0, Y1 = 0, Y2 = 1 },
                new RectLoad { X1 = 0, X2 = 2, Y1 = 1, Y2 = 1 },
            ]);
            Assert.AreEqual(2, problems.Count);
            StringAssert.Contains(problems[0], "2 行目");
            StringAssert.Contains(problems[1], "3 行目");
        }

        private static PileGroupSettlement TwoLayers(double top = 0.0)
        {
            var pgs = new PileGroupSettlement { SettlementSoilLayers = [], RectLoads = [] };
            pgs.SoilLayersTopAltitude = top;
            pgs.SettlementSoilLayers.Add(new SettlementSoilLayer { BottomAltitude = -5, Thickness = 5 });
            pgs.SettlementSoilLayers.Add(new SettlementSoilLayer { BottomAltitude = -12, Thickness = 7 });
            return pgs;
        }

        /// <summary>荷重面が土層の範囲外 (最下層の下端ちょうども含む) なら理由を返す。</summary>
        [TestMethod]
        public void LoadingPlane_OutsideTheLayersIsReported()
        {
            var pgs = TwoLayers();
            pgs.LoadingPlaneAltitude = -2.0;
            Assert.IsNull(pgs.DescribeLoadingPlaneProblem());
            pgs.LoadingPlaneAltitude = 1.0;
            StringAssert.Contains(pgs.DescribeLoadingPlaneProblem(), "土層上端");
            pgs.LoadingPlaneAltitude = -12.0;
            StringAssert.Contains(pgs.DescribeLoadingPlaneProblem(), "荷重面より下に沈下用土層がありません",
                "最下層の下端ちょうどは、荷重面より下に土層が無いので解けない");
        }

        /// <summary>層厚と下端 Z の食い違いを見つける (貼り付け・Delete は下端 Z だけを書き換え、層厚を計算し直さない)。</summary>
        [TestMethod]
        public void LayerThickness_MismatchWithBottomIsReported()
        {
            var pgs = TwoLayers();
            Assert.AreEqual(0, pgs.DescribeLayerThicknessMismatch().Count);
            pgs.SettlementSoilLayers[1].BottomAltitude = -15;   // 層厚 7 のまま
            var problems = pgs.DescribeLayerThicknessMismatch();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems[0], "2 層目");
        }

        /// <summary>基礎梁の材料・断面が正でない値なら、剛性行列の前に止める。</summary>
        [TestMethod]
        public void FoundationBeamProperties_NonPositiveValuesBlockTheAnalysis()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            input.FoundationBeamInput ??= new FoundationBeamInput();
            input.FoundationBeamInput.Beams ??= [];
            input.FoundationBeamInput.Beams.Clear();
            input.FoundationBeamInput.Beams.Add(new FoundationBeam { MaterialNo = 1, SectionNo = 1, Width = 0.5, Height = 0.8, YoungModulus = 2.5e7 });
            input.FoundationBeamInput.EnsureDefaultMaterialAndSection();
            Assert.AreEqual(0, CheckInputData.CollectFoundationBeamPropertyProblems(input).Count, "(前提) 既定の材料・断面は通る");

            input.FoundationBeamInput.Materials[0].YoungModulus = 0;
            var problems = CheckInputData.CollectFoundationBeamPropertyProblems(input);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems[0].Message, "ヤング係数");
        }

        /// <summary>杭先端径が 0 以下なら解析の前に止める (先端の支持力・沈下用の面積が 0 になり、先端ばねが無いまま解いていた)。</summary>
        [TestMethod]
        public void PileToeDiameter_ZeroBlocksTheAnalysis()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            input.PileBodies[0].PileToeDia = 0;
            Assert.IsTrue(CheckInputData.CollectPileBodyGeometryProblems(input).Any(d => d.Message.Contains("杭先端径")));
        }

        /// <summary>半剛接合の杭頭で、パイルキャップの Ec が小さすぎれば止める (γ を 23 のつもりで 2.3 と入れたなど)。</summary>
        [TestMethod]
        public void PileCapYoungsModulus_TooSmallBlocksTheAnalysis()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var pb = input.PileBodies[0];
            pb.PileTopType = "キャプテンパイル工法";
            Assert.IsFalse(CheckInputData.CollectPileBodyGeometryProblems(input).Any(d => d.Message.Contains("パイルキャップ")), "(前提) 既定の γ=23 は通る");
            pb.PileTop.PileCapGamma = 2.3;
            Assert.IsTrue(CheckInputData.CollectPileBodyGeometryProblems(input).Any(d => d.Message.Contains("パイルキャップ")));
        }
    }
}
