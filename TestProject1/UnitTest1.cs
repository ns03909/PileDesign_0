using PileDesign.FEM;
using MathNet.Numerics.LinearAlgebra;
using System.IO;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// NodeDisp（節点変位レコード）のテスト
    /// </summary>
    [TestClass]
    public class NodeDispTests
    {
        [TestMethod]
        public void Constructor_SetsAllComponents()
        {
            var d = new NodeDisp(1, 2, 3, 4, 5, 6);
            Assert.AreEqual(1, d.Ux);
            Assert.AreEqual(2, d.Uy);
            Assert.AreEqual(3, d.Uz);
            Assert.AreEqual(4, d.Rx);
            Assert.AreEqual(5, d.Ry);
            Assert.AreEqual(6, d.Rz);
        }

        [TestMethod]
        public void GetByIndex_ReturnsCorrectComponent()
        {
            var d = new NodeDisp(10, 20, 30, 40, 50, 60);
            Assert.AreEqual(10, d.GetByIndex(0));
            Assert.AreEqual(20, d.GetByIndex(1));
            Assert.AreEqual(30, d.GetByIndex(2));
            Assert.AreEqual(40, d.GetByIndex(3));
            Assert.AreEqual(50, d.GetByIndex(4));
            Assert.AreEqual(60, d.GetByIndex(5));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetByIndex_InvalidIndex_Throws()
        {
            var d = new NodeDisp(0, 0, 0, 0, 0, 0);
            d.GetByIndex(6);
        }

        [TestMethod]
        public void SetByIndex_UpdatesCorrectComponent()
        {
            var d = new NodeDisp(0, 0, 0, 0, 0, 0);
            d.SetByIndex(0, 99);
            Assert.AreEqual(99, d.Ux);
            d.SetByIndex(3, 77);
            Assert.AreEqual(77, d.Rx);
        }

        [TestMethod]
        public void OperatorAdd_SumsComponents()
        {
            var a = new NodeDisp(1, 2, 3, 4, 5, 6);
            var b = new NodeDisp(10, 20, 30, 40, 50, 60);
            var c = a + b;
            Assert.AreEqual(11, c.Ux);
            Assert.AreEqual(22, c.Uy);
            Assert.AreEqual(33, c.Uz);
            Assert.AreEqual(44, c.Rx);
            Assert.AreEqual(55, c.Ry);
            Assert.AreEqual(66, c.Rz);
        }

        [TestMethod]
        public void OperatorSubtract_DifferencesComponents()
        {
            var a = new NodeDisp(10, 20, 30, 40, 50, 60);
            var b = new NodeDisp(1, 2, 3, 4, 5, 6);
            var c = a - b;
            Assert.AreEqual(9, c.Ux);
            Assert.AreEqual(18, c.Uy);
            Assert.AreEqual(27, c.Uz);
            Assert.AreEqual(36, c.Rx);
            Assert.AreEqual(45, c.Ry);
            Assert.AreEqual(54, c.Rz);
        }

        [TestMethod]
        public void Uh_ComputesHorizontalMagnitude()
        {
            var d = new NodeDisp(3, 4, 0, 0, 0, 0);
            Assert.AreEqual(5.0, d.Uh, 1e-12);
        }

        [TestMethod]
        public void GetVector_Returns6ElementVector()
        {
            var d = new NodeDisp(1, 2, 3, 4, 5, 6);
            var v = d.GetVector();
            Assert.AreEqual(6, v.Count);
            Assert.AreEqual(1, v[0]);
            Assert.AreEqual(6, v[5]);
        }

        [TestMethod]
        public void SetVector_UpdatesAllComponents()
        {
            var d = new NodeDisp(0, 0, 0, 0, 0, 0);
            var v = Vector<double>.Build.DenseOfArray(new[] { 1.0, 2, 3, 4, 5, 6 });
            d.SetVector(v);
            Assert.AreEqual(1, d.Ux);
            Assert.AreEqual(6, d.Rz);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SetVector_WrongSize_Throws()
        {
            var d = new NodeDisp(0, 0, 0, 0, 0, 0);
            d.SetVector(Vector<double>.Build.Dense(3));
        }

        [TestMethod]
        public void Clone_CreatesIndependentCopy()
        {
            var original = new NodeDisp(1, 2, 3, 4, 5, 6);
            var clone = original.Clone();
            clone.Ux = 99;
            Assert.AreEqual(1, original.Ux);
            Assert.AreEqual(99, clone.Ux);
        }
    }

    /// <summary>
    /// Material（材料）のテスト
    /// </summary>
    [TestClass]
    public class MaterialTests
    {
        [TestMethod]
        public void Constructor_ComputesShearModulus()
        {
            // コンクリート: E=25e6 kN/m², ν=0.2
            var mat = new Material(25e6, 0.2);
            Assert.AreEqual(25e6, mat.E);
            Assert.AreEqual(0.2, mat.P);
            // G = E / (2*(1+ν))
            double expectedG = 25e6 / (2 * 1.2);
            Assert.AreEqual(expectedG, mat.G, 1e-3);
        }

        [TestMethod]
        public void Constructor_PoissonZero_GEqualsHalfE()
        {
            var mat = new Material(100, 0);
            Assert.AreEqual(50, mat.G, 1e-12);
        }
    }

    /// <summary>
    /// Boundary（拘束条件）のテスト
    /// </summary>
    [TestClass]
    public class BoundaryTests
    {
        [TestMethod]
        public void Constructor_SetsFlags()
        {
            var b = new Boundary(true, false, true, false, true, false);
            Assert.IsTrue(b.Ux);
            Assert.IsFalse(b.Uy);
            Assert.IsTrue(b.Uz);
            Assert.IsFalse(b.Rx);
            Assert.IsTrue(b.Ry);
            Assert.IsFalse(b.Rz);
        }

        [TestMethod]
        public void Set_OverridesAll()
        {
            var b = new Boundary(false, false, false, false, false, false);
            b.Set(true, true, true, true, true, true);
            Assert.IsTrue(b.Ux);
            Assert.IsTrue(b.Rz);
        }
    }

    /// <summary>
    /// Section（断面）のテスト
    /// </summary>
    [TestClass]
    public class SectionTests
    {
        [TestMethod]
        public void Constructor_SetsAllProperties()
        {
            var mat = new Material(25e6, 0.2);
            var sec = new Section(mat, 0.5, 0.4, 0.4, 0.01, 0.005, 0.005);
            Assert.AreEqual(0.5, sec.AX);
            Assert.AreEqual(0.01, sec.IX);
            Assert.AreSame(mat, sec.Material);
        }

        [TestMethod]
        public void DeepCopy_CreatesNewInstance()
        {
            var mat = new Material(25e6, 0.2);
            var sec = new Section(mat, 0.5, 0.4, 0.4, 0.01, 0.005, 0.005);
            var copy = sec.DeepCopy();
            Assert.AreNotSame(sec, copy);
            Assert.AreEqual(sec.AX, copy.AX);
        }
    }

    /// <summary>
    /// MomentCurvatureCurve（M-φ曲線）のテスト
    /// </summary>
    [TestClass]
    public class MomentCurvatureCurveTests
    {
        private static MomentCurvatureCurve CreateTrilinearCurve()
        {
            // 典型的な3折れ線M-φ曲線（ひび割れ→降伏→終局）
            return new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (0.001, 100.0),   // ひび割れ
                (0.005, 200.0),   // 降伏
                (0.020, 250.0),   // 終局
            });
        }

        [TestMethod]
        public void EvaluateMoment_AtControlPoint_ExactValue()
        {
            var curve = CreateTrilinearCurve();
            Assert.AreEqual(0.0, curve.EvaluateMoment(0.0), 1e-10);
            Assert.AreEqual(100.0, curve.EvaluateMoment(0.001), 1e-10);
            Assert.AreEqual(200.0, curve.EvaluateMoment(0.005), 1e-10);
            Assert.AreEqual(250.0, curve.EvaluateMoment(0.020), 1e-10);
        }

        [TestMethod]
        public void EvaluateMoment_Midpoint_LinearInterpolation()
        {
            var curve = CreateTrilinearCurve();
            // φ=0.0005 は (0,0)と(0.001,100)の中間
            Assert.AreEqual(50.0, curve.EvaluateMoment(0.0005), 1e-10);
            // φ=0.003 は (0.001,100)と(0.005,200)の中間
            Assert.AreEqual(150.0, curve.EvaluateMoment(0.003), 1e-10);
        }

        [TestMethod]
        public void EvaluateMoment_BeyondRange_Extrapolates()
        {
            var curve = CreateTrilinearCurve();
            // 最後の区間の傾き: (250-200)/(0.020-0.005) = 50/0.015 ≈ 3333.33
            double slopeLastSeg = (250.0 - 200.0) / (0.020 - 0.005);
            double expected = 200.0 + slopeLastSeg * (0.025 - 0.005);
            Assert.AreEqual(expected, curve.EvaluateMoment(0.025), 1e-6);
        }

        [TestMethod]
        public void EvaluateMoment_NegativePhi_Extrapolates()
        {
            var curve = CreateTrilinearCurve();
            // φ < 0: 最初の区間の傾きで外挿
            double slope = 100.0 / 0.001; // = 100000
            double expected = 0.0 + slope * (-0.001);
            Assert.AreEqual(expected, curve.EvaluateMoment(-0.001), 1e-6);
        }

        [TestMethod]
        public void EvaluateMoment_Empty_ReturnsZero()
        {
            var curve = new MomentCurvatureCurve();
            Assert.AreEqual(0.0, curve.EvaluateMoment(0.01));
        }

        [TestMethod]
        public void EvaluateMoment_SinglePoint_ReturnsConstant()
        {
            var curve = new MomentCurvatureCurve(new[] { (0.0, 42.0) });
            Assert.AreEqual(42.0, curve.EvaluateMoment(0.0));
            Assert.AreEqual(42.0, curve.EvaluateMoment(999.0));
        }

        [TestMethod]
        public void EvaluateTangent_InSegmentCenter_ReturnsSlope()
        {
            var curve = CreateTrilinearCurve();
            // 最初の区間: 傾き = 100/0.001 = 100000
            // セグメント中央 (blendWidth外) で評価
            double slope0 = 100.0 / 0.001;
            double midPhi = 0.0005;
            Assert.AreEqual(slope0, curve.EvaluateTangent(midPhi), slope0 * 0.01);
        }

        [TestMethod]
        public void EvaluateTangent_Empty_ReturnsZero()
        {
            var curve = new MomentCurvatureCurve();
            Assert.AreEqual(0.0, curve.EvaluateTangent(0.01));
        }

        [TestMethod]
        public void EvaluateSecant_NearZero_ReturnsInitialTangent()
        {
            var curve = CreateTrilinearCurve();
            double secant = curve.EvaluateSecant(1e-12);
            double tangent0 = curve.EvaluateTangent(0.0);
            Assert.AreEqual(tangent0, secant, 1e-3);
        }

        [TestMethod]
        public void EvaluateSecant_AtControlPoint_EqualsM_over_Phi()
        {
            var curve = CreateTrilinearCurve();
            double secant = curve.EvaluateSecant(0.005);
            Assert.AreEqual(200.0 / 0.005, secant, 1e-6);
        }

        [TestMethod]
        public void Constructor_DuplicatePhiPoints_AreMerged()
        {
            // φ=0.001が重複 → 平均化されるべき
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (0.001, 90.0),
                (0.001, 110.0),  // 重複
                (0.01, 300.0),
            });
            Assert.AreEqual(100.0, curve.EvaluateMoment(0.001), 1e-6);
        }

        [TestMethod]
        public void Constructor_UnsortedInput_IsSorted()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.01, 300.0),
                (0.0, 0.0),
                (0.001, 100.0),
            });
            Assert.AreEqual(0.0, curve.Points[0].Phi);
            Assert.AreEqual(0.001, curve.Points[1].Phi);
            Assert.AreEqual(0.01, curve.Points[2].Phi);
        }
    }

    /// <summary>
    /// Node（FEM節点）のテスト
    /// </summary>
    [TestClass]
    public class NodeTests
    {
        [TestMethod]
        public void DefaultBoundary_AllFree()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            Assert.IsFalse(node.Boundary.Ux);
            Assert.IsFalse(node.Boundary.Rz);
        }

        [TestMethod]
        public void SetBoundary_SetsCorrectDof()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            node.SetBoundary(new Boundary(true, false, false, false, false, false));
            Assert.IsTrue(node.Boundary.Ux);
            Assert.IsFalse(node.Boundary.Uy);
        }

        [TestMethod]
        public void HasMasterSlave_NoMasters_ReturnsFalse()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            Assert.IsFalse(node.HasMasterSlave);
        }

        [TestMethod]
        public void HasMasterSlave_WithMaster_ReturnsTrue()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            var master = new Node { Name = "M1", Coord = new Point3D(1, 0, 0) };
            node.SetMasterNode(0, master);
            Assert.IsTrue(node.HasMasterSlave);
        }

        [TestMethod]
        public void InitializeDisp_ResetsToZero()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            node.CumulativeDisp = new NodeDisp(1, 2, 3, 4, 5, 6);
            node.InitializeDisp();
            Assert.AreEqual(0, node.CumulativeDisp.Ux);
            Assert.AreEqual(0, node.CumulativeDisp.Rz);
        }

        [TestMethod]
        public void IsForcedDisped_DefaultFalse()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            Assert.IsFalse(node.IsForcedDisped);
        }
    }

    /// <summary>
    /// Beam（梁要素）のテスト
    /// </summary>
    [TestClass]
    public class BeamTests
    {
        private static (Node ni, Node nj, Section sec) CreateSimpleBeam(double length = 1.0)
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(length, 0, 0) };
            // 鋼材: E=205e6 kN/m², ν=0.3
            var mat = new Material(205e6, 0.3);
            // 断面: AX=0.01m², IY=IZ=1e-4 m⁴
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            return (ni, nj, sec);
        }

        [TestMethod]
        public void Constructor_ComputesLength()
        {
            var (ni, nj, sec) = CreateSimpleBeam(2.5);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            Assert.AreEqual(2.5, beam.Length, 1e-10);
        }

        [TestMethod]
        public void SetKe_Tangent_ProducesSymmetricMatrix()
        {
            var (ni, nj, sec) = CreateSimpleBeam(1.0);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            beam.SetKe(true);
            var ke = beam.KeTan;
            Assert.IsNotNull(ke);
            Assert.AreEqual(12, ke.RowCount);
            Assert.AreEqual(12, ke.ColumnCount);

            // 対称性チェック
            for (int i = 0; i < 12; i++)
                for (int j = i + 1; j < 12; j++)
                    Assert.AreEqual(ke[i, j], ke[j, i], 1e-6,
                        $"K[{i},{j}]={ke[i, j]} != K[{j},{i}]={ke[j, i]}");
        }

        [TestMethod]
        public void SetKe_Tangent_DiagonalPositive()
        {
            var (ni, nj, sec) = CreateSimpleBeam(1.0);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            beam.SetKe(true);
            var ke = beam.KeTan;

            // 対角成分はすべて正（剛性マトリクスの基本性質）
            for (int i = 0; i < 12; i++)
                Assert.IsTrue(ke[i, i] >= 0, $"K[{i},{i}]={ke[i, i]} is negative");
        }

        [TestMethod]
        public void SetKe_AxialStiffness_EqualsEA_over_L()
        {
            var (ni, nj, sec) = CreateSimpleBeam(2.0);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            beam.SetKe(true);
            var ke = beam.KeTan;

            double EA_L = sec.Material.E * sec.AX / 2.0;
            // K[0,0]（軸方向剛性）= EA/L
            Assert.AreEqual(EA_L, ke[0, 0], EA_L * 1e-10);
            // K[6,6] も同値
            Assert.AreEqual(EA_L, ke[6, 6], EA_L * 1e-10);
            // K[0,6] = -EA/L
            Assert.AreEqual(-EA_L, ke[0, 6], EA_L * 1e-10);
        }

        [TestMethod]
        public void SetKe_Secant_AlsoWorks()
        {
            var (ni, nj, sec) = CreateSimpleBeam(1.0);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            beam.SetKe(false);
            Assert.IsNotNull(beam.KeSec);
            Assert.AreEqual(12, beam.KeSec.RowCount);
        }

        [TestMethod]
        public void SetKe_WithReducedStiffness_LowerValues()
        {
            var (ni, nj, sec) = CreateSimpleBeam(1.0);
            var beam1 = new Beam("B1", sec, ni, nj, 1.0, 1.0);
            beam1.SetKe(true);
            double fullK11 = beam1.KeTan[1, 1]; // せん断剛性

            // 剛性低減率 50%
            var beam2 = new Beam("B2", sec, ni, nj, 1.0, 1.0);
            beam2.KTan_y = 0.5;
            beam2.KTan_z = 0.5;
            beam2.SetKe(true);
            double reducedK11 = beam2.KeTan[1, 1];

            Assert.IsTrue(reducedK11 < fullK11,
                $"Reduced K[1,1]={reducedK11} should be < Full K[1,1]={fullK11}");
        }
    }

    /// <summary>
    /// AnaModel（FEM解析モデル）の基本テスト
    /// </summary>
    [TestClass]
    public class AnaModelTests
    {
        /// <summary>
        /// 片持ち梁（1要素）の最小モデルを構築して基本動作を検証
        /// </summary>
        private static AnaModel CreateCantileverModel()
        {
            // 固定端（全拘束）
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            // 自由端
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();

            var model = new AnaModel(
                inputModel,
                [nodeA, nodeB],
                [beam],
                [],  // dummyBeams
                [],  // rigidBodies
                [],  // horizontalSoilSprings
                []   // rotationalSprings
            );
            return model;
        }

        [TestMethod]
        public void Constructor_CountsFreeDofs()
        {
            var model = CreateCantileverModel();
            // nodeA: 6 fixed, nodeB: 6 free → CountFree=6
            Assert.AreEqual(6, model.CountFree);
        }

        [TestMethod]
        public void MapOnKtanMat_CreatesNonNullMatrix()
        {
            var model = CreateCantileverModel();
            model.MapOnKtanMat();
            Assert.IsNotNull(model.KAA_tan);
            Assert.AreEqual(6, model.KAA_tan.RowCount);
            Assert.AreEqual(6, model.KAA_tan.ColumnCount);
        }

        [TestMethod]
        public void MapOnKtanMat_SymmetricMatrix()
        {
            var model = CreateCantileverModel();
            model.MapOnKtanMat();
            var K = model.KAA_tan;
            for (int i = 0; i < K.RowCount; i++)
                for (int j = i + 1; j < K.ColumnCount; j++)
                    Assert.AreEqual(K[i, j], K[j, i], Math.Abs(K[i, j]) * 1e-10 + 1e-20,
                        $"K[{i},{j}] != K[{j},{i}]");
        }

        [TestMethod]
        public void MapOnKtanMat_PositiveDiagonal()
        {
            var model = CreateCantileverModel();
            model.MapOnKtanMat();
            var K = model.KAA_tan;
            for (int i = 0; i < K.RowCount; i++)
                Assert.IsTrue(K[i, i] > 0, $"K[{i},{i}]={K[i, i]} should be positive");
        }

        [TestMethod]
        public void VectorF_InitializedToZero()
        {
            var model = CreateCantileverModel();
            Assert.AreEqual(6, model.VectorF.Count);
            Assert.AreEqual(0.0, model.VectorF.L2Norm());
        }

        [TestMethod]
        public void FindR_ZeroLoad_ZeroResidual()
        {
            var model = CreateCantileverModel();
            model.MapOnKtanMat();
            model.MapOnKsecMat();
            model.UpdateVectorDOFForcedDisp();
            model.InitializeVectorR();
            model.SetT();
            model.FindR();
            // 荷重0、変位0 → 残差0
            Assert.AreEqual(0.0, model.NormsROnNormsFint, 1e-10);
        }
    }

    /// <summary>
    /// 片持ち梁の単純荷重問題（直接解法テスト）
    /// </summary>
    [TestClass]
    public class SolverIntegrationTests
    {
        [TestMethod]
        public void Cantilever_PointLoad_DisplacementCorrect()
        {
            // 片持ち梁 L=1m, 先端にP=100kN (Y方向)
            // δ = PL³/(3EI) (理論解)
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            double E = 205e6;  // kN/m²
            double IZ = 1e-4;  // m⁴
            var mat = new Material(E, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, IZ);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(
                inputModel,
                [nodeA, nodeB],
                [beam],
                [], [], [], []
            );

            // Y方向に100kN載荷
            double P = 100.0;
            nodeB.SetIncrementalLoad(new NodeLoad(0, P, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            // 求解: VectorR を荷重ベクトルで初期化
            model.MapOnKtanMat();
            model.InitializeVectorR();
            // R = F（初回は内力ゼロなので残差＝外力）
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            // 理論解: δ = PL³/(3EI)
            double L = 1.0;
            double theoreticalDeflection = P * L * L * L / (3 * E * IZ);
            double computedDeflection = nodeB.CumulativeDisp.Uy;

            Assert.AreEqual(theoreticalDeflection, computedDeflection,
                theoreticalDeflection * 0.01, // 1%以内
                $"理論解={theoreticalDeflection:E4}, 計算値={computedDeflection:E4}");
        }

        [TestMethod]
        public void Cantilever_AxialLoad_DisplacementCorrect()
        {
            // 片持ち梁 L=2m, 先端に軸力 N=500kN
            // δ = NL/(EA)
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(2, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            double E = 205e6;
            double AX = 0.01;
            var mat = new Material(E, 0.3);
            var sec = new Section(mat, AX, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(
                inputModel,
                [nodeA, nodeB],
                [beam],
                [], [], [], []
            );

            double N = 500.0;
            nodeB.SetIncrementalLoad(new NodeLoad(N, 0, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            double L = 2.0;
            double theoreticalDisp = N * L / (E * AX);
            double computedDisp = nodeB.CumulativeDisp.Ux;

            Assert.AreEqual(theoreticalDisp, computedDisp,
                theoreticalDisp * 0.01,
                $"理論解={theoreticalDisp:E4}, 計算値={computedDisp:E4}");
        }
    }

    /// <summary>
    /// 計算例JSONの読み込みテスト（回帰テスト）
    /// </summary>
    [TestClass]
    public class ExampleLoadTests
    {
        private static string GetExamplesDir()
        {
            // テスト実行時のアセンブリから相対パスでExamplesフォルダを探す
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // ビルド出力からプロジェクトルートへ遡る
            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(projectRoot, "Graphics_r1", "Examples");
        }

        [TestMethod]
        public void Example1_Deserializes_WithoutError()
        {
            var examplesDir = GetExamplesDir();
            var filePath = Path.Combine(examplesDir, "Example1.json");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive($"Example file not found: {filePath}");
                return;
            }

            var json = File.ReadAllText(filePath);
            var ground = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json);
            Assert.IsNotNull(ground);
            Assert.IsNotNull(ground.GroundLayers);
            Assert.IsTrue(ground.GroundLayers.Count > 0, "地層データが0件です");
        }

        [TestMethod]
        public void AllExamples_Deserialize_WithoutError()
        {
            var examplesDir = GetExamplesDir();
            if (!Directory.Exists(examplesDir))
            {
                Assert.Inconclusive($"Examples directory not found: {examplesDir}");
                return;
            }

            var files = Directory.GetFiles(examplesDir, "Example*.json");
            Assert.IsTrue(files.Length > 0, "計算例ファイルが見つかりません");

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var ground = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json);
                Assert.IsNotNull(ground, $"{Path.GetFileName(file)} のデシリアライズに失敗");
                Assert.IsNotNull(ground.GroundLayers, $"{Path.GetFileName(file)} に地層データがありません");
            }
        }

        [TestMethod]
        public void Example9_WithEngineeringBedrock_NullH_HandledSafely()
        {
            // 工学的基盤を含む計算例（H=nullとなる行がある）の回帰テスト
            var examplesDir = GetExamplesDir();
            var filePath = Path.Combine(examplesDir, "Example9.json");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive($"Example9.json not found");
                return;
            }

            var json = File.ReadAllText(filePath);
            var ground = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json);
            Assert.IsNotNull(ground);

            // 工学的基盤の行が含まれるか確認
            bool hasEngineeringBedrock = ground.GroundLayers.Any(l => l.IsEngineeringBedrock);
            Assert.IsTrue(hasEngineeringBedrock, "Example9 should contain engineering bedrock layer");
        }
    }

    /// <summary>
    /// バージョン情報のテスト
    /// </summary>
    [TestClass]
    public class VersionTests
    {
        [TestMethod]
        public void AppVersion_IsNotEmpty()
        {
            var version = PileDesign.ViewModels.MainWindowViewModel.AppVersion;
            Assert.IsFalse(string.IsNullOrWhiteSpace(version),
                "AppVersionが空です。csprojにVersionが設定されていない可能性があります。");
        }

        [TestMethod]
        public void AppVersion_DoesNotContainPlusSign()
        {
            // InformationalVersionの+以降（ソースハッシュ）は除外されるべき
            var version = PileDesign.ViewModels.MainWindowViewModel.AppVersion;
            Assert.IsFalse(version.Contains('+'),
                $"バージョン '{version}' に '+' が含まれています");
        }
    }
}
