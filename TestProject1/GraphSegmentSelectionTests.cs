using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// グラフの「杭区間」の選び直し。
    ///
    /// NMINT / QNINT は 1 区間ぶんの断面から限界曲線を作るため、区間が「すべて」だと
    /// 描くものが決まらず、軸だけ描いて終わる。
    /// ところが杭体を選び直すたびに区間が無条件で「すべて」へ戻されていたため、
    /// <b>杭体を変えると NMINT が空になり、2 回目以降は何度変えても空のまま</b>
    /// （＝「再描画されない」ように見える）状態だった。
    ///
    /// 「すべて」を扱えるグラフ（p-y / M-φ / EI-φ）は従来どおりでよいので、
    /// 「具体的な区間が要るか」で場合分けする。
    /// </summary>
    [TestClass]
    public class GraphSegmentSelectionTests
    {
        /// <param name="extraPileBody">
        /// 例題は杭体が 1 種類しかないことがある。杭体の選び直しを試すには 2 種類要るので、
        /// 区間数の違う杭体を 1 つ足せるようにしておく。
        /// </param>
        private static GraphViewModel? Build(bool extraPileBody = false)
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (input == null) return null;

            if (extraPileBody && input.PileBodies is { Count: > 0 })
            {
                var copy = input.PileBodies[0].DeepCopy();
                copy.PileBodyRef = "(PB-TEST)";
                // 区間数を 1 本にして「区間が減る杭体へ切り替える」場合も試せるようにする
                while (copy.PileBodySegments.Count > 1)
                    copy.PileBodySegments.RemoveAt(copy.PileBodySegments.Count - 1);
                input.PileBodies.Add(copy);
            }

            var mainVm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(mainVm);
            return new GraphViewModel(mainVm);
        }

        private static ObservableCollection<string> Options(int segments)
        {
            var opts = new ObservableCollection<string> { UiText.All };
            foreach (int i in Enumerable.Range(1, segments)) opts.Add(i.ToString());
            return opts;
        }

        // ── どのグラフが具体的な区間を要るか ────────────────

        [TestMethod]
        public void OnlyNmintAndQnintNeedAConcreteSegment()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            foreach (string needs in new[] { "NMINT", "QNINT" })
            {
                vm.SelectedGraphOption = needs;
                Assert.IsTrue(vm.RequiresConcretePileSegment, $"{needs} が対象外になっている");
            }

            foreach (string ok in new[] { "水平地盤反力度p-y", "杭体M-φ", "杭体EI-φ", "定着部NMINT" })
            {
                vm.SelectedGraphOption = ok;
                Assert.IsFalse(vm.RequiresConcretePileSegment,
                    $"{ok} は「すべて」を扱えるので対象外のはず");
            }
        }

        /// <summary>
        /// 「定着部NMINT」は杭区間を使わないので、前方一致で巻き込まないこと。
        /// </summary>
        [TestMethod]
        public void AnchorageNmintIsNotCaughtByThePrefix()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "定着部NMINT";

            Assert.IsFalse(vm.RequiresConcretePileSegment);
        }

        // ── 選択肢そのもの ─────────────────────────────────

        /// <summary>
        /// NMINT / QNINT の一覧には「すべて」を入れないこと。
        /// 入れると選べてしまい、選んだ瞬間に軸だけの空グラフになる。
        /// </summary>
        [TestMethod]
        public void ForNmint_AllIsNotEvenOffered()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            foreach (string needs in new[] { "NMINT", "QNINT" })
            {
                vm.SelectedGraphOption = needs;
                var opts = vm.BuildPileSegmentOptions(3);

                CollectionAssert.DoesNotContain(opts, UiText.All, $"{needs} に「すべて」が出ている");
                CollectionAssert.AreEqual(new[] { "1", "2", "3" }, opts);
            }
        }

        /// <summary>「すべて」を描けるグラフでは従来どおり先頭に置くこと。</summary>
        [TestMethod]
        public void ForOtherGraphs_AllIsStillOffered()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "水平地盤反力度p-y";

            CollectionAssert.AreEqual(new[] { UiText.All, "1", "2" }, vm.BuildPileSegmentOptions(2));
        }

        /// <summary>
        /// p-y → NMINT と切替えたとき、区間数が同じでも一覧から「すべて」が消えること。
        /// 数だけ見て抜けると「すべて」が残る。
        /// </summary>
        [TestMethod]
        public void SwitchingToNmintDropsAllFromTheList()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "水平地盤反力度p-y";
            vm.SelectedGraphOption = "NMINT";

            CollectionAssert.DoesNotContain(vm.PileSegmentOptions, UiText.All,
                "NMINT へ切替えても一覧に「すべて」が残っている");
            Assert.AreNotEqual(UiText.All, vm.SelectedPileSegmentOption);
        }

        // ── 選び直しの規則 ─────────────────────────────────

        /// <summary>NMINT では「すべて」を選ばず、先頭の区間へ寄せること。</summary>
        [TestMethod]
        public void ForNmint_AllIsReplacedByTheFirstSegment()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "NMINT";
            vm.SelectedPileSegmentOption = UiText.All;

            Assert.AreEqual("1", vm.ResolvePileSegmentOption(Options(3)));
        }

        /// <summary>いま選んでいる区間が新しい選択肢にもあれば、維持すること。</summary>
        [TestMethod]
        public void ForNmint_AValidSegmentIsKept()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "NMINT";
            vm.SelectedPileSegmentOption = "2";

            Assert.AreEqual("2", vm.ResolvePileSegmentOption(Options(3)));
        }

        /// <summary>
        /// 区間数の少ない杭体へ切替えたときは、範囲外にならず先頭へ寄せること。
        /// </summary>
        [TestMethod]
        public void ForNmint_AnOutOfRangeSegmentFallsBackToTheFirst()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "NMINT";
            vm.SelectedPileSegmentOption = "5";

            Assert.AreEqual("1", vm.ResolvePileSegmentOption(Options(2)));
        }

        /// <summary>
        /// 「すべて」を扱えるグラフでは、従来どおり「すべて」のままでよいこと。
        /// </summary>
        [TestMethod]
        public void ForPy_AllIsLeftAlone()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "水平地盤反力度p-y";
            vm.SelectedPileSegmentOption = UiText.All;

            Assert.AreEqual(UiText.All, vm.ResolvePileSegmentOption(Options(3)));
        }

        /// <summary>選択肢が空なら「すべて」を返し、落ちないこと。</summary>
        [TestMethod]
        public void EmptyOptions_FallBackToAll()
        {
            var vm = Build();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.SelectedGraphOption = "NMINT";

            Assert.AreEqual(UiText.All, vm.ResolvePileSegmentOption([]));
            Assert.AreEqual(UiText.All, vm.ResolvePileSegmentOption(null!));
        }

        // ── 杭体を選び直したとき ───────────────────────────

        /// <summary>
        /// NMINT を見ている状態で杭体を選び直しても、区間が「すべて」に戻らないこと。
        /// これが戻ると NMINT は軸だけ描いて終わる。
        /// </summary>
        [TestMethod]
        public void SwitchingPileBodyKeepsAConcreteSegmentForNmint()
        {
            var vm = Build(extraPileBody: true);
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            Assert.IsTrue(vm.PileBodyRefOptions?.Count >= 2, "杭体が 2 種類そろっていない");

            vm.SelectedGraphOption = "NMINT";
            vm.SelectedPileSegmentOption = "1";

            string other = vm.PileBodyRefOptions.First(r => r != vm.SelectedPileBodyRef);
            vm.SelectedPileBodyRef = other;

            Assert.AreNotEqual(UiText.All, vm.SelectedPileSegmentOption,
                "杭体を選び直したら区間が「すべて」に戻ってしまった (NMINT が空になる)");
            Assert.IsTrue(vm.SelectedPileSegmentNo >= 1,
                $"区間番号が {vm.SelectedPileSegmentNo} になっている");
        }

        /// <summary>
        /// 「すべて」を扱えるグラフでは、杭体を選び直すと従来どおり「すべて」へ戻ること。
        /// </summary>
        [TestMethod]
        public void SwitchingPileBodyStillResetsToAllForOtherGraphs()
        {
            var vm = Build(extraPileBody: true);
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            Assert.IsTrue(vm.PileBodyRefOptions?.Count >= 2, "杭体が 2 種類そろっていない");

            vm.SelectedGraphOption = "杭体M-φ";
            vm.SelectedPileSegmentOption = "1";

            string other = vm.PileBodyRefOptions.First(r => r != vm.SelectedPileBodyRef);
            vm.SelectedPileBodyRef = other;

            Assert.AreEqual(UiText.All, vm.SelectedPileSegmentOption);
        }
    }
}
