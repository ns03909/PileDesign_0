using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PileDesign.Common
{
    /// <summary>
    /// WindowにEscキーでクローズする機能を追加するAttached Behavior
    /// </summary>
    public static class EscapeKeyBehavior
    {
        // Windowごとに閉じる処理中かを管理する辞書
        private static readonly Dictionary<Window, bool> ClosingFlags = new();

        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(EscapeKeyBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject element, bool value)
            => element.SetValue(EnableProperty, value);

        public static bool GetEnable(DependencyObject element)
            => (bool)element.GetValue(EnableProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;

            if ((bool)e.NewValue)
            {
                // イベントハンドラー登録
                window.PreviewKeyDown += Window_PreviewKeyDown;
                window.Closing += Window_Closing;
                window.Closed += Window_Closed;
            }
            else
            {
                // イベントハンドラー解除
                window.PreviewKeyDown -= Window_PreviewKeyDown;
                window.Closing -= Window_Closing;
                window.Closed -= Window_Closed;

                // 辞書からエントリを削除
                if (ClosingFlags.ContainsKey(window))
                    ClosingFlags.Remove(window);
            }
        }

        private static void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Window window) return;

            // Escキーが押された場合
            if (e.Key == Key.Escape)
            {
                // DataGrid編集中の場合は編集をキャンセル（1回目のEsc）
                // 編集中でなければウィンドウをクローズ（2回目のEsc）
                var focusedElement = Keyboard.FocusedElement as FrameworkElement;

                // DataGridセル編集中かチェック
                if (focusedElement != null && IsEditingDataGridCell(focusedElement))
                {
                    // DataGridの編集キャンセルに任せる（e.Handledしない）
                    return;
                }

                // ウィンドウを閉じる
                SafeClose(window);
                e.Handled = true;
            }
        }

        private static void Window_Closing(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                // Closing中フラグを設定
                ClosingFlags[window] = true;
            }
        }

        private static void Window_Closed(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                // Closed時に辞書からクリーンアップ
                if (ClosingFlags.ContainsKey(window))
                    ClosingFlags.Remove(window);
            }
        }

        /// <summary>
        /// 安全にウィンドウを閉じる（再入防止、非同期処理）
        /// ShortcutKeysWindow.xaml.csのSafeClose()パターンを使用
        /// </summary>
        private static void SafeClose(Window window)
        {
            if (window == null) return;

            // 既に閉じている、または閉じる処理中の場合はスキップ
            if (ClosingFlags.TryGetValue(window, out bool isClosing) && isClosing)
                return;

            if (!window.IsVisible)
                return;

            // 非同期でClose()を呼び出し（UIスレッドの再入を防ぐ）
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 再度チェック（BeginInvokeで遅延実行されるため）
                if (ClosingFlags.TryGetValue(window, out bool isClosing2) && isClosing2)
                    return;

                if (!window.IsVisible)
                    return;

                try
                {
                    window.Close();
                }
                catch
                {
                    // Close()中の例外を無視（既に閉じられている等）
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// フォーカス中の要素がDataGridのセル編集中かを判定
        /// </summary>
        private static bool IsEditingDataGridCell(FrameworkElement element)
        {
            // 親要素を辿ってDataGridCellを探す
            DependencyObject current = element;
            while (current != null)
            {
                if (current is System.Windows.Controls.DataGridCell cell)
                {
                    // セルが編集モードかチェック
                    return cell.IsEditing;
                }
                // Run などの Visual でない要素は LogicalTree にフォールバック (例外回避)
                if (current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D)
                {
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                }
                else
                {
                    current = System.Windows.LogicalTreeHelper.GetParent(current);
                }
            }
            return false;
        }
    }
}
