using LiveChartsCore.SkiaSharpView.Painting;
using PileDesign.Common;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using TextAlignment = System.Windows.TextAlignment;

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
            //    { MessageBox.Show("no."); }
            valueToInterpolate = Math.Max(Math.Min(valueToInterpolate, 1), 0);
            //List<double> points = [0, 0.2, 0.4, 0.6, 0.8, 1.0];
            List<Color> colors =
            [
                (Color)ColorConverter.ConvertFromString("#000088"),
                (Color)ColorConverter.ConvertFromString("#0000FF"),
                (Color)ColorConverter.ConvertFromString("#00FFFF"),
                (Color)ColorConverter.ConvertFromString("#FFFF00"),
                (Color)ColorConverter.ConvertFromString("#FF0000"),
                //(Color)ColorConverter.ConvertFromString("#FFFFFF")
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

        // 
        public static SolidColorPaint GetSolidColorPaint(double valueToInterpolate)
        {
            //List<double> points = [0, 0.2, 0.4, 0.6, 0.8, 1.0];
            List<Color> colors =
            [
                (Color)ColorConverter.ConvertFromString("#000088"),
                (Color)ColorConverter.ConvertFromString("#0000FF"),
                (Color)ColorConverter.ConvertFromString("#00FFFF"),
                (Color)ColorConverter.ConvertFromString("#FFFF00"),
                (Color)ColorConverter.ConvertFromString("#FF0000"),
                //(Color)ColorConverter.ConvertFromString("#FFFFFF")
            ];

            List<double> points = [];
            for (int i = 0; i < colors.Count; i++)
            {
                points.Add((double)i / (colors.Count - 1));
            }
            return InterpolateSolidColorPaint(points, colors, valueToInterpolate);
        }

        // 
        static SolidColorPaint InterpolateSolidColorPaint(List<double> points, List<Color> colors, double value)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (value >= points[i] && value <= points[i + 1])
                {
                    double t = (value - points[i]) / (points[i + 1] - points[i]);
                    return new(new(
                        (byte)(colors[i].R + t * (colors[i + 1].R - colors[i].R)),
                        (byte)(colors[i].G + t * (colors[i + 1].G - colors[i].G)),
                        (byte)(colors[i].B + t * (colors[i + 1].B - colors[i].B))
                    ));
                }
            }
            throw new ArgumentOutOfRangeException("Value is out of range");

        }


        // キャンバスにステップカラーバーを描画
        //public static void DrawStepColorBar(Canvas colorBarCanvas, List<ColorBaredGeometry> colorBaredGeometries, string title = "test", string unit = "unit")
        //{
        //    if (double.IsNaN(colorBarCanvas.Height) || double.IsNaN(colorBarCanvas.Width))
        //        return;

        //    // マウスイベントを無視するように設定
        //    colorBarCanvas.IsHitTestVisible = false;

        //    // ZIndexを設定して他のUI要素の背面に配置
        //    Panel.SetZIndex(colorBarCanvas, -1);

        //    // RenderTransformをリセット
        //    colorBarCanvas.RenderTransform = Transform.Identity;

        //    int numberOfColors = colorBaredGeometries.Count;
        //    if (numberOfColors == 0) { return; }

        //    double barHeight = 10; // colorBarCanvas.Height;
        //    double barWidth = 20; // colorBarCanvas.Width;

        //    double pos;

        //    colorBarCanvas.Children.Clear();
        //    int j = 0;
        //    foreach (ColorBaredGeometry colorbaredGeometry in colorBaredGeometries)
        //    {
        //        pos = (j + 0.5) / numberOfColors;
        //        SolidColorBrush brush = new(colorbaredGeometry.Color);

        //        Rectangle colorBar = new()
        //        {
        //            Width = barWidth,
        //            Height = barHeight,
        //            Fill = brush,
        //            Stroke = Brushes.Black, // 黒い枠を追加
        //            StrokeThickness = 0.5 // 枠の太さを設定
        //        };

        //        Canvas.SetLeft(colorBar, 0);
        //        Canvas.SetTop(colorBar, j * barHeight);
        //        colorBarCanvas.Children.Add(colorBar);
        //        j += 1;
        //    }

        //    string format;
        //    if (colorBaredGeometries.Count > 0)
        //    {
        //        format = GetFormat(colorBaredGeometries[0].TopRange - colorBaredGeometries[0].BottomRange);
        //    }
        //    else
        //    {
        //        format = "N4";
        //    }

        //    // カラーバー上に目盛りを追加

        //    int numberOfTicks = numberOfColors + 1;
        //    double value;
        //    for (int i = 0; i < numberOfTicks; i++)
        //    {
        //        if (i == 0)
        //        {
        //            value = colorBaredGeometries[i].BottomRange;
        //        }
        //        else
        //        {
        //            value = colorBaredGeometries[i - 1].TopRange;
        //        }

        //        Line tick = new()
        //        {
        //            X1 = 0, //barWidth,
        //            Y1 = i * barHeight,
        //            X2 = -5, //barWidth + 10,
        //            Y2 = i * barHeight,
        //            Stroke = Brushes.Black,
        //            StrokeThickness = 0.5
        //        };

        //        TextBlock label = new()
        //        {
        //            Text = value.ToString(format),
        //            Foreground = Brushes.Black,
        //            FontSize = 10,
        //        };

        //        // Measure the TextBlock to get its actual height
        //        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //        double labelHeight = label.DesiredSize.Height;
        //        double labelWidth = label.DesiredSize.Width;

        //        Canvas.SetLeft(label, -8 - labelWidth); // barWidth + 15);
        //        Canvas.SetTop(label, i * barHeight - labelHeight / 2);

        //        colorBarCanvas.Children.Add(tick);
        //        colorBarCanvas.Children.Add(label);
        //    }
        //}
        //public static void DrawStepColorBar(
        //    Canvas colorBarCanvas,
        //    List<ColorBaredGeometry> colorBaredGeometries,
        //    string title = "title",
        //    string unit = "mm",
        //    double? minValue = 0,
        //    double? maxValue = 999)
        //{
        //    if (double.IsNaN(colorBarCanvas.Height) || double.IsNaN(colorBarCanvas.Width))
        //        return;

        //    colorBarCanvas.IsHitTestVisible = false;
        //    Panel.SetZIndex(colorBarCanvas, -1);
        //    colorBarCanvas.RenderTransform = Transform.Identity;

        //    int numberOfColors = colorBaredGeometries.Count;
        //    if (numberOfColors == 0) { return; }

        //    double barHeight = 10;
        //    double barWidth = 20;

        //    colorBarCanvas.Children.Clear();

        //    // タイトル描画
        //    if (!string.IsNullOrEmpty(title))
        //    {
        //        TextBlock titleBlock = new()
        //        {
        //            Text = title,
        //            Foreground = Brushes.Black,
        //            FontSize = 12,
        //            FontWeight = FontWeights.Bold
        //        };
        //        titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //        Canvas.SetLeft(titleBlock, 0);
        //        Canvas.SetTop(titleBlock, 0);
        //        colorBarCanvas.Children.Add(titleBlock);
        //    }

        //    // 最大値・最小値の決定
        //    double minDisp = minValue ?? colorBaredGeometries[0].BottomRange;
        //    double maxDisp = maxValue ?? colorBaredGeometries[^1].TopRange;
        //    string format = colorBaredGeometries.Count > 0
        //        ? GetFormat(colorBaredGeometries[0].TopRange - colorBaredGeometries[0].BottomRange)
        //        : "N4";

        //    // 最小値表示
        //    TextBlock minBlock = new()
        //    {
        //        Text = $"Min: {minDisp.ToString(format)}",
        //        Foreground = Brushes.Black,
        //        FontSize = 11
        //    };
        //    minBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //    Canvas.SetLeft(minBlock, barWidth + 8);
        //    Canvas.SetTop(minBlock, 0);
        //    colorBarCanvas.Children.Add(minBlock);

        //    // カラーバー本体のYオフセット
        //    double yOffset = !string.IsNullOrEmpty(title) ? 18 : 0;

        //    int j = 0;
        //    foreach (ColorBaredGeometry colorbaredGeometry in colorBaredGeometries)
        //    {
        //        SolidColorBrush brush = new(colorbaredGeometry.Color);

        //        Rectangle colorBar = new()
        //        {
        //            Width = barWidth,
        //            Height = barHeight,
        //            Fill = brush,
        //            Stroke = Brushes.Black,
        //            StrokeThickness = 0.5
        //        };

        //        Canvas.SetLeft(colorBar, 0);
        //        Canvas.SetTop(colorBar, yOffset + j * barHeight);
        //        colorBarCanvas.Children.Add(colorBar);
        //        j += 1;
        //    }

        //    int numberOfTicks = numberOfColors + 1;
        //    double value;
        //    for (int i = 0; i < numberOfTicks; i++)
        //    {
        //        value = (i == 0)
        //            ? colorBaredGeometries[i].BottomRange
        //            : colorBaredGeometries[i - 1].TopRange;

        //        Line tick = new()
        //        {
        //            X1 = 0,
        //            Y1 = yOffset + i * barHeight,
        //            X2 = -5,
        //            Y2 = yOffset + i * barHeight,
        //            Stroke = Brushes.Black,
        //            StrokeThickness = 0.5
        //        };

        //        TextBlock label = new()
        //        {
        //            Text = value.ToString(format),
        //            Foreground = Brushes.Black,
        //            FontSize = 10,
        //        };

        //        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //        double labelHeight = label.DesiredSize.Height;
        //        double labelWidth = label.DesiredSize.Width;

        //        Canvas.SetLeft(label, -8 - labelWidth);
        //        Canvas.SetTop(label, yOffset + i * barHeight - labelHeight / 2);

        //        colorBarCanvas.Children.Add(tick);
        //        colorBarCanvas.Children.Add(label);
        //    }

        //    // 最大値表示
        //    TextBlock maxBlock = new()
        //    {
        //        Text = $"Max: {maxDisp.ToString(format)}",
        //        Foreground = Brushes.Black,
        //        FontSize = 11
        //    };
        //    maxBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //    Canvas.SetLeft(maxBlock, barWidth + 8);
        //    Canvas.SetTop(maxBlock, yOffset + numberOfColors * barHeight + 2);
        //    colorBarCanvas.Children.Add(maxBlock);

        //    // 単位描画
        //    if (!string.IsNullOrEmpty(unit))
        //    {
        //        TextBlock unitBlock = new()
        //        {
        //            Text = $"unit: {unit}",
        //            Foreground = Brushes.Black,
        //            FontSize = 11
        //        };
        //        unitBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //        Canvas.SetLeft(unitBlock, 0);
        //        Canvas.SetTop(unitBlock, yOffset + numberOfColors * barHeight + 2);
        //        colorBarCanvas.Children.Add(unitBlock);
        //    }

        //}
        public static void DrawStepColorBar(
            Canvas colorBarCanvas,
            List<ColorBaredGeometry> colorBaredGeometries,
            string title = "title",
            string unit = "mm",
            double? minValue = 100,
            double? maxValue = 1000,
            string formatVal = "N3")
        {
            if (double.IsNaN(colorBarCanvas.Height) || double.IsNaN(colorBarCanvas.Width))
                return;

            colorBarCanvas.IsHitTestVisible = false;
            //Panel.SetZIndex(colorBarCanvas, -1);
            colorBarCanvas.RenderTransform = Transform.Identity;

            int numberOfColors = colorBaredGeometries.Count;
            if (numberOfColors == 0) { return; }

            double barHeight = 10;
            double barWidth = 20;

            colorBarCanvas.Children.Clear();

            // 右端のX座標
            double rightX = colorBarCanvas.Width - 4;

            // --- 1. タイトル・単位（カラーバーの上） ---
            double y = 0;
            double titleHeight = 0;
            double titleWidth = 0;
            if (!string.IsNullOrEmpty(title))
            {
                TextBlock titleBlock = new()
                {
                    Text = title,
                    Foreground = Brushes.Black,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Right
                };
                titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                titleWidth = titleBlock.DesiredSize.Width;
                titleHeight = titleBlock.DesiredSize.Height;
                Canvas.SetLeft(titleBlock, barWidth - titleWidth);
                Canvas.SetTop(titleBlock, y);
                colorBarCanvas.Children.Add(titleBlock);
                y += titleHeight + 2;
            }

            double unitHeight = 0;
            if (!string.IsNullOrEmpty(unit))
            {
                TextBlock unitBlock = new()
                {
                    Text = unit,
                    Foreground = Brushes.Black,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Right
                };
                unitBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double unitWidth = unitBlock.DesiredSize.Width;
                unitHeight = unitBlock.DesiredSize.Height;
                Canvas.SetLeft(unitBlock, barWidth - unitWidth);
                Canvas.SetTop(unitBlock, y);
                colorBarCanvas.Children.Add(unitBlock);
                y += unitHeight + 4;
            }

            // --- 2. カラーバー本体 ---
            double yOffset = y;
            int j = 0;
            double maxLabelRight = 0;
            string format = colorBaredGeometries.Count > 0
                ? GetFormat(colorBaredGeometries[0].TopRange - colorBaredGeometries[0].BottomRange)
                : "N4";

            int numberOfTicks = numberOfColors + 1;
            double value;
            List<double> labelRights = [];
            for (int i = 0; i < numberOfTicks; i++)
            {
                value = (i == 0)
                    ? colorBaredGeometries[i].BottomRange
                    : colorBaredGeometries[i - 1].TopRange;

                Line tick = new()
                {
                    X1 = 0,
                    Y1 = yOffset + i * barHeight,
                    X2 = -5,
                    Y2 = yOffset + i * barHeight,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                };

                TextBlock label = new()
                {
                    Text = value.ToString(format),
                    Foreground = Brushes.Black,
                    FontSize = 10,
                };

                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelHeight = label.DesiredSize.Height;
                double labelWidth = label.DesiredSize.Width;

                double labelLeft = -8 - labelWidth;
                double labelRight = labelLeft + labelWidth;
                labelRights.Add(labelRight);
                if (labelRight > maxLabelRight) maxLabelRight = labelRight;

                Canvas.SetLeft(label, labelLeft);
                Canvas.SetTop(label, yOffset + i * barHeight - labelHeight / 2);

                colorBarCanvas.Children.Add(tick);
                colorBarCanvas.Children.Add(label);
            }

            // カラーバー本体
            j = 0;
            foreach (ColorBaredGeometry colorbaredGeometry in colorBaredGeometries)
            {
                SolidColorBrush brush = new(colorbaredGeometry.Color);

                Rectangle colorBar = new()
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = brush,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5
                };

                Canvas.SetLeft(colorBar, 0);
                Canvas.SetTop(colorBar, yOffset + j * barHeight);
                colorBarCanvas.Children.Add(colorBar);
                j += 1;
            }

            // --- 3. min/max（カラーバーの下、目盛ラベルの右端に右揃え） ---
            double minDisp = minValue ?? colorBaredGeometries[0].BottomRange;
            double maxDisp = maxValue ?? colorBaredGeometries[^1].TopRange;

            // FontSizeを目盛ラベルと同じにする
            double minMaxFontSize = 10;

            // min数値
            TextBlock minValueBlock = new()
            {
                Text = string.Format(formatVal, minDisp),
                //Text = minDisp.ToString(formatVal),
                Foreground = Brushes.Black,
                FontSize = minMaxFontSize,
                TextAlignment = TextAlignment.Right
            };
            minValueBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double minValueWidth = minValueBlock.DesiredSize.Width;
            double minValueHeight = minValueBlock.DesiredSize.Height;
            double minY = yOffset + numberOfColors * barHeight + 10;

            Canvas.SetLeft(minValueBlock, maxLabelRight - minValueWidth - 8); // 目盛ラベルの右端に右揃え
            Canvas.SetTop(minValueBlock, minY);
            colorBarCanvas.Children.Add(minValueBlock);

            // minラベル
            TextBlock minLabelBlock = new()
            {
                Text = "min",
                Foreground = Brushes.Black,
                FontSize = minMaxFontSize
            };
            minLabelBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double minLabelWidth = minLabelBlock.DesiredSize.Width;
            //Canvas.SetLeft(minLabelBlock, maxLabelRight + 4);
            Canvas.SetLeft(minLabelBlock, barWidth - minLabelWidth);
            Canvas.SetTop(minLabelBlock, minY);
            colorBarCanvas.Children.Add(minLabelBlock);

            // 目盛ラベルの左端（最小値ラベルの位置と同じ計算方法）
            double labelLeftMin = -8 - minValueBlock.DesiredSize.Width;

            // 水平線を描画
            Line separatorLine = new()
            {
                X1 = labelLeftMin,
                Y1 = minY - 1,
                X2 = barWidth,
                Y2 = minY - 1,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5
            };

            colorBarCanvas.Children.Add(separatorLine);

            // max数値
            TextBlock maxValueBlock = new()
            {
                Text = string.Format(formatVal, maxDisp),
                //Text = maxDisp.ToString(format),
                Foreground = Brushes.Black,
                FontSize = minMaxFontSize,
                TextAlignment = TextAlignment.Right
            };
            maxValueBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double maxValueWidth = maxValueBlock.DesiredSize.Width;
            double maxValueHeight = maxValueBlock.DesiredSize.Height;
            double maxY = minY + minValueHeight;
            Canvas.SetLeft(maxValueBlock, maxLabelRight - maxValueWidth - 8);
            Canvas.SetTop(maxValueBlock, maxY);
            colorBarCanvas.Children.Add(maxValueBlock);

            // maxラベル
            TextBlock maxLabelBlock = new()
            {
                Text = "max",
                Foreground = Brushes.Black,
                FontSize = minMaxFontSize
            };
            maxLabelBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double maxLabelWidth = maxLabelBlock.DesiredSize.Width;
            Canvas.SetLeft(maxLabelBlock, barWidth - maxLabelWidth);
            //Canvas.SetLeft(maxLabelBlock, maxLabelRight + 4);
            Canvas.SetTop(maxLabelBlock, maxY);
            colorBarCanvas.Children.Add(maxLabelBlock);

            // --- 背景の白い長方形を描画 ---
            //double backgroundWidth = Math.Max(barWidth, maxLabelRight - labelRights.Min());
            //double backgroundHeight = maxY + maxValueHeight - y + 10; // 全体の高さを計算
            double margin = 5;
            double top = 0 - margin;
            double bottom = maxY + minLabelBlock.DesiredSize.Height + margin;
            double backgroundHeight = -top + bottom;
            double left = Math.Min(Math.Min(Math.Min(barWidth - titleWidth, maxLabelRight - minValueWidth - 8), maxLabelRight - maxValueWidth - 8), barWidth - maxLabelWidth) - margin;
            double right = barWidth + margin;
            double backgroundWidth = right - left;
            Rectangle background = new()
            {
                Width = backgroundWidth,
                Height = backgroundHeight,
                Fill = new SolidColorBrush(Color.FromArgb(192, 255, 255, 255)) // 半透明の白 (128は透明度: 0=完全透明, 255=完全不透明)
            };
            Canvas.SetLeft(background, left);
            Canvas.SetTop(background, top);
            colorBarCanvas.Children.Insert(0, background); // 背景を最背面に追加

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
            else
            {
                return "N6";
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

