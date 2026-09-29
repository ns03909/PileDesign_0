using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PileDesign.Common
{
    /// <summary>
    /// 操作ごとの所要時間と対象の件数を記録する (応答性の実測)。
    ///
    /// <para>重い処理の見当は、これまで場面ごとに別々の Stopwatch とログで付けていて (保存・計算書・解析など)、
    /// 描画・結果の表・選択・編集の確定は測っていなかった。遅いと感じたときに、どの操作がどれだけの件数で
    /// 何ミリ秒かかったかを同じ形でログに残し、終了時に操作ごとの合計・最大をまとめて書く。
    /// 実測に基づいて改善する先を絞るための道具で、動きは変えない。</para>
    ///
    /// <code>using var _ = PerfLog.Measure("描画", piles.Count, "本の杭");</code>
    /// </summary>
    internal static class PerfLog
    {
        /// <summary>これ以上かかった操作は、1 回ずつ Information で残す (それ未満は Debug)。</summary>
        internal static TimeSpan SlowThreshold { get; set; } = TimeSpan.FromMilliseconds(200);

        private sealed class Stat
        {
            public int Calls;
            public double TotalMs;
            public double MaxMs;
            public int CountAtMax;
            public string Unit = "";
            public long MaxManagedBytes;
            public string ConditionsAtMax = "";
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<string, Stat> _stats = new(StringComparer.Ordinal);

        /// <summary>測り始める。戻り値を破棄 (using) したときに記録する。</summary>
        /// <param name="conditions">
        /// モデルの規模と処理の条件 (<see cref="Conditions"/>)。件数 1 つでは遅さの原因を見分けられない
        /// (杭の本数が同じでも要素・ケースの数や出力の形式で所要時間が変わる) ので、記録に添える。
        /// </param>
        public static Scope Measure(string operation, int count = -1, string unit = "件", string? conditions = null)
            => new(operation, count, unit, conditions, Stopwatch.GetTimestamp());

        /// <summary>
        /// モデルの規模と処理の条件を 1 行にする (杭・要素・荷重ケースの数・出力の形式・並列度。与えないものは省く)。
        /// </summary>
        public static string Conditions(int? piles = null, int? elements = null, int? cases = null, string? format = null, int? parallelism = null)
        {
            var parts = new List<string>();
            if (piles is int p) parts.Add($"杭 {p} 本");
            if (elements is int e) parts.Add($"要素 {e}");
            if (cases is int c) parts.Add($"荷重ケース {c}");
            if (!string.IsNullOrEmpty(format)) parts.Add($"出力 {format}");
            if (parallelism is int m) parts.Add($"並列 {m}");
            return string.Join("・", parts);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string _operation;
            private readonly int _count;
            private readonly string _unit;
            private readonly string? _conditions;
            private readonly long _start;

            internal Scope(string operation, int count, string unit, string? conditions, long start)
            {
                _operation = operation; _count = count; _unit = unit; _conditions = conditions; _start = start;
            }

            public void Dispose()
            {
                if (_operation == null) return;   // default の Scope
                Record(_operation, Stopwatch.GetElapsedTime(_start), _count, _unit, _conditions);
            }
        }

        /// <summary>1 回ぶんを記録する (試験は直接呼ぶ)。</summary>
        internal static void Record(string operation, TimeSpan elapsed, int count, string unit, string? conditions = null)
        {
            double ms = elapsed.TotalMilliseconds;
            // 操作を終えた時点の管理ヒープの使用量 (回収は促さない)。規模とメモリの関係の見当に使う
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            lock (_lock)
            {
                if (!_stats.TryGetValue(operation, out var stat)) _stats[operation] = stat = new Stat { Unit = unit };
                stat.Calls++;
                stat.TotalMs += ms;
                if (ms > stat.MaxMs) { stat.MaxMs = ms; stat.CountAtMax = count; stat.ConditionsAtMax = conditions ?? ""; }
                if (managed > stat.MaxManagedBytes) stat.MaxManagedBytes = managed;
            }
            string size = count >= 0 ? $" ({count} {unit})" : "";
            if (!string.IsNullOrEmpty(conditions)) size += $" [{conditions}]";
            if (elapsed >= SlowThreshold)
                Log.Information("[性能] {Operation}: {Elapsed:N0} ms{Size}・メモリ {Memory:N0} MB", operation, ms, size, managed / 1048576.0);
            else
                Log.Debug("[性能] {Operation}: {Elapsed:N1} ms{Size}", operation, ms, size);
        }

        /// <summary>操作ごとの集計 (合計の多い順)。</summary>
        internal static IReadOnlyList<(string Operation, int Calls, double TotalMs, double MaxMs, int CountAtMax, string Unit, long MaxManagedBytes, string ConditionsAtMax)> Summary()
        {
            lock (_lock)
            {
                return [.. _stats.Select(kv => (kv.Key, kv.Value.Calls, kv.Value.TotalMs, kv.Value.MaxMs, kv.Value.CountAtMax, kv.Value.Unit, kv.Value.MaxManagedBytes, kv.Value.ConditionsAtMax))
                              .OrderByDescending(s => s.TotalMs)];
            }
        }

        /// <summary>集計を消す (試験用)。</summary>
        internal static void Reset() { lock (_lock) _stats.Clear(); }

        /// <summary>集計をログに書く (終了時)。どの操作に時間を使ったかを、次に直す先の見当に使う。</summary>
        public static void WriteSummary()
        {
            var summary = Summary();
            if (summary.Count == 0) return;
            foreach (var s in summary.Take(15))
            {
                string size = s.CountAtMax >= 0 ? $" (最大のときは {s.CountAtMax} {s.Unit}" : "";
                if (!string.IsNullOrEmpty(s.ConditionsAtMax)) size += (size.Length > 0 ? "・" : " (最大のときは ") + s.ConditionsAtMax;
                if (size.Length > 0) size += ")";
                Log.Information("[性能] 集計 {Operation}: {Calls} 回・合計 {Total:N0} ms・平均 {Average:N1} ms・最大 {Max:N0} ms{Size}・メモリ最大 {Memory:N0} MB",
                    s.Operation, s.Calls, s.TotalMs, s.TotalMs / s.Calls, s.MaxMs, size, s.MaxManagedBytes / 1048576.0);
            }
        }
    }
}
