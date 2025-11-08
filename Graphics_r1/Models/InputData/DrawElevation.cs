//using PileDesign.Common;
//using PileDesign.ViewModels;
//using System;
//using System.Collections.ObjectModel;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media;
//using System.Windows.Shapes;

//namespace PileDesign.Models.InputData
//{
//    public class DrawElevaiton
//    {
//        private readonly MainWindowViewModel _mainWindowViewModel;
//        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

//        public Canvas DrawSoilPile(Canvas canvas, SoilPile soilPile)
//        {
//            if (canvas == null)
//            { return canvas; }

//            canvas.Children?.Clear();

//            int pileBodyNo = soilPile.PileBodyNo;
//            double pileTopAltitude = soilPile.Z;
//            int groundNo = soilPile.GroundNo;

//            double ratio = GetRatio(canvas, pileBodyNo, groundNo);

//            DrawPile(canvas, ratio, pileBodyNo, pileTopAltitude);
//            DrawSoil(canvas, ratio, groundNo);

//            return canvas;
//        }

//        public double GetRatio(Canvas canvas, int pileBodyNo, int groundNo)
//        {
//            //InputModel inputModel = InputModel.Instance;
//            if (InputModel == null)
//            { return 999; }

//            PileBodyInput pileBody = InputModel.PileBodies[pileBodyNo];
//            ObservableCollection<PileBodySegment> pileBodySegments = InputModel.PileBodies[pileBodyNo].PileBodySegments;

//            // canvas上に図形を描画するためのサイズと位置を定義します。
//            double canvasWidth = canvas.ActualWidth;
//            double canvasHeight = canvas.ActualHeight;

//            if (pileBodySegments.Count != 0)
//            {
//                double pileLength = pileBodySegments[^1].SegmentDepth;
//                double ratio = canvasHeight / pileLength;
//                return ratio;
//            }
//            return 999;
//        }

//        public Canvas DrawSoil(Canvas canvas, double ratio, int groundNo)
//        {
//            //InputModel inputModel = InputModel.Instance;
//            if (InputModel == null)
//            { return canvas; }

//            //double groundTopAltitude = inputModel.GroundsInput[groundNo].GroundTopAltitude;
//            ObservableCollection<GroundLayerInput> soilLayers = InputModel.GroundsInput[groundNo].GroundLayers;

//            double canvasWidth = canvas.ActualWidth;
//            double canvasHeight = canvas.ActualHeight;

//            if (soilLayers.Count != 0)
//            {

//            }
//            return canvas;
//        }

//        public Canvas DrawPile(Canvas canvas, double ratio, int pileBodyNo, double pileTopAltitude)
//        {
//            //InputModel inputModel = InputModel.Instance;
//            if (InputModel == null)
//            { return canvas; }

//            PileBodyInput pileBody = InputModel.PileBodies[pileBodyNo];
//            ObservableCollection<PileBodySegment> pileBodySegments = InputModel.PileBodies[pileBodyNo].PileBodySegments;

//            // canvas上に図形を描画するためのサイズと位置を定義します。
//            double canvasWidth = canvas.ActualWidth;
//            double canvasHeight = canvas.ActualHeight;

//            if (pileBodySegments.Count != 0)
//            {
//                double pileLength = pileBodySegments[^1].SegmentDepth;
//                //double ratio = canvasHeight / pileLength;

//                for (int i = 0; i < pileBodySegments.Count; i++)
//                {
//                    double _segmentLength = pileBodySegments[i].SegmentLength;
//                    double _segmentDepth = pileBodySegments[i].SegmentDepth;
//                    double _pilediameter = 0.0;
//                    if (pileBodySegments[i].PileSection != null)
//                    {
//                        _pilediameter = pileBodySegments[i].PileSection.PileDiameter / 1000.0;
//                    }

//                    if (_pilediameter < 0.01) // 杭径が極小の場合
//                    {
//                        Line vertLine = new()
//                        {
//                            Stroke = NikkenBrush.SkyBlue, // 線の色
//                            StrokeThickness = 1, // 線の太さ
//                            X1 = canvasWidth * 0.5,
//                            X2 = canvasWidth * 0.5,
//                            Y1 = _segmentDepth * ratio,
//                            Y2 = (_segmentDepth - _segmentLength) * ratio
//                        };
//                        // canvasにLineを追加します
//                        canvas.Children.Add(vertLine);
//                    }
//                    else // 杭径が一般の場合
//                    {
//                        Rectangle rectangle = new()
//                        {
//                            Width = _pilediameter * ratio,
//                            Height = _segmentLength * ratio,
//                            Stroke = NikkenBrush.SkyBlue, // 線の色
//                        };
//                        Canvas.SetLeft(rectangle, canvasWidth * 0.5 - _pilediameter * 0.5 * ratio); // X座標
//                        Canvas.SetTop(rectangle, (_segmentDepth - _segmentLength) * ratio); // Y座標
//                        canvas.Children.Add(rectangle); // canvasに追加
//                    }
//                }

