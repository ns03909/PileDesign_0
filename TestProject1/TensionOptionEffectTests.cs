using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 「引張側の扱い（バイリニア型）」オプションが実際に何を変えるかを固定する。
    ///
    /// 2026-09-07 に「場所打ち鋼管コンクリート杭で引張側を変えても N-M 曲線が変わらない」と
    /// 報告され、調べると
    ///  - 安全限界 N-M への影響は、ひび割れ前の引張域 (中立軸近傍の幅 20〜30 mm) だけで 1% 未満
    ///  - 使用限界・損傷限界 N-M は Linear 則で元からコンクリート引張を無視しており 0
    ///  - 場所打ち RC 杭の M-φ は「最初からひび割れ剛性」になる (ひび割れ点がひび割れ後勾配の線上に乗る)
    ///  - 場所打ち鋼管コンクリート杭の M-φ は Mcr が閉形式 Ze·(Ft+σ0) で Ft 固定のため不変
    /// だった。最後の点を「引張無視なら Ft=0 (Mcr = Ze·σ0、N=0 で 0)」として RC 杭と揃えた。
    /// つまりこのオプションは「解析用 M-φ の初期剛性をひび割れ後のものにする」スイッチであって、
    /// N-M 曲線を変えるものではない。ツールチップもそう書いてある。
    /// </summary>
    [TestClass]
    public class TensionOptionEffectTests
    {
        private static PileSection Sprc() => new()
        {
            PileBodyType = PileTypeNames.InsituSteelPipeConcrete, PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            PipeGrade = "SKK490", PipeDia = 1200.0, PipeTs = 16.0, CorrosionDepth = 1.0,
            ConcreteOutDia = 1168.0, ConcreteGsi = 1.0, ConcreteFc = 27.0,
            MainBarNum = 16, MainBarSize = "D29", MainBarSpec = "SD390", MainBarDr = 768.0,
            HoopSize = "D13", HoopSpacing = 150.0, HoopSpec = "SD295", HoopCenterCover = 150.0, PileDiameter = 1200.0,
        };
        private static PileSection Rc() => new()
        {
            PileBodyType = PileTypeNames.InsituRc, PileSectionType = PileTypeNames.RcSection,
            ConcreteOutDia = 1000.0, ConcreteFc = 27.0, ConcreteGsi = 0.75,
            MainBarNum = 20, MainBarSize = "D25", MainBarSpec = "SD390", MainBarDr = 600.0,
            HoopSize = "D13", HoopSpacing = 150.0, HoopSpec = "SD295", HoopCenterCover = 150.0, PileDiameter = 1000.0,
        };

        private static void Reset()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.UseFiberMPhi = false;
        }
        [TestInitialize] public void Init() => Reset();
        [TestCleanup] public void Cleanup() => Reset();

        // オプションは static なので、断面の構築時だけでなく曲線を取り出す時点でも効く。
        // 「引張負担」「引張無視」それぞれの状態で、構築と取り出しを同じ状態で行う。
        private static T Under<T>(bool ignoreTension, Func<T> f)
        {
            ConcreteModelOptions.IgnoreTensileStrength = ignoreTension;
            try { return f(); }
            finally { ConcreteModelOptions.IgnoreTensileStrength = false; }
        }

        private static AbstractPileSection Build(Func<PileSection> make) =>
            (AbstractPileSection)make().CreateSectionCalculator()!;

        private static double MaxAbsDiff(List<double> a, List<double> b) =>
            a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

        [DataTestMethod]
        [DataRow("場所打ち RC 杭", 0)]
        [DataRow("場所打ち鋼管コンクリート杭", 1)]
        public void AllowableCurvesIgnoreConcreteTensionRegardlessOfTheOption(string name, int kind)
        {
            Func<PileSection> make = kind == 0 ? Rc : Sprc;
            var (svc0, dmg0) = Under(false, () => { var s = Build(make); return (s.UnfactoredServiceNM.Item2, s.UnfactoredDamageNM.Item2); });
            var (svc1, dmg1) = Under(true, () => { var s = Build(make); return (s.UnfactoredServiceNM.Item2, s.UnfactoredDamageNM.Item2); });
            Assert.AreEqual(0.0, MaxAbsDiff(svc0, svc1), 1e-6,
                $"{name}: 使用限界 N-M がオプションで動いた（Linear 則は元から引張を無視する）");
            Assert.AreEqual(0.0, MaxAbsDiff(dmg0, dmg1), 1e-6, $"{name}: 損傷限界 N-M がオプションで動いた");
        }

        [DataTestMethod]
        [DataRow("場所打ち RC 杭", 0)]
        [DataRow("場所打ち鋼管コンクリート杭", 1)]
        public void UltimateCurveBarelyMoves(string name, int kind)
        {
            Func<PileSection> make = kind == 0 ? Rc : Sprc;
            var ult0 = Under(false, () => Build(make).UnfactoredUltimateNM.Item2);
            var ult1 = Under(true, () => Build(make).UnfactoredUltimateNM.Item2);
            double mMax = ult0.Max();
            double d = MaxAbsDiff(ult0, ult1);
            Assert.IsTrue(d > 0, $"{name}: 安全限界 N-M が全く動かない（オプションが読まれていない）");
            Assert.IsTrue(d < 0.01 * mMax,
                $"{name}: 安全限界 N-M の変化 {d / 1e6:F1} kNm が Mmax {mMax / 1e6:F0} kNm の 1% を超えた（ひび割れ前の引張域だけの効果のはず）");
        }

        /// <summary>引張無視の M-φ は最初からひび割れ剛性: 原点からの初期勾配が弾性 Ec·Ie より明確に小さい。</summary>
        [DataTestMethod]
        [DataRow("場所打ち RC 杭", 0)]
        [DataRow("場所打ち鋼管コンクリート杭", 1)]
        public void IgnoringTensionStartsMPhiOnTheCrackedStiffness(string name, int kind)
        {
            Func<PileSection> make = kind == 0 ? Rc : Sprc;
            var (p1, m1) = Under(false, () => Build(make).GetMPhiRelationship(0.0));
            var (p2, m2) = Under(true, () => Build(make).GetMPhiRelationship(0.0));

            double slopeWith = m1[1] / p1[1];        // 引張負担: 第 1 区間は弾性 (Mcr/φcr = Ec·Ie)
            double slopeWithout = m2[1] / p2[1];     // 引張無視: 第 1 区間はひび割れ剛性
            Assert.IsTrue(slopeWithout < 0.6 * slopeWith,
                $"{name}: 引張無視でも初期勾配が落ちない ({slopeWithout:E3} vs 弾性 {slopeWith:E3})");

            for (int i = 1; i < p2.Count; i++)
            {
                Assert.IsTrue(p2[i] > p2[i - 1], $"{name}: 引張無視の M-φ で φ が単調増加でない");
                Assert.IsTrue(m2[i] >= m2[i - 1] - 1e-9, $"{name}: 引張無視の M-φ で M が減少");
            }
        }

        /// <summary>場所打ち鋼管コンクリート杭: 引張無視の Mcr は Ze·σ0 (N=0 で 0、圧縮軸力でデコンプレッション)。</summary>
        [TestMethod]
        public void SprcCrackMomentFollowsTheOption()
        {
            var w = (InsituSteelPipeReinforcedConcreteSection)Build(Sprc);
            double n = 5000e3;

            double mcr0With = Under(false, () => w.GetCrackMoment(0.0).Item1);
            double mcr0Without = Under(true, () => w.GetCrackMoment(0.0).Item1);
            Assert.IsTrue(mcr0With > 0, "引張負担: N=0 の Mcr は正");
            Assert.AreEqual(0.0, mcr0Without, 1e-9, "引張無視: N=0 では曲げ 0 で既にひび割れ (Mcr=0)");

            double mcrNWith = Under(false, () => w.GetCrackMoment(n).Item1);
            double mcrNWithout = Under(true, () => w.GetCrackMoment(n).Item1);
            double expected = w.Ze * (n / w.Ae);
            Assert.AreEqual(expected, mcrNWithout, 1e-6 * expected, "引張無視: 圧縮軸力下の Mcr はデコンプレッション Ze·σ0");
            Assert.IsTrue(mcrNWith > mcrNWithout, "引張負担の Mcr のほうが大きい");
        }
    }
}
