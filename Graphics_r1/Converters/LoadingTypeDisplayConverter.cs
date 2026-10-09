using System;
using System.Globalization;
using System.Windows.Data;
using PileDesign.Models.InputData;

namespace PileDesign.Converters
{
    /// <summary>荷重タイプの値 (「個別十字」など) を画面に出す名前 (「杭十字」など) にする。<see cref="PileGroupSettlement.LoadingTypeDisplayName"/></summary>
    public sealed class LoadingTypeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => PileGroupSettlement.LoadingTypeDisplayName(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
