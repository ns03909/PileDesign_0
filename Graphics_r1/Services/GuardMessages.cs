namespace PileDesign.Services
{
    /// <summary>
    /// 前提が足りないときの案内文。
    ///
    /// 同じ状況の文言が場所ごとに違っていた (「杭が無い」だけで 4 通り) ため、
    /// 同じ状況には同じ文が出るようここに集める。
    ///
    /// 書き方の規約は<b>「現象 + 理由 + 対処」</b>:
    ///   ・何が起きたか (できなかったこと)
    ///   ・なぜか (足りない前提)
    ///   ・どこで何をすれば直るか (画面名とボタン名を具体的に)
    /// 内部の識別子 (PileLayoutItems・IsAnalysisTarget など) は出さない。
    /// </summary>
    public static class GuardMessages
    {
        /// <summary>杭が 1 本も配置されていない。</summary>
        public const string NoPileLayout =
            "杭が 1 本も配置されていません。\n" +
            "メイン画面の「杭」タブで杭を追加してください。";

        /// <summary>
        /// 杭要素分割が済んでいない。
        ///
        /// <para><b>「押してください」で終わらせない場面がある。</b>足りない前提が分かって
        /// いて、それを済ませる画面もこちらが持っているなら、案内より
        /// <see cref="NotElementSplitAsk"/> で訊くほうが早い——閉じて探して実行して
        /// もう一度、の四手が一手になる。</para>
        ///
        /// <para>この文は<b>訊けない場所</b>（もう解析の途中で、後戻りできないところ）
        /// のために残す。</para>
        /// </summary>
        public const string NotElementSplit =
            "杭要素分割が済んでいません。\n" +
            "リボンの「杭要素分割」(F4) を実行してください。";

        /// <summary>
        /// 杭要素分割が済んでいない——<b>その場で実行するかを訊く</b>。
        ///
        /// <para>何をしようとして止まったのかを頭に置く。「杭要素分割が済んでいません」
        /// だけでは、押した覚えのないものが止まったように読める。</para>
        /// </summary>
        public static string NotElementSplitAsk(string what) =>
            $"{what}には杭要素分割が済んでいる必要がありますが、まだ済んでいません。\n" +
            "いま実行しますか。";

        /// <summary>
        /// 単杭沈下解析が済んでいない（荷重-変位が無い）。
        ///
        /// <para>基礎梁を考慮した沈下は、単杭の荷重-変位を土台にする。以前この状況にも
        /// 「杭要素分割が済んでいません」と出していたが、<b>分割は済んでいる</b>ので、
        /// 言われたとおり F4 をやり直しても直らない。</para>
        /// </summary>
        public const string NoLoadDisplacements =
            "単杭沈下解析が済んでいません。\n" +
            "リボンの「単杭沈下解析」(F6) を実行してください。";

        /// <summary>地盤の土層が入力されていない。</summary>
        public const string NoGroundLayer =
            "地盤の土層が入力されていません。\n" +
            "リボンの「地盤」で土層を追加してください。";

        /// <summary>解析対象の荷重ケースが選ばれていない。</summary>
        public const string NoAnalysisTargetLoadCase =
            "解析対象の荷重ケースが 1 件もありません。\n" +
            "リボンの「荷重条件」で、対象にしたいケースの「解析対象」にチェックを入れてください。";

        /// <summary>
        /// ウィンドウを開けなかったときの案内。
        ///
        /// 以前は 9 箇所でほぼ同じ文と <c>ex.Message</c> の直出しが繰り返されていた。
        /// 例外の文面は英語の実装都合が多く読んでも次の操作が決まらないので、
        /// 画面には「何ができなかったか」と「ログの場所」だけを出し、
        /// 中身はログに残す (呼び出し側で Serilog に記録すること)。
        /// </summary>
        public static string WindowOpenFailed(string windowName) =>
            $"{windowName}を開けませんでした。\n" +
            "もう一度お試しください。解消しない場合はアプリを再起動してください。\n\n" +
            $"詳細はログ ({PileDesign.Common.Logging.AppLog.LogDirectory}) に記録しています。";

        /// <summary>基礎梁が定義されていない。</summary>
        public const string NoFoundationBeam =
            "基礎梁が定義されていません。\n" +
            "メイン画面の「基礎梁」タブで梁を追加してください。";
    }
}
