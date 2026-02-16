using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    /// 

    public partial class MainWindow : Window
    {
        private static bool IsEdgeHidden(Vector3D viewVector, Point3D p3a, Point3D p3b, int closestVertexIndex, Point3D[] cubePoints)
        {
            // 元のロジック: 注視点に最も近い頂点を含む辺を陰線とする
            int i = Array.IndexOf(cubePoints, p3a);
            int j = Array.IndexOf(cubePoints, p3b);
            return (i == closestVertexIndex || j == closestVertexIndex);
        }

        // キューブの更新メソッド
        private void UpdateCanvasCube()
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            if (viewModel == null ||
                viewModel.CurrentInputModel == null ||
                viewModel.CurrentInputModel.EmbedmentInput == null ||
                viewModel.CurrentInputModel.EmbedmentInput.EmbedmentLayers == null)
            { return; }
            if (CanvasCube == null)
            {
                CanvasCube = new();
            }
            else
            {
                CanvasCube.Children.Clear();
            }

            double d = 16;　// キューブの寸法

            Point3D[] cubePoints =
            [
                new(d, d, d), new(-d, d, d), new(d, -d, d), new (d, d, -d),
                new (-d, -d, d), new (-d, d, -d), new (d, -d, -d), new (-d, -d, -d)
            ];

            Point[] transformedPoints = [.. cubePoints.Select(p => viewModel.CanvasThreeDView.CubeTransformation(p, CanvasCube))];

            PathGeometry canvasCubeData = new();

            // 注視点ベクトルを取得
            Vector3D viewVector = viewModel.CanvasThreeDView.GetViewVector();

            // 最も注視ベクトルとのなす角が小さい頂点を求める
            int closestVertexIndex = GetClosestVertexIndex(viewVector, cubePoints);

            // 各辺の法線ベクトルを計算し、陰線を判断
            //for (int i = 0; i < cubePoints.Length; i++)
            //{
            //    for (int j = i + 1; j < cubePoints.Length; j++)
            //    {
            //        if (IsEdge(cubePoints[i], cubePoints[j]))
            //        {
            //            bool isHidden = (i == closestVertexIndex || j == closestVertexIndex);
            //            canvasCubeData = AddCubeLine3D(canvasCubeData, transformedPoints[i], transformedPoints[j], isHidden);
            //        }
            //    }
            //}

            for (int i = 0; i < cubePoints.Length; i++)
            {
                for (int j = i + 1; j < cubePoints.Length; j++)
                {
                    if (!IsEdge(cubePoints[i], cubePoints[j])) continue;

                    bool isHidden = IsEdgeHidden(viewVector, cubePoints[i], cubePoints[j], closestVertexIndex, cubePoints);
                    canvasCubeData = AddCubeLine3D(canvasCubeData, transformedPoints[i], transformedPoints[j], isHidden);
                }
            }

            // Z軸周り円環の描画（キューブエッジ描画後）
            DrawZAxisRing(viewVector, d);

            // 各面の中心に文字を描画
            DrawFaceLabels(viewVector, cubePoints, transformedPoints, d);

            // 既存 UpdateCanvasCube の末尾近くにこの1行を追加（面ラベル描画の後）
            BuildCubeHitItems(viewVector, cubePoints, transformedPoints, d);
        }


        /// <summary>
        /// Z軸回りの帯状円環。地震荷重ケース選択時は荷重角度方向線を描画。
        /// 継ぎ目の放射線が出ないよう、塗りと枠線を分離。
        /// </summary>
        private void DrawZAxisRing(Vector3D viewVector, double d)
        {
            if (DataContext is not MainWindowViewModel vm || CanvasCube == null) return;

            // 円環寸法（調整可）
            double ringOuterR = d * 2.0;   // 外径
            double ringInnerR = d * 1.6;   // 内径
            int segments = 128;

            // 0..segments-1（重複点を作らない）
            var outerPts = new List<Point>(segments);
            var innerPts = new List<Point>(segments);
            for (int k = 0; k < segments; k++)
            {
                double ang = 2.0 * Math.PI * k / segments;
                var p3Outer = new Point3D(ringOuterR * Math.Cos(ang), ringOuterR * Math.Sin(ang), 0.0);
                var p3Inner = new Point3D(ringInnerR * Math.Cos(ang), ringInnerR * Math.Sin(ang), 0.0);
                outerPts.Add(vm.CanvasThreeDView.CubeTransformation(p3Outer, CanvasCube));
                innerPts.Add(vm.CanvasThreeDView.CubeTransformation(p3Inner, CanvasCube));
            }

            // 共通: Figure を組み立てるヘルパ
            static PathFigure BuildClosedFigure(IReadOnlyList<Point> pts)
            {
                var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
                fig.Segments.Add(new PolyLineSegment([.. pts.Skip(1)], true));
                return fig;
            }

            // 塗り用 PathGeometry（外周・内周を別 Figure、EvenOdd で穴にする）
            var fillGeom = new PathGeometry { FillRule = FillRule.EvenOdd };
            fillGeom.Figures.Add(BuildClosedFigure(outerPts));
            fillGeom.Figures.Add(BuildClosedFigure(innerPts));

            var fillPath = new Path
            {
                Data = fillGeom,
                Fill = new SolidColorBrush(Color.FromArgb(55, 100, 140, 200)), // 半透明塗り
                Stroke = null,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(fillPath, 30);
            CanvasCube.Children.Add(fillPath);

            // 外周枠線
            var outerGeom = new PathGeometry();
            outerGeom.Figures.Add(BuildClosedFigure(outerPts));
            var outerStrokePath = new Path
            {
                Data = outerGeom,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 80, 120, 170)),
                StrokeThickness = 1.2,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(outerStrokePath, 31);
            CanvasCube.Children.Add(outerStrokePath);

            // 内周枠線
            var innerGeom = new PathGeometry();
            innerGeom.Figures.Add(BuildClosedFigure(innerPts));
            var innerStrokePath = new Path
            {
                Data = innerGeom,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 80, 120, 170)),
                StrokeThickness = 1.2,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(innerStrokePath, 31);
            CanvasCube.Children.Add(innerStrokePath);

            // 地震荷重方向表示（必要なら角度基準に合わせて±補正）
            bool isSeismic = vm.CurrentInputModel.LoadCasesInput.AllSeismicLoadCases
                                .Any(lc => lc.LoadName == vm.SelectedLoadCaseName);
            if (!isSeismic) return;

            double deg = vm.SelectedDirection + 180.0; // 既存仕様：反転＋180°
            double rad = deg * Math.PI / 180.0;

            // 3D上 (XY平面 Z=0) の外径側=底辺中心, 内径側=頂点
            var p3A = new Point3D(ringOuterR * Math.Cos(rad), ringOuterR * Math.Sin(rad), 0.0); // 底辺中心
            var p3B = new Point3D(ringInnerR * Math.Cos(rad), ringInnerR * Math.Sin(rad), 0.0); // 頂点

            // 高さベクトル (3D)
            Vector3D hVec3 = p3B - p3A;
            double h = hVec3.Length;
            double minHeight = 8.0; // 投影後も形状が視認できる最低高さ
            if (h < minHeight)
            {
                if (h > 1e-6)
                {
                    hVec3.Normalize();
                    p3B = p3A + hVec3 * minHeight;
                    hVec3 = p3B - p3A;
                    h = minHeight;
                }
                else
                {
                    return; // 退避
                }
            }

            // 正三角形底辺長
            double s = 2.0 * h / Math.Sqrt(3.0);

            // 法線ベクトル(-dy, dx, 0) を正規化（XY平面内）
            Vector3D n3 = new(-hVec3.Y, hVec3.X, 0.0);
            double nLen = n3.Length;
            if (nLen < 1e-6) return;
            n3 /= nLen;

            // 底辺端点 (3D)
            var p3Base1 = p3A + n3 * (s * 0.5);
            var p3Base2 = p3A - n3 * (s * 0.5);

            // 2D変換
            Point pA = vm.CanvasThreeDView.CubeTransformation(p3A, CanvasCube);
            Point pB = vm.CanvasThreeDView.CubeTransformation(p3B, CanvasCube);
            Point pBase1 = vm.CanvasThreeDView.CubeTransformation(p3Base1, CanvasCube);
            Point pBase2 = vm.CanvasThreeDView.CubeTransformation(p3Base2, CanvasCube);

            // 三角形 PathFigure (底辺両端→頂点)
            var triFig = new PathFigure
            {
                StartPoint = pBase1,
                IsClosed = true,
                IsFilled = true
            };
            triFig.Segments.Add(new PolyLineSegment([.. new[] { pBase2, pB }], true));

            var triGeom = new PathGeometry([triFig]);

            var trianglePath = new Path
            {
                Data = triGeom,
                Fill = new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)), // 半透明オレンジ
                Stroke = Brushes.OrangeRed,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(trianglePath, 65);
            CanvasCube.Children.Add(trianglePath);
        }

        // 最寄りの節点のindexを返すメソッド
        private static int GetClosestVertexIndex(Vector3D viewVector, Point3D[] cubePoints)
        {
            double minAngle = double.MaxValue;
            int closestVertexIndex = -1;

            for (int i = 0; i < cubePoints.Length; i++)
            {
                Vector3D vertexVector = new(cubePoints[i].X, cubePoints[i].Y, cubePoints[i].Z);
                double angle = Vector3D.AngleBetween(-viewVector, vertexVector);

                if (angle < minAngle)
                {
                    minAngle = angle;
                    closestVertexIndex = i;
                }
            }
            return closestVertexIndex;
        }

        // 
        private static bool IsEdge(Point3D point1, Point3D point2)
        {
            int differentCoordinates = 0;

            if (point1.X != point2.X) differentCoordinates++;
            if (point1.Y != point2.Y) differentCoordinates++;
            if (point1.Z != point2.Z) differentCoordinates++;

            return differentCoordinates == 1;
        }

        // キューブの辺を追加するメソッド
        private PathGeometry AddCubeLine3D(PathGeometry canvasCubeData, Point point1, Point point2, bool isHidden)
        {
            LineGeometry lineGeometry = new(point1, point2);
            canvasCubeData.AddGeometry(lineGeometry);

            if (isHidden)
            {
                Path path = new()
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5,
                    Data = new PathGeometry([new PathFigure(point1, [new LineSegment(point2, true)], false)]),
                    StrokeDashArray = [4, 4] // 破線
                };
                CanvasCube.Children.Add(path);
            }
            else
            {
                Path path = new()
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Data = new PathGeometry([new PathFigure(point1, [new LineSegment(point2, true)], false)])
                };
                CanvasCube.Children.Add(path);
            }

            return canvasCubeData;
        }

        // 面ラベルの描画メソッド
        private void DrawFaceLabels(Vector3D viewVector, Point3D[] cubePoints, Point[] transformedPoints, double d)
        {
            var faceCenters = new (Point3D center, string label, Brush color)[]
            {
                (new Point3D(d, 0, 0), "+X", Brushes.Red),
                (new Point3D(-d, 0, 0), "-X", Brushes.DarkRed),
                (new Point3D(0, d, 0), "+Y", Brushes.Green),
                (new Point3D(0, -d, 0), "-Y", Brushes.DarkGreen),
                (new Point3D(0, 0, d), "+Z", Brushes.Blue),
                (new Point3D(0, 0, -d), "-Z", Brushes.DarkBlue)
            };

            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;

            foreach (var (center, label, color) in faceCenters)
            {
                Vector3D faceVector = new(center.X, center.Y, center.Z);
                double dotProduct = Vector3D.DotProduct(viewVector, faceVector);

                if (dotProduct > 0) // 面が隠れていない場合
                {
                    Point transformedCenter = viewModel.CanvasThreeDView.CubeTransformation(center, CanvasCube);
                    DrawText(label, transformedCenter, color);
                }
            }
        }

        // テキストの描画メソッド
        private void DrawText(string text, Point position, Brush color)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = 12,
                Foreground = color
            };

            // テキストの幅と高さを測定するために、TextBlockを一時的にCanvasに追加
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size textSize = textBlock.DesiredSize;

            // テキストの中心を位置に合わせる
            double finalX = position.X - textSize.Width / 2;
            double finalY = position.Y - textSize.Height / 2;

            // シアー変形を適用
            //double shearX = faceVector.Y / faceVector.Z;
            //double shearY = faceVector.X / faceVector.Z;
            //TransformGroup transformGroup = new();
            //transformGroup.Children.Add(new TranslateTransform(finalX, finalY));
            //transformGroup.Children.Add(new SkewTransform(shearX * 45, shearY * 45)); // 45度のシアー変形を適用
            //textBlock.RenderTransform = transformGroup;

            Canvas.SetLeft(textBlock, finalX);
            Canvas.SetTop(textBlock, finalY);
            CanvasCube.Children.Add(textBlock);
        }

        private CancellationTokenSource? _viewAnimationCts;

        // ビューキューブ ドラッグ回転用フィールド
        private bool _cubeDragging;
        private Point _cubeDragStart;
        private double _cubeDragStartTht;
        private double _cubeDragStartPhi;
        private const double CubeDragThreshold = 4.0; // これ以上動いたらドラッグとみなす
        private bool _cubeDragMoved;

        // CanvasCube イベント
        private void CanvasCube_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cubeDragStart = e.GetPosition(CanvasCube);
            _cubeDragMoved = false;

            if (DataContext is MainWindowViewModel vm)
            {
                _cubeDragStartTht = vm.CanvasThreeDView.Tht;
                _cubeDragStartPhi = vm.CanvasThreeDView.Phi;
            }

            _cubeDragging = true;
            CanvasCube.CaptureMouse();
            e.Handled = true;
        }

        private void CanvasCube_MouseMove(object sender, MouseEventArgs e)
        {
            if (_cubeDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(CanvasCube);
                var delta = pos - _cubeDragStart;

                if (!_cubeDragMoved && (Math.Abs(delta.X) > CubeDragThreshold || Math.Abs(delta.Y) > CubeDragThreshold))
                    _cubeDragMoved = true;

                if (_cubeDragMoved && DataContext is MainWindowViewModel vm)
                {
                    // 水平方向のマウス移動 → Tht（左右回転）、垂直方向 → Phi（上下回転）
                    double sensitivity = 1.5;
                    double newTht = _cubeDragStartTht - delta.X * sensitivity;
                    double newPhi = _cubeDragStartPhi + delta.Y * sensitivity;
                    newPhi = Math.Clamp(newPhi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);

                    vm.CanvasThreeDView.Tht = newTht;
                    vm.CanvasThreeDView.Phi = newPhi;
                    UpdateCanvas3D();
                    Mouse.OverrideCursor = Cursors.SizeAll;
                }
                return;
            }

            // ホバー処理（ドラッグ中でない場合）
            var hoverPos = e.GetPosition(CanvasCube);
            var prev = _cubeHoverItem;
            _cubeHoverItem = HitTestCube(hoverPos);
            Mouse.OverrideCursor = _cubeHoverItem != null ? Cursors.Hand : null;

            if (!Equals(prev, _cubeHoverItem))
                DrawCubeHighlight();
        }

        private void CanvasCube_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_cubeDragging)
            {
                _cubeHoverItem = null;
                Mouse.OverrideCursor = null;
                DrawCubeHighlight();
            }
        }

        private void CanvasCube_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool wasDragging = _cubeDragging && _cubeDragMoved;

            if (_cubeDragging)
            {
                _cubeDragging = false;
                CanvasCube.ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
            }

            // ドラッグ操作だった場合はクリック処理をスキップ
            if (wasDragging) return;

            if (_cubeHoverItem == null) return;

            // 1) プリセット角度が決まるならそれを使う
            if (TryGetPresetAngles(_cubeHoverItem, out double tht, out double phi))
            {
                // 安全クランプしてアニメーション
                phi = Math.Clamp(phi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);
                _viewAnimationCts?.Cancel();
                _viewAnimationCts = new CancellationTokenSource();
                _ = AnimateViewToAsync(tht, phi, 350, _viewAnimationCts.Token);
                return;
            }

            // 2) フォールバック（従来の方向ベクトル推定）
            OrientViewToAnimated(_cubeHoverItem.Direction);
        }

        // クリック対象から Tht(=phi表のphi) / Phi(=phi表のtheta) を決める
        // true: マッピング成功
        private static bool TryGetPresetAngles(CubeItem item, out double tht, out double phi)
        {
            tht = 0; phi = 0;

            static int Sgn(double v) => v > 0 ? +1 : v < 0 ? -1 : 0;

            switch (item.Type)
            {
                case CubePartType.Face:
                    {
                        // 法線ベクトルの符号で判定
                        var n = item.Direction;
                        int sx = Sgn(n.X), sy = Sgn(n.Y), sz = Sgn(n.Z);

                        // 面
                        if (sx == +1 && sy == 0 && sz == 0) { tht = 0; phi = 0; return true; }   // +X
                        if (sx == -1 && sy == 0 && sz == 0) { tht = 180; phi = 0; return true; }   // -X
                        if (sx == 0 && sy == +1 && sz == 0) { tht = 90; phi = 0; return true; }   // +Y
                        if (sx == 0 && sy == -1 && sz == 0) { tht = 270; phi = 0; return true; }   // -Y
                        if (sx == 0 && sy == 0 && sz == +1) { tht = -90; phi = 90; return true; }   // +Z
                        if (sx == 0 && sy == 0 && sz == -1) { tht = -90; phi = -90; return true; }   // -Z
                        break;
                    }

                case CubePartType.Edge:
                    {
                        // 辺の中心で2軸が±d、1軸が0
                        var c = new Point3D(
                            (item.Points3D[0].X + item.Points3D[1].X) * 0.5,
                            (item.Points3D[0].Y + item.Points3D[1].Y) * 0.5,
                            (item.Points3D[0].Z + item.Points3D[1].Z) * 0.5);

                        int sx = Sgn(c.X), sy = Sgn(c.Y), sz = Sgn(c.Z);

                        // XY エッジ（Z=0）
                        if (sz == 0)
                        {
                            if (sx == +1 && sy == +1) { tht = 45; phi = 0; return true; } // (+X,+Y)
                            if (sx == +1 && sy == -1) { tht = 315; phi = 0; return true; } // (+X,-Y)
                            if (sx == -1 && sy == +1) { tht = 135; phi = 0; return true; } // (-X,+Y)
                            if (sx == -1 && sy == -1) { tht = 225; phi = 0; return true; } // (-X,-Y)
                        }

                        // ZX エッジ（Y=0） 注: テーブルは(+Z,±X),(-Z,±X)
                        if (sy == 0)
                        {
                            if (sz == +1 && sx == +1) { tht = 0; phi = 45; return true; } // (+Z,+X)
                            if (sz == +1 && sx == -1) { tht = 180; phi = 45; return true; } // (+Z,-X)
                            if (sz == -1 && sx == +1) { tht = 0; phi = -45; return true; } // (-Z,+X)
                            if (sz == -1 && sx == -1) { tht = 180; phi = -45; return true; } // (-Z,-X)
                        }

                        // YZ エッジ（X=0） 注: テーブルは(+Y,±Z),(-Y,±Z)
                        if (sx == 0)
                        {
                            if (sy == +1 && sz == +1) { tht = 90; phi = 45; return true; } // (+Y,+Z)
                            if (sy == +1 && sz == -1) { tht = 90; phi = -45; return true; } // (+Y,-Z)
                            if (sy == -1 && sz == +1) { tht = 270; phi = 45; return true; } // (-Y,+Z)
                            if (sy == -1 && sz == -1) { tht = 270; phi = -45; return true; } // (-Y,-Z)
                        }
                        break;
                    }

                case CubePartType.Vertex:
                    {
                        var p = item.Points3D[0];
                        int sx = Sgn(p.X), sy = Sgn(p.Y), sz = Sgn(p.Z);

                        // 頂点
                        if (sx == +1 && sy == +1 && sz == +1) { tht = 45; phi = Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (+X,+Y,+Z)
                        if (sx == +1 && sy == +1 && sz == -1) { tht = 45; phi = -Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (+X,+Y,-Z)
                        if (sx == +1 && sy == -1 && sz == +1) { tht = 315; phi = Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (+X,-Y,+Z)
                        if (sx == +1 && sy == -1 && sz == -1) { tht = 315; phi = -Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (+X,-Y,-Z)
                        if (sx == -1 && sy == +1 && sz == +1) { tht = 135; phi = Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (-X,+Y,+Z)
                        if (sx == -1 && sy == +1 && sz == -1) { tht = 135; phi = -Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (-X,+Y,-Z)
                        if (sx == -1 && sy == -1 && sz == +1) { tht = 225; phi = Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (-X,-Y,+Z)
                        if (sx == -1 && sy == -1 && sz == -1) { tht = 225; phi = -Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI; return true; } // (-X,-Y,-Z)
                        break;
                    }
            }

            return false;
        }

        // 角度正規化（0..360）
        private static double NormalizeAngle360(double deg)
        {
            deg %= 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }

        private static double DeltaAngle(double fromDeg, double toDeg)
        {
            double d = ((toDeg - fromDeg + 540.0) % 360.0) - 180.0;
            return d;
        }

        private static double LerpAngle(double fromDeg, double toDeg, double t01)
        {
            return fromDeg + DeltaAngle(fromDeg, toDeg) * t01;
        }

        private static double EaseInOutCubic(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        private async Task AnimateViewToAsync(double targetTht, double targetPhi, int durationMs, CancellationToken token)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            double startTht = vm.CanvasThreeDView.Tht;
            double startPhi = vm.CanvasThreeDView.Phi;

            // Phiの目標を安全範囲に
            targetPhi = Math.Clamp(targetPhi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);

            bool prevLightweight = isLightweightDrawing;
            isLightweightDrawing = true;

            var sw = Stopwatch.StartNew();
            const int fps = 60;
            var frameDelay = TimeSpan.FromMilliseconds(1000.0 / fps);

            try
            {
                while (sw.ElapsedMilliseconds < durationMs && !token.IsCancellationRequested)
                {
                    double t = EaseInOutCubic(sw.ElapsedMilliseconds / (double)durationMs);

                    vm.CanvasThreeDView.Tht = LerpAngle(startTht, targetTht, t);
                    // Phiはラップさせる必要が小さいので通常の補間で十分
                    vm.CanvasThreeDView.Phi = startPhi + (targetPhi - startPhi) * t;

                    UpdateCanvas3D();
                    await Task.Delay(frameDelay, token).ConfigureAwait(true);
                }
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
            finally
            {
                // 最終値をセット
                vm.CanvasThreeDView.Tht = targetTht;
                vm.CanvasThreeDView.Phi = targetPhi;

                isLightweightDrawing = prevLightweight;
                UpdateCanvas3D();
            }
        }

        private static (double tht, double phi) AnglesFromDirection(Vector3D dir)
        {
            if (dir.Length == 0) return (0.0, 0.0);
            dir.Normalize();
            double tht = -Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;
            double phi = Math.Atan2(dir.Z, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y)) * 180.0 / Math.PI;
            return (tht, phi);
        }


        // 実 CanvasThreeDView を使った「軽量な局所探索」で targetDir と最も整合する (Tht, Phi) を求める
        private (double tht, double phi) PickAnglesBestAlignedUsingCanvas(Vector3D targetDir)
        {
            if (DataContext is not MainWindowViewModel vm) return (0.0, 0.0);
            if (targetDir.Length == 0) return (vm.CanvasThreeDView.Tht, vm.CanvasThreeDView.Phi);

            targetDir.Normalize();

            // 角度を一時的に適用して GetViewVector を評価（状態は都度戻す）
            double Eval(double th, double ph)
            {
                double thSaved = vm.CanvasThreeDView.Tht;
                double phSaved = vm.CanvasThreeDView.Phi;

                vm.CanvasThreeDView.Tht = th;
                vm.CanvasThreeDView.Phi = Math.Clamp(ph, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);
                Vector3D v = vm.CanvasThreeDView.GetViewVector();

                vm.CanvasThreeDView.Tht = thSaved;
                vm.CanvasThreeDView.Phi = phSaved;

                return Vector3D.DotProduct(v, targetDir);
            }

            static (double th, double ph) ClampAngles((double th, double ph) a)
                => (NormalizeAngle360(a.th), Math.Clamp(a.ph, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle));

            // 解析解（±dir）を初期値に
            var a = ClampAngles(AnglesFromDirection(targetDir));
            var b = ClampAngles(AnglesFromDirection(-targetDir));

            // 候補を少ステップで局所探索（ヒルクライム）
            (double th, double ph) Refine((double th, double ph) start)
            {
                double bestTh = start.th, bestPh = start.ph, best = Eval(bestTh, bestPh);

                double stepTh = 18.0, stepPh = 9.0;
                for (int iter = 0; iter < 3; iter++)
                {
                    bool improved = false;
                    for (int dth = -1; dth <= 1; dth++)
                    {
                        for (int dph = -1; dph <= 1; dph++)
                        {
                            if (dth == 0 && dph == 0) continue;
                            double thTry = NormalizeAngle360(bestTh + dth * stepTh);
                            double phTry = Math.Clamp(bestPh + dph * stepPh, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);

                            double val = Eval(thTry, phTry);
                            if (val > best)
                            {
                                best = val;
                                bestTh = thTry;
                                bestPh = phTry;
                                improved = true;
                            }
                        }
                    }
                    if (!improved)
                    {
                        stepTh *= 0.5;
                        stepPh *= 0.5;
                    }
                }
                return (bestTh, bestPh);
            }

            var a2 = Refine(a);
            var b2 = Refine(b);

            // 最終選択
            double da = Eval(a2.th, a2.ph);
            double db = Eval(b2.th, b2.ph);
            return da >= db ? a2 : b2;
        }

        private async void OrientViewToAnimated(Vector3D dir)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (dir.Length == 0) return;

            var (tht, phi) = PickAnglesBestAlignedUsingCanvas(dir);

            _viewAnimationCts?.Cancel();
            _viewAnimationCts = new CancellationTokenSource();

            // 回転量に応じて時間を可変（体感高速化）
            double angDelta = Math.Max(Math.Abs(DeltaAngle(vm.CanvasThreeDView.Tht, tht)), Math.Abs(phi - vm.CanvasThreeDView.Phi));
            int duration = (int)Math.Clamp(150 + angDelta * 4.0, 220, 700);

            await AnimateViewToAsync(tht, Math.Clamp(phi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle), duration, _viewAnimationCts.Token);
        }

        // MainWindow クラス内（フィールド群）
        private enum CubePartType { Face, Edge, Vertex }

        private sealed class CubeItem
        {
            public CubePartType Type { get; init; }
            public int Index { get; init; }
            public Point[] Points2D { get; init; } = [];
            public Point3D[] Points3D { get; init; } = [];
            public Vector3D Direction { get; init; } // この部位に向かう方向（ビュー設定用）
            public double Depth { get; init; }       // 大きいほど手前（viewVectorとの内積）
            public bool IsHidden { get; init; } // ←追加
        }

        private readonly List<CubeItem> _cubeHitItems = [];
        private CubeItem? _cubeHoverItem;
        private const double CubeVertexHitRadius = 8.0;
        private const double CubeEdgeHitThreshold = 6.0;
        private Shape? _cubeHighlightShape;

        // 既存 UpdateCanvasCube に追記（末尾の面ラベル描画後あたりに呼び出し）
        private void BuildCubeHitItems(Vector3D viewVector, Point3D[] cubePoints, Point[] transformedPoints, double d)
        {
            _cubeHitItems.Clear();

            // 面定義（インデックスは cubePoints の並びに合わせる）
            int[][] faces =
            [
                [0, 2, 6, 3], // +X
                [1, 5, 7, 4], // -X
                [0, 1, 5, 3], // +Y
                [2, 4, 7, 6], // -Y
                [0, 2, 4, 1], // +Z
                [3, 6, 7, 5], // -Z
            ];

            Vector3D[] faceNormals =
            [
                new(+1,0,0),  // +X
                new(-1,0,0),  // -X
                new(0,+1,0),  // +Y
                new(0,-1,0),  // -Y
                new(0,0,+1),  // +Z
                new(0,0,-1),  // -Z
            ];

            const double hitEpsilon = 0.1; // 0.1程度（調整可）

            // 面（手前のみ）
            for (int fi = 0; fi < faces.Length; fi++)
            {
                var idx = faces[fi];
                var poly3D = idx.Select(i => cubePoints[i]).ToArray();
                var poly2D = idx.Select(i => transformedPoints[i]).ToArray();

                var c3 = new Point3D(poly3D.Average(p => p.X), poly3D.Average(p => p.Y), poly3D.Average(p => p.Z));
                var centerVec = new Vector3D(c3.X, c3.Y, c3.Z);
                if (Vector3D.DotProduct(viewVector, faceNormals[fi]) <= -hitEpsilon) continue; // 裏面は除外

                _cubeHitItems.Add(new CubeItem
                {
                    Type = CubePartType.Face,
                    Index = fi,
                    Points2D = poly2D,
                    Points3D = poly3D,
                    Direction = faceNormals[fi],
                    Depth = Vector3D.DotProduct(viewVector, centerVec)
                });
            }

            // 辺（すべて追加、可視性フラグを付与）
            //for (int i = 0; i < cubePoints.Length; i++)
            //{
            //    for (int j = i + 1; j < cubePoints.Length; j++)
            //    {
            //        if (!IsEdge(cubePoints[i], cubePoints[j])) continue;

            //        var p3a = cubePoints[i];
            //        var p3b = cubePoints[j];
            //        var p2a = transformedPoints[i];
            //        var p2b = transformedPoints[j];

            //        var center = new Point3D((p3a.X + p3b.X) * 0.5, (p3a.Y + p3b.Y) * 0.5, (p3a.Z + p3b.Z) * 0.5);
            //        var cvec = new Vector3D(center.X, center.Y, center.Z);

            //        // 可視性判定（描画ロジックと合わせること）
            //        bool isHidden = Vector3D.DotProduct(viewVector, cvec) < 0;

            //        var dir = cvec;
            //        if (dir.Length > 0) dir.Normalize();

            //        _cubeHitItems.Add(new CubeItem
            //        {
            //            Type = CubePartType.Edge,
            //            Index = _cubeHitItems.Count,
            //            Points2D = [p2a, p2b],
            //            Points3D = [p3a, p3b],
            //            Direction = dir,
            //            Depth = Vector3D.DotProduct(viewVector, cvec),
            //            IsHidden = isHidden
            //        });
            //    }
            //}
            for (int i = 0; i < cubePoints.Length; i++)
            {
                for (int j = i + 1; j < cubePoints.Length; j++)
                {
                    if (!IsEdge(cubePoints[i], cubePoints[j])) continue;

                    var p3a = cubePoints[i];
                    var p3b = cubePoints[j];
                    var p2a = transformedPoints[i];
                    var p2b = transformedPoints[j];

                    bool isHidden = IsEdgeHidden(viewVector, p3a, p3b, GetClosestVertexIndex(viewVector, cubePoints), cubePoints);

                    var center = new Point3D((p3a.X + p3b.X) * 0.5, (p3a.Y + p3b.Y) * 0.5, (p3a.Z + p3b.Z) * 0.5);
                    var cvec = new Vector3D(center.X, center.Y, center.Z);
                    var dir = cvec;
                    if (dir.Length > 0) dir.Normalize();

                    _cubeHitItems.Add(new CubeItem
                    {
                        Type = CubePartType.Edge,
                        Index = _cubeHitItems.Count,
                        Points2D = [p2a, p2b],
                        Points3D = [p3a, p3b],
                        Direction = dir,
                        Depth = Vector3D.DotProduct(viewVector, cvec),
                        IsHidden = isHidden
                    });
                }
            }
            // 頂点（少なくとも1本が実線なら選択可能）
            //for (int i = 0; i < cubePoints.Length; i++)
            //{
            //    var p3 = cubePoints[i];
            //    var p2 = transformedPoints[i];
            //    var v = new Vector3D(p3.X, p3.Y, p3.Z);

            //    int visibleEdgeCount = 0;
            //    for (int j = 0; j < cubePoints.Length; j++)
            //    {
            //        if (i == j) continue;
            //        if (!IsEdge(cubePoints[i], cubePoints[j])) continue;

            //        var center = new Point3D((p3.X + cubePoints[j].X) * 0.5, (p3.Y + cubePoints[j].Y) * 0.5, (p3.Z + cubePoints[j].Z) * 0.5);
            //        var cvec = new Vector3D(center.X, center.Y, center.Z);
            //        bool isHidden = Vector3D.DotProduct(viewVector, cvec) < 0;
            //        if (!isHidden) visibleEdgeCount++;
            //    }
            //    // 少なくとも1本が実線なら選択可能
            //    bool isHiddenVertex = (visibleEdgeCount == 0);

            //    var dir = v;
            //    if (dir.Length > 0) dir.Normalize();

            //    _cubeHitItems.Add(new CubeItem
            //    {
            //        Type = CubePartType.Vertex,
            //        Index = i,
            //        Points2D = [p2],
            //        Points3D = [p3],
            //        Direction = dir,
            //        Depth = Vector3D.DotProduct(viewVector, v),
            //        IsHidden = isHiddenVertex
            //    });
            //}
            for (int i = 0; i < cubePoints.Length; i++)
            {
                var p3 = cubePoints[i];
                var p2 = transformedPoints[i];
                var v = new Vector3D(p3.X, p3.Y, p3.Z);

                int visibleEdgeCount = 0;
                int closestVertexIndex = GetClosestVertexIndex(viewVector, cubePoints);
                for (int j = 0; j < cubePoints.Length; j++)
                {
                    if (i == j) continue;
                    if (!IsEdge(cubePoints[i], cubePoints[j])) continue;
                    if (!IsEdgeHidden(viewVector, cubePoints[i], cubePoints[j], closestVertexIndex, cubePoints)) visibleEdgeCount++;
                }
                bool isHiddenVertex = (visibleEdgeCount == 0);

                var dir = v;
                if (dir.Length > 0) dir.Normalize();

                _cubeHitItems.Add(new CubeItem
                {
                    Type = CubePartType.Vertex,
                    Index = i,
                    Points2D = [p2],
                    Points3D = [p3],
                    Direction = dir,
                    Depth = Vector3D.DotProduct(viewVector, v),
                    IsHidden = isHiddenVertex
                });
            }
            _cubeHitItems.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        }

        private CubeItem? HitTestCube(Point pos)
        {
            // 1) 頂点（可視のみ）
            foreach (var it in _cubeHitItems.Where(x => x.Type == CubePartType.Vertex && !x.IsHidden))
            {
                var p = it.Points2D[0];
                if ((p - pos).Length <= CubeVertexHitRadius)
                    return it;
            }

            // 2) 辺（可視のみ）
            foreach (var it in _cubeHitItems.Where(x => x.Type == CubePartType.Edge && !x.IsHidden))
            {
                var p1 = it.Points2D[0];
                var p2 = it.Points2D[1];
                if (DistancePointToSegment(pos, p1, p2) <= CubeEdgeHitThreshold)
                    return it;
            }

            // 3) 面（現状は可視面のみ追加されているので変更不要）
            foreach (var it in _cubeHitItems.Where(x => x.Type == CubePartType.Face))
            {
                if (IsPointInPolygon(pos, it.Points2D))
                    return it;
            }

            return null;
        }

        private static double DistancePointToSegment(Point p, Point a, Point b)
        {
            var ab = b - a;
            var ap = p - a;
            double ab2 = ab.X * ab.X + ab.Y * ab.Y;
            if (ab2 <= double.Epsilon) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, (ap.X * ab.X + ap.Y * ab.Y) / ab2));
            var proj = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            return (p - proj).Length;
        }

        private static bool IsPointInPolygon(Point p, Point[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                bool intersect = ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                                 (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private void DrawCubeHighlight()
        {
            if (CanvasCube == null) return;

            if (_cubeHighlightShape != null)
            {
                CanvasCube.Children.Remove(_cubeHighlightShape);
                _cubeHighlightShape = null;
            }
            if (_cubeHoverItem == null) return;

            switch (_cubeHoverItem.Type)
            {
                case CubePartType.Face:
                    {
                        var pg = new PathGeometry();
                        var fig = new PathFigure { StartPoint = _cubeHoverItem.Points2D[0], IsClosed = true, IsFilled = true };
                        fig.Segments.Add(new PolyLineSegment([.. _cubeHoverItem.Points2D.Skip(1)], true));
                        pg.Figures.Add(fig);
                        var path = new Path
                        {
                            Data = pg,
                            Fill = new SolidColorBrush(Color.FromArgb(60, 98, 176, 226)), // SkyBlue 半透明
                            Stroke = Brushes.SkyBlue,
                            StrokeThickness = 1.5,
                            IsHitTestVisible = false
                        };
                        Panel.SetZIndex(path, 100);
                        CanvasCube.Children.Add(path);
                        _cubeHighlightShape = path;
                        break;
                    }
                case CubePartType.Edge:
                    {
                        var geom = new LineGeometry(_cubeHoverItem.Points2D[0], _cubeHoverItem.Points2D[1]);
                        var path = new Path
                        {
                            Data = geom,
                            Stroke = Brushes.SkyBlue,
                            StrokeThickness = 3.0,
                            StrokeDashCap = PenLineCap.Round,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round,
                            IsHitTestVisible = false
                        };
                        Panel.SetZIndex(path, 100);
                        CanvasCube.Children.Add(path);
                        _cubeHighlightShape = path;
                        break;
                    }
                case CubePartType.Vertex:
                    {
                        var p = _cubeHoverItem.Points2D[0];
                        var ellipse = new Ellipse
                        {
                            Width = 10,
                            Height = 10,
                            Stroke = Brushes.SkyBlue,
                            StrokeThickness = 2,
                            Fill = new SolidColorBrush(Color.FromArgb(90, 144, 238, 144)),
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(ellipse, p.X - ellipse.Width / 2);
                        Canvas.SetTop(ellipse, p.Y - ellipse.Height / 2);
                        Panel.SetZIndex(ellipse, 100);
                        CanvasCube.Children.Add(ellipse);
                        _cubeHighlightShape = ellipse;
                        break;
                    }
            }
        }
    }
}