using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PileDesign.Common.Logging
{
    /// <summary>
    /// 致命的例外発生時に、ユーザーが Anthropic / 開発者に共有しやすい
    /// 1 ファイル zip (クラッシュレポート) を生成する。
    ///
    /// 含まれるもの:
    ///   - report.txt: 例外メッセージ・スタックトレース・OS / .NET / アプリ
    ///                 バージョン・例外発生時刻・緊急 AutoSave のパス
    ///   - 当日と前日の Serilog ログ (PileDesign-yyyymmdd.log) のコピー
    ///
    /// 個人情報や入力データそのものは含めない (緊急 AutoSave は別フォルダに保存され、
    /// ユーザーが任意で添付できる)。
    /// </summary>
    public static class CrashReporter
    {
        /// <summary>クラッシュレポート保存先 (%LocalAppData%\PileDesign\CrashReports)。</summary>
        public static string ReportDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PileDesign", "CrashReports");

        /// <summary>
        /// クラッシュレポート zip を作成して返す。失敗時は null を返す (例外を投げない)。
        /// </summary>
        /// <param name="ex">致命的例外</param>
        /// <param name="source">例外ソース (例: "DispatcherUnhandledException")</param>
        /// <param name="emergencyAutoSavePath">緊急 AutoSave で保存したファイルのパス (任意)</param>
        public static string? TryCreateReport(Exception ex, string source, string? emergencyAutoSavePath)
        {
            try
            {
                Directory.CreateDirectory(ReportDirectory);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var zipPath = Path.Combine(ReportDirectory, $"crash_{timestamp}.zip");

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    AddTextEntry(zip, "report.txt", BuildReportText(ex, source, emergencyAutoSavePath));
                    CopyRecentLogs(zip);
                }

                Log.Information("Crash report created: {Path}", zipPath);
                CleanupOldReports();
                return zipPath;
            }
            catch (Exception inner)
            {
                try { Log.Error(inner, "CrashReporter.TryCreateReport failed"); } catch { }
                return null;
            }
        }

        /// <summary>
        /// 保存先フォルダをエクスプローラーで開く (zip をハイライト表示)。失敗は黙って続行。
        /// </summary>
        public static void TryOpenInExplorer(string zipPath)
        {
            try
            {
                if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{ReportDirectory}\"");
                    return;
                }
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "Failed to open crash report folder"); } catch { }
            }
        }

        // --- 内部 -------------------------------------------------------------

        private static string BuildReportText(Exception ex, string source, string? emergencyAutoSavePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PileDesign Crash Report");
            sb.AppendLine("================================================================");
            sb.AppendLine($"Timestamp     : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"Source        : {source}");
            sb.AppendLine($"App Version   : {GetAppVersion()}");
            sb.AppendLine($".NET Version  : {Environment.Version}");
            sb.AppendLine($"OS            : {Environment.OSVersion}");
            sb.AppendLine($"OS 64bit      : {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Process 64bit : {Environment.Is64BitProcess}");
            sb.AppendLine($"CPU Count     : {Environment.ProcessorCount}");
            sb.AppendLine($"WorkingSet MB : {Environment.WorkingSet / 1024 / 1024}");
            sb.AppendLine($"Culture       : {System.Globalization.CultureInfo.CurrentCulture.Name}");
            sb.AppendLine($"Emergency Save: {emergencyAutoSavePath ?? "(none)"}");
            sb.AppendLine();
            sb.AppendLine("Exception");
            sb.AppendLine("----------------------------------------------------------------");
            AppendException(sb, ex);
            return sb.ToString();
        }

        private static void AppendException(StringBuilder sb, Exception ex, int depth = 0)
        {
            string indent = new(' ', depth * 2);
            sb.AppendLine($"{indent}Type    : {ex.GetType().FullName}");
            sb.AppendLine($"{indent}Message : {ex.Message}");
            sb.AppendLine($"{indent}HResult : 0x{ex.HResult:X8}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine($"{indent}StackTrace:");
                foreach (var line in ex.StackTrace.Split('\n'))
                    sb.AppendLine($"{indent}  {line.TrimEnd('\r')}");
            }
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}-- Inner Exception --");
                AppendException(sb, ex.InnerException, depth + 1);
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                return Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                    ?? "(unknown)";
            }
            catch { return "(unknown)"; }
        }

        private static void AddTextEntry(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.Write(content);
        }

        private static void CopyRecentLogs(ZipArchive zip)
        {
            try
            {
                var logDir = AppLog.LogDirectory;
                if (!Directory.Exists(logDir)) return;

                // 当日 + 前日の Serilog 日次ファイル (最大 2 ファイル)。
                // 大量のログを過去含めて添付すると zip が肥大化するので意図的に絞る。
                var today = DateTime.Now.Date;
                var targets = new List<string>
                {
                    Path.Combine(logDir, $"PileDesign-{today:yyyyMMdd}.log"),
                    Path.Combine(logDir, $"PileDesign-{today.AddDays(-1):yyyyMMdd}.log"),
                };

                foreach (var path in targets.Where(File.Exists))
                {
                    var entryName = $"logs/{Path.GetFileName(path)}";
                    // shared:true でログが書き込み中でも読めるように FileShare.ReadWrite で開く
                    using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "CrashReporter: failed to copy logs"); } catch { }
            }
        }

        /// <summary>
        /// 30 日経過したクラッシュレポートを削除。
        /// </summary>
        private static void CleanupOldReports()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var f in Directory.GetFiles(ReportDirectory, "crash_*.zip"))
                {
                    var fi = new FileInfo(f);
                    if (fi.CreationTime < cutoff)
                        File.Delete(f);
                }
            }
            catch
            {
                // 失敗は次回に再試行
            }
        }
    }
}
