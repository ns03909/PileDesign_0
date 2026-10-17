using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace PileDesign.Common
{
    /// <summary>
    /// 欄の見出しの ▾ メニュー (Themes/AvalonDockTabStyles.xaml) の「ドッキング」(2026-10-17)。
    ///
    /// <para>AvalonDock の DockCommand は、浮かせた欄を元の位置に戻せない。浮かせると「元の位置」(PreviousContainer) が空になり、
    /// 手近な欄のペインにタブとして入れる (メイン画面ならプロパティ欄がミニマップ欄のタブになる)。手近な欄が無いと何も起きず、
    /// しかも同じ欄を浮いた窓のペインに二重に入れてしまう (AvalonDock の不具合)。</para>
    ///
    /// <para>そこで元の位置を自分で覚える。欄がドッキングしている間、どの列の何番目のペインにいたかをレイアウトが変わるたびに控え、
    /// 「ドッキング」で同じペイン (残っていれば) か、同じ列の同じ順番に新しいペインとして戻す。列ごと無くなっていれば、
    /// 画面の右端に新しい列としてドッキングする。浮いた欄では AvalonDock の DockCommand は呼ばない。</para>
    /// </summary>
    public static class DockPaneCommands
    {
        /// <summary>
        /// 浮いた欄・自動で隠した欄をドッキングする。引数は欄 (LayoutAnchorableItem。メニューの DataContext)。
        ///
        /// <para>押せるかどうかは WPF の入力のたびに判定し直す (CommandManager.RequerySuggested)。メニューは画面ごとに 1 つで、
        /// 同じ欄の ▾ から開くと引数が変わらないので、RelayCommand では、ドッキング中に開いたときの「押せない」が
        /// 浮いた窓の ▾ から開いたときまで残った (実機で確認)。</para>
        /// </summary>
        public static ICommand Dock { get; } = new RequeryCommand(DockPane, CanDock);

        private sealed class RequeryCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
        {
            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
            public bool CanExecute(object? parameter) => canExecute(parameter);
            public void Execute(object? parameter) => execute(parameter);
        }

        private static bool CanDock(object? parameter) =>
            parameter is LayoutAnchorableItem item && item.LayoutElement is LayoutAnchorable anchorable &&
            (anchorable.IsFloating || item.DockCommand.CanExecute(null));

        private static void DockPane(object? parameter)
        {
            if (parameter is not LayoutAnchorableItem item || item.LayoutElement is not LayoutAnchorable anchorable) return;
            if (!anchorable.IsFloating) { item.DockCommand.Execute(null); return; }   // 自動で隠した欄は AvalonDock で戻る
            DockFloating(anchorable);
        }

        // ── 元の位置を覚える ──────────────────────────────────────────────

        /// <summary>欄がドッキングしていた場所: 列 (ペインを並べた入れ物)・その中の順番・ペイン。</summary>
        private sealed class Place
        {
            public WeakReference<ILayoutGroup> Column = null!;
            public int Index;
            public WeakReference<LayoutAnchorablePane> Pane = null!;
        }

        private static readonly ConditionalWeakTable<LayoutAnchorable, Place> Places = new();

        /// <summary>DockingManager に付けると、欄がドッキングしていた場所を覚える (共通スタイルで全画面に付ける)。</summary>
        public static readonly DependencyProperty RemembersPlacesProperty = DependencyProperty.RegisterAttached(
            "RemembersPlaces", typeof(bool), typeof(DockPaneCommands), new PropertyMetadata(false, OnRemembersPlacesChanged));

        public static bool GetRemembersPlaces(DependencyObject d) => (bool)d.GetValue(RemembersPlacesProperty);
        public static void SetRemembersPlaces(DependencyObject d, bool value) => d.SetValue(RemembersPlacesProperty, value);

        private static void OnRemembersPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DockingManager manager || e.NewValue is not true) return;
            manager.LayoutChanged += (_, _) => Watch(manager.Layout);
            Watch(manager.Layout);
        }

        private static void Watch(LayoutRoot? root)
        {
            if (root == null) return;
            root.Updated -= OnLayoutUpdated;
            root.Updated += OnLayoutUpdated;
            RecordPlaces(root);
        }

        private static void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (sender is LayoutRoot root) RecordPlaces(root);
        }

        /// <summary>ドッキングしている欄 (浮いていない・隠れていない) の場所を控える。</summary>
        internal static void RecordPlaces(LayoutRoot root)
        {
            if (root.RootPanel == null) return;
            foreach (var anchorable in root.RootPanel.Descendents().OfType<LayoutAnchorable>())
            {
                if (anchorable.Parent is not LayoutAnchorablePane pane || pane.Parent is not ILayoutGroup column) continue;
                Places.AddOrUpdate(anchorable, new Place
                {
                    Column = new WeakReference<ILayoutGroup>(column),
                    Index = column.IndexOfChild(pane),
                    Pane = new WeakReference<LayoutAnchorablePane>(pane),
                });
            }
        }

        // ── 戻す ──────────────────────────────────────────────────────────

        /// <summary>浮いた欄を、覚えていた場所 (無ければ画面の右端) にドッキングする。</summary>
        internal static void DockFloating(LayoutAnchorable anchorable)
        {
            if (anchorable.Root is not LayoutRoot root || root.RootPanel == null) return;
            var floatingWindow = anchorable.FindParent<LayoutFloatingWindow>();
            double width = anchorable.FloatingWidth > 0 ? anchorable.FloatingWidth : 320;
            if (anchorable.Parent is ILayoutContainer floatingPane) floatingPane.RemoveChild(anchorable);

            if (!ReturnToRememberedPlace(anchorable, root))
                AddAtRightEdge(anchorable, root, width);

            if (floatingWindow != null && !floatingWindow.Descendents().OfType<LayoutAnchorable>().Any())
                root.FloatingWindows.Remove(floatingWindow);
            anchorable.IsActive = true;
        }

        private static bool IsDocked(ILayoutElement element, LayoutRoot root) =>
            ReferenceEquals(element.Root, root) && element.FindParent<LayoutFloatingWindow>() == null;

        private static bool ReturnToRememberedPlace(LayoutAnchorable anchorable, LayoutRoot root)
        {
            if (!Places.TryGetValue(anchorable, out var place)) return false;
            // 元のペインが残っていれば (同じペインにほかの欄があった)、そこへタブとして戻す
            if (place.Pane.TryGetTarget(out var pane) && IsDocked(pane, root))
            {
                pane.Children.Add(anchorable);
                return true;
            }
            // 列が残っていれば、同じ順番に新しいペインとして戻す
            if (place.Column.TryGetTarget(out var column) && column is ILayoutElement columnElement && IsDocked(columnElement, root))
            {
                column.InsertChildAt(Math.Clamp(place.Index, 0, column.ChildrenCount), new LayoutAnchorablePane(anchorable));
                return true;
            }
            return false;
        }

        /// <summary>画面の右端に新しい列として足す。上下に並べた画面では、全体を左に、新しい列を右に置く。</summary>
        private static void AddAtRightEdge(LayoutAnchorable anchorable, LayoutRoot root, double width)
        {
            var rootPanel = root.RootPanel;
            var column = new LayoutAnchorablePane(anchorable) { DockWidth = new GridLength(width) };
            if (rootPanel.Orientation == System.Windows.Controls.Orientation.Horizontal)
                rootPanel.Children.Add(column);
            else
            {
                var horizontal = new LayoutPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                root.RootPanel = horizontal;
                horizontal.Children.Add(rootPanel);
                horizontal.Children.Add(column);
            }
        }
    }
}
