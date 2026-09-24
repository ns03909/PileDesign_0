using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

            // 書き込みの途中で落とす (一時ファイルに書いている最中に直列化が例外を出す)。
            // 以前は一時ファイルと同じ名前のフォルダを置いて作成を失敗させていたが、
            // 一時ファイルの名前は保存ごとに変わるようにしたので、途中で落とす形にした (実際の壊れ方にも近い)
            var options = SaveOptions();
            options.Converters.Add(new ThrowingGroundInputConverter());
            var input = new InputModel { GroundsInput = new ObservableCollection<GroundInput> { new() } };

            var service = new FileOperationService(options);
            Assert.ThrowsException<IOException>(
                () => service.SaveProjectData(path, input, null),
                "保存が失敗しなかった (この検査が成立していない)");

            Assert.AreEqual(previous, File.ReadAllText(path),
                "保存に失敗したのに、前の内容が壊れている");
            CollectionAssert.AreEqual(new[] { "model.pdj" },
                Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName),
                "失敗した保存の一時ファイルが残っている");
        }

        /// <summary>直列化の途中で書き込みが失敗したことにする変換器 (試験用)。</summary>
        private sealed class ThrowingGroundInputConverter : JsonConverter<GroundInput>
        {
            public override GroundInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, GroundInput value, JsonSerializerOptions options)
                => throw new IOException("書き込みの途中で失敗 (試験)");
        }

        /// <summary>
        /// 同じ保存先への保存が重なっても、互いの一時ファイルを壊さないこと。
        ///
        /// 以前は一時ファイルが <c>保存先.saving</c> 固定で、上書き保存・自動保存・緊急保存が重なると、
        /// 一方がもう一方の一時ファイルを作り直したり消したりした (保存の失敗・内容の取り違え)。
        /// 今は保存ごとに一時ファイルの名前を分け、さらに同じ保存先への書き込みと差し替えを 1 本ずつ通す
        /// (名前を分けただけでは、最後の差し替え同士がぶつかって OS に「アクセスが拒否されました」で拒まれた)。
        /// 保存先は常にどれか 1 回ぶんの完全な内容になる。
        /// </summary>
        [TestMethod]
        public void OverlappingSavesToTheSamePath_DoNotBreakEachOther()
        {
            string path = Path.Combine(_dir, "model.pdj");
            var service = new FileOperationService(SaveOptions());

            var saves = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
            for (int i = 0; i < 8; i++)
            {
                int n = i;
                // 同期と非同期の保存を混ぜる (両方が同じ一時ファイルの作り方を使う)
                saves.Add(n % 2 == 0
                    ? System.Threading.Tasks.Task.Run(() => service.SaveProjectData(path, new InputModel(), null))
                    : System.Threading.Tasks.Task.Run(() => service.SaveProjectDataAsync(path, new InputModel(), null)));
            }

            // どれか 1 つでも失敗したら、その理由を出して落とす
            try { System.Threading.Tasks.Task.WaitAll([.. saves]); }
            catch (AggregateException ex)
            {
                Assert.Fail("重なった保存が失敗しました: " + string.Join(" / ", ex.InnerExceptions.Select(e => e.Message)));
            }

            var loaded = service.LoadProjectData(path);
            Assert.IsNotNull(loaded.InputModel, "保存先が読めるファイルになっていません");
            CollectionAssert.AreEqual(new[] { "model.pdj" },
                Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName),
                "一時ファイルが残っています");
        }

        [TestMethod]
        public void TemporaryFileNamesAreUniqueAndNotAutoSaveCandidates()
        {
            string a = FileOperationService.TempPathFor(@"C:\x\proj_autosave_20260924_120000.pdj");
            string b = FileOperationService.TempPathFor(@"C:\x\proj_autosave_20260924_120000.pdj");
            Assert.AreNotEqual(a, b, "一時ファイルの名前が保存ごとに変わっていません");
            StringAssert.EndsWith(a, ".saving", "一時ファイルの拡張子が .saving でありません (復元候補に拾われるおそれ)");
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
