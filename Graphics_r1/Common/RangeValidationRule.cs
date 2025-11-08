using System.Globalization;
using System.Windows.Controls;

namespace PileDesign.Common
{
    public class RangeValidationRule : ValidationRule
    {
        public double Min { get; set; }
        public double Max { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (double.TryParse(value.ToString(), out double number))
            {
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