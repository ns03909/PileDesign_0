using System;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// せん断スパン比が 1.0 以下のときの、SC 杭鋼管部分のせん断強度 sQun。
    /// 「基礎部材の強度と変形性能」(7.8)。本会「コンクリート充填鋼管構造設計施工指針」の
    /// 円形鋼管のせん断強度式による。
    ///
    /// <code>
    ///   sā  = a/D           せん断スパンと鋼管径の比
    ///   sN  = 鋼管柱の軸力
    ///   sN0 = 鋼管柱の中心圧縮耐力 (= As·fy、fy は鋼管の降伏強度 1.1F)
    ///
    ///   3|sN| + 2π·sā·sQun &lt; sN0                        → sQun = sN0/π
    ///   2π·sā·sQun ≦ sN0 ≦ 3|sN| + 2π·sā·sQun
    ///       π²(1+sā²)sQun² + π·sā(3|sN|−sN0)sQun
    ///           − (3/2)|sN|·sN0 + (9/4)sN² − (3/4)sN0² = 0
    ///   2π·sā·sQun ≧ sN0
    ///       π²(1+4sā²)sQun² − 4π·sā·sQun·sN0 + (9/4)sN² = 0
    /// </code>
    ///
    /// <b>この式は sQun が条件式にも入っている（陰な式）。</b> 分岐を先に決められないので、
    /// 3 つとも解いてから、自分の条件を満たすものを採る。
    ///
    /// <b>分岐 1 の不等号は、出典の初出が誤っている。</b> 紙面は
    /// 「3sN + 2π·sā·sQun ≧ sN0 の場合」となっているが、正しくは <c>&lt;</c>。
    /// 著者に確認済み (2026-09-10)。
    ///
    /// <b>実装前に数値でも同じ結論に達していた。</b> 不等号の向きと sN の符号の扱いで
    /// 考えられる 8 通りを全部試し、軸力比 −0.3〜0.5、せん断スパン比 0.05〜1.0 の
    /// 820 点で、
    ///
    /// <list type="bullet">
    /// <item>どの分岐も当てはまらない点が 1 つも無い</item>
    /// <item>複数当てはまる 4 点で、値が<b>完全に一致する</b>（境界で分岐がつながっている）</item>
    /// </list>
    ///
    /// を満たすのは上の 1 通りだけだった。他の 7 通りは、覆えない領域が残るか、
    /// 重なった点で値が食い違う。式の集合として成立するのはこれしかない。
    /// (この検証の考え方は、出典から式を起こすときの決まりごと。
    ///  境界で不連続なら、係数か条件のどこかを読み違えている)
    ///
    /// <b>sN は絶対値で入る。</b> 符号付きで扱うと、引張側で分岐が重なって値が食い違う。
    /// 二次式に (9/4)sN² という符号に依らない項が入っていることとも整合する。
    ///
    /// せん断スパン比が 1.0 を超える場合は第 8 章の (8.26) による
    /// (<see cref="SteelPipeUltimateShear"/>)。低減係数 β は呼び出し側で乗じる。
    /// </summary>
    internal static class ScShortSpanShear
    {
        /// <summary>
        /// 低減前のせん断強度 sQun (N)。当てはまる分岐が無ければ 0。
        ///
        /// 軸力が大きいと、どの二次式も実数解を持たなくなる。軸力だけで断面を使い切って
        /// いて、せん断に回せる分が残っていない状態で、(8.26) の |η| ≧ 1 と同じ扱い。
        /// </summary>
        /// <param name="shearSpanRatio">せん断スパン比 sā = a/D</param>
        /// <param name="axialCapacity">鋼管柱の中心圧縮耐力 sN0 = As·fy (N)</param>
        /// <param name="axialForce">鋼管柱の軸力 sN (N)。符号は問わない</param>
        internal static double Unfactored(double shearSpanRatio, double axialCapacity, double axialForce)
        {
            if (axialCapacity <= 0.0 || shearSpanRatio <= 0.0) return 0.0;

            // sN0 で正規化して解く。桁が揃って、条件式が「1 との比較」になる。
            double a = shearSpanRatio;
            double m = Math.Abs(axialForce) / axialCapacity;
            double twoPiA = 2.0 * Math.PI * a;

            // ── 分岐 1: 3|sN| + 2π·sā·sQun < sN0 ──
            double q1 = 1.0 / Math.PI;
            if (3.0 * m + twoPiA * q1 < 1.0 + Tolerance)
                return q1 * axialCapacity;

            // ── 分岐 2: 2π·sā·sQun ≦ sN0 ≦ 3|sN| + 2π·sā·sQun ──
            double q2 = LargerRoot(
                Math.PI * Math.PI * (1.0 + a * a),
                Math.PI * a * (3.0 * m - 1.0),
                -1.5 * m + 2.25 * m * m - 0.75);
            if (IsPositive(q2)
                && twoPiA * q2 <= 1.0 + Tolerance
                && 1.0 <= 3.0 * m + twoPiA * q2 + Tolerance)
                return q2 * axialCapacity;

            // ── 分岐 3: 2π·sā·sQun ≧ sN0 ──
            double q3 = LargerRoot(
                Math.PI * Math.PI * (1.0 + 4.0 * a * a),
                -4.0 * Math.PI * a,
                2.25 * m * m);
            if (IsPositive(q3) && twoPiA * q3 >= 1.0 - Tolerance)
                return q3 * axialCapacity;

            return 0.0;
        }

        /// <summary>
        /// 条件式を比べるときの許容差。
        ///
        /// 境界ちょうどの点が、丸めでどの分岐からも外れることがある。
        /// 実際に sā = 0.425、|sN|/sN0 = 0.05 で
        /// 3m + 2π·sā·(1/π) = 0.15 + 0.85 が 1.0 をわずかに下回り、
        /// <b>どの分岐にも当てはまらず 0 が返って</b>いた（曲線に落ち込みができる）。
        ///
        /// 境界では分岐どうしが同じ値を返すので、どちらが拾っても結果は変わらない。
        /// 拾い損ねないことのほうが大事。
        /// </summary>
        private const double Tolerance = 1e-9;

        /// <summary>
        /// せん断強度として意味のある値か。
        ///
        /// <b>負の根を弾くこと。</b> 軸力が大きいと二次式の大きいほうの根が負になり、
        /// そのとき分岐 2 の条件 (2π·sā·sQun ≦ sN0) は「負 ≦ 正」で成り立ってしまう。
        /// 弾かないと<b>負のせん断耐力</b>が返る。曲線としては描けるので、
        /// 値を見るまで気づけない (実際に −197 kN が出た)。
        /// </summary>
        private static bool IsPositive(double q) => !double.IsNaN(q) && q > 0.0;

        /// <summary>
        /// aq² + bq + c = 0 の大きいほうの実根。実根が無ければ NaN。
        ///
        /// 分岐 3 は軸力 0 で「0 と正の値」の 2 根になる。小さいほうを採ると
        /// 軸力が無いときにせん断耐力も 0 になってしまうので、大きいほうを採る。
        /// </summary>
        private static double LargerRoot(double a, double b, double c)
        {
            if (Math.Abs(a) < 1e-12) return double.NaN;
            double d = b * b - 4.0 * a * c;
            if (d < 0.0) return double.NaN;
            return (-b + Math.Sqrt(d)) / (2.0 * a);
        }
    }
}
