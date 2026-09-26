using MathNet.Numerics.LinearAlgebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;

namespace TestProject1
{
    /// <summary>
    /// 連立方程式の解法 (<see cref="CsparseLinearSolver"/>) が、求めた解を K x ≈ b で確かめてから返すこと。
    ///
    /// Cholesky 分解は行列の片側の三角しか読まないので、非対称な行列に使うと、例外にならずに
    /// <b>別の (対称化した) 方程式の解</b>を返す。以前は対称かどうかに関係なく最初に Cholesky を試し、
    /// 分解と代入が例外なく終われば、その解をそのまま返していた。
    /// </summary>
    [TestClass]
    public class LinearSolverVerificationTests
    {
        private static double Residual(Matrix<double> k, Vector<double> b, double[] x)
            => (k * Vector<double>.Build.DenseOfArray(x) - b).L2Norm() / b.L2Norm();

        [TestMethod]
        public void ANonSymmetricMatrixIsSolvedCorrectly()
        {
            // 正定値だが非対称 (対称部は正定値なので Cholesky は通ってしまう)
            var k = Matrix<double>.Build.SparseOfArray(new double[,]
            {
                { 4, 1, 0 },
                { 3, 5, 1 },
                { 0, 2, 6 },
            });
            var b = Vector<double>.Build.DenseOfArray([1.0, 2.0, 3.0]);

            var x = CsparseLinearSolver.Solve(k, b, isSpd: false);
            Assert.IsTrue(Residual(k, b, x) < 1e-10, $"非対称な行列の解が K x = b を満たしません (残差 {Residual(k, b, x):E2})");
            Assert.AreNotEqual(CsparseLinearSolver.SolverKind.Cholesky, CsparseLinearSolver.LastSuccessfulSolver,
                "非対称な行列に Cholesky 分解を使っています");
        }

        [TestMethod]
        public void ASymmetricPositiveDefiniteMatrixStillUsesCholesky()
        {
            var k = Matrix<double>.Build.SparseOfArray(new double[,]
            {
                { 4, 1, 0 },
                { 1, 5, 2 },
                { 0, 2, 6 },
            });
            var b = Vector<double>.Build.DenseOfArray([1.0, 2.0, 3.0]);

            var x = CsparseLinearSolver.Solve(k, b, isSpd: false);
            Assert.IsTrue(Residual(k, b, x) < 1e-12);
            Assert.AreEqual(CsparseLinearSolver.SolverKind.Cholesky, CsparseLinearSolver.LastSuccessfulSolver,
                "対称正定値の行列で Cholesky 分解を使っていません (速度が落ちる)");
        }
    }
}
