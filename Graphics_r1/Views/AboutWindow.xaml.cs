using PileDesign.Common.Logging;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Text.Json;
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
        /// <summary>
        /// 文献値との照合件数。
        /// Help/verification_results.json は VerificationTableTests (テスト) が文献値カタログから書き出す。
        /// 無ければ「—」(配布物に入れ忘れたときに落とさない)。
        /// </summary>
        private static string DescribeVerification()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Help", "verification_results.json");
                if (!File.Exists(path)) return "—";
                // 名前の文字列で JSON を辿らず型に読む (名前の打ち間違いをビルドで捕まえるため)
                var file = JsonSerializer.Deserialize<VerificationResultsFile>(
                    File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (file?.Summary is not { } summary) return "—";
                int count = summary.Count;
                int ok = summary.OkCount;
                int academic = summary.Academic?.Count ?? 0;
                int methods = summary.CertifiedMethods?.Count ?? 0;
                string scope = $"学会の指針・計算例 {academic} 項目、認定工法のカタログ {methods} 項目";
                return ok == count
                    ? $"{scope}と照合し、すべて許容内です (一覧はヘルプ「検証」)"
                    : $"{scope}と照合し、{count - ok} 項目が許容外です (一覧はヘルプ「検証」)";
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[About] 検証一覧の読込に失敗");
                return "—";
            }
        }

        /// <summary>Help/verification_results.json の形 (VerificationTableWriter.ToJson と対)。</summary>
        private sealed record VerificationResultsFile(VerificationSummary? Summary);
        private sealed record VerificationSummary(int Count, int OkCount, string[]? Sources,
            VerificationKindSummary? Academic, VerificationKindSummary? CertifiedMethods);
        private sealed record VerificationKindSummary(int Count, int OkCount, string[]? Sources);

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
            VerificationText.Text = DescribeVerification();

            // 試用段階の注記は beta のときだけ出す (正式版で残っていると誤解を招く)
            BetaNote.Visibility =
                MainWindowViewModel.AppVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        /// <summary>
        /// ビルド日。実行ファイルの更新日時を使う。
        /// 決定的ビルドではアセンブリに日付が埋まらないため、これが最も確実な手掛かりになる。
        ///
        /// 場所の取得に <c>Assembly.Location</c> は使えない。
        /// 単一ファイル発行では常に空文字を返すため、アナライザ (IL3000) が咎める。
        /// <c>AppContext.BaseDirectory</c> は単一ファイルでも展開先を指す。
        /// </summary>
        private static string DescribeBuildDate()
        {
            try
            {
                string dir = AppContext.BaseDirectory;
                foreach (string name in new[] { "PileDesign.exe", "PileDesign.dll" })
                {
                    string path = Path.Combine(dir, name);
                    if (File.Exists(path))
                        return File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
                }
                return "(不明)";
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
