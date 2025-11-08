
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Output
{
    internal class DataGridCsv
    {
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

            // データ行を追加
            foreach (var item in data)
            {
                if (item is not null)
                {
                    var row = dataGrid.Columns.Select(column =>
                    {
                        var cellContent = column.GetCellContent(item) as TextBlock;
                        return cellContent?.Text ?? string.Empty;
                    }).ToArray();
                    sb.AppendLine(string.Join(",", row));
                }
            }

            // ファイルに書き込む
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
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
                MessageBox.Show("CSVファイルにエクスポートしました。", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}