using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 番号の参照の安全策: 杭体・地盤・土層-杭セットを番号から引く共通の処理、ファイルを開いたときの検査、
/// 診断から開く入力画面の対象の照合、重さと推奨する操作、再解析が要る理由、ログのモデルの識別子。
/// 番号の振り直し・共有・削除のあとも、診断が正しいものを指し続けることを確かめる。
/// </summary>
[TestClass]
[DoNotParallelize]
public class ReferenceIntegrityTests
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

    // ── 1. 番号から引く共通の処理 ──

    /// <summary><b>本題。</b> 範囲外の番号は例外で落ちず、杭番号・杭体番号を持った診断になる。</summary>
    [TestMethod]
    public void AMissingPileBody_BecomesADiagnosticWithBothNumbers()
    {
        var input = Example();
        var pile = input.PileLayoutItems[0];
        pile.PileBodyNo = 99;

        Assert.IsNull(input.PileBodyAt(99));
        Assert.IsNull(input.PileBodyAt(0));
        var ex = Assert.ThrowsException<DiagnosticException>(() => input.RequirePileBody(pile));
        var d = ex.Diagnostics.Single();
        Assert.AreEqual(DiagnosticTarget.Pile(pile.No, 99), d.Target);
        StringAssert.Contains(d.Message, $"杭 No.{pile.No}");
        StringAssert.Contains(d.Message, "杭体番号 99");

        pile.SoilPileAltNo = 0;
        var soil = Assert.ThrowsException<DiagnosticException>(() => input.RequireSoilPile(pile));
        Assert.AreEqual(pile.No, soil.Diagnostics.Single().Target.PileNo);
    }

    /// <summary>
    /// 一覧を別の物の番号でじかに引かない (<c>PileBodies[pile.PileBodyNo - 1]</c> の形)。
    /// 範囲外で「インデックスが範囲外です」とだけ出て、どの杭かが分からなかった。<c>…At</c> / <c>Require…</c> を通す。
    /// 杭体の画面が自分で持つ一覧を自分の選択番号で引くところだけは対象外。
    /// </summary>
    [TestMethod]
    public void NoListIsIndexedByAnotherObjectsNumber()
    {
        var raw = new Regex(@"\[\s*[A-Za-z_][A-Za-z0-9_.]*\.(PileBodyNo|GroundNo|SoilPileAltNo)\s*-\s*1\s*\]");
        char sep = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        TestSource.AssertScanned(files.Count, 300, "アプリのソース");
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l)))
            .Where(l => !l.Text.TrimStart().StartsWith("//") && raw.IsMatch(l.Text))
            .Where(l => !(l.File == "PileBodyWindow.xaml.cs" && l.Text.Contains("viewModel.PileBodies[viewModel.PileBodyNo - 1]")))
            .Select(l => $"{l.File}:{l.Line}  {l.Text.Trim()}")
            .ToList();
        Assert.AreEqual(0, hits.Count, "番号で一覧をじかに引いています。InputModel.PileBodyAt / RequirePileBody などを使ってください:\n  "
            + string.Join("\n  ", hits));
    }

    // ── 5. 図表の出力 ──

    /// <summary>計算書で図表を省いたとき、理由が場所つきの問題なら、その文 (杭番号・杭体番号) を注記に出す。</summary>
    [TestMethod]
    public void AnOmittedFigure_ShowsTheLocatedReason()
    {
        var located = new InvalidOperationException("図を作れません",
            new DiagnosticException(Diagnostic.Input(DiagnosticTarget.Pile(3, 7), "杭 No.3: 杭体番号 7 の杭体がありません。")));
        StringAssert.Contains(WordDocument.DescribeOmissionReason(located), "杭 No.3: 杭体番号 7");
        Assert.IsNull(WordDocument.DescribeOmissionReason(new NullReferenceException("実装の都合の文")),
            "場所の無い例外の文 (実装の都合) を計算書に出しています");
    }

    /// <summary>グラフで参照の切れた杭を飛ばしたときは、飛ばした杭を知らせる (空欄が正常な結果に見えないように)。</summary>
    [TestMethod]
    public void SkippedPilesInAGraph_AreListed()
    {
        Assert.IsNull(GraphViewModel.DescribeMissingReferences([]));
        StringAssert.Contains(GraphViewModel.DescribeMissingReferences(["杭 No.4 (杭体番号 9 の杭体がありません)"]),
            "杭 No.4 (杭体番号 9");
    }

    // ── 2. ファイルを開いた時点の検査 ──

    /// <summary>同梱の例題には、番号の参照の食い違いが無いこと (正しいファイルで騒がない)。</summary>
    [TestMethod]
    public void BundledExamples_HaveConsistentReferences()
    {
        int checkedFiles = 0;
        foreach (var file in TestSource.ExampleFiles("PileExample*.json", 10))
        {
            string pileName = Path.GetFileNameWithoutExtension(file);
            string groundName = "Example" + pileName["PileExample".Length..];
            if (TestSource.ExamplePath(groundName + ".json") == null) continue;
            var (input, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            Assert.IsNotNull(input, $"{pileName}: {error}");
            checkedFiles++;
            var report = ReferenceIntegrity.Check(input);
            Assert.IsTrue(report.IsClean, $"{pileName}: {report.Describe()}");
        }
        TestSource.AssertScanned(checkedFiles, 10, "杭の例題");
    }

    /// <summary>
    /// <b>本題。</b> 選び直しが必要なもの (杭が指す杭体・地盤が無い) と、要素分割をやり直せば直るもの
    /// (土層-杭セットの食い違い) を分けて知らせる。データは書き換えない。
    /// </summary>
    [TestMethod]
    public void BrokenReferences_AreSplitIntoReviewAndRepairable()
    {
        var input = Example();
        var piles = input.PileLayoutItems;
        piles[0].PileBodyNo = 99;                                   // 選び直し
        input.ElementDivision.SoilPiles[0].PileBodyNo = 98;         // 作り直せば直る (セット側)
        int altBefore = piles[1].SoilPileAltNo;
        piles[1].SoilPileAltNo = 500;                               // 作り直せば直る (対応が無い)

        var report = ReferenceIntegrity.Check(input);
        Assert.IsTrue(report.NeedsReview.Any(d => d.Target == DiagnosticTarget.Pile(piles[0].No, 99)));
        Assert.IsTrue(report.Repairable.Any(d => d.Message.Contains("土層-杭セット 1: 杭体番号 98")));
        Assert.IsTrue(report.Repairable.Any(d => d.Target.PileNo == piles[1].No && d.Message.Contains("対応がありません")));
        string text = report.Describe()!;
        StringAssert.Contains(text, "選び直しが必要なもの");
        StringAssert.Contains(text, "作り直せば直るもの");
        Assert.AreEqual(500, piles[1].SoilPileAltNo, "検査がデータを書き換えています");
        piles[1].SoilPileAltNo = altBefore;
    }

    /// <summary>ファイルを開いたときの知らせは、関係する杭を選ぶ。</summary>
    [TestMethod]
    public void OpeningAFile_SelectsThePilesWithBrokenReferences()
    {
        var input = Example();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);
        input.PileLayoutItems[2].PileBodyNo = 99;

        var report = vm.ShowReferenceProblemsIfAny();
        Assert.IsFalse(report.IsClean);
        Assert.IsTrue(input.PileLayoutItems[2].IsSelected);
        Assert.AreEqual(1, input.PileLayoutItems.Count(p => p.IsSelected), "参照の切れていない杭まで選んでいます");
        StringAssert.Contains(TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
            "private void ApplyPostLoadProtocol("), "ShowReferenceProblemsIfAny();", "ファイルを開いたときに検査していません");
    }

    // ── 3・7. 診断から開く画面の対象の照合 (番号の変更・削除・共有) ──

    /// <summary>
    /// <b>本題。</b> 診断のあとに杭体を足して番号がずれたら、同じ番号でも別の杭体を指すと判定する
    /// (違うものを選んで直させない)。番号が同じ実体を指したままなら同じと判定する。
    /// </summary>
    [TestMethod]
    public void ANavigationTarget_IsRecheckedAfterBodiesAreInsertedOrDeleted()
    {
        var vm = new MainWindowViewModel();
        var bodies = vm.CurrentInputModel!.PileBodies;
        bodies.Add(bodies[0].DeepCopy());
        var target = DiagnosticTarget.PileBodySegment(2, 1);
        var subject = vm.SubjectOf(target);
        Assert.IsNotNull(subject);
        Assert.IsTrue(vm.IsSameSubject(target, subject));

        bodies.Insert(0, bodies[0].DeepCopy());      // 杭体を先頭に足した: 「杭体2」は別の杭体になる
        Assert.IsFalse(vm.IsSameSubject(target, subject), "番号がずれたのに同じ対象とみなしています");

        bodies.RemoveAt(0);                           // 元に戻すと同じ実体を指す
        Assert.IsTrue(vm.IsSameSubject(target, subject));

        bodies.RemoveAt(1);                           // 対象を消した
        Assert.IsFalse(vm.IsSameSubject(target, subject), "消した対象を同じとみなしています");
        StringAssert.Contains(MainWindowViewModel.DescribeChangedSubject(target), "選び直して");
    }

    /// <summary>頼んでから開くまでに対象が変わったら、頼みは消え、選ぶ場所も残さない。</summary>
    [TestMethod]
    public void AChangedTarget_IsNotFocused()
    {
        var vm = new MainWindowViewModel();
        vm.RequestInputNavigation(DiagnosticTarget.Pile(1));   // 開く画面の無い場所 (画面は開かない)
        vm.OpenPendingInputNavigation();
        Assert.IsNull(vm.PendingInputNavigation);
        Assert.IsNull(vm.InputFocus);
    }

    /// <summary>
    /// 杭番号を振り直したあとに集めた診断は、振り直したあとの番号で正しい杭を選ぶ (診断は番号を文から拾わない)。
    /// </summary>
    [TestMethod]
    public void DiagnosticsAfterRenumbering_SelectTheRightPile()
    {
        var input = Example();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);
        var victim = input.PileLayoutItems[2];
        input.PileLayoutItems.RemoveAt(0);
        AnalysisModelling.EnsurePileNumbersSequential(input);   // 杭番号を振り直す (No.3 → No.2)
        victim.GroupPileFactor = 0;

        var problems = CheckInputData.CollectAnalysisBlockers(input);
        var selection = DiagnosticSelection.Resolve(problems.Where(p => p.Message.Contains("群杭係数")), input);
        CollectionAssert.AreEqual(new[] { victim }, selection.Piles.ToArray(), "振り直したあとの番号で別の杭を選んでいます");
        StringAssert.Contains(selection.Scopes.Single(), $"杭 No.{victim.No}");
    }

    /// <summary>杭体・地盤を共有する杭: 杭体の問題はその杭体を使う杭すべて、地盤の問題はその地盤を使う杭すべてを選ぶ。</summary>
    [TestMethod]
    public void SharedBodiesAndGrounds_SelectEveryUser()
    {
        var input = Example();
        var byBody = input.PileLayoutItems.GroupBy(p => p.PileBodyNo).OrderByDescending(g => g.Count()).First();
        var byGround = input.PileLayoutItems.GroupBy(p => p.GroundNo).OrderByDescending(g => g.Count()).First();
        if (byBody.Count() < 2) { Assert.Inconclusive("(前提) 杭体を共有する杭がありません"); return; }

        var bodySel = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.PileBodySegment(byBody.Key, 1), "x")], input);
        CollectionAssert.AreEquivalent(byBody.ToArray(), bodySel.Piles.ToArray());
        var groundSel = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.GroundLayer(byGround.Key, 1), "x")], input);
        CollectionAssert.AreEquivalent(byGround.ToArray(), groundSel.Piles.ToArray());

        // 1 本の杭体を別の杭体に替えると、その杭は杭体の問題の範囲から外れる
        var moved = byBody.First();
        moved.PileBodyNo = byBody.Key == 1 ? 2 : 1;
        var after = DiagnosticSelection.Resolve([Diagnostic.InputAt(DiagnosticTarget.PileBody(byBody.Key), "x")], input);
        Assert.IsFalse(after.Piles.Contains(moved));
    }

    // ── 4. 重さと推奨する操作 ──

    [TestMethod]
    public void Warnings_CarrySeverityAndRemedy_AndAreOrderedBySeverity()
    {
        var input = Example();
        input.PileLayoutItems[0].GroupPileFactor = 1.2;                       // 結果に影響
        input.InputNodes.Add(new InputNode { No = 99, X = 50, Y = 50, Z = 0 }); // 情報 (解析では無視)

        var warnings = CheckInputData.CollectInputWarningDiagnostics(input);
        var xi = warnings.Single(w => w.Message.Contains("群杭係数 ξ"));
        Assert.AreEqual(DiagnosticSeverity.Warning, xi.Severity);
        Assert.AreEqual(DiagnosticTarget.Pile(input.PileLayoutItems[0].No), xi.Target);
        var node = warnings.Single(w => w.Message.Contains("一般節点 No.99"));
        Assert.AreEqual(DiagnosticSeverity.Info, node.Severity);

        var lines = CheckInputData.DescribeInputWarnings(input);
        int xiLine = lines.FindIndex(l => l.Contains("群杭係数 ξ"));
        int nodeLine = lines.FindIndex(l => l.Contains("一般節点 No.99"));
        Assert.IsTrue(xiLine >= 0 && xiLine < nodeLine, "重い順に並んでいません");
        StringAssert.StartsWith(lines[xiLine], "【結果に影響】");
        StringAssert.Contains(lines[xiLine], "→ その杭を選んで「選択杭の一括変換」で直す");
        StringAssert.StartsWith(lines[nodeLine], "【情報】");

        // 文だけを返す従来の形は変えない
        CollectionAssert.Contains(CheckInputData.CollectInputWarnings(input), xi.Message);
    }

    [TestMethod]
    public void Blockers_AreSummarisedByRemedy()
    {
        var text = DiagnosticSelection.DescribeRemedies([
            Diagnostic.InputAt(DiagnosticTarget.PileBodySegment(1, 1), "a"),
            Diagnostic.InputAt(DiagnosticTarget.PileBodySegment(2, 1), "b"),
            Diagnostic.InputAt(DiagnosticTarget.GroundLayer(1, 2), "c"),
        ]);
        StringAssert.Contains(text, "・杭体の入力画面で直す (2 件)");
        StringAssert.Contains(text, "・地盤の入力画面で直す (1 件)");
        Assert.IsNull(DiagnosticSelection.DescribeRemedies([Diagnostic.Input(DiagnosticTarget.Nowhere, "場所不明")]));
    }

    // ── 6. 再解析が要る理由 ──

    [TestMethod]
    public void TheStatus_NamesTheEditsBehindEachRerun()
    {
        string text = MainWindowViewModel.BuildResultSetStatusText("2026-09-30 10:00", horizontalStale: true, settlementStale: true,
            materialOptionsChanged: false, horizontalEdits: ["杭体 編集", "杭 追加"], settlementEdits: ["矩形荷重 編集"]);
        StringAssert.Contains(text, "水平解析に効く変更: 杭体 編集・杭 追加");
        StringAssert.Contains(text, "沈下解析に効く変更: 矩形荷重 編集");

        // 陳腐化していない解析については書かない
        string fresh = MainWindowViewModel.BuildResultSetStatusText("2026-09-30 10:00", horizontalStale: false, settlementStale: false,
            materialOptionsChanged: false, horizontalEdits: ["杭体 編集"], settlementEdits: ["矩形荷重 編集"]);
        Assert.IsFalse(fresh.Contains("効く変更"));
    }

    /// <summary>編集した項目は効く解析ごとに控え、その解析の結果が最新に戻ったら消す。</summary>
    [TestMethod]
    public void EditedItems_AreKeptPerAnalysis_AndClearedWhenCurrent()
    {
        var input = Example();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.AttachViewModel(vm);
        var modelling = new AnalysisModelling(input);
        vm.CurrentModel = new AnaModel(input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
            modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
        vm.IsHorizontalAnalysisDone = true;
        vm.CaptureAnalysisResultSet();

        vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.All, "杭体 編集");
        vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement, "矩形荷重 編集");
        CollectionAssert.AreEqual(new[] { "杭体 編集" }, vm.EditsSinceHorizontal.ToArray(), "沈下だけの編集を水平解析の理由にしています");
        CollectionAssert.AreEqual(new[] { "杭体 編集", "矩形荷重 編集" }, vm.EditsSinceSettlement.ToArray());
        StringAssert.Contains(vm.ResultSetStatusText, "水平解析に効く変更: 杭体 編集");

        vm.RestoreInputChangedSinceAnalysis(false);   // 解析をやり直した (水平解析が最新)
        Assert.AreEqual(0, vm.EditsSinceHorizontal.Count);
        vm.MarkSettlementResultsCurrent();
        Assert.AreEqual(0, vm.EditsSinceSettlement.Count);
    }

    // ── 8. ログのモデルの識別子 ──

    [TestMethod]
    public void TheModelIdentity_IsStableAndHidesThePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "顧客A", "案件.pdjson");
        string id = ModelIdentity.Of(path);
        Assert.AreEqual(8, id.Length);
        StringAssert.Matches(id, new Regex("^[0-9A-F]{8}$"));
        Assert.AreEqual(id, ModelIdentity.Of(path.ToLowerInvariant()), "大文字小文字で別のモデルになっています");
        Assert.AreNotEqual(id, ModelIdentity.Of(path + "2"));
        Assert.AreEqual(ModelIdentity.Unsaved, ModelIdentity.Of(null));

        var vm = new MainWindowViewModel { CurrentFilePath = path };
        Assert.AreEqual(id, ModelIdentity.Current);
        StringAssert.Contains(Diagnostic.Input(DiagnosticTarget.Pile(1), "x").ToLogLine(), $"model={id}");
        vm.CurrentFilePath = null;
        Assert.AreEqual(ModelIdentity.Unsaved, ModelIdentity.Current);
    }
}
