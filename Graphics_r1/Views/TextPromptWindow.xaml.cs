using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// 名前を 1 つ聞くだけの小さなダイアログ。OK で <see cref="Answer"/> に入力を入れて閉じる (空のままなら OK にしない)。
    /// </summary>
    public partial class TextPromptWindow : Window
    {
        public TextPromptWindow(string title, string prompt, string initial)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            AnswerBox.Text = initial;
            Loaded += (_, _) => { AnswerBox.Focus(); AnswerBox.SelectAll(); };
        }

        /// <summary>入力された文字 (前後の空白を除く)。キャンセルなら null。</summary>
        public string? Answer { get; private set; }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string text = AnswerBox.Text.Trim();
            if (text.Length == 0) { AnswerBox.Focus(); return; }
            Answer = text;
            DialogResult = true;
        }

        /// <summary>聞いて、OK なら入力を返す。キャンセルなら null。</summary>
        public static string? Ask(Window? owner, string title, string prompt, string initial)
        {
            var window = new TextPromptWindow(title, prompt, initial);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            return window.ShowDialog() == true ? window.Answer : null;
        }
    }
}
