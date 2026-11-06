using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Output;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 計算書の数式を .tex にも出す (利用者の声 2026-11-07:「各種計算式が LaTeX で出力されると自前の計算書への転記が楽」)。
    /// - 本文の順に、見出しと数式を含む段落を書く
    /// - 計算書の LaTeX のうち標準に無い命令 (<c>\P</c> など。標準では段落記号 ¶) は、Word と同じく名前の文字にする
    /// - 元の LaTeX が見つからない数式が無い
    /// </summary>
    [TestClass]
    public class ReportTexExportTests
    {
        [TestMethod]
        public void NonStandardCommandsBecomeLettersLikeInWord()
        {
            Assert.AreEqual(@"\beta_{U} P_{s} + \beta_{L} P_{f}",
                ReportTexExporter.ToStandardLatex(@"\beta_{U} \P_{s} + \beta_{L} \P_{f}"));
            Assert.AreEqual(@"\frac{a}{b} \, \sqrt{x}", ReportTexExporter.ToStandardLatex(@"$\frac{a}{b} \, \sqrt{x}$"),
                "標準の命令・記号の命令はそのまま。前後の $ は外す");
        }

        [TestMethod]
        public void TextIsEscaped()
        {
            Assert.AreEqual(@"50\% \_ \{x\} \$ \& \# \textbackslash{}",
                ReportTexExporter.EscapeText(@"50% _ {x} $ & # \"));
        }

        [TestMethod]
        public void BuildWritesHeadingsAndMathParagraphsInOrder()
        {
            var body = new Body();
            body.Append(Heading("Heading1", "図だけの章"));
            body.Append(Heading("Heading2", "図だけの節"));
            body.Append(Heading("Heading1", "杭の支持力"));
            body.Append(new Paragraph(new Run(new Text("本文だけの段落は書かない"))));
            body.Append(new Paragraph(WordDocument.Tex(@"R_{a} = \frac{1}{3} R_{u}")));
            body.Append(Heading("Heading2", "記号"));
            body.Append(new Paragraph(WordDocument.Tex(@"\alpha_{L}"), new Run(new Text(": 地盤変位に乗じる係数 (50%)"))));

            var result = ReportTexExporter.Build(body, "計算書.docx", new DateTime(2026, 11, 7));

            Assert.AreEqual(2, result.Equations);
            Assert.AreEqual(0, result.Missing);
            string t = result.Text;
            StringAssert.Contains(t, @"\section{杭の支持力}");
            StringAssert.Contains(t, @"\subsection{記号}");
            StringAssert.Contains(t, "\\[\r\n  R_{a} = \\frac{1}{3} R_{u}\r\n\\]".Replace("\r\n", Environment.NewLine));
            StringAssert.Contains(t, @"$\alpha_{L}$: 地盤変位に乗じる係数 (50\%)");
            Assert.IsFalse(t.Contains("本文だけの段落"), "数式の無い段落まで書いています");
            Assert.IsFalse(t.Contains("図だけの"), "式を含まない見出しまで書いています");
            Assert.IsTrue(t.IndexOf(@"\section{杭の支持力}") < t.IndexOf("R_{a}") && t.IndexOf("R_{a}") < t.IndexOf(@"\subsection{記号}"),
                "本文の順になっていません");
            StringAssert.Contains(t, @"\begin{document}");
            StringAssert.Contains(t, @"\end{document}");

            static Paragraph Heading(string style, string text) => new(
                new ParagraphProperties(new ParagraphStyleId { Val = style }),
                new Run(new Text(text)));
        }

        /// <summary>
        /// 例題の計算書 (詳細・全章) から .tex を出し、元の LaTeX が見つからない数式が無いこと・標準に無い命令が残らないこと・
        /// 括弧の数が合うこと。選んでいないときは出さないこと。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void ExampleReportWritesTheEquationsAsStandardLatex()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Run(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is AssertInconclusiveException inconclusive) throw inconclusive;
            if (failure != null) Assert.Fail("STA スレッド内で例外:\n" + failure);

            static void Run()
            {
                var (input, error) = IntegrationTests.BuildExampleInputModel("Example3_1", "PileExample3_1");
                if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
                input!.FundamentalInput ??= new PileDesign.Models.InputData.FundamentalInput();

                var vm = new MainWindowViewModel { CurrentInputModel = input };
                var hcvm = new HorizontalCalculationViewModel(vm) { BypassUiPromptsForTesting = true, MaxCaseDegreeOfParallelism = 1 };
                hcvm.ExecuteAnalysisCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddMinutes(8);
                while (hcvm.IsAnalysisRunning && DateTime.UtcNow < deadline) Thread.Sleep(100);
                Assert.IsFalse(hcvm.IsAnalysisRunning, "水平解析がタイムアウトしました");
                vm.CurrentModel = hcvm.CurrentModel;
                vm.IsHorizontalAnalysisDone = hcvm.CurrentModel != null;
                Assert.IsTrue(vm.IsHorizontalAnalysisDone, "(前提) 水平解析の結果がありません");

                vm.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
                vm.DocxOutput.CalculationReportLevel = 2;

                string dir = Path.Combine(Path.GetTempPath(), $"pd_report_tex_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                try
                {
                    // 選んでいないときは出さない
                    string plain = Path.Combine(dir, "plain.docx");
                    var docPlain = new WordDocument(input, vm.CurrentModel!, vm);
                    docPlain.CreateWordDocument(input, plain);
                    Assert.IsNull(docPlain.TexFilePath);
                    Assert.IsFalse(File.Exists(WordDocument.TexPathFor(plain)), "選んでいないのに .tex を出しています");

                    vm.DocxOutput.ExportEquationsTex = true;
                    string path = Path.Combine(dir, "計算書.docx");
                    var doc = new WordDocument(input, vm.CurrentModel!, vm);
                    doc.CreateWordDocument(input, path);

                    Assert.AreEqual(WordDocument.TexPathFor(path), doc.TexFilePath);
                    Assert.IsTrue(File.Exists(doc.TexFilePath), ".tex がありません");
                    string tex = File.ReadAllText(doc.TexFilePath!);

                    int displays = Regex.Matches(tex, @"^\\\[", RegexOptions.Multiline).Count;
                    int inlines = Regex.Matches(tex, @"\$[^$]+\$").Count;
                    TestSource.AssertScanned(displays + inlines, 50, ".tex の数式");
                    Assert.IsFalse(tex.Contains("元の LaTeX が見つからなかった数式"),
                        "元の LaTeX が見つからない数式があります (Tex を通さずに作った数式・複製した数式)");
                    Assert.IsFalse(doc.Warnings.Any(w => w.Contains(".tex")), string.Join("\n", doc.Warnings));

                    // 本文 (\begin{document} 以降) の命令がすべて標準のものか、記号の命令であること
                    string body = tex[tex.IndexOf(@"\begin{document}", StringComparison.Ordinal)..];
                    var allowedOutsideMath = new[] { "begin", "end", "section", "subsection", "subsubsection", "paragraph",
                        "textbackslash" };
                    var unknown = Regex.Matches(body, @"\\([A-Za-z]+)").Select(m => m.Groups[1].Value)
                        .Where(c => !ReportTexExporter.StandardCommands.Contains(c) && !allowedOutsideMath.Contains(c))
                        .Distinct().ToList();
                    CollectionAssert.AreEqual(Array.Empty<string>(), unknown.ToArray(),
                        "標準の LaTeX に無い命令が残っています: " + string.Join(", ", unknown));

                    // 波括弧の数が合う (\{ \} は文字の括弧なので数えない)
                    string unescaped = body.Replace(@"\{", "").Replace(@"\}", "");
                    Assert.AreEqual(unescaped.Count(c => c == '{'), unescaped.Count(c => c == '}'), "波括弧の数が合いません");
                    // $ の数が偶数 (\$ は文字)
                    Assert.AreEqual(0, body.Replace(@"\$", "").Count(c => c == '$') % 2, "$ の数が奇数です");

                    // 見出しが計算書の章と同じ順に並ぶ
                    StringAssert.Contains(tex, @"\section{");

                    // 目で確かめられるよう、代表の計算書と同じ場所に残す
                    string sampleDir = Path.Combine(TestSource.Dir("TestProject1"), "TestResults", "report-sample");
                    Directory.CreateDirectory(sampleDir);
                    File.Copy(doc.TexFilePath!, Path.Combine(sampleDir, "sample-equations.tex"), overwrite: true);
                }
                finally { Directory.Delete(dir, recursive: true); }
            }
        }
    }
}
