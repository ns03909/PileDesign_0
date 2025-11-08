using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Services
{
    public static class CanvasDrawingService
    {
        public static void DrawLoadCombination(Canvas canvas)
        {
            double offsetX = 0;
            double offsetY = (canvas.ActualHeight - Math.Min(canvas.ActualHeight, canvas.ActualWidth)) * 0.5;

            double scale = Math.Min(canvas.ActualHeight, canvas.ActualWidth) / 200;
            double canvasWidth = canvas.ActualWidth;

            // ÉTÉCÉYíËêî
            double ellipseW = 50 * scale, ellipseH = 50 * scale;
            double springW = 5 * scale, springH = 20 * scale;
            double baseW = 70 * scale, baseH = 30 * scale;
            double pileW = 9 * scale, pileH = 100 * scale;
            double pileSpacing = 45 * scale;
            double buildingCenter = 0.7 * canvasWidth;

            // â~ÇÃï`âÊ
            Ellipse ellipse = new()
            {
                Width = ellipseW,
                Height = ellipseH,
                Fill = Brushes.SkyBlue
            };
            Canvas.SetLeft(ellipse, buildingCenter - ellipse.Width * 0.5 + offsetX);
            Canvas.SetTop(ellipse, 0 + offsetY);
            canvas.Children.Add(ellipse);

            // ÇŒÇÀí∑ï˚å`ÇÃï`âÊ
            Rectangle rectangle1 = new()
            {
                Width = springW,
                Height = springH,
                Fill = Brushes.SkyBlue
            };
            Canvas.SetLeft(rectangle1, buildingCenter - rectangle1.Width * 0.5 + offsetX);
            Canvas.SetTop(rectangle1, ellipse.Height + offsetY);
            canvas.Children.Add(rectangle1);

            // äÓëbí∑ï˚å`ÇÃï`âÊ
            Rectangle rectangle2 = new()
            {
                Width = baseW,
                Height = baseH,
                Fill = Brushes.SkyBlue
            };
            Canvas.SetLeft(rectangle2, buildingCenter - rectangle2.Width * 0.5 + offsetX);
            Canvas.SetTop(rectangle2, rectangle1.Height + ellipse.Height + offsetY);
            canvas.Children.Add(rectangle2);

            // çYí∑ï˚å`ÇÃï`âÊ
            double spacing = pileSpacing;
            Rectangle rectangle3 = new()
            {
                Width = pileW,
                Height = pileH,
                Fill = Brushes.SkyBlue
            };
            Canvas.SetLeft(rectangle3, buildingCenter - spacing * 0.5 - rectangle3.Width * 0.5 + offsetX);
            Canvas.SetTop(rectangle3, rectangle1.Height + ellipse.Height + rectangle2.Height + offsetY);
            canvas.Children.Add(rectangle3);

            Rectangle rectangle4 = new()
            {
                Width = pileW,
                Height = pileH,
                Fill = Brushes.SkyBlue
            };
            Canvas.SetLeft(rectangle4, buildingCenter + spacing * 0.5 - rectangle4.Width * 0.5 + offsetX);
            Canvas.SetTop(rectangle4, rectangle1.Height + ellipse.Height + rectangle2.Height + offsetY);
            canvas.Children.Add(rectangle4);

            // ï˙ï®ê¸ÇÃï`âÊ
            PathGeometry parabolaGeometry = new();
            PathFigure pathFigure = new()
            {
                StartPoint = new Point((buildingCenter) - rectangle2.Width * 0.5 - rectangle2.Width * 0.5 + offsetX, rectangle1.Height + ellipse.Height + offsetY)
            };
            QuadraticBezierSegment quadraticBezierSegment = new()
            {
                Point1 = new Point(buildingCenter - rectangle2.Width * 0.5 - rectangle2.Width * 0.5 + offsetX, rectangle1.Height + ellipse.Height + rectangle4.Height * 0.5 + offsetY),
                Point2 = new Point(buildingCenter - rectangle2.Width - rectangle2.Width * 0.5 + offsetX, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height + offsetY)
            };
            pathFigure.Segments.Add(quadraticBezierSegment);
            parabolaGeometry.Figures.Add(pathFigure);

            Path path = new()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = 1,
                Data = parabolaGeometry
            };
            canvas.Children.Add(path);

            // Lineóvëf
            Point point2 = new(buildingCenter - rectangle2.Width - rectangle2.Width * 0.5 - rectangle2.Width / 4.0 + offsetX, rectangle1.Height + ellipse.Height + offsetY);
            Point point3 = new(buildingCenter - rectangle2.Width - rectangle2.Width * 0.5 - rectangle2.Width / 4.0 + offsetX, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height + offsetY);
            DrawLine(canvas, pathFigure.StartPoint, point2);
            DrawLine(canvas, point2, point3);
            DrawLine(canvas, point3, quadraticBezierSegment.Point2);

            double x = buildingCenter - rectangle2.Width - rectangle2.Width * 0.5 + offsetX;
            double x1 = buildingCenter - rectangle2.Width * 0.25 + offsetX;
            double x2 = buildingCenter + rectangle2.Width * 0.25 + offsetX;

            DrawText(canvas, "É¿UÅEPs", new Point(x1, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + 10 + offsetY), true);
            DrawText(canvas, "É¿LÅEPf", new Point(x2, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + 10 + offsetY), true);
            DrawText(canvas, "ÉøLÅED", new Point(x, rectangle1.Height + ellipse.Height + rectangle2.Height * 0.5 + rectangle4.Height * 0.5 + offsetY), true);

            DrawArrow(canvas, new Point(x1 - 5, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + offsetY),
                            new Point(x1 + 5, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + offsetY), 5);
            DrawArrow(canvas, new Point(x2 - 5, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + offsetY),
                            new Point(x2 + 5, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height + offsetY), 5);
        }

        private static void DrawLine(Canvas canvas, Point startPoint, Point endPoint)
        {
            Line line = new()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = 1,
                X1 = startPoint.X,
                Y1 = startPoint.Y,
                X2 = endPoint.X,
                Y2 = endPoint.Y
            };
            canvas.Children.Add(line);
        }

        private static void DrawArrow(Canvas canvas, Point startPoint, Point endPoint, double arrowSize)
        {
            double deltaX = endPoint.X - startPoint.X;
            double deltaY = endPoint.Y - startPoint.Y;
            double angle = Math.Atan2(deltaY, deltaX);

            Point arrowTip = new(endPoint.X - arrowSize * Math.Cos(angle), endPoint.Y - arrowSize * Math.Sin(angle));
            Point arrowLeft = new(arrowTip.X + arrowSize * Math.Cos(angle + Math.PI * 0.5), arrowTip.Y + arrowSize * Math.Sin(angle + Math.PI * 0.5));
            Point arrowRight = new(arrowTip.X + arrowSize * Math.Cos(angle - Math.PI * 0.5), arrowTip.Y + arrowSize * Math.Sin(angle - Math.PI * 0.5));

            PathGeometry arrowGeometry = new();
            PathFigure pathFigure = new() { StartPoint = endPoint };
            pathFigure.Segments.Add(new LineSegment(arrowLeft, true));
            pathFigure.Segments.Add(new LineSegment(arrowRight, true));
            pathFigure.Segments.Add(new LineSegment(endPoint, true));
            arrowGeometry.Figures.Add(pathFigure);

            Path path = new()
            {
                Stroke = Brushes.BlueViolet,
                Fill = Brushes.BlueViolet,
                StrokeThickness = 1,
                Data = arrowGeometry
            };
            canvas.Children.Add(path);
        }

        private static void DrawText(Canvas canvas, string text, Point position, bool isSubscript = false)
        {
            double fontSize = 12;
            Brush textColor = Brushes.Black;

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                textColor,
                VisualTreeHelper.GetDpi(canvas).PixelsPerDip
            );

            double textWidth = formattedText.Width;
            double textHeight = formattedText.Height;
            double x = position.X - textWidth / 2;
            double y = position.Y - textHeight / 2;

            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = fontSize,
                Foreground = textColor
            };

            if (isSubscript)
            {
                textBlock.Text = "";
                foreach (char c in text)
                {
                    if (c == 'U' || c == 'L' || c == 's' || c == 'f')
                    {
                        Run subscriptRun = new()
                        {
                            Text = c.ToString(),
                            FontSize = fontSize * 0.8,
                            BaselineAlignment = BaselineAlignment.Subscript
                        };
                        textBlock.Inlines.Add(subscriptRun);
                    }
                    else
                    {
                        Run normalRun = new()
                        {
                            Text = c.ToString(),
                            FontSize = fontSize
                        };
                        textBlock.Inlines.Add(normalRun);
                    }
                }
            }

            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);
            canvas.Children.Add(textBlock);
        }
    }
}