using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// 収束を助けるための近道が、解析結果を信用できないものにしていないこと (2026-10-11)。
    ///
    /// <para>方針は「収束が止まるのは困るが、結果を信用できなくする仮定は使わない」。収束の助けは
    /// 既存の再試行・刻みの半減・頭打ちに任せ、それでも収束しなければ未収束として知らせる。
    /// 黙って受理してはいけない。ここで見るのは次の 4 件。</para>
    /// <list type="number">
    /// <item>M-φ の割線 (内力) に剛性比の下限 0.05 を掛けていた → 曲線より大きな曲げモーメントのまま収束</item>
    /// <item>変位増分の頭打ち・緩和係数・線探索の α が地盤変位 (強制変位) まで縮めていた →
    ///   残差は強制変位の自由度を見ないので、地盤変位を一部しか与えていないステップが収束になり得た</item>
    /// <item>収束基準の緩和 3 つに上限が無かった → 残差 0.3 でも「緩和収束」</item>
    /// <item>単杭沈下で荷重段階が失敗したときの巻き戻しが、変位と荷重しか戻していなかった →
    ///   限界値の印が残り、偽の極限の行を記録 / 使用限界の行を飛ばしていた</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class ConvergenceShortcutSoundnessTests
    {
        // ── 1. M-φ の割線は曲線どおり ─────────────────────────────

        private const double E = 205e6;     // [kN/m²]
        private const double Iy = 1e-4;     // [m⁴]
        private const double EI0 = E * Iy;  // 20,500 kN·m²
        private const double PhiY = 0.001;  // 降伏曲率 [1/m]

        /// <summary>
        /// 初期勾配が EI0 で、降伏後ほぼ水平な M-φ (My = EI0·φy、φ = 0.1 で 1.05·My) を持つ長さ 1 m の要素。
        /// 両端に逆向きの回転 ∓θ を与えると、要素は一様曲率 φ = 2θ/L の純曲げになる。
        /// </summary>
        private static (AnaModel Model, Beam Beam) PureBendingBeam(double phi)
        {
            double theta = phi * 1.0 / 2.0;
            var nodeI = new Node { Name = "I", Coord = new Point3D(0, 0, 0), Boundary = new Boundary(false, false, false, false, false, false) };
            var nodeJ = new Node { Name = "J", Coord = new Point3D(1, 0, 0), Boundary = new Boundary(false, false, false, false, false, false) };
            var section = new Section(new Material(E, 0.3), 0.01, 0.01, 0.01, 2e-4, Iy, Iy);
            var beam = new Beam("B1", section, nodeI, nodeJ, 1.0, 1.0);
            double my = EI0 * PhiY;
            beam.SetResolvedCombinedMPhi([0.0, PhiY, 0.1], [0.0, my, 1.05 * my]);

            var model = new AnaModel(new PileDesign.Models.InputData.InputModel(), [nodeI, nodeJ], [beam], [], [], [], []);
            nodeI.CumulativeDisp = new NodeDisp(0, 0, 0, 0, -theta, 0);
            nodeJ.CumulativeDisp = new NodeDisp(0, 0, 0, 0, theta, 0);
            return (model, beam);
        }

        private static double MaxEndMoment(Beam beam)
        {
            var f = beam.CumulativeForce.GetVector();
            return new[] { f[4], f[5], f[10], f[11] }.Max(Math.Abs);
        }

        /// <summary>
        /// 曲率が進んで M/φ が EI0 の 5% を下回っても (φ = 50·φy で約 2%)、要素の曲げモーメントは曲線の M に一致する。
        /// 以前は割線にも 5% の下限を掛けていたので、0.05·EI0·φ ≒ 2.4 倍のモーメントを負担していた。
        /// </summary>
        [TestMethod]
        public void TheSecantFollowsTheCurveBelowFivePercentOfEI()
        {
            double phi = 50 * PhiY;
            var (model, beam) = PureBendingBeam(phi);
            HorizontalCalculationViewModel.UpdateBeamMPhiSecant(model);
            beam.SetBeamDispAndForce();

            double curveM = beam.ResolvedCombinedCurve!.EvaluateMoment(phi);
            Assert.IsTrue(curveM / phi < 0.05 * EI0, $"(前提) M/φ = {curveM / phi:F0} が EI0 の 5% ({0.05 * EI0:F0}) を下回る点で見る");
            Assert.AreEqual(phi, beam.CurrentCurvature, 1e-12, "(前提) 要素の曲率");

            double m = MaxEndMoment(beam);
            Assert.AreEqual(curveM, m, 1e-6 * curveM,
                $"要素の曲げモーメント {m:F3} kN·m が M-φ 曲線の {curveM:F3} kN·m と違います (割線に下限が掛かっている?)");
        }

        /// <summary>弾性域では従来どおり (割線 = 初期勾配)。</summary>
        [TestMethod]
        public void InTheElasticRangeTheSecantIsUnchanged()
        {
            double phi = 0.5 * PhiY;
            var (model, beam) = PureBendingBeam(phi);
            HorizontalCalculationViewModel.UpdateBeamMPhiSecant(model);
            beam.SetBeamDispAndForce();

            Assert.AreEqual(1.0, beam.KSec_y, 1e-9);
            Assert.AreEqual(EI0 * phi, MaxEndMoment(beam), 1e-9 * EI0 * phi);
        }

        /// <summary>
        /// 下限は Newton の係数行列 (接線) にだけ残す。係数行列を正定値に保つためのもので、
        /// 釣合いの判定 (内力) には効かない。
        /// </summary>
        [TestMethod]
        public void TheFloorStaysOnTheTangentOnly()
        {
            double phi = 50 * PhiY;
            var (model, beam) = PureBendingBeam(phi);
            HorizontalCalculationViewModel.UpdateBeamMPhiTangent(model);
            HorizontalCalculationViewModel.UpdateBeamMPhiSecant(model);

            Assert.IsTrue(Math.Min(beam.KTan_y, beam.KTan_z) >= 0.05 - 1e-15,
                $"接線の剛性比は下限 0.05 を保つ (KTan_y={beam.KTan_y}, KTan_z={beam.KTan_z})");
            Assert.IsTrue(beam.KSec_y < 0.05, $"割線の剛性比は曲線どおり 0.05 を下回る (KSec_y={beam.KSec_y})");
        }

        // ── 2. 強制変位 (地盤変位) は縮めずに与えきる ─────────────────────────────

        /// <summary>
        /// A (固定) ─ B (自由) ─ C (Ux を強制) の 2 要素の軸方向。C に 0.2 m を与えると、釣合いは B = 0.1 m。
        /// 変位増分の頭打ち (0.05) に掛かる大きさにしてある。
        /// </summary>
        private static (AnaModel Model, Node B, Node C) AxialBarWithImposedEnd(double imposed)
        {
            var a = new Node { Name = "A", Coord = new Point3D(0, 0, 0), Boundary = new Boundary(true, true, true, true, true, true) };
            var b = new Node { Name = "B", Coord = new Point3D(1, 0, 0), Boundary = new Boundary(false, false, false, false, false, false) };
            // C は Ux・Uy だけ自由 (強制変位の自由度は Ux・Uy)。残りは固定
            var c = new Node { Name = "C", Coord = new Point3D(2, 0, 0), Boundary = new Boundary(false, false, true, true, true, true) };
            var section = new Section(new Material(E, 0.3), 0.01, 0.01, 0.01, 2e-4, Iy, Iy);
            var ab = new Beam("AB", section, a, b, 1.0, 1.0);
            var bc = new Beam("BC", section, b, c, 1.0, 1.0);
            var model = new AnaModel(new PileDesign.Models.InputData.InputModel(), [a, b, c], [ab, bc], [], [], [], []);

            c.IsForcedDisped = true;
            c.CumulativeForcedDisp = new NodeDisp(imposed, 0, 0, 0, 0, 0);
            model.UpdateVectorDOFForcedDisp();
            model.InitializeVectorR();
            return (model, b, c);
        }

        /// <summary>
        /// 残差が許容値を切って「収束」した時点で、地盤変位が目標まで入っていること。
        ///
        /// <para>以前は頭打ちが増分全体を 1/4 に縮め、C = 0.05・B = 0.025 で釣り合ってしまい、残差 0 (強制変位の
        /// 自由度は残差に入れない) で 1 反復目に抜けていた。与えた地盤変位の 1/4 しか効いていない状態が
        /// 「収束」として受理される。</para>
        /// </summary>
        [TestMethod]
        public void ConvergenceIsOnlyReachedWithTheFullImposedDisplacement()
        {
            const double imposed = 0.2;
            var (model, b, c) = AxialBarWithImposedEnd(imposed);

            int iterations = 0;
            for (int it = 1; it <= 20; it++)
            {
                iterations = it;
                model.MapOnKtanMat();
                Solver.SolveDisp(model);
                foreach (var beam in model.Beams) beam.SetBeamDispAndForce();
                model.SetT();
                model.FindR();
                if (model.NormsROnNormsFint < HorizontalCalculationViewModel.BaseConvergenceTolerance) break;
            }

            Assert.AreEqual(imposed, c.CumulativeDisp.Ux, 1e-9,
                $"{iterations} 反復で収束と判定した時点で、地盤変位が {c.CumulativeDisp.Ux:F4} m しか入っていません (目標 {imposed} m)");
            Assert.AreEqual(imposed / 2, b.CumulativeDisp.Ux, 1e-9, "釣合いの変位 (B は中点)");
            Assert.AreEqual(0.0, model.MaxForcedDispShortfall(), 1e-12);
        }

        /// <summary>頭打ちの 1 回目: 強制変位は全量、自由な自由度だけが 0.05 に縮む。</summary>
        [TestMethod]
        public void TheIncrementLimitScalesOnlyTheFreeDofs()
        {
            var (model, b, c) = AxialBarWithImposedEnd(0.2);
            model.MapOnKtanMat();
            Solver.SolveDisp(model);

            Assert.AreEqual(0.2, c.CumulativeDisp.Ux, 1e-12, "強制変位は縮めない");
            Assert.AreEqual(Solver.MaxDispIncrement, b.CumulativeDisp.Ux, 1e-12, "自由な自由度は頭打ち");
        }

        /// <summary>緩和係数・線探索の α を掛けるときも同じ (強制変位の成分はそのまま)。</summary>
        [TestMethod]
        public void ScalingKeepsTheForcedComponents()
        {
            var v = Vector<double>.Build.DenseOfArray([0.1, 0.2, -0.3, 0.4]);
            var scaled = Solver.ScaleKeepingForcedDofs(v, 0.25, new HashSet<int> { 1, 3 });
            CollectionAssert.AreEqual(new[] { 0.025, 0.2, -0.075, 0.4 }, scaled.ToArray());
            Assert.AreEqual(0.1, v[0], 0.0, "元の増分は書き換えない");

            // 頭打ちの最大値は自由な自由度だけで測る (強制変位 0.4 は数えない)
            var limited = Solver.LimitIncrementKeepingForcedDofs(
                Vector<double>.Build.DenseOfArray([0.01, 0.4, -0.02]), new HashSet<int> { 1 });
            CollectionAssert.AreEqual(new[] { 0.01, 0.4, -0.02 }, limited.ToArray(), "自由な自由度は 0.05 以内なので縮めない");
        }

        /// <summary>
        /// 安全網: 残差が許容値を切っていても、地盤変位が目標に届いていなければ収束にしない。
        /// </summary>
        [TestMethod]
        public void TheStepJudgeRequiresTheImposedDisplacement()
        {
            var ok = HorizontalCalculationViewModel.JudgeStepConvergence(1e-8, 1e-6, 3, 100, forcedDispShortfall: 0.0);
            Assert.IsTrue(ok.Converged);

            var shortfall = HorizontalCalculationViewModel.JudgeStepConvergence(1e-8, 1e-6, 3, 100, forcedDispShortfall: 0.15);
            Assert.IsFalse(shortfall.Converged, "地盤変位を 0.15 m 残したまま収束にしてはいけない");
            StringAssert.Contains(shortfall.UnconvergedReason, "地盤変位");

            Assert.IsFalse(HorizontalCalculationViewModel.JudgeStepConvergence(1e-8, 1e-6, 3, 100, double.NaN).Converged,
                "残りが数値でないなら収束にしない");
        }

        // ── 3. 収束基準の緩和には上限がある ─────────────────────────────

        private const double Alpha = HorizontalCalculationViewModel.BaseConvergenceTolerance;
        private const double Relaxed = HorizontalCalculationViewModel.RelaxedResidualTolerance;
        private const double Cap = HorizontalCalculationViewModel.RelaxedAcceptanceCap;

        /// <summary>
        /// 残差 0.3 (力の比で 0.55) は、停滞・長期未改善・緩やかな発散のどれでも緩和しない。
        /// 以前はどれも「現在残差 ×1.1 = 0.33」まで緩め、最終の再試行ではそのまま「緩和収束」として受理していた。
        /// </summary>
        [TestMethod]
        public void ALargeResidualIsNeverRelaxedIntoAcceptance()
        {
            const double r = 0.3;

            var stagnation = HorizontalCalculationViewModel.UpdateStagnationRelaxation(r, r, 29, Alpha, Alpha, Relaxed);
            Assert.AreEqual(Alpha, stagnation.EffectiveAlpha, 0.0, "長期停滞で緩めてはいけない");
            Assert.IsNull(stagnation.Log);

            Assert.IsNull(HorizontalCalculationViewModel.RelaxOnNoImprovement(r, 0.29, Alpha, Relaxed),
                "長期未改善で緩めてはいけない");
            Assert.IsNull(HorizontalCalculationViewModel.RelaxOnSlowDivergence(r, 0.29, Alpha),
                "緩やかな発散で緩めてはいけない");
        }

        /// <summary>上限以下では従来どおり緩める (振動・停滞で抜けられないステップを再試行の手前で受理する)。</summary>
        [TestMethod]
        public void BelowTheCapTheRelaxationRulesAreUnchanged()
        {
            const double r = 1e-3;

            var stagnation = HorizontalCalculationViewModel.UpdateStagnationRelaxation(r, r, 29, Alpha, Alpha, Relaxed);
            Assert.AreEqual(r * 1.1, stagnation.EffectiveAlpha, 1e-15);

            Assert.AreEqual(Math.Max(Math.Max(0.9e-3 * 1.2, r * 1.1), Relaxed),
                HorizontalCalculationViewModel.RelaxOnNoImprovement(r, 0.9e-3, Alpha, Relaxed)!.Value, 1e-15);
            Assert.AreEqual(Math.Max(r * 1.1, 0.99e-3 * 1.05),
                HorizontalCalculationViewModel.RelaxOnSlowDivergence(r, 0.99e-3, Alpha)!.Value, 1e-15);

            // 上限ちょうどは緩める側 (改善率不足の判定と同じ「以下」)
            Assert.IsNotNull(HorizontalCalculationViewModel.RelaxOnNoImprovement(Cap, Cap, Alpha, Relaxed));
            Assert.IsNull(HorizontalCalculationViewModel.RelaxOnNoImprovement(Cap * 1.0001, Cap, Alpha, Relaxed));
        }

        /// <summary>今より厳しい値へは動かさない (既存の約束)。</summary>
        [TestMethod]
        public void TheHelpersNeverTighten()
        {
            Assert.IsNull(HorizontalCalculationViewModel.RelaxOnNoImprovement(1e-3, 1e-3, 5e-3, Relaxed));
            Assert.IsNull(HorizontalCalculationViewModel.RelaxOnSlowDivergence(1e-3, 1e-3, 5e-3));
        }

        // ── 4. 単杭沈下: 失敗した荷重段階の巻き戻し ─────────────────────────────

        /// <summary>
        /// 指定した限界値へ合わせた荷重段階を 1 回だけ失敗扱いにして、荷重-沈下曲線を作る。
        /// </summary>
        private static (VerticalLoadTransferMethod Method, PileDesign.Models.InputData.SoilPile Pile, int Failed) RunFailingOnce(string limitName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); throw new InvalidOperationException(); }
            var soilPile = model.ElementDivision.SoilPiles[0];

            int failed = 0;
            VerticalLoadTransferMethod.FailLoadStepForTesting = name =>
            {
                if (name != limitName || failed > 0) return false;
                failed++;
                return true;
            };
            try
            {
                return (new VerticalLoadTransferMethod(model, soilPile), soilPile, failed);
            }
            finally
            {
                VerticalLoadTransferMethod.FailLoadStepForTesting = null;
            }
        }

        /// <summary>
        /// 極限 (R_ULS) に合わせた段階が失敗しても、記録する極限の行は R_ULS の荷重。
        /// 以前は「到達済み・丁度」の印が残ったまま半分の刻みで再試行し、R_ULS ではない荷重の行を極限として
        /// 記録して打ち切っていた。
        /// </summary>
        [TestMethod]
        public void AFailedUltimateStepDoesNotRecordAFakeUltimateRow()
        {
            var (method, pile, failed) = RunFailingOnce("R_ULS");
            Assert.AreEqual(1, failed, "(前提) R_ULS に合わせた段階を 1 回失敗させた");

            var compression = method.LoadDisplacementsLimit.Where(r => r.PileTopLoad > 0).Select(r => r.PileTopLoad).ToList();
            var expected = new[] { pile.R_SLS, pile.R_DLS, pile.R_ULS };
            foreach (double load in compression)
                Assert.IsTrue(expected.Any(x => Math.Abs(x - load) < 1e-6),
                    $"限界値の行 {load:F3} kN が R_SLS/R_DLS/R_ULS ({string.Join(", ", expected.Select(x => x.ToString("F3")))}) のどれでもありません");
            Assert.IsTrue(compression.Any(l => Math.Abs(l - pile.R_ULS) < 1e-6), "極限 (R_ULS) の行がありません");
        }

        /// <summary>
        /// 使用限界 (R_SLS) に合わせた段階が失敗しても、R_SLS の行は記録される。
        /// 以前は「到達済み」の印が残り、再試行で R_SLS の判定を飛ばして行そのものが欠けていた。
        /// </summary>
        [TestMethod]
        public void AFailedServiceabilityStepIsRetriedNotSkipped()
        {
            var (method, pile, failed) = RunFailingOnce("R_SLS");
            Assert.AreEqual(1, failed, "(前提) R_SLS に合わせた段階を 1 回失敗させた");

            Assert.IsTrue(method.LoadDisplacementsLimit.Any(r => Math.Abs(r.PileTopLoad - pile.R_SLS) < 1e-6),
                $"使用限界 (R_SLS = {pile.R_SLS:F3} kN) の行がありません。限界値の行: "
                + string.Join(", ", method.LoadDisplacementsLimit.Select(r => r.PileTopLoad.ToString("F3"))));
        }

        /// <summary>
        /// 失敗を挟んでも、限界値の行は失敗させないときと同じ荷重に並ぶ (巻き戻しが完全なら、刻みが細かくなるだけ)。
        /// </summary>
        [TestMethod]
        public void TheLimitRowsMatchTheRunWithoutFailure()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }
            var plain = new VerticalLoadTransferMethod(model, model.ElementDivision.SoilPiles[0]);
            var expected = plain.LoadDisplacementsLimit.Select(r => Math.Round(r.PileTopLoad, 6)).ToList();
            TestSource.AssertScanned(expected.Count, 3, "限界値の行");

            foreach (var name in new[] { "R_SLS", "R_DLS", "R_ULS", "Rt_SLS", "Rt_DLS", "Rt_ULS" })
            {
                var (method, _, failed) = RunFailingOnce(name);
                if (failed == 0) continue;   // この例題ではその限界値に合わせる段階が無い
                var actual = method.LoadDisplacementsLimit.Select(r => Math.Round(r.PileTopLoad, 6)).ToList();
                CollectionAssert.AreEqual(expected, actual,
                    $"{name} の段階を失敗させると限界値の行が変わりました: [{string.Join(", ", expected)}] → [{string.Join(", ", actual)}]");
            }
        }
    }
}
