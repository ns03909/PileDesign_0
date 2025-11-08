
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Common
{
    public partial class ShapeDrawer
    {
        public static void DrawPileElevation(
            Canvas canvas,
            ObservableCollection<PileBodySegment> pileBodySegments,
            double pileToeDia,
            double insituPileToeHeight,
            double insituPileToeAngle,
            double precastConcretePileToeHeightRatio,
            string pileConstructionType,
            double pileTopAltitude = 0,
            GroundInput groundInput = null,
            bool isElementDivision = false,
            List<double> zs = null,
            double? selectedZ = null)

        {
            if (canvas == null || canvas.ActualWidth == 0 || canvas.ActualHeight == 0) return;

            canvas.Children.Clear();

            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;

            if (pileBodySegments.Count == 0) return;

            double pileLength = pileBodySegments[^1].SegmentDepth;
            double ratio = Math.Min(canvasHeight / (pileLength + 8.0), canvasWidth / (pileToeDia / 1000.0));
            double topMargin = 3 * ratio;

            if (groundInput != null)
            {
                DrawSoilLayers(canvas, groundInput, pileTopAltitude, ratio, topMargin);
            }

            foreach (var segment in pileBodySegments)
            {
                double segmentLength = segment.SegmentLength;
                double segmentDepth = segment.SegmentDepth;
                double pileDiameter = segment.PileSection?.PileDiameter / 1000.0 ?? 0.0;

                if (pileDiameter < 0.01) // 杭径が極小の場合
                {
                    DrawLine(canvas,
                        canvasWidth * 0.5, segmentDepth * ratio + topMargin,
                        canvasWidth * 0.5, (segmentDepth - segmentLength) * ratio + topMargin,
                        Brushes.SkyBlue, 0, 1);
                }
                else // 杭径が一般の場合
                {
                    var rectangle = new Rectangle
                    {
                        Width = pileDiameter * ratio,
                        Height = segmentLength * ratio,
                        Stroke = Brushes.SkyBlue,
                        Fill = Brushes.White,
                    };
                    Canvas.SetLeft(rectangle, canvasWidth * 0.5 - pileDiameter * 0.5 * ratio);
                    Canvas.SetTop(rectangle, (segmentDepth - segmentLength) * ratio + topMargin);
                    canvas.Children.Add(rectangle);
                }
            }

            var lastSegment = pileBodySegments[^1];
            double bottomSegmentDia = lastSegment.PileSection?.PileDiameter / 1000.0 ?? 0.0;
            double pileToeDiaInMeters = pileToeDia / 1000.0;

            if (pileConstructionType == "場所打ちコンクリート杭" && pileToeDiaInMeters > bottomSegmentDia)
            {
                double angle = 90 - insituPileToeAngle;
                double pileToeElevation = insituPileToeHeight / 1000;
                DrawConeToeShape(canvas, canvasWidth, /*canvasHeight,*/ pileLength, ratio, topMargin, bottomSegmentDia, pileToeDiaInMeters, pileToeElevation, angle);
            }
            else if ((pileConstructionType == "埋込み杭（プレボーリング）" || pileConstructionType == "埋込み杭（中掘り）") && pileToeDiaInMeters > bottomSegmentDia)
            {

                DrawCylinderToeShape(canvas, canvasWidth, /*canvasHeight,*/ pileLength, ratio, topMargin, bottomSegmentDia, pileToeDiaInMeters, precastConcretePileToeHeightRatio);
            }

            if (isElementDivision)
            {
                DrawElementDivision(canvas, zs, ratio, topMargin);
            }

            if (selectedZ != null)
            {
                DrawSelectedZ(canvas, zs[0], selectedZ, ratio, topMargin);
            }
        }

        // 拡大根固め杭の描画メソッド
        private static void DrawCylinderToeShape(
            Canvas canvas,
            double canvasWidth,
            //double canvasHeight,
            double pileLength,
            double ratio,
            double topMargin,
            double bottomSegmentDia,
            double pileToeDiaInMeters,
            double precastConcretePileToeHeightRatio)
        {
            // 拡大根固め球根の点群を生成
            double height = pileToeDiaInMeters * precastConcretePileToeHeightRatio;
            double width = pileToeDiaInMeters;
            var points = new PointCollection
            {
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + width * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
            };
            DrawPolygon(canvas, points, Brushes.SkyBlue, Brushes.White, 1);

            // ダッシュライン
            for (int i = -1; i < 2; i += 2)
            {
                DrawLine(canvas,
                    canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    (pileLength - pileToeDiaInMeters * precastConcretePileToeHeightRatio) * ratio + topMargin,
                    canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    pileLength * ratio + topMargin,
                    Brushes.SkyBlue, 2, 1);
            }
        }

        // 場所打ち拡底杭の描画メソッド
        private static void DrawConeToeShape(
            Canvas canvas,
            double canvasWidth,
            //double canvasHeight,
            double pileLength,
            double ratio,
            double topMargin,
            double bottomSegmentDia,
            double pileToeDiaInMeters,
            double pileToeElevation,
            double angle)
        {
            // 場所打ち拡底杭の点群を生成
            double height = (pileToeDiaInMeters - bottomSegmentDia) * 0.5 * Math.Tan(angle * Math.PI / 180);
            var points = new PointCollection
            {
                new Point(canvasWidth * 0.5 - bottomSegmentDia * 0.5 * ratio, (pileLength - pileToeElevation - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio, (pileLength - pileToeElevation) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio, (pileLength - pileToeElevation) * ratio + topMargin),
                new Point(canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio, (pileLength - pileToeElevation - height) * ratio + topMargin)
            };
            DrawPolygon(canvas, points, Brushes.SkyBlue, Brushes.White, 1);

            // 上辺の直線
            var line = new Line
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = 1,
                X1 = canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio,
                X2 = canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio,
                Y1 = (pileLength - pileToeElevation) * ratio + topMargin,
                Y2 = (pileLength - pileToeElevation) * ratio + topMargin
            };
            canvas.Children.Add(line);

            // ダッシュライン
            for (int i = -1; i < 2; i += 2)
            {
                var dashedLine = new Line
                {
                    Stroke = Brushes.SkyBlue,
                    StrokeThickness = 1,
                    StrokeDashArray = [2],
                    X1 = canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    X2 = canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    Y1 = (pileLength - pileToeElevation - (pileToeDiaInMeters - bottomSegmentDia) * 0.5 * Math.Tan(angle * Math.PI / 180)) * ratio + topMargin,
                    Y2 = pileLength * ratio + topMargin
                };
                canvas.Children.Add(dashedLine);
            }
        }

        // 土層描画メソッド
        private static void DrawSoilLayers(Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;
            double groundTopDepth = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;

            DrawLine(canvas, 0, groundTopDepth, canvasWidth, groundTopDepth, Brushes.Gray, 0, 2);

            // 土層の描画
            foreach (var layer in groundInput.GroundLayers)
            {
                double groundBottomDepth = (-layer.BottomAltitude + pileTopAltitude) * ratio + topMargin;
                if (0 <= groundBottomDepth && groundBottomDepth <= canvasHeight)
                {
                    DrawLine(canvas, 0, groundBottomDepth, canvasWidth, groundBottomDepth, Brushes.Gray, 0, 1);
                }
            }

            // 土層の描画
            for (int i = 0; i < groundInput.GroundLayers.Count; i++)
            {
                double yT = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
                if (i != 0) { yT = (-groundInput.GroundLayers[i - 1].BottomAltitude + pileTopAltitude) * ratio + topMargin; }
                double yB = (-groundInput.GroundLayers[i].BottomAltitude + pileTopAltitude) * ratio + topMargin;

                SolidColorBrush fill = new(Color.FromArgb(64, 0, 255, 255)); // 半透明のAqua色
                if (groundInput.GroundLayers[i].GranularityClass == "粘性土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 210, 180, 140)); // 半透明の薄い茶色
                }
                else if (groundInput.GroundLayers[i].GranularityClass == "砂質土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 255, 165, 0)); // 半透明の薄いオレンジ
                }
                else if (groundInput.GroundLayers[i].GranularityClass == "礫質土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 144, 238, 144)); // 半透明の薄い緑
                }

                var rectangle = new Rectangle
                {
                    Width = canvasWidth,
                    Height = yB - yT,
                    Fill = fill,
                    StrokeThickness = 0.5,
                    Stroke = Brushes.White
                };

                Canvas.SetLeft(rectangle, 0);
                Canvas.SetTop(rectangle, yT);
                canvas.Children.Add(rectangle);

                DrawLine(canvas, 0, yB, canvasWidth, yB, Brushes.Black); //描けない
            }

            // Zメモリ描画
            for (int j = 0; j < 1000; j++)
            {
                double altitude = Math.Ceiling(groundInput.GroundTopAltitude - j);
                double y = (-altitude + pileTopAltitude) * ratio + topMargin;
                if (y > canvasHeight) break;

                double dx = 10;
                if (y % 5 == 0) dx *= 2;

                DrawLine(canvas, canvasWidth - dx, y, canvasWidth, y, Brushes.LightGray, 0, 1);

                // 文字を追加
                var text = new TextBlock
                {
                    Text = altitude.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = text.DesiredSize.Width;

                Canvas.SetLeft(text, canvasWidth - textWidth); // 文字の位置を調整
                Canvas.SetTop(text, y); // 文字の位置を調整
                canvas.Children.Add(text);
            }
            var symbol = new TextBlock
            {
                Text = "Z",
                Foreground = Brushes.Black,
                FontSize = 10
            };
            symbol.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double symbolWidth = symbol.DesiredSize.Width;
            double symbolHeight = symbol.DesiredSize.Height;

            Canvas.SetLeft(symbol, canvasWidth - symbolWidth); // 文字の右端を調整
            Canvas.SetTop(symbol, groundTopDepth - symbolHeight); // 文字の下端を調整
            canvas.Children.Add(symbol);

            DrawNValues(canvas, groundInput, pileTopAltitude, ratio, topMargin);
        }


        // N値描画メソッド
        private static void DrawNValues(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;

            var points = new PointCollection();

            foreach (var massdata in groundInput.GroundMassesData)
            {
                double groundDepth = (-massdata.AltitudeDepth + pileTopAltitude) * ratio + topMargin;
                double groundNvalue = massdata.NValue / 4.0 / 60.0 * canvasWidth;
                points.Add(new Point(groundNvalue, groundDepth));
            }

            var polyline = new Polyline
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                Points = points,
            };

            canvas.Children.Add(polyline);

            double y1 = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
            double y2 = (-groundInput.GroundMassesData[^1].AltitudeDepth + pileTopAltitude) * ratio + topMargin;
            for (int i = 0; i <= 60; i += 10)
            {
                double x = i / 4.0 / 60.0 * canvasWidth;
                var line = new Line
                {
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    X1 = x,
                    X2 = x,
                    Y1 = y1,
                    Y2 = y2
                };
                canvas.Children.Add(line);

                // 文字を追加
                var text = new TextBlock
                {
                    Text = i.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textHeight = text.DesiredSize.Height;
                Canvas.SetLeft(text, x); // 文字の位置を調整
                Canvas.SetTop(text, y1 - textHeight); // 文字の位置を調整
                canvas.Children.Add(text);
            }
        }

        // ElementDivision描画メソッド
        private static void DrawElementDivision(
            Canvas canvas, List<double>? zs, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            //double canvasHeight = canvas.ActualHeight;

            foreach (var z in zs)
            {
                double dia = 5;
                var ellipse = new Ellipse
                {
                    Width = dia,
                    Height = dia,
                    Fill = Brushes.Red,
                };
                Canvas.SetLeft(ellipse, canvasWidth * 0.5 - dia * 0.5);
                Canvas.SetTop(ellipse, topMargin + (zs[0] - z) * ratio - dia * 0.5);
                canvas.Children.Add(ellipse);
            }
        }

        private static void DrawSelectedZ(
            Canvas canvas, double z0, double? selectedZ, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            //double canvasHeight = canvas.ActualHeight;

            double dia = 15;
            var ellipse = new Ellipse
            {
                Width = dia,
                Height = dia,
                Fill = null,
                Stroke = Brushes.Red,
                StrokeThickness = 1,

            };
            Canvas.SetLeft(ellipse, canvasWidth * 0.5 - dia * 0.5);
            Canvas.SetTop(ellipse, (topMargin + (z0 - (double)selectedZ) * ratio - dia * 0.5));
            canvas.Children.Add(ellipse);
        }

        private static void DrawPolygon(
            Canvas canvas, PointCollection points, Brush stroke, Brush fill, double thickness = 1)
        {
            var polygon = new Polygon
            {
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = thickness,
                Points = points
            };
            canvas.Children.Add(polygon);
        }

        private static void DrawLine(
            Canvas canvas, double x1, double y1, double x2, double y2,
            Brush stroke, int dashArray = 0, double thickness = 1)
        {
            var line = new Line
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = [dashArray],
                X1 = x1,
                X2 = x2,
                Y1 = y1,
                Y2 = y2
            };
            canvas.Children.Add(line);
        }
    }
}
