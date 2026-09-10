using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 要素縮小モードの縮め方と、対応している描画。
    ///
    /// 基礎梁は端点を、杭は要素の Z の範囲を縮めます。どちらも投影の前に縮めますが、
    /// <b>縮める割合が食い違うと梁と杭で
    /// 隙間の大きさが変わり、同じ図の中でちぐはぐに見えます。</b>
    ///
    /// 縮めるのは<b>要素だけ</b>です。節点と杭先端は本当の位置に残します。
    /// これらは要素ではなく、位置そのものに意味があるためです
    /// (基礎梁でも節点は縮めていません)。
    ///
    /// 節杭の節は<b>要素の一部</b>なので、帯と一緒に縮めます。帯だけ縮めると
    /// 節が帯からはみ出して別物のように見えるためです。
    /// </summary>
    [TestClass]
    public class ShrinkElementModeTests
    {
        private static string CanvasElements()
            => TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasElements.cs");

        /// <summary>
        /// 梁と杭で、縮める割合の既定が同じであること。
        /// 片方だけ変えると、同じ図の中で隙間の大きさが変わる。
        /// </summary>
        [TestMethod]
        public void BeamsAndPiles_ShrinkByTheSameAmount()
        {
            string src = CanvasElements();

            var factors = Regex.Matches(src, @"double factor = ([0-9.]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            TestSource.AssertScanned(factors.Count, 1, "縮小率の既定");
            Assert.AreEqual(1, factors.Count,
                "梁と杭で縮小率の既定が違います。同じ図の中で隙間の大きさが変わります: "
                + string.Join(", ", factors));
        }

        /// <summary>
        /// 縮小の式が、両端から同じだけ詰める形であること。
        /// 梁 (端点) と杭 (Z の範囲) で、0.8 のとき中央 80% が残る。
        /// </summary>
        [TestMethod]
        public void TheShrink_KeepsTheMiddleAndIsSymmetric()
        {
            // 杭側の式をそのまま再現する (ShrinkSpan は private)
            static (double a, double b) Shrink2D(double top, double bottom, double factor)
            {
                double keep = 0.5 * (1.0 - factor);
                double d = bottom - top;
                return (top + d * keep, bottom - d * keep);
            }
            // 3D 側の式 (GetShrinkElementPoints) を 1 次元で再現する
            static (double a, double b) Shrink3D(double p0, double p1, double factor)
            {
                double factorV = 0.5 + 0.5 * factor;
                double v = p1 - p0;
                return (p1 - factorV * v, p0 + factorV * v);
            }

            foreach (double factor in new[] { 0.8, 0.5, 1.0 })
            {
                var (a2, b2) = Shrink2D(0.0, 10.0, factor);
                var (a3, b3) = Shrink3D(0.0, 10.0, factor);

                Assert.AreEqual(a3, a2, 1e-12, $"factor={factor}: 上端の縮め方が 3D 側と違います");
                Assert.AreEqual(b3, b2, 1e-12, $"factor={factor}: 下端の縮め方が 3D 側と違います");

                Assert.AreEqual(5.0, (a2 + b2) / 2.0, 1e-12, $"factor={factor}: 中央がずれています");
                Assert.AreEqual(10.0 * factor, b2 - a2, 1e-12,
                    $"factor={factor}: 残る長さが factor 倍になっていません");
            }
        }

        /// <summary>
        /// 杭の要素の帯が縮小モードを見ていること。
        /// 見ていなかったので基礎梁だけが縮み、杭は縮まなかった。
        /// </summary>
        [TestMethod]
        public void ThePileBand_FollowsTheMode()
        {
            string src = CanvasElements();

            Assert.IsTrue(src.Contains("ShrinkSpan"),
                "杭の帯を縮める処理がありません");

            int at = src.IndexOf("AddPileSectionGeometry(bandTop, bandBottom", StringComparison.Ordinal);
            Assert.IsTrue(at > 0,
                "杭の要素の帯が、縮めた端点で描かれていません "
                + "(AddPileSectionGeometry に縮小後の点を渡すこと)");

            // 手前で縮小モードを見ていること
            string before = src[Math.Max(0, at - 900)..at];
            StringAssert.Contains(before, "IsShrinkElementMode",
                "杭の帯が縮小モードを見ずに縮んでいます");
        }

        /// <summary>
        /// 節杭の節が、帯と同じ縮んだ Z から描かれること。
        /// 帯だけ縮めると、節が帯からはみ出して別物のように見える。
        /// </summary>
        [TestMethod]
        public void TheNodularBulges_ShrinkWithTheElement()
        {
            string src = CanvasElements();

            int at = src.IndexOf("AddNodularPilePositionGeometry(", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "節杭の節の描画が見つかりません");

            // 呼び出しの引数に、縮めた Z が渡っていること
            string call = src[at..Math.Min(src.Length, at + 240)];
            StringAssert.Contains(call, "bandZ1",
                "節杭の節が、帯と違う Z から描かれています。"
                + "帯だけ縮めると節がはみ出します");
            StringAssert.Contains(call, "nodularZ2",
                "節杭の節の下端が縮んでいません");
        }

        /// <summary>
        /// 杭先端の円柱は縮めないこと。要素ではなく、位置そのものに意味がある。
        /// </summary>
        [TestMethod]
        public void ThePileToe_IsNotShrunk()
        {
            string src = CanvasElements();

            int at = src.IndexOf("AddPileSectionGeometry(ptCylTop, ptCylBtm", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "杭先端の円柱の描画が見つかりません");

            string before = src[Math.Max(0, at - 600)..at];
            Assert.IsFalse(before.Contains("ShrinkSpan"),
                "杭先端の円柱まで縮めています。先端は要素ではないので、"
                + "本当の位置に残すこと");
        }
    }
}
