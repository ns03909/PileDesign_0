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

        public int NgCount => Items.Count(i => !i.IsOk);
        public int OkCount => Items.Count(i => i.IsOk);
        public bool IsEmpty => Items.Count == 0;

        /// <summary>
        /// 支配ケース = 検定比が最大の項目。検定が 0 件なら null。
        ///
        /// NG の有無にかかわらず「一番厳しいところ」を返す。
        /// すべて OK でも、余裕がどれだけあるかはここで分かる。
        /// </summary>
        public EvaluationItem? Governing =>
            Items.Count == 0 ? null : Items.OrderByDescending(i => i.Ratio).First();

        /// <summary>検定比の最大。検定が 0 件なら null。</summary>
        public double? MaxRatio => Governing?.Ratio;

        /// <summary>検定比の降順に並べた項目 (画面の一覧用)。</summary>
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
        public static bool PassesFilter(EvaluationItem item, int displayFilter) => displayFilter switch
        {
            0 => !item.IsOk,
            1 => item.IsOk,
            _ => true,
        };
    }
}
