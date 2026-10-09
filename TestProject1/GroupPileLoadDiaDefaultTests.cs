using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 荷重面等価径の既定は杭先端径。
    ///
    /// <para>以前は未入力 (0) の杭を個別十字・個別矩形で黙って飛ばしており、荷重面等価径を入れずに荷重タイプを
    /// 選ぶと矩形荷重の表が空のままで、理由もどこにも出なかった (2026-10-08 の利用者の報告)。</para>
    /// </summary>
    [TestClass]
    public class GroupPileLoadDiaDefaultTests
    {
        [TestMethod]
        public void AnUnenteredLoadDiameter_UsesThePileToeDiameter()
        {
            var sp = new SoilPile { D = 0.6 };
            Assert.AreEqual(0.6, sp.EffectiveGroupPileLoadDia, 1e-12);
            Assert.IsTrue(sp.IsGroupPileLoadDiaFromToe);

            // 杭先端径を使っているセルを触っただけ (表示の値がそのまま戻る) では手入力にならない
            sp.GroupPileLoadDiaInput = 0.6;
            Assert.IsTrue(sp.IsGroupPileLoadDiaFromToe, "表示の値が書き戻されただけで手入力に変わりました");
            Assert.AreEqual(0.0, sp.GroupPileLoadDia);

            // 杭体を変えれば追従する (値を写し取っていない)
            var raised = new System.Collections.Generic.List<string?>();
            sp.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            sp.D = 0.8;
            Assert.AreEqual(0.8, sp.EffectiveGroupPileLoadDia, 1e-12);
            CollectionAssert.Contains(raised, nameof(SoilPile.GroupPileLoadDiaInput), "杭先端径が変わっても表が更新されません");

            // 入れた値が優先。0 を入れると杭先端径に戻る
            sp.GroupPileLoadDiaInput = 1.2;
            Assert.AreEqual(1.2, sp.EffectiveGroupPileLoadDia, 1e-12);
            Assert.IsFalse(sp.IsGroupPileLoadDiaFromToe);
            sp.D = 0.5;
            Assert.AreEqual(1.2, sp.EffectiveGroupPileLoadDia, 1e-12, "入れた値が杭先端径で上書きされました");
            sp.GroupPileLoadDiaInput = 0;
            Assert.AreEqual(0.5, sp.EffectiveGroupPileLoadDia, 1e-12);
            Assert.IsTrue(sp.IsGroupPileLoadDiaFromToe);

            // 保存するのは入れた値だけ (0 = 未入力)。計算した値は書き出さない
            foreach (var name in new[] { nameof(SoilPile.EffectiveGroupPileLoadDia), nameof(SoilPile.GroupPileLoadDiaInput), nameof(SoilPile.IsGroupPileLoadDiaFromToe) })
                Assert.IsTrue(typeof(SoilPile).GetProperty(name)!.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false),
                    $"{name} を保存しています (入れた値 GroupPileLoadDia だけを保存する)");
        }

        /// <summary>荷重面等価径を入れていなくても、個別十字を選べば全杭の矩形ができる。杭先端径も 0 なら、作らなかった杭を示す。</summary>
        [TestMethod]
        public void ChoosingCrossLoads_BuildsRectsForEveryPileWithoutEnteringTheDiameter()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var soilPiles = model.ElementDivision.SoilPiles;
            foreach (var sp in soilPiles) sp.GroupPileLoadDia = 0;
            Assert.IsTrue(soilPiles.All(sp => sp.D > 0), "前提: 杭先端径が入っている");
            var vm = new MainWindowViewModel { CurrentInputModel = model };

            model.PileGroupSettlement.LoadingType = "個別十字";
            vm.RebuildAutoCrossRectLoadsIfNeeded();
            Assert.AreEqual(model.PileLayoutItems.Count * 5, model.PileGroupSettlement.RectLoads.Count,
                "荷重面等価径が未入力の杭の十字ができていません");
            Assert.AreEqual("", vm.AutoRectLoadNote);

            // 杭先端径も 0 の杭は作れない。黙らずに示す
            foreach (var sp in soilPiles) sp.D = 0;
            vm.RebuildAutoCrossRectLoadsIfNeeded();
            Assert.AreEqual(0, model.PileGroupSettlement.RectLoads.Count);
            StringAssert.Contains(vm.AutoRectLoadNote, "荷重面等価径も杭先端径も 0 の杭");

            // 荷重タイプが自動で作らないものになれば消す
            model.PileGroupSettlement.LoadingType = "任意矩形";
            vm.RebuildAutoCrossRectLoadsIfNeeded();
            Assert.AreEqual("", vm.AutoRectLoadNote);
        }

        [TestMethod]
        public void TheWindow_ShowsTheEffectiveDiameterAndTheNote()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "GroupSettlementWindow.xaml");
            StringAssert.Contains(xaml, "Binding=\"{Binding GroupPileLoadDiaInput, StringFormat=N3}\"");
            Assert.IsFalse(xaml.Contains("Binding=\"{Binding GroupPileLoadDia, "), "表が入力値 (0) をそのまま出しています");
            StringAssert.Contains(xaml, "CellStyle=\"{StaticResource LoadDiaCellStyle}\"");
            StringAssert.Contains(xaml, "Text=\"{Binding AutoRectLoadNote}\"");
        }
    }
}
