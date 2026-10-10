using System;

using Serilog;
namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.Improvements.cs
    ///
    /// 責任範囲:
    /// - IDisposable実装（リソース解放）
    ///
    /// コマンドの CanExecute 一括更新と長時間操作のキャンセルは、どこからも呼ばれて
    /// いなかったので 2026-09-11 に削除した。ハッシュで重複を省く Undo の積み方も
    /// 呼び出し元が無かったので 2026-10-10 に削除した。
    /// </summary>
    public partial class MainWindowViewModel : IDisposable
    {
        // --- Fields for improvements ---
        private bool _disposed = false;


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
            CenterOnPilesAction = null;
            OpenResultDashboardAction = null;
            AnimateViewAnglesAction = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~MainWindowViewModel()
        {
            Dispose();
        }
    }
}