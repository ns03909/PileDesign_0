// DoubleToStringConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class DoubleToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;
            else
                return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string;
            if (string.IsNullOrEmpty(stringValue))
            {
                return 0.0; // 空の文字列を0.0に変換
            }

            if (double.TryParse(stringValue, out double result))
            {
                return result;
            }
            return 0.0; // 変換に失敗した場合も0.0に変換
        }
    }
}