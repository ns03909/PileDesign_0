using PileDesign.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// Hybrid ニーディング工法（三谷セキサン、プレボーリング拡大根固め工法）の支持力算定。
    ///
    /// 大臣認定 TACP-0586（砂）/ 0587（礫）/ 0588（粘土）、
    /// 引抜きは（一財）日本建築センター評定 BCJ評定-FD0421-03（砂）/ FD0422-03（礫）。
    ///
    /// <code>
    /// 押込み  Ra  = 1/3 × {α·N·Ap + (β·Ns·Ls + γ·qu·Lc)·ψ}       短期 = 2×長期
    /// 引抜き  tRa = 2/3 × {κ·N·Ap + (λ·Ns·Ls + μ·qu·Lc)·ψ} + Ws  （短期許容）
    /// </code>
    ///
    /// 押込みの式の形は Smart-MAGNUM と同じなので、そちらと同じ枠組み
    /// （<c>Qpu = α·N</c> と <c>ApBearing = π·D1²/4</c> に分けて持たせる）に載せている。
    /// 異なるのは次の 3 点。
    /// <list type="bullet">
    /// <item>α は設計拡径比 e だけで決まる（<c>200e(e+0.2)</c>、粘土は <c>200e²</c>）</item>
    /// <item>節杭区間の周面摩擦力係数が設計掘削径比 es で割増される（摩擦強化型のみ）</item>
    /// <item><b>引抜きに先端項 κ·N·Ap がある</b>（既存の引抜き抵抗は周面のみ）</item>
    /// </list>
    /// </summary>
    public partial class SoilPile
    {
        // ─── 適用範囲（カタログ p.3-4, p.7-8） ───

        public const double HybridRatioMin = 1.0;
        public const double HybridRatioMax = 2.0;

        /// <summary>設計拡径比 e が 1.7 以上のとき、設計掘削径比 es の上限は 1.6 になる。</summary>
        public const double HybridLargeExpansionThreshold = 1.7;
        public const double HybridExcavationRatioMaxForLargeExpansion = 1.6;

        /// <summary>杭先端平均 N 値の適用範囲。N が 5 未満のときは α = 0 とする。</summary>
        public const double HybridToeNMin = 5.0;
        public const double HybridToeNMax = 60.0;

        public const double HybridNsMax = 30.0;
        public const double HybridQuMin = 40.0;
        public const double HybridQuMax = 200.0;

        /// <summary>引抜き方向の先端支持力係数 κ。設計拡径比 e が 1.3 以下のときは 0。</summary>
        public const double HybridKappa = 157.0;
        public const double HybridKappaExpansionThreshold = 1.3;

        /// <summary>この杭が Hybrid ニーディング工法か。</summary>
        [JsonIgnore]
        public bool IsHybridKneading => PileConstructionTypeNames.IsHybridKneading(PileConstructionType);

        // ─── 形状 ───

        /// <summary>節杭の節部径 D1 (m)。杭体最下段区間の断面から導出する。</summary>
        [JsonIgnore]
        public double HybridD1 =>
            PileBodySegments != null && PileBodySegments.Count > 0
                ? NodeOrShaftDiameterM(PileBodySegments[^1].PileSection)
                : 0;

        /// <summary>
        /// 設計拡径比 e = 根固め部径 D3 / 節部径 D1。適用範囲 1.0〜2.0 にクランプする。
        /// </summary>
        [JsonIgnore]
        public double HybridE =>
            Math.Clamp(PileBodyInput?.HybridExpansionRatio ?? HybridRatioMin, HybridRatioMin, HybridRatioMax);

        /// <summary>
        /// 設計掘削径比 es = 掘削径 / 節部径 D1。
        /// 適用範囲は 1.0〜2.0（e が 1.7 以上のときは 1.0〜1.6）で、さらに es ≦ e という制約がある。
        /// es が 1.0 を超えることは<b>軸部を拡大掘削している</b>ことを意味する。
        /// </summary>
        [JsonIgnore]
        public double HybridEs
        {
            get
            {
                double max = HybridE >= HybridLargeExpansionThreshold
                    ? HybridExcavationRatioMaxForLargeExpansion
                    : HybridRatioMax;
                max = Math.Min(max, HybridE);
                return Math.Clamp(PileBodyInput?.HybridExcavationRatio ?? HybridRatioMin, HybridRatioMin, max);
            }
        }

        /// <summary>根固め部径 D3 = e·D1 (m)。</summary>
        [JsonIgnore]
        public double HybridD3 => HybridE * HybridD1;

        /// <summary>杭周固定部の掘削径 = es·D1 (m)。</summary>
        [JsonIgnore]
        public double HybridCircumFixDia => HybridEs * HybridD1;

        /// <summary>
        /// 杭下長 Lu (m)。先端支持力算定位置から杭先端までの長さ。
        ///
        /// カタログは「節部径・拡径比によって異なります。詳細についてはお問い合わせください」として
        /// 値を公表していないため<b>入力値をそのまま使う</b>。
        /// 既定値から自動で決めると、図と計算の双方（周面摩擦の有効長・先端平均N値の基準位置）が
        /// 黙って動いてしまうため、推定値で補うことはしない。
        /// </summary>
        [JsonIgnore]
        public double HybridLu => Math.Max(PileBodyInput?.HybridPileBelowLength ?? 0, 0);

        /// <summary>先端支持力算定位置の標高。杭先端から Lu だけ上方。</summary>
        [JsonIgnore]
        public double HybridToeEvaluationAltitude => PileBottomAltitude + HybridLu;

        /// <summary>
        /// 根固め部上端の、先端支持力算定位置からの高さ (m)。
        /// 設計拡径比 e が 1.6 以下なら 2m、1.7 以上なら 3m（カタログ p.3 の施工パターン例）。
        /// </summary>
        [JsonIgnore]
        public double HybridBulbTopAboveEvaluation =>
            HybridE >= HybridLargeExpansionThreshold ? 3.0 : 2.0;

        // ─── 先端支持力 ───

        /// <summary>
        /// 先端支持力係数 α。
        /// <code>
        /// 砂質・礫質地盤 α = 200·e·(e + 0.2)
        /// 粘土質地盤     α = 200·e²
        /// </code>
        /// </summary>
        internal static double HybridAlpha(double e, bool isCohesive) =>
            isCohesive ? 200.0 * e * e : 200.0 * e * (e + 0.2);

        /// <summary>
        /// 杭先端平均 N 値。先端支持力算定位置より下方に 1·D1、上方に根固め部上端までの区間の平均。
        /// 個々の N 値は 100 でクランプし、平均は 60 を上限とする。
        /// 5 未満のときは α = 0 とする規定があるが、それは <see cref="HybridQpu"/> で処理する。
        /// </summary>
        [JsonIgnore]
        public double HybridToeNValue
        {
            get
            {
                double basis = HybridToeEvaluationAltitude;
                double n = AverageNValueInAltitudeRange(basis - HybridD1, basis + HybridBulbTopAboveEvaluation);
                return Math.Min(n, HybridToeNMax);
            }
        }

        /// <summary>
        /// 極限先端支持力度 α·N。適用範囲下限（N が 5 未満）では α = 0 として 0 を返す。
        /// </summary>
        [JsonIgnore]
        public double HybridQpu
        {
            get
            {
                double n = HybridToeNValue;
                if (n < HybridToeNMin) return 0;
                return HybridAlpha(HybridE, IsCohesive(PileToeGranularityClass)) * n;
            }
        }

        /// <summary>基礎杭の先端の有効断面積 Ap = π·D1²/4 (m²)。</summary>
        [JsonIgnore]
        public double HybridAp => Math.PI * HybridD1 * HybridD1 * 0.25;

        // ─── 引抜きの先端項 ───

        /// <summary>
        /// 引抜き方向の先端支持力係数 κ。カタログは
        /// 「設計拡径比 e が 1.3 以下の場合」「軸部を拡大掘削する場合」を κ = 0 としている。
        /// 後者は<b>設計掘削径比 es が 1.0 を超える状態</b>（＝軸部を基準掘削径より大きく掘る）を指す。
        /// </summary>
        [JsonIgnore]
        public double HybridKappaValue =>
            HybridE <= HybridKappaExpansionThreshold || HybridEs > HybridRatioMin ? 0 : HybridKappa;

        /// <summary>
        /// 引抜き用の杭先端平均 N 値。先端支持力算定位置から上方 4·D1 の区間の平均。
        /// </summary>
        [JsonIgnore]
        public double HybridUpliftNValue
        {
            get
            {
                double basis = HybridToeEvaluationAltitude;
                return Math.Min(AverageNValueInAltitudeRange(basis, basis + 4.0 * HybridD1), HybridToeNMax);
            }
        }

        /// <summary>
        /// 引抜き方向の極限先端抵抗 κ·N·Ap (kN)。
        /// 既存の引抜き抵抗は周面のみなので、この項は <see cref="CalculateResistances"/> で別途加算する。
        /// </summary>
        [JsonIgnore]
        public double HybridUpliftToeResistance => HybridKappaValue * HybridUpliftNValue * HybridAp;

        // ─── 周面摩擦 ───

        /// <summary>
        /// 一軸圧縮強度 qu (kN/m²)。アプリが保持するのは粘着力 Cu なので qu = 2·Cu とする
        /// （Smart-MAGNUM と同じ換算）。個々の値は 200 を上限とする。
        /// </summary>
        internal static double HybridQu(double cohesive) => Math.Min(2.0 * cohesive, HybridQuMax);

        /// <summary>平均 N 値 Ns を適用範囲 0 〜 30 にクランプする。</summary>
        internal static double HybridClampNs(double ns) => Math.Clamp(ns, 0, HybridNsMax);

        /// <summary>
        /// 押込みの周面摩擦力度 τ2 (kN/m²)。
        /// <code>
        /// 砂質・礫質  ストレート形状    β = 4.4          → τ2 = 4.4·Ns
        ///             節付き 標準型     β·Ns = 5.0·Ns + 20
        ///             節付き 摩擦強化型 β·Ns = (5.0·Ns + 30)·es
        /// 粘土質      ストレート形状    γ = 0.7          → τ2 = 0.7·qu
        ///             節付き 標準型     γ·qu = 0.7·qu + 20
        ///             節付き 摩擦強化型 γ·qu = (0.7·qu + 20)·es
        /// </code>
        /// </summary>
        internal static double HybridTau2(
            bool isCohesive, bool isNodular, bool isFrictionEnhanced, double ns, double qu, double es)
        {
            if (isCohesive)
            {
                if (!isNodular) return 0.7 * qu;
                return isFrictionEnhanced ? (0.7 * qu + 20.0) * es : 0.7 * qu + 20.0;
            }

            if (!isNodular) return 4.4 * ns;
            return isFrictionEnhanced ? (5.0 * ns + 30.0) * es : 5.0 * ns + 20.0;
        }

        /// <summary>
        /// 引抜きの周面摩擦力度 τT (kN/m²)。符号は既存の規約に合わせて負値で保持する。
        /// <code>
        /// 砂質・礫質  ストレート形状 λ = 3.74      → 3.74·Ns
        ///             節付き形状     λ·Ns = 4.25·Ns + 17
        /// 粘土質      ストレート形状 μ = 0.59      → 0.59·qu
        ///             節付き形状     μ·qu = 0.63·qu + 18
        /// </code>
        /// カタログは引抜きについて標準型・摩擦強化型を区別していないため、どちらも同じ値を使う。
        /// </summary>
        internal static double HybridTauT(bool isCohesive, bool isNodular, double ns, double qu)
        {
            if (isCohesive) return -(isNodular ? 0.63 * qu + 18.0 : 0.59 * qu);
            return -(isNodular ? 4.25 * ns + 17.0 : 3.74 * ns);
        }

        /// <summary>Hybrid ニーディングの周面摩擦パラメータを杭区間ごとに設定する。</summary>
        private void ApplyHybridCircumProperties()
        {
            if (PileCircumVerticals == null) return;

            bool enhanced = PileBodyInput?.HybridIsFrictionEnhanced ?? false;
            double es = HybridEs;

            foreach (var pcv in PileCircumVerticals)
            {
                var section = pcv.PileBodySegment?.PileSection;
                bool isNodular = section?.IsNodularPile ?? false;
                bool isCohesive = IsCohesive(pcv.GroundLayer.GranularityClass);

                double ns = HybridClampNs(pcv.GroundLayer.NValue);
                double qu = HybridQu(pcv.GroundLayer.Cohesive);

                pcv.Tau2 = HybridTau2(isCohesive, isNodular, enhanced, ns, qu, es);
                pcv.TauT = HybridTauT(isCohesive, isNodular, ns, qu);

                // ψ = π·D（ストレート形状の範囲は軸部径 D0、節付き形状の範囲は節部径 D1）
                pcv.UseNodeDiameterForCircumference = true;
            }
        }

        /// <summary>
        /// 先端支持力算定位置（杭先端から Lu 上方）より下を杭周面摩擦力の対象から外す。
        /// 境界をまたぐ区間は長さで按分する。
        /// </summary>
        private void ApplyHybridCircumExclusion()
        {
            if (PileCircumVerticals == null) return;

            double cutAltitude = HybridToeEvaluationAltitude;
            foreach (var pcv in PileCircumVerticals)
                pcv.ExcludedLength = Math.Clamp(cutAltitude - pcv.Bottom, 0, pcv.L);
        }

        // ─── 適用範囲チェック ───

        /// <summary>
        /// Hybrid ニーディングの適用範囲を検査し、外れている項目の警告文を返す。
        /// 計算は止めず（クランプ後の値で算定し）警告のみ出す方針。
        /// </summary>
        public IEnumerable<string> ValidateHybridKneadingRange()
        {
            if (!IsHybridKneading) yield break;

            string label = $"杭体{PileBodyNo}×地盤{GroundNo}";

            // 三谷セキサンの工法なので、他メーカーの製品は適用対象外
            if (PileBodyInput?.PileBodyType != PileTypeNames.PrecastConcrete)
            {
                yield return $"{label}: 杭体タイプが「{PileBodyInput?.PileBodyType}」です。"
                    + "Hybrid ニーディング工法は三谷セキサンの既製コンクリート杭にのみ適用できます。";
            }

            if (PileBodySegments != null)
            {
                foreach (var segment in PileBodySegments)
                {
                    if (PileMakers.IsUsableBy(segment?.PileSection, PileMakers.MitaniSekisan, out string? maker)) continue;
                    yield return $"{label}: {maker}の製品が使われています。"
                        + "Hybrid ニーディング工法は三谷セキサンの既製コンクリート杭にのみ適用できます。";
                    break;
                }
            }

            double d1 = HybridD1;
            if (d1 <= 0)
            {
                yield return $"{label}: 節部径 D1 を断面から取得できません。杭体最下段の断面を確認してください。";
                yield break;
            }

            bool cohesive = IsCohesive(PileToeGranularityClass);
            double d1Max = cohesive ? 1.200 : 1.300;
            if (d1 < 0.450 || d1 > d1Max)
                yield return $"{label}: 節部径 D1 = {d1 * 1000:N0} mm は適用範囲 φ450〜φ{d1Max * 1000:N0} の外です。";

            double rawE = PileBodyInput?.HybridExpansionRatio ?? 0;
            if (rawE < HybridRatioMin || rawE > HybridRatioMax)
                yield return $"{label}: 設計拡径比 e = {rawE:N2} は適用範囲 1.0〜2.0 の外です（{HybridE:N2} にクランプして計算します）。";
            else if (Math.Abs(rawE * 10 - Math.Round(rawE * 10)) > 1e-6)
                yield return $"{label}: 設計拡径比 e = {rawE:N2} は 0.1 刻みで指定してください。";

            double rawEs = PileBodyInput?.HybridExcavationRatio ?? 0;
            double esMax = Math.Min(
                HybridE >= HybridLargeExpansionThreshold ? HybridExcavationRatioMaxForLargeExpansion : HybridRatioMax,
                HybridE);
            if (rawEs < HybridRatioMin || rawEs > esMax)
                yield return $"{label}: 設計掘削径比 es = {rawEs:N2} は適用範囲 1.0〜{esMax:N1}（es ≦ e）の外です（{HybridEs:N2} にクランプして計算します）。";

            double n = HybridToeNValue;
            if (n < HybridToeNMin)
                yield return $"{label}: 杭先端平均N値 N = {n:N1} が 5 未満のため、先端支持力係数 α = 0（先端支持力なし）として計算します。";

            if (cohesive)
            {
                double qu = HybridQu(PileToeCohesive);
                if (qu > 0 && qu < HybridQuMin)
                    yield return $"{label}: 杭先端の一軸圧縮強度 qu = {qu:N0} kN/m² が適用範囲 40〜200 の下限を下回っています（qu = 2×粘着力 として算定）。";
            }

            // 最大施工深さ（杭施工地盤面からの深さ）
            double depth = (GroundInput?.GroundTopAltitude ?? 0) - PileBottomAltitude;
            double maxDepth = PileToeGranularityClass switch
            {
                "礫質土" => 76.0,
                "粘性土" => 61.0,
                _ => 70.0,
            };
            if (depth > maxDepth)
                yield return $"{label}: 杭先端深度 {depth:N1} m は{PileToeGranularityClass}の最大施工深さ {maxDepth:N0} m を超えています。";

            // 最小施工深さ（引抜き検討時の規定）
            double minDepth = Math.Max(6.0, 10.0 * d1);
            if (depth < minDepth)
                yield return $"{label}: 杭先端深度 {depth:N1} m は引抜き検討の最小施工深さ {minDepth:N1} m（6m かつ 10·D1）を下回っています。";

            if (PileCircumVerticals != null
                && PileCircumVerticals.Any(p => (p.IsPositiveCircumResistance || p.IsNegativeCircumResistance)
                                                && !IsCohesive(p.GroundLayer.GranularityClass)
                                                && p.GroundLayer.NValue > HybridNsMax))
            {
                yield return $"{label}: 周面の平均N値 Ns が適用範囲 30 を超える土層があります（クランプして計算します）。";
            }
        }
    }
}
