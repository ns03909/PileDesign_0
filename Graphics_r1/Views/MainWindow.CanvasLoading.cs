using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {
        // 3D荷重更新メソッド
        public void UpdateLoading3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel == null) return;
            ObservableCollection<Point3D> points = [];
            ObservableCollection<Vector3D> valueVectors = [];
            ObservableCollection<double> values = [];

            var selectedLoadCase = LoadCases.GetLoadCase(
            viewModel.CurrentInputModel.LoadCasesInput.AllLoadCases, viewModel.SelectedLoadCaseName);
            if (selectedLoadCase == null) return;

            var selectedLoadCombination = LoadCombinations.GetLoadCombination(
            viewModel.CurrentInputModel.LoadCasesInput.LoadCombinations, viewModel.SelectedLoadCombinationName);
            if (selectedLoadCombination == null) return;


            // 杭頭
            if (viewModel.IsAxialLoadingVisible)
            {
                foreach (PileLayoutDataItem pileLocation in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    if (!pileLocation.IsVisible) continue;

                    double forceZ = GetForceZ(viewModel, pileLocation, selectedLoadCase); // 既存ヘルパー
                    Point3D locPileTop = pileLocation.Point3D;

                    points.Add(new Point3D(locPileTop.X, locPileTop.Y, locPileTop.Z));
                    valueVectors.Add(new Vector3D(0, 0, forceZ)); // GetForceZ は符号付きを返している
                    values.Add(Math.Abs(forceZ));
                }
            }

            // 慣性力作用点
            if (viewModel.IsActionPointVisible && viewModel.IsMassLoadingVisible)
            {
                double x = selectedLoadCase.ForceActionPointX;
                double y = selectedLoadCase.ForceActionPointY;
                double z = selectedLoadCase.ForceActionPointAltitude;
                points.Add(new(x, y, z));

                double force = selectedLoadCase.UpperMassForce * selectedLoadCombination.Beta1
                    + selectedLoadCase.FoundationMassForce * selectedLoadCombination.Beta2;
                double forceX = force * Math.Cos(selectedLoadCase.LoadAngle * Math.PI / 180);
                double forceY = force * Math.Sin(selectedLoadCase.LoadAngle * Math.PI / 180);

                valueVectors.Add(new(forceX, forceY, 0));
                values.Add(new Vector3D(forceX, forceY, 0).Length);
            }

            if (values.Count > 0)
            {
                // 直接 Colors.Black を使う
                List<ColorBaredGeometry> colorBaredGeometries = GetMonoColorBarGeometries(Colors.Black, values);
                Update3DValueArrows(points, valueVectors, colorBaredGeometries, 0, false);
            }
        }

        // 3Dポリライン描画メソッド
        public void Update3DValuePolyLines(
            ObservableCollection<ObservableCollection<Point3D>> pointSets,
            ObservableCollection<ObservableCollection<Vector3D>> valueVectorSets,
            Color color,
            bool hasColorBar
            )
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;

            if (viewModel == null || valueVectorSets.Count == 0)
            { return; }

            // 各ベクトルの長さ（ノルム）を計算し、その中で最大の値を取得
            double absMaxValue = valueVectorSets.SelectMany(v => v).Max(v => v.Length);
            ObservableCollection<Point> TextPos2Ds = [];

            List<PathGeometry> polyLines = [];
            List<LineGeometry> lines = [];

            ColorBaredGeometry colorBaredGeometry = new()
            {
                Color = color
            };

            PathGeometry pathGeometry = new();

            for (int i = 0; i < pointSets.Count; i++)
            {
                PathFigure pathFigure = new();
                PolyLineSegment polyLineSegment = new()
                {
                    Points = []
                };

                for (int j = 0; j < pointSets[i].Count; j++)
                {
                    // 座標
                    Point point2D = viewModel.CanvasThreeDView.Transformation(pointSets[i][j]);

                    double value = valueVectorSets[i][j].Length;

                    // valueVectors[i] を正規化
                    Vector3D normalizedVector = valueVectorSets[i][j];

                    // ベクトル長さ
                    double vectorLength2D = absMaxValue == 0 ? 0 : maxArrowLength2D * value / absMaxValue;

                    // 尾の位置の計算
                    Point3D shiftPoint3DTemp = pointSets[i][j] - normalizedVector;
                    Point shiftPoint2DTemp = viewModel.CanvasThreeDView.Transformation(shiftPoint3DTemp);

                    // ベクトル
                    Vector vectorTemp = point2D - shiftPoint2DTemp;

                    // shift2Dを計算
                    Point shiftPoint2D = vectorTemp.Length > Math.Pow(10, -3) ?
                    point2D + vectorTemp / vectorTemp.Length * vectorLength2D : point2D;

                    lines.Add(new(point2D, shiftPoint2D));

                    if (j == 0)
                    {
                        pathFigure.StartPoint = shiftPoint2D;
                    }
                    else
                    {
                        polyLineSegment.Points.Add(shiftPoint2D);
                        LineGeometry lineGeometry = new(point2D, shiftPoint2D);
                        pathGeometry.AddGeometry(lineGeometry);
                    }
                }

                // PathFigureにPolyLineSegmentを追加
                pathFigure.Segments.Add(polyLineSegment);
                // PathGeometryにPathFigureを追加
                pathGeometry.Figures.Add(pathFigure);
            }

            LineGeometry line = new(new(Canvas3DWidth, Canvas3DHeight), new(0, 0));
            colorBaredGeometry.PathGeometry.AddGeometry(pathGeometry);
            colorBaredGeometry.DrawPathes(Canvas3DLayout);
        }

        // 3D矢印描画メソッド
        public void Update3DValueArrows(
            ObservableCollection<Point3D> points,
            ObservableCollection<Vector3D> valueVectors, // 荷重方向ベクトル
            List<ColorBaredGeometry> colorBaredGeometries,
            int decimalPlaces,
            bool hasColorBar,
            bool isPointAtHead = true,
            bool isDoubleHead = false) // モーメント、回転角のとき二重矢印にする
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;
            double maxArrowLength3D = 5;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;

            if (viewModel == null || valueVectors.Count == 0)
            { return; }

            // 各ベクトルの長さ（ノルム）を計算し、その中で最大の値を取得
            double absMaxValue = valueVectors.Max(v => v.Length);
            ObservableCollection<Point> TextPos2Ds = [];
            Vector arrowVectorTemp;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < colorBaredGeometries.Count; j++)
                {
                    Vector3D viewVector = viewModel.CanvasThreeDView.GetViewVector(); // 注視ベクトル
                    double value = valueVectors[i].Length;

                    if ((j == 0 && colorBaredGeometries[j].BottomRange <= value && value <= colorBaredGeometries[j].TopRange) ||
                        (j != 0 && colorBaredGeometries[j].BottomRange < value && value <= colorBaredGeometries[j].TopRange))
                    {
                        Point arrowHead2D;
                        Point arrowEnd2D;

                        // valueVectors[i] を正規化
                        Vector3D normalizedVector = valueVectors[i];

                        // 尾の長さ
                        double tailLength2D = absMaxValue == 0 ? 0 : maxArrowLength3D * value / absMaxValue * viewModel.CanvasThreeDView.Scale * viewModel.ForceDiagramMultiplier;

                        if (isPointAtHead)
                        {
                            // 矢印頭座標
                            arrowHead2D = viewModel.CanvasThreeDView.Transformation(points[i]);

                            // 矢印尾の位置を計算
                            Point3D arrowEnd3DTemp = points[i] - normalizedVector;
                            Point arrowEnd2DTemp = viewModel.CanvasThreeDView.Transformation(arrowEnd3DTemp);

                            // arrowVector を計算
                            arrowVectorTemp = arrowHead2D - arrowEnd2DTemp;

                            // arrowEnd2D を計算
                            arrowEnd2D = arrowVectorTemp.Length > Math.Pow(10, -3) ?
                                arrowHead2D - arrowVectorTemp / arrowVectorTemp.Length * tailLength2D : arrowHead2D;
                            TextPos2Ds.Add(arrowEnd2D);
                        }
                        else // if (!isPointAtHead)
                        {
                            // 矢印尾座標
                            arrowEnd2D = viewModel.CanvasThreeDView.Transformation(points[i]);

                            // 矢印頭の位置を計算
                            Point3D arrowHead3DTemp = points[i] + normalizedVector;
                            Point arrowHead2DTemp = viewModel.CanvasThreeDView.Transformation(arrowHead3DTemp);

                            // arrowVector を計算
                            arrowVectorTemp = arrowHead2DTemp - arrowEnd2D;

                            // arrowEnd2D を計算
                            arrowHead2D = arrowVectorTemp.Length > Math.Pow(10, -3) ?
                                arrowEnd2D + arrowVectorTemp / arrowVectorTemp.Length * tailLength2D : arrowEnd2D;
                            TextPos2Ds.Add(arrowHead2D);
                        }

                        // 矢印本体
                        // LineGeometry の作成
                        LineGeometry lineTale = new(arrowHead2D, arrowEnd2D);
                        colorBaredGeometries[j].PathGeometry.AddGeometry(lineTale);

                        double cosAngle = Vector3D.DotProduct(viewVector, valueVectors[i]) / (viewVector.Length * valueVectors[i].Length);
                        double sinAngle = Math.Sqrt(1 - Math.Pow(cosAngle, 2));

                        Vector arrow2DTemp = arrowHead2D - arrowEnd2D;

                        // １つ目のコーン
                        Point center2D = arrowHead2D - arrowHeadLength2D * sinAngle * arrow2DTemp / arrow2DTemp.Length;
                        EllipseGeometry ellipse = new(center2D, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * cosAngle);
                        RotateTransform rotateTransform = new()
                        {
                            Angle = GetAngle(arrowVectorTemp) + 90, // 回転角度（度単位）
                            CenterX = center2D.X,
                            CenterY = center2D.Y
                        };

                        GeometryGroup geometryGroup = new();
                        geometryGroup.Children.Add(ellipse);
                        geometryGroup.Transform = rotateTransform;
                        colorBaredGeometries[j].PathGeometry.AddGeometry(geometryGroup);

                        // 母線
                        List<LineGeometry> lineGeometries =
                        GetConeGeneratrixes3D(arrowHead2D, center2D, arrowHeadDia2D * 0.5);

                        foreach (LineGeometry lineGeometry in lineGeometries)
                        {
                            colorBaredGeometries[j].PathGeometry.AddGeometry(lineGeometry);
                        }

                        // 2つ目のコーン（ダブルヘッド）
                        if (isDoubleHead)
                        {
                            // 1つ目のコーンの底面中心(center2D)を2つ目のコーンの頂点とする
                            Point arrowHead2D_2 = center2D;
                            Point center2D_2 = arrowHead2D_2 - arrowHeadLength2D * sinAngle * arrow2DTemp / arrow2DTemp.Length;

                            EllipseGeometry ellipse2 = new(center2D_2, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * cosAngle);
                            RotateTransform rotateTransform2 = new()
                            {
                                Angle = GetAngle(arrowVectorTemp) + 90,
                                CenterX = center2D_2.X,
                                CenterY = center2D_2.Y
                            };
                            GeometryGroup geometryGroup2 = new();
                            geometryGroup2.Children.Add(ellipse2);
                            geometryGroup2.Transform = rotateTransform2;
                            colorBaredGeometries[j].PathGeometry.AddGeometry(geometryGroup2);

                            List<LineGeometry> lineGeometries2 = GetConeGeneratrixes3D(
                                arrowHead2D_2, center2D_2, arrowHeadDia2D * 0.5);
                            foreach (LineGeometry lineGeometry in lineGeometries2)
                            {
                                colorBaredGeometries[j].PathGeometry.AddGeometry(lineGeometry);
                            }
                        }
                    }
                }
            }

            foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
            {
                colorBaredGeometry.DrawPathes(Canvas3DLayout);
            }

            ObservableCollection<double> values = [];
            foreach (Vector3D value in valueVectors)
            {
                values.Add(value.Length);
            }

            UpdateValueTexts2D(TextPos2Ds, values, colorBaredGeometries, decimalPlaces, "R", "T"); // テキスト

            if (hasColorBar) { ColorBar.DrawStepColorBar(ColorBarCanvas, colorBaredGeometries); }
            else { ColorBarCanvas.Children.Clear(); }
        }

        // 角度取得メソッド
        public static double GetAngle(Vector vector)
        {
            if (vector.Length == 0)
            {
                return 0;
            }

            // (1, 0) との内積を計算
            double dotProduct = Vector.Multiply(vector, new Vector(1, 0));

            // ベクトルの長さを計算
            double vectorLength = vector.Length;

            // cosθ を計算
            double cosTheta = dotProduct / vectorLength;

            // 角度をラジアンから度に変換
            double angle = Math.Acos(cosTheta) * 180 / Math.PI;

            // y成分が負の場合、角度を反転
            if (vector.Y < 0)
            {
                angle = -angle;
            }

            return angle;
        }

        // 母線を返すメソッド
        public static List<LineGeometry> GetConeGeneratrixes3D(Point arrowHead2D, Point center2D, double radius)
        {
            Vector directionVector = (arrowHead2D - center2D);
            Vector unitOrthogonalVector = GetUnitOrthogonalVector(directionVector);
            List<LineGeometry> lineGeometries = [];
            for (int i = -1; i <= 1; i += 2)
            {
                lineGeometries.Add(
                    new()
                    {
                        StartPoint = arrowHead2D,
                        EndPoint = center2D + i * unitOrthogonalVector * radius,
                    });
            }
            return lineGeometries;
        }

        // 単位直交ベクトルを得るメソッド
        public static Vector GetUnitOrthogonalVector(Vector vector)
        {
            // 元のベクトルを90度回転させた直交ベクトルを求める
            Vector orthogonalVector = new(-vector.Y, vector.X);

            // 直交ベクトルを単位ベクトルに正規化する
            orthogonalVector.Normalize();

            return orthogonalVector;
        }
    }
}
