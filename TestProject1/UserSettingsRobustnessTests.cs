using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 利用者の設定 (user_settings.json) の読み書き (2026-10-18 のレビュー)。
    /// <list type="bullet">
    /// <item>JSON として正しくても中身が使えない設定を、読込で直す。以前は <c>"DisplayPresets": null</c> を受け取り、
    /// 画面の準備 (MainWindowViewModel のコンストラクター) が一覧を列挙して例外になった。ファイルは残るので、起動するたびに落ちた。</item>
    /// <item>保存に失敗したら呼び出し元へ返し、画面は「次回の起動には残らない」と知らせる。以前はログに残すだけで、
    /// 保存したプリセットが次の起動で消えたり、削除したものが戻ったりした。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class UserSettingsRobustnessTests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        private static UserSettingsService LoadFrom(string json, out string file)
        {
            file = Path.Combine(Path.GetTempPath(), $"pd_settings_{Guid.NewGuid():N}.json");
            File.WriteAllText(file, json);
            return new UserSettingsService(file);
        }

        [TestMethod]
        public void ANullPresetList_IsReadAsEmpty_AndTheScreenStarts()
        {
            var service = LoadFrom("""{ "IsSaveAnalysisResultsAutoSave": true, "DisplayPresets": null }""", out string file);
            try
            {
                Assert.IsNotNull(service.Settings.DisplayPresets, "一覧が null のままです (画面の準備で例外になる)");
                Assert.AreEqual(0, service.Settings.DisplayPresets.Count);
                Assert.IsTrue(service.Settings.IsSaveAnalysisResultsAutoSave, "直すついでに、ほかの設定を既定へ戻しています");

                var vm = new MainWindowViewModel();
                vm.UseUserSettingsForTests(service);   // 一覧を作り直す (起動時と同じ処理)
                CollectionAssert.Contains(vm.DisplayPresetNames, MainWindowViewModel.DefaultDisplayPresetName);
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void BrokenEntries_AreDroppedOrFixed_AndTheGoodOnesStay()
        {
            var service = LoadFrom("""
                { "DisplayPresets": [
                    null,
                    { "Name": null, "Toggles": {} },
                    { "Name": "   ", "Toggles": {} },
                    { "Name": "A", "Toggles": null },
                    { "Name": "B", "Toggles": { "IsLabelVisible": false } },
                    { "Name": "B", "Toggles": { "IsLabelVisible": true } },
                    { "Name": "既定", "Toggles": {} }
                ] }
                """, out string file);
            try
            {
                var presets = service.Settings.DisplayPresets;
                CollectionAssert.AreEqual(new[] { "A", "B", "既定" }, presets.Select(p => p.Name).ToArray(),
                    "null・名前の無い項目・同じ名前の 2 つ目を除けていません");
                Assert.IsNotNull(presets[0].Toggles, "表示の項目が null のままです (選ぶと例外になる)");
                Assert.IsFalse(presets[1].Toggles["IsLabelVisible"], "同じ名前は最初の 1 つを残します");

                var vm = new MainWindowViewModel();
                vm.UseUserSettingsForTests(service);
                Assert.AreEqual(1, vm.DisplayPresetNames.Count(n => n == MainWindowViewModel.DefaultDisplayPresetName),
                    "用意したプリセットと同じ名前を、一覧に 2 つ並べています");
                CollectionAssert.Contains(vm.DisplayPresetNames, "A");
                vm.SelectedDisplayPreset = "A";   // 以前は Toggles: null で例外
                Assert.AreEqual("A", vm.SelectedDisplayPreset);
            }
            finally { File.Delete(file); }

            var clean = new UserSettings();
            Assert.AreEqual(0, UserSettingsService.Normalize(clean).Count, "直すことが無いのに直したと言っています");
        }

        /// <summary>書き込めない保存先 (無いフォルダ) で、保存は false を返し、画面は知らせる。画面の状態は変えたまま。</summary>
        [TestMethod]
        public void AFailedSave_IsReported_AndTheChangeStaysForThisSession()
        {
            string file = Path.Combine(Path.GetTempPath(), $"pd_missing_{Guid.NewGuid():N}", "user_settings.json");
            var service = new UserSettingsService(file);
            Assert.IsFalse(service.Save(), "書き込めないのに保存できたと返しています");

            var vm = new MainWindowViewModel();
            vm.UseUserSettingsForTests(service);
            Assert.IsTrue(vm.SaveDisplayPreset("保存できない"));
            Assert.AreEqual(1, vm.UserSettingsSaveFailures, "プリセットを書き込めなかったことを知らせていません");
            CollectionAssert.Contains(vm.DisplayPresetNames, "保存できない", "このセッションでは使えるはずです");

            Assert.IsTrue(vm.DeleteDisplayPreset("保存できない"));
            Assert.AreEqual(2, vm.UserSettingsSaveFailures, "削除を書き込めなかったことを知らせていません");

            vm.IsSaveAnalysisResultsAutoSave = !vm.IsSaveAnalysisResultsAutoSave;
            vm.IsSaveAnalysisResultsManual = !vm.IsSaveAnalysisResultsManual;
            Assert.AreEqual(4, vm.UserSettingsSaveFailures, "保存オプションを書き込めなかったことを知らせていません");

            string saved = UserSettingsService.DescribeSaveFailed("表示プリセット「X」を保存しました");
            StringAssert.Contains(saved, "次回の起動には残りません");
            StringAssert.Contains(saved, "このまま使えます");
            StringAssert.Contains(UserSettingsService.DescribeSaveFailed("表示プリセット「X」を削除しました", "次回の起動では元に戻ります"),
                "次回の起動では元に戻ります");
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(file)), "テストが保存先のフォルダを作っています");
        }
    }
}
