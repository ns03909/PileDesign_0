using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭の沈下量の検定。応答 = 各杭の沈下量、限界 = 許容沈下量（入力値）。
    ///
    /// <para><b>既定では検定しない。</b> 許容沈下量は構造・基礎形式・上部構造が許せる変形から
    /// 設計者が決めるもので、規準が一意の値を与えるわけではない (建築基礎構造設計指針は
    /// 構造種別に応じて定めるとする)。プログラムが勝手に値を置いて合否を出すと、
    /// 根拠の無い判定が計算書に残る。基本設定で明示的に有効にしたときだけ検定する。</para>
    ///
    /// <para>応答値は<b>単杭沈下 + 群杭沈下</b>の合計。画面のグラフ「沈下 単杭+群杭」と同じ組み方で、
    /// 群杭沈下を実行していなければ群杭ぶんは 0 になる (単杭だけの沈下量と等しい)。</para>
    ///
    /// <para>対象は<b>長期 (VL) だけ</b>。許容沈下量は常時の沈下に対して定める量で、
    /// 地震時の沈下量と比べる根拠が無い。地震動レベルは 0 で持つ
    /// (基礎梁の傾斜角の検定と同じ扱い — 水平解析の地震動レベルとは別軸)。</para>
    ///
    /// <para>水平解析の結果は要らない。沈下解析さえ済んでいれば検定できる。</para>
    /// </summary>
    public static class PileSettlementEvaluator
    {
        private const string Unit = "mm";

        /// <summary>限界値の呼び名 (計算書・表・テキスト出力で使う)。</summary>
        public const string LimitName = "許容沈下量";

        /// <summary>全杭配置について沈下量の検定項目を作る。無効なら空を返す。</summary>
        public static List<EvaluationItem> Evaluate(InputModel? inputModel)
        {
            var items = new List<EvaluationItem>();

            var fundamental = inputModel?.FundamentalInput;
            if (fundamental?.EvaluateSettlement != true) return items;

            double limit = fundamental.AllowableSettlement_mm;
            // 0 や負、数値でない許容値では比べようがない。黙って OK を出すより検定しない
            if (!(limit > 0)) return items;

            var piles = inputModel!.PileLayoutItems;
            if (piles == null) return items;

            var pgs = inputModel.PileGroupSettlement;

            foreach (var pile in piles)
            {
                if (pile == null) continue;

                // 単杭沈下は m で持たれている。群杭沈下は mm。画面のグラフと同じ組み方
                double single_mm = pile.SinglePileSettlementVL * 1000.0;
                double group_mm = pgs?.SettlementOf(pile.PileNo) ?? 0.0;
                double response = single_mm + group_mm;

                items.Add(new EvaluationItem
                {
                    Kind = EvaluationKind.PileSettlement,
                    Level = 0,   // 常時。水平解析の地震動レベルとは別軸
                    Category = "杭の沈下量",
                    LimitName = LimitName,
                    TargetName = $"Pile-{pile.PileNo}",
                    PileNo = pile.PileNo,
                    PileBodyNo = pile.PileBodyNo,
                    LoadCaseName = "VL",
                    Response = response,
                    Limit = limit,
                    Unit = Unit,
                    IsOk = !(response > limit),
                });
            }

            return items;
        }
    }
}
