using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 検定 1 回分の結果。
    ///
    /// 「NG が 3 件」だけでは、どこをどれだけ直せばよいかが分からない。
    /// 検定比の最大 (<see cref="Governing"/>) が分かれば、
    /// 「杭 No.7 頭部が 1.15、次が 0.98」のように直す対象と度合いが決まる。
    /// </summary>
    public sealed class EvaluationResult
    {
        public EvaluationResult(IReadOnlyList<EvaluationItem> items)
        {
            Items = items ?? [];
        }

        public IReadOnlyList<EvaluationItem> Items { get; }

        /// <summary>
        /// NG の件数。<b>収束しなかったケースの項目は数えない</b> —
        /// 応答値が釣り合いを満たしておらず、限界値と比べた結果に意味が無いため。
        /// それらは <see cref="UnconvergedCount"/> で別に数える。
        /// </summary>
        public int NgCount => Items.Count(i => !i.IsFromUnconvergedCase && !i.IsOk);

        /// <summary>OK の件数 (収束しなかったケースの項目を除く)。</summary>
        public int OkCount => Items.Count(i => !i.IsFromUnconvergedCase && i.IsOk);

        /// <summary>収束しなかったケースから作られた項目の件数。OK とも NG とも言えないもの。</summary>
        public int UnconvergedCount => Items.Count(i => i.IsFromUnconvergedCase);

        public bool IsEmpty => Items.Count == 0;

        /// <summary>
        /// 支配ケース = 検定比が最大の項目。検定が 0 件なら null。
        ///
        /// NG の有無にかかわらず「一番厳しいところ」を返す。
        /// すべて OK でも、余裕がどれだけあるかはここで分かる。
        ///
        /// 収束しなかったケースの項目は<b>対象から外す</b>。釣り合っていない応答値の比が
        /// たまたま最大になると、支配ケースとして「解けていないケース」を指してしまう。
        /// 収束したケースが 1 件も無ければ null。
        /// </summary>
        public EvaluationItem? Governing =>
            Items.Where(i => !i.IsFromUnconvergedCase)
                 .OrderByDescending(i => i.Ratio)
                 .FirstOrDefault();

        /// <summary>検定比の最大。検定が 0 件なら null。</summary>
        public double? MaxRatio => Governing?.Ratio;

        /// <summary>
        /// 検定比の降順に並べた項目 (画面の一覧用)。
        /// 収束しなかったケースの項目も含む (画面では「未収束」と表示して並べる)。
        /// </summary>
        public IEnumerable<EvaluationItem> ByRatioDescending =>
            Items.OrderByDescending(i => i.Ratio);

        /// <summary>指定した地震動レベルの項目だけ。</summary>
        public IEnumerable<EvaluationItem> OfLevel(int level) =>
            Items.Where(i => i.Level == level);

        /// <summary>指定した種類の項目だけ。</summary>
        public IEnumerable<EvaluationItem> OfKind(EvaluationKind kind) =>
            Items.Where(i => i.Kind == kind);

        /// <summary>
        /// 表示フィルタ (0=NGのみ / 1=OKのみ / 2=両方) を適用する。
        /// 件数の集計には使わないこと (集計は常に全項目が対象)。
        /// </summary>
        /// <remarks>
        /// 収束しなかったケースの項目は<b>どちらのフィルタでも残す</b>。
        /// OK とも NG とも言えないので、「NG のみ」で消すと見落とし、
        /// 「OK のみ」で残すと合格したように読める。常に見えている方が安全側。
        /// </remarks>
        public static bool PassesFilter(EvaluationItem item, int displayFilter) => displayFilter switch
        {
            0 => item.IsFromUnconvergedCase || !item.IsOk,
            1 => item.IsFromUnconvergedCase || item.IsOk,
            _ => true,
        };
    }
}
