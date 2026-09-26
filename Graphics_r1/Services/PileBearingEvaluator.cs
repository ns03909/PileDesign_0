using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭の鉛直支持力の検定（押込み・引抜き）。
    ///
    /// 限界値は <see cref="SoilPile.CalculateResistances"/> が求めたものをそのまま使う。
    /// 荷重との対応は限界状態で取る（杭体断面の検定と同じ規則）。
    ///
    /// <code>
    ///   長期 (VL)   使用限界   押込み R_SLS = Ru/3      引抜き Rt_SLS = Rtu/3
    ///   レベル1     損傷限界   押込み R_DLS = Ru/1.5    引抜き Rt_DLS = Rty (降伏引抜き抵抗力)
    ///   レベル2     終局限界   押込み R_ULS = Ru        引抜き Rt_ULS = Rtr (残留引抜き抵抗力)
    ///                          ※ 耐震グレード S ではレベル2 も損傷限界
    /// </code>
    ///
    /// 応答値は<b>入力した杭軸力</b>、限界値は地盤と杭体から決まるので、
    /// 水平解析の結果は要らない（杭要素分割さえ済んでいれば検定できる）。
    /// ただし解析結果テーブルの 1 枚に混ぜて出すため、呼び出しは検定全体と同じ経路に置いてある。
    /// </summary>
    public static class PileBearingEvaluator
    {
        private const string Unit = "kN";

        /// <summary>全杭配置について支持力の検定項目を作る。</summary>
        public static List<EvaluationItem> Evaluate(InputModel? inputModel, string seismicGrade)
        {
            var items = new List<EvaluationItem>();
            if (inputModel?.PileLayoutItems == null) return items;

            var level1Cases = SeismicCases(inputModel, level: 1);
            var level2Cases = SeismicCases(inputModel, level: 2);
            bool level2UsesDamageLimit = seismicGrade == "S";

            foreach (var pile in inputModel.PileLayoutItems)
            {
                var soilPile = SoilPileFor(inputModel, pile);
                if (soilPile == null) continue;

                AddPileItems(items, pile, soilPile, level1Cases, level2Cases, level2UsesDamageLimit);
            }

            return items;
        }

        /// <summary>
        /// 杭 1 本ぶんの検定項目。<see cref="Evaluate"/> から切り出してあるのは、
        /// 限界状態の割り当てを <see cref="InputModel"/> を組み立てずに試験できるようにするため。
        /// </summary>
        public static void AddPileItems(List<EvaluationItem> items,
            PileLayoutDataItem pile, SoilPile soilPile,
            IReadOnlyList<(string Name, int No)> level1Cases,
            IReadOnlyList<(string Name, int No)> level2Cases,
            bool level2UsesDamageLimit)
        {
            // 長期 (使用限界)
            AddIfAvailable(items, pile,
                level: 0, limitName: "使用限界", loadCaseName: "長期 (VL)",
                axialForce: pile.AxialForceVL,
                compressionLimit: soilPile.R_SLS, upliftLimit: soilPile.Rt_SLS);

            // レベル1 (損傷限界)
            foreach (var (name, no) in level1Cases)
            {
                AddIfAvailable(items, pile,
                    level: 1, limitName: "損傷限界", loadCaseName: name,
                    axialForce: pile.GetDesignAxialForce(no, 1),
                    compressionLimit: soilPile.R_DLS, upliftLimit: soilPile.Rt_DLS);
            }

            // レベル2 (終局限界。耐震グレード S では損傷限界)
            foreach (var (name, no) in level2Cases)
            {
                AddIfAvailable(items, pile,
                    level: 2,
                    limitName: level2UsesDamageLimit ? "損傷限界" : "終局限界",
                    loadCaseName: name,
                    axialForce: pile.GetDesignAxialForce(no, 2),
                    compressionLimit: level2UsesDamageLimit ? soilPile.R_DLS : soilPile.R_ULS,
                    upliftLimit: level2UsesDamageLimit ? soilPile.Rt_DLS : soilPile.Rt_ULS);
            }
        }

        /// <summary>
        /// この杭の地盤条件の土層-杭セット。(地盤No, 杭体No, 杭頭Z) で引く (<see cref="PileLayoutDataItem.SoilPile"/> と同じ対応)。
        ///
        /// 支持力は地盤と杭頭の高さで決まる。以前は杭体 No だけで引き、同じ杭体 No のセットが複数あると
        /// 最初の 1 つを使ったので、地盤や杭頭の高さが違う杭に別の杭の支持力を当てていた。
        /// 検定する入力 (<paramref name="inputModel"/>) の要素分割から引き、無ければ杭自身の条件で組み立てたものを使う
        /// (<see cref="PileLayoutDataItem.SoilPile"/> の既定の動き)。
        /// </summary>
        internal static SoilPile? SoilPileFor(InputModel inputModel, PileLayoutDataItem pile)
            => inputModel.LookupSoilPile(pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ) ?? pile.SoilPile;

        /// <summary>解析対象の地震時荷重ケース (表示名, ケース番号)。</summary>
        private static IReadOnlyList<(string Name, int No)> SeismicCases(InputModel inputModel, int level)
        {
            var cases = inputModel.LoadCasesInput?.AnalysisTargetSeismicLoadCases;
            if (cases == null) return [];

            // 荷重ケース名は未入力のことがある。空欄だと表で行を区別できないので番号で补う。
            return cases.Where(c => c.Level == level)
                        .Select(c => (string.IsNullOrWhiteSpace(c.LoadName)
                                          ? $"レベル{level} ケース{c.No}"
                                          : c.LoadName, c.No))
                        .ToList();
        }

        /// <summary>
        /// 軸力の向きに応じて、押込みか引抜きの<b>どちらか一方</b>を足す。
        /// 両方出すと、圧縮の杭に「引抜きは OK」という無意味な行が並ぶ。
        ///
        /// 押込みの限界値は<b>正値</b>、引抜き抵抗は<b>負値</b>で保持されている
        /// (<see cref="SoilPile.CalculateResistances"/> 参照) ので、符号を確かめてから大きさで比べる。
        /// 限界値が 0 のときは検定できない (引抜き抵抗を計算していない杭など) ので出さない。
        /// 限界値が数でない・無限大・符号が逆のときは「検定不能」として出す。以前は大きさを取ってから
        /// 0 より大きければ採用したので、無限大の限界値では有限の軸力がすべて合格になり、
        /// 符号の逆な限界値もそのまま使われた。
        /// </summary>
        private static void AddIfAvailable(List<EvaluationItem> items, PileLayoutDataItem pile,
            int level, string limitName, string loadCaseName,
            double axialForce, double compressionLimit, double upliftLimit)
        {
            if (!double.IsFinite(axialForce)) return;

            bool isCompression = axialForce >= 0;

            double response = Math.Abs(axialForce);
            double signedLimit = isCompression ? compressionLimit : upliftLimit;
            if (signedLimit == 0) return;
            string? unavailable = DescribeInvalidLimit(signedLimit, isCompression);
            double limit = unavailable == null ? Math.Abs(signedLimit) : double.NaN;

            items.Add(new EvaluationItem
            {
                Kind = isCompression
                    ? EvaluationKind.PileBearingCompression
                    : EvaluationKind.PileUpliftResistance,
                Level = level,
                Category = isCompression
                    ? $"押込み支持力 ({limitName})"
                    : $"引抜き抵抗 ({limitName})",
                LimitName = limitName,
                TargetName = $"Pile-{pile.PileNo}",
                // 杭配置番号は PileNo に持つ。以前は PileBodyNo に入れていたため、
                // 杭ごとに畳む処理 (PileEvaluationSummary) が支持力の行を杭に結び付けられなかった。
                PileNo = pile.PileNo,
                PileBodyNo = pile.PileBodyNo,
                LoadCaseName = loadCaseName,
                Response = response,
                Limit = limit,
                Unit = Unit,
                AxialForce = axialForce,   // 符号付き。圧縮が正
                UnavailableReason = unavailable,
                IsOk = unavailable == null && !(response > limit),
            });
        }

        /// <summary>
        /// 限界値が検定に使えないときの理由 (使えるなら null)。押込みは正、引抜きは負が規約。
        /// </summary>
        internal static string? DescribeInvalidLimit(double signedLimit, bool isCompression)
        {
            string what = isCompression ? "押込みの支持力" : "引抜き抵抗";
            if (!double.IsFinite(signedLimit))
                return $"{what}が数値として求まっていません ({signedLimit})。地盤・杭体の入力を確認してください";
            if (isCompression ? signedLimit < 0 : signedLimit > 0)
                return $"{what}の符号が逆です ({signedLimit:0.0} kN)。地盤・杭体の入力を確認してください";
            return null;
        }
    }
}
