using PileDesign.Common.Undo;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PileDesign.Common
{
    /// <summary>
    /// 元に戻すための控えを、窓の段でまとめて取る添付プロパティ。窓に
    /// <c>common:UndoCapture.Enabled="True"</c> を付け、DataContext の ViewModel が
    /// <see cref="ISnapshotUndoHost"/> を実装していれば働く。
    ///
    /// <para>以前は控えを欄・コンボボックス・表ごとに手で付けていて、付け忘れた操作は Ctrl+Z で戻らなかった
    /// (杭断面の鋼管寸法、杭体の根入れ、杭頭部のコンボボックス、地盤のラジオボタンなど)。
    /// また、フォーカスが移るたびに何も変わっていなくても控えを積み、押しても何も起きない「元に戻す」が混ざった。</para>
    ///
    /// <para>ここでは<b>利用者の操作の直前</b>に控えを取る: キーボードのフォーカスが入るとき・マウスを押したとき・
    /// 書き戻していない編集がある欄から外れるとき・打つたびに値が入る欄で打ち始めるとき。
    /// どの操作も、その前の状態が必ず履歴に残る。同じ状態は積まない
    /// (<see cref="UndoManager.StateFingerprint"/> に中身の指紋を渡す) ので、取りすぎても空振りの手は増えない。</para>
    /// </summary>
    public static class UndoCapture
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(UndoCapture), new PropertyMetadata(false, OnEnabledChanged));

        public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);
        public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

        /// <summary>打つたびに書き戻す欄で、このフォーカスの間に控えを取り終えたか。</summary>
        private static readonly DependencyProperty SnapshotTakenProperty = DependencyProperty.RegisterAttached(
            "SnapshotTaken", typeof(bool), typeof(UndoCapture), new PropertyMetadata(false));

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window w || !(bool)e.NewValue) return;

            // 開いた直後の状態 (最初の編集の前の基準)。ViewModel の初期化が終わってから取る
            w.Loaded += (_, _) => w.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Take(w)));

            w.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e2) =>
            {
                if (e2.OriginalSource is TextBox tb) tb.ClearValue(SnapshotTakenProperty);
                Take(w);
            }), handledEventsToo: true);

            w.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Take(w)), handledEventsToo: true);

            w.AddHandler(Keyboard.PreviewLostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e2) =>
            {
                if (ShouldSnapshotOnLeave(e2.OriginalSource, e2.OldFocus)) Take(w);
            }), handledEventsToo: true);

            void BeforeEdit(object? source)
            {
                if (source is not TextBox tb || !UpdatesWhileTyping(tb) || (bool)tb.GetValue(SnapshotTakenProperty)) return;
                tb.SetValue(SnapshotTakenProperty, true);
                Take(w);
            }
            w.AddHandler(UIElement.PreviewTextInputEvent, new TextCompositionEventHandler((_, e2) => BeforeEdit(e2.OriginalSource)), handledEventsToo: true);
            w.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e2) =>
            {
                if (e2.Key is Key.Back or Key.Delete) BeforeEdit(e2.OriginalSource);
            }), handledEventsToo: true);
            w.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler((_, e2) => BeforeEdit(e2.OriginalSource)), handledEventsToo: true);
        }

        /// <summary>いまの状態の控えを積む (同じ状態なら積まない)。窓の外からも呼べる (テスト・明示の区切り)。</summary>
        public static void Take(Window window)
        {
            if (window.DataContext is not ISnapshotUndoHost host) return;
            var manager = host.UndoManager;
            manager.StateFingerprint ??= Fingerprint;
            object? state;
            try { state = host.CaptureUndoSnapshot(); }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException)
            {
                Serilog.Log.Debug(ex, "[UndoCapture] 控えを取れませんでした");
                return;
            }
            if (state != null) manager.SaveState(state);
        }

        private static readonly JsonSerializerOptions FingerprintOptions = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>控えの中身の指紋 (JSON)。書き出せない形なら null (比べずに積む)。</summary>
        internal static string? Fingerprint(object state)
        {
            try { return JsonSerializer.Serialize(state, state.GetType(), FingerprintOptions); }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>この欄から外れるときに控えを取るか (外れたときに書き戻す欄で、書き戻していない編集がある)。</summary>
        internal static bool ShouldSnapshotOnLeave(object? source, IInputElement? oldFocus)
        {
            if (source is not TextBox tb || !IsEditableField(tb) || !ReferenceEquals(oldFocus, tb)) return false;
            return BindingOperations.GetBindingExpression(tb, TextBox.TextProperty) is { IsDirty: true };
        }

        /// <summary>打つたびに値を書き戻す欄か (この欄は外れるときには書き戻す編集が残っていない)。</summary>
        internal static bool UpdatesWhileTyping(TextBox tb)
            => IsEditableField(tb)
               && BindingOperations.GetBinding(tb, TextBox.TextProperty) is { UpdateSourceTrigger: UpdateSourceTrigger.PropertyChanged };

        private static bool IsEditableField(TextBox tb) => !tb.IsReadOnly && !IsInsideDataGrid(tb);

        private static bool IsInsideDataGrid(DependencyObject d)
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is DataGridCell) return true;
            return false;
        }
    }
}
