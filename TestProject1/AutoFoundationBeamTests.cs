using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 基礎梁の自動生成 (2026-09-27 のレビュー)。
    /// 2. 追加する梁が 0 本なら解析結果を消さない (以前は結果を消してから探した)
    /// 1. 「選択中の杭」と案内したとおり、選んだ杭だけを連結する (以前は選択を見ずに全ての杭)
    /// </summary>
    [TestClass]
    public class AutoFoundationBeamTests
    {
        /// <summary>X 方向に 3 本 (0, 5, 10 m) 並んだ杭。</summary>
        private static List<PileLayoutDataItem> Row()
            => [.. new[] { 0.0, 5.0, 10.0 }.Select((x, i) => new PileLayoutDataItem { No = i + 1, PileNo = i + 1, X = x, Y = 0 })];

        private static HashSet<(Guid, Guid)> Pairs(IEnumerable<FoundationBeam> beams)
            => beams.Select(b => (b.NodeI_Id, b.NodeJ_Id)).ToHashSet();

        [TestMethod]
        public void AllPilesAreConnectedToTheirNeighbours()
        {
            var piles = Row();
            var beams = MainWindowViewModel.FindAutoFoundationBeams(piles, piles, []);
            Assert.AreEqual(2, beams.Count);
            CollectionAssert.AreEquivalent(new[] { (piles[0].UniqueId, piles[1].UniqueId), (piles[1].UniqueId, piles[2].UniqueId) }, Pairs(beams).ToList());
        }

        [TestMethod]
        public void OnlySelectedPilesAreConnectedAndUnselectedOnesAreNotJumpedOver()
        {
            var piles = Row();
            // 両端だけを選ぶ: 間に選んでいない杭があるので、両端を直接結ぶ梁は作らない
            Assert.AreEqual(0, MainWindowViewModel.FindAutoFoundationBeams(piles, [piles[0], piles[2]], []).Count,
                "間の選んでいない杭を飛び越える梁を作っています");

            var beams = MainWindowViewModel.FindAutoFoundationBeams(piles, [piles[0], piles[1]], []);
            Assert.AreEqual(1, beams.Count, "選んでいない杭まで連結しています");
            Assert.AreEqual((piles[0].UniqueId, piles[1].UniqueId), (beams[0].NodeI_Id, beams[0].NodeJ_Id));
        }

        [TestMethod]
        public void AlreadyConnectedPilesGiveNoCandidates()
        {
            var piles = Row();
            var existing = MainWindowViewModel.FindAutoFoundationBeams(piles, piles, []);
            Assert.AreEqual(0, MainWindowViewModel.FindAutoFoundationBeams(piles, piles, existing).Count,
                "連結済みの杭の間に梁を重ねています");
        }

        /// <summary>追加する梁を先に求め、0 本なら解析結果を消す前に終えること。確認文は対象の範囲を書くこと。</summary>
        [TestMethod]
        public void NoBeamsToAddMeansTheResultsAreKept()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ModelEditing.cs"),
                "private void OnAutoGenerateFoundationBeams()");
            int find = body.IndexOf("FindAutoFoundationBeams(", StringComparison.Ordinal);
            int empty = body.IndexOf("if (newBeams.Count == 0)", StringComparison.Ordinal);
            int reset = body.IndexOf("CheckAndResetAnalysisResults()", StringComparison.Ordinal);
            int undo = body.IndexOf("TrySaveUndoSnapshotSafely()", StringComparison.Ordinal);
            Assert.IsTrue(find >= 0 && empty > find, "追加する梁を先に求めていません");
            Assert.IsTrue(reset > empty, "追加する梁が無いのに解析結果を消しています");
            Assert.IsTrue(undo > empty, "追加する梁が無いのに元に戻すの履歴を積んでいます");
            StringAssert.Contains(body, "IsSelected", "選択を見ていません");
            StringAssert.Contains(body, "全ての杭配置", "選んでいないとき全ての杭が対象であることを確認文に書いていません");
        }
    }
}
