using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 欄をドラッグして別の位置へドッキングする操作 (2026-10-25、.NET 10 への移行で追加)。
    ///
    /// .NET 8 では AvalonDock 5.0.1 に net8.0 向けの部品が無く、.NET Framework 4.8 向けを代用していた (NU1701)。
    /// .NET 10 では net10.0 向けの部品に替わるので、DLL の中身が別物になる。メニューから押す操作
    /// (浮かせる・戻す・自動的に隠す・非表示) は <see cref="DockPaneMenuTests"/> が見ている。ここではドラッグで
    /// 手を離したときに走る「着地先」の処理を見る。
    ///
    /// マウスのドラッグそのもの (重ね表示の矢印の当たり判定) は画面操作が要るので見ない。着地先は AvalonDock の
    /// 非公開のクラス (AnchorablePaneDropTarget など) なので、リフレクションで作って Drop を呼ぶ。AvalonDock を更新して
    /// これらの名前や引数が変わると落ちる。そのときは実機でドラッグを確かめてから、ここを新しい形に合わせること。
    /// </summary>
    [TestClass]
    public class AvalonDockDragDropTests
    {
        /// <summary>
        /// AvalonDock が .NET (Core) 向けの部品で読み込まれている。.NET Framework 向けの代用に戻ったら
        /// (TargetFramework を下げた・AvalonDock を net10 向けの無い版にした)、ドッキングを実機で確かめ直すこと。
        /// </summary>
        [TestMethod]
        public void AvalonDock_IsLoadedFromItsDotNetBuild_NotTheNetFrameworkFallback()
        {
            foreach (var asm in new[] { typeof(DockingManager).Assembly, typeof(LayoutRoot).Assembly })
            {
                var framework = asm.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "";
                Assert.IsFalse(framework.StartsWith(".NETFramework", StringComparison.Ordinal),
                    $"{asm.GetName().Name} が .NET Framework 向けの部品 ({framework}) で読み込まれています。ドッキングを実機で確かめてください");
            }
        }

        /// <summary>浮かせた欄を、ほかの欄のペインの中央に落とす → そのペインのタブになる。</summary>
        [TestMethod]
        public void DropOnAnchorablePaneCenter_JoinsItAsATab()
        {
            OnLayout((manager, window, l) =>
            {
                var floating = FloatAndGetWindow(manager, window, l.A);
                Drop(manager, "AnchorablePaneDropTarget", PaneControl<LayoutAnchorablePaneControl>(window, l.PaneB), DropTargetType.AnchorablePaneDockInside, floating);
                Settle(window);

                Assert.AreSame(l.PaneB, l.A.Parent, "B のペインのタブになっていません");
                // 落とした欄は先頭のタブに入る (AvalonDock の動作)
                CollectionAssert.AreEqual(new[] { "A", "B" }, l.PaneB.Children.Select(c => c.Title).ToList(), "タブの並びが違います");
                AssertDockedOnce(manager, l.A);
            });
        }

        /// <summary>浮かせた欄を、ほかの欄のペインの下の矢印に落とす → 同じ列の下に独立したペインで入る。</summary>
        [TestMethod]
        public void DropOnAnchorablePaneBottom_AddsAPaneBelowIt()
        {
            OnLayout((manager, window, l) =>
            {
                var floating = FloatAndGetWindow(manager, window, l.A);
                Drop(manager, "AnchorablePaneDropTarget", PaneControl<LayoutAnchorablePaneControl>(window, l.PaneB), DropTargetType.AnchorablePaneDockBottom, floating);
                Settle(window);

                var pane = l.A.Parent as LayoutAnchorablePane;
                Assert.IsNotNull(pane, "欄のペインに入っていません");
                Assert.AreEqual(1, pane!.ChildrenCount, "B のタブになっています (独立したペインになっていません)");
                var group = pane.Parent as LayoutAnchorablePaneGroup;
                Assert.IsNotNull(group, "列に入っていません");
                Assert.AreEqual(Orientation.Vertical, group!.Orientation, "縦の並びになっていません");
                Assert.AreEqual(group.IndexOfChild(l.PaneB) + 1, group.IndexOfChild(pane), "B のすぐ下に入っていません");
                AssertDockedOnce(manager, l.A);
            });
        }

        /// <summary>浮かせた欄を、画面の左端の矢印に落とす → いちばん左の列になる。</summary>
        [TestMethod]
        public void DropOnManagerLeftEdge_MakesItTheLeftmostColumn()
        {
            OnLayout((manager, window, l) =>
            {
                var floating = FloatAndGetWindow(manager, window, l.A);
                Drop(manager, "DockingManagerDropTarget", manager, DropTargetType.DockingManagerDockLeft, floating);
                Settle(window);

                var root = manager.Layout.RootPanel;
                Assert.AreEqual(Orientation.Horizontal, root.Orientation, "横の並びではなくなっています");
                var first = root.Children.First();
                Assert.IsTrue(first.Descendents().OfType<LayoutAnchorable>().Contains(l.A) || (first as LayoutAnchorablePane)?.Children.Contains(l.A) == true,
                    "いちばん左の列に入っていません");
                Assert.IsTrue(root.Descendents().OfType<LayoutDocumentPane>().Any(p => p.Children.Contains(l.Doc)), "文書がなくなっています");
                AssertDockedOnce(manager, l.A);
            });
        }

        /// <summary>浮かせた欄を、文書のペインの中央に落とす → 文書と並ぶタブになる。</summary>
        [TestMethod]
        public void DropOnDocumentPaneCenter_JoinsTheDocumentTabs()
        {
            OnLayout((manager, window, l) =>
            {
                var floating = FloatAndGetWindow(manager, window, l.A);
                Drop(manager, "DocumentPaneDropTarget", PaneControl<LayoutDocumentPaneControl>(window, l.DocPane), DropTargetType.DocumentPaneDockInside, floating);
                Settle(window);

                Assert.AreSame(l.DocPane, l.A.Parent, "文書のペインのタブになっていません");
                Assert.IsTrue(l.DocPane.Children.Contains(l.Doc), "元の文書がなくなっています");
                AssertDockedOnce(manager, l.A);
            });
        }

        /// <summary>浮かせた欄を、文書のペインの右の (欄として置く) 矢印に落とす → 文書の右に欄のペインで入る。</summary>
        [TestMethod]
        public void DropOnDocumentPaneRightAsAnchorable_PlacesAPaneBesideTheDocuments()
        {
            OnLayout((manager, window, l) =>
            {
                var floating = FloatAndGetWindow(manager, window, l.A);
                Drop(manager, "DocumentPaneDropAsAnchorableTarget", PaneControl<LayoutDocumentPaneControl>(window, l.DocPane), DropTargetType.DocumentPaneDockAsAnchorableRight, floating);
                Settle(window);

                var pane = l.A.Parent as LayoutAnchorablePane;
                Assert.IsNotNull(pane, "欄のペインに入っていません (文書のタブになっています)");
                Assert.AreEqual(1, pane!.ChildrenCount, "ほかの欄のタブになっています");
                Assert.IsFalse(pane.Descendents().OfType<LayoutDocument>().Any(), "文書と同じペインに入っています");
                AssertDockedOnce(manager, l.A);
            });
        }

        /// <summary>文書を浮かせて、文書のペインの中央へ戻す → 元のタブに戻る。</summary>
        [TestMethod]
        public void FloatedDocument_DropsBackIntoTheDocumentPane()
        {
            OnLayout((manager, window, l) =>
            {
                var doc2 = new LayoutDocument { Title = "Doc2", Content = new TextBlock { Text = "d2" } };
                l.DocPane.Children.Add(doc2);
                Settle(window);
                doc2.Float();
                Settle(window);
                Assert.IsTrue(doc2.IsFloating, "文書が浮きません");
                var floating = manager.Layout.FloatingWindows.OfType<LayoutDocumentFloatingWindow>().Single();
                Assert.IsTrue(manager.FloatingWindows.Any(w => w.IsVisible), "浮いた文書の窓が表示されていません");

                Drop(manager, "DocumentPaneDropTarget", PaneControl<LayoutDocumentPaneControl>(window, l.DocPane), DropTargetType.DocumentPaneDockInside, floating);
                Settle(window);

                Assert.IsFalse(doc2.IsFloating, "戻りません");
                Assert.AreSame(l.DocPane, doc2.Parent, "文書のペインに戻っていません");
                Assert.AreEqual(0, manager.Layout.FloatingWindows.Count, "空になった浮いた窓が残っています");
            });
        }

        // ── 補助 ──

        private sealed record LayoutParts(LayoutDocumentPane DocPane, LayoutDocument Doc, LayoutAnchorablePane PaneA, LayoutAnchorable A, LayoutAnchorablePane PaneB, LayoutAnchorable B);

        /// <summary>
        /// メイン画面と同じ形: 左に文書のペイン、右に欄の列 (上に A、下に B の別々のペイン)。
        /// 画面の外に表示し、手前に出さない (利用者の作業中の画面を邪魔しない)。
        /// </summary>
        private static void OnLayout(Action<DockingManager, Window, LayoutParts> body)
        {
            OnSta(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var doc = new LayoutDocument { Title = "Doc", Content = new TextBlock { Text = "d" } };
                var docPane = new LayoutDocumentPane(doc);
                var a = new LayoutAnchorable { Title = "A", Content = new TextBlock { Text = "a" } };
                var b = new LayoutAnchorable { Title = "B", Content = new TextBlock { Text = "b" } };
                var paneA = new LayoutAnchorablePane(a);
                var paneB = new LayoutAnchorablePane(b);
                var column = new LayoutAnchorablePaneGroup(paneA) { Orientation = Orientation.Vertical };
                column.Children.Add(paneB);
                var panel = new LayoutPanel(docPane) { Orientation = Orientation.Horizontal };
                panel.Children.Add(column);
                var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = panel } };
                var window = new Window { Width = 800, Height = 500, Content = manager, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); Settle(window);
                    body(manager, window, new LayoutParts(docPane, doc, paneA, a, paneB, b));
                }
                finally
                {
                    foreach (var fw in manager.FloatingWindows.ToList()) fw.Close();
                    window.Close();
                }
            });
        }

        /// <summary>欄を浮かせ、その浮いた窓 (レイアウト上の) を返す。浮いた窓が実際に表示されることも見る。</summary>
        private static LayoutAnchorableFloatingWindow FloatAndGetWindow(DockingManager manager, Window window, LayoutAnchorable anchorable)
        {
            anchorable.Float();
            Settle(window);
            Assert.IsTrue(anchorable.IsFloating, "欄が浮きません");
            Assert.IsTrue(manager.FloatingWindows.OfType<LayoutAnchorableFloatingWindowControl>().Any(w => w.IsVisible), "浮いた欄の窓が表示されていません");
            return manager.Layout.FloatingWindows.OfType<LayoutAnchorableFloatingWindow>()
                .Single(fw => fw.Descendents().OfType<LayoutAnchorable>().Contains(anchorable));
        }

        /// <summary>
        /// AvalonDock の着地先 (非公開) を作り、浮いた窓を落とす。ドラッグで手を離したときと同じ順に、
        /// 着地先の Drop のあと浮いた窓の側が自分を閉じる (LayoutFloatingWindowControl.InternalClose)。
        /// </summary>
        private static void Drop(DockingManager manager, string targetClass, object targetControl, DropTargetType type, LayoutFloatingWindow floating)
        {
            var t = typeof(DockingManager).Assembly.GetType("AvalonDock.Controls." + targetClass);
            Assert.IsNotNull(t, $"AvalonDock に着地先 {targetClass} がありません (AvalonDock の更新で変わった。実機でドラッグを確かめ、このテストを合わせる)");
            var ctor = t!.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { targetControl.GetType(), typeof(Rect), typeof(DropTargetType) }, null);
            Assert.IsNotNull(ctor, $"{targetClass} の作り方が変わりました (AvalonDock の更新)");
            var target = ctor!.Invoke(new object[] { targetControl, new Rect(0, 0, 10, 10), type });
            var drop = t.GetMethod("Drop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(LayoutFloatingWindow) }, null);
            Assert.IsNotNull(drop, $"{targetClass} に Drop(LayoutFloatingWindow) がありません (AvalonDock の更新)");
            var control = manager.FloatingWindows.SingleOrDefault(w => ReferenceEquals(w.Model, floating));
            Assert.IsNotNull(control, "浮いた窓の表示側がありません");
            var close = typeof(LayoutFloatingWindowControl).GetMethod("InternalClose", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            Assert.IsNotNull(close, "LayoutFloatingWindowControl.InternalClose(bool) がありません (AvalonDock の更新)");
            try
            {
                drop!.Invoke(target, new object[] { floating });
                close!.Invoke(control, new object[] { false });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }

        /// <summary>落としたあと: 浮いておらず、レイアウトに 1 か所だけ入っていて、浮いた窓が残っていない。</summary>
        private static void AssertDockedOnce(DockingManager manager, LayoutAnchorable anchorable)
        {
            Assert.IsFalse(anchorable.IsFloating, "浮いたままです");
            Assert.AreEqual(1, manager.Layout.Descendents().OfType<LayoutAnchorable>().Count(x => ReferenceEquals(x, anchorable)), "欄が二重に入っているか、なくなっています");
            Assert.AreEqual(0, manager.Layout.FloatingWindows.Count, "空になった浮いた窓が残っています");
            Assert.IsTrue(manager.GetLayoutItemFromModel(anchorable) != null, "ドッキングした欄の表示側 (LayoutItem) がありません");
        }

        private static T PaneControl<T>(Window window, ILayoutElement model) where T : FrameworkElement, ILayoutControl
        {
            var control = Descendants<T>(window).FirstOrDefault(c => ReferenceEquals(c.Model, model));
            Assert.IsNotNull(control, $"{typeof(T).Name} が画面にありません");
            return control!;
        }

        private static void Settle(Window window)
        {
            for (int i = 0; i < 4; i++)
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
            }
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject d) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is T t) yield return t;
                foreach (var x in Descendants<T>(child)) yield return x;
            }
        }

        private static void OnSta(Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            if (timedOut) Assert.Inconclusive("STA スレッドが時間内に終わりませんでした");
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }
    }
}
