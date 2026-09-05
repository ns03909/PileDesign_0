using System.Linq;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 材料のモデル化オプション (静的) を変えたら、<b>生きているすべての断面</b>の曲線が追随すること。
    ///
    /// 以前は入力モデル (<c>CurrentInputModel.PileBodies</c>) をたどってキャッシュを捨てて回っていた。
    /// ところが杭体ウィンドウは <c>PileBodies</c> の<b>複製</b>を編集しており、
    /// 杭断面ウィンドウはその複製側の断面を受け取る。走査から漏れるので、
    /// オプションを変えても前の設定で計算済みの曲線が描かれ続けていた。
    ///
    /// いまは断面が<b>使うときに自分で古さを判定する</b> (<c>ConcreteModelOptions.Version</c>)。
    /// どのモデルにぶら下がっているかに依存しないので、ダイアログを増やしても破れない。
    /// </summary>
    [TestClass]
    public class MaterialOptionCacheTests
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

        /// <summary>
        /// どの入力モデルにも属さない断面 (＝ダイアログが編集している複製と同じ立場) でも、
        /// オプションを変えたら曲線が変わること。
        ///
        /// <b>キャッシュを捨てて回る仕組みだけに頼っていると、ここが落ちる。</b>
        /// </summary>
        [TestMethod]
        public void ACopyNotOwnedByAnyInputModel_FollowsTheOptions()
        {
            ResetOptions();
            var section = CreateInsituRcSection();

            // まず今の設定で曲線を作らせる (キャッシュに載る)
            double before = section.FactoredUltimateNM.M.Max();
            Assert.IsTrue(before > 100.0, $"前提: 耐力が小さすぎる ({before:F0})");

            // 誰もこの断面のキャッシュを捨てに来ない状態でオプションを変える
            ConcreteModelOptions.UseReducedCompression = true;

            double after = section.FactoredUltimateNM.M.Max();
            Assert.AreNotEqual(before, after, 1e-6,
                "オプションを変えても曲線が変わりません "
                + "(前の設定で計算済みのキャッシュが返っています)");
        }

        /// <summary>使用・損傷限界のせん断 (N-Q) も同じ扱いであること。</summary>
        [TestMethod]
        public void ShearCurves_FollowTheOptionsToo()
        {
            ResetOptions();
            var section = CreateInsituRcSection();

            double before = section.FactoredDamageNQ.Q.Max();
            Assert.IsTrue(before > 0.0, "前提: せん断耐力が 0");

            // 告示1113 の許容せん断に切り替えると値が変わる
            ConcreteModelOptions.UseNotification1113Shear = true;

            double after = section.FactoredDamageNQ.Q.Max();
            Assert.AreNotEqual(before, after, 1e-6,
                "せん断の曲線がオプションに追随していません");
        }

        /// <summary>
        /// 版数はオプションが実際に変わったときだけ増えること。
        /// 同じ値を書き込むたびに増えると、曲線を読むたびに再計算されて重くなる。
        /// </summary>
        [TestMethod]
        public void Version_ChangesOnlyWhenAnOptionActuallyChanges()
        {
            ResetOptions();
            int v0 = ConcreteModelOptions.Version;

            ConcreteModelOptions.IgnoreTensileStrength = false;   // 同じ値
            Assert.AreEqual(v0, ConcreteModelOptions.Version, "同じ値の書き込みで版数が増えている");

            ConcreteModelOptions.IgnoreTensileStrength = true;
            Assert.AreNotEqual(v0, ConcreteModelOptions.Version, "変更が版数に出ていない");

            int v1 = ConcreteModelOptions.Version;
            ConcreteModelOptions.Notification1113CompressionCase = 2;
            Assert.AreNotEqual(v1, ConcreteModelOptions.Version, "int のオプションが版数に出ていない");

            ConcreteModelOptions.Notification1113CompressionCase = 1;
        }
    }
}
