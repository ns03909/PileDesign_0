using PileDesign.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using TextAlignment = System.Windows.TextAlignment;
using PileDesign.Services;

namespace PileDesign.Services
{
    public class ColorBaredGeometry
    {
        public PathGeometry PathGeometry { get; set; }
        public double TopRange { get; set; }
        public double BottomRange { get; set; }
        public Color Color { get; set; }

        public ColorBaredGeometry()
        {
            PathGeometry = new();
        }

        public void DrawPathes(Canvas canvas)
        {
            canvas.Children.Add(new Path()
            {
                Stroke = new SolidColorBrush(Color),
                Data = PathGeometry,
                Name = "ColorBaredGeometry"
            });
        }
    }

    public class ColorBar
    {
        // カラーバーからのカラー取得メソッド
        public static Color GetColor(double valueToInterpolate)
        {
            //    if (valueToInterpolate < 0 || 1 < valueToInterpolate)
            //    { MessageService.Show("no."); }
            valueToInterpolate = Math.Max(Math.Min(valueToInterpolate, 1), 0);
            // Turbo に類似した 6 色レインボー (青→シアン→緑→黄→橙→赤)。
            // 旧 5 色 (#000088→#0000FF→#00FFFF→#FFFF00→#FF0000) は低値域が
            // 濃い indigo で潰れて変化が分かりにくかったため、起点を彩度の高い青に変更し
            // 中間に緑 (#1FCB6F) を挿入して 1/3 ずつのレンジで色相が切替わるようにした。
            List<Color> colors =
            [
                (Color)ColorConverter.ConvertFromString("#1F3FCB"),  // 鮮青
                (Color)ColorConverter.ConvertFromString("#1F9FE8"),  // シアン青
                (Color)ColorConverter.ConvertFromString("#1FCB6F"),  // 緑
                (Color)ColorConverter.ConvertFromString("#E8E81F"),  // 黄
                (Color)ColorConverter.ConvertFromString("#E89F1F"),  // 橙
                (Color)ColorConverter.ConvertFromString("#CB1F1F"),  // 赤
            ];

            List<double> points = [];
            for (int i = 0; i < colors.Count; i++)
            {
                points.Add((double)i / (colors.Count - 1));
            }
            return InterpolateColor(points, colors, valueToInterpolate);
        }

