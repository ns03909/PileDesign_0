using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 のプログラム全体の見直しで、利用者の了解を得て直した「解析・検定の数値が変わる」不具合の再発防止。
    /// </summary>
    [TestClass]
    public class WholeProgramAuditNumeric20261009Tests
    {
        private static string Map(Node n, int dof)
            => string.Join(" ", (n.ResolvedDofMap?[dof] ?? []).OrderBy(t => t.Eq).Select(t => $"{t.Eq}:{t.Coeff:0.######}"));

        /// <summary>
        /// 1. 杭頭節点の鉛直変位は、同じ位置のキャップ節点と同じ写像 (作用点の Uz + Rx·ΔY − Ry·ΔX)。
        /// 以前は Uz の master をキャップ節点にして腕の項をもう一度足し、転倒の腕を 2 倍に数えていた。
        /// 2. 作用点を動かしたら、作用点を直接 master に持つ杭頭節点の腕も作り直す (キャップ節点と同じ写像のまま)。
        /// </summary>
        [TestMethod]
        public void PileHeadFollowsTheCapNode_EvenAfterTheActionPointMoves()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題を組めません"); return; }
            var model = built.Value.vm.CurrentModel!.DeepCopy();

            void AssertHeadsFollowCaps(string when)
            {
                int compared = 0;
                foreach (var cap in model.Nodes.Where(n => n.Name.StartsWith("CapNode-")))
                {
                    var head = model.Nodes.FirstOrDefault(n => n.Name == $"杭節点-{cap.Name.Substring(8)}-0");
                    if (head == null || head.ResolvedDofMap == null || cap.ResolvedDofMap == null) continue;
                    if ((cap.Coord - head.Coord).Length > 1e-9) continue;
                    foreach (int dof in new[] { 0, 1, 2, 5 })
                        Assert.AreEqual(Map(cap, dof), Map(head, dof), $"{when}: {head.Name} の自由度 {dof} がキャップ節点と別の動きです");
                    compared++;
                }
                TestSource.AssertScanned(compared, 2, $"{when}: キャップ節点と杭頭節点の組");
            }

            AssertHeadsFollowCaps("作ったとき");
            var ap = model.Nodes.First(n => n.Name == "ActionPoint");
            Assert.IsTrue(model.RelocateActionPoint(ap.Coord.X + 0.5, ap.Coord.Y + 0.5, ap.Coord.Z));
            AssertHeadsFollowCaps("作用点を動かしたあと");
        }

        /// <summary>
        /// 3. 解析の M-φ は、要素が属する杭自身の土層-杭セットの断面と、その杭の軸力で作る
        /// (要素の剛性・検定と同じ)。以前は杭体番号ごとに最初のセット・最初の杭で代表していた。
        /// </summary>
        [TestMethod]
        public void MPhi_IsBuiltFromThePilesOwnSoilPileSet()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.MphiSetup.cs");
            foreach (var sig in new[] { "private void SetupMPhiFromPileSectionForLoadCase(", "private void SetupMPhiByCurrentAxialForMiddleBeam(" })
            {
                string body = TestSource.MethodBody(src, sig);
                StringAssert.Contains(body, ".SoilPileAt(InputModel)", $"{sig} が杭自身の土層-杭セットを引いていません");
            }
            StringAssert.Contains(TestSource.MethodBody(src, "private void SetupMPhiFromPileSectionForLoadCase("), "pileByBeam.TryGetValue(beam, out var ownerPile)",
                "ケース開始時の M-φ が、要素の属する杭の軸力を使っていません");
        }

        /// <summary>4. 根入部 (土圧合力ばね) の区間が 1 つでも、根入部の地盤節点に地盤変位を与える。</summary>
        [TestMethod]
        public void GroundDisplacement_ReachesASingleEmbedmentSegment()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            Assert.IsFalse(src.Contains("DoatsuGoryokuBane.Items.Count > 1", System.StringComparison.Ordinal),
                "根入れが 1 区間だと地盤変位を与えない条件が残っています");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(src, @"DoatsuGoryokuBane\.Items\.Count > 0").Count);
        }

        /// <summary>7. 沈下による杭頭変形角 (常時・使用限界) は、常時 (VL) の群杭沈下の記録だけで判定する。</summary>
        [TestMethod]
        public void DeformationAngle_UsesOnlyLongTermGroupRecords()
        {
            var soilPiles = new ObservableCollection<SoilPile>();
            var piles = new ObservableCollection<PileLayoutDataItem>();
            for (int i = 0; i < 3; i++)
            {
                var sp = new SoilPile { LoadDisplacements = [new VerticalLoadTransferMethod.LoadDisplacement()] };
                soilPiles.Add(sp);
                piles.Add(new PileLayoutDataItem { No = i + 1, PileNo = i + 1, X = 5.0 * i, Y = 0, SoilPileAltNo = i + 1 });
            }
            var input = new InputModel
            {
                PileLayoutItems = piles,
                ElementDivision = new ElementDivision { SoilPiles = soilPiles },
                FundamentalInput = new FundamentalInput { SettlementDesignIncludesGroup = true },
                PileGroupSettlement = new PileGroupSettlement(),
            };
            input.PileGroupSettlement.CaseRecords.Add(new GroupSettlementCaseRecord
            {
                LoadCaseName = "杭軸力 VL", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true, IsConverged = true,
                PileSettlements_mm = new() { [1] = 1, [2] = 2, [3] = 3 },
            });
            input.PileGroupSettlement.CaseRecords.Add(new GroupSettlementCaseRecord
            {
                LoadCaseName = "L1-1: 杭軸力", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true, IsConverged = true,
                PileSettlements_mm = new() { [1] = 1, [2] = 40, [3] = 3 },
            });

            var items = SettlementDeformationAngleEvaluator.Evaluate(input);
            Assert.AreEqual(1, items.Count, "地震時の群杭沈下まで常時・使用限界で判定しています");
            Assert.AreEqual("杭軸力 VL", items[0].LoadCaseName);
        }
    }
}