//                if (pileBodySegments.Count > 0)
//                {
//                    int lastIndex = pileBodySegments.Count - 1;
//                    if (pileBodySegments[lastIndex]?.PileSection?.PileDiameter != null)
//                    {
//                        double _bottomSegmentDia = pileBodySegments[lastIndex].PileSection.PileDiameter / 1000.0;
//                        double _pileToeDia = pileBody.PileToeDia / 1000.0;
//                        if (pileBody.PileConstructionType == "場所打ちコンクリート杭" && _pileToeDia > _bottomSegmentDia)
//                        {
//                            PointCollection points = [];
//                            double _height = (_pileToeDia - _bottomSegmentDia) * 0.5 * Math.Tan(78 * Math.PI / 180);
//                            points.Add(new Point(canvasWidth * 0.5 - _bottomSegmentDia * 0.5 * ratio, (pileLength - 0.3 - _height) * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 - _pileToeDia * 0.5 * ratio, (pileLength - 0.3) * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 - _pileToeDia * 0.5 * ratio, pileLength * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 + _pileToeDia * 0.5 * ratio, pileLength * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 + _pileToeDia * 0.5 * ratio, (pileLength - 0.3) * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 + _bottomSegmentDia * 0.5 * ratio, (pileLength - 0.3 - _height) * ratio));
//                            Polygon polygon = new()
//                            {
//                                Stroke = NikkenBrush.SkyBlue, // 線の色
//                                Fill = Brushes.White, // 内部の色
//                                StrokeThickness = 1, // 線の太さ
//                                Points = points // ポリラインの頂点座標を設定
//                            };
//                            canvas.Children.Add(polygon);

//                            Line line = new()
//                            {
//                                Stroke = NikkenBrush.SkyBlue, // 線の色
//                                StrokeThickness = 1, // 線の太さ
//                                X1 = canvasWidth * 0.5 + _pileToeDia * 0.5 * ratio,
//                                X2 = canvasWidth * 0.5 - _pileToeDia * 0.5 * ratio,
//                                Y1 = (pileLength - 0.3) * ratio,
//                                Y2 = (pileLength - 0.3) * ratio
//                            };
//                            canvas.Children.Add(line);

//                            for (int i = -1; i < 2; i += 2)
//                            {
//                                Line dashedLine = new()
//                                {
//                                    Stroke = NikkenBrush.SkyBlue, // 線の色
//                                    StrokeThickness = 1, // 線の太さ
//                                    StrokeDashArray = [2], // 破線の設定
//                                    X1 = canvasWidth * 0.5 + _bottomSegmentDia * 0.5 * ratio * i,
//                                    X2 = canvasWidth * 0.5 + _bottomSegmentDia * 0.5 * ratio * i,
//                                    Y1 = (pileLength - 0.3 - _height) * ratio,
//                                    Y2 = (pileLength) * ratio
//                                };
//                                canvas.Children.Add(dashedLine);
//                            }
//                        }

//                        if ((pileBody.PileConstructionType == "埋込み杭（プレボーリング）" && _pileToeDia > _bottomSegmentDia) ||
//                            (pileBody.PileConstructionType == "埋込み杭（中掘り）" && _pileToeDia > _bottomSegmentDia))
//                        {
//                            PointCollection points = [];
//                            double _height = _pileToeDia * 2;
//                            points.Add(new Point(canvasWidth * 0.5 - _pileToeDia * 0.5 * ratio, (pileLength - _height) * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 - _pileToeDia * 0.5 * ratio, pileLength * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 + _pileToeDia * 0.5 * ratio, pileLength * ratio));
//                            points.Add(new Point(canvasWidth * 0.5 + _pileToeDia * 0.5 * ratio, (pileLength - _height) * ratio));
//                            Polygon polygon = new()
//                            {
//                                Stroke = NikkenBrush.SkyBlue, // 線の色
//                                Fill = Brushes.White, // 内部の色
//                                StrokeThickness = 1, // 線の太さ
//                                Points = points // ポリラインの頂点座標を設定
//                            };
//                            canvas.Children.Add(polygon);

//                            for (int i = -1; i < 2; i += 2)
//                            {
//                                Line dashedLine = new()
//                                {
//                                    Stroke = NikkenBrush.SkyBlue, // 線の色
//                                    StrokeThickness = 1, // 線の太さ
//                                    StrokeDashArray = [2], // 破線の設定
//                                    X1 = canvasWidth * 0.5 + _bottomSegmentDia * 0.5 * ratio * i,
//                                    X2 = canvasWidth * 0.5 + _bottomSegmentDia * 0.5 * ratio * i,
//                                    Y1 = (pileLength - _height) * ratio,
//                                    Y2 = (pileLength) * ratio
//                                };
//                                canvas.Children.Add(dashedLine);
//                            }
//                        }
//                    }
//                }
//            }
//            return canvas;
//        }
//    }
//}