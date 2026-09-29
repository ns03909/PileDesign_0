using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace PileDesign.Services
{
    public static class MoveCopyValidation
    {
        // Includes potential endpoint nodes created when copying beams.
        public const int MaxGeneratedItems = 100_000;

        public static string? DescribeSplitCountProblem(int beamCount, int divisions) =>
            beamCount < 0 || divisions < 2 || (long)beamCount * (2L * divisions - 1) > MaxGeneratedItems
                ? $"分割で生成する梁・節点は合計 {MaxGeneratedItems:N0} 件以下にしてください。" : null;

        public static string? DescribeToleranceProblem(double tolerance) =>
            double.IsFinite(tolerance) && tolerance >= 0 ? null : "距離は有限の0以上の数値で入力してください。";

        public static string? DescribeCopyCountProblem(long itemsPerRepetition, int repetitions)
        {
            if (repetitions <= 0) return "コピー回数は正の整数で入力してください。";
            if (itemsPerRepetition < 0 || itemsPerRepetition > MaxGeneratedItems / (long)repetitions)
                return $"コピーで生成する杭・節点・梁は合計 {MaxGeneratedItems:N0} 件以下にしてください（梁は端点節点を含め最大3件として計算）。";
            return null;
        }

        public static string? DescribeProblem(IEnumerable<Point3D> positions,
            double dx, double dy, double dz, int repetitions)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dz))
                return "移動量 DX・DY・DZ は有限の数値で入力してください。";
            if (repetitions <= 0) return "コピー回数は正の整数で入力してください。";
            double x = dx * repetitions, y = dy * repetitions, z = dz * repetitions;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                return "移動量と回数の積が数値の範囲外です。";
            foreach (var p in positions)
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) ||
                    !double.IsFinite(p.X + x) || !double.IsFinite(p.Y + y) || !double.IsFinite(p.Z + z))
                    return "移動・コピー対象の座標または移動先座標が数値の範囲外です。";
            return null;
        }
    }
}
