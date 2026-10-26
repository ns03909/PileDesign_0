using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 杭の地震時の入力軸力を引く窓口。解析・検定・図・計算書・支持力・沈下は、杭の軸力をここから引く。
    ///
    /// <para>分離入力 (<see cref="InputModel.IsAxialForceSeparated"/>) でなければ、従来どおり荷重ケースごとに 1 つの軸力
    /// (<see cref="PileLayoutDataItem.GetSeismicAxialForce"/> など) をそのまま返す。値も判定も従来と同じ。</para>
    ///
    /// <para>分離入力では、荷重組合せごとに N = VL + βL·ΔL(L) + βU·ΔL(U) で求める。組合せが要るので、
    /// 組合せを受け取るメソッドに null を渡すと例外にする (黙って別の組合せの軸力を使わない)。
    /// 組合せをまたいで 1 つに決める所 (支持力・沈下・Chang・MGT) は <see cref="Envelope"/> を使う。</para>
    ///
    /// <para>杭のメソッド (GetSeismicAxialForce / GetDesignAxialForce / ResolveDesignAxialForce /
    /// GetSeparatedSeismicAxialForce) を直接呼ぶと分離入力を読み落とす。直接の呼び出しは
    /// SeparatedAxialForceTests が見張っている。</para>
    /// </summary>
    public static class SeismicAxialForces
    {
        /// <summary>分離入力か。</summary>
        public static bool IsSeparated(InputModel? input) => input?.IsAxialForceSeparated == true;

        /// <summary>
        /// 解析に与える地震時の入力軸力 [kN]。レベル 0 (常時) は VL。
        /// 分離入力でなければ <see cref="PileLayoutDataItem.GetSeismicAxialForce"/> と同じ (範囲外は ArgumentOutOfRangeException)。
        /// </summary>
        /// <param name="combination">荷重組合せ。分離入力のレベル 1・2 では必須。</param>
        public static double Input(InputModel? input, PileLayoutDataItem pile, int loadCaseNo, int level, LoadCombination? combination)
        {
            if (!IsSeparated(input) || level == 0)
                return pile.GetSeismicAxialForce(loadCaseNo, level);
            var c = RequireCombination(combination, loadCaseNo, level);
            return pile.GetSeparatedSeismicAxialForce(loadCaseNo, level, c.Beta1, c.Beta2);
        }

        /// <summary>
        /// 検定・図・計算書に使う設計軸力 [kN] と、それがどの入力か (検定の根拠に書く)。
        /// 分離入力でなければ <see cref="PileLayoutDataItem.ResolveDesignAxialForce"/> と同じ
        /// (地震時軸力が未入力 (0) なら常時軸力)。
        /// 分離入力では合成した値をそのまま使う (ΔL(U)・ΔL(L) が 0 なら自然に VL になり、合成した結果が 0 でも「未入力」とは読まない)。
        /// </summary>
        public static (double N, string Source) Design(
            InputModel? input, PileLayoutDataItem pile, int loadCaseNo, int level, LoadCombination? combination, bool reportFallback = true)
        {
            if (!IsSeparated(input) || level == 0)
                return pile.ResolveDesignAxialForce(loadCaseNo, level, reportFallback);
            var c = RequireCombination(combination, loadCaseNo, level);
            try
            {
                double n = pile.GetSeparatedSeismicAxialForce(loadCaseNo, level, c.Beta1, c.Beta2);
                return (n, $"杭配置の地震時軸力 (分離入力: VL + βL·ΔL{level}-{loadCaseNo}(L) + βU·ΔL{level}-{loadCaseNo}(U)、"
                    + $"βU={c.Beta1:0.##}・βL={c.Beta2:0.##})");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                if (reportFallback)
                    PileDesign.Common.CalcFallbackTracker.Report(
                        "設計軸力（常時軸力で継続）", ex, $"荷重ケース={loadCaseNo}, レベル={level}, 分離入力");
                return (pile.AxialForceVL, "杭配置の常時軸力 (その荷重ケースの地震時軸力の欄がないため)");
            }
        }

        /// <summary>
        /// 組合せをまたいで 1 つに決める所 (支持力・沈下・Chang・MGT) のための、荷重ケースの軸力の最大・最小 [kN] と、
        /// それを与える組合せ。
        /// 分離入力でなければ最大・最小とも従来の設計軸力 (<see cref="Design"/>。組合せは null)。
        /// 分離入力では、解析の対象になる組合せ (<see cref="LoadCasesInput.AllLoadCombinations"/>) すべての <see cref="Design"/>。
        /// 組合せが 1 つも無ければ常時軸力。
        /// </summary>
        public static AxialForceEnvelope Envelope(InputModel? input, PileLayoutDataItem pile, int loadCaseNo, int level, bool reportFallback = true)
        {
            if (!IsSeparated(input) || level == 0)
            {
                var (n, source) = Design(input, pile, loadCaseNo, level, null, reportFallback);
                return new AxialForceEnvelope(n, null, source, n, null, source);
            }
            var combos = input!.LoadCasesInput?.AllLoadCombinations?.Where(c => c != null).ToList() ?? [];
            if (combos.Count == 0)
            {
                const string none = "杭配置の常時軸力 (分離入力で、適用する荷重組合せがないため)";
                return new AxialForceEnvelope(pile.AxialForceVL, null, none, pile.AxialForceVL, null, none);
            }
            (double N, LoadCombination C, string S)? max = null, min = null;
            foreach (var c in combos)
            {
                var (n, s) = Design(input, pile, loadCaseNo, level, c, reportFallback);
                if (max == null || n > max.Value.N) max = (n, c, s);
                if (min == null || n < min.Value.N) min = (n, c, s);
            }
            return new AxialForceEnvelope(max!.Value.N, max.Value.C, max.Value.S, min!.Value.N, min.Value.C, min.Value.S);
        }

        /// <summary>
        /// 杭配置の荷重ケースの軸力を組合せごとに並べる (図・表で組合せの違いを見せるため)。
        /// 分離入力でなければ組合せは null の 1 件。
        /// </summary>
        public static IReadOnlyList<(LoadCombination? Combination, double N)> PerCombination(
            InputModel? input, PileLayoutDataItem pile, int loadCaseNo, int level)
        {
            if (!IsSeparated(input) || level == 0)
                return [(null, Design(input, pile, loadCaseNo, level, null, reportFallback: false).N)];
            return (input!.LoadCasesInput?.AllLoadCombinations?.Where(c => c != null) ?? [])
                .Select(c => ((LoadCombination?)c, Design(input, pile, loadCaseNo, level, c, reportFallback: false).N))
                .ToList();
        }

        /// <summary>
        /// 杭の軸力を荷重にして鉛直に解く計算 (基礎梁考慮の群杭沈下・単杭沈下 (基礎梁考慮)) で、荷重ケースごとに解く組合せ。
        /// 全杭の軸力を 1 つの荷重状態として解くので、杭ごとに最大を拾うと実在しない荷重状態になる。分離入力では組合せごとに解く。
        /// βU・βL が同じ組合せ (軸力も同じ) は 1 つにまとめる。分離入力でなければ null の 1 件 (従来どおりケースごとに 1 回)。
        /// </summary>
        public static IReadOnlyList<LoadCombination?> CombinationsForVerticalAnalysis(InputModel? input)
        {
            if (!IsSeparated(input)) return [null];
            var list = new List<LoadCombination?>();
            foreach (var c in input!.LoadCasesInput?.AllLoadCombinations ?? [])
                if (c != null && !list.Any(x => x!.Beta1 == c.Beta1 && x.Beta2 == c.Beta2))
                    list.Add(c);
            return list.Count > 0 ? list : [null];
        }

        /// <summary>結果の見出しに添える組合せの名前 (分離入力のとき)。組合せが null なら空。</summary>
        public static string CombinationSuffix(LoadCombination? combination)
            => combination == null ? "" : CombinationSuffix(combination.No, combination.Beta1, combination.Beta2);

        /// <summary>結果の見出しに添える組合せの名前 (番号と係数から)。</summary>
        public static string CombinationSuffix(int combinationNo, double betaU, double betaL)
            => $" / 組合せNo.{combinationNo} (βU={betaU:0.##}, βL={betaL:0.##})";

        /// <summary>
        /// 鉛直に解く計算で、荷重ケース (と分離入力のときは組合せ) の杭の軸力 [kN]。
        /// 組合せが null なら従来どおり AxialForceLevel1s/2s をそのまま読む (欄が無ければ VL。0 も 0 のまま)。
        /// </summary>
        public static double ForVerticalAnalysis(InputModel? input, PileLayoutDataItem pile, int loadCaseNo, int level, LoadCombination? combination)
        {
            if (combination == null || !IsSeparated(input))
            {
                var forces = level == 1 ? pile.AxialForceLevel1s : pile.AxialForceLevel2s;
                int idx = loadCaseNo - 1;
                return forces != null && idx >= 0 && idx < forces.Count ? forces[idx] : pile.AxialForceVL;
            }
            try { return pile.GetSeparatedSeismicAxialForce(loadCaseNo, level, combination.Beta1, combination.Beta2); }
            catch (ArgumentOutOfRangeException ex)
            {
                // 欄が無いケースは常時軸力 (分離入力でないときの従来の扱いと同じ)。黙って落とさず記録する
                PileDesign.Common.CalcFallbackTracker.Report(
                    "地震時軸力（常時軸力で継続）", ex, $"荷重ケース={loadCaseNo}, レベル={level}, 分離入力");
                return pile.AxialForceVL;
            }
        }

        /// <summary>
        /// 杭ごと・荷重ケースごとに 1 つの値を持つ所 (単杭沈下・沈下の図・結果表) に使う、レベルの各荷重ケースの軸力 [kN]。
        /// 分離入力でなければ AxialForceLevel1s/2s そのもの (従来どおり)。
        /// 分離入力では各ケースの組合せのうち最大 (圧縮が最大 = 沈下が最も大きくなる側。<see cref="Envelope"/> の Max)。
        /// </summary>
        public static IReadOnlyList<double> PerCaseForSettlement(InputModel? input, PileLayoutDataItem pile, int level)
        {
            var legacy = level == 1 ? pile.AxialForceLevel1s : pile.AxialForceLevel2s;
            if (!IsSeparated(input)) return legacy ?? (IReadOnlyList<double>)[];
            int count = pile.AxialForceUpperOf(level)?.Count ?? 0;
            return Enumerable.Range(1, count).Select(no => Envelope(input, pile, no, level, reportFallback: false).Max).ToList();
        }

        /// <summary>
        /// 組合せの単杭沈下の結果 (分離入力のとき)。係数 βU・βL で照合する (同じ係数の組合せは 1 件にまとめて求めているため)。
        /// 組合せが null・見つからなければ null。
        /// </summary>
        public static SeparatedSettlementResult? FindSeparatedSettlement(PileLayoutDataItem pile, int level, int caseNo, LoadCombination? combination)
            => combination == null ? null
               : pile.SinglePileSettlementsSeparated?.FirstOrDefault(r => r.Level == level && r.CaseNo == caseNo
                    && r.BetaU == combination.Beta1 && r.BetaL == combination.Beta2);

        /// <summary>
        /// 沈下の図に出す、選んだ荷重ケース・組合せの杭の軸力 [kN]。
        /// 分離入力で組合せが選ばれていればその組合せの軸力、そうでなければ単杭沈下のケースごとの欄と同じ
        /// (<see cref="PerCaseForSettlement"/>。分離入力でなければ AxialForceLevel1s/2s そのもの)。
        /// </summary>
        public static double? ForSettlementDisplay(InputModel? input, PileLayoutDataItem pile, int caseNo, int level, LoadCombination? selectedCombination)
        {
            if (IsSeparated(input) && selectedCombination != null)
                return ForVerticalAnalysis(input, pile, caseNo, level, selectedCombination);
            var forces = PerCaseForSettlement(input, pile, level);
            return caseNo >= 1 && caseNo <= forces.Count ? forces[caseNo - 1] : null;
        }

        private static LoadCombination RequireCombination(LoadCombination? combination, int loadCaseNo, int level)
            => combination ?? throw new InvalidOperationException(
                $"地震時軸力の分離入力では荷重組合せが要ります (レベル{level}・荷重ケース No.{loadCaseNo})。"
                + "組合せをまたぐ所は SeismicAxialForces.Envelope を使ってください。");
    }

    /// <summary>荷重ケースの軸力の最大・最小 (組合せをまたいで 1 つに決めるため)。組合せは分離入力でなければ null。</summary>
    public readonly record struct AxialForceEnvelope(
        double Max, LoadCombination? MaxCombination, string MaxSource,
        double Min, LoadCombination? MinCombination, string MinSource);
}
