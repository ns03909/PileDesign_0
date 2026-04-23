
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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

            // バインディングパスからプロパティ値を直接取得（仮想化対応）
            Binding? binding = null;
            if (column is DataGridBoundColumn boundColumn)
                binding = boundColumn.Binding as Binding;
            else if (column is DataGridTemplateColumn templateColumn)
            {
                // テンプレート列はバインディングが取れないのでスキップ
                return string.Empty;
            }

            if (binding?.Path?.Path is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    var prop = item.GetType().GetProperty(path);
                    if (prop != null)
                    {
                        var value = prop.GetValue(item);
                        if (value == null) return string.Empty;

                        // StringFormatがある場合は適用
                        if (!string.IsNullOrEmpty(binding.StringFormat))
                            return StripThousandSeparator(string.Format(binding.StringFormat, value));

                        return StripThousandSeparator(value.ToString() ?? string.Empty);
                    }
                }
                catch { }
            }

            return string.Empty;
        }
    }
}