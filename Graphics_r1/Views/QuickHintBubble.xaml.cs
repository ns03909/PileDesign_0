using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views
{
    public partial class QuickHintBubble : UserControl
    {
        public static readonly DependencyProperty HintTextProperty =
            DependencyProperty.Register(
                nameof(HintText),
                typeof(string),
                typeof(QuickHintBubble),
                new PropertyMetadata(string.Empty));

        public string HintText
        {
            get => (string)GetValue(HintTextProperty);
            set => SetValue(HintTextProperty, value);
        }

        public QuickHintBubble()
        {
            InitializeComponent();
        }
    }
}