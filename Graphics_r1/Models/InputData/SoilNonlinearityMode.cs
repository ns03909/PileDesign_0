using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 水平地盤ばね (p-y 関係) の非線形性の考慮段階。
    ///
    /// 旧 <c>LoadCase.IsSoilNonLinear</c> (bool) を 3 段階に拡張したもの。
    /// 旧 false = <see cref="Linear"/>、旧 true = <see cref="KhReductionWithPy"/> に対応する。
    /// </summary>
    public enum SoilNonlinearityMode
    {
        /// <summary>線形: kh = kh0 固定 (y0 = 1cm における基準値)。py による頭打ちも行わない。</summary>
        Linear = 0,

        /// <summary>kh 低減のみ: kh = kh0/√(|y|/y0) (|y| ≤ 0.1y0 は 3.16·kh0 で頭打ち)。py による頭打ちは行わない。</summary>
        KhReduction = 1,

        /// <summary>kh 低減 + py 頭打ち: 上記に加え p が塑性地盤反力 py に達したら頭打ちとする (従来の非線形)。</summary>
        KhReductionWithPy = 2,
    }

    /// <summary>
    /// <see cref="SoilNonlinearityMode"/> の表示名。UI の ComboBox / 計算書 (docx) / グラフ凡例で共通に使う。
    /// </summary>
    public static class SoilNonlinearityModes
    {
        public const string LinearText = "線形 (kh0 固定)";
        public const string KhReductionText = "kh 低減のみ";
        public const string KhReductionWithPyText = "kh 低減 + py 頭打ち";

        /// <summary>ComboBox の ItemsSource 用 (表示順は enum 値の昇順 = 非線形性が強くなる順)。</summary>
        public static IReadOnlyList<SoilNonlinearityMode> All { get; } =
        [
            SoilNonlinearityMode.Linear,
            SoilNonlinearityMode.KhReduction,
            SoilNonlinearityMode.KhReductionWithPy,
        ];

        public static string ToText(SoilNonlinearityMode mode) => mode switch
        {
            SoilNonlinearityMode.Linear => LinearText,
            SoilNonlinearityMode.KhReduction => KhReductionText,
            _ => KhReductionWithPyText,
        };

        /// <summary>計算書 (docx) の入力条件表など、狭い欄に収める短縮表記。</summary>
        public static string ToShortText(SoilNonlinearityMode mode) => mode switch
        {
            SoilNonlinearityMode.Linear => "線形",
            SoilNonlinearityMode.KhReduction => "kh低減",
            _ => "kh低減+py",
        };

        /// <summary>非線形 (= 荷重を段階的に載荷して反復する必要がある) かどうか。</summary>
        public static bool IsNonLinear(this SoilNonlinearityMode mode) => mode != SoilNonlinearityMode.Linear;
    }
}
