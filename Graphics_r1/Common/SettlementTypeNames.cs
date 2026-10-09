using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Common
{
    /// <summary>
    /// 沈下の結果の種別 (沈下種別・グラフの種類・凡例の題) の画面に出す名前。
    ///
    /// <para>コードの中の値 (「群杭」「基礎梁考慮+群杭」など) は選択の判定に広く使っているので変えず、表示だけを言い換える。
    /// 以前の呼び名は、群杭沈下の解析を「基礎梁なし / 基礎梁あり」と呼ぶようにしたあと、
    /// 「基礎梁考慮+群杭」(単杭の基礎梁考慮 + 群杭の基礎梁なし) が「群杭の基礎梁あり」と読めてしまった。
    /// 単杭か群杭か、どの解析かを名前に入れる (2026-10-09)。</para>
    /// </summary>
    public static class SettlementTypeNames
    {
        /// <summary>沈下種別の選択肢の表示名。</summary>
        public static string Type(string? key) => key switch
        {
            "群杭" => "群杭（基礎梁なし）",
            "単杭+群杭" => "単杭＋群杭（基礎梁なし）",
            "基礎梁考慮" => "単杭（基礎梁考慮）",
            "基礎梁考慮+群杭" => "単杭（基礎梁考慮）＋群杭（基礎梁なし）",
            _ => key ?? "",
        };

        /// <summary>グラフの種類の表示名。</summary>
        public static string Graph(string? option) => option switch
        {
            "沈下 群杭" => "沈下 群杭（基礎梁なし）",
            "沈下 単杭+群杭" => "沈下 単杭＋群杭（基礎梁なし）",
            "沈下 基礎梁考慮単杭" => "沈下 単杭（基礎梁考慮）",
            "沈下 基礎梁考慮単杭+群杭" => "沈下 単杭（基礎梁考慮）＋群杭（基礎梁なし）",
            "沈下 杭矩形(基礎梁考慮)" => "沈下 群杭（基礎梁あり）",
            _ => option ?? "",
        };

        // 長いものから照合する (「基礎梁考慮+群杭」を「基礎梁考慮」と取り違えない)
        private static readonly string[] Prefixes = ["基礎梁考慮+群杭", "単杭+群杭", "基礎梁考慮", "群杭"];

        /// <summary>
        /// 凡例の題 (表示内容のキー: 「基礎梁考慮+群杭沈下」「群杭沈下部材角」「基礎梁考慮反力（地盤）」など) の表示名。
        /// 先頭の種別だけを言い換え、「の」でつなぐ。種別で始まらないもの (単杭沈下など) はそのまま。
        /// </summary>
        public static string Title(string? content)
        {
            if (string.IsNullOrEmpty(content)) return content ?? "";
            foreach (var prefix in Prefixes)
            {
                if (content.StartsWith(prefix, StringComparison.Ordinal) && content.Length > prefix.Length)
                    return Type(prefix) + "の" + content[prefix.Length..];
            }
            return content;
        }
    }

    /// <summary>沈下種別 (ConverterParameter 省略) / グラフの種類 (ConverterParameter="Graph") の表示名にする。</summary>
    public sealed class SettlementTypeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => parameter as string == "Graph" ? SettlementTypeNames.Graph(value as string) : SettlementTypeNames.Type(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
