using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PileDesign.Services
{
    /// <summary>
    /// MRU (Most Recently Used) ファイルリスト管理サービス
    ///
    /// 機能:
    /// - 最近使用したファイルのリストを保持
    /// - リストの保存・読み込み（JSON形式）
    /// - ファイルの追加・削除
    /// - 存在しないファイルの自動削除
    /// </summary>
    public class MruService
    {
        private readonly List<MruItem> _mruItems = new();
        private readonly string _mruFilePath;

        /// <summary>
        /// MRUリストの最大件数
        /// </summary>
        public int MaxItems { get; set; } = 10;

        /// <summary>
        /// MRUアイテムのリスト（読み取り専用）
        /// </summary>
        public IReadOnlyList<MruItem> Items => _mruItems.AsReadOnly();

        /// <summary>
        /// MRUリスト変更時のイベント
        /// </summary>
        public event EventHandler? MruListChanged;

        public MruService()
        {
            // MRUファイルのパスを設定
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "PileDesign");
            Directory.CreateDirectory(appFolder);
            _mruFilePath = Path.Combine(appFolder, "mru.json");

            // 起動時に読み込み
            Load();
        }

        /// <summary>
        /// ファイルをMRUリストに追加
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        public void AddFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            // 絶対パスに変換
            filePath = Path.GetFullPath(filePath);

            // 既存のエントリを削除（重複を避ける）
            _mruItems.RemoveAll(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            // リストの先頭に追加
            _mruItems.Insert(0, new MruItem
            {
                FilePath = filePath,
                LastAccessTime = DateTime.Now
            });

            // 最大件数を超えたら古いものを削除
            while (_mruItems.Count > MaxItems)
            {
                _mruItems.RemoveAt(_mruItems.Count - 1);
            }

            Save();
            MruListChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// ファイルをMRUリストから削除
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        public void RemoveFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            filePath = Path.GetFullPath(filePath);
            var removed = _mruItems.RemoveAll(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                Save();
                MruListChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// MRUリストをクリア
        /// </summary>
        public void Clear()
        {
            _mruItems.Clear();
            Save();
            MruListChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 存在しないファイルをリストから削除
        /// </summary>
        public void RemoveNonExistentFiles()
        {
            var removed = _mruItems.RemoveAll(item => !File.Exists(item.FilePath));

            if (removed > 0)
            {
                Save();
                MruListChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// MRUリストを保存
        /// </summary>
        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_mruItems, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_mruFilePath, json);
            }
            catch
            {
                // 保存失敗は無視
            }
        }

        /// <summary>
        /// MRUリストを読み込み
        /// </summary>
        private void Load()
        {
            try
            {
                if (File.Exists(_mruFilePath))
                {
                    var json = File.ReadAllText(_mruFilePath);
                    var items = JsonSerializer.Deserialize<List<MruItem>>(json);

                    if (items != null)
                    {
                        _mruItems.Clear();
                        _mruItems.AddRange(items);

                        // 存在しないファイルを削除
                        RemoveNonExistentFiles();
                    }
                }
            }
            catch
            {
                // 読み込み失敗は無視（空のリストで開始）
            }
        }

        /// <summary>
        /// 表示用のファイル名を取得
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="maxLength">最大文字数（パスの省略用）</param>
        /// <returns>表示用ファイル名</returns>
        public static string GetDisplayName(string filePath, int maxLength = 60)
        {
            if (string.IsNullOrEmpty(filePath))
                return "";

            if (filePath.Length <= maxLength)
                return filePath;

            // パスが長い場合は中央を省略
            // 例: C:\...\folder\file.json
            var fileName = Path.GetFileName(filePath);
            var directory = Path.GetDirectoryName(filePath) ?? "";

            if (fileName.Length + 10 > maxLength)
            {
                // ファイル名自体が長い場合
                return "..." + fileName.Substring(fileName.Length - maxLength + 3);
            }

            var availableLength = maxLength - fileName.Length - 6; // "..." + "\" を考慮
            if (availableLength > 0 && directory.Length > availableLength)
            {
                var drive = Path.GetPathRoot(directory) ?? "";
                return drive + "..." + Path.DirectorySeparatorChar + fileName;
            }

            return filePath;
        }
    }

    /// <summary>
    /// MRUアイテム
    /// </summary>
    public class MruItem
    {
        /// <summary>
        /// ファイルパス
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// 最終アクセス日時
        /// </summary>
        public DateTime LastAccessTime { get; set; }

        /// <summary>
        /// 表示用ファイル名
        /// </summary>
        public string DisplayName => MruService.GetDisplayName(FilePath);

        /// <summary>
        /// ファイル名のみ
        /// </summary>
        public string FileName => Path.GetFileName(FilePath);
    }
}
