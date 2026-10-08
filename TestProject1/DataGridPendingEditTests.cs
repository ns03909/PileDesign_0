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
