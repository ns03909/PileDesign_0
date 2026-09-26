using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 検定が、対象にすべき条件をすべて検定し、検定できなかったものを黙って落とさないこと。
    ///
    /// <list type="bullet">
    /// <item>沈下による杭頭変形角: 「単杭沈下のみ」でも、単杭沈下だけを計算すれば検定する (以前は群杭沈下の記録が無いと出なかった)。</item>
    /// <item>荷重条件の識別: 名前ではなく、レベル・荷重ケース番号・組合せ番号で分ける (以前は名前が空欄の計算例で
    ///   レベル1 とレベル2 が 1 つにまとめられ、レベル1 が検定されていなかった)。</item>
    /// <item>検定できなかった項目: 対象なのにデータが欠けたものは「検定不能」の項目として理由を残す (以前は項目を作らなかった)。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class EvaluationCoverageTests
    {
        // ── 沈下による杭頭変形角 ─────────────────────────────

        /// <summary>杭 3 本 (X = 0, 5, 10 m)。<paramref name="singleDone"/> の杭だけ単杭沈下解析の結果を持つ。</summary>
        private static InputModel SettlementModel(bool includesGroup, bool[] singleDone, double[] single_m)
        {
            var soilPiles = new ObservableCollection<SoilPile>();
            var piles = new ObservableCollection<PileLayoutDataItem>();
            for (int i = 0; i < 3; i++)
            {
                var sp = new SoilPile();
                if (singleDone[i]) sp.LoadDisplacements = [new VerticalLoadTransferMethod.LoadDisplacement()];
                soilPiles.Add(sp);
                piles.Add(new PileLayoutDataItem { No = i + 1, PileNo = i + 1, X = 5.0 * i, Y = 0, SoilPileAltNo = i + 1, SinglePileSettlementVL = single_m[i] });
            }
            return new InputModel
            {
                PileLayoutItems = piles,
                ElementDivision = new ElementDivision { SoilPiles = soilPiles },
                FundamentalInput = new FundamentalInput { SettlementDesignIncludesGroup = includesGroup },
            };
        }

        [TestMethod]
        public void SinglePileSettlementAloneIsEvaluated()
        {
            // 群杭沈下は実行していない。2-3 の組が最大: |0.004 - 0.009| / 5 = 1.0e-3
            var input = SettlementModel(includesGroup: false, [true, true, true], [0.001, 0.004, 0.009]);
            var items = SettlementDeformationAngleEvaluator.Evaluate(input);

            Assert.AreEqual(1, items.Count, "単杭沈下のみの設定で、単杭沈下だけを計算したのに検定が出ていません");
            Assert.AreEqual(1.0e-3, items[0].Response, 1e-12);
            Assert.IsTrue(items[0].IsJudged);
            StringAssert.Contains(items[0].TargetName, "杭No.2 − 杭No.3");
        }

        [TestMethod]
        public void NothingIsEvaluatedWhenNoSettlementAnalysisWasRun()
        {
            var input = SettlementModel(includesGroup: false, [false, false, false], [0, 0, 0]);
            Assert.AreEqual(0, SettlementDeformationAngleEvaluator.Evaluate(input).Count,
                "沈下解析をしていないのに項目を作っています (単杭沈下量 0 を結果として読んだ?)");
        }

        [TestMethod]
        public void MissingResultsAreReportedNotSkipped()
        {
            // 単杭沈下の結果が 1 本分しかない → 変形角を求められない
            var single = SettlementDeformationAngleEvaluator.Evaluate(SettlementModel(false, [true, false, false], [0.001, 0, 0]));
            Assert.AreEqual(1, single.Count);
            Assert.IsTrue(single[0].IsUnavailable, "結果が足りないのに、検定不能として知らせていません");
            StringAssert.Contains(single[0].UnavailableReason, "No.2, 3");

            // 単杭＋群杭の設定で、群杭沈下を実行していない
            var group = SettlementDeformationAngleEvaluator.Evaluate(SettlementModel(true, [true, true, true], [0.001, 0.002, 0.003]));
            Assert.AreEqual(1, group.Count);
            Assert.IsTrue(group[0].IsUnavailable);
            StringAssert.Contains(group[0].UnavailableReason, "群杭沈下の結果がありません");
        }

        /// <summary>
        /// 基礎梁を考慮した反復が収束しなかったケースの沈下からは、変形角を判定しないこと (「未収束」)。
        /// 反復しない解析の記録は収束状態を持たないので、そのまま判定する。
        /// </summary>
        [TestMethod]
        public void AnUnconvergedIterativeCaseIsNotJudged()
        {
            var input = SettlementModel(includesGroup: true, [true, true, true], [0, 0, 0]);
            input.PileGroupSettlement = new PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords.Add(new PileDesign.Models.Results.GroupSettlementCaseRecord
            {
                LoadCaseName = "VL", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true, IsConverged = false,
                PileSettlements_mm = new() { [1] = 1, [2] = 3, [3] = 9 },
            });
            input.PileGroupSettlement.CaseRecords.Add(new PileDesign.Models.Results.GroupSettlementCaseRecord
            {
                LoadCaseName = "VL", LoadingType = "個別十字", IsBeamAware = false,
                PileSettlements_mm = new() { [1] = 1, [2] = 3, [3] = 9 },
            });

            var items = SettlementDeformationAngleEvaluator.Evaluate(input);
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items[0].IsFromUnconvergedCase && !items[0].IsJudged, "収束しなかった反復のケースを判定しています");
            Assert.IsTrue(items[1].IsJudged, "反復しない解析の記録まで未収束扱いにしています");
        }

        /// <summary>沈下による変形角は、水平解析の検定 (低減前・低減後) には入れない (以前は両方に同じ項目が並んだ)。</summary>
        [TestMethod]
        public void TheSettlementAngleIsNotPartOfTheHorizontalEvaluation()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            Assert.IsFalse(src.Contains("EvaluateSettlementDeformationAngle", StringComparison.Ordinal),
                "沈下による杭頭変形角が、水平解析の検定の中に戻っています");
            foreach (var (dir, file) in new[] { ("ViewModels", "MainWindowViewModel.ResultsWindows.cs"), ("Services", "PileEvaluationSummary.cs"), ("Output", "WordDocument.SummaryTables.cs") })
                StringAssert.Contains(TestSource.Read("Graphics_r1", dir, file), "SettlementDeformationAngleEvaluator.Evaluate(",
                    $"{file}: 沈下による杭頭変形角の検定を出していません");
        }

        // ── 荷重条件の識別・検定不能 (計算例9 を解析して確かめる) ─────────────────

        private static MainWindowViewModel? _analyzed;
        private static readonly object Gate = new();

        private static MainWindowViewModel Analyzed()
        {
            lock (Gate)
            {
                if (_analyzed != null) return _analyzed;
                try
                {
                    _analyzed = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                        LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
                {
                    Assert.Inconclusive("例題ファイルなし");
                }
                return _analyzed!;
            }
        }

        /// <summary>
        /// 荷重ケース名がすべて空欄でも、レベル1 とレベル2 の両方を検定すること。
        /// 以前は名前で条件をまとめていたので 1 つにまとめられ、ステップの多いレベル2 だけが検定されていた。
        /// </summary>
        [TestMethod]
        public void BothLevelsAreEvaluatedEvenWithBlankCaseNames()
        {
            var vm = Analyzed();
            foreach (var lc in vm.CurrentModel!.AnalysisStepResults.Select(s => s.LoadCase).Distinct()) lc.LoadName = "";

            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var conditions = vm.CurrentModel.AnalysisStepResults
                .Select(s => AnaModel.CaseConvergenceKey(s.LoadCase, s.LoadCombination, s.IsLiquefaction)).Distinct().ToList();
            Assert.IsTrue(conditions.Select(c => c.Level).Distinct().Count() >= 2, "(前提) レベル1 とレベル2 の解析結果があること");

            foreach (var c in conditions.Where(c => c.Level is 1 or 2))
                Assert.IsTrue(result.Items.Any(i => i.Level == c.Level && i.LoadCaseNo == c.LoadCaseNo && i.LoadCombinationNo == c.LoadCombinationNo),
                    $"レベル{c.Level} の荷重ケース {c.LoadCaseNo}・組合せ {c.LoadCombinationNo} が検定されていません (名前で別の条件とまとめられた?)");
        }

        /// <summary>対象なのに解析結果が欠けた要素は、項目を作らずに進めず「検定不能」として理由を残すこと。</summary>
        [TestMethod]
        public void AnElementWithoutResultsIsReportedAsUnavailable()
        {
            var vm = Analyzed();
            var beam = vm.CurrentModel!.Beams.First(b => b.PileBodyNo is int && b.SegmentIndex is int && b.BeamResults?.Count > 0);
            var saved = beam.BeamResults.ToList();
            try
            {
                beam.BeamResults.Clear();
                var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
                // 梁の名前は杭どうしで重なるので、杭と要素の番号で行を特定する
                int pileNo = vm.ResultInputModel.PileLayoutItems!.First(p => p.Beams?.Contains(beam) == true).PileNo;
                var rows = result.Items.Where(i => i.PileNo == pileNo && i.SegmentIndex == beam.SegmentIndex
                    && i.Kind is EvaluationKind.PileSectionMoment or EvaluationKind.PileSectionShear).ToList();

                Assert.IsTrue(rows.Count > 0, "解析結果の無い要素の検定項目がありません (黙って飛ばしている)");
                Assert.IsTrue(rows.All(i => i.IsUnavailable && i.StatusLabel == "検定不能" && !i.IsJudged),
                    "検定不能になっていない行: " + string.Join(" / ", rows.Where(i => !i.IsUnavailable).Select(i => $"{i.Category} {i.TargetDescription} {i.StatusLabel}")));
                Assert.IsTrue(rows.All(i => i.NotJudgedReason == EvaluationService.NoResultReason),
                    "理由: " + string.Join(" / ", rows.Select(i => i.NotJudgedReason).Distinct()));
                Assert.AreEqual(rows.Count, result.UnavailableCount);
                Assert.AreEqual("—", rows[0].ResponseText, "検定不能の行の応答値が NaN のまま表示されます");

                string text = EvaluationService.BuildEvaluationText(vm, factored: true, displayFilter: 2);
                StringAssert.Contains(text, "検定できなかった項目:", "テキストに検定できなかった項目の件数が出ていません");
                Assert.IsFalse(text.Contains("検定: すべてOK", StringComparison.Ordinal), "検定できなかった項目があるのに「すべてOK」と書いています");
            }
            finally
            {
                foreach (var r in saved) beam.BeamResults.Add(r);
            }
        }

        [TestMethod]
        public void TheDashboardDoesNotSayAllOkWhenSomethingWasNotEvaluated()
        {
            var ok = new EvaluationItem { Kind = EvaluationKind.PileSectionMoment, PileNo = 1, Response = 1, Limit = 2, IsOk = true };
            var unavailable = ok with { Response = double.NaN, UnavailableReason = EvaluationService.NoResultReason };
            var summary = PileEvaluationSummary.FromResults(new EvaluationResult([ok, unavailable]), null, new EvaluationResult([]), "A");

            var verdict = PileDesign.Views.ResultDashboardWindow.DecideVerdict(summary, horizontalDone: true);
            StringAssert.StartsWith(verdict.Text, "検定不能 1 件", "検定できなかった項目があるのに総合判定が「すべて OK」になっています");
        }

        [TestMethod]
        public void APileWithUnavailableItemsIsNotShownAsSafe()
        {
            Assert.AreEqual(PileRatioBand.Unavailable, PileEvaluationSummary.BandOf(0.3, false, false, false, hasUnavailable: true),
                "検定できなかった項目のある杭が「余裕あり」になります");
            Assert.AreEqual(PileRatioBand.Ng, PileEvaluationSummary.BandOf(1.2, true, false, false, hasUnavailable: true));
        }

        // ── 荷重組合せの番号 ─────────────────────────────

        [TestMethod]
        public void CombinationsAreMatchedByNumberNotByTheRoundedName()
        {
            var a = new LoadCombination(1, 0.999, -1.0, 0.999);
            var b = new LoadCombination(2, 1.0, -0.999, 1.0);
            Assert.AreEqual(a.Name, b.Name, "(前提) 係数を丸めた名前が同じになること");
            Assert.IsFalse(LoadCombination.IsSameCombination(a, b), "名前が同じ別の組合せを同じとみなしています");
            Assert.IsTrue(LoadCombination.IsSameCombination(a, new LoadCombination(1, 0.5, 0.5, 0.5)));
        }

        /// <summary>
        /// 表示名 (係数を丸めた文字列) が重なる荷重組合せは、画面の選択で見分けられないので読込で知らせること。
        /// 画面では作れず (係数の刻みは 0.1)、手で編集したファイルなどでだけ起きる。
        /// </summary>
        [TestMethod]
        public void CombinationsWithTheSameDisplayNameAreReportedOnLoad()
        {
            var input = new LoadCasesInput
            {
                LoadCombinations = [new LoadCombination(1, 0.999, -1.0, 0.999), new LoadCombination(2, 1.0, -0.999, 1.0), new LoadCombination(3, 0.5, 1, 0.5)],
            };
            var duplicates = input.DescribeDuplicateCombinationNames();
            Assert.AreEqual(1, duplicates.Count, "表示名の重なる組合せを見つけていません");
            StringAssert.Contains(duplicates[0], "組合せ No.1, No.2");

            string message = MainWindowViewModel.DescribeDuplicateCombinationNames(duplicates);
            StringAssert.Contains(message, "見分けられず");
            StringAssert.Contains(message, "結果には影響しません");

            input.LoadCombinations = [new LoadCombination(1, 1, 1, 1), new LoadCombination(2, 0.5, 1, 0.5)];
            Assert.AreEqual(0, input.DescribeDuplicateCombinationNames().Count);

            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            StringAssert.Contains(body, "DescribeDuplicateCombinationNames()", "読込で荷重組合せの表示名の重なりを調べていません");
        }

        [TestMethod]
        public void CombinationNumbersAreAlignedOnLoadAndTheMThetaKeysFollow()
        {
            var input = new LoadCasesInput
            {
                LoadCasesLevel1 = [new LoadCase { Level = 1, No = 1, LoadName = "X" }],
                LoadCasesLevel2 = [],
                LoadCombinations = [new LoadCombination(3, 1, 1, 1), new LoadCombination(3, 0.5, 1, 0.5)],
            };
            var changes = input.NormalizeLoadCaseNumbers();
            CollectionAssert.AreEqual(new[] { 1, 2 }, input.LoadCombinations.Select(c => c.No).ToArray(), "荷重組合せの番号が並び順に揃っていません");
            Assert.AreEqual(2, changes.Count);

            var rs = new RotationalSpring();
            var snap = new MThetaCaseSnapshot();
            rs.CaseMThetaSnapshots["L1-1|3|False"] = snap;   // 重複していた番号の控え → 決められないので捨てる
            rs.CaseMThetaSnapshots["L1-1|7|False"] = snap;   // 番号 7 → 1
            RotationalSpring.RenumberCaseKeys([rs], [(1, 1, 1)], [(3, 1), (3, 2), (7, 1)]);
            CollectionAssert.AreEquivalent(new[] { "L1-1|1|False" }, rs.CaseMThetaSnapshots.Keys.ToArray());
        }
    }
}
