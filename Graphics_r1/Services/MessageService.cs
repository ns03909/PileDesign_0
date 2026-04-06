using System.Windows;

namespace PileDesign.Services
{
    /// <summary>
    /// MessageBox表示の一元管理サービス。
    /// ViewModelからのメッセージ表示を統一し、将来的なカスタムダイアログへの移行を容易にする。
    /// </summary>
    public static class MessageService
    {
        /// <summary>情報メッセージを表示</summary>
        public static void ShowInfo(string message, string title = "情報")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>警告メッセージを表示</summary>
        public static void ShowWarning(string message, string title = "警告")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>エラーメッセージを表示</summary>
        public static void ShowError(string message, string title = "エラー")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>確認ダイアログを表示（Yes/No）</summary>
        public static bool Confirm(string message, string title = "確認")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        /// <summary>確認ダイアログを表示（Yes/No/Cancel）</summary>
        public static MessageBoxResult ConfirmWithCancel(string message, string title = "確認")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }
    }
}
