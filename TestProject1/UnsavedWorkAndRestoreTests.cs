using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 作業を失わないこと。
    ///
    /// この製品で最も痛い壊れ方は「入力した内容が消える」「保存したつもりが別の場所に
    /// 書かれる」で、次の 4 つが実際にそうなっていた。
    ///
    /// <list type="number">
    /// <item>起動して新規に入力しただけのセッションは、自動保存も緊急保存も動かなかった
    ///   (Start を呼ぶのが「開く」と「名前を付けて保存」だけだった)</item>
    /// <item>自動保存から復元すると、保存先が<b>カレントディレクトリ相対</b>の裸のファイル名になり、
    ///   Ctrl+S が元ファイルではない場所へ書いていた</item>
    /// <item>「開く」「最近使ったファイル」「ドラッグ＆ドロップ」「起動引数」は
    ///   未保存の作業を確認なしに捨てていた (確認していたのは「新規作成」だけ)</item>
    /// <item>読込に失敗すると、代入だけ済んで移行処理が落ち、前の入力も新しい入力も
    ///   残らない状態になっていた</item>
    /// </list>
    ///
    /// 画面のダイアログを伴う経路は単体で動かせないので、
    /// サービス層の実挙動と、呼び出し側のソース走査の二本立てで押さえる。
    /// </summary>
    [TestClass]
    public class UnsavedWorkAndRestoreTests
    {
        private static JsonSerializerOptions SaveOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        /// <summary>
        /// ソースを読むテストの起点。出力先 (bin) からと、このファイル自身の位置からの
        /// 両方を辿る。出力先を変えてビルドしたとき (アプリ起動中に bin が使えない場合など) でも
        /// 検査が空振りしないようにするため。
        /// </summary>
        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

        private static string ReadSource(params string[] relativeParts)
            => File.ReadAllText(Path.Combine(new[] { FindSolutionRoot() }.Concat(relativeParts).ToArray()));

        /// <summary>
        /// メソッドの本体 (最初の '{' から対応する '}' まで) を切り出す。
        /// 「この経路がこの呼び出しを通っているか」をソースで検査するために使う。
        /// </summary>
        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");

            int open = source.IndexOf('{', at);
            Assert.IsTrue(open >= 0, $"本体の開き括弧が見つかりません: {signatureFragment}");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source[open..(i + 1)];
                }
            }
            Assert.Fail($"本体の閉じ括弧が見つかりません: {signatureFragment}");
            return "";
        }

        private static void InvokePrivate(object obj, string methodName)
        {
            var m = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"メソッド {methodName} が見つかりません");
            m.Invoke(obj, null);
        }

        /// <summary>自動保存フォルダは共有なので、テストが作ったものだけを消す。</summary>
        private static void DeleteByPrefix(string folder, string prefix)
        {
            try
            {
                foreach (var f in Directory.GetFiles(folder, prefix + "*.pdj"))
                {
                    try { File.Delete(f); } catch (IOException) { }
                }
            }
            catch (DirectoryNotFoundException) { }
        }

        // ───────── (1) 名前の無いセッションでも自動保存が動く ─────────

        /// <summary>
        /// <b>Start を呼んでいなくても</b>、ライブ状態が取れるなら自動保存する。
        ///
        /// 以前は <c>_currentInputModel</c> (Start でしか設定されない) が null なら即 return
        /// していたため、起動して新規に入力しただけのセッションは 3 分ごとの自動保存が
        /// 一度も走らなかった。落ちれば全損する。
        /// </summary>
        [TestMethod]
        public void AutoSave_RunsForASessionThatWasNeverStarted()
        {
            var auto = new AutoSaveService(new FileOperationService(SaveOptions()));
            DeleteByPrefix(auto.AutoSaveFolder, "Untitled_autosave_");
            try
            {
                auto.LiveStateProvider = () => (new InputModel(), null, null);

                InvokePrivate(auto, "PerformAutoSave");

                var produced = Directory.GetFiles(auto.AutoSaveFolder, "Untitled_autosave_*.pdj");
                Assert.AreEqual(1, produced.Length,
                    "Start を呼んでいないセッションで自動保存が走っていない。"
                    + "起動して新規に入力しただけの状態が保護されない");
            }
            finally
            {
                DeleteByPrefix(auto.AutoSaveFolder, "Untitled_autosave_");
                auto.Stop();
            }
        }

        /// <summary>
        /// 緊急保存はタイマーが止まっていても効く。
        /// 「新規作成」は Stop() を通るので、そこで落ちたときこそ効く必要がある。
        /// </summary>
        [TestMethod]
        public void EmergencySave_WorksAfterStop()
        {
            var auto = new AutoSaveService(new FileOperationService(SaveOptions()));
            DeleteByPrefix(auto.AutoSaveFolder, "Untitled_emergency_");
            try
            {
                auto.LiveStateProvider = () => (new InputModel(), null, null);
                auto.Stop();

                var path = auto.TryEmergencyAutoSave();

                Assert.IsNotNull(path, "Stop 後に緊急保存が効かない");
                Assert.IsTrue(File.Exists(path), "緊急保存のファイルが作られていない");
            }
            finally
            {
                DeleteByPrefix(auto.AutoSaveFolder, "Untitled_emergency_");
                auto.Stop();
            }
        }

        /// <summary>保存する状態が無ければ、静かに何もしない (例外にしない)。</summary>
        [TestMethod]
        public void EmergencySave_WithoutAnyState_ReturnsNull()
        {
            var auto = new AutoSaveService(new FileOperationService(SaveOptions()));
            try
            {
                Assert.IsNull(auto.TryEmergencyAutoSave(),
                    "保存する状態が無いのにファイルを作っている");
            }
            finally { auto.Stop(); }
        }

        // ───────── (2) 復元後の保存先 ─────────

        /// <summary>
        /// 自動保存は<b>元ファイルのフルパス</b>を中に記録する。
        ///
        /// ファイル名には拡張子を除いた名前しか入らない。名前から保存先を組み立てると
        /// <c>Foo.pdj</c> という相対パスになり、復元後の Ctrl+S が元ファイルではなく
        /// カレントディレクトリの同名ファイルへ書いてしまう。
        /// </summary>
        [TestMethod]
        public void AutoSave_RecordsTheFullPathOfTheOriginalFile()
        {
            var fileOps = new FileOperationService(SaveOptions());
            var auto = new AutoSaveService(fileOps);
            const string prefix = "PileDesignSourcePathTest";
            string original = Path.Combine(Path.GetTempPath(), prefix + ".pdj");

            DeleteByPrefix(auto.AutoSaveFolder, prefix + "_autosave_");
            try
            {
                auto.Start(original, new InputModel(), null, null);

                InvokePrivate(auto, "PerformAutoSave");

                var produced = Directory.GetFiles(auto.AutoSaveFolder, prefix + "_autosave_*.pdj").Single();
                var loaded = fileOps.LoadProjectData(produced);

                Assert.IsNotNull(loaded, "自動保存ファイルを読み戻せない");
                Assert.AreEqual(original, loaded.SourceFilePath,
                    "元ファイルのフルパスが記録されていない。復元後の保存先が相対パスになる");
            }
            finally
            {
                DeleteByPrefix(auto.AutoSaveFolder, prefix + "_autosave_");
                auto.Stop();
            }
        }

        /// <summary>
        /// 手動保存は元ファイルのパスを書かない。保存先は呼び出し側が知っている。
        /// 配布するファイルに利用者名入りのパスを残さないためでもある。
        /// </summary>
        [TestMethod]
        public void ManualSave_DoesNotRecordASourcePath()
        {
            var fileOps = new FileOperationService(SaveOptions());
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignManualSave", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "model.pdj");
                fileOps.SaveProjectData(path, new InputModel(), null);

                var loaded = fileOps.LoadProjectData(path);

                Assert.IsNotNull(loaded);
                Assert.IsNull(loaded.SourceFilePath,
                    "手動保存が元ファイルのパスを書き込んでいる");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// 復元は<b>ファイル名から保存先を組み立てない</b>こと。
        ///
        /// 組み立てると必ず相対パスになる (ディレクトリが名前に入っていないため)。
        /// 記録したフルパスを使い、それが無ければ null にして
        /// 次の保存を「名前を付けて保存」に倒す。
        /// </summary>
        [TestMethod]
        public void Restore_DoesNotBuildAFilePathFromTheAutoSaveFileName()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.cs"),
                "public void CheckAutoSaveRestore()");

            StringAssert.Contains(body, "SourceFilePath",
                "復元が、自動保存ファイルに記録した元ファイルのパスを見ていない");
            Assert.IsFalse(body.Contains("+ \".pdj\""),
                "復元がファイル名から保存先を組み立てている。"
                + "カレントディレクトリ相対の別ファイルへ上書き保存される");
        }

        // ───────── (3) 未保存の作業の確認 ─────────

        /// <summary>
        /// 入力を捨てて別のものを読み込む経路は、<b>すべて</b>同じ確認を通ること。
        ///
        /// 確認していたのは「新規作成」だけで、「開く」「最近使ったファイル」は素通りだった。
        /// ドラッグ＆ドロップと起動引数は最近使ったファイルの経路に流れるので、
        /// この 2 つを押さえれば 4 経路が塞がる。
        /// </summary>
        [TestMethod]
        public void EveryLoadPath_AsksBeforeDiscardingUnsavedWork()
        {
            var fileIo = ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            var main = ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");

            foreach (var (source, signature, description) in new[]
            {
                (fileIo, "public async Task NewInputModelFile()",  "新規作成"),
                (fileIo, "public async Task OpenInputModelFile()", "ファイルを開く"),
                (main,   "public async Task OpenFromMru(string filePath)",
                         "最近使ったファイル (ドラッグ＆ドロップ・起動引数もここへ来る)"),
            })
            {
                var body = ExtractMethodBody(source, signature);
                StringAssert.Contains(body, "ConfirmDiscardUnsavedWorkAsync",
                    $"{description}: 未保存の作業を確認せずに捨てている");
            }
        }

        /// <summary>
        /// 保存は<b>成否を返す</b>こと。
        ///
        /// 返さないと、「保存しますか？→はい」で保存ダイアログをキャンセルしても
        /// 保存できたことにして先へ進み、未保存のまま終了・新規作成してしまう。
        /// </summary>
        [TestMethod]
        public void SaveCommands_ReportWhetherTheySucceeded()
        {
            foreach (var name in new[] { "SaveInputModelFileCoreAsync", "SaveInputModelFileAsCoreAsync" })
            {
                var method = typeof(MainWindowViewModel).GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                Assert.IsNotNull(method, $"{name} が見つからない");
                Assert.AreEqual(typeof(Task<bool>), method.ReturnType,
                    $"{name} が成否を返さない。保存のキャンセル・失敗を呼び出し側が判別できない");
            }
        }

        // ───────── (4) 読込に失敗しても前の入力を壊さない ─────────

        /// <summary>
        /// 読込は<b>ひとつの入口</b>を通ること。
        ///
        /// 各経路が自分で <c>CurrentInputModel = projectData.InputModel</c> を代入していたため、
        /// 中身の検査 (InputModel が入っているか) も、失敗したときの巻き戻しも無かった。
        /// 無関係な JSON を開くと入力が null になり、移行処理が落ちれば前の作業も失われる。
        /// </summary>
        [TestMethod]
        public void EveryLoadPath_GoesThroughTheSingleEntryPoint()
        {
            foreach (var file in new[] { "MainWindowViewModel.FileIO.cs", "MainWindowViewModel.cs" })
            {
                var source = ReadSource("Graphics_r1", "ViewModels", file);

                // 入口自身の本体は当然この代入を持つので、走査の対象から外す
                if (source.Contains("private void ApplyLoadedProjectData("))
                    source = source.Replace(
                        ExtractMethodBody(source, "private void ApplyLoadedProjectData("), "");

                foreach (var raw in source.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("//")) continue;   // 説明文は対象外

                    Assert.IsFalse(
                        line.Contains("CurrentInputModel = projectData.InputModel"),
                        $"{file}: 読込結果を直接代入している。ApplyLoadedProjectData を通すこと "
                        + "(中身の検査と、失敗時に前の入力へ戻す処理がここにある)");
                }
            }
        }

        /// <summary>
        /// 入口は「入力データが無いファイル」を弾き、前の入力へ戻す作りであること。
        ///
        /// <c>JsonSerializer.Deserialize</c> は JSON オブジェクトでありさえすれば非 null を返すので、
        /// 「projectData が null か」では別アプリの .json を弾けない。
        /// </summary>
        [TestMethod]
        public void TheSingleEntryPoint_ValidatesAndRollsBack()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyLoadedProjectData(");

            StringAssert.Contains(body, "projectData.InputModel == null",
                "入力データの有無を検査していない");
            StringAssert.Contains(body, "CurrentInputModel = previousInput",
                "読込に失敗したときに前の入力へ戻していない");
        }

        /// <summary>
        /// 実際に「入力データの無いファイル」を読ませて、入口の検査が働くこと。
        /// ここは画面を通らないので単体で動かせる。
        /// </summary>
        [TestMethod]
        public void AForeignJsonFile_IsRejectedInsteadOfBecomingAnEmptyModel()
        {
            var fileOps = new FileOperationService(SaveOptions());
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignForeignJson", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "foreign.json");
                File.WriteAllText(path, "{ \"name\": \"別のアプリのファイル\" }");

                var loaded = fileOps.LoadProjectData(path);

                // 読めてしまう (JSON オブジェクトなので) が、中身が無いことは検出できる。
                // 呼び出し側はこの状態を「読込失敗」として扱わなければならない。
                Assert.IsNull(loaded?.InputModel,
                    "入力データが無いファイルなのに InputModel が入っている");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }
    }
}