        // 内挿
        static Color InterpolateColor(List<double> points, List<Color> colors, double value)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (value >= points[i] && value <= points[i + 1])
                {
                    double t = (value - points[i]) / (points[i + 1] - points[i]);
                    return Color.FromRgb(
                        (byte)(colors[i].R + t * (colors[i + 1].R - colors[i].R)),
                        (byte)(colors[i].G + t * (colors[i + 1].G - colors[i].G)),
                        (byte)(colors[i].B + t * (colors[i + 1].B - colors[i].B))
                    );
                }
            }
            throw new ArgumentOutOfRangeException("Value is out of range");
        }

        public static void DrawStepColorBar(
            Canvas colorBarCanvas,
            List<ColorBaredGeometry> colorBaredGeometries,
            string title = "title",
            string unit = "mm",
            double? minValue = 100,
            double? maxValue = 1000,
            string formatVal = "N3",
            double labelSize = 10.0) // 追加: ラベルサイズに基づくスケール（既定 10）
        {
            if (colorBarCanvas == null) return;
            if (double.IsNaN(colorBarCanvas.Height) || double.IsNaN(colorBarCanvas.Width))
                return;

            colorBarCanvas.IsHitTestVisible = false;
            colorBarCanvas.RenderTransform = Transform.Identity;

            int numberOfColors = colorBaredGeometries?.Count ?? 0;
            if (numberOfColors == 0 || colorBaredGeometries == null)
            {
                colorBarCanvas.Children.Clear();
                return;
            }

            // scale: viewModel.LabelSize==10 を基準に比率を計算
            double scale = labelSize / 10.0;

            // --- 表示用リスト: 上が max になるように反転して使う ---
            var displayList = new List<ColorBaredGeometry>(numberOfColors);
            for (int k = numberOfColors - 1; k >= 0; k--)
            {
                displayList.Add(colorBaredGeometries[k]);
            }

            // バンド境界（重複除去して昇順）
            var boundSet = new HashSet<double>();
            for (int i = 0; i < colorBaredGeometries.Count; i++)
            {
                boundSet.Add(colorBaredGeometries[i].BottomRange);
                boundSet.Add(colorBaredGeometries[i].TopRange);
            }
            var boundaries = new List<double>(boundSet);
            boundaries.Sort();

            // 目盛り表示は boundaries の逆順（上→下）
            var revBoundaries = new List<double>(boundaries.Count);
            for (int i = boundaries.Count - 1; i >= 0; i--) revBoundaries.Add(boundaries[i]);

            // スケーリング適用（基準値）
            double barHeight = 10.0 * scale;
            double barWidth = 20.0 * scale;
            double titleFont = 12.0 * scale;
            double unitFont = 11.0 * scale;
            double tickFont = 10.0 * scale;
            double miNMaxFontSize = 10.0 * scale;
            double extraVerticalSpacing = 20.0 * scale;
            double labelOffset = 8.0 * scale;
            double smallMargin = 8.0 * scale;
            double outerMargin = 5.0 * scale;

            colorBarCanvas.Children.Clear();

            // --- 1. タイトル・単位（カラーバーの上） ---
            double y = 0;
            double titleWidth = 0;
            double titleHeight = 0;
            if (!string.IsNullOrEmpty(title))
            {
                var titleBlock = new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.Black,
                    FontSize = titleFont,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Right
                };
                titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                titleWidth = titleBlock.DesiredSize.Width;
                titleHeight = titleBlock.DesiredSize.Height;
                Canvas.SetLeft(titleBlock, barWidth - titleWidth);
                Canvas.SetTop(titleBlock, y);
                colorBarCanvas.Children.Add(titleBlock);
                y += titleHeight + 2 * scale;
            }

            if (!string.IsNullOrEmpty(unit))
            {
                var unitBlock = new TextBlock
                {
                    Text = unit,
                    Foreground = Brushes.Black,
                    FontSize = unitFont,
                    TextAlignment = TextAlignment.Right
                };
                unitBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double unitWidth = unitBlock.DesiredSize.Width;
                double unitHeight = unitBlock.DesiredSize.Height;
                Canvas.SetLeft(unitBlock, barWidth - unitWidth);
                Canvas.SetTop(unitBlock, y);
                colorBarCanvas.Children.Add(unitBlock);
                y += unitHeight + 4 * scale;
            }

            // 表示フォーマット: ユーザー指定があれば優先、それ以外はバンド幅に応じて自動決定
            string format;
            if (!string.IsNullOrEmpty(formatVal))
            {
                // formatVal が composite 型 ("{0:N3}") の場合と単純な numeric format ("N3") の両方に対応
                format = formatVal;
            }
            else if (displayList.Count > 0)
            {
                format = GetFormat(Math.Abs(displayList[0].TopRange - displayList[0].BottomRange));
            }
            else
            {
                format = "N4";
            }

            // 少し下にオフセットしてから描画（タイトルと重ならないように）
            y += extraVerticalSpacing;

            // --- 2. カラーバー本体（上が max）---
            double yOffset = y;

            int numberOfTicks = revBoundaries.Count;
            double maxLabelRight = 0;
            var labelRights = new List<double>(numberOfTicks);

            // 重複するフォーマット済みラベルを除去（全値同一時に同じ値が並ぶのを防ぐ）
            // max/minラベルは別途描画されるため、tick側で重複する場合もスキップ
            string prevLabelText = null;
            for (int i = 0; i < numberOfTicks; i++)
            {
                double value = revBoundaries[i];

                // フォーマット文字列が composite 形式かどうかを判定して表示文字列を作る
                string labelText;
                if (format.Contains("{"))
                    labelText = string.Format(format, value);
                else
                    labelText = value.ToString(format);

                // 直前と同じラベルテキストの場合はスキップ
                if (i > 0 && labelText == prevLabelText)
                {
                    labelRights.Add(0);
                    continue;
                }
                prevLabelText = labelText;

                var tick = new Line
                {
                    X1 = 0,
                    Y1 = yOffset + i * barHeight,
                    X2 = -5 * scale,
                    Y2 = yOffset + i * barHeight,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5 * scale
                };

                var label = new TextBlock
                {
                    Text = labelText,
                    Foreground = Brushes.Black,
                    FontSize = tickFont
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelWidth = label.DesiredSize.Width;
                double labelHeight = label.DesiredSize.Height;
                double labelLeft = -labelOffset - labelWidth;
                double labelRight = labelLeft + labelWidth;
                labelRights.Add(labelRight);
                if (labelRight > maxLabelRight) maxLabelRight = labelRight;

                Canvas.SetLeft(label, labelLeft);
                Canvas.SetTop(label, yOffset + i * barHeight - labelHeight / 2);

                colorBarCanvas.Children.Add(tick);
                colorBarCanvas.Children.Add(label);
            }

            // カラーバー矩形（displayList の順に上→下）
            for (int j = 0; j < displayList.Count; j++)
            {
                var band = displayList[j];
                var brush = new SolidColorBrush(band.Color);
                if (brush.CanFreeze) brush.Freeze();

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = brush,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5 * scale
                };
                Canvas.SetLeft(rect, 0);
                Canvas.SetTop(rect, yOffset + j * barHeight);
                colorBarCanvas.Children.Add(rect);
            }

            // --- 3. min/max 表示（GetFormat に依る同一フォーマット） ---
            double minDisp = minValue ?? (boundaries.Count > 0 ? boundaries.First() : 0.0);
            double maxDisp = maxValue ?? (boundaries.Count > 0 ? boundaries[^1] : 0.0);

            // max: バーの上
            string maxText = format.Contains("{") ? string.Format(format, maxDisp) : maxDisp.ToString(format);
            var maxValueBlock = new TextBlock
            {
                Text = maxText,
                Foreground = Brushes.Black,
                FontSize = miNMaxFontSize,
                TextAlignment = TextAlignment.Right
            };
            maxValueBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double maxValueWidth = maxValueBlock.DesiredSize.Width;
            double maxValueHeight = maxValueBlock.DesiredSize.Height;
            double maxY = yOffset - maxValueHeight - smallMargin;
            Canvas.SetLeft(maxValueBlock, maxLabelRight - maxValueWidth - smallMargin);
            Canvas.SetTop(maxValueBlock, maxY);
            colorBarCanvas.Children.Add(maxValueBlock);

            var maxLabelBlock = new TextBlock { Text = "max", Foreground = Brushes.Black, FontSize = miNMaxFontSize };
            maxLabelBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double maxLabelWidth = maxLabelBlock.DesiredSize.Width;
            Canvas.SetLeft(maxLabelBlock, barWidth - maxLabelWidth);
            Canvas.SetTop(maxLabelBlock, maxY);
            colorBarCanvas.Children.Add(maxLabelBlock);

            // min: バーの下
            string minText = format.Contains("{") ? string.Format(format, minDisp) : minDisp.ToString(format);
            var minValueBlock = new TextBlock
            {
                Text = minText,
                Foreground = Brushes.Black,
                FontSize = miNMaxFontSize,
                TextAlignment = TextAlignment.Right
            };
            minValueBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double minValueWidth = minValueBlock.DesiredSize.Width;
            double minValueHeight = minValueBlock.DesiredSize.Height;
            double minY = yOffset + numberOfColors * barHeight + smallMargin;
            Canvas.SetLeft(minValueBlock, maxLabelRight - minValueWidth - smallMargin);
            Canvas.SetTop(minValueBlock, minY);
            colorBarCanvas.Children.Add(minValueBlock);

            var minLabelBlock = new TextBlock { Text = "min", Foreground = Brushes.Black, FontSize = miNMaxFontSize };
            minLabelBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double minLabelWidth = minLabelBlock.DesiredSize.Width;
            Canvas.SetLeft(minLabelBlock, barWidth - minLabelWidth);
            Canvas.SetTop(minLabelBlock, minY);
            colorBarCanvas.Children.Add(minLabelBlock);

            // 背景矩形（半透明白）
            double top = maxY - outerMargin;
            double bottom = minY + minValueBlock.DesiredSize.Height + outerMargin;
            double backgroundHeight = bottom - top;
            double left = Math.Min(Math.Min(Math.Min(barWidth - titleWidth, maxLabelRight - minValueWidth - smallMargin), maxLabelRight - maxValueWidth - smallMargin), barWidth - maxLabelWidth) - outerMargin;
            double right = barWidth + outerMargin;
            double backgroundWidth = right - left;
            var background = new Rectangle
            {
                Width = backgroundWidth,
                Height = backgroundHeight,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(192 * scale), 255, 255, 255))
            };
            Canvas.SetLeft(background, left);
            Canvas.SetTop(background, top);
            colorBarCanvas.Children.Insert(0, background);
        }


        private static string GetFormat(double step)
        {
            if (step >= 1)
            {
                return "N0";
            }
            else if (step >= 0.1)
            {
                return "N1";
            }
            else if (step >= 0.01)
            {
                return "N2";
            }
            else if (step >= 0.01)
            {
                return "N3";
            }
            else if (step >= 0.001)
            {
                return "N4";
            }
            else if (step >= 0.0001)
            {
                return "N5";
            }
            else if (step >= 0.00001)
            {
                return "N6";
            }
            else if (step >= 0.000001)
            {
                return "N7";
            }
            else if (step >= 0.0000001)
            {
                return "N8";
            }
            else if (step >= 0.00000001)
            {
                return "N9";
            }
            else
            {
                return "N9";
            }
        }

        // キャンバスにカラーバーを描画
        public static void DrawColorBar(Canvas colorBarCanvas)
        {
            if (double.IsNaN(colorBarCanvas.Height) || double.IsNaN(colorBarCanvas.Width))
                return;

            double minValue = 0;
            double maxValue = 100;
            int numberOfColors = 100;
            double barHeight = colorBarCanvas.Height;
            double barWidth = colorBarCanvas.Width;

            // LinearGradientBrushを使用してカラーバーの背景を描画
            LinearGradientBrush gradientBrush = new()
            {
                StartPoint = new Point(0.5, 1),
                EndPoint = new Point(0.5, 0)
            };

            for (int i = 0; i <= numberOfColors; i++)
            {
                double offset = i / (double)numberOfColors;
                Color color = GetColorForValue(offset, minValue, maxValue);
                gradientBrush.GradientStops.Add(new GradientStop(color, offset));
            }

            // Rectangleでカラーバーを描画
            Rectangle colorBar = new()
            {
                Width = barWidth,
                Height = barHeight,
                Fill = gradientBrush
            };

            Canvas.SetLeft(colorBar, 0);
            Canvas.SetTop(colorBar, 0);

            colorBarCanvas.Children.Clear();
            colorBarCanvas.Background = NikkenBrush.SkyBlue;
            colorBarCanvas.Children.Add(colorBar);

            // カラーバー上に目盛りを追加
            int numberOfTicks = 11;
            for (int i = 0; i < numberOfTicks; i++)
            {
                double value = minValue + (maxValue - minValue) * (i / (double)(numberOfTicks - 1));
                double yOffset = barHeight - (value - minValue) / (maxValue - minValue) * barHeight;

                Line tick = new()
                {
                    X1 = 0, //barWidth,
                    Y1 = yOffset,
                    X2 = -5, //barWidth + 10,
                    Y2 = yOffset,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                };

                TextBlock label = new()
                {
                    Text = value.ToString("F0"),
                    Foreground = Brushes.Black,
                    FontSize = 10,
                };

                // Measure the TextBlock to get its actual height
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelHeight = label.DesiredSize.Height;
                double labelWidth = label.DesiredSize.Width;

                Canvas.SetLeft(label, -8 - labelWidth); // barWidth + 15);
                Canvas.SetTop(label, yOffset - labelHeight / 2);

                colorBarCanvas.Children.Add(tick);
                colorBarCanvas.Children.Add(label);
            }
        }

        // 仮
        private static Color GetColorForValue(double value, double minValue, double maxValue)
        {
            // ここでは単純な赤から青へのグラデーションを使用しますが、適宜調整してください。
            byte r = (byte)(255 * (1 - value));
            byte b = (byte)(255 * value);
            return Color.FromRgb(r, 0, b);
        }
    }
}

