using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 断面タイプが「ユーザーが操作していないのに」差し替わらないことを固定する。
    ///
    /// 実際に起きた不具合: 節杭の区間が最上段になると選択肢から節杭が外れ、
    /// 杭断面ウィンドウを開いた瞬間に ComboBox の SelectedItem が null になり、
    /// TwoWay バインドがその null を書き戻して断面タイプが場所打ち RC に化けていた。
    /// 杭姿図・メイン画面から節が消え、選択肢も旧 3 種類だけになる。
    /// </summary>
    [TestClass]
    public class PileSectionTypePersistenceTests
    {
        /// <summary>
        /// 断面タイプの選択肢は区間の位置によらず一定であること。
        ///
        /// 以前は最上段区間で節杭を除外していたが、その判定に使う IsTopSegment は
        /// 区間更新が走るまで false のままで、「初回は選べるが開き直すと消える」という
        /// 不安定な挙動になっていた。Smart-MAGNUM / Hybrid ニーディングを 1 区間で
        /// モデル化する場合にも節杭が必要なため、制限自体を外した。
        /// </summary>
        [TestMethod]
        public void SectionTypeOptions_DoNotDependOnSegmentPosition()
        {
            var section = MakePrecast(PileTypeNames.Phc);

            section.IsTopSegment = false;
            var lower = section.PreCastConcretePileSectionTypeOption;
            section.IsTopSegment = true;
            var top = section.PreCastConcretePileSectionTypeOption;

            CollectionAssert.AreEqual(lower, top, "区間の位置で選択肢が変わっている");
            CollectionAssert.Contains(top, PileTypeNames.PhcNodular, "最上段でも節杭を選べること");
            CollectionAssert.Contains(top, PileTypeNames.BfsTip);
        }

        private static PileSection MakePrecast(string sectionType)
        {
            var section = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
            };
            return section;
        }

        [TestMethod]
        public void NullAssignment_KeepsTheCurrentType()
        {
            var section = MakePrecast(PileTypeNames.PhcNodular);

            section.PileSectionType = null;
            Assert.AreEqual(PileTypeNames.PhcNodular, section.PileSectionType, "null の書き戻しで型が失われている");

            section.PileSectionType = "";
            Assert.AreEqual(PileTypeNames.PhcNodular, section.PileSectionType, "空文字の書き戻しで型が失われている");
        }

        [TestMethod]
        public void OptionsAlwaysContainTheCurrentType_EvenOnTheTopSegment()
        {
            // 上の区間を削除して節杭の区間が最上段になった状況
            var section = MakePrecast(PileTypeNames.PhcNodular);
            section.IsTopSegment = true;

            CollectionAssert.Contains(section.PreCastConcretePileSectionTypeOption, PileTypeNames.PhcNodular,
                "現在選ばれている型が選択肢から消えている (ComboBox が null を書き戻す原因になる)");
        }

        [TestMethod]
        public void UnknownType_FallsBackWithinTheSamePileBodyType()
        {
            // 既製コンクリート杭が場所打ち RC 断面に化けてはいけない
            var precast = MakePrecast(PileTypeNames.Phc);
            precast.PileSectionType = "存在しない断面タイプ";
            Assert.AreEqual(PileTypeNames.Phc, precast.PileSectionType);

            var steel = new PileSection
            {
                PileBodyType = PileTypeNames.SteelPipe,
                PileSectionType = PileTypeNames.SteelPipeSection,
            };
            steel.PileSectionType = "存在しない断面タイプ";
            Assert.AreEqual(PileTypeNames.SteelPipeSection, steel.PileSectionType);
        }

        [TestMethod]
        public void NodularTypesRemainSelectableOnLowerSegments()
        {
            var section = MakePrecast(PileTypeNames.Phc);
            section.IsTopSegment = false;

            var options = section.PreCastConcretePileSectionTypeOption;
            foreach (string t in new[]
            {
                PileTypeNames.PhcNodular, PileTypeNames.PrcNodular,
                PileTypeNames.PrcNodularPhcPart, PileTypeNames.BfsHead, PileTypeNames.BfsTip
            })
            {
                CollectionAssert.Contains(options, t, t);
            }
        }
    }
}
