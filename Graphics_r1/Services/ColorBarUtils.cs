
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace PileDesign.Services
{
    /// <summary>
    /// カラーバー帯・選択ユーティリティ。
    /// </summary>
    public static class ColorBarUtils
    {
        /// <summary>
        /// カラーバー表示モード
        /// </summary>
        public enum ColorBarMode
        {
            Diverging, // 0 = Gray, negative = Blue, positive = Red（既定）
            Rainbow    // レインボー（ColorBar.GetColor を使用）
        }

        /// <summary>
        /// 値列から ColorBaredGeometry のリストを返す。
        /// mode によってダイバージング（0基準）またはレインボーを切替可能。
        /// </summary>
        public static List<ColorBaredGeometry> GetColorBarGeometries(IEnumerable<double> values, int steps = 12, ColorBarMode mode = ColorBarMode.Diverging)
        {
            var list = (values ?? Enumerable.Empty<double>()).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (list.Count == 0) return new List<ColorBaredGeometry>();

            double min = list.Min();
            double max = list.Max();
            if (min == max)
            {
                // 単一値なら単一バンド（グレー）
                return new List<ColorBaredGeometry>
                {
                    new ColorBaredGeometry
                    {
                        BottomRange = min,
                        TopRange = max,
                        Color = Color.FromRgb(128,128,128)
                    }
                };
            }

            steps = Math.Max(1, steps);

            // 区切りの良いステップ幅を計算
            double rawSpan = (max - min) / steps;
            double niceSpan = NiceStep(rawSpan);

            // 区切りの良いmin/maxに拡張
            double niceMin = Math.Floor(min / niceSpan) * niceSpan;
            double niceMax = Math.Ceiling(max / niceSpan) * niceSpan;
            // 実際のステップ数を再計算
            int niceSteps = Math.Max(1, (int)Math.Round((niceMax - niceMin) / niceSpan));
            // ステップ数が多すぎる場合は制限
            if (niceSteps > steps * 2) { niceSteps = steps; niceSpan = (niceMax - niceMin) / niceSteps; }

            var geoms = new List<ColorBaredGeometry>(niceSteps);
            double maxAbs = Math.Max(Math.Abs(niceMin), Math.Abs(niceMax));

            // 正負両方の値が存在するかチェック（Divergingモード自動判定用）
            bool hasBothSigns = niceMin < -1e-12 && niceMax > 1e-12;

            // Divergingモードで正負両方ない場合はRainbowにフォールバック
            if (mode == ColorBarMode.Diverging && !hasBothSigns)
                mode = ColorBarMode.Rainbow;

            Color gray = Color.FromRgb(128, 128, 128);
            Color blue = Color.FromRgb(0, 0, 255);
            Color red = Color.FromRgb(255, 0, 0);

            for (int i = 0; i < niceSteps; i++)
            {
                double b = niceMin + i * niceSpan;
                double t = (i == niceSteps - 1) ? niceMax : (niceMin + (i + 1) * niceSpan);
                double mid = (b + t) * 0.5;

                Color col;

                if (mode == ColorBarMode.Rainbow)
                {
                    double ratio = (niceMax - niceMin) > 1e-15 ? (mid - niceMin) / (niceMax - niceMin) : 0.5;
                    ratio = Math.Max(0.0, Math.Min(1.0, ratio));
                    col = ColorBar.GetColor(ratio);
                }
                else
                {
                    // Diverging: 0 をグレー、負は青へ、正は赤へ
                    if (Math.Abs(maxAbs) <= 1e-12)
                    {
                        col = gray;
                    }
                    else
                    {
                        double v = mid / maxAbs;
                        v = Math.Max(-1.0, Math.Min(1.0, v));

                        if (Math.Abs(v) < 1e-9)
                        {
                            col = gray;
                        }
                        else if (v > 0)
                        {
                            col = LerpColor(gray, red, v);
                        }
                        else
                        {
                            col = LerpColor(gray, blue, -v);
                        }
                    }
                }

                geoms.Add(new ColorBaredGeometry
                {
                    BottomRange = b,
                    TopRange = t,
                    Color = col
                });
            }

            return geoms;
        }

        /// <summary>
        /// 区切りの良いステップ幅を返す（1, 2, 2.5, 5 × 10^n の系列）
        /// </summary>
        private static double NiceStep(double rawStep)
        {
            if (rawStep <= 0) return 1;
            double exponent = Math.Floor(Math.Log10(rawStep));
            double fraction = rawStep / Math.Pow(10, exponent);

            double niceFraction;
            if (fraction <= 1.0) niceFraction = 1.0;
            else if (fraction <= 2.0) niceFraction = 2.0;
            else if (fraction <= 2.5) niceFraction = 2.5;
            else if (fraction <= 5.0) niceFraction = 5.0;
            else niceFraction = 10.0;

            return niceFraction * Math.Pow(10, exponent);
        }

        public static List<ColorBaredGeometry> GetMonoColorBarGeometries(Color color, IEnumerable<double> values)
        {
            var list = (values ?? Enumerable.Empty<double>()).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            double min = list.Count > 0 ? list.Min() : 0.0;
            double max = list.Count > 0 ? list.Max() : 1.0;
            return new List<ColorBaredGeometry>
            {
                new ColorBaredGeometry
                {
                    BottomRange = min,
                    TopRange = max,
                    Color = color
                }
            };
        }

        public static ColorBaredGeometry? PickColorGeometry(double value, List<ColorBaredGeometry> geoms)
        {
            if (geoms == null || geoms.Count == 0) return null;
            foreach (var g in geoms)
            {
                if (value >= g.BottomRange && value < g.TopRange) return g;
            }
            return null;
        }

        public static ColorBaredGeometry? PickColorGeometryInclusiveTop(double value, List<ColorBaredGeometry> geoms)
        {
            if (geoms == null || geoms.Count == 0) return null;
            for (int i = 0; i < geoms.Count; i++)
            {
                var g = geoms[i];
                if (i == geoms.Count - 1)
                {
                    if (value >= g.BottomRange && value <= g.TopRange) return g;
                }
                else
                {
                    if (value >= g.BottomRange && value < g.TopRange) return g;
                }
            }
            return null;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            byte r = (byte)(a.R + (b.R - a.R) * t);
            byte g = (byte)(a.G + (b.G - a.G) * t);
            byte bl = (byte)(a.B + (b.B - a.B) * t);
            return Color.FromRgb(r, g, bl);
        }
    }
}