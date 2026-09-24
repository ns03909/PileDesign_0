using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 正常に終了したら、そのセッションの自動保存を次の起動で勧めないこと。
    ///
    /// 自動保存は変更が無くても 3 分ごとに書く。以前は終了してもそのまま残したので、
    /// 上書き保存して普通に終了しても、最後の自動保存が元のファイルより新しいために
    /// 次の起動で復元を勧めた。「保存しない」で終了した作業も勧めていた。
    /// </summary>
    [TestClass]
    public class AutoSaveSessionEndTests
    {
        private readonly List<string> _created = [];

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var path in _created)
            {
                foreach (var p in new[] { path, Dismissed(path) })
                    try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { }
            }
        }

        private static string Dismissed(string path)
            => System.Text.RegularExpressions.Regex.Replace(path, "_(autosave|emergency)_", "_$1_dismissed_");

        private static AutoSaveService NewService(string projectName)
        {
            var auto = new AutoSaveService(new FileOperationService(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
            }));
            var input = new InputModel();
            auto.LiveStateProvider = () => (input, Path.Combine(Path.GetTempPath(), projectName + ".pdj"), null, null);
            return auto;
        }

        /// <summary>Tick と同じ順番 (画面のスレッドで確定 → 書き出し) で自動保存を 1 回走らせ、書いたファイルを返す。</summary>
        private string? RunAutoSave(AutoSaveService auto)
        {
            string? written = null;
            void OnCompleted(object? s, AutoSaveEventArgs e) => written = e.FilePath;
            auto.AutoSaveCompleted += OnCompleted;
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var prepared = typeof(AutoSaveService).GetMethod("PrepareState", flags)!.Invoke(auto, null);
                typeof(AutoSaveService).GetMethod("PerformAutoSave", flags)!.Invoke(auto, [prepared]);
            }
            finally
            {
                auto.AutoSaveCompleted -= OnCompleted;
            }
            if (written != null) _created.Add(written);
            return written;
        }

        [TestMethod]
        public void EndingNormally_DismissesOnlyThisSessionsAutoSaves()
        {
            string tag = Guid.NewGuid().ToString("N")[..8];
            var mine = NewService($"Mine_{tag}");
            var other = NewService($"Other_{tag}");   // 同時に開いているもう一つのアプリ

            string first = RunAutoSave(mine)!;
            string second = RunAutoSave(mine)!;
            string others = RunAutoSave(other)!;
            Assert.IsNotNull(first); Assert.IsNotNull(second); Assert.IsNotNull(others);

            mine.EndSessionNormally();

            foreach (var path in new[] { first, second })
            {
                Assert.IsFalse(File.Exists(path), $"{Path.GetFileName(path)} が確認済みになっていません (次の起動で復元を勧める)");
                Assert.IsTrue(File.Exists(Dismissed(path)), $"{Path.GetFileName(path)} が消えています (手で復元できるように残すこと)");
            }
            Assert.IsTrue(File.Exists(others),
                "まだ動いているもう一つのアプリの自動保存まで確認済みにしています (そちらが落ちたら取り戻せない)");
            other.Stop();
        }

        /// <summary>
        /// 自動保存・緊急保存が、書いたアプリの起動ごとの印を中に記録すること。
        /// 起動時の確認で、名前の無いプロジェクトの古い自動保存を同じ作業として結ぶのに使う
        /// (記録が無いと、名前の無いプロジェクトは名前 "Untitled" でしか結べず、別の起動の作業まで見送る)。
        /// </summary>
        [TestMethod]
        public void AutoSavesRecordTheRunThatWroteThem()
        {
            var auto = NewService($"Run_{Guid.NewGuid():N}");
            string autoSave = RunAutoSave(auto)!;
            string? emergency = auto.TryEmergencyAutoSave();
            if (emergency != null) _created.Add(emergency);

            Assert.AreEqual(auto.SessionId, AutoSaveService.ReadRecordedOrigin(autoSave).SessionId,
                "自動保存に起動ごとの印が記録されていません");
            Assert.IsNotNull(emergency, "緊急保存が書けていません");
            Assert.AreEqual(auto.SessionId, AutoSaveService.ReadRecordedOrigin(emergency).SessionId,
                "緊急保存に起動ごとの印が記録されていません");
            auto.Stop();
        }

        [TestMethod]
        public void AfterEndingNormally_NoMoreAutoSavesAreWritten()
        {
            var auto = NewService($"Late_{Guid.NewGuid():N}");
            auto.EndSessionNormally();

            Assert.IsNull(RunAutoSave(auto),
                "終了の処理のあとに自動保存を書いています (確認済みにした後なので、次の起動で勧める)");
        }

        /// <summary>
        /// 緊急保存を試みたセッションでは、定期の自動保存を残すこと。
        /// 致命的なエラーの終了処理でも画面は閉じるので、正常終了の後始末へ来ることがある。
        /// 緊急保存に失敗していたら、残っている自動保存が作業を取り戻す唯一の手段になる。
        /// </summary>
        [TestMethod]
        public void AfterAnEmergencySave_TheAutoSavesAreKept()
        {
            var auto = NewService($"Crash_{Guid.NewGuid():N}");
            string autoSave = RunAutoSave(auto)!;

            string? emergency = auto.TryEmergencyAutoSave();
            if (emergency != null) _created.Add(emergency);

            auto.EndSessionNormally();

            Assert.IsTrue(File.Exists(autoSave), "緊急保存を試みたのに、自動保存を確認済みにしています");
            if (emergency != null)
                Assert.IsTrue(File.Exists(emergency), "緊急保存を確認済みにしています");
        }

        /// <summary>
        /// メイン画面が閉じたら後始末を呼ぶこと、閉じるときの「はい」は保存できたときだけ閉じること。
        /// </summary>
        [TestMethod]
        public void TheMainWindow_EndsTheSessionOnCloseAndOnlyClosesAfterASuccessfulSave()
        {
            string src = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml.cs");

            StringAssert.Contains(TestSource.MethodBody(src, "private void Window_Closed(object sender, EventArgs e)"),
                "vm.EndAutoSaveSessionNormally();", "メイン画面を閉じても、このセッションの自動保存を確認済みにしていません");

            string closing = TestSource.MethodBody(src, "private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)");
            StringAssert.Contains(closing, "if (!await viewModel.SaveInputModelFileCoreAsync())",
                "閉じるときの「はい」が、保存できたかどうかを見ずに閉じています (保存をキャンセルしても終了する)");
        }
    }
}
