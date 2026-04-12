using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class AnalysisResultContentEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // parameterに表示したい値を指定（複数値は"|"区切り、例: "梁応力|基礎梁考慮沈下梁応力"）
            if (value is string str && parameter is string target)
            {
                foreach (var t in target.Split('|'))
                {
                    if (str == t) return Visibility.Visible;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}