using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 土層-杭セットの作り直し (<see cref="InputModel.GenerateSoilPiles"/>) の後始末。
    ///
    /// 2026-09-26 のレビューで 2 件と、調べる途中で見つけた 1 件:
    /// - 検索キャッシュを無効化していなかったので、画面側から直接呼ぶ経路のあと LookupSoilPile が古い組を返し得た
    /// - 対応先が見つからない杭の SoilPileAltNo を前の値のまま残し、並びの変わった一覧の別の組を指し得た
    ///   (新しい杭も最初から 1 を持っていた)
    /// - 差し替えた一覧の変更を購読していなかった
    /// </summary>
    [TestClass]
    public class SoilPileRegenerationTests
    {
        private static InputModel Example()
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (inputModel == null) Assert.Inconclusive(error);
            return inputModel!;
        }

        [TestMethod]
        public void LookupReturnsTheRegeneratedSoilPile()
        {
            var input = Example();
            var pile = input.PileLayoutItems[0];
            var before = input.LookupSoilPile(pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ);   // キャッシュを作る
            Assert.IsNotNull(before);

            input.GenerateSoilPiles();

            var after = input.LookupSoilPile(pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ);
            Assert.IsNotNull(after);
            Assert.AreNotSame(before, after, "作り直したあとも、作り直す前の土層-杭セットを返しています (キャッシュが古い)");
            Assert.IsTrue(input.ElementDivision.SoilPiles.Contains(after), "返した組が今の一覧にありません");
            Assert.AreSame(after, pile.SoilPileAt(input));
        }

        [TestMethod]
        public void APileWithoutAMatchGetsZeroNotItsOldNumber()
        {
            var input = Example();
            var pile = input.PileLayoutItems[0];
            Assert.IsTrue(pile.SoilPileAltNo > 0, "(前提) 対応が付いていません");

            pile.GroundNo = 99;   // 無い地盤
            input.GenerateSoilPiles();

            Assert.AreEqual(0, pile.SoilPileAltNo, "対応先が無い杭が前の番号を持ったままです");
            Assert.IsNull(pile.SoilPileAt(input));
            Assert.IsTrue(input.PileLayoutItems.Skip(1).All(p => p.SoilPileAt(input) != null), "ほかの杭の対応まで外れています");
        }

        [TestMethod]
        public void ANewPileStartsWithoutAMatch()
        {
            Assert.AreEqual(0, new PileLayoutDataItem().SoilPileAltNo, "新しい杭が対応付けの前から 1 番目の組を指しています");
            Assert.IsNull(new PileLayoutDataItem().SoilPileAt(new InputModel()));
        }

        /// <summary>対応の無い杭のまま解析へ進まず、杭を名指しして止める (水平解析は番号で配列を引く)。</summary>
        [TestMethod]
        public void TheAnalysisCheckStopsAPileWithoutASoilPile()
        {
            var input = Example();
            Assert.AreEqual("", CheckInputData.CheckSoilPile(input, ""), "(前提) 例題で入力の問題が出ています");

            var pile = input.PileLayoutItems[2];
            pile.SoilPileAltNo = 0;
            string message = CheckInputData.CheckSoilPile(input, "");
            StringAssert.Contains(message, $"杭 No.{pile.No}");
            StringAssert.Contains(message, "土層-杭セットがまだ作られていません");
        }

        [TestMethod]
        public void TheReplacedCollectionIsWatched()
        {
            var input = Example();
            input.GenerateSoilPiles();
            var pile = input.PileLayoutItems[0];
            int raised = 0;
            pile.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PileLayoutDataItem.SoilPile)) raised++; };

            input.ElementDivision.SoilPiles.Add(new SoilPile());
            Assert.IsTrue(raised > 0, "作り直したあとの一覧の変更が杭へ伝わっていません");
        }
    }
}
