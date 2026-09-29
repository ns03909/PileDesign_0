using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭頭 2 点間の変形角の求め方と限界値。
    ///
    /// すべての杭頭の組について、鉛直変位の差を杭間の水平距離で割った角
    ///   θ = |Uz_i − Uz_j| / √((Xi−Xj)² + (Yi−Yj)²)
    /// を求め、その最大値を限界値と比べる。基礎の回転・不同沈下による変形角。
    ///
    /// 水平解析の検定 (<c>EvaluationService</c>) と沈下の検定 (<see cref="SettlementDeformationAngleEvaluator"/>) の
    /// 両方が使うので、画面の層ではなくここに置く。
    /// </summary>
    public static class PileHeadDeformationAngle
    {
        /// <summary>使用限界の変形角の既定値 1.0×10⁻³ rad (= 1/1000)。基本設定で変更できる。</summary>
        public const double DefaultServiceLimit = 1.0e-3;

        /// <summary>損傷限界の変形角の既定値 5.0×10⁻³ rad (= 1/200)。基本設定で変更できる。</summary>
        public const double DefaultDamageLimit = 5.0e-3;

        /// <summary>終局限界の変形角の既定値 7.0×10⁻³ rad (≒ 1/143)。基本設定で変更できる。</summary>
        public const double DefaultUltimateLimit = 7.0e-3;

        /// <summary>使用限界の変形角。基本設定の値を使い、0 以下 (旧いファイルで未設定) のときだけ既定値に落とす。</summary>
        public static double ServiceLimit(FundamentalInput? fundamental) => Configured(fundamental?.ServiceDeformationAngleLimit, DefaultServiceLimit);

        /// <inheritdoc cref="ServiceLimit"/>
        public static double DamageLimit(FundamentalInput? fundamental) => Configured(fundamental?.DamageDeformationAngleLimit, DefaultDamageLimit);

        /// <inheritdoc cref="ServiceLimit"/>
        public static double UltimateLimit(FundamentalInput? fundamental) => Configured(fundamental?.UltimateDeformationAngleLimit, DefaultUltimateLimit);

        private static double Configured(double? value, double fallback)
            => value is double v && v > 0 && double.IsFinite(v) ? v : fallback;

        /// <summary>
        /// 杭頭の (X, Y, 鉛直変位) から、全ペアの変形角の最大値と、その組を返す。
        /// 杭が 2 本未満、または杭間距離が 0 のときは null。
        /// </summary>
        /// <param name="heads">(杭No, X[m], Y[m], 鉛直変位[m])。符号はそのままでよい (差で使う)</param>
        public static (double Angle, int PileNoA, int PileNoB)? Max(
            IReadOnlyList<(int PileNo, double X, double Y, double Uz)> heads)
        {
            TryMax(heads, out var result, out _);
            return result;
        }

        /// <summary>異常値を検出した場合は理由を返し、検定値を返さない。</summary>
        public static bool TryMax(
            IReadOnlyList<(int PileNo, double X, double Y, double Uz)> heads,
            out (double Angle, int PileNoA, int PileNoB)? result,
            out string? invalidReason)
        {
            result = null;
            invalidReason = null;
            foreach (var head in heads)
            {
                if (!double.IsFinite(head.X) || !double.IsFinite(head.Y) || !double.IsFinite(head.Uz))
                {
                    invalidReason = $"杭No.{head.PileNo} の座標または杭頭鉛直変位が有限値ではありません";
                    return false;
                }
            }
            if (heads.Count < 2) return false;

            double maxAngle = -1.0;
            int a = 0, b = 0;

            for (int i = 0; i < heads.Count - 1; i++)
            {
                for (int j = i + 1; j < heads.Count; j++)
                {
                    double dx = heads[i].X - heads[j].X;
                    double dy = heads[i].Y - heads[j].Y;
                    double span = PileDesign.Common.StableNumerics.Norm(dx, dy);
                    if (!double.IsFinite(span))
                    {
                        invalidReason = $"杭No.{heads[i].PileNo} と杭No.{heads[j].PileNo} の水平距離が有限値ではありません";
                        return false;
                    }
                    if (PileDesign.Common.GeometryTolerance.IsZeroLength(span)) continue;   // 同じ位置の杭 (重なり) は角が定義できない

                    double angle = Math.Abs(heads[i].Uz - heads[j].Uz) / span;
                    if (!double.IsFinite(angle))
                    {
                        invalidReason = $"杭No.{heads[i].PileNo} と杭No.{heads[j].PileNo} の変形角が有限値ではありません";
                        return false;
                    }
                    if (angle > maxAngle)
                    {
                        maxAngle = angle;
                        a = heads[i].PileNo;
                        b = heads[j].PileNo;
                    }
                }
            }

            result = maxAngle < 0 ? null : (maxAngle, a, b);
            return result != null;
        }
    }
}
