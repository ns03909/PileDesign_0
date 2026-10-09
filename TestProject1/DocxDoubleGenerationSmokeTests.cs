using PileDesign.ViewModels;
using System;
using System.IO;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// docx 計算書を同一プロセス内で 2 回連続生成するスモークテスト。
    /// 実機で「1 回目成功 → 2 回目 StackOverflowException」が報告されたため、
    /// 生成経路に状態依存の再帰・無限ループがないことを回帰ガードする。
    /// StackOverflow はテストホストごと落ちるため、失敗時は「テスト失敗」ではなく
    /// 「テスト実行のクラッシュ」として現れる点に注意。
    /// </summary>
    [TestClass]
    public class DocxDoubleGenerationSmokeTests
    {
        [TestMethod]
        [Timeout(600000)]
        public void CreateWordDocument_TwiceInSameProcess_Succeeds()
        {
            Exception? threadEx = null;

            // WordDocument は WPF DrawingVisual / RenderTargetBitmap を使うため STA スレッドで実行
            var thread = new Thread(() =>
            {
                try
                {
                    var (inputModel, error) = IntegrationTests.BuildExampleInputModel("Example3_1", "PileExample3_1");
                    if (inputModel == null)
                    {
                        Assert.Inconclusive($"例題ロード失敗: {error}");
                        return;
                    }

                    // 例題ビルダーは FundamentalInput を設定しない場合がある (docx は必須参照)
                    inputModel.FundamentalInput ??= new PileDesign.Models.InputData.FundamentalInput();

                    var mainVm = new MainWindowViewModel
                    {
                        CurrentInputModel = inputModel
                    };

                    // 水平解析をヘッドレス実行 (解析結果込みの計算書 = 実機と同じ経路)
                    var hcvm = new HorizontalCalculationViewModel(mainVm)
                    {
                        BypassUiPromptsForTesting = true,
                        MaxCaseDegreeOfParallelism = 1,
                    };
                    hcvm.ExecuteAnalysisCommand.Execute(null);
                    var deadline = DateTime.UtcNow.AddMinutes(8);
                    while (hcvm.IsAnalysisRunning && DateTime.UtcNow < deadline)
                        Thread.Sleep(100);
                    Assert.IsFalse(hcvm.IsAnalysisRunning, "水平解析がタイムアウトしました");

                    mainVm.CurrentModel = hcvm.CurrentModel;
                    mainVm.IsHorizontalAnalysisDone = hcvm.CurrentModel != null;

                    // 実機報告時の状況に合わせ、全セクション ON + 簡易レベル
                    mainVm.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
                    mainVm.DocxOutput.CalculationReportLevel = 1;

                    string dir = Path.Combine(Path.GetTempPath(), "PileDesignDocxSmoke");
                    Directory.CreateDirectory(dir);

                    for (int i = 1; i <= 2; i++)
                    {
                        string path = Path.Combine(dir, $"docx_double_{i}.docx");
                        if (File.Exists(path)) File.Delete(path);

                        var doc = new PileDesign.Output.WordDocument(inputModel, mainVm.CurrentModel, mainVm);
                        doc.CreateWordDocument(inputModel, path);

                        var info = new FileInfo(path);
                        Assert.IsTrue(info.Exists && info.Length > 10_000,
                            $"{i} 回目の docx が生成されていない/小さすぎる ({(info.Exists ? info.Length : 0)} bytes)");

                        // 図の枠 (wp:docPr) の Id は文書の中で重ならないこと。以前は全部 1 で、Word が修復を求めることがあった
                        using (var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false))
                        {
                            var ids = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(
                                word.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>(),
                                d => d.Id!.Value));
                            Assert.IsTrue(ids.Count > 1, "(前提) 図がありません");
                            Assert.AreEqual(ids.Count, System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(ids)), "図の Id が重なっています");
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadEx != null) Assert.Fail("STA スレッド内で例外:\n" + threadEx);
        }
    }
}
