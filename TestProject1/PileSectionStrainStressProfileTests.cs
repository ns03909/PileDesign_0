using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち鉄筋コンクリート杭断面の「ひずみ度・応力度分布」生成
    /// (InsituReinforcedConcreteSection.GetStrainStressProfile) の単体テスト。
    /// 計算例10 と同じ断面 (D=1500 / Fc=27 / 30-D29 SD390) を題材とする。
    /// プロファイルは材料ごとの MaterialProfile (コンクリート/主筋) として返る。
    /// </summary>
    [TestClass]
    public class PileSectionStrainStressProfileTests
    {
        private static InsituReinforcedConcreteSection CreateSection()
        {
            var concrete = new InsituConcrete(1500.0, 1.0, 27.0);
            // PCD = D - 2×かぶり = 1500 - 2×200 = 1100
            var bars = new MainBars(1100.0, 30, "SD390", "D29");
            return new InsituReinforcedConcreteSection(concrete, bars);
        }

        private static MaterialProfile Concrete(SectionStrainStressProfile p)
            => p.Materials.First(m => m.Kind == SectionMaterialKind.Concrete);

        private static MaterialProfile Bar(SectionStrainStressProfile p)
            => p.Materials.First(m => m.Kind == SectionMaterialKind.MainBar);

        [TestMethod]
        public void Profile_StrainIsLinear_CompressionEdgeAtTop()
        {
            var section = CreateSection();
            double r = 750.0;

            double epsC = 0.003;
            double phi = epsC / r;
            var p = section.GetStrainStressProfile(epsC, phi, ultimate: true);

            var c = Concrete(p);
            Assert.AreEqual(-r, c.Z[0], 1e-6, "先頭 z は圧縮縁 -R");
            Assert.AreEqual(r, c.Z[^1], 1e-6, "末尾 z は引張縁 +R");

            Assert.AreEqual(epsC, p.CompressionEdgeStrain, 1e-9, "圧縮縁ひずみ = εc");
            Assert.IsTrue(p.CompressionEdgeStrain > p.TensionEdgeStrain,
                $"圧縮縁({p.CompressionEdgeStrain}) > 引張縁({p.TensionEdgeStrain}) のはず");

            // ひずみは線形: 先頭=圧縮縁, 末尾=引張縁, 中央は平均
            Assert.AreEqual(p.CompressionEdgeStrain, c.Strain[0], 1e-9);
            Assert.AreEqual(p.TensionEdgeStrain, c.Strain[^1], 1e-9);
            int mid = c.Strain.Count / 2;
            double expectedMid = 0.5 * (c.Strain[0] + c.Strain[^1]);
            Assert.AreEqual(expectedMid, c.Strain[mid], 1e-6, "ひずみ分布は直線");
        }

        [TestMethod]
        public void Profile_ConcreteCompressionPositive_TensionZero()
        {
            var section = CreateSection();
            double r = 750.0;
            double epsC = 0.003;
            double phi = epsC / r;
            var p = section.GetStrainStressProfile(epsC, phi, ultimate: true);
            var c = Concrete(p);

            Assert.IsTrue(c.Stress[0] > 0, $"圧縮縁応力 {c.Stress[0]} は正のはず");
            Assert.AreEqual(0.0, c.Stress[^1], 1e-9, "引張縁コンクリート応力は 0");

            // 圧縮側はバイリニア上限 ξ·Fc=27 を超えない
            foreach (var s in c.Stress)
                Assert.IsTrue(s <= 27.0 + 1e-6, $"コンクリート応力 {s} が ξFc=27 を超過");
        }

        [TestMethod]
        public void Profile_Bars_CompressionAndTensionFibers()
        {
            var section = CreateSection();
            double epsC = 0.003;
            double phi = epsC / 750.0;
            var p = section.GetStrainStressProfile(epsC, phi, ultimate: true);
            var bar = Bar(p);

            // 主筋はリング範囲 (-PCD/2 〜 +PCD/2) を連続サンプリングしたライン
            Assert.IsTrue(bar.Z.Count >= 2, "主筋ラインが生成される");
            Assert.AreEqual(-550.0, bar.Z[0], 1e-6, "圧縮側主筋 z=-PCD/2");
            Assert.AreEqual(550.0, bar.Z[^1], 1e-6, "引張側主筋 z=+PCD/2");
            Assert.IsTrue(bar.Strain[0] > bar.Strain[^1], "圧縮側主筋ひずみ > 引張側主筋ひずみ");
            Assert.IsTrue(bar.Stress[^1] < 0, $"引張側主筋応力 {bar.Stress[^1]} は負(引張)のはず");
        }

        [TestMethod]
        public void Profile_FromUltimateNMPoint_DoesNotThrow_AndConsistent()
        {
            var section = CreateSection();
            var (Ns, Ms, Eps, Phi) = section.UnfactoredUltimateNM;
            Assert.IsTrue(Ns.Count > 2, "安全限界NM曲線が空でない");

            int i = Ns.Count / 2;
            var p = section.GetStrainStressProfile(Eps[i], Phi[i], ultimate: true);
            Assert.IsTrue(p.Materials.Count >= 2, "コンクリート＋主筋の材料プロファイル");
            var c = Concrete(p);
            Assert.AreEqual(c.Z.Count, c.Strain.Count);
            Assert.AreEqual(c.Z.Count, c.Stress.Count);
            Assert.IsTrue(c.Z.Count > 10);
        }

        [TestMethod]
        public void ProfileSourceCurves_IncludeCrackAndYield()
        {
            var section = CreateSection();
            var names = section.GetProfileSourceCurves().Select(x => x.Name).ToList();
            Assert.IsTrue(names.Contains("ひび割れ開始"), "ひび割れ開始曲線が含まれる");
            Assert.IsTrue(names.Contains("引張鉄筋降伏開始"), "引張鉄筋降伏開始曲線が含まれる");
            Assert.IsTrue(names.Contains("(低減後)安全限界"), "低減後安全限界が含まれる");
        }
    }
}
