using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using PileDesign.Services;

namespace PileDesign.Common
{
    public class EnhancedDataGrid : DataGrid
    {
        /// <summary>ペースト完了時に発火するイベント</summary>
        public event EventHandler PasteCompleted;

        /// <summary>
        /// 一括ペースト (塗り潰し / 複数セル) の書込ループ実行中だけ true。
        /// モデル側で「1 件変更ごとに重い派生プロパティを同期再評価する」通知
        /// (例: PileLayoutDataItem.Z 変更時の SoilPile 再評価) を抑制し、ペースト完了後の
        /// 1 回の再評価・再生成 (多くはデバウンス) にまとめて O(N×列数) の劣化を防ぐためのヒント。
        /// ペースト処理は UI スレッド上で逐次実行されるため static で問題ない。
        /// </summary>
        public static bool IsBulkEditing { get; private set; }

        public EnhancedDataGrid()
        {
            // Excel貼付け互換のためヘッダ除外コピー
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;

            // セル選択をデフォルト化（複数セル選択も許可）
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            SelectionMode = DataGridSelectionMode.Extended;

            // 縦スクロールバーを常時表示：Auto だとスクロールバー出現時にデータ行幅だけ縮んでヘッダーとずれる
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Visible);

            // 列仮想化を無効化：FrozenColumnCount との組合せで横スクロール時にガタつき・ヘッダー欠けが発生するため
            EnableColumnVirtualization = false;

            // ヘッダー高さを全列の最大に固定（列仮想化ON時の横スクロールでのガタつき防止）
            Loaded += OnDataGridLoaded;
        }

        private void OnDataGridLoaded(object sender, RoutedEventArgs e)
        {
            FixColumnHeaderHeight();
        }

        /// <summary>
        /// 全カラムヘッダーを計測し、最大高さに ColumnHeaderHeight を固定する。
        /// 列仮想化が有効な場合、表示中のカラムだけで高さが決まるため
        /// 横スクロール時にヘッダー行がガタつく問題を防止する。
        /// </summary>
        private void FixColumnHeaderHeight()
        {
            // XAML で ColumnHeaderHeight が明示指定されている場合はそれを尊重
            if (!double.IsNaN(ColumnHeaderHeight))
                return;

            double maxContentHeight = 0;

            foreach (var column in Columns)
            {
                double measuredHeight = 0;

                if (column.Header is UIElement element)
                {
                    // StackPanel 等の UIElement ヘッダーを計測
                    element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    measuredHeight = element.DesiredSize.Height;
                }
                else if (column.Header is string text && !string.IsNullOrEmpty(text))
                {
                    // 文字列ヘッダーは一時 TextBlock で計測
                    var textBlock = new TextBlock { Text = text };
                    textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    measuredHeight = textBlock.DesiredSize.Height;
                }

                if (measuredHeight > maxContentHeight)
                    maxContentHeight = measuredHeight;
            }

            if (maxContentHeight > 0)
            {
                // DataGridColumnHeader のパディング・ボーダー分を加算
                ColumnHeaderHeight = maxContentHeight + 10;
            }
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            // DataGrid内のどこをクリックしても（ヘッダー・全選択ボタン含む）
            // キーボードフォーカスを確保し、Ctrl+C等のキー操作を有効にする
            if (!IsKeyboardFocusWithin)
                Focus();
            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Ctrl+C でコピー（DataGridTemplateColumn対応）
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (TryCopyToClipboard())
                {
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+A で全セル選択
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SelectAll();
                e.Handled = true;
                return;
            }

            // Ctrl+V で貼り付け
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (TryPasteFromClipboard())
                {
                    PasteCompleted?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }
            }

            bool ctrlOrAlt = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != ModifierKeys.None;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            // Space: アクティブセル内の CheckBox / Button を起動 (Excel 風)。
            // 編集中は通常のスペース入力に委ねる。テキストセル等で内部コントロールがない場合は素通し。
            if (!ctrlOrAlt && !shift && e.Key == Key.Space && !IsEditing())
            {
                if (TryInvokeCurrentCellActionable())
                {
                    e.Handled = true;
                    return;
                }
            }

