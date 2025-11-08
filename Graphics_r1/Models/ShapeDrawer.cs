using PileDesign.Common;
using PileDesign.ViewModels;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Models
{
    public class ShapeDrawer(PileSectionViewModel viewModel, Canvas canvas)
    {
        private Canvas Canvas { get; } = canvas ?? throw new ArgumentNullException(nameof(canvas));
        private PileSectionViewModel ViewModel { get; } = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        private PathGeometry DrawingGeometry = new();
        private double Scale { get; set; }

        // スケール取得メソッド
        private double GetScale()
        {
            if (Canvas == null)
            { return 1.00; }
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            double baseDimension = Math.Max(ViewModel.PileSection.PileDiameter + 150.0, 1200.0);
            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }


        public void RedrawShapes()
        {
            // 描画用パスをクリア
            DrawingGeometry = new PathGeometry();
            if (Canvas == null)
            { return; }

            Scale = GetScale();
            Canvas.Children.Clear();
            DrawGauge();


            if (ViewModel.PileSection.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                ViewModel.PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && ViewModel.PileSection.PileSectionType == "鉄筋コンクリート部")
            {
                double concreteOutDia = ViewModel.PileSection.ConcreteOutDia;
                DrawDonut("concrete", concreteOutDia, 0.0);
                int number = ViewModel.PileSection.MainBarNum;
                double dia = ExtractNumber(ViewModel.PileSection.MainBarSize);
                double pcd = concreteOutDia - ViewModel.PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double hoopsize = ExtractNumber(ViewModel.PileSection.HoopSize);
                double outDia = concreteOutDia - ViewModel.PileSection.HoopCenterCover * 2.0 + hoopsize;
                double inDia = outDia - 2 * hoopsize;
                DrawDonut("hoop", outDia, inDia);
            }
            else if (ViewModel.PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && ViewModel.PileSection.PileSectionType == "鋼管コンクリート部")
            {
                double concreteOutDia = ViewModel.PileSection.ConcreteOutDia;
                double outdia = ViewModel.PileSection.PipeDia;
                double india = ViewModel.PileSection.PipeDia - 2 * ViewModel.PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);

                DrawDonut("concrete", concreteOutDia, 0.0);

                int number = ViewModel.PileSection.MainBarNum;
                double dia = ExtractNumber(ViewModel.PileSection.MainBarSize);
                double pcd = concreteOutDia - ViewModel.PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
            }
            else if (ViewModel.PileSection.PileSectionType == "PHC杭")
            {
                double outdia = ViewModel.PileSection.ConcreteOutDia;
                double india = ViewModel.PileSection.ConcreteOutDia - 2 * ViewModel.PileSection.ConcreteThickness;
                DrawDonut("concrete", outdia, india);
                double tendonPCD = ViewModel.PileSection.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (ViewModel.PileSection.PileSectionType == "PRC杭")
            {
                double outdia = ViewModel.PileSection.ConcreteOutDia;
                double india = ViewModel.PileSection.ConcreteOutDia - 2 * ViewModel.PileSection.ConcreteThickness;
                DrawDonut("concrete", outdia, india);

                int number = ViewModel.PileSection.MainBarNum;
                double dia = ExtractNumber(ViewModel.PileSection.MainBarSize);
                double pcd = outdia - ViewModel.PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double tendonPCD = ViewModel.PileSection.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (ViewModel.PileSection.PileSectionType == "SC杭")
            {
                double outdia = ViewModel.PileSection.PipeDia;
                double india = ViewModel.PileSection.PipeDia - 2 * ViewModel.PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);
                double concreteOutDia = ViewModel.PileSection.ConcreteOutDia;
                double concreteIndia = concreteOutDia - 2 * ViewModel.PileSection.ConcreteThickness;
                DrawDonut("concrete", concreteOutDia, concreteIndia);
            }
            else if (ViewModel.PileSection.PileBodyType == "鋼管杭")
            {
                double outdia = ViewModel.PileSection.PipeDia;
                double india = ViewModel.PileSection.PipeDia - 2 * ViewModel.PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);
            }

            Path path1 = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                Data = DrawingGeometry
            };
            Path path = path1;

            Canvas.Children.Add(path);
        }

        private void DrawGauge()
        {
            // 目盛
            int minor = 100;
            int major = 500;

            LineGeometry line0 = new()
            {
                StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5),
                EndPoint = new Point(50 * Scale, Canvas.ActualHeight * 0.5),
            };
            DrawingGeometry.AddGeometry(line0);
            int i = 1;

            while (i * minor * Scale <= Canvas.ActualHeight * 0.5)
            {
                double linelength = i % (major / minor) == 0 ? 50 : 25;
                LineGeometry line1 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5 + minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight * 0.5 + minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line1);

                LineGeometry line2 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5 - minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight * 0.5 - minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line2);
                i += 1;
            }
        }

        // ドーナツ描画メソッド
        private void DrawDonut(string type, double outdia, double india)
        {
            PathGeometry geometry = new();
            EllipseGeometry outerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), outdia * 0.5 * Scale, outdia * 0.5 * Scale);
            EllipseGeometry innerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), india * 0.5 * Scale, india * 0.5 * Scale);
            geometry.AddGeometry(outerCircle);
            geometry.AddGeometry(innerCircle);
            Path donutPath = new();
            if (type == "concrete")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue; // 線の色
                //donutPath.Fill = Brushes.NavajoWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            if (type == "steelPipe")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue; // 線の色
                //donutPath.Fill = Brushes.WhiteSmoke;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }

            if (type == "hoop")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue;
                //donutPath.Fill = Brushes.AntiqueWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            Canvas.Children.Add(donutPath); // Canvasに追加
        }

        // PC鋼材描画メソッド
        private void DrawTendons(double dia)
        {
            EllipseGeometry circleGeometry = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), dia * 0.5 * Scale, dia * 0.5 * Scale);

            Path dashedCirclePath = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([4, 2]), // 破線パターン
                Data = circleGeometry
            };
            Canvas.Children.Add(dashedCirclePath); // Canvasに追加
        }

        // 主筋描画メソッド
        private void DrawMainBars(double number, double dia, double pcd)
        {
            PathGeometry geo = new();

            for (int i = 0; i < number; i++)
            {
                double X = pcd * 0.5 * Math.Cos(i / number * 2 * Math.PI) * Scale;
                double Y = pcd * 0.5 * Math.Sin(i / number * 2 * Math.PI) * Scale;
                EllipseGeometry ellipse = new(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                geo.AddGeometry(ellipse);
            }
            Path barPath = new()
            {
                Stroke = NikkenBrush.SkyBlue,
                //Fill = Brushes.DarkBlue,
                StrokeThickness = 1,
                //StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = geo
            };
            Canvas.Children.Add(barPath);
        }

        private static double ExtractNumber(string input)
        {
            if (input == null)
            {
                return 0;
            }

            // 数字以外の文字を削除してから double に変換
            string numericPart = Regex.Replace(input, "[^0-9.]", "");
            if (double.TryParse(numericPart, out double result))
            {
                return result;
            }
            // 変換できない場合は例外をスローするか、適切な処理を行う
            throw new ArgumentException("入力文字列に数字が含まれていません。");
        }
    }
}
