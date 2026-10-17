using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 欄の見出しの ▾ メニュー (2026-10-17)。AvalonDock 5.0 は既定のメニューを持たず
    /// (DockingManager.AnchorableContextMenu が null)、テーマを当てないこのアプリでは ▾ を押しても何も開かなかった。
    /// Themes/AvalonDockTabStyles.xaml の共通スタイルで全画面の DockingManager に配る。
    /// </summary>
    [TestClass]
    public class DockPaneMenuTests
    {
        /// <summary>(前提) AvalonDock は既定のメニューを持たない。持つようになったら、自前のメニューを外せるか見直す。</summary>
        [TestMethod]
        public void AvalonDock_HasNoDefaultPaneMenu()
        {
            bool? isNull = null;
            // アプリの共通スタイルを外して (Style = null) AvalonDock だけの既定を見る
            OnSta(() => isNull = new DockingManager { Style = null }.AnchorableContextMenu == null);
            Assert.IsTrue(isNull, "(前提) AvalonDock に既定の ▾ メニューが付くようになりました。AvalonDockTabStyles.xaml の自前のメニューと重複していないか見直してください");
        }

        /// <summary>
        /// 欄を使う画面の DockingManager が Style を自分で指定していない (指定すると共通スタイルが当たらず、▾ メニューが消える)。
        /// </summary>
        [TestMethod]
        public void EveryDockingManager_GetsTheSharedStyle()
        {
            int scanned = 0;
            var problems = new List<string>();
            foreach (var file in System.IO.Directory.GetFiles(TestSource.Dir("Graphics_r1", "Views"), "*.xaml"))
                foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(file), @"<avalonDock:DockingManager\b[^>]*>"))
                {
                    scanned++;
                    if (m.Value.Contains("Style=")) problems.Add(System.IO.Path.GetFileName(file));
                }
            TestSource.AssertScanned(scanned, 7, "DockingManager");
            Assert.AreEqual(0, problems.Count, "DockingManager に Style を指定しています (共通の ▾ メニューが当たりません): " + string.Join(", ", problems));
        }

        [TestMethod]
        public void PaneMenu_Opens_AndItsItemsFloatDockAutoHideAndHide()
        {
            var log = new List<string>();
            OnSta(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var anchorable = new LayoutAnchorable { Title = "P", Content = new TextBlock { Text = "x" }, CanFloat = true, CanAutoHide = true, CanHide = true };
                // メイン画面と同じ形: プロパティ欄とミニマップ欄が同じ列の別々のペインにある。
                // (欄が 1 つきりのペインを浮かせると、元のペインが消えて「ドッキング」の戻り先がなくなる。AvalonDock の動作)
                var column = new LayoutAnchorablePaneGroup(new LayoutAnchorablePane(anchorable)) { Orientation = System.Windows.Controls.Orientation.Vertical };
                column.Children.Add(new LayoutAnchorablePane(new LayoutAnchorable { Title = "Q", Content = new TextBlock { Text = "y" } }));
                var panel = new LayoutPanel(new LayoutDocumentPane());
                panel.Children.Add(column);
                var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = panel } };
                var window = new Window { Width = 500, Height = 300, Content = manager, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); Settle(window);
                    var menu = manager.AnchorableContextMenu;
                    Assert.IsNotNull(menu, "共通スタイルから ▾ メニューが渡っていません (AvalonDockTabStyles.xaml)");
                    // 文書ペインの一覧ボタンも同じ種類なので、メニューを渡された ▾ を選ぶ
                    var button = Descendants<DropDownButton>(window).FirstOrDefault(b => ReferenceEquals(b.DropDownContextMenu, menu));
                    Assert.IsNotNull(button, "欄の ▾ にメニューが渡っていません");
                    typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
                    Settle(window);
                    Assert.IsTrue(menu.IsOpen, "▾ を押してもメニューが開きません");
                    log.Add(string.Join("/", menu.Items.OfType<MenuItem>().Select(m => (string)m.Header)));
                    menu.IsOpen = false;

                    MenuItem Item(string header) => menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == header);
                    void Click(string header) { var item = Item(header); Assert.IsTrue(item.Command?.CanExecute(item.CommandParameter) == true, $"「{header}」が押せません"); item.Command!.Execute(item.CommandParameter); Settle(window); }

                    Click("フローティング");
                    Assert.IsTrue(anchorable.IsFloating, "「フローティング」で浮きません");
                    // 浮いた窓の ▾ も同じメニュー。DataContext は AvalonDock が欄にする
                    menu.DataContext = manager.GetLayoutItemFromModel(anchorable);
                    // 同じ欄なので引数は変わらない。ドッキング中の「押せない」が残らず、入力のたびの判定し直しで押せる表示になる
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested(); Settle(window);
                    Assert.IsTrue(Item("ドッキング").IsEnabled, "浮いた欄のメニューで「ドッキング」が押せない表示のままです");
                    Click("ドッキング");
                    Assert.IsFalse(anchorable.IsFloating, "「ドッキング」で戻りません");
                    Assert.AreSame(column, anchorable.Parent?.Parent, "元の列 (ミニマップ欄と同じ列) に戻っていません");
                    Assert.AreEqual(1, ((LayoutAnchorablePane)anchorable.Parent!).ChildrenCount, "ほかの欄のタブになっています (独立したペインで戻っていません)");
                    Assert.AreEqual(0, column.IndexOfChild((LayoutAnchorablePane)anchorable.Parent!), "元の順番 (上) に戻っていません");
                    Assert.AreEqual(0, manager.Layout.FloatingWindows.Count, "空になった浮いた窓が残っています");
                    menu.DataContext = manager.GetLayoutItemFromModel(anchorable);
                    Click("自動的に隠す");
                    Assert.IsTrue(anchorable.IsAutoHidden, "「自動的に隠す」で隠れません");
                    menu.DataContext = manager.GetLayoutItemFromModel(anchorable);
                    Click("自動的に隠す");
                    Assert.IsFalse(anchorable.IsAutoHidden, "もう一度「自動的に隠す」で元に戻りません");
                    menu.DataContext = manager.GetLayoutItemFromModel(anchorable);
                    Click("非表示");
                    Assert.IsTrue(anchorable.IsHidden, "「非表示」で閉じません");
                }
                finally { window.Close(); }
            });
            CollectionAssert.AreEqual(new[] { "フローティング/ドッキング/自動的に隠す/非表示" }, log, "メニューの項目が違います");
        }

        /// <summary>
        /// 欄が 1 つきりのペイン (解析結果ダッシュボードの列など) を浮かせると、AvalonDock は空になった元のペインを消すので、
        /// AvalonDock の DockCommand では戻る先がなく浮いたままだった。しかも同じ欄を浮いた窓のペインに二重に入れる (AvalonDock の不具合)。
        /// 「ドッキング」は AvalonDock の DockCommand を呼ばずに、右端に新しい列としてドッキングする。
        /// </summary>
        [TestMethod]
        public void Dock_ReturnsAPaneWhoseOriginalPlaceIsGone_ToTheRightEdge()
        {
            bool? avalonDockLeftItFloating = null, floatingAfterOurDock = null;
            int avalonDockCopies = -1, ourCopies = -1, floatingWindows = -1;
            bool? atRightEdge = null;
            OnSta(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();

                // (前提) AvalonDock の DockCommand だけでは戻らず、欄が二重になる
                var (manager1, anchorable1, window1) = SinglePaneLayout();
                try
                {
                    Item(manager1, anchorable1).FloatCommand.Execute(null); Settle(window1);
                    Item(manager1, anchorable1).DockCommand.Execute(null); Settle(window1);
                    avalonDockLeftItFloating = anchorable1.IsFloating;
                    avalonDockCopies = Copies(manager1, anchorable1);
                }
                finally { window1.Close(); }

                var (manager, anchorable, window) = SinglePaneLayout();
                try
                {
                    Item(manager, anchorable).FloatCommand.Execute(null); Settle(window);
                    Assert.IsTrue(PileDesign.Common.DockPaneCommands.Dock.CanExecute(Item(manager, anchorable)), "「ドッキング」が押せません");
                    PileDesign.Common.DockPaneCommands.Dock.Execute(Item(manager, anchorable)); Settle(window);
                    floatingAfterOurDock = anchorable.IsFloating;
                    ourCopies = Copies(manager, anchorable);
                    floatingWindows = manager.Layout.FloatingWindows.Count;
                    var last = manager.Layout.RootPanel.Children.LastOrDefault();
                    atRightEdge = last is LayoutAnchorablePane pane && pane.Children.Contains(anchorable);
                }
                finally { window.Close(); }
            });
            Assert.IsTrue(avalonDockLeftItFloating, "(前提) AvalonDock の DockCommand で戻るようになったなら、右端へ戻す受け皿が要るか見直す");
            Assert.AreEqual(2, avalonDockCopies, "(前提) AvalonDock が欄を二重に入れなくなったなら、DockCommand を避ける理由を見直す");
            Assert.IsFalse(floatingAfterOurDock, "「ドッキング」で戻りません");
            Assert.AreEqual(1, ourCopies, "欄が二重に入っています");
            Assert.AreEqual(0, floatingWindows, "空になった浮いた窓が残っています");
            Assert.IsTrue(atRightEdge, "右端に列としてドッキングしていません");
        }

        private static (DockingManager, LayoutAnchorable, Window) SinglePaneLayout()
        {
            var anchorable = new LayoutAnchorable { Title = "Dash", Content = new TextBlock { Text = "x" } };
            var panel = new LayoutPanel(new LayoutDocumentPane());
            panel.Children.Add(new LayoutAnchorablePaneGroup(new LayoutAnchorablePane(anchorable)));
            var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = panel } };
            var window = new Window { Width = 600, Height = 300, Content = manager, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); Settle(window);
            return (manager, anchorable, window);
        }

        private static LayoutAnchorableItem Item(DockingManager manager, LayoutAnchorable anchorable) =>
            (LayoutAnchorableItem)manager.GetLayoutItemFromModel(anchorable);

        /// <summary>レイアウトの中にこの欄がいくつ入っているか (浮いた窓も含む)。</summary>
        private static int Copies(DockingManager manager, LayoutAnchorable anchorable) =>
            manager.Layout.Descendents().OfType<LayoutAnchorable>().Count(a => ReferenceEquals(a, anchorable));

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

        private static void OnSta(System.Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            if (timedOut) Assert.Inconclusive("STA スレッドが時間内に終わりませんでした");
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }
    }
}
