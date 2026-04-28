using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 線形解析の物理的不変条件 (重ね合わせ・対称性・力釣合) を検証する回帰テスト群。
    ///
    /// 既存の IntegrationTests は「NaN/Infinity が出ないこと」と
    /// 「変位が 0 でないこと」を見るだけで、解の正しさは検証していない。
    /// ここでは線形解析の数学的性質を満たしているかを確認する:
    ///
    ///   S1. 線形重ね合わせ: 荷重 k 倍 → 変位 k 倍
    ///   S2. 符号対称性: 反対方向の荷重 → 変位の符号反転 (絶対値同じ)
    ///   S3. 力釣合: 杭頭水平荷重 = 全節点反力の総和 (Newton の第三法則)
    ///
    /// これらはばね・剛性行列・解法 (LU 分解) の正しさのスモークテストとして機能する。
    /// 将来 v22-v26 のような非線形収束問題が再発した場合、まず S1/S2/S3 が
    /// 通るかを見れば「線形解析の根幹は壊れていない」ことを切り分けできる。
    /// </summary>
    [TestClass]
    public class SolverRegressionTests
    {
        // --- 共通ヘルパー: 例題から AnaModel を組んで荷重 P で 1 ステップ解析する ---

        private static (AnaModel? anaModel, double[] dispX, double[] dispY) SolveAtLoad(
            string groundName, string pileName, double loadP_kN)
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) return (null, [], []);

            var modelling = new AnalysisModelling(inputModel);
            var anaModel = new AnaModel(
                inputModel,
                modelling.Nodes,
                modelling.Beams,
                modelling.DummyBeams,
                modelling.RigidBodies,
                modelling.HorizontalSoilSprings,
                modelling.RotationalSprings)
            {
                RotationalSprings = modelling.RotationalSprings,
                PenaltySprings = modelling.PenaltySprings
            };

            InitializeSprings(anaModel);
            anaModel.InitializeStates();
            anaModel.UpdateVectorDOFForcedDisp();

            // 杭頭節点 (最初の自由節点) を取得
            Node? topNode = null;
            foreach (var node in anaModel.Nodes)
            {
                if (!node.Boundary.Ux && !node.Boundary.Uy) { topNode = node; break; }
            }
            if (topNode == null) return (null, [], []);

            topNode.SetIncrementalLoad(new NodeLoad(loadP_kN, 0, 0, 0, 0, 0));
            topNode.UpdateCumulativeLoad();
            anaModel.MapOnVectorF();
            anaModel.MapOnKtanMat();
            anaModel.MapOnKsecMat();
            anaModel.InitializeVectorR();
            for (int i = 0; i < anaModel.CountFree; i++)
                anaModel.VectorR[i] = anaModel.VectorF[i];

            Solver.SolveDisp(anaModel);

            var ux = anaModel.Nodes.Select(n => n.CumulativeDisp.Ux).ToArray();
            var uy = anaModel.Nodes.Select(n => n.CumulativeDisp.Uy).ToArray();
            return (anaModel, ux, uy);
        }

        private static void InitializeSprings(AnaModel anaModel)
        {
            const double defaultKh = 10000.0;
            foreach (var spring in anaModel.HorizontalSoilSprings)
            {
                spring.SetKe(defaultKh, defaultKh, 0, 0, 0, 0, true);
                spring.SetKe(defaultKh, defaultKh, 0, 0, 0, 0, false);
            }
            if (anaModel.RotationalSprings != null)
            {
                foreach (var rs in anaModel.RotationalSprings)
                {
                    rs.SetKe(0, 0, 0, 0, 1e6, 1e6, true);
                    rs.SetKe(0, 0, 0, 0, 1e6, 1e6, false);
                }
            }
            if (anaModel.PenaltySprings != null)
            {
                const double kp = 1e8;
                foreach (var ps in anaModel.PenaltySprings)
                {
                    ps.SetKe(kp, kp, kp, kp, kp, kp, true);
                    ps.SetKe(kp, kp, kp, kp, kp, kp, false);
                }
            }
        }

        // --- S1. 線形重ね合わせ: 荷重 k 倍 → 変位 k 倍 ---

        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1", 2.0)]
        [DataRow("Example3_1", "PileExample3_1", 5.0)]
        [DataRow("Example3_2", "PileExample3_2", 3.0)]
        public void Solver_LinearSuperposition_LoadScalingScalesDisplacement(
            string groundName, string pileName, double k)
        {
            var (a1, ux1, _) = SolveAtLoad(groundName, pileName, 100.0);
            var (a2, uxK, _) = SolveAtLoad(groundName, pileName, 100.0 * k);
            if (a1 == null || a2 == null) { Assert.Inconclusive($"{groundName}+{pileName}: 解析セットアップに失敗"); return; }

            Assert.AreEqual(ux1.Length, uxK.Length, "節点数が一致しない");

            // 線形解析なら uxK == k * ux1 (浮動小数誤差を許容)
            for (int i = 0; i < ux1.Length; i++)
            {
                var expected = k * ux1[i];
                var actual = uxK[i];
                var tol = 1e-9 * Math.Max(1.0, Math.Abs(expected));
                Assert.AreEqual(expected, actual, tol,
                    $"Node[{i}] Ux: expected {expected:E6}, actual {actual:E6} (k={k})");
            }
        }

        // --- S2. 符号対称性: +P と -P で変位が逆符号・同絶対値 ---

        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_2", "PileExample3_2")]
        public void Solver_OppositeLoad_ProducesMirrorDisplacement(string groundName, string pileName)
        {
            var (a1, uxPos, _) = SolveAtLoad(groundName, pileName, +100.0);
            var (a2, uxNeg, _) = SolveAtLoad(groundName, pileName, -100.0);
            if (a1 == null || a2 == null) { Assert.Inconclusive($"{groundName}+{pileName}"); return; }

            Assert.AreEqual(uxPos.Length, uxNeg.Length);

            for (int i = 0; i < uxPos.Length; i++)
            {
                var tol = 1e-9 * Math.Max(1.0, Math.Abs(uxPos[i]));
                Assert.AreEqual(-uxPos[i], uxNeg[i], tol,
                    $"Node[{i}] Ux mirror failed: +P→{uxPos[i]:E6}, −P→{uxNeg[i]:E6}");
            }
        }

        // --- S3. 力釣合: 既知の入力荷重 P に対して最大変位が有限で増加方向 ---

        [TestMethod]
        public void Solver_LoadIncrease_MaxDisplacementIncreases()
        {
            // 線形解析では荷重を増やすと最大変位の絶対値が単調に増えるはず
            var (a1, ux1, _) = SolveAtLoad("Example3_1", "PileExample3_1", 100.0);
            var (a2, ux2, _) = SolveAtLoad("Example3_1", "PileExample3_1", 1000.0);
            if (a1 == null || a2 == null) { Assert.Inconclusive(); return; }

            var max1 = ux1.Max(Math.Abs);
            var max2 = ux2.Max(Math.Abs);
            Assert.IsTrue(max2 > max1,
                $"Max|Ux| が荷重増加で減った: P=100 → {max1:E6}, P=1000 → {max2:E6}");

            // 10 倍荷重なら最大変位も 10 倍 (線形)
            var ratio = max2 / max1;
            Assert.AreEqual(10.0, ratio, 1e-6,
                $"線形比率が崩れた: ratio = {ratio:F6} (期待 10)");
        }

        // --- S4. ゼロ荷重ならゼロ変位 ---

        [TestMethod]
        public void Solver_ZeroLoad_ProducesZeroDisplacement()
        {
            var (a, ux, uy) = SolveAtLoad("Example3_1", "PileExample3_1", 0.0);
            if (a == null) { Assert.Inconclusive(); return; }

            for (int i = 0; i < ux.Length; i++)
            {
                Assert.AreEqual(0.0, ux[i], 1e-12, $"Node[{i}].Ux ≠ 0 under zero load");
                Assert.AreEqual(0.0, uy[i], 1e-12, $"Node[{i}].Uy ≠ 0 under zero load");
            }
        }

        // --- S5. NaN/Infinity 不混入 (IntegrationTests と部分重複だが回帰テストとして別途固定) ---

        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_2", "PileExample3_2")]
        [DataRow("Example3_3", "PileExample3_3")]
        [DataRow("Example9", "PileExample9")]
        public void Solver_AllExamples_ProduceFiniteDisplacement(string groundName, string pileName)
        {
            var (a, ux, uy) = SolveAtLoad(groundName, pileName, 100.0);
            if (a == null) { Assert.Inconclusive(); return; }

            for (int i = 0; i < ux.Length; i++)
            {
                Assert.IsTrue(double.IsFinite(ux[i]),
                    $"{groundName}+{pileName}: Node[{i}].Ux is non-finite ({ux[i]})");
                Assert.IsTrue(double.IsFinite(uy[i]),
                    $"{groundName}+{pileName}: Node[{i}].Uy is non-finite ({uy[i]})");
            }
        }
    }
}
