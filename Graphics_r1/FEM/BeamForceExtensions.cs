using MathNet.Numerics.LinearAlgebra;

namespace PileDesign.FEM
{
    /// <summary>
    /// BeamForce に関する拡張メソッド群。
    /// GetEnd3Vector はビーム端点の 3 成分ベクトル（力 or モーメントの
    /// 表示比率）を返します。以前 MainWindow 内にあったロジックを移植。
    /// </summary>
    public static class BeamForceExtensions
    {
        /// <summary>
        /// BeamForce から端点ごとの 3 成分ベクトルを取得します。
        /// </summary>
        /// <param name="bf">対象 BeamForce（null はゼロベクトルとして扱う）</param>
        /// <param name="isMoment">モーメント成分を取得する場合 true</param>
        /// <param name="isIend">I端なら true、J端なら false</param>
        /// <param name="derivedType">"Mh" / "Fh" 等の派生タイプ（省略可）</param>
        /// <returns>長さ3 の Vector&lt;double&gt;</returns>
        public static Vector<double> GetEnd3Vector(this BeamForce bf, bool isMoment, bool isIend, string derivedType)
        {
            if (bf == null)
            {
                return Vector<double>.Build.Dense(3, 0.0);
            }

            int baseIdx = isIend ? 0 : 6;
            double fx = bf.GetByIndex(baseIdx + 0);
            double fy = bf.GetByIndex(baseIdx + 1);
            double fz = bf.GetByIndex(baseIdx + 2);
            double mx = bf.GetByIndex(baseIdx + 3);
            double my = bf.GetByIndex(baseIdx + 4);
            double mz = bf.GetByIndex(baseIdx + 5);

            if (!string.IsNullOrEmpty(derivedType))
            {
                if (derivedType == "Mh")
                {
                    // 曲げ合成: My, Mz の比率を使う（Mxは無視）
                    return Vector<double>.Build.DenseOfArray(new double[] { 0.0, mz, my });
                }
                else if (derivedType == "Fh")
                {
                    // 水平力合成: Fx, Fy の比率を使う（Fzは無視）
                    return Vector<double>.Build.DenseOfArray(new double[] { 0.0, fy, fz });
                }
            }

            if (isMoment)
            {
                // Moment: [Mx, Mz, My] （従来ロジックに合わせる）
                return Vector<double>.Build.DenseOfArray(new double[] { mx, mz, my });
            }
            else
            {
                // Force: [Fx, Fy, Fz]
                return Vector<double>.Build.DenseOfArray(new double[] { fx, fy, fz });
            }
        }
    }
}