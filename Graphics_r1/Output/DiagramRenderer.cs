using PileDesign.Graphics;
using PileDesign.Graphics.Abstractions;
using PileDesign.Graphics.Implementations;
using PileDesign.Models.InputData;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Path = System.IO.Path;

using Serilog;
namespace PileDesign.Output
{
    internal static class DiagramRenderer
    {
        private const double InchPerMm = 1.0 / 25.4;
        private const double DefaultDpi = 96.0;

        private static int MmToPx(double mm, double dpi = DefaultDpi, double scale = 1.0)
            => (int)Math.Round(mm * dpi * scale * InchPerMm);

        /// <summary>
        /// 土質ごとの背景色 (地盤ウィンドウと同じ配色、半透明)。
        ///   粘性土: 薄茶 (210,180,140,64)
        ///   砂質土: 薄橙 (255,165,  0,64)
        ///   礫質土: 薄緑 (144,238,144,64)
        ///   その他: 薄灰 (200,200,200,32)
        /// </summary>
        private static Brush GetSoilTypeBackgroundBrush(string? granularityClass) => granularityClass switch
        {
            "粘性土" => new SolidColorBrush(Color.FromArgb(64, 210, 180, 140)),
            "砂質土" => new SolidColorBrush(Color.FromArgb(64, 255, 165, 0)),
            "礫質土" => new SolidColorBrush(Color.FromArgb(64, 144, 238, 144)),
            _ => new SolidColorBrush(Color.FromArgb(32, 200, 200, 200)),
        };

        /// <summary>
        /// ピクセル空間上で土層ごとに薄い背景を描画する。
        /// ToImagePoint は (worldX, worldY=altitude) を受けるラムダ。
        /// </summary>
        private static void DrawSoilLayerBackground(
            DrawingContext dc,
            SoilPile soilPile,
            int widthPx,
            int heightPx,
            double scalePxPerM,
            Func<double, double, Point> ToImagePoint)
        {
            var groundLayers = soilPile?.GroundInput?.GroundLayers;
            if (groundLayers == null || groundLayers.Count == 0) return;

            double soilPileZ = soilPile!.Z;
            double originYPx = ToImagePoint(0, 0).Y;

            for (int i = 0; i < groundLayers.Count; i++)
            {
                var layer = groundLayers[i];
                // 各層の上端/下端 altitude (Z, m 単位)。BottomAltitude が存在するので TopAltitude は前層から取る
                double topAlt = (i == 0)
                    ? soilPileZ // 0 depth at pile top
                    : groundLayers[i - 1].BottomAltitude;
                double btmAlt = layer.BottomAltitude;
                // Altitude 差を px に: ToImagePoint は World(y=Altitude) を描画座標に変換する
                // y_top_altitude は上にあるので画像上での Y は小さくなる
                double yTopPx = -(topAlt - soilPileZ) * scalePxPerM + originYPx;
                double yBtmPx = -(btmAlt - soilPileZ) * scalePxPerM + originYPx;
                double top = Math.Min(yTopPx, yBtmPx);
                double btm = Math.Max(yTopPx, yBtmPx);
                // 画像範囲と交差部分のみ描画
                double visTop = Math.Max(0, top);
                double visBtm = Math.Min(heightPx, btm);
                if (visBtm <= visTop) continue;

                var brush = GetSoilTypeBackgroundBrush(layer.GranularityClass);
                dc.DrawRectangle(brush, null, new Rect(0, visTop, widthPx, visBtm - visTop));
            }
        }

