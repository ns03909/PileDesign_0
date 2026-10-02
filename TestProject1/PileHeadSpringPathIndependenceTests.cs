using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 杭頭の回転ばね (場所打ち RC 杭の杭頭固定) の答えが、計算の経路 (荷重の段数・反復の進み方) に依らないこと。
    ///
    /// <para>以前は、ばねが載荷・除荷を見分けるための回転の最大値を反復の途中で引き上げていた。行き過ぎた反復の値が残り、
    /// 収束点が行き過ぎた点からの除荷の線に乗って、杭頭のモーメントが骨格曲線より 1〜2 割小さく・回転角が大きく出た。
    /// 行き過ぎの大きさは経路で決まるので、計算例8 の安全限界の杭頭回転角は要素分割で 0.62〜1.13 と行き来した。
    /// 最大値はステップの収束時にだけ更新し、そのままでは解けなくなる (ひび割れ後に回転が 0 付近へ戻ると剛性が何桁も跳ぶ)
    /// のを、ひび割れ後の M–θ の最初の区間に有限の剛性 K0 = 10·My/θy を持たせて避けた (2026-10-02)。</para>
    /// </summary>
    [TestClass]
    public class PileHeadSpringPathIndependenceTests
    {
        /// <summary>ひび割れ点は θcr = Mcr/K0、K0 = 10·My/θy (それより前は解析側で剛)。</summary>
        [TestMethod]
        public void TheCrackPoint_UsesTheFiniteInitialStiffness()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("ExampleK8", "PileExampleK8");
            if (model == null) { Assert.Inconclusive(error); return; }
            int checkedCurves = 0;
            foreach (var body in model.PileBodies)
            {
                var curve = body.GetMThetaRelationship(0.0)?.CurveXY;
                if (curve == null || curve.Points.Count != 4) continue;
                var p = curve.Points;
                double k0 = InsituReinforcedConcreteSection.PostCrackInitialStiffnessFactor * p[2].Moment / p[2].Theta;
                Assert.AreEqual(p[1].Moment / k0, p[1].Theta, 1e-9 * p[1].Theta, "ひび割れ点の回転が Mcr/K0 ではありません");
                Assert.IsTrue(p[1].Theta > 1e-6, "ひび割れ点がほぼ剛 (θcr ≈ 0) のままです");
                checkedCurves++;
            }
            TestSource.AssertScanned(checkedCurves, 1, "場所打ち RC 杭の M–θ 曲線");
        }

        /// <summary>解析は反復の途中で回転の最大値を引き上げない (ステップの収束時にだけ確定する)。</summary>
        [TestMethod]
        public void TheSolver_DoesNotRaiseTheMaximumDuringIterations()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            StringAssert.Contains(src, "double thetaProj = dRx * nx + dRy * ny;");
            Assert.IsFalse(Regex.IsMatch(src, @"ThetaProjMax\s*=\s*thetaProj"),
                "反復の途中で回転の最大値を引き上げています (収束点が行き過ぎた点からの除荷の線に乗り、答えが経路に依存します)");
        }

        /// <summary>荷重の段数を 2 倍にしても、杭頭回転角・杭体の曲げの検定比がほとんど変わらない。</summary>
        [TestMethod]
        [Timeout(900000)]
        public void DoublingTheLoadSteps_KeepsThePileHeadRotation()
        {
            string Run(int l1, int l2, out double rotation, out double bending)
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel("ExampleK8", "PileExampleK8", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = l1,
                    Level2Steps = l2,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                });
                vm.ApplyConcreteModelOptions();
                var ev = EvaluationService.BuildEvaluationResult(vm, factored: true);
                Assert.AreEqual(0, ev.UnconvergedCount, $"段数 {l2} で収束しないケースがあります");
                rotation = ev.Items.Where(i => i.IsJudged && i.Kind == PileDesign.Models.Results.EvaluationKind.PileHeadRotation && i.Level == 2).Max(i => i.Ratio);
                bending = ev.Items.Where(i => i.IsJudged && i.Category.StartsWith("杭体曲げ", StringComparison.Ordinal) && i.Level == 2).Max(i => i.Ratio);
                return $"杭頭回転角 {rotation:F4}・曲げ {bending:F4}";
            }

            string a, b;
            double r8, r16, b8, b16;
            try
            {
                a = Run(4, 8, out r8, out b8);
                b = Run(8, 16, out r16, out b16);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            Assert.AreEqual(r8, r16, 0.01 * r8, $"杭頭回転角が荷重の段数で動きます (8 段: {a} / 16 段: {b})");
            Assert.AreEqual(b8, b16, 0.01 * b8, $"杭体の曲げが荷重の段数で動きます (8 段: {a} / 16 段: {b})");
        }
    }
}
