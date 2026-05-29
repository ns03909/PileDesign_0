using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// ElementDivision の派生プロパティ (SoilPileNumberOption / SoilEmbedment 等) と
    /// SetSoilPilesSilently の挙動を検証する。
    ///
    /// memo project_test_gaps.md item 5 のカバー追加。
    /// </summary>
    [TestClass]
    public class ElementDivisionExtraTests
    {
        [TestMethod]
        public void SoilPiles_AssignmentNotifies()
        {
            var ed = new ElementDivision();
            var changed = new System.Collections.Generic.List<string>();
            ed.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            ed.SoilPiles = [new SoilPile(), new SoilPile()];

            CollectionAssert.Contains(changed, nameof(ed.SoilPiles));
        }

        [TestMethod]
        public void UpdateSoilPileNumberOption_PopulatesWithSequence()
        {
            // SoilPiles.Count = N → SoilPileNumberOption = [1, 2, ..., N]
            var ed = new ElementDivision();
            ed.SoilPiles = [new SoilPile(), new SoilPile(), new SoilPile()];
            ed.UpdateSoilPileNumberOption();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ed.SoilPileNumberOption);
        }

        [TestMethod]
        public void UpdateSoilPileNumberOption_EmptyPiles_EmptyOption()
        {
            var ed = new ElementDivision();
            ed.SoilPiles = [];
            ed.UpdateSoilPileNumberOption();
            Assert.AreEqual(0, ed.SoilPileNumberOption.Count);
        }

        [TestMethod]
        public void SetSoilPilesSilently_DoesNotFirePropertyChanged()
        {
            // SetSoilPilesSilently は PropertyChanged を発火しない (Undo スナップショット用)
            var ed = new ElementDivision();
            ed.SoilPiles = [new SoilPile()]; // 初期化

            var changedAfter = new System.Collections.Generic.List<string>();
            ed.PropertyChanged += (_, e) => { if (e.PropertyName != null) changedAfter.Add(e.PropertyName); };

            ed.SetSoilPilesSilently([new SoilPile(), new SoilPile()]);

            Assert.IsFalse(changedAfter.Contains(nameof(ed.SoilPiles)),
                "SetSoilPilesSilently は SoilPiles の PropertyChanged を発火してはならない");
            Assert.AreEqual(2, ed.SoilPiles.Count, "値自体は更新されるべき");
        }

        [TestMethod]
        public void SoilEmbedment_AssignmentNotifies()
        {
            var ed = new ElementDivision();
            var changed = new System.Collections.Generic.List<string>();
            ed.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            ed.SoilEmbedment = new SoilEmbedment();

            CollectionAssert.Contains(changed, nameof(ed.SoilEmbedment));
        }

        [TestMethod]
        public void FirstDistance_AssignmentNotifies()
        {
            var ed = new ElementDivision();
            var changed = new System.Collections.Generic.List<string>();
            ed.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            ed.FirstDistance = 0.5;

            CollectionAssert.Contains(changed, nameof(ed.FirstDistance));
        }
    }
}
