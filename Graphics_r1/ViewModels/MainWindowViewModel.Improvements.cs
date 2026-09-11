using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Serilog;
namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.Improvements.cs
    ///
    /// 責任範囲:
    /// - Undoスナップショット重複保存防止（ハッシュ比較）
    /// - IDisposable実装（リソース解放）
    ///
    /// コマンドの CanExecute 一括更新と長時間操作のキャンセルは、どこからも呼ばれて
    /// いなかったので 2026-09-11 に削除した。
    /// </summary>
    public partial class MainWindowViewModel : IDisposable
    {
        // --- Fields for improvements ---
        private bool _disposed = false;

        // Undo スナップショット重複保存防止のためのハッシュ
        private string? _lastUndoSnapshotHash;

        // 軽量なシリアライズオプション（Undo 専用: 参照保存しない）
        private static readonly JsonSerializerOptions _undoHashJsonOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = null,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        /// <summary>
        /// Undoスナップショットを安全に保存（最適化版）
        /// ハッシュ比較により重複保存を防止し、パフォーマンスを向上
        /// </summary>
        public void TrySaveUndoSnapshotSafelyOptimized([System.Runtime.CompilerServices.CallerMemberName] string? description = null)
        {
            try
            {
                var snapshot = CurrentInputModel?.DeepCopy();
                if (snapshot == null)
                {
                    return;
                }

                // シリアライズしたスナップショットからSHA256ハッシュを生成
                string json = JsonSerializer.Serialize(snapshot, _undoHashJsonOptions);
                string hash;
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var hashed = sha.ComputeHash(bytes);
                    hash = Convert.ToBase64String(hashed);
                }

                if (hash == _lastUndoSnapshotHash)
                {
                    // 以前に取得したハッシュと同一なら重複保存をスキップ
                    return;
                }

                // スナップショットを保存し、最後のハッシュを更新
                _undoManager.SaveState(snapshot, FormatHistoryDescription(description));
                _lastUndoSnapshotHash = hash;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Undo] スナップショット保存失敗");
            }
        }


        /// <summary>
        /// リソースを解放（IDisposable実装）
        /// タイマーとキャンセルトークンをクリーンアップ
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _generateSoilPilesDebounceTimer?.Stop();
                _generateSoilPilesDebounceTimer = null;
            }
            catch (Exception ex) { Log.Warning(ex, "[Dispose] _generateSoilPilesDebounceTimer.Stop"); }

            try
            {
                _updateWindowDebounceTimer?.Stop();
                _updateWindowDebounceTimer = null;
            }
            catch (Exception ex) { Log.Warning(ex, "[Dispose] _updateWindowDebounceTimer.Stop"); }

            // イベントハンドラの解除
            try
            {
                if (CurrentInputModel != null)
                {
                    // PropertyChanged ハンドラは lambda で登録されているため個別解除は不可。
                    // CurrentInputModel 自体が GC されれば問題ないが、念のため参照をクリア。
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Dispose] CurrentInputModel cleanup"); }

            // Clear delegates to avoid leaks
            UpdateWindowAction = null;
            UpdateCanvas3DAction = null;
            ZoomFitAction = null;
            AnimateViewAnglesAction = null;

            _lastUndoSnapshotHash = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~MainWindowViewModel()
        {
            Dispose();
        }
    }
}