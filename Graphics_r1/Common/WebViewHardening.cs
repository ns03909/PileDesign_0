using Microsoft.Web.WebView2.Wpf;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;

namespace PileDesign.Common
{
    /// <summary>
    /// 同梱の HTML を表示する WebView2 に、共通の枠をはめる。
    ///
    /// ヘルプと検証の 2 画面は表示先の URL を設定するだけで、遷移を止める処理も、
    /// 新しいウィンドウを開く要求を横取りする処理も無かった。ヘルプには外部サイトへの
    /// リンクがあり、いずれも新しいウィンドウで開く指定になっている。WebView2 の既定では
    /// <b>アドレスバーの無い小窓がアプリの中に開き</b>、そこから先は普通のブラウジングに
    /// なる。利用者にはこのプログラムの画面に見えるので、どこを見ているのか分からない。
    ///
    /// ここでは次の 3 つを課す。
    /// <list type="bullet">
    /// <item>同梱フォルダの外へは遷移させない。外部の URL は既定のブラウザへ渡す</item>
    /// <item>新しいウィンドウの要求も、アプリ内に開かず既定のブラウザへ渡す</item>
    /// <item>開発者ツールを無効にする</item>
    /// </list>
    ///
    /// あわせて作業フォルダを利用者のローカル領域へ移す。既定では実行ファイルの隣に
    /// 作られるため、読み取り専用の場所に置くと初期化に失敗してヘルプが開けない。
    /// 実行環境の確認は通るので、原因が分かりにくい形で失敗する。
    /// </summary>
    internal static class WebViewHardening
    {
        /// <summary>ブラウザの作業フォルダ。実行ファイルの隣ではなく利用者のローカル領域に置く。</summary>
        internal static string UserDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PileDesign", "WebView2");

        /// <summary>
        /// 表示先を設定する<b>前に</b>呼ぶこと。作業フォルダは初期化のときにしか効かない。
        /// </summary>
        internal static void UseLocalUserDataFolder(WebView2 view)
        {
            if (view == null) return;
            try
            {
                Directory.CreateDirectory(UserDataFolder);
                view.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = UserDataFolder,
                };
            }
            catch (Exception ex)
            {
                // 作れなければ既定 (exe の隣) のまま。ヘルプが開けるかどうかは環境次第。
                Log.Warning(ex, "[WebView2] 作業フォルダを用意できません: {Path}", UserDataFolder);
            }
        }

        /// <summary>
        /// 遷移の制限と開発者ツールの無効化を仕掛ける。
        /// </summary>
        /// <param name="view">対象の WebView2。</param>
        /// <param name="allowedFolder">
        /// この配下のファイルだけ表示を許す。通常は同梱の Help フォルダ。
        /// </param>
        internal static void RestrictToLocalContent(WebView2 view, string allowedFolder)
        {
            if (view == null) return;

            string root;
            try { root = Path.GetFullPath(allowedFolder); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WebView2] 表示を許すフォルダを解決できません: {Path}", allowedFolder);
                return;
            }

            view.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (!e.IsSuccess || view.CoreWebView2 == null)
                {
                    Log.Warning(e.InitializationException, "[WebView2] 初期化に失敗しました");
                    return;
                }

                try
                {
                    var settings = view.CoreWebView2.Settings;
                    settings.AreDevToolsEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.IsStatusBarEnabled = false;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[WebView2] 表示設定を変更できません");
                }

                view.CoreWebView2.NavigationStarting += (s2, args) =>
                {
                    if (IsInside(args.Uri, root)) return;

                    args.Cancel = true;
                    OpenInDefaultBrowser(args.Uri);
                };

                // target="_blank" などの新しいウィンドウ要求。
                // 止めないとアドレスバーの無い小窓がアプリの中に開く。
                view.CoreWebView2.NewWindowRequested += (s2, args) =>
                {
                    args.Handled = true;
                    if (IsInside(args.Uri, root))
                    {
                        // 同梱の資料なら、同じ画面で開く
                        try { view.CoreWebView2.Navigate(args.Uri); }
                        catch (Exception ex) { Log.Warning(ex, "[WebView2] 同梱資料へ移動できません: {Uri}", args.Uri); }
                        return;
                    }
                    OpenInDefaultBrowser(args.Uri);
                };
            };
        }

        /// <summary>同梱フォルダ配下のローカルファイルか。</summary>
        private static bool IsInside(string uri, string root)
        {
            if (string.IsNullOrEmpty(uri)) return false;

            // about:blank は WebView2 が初期化のときに自分で開く
            if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
            if (!parsed.IsFile) return false;

            try
            {
                string full = Path.GetFullPath(parsed.LocalPath);
                // 「Help」と「HelpOther」を取り違えないよう、区切り文字まで含めて比べる
                string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WebView2] パスを解決できません: {Uri}", uri);
                return false;
            }
        }

        /// <summary>外部の URL を既定のブラウザへ渡す。http/https だけを通す。</summary>
        private static void OpenInDefaultBrowser(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                Log.Warning("[WebView2] 解釈できない URL を止めました: {Uri}", uri);
                return;
            }

            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            {
                // file: 以外の任意のスキーム (ms-settings: など) をそのまま起動しない
                Log.Warning("[WebView2] 想定外のスキームを止めました: {Uri}", uri);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
                Log.Information("[WebView2] 外部リンクを既定のブラウザで開きました: {Uri}", parsed.AbsoluteUri);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WebView2] 既定のブラウザで開けません: {Uri}", parsed.AbsoluteUri);
            }
        }
    }
}
