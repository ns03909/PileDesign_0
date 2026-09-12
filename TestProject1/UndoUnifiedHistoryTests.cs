using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// Undo の履歴は<b>1 本</b>で、戻す入口も 1 組であること。
    ///
    /// <para>この履歴には 2 種類の手が入る。入力を丸ごと控える「控え」(<c>SaveState</c>) と、
    /// 戻し方・やり直し方を組で持つ「アクション」(<c>PushAction</c>) で、画面によって
    /// どちらを使うかが違う。以前は入口も 2 組 (<c>Undo</c> / <c>UndoSnapshot</c>) あり、
    /// <c>Undo</c> は控えを見ず、<c>UndoSnapshot</c> は控えを全部消費してからアクションへ移っていた。
    /// 「どちらの入口を呼ぶか」を呼び出し側が知っている必要があり、間違えると<b>押しても無反応</b>になる
    /// (グラフ窓の Ctrl+Z が実際にそうだった)。2026-09-12 に 1 本化した。</para>
    /// </summary>
    [TestClass]
    public class UndoUnifiedHistoryTests
    {
        /// <summary>控えとアクションが混ざっても、新しい手から順に戻ること。</summary>
        [TestMethod]
        public void MixedStepsAreUndoneNewestFirst()
        {
            var undo = new UndoManager();
            var log = new List<string>();

            undo.SaveState("控え1", "控え1");          // 1 手目
            undo.PushAction(() => log.Add("アクションを戻した"), () => log.Add("アクションをやり直した"),
                "アクション");                          // 2 手目
            undo.SaveState("控え2", "控え2");          // 3 手目

            // 3 手目 (控え2) を戻す → 現在の状態は控え1 まで下がる
            undo.Undo();
            Assert.AreEqual(0, log.Count, "控えを戻す手でアクションが動きました (順序が入れ替わっています)");
            Assert.AreEqual("控え1", undo.CurrentState,
                "控えを 1 手戻したのに、戻った先の状態になっていません");

            // 2 手目 (アクション) を戻す
            undo.Undo();
            CollectionAssert.AreEqual(new[] { "アクションを戻した" }, log,
                "アクションの手が戻っていません");
            Assert.AreEqual("控え1", undo.CurrentState, "アクションを戻す手で控えが動きました");
        }

        /// <summary>やり直しは戻したのと逆順、つまり古い手からであること。</summary>
        [TestMethod]
        public void MixedStepsAreRedoneOldestFirst()
        {
            var undo = new UndoManager();
            var log = new List<string>();

            undo.SaveState("控え1");
            undo.PushAction(() => log.Add("undo"), () => log.Add("redo"), "アクション");
            undo.SaveState("控え2");

            undo.Undo();   // 控え2 の手
            undo.Undo();   // アクションの手
            log.Clear();

            undo.Redo();   // 先にアクション (古い手)
            CollectionAssert.AreEqual(new[] { "redo" }, log, "やり直しが古い手からになっていません");
            Assert.AreEqual("控え1", undo.CurrentState, "アクションのやり直しで控えが動きました");

            undo.Redo();   // 次に控え2
            Assert.AreEqual("控え2", undo.CurrentState, "控えのやり直しで状態が進んでいません");
        }

        /// <summary>
        /// 2 つの入口は同じ中身であること。どちらを呼んでも 1 手戻る。
        /// </summary>
        [TestMethod]
        public void BothEntryPointsDoTheSameThing()
        {
            var byUndo = new UndoManager();
            byUndo.SaveState("a");
            byUndo.SaveState("b");
            byUndo.Undo();

            var bySnapshot = new UndoManager();
            bySnapshot.SaveState("a");
            bySnapshot.SaveState("b");
            bySnapshot.UndoSnapshot();

            Assert.AreEqual(byUndo.CurrentState, bySnapshot.CurrentState,
                "Undo と UndoSnapshot で結果が違います (入口が 2 つある意味になってしまいます)");
            Assert.AreEqual(byUndo.CurrentIndex, bySnapshot.CurrentIndex);
        }

        /// <summary>控えだけの履歴でも、Undo（アクション側の入口）で戻れること。</summary>
        [TestMethod]
        public void UndoAlsoWalksBackASnapshotOnlyHistory()
        {
            var undo = new UndoManager();
            undo.SaveState("a");
            undo.SaveState("b");

            Assert.IsTrue(undo.CanUndo, "控えが積まれているのに戻せません");
            undo.Undo();
            Assert.AreEqual("a", undo.CurrentState,
                "Undo が控えを見ていません (以前はアクションしか見ず、押しても無反応だった)");
        }

        /// <summary>何も積まれていなければ、戻しても落ちず何も起きないこと。</summary>
        [TestMethod]
        public void AnEmptyHistoryIsSafeToUndo()
        {
            var undo = new UndoManager();
            Assert.IsFalse(undo.CanUndo);
            Assert.IsFalse(undo.CanRedo);
            undo.Undo();
            undo.Redo();
            Assert.IsNull(undo.CurrentState);
        }

        /// <summary>
        /// <b>Undo コマンドを持つ画面は、必ず履歴に何か積むこと。</b>
        ///
        /// <para>積まないまま Ctrl+Z を割り当てると、押しても何も起きない。理由も出ない。
        /// グラフ窓が実際にそうなっていた (履歴に何も積まないのに Ctrl+Z / Ctrl+Y を割り当てていた)。</para>
        /// </summary>
        [TestMethod]
        public void EveryViewModelWithAnUndoCommandActuallyPushesSomething()
        {
            string dir = TestSource.Dir("Graphics_r1", "ViewModels");
            var offenders = new List<string>();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (!text.Contains("UndoManager")) continue;
                scanned++;

                bool hasUndoEntry = Regex.IsMatch(text, @"(UndoCommand|void\s+Undo\s*\()");
                if (!hasUndoEntry) continue;

                // 積み方は画面によって呼び名が違う (_undoManager / UndoStack など) ので、
                // 変数名ではなくメソッド名で見る
                bool pushes = Regex.IsMatch(text, @"\.(Push|PushAction|PushState|SaveState)\s*\(");
                if (!pushes) offenders.Add(Path.GetFileName(file));
            }

            TestSource.AssertScanned(scanned, 8, "Undo を持つ ViewModel");
            Assert.AreEqual(0, offenders.Count,
                "履歴に何も積まないのに戻す操作を持っている画面があります (押しても無反応になります): "
                + string.Join(" / ", offenders));
        }

        /// <summary>
        /// <b>控え (キャンセル用) を持つ画面は、Ctrl+Z でも戻せること。</b>
        ///
        /// <para>入力を編集するダイアログは実体を直接書き換え、戻し方を 2 通り持つ。
        /// キャンセル (× で閉じても通る) で丸ごと戻す「控え」と、1 手ずつ戻す Ctrl+Z である。
        /// どちらかを配線し忘れると、選び直したつもりが戻っていないまま解析へ行く。
        /// このリポジトリで実際に 4 件踏んだ形なので、<b>対応表を手で書かずに</b>
        /// 控えの有無から機械的に見張る。</para>
        ///
        /// <para>解析を走らせるだけの窓 (水平解析・鉛直解析など) は入力の控えを持たないので、
        /// ここでは対象にならない。除外リストを置かずに済むのがこの見方の利点。</para>
        /// </summary>
        [TestMethod]
        public void EveryDialogWithABackupAlsoHasCtrlZ()
        {
            string vmDir = TestSource.Dir("Graphics_r1", "ViewModels");
            string viewDir = TestSource.Dir("Graphics_r1", "Views");
            var xamls = Directory.EnumerateFiles(viewDir, "*.xaml", SearchOption.AllDirectories)
                .ToDictionary(f => f, File.ReadAllText);

            var offenders = new List<string>();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(vmDir, "*ViewModel*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                // 控え = キャンセルで戻すために入力を写しておくもの (Prev〜Input / PrevFundamentalInput 等)
                if (!Regex.IsMatch(text, @"\bPrev[A-Z]\w*(Input|State|Items)\b")) continue;

                string vmName = Path.GetFileNameWithoutExtension(file).Split('.')[0];
                scanned++;

                if (!Regex.IsMatch(text, @"(UndoCommand|void\s+Undo\s*\()"))
                    offenders.Add($"{vmName}: 控えはあるが Ctrl+Z の入口が無い");
                if (!Regex.IsMatch(text, @"(CancelCommand|void\s+OnCancel\s*\()"))
                    offenders.Add($"{vmName}: 控えはあるがキャンセルが無い");

                // この ViewModel を DataContext にしている窓が Ctrl+Z を割り当てていること
                var windows = xamls.Where(kv => kv.Value.Contains($"Type=local:{vmName}")).ToList();
                if (windows.Count == 0) continue;   // 窓を持たない ViewModel (パネル等) は対象外
                foreach (var (path, xaml) in windows.Select(kv => (kv.Key, kv.Value)))
                {
                    bool ctrlZ = Regex.IsMatch(xaml, @"Key=""Z""\s+Modifiers=""Control""")
                                 && xaml.Contains("UndoCommand");
                    if (!ctrlZ)
                        offenders.Add($"{Path.GetFileName(path)}: 控えを持つ画面なのに Ctrl+Z が割り当てられていない");
                }
            }

            TestSource.AssertScanned(scanned, 4, "控えを持つ ViewModel");
            Assert.AreEqual(0, offenders.Count,
                "戻し方の片方が欠けています (戻したつもりで戻っていない状態になります):\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
