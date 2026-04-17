using PileDesign.FEM;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// BeamFlexureNonlinearity（梁曲げ非線形設定の紐付け）のテスト。
    /// </summary>
    [TestClass]
    public class BeamFlexureNonlinearityTests
    {
        private static Beam CreateBeam()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1, 0, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            return new Beam("B1", sec, ni, nj, 1.0, 1.0);
        }

        private static MomentCurvatureCurve CreateCurve()
            => new(new[] { (0.0, 0.0), (0.001, 100.0), (0.01, 500.0) });

        [TestMethod]
        public void AxisConstructor_SetsSeparateAxesMode()
        {
            var beam = CreateBeam();
            var curve = CreateCurve();
            var nl = new BeamFlexureNonlinearity(beam, FlexureAxis.My, curve);

            Assert.AreEqual(FlexureMode.SeparateAxes, nl.Mode);
            Assert.AreEqual(FlexureAxis.My, nl.Axis);
            Assert.AreSame(beam, nl.Beam);
            Assert.AreSame(curve, nl.Curve);
        }

        [TestMethod]
        public void AxisConstructor_MzAxis_StoresCorrectly()
        {
            var nl = new BeamFlexureNonlinearity(CreateBeam(), FlexureAxis.Mz, CreateCurve());
            Assert.AreEqual(FlexureAxis.Mz, nl.Axis);
        }

        [TestMethod]
        public void CombinedConstructor_SetsCombinedMyMzMode_AndLeavesAxisNull()
        {
            var beam = CreateBeam();
            var curve = CreateCurve();
            var nl = new BeamFlexureNonlinearity(beam, curve);

            Assert.AreEqual(FlexureMode.CombinedMyMz, nl.Mode);
            Assert.IsNull(nl.Axis);
            Assert.AreSame(beam, nl.Beam);
            Assert.AreSame(curve, nl.Curve);
        }

        [TestMethod]
        public void CurrentCurvature_DefaultsToZero()
        {
            var nl = new BeamFlexureNonlinearity(CreateBeam(), CreateCurve());
            Assert.AreEqual(0.0, nl.CurrentCurvature);
        }

        [TestMethod]
        public void CurrentCurvature_IsMutable()
        {
            var nl = new BeamFlexureNonlinearity(CreateBeam(), CreateCurve());
            nl.CurrentCurvature = 0.0025;
            Assert.AreEqual(0.0025, nl.CurrentCurvature, 1e-12);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void AxisConstructor_NullBeam_Throws()
        {
            _ = new BeamFlexureNonlinearity(null!, FlexureAxis.My, CreateCurve());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void AxisConstructor_NullCurve_Throws()
        {
            _ = new BeamFlexureNonlinearity(CreateBeam(), FlexureAxis.My, null!);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CombinedConstructor_NullBeam_Throws()
        {
            _ = new BeamFlexureNonlinearity(null!, CreateCurve());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CombinedConstructor_NullCurve_Throws()
        {
            _ = new BeamFlexureNonlinearity(CreateBeam(), null!);
        }
    }
}
