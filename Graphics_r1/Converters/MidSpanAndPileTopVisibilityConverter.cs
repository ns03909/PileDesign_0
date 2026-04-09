using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class MidSpanAndPileTopVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 ||
                values[0] is not bool isResultValueVisible ||
                values[1] is not bool isPileTopResultValueVisibleOnly)
                return Visibility.Collapsed;

            return (isResultValueVisible && !isPileTopResultValueVisibleOnly) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
