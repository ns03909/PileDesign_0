using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 沈下解析の沈下量から、杭頭 2 点間の変形角を検定する (常時・使用限界)。
    ///
    /// <para>水平解析の杭頭変位による変形角とは別の項目。こちらは常時荷重による即時沈下の不同分。
    /// <b>杭基礎では圧密沈下は生じない</b> (圧密沈下の検討が要るのは直接基礎を圧密層に載せる場合)。
    /// 基礎指針'19 表5.3.8 の常時荷重・使用限界には即時沈下 1×10⁻³ と圧密沈下 2×10⁻³ が併記されているが、
    /// ここで使うのは前者。</para>
    ///
    /// <para>沈下検討の対象 (基本設定) で 2 通りに分かれる。
    /// <list type="bullet">
    /// <item><b>単杭沈下のみ</b>: 各杭の単杭沈下量から 1 件。群杭沈下の結果は要らない。</item>
    /// <item><b>単杭＋群杭沈下</b>: 群杭沈下のケースごとに、単杭沈下 + そのケースの群杭沈下から 1 件ずつ。</item>
    /// </list></para>
    ///
    /// <para>以前は水平解析の検定の中にあり、<b>群杭沈下のケースの記録が無いと何も出さず</b>、検定する杭もその記録から選んでいた。
    /// そのため「単杭沈下のみ」を選んで単杭沈下だけを計算しても検定が出なかった。水平解析を済ませていないと
    /// 出ないうえ、水平解析の検定 (低減前・低減後) の両方に同じ項目が並んでいた。
    /// 水平解析の結果は要らないので、沈下量の検定 (<see cref="PileSettlementEvaluator"/>) と同じく沈下の側で組む。</para>
    /// </summary>
    public static class SettlementDeformationAngleEvaluator
    {
        /// <summary>
        /// 沈下検討の対象が「単杭＋群杭沈下」なのに、群杭沈下 (常時) の記録が無いときの理由。
        /// 沈下量の検定 (<see cref="PileSettlementEvaluator"/>) も同じ文で検定不能にする。
        /// </summary>
        internal const string NoGroupResultReason =
            "群杭沈下の結果がありません (群杭沈下解析を実行するか、基本設定で沈下検討の対象を「単杭沈下」にしてください)";

        /// <summary>
        /// 検定項目を作る。沈下解析を何も実行していなければ空 (対象外)。
        /// 検定の対象なのに必要な結果が欠けているときは「検定不能」の項目を 1 件返す。
        /// </summary>
        public static List<EvaluationItem> Evaluate(InputModel? inputModel)
        {
            var items = new List<EvaluationItem>();
            var piles = inputModel?.PileLayoutItems?.Where(p => p != null).ToList();
            if (piles == null || piles.Count < 2) return items;   // 杭が 1 本なら変形角は無い

            bool includesGroup = PileSettlementComposition.IncludesGroup(inputModel);
            string basisName = inputModel!.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下";
            string category = $"杭頭変形角 ({basisName}・使用限界)";
            double limit = PileHeadDeformationAngle.ServiceLimit(inputModel.FundamentalInput);

            // 単杭沈下 (常時) は杭ごとに 1 つ [m]。単杭沈下解析をしていない杭は持たない。
            // 単杭沈下と群杭沈下の組み方は PileSettlementComposition に 1 つにまとめた (沈下量の検定・計算書の一覧表と同じ)
            var singleOnly = piles.Select(p => PileSettlementComposition.Compose(inputModel, p, null, includeGroup: false)).ToList();
            var singleByPileNo = new Dictionary<int, double>();
            foreach (var s in singleOnly)
                if (s.HasSingle)
                    singleByPileNo[s.PileNo] = s.Single_m;

            // 単杭沈下量が荷重-沈下曲線の範囲外 (下限) の杭。変形角は 2 点の差なので、下限では上にも下にも抑えられない
            var beyondCurve = piles.Where(p => singleByPileNo.ContainsKey(p.PileNo) && p.SinglePileSettlementVLBeyondCurve)
                .Select(p => p.PileNo).OrderBy(n => n).ToList();
            string? beyondReason = beyondCurve.Count == 0 ? null
                : $"単杭沈下量が荷重-沈下曲線の範囲外 (曲線の端の値 = 下限) の杭があり、変形角を求められません: 杭No.{string.Join(", ", beyondCurve)}";

            // 常時 (VL) の記録だけを使う (沈下量の検定 PileSettlementEvaluator と同じ)。この検定は常時・使用限界。
            // 以前は基礎梁ありで L1/L2 も解いた記録 ("L1-1: …") まで常時の限界で判定し、根拠のない NG を出していた
            var groupRecords = PileSettlementComposition.LongTermRecords(inputModel);

            // 沈下解析を何も実行していなければ対象外 (未実施)
            if (singleByPileNo.Count == 0 && groupRecords.Count == 0) return items;

            // 単杭沈下の結果が無い杭が 1 本でもあれば判定しない。変形角は全杭の組の最大なので、欠けた杭を除いて
            // 求めた値は最大とは言えない。以前は残りの杭 (2 本以上) で OK / NG を出していた
            string? singleMissing = singleByPileNo.Count == piles.Count ? null
                : MissingPiles(piles, singleByPileNo.Keys, "単杭沈下") + "。全杭の結果がそろわないと、最大の変形角を求められません";

            if (!includesGroup)
            {
                if (singleMissing != null)
                {
                    items.Add(Unavailable("VL", singleMissing));
                    return items;
                }
                var heads = piles.Zip(singleOnly, (p, s) => (p.PileNo, p.Point3D.X, p.Point3D.Y, Uz: s.Sum_m)).ToList();
                AddItem(heads, "VL", "", MissingPiles(piles, singleByPileNo.Keys, "単杭沈下"));
                return items;
            }

            if (groupRecords.Count == 0)
            {
                items.Add(Unavailable("VL", NoGroupResultReason));
                return items;
            }

            foreach (var rec in groupRecords)
            {
                string caseName = string.IsNullOrEmpty(rec.LoadCaseName) ? "群杭沈下" : rec.LoadCaseName;

                // 単杭沈下 [m] + 群杭沈下 [mm→m]。どちらかが欠けた杭があれば判定しない。
                // 以前は単杭沈下を実行していない杭を 0 として足し (単杭沈下が無く群杭沈下だけがそろうと変形角 0 で OK)、
                // 群杭沈下の記録に無い杭は黙って除いていた。沈下量の検定 (PileSettlementEvaluator) は単杭沈下が無い杭を
                // 判定不能にしており、それとも食い違っていた
                if (singleMissing != null)
                {
                    items.Add(Unavailable(caseName, singleMissing));
                    continue;
                }
                var parts = piles.Select(p => PileSettlementComposition.Compose(inputModel, p, rec, includeGroup: true)).ToList();
                var withGroup = parts.Where(x => x.HasGroup).Select(x => x.PileNo).ToList();
                if (parts.Any(x => !x.HasGroup))
                {
                    items.Add(Unavailable(caseName,
                        MissingPiles(piles, withGroup, "群杭沈下") + "。群杭沈下解析をやり直してください"));
                    continue;
                }
                var heads = piles.Zip(parts, (pile, x) => (pile.PileNo, pile.Point3D.X, pile.Point3D.Y, Uz: x.Sum_m)).ToList();
                string typeName = string.IsNullOrEmpty(rec.LoadingType) ? "" : $"（{PileGroupSettlement.LoadingTypeDisplayName(rec.LoadingType)}）";
                // 旧形式から復元した結果は、どの解析で得た値か確かめられない。収束した結果と同じには判定しない
                if (rec.IsConvergenceUnknown)
                {
                    items.Add(Unavailable(caseName, LegacySettlementMigration.UnknownConvergenceReason));
                    continue;
                }
                int before = items.Count;
                AddItem(heads, caseName, typeName, MissingPiles(piles, withGroup, "群杭沈下"));
                // 基礎梁を考慮した反復が収束しなかったケースは判定しない (反復しない解析の記録は IsConverged を持たないので対象外) (釣り合っていない沈下の変形角なので「未収束」)
                if (parts.Any(x => x.GroupUnconverged))
                    for (int i = before; i < items.Count; i++)
                        items[i] = items[i] with { CaseConvergence = PileDesign.FEM.StepStatus.Unconverged };
            }
            return items;

            void AddItem(List<(int PileNo, double X, double Y, double Uz)> heads, string caseName, string typeName, string missing)
            {
                if (beyondReason != null)
                {
                    items.Add(Unavailable(caseName, beyondReason));
                    return;
                }
                PileHeadDeformationAngle.TryMax(heads, out var max, out var invalidReason);
                if (invalidReason != null)
                {
                    items.Add(Unavailable(caseName, invalidReason));
                    return;
                }
                if (max == null)
                {
                    items.Add(Unavailable(caseName, $"沈下量が 2 本以上の杭で得られません ({missing})"));
                    return;
                }
                items.Add(new EvaluationItem
                {
                    Kind = EvaluationKind.PileHeadDeformationAngle,
                    Level = 0,
                    Category = category,
                    LimitName = "使用限界",
                    TargetName = $"杭No.{max.Value.PileNoA} − 杭No.{max.Value.PileNoB}{typeName}",
                    PileNo = max.Value.PileNoA,
                    LoadCaseName = caseName,
                    // 沈下解析に液状化の区別は無い (null のまま)
                    Response = max.Value.Angle,
                    Limit = limit,
                    Unit = "rad",
                    IsOk = !(max.Value.Angle > limit),
                });
            }

            EvaluationItem Unavailable(string caseName, string reason) => new()
            {
                Kind = EvaluationKind.PileHeadDeformationAngle,
                Level = 0,
                Category = category,
                LimitName = "使用限界",
                TargetName = "杭頭 2 点間",
                LoadCaseName = caseName,
                Response = double.NaN,
                Limit = limit,
                Unit = "rad",
                UnavailableReason = reason,
            };
        }

        /// <summary>
        /// 杭の単杭沈下解析の結果があるか。単杭沈下量 (<see cref="PileLayoutDataItem.SinglePileSettlementVL"/>) は
        /// 解析していなくても 0 を持つので、0 と「未実施」を区別できない。土層-杭セットの荷重-沈下曲線の有無で見る
        /// (保存・読込でも残る)。
        ///
        /// <para>曲線は<b>2 点以上</b>要る。沈下量は曲線を補間して求めるので、1 点では求められず
        /// (VerticalLoadTransferMethod.GetDisplacementForGivenLoad が null を返し)、沈下量は前回の値のまま残る。
        /// 以前は 1 点でも「結果あり」とし、前回の値で検定していた。</para>
        /// </summary>
        internal static bool HasSinglePileSettlement(InputModel inputModel, PileLayoutDataItem pile)
        {
            return (pile.SoilPileAt(inputModel)?.LoadDisplacements?.Count ?? 0) >= 2;
        }

        private static string MissingPiles(List<PileLayoutDataItem> piles, IEnumerable<int> withResult, string what)
        {
            var have = withResult.ToHashSet();
            var missing = piles.Where(p => !have.Contains(p.PileNo)).Select(p => p.PileNo).OrderBy(n => n).ToList();
            return missing.Count == 0
                ? $"{what}の結果が足りません"
                : $"{what}の結果が無い杭: No." + string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? " ほか" : "");
        }
    }
}
