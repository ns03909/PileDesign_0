using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// FileOperationService（ProjectData 保存/読込、コレクション変換）のテスト。
    /// </summary>
    [TestClass]
    [DoNotParallelize]
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
        public void SaveProjectData_PreservesAnalysisRunRecordsPerKind()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "run-metadata.json");
            var metadata = new AnalysisRunMetadata
            {
                ApplicationVersion = "1.0.34-beta",
                ConvergenceMethod = "ラインサーチ",
                Level1Steps = 4,
                Level2Steps = 16,
                CaseParallelism = 8,
                InitialRelaxationFactor = 0.7,
                BaseResidualTolerance = 1e-6,
                RelaxedResidualTolerance = 1e-5,
                MaximumIterations = 100,
                LinearSolverResidualTolerance = 1e-6,
                Cases = [new AnalysisCaseMetadata
                {
                    Level = 2, LoadCaseNo = 3, LoadCaseName = "X方向",
                    LoadCombinationNo = 5, LoadCombinationName = "地震時",
                    IsLiquefaction = true, Status = "Converged",
                }],
            };

            var settlement = new AnalysisRunMetadata
            {
                Kind = AnalysisKind.GroupSettlement,
                ExecutedAt = new DateTime(2026, 9, 30, 10, 15, 0),
                Conditions = [new("荷重の置き方", "全体矩形")],
            };

            svc.SaveProjectData(file, new InputModel(), new AnaModel(), analysisRunRecords: [metadata, settlement]);
            var records = svc.LoadProjectData(file).AnalysisRunRecords;

            Assert.IsNotNull(records);
            Assert.AreEqual(2, records.Count, "解析の種類ごとの記録がすべて残る");
            var loaded = records.Single(r => r.Kind == AnalysisKind.Horizontal);
            var loadedSettlement = records.Single(r => r.Kind == AnalysisKind.GroupSettlement);
            Assert.AreEqual(settlement.ExecutedAt, loadedSettlement.ExecutedAt);
            Assert.AreEqual("【群杭沈下解析 2026/09/30 10:15】 荷重の置き方 全体矩形", loadedSettlement.Describe());
            StringAssert.Matches(File.ReadAllText(file), new System.Text.RegularExpressions.Regex("\"Kind\":\\s*\"GroupSettlement\""),
                "種類は名前で書く (番号だと並びを変えたときに別の種類として読まれる)");
            Assert.AreEqual(metadata.ApplicationVersion, loaded.ApplicationVersion);
            Assert.AreEqual(metadata.ConvergenceMethod, loaded.ConvergenceMethod);
            Assert.AreEqual(metadata.Level1Steps, loaded.Level1Steps);
            Assert.AreEqual(metadata.CaseParallelism, loaded.CaseParallelism);
            Assert.AreEqual("L2 X方向 (地震時)・液状化・収束", loaded.DescribeCases());
            Assert.IsTrue(loaded.DescribeSettings().Contains("反復上限 100", StringComparison.Ordinal));
        }

        [TestMethod]
        public void SaveAndLoad_NumericCoordinatesAreCultureIndependent()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var input = new InputModel();
                // 杭を足すには画面の ViewModel に結び付けておく必要がある (新しい入力では一覧も null)
                input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
                input.PileLayoutItems ??= [];
                input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 1, X = 1.5, Y = -2.25 });
                var file = Path.Combine(_tempDir, "culture-independent.json");
                var svc = new FileOperationService(MakeOptions());

                svc.SaveProjectData(file, input, new AnaModel());
                var json = File.ReadAllText(file);
                var loaded = svc.LoadProjectData(file);

                StringAssert.Contains(json, "1.5", "JSON の数値を de-DE の小数点カンマで書いています。");
                Assert.AreEqual(1.5, loaded.InputModel.PileLayoutItems[0].X);
                Assert.AreEqual(-2.25, loaded.InputModel.PileLayoutItems[0].Y);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
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
