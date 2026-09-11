using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 地盤の質点 i が受け持つ深さの範囲 (質量の算定に使う) が、柱をすき間も重なりもなく
    /// 埋め、質点の位置 (層の上端 = 上の層厚の和) を中心にしていること。
    ///
    /// <para>質点 i (i ≥ 2) の範囲の上端を求めるループが <c>j &lt; i-2</c> になっていて、
    /// H_{i-2} を足し忘れていた。3 番目以降の質点の範囲が 1 小層ぶん上にずれ、
    /// 層ごとに密度が違うと質量が変わる。質量は地盤のモード形 (a1/a2 の深さ方向の
    /// 地盤変位の分布) と応答スペクトル法に効く。地表の地盤変位は U* = 1 なので変わらず、
    /// 地表だけを見る文献照合では出なかった (2026-09-11)。</para>
    /// </summary>
    [TestClass]
    public class GroundMassRegionTests
    {
        private static readonly double[] H = [0.8, 0.7, 1.0, 1.0, 1.1, 0.9];

        [TestMethod]
        public void RegionsTileTheColumnWithoutGapsOrOverlaps()
        {
            var (top0, _) = GroundLayerViewModel.MassRegion(H, 0);
            Assert.AreEqual(0.0, top0, 1e-12, "最初の質点の範囲は地表から始まる");

            for (int i = 0; i + 1 < H.Length; i++)
            {
                var (_, bottom) = GroundLayerViewModel.MassRegion(H, i);
                var (nextTop, _) = GroundLayerViewModel.MassRegion(H, i + 1);
                Assert.AreEqual(bottom, nextTop, 1e-12,
                    $"質点 {i} の下端と質点 {i + 1} の上端がずれています (すき間か重なり)");
            }
        }

        [TestMethod]
        public void EachRegionSpansHalfTheLayersAroundTheMass()
        {
            double depth = 0.0;   // 質点 i の深さ = 上の層厚の和 (地表 0、下向き負)
            for (int i = 0; i < H.Length; i++)
            {
                var (top, bottom) = GroundLayerViewModel.MassRegion(H, i);
                double above = i == 0 ? 0.0 : 0.5 * H[i - 1];
                Assert.AreEqual(depth + above, top, 1e-12, $"質点 {i} の範囲の上端");
                Assert.AreEqual(depth - 0.5 * H[i], bottom, 1e-12, $"質点 {i} の範囲の下端");
                depth -= H[i];
            }
        }
    }
}
