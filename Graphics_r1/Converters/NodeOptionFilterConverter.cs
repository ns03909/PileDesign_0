using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 梁要素一覧の節点I/J ComboBox 用フィルタ。
    /// values[0]: 全節点候補リスト (IEnumerable&lt;NodeReferenceOption&gt;)
    /// values[1]: 除外対象 (もう一方の端で選択中の NodeReferenceOption.Key 文字列、例 "2:guid")
    /// → values[0] から Key が一致するオプションを除外したリストを返す。
    /// 同じノードを節点I/J 両方に選択させないために使う。
    /// </summary>
    public class NodeOptionFilterConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 1) return Array.Empty<NodeReferenceOption>();
            if (values[0] is not IEnumerable<NodeReferenceOption> all) return Array.Empty<NodeReferenceOption>();

            string excludeKey = values.Length >= 2 ? values[1] as string : null;
            if (string.IsNullOrEmpty(excludeKey)) return all.ToList();

            return all.Where(o => o.Key != excludeKey).ToList();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
}
