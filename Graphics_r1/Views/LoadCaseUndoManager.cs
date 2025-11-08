using System;
using System.Collections.Generic;

namespace PileDesign.Views
{
    /// <summary>
    /// LoadCase専用のUndo/Redo管理クラス
    /// do/undoペアで履歴を管理し、Redo時はdoActionを再実行します。
    /// </summary>
    public class LoadCaseUndoManager
    {
        private readonly Stack<(Action doAction, Action undoAction)> _undoStack = new();
        private readonly Stack<(Action doAction, Action undoAction)> _redoStack = new();

        /// <summary>
        /// 操作を実行し、Undo/Redo履歴に登録します。
        /// </summary>
        /// <param name="doAction">実行する操作</param>
        /// <param name="undoAction">元に戻す操作</param>
        public void Execute(Action doAction, Action undoAction)
        {
            doAction();
            _undoStack.Push((doAction, undoAction));
            _redoStack.Clear();
        }

        /// <summary>
        /// Undo操作を実行します。
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var (doAction, undoAction) = _undoStack.Pop();
                undoAction();
                _redoStack.Push((doAction, undoAction));
            }
        }

        /// <summary>
        /// Redo操作を実行します。
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var (doAction, undoAction) = _redoStack.Pop();
                doAction();
                _undoStack.Push((doAction, undoAction));
            }
        }

        /// <summary>
        /// Undo可能かどうか
        /// </summary>
        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>
        /// Redo可能かどうか
        /// </summary>
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 履歴をすべてクリアします。
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
