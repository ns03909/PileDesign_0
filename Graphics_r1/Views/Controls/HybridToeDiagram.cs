using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PileDesign.Models.InputData;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// Hybrid ニーディング工法の杭先端の詳細図 (カタログ p.4「根固め部の仕様」・p.8「平均N値算出区間」にならう)。
    /// 杭体ウィンドウの Hybrid ニーディングの入力欄の下に置く (2026-11-23 利用者の要望)。
    ///
    /// <para>根固め部 (径 D3 = e·D1、先端支持力算定位置の 2 m (e ≧ 1.7 で 3 m) 上から杭先端まで)、杭周固定部 (径 es·D1)、
    /// 杭下長 Lu、先端支持力算定位置、根固め部内の最上部節部、押込み・引抜きの杭先端平均 N 値の区間を、入力した値で描く。
    /// 高さの定義は計算 (<see cref="SoilPile"/> の Hybrid の部分) と同じものを <see cref="Layout"/> で持ち、図だけの定義は作らない。
    /// 縦横とも同じ縮尺。</para>
    /// </summary>
    public class HybridToeDiagram : FrameworkElement
    {
        private static DependencyProperty Register(string name) => DependencyProperty.Register(
            name, typeof(double), typeof(HybridToeDiagram),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty NodeDiameterProperty = Register(nameof(NodeDiameter));
        public static readonly DependencyProperty ExpansionRatioProperty = Register(nameof(ExpansionRatio));
        public static readonly DependencyProperty ExcavationRatioProperty = Register(nameof(ExcavationRatio));
        public static readonly DependencyProperty PileBelowLengthProperty = Register(nameof(PileBelowLength));
        public static readonly DependencyProperty TopNodeHeightProperty = Register(nameof(TopNodeHeight));

        /// <summary>節部径 D1 (mm)。</summary>
        public double NodeDiameter { get => (double)GetValue(NodeDiameterProperty); set => SetValue(NodeDiameterProperty, value); }
        /// <summary>設計拡径比 e。</summary>
        public double ExpansionRatio { get => (double)GetValue(ExpansionRatioProperty); set => SetValue(ExpansionRatioProperty, value); }
        /// <summary>設計掘削径比 es。</summary>
        public double ExcavationRatio { get => (double)GetValue(ExcavationRatioProperty); set => SetValue(ExcavationRatioProperty, value); }
        /// <summary>杭下長 Lu (m)。</summary>
        public double PileBelowLength { get => (double)GetValue(PileBelowLengthProperty); set => SetValue(PileBelowLengthProperty, value); }
        /// <summary>根固め部内の最上部節部の高さ (m、杭先端から)。0 は未入力。</summary>
        public double TopNodeHeight { get => (double)GetValue(TopNodeHeightProperty); set => SetValue(TopNodeHeightProperty, value); }

        public HybridToeDiagram()
        {
            Width = 640;
            Height = 380;
            SnapsToDevicePixels = true;
        }

        /// <summary>
        /// 図に描く高さ (m、杭先端から上を正) と径 (m)。計算と同じ定義:
        /// 先端支持力算定位置 = Lu (<see cref="SoilPile.HybridToeEvaluationAltitude"/>)、根固め部上端 = Lu + 2 m (e ≧ 1.7 で 3 m)
        /// (<see cref="SoilPile.HybridBulbTopAboveEvaluation"/>)、押込みの平均 N 値の区間 = 算定位置の 1·D1 下〜根固め部上端、
        /// 引抜きの区間 = 最上部節部 (未入力なら算定位置) 〜その 4·D1 上 (<see cref="SoilPile.HybridUpliftNBasisAltitude"/>)。
        /// e・es は計算と同じく 1.0〜2.0、es ≦ e に収める。
        /// </summary>
        internal static (double D1, double D3, double Dfix, double Eval, double BulbTop, double PushFrom, double UpliftFrom, double UpliftTo, bool TopNodeGiven)
            Layout(double nodeDiameterMm, double e, double es, double lu, double topNode)
        {
            double d1 = nodeDiameterMm / 1000.0;
            double eUsed = Math.Clamp(double.IsFinite(e) ? e : 1.0, SoilPile.HybridRatioMin, SoilPile.HybridRatioMax);
            double esUsed = Math.Clamp(double.IsFinite(es) ? es : 1.0, SoilPile.HybridRatioMin, Math.Min(SoilPile.HybridRatioMax, eUsed));
            double eval = Math.Max(lu, 0);
            double bulbTop = eval + (eUsed >= SoilPile.HybridLargeExpansionThreshold ? 3.0 : 2.0);
            bool given = topNode > 0;
            double upliftFrom = given ? topNode : eval;
            return (d1, eUsed * d1, esUsed * d1, eval, bulbTop, eval - d1, upliftFrom, upliftFrom + 4.0 * d1, given);
        }

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush PileBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        /// <summary>根固め部。カタログの色分けにならい e ≦ 1.6 は青、e ≧ 1.7 は緑。</summary>
        private static readonly Brush BulbBlue = new SolidColorBrush(Color.FromRgb(0xC6, 0xE6, 0xF5));
        private static readonly Brush BulbGreen = new SolidColorBrush(Color.FromRgb(0xCD, 0xE6, 0xA9));
        private static readonly Brush FixBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xEC, 0xDC));
        private static readonly Brush PushBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x6D, 0xB4));
        private static readonly Brush UpliftBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x50, 0x20));
        private static readonly Pen Outline = new(Ink, 1.2);
        private static readonly Pen Thin = new(Ink, 0.8);
        private static readonly Pen Guide = new(Muted, 0.6) { DashStyle = DashStyles.Dash };
        private static readonly Pen PushPen = new(PushBrush, 1.4);
        private static readonly Pen UpliftPen = new(UpliftBrush, 1.4);

        static HybridToeDiagram()
        {
            foreach (var f in new Freezable[] { Ink, Muted, PileBrush, BulbBlue, BulbGreen, FixBrush, PushBrush, UpliftBrush, Outline, Thin, Guide, PushPen, UpliftPen })
                f.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface("Yu Gothic UI");
            void Label(string s, double x, double y, double size = 10.5, Brush? brush = null, bool right = false)
            {
                var t = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush ?? Ink, dip);
                dc.DrawText(t, new Point(right ? x - t.Width : x, y - t.Height / 2));
            }

            var g = Layout(NodeDiameter, ExpansionRatio, ExcavationRatio, PileBelowLength, TopNodeHeight);
            if (!(g.D1 > 0))
            {
                Label("杭体の最下段の区間に断面 (節部径) を入れると、杭先端の図が出ます", 8, 20, 11, Muted);
                return;
            }
            Label("Hybrid ニーディング工法 杭先端 (カタログ p.4・p.8 にならう)", 8, 10, 11.5);

            // 縦: 杭先端の 1·D1 下 (押込みの区間の下端) 〜 図の上端 (根固め部上端・引抜きの区間の上端の高い方 + 0.5 m)
            double yTopM = Math.Max(g.BulbTop, g.UpliftTo) + 0.5, yBottomM = Math.Min(g.PushFrom, 0) - 0.4;
            double plotTop = 32, plotBottom = (ActualHeight > 0 ? ActualHeight : Height) - 46;
            double scale = (plotBottom - plotTop) / (yTopM - yBottomM);
            double Y(double m) => plotBottom - (m - yBottomM) * scale;
            double cx = 180;
            double W(double m) => m * scale;

            // 杭周固定部 (es·D1)。根固め部の上端から図の上端まで (es = 1.0 なら杭径と同じなので描かない)
            if (g.Dfix > g.D1 + 1e-9)
                dc.DrawRectangle(FixBrush, Thin, new Rect(cx - W(g.Dfix) / 2, Y(yTopM), W(g.Dfix), Y(g.BulbTop) - Y(yTopM)));
            // 根固め部 (D3)。杭先端から根固め部上端まで
            var bulb = new Rect(cx - W(g.D3) / 2, Y(g.BulbTop), W(g.D3), Y(0) - Y(g.BulbTop));
            dc.DrawRectangle(ExpansionRatio >= SoilPile.HybridLargeExpansionThreshold ? BulbGreen : BulbBlue, Outline, bulb);
            // 杭 (節部径 D1 の幅で模式に描く)。杭先端で閉じる
            var pile = new Rect(cx - W(g.D1) / 2, Y(yTopM), W(g.D1), Y(0) - Y(yTopM));
            dc.DrawRectangle(PileBrush, Outline, pile);
            // 根固め部内の最上部節部 (入力があれば、杭より少し広い帯で描く)
            if (g.TopNodeGiven)
            {
                double yn = Y(g.UpliftFrom);
                dc.DrawRectangle(PileBrush, Outline, new Rect(cx - W(g.D1) / 2 - 4, yn - 3, W(g.D1) + 8, 6));
            }
            // 先端支持力算定位置 (黒丸)
            dc.DrawEllipse(Ink, null, new Point(cx, Y(g.Eval)), 3.2, 3.2);

            // 左の寸法線: 杭下長 Lu と 根固め部の長さ (2 m / 3 m)
            static string M(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            double xDim = cx - Math.Max(W(g.D3), W(g.Dfix)) / 2 - 26;
            foreach (double h in new[] { 0.0, g.Eval, g.BulbTop })
            {
                dc.DrawLine(Guide, new Point(xDim - 4, Y(h)), new Point(cx - W(g.D1) / 2, Y(h)));
                dc.DrawEllipse(Ink, null, new Point(xDim, Y(h)), 2.2, 2.2);
            }
            dc.DrawLine(Thin, new Point(xDim, Y(0)), new Point(xDim, Y(g.BulbTop)));
            if (g.Eval > 0) Label($"Lu = {M(g.Eval)} m", xDim - 6, (Y(0) + Y(g.Eval)) / 2, 10, right: true);
            else Label("Lu = 0 (算定位置 = 杭先端)", xDim - 6, Y(0) + 9, 9.5, Muted, right: true);
            Label($"{M(g.BulbTop - g.Eval)} m", xDim - 6, (Y(g.Eval) + Y(g.BulbTop)) / 2, 10, right: true);
            Label(ExpansionRatio >= SoilPile.HybridLargeExpansionThreshold ? "(e ≧ 1.7)" : "(e ≦ 1.6)", xDim - 6, (Y(g.Eval) + Y(g.BulbTop)) / 2 + 13, 9.5, Muted, right: true);

            // 右: 平均 N 値の区間 (押込み・引抜き) と、位置の名前
            double xPush = cx + Math.Max(W(g.D3), W(g.Dfix)) / 2 + 22, xUp = xPush + 18;
            void Bracket(Pen pen, double x, double fromM, double toM)
            {
                dc.DrawLine(pen, new Point(x, Y(fromM)), new Point(x, Y(toM)));
                dc.DrawLine(pen, new Point(x - 4, Y(fromM)), new Point(x + 4, Y(fromM)));
                dc.DrawLine(pen, new Point(x - 4, Y(toM)), new Point(x + 4, Y(toM)));
            }
            Bracket(PushPen, xPush, g.PushFrom, g.BulbTop);
            Bracket(UpliftPen, xUp, g.UpliftFrom, g.UpliftTo);
            // 括弧の上端に短い名前。説明は下の凡例に書く
            Label("押込み", xPush - 2, Y(g.BulbTop) - 9, 9.5, PushBrush, right: true);
            Label("引抜き", xUp + 2, Y(g.UpliftTo) - 9, 9.5, UpliftBrush);

            // 高さの名前 (右)。引出しの破線を引き、文字は重ならないよう 15 px 以上空けて並べる
            double xText = xUp + 26;
            var levels = new System.Collections.Generic.List<(double M, string Text, Brush Brush)>
            {
                (0, "杭先端", Muted),
                (g.Eval, "● 先端支持力算定位置 (これより上が杭周面摩擦の対象)", Ink),
                (g.BulbTop, "根固め部の上端", Muted),
            };
            if (g.TopNodeGiven) levels.Add((g.UpliftFrom, $"根固め部内の最上部節部 (杭先端から {M(g.UpliftFrom)} m)", Ink));
            levels.Sort((a, b) => a.M.CompareTo(b.M));   // 下から
            double lastY = double.PositiveInfinity;
            foreach (var (m, text, brush) in levels)
            {
                double y = Math.Min(Y(m), lastY - 15);
                dc.DrawLine(Guide, new Point(cx + W(g.D1) / 2, Y(m)), new Point(xText - 4, y));
                Label(text, xText, y, 10, brush);
                lastY = y;
            }

            // 下: 凡例と径
            static string Mm(double m) => (m * 1000).ToString("#,0", CultureInfo.InvariantCulture);
            double yLegend = plotBottom + 4;
            Label("押込みの杭先端平均N値の区間: 先端支持力算定位置の 1·D1 下〜根固め部上端", 8, yLegend, 9.5, PushBrush);
            Label(g.TopNodeGiven ? "引抜きの杭先端平均N値の区間: 根固め部内の最上部節部から上方 4·D1"
                                 : "引抜きの杭先端平均N値の区間: 先端支持力算定位置から上方 4·D1 (根固め部内の最上部節部が未入力)",
                8, yLegend + 14, 9.5, UpliftBrush);
            string fix = g.Dfix > g.D1 + 1e-9 ? $"、杭周固定部 es·D1 = {Mm(g.Dfix)}" : "";
            Label($"D1 = {Mm(g.D1)}、根固め部 D3 = e·D1 = {Mm(g.D3)}{fix} (mm)。縦横とも同じ縮尺", 8, yLegend + 28, 9.5, Muted);
        }
    }
}
