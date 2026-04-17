using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;

namespace TestProject1
{
    /// <summary>
    /// BeamForce（梁端点12成分の内力レコード）のテスト。
    /// </summary>
    [TestClass]
    public class BeamForceTests
    {
        private static BeamForce MakeSample()
            => new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

        [TestMethod]
        public void Constructor_SetsAllComponents()
        {
            var bf = MakeSample();
            Assert.AreEqual(1, bf.Fxi);
            Assert.AreEqual(2, bf.Fyi);
            Assert.AreEqual(3, bf.Fzi);
            Assert.AreEqual(4, bf.Mxi);
            Assert.AreEqual(5, bf.Myi);
            Assert.AreEqual(6, bf.Mzi);
            Assert.AreEqual(7, bf.Fxj);
            Assert.AreEqual(8, bf.Fyj);
            Assert.AreEqual(9, bf.Fzj);
            Assert.AreEqual(10, bf.Mxj);
            Assert.AreEqual(11, bf.Myj);
            Assert.AreEqual(12, bf.Mzj);
        }

        [TestMethod]
        public void Fi_ComputesHorizontalShearMagnitudeAtI()
        {
            // Fi = sqrt(Fyi^2 + Fzi^2)、Fxi は含まない
            var bf = new BeamForce(100, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual(5.0, bf.Fi, 1e-12);
        }

        [TestMethod]
        public void Fj_ComputesHorizontalShearMagnitudeAtJ()
        {
            var bf = new BeamForce(0, 0, 0, 0, 0, 0, 100, 6, 8, 0, 0, 0);
            Assert.AreEqual(10.0, bf.Fj, 1e-12);
        }

        [TestMethod]
        public void Mi_ComputesBendingMagnitudeAtI()
        {
            // Mi = sqrt(Myi^2 + Mzi^2)、Mxi は含まない
            var bf = new BeamForce(0, 0, 0, 100, 3, 4, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual(5.0, bf.Mi, 1e-12);
        }

        [TestMethod]
        public void Mj_ComputesBendingMagnitudeAtJ()
        {
            var bf = new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 6, 8);
            Assert.AreEqual(10.0, bf.Mj, 1e-12);
        }

        [TestMethod]
        public void FabsMax_ReturnsLargerOfIAndJ()
        {
            var bf = new BeamForce(0, 3, 4, 0, 0, 0, 0, 6, 8, 0, 0, 0);
            Assert.AreEqual(10.0, bf.FabsMax, 1e-12);
        }

        [TestMethod]
        public void MabsMax_ReturnsLargerOfIAndJ()
        {
            var bf = new BeamForce(0, 0, 0, 0, 6, 8, 0, 0, 0, 0, 3, 4);
            Assert.AreEqual(10.0, bf.MabsMax, 1e-12);
        }

        [TestMethod]
        public void GetVector_Returns12ElementVectorInDeclarationOrder()
        {
            var bf = MakeSample();
            var v = bf.GetVector();
            Assert.AreEqual(12, v.Count);
            for (int i = 0; i < 12; i++)
                Assert.AreEqual(i + 1, v[i], 1e-12);
        }

        [TestMethod]
        public void SetVector_WritesAll12Components()
        {
            var bf = new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var v = Vector<double>.Build.DenseOfArray([1.1, 2.2, 3.3, 4.4, 5.5, 6.6, 7.7, 8.8, 9.9, 10.1, 11.1, 12.1]);
            bf.SetVector(v);
            Assert.AreEqual(1.1, bf.Fxi, 1e-12);
            Assert.AreEqual(6.6, bf.Mzi, 1e-12);
            Assert.AreEqual(12.1, bf.Mzj, 1e-12);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SetVector_WrongLength_Throws()
        {
            var bf = MakeSample();
            var v = Vector<double>.Build.Dense(11);
            bf.SetVector(v);
        }

        [TestMethod]
        public void GetByIndex_MatchesDeclarationOrder()
        {
            var bf = MakeSample();
            for (int i = 0; i < 12; i++)
                Assert.AreEqual(i + 1, bf.GetByIndex(i), 1e-12);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetByIndex_Invalid_Throws()
        {
            MakeSample().GetByIndex(12);
        }

        [TestMethod]
        public void SetByIndex_UpdatesMatchingComponent()
        {
            var bf = new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            bf.SetByIndex(0, 1.5);
            bf.SetByIndex(11, 9.5);
            Assert.AreEqual(1.5, bf.Fxi, 1e-12);
            Assert.AreEqual(9.5, bf.Mzj, 1e-12);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SetByIndex_Invalid_Throws()
        {
            var bf = MakeSample();
            bf.SetByIndex(-1, 0);
        }

        [TestMethod]
        public void OperatorAdd_SumsAllComponents()
        {
            var a = MakeSample();
            var b = new BeamForce(10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120);
            var c = a + b;
            Assert.AreEqual(11, c.Fxi);
            Assert.AreEqual(132, c.Mzj);
        }

        [TestMethod]
        public void Clone_ProducesIndependentCopy()
        {
            var a = MakeSample();
            var b = a.Clone();
            Assert.AreNotSame(a, b);
            b.Fxi = 999;
            Assert.AreEqual(1, a.Fxi);
            Assert.AreEqual(999, b.Fxi);
        }

        [TestMethod]
        public void GetAbsMoment_ReturnsIAndJInOrder()
        {
            var bf = new BeamForce(0, 0, 0, 0, 3, 4, 0, 0, 0, 0, 6, 8);
            var m = bf.GetAbsMoment();
            Assert.AreEqual(2, m.Length);
            Assert.AreEqual(5.0, m[0], 1e-12);
            Assert.AreEqual(10.0, m[1], 1e-12);
        }

        [TestMethod]
        public void GetAbsShear_ReturnsIAndJInOrder()
        {
            var bf = new BeamForce(0, 3, 4, 0, 0, 0, 0, 6, 8, 0, 0, 0);
            var s = bf.GetAbsShear();
            Assert.AreEqual(2, s.Length);
            Assert.AreEqual(5.0, s[0], 1e-12);
            Assert.AreEqual(10.0, s[1], 1e-12);
        }
    }

    /// <summary>
    /// BeamForceExtensions.GetEnd3Vector のテスト（端点3成分ベクトル抽出）。
    /// </summary>
    [TestClass]
    public class BeamForceExtensionsTests
    {
        private static BeamForce SampleForce()
            // I端: Fx=1, Fy=2, Fz=3, Mx=4, My=5, Mz=6
            // J端: Fx=7, Fy=8, Fz=9, Mx=10, My=11, Mz=12
            => new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

        [TestMethod]
        public void GetEnd3Vector_NullBeamForce_ReturnsZeroVector()
        {
            BeamForce? bf = null;
            var v = bf!.GetEnd3Vector(isMoment: false, isIend: true, derivedType: string.Empty);
            Assert.AreEqual(3, v.Count);
            Assert.AreEqual(0.0, v[0]);
            Assert.AreEqual(0.0, v[1]);
            Assert.AreEqual(0.0, v[2]);
        }

        [TestMethod]
        public void GetEnd3Vector_IEnd_Force_Returns_Fx_Fy_Fz()
        {
            var v = SampleForce().GetEnd3Vector(isMoment: false, isIend: true, derivedType: string.Empty);
            Assert.AreEqual(1, v[0]);
            Assert.AreEqual(2, v[1]);
            Assert.AreEqual(3, v[2]);
        }

        [TestMethod]
        public void GetEnd3Vector_JEnd_Force_Returns_Fx_Fy_Fz()
        {
            var v = SampleForce().GetEnd3Vector(isMoment: false, isIend: false, derivedType: string.Empty);
            Assert.AreEqual(7, v[0]);
            Assert.AreEqual(8, v[1]);
            Assert.AreEqual(9, v[2]);
        }

        [TestMethod]
        public void GetEnd3Vector_IEnd_Moment_Returns_Mx_Mz_My_Order()
        {
            // 既存実装は Moment モードで [Mx, Mz, My] の順に並べる
            var v = SampleForce().GetEnd3Vector(isMoment: true, isIend: true, derivedType: string.Empty);
            Assert.AreEqual(4, v[0]); // Mx
            Assert.AreEqual(6, v[1]); // Mz
            Assert.AreEqual(5, v[2]); // My
        }

        [TestMethod]
        public void GetEnd3Vector_JEnd_Moment_Returns_Mx_Mz_My_Order()
        {
            var v = SampleForce().GetEnd3Vector(isMoment: true, isIend: false, derivedType: string.Empty);
            Assert.AreEqual(10, v[0]); // Mxj
            Assert.AreEqual(12, v[1]); // Mzj
            Assert.AreEqual(11, v[2]); // Myj
        }

        [TestMethod]
        public void GetEnd3Vector_DerivedMh_IEnd_Returns_ZeroNegMz_My()
        {
            // Mh 派生: [0, -Mz, My]
            var v = SampleForce().GetEnd3Vector(isMoment: true, isIend: true, derivedType: "Mh");
            Assert.AreEqual(0, v[0]);
            Assert.AreEqual(-6, v[1]); // -Mzi
            Assert.AreEqual(5, v[2]);  //  Myi
        }

        [TestMethod]
        public void GetEnd3Vector_DerivedFh_JEnd_Returns_ZeroFy_Fz()
        {
            // Fh 派生: [0, Fy, Fz]
            var v = SampleForce().GetEnd3Vector(isMoment: false, isIend: false, derivedType: "Fh");
            Assert.AreEqual(0, v[0]);
            Assert.AreEqual(8, v[1]); // Fyj
            Assert.AreEqual(9, v[2]); // Fzj
        }

        [TestMethod]
        public void GetEnd3Vector_DerivedType_OverridesIsMomentFlag()
        {
            // "Mh" 派生は isMoment=false でも Mh が出る（実装ではまず derivedType を見る）
            var v = SampleForce().GetEnd3Vector(isMoment: false, isIend: true, derivedType: "Mh");
            Assert.AreEqual(-6, v[1]);
            Assert.AreEqual(5, v[2]);
        }
    }
}
