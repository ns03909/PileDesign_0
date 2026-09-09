using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 静的な状態が、テストの間で持ち越されないこと。
    /// </summary>
    [TestClass]
    public class TestStateScopeTests
    {
        /// <summary>
        /// 退避する項目を手で並べていないこと。
        ///
        /// 並べると、オプションを足したときに足し忘れる。実際にそれで
        /// 2 つ (指針ヤング係数・鋼管杭の柱座屈) がどの戻し処理にも入っていなかった。
        /// </summary>
        [TestMethod]
        public void EverySettableOptionIsCoveredWithoutListingThemByHand()
        {
            var props = TestStateScope.SettableStaticOptions().ToArray();

            TestSource.AssertScanned(props.Length, 15, "設定できる材料モデル化オプション");

            // 手で並べた形跡が無いこと (この仕組み自体をソースで確かめる)。
            // 説明文の中の使用例は対象外。
            var lines = TestSource.Read("TestProject1", "TestStateScope.cs")
                .Split('\n');
            var listed = lines
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith("//"))
                .Where(l => l.StartsWith("ConcreteModelOptions.") && l.Contains("="))
                .ToArray();

            Assert.AreEqual(0, listed.Length,
                "退避する項目を手で並べています。足し忘れが起きます: "
                + string.Join(" / ", listed));
        }

        /// <summary>触った値が、抜けたときに戻ること。</summary>
        [TestMethod]
        public void ChangesInsideTheScopeAreUndone()
        {
            bool before = ConcreteModelOptions.UseFiberMPhi;
            int caseBefore = ConcreteModelOptions.Notification1113CompressionCase;

            using (TestStateScope.Enter())
            {
                ConcreteModelOptions.UseFiberMPhi = !before;
                ConcreteModelOptions.Notification1113CompressionCase = caseBefore == 1 ? 2 : 1;
            }

            Assert.AreEqual(before, ConcreteModelOptions.UseFiberMPhi, "真偽の設定が戻らない");
            Assert.AreEqual(caseBefore, ConcreteModelOptions.Notification1113CompressionCase, "数値の設定が戻らない");
        }

        /// <summary>
        /// まとめ役 (書くと他も書き換えるもの) を触っても、全部が元に戻ること。
        /// </summary>
        [TestMethod]
        public void TheCombinedSwitchIsUndoneToo()
        {
            bool compBefore = ConcreteModelOptions.UseNotification1113Compression;
            bool shearBefore = ConcreteModelOptions.UseNotification1113Shear;

            using (TestStateScope.Enter())
            {
                ConcreteModelOptions.UseNotification1113 = !compBefore;
            }

            Assert.AreEqual(compBefore, ConcreteModelOptions.UseNotification1113Compression, "許容圧縮が戻らない");
            Assert.AreEqual(shearBefore, ConcreteModelOptions.UseNotification1113Shear, "許容せん断が戻らない");
        }

        /// <summary>画面の参照と軸力モードも戻ること。</summary>
        [TestMethod]
        public void TheAppLevelStateIsUndone()
        {
            var vmBefore = PileDesign.App.CurrentMainViewModel;
            bool modeBefore = PileDesign.Common.AxialForceModeContext.IsVariationMode;

            using (TestStateScope.Enter())
            {
                PileDesign.App.CurrentMainViewModel = new PileDesign.ViewModels.MainWindowViewModel();
                PileDesign.Common.AxialForceModeContext.IsVariationMode = !modeBefore;
            }

            Assert.AreSame(vmBefore, PileDesign.App.CurrentMainViewModel, "画面の参照が戻らない");
            Assert.AreEqual(modeBefore, PileDesign.Common.AxialForceModeContext.IsVariationMode, "軸力モードが戻らない");
        }
    }
}
