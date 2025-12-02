using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PileDesign.Views
{
    public partial class QuickHintBubble : UserControl
    {
        private bool _isUpdatingText;

        public QuickHintBubble()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty HintTextProperty =
            DependencyProperty.Register(
                nameof(HintText),
                typeof(string),
                typeof(QuickHintBubble),
                new PropertyMetadata(string.Empty, OnHintTextChanged));

        public string HintText
        {
            get => (string)GetValue(HintTextProperty);
            set => SetValue(HintTextProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(QuickHintBubble),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty ExtraContentProperty =
            DependencyProperty.Register(
                nameof(ExtraContent),
                typeof(object),
                typeof(QuickHintBubble),
                new PropertyMetadata(null));

        public object ExtraContent
        {
            get => GetValue(ExtraContentProperty);
            set => SetValue(ExtraContentProperty, value);
        }

        public static readonly DependencyProperty BubbleBackgroundProperty =
            DependencyProperty.Register(
                nameof(BubbleBackground),
                typeof(Brush),
                typeof(QuickHintBubble),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#62B0E2"))));

        public Brush BubbleBackground
        {
            get => (Brush)GetValue(BubbleBackgroundProperty);
            set => SetValue(BubbleBackgroundProperty, value);
        }

        public static readonly DependencyProperty BubbleForegroundProperty =
            DependencyProperty.Register(
                nameof(BubbleForeground),
                typeof(Brush),
                typeof(QuickHintBubble),
                new PropertyMetadata(Brushes.White));

        public Brush BubbleForeground
        {
            get => (Brush)GetValue(BubbleForegroundProperty);
            set => SetValue(BubbleForegroundProperty, value);
        }

        // PropertyChangedCallback: 受け取った文字列の先頭改行/空白を除去する
        private static void OnHintTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not QuickHintBubble ctrl) return;
            if (ctrl._isUpdatingText) return;

            string? newVal = e.NewValue as string;
            if (string.IsNullOrEmpty(newVal)) return;

            string trimmed = TrimLeadingBlankLines(newVal);
            if (trimmed != newVal)
            {
                try
                {
                    ctrl._isUpdatingText = true;
                    // SetCurrentValue でも可。 recursion を防ぐためフラグを利用。
                    ctrl.SetValue(HintTextProperty, trimmed);
                }
                finally
                {
                    ctrl._isUpdatingText = false;
                }
            }
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not QuickHintBubble ctrl) return;
            if (ctrl._isUpdatingText) return;

            string? newVal = e.NewValue as string;
            if (string.IsNullOrEmpty(newVal)) return;

            string trimmed = TrimLeadingBlankLines(newVal);
            if (trimmed != newVal)
            {
                try
                {
                    ctrl._isUpdatingText = true;
                    ctrl.SetValue(TitleProperty, trimmed);
                }
                finally
                {
                    ctrl._isUpdatingText = false;
                }
            }
        }

        // 先頭の BOM / 空白 / 改行を取り除く (先頭のみ)
        private static string TrimLeadingBlankLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            int i = 0;
            // 除去対象文字: CR/LF, BOM, タブ, 空白
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\r' || c == '\n' || c == '\uFEFF' || c == '\u200B' || c == '\t' || c == ' ')
                {
                    i++;
                    continue;
                }
                break;
            }

            if (i == 0) return s;
            return s.Substring(i);
        }
    }
}