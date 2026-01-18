using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Windows.Controls;
using System.Data;

namespace PileDesignCore.Output
{
    internal static class WordDocument
    {

        // Word文書作成メソッド
        public static void CreateWordDocument(string fileName, ApplicationViewModel viewModel)
        {

            // 新しいWord文書を作成
            using (WordprocessingDocument wordDocument = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document))
            {
                // MainDocumentPartを取得
                MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();

                // Documentを作成
                Document doc = new Document();
                Body body = new Body();

                AddText(body, "これは新しいWord文書です。");


                // ドキュメントにBodyを追加
                doc.Append(body);

                // MainDocumentPartにドキュメントを追加
                mainPart.Document = doc;
            }
        }

        // テキストを追加するメソッド
        public static void AddText(Body body, string textContent)
        {
            // ドキュメントにテキストを追加
            Paragraph paragraph = new Paragraph();
            Run run = new Run();
            Text text = new Text(textContent);
            run.Append(text);
            paragraph.Append(run);
            body.Append(paragraph);
        }


        // データグリッドから表を作成するメソッド
        public static void AddTableFromDataGrid(Body body, DataGrid dataGrid)
        {
            Table table = new Table();

            // DataGridの列ヘッダーを追加
            TableRow headerRow = new TableRow();
            foreach (var column in dataGrid.Columns)
            {
                TableCell cell = new TableCell(new Paragraph(new Run(new Text(column.Header.ToString()))));
                headerRow.Append(cell);
            }
            table.Append(headerRow);

            // DataGridのデータを追加
            foreach (var item in dataGrid.Items)
            {
                if (item is DataRowView rowView)
                {
                    TableRow dataRow = new TableRow();
                    foreach (var column in dataGrid.Columns)
                    {
                        string cellText = rowView[column.SortMemberPath]?.ToString() ?? string.Empty;
                        TableCell cell = new TableCell(new Paragraph(new Run(new Text(cellText))));
                        dataRow.Append(cell);
                    }
                    table.Append(dataRow);
                }
            }

            body.Append(table);
        }
    }
}

