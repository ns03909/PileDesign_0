using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PileDesign.Services;

namespace PileDesign.Views
{
    /// <summary>
    /// help.html を全文検索する簡易チャットウィンドウ。
    /// LLM 連携なし、オフライン専用 (bigram 検索)。
    /// </summary>
    public partial class HelpChatWindow : Window
    {
        private static readonly Brush UserBubbleBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFB));
        private static readonly Brush BotBubbleBrush = Brushes.White;
        private static readonly Brush BorderBrushColor = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xE0));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x80));
        private static readonly Brush SnippetBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
        private static readonly Brush LinkBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x55, 0xC9));
        private static readonly Brush PathBrush = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x96));

        public HelpChatWindow()
        {
            InitializeComponent();
            UserBubbleBrush.Freeze();
            BorderBrushColor.Freeze();
            MutedBrush.Freeze();
            SnippetBrush.Freeze();
            LinkBrush.Freeze();
            PathBrush.Freeze();

            Loaded += (s, e) =>
            {
                AddBotGreeting();
                InputTextBox.Focus();
            };
        }

        private void AddBotGreeting()
        {
            var greeting = "こんにちは。このプログラムの使い方や機能について、わからないことを質問してください。\n" +
                           "ヘルプ文書 (help.html) を検索して、関連する項目を表示します。";
            var bubble = CreateBotBubble();
            var text = new TextBlock
            {
                Text = greeting,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            };
            ((StackPanel)bubble.Child).Children.Add(text);

            var examples = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = MutedBrush,
                FontSize = 11,
            };
            examples.Inlines.Add(new Run("質問例: "));
            AddExampleLink(examples, "杭頭の M-θ");
            examples.Inlines.Add(new Run("  /  "));
            AddExampleLink(examples, "任意地盤変位");
            examples.Inlines.Add(new Run("  /  "));
            AddExampleLink(examples, "計算書出力");
            examples.Inlines.Add(new Run("  /  "));
            AddExampleLink(examples, "自動基礎梁");
            ((StackPanel)bubble.Child).Children.Add(examples);

            ChatPanel.Children.Add(bubble);
        }

        private void AddExampleLink(TextBlock host, string query)
        {
            var link = new Hyperlink(new Run(query)) { Foreground = LinkBrush };
            link.Click += (s, e) =>
            {
                InputTextBox.Text = query;
                Submit();
            };
            host.Inlines.Add(link);
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                Submit();
                e.Handled = true;
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e) => Submit();

        private void Submit()
        {
            var query = InputTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(query)) return;

            InputTextBox.Clear();
            AddUserBubble(query);

            IReadOnlyList<HelpSearchService.SearchResult> results;
            try
            {
                results = HelpSearchService.Instance.Search(query, maxResults: 6);
            }
            catch (Exception ex)
            {
                AddBotErrorBubble("検索中にエラーが発生しました: " + ex.Message);
                ScrollToEnd();
                return;
            }

            AddBotResultsBubble(query, results);
            ScrollToEnd();
        }

        private void AddUserBubble(string text)
        {
            var bubble = new Border
            {
                Background = UserBubbleBrush,
                BorderBrush = BorderBrushColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(60, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 460,
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            };
            ChatPanel.Children.Add(bubble);
        }

        private Border CreateBotBubble()
        {
            return new Border
            {
                Background = BotBubbleBrush,
                BorderBrush = BorderBrushColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 60, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 480,
                Child = new StackPanel(),
            };
        }

        private void AddBotErrorBubble(string text)
        {
            var bubble = CreateBotBubble();
            ((StackPanel)bubble.Child).Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.IndianRed,
            });
            ChatPanel.Children.Add(bubble);
        }

        private void AddBotResultsBubble(string query, IReadOnlyList<HelpSearchService.SearchResult> results)
        {
            var bubble = CreateBotBubble();
            var panel = (StackPanel)bubble.Child;

            if (results.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "「" + query + "」に関連する項目は見つかりませんでした。\n別のキーワードでお試しください (例: 杭頭, 沈下, M-φ)。",
                    TextWrapping = TextWrapping.Wrap,
                });
                ChatPanel.Children.Add(bubble);
                return;
            }

            panel.Children.Add(new TextBlock
            {
                Text = "関連する項目が " + results.Count + " 件見つかりました:",
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = MutedBrush,
                FontSize = 11,
            });

            int idx = 1;
            foreach (var r in results)
            {
                var row = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

                if (!string.IsNullOrEmpty(r.Section.TitlePath))
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = r.Section.TitlePath,
                        FontSize = 10,
                        Foreground = PathBrush,
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                var titleBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                titleBlock.Inlines.Add(new Run(idx + ". ") { Foreground = MutedBrush });
                var capturedSection = r.Section;
                var link = new Hyperlink(new Run(r.Section.Title))
                {
                    Foreground = LinkBrush,
                    FontWeight = FontWeights.SemiBold,
                };
                link.Click += (s, e) => OpenHelpFor(capturedSection);
                titleBlock.Inlines.Add(link);
                row.Children.Add(titleBlock);

                if (!string.IsNullOrEmpty(r.Snippet))
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = r.Snippet,
                        FontSize = 11,
                        Foreground = SnippetBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14, 1, 0, 0),
                    });
                }
                panel.Children.Add(row);
                idx++;
            }
            ChatPanel.Children.Add(bubble);
        }

        private void OpenHelpFor(HelpSearchService.HelpSection sec)
        {
            try
            {
                string? anchor = string.IsNullOrEmpty(sec.Id) ? null : sec.Id;
                string? scrollTitle = string.IsNullOrEmpty(sec.Id) ? sec.Title : null;
                ViewModels.MainWindowViewModel.OpenHelpWindowAt(anchor, scrollTitle);
            }
            catch (Exception ex)
            {
                MessageBox.Show("ヘルプを開けませんでした: " + ex.Message, "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ScrollToEnd()
        {
            ChatScroll.ScrollChanged += OnScrollChangedScrollToEnd;
            ChatScroll.UpdateLayout();
            ChatScroll.ScrollToEnd();
        }

        private void OnScrollChangedScrollToEnd(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange > 0)
            {
                ChatScroll.ScrollToEnd();
            }
            ChatScroll.ScrollChanged -= OnScrollChangedScrollToEnd;
        }
    }
}
