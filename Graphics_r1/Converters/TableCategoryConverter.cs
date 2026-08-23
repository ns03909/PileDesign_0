using System;
using System.Globalization;
using System.Windows.Data;
using PileDesign.Models.Results;

namespace PileDesign.Converters
{
    /// <summary>
    /// ResultTable をカテゴリ（水平解析結果／沈下解析結果）に変換する。
    /// TableWindow の ListBox 用 CollectionViewSource.GroupDescriptions で使用。
    /// </summary>
    public class TableCategoryConverter : IValueConverter
    {
        public static bool IsSettlementTable(ResultTable t) =>
            t != null && (
                t.Category == "基礎梁考慮沈下" ||
                (t.Name != null && t.Name.StartsWith("基礎梁考慮"))
            );

        /// <summary>検定結果の表か (荷重条件をまたぐ 1 枚もの)。</summary>
        public static bool IsEvaluationTable(ResultTable t) => t?.Category == "検定";

        public static string CategoryOf(ResultTable t) =>
            IsEvaluationTable(t) ? "検定結果"
            : IsSettlementTable(t) ? "沈下解析結果"
            : "水平解析結果";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ResultTable t) return CategoryOf(t);
            return "水平解析結果";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