            // 矢印キーでアクティブセル (CurrentCell) を移動 (Excel 風)。
            //  - 編集中なら CommitEdit (バリデーション失敗時はその場に留まる)
            //  - 読み取り専用や SelectionUnit=FullRow のグリッドでも一律にセル単位移動
            //  - Shift+矢印 で範囲選択拡張
            if (!ctrlOrAlt
                && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                if (IsEditing() && !TryCommitCurrentEdit())
                {
                    e.Handled = true;   // バリデーション失敗 → 移動せず編集を継続
                    return;
                }
                if (TryNavigateCurrentCell(e.Key, shift))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Enter: 編集確定 + 下に移動 (Excel)。最下行なら右列の最上行へ。Shift+Enter は逆方向。
            if (!ctrlOrAlt && e.Key == Key.Enter)
            {
                if (IsEditing() && !TryCommitCurrentEdit())
                {
                    e.Handled = true;
                    return;
                }
                if (TryNavigateExcelStyle(vertical: true, reverse: shift))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Tab: 編集確定 + 右に移動 (Excel)。最右列なら次行の左端へ。Shift+Tab は逆方向。
            if (!ctrlOrAlt && e.Key == Key.Tab)
            {
                if (IsEditing() && !TryCommitCurrentEdit())
                {
                    e.Handled = true;
                    return;
                }
                if (TryNavigateExcelStyle(vertical: false, reverse: shift))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Delete: 選択セルの値をクリア (Excel 風)。編集中は通常の文字削除に委ねる。
            // クリアできるセルが無ければ素通しして既定動作 (行削除等) を妨げない。
            if (!ctrlOrAlt && !shift && e.Key == Key.Delete && !IsEditing())
            {
                if (TryClearSelectedCells())
                {
                    PasteCompleted?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        /// <summary>
        /// 未編集セルで文字キーを打ったとき、WPF 既定では編集モードへ遷移する際に
        /// 既存値が select-all され 1 打目が消費されてしまう (1 打目が入力として残らない)。
        /// ここで明示的に編集を開始し、打鍵文字を編集 TextBox に直接流し込むことで
        /// 「1 打目から入力データとして受け付ける」(Excel 風の上書き入力) を実現する。
        /// </summary>
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if (TryBeginEditWithFirstChar(e))
            {
                e.Handled = true;
                return;
            }
            base.OnPreviewTextInput(e);
        }

        private bool TryBeginEditWithFirstChar(TextCompositionEventArgs e)
        {
            // 既に編集中なら通常入力 (TextBox 側の NumericInput フィルタ等) に委ねる
            if (IsEditing()) return false;
            if (string.IsNullOrEmpty(e.Text)) return false;

            char ch = e.Text[0];
            if (char.IsControl(ch)) return false;   // ESC / Backspace 等は対象外

            if (CurrentCell.Column == null || CurrentCell.Item == null) return false;
            if (IsReadOnly) return false;

            // テキスト/バインド列のみ対象。CheckBox / ComboBox / Template 列は
            // それぞれ独自の入力処理 (トグル・型先頭検索等) に任せる。
            if (CurrentCell.Column is not DataGridBoundColumn col) return false;
            if (col is DataGridCheckBoxColumn) return false;
            if (col.IsReadOnly) return false;

            if (!BeginEdit()) return false;

            var cell = TryFindDataGridCell(CurrentCell);
            var tb = cell != null ? FindVisualChild<TextBox>(cell) : null;
            if (tb == null) return false;   // 編集要素が TextBox でない → 既定動作にフォールバック

            // BeginEdit 直後は既存値が全選択されている。打鍵文字で置き換える (上書き入力)。
            tb.Text = e.Text;
            tb.CaretIndex = tb.Text.Length;
            tb.Focus();
            return true;
        }

        /// <summary>
        /// 現在編集中のセルを確定する。バリデーション/コンバータエラーが発生した場合は
        /// CommitEdit が false を返すので、その時点で false を返して呼び出し側に編集継続を指示する。
        /// </summary>
        private bool TryCommitCurrentEdit()
        {
            try
            {
                // セル単位 → 行単位 の順に commit (両方が編集状態に入っていることがあるため)
                bool cellOk = CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
                if (!cellOk) return false;
                bool rowOk = CommitEdit(DataGridEditingUnit.Row, exitEditingMode: false);
                return rowOk;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Excel ライクなナビゲーション (Enter=下方向 / Tab=右方向)。
        ///  vertical=true  : 主軸=行方向 (Enter)。端に到達したら次列の先頭にラップ。
        ///  vertical=false : 主軸=列方向 (Tab)。端に到達したら次行の先頭にラップ。
        ///  reverse=true で Shift+Enter / Shift+Tab の逆方向。
        /// </summary>
        private bool TryNavigateExcelStyle(bool vertical, bool reverse)
        {
            if (Items == null || Items.Count == 0) return false;
            var visibleCols = Columns.Where(c => c.Visibility == Visibility.Visible)
                                     .OrderBy(c => c.DisplayIndex)
                                     .ToList();
            if (visibleCols.Count == 0) return false;

            int rowIdx = 0;
            int colPos = 0;
            if (CurrentCell.Item != null && CurrentCell.Column != null)
            {
                rowIdx = Math.Max(0, Items.IndexOf(CurrentCell.Item));
                int idx = visibleCols.FindIndex(c => c.DisplayIndex == CurrentCell.Column.DisplayIndex);
                if (idx >= 0) colPos = idx;
            }

            int newRow = rowIdx;
            int newCol = colPos;

            if (vertical)
            {
                if (!reverse)
                {
                    // Enter: 下へ。最下行なら右列の最上行へ。
                    if (rowIdx + 1 < Items.Count) newRow = rowIdx + 1;
                    else if (colPos + 1 < visibleCols.Count) { newRow = 0; newCol = colPos + 1; }
                    else return false; // 右下端: これ以上進めない
                }
                else
                {
                    // Shift+Enter: 上へ。最上行なら左列の最下行へ。
                    if (rowIdx > 0) newRow = rowIdx - 1;
                    else if (colPos > 0) { newRow = Items.Count - 1; newCol = colPos - 1; }
                    else return false;
                }
            }
            else
            {
                if (!reverse)
                {
                    // Tab: 右へ。最右列なら次行の左端へ。
                    if (colPos + 1 < visibleCols.Count) newCol = colPos + 1;
                    else if (rowIdx + 1 < Items.Count) { newCol = 0; newRow = rowIdx + 1; }
                    else return false;
                }
                else
                {
                    // Shift+Tab: 左へ。最左列なら前行の右端へ。
                    if (colPos > 0) newCol = colPos - 1;
                    else if (rowIdx > 0) { newCol = visibleCols.Count - 1; newRow = rowIdx - 1; }
                    else return false;
                }
            }

            if (newRow == rowIdx && newCol == colPos) return false;

            var newItem = Items[newRow];
            var newColumn = visibleCols[newCol];
            var newInfo = new DataGridCellInfo(newItem, newColumn);

            UnselectAllCells();
            CurrentCell = newInfo;
            if (SelectionUnit != DataGridSelectionUnit.FullRow)
            {
                if (!SelectedCells.Contains(newInfo)) SelectedCells.Add(newInfo);
            }
            else
            {
                SelectedItem = newItem;
            }

            ScrollIntoView(newItem, newColumn);
            var cell = TryFindDataGridCell(newInfo);
            cell?.Focus();
            return true;
        }

        /// <summary>
        /// 現在のアクティブセル内に CheckBox / Button / ComboBox があれば起動する。
        /// CheckBox: IsChecked をトグル (Three-state なら null→true→false→null の順)。
        /// Button:   Click イベントを発火 (Command も実行される)。
        /// ComboBox: ドロップダウンを開く (IsDropDownOpen = true)。
        /// </summary>
        private bool TryInvokeCurrentCellActionable()
        {
            if (CurrentCell.Column == null || CurrentCell.Item == null) return false;
            var cell = TryFindDataGridCell(CurrentCell);
            if (cell == null) return false;

            var control = FindInteractiveChild(cell);
            if (control == null) return false;
            if (!control.IsEnabled) return false;

            if (control is CheckBox cb)
            {
                // IsThreeState を考慮した順送り
                if (cb.IsThreeState)
                {
                    cb.IsChecked = cb.IsChecked switch
                    {
                        null  => true,
                        true  => false,
                        false => null,
                    };
                }
                else
                {
                    cb.IsChecked = !(cb.IsChecked ?? false);
                }
                return true;
            }

            if (control is Button btn)
            {
                // Automation 経由で Click を起動 (Command バインドも適切に発火する)
                var peer = new ButtonAutomationPeer(btn);
                if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
                {
                    invoker.Invoke();
                    return true;
                }
            }

            if (control is ComboBox combo)
            {
                // フォーカスを移しておかないと、ドロップダウン内のキー操作 (↑↓Enter で選択確定) が
                // データグリッド側に渡って意図しない動きになることがある。
                combo.Focus();
                combo.IsDropDownOpen = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// セル内を Visual Tree でたどり、最初に見つかった IsEnabled な CheckBox / Button / ComboBox を返す。
        /// 入れ子の StackPanel / Border などは透過してたどる。
        /// </summary>
        private static Control FindInteractiveChild(DependencyObject root)
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is CheckBox cb) return cb;
                if (child is Button btn) return btn;
                if (child is ComboBox combo) return combo;
                var deeper = FindInteractiveChild(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private bool IsEditing()
        {
            // CurrentCell が編集モードかどうか判定。DataGridCell.IsEditing で確認。
            if (CurrentCell.Column == null || CurrentCell.Item == null) return false;
            var cell = TryFindDataGridCell(CurrentCell);
            return cell != null && cell.IsEditing;
        }

        /// <summary>
        /// Items.Refresh() / ICollectionView.Refresh() でセルコンテナが再生成されると、
        /// キーボードフォーカスが DataGrid の外へ外れ、矢印キーナビゲーション (OnPreviewKeyDown) が
        /// 効かなくなる (マウスクリックで初めて復帰する)。ペースト直後に対象セルへフォーカスを戻し、
        /// キーボード操作を継続できるようにする。コンテナ再生成はレイアウト完了後に確定するため
        /// Dispatcher(Loaded) で遅延実行する。
        /// </summary>
        private void RestoreKeyboardFocusAfterRefresh(DataGridCellInfo anchor)
        {
            if (anchor.Item == null || anchor.Column == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    CurrentCell = anchor;
                    ScrollIntoView(anchor.Item, anchor.Column);
                    var cell = TryFindDataGridCell(anchor);
                    if (cell != null)
                        cell.Focus();
                    else if (!IsKeyboardFocusWithin)
                        Focus();   // セルが見つからない場合はグリッド本体へフォーカスを戻す
                }
                catch { /* フォーカス復帰の失敗は致命的でないため無視 */ }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private DataGridCell TryFindDataGridCell(DataGridCellInfo info)
        {
            if (info.Column == null || info.Item == null) return null;
            var row = ItemContainerGenerator.ContainerFromItem(info.Item) as DataGridRow;
            if (row == null) return null;
            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;
            return presenter.ItemContainerGenerator.ContainerFromIndex(info.Column.DisplayIndex) as DataGridCell;
        }

        /// <summary>
        /// CurrentCell を矢印キーの方向に 1 つ移動する。
        /// 表示順 (DisplayIndex) と Items の順序で隣接セルを決定する。
        /// shift=true の場合は SelectedCells に追加 (Excel 風範囲拡張)。
        /// </summary>
        private bool TryNavigateCurrentCell(Key key, bool shift)
        {
            if (Items == null || Items.Count == 0) return false;
            if (Columns == null || Columns.Count == 0) return false;

            // 現在のセル位置を取得 (なければ先頭セルを起点)
            int rowIdx = -1;
            int displayIdx = 0;
            if (CurrentCell.Item != null && CurrentCell.Column != null)
            {
                rowIdx = Items.IndexOf(CurrentCell.Item);
                displayIdx = CurrentCell.Column.DisplayIndex;
            }
            if (rowIdx < 0) rowIdx = 0;

            // 表示可能カラムだけ抜き出し
            var visibleCols = Columns.Where(c => c.Visibility == Visibility.Visible)
                                     .OrderBy(c => c.DisplayIndex)
                                     .ToList();
            if (visibleCols.Count == 0) return false;

            // 現在の表示列ポジションを索引化
            int curColPos = visibleCols.FindIndex(c => c.DisplayIndex == displayIdx);
            if (curColPos < 0) curColPos = 0;

            int newRow = rowIdx;
            int newColPos = curColPos;

            switch (key)
            {
                case Key.Left:  newColPos = Math.Max(0, curColPos - 1); break;
                case Key.Right: newColPos = Math.Min(visibleCols.Count - 1, curColPos + 1); break;
                case Key.Up:    newRow = Math.Max(0, rowIdx - 1); break;
                case Key.Down:  newRow = Math.Min(Items.Count - 1, rowIdx + 1); break;
            }

            // 移動なし (端) なら未処理
            if (newRow == rowIdx && newColPos == curColPos) return false;

            var newItem = Items[newRow];
            var newCol = visibleCols[newColPos];
            var newInfo = new DataGridCellInfo(newItem, newCol);

            // 範囲拡張でない場合は単一選択にクリア
            if (!shift) UnselectAllCells();

            CurrentCell = newInfo;
            if (SelectionUnit != DataGridSelectionUnit.FullRow)
            {
                if (!SelectedCells.Contains(newInfo)) SelectedCells.Add(newInfo);
            }
            else
            {
                SelectedItem = newItem;
            }

            ScrollIntoView(newItem, newCol);
            // セル DOM が生成済みならフォーカスを移す (FullRow + Focusable=False のセルは無視される)
            var newCell = TryFindDataGridCell(newInfo);
            newCell?.Focus();
            return true;
        }

        private bool TryCopyToClipboard()
        {
            try
            {
                if (SelectedCells == null || SelectedCells.Count == 0) return false;

                // Items → 行インデックスの高速逆引きテーブル
                var itemIndexMap = new Dictionary<object, int>();
                for (int i = 0; i < Items.Count; i++)
                {
                    var item = Items[i];
                    if (item != null && !itemIndexMap.ContainsKey(item))
                        itemIndexMap[item] = i;
                }

                // 選択セルを行・列順に整理
                var cellsByRow = new SortedDictionary<int, SortedDictionary<int, string>>();

                foreach (var cellInfo in SelectedCells)
                {
                    if (cellInfo.Item == null || cellInfo.Column == null) continue;

                    if (!itemIndexMap.TryGetValue(cellInfo.Item, out int rowIndex)) continue;
                    int colDisplayIndex = cellInfo.Column.DisplayIndex;

                    string cellText = GetCellText(cellInfo.Item, cellInfo.Column);

                    // DataGridTemplateColumn の場合、VisualTreeから表示テキストを取得
                    if (string.IsNullOrEmpty(cellText) && cellInfo.Column is DataGridTemplateColumn)
                    {
                        cellText = GetCellTextFromVisualTree(rowIndex, cellInfo.Column);
                    }

                    if (!cellsByRow.ContainsKey(rowIndex))
                        cellsByRow[rowIndex] = new SortedDictionary<int, string>();
                    cellsByRow[rowIndex][colDisplayIndex] = cellText;
                }

                if (cellsByRow.Count == 0) return false;

                // 選択に含まれる列のDisplayIndexを収集（ヘッダー行用）
                var selectedColIndices = new SortedSet<int>();
                foreach (var row in cellsByRow.Values)
                    foreach (var colIdx in row.Keys)
                        selectedColIndices.Add(colIdx);

                var displayOrderedCols = Columns.OrderBy(c => c.DisplayIndex).ToList();

                // TSV形式で組み立て
                // 注意: 行番号列は出力しない。
                // 出力すると、自分自身への貼り付け時に行番号 "1", "2"... が
                // データ行先頭セルへ列ズレして書き込まれる (TryPasteFromClipboard は
                // 1 行目のみ非数値ヘッダー行として剥がし、データ行の行番号は剥がさない)。
                var sb = new StringBuilder();

                // ヘッダー行: 各列のヘッダーテキスト (タブ区切り)
                bool first = true;
                foreach (var colIdx in selectedColIndices)
                {
                    if (!first) sb.Append('\t');
                    first = false;
                    if (colIdx >= 0 && colIdx < displayOrderedCols.Count)
                        sb.Append(GetColumnHeaderText(displayOrderedCols[colIdx]));
                }
                sb.AppendLine();

                // データ行: セル値のみ (タブ区切り)
                foreach (var (_, rowData) in cellsByRow)
                {
                    first = true;
                    foreach (var colIdx in selectedColIndices)
                    {
                        if (!first) sb.Append('\t');
                        first = false;
                        if (rowData.TryGetValue(colIdx, out var text))
                            sb.Append(text);
                    }
                    sb.AppendLine();
                }

                // クリップボードアクセスはリトライ（他アプリのロック対策）
                ClipboardHelper.TrySetText(sb.ToString());
                return true;
            }
            catch
            {
                return false; // コピー失敗時はデフォルト動作にフォールバック
            }
        }

        /// <summary>
        /// 列ヘッダーからテキストを取得します（StackPanel内のTextBlockを連結）。
        /// </summary>
        private static string GetColumnHeaderText(DataGridColumn column)
        {
            if (column.Header is string s) return s;
            if (column.Header is StackPanel panel)
            {
                var parts = new List<string>();
                foreach (var child in panel.Children)
                {
                    if (child is System.Windows.Controls.TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                        parts.Add(tb.Text);
                }
                return string.Join(" ", parts);
            }
            return column.Header?.ToString() ?? string.Empty;
        }

        private static string GetCellText(object item, DataGridColumn column)
        {
            switch (column)
            {
                case DataGridBoundColumn bound:
                    if (bound.Binding is Binding binding && binding.Path != null)
                    {
                        var value = GetPropertyValue(item, binding.Path.Path);
                        if (value == null) return string.Empty;

                        if (value is bool b) return b ? "True" : "False";

                        if (!string.IsNullOrEmpty(binding.StringFormat))
                        {
                            try
                            {
                                string fmt = binding.StringFormat;
                                // WPFのStringFormat ("N3"等) をstring.Format互換 ("{0:N3}") に変換
                                if (!fmt.Contains('{'))
                                    fmt = $"{{0:{fmt}}}";
                                string formatted = string.Format(fmt, value);
                                // Excel 貼付け互換: N1/N3 等の桁区切りコンマ "1,554.0" を
                                // Excel が列区切りとして解釈し 1 セルを複数セルに分割するケース
                                // (直前の「区切り位置」設定の残留等) があるため、数値型のみ
                                // コンマを除去する。小数点のピリオドはそのまま維持。
                                if (IsNumericType(value))
                                    formatted = formatted.Replace(",", string.Empty);
                                return formatted;
                            }
                            catch { return value.ToString() ?? string.Empty; }
                        }
                        return value.ToString() ?? string.Empty;
                    }
                    break;

                case DataGridComboBoxColumn combo:
                    {
                        var comboBinding = (combo.SelectedValueBinding as Binding)
                                        ?? (combo.SelectedItemBinding as Binding);
                        if (comboBinding?.Path != null)
                        {
                            var value = GetPropertyValue(item, comboBinding.Path.Path);
                            return value?.ToString() ?? string.Empty;
                        }
                        break;
                    }

                case DataGridTemplateColumn:
                    // TemplateColumn: VisualTree内のTextBlockから表示テキストを取得
                    return string.Empty; // VisualTree版は別途GetCellTextFromVisualTreeで取得
            }

            return string.Empty;
        }

        private string GetCellTextFromVisualTree(int rowIndex, DataGridColumn column)
        {
            try
            {
                // DataGridRowコンテナを取得（仮想化により存在しない場合がある）
                var row = ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
                if (row != null)
                {
                    var presenter = FindVisualChild<DataGridCellsPresenter>(row);
                    if (presenter != null)
                    {
                        var cell = presenter.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;
                        if (cell != null)
                        {
                            // TextBlock または Button の Content からテキストを取得
                            var textBlock = FindVisualChild<System.Windows.Controls.TextBlock>(cell);
                            if (textBlock != null) return textBlock.Text;

                            var button = FindVisualChild<System.Windows.Controls.Button>(cell);
                            if (button?.Content is string btnText) return btnText;
                        }
                    }
                }

                // VisualTree取得失敗時: CellTemplateからバインディングパスを解析してデータから取得
                if (column is DataGridTemplateColumn templateCol && templateCol.CellTemplate != null)
                {
                    var item = Items[rowIndex];
                    // テンプレート内のボタンの静的Contentを取得するフォールバック
                    var content = templateCol.CellTemplate.LoadContent();
                    if (content is System.Windows.Controls.Button btn && btn.Content is string staticText)
                        return staticText;
                    if (content is System.Windows.Controls.TextBlock tb && tb.Text != null)
                        return tb.Text;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static bool IsNumericType(object value) =>
            value is sbyte or byte or short or ushort or int or uint or long or ulong
                 or float or double or decimal;

        private static object? GetPropertyValue(object item, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            object? current = item;
            foreach (var segment in path.Split('.'))
            {
                if (current == null) return null;

                // インデクサ表記 (例: "AxialForceLevel1s[0]") を解析
                string propName = segment;
                int? index = null;
                int bracketStart = segment.IndexOf('[');
                if (bracketStart >= 0)
                {
                    int bracketEnd = segment.IndexOf(']', bracketStart);
                    if (bracketEnd > bracketStart &&
                        int.TryParse(segment.Substring(bracketStart + 1, bracketEnd - bracketStart - 1), out int idx))
                    {
                        index = idx;
                        propName = bracketStart > 0 ? segment[..bracketStart] : string.Empty;
                    }
                }

                if (!string.IsNullOrEmpty(propName))
                {
                    var prop = current.GetType().GetProperty(propName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null) return null;
                    current = prop.GetValue(current);
                }

                if (index.HasValue && current != null)
                {
                    if (current is IList list && index.Value >= 0 && index.Value < list.Count)
                        current = list[index.Value];
                    else if (current is Array arr && index.Value >= 0 && index.Value < arr.Length)
                        current = arr.GetValue(index.Value);
                    else
                        return null;
                }
            }
            return current;
        }

        private bool TryPasteFromClipboard()
        {
            try
            {
                string text = Clipboard.GetText(TextDataFormat.Text);
                if (string.IsNullOrWhiteSpace(text)) return false;

                var rows = text
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Where(line => line.Length > 0)
                    .Select(line => line.Split('\t'))
                    .ToArray();

                if (rows.Length == 0) return false;

                // ヘッダー行スキップ: 先頭行の全セルが数値変換不可の場合はヘッダーとみなす
                if (rows.Length > 1 && rows[0].Length > 0)
                {
                    bool firstRowAllNonNumeric = rows[0].All(cell =>
                        !double.TryParse(cell.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.CurrentCulture, out _) &&
                        !double.TryParse(cell.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out _));
                    if (firstRowAllNonNumeric)
                        rows = rows.Skip(1).ToArray();
                }

                if (rows.Length == 0) return false;

                int pasteRowCount = rows.Length;
                int pasteColCount = rows.Max(r => r.Length);

                if (!TryGetPasteStart(out int startRowIndex, out int startDisplayIndex))
                {
                    MessageService.Show(OwnerWindow, "貼り付け開始セルを特定できません。セルを選択してから実行してください。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var displayOrderedCols = Columns.OrderBy(c => c.DisplayIndex).ToList();

                // Excel ライク: クリップボードが 1×1 で複数セル選択中なら、選択全セルへ同じ値を流し込む
                if (pasteRowCount == 1 && pasteColCount == 1
                    && SelectedCells != null && SelectedCells.Count > 1)
                {
                    return TryFillSelectedCells(rows[0][0]);
                }

                // 行数不足時: ItemsSourceがObservableCollectionの場合は自動追加
                if (startRowIndex + pasteRowCount > Items.Count)
                {
                    if (ItemsSource is System.Collections.IList list)
                    {
                        var itemType = list.GetType().GetGenericArguments().FirstOrDefault();
                        if (itemType != null)
                        {
                            int needRows = startRowIndex + pasteRowCount - Items.Count;
                            for (int i = 0; i < needRows; i++)
                            {
                                try { list.Add(Activator.CreateInstance(itemType)); }
                                catch { break; }
                            }
                        }
                    }
                    // それでも足りない場合はエラー
                    if (startRowIndex + pasteRowCount > Items.Count)
                    {
                        MessageService.Show(OwnerWindow, "貼り付け範囲が行数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                if (startDisplayIndex + pasteColCount > displayOrderedCols.Count)
                {
                    MessageService.Show(OwnerWindow, "貼り付け範囲が列数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 1) 事前検証
                for (int r = 0; r < pasteRowCount; r++)
                {
                    var item = Items[startRowIndex + r];
                    for (int c = 0; c < pasteColCount; c++)
                    {
                        string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                        var col = displayOrderedCols[startDisplayIndex + c];
                        if (col.IsReadOnly)
                            return FailFormat(r, c, "対象列は読み取り専用です。");

                        if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind))
                            return FailFormat(r, c, "対象列へのバインディング情報を取得できません。");

                        if (!CanConvert(cellText, targetType, columnKind))
                            return FailFormat(r, c, $"値 '{cellText}' は列の型({PrettyTypeName(targetType)})に変換できません。");

                        if (!TryNavigateForSet(item, path, out _, out _, out _))
                            return FailFormat(r, c, "バインディングのパスに該当するプロパティ/インデクサが見つかりません。");
                    }
                }

                // 2) 反映 (一括書込中は派生プロパティの重い同期再評価を抑制)
                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);

                IsBulkEditing = true;
                try
                {
                    for (int r = 0; r < pasteRowCount; r++)
                    {
                        var item = Items[startRowIndex + r];
                        for (int c = 0; c < pasteColCount; c++)
                        {
                            string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                            var col = displayOrderedCols[startDisplayIndex + c];
                            TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind);

                            object? converted = ConvertValue(cellText, targetType, columnKind);

                            if (!TrySetValueByPath(item, path, converted))
                                return FailFormat(r, c, "値の設定に失敗しました。");
                        }
                    }
                }
                finally { IsBulkEditing = false; }

                var focusAnchor = CurrentCell;
                if (ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    Items.Refresh();

                RestoreKeyboardFocusAfterRefresh(focusAnchor);
                return true;
            }
            catch (Exception ex)
            {
                MessageService.Show(OwnerWindow, $"貼り付け中にエラーが発生しました。\n{ex.Message}", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Excel ライクな塗り潰しペースト: 単一値 (cellText) を SelectedCells 全てへ書き込む。
        /// 読み取り専用列・型変換不可セルはスキップする (一括ペースト中の局所的失敗で操作全体を止めない)。
        /// </summary>
        private bool TryFillSelectedCells(string cellText)
        {
            try
            {
                // 同じ (Item, Column) ペアの重複を排除
                var uniqueTargets = new HashSet<(object Item, DataGridColumn Column)>();
                foreach (var sc in SelectedCells)
                {
                    if (sc.Item == null || sc.Column == null) continue;
                    uniqueTargets.Add((sc.Item, sc.Column));
                }
                if (uniqueTargets.Count == 0) return false;

                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);

                // 1) 事前検証: 書き込み可能なターゲットがあるか、変換が成立するか
                bool anyWritable = false;
                foreach (var (item, col) in uniqueTargets)
                {
                    if (col.IsReadOnly) continue;
                    if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind)) continue;
                    if (!CanConvert(cellText, targetType, columnKind))
                    {
                        MessageService.Show(OwnerWindow,
                            $"値 '{cellText}' は列「{GetColumnHeaderText(col)}」の型 ({PrettyTypeName(targetType)}) に変換できません。",
                            "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    if (!TryNavigateForSet(item, path, out _, out _, out _)) continue;
                    anyWritable = true;
                }
                if (!anyWritable)
                {
                    MessageService.Show(OwnerWindow, "選択範囲に書き込み可能なセルがありません。",
                        "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 2) 反映 (一括書込中は派生プロパティの重い同期再評価を抑制)
                IsBulkEditing = true;
                try
                {
                    foreach (var (item, col) in uniqueTargets)
                    {
                        if (col.IsReadOnly) continue;
                        if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind)) continue;
                        object? converted = ConvertValue(cellText, targetType, columnKind);
                        TrySetValueByPath(item, path, converted);
                    }
                }
                finally { IsBulkEditing = false; }

                var focusAnchor = CurrentCell;
                if (ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    Items.Refresh();

                RestoreKeyboardFocusAfterRefresh(focusAnchor);
                return true;
            }
            catch (Exception ex)
            {
                MessageService.Show(OwnerWindow, $"塗り潰しペースト中にエラーが発生しました。\n{ex.Message}", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 選択セルの値をクリアする (Excel の Delete 相当)。型に応じて null / "" / false / 0 を設定。
        /// 読み取り専用列・ComboBox 列 (選択値) は対象外。塗り潰しペーストと同様に IsBulkEditing で
        /// 重い派生通知を抑制し、完了後にフォーカスを戻す。クリアできるセルが 1 つも無ければ false。
        /// </summary>
        private bool TryClearSelectedCells()
        {
            try
            {
                if (SelectedCells == null || SelectedCells.Count == 0) return false;

                var uniqueTargets = new HashSet<(object Item, DataGridColumn Column)>();
                foreach (var sc in SelectedCells)
                {
                    if (sc.Item == null || sc.Column == null) continue;
                    uniqueTargets.Add((sc.Item, sc.Column));
                }
                if (uniqueTargets.Count == 0) return false;

                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);

                bool anyCleared = false;
                IsBulkEditing = true;
                try
                {
                    foreach (var (item, col) in uniqueTargets)
                    {
                        if (col.IsReadOnly) continue;
                        if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind)) continue;
                        // ComboBox (選択値/選択アイテム) は null 化で不整合になりやすいためクリア対象外
                        if (columnKind is ColumnKind.ComboSelectedItem or ColumnKind.ComboSelectedValue) continue;
                        if (!TryNavigateForSet(item, path, out _, out _, out _)) continue;

                        object? cleared = ClearedValueFor(targetType, columnKind);
                        if (TrySetValueByPath(item, path, cleared)) anyCleared = true;
                    }
                }
                finally { IsBulkEditing = false; }

                if (!anyCleared) return false;

                var focusAnchor = CurrentCell;
                if (ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    Items.Refresh();

                RestoreKeyboardFocusAfterRefresh(focusAnchor);
                return true;
            }
            catch (Exception ex)
            {
                MessageService.Show(OwnerWindow, $"セルのクリア中にエラーが発生しました。\n{ex.Message}", "クリアエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>Delete クリア時にセルへ設定する「空」値を型から決定する。</summary>
        private static object? ClearedValueFor(Type targetType, ColumnKind kind)
        {
            var (underlying, isNullable) = UnwrapNullable(targetType);
            if (kind == ColumnKind.CheckBox || underlying == typeof(bool))
                return isNullable ? null : false;
            if (underlying == typeof(string)) return string.Empty;
            if (isNullable) return null;                       // Nullable<数値> は null へ
            if (underlying == typeof(int)) return 0;
            if (underlying == typeof(double)) return 0.0;
            if (underlying == typeof(decimal)) return 0m;
            if (underlying.IsValueType) return Activator.CreateInstance(underlying);
            return null;
        }

        private Window? OwnerWindow => Window.GetWindow(this);

        private bool TryGetPasteStart(out int startRowIndex, out int startDisplayIndex)
        {
            startRowIndex = -1;
            startDisplayIndex = -1;

            if (SelectedCells is { Count: > 0 })
            {
                var rows = new List<int>();
                var cols = new List<int>();
                foreach (var sc in SelectedCells)
                {
                    if (sc.Item != null)
                    {
                        int ri = Items.IndexOf(sc.Item);
                        if (ri >= 0) rows.Add(ri);
                    }
                    if (sc.Column != null)
                    {
                        cols.Add(sc.Column.DisplayIndex);
                    }
                }
                if (rows.Count > 0 && cols.Count > 0)
                {
                    startRowIndex = rows.Min();
                    startDisplayIndex = cols.Min();
                    return true;
                }
            }

            if (CurrentCell.Item != null && CurrentCell.Column != null)
            {
                startRowIndex = Items.IndexOf(CurrentCell.Item);
                startDisplayIndex = CurrentCell.Column.DisplayIndex;
                return startRowIndex >= 0 && startDisplayIndex >= 0;
            }

            return false;
        }

        private enum ColumnKind
        {
            Text,
            CheckBox,
            ComboSelectedItem,
            ComboSelectedValue
        }

        // 変更: rowItem を受け取り、パスから実際のターゲット型を解決
        private static bool TryGetBindingInfo(object rowItem, DataGridColumn column, out string path, out Type targetType, out ColumnKind columnKind)
        {
            path = string.Empty;
            targetType = typeof(object);
            columnKind = ColumnKind.Text;

            switch (column)
            {
                case DataGridBoundColumn bound:
                    if (bound.Binding is Binding b && b.Path != null)
                    {
                        path = b.Path.Path;
                        columnKind = bound is DataGridCheckBoxColumn ? ColumnKind.CheckBox : ColumnKind.Text;
                        targetType = InferTargetType(rowItem, path) ?? typeof(object);
                        return true;
                    }
                    return false;

                case DataGridComboBoxColumn combo:
                    Binding? binding = null;
                    if (combo.SelectedValueBinding is Binding svb && svb.Path != null)
                    {
                        binding = svb;
                        columnKind = ColumnKind.ComboSelectedValue;
                    }
                    else if (combo.SelectedItemBinding is Binding sib && sib.Path != null)
                    {
                        binding = sib;
                        columnKind = ColumnKind.ComboSelectedItem;
                    }

                    if (binding?.Path == null) return false;

                    path = binding.Path.Path;
                    targetType = InferTargetType(rowItem, path) ?? typeof(object);
                    return true;

                default:
                    return false;
            }
        }

        private static bool CanConvert(string input, Type targetType, ColumnKind kind)
        {
            try
            {
                _ = ConvertValue(input, targetType, kind);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object? ConvertValue(string input, Type targetType, ColumnKind kind)
        {
            input ??= string.Empty;
            input = input.Trim();

            if (string.IsNullOrEmpty(input))
            {
                if (IsNullable(targetType)) return null;
                if (targetType == typeof(string)) return string.Empty;
                throw new FormatException("空文字は非Nullable列へは設定できません。");
            }

            var (underlying, _) = UnwrapNullable(targetType);

            // 前後の空白・全角スペース・不可視文字を除去
            input = input.Trim().Trim('\u200B', '\uFEFF', '\u00A0');

            if (kind == ColumnKind.CheckBox || underlying == typeof(bool))
            {
                if (TryParseBool(input, out bool b)) return b;
                throw new FormatException("bool型へ変換できません。");
            }

            if (underlying == typeof(string)) return input;

            if (underlying == typeof(int))
            {
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) return i;
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
                throw new FormatException("int型へ変換できません。");
            }

            if (underlying == typeof(double))
            {
                if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var d)) return d;
                if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d)) return d;
                throw new FormatException("double型へ変換できません。");
            }

            if (underlying == typeof(decimal))
            {
                if (decimal.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var m)) return m;
                if (decimal.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out m)) return m;
                throw new FormatException("decimal型へ変換できません。");
            }

            var converter = TypeDescriptor.GetConverter(underlying);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFrom(null, CultureInfo.CurrentCulture, input);
            }

            return input;
        }

        private static bool TryParseBool(string s, out bool value)
        {
            s = s.Trim().ToLowerInvariant();
            switch (s)
            {
                case "true":
                case "1":
                case "y":
                case "yes":
                case "on":
                case "はい":
                    value = true; return true;
                case "false":
                case "0":
                case "n":
                case "no":
                case "off":
                case "いいえ":
                    value = false; return true;
                default:
                    return bool.TryParse(s, out value);
            }
        }

        private static (Type underlying, bool isNullable) UnwrapNullable(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return (Nullable.GetUnderlyingType(t)!, true);
            }
            return (t, false);
        }

        private static bool IsNullable(Type t) => !t.IsValueType || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>));

        private static bool TrySetValueByPath(object root, string path, object? value)
        {
            if (!TryNavigateForSet(root, path, out object? target, out PropertyInfo? prop, out int? index))
                return false;

            if (target == null) return false;

            if (prop != null && index == null)
            {
                var (_, isNullable) = UnwrapNullable(prop.PropertyType);
                if (value == null && prop.PropertyType.IsValueType && !isNullable) return false;
                prop.SetValue(target, value);
                return true;
            }

            if (prop != null && index != null)
            {
                var col = prop.GetValue(target);
                return SetCollectionIndex(col, index.Value, value);
            }

            if (index != null)
            {
                return SetCollectionIndex(target, index.Value, value);
            }

            return false;
        }

        private static bool SetCollectionIndex(object? collection, int index, object? value)
        {
            if (collection == null) return false;

            switch (collection)
            {
                case Array arr:
                    {
                        var elementType = arr.GetType().GetElementType();
                        object? converted = value;
                        if (elementType != null && value != null && !elementType.IsInstanceOfType(value))
                        {
                            var converter = TypeDescriptor.GetConverter(elementType);
                            if (converter.CanConvertFrom(value.GetType()))
                                converted = converter.ConvertFrom(value);
                        }
                        arr.SetValue(converted, index);
                        return true;
                    }
                case IList list:
                    {
                        var elementType = GetListElementType(list.GetType()) ?? value?.GetType();
                        object? converted = value;
                        if (elementType != null && value != null && !elementType.IsInstanceOfType(value))
                        {
                            var converter = TypeDescriptor.GetConverter(elementType);
                            if (converter.CanConvertFrom(value.GetType()))
                                converted = converter.ConvertFrom(value);
                        }
                        list[index] = converted!;
                        return true;
                    }
                default:
                    var indexer = collection.GetType().GetDefaultMembers()
                        .OfType<PropertyInfo>()
                        .FirstOrDefault(pi =>
                        {
                            var idx = pi.GetIndexParameters();
                            return idx.Length == 1 && idx[0].ParameterType == typeof(int) && pi.CanWrite;
                        });

                    if (indexer != null)
                    {
                        indexer.SetValue(collection, value, [index]);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static Type? GetListElementType(Type listType)
        {
            if (listType.IsArray) return listType.GetElementType();
            if (listType.IsGenericType) return listType.GetGenericArguments().FirstOrDefault();
            return typeof(object);
        }

        private static bool TryNavigateForSet(object root, string path, out object? target, out PropertyInfo? leafProperty, out int? leafIndex)
        {
            target = root;
            leafProperty = null;
            leafIndex = null;

            if (string.IsNullOrWhiteSpace(path)) return false;

            var segments = SplitPath(path);

            object? current = root;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                ParseSegment(seg, out string? propName, out int? idx);

                if (!string.IsNullOrEmpty(propName))
                {
                    PropertyInfo? prop = current!.GetType().GetProperty(propName!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null) return false;

                    if (i == segments.Count - 1)
                    {
                        target = current;
                        leafProperty = prop;
                        leafIndex = idx;
                        return true;
                    }

                    current = prop.GetValue(current);
                    if (current == null) return false;
                }
                else
                {
                    if (i == segments.Count - 1)
                    {
                        target = current;
                        leafProperty = null;
                        leafIndex = idx;
                        return true;
                    }

                    current = GetCollectionElement(current, idx ?? 0);
                }
            }

            return false;
        }

        private static object? GetCollectionElement(object? collection, int index)
        {
            if (collection == null) return null;
            return collection switch
            {
                Array arr => (index >= 0 && index < arr.Length) ? arr.GetValue(index) : null,
                IList list => (index >= 0 && index < list.Count) ? list[index] : null,
                _ => collection
            };
        }

        private static List<string> SplitPath(string path)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            int bracket = 0;

            foreach (char ch in path)
            {
                if (ch == '.' && bracket == 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    if (ch == '[') bracket++;
                    if (ch == ']') bracket--;
                    sb.Append(ch);
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        private static void ParseSegment(string segment, out string? propName, out int? index)
        {
            index = null;

            int b = segment.IndexOf('[');
            if (b >= 0)
            {
                int e = segment.IndexOf(']', b + 1);
                if (e > b)
                {
                    string idxStr = segment.Substring(b + 1, e - b - 1);
                    if (int.TryParse(idxStr, out int i)) index = i;
                    propName = b > 0 ? segment[..b] : null;
                    return;
                }
            }

            propName = segment;
        }

        private static Type? InferTargetType(object rowItem, string path)
        {
            if (TryNavigateForSet(rowItem, path, out var target, out var prop, out var index))
            {
                if (prop != null && index == null)
                {
                    // 通常プロパティ
                    return prop.PropertyType;
                }
                if (prop != null && index != null)
                {
                    // プロパティが返すコレクションの要素型
                    var col = prop.GetValue(target);
                    return GetElementTypeFromInstanceOrType(col, prop.PropertyType);
                }
                if (prop == null && index != null)
                {
                    // 直接コレクションに対するインデクサ
                    return GetElementTypeFromInstanceOrType(target, target?.GetType());
                }
            }
            return null;
        }

        private static Type? GetElementTypeFromInstanceOrType(object? instance, Type? declaredType)
        {
            if (instance is Array a) return a.GetType().GetElementType();
            if (instance is IList il) return GetListElementType(il.GetType());

            if (declaredType != null)
            {
                if (declaredType.IsArray) return declaredType.GetElementType();
                if (declaredType.IsGenericType) return declaredType.GetGenericArguments().FirstOrDefault();
            }
            return null;
        }

        private bool FailFormat(int r, int c, string message)
        {
            MessageService.Show(
                OwnerWindow,
                $"貼り付けできませんでした。\n行: {r + 1}, 列: {c + 1}\n理由: {message}",
                "貼り付けエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private static string PrettyTypeName(Type t)
        {
            var (u, isN) = UnwrapNullable(t);
            return isN ? $"{u.Name}?" : u.Name;
        }
    }
}