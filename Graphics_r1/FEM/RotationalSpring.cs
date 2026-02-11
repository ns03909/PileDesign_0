using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesign.FEM
{
    public enum RotationalDof { Rx, Ry, Rz }
    public enum RotationalSpringMode { SingleDof, CombinedXY }

    // M-θ曲線（タプル名の有無に依存しない実装）
    public sealed class MomentRotationCurve
    {
        // デバッグ用カウンタ
        private static int _curveCreateCount = 0;
        private static int _evalTangentCount = 0;
        private static int _evalSecantCount = 0;
        private static readonly object _logLock = new();

        public List<(double Theta, double Moment)> Points { get; } = [];

        public MomentRotationCurve() { }
        public MomentRotationCurve(IEnumerable<(double theta, double moment)> points)
        {
            if (points == null) return;
            foreach (var (t, m) in points) Points.Add((t, m));
            Points.Sort((a, b) => a.Theta.CompareTo(b.Theta));

            // デバッグ: 曲線の点と各区間の接線剛性を出力（最初の10曲線のみ）
            #if DEBUG
            lock (_logLock)
            {
                _curveCreateCount++;
                if (_curveCreateCount <= 10)
                {
                    //System.Diagnostics.Debug.WriteLine($"[M-θ Curve #{_curveCreateCount}] Points={Points.Count}:");
                    for (int i = 0; i < Points.Count; i++)
                    {
                        var pt = Points[i];
                        //System.Diagnostics.Debug.WriteLine($"  [{i}] θ={pt.Theta:E6} [rad], M={pt.Moment:F1} [kNm]");
                    }
                    // 各区間の接線剛性を表示
                    //System.Diagnostics.Debug.WriteLine($"  Segment tangent stiffnesses (dM/dθ = K_rot):");
                    double prevTheta = 0.0, prevMoment = 0.0;
                    for (int i = 0; i < Points.Count; i++)
                    {
                        var (t, m) = Points[i];
                        double dTheta = t - prevTheta;
                        double dM = m - prevMoment;
                        double tangent = dTheta > 1e-12 ? dM / dTheta : 0.0;
                        //System.Diagnostics.Debug.WriteLine($"    Seg[{(i == 0 ? "origin" : (i - 1).ToString())}→{i}]: dM/dθ = {tangent:E3} [kNm/rad]");
                        prevTheta = t;
                        prevMoment = m;
                    }
                }
            }
            #endif
        }

        public double EvaluateMoment(double theta)
        {
            if (Points.Count == 0) return 0.0;
            double t = Math.Abs(theta);
            if (Points.Count == 1) return InterpFromOrigin(Points[0], t);
            if (t <= Points[0].Theta) return InterpFromOrigin(Points[0], t);
            if (t >= Points[^1].Theta) return Points[^1].Moment;

            int idx = FindSegmentIndex(t);
            var a = (Theta: (idx == 0 ? 0.0 : Points[idx - 1].Theta), Moment: (idx == 0 ? 0.0 : Points[idx - 1].Moment));
            var b = Points[idx];
            return Lerp(a, b, t);
        }

        public double EvaluateTangent(double theta)
        {
            if (Points.Count == 0) return 0.0;
            double t = Math.Abs(theta);
            double result;
            string region = "";

            if (Points.Count == 1)
            {
                result = SlopeFromOrigin(Points[0]);
                region = "single_point";
            }
            // 原点からの初期接線剛性: 最初の点が原点(0,0)の場合は次の点との傾きを使用
            else if (t <= Points[0].Theta)
            {
                if (Points[0].Theta <= 1e-12 && Points.Count >= 2)
                {
                    // 最初の点が原点の場合、原点と次の点との傾きを返す
                    result = SafeSlope(Points[0], Points[1]);
                    region = "origin→1";
                }
                else
                {
                    result = SlopeFromOrigin(Points[0]);
                    region = "slope_from_origin";
                }
            }
            else if (t >= Points[^1].Theta)
            {
                result = SafeSlope(Points[^2], Points[^1]);
                region = "above_last";
            }
            else
            {
                // 改良版: セグメント境界付近でスムーズにブレンドして不連続性を解消
                int idx = FindSegmentIndex(t);
                var a = (Theta: (idx == 0 ? 0.0 : Points[idx - 1].Theta), Moment: (idx == 0 ? 0.0 : Points[idx - 1].Moment));
                var b = Points[idx];
                double slopeCurrent = SafeSlope(a, b);

                // 境界付近のブレンド幅（セグメント長の20%）
                const double BLEND_RATIO = 0.20;
                double segLen = b.Theta - a.Theta;
                double blendWidth = segLen * BLEND_RATIO;

                // セグメント終点付近で次のセグメントとブレンド
                if (t > b.Theta - blendWidth && idx < Points.Count - 1)
                {
                    var c = Points[idx + 1];
                    double slopeNext = SafeSlope(b, c);
                    double blendT = (t - (b.Theta - blendWidth)) / blendWidth;
                    blendT = blendT * blendT * (3 - 2 * blendT); // スムーズステップ
                    result = slopeCurrent * (1 - blendT) + slopeNext * blendT;
                    region = $"blend_{idx}→{idx + 1}";
                }
                // セグメント始点付近で前のセグメントとブレンド
                else if (t < a.Theta + blendWidth && idx > 1)
                {
                    var prev = (Theta: Points[idx - 2].Theta, Moment: Points[idx - 2].Moment);
                    double slopePrev = SafeSlope(prev, a);
                    double blendT = (t - a.Theta) / blendWidth;
                    blendT = blendT * blendT * (3 - 2 * blendT); // スムーズステップ
                    result = slopePrev * (1 - blendT) + slopeCurrent * blendT;
                    region = $"blend_{idx - 1}→{idx}";
                }
                else
                {
                    result = slopeCurrent;
                    region = $"{(idx == 0 ? "origin" : (idx - 1).ToString())}→{idx}";
                }
            }

            #if DEBUG
            _evalTangentCount++;
            if (_evalTangentCount <= 30)
            {
                //System.Diagnostics.Debug.WriteLine($"[M-θ EvalTangent #{_evalTangentCount}] θ={theta:E6}, seg={region}, K_tan={result:E3}");
            }
            #endif
            return result;
        }

        // 割線剛性: M/θ（内力計算用）
        // θ→0では初期接線剛性に収束
        public double EvaluateSecant(double theta)
        {
            double t = Math.Abs(theta);
            // θがゼロに近い場合は初期接線剛性を返す（ゼロ除算回避）
            // 閾値を1e-9に引き上げて数値安定性を向上
            if (t < 1e-9)
            {
                double tan0 = EvaluateTangent(0.0);
                #if DEBUG
                _evalSecantCount++;
                if (_evalSecantCount <= 30)
                {
                    //System.Diagnostics.Debug.WriteLine($"[M-θ EvalSecant #{_evalSecantCount}] θ={theta:E6} (near zero) -> using tangent={tan0:E3}");
                }
                #endif
                return tan0;
            }
            double M = EvaluateMoment(theta);
            double secant = M / t;
            #if DEBUG
            _evalSecantCount++;
            if (_evalSecantCount <= 30)
            {
                //System.Diagnostics.Debug.WriteLine($"[M-θ EvalSecant #{_evalSecantCount}] θ={theta:E6}, M={M:F1}, K_sec={secant:E3}");
            }
            #endif
            return secant;
        }

        private static double Lerp((double Theta, double Moment) a, (double Theta, double Moment) b, double t)
        {
            double dt = b.Theta - a.Theta;
            if (Math.Abs(dt) < 1e-20) return b.Moment;
            double u = (t - a.Theta) / dt;
            return a.Moment * (1.0 - u) + b.Moment * u;
        }
        private static double SafeSlope((double Theta, double Moment) a, (double Theta, double Moment) b)
        {
            double dt = b.Theta - a.Theta;
            if (Math.Abs(dt) < 1e-20) return 0.0;
            return (b.Moment - a.Moment) / dt;
        }
        private static double InterpFromOrigin((double Theta, double Moment) p, double t)
        {
            if (p.Theta <= 0.0) return 0.0;
            return (p.Moment / p.Theta) * t;
        }
        private static double SlopeFromOrigin((double Theta, double Moment) p)
        {
            if (p.Theta <= 0.0) return 0.0;
            return p.Moment / p.Theta;
        }
        private int FindSegmentIndex(double t)
        {
            int lo = 1, hi = Points.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (t <= Points[mid].Theta) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        // グラフ用に配列へ展開（θ>=0 の定義点のみ）
        public (double[] thetas, double[] moments) ToArrays()
        {
            if (Points.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());
            var listTheta = new List<double>(Points.Count);
            var listMoment = new List<double>(Points.Count);
            foreach (var (t, m) in Points)
            {
                if (double.IsFinite(t) && double.IsFinite(m) && t >= 0.0)
                {
                    listTheta.Add(t);
                    listMoment.Add(m);
                }
            }
            return (listTheta.ToArray(), listMoment.ToArray());
        }
    }

    // 共通ベースを継承して HorizontalSoilSpring と同一経路でマッピング可能にする
    public class RotationalSpring : TwoNodeSpringElement
    {

        public List<RotationalSpringResult> RotationalSpringResults { get; set; } = [];

        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // 並進+Rz を剛結したい場合に使用（Ke作成は PrepareKmat 側で行う想定）
        public bool TieUx { get; set; } = true;
        public bool TieUy { get; set; } = true;
        public bool TieUz { get; set; } = true;
        public bool TieRz { get; set; } = true;
        [System.Text.Json.Serialization.JsonIgnore]  // JSONから古い値が読み込まれないようにする
        public double Kbig { get; set; } = 1e6;  // アーム変換後の条件数を改善するため1e6に低減

        public RotationalSpringMode Mode { get; set; } = RotationalSpringMode.CombinedXY;
        public RotationalDof Dof { get; set; } = RotationalDof.Rx;

        public MomentRotationCurve? Curve { get; set; }     // SingleDof
        public MomentRotationCurve? CurveXY { get; set; }   // CombinedXY
        public double? Ktheta { get; set; }                 // SingleDof 線形K
        public double? KthetaXY { get; set; }               // CombinedXY 線形K

        public int? PileBodyNo { get; set; }

        public bool IsNonlinear =>
            (Mode == RotationalSpringMode.SingleDof && Curve is not null) ||
            (Mode == RotationalSpringMode.CombinedXY && CurveXY is not null);

        public RotationalSpring() { }

        // 既定（CombinedXY想定）
        public RotationalSpring(string name, Node nodeI, Node nodeJ) : base(name, nodeI, nodeJ) { }

        // CombinedXY: 線形剛性指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, double kthetaXY) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.CombinedXY;
            KthetaXY = kthetaXY;
        }

        // CombinedXY: 曲線指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, MomentRotationCurve curveXY) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.CombinedXY;
            CurveXY = curveXY ?? throw new ArgumentNullException(nameof(curveXY));
        }

        // SingleDof: 線形剛性指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, RotationalDof dof, double ktheta) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.SingleDof;
            Dof = dof;
            Ktheta = ktheta;
        }

        // SingleDof: 曲線指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, RotationalDof dof, MomentRotationCurve curve) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.SingleDof;
            Dof = dof;
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }


        public void SetCurve(MomentRotationCurve? curve)
        {
            Curve = curve;
        }

        // 結果格納ラッパ
        public void SetBeamDispAndForce(bool isTan = false)
        {
            base.SetBeamDispAndForce(isTan, CumulativeDisp, CumulativeForce);
        }
        public RotationalSpring DeepCopy()
        {
            var nodeICopy = NodeI?.DeepCopy();
            var nodeJCopy = NodeJ?.DeepCopy();

            var copy = new RotationalSpring(Name, nodeICopy, nodeJCopy)
            {
                IncrementalDisp = this.IncrementalDisp?.Clone(),
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                IncrementalForce = this.IncrementalForce?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                Mode = this.Mode,
                Dof = this.Dof,
                Curve = this.Curve,            // 参照そのまま（必要ならDeepCopyへ拡張）
                CurveXY = this.CurveXY,
                Ktheta = this.Ktheta,
                KthetaXY = this.KthetaXY,
                PileBodyNo = this.PileBodyNo,
                TieUx = this.TieUx,
                TieUy = this.TieUy,
                TieUz = this.TieUz,
                TieRz = this.TieRz,
                Kbig = this.Kbig,
            };

            if (this.KeTan != null) copy.SetKeFromMatrix(this.KeTan, isTan: true);
            if (this.KeSec != null) copy.SetKeFromMatrix(this.KeSec, isTan: false);

            return copy;
        }

        private void SetKeFromMatrix(Matrix<double> ke, bool isTan)
        {
            double kx = ke[0, 0];
            double ky = ke[1, 1];
            double kz = ke[2, 2];
            double kRx = ke[3, 3];
            double kRy = ke[4, 4];
            double kRz = ke[5, 5];
            base.SetKe(kx, ky, kz, kRx, kRy, kRz, isTan);
        }
    }
}