using PileDesign.Common;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;


namespace PileDesign.ViewModels
{
    internal class PileTopTopViewDrawer(PileTopViewModel _viewModel, Canvas _canvas)
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
            double baseDimension = 1200.0;
            if (ViewModel.PileTopType == "キャプテンパイル工法" && ViewModel.PileTop.CaptainPile.PCRing != null)
            {
                baseDimension = Math.Max(ViewModel.PileTop.CaptainPile.PCRing.RD2 + 150.0, 1200.0);
            }
            else if (ViewModel.PileTopType == "FT-Pile構法" && ViewModel.PileTop.FTPile.FTCap != null)
            {
                baseDimension = Math.Max(ViewModel.PileTop.FTPile.FTCap.D2 + 150.0, 1200.0);
            }
            else if (ViewModel.PileTopType == "キャプリングパイル工法" && ViewModel.PileTop.CapringPile?.PCRing != null)
            {
                baseDimension = Math.Max(ViewModel.PileTop.CapringPile.PCRing.RD2 + 150.0, 1200.0);
            }
            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }


        public void RedrawShapes()
        {
            if (Canvas != null)
            {
                // 描画用パスをクリア
                DrawingGeometry = new PathGeometry();

                Scale = GetScale();

                Canvas.Children.Clear();

                DrawGauge();


                if (ViewModel.PileTopType == "キャプテンパイル工法" && ViewModel.PileTop.CaptainPile.PCRing != null)
                {
                    // 杭
                    double pileconcreteOutDia = ViewModel.PileTop.CaptainPile.PCRing.D;
                    DrawDonut("concrete", pileconcreteOutDia, 0.0);

                    // PCリング
                    double concreteOutDia = ViewModel.PileTop.CaptainPile.PCRing.RD2;
                    double concreteInDia = ViewModel.PileTop.CaptainPile.PCRing.RD1;
                    DrawDonut("concrete", concreteOutDia, concreteInDia);

                    // 鋼リング
                    double steelRingDia = ViewModel.PileTop.CaptainPile.PCRing.RD1 + 2 * ViewModel.PileTop.CaptainPile.PCRing.RingSteelTs;
                    DrawDonut("steelPipe", steelRingDia, concreteInDia);

                    // PCリング引張定着筋
                    int number = ViewModel.PileTop.CaptainPile.PCRing.BarNum;
                    double dia = ExtractNumber(ViewModel.PileTop.CaptainPile.PCRing.BarSize);
                    double pcd = concreteOutDia - 2 * (40.0 + ExtractNumber(ViewModel.PileTop.CaptainPile.PCRing.SpiralDia)) - ExtractNumber(ViewModel.PileTop.CaptainPile.PCRing.BarSize);
                    DrawMainBars(number, dia, pcd);

                    if (ViewModel.PileTop.CaptainPile.CTPTensionRebars.HasTensionRebars == true && ViewModel.PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement == true)
                    {
                        int number1 = ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberCircle;
                        double dia1 = ExtractNumber(ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcd1 = ViewModel.PileTop.CaptainPile.CTPTensionRebars.TDorTB;
                        DrawMainBars(number1, dia1, pcd1);
                    }
                    if (ViewModel.PileTop.CaptainPile.CTPTensionRebars.HasTensionRebars == true && ViewModel.PileTop.CaptainPile.CTPTensionRebars.IsSquareArrangement == true)
                    {
                        int number1 = ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberSquare;
                        double dia1 = ExtractNumber(ViewModel.PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcd1 = ViewModel.PileTop.CaptainPile.CTPTensionRebars.TDorTB;
                        DrawSquareArrangedBars(number1, dia1, pcd1);
                    }

                    // 上面緩衝材
                    if (ViewModel.PileTop.CaptainPile.Nu != 1.00)
                    {
                        double nuDia = ViewModel.PileTop.CaptainPile.PCRing.D * ViewModel.PileTop.CaptainPile.Nu;
                        DrawDonut("concrete", nuDia, 0.0);
                    }

                }

                else if (ViewModel.PileTopType == "FT-Pile構法" && ViewModel.PileTop.FTPile.FTCap != null)
                {
                    //キャップ外径
                    double capOutDia = ViewModel.PileTop.FTPile.FTCap.D2;
                    DrawDonut("steelPipe", capOutDia, 0.0);

                    //杭径
                    double piledia = ViewModel.PileTop.FTPile.FTCap.D1;
                    DrawDonut("dashLine", piledia, 0.0);

                    if (ViewModel.PileTop.FTPile.FTPileTensionBars.IsTensionType == true)
                    {
                        int number1 = ViewModel.PileTop.FTPile.FTPileTensionBars.TensionAnchorNum;
                        double dia1 = 11;
                        double pcd1 = ViewModel.PileTop.FTPile.FTCap.D1 - 150; // need to change
                        DrawMainBars(number1, dia1, pcd1);
                    }
                }

                else if (ViewModel.PileTopType == "キャプリングパイル工法" && ViewModel.PileTop.CapringPile?.PCRing != null)
                {
                    var ring = ViewModel.PileTop.CapringPile.PCRing;

                    // 杭体描画 — 杭種に応じてコンクリート内径・鋼管内径も描画
                    bool isSteelPipe = ViewModel.PileBodyType?.Contains("鋼管杭") ?? false;
                    bool isPrecastConcrete = ViewModel.PileBodyType?.Contains("既製コンクリート杭") ?? false;
                    double pileOuterDia = ring.D;

                    if (isSteelPipe)
                    {
                        // 鋼管杭+キャプリング: 杭頭はコンクリート充填鋼管部
                        double pipeDia = (ViewModel.PileSection != null && ViewModel.PileSection.PipeDia > 0) ? ViewModel.PileSection.PipeDia : pileOuterDia;
                        double pipeTs = ViewModel.PileSection?.PipeTs ?? 0;
                        double pipeInner = Math.Max(0.0, pipeDia - 2 * pipeTs);
                        if (pipeInner > 0)
                            DrawDonut("concrete", pipeInner, 0.0); // 充填コンクリート
                        DrawDonut("steelPipe", pipeDia, pipeInner); // 鋼管 (環)
                    }
                    else if (isPrecastConcrete)
                    {
                        // 既製コンクリート杭 (PHC/PRC/SC): 中空コンクリート (外径〜内径)
                        double concreteThickness = ViewModel.PileTop.ConcreteThickness;
                        double concreteInner = Math.Max(0.0, pileOuterDia - 2 * concreteThickness);
                        DrawDonut("concrete", pileOuterDia, concreteInner);

                        // SC 杭の場合は内側に鋼管あり
                        if (ViewModel.PileSection != null && ViewModel.PileSection.PipeDia > 0 && ViewModel.PileSection.PipeTs > 0)
                        {
                            double pipeInner = Math.Max(0.0, ViewModel.PileSection.PipeDia - 2 * ViewModel.PileSection.PipeTs);
                            DrawDonut("steelPipe", ViewModel.PileSection.PipeDia, pipeInner);
                        }
                    }
                    else
                    {
                        DrawDonut("concrete", pileOuterDia, 0.0);
                    }

                    // PC リング (rd1 内径〜rd2 外径) コンクリート部
                    DrawDonut("concrete", ring.RD2, ring.RD1);

                    // 鋼リング (rd1 + 2*ts、内側鋼管厚)
                    double steelRingDia = ring.RD1 + 2.0 * ring.Ts;
                    DrawDonut("steelPipe", steelRingDia, ring.RD1);

                    // PC リング定着筋 (BarNum 本、BarSize)
                    int barNum = ring.BarNum;
                    double barDia = ExtractNumber(ring.BarSize ?? "");
                    double spiralDia = ExtractNumber(ring.SpiralDia ?? "");
                    double pcd = ring.RD2 - 2.0 * (40.0 + spiralDia + barDia * 0.5);
                    if (pcd > 0 && barNum > 0 && barDia > 0)
                        DrawMainBars(barNum, barDia, pcd);

                    // 引張定着筋 (オプション、円環配置)
                    var capring = ViewModel.PileTop.CapringPile;
                    if (capring.HasTensionBars && capring.TensionBar != null)
                    {
                        var tb = capring.TensionBar;
                        int tNum = tb.BarNum;
                        double tDia = ExtractNumber(tb.BarSize ?? "");
                        double tPcd = tb.Dc;
                        if (tPcd > 0 && tNum > 0 && tDia > 0)
                            DrawMainBars(tNum, tDia, tPcd);
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
        }


        private void DrawGauge()
        {
            // 目盛
            int minor = 100;
            int major = 500;

            LineGeometry line0 = new()
            {
                StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0),
                EndPoint = new Point(50 * Scale, Canvas.ActualHeight / 2.0),
            };
            DrawingGeometry.AddGeometry(line0);
            int i = 1;

            while (i * minor * Scale < Canvas.ActualHeight / 2.0)
            {
                double linelength = i % (major / minor) == 0 ? 50 : 25;
                LineGeometry line1 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line1);

                LineGeometry line2 = new()
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

            if (type == "dashLine")
            {
                donutPath.StrokeDashArray = new DoubleCollection([4, 2]); // 4ピクセルの破線と2ピクセルの間隔
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
                double X = pcd / 2.0 * Math.Cos(i / number * 2 * Math.PI) * Scale;
                double Y = pcd / 2.0 * Math.Sin(i / number * 2 * Math.PI) * Scale;
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

        private void DrawSquareArrangedBars(int number, double dia, double tB)
        {
            PathGeometry geo = new();
            double X;
            double Y;
            EllipseGeometry ellipse;
            for (int i = 0; i < number / 4 + 1; i++)
            {
                for (int j = -1; j < 2; j += 2)
                {
                    X = (-tB * 0.5 + tB * i / (number / 4)) * Scale;
                    Y = -tB * 0.5 * j * Scale;
                    ellipse = new EllipseGeometry(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                    geo.AddGeometry(ellipse);

                    if (i == 0 || i == number / 4)
                    {
                        for (int k = 1; k < number / 4; k++)
                        {
                            X = -tB * 0.5 * j * Scale;
                            Y = (-tB * 0.5 + tB * k / (number / 4)) * Scale;
                            ellipse = new EllipseGeometry(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                            geo.AddGeometry(ellipse);
                        }
                    }
                }
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
