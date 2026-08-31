using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Int32Value = DocumentFormat.OpenXml.Int32Value;
using NumberingFormat = DocumentFormat.OpenXml.Wordprocessing.NumberingFormat;
using Point = System.Windows.Point;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using WpStyle = DocumentFormat.OpenXml.Wordprocessing.Style;

using Serilog;

namespace PileDesign.Output
{
    // 文字列・段落・表の汎用ヘルパ: タイトル/見出し/本文/上付き下付き変換/表罫線・列幅。物理分割 partial (純粋移動)。
    internal partial class WordDocument
    {
        // タイトルを追加するメソッド
        public static void AddTitle(Body body, string titleText, double fontSize = 16)
        {
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Center },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Title" }
                }
            };

            Run run = new()
            {
                RunProperties = new RunProperties
                {
                    FontSize = new FontSize { Val = (fontSize * 2).ToString() } // (1ポイント = 2半角文字)
                }
            };

            Text text = new(titleText);
            run.Append(text);
            paragraph.Append(run);
            body.Append(paragraph);
        }

        // 章タイトルを追加するメソッド（$...$ 内を TeX 数式として処理できるように拡張）
        public static void AddHeader1(Body body, string header1Title, int outlineLevel = 0, double fontSize = 12)
        {
            if (body == null) return;

            int headingLevel = Math.Clamp(outlineLevel <= 0 ? 1 : outlineLevel, 1, 9);
            string styleId = $"Heading{headingLevel}";
            int outline = headingLevel - 1;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = styleId },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header1Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize, Layout.HeadingFontName))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }

        // 見出し2（$...$ を TeX として処理・<^x>/< _x> の簡易上付下付も維持）
        public static void AddHeader2(Body body, string header2Title, int outlineLevel = 1, double fontSize = 12)
        {
            if (body == null) return;

            int outline = Math.Max(0, outlineLevel);
            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Heading2" },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header2Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize, Layout.HeadingFontName))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }

        // 見出し3（$...$ を TeX として処理・<^x>/< _x> の簡易上付下付も維持）
        public static void AddHeader3(Body body, string header3Title, int outlineLevel = 1, double fontSize = 12)
        {
            if (body == null) return;

            int outline = Math.Max(0, outlineLevel);
            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Heading3" },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header3Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize, Layout.HeadingFontName))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }


        // 上付き、下付き文字
        // サポートする記法:
        //   <^...> または ^{...} : 上付き文字
        //   <_...> または _{...} : 下付き文字
        public static List<Run> ConvertStringToRunsWithSuperSub(
            string text, double fontSize = 10.5, string? fontName = null)
        {
            var runs = new List<Run>();
            int pos = 0;
            // 既存の <^...>, <_...> に加え、LaTeX形式の ^{...}, _{...} もサポート
            var pattern = @"\<\^(.*?)\>|<_(.*?)\>|\^{(.*?)}|_{(.*?)}";

            var matches = Regex.Matches(text, pattern);

            foreach (Match match in matches)
            {
                // 通常文字
                if (match.Index > pos)
                {
                    string normalText = text[pos..match.Index];
                    runs.Add(new Run(
                        new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() }, RunFonts = CreateRunFonts(fontName) },
                        new Text(normalText)
                    ));
                }

                if (match.Groups[1].Success || match.Groups[3].Success) // 上付き (<^...> or ^{...})
                {
                    string superText = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
                            RunFonts = CreateRunFonts(fontName),
                            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }
                        },
                        new Text(superText)
                    ));
                }
                else if (match.Groups[2].Success || match.Groups[4].Success) // 下付き (<_...> or _{...})
                {
                    string subText = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
                            RunFonts = CreateRunFonts(fontName),
                            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Subscript }
                        },
                        new Text(subText)
                    ));
                }

                pos = match.Index + match.Length;
            }

            // 残りの通常文字
            if (pos < text.Length)
            {
                string normalText = text[pos..];
                runs.Add(new Run(
                    new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() }, RunFonts = CreateRunFonts(fontName) },
                    new Text(normalText)
                ));
            }

            return runs;
        }

        /// <summary>
        /// 表・図の前置き説明文（本文よりやや小さめ）。初見の読者向けに
        /// 「この表・図が何を示すか」「記号の意味」を 1〜3 行で説明する用途。
        /// </summary>
        public static void AddIntroText(Body body, string text) => AddText(body, text, "left", 9.5);

        /// <summary>
        /// 表・図の下に置く注記（「※ …」）。本文より小さく、明朝で組む。
        /// 本文はゴシックなので、書体を変えることで<b>本文ではなく直前の表への注</b>だと分かる。
        /// </summary>
        public static void AddTableNote(Body body, string text) =>
            AddText(body, text, "left", Layout.NoteFontSize, Layout.NoteFontName);

        // テキストを追加するメソッド
        public static void AddText(Body body, string textContent, string alignment = "left",
            double fontSize = 10.5, string? fontName = null)
        {
            if (body == null) return;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = alignment.ToLower() switch
                    {
                        "center" => new Justification { Val = JustificationValues.Center },
                        "right" => new Justification { Val = JustificationValues.Right },
                        _ => new Justification { Val = JustificationValues.Left },
                    }
                }
            };

            if (string.IsNullOrEmpty(textContent))
            {
                body.Append(paragraph); // 空行
                return;
            }

            // 改行対応：行ごとに BuildInlineMixedRuns を適用し、行間に Break を挿入
            string[] lines = textContent.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var element in BuildInlineMixedRuns(lines[i], fontSize, fontName))
                    paragraph.Append(element);

                if (i < lines.Length - 1)
                    paragraph.Append(new Run(new Break()));
            }

            body.Append(paragraph);
        }

        /// <summary>
        /// すべての表の行に「行の途中で改ページしない」(cantSplit) を設定する。
        ///
        /// Word の既定は行を分割できるため、ページ末尾に来た行が上下に割れ、
        /// 前のページに空のセルだけが残ることがある。
        /// 行を作る箇所が多数に散っているので、組み上がった本文に対して一度だけ通す。
        /// （1 行だけで 1 ページに収まらない場合は、Word がこの指定を無視して分割する）
        /// </summary>
        internal static void PreventTableRowsFromSplittingAcrossPages(Body body)
        {
            foreach (var row in body.Descendants<TableRow>())
            {
                var rowProperties = row.GetFirstChild<TableRowProperties>();
                if (rowProperties == null)
                {
                    // OpenXML スキーマ上 trPr は行の先頭要素
                    rowProperties = new TableRowProperties();
                    row.PrependChild(rowProperties);
                }
                if (rowProperties.Elements<CantSplit>().Any()) continue;

                // CT_TrPr の並びは cantSplit → trHeight → tblHeader
                var successor = rowProperties.ChildElements
                    .FirstOrDefault(e => e is TableRowHeight or TableHeader);
                if (successor == null) rowProperties.Append(new CantSplit());
                else rowProperties.InsertBefore(new CantSplit(), successor);
            }
        }

        // テーブルに枠線を追加するメソッド
        // 表は紙面幅いっぱい（100%）に設定し、列幅は内容に応じて自動調整
        private static Table CreateTableWithBorders()
        {
            Table table = new();
            // OpenXML スキーマ (CT_TblPr / CT_TblBorders) の子要素順序に従う:
            // tblW → tblBorders → tblLayout、罫線は top → left → bottom → right → insideH → insideV
            TableProperties props = new(
                // 表の幅を100%（紙面いっぱい）に設定
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                ),
                // 列幅を内容に応じて自動調整
                new TableLayout { Type = TableLayoutValues.Autofit }
            );
            table.AppendChild(props);
            return table;
        }

        private static Table CreateTableWithBordersAndWidths(params int[] columnWidths)
        // 列幅の比率を維持しながら、表は紙面幅いっぱいに設定
        {
            Table table = new();
            // 罫線は OpenXML スキーマ順 (top → left → bottom → right → insideH → insideV)
            TableProperties props = new(
                // 表の幅を100%（紙面いっぱい）に設定
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
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

        // テーブルセル
        private static TableCell CreateTableCellWithWidth(string text, string alignment = "left", int width = 0, double fontSize = 8)
        {
            TableCell tableCell = new();
            // OpenXML スキーマ (CT_TcPr) の子要素順序: tcW → … → vAlign
            TableCellProperties tableCellProperties = new();
            if (width > 0)
            {
                TableCellWidth tableCellWidth = new() { Type = TableWidthUnitValues.Dxa, Width = width.ToString() };
                tableCellProperties.Append(tableCellWidth);
            }

            TableCellVerticalAlignment tableCellVerticalAlignment = new() { Val = TableVerticalAlignmentValues.Center };
            tableCellProperties.Append(tableCellVerticalAlignment);

            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties()
            };

            paragraph.ParagraphProperties.Justification = alignment.ToLower() switch
            {
                "center" => new Justification { Val = JustificationValues.Center },
                "right" => new Justification { Val = JustificationValues.Right },
                "left" => new Justification { Val = JustificationValues.Left },
                _ => new Justification { Val = JustificationValues.Left },
            };

            Run run = new()
            {
                RunProperties = new RunProperties
                {
                    FontSize = new FontSize { Val = (fontSize * 2).ToString() } // フォントサイズを設定
                }
            };

            // テキストを改行位置で分割し、各行を追加
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    run.Append(new Break());
                }
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }

            paragraph.Append(run);
            tableCell.Append(tableCellProperties);
            tableCell.Append(paragraph);

            return tableCell;
        }

        // テーブルヘッダー行設定のためのメソッド
        private static TableRow CreateHeaderRow(params TableCell[] cells)
        {
            TableRow headerRow = new();

            // ヘッダー行として設定 (OpenXML スキーマ上 trPr は行の先頭要素)
            TableRowProperties rowProperties = new();
            rowProperties.Append(new TableHeader());
            headerRow.Append(rowProperties);

            foreach (var cell in cells)
            {
                headerRow.Append(cell);
            }

            return headerRow;
        }

        private static void SetColumnWidth(Table table, int columnIndex, int width)
        {
            try
            {
                TableGrid tableGrid = table.GetFirstChild<TableGrid>();
                if (tableGrid == null)
                {
                    tableGrid = new TableGrid();
                    for (int i = 0; i < columnIndex + 1; i++)
                    {
                        tableGrid.Append(new GridColumn());
                    }
                    table.AppendChild(tableGrid);
                }

                if (tableGrid.ChildElements.Count <= columnIndex)
                {
                    for (int i = tableGrid.ChildElements.Count; i <= columnIndex; i++)
                    {
                        tableGrid.Append(new GridColumn());
                    }
                }

                GridColumn gridColumn = (GridColumn)tableGrid.ChildElements[columnIndex];
                gridColumn.Width = width.ToString();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SetColumnWidthでエラーが発生しました");
            }
        }

    }
}
