using PileDesign.FEM;
using MathNet.Numerics.LinearAlgebra;
using System.IO;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// 強制変位の処理（直接消去法／1-0法）のテスト
    /// </summary>
    [TestClass]
    public class ForcedDisplacementTests
    {
        /// <summary>
        /// 2節点モデルで強制変位を与え、解が一致するか検証
        /// nodeA: 全拘束（固定端）
        /// nodeB: 自由端、Ux方向に強制変位 δ=0.01m を与える
        /// 期待: Solver後の nodeB.Ux == 0.01
        /// </summary>
        [TestMethod]
        public void ForcedDisplacement_SingleDof_ProducesExactDisplacement()
        {
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

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            // nodeB に Ux=0.01m の強制変位を設定
            double delta = 0.01;
            nodeB.IsForcedDisped = true;
            nodeB.CumulativeForcedDisp = new NodeDisp(delta, 0, 0, 0, 0, 0);

            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();

            // R = 0（外力なし、残差は強制変位処理で設定される）
            Solver.SolveDisp(model);

            Assert.AreEqual(delta, nodeB.CumulativeDisp.Ux, delta * 0.01,
                $"強制変位 δ={delta}, 計算値={nodeB.CumulativeDisp.Ux}");
        }

        /// <summary>
        /// 強制変位の行・列消去が正しく行われるか直接検証
        /// </summary>
        [TestMethod]
        public void GetForcedDisp_ModifiesMatrixCorrectly()
        {
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

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            // Ux 強制変位
            double delta = 0.005;
            nodeB.IsForcedDisped = true;
            nodeB.CumulativeForcedDisp = new NodeDisp(delta, 0, 0, 0, 0, 0);

            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();

            var (Kmod, Rmod) = model.GetForcedDispOnLoadVectorAndStiffnessMatrix(true);

            // nodeB の Ux は EquationNumber[0]（最初の自由DOF）
            int eqUx = nodeB.EquationNumber[0];

            // 対角成分 = 1
            Assert.AreEqual(1.0, Kmod[eqUx, eqUx], 1e-12, "対角成分が1でない");

            // 非対角成分 = 0
            for (int i = 0; i < model.CountFree; i++)
            {
                if (i == eqUx) continue;
                Assert.AreEqual(0.0, Kmod[eqUx, i], 1e-12, $"K[{eqUx},{i}] が 0 でない");
                Assert.AreEqual(0.0, Kmod[i, eqUx], 1e-12, $"K[{i},{eqUx}] が 0 でない");
            }

            // 右辺 = 強制変位値
            Assert.AreEqual(delta, Rmod[eqUx], 1e-12, "右辺が強制変位値でない");
        }
    }

    /// <summary>
    /// 地盤ばね付き杭モデルのテスト
    /// </summary>
    [TestClass]
    public class SpringModelTests
    {
        [TestMethod]
        public void TwoNodeSpring_SetKe_CreatesCorrectMatrix()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(0, 0, -1) };

            var spring = new HorizontalSoilSpring("S1", ni, nj);
            double kh = 5000.0; // kN/m
            spring.SetKe(kh, kh, 0, 0, 0, 0, true);

            var ke = spring.KeTan;
            Assert.AreEqual(12, ke.RowCount);

            // K[0,0] = kx, K[0,6] = -kx
            Assert.AreEqual(kh, ke[0, 0], 1e-10);
            Assert.AreEqual(-kh, ke[0, 6], 1e-10);
            Assert.AreEqual(kh, ke[6, 6], 1e-10);

            // K[1,1] = ky, K[1,7] = -ky
            Assert.AreEqual(kh, ke[1, 1], 1e-10);
            Assert.AreEqual(-kh, ke[1, 7], 1e-10);
        }

        /// <summary>
        /// 梁＋地盤ばねモデル: 杭頭に水平力を載荷し、
        /// ばね剛性が大きいほど変位が小さくなることを検証
        /// </summary>
        [TestMethod]
        public void BeamWithSpring_HigherStiffness_SmallerDisplacement()
        {
            double dispSoft = SolvePileWithSpring(50000.0);    // 柔ばね
            double dispStiff = SolvePileWithSpring(500000.0); // 剛ばね

            Assert.IsTrue(dispStiff < dispSoft,
                $"剛ばね変位({dispStiff:E3})が柔ばね変位({dispSoft:E3})より大きい");
            Assert.IsTrue(dispSoft > 0, "変位が0以下");
            Assert.IsTrue(dispStiff > 0, "変位が0以下");
        }

        private static double SolvePileWithSpring(double springStiffness)
        {
            // 杭先端：固定、杭頭：自由、中間にばね
            var nodeTop = new Node
            {
                Name = "Top",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };
            var nodeMid = new Node
            {
                Name = "Mid",
                Coord = new Point3D(0, 0, -5),
                Boundary = new Boundary(false, false, false, false, false, false)
            };
            var nodeBot = new Node
            {
                Name = "Bot",
                Coord = new Point3D(0, 0, -10),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            // ばねの土側節点（固定）
            var nodeSoil = new Node
            {
                Name = "Soil",
                Coord = new Point3D(0, 0, -5),
                Boundary = new Boundary(true, true, true, true, true, true)
            };

            var mat = new Material(30e6, 0.2); // コンクリート
            var sec = new Section(mat, 0.25, 0.2, 0.2, 0.005, 0.003, 0.003);

            var beam1 = new Beam("B1", sec, nodeTop, nodeMid, 1.0, 1.0);
            var beam2 = new Beam("B2", sec, nodeMid, nodeBot, 1.0, 1.0);

            var spring = new HorizontalSoilSpring("S1", nodeMid, nodeSoil);
            spring.SetKe(springStiffness, springStiffness, 0, 0, 0, 0, true);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(
                inputModel,
                [nodeTop, nodeMid, nodeBot, nodeSoil],
                [beam1, beam2],
                [], [],
                [spring],
                []
            );

            // 杭頭に水平力 10kN（Solver内の50mm制限に引っかからない程度）
            nodeTop.SetIncrementalLoad(new NodeLoad(10, 0, 0, 0, 0, 0));
            nodeTop.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            return Math.Abs(nodeTop.CumulativeDisp.Ux);
        }
    }

    /// <summary>
    /// 複数要素梁の精度テスト
    /// </summary>
    [TestClass]
    public class MultiElementTests
    {
        /// <summary>
        /// 片持ち梁を複数要素に分割 → 1要素と同じ理論解に収束するか
        /// </summary>
        [TestMethod]
        public void Cantilever_MultiElement_ConvergesToTheory()
        {
            double L = 2.0;
            double P = 50.0;
            double E = 205e6;
            double IZ = 1e-4;
            double theory = P * L * L * L / (3.0 * E * IZ);

            double disp1 = SolveCantilever(L, P, E, IZ, 1);
            double disp4 = SolveCantilever(L, P, E, IZ, 4);
            double disp8 = SolveCantilever(L, P, E, IZ, 8);

            // 1要素でも理論解と一致（3次変位場を正確に表現可能）
            Assert.AreEqual(theory, disp1, theory * 0.02,
                $"1要素: 理論={theory:E4}, 計算={disp1:E4}");
            Assert.AreEqual(theory, disp4, theory * 0.02,
                $"4要素: 理論={theory:E4}, 計算={disp4:E4}");
            Assert.AreEqual(theory, disp8, theory * 0.02,
                $"8要素: 理論={theory:E4}, 計算={disp8:E4}");
        }

        private static double SolveCantilever(double L, double P, double E, double IZ, int nElements)
        {
            double dL = L / nElements;
            var mat = new Material(E, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, IZ);

            var nodes = new List<Node>();
            for (int i = 0; i <= nElements; i++)
            {
                bool isFixed = (i == 0);
                nodes.Add(new Node
                {
                    Name = $"N{i}",
                    Coord = new Point3D(i * dL, 0, 0),
                    Boundary = isFixed
                        ? new Boundary(true, true, true, true, true, true)
                        : new Boundary(false, false, false, false, false, false)
                });
            }

            var beams = new List<Beam>();
            for (int i = 0; i < nElements; i++)
                beams.Add(new Beam($"B{i}", sec, nodes[i], nodes[i + 1], 1.0, 1.0));

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, nodes, beams, [], [], [], []);

            // 先端に荷重
            var tip = nodes[^1];
            tip.SetIncrementalLoad(new NodeLoad(0, P, 0, 0, 0, 0));
            tip.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            return tip.CumulativeDisp.Uy;
        }
    }

    /// <summary>
    /// 単純支持梁のテスト
    /// </summary>
    [TestClass]
    public class SimplySupportedBeamTests
    {
        /// <summary>
        /// 単純支持梁の中央集中荷重: δ = PL³/(48EI)
        /// </summary>
        [TestMethod]
        public void SimpleBeam_CenterLoad_DisplacementCorrect()
        {
            double L = 4.0;
            double P = 200.0;
            double E = 205e6;
            double IZ = 2e-4;

            // nodeA: ピン支持（UxUyUz固定、回転自由）
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, false, false, false)
            };
            // nodeB: 中央（自由）
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(L / 2, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };
            // nodeC: ローラー支持（Uy固定のみ、Ux自由で軸力を逃がす）
            var nodeC = new Node
            {
                Name = "C",
                Coord = new Point3D(L, 0, 0),
                Boundary = new Boundary(false, true, true, false, false, false)
            };

            var mat = new Material(E, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 4e-4, 2e-4, IZ);

            var beam1 = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);
            var beam2 = new Beam("B2", sec, nodeB, nodeC, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel,
                [nodeA, nodeB, nodeC],
                [beam1, beam2],
                [], [], [], []);

            // 中央に集中荷重
            nodeB.SetIncrementalLoad(new NodeLoad(0, -P, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            // 理論解: δ = PL³/(48EI)
            double theory = P * L * L * L / (48.0 * E * IZ);
            double computed = Math.Abs(nodeB.CumulativeDisp.Uy);

            Assert.AreEqual(theory, computed, theory * 0.02,
                $"理論解={theory:E4}, 計算値={computed:E4}");
        }
    }

    /// <summary>
    /// 座標変換のテスト（軸と平行でない梁）
    /// </summary>
    [TestClass]
    public class CoordinateTransformTests
    {
        /// <summary>
        /// Z軸方向の梁（杭と同じ配置）でも正しく解けるか
        /// </summary>
        [TestMethod]
        public void VerticalBeam_AxialLoad_Correct()
        {
            // Z方向に延びる梁（杭と同じ向き）
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(0, 0, -3),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            double E = 205e6;
            double AX = 0.02;
            var mat = new Material(E, 0.3);
            var sec = new Section(mat, AX, 0.02, 0.02, 3e-4, 1.5e-4, 1.5e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            Assert.AreEqual(3.0, beam.Length, 1e-10);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            // Z方向に圧縮力（下向き）
            double N = 1000.0;
            nodeB.SetIncrementalLoad(new NodeLoad(0, 0, -N, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            double L = 3.0;
            double theory = N * L / (E * AX);
            double computed = Math.Abs(nodeB.CumulativeDisp.Uz);

            Assert.AreEqual(theory, computed, theory * 0.02,
                $"Z方向軸変位: 理論={theory:E4}, 計算={computed:E4}");
        }

        /// <summary>
        /// 斜め梁（45度）でも長さが正しく計算されるか
        /// </summary>
        [TestMethod]
        public void DiagonalBeam_LengthCorrect()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(3, 4, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 1e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);

            Assert.AreEqual(5.0, beam.Length, 1e-10);
        }
    }

    /// <summary>
    /// M-φ曲線の非線形剛性低減テスト
    /// </summary>
    [TestClass]
    public class NonlinearMPhiTests
    {
        /// <summary>
        /// 割線剛性は曲率が大きくなるほど低下する（典型的なRC断面の場合）
        /// </summary>
        [TestMethod]
        public void SecantStiffness_DecreasesWithCurvature()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (0.001, 100.0),   // 初期弾性域
                (0.005, 180.0),   // ひび割れ後
                (0.020, 220.0),   // 降伏後
                (0.050, 240.0),   // 終局
            });

            double sec1 = curve.EvaluateSecant(0.001);  // 弾性
            double sec2 = curve.EvaluateSecant(0.005);  // ひび割れ後
            double sec3 = curve.EvaluateSecant(0.020);  // 降伏後
            double sec4 = curve.EvaluateSecant(0.050);  // 終局

            Assert.IsTrue(sec1 > sec2, $"sec1={sec1} > sec2={sec2}");
            Assert.IsTrue(sec2 > sec3, $"sec2={sec2} > sec3={sec3}");
            Assert.IsTrue(sec3 > sec4, $"sec3={sec3} > sec4={sec4}");
        }

        /// <summary>
        /// 接線剛性の連続性テスト（スムーズステップ補間により急変しない）
        /// </summary>
        [TestMethod]
        public void TangentStiffness_SmoothAcrossSegments()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (0.001, 100.0),   // 傾き 100000
                (0.005, 200.0),   // 傾き 25000
                (0.020, 250.0),   // 傾き 3333
            });

            // 折れ点(φ=0.001)の前後で接線が急変しないことを確認
            double before = curve.EvaluateTangent(0.0009);
            double at = curve.EvaluateTangent(0.001);
            double after = curve.EvaluateTangent(0.0011);

            // ブレンドにより滑らかに変化するはず
            // 急激な不連続は発生しない（前後の比が10倍以上にはならない）
            double maxRatio = 10.0;
            if (before > 0 && after > 0)
            {
                Assert.IsTrue(before / after < maxRatio && after / before < maxRatio,
                    $"折れ点付近で急変: before={before:E2}, at={at:E2}, after={after:E2}");
            }
        }
    }

    /// <summary>
    /// 計算例を使った回帰テスト（既知のデータで解析モデルが構築可能か）
    /// </summary>
    [TestClass]
    public class RegressionTests
    {
        private static string GetExamplesDir() => TestHelper.GetExamplesDir();

        /// <summary>
        /// 全計算例でGroundMassesDataのH=nullが安全に処理されるか
        /// </summary>
        [TestMethod]
        public void AllExamples_HGetValueOrDefault_NoException()
        {
            var examplesDir = GetExamplesDir();
            if (!Directory.Exists(examplesDir))
            {
                Assert.Inconclusive("Examples directory not found");
                return;
            }

            foreach (var file in Directory.GetFiles(examplesDir, "Example*.json"))
            {
                var json = System.IO.File.ReadAllText(file);
                var ground = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json);
                if (ground?.GroundMassesData == null) continue;

                foreach (var mass in ground.GroundMassesData)
                {
                    // H.GetValueOrDefault() が例外なく動作すること
                    double h = mass.H.GetValueOrDefault();
                    Assert.IsTrue(double.IsFinite(h) || h == 0.0,
                        $"{Path.GetFileName(file)}: H={mass.H}, GetValueOrDefault={h}");
                }
            }
        }

        /// <summary>
        /// GroundInput の JSON ラウンドトリップ（シリアライズ→デシリアライズで情報が失われないか）
        /// </summary>
        [TestMethod]
        public void GroundInput_JsonRoundTrip_PreservesData()
        {
            var examplesDir = GetExamplesDir();
            var filePath = Path.Combine(examplesDir, "Example1.json");
            if (!System.IO.File.Exists(filePath))
            {
                Assert.Inconclusive("Example1.json not found");
                return;
            }

            var json1 = System.IO.File.ReadAllText(filePath);
            var ground1 = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json1);

            // 再シリアライズ
            var json2 = Newtonsoft.Json.JsonConvert.SerializeObject(ground1);
            var ground2 = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json2);

            Assert.IsNotNull(ground2);
            Assert.AreEqual(ground1!.GroundTopAltitude, ground2!.GroundTopAltitude, 1e-6,
                "ラウンドトリップ後の地表面標高が一致しない");

            // 地層数が一致すること（ObjectCreationHandling.Replace修正後）
            Assert.AreEqual(ground1.GroundLayers.Count, ground2!.GroundLayers.Count,
                "ラウンドトリップ後の地層数が一致しない");

            // 各地層のプロパティが保持されていること
            for (int i = 0; i < ground1.GroundLayers.Count; i++)
            {
                var l1 = ground1.GroundLayers[i];
                var l2 = ground2.GroundLayers[i];
                Assert.AreEqual(l1.Name, l2.Name, $"Layer[{i}].Name が一致しない");
                Assert.AreEqual(l1.IsEngineeringBedrock, l2.IsEngineeringBedrock,
                    $"Layer[{i}].IsEngineeringBedrock が一致しない");
            }

            // 2回目のラウンドトリップで安定すること（冪等性の確認）
            var json3 = Newtonsoft.Json.JsonConvert.SerializeObject(ground2);
            var ground3 = Newtonsoft.Json.JsonConvert.DeserializeObject<PileDesign.Models.InputData.GroundInput>(json3);
            Assert.AreEqual(ground2.GroundLayers.Count, ground3!.GroundLayers.Count,
                "2回目のラウンドトリップで地層数が変化した（冪等でない）");
        }
    }

    /// <summary>
    /// 数値安定性テスト
    /// </summary>
    [TestClass]
    public class NumericalStabilityTests
    {
        /// <summary>
        /// 非常に長い梁でも解が発散しないか
        /// </summary>
        [TestMethod]
        public void VeryLongBeam_DoesNotDiverge()
        {
            double L = 100.0; // 100m
            var nodeA = new Node
            {
                Name = "A",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B",
                Coord = new Point3D(L, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            nodeB.SetIncrementalLoad(new NodeLoad(0, 1, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();

            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];

            // 例外なく解ける
            Solver.SolveDisp(model);

            // 結果が有限値
            Assert.IsTrue(double.IsFinite(nodeB.CumulativeDisp.Uy),
                $"変位が非有限: {nodeB.CumulativeDisp.Uy}");
        }

        /// <summary>
        /// MomentCurvatureCurveが非常に小さいφでもNaNを返さないか
        /// </summary>
        [TestMethod]
        public void MPhiCurve_VerySmallPhi_NoNaN()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (0.001, 100.0),
                (0.01, 200.0),
            });

            double[] testPhis = [1e-15, 1e-12, 1e-9, 1e-6, 1e-3, 0.1];
            foreach (var phi in testPhis)
            {
                double m = curve.EvaluateMoment(phi);
                double t = curve.EvaluateTangent(phi);
                double s = curve.EvaluateSecant(phi);

                Assert.IsTrue(double.IsFinite(m), $"EvaluateMoment({phi:E1}) = {m} is not finite");
                Assert.IsTrue(double.IsFinite(t), $"EvaluateTangent({phi:E1}) = {t} is not finite");
                Assert.IsTrue(double.IsFinite(s), $"EvaluateSecant({phi:E1}) = {s} is not finite");
            }
        }
    }

    /// <summary>
    /// ユーティリティ
    /// </summary>
    internal static class TestHelper
    {
        internal static string GetExamplesDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(projectRoot, "Graphics_r1", "Examples");
        }
    }
}
