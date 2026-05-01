using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// IMultiValueConverter: 水平解析 DataGrid「済」列用。
    /// Bind 入力:
    ///   [0] = LoadName (string) — DataGridRow の DataContext.LoadName
    ///   [1] = CompletedCaseKeys (IEnumerable<string>) — VM 側の「LoadName|CombName|Liq」集合
    /// 戻り値:
    ///   このロードケース由来のキーが集合に 1 件以上あれば "✓"、なければ空文字。
    /// </summary>
    public class CompletedCaseToCheckMarkConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "";
            if (values[0] is not string loadName || string.IsNullOrEmpty(loadName)) return "";
            if (values[1] is not IEnumerable keys) return "";

            string prefix = loadName + "|";
            foreach (var k in keys)
            {
                if (k is string s && s.StartsWith(prefix, StringComparison.Ordinal))
                    return "✓"; // ✓ U+2713 Check Mark
            }
            return "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
