using System.Collections.Generic;

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
            redoStack.Clear();
        }

        public void Undo()
        {
            if (undoStack.Count > 1)
            {
                redoStack.Push(undoStack.Pop());
                CurrentState = undoStack.Peek();
            }
        }

        public void Redo()
        {
            if (redoStack.Count > 0)
            {
                var state = redoStack.Pop();
                undoStack.Push(state);
                CurrentState = state;
            }
        }
    }
}
