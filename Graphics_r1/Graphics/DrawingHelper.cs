using PileDesign.Graphics.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PileDesign.Graphics
{
    /// <summary>
    /// 高レベル描画ヘルパー（3D→2D変換含む）
    /// Canvas描画とWord出力で共通して使用できる描画メソッドを提供
    /// </summary>
    public class DrawingHelper
    {
        private readonly IDrawingTarget _target;
        private readonly ICoordinateTransform _transform;

        public DrawingHelper(IDrawingTarget target, ICoordinateTransform transform)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public IDrawingTarget Target => _target;
        public ICoordinateTransform Transform => _transform;

        #region 基本描画（2D）

        /// <summary>
        /// 2D線分を描画
        /// </summary>
        public void AddLine(Point start, Point end, DrawingStyle style = null)
        {
            _target.DrawLine(start, end, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 2D楕円を描画
        /// </summary>
        public void AddEllipse(Point center, double radiusX, double radiusY, DrawingStyle style = null)
        {
            _target.DrawEllipse(center, radiusX, radiusY, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 2D円を描画
        /// </summary>
        public void AddCircle(Point center, double radius, DrawingStyle style = null)
        {
            _target.DrawCircle(center, radius, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 2Dポリラインを描画
        /// </summary>
        public void AddPolyLine(IEnumerable<Point> points, bool isClosed, DrawingStyle style = null)
        {
            _target.DrawPolyLine(points, isClosed, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 2D塗りつぶしポリゴンを描画
        /// </summary>
        public void AddFilledPolygon(IEnumerable<Point> points, DrawingStyle style)
        {
            _target.DrawFilledPolygon(points, style);
        }

        /// <summary>
        /// 2Dテキストを描画
        /// </summary>
        public void AddText(string text, Point position, TextStyle style = null)
        {
            _target.DrawText(text, position, style ?? TextStyle.Default);
        }

        /// <summary>
        /// 2D矩形を描画
        /// </summary>
        public void AddRectangle(Rect rect, DrawingStyle style = null)
        {
            _target.DrawRectangle(rect, style ?? DrawingStyle.Default);
        }

        #endregion

        #region 3D→2D変換描画

        /// <summary>
        /// 3D線分を描画（自動2D変換）
        /// </summary>
        public void AddLine3D(Point3D start, Point3D end, DrawingStyle style = null)
        {
            var start2D = _transform.Transform(start);
            var end2D = _transform.Transform(end);
            _target.DrawLine(start2D, end2D, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 3D楕円を描画（透視変換による潰しを適用）
        /// </summary>
        public void AddEllipse3D(Point3D center, double radius, DrawingStyle style = null)
        {
            var center2D = _transform.Transform(center);
            double radiusX = radius * _transform.Scale;
            double radiusY = radiusX * _transform.Flattening;
            _target.DrawEllipse(center2D, radiusX, radiusY, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 3D位置にテキストを描画
        /// </summary>
        public void AddText3D(string text, Point3D position, TextStyle style = null)
        {
            var pos2D = _transform.Transform(position);
            _target.DrawText(text, pos2D, style ?? TextStyle.Default);
        }

        /// <summary>
        /// 3Dポリラインを描画（自動2D変換）
        /// </summary>
        public void AddPolyLine3D(IEnumerable<Point3D> points, bool isClosed, DrawingStyle style = null)
        {
            var points2D = points.Select(p => _transform.Transform(p));
            _target.DrawPolyLine(points2D, isClosed, style ?? DrawingStyle.Default);
        }

        #endregion

        #region 杭・地盤描画用ヘルパー

        /// <summary>
        /// 杭断面を描画（上下の楕円と側面線）
        /// </summary>
        public void DrawPileSection(Point3D top, Point3D bottom, double diameter, DrawingStyle style = null)
        {
            var top2D = _transform.Transform(top);
            var bottom2D = _transform.Transform(bottom);
            double radius2D = diameter * 0.5 * _transform.Scale;
            double flatRadius = radius2D * _transform.Flattening;

            var effectiveStyle = style ?? DrawingStyle.Default;

            // 上下の楕円
            _target.DrawEllipse(top2D, radius2D, flatRadius, effectiveStyle);
            _target.DrawEllipse(bottom2D, radius2D, flatRadius, effectiveStyle);

            // 側面線（左右）
            var topLeft = new Point(top2D.X - radius2D, top2D.Y);
            var topRight = new Point(top2D.X + radius2D, top2D.Y);
            var bottomLeft = new Point(bottom2D.X - radius2D, bottom2D.Y);
            var bottomRight = new Point(bottom2D.X + radius2D, bottom2D.Y);

            _target.DrawLine(topLeft, bottomLeft, effectiveStyle);
            _target.DrawLine(topRight, bottomRight, effectiveStyle);
        }

        /// <summary>
        /// 節点マーカーを描画
        /// </summary>
        public void DrawNodeMarker(Point3D position, double diameter, DrawingStyle style = null)
        {
            var pos2D = _transform.Transform(position);
            double radius = diameter * 0.5;
            _target.DrawEllipse(pos2D, radius, radius, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// 節点マーカーを描画（2D）
        /// </summary>
        public void DrawNodeMarker(Point position, double diameter, DrawingStyle style = null)
        {
            double radius = diameter * 0.5;
            _target.DrawEllipse(position, radius, radius, style ?? DrawingStyle.Default);
        }

        /// <summary>
        /// ポリライン＋節点マーカーを描画
        /// </summary>
        public void DrawPolyLineWithMarkers(
            IEnumerable<Point> points,
            bool isClosed,
            double markerDiameter,
            DrawingStyle lineStyle = null,
            DrawingStyle markerStyle = null,
            bool markEndPointsOnly = false)
        {
            var pointList = points?.ToList();
            if (pointList == null || pointList.Count == 0) return;

            // ポリライン
            _target.DrawPolyLine(pointList, isClosed, lineStyle ?? DrawingStyle.Default);

            // マーカー
            IEnumerable<Point> markerPoints = markEndPointsOnly && pointList.Count >= 2
                ? new[] { pointList[0], pointList[^1] }
                : pointList;

            var effectiveMarkerStyle = markerStyle ?? lineStyle ?? DrawingStyle.Default;
            double radius = markerDiameter * 0.5;

            foreach (var p in markerPoints)
            {
                _target.DrawEllipse(p, radius, radius, effectiveMarkerStyle);
            }
        }

        /// <summary>
        /// 地盤層を描画（上下の楕円と側面線）
        /// </summary>
        public void DrawSoilLayer(Point3D top, Point3D bottom, double diameter, DrawingStyle style = null)
        {
            var top2D = _transform.Transform(top);
            var bottom2D = _transform.Transform(bottom);
            double radius2D = diameter * 0.5 * _transform.Scale;
            double flatRadius = radius2D * _transform.Flattening;

            var effectiveStyle = style ?? DrawingStyle.Dashed(Colors.Gray);

            // 上下の楕円
            _target.DrawEllipse(top2D, radius2D, flatRadius, effectiveStyle);
            _target.DrawEllipse(bottom2D, radius2D, flatRadius, effectiveStyle);

            // 側面線（左右）
            var topLeft = new Point(top2D.X - radius2D, top2D.Y);
            var topRight = new Point(top2D.X + radius2D, top2D.Y);
            var bottomLeft = new Point(bottom2D.X - radius2D, bottom2D.Y);
            var bottomRight = new Point(bottom2D.X + radius2D, bottom2D.Y);

            _target.DrawLine(topLeft, bottomLeft, effectiveStyle);
            _target.DrawLine(topRight, bottomRight, effectiveStyle);
        }

        #endregion

        #region カラーバー描画

        /// <summary>
        /// カラーバーを描画
        /// </summary>
        public void DrawColorBar(
            double x, double y,
            double barWidth, double barHeight,
            IReadOnlyList<(double Value, Color Color)> colorBands,
            string title = null,
            string unit = null,
            double fontSize = 10,
            int decimalPlaces = 2)
        {
            if (colorBands == null || colorBands.Count == 0) return;

            double totalHeight = barHeight * colorBands.Count;
            double currentY = y;

            // タイトル
            if (!string.IsNullOrEmpty(title))
            {
                _target.DrawText(title, new Point(x, currentY - fontSize * 1.5), new TextStyle
                {
                    FontSize = fontSize,
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    VerticalAlignment = VerticalTextAlignment.Bottom
                });
            }

            // 各バンドを描画（上から下へ、値は大→小）
            for (int i = colorBands.Count - 1; i >= 0; i--)
            {
                var (value, color) = colorBands[i];

                // 色バンド
                var rect = new Rect(x, currentY, barWidth, barHeight);
                _target.DrawRectangle(rect, DrawingStyle.Filled(color, Colors.Black, 0.5));

                // 値ラベル
                string format = $"{{0:N{decimalPlaces}}}";
                _target.DrawText(string.Format(format, value), new Point(x - 5, currentY + barHeight * 0.5), new TextStyle
                {
                    FontSize = fontSize * 0.9,
                    HorizontalAlignment = HorizontalTextAlignment.Right,
                    VerticalAlignment = VerticalTextAlignment.Center
                });

                currentY += barHeight;
            }

            // 単位
            if (!string.IsNullOrEmpty(unit))
            {
                _target.DrawText($"({unit})", new Point(x + barWidth * 0.5, currentY + fontSize * 0.5), new TextStyle
                {
                    FontSize = fontSize * 0.8,
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    VerticalAlignment = VerticalTextAlignment.Top
                });
            }
        }

        #endregion

        #region 軸・グリッド描画

        /// <summary>
        /// 座標軸を描画
        /// </summary>
        public void DrawAxes(Point3D origin, double length)
        {
            var origin2D = _transform.Transform(origin);
            var xEnd = _transform.Transform(new Point3D(origin.X + length, origin.Y, origin.Z));
            var yEnd = _transform.Transform(new Point3D(origin.X, origin.Y + length, origin.Z));
            var zEnd = _transform.Transform(new Point3D(origin.X, origin.Y, origin.Z + length));

            _target.DrawLine(origin2D, xEnd, DrawingStyle.Solid(Colors.Red, 2));
            _target.DrawLine(origin2D, yEnd, DrawingStyle.Solid(Colors.Green, 2));
            _target.DrawLine(origin2D, zEnd, DrawingStyle.Solid(Colors.Blue, 2));

            // ラベル
            _target.DrawText("X", xEnd, new TextStyle { Color = Colors.Red, FontSize = 10 });
            _target.DrawText("Y", yEnd, new TextStyle { Color = Colors.Green, FontSize = 10 });
            _target.DrawText("Z", zEnd, new TextStyle { Color = Colors.Blue, FontSize = 10 });
        }

        /// <summary>
        /// グリッド線を描画
        /// </summary>
        public void DrawGrid(
            double minX, double maxX,
            double minY, double maxY,
            double z,
            double stepX, double stepY,
            DrawingStyle style = null)
        {
            var effectiveStyle = style ?? DrawingStyle.Dashed(Colors.LightGray, 0.5);

            // X方向のグリッド線
            for (double x = minX; x <= maxX; x += stepX)
            {
                var start = _transform.Transform(new Point3D(x, minY, z));
                var end = _transform.Transform(new Point3D(x, maxY, z));
                _target.DrawLine(start, end, effectiveStyle);
            }

            // Y方向のグリッド線
            for (double y = minY; y <= maxY; y += stepY)
            {
                var start = _transform.Transform(new Point3D(minX, y, z));
                var end = _transform.Transform(new Point3D(maxX, y, z));
                _target.DrawLine(start, end, effectiveStyle);
            }
        }

        #endregion

        #region ユーティリティ

        /// <summary>
        /// テキスト位置を調整（既存コードとの互換性用）
        /// </summary>
        public static TextStyle ConvertAlignmentCodes(string horizontalCode, string verticalCode, double angle = 0, double fontSize = 12)
        {
            var style = new TextStyle
            {
                FontSize = fontSize,
                Angle = angle
            };

            style.HorizontalAlignment = horizontalCode?.ToUpperInvariant() switch
            {
                "L" => HorizontalTextAlignment.Left,
                "C" => HorizontalTextAlignment.Center,
                "R" => HorizontalTextAlignment.Right,
                _ => HorizontalTextAlignment.Left
            };

            style.VerticalAlignment = verticalCode?.ToUpperInvariant() switch
            {
                "T" => VerticalTextAlignment.Top,
                "C" => VerticalTextAlignment.Center,
                "B" => VerticalTextAlignment.Bottom,
                _ => VerticalTextAlignment.Top
            };

            return style;
        }

        /// <summary>
        /// カラーを補間
        /// </summary>
        public static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return Color.FromArgb(
                (byte)Math.Round(a.A + (b.A - a.A) * t),
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t)
            );
        }

        /// <summary>
        /// レインボーカラーを取得（0〜1の比率）
        /// </summary>
        public static Color GetRainbowColor(double ratio)
        {
            ratio = Math.Clamp(ratio, 0.0, 1.0);

            Color[] colors =
            [
                Color.FromRgb(0, 0, 136),    // 暗青
                Color.FromRgb(0, 0, 255),    // 青
                Color.FromRgb(0, 255, 255),  // シアン
                Color.FromRgb(255, 255, 0),  // 黄
                Color.FromRgb(255, 0, 0)     // 赤
            ];

            double scaledRatio = ratio * (colors.Length - 1);
            int index = (int)scaledRatio;
            double localRatio = scaledRatio - index;

            if (index >= colors.Length - 1)
                return colors[^1];

            return LerpColor(colors[index], colors[index + 1], localRatio);
        }

        #endregion
    }
}
