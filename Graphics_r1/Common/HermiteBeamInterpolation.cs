using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace PileDesign.Common
{
    /// <summary>
    /// 3次Hermite形状関数による梁要素内変位の補間
    /// Euler-Bernoulli梁の厳密な変位分布を再現する
    /// </summary>
    public static class HermiteBeamInterpolation
    {
        /// <summary>
        /// 梁要素内の変形後座標点列を生成する
        /// </summary>
        /// <param name="beam">梁要素</param>
        /// <param name="dispI">i端の全体座標系変位 (Ux,Uy,Uz,Rx,Ry,Rz)</param>
        /// <param name="dispJ">j端の全体座標系変位 (Ux,Uy,Uz,Rx,Ry,Rz)</param>
        /// <param name="dispScale">表示倍率</param>
        /// <param name="nDiv">要素内分割数</param>
        /// <returns>変形後の3D座標点列 (nDiv+1 点)</returns>
        public static List<Point3D> GetDeformedPoints(
            Beam beam, NodeDisp dispI, NodeDisp dispJ,
            double dispScale, int nDiv = 10)
        {
            Matrix<double> T12 = beam.GetCachedCoordTransform();
            var R = T12.SubMatrix(0, 3, 0, 3); // 3×3 回転行列
            return GetDeformedPointsCore(beam.NodeI.Coord, beam.NodeJ.Coord, R, dispI, dispJ, dispScale, nDiv);
        }

        /// <summary>
        /// 両端座標と変位から変形後座標点列を生成する（FEM Beam インスタンス不要）。
        /// VB 解析の基礎梁など、AnaModel の Beam を介さない要素に使用する。
        /// 3×3 回転行列は梁方向ベクトルから <see cref="Utils.GetNodeTransformMatrix"/> で算出する。
        /// </summary>
        public static List<Point3D> GetDeformedPoints(
            Point3D coordI, Point3D coordJ,
            NodeDisp dispI, NodeDisp dispJ,
            double dispScale, int nDiv = 10)
        {
            Vector3D v = new(coordJ.X - coordI.X, coordJ.Y - coordI.Y, coordJ.Z - coordI.Z);
            if (v.Length < 1e-12)
            {
                return new List<Point3D>
                {
                    new(coordI.X + dispI.Ux * dispScale,
                        coordI.Y + dispI.Uy * dispScale,
                        coordI.Z + dispI.Uz * dispScale)
                };
            }
            var R = Utils.GetNodeTransformMatrix(v);
            return GetDeformedPointsCore(coordI, coordJ, R, dispI, dispJ, dispScale, nDiv);
        }

        private static List<Point3D> GetDeformedPointsCore(
            Point3D coordI, Point3D coordJ, Matrix<double> R,
            NodeDisp dispI, NodeDisp dispJ, double dispScale, int nDiv)
        {
            var points = new List<Point3D>(nDiv + 1);

            double L = Math.Sqrt(
                (coordJ.X - coordI.X) * (coordJ.X - coordI.X) +
                (coordJ.Y - coordI.Y) * (coordJ.Y - coordI.Y) +
                (coordJ.Z - coordI.Z) * (coordJ.Z - coordI.Z));
            if (L < 1e-12)
            {
                points.Add(new Point3D(
                    coordI.X + dispI.Ux * dispScale,
                    coordI.Y + dispI.Uy * dispScale,
                    coordI.Z + dispI.Uz * dispScale));
                return points;
            }

            // 局所座標系に変換
            var uI_local = R * Vector<double>.Build.DenseOfArray([dispI.Ux, dispI.Uy, dispI.Uz]);
            var rI_local = R * Vector<double>.Build.DenseOfArray([dispI.Rx, dispI.Ry, dispI.Rz]);
            var uJ_local = R * Vector<double>.Build.DenseOfArray([dispJ.Ux, dispJ.Uy, dispJ.Uz]);
            var rJ_local = R * Vector<double>.Build.DenseOfArray([dispJ.Rx, dispJ.Ry, dispJ.Rz]);

            double vyi = uI_local[1], vyj = uJ_local[1];
            double vzi = uI_local[2], vzj = uJ_local[2];
            double thetaYi = rI_local[1], thetaYj = rJ_local[1];
            double thetaZi = rI_local[2], thetaZj = rJ_local[2];
            double uxi = uI_local[0], uxj = uJ_local[0];

            var Rt = R.Transpose();

            for (int k = 0; k <= nDiv; k++)
            {
                double s = (double)k / nDiv;

                double ux_s = uxi * (1 - s) + uxj * s;
                double uy_s = Hermite(s, L, vyi, thetaZi, vyj, thetaZj);
                double uz_s = Hermite(s, L, vzi, -thetaYi, vzj, -thetaYj);

                var d_local = Vector<double>.Build.DenseOfArray([ux_s, uy_s, uz_s]);
                var d_global = Rt * d_local;

                double x0 = coordI.X * (1 - s) + coordJ.X * s;
                double y0 = coordI.Y * (1 - s) + coordJ.Y * s;
                double z0 = coordI.Z * (1 - s) + coordJ.Z * s;

                points.Add(new Point3D(
                    x0 + d_global[0] * dispScale,
                    y0 + d_global[1] * dispScale,
                    z0 + d_global[2] * dispScale));
            }

            return points;
        }

        /// <summary>
        /// 3次Hermite補間
        /// v(s) = N1·vi + N2·θi·L + N3·vj + N4·θj·L
        /// </summary>
        /// <param name="s">パラメータ (0〜1)</param>
        /// <param name="L">要素長</param>
        /// <param name="vi">i端変位</param>
        /// <param name="thetaI">i端回転 (v'方向)</param>
        /// <param name="vj">j端変位</param>
        /// <param name="thetaJ">j端回転 (v'方向)</param>
        private static double Hermite(double s, double L, double vi, double thetaI, double vj, double thetaJ)
        {
            double s2 = s * s;
            double s3 = s2 * s;

            double N1 = 1 - 3 * s2 + 2 * s3;
            double N2 = (s - 2 * s2 + s3) * L;
            double N3 = 3 * s2 - 2 * s3;
            double N4 = (-s2 + s3) * L;

            return N1 * vi + N2 * thetaI + N3 * vj + N4 * thetaJ;
        }
    }
}
