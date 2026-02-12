using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PileDesign.Common
{
    /// <summary>
    /// 入力検証エラー時にToolTipを表示するAttached Behavior
    ///
    /// 機能:
    /// - ValidationErrorが発生した時に自動的にToolTipを表示
    /// - エラー時にテキストボックスを赤枠で強調
    /// - 正常時は元に戻す
    /// </summary>
    public static class ValidationErrorToolTipBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(ValidationErrorToolTipBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        /// <summary>
        /// 正しい入力例を表示（任意）
        /// </summary>
        public static readonly DependencyProperty ExampleProperty =
            DependencyProperty.RegisterAttached(
                "Example",
                typeof(string),
                typeof(ValidationErrorToolTipBehavior),
                new PropertyMetadata(null));

        public static void SetEnable(DependencyObject element, bool value)
            => element.SetValue(EnableProperty, value);

        public static bool GetEnable(DependencyObject element)
            => (bool)element.GetValue(EnableProperty);

        public static void SetExample(DependencyObject element, string value)
            => element.SetValue(ExampleProperty, value);

        public static string GetExample(DependencyObject element)
            => (string)element.GetValue(ExampleProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                // イベント購読
                Validation.AddErrorHandler(element, OnValidationError);
            }
            else
            {
                // イベント解除
                Validation.RemoveErrorHandler(element, OnValidationError);
            }
        }

        private static void OnValidationError(object sender, ValidationErrorEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            if (e.Action == ValidationErrorEventAction.Added)
            {
                // エラーが追加された
                var errorMessage = e.Error.ErrorContent?.ToString() ?? "入力エラー";
                var example = GetExample(element);

                // ToolTipを設定
                if (!string.IsNullOrEmpty(example))
                {
                    element.ToolTip = $"{errorMessage}\n\n例: {example}";
                }
                else
                {
                    element.ToolTip = errorMessage;
                }

                // 赤枠を設定（TextBoxの場合）
                if (element is TextBox textBox)
                {
                    textBox.BorderBrush = Brushes.Red;
                    textBox.BorderThickness = new Thickness(2);
                }
            }
            else if (e.Action == ValidationErrorEventAction.Removed)
            {
                // エラーが解消された
                element.ToolTip = null;

                // 枠を元に戻す
                if (element is TextBox textBox)
                {
                    textBox.ClearValue(TextBox.BorderBrushProperty);
                    textBox.ClearValue(TextBox.BorderThicknessProperty);
                }
            }
        }
    }
}
