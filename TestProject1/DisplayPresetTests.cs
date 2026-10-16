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
