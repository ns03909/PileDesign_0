using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// bool を反転するコンバータ（TwoWay 対応）。
    /// 基本設定のモデル化オプションで「既定（基礎部材の強度と変形性能）」側の
    /// RadioButton を、オプション bool プロパティの否定にバインドするために使用する。
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }
}
