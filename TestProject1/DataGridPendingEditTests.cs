using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 表の行の編集を終えないままウィンドウを閉じても、開き直せること。
    ///
    /// <para>セルを Tab / Esc で抜けると、セルの編集は終わるが<b>行の編集</b> (コレクションの EditItem) は続いている。
    /// そのまま閉じると、モデルのコレクションの既定のビューが「編集中」のまま残り、
    /// 同じコレクションを表に出す次のウィンドウが組み立ての途中で落ちる
    /// (「'DeferRefresh' は、AddNew トランザクションまたは EditItem トランザクションの実行中は許可されません」)。
    /// 2026-10-08 の実機確認で、群杭沈下ウィンドウを開き直したときに起きた。</para>
    /// </summary>
    [TestClass]
    public class DataGridPendingEditTests
    {
        private static T? FindVisual<T>(System.Windows.DependencyObject root, System.Func<T, bool> pred) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t && pred(t)) return t;
                if (FindVisual(child, pred) is { } found) return found;
            }
            return null;
        }

        /// <summary>
        /// 矩形荷重の表の「出所」列は、杭に紐付く矩形がある荷重タイプ (個別矩形系) だけで出す。
        /// 列はビジュアルツリーの外にあるので、表示の切り替えが実際に効いているかを描いて確かめる。
        /// </summary>
        [TestMethod]
        public void TheSourceColumn_ShowsOnlyForPileLinkedRects()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var pgs = model.PileGroupSettlement;
            var seen = new System.Collections.Generic.Dictionary<string, System.Windows.Visibility>();

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                var w = new PileDesign.Views.GroupSettlementWindow
                {
                    DataContext = vm, ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                w.Show();
                try
                {
                    w.GroupPileTabControl.SelectedItem = w.TabItemLoadNonBeam;
                    w.UpdateLayout();
                    var grid = FindVisual<DataGrid>(w, g => g.Columns.Any(c => (c.Header as string) == "出所"));
                    Assert.IsNotNull(grid, "前提: 出所の列がある表が見つかりません");
                    var column = grid.Columns.First(c => (c.Header as string) == "出所");
                    foreach (var type in new[] { "個別十字", "任意矩形", "個別矩形", "個別矩形（基礎梁考慮）" })
                    {
                        pgs.LoadingType = type;
                        w.UpdateLayout();
                        seen[type] = column.Visibility;
                    }
                }
                finally { w.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.AreEqual(System.Windows.Visibility.Collapsed, seen["個別十字"], "個別十字で空の出所列が出ています");
            Assert.AreEqual(System.Windows.Visibility.Collapsed, seen["任意矩形"]);
            Assert.AreEqual(System.Windows.Visibility.Visible, seen["個別矩形"]);
            Assert.AreEqual(System.Windows.Visibility.Visible, seen["個別矩形（基礎梁考慮）"]);
        }

        [DataTestMethod]
        [DataRow("Tab", DisplayName = "セルを確定して (Tab) 閉じる")]
        [DataRow("Esc", DisplayName = "セルを取り消して (Esc) 閉じる")]
        public void TheSettlementWindow_ReopensAfterClosingWithARowEditPending(string leaveCellBy)
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var rects = model.PileGroupSettlement.RectLoads;
            Assert.IsTrue(rects.Count > 0, "前提: 矩形荷重がある");
            bool rowEditWasPending = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                PileDesign.Views.GroupSettlementWindow Open()
                {
                    var w = new PileDesign.Views.GroupSettlementWindow
                    {
                        DataContext = vm, ShowActivated = false,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                    };
                    w.Show();
                    w.GroupPileTabControl.SelectedItem = w.TabItemLoadNonBeam;
                    w.UpdateLayout();
                    return w;
                }

                var first = Open();
                var grid = FindVisual<DataGrid>(first, g => ReferenceEquals(g.ItemsSource, rects));
                Assert.IsNotNull(grid, "前提: 矩形荷重の表が見つかりません");
                var dxColumn = grid.Columns.OfType<DataGridTextColumn>()
                    .First(c => (c.Binding as System.Windows.Data.Binding)?.Path.Path == "DX");
                grid.CurrentCell = new DataGridCellInfo(rects[0], dxColumn);
                Assert.IsTrue(grid.BeginEdit(), "前提: 寸法のセルは編集できる");
                if (leaveCellBy == "Tab") grid.CommitEdit(DataGridEditingUnit.Cell, true);
                else grid.CancelEdit(DataGridEditingUnit.Cell);

                var view = (System.ComponentModel.IEditableCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(rects);
                rowEditWasPending = view.IsEditingItem;
                first.Close();
                Assert.IsFalse(view.IsEditingItem, "閉じたのに、行の編集が残っています");

                var second = Open();   // 以前はここで DeferRefresh の例外で落ちた
                second.Close();
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.IsTrue(rowEditWasPending, "前提: セルを抜けたあとも行の編集が続いている (再現の条件が崩れています)");
        }
    }
}
