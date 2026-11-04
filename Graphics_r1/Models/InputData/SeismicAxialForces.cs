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
                // 数でない軸力の組合せが 1 つでもあれば、最大・最小ともその値 (NaN) にして組合せを示す。
                // 大小の比較だけだと、途中の組合せの NaN は比較がどちらも偽で包絡から消え、残りの組合せだけで
                // 判定していた (先頭の組合せの NaN だけは残っていた)。使う側 (支持力の検定) は NaN を「検定不能」にする
                // (2026-11-04 外部レビュー)
                if (!double.IsFinite(n))
                {
                    string bad = s + $" (組合せ No.{c.No} の軸力が数ではありません)";
                    return new AxialForceEnvelope(double.NaN, c, bad, double.NaN, c, bad);
                }
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
        ///
        /// <para>分離入力で適用する組合せが無ければ<b>空</b> (地震時のケースは解けない)。以前は null の 1 件を返し、
        /// null の組合せは分けない入力 (AxialForceLevel1s/2s) を読む約束なので、画面で使っていない古い値を荷重にして
        /// 解いていた (2026-11-04 外部レビュー)。解く画面は <see cref="NoApplicableCombinationMessage"/> で先に止める。</para>
        /// </summary>
        public static IReadOnlyList<LoadCombination?> CombinationsForVerticalAnalysis(InputModel? input)
        {
            if (!IsSeparated(input)) return [null];
            var list = new List<LoadCombination?>();
            foreach (var c in input!.LoadCasesInput?.AllLoadCombinations ?? [])
                if (c != null && !list.Any(x => x!.Beta1 == c.Beta1 && x.Beta2 == c.Beta2))
                    list.Add(c);
            return list;
        }

        /// <summary>分離入力で適用する荷重組合せが無く、地震時の軸力を決められないときの、検定の項目の理由。</summary>
        public const string NoApplicableCombinationReason =
            "地震時軸力の分離入力で、適用する荷重組合せがないため、地震時の軸力を決められません。荷重組合せの「適用」を 1 つ以上入れてください";

        /// <summary>
        /// 地震時のケースを鉛直に解けない理由 (分離入力で、適用する荷重組合せが 1 つも無い)。解けるなら null。
        /// </summary>
        public static string? NoApplicableCombinationMessage(InputModel? input)
            => IsSeparated(input) && CombinationsForVerticalAnalysis(input).Count == 0
                ? "地震時軸力の分離入力で、適用する荷重組合せがありません (地震時の軸力を決められません)。\n"
                  + "リボンの「荷重」で荷重組合せの「適用」を 1 つ以上入れるか、地震時のケース (レベル1・レベル2) のチェックを外してください。"
                : null;

        /// <summary>結果の見出しに添える組合せの名前 (分離入力のとき)。組合せが null なら空。</summary>
        public static string CombinationSuffix(LoadCombination? combination)
            => combination == null ? "" : CombinationSuffix(combination.No, combination.Beta1, combination.Beta2);

        /// <summary>
        /// 結果の見出しに添える組合せの名前 (番号と係数から)。
        ///
        /// <para>この文字列は表示だけでなく、結果を組合せと照合するのにも使う (<see cref="SeismicCaseLabels"/>・単杭沈下と
        /// 群杭沈下の組み合わせ)。係数は<b>丸めずに</b>書く (往復できる最短の表記。1・-1・0.5 などは以前と同じ見た目)。
        /// 以前は小数 2 桁に丸めていたので、解析後に βU を 1.001 から 1.004 に変えても見出しが同じ「βU=1」になり、
        /// 古い結果を新しい条件の結果として選んだり、別の係数で求めた単杭沈下と群杭沈下を足したりしえた
        /// (2026-11-04 外部レビュー)。</para>
        /// </summary>
        public static string CombinationSuffix(int combinationNo, double betaU, double betaL)
            => $" / 組合せNo.{combinationNo} (βU={Exact(betaU)}, βL={Exact(betaL)})";

        private static string Exact(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>
    /// 鉛直に解く計算 (基礎梁考慮の群杭沈下・単杭沈下 (基礎梁考慮)) の結果の見出しと、画面で選んでいる荷重ケース・組合せの照合。
    ///
    /// <para>見出しは「L1-1: U1」「1-1: U1」「VL (常時+追加)」の形で、分離入力では
    /// 「L1-1: U1 / 組合せNo.2 (βU=1, βL=-1)」と組合せを添える (<see cref="SeismicAxialForces.CombinationSuffix(LoadCombination?)"/>)。
    /// βU・βL の同じ組合せはまとめて解くので、結果は代表 (先頭) の組合せの番号を持つ。照合は代表に読み替えてから行う。</para>
    ///
    /// <para>以前は見出しからケース名を取り出す関数が画面 (MainWindow.CanvasResultsVertical) と ViewModel に写しで 2 つ、
    /// 「末尾が ": ケース名"」で探す所が群杭沈下の記録とグラフに 2 つあり、組合せを添えた見出しを片方だけ直して
    /// ほかは先頭の結果 (常時) へ戻っていた。照合はここだけで行う (SeparatedAxialForceTests が写しを見張る)。</para>
    /// </summary>
    public static class SeismicCaseLabels
    {
        /// <summary>
        /// 見出しの末尾の組合せの部分 (<see cref="SeismicAxialForces.CombinationSuffix(int, double, double)"/> の形そのもの)。
        /// 以前は最初の「 / 組合せ」から後ろを組合せとしていたので、荷重ケース名にその文字があると名前の途中で切れ、
        /// 結果を正しいケースと照合できなかった (2026-11-04 外部レビュー)。末尾にある決まった形だけを組合せとする。
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex CombinationAtEnd =
            new(@" / 組合せNo\.\d+ \(βU=[^,()]*, βL=[^,()]*\)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>常時の鉛直の結果の見出しに付ける補足 ("VL (常時+追加)")。見出しを作る側もこれを使う。</summary>
        public const string LongTermDecoration = " (常時+追加)";

        /// <summary>地震時の結果の見出しの前置き ("1-1: " / "L1-1: ")。後ろが荷重ケース名そのもの。</summary>
        private static readonly System.Text.RegularExpressions.Regex CasePrefix =
            new(@"^L?\d+-\d+: ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>見出しの組合せの部分 (" / 組合せNo.…")。無ければ空。</summary>
        public static string CombinationPartOf(string? label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            var m = CombinationAtEnd.Match(label);
            return m.Success ? label[m.Index..] : "";
        }

        /// <summary>見出しから組合せの部分を外したもの。</summary>
        public static string WithoutCombination(string? label)
        {
            if (string.IsNullOrEmpty(label)) return label ?? "";
            var m = CombinationAtEnd.Match(label);
            return m.Success ? label[..m.Index] : label;
        }

        /// <summary>
        /// 見出しのケース名 ("1-1: U1 / 組合せ…" → "U1"、"VL (常時+追加)" → "VL")。
        ///
        /// <para>前置きの後ろはケース名を丸ごと返す。括弧は常時の補足 (<see cref="LongTermDecoration"/>) だけを外す。
        /// 以前は最初の " (" から後ろを捨てていたので、重複を直したケース名 "U1 (L2-1)" (<c>LoadCasesInput</c>) が
        /// "U1" になり、選んでいるケースの結果を見つけられなかった (2026-11-04 外部レビュー)。</para>
        /// </summary>
        public static string BaseCaseName(string? label)
        {
            string s = WithoutCombination(label);
            var prefix = CasePrefix.Match(s);
            if (prefix.Success) return s[prefix.Length..];
            return s.EndsWith(LongTermDecoration, StringComparison.Ordinal) ? s[..^LongTermDecoration.Length] : s;
        }

        /// <summary>見出しがこの荷重ケース名の結果か (厳密一致か、組合せを外して末尾が ": ケース名")。</summary>
        public static bool RefersToCase(string? label, string? caseName)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(caseName)) return false;
            if (label == caseName) return true;
            return WithoutCombination(label).EndsWith(": " + caseName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 選んでいる組合せの結果の見出しに付く組合せの部分。分離入力でない・組合せを選んでいないときは空。
        /// βU・βL の同じ組合せは代表 (<see cref="SeismicAxialForces.CombinationsForVerticalAnalysis"/> の先頭) に読み替える。
        /// </summary>
        public static string SuffixFor(InputModel? input, LoadCombination? selected)
        {
            if (selected == null || !SeismicAxialForces.IsSeparated(input)) return "";
            var representative = SeismicAxialForces.CombinationsForVerticalAnalysis(input)
                .FirstOrDefault(c => c != null && c.Beta1 == selected.Beta1 && c.Beta2 == selected.Beta2);
            return SeismicAxialForces.CombinationSuffix(representative ?? selected);
        }

        /// <summary>
        /// 同じケースの結果のうち、組合せの部分が <paramref name="suffix"/> のものを選ぶ (空なら最初)。
        /// <paramref name="isCandidate"/> を満たすものだけが候補。無ければ -1。
        ///
        /// <para>組合せを指定したのにその組合せの結果が無ければ -1 (結果なし)。以前は同じケースの最初の結果を返し、
        /// 解析後に組合せを変えたときなどに、選んでいない組合せの沈下を黙って表示していた (2026-11-04 外部レビュー)。</para>
        /// </summary>
        public static int Pick(IReadOnlyList<string> labels, Func<int, bool> isCandidate, string suffix)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (!isCandidate(i)) continue;
                if (suffix.Length == 0 || CombinationPartOf(labels[i]) == suffix) return i;
            }
            return -1;
        }
    }

    /// <summary>荷重ケースの軸力の最大・最小 (組合せをまたいで 1 つに決めるため)。組合せは分離入力でなければ null。</summary>
    public readonly record struct AxialForceEnvelope(
        double Max, LoadCombination? MaxCombination, string MaxSource,
        double Min, LoadCombination? MinCombination, string MinSource);
}
