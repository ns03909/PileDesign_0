using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Common;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace TestProject1
{
    /// <summary>
    /// 節杭の杭姿図で、節の立上りを示す横線が縮尺によらず描かれることを固定する。
    ///
    /// 立上りの横線が消えると、縮小表示では節が単なる外形のギザギザにしか見えず、
    /// 節杭であることが図から読み取れなくなる。
    /// (節部の平坦面の 2 本は立上りの内側に入るため、潰れる縮尺では省略してよい)
    /// </summary>
    [TestClass]
    public class NodularPileElevationLinesTests
    {
        /// <summary>
        /// φ1200-1100 の節杭。立上り長 = (1200-1100)/2 = 50mm しかないため、
        /// 長い杭を小さなキャンバスに収めると横線どうしの間隔は数 px 未満になる。
        /// </summary>
        private static ObservableCollection<PileBodySegment> MakeNodularSegments(double segmentLength)
        {
            var section = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.PhcNodular,
                PileDiameter = 1100,
                NodeDiameter = 1200,
                // 節の配置はカタログ姿図の寸法記入値 (区間上端 600mm・ピッチ 1000mm・区間下端 400mm)。
                // 製品を選ばず手組みした断面ではこれらが 0 のままなので節が 1 つも並ばない
                NodeHeadOffset = 600,
                NodePitch = 1000,
                NodeToeOffset = 400,
            };

            return
            [
                new PileBodySegment
                {
                    No = 1,
                    SegmentLength = segmentLength,
                    SegmentDepth = segmentLength,
                    PileSection = section,
                },
            ];
        }

        private static int CountHorizontalLines(double pileLength, double canvasHeight)
        {
            int count = 0;

            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var canvas = new Canvas { Width = 300, Height = canvasHeight };
                canvas.Measure(new System.Windows.Size(300, canvasHeight));
                canvas.Arrange(new System.Windows.Rect(0, 0, 300, canvasHeight));
                canvas.UpdateLayout();

                ShapeDrawer.DrawPileElevation(
                    canvas,
                    MakeNodularSegments(pileLength),
                    pileToeDia: 1100,
                    insituPileToeHeight: 300,
                    insituPileToeAngle: 12,
                    precastConcretePileToeHeightRatio: 2.0,
                    pileConstructionType: PileConstructionTypeNames.Preboring);

                // 節の詳細線は水平 (Y1 == Y2) の Line として描かれる
                count = canvas.Children.OfType<Line>()
                    .Count(l => Math.Abs(l.Y1 - l.Y2) < 1e-6 && Math.Abs(l.X2 - l.X1) > 1e-6);
            }, out bool timedOut);

            Assert.IsFalse(timedOut, "描画が時間内に終わらなかった");
            Assert.IsNull(error, $"描画で例外が発生した: {error}");
            return count;
        }

        [TestMethod]
        public void NodeRiseLines_AreDrawnEvenWhenTheScaleIsSmall()
        {
            // 30m の杭を 400px に収める → 立上り 50mm は約 0.7px。
            // 従来はこの縮尺で横線が 1 本も描かれなかった
            int lines = CountHorizontalLines(pileLength: 30.0, canvasHeight: 400);

            Assert.IsTrue(lines > 0,
                "縮小表示で節の立上りの横線が 1 本も描かれていない");
        }

        [TestMethod]
        public void NodeFlatFaceLines_AppearOnlyWhenTheScaleAllowsThem()
        {
            // 杭長を変えると節の数も変わってしまうので、同じ杭をキャンバスの高さだけ変えて比べる。
            // 節は 30 個 (区間上端 0.6m から 1m ピッチ、区間下端 0.4m 上まで)。
            int small = CountHorizontalLines(pileLength: 30.0, canvasHeight: 400);
            int large = CountHorizontalLines(pileLength: 30.0, canvasHeight: 2400);

            Assert.AreEqual(60, small, "縮小表示では立上りの 2 本のみ (30 節 × 2)");
            Assert.AreEqual(120, large, "拡大表示では節部平坦面も加えて 4 本 (30 節 × 4)");
        }
    }
}
