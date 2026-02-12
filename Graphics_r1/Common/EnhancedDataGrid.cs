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
using System.Windows.Input;

namespace PileDesign.Common
{
    public class EnhancedDataGrid : DataGrid
    {
        public EnhancedDataGrid()
        {
            // Excel貼付け互換のためヘッダ除外コピー
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;

            // セル選択をデフォルト化（複数セル選択も許可）
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            SelectionMode = DataGridSelectionMode.Extended;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Ctrl+V で貼り付け
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (TryPasteFromClipboard())
                {
                    e.Handled = true;
                    return;
                }
            }
            base.OnPreviewKeyDown(e);
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

                int pasteRowCount = rows.Length;
                int pasteColCount = rows.Max(r => r.Length);

                if (!TryGetPasteStart(out int startRowIndex, out int startDisplayIndex))
                {
                    MessageBox.Show(OwnerWindow, "貼り付け開始セルを特定できません。セルを選択してから実行してください。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var displayOrderedCols = Columns.OrderBy(c => c.DisplayIndex).ToList();

                if (startRowIndex + pasteRowCount > Items.Count)
                {
                    MessageBox.Show(OwnerWindow, "貼り付け範囲が行数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
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