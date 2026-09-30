using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TestProject1;

/// <summary>
/// 壊れたファイル・古い形式のファイルの回帰確認。小さなファイル (<c>TestData/BrokenFiles</c>) を実際の読込の経路で開き、
/// 落ちずに、どこを直せばよいか (杭番号・杭体番号・直し方) を説明できることを確かめる。
///
/// <para>ファイルは <c>UPDATE_BROKEN_FIXTURES=1</c> で作り直す (収束の回帰の <c>UPDATE_SNAPSHOTS</c> と同じ流儀)。
/// 既定の新規モデルに杭を 2 本置いたものを保存し、壊し方ごとに 1 か所だけ変える。古い形式のファイルは、
/// 今の版で保存したものから形式の版を 1 に戻し、今の版に無い項目を足し、通り心の一覧を消したもの。</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class BrokenFileTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    private static string Folder => TestSource.Dir("TestProject1", "TestData", "BrokenFiles");

    private static FileOperationService Service() => new(new JsonSerializerOptions
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    });

    /// <summary>fixture を作り直す (UPDATE_BROKEN_FIXTURES=1 のときだけ)。</summary>
    [TestMethod]
    public void RegenerateFixtures()
    {
        if (Environment.GetEnvironmentVariable("UPDATE_BROKEN_FIXTURES") != "1")
        {
            Assert.IsTrue(Directory.GetFiles(Folder, "*.pdjson").Length >= 4, "(前提) 壊れたファイルの fixture がありません。UPDATE_BROKEN_FIXTURES=1 で作ってください");
            return;
        }
        Directory.CreateDirectory(Folder);
        Save("missing-pile-body.pdjson", input => input.PileLayoutItems[1].PileBodyNo = 9);
        Save("missing-ground.pdjson", input => input.PileLayoutItems[1].GroundNo = 7);
        Save("empty-segments.pdjson", input => input.PileBodies[0].PileBodySegments.Clear());
        Save("old-format-v1.pdjson", _ => { }, json =>
        {
            var root = JsonNode.Parse(json)!.AsObject();
            root["FormatVersion"] = 1;
            var model = root["InputModel"]!.AsObject();
            model.Remove("GridXItems");
            model.Add("ObsoleteSetting", 1);
            return root.ToJsonString();
        });
    }

    private static void Save(string name, Action<InputModel> breakIt, Func<string, string>? editJson = null)
    {
        var vm = new MainWindowViewModel();
        var input = vm.CurrentInputModel!;
        input.PileLayoutItems ??= [];
        input.PileLayoutItems.Clear();
        input.PileLayoutItems.Add(new PileLayoutDataItem { No = 1, PileNo = 1, PileBodyNo = 1, GroundNo = 1, X = 0, Y = 0 });
        input.PileLayoutItems.Add(new PileLayoutDataItem { No = 2, PileNo = 2, PileBodyNo = 1, GroundNo = 1, X = 3, Y = 0 });
        breakIt(input);
        string path = Path.Combine(Folder, name);
        Service().SaveProjectData(path, input, null);
        if (editJson != null) File.WriteAllText(path, editJson(File.ReadAllText(path)));
    }

    /// <summary>fixture を実際の読込の経路 (<see cref="MainWindowViewModel.ApplyLoadedProjectData"/>) で開く。</summary>
    private static (MainWindowViewModel Vm, ReferenceIntegrityReport Report) Open(string name)
    {
        string path = Path.Combine(Folder, name);
        if (!File.Exists(path)) Assert.Inconclusive($"{name} がありません (UPDATE_BROKEN_FIXTURES=1 で作ってください)");
        MainWindowViewModel? vm = null;
        var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            vm = new MainWindowViewModel();
            var data = Service().LoadProjectData(path);
            vm.ApplyLoadedProjectData(data, path, "読込が完了しました。");
        }, out bool timedOut);
        if (timedOut) Assert.Inconclusive("読み込みが時間切れになりました");
        Assert.IsNull(captured, $"{name} を開くと落ちます: {captured}");
        return (vm!, ReferenceIntegrity.Check(vm!.CurrentInputModel));
    }

    /// <summary>fixture は小さく保つ (差分を読める大きさ)。</summary>
    [TestMethod]
    public void TheFixturesAreSmall()
    {
        var files = Directory.GetFiles(Folder, "*.pdjson");
        TestSource.AssertScanned(files.Length, 4, "壊れたファイルの fixture");
        foreach (var f in files)
            Assert.IsTrue(new FileInfo(f).Length < 400_000, $"{Path.GetFileName(f)} が大きすぎます ({new FileInfo(f).Length:N0} バイト)");
    }

    /// <summary><b>本題。</b> 無い杭体を指す杭: 開いても落ちず、杭番号・杭体番号と選び直す候補を示し、その杭を選ぶ。</summary>
    [TestMethod]
    public void AMissingPileBody_IsExplainedOnOpen()
    {
        var (vm, report) = Open("missing-pile-body.pdjson");
        var d = report.NeedsReview.Single(x => x.Target.Kind == DiagnosticTargetKind.Pile);
        StringAssert.Contains(d.Message, "杭 No.2: 杭体番号 9 の杭体がありません");
        StringAssert.Contains(d.Message, "選び直す候補: 杭体1");
        Assert.IsTrue(vm.CurrentInputModel!.PileLayoutItems[1].IsSelected, "開いたときに、その杭を選んでいません");
        Assert.AreEqual(9, vm.CurrentInputModel.PileLayoutItems[1].PileBodyNo, "自動で付け替えています (利用者が選ぶこと)");

        // 解析前の検査・グラフ・計算書と同じ取得の経路でも、例外ではなく場所つきの診断になる
        var ex = Assert.ThrowsException<DiagnosticException>(() => vm.CurrentInputModel.RequirePileBody(vm.CurrentInputModel.PileLayoutItems[1]));
        Assert.AreEqual(2, ex.Diagnostics.Single().Target.PileNo);
        Assert.IsTrue(CheckInputData.CollectAnalysisBlockers(vm.CurrentInputModel).Any(p => p.Target.PileNo == 2));
    }

    [TestMethod]
    public void AMissingGround_IsExplainedOnOpen()
    {
        var (_, report) = Open("missing-ground.pdjson");
        StringAssert.Contains(report.NeedsReview.Single().Message, "杭 No.2: 地盤番号 7 の地盤がありません");
        StringAssert.Contains(report.NeedsReview.Single().Message, "選び直す候補: 地盤1");
    }

    [TestMethod]
    public void AnEmptySegmentList_IsExplainedOnOpen()
    {
        var (vm, report) = Open("empty-segments.pdjson");
        Assert.IsTrue(report.NeedsReview.Any(d => d.Target == DiagnosticTarget.PileBody(1) && d.Message.Contains("区間が 1 つもありません")));
        Assert.IsTrue(CheckInputData.CollectAnalysisBlockers(vm.CurrentInputModel!).Any(p => p.Target.Kind == DiagnosticTargetKind.PileBody));
    }

    /// <summary>古い形式: 補った・置き換えた・使わなかった項目を記録し、「入力の診断」にも残す。</summary>
    [TestMethod]
    public void AnOldFormatFile_IsExplainedOnOpen()
    {
        var (vm, report) = Open("old-format-v1.pdjson");
        Assert.IsTrue(report.IsClean, report.Describe());
        var compat = vm.LastLoadCompatibility;
        Assert.IsNotNull(compat, "古い形式の読み込みを記録していません");
        Assert.AreEqual(1, compat.FormatVersion);
        // ファイルに無かった項目は作ったときの既定値で埋まる。黙って埋まるのを「既定値にした」と記録する
        Assert.IsTrue(compat.Entries.Any(e => e.Kind == CompatibilityKind.Filled && e.Text.Contains("「GridXItems」")),
            string.Join(" / ", compat.Entries.Select(e => $"{e.Kind}: {e.Text}")));
        Assert.IsTrue(compat.Entries.Any(e => e.Kind == CompatibilityKind.Converted && e.Text.Contains("v1 → v2")));
        Assert.IsTrue(compat.Entries.Any(e => e.Kind == CompatibilityKind.Unused && e.Text.Contains("ObsoleteSetting")));
        Assert.AreEqual(1, compat.Entries.Count(e => e.Kind == CompatibilityKind.Unused), "今の版で使う項目まで「使わなかった」と言っています: "
            + string.Join(", ", compat.Entries.Where(e => e.Kind == CompatibilityKind.Unused).Select(e => e.Text)));
    }

    /// <summary>今の版で保存した壊れていないファイルは、何も言わない (騒がない)。</summary>
    [TestMethod]
    public void TheFixturesAreOtherwiseClean()
    {
        var (vm, _) = Open("missing-ground.pdjson");
        Assert.IsNull(vm.LastLoadCompatibility, "今の版で保存したファイルなのに、互換の記録を出しています");
    }
}
