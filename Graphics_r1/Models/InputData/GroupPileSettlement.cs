using System;
using System.Collections.Generic;
using System.Windows;


namespace PileDesign.Models.InputData
{
    internal class GroupPileSettlement
    {


        // 多層地盤における矩形載荷面隅角部の沈下量の計算
        private static double Steinbrenner(List<double> nus, List<double> Es, List<double> H, int ii, double q, double B1, double B2)
        {
            double d;
            double Is1;
            double Is2;
            double F1;
            double F2;

            if (B1 <= 0 || B2 <= 0)
            { return 0.0; }

            double B = Math.Min(B1, B2);
            double L = Math.Max(B1, B2) / B;
            double SE = 0.0;

            for (int i = 0; i < ii; i++)
            {
                d = H[i] / B;
                F1 = 1 / Math.PI * (L * Math.Log((1 + Math.Sqrt(Math.Pow(L, 2) + 1)) * Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2)) / L / (1 + Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2) + 1))) + Math.Log((L + Math.Sqrt(Math.Pow(L, 2) + 1)) * Math.Sqrt(1 + Math.Pow(d, 2)) / Math.Sqrt(L + (Math.Pow(L, 2) + Math.Pow(d, 2) + 1))));
                F2 = d / 2 / Math.PI * Math.Atan(L / d / Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2) + 1));
                Is1 = (1 - Math.Pow(nus[i], 2)) * F1 + (1 - nus[i] - 2 * Math.Pow(nus[i], 2)) * F2;
                SE += Is1 / Es[i] * q * B;
                if (i != 0)
                {
                    d = H[i - 1] / B;
                    F1 = 1 / Math.PI * (L * Math.Log((1 + Math.Sqrt(Math.Pow(L, 2) + 1)) * Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2)) / L / (1 + Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2) + 1))) + Math.Log((L + Math.Sqrt(Math.Pow(L, 2) + 1)) * Math.Sqrt(1 + Math.Pow(d, 2)) / Math.Sqrt(L + (Math.Pow(L, 2) + Math.Pow(d, 2) + 1))));
                    F2 = d / 2 / Math.PI * Math.Atan(L / d / Math.Sqrt(Math.Pow(L, 2) + Math.Pow(d, 2) + 1));
                    Is2 = (1 - Math.Pow(nus[i], 2)) * F1 + (1 - nus[i] - 2 * Math.Pow(nus[i], 2)) * F2;
                    SE -= Is2 / Es[i] * q * B;
                }
            }
            return SE;
        }

        public class RectLoad : BaseModel
        {
            private Point _center;
            public Point Center
            {
                get => _center;
                set => SetProperty(ref _center, value);
            }

            private double _dx;
            public double DX
            {
                get => _dx;
                set => SetProperty(ref _dx, value);
            }

            private double _dy;
            public double DY
            {
                get => _dy;
                set => SetProperty(ref _dy, value);
            }

            private double _q;
            public double Q
            {
                get => _q;
                set => SetProperty(ref _q, value);
            }
        }

        // 円形影響範囲から矩形影響範囲を得るメソッド
        public static List<RectLoad> EquivalentRectLoads(Point center, double radius, double load, bool isDouble = true)
        {
            double q = load / (Math.PI * Math.Pow(radius, 2));

            if (isDouble) // 風車型　正方形大×1+長方形小×4
            {
                double p = Math.Sqrt(Math.PI * Math.Pow(radius, 2) / (4 + 2 * Math.Sqrt(2)));
                double side1 = (1 + Math.Sqrt(2)) * p;
                double side2a = p;
                double side2b = p * 0.25;

                return [
                    new RectLoad() {Center = center, DX = side1, DY = side1, Q = q},
                    new RectLoad() {Center = new(center.X + (side1 + side2b) * 0.5 , center.Y),
                        DX = side2b, DY = side2a, Q = q},
                    new RectLoad() {Center = new(center.X - (side1 + side2b) * 0.5 , center.Y),
                        DX = side2b, DY = side2a, Q = q},
                    new RectLoad() {Center = new(center.X, center.Y + (side1 + side2b) * 0.5),
                        DX = side2a, DY = side2b, Q = q},
                    new RectLoad() {Center = new(center.X, center.Y - (side1 + side2b) * 0.5),
                        DX = side2a, DY = side2b, Q = q},
                    ];
            }
            else // 正方形１つ
            {
                double side = Math.Sqrt(Math.PI) / radius;
                return [new RectLoad() { Center = center, DX = side, DY = side, Q = q }];
            }
        }

        // 矩形荷重面による沈下算定メソッド
        public static double Settlement(List<double> nus, List<double> Es, List<double> Hs, List<RectLoad> rectLoads, Point point)
        {
            //'矩形載荷面の隅角部の沈下の組合せ
            // nus: ポアソン比 [0: jj]
            // Es: 縦弾性係数 (kN/m3) [0: jj]
            // Hs: 深さ (m) [0: jj]

            // xs: 矩形裁荷面中心X座標(m)
            // ys: 矩形裁荷面中心Y座標(m)
            // dxs: 矩形裁荷面X幅(m)
            // dys: 矩形裁荷面Y幅(m)
            // qs: 矩形裁荷面荷重度(kN/m2)

            //'B:基礎の短辺長さ　(m)
            //'L:基礎の長辺長さ　(m)
            //'x0:沈下量を求める点
            //'y0:沈下量を求める点
            //'x1:矩形載荷面
            //'x2:矩形載荷面
            //'y1:矩形載荷面
            //'y2:矩形載荷面
            //'沈下を求める点
            double s = 0;
            double x0 = point.X;
            double y0 = point.Y;
            double x1;
            double x2;

            double y1;
            double y2;
            double q;
            int jj = nus.Count;
            double sa;
            double sb;
            double sc;
            double sd;
            double ds;



            for (int j = 0; j < rectLoads.Count; j++)
            {
                q = rectLoads[j].Q;
                x1 = rectLoads[j].Center.X - rectLoads[j].DX * 0.5;
                x2 = rectLoads[j].Center.X + rectLoads[j].DX * 0.5;
                y1 = rectLoads[j].Center.Y - rectLoads[j].DY * 0.5;
                y2 = rectLoads[j].Center.Y + rectLoads[j].DY * 0.5;

                // '下左'沈下を求める点が載荷面の外側にある場合
                if (x0 < x1 && y0 < y1)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y2 - y0);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y1 - y0);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y1 - y0);
                    ds = sa - sb - sc + sd;
                }

                // '下中'沈下を求める点が載荷面の外側にある場合
                else if ((x1 <= x0 && x0 <= x2) && y0 < y1)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y2 - y0);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y1 - y0);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y1 - y0);
                    ds = sa + sb - sc - sd;
                }

                // '下右'沈下を求める点が載荷面の外側にある場合
                else if (x1 < x0 && y0 < y1)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y2 - y0);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y1 - y0);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y1 - y0);
                    ds = sa - sb - sc + sd;
                }

                // '中左'沈下を求める点が載荷面の外側にある場合
                else if (x0 < x1 && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y0 - y1);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y2 - y0);
                    ds = sa + sb - sc - sd;
                }

                // '中中 '沈下を求める点が載荷面の内側にある場合
                else if ((x1 <= x0 && x0 <= x2) && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y1);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y2 - y0);
                    ds = sa + sb + sc + sd;
                }

                // '中右'沈下を求める点が載荷面の外側にある場合
                else if (x2 < x0 && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y2 - y0);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y0 - y1);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y2 - y0);
                    ds = sa + sb - sc - sd;
                }

                // '上左'沈下を求める点が載荷面の外側にある場合
                else if (x0 < x1 && y2 < y0)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y0 - y1);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y2);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x1 - x0, y0 - y2);
                    ds = sa - sb - sc + sd;
                }

                // '上中'沈下を求める点が載荷面の外側にある場合
                else if ((x1 <= x0 && x0 <= x2) && y2 < y0)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y1);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y2);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x2 - x0, y0 - y2);
                    ds = sa + sb - sc - sd;
                }

                // '上右'沈下を求める点が載荷面の外側にある場合
                else if (x1 < x0 && y2 < y0)
                {
                    sa = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y0 - y1);
                    sc = Steinbrenner(nus, Es, Hs, jj, q, x0 - x1, y0 - y2);
                    sd = Steinbrenner(nus, Es, Hs, jj, q, x0 - x2, y0 - y2);
                    ds = sa - sb - sc + sd;
                }
                else { ds = 0; }
                s += ds;

            }
            return s;
        }
    }
}



