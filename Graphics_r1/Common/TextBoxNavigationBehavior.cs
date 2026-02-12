using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Common
{
    /// <summary>
    /// TextBoxでEnterキーを押した時に次のコントロールにフォーカス移動する機能を追加するAttached Behavior
    /// </summary>
    public static class TextBoxNavigationBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(TextBoxNavigationBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject element, bool value)
            => element.SetValue(EnableProperty, value);

        public static bool GetEnable(DependencyObject element)
            => (bool)element.GetValue(EnableProperty);

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

            // Enterキーが押された場合
            if (e.Key == Key.Enter)
            {
                // 1. TextBoxの値をバインディングソースに反映
                UpdateBindingSource(textBox);

                // 2. 次のコントロールにフォーカス移動
                MoveToNextControl(textBox);

                e.Handled = true;
            }
        }

        /// <summary>
        /// TextBoxのバインディングソースを更新
        /// FundamentalWindow.xaml.cs（289-299行）のパターンを使用
        /// </summary>
        private static void UpdateBindingSource(TextBox textBox)
        {
            var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
        }

        /// <summary>
        /// 次のフォーカス可能なコントロールに移動
        /// </summary>
        private static void MoveToNextControl(UIElement currentElement)
        {
            // TraversalRequestを使用してTabキー相当の移動
            var request = new TraversalRequest(FocusNavigationDirection.Next);
            currentElement.MoveFocus(request);
        }
    }
}
