using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 旧形式の群杭沈下データ (入力の中の複製だけが残っているファイル) の移行。
    ///
    /// <para>1. 複製には収束状態も解析条件も残っていない。以前は復元のときに「収束」と書いており、
    /// 計算して収束した結果と見分けがつかなかった。いまは「不明」として持ち、沈下の検定は検定不能にする。</para>
    /// <para>2. 杭ごとの沈下量を杭番号で引く形にするとき、同じ杭番号が複数の杭にあると
    /// <c>ToDictionary</c> が例外を投げ、ファイル全体が開けなかった。いまは重なった番号の値だけ移さずに知らせる。</para>
    /// </summary>
    [TestClass]
    public class LegacySettlementMigrationTests
    {
        private static readonly JsonSerializerOptions Options = new() { ReferenceHandler = ReferenceHandler.Preserve };

        /// <summary>複製 (コンタ) だけを持つ旧形式の群杭沈下の入力。</summary>
        private static PileGroupSettlement LegacyGroupSettlement() =>
            JsonSerializer.Deserialize<PileGroupSettlement>("""
                {
                  "LoadingType": "個別矩形",
                  "SettlementGridData": [
                    { "X": 0, "Y": 0, "Settlement": 1.0 },
                    { "X": 1, "Y": 0, "Settlement": 2.0 }
                  ]
                }
                """, Options)!;

        /// <summary>旧形式の杭配置 (杭ごとの沈下量は "GroupPileSettlement" に入っていた)。</summary>
        private static List<PileLayoutDataItem> LegacyPiles(params (int PileNo, double Settlement_mm)[] piles)
            => piles.Select(p => JsonSerializer.Deserialize<PileLayoutDataItem>(
                   $$"""{ "PileNo": {{p.PileNo}}, "GroupPileSettlement": {{p.Settlement_mm}} }""", Options)!).ToList();

        // ── 1. 収束状態 ────────────────────────────────

        /// <summary><b>本題 1。</b> 複製から復元した結果は「収束」ではなく「不明」になり、知らせが出ること。</summary>
        [TestMethod]
        public void RestoredFromTheMirror_ConvergenceIsUnknownNotConverged()
        {
            var pgs = LegacyGroupSettlement();
            var notices = LegacySettlementMigration.Apply(pgs, LegacyPiles((1, 3.0), (2, 5.0)));

            var rec = pgs.CaseRecords.Single();
            Assert.IsTrue(rec.IsConvergenceUnknown, "収束状態が「不明」になっていない");
            Assert.IsFalse(rec.IsConverged, "確かめられない収束を「収束」と書いている");
            Assert.AreEqual("不明", rec.ConvergenceLabel);
            Assert.AreEqual(2, rec.SettlementGridData.Count, "コンタが移っていない");
            Assert.AreEqual(5.0, pgs.SettlementOf(2), 1e-12, "杭ごとの沈下量が移っていない");
            Assert.IsTrue(notices.Any(n => n.Contains("不明")), "復元したことを知らせていない");
        }

        /// <summary>計算して得た結果 (新しい形式の記録) には「不明」を付けず、知らせも出さないこと。</summary>
        [TestMethod]
        public void RecordsFromTheCurrentFormat_AreNotMarkedUnknown()
        {
            var pgs = new PileGroupSettlement();
            pgs.CaseRecords = [new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = "任意矩形", IsConverged = true }];

            var notices = LegacySettlementMigration.Apply(pgs);

            Assert.IsFalse(pgs.CaseRecords[0].IsConvergenceUnknown);
            Assert.AreEqual("収束", pgs.CaseRecords[0].ConvergenceLabel);
            Assert.AreEqual(0, notices.Count, "移行していないのに知らせが出ている");
        }

        /// <summary>「不明」は保存・読込で残ること (開き直すと「収束」に戻る、では意味が無い)。</summary>
        [TestMethod]
        public void UnknownConvergence_SurvivesSaveAndLoad()
        {
            var pgs = LegacyGroupSettlement();
            LegacySettlementMigration.Apply(pgs, LegacyPiles((1, 3.0)));

            string json = JsonSerializer.Serialize(pgs.Result, Options);
            var loaded = JsonSerializer.Deserialize<GroupSettlementResult>(json, Options)!;

            Assert.IsTrue(loaded.CaseRecords.Single().IsConvergenceUnknown);
            Assert.IsFalse(loaded.CaseRecords.Single().IsConverged);
        }

        // ── 1. 検定 ──────────────────────────────────

        /// <summary>杭 2 本・単杭沈下の結果ありの入力に、旧形式から復元した群杭沈下を結び付ける。</summary>
        private static InputModel BuildWithRestoredGroup()
        {
            var input = new InputModel();
            input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
            input.FundamentalInput ??= new FundamentalInput();
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 1, No = 1, PileBodyNo = 1, SinglePileSettlementVL = 0.010 });
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 2, No = 2, PileBodyNo = 1, SinglePileSettlementVL = 0.012, X = 5 });

            var soilPile = new SoilPile();
            soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
            soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, D0s = 5 });
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles = [soilPile];
            foreach (var p in input.PileLayoutItems) p.SoilPileAltNo = 1;

            var pgs = LegacyGroupSettlement();
            LegacySettlementMigration.Apply(pgs, LegacyPiles((1, 3.0), (2, 5.0)));
            input.PileGroupSettlement = pgs;
            return input;
        }

        /// <summary>
        /// 収束状態が分からない結果で、沈下量を OK / NG と判定しないこと (検定不能とし、理由を示す)。
        /// 許容値を大きく取っても OK にならないことで見る。
        /// </summary>
        [TestMethod]
        public void PileSettlement_WithUnknownGroupResult_IsNotJudged()
        {
            var input = BuildWithRestoredGroup();
            input.FundamentalInput.EvaluateSettlement = true;
            input.FundamentalInput.AllowableSettlement_mm = 1000.0;

            var items = PileSettlementEvaluator.Evaluate(input);

            Assert.AreEqual(2, items.Count);
            foreach (var item in items)
            {
                Assert.IsFalse(item.IsJudged, $"{item.TargetName}: 収束状態が不明な結果で判定している");
                StringAssert.Contains(item.UnavailableReason, "不明");
            }
        }

        /// <summary>杭頭変形角も同じ。収束状態が分からないケースは判定しない。</summary>
        [TestMethod]
        public void DeformationAngle_WithUnknownGroupResult_IsNotJudged()
        {
            var input = BuildWithRestoredGroup();

            var items = SettlementDeformationAngleEvaluator.Evaluate(input);

            Assert.AreEqual(1, items.Count);
            Assert.IsFalse(items[0].IsJudged, "収束状態が不明な結果で変形角を判定している");
            StringAssert.Contains(items[0].UnavailableReason, "不明");
        }

        // ── 2. 杭番号の重なり ───────────────────────────

        /// <summary>
        /// <b>本題 2。</b> 同じ杭番号が複数の杭にあっても例外にならず、重なった番号の値だけ移さずに知らせること。
        /// ほかの杭の沈下量とコンタは移す。
        /// </summary>
        [TestMethod]
        public void DuplicatePileNos_DoNotThrow_AndKeepTheRest()
        {
            var pgs = LegacyGroupSettlement();
            var piles = LegacyPiles((1, 3.0), (2, 5.0), (2, 7.0), (3, 0.0), (4, 9.0));

            var notices = LegacySettlementMigration.Apply(pgs, piles);

            var rec = pgs.CaseRecords.Single();
            CollectionAssert.AreEquivalent(new[] { 1, 4 }, rec.PileSettlements_mm.Keys.ToArray(),
                "重なった番号の値を移している、またはほかの杭の値を落としている");
            Assert.AreEqual(3.0, rec.PileSettlements_mm[1], 1e-12);
            Assert.AreEqual(9.0, rec.PileSettlements_mm[4], 1e-12);
            Assert.AreEqual(2, rec.SettlementGridData.Count, "コンタが移っていない");

            string? dup = notices.FirstOrDefault(n => n.Contains("杭番号"));
            Assert.IsNotNull(dup, "重なった杭番号を知らせていない");
            StringAssert.Contains(dup, "No.2 (2 本)");

        }

        /// <summary>重なった番号の杭がどれも沈下量 0 なら、移すものが無いので知らせない。</summary>
        [TestMethod]
        public void DuplicatePileNos_WithoutValues_AreNotReported()
        {
            var pgs = LegacyGroupSettlement();
            var notices = LegacySettlementMigration.Apply(pgs, LegacyPiles((1, 3.0), (2, 0.0), (2, 0.0)));

            Assert.IsFalse(notices.Any(n => n.Contains("杭番号")), "移すものが無いのに重なりを知らせている");
            Assert.AreEqual(3.0, pgs.SettlementOf(1), 1e-12);
        }

        /// <summary>
        /// 読込の順序: 杭番号を並び順に振り直してから移すこと。振り直す前に移すと、重なった番号の値が決められず、
        /// ずれた番号の杭に別の杭の値が付く (No.1, 2, 2, 3 の 4 本目は振り直すと No.4 になり、No.3 の値は 3 本目の杭に付く)。
        /// </summary>
        [TestMethod]
        public void Load_RenumbersPilesBeforeMigratingTheSettlements()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1/ViewModels/MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");

            int renumber = body.IndexOf("UpdatePileLayoutNo();");
            int migrate = body.IndexOf("LegacySettlementMigration.AttachResultAndMigrate(");
            Assert.IsTrue(renumber >= 0 && migrate >= 0, "読込の手順が見つからない (テストの前提が崩れている)");
            Assert.IsTrue(renumber < migrate, "杭番号を振り直す前に群杭沈下の結果を移している");
        }

        /// <summary>読込で杭番号の重なりを知らせる文面。重なりが無ければ出さない。</summary>
        [TestMethod]
        public void DescribeDuplicatePileNos_ListsOnlyTheDuplicates()
        {
            Assert.IsNull(PileDesign.ViewModels.MainWindowViewModel.DescribeDuplicatePileNos(LegacyPiles((1, 0), (2, 0))));

            string? text = PileDesign.ViewModels.MainWindowViewModel.DescribeDuplicatePileNos(
                LegacyPiles((1, 0), (2, 0), (2, 0), (3, 0), (3, 0), (3, 0)));
            Assert.IsNotNull(text);
            StringAssert.Contains(text, "No.2 (2 本)");
            StringAssert.Contains(text, "No.3 (3 本)");
            Assert.IsFalse(text!.Contains("No.1 "), "重なっていない番号まで並べている");
        }
    }
}
