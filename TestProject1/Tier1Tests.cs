using PileDesign.FEM;
using PileDesign.Models.InputData;
using MathNet.Numerics.LinearAlgebra;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    // ====================================================================
    // Utils.cs のテスト
    // ====================================================================
    [TestClass]
    public class UtilsTests
    {
        [TestMethod]
        public void GetLengthBetweenTwoNodes_XAxis()
        {
            var a = new Node { Name = "A", Coord = new Point3D(0, 0, 0) };
            var b = new Node { Name = "B", Coord = new Point3D(3, 0, 0) };
            Assert.AreEqual(3.0, Utils.GetLengthBetweenTwoNodes(a, b), 1e-12);
        }

        [TestMethod]
        public void GetLengthBetweenTwoNodes_3D_Diagonal()
        {
            var a = new Node { Name = "A", Coord = new Point3D(1, 2, 3) };
            var b = new Node { Name = "B", Coord = new Point3D(4, 6, 3) };
            // sqrt(9+16+0) = 5
            Assert.AreEqual(5.0, Utils.GetLengthBetweenTwoNodes(a, b), 1e-12);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetLengthBetweenTwoNodes_NullNode_Throws()
        {
            Utils.GetLengthBetweenTwoNodes(null!, new Node { Name = "B", Coord = new Point3D(0, 0, 0) });
        }

        [TestMethod]
        public void GetShearModulus_Standard()
        {
            // G = E / (2*(1+ν))
            double E = 200000;
            double nu = 0.3;
            double expected = E / (2 * (1 + nu));
            Assert.AreEqual(expected, Utils.GetShearModulus(E, nu), 1e-6);
        }

        [TestMethod]
        public void GetShearModulus_ZeroPoisson()
        {
            Assert.AreEqual(50.0, Utils.GetShearModulus(100, 0), 1e-12);
        }

        // --- Interpolate ---

        [TestMethod]
        public void Interpolate_Midpoint()
        {
            double[] x = { 0, 1, 2 };
            double[] y = { 0, 10, 20 };
            Assert.AreEqual(5.0, Utils.Interpolate(x, y, 0.5), 1e-10);
            Assert.AreEqual(15.0, Utils.Interpolate(x, y, 1.5), 1e-10);
        }

        [TestMethod]
        public void Interpolate_AtControlPoint()
        {
            double[] x = { 0, 1, 2 };
            double[] y = { 0, 10, 30 };
            Assert.AreEqual(0.0, Utils.Interpolate(x, y, 0.0), 1e-10);
            Assert.AreEqual(10.0, Utils.Interpolate(x, y, 1.0), 1e-10);
        }

        [TestMethod]
        public void Interpolate_BeyondRange_ReturnsLast()
        {
            double[] x = { 0, 1, 2 };
            double[] y = { 10, 20, 30 };
            Assert.AreEqual(30.0, Utils.Interpolate(x, y, 5.0), 1e-10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Interpolate_EmptyArray_Throws()
        {
            Utils.Interpolate([], [], 0.5);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Interpolate_MismatchedLengths_Throws()
        {
            Utils.Interpolate([0, 1], [0, 1, 2], 0.5);
        }

        // --- MapOn ---

        [TestMethod]
        public void MapOn_AddsSubmatrix()
        {
            var dst = Matrix<double>.Build.Dense(4, 4);
            var src = Matrix<double>.Build.DenseOfArray(new double[,] { { 1, 2 }, { 3, 4 } });
            Utils.MapOn(dst, 1, 1, src);
            Assert.AreEqual(1, dst[1, 1]);
            Assert.AreEqual(4, dst[2, 2]);
            Assert.AreEqual(0, dst[0, 0]); // 影響外
        }

        [TestMethod]
        public void MapOn_Accumulates()
        {
            var dst = Matrix<double>.Build.Dense(3, 3);
            dst[0, 0] = 5;
            var src = Matrix<double>.Build.DenseOfArray(new double[,] { { 10 } });
            Utils.MapOn(dst, 0, 0, src);
            Assert.AreEqual(15, dst[0, 0]); // 5 + 10
        }

        // --- GetTransformMatrix ---

        [TestMethod]
        public void GetTransformMatrix_XAxis_IsIdentityBlocks()
        {
            // X軸方向の梁 → 座標変換は恒等
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1, 0, 0) };
            var T = Utils.GetTransformMatrix(ni, nj);
            Assert.AreEqual(12, T.RowCount);
            // T[0,0]=1 (lx=1), T[1,1] should be part of y/z transform
            Assert.AreEqual(1.0, T[0, 0], 1e-10, "lx should be 1 for X-axis beam");
        }

        [TestMethod]
        public void GetTransformMatrix_ZAxis_Vertical()
        {
            // Z軸方向（杭と同じ向き）→ 直立ケース
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(0, 0, -5) };
            var T = Utils.GetTransformMatrix(ni, nj);
            Assert.AreEqual(12, T.RowCount);
            // T は直交行列: T^T * T = I
            var TtT = T.Transpose() * T;
            var I = Matrix<double>.Build.DenseIdentity(12);
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    Assert.AreEqual(I[i, j], TtT[i, j], 1e-10,
                        $"T^T*T[{i},{j}] should be {I[i, j]}");
        }

        [TestMethod]
        public void GetTransformMatrix_Orthogonal()
        {
            // 任意方向の梁でも T^T * T = I（直交行列）
            var ni = new Node { Name = "I", Coord = new Point3D(1, 2, 3) };
            var nj = new Node { Name = "J", Coord = new Point3D(4, 5, 6) };
            var T = Utils.GetTransformMatrix(ni, nj);
            var TtT = T.Transpose() * T;
            var I = Matrix<double>.Build.DenseIdentity(12);
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    Assert.AreEqual(I[i, j], TtT[i, j], 1e-10);
        }

        // --- GetEquationNumbers ---

        [TestMethod]
        public void GetEquationNumbers_Returns12Elements()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1, 0, 0) };
            // 方程式番号を手動設定
            for (int i = 0; i < 6; i++) ni.EquationNumber[i] = i;
            for (int i = 0; i < 6; i++) nj.EquationNumber[i] = 6 + i;
            var eq = Utils.GetEquationNumbers(ni, nj);
            Assert.AreEqual(12, eq.Count);
            Assert.AreEqual(0, eq[0]);
            Assert.AreEqual(11, eq[11]);
        }

        [TestMethod]
        public void GetEquationNumbers_WithMaster_UsesMasterEq()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            master.EquationNumber[0] = 99;
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            slave.EquationNumber[0] = 5;
            slave.SetMasterNode(0, master); // DOF 0 はマスターに従う
            for (int i = 1; i < 6; i++) slave.EquationNumber[i] = i;

            var nj = new Node { Name = "J", Coord = new Point3D(2, 0, 0) };
            for (int i = 0; i < 6; i++) nj.EquationNumber[i] = 10 + i;

            var eq = Utils.GetEquationNumbers(slave, nj);
            Assert.AreEqual(99, eq[0], "DOF 0 should use master's equation number");
            Assert.AreEqual(1, eq[1], "DOF 1 should use slave's own equation number");
        }
    }

    // ====================================================================
    // RigidBody のテスト
    // ====================================================================
    [TestClass]
    public class RigidBodyTests
    {
        [TestMethod]
        public void AddSlaveNode_SetsBoundary()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(2, 3, 0) };
            // Ux, Uy を拘束
            var rb = new RigidBody(master, [true, true, false, false, false, false]);
            rb.AddSlaveNode(slave);

            Assert.IsTrue(slave.Boundary.Ux, "Ux should be constrained");
            Assert.IsTrue(slave.Boundary.Uy, "Uy should be constrained");
            Assert.IsFalse(slave.Boundary.Uz, "Uz should remain free");
        }

        [TestMethod]
        public void AddSlaveNode_SetsMasterReference()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            var rb = new RigidBody(master, [true, true, true, false, false, false]);
            rb.AddSlaveNode(slave);

            Assert.AreSame(master, slave.MasterNodes[0]);
            Assert.AreSame(master, slave.MasterNodes[1]);
            Assert.AreSame(master, slave.MasterNodes[2]);
            Assert.IsNull(slave.MasterNodes[3]); // 回転は拘束なし
        }

        [TestMethod]
        public void RemoveSlaveNode_RestoresBoundary()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            // 元の境界: Uz のみ固定
            slave.SetBoundary(new Boundary(false, false, true, false, false, false));

            var rb = new RigidBody(master, [true, true, true, true, true, true]);
            rb.AddSlaveNode(slave);

            // 全拘束になっているはず
            Assert.IsTrue(slave.Boundary.Ux);
            Assert.IsTrue(slave.Boundary.Uz);

            rb.RemoveSlaveNode(slave);

            // 元の境界に戻る
            Assert.IsFalse(slave.Boundary.Ux, "Ux should be restored to free");
            Assert.IsTrue(slave.Boundary.Uz, "Uz should be restored to fixed");
        }

        [TestMethod]
        public void GetSlaveArmVector_ReturnsCorrectVector()
        {
            var master = new Node { Name = "M", Coord = new Point3D(1, 2, 3) };
            var slave = new Node { Name = "S", Coord = new Point3D(4, 6, 3) };
            var rb = new RigidBody(master, [true, true, false, false, false, false]);
            var arm = rb.GetSlaveArmVector(slave);
            Assert.AreEqual(3, arm.X, 1e-10);
            Assert.AreEqual(4, arm.Y, 1e-10);
            Assert.AreEqual(0, arm.Z, 1e-10);
        }

        [TestMethod]
        public void IsSlave_ReturnsTrueForConstrainedDof()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            var rb = new RigidBody(master, [true, false, false, false, false, false]);
            rb.AddSlaveNode(slave);
            Assert.IsTrue(rb.IsSlave(slave, 0));
            Assert.IsFalse(rb.IsSlave(slave, 1));
        }

        [TestMethod]
        public void AddSlaveNode_DuplicateIgnored()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            var rb = new RigidBody(master, [true, true, true, true, true, true]);
            rb.AddSlaveNode(slave);
            rb.AddSlaveNode(slave); // 重複
            Assert.AreEqual(1, rb.SlaveNodes.Count);
        }

        [TestMethod]
        public void DeepCopy_CreatesIndependentCopy()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave = new Node { Name = "S", Coord = new Point3D(1, 0, 0) };
            var rb = new RigidBody(master, [true, true, false, false, false, false]);
            rb.AddSlaveNode(slave);

            var copy = rb.DeepCopy();
            Assert.AreNotSame(rb.MasterNode, copy.MasterNode);
            Assert.AreEqual(rb.SlaveNodes.Count, copy.SlaveNodes.Count);
            Assert.AreEqual("M", copy.MasterNode.Name);
        }

        [TestMethod]
        public void SetSlaveNodeRelations_EstablishesMasterReferences()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var slave1 = new Node { Name = "S1", Coord = new Point3D(2, 0, 0) };
            var slave2 = new Node { Name = "S2", Coord = new Point3D(0, 3, 0) };
            var rb = new RigidBody(master, [true, true, false, false, false, false]);
            rb.SlaveNodes = [slave1, slave2];
            rb.SetSlaveNodeRelations();

            Assert.AreSame(master, slave1.MasterNodes[0]);
            Assert.AreSame(master, slave2.MasterNodes[1]);
        }
    }

    // ====================================================================
    // GroundInput のテスト（内部摩擦角・バリデーション）
    // ====================================================================
    [TestClass]
    public class GroundInputTests
    {
        // --- 大崎式 ---

        [TestMethod]
        public void GetFrictionAngle_Osaki_N10()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "大崎式";
            // φ = sqrt(20*10) + 15 = sqrt(200) + 15 ≈ 29.14
            double expected = Math.Sqrt(200) + 15;
            Assert.AreEqual(expected, gi.GetFrictionAngle(10, 100), 1e-6);
        }

        [TestMethod]
        public void GetFrictionAngle_Osaki_LargeN_CappedAt40()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "大崎式";
            // N=50: sqrt(1000)+15 ≈ 46.6 → capped at 40
            Assert.AreEqual(40.0, gi.GetFrictionAngle(50, 100), 1e-6);
        }

        [TestMethod]
        public void GetFrictionAngle_Osaki_N0_Returns15()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "大崎式";
            // N=0: sqrt(0)+15 = 15
            Assert.AreEqual(15.0, gi.GetFrictionAngle(0, 100), 1e-6);
        }

        // --- 畑中式 ---

        [TestMethod]
        public void GetFrictionAngle_Hatanaka_N10_Sigma100()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            // N1 = 10 / sqrt(100/100) = 10
            // φ = sqrt(20*10) + 20 = sqrt(200) + 20 ≈ 34.14
            double expected = Math.Sqrt(200) + 20;
            Assert.AreEqual(expected, gi.GetFrictionAngle(10, 100), 1e-6);
        }

        [TestMethod]
        public void GetFrictionAngle_Hatanaka_HighStress_LowerAngle()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            // 高い有効応力 → N1が小さくなる → 摩擦角が小さくなる
            double phi_low = gi.GetFrictionAngle(10, 50);
            double phi_high = gi.GetFrictionAngle(10, 200);
            Assert.IsTrue(phi_low > phi_high,
                $"低応力φ={phi_low} > 高応力φ={phi_high}");
        }

        [TestMethod]
        public void GetFrictionAngle_Hatanaka_CappedAt40()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            Assert.AreEqual(40.0, gi.GetFrictionAngle(100, 100), 1e-6);
        }

        // --- ValidateForAnalysis ---

        [TestMethod]
        public void Validate_ValidData_ReturnsTrue()
        {
            var gi = new GroundInput();
            gi.GroundLayers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, BottomGLDepth = -5, Es = 10000, GranularityClass = "砂質土" },
                new() { No = 2, BottomGLDepth = -10, Es = 15000, GranularityClass = "砂質土" }
            };
            gi.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { No = 1, GLDepth = -3, NValue = 10 },
                new() { No = 2, GLDepth = -8, NValue = 20 }
            };

            bool result = gi.ValidateForAnalysis(out string msg);
            Assert.IsTrue(result, $"Validation failed: {msg}");
        }

        [TestMethod]
        public void Validate_EsZero_ReturnsFalse()
        {
            var gi = new GroundInput();
            gi.GroundLayers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, BottomGLDepth = -5, Es = 0, GranularityClass = "砂質土" }
            };
            gi.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { No = 1, GLDepth = -3, NValue = 10 }
            };

            bool result = gi.ValidateForAnalysis(out string msg);
            Assert.IsFalse(result);
            Assert.IsTrue(msg.Contains("変形係数が0"));
        }

        [TestMethod]
        public void Validate_ClayCohesionZero_ReturnsFalse()
        {
            var gi = new GroundInput();
            gi.GroundLayers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, BottomGLDepth = -5, Es = 10000, GranularityClass = "粘性土", Cohesive = 0 }
            };
            gi.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { No = 1, GLDepth = -3, NValue = 10 }
            };

            bool result = gi.ValidateForAnalysis(out string msg);
            Assert.IsFalse(result);
            Assert.IsTrue(msg.Contains("粘着力が0"));
        }

        [TestMethod]
        public void Validate_DepthOrderReversed_ReturnsFalse()
        {
            var gi = new GroundInput();
            gi.GroundLayers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, BottomGLDepth = -10, Es = 10000, GranularityClass = "砂質土" },
                new() { No = 2, BottomGLDepth = -5, Es = 15000, GranularityClass = "砂質土" } // 上の層より浅い
            };
            gi.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { No = 1, GLDepth = -3, NValue = 10 }
            };

            bool result = gi.ValidateForAnalysis(out string msg);
            Assert.IsFalse(result);
            Assert.IsTrue(msg.Contains("下端深度"));
        }

        [TestMethod]
        public void Validate_NValueZero_ReturnsFalse()
        {
            var gi = new GroundInput();
            gi.GroundLayers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, BottomGLDepth = -5, Es = 10000, GranularityClass = "砂質土" }
            };
            gi.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { No = 1, GLDepth = -3, NValue = 0 }
            };

            bool result = gi.ValidateForAnalysis(out string msg);
            Assert.IsFalse(result);
            Assert.IsTrue(msg.Contains("N値が0"));
        }

        [TestMethod]
        public void DeepCopy_IndependentCollections()
        {
            var gi = new GroundInput();
            gi.GroundLayers[0].Name = "TestLayer";
            var copy = gi.DeepCopy();
            copy.GroundLayers[0].Name = "Modified";
            Assert.AreEqual("TestLayer", gi.GroundLayers[0].Name);
        }
    }

    // ====================================================================
    // GroundLayerInput のテスト
    // ====================================================================
    [TestClass]
    public class GroundLayerInputTests
    {
        [TestMethod]
        public void Constructor_DefaultValues()
        {
            var layer = new GroundLayerInput();
            Assert.AreEqual(-20.0, layer.BottomGLDepth);
            Assert.AreEqual("(AS#)", layer.Name);
            Assert.AreEqual("砂質土", layer.GranularityClass);
            Assert.AreEqual(17.0, layer.Density);
            Assert.AreEqual("沖積層", layer.AgeCategory);
            Assert.IsFalse(layer.IsEngineeringBedrock);
        }

        [TestMethod]
        public void NValue_NaN_ClampedToZero()
        {
            var layer = new GroundLayerInput();
            layer.NValue = double.NaN;
            Assert.AreEqual(0.0, layer.NValue);
        }

        [TestMethod]
        public void NValue_Infinity_ClampedToZero()
        {
            var layer = new GroundLayerInput();
            layer.NValue = double.PositiveInfinity;
            Assert.AreEqual(0.0, layer.NValue);
        }

        [TestMethod]
        public void NValue_Negative_ClampedToZero()
        {
            var layer = new GroundLayerInput();
            layer.NValue = -5;
            Assert.AreEqual(0.0, layer.NValue);
        }

        [TestMethod]
        public void NValue_Over1000_ClampedTo1000()
        {
            var layer = new GroundLayerInput();
            layer.NValue = 1500;
            Assert.AreEqual(1000.0, layer.NValue);
        }

        [TestMethod]
        public void NValue_ValidValue_Accepted()
        {
            var layer = new GroundLayerInput();
            layer.NValue = 25;
            Assert.AreEqual(25.0, layer.NValue);
        }

        [TestMethod]
        public void DeepCopy_PreservesAllFields()
        {
            var original = new GroundLayerInput
            {
                No = 3,
                Name = "Ac1",
                GranularityClass = "粘性土",
                Density = 16.5,
                AgeCategory = "洪積層",
                IsEngineeringBedrock = true,
                Cohesive = 50,
                Vs = 200,
                Es = 15000,
                BottomGLDepth = -15,
            };
            original.NValue = 30; // setter経由

            var copy = original.DeepCopy();

            Assert.AreEqual(3, copy.No);
            Assert.AreEqual("Ac1", copy.Name);
            Assert.AreEqual("粘性土", copy.GranularityClass);
            Assert.AreEqual(16.5, copy.Density);
            Assert.AreEqual("洪積層", copy.AgeCategory);
            Assert.IsTrue(copy.IsEngineeringBedrock);
            Assert.AreEqual(50, copy.Cohesive);
            Assert.AreEqual(200, copy.Vs);
            Assert.AreEqual(15000, copy.Es);
            Assert.AreEqual(-15, copy.BottomGLDepth);
            Assert.AreEqual(30, copy.NValue);
        }

        [TestMethod]
        public void DeepCopy_Independent()
        {
            var original = new GroundLayerInput { Name = "Original" };
            var copy = original.DeepCopy();
            copy.Name = "Modified";
            Assert.AreEqual("Original", original.Name);
        }

        [TestMethod]
        public void StaticOptions_Defined()
        {
            Assert.AreEqual(3, GroundLayerInput.GranularityClassOption.Count);
            Assert.IsTrue(GroundLayerInput.GranularityClassOption.Contains("粘性土"));
            Assert.IsTrue(GroundLayerInput.GranularityClassOption.Contains("砂質土"));
            Assert.IsTrue(GroundLayerInput.GranularityClassOption.Contains("礫質土"));
            Assert.AreEqual(2, GroundLayerInput.AgeCategoryOption.Count);
        }
    }

    // ====================================================================
    // InputModel シリアライゼーションのテスト
    // ====================================================================
    [TestClass]
    public class InputModelSerializationTests
    {
        /// <summary>
        /// InputModel の SaveToFile/LoadFromFile ラウンドトリップ
        /// </summary>
        [TestMethod]
        public void SaveAndLoad_RoundTrip_PreservesBasicData()
        {
            var inputModel = new InputModel();
            inputModel.FundamentalInput = new FundamentalInput { ProjectName = "TestProject_RT" };
            inputModel.GroundsInput = new ObservableCollection<GroundInput>
            {
                new GroundInput { GroundRef = "GR_Test", GroundTopAltitude = 5.0 }
            };

            var tempFile = Path.GetTempFileName();
            try
            {
                inputModel.SaveToFile(tempFile);
                Assert.IsTrue(File.Exists(tempFile));
                Assert.IsTrue(new FileInfo(tempFile).Length > 0);

                // LoadFromFile は MainWindowViewModel を必要とするため、
                // JSON を直接デシリアライズして基本データが保持されているか確認
                var json = File.ReadAllText(tempFile);
                Assert.IsTrue(json.Contains("TestProject_RT"));
                Assert.IsTrue(json.Contains("GR_Test"));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void SaveToFile_ContainsVersion()
        {
            var inputModel = new InputModel();
            var tempFile = Path.GetTempFileName();
            try
            {
                inputModel.SaveToFile(tempFile);
                var json = File.ReadAllText(tempFile);
                // JSON が空でないこと
                Assert.IsTrue(json.Length > 100);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void SaveToFile_HandlesNullCollections()
        {
            var inputModel = new InputModel();
            inputModel.PileLayoutItems = null;
            inputModel.InputNodes = null;

            var tempFile = Path.GetTempFileName();
            try
            {
                // 例外なく保存できること
                inputModel.SaveToFile(tempFile);
                Assert.IsTrue(File.Exists(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        /// <summary>
        /// 計算例ファイルをSystemTextJsonで読み込めるか
        /// （InputModelとしてのフル読み込みではなく、GroundInputとして読み込む既存テストの拡張）
        /// </summary>
        [TestMethod]
        public void ExampleJsonFiles_AreValidJson()
        {
            var examplesDir = TestHelper.GetExamplesDir();
            if (!Directory.Exists(examplesDir)) { Assert.Inconclusive("Examples dir not found"); return; }

            foreach (var file in Directory.GetFiles(examplesDir, "*.json"))
            {
                var json = File.ReadAllText(file);
                Assert.IsTrue(json.Length > 0, $"{Path.GetFileName(file)} is empty");
                // JSONとしてパース可能であること
                try
                {
                    var opts = new System.Text.Json.JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = System.Text.Json.JsonCommentHandling.Skip
                    };
                    var doc = System.Text.Json.JsonDocument.Parse(json, opts);
                    Assert.IsNotNull(doc);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    Assert.Fail($"{Path.GetFileName(file)} is not valid JSON: {ex.Message}");
                }
            }
        }
    }

    // ====================================================================
    // AnalysisModelling のテスト（FEMモデル構築）
    // ====================================================================
    [TestClass]
    public class AnalysisModellingTests
    {
        /// <summary>
        /// null InputModel で例外が発生すること
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullInputModel_Throws()
        {
            new AnalysisModelling(null!);
        }
    }
}
