using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// AnalysisStepResult のテスト。解析ステップ毎の履歴レコード。
    /// </summary>
    [TestClass]
    public class AnalysisStepResultTests
    {
        private static LoadCase MakeLoadCase()
            => new() { No = 1, Level = 1, IsApplicable = true, LoadName = "L1-1" };

        private static LoadCombination MakeLoadCombo()
            => new(1, 1.0, 1.0, 1.0) { IsApplicable = true };

        [TestMethod]
        public void ParameterizedConstructor_AssignsAllProperties()
        {
            var lc = MakeLoadCase();
            var lcomb = MakeLoadCombo();
            var r = new AnalysisStepResult(lc, lcomb, isLiquefaction: true, step: 5, iteration: 3, residualValue: 0.001);

            Assert.AreSame(lc, r.LoadCase);
            Assert.AreSame(lcomb, r.LoadCombination);
            Assert.IsTrue(r.IsLiquefaction);
            Assert.AreEqual(5, r.Step);
            Assert.AreEqual(3, r.Iteration);
            Assert.AreEqual(0.001, r.ResidualValue, 1e-12);
        }

        [TestMethod]
        public void DefaultConstructor_LeavesPropertiesDefaulted()
        {
            var r = new AnalysisStepResult();
            Assert.IsNull(r.LoadCase);
            Assert.IsNull(r.LoadCombination);
            Assert.IsFalse(r.IsLiquefaction);
            Assert.AreEqual(0, r.Step);
            Assert.AreEqual(0, r.Iteration);
            Assert.AreEqual(0.0, r.ResidualValue);
        }

        [TestMethod]
        public void GetLastStep_ReturnsStep()
        {
            var r = new AnalysisStepResult { Step = 42 };
            Assert.AreEqual(42, r.GetLastStep());
        }

        [TestMethod]
        public void DeepCopy_ProducesIndependentInstance()
        {
            var r = new AnalysisStepResult(MakeLoadCase(), MakeLoadCombo(), true, 5, 3, 0.001);
            var copy = r.DeepCopy();

            Assert.AreNotSame(r, copy);
            Assert.AreNotSame(r.LoadCase, copy.LoadCase);
            Assert.AreNotSame(r.LoadCombination, copy.LoadCombination);
            Assert.AreEqual(r.IsLiquefaction, copy.IsLiquefaction);
            Assert.AreEqual(r.Step, copy.Step);
            Assert.AreEqual(r.Iteration, copy.Iteration);
            Assert.AreEqual(r.ResidualValue, copy.ResidualValue);
        }

        [TestMethod]
        public void DeepCopy_NullLoadCase_Tolerated()
        {
            // LoadCase / LoadCombination 未設定でも例外を吐かずに複製できること
            var r = new AnalysisStepResult { Step = 1, Iteration = 0, ResidualValue = 0.0 };
            var copy = r.DeepCopy();
            Assert.IsNull(copy.LoadCase);
            Assert.IsNull(copy.LoadCombination);
            Assert.AreEqual(1, copy.Step);
        }
    }

    /// <summary>
    /// NodeResult のテスト。節点のステップ別スナップショット。
    /// </summary>
    [TestClass]
    public class NodeResultTests
    {
        private static LoadCase MakeLoadCase()
            => new() { No = 1, Level = 1, IsApplicable = true, LoadName = "L1-1" };

        private static LoadCombination MakeLoadCombo()
            => new(1, 1.0, 1.0, 1.0);

        private static Node MakeNode()
        {
            var n = new Node();
            n.SetNodeInfo("N1", 1, 2, 3);
            n.CumulativedLoad = new NodeLoad(10, 20, 30, 40, 50, 60);
            n.CumulativeDisp = new NodeDisp(0.1, 0.2, 0.3, 0.4, 0.5, 0.6);
            n.CumulativeReaction = new NodeReaction(100, 200, 300, 400, 500, 600);
            n.SoilDisp = new NodeDisp(0.01, 0.02, 0.03, 0, 0, 0);
            return n;
        }

        [TestMethod]
        public void Constructor_ClonesNodeState()
        {
            var lc = MakeLoadCase();
            var lcomb = MakeLoadCombo();
            var node = MakeNode();
            var r = new NodeResult(lc, lcomb, isLiquefaction: false, step: 7, node);

            Assert.AreSame(lc, r.LoadCase);
            Assert.AreSame(lcomb, r.LoadCombination);
            Assert.IsFalse(r.IsLiquefaction);
            Assert.AreEqual(7, r.Step);

            // 値は Node と一致、かつ参照は切れている（Clone）
            Assert.AreEqual(node.CumulativedLoad.Fx, r.CumulativedLoad.Fx);
            Assert.AreNotSame(node.CumulativedLoad, r.CumulativedLoad);

            Assert.AreEqual(node.CumulativeDisp.Ux, r.CumulativeDisp.Ux);
            Assert.AreNotSame(node.CumulativeDisp, r.CumulativeDisp);

            Assert.AreEqual(node.CumulativeReaction.Fx, r.CumulativeReaction.Fx);
            Assert.AreNotSame(node.CumulativeReaction, r.CumulativeReaction);

            Assert.AreEqual(node.SoilDisp.Ux, r.SoilDisp.Ux);
            Assert.AreNotSame(node.SoilDisp, r.SoilDisp);
        }

        [TestMethod]
        public void Constructor_NullSoilDispOnNode_LeavesResultSoilDispNull()
        {
            var node = MakeNode();
            node.SoilDisp = null;

            var r = new NodeResult(MakeLoadCase(), MakeLoadCombo(), false, 0, node);
            Assert.IsNull(r.SoilDisp);
        }

        [TestMethod]
        public void MutatingSourceNode_DoesNotAffectClonedResult()
        {
            var node = MakeNode();
            var r = new NodeResult(MakeLoadCase(), MakeLoadCombo(), false, 1, node);

            node.CumulativeDisp.Ux = 999;
            Assert.AreEqual(0.1, r.CumulativeDisp.Ux, 1e-12);
        }

        [TestMethod]
        public void DefaultConstructor_LeavesAllNull()
        {
            var r = new NodeResult();
            Assert.IsNull(r.LoadCase);
            Assert.IsNull(r.LoadCombination);
            Assert.IsNull(r.CumulativedLoad);
            Assert.IsNull(r.CumulativeDisp);
            Assert.IsNull(r.CumulativeReaction);
            Assert.IsNull(r.SoilDisp);
            Assert.AreEqual(0, r.Step);
        }

        [TestMethod]
        public void DeepCopy_ProducesIndependentRecord()
        {
            var r = new NodeResult(MakeLoadCase(), MakeLoadCombo(), true, 3, MakeNode());
            var copy = r.DeepCopy();

            Assert.AreNotSame(r, copy);
            Assert.AreEqual(r.IsLiquefaction, copy.IsLiquefaction);
            Assert.AreEqual(r.Step, copy.Step);

            // すべての clone ターゲットが別インスタンス
            Assert.AreNotSame(r.CumulativedLoad, copy.CumulativedLoad);
            Assert.AreNotSame(r.CumulativeDisp, copy.CumulativeDisp);
            Assert.AreNotSame(r.CumulativeReaction, copy.CumulativeReaction);
            Assert.AreNotSame(r.SoilDisp, copy.SoilDisp);

            // 値が保たれている
            Assert.AreEqual(r.CumulativeDisp.Ux, copy.CumulativeDisp.Ux);
            Assert.AreEqual(r.CumulativedLoad.Fx, copy.CumulativedLoad.Fx);
        }
    }

    /// <summary>
    /// BeamResult のテスト。梁のステップ別スナップショット。
    /// </summary>
    [TestClass]
    public class BeamResultTests
    {
        private static LoadCase MakeLoadCase()
            => new() { No = 1, Level = 1, IsApplicable = true, LoadName = "L1-1" };

        private static LoadCombination MakeLoadCombo()
            => new(1, 1.0, 1.0, 1.0);

        private static Beam MakeBeam()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1, 0, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0)
            {
                CumulativeDisp = new BeamDisp(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12),
                CumulativeForce = new BeamForce(100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200),
                CurrentCurvature = 0.0025,
                CurrentMoment = 150.0
            };
            return beam;
        }

        [TestMethod]
        public void Constructor_AssignsMetadataAndClonesForceDisp()
        {
            var lc = MakeLoadCase();
            var lcomb = MakeLoadCombo();
            var beam = MakeBeam();
            var r = new BeamResult(lc, lcomb, isLiquefaction: true, step: 4, beam);

            Assert.AreSame(lc, r.LoadCase);
            Assert.AreSame(lcomb, r.LoadCombination);
            Assert.IsTrue(r.IsLiquefaction);
            Assert.AreEqual(4, r.Step);
            Assert.AreEqual(0.0025, r.Curvature, 1e-12);
            Assert.AreEqual(150.0, r.Moment, 1e-12);

            Assert.IsNotNull(r.CumulativeDisp);
            Assert.AreNotSame(beam.CumulativeDisp, r.CumulativeDisp);
            Assert.AreEqual(beam.CumulativeDisp.Dxi, r.CumulativeDisp.Dxi);

            Assert.IsNotNull(r.CumulativeForce);
            Assert.AreNotSame(beam.CumulativeForce, r.CumulativeForce);
            Assert.AreEqual(beam.CumulativeForce.Fxi, r.CumulativeForce.Fxi);
        }

        [TestMethod]
        public void Constructor_WithoutResolvedCurve_LeavesMPhiCurveNull()
        {
            // MakeBeam は ResolvedCombinedCurve を設定していないので null のはず
            var r = new BeamResult(MakeLoadCase(), MakeLoadCombo(), false, 0, MakeBeam());
            Assert.IsNull(r.MPhiCurve_Phis);
            Assert.IsNull(r.MPhiCurve_Moments);
        }

        [TestMethod]
        public void MutatingSourceBeam_DoesNotAffectClonedResult()
        {
            var beam = MakeBeam();
            var r = new BeamResult(MakeLoadCase(), MakeLoadCombo(), false, 0, beam);

            beam.CumulativeForce.Fxi = 9999;
            Assert.AreEqual(100, r.CumulativeForce.Fxi);
        }

        [TestMethod]
        public void DefaultConstructor_LeavesPropertiesNullOrZero()
        {
            var r = new BeamResult();
            Assert.IsNull(r.LoadCase);
            Assert.IsNull(r.LoadCombination);
            Assert.IsNull(r.CumulativeDisp);
            Assert.IsNull(r.CumulativeForce);
            Assert.IsNull(r.MPhiCurve_Phis);
            Assert.IsNull(r.MPhiCurve_Moments);
            Assert.AreEqual(0, r.Step);
            Assert.AreEqual(0.0, r.Curvature);
            Assert.AreEqual(0.0, r.Moment);
        }

        [TestMethod]
        public void DeepCopy_ClonesDispForceAndMPhiLists()
        {
            var r = new BeamResult(MakeLoadCase(), MakeLoadCombo(), false, 2, MakeBeam())
            {
                MPhiCurve_Phis = [0.0, 0.001, 0.01],
                MPhiCurve_Moments = [0.0, 100.0, 500.0],
            };
            var copy = r.DeepCopy();

            Assert.AreNotSame(r, copy);
            Assert.AreEqual(r.Step, copy.Step);
            Assert.AreEqual(r.Curvature, copy.Curvature);
            Assert.AreEqual(r.Moment, copy.Moment);

            Assert.AreNotSame(r.CumulativeDisp, copy.CumulativeDisp);
            Assert.AreNotSame(r.CumulativeForce, copy.CumulativeForce);
            Assert.AreNotSame(r.MPhiCurve_Phis, copy.MPhiCurve_Phis);
            Assert.AreNotSame(r.MPhiCurve_Moments, copy.MPhiCurve_Moments);

            CollectionAssert.AreEqual(r.MPhiCurve_Phis, copy.MPhiCurve_Phis);
            CollectionAssert.AreEqual(r.MPhiCurve_Moments, copy.MPhiCurve_Moments);
        }

        [TestMethod]
        public void DeepCopy_NullMPhiCurve_RemainsNull()
        {
            var r = new BeamResult(MakeLoadCase(), MakeLoadCombo(), false, 0, MakeBeam());
            var copy = r.DeepCopy();
            Assert.IsNull(copy.MPhiCurve_Phis);
            Assert.IsNull(copy.MPhiCurve_Moments);
        }
    }
}
