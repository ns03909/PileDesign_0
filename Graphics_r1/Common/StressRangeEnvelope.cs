using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Common;

/// <summary>
/// N-M 図の「応力範囲」の包絡線。解析結果の点 (軸力, 曲げモーメント) をすべて囲む最も外側の多角形 (凸包) を作る。
///
/// <para>実務の計算書では、地震時の点の散らばりを点線で囲んで「設計用地震時応力範囲」として示すことが多い。
/// 点だけだと、どの点が耐力曲線に最も近いか・範囲がどこまで広がっているかを読み取りにくい。</para>
///
/// <para>画面のグラフ (<c>GraphViewModel</c> の N-M 図・定着部の N-M 図) と計算書の N-M 図で同じものを使う。</para>
/// </summary>
public static class StressRangeEnvelope
{
    /// <summary>包絡線の凡例に添える語。</summary>
    public const string LegendSuffix = "応力範囲";

    /// <summary>
    /// 点を囲む凸包の頂点列。反時計回りで、多角形のときは最初の点を最後にもう一度置いて閉じる。
    /// 数でない点は除く。異なる点が 1 つ以下なら空 (囲むものが無い)。すべて一直線上なら両端の 2 点 (線分)。
    /// </summary>
    public static (double[] X, double[] Y) Hull(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);

        int n = Math.Min(xs.Count, ys.Count);
        var points = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++)
        {
            if (double.IsFinite(xs[i]) && double.IsFinite(ys[i])) points.Add((xs[i], ys[i]));
        }
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count <= 1) return ([], []);

        // Andrew の単調連鎖法。一直線上に並ぶ途中の点は頂点にしない (外積 <= 0 で外す)
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var hull = new List<(double X, double Y)>(sorted.Count * 2);
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lowerCount = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lowerCount && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        // 最後の点は最初の点と同じ (閉じている)

        if (hull.Count <= 3)
        {
            // 一直線上: 下側の連鎖と上側の連鎖が同じ 2 点を往復するだけ。両端の線分にする
            var a = sorted[0];
            var b = sorted[^1];
            return ([a.X, b.X], [a.Y, b.Y]);
        }
        return (hull.Select(p => p.X).ToArray(), hull.Select(p => p.Y).ToArray());
    }

    /// <summary>
    /// 包絡線を図に描く。線は点と同じ色の点線。囲むものが無ければ何もしないで null。
    /// </summary>
    public static ScottPlot.Plottables.Scatter? AddTo(ScottPlot.Plot plot, IReadOnlyList<double> xs, IReadOnlyList<double> ys,
        ScottPlot.Color color, string legend)
    {
        ArgumentNullException.ThrowIfNull(plot);
        var (hx, hy) = Hull(xs, ys);
        if (hx.Length < 2) return null;
        var line = plot.Add.ScatterLine(hx, hy);
        line.LegendText = legend;
        line.LineColor = color;
        line.LineWidth = 1.5f;
        line.LinePattern = ScottPlot.LinePattern.Dotted;
        line.MarkerSize = 0;
        return line;
    }
}
