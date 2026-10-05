using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Common
{
    /// <summary>
    /// 入力欄にフォーカスがあっても、窓の Ctrl+Z / Ctrl+Y (KeyBinding) が効くようにする添付プロパティ。
    ///
    /// TextBox は Ctrl+Z を自分の文字の取り消しとして受け取り、<b>取り消すものが無くても処理済みにする</b>。
    /// そのため窓の KeyBinding (Ctrl+Z → 窓の Undo) は、欄にフォーカスがある間はまったく届かなかった。
    /// Tab で次の欄へ移るとフォーカスは常にどこかの欄にあるので、実際には窓の Undo はほぼ使えなかった
    /// (杭断面ウィンドウで実機確認。地盤・ChangWindow は個別に PreviewKeyDown で受けて回避していた)。
    ///
    /// 窓に <c>common:UndoKeys.RouteFromTextBoxes="True"</c> を付けると、PreviewKeyDown で窓の KeyBinding の
    /// コマンドを先に実行する。ただし欄で打ちかけ (値に書き戻す前) で、その欄に取り消せる打鍵があるときは、
    /// これまでどおり欄の文字の取り消しに任せる。
    /// </summary>
    public static class UndoKeys
    {
        public static readonly DependencyProperty RouteFromTextBoxesProperty = DependencyProperty.RegisterAttached(
            "RouteFromTextBoxes", typeof(bool), typeof(UndoKeys), new PropertyMetadata(false, OnChanged));

        public static void SetRouteFromTextBoxes(DependencyObject d, bool value) => d.SetValue(RouteFromTextBoxesProperty, value);
        public static bool GetRouteFromTextBoxes(DependencyObject d) => (bool)d.GetValue(RouteFromTextBoxesProperty);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window w) return;
            w.PreviewKeyDown -= OnPreviewKeyDown;
            if ((bool)e.NewValue) w.PreviewKeyDown += OnPreviewKeyDown;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is Window w && TryRoute(w, e.Key, Keyboard.Modifiers, Keyboard.FocusedElement))
                e.Handled = true;
        }

        /// <summary>
        /// Ctrl+Z / Ctrl+Y を窓の KeyBinding のコマンドへ回す。回したら true。
        /// 打ちかけの欄に取り消せる打鍵があるとき、または窓に該当する KeyBinding が無いときは false (欄に任せる)。
        /// </summary>
        internal static bool TryRoute(Window window, Key key, ModifierKeys modifiers, IInputElement? focused)
        {
            if (modifiers != ModifierKeys.Control || key is not (Key.Z or Key.Y)) return false;

            if (focused is TextBox tb && IsEditingWithTypingToUndo(tb, key)) return false;

            foreach (InputBinding binding in window.InputBindings)
            {
                if (binding is not KeyBinding kb || kb.Key != key || kb.Modifiers != modifiers || kb.Command == null) continue;
                if (!kb.Command.CanExecute(kb.CommandParameter)) return true;   // 窓の Undo が空でも欄には渡さない (欄の取り消しは上で判定済み)
                kb.Command.Execute(kb.CommandParameter);
                // 戻した値に対して「…にしました」が残ると紛らわしいので消す
                NumericInput.ClearAllCorrectionNotices();
                return true;
            }
            return false;
        }

        /// <summary>欄で打ちかけ (値に書き戻す前) で、欄自身に取り消し / やり直しできる打鍵があるか。</summary>
        private static bool IsEditingWithTypingToUndo(TextBox tb, Key key)
        {
            var expr = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            bool editing = expr == null || expr.IsDirty;
            return editing && (key == Key.Z ? tb.CanUndo : tb.CanRedo);
        }
    }
}
