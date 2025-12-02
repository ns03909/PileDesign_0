using System;
using System.Windows;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;


namespace PileDesign.Views
{
    public static class ProjectionUtils
    {
        public static (Point center2D, double major, double minor, double angleDeg)? ProjectCircleAsEllipseExact(
            Point3D center3D,
            Vector3D normal,
            double radius,
            Func<Point3D, Point> canvasTransform)
        {
            if (canvasTransform == null) throw new ArgumentNullException(nameof(canvasTransform));
            if (radius <= 0) return null;

            if (normal.Length == 0) normal = new Vector3D(0, 0, 1);
            normal.Normalize();

            // 平面内の直交基底 u, v を作る
            Vector3D any = Math.Abs(normal.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
            Vector3D u = Vector3D.CrossProduct(normal, any);
            if (u.LengthSquared == 0) u = Vector3D.CrossProduct(normal, new Vector3D(1, 0, 0));
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(normal, u);
            v.Normalize();

            // radius を掛けた基底
            u *= radius;
            v *= radius;

            // 中心の画像座標
            Point center2D = canvasTransform(center3D);

            // 基底ベクトルを投影して画像上の差分ベクトルを得る
            Point imgUpt = canvasTransform(new Point3D(center3D.X + u.X, center3D.Y + u.Y, center3D.Z + u.Z));
            Point imgVpt = canvasTransform(new Point3D(center3D.X + v.X, center3D.Y + v.Y, center3D.Z + v.Z));

            Vector imageU = imgUpt - center2D;
            Vector imageV = imgVpt - center2D;

            double eps = 1e-9;
            if (imageU.Length <= eps && imageV.Length <= eps) return null;

            // 2x2 行列 A = [imageU imageV]（列ベクトル）
            var A = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.OfArray(new double[,]
            {
                { imageU.X, imageV.X },
                { imageU.Y, imageV.Y }
            });

            // SVD により A = U * Σ * V^T
            var svd = A.Svd(computeVectors: true);
            if (svd == null || svd.S == null || svd.S.Count < 2) return null;

            // 特異値と U 行列を取得
            double s1 = svd.S[0];
            double s2 = svd.S[1];
            var Umat = svd.U;
            if (Umat == null || Umat.RowCount < 2 || Umat.ColumnCount < 2) return null;

            // 長軸・短軸を決定（特異値は非負）
            double major = Math.Max(s1, s2);
            double minor = Math.Min(s1, s2);

            // 長軸に対応する U の列インデックス
            int idxMajor = s1 >= s2 ? 0 : 1;

            // U の idxMajor 列が画像上の長軸方向（正規化済み列）
            double ux = Umat[0, idxMajor];
            double uy = Umat[1, idxMajor];

            // 角度は x 軸から反時計回り（ラジアン）
            double angleRad = Math.Atan2(uy, ux);

            double angleDeg = angleRad * 180.0 / Math.PI;

            return (center2D, major, minor, angleDeg);
        }
    }
}
