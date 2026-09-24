using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 起動時に復元を勧める自動保存ファイルの選び方と、見送りの印の付け方。
    ///
    /// - 以前は自動保存ファイルの時刻と「24 時間以内」だけを見ていたので、そのあと元ファイルを
    ///   普通に保存していても古い自動保存の復元を勧めた。復元すると上書き保存が元ファイルへ向かい、
    ///   保存した新しい内容を古い内容で上書きする。
    /// - 見送り (はい・いいえ) の印を提示した 1 件にだけ付けていたので、自動保存が何世代もあると
    ///   起動のたびに 1 つずつ古いものを勧め続けた。
    /// </summary>
    [TestClass]
    public class AutoSaveRestoreCandidateTests
    {
        private string _dir = "";
        private string _autoSave = "";

        private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Local);

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PileDesignRestoreTests", Guid.NewGuid().ToString("N"));
            _autoSave = Path.Combine(_dir, "AutoSave");
            Directory.CreateDirectory(_autoSave);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private static FileOperationService Service() => new(new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        });

        /// <summary>自動保存ファイルを 1 つ作り、書いた時刻を <paramref name="savedAt"/> にする。</summary>
        private string WriteAutoSave(string name, DateTime savedAt, string? source, string? session = null)
        {
            string path = Path.Combine(_autoSave, name);
            var service = Service();
            service.WritePrepared(path,
                service.PrepareSave(new InputModel(), null, sourceFilePath: source, autoSaveSessionId: session));
            File.SetLastWriteTime(path, savedAt);
            return path;
        }

        private static string Dismissed(string path)
            => System.Text.RegularExpressions.Regex.Replace(path, "_(autosave|emergency)_", "_$1_dismissed_");

        private static void AssertDismissed(string path)
        {
            Assert.IsFalse(File.Exists(path), $"{Path.GetFileName(path)} に見送りの印が付いていません");
            Assert.IsTrue(File.Exists(Dismissed(path)), $"{Path.GetFileName(path)} が消えています (手で復元できるように残すこと)");
        }

        /// <summary>元ファイルを作り、最後に保存した時刻を <paramref name="savedAt"/> にする。</summary>
        private string WriteSource(string name, DateTime savedAt)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, "{}");
            File.SetLastWriteTime(path, savedAt);
            return path;
        }

        [TestMethod]
        public void AnAutoSaveOlderThanItsSourceFile_IsNotOffered()
        {
            string source = WriteSource("Foo.pdj", Now.AddMinutes(-10));             // 自動保存のあとに普通に保存した
            WriteAutoSave("Foo_autosave_20260924_113000.pdj", Now.AddMinutes(-30), source);

            Assert.IsNull(AutoSaveService.FindRestoreCandidate(_autoSave, Now),
                "元ファイルのほうが新しいのに復元を勧めています。復元すると上書き保存が新しい内容を古い内容で上書きします");
        }

        [TestMethod]
        public void AnAutoSaveNewerThanItsSourceFile_IsOffered()
        {
            string source = WriteSource("Foo.pdj", Now.AddMinutes(-60));             // 保存したあとも作業して落ちた
            string autoSave = WriteAutoSave("Foo_autosave_20260924_113000.pdj", Now.AddMinutes(-30), source);

            var candidate = AutoSaveService.FindRestoreCandidate(_autoSave, Now);
            Assert.IsNotNull(candidate, "元ファイルより新しい自動保存を勧めていません");
            Assert.AreEqual(autoSave, candidate.FilePath);
            Assert.AreEqual(source, candidate.SourceFilePath, "記録した元ファイルが読めていません");
        }

        /// <summary>
        /// 新しいほうが元ファイルに追い越されていても、別のプロジェクトのまだ新しい自動保存は勧めること
        /// (元ファイルとの比べ方は候補ごと)。
        /// </summary>
        [TestMethod]
        public void ASupersededCandidate_IsSkippedInFavourOfTheNextOne()
        {
            string foo = WriteSource("Foo.pdj", Now.AddMinutes(-5));
            string bar = WriteSource("Bar.pdj", Now.AddHours(-3));
            WriteAutoSave("Foo_autosave_20260924_114500.pdj", Now.AddMinutes(-15), foo);
            string barAuto = WriteAutoSave("Bar_autosave_20260924_113000.pdj", Now.AddMinutes(-30), bar);

            Assert.AreEqual(barAuto, AutoSaveService.FindRestoreCandidate(_autoSave, Now)?.FilePath);
        }

        /// <summary>元ファイルが分からない・無いものは比べられないので、従来どおり勧めること。</summary>
        [TestMethod]
        public void ACandidateWithoutAKnownSourceFile_IsStillOffered()
        {
            string untitled = WriteAutoSave("Untitled_autosave_20260924_113000.pdj", Now.AddMinutes(-30), null);
            Assert.AreEqual(untitled, AutoSaveService.FindRestoreCandidate(_autoSave, Now)?.FilePath);

            File.Delete(untitled);
            string moved = WriteAutoSave("Gone_autosave_20260924_113000.pdj", Now.AddMinutes(-30),
                Path.Combine(_dir, "移動した.pdj"));
            Assert.AreEqual(moved, AutoSaveService.FindRestoreCandidate(_autoSave, Now)?.FilePath);
        }

        [TestMethod]
        public void OnlyAutoSavesWithinTheWindow_AreOffered()
        {
            WriteAutoSave("Foo_autosave_20260923_100000.pdj", Now - AutoSaveService.RestoreWindow - TimeSpan.FromMinutes(1), null);
            Assert.IsNull(AutoSaveService.FindRestoreCandidate(_autoSave, Now), "24 時間を過ぎた自動保存を勧めています");
        }

        /// <summary>
        /// 答えたら、その候補と<b>同じ作業の</b>それより古い候補がまとめて見送り済みになり、次の起動では
        /// その作業を勧めないこと。答えたあとに書かれたもの (いま動いている分) は残すこと。
        /// </summary>
        [TestMethod]
        public void AnsweringDismissesThatCandidateAndOlderOnesOfTheSameWork()
        {
            string foo = Path.Combine(_dir, "Foo.pdj");   // 元ファイルは無い (比べられないので勧める側)
            string oldest = WriteAutoSave("Foo_autosave_20260924_110000.pdj", Now.AddMinutes(-60), foo, "run1");
            string older = WriteAutoSave("Foo_emergency_20260924_113000.pdj", Now.AddMinutes(-30), foo, "run2");
            string answered = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), foo, "run3");

            var candidate = AutoSaveService.FindRestoreCandidate(_autoSave, Now);
            Assert.AreEqual(answered, candidate?.FilePath, "(前提) 最新のものを勧めていません");

            string later = WriteAutoSave("Foo_autosave_20260924_115500.pdj", Now.AddMinutes(-5), foo, "run4");   // 答えたあとに書かれた
            AutoSaveService.DismissRestoreCandidatesUpTo(_autoSave, candidate!);

            foreach (var path in new[] { oldest, older, answered })
                AssertDismissed(path);
            Assert.IsTrue(File.Exists(later), "答えたあとに書かれた自動保存まで見送りにしています");

            File.Delete(later);
            Assert.IsNull(AutoSaveService.FindRestoreCandidate(_autoSave, Now),
                "見送ったのに、次の起動で 1 つ古い自動保存を勧めています");
        }

        /// <summary>
        /// プロジェクト A への答えで、プロジェクト B のまだ復元していない自動保存を見送らないこと。
        /// フォルダ全体から古いものをまとめて見送ると、B の作業が二度と案内されなくなる。
        /// </summary>
        [TestMethod]
        public void AnsweringForOneProject_KeepsAnotherProjectsCandidates()
        {
            string bar = WriteAutoSave("Bar_autosave_20260924_110000.pdj", Now.AddMinutes(-60), Path.Combine(_dir, "Bar.pdj"), "runB");
            string foo = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), Path.Combine(_dir, "Foo.pdj"), "runA");

            var candidate = AutoSaveService.FindRestoreCandidate(_autoSave, Now);
            Assert.AreEqual(foo, candidate?.FilePath, "(前提) 新しい Foo を勧めていません");
            AutoSaveService.DismissRestoreCandidatesUpTo(_autoSave, candidate!);

            AssertDismissed(foo);
            Assert.IsTrue(File.Exists(bar), "Foo への答えで、別のプロジェクト Bar の自動保存まで見送っています");
            Assert.AreEqual(bar, AutoSaveService.FindRestoreCandidate(_autoSave, Now)?.FilePath,
                "Foo に答えたあと、Bar の自動保存を案内していません");
        }

        /// <summary>
        /// 名前の無いプロジェクトは元ファイルで結べないので、書いたアプリの起動ごとの印で結ぶこと。
        /// 同じ起動の古いものは見送り、別の起動の名前の無いプロジェクトは残す。
        /// </summary>
        [TestMethod]
        public void UntitledWork_IsGroupedByTheRunThatWroteIt()
        {
            string otherRun = WriteAutoSave("Untitled_autosave_20260924_110000.pdj", Now.AddMinutes(-60), null, "runX");
            string sameRunOlder = WriteAutoSave("Untitled_autosave_20260924_113000.pdj", Now.AddMinutes(-30), null, "runY");
            string answered = WriteAutoSave("Untitled_emergency_20260924_114000.pdj", Now.AddMinutes(-20), null, "runY");

            var candidate = AutoSaveService.FindRestoreCandidate(_autoSave, Now);
            Assert.AreEqual(answered, candidate?.FilePath);
            Assert.AreEqual("runY", candidate!.SessionId, "書いたアプリの起動ごとの印が読めていません");
            AutoSaveService.DismissRestoreCandidatesUpTo(_autoSave, candidate);

            AssertDismissed(answered);
            AssertDismissed(sameRunOlder);
            Assert.IsTrue(File.Exists(otherRun), "別の起動の名前の無いプロジェクトまで見送っています");
        }

        /// <summary>
        /// 元ファイルが違っても、同じアプリの起動が書いたものは同じ作業とみなすこと
        /// (起動の中でプロジェクトを取り替えるときは「保存しますか？」で保存か破棄を決めている)。
        /// </summary>
        [TestMethod]
        public void OlderWorkFromTheSameRun_IsDismissedToo()
        {
            string before = WriteAutoSave("Untitled_autosave_20260924_110000.pdj", Now.AddMinutes(-60), null, "run1");
            string answered = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), Path.Combine(_dir, "Foo.pdj"), "run1");

            AutoSaveService.DismissRestoreCandidatesUpTo(_autoSave, AutoSaveService.FindRestoreCandidate(_autoSave, Now)!);

            AssertDismissed(answered);
            AssertDismissed(before);
        }

        /// <summary>記録を持たない旧い自動保存ファイルは、名前の頭 (元ファイル名) で結ぶこと。</summary>
        [TestMethod]
        public void LegacyFilesWithoutRecords_AreGroupedByName()
        {
            string fooOld = WriteAutoSave("Foo_autosave_20260924_110000.pdj", Now.AddMinutes(-60), null);
            string barOld = WriteAutoSave("Bar_autosave_20260924_111000.pdj", Now.AddMinutes(-50), null);
            string foo = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), null);

            AutoSaveService.DismissRestoreCandidatesUpTo(_autoSave, AutoSaveService.FindRestoreCandidate(_autoSave, Now)!);

            AssertDismissed(foo);
            AssertDismissed(fooOld);
            Assert.IsTrue(File.Exists(barOld), "名前の違う旧い自動保存まで見送っています");
        }

        /// <summary>
        /// 復元に失敗した候補は、その 1 件だけを見送り、同じ作業の古い候補を残すこと。
        /// まとめて見送ると、最新が壊れているだけで読める古い自動保存まで二度と案内されない。
        /// </summary>
        [TestMethod]
        public void AFailedRestore_DismissesOnlyThatFile()
        {
            string foo = Path.Combine(_dir, "Foo.pdj");
            string good = WriteAutoSave("Foo_autosave_20260924_113000.pdj", Now.AddMinutes(-30), foo, "run1");
            string broken = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), foo, "run1");

            var candidate = AutoSaveService.FindRestoreCandidate(_autoSave, Now);
            Assert.AreEqual(broken, candidate?.FilePath);

            var service = new AutoSaveService(Service());
            service.DismissRestoreCandidate(candidate!);

            AssertDismissed(broken);
            Assert.AreEqual(good, AutoSaveService.FindRestoreCandidate(_autoSave, Now)?.FilePath,
                "復元に失敗したあと、同じ作業のひとつ前の自動保存を案内していません");
        }

        /// <summary>
        /// プロジェクト別の自動保存の一覧が、ファイル名ではなく記録した元ファイルのフルパスで照合すること。
        /// 別のフォルダにある同じ名前のプロジェクトの自動保存が混ざらないこと。
        /// </summary>
        [TestMethod]
        public void TheProjectList_MatchesTheRecordedFullPath()
        {
            string planA = Path.Combine(_dir, "案A", "基礎.pdj");
            string planB = Path.Combine(_dir, "案B", "基礎.pdj");
            string a1 = WriteAutoSave("基礎_autosave_20260924_110000.pdj", Now.AddMinutes(-60), planA, "run1");
            string b1 = WriteAutoSave("基礎_autosave_20260924_111000.pdj", Now.AddMinutes(-50), planB, "run2");
            string a2 = WriteAutoSave("基礎_emergency_20260924_112000.pdj", Now.AddMinutes(-40), planA, "run1");
            string legacy = WriteAutoSave("基礎_autosave_20260924_113000.pdj", Now.AddMinutes(-30), null);

            CollectionAssert.AreEqual(new[] { a2, a1 }, AutoSaveService.GetAutoSaveFilesForProject(_autoSave, planA),
                "案A の一覧に、別のフォルダの同じ名前のプロジェクト (案B) か、どれのものか分からない旧いファイルが混ざっています");
            CollectionAssert.AreEqual(new[] { b1 }, AutoSaveService.GetAutoSaveFilesForProject(_autoSave, planB));
            Assert.IsTrue(File.Exists(legacy));
        }

        /// <summary>
        /// 復元した作業は、明示的に保存するまで未保存として扱うこと。
        ///
        /// 復元は通常の読み込みと同じ処理を通り、そこで「保存していない作業は無い」に戻る。
        /// 以前はそのままだったので、復元してすぐ閉じても「保存しますか？」が出ず、
        /// 正常終了の後始末でこのセッションの自動保存も確認済みになって、復元した内容が消えた。
        /// </summary>
        [TestMethod]
        public void ARestoredProject_CountsAsUnsavedWork()
        {
            string source = WriteSource("基礎.pdj", Now.AddHours(-2));
            string autoSave = WriteAutoSave("基礎_autosave_20260924_113000.pdj", Now.AddMinutes(-30), source, "run1");

            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var error = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    var vm = new PileDesign.ViewModels.MainWindowViewModel();
                    try
                    {
                        bool restored = vm.TryRestoreAutoSave(new AutoSaveService.RestoreCandidate(autoSave, Now, source, "run1"));

                        Assert.IsTrue(restored, "(前提) 復元できていません");
                        Assert.AreEqual(source, vm.CurrentFilePath, "(前提) 復元後の保存先が元ファイルになっていません");
                        Assert.IsTrue(vm.HasUnsavedWork,
                            "復元した作業が保存済み扱いです。閉じても保存の確認が出ず、復元した内容が消えます");
                    }
                    finally
                    {
                        vm.EndAutoSaveSessionNormally();   // 復元で動き出した自動保存のタイマーを止める
                    }
                }, out bool timedOut);
                Assert.IsFalse(timedOut, "時間内に終わりませんでした");
                if (error != null) throw error;
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }

        /// <summary>元ファイルの記録だけを読む処理が、保存ファイルの形の中から正しく拾えること。</summary>
        [TestMethod]
        public void TheRecordedSourcePath_IsReadWithoutLoadingTheWholeFile()
        {
            string withSource = WriteAutoSave("A_autosave_20260924_110000.pdj", Now, @"C:\作業\基礎.pdj");
            string without = WriteAutoSave("B_autosave_20260924_110000.pdj", Now, null);

            Assert.AreEqual(@"C:\作業\基礎.pdj", AutoSaveService.ReadRecordedOrigin(withSource).SourceFilePath);
            Assert.IsNull(AutoSaveService.ReadRecordedOrigin(without).SourceFilePath);

            string broken = Path.Combine(_autoSave, "C_autosave_20260924_110000.pdj");
            File.WriteAllText(broken, "{ 途中");
            Assert.IsNull(AutoSaveService.ReadRecordedOrigin(broken).SourceFilePath, "壊れたファイルで例外が出ています");
        }

        /// <summary>
        /// 元ファイルと起動ごとの印を、ファイルの先頭に書くこと (候補の検索が頭だけ読めば済むように)。
        /// </summary>
        [TestMethod]
        public void TheOriginIsWrittenAtTheHeadOfTheFile()
        {
            string path = WriteAutoSave("A_autosave_20260924_110000.pdj", Now, @"C:\作業\基礎.pdj", "run1");
            string text = File.ReadAllText(path);

            int source = text.IndexOf("\"SourceFilePath\"", StringComparison.Ordinal);
            int session = text.IndexOf("\"AutoSaveSessionId\"", StringComparison.Ordinal);
            int input = text.IndexOf("\"InputModel\"", StringComparison.Ordinal);
            Assert.IsTrue(source >= 0 && session >= 0 && input >= 0, "(前提) 項目が書かれていません");
            Assert.IsTrue(source < input && session < input,
                "元ファイルの記録が入力より後ろに書かれています。候補の検索がファイル全体を読むことになります");

            // 先頭に並べても、読み込みは変わらないこと ($id が先頭から外れると参照が解けない)
            var loaded = Service().LoadProjectData(path);
            Assert.AreEqual(@"C:\作業\基礎.pdj", loaded.SourceFilePath);
            Assert.AreEqual("run1", loaded.AutoSaveSessionId);
            Assert.IsNotNull(loaded.InputModel);
        }

        /// <summary>
        /// 大きなファイルでも、頭だけ読んで記録を拾うこと。
        /// 記録の後ろに頭の大きさを超える中身があっても、そこまでは読まない。
        /// </summary>
        [TestMethod]
        public void TheOriginIsFoundInTheHeadOfALargeFile()
        {
            string path = Path.Combine(_autoSave, "Big_autosave_20260924_110000.pdj");
            string padding = new('x', AutoSaveService.OriginHeadBytes * 4);
            File.WriteAllText(path,
                $"{{\"$id\":\"1\",\"SourceFilePath\":\"C:\\\\a\\\\b.pdj\",\"AutoSaveSessionId\":\"run9\",\"InputModel\":{{\"Pad\":\"{padding}\"}}}}");

            var (source, session) = AutoSaveService.ReadRecordedOrigin(path);
            Assert.AreEqual(@"C:\a\b.pdj", source);
            Assert.AreEqual("run9", session);
        }

        /// <summary>
        /// 先頭に書くようにする前の自動保存 (旧形式、記録が末尾) も、元のファイルを拾うこと。
        /// 小さなものは全体を、頭に収まらないものは末尾を読む (入力だけの自動保存でも 100 KB を超える)。
        /// </summary>
        [TestMethod]
        public void LegacyFilesWithTheOriginAtTheEnd_AreRead()
        {
            string small = Path.Combine(_autoSave, "Small_autosave_20260924_110000.pdj");
            File.WriteAllText(small,
                "{\"$id\":\"1\",\"InputModel\":{\"$id\":\"2\",\"A\":[1,2,3]},\"SourceFilePath\":\"C:\\\\s.pdj\",\"AutoSaveSessionId\":\"old\"}");
            Assert.AreEqual((@"C:\s.pdj", "old"), AutoSaveService.ReadRecordedOrigin(small));

            // 頭に収まらない旧形式。本物の記録より<b>後ろ</b> (= 末尾の読む範囲の中) の文字列の値に、
            // 項目名と同じ文字 (エスケープされた引用符付き) を紛れ込ませる。最後に現れたものを拾うので、
            // 値の中の並びを項目名と取り違えると偽のパスが返る
            string large = Path.Combine(_autoSave, "Large_autosave_20260924_110000.pdj");
            string padding = new('x', AutoSaveService.OriginHeadBytes * 2);
            File.WriteAllText(large,
                "{\"$id\":\"1\",\"InputModel\":{\"Pad\":\"" + padding + "\"},"
                + "\"SourceFilePath\":\"C:\\\\l.pdj\","
                + "\"Note\":\"\\\"SourceFilePath\\\": \\\"C:\\\\偽.pdj\\\"\"}");
            Assert.AreEqual((@"C:\l.pdj", (string?)null), AutoSaveService.ReadRecordedOrigin(large),
                "頭に収まらない旧形式のファイルから、末尾の元ファイルの記録を拾えていません");

            // 字下げして書いたもの (項目名と値のあいだに空白) も拾う
            string indented = Path.Combine(_autoSave, "Indented_autosave_20260924_110000.pdj");
            File.WriteAllText(indented,
                "{\n  \"$id\": \"1\",\n  \"InputModel\": { \"Pad\": \"" + padding + "\" },\n  \"SourceFilePath\": \"C:\\\\i.pdj\",\n  \"AutoSaveSessionId\": null\n}");
            Assert.AreEqual((@"C:\i.pdj", (string?)null), AutoSaveService.ReadRecordedOrigin(indented));
        }

        /// <summary>
        /// 旧形式の大きな自動保存でも、元のファイルのほうが新しければ勧めないこと
        /// (「記録なし」と扱うと、保存した新しい内容を古い自動保存で上書きさせることになる)。
        /// </summary>
        [TestMethod]
        public void ALargeLegacyAutoSaveOlderThanItsSource_IsNotOffered()
        {
            string source = WriteSource("Foo.pdj", Now.AddMinutes(-10));
            string legacy = Path.Combine(_autoSave, "Foo_autosave_20260924_113000.pdj");
            string padding = new('x', AutoSaveService.OriginHeadBytes * 2);
            File.WriteAllText(legacy,
                "{\"$id\":\"1\",\"InputModel\":{\"Pad\":\"" + padding + "\"},\"SourceFilePath\":"
                + System.Text.Json.JsonSerializer.Serialize(source) + "}");
            File.SetLastWriteTime(legacy, Now.AddMinutes(-30));

            Assert.IsNull(AutoSaveService.FindRestoreCandidate(_autoSave, Now),
                "元ファイルのほうが新しい旧形式の大きな自動保存を勧めています");
        }

        /// <summary>
        /// この起動の中で扱った候補は、見送りの印を付けられなくても選び直さないこと。
        /// 選び直すと、壊れた候補のエラーと復元の確認が延々と繰り返される。
        /// </summary>
        [TestMethod]
        public void HandledCandidatesAreNotChosenAgain()
        {
            string older = WriteAutoSave("Foo_autosave_20260924_113000.pdj", Now.AddMinutes(-30), null, "run1");
            string broken = WriteAutoSave("Foo_autosave_20260924_114000.pdj", Now.AddMinutes(-20), null, "run1");

            var handled = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { broken };
            Assert.AreEqual(older, AutoSaveService.FindRestoreCandidate(_autoSave, Now, handled)?.FilePath,
                "扱った候補 (印を付けられなかった) をまた選んでいます");

            handled.Add(older.ToUpperInvariant());   // 大文字・小文字の違いでは別物にしない
            Assert.IsNull(AutoSaveService.FindRestoreCandidate(_autoSave, Now, handled));
        }

        [TestMethod]
        public void ReadingTheOriginDoesNotLoadTheWholeFile()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "Services", "AutoSaveService.cs"),
                "internal static (string? SourceFilePath, string? SessionId) ReadRecordedOrigin(string autoSaveFile)");
            Assert.IsFalse(body.Contains("ReadAllBytes", StringComparison.Ordinal)
                           || body.Contains("ReadAllText", StringComparison.Ordinal),
                "候補の記録を探すのに、ファイル全体をメモリへ読んでいます");
        }

        /// <summary>起動時の確認が、選び方と見送りをこのサービスに任せていること。</summary>
        [TestMethod]
        public void TheStartupPrompt_UsesTheServiceForBothChoosingAndDismissing()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs"),
                "public void CheckAutoSaveRestore()");

            StringAssert.Contains(body, "_autoSaveService.FindRestoreCandidate(handled)");
            StringAssert.Contains(body, "_autoSaveService.DismissRestoreCandidatesUpTo(candidate)");

            // 扱った候補は、見送りの印を付けられたかどうかに関わらず、この起動の中では選び直さない
            int find = body.IndexOf("_autoSaveService.FindRestoreCandidate(handled)", StringComparison.Ordinal);
            int add = body.IndexOf("handled.Add(candidate.FilePath);", StringComparison.Ordinal);
            Assert.IsTrue(find >= 0 && add > find && add < body.IndexOf("MessageService.Show(", StringComparison.Ordinal),
                "扱った候補を除かずに次の候補を探しています。印を付けられないと、同じ候補の確認が繰り返されます");

            // 復元に失敗したら、その 1 件だけを見送って次の候補へ進む (まとめて見送らない)
            int failed = body.IndexOf("!TryRestoreAutoSave(candidate)", StringComparison.Ordinal);
            int single = body.IndexOf("_autoSaveService.DismissRestoreCandidate(candidate);", StringComparison.Ordinal);
            int next = body.IndexOf("continue;", StringComparison.Ordinal);
            int bulk = body.IndexOf("_autoSaveService.DismissRestoreCandidatesUpTo(candidate)", StringComparison.Ordinal);
            Assert.IsTrue(failed >= 0 && single > failed && next > single && bulk > next,
                "復元に失敗したときに、その 1 件だけを見送って次の候補へ進んでいません");
            Assert.IsFalse(body.Contains("CreationTime", StringComparison.Ordinal),
                "起動時の確認が自前で自動保存ファイルの時刻を見ています");
        }
    }
}
