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
    public static class DataGridClipboardBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(DataGridClipboardBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);
        public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid dg) return;

            if ((bool)e.NewValue)
            {
                dg.PreviewKeyDown += OnPreviewKeyDown;
                // Excel貼付け互換のためヘッダ除外コピー
                dg.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
            }
            else
            {
                dg.PreviewKeyDown -= OnPreviewKeyDown;
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not DataGrid dg) return;

            // Ctrl + V → ペースト
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (TryPasteFromClipboard(dg))
                {
                    e.Handled = true;
                }
            }

            // コピー(CTRL+C)は既定の挙動に任せる
        }

        private static bool TryPasteFromClipboard(DataGrid dg)
        {
            try
            {
                string text = Clipboard.GetText(TextDataFormat.Text);
                if (string.IsNullOrWhiteSpace(text)) return false;

                // 改行とタブで分割（末尾の空行除去）
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

                // 開始位置(選択セル群の最左上 or CurrentCell)
                if (!TryGetPasteStart(dg, out int startRowIndex, out int startDisplayIndex))
                {
                    MessageBox.Show(GetOwner(dg), "貼り付け開始セルを特定できません。セルを選択してから実行してください。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 列のDisplayIndex→列オブジェクトのマップ（可視列のみ）
                var displayOrderedCols = dg.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .ToList();

                // 範囲チェック
                if (startRowIndex + pasteRowCount > dg.Items.Count)
                {
                    MessageBox.Show(GetOwner(dg), "貼り付け範囲が行数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (startDisplayIndex + pasteColCount > displayOrderedCols.Count)
                {
                    MessageBox.Show(GetOwner(dg), "貼り付け範囲が列数を超えています。", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 1) 事前検証（全セルOKならまとめて反映）
                for (int r = 0; r < pasteRowCount; r++)
                {
                    var item = dg.Items[startRowIndex + r];
                    for (int c = 0; c < pasteColCount; c++)
                    {
                        string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                        var col = displayOrderedCols[startDisplayIndex + c];
                        if (col.IsReadOnly)
                        {
                            return FailFormat(dg, r, c, "対象列は読み取り専用です。");
                        }

                        if (!TryGetBindingInfo(col, out var path, out var targetType, out var columnType))
                        {
                            return FailFormat(dg, r, c, "対象列へのバインディング情報を取得できません。");
                        }

                        if (!CanConvert(cellText, targetType, columnType))
                        {
                            return FailFormat(dg, r, c, $"値 '{cellText}' は列の型({PrettyTypeName(targetType)})に変換できません。");
                        }

                        if (!TryNavigateForSet(item, path, out _, out _, out _))
                        {
                            return FailFormat(dg, r, c, "バインディングのパスに該当するプロパティ/インデクサが見つかりません。");
                        }
                    }
                }

                // 2) 反映
                dg.CommitEdit(DataGridEditingUnit.Cell, true);
                dg.CommitEdit(DataGridEditingUnit.Row, true);

                for (int r = 0; r < pasteRowCount; r++)
                {
                    var item = dg.Items[startRowIndex + r];
                    for (int c = 0; c < pasteColCount; c++)
                    {
                        string cellText = c < rows[r].Length ? rows[r][c] : string.Empty;

                        var col = displayOrderedCols[startDisplayIndex + c];
                        TryGetBindingInfo(col, out var path, out var targetType, out var columnType);

                        object? converted = ConvertValue(cellText, targetType, columnType);

                        if (!TrySetValueByPath(item, path, converted))
                        {
                            // 理論上ここには来ない想定(事前検証済み)だが安全のため
                            return FailFormat(dg, r, c, "値の設定に失敗しました。");
                        }
                    }
                }

                // 更新を反映
                if (dg.ItemsSource is ICollectionView view)
                    view.Refresh();
                else
                    dg.Items.Refresh();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetOwner(dg), $"貼り付け中にエラーが発生しました。\n{ex.Message}", "貼り付けエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static bool TryGetPasteStart(DataGrid dg, out int startRowIndex, out int startDisplayIndex)
        {
            startRowIndex = -1;
            startDisplayIndex = -1;

            // 選択セルから最小行・最小DisplayIndexを取得
            if (dg.SelectedCells is { Count: > 0 })
            {
                var rows = new List<int>();
                var cols = new List<int>();
                foreach (var sc in dg.SelectedCells)
                {
                    if (sc.Item != null)
                    {
                        int ri = dg.Items.IndexOf(sc.Item);
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

            // それ以外はCurrentCell
            if (dg.CurrentCell.Item != null && dg.CurrentCell.Column != null)
            {
                startRowIndex = dg.Items.IndexOf(dg.CurrentCell.Item);
                startDisplayIndex = dg.CurrentCell.Column.DisplayIndex;
                return startRowIndex >= 0 && startDisplayIndex >= 0;
            }

            return false;
        }

        private static bool TryGetBindingInfo(DataGridColumn column, out string path, out Type targetType, out ColumnKind columnKind)
        {
            path = string.Empty;
            targetType = typeof(string);
            columnKind = ColumnKind.Text;

            switch (column)
            {
                case DataGridBoundColumn bound:
                    if (bound.Binding is Binding b && b.Path != null)
                    {
                        path = b.Path.Path;
                        columnKind = bound is DataGridCheckBoxColumn ? ColumnKind.CheckBox : ColumnKind.Text;
                        // 型はパスから推定（ナビゲーション）
                        targetType = typeof(string); // 後で実体から取得
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
                    targetType = typeof(object);
                    return true;

                default:
                    return false;
            }
        }

        private enum ColumnKind
        {
            Text,
            CheckBox,
            ComboSelectedItem,
            ComboSelectedValue
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

            // Null/空の扱い
            if (string.IsNullOrEmpty(input))
            {
                if (IsNullable(targetType)) return null;
                if (targetType == typeof(string)) return string.Empty;
                // 非Nullableで空は不可
                throw new FormatException("空文字は非Nullable列へは設定できません。");
            }

            var (underlying, isNullable) = UnwrapNullable(targetType);

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

            // 汎用TypeConverter
            var converter = System.ComponentModel.TypeDescriptor.GetConverter(underlying);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFrom(null, CultureInfo.CurrentCulture, input);
            }

            // 既知以外は文字列のまま（SelectedItemなどはVM側で受けることがある）
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
                // 通常プロパティ
                var (_, isNullable) = UnwrapNullable(prop.PropertyType);
                if (value == null && prop.PropertyType.IsValueType && !isNullable)
                {
                    return false;
                }
                prop.SetValue(target, value);
                return true;
            }

            if (prop != null && index != null)
            {
                // コレクションのインデクサ: プロパティがList/Array等を返すケース
                var col = prop.GetValue(target);
                return SetCollectionIndex(col, index.Value, value);
            }

            // 直接インデクサ（Pathが "Foo[0]" のようなケース）
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
                // ArrayはIListも実装しているため、先にArrayを判定する
                case Array arr:
                    {
                        var elementType = arr.GetType().GetElementType();
                        object? converted = value;
                        if (elementType != null && value != null && !elementType.IsInstanceOfType(value))
                        {
                            var converter = System.ComponentModel.TypeDescriptor.GetConverter(elementType);
                            if (converter.CanConvertFrom(value.GetType()))
                                converted = converter.ConvertFrom(value);
                        }
                        arr.SetValue(converted, index);
                        return true;
                    }
                case IList list:
                    {
                        // 型合わせ
                        var elementType = GetListElementType(list.GetType()) ?? value?.GetType();
                        object? converted = value;
                        if (elementType != null && value != null && !elementType.IsInstanceOfType(value))
                        {
                            var converter = System.ComponentModel.TypeDescriptor.GetConverter(elementType);
                            if (converter.CanConvertFrom(value.GetType()))
                                converted = converter.ConvertFrom(value);
                        }
                        list[index] = converted!;
                        return true;
                    }
                default:
                    // インデクサプロパティ(Item[int])を探す
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

            // ドット区切りを分解。ただしインデクサ付き "Name[0]" を1セグメントとして扱う
            var segments = SplitPath(path);

            object? current = root;
            PropertyInfo? prop = null;

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];

                // seg: "Prop", "Prop[0]" または "[0]"
                ParseSegment(seg, out string? propName, out int? idx);

                if (!string.IsNullOrEmpty(propName))
                {
                    prop = current!.GetType().GetProperty(propName!,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null) return false;

                    if (i == segments.Count - 1)
                    {
                        // 最終セグメント
                        target = current;
                        leafProperty = prop;
                        leafIndex = idx;
                        return true;
                    }

                    // 途中セグメント → 深掘り
                    current = prop.GetValue(current);
                    if (current == null) return false; // null経路
                }
                else
                {
                    // プロパティ名なしの「[i]」で始まる場合（直接コレクション想定）
                    if (i == segments.Count - 1)
                    {
                        target = current;
                        leafProperty = null;
                        leafIndex = idx;
                        return true;
                    }

                    // 中間に裸のインデクサは通常想定しないが、フォローする
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
                // ArrayはIListも実装しているため、先にArrayを判定する
                Array arr => (index >= 0 && index < arr.Length) ? arr.GetValue(index) : null,
                IList list => (index >= 0 && index < list.Count) ? list[index] : null,
                _ => collection
            };
        }

        private static List<string> SplitPath(string path)
        {
            // ドットで区切る。ただし角括弧付きは同一セグメント
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
            propName = null;
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

        private static bool FailFormat(DataGrid dg, int r, int c, string message)
        {
            MessageBox.Show(
                GetOwner(dg),
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

        private static Window? GetOwner(FrameworkElement fe)
            => Window.GetWindow(fe);

    }
}