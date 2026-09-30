using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PileDesign.Common.Logging
{
    /// <summary>
    /// 問い合わせのときに渡す「共有用のログ」を作る。
    ///
    /// <para>ログには、解析の診断・版・モデルの規模 (杭・要素・ケースの数) のほかに、ファイルを開いた・保存した場所
    /// (フォルダ名に案件名・顧客名が入る)、Windows のユーザー名・PC 名が入る。調べるのに要るものは残し、
    /// 要らない個人の情報とフォルダの場所は伏せる。モデルの識別子 (<see cref="ModelIdentity"/>) は場所から作った
    /// 値なので、伏せても同じモデルの記録どうしを突き合わせられる。</para>
    /// </summary>
    internal static class LogSanitizer
    {
        internal const string UserProfileMark = "%USERPROFILE%";
        internal const string UserMark = "<ユーザー>";
        internal const string MachineMark = "<PC>";
        internal const string FolderMark = "…\\";

        // フォルダの部分 (ドライブ・UNC・%USERPROFILE% から始まり、「名前\」が続く)。ファイル名は残す。
        // 名前に空白は入りうるが、「:」「\」「"」などは入らないので、別の場所の途中まで食い込まない
        private static readonly Regex FolderPart = new(
            @"(?:[A-Za-z]:\\|\\\\|%USERPROFILE%\\)(?:[^\\\r\n""<>|:*?]*\\)*",
            RegexOptions.Compiled);

        /// <summary>
        /// ログの文から、ユーザーフォルダの場所・フォルダの場所・ユーザー名・PC 名を伏せる。
        /// 既定ではいまの PC の値を使う (試験では与える)。
        /// </summary>
        internal static string Sanitize(string text, string? userProfile = null, string? userName = null, string? machineName = null)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            userName ??= Environment.UserName;
            machineName ??= Environment.MachineName;

            string result = text;
            if (!string.IsNullOrEmpty(userProfile))
                result = Regex.Replace(result, Regex.Escape(userProfile.TrimEnd('\\')), UserProfileMark, RegexOptions.IgnoreCase);
            result = FolderPart.Replace(result, FolderMark);
            // 名前は 2 文字以上のときだけ (1 文字だと関係ない語まで伏せる)。語の途中は伏せない
            if (!string.IsNullOrEmpty(userName) && userName.Length >= 2)
                result = Regex.Replace(result, $@"(?<![\w]){Regex.Escape(userName)}(?![\w])", UserMark, RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(machineName) && machineName.Length >= 2)
                result = Regex.Replace(result, $@"(?<![\w]){Regex.Escape(machineName)}(?![\w])", MachineMark, RegexOptions.IgnoreCase);
            return result;
        }

        /// <summary>
        /// 共有用のログの本文。頭に版・OS・.NET・対象の日付を書き、直近 <paramref name="days"/> 日ぶんのログを伏せて続ける。
        /// </summary>
        internal static string BuildShareableLog(string appVersion, int days = 2, string? logDirectory = null)
        {
            logDirectory ??= AppLog.LogDirectory;
            var sb = new StringBuilder();
            sb.AppendLine("PileDesign 共有用ログ");
            sb.AppendLine($"作成: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"版: {appVersion}");
            sb.AppendLine($"OS: {Environment.OSVersion.VersionString} / .NET {Environment.Version}");
            sb.AppendLine("ユーザー名・PC 名・フォルダの場所は伏せています (ファイル名は残しています)。"
                          + "モデルは識別子 (場所から作った 8 桁) で見分けられます。");
            sb.AppendLine(new string('-', 60));

            var today = DateTime.Now.Date;
            var files = Enumerable.Range(0, Math.Max(1, days))
                .Select(d => Path.Combine(logDirectory, $"PileDesign-{today.AddDays(-d):yyyyMMdd}.log"))
                .Where(File.Exists)
                .Reverse()   // 古い順
                .ToList();
            if (files.Count == 0) sb.AppendLine("(対象の日のログがありません)");
            foreach (var path in files)
            {
                sb.AppendLine($"## {Path.GetFileName(path)}");
                sb.AppendLine(Sanitize(ReadShared(path)));
            }
            return sb.ToString();
        }

        /// <summary>書き込み中のログも読めるように開く (Serilog は shared で書いている)。</summary>
        private static string ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
