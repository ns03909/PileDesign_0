using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 主筋の配置直径が、保存して開き直しても縮まないこと。
    ///
    /// 場所打ちRC の断面では、画面の入力欄は<b>重心かぶり厚</b>で、配置直径は
    /// <c>ConcreteOutDia - 2·かぶり厚</c> で導かれる。ところが例題データと保存ファイルは
    /// 配置直径を<b>直接</b>持つ。かぶり厚を揃えずに配置直径だけ入れると、
    /// 読み込みの仕上げ (<c>OnDeserialized</c> → <c>RecalculatePileDia</c>) が
    /// 既定のかぶり厚 200mm から導いた値で塗り潰す。
    ///
    /// 実際に例題 5 件がこれで、基礎指針 計算例9 (φ1000) は配置直径 700 が 600 になり、
    /// <b>開き直すと安全限界の曲げ耐力が下がっていた。</b>
    /// 例題 5 件はいずれもかぶり厚 150mm 相当なので、既定の 200mm との差が効いていた。
    ///
    /// この不具合は<b>テストのビルダーが例題の断面を読むようになって初めて見えた</b>。
    /// それまでは断面の既定値で解析していたため、配置直径が例題の値になっておらず、
    /// 塗り潰しも起きなかった。
    /// </summary>
    [TestClass]
    public class MainBarPlacementRoundTripTests
    {
        /// <summary>
        /// 配置直径を入れたら、かぶり厚がそれに合うこと。
        /// この 2 つが食い違っていると、再計算のたびに配置直径が動く。
        /// </summary>
        [TestMethod]
        public void SettingThePlacementDiameter_SyncsTheCover()
        {
            var section = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
                PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
            };
            section.ConcreteOutDia = 1000.0;
            section.MainBarDr = 700.0;

            Assert.AreEqual(150.0, section.MainBarCenterCover, 1e-9,
                "配置直径を入れてもかぶり厚が合いません。"
                + "再計算 (ConcreteOutDia - 2·かぶり厚) で配置直径が塗り潰されます");

            // 再計算しても配置直径が動かないこと (これが本題)
            section.RecalculatePileDia();
            Assert.AreEqual(700.0, section.MainBarDr, 1e-9,
                "再計算で主筋の配置直径が変わりました。曲げ耐力が静かに下がります");
        }

        /// <summary>
        /// かぶり厚を入れる従来の向きが壊れていないこと。
        /// 画面の入力欄はこちらなので、こちらが正。
        /// </summary>
        [TestMethod]
        public void SettingTheCover_StillDerivesThePlacementDiameter()
        {
            var section = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
                PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
            };
            section.ConcreteOutDia = 1000.0;
            section.MainBarCenterCover = 120.0;

            Assert.AreEqual(760.0, section.MainBarDr, 1e-9,
                "かぶり厚から配置直径が導かれていません");
        }

        /// <summary>
        /// 例題の場所打ちRC 断面が、かぶり厚と配置直径で食い違っていないこと。
        ///
        /// 例題 JSON は配置直径だけを持つので、読み込んだあとに
        /// <c>(外径 - 配置直径)/2 == かぶり厚</c> が成り立っていなければ、
        /// どこかの再計算で配置直径が動く。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example10", "PileExample10")]
        [DataRow("ExampleK8", "PileExampleK8")]
        [DataRow("Example3_3", "PileExample3_3")]
        [DataRow("Example7", "PileExample7")]
        public void TheExamples_AreSelfConsistent(string groundName, string pileName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            var rcSections = model.PileBodies
                .SelectMany(pb => pb.PileBodySegments)
                .Select(seg => seg.PileSection)
                .Where(sec => sec != null
                    && sec.PileSectionType == PileDesign.Constants.PileTypeNames.RcSection
                    && sec.ConcreteOutDia > 0)
                .ToList();

            TestSource.AssertScanned(rcSections.Count, 1, $"{groundName} の鉄筋コンクリート断面");

            foreach (var sec in rcSections)
            {
                double expectedCover = (sec!.ConcreteOutDia - sec.MainBarDr) * 0.5;
                Assert.AreEqual(expectedCover, sec.MainBarCenterCover, 1e-9,
                    $"{groundName}: 外径 {sec.ConcreteOutDia}・配置直径 {sec.MainBarDr} に対し "
                    + $"かぶり厚が {sec.MainBarCenterCover} で食い違っています。"
                    + "再計算で配置直径が動きます");
            }
        }
    }
}
