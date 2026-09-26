using CSparse;
using CSparse.Double;                // SparseMatrix
using CSparse.Double.Factorization;  // SparseQR, SparseLU, SparseCholesky, SparseLDL
using System;
using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// Cholesky 因子のキャッシュ。AnaModel に保持し、K 行列が再構築されない反復で
    /// CSC 構築 + Cholesky 分解をスキップして後退代入のみ行う。
    /// MapOnKmat 呼出し時に Invalidate() を呼ぶ運用。
    /// </summary>
    internal sealed class CholeskySolverCache
    {
        private SparseCholesky? _factor;
        private long _version;
        private long _factorVersion = -1;

        /// <summary>K 行列が変化したことを通知 (MapOnKmat 等の冒頭で呼ぶ)。</summary>
        public void Invalidate()
        {
            _version++;
            _factor = null; // 旧 factor は GC 任せ
        }

        public bool TryReuse(out SparseCholesky? factor)
        {
            if (_factor != null && _factorVersion == _version)
            {
                factor = _factor;
                return true;
            }
            factor = null;
            return false;
        }

        public void Store(SparseCholesky factor)
        {
            _factor = factor;
            _factorVersion = _version;
        }

        /// <summary>
        /// このモデルで解いた解のうち、相対残差が <see cref="CsparseLinearSolver.ResidualTolerance"/> を超えた数。
        /// モデル (荷重ケース) ごとに持つので、ケースを並列に解いても混ざらない。解析のログ・サマリーに出す。
        /// </summary>
        public long LargeResidualCount;
    }

    /// <summary>
    /// 疎行列直接解法による K x = b の求解。
    /// v28 F-new (2026-04-23): Cholesky → LDL → LU → QR の段階フォールバックで高速化。
    ///   FEM 剛性行列は通常 SPD なので Cholesky が効き、QR 比 3〜5 倍高速になる。
    ///   分岐条件や塑性ヒンジ近傍で indefinite になっても LDL/LU で対応、最終 QR で救済。
    /// v29 (2026-04-27): K が反復間で変化しない (Modified NR 後期, 線形ケース) 場合に
    ///   Cholesky 因子を再利用する CholeskySolverCache 対応を追加。CSC 構築 + 分解をスキップし
    ///   後退代入のみ実行。再利用ヒット数は CholeskyReuseCount で計測。
    /// </summary>
    internal static class CsparseLinearSolver
    {
        /// <summary>Cholesky factor を再利用した回数 (ステップ局所で読み取り→リセット)。</summary>
        public static long CholeskyReuseCount { get; private set; }
        public static void ResetReuseCount() { CholeskyReuseCount = 0; }

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
            CholeskyReuseCount = 0;
        }

        /// <summary>
        /// 解の相対残差 ‖K x − b‖ / ‖b‖ がこれを超えたら記録する (<see cref="LargeResidualCount"/>・ログ)。
        ///
        /// 例題 (計算例 9・10・K8) の解の残差は最大でも 1e-10 程度。条件の悪い行列ではこれを超える。
        /// <b>超えても解は差し替えない。</b>計算例 3-5 のように荷重が大きい段階で残差が 1e-6 を超える例題があり、
        /// 別の解法に差し替えると解析結果が変わる (差し替える場合は影響を測ってから決める)。
        /// </summary>
        internal const double ResidualTolerance = 1e-6;

        /// <summary>残差が <see cref="ResidualTolerance"/> を超えた解の数 (診断用。ステップ局所で読み取り→リセット)。</summary>
        public static long LargeResidualCount => System.Threading.Interlocked.Read(ref _largeResidualCount);
        private static long _largeResidualCount;
        public static void ResetLargeResidualCount() => System.Threading.Interlocked.Exchange(ref _largeResidualCount, 0);

        /// <summary>対称とみなす差の上限 (行列の最大の成分に対する比)。</summary>
        internal const double SymmetryTolerance = 1e-10;

        /// <summary>相対残差 ‖K x − b‖ / ‖b‖。b が 0 なら ‖K x‖。</summary>
        private static double RelativeResidual(MathNet.Numerics.LinearAlgebra.Matrix<double> K,
            MathNet.Numerics.LinearAlgebra.Vector<double> b, double[] x)
        {
            var r = K * MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(x) - b;
            double bn = b.L2Norm();
            double rn = r.L2Norm();
            return bn > 0 ? rn / bn : rn;
        }

        /// <summary>
        /// 解を残差で確かめる。残差が <see cref="ResidualTolerance"/> 以下なら true。超えたら数えて記録する
        /// (解は差し替えない。<see cref="ResidualTolerance"/> 参照)。
        /// </summary>
        private static bool CheckResidual(MathNet.Numerics.LinearAlgebra.Matrix<double> K,
            MathNet.Numerics.LinearAlgebra.Vector<double> b, double[] x, SolverKind kind, CholeskySolverCache? cache)
        {
            double residual = RelativeResidual(K, b, x);
            if (double.IsFinite(residual) && residual <= ResidualTolerance) return true;
            System.Threading.Interlocked.Increment(ref _largeResidualCount);
            if (cache != null) System.Threading.Interlocked.Increment(ref cache.LargeResidualCount);
            Serilog.Log.Debug("[CsparseLinearSolver] {Kind} の解の相対残差 {Residual:E2} が {Tolerance:E0} を超えています (解はそのまま使う)",
                kind, residual, ResidualTolerance);
            return false;
        }

        /// <summary>
        /// CSC の行列が数値的に対称か (<see cref="SymmetryTolerance"/>)。
        /// Cholesky・LDL 分解は片側の三角しか読まないので、非対称な行列に使うと例外にならずに別の方程式を解く。
        /// </summary>
        private static bool IsSymmetric(SparseMatrix A)
        {
            double maxAbs = 0;
            foreach (double v in A.Values) maxAbs = Math.Max(maxAbs, Math.Abs(v));
            if (maxAbs == 0) return true;
            var At = (SparseMatrix)A.Transpose();
            // 列ごとに A と Aᵀ の成分を行の順に突き合わせる (どちらも行の昇順。片方に無い位置は 0 とみなす)
            double tol = SymmetryTolerance * maxAbs;
            for (int j = 0; j < A.ColumnCount; j++)
            {
                int p = A.ColumnPointers[j], pEnd = A.ColumnPointers[j + 1];
                int q = At.ColumnPointers[j], qEnd = At.ColumnPointers[j + 1];
                while (p < pEnd || q < qEnd)
                {
                    int rowA = p < pEnd ? A.RowIndices[p] : int.MaxValue;
                    int rowT = q < qEnd ? At.RowIndices[q] : int.MaxValue;
                    double diff;
                    if (rowA == rowT) { diff = A.Values[p] - At.Values[q]; p++; q++; }
                    else if (rowA < rowT) { diff = A.Values[p]; p++; }
                    else { diff = At.Values[q]; q++; }
                    if (Math.Abs(diff) > tol) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Kx = b を解く。解の残差を確かめ、大きければ記録する (<see cref="ResidualTolerance"/>)。
        ///
        /// <paramref name="isSpd"/> は使わない (互換のために残す)。対称かどうかは行列から調べ、
        /// <b>対称なときだけ</b> Cholesky → LDL を試し、非対称なら LU から始める。以前は isSpd に関係なく最初に
        /// Cholesky を試し、分解と代入が例外なく終われば解をそのまま返していた。非対称な行列では Cholesky が
        /// 例外にならずに片側の三角だけで別の方程式を解くので、誤った解が黙って返った
        /// (例題の剛性行列は対称で、実際には起きていない)。
        /// </summary>
        public static double[] Solve(MathNet.Numerics.LinearAlgebra.Matrix<double> K,
                                     MathNet.Numerics.LinearAlgebra.Vector<double> b,
                                     bool isSpd = true,
                                     CholeskySolverCache? cache = null)
        {
            if (K.RowCount != K.ColumnCount)
                throw new ArgumentException("Matrix K must be square.");
            if (K.RowCount != b.Count)
                throw new ArgumentException("Dimension mismatch between K and b.");

            int n = K.RowCount;

            // --- キャッシュヒット: 後退代入のみ実行 (CSC + 分解をスキップ) ---
            if (cache != null && cache.TryReuse(out var cachedFactor) && cachedFactor != null)
            {
                try
                {
                    var rhsCached = b.ToArray();
                    var xCached = new double[n];
                    long _tsSubReuse = System.Diagnostics.Stopwatch.GetTimestamp();
                    cachedFactor.Solve(rhsCached, xCached);
                    SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSubReuse;
                    // 使い回した因子が今の K と合っているかを残差で確かめ、大きければ作り直す
                    // (K が変わったのに因子が古いままなら、作り直すと正しい解になる。同じ K なら作り直しても同じ解)
                    if (!CheckResidual(K, b, xCached, SolverKind.Cholesky, cache))
                        throw new InvalidOperationException("再利用した Cholesky 因子の解の残差が大きい");
                    LastSuccessfulSolver = SolverKind.Cholesky;
                    CholeskyReuseCount++;
                    return xCached;
                }
                catch
                {
                    // 再利用に失敗した場合は通常パスにフォールスルー (cache は無効化される)
                    cache.Invalidate();
                }
            }

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

            // Cholesky・LDL は対称な行列にだけ使う (片側の三角しか読まないため)
            bool symmetric = IsSymmetric(A);

            // 1) Cholesky (SPD)
            if (symmetric)
            {
                try
                {
                    long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                    var chol = SparseCholesky.Create(A, ColumnOrdering.MinimumDegreeAtPlusA);
                    FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                    long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                    chol.Solve(rhs, x);
                    SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                    CheckResidual(K, b, x, SolverKind.Cholesky, cache);
                    LastSuccessfulSolver = SolverKind.Cholesky;
                    cache?.Store(chol); // 成功した Cholesky 因子のみキャッシュ
                    return x;
                }
                catch
                {
                }
                Array.Clear(x, 0, n);
            }

            // 2) LDL (対称不定)
            if (symmetric)
            {
                try
                {
                    long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                    var ldl = SparseLDL.Create(A, ColumnOrdering.MinimumDegreeAtPlusA);
                    FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                    long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                    ldl.Solve(rhs, x);
                    SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                    CheckResidual(K, b, x, SolverKind.LDL, cache);
                    LastSuccessfulSolver = SolverKind.LDL;
                    return x;
                }
                catch
                {
                }
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

                CheckResidual(K, b, x, SolverKind.LU, cache);
                LastSuccessfulSolver = SolverKind.LU;
                return x;
            }
            catch
            {
            }
            Array.Clear(x, 0, n);

            // 4) QR (最終救済、MinimumDegreeAtA 失敗時は Natural)
            try
            {
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var qr = SparseQR.Create(A, ColumnOrdering.MinimumDegreeAtA);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                qr.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                CheckResidual(K, b, x, SolverKind.QR, cache);
                LastSuccessfulSolver = SolverKind.QR;
                return x;
            }
            catch (Exception ex)
            {
                // 並べ替えを変えて解き直す。解自体は同じなので値は変わらないが、
                // ここに落ちるのは行列の条件が悪い合図なので記録する。
                Serilog.Log.Debug(ex,
                    "[CsparseLinearSolver] MinimumDegreeAtA で分解できず、Natural で解き直します");
                long _tsFact = System.Diagnostics.Stopwatch.GetTimestamp();
                var qr = SparseQR.Create(A, ColumnOrdering.Natural);
                FactorizeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFact;

                long _tsSub = System.Diagnostics.Stopwatch.GetTimestamp();
                qr.Solve(rhs, x);
                SolveBackSubTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSub;

                CheckResidual(K, b, x, SolverKind.QR, cache);
                LastSuccessfulSolver = SolverKind.QR;
                return x;
            }
        }
    }
}
