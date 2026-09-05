using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Linq;
using System.Text;

namespace TestProject1
{
    /// <summary>
    /// 収束しなかった荷重ケースの結果が、検定で OK を名乗らないこと。
    ///
    /// 水平解析は反復が上限に達しても残差が下がらないことがある。そのステップの変位は
    /// <b>釣り合っていない</b>ので、応答値を限界値と比べても意味が無い。
    /// それでも解析は次のステップへ進み、結果は普通に記録される。
    ///
    /// 以前は収束状態が解析ウィンドウのサマリー (メモリ上) にしか無く、
    /// 検定・計算書・保存ファイルのどこにも伝わっていなかった。そのため
    /// <b>収束していないケースの応答値がそのまま「OK」として表に並んでいた</b>。
    /// ここで固定するのは「未収束は OK でも NG でもない」という 1 点。
    /// </summary>
    [TestClass]
    public class UnconvergedCaseEvaluationTests
    {
        private static EvaluationItem Moment(StepStatus status, double response = 100, double limit = 200) => new()
        {
            Kind = EvaluationKind.PileSectionMoment,
            Level = 2,
            Category = "杭体曲げ (安全限界)",
            LimitName = "安全限界",
            TargetName = "beam",
            EndLabel = "i端",
            PileBodyNo = 1,
            PileNo = 1,
            SegmentIndex = 1,
            LoadCaseName = "L2-1",
            LoadCombinationName = "cmb1",
            IsLiquefaction = true,
            Response = response,
            Limit = limit,
            Unit = "kN·m",
            AxialForce = 1000.0,
            IsOk = !(response > limit),
            CaseConvergence = status,
        };

        // ── 判定の表示 ───────────────────────────────────────

        [TestMethod]
        public void ConvergedItem_KeepsOkOrNg()
        {
            Assert.AreEqual("OK", Moment(StepStatus.Converged, response: 100, limit: 200).StatusLabel);
            Assert.AreEqual("NG", Moment(StepStatus.Converged, response: 300, limit: 200).StatusLabel);
        }

        [TestMethod]
        public void UnconvergedItem_DoesNotClaimOk()
        {
            // 限界値を下回っていても OK と言ってはいけない (解いていないので)
            var item = Moment(StepStatus.Unconverged, response: 100, limit: 200);
            Assert.IsTrue(item.IsOk, "IsOk は算術上の比較なので true のまま");
            Assert.IsTrue(item.IsFromUnconvergedCase);
            Assert.AreEqual("未収束", item.StatusLabel);
        }

        [TestMethod]
        public void PhysicallyUnconvergedItem_DoesNotClaimNgEither()
        {
            var item = Moment(StepStatus.PhysicallyUnconverged, response: 300, limit: 200);
            Assert.AreEqual("未収束", item.StatusLabel);
        }

        // ── 集計 ───────────────────────────────────────

        [TestMethod]
        public void Counts_ExcludeUnconvergedItems()
        {
            var result = new EvaluationResult([
                Moment(StepStatus.Converged, response: 100, limit: 200),   // OK
                Moment(StepStatus.Converged, response: 300, limit: 200),   // NG
                Moment(StepStatus.Unconverged, response: 100, limit: 200), // 未収束 (見かけ上は OK)
                Moment(StepStatus.PhysicallyUnconverged, response: 900, limit: 200), // 未収束 (見かけ上は NG)
            ]);

            Assert.AreEqual(1, result.OkCount);
            Assert.AreEqual(1, result.NgCount);
            Assert.AreEqual(2, result.UnconvergedCount);
            Assert.AreEqual(4, result.Items.Count);
        }

        [TestMethod]
        public void GoverningCase_IsNotAnUnconvergedOne()
        {
            // 検定比が最大なのは未収束の行 (900/200 = 4.5) だが、支配ケースはそれではない
            var result = new EvaluationResult([
                Moment(StepStatus.Converged, response: 300, limit: 200),
                Moment(StepStatus.PhysicallyUnconverged, response: 900, limit: 200),
            ]);

            Assert.IsNotNull(result.Governing);
            Assert.IsFalse(result.Governing!.IsFromUnconvergedCase);
            Assert.AreEqual(1.5, result.MaxRatio!.Value, 1e-9);
        }

