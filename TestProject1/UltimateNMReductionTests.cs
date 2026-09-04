using System.Linq;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 安全限界 N-M の低減後曲線は、耐力の求め方 (バイリニア / e 関数) によらず
    /// 低減率 β と軸力制限を課すこと。
    ///
    /// 以前は e 関数のとき「低減後 = 低減前」(同一インスタンス) にしており、
    /// 2 本が完全に重なるため画面では低減後が描かれていないようにしか見えなかった。
    /// 耐力の算定式と低減の扱いは別の話なので、算定式で低減の有無が変わってはいけない。
    /// </summary>
    [TestClass]
    public class UltimateNMReductionTests
    {
        private static PileSection CreateInsituRcSection() => new()
        {
            PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
            PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
            ConcreteOutDia = 1000.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 20,
            MainBarSize = "D25",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1000.0,
        };

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.UseUnitGsiForConcreteE = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
        }

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        [TestMethod]
        public void FactoredUltimateNM_IsReduced_WithBothConcreteLaws()
        {
            foreach (bool eFunction in new[] { false, true })
            {
                ResetOptions();
                ConcreteModelOptions.UseInsituUltimateEFunction = eFunction;

                var s = CreateInsituRcSection();
                var unfactored = s.UnfactoredUltimateNM;
                var factored = s.FactoredUltimateNM;

                string law = eFunction ? "e 関数型" : "バイリニア型";

                Assert.IsTrue(unfactored.M.Count > 0 && factored.M.Count > 0, $"{law}: 曲線が空");

                double maxUnf = unfactored.M.Max();
                double maxFac = factored.M.Max();
                Assert.IsTrue(maxUnf > 100.0, $"{law}: 低減前の耐力が小さすぎる ({maxUnf:F0})");

                Assert.IsTrue(maxFac < maxUnf * 0.999,
                    $"{law}: 低減後が低減前と同じ ({maxFac:F1} / {maxUnf:F1})。"
                    + "低減率と軸力制限が課されていない (2 本が重なって「低減後が無い」ように見える)。");
            }
        }

        /// <summary>
        /// 低減後の曲線は軸力制限で切り詰められる (制限の外で M=0 の点を持つ)。
        /// e 関数でも同じ形になること。
        /// </summary>
        [TestMethod]
        public void FactoredUltimateNM_IsClippedByAxialLimits_WithBothConcreteLaws()
        {
            foreach (bool eFunction in new[] { false, true })
            {
                ResetOptions();
                ConcreteModelOptions.UseInsituUltimateEFunction = eFunction;

                var s = CreateInsituRcSection();
                var factored = s.FactoredUltimateNM;
                string law = eFunction ? "e 関数型" : "バイリニア型";

                Assert.IsTrue(factored.M.Any(m => m <= 1e-9),
                    $"{law}: 低減後の曲線に M=0 の点が無い (軸力制限で切り詰められていない)");
            }
        }
    }
}
