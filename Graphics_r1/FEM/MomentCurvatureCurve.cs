using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    /// <summary>M-φ 曲線の 1 点。保存できる形にするために要る。</summary>
    public sealed class MomentCurvaturePoint
    {
        public double Phi { get; set; }
        public double Moment { get; set; }
    }

    // φ[rad/m] - M[kNm] 曲線（昇順点列、線形補間）
    public sealed class MomentCurvatureCurve
    {
        /// <summary>
        /// 曲線の点。<b>そのままでは保存できない</b>ので <see cref="CurvePoints"/> 経由で読み書きする。
        /// 理由は <see cref="MomentRotationCurve.Points"/> と同じ
        /// (ValueTuple の中身はフィールドで、既定では直列化されない)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<(double Phi, double Moment)> Points { get; } = new();

        /// <summary>保存用の点列。<see cref="Points"/> と中身は同じ。</summary>
        public List<MomentCurvaturePoint> CurvePoints
        {
            get
            {
                var list = new List<MomentCurvaturePoint>(Points.Count);
                foreach (var (phi, m) in Points) list.Add(new MomentCurvaturePoint { Phi = phi, Moment = m });
                return list;
            }
            set
            {
                Points.Clear();
                if (value == null) return;
                foreach (var p in value) Points.Add((p.Phi, p.Moment));
                Points.Sort((a, b) => a.Phi.CompareTo(b.Phi));
            }
        }

        // 2026-05-06: post-yield (φ > φ_last) で K_tan が 0 または微小になり Newton 方向が不定に
        // なる問題対策。降伏時割線剛性 (= M_last / φ_last) の 1% を post-yield 接線勾配の下限とする。
        // 自然な最終セグメント勾配 (strain hardening) がこの下限より大きければそちらを優先。
        // 物理的影響は微小で、p-y curve の v22 修正および M-θ curve の同方針修正と整合。
        private const double PostYieldSlopeRatio = 0.01;

        // 末尾点の post-yield 接線勾配の下限値 = 0.01 × (M_last / φ_last)。
        // Points[^1].Phi == 0 の場合は 0 (退化曲線)。
        private double GetPostYieldSlopeFloor()
        {
            if (Points == null || Points.Count == 0) return 0.0;
            double pLast = Points[^1].Phi;
            if (pLast <= 0.0) return 0.0;
            double secantAtYield = Points[^1].Moment / pLast;
            return PostYieldSlopeRatio * secantAtYield;
        }

        // 点列が φ ≥ 0 のみで定義されている場合に true。
        // この場合、負の φ に対しては原点対称（奇関数）として |φ| で評価し sign(φ) を乗じる。
        // counter-loading（βU=-1 等）で杭体が負の曲率側へ振れても、正側と同じ塑性挙動
        // （降伏後剛性低下）を反映させ、Newton 反復のチャタリングを防ぐ。
        private readonly bool _isOneSided;

        // 各セグメントの傾き（コンストラクタで事前計算）。
        // EvaluateTangent は NR 反復で梁ごとに毎回呼ばれるため、呼び出し毎の配列確保・再計算を避ける。
        // ファイバーモデル M-φ オプションで点数が 4 → 50 程度に増えても評価コストが増えないようにする。
        private readonly double[] _slopes = [];

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

            // セグメント傾きの事前計算（EvaluateTangent 用）
            if (Points.Count >= 2)
            {
                _slopes = new double[Points.Count - 1];
                for (int i = 0; i < Points.Count - 1; i++)
                {
                    double denom = Points[i + 1].Phi - Points[i].Phi;
                    _slopes[i] = Math.Abs(denom) <= 1e-15 ? 0.0 : (Points[i + 1].Moment - Points[i].Moment) / denom;
                }
            }
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
                // post-yield 外挿: 自然な最終セグメント勾配と「降伏時割線×1%」の大きい方を採用。
                // 末尾点 (p1, m1) を起点に外挿して連続性を保つ。
                var (p0, m0) = Points[^2];
                var (p1, m1) = Points[^1];
                double denom = (p1 - p0);
                if (Math.Abs(denom) <= 0.0) return m1;
                double naturalSlope = (m1 - m0) / denom;
                double extrapSlope = Math.Max(naturalSlope, GetPostYieldSlopeFloor());
                return m1 + extrapSlope * (phi - p1);
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

            // 区分定数: M(φ) が区分線形なので dM/dφ はセグメントの傾き（定数、コンストラクタで事前計算）。
            // ブレンドすると EvaluateMoment（ブレンドなし）と不整合になり
            // K_tan ≠ dF/dd → Newton-Raphson 収束停滞の原因になる。
            for (int i = 0; i < Points.Count - 1; i++)
            {
                if (lookup >= Points[i].Phi && lookup <= Points[i + 1].Phi)
                    return _slopes[i];
            }

            // 範囲外の場合は端部の傾きを使用
            // post-yield (lookup > Points[^1].Phi): 自然な最終セグメント勾配と「降伏時割線×1%」の大きい方。
            // EvaluateMomentCore の post-yield 外挿と整合させ K_tan = dM/dφ を保つ。
            if (lookup < Points[0].Phi) return _slopes[0];
            return Math.Max(_slopes[^1], GetPostYieldSlopeFloor());
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