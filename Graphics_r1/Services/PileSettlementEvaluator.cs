using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

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
    /// <para>応答値は<b>単杭沈下 + 群杭沈下 (常時 VL の記録)</b>の合計。画面のグラフ「沈下 単杭＋群杭（基礎梁なし）」と同じ組み方。
    /// 基本設定の「沈下検討の対象」が単杭沈下だけなら群杭ぶんは足さない。「単杭＋群杭沈下」なのに群杭沈下 (常時) の
    /// 記録が無ければ、杭ごとに検定不能にする (杭頭変形角の検定と同じ扱い。以前は群杭ぶんを 0 として判定していた)。
    /// 常時の記録が基礎梁なし・ありの 2 つあれば、それぞれについて項目を作る。</para>
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
            // 正の有限の数でない許容値では比べようがない。以前は「0 より大きい」だけを見ていたので、
            // 無限大が通って有限の沈下量がすべて許容値以下になった。0 以下・数値でない値は黙って検定を
            // 省いていた。検定を有効にしているのに何も出ないと気づけないので、入力箇所を示す項目を 1 件出す
            if (!(double.IsFinite(limit) && limit > 0))
            {
                items.Add(new EvaluationItem
                {
                    Kind = EvaluationKind.PileSettlement,
                    Level = 0,
                    Category = "杭の沈下量",
                    LimitName = LimitName,
                    TargetName = LimitName,
                    LoadCaseName = "VL",
                    Response = double.NaN,
                    Limit = double.NaN,
                    Unit = Unit,
                    UnavailableReason = DescribeUnusableAllowable(limit),
                });
                return items;
            }

            var piles = inputModel!.PileLayoutItems;
            if (piles == null) return items;

            // 単杭沈下と群杭沈下の組み方 (どの記録を足すか・欠けたときの理由) は PileSettlementComposition に 1 つにまとめた。
            // 群杭沈下は常時 (VL) の記録を使う。基本設定の「沈下検討の対象」が単杭沈下だけなら足さない。
            // 以前は画面で表示中のケース (ActiveRecord) の値を足していたので、表示を地震時のケースへ切り替えると
            // 地震時の群杭沈下 (または 0) を「常時」として足し、合否が表示の切り替えで変わった。
            // 「沈下検討の対象」も見ておらず、単杭沈下だけを選んでも群杭ぶんを足していた
            bool includesGroup = fundamental.SettlementDesignIncludesGroup;
            var vlRecords = includesGroup ? PileSettlementComposition.LongTermRecords(inputModel) : [];
            // 「単杭＋群杭沈下」なのに群杭沈下 (常時) の記録が無ければ、杭ごとに検定不能にする
            // (Compose が記録 null に NoGroupResultReason を付ける)。
            // 以前は群杭ぶんを 0 として単杭沈下だけで OK / NG を出しており、同じ状態で杭頭変形角
            // (SettlementDeformationAngleEvaluator) は「群杭沈下の結果がありません」と検定不能にしていた。
            // 足すべき群杭沈下を足さない値は、選んだ基準の沈下量ではない
            var groupCases = vlRecords.Count > 0
                ? vlRecords.Select(r => (Record: (GroupSettlementCaseRecord?)r,
                    Suffix: vlRecords.Count > 1 ? $"（{(r.IsBeamAware ? "群杭 基礎梁あり" : "群杭 基礎梁なし")}）" : "")).ToList()
                : [((GroupSettlementCaseRecord?)null, "")];

            foreach (var pile in piles)
            {
                if (pile == null) continue;

                foreach (var (rec, suffix) in groupCases)
                {
                    var parts = PileSettlementComposition.Compose(inputModel, pile, rec, includesGroup);

                    // 単杭沈下量は解析していなくても 0 を持つ。値ではなく、その杭の単杭沈下の結果 (荷重-沈下曲線) が
                    // あるかで見る。以前は未計算の杭を 0 mm・OK と出していた
                    if (!parts.HasSingle)
                    {
                        items.Add(new EvaluationItem
                        {
                            Kind = EvaluationKind.PileSettlement,
                            Level = 0,
                            Category = "杭の沈下量",
                            LimitName = LimitName,
                            TargetName = $"Pile-{pile.PileNo}",
                            PileNo = pile.PileNo,
                            PileBodyNo = pile.PileBodyNo,
                            LoadCaseName = "VL",
                            Response = double.NaN,
                            Limit = limit,
                            Unit = Unit,
                            UnavailableReason = DescribeNoSinglePileResult(pile.PileNo),
                        });
                        break;   // 単杭沈下が無い杭は記録の数に関わらず 1 件
                    }

                    // 単杭沈下 (m) + 群杭沈下 (mm)。欠けた群杭ぶんは 0 のまま足し、理由があれば値を出さない
                    double response = parts.Sum_mm;
                    string? groupProblem = parts.GroupReason;

                    // 単杭沈下量が荷重-沈下曲線の範囲外の軸力で求めた値なら、それは下限 (実際はもっと沈む)。
                    // 下限で許容値を超えていれば NG と言えるが、超えていなければ OK とは言えない
                    string? unavailable = groupProblem
                        ?? (parts.SingleBeyondCurve && !(response > limit) ? DescribeBeyondCurve(pile.PileNo) : null);

                    items.Add(new EvaluationItem
                    {
                        Kind = EvaluationKind.PileSettlement,
                        Level = 0,   // 常時。水平解析の地震動レベルとは別軸
                        Category = "杭の沈下量",
                        LimitName = LimitName,
                        TargetName = $"Pile-{pile.PileNo}",
                        PileNo = pile.PileNo,
                        PileBodyNo = pile.PileBodyNo,
                        LoadCaseName = "VL" + suffix,
                        Response = groupProblem != null ? double.NaN : response,
                        Limit = limit,
                        Unit = Unit,
                        UnavailableReason = unavailable,
                        IsOk = unavailable == null && !(response > limit),
                        // 基礎梁ありの反復が収束しなかった記録の沈下は、釣り合っていない値 (変形角の検定と同じ扱い)
                        CaseConvergence = parts.GroupUnconverged ? PileDesign.FEM.StepStatus.Unconverged : PileDesign.FEM.StepStatus.Converged,
                    });
                }
            }

            return items;
        }

        /// <summary>許容沈下量が使えない値のときの理由 (入力箇所を示す)。</summary>
        internal static string DescribeUnusableAllowable(double limit)
            => $"基本設定の「許容沈下量」が正の数ではありません ({limit})。基本設定で入力し直してください";

        /// <summary>単杭沈下の結果が無い杭の理由。</summary>
        internal static string DescribeNoSinglePileResult(int pileNo)
            => $"杭No.{pileNo} の単杭沈下解析の結果がありません。単杭沈下解析を実行してください (0 mm として判定しません)";

        /// <summary>単杭沈下量が曲線の範囲外で、許容値以下と言えないときの理由。</summary>
        internal static string DescribeBeyondCurve(int pileNo)
            => $"杭No.{pileNo} の常時軸力が単杭沈下の荷重-沈下曲線の範囲を超えています。"
             + "沈下量は曲線の端の値 (下限) で、許容値以下とは言えません";
    }
}
