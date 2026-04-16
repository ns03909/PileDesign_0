using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PileDesign.Common.Undo;

public sealed class UndoManager
{
    private readonly Stack<IUndoAction> _undo = new();
    private readonly Stack<IUndoAction> _redo = new();

    private CompositeUndoAction? _scope;

    // スナップショット方式用の履歴管理
    private readonly List<object> _history = new();
    private int _currentIndex = -1;
    private int _maxHistory = 50;

    public bool CanUndo => _undo.Count > 0 || _currentIndex > 0;
    public bool CanRedo => _redo.Count > 0 || (_currentIndex >= 0 && _currentIndex < _history.Count - 1);

    /// <summary>
    /// 履歴の最大保持数を設定します（スナップショット方式用）
    /// </summary>
    public int MaxHistory
    {
        get => _maxHistory;
        set
        {
            _maxHistory = value;
            TrimHistory();
        }
    }

    /// <summary>
    /// 現在の状態を取得します（スナップショット方式用）
    /// </summary>
    public object? CurrentState => _currentIndex >= 0 && _currentIndex < _history.Count ? _history[_currentIndex] : null;

    public void BeginScope(string? description = null)
    {
        if (_scope != null)
        {
            throw new InvalidOperationException("Scope already started.");
        }
        _scope = new CompositeUndoAction(description);
    }

    public void EndScope()
    {
        if (_scope == null)
        {
            return;
        }
        PushCore(_scope);
        _scope = null;
    }

    public void Push(IUndoAction action)
    {
        if (_scope != null)
        {
            _scope.Add(action);
            return;
        }
        PushCore(action);
    }

    private void PushCore(IUndoAction action)
    {
        _undo.Push(action);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var a = _undo.Pop();
        a.Undo();
        _redo.Push(a);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var a = _redo.Pop();
        a.Redo();
        _undo.Push(a);
    }

    /// <summary>
    /// Undo/Redoスタックをクリアします
    /// </summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _scope = null;
    }

    /// <summary>
    /// Action/Actionペアを使用してUndoアクションを登録します
    /// </summary>
    public void PushAction(Action undoAction, Action redoAction, string? description = null)
    {
        Push(new ActionUndoAction(undoAction, redoAction, description));
    }

    /// <summary>
    /// 現在のスタックサイズを取得します
    /// </summary>
    public int UndoCount => _undo.Count;

    /// <summary>
    /// Redoスタックサイズを取得します
    /// </summary>
    public int RedoCount => _redo.Count;

    /// <summary>
    /// 次にUndoされるアクションの説明を取得します
    /// </summary>
    public string? PeekUndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    /// <summary>
    /// 次にRedoされるアクションの説明を取得します
    /// </summary>
    public string? PeekRedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    // スナップショット方式のメソッド群

    /// <summary>
    /// 状態のスナップショットを保存します（スナップショット方式用）
    /// </summary>
    public void SaveState(object state)
    {
        if (state == null) return;

        // 現在位置より後ろの履歴を削除
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
        }

        _history.Add(state);
        _currentIndex = _history.Count - 1;

        TrimHistory();
    }

    /// <summary>
    /// 状態のスナップショットを保存します（SaveStateのエイリアス、互換性用）
    /// </summary>
    public void PushState(object state) => SaveState(state);

    /// <summary>
    /// 履歴をトリミングして最大数を維持します
    /// </summary>
    private void TrimHistory()
    {
        if (_history.Count > _maxHistory)
        {
            int removeCount = _history.Count - _maxHistory;
            _history.RemoveRange(0, removeCount);
            _currentIndex -= removeCount;
            if (_currentIndex < 0) _currentIndex = 0;
        }
    }

    /// <summary>
    /// Undoを実行します（スナップショット方式優先、アクション方式も統合）
    /// </summary>
    public void UndoSnapshot()
    {
        // スナップショット方式の履歴がある場合
        if (_currentIndex > 0)
        {
            _currentIndex--;
            return;
        }

        // アクション方式のUndoがある場合
        if (_undo.Count > 0)
        {
            var a = _undo.Pop();
            a.Undo();
            _redo.Push(a);
        }
    }

    /// <summary>
    /// Redoを実行します（スナップショット方式優先、アクション方式も統合）
    /// </summary>
    public void RedoSnapshot()
    {
        // スナップショット方式の履歴がある場合
        if (_currentIndex >= 0 && _currentIndex < _history.Count - 1)
        {
            _currentIndex++;
            return;
        }

        // アクション方式のRedoがある場合
        if (_redo.Count > 0)
        {
            var a = _redo.Pop();
            a.Redo();
            _undo.Push(a);
        }
    }
}

// 手軽に使える共有インスタンス
public static class UndoService
{
    public static readonly UndoManager Instance = new();
}