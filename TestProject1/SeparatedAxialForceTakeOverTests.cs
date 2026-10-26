using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// 分離入力を切り替えたときの、入力済みの地震時軸力の引き継ぎ (2026-10-27)。
    ///
    /// <para>分けない入力 (絶対値) と分離入力 (ΔL(U)・ΔL(L)) は別に持っているので、切り替えただけでは
    /// 引き継がれなかった。チェックを入れると<b>入力済みの軸力が 0 に見える</b>ので、入れ直すか
    /// チェックを戻すしかなかった。</para>
    ///
    /// <para>見るのは 4 つ——配る式、戻す式、<b>配ってから戻すと元に戻ること</b>、そして
    /// 訊き方 (はい・いいえ・キャンセルで何が起きるか)。</para>
    /// </summary>
    [TestClass]
    public class SeparatedAxialForceTakeOverTests
    {
        private bool _unattended;

        [TestInitialize]
        public void Init()
        {
            _unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
        }

        [TestCleanup]
        public void Cleanup()
        {
            MessageService.IsUnattended = _unattended;
            MessageService.UnattendedAnswer = null;
        }

        /// <summary>VL = 1200、変動軸力は L1 が [300, -300, 0, 50]、L2 が [800, 0, 0, 0]。</summary>
        private static PileLayoutDataItem MakePile()
        {
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, AxialForceVL0 = 1000.0, AxialForceVLAdditional = 200.0 };
            pile.AxialForceLevel1s = [1500.0, 900.0, 1200.0, 1250.0];
            pile.AxialForceLevel2s = [2000.0, 1200.0, 1200.0, 1200.0];
            return pile;
        }

        private static InputModel MakeInput(bool separated, params PileLayoutDataItem[] piles)
        {
            var input = new InputModel { IsAxialForceSeparated = separated };
            input.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles);
            return input;
        }

        // ── 入っているかの判定 ──

        /// <summary>
        /// <b>絶対値ではなく変動を見る。</b>何も入れていなくても絶対値は VL のぶん 0 ではないので、
        /// そこを「入力済み」と読むと新しいファイルで毎回訊くことになる。
        /// </summary>
        [TestMethod]
        public void VLAlone_IsNotReadAsEnteredAxialForce()
        {
            var pile = new PileLayoutDataItem { AxialForceVL0 = 1000.0 };
            pile.AxialForceLevel1s = [1000.0, 1000.0, 1000.0, 1000.0];
            pile.AxialForceLevel2s = [1000.0, 1000.0, 1000.0, 1000.0];

            Assert.IsFalse(AxialForceSeparation.HasVariation([pile]), "VL だけで「入力済み」と読んでいる");
            Assert.IsFalse(AxialForceSeparation.HasSeparated([pile]), "0 の ΔL(U)・ΔL(L) を「入力済み」と読んでいる");

            Assert.IsTrue(AxialForceSeparation.HasVariation([MakePile()]));
        }

        [TestMethod]
        public void SeparatedValues_AreSeenWhicheverSideIsEntered()
        {
            var upperOnly = new PileLayoutDataItem { AxialForceUpperLevel2s = [0.0, 0.0, 120.0, 0.0] };
            var foundationOnly = new PileLayoutDataItem { AxialForceFoundationLevel1s = [-80.0, 0.0, 0.0, 0.0] };

            Assert.IsTrue(AxialForceSeparation.HasSeparated([upperOnly]), "ΔL(U) だけ入っているのを見落としている");
            Assert.IsTrue(AxialForceSeparation.HasSeparated([foundationOnly]), "ΔL(L) だけ入っているのを見落としている");
        }

        // ── 配る式・戻す式 ──

        [TestMethod]
        public void Split_GivesTheShareToUpper_AndTheRestToFoundation()
        {
            var pile = MakePile();

            AxialForceSeparation.Split([pile], 0.75);

            // L1 の変動は [300, -300, 0, 50]
            CollectionAssert.AreEqual(new[] { 225.0, -225.0, 0.0, 37.5 }, pile.AxialForceUpperLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 75.0, -75.0, 0.0, 12.5 }, pile.AxialForceFoundationLevel1s.ToArray());

            // L2 の変動は [800, 0, 0, 0]
            CollectionAssert.AreEqual(new[] { 600.0, 0.0, 0.0, 0.0 }, pile.AxialForceUpperLevel2s.ToArray());
            CollectionAssert.AreEqual(new[] { 200.0, 0.0, 0.0, 0.0 }, pile.AxialForceFoundationLevel2s.ToArray());

            // 配っても、分けない入力はそのまま (戻すときの元になる)。
            CollectionAssert.AreEqual(new[] { 1500.0, 900.0, 1200.0, 1250.0 }, pile.AxialForceLevel1s.ToArray());
        }

        [TestMethod]
        public void Split_AtTheEnds_PutsEverythingOnOneSide()
        {
            var all = MakePile();
            AxialForceSeparation.Split([all], 1.0);
            CollectionAssert.AreEqual(new[] { 300.0, -300.0, 0.0, 50.0 }, all.AxialForceUpperLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 0.0, 0.0 }, all.AxialForceFoundationLevel1s.ToArray());

            var none = MakePile();
            AxialForceSeparation.Split([none], 0.0);
            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 0.0, 0.0 }, none.AxialForceUpperLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 300.0, -300.0, 0.0, 50.0 }, none.AxialForceFoundationLevel1s.ToArray());
        }

        [TestMethod]
        public void Clear_ZeroesBothSides_AndLeavesTheUnseparatedInputAlone()
        {
            var pile = MakePile();
            pile.AxialForceUpperLevel1s = [10.0, 20.0, 30.0, 40.0];
            pile.AxialForceFoundationLevel2s = [-5.0, 0.0, 0.0, 0.0];

            AxialForceSeparation.Clear([pile]);

            Assert.IsFalse(AxialForceSeparation.HasSeparated([pile]));
            CollectionAssert.AreEqual(new[] { 1500.0, 900.0, 1200.0, 1250.0 }, pile.AxialForceLevel1s.ToArray());
        }

        [TestMethod]
        public void Merge_MakesTheVariationTheSumOfBothSides()
        {
            var pile = new PileLayoutDataItem { AxialForceVL0 = 1000.0, AxialForceVLAdditional = 200.0 };
            pile.AxialForceLevel1s = [0.0, 0.0, 0.0, 0.0];
            pile.AxialForceLevel2s = [0.0, 0.0, 0.0, 0.0];
            pile.AxialForceUpperLevel1s = [300.0, -300.0, 0.0, 50.0];
            pile.AxialForceFoundationLevel1s = [100.0, -100.0, 0.0, 10.0];

            AxialForceSeparation.Merge([pile]);

            // 絶対値 = VL + ΔL(L) + ΔL(U)
            CollectionAssert.AreEqual(new[] { 1600.0, 800.0, 1200.0, 1260.0 }, pile.AxialForceLevel1s.ToArray());

            // 変動 (= 絶対 − VL) が和になっている。
            CollectionAssert.AreEqual(new[] { 400.0, -400.0, 0.0, 60.0 }, pile.AxialForceVariationLevel1s.ToArray());
        }

        /// <summary>
        /// <b>配ってから戻すと元に戻る。</b>割合で配ったものを足し直すので、丸めると値が動く
        /// ——1/3 のような割合でも一致すること。
        /// </summary>
        [TestMethod]
        public void SplitThenMerge_GivesBackTheSameAxialForce()
        {
            foreach (double share in new[] { 0.0, 0.3, 1.0 / 3.0, 0.5, 0.77, 1.0 })
            {
                var pile = MakePile();
                var wasL1 = pile.AxialForceLevel1s.ToArray();
                var wasL2 = pile.AxialForceLevel2s.ToArray();

                AxialForceSeparation.Split([pile], share);
                AxialForceSeparation.Merge([pile]);

                for (int i = 0; i < wasL1.Length; i++)
                {
                    Assert.AreEqual(wasL1[i], pile.AxialForceLevel1s[i], 1e-9, $"割合 {share} で L1-{i + 1} が動いた");
                    Assert.AreEqual(wasL2[i], pile.AxialForceLevel2s[i], 1e-9, $"割合 {share} で L2-{i + 1} が動いた");
                }
            }
        }

        // ── 訊き方 ──

        /// <summary>ダイアログの答えを決める。引き継ぎの問いだけ指定し、ほかの確認は「進める」側。</summary>
        private static void Answer(MessageBoxResult takeOver, double? percent, MainWindowViewModel vm)
        {
            MessageService.UnattendedAnswer = (text, button) =>
                text.Contains("分離入力", StringComparison.Ordinal) && button != MessageBoxButton.OK
                    ? takeOver
                    : button switch
                    {
                        MessageBoxButton.YesNo => MessageBoxResult.Yes,
                        MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
                        MessageBoxButton.OKCancel => MessageBoxResult.OK,
                        _ => MessageBoxResult.OK,
                    };

            vm.AskUpperSharePercent = percent is { } value ? _ => value.ToString("0.###") : _ => null;
        }

        [TestMethod]
        public void TurningItOn_AndAnsweringYes_SplitsTheEnteredVariationByTheShare()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            Answer(MessageBoxResult.Yes, percent: 60, vm);

            vm.IsAxialForceSeparated = true;

            Assert.IsTrue(vm.IsAxialForceSeparated, "切り替わっていない");
            CollectionAssert.AreEqual(new[] { 180.0, -180.0, 0.0, 30.0 }, pile.AxialForceUpperLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 120.0, -120.0, 0.0, 20.0 }, pile.AxialForceFoundationLevel1s.ToArray());

            // 分離入力にすると「変動」へ移る (入力は ΔL(U)・ΔL(L) で行う)。
            Assert.IsTrue(vm.IsAxialForceVariationMode);
        }

        [TestMethod]
        public void TurningItOn_AndAnsweringNo_ZeroesBothSides()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            Answer(MessageBoxResult.No, percent: null, vm);

            vm.IsAxialForceSeparated = true;

            Assert.IsTrue(vm.IsAxialForceSeparated);
            Assert.IsFalse(AxialForceSeparation.HasSeparated([pile]), "0 にしていない");
        }

        /// <summary>
        /// キャンセルなら<b>何も起きない</b>。チェックも戻るし、割合も訊かない。
        /// </summary>
        [TestMethod]
        public void TurningItOn_AndCancelling_ChangesNothing()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            bool asked = false;
            Answer(MessageBoxResult.Cancel, percent: null, vm);
            vm.AskUpperSharePercent = _ => { asked = true; return null; };

            vm.IsAxialForceSeparated = true;

            Assert.IsFalse(vm.IsAxialForceSeparated, "キャンセルしたのに切り替わった");
            Assert.IsFalse(asked, "キャンセルしたのに割合を訊いた");
            Assert.IsFalse(AxialForceSeparation.HasSeparated([pile]));
            CollectionAssert.AreEqual(new[] { 1500.0, 900.0, 1200.0, 1250.0 }, pile.AxialForceLevel1s.ToArray());
        }

        /// <summary>割合の入力をキャンセルしたときも、切り替えないこと。</summary>
        [TestMethod]
        public void TurningItOn_AndCancellingTheShare_DoesNotSwitch()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            Answer(MessageBoxResult.Yes, percent: null, vm);   // 割合は null = キャンセル

            vm.IsAxialForceSeparated = true;

            Assert.IsFalse(vm.IsAxialForceSeparated);
            Assert.IsFalse(AxialForceSeparation.HasSeparated([pile]));
        }

        /// <summary>範囲外の割合は受けない。3 回で諦めて切り替えない。</summary>
        [TestMethod]
        public void AShareOutsideZeroToHundred_IsRejected()
        {
            var pile = MakePile();
            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            int asked = 0;
            Answer(MessageBoxResult.Yes, percent: null, vm);
            vm.AskUpperSharePercent = _ => { asked++; return "120"; };

            vm.IsAxialForceSeparated = true;

            Assert.IsFalse(vm.IsAxialForceSeparated, "120% で切り替わった");
            Assert.AreEqual(3, asked, "聞き直しの回数が変わっている (繰り返しから出られないと固まる)");
        }

        /// <summary>何も入力されていなければ訊かない——新しいファイルで毎回出るのは邪魔なだけ。</summary>
        [TestMethod]
        public void WithNothingEntered_NobodyIsAsked()
        {
            var pile = new PileLayoutDataItem { AxialForceVL0 = 1000.0 };
            pile.AxialForceLevel1s = [1000.0, 1000.0, 1000.0, 1000.0];
            pile.AxialForceLevel2s = [1000.0, 1000.0, 1000.0, 1000.0];

            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(false, pile) };
            bool asked = false;
            MessageService.UnattendedAnswer = (text, _) =>
            {
                if (text.Contains("変動軸力", StringComparison.Ordinal)) asked = true;
                return MessageBoxResult.Yes;
            };
            vm.AskUpperSharePercent = _ => "50";

            vm.IsAxialForceSeparated = true;

            Assert.IsTrue(vm.IsAxialForceSeparated);
            Assert.IsFalse(asked, "何も入っていないのに引き継ぎを訊いた");
        }

        [TestMethod]
        public void TurningItOff_MakesTheVariationTheSum_AfterTheMessage()
        {
            var pile = MakePile();
            pile.AxialForceUpperLevel1s = [300.0, -300.0, 0.0, 50.0];
            pile.AxialForceFoundationLevel1s = [100.0, -100.0, 0.0, 10.0];

            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(true, pile) };
            Answer(MessageBoxResult.OK, percent: null, vm);

            vm.IsAxialForceSeparated = false;

            Assert.IsFalse(vm.IsAxialForceSeparated);
            CollectionAssert.AreEqual(new[] { 1600.0, 800.0, 1200.0, 1260.0 }, pile.AxialForceLevel1s.ToArray());
        }

        /// <summary>やめるのをキャンセルしたら、分離入力のまま。分けない入力も書き換えない。</summary>
        [TestMethod]
        public void TurningItOff_AndCancelling_KeepsTheSeparatedInput()
        {
            var pile = MakePile();
            pile.AxialForceUpperLevel1s = [300.0, 0.0, 0.0, 0.0];

            var vm = new MainWindowViewModel { CurrentInputModel = MakeInput(true, pile) };
            MessageService.UnattendedAnswer = (text, button) =>
                text.Contains("分離入力をやめると", StringComparison.Ordinal)
                    ? MessageBoxResult.Cancel
                    : MessageBoxResult.Yes;

            vm.IsAxialForceSeparated = false;

            Assert.IsTrue(vm.IsAxialForceSeparated, "キャンセルしたのに分離入力をやめた");
            CollectionAssert.AreEqual(new[] { 1500.0, 900.0, 1200.0, 1250.0 }, pile.AxialForceLevel1s.ToArray());
            CollectionAssert.AreEqual(new[] { 300.0, 0.0, 0.0, 0.0 }, pile.AxialForceUpperLevel1s.ToArray());
        }
    }
}
