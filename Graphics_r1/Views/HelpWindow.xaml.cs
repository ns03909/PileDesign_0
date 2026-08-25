using Serilog;
using System;
using System.IO;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// WindowHelp.xaml の相互作用ロジック
    /// </summary>
    public partial class HelpWindow : Window
    {
        private readonly string _baseFilePath;
        private string? _pendingScrollTitle;
        private string? _pendingAnchorId;

        public HelpWindow(string? anchor = null, string? scrollToTitle = null)
        {
            InitializeComponent();

            // Single-File 公開時は AppDomain.CurrentDomain.BaseDirectory が exe と同じディレクトリを指す。
            // ファイル名は実ディスクと完全一致 (lowercase) させて WebView2 の URI 解決を確実にする。
            _baseFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "help.html");
            _pendingScrollTitle = scrollToTitle;
            _pendingAnchorId = anchor;

            // ファイル存在チェック (publish 時の見落とし検出)
            if (!File.Exists(_baseFilePath))
            {
                Log.Warning("[HelpWindow] help.html が見つかりません: {Path}", _baseFilePath);
                Services.MessageService.Show($"ヘルプファイルが見つかりません。\n{_baseFilePath}\n\nアプリケーションが正しく配置されていない可能性があります。",
                    "ヘルプエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // WebView2 Runtime インストール確認 (Evergreen Runtime が無いと WebView2 がクラッシュする)
            if (!Services.WebView2RuntimeChecker.IsRuntimeInstalled())
            {
                Services.WebView2RuntimeChecker.ShowMissingRuntimeDialog();
                return;
            }

            HelpWebView.Source = BuildUri(anchor);

            HelpWebView.NavigationCompleted += async (s, e) =>
            {
                try
                {
                    if (!e.IsSuccess) return;

                    var version = ViewModels.MainWindowViewModel.AppVersion;
                    try
                    {
                        // バージョンテキスト設定と同時に visibility:visible へ。
                        // help.html 側で初期値 visibility:hidden にしているため、
                        // 古い (誤った) バージョン文字列が一瞬見える問題が解消される。
                        await HelpWebView.ExecuteScriptAsync($@"
                            var el = document.getElementById('app-version');
                            if (el) {{
                                el.textContent = 'ver {version}';
                                el.style.visibility = 'visible';
                            }}
                        ");
                    }
                    catch (Exception scriptEx)
                    {
                        Log.Warning(scriptEx, "[HelpWindow] バージョン更新スクリプト失敗");
                    }

                    // 初回ロード時は WebView2 / DOMContentLoaded / numberHeadings 完了を JS 側で polling して待つ。
                    if (!string.IsNullOrEmpty(_pendingAnchorId) || !string.IsNullOrEmpty(_pendingScrollTitle))
                    {
                        var anchorId = _pendingAnchorId ?? "";
                        var scrollTitle = _pendingScrollTitle ?? "";
                        _pendingAnchorId = null;
                        _pendingScrollTitle = null;
                        await WaitAndScrollAsync(anchorId, scrollTitle);
                    }
                }
                catch (Exception navEx)
                {
                    // NavigationCompleted は WebView2 内部スレッドから fire されることがある。
                    // ここで例外を漏らすと AppDomain.UnhandledException 経由でアプリがダウンする可能性がある。
                    Log.Warning(navEx, "[HelpWindow] NavigationCompleted ハンドラ例外");
                }
            };
        }

        /// <summary>
        /// 初回ロード時、numberHeadings() の完了 (= 最初の h3 に数字 prefix が付くまで) を polling 待機してからスクロール。
        /// </summary>
        private async System.Threading.Tasks.Task WaitAndScrollAsync(string anchorId, string scrollTitle)
        {
            try
            {
                var idJson = System.Text.Json.JsonSerializer.Serialize(anchorId ?? "");
                var titleJson = System.Text.Json.JsonSerializer.Serialize(scrollTitle ?? "");
                var script = @"
                    (function(targetId, targetTitle) {
                        var prefixRegex = /^(?:第\d+部\s*|第\d+章\s*|\d+(?:\.\d+)+\s+)/;
                        var attempts = 0;
                        var maxAttempts = 150; // 最大 約 6 秒 (20ms × 50 + 100ms × 100)

                        function scrollNow() {
                            var found = null;
                            // anchor id 優先
                            if (targetId) {
                                found = document.getElementById(targetId);
                            }
                            // title fallback
                            if (!found && targetTitle) {
                                var hs = document.querySelectorAll('#mainText h1, #mainText h2, #mainText h3, #mainText h4, #mainText h5, #mainText h6');
                                var t = targetTitle.trim();
                                // Pass 1: 完全一致 / prefix除去後一致
                                for (var i = 0; i < hs.length; i++) {
                                    var actual = hs[i].textContent.trim();
                                    if (actual === t) { found = hs[i]; break; }
                                    var stripped = actual.replace(prefixRegex, '').trim();
                                    if (stripped === t) { found = hs[i]; break; }
                                }
                                // Pass 2: endsWith
                                if (!found) {
                                    for (var i = 0; i < hs.length; i++) {
                                        if (hs[i].textContent.trim().endsWith(t)) { found = hs[i]; break; }
                                    }
                                }
                                // Pass 3: 部分一致
                                if (!found) {
                                    for (var i = 0; i < hs.length; i++) {
                                        if (hs[i].textContent.indexOf(t) !== -1) { found = hs[i]; break; }
                                    }
                                }
                            }
                            if (found) {
                                found.scrollIntoView({ behavior: 'auto', block: 'start' });
                                var prev = found.style.backgroundColor;
                                found.style.backgroundColor = '#fff8a3';
                                found.style.transition = 'background-color 1.5s ease';
                                var elRef = found;
                                setTimeout(function() { elRef.style.backgroundColor = prev || ''; }, 2200);
                                return true;
                            }
                            return false;
                        }

                        // 見出し番号付けが済んだか。
                        // help.html 側は「番号付け → 目的位置へスクロール → (1 フレーム後) 目次生成」
                        // の順で走るため、この判定が真になった時点で本文は既に目的の位置にある。
                        // ここでの scrollNow() は位置の取り直しと黄色いハイライトが目的。
                        function isNumberingDone() {
                            if (window.__helpReady) return true;
                            var firstH3 = document.querySelector('#mainText h3');
                            if (firstH3) {
                                return /^\d+\.\d+/.test(firstH3.textContent.trim()) ||
                                       /^第/.test(firstH3.textContent.trim());
                            }
                            var firstH2 = document.querySelector('#mainText h2');
                            return firstH2 && /^第\d+章/.test(firstH2.textContent.trim());
                        }

                        function tryOnce() {
                            attempts++;
                            if (document.readyState !== 'loading' && isNumberingDone()) {
                                scrollNow();
                            } else if (attempts < maxAttempts) {
                                // 最初の 1 秒は細かく見る (準備が整った直後に動きたい)
                                setTimeout(tryOnce, attempts < 50 ? 20 : 100);
                            } else {
                                // タイムアウト - ベストエフォートでスクロール
                                scrollNow();
                            }
                        }
                        tryOnce();
                    })(" + idJson + ", " + titleJson + ");";
                await HelpWebView.ExecuteScriptAsync(script);
            }
            catch { /* ignore script errors */ }
        }

        private Uri BuildUri(string? anchor)
        {
            var url = "file:///" + _baseFilePath.Replace("\\", "/");
            if (!string.IsNullOrEmpty(anchor)) url += "#" + anchor;
            return new Uri(url);
        }

        /// <summary>
        /// 既存のヘルプウィンドウを再ナビゲートする (ヘルプチャットからの遷移用)。
        /// anchor がある場合は #anchor へ navigate、scrollToTitle がある場合は JS で見出しを検索してスクロール。
        /// </summary>
        public void NavigateTo(string? anchor, string? scrollToTitle)
        {
            _pendingScrollTitle = scrollToTitle;
            _pendingAnchorId = anchor;
            var newUri = BuildUri(anchor);

            if (HelpWebView.Source != null && HelpWebView.Source.Equals(newUri))
            {
                // 同じ URL への再ナビゲーションは発火しないので JS で直接スクロール
                if (!string.IsNullOrEmpty(anchor))
                {
                    var id = anchor;
                    _pendingAnchorId = null;
                    _ = ScrollToAnchorIdAsync(id!);
                }
                if (!string.IsNullOrEmpty(scrollToTitle))
                {
                    var title = scrollToTitle;
                    _pendingScrollTitle = null;
                    _ = ScrollToHeadingTitleAsync(title!);
                }
            }
            else
            {
                HelpWebView.Source = newUri;
            }
        }

        private async System.Threading.Tasks.Task ScrollToAnchorIdAsync(string id)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(id);
                var script = @"
                    (function(targetId) {
                        var el = document.getElementById(targetId);
                        if (!el) return;
                        el.scrollIntoView({ behavior: 'auto', block: 'start' });
                        var prev = el.style.backgroundColor;
                        el.style.backgroundColor = '#fff8a3';
                        el.style.transition = 'background-color 1.5s ease';
                        setTimeout(function() { el.style.backgroundColor = prev || ''; }, 2200);
                    })(" + json + ");";
                await HelpWebView.ExecuteScriptAsync(script);
            }
            catch { /* ignore script errors */ }
        }

        private async System.Threading.Tasks.Task ScrollToHeadingTitleAsync(string title)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(title);
                // numberHeadings() で実行時に「第1部 」「第1章 」「1.2 」「1.2.3 」等の prefix が付与されているので、
                // 完全一致 / prefix除去後一致 / endsWith / 部分一致 の優先順で検索する。
                var script = @"
                    (function(target) {
                        target = (target || '').trim();
                        if (!target) return;
                        var hs = document.querySelectorAll('#mainText h1, #mainText h2, #mainText h3, #mainText h4, #mainText h5, #mainText h6');
                        var prefixRegex = /^(?:第\d+部\s*|第\d+章\s*|\d+(?:\.\d+)+\s+)/;
                        var found = null;

                        // Pass 1: 完全一致 / prefix 除去後一致
                        for (var i = 0; i < hs.length; i++) {
                            var actual = hs[i].textContent.trim();
                            if (actual === target) { found = hs[i]; break; }
                            var stripped = actual.replace(prefixRegex, '').trim();
                            if (stripped === target) { found = hs[i]; break; }
                        }
                        // Pass 2: endsWith (target が prefix なし、actual に prefix あり)
                        if (!found) {
                            for (var i = 0; i < hs.length; i++) {
                                if (hs[i].textContent.trim().endsWith(target)) { found = hs[i]; break; }
                            }
                        }
                        // Pass 3: 部分一致 (last resort)
                        if (!found) {
                            for (var i = 0; i < hs.length; i++) {
                                if (hs[i].textContent.indexOf(target) !== -1) { found = hs[i]; break; }
                            }
                        }
                        if (!found) return;

                        found.scrollIntoView({ behavior: 'auto', block: 'start' });
                        var prev = found.style.backgroundColor;
                        found.style.backgroundColor = '#fff8a3';
                        found.style.transition = 'background-color 1.5s ease';
                        var target_el = found;
                        setTimeout(function() { target_el.style.backgroundColor = prev || ''; }, 2200);
                    })(" + json + ");";
                await HelpWebView.ExecuteScriptAsync(script);
            }
            catch { /* ignore script errors */ }
        }
    }
}