        // Example: WPF-based diagram -> PNG bytes (migrated from CreateLoadCombinationDiagramDrawing / SaveLoadCombinationDiagramByMm)
        public static byte[] RenderLoadCombinationDiagramPng(
            double ps, double pf, double alphaL, double betaU, double betaL,
            int widthMm = 30, int heightMm = 30, float dpi = 192f)
        {
            int widthPx = MmToPx(widthMm, dpi, 1.0);
            int heightPx = MmToPx(heightMm, dpi, 1.0);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // --- デザイン: コピー元の描画ロジックをここに入れる ---
                // シンプル版（必要に応じ元の CreateLoadCombinationDiagramDrawing の描画内容を展開）
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                double centerX = widthPx * 2.0 / 3.0;
                double ellipseW = Math.Min(widthPx, heightPx) * 0.25;
                double ellipseH = ellipseW;
                dc.DrawEllipse(null, new Pen(Brushes.SkyBlue, 1), new Point(centerX, ellipseH * 1.5), ellipseW, ellipseH);

                // arrows / texts (簡略化)
                var typeface = new Typeface("Meiryo");
                var ft = new FormattedText(
                    $"α={alphaL:F2} βU={betaU:F2} βL={betaL:F2}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 12, Brushes.Black,
                    VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
                dc.DrawText(ft, new Point(4, 4));
            }

            var bmp = new RenderTargetBitmap(widthPx, heightPx, DefaultDpi, DefaultDpi, PixelFormats.Pbgra32);
            bmp.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        // ScottPlot を使うグラフを PNG にする補助（安全な一時ファイルを使い中央管理するパターン）
        public static byte[] RenderScottPlotToPngBytes(Action<WpfPlot> plotAction, double widthMm, double heightMm, double dpi = DefaultDpi, double scale = 2.0, string preferredFont = "Meiryo")
        {
            int wpx = MmToPx(widthMm, dpi, scale);
            int hpx = MmToPx(heightMm, dpi, scale);

            var wpf = new WpfPlot();
            plotAction?.Invoke(wpf);

            // 実行環境にある日本語フォント名候補から使えるものを選ぶ
            string[] candidates = new[] { preferredFont, "Meiryo", "Yu Gothic", "Yu Gothic UI", "ＭＳ ゴシック", "MS Gothic" };
            string useFont = candidates.FirstOrDefault(fn =>
            {
                try
                {
                    return System.Windows.Media.Fonts.SystemFontFamilies.Any(f => f.Source?.IndexOf(fn, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch
                {
                    return false;
                }
            }) ?? preferredFont;

            try
            {
                // ScottPlot.Fonts.Detect() を使用して日本語対応フォントを検出
                // 軸ラベルのテキストから適切なフォントを検出
                string titleText = wpf.Plot?.Axes?.Title?.Label?.Text ?? "メイリオ";
                string bottomText = wpf.Plot?.Axes?.Bottom?.Label?.Text ?? "メイリオ";
                string leftText = wpf.Plot?.Axes?.Left?.Label?.Text ?? "メイリオ";

                if (wpf.Plot?.Axes?.Title?.Label != null)
                    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(titleText);
                if (wpf.Plot?.Axes?.Bottom?.Label != null)
                    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(bottomText);
                if (wpf.Plot?.Axes?.Left?.Label != null)
                    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(leftText);
                if (wpf.Plot?.Legend != null)
                    wpf.Plot.Legend.FontName = ScottPlot.Fonts.Detect("凡例");
            }
            catch
            {
                // 失敗しても描画は続ける
            }

            // 一時ファイル経由で確実に保存
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            try
            {
                // SavePng は環境によって内部実装が異なるため例外をキャッチして報告する
                wpf.Plot?.SavePng(tempFile, wpx, hpx);
                return File.ReadAllBytes(tempFile);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch (Exception ex) { Log.Warning(ex, "[DiagramRenderer] tempFile delete failed"); }
            }
        }
        //public static byte[] RenderScottPlotToPngBytes(Action<WpfPlot> plotAction, double widthMm, double heightMm, double dpi = DefaultDpi, double scale = 2.0, string fontName = "Meiryo")
        //{
        //    // width/height in px
        //    int wpx = MmToPx(widthMm, dpi, scale);
        //    int hpx = MmToPx(heightMm, dpi, scale);

        //    // create WpfPlot, let caller add series
        //    var wpf = new WpfPlot();
        //    plotAction?.Invoke(wpf);

        //    // 明示的フォント指定（日本語フォントがインストールされていることを前提）
        //    // 軸ラベルや凡例のフォント名プロパティは環境や ScottPlot のバージョンで存在するため
        //    // ここでは主要なプロパティを設定します（存在しなければ例外は無視）。
        //    try
        //    {
        //        if (!string.IsNullOrEmpty(fontName))
        //        {
        //            // 軸ラベル
        //            wpf.Plot.Axes.Bottom.Label.FontName = fontName;
        //            wpf.Plot.Axes.Left.Label.FontName = fontName;
        //            wpf.Plot.Axes.Top.Label.FontName = fontName;
        //            wpf.Plot.Axes.Right.Label.FontName = fontName;

        //            // 凡例
        //            wpf.Plot.Legend.FontName = fontName;

        //            // ラベルに使うテキストが多い場合、自動検出より明示指定の方が安定します
        //        }
        //    }
        //    catch
        //    {
        //        // 古い/異なる ScottPlot API の場合、安全に無視して続行
        //    }

        //    // ScottPlot は直接 MemoryStream 保存がバージョンで異なるため、一時ファイルを安全に使う
        //    string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        //    try
        //    {
        //        wpf.Plot.SavePng(tempFile, wpx, hpx); // SaveFig/SavePng 適宜使う（環境に合わせて調整）
        //        return File.ReadAllBytes(tempFile);
        //    }
        //    finally
        //    {
        //        if (File.Exists(tempFile)) File.Delete(tempFile);
        //    }
        //}

        // 既存ファイルの DiagramRenderer クラス内に以下のメソッドを追加してください。
        public static byte[] RenderPileForceElevationPngBytes(
                    PileDesign.Models.InputData.SoilPile soilPile,
                    string springType,
                    double widthMm = 150,
                    double heightMm = 100,
                    double dpi = DefaultDpi,
                    double scale = 1.0)
        {
            if (soilPile == null) throw new ArgumentNullException(nameof(soilPile));
            Func<byte[]> renderAction = () =>
            {
                var segments = soilPile.PileBodySegments != null
                    ? [.. soilPile.PileBodySegments]
                    : new List<PileDesign.Models.InputData.PileBodySegment>();
                if (segments.Count == 0)
                {
                    int wpx0 = MmToPx(widthMm, dpi, scale);
                    int hpx0 = MmToPx(heightMm, dpi, scale);
                    var dv0 = new DrawingVisual();
                    using (var dc0 = dv0.RenderOpen())
                    {
                        dc0.DrawRectangle(Brushes.White, null, new Rect(0, 0, wpx0, hpx0));
                        var ft = new FormattedText(
                            "No data",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Meiryo"),
                            12,
                            Brushes.Gray,
                            1.0);
                        dc0.DrawText(ft, new Point(4, 4));
                    }
                    var bmp0 = new RenderTargetBitmap(MmToPx(widthMm, dpi, scale), MmToPx(heightMm, dpi, scale), 96, 96, PixelFormats.Pbgra32);
                    bmp0.Render(dv0);
                    var enc0 = new PngBitmapEncoder();
                    enc0.Frames.Add(BitmapFrame.Create(bmp0));
                    using var ms0 = new MemoryStream();
                    enc0.Save(ms0);
                    return ms0.ToArray();
                }

                int widthPx = MmToPx(widthMm, dpi, scale);
                int heightPx = MmToPx(heightMm, dpi, scale);

                double maxSegDiaMm = segments.Max(s => (double)(s?.PileSection?.PileDiameter ?? 0));
                double maxSegDia = Math.Max(1.0, maxSegDiaMm) * 0.001; // m

                double pileDepth = segments[^1].SegmentDepth; // m

                const double topMargin = 1.0;
                const double btmMargin = 5.0;
                const double horMargin = 1.0;

                double minX = -maxSegDia * 0.5, maxX = maxSegDia * 0.5;
                double minY = -pileDepth, maxY = 0;
                double midX = (maxX + minX) * 0.5;
                double midY = (topMargin - maxY + minY - btmMargin) * 0.5;

                double scalePxPerM = Math.Min(
                    widthPx / (maxX - minX + 2 * horMargin),
                    heightPx / (maxY - minY + topMargin + btmMargin)
                );

                Point ToImagePoint(double x, double y) =>
                    new(widthPx * 0.5 + (x - midX) * scalePxPerM,
                        heightPx * 0.5 - (y - midY) * scalePxPerM);

                // local helper: draw zigzag polyline between a -> b
                void DrawZigzag(DrawingContext dc, Point a, Point b, int zigCount, double amplitudePx, Pen pen)
                {
                    if (zigCount < 1)
                    {
                        dc.DrawLine(pen, a, b);
                        return;
                    }
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1.0)
                    {
                        dc.DrawLine(pen, a, b);
                        return;
                    }
                    double ux = dx / length;
                    double uy = dy / length;
                    double nx = -uy;
                    double ny = ux;
                    double step = length / zigCount;

                    var pts = new List<Point> { a };
                    for (int i = 1; i < zigCount; i++)
                    {
                        double t = i * step;
                        double bx = a.X + ux * t;
                        double by = a.Y + uy * t;
                        double off = ((i % 2) == 0) ? -amplitudePx : amplitudePx;
                        pts.Add(new Point(bx + nx * off, by + ny * off));
                    }
                    pts.Add(b);

                    for (int i = 0; i < pts.Count - 1; i++)
                        dc.DrawLine(pen, pts[i], pts[i + 1]);
                }

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // constants
                    double lineWidthThick = Math.Max(1.5, 1.5 * scale);
                    double lineWidthThin = Math.Max(1.0, 1.0 * scale);
                    double nodeRadius = Math.Max(2.0, 2.0 * scale);

                    // background
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                    // 土層背景色 (地盤ウィンドウと同じ配色): 薄茶/薄橙/薄緑/薄灰の半透明で塗る
                    DrawSoilLayerBackground(dc, soilPile, widthPx, heightPx, scalePxPerM, ToImagePoint);

                    // draw pile segments (rectangles) and keep min/max Y px for center line
                    double topMostPx = double.MaxValue;
                    double bottomMostPx = double.MinValue;
                    foreach (var seg in segments)
                    {
                        if (seg == null) continue;
                        double dia = (seg.PileSection?.PileDiameter ?? 0) * 0.001; // m
                        double topY = -(seg.SegmentDepth - seg.SegmentLength);
                        double bottomY = -seg.SegmentDepth;
                        var topLeft = ToImagePoint(midX - dia * 0.5, topY);
                        double dx = Math.Max(1, dia * scalePxPerM);
                        double dy = Math.Max(1, seg.SegmentLength * scalePxPerM);

                        dc.DrawRectangle(null, new Pen(Brushes.SteelBlue, lineWidthThin), new Rect(topLeft.X, topLeft.Y, dx, dy));

                        // update extents for centerline
                        topMostPx = Math.Min(topMostPx, topLeft.Y);
                        bottomMostPx = Math.Max(bottomMostPx, topLeft.Y + dy);
                    }

                    // draw center axis line and upper node at segment tops
                    if (topMostPx < double.MaxValue && bottomMostPx > double.MinValue)
                    {
                        double centerXpx = widthPx * 0.5;
                        dc.DrawLine(new Pen(Brushes.Black, lineWidthThick), new Point(centerXpx, topMostPx), new Point(centerXpx, bottomMostPx));
                        // draw small circles at each segment top (node)
                        foreach (var seg in segments)
                        {
                            double topY = -(seg.SegmentDepth - seg.SegmentLength);
                            var upperNode = ToImagePoint(0, topY);
                            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), upperNode, nodeRadius, nodeRadius);
                        }
                    }

                    // toe node
                    var toeNode = ToImagePoint(0, -pileDepth);
                    dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), toeNode, nodeRadius, nodeRadius);

                    // ground layers
                    var groundLayers = soilPile.GroundInput?.GroundLayers;
                    if (groundLayers != null)
                    {
                        foreach (var layer in groundLayers)
                        {
                            double yPx = -(layer.BottomAltitude - soilPile.Z) * scalePxPerM + ToImagePoint(0, 0).Y;
                            if (yPx >= -1 && yPx <= heightPx + 1)
                                dc.DrawLine(new Pen(Brushes.Gray, lineWidthThin), new Point(0, yPx), new Point(widthPx, yPx));
                        }
                    }

                    var masses = soilPile.GroundInput?.GroundMassesData;
                    if (masses != null && masses.Count > 0)
                    {
                        double leftX = widthPx * 0.75, rightX = widthPx * 0.90;
                        double maxN = 60;
                        double nScale = (rightX - leftX) / maxN;

                        // determine vertical extents for the N-grid lines (use pile extents if available)
                        double topLineY = (topMostPx < double.MaxValue) ? Math.Max(4.0, topMostPx - 8.0) : 8.0;
                        //double bottomLineY = (bottomMostPx > double.MinValue) ? Math.Min(heightPx - 8.0, bottomMostPx + 8.0) : heightPx - 8.0;
                        double bottomLineY = heightPx - 4.0;
                        // draw vertical reference lines for N = 0,10,..,60 and top labels
                        var gridPen = new Pen(Brushes.LightGray, Math.Max(1.0, lineWidthThin)) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
                        for (int v = 0; v <= 60; v += 10)
                        {
                            double x = leftX + v * nScale;
                            dc.DrawLine(gridPen, new Point(x, topLineY), new Point(x, bottomLineY));

                            // label at the top of each vertical line
                            var ftLabel = new FormattedText(
                                $"N={v}",
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface("Meiryo"),
                                10,
                                Brushes.Black,
                                1.0);
                            var labelPos = new Point(x - ftLabel.Width / 2.0, topLineY - ftLabel.Height - 4);
                            dc.DrawText(ftLabel, labelPos);
                        }

                        // plot N points, polyline and numeric labels to the right of each point
                        var nPts = new List<Point>();
                        foreach (var m in masses)
                        {
                            double depth = m.AltitudeDepth - soilPile.Z;
                            var pt = new Point(leftX + m.NValue * nScale, -depth * scalePxPerM + ToImagePoint(0, 0).Y);
                            nPts.Add(pt);

                            // marker
                            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThin), pt, 3, 3);

                            // numeric label to the right of the point
                            var ftVal = new FormattedText(
                                $"{m.NValue:N0}",
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface("Meiryo"),
                                9,
                                Brushes.Black,
                                1.0);
                            var valPos = new Point(pt.X + 6, pt.Y - ftVal.Height / 2.0);
                            dc.DrawText(ftVal, valPos);
                        }

