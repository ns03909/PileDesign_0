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
        public HelpWindow()
        {
            InitializeComponent();

            // WebView2 の初期化
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "Help.html");
            HelpWebView.Source = new Uri("file:///" + filePath.Replace("\\", "/"));

            // ナビゲーション完了後にバージョン文字列を注入
            HelpWebView.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    var version = ViewModels.MainWindowViewModel.AppVersion;
                    // id="app-version" の要素があれば書き換え、なければ既存テキストを置換
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
