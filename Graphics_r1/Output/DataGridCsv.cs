
using Microsoft.Win32;
using PileDesign;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PileDesign.Output
{
    internal class DataGridCsv
    {
        // 桁区切りコンマを含む数値パターン: -?1,234(,567)*(.89)?
        // Excel 貼付け時にセル内のカンマが列区切りとして解釈される問題を避けるため、
        // このパターンにマッチするセル値はコンマを除去する。
        private static readonly Regex ThousandGroupedNumberPattern = new(
            @"^-?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.Compiled);

        private static string StripThousandSeparator(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            return ThousandGroupedNumberPattern.IsMatch(text) ? text.Replace(",", string.Empty) : text;
        }

        // データグリッドをCSVファイルにエクスポートするメソッド
        public static void CreateCsv(IEnumerable<object> data, DataGrid dataGrid, string filePath)
        {
            var sb = new StringBuilder();

            // 先頭行に「R」または「WR」を追加
            var readOnlyFlags = dataGrid.Columns.Select(column => column.IsReadOnly ? "R" : "WR").ToArray();
            sb.AppendLine(string.Join(",", readOnlyFlags));

            // ヘッダー行を追加
            var headers = dataGrid.Columns.Select(column =>
            {
                if (column.Header is StackPanel stackPanel)
                {
                    var textBlocks = stackPanel.Children.OfType<TextBlock>().Select(tb => tb.Text);
                    return string.Join(" ", textBlocks);
                }
                return column.Header.ToString().Trim();
            }).ToArray();
            sb.AppendLine(string.Join(",", headers));

            // データ行を追加（仮想化で画面外のセルも取得できるようバインディングから直接値を取得）
            foreach (var item in data)
            {
                if (item is not null)
                {
                    var row = dataGrid.Columns.Select(column => GetCellValue(column, item)).ToArray();
                    sb.AppendLine(string.Join(",", row));
                }
            }

            // ファイルに書き込む
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// DataGridの内容をタブ区切りテキストとしてクリップボードにコピーする
        /// </summary>
        public static void CopyToClipboard(DataGrid dataGrid)
        {
            var sb = new StringBuilder();

            // ヘッダー行
            var headers = dataGrid.Columns.Select(column =>
            {
                if (column.Header is StackPanel stackPanel)
                {
                    var textBlocks = stackPanel.Children.OfType<TextBlock>().Select(tb => tb.Text);
                    return string.Join(" ", textBlocks);
                }
                return column.Header?.ToString()?.Trim() ?? "";
            }).ToArray();
            sb.AppendLine(string.Join("\t", headers));

            // データ行（仮想化で画面外のセルも取得できるようバインディングから直接値を取得）
            foreach (var item in dataGrid.ItemsSource)
            {
                if (item is not null)
                {
                    var row = dataGrid.Columns.Select(column => GetCellValue(column, item)).ToArray();
                    sb.AppendLine(string.Join("\t", row));
                }
            }

            Clipboard.SetText(sb.ToString());
        }

        // データグリッドのデータをCSVファイルにエクスポートするメソッド
        public static void Export(IEnumerable<object> data, DataGrid dataGrid)
        {
            var dataGridName = dataGrid.Name;
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv",
                FileName = $"{dataGridName}_Export.csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CreateCsv(data, dataGrid, saveFileDialog.FileName);
            }
        }
        /// <summary>
        /// DataGridColumnからセル値を取得する。
        /// 画面に表示されているセルはGetCellContentで、仮想化で未生成のセルはバインディングパスから直接取得する。
        /// </summary>
        /// <summary>
        /// 選択セルからセル値を取得する（仮想化対応）。SelectedCells用ヘルパー。
        /// </summary>
        public static string GetCellValue(DataGridCellInfo cell)
        {
            return GetCellValue(cell.Column, cell.Item);
        }

        public static string GetCellValue(DataGridColumn column, object item)
        {
            // まず表示済みセルから取得を試みる
            if (column.GetCellContent(item) is TextBlock tb)
                return StripThousandSeparator(tb.Text);

            // バインディングを取得（仮想化対応）
            BindingBase? bindingBase = null;
            if (column is DataGridBoundColumn boundColumn)
                bindingBase = boundColumn.Binding;
            else if (column is DataGridTemplateColumn)
            {
                // テンプレート列はバインディングが取れないのでスキップ
                return string.Empty;
            }

            if (bindingBase is Binding binding)
            {
                return EvaluateSingleBinding(binding, item);
            }
            if (bindingBase is MultiBinding multi)
            {
                return EvaluateMultiBinding(multi, item);
            }
            return string.Empty;
        }

        private static string EvaluateSingleBinding(Binding binding, object item)
        {
            if (binding.Path?.Path is not string path || string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                var prop = item.GetType().GetProperty(path);
                if (prop == null) return string.Empty;
                var value = prop.GetValue(item);
                if (value == null) return string.Empty;

                if (!string.IsNullOrEmpty(binding.StringFormat))
                    return StripThousandSeparator(string.Format(binding.StringFormat, value));
                return StripThousandSeparator(value.ToString() ?? string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGridCsv] Binding '{path}' eval failed: {ex.GetType().Name}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string EvaluateMultiBinding(MultiBinding multi, object item)
        {
            // 各子 Binding を解決: item の直接プロパティ または RelativeSource Window 経由 (App.InputModel)
            var values = new object?[multi.Bindings.Count];
            for (int i = 0; i < multi.Bindings.Count; i++)
            {
                if (multi.Bindings[i] is not Binding b || b.Path?.Path is not string p) { values[i] = null; continue; }
                values[i] = ResolveBindingPath(b, p, item);
            }

            try
            {
                if (multi.Converter is IMultiValueConverter conv)
                {
                    var converted = conv.Convert(values, typeof(string), multi.ConverterParameter, CultureInfo.CurrentCulture);
                    if (converted == null) return string.Empty;
                    if (!string.IsNullOrEmpty(multi.StringFormat))
                        return StripThousandSeparator(string.Format(multi.StringFormat, converted));
                    return StripThousandSeparator(converted.ToString() ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGridCsv] MultiBinding converter '{multi.Converter?.GetType().Name ?? "<null>"}' failed: {ex.GetType().Name}: {ex.Message}");
            }
            return string.Empty;
        }

        private static object? ResolveBindingPath(Binding b, string path, object item)
        {
            try
            {
                // RelativeSource Window 経由の DataContext.* パス → App.InputModel ベースで解決
                if (b.RelativeSource != null && path.StartsWith("DataContext.", StringComparison.Ordinal))
                {
                    object? root = App.InputModel;
                    if (root == null) return null;
                    return ResolveDottedPath(root, path["DataContext.".Length..]);
                }
                return ResolveDottedPath(item, path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataGridCsv] Path '{path}' resolve failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static object? ResolveDottedPath(object? root, string path)
        {
            if (root == null) return null;
            object? cur = root;
            foreach (var segment in path.Split('.'))
            {
                if (cur == null) return null;
                var prop = cur.GetType().GetProperty(segment);
                if (prop == null) return null;
                cur = prop.GetValue(cur);
            }
            return cur;
        }
    }
}