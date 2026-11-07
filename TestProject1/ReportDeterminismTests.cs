using DocumentFormat.OpenXml.Packaging;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// 入力を変えずに同じ解析結果から出した計算書は、中身 (本文・表・図) がすべて同じであること。
    ///
    /// <para>同じ結果から出すたびに数値や図が変わるなら、計算書が解析結果とは別の何か (出力のたびに変わる状態・
    /// 画面の今の状態・乱数) を読んでいる。解析結果と計算書の参照元のずれを早く見つけるための網。</para>
    ///
    /// <para>以前は同じ結果から出しても、図の関係の番号・番号付けの定義の識別・図の名前 (一時ファイルの名前) が
    /// 乱数で毎回変わり、中身を比べられなかった。出力日は作成を始めたときに 1 回だけ決め、試験では固定する。</para>
    /// </summary>
    [TestClass]
    public class ReportDeterminismTests
    {
        /// <summary>代表の計算書の名前 (TestResults/report-sample/ に置く。tools/report-layout-check.ps1 が読む)。</summary>
        internal const string SampleReportFileName = "sample-report.docx";

        /// <summary>docx の部品ごとの中身の指紋 (部品の場所 → SHA-256)。zip の時刻などの入れ物の違いは見ない。</summary>
        internal static SortedDictionary<string, string> PartDigests(string path)
        {
            var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using var doc = WordprocessingDocument.Open(path, false);
            var seen = new HashSet<Uri>();
            void Walk(OpenXmlPart part)
            {
                if (!seen.Add(part.Uri)) return;
                using (var s = part.GetStream(FileMode.Open, FileAccess.Read))
                    digests[part.Uri.ToString()] = Convert.ToHexString(SHA256.HashData(s));
                foreach (var child in part.Parts) Walk(child.OpenXmlPart);
            }
            foreach (var p in doc.Parts) Walk(p.OpenXmlPart);
            return digests;
        }

        /// <summary>2 つの docx で中身の違う部品 (片方にしか無いものを含む)。</summary>
        internal static List<string> DifferingParts(string a, string b)
        {
            var da = PartDigests(a);
            var db = PartDigests(b);
            return da.Keys.Union(db.Keys)
                .Where(k => !da.TryGetValue(k, out var x) || !db.TryGetValue(k, out var y) || x != y)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>本文 (document.xml) の最初の違いの前後 (失敗したときに何が違うかを示す)。</summary>
        private static string FirstBodyDifference(string a, string b)
        {
            static string Body(string p)
            {
                using var doc = WordprocessingDocument.Open(p, false);
                return doc.MainDocumentPart?.Document?.OuterXml ?? "";
            }
            string x = Body(a), y = Body(b);
            int i = 0;
            while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
            if (i == x.Length && i == y.Length) return "(本文は同じ)";
            int from = Math.Max(0, i - 120);
            return $"1 回目: …{x.Substring(from, Math.Min(360, x.Length - from))}…\n2 回目: …{y.Substring(from, Math.Min(360, y.Length - from))}…";
        }

        /// <summary>
        /// <b>本題。</b> 水平解析の結果から計算書を 2 回出し、すべての部品 (本文・番号付け・書式・図) が同じであること。
        /// 全章を出す設定で、解析結果を読む表・図 (断面力・N-M・M-φ・検定) を含める。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void TheSameResult_GivesTheSameReport()
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

                string dir = Path.Combine(Path.GetTempPath(), $"pd_report_same_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                try
                {
                    var paths = new List<string>();
                    for (int i = 1; i <= 2; i++)
                    {
                        string path = Path.Combine(dir, $"report_{i}.docx");
                        var doc = new PileDesign.Output.WordDocument(input, vm.CurrentModel!, vm)
                        {
                            FixedOutputDate = new DateTime(2026, 9, 30),
                        };
                        doc.CreateWordDocument(input, path);
                        // 省いた図・表が無いこと。静的なブラシを凍結していなかったとき、全体テストの並び順しだいで
                        // 杭姿図が作れずに省かれていた (別のスレッドで先に作られたブラシを使えない)
                        CollectionAssert.AreEqual(Array.Empty<string>(), doc.OmittedItems.ToArray(),
                            "作成できずに省いた図・表があります: " + string.Join(", ", doc.OmittedItems));
                        paths.Add(path);
                    }

                    var parts = PartDigests(paths[0]);
                    TestSource.AssertScanned(parts.Keys.Count(k => k.Contains("/media/", StringComparison.Ordinal)), 10, "計算書の図");
                    var differing = DifferingParts(paths[0], paths[1]);
                    Assert.AreEqual(0, differing.Count,
                        "同じ解析結果から出した計算書の中身が違います: " + string.Join(", ", differing)
                        + "\n" + FirstBodyDifference(paths[0], paths[1]));

                    // 代表の計算書として残す。レイアウトの検査 (tools/report-layout-check.ps1) が Word で描画して調べる
                    string sampleDir = Path.Combine(TestSource.Dir("TestProject1"), "TestResults", "report-sample");
                    Directory.CreateDirectory(sampleDir);
                    File.Copy(paths[0], Path.Combine(sampleDir, SampleReportFileName), overwrite: true);

                    // 解析モデルの境界条件の表が、杭モデルの図の前にある
                    string text;
                    using (var w = WordprocessingDocument.Open(paths[0], false))
                        text = w.MainDocumentPart!.Document!.Body!.InnerText;
                    int table = text.IndexOf("解析モデルの境界条件", StringComparison.Ordinal);
                    Assert.IsTrue(table >= 0, "解析モデルの境界条件の表がありません");
                    Assert.IsTrue(table < text.IndexOf("水平抵抗解析杭モデル", StringComparison.Ordinal), "表が杭モデルの図より後ろです");

                    // 結果のグラフを 1 ページ 2 枚の高さにした計算書。グラフの高さだけが変わる
                    vm.DocxOutput.ChartsPerPage = 2;
                    string twoPerPage = Path.Combine(dir, "report_2perpage.docx");
                    new PileDesign.Output.WordDocument(input, vm.CurrentModel!, vm) { FixedOutputDate = new DateTime(2026, 9, 30) }
                        .CreateWordDocument(input, twoPerPage);
                    vm.DocxOutput.ChartsPerPage = 3;
                    int Charts(string path, double heightMm) => ImageHeightsMm(path).Count(h => Math.Abs(h - heightMm) < 0.05);
                    double h3 = PileDesign.Output.WordDocument.Layout.ChartHeightMm3PerPage;
                    double h2 = PileDesign.Output.WordDocument.Layout.ChartHeightMm2PerPage;
                    int charts3 = Charts(paths[0], h3);
                    TestSource.AssertScanned(charts3, 5, "1 ページ 3 枚の高さのグラフ");
                    // 地盤グラフは 3 枚の設定でも 2 枚の高さなので、その数を差し引いて比べる
                    Assert.AreEqual(charts3, Charts(twoPerPage, h2) - Charts(paths[0], h2), "1 ページ 2 枚にしたとき、高さの変わらないグラフがあります");
                    Assert.AreEqual(0, Charts(twoPerPage, h3), "1 ページ 2 枚にしたのに 3 枚の高さのグラフが残っています");
                    // 地盤グラフは設定によらず常に 1 ページ 2 枚の高さ (縦長の分布図は 3 枚の高さでは詰まる)
                    var ground = ImageHeightsBeforeCaption(paths[0], "N値分布").Concat(ImageHeightsBeforeCaption(twoPerPage, "N値分布")).ToList();
                    TestSource.AssertScanned(ground.Count, 2, "N値分布の図");
                    Assert.IsTrue(ground.All(h => Math.Abs(h - h2) < 0.05), "地盤グラフが 1 ページ 2 枚の高さになっていません: " + string.Join(", ", ground));
                    File.Copy(twoPerPage, Path.Combine(sampleDir, "sample-report-2perpage.docx"), overwrite: true);
                }
                finally { Directory.Delete(dir, recursive: true); }
            }
        }

        /// <summary>本文に貼った画像の高さ (mm)。</summary>
        private static List<double> ImageHeightsMm(string path)
        {
            using var doc = WordprocessingDocument.Open(path, false);
            return doc.MainDocumentPart!.Document!.Body!
                .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>()
                .Select(e => (e.Cy?.Value ?? 0) / 36000.0)
                .ToList();
        }

        /// <summary>題に <paramref name="caption"/> を含む図の、題の直前に貼った画像の高さ (mm)。</summary>
        private static List<double> ImageHeightsBeforeCaption(string path, string caption)
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var heights = new List<double>();
            double? last = null;
            foreach (var para in doc.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var extent = para.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
                if (extent != null) { last = (extent.Cy?.Value ?? 0) / 36000.0; continue; }
                string text = para.InnerText;
                if (last.HasValue && text.StartsWith("図", StringComparison.Ordinal) && text.Contains(caption, StringComparison.Ordinal))
                {
                    heights.Add(last.Value);
                    last = null;
                }
            }
            return heights;
        }

        /// <summary>出力に乱数・時刻を直接使わない (図の関係の番号・番号付けの識別・図の名前・出力日)。</summary>
        [TestMethod]
        public void TheReportDoesNotUseRandomOrClockDirectly()
        {
            int scanned = 0;
            foreach (var file in Directory.EnumerateFiles(TestSource.Dir("Graphics_r1", "Output"), "WordDocument*.cs"))
            {
                scanned++;
                string name = Path.GetFileName(file);
                foreach (var (line, no) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    // 一時ファイルの名前に乱数を使うのはよい (計算書の中には入らない)
                    bool tempFile = line.Contains("GetTempPath", StringComparison.Ordinal);
                    Assert.IsFalse(line.Contains("Guid.NewGuid", StringComparison.Ordinal) && !tempFile,
                        $"{name}:{no} 計算書の中身に乱数を使っています: {line.Trim()}");
                    Assert.IsFalse(line.Contains("DateTime.Now", StringComparison.Ordinal)
                                   && !line.Contains("FixedOutputDate ?? DateTime.Now", StringComparison.Ordinal)
                                   && !line.Contains("_outputDate = DateTime.Now", StringComparison.Ordinal),
                        $"{name}:{no} 計算書の中身に今の時刻を使っています (出力日は _outputDate): {line.Trim()}");
                }
            }
            TestSource.AssertScanned(scanned, 15, "計算書の実装");

            // 図の部品は番号を順に振る入口を通す (番号を渡さずに足すと、部品が乱数で振る)
            int images = 0;
            foreach (var file in Directory.EnumerateFiles(TestSource.Dir("Graphics_r1", "Output"), "*.cs"))
            {
                foreach (var (line, no) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
                {
                    if (!line.Contains("AddImagePart(", StringComparison.Ordinal)) continue;
                    images++;
                    Assert.IsTrue(line.Contains("return mainPart.AddImagePart(type, id);", StringComparison.Ordinal),
                        $"{Path.GetFileName(file)}:{no} 図の部品を WordDrawingBuilder.AddImagePartInOrder を通さずに足しています: {line.Trim()}");
                }
            }
            Assert.AreEqual(1, images, "(前提) 図の部品を足すのは入口の 1 か所だけ");
        }
    }
}
