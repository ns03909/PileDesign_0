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
        public List<(double Theta, double Moment)> Points { get; } = [];

        public MomentRotationCurve() { }
        public MomentRotationCurve(IEnumerable<(double theta, double moment)> points)
        {
            if (points == null) return;
            foreach (var (t, m) in points) Points.Add((t, m));
            Points.Sort((a, b) => a.Theta.CompareTo(b.Theta));
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
            if (Points.Count == 1) return SlopeFromOrigin(Points[0]);
            if (t <= Points[0].Theta) return SlopeFromOrigin(Points[0]);
            if (t >= Points[^1].Theta) return SafeSlope(Points[^2], Points[^1]);

            int idx = FindSegmentIndex(t);
            var a = (Theta: (idx == 0 ? 0.0 : Points[idx - 1].Theta), Moment: (idx == 0 ? 0.0 : Points[idx - 1].Moment));
            var b = Points[idx];
            return SafeSlope(a, b);
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
    }

    // 共通ベースを継承して HorizontalSoilSpring と同一経路でマッピング可能にする
    public class RotationalSpring : TwoNodeSpringElement
    {

        public ObservableCollection<RotationalSpringResult> RotationalSpringResults { get; set; } = [];

        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // 並進+Rz を剛結したい場合に使用（Ke作成は PrepareKmat 側で行う想定）
        public bool TieUx { get; set; } = true;
        public bool TieUy { get; set; } = true;
        public bool TieUz { get; set; } = true;
        public bool TieRz { get; set; } = true;
        public double Kbig { get; set; } = 1e12;

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