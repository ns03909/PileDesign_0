using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 保存が壊れないこと。
    ///
    /// この製品では「保存ファイルが開けない」が最も痛い壊れ方なので、
    /// 途中で落ちたときと、保存グラフに入る型の作りの 2 方向を押さえる。
    /// </summary>
    [TestClass]
    public class SaveDurabilityTests
    {
        private string _dir = "";

        /// <summary>本番と同じ直列化設定 (参照の保持が肝心なので既定では代用できない)。</summary>
        private static JsonSerializerOptions SaveOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PileDesignSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch (IOException) { }
        }

        // ── 途中で落ちても保存先を壊さない ──────────────────

        /// <summary>
        /// 保存に失敗しても、<b>前の内容がそのまま残る</b>こと。
        ///
        /// 保存先へ直接書いていると、途中で落ちた場合に切り株のファイルが残る。
        /// 自動保存ではその切り株が復元候補として拾われてしまう。
        /// </summary>
        [TestMethod]
        public void FailedSave_LeavesThePreviousFileIntact()
        {
            string path = Path.Combine(_dir, "model.pdj");
            const string previous = "前回の保存内容";
            File.WriteAllText(path, previous);

            // 一時ファイルと同じ名前のフォルダを作っておくと、一時ファイルを作れず保存が落ちる
            string blocker = path + ".saving";
            Directory.CreateDirectory(blocker);

            var service = new FileOperationService(SaveOptions());
            Assert.ThrowsException<UnauthorizedAccessException>(
                () => service.SaveProjectData(path, new InputModel(), null),
                "保存が失敗しなかった (この検査が成立していない)");

            Assert.AreEqual(previous, File.ReadAllText(path),
                "保存に失敗したのに、前の内容が壊れている");
        }

        /// <summary>保存が成功したら、一時ファイルを残さないこと。</summary>
        [TestMethod]
        public void SuccessfulSave_LeavesNoTemporaryFile()
        {
            string path = Path.Combine(_dir, "model.pdj");

            new FileOperationService(SaveOptions()).SaveProjectData(path, new InputModel(), null);

            Assert.IsTrue(File.Exists(path), "保存されていない");
            CollectionAssert.AreEqual(new[] { "model.pdj" },
                Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName),
                "一時ファイルが残っている");
        }

        /// <summary>
        /// 書きかけの一時ファイルが、自動保存の復元候補として拾われないこと。
        /// 拾うと、切り株を「前回の作業」として提示してしまう。
        /// </summary>
        [TestMethod]
        public void PartialFile_IsNotPickedUpAsAnAutoSaveCandidate()
        {
            File.WriteAllText(Path.Combine(_dir, "proj_autosave_20260828.pdj"), "{}");
            File.WriteAllText(Path.Combine(_dir, "proj_autosave_20260828.pdj.saving"), "{ 途中");

            var found = Directory.GetFiles(_dir, "*_autosave_*.pdj");

            CollectionAssert.AreEqual(new[] { "proj_autosave_20260828.pdj" },
                Array.ConvertAll(found, Path.GetFileName),
                "書きかけの一時ファイルが復元候補に混ざっている");
        }

        // ── 保存グラフに入る型の作り ────────────────────────

        /// <summary>
        /// <see cref="DummyBeamResult"/> が復元できること。
        ///
        /// <c>ReferenceHandler.Preserve</c> では「書き出されるが復元されない」プロパティがあると、
        /// そこに付いた <c>$id</c> が読込時に登録されず、他所からの <c>$ref</c> が解決できなくなる。
        /// この型は get のみ + 引数付きコンストラクタだったため、
        /// ダミー梁の結果が 1 件でも入った瞬間に保存ファイルが開けなくなる作りだった
        /// (現状は空のまま運用されているので表面化していないだけ)。
        /// </summary>
        [TestMethod]
        public void DummyBeamResult_SurvivesASaveLoadRoundTrip()
        {
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
            };

            var beam = new DummyBeam
            {
                DummyBeamResults =
                [
                    new DummyBeamResult(new LoadCase(), new LoadCombination(1, 1.0, 0.0, 0.0),
                                        isLiquefaction: true, step: 3),
                ],
            };

            string json = JsonSerializer.Serialize(beam, options);
            var restored = JsonSerializer.Deserialize<DummyBeam>(json, options);

            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored!.DummyBeamResults.Count, "結果が復元されていない");
            Assert.AreEqual(3, restored.DummyBeamResults[0].Step, "Step が復元されていない");
            Assert.IsTrue(restored.DummyBeamResults[0].IsLiquefaction, "液状化の別が復元されていない");
            Assert.IsNotNull(restored.DummyBeamResults[0].LoadCase, "荷重ケースが復元されていない");
        }
    }
}
