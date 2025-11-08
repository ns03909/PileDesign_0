using System; // 追加
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading; // 追加

namespace PileDesign.Views
{
    public partial class ShortcutKeysWindow : Window
    {
        private bool _isClosing; // 追加

        public ShortcutKeysWindow()
        {
            InitializeComponent();

            // 必要に応じて調整（ツールチップに記載のあるキーも含む）
            var items = new List<ShortcutItem>
            {
                new("新規", "Ctrl + N"),
                new("開く", "Ctrl + O"),
                new("保存", "Ctrl + S"),
                new("名前を付けて保存", "Ctrl + Shift + S"),
                new("ヘルプ", "F1"),
                new("元に戻す", "Ctrl + Z"),
                new("やり直し", "Ctrl + Y"),
                new("すべて選択", "Ctrl + Shift + A"),
                new("全選択解除", "Esc"),
                new("すべてアクティブ", "Ctrl + A"),
                new("アクティブ", "F2"),
                new("非アクティブ", "Shift + F2"),
                new("ズームフィット", "Ctrl + 0"),
                new("ビュー: 平面", "Ctrl + Shift + T"),
                new("ビュー: 右面", "Ctrl + Shift + R"),
                new("ビュー: 正面", "Ctrl + Shift + F"),
                new("ビュー: アイソメ", "Ctrl + Shift + I"),
                new("要素追加モード", "Alt + 1"),
                new("要素の節点分割", "Alt + 7"),
            };

            GridShortcuts.ItemsSource = items;

            // Closing中フラグを設定
            this.Closing += (_, __) => _isClosing = true;
        }

        private record ShortcutItem(string Name, string Shortcut);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                SafeClose();
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            // フォーカスが外れたら自動的に閉じる（安全版）
            SafeClose();
        }

        // 再入防止・非同期で安全にClose
        private void SafeClose()
        {
            if (_isClosing) return;
            if (!IsVisible) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing || !IsVisible) return;
                try { Close(); } catch { /* no-op */ }
            }), DispatcherPriority.Background);
        }
    }
}