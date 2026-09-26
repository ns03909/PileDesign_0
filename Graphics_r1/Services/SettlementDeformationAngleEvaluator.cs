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
        /// 検定項目を作る。沈下解析を何も実行していなければ空 (対象外)。
        /// 検定の対象なのに必要な結果が欠けているときは「検定不能」の項目を 1 件返す。
        /// </summary>
        public static List<EvaluationItem> Evaluate(InputModel? inputModel)
        {
            var items = new List<EvaluationItem>();
            var piles = inputModel?.PileLayoutItems?.Where(p => p != null).ToList();
            if (piles == null || piles.Count < 2) return items;   // 杭が 1 本なら変形角は無い

            bool includesGroup = inputModel!.FundamentalInput?.SettlementDesignIncludesGroup ?? true;
            string basisName = inputModel.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下";
            string category = $"杭頭変形角 ({basisName}・使用限界)";
            double limit = PileHeadDeformationAngle.ServiceLimit(inputModel.FundamentalInput);

            // 単杭沈下 (常時) は杭ごとに 1 つ [m]。単杭沈下解析をしていない杭は持たない
            var singleByPileNo = new Dictionary<int, double>();
            foreach (var pile in piles)
                if (HasSinglePileSettlement(inputModel, pile))
                    singleByPileNo[pile.PileNo] = pile.SinglePileSettlementVL;

            var groupRecords = inputModel.PileGroupSettlement?.CaseRecords?
                .Where(r => r?.PileSettlements_mm != null && r.PileSettlements_mm.Count > 0).ToList() ?? [];

            // 沈下解析を何も実行していなければ対象外 (未実施)
            if (singleByPileNo.Count == 0 && groupRecords.Count == 0) return items;

            if (!includesGroup)
            {
                var heads = piles.Where(p => singleByPileNo.ContainsKey(p.PileNo))
                    .Select(p => (p.PileNo, p.Point3D.X, p.Point3D.Y, Uz: singleByPileNo[p.PileNo])).ToList();
                AddItem(heads, "VL", "", MissingPiles(piles, singleByPileNo.Keys, "単杭沈下"));
                return items;
            }

            if (groupRecords.Count == 0)
            {
                items.Add(Unavailable("VL",
                    "群杭沈下の結果がありません (群杭沈下解析を実行するか、基本設定で沈下検討の対象を「単杭沈下」にしてください)"));
                return items;
            }

            foreach (var rec in groupRecords)
            {
                // 単杭沈下 [m] + 群杭沈下 [mm→m]。単杭沈下を実行していない杭は 0 として足す
                // (沈下量の検定・画面のグラフ「沈下 単杭+群杭」と同じ組み方。以前からこの扱い)
                var heads = new List<(int PileNo, double X, double Y, double Uz)>();
                foreach (var pile in piles)
                {
                    if (!rec.PileSettlements_mm!.TryGetValue(pile.PileNo, out double group_mm)) continue;
                    heads.Add((pile.PileNo, pile.Point3D.X, pile.Point3D.Y, pile.SinglePileSettlementVL + group_mm * 1e-3));
                }
                string caseName = string.IsNullOrEmpty(rec.LoadCaseName) ? "群杭沈下" : rec.LoadCaseName;
                string typeName = string.IsNullOrEmpty(rec.LoadingType) ? "" : $"（{rec.LoadingType}）";
                AddItem(heads, caseName, typeName, MissingPiles(piles, rec.PileSettlements_mm.Keys, "群杭沈下"));
            }
            return items;

            void AddItem(List<(int PileNo, double X, double Y, double Uz)> heads, string caseName, string typeName, string missing)
            {
                var max = PileHeadDeformationAngle.Max(heads);
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
        /// </summary>
        internal static bool HasSinglePileSettlement(InputModel inputModel, PileLayoutDataItem pile)
        {
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || pile.SoilPileAltNo < 1 || pile.SoilPileAltNo > soilPiles.Count) return false;
            return (soilPiles[pile.SoilPileAltNo - 1]?.LoadDisplacements?.Count ?? 0) > 0;
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
