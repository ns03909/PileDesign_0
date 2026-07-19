using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace PileDesign.Output
{
    // 解析サマリーレポート (水平解析完了時に HCM が cache した text) を docx に貼り付け。
    // 等幅フォント (MS Gothic) で計算ログ画面と同じ見た目になる。
    internal partial class WordDocument
    {
        private void AddAnalysisSummaryReportSection(Body body)
        {
            if (mainWindowViewModel == null) return;
            if (!mainWindowViewModel.DocxOutput.IncludeAnalysisSummaryReport) return;

            string text = mainWindowViewModel.DocxOutput.LastAnalysisSummaryText;
            if (string.IsNullOrWhiteSpace(text))
            {
                AddText(body, "（水平解析サマリーレポートは未生成。F5 で水平解析を実行後に出力してください）", "left");
                return;
            }

            AddHeader1(body, "水平解析 サマリーレポート", 1);
            AddPreformattedText(body, text);
        }

        // 等幅フォント (MS ゴシック) で改行を保持して段落として追加。
        // 6pt フォントに合わせた極小の行間 (Exact 7pt) を指定して縦方向を最小化する。
        private static void AddPreformattedText(Body body, string text)
        {
            var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            foreach (var line in lines)
            {
                var paragraph = new Paragraph();
                var pPr = new ParagraphProperties();
                // 行間: 固定 7pt (= 140 twentieths)、段落前後 0。
                // 6pt フォントに対し +1pt のみで詰める。Exact 指定で Word の最小行高
                // (デフォルト 10.5pt) に引っ張られないようにする。
                pPr.Append(new SpacingBetweenLines
                {
                    Before = "0", After = "0",
                    Line = "140", LineRule = LineSpacingRuleValues.Exact
                });
                // CJK 用に SnapToGrid を無効化 (文書グリッドに揃えると行高が拡大される)
                pPr.Append(new SnapToGrid { Val = false });
                paragraph.Append(pPr);

                var run = new Run();
                var rPr = new RunProperties();
                // 等幅フォント (Consolas/MS Gothic と一致)
                rPr.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", EastAsia = "ＭＳ ゴシック", ComplexScript = "Consolas" });
                // 6pt: 約 100 文字 / 行 を A4 縦 (利用幅 17cm) に収めるサイズ
                rPr.Append(new FontSize { Val = "12" }); // 6pt
                rPr.Append(new FontSizeComplexScript { Val = "12" });
                run.Append(rPr);
                run.Append(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
                paragraph.Append(run);

                body.Append(paragraph);
            }
        }
    }
}
