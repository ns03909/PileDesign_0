using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 「変更可否」(IsChangeable) は保存されている値を引き継ぐこと。
    ///
    /// この列は「自動分割で足した節点か」を表し、削除と Z の編集の可否を決める。
    /// 読み込み時に false 固定にしていたため、保存済みの分割を開くと<b>全行が
    /// 動かせない扱い</b>になり、直したくても直せない。
    ///
    /// 開くたびに最小分割へ作り直していた頃は、どの節点も元から false だったので
    /// 表に出なかった。分割済みをそのまま開くようにして初めて見えた。
    /// </summary>
    [TestClass]
    public class ElementDivisionChangeabilityTests
    {
        [TestMethod]
        public void LoadingAStoredDivision_KeepsWhichNodesCanBeEdited()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var piles = input.ElementDivision?.SoilPiles;
            Assert.IsTrue(piles?.Count > 0, "土層-杭セットがありません");

            var items = piles[0].ZDataItems;
            Assert.IsTrue(items.Count >= 3, $"節点が {items.Count} 個しかありません");

            // 自動分割で足した節点を模して、1 つおきに立てる
            for (int i = 0; i < items.Count; i++) items[i].IsChangeable = i % 2 == 1;
            var expected = items.Select(z => z.IsChangeable).ToList();

            var mainVm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(mainVm);

            var vm = new ElementDivisionViewModel(
                mainVm,
                piles.Select(sp => sp.DeepCopy()).ToList(),
                input.ElementDivision?.SoilEmbedment?.DeepCopy());

            var shown = vm.SelectedZDataItems.Select(z => z.IsChangeable).ToList();

            CollectionAssert.AreEqual(expected, shown,
                "「変更可否」が保存されている値と食い違っています (false 固定になっていないか)");
            Assert.IsTrue(shown.Any(v => v), "すべて false になっています");
        }
    }
}
