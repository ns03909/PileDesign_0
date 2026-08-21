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
