using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PileDesign.Common.Undo;

/// <summary>
/// スナップショット履歴の 1 件。状態 + 説明 + タイムスタンプを保持。
/// HistoryPanel (D.16) の表示元。
///
/// <para><paramref name="Seq"/> は<b>何番目の手か</b>。控え (スナップショット) とアクションを
/// 1 本の時系列に並べて戻すために使う (<see cref="UndoManager.Undo"/>)。</para>
/// </summary>
public sealed record HistoryEntry(object State, string? Description, DateTime Timestamp, long Seq = 0);

public sealed class UndoManager
{
    /// <summary>積んだアクションと、それが何番目の手かの組。</summary>
    private readonly record struct Pushed(IUndoAction Action, long Seq);

    private readonly Stack<Pushed> _undo = new();
    private readonly Stack<Pushed> _redo = new();

    // 何番目の手か。控えとアクションで共通に振る (時系列で戻すため)
    private long _seq;

    private CompositeUndoAction? _scope;

    // スナップショット方式用の履歴管理
    private readonly List<HistoryEntry> _history = new();
    private int _currentIndex = -1;
    private int _maxHistory = 50;

    /// <summary>履歴 / Undo スタックが変化したときに発火 (D.16 HistoryPanel が購読)。</summary>
    public event Action? HistoryChanged;

    private void RaiseHistoryChanged() => HistoryChanged?.Invoke();

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
    public object? CurrentState => _currentIndex >= 0 && _currentIndex < _history.Count ? _history[_currentIndex].State : null;

    /// <summary>履歴一覧 (古い → 新しい)。HistoryPanel 表示用。</summary>
    public IReadOnlyList<HistoryEntry> History => _history;

    /// <summary>現在位置のインデックス (0 始まり、未保存時 -1)。</summary>
    public int CurrentIndex => _currentIndex;

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
        // 何も積まれなかったスコープは捨てる。積むと Ctrl+Z が 1 回、
        // 見た目に何も起きないまま消費される。
        if (_scope.Count > 0) PushCore(_scope);
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
        _undo.Push(new Pushed(action, ++_seq));
        _redo.Clear();
        RaiseHistoryChanged();
    }

    /// <summary>
    /// 1 手戻す。<b>入口はこれ 1 つ</b>。
    ///
    /// <para>この履歴には 2 種類の手が入る。入力を丸ごと控える「控え」(<see cref="SaveState"/>) と、
    /// 戻し方・やり直し方を組で持つ「アクション」(<see cref="PushAction"/>) で、
    /// 画面によってどちらを使うかが違う。両方が入っている場合は<b>新しい手から順に</b>戻す
    /// (連番 <c>Seq</c> で比べる)。以前は控えを全部消費してからアクションに移る実装だったため、
    /// 混ざると戻る順が入れ替わった。</para>
    ///
    /// <para>控えを 1 手戻したときは、<see cref="CurrentState"/> が戻った先の状態になる。
    /// <b>呼び出し側がそれを画面と入力へ再適用すること</b> (控えは状態そのものなので、
    /// ここでは持ち主のモデルに触れない)。アクションの場合はここで戻し終わっている。</para>
    /// </summary>
    public void Undo()
    {
        bool hasSnapshot = _currentIndex > 0;
        bool hasAction = _undo.Count > 0;
        if (!hasSnapshot && !hasAction) return;

        // 新しい手から戻す。控えの「1 手」は _history[_currentIndex] へ進んだ手なので、
        // その手の新しさはその控えの連番で判断する
        if (hasSnapshot && (!hasAction || _history[_currentIndex].Seq > _undo.Peek().Seq))
        {
            _currentIndex--;
            RaiseHistoryChanged();
            return;
        }

        var a = _undo.Pop();
        a.Action.Undo();
        _redo.Push(a);
        RaiseHistoryChanged();
    }

    /// <summary>
    /// 1 手やり直す。戻したのと逆順に、<b>古い手から</b>やり直す。
    /// 控えをやり直したときは <see cref="CurrentState"/> の再適用が呼び出し側の責任。
    /// </summary>
    public void Redo()
    {
        bool hasSnapshot = _currentIndex >= 0 && _currentIndex < _history.Count - 1;
        bool hasAction = _redo.Count > 0;
        if (!hasSnapshot && !hasAction) return;

        if (hasSnapshot && (!hasAction || _history[_currentIndex + 1].Seq < _redo.Peek().Seq))
        {
            _currentIndex++;
            RaiseHistoryChanged();
            return;
        }

        var a = _redo.Pop();
        a.Action.Redo();
        _undo.Push(a);
        RaiseHistoryChanged();
    }

    /// <summary>
    /// Undo/Redoスタックをクリアします
    /// </summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _scope = null;
        _history.Clear();
        _currentIndex = -1;
        RaiseHistoryChanged();
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
    public string? PeekUndoDescription => _undo.Count > 0 ? _undo.Peek().Action.Description : null;

    /// <summary>
    /// 次にRedoされるアクションの説明を取得します
    /// </summary>
    public string? PeekRedoDescription => _redo.Count > 0 ? _redo.Peek().Action.Description : null;

    // スナップショット方式のメソッド群

    /// <summary>
    /// 状態のスナップショットを保存します（スナップショット方式用）。
    /// description を渡すと HistoryPanel で識別しやすい説明文として表示される。
    /// </summary>
    public void SaveState(object state, string? description = null)
    {
        if (state == null) return;

        // 現在位置より後ろの履歴を削除
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
        }

        _history.Add(new HistoryEntry(state, description, DateTime.Now, ++_seq));
        _currentIndex = _history.Count - 1;

        TrimHistory();
        RaiseHistoryChanged();
    }

    /// <summary>
    /// 状態のスナップショットを保存します（SaveStateのエイリアス、互換性用）
    /// </summary>
    public void PushState(object state) => SaveState(state, null);

    /// <summary>
    /// 履歴の特定インデックスへジャンプします (D.16 HistoryPanel から呼ばれる)。
    /// _currentIndex のみ更新するので、呼び出し側は CurrentState を参照して
    /// アプリ側のモデルを再適用してください。
    /// </summary>
    public void JumpToIndex(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _history.Count) return;
        if (targetIndex == _currentIndex) return;
        _currentIndex = targetIndex;
        RaiseHistoryChanged();
    }

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
    /// <see cref="Undo"/> の別名。控えを使う画面が呼んでいる。
    /// <b>中身は同じ</b>なので、どちらを呼んでも時系列で 1 手戻る。
    /// 名前が 2 つあるのは呼び出し側の都合で、方式が 2 つあるという意味ではない。
    /// </summary>
    public void UndoSnapshot() => Undo();

    /// <summary><see cref="Redo"/> の別名。</summary>
    public void RedoSnapshot() => Redo();
}

// 手の種類は 2 つ (控え / アクション) だが、履歴は 1 本で、戻す入口も Undo / Redo の 1 組だけ。
// 控えは「状態そのもの」なので戻したあとの再適用は持ち主の ViewModel が行い、
// アクションは自分で戻し方を知っているのでここで戻し終わる。この違いは残るが、
// 「どちらの入口を呼ぶか」を呼び出し側が選ぶ必要はない (2026-09-12 に 1 本化)。
//
// プロセス全体で共有する静的インスタンス (UndoService) は置かない。
// 「手軽に使える」ため複数のウィンドウが同じスタックに積み、
// 消費するのは 1 つのウィンドウだけ、という状態になっていた
// (メイン画面のセル編集が ChangWindow の Ctrl+Z で巻き戻る)。
// Undo スタックは、それを消費する ViewModel が 1 本ずつ持つこと。