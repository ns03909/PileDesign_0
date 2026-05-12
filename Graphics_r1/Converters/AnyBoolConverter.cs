using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class AnyBoolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return false;
            foreach (var value in values)
            {
                if (value is bool b && b) return true;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
}
