using CSparse;
using CSparse.Double;                // SparseMatrix
using CSparse.Double.Factorization;  // SparseQR, SparseLU, SparseCholesky, SparseLDL
using System;
using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// 疎行列直接解法による K x = b の求解。
    /// v28 F-new (2026-04-23): Cholesky → LDL → LU → QR の段階フォールバックで高速化。
    ///   FEM 剛性行列は通常 SPD なので Cholesky が効き、QR 比 3〜5 倍高速になる。
    ///   分岐条件や塑性ヒンジ近傍で indefinite になっても LDL/LU で対応、最終 QR で救済。
    /// </summary>
    internal static class CsparseLinearSolver
    {
        /// <summary>前回成功した solver 種別 (診断用)。</summary>
        public enum SolverKind { None, Cholesky, LDL, LU, QR }
        public static SolverKind LastSuccessfulSolver { get; private set; } = SolverKind.None;

        /// <summary>内部フェーズごとの累積 tick (ステップ局所で読み取り→リセット)。</summary>
        public static long CscBuildTicks { get; private set; }
        public static long FactorizeTicks { get; private set; }
        public static long SolveBackSubTicks { get; private set; }
        public static void ResetInternalTimers()
        {
            CscBuildTicks = 0;
            FactorizeTicks = 0;
            SolveBackSubTicks = 0;
        }

        // Kx = b を解く。
        public static double[] Solve(MathNet.Numerics.LinearAlgebra.Matrix<double> K,
                                     MathNet.Numerics.LinearAlgebra.Vector<double> b,
                                     bool isSpd = true)
        {
            if (K.RowCount != K.ColumnCount)
                throw new ArgumentException("Matrix K must be square.");
            if (K.RowCount != b.Count)
                throw new ArgumentException("Dimension mismatch between K and b.");

            int n = K.RowCount;

            // --- 1. CSC 構築フェーズ (計測) ---
            long _tsCsc = System.Diagnostics.Stopwatch.GetTimestamp();

            // 列ごとのエントリを蓄積 (AllowSkip により nnz のみ走査)。
            // Cholesky/LDL は両三角を渡しても内部で lower のみ使うため、全エントリを投入する。
            var colLists = new List<(int row, double val)>[n];
            for (int j = 0; j < n; j++) colLists[j] = new List<(int, double)>();

            foreach (var t in K.EnumerateIndexed(MathNet.Numerics.LinearAlgebra.Zeros.AllowSkip))
            {
                int i = t.Item1, j = t.Item2;
                double v = t.Item3;
                if (v == 0.0) continue;
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

            var A = new SparseMatrix(n, n, vals, rowIdx, colPtr);
            var rhs = b.ToArray();
            var x = new double[n];

            CscBuildTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsCsc;

            // --- 2. 分解+代入フェーズ (計測) ---
            // FEM 剛性行列は通常 SPD なので Cholesky が最速 (QR の ~3-5 倍)。
            // 塑性ヒンジ近傍で indefinite 化した際は LDL が対応。
            // 数値的に SPD でない場合 (非対称 K 等) は LU。最終救済 QR。

            // 1) Cholesky (SPD)
            try
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var chol = SparseCholesky.Create(A, ColumnOrdering.MinimumDegreeAtPlusA);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                chol.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                LastSuccessfulSolver = SolverKind.Cholesky;
                return x;
            }
            catch
            {
                Array.Clear(x, 0, n);
            }

            // 2) LDL (対称不定)
            try
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var ldl = SparseLDL.Create(A, ColumnOrdering.MinimumDegreeAtPlusA);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                ldl.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                LastSuccessfulSolver = SolverKind.LDL;
                return x;
            }
            catch
            {
                Array.Clear(x, 0, n);
            }

            // 3) LU (非対称)
            try
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var lu = SparseLU.Create(A, ColumnOrdering.MinimumDegreeAtA, 1.0);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                lu.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                LastSuccessfulSolver = SolverKind.LU;
                return x;
            }
            catch
            {
                Array.Clear(x, 0, n);
            }

            // 4) QR (最終救済、MinimumDegreeAtA 失敗時は Natural)
            try
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var qr = SparseQR.Create(A, ColumnOrdering.MinimumDegreeAtA);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                qr.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                LastSuccessfulSolver = SolverKind.QR;
                return x;
            }
            catch
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var qr = SparseQR.Create(A, ColumnOrdering.Natural);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                qr.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                LastSuccessfulSolver = SolverKind.QR;
                return x;
            }
        }
    }
}
