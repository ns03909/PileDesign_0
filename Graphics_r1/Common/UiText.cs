namespace PileDesign.Common
{
    /// <summary>
    /// 画面に出る共通の語。
    ///
    /// 「絞り込みなし」を表す選択肢は、グラフ側が "All"、テーブル側が "ALL" と
    /// 綴りまで分かれたうえ、日本語 UI の中でここだけ英語だった。
    /// 表示文字列と番兵 (この値なら絞り込まない) を兼ねているため、
    /// 定数を 1 つ置いて両方をここから採る。
    /// </summary>
    public static class UiText
    {
        /// <summary>絞り込みなしを表す選択肢。</summary>
        public const string All = "すべて";

        /// <summary>
        /// 「絞り込みなし」かどうか。旧綴り ("All" / "ALL") も受け付ける
        /// (保存済みのレイアウトや外部から来た文字列が混ざっても壊れないように)。
        /// </summary>
        public static bool IsAll(string? value) =>
            value is All or "All" or "ALL";
    }
}
