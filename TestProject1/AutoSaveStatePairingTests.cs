using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// 自動保存の「入力」と「元ファイルのパス」が同じプロジェクトを指すこと、
    /// および写しの段階の失敗も書き出しの失敗と同じく数えて知らせること。
    ///
    /// 以前はパスだけを Start 時の控えから、しかもバックグラウンドの書き出しの時点で読んでいた。
    /// 写し (画面のスレッド) と書き出しの間にプロジェクトを切り替えると、前のプロジェクトの入力が
    /// 新しいプロジェクトの名前で、中に新しい元パスを記録して保存される。復元したあとの上書き保存が
    /// 別のファイルに向かう。例題の読み込みは Start を最後に呼ぶので、読み込みの途中に Tick が来ると
    /// 例題の内容が直前のプロジェクトの名前で自動保存され得た。
    /// </summary>
    [TestClass]
    public class AutoSaveStatePairingTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        private static object? InvokePrivate(object obj, string name, params object?[] args)
            => (obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"メソッド {name} が見つかりません")).Invoke(obj, args);

        [TestMethod]
        public void SourcePathIsTakenTogetherWithTheInput()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            string tag = Guid.NewGuid().ToString("N")[..8];
            string pathA = Path.Combine(Path.GetTempPath(), $"PairingA_{tag}.pdj");
            string pathB = Path.Combine(Path.GetTempPath(), $"PairingB_{tag}.pdj");
            var inputA = new InputModel();
            var inputB = new InputModel();
            string? written = null;

            try
            {
                // 画面のスレッドで写しを取る (Tick の前半)
                auto.LiveStateProvider = () => (inputA, pathA, null, null);
                var prepared = InvokePrivate(auto, "PrepareState");
                Assert.IsNotNull(prepared, "保存する状態が取れていません");

                // 書き出しの前にプロジェクトを切り替える (別ファイルを開いた / Start が呼ばれた)。
                // Start そのものはタイマーをテストのスレッドに立てるので呼ばず、Start が書き換える控えを直接変える
                auto.LiveStateProvider = () => (inputB, pathB, null, null);
                typeof(AutoSaveService).GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(auto, pathB);

                // バックグラウンドの書き出し (Tick の後半)
                auto.AutoSaveCompleted += (_, e) => written = e.FilePath;
                InvokePrivate(auto, "PerformAutoSave", prepared);

                Assert.IsNotNull(written, "自動保存が書かれていません");
                StringAssert.StartsWith(Path.GetFileName(written), $"PairingA_{tag}_autosave_",
                    "自動保存ファイルの名前が、写した入力と別のプロジェクトになっています");
                var loaded = new FileOperationService(MakeOptions()).LoadProjectData(written);
                Assert.AreEqual(pathA, loaded.SourceFilePath,
                    "自動保存に記録した元ファイルが、写した入力と別のプロジェクトを指しています。"
                    + "復元したあとの上書き保存が別のファイルに向かいます");
            }
            finally
            {
                auto.Stop();
                if (written != null)
                    try { File.Delete(written); } catch { /* 片付けの失敗は無視 */ }
            }
        }

        [TestMethod]
        public void FailuresAreCountedAndReported()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            AutoSaveEventArgs? last = null;
            auto.AutoSaveCompleted += (_, e) => last = e;

            InvokePrivate(auto, "ReportFailure", new InvalidOperationException("写せません"), "snapshot");
            InvokePrivate(auto, "ReportFailure", new IOException("書けません"), "write");

            Assert.AreEqual(2, auto.ConsecutiveFailures, "写しと書き出しの失敗が同じ回数に数えられていません");
            Assert.IsNotNull(last);
            Assert.IsFalse(last.Success);
            Assert.AreEqual(2, last.ConsecutiveFailures, "通知に連続失敗の回数が載っていません");
        }

        /// <summary>
        /// 完了の知らせは、書き出しのスレッド (バックグラウンド) ではなく、サービスを作った画面のスレッドで届くこと。
        /// 以前はバックグラウンドから直接発火していたので、受け手が出すトースト (画面の部品) が例外になり、
        /// 3 回続けて失敗したときの通知が出ていなかった。
        /// </summary>
        [TestMethod]
        public void CompletionIsDeliveredOnTheUiThread()
        {
            AutoSaveService? auto = null;
            int uiThread = -1;
            var ex = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                auto = new AutoSaveService(new FileOperationService(MakeOptions()));
                uiThread = Environment.CurrentManagedThreadId;
            }, out bool timedOut);
            Assert.IsFalse(timedOut);
            Assert.IsNull(ex, ex?.ToString());

            int deliveredOn = -1;
            using var delivered = new System.Threading.ManualResetEventSlim();
            auto!.AutoSaveCompleted += (_, _) =>
            {
                deliveredOn = Environment.CurrentManagedThreadId;
                delivered.Set();
            };

            // 書き出しと同じく、バックグラウンドのスレッドから失敗を報告する
            System.Threading.Tasks.Task.Run(() =>
                InvokePrivate(auto, "ReportFailure", new IOException("書けません"), "write")).Wait();

            Assert.IsTrue(delivered.Wait(TimeSpan.FromSeconds(10)), "完了の知らせが届きません");
            Assert.AreEqual(uiThread, deliveredOn,
                "完了の知らせが画面のスレッド以外で届いています (受け手が画面を操作すると例外になります)");
        }

        /// <summary>
        /// 同じ秒に続けて保存しても、先の保存が上書きで消えないこと。
        /// 名前は秒までの時刻で決まるので、以前は同じ秒の 2 回目が 1 回目のファイルに上書きしていた。
        /// </summary>
        [TestMethod]
        public void SavesInTheSameSecondDoNotOverwriteEachOther()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            string tag = Guid.NewGuid().ToString("N")[..8];
            string source = Path.Combine(Path.GetTempPath(), $"SameSecond_{tag}.pdj");
            auto.LiveStateProvider = () => (new InputModel(), source, null, null);

            var written = new System.Collections.Generic.List<string>();
            auto.AutoSaveCompleted += (_, e) => { if (e.FilePath != null) written.Add(e.FilePath); };
            try
            {
                for (int i = 0; i < 3; i++)
                    InvokePrivate(auto, "PerformAutoSave", InvokePrivate(auto, "PrepareState"));
                string? emergency1 = auto.TryEmergencyAutoSave();
                string? emergency2 = auto.TryEmergencyAutoSave();
                if (emergency1 != null) written.Add(emergency1);
                if (emergency2 != null) written.Add(emergency2);

                Assert.AreEqual(5, written.Count, "保存が書かれていません");
                Assert.AreEqual(5, written.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    "同じ名前に保存しています (同じ秒の保存が先の保存を上書きします): " + string.Join(", ", written.Select(Path.GetFileName)));
                Assert.IsTrue(written.All(File.Exists), "保存したファイルが残っていません");
                Assert.IsTrue(written.All(p => Path.GetFileName(p).StartsWith($"SameSecond_{tag}_", StringComparison.Ordinal)),
                    "名前の型 (元ファイル名_種別_時刻) が崩れています");
                CollectionAssert.IsSubsetOf(written.Take(3).ToList(), auto.GetAutoSaveFilesForProject(source).ToList(),
                    "番号を付けた自動保存が、元ファイルの自動保存として一覧に出ません");
            }
            finally
            {
                auto.Stop();
                foreach (var p in written)
                    try { File.Delete(p); } catch { /* 片付けの失敗は無視 */ }
            }
        }

        /// <summary>受け手が例外を出しても、それを保存の失敗として数えないこと。</summary>
        [TestMethod]
        public void HandlerFailureIsNotCountedAsASaveFailure()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            auto.AutoSaveCompleted += (_, _) => throw new InvalidOperationException("受け手の不具合");

            InvokePrivate(auto, "ReportFailure", new IOException("書けません"), "write");

            Assert.AreEqual(1, auto.ConsecutiveFailures, "受け手の例外が、保存の失敗として余分に数えられています");
        }

        /// <summary>
        /// 写しの段階の失敗が、ログだけで終わらず報告処理を通ること。
        /// 写しは内部で例外を握るので、実物で失敗を起こすのは難しい。形で見張る。
        /// </summary>
        [TestMethod]
        public void SnapshotFailureGoesThroughTheSameReport()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "AutoSaveService.cs");
            int prepare = src.IndexOf("var p = PrepareState();", StringComparison.Ordinal);
            Assert.IsTrue(prepare >= 0, "自動保存が写しを取る処理が見つかりません (PrepareState)");
            int catchAt = src.IndexOf("catch (Exception ex)", prepare, StringComparison.Ordinal);
            int nextTry = src.IndexOf("try", catchAt + 1, StringComparison.Ordinal);
            string handler = src[catchAt..nextTry];
            StringAssert.Contains(handler, "ReportFailure(",
                "写しの段階の失敗がログだけで終わっています。連続失敗の回数も画面の表示も動きません");
        }
    }
}
