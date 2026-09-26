using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// IMultiValueConverter: 水平解析 DataGrid「済」列用。
    /// Bind 入力:
    ///   [0] = 荷重ケース (LoadCase) — DataGridRow の DataContext
    ///   [1] = CompletedCaseKeys (IEnumerable<string>) — VM 側の「L{レベル}-{番号}|組合せ番号|液状化」集合
    ///         (最後まで解けたケースだけ。AnalysisRunSnapshot.CaseKey.ToDisplayKey)
    /// 戻り値:
    ///   この荷重ケースのキーが集合に 1 件以上あれば "✓"、なければ空文字。
    ///   荷重ケースは名前ではなくレベルと番号で見分ける (名前は空欄・重複がありうる)。
    /// </summary>
    public class CompletedCaseToCheckMarkConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "";
            if (values[0] is not PileDesign.Models.InputData.LoadCase loadCase) return "";
            if (values[1] is not IEnumerable keys) return "";

            string prefix = PileDesign.FEM.AnalysisRunSnapshot.CaseKey.PrefixOf(loadCase.Level, loadCase.No);
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
