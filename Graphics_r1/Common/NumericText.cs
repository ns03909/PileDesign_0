using System.Globalization;
using System.Text.RegularExpressions;

namespace PileDesign.Common
{
    /// <summary>
    /// 画面で入力・貼り付けされた数値の文字列を読む規則 (1 か所にまとめたもの)。
    ///
    /// 小数点は「.」、指数表記可、環境の地域設定 (Windows の「地域」) に依らない。
    /// 画面の数値はバインディングで表示・変換され、その書式は WPF の既定 (en-US) で小数点が「.」になる。
    /// 数値の入力欄 (<see cref="NumericInput"/>) も「.」しか打てない。保存ファイル (JSON) も同じ。
    ///
    /// 以前は検証ルールやセル編集の一部が、地域設定のまま <c>double.TryParse(text, out v)</c> で読んでいた。
    /// 小数点に「,」を使う地域 (ドイツなど) では「1.5」が桁区切りとみなされて 15 と読まれ、
    /// バインディングは 1.5 を入れる一方で、検証やセル編集の値は 15 になった (欄ごとに受け付ける値が違った)。
    /// </summary>
    public static class NumericText
    {
        /// <summary>
        /// 数値として読む。前後の空白は無視する。桁区切りの「,」は受け付けない。
        /// 「∞」も無限大として読む (日本の地域設定の表記。読んだうえで、呼び出し側が「無限大は入力できない」と理由を示す)。
        /// </summary>
        public static bool TryParse(string? text, out double value)
        {
            switch (text?.Trim())
            {
                case "∞" or "+∞": value = double.PositiveInfinity; return true;
                case "-∞": value = double.NegativeInfinity; return true;
            }
            return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>整数として読む。前後の空白は無視する。</summary>
        public static bool TryParse(string? text, out int value)
            => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// 貼り付けた値を読む。表計算ソフトから写した「1,000」のような桁区切りも受け付ける。
        /// ただし「,」は 3 桁ごとの桁区切りとして正しい並びのときに限る。「1,5」(小数点に「,」を使う地域の 1.5) は
        /// 15 と読み違えないよう、読めないものとして拒む。
        /// </summary>
        public static bool TryParseAllowingThousands(string? text, out double value)
        {
            value = 0;
            return TryRemoveThousands(text, out var plain) && TryParse(plain, out value);
        }

        /// <inheritdoc cref="TryParseAllowingThousands(string?, out double)"/>
        public static bool TryParseAllowingThousands(string? text, out int value)
        {
            value = 0;
            return TryRemoveThousands(text, out var plain) && TryParse(plain, out value);
        }

        /// <inheritdoc cref="TryParseAllowingThousands(string?, out double)"/>
        public static bool TryParseAllowingThousands(string? text, out decimal value)
        {
            value = 0;
            return TryRemoveThousands(text, out var plain)
                && decimal.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>桁区切りの「,」を除く。3 桁ごとの正しい並びでなければ false (「1,5」など)。</summary>
        private static bool TryRemoveThousands(string? text, out string? plain)
        {
            plain = text?.Trim();
            if (plain == null || !plain.Contains(',')) return true;
            if (!ThousandsGrouping.IsMatch(plain)) return false;
            plain = plain.Replace(",", "");
            return true;
        }

        private static readonly Regex ThousandsGrouping = new(@"^[+-]?\d{1,3}(,\d{3})+(\.\d*)?([eE][+-]?\d+)?$", RegexOptions.CultureInvariant);
    }
}
