using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 非線形反復の収束判定で、残差を<b>何の大きさで割るか</b> (基準値の取り方)。
    ///
    /// <para>判定は残差比 ‖R‖²/‖基準‖² で行う (<c>AnaModel.FindR</c>)。残差 R はまだ釣り合って
    /// いない力なので、絶対値で見ると荷重の大きいモデルほど厳しくなる。そのため何かの力の
    /// 大きさで割って無次元にするが、<b>何で割るかで判定の厳しさが変わる</b>。</para>
    ///
    /// <para>従来は外力 (慣性力) のみを基準にしていた。液状化ケースは地盤変位を強制変位で
    /// 与えるので、慣性力が小さくても構造は大きく動く。慣性力が 0 の組合せでは基準が 0 になり、
    /// 比が固定値のまま反復上限まで回る。慣性力が小さいだけのケースでも、基準が実際の駆動力より
    /// 小さいので比が過大に出て、必要以上に反復する。</para>
    /// </summary>
    public enum ResidualReferenceMode
    {
        /// <summary>
        /// 外力のみ: ‖F‖² で割る (従来)。
        /// <b>強制変位だけで駆動するケース (慣性力 0) では基準が 0 になり、判定が成り立たない。</b>
        /// この設定を選んだときだけ、慣性力 0 の組合せを入力検査で止める。
        /// 過去の結果と比べるとき以外に選ぶ理由は無い。
        /// </summary>
        ExternalForce = 0,

        /// <summary>
        /// 外力と強制変位の反力の大きい方: max(‖F‖², ‖T_強制変位‖²) で割る (<b>既定</b>)。
        /// ANSYS の CNVTOL が採る流儀 (基準値が小さすぎるときの下限を持つ) に相当する。
        /// 強制変位で駆動するケースでも判定が成り立つので、これを既定とする
        /// (<see cref="ResidualReferenceModes.Default"/>)。履歴を持たないので、
        /// 再試行やケース並列でも同じ基準になる。
        /// </summary>
        ExternalForceOrReaction = 1,

        /// <summary>
        /// 内力の時間平均: これまでのステップの ‖T‖ の平均を基準にする (外力・反力の大きい方を下限とする)。
        /// Abaqus/Standard の flux norm (蓄えた内力の時間平均に対して残差を見る) に相当する。
        /// 駆動源が慣性力と地盤変位で混在するモデルでも、ケースごとの厳しさが揃う。
        /// </summary>
        InternalForce = 2,
    }

    /// <summary>
    /// <see cref="ResidualReferenceMode"/> の表示名。基本設定・計算書・ヘルプで共通に使う。
    /// </summary>
    public static class ResidualReferenceModes
    {
        /// <summary>
        /// 既定の基準値の取り方。<b>定義はここだけ</b>に置く。
        ///
        /// <para>入力 (<c>FundamentalInput</c>)・解析モデル (<c>AnaModel</c>)・入力検査・水平解析の
        /// 受け渡しがそれぞれ既定値を書くと、片方だけ直して食い違う。
        /// <c>ResidualReferenceModeTests</c> が 3 者の一致を見る。</para>
        ///
        /// <para>外力のみではなく<b>外力と反力の大きい方</b>を既定とする。外力 (慣性力) が 0 の組合せで
        /// 判定そのものが成り立たなくなるのを避けるため (2026-09-12 に既定を切り替えた)。</para>
        /// </summary>
        public const ResidualReferenceMode Default = ResidualReferenceMode.ExternalForceOrReaction;

        public const string ExternalForceText = "外力のみ（旧版の判定）";
        public const string ExternalForceOrReactionText = "外力と強制変位の反力の大きい方（既定）";
        public const string InternalForceText = "内力の時間平均";

        /// <summary>ComboBox の ItemsSource 用 (表示順は enum 値の昇順)。</summary>
        public static IReadOnlyList<ResidualReferenceMode> All { get; } =
        [
            ResidualReferenceMode.ExternalForce,
            ResidualReferenceMode.ExternalForceOrReaction,
            ResidualReferenceMode.InternalForce,
        ];

        public static string ToText(ResidualReferenceMode mode) => mode switch
        {
            ResidualReferenceMode.ExternalForceOrReaction => ExternalForceOrReactionText,
            ResidualReferenceMode.InternalForce => InternalForceText,
            _ => ExternalForceText,
        };

        /// <summary>計算書 (docx) の入力条件表など、狭い欄に収める短縮表記。</summary>
        public static string ToShortText(ResidualReferenceMode mode) => mode switch
        {
            ResidualReferenceMode.ExternalForceOrReaction => "外力/反力",
            ResidualReferenceMode.InternalForce => "内力平均",
            _ => "外力",
        };

        /// <summary>強制変位だけで駆動するケース (慣性力 0) でも判定が成り立つかどうか。</summary>
        public static bool WorksWithoutInertia(this ResidualReferenceMode mode)
            => mode != ResidualReferenceMode.ExternalForce;
    }
}
