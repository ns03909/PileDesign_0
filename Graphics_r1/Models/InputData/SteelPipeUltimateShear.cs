using System;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 鋼管のせん断降伏で決まる安全限界せん断力。「基礎部材の強度と変形性能」(8.26)。
    ///
    /// <code>
    ///   Qu  = β1·β2 · sQ0 · √(1 − η²)
    ///   sQ0 = 2t(D − t)·sσty / √3
    ///   η   = Nud / sNy ,  sNy = sσty·sAp
    /// </code>
    ///
    /// <b>sσty は 1.1F です。</b> (8.18) の記号説明で <c>sσty = 1.1 × 1.5 × sft</c> と定義され、
    /// sft は長期許容引張応力度 F/1.5 なので 1.1F になります。基準強度 F をそのまま入れると
    /// せん断耐力が 1 割低く、しかも η が 1 割大きくなって √(1−η²) も小さくなります。
    ///
    /// この式は鋼管杭 (中間部・下部)、鋼管杭の杭頭部 (これに Mu/sMu を乗じる (8.27))、
    /// SC 杭 (せん断スパン比が 1.0 を超える場合) の 3 か所で使います。
    /// 以前は 3 か所に書き写されており、<b>SC 杭のものだけが F を使っていました。</b>
    /// 曲線は描けて例外にもならないので、値を比べない限り気づけません。
    ///
    /// なお (8.27) の杭頭部 (コンクリート充填) は η の正規化が違い、圧縮側は Nuc、
    /// 引張側は Nut で正規化します ((8.22))。ここで一緒にできるのは sQ0 と √(1−η²) までです。
    /// </summary>
    internal static class SteelPipeUltimateShear
    {
        /// <summary>軸力がないときの安全限界せん断力 sQ0 = 2t(D−t)·sσty/√3。t・D は腐食しろ考慮後。</summary>
        internal static double Q0(double t, double outerDia, double sSigmaTy)
            => 2.0 * t * (outerDia - t) * sSigmaTy / Math.Sqrt(3.0);

        /// <summary>
        /// 軸力相互作用 √(1−η²)。|η| ≥ 1 では 0 を返す。
        /// 軸力だけで降伏に達していて、せん断に回せる分が残っていない状態。
        /// </summary>
        internal static double AxialInteraction(double eta)
            => Math.Abs(eta) >= 1.0 ? 0.0 : Math.Sqrt(1.0 - eta * eta);

        /// <summary>
        /// (8.26) の低減前の値。β1·β2 は呼び出し側で乗じる。
        /// </summary>
        /// <param name="t">腐食しろ考慮後の板厚 (mm)</param>
        /// <param name="outerDia">腐食しろ考慮後の外径 D (mm)</param>
        /// <param name="sSigmaTy">鋼管杭の材料強度 sσty = 1.1F (N/mm²)</param>
        /// <param name="sAp">腐食しろ考慮後の鋼管断面積 (mm²)</param>
        /// <param name="nud">安全限界状態における設計用軸力 Nud (N)。圧縮が正。</param>
        internal static double Unfactored(
            double t, double outerDia, double sSigmaTy, double sAp, double nud)
        {
            double sNy = sSigmaTy * sAp;
            if (Math.Abs(sNy) < 1e-10) return 0.0;
            return Q0(t, outerDia, sSigmaTy) * AxialInteraction(nud / sNy);
        }
    }
}
