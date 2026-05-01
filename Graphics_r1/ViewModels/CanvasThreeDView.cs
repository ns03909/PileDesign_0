using PileDesign.Graphics.Abstractions;
using PileDesign.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    public class CanvasThreeDView : BaseModel, ICoordinateTransform
    {
        public double BoxX { get; set; } // 図枠原点のx座標
        public double BoxY { get; set; } // 図枠原点のy座標
        public double BoxLx { get; set; } // 図枠のx方向の長さ
        public double BoxLy { get; set; } // 図枠のy方向の長さ
        public double FigScale { get; set; } // 図の縮尺
        public const double Sscale = 10;

        /// <summary>
        /// φ角度の最大・最小制限値（±90度）
        /// ジンバルロック回避のため以前は89.9に制限していたが、
        /// IsPerspective=false時は問題ないため90.0に設定
        /// </summary>
        public const double MaxPhiAngle = 90.0;

        public Action UpdateCanvas3DAction { get; set; }

        private Point3D _ct; // 透視変換の図心
        public Point3D Ct
        {
            get => _ct;
            set => SetProperty(ref _ct, value);
        }

        private double _tht;
        public double Tht
        {
            get { return _tht; }
            set
            {
                if (_tht != value)
                {
                    _tht = value;
                    OnPropertyChanged(nameof(Tht));
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double _phi;
        public double Phi
        {
            get { return _phi; }
            set
            {
                if (_phi != value)
                {
                    _phi = value;
                    OnPropertyChanged(nameof(Phi));
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 扁平率
        public double Flattening => Math.Sin(Math.Abs(Phi) * Math.PI / 180.0);

        private double _dv;
        public double Dv
        {
            get { return _dv; }
            set
            {
                if (_dv != value)
                {
                    _dv = value;
                    OnPropertyChanged(nameof(Dv));
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double scale;
        public double Scale
        {
            get { return scale; }
            set
            {
                if (scale != value)
                {
                    scale = value;
                    OnPropertyChanged(nameof(Scale));
                    OnPropertyChanged(nameof(ScaleBarPixels));
                    OnPropertyChanged(nameof(ScaleBarMeters));
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        /// <summary>
        /// Canvas オーバーレイのスケールバー (B.6) で使う表示長 (ピクセル単位)。
        /// 現在の Scale (1m あたりピクセル数) を見て 1 / 5 / 10 / 50m から「画面上 50〜200px」に
        /// 収まる代表長を選ぶ。Scale 変化時に自動で 1m → 5m → 10m と切り替わる。
        /// </summary>
        public double ScaleBarPixels
        {
            get
            {
                double s = scale;
                if (s <= 0) return 0;
                // 候補: 0.1, 0.5, 1, 5, 10, 50, 100 m
                double[] candidates = { 0.1, 0.5, 1, 5, 10, 50, 100, 500 };
                foreach (var m in candidates)
                {
                    double px = m * s;
                    if (px >= 50 && px <= 200) return px;
                }
                return 100; // フォールバック
            }
        }

        public string ScaleBarMeters
        {
            get
            {
                double s = scale;
                if (s <= 0) return "";
                double[] candidates = { 0.1, 0.5, 1, 5, 10, 50, 100, 500 };
                foreach (var m in candidates)
                {
                    double px = m * s;
                    if (px >= 50 && px <= 200)
                        return m < 1 ? $"{m:0.0} m" : $"{m:0} m";
                }
                return "";
            }
        }

        private bool isPerspective;
        public bool IsPerspective
        {
            get { return isPerspective; }
            set
            {
                if (isPerspective != value)
                {
                    isPerspective = value;
                    OnPropertyChanged(nameof(IsPerspective));
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private Point _viewTransition;
        public Point ViewTransition
        {
            get => _viewTransition;
            set => SetProperty(ref _viewTransition, value);
        }

        public double Dv0 { get; set; }

        private Point _org;
        public Point Org
        {
            get => _org;
            set => SetProperty(ref _org, value);
        }

        // コンストラクタ
        public CanvasThreeDView()
        {
            BoxX = 0;
            BoxY = 0;
            BoxLx = 0;
            BoxLy = 0;
            FigScale = 0;
            Ct = new Point3D(0, 0, 0);
            Tht = -45;
            Phi = 45;
            Dv = 10;
            Dv0 = 10;
            Scale = 10;
            Org = new Point(0, 0);
            ViewTransition = new Point(0, 0);
        }

        // 視点の初期設定
        public void SetCt(IList<Point3D> nodes)
        {
            if (nodes == null || nodes.Count == 0) return;

            double xMax = double.MinValue, yMax = double.MinValue, zMax = double.MinValue;
            double xMin = double.MaxValue, yMin = double.MaxValue, zMin = double.MaxValue;

            foreach (Point3D node in nodes)
            {
                if (node.X > xMax) xMax = node.X;
                if (node.Y > yMax) yMax = node.Y;
                if (node.Z > zMax) zMax = node.Z;
                if (node.X < xMin) xMin = node.X;
                if (node.Y < yMin) yMin = node.Y;
                if (node.Z < zMin) zMin = node.Z;
            }

            Point3D w = new(xMax - xMin, yMax - yMin, zMax - zMin);
            Dv0 = 5 * Math.Sqrt(w.X * w.X + w.Y * w.Y + w.Z * w.Z);

            // 注視点の計算
            Ct = new Point3D((xMax + xMin) / 2, (yMax + yMin) / 2, (zMax + zMin) / 2);
        }

        // 中心の設定
        public void SetOrg(double width, double height)
        {
            Org = new Point(width * 0.5, height * 0.5);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }

        // 節点の変換
        public Point Transformation(Point3D node)
        {
            // 入力座標のNaN/Infinity防止
            if (!double.IsFinite(node.X) || !double.IsFinite(node.Y) || !double.IsFinite(node.Z))
                return new Point(0, 0);

            Point s = HomographyTransformation(Ct, node);

            // 原点を図枠の中心に移動
            s.X = s.X * Scale + Org.X + ViewTransition.X;
            s.Y = -s.Y * Scale + Org.Y + ViewTransition.Y;

            // 出力座標のNaN/Infinity防止（HomographyTransformation内の除算で発生しうる）
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y))
                return new Point(0, 0);

            return s;
        }

        // ICoordinateTransform.Transform の実装（既存のTransformationメソッドをラップ）
        Point ICoordinateTransform.Transform(Point3D point3D) => Transformation(point3D);

        // 新しいメソッドを追加して、逆変換を行います
        public Point3D InverseTransformation(Point point)
        {
            // 原点を図枠の中心に戻す
            double x = (point.X - Org.X - ViewTransition.X) / Scale;
            double y = -(point.Y - Org.Y - ViewTransition.Y) / Scale;

            // 逆透視変換を行います
            return InverseHomographyTransformation(Ct, new Point(x, y));
        }

        // 指定Z平面上のワールド座標を求める逆変換
        public Point3D InverseTransformationAtZ(Point point, double targetZ)
        {
            double xCamera = (point.X - Org.X - ViewTransition.X) / Scale;
            double yCamera = -(point.Y - Org.Y - ViewTransition.Y) / Scale;

            var camPoint = new Point(xCamera, yCamera);

            // 視線レイ上の2点を求める（カメラ深度 0 と 1）
            Point3D p0 = InverseHomographyAtDepth(Ct, camPoint, 0.0);
            Point3D p1 = InverseHomographyAtDepth(Ct, camPoint, 1.0);

            // レイ方向
            double rdx = p1.X - p0.X;
            double rdy = p1.Y - p0.Y;
            double rdz = p1.Z - p0.Z;

            // Z=targetZ 平面との交点（平行の場合はフォールバック）
            if (Math.Abs(rdz) < 1e-10)
                return p0;

            double t = (targetZ - p0.Z) / rdz;
            return new Point3D(p0.X + t * rdx, p0.Y + t * rdy, targetZ);
        }

        // カメラ深度を指定した逆透視変換
        private Point3D InverseHomographyAtDepth(Point3D ct, Point point, double zS)
        {
            double thtRad = DegreesToRadians(Tht);
            double phiRad = DegreesToRadians(Phi);

            double cosTht = Math.Cos(thtRad);
            double sinTht = Math.Sin(thtRad);
            double cosPhi = Math.Cos(phiRad);
            double sinPhi = Math.Sin(phiRad);

            double xS = point.X;
            double yS = point.Y;

            if (IsPerspective)
            {
                double dvAdjusted = Dv - zS;
                if (dvAdjusted == 0) dvAdjusted = 1;
                xS /= Dv / dvAdjusted;
                yS /= Dv / dvAdjusted;
            }

            double y2 = xS;
            double z2 = yS;
            double x2 = zS;

            double x1 = x2 * cosPhi + z2 * sinPhi;
            double y1 = y2;
            double z1 = -x2 * sinPhi + z2 * cosPhi;

            double dx = x1 * cosTht + y1 * sinTht;
            double dy = -x1 * sinTht + y1 * cosTht;
            double dz = z1;

            return new Point3D(dx + ct.X, dy + ct.Y, dz + ct.Z);
        }

        // 逆透視変換メソッド（既存: zS=0 固定）
        private Point3D InverseHomographyTransformation(Point3D ct, Point point)
        {
            double thtRad = DegreesToRadians(Tht);
            double phiRad = DegreesToRadians(Phi);

            double cosTht = Math.Cos(thtRad);
            double sinTht = Math.Sin(thtRad);
            double cosPhi = Math.Cos(phiRad);
            double sinPhi = Math.Sin(phiRad);

            // 透視変換の逆を計算
            double xS = point.X;
            double yS = point.Y;
            double zS = 0;

            if (IsPerspective)
            {
                double dvAdjusted = Dv - zS;
                if (dvAdjusted == 0) dvAdjusted = 1;
                xS /= Dv / dvAdjusted;
                yS /= Dv / dvAdjusted;
            }

            // 座標変換の逆を計算
            double y2 = xS;
            double z2 = yS;
            double x2 = zS;

            // φ回転の逆を計算
            double x1 = x2 * cosPhi + z2 * sinPhi;
            double y1 = y2;
            double z1 = -x2 * sinPhi + z2 * cosPhi;

            // θ回転の逆を計算
            double dx = x1 * cosTht + y1 * sinTht;
            double dy = -x1 * sinTht + y1 * cosTht;
            double dz = z1;

            // 原点を注視点に戻す
            return new Point3D(dx + ct.X, dy + ct.Y, dz + ct.Z);
        }

        // キューブの変換
        public Point CubeTransformation(Point3D node, Canvas canvas)
        {
            Point3D center = new();
            Point s = HomographyTransformation(center, node);

            s.X += canvas.Width * 0.5;
            s.Y = -s.Y + canvas.Height * 0.5;
            return s;
        }

        // 注視ベクトル
        public Vector3D GetViewVector()
        {
            double thtRad = DegreesToRadians(Tht);
            double phiRad = DegreesToRadians(Phi);

            double cosTht = Math.Cos(thtRad);
            double sinTht = Math.Sin(thtRad);
            double cosPhi = Math.Cos(phiRad);
            double sinPhi = Math.Sin(phiRad);

            // 注視ベクトルの計算
            double x = cosPhi * cosTht;
            double y = cosPhi * sinTht;
            double z = sinPhi;

            return new Vector3D(x, y, z);
        }

        // 投影長さ / 実長さ
        public double GetTransformedLengthOverAbsLength(Point3D point0, Point3D point1)
        {
            double originalLength = (point1 - point0).Length;
            Point transformedPoint0 = Transformation(point0);
            Point transformedPoint1 = Transformation(point1);
            double transformedLength = (transformedPoint1 - transformedPoint0).Length;
            return transformedLength / (originalLength * Scale);
        }

        // 3D矩形対角頂点から2D長方形への変換
        public List<PathFigure> RectsTranformation(List<(Point3D, Point3D)> rectangles)
        {
            List<PathFigure> pathFigures = [];

            foreach ((Point3D, Point3D) rectangle in rectangles)
            {
                double x1 = rectangle.Item1.X;
                double y1 = rectangle.Item1.Y;
                double z = rectangle.Item1.Z;
                double x2 = rectangle.Item2.X;
                double y2 = rectangle.Item2.Y;
                Point s1 = Transformation(new Point3D(x1, y1, z));
                Point s2 = Transformation(new Point3D(x1, y2, z));
                Point s3 = Transformation(new Point3D(x2, y2, z));
                Point s4 = Transformation(new Point3D(x2, y1, z));

                // PathFigure を作成し、最初の点を設定
                PathFigure pathFigure = new()
                {
                    StartPoint = s1,
                    Segments = [
                        new LineSegment(s2, true),
                        new LineSegment(s3, true),
                        new LineSegment(s4, true)
                        ],
                    IsClosed = true
                };

                pathFigures.Add(pathFigure);
            }
            return pathFigures;
        }

        // 節点の透視変換 注視点Ct、Tht、Phi、Dv0
        private Point HomographyTransformation(Point3D ct, Point3D node)
        {
            double thtRad = DegreesToRadians(Tht);
            double phiRad = DegreesToRadians(Phi);

            double cosTht = Math.Cos(-thtRad);
            double sinTht = Math.Sin(-thtRad);
            double cosPhi = Math.Cos(-phiRad);
            double sinPhi = Math.Sin(-phiRad);

            // 1. 原点を注視点に移動
            double dx = node.X - ct.X;
            double dy = node.Y - ct.Y;
            double dz = node.Z - ct.Z;

            // 2. θ回転
            double x1 = dx * cosTht - dy * sinTht;
            double y1 = dx * sinTht + dy * cosTht;
            double z1 = dz;

            // 3. φ回転
            double x2 = x1 * cosPhi - z1 * sinPhi;
            double y2 = y1;
            double z2 = x1 * sinPhi + z1 * cosPhi;

            // 4. 座標変換
            double xS = y2;
            double yS = z2;
            double zS = x2;

            // 5. 透視変換
            if (IsPerspective)
            {
                double dvAdjusted = Dv - zS;
                if (dvAdjusted == 0) dvAdjusted = 1;
                xS *= Dv / dvAdjusted;
                yS *= Dv / dvAdjusted;
            }

            return new Point(xS, yS);
        }
    }
}