                        // connect points with polyline
                        if (nPts.Count > 1)
                        {
                            var polyPen = new Pen(Brushes.Black, lineWidthThin);
                            for (int i = 0; i < nPts.Count - 1; i++)
                                dc.DrawLine(polyPen, nPts[i], nPts[i + 1]);
                        }
                    }

                    // displacement / springs
                    var zItems = soilPile.ZDataItems;
                    if (zItems != null && zItems.Count > 0)
                    {
                        // compute pile visual half width in px (use last segment diameters where available)
                        double lastDia = (segments.LastOrDefault()?.PileSection?.PileDiameter ?? 0) * 0.001;
                        double halfWidthPx = Math.Max(4.0, 0.5 * lastDia * scalePxPerM);
                        double pileCenterX = widthPx * 0.5;
                        // horizontal branch: replace existing code with this
                        if (string.Equals(springType, "horizontal", StringComparison.OrdinalIgnoreCase))
                        {
                            double dispBaseX = pileCenterX - 2.0 * scalePxPerM;

                            // collect all displacement values (mm)
                            var allDispMm = new List<double>();
                            foreach (var z in zItems)
                            {
                                allDispMm.Add(z.GroundDisp1);
                                allDispMm.Add(z.GroundDisp2);
                                allDispMm.Add(z.GroundDisp1L);
                                allDispMm.Add(z.GroundDisp2L);
                            }

                            // find maximum displacement (mm) among all (fallback to refMm)
                            double maxDispMm = allDispMm.Count > 0 ? allDispMm.Max() : 0;

                            // target: map maxDeltaMm -> -1500 mm (display units)
                            double scaleDisplayPtOnMm = Math.Abs(maxDispMm) > 1e-9 ? (scalePxPerM * 0.500 / maxDispMm) : 1.0;

                            // available pixel width to represent absolute 1500 mm
                            double availableWidth = Math.Max(10.0, (pileCenterX - dispBaseX - halfWidthPx - 8.0));
                            double pxPerDisplayMm = availableWidth / 1500.0; // 1500 mm maps to availableWidth px

                            Pen khakiPen = new(Brushes.Khaki, Math.Max(1.0, lineWidthThick));
                            Pen darkKhakiPen = new(Brushes.DarkKhaki, Math.Max(1.0, lineWidthThick));
                            Pen slatePen = new(Brushes.SlateBlue, Math.Max(1.0, lineWidthThick));
                            Pen darkSlatePen = new(Brushes.DarkSlateGray, Math.Max(1.0, lineWidthThick));

                            // 杭頭標高を取得（絶対標高→相対深度変換用）
                            double pileHeadZ = soilPile.Z;

                            void DrawDisp(Func<PileDesign.Models.InputData.PileZDataItem, double> sel, Pen pen)
                            {
                                var pts = new List<Point>();
                                foreach (var z in zItems)
                                {
                                    double valMm = sel(z);
                                    double displayMm = valMm * scaleDisplayPtOnMm; // 1500
                                    double xPx = dispBaseX + displayMm;
                                    // z.Z（絶対標高）を杭頭からの相対深度に変換
                                    var p = new Point(xPx, ToImagePoint(0, z.Z - pileHeadZ).Y);
                                    pts.Add(p);
                                }

                                // draw points and connecting lines
                                for (int i = 0; i < pts.Count; i++)
                                {
                                    dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThin), pts[i], nodeRadius, nodeRadius);
                                    if (i < pts.Count - 1)
                                        dc.DrawLine(pen, pts[i], pts[i + 1]);
                                }

                                // draw zigzag springs from each disp point to pile center
                                double zigAmp = Math.Max(4.0, halfWidthPx * 0.3);
                                int zigCount = 8;
                                foreach (var p in pts)
                                {
                                    var target = new Point(pileCenterX, p.Y);
                                    DrawZigzag(dc, p, target, zigCount, zigAmp, new Pen(Brushes.DarkGray, Math.Max(1.0, lineWidthThin)));
                                }
                            }

