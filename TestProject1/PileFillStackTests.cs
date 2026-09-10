using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 杭体の塗りを<b>1 本ぶん積み重ねた</b>ときに穴が空かないこと。
    ///
    /// <see cref="PileFillBandTests"/> は帯 1 つだけを見ていた。実機では
    /// 杭 1 本の全要素を 1 つの <c>PathGeometry</c> に積むので、帯どうしが重なる。
    /// <c>FillRule.Nonzero</c> は巻き数の和なので、<b>巻き方向が逆の帯が 1 つ混ざると
    /// 重なった相手を打ち消して穴になる</b>。
    ///
    /// 逆向きの帯は実際に作られる。帯の下端は
    /// <c>z2 = Math.Max(zs[i + 1], zToeTop)</c> なので、根固め部より上に
    /// 切り上がる要素では<b>下端が上端より上に来る</b>。
    /// </summary>
    [TestClass]
    public class PileFillStackTests
    {
        private static readonly MethodInfo Band = typeof(PileDesign.Views.MainWindow).GetMethod(
            "AddPileFillBand", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static void AddBand(PathGeometry path, Point top, Point bottom, double rx, double ry)
            => Band.Invoke(null, [path, top, rx, ry, bottom, rx, ry]);

        /// <summary>
        /// 実機の帯の作り方をそのまま真似る。zs は上から下へ (Z は上が大きい)。
        /// </summary>
        private static PathGeometry StackForPile(IReadOnlyList<double> zs, double zToeTop,
            double scale, double flattening, double pileDia)
        {
            // 画面座標: Y は下向き。Z=0 を Y=100 に置く
            static double ToY(double z, double scale) => 100.0 + (0.0 - z) * scale;

            var path = new PathGeometry { FillRule = FillRule.Nonzero };
            double rx = pileDia * scale * 0.5;
            double ry = rx * flattening;

            for (int i = 0; i < zs.Count - 1; i++)
            {
                double z1 = zs[i];
                double z2 = Math.Max(zs[i + 1], zToeTop);
                AddBand(path,
                    new Point(200, ToY(z1, scale)),
                    new Point(200, ToY(z2, scale)), rx, ry);
            }
            return path;
        }

        /// <summary>
        /// 要素が短く密集していても、杭の内側がすべて塗られること。
        /// 実機では要素分割で 0.5m 程度の要素が並ぶ。
        /// </summary>
        [TestMethod]
        public void ManyShortElements_LeaveNoHole()
        {
            // 杭長 10m を 0.5m ずつに分割。zToeTop は真の先端 (切り上げなし)
            var zs = Enumerable.Range(0, 21).Select(k => -0.5 * k).ToList();
            var path = StackForPile(zs, zs[^1], scale: 20.0, flattening: 0.3, pileDia: 1.0);

            var holes = Probe(path, 200, 100, 100 + 10.0 * 20.0);

            Assert.AreEqual(0, holes.Count,
                "短い要素を積んだ杭の内側に塗られていない点があります: "
                + string.Join(", ", holes.Take(8).Select(p => $"({p.X:0.#},{p.Y:0.#})")));
        }

        /// <summary>
        /// 根固め部で下端が切り上がる要素が混ざっても、穴が空かないこと。
        ///
        /// 既製杭の埋込み杭では zToeTop = 先端 + 根固め部径 × 高さ径比 なので、
        /// <b>最下の数要素は下端が上端より上に来る</b>。その帯は巻き方向が逆になり、
        /// Nonzero では上の要素の帯を打ち消す。
        /// </summary>
        [TestMethod]
        public void ElementsClippedByTheToeBulb_LeaveNoHole()
        {
            // 杭長 10m を 0.5m 刻み。根固め部の頂点は先端の 2.0m 上
            var zs = Enumerable.Range(0, 21).Select(k => -0.5 * k).ToList();
            double zToeTop = zs[^1] + 2.0;
            var path = StackForPile(zs, zToeTop, scale: 20.0, flattening: 0.3, pileDia: 1.0);

            // 塗る範囲は杭頭 (z=0) から zToeTop まで
            var holes = Probe(path, 200, 100, 100 + (0.0 - zToeTop) * 20.0);

            Assert.AreEqual(0, holes.Count,
                "根固め部で切り上がる要素が、上の要素の塗りを打ち消して穴にしています "
                + $"({holes.Count} 点): "
                + string.Join(", ", holes.Take(10).Select(p => $"({p.X:0.#},{p.Y:0.#})")));
        }

        /// <summary>軸の左右 ±60% の範囲を 2px 刻みで探る。</summary>
        private static List<Point> Probe(PathGeometry path, double cx, double yTop, double yBottom)
        {
            var holes = new List<Point>();
            for (double y = yTop + 1; y <= yBottom - 1; y += 2.0)
                for (double dx = -6; dx <= 6; dx += 3)
                {
                    var p = new Point(cx + dx, y);
                    if (!path.FillContains(p)) holes.Add(p);
                }
            return holes;
        }
    }
}
