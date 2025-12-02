using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class AllTrueToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is { Length: > 0 })
            {
                foreach (var v in values)
                {
                    if (v is bool b)
                    {
                        if (!b) return Visibility.Collapsed;
                    }
                    else
                    {
                        return Visibility.Collapsed;
                    }
                }
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}