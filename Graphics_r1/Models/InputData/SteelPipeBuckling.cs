using System;
using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 鋼管杭の曲げ座屈の検討範囲（座屈長）を求める。
    ///
    /// 日本建築学会「基礎部材の強度と変形性能」解説図 8.3 によれば、鋼管杭の設計における
    /// <b>座屈長は液状化区間の長さ</b>とする。液状化区間とは水平地盤反力係数の低減係数 β が
    /// 1 未満の範囲で、<b>連続する場合はその合計</b>を 1 つの座屈長とする。
    ///
    /// 液状化した層では地盤が杭を横に支えられなくなるため、その区間の杭は横支持のない
    /// 柱と同じになる。区間の長さがそのまま座屈長になるのはこのためで、
    /// 液状化が無ければ座屈長は 0（= 座屈による低減なし）になる。
    ///
    /// この長さから弾性曲げ座屈荷重 N_c = π²EI / l_k² を求め、
    /// 許容曲げ座屈応力度 sfc2（同 (8.10)〜(8.12)）に用いる。
    /// </summary>
    public static class SteelPipeBuckling
    {
        /// <summary>
        /// 座屈長 l_k (m)。液状化区間が無ければ 0。
        ///
        /// <para>
        /// 数え方:
        /// 土質点を上から下へ見て、β &lt; 1 の土質点が続く範囲を 1 つの区間としてまとめ、
        /// その区間が<b>杭の範囲と重なる長さ</b>を合計する。区間が複数あれば、
        /// 最も長い区間を座屈長とする（そこが最初に座屈するため）。
        /// </para>
        /// <para>
        /// 杭の範囲で切るのは、杭より下（または上）の液状化層が杭を座屈させないため。
        /// 杭が液状化区間の一部しか通っていなければ、支えの無い長さはその一部だけになる。
        /// </para>
        /// </summary>
        /// <param name="groundMasses">土質点（上から下の順）。</param>
        /// <param name="groundTopAltitude">地表面の Z。土質点の範囲はここから層厚 H を積み上げて決まる。</param>
        /// <param name="levelIndex">地震動レベル（0 = レベル1、1 = レベル2）。β はレベルごとに違う。</param>
        /// <param name="pileTopZ">杭頭の Z。</param>
        /// <param name="pileBottomZ">杭先端の Z（杭頭より下）。</param>
        public static double ComputeBucklingLength(
            IReadOnlyList<GroundMassDataInput> groundMasses,
            double groundTopAltitude,
            int levelIndex,
            double pileTopZ,
            double pileBottomZ)
        {
            if (groundMasses == null || groundMasses.Count == 0) return 0.0;
            if (levelIndex < 0) return 0.0;

            // 杭の範囲。上下が逆に渡っても成り立つようにしておく。
            double zUpper = Math.Max(pileTopZ, pileBottomZ);
            double zLower = Math.Min(pileTopZ, pileBottomZ);
            if (!(zUpper > zLower)) return 0.0;

            double longestRun = 0.0;
            double currentRun = 0.0;
            double top = groundTopAltitude;

            foreach (var mass in groundMasses)
            {
                double thickness = mass?.H ?? 0.0;
                if (!(thickness > 0) || !double.IsFinite(thickness))
                {
                    // 層厚が入っていない土質点は範囲を持たない。区間を切らずに読み飛ばす
                    // （区間の連続性は β で決まり、厚さの入力漏れで分断すべきではない）。
                    continue;
                }

                double bottom = top - thickness;

                if (IsLiquefied(mass, levelIndex))
                {
                    // 杭と重なる長さだけを数える
                    double overlap = Math.Min(top, zUpper) - Math.Max(bottom, zLower);
                    if (overlap > 0) currentRun += overlap;
                }
                else
                {
                    // 支えのある層が挟まったら、そこで区間が切れる
                    if (currentRun > longestRun) longestRun = currentRun;
                    currentRun = 0.0;
                }

                top = bottom;
            }

            if (currentRun > longestRun) longestRun = currentRun;
            return longestRun;
        }

        /// <summary>
        /// この土質点が液状化区間か。<b>液状化対象層と判定されていて、かつ β が 1 未満</b>のとき。
        ///
        /// <b>β だけで判定してはいけない。</b> <c>BetaL</c> の初期値は null ではなく
        /// <c>[0.0, 0.0]</c> で、液状化の判定を一度も行っていない地盤では 0 のまま残る。
        /// β &lt; 1 だけを見ると、この初期値を「全層が完全に液状化」と読んでしまい、
        /// 杭全長が座屈長になる。
        ///
        /// 液状化の判定を行うと <c>IsLiquefactionLayer</c> が立ち、対象層には表から読んだ
        /// β (0.0〜1.0) が、対象外の層には null が入る。β = 0 は「ゆるくて完全に低減」という
        /// 正当な値なので、0 を除外するのではなく<b>判定済みかどうか</b>で切り分ける。
        ///
        /// FL ≥ 1 で液状化に至らない場合は β = 1 が入る (低減なし)。
        /// </summary>
        private static bool IsLiquefied(GroundMassDataInput mass, int levelIndex)
        {
            if (mass == null || !mass.IsLiquefactionLayer) return false;

            var betaList = mass.BetaL;
            if (betaList == null || levelIndex >= betaList.Count) return false;
            double? beta = betaList[levelIndex];
            if (!beta.HasValue || !double.IsFinite(beta.Value)) return false;

            // 1.0 ちょうどは「低減なし」。浮動小数の丸めで 0.9999… になることがあるので少し余裕を見る。
            return beta.Value < 1.0 - 1e-9;
        }
    }
}
