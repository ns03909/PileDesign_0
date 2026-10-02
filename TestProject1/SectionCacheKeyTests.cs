using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 解析モデルの要素の断面 (Section) は、その杭の断面の値そのもの。
    ///
    /// <para>断面は同じ値のものを使い回すが、以前は使い回しの鍵を m²・m⁴ のまま小数 5 桁に丸めていた。
    /// 細い杭の I は 3〜10×10⁻⁴ m⁴ なので、1〜3% 違う断面が同じ鍵になりうる (PHC-300 の B 種と C 種は I の鍵が同じ)。
    /// 鍵が同じだと、あとの杭が先に作った断面の剛性で黙って解かれる。</para>
    /// </summary>
    [TestClass]
    public class SectionCacheKeyTests
    {
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_4", "PileExample3_4")]   // SC 杭 + PHC 杭
        [DataRow("Example3_8_1", "PileExample3_8")] // 杭体 9 種
        public void EveryPileElement_HasItsOwnSectionValues(string ground, string pile)
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel(ground, pile);
            if (input == null) { Assert.Inconclusive($"例題ロード失敗: {error}"); return; }
            var model = new AnalysisModelling(input);

            int checkedBeams = 0;
            foreach (var beam in model.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg) continue;
                var section = input.ElementDivision.SoilPiles.First(s => s.PileBodyNo == pb).PileBodySegments[seg].PileSection;
                double e = beam.Section.Material.E;
                Assert.AreEqual(section.EI / e, beam.Section.IZ, 1e-12 * Math.Abs(section.EI / e), $"杭体 {pb} 要素 {seg}: 断面二次モーメント");
                Assert.AreEqual(section.EA / e, beam.Section.AX, 1e-12 * Math.Abs(section.EA / e), $"杭体 {pb} 要素 {seg}: 断面積");
                checkedBeams++;
            }
            TestSource.AssertScanned(checkedBeams, 50, "杭の要素");
        }

        /// <summary>
        /// 断面の使い回しの鍵は値そのもの (丸めると、違う断面が同じ鍵になる)。材料は材料そのものを鍵にする
        /// (E だけだと、ポアソン比だけ違う梁が先に作った材料で解かれる)。
        /// 基礎梁の鉛直の解析 (VerticalBeamModelling) は AnalysisModelling の写しで、こちらだけ丸めたまま残っていた。
        /// </summary>
        [DataTestMethod]
        [DataRow("AnalysisModelling.cs", 2)]
        [DataRow("VerticalBeamModelling.cs", 1)]
        public void SectionCacheKeys_AreNotRounded_AndHoldTheMaterial(string file, int expected)
        {
            string src = TestSource.Read("Graphics_r1", "FEM", file);
            var keys = Regex.Matches(src, @"var sectionKey = \(([^;]*)\);");
            TestSource.AssertScanned(keys.Count, expected, "断面の使い回しの鍵");
            foreach (Match k in keys)
            {
                Assert.IsFalse(k.Groups[1].Value.Contains("Math.Round"), $"{file}: 断面の使い回しの鍵を丸めています: " + k.Value);
                StringAssert.StartsWith(k.Groups[1].Value.Trim(), "material", $"{file}: 断面の使い回しの鍵に材料が入っていません: " + k.Value);
            }
        }
    }
}
