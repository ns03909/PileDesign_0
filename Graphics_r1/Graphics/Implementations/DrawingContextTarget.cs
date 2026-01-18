using PileDesign.Graphics.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PileDesign.Graphics.Implementations
{
    /// <summary>
    /// DrawingContext用描画ターゲット（Word出力用）
    /// </summary>
    public class DrawingContextTarget : IDrawingTarget
    {
        private readonly DrawingContext _dc;
        private readonly Size _size;
        private readonly List<Abstractions.TextInfo> _deferredTexts = [];
        private string _currentLayer = "default";

        // Penのキャッシュ（パフォーマンス向上）
        private readonly Dictionary<(Color, double, string?), Pen> _penCache = [];

        public DrawingContextTarget(DrawingContext dc, Size size)
        {
            _dc = dc ?? throw new ArgumentNullException(nameof(dc));
            _size = size;
        }

        public Size Size => _size;

        public void DrawLine(Point start, Point end, DrawingStyle style)
        {
            if (!IsValidPoint(start) || !IsValidPoint(end)) return;

            var pen = GetOrCreatePen(style);
            _dc.DrawLine(pen, start, end);
        }

        public void DrawEllipse(Point center, double radiusX, double radiusY, DrawingStyle style)
        {
            if (!IsValidPoint(center) || radiusX <= 0 || radiusY <= 0) return;

            var pen = GetOrCreatePen(style);
            Brush? fill = style.FillColor.HasValue
                ? CreateBrush(style.FillColor.Value, style.Opacity)
                : null;

            _dc.DrawEllipse(fill, pen, center, radiusX, radiusY);
        }

        public void DrawPolyLine(IEnumerable<Point> points, bool isClosed, DrawingStyle style)
        {
            var pointList = points?.Where(IsValidPoint).ToList();
            if (pointList == null || pointList.Count < 2) return;

            var pen = GetOrCreatePen(style);
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(pointList[0], false, isClosed);
                for (int i = 1; i < pointList.Count; i++)
                {
                    ctx.LineTo(pointList[i], true, false);
                }
            }

            geometry.Freeze();
            _dc.DrawGeometry(null, pen, geometry);
        }

        public void DrawFilledPolygon(IEnumerable<Point> points, DrawingStyle style)
        {
            var pointList = points?.Where(IsValidPoint).ToList();
            if (pointList == null || pointList.Count < 3) return;

            var pen = style.StrokeThickness > 0 ? GetOrCreatePen(style) : null;
            Brush? fill = style.FillColor.HasValue
                ? CreateBrush(style.FillColor.Value, style.Opacity)
                : null;

            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(pointList[0], true, true);
                for (int i = 1; i < pointList.Count; i++)
                {
                    ctx.LineTo(pointList[i], true, false);
                }
            }

            geometry.Freeze();
            _dc.DrawGeometry(fill, pen, geometry);
        }

        public void DrawRectangle(Rect rect, DrawingStyle style)
        {
            if (rect.IsEmpty || double.IsNaN(rect.X) || double.IsNaN(rect.Y)) return;

            var pen = style.StrokeThickness > 0 ? GetOrCreatePen(style) : null;
            Brush? fill = style.FillColor.HasValue
                ? CreateBrush(style.FillColor.Value, style.Opacity)
                : null;

            _dc.DrawRectangle(fill, pen, rect);
        }

        public void DrawText(string text, Point position, TextStyle style)
        {
            if (string.IsNullOrEmpty(text) || !IsValidPoint(position)) return;

            // 遅延レンダリング用にリストに蓄積
            _deferredTexts.Add(new Abstractions.TextInfo
            {
                Text = text,
                Position = position,
                Style = style
            });
        }

        public void DrawGeometry(Geometry geometry, DrawingStyle style)
        {
            if (geometry == null) return;

            var pen = GetOrCreatePen(style);
            Brush? fill = style.FillColor.HasValue
                ? CreateBrush(style.FillColor.Value, style.Opacity)
                : null;

            _dc.DrawGeometry(fill, pen, geometry);
        }

        public void BeginLayer(string layerName)
        {
            _currentLayer = layerName ?? "default";
        }

        public void EndLayer()
        {
            _currentLayer = "default";
        }

        public void Flush()
        {
            // 蓄積されたテキストを一括描画
            foreach (var textInfo in _deferredTexts)
            {
                RenderText(textInfo);
            }
            _deferredTexts.Clear();
        }

        private void RenderText(Abstractions.TextInfo textInfo)
        {
            var style = textInfo.Style;
            var typeface = new Typeface(style.FontFamily);
            var brush = new SolidColorBrush(style.Color);

            var formattedText = new FormattedText(
                textInfo.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                style.FontSize,
                brush,
                1.0
            );

            // テキスト位置調整
            var position = AdjustTextPosition(textInfo.Position, formattedText, style);

            // 変換を適用（回転、スケール）
            bool hasTransform = Math.Abs(style.Angle) > 0.001 || Math.Abs(style.ScaleY - 1.0) > 0.001;

            if (hasTransform)
            {
                _dc.PushTransform(new TransformGroup
                {
                    Children =
                    {
                        new ScaleTransform(1.0, style.ScaleY, position.X, position.Y),
                        new RotateTransform(style.Angle, position.X, position.Y)
                    }
                });
            }

            _dc.DrawText(formattedText, position);

            if (hasTransform)
            {
                _dc.Pop();
            }
        }

        private static Point AdjustTextPosition(Point position, FormattedText formattedText, TextStyle style)
        {
            double x = position.X;
            double y = position.Y;

            // 水平方向の調整
            switch (style.HorizontalAlignment)
            {
                case HorizontalTextAlignment.Center:
                    x -= formattedText.Width / 2;
                    break;
                case HorizontalTextAlignment.Right:
                    x -= formattedText.Width;
                    break;
            }

            // 垂直方向の調整
            switch (style.VerticalAlignment)
            {
                case VerticalTextAlignment.Center:
                    y -= formattedText.Height / 2;
                    break;
                case VerticalTextAlignment.Bottom:
                    y -= formattedText.Height;
                    break;
            }

            return new Point(x, y);
        }

        private Pen GetOrCreatePen(DrawingStyle style)
        {
            var dashKey = style.DashArray != null ? string.Join(",", style.DashArray) : null;
            var key = (style.StrokeColor, style.StrokeThickness, dashKey);

            if (_penCache.TryGetValue(key, out var cachedPen))
            {
                return cachedPen;
            }

            var brush = new SolidColorBrush(style.StrokeColor);
            brush.Freeze();

            var pen = new Pen(brush, style.StrokeThickness);

            if (style.DashArray != null && style.DashArray.Count > 0)
            {
                pen.DashStyle = new DashStyle(style.DashArray, 0);
            }

            pen.Freeze();
            _penCache[key] = pen;

            return pen;
        }

        private static SolidColorBrush CreateBrush(Color color, double opacity)
        {
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)(color.A * opacity),
                color.R,
                color.G,
                color.B
            ));
            brush.Freeze();
            return brush;
        }

        private static bool IsValidPoint(Point p)
        {
            return !double.IsNaN(p.X) && !double.IsNaN(p.Y) &&
                   !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);
        }
    }
}
