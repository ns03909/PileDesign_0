using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;

using Serilog;
namespace PileDesign.Services
{
    /// <summary>
    /// Word文書のテーブル生成を担当するサービスクラス
    /// </summary>
    public class TableBuilderService
    {
        private readonly double _defaultFontSize;

        public TableBuilderService(double defaultFontSize = 8.0)
        {
            _defaultFontSize = defaultFontSize;
        }

        /// <summary>
        /// テーブル生成の構成オプション
        /// </summary>
        public class TableConfig
        {
            public double FontSize { get; set; } = 8.0;
            public int[]? ColumnWidths { get; set; }
            public bool StartNumberingFromOne { get; set; } = true;
            public bool AddNumberColumn { get; set; } = true;
        }

        /// <summary>
        /// 列定義
        /// </summary>
        public class ColumnDefinition
        {
            public string[] HeaderTexts { get; set; } = Array.Empty<string>();
            public string Alignment { get; set; } = "center";
            public Func<object, string> ValueFormatter { get; set; } = obj => obj?.ToString() ?? "";
        }

        /// <summary>
        /// 汎用テーブルビルダー
        /// </summary>
        /// <typeparam name="T">データ型</typeparam>
        /// <param name="body">Word文書のBody</param>
        /// <param name="dataItems">データコレクション</param>
        /// <param name="columns">列定義</param>
        /// <param name="config">テーブル設定</param>
        /// <param name="createTableCell">TableCell作成関数</param>
        /// <param name="createHeaderRow">HeaderRow作成関数</param>
        public void BuildTable<T>(
            Body body,
            IEnumerable<T> dataItems,
            IList<ColumnDefinition> columns,
            TableConfig config,
            Func<List<object>, double, string, TableCell> createTableCell,
            Func<TableCell[], TableRow> createHeaderRow)
        {
            try
            {
                // テーブル作成
                Table table = config.ColumnWidths != null && config.ColumnWidths.Length > 0
                    ? CreateTableWithBordersAndWidths(config.ColumnWidths)
                    : CreateTableWithBorders();

                // ヘッダー行作成
                var headerCells = new List<TableCell>();
                if (config.AddNumberColumn)
                {
                    headerCells.Add(createTableCell(["No."], config.FontSize, "center"));
                }

                foreach (var column in columns)
                {
                    headerCells.Add(createTableCell(
                        new List<object>(column.HeaderTexts),
                        config.FontSize,
                        column.Alignment));
                }

                var headerRow = createHeaderRow(headerCells.ToArray());
                table.Append(headerRow);

                // データ行作成
                int no = config.StartNumberingFromOne ? 1 : 0;
                foreach (T item in dataItems)
                {
                    TableRow dataRow = new();

                    if (config.AddNumberColumn)
                    {
                        dataRow.Append(createTableCell([no.ToString()], config.FontSize, "right"));
                        no++;
                    }

                    foreach (var column in columns)
                    {
                        string value = column.ValueFormatter(item!);
                        dataRow.Append(createTableCell([value], config.FontSize, column.Alignment));
                    }

                    table.Append(dataRow);
                }

                body.Append(table);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BuildTableでエラーが発生しました");
            }
        }

        /// <summary>
        /// 単純な2列テーブルビルダー (Key-Valueペア用)
        /// </summary>
        public void BuildKeyValueTable(
            Body body,
            IEnumerable<(string Key, string Value)> items,
            int[]? columnWidths,
            Func<string, string, int, TableCell> createTableCellWithWidth,
            Func<TableCell[], TableRow> createHeaderRow)
        {
            try
            {
                Table table = columnWidths != null && columnWidths.Length == 2
                    ? CreateTableWithBordersAndWidths(columnWidths)
                    : CreateTableWithBorders();

                // 最初のアイテムをヘッダーとして使用
                bool isFirst = true;
                foreach (var (key, value) in items)
                {
                    TableRow row;
                    if (isFirst && columnWidths != null && columnWidths.Length == 2)
                    {
                        row = createHeaderRow([
                            createTableCellWithWidth(key, "left", columnWidths[0]),
                            createTableCellWithWidth(value, "left", columnWidths[1])
                        ]);
                        isFirst = false;
                    }
                    else
                    {
                        row = new TableRow();
                        if (columnWidths != null && columnWidths.Length == 2)
                        {
                            row.Append(
                                createTableCellWithWidth(key, "left", columnWidths[0]),
                                createTableCellWithWidth(value, "left", columnWidths[1])
                            );
                        }
                    }
                    table.Append(row);
                }

                body.Append(table);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BuildKeyValueTableでエラーが発生しました");
            }
        }

        /// <summary>
        /// 枠線付きテーブルを作成
        /// </summary>
        private static Table CreateTableWithBorders()
        {
            Table table = new();
            TableProperties props = new(
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                )
            );
            table.AppendChild(props);
            return table;
        }

        /// <summary>
        /// 列幅指定付きの枠線付きテーブルを作成
        /// </summary>
        private static Table CreateTableWithBordersAndWidths(params int[] columnWidths)
        {
            Table table = new();
            TableProperties props = new(
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                )
            );
            table.AppendChild(props);

            TableGrid tableGrid = new();
            foreach (int width in columnWidths)
            {
                tableGrid.Append(new GridColumn { Width = width.ToString() });
            }
            table.AppendChild(tableGrid);

            return table;
        }
    }
}
