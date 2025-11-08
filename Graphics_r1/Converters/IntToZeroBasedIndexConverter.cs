using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 1始まりの番号と0始まりのインデックスを相互変換するコンバータ
    /// </summary>
    public class IntToZeroBasedIndexConverter : IValueConverter
    {
        // ViewModel → ComboBox.SelectedIndex
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue - 1;
            }
            return 0;
        }

        // ComboBox.SelectedIndex → ViewModel
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return index + 1;
            }
            return 1;
        }
    }
}
