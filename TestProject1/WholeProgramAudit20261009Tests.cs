using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 のプログラム全体の見直しで直した、解析の数値を変えない不具合の再発防止。
    /// </summary>
    [TestClass]
    public class WholeProgramAudit20261009Tests
    {
        /// <summary>
        /// 群杭沈下の結果を差し替えたら、色分けの検定サマリーを作り直す。
        /// 解析完了の時刻は「未実行→実行済み」のときしか進まないので、2 回目以降の群杭沈下で色分けが前の結果のまま残った。
        /// </summary>
        [TestMethod]
        public void ReplacingTheGroupSettlementResult_RebuildsTheColoringSummary()
        {
            var vm = new MainWindowViewModel { CurrentModel = new AnaModel(), IsHorizontalAnalysisDone = true };
            var pgs = vm.CurrentInputModel?.PileGroupSettlement;
            Assert.IsNotNull(pgs, "前提: 入力に群杭沈下がある");

            var first = vm.GetEvaluationSummary();
            Assert.AreSame(first, vm.GetEvaluationSummary(), "前提: 何も変わらなければ同じまとめを返す");

            pgs.Result = new GroupSettlementResult();
            Assert.AreNotSame(first, vm.GetEvaluationSummary(), "群杭沈下の結果を差し替えたのに、色分けが前のまとめのままです");
        }

        /// <summary>基礎梁ありの反復は、沈下が数値にならない杭があれば収束を判定しない (Max も比較も NaN を素通りする)。</summary>
        [TestMethod]
        public void IterativeBeamSettlement_DoesNotJudgeConvergenceOnNonFiniteSettlements()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "IterativeBeamSettlementService.cs");
            int guard = src.IndexOf("double.IsFinite(kv.Value)", System.StringComparison.Ordinal);
            int residual = src.IndexOf("residual = maxAbs > 1e-12", System.StringComparison.Ordinal);
            Assert.IsTrue(guard > 0 && residual > guard, "沈下が有限かを、収束の残差を求める前に見ていません");
        }

        /// <summary>
        /// 杭頭回転角の検定の軸力 (FT-Pile の θa に効く) は、曲げ・せん断と同じ規則 (ResolveCheckAxialForce)。
        /// 以前は入力の軸力だけで、「入力値＋応力解析結果」で同じ杭の曲げの行と軸力が食い違った。
        /// </summary>
        [TestMethod]
        public void RotationCheck_UsesTheSameAxialForceAsBendingAndShear()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs"),
                "private List<EvaluationItem> CheckThetaLimit(AnaModel model,");
            StringAssert.Contains(body, "ResolveCheckAxialForce(");
            Assert.IsFalse(body.Contains("GetDesignAxialForce(", System.StringComparison.Ordinal),
                "回転角の検定が入力の軸力だけを使っています");
        }

        /// <summary>
        /// 画面の深さ分布図の限界線は検定と同じ前提: 軸力 (ResolveCheckAxialForce)、損傷限界はケースのレベルの曲線、
        /// せん断は検定と同じ M/(Q·d) と設計用せん断力の割増。計算書の深さ分布図も同じ軸力。
        /// </summary>
        [TestMethod]
        public void ElevationLimitLines_FollowTheCheck()
        {
            string elev = TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.ElevationGraphs.cs");
            StringAssert.Contains(elev, "EvaluationService.ResolveCheckAxialForce(");
            StringAssert.Contains(elev, "GetLimitStateNMCurve(pileSection, SelectedLimitState, loadCase.Level)");
            StringAssert.Contains(elev, "EvaluationService.ResolveMonQd(");
            StringAssert.Contains(elev, "ShearDesignMagnification");
            Assert.IsFalse(elev.Contains("GetDesignAxialForce(", System.StringComparison.Ordinal), "限界線が入力の軸力だけを使っています");

            string graph = TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.cs");
            Assert.IsFalse(graph.Contains("DamageLevelForGrade", System.StringComparison.Ordinal),
                "損傷限界の曲線を耐震グレードから選んでいます (グレード S でレベル1 のケースにレベル2 の曲線)");

            string charts = TestSource.Read("Graphics_r1", "Output", "WordDocument.Charts.cs");
            Assert.IsFalse(charts.Contains("double axialN = pli.GetDesignAxialForce(", System.StringComparison.Ordinal),
                "計算書の深さ分布図の限界線が入力の軸力だけを使っています");
        }

        /// <summary>画面の N-M・N-Q 図は、結果を持つ要素が 1 本も無い杭に点を打たない (M・Q = double.MinValue の点で軸が壊れた)。</summary>
        [TestMethod]
        public void InteractionDiagrams_SkipPilesWithoutResults()
        {
            string graph = TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.cs");
            StringAssert.Contains(graph, "if (moment == double.MinValue) continue;");
            StringAssert.Contains(graph, "if (shear == double.MinValue) continue;");
        }
    }
}
