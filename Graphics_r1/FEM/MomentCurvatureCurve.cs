using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    // φ[rad/m] - M[kNm] 曲線（昇順点列、線形補間）
    public sealed class MomentCurvatureCurve
    {
        public List<(double Phi, double Moment)> Points { get; } = new();

        public MomentCurvatureCurve() { }

        public MomentCurvatureCurve(IEnumerable<(double phi, double moment)> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            // ソートして、phi がほぼ同じ点はマージする（数値ノイズ対策）
            var sorted = points.OrderBy(p => p.phi).ToList();
            if (sorted.Count == 0) return;

            const double eps = 1e-12;
            var merged = new List<(double Phi, double Moment)>(sorted.Count);
            double curPhi = sorted[0].phi;
            double curMoment = sorted[0].moment;
            int count = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                var (p, m) = sorted[i];
                if (Math.Abs(p - curPhi) <= eps)
                {
                    // phi が同値に近い -> モーメントを平均化して集約
                    curMoment += m;
                    count++;
                }
                else
                {
                    merged.Add((curPhi, curMoment / count));
                    curPhi = p;
                    curMoment = m;
                    count = 1;
                }
            }
            // 最後のグループを追加
            merged.Add((curPhi, curMoment / count));

            // 最低でも2点が必要（1点なら EvaluateTangent で 0 を返す）
            Points = merged;
        }

        public double EvaluateMoment(double phi)
        {
            if (Points == null || Points.Count == 0) return 0.0;
            if (Points.Count == 1) return Points[0].Moment;

            for (int i = 0; i < Points.Count - 1; i++)
            {
                var (p0, m0) = Points[i];
                var (p1, m1) = Points[i + 1];
                if (phi >= p0 && phi <= p1)
                {
                    double denom = (p1 - p0);
                    if (Math.Abs(denom) <= 0.0) return 0.5 * (m0 + m1); // 安全化
                    double r = (phi - p0) / denom;
                    return m0 + r * (m1 - m0);
                }
            }

            // 端部外挿（直線）
            if (phi < Points[0].Phi)
            {
                var (p0, m0) = Points[0];
                var (p1, m1) = Points[1];
                double denom = (p1 - p0);
                if (Math.Abs(denom) <= 0.0) return m0;
                double k = (m1 - m0) / denom;
                return m0 + k * (phi - p0);
            }
            else
            {
                var (p0, m0) = Points[^2];
                var (p1, m1) = Points[^1];
                double denom = (p1 - p0);
                if (Math.Abs(denom) <= 0.0) return m1;
                double k = (m1 - m0) / denom;
                return m0 + k * (phi - p0);
            }
        }

        // dM/dφ（接線剛性）: EI_eff として扱える
        // 改良版: セグメント境界付近でスムーズにブレンドして不連続性を解消
        public double EvaluateTangent(double phi)
        {
            if (Points == null || Points.Count <= 1)
            {
                return 0.0;
            }

            // 各セグメントの傾きを事前計算
            var slopes = new double[Points.Count - 1];
            for (int i = 0; i < Points.Count - 1; i++)
            {
                var (p0, m0) = Points[i];
                var (p1, m1) = Points[i + 1];
                double denom = p1 - p0;
                slopes[i] = Math.Abs(denom) <= 1e-15 ? 0.0 : (m1 - m0) / denom;
            }

            // 境界付近のブレンド幅（セグメント長の20%）
            const double BLEND_RATIO = 0.20;

            for (int i = 0; i < Points.Count - 1; i++)
            {
                var (p0, _) = Points[i];
                var (p1, _) = Points[i + 1];
                if (phi >= p0 && phi <= p1)
                {
                    double segLen = p1 - p0;
                    double blendWidth = segLen * BLEND_RATIO;

                    // セグメント中央付近: そのセグメントの傾きをそのまま使用
                    if (phi >= p0 + blendWidth && phi <= p1 - blendWidth)
                    {
                        return slopes[i];
                    }

                    // セグメント始点付近: 前のセグメントとブレンド
                    if (phi < p0 + blendWidth && i > 0)
                    {
                        double t = (phi - p0) / blendWidth; // 0→1
                        t = t * t * (3 - 2 * t); // スムーズステップ補間
                        double blended = slopes[i - 1] * (1 - t) + slopes[i] * t;
                        return blended;
                    }

                    // セグメント終点付近: 次のセグメントとブレンド
                    if (phi > p1 - blendWidth && i < Points.Count - 2)
                    {
                        double t = (phi - (p1 - blendWidth)) / blendWidth; // 0→1
                        t = t * t * (3 - 2 * t); // スムーズステップ補間
                        double blended = slopes[i] * (1 - t) + slopes[i + 1] * t;
                        return blended;
                    }

                    // ブレンド対象がない場合（最初・最後のセグメント端）
                    return slopes[i];
                }
            }

            // 範囲外の場合は端部の傾きを使用
            double result = (phi < Points[0].Phi) ? slopes[0] : slopes[^1];
            return result;
        }

        // 割線剛性: M/φ（内力計算用）
        // φ→0では初期接線剛性に収束
        public double EvaluateSecant(double phi)
        {
            double p = Math.Abs(phi);
            // φがゼロに近い場合は初期接線剛性を返す（ゼロ除算回避）
            // 閾値を1e-9に引き上げて数値安定性を向上
            if (p < 1e-9)
            {
                double tan0 = EvaluateTangent(0.0);
                return tan0;
            }
            double M = EvaluateMoment(phi);
            double secant = M / p;
            return secant;
        }
    }
}