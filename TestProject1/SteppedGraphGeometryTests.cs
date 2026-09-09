using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 地盤ウィンドウの階段状グラフ (N 値・Cu・Vs・Es) の座標づくり。
    ///
    /// 折れ線 <c>GetSteppedData</c> と半透明矩形 <c>GetRectangleGeometry</c> は
    /// <b>同じ 2 本のリストを続けて渡して重ねて描く</b>ので、片方だけが空や
    /// null を許すと、折れ線は描けて矩形で落ちる、という壊れ方をします。
    /// 描画だけを扱うので、地盤ウィンドウを開かずにここで確かめます。
    ///
    /// 深さは下向き正 (GL 基準深さ)。1 層目は地表 (0) から始まり、
    /// 2 層目以降は前の層の下端から始まります。
    /// </summary>
    [TestClass]
    public class SteppedGraphGeometryTests
    {
        [TestMethod]
        public void TheStep_StartsAtTheGroundSurface_AndHoldsEachValueOverItsLayer()
        {
            // 1 層目: N=10, 下端 GL-3m / 2 層目: N=25, 下端 GL-8m
            var values = new List<double> { 10, 25 };
            var bottoms = new List<double> { 3, 8 };

            var (x, y) = GroundLayerViewModel.GetSteppedData(values, bottoms);

            var pts = x.Zip(y, (a, b) => (a, b)).ToList();

            CollectionAssert.AreEqual(
                new[]
                {
                    (0.0, 0.0),     // 地表の軸上から
                    (10.0, 0.0),    // 1 層目の値まで水平に
                    (10.0, 3.0),    // 1 層目の下端まで垂直に
                    (25.0, 3.0),    // 2 層目の値まで水平に
                    (25.0, 8.0),    // 2 層目の下端まで垂直に
                    (0.0, 8.0),     // 軸まで戻す (塗りつぶしを閉じる)
                    (25.0, 8.0),
                },
                pts,
                "階段の折れ方が変わっています。値は層の途中で変わらず、"
                + "層境界でだけ変わるべきです");
        }

        [TestMethod]
        public void TheRectangles_SpanEachLayerFromItsTopToItsBottom()
        {
            var values = new List<double> { 10, 25 };
            var bottoms = new List<double> { 3, 8 };

            var rects = GroundLayerViewModel.GetRectangleGeometry(values, bottoms);

            Assert.AreEqual(2, rects.Count, "矩形は層の数だけ出るべきです");

            Assert.AreEqual(0.0, rects[0].Top, 1e-12, "1 層目の上端は地表です");
            Assert.AreEqual(3.0, rects[0].Bottom, 1e-12);
            Assert.AreEqual(0.0, rects[0].Left, 1e-12, "矩形は軸から伸ばします");
            Assert.AreEqual(10.0, rects[0].Right, 1e-12);

            Assert.AreEqual(3.0, rects[1].Top, 1e-12,
                "2 層目の上端は 1 層目の下端です。ここが 0 のままだと矩形が重なります");
            Assert.AreEqual(8.0, rects[1].Bottom, 1e-12);
            Assert.AreEqual(25.0, rects[1].Right, 1e-12);
        }

        [TestMethod]
        public void ASingleLayer_StillClosesBackToTheAxis()
        {
            var (x, y) = GroundLayerViewModel.GetSteppedData(
                new List<double> { 7 }, new List<double> { 4 });

            var pts = x.Zip(y, (a, b) => (a, b)).ToList();
            CollectionAssert.AreEqual(
                new[] { (0.0, 0.0), (7.0, 0.0), (7.0, 4.0), (0.0, 4.0), (7.0, 4.0) }, pts);
        }

        /// <summary>
        /// 両方が空や null を同じように受けること。
        /// 折れ線側だけがガードしていて、矩形側は <c>originalX.Count</c> を
        /// いきなり読んでいました。同じ引数を続けて渡す使い方なので、
        /// 片側だけのガードは「折れ線は出たのに落ちる」形になります。
        /// </summary>
        [TestMethod]
        public void BothSides_HandleTheSameEmptyInputTheSameWay()
        {
            var empty = new List<double>();
            var some = new List<double> { 1, 2 };

            foreach (var (xs, ys, what) in new (List<double>?, List<double>?, string)[]
                     {
                         (null, null, "両方 null"),
                         (empty, empty, "両方 空"),
                         (some, empty, "深さだけ空"),
                         (empty, some, "値だけ空"),
                         (null, some, "値だけ null"),
                         (some, null, "深さだけ null"),
                     })
            {
                var (sx, sy) = GroundLayerViewModel.GetSteppedData(xs!, ys!);
                Assert.AreEqual(0, sx.Count, $"{what}: 折れ線が空になっていません");
                Assert.AreEqual(0, sy.Count, $"{what}: 折れ線が空になっていません");

                var rects = GroundLayerViewModel.GetRectangleGeometry(xs!, ys!);
                Assert.AreEqual(0, rects.Count, $"{what}: 矩形が空になっていません");
            }
        }

        /// <summary>
        /// リストの長さが食い違っても落ちないこと。呼び出し側は必ず同じ
        /// 土層コレクションから両方を作るので今は揃っていますが、揃えるのは
        /// 呼び出し側 4 か所の約束にすぎず、崩れると描画が例外で止まります。
        /// </summary>
        [TestMethod]
        public void MismatchedLengths_DoNotThrow()
        {
            var rects = GroundLayerViewModel.GetRectangleGeometry(
                new List<double> { 10, 25, 40 }, new List<double> { 3, 8 });

            Assert.AreEqual(2, rects.Count, "短いほうに合わせるべきです");
        }
    }
}
