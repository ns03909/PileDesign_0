using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 子ウィンドウ (杭断面・杭頭部・杭体・荷重・地盤・要素分割・沈下) の元に戻す/やり直しで、
    /// 戻したあとの編集がやり直しで上書きされないこと。
    ///
    /// <para>控えは操作の直前 (フォーカスが入る・マウスを押す) と欄から外れるときに取る。いまの位置の控えと同じ状態は
    /// 積まない (戻したあとにフォーカスを移しただけで、やり直せる手を消さないため)。そのため
    /// 「編集 → C、Ctrl+Z → B、コンボボックスで D を選ぶ」では、選ぶ直前の控え (= B) は積まれず、
    /// D はまだ控えに入っていない。フォーカスを動かさずに Ctrl+Y を押すと、以前は C をやり直し、
    /// <b>選んだ D を黙って上書きしていた</b>。</para>
    ///
    /// <para>戻す・やり直すの入口で、いまの入力 (<see cref="UndoManager.LiveStateProvider"/>) を
    /// いまの位置の控えと比べ、違えば新しい手として残す (やり直せる手は捨てる)。</para>
    /// </summary>
    [TestClass]
    public class ChildUndo20261009Tests
    {
        private sealed class Box { public int Value { get; set; } }

        /// <summary>画面の値 (控えの元) と、窓の段の仕組み (UndoCapture.Take) と同じ設定の履歴。</summary>
        private static (UndoManager m, Box live) Scene()
        {
            var live = new Box { Value = 1 };
            var m = new UndoManager
            {
                StateFingerprint = UndoCapture.Fingerprint,
                LiveStateProvider = () => new Box { Value = live.Value },
            };
            return (m, live);
        }

        /// <summary>控えを今の画面へ当てる (ViewModel の ApplyUndoState 相当)。</summary>
        private static void Apply(UndoManager m, Box live) => live.Value = ((Box)m.CurrentState!).Value;

        private static void Take(UndoManager m, Box live) => m.SaveState(new Box { Value = live.Value });

        [TestMethod]
        public void RedoAfterAnUnsavedEdit_KeepsTheEdit_AndDropsTheOldFuture()
        {
            var (m, live) = Scene();
            Take(m, live);                 // B = 1 (開いた直後)
            live.Value = 2; Take(m, live); // C = 2 (欄から外れた)
            m.Undo(); Apply(m, live);      // Ctrl+Z → B
            Assert.AreEqual(1, live.Value);

            Take(m, live);                 // コンボボックスを開く直前の控え (= B なので積まれない)
            Assert.IsTrue(m.CanRedo, "前提: 戻したあとにフォーカスを移しただけでは、やり直せる手は残る");
            live.Value = 4;                // D を選んだ (まだ控えに入っていない)

            m.Redo(); Apply(m, live);      // Ctrl+Y
            Assert.AreEqual(4, live.Value, "戻したあとの編集 (D) が、やり直しで古い手 (C) に上書きされました");
            Assert.IsFalse(m.CanRedo, "新しい編集のあとに、分岐前の手がまだやり直せます");

            m.Undo(); Apply(m, live);      // D も 1 手として戻せる
            Assert.AreEqual(1, live.Value, "D を戻すと B に戻るはずです");
            m.Redo(); Apply(m, live);
            Assert.AreEqual(4, live.Value, "D をやり直せません");
        }

        [TestMethod]
        public void UndoAfterAnUnsavedEdit_KeepsTheEditRedoable()
        {
            var (m, live) = Scene();
            Take(m, live);                 // A = 1
            live.Value = 2; Take(m, live); // C = 2
            m.Undo(); Apply(m, live);      // → A
            live.Value = 4;                // D (控えに入っていない)

            m.Undo(); Apply(m, live);      // Ctrl+Z: D を 1 手として残してから戻る
            Assert.AreEqual(1, live.Value);
            m.Redo(); Apply(m, live);
            Assert.AreEqual(4, live.Value, "戻す前の編集 (D) がやり直しで戻りません (C が出るなら分岐前の手です)");
        }

        [TestMethod]
        public void PlainUndoRedo_IsUnchanged_WhenNothingWasEditedInBetween()
        {
            var (m, live) = Scene();
            Take(m, live);
            live.Value = 2; Take(m, live);
            live.Value = 3; Take(m, live);
            m.Undo(); Apply(m, live);
            m.Undo(); Apply(m, live);
            Assert.AreEqual(1, live.Value);
            m.Redo(); Apply(m, live);
            Assert.AreEqual(2, live.Value);
            m.Redo(); Apply(m, live);
            Assert.AreEqual(3, live.Value);
            Assert.AreEqual(3, m.History.Count, "何も編集していないのに手が増えました");
        }

        /// <summary>いまの入力を取れない (窓の段の仕組みを使っていない) 履歴は、以前と同じに動く。</summary>
        [TestMethod]
        public void WithoutALiveStateProvider_RedoBehavesAsBefore()
        {
            var m = new UndoManager { StateFingerprint = UndoCapture.Fingerprint };
            m.SaveState(new Box { Value = 1 });
            m.SaveState(new Box { Value = 2 });
            m.Undo();
            m.Redo();
            Assert.AreEqual(2, ((Box)m.CurrentState!).Value);
        }

        /// <summary>窓の段の仕組みは、ViewModel の控えの取り方を履歴に渡す (渡し忘れるとこの対策が効かない)。</summary>
        [TestMethod]
        public void TheWindowLevelCapture_HandsTheLiveStateToTheHistory()
        {
            string src = TestSource.Read("Graphics_r1", "Common", "UndoCapture.cs");
            StringAssert.Contains(src, "LiveStateProvider ??= host.CaptureUndoSnapshot",
                "UndoCapture.Take が、いまの入力の取り方を履歴に渡していません");
        }

        /// <summary>
        /// 実際の ViewModel で: 戻した状態を画面へ当てたあとの入力が、控えと同じ指紋になること
        /// (ならないと、何も編集していないのにやり直しが効かなくなる)。そのうえで、戻したあとの編集が残ること。
        /// </summary>
        private static void RunOnRealHost(ISnapshotUndoHost host, Action editC, Action editD, Func<string> read, Action undo, Action redo)
        {
            var m = host.UndoManager;
            m.StateFingerprint = UndoCapture.Fingerprint;
            m.LiveStateProvider = host.CaptureUndoSnapshot;

            m.SaveState(host.CaptureUndoSnapshot());
            string b = read();
            editC();
            m.SaveState(host.CaptureUndoSnapshot());
            string c = read();
            Assert.AreNotEqual(b, c, "前提: 編集で値が変わる");

            undo();
            Assert.AreEqual(b, read(), "Ctrl+Z で戻りません");
            redo();
            Assert.AreEqual(c, read(), "何も編集していないのに、やり直しが効きません (戻した後の入力が控えと違う指紋になっています)");

            undo();
            m.SaveState(host.CaptureUndoSnapshot());   // 選ぶ直前の控え (戻した状態と同じなので積まれない)
            Assert.IsTrue(m.CanRedo, "前提: 戻したあとにフォーカスを移しただけでは、やり直せる手は残る");
            editD();
            string d = read();
            Assert.AreNotEqual(b, d, "前提: 2 度目の編集で値が変わる");
            redo();
            Assert.AreEqual(d, read(), "戻したあとの編集が、やり直しで古い手に上書きされました");
            undo();
            Assert.AreEqual(b, read(), "戻したあとの編集を Ctrl+Z で戻せません");
        }

        [TestMethod]
        public void PileSectionWindow_RedoDoesNotOverwriteAnEditMadeAfterUndo()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }
            var section = model.PileBodies?.FirstOrDefault()?.PileBodySegments?.FirstOrDefault()?.PileSection;
            Assert.IsNotNull(section, "前提: 断面がある");
            var vm = new PileSectionViewModel(new MainWindowViewModel { CurrentInputModel = model }, section!, 1, 1);
            double dr = vm.PileSection.MainBarDr;
            RunOnRealHost(vm,
                editC: () => vm.PileSection.MainBarDr = dr - 50,
                editD: () => vm.PileSection.MainBarDr = dr - 80,
                read: () => vm.PileSection.MainBarDr.ToString("R"),
                undo: () => vm.UndoCommand.Execute(null),
                redo: () => vm.RedoCommand.Execute(null));
        }

        [TestMethod]
        public void LoadCaseWindow_RedoDoesNotOverwriteAnEditMadeAfterUndo()
        {
            var mainVm = new MainWindowViewModel();
            var vm = new LoadCaseViewModel(mainVm);
            double before = vm.LoadCasesLevel1.First().UpperMassForce;
            RunOnRealHost(vm,
                editC: () => vm.LoadCasesLevel1.First().UpperMassForce = before + 111,
                editD: () => vm.LoadCasesLevel1.First().UpperMassForce = before + 222,
                read: () => vm.LoadCasesLevel1.First().UpperMassForce.ToString("R"),
                undo: () => vm.UndoCommand.Execute(null),
                redo: () => vm.RedoCommand.Execute(null));
        }
    }
}
