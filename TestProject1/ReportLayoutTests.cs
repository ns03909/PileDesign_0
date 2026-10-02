using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 計算書のレイアウト。描画して初めて分かるもの (見出しだけのページ・図と題の泣き別れ・はみ出し) は、
    /// リリースの確認で代表の計算書を Word に描画させて調べる (tools/report-layout-check.ps1)。
    /// ここでは、そこで見つかった誤りの直し方と、検査の手順そのものが外れていないことを見る。
    /// </summary>
    [TestClass]
    public class ReportLayoutTests
    {
        /// <summary>
        /// 見出しは次の段落と同じページに置く。無いと、見出しがページの最後に残り、本文や図が次のページから始まる
        /// (代表の計算書で「杭の図・諸元」「液状化の検討」がそうなっていた)。
        /// </summary>
        [TestMethod]
        public void HeadingsKeepWithTheNextParagraph()
        {
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                PileDesign.Output.WordDocument.EnsureHeadingStylesWithNumbering(main);

                var headings = main.StyleDefinitionsPart!.Styles!.Elements<Style>()
                    .Where(s => s.StyleId?.Value?.StartsWith("Heading") == true).ToList();
                TestSource.AssertScanned(headings.Count, 3, "見出しの書式");
                foreach (var h in headings)
                {
                    Assert.IsNotNull(h.StyleParagraphProperties?.KeepNext, $"{h.StyleId}: 次の段落と同じページに置く設定がありません");
                    Assert.IsNotNull(h.StyleParagraphProperties?.KeepLines, $"{h.StyleId}: 段落の途中でページを分けない設定がありません");
                }
            }
        }

        /// <summary>
        /// 表題は表と同じページに置く (表題は表の直前にある)。無いと表題だけがページの最後に残り、表が次のページから始まる
        /// (検定の根拠の表で 2 か所そうなった)。図の題は図の直後なので付けない (付けると題が次の段落へ引っ張られる)。
        /// </summary>
        [TestMethod]
        public void TableTitlesKeepWithTheTable_FigureTitlesDoNot()
        {
            var doc = new PileDesign.Output.WordDocument(new PileDesign.Models.InputData.InputModel(), null!, null!);
            var body = new Body();
            doc.AddAutoFigureCaption(body, "表の題", "表");
            doc.AddAutoFigureCaption(body, "図の題", "図");
            var captions = body.Elements<Paragraph>().ToList();
            Assert.AreEqual(2, captions.Count);
            Assert.IsNotNull(captions[0].ParagraphProperties?.KeepNext, "表題に「次の段落と同じページに置く」がありません");
            Assert.IsNull(captions[1].ParagraphProperties?.KeepNext, "図の題に「次の段落と同じページに置く」が付いています");
        }

        /// <summary>リリースの確認は、代表の計算書のレイアウトの検査を行う (Word が無い PC のために飛ばす指定もある)。</summary>
        [TestMethod]
        public void TheReleaseCheckRunsTheLayoutCheck()
        {
            string release = File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "release-check.ps1"));
            StringAssert.Contains(release, "report-layout-check.ps1");
            StringAssert.Contains(release, "SkipReportLayout");

            string path = Path.Combine(TestSource.Dir(), "tools", "report-layout-check.ps1");
            var raw = File.ReadAllBytes(path);
            Assert.IsTrue(raw.Length > 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF,
                "report-layout-check.ps1 が BOM 付き UTF-8 ではありません (Windows PowerShell 5.1 で日本語が化けます)");
            string script = File.ReadAllText(path);
            // 検査する計算書は全体テストが作るもの
            StringAssert.Contains(script, ReportDeterminismTests.SampleReportFileName);
            // Word へ渡す文字列は素の型にする。Join-Path の返す文字列のまま渡すと Word が応答を返さなくなった
            StringAssert.Contains(script, "$doc.SaveAs2([string]$pdf, [int]$wdExportFormatPDF)");
            StringAssert.Contains(script, "$word.Documents.Open([string]$Docx");
        }

        /// <summary>
        /// 環境の違いを見る基準: 図・表の数 (環境に依らない) と、環境ごとのページ数、計算書が指定するフォント。
        /// 検査は、フォントがその PC に入っているかを英語名・日本語名の両方で照らす (「游明朝」は英語名では Yu Mincho)。
        /// </summary>
        [TestMethod]
        public void TheLayoutBaseline_CoversCountsPagesAndFonts()
        {
            string path = Path.Combine(TestSource.Dir(), "tools", "report-layout-baseline.json");
            Assert.IsTrue(File.Exists(path), "計算書のレイアウトの基準がありません (tools/report-layout-check.ps1 -Update で作る)");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            Assert.IsTrue(root.GetProperty("Figures").GetInt32() > 0, "図の数");
            Assert.IsTrue(root.GetProperty("Tables").GetInt32() > 0, "表の数");
            TestSource.AssertScanned(root.GetProperty("PagesByEnvironment").EnumerateObject().Count(), 1, "ページ数の基準の環境");
            TestSource.AssertScanned(root.GetProperty("Fonts").GetArrayLength(), 1, "計算書が指定するフォント");

            string script = File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "report-layout-check.ps1"));
            StringAssert.Contains(script, "report-layout-baseline.json");
            StringAssert.Contains(script, "InstalledFontCollection");
            StringAssert.Contains(script, "GetName(1041)");
        }
    }
}
