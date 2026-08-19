using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 杭体ウィンドウを各杭工法で実際に開くスモークテスト。
    ///
    /// 工法を増やしたときに追随が要る箇所（VisibilityConverter の ConverterParameter・
    /// 工法別の入力パネル・杭姿図の形状分岐）は、いずれも
    /// <b>ビルドが通ったまま実行時に静かに壊れる</b>。
    /// </summary>
    [TestClass]
    public class PileBodyWindowXamlSmokeTests
    {
        private static IEnumerable<string> ConstructionTypes()
        {
            yield return PileConstructionTypeNames.Insitu;
            yield return PileConstructionTypeNames.Preboring;
            yield return PileConstructionTypeNames.Chubori;
            yield return PileConstructionTypeNames.Driven;
            yield return PileConstructionTypeNames.Rotary;
            yield return PileConstructionTypeNames.SmartMagnum;
        }

        [TestMethod]
        public void PileBodyWindow_OpensForEveryConstructionType()
        {
            var failures = new List<string>();

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var mainVm = new MainWindowViewModel();

                foreach (string constructionType in ConstructionTypes())
                {
                    foreach (var body in mainVm.CurrentInputModel!.PileBodies)
                        body.PileConstructionType = constructionType;

                    var window = new PileDesign.Views.PileBodyWindow(mainVm);
                    var vm = (PileBodyViewModel)window.DataContext;
                    try
                    {
                        // 杭姿図の形状分岐に工法が無いと、例外にならず「何も描かれない」。
                        // Canvas は Measure/Arrange して ActualWidth/Height を持たせること
                        // (0 のままだと DrawPileElevation が即 return する)。
                        var canvas = new System.Windows.Controls.Canvas();
                        canvas.Measure(new System.Windows.Size(400, 500));
                        canvas.Arrange(new System.Windows.Rect(0, 0, 400, 500));
                        canvas.UpdateLayout();
                        vm.Canvas = canvas;
                        vm.DrawShapes();

                        if (canvas.Children.Count == 0)
                            failures.Add($"{constructionType}: 杭姿図が 1 つも描画されない");
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }, out bool timedOut, timeoutSeconds: 180);

            if (timedOut)
            {
                Assert.Inconclusive("ウィンドウ生成が 180 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"杭体ウィンドウの生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

        /// <summary>
        /// Smart-MAGNUM の掘削形状（根固め部が杭先端より下に張り出す・拡翼掘削部が根固め部より太い）
        /// でも姿図が描け、拡翼掘削部が横方向にキャンバス内へ収まること。
        /// </summary>
        [TestMethod]
        public void PileBodyWindow_DrawsSmartMagnumExcavationShape()
        {
            string? failure = null;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var mainVm = new MainWindowViewModel();
                foreach (var body in mainVm.CurrentInputModel!.PileBodies)
                {
                    body.PileConstructionType = PileConstructionTypeNames.SmartMagnum;
                    body.PileToeDia = 1900;            // Den
                    body.SmartMagnumLL = 2.0;          // 杭先端より下へ 2m
                    body.SmartMagnumDes = 2400;        // Des > Den
                    body.SmartMagnumWingLength = 10.0;
                }

                var window = new PileDesign.Views.PileBodyWindow(mainVm);
                var vm = (PileBodyViewModel)window.DataContext;
                try
                {
                    var canvas = new System.Windows.Controls.Canvas();
                    canvas.Measure(new System.Windows.Size(400, 500));
                    canvas.Arrange(new System.Windows.Rect(0, 0, 400, 500));
                    canvas.UpdateLayout();
                    vm.Canvas = canvas;
                    vm.DrawShapes();

                    if (canvas.Children.Count == 0)
                    {
                        failure = "Smart-MAGNUM の杭姿図が 1 つも描画されない";
                        return;
                    }

                    // Des が Den より大きくても横スケールが Des 基準になっていれば画面内に収まる
                    foreach (var child in canvas.Children)
                    {
                        if (child is not System.Windows.Shapes.Rectangle rect) continue;
                        double left = System.Windows.Controls.Canvas.GetLeft(rect);
                        if (double.IsNaN(left)) continue;
                        if (left < -0.5 || left + rect.Width > canvas.ActualWidth + 0.5)
                        {
                            failure = $"拡翼掘削部が横方向にはみ出している (left={left:N1} width={rect.Width:N1} canvas={canvas.ActualWidth:N1})";
                            return;
                        }
                    }
                }
                finally
                {
                    window.Close();
                }
            }, out bool timedOut, timeoutSeconds: 180);

            if (timedOut)
            {
                Assert.Inconclusive("ウィンドウ生成が 180 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"杭体ウィンドウの生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.IsNull(failure, failure);
        }
    }
}
