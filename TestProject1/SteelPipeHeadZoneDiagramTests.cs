using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Views.Controls;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestProject1
{
    /// <summary>
    /// 杭断面ウィンドウの、鋼管杭の杭頭部の区間の図 (l1・l2・l3 と EI1〜EI3。「基礎部材の強度と変形性能」図 8.3 にならう)。
    /// 利用者の要望 (2026-11-21): EI3 がどこの剛性か、文章だけでは分かりにくい。
    ///
    /// <para>図の区間は計算 (合成剛性 EIeq) と同じ定義・同じ打ち切りで決める。食い違うと、図で見た区間と計算の区間が違ってしまう。</para>
    /// </summary>
    [TestClass]
    public class SteelPipeHeadZoneDiagramTests
    {
        [TestMethod]
        public void TheZonesFollowTheFormula()
        {
            // D = 800: L = 4D = 3200。l1 未入力は D、l2 = 400 なら l3 = 3200 − 800 − 400
            var z = SteelPipeHeadZoneDiagram.Zones(800, 0, 400);
            Assert.AreEqual((800.0, 400.0, 2000.0, 3200.0, true), z);

            // l2 = 0 は厚部なし
            Assert.AreEqual((1000.0, 0.0, 2200.0, 3200.0, false), SteelPipeHeadZoneDiagram.Zones(800, 1000, 0));

            // l1 が L を超えたら L 全体が充填部 (計算も L で打ち切る)
            Assert.AreEqual((3200.0, 0.0, 0.0, 3200.0, false), SteelPipeHeadZoneDiagram.Zones(800, 5000, 400));

            // l1 + l2 が L を超えたら l2 を L − l1 まで
            Assert.AreEqual((2000.0, 1200.0, 0.0, 3200.0, false), SteelPipeHeadZoneDiagram.Zones(800, 2000, 3000));

            // 杭径が無ければ描かない
            Assert.AreEqual(0.0, SteelPipeHeadZoneDiagram.Zones(0, 0, 0).L);
        }

        /// <summary>
        /// 杭断面ウィンドウの入力タブは縦に長くなりうるので、タブの中身はスクロールできる (利用者の要望 2026-11-21)。
        /// 入力タブ (InputTab) の直下が ScrollViewer であること。
        /// </summary>
        [TestMethod]
        public void TheInputTabScrolls()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "PileSectionWindow.xaml");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(xaml,
                    @"<avalonDock:LayoutAnchorable x:Name=""InputTab""[^>]*>\s*(<!--.*?-->\s*)?<ScrollViewer x:Name=""InputTabScroll""[^>]*VerticalScrollBarVisibility=""Auto""",
                    System.Text.RegularExpressions.RegexOptions.Singleline),
                "杭断面ウィンドウの入力タブがスクロールできません (中身が ScrollViewer で包まれていない)");
            // 杭頭部の区間の図は 2026-11-21 に杭体ウィンドウへ移した (HeadZoneSegmentsTests.TheInputsLiveInThePileBodyWindow)
        }

        /// <summary>入力の組み合わせ (未入力・打ち切り・厚部なし) で、描画が例外なく終わり、何かを描く。</summary>
        [TestMethod]
        public void ItRendersForEveryCase()
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                foreach (var (d, l1, l2) in new[] { (800.0, 0.0, 400.0), (800.0, 1000.0, 0.0), (800.0, 5000.0, 400.0), (800.0, 2000.0, 3000.0), (0.0, 0.0, 0.0) })
                {
                    var diagram = new SteelPipeHeadZoneDiagram { PileDiameter = d, FillLength = l1, ThickPartLength = l2 };
                    diagram.Measure(new Size(diagram.Width, diagram.Height));
                    diagram.Arrange(new Rect(0, 0, diagram.Width, diagram.Height));
                    var bitmap = new RenderTargetBitmap((int)diagram.Width, (int)diagram.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(diagram);
                    var pixels = new byte[(int)diagram.Width * (int)diagram.Height * 4];
                    bitmap.CopyPixels(pixels, (int)diagram.Width * 4, 0);
                    Assert.IsTrue(Array.Exists(pixels, b => b != 0), $"D = {d}, l1 = {l1}, l2 = {l2} で何も描いていない");
                }
            }, out bool timedOut);
            Assert.IsFalse(timedOut, "時間切れになりました");
            if (ex != null) throw new AssertFailedException(ex.ToString(), ex);
        }
    }
}
