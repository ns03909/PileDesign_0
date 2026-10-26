using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 地震時軸力の分離入力 (2026-10-26)。地震時軸力を ΔL(U) (上部構造慣性力による分) と ΔL(L) (基礎部慣性力による分) に分けて入れ、
    /// 荷重組合せごとに N = VL + βL·ΔL(L) + βU·ΔL(U) で求める。
    ///
    /// 軸力は解析・検定・図・計算書・支持力・沈下・MGT・Chang の 30 ほどの所が読む。分離入力では組合せが要るので、
    /// 読む所は <see cref="SeismicAxialForces"/> を通す。ここでは、式・従来の入力との一致・組合せの渡し忘れの検出・
    /// 保存と複製・支持力と沈下の組合せごとの扱い・解析まで通した結果を見る。
    /// </summary>
    [TestClass]
    public class SeparatedAxialForceTests
    {
        private static readonly LoadCombination Plus = new(1, 1.0, 1.0, 1.0);
        private static readonly LoadCombination Opposite = new(2, 1.0, 1.0, -1.0);
        private static readonly LoadCombination Half = new(3, 1.0, 0.5, 1.0);

        private static PileLayoutDataItem MakePile()
        {
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, AxialForceVL0 = 1000.0, AxialForceVLAdditional = 200.0 };
            pile.AxialForceLevel1s = [1500.0, 900.0, 0.0, 1300.0];
            pile.AxialForceLevel2s = [2000.0, 400.0, 1200.0, 1200.0];
            pile.AxialForceUpperLevel1s = [300.0, -300.0, 0.0, 50.0];
            pile.AxialForceFoundationLevel1s = [100.0, -100.0, 0.0, 10.0];
            pile.AxialForceUpperLevel2s = [800.0, -800.0, 0.0, 0.0];
            pile.AxialForceFoundationLevel2s = [600.0, -600.0, 0.0, 0.0];
            return pile;
        }

        private static InputModel MakeInput(bool separated, params LoadCombination[] combinations)
        {
            var input = new InputModel { IsAxialForceSeparated = separated };
            input.LoadCasesInput = new LoadCasesInput
            {
                LoadCombinations = new ObservableCollection<LoadCombination>(combinations.Length > 0 ? combinations : [Plus, Opposite, Half]),
            };
            foreach (var c in input.LoadCasesInput.LoadCombinations) c.IsApplicable = true;
            return input;
        }

        // ── 式 ──

        [TestMethod]
        public void Separated_AxialForceIs_VL_Plus_BetaL_TimesFoundation_Plus_BetaU_TimesUpper()
        {
            var pile = MakePile();
            var input = MakeInput(true);
            // VL = 1200。L1-1: ΔU = 300、ΔL = 100
            Assert.AreEqual(1200 + 1 * 100 + 1 * 300, SeismicAxialForces.Input(input, pile, 1, 1, Plus), 1e-9);
            Assert.AreEqual(1200 - 1 * 100 + 1 * 300, SeismicAxialForces.Input(input, pile, 1, 1, Opposite), 1e-9);
            Assert.AreEqual(1200 + 1 * 100 + 0.5 * 300, SeismicAxialForces.Input(input, pile, 1, 1, Half), 1e-9);
            // L2-2: ΔU = -800、ΔL = -600
            Assert.AreEqual(1200 - 600 - 800, SeismicAxialForces.Input(input, pile, 2, 2, Plus), 1e-9);
            // 常時は VL (組合せは要らない)
            Assert.AreEqual(1200, SeismicAxialForces.Input(input, pile, 0, 0, null), 1e-9);
        }

        [TestMethod]
        public void NotSeparated_EveryEntry_GivesTheSameValueAsBefore()
        {
            var pile = MakePile();
            var input = MakeInput(false);
            for (int level = 1; level <= 2; level++)
                for (int no = 1; no <= 4; no++)
                    foreach (var c in new[] { null, Plus, Opposite })
                    {
                        Assert.AreEqual(pile.GetSeismicAxialForce(no, level), SeismicAxialForces.Input(input, pile, no, level, c), 0.0);
                        Assert.AreEqual(pile.ResolveDesignAxialForce(no, level).N, SeismicAxialForces.Design(input, pile, no, level, c).N, 0.0);
                        var e = SeismicAxialForces.Envelope(input, pile, no, level);
                        Assert.AreEqual(pile.GetDesignAxialForce(no, level), e.Max, 0.0);
                        Assert.AreEqual(e.Max, e.Min, 0.0);
                        Assert.IsNull(e.MaxCombination);
                    }
            // 未入力 (0) は常時軸力 (従来の決まり)。分離入力でなければ変わらない
            Assert.AreEqual(pile.AxialForceVL, SeismicAxialForces.Design(input, pile, 3, 1, null).N, 0.0);
            CollectionAssert.AreEqual(pile.AxialForceLevel1s.ToList(), SeismicAxialForces.PerCaseForSettlement(input, pile, 1).ToList());
            Assert.AreEqual(1, SeismicAxialForces.CombinationsForVerticalAnalysis(input).Count);
            Assert.IsNull(SeismicAxialForces.CombinationsForVerticalAnalysis(input)[0]);
        }

        [TestMethod]
        public void Separated_ZeroResultIsNotReadAsNotEntered()
        {
            // 合成した結果が 0 でも「未入力 → VL」とは読まない (分離入力の Δ が 0 なら自然に VL)
            var pile = MakePile();
            pile.AxialForceUpperLevel1s[2] = -1200.0;   // L1-3: VL + 0 + 1·(-1200) = 0
            var input = MakeInput(true, Plus);
            Assert.AreEqual(0.0, SeismicAxialForces.Design(input, pile, 3, 1, Plus).N, 1e-9);
            pile.AxialForceUpperLevel1s[2] = 0.0;
            Assert.AreEqual(pile.AxialForceVL, SeismicAxialForces.Design(input, pile, 3, 1, Plus).N, 1e-9, "Δ が 0 なら VL");
        }

        [TestMethod]
        public void Separated_WithoutACombination_Throws_InsteadOfSilentlyUsingAnother()
        {
            var pile = MakePile();
            var input = MakeInput(true);
            Assert.ThrowsException<InvalidOperationException>(() => SeismicAxialForces.Input(input, pile, 1, 1, null));
            Assert.ThrowsException<InvalidOperationException>(() => SeismicAxialForces.Design(input, pile, 1, 1, null));
        }

        // ── 組合せをまたぐ所 ──

        [TestMethod]
        public void Envelope_TakesTheLargestAndSmallest_WithTheirCombinations()
        {
            var pile = MakePile();
            var input = MakeInput(true);
            var e = SeismicAxialForces.Envelope(input, pile, 2, 2);   // L2-2: ΔU = -800、ΔL = -600
            double plus = 1200 - 600 - 800, opposite = 1200 + 600 - 800, half = 1200 - 600 - 400;
            Assert.AreEqual(new[] { plus, opposite, half }.Max(), e.Max, 1e-9);
            Assert.AreEqual(new[] { plus, opposite, half }.Min(), e.Min, 1e-9);
            Assert.AreSame(Opposite, e.MaxCombination);
            Assert.AreSame(Plus, e.MinCombination);
        }

        [TestMethod]
        public void CombinationsForVerticalAnalysis_MergesSameBetas()
        {
            var twin = new LoadCombination(4, 0.5, 1.0, 1.0);   // αL だけ違う (軸力は Plus と同じ)
            var input = MakeInput(true, Plus, Opposite, twin);
            var combos = SeismicAxialForces.CombinationsForVerticalAnalysis(input);
            CollectionAssert.AreEqual(new[] { 1, 2 }, combos.Select(c => c!.No).ToList(), "βU・βL の同じ組合せは 1 つにまとめる");
        }

        [TestMethod]
        public void Bearing_ChecksCompressionWithTheMaximum_AndUpliftWithTheMinimum()
        {
            var pile = MakePile();
            // L2-2 は Plus で引張 (-200)、Opposite で圧縮 (1000)
            var input = MakeInput(true, Plus, Opposite);
            var soilPile = new SoilPile { Rfu = 8000, Rt_DLS = -100, Rt_ULS = -150 };   // R_ULS = Ru = 8000
            var items = new List<PileDesign.Models.Results.EvaluationItem>();
            PileBearingEvaluator.AddPileItems(items, pile, soilPile, [], [("L2-2", 2)], level2UsesDamageLimit: false, inputModel: input);

            var compression = items.Single(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileBearingCompression && i.Level == 2);
            var uplift = items.Single(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileUpliftResistance && i.Level == 2);
            Assert.AreEqual(1200 + 600 - 800, compression.AxialForce!.Value, 1e-9);
            Assert.AreEqual(1200 - 600 - 800, uplift.AxialForce!.Value, 1e-9);
            StringAssert.Contains(compression.LoadCaseName, "組合せNo.2");
            StringAssert.Contains(uplift.LoadCaseName, "組合せNo.1");
            Assert.IsFalse(uplift.IsOk, "引抜き 200 kN > 150 kN");

            // 分離入力でなければ従来どおり 1 項目 (L2-2 の入力 400 kN で押込み)
            items.Clear();
            PileBearingEvaluator.AddPileItems(items, pile, soilPile, [], [("L2-2", 2)], level2UsesDamageLimit: false, inputModel: MakeInput(false));
            Assert.AreEqual(1, items.Count(i => i.Level == 2));
            Assert.AreEqual(400.0, items.Single(i => i.Level == 2).AxialForce!.Value, 1e-9);
        }

        [TestMethod]
        public void SingleSettlement_IsStoredPerCombination_AndTheCaseColumnHasTheLargest()
        {
            var (example, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            var soilPile = example?.ElementDivision?.SoilPiles?.FirstOrDefault();
            if (example == null || soilPile == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var vtm = new PileDesign.FEM.VerticalLoadTransferMethod(example, soilPile);
            vtm.LoadDisplacements.Clear();
            vtm.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, DD0s = 0, DDns = 0 });
            vtm.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 4000, DD0s = 40, DDns = 0 });

            var pile = MakePile();
            var input = MakeInput(true, Plus, Opposite);
            SingleSettlementSession.WriteSinglePileSettlements(input, pile, vtm);

            // L1-1: Plus = 1600 kN → 16 mm、Opposite = 1400 kN → 14 mm
            var plus = SeismicAxialForces.FindSeparatedSettlement(pile, 1, 1, Plus);
            var opposite = SeismicAxialForces.FindSeparatedSettlement(pile, 1, 1, Opposite);
            Assert.IsNotNull(plus); Assert.IsNotNull(opposite);
            Assert.AreEqual(1600.0, plus!.AxialLoad, 1e-9);
            Assert.AreEqual(0.016, plus.Settlement, 1e-9);
            Assert.AreEqual(0.014, opposite!.Settlement, 1e-9);
            Assert.AreEqual(0.016, pile.SinglePileSettlementLevel1s[0], 1e-9, "ケースの欄は組合せのうち軸力が最大のもの");
            Assert.AreEqual(2 * 2 * 4, pile.SinglePileSettlementsSeparated.Count, "レベル 2 × ケース 4 × 組合せ 2");

            // 分離入力でなければ組合せごとの結果は持たない
            SingleSettlementSession.WriteSinglePileSettlements(MakeInput(false), pile, vtm);
            Assert.AreEqual(0, pile.SinglePileSettlementsSeparated.Count);
            Assert.AreEqual(0.015, pile.SinglePileSettlementLevel1s[0], 1e-9, "従来どおり AxialForceLevel1s (1500 kN)");
        }

        // ── 保存・複製 ──

        [TestMethod]
        public void FlagAndValues_SurviveCopyForHistoryAndQuickClone()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null || input.PileLayoutItems.Count == 0) { Assert.Inconclusive("例題ファイルなし"); return; }
            input.IsAxialForceSeparated = true;
            var pile = input.PileLayoutItems[0];
            var source = MakePile();
            pile.AxialForceUpperLevel1s = source.AxialForceUpperLevel1s;
            pile.AxialForceUpperLevel2s = source.AxialForceUpperLevel2s;
            pile.AxialForceFoundationLevel1s = source.AxialForceFoundationLevel1s;
            pile.AxialForceFoundationLevel2s = source.AxialForceFoundationLevel2s;
            pile.SinglePileSettlementsSeparated.Add(new SeparatedSettlementResult { Level = 1, CaseNo = 2, CombinationNo = 1, BetaU = 1, BetaL = 1, AxialLoad = 1500, Settlement = 0.01 });

            var copy = input.CopyForHistory();
            Assert.IsTrue(copy.IsAxialForceSeparated, "複製 (元に戻す・保存に使う) で分離入力が落ちる");
            var p = copy.PileLayoutItems[0];
            CollectionAssert.AreEqual(pile.AxialForceUpperLevel2s.ToList(), p.AxialForceUpperLevel2s.ToList());
            CollectionAssert.AreEqual(pile.AxialForceFoundationLevel1s.ToList(), p.AxialForceFoundationLevel1s.ToList());
            Assert.AreEqual(1, p.SinglePileSettlementsSeparated.Count);

            var clone = pile.QuickClone();
            CollectionAssert.AreEqual(pile.AxialForceUpperLevel1s.ToList(), clone.AxialForceUpperLevel1s.ToList());
            Assert.AreNotSame(pile.AxialForceUpperLevel1s, clone.AxialForceUpperLevel1s, "一覧を共有している");
            Assert.AreEqual(0.01, clone.SinglePileSettlementsSeparated.Single().Settlement, 1e-12);
        }

        [TestMethod]
        public void OldFilesWithoutTheValues_ReadAsNotSeparated_WithZeroDeltas()
        {
            var pile = new PileLayoutDataItem();
            Assert.AreEqual(4, pile.AxialForceUpperLevel1s.Count);
            Assert.IsTrue(pile.AxialForceFoundationLevel2s.All(v => v == 0.0));
            Assert.IsFalse(new InputModel().IsAxialForceSeparated);
        }

        // ── 解析まで通す ──

        /// <summary>
        /// ΔL(U) = 従来の地震時軸力 − VL、ΔL(L) = 0 とした分離入力は、βU = 1 の組合せで従来の入力と同じ結果になり、
        /// βU = 0.5 の組合せでは軸力が変わるので結果も変わる。組合せが解析の軸力に届いていることの確認。
        /// </summary>
        [TestMethod]
        [Timeout(900000)]
        public void Analysis_UsesTheCombinationsAxialForce()
        {
            void Combos(InputModel m)
            {
                m.LoadCasesInput.LoadCombinations = [new LoadCombination(1, 1.0, 1.0, 1.0) { IsApplicable = true }, new LoadCombination(2, 1.0, 0.5, 1.0) { IsApplicable = true }];
                // 解く荷重ケースを 1 つに絞る (時間)
                foreach (var lc in m.LoadCasesInput.AllSeismicLoadCases) lc.IsApplicable = lc.Level == 2 && lc.No == 1;
            }
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4, Level2Steps = 8,
                LiquefactionMode = PileDesign.ViewModels.HorizontalCalculationViewModel.LiquefactionOptionType.None,
                Customize = Combos,
            };
            ConvergenceSnapshot legacy, separated;
            try
            {
                legacy = HeadlessHorizontalRunner.RunExample("Example10", "PileExample10", options);
                options.Customize = m =>
                {
                    Combos(m);
                    m.IsAxialForceSeparated = true;
                    foreach (var p in m.PileLayoutItems)
                        for (int i = 0; i < 4; i++)
                        {
                            p.AxialForceUpperLevel1s[i] = p.AxialForceLevel1s[i] - p.AxialForceVL;
                            p.AxialForceUpperLevel2s[i] = p.AxialForceLevel2s[i] - p.AxialForceVL;
                            p.AxialForceFoundationLevel1s[i] = 0.0;
                            p.AxialForceFoundationLevel2s[i] = 0.0;
                        }
                };
                separated = HeadlessHorizontalRunner.RunExample("Example10", "PileExample10", options);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗")) { Assert.Inconclusive(ex.Message); return; }

            CaseRecord Find(ConvergenceSnapshot s, int combo) => s.Cases.Single(c => c.Level == 2 && c.LoadCaseNo == 1 && c.CombinationNo == combo);
            var lOne = Find(legacy, 1); var sOne = Find(separated, 1);
            var lHalf = Find(legacy, 2); var sHalf = Find(separated, 2);
            Assert.IsTrue(lOne.Converged && sOne.Converged && lHalf.Converged && sHalf.Converged, "収束していない");

            // βU = 1: 同じ軸力 (VL + 1·(L − VL)。丸めの差だけ)
            Assert.AreEqual(lOne.ApUx, sOne.ApUx, Math.Abs(lOne.ApUx) * 1e-6 + 1e-12, "βU = 1 の組合せで結果が従来と違う");
            Assert.AreEqual(lOne.MaxAbsPileMoment ?? 0, sOne.MaxAbsPileMoment ?? 0, Math.Abs(lOne.MaxAbsPileMoment ?? 0) * 1e-6 + 1e-9);

            // βU = 0.5: 従来は組合せによらず同じ軸力、分離入力では (L − VL) の半分 → M-φ が変わる
            Assert.AreNotEqual(lHalf.MaxAbsPileMoment ?? 0, sHalf.MaxAbsPileMoment ?? 0, Math.Abs(lHalf.MaxAbsPileMoment ?? 0) * 1e-6,
                "βU = 0.5 の組合せで軸力が変わっていない (組合せが解析に届いていない)");
        }

        // ── 結果の見出しの照合 (2026-10-27 のレビューの 3 件) ──

        [TestMethod]
        public void CaseLabels_StripTheCombination_AndMatchTheCase()
        {
            const string withCombo = "L1-1: U1 / 組合せNo.2 (βU=1, βL=-1)";
            Assert.AreEqual("U1", SeismicCaseLabels.BaseCaseName(withCombo));
            Assert.AreEqual("U1", SeismicCaseLabels.BaseCaseName("1-1: U1 / 組合せNo.2 (βU=1, βL=-1)"));
            Assert.AreEqual("U1", SeismicCaseLabels.BaseCaseName("1-1: U1"));
            Assert.AreEqual("VL", SeismicCaseLabels.BaseCaseName("VL (常時+追加)"));
            Assert.IsTrue(SeismicCaseLabels.RefersToCase(withCombo, "U1"), "組合せを添えた見出しが荷重ケースに当たらない (群杭沈下の記録を選べない)");
            Assert.IsTrue(SeismicCaseLabels.RefersToCase("L1-1: U1", "U1"));
            Assert.IsFalse(SeismicCaseLabels.RefersToCase("L1-1: U10", "U1"));
            Assert.AreEqual(" / 組合せNo.2 (βU=1, βL=-1)", SeismicCaseLabels.CombinationPartOf(withCombo));
        }

        [TestMethod]
        public void CaseLabels_SelectingAMergedCombination_PicksItsRepresentativesResult()
        {
            // No.2 と No.3 は βU・βL が同じ → まとめて解き、結果は代表 No.2 の見出しを持つ
            var c1 = new LoadCombination(1, 1.0, 1.0, 1.0);
            var c2 = new LoadCombination(2, 1.0, 1.0, -1.0);
            var c3 = new LoadCombination(3, 0.5, 1.0, -1.0);
            var input = MakeInput(true, c1, c2, c3);
            var labels = new List<string>
            {
                "VL (常時+追加)",
                "1-1: U1" + SeismicAxialForces.CombinationSuffix(c1),
                "1-1: U1" + SeismicAxialForces.CombinationSuffix(c2),
            };
            bool SameCase(int i) => SeismicCaseLabels.BaseCaseName(labels[i]) == "U1";
            Assert.AreEqual(2, SeismicCaseLabels.Pick(labels, SameCase, SeismicCaseLabels.SuffixFor(input, c3)),
                "No.3 を選んだのに代表 (No.2) の結果を選ばない (別の組合せの沈下量が出る)");
            Assert.AreEqual(1, SeismicCaseLabels.Pick(labels, SameCase, SeismicCaseLabels.SuffixFor(input, c1)));
            // 分離入力でなければ組合せは見ない (従来どおり同じケースの最初)
            Assert.AreEqual("", SeismicCaseLabels.SuffixFor(MakeInput(false, c1, c2, c3), c3));
        }

        /// <summary>結果の見出しからケース名を取り出す・末尾で照合する処理は SeismicCaseLabels だけ (写しは直すと取り残される)。</summary>
        [TestMethod]
        public void CaseLabelMatching_HasNoCopies()
        {
            var copy = new Regex(@"ExtractVBCaseBaseName|EndsWith\(\s*"":\s""\s*\+");
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                scanned++;
                if (Path.GetFileName(file) == "SeismicAxialForces.cs") continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (copy.IsMatch(lines[i].Split("//")[0])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
            TestSource.AssertScanned(scanned, 300, "本体の .cs");
            Assert.AreEqual(0, offenders.Count, "結果の見出しを自前で照合しています (SeismicCaseLabels を使う): " + string.Join(", ", offenders));
        }

        // ── 合成した軸力 0 (2026-10-27 のレビュー) ──

        /// <summary>
        /// 分離入力では合成した軸力 0 も有効。杭頭の M-θ をケース開始時に 0 の軸力で作る
        /// (以前は「0 = 未入力」として常時軸力の曲線を作り、図・計算書にも残った)。
        /// </summary>
        [TestMethod]
        public void SeparatedZeroAxialForce_IsUsedForTheMThetaCurve()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            foreach (var c in input.LoadCasesInput.AnalysisTargetSeismicLoadCases) c.IsPileNonLinear = true;
            var lc = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(c => c.Level == 2);
            var combo = new LoadCombination(1, 1.0, 1.0, 1.0);

            input.IsAxialForceSeparated = true;
            foreach (var p in input.PileLayoutItems)
            {
                // ΔL(U) = -VL、ΔL(L) = 0 → 合成した軸力 0
                p.AxialForceUpperOf(lc.Level)[lc.No - 1] = -p.AxialForceVL;
                p.AxialForceFoundationOf(lc.Level)[lc.No - 1] = 0.0;
            }

            var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input };
            var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };
            var modelling = new PileDesign.FEM.AnalysisModelling(input);
            var model = new PileDesign.FEM.AnaModel(input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates();
            vm.SetupNonlinearMThetaForLoadCase(model, lc, combo);

            var spring = model.RotationalSprings.FirstOrDefault(s => s.McrXY.HasValue && s.Name != null && s.Name.Contains('-'));
            if (spring == null) { Assert.Inconclusive("Mcr を持つ杭頭ばねが無い例題"); return; }
            int pileNo = int.Parse(spring.Name!.Split('-')[^1]);
            var pile = input.PileLayoutItems.First(p => p.No == pileNo);
            Assert.AreEqual(0.0, SeismicAxialForces.Input(input, pile, lc.No, lc.Level, combo), 1e-9, "(前提) 合成した軸力が 0");
            var pileBody = input.PileBodies[(spring.PileBodyNo is int v && v > 0 ? v : 1) - 1];
            double atZero = pileBody.GetMThetaRelationship(0).McrXY ?? double.NaN;
            double atVl = pileBody.GetMThetaRelationship(Math.Round(pile.AxialForceVL)).McrXY ?? double.NaN;
            Assert.AreNotEqual(atZero, atVl, 1e-6, "(前提) 軸力 0 と常時軸力で Mcr が違う");
            Assert.AreEqual(atZero, spring.McrXY!.Value, 1e-6, "合成した軸力 0 を未入力として常時軸力の M-θ を作っている");
        }

        // ── 網 ──

        /// <summary>
        /// 杭の軸力のメソッド (ケース番号で引く) を直接呼ぶと、分離入力を読み落とす。窓口 (SeismicAxialForces) と杭自身の外では呼ばない。
        /// </summary>
        [TestMethod]
        public void PileAxialForceMethods_AreCalledOnlyThroughTheEntry()
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SeismicAxialForces.cs", "PileLayoutDataItem.cs" };
            var pattern = new Regex(@"\.(GetSeismicAxialForce|GetDesignAxialForce|ResolveDesignAxialForce|GetSeparatedSeismicAxialForce)\(");
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                scanned++;
                if (allowed.Contains(Path.GetFileName(file))) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var code = lines[i].Split("//")[0];
                    if (pattern.IsMatch(code)) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
            TestSource.AssertScanned(scanned, 300, "本体の .cs");
            Assert.AreEqual(0, offenders.Count, "杭の軸力のメソッドを直接呼んでいます (SeismicAxialForces を通す): " + string.Join(", ", offenders));
        }

        /// <summary>
        /// 分けない入力 (AxialForceLevel1s/2s) を読むファイルは、分離入力も考えていること (窓口か分離入力の値を参照している)。
        /// 例外は下の一覧 (分離入力の値を読む必要の無い所。理由付き)。
        /// </summary>
        [TestMethod]
        public void FilesReadingTheLegacyAxialArrays_AlsoHandleTheSeparatedInput()
        {
            var exempt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PileLayoutDataItem.cs"] = "値そのものの持ち主",
                ["AxialForceModeContext.cs"] = "説明の文だけ",
                ["BindingPath.cs"] = "説明の文だけ",
                ["PileExampleData.cs"] = "例題の読み込み (例題は分離入力を使わない)",
                ["PileExampleLoader.cs"] = "例題の読み込み (例題は分離入力を使わない)",
                ["PileLayoutService.cs"] = "一括編集 (分離入力のあいだは呼び出し側で止める) と杭の複製 (QuickClone が分離入力の値も写す)",
                ["MainWindow.DataGridHandlers.cs"] = "編集した列から選択中の荷重ケースを決めるだけ",
                ["MainWindow.xaml.cs"] = "表の再描画の判定だけ",
            };
            var reads = new Regex(@"AxialForce(Variation)?Level[12]s");
            var handles = new Regex(@"SeismicAxialForces|IsAxialForceSeparated|AxialForceUpperOf|AxialForceUpperLevel");
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                var text = File.ReadAllText(file);
                if (!reads.IsMatch(text)) continue;
                scanned++;
                if (exempt.ContainsKey(Path.GetFileName(file))) continue;
                if (!handles.IsMatch(text)) offenders.Add(Path.GetFileName(file));
            }
            TestSource.AssertScanned(scanned, 15, "地震時軸力を読むファイル");
            Assert.AreEqual(0, offenders.Count, "分けない地震時軸力だけを読み、分離入力を考えていないファイル: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void OverturningMomentWindow_XamlParses_InBothModes()
        {
            foreach (bool separated in new[] { false, true })
            {
                bool created = false;
                var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    XamlSmokeTestSupport.EnsureApplicationResources();
                    var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = new InputModel { IsAxialForceSeparated = separated } };
                    var window = new PileDesign.Views.AutoOverturningMomentWindow(mainVm);
                    created = window != null;
                    window?.Close();
                }, out bool timedOut);
                if (timedOut) { Assert.Inconclusive("XAML パースが時間内に終わらなかった"); return; }
                if (captured != null) Assert.Fail($"OTM の画面 (分離入力 {separated}) の XAML パースに失敗: {captured}");
                Assert.IsTrue(created);
            }
        }
    }
}
