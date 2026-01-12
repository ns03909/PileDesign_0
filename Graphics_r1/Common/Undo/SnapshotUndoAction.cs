using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Common.Undo
{
    public sealed class SnapshotUndoAction<T> : IUndoAction
    {
        private readonly List<T> _oldSnapshot;
        private readonly List<T> _newSnapshot;
        private readonly Action<IEnumerable<T>> _apply;

        public string Description { get; }

        public SnapshotUndoAction(string description, IEnumerable<T> oldSnapshot, IEnumerable<T> newSnapshot, Action<IEnumerable<T>> apply)
        {
            Description = description ?? string.Empty;
            _oldSnapshot = oldSnapshot != null ? new List<T>(oldSnapshot) : new List<T>();
            _newSnapshot = newSnapshot != null ? new List<T>(newSnapshot) : new List<T>();
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public void Undo() => _apply(_oldSnapshot);
        public void Redo() => _apply(_newSnapshot);
    }
}