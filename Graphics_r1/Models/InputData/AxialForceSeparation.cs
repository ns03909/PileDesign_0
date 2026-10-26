using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 「分離入力」(<see cref="InputModel.IsAxialForceSeparated"/>) を切り替えたときに、
    /// <b>入力済みの地震時軸力を引き継ぐ</b>ための計算。
    ///
    /// <para>分離しない入力 (絶対値 <see cref="PileLayoutDataItem.AxialForceLevel1s"/> ほか) と
    /// 分離入力 (ΔL(U)・ΔL(L)) は別に持っている。切り替えただけでは引き継がれないので、
    /// <b>入力済みの軸力が 0 に見える</b>——入れ直すか、チェックを戻すしかなかった。</para>
    ///
    /// <para>引き継ぎは 2 方向。分離入力にするときは変動軸力 ΔL を U の割合で配り、
    /// やめるときは ΔL(L) と ΔL(U) の和を変動軸力に戻す。<b>割合で配ったものは、
    /// やめれば元に戻る</b>——丸めないので、和は配る前の値と一致する。</para>
    ///
    /// <para>どちらにするか (と、割合) を訊くのは画面側
    /// (<c>MainWindowViewModel.IsAxialForceSeparated</c>)。ここは訊かれた答えのとおりに書く。</para>
    /// </summary>
    internal static class AxialForceSeparation
    {
        /// <summary>レベルは 1 と 2。</summary>
        private static readonly int[] Levels = [1, 2];

        /// <summary>入っていると見なす下限 [kN]。表示は 0.1 kN 刻みなので、その半分。</summary>
        private const double Entered = 0.05;

        /// <summary>
        /// 分離しない入力に、変動軸力 ΔL (= 絶対 − VL) が入っているか。
        ///
        /// <para><b>絶対値ではなく変動を見る。</b>絶対値は VL を足した値なので、何も入れて
        /// いなくても VL のぶん 0 ではない——それを「入力済み」と読むと、新しいファイルで
        /// 毎回訊くことになる。</para>
        /// </summary>
        public static bool HasVariation(IEnumerable<PileLayoutDataItem>? piles) =>
            piles != null && piles.Any(pile => pile != null && Levels.Any(
                level => Variation(pile, level).Any(delta => Math.Abs(delta) >= Entered)));

        /// <summary>分離入力に ΔL(U)・ΔL(L) が入っているか。</summary>
        public static bool HasSeparated(IEnumerable<PileLayoutDataItem>? piles) =>
            piles != null && piles.Any(pile => pile != null && Levels.Any(level =>
                Both(pile, level).Any(value => Math.Abs(value) >= Entered)));

        /// <summary>
        /// 分離入力にする。変動軸力 ΔL を <paramref name="upperShare"/> の割合で配る
        /// ——ΔL(U) = 割合 × ΔL、ΔL(L) = (1 − 割合) × ΔL。
        /// </summary>
        /// <param name="upperShare">ΔL(U) の割合 (0.0〜1.0)。範囲外は丸める。</param>
        public static void Split(IEnumerable<PileLayoutDataItem>? piles, double upperShare)
        {
            if (piles == null) return;
            double share = Math.Clamp(upperShare, 0.0, 1.0);

            foreach (var pile in piles)
            {
                if (pile == null) continue;

                foreach (var level in Levels)
                {
                    var delta = Variation(pile, level);
                    var upper = pile.AxialForceUpperOf(level);
                    var foundation = pile.AxialForceFoundationOf(level);

                    Fit(upper, delta.Count);
                    Fit(foundation, delta.Count);

                    for (int i = 0; i < delta.Count; i++)
                    {
                        upper[i] = delta[i] * share;

                        // **残りは引き算で出す。**(1 − share) を掛けると、share が 0.3 の
                        // ときに和が元の値に戻らないことがある (二進の丸め)。やめたときに
                        // 和を変動へ戻すので、ここが合っていないと値が動く。
                        foundation[i] = delta[i] - upper[i];
                    }
                }
            }
        }

        /// <summary>分離入力にする。ΔL(U)・ΔL(L) をすべて 0 にする。</summary>
        public static void Clear(IEnumerable<PileLayoutDataItem>? piles)
        {
            if (piles == null) return;

            foreach (var pile in piles)
            {
                if (pile == null) continue;

                foreach (var level in Levels)
                {
                    foreach (var list in new[] { pile.AxialForceUpperOf(level), pile.AxialForceFoundationOf(level) })
                    {
                        for (int i = 0; i < list.Count; i++) list[i] = 0.0;
                    }
                }
            }
        }

        /// <summary>
        /// 分離入力をやめる。変動軸力 ΔL を ΔL(L) と ΔL(U) の和にする
        /// (絶対値は VL + 和)。
        /// </summary>
        public static void Merge(IEnumerable<PileLayoutDataItem>? piles)
        {
            if (piles == null) return;

            foreach (var pile in piles)
            {
                if (pile == null) continue;

                foreach (var level in Levels)
                {
                    var upper = pile.AxialForceUpperOf(level);
                    var foundation = pile.AxialForceFoundationOf(level);
                    var absolute = Absolute(pile, level);
                    if (absolute == null) continue;

                    int n = Math.Min(upper.Count, foundation.Count);
                    Fit(absolute, Math.Max(absolute.Count, n));

                    // **絶対値へ書く。**保存するのは絶対値で、変動はそこから出る派生値。
                    // 変動のコレクションへ書いても絶対値は追いつくが、VL を足し引きする
                    // 向きが二重に掛かることがあるので、元の側へ書く。
                    for (int i = 0; i < n && i < absolute.Count; i++)
                    {
                        absolute[i] = pile.AxialForceVL + foundation[i] + upper[i];
                    }
                }
            }
        }

        /// <summary>そのレベルの変動軸力 ΔL (= 絶対 − VL)。</summary>
        private static List<double> Variation(PileLayoutDataItem pile, int level)
        {
            var absolute = Absolute(pile, level);
            if (absolute == null) return [];
            double vl = pile.AxialForceVL;
            return [.. absolute.Select(value => value - vl)];
        }

        /// <summary>そのレベルの ΔL(U) と ΔL(L) を続けて。</summary>
        private static IEnumerable<double> Both(PileLayoutDataItem pile, int level) =>
            pile.AxialForceUpperOf(level).Concat(pile.AxialForceFoundationOf(level));

        private static ObservableCollection<double>? Absolute(PileLayoutDataItem pile, int level) =>
            level == 1 ? pile.AxialForceLevel1s : pile.AxialForceLevel2s;

        /// <summary>要素の数を合わせる (足りなければ 0 を足し、余れば落とす)。</summary>
        private static void Fit(ObservableCollection<double> list, int count)
        {
            while (list.Count > count) list.RemoveAt(list.Count - 1);
            while (list.Count < count) list.Add(0.0);
        }
    }
}
