using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>地層境界・地下水位と杭頭／杭区間境界の一致、その直上・直下を独立な積分と照合する。</summary>
    [TestClass]
    public class HorizontalLayerBoundaryTests
    {
        [DataTestMethod]
        [DataRow(-0.0001)]
        [DataRow(-0.000005)]
        [DataRow(0.0)]
        [DataRow(0.000005)]
        [DataRow(0.0001)]
        public void LayerAndWaterBoundary_AssignEachIntervalAndStressCorrectly(double offset)
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            Assert.IsNotNull(input, "例題を読み込めません: " + error);
            double water = -5 + offset;
            var ground = new GroundInput
            {
                GroundTopAltitude = 0,
                GroundWaterGLDepth = water,
                StressGLDepth = 0,
                GroundLayers =
                [
                    new GroundLayerInput { No = 1, Name = "上層", BottomGLDepth = -5,
                        Density = 18, Es = 10000, NValue = 10 },
                    new GroundLayerInput { No = 2, Name = "下層", BottomGLDepth = -80,
                        Density = 20, Es = 40000, NValue = 30 }
                ]
            };
            ground.SyncAltitudesFromGLDepths();
            input.GroundsInput[0] = ground;
            var body = input.PileBodies[0];
            double length = body.PileBodySegments.Sum(s => s.SegmentLength);
            var upper = body.PileBodySegments[0].DeepCopy();
            var lower = upper.DeepCopy();
            upper.No = 1;
            upper.SegmentLength = upper.SegmentDepth = 3 - offset;
            lower.No = 2;
            lower.SegmentLength = length - upper.SegmentLength;
            lower.SegmentDepth = length;
            body.PileBodySegments = [upper, lower];
            for (int i = 0; i < input.PileLayoutItems.Count; i++)
            {
                var pile = input.PileLayoutItems[i];
                double head = i % 2 == 0 ? -2 : -5 + offset;
                pile.Z = head + pile.FoundationBeamDeltaZc;
            }
            input.GenerateSoilPiles();
            Assert.AreEqual(2, input.ElementDivision.SoilPiles.Count, "杭頭２水準の前提が成立していません");

            // 地表から５ｍは γ=18、その下は γ=20。地下水位以下の水圧を控除する。
            double Stress(double z) => 18 * Math.Min(-z, 5) + 20 * Math.Max(-z - 5, 0)
                - 10 * Math.Max(water - z, 0);
            foreach (double z in new[] { -5 - 0.0001, -5.0, -5 + 0.0001, water, -10.0 })
                Assert.AreEqual(Stress(z), SoilPile.GetEffectiveStress(ground, z), 1e-9,
                    $"水位{water:F4}/Z={z:F4}: 地層境界・水位の直上／直下の有効応力");

            foreach (var sp in input.ElementDivision.SoilPiles)
            {
                var zs = sp.ZDataItems.Select(d => d.Z).ToArray();
                Assert.AreEqual(sp.Z, zs[0], 1e-9, "杭頭が欠けています");
                Assert.AreEqual(sp.Z - length, zs[^1], 1e-9, "杭先端が欠けています");
                Assert.AreEqual(zs.Length - 1, sp.GroundLayers.Count, "地層の対応が欠けています");
                Assert.AreEqual(zs.Length - 1, sp.HorizontalSoilReactions.Count, "ばね区間が欠けています");
                if (sp.Z > -5)
                    Assert.IsTrue(zs.Any(z => Math.Abs(z + 5) <= 1e-9), "杭が横切る地層境界が節点にありません");
                double integratedStiffness = 0;
                for (int i = 0; i < zs.Length - 1; i++)
                {
                    Assert.IsTrue(zs[i] > zs[i + 1], "ゼロ長または逆向きの区間があります");
                    var expected = (zs[i] + zs[i + 1]) / 2 > -5 ? ground.GroundLayers[0] : ground.GroundLayers[1];
                    Assert.AreSame(expected, sp.GroundLayers[i], $"杭頭{sp.Z:F4}/区間{i}: 別の層が対応しています");
                    var r = sp.HorizontalSoilReactions[i];
                    Assert.AreEqual(expected.Name, r.Name, $"区間{i}: ばねの地層が違います");
                    Assert.AreEqual(expected.Es, r.E0, 1e-9, $"区間{i}: ばねの地盤剛性が違います");
                    Assert.AreEqual(Stress(zs[i]), r.SigmaZPrimeTop, 1e-9, $"区間{i}: 上端有効応力");
                    Assert.AreEqual(Stress(zs[i + 1]), r.SigmaZPrimeBtm, 1e-9, $"区間{i}: 下端有効応力");
                    integratedStiffness += r.GetSoilSecantReactionCoefficient(0, true, true,
                        GroupPileEffect.None, SoilNonlinearityMode.Linear)
                        + r.GetSoilSecantReactionCoefficient(0, false, true,
                            GroupPileEffect.None, SoilNonlinearityMode.Linear);
                }
                double b = upper.PileSection.PileDiameter / 1000;
                double upperLength = Math.Max(sp.Z + 5, 0);
                double lowerLength = length - upperLength;
                double khUpper = 80 * 10000 * Math.Pow(b / 0.01, -0.75);
                double khLower = 80 * 40000 * Math.Pow(b / 0.01, -0.75);
                double expectedStiffness = b * (khUpper * upperLength + khLower * lowerLength);
                Assert.AreEqual(expectedStiffness, integratedStiffness, expectedStiffness * 1e-9,
                    "境界付近の区間の欠落・重複、または別層の剛性が含まれています");
            }
        }

        /// <summary>
        /// 杭区間の境と地層境界が許容誤差 (1e-5 m) より小さくずれても、境のすぐ下の微小な区間のばねは、その区間が実際に
        /// 収まる杭区間の杭径で作る。土層の検索と同じ型の誤りで、以前は許容誤差の内で先に見つかる上の杭区間の杭径を当てていた。
        /// </summary>
        [DataTestMethod]
        [DataRow(-0.000005)]
        [DataRow(0.000005)]
        public void PileSegmentBoundary_AssignsEachIntervalTheDiameterOfItsOwnSegment(double offset)
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            Assert.IsNotNull(input, "例題を読み込めません: " + error);
            var ground = new GroundInput
            {
                GroundTopAltitude = 0,
                GroundWaterGLDepth = -80,
                StressGLDepth = 0,
                GroundLayers =
                [
                    new GroundLayerInput { No = 1, Name = "上層", BottomGLDepth = -5, Density = 18, Es = 10000, NValue = 10 },
                    new GroundLayerInput { No = 2, Name = "下層", BottomGLDepth = -80, Density = 20, Es = 40000, NValue = 30 }
                ]
            };
            ground.SyncAltitudesFromGLDepths();
            input.GroundsInput[0] = ground;
            var body = input.PileBodies[0];
            double length = body.PileBodySegments.Sum(s => s.SegmentLength);
            var upper = body.PileBodySegments[0].DeepCopy();
            var lower = upper.DeepCopy();
            upper.No = 1;
            upper.SegmentLength = upper.SegmentDepth = 3 - offset;   // 杭頭 -2 の杭で、杭区間の境は -5 + offset
            lower.No = 2;
            lower.SegmentLength = length - upper.SegmentLength;
            lower.SegmentDepth = length;
            body.PileBodySegments = [upper, lower];
            // 入れ直すと断面は既定値に戻るので、そのあとで下の区間だけ太くする (場所打ちRC の杭径は外径から決まる)
            lower.PileSection.ConcreteOutDia = upper.PileSection.ConcreteOutDia + 300;
            Assert.AreNotEqual(upper.PileSection.PileDiameter, lower.PileSection.PileDiameter, "(前提) 上下の杭区間で杭径が同じです");
            foreach (var pile in input.PileLayoutItems) pile.Z = -2 + pile.FoundationBeamDeltaZc;
            input.GenerateSoilPiles();

            var sp = input.ElementDivision.SoilPiles.Single();
            var zs = sp.ZDataItems.Select(d => d.Z).ToArray();
            double segmentBoundary = -5 + offset;
            Assert.IsTrue(zs.Any(z => Math.Abs(z - segmentBoundary) < 1e-9) && zs.Any(z => Math.Abs(z + 5) < 1e-9),
                "(前提) 杭区間の境と地層境界の両方が節点にありません");
            for (int i = 0; i < zs.Length - 1; i++)
            {
                double mid = (zs[i] + zs[i + 1]) / 2;
                double expected = (mid > segmentBoundary ? upper : lower).PileSection.PileDiameter / 1000;
                Assert.AreEqual(expected, sp.HorizontalSoilReactions[i].B, 1e-12,
                    $"区間{i} (Z={zs[i]:F6}〜{zs[i + 1]:F6}): 別の杭区間の杭径でばねを作っています");
            }
        }
    }
}
