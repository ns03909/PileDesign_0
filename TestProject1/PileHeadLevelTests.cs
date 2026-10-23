using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭頭レベルが複数あるときの、杭体ウィンドウ・地盤の標高 (2026-10-23 の指摘「地盤と杭頭のレベル関係が適切でない」)。
    ///
    /// <list type="bullet">
    /// <item>杭体ウィンドウの支持力は、画面用の仮の土層-杭セットで求めていた。仮のセットは杭区間の境だけで区切り、
    /// 地層の境で区切らなかったので、区間の土層がその区間の下端の 1 層で決まった (例題10 で Rfu が解析の 10,074 kN に対し 29,158 kN)。</item>
    /// <item>杭体ウィンドウの「杭頭Z」は開くたびに 0 から始まり、杭配置の杭頭と結び付いていなかった (地盤を重ねた姿図で杭頭が地表にあるように描いた)。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class PileHeadLevelTests
    {
        /// <summary>例題10 の杭の半分を 1.5 m 下げ、杭頭レベルを 2 つ (−2.0・−3.5) にする。</summary>
        internal static void TwoPileHeadLevels(InputModel model)
        {
            var piles = model.PileLayoutItems.ToList();
            for (int i = 0; i < piles.Count; i += 2) piles[i].Z -= 1.5;
            model.GenerateSoilPiles();
        }

        private static (MainWindowViewModel vm, InputModel input) BuildTwoLevels()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) throw new AssertInconclusiveException("例題ファイルなし: " + err);
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            TwoPileHeadLevels(input);
            return (vm, input);
        }

        private static void OnSta(Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try { action(); }
                finally { MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) throw new AssertInconclusiveException("STA スレッドが時間内に終わりませんでした");
            if (captured is AssertInconclusiveException inconclusive) throw inconclusive;
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }

        [TestMethod]
        public void ThePileBodyWindow_ComputesTheSameSetAsTheAnalysis_AtEachPileHeadLevel()
        {
            OnSta(() =>
            {
                var (vm, input) = BuildTwoLevels();
                var sets = input.ElementDivision.SoilPiles.ToList();
                Assert.AreEqual(2, sets.Count, "杭頭レベル 2 つの土層-杭セットになっていません");

                var window = new PileBodyViewModel(vm);
                foreach (var sp in sets)
                {
                    var choice = window.PileHeadChoices.Single(c => c.GroundNo == sp.GroundNo
                        && Math.Abs(c.PileHeadZ - sp.Z) < 1e-9);
                    window.SelectedPileHeadChoice = choice;
                    var t = window.TemporarySoilPile;
                    CollectionAssert.AreEqual(sp.ZDataItems.Select(z => z.Z).ToArray(), t.ZDataItems.Select(z => z.Z).ToArray(),
                        $"杭頭Z {sp.Z:F2}: 杭体ウィンドウの節点が解析と違います (地層の境で区切っていない)");
                    Assert.AreEqual(sp.Rpu, t.Rpu, 1e-6, $"杭頭Z {sp.Z:F2}: 先端の支持力");
                    Assert.AreEqual(sp.Rfu, t.Rfu, 1e-6, $"杭頭Z {sp.Z:F2}: 周面摩擦力が解析と違います");
                    Assert.AreEqual(sp.R_ULS, t.R_ULS, 1e-6, $"杭頭Z {sp.Z:F2}: 終局の支持力が解析と違います");
                }
            });

            // 組み立ては 1 つ (写しを持たない)
            string window = TestSource.Read("Graphics_r1", "ViewModels", "PileBodyViewModel.cs");
            StringAssert.Contains(TestSource.MethodBody(window, "public void UpdateTemporarySoilPile("), "InputModel.SoilPileNodeAltitudes(");
            string model = TestSource.Read("Graphics_r1", "Models", "InputData", "InputModel.cs");
            StringAssert.Contains(TestSource.MethodBody(model, "public void GenerateSoilPiles("), "SoilPileNodeAltitudes(");
        }

        [TestMethod]
        public void ThePileBodyWindow_OffersThePileHeadLevelsOfTheLayout()
        {
            OnSta(() =>
            {
                var (vm, _) = BuildTwoLevels();
                var window = new PileBodyViewModel(vm);
                Assert.AreEqual(2, window.PileHeadChoices.Count, "杭頭レベル 2 つを選べません");
                Assert.IsTrue(window.PileHeadChoices.All(c => c.PileCount == 12), "組ごとの杭の本数が違います");

                // 開いたときは杭配置の杭頭 (上の方) を選んだ状態。以前は 0 から始まった
                Assert.IsNotNull(window.SelectedPileHeadChoice, "開いたときに杭配置の杭頭を選んでいません");
                Assert.AreEqual(-2.0, window.PileTopAltitude, 1e-9, "開いたときの杭頭Z が杭配置の杭頭ではありません");

                window.SelectedPileHeadChoice = window.PileHeadChoices[1];
                Assert.AreEqual(-3.5, window.PileTopAltitude, 1e-9, "選んだ杭頭レベルになっていません");
                Assert.AreEqual(1, window.SelectedGroundNo);

                // 手で打った杭頭Z が杭配置に無ければ選択は外れ、合えば選び直る
                window.PileTopAltitude = -2.75;
                Assert.IsNull(window.SelectedPileHeadChoice, "杭配置に無い杭頭Z なのに、組を選んだままです");
                window.PileTopAltitude = -2.0;
                Assert.AreSame(window.PileHeadChoices[0], window.SelectedPileHeadChoice);
                StringAssert.Contains(window.PileHeadChoices[0].Label, "-2.000", "杭頭Z の負の値が 3 桁で出ていません");
            });

            string xaml = TestSource.Read("Graphics_r1", "Views", "PileBodyWindow.xaml");
            StringAssert.Contains(xaml, "SelectedItem=\"{Binding SelectedPileHeadChoice}\"", "杭配置の組を選ぶ欄がありません");
            Assert.IsFalse(xaml.Contains("-0.00;"), "負の値だけ小数 2 桁で出す書式が残っています");
        }

    }
}
