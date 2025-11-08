using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.ViewModels
{
    public class GroundUndoManager
    {
        private readonly Stack<List<GroundInput>> _undoStack = new();
        private readonly Stack<List<GroundInput>> _redoStack = new();

        // 履歴を追加
        //public void PushState(List<GroundInput> state)
        //{
        //    var deepCopy = state.Select(x => x.DeepCopy()).ToList();
        //    _undoStack.Push(deepCopy);
        //    _redoStack.Clear(); // 新しい操作が入ったらRedo履歴はクリア
        //}

        // Undo操作
        //public List<GroundInput>? Undo()
        //{
        //    if (_undoStack.Count > 0)
        //    {
        //        var currentState = _undoStack.Pop();
        //        _redoStack.Push(currentState);
        //        if (_undoStack.Count > 0)
        //        {
        //            // Undo後の状態を返す
        //            return _undoStack.Peek().Select(x => x.DeepCopy()).ToList();
        //        }
        //        else
        //        {
        //            // 履歴が空なら元の状態を返す
        //            return currentState.Select(x => x.DeepCopy()).ToList();
        //        }
        //    }
        //    return null;
        //}

        //// Redo操作
        //public List<GroundInput>? Redo()
        //{
        //    if (_redoStack.Count > 0)
        //    {
        //        var redoState = _redoStack.Pop();
        //        _undoStack.Push(redoState);
        //        return redoState.Select(x => x.DeepCopy()).ToList();
        //    }
        //    return null;


        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public List<GroundInput>? CurrentState { get; private set; }

        public void PushState(List<GroundInput> state)
        {
            var deepCopy = state.Select(x => x.DeepCopy()).ToList();
            _undoStack.Push(deepCopy);
            _redoStack.Clear();
            CurrentState = deepCopy;
        }

        public List<GroundInput>? Undo()
        {
            if (_undoStack.Count > 1)
            {
                var currentState = _undoStack.Pop();
                _redoStack.Push(currentState);
                CurrentState = [.. _undoStack.Peek().Select(x => x.DeepCopy())];
                return CurrentState;
            }
            return null;
        }

        public List<GroundInput>? Redo()
        {
            if (_redoStack.Count > 0)
            {
                var redoState = _redoStack.Pop();
                _undoStack.Push(redoState);
                CurrentState = [.. redoState.Select(x => x.DeepCopy())];
                return CurrentState;
            }
            return null;
        }
    }
}