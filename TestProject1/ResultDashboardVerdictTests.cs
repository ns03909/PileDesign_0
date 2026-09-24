using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using PileDesign.Services;
using System;
using static PileDesign.Views.ResultDashboardWindow;

namespace TestProject1
{
    /// <summary>
    /// ダッシュボードの総合判定が、判定できない項目を「OK」と読ませないこと。
    ///
    /// - 適用範囲外の項目があっても、総合判定は数えず緑の「すべて OK」を出していた
    ///   (杭ごとの一覧には「適用範囲外」と出ているのに)。
    /// - 検定の組み立てに失敗しても、失敗は「検定なし」として扱われ、支持力だけを見て
    ///   「すべて OK」を出し得た。
    /// </summary>
    [TestClass]
    public class ResultDashboardVerdictTests
    {
        private static EvaluationItem Item(int pileNo, double response, double limit, bool ok,
            string? outOfScope = null, StepStatus status = StepStatus.Converged,
            EvaluationKind kind = EvaluationKind.PileSectionShear)
            => new()
            {
                Kind = kind,
                Level = 2,
                Category = kind == EvaluationKind.PileBearingCompression ? "押込み支持力" : "杭体せん断 (安全限界)",
                PileBodyNo = 1,
                PileNo = pileNo,
                LoadCaseName = "L2-1",
                LoadCombinationName = "1.0/1.0/1.0",
                IsLiquefaction = false,
                Response = response,
                Limit = limit,
                IsOk = ok,
                CaseConvergence = status,
                OutOfScopeReason = outOfScope,
                Unit = "kN",
            };

        private static EvaluationResult Bearing(bool ok = true)
            => new([Item(1, ok ? 50 : 150, 100, ok, kind: EvaluationKind.PileBearingCompression)]);

        [TestMethod]
        public void AllJudgedAndOk_IsAllOk()
        {
            var summary = PileEvaluationSummary.FromResults(new EvaluationResult([Item(1, 50, 100, true)]), null, Bearing(), "A");
            var v = DecideVerdict(summary, horizontalDone: true);

            Assert.AreEqual(VerdictKind.Ok, v.Kind);
            StringAssert.StartsWith(v.Text, "すべて OK");
        }

        [TestMethod]
        public void OutOfScopeItems_AreNotAllOk()
        {
            var horizontal = new EvaluationResult(
            [
                Item(1, 50, 100, true),
                Item(2, 60, 100, true, outOfScope: "Fc が工法の適用範囲の外"),
            ]);
            var summary = PileEvaluationSummary.FromResults(horizontal, null, Bearing(), "A");
            var v = DecideVerdict(summary, horizontalDone: true);

            Assert.AreEqual(VerdictKind.CannotJudge, v.Kind,
                "適用範囲外の項目があるのに、総合判定が OK の色になっています");
            Assert.AreEqual("適用範囲外 1 件", v.Text);
        }

        [TestMethod]
        public void NgComesFirstEvenWithOutOfScopeItems()
        {
            var horizontal = new EvaluationResult(
            [
                Item(1, 150, 100, false),
                Item(2, 60, 100, true, outOfScope: "杭径が工法の適用範囲の外"),
            ]);
            var v = DecideVerdict(PileEvaluationSummary.FromResults(horizontal, null, Bearing(), "A"), horizontalDone: true);

            Assert.AreEqual(VerdictKind.Ng, v.Kind);
            Assert.AreEqual("NG 1 件", v.Text);
        }

        [TestMethod]
        public void UnconvergedNoteMentionsOutOfScopeToo()
        {
            var horizontal = new EvaluationResult(
            [
                Item(1, 50, 100, true, status: StepStatus.Unconverged),
                Item(2, 60, 100, true, outOfScope: "Fc が工法の適用範囲の外"),
            ]);
            var v = DecideVerdict(PileEvaluationSummary.FromResults(horizontal, null, Bearing(), "A"), horizontalDone: true);

            Assert.AreEqual(VerdictKind.CannotJudge, v.Kind);
            Assert.AreEqual("未収束 1 件", v.Text);
            StringAssert.Contains(v.Note, "適用範囲外");
        }

        /// <summary>
        /// 水平解析は済んでいるのに低減後の検定が組めなかったとき、支持力が OK でも「すべて OK」にしないこと。
        /// </summary>
        [TestMethod]
        public void AFailedHorizontalEvaluation_CannotBeJudged()
        {
            var summary = PileEvaluationSummary.FromResults(null, null, Bearing(), "A",
                [new PileEvaluationSummary.BuildFailure(PileEvaluationSummary.HorizontalPart, "試験用の失敗")]);

            Assert.IsTrue(summary.HorizontalFailed, "失敗がまとめに残っていません");
            Assert.IsFalse(summary.BearingFailed);

            var v = DecideVerdict(summary, horizontalDone: true);
            Assert.AreEqual(VerdictKind.CannotJudge, v.Kind,
                "水平解析の検定が組めなかったのに、総合判定が OK になっています");
            Assert.AreEqual("判定できません", v.Text);
            StringAssert.Contains(v.Note, PileEvaluationSummary.HorizontalPart);
            StringAssert.Contains(v.Note, "試験用の失敗");
        }

        /// <summary>まとめ全体が組めなかったときは、水平解析をしていなくても「未実施」「支持力 OK」にしないこと。</summary>
        [TestMethod]
        public void AWholeFailure_CannotBeJudged()
        {
            var summary = PileEvaluationSummary.FromResults(null, null, new EvaluationResult([]), "A",
                [new PileEvaluationSummary.BuildFailure(PileEvaluationSummary.WholePart, "試験用の失敗")]);

            Assert.IsTrue(summary.HorizontalFailed && summary.HorizontalUnfactoredFailed && summary.BearingFailed);
            Assert.AreEqual(VerdictKind.CannotJudge, DecideVerdict(summary, horizontalDone: false).Kind,
                "検定が組めなかったのに、「未実施」と表示しています");
            Assert.AreEqual(VerdictKind.CannotJudge, DecideVerdict(summary, horizontalDone: true).Kind);
        }

        /// <summary>組み立ての失敗を、まとめに記録して画面へ渡していること (ログだけで終わらせない)。</summary>
        [TestMethod]
        public void BuildRecordsFailuresInsteadOfOnlyLoggingThem()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "PileEvaluationSummary.cs");
            string tryBuild = TestSource.MethodBody(src,
                "private static EvaluationResult? TryBuild(Func<EvaluationResult> build, string part, List<BuildFailure> failures)");
            StringAssert.Contains(tryBuild, "failures.Add(", "検定の組み立ての失敗を記録していません (画面が「検定なし」と区別できない)");

            string build = TestSource.MethodBody(src, "public static PileEvaluationSummary Build(MainWindowViewModel vm)");
            StringAssert.Contains(build, "FromResults(horizontal, unfactored, bearing, seismicGrade, failures)",
                "記録した失敗を、まとめに渡していません");

            string dashboard = TestSource.Read("Graphics_r1", "Views", "ResultDashboardWindow.xaml.cs");
            StringAssert.Contains(dashboard, "PileEvaluationSummary.WholePart",
                "ダッシュボードがまとめを組めなかったとき、失敗を「検定なし」として扱っています");
        }
    }
}
