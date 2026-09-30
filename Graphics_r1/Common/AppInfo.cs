using System.Reflection;

namespace PileDesign.Common
{
    /// <summary>
    /// アプリの版。画面 (タイトル・バージョン情報)・解析の条件の記録・計算書が同じ値を使う。
    /// 計算書や解析の記録は画面の層を参照しないので、ここに置く。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>アプリの版 (InformationalVersion の「+コミットの識別」より前。例: 1.0.34-beta)。読めなければ「不明」。</summary>
        internal static string Version { get; } =
            typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "不明";
    }
}
