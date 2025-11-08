using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Data;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace PileDesignCore.Output
{
    internal class WordDocument
    {
        // コンストラクタ
        public WordDocument() { }

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

            Console.WriteLine("Word文書が作成され、保存されました。");
        }

        // テキストを追加するメソッド
        public static void AddText(Body body, string _string)
        {
            // ドキュメントにテキストを追加
            Paragraph paragraph = new Paragraph();
            Run run = new Run();
            Text text = new Text(_string);
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

