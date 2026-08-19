using Serilog;
using System;
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
    }

    /// <summary>
    /// <see cref="UserSettings"/> の読み書き。
    ///
    /// 起動時に一度読み込み、値が変わるたびに保存する。
    /// 読み書きの失敗はアプリの動作を止める理由にならないので、
    /// ログに残して既定値で続行する（MruService と同じ方針）。
    /// </summary>
    public class UserSettingsService
    {
        private readonly string _filePath;

        public UserSettings Settings { get; private set; } = new();

        public UserSettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "PileDesign");
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "user_settings.json");

            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded != null) Settings = loaded;
            }
            catch (Exception ex)
            {
                // 壊れた設定ファイルで起動不能にはしない。既定値で続行する
                Log.Warning(ex, "[UserSettingsService] 設定の読込に失敗しました: {Path}", _filePath);
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[UserSettingsService] 設定の保存に失敗しました: {Path}", _filePath);
            }
        }
    }
}
