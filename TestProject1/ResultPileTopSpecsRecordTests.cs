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

            // 曲線の照合で M-θ が同じと確かめた杭体は、記録が無くても断らない。違う項目は検定などの値の違いとして書く
            Assert.IsNull(PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true, Record("1.1.0"), curveVerified: true));
            StringAssert.Contains(PileTopSpecsBuilder.ChangeNote(1, "キャプリングパイル工法", Current, true,
                Record("1.2.0", ("初期回転剛性 (圧縮時) Ke", "1,023,000 kN·m/rad")), curveVerified: true), "解析に使った曲線と照合して同じ");

            // 曲線の照合の注記: 控えがあって比べたものが無い (どのケースも設定で剛) なら断らない。控えが無い旧版は断る
            var none = new System.Collections.Generic.HashSet<int>();
            Assert.IsNull(PileTopCurveCheck.Note(new PileTopCurveCheck.Result(0, [], none, HasSnapshots: true), true),
                "杭頭を設定で剛にした (M-θ を使っていない) のに、記録が無いと書いています");
            StringAssert.Contains(PileTopCurveCheck.Note(new PileTopCurveCheck.Result(0, [], none, HasSnapshots: false), true), "記録が無い");
            Assert.IsNull(PileTopCurveCheck.Note(new PileTopCurveCheck.Result(0, [], none, HasSnapshots: false), false), "杭頭に M-θ が無いモデルで断っています");

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
                // 杭頭の M-θ を使うには杭を非線形で解く (例題は線形で、杭頭は設定で剛になる)
                foreach (var lc in input.LoadCasesInput.AnalysisTargetSeismicLoadCases) lc.IsPileNonLinear = true;

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

                    // 記録と同じ・曲線も同じ: 注記なし。解析に使った曲線と照合できている
                    var curves = PileTopCurveCheck.Compare(reopened.ResultInputModel, reopened.CurrentModel);
                    Assert.IsTrue(curves.Compared > 0, "(前提) 解析に使った M-θ 曲線の控えが保存されている");
                    Assert.AreEqual(0, curves.Differences.Count, "再解析していないのに、解析時と今の M-θ 曲線が違うと判定しています: "
                        + string.Join(" / ", curves.Differences.Select(d => d.Detail)));
                    var (same, sameWarnings) = Report(reopened, Path.Combine(dir, "same.docx"));
                    Assert.IsFalse(same.Contains("水平解析のときの値と異なる", StringComparison.Ordinal) || same.Contains("M-θ 関係が、いまの", StringComparison.Ordinal),
                        "値が同じなのに注記しています");

                    // 以前の控えは軸力を入れていなかった: 理由の文字から読んで比べられる
                    var spring = reopened.CurrentModel!.RotationalSprings.First(s => !s.CaseMThetaSnapshots.IsEmpty);
                    foreach (var key in spring.CaseMThetaSnapshots.Keys.ToList())
                    {
                        var s0 = spring.CaseMThetaSnapshots[key];
                        Assert.AreEqual(s0.AxialN, PileTopCurveCheck.AxialNOf(new PileDesign.FEM.MThetaCaseSnapshot { SetupReason = s0.SetupReason }),
                            "理由の文字から読んだ軸力が、記録した軸力と違います");
                    }

                    // 計算方法が変わる前に解析した結果 (記録の Ke が違う) を、再解析せずに出す。諸元表を出さない設定でも警告は出る
                    var keEntry = reopenedRecord.PileTopSpecs.First(p => p.PileTopType == "キャプリングパイル工法").Specs
                        .Single(e => e.Name == "初期回転剛性 (圧縮時) Ke");
                    string recomputed = keEntry.Value;
                    keEntry.Value = "1,023,000 kN·m/rad";
                    var (changed, _) = Report(reopened, Path.Combine(dir, "changed.docx"));
                    StringAssert.Contains(changed, $"初期回転剛性 (圧縮時) Ke (解析時 1,023,000 kN·m/rad → 現在 {recomputed})");
                    reopened.DocxOutput.IncludePileTopSpecs = false;
                    var (_, changedWarnings) = Report(reopened, Path.Combine(dir, "changed-nospecs.docx"));
                    Assert.IsTrue(changedWarnings.Any(w => w.Contains("初期回転剛性 (圧縮時) Ke (解析時 1,023,000", StringComparison.Ordinal)),
                        "杭頭諸元の表を出さない設定で、記録との違いを警告していません");
                    keEntry.Value = recomputed;

                    // 表示用の諸元は同じで、M-θ の算定だけが変わった (諸元に出ない Ke・軸力ごとの曲線)。曲線の照合で分かる。
                    // 諸元表も計算条件の章も出さない設定でも、表紙と警告に出る
                    reopened.DocxOutput.IncludeAssumptions = false;
                    foreach (var key in spring.CaseMThetaSnapshots.Keys.ToList())
                    {
                        var s0 = spring.CaseMThetaSnapshots[key];
                        var old = s0.CurveXY ?? s0.Curve;
                        if (old == null) continue;
                        var scaled = new PileDesign.FEM.MomentRotationCurve();
                        foreach (var (theta, moment) in old.Points) scaled.Points.Add((theta, moment * 1.1));
                        spring.CaseMThetaSnapshots[key] = new PileDesign.FEM.MThetaCaseSnapshot
                        {
                            Mode = s0.Mode, CurveXY = s0.CurveXY == null ? null : scaled, Curve = s0.Curve == null ? null : scaled,
                            KthetaXY = s0.KthetaXY, Ktheta = s0.Ktheta, McrXY = s0.McrXY, AxialN = s0.AxialN, SetupReason = s0.SetupReason,
                        };
                    }
                    var (methodChanged, methodWarnings) = Report(reopened, Path.Combine(dir, "method-changed.docx"));
                    StringAssert.Contains(methodChanged, "水平解析に使った杭頭の M-θ 関係が、いまの計算方法で同じ軸力から求めたものと異なる");
                    Assert.IsTrue(methodWarnings.Any(w => w.StartsWith("杭頭の M-θ 関係:", StringComparison.Ordinal)), "M-θ の違いを警告していません");

                    // 曲線の控えも杭頭諸元の記録も無い旧版の結果: 確かめられないことと版を書く
                    foreach (var s in reopened.CurrentModel.RotationalSprings) s.CaseMThetaSnapshots.Clear();
                    reopenedRecord.PileTopSpecs = [];
                    reopenedRecord.ApplicationVersion = "1.1.0";
                    reopened.DocxOutput.IncludePileTopSpecs = true;
                    var (old11, oldWarnings) = Report(reopened, Path.Combine(dir, "old.docx"));
                    StringAssert.Contains(old11, "解析に使った杭頭の M-θ 関係の記録が無い");
                    StringAssert.Contains(old11, "杭頭諸元を記録していない版 (プログラムの版 1.1.0)");
                    Assert.IsTrue(oldWarnings.Any(w => w.Contains("記録が無い", StringComparison.Ordinal)));
                }
                finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
            }
        }

        private static (string Text, System.Collections.Generic.IReadOnlyList<string> Warnings) Report(MainWindowViewModel vm, string path)
        {
            var input = vm.ResultInputModel ?? vm.CurrentInputModel!;
            var doc = new WordDocument(input, vm.CurrentModel!, vm);
            doc.CreateWordDocument(input, path);
            using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
            return (word.MainDocumentPart!.Document!.Body!.InnerText, doc.Warnings);
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
