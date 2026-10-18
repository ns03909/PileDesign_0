using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 表示プリセット (2026-10-16 のレビュー)。リボン「表示」タブの項目をまとめて切り替え、名前を付けて保存する。
    /// </summary>
    [TestClass]
    public class DisplayPresetTests
    {
        [TestMethod]
        public void TheTargets_AreTheDisplayToggles_WithoutWindowChrome()
        {
            var names = MainWindowViewModel.DisplayToggleProperties.Select(p => p.Name).ToHashSet();
            TestSource.AssertScanned(names.Count, 50, "表示のトグル");
            foreach (var expected in new[] { nameof(MainWindowViewModel.IsLabelVisible), nameof(MainWindowViewModel.IsEvaluationColoringVisible),
                                             nameof(MainWindowViewModel.IsMidSpanResultValueVisibleOnly) })
                Assert.IsTrue(names.Contains(expected), $"{expected} がプリセットの対象に入っていません");
            foreach (var chrome in MainWindowViewModel.WindowChromeToggles)
                Assert.IsFalse(names.Contains(chrome), $"ウィンドウの部品 {chrome} をプリセットの対象にしています");
        }

        /// <summary>用意したプリセットの項目名がすべて実在する (書き誤りは当てるときに黙って飛ばされる)。</summary>
        [TestMethod]
        public void BuiltInPresets_NameOnlyExistingToggles()
        {
            var names = MainWindowViewModel.DisplayToggleProperties.Select(p => p.Name).ToHashSet();
            foreach (var (preset, toggles) in MainWindowViewModel.BuiltInDisplayPresets)
                foreach (var key in toggles.Keys)
                    Assert.IsTrue(names.Contains(key), $"プリセット「{preset}」の {key} は表示のトグルではありません");
        }

        [TestMethod]
        public void SaveApplyAndDelete_RoundTrip()
        {
            string file = Path.Combine(Path.GetTempPath(), $"pd_presets_{Guid.NewGuid():N}.json");
            try
            {
                var vm = new MainWindowViewModel();
                vm.UseUserSettingsForTests(new UserSettingsService(file));
                bool labelDefault = vm.IsLabelVisible, nodeNoDefault = vm.IsNodeNoVisible;

                vm.IsLabelVisible = !labelDefault;
                vm.IsNodeNoVisible = !nodeNoDefault;
                Assert.IsTrue(vm.SaveDisplayPreset("  私のプリセット  "));
                Assert.AreEqual("私のプリセット", vm.SelectedDisplayPreset, "保存したプリセットを選んだ状態にしていません");
                CollectionAssert.Contains(vm.DisplayPresetNames, "私のプリセット");
                Assert.IsTrue(vm.DeleteSelectedDisplayPresetCommand.CanExecute(null), "保存したプリセットを削除できません");

                // 手で変えるとプリセットの選択が外れる
                vm.IsTickMarkVisible = !vm.IsTickMarkVisible;
                Assert.IsNull(vm.SelectedDisplayPreset, "プリセットと違う表示になったのにプリセットを選んだままです");

                vm.SelectedDisplayPreset = MainWindowViewModel.DefaultDisplayPresetName;
                Assert.AreEqual(labelDefault, vm.IsLabelVisible, "「既定」で起動時の表示に戻っていません");
                Assert.AreEqual(nodeNoDefault, vm.IsNodeNoVisible);
                Assert.IsFalse(vm.DeleteSelectedDisplayPresetCommand.CanExecute(null), "用意したプリセットを削除できる状態です");

                vm.SelectedDisplayPreset = "私のプリセット";
                Assert.AreEqual(!labelDefault, vm.IsLabelVisible, "保存したプリセットの表示になっていません");
                Assert.AreEqual(!nodeNoDefault, vm.IsNodeNoVisible);
                Assert.AreEqual("私のプリセット", vm.SelectedDisplayPreset, "プリセットを当てた変更で、プリセットの選択が外れています");

                // 次の起動でも読める
                var reread = new UserSettingsService(file).Settings.DisplayPresets.Single();
                Assert.AreEqual("私のプリセット", reread.Name);
                Assert.AreEqual(!labelDefault, reread.Toggles[nameof(MainWindowViewModel.IsLabelVisible)]);

                Assert.IsFalse(vm.SaveDisplayPreset(MainWindowViewModel.DefaultDisplayPresetName), "用意したプリセットの名前で保存できています");
                Assert.IsFalse(vm.DeleteDisplayPreset("入力の確認"), "用意したプリセットを削除できています");
                Assert.IsTrue(vm.DeleteDisplayPreset("私のプリセット"));
                Assert.IsNull(vm.SelectedDisplayPreset);
                CollectionAssert.DoesNotContain(vm.DisplayPresetNames, "私のプリセット");
                Assert.AreEqual(0, new UserSettingsService(file).Settings.DisplayPresets.Count, "削除が保存されていません");
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// 用意したプリセットは挙げた項目だけを切り替え、ほかは今のまま。3D の描き直しは 1 回にまとめる。
        /// 解析結果が無いときは「解析結果表示」を ON にせず、警告も出さない。
        /// </summary>
        [TestMethod]
        public void BuiltInPreset_TouchesOnlyItsItems_AndRedrawsOnce()
        {
            var vm = new MainWindowViewModel();
            vm.UseUserSettingsForTests(new UserSettingsService(Path.Combine(Path.GetTempPath(), $"pd_presets_{Guid.NewGuid():N}.json")));
            Assert.IsFalse(vm.HasAnyAnalysisResultToShow, "(前提) 解析結果が無いこと");
            bool tick = !vm.IsTickMarkVisible;
            vm.IsTickMarkVisible = tick;   // 「結果の確認」に無い項目
            int redraws = 0, messages = 0;
            vm.UpdateCanvas3DAction = () => redraws++;

            bool unattended = MessageService.IsUnattended;
            var answer = MessageService.UnattendedAnswer;
            MessageService.IsUnattended = true;
            MessageService.UnattendedAnswer = (_, _) => { messages++; return null; };
            try { vm.SelectedDisplayPreset = "結果の確認"; }
            finally { MessageService.IsUnattended = unattended; MessageService.UnattendedAnswer = answer; }

            Assert.AreEqual(0, messages, "プリセットを選んだだけで警告を出しています");
            Assert.IsFalse(vm.IsAnalysisResultVisible, "解析結果が無いのに解析結果表示を ON にしています");
            Assert.IsTrue(vm.IsEvaluationColoringVisible);
            Assert.IsFalse(vm.IsMassLoadingVisible);
            Assert.AreEqual(tick, vm.IsTickMarkVisible, "プリセットに無い項目まで切り替えています");
            Assert.AreEqual(1, redraws, "3D の描き直しを項目ごとに行っています");
        }

        /// <summary>相互排他の項目 (「…のみ」) は、false を先に当てるのでプリセットのとおりになる。知らない項目名は飛ばす。</summary>
        [TestMethod]
        public void MutuallyExclusiveToggles_EndAsThePresetSays()
        {
            var vm = new MainWindowViewModel();
            vm.IsPileTopResultValueVisibleOnly = true;
            vm.ApplyDisplayToggles(new Dictionary<string, bool>
            {
                [nameof(MainWindowViewModel.IsMidSpanResultValueVisibleOnly)] = true,
                [nameof(MainWindowViewModel.IsPileTopResultValueVisibleOnly)] = false,
                [nameof(MainWindowViewModel.IsPileMaxMinResultValueVisibleOnly)] = false,
                ["IsRemovedInAnOlderVersionVisible"] = true,
            });
            Assert.IsTrue(vm.IsMidSpanResultValueVisibleOnly);
            Assert.IsFalse(vm.IsPileTopResultValueVisibleOnly);
            Assert.IsFalse(vm.IsPileMaxMinResultValueVisibleOnly);
        }

        /// <summary>
        /// 表示プリセットは欄の並びも切り替える (2026-10-17 のレビュー)。「結果の確認」はダッシュボードを開き、「入力の確認」は閉じ、
        /// 「既定」は起動時の並びに戻す。保存したプリセットは保存したときの並びを持つ。これより前に保存したプリセット (並びを持たない) は欄を触らない。
        /// </summary>
        [TestMethod]
        public void Presets_AlsoArrangeThePanels()
        {
            string file = Path.Combine(Path.GetTempPath(), $"pd_presets_{Guid.NewGuid():N}.json");
            try
            {
                var vm = new MainWindowViewModel();
                vm.UseUserSettingsForTests(new UserSettingsService(file));
                var applied = new List<PanelLayout>();
                vm.ApplyPanelLayout = applied.Add;
                vm.CapturePanelLayout = () => new PanelLayout(true, false, true);

                vm.SelectedDisplayPreset = "結果の確認";
                Assert.AreEqual(new PanelLayout(true, null, null), applied.Last(), "「結果の確認」でダッシュボードを開いていません");
                vm.SelectedDisplayPreset = "入力の確認";
                Assert.AreEqual(new PanelLayout(false, null, null), applied.Last(), "「入力の確認」でダッシュボードを閉じていません");
                vm.SelectedDisplayPreset = MainWindowViewModel.DefaultDisplayPresetName;
                Assert.AreEqual(new PanelLayout(false, true, true), applied.Last(), "「既定」で起動時の並びに戻していません");

                Assert.IsTrue(vm.SaveDisplayPreset("並びつき"));
                var saved = new UserSettingsService(file).Settings.DisplayPresets.Single();
                Assert.AreEqual(true, saved.DashboardOpen, "保存したときの並びを持っていません");
                Assert.AreEqual(false, saved.PropertiesVisible);
                Assert.AreEqual(true, saved.MinimapVisible);
                applied.Clear();
                vm.SelectedDisplayPreset = MainWindowViewModel.DefaultDisplayPresetName;
                vm.SelectedDisplayPreset = "並びつき";
                Assert.AreEqual(new PanelLayout(true, false, true), applied.Last(), "保存したプリセットで、保存したときの並びに戻していません");

                // これより前に保存したプリセット (並びを持たない) は欄を触らない
                var settings = new UserSettingsService(file);
                settings.Settings.DisplayPresets.Add(new DisplayPreset { Name = "古い", Toggles = vm.CaptureDisplayToggles() });
                settings.Save();
                vm.UseUserSettingsForTests(new UserSettingsService(file));
                applied.Clear();
                vm.SelectedDisplayPreset = "古い";
                Assert.AreEqual(0, applied.Count, "並びを持たない (古い) プリセットで欄を動かしています");
            }
            finally { File.Delete(file); }

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            StringAssert.Contains(main, "viewModel.CapturePanelLayout = ", "欄の並びを取る道がメイン画面につながっていません");
            StringAssert.Contains(main, "viewModel.ApplyPanelLayout = ApplyPanelLayout;", "欄の並びを当てる道がメイン画面につながっていません");
        }

        /// <summary>
        /// 欄を手で開閉して、選んでいるプリセットの並びと食い違ったら、選択を外す (2026-10-18 のレビュー)。以前は
        /// モデル図の表示項目しか見ておらず、「結果の確認」でダッシュボードを閉じても「結果の確認」のままで、
        /// 選び直しても (値が同じなので) 当て直されず、ダッシュボードを戻せなかった。
        /// プリセットが指定していない欄の開閉では外さない。当てている途中・当てたあとに遅れて来た通知でも外さない。
        /// </summary>
        [TestMethod]
        public void ClosingAPanelByHand_ClearsThePreset_SoItCanBeChosenAgain()
        {
            var expected = new PanelLayout(true, null, null);
            Assert.IsTrue(MainWindowViewModel.PanelLayoutMatches(expected, new PanelLayout(true, false, false)), "指定していない欄まで比べています");
            Assert.IsFalse(MainWindowViewModel.PanelLayoutMatches(expected, new PanelLayout(false, true, true)));
            Assert.IsFalse(MainWindowViewModel.PanelLayoutMatches(new PanelLayout(false, true, true), new PanelLayout(false, false, true)));

            string file = Path.Combine(Path.GetTempPath(), $"pd_presets_{Guid.NewGuid():N}.json");
            try
            {
                var vm = new MainWindowViewModel();
                vm.UseUserSettingsForTests(new UserSettingsService(file));
                // 画面の代わり: 欄の並びを持ち、開閉のたびに (当てている最中も) 知らせる
                var screen = new PanelLayout(false, true, true);
                vm.CapturePanelLayout = () => screen;
                vm.ApplyPanelLayout = layout =>
                {
                    if (layout.Dashboard is bool d) { screen = screen with { Dashboard = d }; vm.OnPanelLayoutChanged(); }
                    if (layout.Properties is bool pr) { screen = screen with { Properties = pr }; vm.OnPanelLayoutChanged(); }
                    if (layout.Minimap is bool m) { screen = screen with { Minimap = m }; vm.OnPanelLayoutChanged(); }
                };

                vm.SelectedDisplayPreset = "結果の確認";
                Assert.AreEqual(true, screen.Dashboard);
                vm.OnPanelLayoutChanged();   // 当てたあとに遅れて届いた通知
                Assert.AreEqual("結果の確認", vm.SelectedDisplayPreset, "プリセットを当てた開閉で、選択を外しています");

                screen = screen with { Properties = false }; vm.OnPanelLayoutChanged();
                Assert.AreEqual("結果の確認", vm.SelectedDisplayPreset, "プリセットが指定していない欄の開閉で、選択を外しています");

                screen = screen with { Dashboard = false }; vm.OnPanelLayoutChanged();   // ダッシュボードを × で閉じた
                Assert.IsNull(vm.SelectedDisplayPreset, "ダッシュボードを閉じても「結果の確認」のままです");
                vm.SelectedDisplayPreset = "結果の確認";
                Assert.AreEqual(true, screen.Dashboard, "選び直しても、ダッシュボードが開き直りません");

                // 「既定」(すべての欄を指定) はプロパティ欄の開閉でも外れる
                vm.SelectedDisplayPreset = MainWindowViewModel.DefaultDisplayPresetName;
                Assert.AreEqual(new PanelLayout(false, true, true), screen);
                Assert.AreEqual(MainWindowViewModel.DefaultDisplayPresetName, vm.SelectedDisplayPreset);
                screen = screen with { Properties = false }; vm.OnPanelLayoutChanged();
                Assert.IsNull(vm.SelectedDisplayPreset, "「既定」の並びと違うのに選んだままです");
            }
            finally { File.Delete(file); }

            // 画面は 3 つの欄の開閉を知らせる
            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");
            string show = TestSource.MethodBody(main, "internal void ShowResultDashboard(");
            Assert.AreEqual(2, show.Split("vm.OnPanelLayoutChanged();").Length - 1, "ダッシュボードの開く・閉じるの両方を知らせていません");
            string sync = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasCore.cs"), "internal void InitializePanelToggleSync(");
            Assert.AreEqual(2, sync.Split("?.OnPanelLayoutChanged();").Length - 1, "プロパティ欄・ミニマップ欄の開閉を知らせていません");
        }

        /// <summary>名前を聞くダイアログの XAML (StaticResource のキー誤りはビルドを通る)。</summary>
        [TestMethod]
        public void TextPromptWindow_XamlParses()
        {
            string? prompt = null, answer = null;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.TextPromptWindow("表示プリセットを保存", "名前", "初期値");
                try
                {
                    prompt = window.PromptText.Text;
                    answer = window.AnswerBox.Text;
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが時間内に終わりませんでした"); return; }
            if (captured != null) throw new AssertFailedException(captured.ToString());
            Assert.AreEqual("名前", prompt);
            Assert.AreEqual("初期値", answer);
        }
    }
}
