using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Common
{
    /// <summary>
    /// TextBoxで矢印キーによる数値の増減機能を追加するAttached Behavior
    /// </summary>
    public static class NumericUpDownBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(NumericUpDownBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static readonly DependencyProperty IncrementProperty =
            DependencyProperty.RegisterAttached(
                "Increment",
                typeof(double),
                typeof(NumericUpDownBehavior),
                new PropertyMetadata(1.0)); // デフォルト増分: 1.0

        public static readonly DependencyProperty LargeIncrementProperty =
            DependencyProperty.RegisterAttached(
                "LargeIncrement",
                typeof(double),
                typeof(NumericUpDownBehavior),
                new PropertyMetadata(10.0)); // デフォルト大増分: 10.0

        public static void SetEnable(DependencyObject element, bool value)
            => element.SetValue(EnableProperty, value);

        public static bool GetEnable(DependencyObject element)
            => (bool)element.GetValue(EnableProperty);

        public static void SetIncrement(DependencyObject element, double value)
            => element.SetValue(IncrementProperty, value);

        public static double GetIncrement(DependencyObject element)
            => (double)element.GetValue(IncrementProperty);

        public static void SetLargeIncrement(DependencyObject element, double value)
            => element.SetValue(LargeIncrementProperty, value);

        public static double GetLargeIncrement(DependencyObject element)
            => (double)element.GetValue(LargeIncrementProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            if ((bool)e.NewValue)
            {
                textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
            }
            else
            {
                textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            // ↑/↓キーのみ処理
            if (e.Key != Key.Up && e.Key != Key.Down)
                return;

            // 現在値を取得
            if (!TryParseCurrentValue(textBox, out double currentValue))
                return;

            // Ctrl押下で大きな増分、それ以外は通常の増分
            bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            double increment = isCtrlPressed
                ? GetLargeIncrement(textBox)
                : GetIncrement(textBox);

            // ↑キーで増加、↓キーで減少
            double newValue = e.Key == Key.Up
                ? currentValue + increment
                : currentValue - increment;

            // ValidationRuleがあれば範囲チェック
            if (!IsValueInValidRange(textBox, newValue))
            {
                // 範囲外なので増減を拒否
                e.Handled = true;
                return;
            }

            // 新しい値を設定
            ApplyNewValue(textBox, newValue);

            e.Handled = true;
        }

        /// <summary>
        /// TextBoxのテキストから数値を解析
        /// </summary>
        private static bool TryParseCurrentValue(TextBox textBox, out double currentValue)
        {
            currentValue = 0.0;

            string text = textBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                // 空の場合は0として扱う
                currentValue = 0.0;
                return true;
            }

            // カルチャを考慮して解析
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture, out currentValue) ||
                   double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out currentValue);
        }

        /// <summary>
        /// 新しい値がValidationRuleの範囲内かチェック
        /// </summary>
        private static bool IsValueInValidRange(TextBox textBox, double newValue)
        {
            var binding = BindingOperations.GetBinding(textBox, TextBox.TextProperty);
            if (binding == null)
                return true; // バインディングがない場合はOK

            // ValidationRulesからRangeValidationRuleを探す
            var rangeRule = binding.ValidationRules
                .OfType<RangeValidationRule>()
                .FirstOrDefault();

            if (rangeRule == null)
                return true; // 範囲検証ルールがない場合はOK

            // Min/Max範囲内かチェック
            return newValue >= rangeRule.Min && newValue <= rangeRule.Max;
        }

        /// <summary>
        /// 新しい値をTextBoxに設定し、バインディングソースを更新
        /// </summary>
        private static void ApplyNewValue(TextBox textBox, double newValue)
        {
            // StringFormatを取得してフォーマット保持
            string formattedValue = FormatValue(textBox, newValue);

            // TextBoxのテキストを更新
            textBox.Text = formattedValue;

            // バインディングソースを更新
            var bindingExpression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            bindingExpression?.UpdateSource();
        }

        /// <summary>
        /// バインディングのStringFormatに従って値をフォーマット
        /// </summary>
        private static string FormatValue(TextBox textBox, double value)
        {
            var binding = BindingOperations.GetBinding(textBox, TextBox.TextProperty);
            if (binding == null || string.IsNullOrEmpty(binding.StringFormat))
            {
                // StringFormatがない場合はそのまま
                return value.ToString(CultureInfo.CurrentCulture);
            }

            string format = binding.StringFormat;

            // "{}{0:N2}" のような形式から "N2" を抽出
            if (format.Contains(":"))
            {
                int colonIndex = format.IndexOf(':');
                int endIndex = format.IndexOf('}', colonIndex);
                if (endIndex > colonIndex)
                {
                    string formatSpecifier = format.Substring(colonIndex + 1, endIndex - colonIndex - 1);
                    return value.ToString(formatSpecifier, CultureInfo.CurrentCulture);
                }
            }

            // StringFormatが "N2" のようなシンプルな形式の場合
            if (format.Length <= 3 && (format.StartsWith("N") || format.StartsWith("F") || format.StartsWith("C")))
            {
                return value.ToString(format, CultureInfo.CurrentCulture);
            }

            // フォールバック: string.Formatを使用
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, value);
            }
            catch
            {
                return value.ToString(CultureInfo.CurrentCulture);
            }
        }
    }
}
