using System;
using System.Globalization;
using System.Windows.Data;
using PileDesign.Models.InputData;

namespace PileDesign.Converters
{
    /// <summary>
    /// ConverterParameter に与えた限界状態の表示文字列（例「使用限界」「損傷限界支持力」）を、
    /// 解説書準拠（告示1113 圧縮）オプションON時に「長期許容」「短期許容」へ写像する。
    /// value は使用しない（DataContext などをソースにしてバインド評価を発火させる用途）。
    /// 内部キー・プロパティ名には使わず、UI 表示専用。
    /// </summary>
    public class LimitStateLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            // ConverterParameter があればそれを、無ければバインド値そのものを写像対象とする
            // （前者は固定ラベル用、後者は ComboBox ItemTemplate 等で項目文字列を写像する用途）。
            => ConcreteModelOptions.MapLimitStateText(parameter as string ?? value as string ?? string.Empty);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
