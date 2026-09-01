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
    /// <summary>
    /// 図の中に焼き込む文字と線の見た目。
    /// <see cref="DiagramRenderer"/> からは WordDocument.Layout が見えないので値で受け取る。
    /// 大きさは<b>すべて pt</b>。px で渡すと画像だけが倍密度になり、紙面で文字だけが極小になる。
    /// </summary>
    internal readonly record struct PileDiagramStyle(
        string FontName,
        double FontSizePt,
        double SmallFontSizePt,
        double LineWidthThickPt,
        double LineWidthThinPt,
        int SpringZigzagMaxCount,
        double MinSpringLengthPx);

    internal static class DiagramRenderer
    {
        private const double InchPerMm = 1.0 / 25.4;
        private const double DefaultDpi = 96.0;

        // 解析杭モデル図の横方向の帯 (画像幅に対する割合)。
        // 左から 土層諸元 / 深度目盛 / 杭とばね / N値柱状図。
        private const double BandLayerInfoLeft = 0.02;
        private const double BandLayerInfoRight = 0.30;
        private const double BandDepthAxisLeft = 0.32;
        private const double BandDepthAxisRight = 0.35;
        private const double BandPileLeft = 0.35;
        private const double BandPileRight = 0.72;
        private const double BandNChartLeft = 0.75;
        private const double BandNChartRight = 0.95;

        private static int MmToPx(double mm, double dpi = DefaultDpi, double scale = 1.0)
            => (int)Math.Round(mm * dpi * scale * InchPerMm);

        /// <summary>
        /// N値柱状図の軸の上限。データが 60 を超えたら 10 刻みで伸ばす。
        /// 60 固定にしていたため、支持層の N 値が図の外へはみ出していた。
        /// </summary>
        internal static double NAxisMax(double dataMaxN)
        {
            if (double.IsNaN(dataMaxN) || dataMaxN < 0) dataMaxN = 0;
            return Math.Clamp(Math.Ceiling(Math.Max(60.0, dataMaxN) / 10.0) * 10.0, 60.0, 300.0);
        }

        /// <summary>N値柱状図の目盛の刻み。軸が伸びたら刻みも粗くしてラベルの重なりを防ぐ。</summary>
        internal static double NAxisStep(double maxN)
            => maxN <= 60.0 ? 10.0 : maxN <= 120.0 ? 20.0 : 50.0;

        /// <summary>
        /// 土層情報帯の層番号に使う丸数字。
        /// 丸数字は ⑳ (U+2473) までしかないので、21 以上は括弧書きにする。
        /// </summary>
        internal static string CircledNumber(int no)
        {
            if (no <= 0) return string.Empty;
            if (no <= 20) return ((char)('①' + (no - 1))).ToString();
            return $"({no})";
        }

        /// <summary>
        /// その土層の帯に何行書けるか。薄い層は 3 行 → 2 行 → 1 行 と落とし、
        /// 1 行も入らなければ 0 を返す (呼び出し側で引き出し線に逃がす)。
        /// </summary>
        /// <param name="bandHeightPx">その層が占める帯の高さ</param>
        /// <param name="lineHeightPx">通常の書体の行高</param>
        /// <param name="smallLineHeightPx">小さい書体の行高</param>
        internal static int LayerInfoLineCount(double bandHeightPx, double lineHeightPx, double smallLineHeightPx)
        {
            if (bandHeightPx >= 3 * lineHeightPx + 4) return 3;
            if (bandHeightPx >= 2 * lineHeightPx + 4) return 2;
            if (bandHeightPx >= lineHeightPx + 2) return 1;
            if (bandHeightPx >= smallLineHeightPx) return 1;   // 小さい書体で 1 行
            return 0;
        }

        /// <summary>
        /// 土層情報帯に書く文字列。行数が減ったときに落とす順は 下端深度・Cu → γ。
        /// Cu は粘性土以外では 0 なので、値が無ければ行数によらず項ごと省く。
        /// </summary>
        internal static string[] LayerInfoLines(
            int no, string? name, double thickness, double bottomDepth,
            double density, double nValue, double cohesive, int lineCount)
        {
            if (lineCount <= 0) return [];

            string head = $"{CircledNumber(no)} {name}".Trim();
            if (lineCount == 1) return [$"{head}  t={thickness:0.00}  N={nValue:0}"];
            if (lineCount == 2) return [head, $"t={thickness:0.00}  γ={density:0.0}  N={nValue:0}"];

            string third = $"γ={density:0.0}  N={nValue:0}";
            if (cohesive > 0) third += $"  Cu={cohesive:0}";
            return [head, $"t={thickness:0.00}  GL-{bottomDepth:0.00}", third];
        }

        /// <summary>
        /// 引き出し線の逃がし先。希望位置から上下に探し、遠すぎる (maxDistance を超える)
        /// なら諦める。遠くへ逃がすと引き出し線どうしが交差して却って読めなくなる。
        /// </summary>
        private static int FindFreeSlot(bool[] used, int want, int maxDistance)
        {
            if (used.Length == 0) return -1;
            want = Math.Clamp(want, 0, used.Length - 1);
            for (int d = 0; d <= maxDistance; d++)
            {
                if (want - d >= 0 && !used[want - d]) return want - d;
                if (want + d < used.Length && !used[want + d]) return want + d;
            }
            return -1;
        }

        /// <summary>a → b をジグザグ (ばね記号) で結ぶ。</summary>
        private static void DrawZigzag(DrawingContext dc, Point a, Point b, int zigCount, double amplitudePx, Pen pen)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (zigCount < 1 || length < 1.0)
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
                double off = (i % 2) == 0 ? -amplitudePx : amplitudePx;
                pts.Add(new Point(a.X + ux * t + nx * off, a.Y + uy * t + ny * off));
            }
            pts.Add(b);

            for (int i = 0; i < pts.Count - 1; i++)
                dc.DrawLine(pen, pts[i], pts[i + 1]);
        }

        /// <summary>
        /// 土質ごとの色 (地盤ウィンドウと同じ配色)。不透明度は用途ごとに呼び出し側で決める。
        ///   粘性土: 薄茶 (210,180,140)
        ///   砂質土: 薄橙 (255,165,  0)
        ///   礫質土: 薄緑 (144,238,144)
        ///   その他: 薄灰 (200,200,200)
        /// </summary>
        private static Color GetSoilTypeColor(string? granularityClass, byte alpha) => granularityClass switch
        {
            "粘性土" => Color.FromArgb(alpha, 210, 180, 140),
            "砂質土" => Color.FromArgb(alpha, 255, 165, 0),
            "礫質土" => Color.FromArgb(alpha, 144, 238, 144),
            _ => Color.FromArgb((byte)(alpha / 2), 200, 200, 200),
        };

        /// <summary>土質ごとの背景色 (半透明)。</summary>
        private static Brush GetSoilTypeBackgroundBrush(string? granularityClass)
            => new SolidColorBrush(GetSoilTypeColor(granularityClass, 64));

        /// <summary>
        /// 土層ごとに薄い背景帯を塗る。
        /// 1 層目の上端は<b>地表面標高</b>であって杭頭標高ではない。
        /// 杭頭が地表と一致しないモデルで色帯が層境界からずれていたため、ここを取り違えないこと。
        /// </summary>
        /// <param name="yOf">標高 (m) → 画像 Y (px) の変換</param>
        private static void DrawSoilLayerBackground(
            DrawingContext dc,
            GroundInput? ground,
            double x0,
            double x1,
            Func<double, double> yOf,
            double clipTop,
            double clipBottom,
            byte alpha = 64)
        {
            var groundLayers = ground?.GroundLayers;
            if (groundLayers == null || groundLayers.Count == 0) return;

            double topAlt = ground!.GroundTopAltitude;
            foreach (var layer in groundLayers)
            {
                double btmAlt = layer.BottomAltitude;
                double yTopPx = yOf(topAlt);
                double yBtmPx = yOf(btmAlt);
                topAlt = btmAlt;

                double top = Math.Max(clipTop, Math.Min(yTopPx, yBtmPx));
                double btm = Math.Min(clipBottom, Math.Max(yTopPx, yBtmPx));
                if (btm <= top) continue;

                var brush = new SolidColorBrush(GetSoilTypeColor(layer.GranularityClass, alpha));
                dc.DrawRectangle(brush, null, new Rect(x0, top, x1 - x0, btm - top));
            }
        }

        // Example: WPF-based diagram -> PNG bytes (migrated from CreateLoadCombinationDiagramDrawing / SaveLoadCombinationDiagramByMm)
        public static byte[] RenderLoadCombinationDiagramPng(
            double _ps, double _pf, double alphaL, double betaU, double betaL,
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
                    SafePixelsPerDip());
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

        /// <summary>
        /// 計算書の「沈下解析杭モデル」「水平抵抗解析杭モデル」の図。
        /// 横は左から 土層諸元 / 深度目盛 / 杭とばね / N値柱状図 の 4 帯に分ける。
        /// 縦は標高 (m) → 画像 Y (px) の変換を <c>YOf</c> ひとつに集約する。
        /// 変換式を各所に散らすと、地表面と杭頭がずれたモデルで土層と杭の位置関係が壊れる。
        /// 横方向は模式図で、杭径を読める太さまで誇張している (縮尺は縦方向のみ)。
        /// </summary>
        public static byte[] RenderPileForceElevationPngBytes(
            SoilPile soilPile,
            string springType,
            PileDiagramStyle style,
            double widthMm,
            double heightMm,
            double dpi = DefaultDpi,
            double scale = 1.0)
        {
            ArgumentNullException.ThrowIfNull(soilPile);

            byte[] Render()
            {
                int widthPx = MmToPx(widthMm, dpi, scale);
                int heightPx = MmToPx(heightMm, dpi, scale);

                // 画像は倍密度 (scale) で描く。文字も同じ倍率で拡大しないと、
                // 線だけが太く文字だけが極小の図になる。em サイズを px で直書きしないこと。
                double PtToPx(double pt) => pt * dpi * scale / 72.0;

                var segments = soilPile.PileBodySegments is { } sourceSegments
                    ? sourceSegments.Where(s => s != null).ToList()
                    : new List<PileBodySegment>();
                if (segments.Count == 0)
                {
                    return RenderEmptyDiagram(
                        widthPx, heightPx, "杭のデータがありません",
                        PtToPx(style.FontSizePt), style.FontName);
                }

                var ground = soilPile.GroundInput;
                var layers = ground?.GroundLayers;

                double pileHeadAlt = soilPile.Z;                // 杭頭標高 (m)
                double pileDepth = segments[^1].SegmentDepth;   // 杭頭からの杭長 (m)
                double maxSegDiaM =
                    Math.Max(1.0, segments.Max(s => (double)(s.PileSection?.PileDiameter ?? 0))) * 0.001;

                // ---- 縦: 標高 (m) → 画像 Y (px) --------------------------------------
                double topAlt = ground != null ? Math.Max(pileHeadAlt, ground.GroundTopAltitude) : pileHeadAlt;
                double botAlt = pileHeadAlt - pileDepth;
                if (layers is { Count: > 0 }) botAlt = Math.Min(botAlt, layers[^1].BottomAltitude);
                if (topAlt - botAlt < 1e-6) topAlt = botAlt + 1.0;

                double topPadPx = PtToPx(style.FontSizePt) * 2.8;       // N値柱状図の見出し 2 行分
                double botPadPx = PtToPx(style.SmallFontSizePt) * 2.8;  // 凡例の分
                double scaleYPxPerM = (heightPx - topPadPx - botPadPx) / (topAlt - botAlt);
                double YOf(double alt) => topPadPx + (topAlt - alt) * scaleYPxPerM;
                double plotTopPx = topPadPx;
                double plotBottomPx = heightPx - botPadPx;

                // ---- 横: 帯の割り付け ------------------------------------------------
                double layerInfoL = widthPx * BandLayerInfoLeft;
                double layerInfoR = widthPx * BandLayerInfoRight;
                double depthAxisL = widthPx * BandDepthAxisLeft;
                double depthAxisR = widthPx * BandDepthAxisRight;
                double pileBandL = widthPx * BandPileLeft;
                double pileBandR = widthPx * BandPileRight;
                double nChartL = widthPx * BandNChartLeft;
                double nChartR = widthPx * BandNChartRight;

                bool isVertical = string.Equals(springType, "vertical", StringComparison.OrdinalIgnoreCase);

                // 等方スケールだと杭径 1.2m が紙面 6mm にしかならず、節点もばねも潰れる。
                // 横だけ 0.06W〜0.16W にクランプして誇張する (縮尺は縦方向のみ)。
                double pileDrawWidthPx = Math.Clamp(maxSegDiaM * scaleYPxPerM, widthPx * 0.06, widthPx * 0.16);
                double halfWidthPx = pileDrawWidthPx * 0.5;
                double pileCenterX = isVertical
                    ? (pileBandL + pileBandR) * 0.5   // 両側に対称にばねを出す
                    : pileBandR - halfWidthPx;        // 左に地盤変位のファンを置く

                double thick = Math.Max(1.0, PtToPx(style.LineWidthThickPt));
                double thin = Math.Max(1.0, PtToPx(style.LineWidthThinPt));
                double nodeRadius = Math.Max(2.0, PtToPx(style.FontSizePt) * 0.18);

                FormattedText Text(string s, double pt, Brush brush) => new FormattedText(
                    s,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(style.FontName),
                    PtToPx(pt),
                    brush,
                    1.0);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                    // --- 1. 土層の背景色 (地盤ウィンドウと同じ配色) ---
                    DrawSoilLayerBackground(dc, ground, 0, widthPx, YOf, plotTopPx, plotBottomPx);

                    // --- 2. 土層の境界線。杭とばねの上を横切らないよう杭バンドで分断する ---
                    if (layers != null)
                    {
                        var layerPen = new Pen(Brushes.Gray, thin);
                        // 杭に触れないよう、杭の実際の左右端で切って少し空ける
                        double gapL = Math.Min(pileBandL, pileCenterX - halfWidthPx) - PtToPx(2);
                        double gapR = Math.Max(pileBandR, pileCenterX + halfWidthPx) + PtToPx(2);
                        foreach (var layer in layers)
                        {
                            double y = YOf(layer.BottomAltitude);
                            if (y < plotTopPx - 1 || y > plotBottomPx + 1) continue;
                            dc.DrawLine(layerPen, new Point(layerInfoL, y), new Point(gapL, y));
                            dc.DrawLine(layerPen, new Point(gapR, y), new Point(nChartR, y));
                        }
                    }

                    // --- 3. 深度目盛・地表面線・地下水位線 ---
                    // 旧実装には目盛も地表面線も無く、層境界線がどの深さなのか図から読めなかった
                    double surfaceAlt = ground?.GroundTopAltitude ?? pileHeadAlt;
                    {
                        double depthRange = topAlt - botAlt;
                        double depthUnit = depthRange <= 20 ? 2.0 : depthRange <= 50 ? 5.0 : 10.0;
                        var tickPen = new Pen(Brushes.DimGray, thin);
                        foreach (double d in GetMultiplesInRange(0, surfaceAlt - botAlt, depthUnit))
                        {
                            double y = YOf(surfaceAlt - d);
                            if (y < plotTopPx || y > plotBottomPx) continue;
                            dc.DrawLine(tickPen, new Point(depthAxisL, y), new Point(depthAxisR, y));
                            var ft = Text(d <= 0 ? "GL±0" : $"GL-{d:0}", style.SmallFontSizePt, Brushes.DimGray);
                            dc.DrawText(ft, new Point(depthAxisR - ft.Width, y - ft.Height));
                        }
                    }

                    if (ground != null)
                    {
                        double ys = YOf(surfaceAlt);
                        if (ys >= plotTopPx - 1 && ys <= plotBottomPx + 1)
                        {
                            dc.DrawLine(new Pen(Brushes.Black, thick), new Point(layerInfoL, ys), new Point(nChartR, ys));
                            // 地表面であることを示すハッチ (目盛の側だけに引いて図を煩くしない)
                            double hatch = PtToPx(3);
                            var hatchPen = new Pen(Brushes.Black, thin);
                            for (double x = layerInfoL + hatch; x < depthAxisR; x += hatch * 1.8)
                                dc.DrawLine(hatchPen, new Point(x, ys), new Point(x - hatch * 0.7, ys + hatch));
                        }

                        double yw = YOf(ground.GroundWaterTableAltitude);
                        if (yw > plotTopPx && yw < plotBottomPx)
                        {
                            var waterPen = new Pen(Brushes.SteelBlue, thin) { DashStyle = new DashStyle([6, 3], 0) };
                            dc.DrawLine(waterPen, new Point(layerInfoL, yw), new Point(nChartR, yw));
                            var ftw = Text("▽GWL", style.SmallFontSizePt, Brushes.SteelBlue);
                            dc.DrawText(ftw, new Point(depthAxisR + PtToPx(2), yw - ftw.Height));
                        }
                    }

                    // --- 4. 土層情報帯 (層番号・土層名・層厚・下端深度・γ・N値・Cu) ---
                    if (layers is { Count: > 0 })
                    {
                        double lineH = PtToPx(style.FontSizePt) * 1.25;
                        double smallLineH = PtToPx(style.SmallFontSizePt) * 1.25;
                        double bandW = layerInfoR - layerInfoL;
                        var infoBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                        var leaderPen = new Pen(Brushes.Gray, Math.Max(0.5, thin * 0.6));

                        // 薄い層の逃がし先。行高ピッチのスロット列とみなして先着順に埋める
                        int slotCount = Math.Max(1, (int)((plotBottomPx - plotTopPx) / smallLineH));
                        var slotUsed = new bool[slotCount];

                        FormattedText InfoText(string s, double pt)
                        {
                            var ft = Text(s, pt, infoBrush);
                            ft.MaxTextWidth = bandW;
                            ft.MaxLineCount = 1;
                            ft.Trimming = TextTrimming.CharacterEllipsis;
                            return ft;
                        }

                        double layerTopAlt = ground!.GroundTopAltitude;
                        foreach (var layer in layers)
                        {
                            double yTop = YOf(layerTopAlt);
                            double yBtm = YOf(layer.BottomAltitude);
                            layerTopAlt = layer.BottomAltitude;
                            if (yBtm <= plotTopPx || yTop >= plotBottomPx) continue;

                            double visTop = Math.Max(plotTopPx, yTop);
                            double visBtm = Math.Min(plotBottomPx, yBtm);
                            double h = visBtm - visTop;
                            int lineCount = LayerInfoLineCount(h, lineH, smallLineH);
                            bool small = lineCount == 1 && h < lineH + 2;

                            var lines = LayerInfoLines(
                                layer.No, layer.Name, layer.LayerThickness, layer.BottomGLDepth,
                                layer.Density, layer.NValue, layer.Cohesive, Math.Max(1, lineCount));

                            if (lineCount >= 1)
                            {
                                double pt = small ? style.SmallFontSizePt : style.FontSizePt;
                                double rowH = small ? smallLineH : lineH;
                                double y = visTop + (h - rowH * lines.Length) * 0.5;
                                foreach (var line in lines)
                                {
                                    dc.DrawText(InfoText(line, pt), new Point(layerInfoL, y));
                                    y += rowH;
                                }
                                continue;
                            }

                            // 1 行も入らない薄い層は、近くの空きスロットへ引き出す
                            double yMid = (visTop + visBtm) * 0.5;
                            int want = (int)((yMid - plotTopPx) / smallLineH);
                            int slot = FindFreeSlot(slotUsed, want, 3);
                            if (slot < 0)
                            {
                                // 逃がし先が無ければ層番号だけ帯の右端に出す (諸元は土層表を参照)
                                var ftNo = Text(CircledNumber(layer.No), style.SmallFontSizePt, infoBrush);
                                dc.DrawText(ftNo, new Point(layerInfoR - ftNo.Width, yMid - ftNo.Height * 0.5));
                                continue;
                            }

                            slotUsed[slot] = true;
                            double slotY = plotTopPx + (slot + 0.5) * smallLineH;
                            var ftSlot = InfoText(lines[0], style.SmallFontSizePt);
                            dc.DrawText(ftSlot, new Point(layerInfoL, slotY - ftSlot.Height * 0.5));

                            double leaderX0 = layerInfoL + Math.Min(ftSlot.Width, bandW) + PtToPx(1);
                            double leaderX1 = layerInfoR;
                            if (leaderX1 > leaderX0)
                            {
                                double mid = (leaderX0 + leaderX1) * 0.5;
                                dc.DrawLine(leaderPen, new Point(leaderX0, slotY), new Point(mid, slotY));
                                dc.DrawLine(leaderPen, new Point(mid, slotY), new Point(leaderX1, yMid));
                            }
                        }
                    }

                    // --- 5. N値柱状図 ---
                    var masses = ground?.GroundMassesData;
                    {
                        double dataMaxN = 0;
                        if (masses != null)
                            foreach (var m in masses) dataMaxN = Math.Max(dataMaxN, m.NValue);
                        if (layers != null)
                            foreach (var l in layers) dataMaxN = Math.Max(dataMaxN, l.NValue);

                        double maxN = NAxisMax(dataMaxN);
                        double nStep = NAxisStep(maxN);
                        double XOfN(double n) => nChartL + Math.Clamp(n, 0, maxN) / maxN * (nChartR - nChartL);

                        // 目盛線とラベル。旧実装は N=0〜60 の 7 本を間隔 28px に詰め込んでおり、
                        // 見出しが互いに重なって読めなかった。重なるものは間引く (両端は必ず出す)。
                        var gridPen = new Pen(Brushes.LightGray, thin) { DashStyle = new DashStyle([4, 2], 0) };
                        var ticks = GetMultiplesInRange(0, maxN, nStep);
                        double tickLabelTop = plotTopPx - PtToPx(style.SmallFontSizePt) * 1.3;
                        double lastRight = double.NegativeInfinity;
                        for (int i = 0; i < ticks.Count; i++)
                        {
                            double x = XOfN(ticks[i]);
                            dc.DrawLine(gridPen, new Point(x, plotTopPx), new Point(x, plotBottomPx));

                            var ft = Text($"{ticks[i]:N0}", style.SmallFontSizePt, Brushes.Black);
                            double left = x - ft.Width * 0.5;
                            bool isEnd = i == 0 || i == ticks.Count - 1;
                            if (!isEnd && left < lastRight + PtToPx(2)) continue;
                            dc.DrawText(ft, new Point(left, tickLabelTop));
                            lastRight = left + ft.Width;
                        }

                        var nTitle = Text("N値", style.FontSizePt, Brushes.Black);
                        dc.DrawText(nTitle, new Point(
                            (nChartL + nChartR) * 0.5 - nTitle.Width * 0.5,
                            tickLabelTop - nTitle.Height));

                        // 層の代表 N の階段図。数値は左の土層情報帯に出すので、ここでは形だけ薄く重ねる
                        if (layers is { Count: > 0 })
                        {
                            var stepPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 70, 130, 180)), thin);
                            double stepTopAlt = ground!.GroundTopAltitude;
                            double? prevX = null;
                            foreach (var layer in layers)
                            {
                                double x = XOfN(layer.NValue);
                                double y0 = YOf(stepTopAlt);
                                double y1 = YOf(layer.BottomAltitude);
                                stepTopAlt = layer.BottomAltitude;
                                if (y1 <= plotTopPx || y0 >= plotBottomPx) continue;

                                double cy0 = Math.Max(plotTopPx, y0);
                                double cy1 = Math.Min(plotBottomPx, y1);
                                if (prevX.HasValue)
                                    dc.DrawLine(stepPen, new Point(prevX.Value, cy0), new Point(x, cy0));
                                dc.DrawLine(stepPen, new Point(x, cy0), new Point(x, cy1));
                                prevX = x;
                            }
                        }

                        if (masses is { Count: > 0 })
                        {
                            var pts = new List<Point>(masses.Count);
                            foreach (var m in masses) pts.Add(new Point(XOfN(m.NValue), YOf(m.AltitudeDepth)));

                            var polyPen = new Pen(Brushes.Black, thin);
                            for (int i = 0; i + 1 < pts.Count; i++)
                                dc.DrawLine(polyPen, pts[i], pts[i + 1]);

                            // 数値は重なる分を間引く。旧実装は全質点に印字していて、
                            // 同じ値が縦に密集して読めなかった。
                            double markerR = Math.Max(1.5, nodeRadius * 0.75);
                            double lastBottom = double.NegativeInfinity;
                            for (int i = 0; i < pts.Count; i++)
                            {
                                dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, thin), pts[i], markerR, markerR);

                                var ft = Text($"{masses[i].NValue:N0}", style.SmallFontSizePt, Brushes.Black);
                                // 上端は軸の目盛ラベルと重なるので、そこまでで止める
                                double top = Math.Max(plotTopPx, pts[i].Y - ft.Height * 0.5);
                                if (top < lastBottom) continue;
                                // 軸を振り切った点は右端でクランプしているので、その旨を示す
                                string mark = masses[i].NValue > maxN ? "▶" : "";
                                var label = mark.Length > 0
                                    ? Text($"{mark}{masses[i].NValue:N0}", style.SmallFontSizePt, Brushes.Black)
                                    : ft;
                                dc.DrawText(label, new Point(pts[i].X + markerR + PtToPx(1.5), top));
                                lastBottom = top + label.Height;
                            }
                        }
                    }

                    // --- 6. 杭体。各セグメントの径は最大径に対する比で描く ---
                    double topMostPx = double.MaxValue;
                    double bottomMostPx = double.MinValue;
                    foreach (var seg in segments)
                    {
                        double diaM = (seg.PileSection?.PileDiameter ?? 0) * 0.001;
                        double w = Math.Max(2.0, pileDrawWidthPx * (diaM / maxSegDiaM));
                        double y0 = YOf(pileHeadAlt - (seg.SegmentDepth - seg.SegmentLength));
                        double y1 = YOf(pileHeadAlt - seg.SegmentDepth);
                        dc.DrawRectangle(null, new Pen(Brushes.SteelBlue, thin),
                            new Rect(pileCenterX - w * 0.5, y0, w, Math.Max(1.0, y1 - y0)));
                        topMostPx = Math.Min(topMostPx, y0);
                        bottomMostPx = Math.Max(bottomMostPx, y1);
                    }

                    var axisPen = new Pen(Brushes.Black, thick);
                    dc.DrawLine(axisPen, new Point(pileCenterX, topMostPx), new Point(pileCenterX, bottomMostPx));
                    foreach (var seg in segments)
                    {
                        double y = YOf(pileHeadAlt - (seg.SegmentDepth - seg.SegmentLength));
                        dc.DrawEllipse(Brushes.White, axisPen, new Point(pileCenterX, y), nodeRadius, nodeRadius);
                    }
                    dc.DrawEllipse(Brushes.White, axisPen,
                        new Point(pileCenterX, YOf(pileHeadAlt - pileDepth)), nodeRadius, nodeRadius);

                    // --- 7. 地盤ばね / 地盤変位 ---
                    double dispAmpMm = 0;   // 凡例に実寸を出すため外に持つ
                    var zItems = soilPile.ZDataItems;
                    if (zItems is { Count: > 0 })
                    {
                        var springPen = new Pen(Brushes.DarkGray, thin);

                        // 隣り合うばねが融合しないよう、山の高さは節点間隔でも抑える
                        double nodePitchPx = double.MaxValue;
                        for (int i = 1; i < zItems.Count; i++)
                            nodePitchPx = Math.Min(nodePitchPx, Math.Abs(YOf(zItems[i].Z) - YOf(zItems[i - 1].Z)));
                        if (double.IsInfinity(nodePitchPx) || nodePitchPx <= 0)
                            nodePitchPx = PtToPx(style.FontSizePt);

                        // ばね記号は山の「ピッチ」を一定に保ち、長いばねほど山数を増やす。
                        // 山数を固定にすると、長いばねで 1 山が横に伸びて引き伸ばした線に見える。
                        (double Amp, int Count) Zig(double lengthPx, double ampLimitPx)
                        {
                            double len = Math.Abs(lengthPx);
                            double amp = Math.Max(2.0, Math.Min(ampLimitPx, len * 0.25));
                            double pitch = Math.Max(4.0, amp * 2.0);   // 山の傾きが 45 度前後になる
                            int count = (int)Math.Clamp(Math.Round(len / pitch), 2, style.SpringZigzagMaxCount);
                            return (amp, count);
                        }

                        if (isVertical)
                        {
                            // 下向きに伸ばすので、次の節点に届かない長さ (節点間隔の半分) までにする
                            double springLengthPx = Math.Max(style.MinSpringLengthPx * scale, 0.5 * nodePitchPx);
                            var (zigAmp, zigCount) = Zig(springLengthPx, halfWidthPx * 0.5);
                            double xLeft = pileCenterX - halfWidthPx;
                            double xRight = pileCenterX + halfWidthPx;
                            foreach (var z in zItems)
                            {
                                double y = YOf(z.Z);
                                dc.DrawLine(axisPen, new Point(xLeft, y), new Point(pileCenterX - nodeRadius, y));
                                dc.DrawLine(axisPen, new Point(pileCenterX + nodeRadius, y), new Point(xRight, y));
                                DrawZigzag(dc, new Point(xLeft, y), new Point(xLeft, y + springLengthPx), zigCount, zigAmp, springPen);
                                DrawZigzag(dc, new Point(xRight, y), new Point(xRight, y + springLengthPx), zigCount, zigAmp, springPen);
                            }
                            double toeY = YOf(zItems[^1].Z);
                            DrawZigzag(dc, new Point(pileCenterX, toeY),
                                new Point(pileCenterX, toeY + springLengthPx), zigCount, zigAmp, springPen);
                        }
                        else
                        {
                            // 地盤変位。旧実装は最大変位を常に「0.5m 相当の px」へ正規化するだけで
                            // 実寸が読めず、しかも求めた表示幅を使わないままだった。
                            // ここでは 0 の位置を固定し、正負を左右に振る。
                            double ampMm = 0;
                            foreach (var z in zItems)
                            {
                                ampMm = Math.Max(ampMm, Math.Abs(z.GroundDisp1));
                                ampMm = Math.Max(ampMm, Math.Abs(z.GroundDisp2));
                                ampMm = Math.Max(ampMm, Math.Abs(z.GroundDisp1L));
                                ampMm = Math.Max(ampMm, Math.Abs(z.GroundDisp2L));
                            }
                            dispAmpMm = ampMm;
                            double dispRightX = pileCenterX - halfWidthPx - PtToPx(2);
                            double dispZeroX = (pileBandL + dispRightX) * 0.5;
                            double dispHalfSpan = (dispRightX - pileBandL) * 0.45;
                            double XOfDisp(double mm) =>
                                dispZeroX + (ampMm > 1e-9 ? mm / ampMm : 0.0) * dispHalfSpan;

                            // ばねは節点ごとに 1 本だけ。4 本の変位曲線それぞれに引くと
                            // 同じ高さに 4 重に重なり、図が網目に潰れる
                            double markerR = Math.Max(1.5, nodeRadius * 0.7);
                            foreach (var z in zItems)
                            {
                                // 杭から最も遠い (＝図の左端側の) 変位点にばねの外端を合わせる
                                double outer = Math.Min(
                                    Math.Min(z.GroundDisp1, z.GroundDisp2),
                                    Math.Min(z.GroundDisp1L, z.GroundDisp2L));
                                double y = YOf(z.Z);
                                var start = new Point(XOfDisp(outer), y);
                                var (zigAmp, zigCount) = Zig(
                                    pileCenterX - start.X,
                                    Math.Min(halfWidthPx * 0.35, nodePitchPx * 0.35));
                                DrawZigzag(dc, start, new Point(pileCenterX, y), zigCount, zigAmp, springPen);
                            }

                            void DrawDisp(Func<PileZDataItem, double> sel, Brush brush)
                            {
                                var pts = new List<Point>(zItems.Count);
                                foreach (var z in zItems) pts.Add(new Point(XOfDisp(sel(z)), YOf(z.Z)));

                                var pen = new Pen(brush, thick);
                                for (int i = 0; i + 1 < pts.Count; i++)
                                    dc.DrawLine(pen, pts[i], pts[i + 1]);

                                foreach (var p in pts)
                                    dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, thin), p, markerR, markerR);
                            }

                            DrawDisp(z => z.GroundDisp1, Brushes.Khaki);
                            DrawDisp(z => z.GroundDisp2, Brushes.DarkKhaki);
                            DrawDisp(z => z.GroundDisp1L, Brushes.SlateBlue);
                            DrawDisp(z => z.GroundDisp2L, Brushes.DarkSlateGray);
                        }
                    }

                    // --- 8. 凡例 ---
                    {
                        double legendPt = style.SmallFontSizePt;
                        double swatch = PtToPx(legendPt) * 0.8;
                        double lx = layerInfoL;
                        double ly = plotBottomPx + PtToPx(legendPt) * 1.2;

                        void LegendText(string s)
                        {
                            var ft = Text(s, legendPt, Brushes.Black);
                            dc.DrawText(ft, new Point(lx, ly - ft.Height * 0.5));
                            lx += ft.Width + PtToPx(4);
                        }

                        void LegendSoil(string granularityClass)
                        {
                            dc.DrawRectangle(
                                new SolidColorBrush(GetSoilTypeColor(granularityClass, 128)),
                                new Pen(Brushes.Gray, Math.Max(0.5, thin * 0.6)),
                                new Rect(lx, ly - swatch * 0.5, swatch, swatch));
                            lx += swatch + PtToPx(1);
                            LegendText(granularityClass);
                        }

                        LegendSoil("粘性土");
                        LegendSoil("砂質土");
                        LegendSoil("礫質土");
                        LegendText(isVertical ? "○ 節点   ⌇ 鉛直地盤ばね" : "○ 節点   ⌇ 水平地盤ばね");
                        if (!isVertical && dispAmpMm > 0)
                            LegendText($"地盤変位 δmax = {dispAmpMm:N1} mm");
                    }
                }

                return RenderDrawingVisualToPng(dv, widthPx, heightPx);
            }

            return ExecuteOnUIThread(Render);
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
            ArgumentNullException.ThrowIfNull(pileLayoutItems);
            ArgumentNullException.ThrowIfNull(markSelector);

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
                var color = GetSoilTypeColor(layer.GranularityClass, 64);
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

        private static void DrawGroundLayersInternal(DrawingHelper helper, GroundInput ground, double _pileTopZ, double xOffset)
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
        private static void DrawNValueGraphInternal(DrawingHelper helper, GroundInput ground, double _pileTopZ, double xStart, double xEnd)
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
        /// <summary>
        /// 昇順の格子座標 <paramref name="grid"/> の中で、<paramref name="v"/> を含むセルの
        /// 左 (下) 側の索引を返す。格子の外は -1。
        /// </summary>
        internal static int FindCell(List<double> grid, double v)
        {
            if (grid == null || grid.Count < 2) return -1;
            if (v < grid[0] || v > grid[^1]) return -1;

            int lo = 0, hi = grid.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (grid[mid] <= v) lo = mid; else hi = mid;
            }
            return lo;
        }

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
                // キャンバスは scale 倍の画素で作る。ここを 1 倍のまま書くと、
                // 図全体を紙のサイズへ縮めたときに余白・カラーバー・文字だけが
                // 1/scale に縮み、文字が読めなくなる。
                double marginLeft = 10 * scale, marginRight = 80 * scale,
                       marginTop = 10 * scale, marginBottom = 10 * scale;
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

                    // 画素ごとに双線形補間して 1 枚の画像として描く。
                    //
                    // 以前はセルを 4x4 の<b>単色の小矩形</b>で塗っていた。1 区画あたり 16 色しか
                    // 使えないので、格子が粗い方向 (たいてい Y) に段差が出て縞に見えていた。
                    // 分割数を増やすと矩形の数が跳ね上がるため、画素単位で塗る。
                    int fieldW = Math.Max(1, (int)Math.Ceiling(dataW * scaleF));
                    int fieldH = Math.Max(1, (int)Math.Ceiling(dataH * scaleF));

                    // 画素 → 格子セルの索引と補間係数を、行と列で 1 回ずつ求めておく
                    var colCell = new int[fieldW];
                    var colT = new double[fieldW];
                    for (int px = 0; px < fieldW; px++)
                    {
                        double wx = minX + (px + 0.5) / scaleF;
                        int ix = FindCell(xs, wx);
                        colCell[px] = ix;
                        colT[px] = ix < 0 ? 0 : (wx - xs[ix]) / (xs[ix + 1] - xs[ix]);
                    }
                    var rowCell = new int[fieldH];
                    var rowT = new double[fieldH];
                    for (int py = 0; py < fieldH; py++)
                    {
                        double wy = maxY - (py + 0.5) / scaleF;   // 画像は上が maxY
                        int iy = FindCell(ys, wy);
                        rowCell[py] = iy;
                        rowT[py] = iy < 0 ? 0 : (wy - ys[iy]) / (ys[iy + 1] - ys[iy]);
                    }

                    var pixels = new byte[fieldW * fieldH * 4];   // BGRA
                    for (int py = 0; py < fieldH; py++)
                    {
                        int iy = rowCell[py];
                        if (iy < 0) continue;                     // 格子の外は透明のまま
                        double ty = rowT[py];
                        int rowHead = py * fieldW * 4;

                        for (int px = 0; px < fieldW; px++)
                        {
                            int ix = colCell[px];
                            if (ix < 0) continue;

                            double? v00 = grid[ix, iy];
                            double? v10 = grid[ix + 1, iy];
                            double? v01 = grid[ix, iy + 1];
                            double? v11 = grid[ix + 1, iy + 1];
                            if (v00 == null || v10 == null || v01 == null || v11 == null) continue;

                            double tx = colT[px];
                            double val = (1 - tx) * (1 - ty) * v00.Value
                                       + tx * (1 - ty) * v10.Value
                                       + (1 - tx) * ty * v01.Value
                                       + tx * ty * v11.Value;

                            double ratio = maxS > minS ? (val - minS) / (maxS - minS) : 0.5;
                            var color = DrawingHelper.GetRainbowColor(ratio);

                            int o = rowHead + px * 4;
                            pixels[o + 0] = color.B;
                            pixels[o + 1] = color.G;
                            pixels[o + 2] = color.R;
                            pixels[o + 3] = 255;
                        }
                    }

                    var field = BitmapSource.Create(
                        fieldW, fieldH, 96, 96, PixelFormats.Bgra32, null, pixels, fieldW * 4);
                    field.Freeze();
                    dc.DrawImage(field, new Rect(
                        ToPxX(minX), ToPxY(maxY), dataW * scaleF, dataH * scaleF));

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
                    double barX = widthPx - marginRight + 10 * scale;
                    double barY = marginTop + 10 * scale;
                    double barW = 15 * scale;
                    double barCellH = Math.Min(15.0 * scale,
                        (heightPx - 2 * marginTop - 40 * scale) / (colorBandCount + 1));
                    var thinPen = new Pen(Brushes.Black, 0.5 * scale);

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
                            10.0 * scale,
                            Brushes.Black,
                            1.0);
                        dc.DrawText(ft, new Point(barX + barW + 3 * scale, barY + drawIdx * barCellH + (barCellH - ft.Height) * 0.5));
                    }

                    // カラーバーのタイトル
                    var titleFt = new FormattedText(
                        "沈下量",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Meiryo"),
                        11.0 * scale,
                        Brushes.Black,
                        1.0);
                    dc.DrawText(titleFt, new Point(barX, barY - titleFt.Height - 2 * scale));

                    var unitFt = new FormattedText(
                        "(mm)",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Meiryo"),
                        10.0 * scale,
                        Brushes.Black,
                        1.0);
                    dc.DrawText(unitFt, new Point(barX, barY + colorBands.Count * barCellH + 2 * scale));
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
        private static byte[] RenderEmptyDiagram(int widthPx, int heightPx, string message, double emSizePx = 12, string fontName = "Meiryo")
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));
                var ft = new FormattedText(
                    message,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(fontName),
                    emSizePx,
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
        /// FormattedText 用の PixelsPerDip を安全に返す。
        ///
        /// <c>Application.Current.MainWindow</c> は、それを開いたスレッドからしか触れない。
        /// テストではスモークテストがウィンドウを開いて閉じたあと MainWindow の参照だけが残り、
        /// 別スレッドから <c>VisualTreeHelper.GetDpi(...)</c> を呼ぶと
        /// スレッド親和性の違反でテストホストごと落ちる
        /// （単体では通るのにクラス名の並び順しだいで全体実行だけ壊れる、という形で出る）。
        /// アクセスできないときは既定の 1.0 にフォールバックする。
        /// </summary>
        private static double SafePixelsPerDip()
        {
            try
            {
                var w = Application.Current?.MainWindow;
                if (w != null && w.CheckAccess())
                    return VisualTreeHelper.GetDpi(w).PixelsPerDip;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "PixelsPerDip の取得に失敗したため 1.0 を使用");
            }
            return 1.0;
        }

        /// <summary>
        /// UIスレッドで実行
        /// </summary>
        private static byte[] ExecuteOnUIThread(Func<byte[]> action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                // 投げ先は「生きていて、まだ終了処理に入っていない」Dispatcher に限る。
                // 終了済みスレッドの Dispatcher に Invoke すると応答が返らず、
                // 例外にもならないまま永久に待ち続ける。
                if (dispatcher != null
                    && !dispatcher.CheckAccess()
                    && dispatcher.Thread.IsAlive
                    && !dispatcher.HasShutdownStarted
                    && !dispatcher.HasShutdownFinished)
                {
                    return (byte[])dispatcher.Invoke(action);
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
