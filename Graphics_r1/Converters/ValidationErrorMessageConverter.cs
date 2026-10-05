using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 入力欄の検証エラー (<see cref="ValidationError"/>) を、欄の直下に出す日本語の文に変換する。
    ///
    /// 数値に読めない文字列は WPF の変換エラーになり、その文は英語
    /// ("Value 'abc' could not be converted.") のままだったので、ここで言い換える。
    /// それ以外 (範囲の検証規則・BaseModel.AddError) は書かれた文をそのまま出す。
    /// </summary>
    public sealed class ValidationErrorMessageConverter : IValueConverter
    {
        internal const string NotANumber = "数値として読めません。数字で入力してください。";

        public static string? Describe(ValidationError? error)
        {
            if (error == null) return null;
            // 変換の失敗 (規則名は WPF 内部の ConversionValidationRule) と、変換で投げられた例外
            if (error.RuleInError?.GetType().Name == "ConversionValidationRule"
                || error.Exception is FormatException or OverflowException
                || error.Exception?.InnerException is FormatException or OverflowException)
                return NotANumber;
            return error.ErrorContent?.ToString();
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Describe(value as ValidationError);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
