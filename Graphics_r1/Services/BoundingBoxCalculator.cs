using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;
using System;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭配置の境界ボックス計算ユーティリティ
    /// </summary>
    public static class BoundingBoxCalculator
    {
        /// <summary>
        /// 境界ボックスを表す構造体
        /// </summary>
        public struct BoundingBox
        {
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }

            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;
        }

        /// <summary>
        /// 杭配置アイテムから境界ボックスを計算
        /// </summary>
        /// <param name="pileLayoutItems">杭配置アイテムのコレクション</param>
        /// <param name="margin">境界への追加マージン（オプション）</param>
        /// <returns>計算された境界ボックス</returns>
        public static BoundingBox Calculate(IEnumerable<PileLayoutDataItem> pileLayoutItems, double margin = 0.0)
        {
            if (!double.IsFinite(margin) || margin < 0) throw new ArgumentException("余白は有限の0以上の値にしてください。", nameof(margin));
            if (pileLayoutItems == null || !pileLayoutItems.Any())
            {
                return new BoundingBox
                {
                    MinX = 0,
                    MaxX = 0,
                    MinY = 0,
                    MaxY = 0
                };
            }

            double maxX = double.MinValue;
            double minX = double.MaxValue;
            double maxY = double.MinValue;
            double minY = double.MaxValue;

            foreach (var pileLayoutDataItem in pileLayoutItems)
            {
                double x = pileLayoutDataItem.Point3D.X;
                double y = pileLayoutDataItem.Point3D.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("杭の平面座標は有限の値にしてください。");

                if (x > maxX) maxX = x;
                if (x < minX) minX = x;
                if (y > maxY) maxY = y;
                if (y < minY) minY = y;
            }

            var result = new BoundingBox
            {
                MinX = minX - margin,
                MaxX = maxX + margin,
                MinY = minY - margin,
                MaxY = maxY + margin
            };
            if (!double.IsFinite(result.MinX) || !double.IsFinite(result.MaxX) || !double.IsFinite(result.MinY) || !double.IsFinite(result.MaxY) ||
                !double.IsFinite(result.Width) || !double.IsFinite(result.Height) || result.Width < 0 || result.Height < 0)
                throw new ArgumentException("平面範囲の計算が数値の範囲外です。");
            return result;
        }

        /// <summary>
        /// 境界ボックスの中心点を計算
        /// </summary>
        public static (double X, double Y) GetCenter(BoundingBox box)
        {
            return ((box.MinX + box.MaxX) / 2, (box.MinY + box.MaxY) / 2);
        }

        /// <summary>
        /// 杭配置アイテムの平均座標を計算
        /// </summary>
        public static (double X, double Y) CalculateAverageCenter(IEnumerable<PileLayoutDataItem> pileLayoutItems)
        {
            if (pileLayoutItems == null || !pileLayoutItems.Any())
                return (0, 0);

            var xs = pileLayoutItems.Select(p => p.Point3D.X);
            var ys = pileLayoutItems.Select(p => p.Point3D.Y);

            return (xs.Average(), ys.Average());
        }
    }
}
