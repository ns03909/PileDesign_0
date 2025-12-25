//using CommunityToolkit.Mvvm.Input;
//using Microsoft.Win32;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Windows;
//using System.Windows.Controls;

//namespace PileDesign.ViewModels
//{
//    // 部分クラスでコマンドを追加（既存の MainWindowViewModel と合成されます）
//    public partial class MainWindowViewModel
//    {
//        // DataGrid を受け取り選択セルをタブ区切りでクリップボードへコピー
//        [RelayCommand]
//        private static void CopyDataGridSelection(DataGrid dataGrid)
//        {
//            if (dataGrid == null || dataGrid.SelectedCells.Count == 0) return;

//            var sb = new StringBuilder();
//            var selectedRows = dataGrid.SelectedCells.GroupBy(c => c.Item);

//            foreach (var row in selectedRows)
//            {
//                var rowValues = new List<string>();
//                foreach (var cell in row)
//                {
//                    if (cell.Column.GetCellContent(cell.Item) is TextBlock tb)
//                        rowValues.Add(tb.Text);
//                    else
//                        rowValues.Add(string.Empty);
//                }
//                sb.AppendLine(string.Join("\t", rowValues));
//            }

//            Clipboard.SetText(sb.ToString());
//        }

//        // CSV 出力（DataGrid を受け取り、選択セルを CSV として保存）
//        [RelayCommand]
//        private void ExportCsvFromContextMenu(DataGrid dataGrid)
//        {
//            if (dataGrid == null) return;

//            var sfd = new SaveFileDialog
//            {
//                Filter = "CSV ファイル (*.csv)|*.csv",
//                FileName = "export.csv",
//                Title = "CSV 出力"
//            };
//            if (sfd.ShowDialog() != true) return;

//            var sb = new StringBuilder();
//            var selectedRows = dataGrid.SelectedCells.GroupBy(c => c.Item);

//            foreach (var row in selectedRows)
//            {
//                var rowValues = new List<string>();
//                foreach (var cell in row)
//                {
//                    string text = string.Empty;
//                    if (cell.Column.GetCellContent(cell.Item) is TextBlock tb)
//                        text = tb.Text?.Replace("\"", "\"\"") ?? string.Empty;

//                    // CSV: カンマで区切る。必要に応じてエスケープ
//                    if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
//                        rowValues.Add($"\"{text}\"");
//                    else
//                        rowValues.Add(text);
//                }
//                sb.AppendLine(string.Join(",", rowValues));
//            }

//            try
//            {
//                System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show($"CSV 出力に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
//            }
//        }
//    }
//}