using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭 1 本の「単杭沈下 + 群杭沈下」を組む<b>唯一の場所</b>。
    ///
    /// <para>以前は 4 か所 (沈下量の検定・杭頭変形角の検定・計算書の各杭位置の沈下量一覧・沈下グラフ) と
    /// 画面の沈下表示が、それぞれ自前で単杭沈下と群杭沈下を足していた。組み方が少しずつ違っていた。
    /// <list type="bullet">
    /// <item>検定 2 つは群杭沈下の<b>常時 (VL) の記録</b>を使い、基本設定の「沈下検討の対象」を見て、
    ///   記録が無ければ検定不能にしていた。</item>
    /// <item>計算書の一覧表は<b>画面で表示中のケース</b> (地震時のこともある) の値を足し、「沈下検討の対象」も見ていなかった。
    ///   同じ計算書の中で、沈下量の検定の値と一覧表の合計沈下量が食い違うことがあった。</item>
    /// <item>沈下グラフ・画面の表示は表示中のケースを足し、群杭沈下の値が無い杭を<b>黙って 0</b> として描いていた。</item>
    /// </list>
    /// 足すべき群杭沈下をどの記録から取るか・無いときにどう扱うかを、ここに 1 つにまとめる。
    /// 単位も揃える: 単杭沈下は m、群杭沈下は mm で持たれている。</para>
    ///
    /// <para>記録の選び方は 2 通り。
    /// <list type="bullet">
    /// <item><b>設計値</b> (検定・計算書の一覧表): <see cref="LongTermRecords"/> (常時の記録) を
    ///   基本設定の「沈下検討の対象」 (<see cref="IncludesGroup"/>) に従って足す。</item>
    /// <item><b>ケースごとの表示</b> (グラフ・画面): 呼び出し側が明示した記録 (表示中のケース) を足し、
    ///   どの記録を使ったかを示す。</item>
    /// </list></para>
    /// </summary>
    public static class PileSettlementComposition
    {
        /// <summary>
        /// 基本設定の「沈下検討の対象」が「単杭＋群杭沈下」か (基本設定が無ければ既定の「単杭＋群杭沈下」)。
        /// </summary>
        public static bool IncludesGroup(InputModel? input)
            => input?.FundamentalInput?.SettlementDesignIncludesGroup ?? true;

        /// <summary>
        /// 群杭沈下の常時 (VL) の記録 (杭ごとの沈下量を持つもの)。基本設定に関わらず返す。
        /// 基礎梁なし・ありの 2 つがありうる。
        /// </summary>
        public static List<GroupSettlementCaseRecord> LongTermRecords(InputModel? input)
            => input?.PileGroupSettlement?.CaseRecords?
                .Where(r => r?.PileSettlements_mm is { Count: > 0 } && r.IsLongTerm).ToList() ?? [];

        /// <summary>
        /// 設計値に足す群杭沈下の記録。「沈下検討の対象」が単杭沈下だけなら空。
        /// </summary>
        public static List<GroupSettlementCaseRecord> DesignGroupRecords(InputModel? input)
            => IncludesGroup(input) ? LongTermRecords(input) : [];

        /// <summary>
        /// 画面で表示中の群杭沈下のケース (グラフ・画面の表示がケースごとに足す記録)。無ければ null。
        /// </summary>
        public static GroupSettlementCaseRecord? DisplayedGroupRecord(InputModel? input)
            => input?.PileGroupSettlement?.ActiveRecord;

        /// <summary>
        /// 杭 1 本の単杭沈下と群杭沈下を組む。
        /// </summary>
        /// <param name="input">結果を持つ入力 (単杭沈下の荷重-沈下曲線・荷重ケースの並びを引く)。</param>
        /// <param name="pile">杭。</param>
        /// <param name="groupRecord">足す群杭沈下の記録。<paramref name="includeGroup"/> が true で null なら「群杭沈下の結果なし」。</param>
        /// <param name="includeGroup">群杭沈下を足すか。false なら単杭沈下だけ (群杭ぶんは 0、理由なし)。</param>
        /// <param name="singleCase">単杭沈下の荷重ケース名。null または "VL" で常時。</param>
        public static PileSettlementParts Compose(InputModel input, PileLayoutDataItem pile,
            GroupSettlementCaseRecord? groupRecord, bool includeGroup, string? singleCase = null)
        {
            var (found, single_m, beyond) = SingleOfCase(input, pile, singleCase);
            // 単杭沈下量は解析していなくても 0 を持つ。値ではなく荷重-沈下曲線があるかで見る
            bool hasSingle = found && SettlementDeformationAngleEvaluator.HasSinglePileSettlement(input, pile);

            bool hasGroup = false;
            double group_mm = 0.0;
            string? groupReason = null;
            if (includeGroup)
            {
                if (groupRecord == null)
                    groupReason = SettlementDeformationAngleEvaluator.NoGroupResultReason;
                else
                {
                    hasGroup = groupRecord.PileSettlements_mm?.TryGetValue(pile.PileNo, out group_mm) == true;
                    if (!hasGroup) group_mm = 0.0;
                    if (groupRecord.IsConvergenceUnknown)
                        groupReason = LegacySettlementMigration.UnknownConvergenceReason;   // 旧形式から復元 (収束状態が不明)
                    else if (!hasGroup)
                        groupReason = $"群杭沈下 ({CaseNameOf(groupRecord)}) の結果に杭No.{pile.PileNo} がありません (群杭沈下解析をやり直してください)";
                }
            }

            return new PileSettlementParts
            {
                PileNo = pile.PileNo,
                HasSingle = hasSingle,
                Single_m = single_m,
                SingleBeyondCurve = beyond,
                GroupIncluded = includeGroup,
                GroupRecord = includeGroup ? groupRecord : null,
                HasGroup = hasGroup,
                Group_mm = group_mm,
                GroupReason = groupReason,
            };
        }

        /// <summary>
        /// 杭 1 本の群杭沈下 [mm] だけ (単杭沈下は足さない)。記録が無い・記録にその杭が無いときは null
        /// (以前の <c>SettlementOf</c> は 0 を返し、未計算と区別できなかった)。
        /// </summary>
        public static double? GroupOnly_mm(GroupSettlementCaseRecord? groupRecord, int pileNo)
            => groupRecord?.PileSettlements_mm != null && groupRecord.PileSettlements_mm.TryGetValue(pileNo, out double v)
                ? v : null;

        /// <summary>
        /// 基礎梁を考慮した単杭沈下 [mm] と群杭沈下 [mm] の和。どちらかが無い杭は null (0 として足さない)。
        /// 画面の「基礎梁考慮+群杭沈下」の吹き出しが使う。以前は杭頭の沈下 (基礎梁考慮) が無い杭を 0、
        /// 群杭沈下の値が無い杭も 0 (SettlementOf) として足し、値の無い杭に小さな沈下量を描いていた。
        /// </summary>
        public static double? VerticalBeamPlusGroup_mm(double? verticalBeam_mm, GroupSettlementCaseRecord? groupRecord, int pileNo)
            => verticalBeam_mm is double vb && double.IsFinite(vb) && GroupOnly_mm(groupRecord, pileNo) is double g
                ? vb + g : null;

        /// <summary>記録の呼び名 (常時の記録は「常時」)。理由の文に使う。</summary>
        private static string CaseNameOf(GroupSettlementCaseRecord record)
            => record.IsLongTerm ? "常時"
             : string.IsNullOrWhiteSpace(record.LoadCaseName) ? "ケース名なし" : record.LoadCaseName.Trim();

        /// <summary>
        /// 荷重ケースの単杭沈下量 [m] と、曲線の範囲外 (下限) の印。ケースが見つからなければ found = false。
        /// レベル1 → レベル2 の順に、名前が最初に一致したものを使う。
        /// </summary>
        private static (bool Found, double Value_m, bool BeyondCurve) SingleOfCase(InputModel input, PileLayoutDataItem pile, string? singleCase)
        {
            if (singleCase == null || singleCase == "VL")
                return (true, pile.SinglePileSettlementVL, pile.SinglePileSettlementVLBeyondCurve);

            var cases = input.LoadCasesInput;
            if (cases?.LoadCasesLevel1 != null)
                for (int i = 0; i < cases.LoadCasesLevel1.Count; i++)
                    if (cases.LoadCasesLevel1[i].LoadName == singleCase)
                        return At(pile.SinglePileSettlementLevel1s, pile.SinglePileSettlementLevel1sBeyondCurve, i);
            if (cases?.LoadCasesLevel2 != null)
                for (int i = 0; i < cases.LoadCasesLevel2.Count; i++)
                    if (cases.LoadCasesLevel2[i].LoadName == singleCase)
                        return At(pile.SinglePileSettlementLevel2s, pile.SinglePileSettlementLevel2sBeyondCurve, i);
            return (false, 0.0, false);

            static (bool, double, bool) At(IList<double>? values, IList<bool>? flags, int i)
                => values != null && i < values.Count
                    ? (true, values[i], PileLayoutDataItem.BeyondAt(flags, i))
                    : (false, 0.0, false);   // 以前は並びが短いと添字の範囲外で落ちた
        }
    }

    /// <summary>
    /// 杭 1 本の単杭沈下・群杭沈下とその合計 (<see cref="PileSettlementComposition.Compose"/> の結果)。
    /// </summary>
    public sealed record PileSettlementParts
    {
        public int PileNo { get; init; }

        /// <summary>単杭沈下の結果 (荷重-沈下曲線とそのケースの値) があるか。</summary>
        public bool HasSingle { get; init; }

        /// <summary>単杭沈下量 [m] (保存されている値そのまま。<see cref="HasSingle"/> が false なら意味がない)。</summary>
        public double Single_m { get; init; }

        /// <summary>単杭沈下量が荷重-沈下曲線の範囲外の軸力で求めた値か (曲線の端の値 = 下限)。</summary>
        public bool SingleBeyondCurve { get; init; }

        /// <summary>群杭沈下を足す組み方か。</summary>
        public bool GroupIncluded { get; init; }

        /// <summary>足した群杭沈下の記録 (足さないなら null)。</summary>
        public GroupSettlementCaseRecord? GroupRecord { get; init; }

        /// <summary>記録にこの杭の群杭沈下量があるか。</summary>
        public bool HasGroup { get; init; }

        /// <summary>群杭沈下量 [mm]。無いとき・足さないときは 0 (合計には使わないこと。<see cref="Total_mm"/> を使う)。</summary>
        public double Group_mm { get; init; }

        /// <summary>
        /// 群杭ぶんを使えない理由 (結果なし・記録にこの杭が無い・収束状態が不明)。足さない組み方なら null。
        /// </summary>
        public string? GroupReason { get; init; }

        /// <summary>基礎梁ありの反復が収束しなかった記録の値か (釣り合っていない沈下)。</summary>
        public bool GroupUnconverged => GroupRecord is { IsBeamAware: true, IsConverged: false };

        /// <summary>単杭沈下量 [mm]。</summary>
        public double Single_mm => Single_m * 1000.0;

        /// <summary>
        /// 合計 [mm] を欠けの有無を問わずに足した値 (欠けた群杭ぶんは 0)。検定が以前と同じ演算順で応答値を作るためのもの。
        /// 表示には <see cref="Total_mm"/> を使うこと。
        /// </summary>
        public double Sum_mm => Single_mm + Group_mm;

        /// <summary>合計 [m] (杭頭変形角が使う単位。演算は以前の変形角の検定と同じ)。</summary>
        public double Sum_m => GroupIncluded ? Single_m + Group_mm * 1e-3 : Single_m;

        /// <summary>
        /// 合計 [mm]。単杭沈下の結果が無い・足すべき群杭ぶんが無い・有限でないなら null (0 にしない)。
        /// 収束状態が不明・未収束の記録でも値は返す (表示の側で注記する)。
        /// 欠けの扱いは計算書の表の値 (<see cref="PileSettlementTableValues"/>) と同じものを使う。
        /// </summary>
        public double? Total_mm => GroupIncluded ? Table.TotalMm : Table.SingleMm;

        /// <summary>群杭ぶんの値 [mm] (無ければ・足さない組み方なら null)。</summary>
        public double? GroupOrNull_mm => Table.GroupMm;

        /// <summary>単杭ぶんの値 [mm] (無ければ null)。</summary>
        public double? SingleOrNull_mm => Table.SingleMm;

        private (double? SingleMm, double? GroupMm, double? TotalMm) Table
            => PileSettlementTableValues.Build(HasSingle, Single_m, HasGroup, Group_mm);
    }
}
