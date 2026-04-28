using Serilog;
using Serilog.Core;
using System;
using System.IO;

namespace PileDesign.Common.Logging
{
    /// <summary>
    /// アプリケーション全体のロギング窓口。
    ///
    /// 起動時に <see cref="Initialize"/> を呼び、終了時に <see cref="Close"/> を呼ぶ。
    /// ログは %LocalAppData%\PileDesign\Logs\PileDesign-yyyymmdd.log にローリング出力され、
    /// 30 日経過したファイルは自動削除される。
    ///
    /// 例外は Error / Fatal で第一引数に渡すと StackTrace まで自動的に書く。
    /// </summary>
    public static class AppLog
    {
        private static Logger? _logger;

        /// <summary>
        /// ログサブシステムを初期化する。失敗しても黙って続行 (致命傷ではない)。
        /// </summary>
        public static void Initialize()
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PileDesign", "Logs");
                Directory.CreateDirectory(logDir);

                _logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        Path.Combine(logDir, "PileDesign-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        outputTemplate:
                            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        shared: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(2))
                    .WriteTo.Debug()
                    .CreateLogger();

                Log.Logger = _logger;

                Log.Information("AppLog initialized. Log directory: {LogDir}", logDir);
            }
            catch (Exception ex)
            {
                // ロギング初期化失敗は致命的ではない。Debug 出力のみで継続。
                System.Diagnostics.Debug.WriteLine(
                    $"[AppLog.Initialize] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// バッファに残ったログをディスクへフラッシュして閉じる。
        /// アプリ終了時 (OnExit や致命的エラー時) に呼ぶ。
        /// </summary>
        public static void Close()
        {
            try
            {
                Log.CloseAndFlush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AppLog.Close] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>ログ出力先ディレクトリ。診断ダイアログ等で表示する用。</summary>
        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PileDesign", "Logs");
    }
}
