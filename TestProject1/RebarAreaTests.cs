using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 異形棒鋼の公称断面積は JIS G 3112 の値で、1 か所 (RebarAreas) から引くこと (2026-11-23 利用者の判断)。
    ///
    /// <para>以前は同じ表の写しが 4 か所にあり、主筋・杭頭・材料モデルは RC 規準の表の丸めた値 (D25 = 507)、
    /// キャプリングパイルだけ JIS の公称値 (D25 = 506.7) で、同じ呼び名の断面積が割れていた。</para>
    /// </summary>
    [TestClass]
    public class RebarAreaTests
    {
        [DataTestMethod]
        [DataRow("D10", 71.33)]
        [DataRow("D13", 126.7)]
        [DataRow("D16", 198.6)]
        [DataRow("D19", 286.5)]
        [DataRow("D22", 387.1)]
        [DataRow("D25", 506.7)]
        [DataRow("D29", 642.4)]
        [DataRow("D32", 794.2)]
        [DataRow("D35", 956.6)]
        [DataRow("D38", 1140.0)]
        [DataRow("D41", 1340.0)]
        [DataRow("D51", 2027.0)]
        public void TheAreaIsTheJisNominalValue(string size, double area)
            => Assert.AreEqual(area, RebarAreas.Of(size), 0.0);

        /// <summary>主筋・杭頭・材料モデル・キャプリングパイルの 4 つの入口が同じ値を返す (写しが残っていない)。</summary>
        [TestMethod]
        public void EveryEntryPointReadsTheSameTable()
        {
            var sizes = RebarAreas.Sizes.ToList();
            Assert.IsTrue(sizes.Count >= 12, "(前提) 表の呼び名");
            foreach (var size in sizes)
            {
                double expected = RebarAreas.Of(size);
                Assert.AreEqual(expected, PileSection.GetBarArea(size), 0.0, $"{size}: 主筋 (PileSection)");
                Assert.AreEqual(expected, PileTop.GetBarArea(size), 0.0, $"{size}: 杭頭 (PileTop)");
                Assert.AreEqual(expected, CapringPile.GetBarArea(size), 0.0, $"{size}: キャプリングパイル");
                Assert.AreEqual(3 * expected, new MainBars(1000, 3, "SD345", size).Ag, 1e-9, $"{size}: 材料モデル (MainBars)");
            }
            Assert.AreEqual(0.0, RebarAreas.Of("D99"), 0.0, "表に無い呼び名は 0");
            Assert.AreEqual(506.7, CapringPile.GetBarArea(" d25 "), 0.0, "キャプリングパイルは前後の空白と大文字小文字を許す (以前どおり)");
        }
    }
}
