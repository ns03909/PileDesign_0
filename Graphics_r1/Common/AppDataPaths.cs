using System;
using System.IO;

namespace PileDesign.Common
{
    /// <summary>
    /// アプリが利用者ごとに書くファイル (自動保存・ログ・クラッシュレポート・最近使ったファイル・レイアウト・設定・ブラウザの作業領域) の置き場。
    ///
    /// <para>以前はそれぞれが <c>%LocalAppData%\PileDesign</c> を自分で組み立てていたので、テストも利用者の実際のフォルダに書いていた。
    /// 自動保存のテストは古いファイルの掃除まで走らせるので、利用者の自動保存を消しうる
    /// (2026-10-08 の実機確認で、全体テストの時刻に自動保存ファイルが増えたり入れ替わったりしていた)。
    /// 置き場をここ 1 か所にし、テストは実行の最初に一時フォルダへ向ける。</para>
    /// </summary>
    public static class AppDataPaths
    {
        private static string? _root;

        /// <summary>置き場の根 (既定は <c>%LocalAppData%\PileDesign</c>)。</summary>
        public static string Root => _root ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PileDesign");

        /// <summary>根の下のフォルダ (無ければ作らない)。</summary>
        public static string Sub(string name) => Path.Combine(Root, name);

        /// <summary>
        /// 置き場を差し替える。<b>テストの実行の最初にだけ</b>使う (サービスは生成時に場所を決めるので、途中で変えても効かないものがある)。
        /// </summary>
        internal static void OverrideRootForTests(string root) => _root = root;
    }
}
