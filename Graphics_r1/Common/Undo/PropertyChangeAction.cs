using System;
using System.Collections.ObjectModel;
using System.Reflection;

namespace PileDesign.Common.Undo;

public sealed class PropertyChangeAction<T> : IUndoAction
{
    private readonly object _target;
    private readonly PropertyInfo _prop;
    private readonly T? _oldValue;
    private readonly T? _newValue;

    public string? Description { get; }

    public PropertyChangeAction(object target, string propertyName, T? oldValue, T? newValue, string? description = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new ArgumentException($"Property '{propertyName}' not found on {target.GetType().Name}");
        _oldValue = oldValue;
        _newValue = newValue;
        Description = description;
    }

    public void Undo() => _prop.SetValue(_target, _oldValue);
    public void Redo() => _prop.SetValue(_target, _newValue);
}

public sealed class CollectionChangeAction<T> : IUndoAction
{
    private readonly ObservableCollection<T> _collection;
    private readonly T _item;
    private readonly int _index;
    private readonly bool _wasAdd;

    public string? Description { get; }

    private CollectionChangeAction(ObservableCollection<T> collection, T item, int index, bool wasAdd, string? description)
    {
        _collection = collection;
        _item = item;
        _index = index;
        _wasAdd = wasAdd;
        Description = description;
    }

    public static CollectionChangeAction<T> ForAdd(ObservableCollection<T> collection, T item, int index, string? description = null)
        => new(collection, item, index, wasAdd: true, description);

    public static CollectionChangeAction<T> ForRemove(ObservableCollection<T> collection, T item, int index, string? description = null)
        => new(collection, item, index, wasAdd: false, description);

    public void Undo()
    {
        if (_wasAdd)
        {
            if (_index >= 0 && _index <= _collection.Count) _collection.RemoveAt(_index);
            else _collection.Remove(_item);
        }
        else
        {
            if (_index < 0 || _index > _collection.Count) _collection.Add(_item);
            else _collection.Insert(_index, _item);
        }
    }

    public void Redo()
    {
        if (_wasAdd)
        {
            if (_index < 0 || _index > _collection.Count) _collection.Add(_item);
            else _collection.Insert(_index, _item);
        }
        else
        {
            if (_index >= 0 && _index < _collection.Count) _collection.RemoveAt(_index);
            else _collection.Remove(_item);
        }
    }
}

public sealed class CompositeUndoAction : IUndoAction
{
    private readonly System.Collections.Generic.List<IUndoAction> _actions = new();
    public string? Description { get; }

    public CompositeUndoAction(string? description = null) { Description = description; }

    public void Add(IUndoAction action) => _actions.Add(action);

    public void Undo()
    {
        for (int i = _actions.Count - 1; i >= 0; i--) _actions[i].Undo();
    }

    public void Redo()
    {
        for (int i = 0; i < _actions.Count; i++) _actions[i].Redo();
    }
}