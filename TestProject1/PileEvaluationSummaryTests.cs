using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 検定サマリー (杭ごとの最大検定比) の畳み方。
    ///
    /// ダッシュボードの一覧とキャンバスの色分けはここから同じ値を受け取る。
    /// 畳み方を誤ると「NG の杭が緑に塗られる」のような、解析値は正しいのに
    /// 表示だけ嘘をつく不具合になるので、帯の境界と未収束の扱いを固定する。
    /// </summary>
    [TestClass]
    public class PileEvaluationSummaryTests
    {
        private static EvaluationItem Item(
            int? pileNo, double response, double limit, bool ok,
            StepStatus status = StepStatus.Converged,
            string caseName = "L2-1",
            EvaluationKind kind = EvaluationKind.PileSectionMoment,
            bool? liquefaction = false,
            int? segmentIndex = null)
            => new()
            {
                Kind = kind,
                Level = 2,
                Category = kind == EvaluationKind.PileBearingCompression ? "押込み支持力" : "杭体曲げ (安全限界)",
                PileBodyNo = 1,
                PileNo = pileNo,
                LoadCaseName = caseName,
                LoadCombinationName = "1.0/1.0/1.0",
                IsLiquefaction = liquefaction,
                SegmentIndex = segmentIndex,
                Response = response,
                Limit = limit,
                IsOk = ok,
                CaseConvergence = status,
                Unit = "kN·m",
            };

        private static EvaluationResult Empty => new([]);

        [TestMethod]
        public void BandOf_UsesTheDocumentedThresholds()
        {
            Assert.AreEqual(PileRatioBand.Safe, PileEvaluationSummary.BandOf(0.5, false, false));
            Assert.AreEqual(PileRatioBand.Safe, PileEvaluationSummary.BandOf(0.8, false, false), "0.8 ちょうどは余裕あり");
            Assert.AreEqual(PileRatioBand.Tight, PileEvaluationSummary.BandOf(0.81, false, false));
            Assert.AreEqual(PileRatioBand.Tight, PileEvaluationSummary.BandOf(1.0, false, false), "1.0 ちょうどは判定側が OK と言うので余裕小");
            Assert.AreEqual(PileRatioBand.Ng, PileEvaluationSummary.BandOf(1.01, false, false));

            // 判定は比から導かず、算出元の IsOk を優先する
            Assert.AreEqual(PileRatioBand.Ng, PileEvaluationSummary.BandOf(0.5, hasNg: true, hasUnconverged: false));
            Assert.AreEqual(PileRatioBand.Ng, PileEvaluationSummary.BandOf(0.5, hasNg: true, hasUnconverged: true), "NG は未収束より優先");
            Assert.AreEqual(PileRatioBand.Unconverged, PileEvaluationSummary.BandOf(0.5, false, hasUnconverged: true));
            Assert.AreEqual(PileRatioBand.Unconverged, PileEvaluationSummary.BandOf(double.NaN, false, hasUnconverged: true));
            Assert.AreEqual(PileRatioBand.None, PileEvaluationSummary.BandOf(double.NaN, false, false));
        }

        [TestMethod]
        public void FromResults_FoldsToTheWorstItemPerPile()
        {
            var horizontal = new EvaluationResult(
            [
                Item(1, 50, 100, ok: true),                       // 0.50
                Item(1, 90, 100, ok: true, caseName: "L2-2"),     // 0.90 ← 支配
                Item(2, 120, 100, ok: false),                     // 1.20 NG
                Item(3, 30, 100, ok: true),                       // 0.30
                Item(3, 500, 100, ok: false, status: StepStatus.Unconverged, caseName: "L2-3"), // 未収束 (比は無視)
                Item(null, 999, 100, ok: false),                  // 杭を特定できない行 → 一覧には出ないが件数には入る
            ]);

            var summary = PileEvaluationSummary.FromResults(horizontal, null, Empty, "A");

            Assert.AreEqual(3, summary.ByPile.Count, "杭を特定できる行だけを畳む");
            Assert.AreEqual(2, summary.Horizontal!.NgCount, "杭を特定できない NG も件数には入る");

            var p1 = summary.ByPile[1];
            Assert.AreEqual(0.90, p1.MaxRatio, 1e-12);
            Assert.AreEqual("L2-2", p1.Governing!.LoadCaseName);
            Assert.AreEqual(PileRatioBand.Tight, p1.Band);
            Assert.AreEqual("OK", p1.StatusLabel);

            var p2 = summary.ByPile[2];
            Assert.AreEqual(PileRatioBand.Ng, p2.Band);
            Assert.IsTrue(p2.HasNg);
            Assert.AreEqual("NG", p2.StatusLabel);

            var p3 = summary.ByPile[3];
            Assert.AreEqual(0.30, p3.MaxRatio, 1e-12, "未収束の行の比は最大値に使わない");
            Assert.AreEqual(PileRatioBand.Unconverged, p3.Band);
            Assert.IsTrue(p3.HasUnconverged);
            Assert.AreEqual("未収束", p3.StatusLabel);

            // 一覧は NG を先頭に、以降は検定比の降順
            CollectionAssert.AreEqual(new[] { 2, 1, 3 }, summary.ByRatioDescending.Select(e => e.PileNo).ToArray());

            CollectionAssert.AreEqual(new[] { "L2-3 1.0/1.0/1.0（液状化無）" }, summary.UnconvergedCaseNames.ToArray());
        }

        /// <summary>
        /// 要素ごとの畳み方。キャンバスは分割した要素を 1 つずつこの帯で塗るので、
        /// 「杭頭回転角だけが NG」の杭で杭体の要素まで NG 色にしてはいけない。
        /// </summary>
        [TestMethod]
        public void FromResults_FoldsPerElement_AndKeepsPileHeadChecksSeparate()
        {
            var horizontal = new EvaluationResult(
            [
                // 杭 1: 要素 0 は余裕あり、要素 3 が NG
                Item(1, 50, 100, ok: true, segmentIndex: 0),
                Item(1, 85, 100, ok: true, segmentIndex: 0, caseName: "L2-2"),   // 0.85 ← 要素 0 の支配
                Item(1, 130, 100, ok: false, segmentIndex: 3),                   // 1.30 NG
                // 杭 1: 部位を持たない検定 (杭頭回転角) が NG
                Item(1, 17, 10, ok: false, kind: EvaluationKind.PileHeadRotation),
            ]);

            var summary = PileEvaluationSummary.FromResults(horizontal, null, Empty, "A");

            var e0 = summary.ByPileElement[(1, 0)];
            Assert.AreEqual(0, e0.ElementIndex);
            Assert.AreEqual(0.85, e0.MaxRatio, 1e-12);
            Assert.AreEqual(PileRatioBand.Tight, e0.Band, "余裕の少ない要素まで NG 色にしない");

            var e3 = summary.ByPileElement[(1, 3)];
            Assert.AreEqual(PileRatioBand.Ng, e3.Band);

            Assert.IsFalse(summary.ByPileElement.ContainsKey((1, 1)), "検定の無い要素は色を持たない");

            // 杭頭の印は部位を持たない検定だけ。杭体の要素の比は混ざらない
            var head = summary.ByPileHead[1];
            Assert.AreEqual(PileRatioBand.Ng, head.Band);
            Assert.AreEqual(EvaluationKind.PileHeadRotation, head.Governing!.Kind);
            Assert.IsNull(head.ElementIndex);

            // 杭 1 本ぶんの要約 (一覧・件数用) は従来どおり全項目の最悪
            Assert.AreEqual(1.7, summary.ByPile[1].MaxRatio, 1e-12);
        }

        [TestMethod]
        public void FromResults_BearingItemsJoinTheSamePile()
        {
            var horizontal = new EvaluationResult([Item(1, 50, 100, ok: true)]);
            var bearing = new EvaluationResult([Item(1, 95, 100, ok: true, kind: EvaluationKind.PileBearingCompression, liquefaction: null)]);

            var summary = PileEvaluationSummary.FromResults(horizontal, null, bearing, "A");

            var p1 = summary.ByPile[1];
            Assert.AreEqual(0.95, p1.MaxRatio, 1e-12);
            Assert.AreEqual(EvaluationKind.PileBearingCompression, p1.Governing!.Kind, "支持力の方が厳しければそれが支配");
            Assert.AreEqual(PileRatioBand.Tight, p1.Band);
            Assert.IsTrue(summary.HasBearing);
        }

        [TestMethod]
        public void FromResults_PileWithOnlyUnconvergedRows_HasNoRatioButIsUnconverged()
        {
            var horizontal = new EvaluationResult([Item(7, 500, 100, ok: false, status: StepStatus.Unconverged)]);
            var summary = PileEvaluationSummary.FromResults(horizontal, null, Empty, "A");

            var p7 = summary.ByPile[7];
            Assert.IsTrue(double.IsNaN(p7.MaxRatio));
            Assert.IsNull(p7.Governing);
            Assert.AreEqual(PileRatioBand.Unconverged, p7.Band);
        }

        [TestMethod]
        public void Empty_WhenNothingWasAnalysed()
        {
            var summary = PileEvaluationSummary.FromResults(null, null, Empty, "A");
            Assert.IsTrue(summary.IsEmpty);
            Assert.AreEqual(0, summary.ByPile.Count);
            Assert.AreEqual(0, summary.UnconvergedCaseNames.Count);
        }

        // ── 実際に例題を解いた結果と突き合わせる ──

        private static MainWindowViewModel? RunExample9()
        {
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4,
                Level2Steps = 8,
                LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                UseLineSearch = true,
                Parallelism = 1,
            };

            try
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", options);
                vm?.ApplyConcreteModelOptions();
                return vm;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                return null;
            }
        }

        [TestMethod]
        public void Example9_SummaryAgreesWithTheEvaluationRows()
        {
            var vm = RunExample9();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var summary = vm.GetEvaluationSummary(force: true);
            Assert.IsTrue(summary.HasHorizontal, "水平解析の検定が組めていない");
            Assert.IsTrue(summary.ByPile.Count > 0, "杭ごとの行が 1 本も無い");

            var all = summary.Horizontal!.Items.Concat(summary.Bearing.Items).ToList();
            foreach (var entry in summary.ByPile.Values)
            {
                var rows = all.Where(i => i.PileNo == entry.PileNo).ToList();
                var converged = rows.Where(i => !i.IsFromUnconvergedCase).ToList();

                double expectedMax = converged.Count == 0 ? double.NaN : converged.Max(i => i.Ratio);
                if (double.IsNaN(expectedMax)) Assert.IsTrue(double.IsNaN(entry.MaxRatio));
                else Assert.AreEqual(expectedMax, entry.MaxRatio, 1e-12, $"杭 {entry.PileNo}");

                Assert.AreEqual(converged.Any(i => !i.IsOk), entry.HasNg, $"杭 {entry.PileNo} の NG");
                Assert.AreEqual(rows.Any(i => i.IsFromUnconvergedCase), entry.HasUnconverged, $"杭 {entry.PileNo} の未収束");
                Assert.AreEqual(PileEvaluationSummary.BandOf(entry.MaxRatio, entry.HasNg, entry.HasUnconverged), entry.Band);
            }

            // 解析結果と入力が変わらない間は同じインスタンスを返す (キャンバスの再描画ごとに検定し直さない)
            Assert.AreSame(summary, vm.GetEvaluationSummary());
            Assert.AreNotSame(summary, vm.GetEvaluationSummary(force: true));

            // 入力の編集で作り直す (支持力の検定は入力だけから決まる)。
            // ヘッドレスの例題は DeepCopy できないので、Undo を積まずに編集済みの印だけ付ける経路で確かめる
            var cached = vm.GetEvaluationSummary();
            vm.MarkPossiblyEdited();
            Assert.AreNotSame(cached, vm.GetEvaluationSummary());
        }
    }
}
