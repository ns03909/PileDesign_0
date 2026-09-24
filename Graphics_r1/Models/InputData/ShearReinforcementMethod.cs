using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 場所打ちコンクリート杭のせん断補強筋に使う工法。
    ///
    /// 高強度せん断補強筋の工法は、材料の降伏点が上がるだけではありません。
    /// <b>せん断耐力の算定式そのものが工法の指針で決められています</b>。
    /// 等価正方形断面の取り方 (b = (B/2)·√π) が既定の式 (b = πD/4) と違うため、
    /// 材料だけ差し替えると耐力が 13% ずれます。工法を選んだら
    /// <see cref="InsituReinforcedConcreteSection"/> は既定の式を通さず、
    /// ここに登録した工法の式で 3 つの限界状態すべてを算定します。
    ///
    /// 出典:
    /// - エムケーパイルリング785 設計施工指針・同解説 (2023年4月, 株式会社向山工場)
    ///   GBRC 性能証明 第23-01号 / 材料は大臣認定 MSRB-0067・MSRB-0116
    /// - 場所打ち杭用ウルボンスパイラルせん断補強筋 カタログ (高周波熱錬株式会社)
    ///   大臣認定 MSRB-0024 / BCJ評定-FD0157-06
    /// </summary>
    internal static class ShearReinforcementMethods
    {
        /// <summary>工法を使わない (JIS の異形棒鋼 + 建築基礎構造設計指針の式)。</summary>
        public const string Standard = "標準";

        /// <summary>エムケーパイルリング785 (株式会社向山工場)。785 N/mm2 級・円形の溶接閉鎖形。</summary>
        public const string MkPileRing785 = "エムケーパイルリング785";

        /// <summary>場所打ち杭用ウルボンスパイラル (高周波熱錬株式会社)。1275 N/mm2 級・スパイラル。</summary>
        public const string UlbonSpiral = "ウルボンスパイラル";

        /// <summary>画面の選択肢。並び順は「標準」→ 工法。</summary>
        public static readonly string[] Options = [Standard, MkPileRing785, UlbonSpiral];

        /// <summary>「標準」のせん断補強筋の呼び名 (主筋と同じ JIS の異形棒鋼)。</summary>
        public static readonly string[] StandardBarSizes =
            ["D10", "D13", "D16", "D19", "D22", "D25", "D29", "D32", "D35", "D38", "D41"];

        /// <summary>「標準」のせん断補強筋の規格。</summary>
        public static readonly string[] StandardGrades = ["SD295", "SD345", "SD390", "SD490"];

        // ── 限界状態ごとの算定式の選択肢 ────────────────────────────────
        //
        // 指針が「どちらかによる」と書いている所は、設計者が選ぶ。
        // プログラムが勝手に有利な方を採らない (どちらで算定したかが計算書に残る)。

        /// <summary>損傷限界せん断力 (エムケー 3.2式 / ウルボン ②式)。</summary>
        public const string DamageFormulaDamageLimit = "損傷限界せん断力";

        /// <summary>
        /// 大地震動に対する安全性確保のための短期許容せん断力 (エムケー 3.3式)。
        /// <b>設計用せん断力を水平荷重時の 1.5 倍以上に割り増す</b>のが前提の式。
        /// </summary>
        public const string DamageFormulaSafetyShortTerm = "安全性確保のための短期許容せん断力";

        /// <summary>終局限界せん断力 (大野・荒川 min 式)。エムケー 4.1式 / ウルボン ③式。</summary>
        public const string UltimateFormulaArakawa = "大野・荒川min式";

        /// <summary>終局せん断強度 (トラス・アーチ機構)。ウルボン ④式。</summary>
        public const string UltimateFormulaTrussArch = "トラス・アーチ式";

        private static readonly Dictionary<string, ShearReinforcementMethodSpec> _specs = new()
        {
            [MkPileRing785] = new ShearReinforcementMethodSpec
            {
                Name = MkPileRing785,
                MaterialGrade = "MK785",
                Certification = "GBRC性能証明 第23-01号 / MSRB-0067・MSRB-0116",
                // 表2.4 記号、呼び名、公称直径、公称断面積 (cm2 -> mm2)
                BarAreas = new Dictionary<string, double>
                {
                    ["MD10"] = 71.33,
                    ["MD13"] = 126.7,
                    ["MD16"] = 198.6,
                },
                // 1.5 記号 σwy: 終局限界せん断力算定用の材料強度
                UltimateSigmaWy = 785.0,
                // 2.2.5 MK785 の短期許容応力度 wft
                ShortTermTensileStress = 590.0,
                // 5.1 構造規定 (1) 一次設計のみ 0.1% 以上、二次設計を行う場合は 0.2% 以上
                MinPw = 0.001,
                UltimateMinPw = 0.002,
                // 3.3 式・4.1 式とも 0.4% を超える場合は 0.4% として算定する
                DamagePwCap = 0.004,
                UltimatePwCap = 0.004,
                // 2.1 (1) コンクリートの種類は普通コンクリート、Fc は 24〜45 N/mm2
                FcMin = 24.0,
                FcMax = 45.0,
                // 1.2 杭径の適用範囲
                PileDiaMin = 800.0,
                PileDiaMax = 2600.0,
                // 5.1 構造規定 (2) 杭頭から杭径の 5 倍の深さの範囲は 150mm 以下、それ以外は 300mm 以下
                SpacingMaxNearPileHead = 150.0,
                SpacingMax = 300.0,
                PileHeadRangeInDiameters = 5.0,
                // 4.1 σ0 は圧縮側を正とし 0 以上かつ 0.4·Fc 以下
                UltimateSigma0Cap = 0.4,
                // 4.1 M/(Q·d) は 1.0 以下なら 1.0、3.0 以上なら 3.0
                UltimateMonQdMin = 1.0,
                UltimateMonQdMax = 3.0,
                // 4.1 β: 寸法効果を考慮する低減係数
                UltimateBeta = 0.9,
                // 4.1 Fc: 掘削時に水・泥水を使う場合 (告示1113 第8 第一号の区分(2)) は 0.9 倍
                ReduceUltimateFcForSlurry = true,
                // 4.1 大野・荒川 min 式の第2項の係数
                UltimateHoopCoefficient = 0.85,
                // 3.2 損傷限界せん断力は QAs = sfs·Ac/κ (せん断補強筋を考慮しない)
                DamageIncludesHoop = false,
                // 3.2 ただし、損傷限界せん断力時に引張軸力になる杭体は適用外とする
                DamageExcludesTension = true,
                // 解図3.1 一次設計は 3.2式 (損傷限界) か 3.3式 (安全性確保) のどちらかによる
                DamageFormulaOptions = [DamageFormulaDamageLimit, DamageFormulaSafetyShortTerm],
                // 3.3 設計用せん断力は水平荷重時せん断力を 1.5 倍以上に割り増す
                SafetyShearMagnification = 1.5,
                // 4.1 終局は大野・荒川 min 式のみ
                UltimateFormulaOptions = [UltimateFormulaArakawa],
                // 2.3 主筋 呼び名 D19〜D41
                MainBarSizeNote = "D19〜D41",
            },
            [UlbonSpiral] = new ShearReinforcementMethodSpec
            {
                Name = UlbonSpiral,
                MaterialGrade = "SBPD1275/1420",
                Certification = "MSRB-0024 / BCJ評定-FD0157-06",
                // ウルボンの諸元 公称断面積 (cm2 -> mm2)
                BarAreas = new Dictionary<string, double>
                {
                    ["U9.0"] = 63.6,
                    ["U10.7"] = 89.9,
                    ["U12.6"] = 124.7,
                    ["U15"] = 169.7,
                    ["U17"] = 213.8,
                },
                // 構成材料 せん断補強筋 規格降伏点 σwy
                UltimateSigmaWy = 1275.0,
                // 構成材料 せん断補強筋 短期許容応力度 wft
                ShortTermTensileStress = 590.0,
                // 構造規定 最小せん断補強筋比
                MinPw = 0.001,
                UltimateMinPw = 0.001,
                // 構造規定 最大せん断補強筋比 0.5%、ただし終局せん断耐力は 0.3% 上限で算定
                MaxPw = 0.005,
                DamagePwCap = 0.005,
                UltimatePwCap = 0.003,
                // 構成材料 コンクリート 設計基準強度 Fc = 21〜45 N/mm2
                FcMin = 21.0,
                FcMax = 45.0,
                // 構造規定 せん断補強筋間隔 150mm 以下
                SpacingMaxNearPileHead = 150.0,
                SpacingMax = 150.0,
                // 構造規定 補強範囲: 原則、杭頭から下方に杭径の 5 倍
                PileHeadRangeInDiameters = 5.0,
                // 3式には σ0 の上限の規定が無い (引張側だけ 0 で止める)
                UltimateSigma0Cap = double.PositiveInfinity,
                // 3式には M/(Q·d) の上下限の規定が無い
                UltimateMonQdMin = 0.0,
                UltimateMonQdMax = double.PositiveInfinity,
                // 3式は寸法効果を η として式の中に持つ。別建ての低減係数は無い
                UltimateBeta = 1.0,
                UseSizeEffectEta = true,
                ReduceUltimateFcForSlurry = false,
                // 3式の第2項の係数 (0.85 ではなく 0.846)
                UltimateHoopCoefficient = 0.846,
                // 2式 (短期) は b·j·{ fs + 0.5·wft·(pw - 0.001) }
                DamageIncludesHoop = true,
                DamageExcludesTension = false,
                // 短期は ②式 1 本 (エムケーの 3.2式 に相当する式は無い)
                DamageFormulaOptions = [DamageFormulaDamageLimit],
                SafetyShearMagnification = 1.0,
                // 終局せん断強度の算定は ③式または④式による
                UltimateFormulaOptions = [UltimateFormulaArakawa, UltimateFormulaTrussArch],
                MainBarSizeNote = "",
            },
        };

        /// <summary>工法名が「標準」以外 (= 工法の式を使う) か。</summary>
        public static bool IsProprietary(string? method)
            => !string.IsNullOrEmpty(method) && method != Standard && _specs.ContainsKey(method);

        /// <summary>
        /// 工法の諸元を返す。「標準」や未知の名前では null。
        /// <b>null を耐力 0 に読み替えないこと</b> — 呼び出し側は既定の式へ分岐する。
        /// </summary>
        public static ShearReinforcementMethodSpec? Get(string? method)
            => method != null && _specs.TryGetValue(method, out var spec) ? spec : null;

        /// <summary>工法で使えるせん断補強筋の呼び名。「標準」では JIS の異形棒鋼。</summary>
        public static string[] BarSizes(string? method)
            => Get(method)?.BarAreas.Keys.ToArray() ?? StandardBarSizes;

        /// <summary>工法に対応するせん断補強筋の規格の選択肢。工法では 1 つに決まる。</summary>
        public static string[] Grades(string? method)
        {
            var spec = Get(method);
            return spec == null ? StandardGrades : [spec.MaterialGrade];
        }

        /// <summary>損傷限界 (一次設計) の算定式の選択肢。「標準」では 1 つ (選べない)。</summary>
        public static string[] DamageFormulas(string? method)
            => Get(method)?.DamageFormulaOptions ?? [DamageFormulaDamageLimit];

        /// <summary>終局限界の算定式の選択肢。「標準」では 1 つ (選べない)。</summary>
        public static string[] UltimateFormulas(string? method)
            => Get(method)?.UltimateFormulaOptions ?? [UltimateFormulaArakawa];
    }

    /// <summary>
    /// 1 つの工法の諸元と、指針が定めた算定上の頭打ち・適用範囲。
    /// 値の出典は <see cref="ShearReinforcementMethods"/> の登録箇所にコメントで残してある。
    /// </summary>
    internal sealed class ShearReinforcementMethodSpec
    {
        public required string Name { get; init; }

        /// <summary>せん断補強筋の材料規格名 (諸元表の「せん断補強筋規格」に出る)。</summary>
        public required string MaterialGrade { get; init; }

        /// <summary>大臣認定・性能証明の番号。諸元表の注記に出す。</summary>
        public required string Certification { get; init; }

        /// <summary>呼び名 → 公称断面積 (mm2)。</summary>
        public required Dictionary<string, double> BarAreas { get; init; }

        /// <summary>終局限界せん断力の算定に使う材料強度 σwy (N/mm2)。</summary>
        public required double UltimateSigmaWy { get; init; }

        /// <summary>短期許容引張応力度 wft (N/mm2)。許容せん断力の補強筋項に使う。</summary>
        public required double ShortTermTensileStress { get; init; }

        /// <summary>構造規定の最小せん断補強筋比 (一次設計)。</summary>
        public required double MinPw { get; init; }

        /// <summary>構造規定の最大せん断補強筋比。規定が無ければ +∞。</summary>
        public double MaxPw { get; init; } = double.PositiveInfinity;

        /// <summary>終局 (二次設計) の最小せん断補強筋比。</summary>
        public required double UltimateMinPw { get; init; }

        /// <summary>短期許容せん断力の算定で pw に掛ける頭打ち。</summary>
        public required double DamagePwCap { get; init; }

        /// <summary>終局限界せん断力の算定で pw に掛ける頭打ち。</summary>
        public required double UltimatePwCap { get; init; }

        public required double FcMin { get; init; }
        public required double FcMax { get; init; }

        /// <summary>杭径の適用範囲 (mm)。規定が無ければ 0 / +∞。</summary>
        public double PileDiaMin { get; init; }
        public double PileDiaMax { get; init; } = double.PositiveInfinity;

        /// <summary>杭頭から <see cref="PileHeadRangeInDiameters"/> 倍の範囲の補強筋間隔の上限 (mm)。</summary>
        public required double SpacingMaxNearPileHead { get; init; }

        /// <summary>上記の範囲より下の補強筋間隔の上限 (mm)。</summary>
        public required double SpacingMax { get; init; }

        /// <summary>杭頭からの範囲を杭径の何倍で見るか。</summary>
        public required double PileHeadRangeInDiameters { get; init; }

        /// <summary>終局の σ0 の上限を Fc の何倍で切るか。規定が無ければ +∞。</summary>
        public required double UltimateSigma0Cap { get; init; }

        public required double UltimateMonQdMin { get; init; }
        public required double UltimateMonQdMax { get; init; }

        /// <summary>終局の寸法効果を考慮する低減係数 β。1.0 なら低減なし。</summary>
        public required double UltimateBeta { get; init; }

        /// <summary>終局の第1項に杭径による係数 η を掛けるか (ウルボン)。</summary>
        public bool UseSizeEffectEta { get; init; }

        /// <summary>終局の Fc を水中・泥水打設 (告示1113 区分(2)) で 0.9 倍するか。</summary>
        public required bool ReduceUltimateFcForSlurry { get; init; }

        /// <summary>終局の第2項 c·√(pw·σwy) の係数 c。</summary>
        public required double UltimateHoopCoefficient { get; init; }

        /// <summary>損傷限界 (短期) の式がせん断補強筋の項を含むか。</summary>
        public required bool DamageIncludesHoop { get; init; }

        /// <summary>損傷限界 (一次設計) に選べる算定式。先頭が既定。</summary>
        public required string[] DamageFormulaOptions { get; init; }

        /// <summary>終局限界に選べる算定式。先頭が既定。</summary>
        public required string[] UltimateFormulaOptions { get; init; }

        /// <summary>
        /// 「安全性確保のための短期許容せん断力」を使うときに設計用せん断力へ掛ける割増係数。
        /// 指針が「水平荷重時せん断力を 1.5 倍以上に割り増す」と定めているもの。
        /// </summary>
        public required double SafetyShearMagnification { get; init; }

        /// <summary>損傷限界の式が引張軸力の杭体に適用できないか。</summary>
        public required bool DamageExcludesTension { get; init; }

        /// <summary>主筋の呼び名の適用範囲。空なら規定なし。</summary>
        public required string MainBarSizeNote { get; init; }

        /// <summary>
        /// 杭径・Fc が工法の適用範囲の外なら、その理由 (利用者向けの文)。範囲内なら null。
        ///
        /// 範囲の外では工法の指針の式が使えないので、その断面のせん断の検定は OK とも NG とも言わない
        /// (検定の項目に理由を付け、判定を「適用範囲外」にする)。諸元表の注記も同じ判定を使う。
        /// </summary>
        public string? OutOfScopeReason(double pileDiaMm, double fc)
        {
            bool hasDiaLimit = !double.IsPositiveInfinity(PileDiaMax);
            if (hasDiaLimit && (pileDiaMm < PileDiaMin || pileDiaMm > PileDiaMax))
                return $"杭径が{Name}の適用範囲 ({PileDiaMin:N0}〜{PileDiaMax:N0}mm) の外です";
            if (fc < FcMin || fc > FcMax)
                return $"Fc が{Name}の適用範囲 ({FcMin:N0}〜{FcMax:N0}N/mm2) の外です";
            return null;
        }

        /// <summary>
        /// せん断補強筋比・間隔が工法の構造規定を満たさなければ、その理由 (利用者向けの文)。満たせば null。
        ///
        /// 構造規定を満たさない断面には、工法の指針の式の前提が成り立たない。杭径・Fc の範囲外
        /// (<see cref="OutOfScopeReason"/>) と同じく、せん断の検定は OK とも NG とも言わない。
        /// 間隔の上限は深さで変わる (杭頭から杭径の <see cref="PileHeadRangeInDiameters"/> 倍の範囲は厳しい)ので、
        /// その範囲にかかる要素かを <paramref name="nearPileHead"/> で受け取る。
        /// 終局の算定上の頭打ち (<see cref="UltimatePwCap"/>) は違反ではないので含めない。
        /// </summary>
        /// <param name="pw">せん断補強筋比 (比。0.002 = 0.2%)</param>
        /// <param name="spacingMm">せん断補強筋の間隔 (mm)</param>
        /// <param name="nearPileHead">要素が杭頭から杭径の所定倍の範囲にかかるか</param>
        /// <param name="ultimate">終局 (安全限界) の検定か。終局を検討する場合の最小せん断補強筋比を加える</param>
        public string? DetailingViolation(double pw, double spacingMm, bool nearPileHead, bool ultimate)
        {
            if (pw < MinPw)
                return $"せん断補強筋比 {pw * 100:N2}% が{Name}の構造規定 ({MinPw * 100:N1}% 以上) を満たしません";
            if (pw > MaxPw)
                return $"せん断補強筋比 {pw * 100:N2}% が{Name}の構造規定 ({MaxPw * 100:N1}% 以下) を満たしません";
            if (ultimate && pw < UltimateMinPw)
                return $"せん断補強筋比 {pw * 100:N2}% が{Name}の終局を検討する場合の構造規定 ({UltimateMinPw * 100:N1}% 以上) を満たしません";

            double maxSpacing = nearPileHead ? SpacingMaxNearPileHead : SpacingMax;
            if (spacingMm > maxSpacing)
                return nearPileHead
                    ? $"せん断補強筋の間隔 {spacingMm:N0}mm が{Name}の構造規定 (杭頭から杭径の{PileHeadRangeInDiameters:N0}倍の範囲は {maxSpacing:N0}mm 以下) を満たしません"
                    : $"せん断補強筋の間隔 {spacingMm:N0}mm が{Name}の構造規定 ({maxSpacing:N0}mm 以下) を満たしません";
            return null;
        }

        /// <summary>呼び名の公称断面積 (mm2)。未登録なら 0。</summary>
        public double BarArea(string? barSize)
            => barSize != null && BarAreas.TryGetValue(barSize, out var a) ? a : 0.0;

        /// <summary>既定のせん断補強筋の呼び名 (一覧の先頭)。</summary>
        public string DefaultBarSize => BarAreas.Keys.First();

        /// <summary>
        /// 杭径による寸法効果の係数 η。η = 1 (B &lt; 1m)、η = B^(-1/4) (B ≧ 1m、B は m)。
        /// <see cref="UseSizeEffectEta"/> が false の工法では常に 1。
        /// </summary>
        public double SizeEffectEta(double pileDiaMm)
        {
            if (!UseSizeEffectEta) return 1.0;
            double bMeter = pileDiaMm / 1000.0;
            return bMeter < 1.0 ? 1.0 : Math.Pow(bMeter, -0.25);
        }
    }
}
