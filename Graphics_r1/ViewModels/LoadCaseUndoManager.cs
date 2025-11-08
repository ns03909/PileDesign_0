using System;
using System.Collections.Generic;

namespace PileDesign.ViewModels
{
    public class LoadCaseUndoManager
    {
        private readonly Stack<Action> _undoStack = new();
        private readonly Stack<Action> _redoStack = new();

        public void Execute(Action doAction, Action undoAction)
        {
            doAction();
            _undoStack.Push(undoAction);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var undoAction = _undoStack.Pop();
                undoAction();
                _redoStack.Push(undoAction);
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var redoAction = _redoStack.Pop();
                redoAction();
                _undoStack.Push(redoAction);
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
    }
}
