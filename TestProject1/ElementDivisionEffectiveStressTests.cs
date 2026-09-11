using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭要素分割ウィンドウの kh0・py の表示が、解析と同じ有効応力を使っていること。
    ///
    /// <para>有効応力の水圧は「応力計算面より下の水柱だけ」を引く。解析側
    /// (<c>SoilPile.GetEffectiveStress</c>) は以前この形に直したが、ウィンドウは同じ関数の
    /// 写しを持っていて取り残されていた。地下水位が応力計算面より上にあると
    /// (地表 0・応力計算面 -3・地下水位 -1・γ=18、z=-3.5)、解析は σ' = 4、ウィンドウは -16 になり、
    /// 畑中式の内部摩擦角が NaN になって受働土圧の表示が静かに NaN になっていた。
    /// 2026-09-11 に写しを消して解析の関数を呼ぶようにした。</para>
    /// </summary>
    [TestClass]
    public class ElementDivisionEffectiveStressTests
    {
        [TestMethod]
        public void TheWindowUsesTheAnalysisEffectiveStress()
        {
            string src = Regex.Replace(
                TestSource.Read("Graphics_r1", "ViewModels", "ElementDivisionViewModel.cs"), "//.*", "");

            Assert.IsFalse(Regex.IsMatch(src, @"double\s+GetEffectiveStress\s*\("),
                "杭要素分割ウィンドウが有効応力の計算の写しを持っています (解析の関数を呼ぶこと)");
            Assert.IsTrue(Regex.Matches(src, @"SoilPile\.GetEffectiveStress\s*\(").Count >= 2,
                "杭要素分割ウィンドウが解析の有効応力 (SoilPile.GetEffectiveStress) を呼んでいません");
        }

        [DataTestMethod]
        // 地下水位が応力計算面より上: 水圧は応力計算面から下だけ (0.5×18 − 0.5×10 = 4)。写しは -16 だった
        [DataRow(-1.0, -3.5, 4.0)]
        // 地下水位が応力計算面より下: 土被り 5×18 − 水柱 3×10 = 60
        [DataRow(-5.0, -8.0, 60.0)]
        // 地下水位より上: 水圧を引かない (1×18)
        [DataRow(-5.0, -4.0, 18.0)]
        public void TheAnalysisEffectiveStress(double waterTable, double z, double expected)
        {
            var ground = new GroundInput
            {
                GroundTopAltitude = 0.0,
                StressAltitude = -3.0,
                GroundWaterTableAltitude = waterTable,
            };
            ground.GroundLayers = [new GroundLayerInput { BottomAltitude = -20.0, LayerThickness = 20.0, Density = 18.0 }];

            Assert.AreEqual(expected, SoilPile.GetEffectiveStress(ground, z), 1e-9);
        }
    }
}
