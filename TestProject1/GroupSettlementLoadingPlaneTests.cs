using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の荷重面Z は、基礎梁なし・ありで 1 つの値を使い、どちらの解析でも荷重面より上の土層を除く。
    ///
    /// 以前は解析ごとに別の荷重面を持ち、画面の 2 つのタブで別々に入力していた。
    /// しかも基礎梁を考える解析 (反復) は荷重面を計算に使っておらず、図にしか効いていなかった。
    /// </summary>
    [TestClass]
    public class GroupSettlementLoadingPlaneTests
    {
        /// <summary>荷重面を土層の中へ下げると、その上の土層を数えなくなり、反復解析の沈下が小さくなる。</summary>
        [TestMethod]
        public void TheIterativeAnalysis_OmitsTheLayersAboveTheLoadingPlane()
        {
            var (atTop, ppi1) = IterativeBeamSettlementInvariantTests.Scene();
            var (lowered, ppi2) = IterativeBeamSettlementInvariantTests.Scene();
            if (atTop == null || lowered == null) return;

            // 計算例5 の土層は荷重面 (−3 m) から始まる。荷重面を 1 層目の中 (−6 m) へ下げる
            lowered.PileGroupSettlement.LoadingPlaneAltitude = -6.0;
            var a = IterativeBeamSettlementService.Run(atTop, ppi1, "荷重面 −3 m");
            var b = IterativeBeamSettlementService.Run(lowered, ppi2, "荷重面 −6 m");
            Assert.IsTrue(a.Converged && b.Converged, "前提: どちらも収束する");

            double maxA = a.BeamSettlement.Values.Max(), maxB = b.BeamSettlement.Values.Max();
            Assert.IsTrue(maxB < maxA * 0.99,
                $"荷重面を下げても反復解析の沈下が減りません (−3 m: {maxA * 1000:F2} mm / −6 m: {maxB * 1000:F2} mm)。荷重面が計算に効いていません");

            string service = TestSource.Read("Graphics_r1", "Services", "IterativeBeamSettlementService.cs");
            StringAssert.Contains(service, "GetEffectiveLayersForAnalysis(", "反復解析が荷重面で土層を切り詰めていません");
            string vm = TestSource.Read("Graphics_r1", "ViewModels", "GroupSettlementWithBeamCalculationViewModel.cs");
            StringAssert.Contains(vm, "GetEffectiveLayersForAnalysis(", "反復解析のコンタが荷重面で土層を切り詰めていません");
        }

        /// <summary>解析ごとに違う荷重面を持つ旧ファイルは、基礎梁なし (一般) の値にそろえて知らせる。</summary>
        [TestMethod]
        public void OldFilesWithTwoPlanes_KeepTheNonBeamValue_AndSaySo()
        {
            var pgs = new PileGroupSettlement();
            // 旧ファイルの読み込みと同じ順 (宣言順: 現在値 → 一般 → 反復)
            pgs.LoadingPlaneAltitude = -2.0;
            pgs.LoadingPlaneAltitudeNonBeam = -2.5;
            pgs.LoadingPlaneAltitudeBeamAware = -3.0;

            var notices = LegacySettlementMigration.Apply(pgs);
            Assert.AreEqual(-2.5, pgs.LoadingPlaneAltitude, "基礎梁なし (一般) の値になっていません");
            Assert.AreEqual(-2.5, pgs.LoadingPlaneAltitudeNonBeam, "保存の互換の項目がそろっていません");
            Assert.AreEqual(-2.5, pgs.LoadingPlaneAltitudeBeamAware, "保存の互換の項目がそろっていません");
            Assert.IsTrue(notices.Any(n => n.Contains("-2.5 m にそろえ") && n.Contains("-3 m は使いません")),
                "違っていたことを知らせていません:\n" + string.Join("\n", notices));

            var same = new PileGroupSettlement { LoadingPlaneAltitude = -2.0 };
            same.LoadingPlaneAltitudeNonBeam = -2.0;
            same.LoadingPlaneAltitudeBeamAware = -2.0;
            Assert.IsFalse(LegacySettlementMigration.Apply(same).Any(n => n.Contains("荷重面Z")), "同じ値なのに知らせています");
        }

        /// <summary>画面の荷重面Z は 1 つの値 (LoadingPlaneAltitude) を編集する。解析ごとの値を編集する入口は残さない。</summary>
        [TestMethod]
        public void TheWindow_EditsOneLoadingPlane()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "GroupSettlementWindow.xaml");
            Assert.IsFalse(xaml.Contains("LoadingPlaneAltitudeNonBeamProxy") || xaml.Contains("LoadingPlaneAltitudeBeamAwareProxy"),
                "解析ごとの荷重面を編集する欄が残っています");
            StringAssert.Contains(xaml, "Binding LoadingPlaneAltitudeProxy");
        }
    }
}
