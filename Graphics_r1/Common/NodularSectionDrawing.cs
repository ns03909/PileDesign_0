using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Common
{
    /// <summary>
    /// 節杭の断面図に節の情報を描き足す。
    ///
    /// 節杭の断面（軸方向に直交する切り口）は<b>軸部そのもの</b>で、ストレート杭と見分けがつかない。
    /// 節を特徴づける寸法（節部径・テーパー・節ピッチ）はすべて軸方向にあるため、
    /// 断面図だけでは節杭であることも節の形も読み取れない。そこで
    /// <list type="number">
    /// <item>断面図に<b>節部径の円</b>を一点鎖線で重ねて、軸部との径差を示す</item>
    /// <item>断面図の隣の専用キャンバスに<b>節部の側面図</b>（節 3 個分の軸方向長さ）を描いて、
    ///       節の形とピッチを示す</item>
    /// </list>
    /// を描く。断面図のキャンバスは 300×300 固定で円がほぼ埋めているため、
    /// 側面図を同じキャンバスの余白に描くことはできない。
    /// </summary>
    public static class NodularSectionDrawing
    {
        /// <summary>側面図に表示する軸方向の長さの下限 [mm]。</summary>
        public const double SideViewMinWindowMm = 2000.0;

        /// <summary>
        /// 側面図に表示する軸方向の長さ [mm]。
        /// 節が 1 個しか入らないとピッチが読み取れないので、
        /// 節 3 個（中央と上下 1 ピッチずつ）が必ず収まる長さにする。
        /// </summary>
        public static double SideViewWindow(PileSection section)
        {
            if (section == null || section.NodePitch <= 0) return SideViewMinWindowMm;
            return Math.Max(SideViewMinWindowMm, 2.0 * section.NodePitch + section.NodeTotalLength * 1.5);
        }


        // static な Brush は必ず Freeze する。凍結していない Freezable は生成したスレッドに
        // 縛られるため、別スレッドで生成した UI 要素に設定すると
        // 「親の Freezable とは異なるスレッドに属する DependencyObject」で落ちる。
        private static readonly Brush Outline = NikkenBrush.SkyBlue;
        private static readonly Brush Fill = NikkenBrush.PileConcreteFill;
        private static readonly Brush LabelBrush = FrozenBrush(Color.FromRgb(0x55, 0x55, 0x55));

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 断面図に必要な基準寸法 [mm]。節杭では節部径が軸部径より大きいので、
        /// 節部径の円が切れないようこちらを基準にする。
        /// </summary>
        public static double BaseDimension(PileSection section)
        {
            double dia = section == null ? 0
                : Math.Max(section.PileDiameter, section.IsNodularPile ? section.NodeDiameter : 0);
            return Math.Max(dia + 150.0, 1200.0);
        }

        /// <summary>断面図に節部径の円（一点鎖線）を重ねる。節杭でなければ何もしない。</summary>
        public static void DrawNodeDiameterCircle(Canvas canvas, PileSection section, double scale)
        {
            if (canvas == null || section == null) return;
            if (!section.IsNodularPile || section.NodeDiameter <= section.PileDiameter) return;

            var circle = new EllipseGeometry(
                new Point(canvas.ActualWidth * 0.5, canvas.ActualHeight * 0.5),
                section.NodeDiameter * 0.5 * scale,
                section.NodeDiameter * 0.5 * scale);

            canvas.Children.Add(new Path
            {
                Stroke = Outline,
                StrokeThickness = 1,
                // 一点鎖線。PC鋼棒の破線 (4,2) と見分けられるパターンにする
                StrokeDashArray = new DoubleCollection([8, 3, 2, 3]),
                Data = circle,
            });
        }

        /// <summary>
        /// 節部の側面図を、専用キャンバスの中央に描く。節杭でなければ何もしない。
        ///
        /// 断面図のキャンバスは円がほぼ埋めていて余白が無いため、側面図は隣に置いた
        /// 専用キャンバスに描く。縦横は同一スケールで、<see cref="SideViewWindow"/> が
        /// 収まる大きさに自動で合わせる。
        /// </summary>
        public static void DrawNodeSideView(Canvas canvas, PileSection section)
        {
            if (canvas == null || section == null) return;

            canvas.Children.Clear();

            if (!section.IsNodularPile || section.NodeDiameter <= section.PileDiameter) return;
            if (section.NodePitch <= 0) return;

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
            if (!(w > 0) || !(h > 0)) return;

            // 縦は表示窓が高さの 80%、横は節部径が幅の 70% に収まるように
            double windowMm = SideViewWindow(section);
            double scale = Math.Min(h * 0.80 / windowMm, w * 0.70 / section.NodeDiameter);
            if (scale <= 0) return;

            double cx = w * 0.5;
            double cy = h * 0.5;

            var profile = BuildSideProfile(section, windowMm);

            // 左辺（上から下へ）と、その鏡像の右辺（下から上へ）で外形を閉じる
            var points = new PointCollection();
            foreach (var (offset, radius) in profile)
                points.Add(new Point(cx - radius * scale, cy - offset * scale));
            for (int i = profile.Count - 1; i >= 0; i--)
                points.Add(new Point(cx + profile[i].Radius * scale, cy - profile[i].Offset * scale));

            canvas.Children.Add(new Polygon
            {
                Points = points,
                Stroke = Outline,
                Fill = Fill,
                StrokeThickness = 1,
            });

            // 節の折れ位置に細い横線（杭姿図と同じ表現）
            foreach (var (offset, radius) in profile)
            {
                if (Math.Abs(Math.Abs(offset) - windowMm * 0.5) < 1e-9) continue; // 上下端は外形と重なる
                double y = cy - offset * scale;
                canvas.Children.Add(new Line
                {
                    X1 = cx - radius * scale,
                    X2 = cx + radius * scale,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brushes.LightSkyBlue,
                    StrokeThickness = 0.5,
                });
            }

            AddLabel(canvas, cx, cy - windowMm * 0.5 * scale - 16, "節部側面図");
            AddLabel(canvas, cx, cy + windowMm * 0.5 * scale + 4, $"D={section.PileDiameter:N0}");
            AddLabel(canvas, cx, cy + windowMm * 0.5 * scale + 18, $"Do={section.NodeDiameter:N0}");
            AddLabel(canvas, cx, cy + windowMm * 0.5 * scale + 32, $"@{section.NodePitch:N0}");
        }

        /// <summary>
        /// 側面図の外形（軸方向オフセット [mm], 半径 [mm]）を上から順に返す。
        /// オフセットは表示窓の中心を 0 とし、上を正とする。
        /// </summary>
        private static List<(double Offset, double Radius)> BuildSideProfile(PileSection section, double windowMm)
        {
            double half = windowMm * 0.5;
            double rShaft = section.PileDiameter * 0.5;
            double rNode = section.NodeDiameter * 0.5;
            double halfTotal = section.NodeTotalLength * 0.5;
            double halfFlat = section.NodeFlatLength * 0.5;

            var profile = new List<(double Offset, double Radius)> { (half, rShaft) };

            // 窓の中心に節が来るように、ピッチ間隔で上から並べる
            int n = (int)Math.Floor(half / section.NodePitch);
            for (int k = n; k >= -n; k--)
            {
                double center = k * section.NodePitch;
                if (center + halfTotal > half || center - halfTotal < -half) continue;

                profile.Add((center + halfTotal, rShaft));
                profile.Add((center + halfFlat, rNode));
                profile.Add((center - halfFlat, rNode));
                profile.Add((center - halfTotal, rShaft));
            }

            profile.Add((-half, rShaft));
            return profile;
        }

        private static void AddLabel(Canvas canvas, double centerX, double top, string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = LabelBrush,
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(block, centerX - block.DesiredSize.Width * 0.5);
            Canvas.SetTop(block, top);
            canvas.Children.Add(block);
        }
    }
}
