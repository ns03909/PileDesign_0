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

        public HelpWindow(string? anchor = null, string? scrollToTitle = null)
        {
            InitializeComponent();

            _baseFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "Help.html");
            _pendingScrollTitle = scrollToTitle;

            HelpWebView.Source = BuildUri(anchor);

            HelpWebView.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess) return;

                var version = ViewModels.MainWindowViewModel.AppVersion;
                await HelpWebView.ExecuteScriptAsync($@"
                    var el = document.getElementById('app-version');
                    if (el) {{ el.textContent = 'ver {version}'; }}
                ");

                if (!string.IsNullOrEmpty(_pendingScrollTitle))
                {
                    var title = _pendingScrollTitle;
                    _pendingScrollTitle = null;
                    await ScrollToHeadingTitleAsync(title!);
                }
            };
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
            var newUri = BuildUri(anchor);

            if (HelpWebView.Source != null && HelpWebView.Source.Equals(newUri))
            {
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

        private async System.Threading.Tasks.Task ScrollToHeadingTitleAsync(string title)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(title);
                var script = @"
                    (function(target) {
                        target = (target || '').trim();
                        if (!target) return;
                        var hs = document.querySelectorAll('h2, h3, h4');
                        for (var i = 0; i < hs.length; i++) {
                            if (hs[i].textContent.trim() === target) {
                                hs[i].scrollIntoView({ behavior: 'auto', block: 'start' });
                                var prev = hs[i].style.backgroundColor;
                                hs[i].style.backgroundColor = '#fff8a3';
                                hs[i].style.transition = 'background-color 1.5s ease';
                                setTimeout(function() { hs[i].style.backgroundColor = prev || ''; }, 2200);
                                return;
                            }
                        }
                    })(" + json + ");";
                await HelpWebView.ExecuteScriptAsync(script);
            }
            catch { /* ignore script errors */ }
        }
    }
}
