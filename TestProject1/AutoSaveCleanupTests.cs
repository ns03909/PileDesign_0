using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 自動保存の後始末と、保存の中身を確定させる時点。
    /// </summary>
    [TestClass]
    public class AutoSaveCleanupTests
    {
        private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0);
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"AutoSaveCleanup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private string Write(string name, DateTime created, DateTime written)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, "{}");
            File.SetCreationTime(path, created);
            File.SetLastWriteTime(path, written);
            return path;
        }

        /// <summary>
        /// 保持期間は<b>最後に書いた日時</b>で判定すること。復元候補の選択 (最終更新日時) と揃える。
        /// 以前は作成日時で判定していたので、作られたのは古いがあとで書き直されたファイル (復元候補になる新しい内容) が消えた。
        /// </summary>
        [TestMethod]
        public void RetentionIsJudgedByTheLastWriteTime()
        {
            string rewritten = Write("Foo_autosave_20260910_100000.pdj", created: Now.AddDays(-20), written: Now.AddHours(-1));
            string stale = Write("Bar_autosave_20260910_100000.pdj", created: Now.AddDays(-20), written: Now.AddDays(-10));
            string staleEmergency = Write("Baz_emergency_20260910_100000.pdj", created: Now.AddHours(-1), written: Now.AddDays(-8));
            string unrelated = Write("Notes.pdj", created: Now.AddDays(-30), written: Now.AddDays(-30));

            int deleted = AutoSaveService.CleanupOldAutoSaveFiles(_dir, Now, retentionDays: 7);

            Assert.IsTrue(File.Exists(rewritten), "あとで書き直された (新しい内容の) 自動保存を、作成日時が古いという理由で消しています");
            Assert.IsFalse(File.Exists(stale), "最後に書かれてから保持期間を過ぎた自動保存が残っています");
            Assert.IsFalse(File.Exists(staleEmergency), "最後に書かれてから保持期間を過ぎた緊急保存が残っています (作成日時が新しくても)");
            Assert.IsTrue(File.Exists(unrelated), "自動保存でないファイルを消しています");
            Assert.AreEqual(2, deleted);
        }

        /// <summary>コメント行を除く (説明の中の「await」に当たらないように)。</summary>
        private static string CodeOnly(string source)
            => string.Join("\n", source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        /// <summary>
        /// 手動保存は、保存する中身を<b>最初の await より前に</b> (画面のスレッドで) 確定させること。
        ///
        /// 保存用の写しを作れなかったときは生きたモデルをそのまま直列化する (<c>SnapshotForSaving</c>)。
        /// 写しと直列化が同じスレッドで続けて行われる限り、そのあいだに編集は割り込めず、書かれる中身は写した場合と同じ
        /// (だから利用者へは知らせない)。確定をバックグラウンドへ移すと、この前提が崩れる。
        /// </summary>
        [TestMethod]
        public void TheManualSaveFixesTheContentBeforeItsFirstAwait()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "FileOperationService.cs");
            string body = CodeOnly(TestSource.MethodBody(src, "public async Task<string?> SaveProjectDataAsync("));
            int prepare = body.IndexOf("PrepareSave(", StringComparison.Ordinal);
            int firstAwait = body.IndexOf("await ", StringComparison.Ordinal);
            Assert.IsTrue(prepare >= 0 && prepare < firstAwait,
                "手動保存が、中身の確定 (PrepareSave) より前に await しています。写せなかったときに編集中の変更が混ざります");

            string prepareBody = CodeOnly(TestSource.MethodBody(src, "internal PreparedSave PrepareSave("));
            Assert.IsFalse(prepareBody.Contains("await ", StringComparison.Ordinal) || prepareBody.Contains("Task.Run", StringComparison.Ordinal),
                "中身の確定 (写し〜直列化) の途中でスレッドを離れています");
        }
    }
}
