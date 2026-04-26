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

namespace PileDesign.Common
{
    public class EnhancedDataGrid : DataGrid
    {
        /// <summary>ペースト完了時に発火するイベント</summary>
        public event EventHandler PasteCompleted;

        public EnhancedDataGrid()
        {
            // Excel貼付け互換のためヘッダ除外コピー
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;

            // セル選択をデフォルト化（複数セル選択も許可）
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            SelectionMode = DataGridSelectionMode.Extended;

            // 縦スクロールバーを常時表示：Auto だとスクロールバー出現時にデータ行幅だけ縮んでヘッダーとずれる
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Visible);

            // 列仮想化を無効化：FrozenColumnCount との組み合わせで横スクロール時にガタつき・ヘッダー欠けが発生するため
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
            base.OnPreviewKeyDown(e);
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
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        Clipboard.SetText(sb.ToString());
                        return true;
                    }
                    catch (System.Runtime.InteropServices.ExternalException)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
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
                    MessageBox.Show(OwnerWindow, "貼り付け開始セルを特定できません。セルを選択してから実行してください。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show(OwnerWindow, "貼り付け範囲が行数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                if (startDisplayIndex + pasteColCount > displayOrderedCols.Count)
                {
                    MessageBox.Show(OwnerWindow, "貼り付け範囲が列数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // 2) 反映
                CommitEdit(DataGridEditingUnit.Cell, true);
                CommitEdit(DataGridEditingUnit.Row, true);

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

                if (ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    Items.Refresh();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(OwnerWindow, $"貼り付け中にエラーが発生しました。\n{ex.Message}", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MessageBox.Show(OwnerWindow,
                            $"値 '{cellText}' は列「{GetColumnHeaderText(col)}」の型 ({PrettyTypeName(targetType)}) に変換できません。",
                            "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    if (!TryNavigateForSet(item, path, out _, out _, out _)) continue;
                    anyWritable = true;
                }
                if (!anyWritable)
                {
                    MessageBox.Show(OwnerWindow, "選択範囲に書き込み可能なセルがありません。",
                        "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 2) 反映
                foreach (var (item, col) in uniqueTargets)
                {
                    if (col.IsReadOnly) continue;
                    if (!TryGetBindingInfo(item, col, out var path, out var targetType, out var columnKind)) continue;
                    object? converted = ConvertValue(cellText, targetType, columnKind);
                    TrySetValueByPath(item, path, converted);
                }

                if (ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    Items.Refresh();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(OwnerWindow, $"塗り潰しペースト中にエラーが発生しました。\n{ex.Message}", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
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
            MessageBox.Show(
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