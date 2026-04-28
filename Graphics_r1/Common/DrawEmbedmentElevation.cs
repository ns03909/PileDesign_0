
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Line = System.Windows.Shapes.Line;

namespace PileDesign.Common
{
    public partial class ShapeDrawer
    {
        public static void DrawEmbedmentElevation(
            Canvas canvas,
            DoatsuGoryokuBane doatsuGoryokuBane,
            GroundInput groundInput,
            string direction = "X",
            List<double> zs = null,
            double? selectedZ = null)
        {
            if (canvas == null || canvas.ActualWidth == 0 || canvas.ActualHeight == 0) return;

            // 安全チェック: 層情報が無い場合は描画を中止
            if (doatsuGoryokuBane == null || doatsuGoryokuBane.Items == null || doatsuGoryokuBane.Items.Count == 0) return;

            canvas.Children.Clear();

            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;

            // 計算部分を分離
            var (zTop0, embedmentHeight, maxValue, minValue, midValue, ratio, topMargin) =
                CalculateEmbedmentParameters(canvas, doatsuGoryokuBane, direction);

            // 無効な比率は描画せず終了
            if (ratio <= 0) return;

            // 地盤層の描画
            if (groundInput != null)
            {
                //DrawSoilLayers(canvas, groundInput, embedmentHeight, ratio, topMargin);
                DrawSoilLayers(canvas, groundInput, zTop0, ratio, topMargin);
            }

            // 各セグメントの描画
            foreach (var segment in doatsuGoryokuBane.Items)
            {
                DrawSegment(canvas, segment, direction, midValue, ratio, topMargin, zTop0, canvasWidth);
            }

            // 選択されたZラインの描画
            if (selectedZ != null)
            {
                DrawSelectedZLine(canvas, zs[0], selectedZ, doatsuGoryokuBane, midValue, direction, ratio, topMargin);
            }
            // 水平メモリ
            DrawHorizontalTicks(canvas, groundInput, ratio, topMargin, direction, midValue);
        }

        // Embedmentのパラメータ計算を分離
        private static (double zTop0, double embedmentHeight, double maxValue, double minValue, double midValue, double ratio, double topMargin)
            CalculateEmbedmentParameters(Canvas canvas, DoatsuGoryokuBane doatsuGoryokuBane, string direction)
        {
            // 安全チェック: Items が無効な場合はデフォルト値を返す
            if (doatsuGoryokuBane == null || doatsuGoryokuBane.Items == null || doatsuGoryokuBane.Items.Count == 0)
            {
                double defaultRatio = Math.Min(canvas.ActualHeight / 8.0, canvas.ActualWidth / 20.0);
                if (double.IsInfinity(defaultRatio) || double.IsNaN(defaultRatio) || defaultRatio <= 0) defaultRatio = 1.0;
                return (0.0, 0.0, 0.0, 0.0, 0.0, defaultRatio, 3 * defaultRatio);
            }

            double zTop0 = doatsuGoryokuBane.Items[0].ZTop;
            double embedmentHeight = doatsuGoryokuBane.Items[0].ZTop - doatsuGoryokuBane.Items[^1].ZBtm;

            double maxValue = double.MinValue;
            double minValue = double.MaxValue;

            foreach (var item in doatsuGoryokuBane.Items)
            {
                if (direction == "X")
                {
                    maxValue = Math.Max(maxValue, Math.Max(item.Y1, item.Y2));
                    minValue = Math.Min(minValue, Math.Min(item.Y1, item.Y2));
                }
                else if (direction == "Y")
                {
                    maxValue = Math.Max(maxValue, Math.Max(item.X1, item.X2));
                    minValue = Math.Min(minValue, Math.Min(item.X1, item.X2));
                }
            }

            // 万一すべての値が未設定だった場合のフォールバック
            if (maxValue == double.MinValue && minValue == double.MaxValue)
            {
                maxValue = minValue = 0.0;
            }

            double midValue = (maxValue + minValue) * 0.5;
            double embedmentWidth = maxValue - minValue;

            double ratio = Math.Min(
                canvas.ActualHeight / (embedmentHeight + 8.0), 
                canvas.ActualWidth / (embedmentWidth + 20));

            if (double.IsInfinity(ratio) || double.IsNaN(ratio) || ratio <= 0) ratio = 1.0;

            double topMargin = 3 * ratio;


            return (zTop0, embedmentHeight, maxValue, minValue, midValue, ratio, topMargin);
        }

