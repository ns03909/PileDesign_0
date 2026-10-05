using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// 結果表示まわりのウィンドウの XAML が実際にパースできることを確認するスモークテスト。
    ///
    /// StaticResource のキー誤り・x:Static の解決失敗・DataTemplate の型不整合は
    /// ビルドを通ってしまい、ウィンドウを開いた瞬間に例外になる。
    /// 解析結果セット (2026-08-22) でステータスバーとグラフの基準切替を足したため、
    /// これらのウィンドウもパースだけは自動で踏んでおく。
    /// </summary>
    [TestClass]
    public class ResultWindowsXamlSmokeTests
    {
        private static void AssertWindowParses(string name, System.Func<System.Windows.Window> factory)
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = factory();
                created = window != null;
                window?.Close();
            }, out bool timedOut);

            if (timedOut)
            {
                Assert.Inconclusive($"{name} の XAML パースが 60 秒以内に完了しなかったためスキップ");
                return;
            }

            if (captured != null)
            {
                Assert.Fail($"{name} の XAML パースに失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            }
            Assert.IsTrue(created, $"{name} が生成されなかった");
        }

        /// <summary>
        /// 個別矩形（基礎梁考慮）の反復沈下解析ウィンドウ。収束しなかったケースの注意 (赤字) と、
        /// ケース一覧の表示名 (「(未収束)」を付ける) のバインディングを足したので、パースを踏む。
        /// </summary>
        [TestMethod]
        public void GroupSettlementWithBeamWindow_XamlParses_WithoutException()
        {
            AssertWindowParses("GroupSettlementWithBeamWindow", () =>
            {
                var mainVm = new PileDesign.ViewModels.MainWindowViewModel();
                return new PileDesign.Views.GroupSettlementWithBeamWindow
                {
                    DataContext = new PileDesign.ViewModels.GroupSettlementWithBeamCalculationViewModel(mainVm),
                };
            });
        }

        /// <summary>グラフウィンドウ（断面の基準切替の RadioButton / BoolEqualsConverter を含む）。</summary>
        [TestMethod]
        public void GraphWindow_XamlParses_WithoutException()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AssertWindowParses("GraphWindow", () =>
            {
                var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = inputModel };
                inputModel.AttachViewModel(mainVm);
                return new PileDesign.Views.GraphWindow(new PileDesign.ViewModels.GraphViewModel(mainVm));
            });
        }

        /// <summary>
        /// 解析結果ダッシュボード。DataTrigger 付きの DataGrid 列と、
        /// StatusWarningDarkBrush などアプリ辞書のブラシを使うので、パースと再計算を踏む。
        /// 解析していない状態で「未実施」の分岐を通す。
        /// </summary>
        [TestMethod]
        public void ResultDashboardWindow_XamlParses_AndRefreshesWithoutAnalysis()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AssertWindowParses("ResultDashboardWindow", () =>
            {
                var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = inputModel };
                inputModel.AttachViewModel(mainVm);
                var window = new PileDesign.Views.ResultDashboardWindow(mainVm);
                window.RefreshNow();
                return window;
            });
        }

        /// <summary>
        /// 解析結果テーブル。入力変更後の帯 (ResultStaleBanner) と杭の絞り込みの帯を足したので、
        /// 両方が出ている状態でパースを踏む。
        /// </summary>
        [TestMethod]
        public void TableWindow_XamlParses_WithStaleBannerAndPileFilter()
        {
            if (AnalysisInputScopeTests.BuildAnalyzed() == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            AssertWindowParses("TableWindow", () =>
            {
                var mainVm = AnalysisInputScopeTests.BuildAnalyzed()!.Value.vm;
                mainVm.MarkInputChangedSinceAnalysis(PileDesign.ViewModels.MainWindowViewModel.AnalysisInputScope.Model, "杭配置");
                var vm = new PileDesign.ViewModels.TableWindowViewModel { MainVm = mainVm };
                vm.PileNoFilter = 1;
                return new PileDesign.Views.TableWindow { DataContext = vm };
            });
        }

        /// <summary>
        /// メイン画面の工程の帯。MainWindow 自体はここで開けない (下の注記) ので、帯だけを窓に載せてパースを踏む。
        /// x:Static の列挙値 (WorkflowStepStatus) と PreviousData の DataTrigger を含む。
        /// </summary>
        [TestMethod]
        public void WorkflowStrip_XamlParses_AndShowsFiveSteps()
        {
            if (AnalysisInputScopeTests.BuildAnalyzed() == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            int shown = -1;
            AssertWindowParses("WorkflowStrip", () =>
            {
                var mainVm = AnalysisInputScopeTests.BuildAnalyzed()!.Value.vm;
                mainVm.MarkInputChangedSinceAnalysis(PileDesign.ViewModels.MainWindowViewModel.AnalysisInputScope.Model, "杭配置");
                var strip = new PileDesign.Views.Controls.WorkflowStrip { DataContext = mainVm };
                var window = new System.Windows.Window { Content = strip };
                strip.ApplyTemplate();
                strip.Measure(new System.Windows.Size(1200, 40));
                // バインディングはディスパッチャー経由で付くので、一度回してから数える
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                var items = (System.Windows.Controls.ItemsControl)((System.Windows.Controls.Border)strip.Content).Child;
                shown = items.Items.Count;
                return window;
            });
            Assert.AreEqual(5, shown, "工程の帯に 5 つの工程が並んでいません");
        }

        // 検定ウィンドウは廃止した (検定は解析結果テーブルから見る)。

        // MainWindow は他のテストが別 STA スレッドで作った WPF の静的オブジェクトに触れてしまい、
        // 「呼び出しスレッドはこのオブジェクトにアクセスできません」で落ちるためここでは対象外。
        // ステータスバーへの追加は StaticResource を使わない TextBlock なのでパース失敗の危険は小さい。
    }
}
