using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 分離入力の「軸力モード: 絶対」(2026-10-27)。
    ///
    /// <para>分離入力のあいだは「変動」に固定していた。分離入力の欄は変動の値だけなので、絶対を選んでも
    /// 出すものが無かった——が、<b>いくらになっているのかを絶対値で確かめたい</b>。</para>
    ///
    /// <para>組合せごとに値が変わるので、絶対値として出せるのは一つに決めるしかない。出すのは
    /// <b>βU = βL = 1.0 の和</b> (VL + ΔL(L) + ΔL(U))。打った値をどの組合せの ΔL(U)・ΔL(L) へ戻すかが
    /// 決まらないので<b>読み取り専用</b>で、編集は変動の欄で行う。</para>
    ///
    /// <para>見るのは 3 つ——和の式、ΔL(U)・ΔL(L)・VL を動かしたときに追いつくこと、そして
    /// <b>出している数が解析の使う数と一致すること</b> (βU = βL = 1 の組合せで)。</para>
    /// </summary>
    [TestClass]
    public class SeparatedAbsoluteModeTests
    {
        /// <summary>βU = βL = 1.0 の組合せ。</summary>
        private static readonly LoadCombination Unity = new(1, 1.0, 1.0, 1.0);

        private bool _wasVariationMode;

        [TestInitialize]
        public void Init() => _wasVariationMode = PileDesign.Common.AxialForceModeContext.IsVariationMode;

        [TestCleanup]
        public void Cleanup() => PileDesign.Common.AxialForceModeContext.IsVariationMode = _wasVariationMode;

        private static PileLayoutDataItem MakePile()
        {
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, AxialForceVL0 = 1000.0, AxialForceVLAdditional = 200.0 };
            pile.AxialForceUpperLevel1s = [300.0, -300.0, 0.0, 50.0];
            pile.AxialForceFoundationLevel1s = [100.0, -100.0, 0.0, 10.0];
            pile.AxialForceUpperLevel2s = [800.0, -800.0, 0.0, 0.0];
            pile.AxialForceFoundationLevel2s = [600.0, -600.0, 0.0, 0.0];
            return pile;
        }

        [TestMethod]
        public void TheAbsoluteValueIsVLPlusBothVariations()
        {
            var pile = MakePile();

            // VL = 1200
            CollectionAssert.AreEqual(new[] { 1600.0, 800.0, 1200.0, 1260.0 }, pile.AxialForceSeparatedSumLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 2600.0, -200.0, 1200.0, 1200.0 }, pile.AxialForceSeparatedSumLevel2s.ToArray());
        }

        /// <summary>
        /// <b>欄を打ったら追いつくこと。</b>追いつかないと、読み取り専用の欄が古い数を出し続ける
        /// ——見ている人には、それが嘘だと分からない。
        /// </summary>
        [TestMethod]
        public void TheAbsoluteValueFollowsEveryInputItIsMadeOf()
        {
            var pile = MakePile();

            pile.AxialForceUpperLevel1s[0] = 500.0;
            Assert.AreEqual(1200 + 100 + 500, pile.AxialForceSeparatedSumLevel1s[0], 1e-9, "ΔL(U) に追いつかない");

            pile.AxialForceFoundationLevel1s[0] = 0.0;
            Assert.AreEqual(1200 + 0 + 500, pile.AxialForceSeparatedSumLevel1s[0], 1e-9, "ΔL(L) に追いつかない");

            pile.AxialForceVL0 = 2000.0;   // VL = 2200
            Assert.AreEqual(2200 + 0 + 500, pile.AxialForceSeparatedSumLevel1s[0], 1e-9, "VL に追いつかない");

            pile.AxialForceVLAdditional = 0.0;   // VL = 2000
            Assert.AreEqual(2000 + 0 + 500, pile.AxialForceSeparatedSumLevel1s[0], 1e-9, "VLadd に追いつかない");
        }

        /// <summary>コレクションごと差し替えても追いつくこと (ファイルの読み込みがこの道を通る)。</summary>
        [TestMethod]
        public void ReplacingTheWholeCollection_RebuildsTheAbsoluteValue()
        {
            var pile = MakePile();

            pile.AxialForceUpperLevel1s = [10.0, 20.0, 30.0, 40.0];
            pile.AxialForceFoundationLevel1s = [1.0, 2.0, 3.0, 4.0];

            CollectionAssert.AreEqual(new[] { 1211.0, 1222.0, 1233.0, 1244.0 }, pile.AxialForceSeparatedSumLevel1s.ToArray());

            // 差し替えた先の要素を打っても追いつく (購読を張り直していること)。
            pile.AxialForceUpperLevel1s[0] = 0.0;
            Assert.AreEqual(1201.0, pile.AxialForceSeparatedSumLevel1s[0], 1e-9, "差し替えた先の購読が張られていない");
        }

        /// <summary>
        /// <b>出している数が、解析の使う数と一致すること。</b>絶対値の欄を別に計算しているので、
        /// 式がずれると「画面の絶対値」と「解析の軸力」が食い違う。
        /// </summary>
        [TestMethod]
        public void TheAbsoluteValueMatchesWhatTheAnalysisUsesAtUnityBetas()
        {
            var pile = MakePile();
            var input = new InputModel { IsAxialForceSeparated = true };
            input.PileLayoutItems = [pile];

            foreach (int level in new[] { 1, 2 })
            {
                var shown = pile.AxialForceSeparatedSumOf(level);
                for (int i = 0; i < shown.Count; i++)
                {
                    double used = SeismicAxialForces.Input(input, pile, i + 1, level, Unity);
                    Assert.AreEqual(used, shown[i], 1e-9, $"L{level}-{i + 1} が解析の軸力と違う");
                }
            }
        }

        /// <summary>ファイルには入れない (入力ではなく派生値)。</summary>
        [TestMethod]
        public void TheAbsoluteValueIsNotSaved()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(MakePile());

            Assert.IsFalse(json.Contains("AxialForceSeparatedSum", System.StringComparison.Ordinal),
                "派生値がファイルに入っている");
        }

        // ── モードの切り替えと、欄の出し分け ──

        /// <summary>
        /// <b>分離入力でも「絶対」を選べる。</b>以前は弾いていた (チェックが戻るだけで、何も起きなかった)。
        /// </summary>
        [TestMethod]
        public void AbsoluteModeCanBeChosenWhileSeparated()
        {
            var vm = new MainWindowViewModel
            {
                CurrentInputModel = new InputModel { IsAxialForceSeparated = true, IsAxialForceVariationMode = true },
            };

            vm.IsAxialForceVariationMode = false;

            Assert.IsFalse(vm.IsAxialForceVariationMode, "分離入力のあいだ「絶対」を選べない");
            Assert.IsFalse(PileDesign.Common.AxialForceModeContext.IsVariationMode, "静的フラグが追いついていない");
        }

        /// <summary>
        /// 欄の出し分け。分離入力の絶対モードでは<b>読み取り専用の絶対の欄だけ</b>を出す
        /// ——打てる絶対の欄と並べて出すと、どちらを打つのかが分からない。
        /// </summary>
        [TestMethod]
        public void TheColumnsShownMatchTheModeAndTheSeparatedFlag()
        {
            foreach (bool separated in new[] { false, true })
            {
                foreach (bool variation in new[] { false, true })
                {
                    var vm = new MainWindowViewModel
                    {
                        CurrentInputModel = new InputModel
                        {
                            IsAxialForceSeparated = separated,
                            IsAxialForceVariationMode = variation,
                        },
                    };

                    string where = $"分離入力 {separated} / 変動 {variation}";

                    Assert.AreEqual(!separated && !variation, vm.IsAxialForceAbsoluteColumnsVisible, $"打てる絶対の欄: {where}");
                    Assert.AreEqual(!separated && variation, vm.IsAxialForceVariationColumnsVisible, $"変動の欄: {where}");
                    Assert.AreEqual(separated && !variation, vm.IsAxialForceSeparatedSumColumnsVisible, $"分離入力の絶対の欄: {where}");

                    // 断り書きは、分離入力の絶対モードのときだけ。
                    Assert.AreEqual(separated && !variation, vm.IsSeparatedAbsoluteNoticeVisible, $"断り書き: {where}");

                    // **打てる欄と読み取り専用の欄は、同時に出さない。**
                    Assert.IsFalse(vm.IsAxialForceAbsoluteColumnsVisible && vm.IsAxialForceSeparatedSumColumnsVisible,
                        $"絶対の欄が二重に出ている: {where}");
                }
            }
        }

        /// <summary>
        /// 読み取り専用の欄は、XAML でも読み取り専用にしてあること。
        /// <b>ここが抜けると打ててしまい、打った値は派生値なので次の作り直しで消える。</b>
        /// </summary>
        [TestMethod]
        public void TheSumColumnsAreReadOnlyInTheXaml()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            var columns = System.Text.RegularExpressions.Regex.Matches(
                xaml, @"<DataGridTextColumn[^>]*AxialForceSeparatedSumLevel\ds\[\d\][^>]*>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            Assert.AreEqual(8, columns.Count, "分離入力の絶対の欄が 8 つ (L1-1〜L2-4) ない");

            foreach (var column in columns.Cast<System.Text.RegularExpressions.Match>())
            {
                Assert.IsTrue(column.Value.Contains("IsReadOnly=\"True\"", System.StringComparison.Ordinal),
                    "分離入力の絶対の欄が読み取り専用になっていない: " + column.Value[..80]);
            }
        }

        /// <summary>
        /// <b>何の和かを画面で言うこと。</b>断りが無いと、組合せの係数を掛けた値だと読まれる。
        /// </summary>
        [TestMethod]
        public void TheScreenSaysItIsTheUnityBetaSum()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");

            Assert.IsTrue(xaml.Contains("βU = βL = 1.0 の和", System.StringComparison.Ordinal),
                "βU = βL = 1.0 の和であることが画面に出ていない");
            Assert.IsTrue(xaml.Contains("IsSeparatedAbsoluteNoticeVisible", System.StringComparison.Ordinal),
                "断り書きの出し分けが画面に無い");
        }

        /// <summary>プロパティの面でも、絶対モードでは和を読み取り専用で添えること。</summary>
        [TestMethod]
        public void ThePropertyPanelAddsTheSumAsAReadOnlyRow()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel
            {
                CurrentInputModel = new InputModel { IsAxialForceSeparated = true, IsAxialForceVariationMode = false },
            };
            vm.CurrentInputModel.PileLayoutItems = [pile];
            PileDesign.Common.AxialForceModeContext.IsVariationMode = false;

            pile.IsSelected = true;
            vm.UpdatePropertyPanel();

            var row = vm.SelectedItemProperties.FirstOrDefault(p => p.Name == "軸力 1-1");
            Assert.IsNotNull(row, "絶対モードなのに和の行が無い");
            Assert.AreEqual(PropertyInputType.ReadOnly, row.InputType, "和の行が打てる");
            Assert.AreEqual("1600.0", row.Value);

            // 変動の欄は打てるまま (編集はそちらで行う)。
            var upper = vm.SelectedItemProperties.FirstOrDefault(p => p.Name == "ΔL1-1(U)");
            Assert.IsNotNull(upper, "ΔL(U) の行が無い");
            Assert.AreEqual(PropertyInputType.Number, upper.InputType);
        }
    }
}
