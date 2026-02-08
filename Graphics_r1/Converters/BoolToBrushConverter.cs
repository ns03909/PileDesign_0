using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PileDesign.Converters
{
    // true -> 緑LED, false -> 赤LED（リアルなグラデーション）
    public class BoolToBrushConverter : IValueConverter
    {
        // 緑LED（点灯）: 明るい緑から深い緑へのグラデーション
        private static readonly RadialGradientBrush GreenLedBrush = CreateLedBrush(
            Color.FromRgb(200, 255, 200),  // ハイライト（白っぽい緑）
            Color.FromRgb(0, 220, 80),     // 中心（明るい緑）
            Color.FromRgb(0, 140, 50));    // 外側（深い緑）

        // 赤LED（消灯/警告）: 明るい赤から深い赤へのグラデーション
        private static readonly RadialGradientBrush RedLedBrush = CreateLedBrush(
            Color.FromRgb(255, 200, 200),  // ハイライト（白っぽい赤）
            Color.FromRgb(255, 80, 80),    // 中心（明るい赤）
            Color.FromRgb(180, 30, 30));   // 外側（深い赤）

        private static RadialGradientBrush CreateLedBrush(Color highlight, Color center, Color edge)
        {
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.3, 0.3),  // ハイライト位置（左上寄り）
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(highlight, 0.0));  // 中心のハイライト
            brush.GradientStops.Add(new GradientStop(center, 0.4));     // 中間色
            brush.GradientStops.Add(new GradientStop(edge, 1.0));       // 外縁
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return GreenLedBrush;
            return RedLedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
