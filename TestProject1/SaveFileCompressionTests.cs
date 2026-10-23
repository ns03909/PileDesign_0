using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 保存ファイルの大きさ (2026-10-24)。解析結果入りのファイルが数百 MB になっていた (杭 45 本・304 ステップで 689 MB)。
    /// <list type="bullet">
    /// <item>gzip で圧縮して書く (1/5〜1/7)。読むときは先頭の 2 バイトで判断し、圧縮していない今までのファイルも開ける。</item>
    /// <item>他の値から求める読み出し専用の値 (合力・合成変位) は書かない (689 MB 中 127 MB)。</item>
    /// <item>読むときにファイル全体を文字列にしない (1 文字 2 バイトで、689 MB のファイルが約 1.4 GB になった)。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class SaveFileCompressionTests
    {
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PileDesignCompressionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private static FileOperationService Service() => new(new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        });

        private static FileOperationService.PreparedSave Prepare(InputModel input, string? source = null, string? session = null)
            => Service().PrepareSave(input, null, sourceFilePath: source, autoSaveSessionId: session);

        [TestMethod]
        public void SavedFiles_AreCompressed_AndOpenAgain()
        {
            using var vm = new PileDesign.ViewModels.MainWindowViewModel();
            var input = vm.CurrentInputModel!;
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Clear();
            input.PileLayoutItems.Add(new PileLayoutDataItem { No = 1, PileNo = 1, PileBodyNo = 1, GroundNo = 1, X = 1.25, Y = -2.5, Z = -1.0 });
            string path = Path.Combine(_dir, "保存.pdj");
            var prepared = Prepare(input);
            Service().WritePrepared(path, prepared);

            byte[] onDisk = File.ReadAllBytes(path);
            Assert.IsTrue(FileOperationService.IsCompressed(onDisk), "保存ファイルが圧縮されていません");
            Assert.IsTrue(onDisk.Length < prepared.Payload!.Length, "圧縮して小さくなっていません");
            CollectionAssert.AreEqual(prepared.Payload, FileOperationService.ReadSaveFileBytes(path), "展開した中身が保存した JSON と違います");

            var loaded = Service().LoadProjectData(path);
            Assert.AreEqual(1, loaded.InputModel!.PileLayoutItems.Count);
            Assert.AreEqual(-2.5, loaded.InputModel.PileLayoutItems[0].Y);
            Assert.IsTrue(loaded.InputPropertyNamesInFile is { Count: > 0 }, "入力の節の項目名を集めていません (使わなかった項目の知らせが働かない)");
        }

        /// <summary>今までの (圧縮していない) 保存ファイルも開ける。BOM 付きでも開ける。</summary>
        [TestMethod]
        public void UncompressedFiles_FromEarlierVersions_StillOpen()
        {
            var prepared = Prepare(new InputModel());
            string plain = Path.Combine(_dir, "旧.pdj");
            File.WriteAllBytes(plain, prepared.Payload!);
            Assert.IsNotNull(Service().LoadProjectData(plain).InputModel);

            string withBom = Path.Combine(_dir, "旧BOM.pdj");
            File.WriteAllBytes(withBom, [0xEF, 0xBB, 0xBF, .. prepared.Payload!]);
            Assert.IsNotNull(Service().LoadProjectData(withBom).InputModel, "BOM 付きのファイルを開けません");

            // 古い形式の fixture (圧縮していない・形式 v1) も今までどおり
            string fixture = Path.Combine(TestSource.Dir("TestProject1", "TestData", "BrokenFiles"), "old-format-v1.pdjson");
            Assert.IsFalse(FileOperationService.IsCompressed(File.ReadAllBytes(fixture)));
            Assert.AreEqual(1, Service().LoadProjectData(fixture).FormatVersion);
        }

        [TestMethod]
        public void ABrokenCompressedFile_IsReportedAsBroken()
        {
            string path = Path.Combine(_dir, "壊れた.pdj");
            File.WriteAllBytes(path, [0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02, 0x03]);
            var ex = Assert.ThrowsException<InvalidOperationException>(() => Service().LoadProjectData(path));
            StringAssert.Contains(ex.Message, "破損");
        }

        /// <summary>入力の節の項目名は、入力の節までの頭だけから集める。文書全体から集めたものと同じになる。</summary>
        [TestMethod]
        public void TheInputPropertyNames_ComeFromTheHeadOnly_AndMatchTheWholeDocument()
        {
            using var vm = new PileDesign.ViewModels.MainWindowViewModel();
            var input = vm.CurrentInputModel!;
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Clear();
            input.PileLayoutItems.Add(new PileLayoutDataItem { No = 1, PileNo = 1, PileBodyNo = 1, GroundNo = 1 });
            byte[] payload = Prepare(input).Payload!;
            string whole = Encoding.UTF8.GetString(payload);
            string head = FileOperationService.InputSectionDocument(payload);
            Assert.IsTrue(head.Length <= whole.Length);
            CollectionAssert.AreEquivalent(
                LoadCompatibilityReport.CollectPropertyNames(whole, "InputModel").ToList(),
                LoadCompatibilityReport.CollectPropertyNames(head, "InputModel").ToList());
            Assert.AreEqual("{}", FileOperationService.InputSectionDocument("""{"FormatVersion":2}"""u8));
        }

        /// <summary>自動保存が圧縮されていても、復元候補の元ファイルと起動の印を頭から読める。</summary>
        [TestMethod]
        public void CompressedAutoSaves_StillTellWhereTheyCameFrom()
        {
            string path = Path.Combine(_dir, "Foo_autosave_20261024_100000.pdj");
            Service().WritePrepared(path, Prepare(new InputModel(), source: @"C:\work\Foo.pdj", session: "session-1"));
            Assert.IsTrue(FileOperationService.IsCompressed(File.ReadAllBytes(path)));
            var (source, session) = AutoSaveService.ReadRecordedOrigin(path);
            Assert.AreEqual(@"C:\work\Foo.pdj", source);
            Assert.AreEqual("session-1", session);
        }

        /// <summary>他の値から求める値 (合力・合成変位) は書かない。読み込んだあとも同じ値が求まる。</summary>
        [TestMethod]
        public void ComputedResultValues_AreNotWritten()
        {
            var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
            var force = new BeamForce(1, 3, 4, 0, 6, 8, 0, 0, 0, 0, 0, 0);
            var disp = new BeamDisp(0, 3, 4, 0, 6, 8, 0, 0, 0, 0, 0, 0);
            var node = new NodeDisp(3, 4, 0, 6, 8, 0);
            string json = JsonSerializer.Serialize(new object[] { force, disp, node }, options);
            foreach (var name in new[] { "\"Fi\"", "\"Mi\"", "\"Fj\"", "\"Mj\"", "\"FabsMax\"", "\"MabsMax\"", "\"Di\"", "\"Ri\"", "\"Dj\"", "\"Rj\"", "\"Uh\"", "\"Th\"" })
                Assert.IsFalse(json.Contains(name), $"{name} を書いています");

            var back = JsonSerializer.Deserialize<BeamForce>(JsonSerializer.Serialize(force, options), options)!;
            Assert.AreEqual(5.0, back.Fi, 1e-12);
            Assert.AreEqual(10.0, back.MabsMax, 1e-12);
            Assert.AreEqual(5.0, JsonSerializer.Deserialize<NodeDisp>(JsonSerializer.Serialize(node, options), options)!.Uh, 1e-12);

            var load = new NodeLoad(3, 4, 0, 6, 8, 0);
            string loadJson = JsonSerializer.Serialize(load, options);
            Assert.IsFalse(loadJson.Contains("\"Fh\"") || loadJson.Contains("\"Mh\""));
        }
    }
}
