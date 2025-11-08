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
    internal partial class PileTopDrawer(PileTopViewModel _viewModel, Canvas _canvas)
    {
        private Canvas Canvas { get; } = _canvas;
        private PileTopViewModel ViewModel { get; } = _viewModel;
        private PathGeometry DrawingGeometry = new();
        private double Scale { get; set; }

        // スケール取得メソッド
        private double GetScale()
        {
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            double dia = 0.0;
            if (ViewModel.PileTopType == "キャプテンパイル")
            {
                dia = ViewModel.PileTop.CaptainPile.CTPConcrete.D;
            }
            else if (ViewModel.PileTopType == "FTパイル")
            {
                dia = ViewModel.PileTop.FTPile.FTPilePile.D1;
            }

            double baseDimension = Math.Max(dia + 150.0, 1200.0);
            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }


        public void RedrawShapes()
        {
            // 描画用パスをクリア
            DrawingGeometry = new PathGeometry();

            Scale = GetScale();

            Canvas.Children.Clear();

            DrawGauge();


            if (ViewModel.PileTopType == "キャプテンパイル")
            {
                // 杭
                double concreteOutDia = ViewModel.PileTop.CaptainPile.CTPConcrete.D;
                DrawDonut("concrete", concreteOutDia, 0.0);

                // PCリング
                double pcRingOutDia = ViewModel.PileTop.CaptainPile.PCRing.RD1;
                double pcRingInDia = ViewModel.PileTop.CaptainPile.PCRing.RD1 + 2 * ViewModel.PileTop.CaptainPile.PCRing.Tc;
                DrawDonut("concrete", pcRingOutDia, pcRingInDia);

                // 鋼管
                double steelRingOutDia = ViewModel.PileTop.CaptainPile.PCRing.RD1;
                double steelRingInDia = ViewModel.PileTop.CaptainPile.PCRing.RD1 + 2 * ViewModel.PileTop.CaptainPile.PCRing.RingSteelTs;
                DrawDonut("steel", steelRingOutDia, steelRingInDia);

                // 鉄筋
                int number = ViewModel.PileTop.CaptainPile.PCRing.BarNum;
                double dia = ExtractNumber(ViewModel.PileTop.CaptainPile.PCRing.BarSize);
                double pcd = pcRingOutDia - (40.0 + ExtractNumber(ViewModel.PileTop.CaptainPile.PCRing.SpiralDia) + dia * 0.5) * 2.0;
                DrawMainBars(number, dia, pcd);

                // 引張鉄筋
                if (ViewModel.PileTop.CaptainPile.CTPTensionRebars.HasTensionRebars)
                {
                    if (ViewModel.PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement)
                    {
                        int numberTen = ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberCircle;
                        double diaTen = ExtractNumber(ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcdTen = ViewModel.PileTop.CaptainPile.CTPTensionRebars.TDorTBmax;
                        DrawMainBars(numberTen, diaTen, pcdTen);
                    }
                    else
                    {
                        int numberTen = ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberSquare;
                        double diaTen = ExtractNumber(ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcdTen = ViewModel.PileTop.CaptainPile.CTPTensionRebars.TDorTBmax;
                        DrawSquareDotArrangement(numberTen, diaTen, pcdTen);
                    }
                }
            }


            Path path = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                Data = DrawingGeometry
            };

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
                //double linelength;
                //if (i % (major / minor) == 0) { linelength = 50; } else { linelength = 25; }
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
            Canvas.Children.Add(barPath); // Canvasに追加
        }

        private void DrawSquareDotArrangement(double number, double dia, double tB)
        {
            PathGeometry geo = new();
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < number / 4; j++)
                {
                    double x0 = -tB * 0.5 + tB / 4 * j;
                    double y0 = -tB * 0.5;

                    double X = Math.Cos(Math.PI * 0.5 * i) * x0 - Math.Sin(Math.PI * 0.5 * i) * y0;
                    double Y = Math.Sin(Math.PI * 0.5 * i) * x0 + Math.Cos(Math.PI * 0.5 * i) * y0;
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
                Canvas.Children.Add(barPath); // Canvasに追加
            }
        }

        private static double ExtractNumber(string input)
        {
            if (input == null)
            {
                return 0;
            }

            // 数字以外の文字を削除してから double に変換
            string numericPart = MyRegex().Replace(input, "");
            if (double.TryParse(numericPart, out double result))
            {
                return result;
            }
            // 変換できない場合は例外をスローするか、適切な処理を行う
            throw new ArgumentException("入力文字列に数字が含まれていません。");
        }

        [GeneratedRegex("[^0-9.]")]
        private static partial Regex MyRegex();
    }
}
