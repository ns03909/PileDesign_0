using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public sealed class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Binding.DoNothing;

            if (!double.TryParse(System.Convert.ToString(value, culture), NumberStyles.Any, culture, out double d))
                return Binding.DoNothing;

            double factor = 1.0;
            if (parameter != null)
                double.TryParse(System.Convert.ToString(parameter, culture), NumberStyles.Any, culture, out factor);

            double result = d * factor;

            // 重要: 常に数値を返す（string を返すと StringFormat が効かない）
            if (double.IsNaN(result) || double.IsInfinity(result))
                return Binding.DoNothing;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Two-way conversion is not supported for MultiplyConverter.");
        }
    }
}