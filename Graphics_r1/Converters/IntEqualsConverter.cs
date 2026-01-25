using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    // CalculationReportLevel(int) と RadioButton(IsChecked:bool) の双方向バインド用
    public class IntEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!int.TryParse(parameter?.ToString(), out var param)) return DependencyProperty.UnsetValue;
            if (value is int v) return v == param;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!int.TryParse(parameter?.ToString(), out var param)) return Binding.DoNothing;
            if (value is bool b && b) return param;
            return Binding.DoNothing; // 他の RadioButton が true にするので、false 時は既存値を変更しない
        }
    }
}