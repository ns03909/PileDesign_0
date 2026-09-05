using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 材料の限界ひずみ度は<b>典拠のある値</b>であること。
    ///
    /// これらは安全限界 N-M と M-φ の終点を決める。書き換えると耐力が静かに動くので、
    /// 出典と値をここで固定しておく。
    /// </summary>
    [TestClass]
    public class MaterialLimitStrainTests
    {
        /// <summary>
        /// PC鋼材の安全限界引張ひずみ度は -0.02。
        /// 出典:「基礎部材の強度と変形性能」6.2.3。
        /// </summary>
        [TestMethod]
        public void Tendon_UltimateTensileStrain_Is_Minus0_02()
        {
            var tendons = new Tendons(_PCD: 400.0, _ap: 100.0);

            Assert.AreEqual(0.02, Tendons.DefaultEpsilonPu, 1e-12,
                "PC鋼材の安全限界引張ひずみ度の既定値が変わっています "
                + "(基礎部材の強度と変形性能 6.2.3 は 0.02)");
            Assert.AreEqual(-0.02, tendons.UltimateLimitStrainT, 1e-12,
                "安全限界引張ひずみ度が -εpu になっていません "
                + "(頭打ちが効かないと PHC/PRC の安全限界 N-M が過大になる)");
        }

        /// <summary>
        /// 圧縮側は頭打ちしない (PC鋼材は圧縮で限界状態を決めない)。
        /// 引張の使用・損傷限界も同様で、PC鋼材が決めるのは安全限界だけ。
        /// </summary>
        [TestMethod]
        public void Tendon_OnlyTheUltimateTensileSideIsCapped()
        {
            var tendons = new Tendons(_PCD: 400.0, _ap: 100.0);

            Assert.AreEqual(double.MaxValue, tendons.UltimateLimitStrainC,
                "圧縮側に上限が入っています");
            Assert.AreEqual(double.MinValue, tendons.ServiceLimitStrainT,
                "使用限界の引張に上限が入っています");
            Assert.AreEqual(double.MinValue, tendons.DamageLimitStrainT,
                "損傷限界の引張に上限が入っています");
        }
    }
}
