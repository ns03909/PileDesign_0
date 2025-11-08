using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;



namespace PileDesign.Models
{
    internal class Draw2D

    {

        public static void PileDraw(Canvas canvas, PileBodyViewModel viewModel)
        {
            if (canvas != null)
            {
                canvas.Children?.Clear();

                // Canvas上に図形を描画するためのサイズと位置を定義します。
                double canvasWidth = canvas.ActualWidth;
                double canvasHeight = canvas.ActualHeight;
                PileBodyInput pileBody = viewModel.PileBodies[viewModel.PileBodyNo - 1];
                if (pileBody.PileBodySegments.Count != 0)
                {
                    double pileLength = pileBody.PileBodySegments[^1].SegmentDepth;
                    double ratio = canvasHeight / pileLength;

                    for (int i = 0; i < pileBody.PileBodySegments.Count; i++)
                    {
                        double _segmentLength = pileBody.PileBodySegments[i].SegmentLength;
                        double _segmentDepth = pileBody.PileBodySegments[i].SegmentDepth;
                        double _pilediameter = 0.0;
                        if (pileBody.PileBodySegments[i].PileSection != null)
                        {
                            _pilediameter = pileBody.PileBodySegments[i].PileSection.PileDiameter / 1000.0;
                        }

                        if (i == 0)
                        {
                            Line horLine0 = new()
                            {
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                                StrokeThickness = 1, // 線の太さ
                                X1 = canvasWidth / 2.0 - 0.1 * ratio,
                                X2 = canvasWidth / 2.0 + 0.1 * ratio,
                                Y1 = 0.0,
                                Y2 = 0.0
                            };
                            // CanvasにLineを追加します
                            canvas.Children.Add(horLine0);
                        }

                        Line horLine = new()
                        {
                            Stroke = NikkenBrush.SkyBlue, // 線の色
                            StrokeThickness = 1, // 線の太さ
                            X1 = canvasWidth / 2.0 - 0.1 * ratio,
                            X2 = canvasWidth / 2.0 + 0.1 * ratio,
                            Y1 = _segmentDepth * ratio,
                            Y2 = _segmentDepth * ratio
                        };
                        // CanvasにLineを追加します
                        canvas.Children.Add(horLine);


                        if (_pilediameter < 0.01) // 杭径が極小の場合
                        {
                            Line vertLine = new()
                            {
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                                StrokeThickness = 1, // 線の太さ
                                X1 = canvasWidth / 2.0,
                                X2 = canvasWidth / 2.0,
                                Y1 = _segmentDepth * ratio,
                                Y2 = (_segmentDepth - _segmentLength) * ratio
                            };
                            // CanvasにLineを追加します
                            canvas.Children.Add(vertLine);
                        }

                        else // 杭径が一般の場合
                        {
                            Rectangle rectangle = new()
                            {
                                Width = _pilediameter * ratio,
                                Height = _segmentLength * ratio,
                                //Fill = Brushes.White,
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                            };
                            Canvas.SetLeft(rectangle, canvasWidth / 2.0 - _pilediameter / 2.0 * ratio); // X座標
                            Canvas.SetTop(rectangle, (_segmentDepth - _segmentLength) * ratio); // Y座標
                            canvas.Children.Add(rectangle); // Canvasに追加
                        }
                    }

                    if (pileBody.PileBodySegments.Count > 0)
                    {
                        int lastIndex = pileBody.PileBodySegments.Count - 1;
                        if (pileBody.PileBodySegments[lastIndex]?.PileSection?.PileDiameter != null)
                        {
                            double _bottomSegmentDia = pileBody.PileBodySegments[lastIndex].PileSection.PileDiameter / 1000.0;
                            double _pileToeDia = pileBody.PileToeDia / 1000.0;
                            if (pileBody.PileConstructionType == "場所打ちコンクリート杭" && _pileToeDia > _bottomSegmentDia)
                            {
                                // ポリラインの各頂点の座標を設定します
                                PointCollection points = [];
                                double _height = (_pileToeDia - _bottomSegmentDia) / 2.0 * Math.Tan(78 * Math.PI / 180);
                                points.Add(new Point(canvasWidth / 2.0 - _bottomSegmentDia / 2.0 * ratio, (pileLength - 0.3 - _height) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, (pileLength - 0.3) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, (pileLength - 0.3) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio, (pileLength - 0.3 - _height) * ratio));
                                // ポリラインを作成します
                                Polygon polygon = new()
                                {
                                    Stroke = NikkenBrush.SkyBlue, // 線の色
                                    Fill = Brushes.White, // 内部の色
                                    StrokeThickness = 1, // 線の太さ
                                    Points = points // ポリラインの頂点座標を設定
                                };
                                // Canvasにポリラインを追加
                                canvas.Children.Add(polygon);

                                {
                                    Line line = new()
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        X1 = canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio,
                                        X2 = canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio,
                                        Y1 = (pileLength - 0.3) * ratio,
                                        Y2 = (pileLength - 0.3) * ratio
                                    };
                                    // CanvasにLineを追加します
                                    canvas.Children.Add(line);
                                }

                                for (int i = -1; i < 2; i += 2)
                                {
                                    Line line = new()
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        StrokeDashArray = new DoubleCollection([2]), // 破線の設定
                                        X1 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        X2 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        Y1 = (pileLength - 0.3 - _height) * ratio,
                                        Y2 = pileLength * ratio
                                    };
                                    // CanvasにLineを追加します
                                    canvas.Children.Add(line);
                                }
                            }

                            if (pileBody.PileConstructionType == "埋込み杭（プレボーリング）" && _pileToeDia > _bottomSegmentDia ||
                                pileBody.PileConstructionType == "埋込み杭（中掘り）" && _pileToeDia > _bottomSegmentDia)
                            {
                                // ポリラインの各頂点の座標を設定します
                                PointCollection points = [];
                                double _height = _pileToeDia * 2;
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, (pileLength - _height) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, (pileLength - _height) * ratio));
                                // ポリラインを作成します
                                Polygon polygon = new()
                                {
                                    Stroke = NikkenBrush.SkyBlue, // 線の色
                                    Fill = Brushes.White, // 内部の色
                                    StrokeThickness = 1, // 線の太さ
                                    Points = points // ポリラインの頂点座標を設定
                                };
                                // Canvasにポリラインを追加
                                canvas.Children.Add(polygon);


                                for (int i = -1; i < 2; i += 2)
                                {
                                    Line line = new()
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        StrokeDashArray = new DoubleCollection([2]), // 破線の設定
                                        X1 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        X2 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        Y1 = (pileLength - _height) * ratio,
                                        Y2 = pileLength * ratio
                                    };
                                    // CanvasにLineを追加します
                                    canvas.Children.Add(line);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
