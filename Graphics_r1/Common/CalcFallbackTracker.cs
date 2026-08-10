using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Serilog;

namespace PileDesign.Common
{
    /// <summary>
    /// 断面計算などの「計算失敗時に既定値（耐力 0・線形 M-φ 等）で代替した」フォールバックを
    /// 集約記録する静的トラッカー。
    ///
    /// 背景: 断面積分・終局ソルバは例外時に (0,0) 等を返して処理を継続する設計だが、
    /// 従来は大半が無音（または Serilog のみ）で、耐力ゼロが検定・グラフへ静かに流れていた。
    /// 本トラッカーは
    ///   (1) Serilog への記録（発生源ごとに初回＋1000 回ごと。ループ内呼び出しのログ洪水を防ぐ）
    ///   (2) 発生源別カウント
    ///   (3) UI 表示用サマリー（解析完了ダイアログ等で使用）
    /// を提供する。発生源名（source）はユーザー向けダイアログにそのまま表示されるため、
    /// 日本語の平易な表現とし、技術的詳細は detail / 例外に載せる。
    ///
    /// スレッド安全（解析のケース並列から呼ばれる）。
    /// </summary>
    public static class CalcFallbackTracker
    {
        private static readonly ConcurrentDictionary<string, int> _counts = new();
        private static long _total;

        /// <summary>
        /// フォールバック発生を記録する。呼び出し側は従来どおり既定値を返してよい。
        /// </summary>
        /// <param name="source">発生源（ユーザー向け表示名。例:「安全限界曲げモーメントの算定（→0）」）</param>
        /// <param name="ex">元の例外（あれば）</param>
        /// <param name="detail">技術的詳細（諸元・軸力など。ログにのみ出力）</param>
        public static void Report(string source, Exception? ex = null, string? detail = null)
        {
            int n = _counts.AddOrUpdate(source, 1, (_, c) => c + 1);
            Interlocked.Increment(ref _total);

            // 初回は例外付きで警告、以後は 1000 回ごとに集計のみ（ログ洪水防止）
            if (n == 1)
                Log.Warning(ex, "[計算フォールバック] {Source}: 計算に失敗し既定値で代替しました。{Detail}", source, detail ?? "");
            else if (n % 1000 == 0)
                Log.Warning("[計算フォールバック] {Source}: 累計 {Count} 回", source, n);
        }

        /// <summary>累計件数（Reset 以降）。</summary>
        public static long TotalCount => Interlocked.Read(ref _total);

        /// <summary>カウンタをリセットする（解析開始時などの区間集計用）。</summary>
        public static void Reset()
        {
            _counts.Clear();
            Interlocked.Exchange(ref _total, 0);
        }

        /// <summary>発生源別サマリー（件数降順、最大 maxItems 件）。イベントが無ければ空文字列。</summary>
        public static string BuildSummary(int maxItems = 8)
        {
            var items = _counts.ToArray()
                .OrderByDescending(kv => kv.Value)
                .Take(maxItems)
                .Select(kv => $"・{kv.Key}: {kv.Value} 回");
            return string.Join(Environment.NewLine, items);
        }
    }
}
