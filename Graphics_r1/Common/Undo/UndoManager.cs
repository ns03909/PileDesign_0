using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PileDesign.Common.Undo;

public sealed class UndoManager
{
    private readonly Stack<IUndoAction> _undo = new();
    private readonly Stack<IUndoAction> _redo = new();

    private CompositeUndoAction? _scope;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void BeginScope(string? description = null)
    {
        if (_scope != null)
        {
            Debug.WriteLine($"[Undo] BeginScope ignored (already open): {_scope.Description}");
            throw new InvalidOperationException("Scope already started.");
        }
        _scope = new CompositeUndoAction(description);
        Debug.WriteLine($"[Undo] BeginScope: {description}");
    }

    public void EndScope()
    {
        if (_scope == null)
        {
            Debug.WriteLine("[Undo] EndScope ignored (no scope)");
            return;
        }
        Debug.WriteLine($"[Undo] EndScope: {_scope.Description}");
        PushCore(_scope);
        _scope = null;
    }

    public void Push(IUndoAction action)
    {
        if (_scope != null)
        {
            Debug.WriteLine($"[Undo] Push (scoped): {action.Description}");
            _scope.Add(action);
            return;
        }
        Debug.WriteLine($"[Undo] Push: {action.Description}");
        PushCore(action);
    }

    private void PushCore(IUndoAction action)
    {
        _undo.Push(action);
        _redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var a = _undo.Pop();
        Debug.WriteLine($"[Undo] Undo: {a.Description}");
        a.Undo();
        _redo.Push(a);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var a = _redo.Pop();
        Debug.WriteLine($"[Undo] Redo: {a.Description}");
        a.Redo();
        _undo.Push(a);
    }
}

// 手軽に使える共有インスタンス
public static class UndoService
{
    public static readonly UndoManager Instance = new();
}