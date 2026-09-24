using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1.LiteratureVerification
{
    /// <summary>
    /// 文献値のカタログ。ここに 1 件足せば、テストの照合と検証ウィンドウ・README の表の両方に出る。
    ///
    /// 学会の指針 (<see cref="SourceKind.AcademicStandard"/>) と認定工法のカタログ
    /// (<see cref="SourceKind.CertifiedMethod"/>) は区分を分け、別ページ・別表に出す。
    ///
    /// 載せるのは<b>文献に数値として書かれている値</b>だけ。プログラムの既知値を固定する回帰は
    /// 別のテストの仕事で、ここに混ぜると「文献と一致」の件数が水増しになる。
    ///
    /// 出典と値の所在:
    ///  - 建築基礎構造設計指針 2019 (基礎指針'19): 計算例1 例表1.2、計算例2 例表2.2
    ///  - Smart-MAGNUM 工法カタログ (ジャパンパイル): p.10 の α 対比表と計算例
    ///  - Hybrid ニーディング工法カタログ (三谷セキサン): p.3 の α 対比表
    /// verification.html の比較表の多くは文献値が画像の中にあり、ここへ転記できたものから足している。
    /// </summary>
    public static class LiteratureCatalog
    {
        public const string Shishin19 = "建築基礎構造設計指針 2019";
        public const string SmartMagnum = "Smart-MAGNUM 工法カタログ (ジャパンパイル)";
        public const string HybridKneading = "Hybrid ニーディング工法カタログ (三谷セキサン)";
        public const string MkPileRing785 = "エムケーパイルリング785 設計施工指針・同解説 (向山工場)";

        private static readonly Lazy<IReadOnlyList<LiteratureCheck>> _all = new(Build);

        public static IReadOnlyList<LiteratureCheck> All => _all.Value;

        private static IReadOnlyList<LiteratureCheck> Build()
        {
            var list = new List<LiteratureCheck>();
            list.AddRange(Shishin19Checks());
            list.AddRange(SmartMagnumChecks());
            list.AddRange(HybridKneadingChecks());
            list.AddRange(MkPileRing785Checks());
            return list;
        }

        // ── 基礎指針'19 ──────────────────────────────────────────────

        /// <summary>地盤ウィンドウの例題は読込が重いので、例題ごとに 1 回だけ読む。</summary>
        private static readonly Dictionary<string, Lazy<GroundLayerViewModel>> _groundExamples = new()
        {
            ["Example1"] = new(() => LoadGroundExample(vm => vm.Example1Command)),
            ["Example2"] = new(() => LoadGroundExample(vm => vm.Example2Command)),
        };

        private static GroundLayerViewModel LoadGroundExample(Func<GroundLayerViewModel, System.Windows.Input.ICommand> command)
        {
            var mainVm = new MainWindowViewModel();
            var glvm = new GroundLayerViewModel(mainVm);
            command(glvm).Execute(null);
            return glvm;
        }

        private static IEnumerable<LiteratureCheck> Shishin19Checks()
        {
            // 計算例2 例表2.2: 地表面のレベル2 地盤変位 (液状化なし)。
            // DmaxUStar[1] = Dmax × U*(z) × 1000 [mm]、先頭の質点が地表面。
            // 許容 ±1.5% はチャート・丸めの範囲。これを超えたら地盤変位算定の回帰を疑う。
            yield return new LiteratureCheck(
                Shishin19, "計算例2 例表2.2", "地表面の地盤変位 (レベル2)", "mm",
                Literature: 127.4, ToleranceKind.Relative, 0.015,
                () => _groundExamples["Example2"].Value.GroundInput!.GroundMassesData[0].DmaxUStar[1]);

            // 計算例1 例表1.2: 補正N値から求める繰返しせん断ひずみ γcy。
            // 5 点はレベル1 (index 0) の GL-3.0〜-7.0 m の質点。
            // 3 層目 (文献 1.0%、プログラム 0.5%) は図3.2.6 のチャート読み取りで
            // 倍半分の誤差が許容される旨が文献に明記されているので、全点を倍半分で照合する。
            double[] literature = [4.0, 8.0, 1.0, 2.0, 0.5];
            double[] depths = [-3.0, -4.0, -5.0, -6.0, -7.0];
            for (int i = 0; i < literature.Length; i++)
            {
                int index = i;
                yield return new LiteratureCheck(
                    Shishin19, "計算例1 例表1.2", $"繰返しせん断ひずみ γcy (GL{depths[i]:0.0} m, レベル1)", "%",
                    literature[i], ToleranceKind.Factor, 2.0,
                    () => Example1GammaCy()[index],
                    Note: index == 2 ? "チャート読み取り (図3.2.6) のため文献が倍半分を許容" : null);
            }
        }

        private static double[] Example1GammaCy()
        {
            var masses = _groundExamples["Example1"].Value.GroundInput!.GroundMassesData;
            return masses
                .Where(m => m.GLDepth <= -2.5 && m.GLDepth >= -7.5)
                .Select(m => (m.GammaCy != null && m.GammaCy.Count > 0) ? m.GammaCy[0] : null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToArray();
        }

        // ── Smart-MAGNUM ─────────────────────────────────────────────

        private static IEnumerable<LiteratureCheck> SmartMagnumChecks()
        {
            // p.10 対比表 (Don = 1200、Dsn = 1.25 の列から ωp を逆算した既知点)。
            // α = 240·ωp^1.5 + 45(2+LL')·ωp (砂質・礫質) / 210·ωp^1.25 + 45(2+LL')·ωp (粘土質)。
            // 表の α は切り捨て表示 (例 348.14 → 348)。
            (double omegaP, double ll, bool cohesive, int alpha)[] table =
            [
                (1.04, 0.5, false, 348), (1.04, 1.0, false, 394), (1.04, 2.0, false, 441),
                (1.28, 0.5, false, 462), (1.60, 0.5, false, 629), (1.60, 1.0, false, 701),
                (1.60, 2.0, false, 773), (1.00, 0.5, false, 330), (2.00, 2.0, false, 1038),
                (1.04, 0.5, true, 314), (1.28, 0.5, true, 401), (1.76, 0.5, true, 584),
                (1.76, 2.0, true, 742), (1.00, 0.5, true, 300), (2.00, 2.0, true, 859),
            ];
            foreach (var (omegaP, ll, cohesive, alpha) in table)
            {
                double llEffective = ll <= 0.5 ? 0 : ll;
                yield return new LiteratureCheck(
                    SmartMagnum, "p.10 先端支持力係数 α 対比表",
                    $"α (ωp={omegaP:0.00}, LL={ll:0.0} m, {(cohesive ? "粘土質" : "砂質・礫質")})", "",
                    alpha, ToleranceKind.FloorEqual, 0,
                    () => SoilPile.SmartMagnumAlpha(omegaP, llEffective, cohesive),
                    SourceKind: SourceKind.CertifiedMethod);
            }

            // p.10 計算例: 礫質地盤、下杭 φ1200 (節付き)、Den = 1.9 m、Don = 1.2 m、LL = 1.0 m、
            // Nu = 36、Nl = 47.5。
            const double don = 1.20, den = 1.90, ll1 = 1.0, nu = 36.0, nl = 47.5;
            yield return new LiteratureCheck(
                SmartMagnum, "p.10 計算例", "先端支持力係数 α", "",
                654, ToleranceKind.FloorEqual, 0,
                () => SmartMagnumExampleAlpha(don, den, ll1),
                SourceKind: SourceKind.CertifiedMethod);
            yield return new LiteratureCheck(
                SmartMagnum, "p.10 計算例", "長期許容先端支持力 Rpa = α·N·Ap/3", "kN",
                11000, ToleranceKind.Relative, 0.01,
                () =>
                {
                    double alpha = SmartMagnumExampleAlpha(don, den, ll1);
                    double n = (nu + 3.0 * nl) / 4.0;
                    double ap = Math.PI * don * don * 0.25;
                    return alpha * n * ap / 3.0;
                },
                Note: "カタログ表記 1.10×10⁴ kN",
                SourceKind: SourceKind.CertifiedMethod);
        }

        private static double SmartMagnumExampleAlpha(double don, double den, double ll)
        {
            double dsn = SoilPile.SmartMagnumStandardExcavationDia(don);
            double omegaP = SoilPile.ClampOmega(den / dsn);
            return SoilPile.SmartMagnumAlpha(omegaP, llEffective: ll, isCohesive: false);
        }

        // ── Hybrid ニーディング ───────────────────────────────────────

        private static IEnumerable<LiteratureCheck> HybridKneadingChecks()
        {
            // p.3 対比表 11 点。α = 200e(e+0.2) (砂質・礫質) / 200e² (粘土質)。
            (double e, int sand, int clay)[] table =
            [
                (1.0, 240, 200), (1.1, 286, 242), (1.2, 336, 288), (1.3, 390, 338),
                (1.4, 448, 392), (1.5, 510, 450), (1.6, 576, 512), (1.7, 646, 578),
                (1.8, 720, 648), (1.9, 798, 722), (2.0, 880, 800),
            ];
            foreach (var (e, sand, clay) in table)
            {
                yield return new LiteratureCheck(
                    HybridKneading, "p.3 先端支持力係数 α 対比表",
                    $"α (e={e:0.0}, 砂質・礫質)", "",
                    sand, ToleranceKind.Absolute, 0.5,
                    () => SoilPile.HybridAlpha(e, isCohesive: false),
                    SourceKind: SourceKind.CertifiedMethod);
                yield return new LiteratureCheck(
                    HybridKneading, "p.3 先端支持力係数 α 対比表",
                    $"α (e={e:0.0}, 粘土質)", "",
                    clay, ToleranceKind.Absolute, 0.5,
                    () => SoilPile.HybridAlpha(e, isCohesive: true),
                    SourceKind: SourceKind.CertifiedMethod);
            }
        }

        // ── エムケーパイルリング785 ───────────────────────────────────

        /// <summary>
        /// 場所打ち RC 杭の高強度せん断補強筋の工法。指針が式の検証に使った試験体の計算値と突き合わせる。
        ///
        /// 解表5.2『杭径の検討用試験体の計算値と実験値』の QAL (使用限界) と QAS (損傷限界)。
        /// どちらも QAL = Lfs·Ac/κ、QAS = sfs·Ac/κ で、せん断補強筋にも軸力にも依らない。
        /// ここが合えば、杭断面積 Ac・形状係数 κ=4/3・告示1113(第8 第一号) の fs が指針どおりである。
        ///
        /// 試験体の杭径は 400mm と 700mm で、指針の適用範囲 (800〜2600mm) の外にあるが、
        /// 指針自身が算定式の検証に用いた値なので、式の照合にはこれを使う。
        ///
        /// 終局限界せん断力 QSU は、解表5.2 が<b>実験の実測 σwy = 834 N/mm²</b> で算定した値のため
        /// ここには載せない (本プログラムは指針が定める設計値 785 N/mm² を使う)。
        /// 式の形の照合は <c>ShearReinforcementMethodTests</c> で行っている。
        /// </summary>
        private static IEnumerable<LiteratureCheck> MkPileRing785Checks()
        {
            (string no, double b, double fc, string mainBar, double qal, double qas)[] table =
            [
                ("No.4",  400.0, 29.8, "D13", 55.7, 83.6),
                ("No.12", 700.0, 36.6, "D25", 185.3, 278.0),
            ];
            foreach (var (no, b, fc, mainBar, qal, qas) in table)
            {
                double diameter = b, strength = fc;
                string bar = mainBar;
                yield return new LiteratureCheck(
                    MkPileRing785, "解表5.2 杭径の検討用試験体",
                    $"使用限界せん断力 QAL (試験体 {no}、杭径 {b:0} mm)", "kN",
                    qal, ToleranceKind.Relative, 0.005,
                    () => MkShearKn(diameter, strength, bar, ultimate: false, damage: false),
                    Note: "QAL = Lfs·Ac/κ (κ = 4/3)",
                    SourceKind: SourceKind.CertifiedMethod);
                yield return new LiteratureCheck(
                    MkPileRing785, "解表5.2 杭径の検討用試験体",
                    $"損傷限界せん断力 QAs (試験体 {no}、杭径 {b:0} mm)", "kN",
                    qas, ToleranceKind.Relative, 0.005,
                    () => MkShearKn(diameter, strength, bar, ultimate: false, damage: true),
                    Note: "QAs = sfs·Ac/κ (せん断補強筋を考慮しない式)",
                    SourceKind: SourceKind.CertifiedMethod);
            }
        }

        /// <summary>
        /// エムケーパイルリング785 を選んだ場所打ち RC 断面のせん断力 [kN]。
        ///
        /// 告示1113 の区分は静的オプションなので、区分(1) に固定してから計算し、必ず戻す。
        /// 戻さないと後続のテストが別の区分で走る。
        /// </summary>
        private static double MkShearKn(double pileDia, double fc, string mainBarSize, bool ultimate, bool damage)
        {
            int saved = ConcreteModelOptions.Notification1113CompressionCase;
            try
            {
                ConcreteModelOptions.Notification1113CompressionCase = 1;
                var section = new PileSection
                {
                    PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
                    PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
                    ConcreteOutDia = pileDia,
                    PileDiameter = pileDia,
                    ConcreteFc = fc,
                    ConcreteGsi = 1.0,
                    MainBarNum = 20,
                    MainBarSize = mainBarSize,
                    MainBarSpec = "SD390",
                    MainBarCenterCover = 40.0,
                    HoopMethod = ShearReinforcementMethods.MkPileRing785,
                    HoopSize = "MD10",
                    HoopSpacing = 150.0,
                    HoopCenterCover = 40.0,
                };
                var calc = section.CreateSectionCalculator() as InsituReinforcedConcreteSection;
                if (calc == null) return double.NaN;
                var curve = ultimate
                    ? calc.GetUltimateQNInteraction(2.54, section.HoopPw, section.HoopSigmay, true)
                    : damage
                        ? calc.GetDamageLimitQNInteraction(2.54, true)
                        : calc.GetServiceLimitQNInteraction(2.54, true);
                return curve.Item1[0] / 1000.0;
            }
            finally
            {
                ConcreteModelOptions.Notification1113CompressionCase = saved;
            }
        }
    }
}
