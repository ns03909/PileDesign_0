using PileDesign.Common.Logging;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace PileDesign.Services
{
    /// <summary>
    /// 自動保存サービス
    ///
    /// 機能:
    /// - 定期的にプロジェクトデータをバックアップフォルダに保存
    /// - 古い自動保存ファイルの自動削除
    /// - クラッシュ時の復元用データ管理
    /// </summary>
    public class AutoSaveService
    {
        private readonly FileOperationService _fileOperationService;
        private readonly DispatcherTimer _autoSaveTimer;
        private string? _currentFilePath;
        private InputModel? _currentInputModel;
        private AnaModel? _currentModel;
        private IList<FEM.VerticalBeamCaseResult>? _verticalBeamCaseResults;

        /// <summary>
        /// 自動保存フォルダのパス（AppData/Local/PileDesign/AutoSave/）
        /// </summary>
        public string AutoSaveFolder { get; }

        /// <summary>
        /// 自動保存の間隔（分）
        /// </summary>
        public int AutoSaveIntervalMinutes { get; set; } = 3;

        /// <summary>
        /// 自動保存ファイルの保持期間（日）
        /// </summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>
        /// 自動保存が有効かどうか
        /// </summary>
        public bool IsEnabled => _autoSaveTimer.IsEnabled;

        /// <summary>
        /// 最後の自動保存時刻
        /// </summary>
        public DateTime? LastAutoSaveTime { get; private set; }

        /// <summary>
        /// 連続失敗回数。成功で 0 にリセット。
        /// UI 側はこの値を使って閾値超過時に通知エスカレーションできる。
        /// </summary>
        public int ConsecutiveFailures { get; private set; }

        /// <summary>
        /// 自動保存時のイベント
        /// </summary>
        public event EventHandler<AutoSaveEventArgs>? AutoSaveCompleted;

        public AutoSaveService(FileOperationService fileOperationService)
        {
            _fileOperationService = fileOperationService;

            // 自動保存フォルダのパスを設定
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AutoSaveFolder = Path.Combine(appDataPath, "PileDesign", "AutoSave");

            // フォルダが存在しなければ作成
            Directory.CreateDirectory(AutoSaveFolder);

            // タイマー設定
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
            };
            _autoSaveTimer.Tick += OnAutoSaveTimer;
        }

        /// <summary>
        /// 自動保存を開始
        /// </summary>
        /// <param name="currentFilePath">現在のファイルパス</param>
        /// <param name="inputModel">InputModel</param>
        /// <param name="anaModel">AnaModel</param>
        public void Start(string? currentFilePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null)
        {
            _currentFilePath = currentFilePath;
            _currentInputModel = inputModel;
            _currentModel = anaModel;
            _verticalBeamCaseResults = verticalBeamCaseResults;

            // 既存の古いファイルをクリーンアップ
            CleanupOldAutoSaveFiles();

            // タイマー間隔を更新
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes);

            if (!_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Start();
            }
        }

        /// <summary>
        /// 自動保存を停止
        /// </summary>
        public void Stop()
        {
            _autoSaveTimer.Stop();
            _currentFilePath = null;
            _currentInputModel = null;
            _currentModel = null;
            _verticalBeamCaseResults = null;
            ConsecutiveFailures = 0;
        }

        /// <summary>
        /// タイマーイベント
        /// </summary>
        private void OnAutoSaveTimer(object? sender, EventArgs e)
        {
            PerformAutoSave();
        }

        /// <summary>
        /// 自動保存を実行
        /// </summary>
        private void PerformAutoSave()
        {
            if (_currentInputModel == null)
                return;

            try
            {
                var path = SaveSnapshot(tag: "autosave");

                LastAutoSaveTime = DateTime.Now;
                ConsecutiveFailures = 0;

                AutoSaveCompleted?.Invoke(this, new AutoSaveEventArgs
                {
                    FilePath = path,
                    Success = true,
                    Timestamp = LastAutoSaveTime.Value,
                    ConsecutiveFailures = 0
                });
            }
            catch (Exception ex)
            {
                ConsecutiveFailures++;
                Log.Warning(ex, "AutoSave failed (consecutive: {Count})", ConsecutiveFailures);

                AutoSaveCompleted?.Invoke(this, new AutoSaveEventArgs
                {
                    FilePath = null,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.Now,
                    ConsecutiveFailures = ConsecutiveFailures
                });
            }
        }

        /// <summary>
        /// 致命的例外発生時に呼び出される緊急保存。
        /// 通常の AutoSave と異なり、イベントを発火せず、成功時はファイルパスを返す。
        /// 失敗時 (またはセッション開始前で _currentInputModel が無いとき) は null を返す。
        /// 例外を投げない (呼び出し側がさらに例外処理する手間を避けるため)。
        /// </summary>
        public string? TryEmergencyAutoSave()
        {
            if (_currentInputModel == null) return null;
            try
            {
                var path = SaveSnapshot(tag: "emergency");
                Log.Information("Emergency AutoSave succeeded: {Path}", path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Emergency AutoSave failed");
                return null;
            }
        }

        /// <summary>
        /// 共通スナップショット保存ロジック。例外はそのまま伝播する。
        /// tag は "autosave" または "emergency" を渡す (ファイル名に埋め込む)。
        /// </summary>
        private string SaveSnapshot(string tag)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var originalFileName = !string.IsNullOrEmpty(_currentFilePath)
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : "Untitled";

            var fileName = $"{originalFileName}_{tag}_{timestamp}.json";
            var filePath = Path.Combine(AutoSaveFolder, fileName);

            _fileOperationService.SaveProjectData(filePath, _currentInputModel!, _currentModel, _verticalBeamCaseResults);
            return filePath;
        }

        /// <summary>
        /// 古い自動保存ファイルを削除
        /// </summary>
        private void CleanupOldAutoSaveFiles()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-RetentionDays);
                // 通常の autosave と緊急保存 (emergency) の両方をクリーンアップ対象にする
                var autoSaveFiles = Directory.GetFiles(AutoSaveFolder, "*_autosave_*.json")
                    .Concat(Directory.GetFiles(AutoSaveFolder, "*_emergency_*.json"));

                foreach (var file in autoSaveFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                // クリーンアップ失敗は次回に再試行
                Log.Warning(ex, "[AutoSave] クリーンアップ失敗");
            }
        }

        /// <summary>
        /// 最新の自動保存ファイルを取得
        /// </summary>
        /// <returns>最新の自動保存ファイルパス（存在しない場合はnull）</returns>
        public string? GetLatestAutoSaveFile()
        {
            try
            {
                var autoSaveFiles = Directory.GetFiles(AutoSaveFolder, "*_autosave_*.json")
                    .Concat(Directory.GetFiles(AutoSaveFolder, "*_emergency_*.json"))
                    .Where(f => !f.Contains("_dismissed_"))
                    .ToArray();
                if (autoSaveFiles.Length == 0)
                    return null;

                // 最新のファイルを返す
                return autoSaveFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault()
                    ?.FullName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 指定されたファイルに関連する自動保存ファイルを取得
        /// </summary>
        /// <param name="filePath">元のファイルパス</param>
        /// <returns>関連する自動保存ファイルのリスト</returns>
        public string[] GetAutoSaveFilesForProject(string filePath)
        {
            try
            {
                var originalFileName = Path.GetFileNameWithoutExtension(filePath);
                var autoSaveFiles = Directory
                    .GetFiles(AutoSaveFolder, $"{originalFileName}_autosave_*.json")
                    .Concat(Directory.GetFiles(AutoSaveFolder, $"{originalFileName}_emergency_*.json"));

                return [.. autoSaveFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Select(f => f.FullName)];
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// 自動保存イベント引数
    /// </summary>
    public class AutoSaveEventArgs : EventArgs
    {
        public string? FilePath { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 連続失敗回数 (成功時は 0)。閾値超過で UI 側がエスカレーション通知に使う。
        /// </summary>
        public int ConsecutiveFailures { get; set; }
    }
}
