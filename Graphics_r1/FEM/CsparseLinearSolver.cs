using CSparse;
using CSparse.Double;                // SparseMatrix
using CSparse.Double.Factorization;  // SparseQR
using System;
using System.Collections.Generic;

namespace PileDesign.FEM
{
    internal static class CsparseLinearSolver
    {
        // Kx = b を解く。CSparse の SparseQR で一般疎行列を解く
        public static double[] Solve(MathNet.Numerics.LinearAlgebra.Matrix<double> K,
                                     MathNet.Numerics.LinearAlgebra.Vector<double> b,
                                     bool isSpd = true)
        {
            if (K.RowCount != K.ColumnCount)
                throw new ArgumentException("Matrix K must be square.");
            if (K.RowCount != b.Count)
                throw new ArgumentException("Dimension mismatch between K and b.");

            int n = K.RowCount;

            // 列ごとのエントリを蓄積（SPD想定なら上三角のみ）
            var colLists = new List<(int row, double val)>[n];
            for (int j = 0; j < n; j++) colLists[j] = new List<(int, double)>();

            foreach (var t in K.EnumerateIndexed(MathNet.Numerics.LinearAlgebra.Zeros.AllowSkip))
            {
                int i = t.Item1, j = t.Item2;
                double v = t.Item3;
                if (v == 0.0) continue;
                if (isSpd && j < i) continue; // SPD時は上三角のみ投入
                colLists[j].Add((i, v));
            }

            // CSC 配列を構築
            int nnz = 0;
            for (int j = 0; j < n; j++) nnz += colLists[j].Count;

            var colPtr = new int[n + 1];
            var rowIdx = new int[nnz];
            var vals = new double[nnz];

            int p = 0;
            for (int j = 0; j < n; j++)
            {
                colPtr[j] = p;
                var list = colLists[j];
                list.Sort((a, b2) => a.row.CompareTo(b2.row)); // 行昇順

                foreach (var (row, val) in list)
                {
                    rowIdx[p] = row;
                    vals[p] = val;
                    p++;
                }
            }
            colPtr[n] = p;

            // CSparse の疎行列（CSC）
            var A = new SparseMatrix(n, n, vals, rowIdx, colPtr);

            var rhs = b.ToArray();
            var x = new double[n];

            // SparseQR（MinimumDegreeAtA がない環境では Natural にフォールバック）
            try
            {
                var qr = SparseQR.Create(A, ColumnOrdering.MinimumDegreeAtA);
                qr.Solve(rhs, x);
            }
            catch
            {
                var qr = SparseQR.Create(A, ColumnOrdering.Natural);
                qr.Solve(rhs, x);
            }

            return x;
        }
    }
}