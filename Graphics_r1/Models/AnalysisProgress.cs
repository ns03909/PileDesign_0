using System;

namespace PileDesign.Models
{
    /// <summary>
    /// 解析進捗情報
    /// </summary>
    public class AnalysisProgress
    {
        /// <summary>
        /// 進捗率（0-100）
        /// </summary>
        public double Percentage { get; set; }

        /// <summary>
        /// 現在のステップ説明
        /// </summary>
        public string CurrentStep { get; set; } = "";

        /// <summary>
        /// 現在のステップ番号
        /// </summary>
        public int CurrentStepNumber { get; set; }

        /// <summary>
        /// 総ステップ数
        /// </summary>
        public int TotalSteps { get; set; }

        /// <summary>
        /// 開始時刻
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 推定残り時間（秒）
        /// </summary>
        public double EstimatedRemainingSeconds
        {
            get
            {
                // 進捗率が0以下、または100以上の場合は0を返す
                if (Percentage <= 0 || Percentage >= 100)
                    return 0;

                var elapsed = (DateTime.Now - StartTime).TotalSeconds;

                // 経過時間が負の場合（StartTimeが未来）、または非常に小さい場合は0を返す
                if (elapsed < 0.1)
                    return 0;

                var estimatedTotal = elapsed / (Percentage / 100.0);
                var remaining = estimatedTotal - elapsed;

                // 異常に大きい値（24時間以上）をキャップする
                const double MAX_HOURS = 24;
                return Math.Max(0, Math.Min(remaining, MAX_HOURS * 3600));
            }
        }

        /// <summary>
        /// 推定残り時間（フォーマット済み文字列）
        /// </summary>
        public string EstimatedRemainingTimeText
        {
            get
            {
                var seconds = EstimatedRemainingSeconds;
                if (seconds < 1)
                    return "まもなく完了";
                else if (seconds < 60)
                    return $"残り約{seconds:F0}秒";
                else if (seconds < 3600)
                    return $"残り約{seconds / 60:F0}分";
                else
                    return $"残り約{seconds / 3600:F1}時間";
            }
        }

        /// <summary>
        /// 解析がキャンセルされたか
        /// </summary>
        public bool IsCancelled { get; set; }
    }
}
