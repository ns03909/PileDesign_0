using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// ヤング係数などを 3 桁区切りの整数で表示する。ただし 0 でない 1 未満の値は小数まで出す
    /// (<see cref="Common.NumberDisplay.Modulus"/>)。表示専用 (書き戻さない)。
    /// </summary>
    public class ModulusDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? Common.NumberDisplay.Modulus(d) : value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
