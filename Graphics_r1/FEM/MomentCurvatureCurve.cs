using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    // φ[rad/m] - M[kNm] 曲線（昇順点列、線形補間）
    public sealed class MomentCurvatureCurve
    {
        public List<(double Phi, double Moment)> Points { get; } = new();

        // 点列が φ ≥ 0 のみで定義されている場合に true。
        // この場合、負の φ に対しては原点対称（奇関数）として |φ| で評価し sign(φ) を乗じる。
        // counter-loading（βU=-1 等）で杭体が負の曲率側へ振れても、正側と同じ塑性挙動
        // （降伏後剛性低下）を反映させ、Newton 反復のチャタリングを防ぐ。
        private readonly bool _isOneSided;

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

            // 全点が φ ≥ -ε なら片側定義とみなす
            _isOneSided = Points.All(p => p.Phi >= -eps);
        }

        public double EvaluateMoment(double phi)
        {
            if (Points == null || Points.Count == 0) return 0.0;
            // 片側定義曲線に対しては奇関数として評価（負 φ も塑性域を正しく反映）
            if (_isOneSided && phi < 0.0)
                return -EvaluateMomentCore(-phi);
            return EvaluateMomentCore(phi);
        }

        private double EvaluateMomentCore(double phi)
        {
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

        /// <summary>
        /// 現在の φ が属するセグメントのインデックスを返す (0 起点)。
        /// 0: Points[0]→Points[1] 区間 (通常は弾性〜Mcr), 1: Points[1]→Points[2] 区間 (ひび割れ後), ...
        /// Points が 2 点未満なら -1。範囲外 (φ < Points[0] or φ > Points[^1]) は端点セグメントを返す。
        /// </summary>
        public int GetSegmentIndex(double phi)
        {
            if (Points == null || Points.Count < 2) return -1;
            double lookup = (_isOneSided && phi < 0.0) ? -phi : phi;
            for (int i = 0; i < Points.Count - 1; i++)
            {
                if (lookup >= Points[i].Phi && lookup <= Points[i + 1].Phi)
                    return i;
            }
            return lookup < Points[0].Phi ? 0 : Points.Count - 2;
        }

        // dM/dφ（接線剛性）: EI_eff として扱える
        // 改良版: セグメント境界付近でスムーズにブレンドして不連続性を解消
        public double EvaluateTangent(double phi)
        {
            if (Points == null || Points.Count <= 1)
            {
                return 0.0;
            }

            // 片側定義曲線に対しては |φ| で検索（負 φ でも降伏後剛性を正しく返す）。
            // 奇関数 M(−φ)=−M(φ) の微分は偶関数 → 接線剛性は |φ| 依存で OK。
            double lookup = (_isOneSided && phi < 0.0) ? -phi : phi;

            // 各セグメントの傾きを事前計算
            var slopes = new double[Points.Count - 1];
            for (int i = 0; i < Points.Count - 1; i++)
            {
                var (p0, m0) = Points[i];
                var (p1, m1) = Points[i + 1];
                double denom = p1 - p0;
                slopes[i] = Math.Abs(denom) <= 1e-15 ? 0.0 : (m1 - m0) / denom;
            }

            // 区分定数: M(φ) が区分線形なので dM/dφ はセグメントの傾き（定数）。
            // ブレンドすると EvaluateMoment（ブレンドなし）と不整合になり
            // K_tan ≠ dF/dd → Newton-Raphson 収束停滞の原因になる。
            for (int i = 0; i < Points.Count - 1; i++)
            {
                if (lookup >= Points[i].Phi && lookup <= Points[i + 1].Phi)
                    return slopes[i];
            }

            // 範囲外の場合は端部の傾きを使用
            double result = (lookup < Points[0].Phi) ? slopes[0] : slopes[^1];
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
            double secant = Math.Abs(M) / p;
            return secant;
        }
    }
}