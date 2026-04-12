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
        private readonly string? _anchor;

        public HelpWindow(string? anchor = null)
        {
            InitializeComponent();
            _anchor = anchor;

            // WebView2 の初期化
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "Help.html");
            var url = "file:///" + filePath.Replace("\\", "/");
            if (!string.IsNullOrEmpty(anchor))
                url += "#" + anchor;
            HelpWebView.Source = new Uri(url);

            // ナビゲーション完了後にバージョン文字列を注入
            HelpWebView.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    var version = ViewModels.MainWindowViewModel.AppVersion;
                    var script = $@"
                        var el = document.getElementById('app-version');
                        if (el) {{ el.textContent = 'ver {version}'; }}
                    ";
                    await HelpWebView.ExecuteScriptAsync(script);
                }
            };

        }
    }
}
