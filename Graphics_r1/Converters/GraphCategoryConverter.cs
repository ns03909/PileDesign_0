using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// GraphWindow のグラフ種別名をカテゴリ（水平解析結果／沈下解析結果）に変換する。
    /// ComboBox の CollectionViewSource.GroupDescriptions で使用。
    /// </summary>
    public class GraphCategoryConverter : IValueConverter
    {
        // 沈下解析結果側に分類するグラフ名
        private static readonly HashSet<string> SettlementItems = new()
        {
            "荷重沈下曲線",
            "沈下 単杭",
            "沈下 群杭",
            "沈下 単杭+群杭",
            "沈下 基礎梁考慮単杭",
            "沈下 基礎梁考慮単杭+群杭",
            "沈下 個別矩形(基礎梁考慮)",
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && SettlementItems.Contains(s))
                return "沈下解析結果";
            return "水平解析結果";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
