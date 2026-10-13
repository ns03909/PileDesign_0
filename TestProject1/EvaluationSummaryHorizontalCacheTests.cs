using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 検定サマリーの水平解析の部分を、入力の編集のたびに組み直さないこと。
    ///
    /// 以前はまとめ全体を入力の編集番号を含む 1 つの鍵で控えていたので、色分けを出している間は
    /// 入力を 1 か所編集するたびに、キャンバスの再描画で水平解析の検定 (低減後・低減前) を UI スレッドで
    /// 組み直していた (計算例 10 で 1 回あたり約 1.2 秒)。水平解析の検定は解析結果と解析時の入力の控えだけを読むので、
    /// 控えがある間は使い回し、編集で変わる鉛直系 (支持力・沈下量・傾斜角) だけを組み直す。
    /// 控えが無い (編集中の入力を読む) ときは従来どおり編集のたびに組み直す。
    /// </summary>
    [TestClass]
    public class EvaluationSummaryHorizontalCacheTests
    {
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

        /// <summary>控えたまとめが、その場で組み直したまとめと同じ中身であること。</summary>
        private static void AssertSameAsFresh(MainWindowViewModel vm, PileEvaluationSummary cached, string when)
        {
            var fresh = PileEvaluationSummary.Build(vm);
            Assert.AreEqual(fresh.HasHorizontal, cached.HasHorizontal, $"{when}: 水平解析の検定の有無");
            Assert.AreEqual(fresh.Horizontal?.Items.Count, cached.Horizontal?.Items.Count, $"{when}: 水平解析の検定の件数");
            Assert.AreEqual(fresh.HorizontalUnfactored?.Items.Count, cached.HorizontalUnfactored?.Items.Count, $"{when}: 低減前の件数");
            Assert.AreEqual(fresh.Bearing.Items.Count, cached.Bearing.Items.Count, $"{when}: 鉛直系の件数");
            Assert.AreEqual(fresh.Failures.Count, cached.Failures.Count, $"{when}: 失敗の件数");
            CollectionAssert.AreEquivalent(fresh.ByPile.Keys.ToList(), cached.ByPile.Keys.ToList(), $"{when}: 杭の並び");
            foreach (var (pileNo, entry) in fresh.ByPile)
            {
                var c = cached.ByPile[pileNo];
                Assert.AreEqual(entry.Band, c.Band, $"{when}: 杭 {pileNo} の帯");
                if (double.IsNaN(entry.MaxRatio)) Assert.IsTrue(double.IsNaN(c.MaxRatio), $"{when}: 杭 {pileNo}");
                else Assert.AreEqual(entry.MaxRatio, c.MaxRatio, 0.0, $"{when}: 杭 {pileNo} の最大検定比");
            }
        }

        /// <summary>
        /// 控え (解析時の入力の写し) があるときは、入力を編集しても水平解析の検定を組み直さない。
        /// 鉛直系は組み直す。解析結果・控え・材料のオプションが変われば組み直す。
        /// </summary>
        [TestMethod]
        public void WithResultSnapshot_InputEditReusesHorizontalAndRebuildsVertical()
        {
            var vm = RunExample9();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.CaptureAnalysisResultSet();
            Assert.IsNotNull(vm.CurrentResultSet, "解析結果の控えを作れていない (前提)");

            var first = vm.GetEvaluationSummary();
            Assert.IsTrue(first.HasHorizontal, "水平解析の検定が組めていない (前提)");
            int built = vm.HorizontalEvaluationBuildCount;
            Assert.AreEqual(1, built, "最初の 1 回で水平解析の検定を組んでいない");

            // 編集中の入力の軸力を変える (解析時の入力の控えは変わらない)
            var pile = vm.CurrentInputModel!.PileLayoutItems.First();
            pile.AxialForceVL0 = pile.AxialForceVL0 * 1.5 + 100.0;
            vm.MarkPossiblyEdited();

            var afterEdit = vm.GetEvaluationSummary();
            Assert.AreEqual(built, vm.HorizontalEvaluationBuildCount,
                "入力を編集しただけで水平解析の検定を組み直しています (控えの入力は変わっていない)");
            Assert.AreSame(first.Horizontal, afterEdit.Horizontal, "水平解析の検定を使い回していません");
            Assert.AreSame(first.HorizontalUnfactored, afterEdit.HorizontalUnfactored, "低減前の検定を使い回していません");
            Assert.AreNotSame(first, afterEdit, "入力の編集でまとめを作り直していません (鉛直系は編集で変わりうる)");
            Assert.AreNotSame(first.Bearing, afterEdit.Bearing, "入力の編集で鉛直系の検定を組み直していません");
            AssertSameAsFresh(vm, afterEdit, "入力の編集のあと");

            // 編集が続いても同じ
            vm.MarkPossiblyEdited();
            vm.GetEvaluationSummary();
            vm.MarkPossiblyEdited();
            AssertSameAsFresh(vm, vm.GetEvaluationSummary(), "編集を重ねたあと");
            Assert.AreEqual(built, vm.HorizontalEvaluationBuildCount, "編集を重ねると水平解析の検定を組み直しています");

            // 材料のオプションが変わると耐力が変わるので組み直す (戻すときも)
            bool original = ConcreteModelOptions.IgnoreTensileStrength;
            try
            {
                ConcreteModelOptions.IgnoreTensileStrength = !original;
                vm.GetEvaluationSummary();
                Assert.AreEqual(built + 1, vm.HorizontalEvaluationBuildCount, "材料のオプションを変えても組み直していません");
            }
            finally
            {
                ConcreteModelOptions.IgnoreTensileStrength = original;
            }
            vm.GetEvaluationSummary();
            Assert.AreEqual(built + 2, vm.HorizontalEvaluationBuildCount, "材料のオプションを戻しても組み直していません");
            built = vm.HorizontalEvaluationBuildCount;

            // 解析結果を取り直す (別の控え・別の解析モデル) と組み直す
            var beforeRecapture = vm.GetEvaluationSummary();
            vm.CaptureAnalysisResultSet();
            var afterRecapture = vm.GetEvaluationSummary();
            Assert.AreEqual(built + 1, vm.HorizontalEvaluationBuildCount, "解析結果の控えが替わっても組み直していません");
            Assert.AreNotSame(beforeRecapture.Horizontal, afterRecapture.Horizontal);
            AssertSameAsFresh(vm, afterRecapture, "控えを取り直したあと");

            // ダッシュボードの「再計算」(force) は必ず組み直す
            vm.GetEvaluationSummary(force: true);
            Assert.AreEqual(built + 2, vm.HorizontalEvaluationBuildCount, "force で組み直していません");
        }

        /// <summary>
        /// 控えが無いとき (控えを持たない古いファイル・控えの失敗) は、水平解析の検定が編集中の入力を読むので、
        /// 編集のたびに組み直す (使い回すと編集前の入力で組んだ検定が残る)。
        /// </summary>
        [TestMethod]
        public void WithoutResultSnapshot_InputEditRebuildsHorizontal()
        {
            var vm = RunExample9();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            Assert.IsNull(vm.CurrentResultSet, "ヘッドレスの実行は控えを持たない (前提)");
            Assert.AreSame(vm.CurrentInputModel, vm.ResultInputModel, "検定は編集中の入力を読む (前提)");

            var first = vm.GetEvaluationSummary();
            int built = vm.HorizontalEvaluationBuildCount;

            var pile = vm.CurrentInputModel!.PileLayoutItems.First();
            pile.AxialForceVL0 = pile.AxialForceVL0 * 1.5 + 100.0;
            vm.MarkPossiblyEdited();

            var afterEdit = vm.GetEvaluationSummary();
            Assert.AreEqual(built + 1, vm.HorizontalEvaluationBuildCount,
                "控えが無い (編集中の入力を読む) のに、編集のあと水平解析の検定を使い回しています");
            Assert.AreNotSame(first.Horizontal, afterEdit.Horizontal);
            AssertSameAsFresh(vm, afterEdit, "控えなしで編集したあと");

            // 編集が無ければ再描画では組み直さない
            Assert.AreSame(afterEdit, vm.GetEvaluationSummary());
            Assert.AreEqual(built + 1, vm.HorizontalEvaluationBuildCount);
        }

        /// <summary>まとめを作り直す所が、水平解析の検定の控えを通すこと (直接 Build(this) に戻さない)。</summary>
        [TestMethod]
        public void GetEvaluationSummary_GoesThroughTheHorizontalCache()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.EvaluationColoring.cs"),
                "public PileEvaluationSummary GetEvaluationSummary(bool force = false)");
            StringAssert.Contains(body, "GetHorizontalEvaluation(force)");
            Assert.IsFalse(body.Contains("PileEvaluationSummary.Build(this)", StringComparison.Ordinal),
                "まとめを丸ごと組み直しています (入力の編集のたびに水平解析の検定を組み直す)");
        }
    }
}
