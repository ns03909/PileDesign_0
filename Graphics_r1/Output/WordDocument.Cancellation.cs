using System;
using System.Diagnostics;

namespace PileDesign.Output
{
    internal partial class WordDocument
    {
        // 作成中の計算書の中断の受け口。図表を作る処理には静的なものもあるので、スレッドごとの静的な値で持つ
        // (計算書は 1 つのスレッドで 1 冊ずつ作る)。CreateWordDocument が作成のあいだだけ置く。
        [ThreadStatic] private static ReportRunControl? t_run;
        [ThreadStatic] private static Stopwatch? t_sincePump;
        [ThreadStatic] private static string? t_step;
        [ThreadStatic] private static int t_stepNumber;
        [ThreadStatic] private static int t_items;

        /// <summary>作成の段階の数 (進み具合の分母)。<see cref="BuildWordDocument"/> の <see cref="Checkpoint(string, int)"/> の数と合わせる。</summary>
        internal const int ReportStepCount = 5;

        private static void BeginRun(ReportRunControl? run)
        {
            t_run = run;
            t_sincePump = null;
            t_step = null;
            t_stepNumber = 0;
            t_items = 0;
        }

        private static void EndRun() => t_run = null;

        /// <summary>段階の区切り。進み具合を段階の名前で知らせ、止められていれば抜ける。</summary>
        internal static void Checkpoint(string step, int stepNumber)
        {
            t_step = step;
            t_stepNumber = stepNumber;
            Checkpoint(force: true);
        }

        /// <summary>
        /// 見出し・図表の区切り。止められていれば <see cref="OperationCanceledException"/> で抜ける。
        /// 画面のメッセージは間隔をあけて回す。計算書を作っていないとき (受け口が無いとき) は何もしない。
        /// </summary>
        internal static void Checkpoint() => Checkpoint(force: false);

        private static void Checkpoint(bool force)
        {
            var run = t_run;
            if (run == null) return;
            if (!force) t_items++;
            if (force || t_sincePump == null || t_sincePump.Elapsed >= run.PumpInterval)
            {
                run.Report?.Invoke(new ReportProgress(t_step ?? "", t_stepNumber, ReportStepCount, t_items));
                run.PumpMessages?.Invoke();
                t_sincePump = Stopwatch.StartNew();
            }
            run.Token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 止められたことによる例外か。図表 1 つの失敗は省いて続ける (<see cref="NoteOmitted"/>) が、
        /// 止められたときは続けずに抜ける。
        /// </summary>
        private static bool IsCancellation(Exception ex)
            => ex is OperationCanceledException && t_run?.Token.IsCancellationRequested == true;
    }
}
