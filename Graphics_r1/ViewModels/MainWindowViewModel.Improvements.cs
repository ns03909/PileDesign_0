using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.Improvements.cs
    ///
    /// 責任範囲:
    /// - パフォーマンス最適化機能
    ///   - コマンドCanExecute一括更新（キャッシュ使用）
    ///   - Undoスナップショット重複保存防止（ハッシュ比較）
    ///   - 長時間操作のキャンセル機能
    /// - IDisposable実装（リソース解放）
    /// </summary>
    public partial class MainWindowViewModel : IDisposable
    {
        // --- Fields for improvements ---
        private bool _disposed = false;

        // キャッシュ: コマンド列挙に使う PropertyInfo のキャッシュ（反復コスト削減）
        private PropertyInfo[]? _commandPropsCache;

        // Undo スナップショット重複保存防止のためのハッシュ
        private string? _lastUndoSnapshotHash;

        // 長時間処理のキャンセル用
        private CancellationTokenSource? _longOpCts = null;

        // 軽量なシリアライズオプション（Undo 専用: 参照保存しない）
        private static readonly JsonSerializerOptions _undoHashJsonOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = null
        };

        /// <summary>
        /// 全コマンドのPropertyInfoキャッシュを初期化
        /// パフォーマンス向上のため、コマンド列挙を事前にキャッシュする
        /// </summary>
        public void InitializeCommandPropsCache()
        {
            if (_commandPropsCache != null) return;

            _commandPropsCache = this.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
                .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
                .ToArray();
        }

        /// <summary>
        /// 全コマンドのCanExecute状態を一括更新（最適化版）
        /// キャッシュを使用して反復処理のコストを削減
        /// </summary>
        public void RaiseAllCommandsCanExecuteOptimized()
        {
            try
            {
                // 一度だけプロパティリストを列挙
                if (_commandPropsCache == null)
                    InitializeCommandPropsCache();

                if (_commandPropsCache == null || _commandPropsCache.Length == 0)
                    return;

                foreach (var p in _commandPropsCache)
                {
                    try
                    {
                        if (!p.CanRead) continue;
                        var cmdObj = p.GetValue(this) as ICommand;
                        if (cmdObj == null) continue;

                        if (cmdObj is CommunityToolkit.Mvvm.Input.IRelayCommand toolkitCmd)
                        {
                            toolkitCmd.NotifyCanExecuteChanged();
                            continue;
                        }

                        var raiseMethod = cmdObj.GetType().GetMethod("RaiseCanExecuteChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (raiseMethod != null)
                        {
                            raiseMethod.Invoke(cmdObj, null);
                            continue;
                        }

                        var notifyMethod = cmdObj.GetType().GetMethod("NotifyCanExecuteChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        notifyMethod?.Invoke(cmdObj, null);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CanExecute] コマンド更新失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CanExecute] 全体エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// Undoスナップショットを安全に保存（最適化版）
        /// ハッシュ比較により重複保存を防止し、パフォーマンスを向上
        /// </summary>
        public void TrySaveUndoSnapshotSafelyOptimized()
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
                _undoManager.SaveState(snapshot);
                _lastUndoSnapshotHash = hash;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Undo] スナップショット保存失敗: {ex.GetType().Name}: {ex.Message}");
            }
        }


        /// <summary>
        /// 長時間操作用のキャンセルトークンを取得
        /// 既存の操作はキャンセルし、新しいトークンを返す
        /// </summary>
        /// <returns>新しいキャンセルトークン</returns>
        public CancellationToken GetLongOperationCancellationToken()
        {
            _longOpCts?.Cancel();
            _longOpCts?.Dispose();
            _longOpCts = new CancellationTokenSource();
            return _longOpCts.Token;
        }

        /// <summary>
        /// 実行中の長時間操作をキャンセル
        /// </summary>
        public void CancelLongRunningOperations()
        {
            try
            {
                _longOpCts?.Cancel();
            }
            catch (ObjectDisposedException) { }
        }



        /// <summary>
        /// 節点をキャンセル可能な非同期処理でコピー
        /// </summary>
        /// <param name="dX">X方向の移動距離</param>
        /// <param name="dY">Y方向の移動距離</param>
        /// <param name="repetitionNumber">繰り返し回数</param>
        /// <returns>非同期タスク</returns>
        public Task CopyNodesWithCancellationAsync(double dX, double dY, int repetitionNumber)
        {
            var ct = GetLongOperationCancellationToken();
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                CopyNodes(dX, dY, repetitionNumber);
            }, ct);
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
            catch { }

            try
            {
                _updateWindowDebounceTimer?.Stop();
                _updateWindowDebounceTimer = null;
            }
            catch { }

            try
            {
                // Cancel long running ops
                _longOpCts?.Cancel();
                _longOpCts?.Dispose();
                _longOpCts = null;
            }
            catch { }

            // イベントハンドラの解除
            try
            {
                if (CurrentInputModel != null)
                {
                    // PropertyChanged ハンドラは lambda で登録されているため個別解除は不可。
                    // CurrentInputModel 自体が GC されれば問題ないが、念のため参照をクリア。
                }
            }
            catch { }

            // Clear delegates to avoid leaks
            UpdateWindowAction = null;
            UpdateCanvas3DAction = null;
            ZoomFitAction = null;
            AnimateViewAnglesAction = null;

            _commandPropsCache = null;
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