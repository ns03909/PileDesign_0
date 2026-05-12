using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// FileOperationService（ProjectData 保存/読込、コレクション変換）のテスト。
    /// </summary>
    [TestClass]
    public class FileOperationServiceTests
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
            _tempDir = Path.Combine(Path.GetTempPath(), "PileDesignTests_" + Guid.NewGuid().ToString("N"));
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

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SaveProjectData_EmptyPath_Throws()
        {
            var svc = new FileOperationService(MakeOptions());
            svc.SaveProjectData("", new InputModel(), new AnaModel());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SaveProjectData_NullPath_Throws()
        {
            var svc = new FileOperationService(MakeOptions());
            svc.SaveProjectData(null!, new InputModel(), new AnaModel());
        }

        [TestMethod]
        public void SaveProjectData_WritesJsonFile_ThatCanBeLoaded()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "project.json");

            svc.SaveProjectData(file, new InputModel(), new AnaModel());

            Assert.IsTrue(File.Exists(file));
            var loaded = svc.LoadProjectData(file);
            Assert.IsNotNull(loaded);
            Assert.IsNotNull(loaded.InputModel);
            Assert.IsNotNull(loaded.AnaModel);
            Assert.AreEqual(2, loaded.FormatVersion);  // v2: PileLayoutItems[*].Z = 接合節点 Z (2026-05 改修)
        }

        [TestMethod]
        public void SaveProjectData_WithVerticalBeamResults_PersistsList()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "project.json");
            var vbrs = new List<VerticalBeamCaseResult>
            {
                new() { },
                new() { },
            };

            svc.SaveProjectData(file, new InputModel(), new AnaModel(), vbrs);
            var loaded = svc.LoadProjectData(file);

            Assert.IsNotNull(loaded.VerticalBeamCaseResults);
            Assert.AreEqual(2, loaded.VerticalBeamCaseResults.Count);
        }

        [TestMethod]
        public void SaveProjectData_NullVerticalBeamResults_LeavesFieldNull()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "project.json");

            svc.SaveProjectData(file, new InputModel(), new AnaModel(), verticalBeamCaseResults: null);
            var loaded = svc.LoadProjectData(file);

            Assert.IsNull(loaded.VerticalBeamCaseResults);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void LoadProjectData_MissingFile_Throws()
        {
            var svc = new FileOperationService(MakeOptions());
            svc.LoadProjectData(Path.Combine(_tempDir, "does_not_exist.json"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void LoadProjectData_EmptyPath_Throws()
        {
            var svc = new FileOperationService(MakeOptions());
            svc.LoadProjectData("");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void LoadProjectData_InvalidJson_ThrowsInvalidOperation()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "bad.json");
            File.WriteAllText(file, "{ this is : not :: valid JSON ");
            svc.LoadProjectData(file);
        }

        [TestMethod]
        public void LoadProjectData_NewerVersion_ThrowsWithVersionMessage()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "future.json");
            // FormatVersion = 99 の将来ファイルを模擬（最低限の有効 JSON）
            File.WriteAllText(file, """{"FormatVersion": 99}""");

            try
            {
                svc.LoadProjectData(file);
                Assert.Fail("例外が必要");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "v99");
            }
        }

        [TestMethod]
        public async Task SaveAndLoadAsync_RoundTrip()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "async.json");

            await svc.SaveProjectDataAsync(file, new InputModel(), new AnaModel());
            Assert.IsTrue(File.Exists(file));

            var loaded = await svc.LoadProjectDataAsync(file);
            Assert.IsNotNull(loaded);
            Assert.IsNotNull(loaded.InputModel);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void ConvertToObservableCollections_NullInput_Throws()
        {
            var svc = new FileOperationService(MakeOptions());
            svc.ConvertToObservableCollections(null!);
        }

        [TestMethod]
        public void ConvertToObservableCollections_ListToObservable()
        {
            var svc = new FileOperationService(MakeOptions());
            // 通常の List<T> を持つ InputModel を用意
            var model = new InputModel
            {
                PileLayoutItems = new List<PileLayoutDataItem>() as ICollection<PileLayoutDataItem>
                    is ObservableCollection<PileLayoutDataItem> oc ? oc : [],
                InputNodes = [],
                GridXItems = null,
                GridYItems = null,
                PileBodies = [],
                GroundsInput = [],
            };

            svc.ConvertToObservableCollections(model);

            // Null だったグリッドは空 ObservableCollection に初期化される
            Assert.IsInstanceOfType<ObservableCollection<GridDataItem>>(model.GridXItems);
            Assert.IsInstanceOfType<ObservableCollection<GridDataItem>>(model.GridYItems);
            Assert.AreEqual(0, model.GridXItems.Count);
            Assert.AreEqual(0, model.GridYItems.Count);
        }

        [TestMethod]
        public void ConvertToObservableCollections_AlreadyObservable_Preserved()
        {
            var svc = new FileOperationService(MakeOptions());
            var original = new ObservableCollection<GridDataItem> { new() { Coord = 1.0 } };
            var model = new InputModel { GridXItems = original };

            svc.ConvertToObservableCollections(model);

            // 既に ObservableCollection ならそのまま保持（内容は変わらない）
            Assert.AreSame(original, model.GridXItems);
            Assert.AreEqual(1, model.GridXItems.Count);
        }
    }
}
