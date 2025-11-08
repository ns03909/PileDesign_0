using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace PileDesign.FEM
{
    internal class Utils
    {
        public static double GetLengthBetweenTwoNodes(Node nodeI, Node nodeJ)
        {
            if (nodeI == null) throw new ArgumentNullException(nameof(nodeI));
            if (nodeJ == null) throw new ArgumentNullException(nameof(nodeJ));
            if (nodeI.Coord == null) throw new ArgumentNullException(nameof(nodeI.Coord));
            if (nodeJ.Coord == null) throw new ArgumentNullException(nameof(nodeJ.Coord));


            double lengthX = Math.Abs(nodeI.Coord.X - nodeJ.Coord.X);
            double lengthY = Math.Abs(nodeI.Coord.Y - nodeJ.Coord.Y);
            double lengthZ = Math.Abs(nodeI.Coord.Z - nodeJ.Coord.Z);
            double length = Math.Sqrt(lengthX * lengthX + lengthY * lengthY + lengthZ * lengthZ);
            return length;
        }

        public static Matrix<double> MapOn(Matrix<double> dst, int row, int col, Matrix<double> src)
        {
            for (int iRow = 0; iRow < src.RowCount; iRow++)
            {
                for (int iCol = 0; iCol < src.ColumnCount; iCol++)
                {
                    dst[row + iRow, col + iCol] += src[iRow, iCol];
                }
            }
            return dst;
        }

        // 3x3座標変換行列を生成
        private static Matrix<double> CreateTransform3x3(double dx, double dy, double dz, double coordAngle = 0.0)
        {
            double cos = Math.Cos(coordAngle / 180 * Math.PI);
            double sin = Math.Sin(coordAngle / 180 * Math.PI);
            double errorValue = 1.0E-10;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double lx = dx / length, mx = dy / length, nx = dz / length;
            double lambda = Math.Sqrt(lx * lx + mx * mx);
            double horzLength = Math.Sqrt(dx * dx + dy * dy);

            double ly, my, ny, lz, mz, nz;
            if (horzLength > errorValue)
            {
                ly = 1 / lambda * (-mx * cos + (-nx * lx) * sin);
                my = 1 / lambda * (lx * cos + (-mx * nx) * sin);
                ny = 1 / lambda * (lambda * lambda * sin);
                lz = 1 / lambda * (mx * sin - nx * lx * cos);
                mz = 1 / lambda * (lx * sin - mx * nx * cos);
                nz = 1 / lambda * (lambda * lambda * cos);
            }
            else
            {
                ly = nx * cos;
                my = sin;
                ny = 0;
                lz = -nx * sin;
                mz = cos;
                nz = 0;
            }
            return Matrix<double>.Build.DenseOfArray(new double[,] {
        { lx, mx, nx },
        { ly, my, ny },
        { lz, mz, nz }
    });
        }

        // 3x3座標変換マトリクスを返すメソッド
        public static Matrix<double> GetNodeTransformMatrix(Vector3D v, double coordAngle = 0.0)
            => CreateTransform3x3(v.X, v.Y, v.Z, coordAngle);

        // 12x12座標変換マトリクスを返すメソッド
        public static Matrix<double> GetTransformMatrix(Node nodeI, Node nodeJ, double coordAngle = 0.0)
        {
            var m3 = CreateTransform3x3(
                nodeJ.Coord.X - nodeI.Coord.X,
                nodeJ.Coord.Y - nodeI.Coord.Y,
                nodeJ.Coord.Z - nodeI.Coord.Z,
                coordAngle);
            var t = Matrix<double>.Build.Dense(12, 12);
            for (int block = 0; block < 4; block++)
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        t[block * 3 + i, block * 3 + j] = m3[i, j];
            return t;
        }


        //// 座標変換マトリクスを返すメソッド
        //public static Matrix<double> GetNodeTransformMatrix(Vector3D vector3D, double coordAngle = 0.0)
        //{
        //    double cos = Math.Cos(coordAngle / 180 * Math.PI);
        //    double sin = Math.Sin(coordAngle / 180 * Math.PI);

        //    double errorValue = 1.0E-10;
        //    double distanceX = vector3D.X;
        //    double distanceY = vector3D.Y;
        //    double distanceZ = vector3D.Z;
        //    double length = vector3D.Length;
        //    double horzLength = Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
        //    double lx = distanceX / length;
        //    double mx = distanceY / length;
        //    double nx = distanceZ / length;
        //    double lambda = Math.Sqrt(lx * lx + mx * mx); 

        //    double ly;
        //    double my;
        //    double ny;

        //    double lz;
        //    double mz;
        //    double nz;

        //    if (horzLength > errorValue) // 一般の場合
        //    {
        //        ly = 1 / lambda * (-mx * cos + (-nx * lx) * sin);
        //        my = 1 / lambda * (lx * cos + (-mx * nx) * sin);
        //        ny = 1 / lambda* (0 * cos + lambda * lambda * sin);

        //        lz = 1 / lambda * (-(-mx) * sin + (-nx * lx) * cos);
        //        mz = 1 / lambda * (-lx * sin + (-mx * nx) * cos);
        //        nz = 1 / lambda * (-0 * sin + lambda * lambda * cos);
        //    }
        //    else // 直立の場合
        //    {
        //        ly = nx * cos + 0 * sin;
        //        my = 0 * cos + 1 * sin;
        //        ny = 0 * cos + 0 * sin;

        //        lz = -(nx)* sin + 0 * cos;
        //        mz = -0 * sin + 1 * cos;
        //        nz = -0 * sin + 0 * cos;
        //    }

        //    return Matrix<double>.Build.DenseOfArray(new double[,]
        //        {
        //            {lx, mx, nx},
        //            {ly, my, ny},
        //            {lz, mz, nz}
        //        });
        //}

        //// 座標変換マトリクスを返すメソッド
        //public static Matrix<double> GetTransformMatrix(Node nodeI, Node nodeJ, double coordAngle = 0.0)
        //{
        //    double cos = Math.Cos(coordAngle / 180 * Math.PI);
        //    double sin = Math.Sin(coordAngle / 180 * Math.PI);

        //    double errorValue = 1.0E-10;
        //    double distanceX = nodeJ.Coord.X - nodeI.Coord.X;
        //    double distanceY = nodeJ.Coord.Y - nodeI.Coord.Y;
        //    double distanceZ = nodeJ.Coord.Z - nodeI.Coord.Z;
        //    double length = GetLengthBetweenTwoNodes(nodeI, nodeJ);
        //    double horzLength = Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
        //    double lx = distanceX / length;
        //    double mx = distanceY / length;
        //    double nx = distanceZ / length;
        //    double lambda = Math.Sqrt(lx * lx + mx * mx);

        //    double ly;
        //    double my;
        //    double ny;

        //    double lz;
        //    double mz;
        //    double nz;

        //    if (horzLength > errorValue) // 一般の場合
        //    {
        //        ly = 1 / lambda * (-mx * cos + (-nx * lx) * sin);
        //        my = 1 / lambda * (lx * cos + (-mx * nx) * sin);
        //        ny = 1 / lambda * (0 * cos + lambda * lambda * sin);

        //        lz = 1 / lambda * (-(-mx) * sin + (-nx * lx) * cos);
        //        mz = 1 / lambda * (-lx * sin + (-mx * nx) * cos);
        //        nz = 1 / lambda * (-0 * sin + lambda * lambda * cos);
        //    }
        //    else // 直立の場合
        //    {
        //        ly = nx * cos + 0 * sin;
        //        my = 0 * cos + 1 * sin;
        //        ny = 0 * cos + 0 * sin;

        //        lz = -(nx) * sin + 0 * cos;
        //        mz = -0 * sin + 1 * cos;
        //        nz = -0 * sin + 0 * cos;
        //    }

        //    Matrix<double> transMatrix = Matrix<double>.Build.DenseOfArray(new double[,]
        //        {
        //            {lx, mx, nx},
        //            {ly, my, ny},
        //            {lz, mz, nz}
        //        });
        //    Matrix<double> t = Matrix<double>.Build.Dense(12, 12);
        //    for (int block = 0; block < 4; block++)
        //    {
        //        int baseIndex = block * 3;
        //        for (int i = 0; i < 3; i++)
        //            for (int j = 0; j < 3; j++)
        //                t[baseIndex + i, baseIndex + j] = transMatrix[i, j];
        //    }
        //    return t;
        //}

        // eqリスト作成
        public static List<int> GetEquationNumbers(Node nodeI, Node nodeJ)/*, AnaModel anaModel)*/
        {
            ArgumentNullException.ThrowIfNull(nodeI);
            ArgumentNullException.ThrowIfNull(nodeJ);

            var eq = new List<int>(12);
            Node[] masterNodesI = nodeI.MasterNodes;
            Node[] masterNodesJ = nodeJ.MasterNodes;

            for (int index = 0; index < 6; index++)
                eq.Add(masterNodesI[index]?.EquationNumber[index] ?? nodeI.EquationNumber[index]);
            for (int index = 0; index < 6; index++)
                eq.Add(masterNodesJ[index]?.EquationNumber[index] ?? nodeJ.EquationNumber[index]);
            return eq;
        }

        // ヤング率からせん断剛性を取得
        public static double GetShearModulus(double youngsModulus, double poissonRatio)
        {
            // 剛性率G = E / (2 * (1 + ν))
            return youngsModulus / (2 * (1 + poissonRatio));
        }


        // 補完メソッド
        public static double Interpolate(double[] x, double[] y, double value)
        {
            if (x == null || y == null || x.Length != y.Length || x.Length == 0)
                throw new ArgumentException("配列の長さが不正です");

            for (int i = 0; i < x.Length - 1; i++)
            {
                if (x[i] <= value && value < x[i + 1])
                {
                    return (y[i + 1] * (value - x[i]) + y[i] * (x[i + 1] - value)) / (x[i + 1] - x[i]);
                }
            }
            return y[^1]; // 範囲外は最後の値
        }

        // 剛性マトリクス加算（スレイブ回転成分含む）
        public static void AddStiffnessToGlobal(
            Matrix<double> matrixGlobalStiffness,
            Matrix<double> localStiffness,
            List<int> eq,
            bool isRowFree,
            bool isColFree,
            Node nodeI,
            Node nodeJ)
        {
            Matrix<double> transferMatrix = Matrix<double>.Build.DenseIdentity(12);

            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    transferMatrix[r, c] = nodeI.TransferMatrix[r, c];
                    transferMatrix[r + 6, c + 6] = nodeJ.TransferMatrix[r, c];
                }
            }

            // ローカル剛性マトリクスを変換
            var transferedLocalStiffness = transferMatrix.Transpose() * localStiffness * transferMatrix;
            // eqリストを配列に変換

            for (int i_row = 0; i_row < 12; i_row++) // 行
            {
                if ((isRowFree && eq[i_row] < 0) || (!isRowFree && eq[i_row] >= 0))
                    continue; // 負値の場合はスキップ

                for (int i_col = 0; i_col < 12; i_col++) // 列
                {

                    if ((isColFree && eq[i_col] < 0) || (!isColFree && eq[i_col] >= 0))
                        continue; // 負値の場合はスキップ

                    matrixGlobalStiffness[eq[i_row], eq[i_col]] += transferedLocalStiffness[i_row, i_col];
                }
            }
        }
    }
}