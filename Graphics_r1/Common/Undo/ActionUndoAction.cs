using System;

namespace PileDesign.Common.Undo
{
    /// <summary>
    /// Action/Actionペアを使用した単純なUndoアクション
    /// </summary>
    public sealed class ActionUndoAction : IUndoAction
    {
        private readonly Action _undoAction;
        private readonly Action _redoAction;

        public string? Description { get; }

        public ActionUndoAction(Action undoAction, Action redoAction, string? description = null)
        {
            _undoAction = undoAction ?? throw new ArgumentNullException(nameof(undoAction));
            _redoAction = redoAction ?? throw new ArgumentNullException(nameof(redoAction));
            Description = description;
        }

        public void Undo() => _undoAction();
        public void Redo() => _redoAction();
    }
}
