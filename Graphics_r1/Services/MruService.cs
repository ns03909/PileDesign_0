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
        /// <param name="maxLength">最大文字数（パスの省略用）。1 以上。返す文字列は必ずこの長さ以内</param>
        /// <returns>表示用ファイル名</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> が 1 未満</exception>
        public static string GetDisplayName(string filePath, int maxLength = 60)
        {
            // 0 文字以下に収まる表示名は無い。空文字を返すと「ファイルが無い」と区別できないので拒否する
            if (maxLength < 1)
                throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "表示名の最大文字数は 1 以上にしてください。");

            if (string.IsNullOrEmpty(filePath))
                return "";

            if (filePath.Length <= maxLength)
                return filePath;

            // パスが長い場合は中央を省略する。長さは必ず maxLength 以内に収める。
            //   C:\...\file.pdj        ドライブとファイル名が入るとき
            //   ...\file.pdj           ファイル名は入るがドライブまでは入らないとき
            //   ...ong_file_name.pdj   ファイル名だけでも入らないとき (末尾を残す)
            //
            // 以前は「ファイル名 + 10 文字が入らない」ときに末尾 (maxLength − 3) 文字を切り出していたが、
            // ファイル名がそれより短い (既定の 60 で 51〜56 文字) と切り出し位置が負になり、
            // 最近使ったファイルの一覧を作るところで例外になった。
            const string Ellipsis = "...";
            var fileName = Path.GetFileName(filePath);

            // 「...」を付ける余地が無い幅 (3 文字以下) では、ファイル名の末尾をその幅だけ返す。
            // 以前は幅が 3 未満でも「...」(3 文字) を返し、最大文字数を超えていた
            if (maxLength <= Ellipsis.Length)
                return fileName.Length <= maxLength ? fileName : fileName[^maxLength..];

            var tail = Path.DirectorySeparatorChar + fileName;
            var drive = Path.GetPathRoot(filePath) ?? "";

            if (drive.Length + Ellipsis.Length + tail.Length <= maxLength)
                return drive + Ellipsis + tail;
            if (Ellipsis.Length + tail.Length <= maxLength)
                return Ellipsis + tail;

            int keep = Math.Max(0, maxLength - Ellipsis.Length);
            return Ellipsis + fileName[^Math.Min(keep, fileName.Length)..];
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
