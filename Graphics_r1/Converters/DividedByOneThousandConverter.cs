using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class DivideByThousandConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue / 1000;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return doubleValue * 1000;
            }
            return value;
        }
    }
}

