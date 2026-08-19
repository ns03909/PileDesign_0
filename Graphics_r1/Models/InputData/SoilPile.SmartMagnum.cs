using PileDesign.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// Smart-MAGNUM 工法（ジャパンパイル、プレボーリング拡大根固め工法）の支持力算定。
    ///
    /// 大臣認定 TACP-0625（砂質）/ 0626（礫質）/ 0627（粘土質）、引抜きは GBRC 性能証明第 20-21 号。
    ///
    /// <code>
    /// 押込み  Ra  = 1/3 × { α·N·Ap + (β·Ns·Ls + γ·qu·Lc)·ψ }     短期 Ra' = 2Ra
    /// 引抜き  tRu = (0.8·β·Ns·Ls + 0.9·γ·qu·Lc)·ψ                 tRa = tRu/3, tRa' = 2tRu/3
    /// </code>
    ///
    /// 基礎指針'19 の <c>q_p = f(N)</c> 形とは次の点で構造が違うため、既存 5 工法の switch 式には
    /// 手を入れず本ファイルに分離している。
    /// <list type="bullet">
    /// <item>先端面積 Ap は<b>節部径 Don</b> 基準（姿図と N 値範囲に使う根固め部径 Den とは別物）</item>
    /// <item>先端支持力係数 α は根固め部の拡大比 ωp と杭下拡大根固め部長さ LL の関数</item>
    /// <item>周面は 標準型/周面強化型 × ストレート杭/節杭 の 4 通り。節杭では拡大比 ωs が掛かる</item>
    /// <item>周長 ψ は節杭では節部径基準（一般式は軸部径固定）</item>
    /// <item>周面摩擦算定範囲は杭先端の 0.4m 上（先端支持力評価位置）まで</item>
    /// <item>先端平均 N 値は Nu（上方 2m）と Nl（下方 LL+Den+Don）の重み付き平均</item>
    /// </list>
    ///
    /// 限界状態の係数は既存構造をそのまま使う。カタログの 長期 Ru/3・短期 2Ru/3 が
    /// 既存の使用限界 Ru/3・損傷限界 Ru/1.5 と一致するため。
    /// </summary>
    public partial class SoilPile
    {
        // ─── 形状の規定値（カタログ p.5 の模式図） ───

        /// <summary>根固め部上端の位置：杭先端より上方 2m。</summary>
        public const double SmartMagnumBulbTopAboveToe = 2.0;

        /// <summary>先端支持力評価位置：杭先端より上方 0.4m。ここより下は周面摩擦算定範囲から外れる。</summary>
        public const double SmartMagnumToeEvaluationAboveToe = 0.4;

        /// <summary>杭先端平均 N 値 Nu の算定範囲：杭先端より上方 2m。</summary>
        public const double SmartMagnumNuRangeAboveToe = 2.0;

        // ─── 適用範囲（カタログ p.3, p.7-8） ───

        public const double SmartMagnumOmegaMin = 1.00;
        public const double SmartMagnumOmegaMax = 2.00;
        public const double SmartMagnumLLMax = 2.0;
        public const double SmartMagnumExcavationDiaMaxM = 2.5;
        public const double SmartMagnumNsMin = 1.0;
        public const double SmartMagnumNsMax = 30.0;

        /// <summary>この杭が Smart-MAGNUM 工法か。</summary>
        [JsonIgnore]
        public bool IsSmartMagnum => PileConstructionTypeNames.IsSmartMagnum(PileConstructionType);

        /// <summary>杭先端土質が粘土質か（Smart-MAGNUM の式は砂質・礫質 / 粘土質 の 2 分岐）。</summary>
        private static bool IsCohesive(string granularityClass) => granularityClass == "粘性土";

        /// <summary>
        /// Smart-MAGNUM の適用対象外である、他メーカーの既製杭製品の名称接頭辞。
        ///
        /// 本工法はジャパンパイルの工法なので、適用はジャパンパイルの既製コンクリート杭に限る。
        /// 節杭ライブラリ（JP-NPH / JP-NPRC）は Maker 列がジャパンパイルなので断面タイプで判別できるが、
        /// PHC / PRC / SC の一般リストには他メーカーの製品を追記しているため名称で見分ける。
        /// JIS 規格品（PHC- / CPRC- / SC-）はメーカー中立なので適用対象として扱う。
        /// </summary>
        private static readonly (string Prefix, string Maker)[] NonJapanPileProductPrefixes =
        [
            ("MS-hi105", "三谷セキサン"),
            ("Hi-SC105", "三谷セキサン"),
            ("DAM105", "三谷セキサン"),
            ("BF.S", "三谷セキサン"),
        ];

        /// <summary>
        /// 断面がジャパンパイル製（＝ Smart-MAGNUM の適用対象）とみなせるか。
        /// 判別できた他メーカー製品のみ false を返し、メーカー中立の JIS 規格品は true とする。
        /// </summary>
        internal static bool IsJapanPileSection(PileSection section, out string maker)
        {
            maker = "ジャパンパイル";
            if (section == null) return true;

            // 三谷セキサンの BF.S は専用の断面タイプを持つ
            if (section.PileSectionType is PileTypeNames.BfsHead or PileTypeNames.BfsTip)
            {
                maker = "三谷セキサン";
                return false;
            }

            string name = section.SelectedPrecastPile?.Name ?? string.Empty;
            foreach (var (prefix, m) in NonJapanPileProductPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    maker = m;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 周面摩擦・先端支持力の算定に使う杭径 (m)。
        /// 節杭は節部径、ストレート杭は公称外径（腐食代控除前）を使う。
        /// 解析用の <c>PileSection.PileDiameter</c> は鋼管系で腐食代控除後の有効径になるため、
        /// 掘削径との比を取る用途にはそのまま使えない。
        /// </summary>
        internal static double NodeOrShaftDiameterM(PileSection section)
        {
            if (section == null) return 0;
            return (section.IsNodularPile ? section.NodeDiameter : section.NominalPileDiameter) / 1000.0;
        }

        /// <summary>
        /// 基準掘削径 (m)。<c>Dsn = Don + 0.05</c>、<c>Dss = Dos + 0.05</c>。
        /// ただしカタログの特例により、径が 0.44m ちょうどの場合は 0.50m とする（0.49m ではない）。
        /// </summary>
        internal static double SmartMagnumStandardExcavationDia(double nodeOrShaftDiaM)
        {
            if (nodeOrShaftDiaM <= 0) return 0;
            return Math.Abs(nodeOrShaftDiaM - 0.44) < 1e-9 ? 0.50 : nodeOrShaftDiaM + 0.05;
        }

        /// <summary>拡大比 ω を適用範囲 [1.00, 2.00] にクランプする。範囲外は警告で通知する。</summary>
        internal static double ClampOmega(double omega) =>
            Math.Clamp(double.IsFinite(omega) ? omega : SmartMagnumOmegaMin, SmartMagnumOmegaMin, SmartMagnumOmegaMax);

        // ─── 根固め部（先端側） ───

        /// <summary>根固め部に位置する節杭の節部径 Don (m)。杭体最下段区間の断面から導出する。</summary>
        [JsonIgnore]
        public double SmartMagnumDon =>
            PileBodySegments != null && PileBodySegments.Count > 0
                ? NodeOrShaftDiameterM(PileBodySegments[^1].PileSection)
                : 0;

        /// <summary>根固め部の基準掘削径 Dsn (m)。</summary>
        [JsonIgnore]
        public double SmartMagnumDsn => SmartMagnumStandardExcavationDia(SmartMagnumDon);

        /// <summary>拡大根固め部径 Den (m)。入力は <c>PileBodyInput.PileToeDia</c> を流用する。</summary>
        [JsonIgnore]
        public double SmartMagnumDen => D;

        /// <summary>根固め部の拡大比 ωp = Den / Dsn。</summary>
        [JsonIgnore]
        public double SmartMagnumOmegaP =>
            SmartMagnumDsn > 0 ? ClampOmega(SmartMagnumDen / SmartMagnumDsn) : SmartMagnumOmegaMin;

        /// <summary>杭下拡大根固め部長さ LL (m)。適用範囲 0〜2m にクランプする。</summary>
        [JsonIgnore]
        public double SmartMagnumLL =>
            Math.Clamp(PileBodyInput?.SmartMagnumLL ?? 0, 0, SmartMagnumLLMax);

        /// <summary>杭下拡大根固め部長さの有効値 LL'。LL ≤ 0.5m のときは 0 とする。</summary>
        [JsonIgnore]
        public double SmartMagnumLLEffective => SmartMagnumLL <= 0.5 ? 0 : SmartMagnumLL;

        /// <summary>
        /// 先端支持力係数 α。
        /// <code>
        /// 砂質・礫質地盤 α = 240·ωp^1.5  + 45(2 + LL')·ωp
        /// 粘土質地盤     α = 210·ωp^1.25 + 45(2 + LL')·ωp
        /// </code>
        /// </summary>
        internal static double SmartMagnumAlpha(double omegaP, double llEffective, bool isCohesive)
        {
            double baseTerm = isCohesive
                ? 210.0 * Math.Pow(omegaP, 1.25)
                : 240.0 * Math.Pow(omegaP, 1.5);
            return baseTerm + 45.0 * (2.0 + llEffective) * omegaP;
        }

        /// <summary>この杭の先端支持力係数 α。</summary>
        [JsonIgnore]
        public double SmartMagnumAlphaValue =>
            SmartMagnumAlpha(SmartMagnumOmegaP, SmartMagnumLLEffective, IsCohesive(PileToeGranularityClass));

        /// <summary>根固め部に位置する節杭の節部有効断面積 Ap = π·Don²/4 (m²)。</summary>
        [JsonIgnore]
        public double SmartMagnumAp => Math.PI * SmartMagnumDon * SmartMagnumDon * 0.25;

        // ─── 杭先端平均 N 値 ───

        /// <summary>杭先端から上方 2m の平均 N 値 Nu。</summary>
        [JsonIgnore]
        public double SmartMagnumNu =>
            AverageNValueInAltitudeRange(PileBottomAltitude, PileBottomAltitude + SmartMagnumNuRangeAboveToe);

        /// <summary>杭先端から下方 (LL + Den + Don) の平均 N 値 Nl。</summary>
        [JsonIgnore]
        public double SmartMagnumNl =>
            AverageNValueInAltitudeRange(
                PileBottomAltitude - (SmartMagnumLL + SmartMagnumDen + SmartMagnumDon),
                PileBottomAltitude);

        /// <summary>
        /// 指定した標高範囲の平均 N 値。個々の N 値は 100 でクランプする（既存の一般式と同じ規約）。
        /// 該当データが無ければ 0 を返す。
        /// </summary>
        private double AverageNValueInAltitudeRange(double lowerAltitude, double upperAltitude)
        {
            var masses = GroundInput?.GroundMassesData;
            if (masses == null) return 0;

            var relevant = masses
                .Where(data => data.AltitudeDepth <= upperAltitude && data.AltitudeDepth >= lowerAltitude)
                .Select(data => Math.Min(data.NValue, 100))
                .ToList();

            return relevant.Count == 0 ? 0 : relevant.Average();
        }

        /// <summary>
        /// 杭先端平均 N 値。<c>砂質・礫質 N = (Nu + 3Nl)/4</c>、<c>粘土質 N = (Nu + 2Nl)/3</c>。
        /// 適用範囲の上限 60 でクランプする。
        /// </summary>
        [JsonIgnore]
        public double SmartMagnumPileToeNValue
        {
            get
            {
                double nu = SmartMagnumNu;
                double nl = SmartMagnumNl;
                double n = IsCohesive(PileToeGranularityClass)
                    ? (nu + 2.0 * nl) / 3.0
                    : (nu + 3.0 * nl) / 4.0;
                return Math.Min(n, 60);
            }
        }

        // ─── 周面摩擦 ───

        /// <summary>
        /// 一軸圧縮強度 qu (kN/m²)。アプリが保持するのは粘着力 Cu なので <c>qu = 2·Cu</c> とする。
        /// 個々の値はカタログの規定により 16 未満は 0、535 超は 535 に丸める。
        /// </summary>
        internal static double SmartMagnumQu(double cohesive)
        {
            double qu = 2.0 * cohesive;
            if (qu < 16.0) return 0;
            return Math.Min(qu, 535.0);
        }

        /// <summary>平均 N 値 Ns を適用範囲 [1, 30] にクランプする。</summary>
        internal static double SmartMagnumClampNs(double ns) =>
            Math.Clamp(ns, SmartMagnumNsMin, SmartMagnumNsMax);

        /// <summary>
        /// 周面摩擦力度 τ2 (kN/m²)。砂質・礫質は β·Ns、粘土質は γ·qu。
        /// <code>
        /// 砂質・礫質  標準型     ストレート β=5.0      節杭 β·Ns = (30 + 5.5·Ns)·ωs
        ///             周面強化型 ストレート β=8.0      節杭 β    = 9.5·ωs
        /// 粘土質      標準型     ストレート γ=0.7      節杭 γ·qu = (20 + 0.5·qu)·ωs
        ///             周面強化型 ストレート γ=0.9      節杭 γ    = 1.0·ωs
        /// </code>
        /// ωs は節杭にのみ掛かる（拡大比が節部径 Dos を基準に定義されているため）。
        /// </summary>
        internal static double SmartMagnumTau2(
            bool isCohesive, bool isNodular, bool isReinforced, double ns, double qu, double omegaS)
        {
            if (isCohesive)
            {
                if (!isNodular) return (isReinforced ? 0.9 : 0.7) * qu;
                return isReinforced ? 1.0 * omegaS * qu : (20.0 + 0.5 * qu) * omegaS;
            }

            if (!isNodular) return (isReinforced ? 8.0 : 5.0) * ns;
            return isReinforced ? 9.5 * omegaS * ns : (30.0 + 5.5 * ns) * omegaS;
        }

        /// <summary>
        /// 引抜き時の周面摩擦力度 τT (kN/m²)。カタログの
        /// <c>tRu = (0.8·β·Ns·Ls + 0.9·γ·qu·Lc)·ψ</c> より、砂質・礫質 0.8 / 粘土質 0.9 を掛ける。
        /// 符号は既存の規約に合わせて負値で保持する。
        /// </summary>
        internal static double SmartMagnumTauT(bool isCohesive, double tau2) =>
            -(isCohesive ? 0.9 : 0.8) * tau2;

        /// <summary>
        /// 拡翼掘削部（杭周面部の拡大掘削範囲）の標高範囲。
        /// カタログの計算例と同じく、拡翼掘削部長さは杭下拡大根固め部長さ LL を含む長さで、
        /// 杭下拡大根固め部の下端（杭先端から下方 LL）を起点に上方へ測る。
        /// </summary>
        [JsonIgnore]
        public (double BottomAltitude, double TopAltitude) SmartMagnumWingRange
        {
            get
            {
                double bottom = PileBottomAltitude - SmartMagnumLL;
                double length = PileBodyInput?.SmartMagnumWingLength ?? 0;
                return (bottom, bottom + Math.Max(length, 0));
            }
        }

        /// <summary>
        /// 杭区間ごとの杭周面部の拡大比 ωs。
        /// 拡翼掘削範囲に掛からない区間、掘削径 Des が未入力の区間、ストレート杭の区間は 1.0 を返す
        /// （ωs は節部径 Dos を基準に定義されているため節杭にのみ意味を持つ）。
        /// </summary>
        private double SmartMagnumOmegaSFor(PileCircumVertical pcv)
        {
            var section = pcv?.PileBodySegment?.PileSection;
            if (section == null || !section.IsNodularPile) return SmartMagnumOmegaMin;

            double des = (PileBodyInput?.SmartMagnumDes ?? 0) / 1000.0;
            if (des <= 0) return SmartMagnumOmegaMin;

            var (wingBottom, wingTop) = SmartMagnumWingRange;
            // 区間が拡翼掘削範囲に少しでも掛かっていれば拡大比を適用する
            if (pcv.Top <= wingBottom || pcv.Bottom >= wingTop) return SmartMagnumOmegaMin;

            double dss = SmartMagnumStandardExcavationDia(NodeOrShaftDiameterM(section));
            return dss > 0 ? ClampOmega(des / dss) : SmartMagnumOmegaMin;
        }

        /// <summary>
        /// Smart-MAGNUM の周面摩擦パラメータを杭区間ごとに設定する。
        /// τ1 / S1 / S2 はカタログに規定が無いため、土質別の既存値をそのまま使う
        /// （呼び出し元の <c>UpdatePileCircumVerticalProperties</c> が続けて設定する）。
        /// </summary>
        private void ApplySmartMagnumCircumProperties()
        {
            if (PileCircumVerticals == null) return;

            bool isReinforced = PileBodyInput?.SmartMagnumIsReinforcedCircum ?? false;

            foreach (var pcv in PileCircumVerticals)
            {
                var section = pcv.PileBodySegment?.PileSection;
                bool isNodular = section?.IsNodularPile ?? false;
                bool isCohesive = IsCohesive(pcv.GroundLayer.GranularityClass);

                double ns = SmartMagnumClampNs(pcv.GroundLayer.NValue);
                double qu = SmartMagnumQu(pcv.GroundLayer.Cohesive);
                double omegaS = SmartMagnumOmegaSFor(pcv);

                pcv.Tau2 = SmartMagnumTau2(isCohesive, isNodular, isReinforced, ns, qu, omegaS);
                pcv.TauT = SmartMagnumTauT(isCohesive, pcv.Tau2);

                // 周長は節杭では節部径基準
                pcv.UseNodeDiameterForCircumference = true;
            }
        }

        /// <summary>
        /// 先端支持力評価位置（杭先端の 0.4m 上）より下を周面摩擦算定範囲から外す。
        /// 境界をまたぐ区間は長さで按分する。
        /// </summary>
        private void ApplySmartMagnumCircumExclusion()
        {
            if (PileCircumVerticals == null) return;

            double cutAltitude = PileBottomAltitude + SmartMagnumToeEvaluationAboveToe;

            foreach (var pcv in PileCircumVerticals)
            {
                // Top / Bottom は標高（Top が上）
                double excluded = Math.Clamp(cutAltitude - pcv.Bottom, 0, pcv.L);
                pcv.ExcludedLength = excluded;
            }
        }

        /// <summary>杭区間の周面摩擦・周長設定を既存工法の既定（軸部径・全長有効）に戻す。</summary>
        private void ResetCircumOverrides()
        {
            if (PileCircumVerticals == null) return;
            foreach (var pcv in PileCircumVerticals)
            {
                pcv.UseNodeDiameterForCircumference = false;
                pcv.ExcludedLength = 0;
            }
        }

        // ─── 適用範囲チェック ───

        /// <summary>
        /// Smart-MAGNUM の適用範囲を検査し、外れている項目の警告文を返す。
        /// 計算は止めず（クランプ後の値で算定し）警告のみ出す方針。
        /// </summary>
        public IEnumerable<string> ValidateSmartMagnumRange()
        {
            if (!IsSmartMagnum) yield break;

            string label = $"杭体{PileBodyNo}×地盤{GroundNo}";

            // 本工法はジャパンパイルの工法なので、既製コンクリート杭以外は適用対象外
            if (PileBodyInput?.PileBodyType != PileTypeNames.PrecastConcrete)
            {
                yield return $"{label}: 杭体タイプが「{PileBodyInput?.PileBodyType}」です。"
                    + "Smart-MAGNUM 工法はジャパンパイルの既製コンクリート杭にのみ適用できます。";
            }

            // 他メーカーの製品が混ざっていないか（区間ごとに確認し、最初の 1 件だけ通知）
            if (PileBodySegments != null)
            {
                foreach (var segment in PileBodySegments)
                {
                    if (IsJapanPileSection(segment?.PileSection, out string maker)) continue;
                    yield return $"{label}: {maker}の製品が使われています。"
                        + "Smart-MAGNUM 工法はジャパンパイルの既製コンクリート杭にのみ適用できます。";
                    break;
                }
            }

            double don = SmartMagnumDon;
            double den = SmartMagnumDen;
            double dsn = SmartMagnumDsn;

            if (don <= 0)
            {
                yield return $"{label}: 節部径 Don を断面から取得できません。杭体最下段の断面を確認してください。";
                yield break;
            }

            if (don < 0.400 || don > 1.300)
                yield return $"{label}: 根固め部の節部径 Don = {don * 1000:N0} mm は適用範囲 φ400〜φ1300 の外です。";

            if (dsn > 0)
            {
                double rawOmegaP = den / dsn;
                if (rawOmegaP < SmartMagnumOmegaMin || rawOmegaP > SmartMagnumOmegaMax)
                    yield return $"{label}: 根固め部の拡大比 ωp = {rawOmegaP:N2} は適用範囲 1.00〜2.00 の外です（{SmartMagnumOmegaP:N2} にクランプして計算します）。";
            }

            if (den > SmartMagnumExcavationDiaMaxM)
                yield return $"{label}: 拡大根固め部径 Den = {den:N2} m は上限 2.5 m を超えています。";

            double desM = (PileBodyInput?.SmartMagnumDes ?? 0) / 1000.0;
            if (desM > SmartMagnumExcavationDiaMaxM)
                yield return $"{label}: 杭周面部の掘削径 Des = {desM:N2} m は上限 2.5 m を超えています。";

            double rawLL = PileBodyInput?.SmartMagnumLL ?? 0;
            if (rawLL < 0 || rawLL > SmartMagnumLLMax)
                yield return $"{label}: 杭下拡大根固め部長さ LL = {rawLL:N2} m は適用範囲 0〜2 m の外です（{SmartMagnumLL:N2} m にクランプして計算します）。";

            // LL ≤ 3.1·Den
            if (rawLL > 3.1 * den)
                yield return $"{label}: 杭下拡大根固め部長さ LL = {rawLL:N2} m が 3.1×Den = {3.1 * den:N2} m を超えています。";

            // 先端平均 N 値の適用範囲（砂質・礫質 2〜60 / 粘土質 0〜60）
            bool cohesive = IsCohesive(PileToeGranularityClass);
            double n = SmartMagnumPileToeNValue;
            double nMin = cohesive ? 0 : 2;
            if (n < nMin)
                yield return $"{label}: 杭先端平均N値 N = {n:N1} は適用範囲 {nMin:N0}〜60 の下限を下回っています。";

            // 一軸圧縮強度の適用範囲（10〜200）
            if (cohesive)
            {
                double qu = SmartMagnumQu(PileToeCohesive);
                if (qu > 0 && (qu < 10 || qu > 200))
                    yield return $"{label}: 杭先端の一軸圧縮強度 qu = {qu:N0} kN/m² が適用範囲 10〜200 の外です（qu = 2×粘着力 として算定）。";
            }

            if (PileCircumVerticals != null)
            {
                foreach (var pcv in PileCircumVerticals.Where(p => p.IsPositiveCircumResistance || p.IsNegativeCircumResistance))
                {
                    double ns = pcv.GroundLayer.NValue;
                    if (!IsCohesive(pcv.GroundLayer.GranularityClass) && (ns < SmartMagnumNsMin || ns > SmartMagnumNsMax))
                    {
                        yield return $"{label}: 周面の平均N値 Ns = {ns:N1} が適用範囲 1〜30 の外の土層があります（クランプして計算します）。";
                        break;
                    }
                }
            }
        }
    }
}
