using MathNet.Numerics.LinearAlgebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// 杭の接線剛性に入れる二方向曲げの連成項 (<see cref="Beam.EIyzTan"/>) の形。
    ///
    /// 合成 M–φ 曲線の真の接線は <c>D = R·diag(EI_tan, EI_sec)·Rᵀ</c> (R は曲率の向きの回転) で、非対角
    /// <c>(EI_tan − EI_sec)·cosθ·sinθ</c> を持つ。以前は対角だけを剛性に入れていた。
    /// 連成ブロックの符号・係数を取り違えると、剛性行列が静かに誤る (内力は割線で求めるので答えは変わらないが、
    /// 反復が遅くなる・収束しなくなる) ので、形状関数から独立に積分した行列と突き合わせる。
    /// </summary>
    [TestClass]
    public class BiaxialTangentCouplingTests
    {
        private const double L = 1.7, E = 2.5e7, I = 0.05;

        private static Beam NewBeam()
        {
            var nI = new Node { Name = "I", Coord = new Point3D(0, 0, 0), Boundary = new Boundary(false, false, false, false, false, false) };
            var nJ = new Node { Name = "J", Coord = new Point3D(0, 0, -L), Boundary = new Boundary(false, false, false, false, false, false) };
            var section = new Section(new Material(E, 0.2), 0.8, 0.8, 0.8, 0.1, I, I);
            return new Beam("B", section, nI, nJ, 1.0, 1.0);
        }

        /// <summary>
        /// 曲げの自由度 (1,2,4,5,7,8,10,11) の剛性を、Hermite の形状関数の 2 階微分から
        /// <c>∫ Bᵀ D B dx</c> を Gauss 積分 (2 点で厳密) して求める。
        /// φz = v″ (自由度 v1, θz1, v2, θz2 = 1, 5, 7, 11)、φy = −w″ (θy = −w′。自由度 w1, θy1, w2, θy2 = 2, 4, 8, 10)。
        /// </summary>
        private static Matrix<double> IntegratedBending(double dyy, double dzz, double dyz)
        {
            var k = Matrix<double>.Build.Dense(12, 12);
            double[] gp = { 0.5 - 0.5 / Math.Sqrt(3), 0.5 + 0.5 / Math.Sqrt(3) };
            foreach (double xi in gp)
            {
                double[] n2 = { (-6 + 12 * xi) / (L * L), (-4 + 6 * xi) / L, (6 - 12 * xi) / (L * L), (-2 + 6 * xi) / L };
                var bz = new double[12];
                var by = new double[12];
                int[] zd = { 1, 5, 7, 11 }, yd = { 2, 4, 8, 10 };
                double[] ys = { -1, 1, -1, 1 };
                for (int a = 0; a < 4; a++) { bz[zd[a]] = n2[a]; by[yd[a]] = ys[a] * n2[a]; }
                for (int r = 0; r < 12; r++)
                    for (int c = 0; c < 12; c++)
                        k[r, c] += 0.5 * L * (by[r] * dyy * by[c] + bz[r] * dzz * bz[c] + by[r] * dyz * bz[c] + bz[r] * dyz * by[c]);
            }
            return k;
        }

        [DataTestMethod]
        [DataRow(1.0, 1.0, 0.0)]          // 連成なし (従来と同じ)
        [DataRow(0.30, 0.55, -0.20)]      // 塑性化して二方向に曲がっている要素
        [DataRow(0.80, 0.20, 0.35)]
        public void TheTangentMatchesTheIntegratedBendingStiffness(double ky, double kz, double kyzRatio)
        {
            var beam = NewBeam();
            beam.KTan_y = ky;
            beam.KTan_z = kz;
            beam.EIyzTan = kyzRatio * E * I;
            beam.SetKe(true);

            var expected = IntegratedBending(ky * E * I, kz * E * I, kyzRatio * E * I);
            int[] bending = { 1, 2, 4, 5, 7, 8, 10, 11 };
            foreach (int r in bending)
                foreach (int c in bending)
                    Assert.AreEqual(expected[r, c], beam.KeTan[r, c], 1e-6 * E * I / L,
                        $"KeTan[{r},{c}] が形状関数から積分した値と合いません (連成の符号・係数の誤り)");
        }

        /// <summary>
        /// 曲率の主軸に沿って曲げると EI_tan、直交方向は EI_sec の剛性になること (連成を入れた接線の意味)。
        /// 回転だけで曲率を与え、同じ変形の一方向曲げ (EI0) のエネルギーとの比を見る。
        /// </summary>
        [TestMethod]
        public void BendingAlongThePrincipalDirectionUsesTheTangentStiffness()
        {
            const double tan = 0.1 * E * I, sec = 0.6 * E * I;
            double theta = 35 * Math.PI / 180, c = Math.Cos(theta), s = Math.Sin(theta);

            var beam = NewBeam();
            beam.KTan_y = (tan * c * c + sec * s * s) / (E * I);
            beam.KTan_z = (tan * s * s + sec * c * c) / (E * I);
            beam.EIyzTan = (tan - sec) * c * s;
            beam.SetKe(true);

            var elastic = NewBeam();
            elastic.SetKe(true);

            double Energy(Matrix<double> k, double py, double pz)
            {
                var u = Vector<double>.Build.Dense(12);
                u[10] = py;   // θy_j (φy = (θyj − θyi)/L)
                u[11] = pz;   // θz_j
                return u * (k * u);
            }

            double along = Energy(beam.KeTan, c, s) / Energy(elastic.KeTan, c, s);
            double across = Energy(beam.KeTan, -s, c) / Energy(elastic.KeTan, -s, c);
            Assert.AreEqual(tan / (E * I), along, 1e-9, "曲率の主軸に沿った剛性が EI_tan になっていません");
            Assert.AreEqual(sec / (E * I), across, 1e-9, "主軸に直交する剛性が EI_sec になっていません");
        }

        /// <summary>端部の回転ばね係数が剛 (1) でない要素には、形が変わるので連成を足さないこと。</summary>
        [TestMethod]
        public void CouplingIsNotAddedWhenTheEndsAreNotRigid()
        {
            var nI = new Node { Name = "I", Coord = new Point3D(0, 0, 0), Boundary = new Boundary(false, false, false, false, false, false) };
            var nJ = new Node { Name = "J", Coord = new Point3D(0, 0, -L), Boundary = new Boundary(false, false, false, false, false, false) };
            var beam = new Beam("B", new Section(new Material(E, 0.2), 0.8, 0.8, 0.8, 0.1, I, I), nI, nJ, 0.5, 1.0) { EIyzTan = 0.3 * E * I };
            beam.SetKe(true);
            Assert.AreEqual(0.0, beam.KeTan[2, 1], 0.0);
            Assert.AreEqual(0.0, beam.KeTan[4, 5], 0.0);
        }

        /// <summary>割線剛性 (内力を求める側) には連成を足さないこと (収束した答えを変えないため)。</summary>
        [TestMethod]
        public void TheSecantStiffnessHasNoCoupling()
        {
            var beam = NewBeam();
            beam.EIyzTan = 0.3 * E * I;
            beam.SetKe(false);
            foreach (int y in new[] { 2, 4, 8, 10 })
                foreach (int z in new[] { 1, 5, 7, 11 })
                    Assert.AreEqual(0.0, beam.KeSec[y, z], 0.0, $"KeSec[{y},{z}] に連成が入っています");
        }
    }
}
