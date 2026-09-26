using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下解析が、選んだ荷重条件と違う入力で結果を作らず、荷重の無い計算を正常な結果として出さないこと。
    /// 反復沈下解析が、入力の杭番号を書き換えないこと。
    /// </summary>
    [TestClass]
    public class SettlementInputGuardTests
    {
        private static PileLayoutDataItem Pile(int no, double x)
            => new() { No = no, PileNo = no, X = x, Y = 0, Z = 0, AxialForceVL0 = 1000, SoilPileAltNo = 1, PileBodyNo = 1, GroundNo = 1 };

        private static SettlementAnalysisService.SettlementAnalysisResult Run(PileGroupSettlement pgs,
            ObservableCollection<PileLayoutDataItem> piles, double loadDia,
            ObservableCollection<VerticalBeamCaseResult>? vb = null)
        {
            pgs.SettlementSoilLayers = [new SettlementSoilLayer { Thickness = 10.0, Ek = 10_000.0, PoissonsRatio = 0.3 }];
            return new SettlementAnalysisService().PerformSettlementAnalysis(
                pgs, piles, [new SoilPile { GroupPileLoadDia = loadDia }], [], [],
                -5, 5, -5, 5, 0, 0, 1, 1, vb!);
        }

        private static VerticalBeamCaseResult Case(string name, double reaction)
            => new() { LoadCaseName = name, PileResults = [new VerticalBeamPileResult { PileNo = 1, Reaction_kN = reaction }, new VerticalBeamPileResult { PileNo = 2, Reaction_kN = reaction }] };

        /// <summary>
        /// 「個別十字（基礎梁反力）」で常時 (VL) ケースの結果が無ければ、別のケースの反力を使わずに止めること。
        /// 以前は先頭のケース (地震時など) の反力で沈下を求めていた。
        /// </summary>
        [TestMethod]
        public void BeamReactionLoadingWithoutTheLongTermCaseStops()
        {
            var piles = new ObservableCollection<PileLayoutDataItem> { Pile(1, 0), Pile(2, 3) };
            var pgs = new PileGroupSettlement { LoadingType = "個別十字（基礎梁反力）" };

            var result = Run(pgs, piles, 1.0, [Case("L1-1", 5000)]);
            Assert.IsFalse(result.Success, "VL ケースが無いのに、別のケースの反力で沈下を求めています");
            StringAssert.Contains(result.ErrorMessage, "常時 (VL) ケースの杭反力");

            Assert.IsFalse(Run(new PileGroupSettlement { LoadingType = "個別十字（基礎梁反力）" }, piles, 1.0).Success,
                "基礎梁考慮鉛直解析の結果が無いのに、荷重 0 のまま沈下 0 を結果にしています");

            var ok = Run(new PileGroupSettlement { LoadingType = "個別十字（基礎梁反力）" }, piles, 1.0, [Case("L1-1", 5000), Case("VL (常時+追加)", 800)]);
            Assert.IsTrue(ok.Success);
            Assert.IsNull(SettlementAnalysisService.FindVBLongTermCase([Case("L1-1", 1)]), "VL が無いのに別のケースを返しています");
        }

        /// <summary>矩形荷重が 1 つも作れなければ、沈下 0 を正常な結果として出さず理由を返すこと。</summary>
        [TestMethod]
        public void NoLoadsAtAllIsAnError()
        {
            var piles = new ObservableCollection<PileLayoutDataItem> { Pile(1, 0), Pile(2, 3) };

            var none = Run(new PileGroupSettlement { LoadingType = "個別十字" }, piles, loadDia: 0);
            Assert.IsFalse(none.Success, "荷重面等価径が未入力で荷重が 1 つも無いのに、成功扱いにしています");
            StringAssert.Contains(none.ErrorMessage, "荷重面等価径が未入力");
            StringAssert.Contains(none.ErrorMessage, "No.1, 2");

            var arbitrary = Run(new PileGroupSettlement { LoadingType = "任意矩形", RectLoads = [] }, piles, 1.0);
            Assert.IsFalse(arbitrary.Success);
            StringAssert.Contains(arbitrary.ErrorMessage, "矩形荷重が 1 つもありません");
        }

        [TestMethod]
        public void MissingReactionsAreReportedAsWarnings()
        {
            var piles = new ObservableCollection<PileLayoutDataItem> { Pile(1, 0), Pile(2, 3), Pile(3, 6) };
            var result = Run(new PileGroupSettlement { LoadingType = "個別十字（基礎梁反力）" }, piles, 1.0, [Case("VL", 800)]);
            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("反力の無い杭 No.3", System.StringComparison.Ordinal)),
                "反力の無い杭を荷重 0 として扱ったことを知らせていません");
        }

        /// <summary>
        /// 杭番号が重なる・0 以下なら、入力を書き換えずに名指しで止めること。
        /// 以前は反復沈下解析が組む前に入力の杭番号を 1〜N に書き換え、失敗しても変わったまま残った。
        /// </summary>
        [TestMethod]
        public void BadPileNumbersAreNamedWithoutRewritingThem()
        {
            var piles = new[] { Pile(1, 0), Pile(1, 3), Pile(0, 6) };
            string? message = VerticalBeamModelling.DescribeBadPileNumbers(piles);
            Assert.IsNotNull(message);
            StringAssert.Contains(message, "No.1 が 2 本");
            StringAssert.Contains(message, "0 以下: No.0");
            Assert.IsNull(VerticalBeamModelling.DescribeBadPileNumbers([Pile(1, 0), Pile(2, 3)]));

            var model = new InputModel { PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles) };
            Assert.ThrowsException<System.InvalidOperationException>(() => new VerticalBeamModelling(model));
            CollectionAssert.AreEqual(new[] { 1, 1, 0 }, model.PileLayoutItems.Select(p => p.No).ToArray(), "入力の杭番号が書き換えられています");

            string src = TestSource.Read("Graphics_r1", "Services", "IterativeBeamSettlementService.cs");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(src, @"pile\.No\s*=\s*seqNo"),
                "反復沈下解析が入力の杭番号を書き換えています");
        }
    }
}
