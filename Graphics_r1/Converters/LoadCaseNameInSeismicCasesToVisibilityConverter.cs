using PileDesign.Models.InputData;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class LoadCaseNameInSeismicCasesToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string selectedLoadCaseName &&
                values[1] is System.Collections.IEnumerable loadCases)
            {
                foreach (var item in loadCases)
                {
                    if (item is LoadCase lc && lc.LoadName == selectedLoadCaseName)
                        return Visibility.Visible;
                }
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}