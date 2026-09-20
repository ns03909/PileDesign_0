using Microsoft.VisualStudio.TestTools.UnitTesting;
using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 入力を編集したとき、沈下の結果は<b>捨てずに「再解析が必要」の印を立てる</b>こと
    /// (2026-09-20、水平解析と同じ扱いに揃えた)。
    ///
    /// <para>以前は確認のうえ削除していた。理由は「結果が入力モデルの中にあって解析結果セットで
    /// 切り離せない / 古い値が入力系の表示に出る / 傾斜角検定に使われる」だったが、いずれも解消した。
    /// 結果は <see cref="GroupSettlementResult"/> が持ち、各杭の沈下量は結果から引く計算プロパティ、
    /// 傾斜角検定はスナップショットを読む。</para>
    ///
    /// <para><b>沈下の印は水平解析とは別に持つ。</b>沈下の結果は表示されるだけでなく次の解析の
    /// 入力でもある (単杭沈下の曲線を水平解析の杭先端 P-S ばねと基礎梁考慮沈下の杭頭ばねが読む)。
    /// 印を 1 つで兼ねると、水平解析をやり直した時点で沈下の陳腐化まで消え、入力変更前の曲線が
    /// 「最新」の顔で次の解析に入る。</para>
    /// </summary>
    [TestClass]
    public class SettlementStaleInsteadOfDiscardTests
    {
        /// <summary>沈下解析を終えた状態を作る (群杭の記録 + 単杭の曲線と節点別履歴)。</summary>
        private static (MainWindowViewModel vm, InputModel input, SoilPile soilPile) Build()
        {
            var input = new InputModel();
            input.ElementDivision ??= new ElementDivision();

            var sp = new SoilPile { GroundNo = 1, PileBodyNo = 1, Z = 0.0 };
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, DD0s = 0 });
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, DD0s = 5 });
            sp.NodeDisplacements = [Vector<double>.Build.Dense(4, 0.0), Vector<double>.Build.Dense(4, 0.001)];
            sp.NodeReactions = [Vector<double>.Build.Dense(4, 0.0), Vector<double>.Build.Dense(4, 100.0)];
            input.ElementDivision.SoilPiles.Add(sp);

            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords ??= [];
            input.PileGroupSettlement.CaseRecords.Add(new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "任意矩形",
                IsConverged = true,
                PileSettlements_mm = new Dictionary<int, double> { [1] = 7.5 },
            });
            input.PileGroupSettlement.ActiveCaseIndex = input.PileGroupSettlement.CaseRecords!.Count - 1;

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            vm.IsVerticalAnalysisDone = true;
            vm.IsGroupPileSettlementAnalysisDone = true;
            return (vm, input, sp);
        }

        /// <summary>
        /// 沈下解析を終えた直後は、陳腐化していないこと (印が最初から立っていない)。
        /// </summary>
        [TestMethod]
        public void FreshResultsAreNotStale()
        {
            var (vm, _, _) = Build();
            Assert.IsFalse(vm.SettlementResultsAreStale);
        }

        /// <summary>
        /// 入力を編集すると陳腐化の印が立ち、<b>結果そのものは残る</b>こと。
        /// </summary>
        [TestMethod]
        public void EditingTheInputMarksStaleButKeepsTheResults()
        {
            var (vm, input, sp) = Build();

            vm.MarkInputChangedSinceAnalysis();

            Assert.IsTrue(vm.SettlementResultsAreStale, "陳腐化の印が立っていない");
            Assert.AreEqual(1, input.PileGroupSettlement.CaseRecords!.Count, "群杭沈下の記録が消えている");
            Assert.AreEqual(7.5, input.PileGroupSettlement.SettlementOf(1), 1e-9, "各杭の沈下量が消えている");
            Assert.AreEqual(2, sp.LoadDisplacements.Count, "単杭沈下の曲線が消えている");
            Assert.AreEqual(2, sp.NodeDisplacements.Count, "節点別の履歴が消えている");
        }

        /// <summary>
        /// 沈下解析をやり直したら印が降りること。
        /// </summary>
        [TestMethod]
        public void RerunningTheSettlementAnalysisClearsTheMark()
        {
            var (vm, _, _) = Build();
            vm.MarkInputChangedSinceAnalysis();
            Assert.IsTrue(vm.SettlementResultsAreStale);

            vm.MarkSettlementResultsCurrent();

            Assert.IsFalse(vm.SettlementResultsAreStale, "沈下解析をやり直しても印が残っている");
        }

        /// <summary>
        /// <b>本題。</b> 水平解析をやり直しても、沈下の陳腐化は降りないこと。
        /// 印を 1 つで兼ねていると、ここで沈下も「最新」になってしまう
        /// (曲線は次の解析の入力なので、古いまま静かに使われる)。
        /// </summary>
        [TestMethod]
        public void CapturingANewResultSetDoesNotMakeTheSettlementLookCurrent()
        {
            var (vm, input, _) = Build();
            vm.MarkInputChangedSinceAnalysis();

            // 水平解析をやり直した = 結果セットを取り直した
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            Assert.IsFalse(vm.InputChangedSinceAnalysis, "前提: 水平解析側の印は降りる");
            Assert.IsTrue(vm.SettlementResultsAreStale,
                "水平解析をやり直しただけで沈下まで「最新」になっている");
        }

        /// <summary>
        /// 沈下の結果を持っていなければ、印は立たない (何も陳腐化していない)。
        /// </summary>
        [TestMethod]
        public void WithoutResultsNothingIsStale()
        {
            var input = new InputModel();
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            vm.MarkInputChangedSinceAnalysis();

            Assert.IsFalse(vm.SettlementResultsAreStale);
        }

        /// <summary>
        /// 陳腐化していなければ、古い結果を使う確認は出さずに通ること
        /// (解析のたびに尋ねられては困る)。
        /// </summary>
        [TestMethod]
        public void TheGuardIsSilentWhenTheResultsAreCurrent()
        {
            var (vm, _, _) = Build();

            Assert.IsTrue(vm.ConfirmUsingStaleSettlementResults("水平解析", "杭先端の P-S ばね"),
                "陳腐化していないのに尋ねている");
        }

        /// <summary>
        /// 状態表示は<b>実際に持っている解析</b>だけを名指しすること。
        ///
        /// 単杭沈下だけを実行して入力を編集したとき、水平解析を実行していないのに
        /// 「表示中の解析結果は … 実行時の入力によるものです（再解析が必要です）」と
        /// 出ていた (実機で確認、2026-09-20)。
        /// </summary>
        [TestMethod]
        public void TheStatusTextNamesOnlyTheAnalysesThatExist()
        {
            // 沈下だけ実行して入力を編集した状態
            string settlementOnly = MainWindowViewModel.BuildResultSetStatusText(
                "2026-09-20 14:50", horizontalStale: false, settlementStale: true, materialOptionsChanged: false);
            StringAssert.Contains(settlementOnly, "沈下解析の再実行が必要です");
            Assert.IsFalse(settlementOnly.Contains("表示中の解析結果は"),
                "水平解析の結果が表示されている前提の文が出ている");

            // 水平解析だけを持っている状態 (従来の文面)
            string horizontalOnly = MainWindowViewModel.BuildResultSetStatusText(
                "2026-09-20 14:50", horizontalStale: true, settlementStale: false, materialOptionsChanged: false);
            StringAssert.Contains(horizontalOnly, "表示中の解析結果は");
            Assert.IsFalse(horizontalOnly.Contains("沈下"), "沈下を実行していないのに名指ししている");

            // 両方が陳腐化したら両方を名指しする
            string both = MainWindowViewModel.BuildResultSetStatusText(
                "2026-09-20 14:50", horizontalStale: true, settlementStale: true, materialOptionsChanged: false);
            StringAssert.Contains(both, "水平解析と沈下解析の再実行が必要です");

            // どちらも最新なら余計なことを言わない
            string fresh = MainWindowViewModel.BuildResultSetStatusText(
                "2026-09-20 14:50", horizontalStale: false, settlementStale: false, materialOptionsChanged: false);
            Assert.AreEqual("解析結果: 2026-09-20 14:50 実行", fresh);

            // 材料オプションの注記は、どの組み合わせにも後ろから足す
            string withOptions = MainWindowViewModel.BuildResultSetStatusText(
                "2026-09-20 14:50", horizontalStale: false, settlementStale: true, materialOptionsChanged: true);
            StringAssert.Contains(withOptions, "沈下解析の再実行が必要です");
            StringAssert.Contains(withOptions, "材料モデル化オプション");
        }

        /// <summary>
        /// 明示的な破棄 (解析結果の削除) では、これまでどおり全部消えること。
        /// 「残して印を立てる」のは<b>入力編集のとき</b>だけ。
        /// </summary>
        [TestMethod]
        public void TheExplicitDiscardStillRemovesEverything()
        {
            var (vm, input, sp) = Build();

            vm.ClearSettlementResultsForTest();

            Assert.AreEqual(0, input.PileGroupSettlement.CaseRecords?.Count ?? 0);
            Assert.AreEqual(0, sp.LoadDisplacements.Count);
            Assert.AreEqual(0, sp.NodeDisplacements.Count);
            Assert.IsFalse(vm.HasSettlementResultsForTest());
        }
    }
}
