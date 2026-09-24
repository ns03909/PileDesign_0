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
            //
            // 貼り付けを試みたら、成功しても拒否しても (理由を表示して) キーは処理済みにする。
            // 以前は成功したときだけ処理済みにしていたので、拒否のあとキーが既定の処理へ流れ、
            // セルが編集状態に入って「v」が入っていた (Esc で戻るが、確定すると値が壊れる)。
            // クリップボードに文字が無いときは何もしていないので、従来どおり既定の処理に任せる。
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && ClipboardHasText())
            {
                if (TryPasteFromClipboard())
                    PasteCompleted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
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
                string? text = BuildSelectionText();
                if (text == null) return false;

                // クリップボードアクセスはリトライ（他アプリのロック対策）
                ClipboardHelper.TrySetText(text);
                return true;
            }
            catch
            {
                return false; // コピー失敗時はデフォルト動作にフォールバック
            }
        }

        /// <summary>
        /// Ctrl+C でコピーする文字 (見出し 1 行 + 選択セルをタブ区切り)。選択が無ければ null。
        /// クリップボードに書く部分と分けてあるのは、試験で利用者のクリップボードを書き換えずに済ませるため。
        /// </summary>
        internal string? BuildSelectionText()
            // 並べ方は右クリックの「選択セルをコピー」と同じ処理を使う (見出し行を付けるかどうかだけが違う)。
            // 以前はここに別の写しがあった。
            => PileDesign.Output.DataGridCsv.BuildSelectionText(this, withTitleRow: true);

        /// <summary>列ヘッダーの表示文字列を取得します。</summary>
        private static string GetColumnHeaderText(DataGridColumn column) => DataGridHeaderText.From(column);

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

        /// <summary>貼り付けに使える文字がクリップボードにあるか。読めなければ無いものとする。</summary>
        private static bool ClipboardHasText()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Clipboard.GetText(TextDataFormat.Text));
            }
            catch
            {
                return false;
            }
        }

        private bool TryPasteFromClipboard()
        {
            string text;
            try
            {
                text = Clipboard.GetText(TextDataFormat.Text);
            }
            catch (Exception ex)
            {
                MessageService.ShowError(OwnerWindow, "貼り付け中にエラーが発生しました。", ex, "貼り付けエラー");
                return false;
            }
            return TryPasteText(text);
        }

        /// <summary>
        /// タブ区切りの文字列を、選択セルを起点に貼り付ける。クリップボードを読む部分と分けてあるのは、
        /// 試験でクリップボード (利用者のもの) を書き換えずに済ませるため。
        /// </summary>
        internal bool TryPasteText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return false;

                var rows = SplitPastedRows(text);

                if (rows.Length == 0) return false;

                if (!TryGetPasteStart(out int startRowIndex, out int startDisplayIndex))
                {
                    MessageService.Show(OwnerWindow, "貼り付け開始セルを特定できません。セルを選択してから実行してください。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var displayOrderedCols = Columns.OrderBy(c => c.DisplayIndex).ToList();

                // 先頭行が貼り付け先の列の見出しと同じなら、見出しとして読み飛ばす
                // (この表を見出しごと「全体コピー」して貼り戻す場合)。
                //
                // 以前は「先頭行のセルがすべて数値に読めない」だけで見出しとみなしていた。
                // 荷重ケース名のような文字の列に「CASE-A / CASE-B」を貼ると、CASE-A が黙って捨てられた。
                if (rows.Length > 1 && IsHeaderRowOf(rows[0], displayOrderedCols, startDisplayIndex))
                    rows = rows.Skip(1).ToArray();

                int pasteRowCount = rows.Length;
                int pasteColCount = rows.Max(r => r.Length);

                // Excel ライク: クリップボードが 1×1 で複数セル選択中なら、選択全セルへ同じ値を流し込む
                if (pasteRowCount == 1 && pasteColCount == 1
                    && SelectedCells != null && SelectedCells.Count > 1)
                {
                    return TryFillSelectedCells(rows[0][0]);
                }

                if (startDisplayIndex + pasteColCount > displayOrderedCols.Count)
                {
                    MessageService.Show(OwnerWindow, "貼り付け範囲が列数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 行が足りないときは、表に入れない仮の行を作って検証にかけ、全体が妥当なときだけ表へ足す。
                //
                // 以前は先に表へ行を足してから列数・値を検証していたので、貼り付けが検証で止まっても
                // 足した空の行が残った (失敗したのに表が変わる)。
                //
                // 行は、新規入力用の空行 (NewItemPlaceholder) を除いたデータの行だけを数える。
                // 空行を数に入れると、足りない行を 1 行少なく見積もり、空行そのものへ書こうとして止まる。
                var dataRows = Items.Cast<object>().Where(i => i != CollectionView.NewItemPlaceholder).ToList();
                var pendingRows = new List<object>();
                System.Collections.IList? targetList = null;
                if (startRowIndex + pasteRowCount > dataRows.Count)
                {
                    targetList = ItemsSource as System.Collections.IList;
                    var itemType = targetList?.GetType().GetGenericArguments().FirstOrDefault();
                    int needRows = startRowIndex + pasteRowCount - dataRows.Count;
                    if (itemType != null)
                    {
                        for (int i = 0; i < needRows; i++)
                        {
                            object? created;
                            try { created = Activator.CreateInstance(itemType); }
                            catch { break; }
                            if (created == null) break;
                            pendingRows.Add(created);
                        }
                    }
                    if (pendingRows.Count < needRows)
                    {
                        MessageService.Show(OwnerWindow, "貼り付け範囲が行数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                // 貼り付け先の r 行目 (既存の行か、まだ表に無い仮の行)。
                // 仮の行を表へ足すと Items が変わるので、足す前に控えたデータの行から引く
                object RowAt(int r)
                {
                    int index = startRowIndex + r;
                    return index < dataRows.Count ? dataRows[index] : pendingRows[index - dataRows.Count];
                }

                // 1) 事前検証 (仮の行も含めて、表に何も書く前に)
                for (int r = 0; r < pasteRowCount; r++)
                {
                    var item = RowAt(r);
                    for (int c = 0; c < pasteColCount; c++)
                    {
                        string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                        var col = displayOrderedCols[startDisplayIndex + c];
                        if (col.IsReadOnly)
                            return FailFormat(r, c, "この列は読み取り専用です (計算で決まる値など)。", col);

                        if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind))
                            return FailFormat(r, c, "この列には貼り付けられません (ボタンや、値を直接持たない列です)。", col);

                        if (!CanConvert(cellText, targetType, columnKind))
                            return FailFormat(r, c, DescribeConversionFailure(cellText, targetType, columnKind), col);

                        if (!TryNavigateForSet(item, path, out _, out _, out _))
                            return FailFormat(r, c, "この列には貼り付けられません (書き込み先が見つかりません)。", col);
                    }
                }

                // 2) 反映 (一括書込中は派生プロパティの重い同期再評価を抑制)
                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);

                // 検証が通ったので、仮の行を表へ足す。値は足したあとに書く
                // (表に入ってから書かないと、行の追加で始まる購読が値の変更を取りこぼす)。
                foreach (var row in pendingRows)
                    targetList!.Add(row);

                bool written = false;
                IsBulkEditing = true;
                try
                {
                    for (int r = 0; r < pasteRowCount; r++)
                    {
                        var item = RowAt(r);
                        for (int c = 0; c < pasteColCount; c++)
                        {
                            string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                            var col = displayOrderedCols[startDisplayIndex + c];
                            TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind);

                            object? converted = ConvertValue(cellText, targetType, columnKind);

                            if (!TrySetValueByPath(item, path, converted))
                                return FailFormat(r, c, "値を書き込めませんでした。", col);
                        }
                    }
                    written = true;
                }
                finally
                {
                    IsBulkEditing = false;
                    // 書き込みの途中で止まったら (値の設定の失敗・例外)、この貼り付けで足した行は取り除く
                    if (!written)
                        foreach (var row in pendingRows)
                            targetList!.Remove(row);
                }

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
                MessageService.ShowError(OwnerWindow, "貼り付け中にエラーが発生しました。", ex, "貼り付けエラー");
                return false;
            }
        }

        /// <summary>
        /// 貼り付ける文字列を行・セルに分ける。
        ///
        /// 捨てるのは<b>末尾の空行だけ</b> (コピーした文字列の最後に付く改行のぶん)。
        /// 以前は空の行をすべて捨てていたので、1 列の「10 / 空白 / 30」が「10 / 30」に詰まり、
        /// 30 が 1 行上の別の行に入った。途中の空行は空欄のセルとして残し、
        /// 空欄にできない列なら検証で止める (行はずらさない)。
        ///
        /// <b>引用符で囲まれたセルの中のタブ・改行は、区切りではなく値の一部として読む</b> (Excel の形)。
        /// 表のコピー (<c>DataGridCsv</c>) は、タブ・改行を含む値を引用符で囲んで書き出す。Excel も
        /// 改行を含むセルをそう書き出す。以前は単純にタブと改行で割っていたので、名称などにタブや改行が
        /// 入ると、貼り付け先で列や行がずれた。囲みの中の "" は " 1 文字。
        /// </summary>
        internal static string[][] SplitPastedRows(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;      // 囲みの中
            bool fieldStart = true;   // セルの先頭 (ここに来た " だけが囲みの始まり)

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"' when fieldStart:
                        quoted = true;
                        fieldStart = false;
                        break;
                    case '\t':
                        fields.Add(field.ToString()); field.Clear();
                        fieldStart = true;
                        break;
                    case '\n':
                        fields.Add(field.ToString()); field.Clear();
                        rows.Add([.. fields]); fields.Clear();
                        fieldStart = true;
                        break;
                    default:
                        field.Append(c);
                        fieldStart = false;
                        break;
                }
            }
            fields.Add(field.ToString());
            rows.Add([.. fields]);

            // 末尾の空行 (最後の改行のぶん) だけを捨てる
            while (rows.Count > 0 && rows[^1] is [""])
                rows.RemoveAt(rows.Count - 1);
            return [.. rows];
        }

        /// <summary>
        /// 貼り付ける先頭行が、貼り付け先の列の見出しそのものか。
        ///
        /// 見出しと一致しない限り、文字だけの行もデータとして扱う。見出しの比較は空白を除いて行う
        /// (多段の見出しは「層厚 (m)」のように空白で繋いで書き出しているため)。
        /// 見出しが空の列だけに当たるときは、見出しとは判定しない。
        /// </summary>
        internal static bool IsHeaderRowOf(IReadOnlyList<string> firstRow, IReadOnlyList<DataGridColumn> displayOrderedCols, int startDisplayIndex)
        {
            static string Normalize(string s) => new(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

            bool anyHeader = false;
            for (int c = 0; c < firstRow.Count; c++)
            {
                int index = startDisplayIndex + c;
                if (index >= displayOrderedCols.Count) return false;

                string header = Normalize(DataGridHeaderText.From(displayOrderedCols[index]));
                string cell = Normalize(firstRow[c]);
                if (header.Length == 0)
                {
                    if (cell.Length != 0) return false;
                    continue;
                }
                if (!string.Equals(header, cell, StringComparison.Ordinal)) return false;
                anyHeader = true;
            }
            return anyHeader;
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
                            $"列「{GetColumnHeaderText(col)}」: {DescribeConversionFailure(cellText, targetType, columnKind)}",
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
                MessageService.ShowError(OwnerWindow, "選択したセルへの貼り付け中にエラーが発生しました。", ex, "貼り付けエラー");
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
                MessageService.ShowError(OwnerWindow, "セルのクリア中にエラーが発生しました。", ex, "クリアエラー");
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
                // 文字の列は空文字にする。string は「null を許す型」でもあるので、先に見ないと
                // null が入る (名前などを空文字で持つ前提の処理が null に当たる)
                if (targetType == typeof(string)) return string.Empty;
                if (IsNullable(targetType)) return null;
                throw new FormatException("この列は空欄にできません。");
            }

            var (underlying, _) = UnwrapNullable(targetType);

            // 前後の空白・全角スペース・不可視文字を除去
            input = input.Trim().Trim('\u200B', '\uFEFF', '\u00A0');

            if (kind == ColumnKind.CheckBox || underlying == typeof(bool))
            {
                if (TryParseBool(input, out bool b)) return b;
                throw new FormatException("「はい」「いいえ」として読めません。");
            }

            if (underlying == typeof(string)) return input;

            if (underlying == typeof(int))
            {
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) return i;
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
                throw new FormatException("整数として読めません。");
            }

            if (underlying == typeof(double))
            {
                if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var d)
                    || double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d))
                    return RequireFinite(d);
                throw new FormatException("数値として読めません。");
            }

            if (underlying == typeof(float))
            {
                if (float.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var f)
                    || float.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out f))
                    return float.IsFinite(f) ? f : throw new NonFiniteNumberException();
                throw new FormatException("数値として読めません。");
            }

            if (underlying == typeof(decimal))
            {
                if (decimal.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var m)) return m;
                if (decimal.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out m)) return m;
                throw new FormatException("数値として読めません。");
            }

            var converter = TypeDescriptor.GetConverter(underlying);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFrom(null, CultureInfo.CurrentCulture, input);
            }

            return input;
        }

        /// <summary>
        /// 貼り付けた文字が「NaN」「Infinity」「∞」だったとき。
        ///
        /// <c>double.TryParse</c> はこれらを数値として受け付けるので、型の変換だけを見ていると
        /// 貼り付けが通り、一般節点の座標のように有限値を検査しないプロパティへそのまま入る。
        /// 画面では空欄や「NaN」と出るだけで、解析や描画で初めて壊れる。
        /// </summary>
        internal sealed class NonFiniteNumberException : FormatException
        {
            public NonFiniteNumberException() : base("有限の数値ではありません。") { }
        }

        private static double RequireFinite(double value)
            => double.IsFinite(value) ? value : throw new NonFiniteNumberException();

        /// <summary>貼り付けの事前検証で、変換できない理由を利用者向けの文にする。</summary>
        private static string DescribeConversionFailure(string cellText, Type targetType, ColumnKind kind)
        {
            try
            {
                _ = ConvertValue(cellText, targetType, kind);
                return string.Empty;
            }
            catch (NonFiniteNumberException)
            {
                return $"値 '{cellText}' は数値として扱えません。有限の数値を貼り付けてください。";
            }
            catch
            {
                return string.IsNullOrWhiteSpace(cellText)
                    ? "この列は空欄にできません。値を貼り付けてください。"
                    : $"値 '{cellText}' は{ExpectedValueText(targetType, kind)}として読めません。";
            }
        }

        /// <summary>
        /// 列に入る値の種類を、利用者の言葉で言う (「数値」「整数」など)。
        /// 以前は型名をそのまま出していて、「列の型(Double)に変換できません」のように内部の名前が見えていた。
        /// </summary>
        private static string ExpectedValueText(Type targetType, ColumnKind kind)
        {
            var (u, _) = UnwrapNullable(targetType);
            if (kind == ColumnKind.CheckBox || u == typeof(bool)) return "「はい」「いいえ」(1 / 0)";
            if (u == typeof(int) || u == typeof(long) || u == typeof(short)) return "整数";
            if (u == typeof(double) || u == typeof(float) || u == typeof(decimal)) return "数値";
            if (u.IsEnum || kind == ColumnKind.ComboSelectedItem || kind == ColumnKind.ComboSelectedValue) return "この列の選択肢";
            return "この列の値";
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

        /// <summary>
        /// 貼り付けを止めて理由を示す。場所は「貼り付けたデータの何行目・何列目」と、表の列の見出しで言う。
        /// 以前は「行: 2, 列: 1」とだけ出していて、表の行なのか貼ったデータの行なのかが分からなかった。
        /// </summary>
        private bool FailFormat(int r, int c, string message, DataGridColumn? column = null)
        {
            string header = column != null ? GetColumnHeaderText(column) : "";
            string where = $"貼り付けたデータの {r + 1} 行目・{c + 1} 列目"
                + (string.IsNullOrWhiteSpace(header) ? "" : $" (列「{header}」)");
            MessageService.Show(
                OwnerWindow,
                $"貼り付けできませんでした。\n場所: {where}\n理由: {message}",
                "貼り付けエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }
}