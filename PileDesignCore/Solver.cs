using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace SolveLinearEquations
{
	public class Class1
	{
		// コンストラクタ
		public Class1(string[] args)
        {
            // 行列Aを定義
            DenseMatrix A = DenseMatrix.OfArray(new double[,]
            {
                { 2, 3 },
                { 4, 6 }
            });
            // ベクトルBを定義
            DenseVector B = DenseVector.OfArray(new double[]
            {
                5,
                10
            });
            // 最小二乗法で連立方程式を解く
            MathNet.Numerics.LinearAlgebra.Vector<double> X = A.PseudoInverse().Multiply(B);
            // 結果を表示
            Console.WriteLine("Solution: ");
            for (int i = 0; i < X.Count; i++)
            {
                Console.WriteLine($"x{i + 1} = {X[i]}");
            }
        }
    }
}