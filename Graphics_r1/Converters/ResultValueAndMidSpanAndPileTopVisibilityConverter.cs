using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;


namespace PileDesign.Converters
{
    public class ResultValueAndMidSpanAndPileTopVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                values[0] is not bool isResultValueVisible ||
                values[1] is not bool isPileTopResultValueVisibleOnly ||
                values[2] is not bool isMidSpanResultValueVisibleOnly)
            {
                return Visibility.Collapsed;
            }

            return (isResultValueVisible && !isPileTopResultValueVisibleOnly && !isMidSpanResultValueVisibleOnly)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
