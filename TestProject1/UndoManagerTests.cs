using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// <c>Common/Undo/</c> の実行テスト。
    ///
    /// ここは 1,000 行あって、これまで反射での存在確認しかありませんでした。
    /// Undo は「押しても何も起きない」「1 つ前ではなく 2 つ前に戻る」のように
    /// <b>例外にならないまま静かに間違える</b>種類の壊れ方をします。過去にも
    /// プロセス全体で共有する静的スタックのせいで、別ウィンドウの Ctrl+Z が
    /// メイン画面のセル編集を巻き戻す、という形で実際に起きています。
    ///
    /// 呼び出し側の作法は <c>ChangViewModel</c> に倣います。
    /// <b>アクションを作ったら先に <c>Redo()</c> で実行し、それから積む</b>
    /// (<c>CollectionRemoveAction</c> は <c>Redo()</c> の中で元の位置を控えるので、
    /// 実行せずに積むと Undo が末尾に戻します)。
    /// </summary>
    [TestClass]
    public class UndoManagerTests
    {
        private sealed class Box
        {
            public double Value { get; set; }
            public string? Name { get; set; }
        }

        private static void PushSet(UndoManager m, Box box, double from, double to)
        {
            box.Value = to;
            m.PushAction(() => box.Value = from, () => box.Value = to, $"{from}→{to}");
        }

        // ---- アクション方式 ----

        [TestMethod]
        public void Undo_ThenRedo_ReturnsTheValue()
        {
            var m = new UndoManager();
            var box = new Box { Value = 1 };

            PushSet(m, box, 1, 2);

            Assert.IsTrue(m.CanUndo);
            Assert.IsFalse(m.CanRedo);

            m.Undo();
            Assert.AreEqual(1, box.Value, "Undo で元の値に戻っていません");
            Assert.IsTrue(m.CanRedo);

            m.Redo();
            Assert.AreEqual(2, box.Value, "Redo で戻していません");
        }

        [TestMethod]
        public void Undo_WalksBackOneStepAtATime()
        {
            var m = new UndoManager();
            var box = new Box { Value = 0 };

            PushSet(m, box, 0, 1);
            PushSet(m, box, 1, 2);
            PushSet(m, box, 2, 3);

            m.Undo();
            Assert.AreEqual(2, box.Value, "1 つ前ではないところに戻っています");
            m.Undo();
            Assert.AreEqual(1, box.Value);
            m.Undo();
            Assert.AreEqual(0, box.Value);

            Assert.IsFalse(m.CanUndo);
            m.Undo();   // 空で呼んでも例外にしない
            Assert.AreEqual(0, box.Value);
        }

        [TestMethod]
        public void APushAfterUndo_ThrowsAwayTheRedoBranch()
        {
            var m = new UndoManager();
            var box = new Box { Value = 0 };

            PushSet(m, box, 0, 1);
            m.Undo();
            Assert.IsTrue(m.CanRedo);

            PushSet(m, box, 0, 9);

            Assert.IsFalse(m.CanRedo,
                "Undo した後に別の編集をしたら、やり直しの枝は捨てられるべきです。"
                + "残っていると、Redo が今の値と無関係な値を書き戻します");
            Assert.AreEqual(0, m.RedoCount);
        }

        [TestMethod]
        public void TheDescription_IsWhatThePanelShows()
        {
            var m = new UndoManager();
            var box = new Box();

            Assert.IsNull(m.PeekUndoDescription);
            PushSet(m, box, 0, 1);
            Assert.AreEqual("0→1", m.PeekUndoDescription);

            m.Undo();
            Assert.IsNull(m.PeekUndoDescription);
            Assert.AreEqual("0→1", m.PeekRedoDescription);
        }

        [TestMethod]
        public void Clear_EmptiesBothSides()
        {
            var m = new UndoManager();
            var box = new Box();

            PushSet(m, box, 0, 1);
            PushSet(m, box, 1, 2);
            m.Undo();
            m.SaveState("なにか");

            m.Clear();

            Assert.IsFalse(m.CanUndo);
            Assert.IsFalse(m.CanRedo);
            Assert.AreEqual(0, m.UndoCount);
            Assert.AreEqual(0, m.RedoCount);
            Assert.AreEqual(0, m.History.Count);
            Assert.AreEqual(-1, m.CurrentIndex);
            Assert.IsNull(m.CurrentState);
        }

        [TestMethod]
        public void HistoryChanged_FiresOnEveryThingThePanelWouldShow()
        {
            var m = new UndoManager();
            var box = new Box();
            int fired = 0;
            m.HistoryChanged += () => fired++;

            PushSet(m, box, 0, 1);
            Assert.AreEqual(1, fired, "Push で通知されていません");

            m.Undo();
            Assert.AreEqual(2, fired, "Undo で通知されていません");

            m.Redo();
            Assert.AreEqual(3, fired, "Redo で通知されていません");

            m.SaveState("s");
            Assert.AreEqual(4, fired, "SaveState で通知されていません");

            m.Clear();
            Assert.AreEqual(5, fired, "Clear で通知されていません");
        }

        // ---- まとめて 1 回に見せる (スコープ) ----

        [TestMethod]
        public void AScope_IsUndoneAsOneStep_InReverseOrder()
        {
            var m = new UndoManager();
            var order = new List<string>();

            m.BeginScope("まとめて");
            m.PushAction(() => order.Add("undo A"), () => order.Add("redo A"));
            m.PushAction(() => order.Add("undo B"), () => order.Add("redo B"));
            m.EndScope();

            Assert.AreEqual(1, m.UndoCount, "スコープの中は 1 件にまとまるべきです");
            Assert.AreEqual("まとめて", m.PeekUndoDescription);

            m.Undo();
            CollectionAssert.AreEqual(new[] { "undo B", "undo A" }, order,
                "Undo は後から積んだものから順に戻すべきです。順序が逆だと、"
                + "後の編集が前の編集の結果に依存している場合に壊れます");

            order.Clear();
            m.Redo();
            CollectionAssert.AreEqual(new[] { "redo A", "redo B" }, order,
                "Redo は積んだ順に進めるべきです");
        }

        [TestMethod]
        public void AnEmptyScope_DoesNotLeaveADeadUndoStep()
        {
            var m = new UndoManager();

            m.BeginScope("何も起きなかった");
            m.EndScope();

            Assert.IsFalse(m.CanUndo,
                "中身のないスコープを積むと、Ctrl+Z が 1 回無反応になります");
        }

        [TestMethod]
        public void BeginScope_Twice_Throws()
        {
            var m = new UndoManager();
            m.BeginScope();
            Assert.ThrowsException<InvalidOperationException>(() => m.BeginScope(),
                "入れ子のスコープは畳み方が決まっていないので、黙って握らないこと");
        }

        [TestMethod]
        public void EndScope_WithoutBegin_IsIgnored()
        {
            var m = new UndoManager();
            m.EndScope();
            Assert.IsFalse(m.CanUndo);
        }

        // ---- スナップショット方式 ----

        [TestMethod]
        public void SaveState_MovesTheCurrentPositionToTheEnd()
        {
            var m = new UndoManager();
            m.SaveState("A", "1 つめ");
            m.SaveState("B", "2 つめ");

            Assert.AreEqual(2, m.History.Count);
            Assert.AreEqual(1, m.CurrentIndex);
            Assert.AreEqual("B", m.CurrentState);
            Assert.AreEqual("2 つめ", m.History[1].Description);
        }

        [TestMethod]
        public void UndoSnapshot_MovesTheCursor_WithoutLosingTheFuture()
        {
            var m = new UndoManager();
            m.SaveState("A");
            m.SaveState("B");
            m.SaveState("C");

            m.UndoSnapshot();
            Assert.AreEqual("B", m.CurrentState);
            m.UndoSnapshot();
            Assert.AreEqual("A", m.CurrentState);

            Assert.AreEqual(3, m.History.Count, "巻き戻しただけで履歴を削ってはいけません");

            m.RedoSnapshot();
            Assert.AreEqual("B", m.CurrentState);
        }

        [TestMethod]
        public void SavingAfterUndo_DropsWhatWasAhead()
        {
            var m = new UndoManager();
            m.SaveState("A");
            m.SaveState("B");
            m.SaveState("C");

            m.UndoSnapshot();           // → B
            m.SaveState("B2");

            CollectionAssert.AreEqual(new object[] { "A", "B", "B2" },
                m.History.Select(h => h.State).ToArray(),
                "巻き戻した先で編集したら、その先の履歴は捨てるべきです");
            Assert.AreEqual(2, m.CurrentIndex);
            Assert.IsFalse(m.CanRedo);
        }

        [TestMethod]
        public void MaxHistory_DropsTheOldest_AndKeepsPointingAtTheSameState()
        {
            var m = new UndoManager { MaxHistory = 3 };
            m.SaveState("A");
            m.SaveState("B");
            m.SaveState("C");
            m.SaveState("D");

            CollectionAssert.AreEqual(new object[] { "B", "C", "D" },
                m.History.Select(h => h.State).ToArray(),
                "古いほうから捨てるべきです");
            Assert.AreEqual("D", m.CurrentState,
                "履歴を削ったときに現在位置がずれると、Undo が 1 つ飛ばしになります");
        }

        [TestMethod]
        public void SaveState_IgnoresNull()
        {
            var m = new UndoManager();
            m.SaveState(null!);
            Assert.AreEqual(0, m.History.Count);
        }

        [TestMethod]
        public void JumpToIndex_MovesTheCursor_AndRefusesTheOutOfRange()
        {
            var m = new UndoManager();
            m.SaveState("A");
            m.SaveState("B");
            m.SaveState("C");

            m.JumpToIndex(0);
            Assert.AreEqual("A", m.CurrentState);

            m.JumpToIndex(-1);
            Assert.AreEqual("A", m.CurrentState, "範囲外は無視されるべきです");
            m.JumpToIndex(3);
            Assert.AreEqual("A", m.CurrentState, "範囲外は無視されるべきです");
        }

        // ---- コレクションの追加・削除 ----

        [TestMethod]
        public void DeletingARow_PutsItBackWhereItWas()
        {
            var list = new ObservableCollection<string> { "a", "b", "c" };
            var m = new UndoManager();

            var act = new CollectionRemoveAction<string>(list, "b");
            act.Redo();                 // 呼び出し側の作法: 先に実行してから積む
            m.Push(act);

            CollectionAssert.AreEqual(new[] { "a", "c" }, list.ToArray());

            m.Undo();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, list.ToArray(),
                "削除の Undo は元の位置に戻すべきです。末尾に付くと行の並びが変わります");

            m.Redo();
            CollectionAssert.AreEqual(new[] { "a", "c" }, list.ToArray());
        }

        [TestMethod]
        public void AddingARow_RemovesTheRightOne_EvenIfTheListMovedOn()
        {
            var list = new ObservableCollection<string> { "a", "b" };
            var m = new UndoManager();

            var act = new CollectionAddAction<string>(list, "c");
            act.Redo();
            m.Push(act);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, list.ToArray());

            // 追加のあとで別の行が消えると、控えた添字は当てにならない
            list.Remove("a");

            m.Undo();
            CollectionAssert.AreEqual(new[] { "b" }, list.ToArray(),
                "添字ではなく実体で消すべきです。添字を信じると別の行が消えます");
        }

        [TestMethod]
        public void PropertyChangeAction_RoundTrips()
        {
            var box = new Box { Name = "旧" };
            var m = new UndoManager();

            box.Name = "新";
            m.Push(new PropertyChangeAction(box, nameof(Box.Name), "旧", "新"));

            m.Undo();
            Assert.AreEqual("旧", box.Name);
            m.Redo();
            Assert.AreEqual("新", box.Name);
        }

        [TestMethod]
        public void PropertyChangeAction_ConvertsWhatTheGridHandsBack()
        {
            // DataGrid のセル編集は宣言と違う型で戻ってくることがある
            var box = new Box { Value = 1.0 };
            var m = new UndoManager();

            box.Value = 2.0;
            m.Push(new PropertyChangeAction(box, nameof(Box.Value), 1, 2));

            m.Undo();
            Assert.AreEqual(1.0, box.Value, 0.0, "int → double の変換ができていません");
        }

        [TestMethod]
        public void PropertyChangeAction_RejectsAMissingProperty()
        {
            Assert.ThrowsException<ArgumentException>(
                () => new PropertyChangeAction(new Box(), "存在しない", 1, 2),
                "名前の打ち間違いは、押しても何も起きない Ctrl+Z になる前に落とすこと");
        }

        /// <summary>
        /// プロセス全体で共有する静的な Undo スタックを作らないこと。
        /// 以前これがあったせいで、<c>ChangWindow</c> の Ctrl+Z がメイン画面の
        /// セル編集を巻き戻していました。<c>UndoManager</c> はウィンドウごとに持ちます。
        /// </summary>
        [TestMethod]
        public void ThereIsNoSharedStaticUndoStack()
        {
            var shared = typeof(UndoManager).Assembly.GetTypes()
                .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static
                                             | System.Reflection.BindingFlags.Public
                                             | System.Reflection.BindingFlags.NonPublic))
                .Where(f => typeof(UndoManager).IsAssignableFrom(f.FieldType))
                .Select(f => $"{f.DeclaringType?.FullName}.{f.Name}")
                .ToList();

            Assert.AreEqual(0, shared.Count,
                "static な UndoManager があります。複数のウィンドウが同じスタックに積み、"
                + "別のウィンドウの Ctrl+Z で巻き戻る事故になります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", shared));
        }
    }
}
