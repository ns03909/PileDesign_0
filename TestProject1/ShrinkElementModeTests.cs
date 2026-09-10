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
        /// 応力図も要素縮小モードに追従すること。
        ///
        /// 縮めるのは端点だけで、値は端の値のまま描く。縮めた位置の補間値ではないので、
        /// 図は要素とぴったり揃うかわりに、折れ線の両端が実際の節点位置から少し
        /// 内側にずれる。
        ///
        /// 追従させたのは応力図と節点変位図。<b>変形後形状は縮めていない</b>
        /// (折れ線の連続性そのものに意味があるため)。
        /// </summary>
        [TestMethod]
        public void TheForceDiagram_FollowsTheMode()
        {
            var files = new[]
            {
                ("MainWindow.CanvasResults.cs", 2),        // 応力図と節点変位図
                ("MainWindow.CanvasResultsForces.cs", 2),  // 基礎梁の Mh / Fh
            };

            int scanned = 0;
            foreach (var (name, expected) in files)
            {
                string src = TestSource.Read("Graphics_r1", "Views", name);
                int n = Regex.Matches(src,
                    @"IsShrinkElementMode[\s\S]{0,120}?GetShrinkElementPoints\(nodeI3D, nodeJ3D\)").Count;
                scanned += n;
                Assert.AreEqual(expected, n,
                    $"{name} で応力図が縮小モードに追従している箇所が {n} 件です "
                    + $"({expected} 件のはず)。端点を縮めてから図を作ること");
            }

            TestSource.AssertScanned(scanned, 4, "応力図・節点変位図の縮小追従");
        }

        /// <summary>
        /// 変形後形状も要素縮小モードに追従すること。
        ///
        /// 変形後形状は Hermite 補間で要素内の点列を作るので、縮めるのは<b>媒介変数</b>。
        /// 点列を作ってから両端を捨てるのではないので、端が刻み幅に量子化しない。
        /// </summary>
        [TestMethod]
        public void TheDeformedShape_FollowsTheMode()
        {
            string hermite = TestSource.Read("Graphics_r1", "Common", "HermiteBeamInterpolation.cs");

            // 刻みが sFrom〜sTo の範囲になっていること (0〜1 決め打ちでない)
            StringAssert.Contains(hermite, "sFrom + (sTo - sFrom)",
                "Hermite 補間の刻みが要素の全長に固定されています。"
                + "範囲を渡せないと変形後形状を分節できません");

            string src = TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsDeformed.cs");

            StringAssert.Contains(src, "DeformedShrinkRange",
                "変形後形状の縮小範囲を決める処理がありません");

            // 縮小率は要素の形状・応力図と同じ ShrinkSpan を通すこと。
            // 別に書くと同じ図の中で隙間の大きさが食い違う
            StringAssert.Contains(src, "ShrinkSpan(0.0, 1.0)",
                "変形後形状が ShrinkSpan を通っていません。"
                + "縮小率を別に持つと、要素の形状と隙間の大きさが食い違います");

            // 3 種類 (基礎梁の断面・杭の断面・中心線) すべてに範囲が渡っていること
            int wired = Regex.Matches(src, @"GetDeformedPoints\([\s\S]{0,200}?sFrom: sFrom, sTo: sTo").Count;
            TestSource.AssertScanned(wired, 3, "変形後形状の縮小追従");
            Assert.AreEqual(3, wired,
                $"変形後形状で縮小範囲を渡している箇所が {wired} 件です (3 件のはず: "
                + "基礎梁の断面・杭の断面・中心線)");
        }

        /// <summary>
        /// 杭の変形後輪郭が、要素ごとの区間に分かれること。
        ///
        /// 杭の輪郭は<b>杭 1 本ぶんを 1 本の連続した点列に連結して</b>張っている。
        /// そのままだと縮めても隙間をまたぐ線分が張られ、隙間が埋まって
        /// 縮めていないのと同じ見え方になる。
        /// </summary>
        [TestMethod]
        public void TheDeformedPileOutline_DoesNotBridgeTheGaps()
        {
            string src = TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsDeformed.cs");

            StringAssert.Contains(src, "runStarts",
                "杭の変形後輪郭に区間の区切りがありません");
            StringAssert.Contains(src, "runStarts.Contains(k + 1)",
                "杭の変形後輪郭が区間をまたいで線分を張っています。"
                + "隙間が埋まって、縮めていないのと同じ見え方になります");

            // 縮小モードでは 2 要素目以降も始点を落とさないこと (落とすと 1 点短くなる)
            StringAssert.Contains(src, "isFirstBeam || viewModel.IsShrinkElementMode",
                "縮小モードでも 2 要素目以降の始点を落としています。"
                + "節点を共有しないので、落とすと要素が 1 点短くなります");
        }

        /// <summary>
        /// ツールチップの当たり判定が、縮小モードでも描いてあるところに合うこと。
        ///
        /// 判定を縮める前の座標で取ると、隙間 (描かれていない場所) を指しても値が出る。
        /// さらに図は「端点を縮めて値は端の値のまま」描くので、縮めた端では
        /// <b>図と値が食い違う</b> (同じ場所を指しているのに t が 0.1 ずれる)。
        /// </summary>
        [TestMethod]
        public void TheTooltipHitTest_FollowsTheMode()
        {
            string src = TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsTooltips.cs");

            int n = Regex.Matches(src,
                @"IsShrinkElementMode[\s\S]{0,120}?GetShrinkElementPoints\(").Count;

            TestSource.AssertScanned(n, 4, "ツールチップの当たり判定の縮小追従");
            Assert.AreEqual(4, n,
                $"当たり判定が縮小モードに追従している箇所が {n} 件です (4 件のはず: "
                + "応力・変位 / 杭要素の検定比 / 基礎梁の沈下 / 部材角の色分け)");
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
