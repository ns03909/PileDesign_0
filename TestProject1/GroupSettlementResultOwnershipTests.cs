using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の結果は <see cref="GroupSettlementResult"/> が持ち、入力モデルはそれを
    /// <b>参照するだけ</b> ([JsonIgnore])。保存は <c>ProjectData.GroupSettlementResult</c> の節。
    ///
    /// 以前はケース記録が入力モデルの中にあったため、
    /// <list type="bullet">
    /// <item>保存ファイルに二重に入った (現在の入力と、解析時のスナップショットの両方)</item>
    /// <item>Undo が入力ごと巻き戻すので、入力を 1 つ直して戻すと沈下の結果まで消えた</item>
    /// </list>
    /// ここでは「1 回だけ書き出す」「旧ファイルも開ける」「Undo で消えない」を固定する。
    /// </summary>
    [TestClass]
    public class GroupSettlementResultOwnershipTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static InputModel? LoadExample()
        {
            var (m, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            return m;
        }

        /// <summary>沈下解析が終わった状態を作る。</summary>
        private static GroupSettlementCaseRecord AddSettlementResult(InputModel input, double settlement_mm)
        {
            input.PileGroupSettlement ??= new PileGroupSettlement();
            var pgs = input.PileGroupSettlement;
            int pileNo = input.PileLayoutItems[0].PileNo;

            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "任意矩形",
                IsConverged = true,
                PileSettlements_mm = new Dictionary<int, double> { [pileNo] = settlement_mm },
            };
            pgs.CaseRecords.Add(record);
            pgs.ActiveCaseIndex = pgs.CaseRecords.Count - 1;
            return record;
        }

        // ── 保存ファイルに 1 回だけ入る ───────────────────

        /// <summary>
        /// 沈下の結果が保存ファイルに<b>1 回だけ</b>書き出されること。
        ///
        /// 入力の中に持っていた頃は、現在の入力とスナップショットの両方に同じ記録が入っていた。
        /// </summary>
        [TestMethod]
        public void SavedFile_ContainsTheSettlementResultOnlyOnce()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AddSettlementResult(live, 12.5);

            // 解析時のスナップショットも同じ結果を指す (本番と同じ形)
            var snapshot = LoadExample()!;
            snapshot.PileGroupSettlement ??= new PileGroupSettlement();
            snapshot.PileGroupSettlement.Result = live.PileGroupSettlement.Result;

            var data = new ProjectData
            {
                FormatVersion = 2,
                InputModel = live,
                ResultInputSnapshot = snapshot,
                GroupSettlementResult = live.PileGroupSettlement.Result,
            };

            string json = JsonSerializer.Serialize(data, Options);

            int occurrences = CountOccurrences(json, "LoadCaseName");
            Assert.AreEqual(1, occurrences,
                $"沈下の記録が保存ファイルに {occurrences} 回入っています (入力の中にも書き出している)");
        }

        /// <summary>
        /// 保存 → 読込 で沈下の結果が戻ること。読込側の手当ては本番の処理をそのまま呼ぶ。
        /// </summary>
        [TestMethod]
        public void SavedFile_RestoresTheSettlementResult()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            int pileNo = live.PileLayoutItems[0].PileNo;
            AddSettlementResult(live, 12.5);

            var data = new ProjectData
            {
                FormatVersion = 2,
                InputModel = live,
                GroupSettlementResult = live.PileGroupSettlement.Result,
            };

            string json = JsonSerializer.Serialize(data, Options);
            var loaded = JsonSerializer.Deserialize<ProjectData>(json, Options)!;

            LegacySettlementMigration.AttachResultAndMigrate(
                loaded.InputModel, loaded.GroupSettlementResult);

            var pgs = loaded.InputModel.PileGroupSettlement;
            Assert.AreEqual(1, pgs.CaseRecords.Count, "沈下の結果が復元されていない");
            Assert.AreEqual(12.5, pgs.SettlementOf(pileNo), 1e-9);
            Assert.AreEqual(0, pgs.ActiveCaseIndex);
        }

        /// <summary>
        /// 水平解析の結果を保存しない (入力のみの軽量保存・自動保存) 場合でも、
        /// 沈下の結果は保存されること。従来は入力の中にあったので当然残っていた。
        /// </summary>
        [TestMethod]
        public void SaveWithoutAnaModel_StillWritesTheSettlementResult()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AddSettlementResult(live, 7.5);

            string path = Path.Combine(Path.GetTempPath(), $"pd_settlement_{Guid.NewGuid():N}.pdj");
            try
            {
                new FileOperationService(Options).SaveProjectData(path, live, anaModel: null);

                var loaded = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path), Options)!;
                Assert.IsNotNull(loaded.GroupSettlementResult,
                    "沈下の結果が保存されていない (自動保存から復元すると沈下が消える)");
                Assert.AreEqual(1, loaded.GroupSettlementResult!.CaseRecords.Count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ── 旧ファイル ────────────────────────────────

        /// <summary>
        /// 結果が入力の中に入っている旧ファイルが、いまも開けること。
        /// 開いたあと保存し直しても<b>二重にならない</b>こと。
        /// </summary>
        [TestMethod]
        public void OldFile_WithRecordsInsideTheInput_LoadsAndIsNotDuplicatedOnSave()
        {
            string oldJson =
                "{\n" +
                "  \"$id\": \"1\",\n" +
                "  \"LoadingType\": \"任意矩形\",\n" +
                "  \"CaseRecords\": {\n" +
                "    \"$id\": \"2\",\n" +
                "    \"$values\": [\n" +
                "      {\n" +
                "        \"$id\": \"3\",\n" +
                "        \"LoadCaseName\": \"VL\",\n" +
                "        \"LoadingType\": \"任意矩形\",\n" +
                "        \"PileSettlements_mm\": { \"$id\": \"4\", \"1\": 9.0 }\n" +
                "      }\n" +
                "    ]\n" +
                "  },\n" +
                "  \"ActiveCaseIndex\": 0\n" +
                "}";

            var pgs = JsonSerializer.Deserialize<PileGroupSettlement>(oldJson, Options)!;
            var input = new InputModel { PileGroupSettlement = pgs };

            LegacySettlementMigration.AttachResultAndMigrate(input, loadedResult: null);

            Assert.AreEqual(1, pgs.CaseRecords.Count, "旧ファイルの沈下結果が失われている");
            Assert.AreEqual(9.0, pgs.SettlementOf(1), 1e-9);

            // 保存し直したら入力の側には出ない
            string resaved = JsonSerializer.Serialize(pgs, Options);
            Assert.AreEqual(0, CountOccurrences(resaved, "LoadCaseName"),
                "開き直して保存すると入力の中に結果が復活しています");
        }

        // ── Undo ────────────────────────────────────

        /// <summary>
        /// Undo は入力を巻き戻すもので、沈下の結果を消さないこと。
        ///
        /// 結果が入力モデルの中にあった頃は、沈下解析より前の状態へ戻すと結果も消えていた。
        /// 一方で結果は [JsonIgnore] なので、持ち越しを忘れると<b>どの Undo でも</b>消える。
        /// </summary>
        [TestMethod]
        public void Undo_DoesNotDiscardTheSettlementResult()
        {
            // 例題モデルは Undo の DeepCopy を通らない (荷重ケースが揃っていない) ので
            // 既定のコンストラクタから組む。ここで見たいのは沈下の結果の持ち越しだけ。
            var input = new InputModel
            {
                PileLayoutItems = [new PileLayoutDataItem { PileNo = 1 }],
            };

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            AddSettlementResult(input, 12.5);
            var result = input.PileGroupSettlement.Result;

            // 入力を 1 つ編集して元に戻す
            vm.SaveUndoState("テスト");
            input.PileLayoutItems[0].AxialForceVL0 += 100.0;
            vm.SaveUndoState("テスト");
            vm.UndoCommand.Execute(null);

            var pgs = vm.CurrentInputModel.PileGroupSettlement;
            Assert.AreEqual(1, pgs.CaseRecords.Count, "Undo で沈下の結果が消えた");
            Assert.AreSame(result, pgs.Result, "Undo で結果のインスタンスが差し替わっている");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                n++;
            return n;
        }
    }
}
