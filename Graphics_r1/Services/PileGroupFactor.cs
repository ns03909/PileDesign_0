using System;

namespace PileDesign.Services
{
    internal static class PileGroupFactor
    {
        public static double GetPileGroupFactor(int totalPileCount, double pileSpacingDiaRatio)
        {
            double e = 1.2 / Math.Pow(totalPileCount, 0.65 / pileSpacingDiaRatio);
            return Math.Min(Math.Pow(e, 4.0 / 3.0), 1);
        }
    }
}
