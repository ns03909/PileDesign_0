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

            // パラメータ: "factor" or "factor|format"
            double factor = 1.0;
            string format = null;
            if (parameter != null)
            {
                string paramStr = System.Convert.ToString(parameter, culture) ?? "";
                if (paramStr.Contains('|'))
                {
                    var parts = paramStr.Split('|');
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out factor);
                    format = parts[1];
                }
                else
                {
                    double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out factor);
                }
            }

            double result = d * factor;

            if (double.IsNaN(result) || double.IsInfinity(result))
                return Binding.DoNothing;

            // フォーマット指定がある場合はstringで返す
            if (format != null)
                return result.ToString(format, culture);

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Two-way conversion is not supported for MultiplyConverter.");
        }
    }
}