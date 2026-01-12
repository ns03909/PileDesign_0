using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PileDesign.Common.Undo;

namespace PileDesign.ViewModels
{
    public class UndoManager
    {
        private readonly List<object> _history = new();
        private int _currentIndex = -1;
        private readonly object _lock = new();

        // 履歴上限 (必要に応じて設定可能) - 1未満は無効とみなして1に補正される
        private int _maxHistory = 20;
        public int MaxHistory
        {
            get => _maxHistory;
            set
            {
                //_maxHistory = value < 1 ? 1 : value;
                _maxHistory = Math.Max(1, value);
                // 履歴上限を下げた場合は直ちにトリム
                TrimToMaxHistory();
            }
        }
        public int Count
        {
            get
            {
                lock (_lock) { return _history.Count; }
            }
        }

        public object? CurrentState
        {
            get
            {
                lock (_lock)
                {
                    if (_currentIndex >= 0 && _currentIndex < _history.Count)
                        return _history[_currentIndex];
                    return null;
                }
            }
        }


        public void SaveState(object state)
        {
            if (state == null) return;
            lock (_lock)
            {
                // 現在位置より後ろの redo 履歴は上書きで破棄
                if (_currentIndex < _history.Count - 1)
                {
                    _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
                }

                _history.Add(state);
                _currentIndex = _history.Count - 1;

                // 上限を超えたら古い要素を削除
                TrimToMaxHistory();
            }
        }

        private void TrimToMaxHistory()
        {
            lock (_lock)
            {
                if (_maxHistory < 1) _maxHistory = 1;
                if (_history.Count <= _maxHistory) return;

                int removeCount = _history.Count - _maxHistory;
                // 古い要素を削除
                _history.RemoveRange(0, removeCount);

                // currentIndex を新しい配列に合わせて調整
                _currentIndex = _history.Count - 1;
                if (_currentIndex < -1) _currentIndex = -1;
            }
        }

        public void Undo()
        {
            lock (_lock)
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                }
            }
        }

        public void Redo()
        {
            lock (_lock)
            {
                if (_currentIndex < _history.Count - 1)
                {
                    _currentIndex++;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _history.Clear();
                _currentIndex = -1;
            }
        }

        // Backward-compatible alias used throughout the codebase
        public void PushState(object state) => SaveState(state);

        // Backward-compatible alias for getting current state (if needed)
        // (CurrentState property already exists)

        // --- Compatibility helpers to interoperate with Common.Undo -----
        // Begin an operation scope in the common undo manager (if consumers expect it)
        public void BeginScope(string? description = null)
        {
            try
            {
                UndoService.Instance.BeginScope(description);
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"BeginScope failed - operation invalid: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BeginScope unexpected error: {ex}");
                throw; // 想定外の例外は再スロー
            }
        }

        public void EndScope()
        {
            try
            {
                UndoService.Instance.EndScope();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"EndScope failed - no matching BeginScope: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EndScope unexpected error: {ex}");
                throw; // 想定外の例外は再スロー
            }
        }

        // Push an IUndoAction into the common undo manager
        public void Push(IUndoAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            UndoService.Instance.Push(action);
        }
    }
}
//{
//    public class UndoManager
//    {
//        private readonly Stack<object> undoStack = new();
//        private readonly Stack<object> redoStack = new();
//        public object CurrentState { get; private set; }

//        public void SaveState(object state)
//        {
//            undoStack.Push(state);
//            CurrentState = state;
//            // 新しい変更が入ると redo は無効化
//            redoStack.Clear();
//            Debug.WriteLine("UndoStack: " + string.Join(",", undoStack.Select(s => (s as InputModel)?.PileLayoutItems.Count ?? -1)));
//        }

//        /// <summary>
//        /// SaveState のエイリアス（互換性のため）
//        /// </summary>
//        public void PushState(object state) => SaveState(state);

//        // Undo: 現在のトップを redo に送り、undo の次点を CurrentState にする
//        public void Undo()
//        {
//            // 1つ以上の状態があり、元に戻せる履歴があること（少なくとも 2 要素）が必要
//            if (undoStack.Count > 1)
//            {
//                // 現在トップ（最新）を redo に移動
//                var top = undoStack.Pop();
//                redoStack.Push(top);

//                // undo の次点（新しいトップ）を CurrentState とする
//                CurrentState = undoStack.Peek();
//            }
//        }

//        // Redo: redo スタックから取り出して undo スタックに戻す
//        public void Redo()
//        {
//            if (redoStack.Count > 0)
//            {
//                var state = redoStack.Pop();
//                undoStack.Push(state);
//                CurrentState = state;
//            }
//        }
//    }
//}
