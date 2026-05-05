using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    // Visibility Converterクラス
    public class VisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // パラメーターを区切り文字で分割
            string[] targetValues = (parameter as string)?.Split(',');

            string currentValue = value as string;

            // 分割された各値を確認し、現在の値と一致するかどうかをチェック
            if (targetValues != null && targetValues.Contains(currentValue))
            {
                return Visibility.Visible;
            }
            else
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class CountLargerVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && int.TryParse(parameter.ToString(), out int requiredCount))
            {
                return count > requiredCount ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }


    public class CountSmallerVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && int.TryParse(parameter.ToString(), out int requiredCount))
            {
                return count < requiredCount ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // BooleanToVisibilityConverterクラス
    // ConverterParameter に "Inverse" または "!" を指定すると true→Collapsed / false→Visible で反転動作
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;
            string param = parameter as string;
            bool inverse = !string.IsNullOrEmpty(param) &&
                           (param.Equals("Inverse", StringComparison.OrdinalIgnoreCase) || param == "!");
            bool visible = inverse ? !boolValue : boolValue;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