        [TestMethod]
        public void Filter_KeepsUnconvergedItemsOnBothSides()
        {
            var unconverged = Moment(StepStatus.Unconverged, response: 100, limit: 200);

            // 「NG のみ」で消えると見落とす。「OK のみ」で消えても見落とす。
            Assert.IsTrue(EvaluationResult.PassesFilter(unconverged, 0));
            Assert.IsTrue(EvaluationResult.PassesFilter(unconverged, 1));
            Assert.IsTrue(EvaluationResult.PassesFilter(unconverged, 2));
        }

        // ── テキスト ───────────────────────────────────────

        [TestMethod]
        public void Text_TagsUnconvergedInsteadOfOk()
        {
            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, Moment(StepStatus.Unconverged, response: 100, limit: 200));
            string text = sb.ToString();

            StringAssert.Contains(text, "[未収束]");
            Assert.IsFalse(text.Contains("[OK]"), "収束していないケースが OK を名乗っている: " + text);
        }

        [TestMethod]
        public void Text_UnconvergedNgDoesNotSayExceeded()
        {
            // 「超過」も断定できない (釣り合っていない値なので)
            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, Moment(StepStatus.PhysicallyUnconverged, response: 300, limit: 200));
            string text = sb.ToString();

            StringAssert.Contains(text, "[未収束]");
            Assert.IsFalse(text.Contains("超過"), "未収束なのに超過と断定している: " + text);
        }

        [TestMethod]
        public void Text_ConvergedOutputIsUnchanged()
        {
            // 収束していれば従来と 1 文字も変わらないこと (golden テストが依存している)
            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, Moment(StepStatus.Converged, response: 300, limit: 200));

            StringAssert.Contains(sb.ToString(), "[NG] 安全限界超過（i端）");
        }

        // ── 結果からケース単位の収束状態を畳む ──────────────────────

        /// <summary>組合せ名は Alpha1/Beta1/Beta2 から作られる計算値なので、そこから決まる。</summary>
        private static readonly LoadCombination Combo = new(1, 1.0, 1.0, 1.0);

        private static AnalysisStepResult Step(int step, StepStatus status) =>
            new(new LoadCase { LoadName = "L2-1" }, Combo,
                isLiquefaction: true, step: step, iteration: 5, residualValue: 1e-7, status: status);

        [TestMethod]
        public void CaseConvergence_TakesTheWorstStepNotTheLast()
        {
            // 途中のステップが収束していなければ、そこから先は釣り合っていない状態の上に積み上がる。
            // 最終ステップだけを見て「収束した」と言ってはいけない。
            var model = new AnaModel();
            model.AnalysisStepResults.Add(Step(1, StepStatus.Converged));
            model.AnalysisStepResults.Add(Step(2, StepStatus.Unconverged));
            model.AnalysisStepResults.Add(Step(3, StepStatus.Converged));

            var map = model.BuildCaseConvergenceMap();

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual(StepStatus.Unconverged, map[("L2-1", Combo.Name, true)]);
            Assert.IsTrue(model.HasUnconvergedSteps());
        }

        [TestMethod]
        public void CaseConvergence_AllConvergedIsConverged()
        {
            var model = new AnaModel();
            model.AnalysisStepResults.Add(Step(1, StepStatus.Converged));
            model.AnalysisStepResults.Add(Step(2, StepStatus.Converged));

            Assert.AreEqual(StepStatus.Converged, model.BuildCaseConvergenceMap()[("L2-1", Combo.Name, true)]);
            Assert.IsFalse(model.HasUnconvergedSteps());
        }

        [TestMethod]
        public void OldFilesWithoutTheField_ReadAsConverged()
        {
            // 収束状態を記録していない古い保存ファイルは、従来どおり (収束したものとして) 扱う。
            Assert.AreEqual(StepStatus.Converged, new AnalysisStepResult().Status);
        }

        [TestMethod]
        public void DeepCopy_CarriesTheStatus()
        {
            var copy = Step(1, StepStatus.PhysicallyUnconverged).DeepCopy();
            Assert.AreEqual(StepStatus.PhysicallyUnconverged, copy.Status);
        }
    }
}
