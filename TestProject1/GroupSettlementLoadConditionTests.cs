using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の荷重条件 (1 組) と、個別矩形の荷重の決め方。
    ///
    /// <para>個別矩形の荷重 QA は既定で杭の軸力から決める。以前は手で入れた値も解析のたびに軸力で上書きしており、
    /// 与えた荷重とプログラムが決めた値の区別がつかなかった。手で入れた矩形は上書きせず、
    /// 「荷重を軸力から作り直す」で軸力に戻す。</para>
    ///
    /// <para>画面は「荷重条件」(荷重面・荷重タイプ・矩形荷重) と「解析」(基礎梁なし / あり) に分ける。
    /// 以前は「解析（一般）」「解析（反復）」の 2 つのタブにそれぞれ荷重面と矩形荷重の表があり、
    /// どちらを変えるとどの解析に効くのかが分からなかった。</para>
    /// </summary>
    [TestClass]
    public class GroupSettlementLoadConditionTests
    {
        [TestMethod]
        public void ManuallyEnteredLoads_AreNotOverwrittenByTheAxialForce()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var pgs = model.PileGroupSettlement;
            // 計算例5 は荷重面等価径を入れていないので、杭ごとの矩形を作れるように入れる
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            pgs.LoadingType = "個別矩形";
            var linked = pgs.RectLoads.Where(r => r.LinkedPileNo > 0).ToList();
            Assert.IsTrue(linked.Count >= 2, "前提: 杭に紐付く矩形が 2 つ以上ある");

            var manual = linked[0];
            var auto = linked[1];
            manual.QAInput = 1234;                 // 表で入れた (手入力)
            auto.QA = 9999;                        // プログラムが入れた値 (手入力ではない)
            Assert.IsTrue(manual.IsQaManual && manual.QASource == "手入力");
            Assert.IsFalse(auto.IsQaManual);
            Assert.AreEqual("軸力", auto.QASource);

            var result = new SettlementAnalysisService().PerformSettlementAnalysis(
                pgs, model.PileLayoutItems, model.ElementDivision.SoilPiles, model.GridXItems, model.GridYItems,
                -10, 40, -10, 40, 0, 0, 5, 5);
            Assert.IsTrue(result.Success, result.ErrorMessage);

            var after = pgs.RectLoads.ToDictionary(r => r.LinkedPileNo);
            var autoPile = model.PileLayoutItems.First(p => p.PileNo == auto.LinkedPileNo);
            Assert.AreEqual(1234, after[manual.LinkedPileNo].QA, 1e-9, "手で入れた荷重を解析が軸力で上書きしました");
            Assert.AreEqual(autoPile.AxialForceVL0 + autoPile.AxialForceVLAdditional, after[auto.LinkedPileNo].QA, 1e-9,
                "手入力でない荷重が軸力から決まっていません");

            // 「荷重を軸力から作り直す」で手入力も軸力に戻る
            var vm = new MainWindowViewModel { CurrentInputModel = model };
            vm.RebuildRectLoadsFromAxialForceCommand.Execute(null);
            var manualPile = model.PileLayoutItems.First(p => p.PileNo == manual.LinkedPileNo);
            var rebuilt = pgs.RectLoads.First(r => r.LinkedPileNo == manual.LinkedPileNo);
            Assert.AreEqual(manualPile.AxialForceVL0 + manualPile.AxialForceVLAdditional, rebuilt.QA, 1e-9);
            Assert.IsFalse(rebuilt.IsQaManual, "作り直したのに手入力の印が残っています");
        }

        /// <summary>手入力の印は保存・複製で失われない (失うと、開き直した後の解析で手入力が軸力に戻る)。</summary>
        [TestMethod]
        public void TheManualMark_SurvivesCloneAndSave()
        {
            var r = new RectLoad { X1 = 0, X2 = 1, Y1 = 0, Y2 = 1, LinkedPileNo = 3 };
            r.QAInput = 500;
            Assert.IsTrue(r.Clone().IsQaManual, "複製で手入力の印が落ちます");
            string json = System.Text.Json.JsonSerializer.Serialize(r);
            StringAssert.Contains(json, "\"IsQaManual\":true", "保存に手入力の印が入っていません");
            Assert.IsFalse(json.Contains("QAInput") || json.Contains("QASource"), "画面用の値まで保存しています");
        }

        /// <summary>荷重条件・解析のタブを実際に描く (タブの中身は選んだときに組まれるので、開くだけの試験では参照の誤りを踏まない)。</summary>
        [TestMethod]
        public void TheLoadConditionAndAnalysisTabs_Render()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            int rendered = 0;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                var window = new PileDesign.Views.GroupSettlementWindow
                {
                    DataContext = vm, ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                window.Show();
                try
                {
                    foreach (var tab in new[] { window.TabItemLoadNonBeam, window.TabItemLoadBeamAware })
                    {
                        window.GroupPileTabControl.SelectedItem = tab;
                        window.UpdateLayout();
                        rendered++;
                    }
                    vm.IsGroupSettlementWithBeamSelected = true;   // 基礎梁ありの中身も描く
                    window.UpdateLayout();
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.AreEqual(2, rendered);
        }

        /// <summary>
        /// 基礎梁なしの解析は、荷重タイプが「個別矩形（基礎梁考慮）」でも基礎梁ありの画面を開かず、基礎梁なしの結果として記録する。
        /// 以前はこの荷重タイプだと基礎梁ありの画面を開き、そこでキャンセルしても続けて基礎梁なしの計算へ進んでいた。
        /// </summary>
        [TestMethod]
        public void TheNonBeamAnalysis_DoesNotOpenTheBeamWindow_EvenForTheBeamAwareLoadingType()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ModelEditing.cs");
            int start = src.IndexOf("private void PileGroupSettlementAnalysis()");
            Assert.IsTrue(start > 0, "前提: 基礎梁なしの解析の入口が見つかりません");
            int end = src.IndexOf("\n        private ", start + 10);
            Assert.IsFalse(src[start..end].Contains("OpenGroupSettlementWithBeamWindow"),
                "基礎梁なしの解析から基礎梁ありの画面を開いています");

            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            var pgs = model.PileGroupSettlement;
            pgs.LoadingType = "個別矩形（基礎梁考慮）";
            pgs.CaseRecords = [];

            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                Assert.IsTrue(vm.PileGroupSettlementAnalysisCommand.CanExecute(null), vm.GroupSettlementAnalysisDisabledReason);
                vm.PileGroupSettlementAnalysisCommand.Execute(null);
            }
            finally { MessageService.IsUnattended = unattended; }

            var rec = pgs.CaseRecords.SingleOrDefault();
            Assert.IsNotNull(rec, "基礎梁なしの結果が記録されていません");
            Assert.IsFalse(rec.IsBeamAware);
            Assert.AreEqual("個別矩形", rec.LoadingType,
                "基礎梁なしの結果を「個別矩形（基礎梁考慮）」として記録すると、基礎梁ありの結果と取り違えます");
        }

        private static GroupSettlementWithBeamCalculationViewModel BeamVm(InputModel model)
            => new(new MainWindowViewModel { CurrentInputModel = model });

        /// <summary>
        /// 基礎梁ありの解析は、杭に対応しない矩形 (任意矩形・建物全体の矩形) や矩形のない杭があれば解かずに止める。
        /// 以前は対応しない矩形を黙って捨て、1 つも対応しなければ荷重 0 のまま解いていた。
        /// </summary>
        [TestMethod]
        public void TheBeamAnalysis_StopsWhenRectLoadsDoNotMatchThePiles()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            var pgs = model.PileGroupSettlement;
            pgs.LoadingType = "個別矩形";
            var vm = BeamVm(model);
            Assert.AreEqual(GroupSettlementWithBeamCalculationViewModel.SourceRectLoads, vm.LoadSource, "既定は荷重条件の矩形荷重");
            Assert.IsNull(vm.ValidateInput(), "前提: 杭ごとの矩形なら解ける");

            // 杭に対応しない矩形 (任意矩形で足したもの)
            var extra = new RectLoad { X1 = 0, X2 = 1, Y1 = 0, Y2 = 1, QA = 500 };
            pgs.RectLoads.Add(extra);
            string? error = vm.ValidateInput();
            Assert.IsNotNull(error, "杭に対応しない矩形の荷重を黙って捨てて解こうとしています");
            StringAssert.Contains(error, "杭に対応しない矩形が 1 件");
            StringAssert.Contains(error, "500.0 kN");
            StringAssert.Contains(error, "杭ごとの矩形を作り直す");
            // 杭の軸力が入っていないとき (アプリで開く計算例5 がそう)、軸力へ切り替える案内は荷重 0 の解析へ案内することになるので出さない
            foreach (var pile in model.PileLayoutItems) { pile.AxialForceVL0 = 0; pile.AxialForceVLAdditional = 0; }
            error = vm.ValidateInput()!;
            Assert.IsFalse(error.Contains("荷重の出所を「" + GroupSettlementWithBeamCalculationViewModel.SourceAxialForce),
                "軸力が 0 なのに、杭の軸力へ切り替えるよう案内しています");
            StringAssert.Contains(error, "杭の軸力が入っていない");
            pgs.RectLoads.Remove(extra);

            // 矩形のない杭
            var dropped = pgs.RectLoads.First(r => r.LinkedPileNo > 0);
            pgs.RectLoads.Remove(dropped);
            error = vm.ValidateInput();
            Assert.IsNotNull(error, "矩形のない杭を荷重 0 として解こうとしています");
            StringAssert.Contains(error, $"矩形のない杭が 1 本あります (杭No. {dropped.LinkedPileNo})");

            // 任意矩形だけ (杭に 1 つも対応しない) なら、荷重 0 で解かない
            pgs.RectLoads.Clear();
            pgs.RectLoads.Add(extra);
            Assert.IsNotNull(vm.ValidateInput(), "杭に対応する矩形が 1 つもないのに、荷重 0 のまま解こうとしています");

            // 杭の軸力を荷重にするときは荷重条件の矩形を見ない。ただし軸力が 0 なら解かない
            vm.LoadSource = GroupSettlementWithBeamCalculationViewModel.SourceAxialForce;
            error = vm.ValidateInput();
            Assert.IsNotNull(error, "杭の軸力が 0 のまま、荷重 0 で解こうとしています");
            StringAssert.Contains(error, "杭の軸力の合計が 0 のケースがあります (杭軸力 VL)");
            foreach (var pile in model.PileLayoutItems) pile.AxialForceVL0 = 1000;
            Assert.IsNull(vm.ValidateInput(), "杭の軸力が入っていれば、荷重条件の矩形に関わらず解ける");
        }

        /// <summary>杭の軸力を荷重にするとき、地震時のケースも軸力の合計が 0 なら解かない (ケース名を示す)。</summary>
        [TestMethod]
        public void TheBeamAnalysis_StopsWhenASeismicCaseHasNoAxialForce()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var vm = BeamVm(model);
            model.LoadCasesInput.LoadCasesLevel1 = new System.Collections.ObjectModel.ObservableCollection<LoadCase>(
                [new LoadCase { Level = 1, No = 1, LoadName = "E1", IsAnalysisTarget = true, IsApplicable = true }]);
            foreach (var pile in model.PileLayoutItems)
            {
                pile.AxialForceVL0 = 1000;
                pile.AxialForceLevel1s = [0.0];
            }
            vm.LoadSource = GroupSettlementWithBeamCalculationViewModel.SourceAxialForce;
            vm.AnalyzeLevel1 = true;
            string? error = vm.ValidateInput();
            Assert.IsNotNull(error, "地震時の軸力が 0 のケースを解こうとしています");
            StringAssert.Contains(error, "L1-1: E1");
            Assert.IsFalse(error.Contains("杭軸力 VL"), "軸力の入っている VL まで 0 として挙げています");

            foreach (var pile in model.PileLayoutItems) pile.AxialForceLevel1s = [800.0];
            Assert.IsNull(vm.ValidateInput());
        }

        /// <summary>
        /// 基礎梁ありの解析は、同じ杭に紐付く矩形が複数あれば解かずに止める (荷重の出所に関わらず)。
        /// 以前は荷重を合算し、寸法は先頭の矩形のものを黙って使っていた。
        /// </summary>
        [TestMethod]
        public void TheBeamAnalysis_StopsWhenAPileHasTwoRects()
        {
            var (model, ppi) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            var pgs = model.PileGroupSettlement;
            pgs.LoadingType = "個別矩形";
            var vm = BeamVm(model);
            Assert.IsNull(vm.ValidateInput(), "前提: 杭ごとの矩形なら解ける");

            var original = pgs.RectLoads.First(r => r.LinkedPileNo > 0);
            var copy = original.Clone();
            copy.DX = original.DX * 2;
            pgs.RectLoads.Add(copy);

            string? error = vm.ValidateInput();
            Assert.IsNotNull(error, "同じ杭の矩形が 2 つあるのに解こうとしています");
            StringAssert.Contains(error, $"杭No.{original.LinkedPileNo}");
            StringAssert.Contains(error, $"{copy.DX:F3}×", "それぞれの寸法を示していません");

            vm.LoadSource = GroupSettlementWithBeamCalculationViewModel.SourceAxialForce;
            StringAssert.Contains(vm.ValidateInput(), "同じ杭に紐付く矩形荷重が複数あります",
                "杭の軸力でも、矩形の寸法は荷重条件から読むので止めること");

            var result = IterativeBeamSettlementService.Run(model, ppi, "検査");
            Assert.IsFalse(result.Converged);
            Assert.IsTrue(result.Log.Any(l => l.StartsWith("[ERROR] 同じ杭に紐付く矩形荷重が複数あります")),
                "解析の側でも受け付けていません (画面を通らない呼び出し)");
        }

        /// <summary>実行の前に、解析方法・荷重の出所・対象ケース・荷重合計をまとめて示す。杭の軸力では荷重条件の荷重を使わないと書く。</summary>
        [TestMethod]
        public void TheBeamWindow_SummarizesWhatItWillSolve()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            model.PileGroupSettlement.LoadingType = "個別矩形";
            var vm = BeamVm(model);
            double total = model.PileGroupSettlement.RectLoads.Where(r => r.LinkedPileNo > 0).Sum(r => r.QA);

            string s = vm.RunSummaryText;
            StringAssert.Contains(s, "解析方法: 基礎梁あり");
            StringAssert.Contains(s, "荷重の出所: 荷重条件の矩形荷重（VL）");
            StringAssert.Contains(s, "解くケース 1 件");
            StringAssert.Contains(s, $"荷重合計 (VL): {total:F1} kN");
            Assert.IsFalse(s.Contains("使いません"));

            var raised = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.LoadSource = GroupSettlementWithBeamCalculationViewModel.SourceAxialForce;
            CollectionAssert.Contains(raised, nameof(vm.RunSummaryText), "荷重の出所を変えてもまとめが更新されません");
            s = vm.RunSummaryText;
            StringAssert.Contains(s, "荷重の出所: 杭の軸力（VL・L1・L2）");
            StringAssert.Contains(s, "荷重条件タブの矩形荷重 (手入力の荷重を含む) は使いません");
            StringAssert.Contains(s, $"荷重合計 (VL): {model.PileLayoutItems.Sum(p => p.AxialForceVL):F1} kN");

            string xaml = TestSource.Read("Graphics_r1", "Views", "GroupSettlementWithBeamWindow.xaml");
            StringAssert.Contains(xaml, "Text=\"{Binding RunSummaryText}\"");
            Assert.IsTrue(xaml.IndexOf("RunSummaryText") < xaml.IndexOf("ExecuteAnalysisCommand"), "まとめは実行ボタンの上に置く");
        }

        [TestMethod]
        public void TheWindow_HasOneLoadConditionTab_AndAnAnalysisTabWithBeamChoice()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "GroupSettlementWindow.xaml");
            StringAssert.Contains(xaml, "<TabItem Header=\"荷重条件\"");
            StringAssert.Contains(xaml, "<TabItem Header=\"解析\"");
            Assert.IsFalse(xaml.Contains("Header=\"解析（一般）\"") || xaml.Contains("Header=\"解析（反復）\""), "旧いタブが残っています");
            Assert.AreEqual(1, Regex.Matches(xaml, Regex.Escape("ItemsSource=\"{Binding CurrentInputModel.PileGroupSettlement.RectLoads")).Count,
                "矩形荷重の表が 1 つではありません (同じ荷重を 2 か所で編集させない)");
            StringAssert.Contains(xaml, "Binding=\"{Binding QASource, Mode=OneWay}\" Header=\"出所\"");
            StringAssert.Contains(xaml, "Command=\"{Binding RebuildRectLoadsFromAxialForceCommand}\"");
            StringAssert.Contains(xaml, "Content=\"基礎梁なし\"");
            StringAssert.Contains(xaml, "Content=\"基礎梁あり\"");
            StringAssert.Contains(xaml, "<TextBlock Text=\"基礎梁なしで解析\"");
            // 基礎梁ありはこのボタンで解析画面を開くだけ (荷重の出所・ケースを選んでから実行する)。「で解析」だと押すと解けると読める
            StringAssert.Contains(xaml, "<TextBlock Text=\"基礎梁ありの解析設定へ…\"");
            Assert.IsFalse(xaml.Contains("QA は収束値で更新されます") || xaml.Contains("反復計算により自動収束されます"),
                "反復解析が入力の荷重を書き換えるという古い説明が残っています (1.0.33-beta から入力と結果を分けて持っている)");

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(main, "解析（基礎梁なし）");
            StringAssert.Contains(main, "解析（基礎梁あり）");
        }
    }
}
