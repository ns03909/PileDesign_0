using PileDesign.Common;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PileDesign.Converters
{
    // true -> ê¬ÅAfalse -> ê‘
    public class BoolToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Blue = NikkenBrush.SkyBlue;
        private static readonly SolidColorBrush Red = NikkenBrush.PaleRed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return Blue;
            return Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}