using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 解析結果セットの保存／復元の検証。
    ///
    /// 編集途中で保存したファイルを開き直したとき、結果が「現在の入力」を基準に
    /// 描かれてしまうと混在表示に戻る。保存側は現在の入力とは別に
    /// 「解析を実行した時点の入力」(ProjectData.ResultInputSnapshot) を持つ。
    /// </summary>
    [TestClass]
    public class AnalysisResultSetPersistenceTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static ProjectData RoundTrip(ProjectData data)
        {
            string json = JsonSerializer.Serialize(data, Options);
            return JsonSerializer.Deserialize<ProjectData>(json, Options)!;
        }

        private static InputModel? LoadExample()
        {
            var (m, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            return m;
        }

        [TestMethod]
        public void Snapshot_SurvivesRoundTrip_AndStaysSeparateFromLiveInput()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            // 解析時の入力を模した別インスタンス（軸力を変えて区別できるようにする）
            var snapshot = LoadExample()!;
            snapshot.PileLayoutItems[0].AxialForceVL0 = 1111.0;
            live.PileLayoutItems[0].AxialForceVL0 = 2222.0;

            var saved = new ProjectData
            {
                FormatVersion = 2,
                InputModel = live,
                ResultInputSnapshot = snapshot,
                ResultCapturedAt = new DateTime(2026, 8, 22, 10, 30, 0),
            };

            var loaded = RoundTrip(saved);

            Assert.IsNotNull(loaded.ResultInputSnapshot, "スナップショットが復元されていない");
            Assert.AreEqual(2222.0, loaded.InputModel.PileLayoutItems[0].AxialForceVL0, 1e-9,
                "現在の入力が壊れている");
            Assert.AreEqual(1111.0, loaded.ResultInputSnapshot!.PileLayoutItems[0].AxialForceVL0, 1e-9,
                "スナップショットが現在の入力で上書きされている");
            Assert.AreNotSame(loaded.InputModel, loaded.ResultInputSnapshot,
                "復元後に同一インスタンスへ潰れている");
            Assert.AreEqual(new DateTime(2026, 8, 22, 10, 30, 0), loaded.ResultCapturedAt);
        }

        /// <summary>
        /// スナップショットを持たない旧ファイルでも読めること（既定値 null）。
        /// </summary>
        [TestMethod]
        public void LegacyFileWithoutSnapshot_LoadsWithNullSnapshot()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var saved = new ProjectData { FormatVersion = 2, InputModel = live };
            string json = JsonSerializer.Serialize(saved, Options);

            // 旧ファイルにはプロパティ自体が無い状態を作る
            Assert.IsFalse(json.Contains("\"ResultInputSnapshot\":{"),
                "スナップショット無しなのに実体が書き出されている");

            var loaded = JsonSerializer.Deserialize<ProjectData>(json, Options)!;
            Assert.IsNull(loaded.ResultInputSnapshot, "旧ファイル相当で null にならない");
            Assert.IsNull(loaded.ResultCapturedAt);
        }

        /// <summary>
        /// 結果 (AnaModel) が参照する入力とスナップショットが同一インスタンスなら、
        /// ReferenceHandler.Preserve により実体は 1 つ ($ref) になり、ファイルは二重化しない。
        /// </summary>
        [TestMethod]
        public void SharedSnapshot_IsNotDuplicatedInFile()
        {
            var live = LoadExample();
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var snapshot = LoadExample()!;

            var modelling = new AnalysisModelling(snapshot);
            var ana = new AnaModel(
                snapshot, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);

            var withSnapshot = new ProjectData
            {
                FormatVersion = 2, InputModel = live, AnaModel = ana, ResultInputSnapshot = snapshot,
            };
            var withoutSnapshot = new ProjectData
            {
                FormatVersion = 2, InputModel = live, AnaModel = ana,
            };

            int lenWith = JsonSerializer.Serialize(withSnapshot, Options).Length;
            int lenWithout = JsonSerializer.Serialize(withoutSnapshot, Options).Length;

            // AnaModel 経由で同じ入力が既に書き出されているので、増えるのは参照 1 個分だけ
            Assert.IsTrue(lenWith - lenWithout < 200,
                $"スナップショットを持たせるとファイルが大きく増えている ({lenWithout} → {lenWith})");
        }

        /// <summary>
        /// ダミー梁を含む解析結果が往復できること。
        ///
        /// DummyBeam は get のみ + 引数付きコンストラクタだったため、System.Text.Json が
        /// コンストラクタ経由で復元しようとし、ReferenceHandler.Preserve の $ref を
        /// コンストラクタ引数へ渡せずに
        /// 「Reference metadata is not supported when deserializing constructor parameters」
        /// で落ちていた。節点は他要素と共有される = 2 個目以降は必ず $ref になるので、
        /// この形のクラスがグラフに混ざると保存ファイルが開けなくなる。
        /// </summary>
        [TestMethod]
        public void DummyBeams_SurviveRoundTrip_WithSharedNodeReferences()
        {
            var input = LoadExample();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var modelling = new AnalysisModelling(input);
            var ana = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);

            // 節点を共有するダミー梁を必ず 1 本は入れる ($ref が発生する状況を作る)
            Assert.IsTrue(ana.Nodes.Count >= 2, "節点が足りない");
            ana.DummyBeams ??= [];
            ana.DummyBeams.Add(new DummyBeam("dummy-test", ana.Nodes[0], ana.Nodes[1]) { Length = 1.5 });

            var loaded = RoundTrip(new ProjectData
            {
                FormatVersion = 2, InputModel = input, AnaModel = ana, ResultInputSnapshot = input,
            });

            var db = loaded.AnaModel.DummyBeams.LastOrDefault(d => d.Name == "dummy-test");
            Assert.IsNotNull(db, "ダミー梁が復元されていない");
            Assert.IsNotNull(db!.NodeI, "ダミー梁の NodeI が復元されていない ($ref が捨てられている)");
            Assert.IsNotNull(db.NodeJ, "ダミー梁の NodeJ が復元されていない");
            Assert.AreEqual(1.5, db.Length, 1e-9);

            // 共有参照が保たれ、コピー内の節点と同一インスタンスであること
            Assert.AreSame(loaded.AnaModel.Nodes[0], db.NodeI, "節点の共有参照が保たれていない");
            Assert.AreSame(loaded.AnaModel.Nodes[1], db.NodeJ, "節点の共有参照が保たれていない");
        }

        /// <summary>
        /// 杭要素分割の状態が保存・復元されること。
        ///
        /// 以前は読込時に「AnaModel に節点があるか」で推定していた。解析結果を保持したまま
        /// 分割だけ取り消せるようになった今、その推定では分割を取り消した状態が復元できず、
        /// 開き直すと分割済みに戻ってメイン画面の杭が「分割後」の色 (青) で描かれてしまう。
        /// </summary>
        [TestMethod]
        public void ElementSplitState_SurvivesRoundTrip()
        {
            var input = LoadExample();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            foreach (bool split in new[] { true, false })
            {
                var loaded = RoundTrip(new ProjectData
                {
                    FormatVersion = 2, InputModel = input, IsElementSplit = split,
                });
                Assert.AreEqual(split, loaded.IsElementSplit,
                    $"杭要素分割の状態 ({split}) が往復で保たれていない");
            }

            // 旧ファイル相当: 値が無ければ null（読込側で従来どおり推定する）
            var legacy = RoundTrip(new ProjectData { FormatVersion = 2, InputModel = input });
            Assert.IsNull(legacy.IsElementSplit, "旧ファイル相当で null にならない");
        }

        /// <summary>
        /// 実際の保存/読込サービス経由でも杭要素分割の状態が往復すること。
        /// (ProjectData の往復だけでなく、引数の受け渡しまで含めて確認する)
        /// </summary>
        [TestMethod]
        public void ElementSplitState_SurvivesSaveLoadThroughService()
        {
            var input = LoadExample();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var modelling = new AnalysisModelling(input);
            var ana = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);

            // 実機と同じシリアライザ設定
            var service = new PileDesign.Services.FileOperationService(new JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });
            string path = Path.Combine(Path.GetTempPath(), $"pd_split_{Guid.NewGuid():N}.pdj");
            try
            {
                // 解析結果は保持しつつ、杭要素分割は取り消した状態を保存する
                service.SaveProjectData(path, input, ana, null, input, DateTime.Now, null, isElementSplit: false);

                var loaded = service.LoadProjectData(path);
                Assert.IsNotNull(loaded, "読み込めていない");
                Assert.IsNotNull(loaded!.AnaModel, "解析結果が保存されていない (前提の確認)");
                Assert.AreEqual(false, loaded.IsElementSplit,
                    "杭要素分割の取り消しが保存されていない (開き直すと分割済みに戻り杭が青くなる)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// 逆シリアライズ後の AnaModel は入力への参照を失う (getter のみのため)。
        /// RebindInputModel で張り直せること。
        /// </summary>
        [TestMethod]
        public void AnaModel_InputModelReference_CanBeRebound()
        {
            var snapshot = LoadExample();
            if (snapshot == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var modelling = new AnalysisModelling(snapshot);
            var ana = new AnaModel(
                snapshot, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            Assert.AreSame(snapshot, ana.InputModel);

            var loaded = RoundTrip(new ProjectData
            {
                FormatVersion = 2, InputModel = snapshot, AnaModel = ana, ResultInputSnapshot = snapshot,
            });

            Assert.IsNull(loaded.AnaModel.InputModel,
                "getter のみのプロパティが復元されている（前提が変わったらこのテストを見直す）");

            loaded.AnaModel.RebindInputModel(loaded.ResultInputSnapshot!);
            Assert.AreSame(loaded.ResultInputSnapshot, loaded.AnaModel.InputModel,
                "入力への参照を張り直せていない");
        }
    }
}
