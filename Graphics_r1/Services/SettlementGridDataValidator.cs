using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;

namespace PileDesign.Services
{
    /// <summary>コンタ描画で必要な完全な矩形格子を組み立てる。</summary>
    public static class SettlementGridDataValidator
    {
        public static bool TryBuildGrid(
            IReadOnlyList<SettlementGridDataItem> items,
            IReadOnlyList<double> xs,
            IReadOnlyList<double> ys,
            out SettlementGridDataItem[,]? grid,
            out string? error)
        {
            grid = null;
            error = null;
            if (xs.Count == 0 || ys.Count == 0)
            {
                error = "格子の座標がありません";
                return false;
            }

            var xIndex = new Dictionary<double, int>();
            var yIndex = new Dictionary<double, int>();
            for (int i = 0; i < xs.Count; i++)
            {
                if (!double.IsFinite(xs[i]) || !xIndex.TryAdd(xs[i], i))
                {
                    error = $"X 座標が不正または重複しています: {xs[i]}";
                    return false;
                }
            }
            for (int i = 0; i < ys.Count; i++)
            {
                if (!double.IsFinite(ys[i]) || !yIndex.TryAdd(ys[i], i))
                {
                    error = $"Y 座標が不正または重複しています: {ys[i]}";
                    return false;
                }
            }

            var candidate = new SettlementGridDataItem[xs.Count, ys.Count];
            foreach (var item in items)
            {
                if (item == null || !double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
                    !double.IsFinite(item.Settlement) ||
                    !xIndex.TryGetValue(item.X, out int ix) || !yIndex.TryGetValue(item.Y, out int iy))
                {
                    error = "格子点の座標または沈下量が不正です";
                    return false;
                }
                if (candidate[ix, iy] != null)
                {
                    error = $"格子点が重複しています: X={item.X}, Y={item.Y}";
                    return false;
                }
                candidate[ix, iy] = item;
            }

            for (int ix = 0; ix < xs.Count; ix++)
                for (int iy = 0; iy < ys.Count; iy++)
                    if (candidate[ix, iy] == null)
                    {
                        error = $"格子点がありません: X={xs[ix]}, Y={ys[iy]}";
                        return false;
                    }

            grid = candidate;
            return true;
        }
    }
}
