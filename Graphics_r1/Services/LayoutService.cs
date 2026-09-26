using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace PileDesign.Services
{
    /// <summary>
    /// ウィンドウレイアウト管理サービス
    ///
    /// 機能:
    /// - DataGrid列設定（幅、並び順）の保存/復元
    /// - ウィンドウサイズ・位置の保存/復元
    ///
    /// AvalonDock のドッキングレイアウトの保存/復元は持たない。
    /// 復元は呼び出し側でコメントアウトされていて到達不能な状態が続いており、
    /// 保存だけが誰も読まないファイルを書いていた。
    /// AvalonDock 5.0.0 で XmlLayoutSerializer が削除された機に整理した
    /// (必要になったら DockingManager の DtoMapper で作り直す)。
    /// </summary>
    public class LayoutService
    {
        private readonly string _dataGridSettingsFilePath;

        public LayoutService()
        {
            // レイアウトファイルのパスを設定
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "PileDesign");
            Directory.CreateDirectory(appFolder);

            _dataGridSettingsFilePath = Path.Combine(appFolder, "datagrid_settings.json");
        }


        /// <summary>
        /// DataGrid列設定を保存
        /// </summary>
        /// <param name="dataGridName">DataGrid識別名</param>
        /// <param name="dataGrid">DataGrid</param>
        public void SaveDataGridSettings(string dataGridName, DataGrid dataGrid)
        {
            if (string.IsNullOrEmpty(dataGridName) || dataGrid == null)
                return;

            try
            {
                // 全設定を読み込み
                var allSettings = LoadAllDataGridSettings();

                // 列設定を抽出
                var columnSettings = new List<DataGridColumnSetting>();
                for (int i = 0; i < dataGrid.Columns.Count; i++)
                {
                    var column = dataGrid.Columns[i];
                    columnSettings.Add(new DataGridColumnSetting
                    {
                        Index = i,
                        Header = PileDesign.Common.DataGridHeaderText.From(column),
                        Width = column.Width.Value,
                        DisplayIndex = column.DisplayIndex
                    });
                }

                // 設定を更新
                allSettings[dataGridName] = columnSettings;

                // 保存
                var json = JsonSerializer.Serialize(allSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                // 一時ファイルに書き切ってから差し替える (途中で終了しても前の設定を壊さない)
                FileOperationService.WriteAtomically(_dataGridSettingsFilePath, stream => stream.Write(System.Text.Encoding.UTF8.GetBytes(json)));
            }
            catch
            {
                // 保存失敗は無視
            }
        }

        /// <summary>
        /// DataGrid列設定を復元
        /// </summary>
        /// <param name="dataGridName">DataGrid識別名</param>
        /// <param name="dataGrid">DataGrid</param>
        /// <returns>復元に成功した場合true</returns>
        public bool RestoreDataGridSettings(string dataGridName, DataGrid dataGrid)
        {
            if (string.IsNullOrEmpty(dataGridName) || dataGrid == null)
                return false;

            try
            {
                var allSettings = LoadAllDataGridSettings();

                if (!allSettings.ContainsKey(dataGridName))
                    return false;

                return ApplyColumnSettings(allSettings[dataGridName], dataGrid);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 保存した列設定を表に当てる。<b>列番号と見出しの両方が一致する列</b>にだけ当てる。1 列でも当てたら true。
        ///
        /// 以前は列数が同じなら列番号だけで当てていた。更新で列の並びや中身が変わると (列数は同じまま)、
        /// 別の列に幅と表示順が当たった。一致しない列は既定のままにする。表示順は、全列が一致したときだけ当てる
        /// (一部の列だけ表示順を動かすと、ほかの列の並びが押し出されて崩れる)。
        /// </summary>
        internal static bool ApplyColumnSettings(IReadOnlyList<DataGridColumnSetting> settings, DataGrid dataGrid)
        {
            var matched = new List<(DataGridColumn Column, DataGridColumnSetting Setting)>();
            foreach (var setting in settings)
            {
                if (setting == null || setting.Index < 0 || setting.Index >= dataGrid.Columns.Count) continue;
                var column = dataGrid.Columns[setting.Index];
                if (!string.Equals(PileDesign.Common.DataGridHeaderText.From(column), setting.Header ?? "", StringComparison.Ordinal))
                    continue;
                matched.Add((column, setting));
            }
            if (matched.Count == 0) return false;

            foreach (var (column, setting) in matched)
                if (setting.Width > 0 && double.IsFinite(setting.Width))
                    column.Width = new DataGridLength(setting.Width);

            bool allMatched = matched.Count == dataGrid.Columns.Count && settings.Count == dataGrid.Columns.Count;
            if (allMatched)
            {
                // 目標の表示順の小さい列から当てる (当てるたびにほかの列が押し出されるため)
                foreach (var (column, setting) in matched.OrderBy(m => m.Setting.DisplayIndex))
                    if (setting.DisplayIndex >= 0 && setting.DisplayIndex < dataGrid.Columns.Count)
                        column.DisplayIndex = setting.DisplayIndex;
            }
            return true;
        }

        /// <summary>
        /// すべてのDataGrid設定を読み込み
        /// </summary>
        private Dictionary<string, List<DataGridColumnSetting>> LoadAllDataGridSettings()
        {
            try
            {
                if (File.Exists(_dataGridSettingsFilePath))
                {
                    var json = File.ReadAllText(_dataGridSettingsFilePath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, List<DataGridColumnSetting>>>(json);
                    return settings ?? new Dictionary<string, List<DataGridColumnSetting>>();
                }
            }
            catch
            {
                // 読み込み失敗
            }

            return new Dictionary<string, List<DataGridColumnSetting>>();
        }

        /// <summary>
        /// レイアウト設定をクリア
        /// </summary>
        public void ClearAllSettings()
        {
            try
            {
                if (File.Exists(_dataGridSettingsFilePath))
                    File.Delete(_dataGridSettingsFilePath);
            }
            catch
            {
                // 削除失敗は無視
            }
        }
    }

    /// <summary>
    /// DataGrid列設定
    /// </summary>
    public class DataGridColumnSetting
    {
        /// <summary>
        /// 列インデックス
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// ヘッダー名
        /// </summary>
        public string Header { get; set; } = "";

        /// <summary>
        /// 列幅
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 表示順
        /// </summary>
        public int DisplayIndex { get; set; }
    }
}
