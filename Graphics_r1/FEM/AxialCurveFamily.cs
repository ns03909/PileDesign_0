using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    // 軸力 N に依存する M-φ/M-θ 曲線ファミリから、指定 N の曲線を解決（最近傍/線形内挿）
    public static class AxialCurveFamily
    {
        // φ[rad/m] - M[kNm]
        public static MomentCurvatureCurve? ResolveMPhi(object familyObj, double N)
        {
            var samples = ReadFamily(familyObj,
                xName: "Phi", yName: "Moment",
                tupleAlt: ("phi", "moment"));
            if (samples == null || samples.Count == 0) return null;
            var pts = InterpolateByN(samples, N);
            return new MomentCurvatureCurve(pts.Select(p => (p.x, p.y)));
        }

        // θ[rad] - M[kNm]（回転ばね用。現時点で未使用なら残しておいて問題なし）
        public static MomentRotationCurve? ResolveMTheta(object familyObj, double N)
        {
            var samples = ReadFamily(familyObj,
                xName: "Theta", yName: "Moment",
                tupleAlt: ("theta", "moment"));
            if (samples == null || samples.Count == 0) return null;
            var pts = InterpolateByN(samples, N);
            return new MomentRotationCurve(pts.Select(p => (p.x, p.y)));
        }

        // familyObj = IEnumerable の各要素: { double N; IEnumerable<Point> Points; }
        // Point は (double x,double y) または { xName, yName } を想定
        private static List<(double N, List<(double x, double y)> points)>? ReadFamily(
            object familyObj, string xName, string yName, (string x, string y) tupleAlt)
        {
            if (familyObj is not System.Collections.IEnumerable seq) return null;

            var list = new List<(double N, List<(double x, double y)> points)>();

            foreach (var it in seq)
            {
                if (it is null) continue;

                var t = it.GetType();
                var nProp = t.GetProperty("N") ?? t.GetProperty("Axial") ?? t.GetProperty("Force");
                if (nProp?.GetValue(it) is not IConvertible ncv) continue;
                double Nval = Convert.ToDouble(ncv);

                var ptsObj = t.GetProperty("Points")?.GetValue(it) ?? t.GetProperty("Curve")?.GetValue(it);
                if (ptsObj is null) continue;

                var pts = new List<(double x, double y)>();

                // ValueTuple 直受け
                if (ptsObj is IEnumerable<(double, double)> tupleSeq)
                {
                    foreach (var (x, y) in tupleSeq) pts.Add((x, y));
                }
                else if (ptsObj is System.Collections.IEnumerable pseq)
                {
                    foreach (var p in pseq)
                    {
                        if (p is ValueTuple<double, double> tv)
                        {
                            pts.Add((tv.Item1, tv.Item2));
                            continue;
                        }
                        var tp = p?.GetType();
                        var xp = tp?.GetProperty(xName) ?? tp?.GetProperty(tupleAlt.x);
                        var yp = tp?.GetProperty(yName) ?? tp?.GetProperty(tupleAlt.y);
                        if (xp?.GetValue(p) is IConvertible xcv && yp?.GetValue(p) is IConvertible ycv)
                        {
                            pts.Add((Convert.ToDouble(xcv), Convert.ToDouble(ycv)));
                        }
                    }
                }

                if (pts.Count >= 2)
                {
                    list.Add((Nval, pts.OrderBy(a => a.x).ToList()));
                }
            }

            return list.OrderBy(s => s.N).ToList();
        }

        // N に対して、最近傍/区間内線形内挿で曲線点列を生成
        private static List<(double x, double y)> InterpolateByN(
            List<(double N, List<(double x, double y)> points)> samples, double N)
        {
            if (samples.Count == 1) return samples[0].points;

            // ちょうど一致 or 区間内検索
            int idx = samples.FindIndex(s => s.N >= N);
            if (idx <= 0) return samples[0].points;
            if (idx < samples.Count && Math.Abs(samples[idx].N - N) < 1e-12) return samples[idx].points;

            int i1 = idx - 1;
            int i2 = idx;
            var (N1, P1) = samples[i1];
            var (N2, P2) = samples[i2];
            double r = (N - N1) / (N2 - N1);

            // x軸の共通節点集合を作り、各曲線で y を線形内挿 → N 方向に線形内挿
            var xs = P1.Select(p => p.x).Union(P2.Select(p => p.x)).Distinct().OrderBy(x => x).ToList();

            static double EvalY(List<(double x, double y)> pts, double x)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var (x0, y0) = pts[i];
                    var (x1, y1) = pts[i + 1];
                    if (x >= x0 && x <= x1)
                    {
                        double rr = (x - x0) / (x1 - x0);
                        return y0 + rr * (y1 - y0);
                    }
                }
                // 端部外挿
                if (x <= pts[0].x)
                {
                    var (x0, y0) = pts[0];
                    var (x1, y1) = pts[1];
                    return y0 + (y1 - y0) / (x1 - x0) * (x - x0);
                }
                else
                {
                    var (xa, ya) = pts[^2];
                    var (xb, yb) = pts[^1];
                    return ya + (yb - ya) / (xb - xa) * (x - xa);
                }
            }

            var res = new List<(double x, double y)>(xs.Count);
            foreach (var x in xs)
            {
                double y1 = EvalY(P1, x);
                double y2 = EvalY(P2, x);
                res.Add((x, y1 + r * (y2 - y1)));
            }
            return res;
        }
    }
}