using AvalonDock;
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
    /// - AvalonDockレイアウトの保存/復元
    /// - DataGrid列設定（幅、並び順）の保存/復元
    /// - ウィンドウサイズ・位置の保存/復元
    /// </summary>
    public class LayoutService
    {
        private readonly string _layoutFilePath;
        private readonly string _dataGridSettingsFilePath;

        public LayoutService()
        {
            // レイアウトファイルのパスを設定
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "PileDesign");
            Directory.CreateDirectory(appFolder);

            _layoutFilePath = Path.Combine(appFolder, "layout.xml");
            _dataGridSettingsFilePath = Path.Combine(appFolder, "datagrid_settings.json");
        }

        /// <summary>
        /// AvalonDockレイアウトを保存
        /// </summary>
        /// <param name="dockingManager">DockingManager</param>
        public void SaveDockLayout(DockingManager dockingManager)
        {
            if (dockingManager == null)
                return;

            try
            {
                // AvalonDockのレイアウトをXMLで保存
                var layoutSerializer = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockingManager);
                using var writer = new StreamWriter(_layoutFilePath);
                layoutSerializer.Serialize(writer);
            }
            catch
            {
                // 保存失敗は無視
            }
        }

        /// <summary>
        /// AvalonDockレイアウトを復元
        /// </summary>
        /// <param name="dockingManager">DockingManager</param>
        /// <returns>復元に成功した場合true</returns>
        public bool RestoreDockLayout(DockingManager dockingManager)
        {
            if (dockingManager == null || !File.Exists(_layoutFilePath))
                return false;

            try
            {
                var layoutSerializer = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockingManager);
                using var reader = new StreamReader(_layoutFilePath);
                layoutSerializer.Deserialize(reader);
                return true;
            }
            catch
            {
                return false;
            }
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
                        Header = column.Header?.ToString() ?? "",
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
                File.WriteAllText(_dataGridSettingsFilePath, json);
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

                var columnSettings = allSettings[dataGridName];

                // 列数が一致しない場合は復元しない
                if (columnSettings.Count != dataGrid.Columns.Count)
                    return false;

                // 列設定を適用
                foreach (var setting in columnSettings)
                {
                    if (setting.Index >= 0 && setting.Index < dataGrid.Columns.Count)
                    {
                        var column = dataGrid.Columns[setting.Index];

                        // 幅を復元
                        if (setting.Width > 0)
                        {
                            column.Width = new DataGridLength(setting.Width);
                        }

                        // 表示順を復元
                        if (setting.DisplayIndex >= 0 && setting.DisplayIndex < dataGrid.Columns.Count)
                        {
                            column.DisplayIndex = setting.DisplayIndex;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
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
                if (File.Exists(_layoutFilePath))
                    File.Delete(_layoutFilePath);

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
