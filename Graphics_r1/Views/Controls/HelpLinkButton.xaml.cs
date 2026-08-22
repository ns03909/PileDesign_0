using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// help.html の特定の位置へ飛ぶ小さなリンク。
    ///
    /// 各ウィンドウにヘルプの入口を置くための共通部品。code-behind に
    /// <c>new HelpWindow("anchor")</c> を書いて回ると、開き方 (別 UI スレッド / モーダル)
    /// がウィンドウごとにばらつくので、ここに一本化する。
    ///
    /// <c>Anchor</c> は help.html の id。実在するかは HelpAnchorTests が検査する。
    /// </summary>
    public partial class HelpLinkButton : UserControl
    {
        public HelpLinkButton()
        {
            InitializeComponent();
        }

        /// <summary>help.html のアンカー id (遷移先)</summary>
        public string Anchor
        {
            get => (string)GetValue(AnchorProperty);
            set => SetValue(AnchorProperty, value);
        }
        public static readonly DependencyProperty AnchorProperty =
            DependencyProperty.Register(nameof(Anchor), typeof(string), typeof(HelpLinkButton),
                new PropertyMetadata(string.Empty));

        /// <summary>
        /// アンカーで見つからなかったときにスクロール先を探す見出し文字列。
        /// 見出しに固定 id が無い箇所への保険。
        /// </summary>
        public string ScrollToTitle
        {
            get => (string)GetValue(ScrollToTitleProperty);
            set => SetValue(ScrollToTitleProperty, value);
        }
        public static readonly DependencyProperty ScrollToTitleProperty =
            DependencyProperty.Register(nameof(ScrollToTitle), typeof(string), typeof(HelpLinkButton),
                new PropertyMetadata(default(string)));

        /// <summary>リンクの表示文字列</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(HelpLinkButton),
                new PropertyMetadata("このウィンドウのヘルプ"));

        /// <summary>リンクのツールチップ</summary>
        public string LinkToolTip
        {
            get => (string)GetValue(LinkToolTipProperty);
            set => SetValue(LinkToolTipProperty, value);
        }
        public static readonly DependencyProperty LinkToolTipProperty =
            DependencyProperty.Register(nameof(LinkToolTip), typeof(string), typeof(HelpLinkButton),
                new PropertyMetadata("ヘルプの該当箇所を開きます。"));

        private void HelpLink_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Anchor))
                ViewModels.MainWindowViewModel.OpenHelpWindowAt(Anchor, ScrollToTitle);
        }
    }
}
