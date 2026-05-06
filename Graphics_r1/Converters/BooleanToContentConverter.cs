using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class BooleanToContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string trueContent = "杭要素分割(済)";
            string falseContent = "杭要素分割(未)";

            if (parameter is string paramStr)
            {
                var parts = paramStr.Split(',');
                if (parts.Length >= 2)
                {
                    trueContent = parts[0];
                    falseContent = parts[1];
                }
            }

            if (value is bool boolValue)
            {
                return boolValue ? trueContent : falseContent;
            }
            return falseContent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}