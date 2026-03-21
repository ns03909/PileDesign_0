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
                // ファイル操作
                new("新規", "Ctrl + N"),
                new("開く", "Ctrl + O"),
                new("保存", "Ctrl + S"),
                new("名前を付けて保存", "Ctrl + Shift + S"),

                // 入力ウィンドウ
                new("荷重ケース編集", "Ctrl + L"),
                new("地盤編集", "Ctrl + G"),
                new("杭体編集", "Ctrl + B"),
                new("軸力確認", "Ctrl + K"),
                new("自動梁要素生成", "Ctrl + Shift + B"),
                new("要素分割", "F4 / Ctrl + D"),

                // 解析
                new("水平解析実行", "F5"),
                new("単杭沈下解析実行", "F6"),
                new("解析前処理モード", "F7"),
                new("解析後処理モード", "Ctrl + F7"),

                // ヘルプ
                new("ヘルプ", "F1"),
                new("クイックヒント", "Shift + F1"),
                new("ショートカット一覧", "Ctrl + /"),

                // 編集
                new("元に戻す", "Ctrl + Z"),
                new("やり直し", "Ctrl + Y"),
                new("すべて選択", "Ctrl + Shift + A"),
                new("全選択解除", "Esc"),
                new("選択した節点の削除", "Delete"),
                new("すべてアクティブ", "Ctrl + A"),
                new("アクティブ", "F2"),
                new("非アクティブ", "Shift + F2"),

                // ビュー
                new("ズームフィット", "Ctrl + 0"),
                new("ビュー: 平面", "Ctrl + Shift + T"),
                new("ビュー: 右面", "Ctrl + Shift + R"),
                new("ビュー: 正面", "Ctrl + Shift + F"),
                new("ビュー: アイソメ", "Ctrl + Shift + I"),

                // 編集モード
                new("要素追加モード", "Alt + 1"),
                new("選択モード（通常）", "Alt + 0"),
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