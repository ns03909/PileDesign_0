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
        public double EvaluateTangent(double phi)
        {
            if (Points == null || Points.Count <= 1)
            {
                System.Diagnostics.Debug.WriteLine($"EvaluateTangent: phi={phi:E6}, returning 0.0 (no points)");
                return 0.0;
            }

            for (int i = 0; i < Points.Count - 1; i++)
            {
                var (p0, m0) = Points[i];
                var (p1, m1) = Points[i + 1];
                if (phi >= p0 && phi <= p1)
                {
                    double denom = (p1 - p0);
                    if (Math.Abs(denom) <= 0.0) return 0.0; // 安全化: 重複phiは勾配0扱い
                    return (m1 - m0) / denom;
                }
            }

            var (q0, n0) = Points[0];
            var (q1, n1) = Points[1];
            double denom0 = (q1 - q0);
            double k0 = Math.Abs(denom0) <= 0.0 ? 0.0 : (n1 - n0) / denom0;

            var (qn0, nn0) = Points[^2];
            var (qn1, nn1) = Points[^1];
            double denomn = (qn1 - qn0);
            double kn = Math.Abs(denomn) <= 0.0 ? 0.0 : (nn1 - nn0) / denomn;

            return (phi < Points[0].Phi) ? k0 : kn;
        }
    }
}