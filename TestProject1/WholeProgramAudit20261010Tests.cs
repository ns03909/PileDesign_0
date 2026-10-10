using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-10 のプログラム全体の見直し (第 4 回) で直した、解析の数値を変えない不具合の再発防止。
    /// </summary>
    [TestClass]
    public class WholeProgramAudit20261010Tests
    {
        /// <summary>
        /// 基礎梁の鉛直解析のモデルは、基礎梁につながっていない一般節点を入れない (水平解析のモデルと同じ)。
        /// 入れると剛性の入らない自由な節点が残り、剛性行列が特異になって求解に失敗した。
        /// </summary>
        [TestMethod]
        public void VerticalBeamModel_LeavesOutUnconnectedGeneralNodes()
        {
            var piles = Enumerable.Range(0, 2).Select(i => new PileLayoutDataItem { No = i + 1, X = i * 5.0, Y = 0, Z = 0 }).ToArray();
            var input = new InputModel
            {
                PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles),
                FoundationBeamInput = new FoundationBeamInput
                {
                    Materials = [new BeamMaterial { YoungModulus = 2.5e7, PoissonRatio = 0.2 }],
                    Sections = [],
                    Beams = [],
                },
                InputNodes = [new InputNode { No = 7, Type = NodeType.General, X = 20, Y = 20, Z = 0 }],
                FundamentalInput = new FundamentalInput(),
            };
            input.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = piles[0].UniqueId,
                NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = piles[1].UniqueId,
                MaterialNo = 1, Width = 0.5, Height = 1.0, YoungModulus = 2.5e7,
            });
            Assert.AreEqual(1, input.GetUnconnectedGeneralNodes().Count, "前提: 一般節点 7 は基礎梁につながっていない");

            var modelling = new VerticalBeamModelling(input);
            Assert.IsFalse(modelling.Nodes.Any(n => n.Name == "InputNode-7"),
                "基礎梁につながっていない一般節点が鉛直解析のモデルに入っています (剛性行列が特異になる)");
            Assert.IsTrue(modelling.Nodes.Count > 0, "前提: モデルが組めている");
        }

        /// <summary>
        /// 解析結果の控えは、結果や入力に NaN があっても作れる (保存・DeepCopy と同じく NaN・∞ を書く)。
        /// 以前は書き出しが例外になって控えを作れず、結果が編集中の入力で描かれ、編集すると計算書を出せなくなった。
        /// </summary>
        [TestMethod]
        public void ResultSnapshot_IsCapturedEvenWithNaN()
        {
            var input = new InputModel
            {
                PileLayoutItems = [new PileLayoutDataItem { No = 1, PileNo = 1, SinglePileSettlementVL = double.NaN }],
            };
            var set = PileDesign.Models.AnalysisResultSet.Capture(input, null, null,
                hasHorizontal: false, hasVertical: true, hasGroupPileSettlement: false, hasVerticalBeam: false,
                isElementSplit: false, out var failure);
            Assert.IsNotNull(set, $"NaN を含むと解析結果の控えを作れません: {failure?.Message}");
            Assert.IsTrue(double.IsNaN(set.InputSnapshot.PileLayoutItems[0].SinglePileSettlementVL), "NaN が控えに残っていません");
        }

        /// <summary>
        /// 解く荷重ケースの判定 (慣性力 0 でも地盤変位を与えるなら解く) を、ケースループと件数の数え方で共有する。
        /// 以前は数える側が慣性力だけを見ており、地盤変位だけのケースしかないと実行できなかった。
        /// </summary>
        [TestMethod]
        public void CaseCounting_SharesTheLoopsSkipRule()
        {
            string vm = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            Assert.IsFalse(vm.Contains("x.UpperMassForce != 0 || x.FoundationMassForce != 0", System.StringComparison.Ordinal),
                "件数の数え方が慣性力だけで解くケースを決めています");
            Assert.IsFalse(vm.Contains("lc.UpperMassForce == 0 && lc.FoundationMassForce == 0", System.StringComparison.Ordinal),
                "追加実行の未計算件数が慣性力だけで解くケースを決めています");
            StringAssert.Contains(run, "!HasLoadToRun(loadCase, null)");
            StringAssert.Contains(run, "!HasLoadToRun(loadCase, loadCombination)");
            foreach (var sig in new[] { "public int TotalCalculationCount", "public int TotalLoadCaseCount" })
                StringAssert.Contains(TestSource.Region(vm, sig, 1500), "CountCaseCombinationsToRun(", $"{sig} が共通の判定を使っていません");
        }

        /// <summary>計算書の水平解析サマリーは、OK で登録した解析のもの (完了しただけの解析で書き換えない)。</summary>
        [TestMethod]
        public void AnalysisSummaryText_IsPublishedOnlyWhenTheResultIsRegistered()
        {
            string summary = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Summary.cs");
            Assert.IsFalse(summary.Contains("DocxOutput.LastAnalysisSummaryText = summaryText", System.StringComparison.Ordinal),
                "解析が完了しただけで計算書のサマリーを書き換えています");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "vm.DocxOutput.LastAnalysisSummaryText = LastRunSummaryText;");
        }

        /// <summary>計算書出力ウィンドウの「解析済み」は、荷重ケース・組合せを番号で照合する (名前は係数や改名で変わる)。</summary>
        [TestMethod]
        public void DocxAnalyzedFlags_MatchByNumber()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ResultsWindows.cs"),
                "private void UpdateDocxOutputAnalyzedFlags()");
            Assert.IsFalse(body.Contains(".Name)", System.StringComparison.Ordinal) || body.Contains("LoadName)", System.StringComparison.Ordinal),
                "解析済みの照合に名前を使っています");
            StringAssert.Contains(body, "analyzedCombNos.Contains(comb.No)");
        }

        /// <summary>
        /// P-S ばねの出所の設定は複製で落ちない (保存ファイルの値を消さない)。
        /// 解析はこの設定を使わないので、計算書は「常時」「極限」を書き分けない。
        /// </summary>
        [TestMethod]
        public void PsSpringSource_SurvivesDeepCopy_AndTheReportDoesNotClaimIt()
        {
            var input = new InputModel { PsSpringSource = PsSpringSourceMode.Ultimate, UsePsSpringAtPileTip = true };
            Assert.AreEqual(PsSpringSourceMode.Ultimate, input.DeepCopy().PsSpringSource, "複製で既定へ落ちています");
            var row = PileDesign.Output.WordDocument.BuildDesignConditionRows(input).Single(r => r.Item == "杭先端の鉛直ばね");
            Assert.IsFalse(row.Value.Contains("極限", System.StringComparison.Ordinal), "解析が使っていない「極限」を計算書に書いています");
        }



        /// <summary>動かす瞬間に項目を 1 つ消すコレクション (別の書き手とのすれ違いの再現)。</summary>
        private sealed class ShrinksOnMove : ObservableCollection<string>
        {
            public ShrinksOnMove(params string[] items) : base(items) { }
            protected override void MoveItem(int oldIndex, int newIndex)
            {
                RemoveAt(Count - 1);
                base.MoveItem(oldIndex, newIndex);
            }
        }

        /// <summary>
        /// 解析結果の表示内容の並べ替えは、位置を調べてから動かすまでに候補が減っても例外を外へ出さない (諦めて次の変更でやり直す)。
        /// 以前は Dispatcher の未処理例外になった (2026-10-10 の全体テストで発生)。
        /// </summary>
        [TestMethod]
        public void ContentReorder_GivesUpQuietlyWhenTheListShrinksMidMove()
        {
            var live = new ShrinksOnMove("A", "B", "C");
            PileDesign.ViewModels.MainWindowViewModel.ApplyContentOrder(live, ["C", "A", "B"]);
        }

        // ── F.T.Pile の FT キャップ未選択 (2026-10-10 利用者の依頼) ─────────────────

        /// <summary>例題の最初の杭体を F.T.Pile 構法にして、FT キャップを選んでいない状態にする。</summary>
        private static (InputModel Input, PileBodyInput Body)? FTPileWithoutCap()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (input == null) return null;
            var body = input.PileBodies[0];
            body.PileTopType = "FT-Pile構法";
            body.PileTop.FTPile = new PileDesign.Models.FTPile(24.0, 22669.0);
            Assert.IsTrue(body.PileTop.FTPile.IsCapUnselected(), "前提: キャップを選んでいない");
            return (input, body);
        }

        /// <summary>
        /// FT キャップを選んでいない杭体を見つけ、杭径に合うキャップを最初から選んでおき、反映すると許容回転角が求まる。
        /// 入力の注意の一覧にも出す。
        /// </summary>
        [TestMethod]
        public void FTCap_Unselected_IsFoundSuggestedAndApplied()
        {
            var built = FTPileWithoutCap();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (input, body) = built.Value;
            var ft = body.PileTop.FTPile;

            StringAssert.Contains(string.Join(Environment.NewLine, PileDesign.Services.CheckInputData.CollectInputWarnings(input)),
                "FT キャップが選ばれていません", "入力の注意の一覧に出ていません");

            var rows = PileDesign.Services.FTCapSelection.FindUnselected(input);
            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            double diameter = body.PileBodySegments[0].PileSection.PileDiameter;
            Assert.AreEqual(diameter, row.PileDiameter, 1e-9);
            Assert.AreEqual(ft.CapForPileDiameter(diameter)?.Phi, row.SelectedPhi, "杭径に合うキャップを最初から選んでいません");
            Assert.IsTrue(row.Options.Count > 0, "選べるキャップがありません");
            row.SelectedPhi ??= row.Options[0];

            Assert.AreEqual(1, PileDesign.Services.FTCapSelection.Apply(input, rows));
            Assert.IsFalse(ft.IsCapUnselected(), "選んだキャップが反映されていません");
            Assert.AreEqual(row.SelectedPhi!.Value, ft.FTCap.Phi, 1e-9);
            Assert.AreEqual($"{ft.FTCap.Phi}", ft.SelectedFTCapName, "杭頭部の画面の選択欄と食い違います");
            Assert.IsTrue(double.IsFinite(ft.GetAllowableRotationAngle(1_000_000.0)), "キャップを選んでも許容回転角が求まりません");
            Assert.AreEqual(0, PileDesign.Services.FTCapSelection.FindUnselected(input).Count);
        }

        /// <summary>水平解析の前の確認: 取りやめたら解析しない (黙って選ばない)、選んだら反映して続ける。</summary>
        [TestMethod]
        public void FTCap_Unselected_IsAskedBeforeTheHorizontalAnalysis()
        {
            var built = FTPileWithoutCap();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (input, body) = built.Value;
            var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input };
            var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };

            // 画面を出せないテストの実行で、選ぶ手段が無ければ取りやめる
            Assert.IsFalse(vm.EnsureFTCapsSelected(), "選ばずに解析を続けています");
            Assert.IsTrue(body.PileTop.FTPile.IsCapUnselected(), "黙ってキャップを選んでいます");

            // 取りやめ
            int asked = 0;
            vm.FTCapSelectorForTesting = rows => { asked++; return false; };
            Assert.IsFalse(vm.EnsureFTCapsSelected());
            Assert.AreEqual(1, asked);
            Assert.IsTrue(body.PileTop.FTPile.IsCapUnselected());

            // 選んで続ける
            vm.FTCapSelectorForTesting = rows => { foreach (var r in rows) r.SelectedPhi ??= r.Options[0]; return true; };
            Assert.IsTrue(vm.EnsureFTCapsSelected());
            Assert.IsFalse(body.PileTop.FTPile.IsCapUnselected());

            StringAssert.Contains(TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"), "private async Task OnExecuteAnalysisCore(bool additive)"),
                "EnsureFTCapsSelected()", "解析の入口で FT キャップを確かめていません");
        }

        /// <summary>FT キャップを選ぶ画面が開ける (StaticResource・バインドの誤りはビルドを通る)。</summary>
        [TestMethod]
        public void FTCapSelectionDialog_Opens()
        {
            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var rows = new[] { new PileDesign.Services.FTCapSelectionRow(0, "杭体 1", 600.0, [500.0, 600.0]) { SelectedPhi = 500.0 } };
                var window = new PileDesign.Views.FTCapSelectionDialog(rows) { ShowActivated = false };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.IsTrue(rows[0].DiffersFromPileDiameter, "杭径と違うキャップを知らせる印が立ちません");
                }
                finally { window.Close(); }
            }, out bool timedOut);
            Assert.IsFalse(timedOut);
            Assert.IsNull(error, error?.ToString());
        }
    }
}
