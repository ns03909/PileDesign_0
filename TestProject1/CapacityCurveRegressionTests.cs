using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using PileDesign.Services;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 中核の耐力算定（N-M 曲線・せん断 Q-N 曲線・群杭効率）の回帰テスト。
    /// これらは杭の検定に直結し、無言の数値回帰が設計を誤らせるため、
    /// 物理的な性質（大小関係・比・軸力依存）を固定して監視する。
    /// </summary>
    [TestClass]
    public class CapacityCurveRegressionTests
    {
        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = false;
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = false;
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = true;
            ConcreteModelOptions.UseReducedCompression = false;
        }

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        // ===== 場所打ち鉄筋コンクリート杭（計算例10 D=1500 / Fc27 / 30-D29）=====
        private static PileSection CreateInsituRcSection() => new()
        {
            PileBodyType = "場所打ち鉄筋コンクリート杭",
            PileSectionType = "鉄筋コンクリート部",
            ConcreteOutDia = 1500.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1500.0,
        };

        private static PileSection CreateSprcSection() => new()
        {
            PileBodyType = "場所打ち鋼管コンクリート杭",
            PileSectionType = "鋼管コンクリート部",
            PipeGrade = "SKK400",
            PipeDia = 1000.0,
            PipeTs = 12.0,
            CorrosionDepth = 1.0,
            ConcreteOutDia = 1000.0,
            ConcreteGsi = 1.0,
            ConcreteFc = 27.0,
            MainBarNum = 20,
            MainBarSize = "D25",
            MainBarSpec = "SD390",
            MainBarDr = 150.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1000.0,
        };

        private static double MaxAbs(List<double> xs)
        {
            double m = 0;
            foreach (double x in xs) if (System.Math.Abs(x) > m) m = System.Math.Abs(x);
            return m;
        }

        private static void AssertAllFinite(List<double> xs, string name)
        {
            Assert.IsNotNull(xs, $"{name} が null");
            Assert.IsTrue(xs.Count > 0, $"{name} が空");
            for (int i = 0; i < xs.Count; i++)
                Assert.IsFalse(double.IsNaN(xs[i]) || double.IsInfinity(xs[i]), $"{name}[{i}] が非有限");
        }

        // ---- N-M 曲線: 使用限界 ≤ 損傷限界 ≤ 安全限界 の耐力大小関係（最大曲げで比較）----
        [TestMethod]
        public void InsituRc_NMCurves_CapacityOrdering()
        {
            ResetOptions();
            var s = CreateInsituRcSection();
            var svc = s.UnfactoredServiceNM;
            var dmg = s.UnfactoredDamageNM;
            var ult = s.UnfactoredUltimateNM;

            AssertAllFinite(svc.M, "使用限界NM.M");
            AssertAllFinite(dmg.M, "損傷限界NM.M");
            AssertAllFinite(ult.M, "安全限界NM.M");

            double mSvc = MaxAbs(svc.M), mDmg = MaxAbs(dmg.M), mUlt = MaxAbs(ult.M);
            Assert.IsTrue(mSvc > 0 && mDmg > 0 && mUlt > 0, $"耐力がゼロ svc={mSvc:F0} dmg={mDmg:F0} ult={mUlt:F0}");
            Assert.IsTrue(mDmg >= mSvc * 0.999, $"損傷限界 < 使用限界 (dmg={mDmg:F0} < svc={mSvc:F0})");
            Assert.IsTrue(mUlt >= mDmg * 0.999, $"安全限界 < 損傷限界 (ult={mUlt:F0} < dmg={mDmg:F0})");
        }

        // ---- せん断 Q-N: 非低減の損傷/使用せん断は式の 2/3 の差のみ ⇒ 各点で厳密に 1.5 倍 ----
        [TestMethod]
        public void InsituRc_ShearQN_DamageIs1p5xService()
        {
            ResetOptions();
            var s = CreateInsituRcSection();
            var svc = s.UnfactoredServiceNQ;
            var dmg = s.UnfactoredDamageNQ;

            AssertAllFinite(svc.Q, "使用限界NQ.Q");
            AssertAllFinite(dmg.Q, "損傷限界NQ.Q");
            Assert.AreEqual(svc.Q.Count, dmg.Q.Count, "使用/損傷 NQ の点数不一致");

            for (int i = 0; i < svc.Q.Count; i++)
            {
                Assert.IsTrue(svc.Q[i] > 0, $"使用限界せん断が非正 [{i}]={svc.Q[i]}");
                double ratio = dmg.Q[i] / svc.Q[i];
                Assert.AreEqual(1.5, ratio, 1e-6,
                    $"非低減の損傷/使用せん断比は 1.5 であるべき（i={i}, ratio={ratio:F6}）");
            }
        }

        // ---- 鋼管の基準強度 F は「基準強度」であって JIS の規格降伏点ではない ----
        // SKK490 は規格降伏点 315 N/mm²、基準強度 325 N/mm² と値が分かれる。
        // F は許容応力度 (長期 F/1.5・短期 F) と材料強度 (1.1F) の基準なので、基準強度が正しい。
        // 出典: 平成12年建設省告示第2464号。ジャパンパイル Technical Note Vol.1-5 表1 も 325。
        // 以前ここが 315 になっており、鋼管の耐力が約 3% 過小だった。
        [TestMethod]
        public void SteelPipeGrade_F_IsSpecifiedStrengthNotYieldPoint()
        {
            // SKK400: 規格降伏点・基準強度とも 235
            Assert.AreEqual(235.0, SteelPipeGrades.GetProperties("SKK400").F, 1e-9,
                "SKK400 の基準強度");

            // SKK490: 基準強度は 325（規格降伏点 315 ではない）
            Assert.AreEqual(325.0, SteelPipeGrades.GetProperties("SKK490").F, 1e-9,
                "SKK490 の基準強度は 325（315 は JIS A5525 の規格降伏点であって基準強度ではない）");

            // 耐力側に F がそのまま効いていること（許容せん断は F に正比例）
            ResetOptions();
            var s400 = CreateSprcSection();
            var s490 = CreateSprcSection();
            s490.PipeGrade = "SKK490";
            double q400 = s400.UnfactoredServiceNQ.Q[0];
            double q490 = s490.UnfactoredServiceNQ.Q[0];
            Assert.AreEqual(325.0 / 235.0, q490 / q400, 1e-6,
                "許容せん断が基準強度に正比例していない");
        }

        // ---- SPRC 使用/損傷限界せん断が鋼管の全断面積で算定されていること ----
        // 以前は内径を d − ts（板厚を片側しか引かない）としており、板厚 ts/2 の管の断面積＝
        // 正しい値のおよそ半分で算定していた。Qs は面積に正比例するので検定比が倍近く動く。
        // 閉形式 Qs = π·ts·(d − ts)/κ · F/1.5/√3（κ=2、d・ts は腐食考慮値）で固定する。
        [TestMethod]
        public void Sprc_ServiceShear_UsesFullPipeArea()
        {
            ResetOptions();
            var s = CreateSprcSection();

            // 腐食 1mm: 外径 1000 → 998、板厚 12 → 11。せん断式はさらに OutDiaMinus = 外径 − 2mm を使う。
            const double d = 1000.0 - 2 * 1.0 - 2.0;   // = 996
            const double ts = 12.0 - 1.0;              // = 11
            const double F = 235.0;                    // SKK400
            double area = System.Math.PI * (d - ts) * ts;
            double expectedQs = area / 2.0 * (F / 1.5 / System.Math.Sqrt(3.0)) * 1e-3;  // kN

            var svc = s.UnfactoredServiceNQ;
            AssertAllFinite(svc.Q, "SPRC使用限界NQ.Q");
            // せん断は軸力に依存しないので全点同値
            foreach (double q in svc.Q)
                Assert.AreEqual(expectedQs, q, expectedQs * 1e-6,
                    "SPRC 使用限界せん断が鋼管の全断面積で算定されていない（内径を d−2ts にすること）");

            // 損傷限界は長期→短期で 1.5 倍
            foreach (double q in s.UnfactoredDamageNQ.Q)
                Assert.AreEqual(expectedQs * 1.5, q, expectedQs * 1e-6,
                    "SPRC 損傷限界せん断が使用限界の 1.5 倍になっていない");
        }

        // ---- SPRC 安全限界せん断は軸力に依存（√(1-p²)）。A2 の面積バグ再発ならほぼ一定になり検出 ----
        [TestMethod]
        public void Sprc_UltimateShear_VariesWithAxialForce()
        {
            ResetOptions();
            var s = CreateSprcSection();
            var nq = s.UnfactoredUltimateNQ;

            AssertAllFinite(nq.Q, "SPRC安全限界NQ.Q");
            double max = 0, min = double.MaxValue;
            foreach (double q in nq.Q) { if (q > max) max = q; if (q < min) min = q; }
            Assert.IsTrue(max > 0, "SPRC安全限界せん断がゼロ");
            double variation = (max - min) / max;
            // 正しい面積なら軸力比 p により端で数%以上減少する。A2 バグ（面積に余分な π）では
            // p≈0 となりほぼ一定（variation≈0）になるため、この下限で再発を検出する。
            Assert.IsTrue(variation > 0.01,
                $"SPRC安全限界せん断が軸力にほぼ依存していない（variation={variation:P2}）。" +
                "GetUltimateLimitShear のせん断面積式（A2）を確認。");
        }

        // ---- PHC・PRC のせん断耐力の内訳（斜めひび割れ / 縦ひび割れ）----
        // 2 式の小さい方が採用値になる。内訳を図に重ねられるようにしたので、
        // 「内訳の小さい方が低減前の採用値と一致する」ことを固定する。
        // ここがずれると、図の点線と実線が食い違っていても誰も気づかない。
        [TestMethod]
        public void Precast_ShearComponents_MinimumMatchesAdoptedCurve()
        {
            ResetOptions();

            foreach (string sectionType in new[] { PileTypeNames.Phc, PileTypeNames.Prc })
            {
                var lib = sectionType == PileTypeNames.Phc ? PileSection.PHCs : PileSection.PRCs;
                if (lib == null || lib.Count == 0) continue;

                var s = CreatePrecastSection(sectionType, lib, "400");
                var components = s.ComputeQNShearComponents(3.0);

                Assert.AreEqual(6, components.Count,
                    $"{sectionType}: 内訳は 3 限界状態 × 2 モードの 6 本のはず");

                foreach (var (limitState, adopted) in new[]
                {
                    ("使用限界", s.UnfactoredServiceNQ),
                    ("損傷限界", s.UnfactoredDamageNQ),
                    ("安全限界", s.UnfactoredUltimateNQ),
                })
                {
                    var two = components.Where(c => c.LimitState == limitState).ToList();
                    Assert.AreEqual(2, two.Count, $"{sectionType} {limitState}: 内訳が 2 本でない");

                    double minOfComponents = two.Min(c => c.Q[0]);
                    double adoptedQ = adopted.Q[0];
                    Assert.IsTrue(adoptedQ > 0, $"{sectionType} {limitState}: 採用値が 0");
                    Assert.AreEqual(adoptedQ, minOfComponents, adoptedQ * 1e-9,
                        $"{sectionType} {limitState}: 内訳の小さい方が低減前の採用値と一致しない");

                    // 内訳はどちらも正で、破壊モードの名前が付いている
                    foreach (var c in two)
                    {
                        Assert.IsTrue(c.Q[0] > 0, $"{sectionType} {limitState} {c.Mode}: 内訳が非正");
                        Assert.IsTrue(c.Mode is "斜めひび割れ" or "縦ひび割れ",
                            $"想定外の破壊モード名: {c.Mode}");
                    }
                }
            }
        }

        // ---- PHC・PRC の斜めひび割れは軸力に依存（σG = σe + σ0e, σ0e = N/Ae）----
        // σ0e を渡し忘れると σG が有効プレストレスだけになり、Q-N が完全な水平線になる。
        // N の掃引範囲そのものが σG = 4.0 〜 Fc/3.5 を動くように決められているので、
        // 水平になっている時点で軸力が効いていない（実際に長らくそうなっていた）。
        // 一方縦ひび割れ側は τV に σG を含まないので軸力に依存しない。両方を固定する。
        [TestMethod]
        public void Precast_ShearDiagonalTension_VariesWithAxialForce()
        {
            ResetOptions();

            foreach (string sectionType in new[] { PileTypeNames.Phc, PileTypeNames.Prc })
            {
                var lib = sectionType == PileTypeNames.Phc ? PileSection.PHCs : PileSection.PRCs;
                if (lib == null || lib.Count == 0) continue;

                var s = CreatePrecastSection(sectionType, lib, "400");
                var components = s.ComputeQNShearComponents(3.0);

                foreach (string limitState in new[] { "使用限界", "損傷限界", "安全限界" })
                {
                    var diagonal = components.Single(
                        c => c.LimitState == limitState && c.Mode == "斜めひび割れ");
                    var web = components.Single(
                        c => c.LimitState == limitState && c.Mode == "縦ひび割れ");

                    AssertAllFinite(diagonal.Q, $"{sectionType} {limitState} 斜めひび割れ");

                    // 圧縮軸力が増えるほど斜めひび割れのせん断耐力は上がる（単調非減少）
                    for (int i = 1; i < diagonal.Q.Count; i++)
                        Assert.IsTrue(diagonal.Q[i] >= diagonal.Q[i - 1] * (1.0 - 1e-9),
                            $"{sectionType} {limitState}: 斜めひび割れが軸力に対して単調増加でない");

                    double lo = diagonal.Q[0];
                    double hi = diagonal.Q[^1];
                    Assert.IsTrue(hi > lo * 1.2,
                        $"{sectionType} {limitState}: 斜めひび割れが軸力でほとんど変わらない " +
                        $"({lo:F1}→{hi:F1} kN)。ShearTauDiagonal に σ0e = N/Ae を渡しているか確認。");

                    // 縦ひび割れは τV = 1.9·Fc^0.323 で σG を含まないため軸力に依存しない
                    foreach (double q in web.Q)
                        Assert.AreEqual(web.Q[0], q, web.Q[0] * 1e-9,
                            $"{sectionType} {limitState}: 縦ひび割れが軸力に依存している");
                }
            }
        }

        // ---- 対象外の杭種では内訳を返さない ----
        // SC は式が別系統、場所打ち系は 2 式に分かれない。空を返さないと図に無意味な線が出る。
        [TestMethod]
        public void ShearComponents_AreEmptyForOtherPileTypes()
        {
            ResetOptions();
            Assert.AreEqual(0, CreateInsituRcSection().ComputeQNShearComponents(3.0).Count,
                "場所打ちRC杭で内訳が返っている");
            Assert.AreEqual(0, CreateSprcSection().ComputeQNShearComponents(3.0).Count,
                "場所打ち鋼管コンクリート杭で内訳が返っている");
        }

        // ===== 既製杭（PrecastPileSection: PHC / SC）=====
        // ライブラリ CSV (pile_library_*.csv) から製品を選んで断面を構築する。
        private static PileSection CreatePrecastSection(string sectionType, List<PrecastPile> lib, string preferDiameter)
        {
            var product = lib.Find(p => p.Name != null && p.Name.Contains(preferDiameter)) ?? lib[0];
            var s = new PileSection
            {
                PileBodyType = "既製コンクリート杭",
                PileSectionType = sectionType,
            };
            s.SetSelectedPrecastPileByName(product.Name);   // D/t/Fc/テンドン等をライブラリから転写
            return s;
        }

        [TestMethod]
        public void Precast_Phc_NMCurves_FiniteAndOrdered()
        {
            if (PileSection.PHCs == null || PileSection.PHCs.Count == 0)
            {
                Assert.Inconclusive("PHC ライブラリ (pile_library_PHC.csv) が読めないためスキップ");
                return;
            }
            ResetOptions();
            var s = CreatePrecastSection("PHC杭", PileSection.PHCs, "-500-");

            var svc = s.UnfactoredServiceNM;
            var dmg = s.UnfactoredDamageNM;
            var ult = s.UnfactoredUltimateNM;
            AssertAllFinite(svc.M, "PHC使用限界NM.M");
            AssertAllFinite(dmg.M, "PHC損傷限界NM.M");
            AssertAllFinite(ult.M, "PHC安全限界NM.M");

            double mSvc = MaxAbs(svc.M), mDmg = MaxAbs(dmg.M), mUlt = MaxAbs(ult.M);
            Assert.IsTrue(mSvc > 0 && mDmg > 0 && mUlt > 0, $"PHC耐力ゼロ svc={mSvc:F0} dmg={mDmg:F0} ult={mUlt:F0}");
            Assert.IsTrue(mDmg >= mSvc * 0.999, $"PHC 損傷限界 < 使用限界 (dmg={mDmg:F0} < svc={mSvc:F0})");
            Assert.IsTrue(mUlt >= mDmg * 0.999, $"PHC 安全限界 < 損傷限界 (ult={mUlt:F0} < dmg={mDmg:F0})");
        }

        [TestMethod]
        public void Precast_Sc_Curves_Finite()
        {
            if (PileSection.SCs == null || PileSection.SCs.Count == 0)
            {
                Assert.Inconclusive("SC ライブラリ (pile_library_SC.csv) が読めないためスキップ");
                return;
            }
            ResetOptions();
            var s = CreatePrecastSection("SC杭", PileSection.SCs, "-500-");

            var ult = s.UnfactoredUltimateNM;
            var nq = s.UnfactoredUltimateNQ;
            AssertAllFinite(ult.M, "SC安全限界NM.M");
            AssertAllFinite(ult.N, "SC安全限界NM.N");
            AssertAllFinite(nq.Q, "SC安全限界NQ.Q");
            Assert.IsTrue(MaxAbs(ult.M) > 0, "SC安全限界曲げ耐力ゼロ");
            Assert.IsTrue(MaxAbs(nq.Q) > 0, "SC安全限界せん断耐力ゼロ");
        }

        // ===== 群杭効率係数 =====
        [TestMethod]
        public void PileGroupFactor_KnownValuesAndProperties()
        {
            // 単杭は 1.0（min(e^(4/3),1) の上限）
            Assert.AreEqual(1.0, PileGroupFactor.GetPileGroupFactor(1, 2.5), 1e-9, "単杭は 1.0");

            // 手計算値（e=1.2/N^(0.65/ratio), 返り値=min(e^(4/3),1)）
            Assert.AreEqual(0.6761, PileGroupFactor.GetPileGroupFactor(9, 3.0), 1e-3, "N=9,ratio=3.0");
            Assert.AreEqual(0.4876, PileGroupFactor.GetPileGroupFactor(16, 2.5), 1e-3, "N=16,ratio=2.5");

            // 性質: 常に ≤ 1
            Assert.IsTrue(PileGroupFactor.GetPileGroupFactor(25, 2.0) <= 1.0);
            // 本数が増えると効率は下がる（単調減少）
            Assert.IsTrue(PileGroupFactor.GetPileGroupFactor(16, 2.5) < PileGroupFactor.GetPileGroupFactor(4, 2.5),
                "本数増で効率低下のはず");
            // 杭間隔比が大きいほど効率は上がる（1 に近づく）
            Assert.IsTrue(PileGroupFactor.GetPileGroupFactor(16, 4.0) > PileGroupFactor.GetPileGroupFactor(16, 2.0),
                "間隔比増で効率向上のはず");
        }
    }
}
