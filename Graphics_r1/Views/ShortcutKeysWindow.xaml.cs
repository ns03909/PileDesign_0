using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace PileDesign.Views
{
    public partial class ShortcutKeysWindow : Window
    {
        private bool _isClosing;

        public ShortcutKeysWindow()
        {
            InitializeComponent();

            var items = new List<ShortcutItem>
            {
                // ファイル
                new("ファイル", "新規", "Ctrl + N"),
                new("ファイル", "開く", "Ctrl + O"),
                new("ファイル", "保存", "Ctrl + S"),
                new("ファイル", "名前を付けて保存", "Ctrl + Shift + S"),
                new("ファイル", "計算書 (Word) の出力", "Ctrl + Shift + W"),

                // 入力ウィンドウ
                new("入力ウィンドウ", "基本設定", "Ctrl + E"),
                new("入力ウィンドウ", "荷重ケース編集", "Ctrl + L"),
                new("入力ウィンドウ", "地盤編集", "Ctrl + G"),
                new("入力ウィンドウ", "杭体編集", "Ctrl + B"),
                new("入力ウィンドウ", "軸力確認", "Ctrl + K"),
                new("入力ウィンドウ", "自動梁要素生成", "Ctrl + Shift + B"),
                new("入力ウィンドウ", "杭要素分割", "F4 / Ctrl + D"),

                // 解析
                // F5 / F6 / Shift + F6 は「ウィンドウを開く」。実行はウィンドウの中で行う。
                // 直接実行するのは群杭沈下 (F7) だけ。「実行」と書くと押した瞬間に走ると読める。
                new("解析", "水平解析ウィンドウを開く", "F5"),
                new("解析", "単杭沈下解析ウィンドウを開く", "F6"),
                new("解析", "単杭沈下解析（基礎梁考慮）ウィンドウを開く", "Shift + F6"),
                new("解析", "群杭沈下解析（一般）を実行", "F7"),
                new("解析", "群杭沈下タブへ移動", "F8"),
                new("解析", "解析実行（水平解析ウィンドウ内）", "F5"),
                new("解析", "追加解析実行（水平解析ウィンドウ内）", "Ctrl + F5"),

                // 編集
                new("編集", "元に戻す", "Ctrl + Z"),
                new("編集", "やり直し", "Ctrl + Y"),
                new("編集", "すべて選択", "Ctrl + A"),
                new("編集", "全選択解除", "Esc"),
                new("編集", "選択した節点の削除", "Delete"),
                new("編集", "すべてアクティブ", "Ctrl + Shift + A"),
                new("編集", "アクティブ", "F2"),
                new("編集", "非アクティブ", "Shift + F2"),

                // 編集モード
                new("編集モード", "要素追加モード", "Alt + 1"),
                new("編集モード", "選択モード（通常）", "Alt + 0"),
                new("編集モード", "要素の節点分割", "Alt + 7"),

                // ビュー
                new("ビュー", "ズームフィット", "Ctrl + 0"),
                new("ビュー", "平面", "Ctrl + Shift + T"),
                new("ビュー", "右面", "Ctrl + Shift + R"),
                new("ビュー", "正面", "Ctrl + Shift + F"),
                new("ビュー", "アイソメ", "Ctrl + Shift + I"),

                // 説明用
                new("説明用", "INPUT 表示切替", "F12"),

                // ダイアログ共通
                new("ダイアログ共通", "OK・実行（編集ウィンドウの保存）", "Ctrl + Enter"),
                new("ダイアログ共通", "キャンセル・閉じる", "Esc"),
                new("ダイアログ共通", "元に戻す / やり直し", "Ctrl + Z / Ctrl + Y"),

                // ヘルプ
                new("ヘルプ", "ヘルプ", "F1"),
                new("ヘルプ", "クイックヒント", "Shift + F1"),
                new("ヘルプ", "ショートカット一覧", "Ctrl + /"),
                new("ヘルプ", "コマンドパレット", "Ctrl + Shift + P"),
            };

            var view = CollectionViewSource.GetDefaultView(items);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            GridShortcuts.ItemsSource = view;

            this.Closing += (_, __) => _isClosing = true;
        }

        private record ShortcutItem(string Category, string Name, string Shortcut);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                SafeClose();
        }

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
