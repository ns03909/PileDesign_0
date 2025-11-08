using MathNet.Numerics.LinearAlgebra;
using System;

namespace PileDesign.FEM
{
    // 節点ばね
    public class NodeSpring(double kx, double ky, double kz, double kxx, double kyy, double kzz)
    {
        public double Kx { get; set; } = kx;
        public double Ky { get; set; } = ky;
        public double Kz { get; set; } = kz;
        public double Kxx { get; set; } = kxx;
        public double Kyy { get; set; } = kyy;
        public double Kzz { get; set; } = kzz;


        public Vector<double> GetVector()
        {
            // より簡潔かつ効率的に6成分を配列でまとめて生成
            return Vector<double>.Build.DenseOfArray([Kx, Ky, Kz, Kxx, Kyy, Kzz]);
        }
        /// <summary>
        /// インデックスで成分を取得する
        /// 0:kx, 1:ky, 2:kz, 3:kxx, 4:kyy, 5:kzz
        /// </summary>
        public double GetByIndex(int index) => index switch
        {
            0 => Kx,
            1 => Ky,
            2 => Kz,
            3 => Kxx,
            4 => Kyy,
            5 => Kzz,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };


    }
}
