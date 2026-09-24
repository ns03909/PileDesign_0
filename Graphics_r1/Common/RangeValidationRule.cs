using System.Globalization;
using System.Windows.Controls;

namespace PileDesign.Common
{
    /// <summary>
    /// 入力欄の値が数値で、<see cref="Min"/> 以上 <see cref="Max"/> 以下であること。
    ///
    /// <b>NaN・無限大は範囲の比較の前に拒む。</b>以前は大小の比較だけを見ていたので、NaN は
    /// 「Min より小さい」「Max より大きい」のどちらも偽になり、有効な入力として通った
    /// (「NaN」と打てば入力値が NaN になる)。
    /// </summary>
    public class RangeValidationRule : ValidationRule
    {
        public double Min { get; set; }
        public double Max { get; set; }

        /// <summary>数値として扱えない (NaN・無限大) ときの文言。範囲の検証ルールで共有する。</summary>
        internal const string NonFiniteMessage = "数値を入力してください (NaN や無限大は入力できません)。";

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value != null && double.TryParse(value.ToString(), out double number))
            {
                if (!double.IsFinite(number))
                    return new ValidationResult(false, NonFiniteMessage);
                if (number < Min || number > Max)
                {
                    return new ValidationResult(false, $"値は {Min} 以上 {Max} 以下でなければなりません。");
                }
                return ValidationResult.ValidResult;
            }
            return new ValidationResult(false, "無効な数値です。");
        }
    }
}