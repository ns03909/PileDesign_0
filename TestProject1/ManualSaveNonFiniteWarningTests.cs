using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 手動保存は NaN・無限大で止めないが、その箇所を知らせること。
    ///
    /// 手動保存は作業を失わないよう、既定では NaN の検査をせずに保存していた。自動保存は箇所を示して止まるので、
    /// 手動保存だけの利用者には値の異常に気付く手掛かりが無かった。保存は止めずに、見つかった箇所を返して警告する。
    /// </summary>
    [TestClass]
    public class ManualSaveNonFiniteWarningTests
    {
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PileDesignManualSaveNaN", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        /// <summary>画面の入力の検証をすり抜けた NaN (setter は NaN を弾くので、フィールドへ直接書く)。</summary>
        private static InputModel ModelWithNaN()
        {
            var ground = new GroundInput();
            typeof(GroundInput).GetField("_groundTopAltitude", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ground, double.NaN);
            return new InputModel { GroundsInput = new ObservableCollection<GroundInput> { ground } };
        }

        /// <summary>本番と同じく NaN を名前付きの値として書ける設定 (手動保存が NaN で止まらないのはこのため)。</summary>
        private static FileOperationService Service() => new(new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });

        [TestMethod]
        public async Task AManualSaveWithNaNSucceedsAndReportsWhere()
        {
            string path = Path.Combine(_dir, "model.pdj");

            string? location = await Service().SaveProjectDataAsync(path, ModelWithNaN(), null);

            Assert.IsTrue(File.Exists(path), "NaN があると手動保存が止まりました (作業を失います)");
            Assert.IsNotNull(location, "NaN を含んだまま保存したのに、その箇所を返していません");
            StringAssert.Contains(location, "GroundTopAltitude");
            StringAssert.Contains(location, "NaN");

            string message = FileOperationService.DescribeSavedNonFinite(location!);
            StringAssert.Contains(message, "保存しましたが");
            StringAssert.Contains(message, location!);
        }

        [TestMethod]
        public async Task AManualSaveWithoutNaNReportsNothing()
        {
            string? location = await Service().SaveProjectDataAsync(Path.Combine(_dir, "model.pdj"), new InputModel(), null);
            Assert.IsNull(location, "NaN が無いのに、警告する箇所を返しています");
        }

        /// <summary>保存を止める設定では、これまでどおり止まること。</summary>
        [TestMethod]
        public async Task TheStoppingSettingStillStops()
        {
            var service = Service();
            service.ValidateFiniteBeforeSave = true;
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => service.SaveProjectDataAsync(Path.Combine(_dir, "model.pdj"), ModelWithNaN(), null));
        }

        /// <summary>画面の 2 つの保存の経路 (上書き保存・名前を付けて保存) が、保存のあとで警告を出すこと。</summary>
        [TestMethod]
        public void BothSaveCommandsWarnAfterSaving()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            foreach (var signature in new[]
                     {
                         "internal async Task<bool> SaveInputModelFileAsCoreAsync()",
                         "internal async Task<bool> SaveInputModelFileCoreAsync()",
                     })
            {
                string body = TestSource.MethodBody(src, signature);
                int saved = body.IndexOf("string? nonFinite = await _fileOperationService.SaveProjectDataAsync(", StringComparison.Ordinal);
                int warned = body.IndexOf("WarnIfSavedNonFinite(nonFinite);", StringComparison.Ordinal);
                Assert.IsTrue(saved >= 0 && warned > saved, $"{signature}: 保存した入力の NaN・無限大を知らせていません");
            }
        }
    }
}
