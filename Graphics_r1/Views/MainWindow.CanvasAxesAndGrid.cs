using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {

        // XYZ軸の更新メソッド
        private void UpdateAxes3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel?.FundamentalInput == null || viewModel.CanvasThreeDView == null) return;

            double length = 300;
            Point3D point3D0 = viewModel.CurrentInputModel.FundamentalInput.Point3D0;
            var axisPoints = new (Point3D start, Point3D end,
                Brush color, string name, PathGeometry pathGeometry)[]
            {
                (point3D0, new(point3D0.X + length, point3D0.Y, point3D0.Z),
                    Brushes.Red, "AxisX", viewModel.CanvasGeometry.PathGeoAxisX),
                (point3D0, new(point3D0.X, point3D0.Y + length,  point3D0.Z),
                    Brushes.Green, "AxisY", viewModel.CanvasGeometry.PathGeoAxisY),
                (point3D0, new(point3D0.X, point3D0.Y,  point3D0.Z + length),
                    Brushes.Blue, "AxisZ", viewModel.CanvasGeometry.PathGeoAxisZ),
                (point3D0, new(point3D0.X - length, point3D0.Y,  point3D0.Z),
                    Brushes.DarkRed, "AxisXN", viewModel.CanvasGeometry.PathGeoAxisXM),
                (point3D0, new(point3D0.X, point3D0.Y - length,  point3D0.Z),
                    Brushes.DarkGreen, "AxisYN", viewModel.CanvasGeometry.PathGeoAxisYM),
                (point3D0, new(point3D0.X, point3D0.Y,  point3D0.Z - length),
                    Brushes.DarkBlue, "AxisZN", viewModel.CanvasGeometry.PathGeoAxisZM)
            };

            // 軸ごとにPathGeometryへ追加
            foreach (var (start3D, end3D, color, name, pathGeometry) in axisPoints)
            {
                Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                Point end = viewModel.CanvasThreeDView.Transformation(end3D);

                // ここでPathGeoAxesに追加
                AddAxisLine3D(start, end, color, name, pathGeometry);
            }
        }

        // XYZ軸の追加メソッド
        private static void AddAxisLine3D(Point start, Point end, Brush color, string name, PathGeometry pathGeometry)
        {
            // 無効な値が含まれている場合は、Lineを作成しない
            if (double.IsNaN(start.X) || double.IsNaN(start.Y) || double.IsNaN(end.X) || double.IsNaN(end.Y) ||
                double.IsInfinity(start.X) || double.IsInfinity(start.Y) || double.IsInfinity(end.X) || double.IsInfinity(end.Y))
            { return; }

            var lineGeometry = new LineGeometry
            {
                StartPoint = start,
                EndPoint = end
            };
            pathGeometry.AddGeometry(lineGeometry);
        }


        // 共通メソッド
        private void DrawTickMarks(
            int minIndex, int maxIndex, double tickSpacing,
            Func<int, double> getValue,
            Func<double, Point3D> get3DLocation,
            Func<Point3D, Point> to2D,
            Action<Point, string> drawTick)
        {
            for (int i = minIndex; i <= maxIndex; i++)
            {
                double value = getValue(i);
                Point3D loc = get3DLocation(value);
                Point coord = to2D(loc);
                if (0 < coord.X && coord.X < Canvas3DWidth && 0 < coord.Y && coord.Y < Canvas3DHeight)
                {
                    drawTick(coord, $"{value}m");
                }
            }
        }

        private void UpdateTickMarks3DElevation()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                z => new Point3D(0, 0, z),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkY(coord.Y, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DYofYZ()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            // 追加: 安全なアクセスを保証するためのチェック
            if (viewModel.CurrentInputModel?.FundamentalInput == null || viewModel.CanvasThreeDView == null) return;

            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                y => new Point3D(0, y, viewModel.CurrentInputModel.FundamentalInput.Z0),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkX(coord.X, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DXofXZ()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel?.FundamentalInput == null || viewModel.CanvasThreeDView == null) return;
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            DrawTickMarks(
                -1000 / (int)tickSpacing, 1000 / (int)tickSpacing, tickSpacing,
                i => i * tickSpacing,
                x => new Point3D(x, 0, viewModel.CurrentInputModel.FundamentalInput.Z0),
                loc => viewModel.CanvasThreeDView.Transformation(loc),
                (coord, text) => AddTickMarkX(coord.X, tickLength, textPos, solidColorBrush, text)
            );
        }

        private void UpdateTickMarks3DPlan()
        {
            double tickLength = 35;
            double textPos = tickLength - 5;
            SolidColorBrush solidColorBrush = Brushes.Gray;

            for (int i = -1000 / (int)tickSpacing; i <= 1000 / (int)tickSpacing; i++)
            {
                double tickValue = i * tickSpacing;
                DrawTickMarksForValue(tickValue, tickLength, textPos, solidColorBrush);
            }
        }


        private void AddTickMarkY(double y, double tickLength, double textPos, SolidColorBrush brush, string text)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            LineGeometry lineGeometry = new()
            {
                StartPoint = new(Canvas3DWidth - tickLength, y),
                EndPoint = new(Canvas3DWidth, y)
            };

            viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);
            AddText3D(brush, text, Canvas3DWidth, y, "R", "B", 0.0);
        }

        // チェックマーク描画メソッド
        private void AddTickMarkX(double x, double tickLength, double textPos, SolidColorBrush brush, string text)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            LineGeometry lineGeometry = new()
            {
                StartPoint = new(x, Canvas3DHeight - tickLength),
                EndPoint = new(x, Canvas3DHeight)
            };

            viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);
            AddText3D(brush, text, x, Canvas3DHeight - textPos, "R", "B", -90);
        }

        // 指定された値のTickMarkを描画するメソッド
        private void DrawTickMarksForValue(
            double tickValue, double tickLength, double textPos, SolidColorBrush solidColorBrush
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel.CurrentInputModel?.FundamentalInput == null || viewModel.CanvasThreeDView == null) return;

            Point3D[] locs =
            [
                new Point3D(tickValue, 0, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(tickValue, 10, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(0, tickValue, viewModel.CurrentInputModel.FundamentalInput.Z0),
                new Point3D(10, tickValue, viewModel.CurrentInputModel.FundamentalInput.Z0)
            ];

            Point[] coords = [.. locs.Select(loc => viewModel.CanvasThreeDView.Transformation(loc))];

            AddTickMark3DPlan(coords[0], coords[1], tickLength, textPos, solidColorBrush, tickValue, true);
            AddTickMark3DPlan(coords[2], coords[3], tickLength, textPos, solidColorBrush, tickValue, false);
        }

        // TickMarkを追加するメソッド
        private void AddTickMark3DPlan(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (coord1.Y != coord2.Y)
            {
                AddVerticalTickMark(coord1, coord2, tickLength, textPos, brush, tickValue, isXAxis);
            }

            if (coord1.X != coord2.X)
            {
                AddHorizontalTickMark(coord1, coord2, tickLength, textPos, brush, tickValue, isXAxis);
            }
        }

        // 垂直TickMarkを追加するメソッド
        private void AddVerticalTickMark(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            double xx1 = CalculateTickPosition(coord1, coord2, Canvas3DHeight - tickLength, 'Y');
            double xx2 = CalculateTickPosition(coord1, coord2, Canvas3DHeight, 'Y');

            if (IsInCanvasWidth(xx1))
            {
                LineGeometry lineGeometry = new()
                {
                    StartPoint = new(xx1, Canvas3DHeight - tickLength),
                    EndPoint = new(xx2, Canvas3DHeight)
                };
                viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);

                AddText3D(brush, $"{tickValue:F1}", xx2, Canvas3DHeight - textPos, "R", "B", isXAxis ? -90.0 : 0.0);
            }
        }

        // 水平TickMarkを追加するメソッド
        private void AddHorizontalTickMark(
            Point coord1, Point coord2, double tickLength, double textPos, SolidColorBrush brush, double tickValue, bool isXAxis
            )
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            double yy1 = CalculateTickPosition(coord1, coord2, Canvas3DWidth - tickLength, 'X');
            //double yy2 = CalculateTickPosition(coord1, coord2, Canvas3DWidth, 'X');
            double yy2 = CalculateTickPosition(coord1, coord2, Canvas3DWidth - 2, 'X');
            if (IsInCanvasHeight(yy1))
            {
                LineGeometry lineGeometry = new()
                {
                    StartPoint = new(Canvas3DWidth - tickLength, yy1),
                    //EndPoint = new(Canvas3DWidth, yy2)
                    EndPoint = new(Canvas3DWidth - 2, yy2)
                };
                viewModel.CanvasGeometry.PathGeoTicks.AddGeometry(lineGeometry);

                //AddText3D(brush, $"{tickValue:F1}", Canvas3DWidth, yy2, "R", "B", isXAxis ? -90.0 : 0.0);
                AddText3D(brush, $"{tickValue:F1}", Canvas3DWidth - 2, yy2, "R", "B", isXAxis ? -90.0 : 0.0);
            }
        }

        // TickMarkの位置を計算するメソッド
        private static double CalculateTickPosition(Point coord1, Point coord2, double targetValue, char axis)
        {
            double delta;
            if (axis == 'Y')
            {
                delta = coord2.Y - coord1.Y;
                return (targetValue - coord1.Y) * (coord2.X - coord1.X) / delta + coord1.X;
            }
            else // ;
            {
                delta = coord2.X - coord1.X;
                return (targetValue - coord1.X) * (coord2.Y - coord1.Y) / delta + coord1.Y;
            }
        }

        // TickMarkがキャンバスの幅に収まっているかチェックするメソッド
        private bool IsInCanvasWidth(double x)
        {
            return 0 < x && x < Canvas3DWidth;
        }

        // TickMarkがキャンバスの高さに収まっているかチェックするメソッド
        private bool IsInCanvasHeight(double y)
        {
            return 0 < y && y < Canvas3DHeight;
        }

        private void UpdateGridLinesAndDimensionsXforXZ()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            //CurrentInputModel inputModel = CurrentInputModel.Instance;
            if (viewModel.CurrentInputModel == null || viewModel.CanvasThreeDView == null) return;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                {
                    if (0 < coord.X && coord.X < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, 0),
                            EndPoint = new(coord.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridX.Name, coord.X, Canvas3DHeight - SymbolPos, "C", "C", 0.0);

                    }
                }
            }

            if (DataContext == null) { return; }

            bool first = true;

            if (viewModel.CurrentInputModel.GridXItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridXItems.Count; i++)
                {
                    GridDataItem gridX = viewModel.CurrentInputModel.GridXItems[i];
                    Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), actualTickPointSize * 0.5, actualTickPointSize * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridX0 = viewModel.CurrentInputModel.GridXItems[i - 1];
                        Point3D loc0 = new(gridX0.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridXItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }
        }

        private void UpdateGridLinesAndDimensionsYforYZ()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel == null) return;
            if (viewModel.CurrentInputModel?.FundamentalInput == null || viewModel.CanvasThreeDView == null) return;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                {
                    if (0 < coord.X && coord.X < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, 0),
                            EndPoint = new(coord.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridY.Name, coord.X, Canvas3DHeight - SymbolPos, "C", "C", 0.0);
                    }
                }
            }

            if (DataContext == null) { return; }

            bool first = true;

            if (viewModel.CurrentInputModel.GridYItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridYItems.Count; i++)
                {
                    GridDataItem gridY = viewModel.CurrentInputModel.GridYItems[i];
                    Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridY0 = viewModel.CurrentInputModel.GridYItems[i - 1];
                        Point3D loc0 = new(0, gridY0.Coord, 0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridYItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }
        }


        private void UpdateGridLines3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel == null) return;
            if (viewModel.CanvasThreeDView == null) return;

            var gridXItems = viewModel.CurrentInputModel.GridXItems;
            var gridYItems = viewModel.CurrentInputModel.GridYItems;
            if (gridXItems == null || gridYItems == null) return;
            if (gridXItems.Count == 0 && gridYItems.Count == 0) return;

            SolidColorBrush solidColorBrush = Brushes.Purple;

            double lineEndPosTickSide = viewModel.GridSymbolZoneWidth;
            double lineEndPos = viewModel.GridSymbolZoneWidth;
            double gridSymbolPosTickSide = viewModel.GridSymbolZoneWidth * 0.5;
            double gridSymbolPos = viewModel.GridSymbolZoneWidth * 0.5;
            double gridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            if (viewModel.CanvasThreeDView.Phi == 90 && viewModel.IsTickMarkVisible)
            {
                lineEndPosTickSide += viewModel.TickZoneWidth;
                gridSymbolPosTickSide += viewModel.TickZoneWidth;
            }

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc1 = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(gridX.Coord, 10, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                DrawGridLineAndSymbol(
                    coord1, coord2,
                    lineEndPos, lineEndPosTickSide,
                    gridSymbolPos, gridSymbolPosTickSide,
                    gridSymbolCircleDia, solidColorBrush, gridX.Name, flattening);
            }

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc1 = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(10, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                DrawGridLineAndSymbol(
                    coord1, coord2,
                    lineEndPos, lineEndPosTickSide,
                    gridSymbolPos, gridSymbolPosTickSide,
                    gridSymbolCircleDia, solidColorBrush, gridY.Name, flattening);
            }
        }

        private void DrawGridLineAndSymbol(
            Point coord1, Point coord2,
            double lineEndPos, double lineEndPosTickSide,
            double gridSymbolPos, double gridSymbolPosTickSide,
            double gridSymbolCircleDia, SolidColorBrush solidColorBrush,
            string name, double flattening)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 長方形の境界
            double minX = lineEndPos;
            double maxX = Canvas3DWidth - lineEndPosTickSide;
            double minY = lineEndPos;
            double maxY = Canvas3DHeight - lineEndPosTickSide;

            // 長方形の境界
            double minXGS = gridSymbolPos;
            double maxXGS = Canvas3DWidth - gridSymbolPosTickSide;
            double minYGS = gridSymbolPos;
            double maxYGS = Canvas3DHeight - gridSymbolPosTickSide;

            // 線分の端点が長方形の外にある場合、クリッピングする
            List<Point> intersections = [];
            List<Point> intersectionsGS = [];


            // 左右辺
            if (!coord1.X.Equals(coord2.X))
            {
                double yL = (minX - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minY <= yL && yL <= maxY) intersections.Add(new Point(minX, yL));

                double yR = (maxX - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minY <= yR && yR <= maxY) intersections.Add(new Point(maxX, yR));

                double yLGS = (minXGS - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minYGS <= yLGS && yLGS <= maxYGS) intersectionsGS.Add(new Point(minXGS, yLGS));

                double yRGS = (maxXGS - coord1.X) * (coord2.Y - coord1.Y) / (coord2.X - coord1.X) + coord1.Y;
                if (minYGS <= yRGS && yRGS <= maxYGS) intersectionsGS.Add(new Point(maxXGS, yRGS));
            }

            // 上下辺
            if (!coord1.Y.Equals(coord2.Y))
            {
                double xT = (minY - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minX <= xT && xT <= maxX) intersections.Add(new Point(xT, minY));

                double xB = (maxY - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minX <= xB && xB <= maxX) intersections.Add(new Point(xB, maxY));

                double xTGS = (minYGS - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minXGS <= xTGS && xTGS <= maxXGS) intersectionsGS.Add(new Point(xTGS, minYGS));

                double xBGS = (maxYGS - coord1.Y) * (coord2.X - coord1.X) / (coord2.Y - coord1.Y) + coord1.X;
                if (minXGS <= xBGS && xBGS <= maxXGS) intersectionsGS.Add(new Point(xBGS, maxYGS));
            }

            // 必要条件を満たさない場合は描画しない（落ちないように）
            if (intersections.Count != 2 || intersectionsGS.Count != 2) return;

            var lineGeometry = new LineGeometry
            {
                StartPoint = intersections[0],
                EndPoint = intersections[1]
            };
            viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

            var ellipse1 = new EllipseGeometry(intersectionsGS[0], gridSymbolCircleDia * 0.5, gridSymbolCircleDia * 0.5 * flattening);
            viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse1);

            var ellipse2 = new EllipseGeometry(intersectionsGS[1], gridSymbolCircleDia * 0.5, gridSymbolCircleDia * 0.5 * flattening);
            viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse2);

            AddText3D(solidColorBrush, name ?? string.Empty, intersectionsGS[0].X, intersectionsGS[0].Y, "C", "C", 0.0, flattening);
            AddText3D(solidColorBrush, name ?? string.Empty, intersectionsGS[1].X, intersectionsGS[1].Y, "C", "C", 0.0, flattening);
        }


        // 通り心描画メソッド
        private void UpdateGridLines3DPlan()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel.CurrentInputModel == null || viewModel.CanvasThreeDView == null) return;
            SolidColorBrush solidColorBrush = Brushes.Purple;

            double SymbolPos = 50;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;
            double GridSymbolCircleDia = viewModel.GridSymbolCircleDia * viewModel.LabelSize / 10.0;
            double flattening = viewModel.CanvasThreeDView.Flattening;

            foreach (GridDataItem gridX in viewModel.CurrentInputModel.GridXItems)
            {
                Point3D loc1 = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(gridX.Coord, 10, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                // canvas x軸との交点
                if (coord1.Y != coord2.Y)
                {
                    double xx1 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (0 - coord1.Y) + coord1.X;
                    double xx2 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - LineEndPos - coord1.Y) + coord1.X;
                    double xxSymbol = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - SymbolPos - coord1.Y) + coord1.X;
                    if (0 < xxSymbol && xxSymbol < Canvas3DWidth)
                    //if (0 < xx1 && xx1 < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(xx1, 0),
                            EndPoint = new(xx2, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(xxSymbol, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                        AddText3D(solidColorBrush, gridX.Name, xxSymbol, Canvas3DHeight - SymbolPos, "C", "C", 0.0, flattening);
                    }
                }

                // canvas y軸との交点
                if (coord1.X != coord2.X)
                {
                    double yy1 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (0 - coord1.X) + coord1.Y;
                    double yy2 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - LineEndPos - coord1.X) + coord1.Y;
                    double yySymbol = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - SymbolPos - coord1.X) + coord1.Y;
                    if (0 < yySymbol && yySymbol < Canvas3DHeight)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(0, yy1),
                            EndPoint = new(Canvas3DWidth - LineEndPos, yy2)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(Canvas3DWidth - SymbolPos, yySymbol), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridX.Name, Canvas3DWidth - SymbolPos, yySymbol, "C", "C", 0.0, flattening);
                    }
                }
            }

            foreach (GridDataItem gridY in viewModel.CurrentInputModel.GridYItems)
            {
                Point3D loc1 = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);
                Point3D loc2 = new(10, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                Point coord2 = viewModel.CanvasThreeDView.Transformation(loc2);

                // canvas x軸との交点
                if (coord1.Y != coord2.Y)
                {
                    double xx1 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (0 - coord1.Y) + coord1.X;
                    double xx2 = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - LineEndPos - coord1.Y) + coord1.X;
                    double xxSymbol = (coord2.X - coord1.X) / (coord2.Y - coord1.Y) * (Canvas3DHeight - SymbolPos - coord1.Y) + coord1.X;

                    if (0 < xxSymbol && xxSymbol < Canvas3DWidth)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(xx1, 0),
                            EndPoint = new(xx2, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(xxSymbol, Canvas3DHeight - SymbolPos), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridY.Name, xxSymbol, Canvas3DHeight - SymbolPos, "C", "C", 0.0, flattening);
                    }
                }

                // canvas y軸との交点
                if (coord1.X != coord2.X)
                {
                    double yy1 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (0 - coord1.X) + coord1.Y;
                    double yy2 = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - LineEndPos - coord1.X) + coord1.Y;
                    double yySymbol = (coord2.Y - coord1.Y) / (coord2.X - coord1.X) * (Canvas3DWidth - SymbolPos - coord1.X) + coord1.Y;

                    if (0 < yySymbol && yySymbol < Canvas3DHeight)
                    {
                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(0, yy1),
                            EndPoint = new(Canvas3DWidth - LineEndPos, yy2)
                        };
                        viewModel.CanvasGeometry.PathGeoGridLines.AddGeometry(lineGeometry);

                        EllipseGeometry ellipse = new(new Point(Canvas3DWidth - SymbolPos, yySymbol), GridSymbolCircleDia * 0.5, GridSymbolCircleDia * 0.5 * flattening);
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);
                        AddText3D(solidColorBrush, gridY.Name, Canvas3DWidth - SymbolPos, yySymbol, "C", "C", 0.0, flattening);
                    }
                }
            }
        }

        // 寸法線描画メソッド
        private void UpdateDimensionLines3DPlan()
        {
            if (DataContext == null) { return; }
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel.CurrentInputModel == null || viewModel.CanvasThreeDView == null) return;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double LineEndPos = 50 + 15 * viewModel.LabelSize / 10.0;

            bool first = true;

            if (viewModel.CurrentInputModel.GridXItems != null)
            {
                for (int i = 0; i < viewModel.CurrentInputModel.GridXItems.Count; i++)
                {
                    GridDataItem gridX = viewModel.CurrentInputModel.GridXItems[i];

                    Point3D loc = new(gridX.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(coord.X, Canvas3DHeight - LineEndPos), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridX0 = viewModel.CurrentInputModel.GridXItems[i - 1];
                        Point3D loc0 = new(gridX0.Coord, 0, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(coord.X, Canvas3DHeight - LineEndPos),
                            EndPoint = new(coord0.X, Canvas3DHeight - LineEndPos)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasXpos = (coord.X + coord0.X) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridXItems[i].Spacing * 1000).ToString();
                        AddText3D(solidColorBrush, spacing, canvasXpos, Canvas3DHeight - LineEndPos, "C", "B", 0.0);
                    }
                }
            }


            if (viewModel.CurrentInputModel.GridYItems != null)
            {
                first = true;
                for (int i = 0; i < viewModel.CurrentInputModel.GridYItems.Count; i++)
                {
                    GridDataItem gridY = viewModel.CurrentInputModel.GridYItems[i];

                    Point3D loc = new(0, gridY.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                    Point coord = viewModel.CanvasThreeDView.Transformation(loc);
                    EllipseGeometry ellipse = new(new Point(Canvas3DWidth - LineEndPos, coord.Y), 2 * 0.5, 2 * 0.5);
                    viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(ellipse);

                    if (first)
                    {
                        first = false;
                        continue; // 最初のループをスキップ
                    }
                    else
                    {
                        GridDataItem gridY0 = viewModel.CurrentInputModel.GridYItems[i - 1];
                        Point3D loc0 = new(0, gridY0.Coord, viewModel.CurrentInputModel.FundamentalInput.Z0);
                        Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);

                        LineGeometry lineGeometry = new()
                        {
                            StartPoint = new(Canvas3DWidth - LineEndPos, coord.Y),
                            EndPoint = new(Canvas3DWidth - LineEndPos, coord0.Y)
                        };
                        viewModel.CanvasGeometry.PathGeoSoildGridLines.AddGeometry(lineGeometry);

                        double canvasYpos = (coord.Y + coord0.Y) * 0.5;
                        string spacing = (viewModel.CurrentInputModel.GridYItems[i].Spacing * 1000).ToString();

                        AddText3D(solidColorBrush, spacing, Canvas3DWidth - LineEndPos, canvasYpos, "C", "B", -90);
                    }
                }
            }
        }
    }
}
