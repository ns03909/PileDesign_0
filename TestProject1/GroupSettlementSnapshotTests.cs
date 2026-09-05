using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 矩形荷重 (<c>PileGroupSettlement.RectLoads</c>) は<b>利用者の入力</b>であり、
    /// 解析結果で書き換えないこと。
    ///
    /// 以前は反復解析 (基礎梁考慮) の収束荷重を入力へ書き戻しており、
    /// 元へ戻すための退避フィールド <c>NonBeamRectLoadsSnapshot</c> と、
    /// 「一般モードへ切替えたら復元する」処理が要る状態だった。
    /// 上書きをやめたので、退避も復元も不要になっている。
    /// 収束後の荷重を見せたい場面は <c>ActiveRectLoads</c> (= 表示中のケースの荷重) を読む。

    /// </summary>
    [TestClass]
    public class GroupSettlementSnapshotTests
    {
        private static ObservableCollection<RectLoad> MakeRectLoads(params (double qa, int linkedPileNo)[] specs)
        {
            var col = new ObservableCollection<RectLoad>();
            int idx = 0;
            foreach (var (qa, linkedPileNo) in specs)
            {
                col.Add(new RectLoad
                {
                    X1 = idx, X2 = idx + 1, Y1 = 0, Y2 = 1,
                    QA = qa,
                    LinkedPileNo = linkedPileNo,
                });
                idx++;
            }
            return col;
        }

        private static GroupSettlementExampleData MakeExampleData()
        {
            return new GroupSettlementExampleData
            {
                LoadingPlaneAltitude = -3.0,
                LoadingPlaneAltitudeNonBeam = -3.0,
                LoadingPlaneAltitudeBeamAware = -3.0,
                RectLoads =
                [
                    new RectLoadDto { X1 = -1.25, X2 = 1.25, Y1 = -1.25, Y2 = 1.25, QA = 500, LinkedPileNo = 1 },
                    new RectLoadDto { X1 = 5.30, X2 = 8.70, Y1 = -1.50, Y2 = 1.50, QA = 816, LinkedPileNo = 2 },
                    new RectLoadDto { X1 = 5.00, X2 = 9.00, Y1 = 7.00, Y2 = 11.00, QA = 1280, LinkedPileNo = 6 },
                ],
                SettlementSoilLayers =
                [
                    new SettlementSoilLayerDto { BottomAltitude = -12.0, Ek = 28000, PoissonsRatio = 0.33, Thickness = 9.0, GranularityClass = "砂質土" },
                ],
            };
        }

        // ── 入力は結果で書き換わらない ──────────────────────

        /// <summary>例題を読み込んだら、矩形荷重は例題の原値であること。</summary>
        [TestMethod]
        public void ExampleLoad_KeepsTheOriginalLoads()
        {
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };

            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, MakeExampleData());

            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                input.PileGroupSettlement.RectLoads.Select(r => r.QA).ToList());
        }

        /// <summary>
        /// 反復解析のケースを表示に切り替えても、入力の矩形荷重が収束荷重で
        /// 書き換わらないこと。<b>退避も復元も要らない</b>のはこれが成り立つため。
        /// </summary>
        [TestMethod]
        public void ShowingAnIteratedCase_LeavesTheInputLoadsAlone()
        {
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, MakeExampleData());
            var pgs = input.PileGroupSettlement;

            var converged = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "個別矩形（基礎梁考慮）",
                IsBeamAware = true,
                RectLoads = MakeRectLoads((586, 1), (825, 2), (1097, 6)),
            };
            pgs.CaseRecords = [converged];
            pgs.ActiveCaseIndex = 0;

            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(pgs, converged);

            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                pgs.RectLoads.Select(r => r.QA).ToList(),
                "入力の矩形荷重が収束荷重で上書きされている");

            CollectionAssert.AreEqual(
                new[] { 586.0, 825.0, 1097.0 },
                pgs.ActiveRectLoads.Select(r => r.QA).ToList(),
                "収束後の荷重はケースから引けること");
        }

        // ── ApplyActiveCaseToLegacyFields: ケース切替の独立性 ──

        /// <summary>
        /// ケース切替でコンタの複製へ書き戻さないこと。
        ///
        /// 書き戻すと保存ファイルに複製が復活し、ケース側の要素が <c>$ref</c> になる。
        /// 表示・計算書はケース記録 (<c>ActiveSettlementGridData</c>) から読む。
        /// </summary>
        [TestMethod]
        public void ApplyActiveCaseToLegacyFields_DoesNotWriteBackTheGridMirror()
        {
            var pgs = new PileGroupSettlement();
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                IsBeamAware = true,
                SettlementGridData =
                [
                    new SettlementGridDataItem { X = 0, Y = 0, Settlement = 5.0 },
                    new SettlementGridDataItem { X = 1, Y = 0, Settlement = 8.0 },
                    new SettlementGridDataItem { X = 0, Y = 1, Settlement = 11.0 },
                ],
            };
            pgs.CaseRecords = [record];
            pgs.ActiveCaseIndex = 0;

            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(pgs, record);

            Assert.AreEqual(0, pgs.LegacySettlementGridData?.Count ?? 0,
                "コンタの複製へ書き戻しています");

            // 表示はケースから引ける
            CollectionAssert.AreEqual(
                new[] { 5.0, 8.0, 11.0 },
                pgs.ActiveSettlementGridData.Select(g => g.Settlement).ToList());
        }

        /// <summary>杭ごとの沈下量の複製は、まだ同期していること。</summary>
        [TestMethod]
        public void ApplyActiveCaseToLegacyFields_StillSyncsPileSettlements()
        {
            // 杭ごとの沈下量は<b>結果から引く</b>。同期 (コピー) はもう行わないので、
            // 見るのは「表示中のケースの値が読めること」と「切替が表示に伝わること」。
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                PileSettlements_mm = new Dictionary<int, double> { [1] = 4.25 },
            };
            var pile = new PileLayoutDataItem { PileNo = 1 };
            var input = new InputModel { PileLayoutItems = [pile] };
            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords.Add(record);
            input.PileGroupSettlement.ActiveCaseIndex = 0;
            input.SetPileOwnersForTest();

            var raised = new List<string>();
            pile.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(
                input.PileGroupSettlement, record, [pile]);

            Assert.AreEqual(4.25, pile.GroupPileSettlement, 1e-12,
                "表示中のケースの沈下量が読めていない");
            CollectionAssert.Contains(raised, nameof(PileLayoutDataItem.GroupPileSettlement),
                "ケースを切り替えても沈下量の表示更新が通知されない");
        }

        [TestMethod]
        public void ApplyActiveCaseToLegacyFields_NullSafe()
        {
            // 防御的: null 引数で例外しない
            var pgs = new PileGroupSettlement();
            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(null, null);
            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(pgs, null);
            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(null, new GroupSettlementCaseRecord());
            // 例外がスローされなければ OK
        }

        // ── CaseRecords クリア (例題ロード時の旧記録混入防止) ──
        // 注: ApplyToInputModel 経由のテストは MainWindowViewModel が必要なため、ここでは
        // model レベルで「クリア後に追加された CaseRecord が IsBeamAware で正しくフィルタされる」
        // ことだけを保証する。

        [TestMethod]
        public void CaseRecords_FilterByIsBeamAware()
        {
            var pgs = new PileGroupSettlement();
            pgs.CaseRecords.Add(new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = "任意矩形", IsBeamAware = false });
            pgs.CaseRecords.Add(new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true });

            Assert.AreEqual(1, pgs.CaseRecords.Count(r => !r.IsBeamAware), "一般 (基礎梁無し) の CaseRecord は 1 件");
            Assert.AreEqual(1, pgs.CaseRecords.Count(r => r.IsBeamAware), "反復 (基礎梁考慮) の CaseRecord は 1 件");
        }
    }
}
