using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PileDesign.Services
{
    /// <summary>
    /// アプリ全体のユーザー設定（プロジェクトファイルではなく、この PC のこのユーザーに紐づく設定）。
    ///
    /// 保存先は <c>%LocalAppData%\PileDesign\user_settings.json</c> で、
    /// <see cref="MruService"/> / <c>LayoutService</c> と同じ流儀。
    ///
    /// 設定を増やすときはプロパティを 1 つ足すだけでよい。
    /// 未知のプロパティを持つ古い JSON も読めるし、新しいプロパティは既定値で補われる。
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// 手動保存 (Ctrl+S / 名前を付けて保存) に解析結果を含めるか。
        ///
        /// 既定 true。ファイルを開き直したときに再計算なしで前回結果を確認できることを
        /// 標準の挙動とする。結果が不要でファイルを軽くしたい場合は OFF にできる。
        /// </summary>
        public bool IsSaveAnalysisResultsManual { get; set; } = true;

        /// <summary>
        /// 自動保存に解析結果を含めるか。
        ///
        /// 既定 false。自動保存は定期実行なので、ON にすると数十 MB の書込が繰り返し発生し
        /// 操作中に引っかかる。明示的に保存したときだけ結果を含めれば足りるため、
        /// 手動保存とは既定を変えている。
        /// </summary>
        public bool IsSaveAnalysisResultsAutoSave { get; set; }

        /// <summary>
        /// 利用者が保存した表示プリセット (リボン「表示」タブの「表示プリセット」)。
        /// 項目の名前はビューモデルのプロパティ名。いまは無い名前は当てるときに飛ばす。
        /// </summary>
        public List<DisplayPreset> DisplayPresets { get; set; } = new();
    }

    /// <summary>保存した表示プリセット 1 つ。</summary>
    public class DisplayPreset
    {
        public string Name { get; set; } = "";
        public Dictionary<string, bool> Toggles { get; set; } = new();

        // 欄の並び (2026-10-17)。null は「触らない」(これより前に保存した組は欄を変えない)
        public bool? DashboardOpen { get; set; }
        public bool? PropertiesVisible { get; set; }
        public bool? MinimapVisible { get; set; }
    }

    /// <summary>
    /// <see cref="UserSettings"/> の読み書き。
    ///
    /// 起動時に一度読み込み、値が変わるたびに保存する。
    /// 読み書きの失敗はアプリの動作を止める理由にならないので、
    /// ログに残して既定値で続行する（MruService と同じ方針）。
    /// 保存の失敗は呼び出し元へ返す (<see cref="Save"/>)。画面は「次回の起動には残らない」と知らせる。
    /// </summary>
    public class UserSettingsService
    {
        private readonly string _filePath;

        public UserSettings Settings { get; private set; } = new();

        public UserSettingsService()
        {
            var appFolder = PileDesign.Common.AppDataPaths.Root;
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "user_settings.json");

            Load();
        }

        /// <summary>設定ファイルの場所を指定して作る (テスト用。利用者の設定ファイルに触れないため)。</summary>
        internal UserSettingsService(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded == null) return;
                var fixes = Normalize(loaded);
                if (fixes.Count > 0)
                    Log.Warning("[UserSettingsService] 設定の中身を補正しました ({Path}): {Fixes}", _filePath, string.Join(" / ", fixes));
                Settings = loaded;
            }
            catch (Exception ex)
            {
                // 壊れた設定ファイルで起動不能にはしない。既定値で続行する
                Log.Warning(ex, "[UserSettingsService] 設定の読込に失敗しました: {Path}", _filePath);
            }
        }

        /// <summary>
        /// 読み込んだ設定の中身を、使える形に直す (2026-10-18 のレビュー)。直したことを返す。ファイルは書き直さない
        /// (次に保存したとき、直した中身で書く)。
        ///
        /// <para>JSON として正しくても、中身が使えないことがある。以前は <c>"DisplayPresets": null</c> を
        /// そのまま受け取り、画面の準備 (MainWindowViewModel のコンストラクター) が一覧を列挙して例外になった。
        /// 設定ファイルは残るので、起動するたびに落ちた。</para>
        /// </summary>
        internal static List<string> Normalize(UserSettings settings)
        {
            var fixes = new List<string>();
            if (settings.DisplayPresets == null)
            {
                settings.DisplayPresets = new();
                fixes.Add("表示プリセットの一覧が null");
                return fixes;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<DisplayPreset>();
            foreach (var p in settings.DisplayPresets)
            {
                if (p == null) { fixes.Add("表示プリセットの null の項目を除外"); continue; }
                if (string.IsNullOrWhiteSpace(p.Name)) { fixes.Add("名前の無い表示プリセットを除外"); continue; }
                if (!seen.Add(p.Name)) { fixes.Add($"同じ名前の表示プリセット「{p.Name}」の 2 つ目以降を除外"); continue; }
                if (p.Toggles == null) { p.Toggles = new(); fixes.Add($"表示プリセット「{p.Name}」の表示の項目が null"); }
                kept.Add(p);
            }
            if (kept.Count != settings.DisplayPresets.Count) settings.DisplayPresets = kept;
            return fixes;
        }

        /// <summary>保存する。書き込めなければ false (理由はログに残す。画面の状態はそのまま使える)。</summary>
        public bool Save()
        {
            try
            {
                // 同じフォルダの一時ファイルに書き切ってから差し替える (プロジェクトの保存と同じ)。
                // 以前は設定ファイルへ直接書いていたので、書き込みの途中で終了すると JSON が壊れ、
                // 次の起動で読めずに設定が既定値へ戻った。中身は従来と同じ (BOM なし UTF-8)。
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(Settings, new JsonSerializerOptions { WriteIndented = true });
                FileOperationService.WriteAtomically(_filePath, stream => stream.Write(json));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[UserSettingsService] 設定の保存に失敗しました: {Path}", _filePath);
                return false;
            }
        }

        /// <summary>
        /// 保存に失敗したときの知らせ。<paramref name="change"/> は「表示プリセット「A」を保存しました」のような、したことの文。
        /// <paramref name="nextLaunch"/> は次回の起動でどうなるか。
        /// </summary>
        internal static string DescribeSaveFailed(string change, string nextLaunch = "次回の起動には残りません") =>
            $"{change}が、設定ファイルに書き込めませんでした (理由はログに記録しています)。\n\n"
            + $"このまま使えますが、{nextLaunch}。";
    }
}
