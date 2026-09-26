using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 解析結果の保存・復元の抜け 2 件 (2026-09-26 のレビュー)。
    /// - 水平解析のモデルが無いと、解析時の入力 (ResultInputSnapshot) を書いていなかった。沈下・基礎梁の鉛直解析の
    ///   結果は別に保存されるので、それだけを解いてから入力を編集して保存すると、読み直したとき結果が編集後の入力に対応付いた
    /// - 杭と FEM 要素の対応表の、範囲の外の番号・重複した杭番号を黙って扱っていた
    /// </summary>
    [TestClass]
    public class ResultPersistenceGapTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static InputModel Example()
        {
            var (m, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (m == null) Assert.Inconclusive(error);
            return m!;
        }

        private static ProjectData SaveAndRead(InputModel live, AnaModel? ana, List<VerticalBeamCaseResult>? vbcr, InputModel snapshot)
        {
            var prepared = new FileOperationService(Options).PrepareSave(live, ana, vbcr, snapshot,
                new DateTime(2026, 9, 26, 12, 0, 0), inputChangedSinceAnalysis: true);
            Assert.IsNull(prepared.Error, prepared.Error?.ToString());
            return JsonSerializer.Deserialize<ProjectData>(prepared.Payload!, Options)!;
        }

        [TestMethod]
        public void TheAnalysisInputIsSavedWithSettlementOrBeamResultsAlone()
        {
            var live = Example();
            var snapshot = Example();
            snapshot.PileLayoutItems[0].AxialForceVL0 = 1111.0;
            live.PileLayoutItems[0].AxialForceVL0 = 2222.0;   // 解析のあとに編集した

            // 水平解析のモデルは無く、基礎梁の鉛直解析の結果だけがある
            var loaded = SaveAndRead(live, null, [new VerticalBeamCaseResult()], snapshot);

            Assert.IsNotNull(loaded.ResultInputSnapshot, "水平解析のモデルが無いと、解析時の入力が保存されません");
            Assert.AreEqual(1111.0, loaded.ResultInputSnapshot!.PileLayoutItems[0].AxialForceVL0, 1e-9);
            Assert.AreEqual(true, loaded.InputChangedSinceAnalysis, "解析後に編集した記録が保存されません");
            Assert.AreEqual(new DateTime(2026, 9, 26, 12, 0, 0), loaded.ResultCapturedAt);
            Assert.IsNull(loaded.PileFemLinks, "水平解析のモデルが無いのに要素の対応表を書いています");
        }

        [TestMethod]
        public void NoResultsMeansNoAnalysisInput()
        {
            var loaded = SaveAndRead(Example(), null, null, Example());
            Assert.IsNull(loaded.ResultInputSnapshot, "解析結果を何も保存しないのに解析時の入力を書いています");
            Assert.IsNull(loaded.InputChangedSinceAnalysis);
        }

        /// <summary>保存の本体が、どの結果を持つかで判定していること (水平解析のモデルだけで決めない)。</summary>
        [TestMethod]
        public void TheSnapshotConditionCoversEveryKindOfResult()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Services", "FileOperationService.cs"),
                "internal PreparedSave PrepareSave(");
            StringAssert.Contains(body, "ResultInputSnapshot = savesAnyResult ? resultInputSnapshot : null");
            foreach (var kind in new[] { "anaModel != null", "singlePileSettlement != null", "groupSettlement != null", "verticalBeam != null" })
                StringAssert.Contains(body, kind, $"解析結果の有無の判定に {kind} がありません");
        }

        // ── 杭と FEM 要素の対応表 ───────────────────────────

        private static (InputModel input, AnaModel model) Fixture(int piles = 2)
        {
            var input = new InputModel
            {
                PileLayoutItems = new(Enumerable.Range(1, piles).Select(i => new PileLayoutDataItem { No = i })),
            };
            var model = new AnaModel
            {
                Nodes = [new Node(), new Node(), new Node()],
                Beams = [new Beam(), new Beam()],
                HorizontalSoilSprings = [new HorizontalSoilSpring()],
                RotationalSprings = [new RotationalSpring()],
            };
            return (input, model);
        }

        private static PileFemLink Link(int no, params int[] beams) => new()
        {
            PileNo = no, BeamIndices = [.. beams], PileNodeIndices = [0], RotationalSpringIndex = -1,
        };

        [TestMethod]
        public void AConsistentTableHasNoProblems()
        {
            var (input, model) = Fixture();
            var table = new PileFemLinkTable { Piles = [Link(1, 0), Link(2, 1)] };
            Assert.AreEqual(0, PileFemLinkTable.Apply(table, input, model).Count);
            Assert.AreSame(model.Beams[1], input.PileLayoutItems[1].Beams.Single());
        }

        [TestMethod]
        public void OutOfRangeIndicesAreReported()
        {
            var (input, model) = Fixture();
            var bad = Link(2, 1, 7);
            bad.RotationalSpringIndex = 5;
            var problems = PileFemLinkTable.Apply(new PileFemLinkTable { Piles = [Link(1, 0), bad] }, input, model);

            Assert.AreEqual(1, problems.Count, string.Join(" / ", problems));
            StringAssert.Contains(problems[0], "杭 No.2");
            StringAssert.Contains(problems[0], "杭要素 7 (全 2 個)");
            StringAssert.Contains(problems[0], "杭頭回転ばね 5 (全 1 個)");
            Assert.AreSame(model.Beams[1], input.PileLayoutItems[1].Beams.Single(), "範囲内の要素まで捨てています");
        }

        [TestMethod]
        public void DuplicatedPileNumbersAreNotLinkedAndAreReported()
        {
            var (input, model) = Fixture();
            var problems = PileFemLinkTable.Apply(new PileFemLinkTable { Piles = [Link(1, 0), Link(2, 1), Link(2, 0)] }, input, model);

            Assert.IsTrue(problems.Any(p => p.Contains("杭 No.2") && p.Contains("重複")), string.Join(" / ", problems));
            Assert.AreEqual(0, input.PileLayoutItems[1].Beams.Count, "重複した対応のどちらかを黙って使っています");
            Assert.AreSame(model.Beams[0], input.PileLayoutItems[0].Beams.Single(), "重複の無い杭まで結び付けていません");
        }

        [TestMethod]
        public void MissingAndExtraPilesAreReported()
        {
            var (input, model) = Fixture(piles: 2);
            var problems = PileFemLinkTable.Apply(new PileFemLinkTable { Piles = [Link(1, 0), Link(3, 1)] }, input, model);

            Assert.IsTrue(problems.Any(p => p.Contains("杭 No.2") && p.Contains("対応がありません")), string.Join(" / ", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("杭 No.3") && p.Contains("この杭がありません")), string.Join(" / ", problems));
        }

        [TestMethod]
        public void TheLoadMessageListsTheProblems()
        {
            var problems = Enumerable.Range(1, 12).Select(i => $"杭 No.{i}: 解析結果との対応がありません").ToList();
            string message = PileFemLinkTable.DescribeProblems(problems);
            StringAssert.Contains(message, "再解析すると揃います");
            StringAssert.Contains(message, "杭 No.10:");
            Assert.IsFalse(message.Contains("杭 No.11:"));
            StringAssert.Contains(message, "ほか 2 件");

            string load = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            StringAssert.Contains(load, "PileFemLinkTable.DescribeProblems(", "読込の仕上げで対応表の問題を知らせていません");
        }
    }
}
