using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PileDesign.Models.InputData;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// 鋼管杭の杭頭部 (コンクリート充填鋼管部) の、等価曲げ剛性 EIeq の算定に使う区間の図
    /// (「基礎部材の強度と変形性能」図 8.3 にならう)。杭断面ウィンドウの充填長 l1・鋼管厚部の長さ l2 の入力欄の下に置く。
    ///
    /// <para>EI1 (鋼管 + 充填コンクリート)・EI2 (鋼管の厚部)・EI3 (断面変化部より下の鋼管) の区間が、杭頭から
    /// L = 4D のどこにあるかを、入力した l1・l2 で描く。文章だけでは EI3 がどこの剛性か分かりにくかった (利用者の声、2026-11-21)。</para>
    ///
    /// <para>区間の位置は計算と同じ <see cref="SteelPipeSection.HeadZoneDepths"/> から取り、計算と同じく L で打ち切る
    /// (<see cref="Zones"/>)。図と計算で区間が食い違わないよう、ここで別の定義を持たない。縦は L を基準にした縮尺、
    /// 横 (杭径・肉厚) は模式。</para>
    /// </summary>
    public class SteelPipeHeadZoneDiagram : FrameworkElement
    {
        public static readonly DependencyProperty PileDiameterProperty = DependencyProperty.Register(
            nameof(PileDiameter), typeof(double), typeof(SteelPipeHeadZoneDiagram),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillLengthProperty = DependencyProperty.Register(
            nameof(FillLength), typeof(double), typeof(SteelPipeHeadZoneDiagram),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThickPartLengthProperty = DependencyProperty.Register(
            nameof(ThickPartLength), typeof(double), typeof(SteelPipeHeadZoneDiagram),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>杭径 D (mm)。</summary>
        public double PileDiameter { get => (double)GetValue(PileDiameterProperty); set => SetValue(PileDiameterProperty, value); }

        /// <summary>充填長 l1 の入力 (mm)。0 は未入力で D とみなす。</summary>
        public double FillLength { get => (double)GetValue(FillLengthProperty); set => SetValue(FillLengthProperty, value); }

        /// <summary>鋼管厚部の長さ l2 の入力 (mm)。0 は厚部なし。</summary>
        public double ThickPartLength { get => (double)GetValue(ThickPartLengthProperty); set => SetValue(ThickPartLengthProperty, value); }

        public SteelPipeHeadZoneDiagram()
        {
            Width = 420;
            Height = 290;
            SnapsToDevicePixels = true;
        }

        /// <summary>
        /// 図に描く区間の長さ (mm)。計算 (<see cref="SteelPipeSection.ComputeEIeqHead"/>) と同じく、
        /// l1 は L まで、l2 は L − l1 までに打ち切る。<paramref name="fillIsDefault"/> は l1 を D とみなしたか。
        /// </summary>
        internal static (double L1, double L2, double L3, double L, bool fillIsDefault) Zones(double diameter, double fillLength, double thickPartLength)
        {
            if (!(diameter > 0)) return (0, 0, 0, 0, fillLength <= 0);
            var (fillEnd, sectionChange, zoneEnd) = SteelPipeSection.HeadZoneDepths(diameter, fillLength, thickPartLength);
            double l1 = Math.Min(fillEnd, zoneEnd);
            double change = Math.Min(Math.Max(sectionChange, l1), zoneEnd);
            return (l1, change - l1, zoneEnd - change, zoneEnd, !(fillLength > 0));
        }

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush SteelBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly Brush ConcreteBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
        private static readonly Brush CapBrush = Brushes.White;
        private static readonly Pen Outline = new(Ink, 1.2);
        private static readonly Pen Thin = new(Ink, 0.8);
        private static readonly Pen Hatch = new(Muted, 0.7);
        private static readonly Pen Guide = new(Muted, 0.6) { DashStyle = DashStyles.Dash };

        static SteelPipeHeadZoneDiagram()
        {
            foreach (var f in new Freezable[] { Ink, Muted, SteelBrush, ConcreteBrush, Outline, Thin, Hatch, Guide }) f.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText Text(string s, double size = 11, Brush? brush = null)
                => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), size, brush ?? Ink, dip);
            void Label(string s, double x, double y, double size = 11, Brush? brush = null, bool right = false)
            {
                var t = Text(s, size, brush);
                dc.DrawText(t, new Point(right ? x - t.Width : x, y - t.Height / 2));
            }

            var (l1, l2, l3, L, fillIsDefault) = Zones(PileDiameter, FillLength, ThickPartLength);
            if (!(L > 0))
            {
                Label("杭径を入力すると、杭頭部の区間の図が出ます", 8, 20, 11, Muted);
                return;
            }

            // 配置: 左に剛性の説明、中央に杭、右に寸法線
            const double pileX = 150, pileW = 64, capTop = 8, capH = 30, wallThick = 9, wallThin = 4.5;
            double top = capTop + capH;                  // パイルキャップ下面 = 杭頭
            double zoneH = ActualHeight > 0 ? ActualHeight - top - 34 : 220;
            double scale = zoneH / L;
            double y1 = top + l1 * scale, y2 = y1 + l2 * scale, yL = top + L * scale, yEnd = yL + 18;

            // パイルキャップ
            dc.DrawRectangle(CapBrush, Outline, new Rect(pileX - 34, capTop, pileW + 68, capH));
            Label("パイルキャップ", pileX + pileW / 2 - 36, capTop + capH / 2, 10.5);

            // 充填コンクリート (l1)。斜線で示す
            var core = new Rect(pileX + wallThick, top, pileW - 2 * wallThick, Math.Max(y1 - top, 0));
            if (core.Height > 0)
            {
                dc.DrawRectangle(ConcreteBrush, null, core);
                dc.PushClip(new RectangleGeometry(core));
                for (double d = -core.Height; d < core.Width; d += 7)
                    dc.DrawLine(Hatch, new Point(core.Left + d, core.Bottom), new Point(core.Left + d + core.Height, core.Top));
                dc.Pop();
                dc.DrawRectangle(null, Thin, core);
            }

            // 鋼管: 断面変化部 (y2) までは厚い壁、その下は薄い壁。断面変化部は斜めにつなぐ
            void Walls(double yFrom, double yTo, double thick)
            {
                if (yTo <= yFrom) return;
                dc.DrawRectangle(SteelBrush, Thin, new Rect(pileX, yFrom, thick, yTo - yFrom));
                dc.DrawRectangle(SteelBrush, Thin, new Rect(pileX + pileW - thick, yFrom, thick, yTo - yFrom));
            }
            Walls(top, y2, wallThick);
            double taper = l3 > 0 ? Math.Min(8, (yEnd - y2) / 3) : 0;
            if (taper > 0)
            {
                foreach (int side in new[] { 0, 1 })
                {
                    double xo = side == 0 ? pileX : pileX + pileW;
                    double xi1 = side == 0 ? pileX + wallThick : pileX + pileW - wallThick;
                    double xi2 = side == 0 ? pileX + wallThin : pileX + pileW - wallThin;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(xo, y2), true, true);
                        c.LineTo(new Point(xi1, y2), true, false);
                        c.LineTo(new Point(xi2, y2 + taper), true, false);
                        c.LineTo(new Point(xo, y2 + taper), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(SteelBrush, Thin, g);
                }
            }
            Walls(y2 + taper, yEnd, l3 > 0 ? wallThin : wallThick);
            // 杭の外形 (下は続くので開けておく)
            dc.DrawLine(Outline, new Point(pileX, top), new Point(pileX, yEnd));
            dc.DrawLine(Outline, new Point(pileX + pileW, top), new Point(pileX + pileW, yEnd));

            // 寸法線: 区間 (l1・l2・l3) と L
            double xDim = pileX + pileW + 34, xL = xDim + 100;
            foreach (double y in new[] { top, y1, y2, yL })
                dc.DrawLine(Guide, new Point(pileX + pileW + 2, y), new Point(xL + 4, y));
            dc.DrawLine(Thin, new Point(xDim, top), new Point(xDim, yL));
            dc.DrawLine(Thin, new Point(xL, top), new Point(xL, yL));
            foreach (double y in new[] { top, y1, y2, yL })
            {
                dc.DrawEllipse(Ink, null, new Point(xDim, y), 2.2, 2.2);
                dc.DrawEllipse(Ink, null, new Point(xL, y), 2.2, 2.2);
            }
            static string Mm(double v) => v.ToString("#,0", CultureInfo.InvariantCulture);
            if (l1 > 0) Label($"l1 = {Mm(l1)}" + (fillIsDefault ? " (D)" : ""), xDim + 4, (top + y1) / 2, 10.5);
            if (l2 > 0) Label($"l2 = {Mm(l2)}", xDim + 4, (y1 + y2) / 2, 10.5);
            if (l3 > 0) Label($"l3 = {Mm(l3)}", xDim + 4, (y2 + yL) / 2, 10.5);
            Label($"L = 4D", xL + 4, (top + yL) / 2 - 7, 10.5);
            Label($"= {Mm(L)}", xL + 4, (top + yL) / 2 + 7, 10.5);

            // 区間ごとの剛性 (左)。区間が無いものは出さない
            double xText = pileX - 8;
            if (l1 > 0) Label("EI1 = 鋼管 + 充填", xText, (top + y1) / 2, 10.5, right: true);
            if (l2 > 0) Label("EI2 = 鋼管 (厚部)", xText, (y1 + y2) / 2, 10.5, right: true);
            if (l3 > 0)
            {
                Label("EI3 = 鋼管 (薄部)", xText, (y2 + yL) / 2 - 7, 10.5, right: true);
                Label("下の区間の鋼管", xText, (y2 + yL) / 2 + 7, 9.5, Muted, right: true);
            }
            Label("寸法は mm。縦は L を基準にした縮尺、横は模式", 8, yEnd + 10, 9.5, Muted);
        }
    }
}