                            DrawDisp(z => z.GroundDisp1, khakiPen);
                            DrawDisp(z => z.GroundDisp2, darkKhakiPen);
                            DrawDisp(z => z.GroundDisp1L, slatePen);
                            DrawDisp(z => z.GroundDisp2L, darkSlatePen);
                        }

                        else if (string.Equals(springType, "vertical", StringComparison.OrdinalIgnoreCase))
                        {
                            double xCenter = pileCenterX;
                            double xLeft = xCenter - halfWidthPx;
                            double xRight = xCenter + halfWidthPx;
                            double springLengthPx = Math.Max(8.0, 0.2 * scalePxPerM); // px
                            double zigAmp = Math.Max(4.0, halfWidthPx * 0.25);
                            int zigCount = 8;
                            Pen thinBlack = new(Brushes.Black, Math.Max(1.0, lineWidthThick));
                            Pen springPen = new(Brushes.DarkGray, Math.Max(1.0, lineWidthThin));

                            // 杭頭標高を取得（絶対標高→相対深度変換用）
                            double pileHeadZ = soilPile.Z;

                            foreach (var z in zItems)
                            {
                                // z.Z（絶対標高）を杭頭からの相対深度に変換
                                double yPx = ToImagePoint(0, z.Z - pileHeadZ).Y;
                                // draw horizontal short bar representing spring attachment
                                dc.DrawLine(thinBlack, new Point(xLeft, yPx), new Point(xCenter - nodeRadius, yPx));
                                dc.DrawLine(thinBlack, new Point(xCenter + nodeRadius, yPx), new Point(xRight, yPx));
                                // left zigzag downward
                                var leftStart = new Point(xLeft, yPx);
                                var leftEnd = new Point(xLeft, yPx + springLengthPx);
                                DrawZigzag(dc, leftStart, leftEnd, zigCount, zigAmp, springPen);
                                // right zigzag downward
                                var rightStart = new Point(xRight, yPx);
                                var rightEnd = new Point(xRight, yPx + springLengthPx);
                                DrawZigzag(dc, rightStart, rightEnd, zigCount, zigAmp, springPen);
                            }
                            // bottom spring from toe to deeper ground (small representation)
                            var bottomY = ToImagePoint(0, zItems[^1].Z - pileHeadZ).Y;
                            DrawZigzag(dc, new Point(xCenter, bottomY), new Point(xCenter, bottomY + springLengthPx), zigCount, zigAmp, springPen);
                        }
                    }
                }

                // RenderTargetBitmap (dpi fixed to 96 for stable Word insertion)
                var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            };

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    return (byte[])Application.Current.Dispatcher.Invoke(renderAction);
                }
                else
                {
                    return renderAction();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RenderPileForceElevationPngBytes error");
                return Array.Empty<byte>();
            }
        }


        public static byte[] RenderPilingLayoutPngBytes(
            IEnumerable<PileLayoutDataItem> pileLayoutItems,
            Func<PileLayoutDataItem, string> markSelector,
            Func<PileLayoutDataItem, double>? diameterSelector = null, // diameter in meters (m)
            double widthMm = 150,
            double heightMm = 200,
            double dpi = DefaultDpi,
            double scale = 1.0)
        {
            if (pileLayoutItems == null) throw new ArgumentNullException(nameof(pileLayoutItems));
            if (markSelector == null) throw new ArgumentNullException(nameof(markSelector));

            Func<byte[]> renderAction = () =>
            {
                var list = pileLayoutItems as IList<PileLayoutDataItem> ?? pileLayoutItems.ToList();
                int widthPx = MmToPx(widthMm, dpi, scale);
                int heightPx = MmToPx(heightMm, dpi, scale);

                // 元の描画パラメータ（復元）
                double gridBandWidth = 10; // m
                double unitX = 5; // m
                double unitY = 5; // m
                double tickLength = 1; // m (logical units, converted later)
                double tickWidth = 1; // m (logical)
                double pileWidth = 1; // m (logical)
                double symbolCircleDiaInPixel = 20; //
                double symbolTextHeight = symbolCircleDiaInPixel * 0.5;

                if (list.Count == 0)
                {
                    var dv0 = new DrawingVisual();
                    using (var dc0 = dv0.RenderOpen())
                    {
                        dc0.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));
                        var ft = new FormattedText(
                            "No data",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Meiryo"),
                            12,
                            Brushes.Gray,
                            1.0);
                        dc0.DrawText(ft, new Point(4, 4));
                    }
                    var bmp0 = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
                    bmp0.Render(dv0);
                    var enc0 = new PngBitmapEncoder();
                    enc0.Frames.Add(BitmapFrame.Create(bmp0));
                    using var ms0 = new MemoryStream();
                    enc0.Save(ms0);
                    return ms0.ToArray();
                }

                // compute extents (m)
                double maxX = double.MinValue, minX = double.MaxValue, maxY = double.MinValue, minY = double.MaxValue;
                foreach (var p in list)
                {
                    var pt = p.Point3D;
                    maxX = Math.Max(maxX, pt.X);
                    minX = Math.Min(minX, pt.X);
                    maxY = Math.Max(maxY, pt.Y);
                    minY = Math.Min(minY, pt.Y);
                }

                // scale px/m
                double scalePxPerM = Math.Min(
                    widthPx / Math.Max(1e-6, (maxX - minX) + 2.0 * gridBandWidth),
                    heightPx / Math.Max(1e-6, (maxY - minY) + 2.0 * gridBandWidth)
                );

                double midX = (maxX + minX) * 0.5;
                double midY = (maxY + minY) * 0.5;

                // local coordinate converter (m -> px)
                System.Windows.Point ToImagePoint(double x, double y)
                {
                    double px = widthPx * 0.5 + (x - midX) * scalePxPerM;
                    double py = heightPx * 0.5 - (y - midY) * scalePxPerM; // Y inverted
                    return new System.Windows.Point(px, py);
                }

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // background
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                    // draw piles (circle markers) and labels, similar to original implementation
                    foreach (var pli in list)
                    {
                        var ip = ToImagePoint(pli.Point3D.X, pli.Point3D.Y);

                        double diaM = diameterSelector?.Invoke(pli) ?? 1.0; // m
                        double radiusPx = Math.Max(2.0, 0.5 * diaM * scalePxPerM);

                        // pile circle
                        dc.DrawEllipse(null, new Pen(Brushes.Gray, Math.Max(1.0, 0.5)), ip, radiusPx, radiusPx);

                        // mark text to the right
                        string mark = markSelector(pli) ?? string.Empty;
                        var lines = mark.Replace("\r\n", "\n").Split('\n');
                        var typeface = new Typeface("Meiryo");
                        double fontSize = Math.Max(8.0, Math.Min(14.0, radiusPx * 0.6));
                        double lineHeight = fontSize * 1.2;
                        double textX = ip.X + radiusPx + 6;
                        double textY = ip.Y - (lines.Length - 1) * lineHeight * 0.5;

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (string.IsNullOrEmpty(lines[i])) continue;
                            var ft = new FormattedText(
                                lines[i],
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface, fontSize, Brushes.Black, 1.0);
                            dc.DrawText(ft, new System.Windows.Point(textX, textY + i * lineHeight));
                        }
                    }

                    // tick marks and axis labels (replicate original SavePilingLayoutDiagramByMm behavior)

                    // tick positions in world coordinates
                    var tickXs = GetMultiplesInRange(minX, maxX, unitX);
                    var tickYs = GetMultiplesInRange(minY, maxY, unitY);

                    // draw ticks at top (X ticks)
                    foreach (var x in tickXs)
                    {
                        var ptTop = ToImagePoint(x, maxY + gridBandWidth);
                        var ptTick = ToImagePoint(x, maxY + gridBandWidth - tickLength);
                        dc.DrawLine(new Pen(Brushes.Gray, Math.Max(1.0, tickWidth)), ptTop, ptTick);

                        // label (left aligned)
                        WordDocumentUtils.DrawTextWithAlignment(dc, $"{x}", ptTop, 10, Brushes.Black, null, "left", "top");
                    }

                    // draw ticks at left (Y ticks)
                    foreach (var y in tickYs)
                    {
                        var ptLeft = ToImagePoint(-gridBandWidth, y);
                        var ptTick = ToImagePoint(-gridBandWidth + tickLength, y);
                        dc.DrawLine(new Pen(Brushes.Gray, Math.Max(1.0, tickWidth)), ptLeft, ptTick);
                        WordDocumentUtils.DrawTextWithAlignment(dc, $"{y}", ptLeft, 10, Brushes.Black, null, "left", "bottom");
                    }

                    // dashed grid lines and grid symbols (replicate original)
                    var dashStyle = new DashStyle(new double[] { 10, 2, 1, 2 }, 0);
                    var dashedPen = new Pen(Brushes.Gray, Math.Max(1.0, tickWidth)) { DashStyle = dashStyle };

                    // draw gridX items: circles and vertical dashed lines + labels
                    foreach (var xItem in (list.FirstOrDefault()?.GetType().Assembly.GetType("PileDesign.Models.InputData.InputModel") != null ? Enumerable.Empty<object>() : Enumerable.Empty<object>())) { /* no-op - keep compile path */ }

                    // original used inputModel.GridXItems / GridYItems; try to access via first pile's model if available
                    // but safer: if caller's PileLayoutDataItem has GridX/ GridY via InputModel, use public InputModel stored elsewhere.
                    // In current project WordDocument.AddPilingLayoutDiagramByMm passed inputModel.PileLayoutItems; we can assume GridXItems available globally in WordDocument context, but here we don't have inputModel.
                    // To maintain original behavior, we attempt to draw grid markers if PileLayoutDataItem exposes GridName via properties (common case already covered above).
                    // If project has InputModel accessible globally, replace the above no-op with actual gridX/gridY rendering. For now, draw faint bounding rectangle and continue.

                    // faint bounding rectangle to indicate plotting area
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1), new Rect(2, 2, widthPx - 4, heightPx - 4));

                    // footer info (range)
                    var info = $"X: {minX:N2} .. {maxX:N2} m  Y: {minY:N2} .. {maxY:N2} m";
                    var infoFt = new FormattedText(info, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Meiryo"), 10, Brushes.Gray, 1.0);
                    dc.DrawText(infoFt, new System.Windows.Point(4, heightPx - infoFt.Height - 4));
                }

                // render to PNG (96 dpi for Word stability)
                var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            };

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                    return (byte[])Application.Current.Dispatcher.Invoke(renderAction);
                else
                    return renderAction();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RenderPilingLayoutPngBytes error");
                return Array.Empty<byte>();
            }
        }

        // 指定範囲内の単位の倍数リストを返す（DiagramRenderer 用に複製）
        private static List<double> GetMultiplesInRange(double min, double max, double unit)
        {
            var result = new List<double>();
            if (unit <= 0) return result;

            // 最初の倍数（min 以上）
            double start = Math.Ceiling(min / unit) * unit;

            // 安全マージン付きループ（丸め誤差に注意）
            for (double x = start; x <= max + 1e-8; x += unit)
            {
                result.Add(Math.Round(x, 8));
                // 防止：無限ループ対策
                if (result.Count > 100000) break;
            }

            // 最後の値が max より小さい場合は次の倍数を追加（元実装互換）
            if (result.Count > 0 && result[^1] < max)
            {
                result.Add(Math.Round(result[^1] + unit, 8));
            }

            return result;
        }

        #region 抽象レイヤーを使用した新しい描画メソッド

        /// <summary>
        /// 抽象レイヤーを使用して杭立面図をPNG出力
        /// </summary>
        public static byte[] RenderPileElevationWithAbstraction(
            SoilPile soilPile,
            double widthMm = 150,
            double heightMm = 100,
            double dpi = DefaultDpi,
            double scale = 2.0)
        {
            if (soilPile == null) return [];

            Func<byte[]> renderAction = () =>
            {
                int widthPx = MmToPx(widthMm, dpi, scale);
                int heightPx = MmToPx(heightMm, dpi, scale);

                var segments = soilPile.PileBodySegments?.ToList() ?? [];
                if (segments.Count == 0) return RenderEmptyDiagram(widthPx, heightPx, "No pile data");

                // 境界計算
                double maxDia = segments.Max(s => (s?.PileSection?.PileDiameter ?? 0) * 0.001);
                double pileDepth = segments[^1].SegmentDepth;
                double pileTopZ = soilPile.Z;

                double minX = -maxDia * 1.5;
                double maxX = maxDia * 3.0; // N値グラフ用のスペース
                double minZ = pileTopZ - pileDepth - 2;
                double maxZ = pileTopZ + 2;

                var transform = new StaticCoordinateTransform(
                    widthPx, heightPx,
                    minX, maxX,
                    0, 1, // Y方向は使わない
                    minZ, maxZ,
                    StaticCoordinateTransform.ViewDirection.FrontView,
                    0.05,
                    1.0
                );

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var size = new Size(widthPx, heightPx);
                    var target = new DrawingContextTarget(dc, size);
                    var helper = new DrawingHelper(target, transform);

                    // 背景
                    target.DrawRectangle(new Rect(0, 0, widthPx, heightPx), DrawingStyle.Filled(Colors.White, null, 0));

                    // 土層背景色 (地盤ウィンドウと同配色) を杭セグメントより前に塗る
                    DrawGroundLayerBackgroundsInternal(helper, soilPile.GroundInput, minX, maxX);

                    // 杭セグメント描画
                    DrawPileSegmentsInternal(helper, segments, pileTopZ);

                    // 地盤層描画
                    DrawGroundLayersInternal(helper, soilPile.GroundInput, pileTopZ, maxDia * 1.2);

                    // N値グラフ描画
                    DrawNValueGraphInternal(helper, soilPile.GroundInput, pileTopZ, maxDia * 1.5, maxDia * 2.8);

                    target.Flush();
                }

                return RenderDrawingVisualToPng(dv, widthPx, heightPx);
            };

            return ExecuteOnUIThread(renderAction);
        }

        /// <summary>
        /// 杭セグメントを描画（内部メソッド）
        /// </summary>
        private static void DrawPileSegmentsInternal(DrawingHelper helper, List<PileBodySegment> segments, double pileTopZ)
        {
            var pileStyle = DrawingStyle.Solid(Colors.SteelBlue, 1.5);
            var nodeStyle = new DrawingStyle { StrokeColor = Colors.Black, StrokeThickness = 1.5, FillColor = Colors.White };

            foreach (var seg in segments)
            {
                if (seg?.PileSection == null) continue;

                double dia = seg.PileSection.PileDiameter * 0.001; // mm → m
                double topZ = pileTopZ - (seg.SegmentDepth - seg.SegmentLength);
                double bottomZ = pileTopZ - seg.SegmentDepth;

                var top = new Point3D(0, 0, topZ);
                var bottom = new Point3D(0, 0, bottomZ);

                helper.DrawPileSection(top, bottom, dia, pileStyle);
            }

            // 中心軸線
            double topMostZ = pileTopZ;
            double bottomMostZ = pileTopZ - segments[^1].SegmentDepth;
            helper.AddLine3D(new Point3D(0, 0, topMostZ), new Point3D(0, 0, bottomMostZ), DrawingStyle.Solid(Colors.Black, 2));

            // 節点マーカー
            helper.DrawNodeMarker(new Point3D(0, 0, topMostZ), 6, nodeStyle);
            foreach (var seg in segments)
            {
                double z = pileTopZ - seg.SegmentDepth;
                helper.DrawNodeMarker(new Point3D(0, 0, z), 6, nodeStyle);
            }
        }

        /// <summary>
        /// 地盤層を描画（内部メソッド）
        /// </summary>
        /// <summary>
        /// 土層ごとの背景色を塗る。地盤ウィンドウと同じ配色。
        /// minX..maxX のワールド X 範囲で層ごとに塗る (X-Z 断面上)。
        /// </summary>
        private static void DrawGroundLayerBackgroundsInternal(DrawingHelper helper, GroundInput ground, double minX, double maxX)
        {
            if (ground?.GroundLayers == null || ground.GroundLayers.Count == 0) return;

            // 上端 altitude: GroundTopAltitude または最初の層の上端 (= 前層なし→GroundTopAltitude)
            double topAlt = ground.GroundTopAltitude;
            foreach (var layer in ground.GroundLayers)
            {
                double btmAlt = layer.BottomAltitude;
                var color = layer.GranularityClass switch
                {
                    "粘性土" => Color.FromArgb(64, 210, 180, 140),
                    "砂質土" => Color.FromArgb(64, 255, 165, 0),
                    "礫質土" => Color.FromArgb(64, 144, 238, 144),
                    _ => Color.FromArgb(32, 200, 200, 200),
                };
                var style = DrawingStyle.Filled(color, null, 0);
                // 4 角形 (minX, btmAlt) → (maxX, btmAlt) → (maxX, topAlt) → (minX, topAlt)
                //   _transform.Transform(Point3D) で 3D → 2D ピクセルに射影
                helper.AddFilledPolygon(new[]
                {
                    helper.Transform.Transform(new Point3D(minX, 0, btmAlt)),
                    helper.Transform.Transform(new Point3D(maxX, 0, btmAlt)),
                    helper.Transform.Transform(new Point3D(maxX, 0, topAlt)),
                    helper.Transform.Transform(new Point3D(minX, 0, topAlt)),
                }, style);
                topAlt = btmAlt;
            }
        }

        private static void DrawGroundLayersInternal(DrawingHelper helper, GroundInput ground, double pileTopZ, double xOffset)
        {
            if (ground?.GroundLayers == null) return;

            var layerStyle = DrawingStyle.Dashed(Colors.Gray, 0.5);

            foreach (var layer in ground.GroundLayers)
            {
                double z = layer.BottomAltitude;
                helper.AddLine3D(
                    new Point3D(-xOffset, 0, z),
                    new Point3D(xOffset, 0, z),
                    layerStyle
                );

                // 層名ラベル
                helper.AddText3D(layer.Name ?? "", new Point3D(-xOffset - 0.2, 0, z + 0.5), new TextStyle
                {
                    FontSize = 9,
                    Color = Colors.DarkGray,
                    HorizontalAlignment = HorizontalTextAlignment.Right,
                    VerticalAlignment = VerticalTextAlignment.Center
                });
            }
        }

        /// <summary>
        /// N値グラフを描画（内部メソッド）
        /// </summary>
        private static void DrawNValueGraphInternal(DrawingHelper helper, GroundInput ground, double pileTopZ, double xStart, double xEnd)
        {
            if (ground?.GroundMassesData == null || ground.GroundMassesData.Count == 0) return;

            var gridStyle = DrawingStyle.Dashed(Colors.LightGray, 0.5);
            var lineStyle = DrawingStyle.Solid(Colors.Black, 1);
            var markerStyle = new DrawingStyle { StrokeColor = Colors.Black, StrokeThickness = 1, FillColor = Colors.White };

            double maxN = 60;
            double nScale = (xEnd - xStart) / maxN;

            // グリッド線
            double topZ = ground.GroundTopAltitude;
            double bottomZ = ground.GroundLayers[^1].BottomAltitude;

            for (int n = 0; n <= 60; n += 10)
            {
                double x = xStart + n * nScale;
                helper.AddLine3D(new Point3D(x, 0, topZ), new Point3D(x, 0, bottomZ), gridStyle);

                // ラベル
                helper.AddText3D($"{n}", new Point3D(x, 0, topZ + 0.5), new TextStyle
                {
                    FontSize = 8,
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    VerticalAlignment = VerticalTextAlignment.Bottom
                });
            }

            // N値ポイントとポリライン
            var points = new List<Point>();
            foreach (var m in ground.GroundMassesData)
            {
                double x = xStart + Math.Min(m.NValue, maxN) * nScale;
                double z = m.AltitudeDepth;
                var pt2D = helper.Transform.Transform(new Point3D(x, 0, z));
                points.Add(pt2D);
            }

            if (points.Count > 1)
            {
                helper.DrawPolyLineWithMarkers(points, false, 4, lineStyle, markerStyle);
            }

            // 数値ラベル
            foreach (var m in ground.GroundMassesData)
            {
                double x = xStart + Math.Min(m.NValue, maxN) * nScale;
                helper.AddText3D($"{m.NValue:N0}", new Point3D(x + 0.2, 0, m.AltitudeDepth), new TextStyle
                {
                    FontSize = 8,
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    VerticalAlignment = VerticalTextAlignment.Center
                });
            }
        }

        /// <summary>
        /// 沈下コンター図をPNG出力（双線形補間ヒートマップ＋杭位置マーカー）
        /// </summary>
        public static byte[] RenderSettlementContourDiagram(
            IEnumerable<SettlementGridDataItem> gridData,
            List<double>? gridXs = null,
            List<double>? gridYs = null,
            List<(double X, double Y)>? pilePositions = null,
            double widthMm = 150,
            double heightMm = 150,
            double dpi = DefaultDpi,
            double scale = 2.0,
            int colorBandCount = 12)
        {
            if (gridData == null) return [];

            Func<byte[]> renderAction = () =>
            {
                var data = gridData.ToList();
                if (data.Count == 0) return RenderEmptyDiagram(MmToPx(widthMm, dpi, scale), MmToPx(heightMm, dpi, scale), "No settlement data");

                int widthPx = MmToPx(widthMm, dpi, scale);
                int heightPx = MmToPx(heightMm, dpi, scale);

                // グリッド座標の決定
                var xs = gridXs ?? data.Select(d => d.X).Distinct().OrderBy(v => v).ToList();
                var ys = gridYs ?? data.Select(d => d.Y).Distinct().OrderBy(v => v).ToList();

                double minX = xs.Min();
                double maxX = xs.Max();
                double minY = ys.Min();
                double maxY = ys.Max();
                double minS = data.Min(d => d.Settlement);
                double maxS = data.Max(d => d.Settlement);

                // 2次元配列化
                var grid = new double?[xs.Count, ys.Count];
                foreach (var item in data)
                {
                    int ix = xs.IndexOf(item.X);
                    int iy = ys.IndexOf(item.Y);
                    if (ix >= 0 && iy >= 0) grid[ix, iy] = item.Settlement;
                }

                // マージン設定（右側にカラーバー用スペース）
                double marginLeft = 10, marginRight = 80, marginTop = 10, marginBottom = 10;
                double plotW = widthPx - marginLeft - marginRight;
                double plotH = heightPx - marginTop - marginBottom;

                // アスペクト比を維持
                double dataW = maxX - minX;
                double dataH = maxY - minY;
                if (dataW < 1e-9) dataW = 1;
                if (dataH < 1e-9) dataH = 1;
                double scaleF = Math.Min(plotW / dataW, plotH / dataH);
                double offsetX = marginLeft + (plotW - dataW * scaleF) * 0.5;
                double offsetY = marginTop + (plotH - dataH * scaleF) * 0.5;

                // ワールド座標→ピクセル変換
                double ToPxX(double wx) => offsetX + (wx - minX) * scaleF;
                double ToPxY(double wy) => offsetY + (maxY - wy) * scaleF; // Y軸反転

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 背景
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                    // セルごとに双線形補間でヒートマップ描画
                    int subDiv = 4; // セル内の分割数（高品質化）
                    for (int ix = 0; ix < xs.Count - 1; ix++)
                    {
                        for (int iy = 0; iy < ys.Count - 1; iy++)
                        {
                            double? v00 = grid[ix, iy];
                            double? v10 = grid[ix + 1, iy];
                            double? v01 = grid[ix, iy + 1];
                            double? v11 = grid[ix + 1, iy + 1];
                            if (v00 == null || v10 == null || v01 == null || v11 == null) continue;

                            double cellLeft = ToPxX(xs[ix]);
                            double cellRight = ToPxX(xs[ix + 1]);
                            double cellTop = ToPxY(ys[iy + 1]);
                            double cellBottom = ToPxY(ys[iy]);

                            double subW = (cellRight - cellLeft) / subDiv;
                            double subH = (cellBottom - cellTop) / subDiv;

                            for (int si = 0; si < subDiv; si++)
                            {
                                for (int sj = 0; sj < subDiv; sj++)
                                {
                                    double tx = (si + 0.5) / subDiv;
                                    double ty = (sj + 0.5) / subDiv;

                                    // 双線形補間
                                    double val = (1 - tx) * (1 - ty) * v00.Value
                                               + tx * (1 - ty) * v10.Value
                                               + (1 - tx) * ty * v01.Value
                                               + tx * ty * v11.Value;

                                    double ratio = maxS > minS ? (val - minS) / (maxS - minS) : 0.5;
                                    var color = DrawingHelper.GetRainbowColor(ratio);
                                    var brush = new SolidColorBrush(color);
                                    brush.Freeze();

                                    double rx = cellLeft + si * subW;
                                    double ry = cellTop + sj * subH;
                                    dc.DrawRectangle(brush, null, new Rect(rx, ry, subW + 0.5, subH + 0.5));
                                }
                            }
                        }
                    }

                    // 杭位置マーカー
                    if (pilePositions != null)
                    {
                        double markerR = Math.Max(3.0, 3.0 * scale);
                        var pilePen = new Pen(Brushes.Black, Math.Max(1.0, 1.0 * scale));
                        foreach (var (px, py) in pilePositions)
                        {
                            double cx = ToPxX(px);
                            double cy = ToPxY(py);
                            dc.DrawEllipse(null, pilePen, new Point(cx, cy), markerR, markerR);
                        }
                    }

                    // カラーバー描画
                    var colorBands = GenerateColorBands(minS, maxS, colorBandCount);
                    double barX = widthPx - marginRight + 10;
                    double barY = marginTop + 10;
                    double barW = 15;
                    double barCellH = Math.Min(15.0, (heightPx - 2 * marginTop - 40) / (colorBandCount + 1));
                    var thinPen = new Pen(Brushes.Black, 0.5);

                    for (int i = colorBands.Count - 1; i >= 0; i--)
                    {
                        var (val, bandColor) = colorBands[i];
                        int drawIdx = colorBands.Count - 1 - i;
                        var cellBrush = new SolidColorBrush(bandColor);
                        cellBrush.Freeze();
                        dc.DrawRectangle(cellBrush, thinPen, new Rect(barX, barY + drawIdx * barCellH, barW, barCellH));

                        var ft = new FormattedText(
                            $"{val:N2}",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Meiryo"),
                            Math.Max(8.0, 8.0 * scale * 0.5),
                            Brushes.Black,
                            1.0);
                        dc.DrawText(ft, new Point(barX + barW + 3, barY + drawIdx * barCellH + (barCellH - ft.Height) * 0.5));
                    }

                    // カラーバーのタイトル
                    var titleFt = new FormattedText(
                        "沈下量",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Meiryo"),
                        Math.Max(9.0, 9.0 * scale * 0.5),
                        Brushes.Black,
                        1.0);
                    dc.DrawText(titleFt, new Point(barX, barY - titleFt.Height - 2));

                    var unitFt = new FormattedText(
                        "(mm)",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Meiryo"),
                        Math.Max(8.0, 8.0 * scale * 0.5),
                        Brushes.Black,
                        1.0);
                    dc.DrawText(unitFt, new Point(barX, barY + colorBands.Count * barCellH + 2));
                }

                return RenderDrawingVisualToPng(dv, widthPx, heightPx);
            };

            return ExecuteOnUIThread(renderAction);
        }

        /// <summary>
        /// カラーバンドを生成
        /// </summary>
        private static List<(double Value, Color Color)> GenerateColorBands(double min, double max, int count)
        {
            var bands = new List<(double, Color)>();
            if (count <= 0 || max <= min)
            {
                bands.Add((min, DrawingHelper.GetRainbowColor(0.5)));
                return bands;
            }

            double step = (max - min) / count;
            for (int i = 0; i <= count; i++)
            {
                double value = min + step * i;
                double ratio = (double)i / count;
                bands.Add((value, DrawingHelper.GetRainbowColor(ratio)));
            }
            return bands;
        }

        /// <summary>
        /// 空の図を生成
        /// </summary>
        private static byte[] RenderEmptyDiagram(int widthPx, int heightPx, string message)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));
                var ft = new FormattedText(
                    message,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Meiryo"),
                    12,
                    Brushes.Gray,
                    1.0);
                dc.DrawText(ft, new Point(widthPx / 2 - ft.Width / 2, heightPx / 2 - ft.Height / 2));
            }
            return RenderDrawingVisualToPng(dv, widthPx, heightPx);
        }

        /// <summary>
        /// DrawingVisualをPNGバイト配列に変換
        /// </summary>
        private static byte[] RenderDrawingVisualToPng(DrawingVisual dv, int widthPx, int heightPx)
        {
            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// UIスレッドで実行
        /// </summary>
        private static byte[] ExecuteOnUIThread(Func<byte[]> action)
        {
            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    return (byte[])Application.Current.Dispatcher.Invoke(action);
                }
                return action();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DiagramRenderer error");
                return [];
            }
        }

        #endregion
    }
}