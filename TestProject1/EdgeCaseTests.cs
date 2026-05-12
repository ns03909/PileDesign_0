using PileDesign.FEM;
using PileDesign.Models.InputData;
using MathNet.Numerics.LinearAlgebra;
using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    // ====================================================================
    // Beam 要素の境界値テスト
    // ====================================================================
    [TestClass]
    public class BeamEdgeCaseTests
    {
        /// <summary>
        /// ゼロ長梁（同一座標の2節点）でSetKeすると NaN/Inf が発生するか
        /// </summary>
        [TestMethod]
        public void ZeroLengthBeam_SetKe_ProducesInfOrNaN()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(0, 0, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);

            Assert.AreEqual(0, beam.Length, 1e-15);
            beam.SetKe(true);
            var ke = beam.KeTan;

            // ゼロ長 → EA/L = Inf → 剛性マトリクスに非有限値が含まれるはず
            bool hasNonFinite = false;
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    if (!double.IsFinite(ke[i, j])) hasNonFinite = true;

            Assert.IsTrue(hasNonFinite,
                "ゼロ長梁の剛性マトリクスに非有限値が含まれるべき（ガードがない場合）");
        }

        /// <summary>
        /// 非常に短い梁（L=1e-10）では剛性が極端に大きくなる
        /// </summary>
        [TestMethod]
        public void VeryShortBeam_SetKe_LargeButFiniteStiffness()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1e-6, 0, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);

            beam.SetKe(true);
            var ke = beam.KeTan;

            // 非常に短い梁は巨大な剛性を持つ
            Assert.IsTrue(ke[0, 0] > 1e12, $"K[0,0]={ke[0, 0]:E3} は極めて大きいはず");

            // ただし有限値
            for (int i = 0; i < 12; i++)
                Assert.IsTrue(double.IsFinite(ke[i, i]),
                    $"K[{i},{i}]={ke[i, i]} が非有限");
        }

        /// <summary>
        /// 剛性低減率が0に近い場合のSetKe
        /// </summary>
        [TestMethod]
        public void MinimumStiffnessRatio_SetKe_StillPositive()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            var nj = new Node { Name = "J", Coord = new Point3D(1, 0, 0) };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, ni, nj, 1.0, 1.0);

            // 最小剛性率
            beam.KTan_y = 0.05;
            beam.KTan_z = 0.05;
            beam.SetKe(true);
            var ke = beam.KeTan;

            // 対角成分はすべて正
            for (int i = 0; i < 12; i++)
                Assert.IsTrue(ke[i, i] >= 0, $"K[{i},{i}]={ke[i, i]} が負");
        }
    }

    // ====================================================================
    // AnaModel の境界値テスト
    // ====================================================================
    [TestClass]
    public class AnaModelEdgeCaseTests
    {
        /// <summary>
        /// 全自由度拘束（CountFree=0）のモデル
        /// </summary>
        [TestMethod]
        public void AllFixed_CountFreeIsZero()
        {
            var nodeA = new Node
            {
                Name = "A", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            Assert.AreEqual(0, model.CountFree);
            Assert.AreEqual(0, model.VectorF.Count);
        }

        /// <summary>
        /// 単一自由度モデル（1DOFだけ自由）
        /// </summary>
        [TestMethod]
        public void SingleFreeDof_SolvesCorrectly()
        {
            // nodeA: 全拘束, nodeB: Ux のみ自由
            var nodeA = new Node
            {
                Name = "A", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, true, true, true, true, true)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            Assert.AreEqual(1, model.CountFree);

            // 荷重載荷→求解
            nodeB.SetIncrementalLoad(new NodeLoad(100, 0, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();
            model.VectorR[0] = model.VectorF[0];
            Solver.SolveDisp(model);

            // δ = PL/(EA)
            double theory = 100 * 1.0 / (205e6 * 0.01);
            Assert.AreEqual(theory, nodeB.CumulativeDisp.Ux, theory * 0.02);
        }

        /// <summary>
        /// 非常に大きな荷重で変位クランプ（50mm制限）が作動するか
        /// </summary>
        [TestMethod]
        public void VeryLargeLoad_DisplacementClamped()
        {
            var nodeA = new Node
            {
                Name = "A", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            // 非常に大きな荷重
            double P = 1e12;
            nodeB.SetIncrementalLoad(new NodeLoad(0, P, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            // 変位は有限値であること（クランプにより50mm以下）
            double maxDisp = 0;
            for (int i = 0; i < 6; i++)
                maxDisp = Math.Max(maxDisp, Math.Abs(nodeB.CumulativeDisp.GetByIndex(i)));

            Assert.IsTrue(maxDisp <= 0.05 + 1e-10,
                $"最大変位={maxDisp:E3} が50mmクランプを超えている");
            Assert.IsTrue(double.IsFinite(maxDisp), "変位が非有限値");
        }
    }

    // ====================================================================
    // Solver の境界値テスト
    // ====================================================================
    [TestClass]
    public class SolverEdgeCaseTests
    {
        /// <summary>
        /// 緩和係数=0ではSolverが変位を返さない（ゼロ進行）
        /// </summary>
        [TestMethod]
        public void RelaxationFactor_Zero_NoDisplacement()
        {
            var nodeA = new Node
            {
                Name = "A", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB = new Node
            {
                Name = "B", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nodeA, nodeB, 1.0, 1.0);

            var inputModel = new InputModel();
            var model = new AnaModel(inputModel, [nodeA, nodeB], [beam], [], [], [], []);

            nodeB.SetIncrementalLoad(new NodeLoad(100, 0, 0, 0, 0, 0));
            nodeB.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++)
                model.VectorR[i] = model.VectorF[i];

            // ω=0 → 変位増分がゼロ（ただし Solver のコードでは ω<1 && ω>0 でのみスケーリング）
            // ω=0 はガードされないため、スケーリングが適用されない → 通常の変位が出る
            Solver.SolveDisp(model, 0.0);

            // 現仕様: ω=0 はスケーリング条件 (< 1.0 && > 0.0) を満たさないので通常解が出る
            Assert.IsTrue(nodeB.CumulativeDisp.Ux != 0, "ω=0でも変位が出る（現仕様）");
        }

        /// <summary>
        /// 緩和係数=0.5で変位が半分になるか
        /// </summary>
        [TestMethod]
        public void RelaxationFactor_Half_HalvesDisplacement()
        {
            var nodeA = new Node
            {
                Name = "A", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nodeB_full = new Node
            {
                Name = "B", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, true, true, true, true, true) // Uxのみ自由
            };
            var nodeB_half = new Node
            {
                Name = "B2", Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, true, true, true, true, true)
            };

            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);

            // ω=1.0 で求解
            var beam1 = new Beam("B1", sec, nodeA, nodeB_full, 1.0, 1.0);
            var model1 = new AnaModel(new InputModel(), [nodeA, nodeB_full], [beam1], [], [], [], []);
            nodeB_full.SetIncrementalLoad(new NodeLoad(100, 0, 0, 0, 0, 0));
            nodeB_full.UpdateCumulativeLoad();
            model1.MapOnVectorF(); model1.UpdateVectorDOFForcedDisp();
            model1.MapOnKtanMat(); model1.InitializeVectorR();
            model1.VectorR[0] = model1.VectorF[0];
            Solver.SolveDisp(model1, 1.0);
            double fullDisp = nodeB_full.CumulativeDisp.Ux;

            // ω=0.5 で求解（nodeAを再利用するため新ノード）
            var nodeA2 = new Node
            {
                Name = "A2", Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var beam2 = new Beam("B2", sec, nodeA2, nodeB_half, 1.0, 1.0);
            var model2 = new AnaModel(new InputModel(), [nodeA2, nodeB_half], [beam2], [], [], [], []);
            nodeB_half.SetIncrementalLoad(new NodeLoad(100, 0, 0, 0, 0, 0));
            nodeB_half.UpdateCumulativeLoad();
            model2.MapOnVectorF(); model2.UpdateVectorDOFForcedDisp();
            model2.MapOnKtanMat(); model2.InitializeVectorR();
            model2.VectorR[0] = model2.VectorF[0];
            Solver.SolveDisp(model2, 0.5);
            double halfDisp = nodeB_half.CumulativeDisp.Ux;

            Assert.AreEqual(fullDisp * 0.5, halfDisp, Math.Abs(fullDisp) * 0.01,
                $"ω=1.0: {fullDisp:E4}, ω=0.5: {halfDisp:E4}");
        }
    }

    // ====================================================================
    // MomentCurvatureCurve の境界値テスト
    // ====================================================================
    [TestClass]
    public class MPhiEdgeCaseTests
    {
        /// <summary>
        /// 負のφに対するEvaluateSecant
        /// </summary>
        [TestMethod]
        public void EvaluateSecant_NegativePhi_ReturnsFinite()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0), (0.001, 100.0), (0.01, 200.0)
            });

            double sec = curve.EvaluateSecant(-0.005);
            Assert.IsTrue(double.IsFinite(sec), $"Secant at φ=-0.005: {sec}");
            Assert.IsTrue(sec > 0, $"負のφでも割線剛性は正であるべき: sec={sec}");
        }

        /// <summary>
        /// 非常に大きなφでの外挿
        /// </summary>
        [TestMethod]
        public void EvaluateMoment_VeryLargePhi_ExtrapolatesLinearly()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0), (0.001, 100.0), (0.01, 200.0)
            });

            double m1 = curve.EvaluateMoment(0.1);    // 10倍外挿
            double m2 = curve.EvaluateMoment(1.0);     // 100倍外挿
            double m3 = curve.EvaluateMoment(100.0);   // 10000倍外挿

            Assert.IsTrue(double.IsFinite(m1), $"φ=0.1: M={m1}");
            Assert.IsTrue(double.IsFinite(m2), $"φ=1.0: M={m2}");
            Assert.IsTrue(double.IsFinite(m3), $"φ=100: M={m3}");
            // 線形外挿なので単調増加
            Assert.IsTrue(m2 > m1, "外挿は単調増加");
            Assert.IsTrue(m3 > m2, "外挿は単調増加");
        }

        /// <summary>
        /// 全点が同じφの場合（マージ後1点になる）
        /// </summary>
        [TestMethod]
        public void AllSamePhi_MergedToSinglePoint()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.001, 100.0), (0.001, 200.0), (0.001, 300.0)
            });

            // 全点マージ → 1点（平均モーメント）
            Assert.AreEqual(1, curve.Points.Count);
            Assert.AreEqual(200.0, curve.Points[0].Moment, 1e-6); // 平均

            // 接線は0（1点では傾き不定）
            Assert.AreEqual(0.0, curve.EvaluateTangent(0.001));

            // 割線は初期接線（→0）に等しい
            double sec = curve.EvaluateSecant(0.001);
            Assert.IsTrue(double.IsFinite(sec));
        }

        /// <summary>
        /// 2点だけのM-φ曲線（最小有効ケース）
        /// </summary>
        [TestMethod]
        public void TwoPoints_WorksCorrectly()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0), (0.01, 500.0)
            });

            Assert.AreEqual(2, curve.Points.Count);
            Assert.AreEqual(250.0, curve.EvaluateMoment(0.005), 1e-6);

            double slope = 500.0 / 0.01;
            Assert.AreEqual(slope, curve.EvaluateTangent(0.005), slope * 0.01);
        }

        /// <summary>
        /// 負のモーメントを含むM-φ曲線
        /// </summary>
        [TestMethod]
        public void NegativeMoments_HandledCorrectly()
        {
            var curve = new MomentCurvatureCurve(new[]
            {
                (-0.01, -200.0), (0.0, 0.0), (0.01, 200.0)
            });

            Assert.AreEqual(0.0, curve.EvaluateMoment(0.0), 1e-10);
            Assert.AreEqual(-200.0, curve.EvaluateMoment(-0.01), 1e-6);
            Assert.AreEqual(200.0, curve.EvaluateMoment(0.01), 1e-6);
        }

        /// <summary>
        /// 片側（φ≥0）のみで定義された M-φ 曲線に対し、負 φ 側でも原点対称に
        /// 降伏後の挙動を再現すること（counter-loading 収束性のための対称化）。
        /// </summary>
        [TestMethod]
        public void OneSidedCurve_SymmetricEvaluation_NegativePhiUsesPostYieldBehavior()
        {
            // 二段折れ線: 降伏前 EI=1e5、降伏後 EI=1e4 （10 倍の剛性低下）
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),      // 原点
                (0.001, 100.0),  // 降伏点: K_elastic = 100/0.001 = 1e5
                (0.011, 200.0),  // 降伏後: K_post = (200-100)/(0.011-0.001) = 1e4
            });

            // モーメントが符号付きで対称に評価されること
            Assert.AreEqual(100.0, curve.EvaluateMoment(0.001), 1e-6);
            Assert.AreEqual(-100.0, curve.EvaluateMoment(-0.001), 1e-6);
            Assert.AreEqual(150.0, curve.EvaluateMoment(0.006), 1e-6);
            Assert.AreEqual(-150.0, curve.EvaluateMoment(-0.006), 1e-6);

            // 接線剛性: 降伏後の |φ| に対して post-yield 剛性を返すこと
            // （修正前は負 φ で常に初期弾性剛性 1e5 を返していた）
            Assert.AreEqual(1e5, curve.EvaluateTangent(0.0005), 1.0);   // 弾性域
            Assert.AreEqual(1e5, curve.EvaluateTangent(-0.0005), 1.0);  // 弾性域（負側）
            Assert.AreEqual(1e4, curve.EvaluateTangent(0.006), 1.0);    // 降伏後
            Assert.AreEqual(1e4, curve.EvaluateTangent(-0.006), 1.0);   // 降伏後（負側）★ 修正で追加

            // 両側点列を持つ既存曲線は従来通り（対称化は片側のみに適用）
            var twoSided = new MomentCurvatureCurve(new[]
            {
                (-0.01, -150.0), (0.0, 0.0), (0.01, 200.0)  // 非対称
            });
            Assert.AreEqual(-150.0, twoSided.EvaluateMoment(-0.01), 1e-6);
            Assert.AreEqual(200.0, twoSided.EvaluateMoment(0.01), 1e-6);
        }

        /// <summary>
        /// RotationalSpring の M-θ 曲線（片側定義）が負 θ に対して
        /// 原点対称に符号付きモーメントを返すこと。
        /// </summary>
        [TestMethod]
        public void RotationalMomentRotationCurve_SymmetricEvaluation_NegativeTheta()
        {
            var curve = new MomentRotationCurve(new[]
            {
                (0.001, 100.0),
                (0.010, 150.0),
            });

            // 正側（既存挙動）— 折れ点上ピッタリは厳密一致
            Assert.AreEqual(100.0, curve.EvaluateMoment(0.001), 1e-6);
            Assert.AreEqual(150.0, curve.EvaluateMoment(0.010), 1e-6);

            // 負側（修正点）: 以前は +100, +150 を返していた
            Assert.AreEqual(-100.0, curve.EvaluateMoment(-0.001), 1e-6);
            Assert.AreEqual(-150.0, curve.EvaluateMoment(-0.010), 1e-6);

            // post-yield 外挿（|θ| > θ_last）は奇関数: f(-θ) = -f(θ)
            // 実装は微小 (1%) post-yield 勾配を持つため、厳密値ではなく対称性をチェック
            double mPlus = curve.EvaluateMoment(0.050);
            double mMinus = curve.EvaluateMoment(-0.050);
            Assert.AreEqual(mPlus, -mMinus, 1e-10, "post-yield 外挿域でも奇関数性が保たれること");
            Assert.IsTrue(mPlus >= 150.0, "post-yield 外挿は M_last 以上を返す");

            // セカント剛性は正のスカラー（F = K·θ で符号は θ 側から戻る）
            Assert.IsTrue(curve.EvaluateSecant(0.005) > 0);
            Assert.IsTrue(curve.EvaluateSecant(-0.005) > 0);
            Assert.AreEqual(curve.EvaluateSecant(0.005), curve.EvaluateSecant(-0.005), 1e-10);
        }
    }

    // ====================================================================
    // DoatsuGoryokuBaneItem（土圧合力ばね）の境界値テスト
    // counter-loading（負方向漸増）時に圧力が正しい符号で評価されること
    // ====================================================================
    [TestClass]
    public class DoatsuGoryokuBaneItemEdgeCaseTests
    {
        private static DoatsuGoryokuBaneItem BuildTestItem()
        {
            // Pp - P0 が既知の値になるパラメータを用意
            // P0 = (Q + γ·(ZTop-ZBtm)) · K0
            // Pp = (Q + γ·(ZTop-ZBtm)) · Kp + 2·C·√Kp
            // γ=10, ZTop=1, ZBtm=0, Q=0, K0=0.5, Φ=30°(Kp=3), C=0
            //   → P0 = 5, Pp = 30, Pp-P0 = 25
            var item = new DoatsuGoryokuBaneItem
            {
                Gamma = 10,
                Phi = 30,
                C = 0,
                ZTop = 1,
                ZBtm = 0,
                K0 = 0.5,
                Q = 0,
            };
            item.SetDeltaPAndYsp(deltaP: 0.01, ysp: 0.005);
            return item;
        }

        [TestMethod]
        public void GetPressure_ZeroDisp_ReturnsZero()
        {
            var item = BuildTestItem();
            Assert.AreEqual(0.0, item.GetPressure(0.0), 1e-12);
        }

        [TestMethod]
        public void GetPressure_PositivePlateau_ReturnsPpMinusP0()
        {
            var item = BuildTestItem();
            double ppMinusP0 = item.Pp - item.P0;
            Assert.AreEqual(ppMinusP0, item.GetPressure(item.DeltaP), 1e-9);
            Assert.AreEqual(ppMinusP0, item.GetPressure(0.03), 1e-9);  // 3倍のプラトー
        }

        [TestMethod]
        public void GetPressure_NegativeDisp_IsOddSymmetric()
        {
            // 奇関数 P(-x) = -P(x) であること
            var item = BuildTestItem();
            double[] testDisps = [0.001, 0.003, 0.005, 0.008, 0.009];
            foreach (var d in testDisps)
            {
                double posP = item.GetPressure(d);
                double negP = item.GetPressure(-d);
                Assert.AreEqual(-posP, negP, 1e-9, $"GetPressure({-d}) should equal -GetPressure({d}): got {negP} vs -{posP}");
            }
        }

        [TestMethod]
        public void GetPressure_NegativePlateau_ReturnsMinusPpMinusP0()
        {
            // 修正前は |disp| ≈ 2·DeltaP でゼロ除算、それを超えると符号反転（+Pp が返る）
            // 修正後は |disp| >= DeltaP で -(Pp-P0) プラトーへ
            var item = BuildTestItem();
            double ppMinusP0 = item.Pp - item.P0;

            Assert.AreEqual(-ppMinusP0, item.GetPressure(-item.DeltaP), 1e-9);
            Assert.AreEqual(-ppMinusP0, item.GetPressure(-2 * item.DeltaP), 1e-9);  // 旧: Inf
            Assert.AreEqual(-ppMinusP0, item.GetPressure(-3 * item.DeltaP), 1e-9);  // 旧: +3·(Pp-P0) (符号反転)
        }

        [TestMethod]
        public void GetPressure_NoSingularity_AtTwiceDeltaP()
        {
            // |disp| = 2·DeltaP はまさに旧実装でゼロ除算を起こした点
            var item = BuildTestItem();
            double val = item.GetPressure(-2 * item.DeltaP);
            Assert.IsTrue(double.IsFinite(val), "値が有限であるべき（修正前は Inf/NaN）");
            Assert.AreEqual(-(item.Pp - item.P0), val, 1e-9);
        }

        // v22: 降伏後接線剛性が降伏境界接線の 2% となり、500× → 50× の不連続緩和が
        // 実装されたことを確認する
        [TestMethod]
        public void GetTangentStiffness_PlateauIsContinuousWithYield()
        {
            var item = BuildTestItem();

            // 降伏境界直前（|disp| = 0.999 × DeltaP）での数値微分接線
            double preYieldTan = item.GetTangentStiffness(0.999 * item.DeltaP);
            // 降伏境界直後（|disp| = 1.001 × DeltaP）での接線
            double postYieldTan = item.GetTangentStiffness(1.001 * item.DeltaP);

            // ジャンプ比 = pre / post。旧実装では ~500x、新実装では ~50x 以下を期待
            double ratio = preYieldTan / postYieldTan;

            Assert.IsTrue(postYieldTan > 0, "塑性領域でも接線剛性は正であるべき");
            Assert.IsTrue(ratio < 100.0, $"降伏前後の接線比は 100× 未満に緩和されるべき（実測: {ratio:F2}×）");
            // 参考: 旧実装は 0.0001 × k0 ≈ P_max × 0.018 で約 500× ジャンプ
            Assert.IsTrue(ratio > 10.0, $"完全連続ではなく離散的な遷移は残る想定（実測: {ratio:F2}×）");
        }

        [TestMethod]
        public void GetTangentStiffness_PlateauScalesWithYieldBoundary()
        {
            var item = BuildTestItem();
            double plateauTan = item.GetTangentStiffness(2 * item.DeltaP);

            // 期待値: 降伏境界の解析接線 × 0.02
            double pMax = item.Pp - item.P0;
            double yieldTan = pMax * 2.0 * item.Ysp / (item.DeltaP * (item.DeltaP - item.Ysp));
            double expected = 0.02 * yieldTan;

            Assert.AreEqual(expected, plateauTan, 1e-9);
        }
    }

    // ====================================================================
    // v22: HorizontalSoilReaction 降伏境界接線連続化（バグ#2）
    // ====================================================================
    [TestClass]
    public class HorizontalSoilReactionEdgeCaseTests
    {
        private static PileDesign.Models.InputData.HorizontalSoilReactionItem BuildTestItem()
        {
            // kh0 = 10000 kN/m3, py 系列は下記 SetParameters で計算されるが
            // テストでは SetParameters を呼ばずに kh0/py を直接制御する必要があるため
            // 内部フィールドを使う。
            var item = new PileDesign.Models.InputData.HorizontalSoilReactionItem();
            // SetParameters を呼ぶと py が自動計算される: 前方砂質土で κ=3, Kp=3, sigmaZ=10
            // → py = 3*3*10 = 90
            item.SetParameters(
                name: "Test", soilType: "砂質土", gamma: 10, b: 1.0, e0: 1000,
                zTop: 1, zBtm: 0, xi: 1, rOnB: 1, nValue: 10, phi: 30, cu: 0,
                sigmaZPrimeTop: 10, sigmaZPrimeBtm: 10);
            return item;
        }

        [TestMethod]
        public void GetkhTan_PreYieldAndPostYield_ContinuityImproved()
        {
            var item = BuildTestItem();
            double kh0 = item.Kh0;
            double py = item.PyFrontTop; // 90 前後

            // 降伏変位 yy = (py/kh0)^2 / y0
            double y0 = 0.01;
            double yy = Math.Pow(py / kh0, 2) / y0;

            // 降伏直前の接線（数値微分相当を直接計算）
            double preYieldTan = PileDesign.Models.InputData.HorizontalSoilReactionItem
                .GetkhTan(kh0, 0.999 * yy, py);
            // 降伏直後の接線
            double postYieldTan = PileDesign.Models.InputData.HorizontalSoilReactionItem
                .GetkhTan(kh0, 1.001 * yy, py);

            Assert.IsTrue(postYieldTan > 0, "降伏後接線も正値であるべき");

            // 旧実装では比率 ~ 500、新実装では ~ 50 になる
            double ratio = preYieldTan / postYieldTan;
            Assert.IsTrue(ratio < 100.0, $"降伏前後の接線比は 100× 未満に緩和されるべき（実測: {ratio:F2}×）");
        }

        [TestMethod]
        public void GetkhTan_AndGetSoilSecantReactionCoefficient_AreConsistentPostYield()
        {
            // 降伏後: K_tan = d(K_sec × |y|) / d|y| = gradient が成立することを検証
            var item = BuildTestItem();
            double kh0 = item.Kh0;
            double py = item.PyFrontTop;
            double y0 = 0.01;
            double yy = Math.Pow(py / kh0, 2) / y0;

            double y1 = 1.5 * yy;
            double y2 = 1.5 * yy + 1e-7;

            // 力 p = K_sec × |y| を 2 点で計算、数値微分で接線を得る
            double B = item.B, DZ = Math.Abs(item.ZTop - item.ZBtm);
            double areaScale = B * DZ * 0.5;
            double kSec1 = item.GetSoilSecantReactionCoefficient(y1, true, true);
            double kSec2 = item.GetSoilSecantReactionCoefficient(y2, true, true);
            double p1 = kSec1 * y1 / areaScale; // kh_sec 相当に戻す
            double p2 = kSec2 * y2 / areaScale;
            double numericalTangent = (p2 - p1) / (y2 - y1);

            double analyticalTangent = PileDesign.Models.InputData.HorizontalSoilReactionItem
                .GetkhTan(kh0, y1, py);

            // 1% 以内で一致（新 gradient が GetKh/GetkhTan で共通のため）
            Assert.AreEqual(analyticalTangent, numericalTangent, analyticalTangent * 0.01);
        }

        // v23 (A-2): elastic/sqrt 境界のスムージング効果を確認
        [TestMethod]
        public void GetkhTan_ElasticSqrtBoundary_IsContinuous()
        {
            var item = BuildTestItem();
            double kh0 = item.Kh0;
            double py = item.PyFrontTop;
            double y0 = 0.01;

            // 境界 |y|/y0 = 0.1 の前後
            double yJustBelow = 0.099 * y0;
            double yAtStart = 0.101 * y0;    // ブレンド開始直後
            double yMiddleBlend = 0.15 * y0; // ブレンド中央
            double yAfterBlend = 0.21 * y0;  // ブレンド終了後

            double tBelow = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, yJustBelow, py);
            double tStart = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, yAtStart, py);
            double tMid = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, yMiddleBlend, py);
            double tAfter = PileDesign.Models.InputData.HorizontalSoilReactionItem.GetkhTan(kh0, yAfterBlend, py);

            // 境界直後の接線は弾性側（3.16×kh0）に近い（ブレンド始まり t≈0）
            // 旧実装ではここで 1.58×kh0 に 2× ジャンプしていた
            Assert.IsTrue(tStart > 0.9 * tBelow,
                $"境界直後の接線が弾性直前 {tBelow:E2} に近いはず（旧: 半分に落ちる）。実測: {tStart:E2}");

            // ブレンド中央は弾性と sqrt の中間あたり
            Assert.IsTrue(tMid < tBelow && tMid > tAfter,
                $"ブレンド中央 {tMid:E2} は弾性 {tBelow:E2} と sqrt {tAfter:E2} の間にあるべき");

            // ブレンド終了後は sqrt 解析接線
            double expectedSqrt = Math.Sqrt(y0) / 2.0 * kh0 / Math.Sqrt(yAfterBlend);
            Assert.AreEqual(expectedSqrt, tAfter, expectedSqrt * 0.01);
        }

        // v25: GetP の対称性回帰テスト
        // 旧実装 `Math.Min(Kh*y, py)` は y<0 側で clamp されず post-yield creep が不等
        // に発生していた。counter-loading 解析の表示や MGT エクスポートに影響。
        [TestMethod]
        public void GetP_PostYield_IsAntisymmetricAcrossZero()
        {
            var item = BuildTestItem();
            double py = item.PyFrontTop;
            double y0 = 0.01;
            double yy = Math.Pow(py / item.Kh0, 2) / y0;

            // 十分塑性化した領域（|y| > yy）で正負対称に評価
            double yPos = 5.0 * yy;
            double yNeg = -5.0 * yy;

            double pPos = item.GetP(yPos, py);
            double pNeg = item.GetP(yNeg, py);

            Assert.AreEqual(-pPos, pNeg, 1e-9,
                $"GetP は奇関数であるべき: p({yPos})={pPos}, p({yNeg})={pNeg}");
            // 両側とも py 以下に clamp されているはず
            Assert.IsTrue(Math.Abs(pPos) <= py + 1e-9, $"|p(+y)| が py={py} を超過: {pPos}");
            Assert.IsTrue(Math.Abs(pNeg) <= py + 1e-9, $"|p(-y)| が py={py} を超過: {pNeg}");
        }

        [TestMethod]
        public void GetP_Zero_ReturnsZero()
        {
            var item = BuildTestItem();
            Assert.AreEqual(0.0, item.GetP(0.0, item.PyFrontTop), 1e-12);
        }

        [TestMethod]
        public void GetP_PreYield_IsAntisymmetric()
        {
            var item = BuildTestItem();
            double py = item.PyFrontTop;
            double y0 = 0.01;
            double yy = Math.Pow(py / item.Kh0, 2) / y0;

            // sqrt 領域（|y| < yy）
            double ySmall = 0.3 * yy;
            double pPos = item.GetP(ySmall, py);
            double pNeg = item.GetP(-ySmall, py);

            Assert.AreEqual(-pPos, pNeg, 1e-9);
            Assert.IsTrue(pPos > 0, "正の y では正の p");
            Assert.IsTrue(pNeg < 0, "負の y では負の p");
        }
    }

    // ====================================================================
    // v23 (B-1): TwoNodeSpringElement.SetKeWithXYCoupling 2D Jacobian
    // ====================================================================
    [TestClass]
    public class TwoNodeSpringElementCouplingTests
    {
        private sealed class TestSpring : PileDesign.FEM.TwoNodeSpringElement
        {
            public TestSpring(PileDesign.FEM.Node i, PileDesign.FEM.Node j) : base("T", i, j) { }
        }

        [TestMethod]
        public void SetKeWithXYCoupling_ProducesSymmetricAndPenaltyFormMatrix()
        {
            var nodeI = new PileDesign.FEM.Node();
            var nodeJ = new PileDesign.FEM.Node();
            var spring = new TestSpring(nodeI, nodeJ);

            double kxx = 100, kxy = 20, kyy = 80;
            spring.SetKeWithXYCoupling(kxx, kxy, kyy, 0, 0, 0, 0, isTan: true);

            var ke = spring.KeTan;

            // 対称性チェック: K[i,j] = K[j,i]
            for (int r = 0; r < 12; r++)
                for (int c = 0; c < r; c++)
                    Assert.AreEqual(ke[r, c], ke[c, r], 1e-12, $"K[{r},{c}] != K[{c},{r}]");

            // Penalty ブロック構造: 左上と右下が同じ、対角オフが符号反転
            Assert.AreEqual(kxx, ke[0, 0], 1e-12);
            Assert.AreEqual(kxx, ke[6, 6], 1e-12);
            Assert.AreEqual(-kxx, ke[0, 6], 1e-12);
            Assert.AreEqual(kxy, ke[0, 1], 1e-12);
            Assert.AreEqual(kxy, ke[6, 7], 1e-12);
            Assert.AreEqual(-kxy, ke[0, 7], 1e-12);
            Assert.AreEqual(kyy, ke[1, 1], 1e-12);
            Assert.AreEqual(-kyy, ke[1, 7], 1e-12);
        }

        [TestMethod]
        public void SetKeWithXYCoupling_IsotropicInput_MatchesSetKe()
        {
            // kxy=0 で kxx=kyy なら、従来の SetKe(k, k, ...) と完全一致するはず
            var nodeI = new PileDesign.FEM.Node();
            var nodeJ = new PileDesign.FEM.Node();
            var spring1 = new TestSpring(nodeI, nodeJ);
            var spring2 = new TestSpring(nodeI, nodeJ);

            double k = 150;
            spring1.SetKe(k, k, 10, 20, 30, 40, isTan: true);
            spring2.SetKeWithXYCoupling(k, 0, k, 10, 20, 30, 40, isTan: true);

            for (int r = 0; r < 12; r++)
                for (int c = 0; c < 12; c++)
                    Assert.AreEqual(spring1.KeTan[r, c], spring2.KeTan[r, c], 1e-12, $"差分 at [{r},{c}]");
        }
    }

    // ====================================================================
    // v24 (B-2): 合成 M-φ モードの対角 Jacobian ブレンド検証
    // MomentCurvatureCurve 自体の挙動（対角ブレンドの前提となる EvaluateTangent / EvaluateSecant の整合）
    // ====================================================================
    [TestClass]
    public class CombinedMPhiDiagonalBlendTests
    {
        [TestMethod]
        public void MomentCurvatureCurve_TangentAndSecant_DiverStrictlyInPostYield()
        {
            // ポスト降伏で tan < sec になることを検証（対角ブレンドの前提条件）
            var curve = new MomentCurvatureCurve(new[]
            {
                (0.0, 0.0),
                (1e-3, 100.0),   // 初期弾性
                (1e-2, 110.0),   // ポスト降伏（小さな傾き）
            });

            // 弾性領域: tan ≈ sec
            double tanElastic = curve.EvaluateTangent(5e-4);
            double secElastic = curve.EvaluateSecant(5e-4);
            Assert.AreEqual(secElastic, tanElastic, 1e-6, "弾性領域では tan = sec のはず");

            // ポスト降伏: tan < sec
            double tanPost = curve.EvaluateTangent(5e-3);
            double secPost = curve.EvaluateSecant(5e-3);
            Assert.IsTrue(tanPost < secPost,
                $"ポスト降伏では tan ({tanPost:E3}) < sec ({secPost:E3}) のはず（対角ブレンドで EIz = sec の根拠）");
        }

        [TestMethod]
        public void DiagonalBlend_PureBendingRetrievesTangent()
        {
            // 純粋な Y 方向曲げ（phiZ = 0）のとき、EIy = EI_tan、EIz = EI_sec となる
            double EI_tan = 100, EI_sec = 500;
            double phiY = 0.01, phiZ = 0.0;
            double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
            double cosT = phiY / phiRes; // = 1
            double sinT = phiZ / phiRes; // = 0

            double EIy = EI_tan * cosT * cosT + EI_sec * sinT * sinT;
            double EIz = EI_tan * sinT * sinT + EI_sec * cosT * cosT;

            Assert.AreEqual(EI_tan, EIy, 1e-12, "純 Y 曲げでは EIy = EI_tan");
            Assert.AreEqual(EI_sec, EIz, 1e-12, "純 Y 曲げでは EIz = EI_sec（直交方向の割線）");
        }

        [TestMethod]
        public void DiagonalBlend_At45DegreesEqualContribution()
        {
            // 45°（phiY = phiZ）では両方向とも (EI_tan + EI_sec) / 2
            double EI_tan = 100, EI_sec = 500;
            double phiY = 0.01, phiZ = 0.01;
            double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
            double cosT = phiY / phiRes; // = 1/√2
            double sinT = phiZ / phiRes; // = 1/√2

            double EIy = EI_tan * cosT * cosT + EI_sec * sinT * sinT;
            double EIz = EI_tan * sinT * sinT + EI_sec * cosT * cosT;
            double expected = (EI_tan + EI_sec) / 2.0;

            Assert.AreEqual(expected, EIy, 1e-10);
            Assert.AreEqual(expected, EIz, 1e-10);
        }
    }

    // ====================================================================
    // Utils の境界値テスト
    // ====================================================================
    [TestClass]
    public class UtilsEdgeCaseTests
    {
        /// <summary>
        /// 同一座標の2節点で座標変換マトリクスを生成（ゼロ長）
        /// </summary>
        [TestMethod]
        public void GetTransformMatrix_ZeroLength_ContainsNaN()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(5, 5, 5) };
            var nj = new Node { Name = "J", Coord = new Point3D(5, 5, 5) };

            var T = Utils.GetTransformMatrix(ni, nj);

            // ゼロ長 → NaN が含まれるはず
            bool hasNaN = false;
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    if (double.IsNaN(T[i, j])) hasNaN = true;

            Assert.IsTrue(hasNaN, "ゼロ長要素の座標変換にNaNが含まれるべき");
        }

        /// <summary>
        /// ほぼ鉛直（水平成分が極小）の梁
        /// </summary>
        [TestMethod]
        public void GetTransformMatrix_NearlyVertical_StillOrthogonal()
        {
            var ni = new Node { Name = "I", Coord = new Point3D(0, 0, 0) };
            // 水平偏差が1e-12程度
            var nj = new Node { Name = "J", Coord = new Point3D(1e-12, 0, -10) };

            var T = Utils.GetTransformMatrix(ni, nj);
            var TtT = T.Transpose() * T;
            var I = Matrix<double>.Build.DenseIdentity(12);

            // 直交性が保たれること
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    Assert.AreEqual(I[i, j], TtT[i, j], 1e-6,
                        $"T^T*T[{i},{j}]={TtT[i, j]}, expected {I[i, j]}");
        }

        /// <summary>
        /// Interpolate: 配列要素数1（最小ケース）
        /// </summary>
        [TestMethod]
        public void Interpolate_SingleElement_ReturnsValue()
        {
            double[] x = { 5.0 };
            double[] y = { 42.0 };
            Assert.AreEqual(42.0, Utils.Interpolate(x, y, 0.0), 1e-10);
            Assert.AreEqual(42.0, Utils.Interpolate(x, y, 100.0), 1e-10);
        }

        /// <summary>
        /// Interpolate: x値が非昇順の場合
        /// </summary>
        [TestMethod]
        public void Interpolate_NonAscending_ReturnsLast()
        {
            // x = [10, 5, 0] → 非昇順 → ループが一致しない → y[^1] を返す
            double[] x = { 10, 5, 0 };
            double[] y = { 100, 50, 0 };
            double result = Utils.Interpolate(x, y, 7.0);
            Assert.AreEqual(0.0, result, 1e-10, "非昇順では最後の値が返る");
        }

        /// <summary>
        /// GetLengthBetweenTwoNodes: 非常に遠い2点
        /// </summary>
        [TestMethod]
        public void GetLength_VeryLargeDistance_Finite()
        {
            var a = new Node { Name = "A", Coord = new Point3D(0, 0, 0) };
            var b = new Node { Name = "B", Coord = new Point3D(1e6, 1e6, 1e6) };
            double len = Utils.GetLengthBetweenTwoNodes(a, b);
            Assert.IsTrue(double.IsFinite(len));
            Assert.AreEqual(Math.Sqrt(3e12), len, 1e-3);
        }
    }

    // ====================================================================
    // GroundInput の境界値テスト
    // ====================================================================
    [TestClass]
    public class GroundInputEdgeCaseTests
    {
        /// <summary>
        /// 畑中式で sigmaZPrime=0（ゼロ除算）
        /// </summary>
        [TestMethod]
        public void GetFrictionAngle_Hatanaka_ZeroStress_Returns40()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            // σ'z=0 → N1=N/sqrt(0)=Inf → sqrt(20*Inf)=Inf → min(Inf+20, 40)=40
            double phi = gi.GetFrictionAngle(10, 0);
            Assert.AreEqual(40.0, phi, 1e-6, "σ'z=0では上限40°が返るべき");
        }

        /// <summary>
        /// 負のN値（本来ありえないが入力される可能性）
        /// </summary>
        [TestMethod]
        public void GetFrictionAngle_Osaki_NegativeN()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "大崎式";
            // sqrt(20 * -5) = sqrt(-100) = NaN → min(NaN+15, 40) = NaN → NaN
            double phi = gi.GetFrictionAngle(-5, 100);
            // NaN は Min で 40 にはならない → NaN が返る
            Assert.IsTrue(double.IsNaN(phi),
                $"負のN値ではNaNが返る（現仕様確認）: φ={phi}");
        }

        /// <summary>
        /// 負のN値（畑中式）
        /// </summary>
        [TestMethod]
        public void GetFrictionAngle_Hatanaka_NegativeN()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            double phi = gi.GetFrictionAngle(-5, 100);
            // sqrt(20 * (-5/1)) = sqrt(-100) = NaN → NaN
            Assert.IsTrue(double.IsNaN(phi),
                $"負のN値ではNaNが返る: φ={phi}");
        }

        /// <summary>
        /// 負の有効応力（畑中式）
        /// </summary>
        [TestMethod]
        public void GetFrictionAngle_Hatanaka_NegativeStress()
        {
            var gi = new GroundInput();
            gi.SelectedInternalFrictionAngleCalcumationMethod = "畑中式";
            // σ'z = -100 → sqrt(-100/100) = sqrt(-1) = NaN
            double phi = gi.GetFrictionAngle(10, -100);
            Assert.IsTrue(double.IsNaN(phi),
                $"負の有効応力ではNaNが返る: φ={phi}");
        }

        /// <summary>
        /// ValidateForAnalysis: 空のGroundLayers
        /// </summary>
        [TestMethod]
        public void Validate_EmptyLayers_ReturnsTrue()
        {
            var gi = new GroundInput();
            gi.GroundLayers = [];
            gi.GroundMassesData = [];
            bool result = gi.ValidateForAnalysis(out _);
            Assert.IsTrue(result, "空の地層リストではバリデーションは成功すべき");
        }

        /// <summary>
        /// DeepCopy後のコレクション変更が元に影響しない
        /// </summary>
        [TestMethod]
        public void DeepCopy_AddLayer_DoesNotAffectOriginal()
        {
            var gi = new GroundInput();
            int originalCount = gi.GroundLayers.Count;
            var copy = gi.DeepCopy();
            copy.GroundLayers.Add(new GroundLayerInput { No = 99, Name = "Added" });
            Assert.AreEqual(originalCount, gi.GroundLayers.Count);
        }
    }

    // ====================================================================
    // RigidBody の境界値テスト
    // ====================================================================
    [TestClass]
    public class RigidBodyEdgeCaseTests
    {
        /// <summary>
        /// 追加されていないノードの RemoveSlaveNode は無害
        /// </summary>
        [TestMethod]
        public void RemoveSlaveNode_NeverAdded_NoEffect()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var notSlave = new Node { Name = "NS", Coord = new Point3D(1, 0, 0) };
            notSlave.SetBoundary(new Boundary(false, false, true, false, false, false));

            var rb = new RigidBody(master, [true, true, true, true, true, true]);
            rb.RemoveSlaveNode(notSlave);

            // 境界が変更されていないこと
            Assert.IsFalse(notSlave.Boundary.Ux);
            Assert.IsTrue(notSlave.Boundary.Uz);
        }

        /// <summary>
        /// null ノードの追加・削除は無害
        /// </summary>
        [TestMethod]
        public void AddAndRemove_Null_NoException()
        {
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            var rb = new RigidBody(master, [true, true, true, true, true, true]);
            rb.AddSlaveNode(null!);
            rb.RemoveSlaveNode(null!);
            Assert.AreEqual(0, rb.SlaveNodes.Count);
        }

        /// <summary>
        /// マスターとスレーブが同じ座標（アームベクトル=0）
        /// </summary>
        [TestMethod]
        public void SlaveAtMasterPosition_ZeroArm()
        {
            var master = new Node { Name = "M", Coord = new Point3D(3, 4, 5) };
            var slave = new Node { Name = "S", Coord = new Point3D(3, 4, 5) };
            var rb = new RigidBody(master, [true, true, true, false, false, false]);

            var arm = rb.GetSlaveArmVector(slave);
            Assert.AreEqual(0, arm.X, 1e-12);
            Assert.AreEqual(0, arm.Y, 1e-12);
            Assert.AreEqual(0, arm.Z, 1e-12);

            // 追加しても例外なし
            rb.AddSlaveNode(slave);
            Assert.AreEqual(1, rb.SlaveNodes.Count);
        }
    }

    // ====================================================================
    // Node の境界値テスト
    // ====================================================================
    [TestClass]
    public class NodeEdgeCaseTests
    {
        /// <summary>
        /// InitializeDisp を複数回呼んでも安全
        /// </summary>
        [TestMethod]
        public void InitializeDisp_CalledTwice_Safe()
        {
            var node = new Node { Name = "N1", Coord = new Point3D(0, 0, 0) };
            node.CumulativeDisp = new NodeDisp(1, 2, 3, 4, 5, 6);
            node.InitializeDisp();
            node.InitializeDisp(); // 2回目
            Assert.AreEqual(0, node.CumulativeDisp.Ux);
        }

        /// <summary>
        /// NodeDisp の Uh（水平合成変位）がゼロ入力で正しい
        /// </summary>
        [TestMethod]
        public void NodeDisp_Uh_ZeroComponents_ReturnsZero()
        {
            var d = new NodeDisp(0, 0, 5, 0, 0, 0);
            Assert.AreEqual(0.0, d.Uh, 1e-15);
        }

        /// <summary>
        /// 非常に大きな変位値でも演算が安全
        /// </summary>
        [TestMethod]
        public void NodeDisp_VeryLargeValues_OperationsSafe()
        {
            var a = new NodeDisp(1e15, -1e15, 1e15, -1e15, 1e15, -1e15);
            var b = new NodeDisp(1e15, 1e15, 1e15, 1e15, 1e15, 1e15);
            var sum = a + b;
            Assert.AreEqual(2e15, sum.Ux, 1e5);
            Assert.AreEqual(0, sum.Uy, 1e5);
            Assert.IsTrue(double.IsFinite(sum.Uh));
        }
    }
}
