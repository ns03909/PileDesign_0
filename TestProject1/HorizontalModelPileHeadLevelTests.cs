using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 水平解析のモデル (節点・水平地盤ばね) が、杭頭レベルの複数ある配置でも、杭ごとに正しい高さに組まれること。
    ///
    /// 検定比が平行移動で変わらないこと (<see cref="AnalysisTranslationInvarianceTests"/>) は、全体の取り違えは捕まえるが、
    /// 「杭 A の節点だけ別の杭頭の高さで組む」ような杭ごとの食い違いは、検定比の差に埋もれうる。
    /// ここでは解析モデルの節点の高さを、杭自身の杭頭Z・杭長・土層-杭セットと直接突き合わせる。
    /// </summary>
    [TestClass]
    public class HorizontalModelPileHeadLevelTests
    {
        private const double Tol = 1e-6;

        [TestMethod]
        public void EachPile_IsModelledBetweenItsOwnHeadAndTip_AtItsOwnGroundLevels()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            PileHeadLevelTests.TwoPileHeadLevels(input);

            var modelling = new AnalysisModelling(input);
            var problems = new List<string>();
            int checkedPiles = 0;

            Assert.AreEqual(2, input.PileLayoutItems.Select(p => Math.Round(p.PileHeadZ, 6)).Distinct().Count(),
                "(前提) 杭頭レベルが 2 つになっていません");

            foreach (var pile in input.PileLayoutItems)
            {
                var soilPile = pile.SoilPileAt(input);
                if (soilPile == null) { problems.Add($"杭{pile.No}: 土層-杭セットが無い"); continue; }
                var body = input.PileBodyAt(pile.PileBodyNo)!;
                double length = body.PileBodySegments.Sum(s => s.SegmentLength);

                if (Math.Abs(soilPile.Z - pile.PileHeadZ) > Tol)
                    problems.Add($"杭{pile.No}: 土層-杭セットの杭頭Z {soilPile.Z:F3} が杭の杭頭Z {pile.PileHeadZ:F3} と違う");
                if (soilPile.GroundNo != pile.GroundNo || soilPile.PileBodyNo != pile.PileBodyNo)
                    problems.Add($"杭{pile.No}: 土層-杭セットの地盤/杭体が違う");

                var zs = pile.PileNodes.Select(n => n.Coord.Z).ToList();
                if (zs.Count < 2) { problems.Add($"杭{pile.No}: 杭の節点が組まれていない"); continue; }
                checkedPiles++;

                if (Math.Abs(zs[0] - pile.PileHeadZ) > Tol)
                    problems.Add($"杭{pile.No}: 杭頭の節点 Z {zs[0]:F3} が杭頭Z {pile.PileHeadZ:F3} と違う");
                if (Math.Abs(zs[^1] - (pile.PileHeadZ - length)) > Tol)
                    problems.Add($"杭{pile.No}: 杭先端の節点 Z {zs[^1]:F3} が 杭頭Z − 杭長 = {pile.PileHeadZ - length:F3} と違う");
                for (int i = 1; i < zs.Count; i++)
                    if (!(zs[i] < zs[i - 1])) { problems.Add($"杭{pile.No}: 節点の高さが下へ単調でない (i={i})"); break; }

                // 節点は土層-杭セットの高さの並びそのもの (重複で飛ばした分だけ少なくなりうる)
                var expected = soilPile.ZDataItems.Select(d => d.Z).ToList();
                foreach (var z in zs)
                    if (!expected.Any(e => Math.Abs(e - z) <= Tol))
                        problems.Add($"杭{pile.No}: 節点 Z {z:F3} が土層-杭セットの高さに無い");

                // 地盤側の節点・水平ばねは、同じ杭の同じ高さに付く
                if (pile.SoilNodes.Count != pile.PileNodes.Count)
                    problems.Add($"杭{pile.No}: 地盤側の節点数 {pile.SoilNodes.Count} が杭の節点数 {pile.PileNodes.Count} と違う");
                else
                    for (int i = 0; i < zs.Count; i++)
                        if (Math.Abs(pile.SoilNodes[i].Coord.Z - zs[i]) > Tol)
                            problems.Add($"杭{pile.No}: 地盤側の節点 {i} の Z {pile.SoilNodes[i].Coord.Z:F3} が杭の節点 {zs[i]:F3} と違う");

                // 杭の範囲は、使っている地盤の範囲の内に収まる (別の地盤・別の杭頭の高さで組むと外れる)
                var ground = input.GroundAt(pile.GroundNo)!;
                double gBottom = ground.GroundLayers.Min(l => l.BottomAltitude);
                double gTop = ground.GroundLayers.Max(l => l.BottomAltitude + l.LayerThickness);
                if (zs[0] > gTop + Tol || zs[^1] < gBottom - Tol)
                    problems.Add($"杭{pile.No}: 杭の範囲 [{zs[^1]:F2}, {zs[0]:F2}] が地盤 [{gBottom:F2}, {gTop:F2}] の外");
            }

            TestSource.AssertScanned(checkedPiles, 12, "確かめた杭");
            Assert.AreEqual(0, problems.Count,
                "杭頭レベルが複数あるモデルで、水平解析のモデルの高さが杭と合いません:\n  " + string.Join("\n  ", problems.Take(20)));

            // 水平ばねは、杭の節点と地盤側の節点を結ぶ
            var pileNodeSet = new HashSet<Node>(input.PileLayoutItems.SelectMany(p => p.PileNodes), ReferenceEqualityComparer.Instance);
            var soilNodeSet = new HashSet<Node>(input.PileLayoutItems.SelectMany(p => p.SoilNodes), ReferenceEqualityComparer.Instance);
            var wrongSprings = modelling.HorizontalSoilSprings
                .Where(s => !(pileNodeSet.Contains(s.NodeI) && soilNodeSet.Contains(s.NodeJ)
                              && Math.Abs(s.NodeI.Coord.Z - s.NodeJ.Coord.Z) <= Tol))
                .Select(s => s.Name).Take(10).ToList();
            Assert.AreEqual(0, wrongSprings.Count, "杭と地盤の高さが違う水平ばねがあります: " + string.Join(", ", wrongSprings));
        }
    }
}
