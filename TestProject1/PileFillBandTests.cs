using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 杭体の塗り (<c>AddPileFillBand</c>) が、帯の内側をすべて塗ること。
    ///
    /// 塗りは「上端の楕円 + 下端の楕円 + その間の四角」を <c>FillRule.Nonzero</c> の
    /// 和集合として積みます。<b>巻き方向が揃っていないと、重なった部分で巻き数が 0 に
    /// なって穴が空きます</b>。穴は画面では白い帯に見えます。
    ///
    /// 実機で杭の中に白い帯が出ていたので、まず再現させるために書いています。
    /// 目で見て推測するより、WPF に「この点は塗りに含まれるか」を直接聞くほうが確実です。
    /// </summary>
    [TestClass]
    public class PileFillBandTests
    {
        /// <summary>MainWindow の private static メソッドを呼ぶ。</summary>
        private static PathGeometry Band(
            Point top, double topRx, double topRy,
            Point bottom, double bottomRx, double bottomRy)
        {
            var m = typeof(PileDesign.Views.MainWindow).GetMethod(
                "AddPileFillBand", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "AddPileFillBand が見つかりません (名前が変わった?)");

            var path = new PathGeometry { FillRule = FillRule.Nonzero };
            m!.Invoke(null, [path, top, topRx, topRy, bottom, bottomRx, bottomRy]);
            return path;
        }

        /// <summary>
        /// ふつうの円筒（上下同径、縦に十分な長さ）で、軸上の点がすべて塗られること。
        /// </summary>
        [TestMethod]
        public void AStraightCylinder_IsFilledAlongItsAxis()
        {
            var path = Band(new Point(100, 100), 20, 6, new Point(100, 300), 20, 6);

            var holes = Enumerable.Range(0, 21)
                .Select(k => new Point(100, 100 + k * 10.0))
                .Where(p => !path.FillContains(p))
                .ToList();

            Assert.AreEqual(0, holes.Count,
                "円筒の軸上に塗られていない点があります (画面では白い帯に見えます): "
                + string.Join(", ", holes.Select(p => $"({p.X},{p.Y})")));
        }

        /// <summary>
        /// 帯が短いとき（要素分割後や要素縮小モード）でも穴が空かないこと。
        /// 上下の楕円が互いに重なる状況になる。
        /// </summary>
        [TestMethod]
        public void AShortBand_HasNoHole()
        {
            // 高さ 8px に対して楕円の縦半径 6px → 上下の楕円が重なる
            var path = Band(new Point(100, 100), 20, 6, new Point(100, 108), 20, 6);

            // 探るのは確実に内側の点だけ (半径 20 に対して ±18)
            var holes = Enumerable.Range(-6, 13)
                .SelectMany(kx => Enumerable.Range(0, 9).Select(ky =>
                    new Point(100 + kx * 3.0, 100 + ky)))
                .Where(p => !path.FillContains(p))
                .ToList();

            Assert.AreEqual(0, holes.Count,
                "短い帯の内側に塗られていない点があります: "
                + string.Join(", ", holes.Take(8).Select(p => $"({p.X},{p.Y})")));
        }

        /// <summary>
        /// 円錐台（上下で径が違う）でも穴が空かないこと。杭先端の拡底部で使う形。
        /// </summary>
        [TestMethod]
        public void ATaperedBand_HasNoHole()
        {
            var path = Band(new Point(100, 100), 12, 4, new Point(100, 200), 28, 9);

            var holes = Enumerable.Range(0, 11)
                .Select(k => new Point(100, 100 + k * 10.0))
                .Where(p => !path.FillContains(p))
                .ToList();

            Assert.AreEqual(0, holes.Count,
                "円錐台の軸上に塗られていない点があります: "
                + string.Join(", ", holes.Select(p => $"({p.X},{p.Y})")));
        }

        /// <summary>
        /// 上下が入れ替わった帯（下端のほうが画面上で上にある）でも穴が空かないこと。
        ///
        /// 杭体の帯は z2 = Math.Max(zs[i+1], zToeTop) で作るので、根固め部に
        /// かかる要素では<b>下端が上端より上に来る</b>ことがある。そうなると
        /// 側面の四角の巻き方向が反転し、Nonzero で楕円と打ち消して穴になる。
        /// </summary>
        [TestMethod]
        public void AnInvertedBand_HasNoHole()
        {
            // 下端 (Y=100) が上端 (Y=160) より画面上で上にある
            var path = Band(new Point(100, 160), 20, 6, new Point(100, 100), 20, 6);

            // 打ち消しは<b>楕円と側面の四角が重なる範囲</b>で起きるので、
            // 端 (Y=100 付近と Y=160 付近) を飛ばさずに 2px 刻みで探る。
            // 10px 刻みだと四角の縁だけを踏んで、穴を見落とす。
            var holes = Enumerable.Range(-6, 13)
                .SelectMany(kx => Enumerable.Range(0, 30).Select(ky =>
                    new Point(100 + kx * 3.0, 101 + ky * 2.0)))
                .Where(p => !path.FillContains(p))
                .ToList();

            Assert.AreEqual(0, holes.Count,
                "上下が入れ替わった帯の内側に塗られていない点があります "
                + "(側面の四角の巻き方向が反転して打ち消しています): "
                + string.Join(", ", holes.Take(8).Select(p => $"({p.X},{p.Y})")));
        }

        /// <summary>
        /// 真上から見て潰れた帯（上下の中心がほぼ同じ）でも穴が空かないこと。
        /// 「上下を弧で分けて 1 枚の帯にする方法だと真上から見て破綻する」という
        /// 注記の根拠にあたる状況。
        /// </summary>
        [TestMethod]
        public void ABandSeenFromAbove_HasNoHole()
        {
            var path = Band(new Point(100, 100), 20, 20, new Point(100, 102), 20, 20);

            // 円形 (半径 20) なので、四隅ではなく中心からの距離で内側を選ぶ
            var holes = Enumerable.Range(-6, 13)
                .SelectMany(kx => Enumerable.Range(-6, 13).Select(ky =>
                    new Point(100 + kx * 3.0, 101 + ky * 3.0)))
                .Where(p => (p.X - 100) * (p.X - 100) + (p.Y - 101) * (p.Y - 101) <= 18 * 18)
                .Where(p => !path.FillContains(p))
                .ToList();

            Assert.AreEqual(0, holes.Count,
                "真上から見た帯の内側に塗られていない点があります: "
                + string.Join(", ", holes.Take(8).Select(p => $"({p.X},{p.Y})")));
        }
    }
}
