using System;
using System.Windows;

namespace PileDesign.Services
{
    /// <summary>
    /// MessageBox 表示の一元管理サービス。
    /// owner = Application.Current.MainWindow を自動注入することで、
    /// 「メッセージダイアログ表示中に他ウィンドウを操作できる」WPF 標準挙動を防ぐ。
    ///
    /// 使い方:
    ///   - System.Windows.MessageBox.Show(...) と同シグネチャの MessageService.Show(...) を提供。
    ///     既存コードを `MessageBox.Show` → `MessageService.Show` に置換するだけで modal block が効く。
    ///   - ShowInfo / ShowWarning / ShowError / Confirm / ConfirmWithCancel は短縮 API。
    /// </summary>
    public static class MessageService
    {
        // owner は「現在アクティブなウィンドウ」を優先取得。
        // 例: 水平解析ウィンドウで操作中なら HorizontalCalculationWindow が owner となり、
        //     完了通知ダイアログがその上に表示される。アクティブウィンドウがなければ
        //     MainWindow にフォールバック。null の場合 (アプリ初期化前等) はオーナーなし。
        private static Window? Owner
        {
            get
            {
                var app = System.Windows.Application.Current;
                if (app == null) return null;
                Window? active = null;
                foreach (Window w in app.Windows)
                {
                    if (w.IsActive)
                    {
                        active = w;
                        break;
                    }
                }
                return active ?? app.MainWindow;
            }
        }

        /// <summary>
        /// 例外を伴うエラーを伝える。<b>例外を画面に出すときは必ずこれを通すこと。</b>
        ///
        /// README の約束は「詳細は Serilog に残し、画面には要約とログの場所を出す」。
        /// 実際には例外の文言を画面に出すだけでログに残していない箇所が大半で、
        /// 利用者が問い合わせても手掛かりが残っていない状態だった。
        ///
        /// 画面に出すのは要約と <c>ex.Message</c> まで。例外オブジェクトそのもの
        /// (型名・スタックトレース) は出さず、ログに送る。
        /// </summary>
        /// <param name="summary">何ができなかったか。利用者向けの文で書く。</param>
        /// <param name="ex">記録する例外。</param>
        /// <param name="caption">ダイアログのタイトル。</param>
        public static void ShowError(string summary, Exception ex, string caption = "エラー")
        {
            Serilog.Log.Error(ex, "[{Caption}] {Summary}", caption, summary);

            Show($"{summary}\n{ex.Message}\n\n詳細はログに記録しています（ヘルプ タブ → バージョン情報 → ログフォルダを開く）。",
                 caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>オーナーウィンドウを指定する <see cref="ShowError(string, Exception, string)"/>。</summary>
        public static void ShowError(Window owner, string summary, Exception ex, string caption = "エラー")
        {
            Serilog.Log.Error(ex, "[{Caption}] {Summary}", caption, summary);

            MessageBox.Show(owner,
                $"{summary}\n{ex.Message}\n\n詳細はログに記録しています（ヘルプ タブ → バージョン情報 → ログフォルダを開く）。",
                caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // === System.Windows.MessageBox.Show と同シグネチャの Show() overload群 ===
        // 既存コードの一括置換用 (MessageBox.Show → MessageService.Show)。
        // owner が取れる場合は注入し、modal block を有効化する。

        public static MessageBoxResult Show(string messageBoxText)
        {
            var o = Owner;
            return o != null ? MessageBox.Show(o, messageBoxText)
                             : MessageBox.Show(messageBoxText);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            var o = Owner;
            return o != null ? MessageBox.Show(o, messageBoxText, caption)
                             : MessageBox.Show(messageBoxText, caption);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            var o = Owner;
            return o != null ? MessageBox.Show(o, messageBoxText, caption, button)
                             : MessageBox.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var o = Owner;
            return o != null ? MessageBox.Show(o, messageBoxText, caption, button, icon)
                             : MessageBox.Show(messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            var o = Owner;
            return o != null ? MessageBox.Show(o, messageBoxText, caption, button, icon, defaultResult)
                             : MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }

        // Owner を明示的に指定したい場合のフォールバック (子ウィンドウから自身を owner にしたい等)。
        public static MessageBoxResult Show(Window owner, string messageBoxText)
            => MessageBox.Show(owner, messageBoxText);
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
            => MessageBox.Show(owner, messageBoxText, caption);
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
            => MessageBox.Show(owner, messageBoxText, caption, button);
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
            => MessageBox.Show(owner, messageBoxText, caption, button, icon);
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
            => MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult);

        // === 短縮 API (用途別) ===

        /// <summary>情報メッセージを表示</summary>
        public static void ShowInfo(string message, string title = "情報")
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        /// <summary>警告メッセージを表示</summary>
        public static void ShowWarning(string message, string title = "警告")
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>エラーメッセージを表示</summary>
        public static void ShowError(string message, string title = "エラー")
            => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        /// <summary>確認ダイアログを表示（Yes/No）</summary>
        public static bool Confirm(string message, string title = "確認")
            => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        /// <summary>確認ダイアログを表示（Yes/No/Cancel）</summary>
        public static MessageBoxResult ConfirmWithCancel(string message, string title = "確認")
            => Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
    }
}
