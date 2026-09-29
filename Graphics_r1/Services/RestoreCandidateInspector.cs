using PileDesign.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 自動保存の復元候補を、開く前に調べ終える。
    ///
    /// <para>以前は利用者に「復元しますか？」と尋ねてから読み込み、壊れていればそこで初めて失敗を知らせていた。
    /// 失敗の理由 (JSON の形・新しい版・入力の欠け) は読込の途中の例外からしか分からず、何が戻るのか
    /// (入力だけか・結果もあるか・元のファイルへ保存できるか) も尋ねる時点では分からなかった。
    /// 読み込み (JSON の構造・版・参照の復元) と中身の点検を先に済ませ、読めない候補は尋ねずに理由を集め、
    /// 読める候補は戻る中身と注意を添えて尋ねる。読み込んだ中身はそのまま復元に使う (読み直さない)。</para>
    /// </summary>
    internal static class RestoreCandidateInspector
    {
        /// <summary>
        /// 調べた結果。<see cref="Problem"/> があれば復元できない (理由)。無ければ <see cref="Data"/> が読み込んだ中身。
        /// </summary>
        internal sealed record Inspection(
            ProjectData? Data, string? Problem, IReadOnlyList<string> Contents, IReadOnlyList<string> Cautions)
        {
            public bool CanRestore => Problem == null && Data != null;
        }

        internal static Inspection Inspect(FileOperationService files, string filePath)
        {
            ProjectData data;
            try
            {
                data = files.LoadProjectData(filePath);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
            {
                Serilog.Log.Warning(ex, "[復元] 自動保存ファイルを読めません: {File}", Path.GetFileName(filePath));
                return new Inspection(null, FirstLine(ex.Message), [], []);
            }
            return Describe(data);
        }

        /// <summary>読み込んだ中身を点検する (試験のため読み込みと分けてある)。</summary>
        internal static Inspection Describe(ProjectData data)
        {
            var input = data.InputModel;
            if (input == null)
                return new Inspection(null, "入力データが含まれていません。", [], []);

            var piles = input.PileLayoutItems?.Where(p => p != null).ToList() ?? [];
            int grounds = input.GroundsInput?.Count ?? 0;
            int bodies = input.PileBodies?.Count ?? 0;

            var contents = new List<string>
            {
                $"入力データ (杭 {piles.Count} 本・地盤 {grounds} 件・杭体 {bodies} 件)",
                data.AnaModel != null ? "水平解析の結果" : "解析結果は含まれていません (自動保存は入力だけを保存します)",
            };
            if (data.GroupSettlementResult?.HasResults == true)
                contents.Add("群杭沈下の結果");

            var cautions = new List<string>();
            // 参照の整合: 杭が指す地盤・杭体が無いと、開いたあとの分割・解析で止まる
            var missingGround = piles.Where(p => p.GroundNo < 1 || p.GroundNo > grounds).Select(p => p.PileNo).ToList();
            var missingBody = piles.Where(p => p.PileBodyNo < 1 || p.PileBodyNo > bodies).Select(p => p.PileNo).ToList();
            if (missingGround.Count > 0)
                cautions.Add($"杭No.{Short(missingGround)} が存在しない地盤を指しています。");
            if (missingBody.Count > 0)
                cautions.Add($"杭No.{Short(missingBody)} が存在しない杭体を指しています。");
            if (data.FormatVersion < 2)
                cautions.Add("古い形式のファイルです。開くときに今の形式へ変換します (杭の Z の意味が変わった版より前)。");
            if (string.IsNullOrEmpty(data.SourceFilePath))
                cautions.Add("元のファイルの記録が無いため、復元したあとの保存は「名前を付けて保存」になります。");
            else if (!File.Exists(data.SourceFilePath))
                cautions.Add($"元のファイルが見つからないため、復元したあとの保存は「名前を付けて保存」になります ({data.SourceFilePath})。");

            return new Inspection(data, null, contents, cautions);
        }

        /// <summary>読めなかった候補をまとめて知らせる文。</summary>
        internal static string DescribeSkipped(IReadOnlyList<(string File, string Problem)> skipped)
            => $"次の {skipped.Count} 件の自動保存ファイルは読めなかったため、復元の候補から外しました。\n\n"
               + string.Join("\n", skipped.Take(10).Select(s => $"・{Path.GetFileName(s.File)}: {s.Problem}"))
               + (skipped.Count > 10 ? $"\n…ほか {skipped.Count - 10} 件" : "")
               + "\n\nファイルは消していません (名前に _dismissed_ を付けて自動保存のフォルダに残しています)。";

        private static string Short(List<int> nos)
            => string.Join(", ", nos.Take(8)) + (nos.Count > 8 ? $" ほか {nos.Count - 8} 本" : "");

        private static string FirstLine(string message)
        {
            string line = (message ?? "").Split('\n')[0].Trim();
            return line.Length == 0 ? "読み込めませんでした。" : line;
        }
    }
}
