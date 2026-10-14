using PileDesign.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PileDesign.Views
{
    /// <summary>
    /// 杭配置の表の並べ替え (上へ・下へのボタン、Alt+↑/↓、行見出しのドラッグ)。杭 No. は表の並び順なので、
    /// 並べ替えは <see cref="MainWindowViewModel.ReorderPileLayout"/> が番号の付け直しまで行う。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>ドラッグで運ぶデータの名前 (ファイルのドロップと区別する)。</summary>
        private const string PileRowsDragFormat = "PileDesign.PileLayoutRows";

        private Point? _pileRowDragStart;
        /// <summary>選択済みの行の見出しを押したときは選択を変えずにおき、ドラッグせずに離したらその行だけを選ぶ (エクスプローラーと同じ)。</summary>
        private DataGridRow? _pileRowPendingClick;
        /// <summary>ドラッグ中に、落とす位置を示す線。</summary>
        private PileRowInsertionAdorner? _pileRowInsertion;

        /// <summary>
        /// 並べ替えの前に、杭配置の一覧を表示している表 (配置・軸力・前後方杭) の入力中の値を確定する。確定できなければ並べ替えない。
        /// 以前は確定の成否を見ずに並べ替えたので、入力の誤りで確定できない値と並べ替えの履歴が食い違うことがあった。
        /// </summary>
        private bool CommitPileTablesBeforeReorder()
        {
            foreach (var grid in new DataGrid?[] { DataGridPileLayout, DataGridPileAxialForce, DataGridIsFrontPile })
            {
                if (grid == null) continue;
                if (grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true)) continue;
                if (DataContext is MainWindowViewModel vm)
                    vm.StatusMessage = "入力中の値を確定できないため、並べ替えません (値を直すか Esc で取り消してください)。";
                return false;
            }
            return true;
        }

        /// <summary>選択した杭を 1 行上 (<paramref name="up"/>) か下へ。ボタンと Alt+↑/↓ の共通の入口。</summary>
        private void MoveSelectedPileRows(bool up)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (!CommitPileTablesBeforeReorder()) return;
            (up ? vm.MovePileLayoutUpCommand : vm.MovePileLayoutDownCommand).Execute(null);
            BringSelectedPileRowIntoView();
        }

        private void MovePileRowUp_Click(object sender, RoutedEventArgs e) => MoveSelectedPileRows(up: true);
        private void MovePileRowDown_Click(object sender, RoutedEventArgs e) => MoveSelectedPileRows(up: false);

        private void DataGridPileLayout_ReorderKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.System || Keyboard.Modifiers != ModifierKeys.Alt) return;
            if (e.SystemKey != Key.Up && e.SystemKey != Key.Down) return;
            e.Handled = true;
            MoveSelectedPileRows(up: e.SystemKey == Key.Up);
        }

        private void DataGridPileLayout_ReorderMouseDown(object sender, MouseButtonEventArgs e)
        {
            _pileRowDragStart = null;
            _pileRowPendingClick = null;
            var header = FindAncestor<DataGridRowHeader>(e.OriginalSource as DependencyObject);
            var row = FindAncestor<DataGridRow>(header);
            if (header == null || row == null) return;
            _pileRowDragStart = e.GetPosition(DataGridPileLayout);
            if (row.IsSelected && Keyboard.Modifiers == ModifierKeys.None && DataGridPileLayout.SelectedItems.Count > 1)
            {
                _pileRowPendingClick = row;
                e.Handled = true;   // 複数選択を保ったままドラッグできるように
            }
        }

        private void DataGridPileLayout_ReorderMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_pileRowPendingClick is { } row)
            {
                DataGridPileLayout.SelectedItems.Clear();
                row.IsSelected = true;
            }
            _pileRowDragStart = null;
            _pileRowPendingClick = null;
        }

        private void DataGridPileLayout_ReorderMouseMove(object sender, MouseEventArgs e)
        {
            if (_pileRowDragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(DataGridPileLayout) - start;
            if (Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance
                && Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _pileRowDragStart = null;
            _pileRowPendingClick = null;
            if (DataGridPileLayout.SelectedItems.Count == 0) return;
            if (!CommitPileTablesBeforeReorder()) return;
            try
            {
                DragDrop.DoDragDrop(DataGridPileLayout, new DataObject(PileRowsDragFormat, true), DragDropEffects.Move);
            }
            finally { HidePileRowInsertion(); }
        }

        private void DataGridPileLayout_ReorderDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(PileRowsDragFormat)) return;   // ファイルのドロップは画面全体の処理へ
            int index = DropIndexAt(e.GetPosition(DataGridPileLayout));
            e.Effects = index < 0 ? DragDropEffects.None : DragDropEffects.Move;
            ShowPileRowInsertion(index);
            e.Handled = true;
        }

        private void DataGridPileLayout_ReorderDragLeave(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(PileRowsDragFormat)) HidePileRowInsertion();
        }

        private void DataGridPileLayout_ReorderDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(PileRowsDragFormat)) return;
            e.Handled = true;
            HidePileRowInsertion();
            int index = DropIndexAt(e.GetPosition(DataGridPileLayout));
            if (index < 0 || DataContext is not MainWindowViewModel vm) return;
            vm.MoveSelectedPilesTo(index);
            BringSelectedPileRowIntoView();
        }

        /// <summary>
        /// 落とした位置の挿入先 (0 始まり、並べ替える前の位置)。行の上半分ならその行の前、下半分なら次の行の前、
        /// 最後の行より下の余白なら末尾。それ以外 (列見出し・スクロールバー・表の外) は -1 (落とせない)。
        /// 以前は行でなければどこでも末尾に動かしたので、列見出しやスクロールバーに落としても末尾へ移った。
        /// </summary>
        private int DropIndexAt(Point p)
        {
            var grid = DataGridPileLayout;
            if (p.X < 0 || p.Y < 0 || p.X > grid.ActualWidth || p.Y > grid.ActualHeight) return -1;
            var hit = grid.InputHitTest(p) as DependencyObject;
            if (hit == null || FindAncestor<DataGridColumnHeader>(hit) != null || FindAncestor<ScrollBar>(hit) != null) return -1;
            if (FindAncestor<DataGridRow>(hit) is { } row)
            {
                int index = row.GetIndex();
                return grid.TranslatePoint(p, row).Y > row.ActualHeight / 2 ? index + 1 : index;
            }
            int n = grid.Items.Count;
            if (n == 0) return 0;
            if (grid.ItemContainerGenerator.ContainerFromIndex(n - 1) is DataGridRow last && last.IsVisible
                && p.Y >= last.TranslatePoint(new Point(0, last.ActualHeight), grid).Y)
                return n;
            return -1;
        }

        /// <summary>
        /// 表の下の余白 (表を入れている枠のうち、最後の行より下) にも落とせるようにする。落とすと末尾。
        /// 杭配置の表は行の数だけの高さ (VerticalAlignment=Top) なので、その下の余白は表の外にあり、表のドラッグの受け口に
        /// 届かなかった (2026-10-14 の実機確認。ヘルプの「行の下の余白に落とすと末尾」のとおりに動かなかった)。
        /// 枠の背景が無いとドラッグを受けないので、透明の背景を付ける。
        /// </summary>
        private void HookPileRowDropBelowTable()
        {
            if (DataGridPileLayout.Parent is not Panel frame) return;
            frame.Background ??= Brushes.Transparent;
            frame.AllowDrop = true;
            frame.DragEnter += PileLayoutFrame_DragOver;
            frame.DragOver += PileLayoutFrame_DragOver;
            frame.DragLeave += DataGridPileLayout_ReorderDragLeave;
            frame.Drop += PileLayoutFrame_Drop;
        }

        /// <summary>表の下の余白か (表の横幅の中で、表の下端より下)。表の上のドラッグは表の受け口が先に処理する。</summary>
        private bool IsBelowPileTable(DragEventArgs e)
        {
            var p = e.GetPosition(DataGridPileLayout);
            return p.Y >= DataGridPileLayout.ActualHeight && p.X >= 0 && p.X <= DataGridPileLayout.ActualWidth;
        }

        private void PileLayoutFrame_DragOver(object sender, DragEventArgs e)
        {
            if (e.Handled || !e.Data.GetDataPresent(PileRowsDragFormat)) return;
            bool below = IsBelowPileTable(e);
            e.Effects = below ? DragDropEffects.Move : DragDropEffects.None;
            ShowPileRowInsertion(below ? DataGridPileLayout.Items.Count : -1);
            e.Handled = true;
        }

        private void PileLayoutFrame_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled || !e.Data.GetDataPresent(PileRowsDragFormat)) return;
            e.Handled = true;
            HidePileRowInsertion();
            if (!IsBelowPileTable(e) || DataContext is not MainWindowViewModel vm) return;
            vm.MoveSelectedPilesTo(DataGridPileLayout.Items.Count);
            BringSelectedPileRowIntoView();
        }

        /// <summary>挿入位置 (<paramref name="index"/> 行目の上端、末尾なら最後の行の下端) に線を出す。-1 なら消す。</summary>
        private void ShowPileRowInsertion(int index)
        {
            var grid = DataGridPileLayout;
            double? y = null;
            if (index >= 0 && index < grid.Items.Count && grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row && row.IsVisible)
                y = row.TranslatePoint(new Point(0, 0), grid).Y;
            else if (index >= 0 && index == grid.Items.Count && index > 0
                     && grid.ItemContainerGenerator.ContainerFromIndex(index - 1) is DataGridRow last && last.IsVisible)
                y = last.TranslatePoint(new Point(0, last.ActualHeight), grid).Y;
            if (y == null) { HidePileRowInsertion(); return; }
            if (_pileRowInsertion == null)
            {
                var layer = AdornerLayer.GetAdornerLayer(grid);
                if (layer == null) return;
                _pileRowInsertion = new PileRowInsertionAdorner(grid);
                layer.Add(_pileRowInsertion);
            }
            _pileRowInsertion.Y = y.Value;
        }

        private void HidePileRowInsertion()
        {
            if (_pileRowInsertion == null) return;
            AdornerLayer.GetAdornerLayer(DataGridPileLayout)?.Remove(_pileRowInsertion);
            _pileRowInsertion = null;
        }

        /// <summary>行を落とす位置を示す横線。</summary>
        private sealed class PileRowInsertionAdorner(UIElement adorned) : Adorner(adorned)
        {
            private double _y;
            public double Y
            {
                get => _y;
                set { if (_y != value) { _y = value; InvalidateVisual(); } }
            }

            protected override void OnRender(DrawingContext dc)
            {
                var brush = SystemColors.HighlightBrush;
                var pen = new Pen(brush, 2);
                double width = AdornedElement.RenderSize.Width;
                dc.DrawLine(pen, new Point(0, Y), new Point(width, Y));
                dc.DrawEllipse(brush, null, new Point(3, Y), 3, 3);
            }
        }

        private void BringSelectedPileRowIntoView()
        {
            if (DataGridPileLayout.SelectedItems.Count > 0)
                DataGridPileLayout.ScrollIntoView(DataGridPileLayout.SelectedItems[0]);
        }

        /// <summary>
        /// 並べ替えが終わったら 1 回だけ、表の選択を杭の選択 (IsSelected) に合わせ、行見出しの番号を振り直し、描き直す。
        /// 並べ替えは一覧の Move を杭の数だけ繰り返すので、以前は Move のたびに 3 つの表の選択を作り直し、
        /// 描き直し、杭要素分割の前なら土層-杭セットまで作り直していた (杭が多いと重い)。
        /// </summary>
        private void OnPileLayoutReordered(object? sender, EventArgs e)
        {
            SyncPileGridsSelectionFromModel();
            RenumberPileRowHeadersLater();
        }

        /// <summary>
        /// 行見出しの番号 (= 杭 No.) を振り直す。見出しは行を作ったときにしか付かないので、並べ替え (Move) のあとは
        /// 動かなかった行の見出しが古い番号のまま残る。杭配置の一覧を表示している表すべてで行う。
        /// </summary>
        private void RenumberPileRowHeadersLater()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var grid in new DataGrid?[] { DataGridPileLayout, DataGridPileAxialForce, DataGridIsFrontPile })
                {
                    if (grid == null) continue;
                    for (int i = 0; i < grid.Items.Count; i++)
                        if (grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow r)
                            r.Header = (i + 1).ToString();
                }
            }));
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T)
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
