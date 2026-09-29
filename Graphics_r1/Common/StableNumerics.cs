using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace PileDesign.Common;

internal static class StableNumerics
{
    internal static double Sum(IEnumerable<double> values)
    {
        double sum = 0, correction = 0;
        foreach (double value in values)
        {
            double next = sum + value;
            correction += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
            sum = next;
        }
        return sum + correction;
    }

    /// <summary>
    /// 2 成分の大きさ √(x² + y²)。二乗してから足すと、大きな有限の値 (1e155 程度より大きい) で途中が無限大になり、
    /// 小さな値 (1e-155 程度より小さい) では 0 に潰れる。大きさをそろえてから計算する <see cref="double.Hypot"/> を使う。
    /// 数値でない成分があれば数値でない値を返す (隠さない)。
    /// </summary>
    internal static double Norm(double x, double y) => double.Hypot(x, y);

    /// <summary>3 成分の大きさ √(x² + y² + z²)。最大の成分で割ってから二乗して足す (<see cref="Norm(double, double)"/> と同じ理由)。</summary>
    internal static double Norm(double x, double y, double z)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return double.NaN;
        double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        if (scale == 0 || double.IsInfinity(scale)) return scale;
        double a = x / scale, b = y / scale, c = z / scale;
        return scale * Math.Sqrt(a * a + b * b + c * c);
    }

    internal static double Mean(IEnumerable<double> values)
    {
        var data = values.ToArray();
        if (data.Length == 0 || data.Any(v => !double.IsFinite(v))) throw new ArgumentException("図心の計算には有限の座標が必要です。");
        double scale = data.Max(v => Math.Abs(v));
        if (scale == 0) return 0;
        return Math.Clamp(Sum(data.Select(v => (v / scale) / data.Length)), -1, 1) * scale;
    }

    // Split powers of two to avoid overflow in intermediate products/quotients.
    private static double ProductQuotient(double a, double b, double c)
    {
        if (a == 0 || b == 0) return 0;
        int ea = Math.ILogB(a), eb = Math.ILogB(b), ec = Math.ILogB(c);
        double result = Math.ScaleB(Math.ScaleB(a, -ea) * Math.ScaleB(b, -eb) / Math.ScaleB(c, -ec), ea + eb - ec);
        if (!double.IsFinite(result)) throw new ArithmeticException("荷重重心が数値の範囲外です。");
        return result;
    }

    internal static Point3D WeightedCenter(IEnumerable<(Point3D Position, double Weight)> values)
    {
        var data = values.ToArray();
        if (data.Length == 0) return new Point3D();
        if (data.Any(v => !double.IsFinite(v.Weight) || !double.IsFinite(v.Position.X) ||
            !double.IsFinite(v.Position.Y) || !double.IsFinite(v.Position.Z)))
            throw new ArgumentException("荷重重心の計算には有限の座標と荷重が必要です。");
        double weightScale = data.Max(v => Math.Abs(v.Weight));
        if (weightScale == 0) return new Point3D();
        double total = Sum(data.Select(v => v.Weight / weightScale));
        // Retain the existing convention for exactly cancelling loads.
        if (total == 0) return new Point3D();
        double Coordinate(Func<Point3D, double> coordinate)
        {
            double scale = data.Max(v => Math.Abs(coordinate(v.Position)));
            if (scale == 0) return 0;
            double numerator = Sum(data.Select(v => (coordinate(v.Position) / scale) * (v.Weight / weightScale)));
            return ProductQuotient(numerator, scale, total);
        }
        return new Point3D(Coordinate(p => p.X), Coordinate(p => p.Y), Coordinate(p => p.Z));
    }
}
