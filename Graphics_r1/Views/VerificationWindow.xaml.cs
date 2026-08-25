using Serilog;
using System;
using System.IO;
using System.Windows;

namespace PileDesign.Views
{
    public partial class VerificationWindow : Window
    {
        public VerificationWindow()
        {
            InitializeComponent();

            // WebView2 Runtime インストール確認 (Evergreen Runtime が無いとクラッシュする)
            if (!Services.WebView2RuntimeChecker.IsRuntimeInstalled())
            {
                Services.WebView2RuntimeChecker.ShowMissingRuntimeDialog();
                return;
            }

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "verification.html");
            if (!File.Exists(filePath))
            {
                Log.Warning("[VerificationWindow] verification.html が見つかりません: {Path}", filePath);
                Services.MessageService.Show($"検証ファイルが見つかりません。\n{filePath}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            VerificationWebView.Source = new Uri("file:///" + filePath.Replace("\\", "/"));
        }
    }
}
