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
            var points = new List<Point3D>(nDiv + 1);

            double L = beam.Length;
            if (L < 1e-12)
            {
                // 長さゼロの要素は1点のみ
                var c = beam.NodeI.Coord;
                points.Add(new Point3D(
                    c.X + dispI.Ux * dispScale,
                    c.Y + dispI.Uy * dispScale,
                    c.Z + dispI.Uz * dispScale));
                return points;
            }

            // 座標変換行列 T (12×12 → 6×6ブロック2つ)
            // T は 3×3 の回転行列を対角に4つ並べた 12×12 行列
            // ここでは 3×3 回転行列を取得して使う
            Matrix<double> T12 = beam.GetCachedCoordTransform();
            // T12 は 12×12: [R 0 0 0; 0 R 0 0; 0 0 R 0; 0 0 0 R] (各 R は 3×3)
            // 6DOF変位ベクトルの変換: d_local = T6 × d_global
            // T6 は 6×6: [R 0; 0 R]
            var R = T12.SubMatrix(0, 3, 0, 3); // 3×3 回転行列

            // 全体座標系の変位をベクトル化
            var dI_global = Vector<double>.Build.DenseOfArray(
                [dispI.Ux, dispI.Uy, dispI.Uz, dispI.Rx, dispI.Ry, dispI.Rz]);
            var dJ_global = Vector<double>.Build.DenseOfArray(
                [dispJ.Ux, dispJ.Uy, dispJ.Uz, dispJ.Rx, dispJ.Ry, dispJ.Rz]);

            // 局所座標系に変換
            // 並進: u_local = R × u_global, 回転: θ_local = R × θ_global
            var uI_local = R * Vector<double>.Build.DenseOfArray([dispI.Ux, dispI.Uy, dispI.Uz]);
            var rI_local = R * Vector<double>.Build.DenseOfArray([dispI.Rx, dispI.Ry, dispI.Rz]);
            var uJ_local = R * Vector<double>.Build.DenseOfArray([dispJ.Ux, dispJ.Uy, dispJ.Uz]);
            var rJ_local = R * Vector<double>.Build.DenseOfArray([dispJ.Rx, dispJ.Ry, dispJ.Rz]);

            // 局所座標系の成分:
            // x: 軸方向, y: 断面y方向, z: 断面z方向
            // uy(s) = Hermite(vyi, θzi, vyj, θzj)  (y方向変位は z回転に依存)
            // uz(s) = Hermite(vzi, -θyi, vzj, -θyj) (z方向変位は -y回転に依存)
            double vyi = uI_local[1], vyj = uJ_local[1];
            double vzi = uI_local[2], vzj = uJ_local[2];
            double thetaYi = rI_local[1], thetaYj = rJ_local[1];
            double thetaZi = rI_local[2], thetaZj = rJ_local[2];

            // 軸方向変位は線形補間
            double uxi = uI_local[0], uxj = uJ_local[0];

            Point3D coordI = beam.NodeI.Coord;
            Point3D coordJ = beam.NodeJ.Coord;

            // R^T (局所→全体座標変換)
            var Rt = R.Transpose();

            for (int k = 0; k <= nDiv; k++)
            {
                double s = (double)k / nDiv; // 0 → 1

                // 軸方向: 線形補間
                double ux_s = uxi * (1 - s) + uxj * s;

                // y方向: 3次Hermite補間
                double uy_s = Hermite(s, L, vyi, thetaZi, vyj, thetaZj);

                // z方向: 3次Hermite補間 (回転の符号に注意: vz' = -θy)
                double uz_s = Hermite(s, L, vzi, -thetaYi, vzj, -thetaYj);

                // 局所座標系の変位ベクトル
                var d_local = Vector<double>.Build.DenseOfArray([ux_s, uy_s, uz_s]);

                // 全体座標系に戻す
                var d_global = Rt * d_local;

                // 元の座標（線形補間）+ 変位 × スケール
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
