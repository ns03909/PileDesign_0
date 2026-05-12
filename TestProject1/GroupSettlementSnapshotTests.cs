using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 一般モード入力スナップショット (PileGroupSettlement.NonBeamRectLoadsSnapshot) と
    /// 例題ロード時の初期化、ApplyActiveCaseToLegacyFields によるケース切替の独立性をテスト。
    /// 反復解析が pgs.RectLoads を収束反力で書き換えても、一般モードに戻ると原入力が復元される
    /// 仕様 (Phase 1: 一般入力の独立保持) の回帰防止を目的とする。
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

        // ── PileGroupSettlement.NonBeamRectLoadsSnapshot 基本動作 ──

        [TestMethod]
        public void Snapshot_DefaultIsNull()
        {
            var pgs = new PileGroupSettlement();
            Assert.IsNull(pgs.NonBeamRectLoadsSnapshot,
                "新規 PileGroupSettlement の snapshot は null であること");
        }

        [TestMethod]
        public void Snapshot_AssignAndRetain()
        {
            var pgs = new PileGroupSettlement();
            var snap = MakeRectLoads((500, 1), (816, 2), (1280, 3));
            pgs.NonBeamRectLoadsSnapshot = snap;

            Assert.IsNotNull(pgs.NonBeamRectLoadsSnapshot);
            Assert.AreEqual(3, pgs.NonBeamRectLoadsSnapshot.Count);
            Assert.AreEqual(500.0, pgs.NonBeamRectLoadsSnapshot[0].QA);
            Assert.AreEqual(1280.0, pgs.NonBeamRectLoadsSnapshot[2].QA);
        }

        [TestMethod]
        public void Snapshot_NotSerializedByJson()
        {
            // [JsonIgnore] によりシリアライズ対象外であることを保証 (セッション内のみの状態)
            var pgs = new PileGroupSettlement();
            pgs.RectLoads = MakeRectLoads((500, 1));
            pgs.NonBeamRectLoadsSnapshot = MakeRectLoads((999, 99));

            string json = JsonSerializer.Serialize(pgs);
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("NonBeamRectLoadsSnapshot|nonBeamRectLoadsSnapshot"),
                "JsonIgnore のため snapshot プロパティはシリアライズされない");
        }

        [TestMethod]
        public void Snapshot_DeserializedAsNull()
        {
            var pgs = new PileGroupSettlement();
            pgs.NonBeamRectLoadsSnapshot = MakeRectLoads((500, 1));
            string json = JsonSerializer.Serialize(pgs);

            var restored = JsonSerializer.Deserialize<PileGroupSettlement>(json);
            Assert.IsNotNull(restored);
            Assert.IsNull(restored.NonBeamRectLoadsSnapshot,
                "シリアライズ往復で snapshot は永続化されないため null になる");
        }

        // ── ApplySettlementConditionsOnly: 例題条件のみ適用 (snapshot 初期化) ──

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

        [TestMethod]
        public void ApplySettlementConditionsOnly_InitializesSnapshotWithOriginalQA()
        {
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            var data = MakeExampleData();

            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, data);

            var snap = input.PileGroupSettlement.NonBeamRectLoadsSnapshot;
            Assert.IsNotNull(snap, "例題ロード後 snapshot が null でないこと");
            Assert.AreEqual(3, snap.Count);
            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                snap.Select(r => r.QA).ToList(),
                "snapshot の QA は JSON 原入力と一致");
        }

        [TestMethod]
        public void ApplySettlementConditionsOnly_SnapshotIsIndependentOfRectLoads()
        {
            // 反復解析が pgs.RectLoads を書き換えても snapshot は変わらない (= deep copy)
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            var data = MakeExampleData();

            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, data);

            // pgs.RectLoads を反復後の値で書き換え (シミュレーション)
            input.PileGroupSettlement.RectLoads = MakeRectLoads((586, 1), (825, 2), (1097, 6));

            var snap = input.PileGroupSettlement.NonBeamRectLoadsSnapshot;
            Assert.IsNotNull(snap);
            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                snap.Select(r => r.QA).ToList(),
                "pgs.RectLoads を変更しても snapshot は不変 (独立コピー)");
        }

        [TestMethod]
        public void ApplySettlementConditionsOnly_SnapshotPreservesLinkedPileNo()
        {
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            var data = MakeExampleData();

            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, data);

            var snap = input.PileGroupSettlement.NonBeamRectLoadsSnapshot;
            Assert.IsNotNull(snap);
            CollectionAssert.AreEqual(
                new[] { 1, 2, 6 },
                snap.Select(r => r.LinkedPileNo).ToList(),
                "snapshot は LinkedPileNo も保持");
        }

        [TestMethod]
        public void ApplySettlementConditionsOnly_SnapshotPreservesGeometry()
        {
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            var data = MakeExampleData();

            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, data);

            var snap = input.PileGroupSettlement.NonBeamRectLoadsSnapshot;
            Assert.IsNotNull(snap);
            // 1 番目: x=[-1.25, 1.25], y=[-1.25, 1.25]
            Assert.AreEqual(-1.25, snap[0].X1, 1e-9);
            Assert.AreEqual(1.25, snap[0].X2, 1e-9);
            Assert.AreEqual(-1.25, snap[0].Y1, 1e-9);
            Assert.AreEqual(1.25, snap[0].Y2, 1e-9);
        }

        // ── スナップショット復元シナリオ (反復後の一般モード復帰) ──

        [TestMethod]
        public void RestoreFromSnapshot_RecoversOriginalRectLoadsAfterIteration()
        {
            // ユーザーフローの統合再現:
            // 1. 例題ロード: pgs.RectLoads = original, snapshot = original
            // 2. 反復解析実行: pgs.RectLoads = converged (snapshot は不変)
            // 3. 一般モードに切替: snapshot から RectLoads を復元
            var input = new InputModel { PileGroupSettlement = new PileGroupSettlement() };
            var data = MakeExampleData();

            // Phase 1: 例題ロード
            GroupSettlementExampleLoader.ApplySettlementConditionsOnly(input, data);
            var pgs = input.PileGroupSettlement;
            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                pgs.RectLoads.Select(r => r.QA).ToList());

            // Phase 2: 反復が pgs.RectLoads を収束反力で上書き (シミュレーション)
            pgs.RectLoads = MakeRectLoads((586, 1), (825, 2), (1097, 6));
            CollectionAssert.AreEqual(
                new[] { 586.0, 825.0, 1097.0 },
                pgs.RectLoads.Select(r => r.QA).ToList(),
                "Phase 2: 反復後は収束反力");

            // Phase 3: 一般モード復帰 = snapshot から RectLoads を復元
            // (SelectedActiveLoadingType setter / PileGroupSettlementAnalysis の復元コードと同等の処理)
            Assert.IsNotNull(pgs.NonBeamRectLoadsSnapshot);
            pgs.RectLoads = new ObservableCollection<RectLoad>(
                pgs.NonBeamRectLoadsSnapshot.Select(r => new RectLoad
                {
                    X1 = r.X1, X2 = r.X2, Y1 = r.Y1, Y2 = r.Y2,
                    QA = r.QA, LinkedPileNo = r.LinkedPileNo,
                }));

            CollectionAssert.AreEqual(
                new[] { 500.0, 816.0, 1280.0 },
                pgs.RectLoads.Select(r => r.QA).ToList(),
                "Phase 3: 一般モードでは原入力 (500/816/1280) に戻ること");
        }

        // ── ApplyActiveCaseToLegacyFields: ケース切替の独立性 ──

        [TestMethod]
        public void ApplyActiveCaseToLegacyFields_CopiesRectLoadsAsIndependentCollection()
        {
            var pgs = new PileGroupSettlement();
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "個別矩形（基礎梁考慮）",
                IsBeamAware = true,
                RectLoads = MakeRectLoads((586, 1), (825, 2), (1097, 6)),
            };

            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(pgs, record);

            CollectionAssert.AreEqual(
                new[] { 586.0, 825.0, 1097.0 },
                pgs.RectLoads.Select(r => r.QA).ToList(),
                "record の RectLoads が pgs.RectLoads にコピーされる");

            // 独立コレクション: pgs を変更しても record は不変
            pgs.RectLoads.Add(new RectLoad { QA = 9999 });
            Assert.AreEqual(3, record.RectLoads.Count, "record.RectLoads の長さは独立");
        }

        [TestMethod]
        public void ApplyActiveCaseToLegacyFields_CopiesSettlementGridData()
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

            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(pgs, record);

            Assert.AreEqual(3, pgs.SettlementGridData.Count);
            CollectionAssert.AreEqual(
                new[] { 5.0, 8.0, 11.0 },
                pgs.SettlementGridData.Select(g => g.Settlement).ToList());
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
