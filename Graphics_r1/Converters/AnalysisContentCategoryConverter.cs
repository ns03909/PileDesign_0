using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 解析結果表示コンテンツ名をカテゴリ（沈下解析結果／水平解析結果）に変換する。
    /// ComboBox の CollectionViewSource.GroupDescriptions で使用。
    /// </summary>
    public class AnalysisContentCategoryConverter : IValueConverter
    {
        private static readonly HashSet<string> SettlementItems = new()
        {
            "沈下量",
            "沈下部材角",
            "沈下反力（地盤）",
            "沈下反力（杭頭集約）",
            "沈下応力",
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
