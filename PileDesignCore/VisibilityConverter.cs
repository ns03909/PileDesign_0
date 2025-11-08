using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

//namespace PileDesignCore
//{
//    public class VisibilityConverter : IValueConverter
//    {
//        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
//        {
//            string targetValue = parameter as string;
//            string currentValue = value as string;

//            if (currentValue == targetValue)
//            {
//                return Visibility.Visible;
//            }
//            else
//            {
//                return Visibility.Collapsed;
//            }
//        }

//        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}

namespace PileDesignCore
{
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
            throw new NotImplementedException();
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
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
            throw new NotImplementedException();
        }
    }
}
