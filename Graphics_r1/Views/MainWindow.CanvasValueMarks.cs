using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Element = PileDesign.Models.InputData.Element;
using Node = PileDesign.FEM.Node;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using TransformGroup = System.Windows.Media.TransformGroup;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {
        // バブルチャート描画メソッド
        // バブル（値は mm 前提）
        public void UpdateValueBubbles(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBarGeometries)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel == null || values.Count == 0) return;

            double flattening = viewModel.CanvasThreeDView.Flattening;

            for (int i = 0; i < points.Count; i++)
            {
                double valueMm = values[i];
                // 直径(px) = mm → m → px
                double bubbleDia2D = Math.Abs(valueMm) / 1000.0 * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;

                if (bubbleDia2D <= 0) continue;

                Point center2D = viewModel.CanvasThreeDView.Transformation(points[i]);
                EllipseGeometry ellipse = new(center2D, bubbleDia2D * 0.5, bubbleDia2D * 0.5 * flattening);

                foreach (ColorBaredGeometry colorBaredGeometry in colorBarGeometries)
                {
                    if (colorBaredGeometry.BottomRange <= valueMm && valueMm <= colorBaredGeometry.TopRange)
                    {
                        colorBaredGeometry.PathGeometry.AddGeometry(ellipse);
                        break;
                    }
                }
            }
        }
        //public void UpdateValueBubbles(
        //    ObservableCollection<Point3D> points,
        //    ObservableCollection<double> values,
        //    List<ColorBaredGeometry> colorBarGeometries
        //    )
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

        //    if (viewModel == null || values.Count == 0)
        //    { return; }
        //    double maxBubbleDia2D/* = viewModel.BubbleDia*/;

        //    double flattening = viewModel.CanvasThreeDView.Flattening;

        //    double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

        //    for (int i = 0; i < points.Count; i++)
        //    {
        //        double value = values[i];
        //        double bubbleDia2D;
        //        if (absMaxValue == 0)
        //        {
        //            bubbleDia2D = 0;
        //        }
        //        else
        //        {
        //            bubbleDia2D = Math.Abs(value) * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;
        //        }

        //        Point center2D = viewModel.CanvasThreeDView.Transformation(points[i]);

        //        EllipseGeometry ellipse = new(center2D, bubbleDia2D * 0.5, bubbleDia2D * 0.5 * flattening);

        //        foreach (ColorBaredGeometry colorBaredGeometry in colorBarGeometries)
        //        {
        //            if (colorBaredGeometry.BottomRange <= value && value <= colorBaredGeometry.TopRange)
        //            {
        //                colorBaredGeometry.PathGeometry.AddGeometry(ellipse);
        //                break;
        //            }
        //        }
        //    }
        //}


        //public void UpdateValueArrows(
        //    ObservableCollection<Point3D> points,
        //    ObservableCollection<double> values,
        //    List<ColorBaredGeometry> colorBaredGeometries
        //    )
        //{
        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    /*double maxArrowLength2D*//* = viewModel.ArrowLength*/
        //    ;
        //    double arrowHeadLength2D = viewModel.ArrowHeadLength;
        //    double arrowHeadDia2D = viewModel.ArrowHeadDia;
        //    if (viewModel == null || values.Count == 0)
        //    { return; }

        //    double flattening = viewModel.CanvasThreeDView.Flattening;
        //    double flattening0 = Math.Sqrt(1 - Math.Pow(flattening, 2));
        //    //double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

        //    for (int i = 0; i < points.Count; i++)
        //    {
        //        foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
        //        {
        //            double value = values[i];
        //            if (colorBaredGeometry.BottomRange <= value && value <= colorBaredGeometry.TopRange)
        //            {
        //                // 尾
        //                double tailLength2D = value * 0.01 * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;

        //                // 頂点
        //                Point arrowEnd2D = viewModel.CanvasThreeDView.Transformation(points[i]);
        //                // 頂点
        //                Point arrowHead2D = viewModel.CanvasThreeDView.Transformation(points[i] - new Vector3D(0, 0, tailLength2D));
        //                // LineGeometryの作成
        //                LineGeometry lineTale = new(arrowEnd2D, arrowHead2D);

        //                colorBaredGeometry.PathGeometry.AddGeometry(lineTale);

        //                //楕円中心位置
        //                Point center2D = new(arrowHead2D.X, arrowHead2D.Y - arrowHeadLength2D * flattening0);

        //                //楕円
        //                EllipseGeometry ellipse1 = new(center2D, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * flattening);
        //                colorBaredGeometry.PathGeometry.AddGeometry(ellipse1);

        //                //母線
        //                List<LineGeometry> lineGeometries =
        //                GetConeGeneratrixes(center2D, arrowHeadDia2D * 0.5, 0, -arrowHeadLength2D * flattening0, flattening);

        //                foreach (LineGeometry lineGeometry in lineGeometries)
        //                {
        //                    colorBaredGeometry.PathGeometry.AddGeometry(lineGeometry);
        //                }
        //            }
        //        }
        //    }

        //    // 描画する要素に対して、IsHitTestVisibleをfalseに設定
        //    //foreach (UIElement element in Canvas3DLayout.Children)
        //    //{
        //    //    element.IsHitTestVisible = false;
        //    //}

        //    //// ZIndexを調整して他のUI要素の背面に配置
        //    //Panel.SetZIndex(Canvas3DLayout, -1);
        //}


        // 矢印描画メソッド
        // 矢印（値は mm 前提）
        public void UpdateValueArrows(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double arrowHeadLength2D = viewModel.ArrowHeadLength;
            double arrowHeadDia2D = viewModel.ArrowHeadDia;
            if (viewModel == null || values.Count == 0) return;

            double flattening = viewModel.CanvasThreeDView.Flattening;
            double flattening0 = Math.Sqrt(1 - Math.Pow(flattening, 2));

            for (int i = 0; i < points.Count; i++)
            {
                foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
                {
                    double valueMm = values[i];
                    if (colorBaredGeometry.BottomRange <= valueMm && valueMm <= colorBaredGeometry.TopRange)
                    {
                        // 尾の長さ(px) = mm → m → px
                        double tailLength2D = Math.Abs(valueMm) / 1000.0 * viewModel.CanvasThreeDView.Scale * viewModel.DispDiagramMultiplier;

                        Point arrowEnd2D = viewModel.CanvasThreeDView.Transformation(points[i]); // 頂点
                        Point arrowHead2D = viewModel.CanvasThreeDView.Transformation(points[i] - new Vector3D(0, 0, tailLength2D)); // 下向き

                        // 本体
                        colorBaredGeometry.PathGeometry.AddGeometry(new LineGeometry(arrowEnd2D, arrowHead2D));

                        // ヘッド（簡易コーン）
                        Point center2D = new(arrowHead2D.X, arrowHead2D.Y - arrowHeadLength2D * flattening0);
                        EllipseGeometry ellipse1 = new(center2D, arrowHeadDia2D * 0.5, arrowHeadDia2D * 0.5 * flattening);
                        colorBaredGeometry.PathGeometry.AddGeometry(ellipse1);

                        var lineGeometries = GetConeGeneratrixes2D(center2D, arrowHeadDia2D * 0.5, 0, -arrowHeadLength2D * flattening0, flattening);
                        foreach (LineGeometry lineGeometry in lineGeometries)
                            colorBaredGeometry.PathGeometry.AddGeometry(lineGeometry);

                        break;
                    }
                }
            }
        }


        private void UpdateValueTextsCore<T>(
            IList<T> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            Func<T, Point> to2D,
            int decimalPlaces = 3,
            string horizontalPos = "L",
            string verticalPos = "B")
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            if (viewModel == null || values.Count == 0) return;

            for (int i = 0; i < points.Count; i++)
            {
                double value = values[i];
                Point point2D = to2D(points[i]);
                for (int j = 0; j < colorBaredGeometries.Count; j++)
                {
                    if ((j == 0 && colorBaredGeometries[j].BottomRange <= value && value <= colorBaredGeometries[j].TopRange) ||
                        (j != 0 && colorBaredGeometries[j].BottomRange < value && value <= colorBaredGeometries[j].TopRange))
                    {
                        AddText3D(
                            new SolidColorBrush(colorBaredGeometries[j].Color),
                            GetNumberString(value, decimalPlaces),
                            point2D.X,
                            point2D.Y,
                            horizontalPos,
                            verticalPos,
                            0.0
                        );
                        break;
                    }
                }
            }
        }

        public void UpdateValueTexts2D(
            ObservableCollection<Point> points2D,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries,
            int decimalPlaces = 3,
            string horizontalPos = "L",
            string verticalPos = "B")
        {
            UpdateValueTextsCore(points2D, values, colorBaredGeometries, p => p, decimalPlaces, horizontalPos, verticalPos);
        }

        public void UpdateValueTexts(
            ObservableCollection<Point3D> points,
            ObservableCollection<double> values,
            List<ColorBaredGeometry> colorBaredGeometries)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            UpdateValueTextsCore(points, values, colorBaredGeometries, p => viewModel.CanvasThreeDView.Transformation(p));
        }

        // 番号取得メソッド
        private string GetNumberString(double value, int decimalPlaces)
        {
            return value.ToString($"N{decimalPlaces}");
        }

        // テキスト位置の調整メソッド
        private Point AdjustTextPosition(Point originalPoint, Size textSize, string horizontalPos, string verticalPos, double textAngle)
        {
            double x = originalPoint.X;
            double y = originalPoint.Y;

            double dx = 0;
            double dy = 0;
            switch (horizontalPos)
            {
                case "C":
                    dx = textSize.Width / 2;
                    break;
                case "R":
                    dx = textSize.Width;
                    break;
            }

            switch (verticalPos)
            {
                case "C":
                    dy = textSize.Height / 2;
                    break;
                case "B":
                    dy = textSize.Height;
                    break;
            }

            x -= Math.Cos(textAngle / 180 * Math.PI) * dx - Math.Sin(textAngle / 180 * Math.PI) * dy;
            y -= Math.Sin(textAngle / 180 * Math.PI) * dx + Math.Cos(textAngle / 180 * Math.PI) * dy;
            return new Point(x, y);
        }



        // 有効数字1桁の値を返すメソッド
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (value == 0)
            { return 0; }

            double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) + 1 - significantDigits);
            return Math.Round(value / scale) * scale;
        }

        // a の倍数で b を超える最小の値を返すメソッド
        public static double GetSmallestMultipleGreaterThan(double a, double b)
        {
            return Math.Ceiling(b / a) * a;
        }

        // a の倍数で b より小さい最大の値を返すメソッド
        public static double GetLargestMultipleLessThan(double a, double b)
        {
            return Math.Floor(b / a) * a;
        }

        //テキスト一括レンダリングメソッド

        private Image _textLayerImage;

        private void RenderTextBlocksWithDrawingVisual()
        {
            if (Canvas3DLayout == null)
                return;
            if ((int)Canvas3DLayout.ActualWidth > 0 && (int)Canvas3DLayout.ActualHeight > 0)
            {
                DrawingVisual drawingVisual = new();
                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    foreach (var textBlockInfo in TextBlockInfos)
                    {
                        var typeface = new Typeface(
                            new FontFamily("Arial"),
                            FontStyles.Normal,
                            FontWeights.Normal,
                            FontStretches.Normal);

                        FormattedText formattedText = new(
                            textBlockInfo.TextBlock.Text,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            textBlockInfo.TextBlock.FontSize,
                            textBlockInfo.TextBlock.Foreground,
                            VisualTreeHelper.GetDpi(Canvas3DLayout).PixelsPerDip
                        );
                        dc.PushTransform(new ScaleTransform(1.0, textBlockInfo.ScaleY, textBlockInfo.X, textBlockInfo.Y));
                        dc.PushTransform(new RotateTransform(textBlockInfo.TextAngle, textBlockInfo.X, textBlockInfo.Y));
                        dc.DrawText(formattedText, new Point(textBlockInfo.X, textBlockInfo.Y));
                        dc.Pop();
                        dc.Pop();
                    }
                }

                RenderTargetBitmap renderBitmap = new((int)Canvas3DLayout.ActualWidth, (int)Canvas3DLayout.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(drawingVisual);

                RenderOptions.SetBitmapScalingMode(renderBitmap, BitmapScalingMode.HighQuality);

                // _textLayerImageがnullまたはCanvasから消えていたら再生成
                if (_textLayerImage == null || !Canvas3DLayout.Children.Contains(_textLayerImage))
                {
                    _textLayerImage = new Image();
                    Canvas.SetLeft(_textLayerImage, 0);
                    Canvas.SetTop(_textLayerImage, 0);
                    Canvas3DLayout.Children.Add(_textLayerImage);
                }
                _textLayerImage.Source = renderBitmap;

                TextBlockInfos.Clear();
            }
        }

        // バブル・矢印描画
        private void DrawBubbleAndArrow(ObservableCollection<Point3D> points, ObservableCollection<double> values, string title, string unit)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (values.Count == 0) return;

            //var colorBaredGeometries = GetColorBarGeometries(values);
            var colorBaredGeometries = ColorBarUtils.GetColorBarGeometries(values);

            bool drewSomething = false;

            if (viewModel.IsBubbleVisible)
            {
                UpdateValueBubbles(points, values, colorBaredGeometries);
                drewSomething = true;
            }

            if (viewModel.IsArrowVisible)
            {
                bool isPlanView = Math.Abs(viewModel.CanvasThreeDView.Phi - 90.0) < 0.5 && !viewModel.CanvasThreeDView.IsPerspective;
                if (!isPlanView)
                {
                    UpdateValueArrows(points, values, colorBaredGeometries);
                    drewSomething = true;
                }
            }

            // フォールバック（両方オフ/見えない時、1点だけ黒丸を打って確認）
            if (!drewSomething && points.Count > 0)
            {
                var p2d = viewModel.CanvasThreeDView.Transformation(points[0]);
                var pg = new PathGeometry();
                pg.AddGeometry(new EllipseGeometry(p2d, 3, 3));
                var mono = GetMonoColorBarGeometries(Colors.Black, [1.0]);
                mono[0].PathGeometry = pg;
                mono[0].DrawPathes(Canvas3DLayout);
            }

            UpdateValueTexts(points, values, colorBaredGeometries);

            foreach (var g in colorBaredGeometries) g.DrawPathes(Canvas3DLayout);

            ColorBar.DrawStepColorBar(
                ColorBarCanvas,
                colorBaredGeometries,
                title,
                unit,
                values.Min(),
                values.Max(),
                "{0:N" + viewModel.DecimalPlaces + "}",
                viewModel.LabelSize
            );
        }
        //private void DrawBubbleAndArrow(ObservableCollection<Point3D> points, ObservableCollection<double> values, string title, string unit)
        //{
        //    if (DataContext is not MainWindowViewModel viewModel) return;
        //    if (values.Count == 0) return;

        //    List<ColorBaredGeometry> colorBaredGeometries = GetColorBarGeometries(values); // カラーバージオメトリ取得

        //    if (viewModel.IsBubbleVisible) // バブル
        //    { UpdateValueBubbles(points, values, colorBaredGeometries); }

        //    if (viewModel.IsArrowVisible) // 矢印
        //    {
        //        // 平面図（上から見下ろし）の場合はZ方向矢印が投影で消えるためスキップ
        //        bool isPlanView = Math.Abs(viewModel.CanvasThreeDView.Phi - 90.0) < 0.5 && !viewModel.CanvasThreeDView.IsPerspective;
        //        if (!isPlanView)
        //        {
        //            UpdateValueArrows(points, values, colorBaredGeometries);
        //        }
        //    }

        //    if (viewModel.IsDeformedElementVisible) // 変形後要素
        //    { UpdateDeformedGeneralelement3D(values); }

        //    UpdateValueTexts(points, values, colorBaredGeometries); // テキスト

        //    foreach (ColorBaredGeometry colorBaredGeometry in colorBaredGeometries)
        //    { colorBaredGeometry.DrawPathes(Canvas3DLayout); }

        //    ColorBar.DrawStepColorBar(
        //        ColorBarCanvas,
        //        colorBaredGeometries,
        //        title,
        //        unit,
        //        values.Min(),
        //        values.Max(),
        //        "{0:N" + viewModel.DecimalPlaces + "}",
        //        viewModel.LabelSize
        //    );
        //}
    }
}
