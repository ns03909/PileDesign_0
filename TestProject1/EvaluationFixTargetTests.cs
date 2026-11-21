using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 検定できない項目 (検定不能・適用範囲外) から、直す入力画面を開く (外部レビュー #20 2026-11-21)。
    ///
    /// <para>開き先 (<see cref="EvaluationItem.FixTarget"/>) は理由を決めたところで一緒に決める。理由の文から
    /// 推し量ると、文を直したときに静かに開かなくなる。解析のやり直しで直す理由 (結果が無い・杭との対応が崩れた・
    /// 要素分割の断面が無い・杭頭の軸力が無い) は開き先を持たない。</para>
    /// </summary>
    [TestClass]
    public class EvaluationFixTargetTests
    {
        private static string Service() => TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");

        /// <summary>曲げ・せん断の「検定不能」の理由ごとに、開き先が決まっている (書き忘れを捕まえる)。</summary>
        [TestMethod]
        public void EveryUnavailableReasonHasItsDestination()
        {
            var calls = Regex.Matches(Service(), @"\{ Unavailable\((.+?)\); return; \}|Unavailable\((.+?)\); return;")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
            TestSource.AssertScanned(calls.Count, 14, "曲げ・せん断の検定不能の理由");

            foreach (var call in calls)
            {
                if (call.Contains("曲線を作れませんでした"))
                    StringAssert.Contains(call, "DiagnosticTarget.PileBody(pb)", $"断面の入力で直すのに杭体を開かない: {call}");
                else if (call.Contains("OutsideCurveReason"))
                    StringAssert.Contains(call, "DiagnosticTarget.Pile(pileItem.PileNo, pb)", $"軸力が曲線の外なのに杭を開かない: {call}");
                else
                    Assert.IsFalse(call.Contains("DiagnosticTarget"), $"解析のやり直しで直す理由に開き先が付いている: {call}");
            }

            // 適用範囲外 (せん断補強筋の工法) と杭頭回転角の工法の諸元は杭体
            StringAssert.Contains(Service(), "FixTarget = outOfScope != null ? DiagnosticTarget.PileBody(pb) : null,");
            Assert.IsTrue(Regex.IsMatch(Service(), @"criterion\.UnavailableReason,\s*DiagnosticTarget\.PileBody\(pb\)\)"),
                "杭頭回転角の工法の諸元 (FT キャップなど) の検定不能が杭体を開かない");
        }

        private static EvaluationItem Item(int pile, string? unavailable = null, string? outOfScope = null, DiagnosticTarget? fix = null,
            double response = 1, double limit = 2)
            => new()
            {
                Kind = EvaluationKind.PileSectionMoment, Category = "杭体曲げ", PileNo = pile, PileBodyNo = 1,
                Response = response, Limit = limit, IsOk = response <= limit,
                UnavailableReason = unavailable, OutOfScopeReason = outOfScope, FixTarget = fix,
            };

        /// <summary>ダッシュボードの杭の行は、開き先を持つ検定できない項目を拾う (検定不能を先に)。</summary>
        [TestMethod]
        public void ThePileRowPicksTheFixableItem_UnavailableFirst()
        {
            var outOfScope = Item(1, outOfScope: "工法の適用範囲外", fix: DiagnosticTarget.PileBody(1));
            var unavailable = Item(1, unavailable: "N-M 曲線を作れませんでした", fix: DiagnosticTarget.PileBody(1));
            var rerun = Item(1, unavailable: "解析結果がありません");   // 開き先なし
            var judged = Item(1);
            var summary = PileEvaluationSummary.FromResults(new EvaluationResult([judged, outOfScope, rerun, unavailable]), null,
                new EvaluationResult([]), "A");
            Assert.AreSame(unavailable, summary.ByPile[1].FirstFixable, "検定不能 (開き先あり) を先に拾っていない");

            var onlyRerun = PileEvaluationSummary.FromResults(new EvaluationResult([judged, rerun]), null, new EvaluationResult([]), "A");
            Assert.IsNull(onlyRerun.ByPile[1].FirstFixable, "開き先の無い項目を拾っている (ボタンを押しても開く画面が無い)");

            var row = ResultDashboardView.ToRow(summary.ByPile[1]);
            Assert.AreSame(unavailable, row.Fixable, "一覧の行に開き先の項目が渡っていない");
        }

        /// <summary>説明の文は、状態・項目・理由と、開く入力画面の名前・対象を書く。</summary>
        [TestMethod]
        public void TheDescriptionNamesTheDestination()
        {
            string text = ResultDashboardView.DescribeFixable(Item(3, unavailable: "N-M 曲線を作れませんでした", fix: DiagnosticTarget.PileBody(2)));
            StringAssert.Contains(text, "検定不能");
            StringAssert.Contains(text, "N-M 曲線を作れませんでした");
            StringAssert.Contains(text, DiagnosticSelection.DestinationName(InputDestination.PileBodyWindow));
            StringAssert.Contains(text, DiagnosticTarget.PileBody(2).Label);

            string pile = ResultDashboardView.DescribeFixable(Item(3, unavailable: "軸力が曲線の外", fix: DiagnosticTarget.Pile(3, 2)));
            StringAssert.Contains(pile, DiagnosticSelection.DestinationName(InputDestination.PileLayoutEdit));
        }
    }
}
