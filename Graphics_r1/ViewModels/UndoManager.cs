using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PileDesign.ViewModels
{
    public class UndoManager
    {
        private readonly Stack<object> undoStack = new();
        private readonly Stack<object> redoStack = new();
        public object CurrentState { get; private set; }

        public void SaveState(object state)
        {
            undoStack.Push(state);
            CurrentState = state;
            // 新しい変更が入ると redo は無効化
            redoStack.Clear();
            Debug.WriteLine("UndoStack: " + string.Join(",", undoStack.Select(s => (s as InputModel)?.PileLayoutItems.Count ?? -1)));
        }

        /// <summary>
        /// SaveState のエイリアス（互換性のため）
        /// </summary>
        public void PushState(object state) => SaveState(state);

        //public void Undo()
        //{
        //    if (undoStack.Count > 1)
        //    {
        //        redoStack.Push(undoStack.Pop());
        //        CurrentState = undoStack.Peek();
        //        if (CurrentState is InputModel model)
        //            Debug.WriteLine("Undo後の本数: " + model.PileLayoutItems.Count);
        //        Debug.WriteLine("UndoStack IDs: " + string.Join(",", undoStack.Select(s => s.GetHashCode())));

        //    }
        //}

        //public void Redo()
        //{
        //    if (redoStack.Count > 0)
        //    {
        //        var state = redoStack.Pop();
        //        undoStack.Push(state);
        //        CurrentState = state;
        //        Debug.WriteLine("UndoStack: " + string.Join(",", undoStack.Select(s => (s as InputModel)?.PileLayoutItems.Count ?? -1)));

        //    }
        //}
        // Undo: 現在のトップを redo に送り、undo の次点を CurrentState にする
        public void Undo()
        {
            // 1つ以上の状態があり、元に戻せる履歴があること（少なくとも 2 要素）が必要
            if (undoStack.Count > 1)
            {
                // 現在トップ（最新）を redo に移動
                var top = undoStack.Pop();
                redoStack.Push(top);

                // undo の次点（新しいトップ）を CurrentState とする
                CurrentState = undoStack.Peek();
            }
        }

        // Redo: redo スタックから取り出して undo スタックに戻す
        public void Redo()
        {
            if (redoStack.Count > 0)
            {
                var state = redoStack.Pop();
                undoStack.Push(state);
                CurrentState = state;
            }
        }

        //public void Undo()
        //{
        //    if (undoStack.Count > 0)
        //    {
        //        redoStack.Push(CurrentState); // 現在の状態をRedoStackに積む
        //        CurrentState = undoStack.Pop();
        //    }
        //}

        //public void Redo()
        //{
        //    if (redoStack.Count > 0)
        //    {
        //        undoStack.Push(CurrentState); // 現在の状態をUndoStackに積む
        //        CurrentState = redoStack.Pop();
        //    }
        //}
    }
}

//using System.Collections.Generic;

//namespace PileDesign.ViewModels
//{
//    public class UndoManager
//    {
//        private readonly Stack<object> undoStack = new();
//        private readonly Stack<object> redoStack = new();
//        public object CurrentState { get; private set; }

//        public void SaveState(object state)
//        {
//            // 現在の状態をundoスタックに追加（CurrentStateは変更しない）
//            undoStack.Push(state);
//            redoStack.Clear();
//        }

//        public void Undo()
//        {
//            if (undoStack.Count > 0)
//            {
//                CurrentState = undoStack.Pop();
//                // 復元した状態をredoスタックにも追加（Redo用）
//                redoStack.Push(CurrentState);
//            }
//        }

//        public void Redo()
//        {
//            if (redoStack.Count > 0)
//            {
//                var state = redoStack.Pop();
//                undoStack.Push(state);
//                CurrentState = state;
//            }
//        }
//    }
//}