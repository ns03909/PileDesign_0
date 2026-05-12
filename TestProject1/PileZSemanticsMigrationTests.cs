using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// PileLayoutDataItem.Z セマンティクスを v1 (杭頭節点 Z) → v2 (接合節点 Z) へ
    /// マイグレートする MigratePileZSemantics_v1_to_v2 の動作を検証するテスト群。
    ///
    /// Phase 1 段階のテスト:
    /// - マイグレーション関数自体は呼べば期待通り Z を変換する
    /// - ApplyPostLoadProtocol / LoadHeadless のフックは「コメントアウト状態」で組み込まれており、
    ///   実行されない (Phase 2 で解禁)
    /// - FormatVersion=2 が Save 時に書き出される
    /// </summary>
    [TestClass]
    public class PileZSemanticsMigrationTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PileZSemantics_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
            }
        }

        // --- マイグレーション関数の単体動作 -------------------------------------

        [TestMethod]
        public void MigratePileZSemantics_v1_to_v2_AddsDeltaZcToZ()
        {
            var input = new InputModel();
            input.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(no: 1, x: 0, y: 0, z: 10.0, deltaZc: 1.0),
                MakePile(no: 2, x: 5, y: 0, z: -3.0, deltaZc: 0.5),
                MakePile(no: 3, x: 0, y: 5, z: 0.0,  deltaZc: 0.0),
            };

            InvokeMigrate(input);

            Assert.AreEqual(11.0, input.PileLayoutItems[0].Z, 1e-12, "Z=10 + ΔZc=1 → 11");
            Assert.AreEqual(-2.5, input.PileLayoutItems[1].Z, 1e-12, "Z=-3 + ΔZc=0.5 → -2.5");
            Assert.AreEqual(0.0,  input.PileLayoutItems[2].Z, 1e-12, "Z=0 + ΔZc=0 → 0 (no-op)");
        }

        [TestMethod]
        public void MigratePileZSemantics_v1_to_v2_PreservesDeltaZc()
        {
            var input = new InputModel();
            input.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
            {
                MakePile(no: 1, x: 0, y: 0, z: 10.0, deltaZc: 1.0),
            };

            InvokeMigrate(input);

            // ΔZc 自体は変更しない (保存時の物理量を維持)
            Assert.AreEqual(1.0, input.PileLayoutItems[0].FoundationBeamDeltaZc, 1e-12);
        }

        [TestMethod]
        public void MigratePileZSemantics_v1_to_v2_NullPileLayoutItems_NoOp()
        {
            var input = new InputModel
            {
                PileLayoutItems = null!
            };

            // null でも例外を投げない
            InvokeMigrate(input);
            Assert.IsNull(input.PileLayoutItems);
        }

        [TestMethod]
        public void MigratePileZSemantics_v1_to_v2_EmptyCollection_NoOp()
        {
            var input = new InputModel();
            input.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>();

            InvokeMigrate(input);
            Assert.AreEqual(0, input.PileLayoutItems.Count);
        }

        // --- Save 時 FormatVersion=2 ---------------------------------------------

        [TestMethod]
        public void SaveProjectData_WritesFormatVersion2()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "fv2.json");
            svc.SaveProjectData(file, new InputModel(), new AnaModel(), null);

            var raw = File.ReadAllText(file);
            StringAssert.Contains(raw, "\"FormatVersion\": 2",
                "Save 後の JSON に FormatVersion=2 が含まれていること");
        }

        [TestMethod]
        public async Task SaveProjectDataAsync_WritesFormatVersion2()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "fv2_async.json");
            await svc.SaveProjectDataAsync(file, new InputModel(), new AnaModel(), null);

            var raw = File.ReadAllText(file);
            StringAssert.Contains(raw, "\"FormatVersion\": 2");
        }

        // --- v2 ファイルのロードは拒否されない -----------------------------------

        [TestMethod]
        public void LoadProjectData_AcceptsV2Files()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "v2.json");
            svc.SaveProjectData(file, new InputModel(), new AnaModel(), null);

            // currentVersion=2 なので v2 はロード可能
            var loaded = svc.LoadProjectData(file);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.FormatVersion);
        }

        [TestMethod]
        public void LoadProjectData_RejectsV3Files()
        {
            // 未来バージョンのファイルを偽造
            var raw = """
            {
              "FormatVersion": 3,
              "InputModel": {},
              "AnaModel": {}
            }
            """;
            var file = Path.Combine(_tempDir, "v3_future.json");
            File.WriteAllText(file, raw);

            var svc = new FileOperationService(MakeOptions());
            var ex = Assert.ThrowsException<System.InvalidOperationException>(
                () => svc.LoadProjectData(file));
            StringAssert.Contains(ex.Message, "v3");
        }

        // --- v1 ファイルは互換扱いで通る (FormatVersion=1 < currentVersion=2) ----

        [TestMethod]
        public void LoadProjectData_AcceptsV1Files()
        {
            var raw = """
            {
              "FormatVersion": 1,
              "InputModel": {},
              "AnaModel": {}
            }
            """;
            var file = Path.Combine(_tempDir, "v1_legacy.json");
            File.WriteAllText(file, raw);

            var svc = new FileOperationService(MakeOptions());
            var loaded = svc.LoadProjectData(file);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.FormatVersion);
        }

        // --- helpers --------------------------------------------------------------

        private static PileLayoutDataItem MakePile(int no, double x, double y, double z, double deltaZc)
        {
            return new PileLayoutDataItem
            {
                No = no,
                PileNo = no,
                X = x,
                Y = y,
                Z = z,
                FoundationBeamDeltaZc = deltaZc,
                PileBodyNo = 1,
                GroundNo = 1,
            };
        }

        /// <summary>
        /// internal メソッド MigratePileZSemantics_v1_to_v2 をリフレクションで呼ぶ。
        /// </summary>
        private static void InvokeMigrate(InputModel input)
        {
            var mi = typeof(InputModel).GetMethod(
                "MigratePileZSemantics_v1_to_v2",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "MigratePileZSemantics_v1_to_v2 が見つかりません");
            mi.Invoke(input, null);
        }
    }
}
