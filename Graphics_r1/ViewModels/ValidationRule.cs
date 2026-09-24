
using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace PileDesign.ViewModels
{
    public class FactorValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            // NaN は比較が偽になるので、ここで拒まれる (範囲の比較を「外なら拒む」に書き換えないこと)
            if (double.TryParse(value as string, out double result))
            {
                if (result >= 0.0 && result <= 1.0)
                {
                    return ValidationResult.ValidResult;
                }
            }
            return new ValidationResult(false, "0.00 以上 1.00 以下の数値を入力してください。");
        }
    }


    public class StringMaxLengthValidationRule : ValidationRule
    {
        public int MaxLength { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value is string str && str.Length <= MaxLength)
            {
                return ValidationResult.ValidResult;
            }

            return new ValidationResult(false, $"{MaxLength} 文字以内で入力してください。");
        }
    }

    public class NumericRangeValidationRule : ValidationRule
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            // NaN・無限大は範囲の比較の前に拒む (NaN は大小の比較がどちらも偽になり、範囲内として通るため)
            if (value != null && double.TryParse(value.ToString(), out double inputValue))
            {
                if (!double.IsFinite(inputValue))
                    return new ValidationResult(false, PileDesign.Common.RangeValidationRule.NonFiniteMessage);
                if (inputValue < Minimum || inputValue > Maximum)
                    return new ValidationResult(false, $"値は{Minimum}から{Maximum}の間である必要があります。");
            }
            else
            {
                return new ValidationResult(false, "有効な数値を入力してください。");
            }

            return ValidationResult.ValidResult;
        }
    }

    public class DoubleLessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && double.TryParse(value.ToString(), out double doubleValue))
            {
                if (double.TryParse(parameter.ToString(), out double threshold))
                {
                    return doubleValue < threshold;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}