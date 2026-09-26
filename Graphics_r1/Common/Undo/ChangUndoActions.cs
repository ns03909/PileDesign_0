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

        /// <summary>
        /// 値を入れる。変換や設定に失敗したら、どの項目・どの値かを添えて例外を伝える。
        ///
        /// 以前は失敗を握りつぶしていた。<see cref="UndoManager"/> は成功したものとして履歴の位置を進めるので、
        /// 画面の値は戻っていないのに履歴だけが戻り、値と履歴が食い違った。いまは <see cref="UndoManager"/> が
        /// 失敗を受けて履歴の位置を動かさず、利用者に知らせる。
        /// </summary>
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
                    _pi.SetValue(_target, Convert.ChangeType(v, Nullable.GetUnderlyingType(_pi.PropertyType) ?? _pi.PropertyType,
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
                throw new InvalidOperationException($"{_pi.Name} に「{v}」を入れられませんでした ({inner.Message})", inner);
            }
        }
    }

    // コレクション追加アクション（IList を扱う）
    public sealed class CollectionAddAction<T>(IList<T> collection, T item) : IUndoAction
    {
        private readonly IList<T> _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        private readonly T _item = item!;
        private int _index = -1;

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
    public sealed class CollectionRemoveAction<T>(IList<T> collection, T item) : IUndoAction
    {
        private readonly IList<T> _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        private readonly T _item = item!;
        private int _index = -1;

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