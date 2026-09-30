using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// せん断の限界値を、代表断面について<b>手計算で</b>求め、プログラムの値と突き合わせる。
    ///
    /// <para>手計算は、断面の入力値 (径・肉厚・Fc・有効プレストレス・鉄筋・鋼管の規格と腐食代) と指針の式だけから、
    /// このテストの中で書き下して求める。プログラムの関数・中間の量 (換算断面積・曲線) は使わない。
    /// 「内訳と検定が同じ関数を通る」ことは <c>ShearLimitBasisTests</c> が見るが、それでは実装の誤り
    /// (単位・断面の量・係数の取り違え) は捕まらない。ここはそれを捕まえる。</para>
    ///
    /// <para>軸力は 0 とする (σ0 の項が消え、換算断面積に依らずに手計算できる)。M/(Q·d) = 2.5。
    /// 許容は 0.3% (手計算は係数を丸めずに書くので、ずれは実装の違いでしか出ない)。</para>
    ///
    /// 式の出典:
    ///  - 場所打ち RC 杭: 建築基礎構造設計指針 2019 (使用・損傷限界) と荒川 min 式 (安全限界)。b = πD/4、j = (7/8)·0.9D、kc = 0.72
    ///  - PHC 杭: 基礎部材の強度と変形性能 5 章。斜めひび割れ τ = √(σt² + σG·σt)、縦ひび割れ τV = 1.9·Fc^0.323、2 式の小さい方
    ///  - SC 杭・場所打ち鋼管コンクリート杭: 鋼管のせん断 (平均せん断応力度 = 最大の 1/2)
    /// </summary>
    [TestClass]
    public class ShearHandCalculationTests
    {
        private const double MonQd = 2.5;
        private const double Tolerance = 3e-3;

        [TestInitialize]
        public void Init()
        {
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
        }

        [TestCleanup]
        public void Cleanup() => Init();

        /// <summary>プログラムの値 [kN] (軸力 0、検定と同じ関数)。</summary>
        private static double Program(PileSection s, SectionLimitState limit, int level, bool factored)
        {
            var basis = s.DescribeShearLimit(limit, level, factored, MonQd, 0.0);
            Assert.IsNotNull(basis, $"{s.PileSectionType} {limit}: 内訳がありません");
            return basis!.ValueN / 1000.0;
        }

        private static void Check(string what, double hand, double program, List<string> problems)
        {
            if (!(Math.Abs(program - hand) <= Tolerance * Math.Abs(hand)))
                problems.Add($"{what}: 手計算 {hand:N2} kN / プログラム {program:N2} kN (差 {(program / hand - 1) * 100:+0.00;-0.00}%)");
        }

        /// <summary>場所打ち RC 杭 D1000・Fc27・ξ0.75・主筋 20-D25・帯筋 D13@150 (SD295)。</summary>
        [TestMethod]
        public void InsituRc_MatchesTheHandCalculation()
        {
            var s = SectionInvariantTests.Recipes[PileTypeNames.RcSection]();
            double D = s.ConcreteOutDia, fc = s.ConcreteFc, xi = s.ConcreteGsi;
            Assert.AreEqual((1000.0, 27.0, 0.75), (D, fc, xi), "(前提) 代表断面が変わった");

            double b = Math.PI * D / 4.0;                 // 等価な幅
            double j = 7.0 / 8.0 * 0.9 * D;               // 応力中心距離
            const double kc = 0.72;
            double ag = 20 * 506.7;                        // D25 × 20 本
            double ac = Math.PI * D * D / 4.0;
            double pt = 100.0 * ag / ac / 4.0;             // 引張鉄筋比 (%) = 全主筋の 1/4
            double pw = 2 * 126.7 / (b * 150.0);           // D13 の 1 組 (2 本) / (b·s)
            const double sigmaWy = 295.0;

            double serviceCore = 0.065 * kc * (49.0 + xi * fc) / (MonQd + 1.7) * b * j / 1000.0;   // (1 + σ0/14.7) = 1
            double ultimateCore = (0.053 * Math.Pow(pt, 0.23) * (18.0 + xi * fc) / (MonQd + 0.12)
                                   + 0.85 * Math.Sqrt(pw * sigmaWy)) * b * j / 1000.0;           // 0.1σ0 = 0

            var problems = new List<string>();
            Check("使用限界 (低減後 β1=0.9、長期は 2/3)", 0.9 * 2.0 / 3.0 * serviceCore, Program(s, SectionLimitState.Service, 1, true), problems);
            Check("使用限界 (低減前)", 2.0 / 3.0 * serviceCore, Program(s, SectionLimitState.Service, 1, false), problems);
            Check("損傷限界 レベル1 (β1=0.9)", 0.9 * serviceCore, Program(s, SectionLimitState.Damage, 1, true), problems);
            Check("損傷限界 レベル2 (β1·β2=0.9×0.75)", 0.9 * 0.75 * serviceCore, Program(s, SectionLimitState.Damage, 2, true), problems);
            Check("安全限界 (β1·β2=0.8×0.75)", 0.8 * 0.75 * ultimateCore, Program(s, SectionLimitState.Ultimate, 2, true), problems);
            Check("安全限界 (低減前)", ultimateCore, Program(s, SectionLimitState.Ultimate, 2, false), problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        /// <summary>PHC 杭 PHC-600-標準-85-B (D600・t90・Fc85・σe8.0)。</summary>
        [TestMethod]
        public void Phc_MatchesTheHandCalculation()
        {
            var s = SectionInvariantTests.Recipes[PileTypeNames.Phc]();
            double D = s.PileDiameter, t = s.ConcreteThickness, fc = s.ConcreteFc, sigmaE = s.SelectedPrecastPile.SigmaE;
            Assert.AreEqual((600.0, 90.0, 85.0, 8.0), (D, t, fc, sigmaE), "(前提) 代表断面が変わった");

            double ro = D / 2, ri = D / 2 - t;
            double I = Math.PI * (Math.Pow(ro, 4) - Math.Pow(ri, 4)) / 4.0;
            double s0 = 2.0 / 3.0 * (Math.Pow(ro, 3) - Math.Pow(ri, 3));
            double alpha = Math.Min(Math.Max(4.0 / (MonQd + 1.0), 1.0), 2.0);
            double eta1 = (t - 15.0) / t;                  // PC 鋼線径 15mm の欠損
            double tauV = 1.9 * Math.Pow(fc, 0.323);
            double shape = 2 * t * I / s0;                  // b·I/S (b = 2t)

            double Diagonal(double k, double sigmaT) => k * alpha * shape * Math.Sqrt(sigmaT * sigmaT + sigmaE * sigmaT) / 1000.0;  // σG = σe (N=0)
            double Web(double k, double factor) => k * alpha * eta1 * shape * factor * tauV / 1000.0;

            double service = Math.Min(Diagonal(0.6, 1.2), Web(0.6, 2.0 / 3.0));
            double damage = Math.Min(Diagonal(0.6, 1.8), Web(0.6, 1.0));
            double ultimate = Math.Min(Diagonal(0.75, 1.8), Web(0.75, 1.0));

            var problems = new List<string>();
            Check("使用限界 (β1=1.0)", service, Program(s, SectionLimitState.Service, 1, true), problems);
            Check("損傷限界 レベル1 (β1=1.0)", damage, Program(s, SectionLimitState.Damage, 1, true), problems);
            Check("損傷限界 レベル2 (β1·β2=1.0×0.65)", 0.65 * damage, Program(s, SectionLimitState.Damage, 2, true), problems);
            Check("安全限界 (β1·β2=1.0×0.65)", 0.65 * ultimate, Program(s, SectionLimitState.Ultimate, 2, true), problems);
            Check("安全限界 (低減前)", ultimate, Program(s, SectionLimitState.Ultimate, 2, false), problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        /// <summary>
        /// PHC 節杭のカタログの許容せん断力 (長期 Qal・短期 Qas) を、プログラムの斜めひび割れ側の値で再現する。
        ///
        /// カタログの許容せん断力は Q = (2t·I/s0)·√(σt² + σG·σt) (σt = 1.2 / 1.8 N/mm²、t は肉厚)。
        /// プログラムの斜めひび割れ側はこれに 0.6·α を掛けたもので、M/(Q·d) = 1.4 なら α = 4/2.4 = 5/3、0.6·α = 1 になる。
        /// せん断の式の肉厚を半分にしていた誤り (値がすべて半分) は、ここでカタログと 2 倍ずれて見つかる。
        /// </summary>
        [TestMethod]
        public void PhcNodularCatalogAllowableShear_IsReproduced()
        {
            const double monQdForUnitFactor = 1.4;   // 0.6·α = 1
            // カタログ (取り込んだ表) の側が食い違っているもの。JP-NPH123 の特厚 CH は、径・肉厚・有効プレストレス (8.5) が
            // JP-NPH105 の特厚 CH と同じなのに Qal・Qas が 1 割小さい (NPH105 は 298/377 kN、NPH123 は 269/339 kN)。
            // 同じ系列のほかの種別 (AH など) は NPH105 と同じ値なので、表の側の誤りとみて照合から外す。
            // 表が直ったら、ここから外すこと (下で「まだ食い違っている」ことも見ている)。
            var knownCatalogDiscrepancies = new HashSet<string>
            {
                "NPH-800-600-特厚-123-CH",
                "NPH-700-600-特厚-123-CH",
            };
            var problems = new List<string>();
            int compared = 0;
            int discrepanciesSeen = 0;
            foreach (var pile in PileSection.NodularPiles.Where(p => p.Qal > 0 && p.Qas > 0))
            {
                if (knownCatalogDiscrepancies.Contains(pile.DisplayName))
                {
                    discrepanciesSeen++;
                    continue;
                }
                var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete, PileSectionType = PileTypeNames.PhcNodular };
                s.SelectedPrecastPile.Name = pile.DisplayName;
                s.RecalculateSelectedPrecastPile();
                if (s.CreateSectionCalculator() is not PrecastPileSection calc) continue;
                var service = calc.GetServiceLimitShearComponents(monQdForUnitFactor, 0.0).DiagonalTension / 1000.0;
                var damage = calc.GetDamageLimitShearComponents(monQdForUnitFactor, 0.0).DiagonalTension / 1000.0;
                // カタログは kN 単位に丸めてある
                if (!(Math.Abs(service - pile.Qal) <= 0.02 * pile.Qal + 1.0))
                    problems.Add($"{pile.DisplayName}: 長期 プログラム {service:N1} kN / カタログ Qal {pile.Qal} kN");
                if (!(Math.Abs(damage - pile.Qas) <= 0.02 * pile.Qas + 1.0))
                    problems.Add($"{pile.DisplayName}: 短期 プログラム {damage:N1} kN / カタログ Qas {pile.Qas} kN");
                compared++;
            }
            TestSource.AssertScanned(compared, 20, "カタログの許容せん断力を持つ節杭");
            Assert.AreEqual(knownCatalogDiscrepancies.Count, discrepanciesSeen, "(前提) 食い違いとして外した製品が表に無い (表が直った・名前が変わった)");
            Assert.AreEqual(0, problems.Count, $"{problems.Count} 件:\n" + string.Join("\n", problems.Take(20)));
        }

        /// <summary>鋼管の基準強度 F (N/mm²)。JIS A 5525 の SKK400・SKK490。</summary>
        private static double SteelF(string grade) => grade switch
        {
            "SKK400" => 235.0,
            "SKK490" => 315.0,
            _ => throw new AssertInconclusiveException($"(前提) 手計算に無い鋼管の規格 {grade}"),
        };

        /// <summary>SC 杭 (鋼管のせん断降伏。軸力に依らない)。鋼管は腐食代を外径の両側と板厚から引く。</summary>
        [TestMethod]
        public void Sc_MatchesTheHandCalculation()
        {
            var s = SectionInvariantTests.Recipes[PileTypeNames.Sc]();
            double f = SteelF(s.PipeGrade);
            double d = s.PipeDia - 2 * s.CorrosionDepth, ts = s.PipeTs - s.CorrosionDepth;
            double area = Math.PI * (d - ts) * ts;

            var problems = new List<string>();
            Check("使用限界 Q = (1/2)·F/(1.5√3)·As", 0.5 * f / (1.5 * Math.Sqrt(3)) * area / 1000.0, Program(s, SectionLimitState.Service, 1, true), problems);
            Check("損傷限界 Q = (1/2)·F/√3·As", 0.5 * f / Math.Sqrt(3) * area / 1000.0, Program(s, SectionLimitState.Damage, 1, true), problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        /// <summary>場所打ち鋼管コンクリート杭 (鋼管 φ1000・t12・腐食代 1mm・SKK400)。</summary>
        [TestMethod]
        public void InsituSteelPipeConcrete_MatchesTheHandCalculation()
        {
            var s = SectionInvariantTests.Recipes[PileTypeNames.SteelPipeConcreteSection]();
            Assert.AreEqual((1000.0, 12.0, 1.0), (s.PipeDia, s.PipeTs, s.CorrosionDepth), "(前提) 代表断面が変わった");
            double f = SteelF(s.PipeGrade);
            double d = s.PipeDia - 2 * s.CorrosionDepth, t = s.PipeTs - s.CorrosionDepth;
            double area = Math.PI * (d - t) * t;

            var problems = new List<string>();
            Check("使用限界 Q = A/2·F/(1.5√3)", area / 2 * f / 1.5 / Math.Sqrt(3) / 1000.0, Program(s, SectionLimitState.Service, 1, true), problems);
            Check("損傷限界 Q = A/2·F/√3", area / 2 * f / Math.Sqrt(3) / 1000.0, Program(s, SectionLimitState.Damage, 1, true), problems);
            // 軸力 0 では p = 0。Qu = (2/3)·π·t·(D − t)·1.1F/√3
            Check("安全限界 Qu = (2/3)·π·t·(D−t)·1.1F/√3", 2.0 / 3.0 * Math.PI * t * (d - t) * 1.1 * f / Math.Sqrt(3) / 1000.0,
                Program(s, SectionLimitState.Ultimate, 2, true), problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }
    }
}
