using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Common
{
    /// <summary>
    /// DataGridでEnterキーによるセル間移動を改善するAttached Behavior
    /// </summary>
    public static class DataGridNavigationBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(DataGridNavigationBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject element, bool value)
            => element.SetValue(EnableProperty, value);

        public static bool GetEnable(DependencyObject element)
            => (bool)element.GetValue(EnableProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid dataGrid) return;

            if ((bool)e.NewValue)
            {
                dataGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            }
            else
            {
                dataGrid.PreviewKeyDown -= DataGrid_PreviewKeyDown;
            }
        }

        private static void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;

            // Enterキーのみ処理
            if (e.Key != Key.Enter)
                return;

            // 現在編集中のセルをコミット
            CommitCurrentEdit(dataGrid);

            // 次のセルに移動
            if (MoveToNextCell(dataGrid))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 現在編集中のセルをコミット
        /// </summary>
        private static void CommitCurrentEdit(DataGrid dataGrid)
        {
            // セル編集をコミット
            dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);

            // 行編集をコミット
            dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>
        /// 次のセルに移動（右方向、最後の列なら次の行の最初の列）
        /// </summary>
        private static bool MoveToNextCell(DataGrid dataGrid)
        {
            var currentCell = dataGrid.CurrentCell;
            if (currentCell.Item == null || currentCell.Column == null)
                return false;

            // 表示順の列リストを取得
            var columns = dataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            if (columns.Count == 0)
                return false;

            int currentColumnIndex = columns.IndexOf(currentCell.Column);
            if (currentColumnIndex < 0)
                return false;

            // 次の列を計算
            int nextColumnIndex = currentColumnIndex + 1;

            if (nextColumnIndex < columns.Count)
            {
                // 同じ行の次の列に移動
                dataGrid.CurrentCell = new DataGridCellInfo(currentCell.Item, columns[nextColumnIndex]);
                dataGrid.BeginEdit();
                return true;
            }
            else
            {
                // 次の行の最初の列に移動
                int currentRowIndex = dataGrid.Items.IndexOf(currentCell.Item);
                if (currentRowIndex >= 0 && currentRowIndex < dataGrid.Items.Count - 1)
                {
                    var nextItem = dataGrid.Items[currentRowIndex + 1];
                    dataGrid.CurrentCell = new DataGridCellInfo(nextItem, columns[0]);
                    dataGrid.BeginEdit();
                    return true;
                }
                else
                {
                    // 最後の行の最後の列 → DataGrid外の次のコントロールに移動
                    var request = new TraversalRequest(FocusNavigationDirection.Next);
                    dataGrid.MoveFocus(request);
                    return true;
                }
            }
        }
    }
}
