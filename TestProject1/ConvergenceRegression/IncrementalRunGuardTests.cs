using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Text.Json;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 水平解析の「追加実行」で、異なる条件の結果を混ぜないこと (2026-09-27 のレビュー)。
    /// 1. 解析に効く入力が前回と同じときだけ追加実行する (以前は InputModelHash が常に null で入力を照合しなかった)
    /// 2. 「実行済み」は最後まで解けたケースだけ (以前は結果が 1 ステップでもあれば済として飛ばした)
    /// 3. 実行済みのキーは番号で作る (以前は荷重ケース名と、係数を丸めた組合せの表示名)
    /// </summary>
    [TestClass]
    public class IncrementalRunGuardTests
    {
        // ── 3. キー ─────────────────────────────────────

        [TestMethod]
        public void CombinationsWithTheSameDisplayNameGetDifferentKeys()
        {
            var lc = new LoadCase { Level = 2, No = 3, LoadName = "U1" };
            var one = new LoadCombination(1, 0.999, -1.0, 0.999);
            var two = new LoadCombination(2, 1.0, -0.999, 1.0);
            Assert.AreEqual(one.GetName(), two.GetName(), "(前提) 表示名が同じ組合せです");

            Assert.AreNotEqual(AnalysisRunSnapshot.CaseKey.Of(lc, one, false), AnalysisRunSnapshot.CaseKey.Of(lc, two, false),
                "表示名の同じ組合せを同じケースとして扱っています");
            Assert.AreEqual(new AnalysisRunSnapshot.CaseKey(2, 3, 1, false), AnalysisRunSnapshot.CaseKey.Of(lc, one, false));
        }

        /// <summary>名前で作っていた旧形式のキーは番号が 0 で読まれる (追加実行はそれを飛ばす判断に使わない)。</summary>
        [TestMethod]
        public void LegacyKeysReadWithoutNumbers()
        {
            var legacy = JsonSerializer.Deserialize<AnalysisRunSnapshot.CaseKey>(
                "{\"LoadName\":\"U1\",\"CombinationName\":\"1.00/1.00/1.00\",\"IsLiquefaction\":true}");
            Assert.IsNotNull(legacy);
            Assert.AreEqual(0, legacy.CaseNo);

            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            StringAssert.Contains(run, ".Where(k => k.CaseNo > 0)", "旧形式のキーを既存ケースとして読んでいます");
        }

        [TestMethod]
        public void TheCompletedMarkMatchesTheCaseByLevelAndNumber()
        {
            var converter = new CompletedCaseToCheckMarkConverter();
            var keys = new[] { new AnalysisRunSnapshot.CaseKey(2, 1, 1, false).ToDisplayKey() };
            object Mark(LoadCase lc) => converter.Convert([lc, keys], typeof(string), null!, null!);

            Assert.AreEqual("✓", Mark(new LoadCase { Level = 2, No = 1, LoadName = "同名" }));
            Assert.AreEqual("", Mark(new LoadCase { Level = 1, No = 1, LoadName = "同名" }), "レベルの違う同じ番号のケースに印が付いています");
        }

        // ── 1. 入力の照合 ───────────────────────────────

        [TestMethod]
        public void TheSignatureTracksAnalysisInputButNotTheTargetSelection()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, error);
            string? original = HorizontalCalculationViewModel.HorizontalInputSignature(input);
            Assert.IsNotNull(original);

            // 解析対象を足すのは追加実行の使い方そのもの。署名は変わらない
            var target = input.LoadCasesInput.LoadCasesLevel1.Concat(input.LoadCasesInput.LoadCasesLevel2).First();
            target.IsAnalysisTarget = !target.IsAnalysisTarget;
            Assert.AreEqual(original, HorizontalCalculationViewModel.HorizontalInputSignature(input), "解析対象のチェックで署名が変わります");

            // 群杭沈下の入力は水平解析が読まない
            input.PileGroupSettlement.LoadingPlaneAltitude += 1.0;
            Assert.AreEqual(original, HorizontalCalculationViewModel.HorizontalInputSignature(input), "群杭沈下の入力で署名が変わります");

            // 地盤を変えたら別の条件
            var layer = input.GroundsInput.First(g => g?.GroundLayers?.Count > 0).GroundLayers[0];
            layer.LayerThickness += 0.5;
            Assert.AreNotEqual(original, HorizontalCalculationViewModel.HorizontalInputSignature(input), "地盤を変えても署名が変わりません");
        }

        [TestMethod]
        public void TheCompatibilityCheckComparesTheInput()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "private bool ValidateIncrementalCompatibility(");
            StringAssert.Contains(body, "prev.InputModelHash == null", "入力の記録が無い前回結果に追加実行を許しています");
            StringAssert.Contains(body, "HorizontalInputSignature(InputModel)", "追加実行の前に入力を照合していません");

            string snapshot = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "private FEM.AnalysisRunSnapshot CaptureCurrentRunSnapshot()");
            StringAssert.Contains(snapshot, "InputModelHash = HorizontalInputSignature(InputModel)", "解析したときの入力の署名を残していません");
        }

        // ── 2. 実行済みの判定と、やり直す前の片付け ─────────────────

        [TestMethod]
        public void OnlyCasesThatFinishedCountAsExecuted()
        {
            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            string finish = TestSource.MethodBody(run, "private async Task FinishRunAsync(");
            StringAssert.Contains(finish, "ctx.CompletedKeys", "実行済みを、最後まで解けたケースから作っていません");
            Assert.IsFalse(finish.Contains("AnalysisStepResults\n") || finish.Contains(".Select(r => new FEM.AnalysisRunSnapshot.CaseKey("),
                "結果が 1 ステップでもあるケースを実行済みにしています");

            string solve = TestSource.MethodBody(run, "private async Task SolveOneCaseAsync(");
            StringAssert.Contains(solve, "if (caseConverged)", "最後まで解けたかで実行済みを決めていません");

            StringAssert.Contains(run, "targetModel.RemoveCaseResults(loadCase, loadCombination, isLiquefaction);",
                "やり直すケースの途中までの結果を取り除いていません (新旧が並ぶ)");
        }

        [TestMethod]
        public void RemoveCaseResultsTakesOutOnlyThatCase()
        {
            AnaModel model;
            try
            {
                model = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 2, Level2Steps = 4, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                }).CurrentModel!;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return;
            }

            var cases = model.AnalysisStepResults
                .Select(r => (r.LoadCase, r.LoadCombination, r.IsLiquefaction))
                .DistinctBy(c => (c.LoadCase.Level, c.LoadCase.No, c.LoadCombination.No, c.IsLiquefaction))
                .ToList();
            Assert.IsTrue(cases.Count >= 2, "(前提) 荷重条件が 2 つ以上ありません");
            var (lc, comb, liq) = cases[0];
            bool Same(LoadCase? l, LoadCombination? c, bool q) =>
                q == liq && LoadCase.IsSameCase(l, lc) && LoadCombination.IsSameCombination(c, comb);
            int othersBefore = model.AnalysisStepResults.Count(r => !Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction));

            model.RemoveCaseResults(lc, comb, liq);

            Assert.IsFalse(model.AnalysisStepResults.Any(r => Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction)));
            Assert.IsFalse(model.Beams.Any(b => b.BeamResults.Any(r => Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction))));
            Assert.IsFalse(model.Nodes.Any(n => n.NodeResults.Any(r => Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction))));
            Assert.IsFalse(model.HorizontalSoilSprings.Any(s => s.HorizontalSpringResults.Any(r => Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction))));
            Assert.IsFalse(model.RotationalSprings.Any(s => s.RotationalSpringResults.Any(r => Same(r.LoadCase, r.LoadCombination, r.IsLiquefaction))));
            Assert.AreEqual(othersBefore, model.AnalysisStepResults.Count, "ほかのケースの結果まで取り除いています");
        }
    }
}
