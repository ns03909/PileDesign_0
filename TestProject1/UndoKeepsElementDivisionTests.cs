using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// Undo で<b>杭要素分割 (SoilPiles) が消えない</b>こと。
    ///
    /// <c>InputModel.DeepCopy</c> は SoilPiles を JSON 直列化から外している (重いため)。
    /// ところが複製側へ入れ直しておらず、<b>複製の SoilPiles が空</b>になっていた。
    /// Undo はこの複製を現在の入力に差し替えるので、一度でも Undo すると
    /// <list type="bullet">
    /// <item>土層-杭セットが丸ごと消える (分割済みのフラグだけが残る)</item>
    /// <item>単杭沈下の荷重-沈下曲線も一緒に消える</item>
    /// <item>基礎梁考慮沈下が「単杭沈下解析が未実行の杭があります」と言い出す</item>
    /// <item>水平解析の杭先端 P-S ばね (沈下解析の曲線を流用する設定) も効かなくなる</item>
    /// </list>
    /// という状態になっていた。
    /// </summary>
    [TestClass]
    public class UndoKeepsElementDivisionTests
    {
        private static SoilPile MakeSoilPile()
        {
            var sp = new SoilPile();
            sp.Initialize(1, 1, new GroundInput(), 1, new PileBodyInput(), 0.0, []);
            return sp;
        }

        [TestMethod]
        public void DeepCopy_KeepsTheSoilPiles()
        {
            var input = new InputModel { ElementDivision = new ElementDivision() };
            input.ElementDivision.SoilPiles.Add(MakeSoilPile());
            input.ElementDivision.SoilPiles.Add(MakeSoilPile());

            var copy = input.DeepCopy();

            Assert.AreEqual(2, copy?.ElementDivision?.SoilPiles?.Count ?? -1,
                "複製に土層-杭セットが入っていません (Undo すると杭要素分割が消えます)");
        }

        [TestMethod]
        public void DeepCopy_KeepsTheSinglePileSettlementCurve()
        {
            var sp = MakeSoilPile();
            sp.LoadDisplacements.Add(
                new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement
                { PileTopLoad = 1000.0, DD0s = 2.5 });
            sp.LoadDisplacementsLimit.Add(
                new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement
                { PileTopLoad = 2000.0, DD0s = 9.0 });

            var input = new InputModel { ElementDivision = new ElementDivision() };
            input.ElementDivision.SoilPiles.Add(sp);

            var copy = input.DeepCopy();
            var copied = copy!.ElementDivision.SoilPiles[0];

            Assert.AreEqual(1, copied.LoadDisplacements.Count,
                "単杭沈下の荷重-沈下曲線が複製に入っていません");
            Assert.AreEqual(2.5, copied.LoadDisplacements[0].DD0s, 1e-12);
            Assert.AreEqual(1, copied.LoadDisplacementsLimit.Count,
                "極限側の曲線が複製に入っていません");
        }

        /// <summary>
        /// 複製は<b>同じ土層-杭セットを指す</b>こと (複製しない)。
        ///
        /// 土層-杭セットは解析の入力そのもので、水平地盤反力・杭周鉛直力・荷重-沈下曲線と
        /// 派生した値を大量に抱えている。手書きの複製はそれらを完全には写せず
        /// (<c>PileZDataItem.DeepCopy</c> は 3 つのプロパティを落とす)、
        /// 写し損ねると<b>地盤ばねが 0 の解析モデル</b>ができあがる。
        /// </summary>
        [TestMethod]
        public void DeepCopy_SharesTheSoilPileInstances()
        {
            var sp = MakeSoilPile();
            var input = new InputModel { ElementDivision = new ElementDivision() };
            input.ElementDivision.SoilPiles.Add(sp);

            var copy = input.DeepCopy();

            Assert.AreSame(sp, copy!.ElementDivision.SoilPiles[0],
                "土層-杭セットを複製しています。写し損ねた値で解析されます");
        }

        /// <summary>
        /// 途中まで組んだモデル (下位のコレクションが空) でも複製で落ちないこと。
        /// Undo のたびに全 SoilPile がこの経路を通る。
        /// </summary>
        [TestMethod]
        public void DeepCopy_DoesNotThrowOnAHalfBuiltSoilPile()
        {
            var input = new InputModel { ElementDivision = new ElementDivision() };
            input.ElementDivision.SoilPiles.Add(MakeSoilPile());

            var copy = input.DeepCopy();

            Assert.IsNotNull(copy, "複製に失敗しました");
            Assert.AreEqual(1, copy!.ElementDivision.SoilPiles.Count);
        }
    }
}
