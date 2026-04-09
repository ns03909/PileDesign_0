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
            // parameterに表示したい値（例: "杭応力"）を指定
            if (value is string str && parameter is string target)
                return str == target ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}