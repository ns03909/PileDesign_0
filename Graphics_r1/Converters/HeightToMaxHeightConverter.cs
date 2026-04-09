using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class HeightToMaxHeightConverter : IValueConverter
    {
        // 他の要素の高さ（ボタンや余白など）を定義
        private const double ReservedHeight = 100; // 例: ボタンや余白の合計高さ

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double windowHeight)
            {
                // ウィンドウの高さから予約された高さを引いて返す
                return Math.Max(0, windowHeight - ReservedHeight);
            }

            return 0; // デフォルト値
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}