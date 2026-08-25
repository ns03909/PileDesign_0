using CsvHelper;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PileDesign.Services;

namespace PileDesign.Views
{
    public partial class PileLibraryWindow : Window
    {
        public PileLibraryWindow()
        {
            InitializeComponent();
            Loaded += PileLibraryWindow_Loaded;
        }

        private void PileLibraryWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = new[]
                {
                    "pile_library_PHC.csv",
                    "pile_library_NodularPile.csv",
                    "pile_library_NodularPile_head.csv",
                    "pile_library_PRC.csv",
                    "pile_library_SC.csv",
                    "pile_library_SteelPile.csv"
                };

                var baseDir = FindGraphicsR1Folder(AppDomain.CurrentDomain.BaseDirectory);
                if (baseDir == null)
                {
                    MessageService.Show(this, "杭ライブラリのデータフォルダが見つかりません。\nインストール先のファイルが不足している可能性があります。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var libFolder = System.IO.Path.Combine(baseDir, "Models", "PileLibrary");
                foreach (var f in files)
                {
                    var path = System.IO.Path.Combine(libFolder, f);
                    if (!File.Exists(path))
                    {
                        // 念のため別名バリエーションを試す（ユーザ入力の誤差対応）
                        var alt = f.Replace("PRC.csv", "PRCcsv").Replace("PRCcsv", "PRC.csv");
                        var altPath = System.IO.Path.Combine(libFolder, alt);
                        if (File.Exists(altPath)) path = altPath;
                    }

                    if (File.Exists(path))
                    {
                        var dt = LoadCsvToDataTable(path);
                        var tab = new TabItem { Header = System.IO.Path.GetFileName(path) };
                        var dg = new DataGrid
                        {
                            IsReadOnly = true,
                            AutoGenerateColumns = true,
                            ItemsSource = dt.DefaultView,
                            Margin = new Thickness(4),
                            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
                            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
                        };
                        // 数値列を右揃えにする
                        dg.AutoGeneratingColumn += (s, args) =>
                        {
                            // 列のサンプル値が数値かどうか判定
                            if (dt.Rows.Count > 0)
                            {
                                string sample = dt.Rows[0][args.Column.Header.ToString()]?.ToString() ?? "";
                                if (double.TryParse(sample, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                                {
                                    args.Column.CellStyle = new Style(typeof(DataGridCell))
                                    {
                                        Setters =
                                        {
                                            new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right)
                                        }
                                    };
                                }
                            }
                        };
                        tab.Content = dg;
                        TabControlLibraries.Items.Add(tab);
                    }
                    else
                    {
                        // 存在しないファイルはタブを追加せず、ログ用にタブを追加することも可
                    }
                }

                if (TabControlLibraries.Items.Count == 0)
                {
                    MessageService.Show(this, $"ライブラリ CSV が見つかりませんでした。期待パス: {libFolder}", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageService.ShowError(this, "杭ライブラリの読み込みに失敗しました。", ex);
            }
        }

        private static string? FindGraphicsR1Folder(string start)
        {
            try
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "Graphics_r1", StringComparison.OrdinalIgnoreCase))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return null;
            }
            catch (Exception ex)
            {
                MessageService.ShowError($"フォルダ探索中にエラーが発生しました。", ex, "エラー");
                return null;
            }
        }

        private static DataTable LoadCsvToDataTable(string path)
        {
            var dt = new DataTable();
            try
            {
                using var reader = new StreamReader(path);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                csv.Read();
                csv.ReadHeader();
                foreach (var h in csv.HeaderRecord ?? Array.Empty<string>())
                    dt.Columns.Add(h);

                while (csv.Read())
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < dt.Columns.Count; i++)
                    {
                        row[i] = csv.GetField(i) ?? string.Empty;
                    }
                    dt.Rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                MessageService.ShowError($"CSV読込中にエラーが発生しました。", ex, "エラー");
                // エラー時は空のDataTableを返す
            }
            return dt;
        }
    }
}