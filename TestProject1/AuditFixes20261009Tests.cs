using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 に利用者の了解を得て直した、網羅的な見直しの残り (解析・検定・計算書の数値が変わるものを含む)。
    /// </summary>
    [TestClass]
    public class AuditFixes20261009Tests
    {
        /// <summary>沈下種別・グラフ・凡例の題は、単杭か群杭か・どの解析かが分かる名前で出す (コードの中の値は変えない)。</summary>
        [TestMethod]
        public void SettlementTypes_AreShownWithTheirAnalysis()
        {
            Assert.AreEqual("単杭", SettlementTypeNames.Type("単杭"));
            Assert.AreEqual("群杭（基礎梁なし）", SettlementTypeNames.Type("群杭"));
            Assert.AreEqual("単杭＋群杭（基礎梁なし）", SettlementTypeNames.Type("単杭+群杭"));
            Assert.AreEqual("単杭（基礎梁考慮）", SettlementTypeNames.Type("基礎梁考慮"));
            Assert.AreEqual("単杭（基礎梁考慮）＋群杭（基礎梁なし）", SettlementTypeNames.Type("基礎梁考慮+群杭"));
            Assert.AreEqual("沈下 群杭（基礎梁あり）", SettlementTypeNames.Graph("沈下 杭矩形(基礎梁考慮)"));
            Assert.AreEqual("沈下 単杭（基礎梁考慮）＋群杭（基礎梁なし）", SettlementTypeNames.Graph("沈下 基礎梁考慮単杭+群杭"));
            Assert.AreEqual("単杭（基礎梁考慮）＋群杭（基礎梁なし）の沈下部材角", SettlementTypeNames.Title("基礎梁考慮+群杭沈下部材角"),
                "長い種別から照合していません (「基礎梁考慮」と取り違える)");
            Assert.AreEqual("単杭（基礎梁考慮）の反力（地盤）", SettlementTypeNames.Title("基礎梁考慮反力（地盤）"));
            Assert.AreEqual("単杭沈下", SettlementTypeNames.Title("単杭沈下"));

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(main, System.Text.RegularExpressions.Regex.Escape(
                "<TextBlock Text=\"{Binding Converter={StaticResource SettlementTypeDisplayConverter}}\"/>")).Count,
                "沈下種別の 2 つのコンボが表示名に変えていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "GraphWindow.xaml"), "ConverterParameter=Graph");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasCore.cs"), "SettlementTypeNames.Title(");
        }

        /// <summary>
        /// 前方杭・後方杭はレベルごとに持つ (以前はレベル2 もレベル1 の判定で解いていた)。
        /// レベル2 の判定を持たない旧ファイルは、レベル1 の値を写す (それまでの結果を変えない)。
        /// </summary>
        [TestMethod]
        public void FrontPiles_AreHeldPerLevel()
        {
            var pile = new PileLayoutDataItem
            {
                IsFrontPiles = new ObservableCollection<bool> { true, false, true, true },
                IsFrontPilesLevel2 = new ObservableCollection<bool> { false, true, false, false },
            };
            Assert.IsTrue(pile.IsFrontAt(1, 0));
            Assert.IsFalse(pile.IsFrontAt(2, 0), "レベル2 のケースでレベル1 の判定を引いています");
            Assert.IsTrue(pile.IsFrontFor(new LoadCase { Level = 2, No = 2 }));
            Assert.IsFalse(pile.IsFrontFor(new LoadCase { Level = 1, No = 2 }));

            // 旧ファイル (レベル2 の判定が無い) はレベル1 を写す。ある新しいファイルは写さない
            var old = System.Text.Json.JsonSerializer.Deserialize<PileLayoutDataItem>("{\"IsFrontPiles\":[false,true,true,false]}")!;
            Assert.IsTrue(old.FillLevel2FrontFlagsFromLevel1IfMissing());
            CollectionAssert.AreEqual(new[] { false, true, true, false }, old.IsFrontPilesLevel2.ToArray());
            var current = System.Text.Json.JsonSerializer.Deserialize<PileLayoutDataItem>(
                "{\"IsFrontPiles\":[false,true,true,false],\"IsFrontPilesLevel2\":[true,true,true,true]}")!;
            Assert.IsFalse(current.FillLevel2FrontFlagsFromLevel1IfMissing(), "レベル2 を持つファイルの値を上書きしています");

            CollectionAssert.AreEqual(pile.IsFrontPilesLevel2.ToArray(), pile.QuickClone().IsFrontPilesLevel2.ToArray(), "複製でレベル2 の判定が落ちます");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs"),
                "pileLayoutItem.IsFrontAt(model.CaseLevel, iLC)", "解析がケースのレベルの判定を使っていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml"), "Binding=\"{Binding IsFrontPilesLevel2[0]}\"");
        }

        /// <summary>代表 Fc・E0=700N のボタンは礫質土も対象にする (以前は存在しない分類名「砂礫土」で判定していた)。</summary>
        [TestMethod]
        public void SoilButtons_UseTheExistingClassNames()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs");
            Assert.IsFalse(src.Contains("\"砂礫土\""), "存在しない分類名「砂礫土」で判定しています");

            // 地表面加速度・算定法を変えたら FL・βL・地盤変位を計算し直す (以前は Enter を押したときだけ)
            StringAssert.Contains(src, "nameof(GroundInput.GroundAcceleration1),");
            StringAssert.Contains(src, "nameof(GroundInput.GroundAcceleration2),");
            Assert.IsFalse(src.Contains("// nameof(GroundInput.GroundAcceleration1)"));
        }

        /// <summary>基礎梁の角度β は解析の梁要素に渡す (以前は描画・書き出しにだけ効いていた)。</summary>
        [TestMethod]
        public void TheBeamAngle_ReachesTheAnalysis()
        {
            var (input, _) = IterativeBeamSettlementInvariantTests.Scene();   // 計算例5 (基礎梁つき)
            if (input == null) return;
            Assert.IsTrue(input.FoundationBeamInput.Beams.Count > 1, "前提: 基礎梁が 2 本以上ある");
            input.FoundationBeamInput.Beams[0].AngleBeta = 90.0;
            var modelling = new AnalysisModelling(input);
            var beam = modelling.Beams.First(b => b.Name == "FoundationBeam-1");
            Assert.AreEqual(90.0, beam.CoordAngle, 1e-12, "角度β が解析の梁要素に渡っていません");
            var other = modelling.Beams.First(b => b.Name == "FoundationBeam-2");
            Assert.AreEqual(0.0, other.CoordAngle, 1e-12);
            Assert.AreEqual(90.0, beam.DeepCopy().CoordAngle, 1e-12, "複製で角度β が落ちます");

            // 回した梁の座標変換は、回さない梁と違う (局所 y・z が入れ替わる)
            var t0 = new Beam("t", beam.Section, beam.NodeI, beam.NodeJ, 1, 1).GetCachedCoordTransform();
            var t90 = beam.GetCachedCoordTransform();
            Assert.IsTrue((t0 - t90).FrobeniusNorm() > 1e-6);
        }

        /// <summary>
        /// 杭の沈下量の検定は、群杭沈下の常時 (VL) の記録を使い、「沈下検討の対象」が単杭沈下だけなら群杭ぶんを足さない。
        /// 以前は画面で表示中のケースの値を足していた。
        /// </summary>
        [TestMethod]
        public void PileSettlement_UsesTheLongTermGroupRecord()
        {
            var input = new InputModel();
            input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
            input.FundamentalInput ??= new FundamentalInput();
            input.FundamentalInput.EvaluateSettlement = true;
            input.FundamentalInput.AllowableSettlement_mm = 100;
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 1, No = 1, PileBodyNo = 1, SinglePileSettlementVL = 0.010 });
            var soilPile = new SoilPile();
            soilPile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
            soilPile.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, D0s = 5 });
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles = [soilPile];
            input.PileLayoutItems[0].SoilPileAltNo = 1;
            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords =
            [
                new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = "任意矩形", IsConverged = true, PileSettlements_mm = new Dictionary<int, double> { [1] = 20 } },
                new GroupSettlementCaseRecord { LoadCaseName = "L1-1", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true, IsConverged = true, PileSettlements_mm = new Dictionary<int, double> { [1] = 70 } },
            ];
            input.PileGroupSettlement.ActiveCaseIndex = 1;   // 表示は地震時のケース

            var item = PileSettlementEvaluator.Evaluate(input).Single();
            Assert.AreEqual(10 + 20, item.Response, 1e-9, "表示中のケースではなく、常時 (VL) の群杭沈下を足すこと");

            input.FundamentalInput.SettlementDesignIncludesGroup = false;
            Assert.AreEqual(10, PileSettlementEvaluator.Evaluate(input).Single().Response, 1e-9,
                "「沈下検討の対象」が単杭沈下だけなのに群杭ぶんを足しています");
        }

        /// <summary>
        /// 計算書出力ウィンドウのチェックは「計算書に載せるか」だけを変え、解析の対象・適用は変えない。
        /// 荷重の画面の OK で、組合せの「対象」を ON に戻さない。
        /// </summary>
        [TestMethod]
        public void ReportSelection_DoesNotChangeWhatIsAnalyzed()
        {
            var lc = new LoadCase { Level = 1, No = 1, IsApplicable = true, IsAnalyzed = true };
            lc.IsApplicableForDocxDisplay = false;
            Assert.IsTrue(lc.IsApplicable, "計算書から外しただけで、解析の適用まで外れました");
            Assert.IsFalse(lc.IsIncludedInReport);
            Assert.IsFalse(lc.IsReported);

            var combo = new LoadCombination(1, 1, 1, 1) { IsApplicable = true, IsAnalyzed = true };
            combo.IsApplicableForDocxDisplay = false;
            Assert.IsTrue(combo.IsApplicable);
            Assert.IsFalse(combo.IsReported);

            // 解析時の入力 (別のインスタンス) へ写す
            var now = new LoadCasesInput();
            var then = new LoadCasesInput();
            now.LoadCasesLevel1 = [new LoadCase { Level = 1, No = 1, IsApplicable = true }];
            then.LoadCasesLevel1 = [new LoadCase { Level = 1, No = 1, IsApplicable = true }];
            now.LoadCasesLevel1[0].IsIncludedInReport = false;
            then.CopyReportSelectionFrom(now);
            Assert.IsFalse(then.LoadCasesLevel1[0].IsReported, "計算書の選択が解析時の入力へ写っていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "inputForReport?.LoadCasesInput?.CopyReportSelectionFrom(CurrentInputModel?.LoadCasesInput);");
            string charts = TestSource.Read("Graphics_r1", "Output", "WordDocument.Charts.cs");
            Assert.IsFalse(charts.Contains("!loadCase.IsApplicable)"), "計算書が解析の適用だけで絞り込んでいます");

            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "LoadCaseViewModel.cs"),
                "LoadCombinations = KeepCombinationSelection(GetCombinations(_loadCombinationFactor));", "荷重の画面を開くたびに組合せの「対象」を ON に戻しています");
        }

        /// <summary>計算書の支持力・沈下量の件数で、検定できなかった項目を NG に数えない。杭体の検定は、その杭の土層-杭セットの断面で判定する。</summary>
        [TestMethod]
        public void ReportCounts_AndPileSections()
        {
            string summary = TestSource.Read("Graphics_r1", "Output", "WordDocument.SummaryTables.cs");
            Assert.IsFalse(summary.Contains("int ng = items.Count(i => !i.IsOk);"), "検定できなかった項目を NG に数えています");
            string eval = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(eval, System.Text.RegularExpressions.Regex.Escape("SoilPileForBeam(pileItem, pb, soilPileByPileBodyNo)")).Count,
                "杭体の曲げ・せん断の検定が、その杭の土層-杭セットの断面を引いていません");
            StringAssert.Contains(eval, "SectionOwnerKey(soilPile, pb)", "曲線のキャッシュの鍵が杭体番号のままです (別のセットの曲線と取り違える)");
        }
    }
}
