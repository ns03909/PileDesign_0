using System;

namespace PileDesign.Services
{
    /// <summary>計算書の杭別沈下量。値がない欄は null として未計算を区別する。</summary>
    public static class PileSettlementTableValues
    {
        public static (double? SingleMm, double? GroupMm, double? TotalMm) Build(
            bool hasSingle, double singleM, bool hasGroup, double groupMm)
        {
            double? single = hasSingle && double.IsFinite(singleM) ? singleM * 1000.0 : null;
            double? group = hasGroup && double.IsFinite(groupMm) ? groupMm : null;
            if (single.HasValue && !double.IsFinite(single.Value)) single = null;
            double? total = single.HasValue && group.HasValue ? single + group : null;
            if (total.HasValue && !double.IsFinite(total.Value)) total = null;
            return (single, group, total);
        }
    }
}
