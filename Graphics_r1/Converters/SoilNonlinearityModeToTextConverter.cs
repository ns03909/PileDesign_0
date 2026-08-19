using PileDesign.Models.InputData;
using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// <see cref="SoilNonlinearityMode"/> を ComboBox の表示文字列に変換する。
    /// ConverterParameter に "short" を渡すと短縮表記になる。
    /// </summary>
    public class SoilNonlinearityModeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SoilNonlinearityMode mode) return string.Empty;
            return (parameter as string) == "short"
                ? SoilNonlinearityModes.ToShortText(mode)
                : SoilNonlinearityModes.ToText(mode);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
