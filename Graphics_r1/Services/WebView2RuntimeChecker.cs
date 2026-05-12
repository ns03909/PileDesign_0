using Microsoft.Web.WebView2.Core;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PileDesign.Services
{
    /// <summary>
    /// WebView2 Evergreen Runtime のインストール状況をチェックし、未インストール時には
    /// 同梱した Evergreen Bootstrapper (MicrosoftEdgeWebview2Setup.exe) を起動して
    /// per-user (管理者不要) インストールを試みる。
    ///
    /// 本プログラムは csproj で WebView2 Runtime を同梱せず SDK のみ参照しているため
    /// (-509MB)、ターゲット PC に Evergreen Runtime が無いと WebView2 を使う
    /// HelpWindow / VerificationWindow でクラッシュする。
    /// Bootstrapper は ~3MB の小さい再配布可能インストーラで、非管理者ユーザーで
    /// 実行すると自動的に per-user インストールにフォールバックする。
    ///
    /// 同梱ファイル: {App}/MicrosoftEdgeWebview2Setup.exe
    /// ダウンロード元: https://developer.microsoft.com/microsoft-edge/webview2/
    /// </summary>
    public static class WebView2RuntimeChecker
    {
        private const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
        private const string BootstrapperFileName = "MicrosoftEdgeWebview2Setup.exe";

        private static bool? _cachedResult;

        /// <summary>
        /// WebView2 Evergreen Runtime がインストール済みか。結果はキャッシュ。
        /// </summary>
        public static bool IsRuntimeInstalled()
        {
            if (_cachedResult.HasValue) return _cachedResult.Value;
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                bool installed = !string.IsNullOrEmpty(version);
                Log.Information("[WebView2] Runtime version: {Version}", version ?? "(none)");
                _cachedResult = installed;
                return installed;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WebView2] Runtime detection failed");
                _cachedResult = false;
                return false;
            }
        }

        /// <summary>
        /// 同梱した Bootstrapper のフルパスを返す (存在しない場合は null)。
        /// </summary>
        private static string? FindBundledBootstrapper()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BootstrapperFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Runtime 未インストール時の自動インストールフローを起動。
        /// Bootstrapper 同梱版は同梱インストーラを実行 (per-user/管理者不要)、
        /// 同梱なし版は Microsoft 公式ダウンロードページを開く。
        /// 戻り値: インストール成功時 true。失敗 or ユーザーキャンセル時 false。
        /// </summary>
        public static bool ShowMissingRuntimeDialog()
        {
            var bootstrapper = FindBundledBootstrapper();

            if (bootstrapper != null)
            {
                return TryAutoInstallWithBootstrapper(bootstrapper);
            }
            else
            {
                ShowDownloadPageFallback();
                return false;
            }
        }

        /// <summary>
        /// 同梱 Bootstrapper を per-user モードで起動。インストール完了後に Runtime 再検出。
        /// </summary>
        private static bool TryAutoInstallWithBootstrapper(string bootstrapperPath)
        {
            var msg = "ヘルプ機能を使うには Microsoft Edge WebView2 Runtime が必要です。\n\n" +
                      "今すぐインストールしますか? (管理者権限不要、~3 分)\n\n" +
                      "• 「はい」: 同梱インストーラを起動します。\n" +
                      "  インストール完了後、PileDesign を再起動してください。\n" +
                      "• 「いいえ」: 後で手動でインストールする場合に選択してください。";
            var result = MessageBox.Show(msg, "WebView2 Runtime が必要です",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes) return false;

            try
            {
                Log.Information("[WebView2] Bootstrapper を起動: {Path}", bootstrapperPath);
                // /install : 静かにインストール。/silent はサイレント (管理者でない場合は per-user に自動切替)
                var psi = new ProcessStartInfo(bootstrapperPath)
                {
                    UseShellExecute = true,
                    Verb = "open", // 管理者昇格は要求しない (per-user 用)
                };
                var proc = Process.Start(psi);
                if (proc == null)
                {
                    Log.Warning("[WebView2] Bootstrapper の起動に失敗 (Process.Start が null)");
                    ShowDownloadPageFallback();
                    return false;
                }

                // 完了を待たずに「インストール後再起動してください」と案内
                MessageBox.Show(
                    "WebView2 Runtime インストーラを起動しました。\n" +
                    "完了後、PileDesign を再起動してください。",
                    "インストール開始", MessageBoxButton.OK, MessageBoxImage.Information);

                // Runtime キャッシュをクリア (次回起動時に再検出させる)
                _cachedResult = null;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WebView2] Bootstrapper の起動に失敗");
                MessageBox.Show(
                    $"インストーラの起動に失敗しました。\n{ex.Message}\n\n" +
                    "公式サイトから手動でダウンロードしてください。",
                    "インストール失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowDownloadPageFallback();
                return false;
            }
        }

        /// <summary>
        /// Bootstrapper 未同梱時のフォールバック: Microsoft 公式サイトを既定ブラウザで開く。
        /// </summary>
        private static void ShowDownloadPageFallback()
        {
            var msg = "ヘルプ機能を使うには Microsoft Edge WebView2 Runtime が必要です。\n\n" +
                      "「はい」で Microsoft 公式ダウンロードページを開きます。\n" +
                      "ダウンロードした Evergreen Bootstrapper を実行してインストール後、\n" +
                      "PileDesign を再起動してください。\n\n" +
                      $"URL: {DownloadUrl}";
            var result = MessageBox.Show(msg, "WebView2 Runtime が必要です",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[WebView2] ダウンロードページの起動に失敗");
                    MessageBox.Show($"ブラウザが開けませんでした。手動で以下にアクセスしてください:\n{DownloadUrl}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
