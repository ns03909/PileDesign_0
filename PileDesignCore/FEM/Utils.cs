using System;

namespace PileDesignCore
{
    internal class Utils
    {
        public static double GetLengthBetweenTwoNodes(Node nodeI, Node nodeJ)
        {
            double lengthX = Math.Abs(nodeI.Coord.X - nodeJ.Coord.X);
            double lengthY = Math.Abs(nodeI.Coord.Y - nodeJ.Coord.Y);
            double lengthZ = Math.Abs(nodeI.Coord.Z - nodeJ.Coord.Z);
            double length = Math.Sqrt(lengthX * lengthX + lengthY * lengthY + lengthZ * lengthZ);
            return length;
        }

        public static double[,] MapOn(double[,] dst, int row, int col, double[,] src)
        {
            for (int iRow = 0; iRow < src.GetLength(0); iRow++)
            {
                for (int iCol = 0; iCol < src.GetLength(1); iCol++)
                {
                    dst[row + iRow, col + iCol] += src[iRow, iCol];
                }
            }
            return dst;
        }

        public static double[,] GetTransformMatrix(Node nodeI, Node nodeJ)
        {
            double errorValue = 1.0E-10;
            double distanceX = nodeJ.Coord.X - nodeI.Coord.X;
            double distanceY = nodeJ.Coord.Y - nodeI.Coord.Y;
            double distanceZ = nodeJ.Coord.Z - nodeI.Coord.Z;
            double length = GetLengthBetweenTwoNodes(nodeI, nodeJ);
            double horzLength = Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
            double l = distanceX / length;
            double m = distanceY / length;
            double n = distanceZ / length;
            double normHorzLength = Math.Sqrt(l * l + m * m);

            double[,] transMatrix;
            if (horzLength < errorValue)
            {
                // 直立状態
                transMatrix = new double[,]
                {
                    {0.0, 0.0, n},
                    {n, 0.0, 0.0},
                    {0.0, 1.0, 0.0}
                };
            }
            else
            {
                // 直立以外の状態
                transMatrix = new double[,]
                {
                    {l, m, n},
                    {-m / normHorzLength, l / normHorzLength, 0.0},
                    {-l * n / normHorzLength, -m * n / normHorzLength, normHorzLength}
                };
            }

            double[,] t = new double[12, 12];
            MapOn(t, 0, 0, transMatrix);
            MapOn(t, 3, 3, transMatrix);
            MapOn(t, 6, 6, transMatrix);
            MapOn(t, 9, 9, transMatrix);
            return t;
        }

        public static double[] SolveLinearSystem(double[,] A, double[] b)
        {
            // This is a placeholder implementation for solving the linear system Ax = b.
            // You may want to replace it with a more sophisticated linear solver.
            return new double[A.GetLength(0)];  // Assuming a square system for simplicity
        }

        public static double[] MatrixVectorMultiply(double[,] matrix, double[] vector)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            if (cols != vector.Length)
            {
                throw new ArgumentException("Matrix and vector dimensions do not match.");
            }

            double[] result = new double[rows];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    result[i] += matrix[i, j] * vector[j];
                }
            }

            return result;
        }
    }
}