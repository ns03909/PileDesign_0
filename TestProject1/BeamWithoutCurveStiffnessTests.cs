using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using Point3D = System.Windows.Media.Media3D.Point3D;

namespace TestProject1
{
    /// <summary>
    /// M-φ 曲線を持たない梁 (基礎梁・杭頭の剛な接続部・M-φ を作れなかった杭の要素) は線形。
    /// 接線 (係数行列) も割線 (内力) も初期 EI を返す。
    ///
    /// <para>2026-11-20 まで接線だけ初期 EI の 0.001 倍を返し、呼び出し元の下限で 5% に切り上がっていた。
    /// 杭の非線形を ON にすると、これらの梁だけ係数行列が 1/20 に柔らかく、反復が遠回りになっていた
    /// (外部レビュー #12)。収束の挙動は ConvergenceRegressionTests の基礎梁ありの例題で見張る。</para>
    /// </summary>
    [TestClass]
    public class BeamWithoutCurveStiffnessTests
    {
        [TestMethod]
        public void TangentAndSecantAreBothTheInitialEI()
        {
            var mat = new Material(2.5e7, 0.2);
            var sec = new Section(mat, 1.2, 1.0, 1.0, 0.3, 0.225, 0.064);
            var beam = new Beam("FoundationBeam-1", sec,
                new Node { Name = "A", Coord = new Point3D(0, 0, 0) },
                new Node { Name = "B", Coord = new Point3D(5, 0, 0) }, 1.0, 1.0);
            Assert.IsNull(beam.ResolvedCombinedCurve, "(前提) 曲線を持たない梁");

            foreach (var (phiY, phiZ) in new[] { (0.0, 0.0), (1e-3, 0.0), (2e-4, -5e-4) })
            {
                var (tanY, tanZ) = beam.EvaluateEIeff(phiY, phiZ);
                var (secY, secZ) = beam.EvaluateEIeffSecant(phiY, phiZ);
                Assert.AreEqual(2.5e7 * 0.225, tanY, 1e-6, "接線が初期 EI でない (係数行列だけ柔らかくなる)");
                Assert.AreEqual(2.5e7 * 0.064, tanZ, 1e-6);
                Assert.AreEqual(tanY, secY, "接線と割線が食い違う");
                Assert.AreEqual(tanZ, secZ);
                Assert.AreEqual(0.0, beam.EvaluateEIyzTangent(phiY, phiZ), "線形の梁に二方向曲げの連成項がある");
            }
        }
    }
}