        // セグメント描画を分離
        private static void DrawSegment(Canvas canvas, DoatsuGoryokuBaneItem segment, string direction, double midValue, double ratio, double topMargin, double zTop0, double canvasWidth)
        {
            double segmentLength = segment.ZTop - segment.ZBtm;
            double segmentDepth = segment.ZBtm;

            double width = direction == "X" ? segment.DY : segment.DX;
            double left = direction == "X" ? Math.Min(segment.Y1, segment.Y2) : Math.Min(segment.X1, segment.X2);

            var rectangle = new Rectangle
            {
                Width = width * ratio,
                Height = segmentLength * ratio,
                Stroke = Brushes.SkyBlue,
                StrokeThickness = 1,
                Fill = Brushes.White,
            };

            Canvas.SetLeft(rectangle, canvasWidth * 0.5 + (left - midValue) * ratio);
            Canvas.SetTop(rectangle, (-segmentLength - segmentDepth + zTop0) * ratio + topMargin);
            canvas.Children.Add(rectangle);
        }

        // 水平メモリ
        private static void DrawHorizontalTicks(Canvas canvas, GroundInput groundInput, double ratio, double topMargin, string direction, double midValue)
        {
            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;

            if (ratio <= 0) return; // 安全策

            double start = midValue - 0.5 * canvasWidth / ratio;
            double end = midValue + 0.5 * canvasWidth / ratio;

            // start > end にならないように guard
            if (end < start) return;

            // 5で割り切れる値のリストを作成（安全に型変換）
            int firstIndex = (int)Math.Ceiling(start / 5.0);
            int lastIndex = (int)Math.Floor(end / 5.0);
            if (lastIndex < firstIndex) return;

            var multiplesOf5 = Enumerable.Range(firstIndex, lastIndex - firstIndex + 1)
                                          .Select(v => v * 5.0);

            double tickLength = 10;
            foreach (var number in multiplesOf5)
            {
                double x = 0.5 * canvasWidth - (midValue - number) * ratio;
                // 直線
                var line = new Line
                {
                    Stroke = Brushes.SkyBlue,
                    StrokeThickness = 1,
                    X1 = x,
                    X2 = x,
                    Y1 = canvasHeight,
                    Y2 = canvasHeight - tickLength
                };
                canvas.Children.Add(line);

                // 文字を追加
                var text = new TextBlock
                {
                    Text = number.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 10
                };

                // 文字の位置を調整
                Canvas.SetLeft(text, x - text.ActualWidth / 2);
                Canvas.SetTop(text, canvasHeight - tickLength - text.ActualHeight);
                canvas.Children.Add(text);
            }
        }

        private static void DrawSelectedZLine(
            Canvas canvas, double z0, double? selectedZ, DoatsuGoryokuBane doatsuGoryokuBane, double midValue, string direction, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;
            double selectedMinValue = double.MaxValue;
            double selectedMaxValue = double.MinValue;
            foreach (var item in doatsuGoryokuBane.Items)
            {
                if (item.ZBtm == selectedZ || item.ZTop == selectedZ)
                {
                    if (direction == "X")
                    {
                        selectedMinValue = Math.Min(selectedMinValue, Math.Min(item.Y1, item.Y2));
                        selectedMaxValue = Math.Max(selectedMaxValue, Math.Max(item.Y1, item.Y2));
                    }
                    else if (direction == "Y")
                    {
                        selectedMinValue = Math.Min(selectedMinValue, Math.Min(item.X1, item.X2));
                        selectedMaxValue = Math.Max(selectedMaxValue, Math.Max(item.X1, item.X2));
                    }
                }
            }

            double selectedMidValue = (selectedMinValue + selectedMaxValue) * 0.5;

            double dia = 5;

            double cetnerXInPixwl = canvas.ActualWidth * 0.5 + (selectedMidValue - midValue) * ratio;
            double lengthInPixel = (selectedMaxValue - selectedMinValue) * ratio;

            double centerYInPixel = topMargin + (z0 - (double)selectedZ) * ratio;
            DrawCapsule(
                canvas,
                cetnerXInPixwl,
                centerYInPixel,
                lengthInPixel,
                dia,
                Brushes.Red,
                null,
                1);
        }

        // 長円（カプセル型）の描画
        private static void DrawCapsule(Canvas canvas, double cx, double cy, double length, double radius, Brush stroke, Brush fill, double thickness = 1)
        {
            // 左端・右端の中心
            double x1 = cx - length / 2;
            double x2 = cx + length / 2;

            // PathFigureの作成
            var figure = new PathFigure
            {
                StartPoint = new Point(x1, cy - radius)
            };

            // 左半円（下から上へ180度）
            figure.Segments.Add(new ArcSegment(
                new Point(x1, cy + radius),
                new System.Windows.Size(radius, radius),
                0, false, SweepDirection.Counterclockwise, true));

            // 下辺直線
            figure.Segments.Add(new LineSegment(new Point(x2, cy + radius), true));

            // 右半円（上から下へ180度）
            figure.Segments.Add(new ArcSegment(
                new Point(x2, cy - radius),
                new System.Windows.Size(radius, radius),
                0, false, SweepDirection.Counterclockwise, true));

            // 上辺直線
            figure.Segments.Add(new LineSegment(new Point(x1, cy - radius), true));

            figure.IsClosed = true;

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var path = new Path
            {
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = thickness,
                Data = geometry
            };

            canvas.Children.Add(path);
        }
    }
}
