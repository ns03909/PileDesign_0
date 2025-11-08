using System;
using System.Windows;


namespace PileDesign.Converters
{
    public class VectorConverter
    {
        public static Vector ConvertAngleToUnitVector(double angleInDegrees)
        {
            // 角度をラジアンに変換
            double angleInRadians = angleInDegrees * (Math.PI / 180.0);

            // 単位ベクトルのx成分とy成分を計算
            double x = Math.Cos(angleInRadians);
            double y = Math.Sin(angleInRadians);
            Vector vector = new(x, y);
            return vector;
        }
    }

}
