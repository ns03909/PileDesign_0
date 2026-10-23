using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-24 の見直し (杭体ウィンドウの支持力の誤りと同じ型の誤りを計算書・画面で探した) で直したもの。
    /// </summary>
    [TestClass]
    public class AuditFixes20261024Tests
    {
        private static InputModel BuildSteelPipeExample()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (input == null) throw new AssertInconclusiveException("例題ファイルなし: " + err);
            return input;
        }

        /// <summary>
        /// 鋼管杭の座屈長 (液状化区間) は、検定・解析が読む土層-杭セットの断面の複写にも入る。
        /// 以前は杭体の断面にだけ書いたので、セットを作った直後は複写が前の値 (初回は 0) のままで、
        /// 柱座屈による許容応力度の低減が入らなかった (設計例集3.5 で杭体 5.13 m・複写 0 m)。
        /// </summary>
        [TestMethod]
        public void TheBucklingLength_ReachesTheSectionsTheCheckReads()
        {
            var input = BuildSteelPipeExample();
            double bodyLk = input.PileBodies[0].PileBodySegments[0].PileSection!.BucklingLength;
            Assert.IsTrue(bodyLk > 0, "(前提) 液状化区間がありません");
            foreach (var sp in input.ElementDivision.SoilPiles)
                foreach (var seg in sp.PileBodySegments)
                    Assert.AreEqual(bodyLk, seg.PileSection!.BucklingLength, 1e-9,
                        $"土層-杭セット (杭頭Z {sp.Z:F2}) の断面に座屈長が入っていません (検定は柱座屈の低減なしになる)");

            // 解析の入口で求め直すときも、複写に入る
            foreach (var seg in input.ElementDivision.SoilPiles[0].PileBodySegments) seg.PileSection!.BucklingLength = 0;
            input.RefreshSteelPipeBucklingLengths();
            Assert.AreEqual(bodyLk, input.ElementDivision.SoilPiles[0].PileBodySegments[0].PileSection!.BucklingLength, 1e-9);
        }

        /// <summary>
        /// 土層-杭セットが見つからないときの代わりの組み立ても、解析と同じ組み立て (SoilPileNodeAltitudes) を通す。
        /// 支持力の検定・応力図の限界線・基礎梁考慮の沈下は、代わりの組み立てに落ちず、解析と同じセット (SoilPileAltNo) を引く。
        /// </summary>
        [TestMethod]
        public void NothingFallsBackToARebuiltSoilPileSet()
        {
            var input = BuildSteelPipeExample();
            var pile = input.PileLayoutItems[0];
            var fallback = (SoilPile)typeof(PileLayoutDataItem)
                .GetMethod("CreateTemporarySoilPile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(pile, null)!;
            var body = input.PileBodyAt(pile.PileBodyNo)!;
            CollectionAssert.AreEqual(
                InputModel.SoilPileNodeAltitudes(body, input.GroundAt(pile.GroundNo)!.GroundLayers, pile.PileHeadZ).ToArray(),
                fallback.ZDataItems.Select(z => z.Z).ToArray(), "代わりの組み立てが、解析の組み立てと違う節点を作っています");

            // 対応が無い杭は、支持力を組み立て直したセットで判定せず、判定できない項目にする
            Assert.AreSame(pile.SoilPileAt(input), PileBearingEvaluator.SoilPileFor(input, pile), "解析と違うセットで検定しています");
            int saved = pile.GroundNo;
            pile.GroundNo = 99;
            try { Assert.IsNull(PileBearingEvaluator.SoilPileFor(input, pile), "対応の無い杭を、組み立て直したセットで判定しています"); }
            finally { pile.GroundNo = saved; }

            foreach (var (folder, file) in new[]
            {
                ("Output", "WordDocument.Charts.cs"), ("ViewModels", "GraphViewModel.ElevationGraphs.cs"),
                ("ViewModels", "VerticalBeamCalculationViewModel.cs"), ("FEM", "VerticalBeamModelling.cs"),
            })
            {
                string code = TestSource.Read("Graphics_r1", folder, file);
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(code, @"\.SoilPile;"),
                    $"{file} が、組み立て直すことのある PileLayoutDataItem.SoilPile を引いています (SoilPileAt を使う)");
            }
        }

        /// <summary>杭頭レベルが複数あるときだけ、計算書の表・図の見出しに杭頭Z を添える (1 つなら見出しを変えない)。</summary>
        [TestMethod]
        public void ReportHeadings_TellThePileHeadLevelsApart()
        {
            var input = BuildSteelPipeExample();
            var one = input.ElementDivision.SoilPiles.ToList();
            Assert.IsFalse(WordDocument.HasSeveralPileHeadLevels(one));
            Assert.AreEqual("", WordDocument.PileHeadLabel(one, one[0]), "杭頭レベルが 1 つなのに見出しを変えています");

            PileHeadLevelTests.TwoPileHeadLevels(input);
            var two = input.ElementDivision.SoilPiles.ToList();
            Assert.IsTrue(WordDocument.HasSeveralPileHeadLevels(two));
            var labels = two.Select(sp => WordDocument.PileHeadLabel(two, sp)).ToList();
            Assert.AreEqual(two.Count, labels.Distinct().Count(), "杭頭レベルの違うセットの見出しが同じです");
            StringAssert.StartsWith(labels[0], "杭頭Z ");

            string code = string.Concat(new[] { "WordDocument.cs", "WordDocument.InputTables.cs", "WordDocument.PileDiagrams.cs", "WordDocument.ElementDivisionGraphs.cs" }
                .Select(f => TestSource.Read("Graphics_r1", "Output", f)));
            TestSource.AssertScanned(System.Text.RegularExpressions.Regex.Matches(code, @"PileHeadSuffix\(|PileBodyCellLines\(|PileHeadLabel\(").Count, 10, "杭頭Z を添える箇所");
        }

        /// <summary>チャンの窓で本体のモデルを読み込むと、杭ごとにその杭の杭体の組を当てる (以前はどの杭にも杭体 1)。</summary>
        [TestMethod]
        public void TheChangWindow_GivesEachPileItsOwnPileBody()
        {
            var input = BuildSteelPipeExample();
            if (input.PileBodies.Count < 2)
            {
                input.PileBodies.Add(input.PileBodies[0].DeepCopy());
                input.PileLayoutItems[^1].PileBodyNo = 2;
            }
            var vm = new ChangViewModel(input);
            vm.ApplyInputModel();
            for (int i = 0; i < input.PileLayoutItems.Count; i++)
            {
                int body = input.PileLayoutItems[i].PileBodyNo;
                Assert.AreEqual(body, vm.Changs[i].SelectedSoilPileIndex, $"杭{i + 1} に杭体{body} の組を当てていません");
                Assert.AreSame(vm.ChangSoilPiles[body - 1], vm.Changs[i].AssignedSoilPile);
            }
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "ChangWindow.xaml"), "概算用のサブプログラムです",
                "地盤の入力を使わない概算であることを示していません");
        }

        /// <summary>メイン画面の沈下の変形図は、荷重の矢印と同じく荷重ケースそのもので軸力を引く (名前の一致ではなく)。</summary>
        [TestMethod]
        public void TheSettlementView_FindsTheAxialForceByTheCase_NotByName()
        {
            string code = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsVertical.cs"),
                "private static double? GetSelectedCaseAxialForce(");
            StringAssert.Contains(code, "LoadCase.IsSameCase(selected, cases[i])");
            StringAssert.Contains(code, "case \"VL0\"");
            StringAssert.Contains(code, "case \"VLadd\"");
            Assert.IsFalse(code.Contains(".LoadName == caseName"), "名前の一致で軸力を引いています");
        }
    }
}
