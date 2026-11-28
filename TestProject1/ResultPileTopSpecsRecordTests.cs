using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.PileLibrary;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace TestProject1
{
    /// <summary>
    /// 結果付きのファイルを再解析せずに計算書にしたとき、杭頭諸元の表 (いまの計算方法で組み直す) と、
    /// 応力 (解析したときの Ke・Mu などで解いた) が食い違うことを計算書で断る (2026-11-28 のレビューの指摘 P1)。
    /// <para>水平解析を終えたときに杭頭諸元を解析の記録 (AnalysisRunMetadata.PileTopSpecs) に残し、計算書は記録と比べて、
    /// 違う項目と解析時の値を注記する。記録の無い旧版の結果は、その旨と再解析を促す。</para>
    /// </summary>
    [TestClass]
    public class ResultPileTopSpecsRecordTests
    {
        private static readonly Spec[] Current =
        [
            new("パイルキャップ コンクリート基準強度", "Fc", "24", "N/mm2"),
            new("初期回転剛性 (圧縮時)", "Ke", "1,037,490", "kN·m/rad"),
        ];

        private static AnalysisRunMetadata Record(string version, params (string Name, string Value)[] specs) => new()
        {
            Kind = AnalysisKind.Horizontal,
            ApplicationVersion = version,
            PileTopSpecs = specs.Length == 0 ? [] :
            [
                new AnalysisPileTopSpecs
                {
                    PileBodyNo = 1, PileTopType = "キャプリングパイル工法",
                    Specs = [.. specs.Select(s => new AnalysisConditionEntry(s.Name, s.Value))],
                },
            ],
        };

        [TestMethod]
        public void TheNote_ComparesTheCurrentSpecsWithTheRecordOfTheAnalysis()
        {
            // 水平解析の結果が無ければ断らない
            Assert.IsNull(PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, false, Record("1.2.0")));

            // 記録の無い旧版の結果: 版と再解析を書く
            string? old = PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true, Record("1.1.0"));
            StringAssert.Contains(old, "杭頭諸元を記録していない版 (プログラムの版 1.1.0)");
            StringAssert.Contains(old, "水平解析をやり直す");
            StringAssert.Contains(PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true, null), "版 不明");

            // 記録と同じなら断らない
            Assert.IsNull(PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true,
                Record("1.2.0", ("初期回転剛性 (圧縮時) Ke", "1,037,490 kN·m/rad"), ("パイルキャップ コンクリート基準強度 Fc", "24 N/mm2"))));

            // 計算方法が変わって値が違う: 項目と解析時・現在の値を書く
            string? changed = PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true,
                Record("1.2.0", ("初期回転剛性 (圧縮時) Ke", "1,023,000 kN·m/rad"), ("パイルキャップ コンクリート基準強度 Fc", "24 N/mm2")));
            StringAssert.Contains(changed, "初期回転剛性 (圧縮時) Ke (解析時 1,023,000 kN·m/rad → 現在 1,037,490 kN·m/rad)");
            Assert.IsFalse(changed!.Contains("基準強度", StringComparison.Ordinal), "同じ値の項目まで挙げています");

            // 解析のあとに杭頭タイプを変えた (記録はあるが、この杭体・杭頭タイプのものが無い)
            StringAssert.Contains(PileTopSpecsBuilder.ChangeNote(1, "キャプテンパイル工法", Current, true,
                Record("1.2.0", ("初期回転剛性 (圧縮時) Ke", "1,037,490 kN·m/rad"))), "記録されていない");
        }

        /// <summary>
        /// 通しで: 例題 (キャプリング) を解くと記録に杭頭諸元が残り、保存して開き直しても残る。
        /// 計算方法が変わる前の解析 (記録の Ke が違う) と、記録の無い旧版の結果を再解析せずに計算書にすると、注記が出る。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void AnOldResultReportedWithoutReanalysis_SaysWhichSpecsDiffer()
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
                MessageService.IsUnattended = true;
                var (input, error) = IntegrationTests.BuildExampleInputModel("ExampleCAP3_7", "PileExampleCAP3_7");
                if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
                input!.FundamentalInput ??= new PileDesign.Models.InputData.FundamentalInput();

                var vm = new MainWindowViewModel { CurrentInputModel = input };
                var hcvm = new HorizontalCalculationViewModel(vm) { BypassUiPromptsForTesting = true, MaxCaseDegreeOfParallelism = 4 };
                hcvm.ExecuteAnalysisCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddMinutes(8);
                while (hcvm.IsAnalysisRunning && DateTime.UtcNow < deadline) Thread.Sleep(100);
                Assert.IsFalse(hcvm.IsAnalysisRunning, "水平解析がタイムアウトしました");
                vm.CurrentModel = hcvm.CurrentModel;
                vm.IsHorizontalAnalysisDone = hcvm.CurrentModel != null;
                Assert.IsTrue(vm.IsHorizontalAnalysisDone, "(前提) 水平解析の結果がありません");
                // 画面の「OK」と同じく、結果と解析の記録を組にする
                vm.CaptureAnalysisResultSet(hcvm.SettingsAtLastRunForTesting ?? hcvm.CaptureRunSettings());

                var record = vm.CurrentResultSet!.RunRecords.Single(r => r.Kind == AnalysisKind.Horizontal);
                var body = record.PileTopSpecs.FirstOrDefault(p => p.PileTopType == "キャプリングパイル工法");
                Assert.IsNotNull(body, "水平解析の記録に杭頭諸元が残っていません");
                var ke = body!.Specs.Single(e => e.Name == "初期回転剛性 (圧縮時) Ke");
                // 解析が使う Ke は、解析の入口と同じく杭体の値を配ってから求めたもの (控えの複製は派生値を配り直すまで古い)
                var analyzed = vm.CurrentResultSet.InputSnapshot!.PileBodies[body.PileBodyNo - 1];
                analyzed.ApplyTopSectionToPileTop();
                Assert.AreEqual($"{analyzed.PileTop.CapringPile!.Ke * 1e-6:N0} kN·m/rad", ke.Value, "記録の Ke が解析に使った値と違います");

                // 保存して開き直しても記録が残る
                var reopened = SaveAndReopen(vm);
                var reopenedRecord = reopened.CurrentResultSet!.RunRecords.Single(r => r.Kind == AnalysisKind.Horizontal);
                Assert.AreEqual(record.PileTopSpecs.Count, reopenedRecord.PileTopSpecs.Count, "保存して開き直すと杭頭諸元の記録が消えます");
                Assert.IsTrue(reopened.IsHorizontalAnalysisDone, "(前提) 開き直しても水平解析の結果がある");

                string dir = Path.Combine(Path.GetTempPath(), $"pd_specs_record_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                try
                {
                    reopened.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);

                    // 記録と同じ: 注記なし
                    string same = Report(reopened, Path.Combine(dir, "same.docx"));
                    Assert.IsFalse(same.Contains("水平解析のときの値と異なる", StringComparison.Ordinal), "値が同じなのに注記しています");

                    // 計算方法が変わる前に解析した結果 (記録の Ke が違う) を、再解析せずに出す
                    var keEntry = reopenedRecord.PileTopSpecs.First(p => p.PileTopType == "キャプリングパイル工法").Specs
                        .Single(e => e.Name == "初期回転剛性 (圧縮時) Ke");
                    string recomputed = keEntry.Value;
                    keEntry.Value = "1,023,000 kN·m/rad";
                    string changed = Report(reopened, Path.Combine(dir, "changed.docx"));
                    StringAssert.Contains(changed, $"初期回転剛性 (圧縮時) Ke (解析時 1,023,000 kN·m/rad → 現在 {recomputed})");
                    StringAssert.Contains(changed, "一致させるには水平解析をやり直す");

                    // 杭頭諸元を記録していない旧版の結果
                    reopenedRecord.PileTopSpecs = [];
                    reopenedRecord.ApplicationVersion = "1.1.0";
                    string old = Report(reopened, Path.Combine(dir, "old.docx"));
                    StringAssert.Contains(old, "杭頭諸元を記録していない版 (プログラムの版 1.1.0)");
                }
                finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
            }
        }

        private static string Report(MainWindowViewModel vm, string path)
        {
            var input = vm.ResultInputModel ?? vm.CurrentInputModel!;
            var doc = new WordDocument(input, vm.CurrentModel!, vm);
            doc.CreateWordDocument(input, path);
            using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
            return word.MainDocumentPart!.Document!.Body!.InnerText;
        }

        private static MainWindowViewModel SaveAndReopen(MainWindowViewModel vm)
        {
            var files = (FileOperationService)typeof(MainWindowViewModel).GetField("_fileOperationService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
            var prepared = files.PrepareSave(vm.CurrentInputModel, vm.CurrentModel, vm.VerticalBeamCaseResults?.ToList(),
                vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.CapturedAt,
                PileFemLinkTable.Build(vm.CurrentResultSet?.InputSnapshot, vm.CurrentResultSet?.AnaModel),
                vm.IsElementSplit, vm.InputChangedSinceAnalysis, analysisRunRecords: vm.CurrentResultSet?.RunRecords);
            Assert.IsNull(prepared.Error, prepared.Error?.ToString());
            string path = Path.Combine(Path.GetTempPath(), $"pd_specs_record_{Guid.NewGuid():N}.pdj");
            files.WritePrepared(path, prepared);
            try
            {
                var data = files.LoadProjectData(path);
                var opened = new MainWindowViewModel();
                opened.ApplyLoadedProjectData(data, null, "読込が完了しました。");
                return opened;
            }
            finally { File.Delete(path); }
        }
    }
}
