using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Common;
using PileDesign.Models.InputData;
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
    public sealed partial class TableWindowViewModel : BaseViewModel
    {
        // グラフウィンドウと同じ特別フィルタ名 (レベル一括絞り込み)
        public const string LoadCaseFilterLevel1 = "L1 (地震荷重レベル1)";
        public const string LoadCaseFilterLevel2 = "L2 (地震荷重レベル2)";

        // 全テーブル
        public ObservableCollection<ResultTable> AllTables { get; } = [];
        // フィルタ後
        public ObservableCollection<ResultTable> FilteredTables { get; } = [];

        [ObservableProperty]
        private ResultTable? _selectedTable;

        partial void OnSelectedTableChanged(ResultTable? value) => ExportCsvCommandRaiseCanExecute();

        /// <summary>色分け用: 地震荷重ケース一覧</summary>
        public ObservableCollection<LoadCase>? AllSeismicLoadCases { get; set; }

        /// <summary>解析済みインジケータ用: AnaModel</summary>
        public FEM.AnaModel? AnaModel { get; set; }

        // フィルタ選択肢
        public ObservableCollection<string> TableCategoryFilterOptions { get; } = [];
        public ObservableCollection<string> LoadCaseFilterOptions { get; } = [];
        public ObservableCollection<string> LoadCombinationFilterOptions { get; } = [];
        public ObservableCollection<string> LiquefactionFilterOptions { get; } = [];

        [ObservableProperty]
        private string _selectedTableCategoryFilter = UiText.All;

        partial void OnSelectedTableCategoryFilterChanged(string value) => ApplyFilters();

        [ObservableProperty]
        private string _selectedLoadCaseFilter = UiText.All;

        partial void OnSelectedLoadCaseFilterChanged(string value) => ApplyFilters();

        [ObservableProperty]
        private string _selectedLoadCombinationFilter = UiText.All;

        partial void OnSelectedLoadCombinationFilterChanged(string value) => ApplyFilters();

        [ObservableProperty]
        private string _selectedLiquefactionFilter = UiText.All;

        partial void OnSelectedLiquefactionFilterChanged(string value) => ApplyFilters();

        public ICommand ExportCsvCommand { get; }

        public TableWindowViewModel()
        {
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(),
                                                _ => SelectedTable != null);
        }

        public void LoadTables(IReadOnlyList<ResultTable> list)
        {
            foreach (var t in list)
            {
            }

            AllTables.Clear();
            foreach (var t in list) AllTables.Add(t);

            BuildFilterOptions();

            ApplyFilters();
            SelectedTable = FilteredTables.FirstOrDefault();
        }

        private void BuildFilterOptions()
        {
            // テーブル種別フィルタ: カテゴリ（水平／沈下）
            TableCategoryFilterOptions.Clear();
            TableCategoryFilterOptions.Add(UiText.All);
            var distinctCategories = AllTables
                .Select(t => Converters.TableCategoryConverter.CategoryOf(t))
                .Distinct()
                .OrderBy(c => c == "水平解析結果" ? 0 : 1)
                .ToList();
            foreach (var c in distinctCategories)
                TableCategoryFilterOptions.Add(c);

            LoadCaseFilterOptions.Clear();
            LoadCaseFilterOptions.Add(UiText.All);
            // レベル一括絞り込み: AllSeismicLoadCases がセットされていて対応ケースが存在する時のみ追加
            bool hasL1 = AllSeismicLoadCases?.Any(lc => lc.Level == 1) == true;
            bool hasL2 = AllSeismicLoadCases?.Any(lc => lc.Level == 2) == true;
            if (hasL1) LoadCaseFilterOptions.Add(LoadCaseFilterLevel1);
            if (hasL2) LoadCaseFilterOptions.Add(LoadCaseFilterLevel2);
            foreach (var name in AllTables.Select(t => t.LoadCaseName).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                LoadCaseFilterOptions.Add(name);

            LoadCombinationFilterOptions.Clear();
            LoadCombinationFilterOptions.Add(UiText.All);
            foreach (var name in AllTables.Select(t => t.LoadCombinationName).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                LoadCombinationFilterOptions.Add(name);

            LiquefactionFilterOptions.Clear();
            LiquefactionFilterOptions.Add(UiText.All);
            LiquefactionFilterOptions.Add("有");
            LiquefactionFilterOptions.Add("無");

            SelectedTableCategoryFilter = UiText.All;
            SelectedLoadCaseFilter = UiText.All;
            SelectedLoadCombinationFilter = UiText.All;
            SelectedLiquefactionFilter = UiText.All;
        }

        private void ApplyFilters()
        {
            // レベル絞り込み時は該当ケース名の集合を事前構築 (LoadCaseName → Level 判定用)
            HashSet<string>? levelCaseNames = null;
            if (SelectedLoadCaseFilter == LoadCaseFilterLevel1 || SelectedLoadCaseFilter == LoadCaseFilterLevel2)
            {
                int targetLevel = SelectedLoadCaseFilter == LoadCaseFilterLevel1 ? 1 : 2;
                levelCaseNames = new HashSet<string>(
                    AllSeismicLoadCases?
                        .Where(lc => lc.Level == targetLevel)
                        .Select(lc => lc.LoadName)
                    ?? []);
            }

            FilteredTables.Clear();
            var filtered = AllTables.Where(t =>
                (SelectedTableCategoryFilter == UiText.All ||
                 Converters.TableCategoryConverter.CategoryOf(t) == SelectedTableCategoryFilter) &&
                // 全条件をまたぐ表 (検定結果) は、条件で絞り込んでも残す。
                // 中身の行に条件の列があるので、そちらで見分けられる。
                (t.SpansAllConditions || (
                    (SelectedLoadCaseFilter == UiText.All
                        || (levelCaseNames != null && levelCaseNames.Contains(t.LoadCaseName))
                        || t.LoadCaseName == SelectedLoadCaseFilter) &&
                    (SelectedLoadCombinationFilter == UiText.All || t.LoadCombinationName == SelectedLoadCombinationFilter) &&
                    (SelectedLiquefactionFilter == UiText.All ||
                     (SelectedLiquefactionFilter == "有" ? t.IsLiquefaction : !t.IsLiquefaction))))
            ).ToList();

            foreach (var t in filtered)
            {
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

            // 荷重条件のメタ列は、表 1 枚が 1 つの荷重条件に対応する場合だけ出す。
            // 全条件をまたぐ表 (検定結果) では表そのものが荷重条件を持たず、
            // 既定値の「液状化 = 無」が出て「液状化を考慮しないケースの結果」と
            // 読めてしまう。またぐ表は行ごとに条件の列を持っている。
            bool withMeta = !SelectedTable.SpansAllConditions;

            // ヘッダ
            var metaHeaders = withMeta ? new[] { "荷重条件", "荷重組合せ", "液状化" } : [];
            writer.WriteLine(string.Join(",",
                metaHeaders.Concat(SelectedTable.Columns.Select(c => Escape(c.Header)))));

            var ci = CultureInfo.InvariantCulture;

            foreach (var row in SelectedTable.Rows)
            {
                var metaValues = withMeta
                    ? new[]
                    {
                        Escape(SelectedTable.LoadCaseName),
                        Escape(SelectedTable.LoadCombinationName),
                        Escape(SelectedTable.LiquefactionLabel)
                    }
                    : [];

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
                        string formatted = c.Format is not null && v is IFormattable f
                            ? f.ToString(c.Format, ci)
                            : v.ToString() ?? "";
                        // Excel 貼付け互換: 数値型の桁区切りコンマを除去
                        // (Excel が列区切りとして再分割する問題を回避)
                        if (v is sbyte or byte or short or ushort or int or uint or long or ulong
                             or float or double or decimal)
                        {
                            formatted = formatted.Replace(",", string.Empty);
                        }
                        return formatted;
                    });
                    sb.AppendLine(string.Join("\t", values));
                }

                Common.ClipboardHelper.TrySetText(sb.ToString());
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[TableWindow.Copy] クリップボードコピー失敗: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}