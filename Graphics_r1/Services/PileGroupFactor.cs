using System;

namespace PileDesign.Services
{
    internal static class PileGroupFactor
    {
        public static bool IsValidSpacingRatio(double value) => double.IsFinite(value) && value > 0;
        public static bool IsValidFactor(double value) => double.IsFinite(value) && value > 0 && value <= 1;

        public static bool TryGetPileGroupFactor(int totalPileCount, double pileSpacingDiaRatio, out double factor)
        {
            factor = double.NaN;
            if (totalPileCount <= 0 || !IsValidSpacingRatio(pileSpacingDiaRatio)) return false;
            double e = 1.2 / Math.Pow(totalPileCount, 0.65 / pileSpacingDiaRatio);
            double calculated = Math.Min(Math.Pow(e, 4.0 / 3.0), 1);
            if (!IsValidFactor(calculated)) return false;
            factor = calculated;
            return true;
        }

        public static double GetPileGroupFactor(int totalPileCount, double pileSpacingDiaRatio)
        {
            if (!TryGetPileGroupFactor(totalPileCount, pileSpacingDiaRatio, out double factor))
                throw new ArgumentOutOfRangeException(nameof(pileSpacingDiaRatio), "杭本数と R/B は正の有限値にしてください。");
            return factor;
        }
    }
}
