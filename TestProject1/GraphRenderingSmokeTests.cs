using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScottPlot; // Multiplot.AddPlots / SavePng は拡張メソッドのため必須
using System;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// グラフ描画スタック (ScottPlot → SkiaSharp → HarfBuzzSharp) の実行時スモークテスト。
    ///
    /// これらのライブラリはコンパイル時には型が解決できても、依存バージョンが食い違うと
    /// 描画呼び出し時に初めて MissingMethodException / TypeLoadException で落ちる。
    /// 通常の単体テストは計算ロジックのみを検証しており描画経路を通らないため、
    /// パッケージ更新時の破壊を検出できるようここで実際に PNG を生成する。
    /// </summary>
    [TestClass]
    public class GraphRenderingSmokeTests
    {
        /// <summary>
        /// 計算書 (WordDocument.AddNMinTScottPlotGraphToBody) と同じ Multiplot → SavePng の経路。
        /// </summary>
        [TestMethod]
        public void ScottPlot_Multiplot_SavePng_Succeeds()
        {
            var multiplot = new ScottPlot.Multiplot();
            multiplot.AddPlots(1);
            var plot = multiplot.Subplots.GetPlot(0);

            // 線・散布点・補助線 — 計算書グラフで実際に使う要素をひととおり
            var line = plot.Add.ScatterLine(new double[] { 0, 1, 2 }, new double[] { 0, 10, 15 });
            line.LegendText = "曲線";
            line.LinePattern = ScottPlot.LinePattern.Dashed;

            var scatter = plot.Add.Scatter(new double[] { 1 }, new double[] { 10 });
            scatter.LegendText = "レベル1";
            scatter.MarkerSize = 6;

            plot.Add.VerticalLine(0, 1, new ScottPlot.Color(128, 128, 128, 255));
            plot.Add.HorizontalLine(0, 1, new ScottPlot.Color(128, 128, 128, 255));

            // 日本語ラベル (HarfBuzzSharp のテキストシェーピングを経由する)
            plot.Axes.Bottom.Label.Text = "曲率φ[rad/m]";
            plot.Axes.Left.Label.Text = "曲げモーメント[kNm]";
            plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect("曲率");
            plot.ShowLegend();
            plot.Axes.AutoScale();

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            try
            {
                multiplot.SavePng(tempFile, 600, 600);

                Assert.IsTrue(File.Exists(tempFile), "PNG が生成されていない");
                var info = new FileInfo(tempFile);
                Assert.IsTrue(info.Length > 1000,
                    $"PNG が小さすぎる ({info.Length} bytes) — 描画が空の可能性");
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* 後始末の失敗は無視 */ }
            }
        }

        /// <summary>
        /// SkiaSharp を直接使う経路 (NikkenSKColor 等の色定義・サーフェス描画) が動くこと。
        /// </summary>
        [TestMethod]
        public void SkiaSharp_Surface_Draw_And_Encode_Succeeds()
        {
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(64, 64));
            Assert.IsNotNull(surface, "SKSurface を生成できない");

            var canvas = surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.White);
            using var paint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(200, 60, 60), IsAntialias = true };
            canvas.DrawCircle(32, 32, 20, paint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            Assert.IsTrue(data.Size > 100, $"PNG エンコード結果が小さすぎる ({data.Size} bytes)");
        }
    }
}
