using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 結果を表示する画面 (グラフ・結果の表・ダッシュボード) の上部に、表示中の結果が入力変更前のものであることを
    /// 解析した日時と一緒に出す帯。以前は画面下端の状態表示に 1 行出るだけで、結果の画面からは見えなかった。
    /// </summary>
    [TestClass]
    public class ResultStaleBannerTests
    {
        private static readonly DateTime At = new(2026, 10, 4, 9, 30, 0);

        [TestMethod]
        public void TheNotice_NamesOnlyTheStaleResults_WithTheAnalysisTime()
        {
            Assert.IsNull(MainWindowViewModel.BuildResultStaleNotice(false, At, [], false, At, []), "古くなった結果が無いのに帯を出しています");

            string? horizontal = MainWindowViewModel.BuildResultStaleNotice(true, At, ["杭配置", "杭体"], false, null, []);
            Assert.IsNotNull(horizontal);
            StringAssert.Contains(horizontal, "水平解析の結果 (2026/10/04 09:30 に解析)");
            StringAssert.Contains(horizontal, "（杭配置・杭体）");
            StringAssert.Contains(horizontal, "いまの入力に対する結果ではありません");
            Assert.IsFalse(horizontal!.Contains("沈下"), "古くなっていない沈下の結果まで名指ししています");

            string? settlement = MainWindowViewModel.BuildResultStaleNotice(false, At, [], true, At, ["沈下用土層"]);
            StringAssert.Contains(settlement, "沈下の結果 (2026/10/04 09:30 に解析)");
            Assert.IsFalse(settlement!.Contains("水平解析"), "古くなっていない水平解析の結果まで名指ししています");

            string? both = MainWindowViewModel.BuildResultStaleNotice(true, At, [], true, null, []);
            Assert.AreEqual(2, both!.Split('\n').Length, "両方が古いときは 1 行ずつ書く");
        }

        [TestMethod]
        public void ManyEdits_AreShortened()
        {
            var edits = new List<string> { "A", "B", "C", "D", "E" };
            string? notice = MainWindowViewModel.BuildResultStaleNotice(true, At, edits, false, null, []);
            StringAssert.Contains(notice, "（A・B・C ほか 2 件）");
        }

        /// <summary>
        /// 水平解析に効く入力を変えたら帯が出て、水平解析の結果が古いと示す (水平解析を開くボタンが出る)。
        /// 群杭沈下だけに効く入力では、水平解析の結果が古いとは言わない。
        /// </summary>
        [TestMethod]
        public void TheViewModel_SeparatesHorizontalFromSettlementOnlyEdits()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;
            Assert.IsFalse(vm.HasResultStaleNotice, "解析した直後に帯を出しています");

            var raised = new HashSet<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement, "沈下用土層");
            Assert.IsFalse(vm.IsHorizontalResultStale, "群杭沈下だけに効く入力で、水平解析の結果が古いとしています");
            Assert.IsFalse((vm.ResultStaleNotice ?? "").Contains("水平解析"), $"水平解析の結果を名指ししています: {vm.ResultStaleNotice}");

            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Model, "杭配置");
            Assert.IsTrue(vm.IsHorizontalResultStale);
            Assert.IsTrue(vm.HasResultStaleNotice);
            StringAssert.Contains(vm.ResultStaleNotice, "表示中の水平解析の結果");
            StringAssert.Contains(vm.ResultStaleNotice, "杭配置");
            CollectionAssert.IsSubsetOf(new[] { nameof(vm.ResultStaleNotice), nameof(vm.HasResultStaleNotice), nameof(vm.IsHorizontalResultStale) },
                new List<string>(raised), "帯の表示が変わったことを画面へ知らせていません");
        }

        /// <summary>結果を表示する 3 つの画面の上部に帯がある (DataContext はメイン画面の VM)。</summary>
        [DataTestMethod]
        [DataRow("GraphWindow.xaml", "DataContext=\"{Binding MainVm}\"")]
        [DataRow("TableWindow.xaml", "DataContext=\"{Binding MainVm}\"")]
        [DataRow("ResultDashboardWindow.xaml", "")]
        public void ResultWindows_ShowTheBanner(string file, string binding)
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", file);
            StringAssert.Contains(xaml, "<ctrl:ResultStaleBanner DockPanel.Dock=\"Top\"" + (binding.Length > 0 ? " " + binding : "") + "/>");
            string banner = TestSource.Read("Graphics_r1", "Views", "Controls", "ResultStaleBanner.xaml");
            // ボタンは解析を走らせず、水平解析のウィンドウを開く
            StringAssert.Contains(banner, "Command=\"{Binding OpenLateralLoadAnalysisWindowCommand}\"");
        }
    }
}
