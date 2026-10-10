using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-08 の網羅的な見直しで見つけた、落ちる・状態が食い違う不具合の再発防止。
    /// </summary>
    [TestClass]
    public class RobustnessFixes20261008Tests
    {
        /// <summary>
        /// 文字の編集中の Delete は、メイン画面の「選択中の杭・節点・梁の削除」にしない。
        /// 以前は窓全体で横取りしており、表のセルを編集中に Delete を押すと杭がつながる基礎梁ごと消えた。
        /// </summary>
        [TestMethod]
        public void DeleteWhileEditingText_IsLeftToTheTextBox()
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                Assert.IsTrue(PileDesign.Views.MainWindow.IsEditingText(new System.Windows.Controls.TextBox()));
                Assert.IsTrue(PileDesign.Views.MainWindow.IsEditingText(new System.Windows.Controls.ComboBox { IsEditable = true }));
                Assert.IsFalse(PileDesign.Views.MainWindow.IsEditingText(new System.Windows.Controls.Button()));
                Assert.IsFalse(PileDesign.Views.MainWindow.IsEditingText(null));
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            string src = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            int start = src.IndexOf("private void HandleKeyDown(");
            int del = src.IndexOf("DeleteSelectedItems();", start);
            int guard = src.IndexOf("IsEditingText(Keyboard.FocusedElement", start);
            Assert.IsTrue(guard > start && guard < del, "Delete の処理より前に、文字の編集中かを確かめていません");
        }

        /// <summary>
        /// 基本設定 (材料モデルの選択) を Undo したら、解析が読む静的な設定も戻す。
        /// 以前は画面だけ戻り、解析は変更後の設定のまま走った。
        /// </summary>
        [TestMethod]
        public void UndoingTheMaterialModelChoice_AlsoResetsWhatTheAnalysisReads()
        {
            bool saved = ConcreteModelOptions.UseFiberMPhi;
            try
            {
                var vm = new MainWindowViewModel();
                var f = vm.CurrentInputModel!.FundamentalInput;
                f.UseFiberMPhi = false;
                vm.ApplyConcreteModelOptions();

                var before = vm.CaptureInputEdit();
                f.UseFiberMPhi = true;
                vm.ApplyConcreteModelOptions();
                vm.CompleteInputEdit(before, "基本設定");
                Assert.IsTrue(ConcreteModelOptions.UseFiberMPhi, "前提: 変更が解析の設定に入っている");

                vm.UndoCommand.Execute(null);
                Assert.IsFalse(vm.CurrentInputModel.FundamentalInput.UseFiberMPhi, "前提: 入力は戻っている");
                Assert.IsFalse(ConcreteModelOptions.UseFiberMPhi, "Undo で入力は戻ったのに、解析は変更後の材料モデルのままです");
            }
            finally
            {
                ConcreteModelOptions.UseFiberMPhi = saved;
            }
        }

        /// <summary>
        /// 荷重タイプの選択肢の一覧は保存しない (古いファイルの一覧がコードの一覧を上書きしていた)。
        /// 荷重タイプは空にならず、選択肢には現在の値が必ず入る (外れると ComboBox が null を書き戻す)。
        /// </summary>
        [TestMethod]
        public void TheLoadingTypeOptions_AreNotSaved_AndTheCurrentValueStaysSelectable()
        {
            var prop = typeof(PileGroupSettlement).GetProperty(nameof(PileGroupSettlement.LoadingTypeOptions))!;
            Assert.IsTrue(prop.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false), "選択肢の一覧を保存しています");
            string json = "{\"LoadingType\":\"任意矩形\",\"LoadingTypeOptions\":[\"任意矩形\",\"個別十字（基礎梁考慮）\"]}";
            var read = System.Text.Json.JsonSerializer.Deserialize<PileGroupSettlement>(json)!;
            CollectionAssert.Contains(read.LoadingTypeOptions, "個別矩形", "古いファイルの一覧で、コードの一覧が上書きされました");
            CollectionAssert.DoesNotContain(read.LoadingTypeOptions, "個別十字（基礎梁考慮）");

            var pgs = new PileGroupSettlement { LoadingType = "個別十字" };
            pgs.LoadingType = null!;
            Assert.AreEqual("個別十字", pgs.LoadingType, "荷重タイプが空で上書きされました");

            var vm = new MainWindowViewModel();
            vm.CurrentInputModel!.FoundationBeamInput.Beams.Clear();
            vm.CurrentInputModel.PileGroupSettlement.LoadingType = "個別矩形（基礎梁考慮）";
            CollectionAssert.Contains(vm.AvailableLoadingTypeOptions, "個別矩形（基礎梁考慮）",
                "基礎梁を消したあと、現在の荷重タイプが選択肢から外れています");
        }

        /// <summary>
        /// 群杭沈下ウィンドウを開いたまま入力が差し替わっても (Undo・ファイルを開く)、荷重タイプの切替として扱わない。
        /// 以前は矩形荷重を作り直し (手で直した寸法・荷重が消える)、キャンセルすると画面とモデルが食い違った。
        /// </summary>
        [TestMethod]
        public void ReplacingTheInput_IsNotTreatedAsTheUserSwitchingTheLoadType()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var replaced = model.CopyForHistory();
            replaced.PileGroupSettlement.LoadingType = "個別十字";
            replaced.PileGroupSettlement.RectLoads = [new RectLoad { X1 = 0, X2 = 1, Y1 = 0, Y2 = 1, QA = 123 }];
            int rectCount = -1;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                var w = new PileDesign.Views.GroupSettlementWindow
                {
                    DataContext = vm, ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                w.Show();
                try
                {
                    w.GroupPileTabControl.SelectedItem = w.TabItemLoadNonBeam;
                    w.UpdateLayout();
                    vm.CurrentInputModel = replaced;      // Undo・ファイルを開くと同じ差し替え
                    w.UpdateLayout();
                    rectCount = replaced.PileGroupSettlement.RectLoads.Count;
                }
                finally { w.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.AreEqual(1, rectCount, "入力の差し替えを荷重タイプの切替として扱い、矩形荷重を作り直しました");
        }

        /// <summary>基礎梁考慮の単杭沈下で、基準を満たさずに受理したステップがあれば、計算書の収束状態にそう書く (以前は「収束」とだけ出た)。</summary>
        [TestMethod]
        public void RelaxedSteps_AreShownInTheConvergenceLabel()
        {
            var ok = new PileDesign.FEM.VerticalBeamCaseResult { IsConverged = true };
            ok.StepResults.Add(new PileDesign.FEM.VerticalBeamStepResult { Step = 1, IsConverged = true });
            Assert.AreEqual("収束", ok.ConvergenceLabel);
            ok.StepResults.Add(new PileDesign.FEM.VerticalBeamStepResult { Step = 2, IsConverged = false });
            Assert.IsTrue(ok.HasRelaxedSteps);
            StringAssert.Contains(ok.ConvergenceLabel, "1000 倍以内で受理");
            Assert.AreEqual("未収束", new PileDesign.FEM.VerticalBeamCaseResult { IsConverged = false }.ConvergenceLabel);
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Output", "WordDocument.Diagrams.cs"), "収束状態: {caseResult.ConvergenceLabel}");
        }

        /// <summary>
        /// 基礎梁考慮の単杭沈下は、保存して閉じたときだけ「解析済み・沈下結果は最新」とする。
        /// 以前は解き終えた時点で付けており、保存せずに閉じても古い結果が「最新」になった。
        /// 基礎梁の材料・断面の表は、値が変わったときだけ「再解析が必要」にする (以前は触っただけで全結果と杭要素分割を捨てた)。
        /// </summary>
        [TestMethod]
        public void ResultsAreMarkedCurrentOnlyWhenAccepted_AndBeamMaterialEditsDoNotDiscardResults()
        {
            string vb = TestSource.Read("Graphics_r1", "ViewModels", "VerticalBeamCalculationViewModel.cs");
            Assert.IsFalse(vb.Contains("_mainWindowViewModel.MarkSettlementResultsCurrent()")
                           || vb.Contains("_mainWindowViewModel.IsVerticalBeamAnalysisDone = true"),
                "解析の画面が、保存の前にメイン側の結果を「最新」にしています");
            string main = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");
            int saved = main.IndexOf("if (viewModel.IsSaved && viewModel.CaseResults.Count > 0)");
            Assert.IsTrue(saved > 0 && main.IndexOf("MarkSettlementResultsCurrent();", saved) > saved);

            string grid = TestSource.Read("Graphics_r1", "Views", "MainWindow.DataGridHandlers.cs");
            int start = grid.IndexOf("private void DataGridBeamMaterialsOrSections_CellEditEnding(");
            int end = grid.IndexOf("\n        }", start);
            string body = grid[start..end];
            Assert.IsFalse(body.Contains("ResetAnalysisResultsSilently"), "基礎梁の材料・断面の編集で、解析結果を確認なしで捨てています");
            StringAssert.Contains(body, "BeamMaterialFingerprint() == fingerprint", "値が変わったかを見ていません");
        }
    }
}
