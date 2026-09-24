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
        /// NG の件数。<b>判定できない項目は数えない</b> — 収束しなかったケースの項目 (応答値が釣り合いを
        /// 満たしていない) と、算定式の適用範囲の外の項目 (限界値が指針の保証の外)。
        /// それらは <see cref="UnconvergedCount"/> / <see cref="OutOfScopeCount"/> で別に数える。
        /// </summary>
        public int NgCount => Items.Count(i => i.IsJudged && !i.IsOk);

        /// <summary>OK の件数 (判定できない項目を除く)。</summary>
        public int OkCount => Items.Count(i => i.IsJudged && i.IsOk);

        /// <summary>収束しなかったケースから作られた項目の件数。OK とも NG とも言えないもの。</summary>
        public int UnconvergedCount => Items.Count(i => i.IsFromUnconvergedCase);

        /// <summary>
        /// 算定式 (工法) の適用範囲の外の項目の件数 (収束しなかったケースの項目を除く)。OK とも NG とも言えないもの。
        /// </summary>
        public int OutOfScopeCount => Items.Count(i => !i.IsFromUnconvergedCase && i.IsOutOfScope);

        /// <summary>
        /// 緩めた基準 (残差 1e-6 に届かず最大 1e-2) で受理したケースから作られた項目の件数。
        ///
        /// <para><b>OK / NG の内訳にも含まれる</b> (未収束とは違い、釣り合いは満たしているとみなす)。
        /// ここは「そのうち何件が緩めた基準だったか」を別に数えるためのもの。行の判定は
        /// 「OK(緩和受理)」と出るのに、集計行が「OK n 件」としか言わないと、
        /// まとめだけ読んだ人には伝わらない。</para>
        /// </summary>
        public int RelaxedCount => Items.Count(i => i.IsFromRelaxedCase);

        public bool IsEmpty => Items.Count == 0;

        /// <summary>
        /// 支配ケース = 検定比が最大の項目。検定が 0 件なら null。
        ///
        /// NG の有無にかかわらず「一番厳しいところ」を返す。
        /// すべて OK でも、余裕がどれだけあるかはここで分かる。
        ///
        /// 判定できない項目 (収束しなかったケース・算定式の適用範囲の外) は<b>対象から外す</b>。
        /// 釣り合っていない応答値や、保証の外の限界値との比がたまたま最大になると、
        /// 支配ケースとして判定できない項目を指してしまう。判定できる項目が 1 件も無ければ null。
        /// </summary>
        public EvaluationItem? Governing =>
            Items.Where(i => i.IsJudged)
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
        /// 判定できない項目 (収束しなかったケース・算定式の適用範囲の外) は<b>どちらのフィルタでも残す</b>。
        /// OK とも NG とも言えないので、「NG のみ」で消すと見落とし、
        /// 「OK のみ」で残すと合格したように読める。常に見えている方が安全側。
        /// </remarks>
        public static bool PassesFilter(EvaluationItem item, int displayFilter) => displayFilter switch
        {
            0 => !item.IsJudged || !item.IsOk,
            1 => !item.IsJudged || item.IsOk,
            _ => true,
        };
    }
}
