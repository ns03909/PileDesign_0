using PileDesignCore.Shared;
using System;
using System.Windows;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;


namespace PileDesignCore.PileSection
{
    internal class ShapeDrawer
    {
        private Canvas Canvas { get; }
        private PileSectionViewModel ViewModel { get; }
        private PathGeometry DrawingGeometry = new PathGeometry();
        private double Scale { get; set; }

        // コンストラクタ
        public ShapeDrawer(PileSectionViewModel _viewModel, Canvas _canvas)
        {
            ViewModel = _viewModel;
            Canvas = _canvas;
        }

        // スケール取得メソッド
        private double GetScale()
        {
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            double baseDimension = Math.Max(ViewModel.PileDiameter + 150.0, 1200.0);
            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }


        public void RedrawShapes()
        {
            // 描画用パスをクリア
            DrawingGeometry = new PathGeometry();

            Scale = GetScale();

            Canvas.Children.Clear();

            DrawGauge();


            if (ViewModel.SelectedPileBodyType == "場所打ち鉄筋コンクリート杭" ||
                (ViewModel.SelectedPileBodyType == "場所打ち鋼管コンクリート杭" && ViewModel.SelectedPileSectionType == "鉄筋コンクリート部"))
            {
                double concreteOutDia = ViewModel.ConcreteOutDia;
                DrawDonut("concrete", concreteOutDia, 0.0);
                int number = ViewModel.MainBarNum;
                double dia = ExtractNumber(ViewModel.MainBarSize);
                double pcd = concreteOutDia - ViewModel.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double hoopsize = ExtractNumber(ViewModel.HoopSize);
                double outDia = concreteOutDia - ViewModel.HoopCenterCover * 2.0 + hoopsize;
                double inDia = outDia - 2 * hoopsize;
                DrawDonut("hoop", outDia, inDia);
            }
            else if (ViewModel.SelectedPileBodyType == "場所打ち鋼管コンクリート杭" && ViewModel.SelectedPileSectionType == "鋼管コンクリート部")
            {
                double concreteOutDia = ViewModel.ConcreteOutDia;
                double outdia = ViewModel.PipeDia;
                double india = ViewModel.PipeDia - 2 * ViewModel.PipeTs;
                DrawDonut("steelPipe", outdia, india);

                DrawDonut("concrete", concreteOutDia, 0.0);

                int number = ViewModel.MainBarNum;
                double dia = ExtractNumber(ViewModel.MainBarSize);
                double pcd = concreteOutDia - ViewModel.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
            }
            else if (ViewModel.SelectedPileSectionType == "PHC杭")
            {
                double outdia = ViewModel.ConcreteOutDia;
                double india = ViewModel.ConcreteOutDia - 2 * ViewModel.ConcreteThickness;
                DrawDonut("concrete", outdia, india);
                double tendonPCD = ViewModel.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (ViewModel.SelectedPileSectionType == "PRC杭")
            {
                double outdia = ViewModel.ConcreteOutDia;
                double india = ViewModel.ConcreteOutDia - 2 * ViewModel.ConcreteThickness;
                DrawDonut("concrete", outdia, india);

                int number = ViewModel.MainBarNum;
                double dia = ExtractNumber(ViewModel.MainBarSize);
                double pcd = outdia - ViewModel.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double tendonPCD = ViewModel.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (ViewModel.SelectedPileSectionType == "SC杭")
            {
                double outdia = ViewModel.PipeDia;
                double india = ViewModel.PipeDia - 2 * ViewModel.PipeTs;
                DrawDonut("steelPipe", outdia, india);
                double concreteOutDia = ViewModel.ConcreteOutDia;
                double concreteIndia = concreteOutDia - 2 * ViewModel.ConcreteThickness;
                DrawDonut("concrete", concreteOutDia, concreteIndia);
            }
            else if (ViewModel.SelectedPileBodyType == "鋼管杭")
            {
                double outdia = ViewModel.PipeDia;
                double india = ViewModel.PipeDia - 2 * ViewModel.PipeTs;
                DrawDonut("steelPipe", outdia, india);
            }

            System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
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

            LineGeometry line0 = new LineGeometry()
            {
                StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0),
                EndPoint = new Point(50 * Scale, Canvas.ActualHeight / 2.0),
            };
            DrawingGeometry.AddGeometry(line0);
            int i = 1;

            while (i * minor * Scale <= Canvas.ActualHeight / 2.0)
            {
                double linelength = (i % (major / minor) == 0) ? 50 : 25;
                LineGeometry line1 = new LineGeometry()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line1);

                LineGeometry line2 = new LineGeometry()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0 - minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight / 2.0 - minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line2);
                i += 1;
            }
        }

        // ドーナツ描画メソッド
        private void DrawDonut(string type, double outdia, double india)
        {
            PathGeometry geometry = new PathGeometry();
            EllipseGeometry outerCircle = new EllipseGeometry(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2), outdia / 2 * Scale, outdia / 2 * Scale);
            EllipseGeometry innerCircle = new EllipseGeometry(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2), india / 2 * Scale, india / 2 * Scale);
            geometry.AddGeometry(outerCircle);
            geometry.AddGeometry(innerCircle);
            Path donutPath = new Path();
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
            EllipseGeometry circleGeometry = new EllipseGeometry(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2), dia / 2 * Scale, dia / 2 * Scale);

            Path dashedCirclePath = new Path
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = circleGeometry
            };
            Canvas.Children.Add(dashedCirclePath); // Canvasに追加
        }

        // 主筋描画メソッド
        private void DrawMainBars(double number, double dia, double pcd)
        {
            PathGeometry geo = new PathGeometry();

            for (int i = 0; i < number; i++)
            {
                double X = pcd / 2.0 * Math.Cos(i / number * 2 * Math.PI) * Scale;
                double Y = pcd / 2.0 * Math.Sin(i / number * 2 * Math.PI) * Scale;
                EllipseGeometry ellipse = new EllipseGeometry(new Point(Canvas.ActualWidth / 2 + X, Canvas.ActualHeight / 2 + Y), dia / 2 * Scale, dia / 2 * Scale);
                geo.AddGeometry(ellipse);
            }
            Path barPath = new Path
            {
                Stroke = NikkenBrush.SkyBlue,
                //Fill = Brushes.DarkBlue,
                StrokeThickness = 1,
                //StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = geo
            };
            Canvas.Children.Add(barPath); // Canvasに追加
        }


        private double ExtractNumber(string input)
        {
            if (input == null)
            {
                return 0;
            }

            // 数字以外の文字を削除してから double に変換
            string numericPart = Regex.Replace(input, "[^0-9.]", "");
            double result;
            if (double.TryParse(numericPart, out result))
            {
                return result;
            }
            // 変換できない場合は例外をスローするか、適切な処理を行う
            throw new ArgumentException("入力文字列に数字が含まれていません。");
        }
    }
}
