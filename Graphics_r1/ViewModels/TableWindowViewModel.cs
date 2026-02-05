using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public sealed class TableWindowViewModel : BaseViewModel
    {
        // 全テーブル
        public ObservableCollection<ResultTable> AllTables { get; } = [];
        // フィルタ後
        public ObservableCollection<ResultTable> FilteredTables { get; } = [];

        private ResultTable? _selectedTable;
        public ResultTable? SelectedTable
        {
            get => _selectedTable;
            set
            {
                if (SetProperty(ref _selectedTable, value))
                    ExportCsvCommandRaiseCanExecute();
            }
        }

        // フィルタ選択肢
        public ObservableCollection<string> LoadCaseFilterOptions { get; } = [];
        public ObservableCollection<string> LoadCombinationFilterOptions { get; } = [];
        public ObservableCollection<string> LiquefactionFilterOptions { get; } = [];

        private string _selectedLoadCaseFilter = "ALL";
        public string SelectedLoadCaseFilter
        {
            get => _selectedLoadCaseFilter;
            set
            {
                if (SetProperty(ref _selectedLoadCaseFilter, value))
                    ApplyFilters();
            }
        }

        private string _selectedLoadCombinationFilter = "ALL";
        public string SelectedLoadCombinationFilter
        {
            get => _selectedLoadCombinationFilter;
            set
            {
                if (SetProperty(ref _selectedLoadCombinationFilter, value))
                    ApplyFilters();
            }
        }

        private string _selectedLiquefactionFilter = "ALL";
        public string SelectedLiquefactionFilter
        {
            get => _selectedLiquefactionFilter;
            set
            {
                if (SetProperty(ref _selectedLiquefactionFilter, value))
                    ApplyFilters();
            }
        }

        public ICommand ExportCsvCommand { get; }

        public TableWindowViewModel()
        {
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(),
                                                _ => SelectedTable != null);
        }

        public void LoadTables(IReadOnlyList<ResultTable> list)
        {
            System.Diagnostics.Debug.WriteLine($"=== LoadTables called ===");
            System.Diagnostics.Debug.WriteLine($"Input list.Count = {list.Count}");
            foreach (var t in list)
            {
                System.Diagnostics.Debug.WriteLine($"  Table: {t.Name}, IsLiq={t.IsLiquefaction}, LoadCase={t.LoadCaseName}");
            }

            AllTables.Clear();
            foreach (var t in list) AllTables.Add(t);

            System.Diagnostics.Debug.WriteLine($"AllTables.Count after load = {AllTables.Count}");

            BuildFilterOptions();

            ApplyFilters();
            SelectedTable = FilteredTables.FirstOrDefault();
        }

        private void BuildFilterOptions()
        {
            LoadCaseFilterOptions.Clear();
            LoadCaseFilterOptions.Add("ALL");
            foreach (var name in AllTables.Select(t => t.LoadCaseName).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                LoadCaseFilterOptions.Add(name);

            LoadCombinationFilterOptions.Clear();
            LoadCombinationFilterOptions.Add("ALL");
            foreach (var name in AllTables.Select(t => t.LoadCombinationName).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                LoadCombinationFilterOptions.Add(name);

            LiquefactionFilterOptions.Clear();
            LiquefactionFilterOptions.Add("ALL");
            LiquefactionFilterOptions.Add("有");
            LiquefactionFilterOptions.Add("無");

            SelectedLoadCaseFilter = "ALL";
            SelectedLoadCombinationFilter = "ALL";
            SelectedLiquefactionFilter = "ALL";
        }

        private void ApplyFilters()
        {
            System.Diagnostics.Debug.WriteLine($"=== ApplyFilters ===");
            System.Diagnostics.Debug.WriteLine($"  SelectedLiquefactionFilter = '{SelectedLiquefactionFilter}'");
            System.Diagnostics.Debug.WriteLine($"  AllTables.Count = {AllTables.Count}");

            FilteredTables.Clear();
            var filtered = AllTables.Where(t =>
                (SelectedLoadCaseFilter == "ALL" || t.LoadCaseName == SelectedLoadCaseFilter) &&
                (SelectedLoadCombinationFilter == "ALL" || t.LoadCombinationName == SelectedLoadCombinationFilter) &&
                (SelectedLiquefactionFilter == "ALL" ||
                 (SelectedLiquefactionFilter == "有" ? t.IsLiquefaction : !t.IsLiquefaction))
            ).ToList();

            System.Diagnostics.Debug.WriteLine($"  Filtered count = {filtered.Count}");
            foreach (var t in filtered)
            {
                System.Diagnostics.Debug.WriteLine($"    Filtered: {t.Name}, IsLiq={t.IsLiquefaction}");
                FilteredTables.Add(t);
            }

            if (!FilteredTables.Contains(SelectedTable))
                SelectedTable = FilteredTables.FirstOrDefault();
        }

        private void ExportCsv()
        {
            if (SelectedTable == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV|*.csv",
                FileName = SelectedTable.Name + ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            using var writer = new StreamWriter(dlg.FileName, false, Encoding.UTF8);

            // ヘッダ
            var metaHeaders = new[] { "荷重条件", "荷重組合せ", "液状化" };
            writer.WriteLine(string.Join(",",
                metaHeaders.Concat(SelectedTable.Columns.Select(c => Escape(c.Header)))));

            var ci = CultureInfo.InvariantCulture;

            foreach (var row in SelectedTable.Rows)
            {
                var metaValues = new[]
                {
                    Escape(SelectedTable.LoadCaseName),
                    Escape(SelectedTable.LoadCombinationName),
                    Escape(SelectedTable.LiquefactionLabel)
                };

                var valuePart = SelectedTable.Columns.Select(c =>
                {
                    var v = c.Property.GetValue(row);
                    if (v == null) return "";
                    return c.Format is not null && v is IFormattable f
                        ? Escape(f.ToString(c.Format, ci))
                        : Escape(v.ToString() ?? "");
                });

                writer.WriteLine(string.Join(",", metaValues.Concat(valuePart)));
            }
        }

        private static string Escape(string s) =>
            (s.Contains(',') || s.Contains('"'))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;

        private void ExportCsvCommandRaiseCanExecute()
        {
            if (ExportCsvCommand is RelayCommand rc)
                rc.RaiseCanExecuteChanged();
        }

        // DataGrid の選択行をコピーしてクリップボードに格納
        private void CopyDataGridSelection(object? parameter)
        {
            try
            {
                if (SelectedTable == null)
                    return;

                if (parameter is not DataGrid dg)
                    return;

                var selectedItems = dg.SelectedItems.Cast<object>().ToList();
                if (selectedItems.Count == 0)
                {
                    // セル選択モードや空選択時の取り扱いはここで拡張可
                    return;
                }

                var sb = new StringBuilder();
                // ヘッダ（タブ区切り）
                sb.AppendLine(string.Join("\t", SelectedTable.Columns.Select(c => c.Header)));

                var ci = CultureInfo.InvariantCulture;

                foreach (var item in selectedItems)
                {
                    var values = SelectedTable.Columns.Select(c =>
                    {
                        var v = c.Property.GetValue(item);
                        if (v == null) return "";
                        return c.Format is not null && v is IFormattable f
                            ? f.ToString(c.Format, ci)
                            : v.ToString() ?? "";
                    });
                    sb.AppendLine(string.Join("\t", values));
                }

                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CopyDataGridSelection failed: {ex}");
            }
        }
    }
}