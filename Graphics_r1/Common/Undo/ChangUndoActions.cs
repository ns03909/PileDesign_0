using System;
using System.Collections.Generic;
using System.Reflection;

namespace PileDesign.Common.Undo
{
    // 単一プロパティの変更を Undo/Redo 可能にするアクション
    public sealed class PropertyChangeAction : IUndoAction
    {
        private readonly object _target;
        private readonly PropertyInfo _pi;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public PropertyChangeAction(object target, string propertyName, object? oldValue, object? newValue)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _pi = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                  ?? throw new ArgumentException($"Property '{propertyName}' not found on {_target.GetType().FullName}");
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public string? Description => $"Set {_pi.Name}";

        public void Undo()
        {
            SetValue(_oldValue);
        }

        public void Redo()
        {
            SetValue(_newValue);
        }

        private void SetValue(object? v)
        {
            try
            {
                if (v == null)
                {
                    _pi.SetValue(_target, null);
                    return;
                }
                if (_pi.PropertyType.IsAssignableFrom(v.GetType()))
                    _pi.SetValue(_target, v);
                else
                    _pi.SetValue(_target, Convert.ChangeType(v, _pi.PropertyType));
            }
            catch
            {
                // 変換失敗は無視（安全側）
            }
        }
    }

    // コレクション追加アクション（IList を扱う）
    public sealed class CollectionAddAction<T> : IUndoAction
    {
        private readonly IList<T> _collection;
        private readonly T _item;
        private int _index = -1;

        public CollectionAddAction(IList<T> collection, T item)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _item = item!;
        }

        public string? Description => $"Add {typeof(T).Name}";

        public void Undo()
        {
            if (_index >= 0 && _index < _collection.Count && ReferenceEquals(_collection[_index], _item))
                _collection.RemoveAt(_index);
            else
                _collection.Remove(_item);
        }

        public void Redo()
        {
            _collection.Add(_item);
            _index = _collection.IndexOf(_item);
        }
    }

    // コレクション削除アクション（削除時に元の位置を保存して復元）
    public sealed class CollectionRemoveAction<T> : IUndoAction
    {
        private readonly IList<T> _collection;
        private readonly T _item;
        private int _index = -1;

        public CollectionRemoveAction(IList<T> collection, T item)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _item = item!;
        }

        public string? Description => $"Remove {typeof(T).Name}";

        public void Undo()
        {
            if (_index >= 0 && _index <= _collection.Count)
                _collection.Insert(_index, _item);
            else
                _collection.Add(_item);
        }

        public void Redo()
        {
            _index = _collection.IndexOf(_item);
            if (_index >= 0) _collection.RemoveAt(_index);
        }
    }
}