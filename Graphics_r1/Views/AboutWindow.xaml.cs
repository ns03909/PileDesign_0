using PileDesign.Common.Logging;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// バージョン情報。
    ///
    /// 「今どの版を使っているか」と「何が変わったか」を利用者が自分で確かめられるようにする。
    /// バージョンはウィンドウタイトル・起動画面・ヘルプの右上にも出ているが、
    /// 「プログラム更新履歴概要」への入口はここだけ。
    ///
    /// ログと自動保存の場所も併せて出す。不具合の連絡時にほぼ必ず要るため。
    /// </summary>
    public partial class AboutWindow : Window
    {
        /// <summary>ヘルプの「プログラム更新履歴概要」章のアンカー。実在は VersionVisibilityTests が検査する。</summary>
        public const string ReleaseNotesAnchor = "h-プログラム更新履歴概要";

        public AboutWindow()
        {
            InitializeComponent();

            VersionText.Text = $"バージョン {MainWindowViewModel.AppVersion}";
            BuildDateText.Text = DescribeBuildDate();
            RuntimeText.Text = $".NET {Environment.Version}　/　{(Environment.Is64BitProcess ? "64 ビット" : "32 ビット")}";
            LogPathText.Text = AppLog.LogDirectory;
            AutoSavePathText.Text = AutoSaveFolder();

            // 試用段階の注記は beta のときだけ出す (正式版で残っていると誤解を招く)
            BetaNote.Visibility =
                MainWindowViewModel.AppVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        /// <summary>
        /// ビルド日。実行ファイルの更新日時を使う。
        /// 決定的ビルドではアセンブリに日付が埋まらないため、これが最も確実な手掛かりになる。
        /// </summary>
        private static string DescribeBuildDate()
        {
            try
            {
                string path = Assembly.GetEntryAssembly()?.Location ?? "";
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PileDesign.exe");

                return File.Exists(path)
                    ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd")
                    : "(不明)";
            }
            catch
            {
                return "(不明)";
            }
        }

        private static string AutoSaveFolder() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PileDesign", "AutoSave");

        private void ButtonReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.OpenHelpWindowAt(ReleaseNotesAnchor, "プログラム更新履歴概要");
        }

        private void ButtonOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = AppLog.LogDirectory;
                Directory.CreateDirectory(dir);   // 初回起動直後などで未作成でも開けるように
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[About] ログフォルダを開けませんでした");
                Services.MessageService.Show(this,
                    "ログの保存先を開けませんでした。\n" +
                    "画面に表示されているパスをコピーして、エクスプローラーのアドレスバーに貼り付けてください。",
                    "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }
    }
}
