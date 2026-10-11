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

namespace TestProject1
{
    /// <summary>
    /// 2026-10-11 の横断監査の続き (検定の置き場所と欠け、沈下の結果の陳腐化を解析ごとに、
    /// 群杭沈下（基礎梁あり）の確定、解析結果の控えの Ec、保存する結果の追加、まとめ一覧の欠けの印) の再発防止。
    /// </summary>
    [TestClass]
    public class Audit20261011FollowupTests
    {
        // ── 基礎梁の傾斜角は沈下の側で検定する ─────────────────────────────

        /// <summary>基礎節点 3 つ (X = 0, 5, 10 m) と、1-2・2-3 の基礎梁 2 本を持つ入力。</summary>
        private static InputModel BeamModel(out FoundationBeam beam12, out FoundationBeam beam23)
        {
            var nodes = new ObservableCollection<FoundationNode>();
            for (int i = 0; i < 3; i++)
                nodes.Add(new FoundationNode { Id = Guid.NewGuid(), No = i + 1, X = 5.0 * i, Y = 0 });
            beam12 = new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.FoundationNode, NodeI_Id = nodes[0].Id,
                NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = nodes[1].Id,
            };
            beam23 = new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.FoundationNode, NodeI_Id = nodes[1].Id,
                NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = nodes[2].Id,
            };
            var input = new InputModel
            {
                FundamentalInput = new FundamentalInput { ServiceDeformationAngleLimit = 1.0e-3 },
                FoundationBeamInput = new FoundationBeamInput(),
                PileGroupSettlement = new PileGroupSettlement(),
            };
            input.FoundationBeamInput.Nodes = nodes;
            input.FoundationBeamInput.Beams = [beam12, beam23];
            return input;
        }

        private static GroupSettlementCaseRecord BeamAwareRecord(string name, params (string Node, double Uz_mm)[] uz) => new()
        {
            LoadCaseName = name,
            IsBeamAware = true,
            IsConverged = true,
            NodeResults = uz.Select(u => new VerticalBeamNodeResult { NodeName = u.Node, Uz_mm = u.Uz_mm }).ToList(),
        };

        /// <summary>
        /// 基礎梁の傾斜角は沈下の結果だけで検定する (水平解析の検定には入れない)。
        /// 端の節点の沈下量が無い梁・節点の結果が無い記録は、黙って飛ばさずに検定不能として出す。
        /// 以前は水平解析の検定の中にあり (水平解析を済ませないと出ない)、欠けた梁・記録を飛ばして残りの梁で判定していた。
        /// </summary>
        [TestMethod]
        public void FoundationBeamInclination_IsASettlementCheck_AndReportsWhatItCannotJudge()
        {
            var input = BeamModel(out _, out _);
            input.PileGroupSettlement.CaseRecords =
            [
                // 節点 3 の沈下量が無い
                BeamAwareRecord("矩形荷重 (VL)", ("FoundationNode-1", 10.0), ("FoundationNode-2", 12.5)),
                // 節点の結果が丸ごと無い
                new GroupSettlementCaseRecord { LoadCaseName = "L1-1: 杭軸力", IsBeamAware = true, IsConverged = true },
            ];

            var items = FoundationBeamInclinationEvaluator.Evaluate(input);

            var vl = items.Where(i => i.LoadCaseName == "矩形荷重 (VL)").OrderBy(i => i.FoundationBeamNo).ToList();
            Assert.AreEqual(2, vl.Count, "梁ごとに 1 件ずつ出すこと (欠けた梁を黙って飛ばしています)");
            Assert.IsTrue(vl[0].IsJudged, "沈下量のそろった梁は判定すること");
            Assert.AreEqual(2.5e-3 / 5.0, vl[0].Response, 1e-15);
            Assert.AreEqual(0, vl[0].Level);
            Assert.IsTrue(vl[1].IsUnavailable, "端の沈下量が無い梁を黙って飛ばしています");
            StringAssert.Contains(vl[1].UnavailableReason, "基礎梁 #2");

            var l1 = items.Where(i => i.LoadCaseName == "L1-1: 杭軸力").ToList();
            Assert.AreEqual(1, l1.Count, "節点の結果が無い記録を黙って飛ばしています");
            Assert.IsTrue(l1[0].IsUnavailable);
            Assert.AreEqual(1, l1[0].Level, "限界値の取り方 (レベル1 = 損傷限界) は変えないこと");
            Assert.AreEqual("損傷限界", l1[0].LimitName);
            Assert.IsTrue(items.All(i => i.Kind == EvaluationKind.FoundationBeamInclination));
        }

        /// <summary>
        /// 基礎梁の傾斜角は水平解析の検定に戻さない。沈下による杭頭変形角と同じ 3 か所 (結果のまとめ・解析結果テーブル・
        /// 計算書) から出す。
        /// </summary>
        [TestMethod]
        public void FoundationBeamInclination_IsNotPartOfTheHorizontalEvaluation()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            foreach (var word in new[] { "EvaluateBeamAwareInclination", "InclinationLimitFor", "EvaluationKind.FoundationBeamInclination" })
                Assert.IsFalse(src.Contains(word, StringComparison.Ordinal), $"基礎梁の傾斜角が水平解析の検定の中に戻っています ({word})");
            foreach (var (dir, file) in new[] { ("ViewModels", "MainWindowViewModel.ResultsWindows.cs"), ("Services", "PileEvaluationSummary.cs"), ("Output", "WordDocument.SummaryTables.cs") })
                StringAssert.Contains(TestSource.Read("Graphics_r1", dir, file), "FoundationBeamInclinationEvaluator.Evaluate(",
                    $"{file}: 基礎梁の傾斜角の検定を出していません");
        }

        // ── 沈下の結果の陳腐化を解析ごとに ─────────────────────────────

        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        /// <summary>杭 2 本・土層-杭セット 1 つ (荷重-沈下曲線あり) の入力を持つ画面。解析結果はまだ無い。</summary>
        private static MainWindowViewModel SettlementVm()
        {
            var input = new InputModel();
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            input.FundamentalInput ??= new FundamentalInput();
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 1, No = 1, PileBodyNo = 1, SoilPileAltNo = 1, AxialForceVL0 = 1000 });
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = 2, No = 2, PileBodyNo = 1, SoilPileAltNo = 1, X = 5, AxialForceVL0 = 1000 });
            var sp = new SoilPile();
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 2000, D0s = 5 });
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles = [sp];
            return vm;
        }

        /// <summary>
        /// (A) 単杭沈下 → 基礎梁考慮 → 杭の軸力 (VL) を変える → 群杭沈下（基礎梁なし）だけをやり直す。
        /// 単杭沈下と基礎梁考慮の結果は古いままであること。以前は印が 1 つで、群杭沈下を実行した時点で
        /// すべて「最新」になり、古い曲線が次の解析 (P-S ばね・杭頭ばね) に黙って入った。
        /// </summary>
        [TestMethod]
        public void RunningTheGroupSettlement_DoesNotRefreshTheSinglePileOrVerticalBeamResults()
        {
            var vm = SettlementVm();
            vm.IsVerticalAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);
            vm.IsVerticalBeamAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam);
            vm.CaptureAnalysisResultSet();   // 状態表示は解析結果の控えがあるときに出る
            Assert.IsFalse(vm.SettlementResultsAreStale, "(前提) 解いた直後なのに古い結果があります");

            vm.CurrentInputModel!.PileLayoutItems[0].AxialForceVL0 += 100.0;
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.All, "杭の軸力");
            Assert.AreEqual(SettlementAnalyses.SinglePile | SettlementAnalyses.VerticalBeam, vm.StaleSettlementAnalyses,
                "(前提) 結果のある解析だけが古くなること");

            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.Group);

            Assert.IsFalse(vm.IsSettlementResultStale(SettlementAnalyses.Group), "やり直した群杭沈下が古いままです");
            Assert.IsTrue(vm.IsSettlementResultStale(SettlementAnalyses.SinglePile), "群杭沈下だけをやり直したら、単杭沈下まで「最新」になりました");
            Assert.IsTrue(vm.IsSettlementResultStale(SettlementAnalyses.VerticalBeam), "群杭沈下だけをやり直したら、基礎梁考慮まで「最新」になりました");
            // どの解析をやり直すのかを名指しする
            StringAssert.Contains(vm.ResultSetStatusText ?? "", "単杭沈下解析");
            StringAssert.Contains(vm.ResultSetStatusText ?? "", "単杭沈下解析（基礎梁考慮）");
            Assert.IsFalse((vm.ResultSetStatusText ?? "").Contains("群杭沈下解析（基礎梁なし）"), vm.ResultSetStatusText);
            // 古い曲線を杭頭ばねに使うかを尋ねる (基礎梁考慮の入口)
            Assert.IsFalse(vm.ConfirmUsingStaleSettlementResults("単杭沈下解析（基礎梁考慮）", "杭頭ばね", SettlementAnalyses.SinglePile),
                "単杭沈下の曲線が古いのに、尋ねずに通しています (無人では「いいえ」)");
        }

        /// <summary>
        /// (B) 単杭沈下と基礎梁考慮を解いたあと、単杭沈下の画面で α・n を変えて OK (解き直し) すると、
        /// その曲線で解いた基礎梁考慮の結果が古くなること。単杭沈下は最新。
        /// 以前は OK で印をまとめて降ろし、基礎梁考慮の結果が古い曲線のまま「最新」になった。
        /// </summary>
        [TestMethod]
        public void ResolvingTheSinglePileSettlement_MakesTheVerticalBeamResultStale()
        {
            var vm = SettlementVm();
            vm.IsVerticalAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);
            vm.IsVerticalBeamAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam);

            // 単杭沈下の画面の OK と同じ順 (SettlementViewModel.OnOk): 編集の記録 → 単杭沈下を最新に
            vm.CurrentInputModel!.ElementDivision.SoilPiles[0].Dp += 10.0;   // 単杭沈下の入力 (画面の値の代わり)
            vm.SaveUndoState(MainWindowViewModel.AnalysisInputScope.SinglePileSettlement, "単杭沈下 編集");
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);

            Assert.IsFalse(vm.IsSettlementResultStale(SettlementAnalyses.SinglePile), "解き直した単杭沈下が古いままです");
            Assert.IsTrue(vm.IsSettlementResultStale(SettlementAnalyses.VerticalBeam),
                "単杭沈下を解き直したのに、前の曲線で解いた基礎梁考慮の結果が「最新」のままです");

            // 編集なしでも、単杭沈下をやり直せば基礎梁考慮は古くなる (使った曲線が変わる)
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam);
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);
            Assert.IsTrue(vm.IsSettlementResultStale(SettlementAnalyses.VerticalBeam));
        }

        /// <summary>
        /// 群杭沈下（基礎梁なし）の荷重タイプ「個別十字（基礎梁反力）」は基礎梁考慮の反力を荷重に使う。
        /// 基礎梁考慮をやり直すと、その記録は古くなる。ほかの荷重タイプの記録は古くならない。
        /// 群杭沈下の入力だけの編集では、単杭沈下・基礎梁考慮は古くならない。
        /// </summary>
        [TestMethod]
        public void TheGroupSettlementDependsOnTheVerticalBeamOnlyThroughBeamReactions()
        {
            foreach (var (type, dependent) in new[] { ("個別十字（基礎梁反力）", true), ("個別十字", false) })
            {
                var vm = SettlementVm();
                var pgs = vm.CurrentInputModel!.PileGroupSettlement ??= new PileGroupSettlement();
                pgs.CaseRecords = [new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = type, PileSettlements_mm = new() { [1] = 1, [2] = 2 } }];
                vm.IsVerticalBeamAnalysisDone = true;
                vm.IsGroupPileSettlementAnalysisDone = true;
                vm.MarkSettlementResultsCurrent(SettlementAnalyses.All);

                vm.MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam);
                Assert.AreEqual(dependent, vm.IsSettlementResultStale(SettlementAnalyses.Group), $"{type}: 基礎梁考慮のやり直しとの依存");
            }

            var v = SettlementVm();
            v.IsVerticalAnalysisDone = true;
            v.IsVerticalBeamAnalysisDone = true;
            v.MarkSettlementResultsCurrent(SettlementAnalyses.All);
            v.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement, "矩形荷重 編集");
            Assert.IsFalse(v.SettlementResultsAreStale, "群杭沈下の入力だけの編集で、単杭沈下・基礎梁考慮まで古くなりました");
        }

        /// <summary>沈下系の解析の確定は、それぞれ自分の解析の印だけを降ろす (呼び出し側の網)。</summary>
        [TestMethod]
        public void EachSettlementAnalysis_MarksOnlyItsOwnResultCurrent()
        {
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs"), "MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile)");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ModelEditing.cs"), "MarkSettlementResultsCurrent(SettlementAnalyses.Group)");
            string main = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");
            StringAssert.Contains(main, "MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam)");
            StringAssert.Contains(main, "MarkSettlementResultsCurrent(SettlementAnalyses.GroupWithBeam)");
            foreach (var file in new[] { "SettlementViewModel.cs", "MainWindowViewModel.ModelEditing.cs", "MainWindowViewModel.cs" })
                Assert.IsFalse(TestSource.Read("Graphics_r1", "ViewModels", file).Contains("MarkSettlementResultsCurrent(SettlementAnalyses.All)", StringComparison.Ordinal),
                    $"{file}: 1 つの解析の確定で沈下系の印をまとめて降ろしています");
        }

        // ── 群杭沈下（基礎梁あり）の確定 ─────────────────────────────

        /// <summary>
        /// 前回の結果を読み込んだだけ (この画面で解析していない) で保存しても、記録を作り直さない。
        /// 以前は記録を作り直し、コンタ図を<b>今の</b>沈下用土層・格子で計算し直していた (杭の値は前回のまま)。
        /// </summary>
        [TestMethod]
        public void GroupSettlementWithBeam_SavingWithoutRerunning_KeepsTheStoredRecords()
        {
            var vm = SettlementVm();
            var pgs = vm.CurrentInputModel!.PileGroupSettlement ??= new PileGroupSettlement();
            var grid = new ObservableCollection<SettlementGridDataItem> { new() { X = 1, Y = 2, Settlement = 3.5 } };
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "矩形荷重 (VL)", LoadingType = "個別矩形（基礎梁考慮）", IsBeamAware = true, IsConverged = true,
                PileSettlements_mm = new() { [1] = 4, [2] = 5 }, PileReactions_kN = new() { [1] = 900, [2] = 1100 },
                SpringStiffness = new() { [1] = 1e5, [2] = 1e5 }, SettlementGridData = grid,
            };
            pgs.CaseRecords = [record];

            var dialog = new GroupSettlementWithBeamCalculationViewModel(vm);
            Assert.IsTrue(dialog.CanSave, "(前提) 前回の結果を読み込んでいません");
            Assert.IsFalse(dialog.HasFreshResults);
            dialog.OkCommand.Execute(null);

            Assert.IsFalse(dialog.IsSaved, "解析していないのに、保存した (結果を最新にする) 扱いです");
            Assert.AreSame(record, pgs.CaseRecords.Single(), "解析していないのに記録を作り直しました");
            Assert.AreSame(grid, record.SettlementGridData, "解析していないのに、コンタ図を今の入力で計算し直しました");
            Assert.AreEqual(3.5, record.SettlementGridData.Single().Settlement);
        }

        /// <summary>
        /// 群杭沈下（基礎梁あり）を解いて保存したら、ほかの解析と同じく結果を最新として記録し、条件を残す。
        /// 群杭沈下（基礎梁なし）の条件の記録とは別に持つ (片方を実行してももう片方の記録を消さない)。
        /// </summary>
        [TestMethod]
        public void GroupSettlementWithBeam_FreshRunIsRecordedSeparately()
        {
            string main = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");
            string body = TestSource.MethodBody(main, "public void OpenGroupSettlementWithBeamWindow(");
            StringAssert.Contains(body, "if (vm.IsSaved)");
            StringAssert.Contains(body, "MarkSettlementResultsCurrent(SettlementAnalyses.GroupWithBeam);");
            StringAssert.Contains(body, "CaptureAnalysisResultSet(vm.LastRunSettings);");

            var without = new PileDesign.Models.AnalysisRunMetadata { Kind = PileDesign.Models.AnalysisKind.GroupSettlement };
            var with = new PileDesign.Models.AnalysisRunMetadata
            {
                Kind = PileDesign.Models.AnalysisKind.GroupSettlement,
                Variant = PileDesign.Models.AnalysisRunMetadata.GroupWithBeamVariant,
            };
            var merged = PileDesign.Models.AnalysisRunMetadata.Merge([without], with, _ => true);
            Assert.AreEqual(2, merged.Count, "基礎梁ありの条件が、基礎梁なしの条件の記録を消しました");
            Assert.AreEqual("群杭沈下解析（基礎梁あり）", merged[1].Label());
            Assert.AreEqual(1, PileDesign.Models.AnalysisRunMetadata.Merge(merged, new() { Kind = PileDesign.Models.AnalysisKind.GroupSettlement }, _ => true)
                .Count(r => r.Variant == ""), "同じ解析の前の記録を差し替えていません");
        }

        // ── 保存と読込 ─────────────────────────────

        private static object? Field(object obj, string name)
            => obj.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(obj);

        private static void RunOnSta(Action action)
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut, timeoutSeconds: 180);
            Assert.IsFalse(timedOut, "時間切れになりました");
            if (ex is AssertInconclusiveException) throw ex;
            if (ex != null) throw new AssertFailedException(ex.ToString(), ex);
        }

        /// <summary>保存してから開き直す (自動保存・手動保存と同じ確定の処理を通す)。</summary>
        private static MainWindowViewModel SaveAndReopen(MainWindowViewModel vm, Action? beforeLoad = null)
        {
            var files = (FileOperationService)Field(vm, "_fileOperationService")!;
            var prepared = files.PrepareSave(vm.CurrentInputModel, vm.CurrentModel, vm.VerticalBeamCaseResults?.ToList(),
                vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.CapturedAt,
                PileDesign.Models.PileFemLinkTable.Build(vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.AnaModel),
                vm.IsElementSplit, vm.InputChangedSinceAnalysis, analysisRunRecords: vm.CurrentResultSet?.RunRecords,
                staleSettlementResults: MainWindowViewModel.SettlementAnalysisKeys(vm.StaleSettlementAnalyses),
                horizontalAnalysisSummaryText: vm.DocxOutput.LastAnalysisSummaryText);
            Assert.IsNull(prepared.Error, prepared.Error?.ToString());
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Audit20261011_{Guid.NewGuid():N}.pdjson");
            System.IO.File.WriteAllBytes(path, prepared.Payload!);
            try
            {
                beforeLoad?.Invoke();
                var data = files.LoadProjectData(path);
                var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                return opened;
            }
            finally { System.IO.File.Delete(path); }
        }

        /// <summary>例題 3.4 を組み、全土層-杭セットの単杭沈下を解いた画面 (水平解析のモデルも組む。解かない)。</summary>
        private static MainWindowViewModel? SolvedExample(string ground = "Example3_4", string pile = "PileExample3_4")
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel(ground, pile);
            if (model == null) return null;
            var vm = new MainWindowViewModel();
            vm.CurrentInputModel = model;
            model.AttachViewModel(vm);
            foreach (var sp in model.ElementDivision.SoilPiles)
            {
                var vtm = new VerticalLoadTransferMethod(model, sp);
                sp.LoadDisplacements = vtm.LoadDisplacements;
                sp.LoadDisplacementsLimit = vtm.LoadDisplacementsLimit;
                sp.NodeDisplacements = vtm.Ds;
                sp.NodeReactions = vtm.Rs;
            }
            vm.IsElementSplit = true;
            vm.IsVerticalAnalysisDone = true;
            vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);
            var modelling = new AnalysisModelling(model);
            vm.CurrentModel = new AnaModel(model, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();
            return vm;
        }

        /// <summary>
        /// 単杭沈下の節点の履歴 (各荷重段階の全節点の変位・反力) と水平解析のサマリーが、保存して開き直しても残る。
        /// 以前はどちらも保存しておらず、開き直すと単杭の変形・反力の図が空になり、杭先端の P-S ばねも付かず、
        /// 計算書から水平解析のサマリーが消えた。沈下系の解析ごとの「古い」の印も保存する。
        /// </summary>
        [TestMethod]
        public void NodeHistories_SummaryText_AndStaleSettlementKinds_SurviveSaveAndReopen()
        {
            RunOnSta(() =>
            {
                var vm = SolvedExample();
                if (vm == null) { Assert.Inconclusive("例題を組めません"); return; }
                vm.DocxOutput.LastAnalysisSummaryText = "水平解析のサマリー (試験)";
                var before = vm.CurrentInputModel!.ElementDivision.SoilPiles.Select(s => (s.NodeDisplacements!, s.NodeReactions!)).ToList();
                // 基礎梁考慮だけを古くしておく (単杭沈下は最新)
                vm.IsVerticalBeamAnalysisDone = true;
                vm.VerticalBeamCaseResults = [new VerticalBeamCaseResult { LoadCaseName = "VL" }];
                vm.MarkSettlementResultsCurrent(SettlementAnalyses.VerticalBeam);
                vm.MarkSettlementResultsCurrent(SettlementAnalyses.SinglePile);   // → 基礎梁考慮が古くなる
                Assert.AreEqual(SettlementAnalyses.VerticalBeam, vm.StaleSettlementAnalyses, "(前提)");

                var opened = SaveAndReopen(vm);

                var sets = opened.ResultInputModel.ElementDivision.SoilPiles;
                Assert.AreEqual(before.Count, sets.Count);
                for (int i = 0; i < sets.Count; i++)
                {
                    var sp = sets[i];
                    Assert.IsNotNull(sp.NodeDisplacements, $"セット{i + 1}: 節点の変位の履歴が開き直すと消えます");
                    Assert.AreEqual(before[i].Item1.Count, sp.NodeDisplacements!.Count);
                    for (int s = 0; s < sp.NodeDisplacements.Count; s++)
                    {
                        CollectionAssert.AreEqual(before[i].Item1[s].ToArray(), sp.NodeDisplacements[s].ToArray(), $"セット{i + 1} 段{s}: 変位が往復で変わりました");
                        CollectionAssert.AreEqual(before[i].Item2[s].ToArray(), sp.NodeReactions![s].ToArray(), $"セット{i + 1} 段{s}: 反力が往復で変わりました");
                    }
                    // 結果の図 (MainWindow.CanvasResultsVertical) が読む補間が引けること
                    Assert.IsNotNull(sp.GetFullDisplacementForLoad(sp.LoadDisplacements[1].PileTopLoad), "変形の図が引けません");
                    Assert.IsNotNull(sp.GetFullReactionForLoad(sp.LoadDisplacements[1].PileTopLoad), "反力の図が引けません");
                }
                // 杭先端の P-S ばね (鉛直の節点ばね) が付く
                opened.CurrentInputModel!.UsePsSpringAtPileTip = true;
                var modelling = new AnalysisModelling(opened.CurrentInputModel);
                var should = typeof(AnalysisModelling).GetMethod("ShouldApplyVerticalSpringsToPile",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                foreach (var sp in opened.CurrentInputModel.ElementDivision.SoilPiles)
                    Assert.IsTrue((bool)should.Invoke(modelling, [sp])!, "開き直すと杭先端の P-S ばねが付きません");

                Assert.AreEqual("水平解析のサマリー (試験)", opened.DocxOutput.LastAnalysisSummaryText, "開き直すと水平解析のサマリーが消えます");
                Assert.AreEqual(SettlementAnalyses.VerticalBeam, opened.StaleSettlementAnalyses,
                    "沈下系の解析ごとの「古い」の印が開き直すと変わります");
            });
        }

        /// <summary>
        /// 解析結果の控え (解析したときの入力) の断面の Ec は、控え自身の基本設定 (Ec の算定に ξ = 1.0) で求める。
        /// 以前は読込の時点の静的なオプション (前に開いていた作業のもの) で導いたまま、控えには合わせ直さなかった。
        /// </summary>
        [TestMethod]
        public void TheResultSnapshotEc_FollowsItsOwnOptionsAfterLoading()
        {
            bool optionBefore = ConcreteModelOptions.UseUnitGsiForConcreteE;
            try
            {
                RunOnSta(() =>
                {
                    var vm = SolvedExample("Example9", "PileExample9");   // 場所打ちの杭
                    if (vm == null) { Assert.Inconclusive("例題を組めません"); return; }
                    var input = vm.CurrentInputModel!;
                    var section = input.PileBodies.SelectMany(b => b.PileBodySegments).Select(s => s.PileSection)
                        .FirstOrDefault(s => s != null && s.PileBodyType != PileDesign.Constants.PileTypeNames.PrecastConcrete);
                    if (section == null) { Assert.Inconclusive("場所打ちの断面がありません"); return; }
                    if (section.ConcreteGsi == 1.0) section.ConcreteGsi = 0.75;   // ξ の設定で Ec が変わるように

                    // この作業は「Ec の算定に ξ = 1.0」。解析して、そのあと入力を編集する (控えが別の実体になる)
                    input.FundamentalInput.UseUnitGsiForConcreteE = true;
                    vm.ApplyConcreteModelOptions();
                    vm.CaptureAnalysisResultSet();
                    double ecUnit = section.ConcreteE;
                    input.PileLayoutItems[0].AxialForceVL0 += 100.0;
                    vm.SaveUndoState("杭の軸力");
                    Assert.AreNotSame(vm.CurrentInputModel, vm.ResultInputModel, "(前提) 控えが別の実体になっていません");

                    // 前に開いていた作業の設定は「実際の ξ」(読込の時点の静的なオプション)
                    var opened = SaveAndReopen(vm, beforeLoad: () => ConcreteModelOptions.UseUnitGsiForConcreteE = false);
                    Assert.AreNotSame(opened.CurrentInputModel, opened.ResultInputModel, "(前提) 開き直した控えが別の実体ではありません");

                    var snapSection = opened.ResultInputModel.PileBodies.SelectMany(b => b.PileBodySegments).Select(s => s.PileSection)
                        .First(s => s != null && s.PileBodyType == section.PileBodyType && s.ConcreteFc == section.ConcreteFc && s.ConcreteGsi == section.ConcreteGsi);
                    double ecActualGsi = 3.35e4 * Math.Pow(snapSection.ConcreteGamma / 24.0, 2.0) * Math.Pow(snapSection.ConcreteGsi * snapSection.ConcreteFc / 60.0, 1.0 / 3.0);
                    Assert.AreNotEqual(ecActualGsi, ecUnit, 1e-6, "(前提) ξ の設定で Ec が変わらない断面です");
                    Assert.AreEqual(ecUnit, snapSection.ConcreteE, 1e-9,
                        "解析結果の控えの Ec が、控えの設定 (ξ = 1.0) ではなく前の作業の設定で求められています");
                });
            }
            finally
            {
                ConcreteModelOptions.UseUnitGsiForConcreteE = optionBefore;
                PileSection.ClearMphiCache();
            }
        }
    }
}
