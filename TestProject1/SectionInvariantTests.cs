using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// すべての断面タイプに共通する<b>物理的な不変条件</b>を、断面タイプを自動列挙して検査する。
    ///
    /// 2026-09-07 に既製杭で見つかった 2 つの誤り
    ///  ・ひび割れモーメントの符号（Ftd は「引張＝負」なのに足していた → A 種で Mcr が負）
    ///  ・プレストレスひずみの二重加算（積分側と材料側の両方で足していた → 零ひずみで N ≠ 0）
    /// は、どちらも「杭種を 1 つずつ見れば分かる」種類の誤りだったが、既製杭を通す回帰網が
    /// 無かったため初期コミットから残っていた。杭種・工法はこれからも増えるので、
    /// 個別製品のテストではなく<b>杭種に依らない条件</b>で網を張る。
    ///
    /// 新しい断面タイプを <see cref="PileTypeNames"/> に足すと、ここに代表断面の作り方を
    /// 登録するまで <see cref="EverySectionTypeIsRegistered"/> が落ちる。
    /// 「登録し忘れて網の外に出る」ことが起きない作りにしてある。
    /// </summary>
    [TestClass]
    public class SectionInvariantTests
    {
        // ── 代表断面の作り方（断面タイプ → PileSection）────────────────────────
        // 製品ライブラリを持つ杭種は先頭の製品を使う（名前を固定すると製品の入れ替えで壊れるため）。

        private static readonly Dictionary<string, Func<PileSection>> Recipes = new()
        {
            [PileTypeNames.RcSection] = () => InsituRc(PileTypeNames.InsituRc),
            [PileTypeNames.SteelPipeConcreteSection] = Sprc,
            [PileTypeNames.Phc] = () => Precast(PileTypeNames.Phc, "PHC-600-標準-85-B"),
            [PileTypeNames.PhcNodular] = () => Precast(PileTypeNames.PhcNodular, PileSection.NodularPiles.First().DisplayName),
            [PileTypeNames.Prc] = () => Precast(PileTypeNames.Prc, PileSection.PRCs.First().Name),
            [PileTypeNames.PrcNodular] = () => Precast(PileTypeNames.PrcNodular, PileSection.NodularPrcPiles.First().DisplayName),
            [PileTypeNames.PrcNodularPhcPart] = () => Precast(PileTypeNames.PrcNodularPhcPart, PileSection.NodularPrcPiles.First().PhcPartDisplayName),
            [PileTypeNames.BfsHead] = () => Precast(PileTypeNames.BfsHead, PileSection.BfsPiles.First().DisplayName),
            [PileTypeNames.BfsTip] = () => Precast(PileTypeNames.BfsTip, PileSection.BfsPiles.First().TipDisplayName),
            [PileTypeNames.Sc] = () => Precast(PileTypeNames.Sc, PileSection.SCs.First().Name, pipeGrade: "SKK400"),
            [PileTypeNames.CftSection] = Cft,
        };

        /// <summary>断面計算オブジェクトを持たない断面タイプ（理由付きで明示的に除外する）。</summary>
        private static readonly Dictionary<string, string> NoCalculator = new()
        {
            [PileTypeNames.SteelPipeSection] = "純鋼管区間は M-φ を持たない（CreateSectionCalculator が null を返す設計）",
        };

        /// <summary>杭体タイプ（断面タイプではないので列挙から外す）。</summary>
        private static readonly HashSet<string> BodyTypes =
        [
            PileTypeNames.InsituRc, PileTypeNames.InsituSteelPipeConcrete,
            PileTypeNames.PrecastConcrete, PileTypeNames.SteelPipe,
        ];

        private static PileSection InsituRc(string bodyType) => new()
        {
            PileBodyType = bodyType,
            PileSectionType = PileTypeNames.RcSection,
            ConcreteOutDia = 1000.0, ConcreteFc = 27.0, ConcreteGsi = 0.75,
            MainBarNum = 20, MainBarSize = "D25", MainBarSpec = "SD390", MainBarDr = 600.0,
            HoopSize = "D13", HoopSpacing = 150.0, HoopSpec = "SD295", HoopCenterCover = 150.0,
            PileDiameter = 1000.0,
        };

        private static PileSection Sprc() => new()
        {
            PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
            PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            PipeGrade = "SKK400", PipeDia = 1000.0, PipeTs = 12.0, CorrosionDepth = 1.0,
            ConcreteOutDia = 976.0, ConcreteGsi = 1.0, ConcreteFc = 27.0,
            MainBarNum = 20, MainBarSize = "D25", MainBarSpec = "SD390", MainBarDr = 700.0,
            HoopSize = "D13", HoopSpacing = 150.0, HoopSpec = "SD295", HoopCenterCover = 150.0,
            PileDiameter = 1000.0,
        };

        private static PileSection Cft() => new()
        {
            PileBodyType = PileTypeNames.SteelPipe,
            PileSectionType = PileTypeNames.CftSection,
            PipeGrade = "SKK400", PipeDia = 1000.0, PipeTs = 12.0, CorrosionDepth = 1.0,
            ConcreteOutDia = 976.0, ConcreteGsi = 1.0, ConcreteFc = 27.0,
            MainBarNum = 0, MainBarSize = "D25", MainBarSpec = "SD390", MainBarDr = 700.0,
            PileDiameter = 1000.0,
        };

        private static PileSection Precast(string sectionType, string productName, string? pipeGrade = null)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
            };
            if (pipeGrade != null) s.PipeGrade = pipeGrade;
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.UseUnitGsiForConcreteE = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.UseFiberMPhi = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
        }

        [TestInitialize] public void Init() => ResetOptions();
        [TestCleanup] public void Cleanup() => ResetOptions();

        private static IEnumerable<string> AllSectionTypeNames() =>
            typeof(PileTypeNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .Where(n => !BodyTypes.Contains(n));

        // ── 登録の網 ─────────────────────────────────────────────────────────

        /// <summary>
        /// PileTypeNames にある断面タイプは、すべて代表断面の作り方（か、除外の理由）を持つこと。
        /// 新しい杭種を足したのにここに登録しなければ落ちる。それがこのテストの目的。
        /// </summary>
        [TestMethod]
        public void EverySectionTypeIsRegistered()
        {
            var missing = AllSectionTypeNames()
                .Where(n => !Recipes.ContainsKey(n) && !NoCalculator.ContainsKey(n))
                .ToList();
            Assert.AreEqual(0, missing.Count,
                "SectionInvariantTests に代表断面が登録されていない断面タイプがあります: "
                + string.Join(", ", missing)
                + "\n  Recipes に作り方を足すか、断面計算を持たない理由を NoCalculator に書いてください。");

            foreach (var (type, reason) in NoCalculator)
            {
                var s = new PileSection { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = type };
                Assert.IsNull(s.CreateSectionCalculator(),
                    $"{type}: 除外理由「{reason}」が古くなっています（断面計算が生成されました）。Recipes へ移してください。");
            }
        }

        // ── 物理的な不変条件 ───────────────────────────────────────────────────

        private sealed record Built(string Type, AbstractPileSection Section);

        private static List<Built> BuildAll()
        {
            var list = new List<Built>();
            foreach (var (type, recipe) in Recipes)
            {
                var calc = recipe().CreateSectionCalculator();
                Assert.IsNotNull(calc, $"{type}: 代表断面から断面計算オブジェクトが作れません");
                Assert.IsInstanceOfType(calc, typeof(AbstractPileSection), $"{type}: AbstractPileSection ではありません");
                list.Add(new Built(type, (AbstractPileSection)calc));
            }
            return list;
        }

        private static IEnumerable<(string Name, Material Mat)> MaterialsOf(AbstractPileSection sec) =>
            sec.GetType()
               .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               .Where(p => typeof(Material).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0)
               .Select(p => (p.Name, (Material?)p.GetValue(sec)))
               .Where(t => t.Item2 != null)
               .Select(t => (t.Name, t.Item2!));

        private static MethodInfo? CrackMethod(AbstractPileSection sec) =>
            sec.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               .Where(m => m.Name == "GetCrackMoment")
               .OrderBy(m => m.GetParameters().Length)
               .FirstOrDefault();

        private static double CrackMomentAtZero(AbstractPileSection sec, MethodInfo m)
        {
            object[] args = m.GetParameters().Length == 1 ? [0.0] : [0.0, false];
            var (mcr, _) = ((double, double))m.Invoke(sec, args)!;
            return mcr;
        }

        /// <summary>
        /// 材料の応力度は、ひずみ度 0 で 0 であること。
        /// プレストレスひずみは断面積分が Prestrains として足して渡す規約なので、
        /// 材料側が隠れたオフセット（EpsilonE / EpsilonPi / EpsilonSi）を持ってはいけない。
        /// 持つと二重加算になる（2026-09-07 に PHC/PRC で実際に起きていた）。
        /// </summary>
        [TestMethod]
        public void MaterialsHaveNoHiddenStrainOffset()
        {
            var bad = new List<string>();
            foreach (var b in BuildAll())
                foreach (var (name, mat) in MaterialsOf(b.Section))
                    foreach (var law in new[] { MaterialLaw.Linear, MaterialLaw.Bilinear })
                    {
                        double s0 = mat.GetStress(law, 0.0);
                        if (Math.Abs(s0) > 1e-9)
                            bad.Add($"{b.Type}.{name} ({mat.GetType().Name}, {law}): σ(ε=0) = {s0:F3} N/mm²");
                    }
            Assert.AreEqual(0, bad.Count, "ひずみ度 0 で応力度が 0 でない材料があります（プレストレスの二重加算の疑い）:\n  "
                + string.Join("\n  ", bad));
        }

        /// <summary>
        /// 断面ひずみ 0・曲率 0 の状態は自己釣合い（N ≈ 0, M ≈ 0）であること。
        /// プレストレスはコンクリートの圧縮と鋼材の引張が打ち消し合う内力なので、外力ゼロで N は出ない。
        /// 二重加算があると PC 鋼材が降伏域に入って釣合いが崩れ、N が軸耐力の 1 割近く出ていた。
        /// 許容 2%: 「基礎部材の強度と変形性能」の PC 鋼材プレストレスひずみ式（PHC・PRC 共通）は
        /// コンクリートの弾性短縮項 σe/Ec を含むので、零ひずみ状態では n·Ap·σe（軸耐力の 1% 程度）の
        /// 残差が式の定義上残る。二重加算 (10% 近く) との区別が付けばよい。
        /// </summary>
        [TestMethod]
        public void ZeroStrainStateIsSelfEquilibrated()
        {
            var bad = new List<string>();
            foreach (var b in BuildAll())
            {
                var (ns, ms, _, _) = b.Section.UnfactoredUltimateNM;
                double nRef = ns.Max(Math.Abs), mRef = ms.Max(Math.Abs);
                var (n0, m0) = b.Section.GetUltimateForceAndMoment(0.0, 0.0);
                if (!(Math.Abs(n0) <= 0.02 * nRef) || !(Math.Abs(m0) <= 0.005 * mRef))
                    bad.Add($"{b.Type}: N(0,0)={n0 / 1e3:F1} kN ({n0 / nRef * 100:F2}% of {nRef / 1e3:F0} kN), M={m0 / 1e6:F2} kNm");
            }
            Assert.AreEqual(0, bad.Count, "零ひずみ状態が自己釣合いになっていない断面があります:\n  " + string.Join("\n  ", bad));
        }

        /// <summary>
        /// ひび割れモーメントは正で有限。既製杭は閉形式 Ze·(|ft| + σe + σ0) と一致すること
        /// （Ftd は「引張＝負」で持つので −Ftd。符号を取り違えると A 種で負になる）。
        /// </summary>
        [TestMethod]
        public void CrackMomentIsPositiveAndMatchesTheClosedFormWhereDefined()
        {
            var bad = new List<string>();
            foreach (var b in BuildAll())
            {
                var m = CrackMethod(b.Section);
                if (m == null) continue;
                double mcr = CrackMomentAtZero(b.Section, m);
                if (!double.IsFinite(mcr) || mcr <= 0)
                    bad.Add($"{b.Type}: Mcr(N=0) = {mcr / 1e6:F2} kNm（正でない）");

                if (b.Section is PrecastPileSection p)
                {
                    double closed = p.Ze * (-p.Ftd + p.SigmaE);
                    if (Math.Abs(mcr - closed) > 1e-6 * Math.Abs(closed))
                        bad.Add($"{b.Type}: Mcr={mcr / 1e6:F2} が閉形式 Ze·(−Ftd+σe)={closed / 1e6:F2} と違う");
                }
            }
            Assert.AreEqual(0, bad.Count, "ひび割れモーメントの不整合:\n  " + string.Join("\n  ", bad));
        }

        private static double CrackMomentAt(AbstractPileSection sec, MethodInfo m, double n)
        {
            object[] args = m.GetParameters().Length == 1 ? [n] : [n, false];
            var (mcr, _) = ((double, double))m.Invoke(sec, args)!;
            return mcr;
        }

        /// <summary>
        /// 解析用 M-φ は、軸力を引張側から圧縮側まで掃引しても、φ が単調増加・M が単調非減少・
        /// 全点有限で、折れ点が 3 つ以上あるときは 2 点目がその軸力のひび割れモーメントそのものであること。
        ///
        /// 指針折線は FEM に単調化なしで渡り、FEM は区間勾配をそのまま接線剛性に使う
        /// （1% の下限があるのは終点より先だけ）。途中の区間が負勾配だと K_tan が負になり
        /// Newton-Raphson が停滞する。N=0 だけ見ていた 2026-09-07 時点では、既製杭の
        /// 折り返し (Mcr ≥ β1β2·Mu0) が N=0 で見つかったが、高軸力側の分岐は誰も見ていなかった。
        /// 掃引は安全限界 N-M の軸力範囲を基準に、引張端の半分から圧縮端の 7 割までを取る
        /// （端では折線が原点だけになる杭種があるので、2 点未満は「M-φ 無し」として許す）。
        /// </summary>
        [TestMethod]
        public void MPhiIsMonotonicAndStartsAtTheCrackMomentAcrossAxialForce()
        {
            var bad = new List<string>();
            foreach (var b in BuildAll())
            {
                var (ns, _, _, _) = b.Section.UnfactoredUltimateNM;
                double nMin = ns.Min(), nMax = ns.Max();
                double[] levels = [0.5 * nMin, 0.0, 0.15 * nMax, 0.3 * nMax, 0.5 * nMax, 0.7 * nMax];
                var m = CrackMethod(b.Section);

                // 杭中間部用の折線 (場所打ち鋼管コンクリート杭) は解析が別経路で使うので、同じ条件で見る
                var middle = b.Section.GetType().GetMethod("GetMPhiRelationshipForMiddle",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (double n in levels)
                foreach (var (label, curve) in new (string, Func<(List<double>, List<double>)>)[]
                {
                    ("", () => b.Section.GetMPhiRelationship(n)),
                    (" 杭中間部", () => ((List<double>, List<double>))middle!.Invoke(b.Section, [n])!),
                }.Where(c => c.Item1 == "" || middle != null))
                {
                    string tag = $"{b.Type}{label} N={n / 1e3:F0} kN";
                    var (phis, moments) = curve();
                    if (phis.Count != moments.Count) { bad.Add($"{tag}: 点数不一致 ({phis.Count}/{moments.Count})"); continue; }
                    if (phis.Count < 2) continue;   // 端の軸力で M-φ が成り立たない杭種 (原点のみ) は対象外

                    for (int i = 0; i < phis.Count; i++)
                        if (!double.IsFinite(phis[i]) || !double.IsFinite(moments[i]))
                            bad.Add($"{tag}: M-φ[{i}] が非有限");
                    for (int i = 1; i < phis.Count; i++)
                    {
                        if (!(phis[i] > phis[i - 1])) bad.Add($"{tag}: φ が単調増加でない ({phis[i - 1]:E3} → {phis[i]:E3})");
                        if (moments[i] < moments[i - 1] - 1e-9) bad.Add($"{tag}: M が減少 ({moments[i - 1] / 1e6:F1} → {moments[i] / 1e6:F1})");
                    }

                    if (m != null && phis.Count >= 2)
                    {
                        // ひび割れ点 (φcr, Mcr) が折線に含まれるなら、その M は Mcr そのもの。
                        // ひび割れが最初の折れ点より手前に来るのに点が無いのは、点を落としている。
                        // (鋼管が先に圧縮降伏する SC の分岐のように、ひび割れ点を持たない折線は正当)
                        object[] args = m.GetParameters().Length == 1 ? [n] : [n, false];
                        var (mcr, phiCr) = ((double, double))m.Invoke(b.Section, args)!;
                        if (mcr > 0 && phiCr > 0)
                        {
                            int at = phis.FindIndex(p => Math.Abs(p - phiCr) <= 1e-9 * phiCr);
                            if (at >= 0)
                            {
                                if (Math.Abs(moments[at] - mcr) > 1e-6 * mcr)
                                    bad.Add($"{tag}: φcr の点の M {moments[at] / 1e6:F2} が Mcr {mcr / 1e6:F2} と違う");
                            }
                            else if (phiCr < phis[1] && mcr < moments[^1])
                            {
                                bad.Add($"{tag}: ひび割れ (φ={phiCr:E3}, M={mcr / 1e6:F1}) が最初の折れ点 (φ={phis[1]:E3}) より手前なのに点が無い");
                            }
                        }
                    }
                }
            }
            Assert.AreEqual(0, bad.Count, "M-φ の不整合:\n  " + string.Join("\n  ", bad));
        }

        /// <summary>安全限界 N-M は N=0 で正の曲げ耐力を持ち、全点有限であること。</summary>
        [TestMethod]
        public void UltimateNMIsFiniteWithPositiveMomentAtZeroAxial()
        {
            var bad = new List<string>();
            foreach (var b in BuildAll())
            {
                var (ns, ms, _, _) = b.Section.UnfactoredUltimateNM;
                if (ns.Count < 3) { bad.Add($"{b.Type}: 安全限界 N-M の点数が {ns.Count}"); continue; }
                if (ns.Any(x => !double.IsFinite(x)) || ms.Any(x => !double.IsFinite(x)))
                    bad.Add($"{b.Type}: 安全限界 N-M に非有限値");
                double mAtZero = 0.0;
                for (int i = 0; i + 1 < ns.Count; i++)
                    if ((ns[i] <= 0 && ns[i + 1] >= 0) || (ns[i] >= 0 && ns[i + 1] <= 0))
                    {
                        double d = ns[i + 1] - ns[i];
                        double t = Math.Abs(d) < 1e-12 ? 0.0 : -ns[i] / d;
                        mAtZero = Math.Max(mAtZero, ms[i] + t * (ms[i + 1] - ms[i]));
                    }
                if (!(mAtZero > 0)) bad.Add($"{b.Type}: N=0 での安全限界曲げが正でない ({mAtZero / 1e6:F2} kNm)");
            }
            Assert.AreEqual(0, bad.Count, "安全限界 N-M の不整合:\n  " + string.Join("\n  ", bad));
        }
    }
}
