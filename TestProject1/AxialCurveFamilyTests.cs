using PileDesign.FEM;

namespace TestProject1
{
    /// <summary>
    /// AxialCurveFamily（軸力 N に依存する M-φ/M-θ 曲線ファミリの解決）のテスト。
    /// 入力はリフレクションで受け取るため、匿名型ベースのヘルパーを使う。
    /// </summary>
    [TestClass]
    public class AxialCurveFamilyTests
    {
        // ファミリ1要素のコンテナ（N + Points/Curve）
        private sealed class FamilyEntry
        {
            public double N { get; init; }
            public List<(double Phi, double Moment)>? Points { get; init; }
        }

        private sealed class FamilyEntryTheta
        {
            public double N { get; init; }
            public List<(double Theta, double Moment)>? Points { get; init; }
        }

        [TestMethod]
        public void ResolveMPhi_NullFamily_ReturnsNull()
        {
            var c = AxialCurveFamily.ResolveMPhi(null!, 0);
            Assert.IsNull(c);
        }

        [TestMethod]
        public void ResolveMPhi_EmptySequence_ReturnsNull()
        {
            var c = AxialCurveFamily.ResolveMPhi(Array.Empty<FamilyEntry>(), 100);
            Assert.IsNull(c);
        }

        [TestMethod]
        public void ResolveMPhi_SingleCurveFamily_ReturnsThatCurveRegardlessOfN()
        {
            var family = new[]
            {
                new FamilyEntry
                {
                    N = 500,
                    Points = [(0.0, 0.0), (0.001, 100.0), (0.01, 500.0)]
                }
            };
            var c1 = AxialCurveFamily.ResolveMPhi(family, N: 100);
            var c2 = AxialCurveFamily.ResolveMPhi(family, N: 10000);

            Assert.IsNotNull(c1);
            Assert.IsNotNull(c2);
            Assert.AreEqual(3, c1.Points.Count);
            Assert.AreEqual(3, c2.Points.Count);
            // どちらも同じ点列
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(c1.Points[i].Phi, c2.Points[i].Phi, 1e-12);
                Assert.AreEqual(c1.Points[i].Moment, c2.Points[i].Moment, 1e-12);
            }
        }

        [TestMethod]
        public void ResolveMPhi_ExactMatch_ReturnsSampleCurve()
        {
            var family = new[]
            {
                new FamilyEntry { N = 0, Points = [(0.0, 0.0), (0.01, 100.0)] },
                new FamilyEntry { N = 1000, Points = [(0.0, 0.0), (0.01, 300.0)] }
            };
            var c = AxialCurveFamily.ResolveMPhi(family, N: 1000);
            Assert.IsNotNull(c);
            Assert.AreEqual(2, c.Points.Count);
            Assert.AreEqual(300.0, c.Points[1].Moment, 1e-12);
        }

        [TestMethod]
        public void ResolveMPhi_InterpolatesBetweenSamples()
        {
            var family = new[]
            {
                new FamilyEntry { N = 0,    Points = [(0.0, 0.0), (0.01, 100.0)] },
                new FamilyEntry { N = 1000, Points = [(0.0, 0.0), (0.01, 300.0)] }
            };
            // 中点 N=500 で線形内挿 → 200
            var c = AxialCurveFamily.ResolveMPhi(family, N: 500);
            Assert.IsNotNull(c);
            Assert.AreEqual(200.0, c.Points[^1].Moment, 1e-9);
        }

        [TestMethod]
        public void ResolveMPhi_BelowRange_ClampsToFirstCurve()
        {
            var family = new[]
            {
                new FamilyEntry { N = 100,  Points = [(0.0, 0.0), (0.01, 100.0)] },
                new FamilyEntry { N = 1000, Points = [(0.0, 0.0), (0.01, 300.0)] }
            };
            var c = AxialCurveFamily.ResolveMPhi(family, N: -500);
            Assert.IsNotNull(c);
            // 最初のサンプル (N=100) の Moment を返す（上側クランプ）
            Assert.AreEqual(100.0, c.Points[^1].Moment, 1e-12);
        }

        [TestMethod]
        public void ResolveMPhi_OutOfOrderInput_SortedByNInternally()
        {
            // 意図的に逆順で渡しても結果が正しい
            var family = new[]
            {
                new FamilyEntry { N = 1000, Points = [(0.0, 0.0), (0.01, 300.0)] },
                new FamilyEntry { N = 0,    Points = [(0.0, 0.0), (0.01, 100.0)] }
            };
            var c = AxialCurveFamily.ResolveMPhi(family, N: 500);
            Assert.IsNotNull(c);
            Assert.AreEqual(200.0, c.Points[^1].Moment, 1e-9);
        }

        [TestMethod]
        public void ResolveMPhi_EntryWithOnlyOnePoint_Skipped()
        {
            // 2 点未満のエントリは除外される → 残り 1 エントリ
            var family = new object[]
            {
                new FamilyEntry { N = 100, Points = [(0.0, 0.0)] }, // 1 点のみ → 除外
                new FamilyEntry { N = 500, Points = [(0.0, 0.0), (0.01, 200.0)] }
            };
            var c = AxialCurveFamily.ResolveMPhi(family, N: 300);
            Assert.IsNotNull(c);
            // N=300 で 500 のみ残るため、その曲線が返る
            Assert.AreEqual(200.0, c.Points[^1].Moment, 1e-12);
        }

        [TestMethod]
        public void ResolveMTheta_BasicFamily_ReturnsCurve()
        {
            var family = new[]
            {
                new FamilyEntryTheta { N = 0,    Points = [(0.0, 0.0), (0.02, 150.0)] },
                new FamilyEntryTheta { N = 1000, Points = [(0.0, 0.0), (0.02, 450.0)] }
            };
            var c = AxialCurveFamily.ResolveMTheta(family, N: 500);
            Assert.IsNotNull(c);
            Assert.IsTrue(c.Points.Count >= 2);
            // N=500 中点、θ=0.02 での M は 300
            var last = c.Points[^1];
            Assert.AreEqual(0.02, last.Theta, 1e-12);
            Assert.AreEqual(300.0, last.Moment, 1e-9);
        }
    }
}
