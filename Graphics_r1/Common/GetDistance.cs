using System;
using System.Windows;
using System.Windows.Media.Media3D;
namespace PileDesign.Common
{
    public class GetDistance
    {
        // 2点間の距離を返すメソッド
        public static double BetweenTwoPoint3Ds(Point3D p1, Point3D p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2) + Math.Pow(p1.Z - p2.Z, 2));
        }

        // 2点間の距離を返すメソッド
        public static double BetweenTwoNodes(Point p1, Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        // 点と直線の距離を返すメソッド
        public static double BetweenNodeAndLine(Point lineStart, Point lineEnd, Point p)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            if (dx == 0 && dy == 0)
            {
                // lineStart と lineEnd が同じ点の場合
                return BetweenTwoNodes(lineStart, p);
            }

            // 線分の長さの二乗
            double lineLengthSquared = dx * dx + dy * dy;

            // 点 p から線分の始点 lineStart へのベクトル
            double t = ((p.X - lineStart.X) * dx + (p.Y - lineStart.Y) * dy) / lineLengthSquared;

            if (t < 0)
            {
                // 点 p が線分の外側で lineStart に最も近い場合
                return BetweenTwoNodes(lineStart, p);
            }
            else if (t > 1)
            {
                // 点 p が線分の外側で lineEnd に最も近い場合
                return BetweenTwoNodes(lineEnd, p);
            }

            // 点 p から線分上の最近接点へのベクトル
            Point projection = new(lineStart.X + t * dx, lineStart.Y + t * dy);
            return BetweenTwoNodes(projection, p);
        }
    }
}
