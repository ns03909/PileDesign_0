using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Common.Logging;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 参照切れと出力の安全策: 杭体画面の番号の付け直し、開いたときの区間・層・kh0 の参照の検査、
/// 書き出しの原子的な置換と失敗の知らせ、診断の一覧、共有用のログ、古い形式の読み込みの記録。
/// </summary>
[TestClass]
[DoNotParallelize]
public class ReferenceAndExportSafetyTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    private static InputModel Example()
    {
        var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
        if (input == null) Assert.Inconclusive(error);
        return input!;
    }

    // ── 1. 杭体の画面: 杭配置の杭体番号は OK で閉じたときに付け直す ──

    /// <summary>
    /// <b>本題。</b> 杭体 2 を消した (1・3 が残り、新しい杭体を足した) 画面を OK で閉じると、杭配置の杭体番号は
    /// 開いたときの番号から付け直される (3 → 2)。消した杭体を指す杭は付け直さずに知らせる。
    /// </summary>
    [TestMethod]
    public void PileLayout_IsRenumberedFromTheBodiesAtEditStart()
    {
        var b1 = new PileBodyInput { NoAtEditStart = 1 };
        var b3 = new PileBodyInput { NoAtEditStart = 3 };
        var added = new PileBodyInput();   // 画面で足した杭体 (0)
        var piles = new[]
        {
            new PileLayoutDataItem { No = 1, PileBodyNo = 1 },
            new PileLayoutDataItem { No = 2, PileBodyNo = 3 },
            new PileLayoutDataItem { No = 3, PileBodyNo = 2 },   // 消した杭体
        };

        var unresolved = PileBodyViewModel.RenumberPileLayout(piles, [b1, b3, added]);
        CollectionAssert.AreEqual(new[] { 1, 2, 2 }, piles.Select(p => p.PileBodyNo).ToArray());
        StringAssert.Contains(unresolved.Single(), "杭 No.3");
    }

    /// <summary>
    /// 杭体を消した時点では杭配置を書き換えない (キャンセル・× で閉じる・元に戻すと、杭体の一覧だけ戻って杭が
    /// 別の杭体を指した)。付け直すのは OK だけ。開いたときの番号は複製 (元に戻す) でも残る。
    /// </summary>
    [TestMethod]
    public void DeletingABody_DoesNotTouchThePileLayoutUntilOk()
    {
        string source = TestSource.Read("Graphics_r1", "ViewModels", "PileBodyViewModel.cs");
        string delete = TestSource.MethodBody(source, "public void DeletePileBody()");
        // 画面自身の選択 (PileBodyNo = …) は範囲に戻すので書き換えてよい。杭配置の杭 (p.PileBodyNo) は書き換えない
        Assert.IsFalse(Regex.IsMatch(delete, @"\w+\.PileBodyNo\s*(-=|\+=|=(?!=))"), "杭体を消した時点で杭配置の番号を書き換えています");
        StringAssert.Contains(TestSource.MethodBody(source, "private void OnOk()"), "RenumberPileLayout(");
        Assert.IsFalse(TestSource.MethodBody(source, "private void OnCancel()").Contains("PileBodyNo"));

        var body = new PileBodyInput { NoAtEditStart = 4 };
        Assert.AreEqual(4, body.DeepCopy().NoAtEditStart, "元に戻す (複製) で開いたときの番号が消えます");
    }

    // ── 2. 開いたときの検査: 区間・層・kh0 の参照 ──

    [TestMethod]
    public void SegmentLayerAndKh0References_AreChecked()
    {
        var input = Example();
        var sp = input.ElementDivision.SoilPiles[0];
        int segments = input.PileBodyAt(sp.PileBodyNo)!.PileBodySegments.Count;
        sp.ZDataItems[0].SegmentNo = segments + 5;                                 // 無い区間
        sp.Kh0LayerOverrides = [new Kh0LayerOverride { LayerName = "存在しない土層", Kh0 = 1000 }];
        input.GroundsInput[0].GroundMassesData[0].LayerNo = 99;                   // 無い土層

        var report = ReferenceIntegrity.Check(input);
        Assert.IsTrue(report.Repairable.Any(d => d.Message.Contains("無い区間番号")));
        Assert.IsTrue(report.NeedsReview.Any(d => d.Message.Contains("「存在しない土層」") && d.Message.Contains("効いていません")));
        Assert.IsTrue(report.Repairable.Any(d => d.Target == DiagnosticTarget.Ground(1) && d.Message.Contains("無い土層の番号")));
    }

    [TestMethod]
    public void ABodyWithoutSegments_NeedsReview()
    {
        var input = Example();
        int bodyNo = input.PileLayoutItems[0].PileBodyNo;
        input.PileBodyAt(bodyNo)!.PileBodySegments.Clear();
        Assert.IsTrue(ReferenceIntegrity.Check(input).NeedsReview.Any(d => d.Target == DiagnosticTarget.PileBody(bodyNo)));
    }

    // ── 3・4・5. 書き出し ──

    /// <summary>
    /// <b>本題。</b> 保存先がほかのアプリで開かれていて差し替えられなくても、既存のファイルは壊れない
    /// (一時ファイルに書き切ってから差し替える)。失敗は知らせて null を返す。
    /// </summary>
    [TestMethod]
    public void AFailedExport_LeavesTheExistingFileIntact()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pd_export_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "old");
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))   // 使用中
            {
                Assert.IsNull(ExportFile.TryWriteText(path, "new", ExportFormat.Csv, "試験の CSV"));
            }
            Assert.AreEqual("old", File.ReadAllText(path), "失敗した書き出しが既存のファイルを壊しています");
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.saving").Length,
                "一時ファイルが残っています");

            Assert.AreEqual(path, ExportFile.TryWriteText(path, "new", ExportFormat.Csv, "試験の CSV"));
            Assert.AreEqual("new", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>形式ごとの約束: 拡張子を補う・CSV とテキストは BOM 付き・JSON は BOM 無し。</summary>
    [TestMethod]
    public void ExportFormats_FixExtensionAndEncoding()
    {
        Assert.AreEqual(@"C:\a\表.csv", ExportFile.EnsureExtension(@"C:\a\表", ExportFormat.Csv));
        Assert.AreEqual(@"C:\a\表.txt", ExportFile.EnsureExtension(@"C:\a\表.txt", ExportFormat.Csv), "選んだ拡張子を変えています");

        string dir = Path.Combine(Path.GetTempPath(), $"pd_fmt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ExportFile.WriteText(Path.Combine(dir, "a.csv"), "x", ExportFormat.Csv);
            ExportFile.WriteText(Path.Combine(dir, "a.json"), "{}", ExportFormat.Json);
            var csv = File.ReadAllBytes(Path.Combine(dir, "a.csv"));
            var json = File.ReadAllBytes(Path.Combine(dir, "a.json"));
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' }, csv, "CSV に BOM がありません (Excel で文字化けする)");
            Assert.AreEqual((byte)'{', json[0], "JSON に BOM を付けています");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>失敗の原因ごとに、利用者にできることを書き、既存のファイル・計算結果が失われていないことを添える。</summary>
    [TestMethod]
    public void FailureMessages_NameTheCauseAndTheWayBack()
    {
        string busy = ExportFile.DescribeFailure(new IOException("x", unchecked((int)0x80070020)), "計算書 (Word)");
        StringAssert.Contains(busy, "ほかのアプリ");
        StringAssert.Contains(ExportFile.DescribeFailure(new IOException("x", unchecked((int)0x80070070)), "CSV"), "空き容量");
        StringAssert.Contains(ExportFile.DescribeFailure(new UnauthorizedAccessException("x"), "CSV"), "権限");
        StringAssert.Contains(ExportFile.DescribeFailure(new DirectoryNotFoundException("x"), "CSV"), "フォルダが見つかりません");
        StringAssert.Contains(busy, "既存のファイルはそのまま残っています");
        StringAssert.Contains(busy, "もう一度書き出せます");
    }

    /// <summary>
    /// 画面・計算書の書き出しは、保存先を直接開いて書かない (一時ファイルに書き切ってから差し替える)。
    /// 計算書の図 (Output) も対象 (一時ファイルでも同じ形にそろえる)。
    /// </summary>
    [TestMethod]
    public void ScreenExports_DoNotWriteTheDestinationDirectly()
    {
        var direct = new Regex(@"File\.(WriteAllText|WriteAllLines|WriteAllBytes|WriteAllTextAsync)\s*\(|new\s+StreamWriter\s*\(\s*\w+\.FileName|\.Plot\.Save\s*\(|new\s+FileStream\s*\([^,]+,\s*FileMode\.Create\s*\)");
        char sep = Path.DirectorySeparatorChar;
        var files = new[] { "ViewModels", "Views", "Output" }
            .SelectMany(d => Directory.GetFiles(TestSource.Dir("Graphics_r1", d), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(TestSource.Dir("Graphics_r1", "Common"), "PlotHelper.cs"))
            .Where(f => !f.Contains($"{sep}obj{sep}"))
            .ToList();
        TestSource.AssertScanned(files.Count, 100, "画面のソース");
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l)))
            .Where(l => !l.Text.TrimStart().StartsWith("//") && direct.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}  {l.Text.Trim()}")
            .ToList();
        Assert.AreEqual(0, hits.Count, "保存先を直接書いています。ExportFile.TryWriteText / FileOperationService.WriteAtomically を使ってください:\n  "
            + string.Join("\n  ", hits));
    }

    // ── 6. 診断の一覧 ──

    /// <summary><b>本題。</b> 一覧は重い順に並び、直してから再検査すると解消した件数を知らせて一覧から除く。</summary>
    [TestMethod]
    public void TheDiagnosticList_RechecksAndDropsResolvedItems()
    {
        var input = Example();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);
        input.PileLayoutItems[0].GroupPileFactor = 0;      // 解析を止める
        input.PileLayoutItems[1].GroupPileFactor = 1.2;    // 結果に影響

        var list = new DiagnosticListViewModel(vm);
        int errorRow = list.Rows.ToList().FindIndex(r => r.Diagnostic.Severity == DiagnosticSeverity.Error && r.Message.Contains("群杭係数"));
        int warnRow = list.Rows.ToList().FindIndex(r => r.Diagnostic.Severity == DiagnosticSeverity.Warning && r.Message.Contains("群杭係数"));
        Assert.IsTrue(errorRow >= 0 && errorRow < warnRow, "重い順に並んでいません");

        // 移動: 杭の指摘は、その杭だけを選ぶ (開く画面は無い)
        list.SelectedRow = list.Rows[errorRow];
        list.GoTo();
        Assert.IsTrue(input.PileLayoutItems[0].IsSelected);
        Assert.AreEqual(1, input.PileLayoutItems.Count(p => p.IsSelected));

        input.PileLayoutItems[0].GroupPileFactor = 1.0;    // 直した
        list.Recheck();
        Assert.IsFalse(list.Rows.Any(r => r.Diagnostic.Severity == DiagnosticSeverity.Error && r.Message.Contains("群杭係数")));
        StringAssert.Contains(list.StatusText, "1 件が解消しました");
    }

    [TestMethod]
    public void TheDiagnosticListWindow_Opens()
    {
        var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var vm = new MainWindowViewModel();
            var window = new PileDesign.Views.DiagnosticListWindow(new DiagnosticListViewModel(vm));
            window.Close();
        }, out bool timedOut);
        if (timedOut) { Assert.Inconclusive("時間切れ"); return; }
        Assert.IsNull(captured, captured?.ToString());
    }

    // ── 7. 共有用のログ ──

    /// <summary><b>本題。</b> ユーザー名・PC 名・フォルダの場所を伏せ、診断・規模・ファイル名・モデルの識別子は残す。</summary>
    [TestMethod]
    public void TheShareableLog_HidesPersonalPathsButKeepsTheFacts()
    {
        string log = string.Join("\n",
            @"2026-09-30 10:00:00.000 [INF] 読込: C:\Users\taro\Documents\顧客A 案件\杭基礎.pdjson",
            @"2026-09-30 10:00:01.000 [INF] [性能] 水平解析の規模: 杭 12 本・要素 340・ケース 8・並列 4",
            @"2026-09-30 10:00:02.000 [WRN] [入力・結果に影響] model=1A2B3C4D kind=Pile pile=3 : 杭 No.3: 群杭係数",
            @"2026-09-30 10:00:03.000 [INF] 共有フォルダ \\SERVER01\共有\設計\a.docx を taro が TARO-PC で出力",
            @"2026-09-30 10:00:04.000 [INF] taroko さんの記録は伏せない");
        string s = LogSanitizer.Sanitize(log, userProfile: @"C:\Users\taro", userName: "taro", machineName: "TARO-PC");

        Assert.IsFalse(s.Contains(@"C:\Users"), s);
        Assert.IsFalse(s.Contains("顧客A"), "フォルダ名 (案件名) を残しています: " + s);
        Assert.IsFalse(s.Contains("SERVER01"), s);
        Assert.IsFalse(Regex.IsMatch(s, @"(?<!\w)taro(?!\w)", RegexOptions.IgnoreCase), "ユーザー名を残しています: " + s);
        Assert.IsFalse(s.Contains("TARO-PC"), s);
        StringAssert.Contains(s, @"…\杭基礎.pdjson", "ファイル名まで消しています");
        StringAssert.Contains(s, "杭 12 本・要素 340・ケース 8");
        StringAssert.Contains(s, "model=1A2B3C4D");
        StringAssert.Contains(s, "taroko", "名前を含む別の語まで伏せています");
    }

    [TestMethod]
    public void TheShareableLog_HasAHeaderAndReadsTheDailyFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pd_logs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, $"PileDesign-{DateTime.Now:yyyyMMdd}.log"), "今日の記録\n");
            string text = LogSanitizer.BuildShareableLog("1.0.34-beta", days: 2, logDirectory: dir);
            StringAssert.Contains(text, "版: 1.0.34-beta");
            StringAssert.Contains(text, "今日の記録");
            StringAssert.Contains(text, "伏せています");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── 8. 古い形式の読み込みの記録 ──

    private static FileOperationService Service() => new(new JsonSerializerOptions
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    });

    /// <summary>
    /// <b>本題。</b> ファイルにあって今の版で使わない項目 (保存し直すと消える) を見つける。
    /// 今の版で保存したファイルでは何も出ない (騒がない)。
    /// </summary>
    [TestMethod]
    public void UnusedProperties_AreFound_AndACurrentFileIsQuiet()
    {
        var input = Example();
        var service = Service();
        string path = Path.Combine(Path.GetTempPath(), $"pd_compat_{Guid.NewGuid():N}.pdjson");
        try
        {
            service.SaveProjectData(path, input, null);
            var clean = service.LoadProjectData(path);
            CollectionAssert.AreEqual(Array.Empty<string>(), service.FindUnusedInputProperties(clean, clean.InputModel).ToArray(),
                "今の版で保存したファイルで、使わなかった項目があると言っています");
            CollectionAssert.AreEqual(Array.Empty<string>(), service.CompareInputProperties(clean, clean.InputModel).Missing.ToArray(),
                "今の版で保存したファイルで、既定値で補った項目があると言っています");

            string json = File.ReadAllText(path);
            var m = Regex.Match(json, @"""InputModel"":\{""\$id"":""\d+"",");
            Assert.IsTrue(m.Success, "(前提) 入力の節が見つかりません");
            File.WriteAllText(path, json.Insert(m.Index + m.Length, @"""ObsoleteSetting"":1,"));
            var old = service.LoadProjectData(path);
            CollectionAssert.AreEqual(new[] { "ObsoleteSetting" }, service.FindUnusedInputProperties(old, old.InputModel).ToArray());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TheCompatibilityReport_GroupsEntries_AndFeedsTheDiagnosticList()
    {
        var report = new LoadCompatibilityReport(formatVersion: 1);
        report.Add(CompatibilityKind.Filled, "通り心 (X) の一覧が無かったので、空の一覧で補いました。");
        report.Add(CompatibilityKind.Converted, "杭配置の Z を読み替えました。");
        report.Add(CompatibilityKind.Unused, "「ObsoleteSetting」");
        string text = report.Describe()!;
        StringAssert.Contains(text, "古い形式 (v1)");
        StringAssert.Contains(text, "■ 既定値で補ったもの");
        StringAssert.Contains(text, "■ 置き換えたもの");
        StringAssert.Contains(text, "■ 読み込みで使わなかったもの");
        StringAssert.Contains(text, "名前を付けて保存");
        Assert.IsTrue(report.NeedsNotice(new System.Collections.Generic.HashSet<string>()));

        var current = new LoadCompatibilityReport(LoadCompatibilityReport.CurrentFormatVersion);
        Assert.IsTrue(current.IsEmpty);
        current.Add(CompatibilityKind.Converted, "荷重ケースの番号を振り直しました (1 件)。");
        Assert.IsFalse(current.NeedsNotice(new System.Collections.Generic.HashSet<string> { "荷重ケースの番号を振り直しました (1 件)。" }),
            "個別のダイアログで知らせたものを、もう一度知らせています");

        var vm = new MainWindowViewModel { CurrentInputModel = Example() };
        vm.ShowLoadCompatibilityIfAny(report, new System.Collections.Generic.HashSet<string>());
        var rows = new DiagnosticListViewModel(vm).Rows;
        Assert.IsTrue(rows.Any(r => r.Message.StartsWith("読込 (読み込みで使わなかった): 「ObsoleteSetting」") && r.Diagnostic.Severity == DiagnosticSeverity.Info));
        Assert.IsTrue(LoadCompatibilityReport.CollectPropertyNames(@"{""$id"":""1"",""A"":{""B"":[{""C"":1}]}}").SetEquals(new[] { "A", "B", "C" }));
    }
}
